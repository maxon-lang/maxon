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

        // Control flow
        ret,
        br,
        br_cond,

        // Comparison
        icmp_eq,
        icmp_ne,
        icmp_lt,
        icmp_le,
        icmp_gt,
        icmp_ge,

        // Function call
        call,

        // Parameters
        param,

        // Array operations
        getelemptr, // Get pointer to array element

        // Memory operations
        memcpy, // Copy memory: dest, src, size

        // Heap allocation
        heap_alloc, // Allocate heap memory, returns ptr
        heap_free, // Free heap memory

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
                .ret => "ret",
                .br => "br",
                .br_cond => "br.cond",
                .icmp_eq => "icmp.eq",
                .icmp_ne => "icmp.ne",
                .icmp_lt => "icmp.lt",
                .icmp_le => "icmp.le",
                .icmp_gt => "icmp.gt",
                .icmp_ge => "icmp.ge",
                .call => "call",
                .param => "param",
                .getelemptr => "getelemptr",
                .memcpy => "memcpy",
                .heap_alloc => "heap.alloc",
                .heap_free => "heap.free",
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
    blocks: std.ArrayListUnmanaged(BasicBlock),
    next_value: Value,
    allocator: std.mem.Allocator,

    pub fn init(allocator: std.mem.Allocator, name: []const u8, return_type: Type) Function {
        return .{
            .name = name,
            .return_type = return_type,
            .blocks = .empty,
            .next_value = 0,
            .allocator = allocator,
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

    pub fn emit(self: *Function, inst: Instruction) !void {
        if (self.currentBlock()) |block| {
            try block.instructions.append(self.allocator, inst);
        }
    }

    // Core emit helpers
    fn emitWithResult(self: *Function, op: Instruction.Op, result_type: Type, operands: [2]Instruction.Operand) !Value {
        const result = self.newValue();
        try self.emit(.{ .op = op, .result = result, .result_type = result_type, .operands = operands });
        return result;
    }

    fn emitBinaryOp(self: *Function, op: Instruction.Op, lhs: Value, rhs: Value, ty: Type) !Value {
        return self.emitWithResult(op, ty, .{ .{ .value = lhs }, .{ .value = rhs } });
    }

    fn emitUnaryOp(self: *Function, op: Instruction.Op, src: Value, ty: Type) !Value {
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
        return self.emitWithResult(.alloca_sized, .ptr, .{ .{ .immediate_i32 = size_bytes }, .none });
    }

    pub fn emitAllocaDynamic(self: *Function, size_value: Value) !Value {
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
        // We use a GetElemPtrInfo struct to store all needed data
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

    // Integer arithmetic
    pub fn emitAdd(self: *Function, lhs: Value, rhs: Value, ty: Type) !Value {
        return self.emitBinaryOp(.add, lhs, rhs, ty);
    }

    pub fn emitSub(self: *Function, lhs: Value, rhs: Value, ty: Type) !Value {
        return self.emitBinaryOp(.sub, lhs, rhs, ty);
    }

    pub fn emitMul(self: *Function, lhs: Value, rhs: Value, ty: Type) !Value {
        return self.emitBinaryOp(.mul, lhs, rhs, ty);
    }

    pub fn emitDiv(self: *Function, lhs: Value, rhs: Value, ty: Type) !Value {
        return self.emitBinaryOp(.div, lhs, rhs, ty);
    }

    pub fn emitMod(self: *Function, lhs: Value, rhs: Value, ty: Type) !Value {
        return self.emitBinaryOp(.mod, lhs, rhs, ty);
    }

    // Float arithmetic
    pub fn emitFAdd(self: *Function, lhs: Value, rhs: Value) !Value {
        return self.emitBinaryOp(.fadd, lhs, rhs, .f64);
    }

    pub fn emitFSub(self: *Function, lhs: Value, rhs: Value) !Value {
        return self.emitBinaryOp(.fsub, lhs, rhs, .f64);
    }

    pub fn emitFMul(self: *Function, lhs: Value, rhs: Value) !Value {
        return self.emitBinaryOp(.fmul, lhs, rhs, .f64);
    }

    pub fn emitFDiv(self: *Function, lhs: Value, rhs: Value) !Value {
        return self.emitBinaryOp(.fdiv, lhs, rhs, .f64);
    }

    // Conversions
    pub fn emitFpToSi(self: *Function, value: Value, dest_type: Type) !Value {
        return self.emitUnaryOp(.fptosi, value, dest_type);
    }

    pub fn emitSiToFp(self: *Function, value: Value, dest_type: Type) !Value {
        return self.emitUnaryOp(.sitofp, value, dest_type);
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

    // Heap allocation
    pub fn emitHeapAlloc(self: *Function, size: Value) !Value {
        return self.emitWithResult(.heap_alloc, .ptr, .{ .{ .value = size }, .none });
    }

    pub fn emitHeapFree(self: *Function, ptr: Value) !void {
        try self.emit(.{ .op = .heap_free, .operands = .{ .{ .value = ptr }, .none } });
    }
};

/// IR Module
pub const Module = struct {
    functions: std.ArrayListUnmanaged(Function),
    allocator: std.mem.Allocator,

    pub fn init(allocator: std.mem.Allocator) Module {
        return .{
            .functions = .empty,
            .allocator = allocator,
        };
    }

    pub fn deinit(self: *Module) void {
        for (self.functions.items) |*func| {
            func.deinit();
        }
        self.functions.deinit(self.allocator);
    }

    pub fn addFunction(self: *Module, name: []const u8, return_type: Type) !*Function {
        try self.functions.append(self.allocator, Function.init(self.allocator, name, return_type));
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
                    try printInstruction(writer, inst);
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
};

fn printInstruction(writer: anytype, inst: Instruction) !void {
    // Print result if present
    if (inst.result) |r| {
        try writer.print("%{d} = ", .{r});
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
            .value => |v| try writer.print(" %{d}", .{v}),
            .immediate_i32 => |i| try writer.print(" {d}", .{i}),
            .immediate_i64 => |i| try writer.print(" {d}", .{i}),
            .immediate_f64 => |f| try writer.print(" {d}", .{f}),
            .block_ref => |b| try writer.print(" @block{d}", .{b}),
            .func_name => |n| try writer.print(" @{s}", .{n}),
            .call_args => |args| {
                try writer.writeAll("(");
                for (args, 0..) |arg, i| {
                    if (i > 0) try writer.writeAll(", ");
                    try writer.print("%{d}", .{arg});
                }
                try writer.writeAll(")");
            },
            .elem_size => |size| try writer.print(" elemsize={d}", .{size}),
        }
    }
}
