const std = @import("std");
const ir = @import("ir.zig");

/// x86-64 code generator from IR
pub const IrCodegen = struct {
    allocator: std.mem.Allocator,
    value_locations: std.AutoHashMapUnmanaged(ir.Value, ValueLocation),
    next_stack_offset: i32,

    const ValueLocation = union(enum) {
        stack: i32, // offset from rbp
        register: Register,
    };

    const Register = enum { rax, rcx, rdx, r8, r9 };

    pub fn init(allocator: std.mem.Allocator) IrCodegen {
        return .{
            .allocator = allocator,
            .value_locations = .{},
            .next_stack_offset = -8,
        };
    }

    pub fn deinit(self: *IrCodegen) void {
        self.value_locations.deinit(self.allocator);
    }

    pub fn generate(self: *IrCodegen, module: ir.Module) ![]u8 {
        var code: std.ArrayListUnmanaged(u8) = .empty;
        errdefer code.deinit(self.allocator);

        // Generate main function
        if (module.getFunction("main")) |func| {
            try self.generateFunction(&code, func);
        }

        return code.toOwnedSlice(self.allocator);
    }

    fn generateFunction(self: *IrCodegen, code: *std.ArrayListUnmanaged(u8), func: *const ir.Function) !void {
        self.value_locations.clearRetainingCapacity();
        self.next_stack_offset = -8;

        // Function prologue
        // push rbp
        try code.append(self.allocator, 0x55);
        // mov rbp, rsp
        try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x89, 0xE5 });
        // sub rsp, 48
        try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x83, 0xEC, 0x30 });

        // Generate blocks
        for (func.blocks.items) |block| {
            for (block.instructions.items) |inst| {
                try self.generateInstruction(code, inst);
            }
        }
    }

    fn generateInstruction(self: *IrCodegen, code: *std.ArrayListUnmanaged(u8), inst: ir.Instruction) !void {
        switch (inst.op) {
            .const_i64 => try self.genConstI64(code, inst),
            .alloca => try self.genAlloca(inst),
            .store => try self.genStore(code, inst),
            .load => try self.genLoad(code, inst),
            .add, .sub, .mul, .div, .mod => try self.genBinaryOp(code, inst),
            .ret => try self.genRet(code, inst),
            else => {},
        }
    }

    fn genConstI64(self: *IrCodegen, code: *std.ArrayListUnmanaged(u8), inst: ir.Instruction) !void {
        const result = inst.result.?;
        const value = inst.operands[0].immediate_i64;
        const offset = self.allocStackSlot();
        // mov rax, imm64
        try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0xB8 });
        try code.appendSlice(self.allocator, &@as([8]u8, @bitCast(value)));
        // mov qword [rbp + offset], rax
        try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x89, 0x45 });
        try code.append(self.allocator, @bitCast(@as(i8, @intCast(offset))));
        try self.value_locations.put(self.allocator, result, .{ .stack = offset });
    }

    fn genAlloca(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;
        const offset = self.allocStackSlot();
        try self.value_locations.put(self.allocator, result, .{ .stack = offset });
    }

    fn genStore(self: *IrCodegen, code: *std.ArrayListUnmanaged(u8), inst: ir.Instruction) !void {
        const ptr = inst.operands[0].value;
        const offset = try self.getStackOffset(ptr);
        // mov qword [rbp + offset], rax
        try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x89, 0x45 });
        try code.append(self.allocator, @bitCast(@as(i8, @intCast(offset))));
    }

    fn genLoad(self: *IrCodegen, code: *std.ArrayListUnmanaged(u8), inst: ir.Instruction) !void {
        const result = inst.result.?;
        const ptr = inst.operands[0].value;
        const offset = try self.getStackOffset(ptr);
        // mov rax, qword [rbp + offset]
        try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x8B, 0x45 });
        try code.append(self.allocator, @bitCast(@as(i8, @intCast(offset))));
        try self.value_locations.put(self.allocator, result, .{ .register = .rax });
    }

    fn genBinaryOp(self: *IrCodegen, code: *std.ArrayListUnmanaged(u8), inst: ir.Instruction) !void {
        const result = inst.result.?;
        const lhs = inst.operands[0].value;
        const rhs = inst.operands[1].value;

        // Load operands: lhs -> rax, rhs -> rcx
        try self.loadValueToRax(code, lhs);
        try code.append(self.allocator, 0x50); // push rax
        try self.loadValueToRax(code, rhs);
        try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x89, 0xC1 }); // mov rcx, rax
        try code.append(self.allocator, 0x58); // pop rax

        // Emit 64-bit operation
        switch (inst.op) {
            .add => try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x01, 0xC8 }), // add rax, rcx
            .sub => try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x29, 0xC8 }), // sub rax, rcx
            .mul => try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x0F, 0xAF, 0xC1 }), // imul rax, rcx
            .div => {
                try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x99 }); // cqo (sign extend rax into rdx:rax)
                try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0xF7, 0xF9 }); // idiv rcx
            },
            .mod => {
                try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x99 }); // cqo (sign extend rax into rdx:rax)
                try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0xF7, 0xF9 }); // idiv rcx
                try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x89, 0xD0 }); // mov rax, rdx (remainder)
            },
            else => unreachable,
        }

        try self.value_locations.put(self.allocator, result, .{ .register = .rax });
    }

    fn genRet(self: *IrCodegen, code: *std.ArrayListUnmanaged(u8), inst: ir.Instruction) !void {
        if (inst.operands[0] != .none) {
            const val = inst.operands[0].value;
            try self.loadValueToRax(code, val);
            try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x89, 0xC1 }); // mov rcx, rax
            // call [rip + offset] to ExitProcess
            try code.append(self.allocator, 0xFF);
            try code.append(self.allocator, 0x15);
            try code.appendSlice(self.allocator, &[4]u8{ 0, 0, 0, 0 });
        }
    }

    fn allocStackSlot(self: *IrCodegen) i32 {
        const offset = self.next_stack_offset;
        self.next_stack_offset -= 8;
        return offset;
    }

    fn getStackOffset(self: *IrCodegen, val: ir.Value) !i32 {
        const loc = self.value_locations.get(val) orelse return error.InvalidValue;
        return switch (loc) {
            .stack => |o| o,
            else => error.ExpectedStackLocation,
        };
    }

    fn loadValueToRax(self: *IrCodegen, code: *std.ArrayListUnmanaged(u8), val: ir.Value) !void {
        const loc = self.value_locations.get(val) orelse return error.InvalidValue;
        switch (loc) {
            .register => |reg| {
                if (reg != .rax) {
                    // mov rax, reg
                    switch (reg) {
                        .rcx => try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x89, 0xC8 }),
                        .rdx => try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x89, 0xD0 }),
                        else => return error.UnsupportedRegister,
                    }
                }
            },
            .stack => |offset| {
                // mov rax, qword [rbp + offset]
                try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x8B, 0x45 });
                try code.append(self.allocator, @bitCast(@as(i8, @intCast(offset))));
            },
        }
    }
};

pub fn generate(module: ir.Module, allocator: std.mem.Allocator) ![]u8 {
    var codegen = IrCodegen.init(allocator);
    defer codegen.deinit();
    return try codegen.generate(module);
}
