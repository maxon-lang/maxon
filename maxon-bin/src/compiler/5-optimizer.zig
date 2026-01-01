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
                .operands = .{ .{ .immediate_i64 = result_val }, .none, .none },
            };
        },
        // Instructions not handled by constant folding
        .const_i8,
        .const_i32,
        .const_f64,
        .const_string,
        .alloca,
        .alloca_sized,
        .alloca_dynamic,
        .load,
        .store,
        .store_i8,
        .store_i32,
        .getfieldptr,
        .getelemptr,
        .band,
        .bitor,
        .bxor,
        .shl,
        .shr,
        .fadd,
        .fsub,
        .fmul,
        .fdiv,
        .fptosi,
        .sitofp,
        .fabs,
        .fsqrt,
        .fceil,
        .ffloor,
        .fround,
        .bitcast_f64_to_i64,
        .sext_i32_i64,
        .trunc_i64_i32,
        .trunc_i64_i8,
        .zext_i8_i64,
        .ret,
        .br,
        .br_cond,
        .icmp_eq,
        .icmp_ne,
        .icmp_lt,
        .icmp_le,
        .icmp_gt,
        .icmp_ge,
        .fcmp_eq,
        .fcmp_ne,
        .fcmp_lt,
        .fcmp_le,
        .fcmp_gt,
        .fcmp_ge,
        .call,
        .call_indirect,
        .func_addr,
        .param,
        .memcpy,
        .memcpy_dyn,
        .memset,
        .heap_alloc,
        .heap_free,
        .heap_realloc,
        => return null,
    }
}

// ============================================================================
// Copy Propagation
// ============================================================================

/// Context for copy propagation with normalized field key tracking
const CopyPropContext = struct {
    ptr_to_immediate: std.AutoHashMapUnmanaged(ir.Value, ImmediateFieldEntry),
    field_to_value: std.ArrayListUnmanaged(FieldValueEntry),
    value_map: std.AutoHashMapUnmanaged(ir.Value, ir.Value),
    allocator: std.mem.Allocator,

    const FieldValueEntry = struct {
        key: FieldPtrKey,
        value: ir.Value,
    };

    fn init(allocator: std.mem.Allocator) CopyPropContext {
        return .{
            .ptr_to_immediate = .{},
            .field_to_value = .{},
            .value_map = .{},
            .allocator = allocator,
        };
    }

    fn deinit(self: *CopyPropContext) void {
        self.ptr_to_immediate.deinit(self.allocator);
        for (self.field_to_value.items) |entry| {
            self.allocator.free(entry.key.offsets);
        }
        self.field_to_value.deinit(self.allocator);
        self.value_map.deinit(self.allocator);
    }

    fn resolveFieldKey(self: *CopyPropContext, ptr: ir.Value) !?FieldPtrKey {
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

    fn lookupFieldValue(self: *CopyPropContext, key: FieldPtrKey) ?ir.Value {
        for (self.field_to_value.items) |entry| {
            if (FieldPtrKey.eql(key, entry.key)) {
                return entry.value;
            }
        }
        return null;
    }

    fn storeFieldValue(self: *CopyPropContext, key: FieldPtrKey, value: ir.Value) !void {
        // Update existing entry or add new one
        for (self.field_to_value.items) |*entry| {
            if (FieldPtrKey.eql(key, entry.key)) {
                entry.value = value;
                self.allocator.free(key.offsets);
                return;
            }
        }
        try self.field_to_value.append(self.allocator, .{ .key = key, .value = value });
    }
};

/// Replace loads with the value that was stored
/// NOTE: This operates block-locally to avoid incorrect propagation across
/// basic blocks that may not have proper dominance relationships.
fn copyPropagation(func: *ir.Function, allocator: std.mem.Allocator) !void {
    // Process each block independently to avoid cross-block propagation issues
    for (func.blocks.items) |*block| {
        var ctx = CopyPropContext.init(allocator);
        defer ctx.deinit();

        // Collect getfieldptr mappings within this block only
        for (block.instructions.items) |inst| {
            if (inst.op == .getfieldptr) {
                if (inst.result) |result| {
                    try ctx.ptr_to_immediate.put(allocator, result, .{
                        .base = inst.operands[0].value,
                        .offset = inst.operands[1].immediate_i32,
                    });
                }
            }
        }

        // Propagate copies within this block
        var new_instructions: std.ArrayListUnmanaged(ir.Instruction) = .empty;
        defer new_instructions.deinit(allocator);

        for (block.instructions.items) |inst| {
            switch (inst.op) {
                .store => try handleStore(inst, &ctx, &new_instructions, allocator),
                .load => try handleLoad(inst, &ctx, &new_instructions, allocator),
                else => try handleOther(inst, &ctx.value_map, &new_instructions, func.allocator, allocator),
            }
        }

        try replaceBlockInstructions(block, &new_instructions, func.allocator);
    }
}

fn handleStore(
    inst: ir.Instruction,
    ctx: *CopyPropContext,
    new_instructions: *std.ArrayListUnmanaged(ir.Instruction),
    allocator: std.mem.Allocator,
) !void {
    const ptr = inst.operands[0].value;
    const val = inst.operands[1].value;
    const mapped_val = ctx.value_map.get(val) orelse val;

    // Store using normalized field key if possible
    if (try ctx.resolveFieldKey(ptr)) |key| {
        try ctx.storeFieldValue(key, mapped_val);
    }

    var new_inst = inst;
    new_inst.operands[1] = .{ .value = mapped_val };
    try new_instructions.append(allocator, new_inst);
}

fn handleLoad(
    inst: ir.Instruction,
    ctx: *CopyPropContext,
    new_instructions: *std.ArrayListUnmanaged(ir.Instruction),
    allocator: std.mem.Allocator,
) !void {
    const ptr = inst.operands[0].value;

    // Try to find stored value using normalized field key
    if (try ctx.resolveFieldKey(ptr)) |key| {
        defer allocator.free(key.offsets);
        if (ctx.lookupFieldValue(key)) |stored_val| {
            if (inst.result) |result| {
                try ctx.value_map.put(allocator, result, stored_val);
            }
            // Eliminate load - value will be propagated
            return;
        }
    }

    try new_instructions.append(allocator, inst);
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
            // none, immediate_*, block_ref, func_name, elem_size, string_data:
            // These don't contain ir.Value references, so no propagation needed
            .none, .immediate_i32, .immediate_i64, .immediate_f64, .block_ref, .func_name, .elem_size, .string_data => {},
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

/// Unified pointer derivation - tracks how a pointer was derived from a base
const PtrDerivation = union(enum) {
    /// Static field access (getfieldptr) - can be precisely tracked
    field: ImmediateFieldEntry,
    /// Dynamic element access (getelemptr) - conservatively tracks base only
    element: ir.Value,
};

/// Context for dead store elimination analysis
const DseContext = struct {
    ptr_derivation: std.AutoHashMapUnmanaged(ir.Value, PtrDerivation),
    loaded_bases: std.AutoHashMapUnmanaged(ir.Value, void),
    loaded_fields: std.ArrayListUnmanaged(FieldPtrKey),
    loaded_ptrs: std.AutoHashMapUnmanaged(ir.Value, void),
    allocator: std.mem.Allocator,

    fn init(allocator: std.mem.Allocator) DseContext {
        return .{
            .ptr_derivation = .{},
            .loaded_bases = .{},
            .loaded_fields = .{},
            .loaded_ptrs = .{},
            .allocator = allocator,
        };
    }

    fn deinit(self: *DseContext) void {
        self.ptr_derivation.deinit(self.allocator);
        self.loaded_bases.deinit(self.allocator);
        for (self.loaded_fields.items) |field| {
            self.allocator.free(field.offsets);
        }
        self.loaded_fields.deinit(self.allocator);
        self.loaded_ptrs.deinit(self.allocator);
    }

    /// Resolve pointer to ultimate base by following derivation chain
    fn resolveToBase(self: *DseContext, ptr: ir.Value) ir.Value {
        var current = ptr;
        while (self.ptr_derivation.get(current)) |deriv| {
            current = switch (deriv) {
                .field => |f| f.base,
                .element => |base| base,
            };
        }
        return current;
    }

    /// Resolve pointer to normalized field key by tracing getfieldptr chain
    fn resolveFieldKey(self: *DseContext, ptr: ir.Value) !?FieldPtrKey {
        var offsets: std.ArrayListUnmanaged(i32) = .empty;
        defer offsets.deinit(self.allocator);

        var current = ptr;
        while (self.ptr_derivation.get(current)) |deriv| {
            switch (deriv) {
                .field => |entry| {
                    try offsets.append(self.allocator, entry.offset);
                    current = entry.base;
                },
                .element => return null, // Can't precisely track through dynamic index
            }
        }

        if (offsets.items.len == 0) return null;

        std.mem.reverse(i32, offsets.items);
        const owned_offsets = try self.allocator.dupe(i32, offsets.items);
        return .{ .base = current, .offsets = owned_offsets };
    }

    fn isStoreNeeded(self: *DseContext, ptr: ir.Value) !bool {
        if (self.loaded_ptrs.contains(ptr)) return true;

        // Check if base is external/loaded (works for both field and element pointers)
        const base = self.resolveToBase(ptr);
        if (self.loaded_bases.contains(base)) return true;

        // For field pointers, also check precise field tracking
        if (try self.resolveFieldKey(ptr)) |store_key| {
            defer self.allocator.free(store_key.offsets);
            for (self.loaded_fields.items) |load_key| {
                if (FieldPtrKey.eql(store_key, load_key)) return true;
            }
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
            if (inst.result) |result| {
                const derivation: ?PtrDerivation = switch (inst.op) {
                    .getfieldptr => .{ .field = .{
                        .base = inst.operands[0].value,
                        .offset = inst.operands[1].immediate_i32,
                    } },
                    .getelemptr => .{ .element = inst.operands[0].value },
                    // Instructions that don't derive pointers
                    .const_i8,
                    .const_i32,
                    .const_i64,
                    .const_f64,
                    .const_string,
                    .alloca,
                    .alloca_sized,
                    .alloca_dynamic,
                    .load,
                    .store,
                    .store_i8,
                    .store_i32,
                    .add,
                    .sub,
                    .mul,
                    .div,
                    .mod,
                    .band,
                    .bitor,
                    .bxor,
                    .shl,
                    .shr,
                    .fadd,
                    .fsub,
                    .fmul,
                    .fdiv,
                    .fptosi,
                    .sitofp,
                    .fabs,
                    .fsqrt,
                    .fceil,
                    .ffloor,
                    .fround,
                    .bitcast_f64_to_i64,
                    .sext_i32_i64,
                    .trunc_i64_i32,
                    .trunc_i64_i8,
                    .zext_i8_i64,
                    .ret,
                    .br,
                    .br_cond,
                    .icmp_eq,
                    .icmp_ne,
                    .icmp_lt,
                    .icmp_le,
                    .icmp_gt,
                    .icmp_ge,
                    .fcmp_eq,
                    .fcmp_ne,
                    .fcmp_lt,
                    .fcmp_le,
                    .fcmp_gt,
                    .fcmp_ge,
                    .call,
                    .call_indirect,
                    .func_addr,
                    .param,
                    .memcpy,
                    .memcpy_dyn,
                    .memset,
                    .heap_alloc,
                    .heap_free,
                    .heap_realloc,
                    => null,
                };
                if (derivation) |d| {
                    try ctx.ptr_derivation.put(ctx.allocator, result, d);
                }
            }
        }
    }
}

fn collectLoadedPointers(func: *ir.Function, ctx: *DseContext) !void {
    // First pass: identify external bases (params, ret values)
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

                    // Mark base as loaded (works for both field and element pointers)
                    const base = ctx.resolveToBase(ptr);
                    try ctx.loaded_bases.put(ctx.allocator, base, {});

                    // For field pointers, also track precise field access
                    if (try ctx.resolveFieldKey(ptr)) |field_key| {
                        try ctx.loaded_fields.append(ctx.allocator, field_key);
                    }
                },
                .call, .call_indirect => {
                    // Pointers passed to calls may be read
                    for (inst.operands) |op| {
                        if (op == .call_args) {
                            for (op.call_args) |arg| {
                                try ctx.loaded_bases.put(ctx.allocator, arg, {});
                            }
                        }
                    }
                },
                .memcpy => {
                    // memcpy reads from source pointer - mark it as loaded
                    const src_ptr = inst.operands[1].value;
                    try ctx.loaded_ptrs.put(ctx.allocator, src_ptr, {});
                    const src_base = ctx.resolveToBase(src_ptr);
                    try ctx.loaded_bases.put(ctx.allocator, src_base, {});
                },
                .ret => {
                    // Pointers returned from functions may be read by caller
                    if (inst.operands[0] == .value) {
                        const ret_val = inst.operands[0].value;
                        // Mark both the returned value and its ultimate base as loaded
                        try ctx.loaded_bases.put(ctx.allocator, ret_val, {});
                        const base = ctx.resolveToBase(ret_val);
                        try ctx.loaded_bases.put(ctx.allocator, base, {});
                    }
                },
                .memcpy_dyn => {
                    // memcpy_dyn reads from source pointer - mark it as loaded
                    const src_ptr = inst.operands[1].value;
                    try ctx.loaded_ptrs.put(ctx.allocator, src_ptr, {});
                    const src_base = ctx.resolveToBase(src_ptr);
                    try ctx.loaded_bases.put(ctx.allocator, src_base, {});
                },
                // Instructions that don't affect pointer liveness analysis
                .const_i8,
                .const_i32,
                .const_i64,
                .const_f64,
                .const_string,
                .alloca,
                .alloca_sized,
                .alloca_dynamic,
                .store,
                .store_i8,
                .store_i32,
                .getfieldptr,
                .getelemptr,
                .add,
                .sub,
                .mul,
                .div,
                .mod,
                .band,
                .bitor,
                .bxor,
                .shl,
                .shr,
                .fadd,
                .fsub,
                .fmul,
                .fdiv,
                .fptosi,
                .sitofp,
                .fabs,
                .fsqrt,
                .fceil,
                .ffloor,
                .fround,
                .bitcast_f64_to_i64,
                .sext_i32_i64,
                .trunc_i64_i32,
                .trunc_i64_i8,
                .zext_i8_i64,
                .br,
                .br_cond,
                .icmp_eq,
                .icmp_ne,
                .icmp_lt,
                .icmp_le,
                .icmp_gt,
                .icmp_ge,
                .fcmp_eq,
                .fcmp_ne,
                .fcmp_lt,
                .fcmp_le,
                .fcmp_gt,
                .fcmp_ge,
                .memset,
                .heap_alloc,
                .heap_free,
                .heap_realloc,
                .func_addr,
                => {},
            }
        }
    }

    // Second pass: if a pointer is stored to an external location, mark it as external too
    // This handles nested struct pointers stored to sret buffers
    var changed = true;
    while (changed) {
        changed = false;
        for (func.blocks.items) |block| {
            for (block.instructions.items) |inst| {
                if (inst.op == .store) {
                    const dest_ptr = inst.operands[0].value;
                    const stored_val = inst.operands[1].value;

                    // Check if destination is derived from an external base
                    const dest_base = ctx.resolveToBase(dest_ptr);
                    if (ctx.loaded_bases.contains(dest_base)) {
                        // The destination is external, so the stored pointer is also accessible
                        if (!ctx.loaded_bases.contains(stored_val)) {
                            try ctx.loaded_bases.put(ctx.allocator, stored_val, {});
                            changed = true;
                        }
                    }
                }
            }
        }
    }
}

fn removeDeadStores(func: *ir.Function, ctx: *DseContext) !void {
    for (func.blocks.items) |*block| {
        var new_instructions: std.ArrayListUnmanaged(ir.Instruction) = .empty;
        defer new_instructions.deinit(ctx.allocator);

        for (block.instructions.items) |inst| {
            const keep = if (inst.op == .store) blk: {
                const ptr = inst.operands[0].value;
                // Always keep stores to array elements (getelemptr) - they may be accessed
                // through different load paths that the optimizer can't track
                if (ctx.ptr_derivation.get(ptr)) |derivation| {
                    if (derivation == .element) break :blk true;
                }
                break :blk try ctx.isStoreNeeded(ptr);
            } else true;

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
                    // none, immediate_*, block_ref, func_name, elem_size, string_data:
                    // These don't reference ir.Value, so nothing to mark as used
                    .none, .immediate_i32, .immediate_i64, .immediate_f64, .block_ref, .func_name, .elem_size, .string_data => {},
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
        .store,
        .store_i8,
        .store_i32,
        .ret,
        .br,
        .br_cond,
        .call,
        .call_indirect,
        .memcpy,
        .memcpy_dyn,
        .memset,
        .heap_alloc,
        .heap_free,
        .heap_realloc,
        => return false,
        // Pure instructions: can be eliminated if result is unused
        .const_i8,
        .const_i32,
        .const_i64,
        .const_f64,
        .const_string,
        .alloca,
        .alloca_sized,
        .alloca_dynamic,
        .load,
        .getfieldptr,
        .add,
        .sub,
        .mul,
        .div,
        .mod,
        .band,
        .bitor,
        .bxor,
        .shl,
        .shr,
        .fadd,
        .fsub,
        .fmul,
        .fdiv,
        .fptosi,
        .sitofp,
        .fabs,
        .fsqrt,
        .fceil,
        .ffloor,
        .fround,
        .bitcast_f64_to_i64,
        .sext_i32_i64,
        .trunc_i64_i32,
        .trunc_i64_i8,
        .zext_i8_i64,
        .icmp_eq,
        .icmp_ne,
        .icmp_lt,
        .icmp_le,
        .icmp_gt,
        .icmp_ge,
        .fcmp_eq,
        .fcmp_ne,
        .fcmp_lt,
        .fcmp_le,
        .fcmp_gt,
        .fcmp_ge,
        .param,
        .getelemptr,
        .func_addr,
        => {},
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
