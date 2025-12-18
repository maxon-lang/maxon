const std = @import("std");
const ir = @import("ir.zig");
const debug = @import("debug.zig");

// ============================================================================
// Public API
// ============================================================================

/// Run all optimizer passes with fixed-point iteration
pub fn optimize(module: *ir.Module, allocator: std.mem.Allocator) !void {
    const max_iterations = 10;

    for (module.functions.items) |*func| {
        debug.log("Optimizing function: {s}", .{func.name});
        const initial_count = countInstructions(func);
        debug.log("  Before optimization: {d} instructions", .{initial_count});

        var iteration: usize = 0;
        while (iteration < max_iterations) : (iteration += 1) {
            const before_count = countInstructions(func);

            try constantFolding(func, allocator);
            try copyPropagation(func, allocator);
            try deadStoreElimination(func, allocator);
            try deadCodeElimination(func, allocator);

            const after_count = countInstructions(func);

            if (after_count == before_count) {
                debug.log("  Fixed point reached after {d} iteration(s): {d} instructions", .{ iteration + 1, after_count });
                break;
            }

            debug.log("  Iteration {d}: {d} -> {d} instructions", .{ iteration + 1, before_count, after_count });
        }

        if (iteration == max_iterations) {
            debug.log("  Warning: max iterations reached", .{});
        }
    }
}

// ============================================================================
// Constant Folding
// ============================================================================

/// Evaluate constant expressions at compile time
fn constantFolding(func: *ir.Function, allocator: std.mem.Allocator) !void {
    var constants = std.AutoHashMapUnmanaged(ir.Value, i64){};
    defer constants.deinit(allocator);

    for (func.blocks.items) |*block| {
        var new_instructions: std.ArrayListUnmanaged(ir.Instruction) = .empty;
        defer new_instructions.deinit(allocator);

        for (block.instructions.items) |inst| {
            if (try tryFoldConstant(inst, &constants, allocator)) |folded| {
                try new_instructions.append(allocator, folded);
            } else {
                try new_instructions.append(allocator, inst);
            }
        }

        try replaceBlockInstructions(block, &new_instructions, func.allocator);
    }
}

fn tryFoldConstant(inst: ir.Instruction, constants: *std.AutoHashMapUnmanaged(ir.Value, i64), allocator: std.mem.Allocator) !?ir.Instruction {
    switch (inst.op) {
        .const_i64 => {
            if (inst.result) |result| {
                try constants.put(allocator, result, inst.operands[0].immediate_i64);
            }
            return null;
        },
        .add, .sub, .mul, .div, .mod => {
            const lhs = constants.get(inst.operands[0].value) orelse return null;
            const rhs = constants.get(inst.operands[1].value) orelse return null;

            const result_val = switch (inst.op) {
                .add => lhs + rhs,
                .sub => lhs - rhs,
                .mul => lhs * rhs,
                .div => @divTrunc(lhs, rhs),
                .mod => @mod(lhs, rhs),
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
        },
        else => return null,
    }
}

// ============================================================================
// Copy Propagation
// ============================================================================

/// Replace loads with the value that was stored
fn copyPropagation(func: *ir.Function, allocator: std.mem.Allocator) !void {
    var ptr_to_value = std.AutoHashMapUnmanaged(ir.Value, ir.Value){};
    defer ptr_to_value.deinit(allocator);

    var value_map = std.AutoHashMapUnmanaged(ir.Value, ir.Value){};
    defer value_map.deinit(allocator);

    for (func.blocks.items) |*block| {
        var new_instructions: std.ArrayListUnmanaged(ir.Instruction) = .empty;
        defer new_instructions.deinit(allocator);

        for (block.instructions.items) |inst| {
            switch (inst.op) {
                .store => try handleStore(inst, &ptr_to_value, &value_map, &new_instructions, allocator),
                .load => try handleLoad(inst, &ptr_to_value, &value_map, &new_instructions, allocator),
                else => try handleOther(inst, &value_map, &new_instructions, func.allocator, allocator),
            }
        }

        try replaceBlockInstructions(block, &new_instructions, func.allocator);
    }
}

fn handleStore(
    inst: ir.Instruction,
    ptr_to_value: *std.AutoHashMapUnmanaged(ir.Value, ir.Value),
    value_map: *std.AutoHashMapUnmanaged(ir.Value, ir.Value),
    new_instructions: *std.ArrayListUnmanaged(ir.Instruction),
    allocator: std.mem.Allocator,
) !void {
    const ptr = inst.operands[0].value;
    const val = inst.operands[1].value;
    const mapped_val = value_map.get(val) orelse val;

    try ptr_to_value.put(allocator, ptr, mapped_val);

    var new_inst = inst;
    new_inst.operands[1] = .{ .value = mapped_val };
    try new_instructions.append(allocator, new_inst);
}

fn handleLoad(
    inst: ir.Instruction,
    ptr_to_value: *std.AutoHashMapUnmanaged(ir.Value, ir.Value),
    value_map: *std.AutoHashMapUnmanaged(ir.Value, ir.Value),
    new_instructions: *std.ArrayListUnmanaged(ir.Instruction),
    allocator: std.mem.Allocator,
) !void {
    const ptr = inst.operands[0].value;

    if (ptr_to_value.get(ptr)) |stored_val| {
        if (inst.result) |result| {
            try value_map.put(allocator, result, stored_val);
        }
        // Eliminate load - value will be propagated
    } else {
        try new_instructions.append(allocator, inst);
    }
}

fn handleOther(
    inst: ir.Instruction,
    value_map: *std.AutoHashMapUnmanaged(ir.Value, ir.Value),
    new_instructions: *std.ArrayListUnmanaged(ir.Instruction),
    func_allocator: std.mem.Allocator,
    temp_allocator: std.mem.Allocator,
) !void {
    var new_inst = inst;

    for (&new_inst.operands) |*op| {
        switch (op.*) {
            .value => |v| {
                if (value_map.get(v)) |propagated| {
                    op.* = .{ .value = propagated };
                }
            },
            .call_args => |args| {
                if (anyArgNeedsPropagation(args, value_map)) {
                    const new_args = try func_allocator.alloc(ir.Value, args.len);
                    for (args, 0..) |arg, i| {
                        new_args[i] = value_map.get(arg) orelse arg;
                    }
                    func_allocator.free(args);
                    op.* = .{ .call_args = new_args };
                }
            },
            else => {},
        }
    }

    try new_instructions.append(temp_allocator, new_inst);
}

fn anyArgNeedsPropagation(args: []const ir.Value, value_map: *std.AutoHashMapUnmanaged(ir.Value, ir.Value)) bool {
    for (args) |arg| {
        if (value_map.contains(arg)) return true;
    }
    return false;
}

// ============================================================================
// Dead Store Elimination
// ============================================================================

/// Key for identifying equivalent field pointers via offset chain from base alloca
const FieldPtrKey = struct {
    base: ir.Value,
    offsets: []const i32,

    fn eql(a: FieldPtrKey, b: FieldPtrKey) bool {
        if (a.base != b.base) return false;
        if (a.offsets.len != b.offsets.len) return false;
        for (a.offsets, b.offsets) |ao, bo| {
            if (ao != bo) return false;
        }
        return true;
    }
};

const ImmediateFieldEntry = struct {
    base: ir.Value,
    offset: i32,
};

/// Context for dead store elimination analysis
const DseContext = struct {
    ptr_to_immediate: std.AutoHashMapUnmanaged(ir.Value, ImmediateFieldEntry),
    ptr_to_array_base: std.AutoHashMapUnmanaged(ir.Value, ir.Value),
    loaded_array_bases: std.AutoHashMapUnmanaged(ir.Value, void),
    loaded_bases: std.AutoHashMapUnmanaged(ir.Value, void),
    loaded_fields: std.ArrayListUnmanaged(FieldPtrKey),
    loaded_ptrs: std.AutoHashMapUnmanaged(ir.Value, void),
    allocator: std.mem.Allocator,

    fn init(allocator: std.mem.Allocator) DseContext {
        return .{
            .ptr_to_immediate = .{},
            .ptr_to_array_base = .{},
            .loaded_array_bases = .{},
            .loaded_bases = .{},
            .loaded_fields = .{},
            .loaded_ptrs = .{},
            .allocator = allocator,
        };
    }

    fn deinit(self: *DseContext) void {
        self.ptr_to_immediate.deinit(self.allocator);
        self.ptr_to_array_base.deinit(self.allocator);
        self.loaded_array_bases.deinit(self.allocator);
        self.loaded_bases.deinit(self.allocator);
        for (self.loaded_fields.items) |field| {
            self.allocator.free(field.offsets);
        }
        self.loaded_fields.deinit(self.allocator);
        self.loaded_ptrs.deinit(self.allocator);
    }

    /// Resolve pointer to normalized field key by tracing getfieldptr chain
    fn resolveFieldKey(self: *DseContext, ptr: ir.Value) !?FieldPtrKey {
        var offsets: std.ArrayListUnmanaged(i32) = .empty;
        defer offsets.deinit(self.allocator);

        var current = ptr;
        while (self.ptr_to_immediate.get(current)) |entry| {
            try offsets.append(self.allocator, entry.offset);
            current = entry.base;
        }

        if (offsets.items.len == 0) return null;

        std.mem.reverse(i32, offsets.items);
        const owned_offsets = try self.allocator.dupe(i32, offsets.items);
        return .{ .base = current, .offsets = owned_offsets };
    }

    fn isStoreNeeded(self: *DseContext, ptr: ir.Value) !bool {
        if (self.loaded_ptrs.contains(ptr)) return true;

        if (try self.resolveFieldKey(ptr)) |store_key| {
            defer self.allocator.free(store_key.offsets);

            if (self.loaded_bases.contains(store_key.base)) return true;

            for (self.loaded_fields.items) |load_key| {
                if (FieldPtrKey.eql(store_key, load_key)) return true;
            }
        }

        if (self.ptr_to_array_base.get(ptr)) |array_base| {
            if (self.loaded_array_bases.contains(array_base)) return true;
            // Also check if array base is passed to a call (marked in loaded_bases)
            if (self.loaded_bases.contains(array_base)) return true;
        }

        return false;
    }
};

/// Remove stores to allocas that are never loaded
fn deadStoreElimination(func: *ir.Function, allocator: std.mem.Allocator) !void {
    var ctx = DseContext.init(allocator);
    defer ctx.deinit();

    // Pass 1: Build pointer mappings
    try collectPointerMappings(func, &ctx);

    // Pass 2: Find loaded pointers
    try collectLoadedPointers(func, &ctx);

    // Pass 3: Remove dead stores
    try removeDeadStores(func, &ctx);
}

fn collectPointerMappings(func: *ir.Function, ctx: *DseContext) !void {
    for (func.blocks.items) |block| {
        for (block.instructions.items) |inst| {
            if (inst.op == .getfieldptr) {
                if (inst.result) |result| {
                    try ctx.ptr_to_immediate.put(ctx.allocator, result, .{
                        .base = inst.operands[0].value,
                        .offset = inst.operands[1].immediate_i32,
                    });
                }
            } else if (inst.op == .getelemptr) {
                if (inst.result) |result| {
                    try ctx.ptr_to_array_base.put(ctx.allocator, result, inst.operands[0].value);
                }
            }
        }
    }
}

fn collectLoadedPointers(func: *ir.Function, ctx: *DseContext) !void {
    for (func.blocks.items) |block| {
        for (block.instructions.items) |inst| {
            switch (inst.op) {
                .param => {
                    // Pointer parameters are externally accessible (e.g., sret)
                    if (inst.result) |result| {
                        if (inst.result_type == .ptr) {
                            try ctx.loaded_bases.put(ctx.allocator, result, {});
                        }
                    }
                },
                .load => {
                    const ptr = inst.operands[0].value;
                    try ctx.loaded_ptrs.put(ctx.allocator, ptr, {});

                    if (try ctx.resolveFieldKey(ptr)) |field_key| {
                        try ctx.loaded_bases.put(ctx.allocator, field_key.base, {});
                        try ctx.loaded_fields.append(ctx.allocator, field_key);
                    }

                    if (ctx.ptr_to_array_base.get(ptr)) |array_base| {
                        try ctx.loaded_array_bases.put(ctx.allocator, array_base, {});
                    }
                },
                .call => {
                    // Pointers passed to calls may be read
                    for (inst.operands) |op| {
                        if (op == .call_args) {
                            for (op.call_args) |arg| {
                                try ctx.loaded_bases.put(ctx.allocator, arg, {});
                            }
                        }
                    }
                },
                .ret => {
                    // Pointers returned from functions may be read by caller
                    // Mark the returned value as loaded so stores to it are preserved
                    if (inst.operands[0] == .value) {
                        const ret_val = inst.operands[0].value;
                        try ctx.loaded_bases.put(ctx.allocator, ret_val, {});
                        // Also check if it's derived from an array base
                        if (ctx.ptr_to_array_base.get(ret_val)) |array_base| {
                            try ctx.loaded_array_bases.put(ctx.allocator, array_base, {});
                        }
                    }
                },
                else => {},
            }
        }
    }
}

fn removeDeadStores(func: *ir.Function, ctx: *DseContext) !void {
    for (func.blocks.items) |*block| {
        var new_instructions: std.ArrayListUnmanaged(ir.Instruction) = .empty;
        defer new_instructions.deinit(ctx.allocator);

        for (block.instructions.items) |inst| {
            const keep = if (inst.op == .store)
                try ctx.isStoreNeeded(inst.operands[0].value)
            else
                true;

            if (keep) {
                try new_instructions.append(ctx.allocator, inst);
            }
        }

        try replaceBlockInstructions(block, &new_instructions, func.allocator);
    }
}

// ============================================================================
// Dead Code Elimination
// ============================================================================

/// Remove instructions whose results are never used
fn deadCodeElimination(func: *ir.Function, allocator: std.mem.Allocator) !void {
    var used = std.AutoHashMapUnmanaged(ir.Value, void){};
    defer used.deinit(allocator);

    // Pass 1: Find all used values
    for (func.blocks.items) |block| {
        for (block.instructions.items) |inst| {
            for (inst.operands) |op| {
                switch (op) {
                    .value => |v| try used.put(allocator, v, {}),
                    .call_args => |args| {
                        for (args) |arg| {
                            try used.put(allocator, arg, {});
                        }
                    },
                    else => {},
                }
            }
        }
    }

    // Pass 2: Remove dead instructions
    for (func.blocks.items) |*block| {
        var new_instructions: std.ArrayListUnmanaged(ir.Instruction) = .empty;
        defer new_instructions.deinit(allocator);

        for (block.instructions.items) |inst| {
            if (!isDeadInstruction(inst, &used)) {
                try new_instructions.append(allocator, inst);
            }
        }

        try replaceBlockInstructions(block, &new_instructions, func.allocator);
    }
}

fn isDeadInstruction(inst: ir.Instruction, used: *std.AutoHashMapUnmanaged(ir.Value, void)) bool {
    // Side-effecting instructions are never dead
    switch (inst.op) {
        .store, .ret, .br, .br_cond, .call => return false,
        else => {},
    }

    if (inst.result) |result| {
        return !used.contains(result);
    }

    return false;
}

// ============================================================================
// Utilities
// ============================================================================

fn countInstructions(func: *ir.Function) usize {
    var count: usize = 0;
    for (func.blocks.items) |block| {
        count += block.instructions.items.len;
    }
    return count;
}

fn replaceBlockInstructions(
    block: *ir.BasicBlock,
    new_instructions: *std.ArrayListUnmanaged(ir.Instruction),
    func_allocator: std.mem.Allocator,
) !void {
    block.instructions.deinit(func_allocator);
    block.instructions = .empty;
    for (new_instructions.items) |inst| {
        try block.instructions.append(func_allocator, inst);
    }
}
