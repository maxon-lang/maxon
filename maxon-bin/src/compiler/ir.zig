const std = @import("std");
const debug = @import("debug.zig");

/// IR Value reference - %0, %1, etc.
pub const Value = u32;

/// IR Type
pub const Type = enum {
    i32,
    i64,
    f64,
    void,
    ptr,

    pub fn format(self: Type) []const u8 {
        return switch (self) {
            .i32 => "i32",
            .i64 => "i64",
            .f64 => "f64",
            .void => "void",
            .ptr => "ptr",
        };
    }
};

/// IR Instruction
pub const Instruction = struct {
    op: Op,
    result: ?Value = null,
    result_type: Type = .void,
    operands: [2]Operand = .{ .none, .none },

    pub const Op = enum {
        // Constants
        const_i32,
        const_i64,
        const_f64,

        // Memory
        alloca,
        alloca_sized,
        alloca_dynamic, // alloca with runtime size value
        load,
        store,
        getfieldptr,

        // Integer arithmetic
        add,
        sub,
        mul,
        div,
        mod,

        // Float arithmetic
        fadd,
        fsub,
        fmul,
        fdiv,

        // Conversions
        fptosi, // float to signed int
        sitofp, // signed int to float
        fabs, // float absolute value
        bitcast_f64_to_i64, // reinterpret f64 bits as i64 (for hashing)

        // Control flow
        ret,
        br,
        br_cond,

        // Integer comparison
        icmp_eq,
        icmp_ne,
        icmp_lt,
        icmp_le,
        icmp_gt,
        icmp_ge,

        // Float comparison
        fcmp_eq,
        fcmp_ne,
        fcmp_lt,
        fcmp_le,
        fcmp_gt,
        fcmp_ge,

        // Function call
        call,

        // Parameters
        param,

        // Array operations
        getelemptr, // Get pointer to array element

        // Memory operations
        memcpy, // Copy memory: dest, src, size
        memset, // Set memory: dest, value, size (value is typically 0)

        // Heap allocation
        heap_alloc, // Allocate heap memory, returns ptr
        heap_free, // Free heap memory
        heap_realloc, // Reallocate heap memory: old_ptr, new_size -> new_ptr

        pub fn format(self: Op) []const u8 {
            return switch (self) {
                .const_i32 => "const.i32",
                .const_i64 => "const.i64",
                .const_f64 => "const.f64",
                .alloca => "alloca",
                .alloca_sized => "alloca.sized",
                .alloca_dynamic => "alloca.dynamic",
                .load => "load",
                .store => "store",
                .getfieldptr => "getfieldptr",
                .add => "add",
                .sub => "sub",
                .mul => "mul",
                .div => "div",
                .mod => "mod",
                .fadd => "fadd",
                .fsub => "fsub",
                .fmul => "fmul",
                .fdiv => "fdiv",
                .fptosi => "fptosi",
                .sitofp => "sitofp",
                .fabs => "fabs",
                .bitcast_f64_to_i64 => "bitcast.f64.i64",
                .ret => "ret",
                .br => "br",
                .br_cond => "br.cond",
                .icmp_eq => "icmp.eq",
                .icmp_ne => "icmp.ne",
                .icmp_lt => "icmp.lt",
                .icmp_le => "icmp.le",
                .icmp_gt => "icmp.gt",
                .icmp_ge => "icmp.ge",
                .fcmp_eq => "fcmp.eq",
                .fcmp_ne => "fcmp.ne",
                .fcmp_lt => "fcmp.lt",
                .fcmp_le => "fcmp.le",
                .fcmp_gt => "fcmp.gt",
                .fcmp_ge => "fcmp.ge",
                .call => "call",
                .param => "param",
                .getelemptr => "getelemptr",
                .memcpy => "memcpy",
                .memset => "memset",
                .heap_alloc => "heap.alloc",
                .heap_free => "heap.free",
                .heap_realloc => "heap.realloc",
            };
        }
    };

    pub const Operand = union(enum) {
        none,
        value: Value,
        immediate_i32: i32,
        immediate_i64: i64,
        immediate_f64: f64,
        block_ref: u32,
        func_name: []const u8,
        call_args: []const Value,
        elem_size: i32, // Element size for getelemptr
    };
};

/// Basic Block
pub const BasicBlock = struct {
    name: []const u8,
    instructions: std.ArrayListUnmanaged(Instruction),

    pub fn init(name: []const u8) BasicBlock {
        return .{
            .name = name,
            .instructions = .empty,
        };
    }

    pub fn deinit(self: *BasicBlock, allocator: std.mem.Allocator) void {
        self.instructions.deinit(allocator);
    }
};

/// IR Function
pub const Function = struct {
    name: []const u8,
    return_type: Type,
    is_exported: bool,
    blocks: std.ArrayListUnmanaged(BasicBlock),
    next_value: Value,
    allocator: std.mem.Allocator,
    // SSA value naming for readable IR output
    value_names: std.AutoHashMapUnmanaged(Value, []const u8),
    name_counters: std.StringHashMapUnmanaged(u32),
    allocated_names: std.ArrayListUnmanaged([]const u8), // Track strings we allocated

    pub fn init(allocator: std.mem.Allocator, name: []const u8, return_type: Type) Function {
        return initWithExport(allocator, name, return_type, false);
    }

    pub fn initWithExport(allocator: std.mem.Allocator, name: []const u8, return_type: Type, is_exported: bool) Function {
        return .{
            .name = name,
            .return_type = return_type,
            .is_exported = is_exported,
            .blocks = .empty,
            .next_value = 0,
            .allocator = allocator,
            .value_names = .empty,
            .name_counters = .empty,
            .allocated_names = .empty,
        };
    }

    pub fn deinit(self: *Function) void {
        for (self.blocks.items) |*block| {
            // Free call_args slices in instructions
            for (block.instructions.items) |inst| {
                for (inst.operands) |op| {
                    if (op == .call_args) {
                        self.allocator.free(op.call_args);
                    }
                }
            }
            block.deinit(self.allocator);
        }
        self.blocks.deinit(self.allocator);
        // Free allocated name strings
        for (self.allocated_names.items) |name| {
            self.allocator.free(name);
        }
        self.allocated_names.deinit(self.allocator);
        self.value_names.deinit(self.allocator);
        self.name_counters.deinit(self.allocator);
    }

    pub fn newValue(self: *Function) Value {
        const v = self.next_value;
        self.next_value += 1;
        return v;
    }

    pub fn addBlock(self: *Function, name: []const u8) !*BasicBlock {
        try self.blocks.append(self.allocator, BasicBlock.init(name));
        return &self.blocks.items[self.blocks.items.len - 1];
    }

    pub fn currentBlock(self: *Function) ?*BasicBlock {
        if (self.blocks.items.len > 0) {
            return &self.blocks.items[self.blocks.items.len - 1];
        }
        return null;
    }

    /// Set a debug name for an SSA value. Names are made unique automatically.
    pub fn setValueName(self: *Function, val: Value, base_name: []const u8) !void {
        const entry = try self.name_counters.getOrPut(self.allocator, base_name);
        if (!entry.found_existing) {
            entry.value_ptr.* = 0;
        }
        const counter = entry.value_ptr.*;
        entry.value_ptr.* = counter + 1;

        // Generate unique name: "x" -> "x", then "x.1", "x.2", etc.
        const unique_name = if (counter == 0)
            base_name
        else blk: {
            const allocated = try std.fmt.allocPrint(self.allocator, "{s}.{d}", .{ base_name, counter });
            try self.allocated_names.append(self.allocator, allocated);
            break :blk allocated;
        };

        try self.value_names.put(self.allocator, val, unique_name);
    }

    /// Get the debug name for an SSA value, if one exists.
    pub fn getValueName(self: *const Function, val: Value) ?[]const u8 {
        return self.value_names.get(val);
    }

    /// Get a base name for an operation type (temporary values).
    fn opToBaseName(op: Instruction.Op) []const u8 {
        return switch (op) {
            .const_i32 => "tmp_const",
            .const_i64 => "tmp_const",
            .const_f64 => "tmp_fconst",
            .alloca, .alloca_sized, .alloca_dynamic => "tmp_alloca",
            .load => "tmp_load",
            .store => "tmp_store",
            .getfieldptr => "tmp_fieldptr",
            .add => "tmp_add",
            .sub => "tmp_sub",
            .mul => "tmp_mul",
            .div => "tmp_div",
            .mod => "tmp_mod",
            .fadd => "tmp_fadd",
            .fsub => "tmp_fsub",
            .fmul => "tmp_fmul",
            .fdiv => "tmp_fdiv",
            .fptosi => "tmp_fptosi",
            .sitofp => "tmp_sitofp",
            .fabs => "tmp_fabs",
            .ret => "tmp_ret",
            .br, .br_cond => "tmp_br",
            .icmp_eq, .icmp_ne, .icmp_lt, .icmp_le, .icmp_gt, .icmp_ge => "tmp_icmp",
            .fcmp_eq, .fcmp_ne, .fcmp_lt, .fcmp_le, .fcmp_gt, .fcmp_ge => "tmp_fcmp",
            .call => "tmp_call",
            .param => "tmp_param",
            .getelemptr => "tmp_elemptr",
            .memcpy => "tmp_memcpy",
            .memset => "tmp_memset",
            .heap_alloc => "tmp_heap",
            .heap_free => "tmp_free",
            .heap_realloc => "tmp_realloc",
            .bitcast_f64_to_i64 => "tmp_bitcast",
        };
    }

    pub fn emit(self: *Function, inst: Instruction) !void {
        if (self.currentBlock()) |block| {
            try block.instructions.append(self.allocator, inst);
        }
    }

    // Core emit helpers
    fn emitWithResult(self: *Function, op: Instruction.Op, result_type: Type, operands: [2]Instruction.Operand) !Value {
        const result = self.newValue();
        try self.emit(.{ .op = op, .result = result, .result_type = result_type, .operands = operands });
        // Auto-name based on operation type
        try self.setValueName(result, opToBaseName(op));
        return result;
    }

    pub fn emitBinaryOp(self: *Function, op: Instruction.Op, lhs: Value, rhs: Value, ty: Type) !Value {
        return self.emitWithResult(op, ty, .{ .{ .value = lhs }, .{ .value = rhs } });
    }

    pub fn emitUnaryOp(self: *Function, op: Instruction.Op, src: Value, ty: Type) !Value {
        return self.emitWithResult(op, ty, .{ .{ .value = src }, .none });
    }

    // Constants
    pub fn emitConstI64(self: *Function, value: i64) !Value {
        return self.emitWithResult(.const_i64, .i64, .{ .{ .immediate_i64 = value }, .none });
    }

    pub fn emitConstF64(self: *Function, value: f64) !Value {
        return self.emitWithResult(.const_f64, .f64, .{ .{ .immediate_f64 = value }, .none });
    }

    // Memory
    pub fn emitAlloca(self: *Function, ty: Type) !Value {
        _ = ty;
        return self.emitWithResult(.alloca, .ptr, .{ .none, .none });
    }

    pub fn emitAllocaSized(self: *Function, size_bytes: i32) !Value {
        if (size_bytes <= 0) return error.ZeroSizeAllocation;
        return self.emitWithResult(.alloca_sized, .ptr, .{ .{ .immediate_i32 = size_bytes }, .none });
    }

    pub fn emitAllocaDynamic(self: *Function, size_value: Value) !Value {
        // Note: for dynamic sizes, zero-check must happen at runtime
        return self.emitWithResult(.alloca_dynamic, .ptr, .{ .{ .value = size_value }, .none });
    }

    pub fn emitLoad(self: *Function, ptr: Value, ty: Type) !Value {
        return self.emitUnaryOp(.load, ptr, ty);
    }

    pub fn emitStore(self: *Function, ptr: Value, value: Value) !void {
        try self.emit(.{ .op = .store, .operands = .{ .{ .value = ptr }, .{ .value = value } } });
    }

    pub fn emitGetFieldPtr(self: *Function, base_ptr: Value, field_offset: i32) !Value {
        return self.emitWithResult(.getfieldptr, .ptr, .{ .{ .value = base_ptr }, .{ .immediate_i32 = field_offset } });
    }

    pub fn emitGetElemPtr(self: *Function, base_ptr: Value, index: Value, elem_size: i32) !Value {
        const result = self.newValue();
        // The codegen will need to track element sizes separately
        // For simplicity, assume all elements are 8 bytes (i64/f64/ptr)
        _ = elem_size;
        try self.emit(.{
            .op = .getelemptr,
            .result = result,
            .result_type = .ptr,
            .operands = .{ .{ .value = base_ptr }, .{ .value = index } },
        });
        return result;
    }

    // Control flow
    pub fn emitRet(self: *Function, value: ?Value) !void {
        try self.emit(.{ .op = .ret, .operands = .{ if (value) |v| .{ .value = v } else .none, .none } });
    }

    pub fn emitBr(self: *Function, block_idx: u32) !void {
        try self.emit(.{ .op = .br, .operands = .{ .{ .block_ref = block_idx }, .none } });
    }

    pub fn emitBrCond(self: *Function, cond: Value, then_block: u32, else_block: u32) !void {
        try self.emit(.{
            .op = .br_cond,
            .operands = .{ .{ .value = cond }, .{ .block_ref = then_block } },
            .result = else_block, // Use result field for else block
        });
    }

    // Function calls
    pub fn emitCall(self: *Function, func_name: []const u8, args: []const Value, ret_type: Type) !?Value {
        if (ret_type == .void) {
            try self.emit(.{ .op = .call, .operands = .{ .{ .func_name = func_name }, .{ .call_args = args } } });
            return null;
        }
        return try self.emitWithResult(.call, ret_type, .{ .{ .func_name = func_name }, .{ .call_args = args } });
    }

    // Parameters
    pub fn emitParam(self: *Function, param_index: i32, ty: Type) !Value {
        return self.emitWithResult(.param, ty, .{ .{ .immediate_i32 = param_index }, .none });
    }

    // Memory copy
    pub fn emitMemcpy(self: *Function, dest: Value, src: Value, size: i32) !void {
        try self.emit(.{
            .op = .memcpy,
            .operands = .{ .{ .value = dest }, .{ .value = src } },
            .result_type = .void,
            .result = @intCast(size), // Store size in result field
        });
    }

    // Memory set (typically used to zero memory)
    pub fn emitMemset(self: *Function, dest: Value, byte_value: u8, size: i32) !void {
        try self.emit(.{
            .op = .memset,
            .operands = .{ .{ .value = dest }, .{ .immediate_i32 = @intCast(byte_value) } },
            .result_type = .void,
            .result = @intCast(size), // Store size in result field
        });
    }

    // Heap allocation
    pub fn emitHeapAlloc(self: *Function, size: Value) !Value {
        return self.emitWithResult(.heap_alloc, .ptr, .{ .{ .value = size }, .none });
    }

    pub fn emitHeapFree(self: *Function, ptr: Value) !void {
        try self.emit(.{ .op = .heap_free, .operands = .{ .{ .value = ptr }, .none } });
    }

    pub fn emitHeapRealloc(self: *Function, old_ptr: Value, new_size: Value) !Value {
        return self.emitWithResult(.heap_realloc, .ptr, .{ .{ .value = old_ptr }, .{ .value = new_size } });
    }
};

/// IR Module
pub const Module = struct {
    functions: std.ArrayListUnmanaged(Function),
    allocator: std.mem.Allocator,
    // Allocated strings that must be freed when module is deinitialized
    allocated_strings: std.ArrayListUnmanaged([]const u8) = .empty,

    pub fn init(allocator: std.mem.Allocator) Module {
        return .{
            .functions = .empty,
            .allocator = allocator,
            .allocated_strings = .empty,
        };
    }

    pub fn deinit(self: *Module) void {
        for (self.functions.items) |*func| {
            func.deinit();
        }
        self.functions.deinit(self.allocator);

        // Free allocated strings (e.g., mangled method names)
        for (self.allocated_strings.items) |s| {
            self.allocator.free(s);
        }
        self.allocated_strings.deinit(self.allocator);
    }

    /// Track an allocated string for cleanup when module is deinitialized
    pub fn trackString(self: *Module, s: []const u8) !void {
        try self.allocated_strings.append(self.allocator, s);
    }

    pub fn addFunction(self: *Module, name: []const u8, return_type: Type) !*Function {
        return self.addFunctionWithExport(name, return_type, false);
    }

    pub fn addFunctionWithExport(self: *Module, name: []const u8, return_type: Type, is_exported: bool) !*Function {
        try self.functions.append(self.allocator, Function.initWithExport(self.allocator, name, return_type, is_exported));
        return &self.functions.items[self.functions.items.len - 1];
    }

    pub fn getFunction(self: *const Module, name: []const u8) ?*const Function {
        for (self.functions.items) |*func| {
            if (std.mem.eql(u8, func.name, name)) {
                return func;
            }
        }
        return null;
    }

    /// Print IR to a writer
    pub fn print(self: *const Module, writer: anytype) !void {
        for (self.functions.items) |func| {
            try writer.print("function {s}() -> {s} {{\n", .{ func.name, func.return_type.format() });

            for (func.blocks.items) |block| {
                try writer.print("{s}:\n", .{block.name});
                debug.ir("Block {s} has {d} instructions", .{ block.name, block.instructions.items.len });

                for (block.instructions.items, 0..) |inst, idx| {
                    debug.ir("  Instruction {d}: op={s}", .{ idx, @tagName(inst.op) });
                    try writer.writeAll("    ");
                    try printInstruction(writer, inst, &func);
                    try writer.writeAll("\n");
                }
            }

            try writer.writeAll("}\n\n");
        }
    }

    /// Print IR to string
    pub fn printToString(self: *const Module, allocator: std.mem.Allocator) ![]u8 {
        var list: std.ArrayListUnmanaged(u8) = .empty;
        errdefer list.deinit(allocator);
        try self.print(list.writer(allocator));
        return list.toOwnedSlice(allocator);
    }

    /// Merge another module's functions into this module.
    /// Functions from the other module are moved (not copied).
    /// Duplicate function names are skipped (first definition wins).
    /// If exports_only is true, only exported functions are included.
    pub fn merge(self: *Module, other: *Module) !void {
        return self.mergeWithOptions(other, .{});
    }

    pub const MergeOptions = struct {
        exports_only: bool = false,
    };

    pub fn mergeWithOptions(self: *Module, other: *Module, options: MergeOptions) !void {
        for (other.functions.items) |*func| {
            // Skip non-exported functions if exports_only is set
            if (options.exports_only and !func.is_exported) {
                // Free the skipped function's resources
                func.deinit();
                continue;
            }

            // Skip if we already have a function with this name
            var exists = false;
            for (self.functions.items) |existing| {
                if (std.mem.eql(u8, existing.name, func.name)) {
                    exists = true;
                    break;
                }
            }
            if (!exists) {
                try self.functions.append(self.allocator, func.*);
            } else {
                // Free the duplicate function's resources
                func.deinit();
            }
        }
        // Clear other's functions list without freeing items (they were moved or freed)
        other.functions.items.len = 0;

        // Move allocated strings from other module to this one
        for (other.allocated_strings.items) |s| {
            try self.allocated_strings.append(self.allocator, s);
        }
        other.allocated_strings.items.len = 0;
    }
};

fn printInstruction(writer: anytype, inst: Instruction, func: *const Function) !void {
    // Print result if present
    if (inst.result) |r| {
        if (func.getValueName(r)) |name| {
            try writer.print("%{s} = ", .{name});
        } else {
            try writer.print("%{d} = ", .{r});
        }
    }

    // Print opcode
    try writer.writeAll(inst.op.format());

    // Print type for typed operations
    if (inst.result != null and inst.result_type != .void) {
        try writer.print(" {s}", .{inst.result_type.format()});
    }

    // Print operands
    for (inst.operands) |op| {
        switch (op) {
            .none => {},
            .value => |v| {
                if (func.getValueName(v)) |name| {
                    try writer.print(" %{s}", .{name});
                } else {
                    try writer.print(" %{d}", .{v});
                }
            },
            .immediate_i32 => |i| try writer.print(" {d}", .{i}),
            .immediate_i64 => |i| try writer.print(" {d}", .{i}),
            .immediate_f64 => |f| try writer.print(" {d}", .{f}),
            .block_ref => |b| try writer.print(" @block{d}", .{b}),
            .func_name => |n| try writer.print(" @{s}", .{n}),
            .call_args => |args| {
                try writer.writeAll("(");
                for (args, 0..) |arg, i| {
                    if (i > 0) try writer.writeAll(", ");
                    if (func.getValueName(arg)) |name| {
                        try writer.print("%{s}", .{name});
                    } else {
                        try writer.print("%{d}", .{arg});
                    }
                }
                try writer.writeAll(")");
            },
            .elem_size => |size| try writer.print(" elemsize={d}", .{size}),
        }
    }
}
