const std = @import("std");

/// IR Value reference - %0, %1, etc.
pub const Value = u32;

/// IR Type
pub const Type = enum {
    i32,
    i64,
    void,
    ptr,

    pub fn format(self: Type) []const u8 {
        return switch (self) {
            .i32 => "i32",
            .i64 => "i64",
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

        // Memory
        alloca,
        load,
        store,

        // Arithmetic
        add,
        sub,
        mul,
        div,

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
    };

    pub const Operand = union(enum) {
        none,
        value: Value,
        immediate_i32: i32,
        immediate_i64: i64,
        block_ref: u32,
        func_name: []const u8,
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

    // Instruction builders
    pub fn emitConstI64(self: *Function, value: i64) !Value {
        const result = self.newValue();
        try self.emit(.{
            .op = .const_i64,
            .result = result,
            .result_type = .i64,
            .operands = .{ .{ .immediate_i64 = value }, .none },
        });
        return result;
    }

    pub fn emitAlloca(self: *Function, ty: Type) !Value {
        const result = self.newValue();
        try self.emit(.{
            .op = .alloca,
            .result = result,
            .result_type = .ptr,
            .operands = .{ .none, .none },
        });
        _ = ty; // Type info for debugging
        return result;
    }

    pub fn emitLoad(self: *Function, ptr: Value, ty: Type) !Value {
        const result = self.newValue();
        try self.emit(.{
            .op = .load,
            .result = result,
            .result_type = ty,
            .operands = .{ .{ .value = ptr }, .none },
        });
        return result;
    }

    pub fn emitStore(self: *Function, ptr: Value, value: Value) !void {
        try self.emit(.{
            .op = .store,
            .result = null,
            .result_type = .void,
            .operands = .{ .{ .value = ptr }, .{ .value = value } },
        });
    }

    pub fn emitRet(self: *Function, value: ?Value) !void {
        try self.emit(.{
            .op = .ret,
            .result = null,
            .result_type = .void,
            .operands = .{ if (value) |v| .{ .value = v } else .none, .none },
        });
    }

    pub fn emitAdd(self: *Function, lhs: Value, rhs: Value, ty: Type) !Value {
        const result = self.newValue();
        try self.emit(.{
            .op = .add,
            .result = result,
            .result_type = ty,
            .operands = .{ .{ .value = lhs }, .{ .value = rhs } },
        });
        return result;
    }

    pub fn emitSub(self: *Function, lhs: Value, rhs: Value, ty: Type) !Value {
        const result = self.newValue();
        try self.emit(.{
            .op = .sub,
            .result = result,
            .result_type = ty,
            .operands = .{ .{ .value = lhs }, .{ .value = rhs } },
        });
        return result;
    }

    pub fn emitMul(self: *Function, lhs: Value, rhs: Value, ty: Type) !Value {
        const result = self.newValue();
        try self.emit(.{
            .op = .mul,
            .result = result,
            .result_type = ty,
            .operands = .{ .{ .value = lhs }, .{ .value = rhs } },
        });
        return result;
    }

    pub fn emitDiv(self: *Function, lhs: Value, rhs: Value, ty: Type) !Value {
        const result = self.newValue();
        try self.emit(.{
            .op = .div,
            .result = result,
            .result_type = ty,
            .operands = .{ .{ .value = lhs }, .{ .value = rhs } },
        });
        return result;
    }

    pub fn emitCall(self: *Function, func_name: []const u8, ret_type: Type) !?Value {
        const result = if (ret_type != .void) self.newValue() else null;
        try self.emit(.{
            .op = .call,
            .result = result,
            .result_type = ret_type,
            .operands = .{ .{ .func_name = func_name }, .none },
        });
        return result;
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

                for (block.instructions.items) |inst| {
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
    const op_str = switch (inst.op) {
        .const_i32 => "const.i32",
        .const_i64 => "const.i64",
        .alloca => "alloca",
        .load => "load",
        .store => "store",
        .add => "add",
        .sub => "sub",
        .mul => "mul",
        .div => "div",
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
    };
    try writer.writeAll(op_str);

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
            .block_ref => |b| try writer.print(" @block{d}", .{b}),
            .func_name => |n| try writer.print(" @{s}", .{n}),
        }
    }
}
