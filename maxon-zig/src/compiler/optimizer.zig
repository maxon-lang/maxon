const std = @import("std");
const ir = @import("ir.zig");

/// Run all optimizer passes
pub fn optimize(module: *ir.Module, allocator: std.mem.Allocator) !void {
    for (module.functions.items) |*func| {
        try constantFolding(func, allocator);
        try copyPropagation(func, allocator);
        try deadStoreElimination(func, allocator);
        try deadCodeElimination(func, allocator);
    }
}

/// Constant folding: evaluate constant expressions at compile time
fn constantFolding(func: *ir.Function, allocator: std.mem.Allocator) !void {
    var constants = std.AutoHashMapUnmanaged(ir.Value, i64){};
    defer constants.deinit(allocator);

    for (func.blocks.items) |*block| {
        var new_instructions: std.ArrayListUnmanaged(ir.Instruction) = .empty;
        defer new_instructions.deinit(allocator);

        for (block.instructions.items) |inst| {
            const folded = try tryFold(inst, &constants, allocator);
            if (folded) |new_inst| {
                try new_instructions.append(allocator, new_inst);
            } else {
                try new_instructions.append(allocator, inst);
            }
        }

        block.instructions.deinit(func.allocator);
        block.instructions = .empty;
        for (new_instructions.items) |inst| {
            try block.instructions.append(func.allocator, inst);
        }
    }
}

fn tryFold(inst: ir.Instruction, constants: *std.AutoHashMapUnmanaged(ir.Value, i64), allocator: std.mem.Allocator) !?ir.Instruction {
    switch (inst.op) {
        .const_i64 => {
            if (inst.result) |result| {
                try constants.put(allocator, result, inst.operands[0].immediate_i64);
            }
            return null;
        },
        .add, .sub, .mul, .div, .mod => {
            const lhs_val = inst.operands[0].value;
            const rhs_val = inst.operands[1].value;
            const lhs = constants.get(lhs_val);
            const rhs = constants.get(rhs_val);

            if (lhs != null and rhs != null) {
                const result_val = switch (inst.op) {
                    .add => lhs.? + rhs.?,
                    .sub => lhs.? - rhs.?,
                    .mul => lhs.? * rhs.?,
                    .div => @divTrunc(lhs.?, rhs.?),
                    .mod => @mod(lhs.?, rhs.?),
                    else => unreachable,
                };

                if (inst.result) |result| {
                    try constants.put(allocator, result, result_val);
                }

                return .{
                    .op = .const_i64,
                    .result = inst.result,
                    .result_type = .i64,
                    .operands = .{ .{ .immediate_i64 = result_val }, .none },
                };
            }
            return null;
        },
        else => return null,
    }
}

/// Copy propagation: replace loads with the value that was stored
fn copyPropagation(func: *ir.Function, allocator: std.mem.Allocator) !void {
    // Track what value is stored at each alloca ptr
    var ptr_to_value = std.AutoHashMapUnmanaged(ir.Value, ir.Value){};
    defer ptr_to_value.deinit(allocator);

    for (func.blocks.items) |*block| {
        var new_instructions: std.ArrayListUnmanaged(ir.Instruction) = .empty;
        defer new_instructions.deinit(allocator);

        for (block.instructions.items) |inst| {
            switch (inst.op) {
                .store => {
                    const ptr = inst.operands[0].value;
                    const val = inst.operands[1].value;
                    try ptr_to_value.put(allocator, ptr, val);
                    try new_instructions.append(allocator, inst);
                },
                .load => {
                    const ptr = inst.operands[0].value;
                    if (ptr_to_value.get(ptr)) |stored_val| {
                        // Replace load with a copy of the stored value
                        if (inst.result) |result| {
                            // Emit a "copy" by just mapping load result to stored value
                            try ptr_to_value.put(allocator, result, stored_val);
                        }
                        // Don't emit the load - we'll propagate the value
                    } else {
                        try new_instructions.append(allocator, inst);
                    }
                },
                .ret => {
                    // Rewrite ret to use propagated value if available
                    var new_inst = inst;
                    if (inst.operands[0] == .value) {
                        if (ptr_to_value.get(inst.operands[0].value)) |propagated| {
                            new_inst.operands[0] = .{ .value = propagated };
                        }
                    }
                    try new_instructions.append(allocator, new_inst);
                },
                else => try new_instructions.append(allocator, inst),
            }
        }

        block.instructions.deinit(func.allocator);
        block.instructions = .empty;
        for (new_instructions.items) |inst| {
            try block.instructions.append(func.allocator, inst);
        }
    }
}

/// Dead store elimination: remove stores to allocas that are never loaded
fn deadStoreElimination(func: *ir.Function, allocator: std.mem.Allocator) !void {
    // Find all pointers that are loaded
    var loaded_ptrs = std.AutoHashMapUnmanaged(ir.Value, void){};
    defer loaded_ptrs.deinit(allocator);

    for (func.blocks.items) |block| {
        for (block.instructions.items) |inst| {
            if (inst.op == .load) {
                try loaded_ptrs.put(allocator, inst.operands[0].value, {});
            }
        }
    }

    // Remove stores to never-loaded pointers
    for (func.blocks.items) |*block| {
        var new_instructions: std.ArrayListUnmanaged(ir.Instruction) = .empty;
        defer new_instructions.deinit(allocator);

        for (block.instructions.items) |inst| {
            if (inst.op == .store) {
                const ptr = inst.operands[0].value;
                if (!loaded_ptrs.contains(ptr)) {
                    continue; // Skip dead store
                }
            }
            try new_instructions.append(allocator, inst);
        }

        block.instructions.deinit(func.allocator);
        block.instructions = .empty;
        for (new_instructions.items) |inst| {
            try block.instructions.append(func.allocator, inst);
        }
    }
}

/// Dead code elimination: remove instructions whose results are never used
fn deadCodeElimination(func: *ir.Function, allocator: std.mem.Allocator) !void {
    var used = std.AutoHashMapUnmanaged(ir.Value, void){};
    defer used.deinit(allocator);

    // First pass: find all used values
    for (func.blocks.items) |block| {
        for (block.instructions.items) |inst| {
            for (inst.operands) |op| {
                switch (op) {
                    .value => |v| try used.put(allocator, v, {}),
                    else => {},
                }
            }
        }
    }

    // Second pass: remove dead instructions
    for (func.blocks.items) |*block| {
        var new_instructions: std.ArrayListUnmanaged(ir.Instruction) = .empty;
        defer new_instructions.deinit(allocator);

        for (block.instructions.items) |inst| {
            if (!isDead(inst, &used)) {
                try new_instructions.append(allocator, inst);
            }
        }

        block.instructions.deinit(func.allocator);
        block.instructions = .empty;
        for (new_instructions.items) |inst| {
            try block.instructions.append(func.allocator, inst);
        }
    }
}

fn isDead(inst: ir.Instruction, used: *std.AutoHashMapUnmanaged(ir.Value, void)) bool {
    switch (inst.op) {
        .store, .ret, .br, .br_cond, .call => return false,
        else => {},
    }

    if (inst.result) |result| {
        return !used.contains(result);
    }

    return false;
}
