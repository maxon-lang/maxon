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
            .const_i32 => {
                const result = inst.result.?;
                const value = inst.operands[0].immediate_i32;
                // mov eax, imm32
                try code.append(self.allocator, 0xB8);
                try code.appendSlice(self.allocator, &@as([4]u8, @bitCast(value)));
                try self.value_locations.put(self.allocator, result, .{ .register = .rax });
            },

            .alloca => {
                const result = inst.result.?;
                const offset = self.next_stack_offset;
                self.next_stack_offset -= 8;
                try self.value_locations.put(self.allocator, result, .{ .stack = offset });
            },

            .store => {
                const ptr = inst.operands[0].value;
                const ptr_loc = self.value_locations.get(ptr) orelse return error.InvalidValue;
                const offset = switch (ptr_loc) {
                    .stack => |o| o,
                    else => return error.InvalidStore,
                };
                // mov [rbp + offset], eax
                try code.appendSlice(self.allocator, &[_]u8{ 0x89, 0x45 });
                try code.append(self.allocator, @bitCast(@as(i8, @intCast(offset))));
            },

            .load => {
                const result = inst.result.?;
                const ptr = inst.operands[0].value;
                const ptr_loc = self.value_locations.get(ptr) orelse return error.InvalidValue;
                const offset = switch (ptr_loc) {
                    .stack => |o| o,
                    else => return error.InvalidLoad,
                };
                // mov eax, [rbp + offset]
                try code.appendSlice(self.allocator, &[_]u8{ 0x8B, 0x45 });
                try code.append(self.allocator, @bitCast(@as(i8, @intCast(offset))));
                try self.value_locations.put(self.allocator, result, .{ .register = .rax });
            },

            .ret => {
                if (inst.operands[0] != .none) {
                    // mov ecx, eax
                    try code.appendSlice(self.allocator, &[_]u8{ 0x89, 0xC1 });
                    // call [rip + offset] to ExitProcess
                    try code.append(self.allocator, 0xFF);
                    try code.append(self.allocator, 0x15);
                    try code.appendSlice(self.allocator, &[4]u8{ 0, 0, 0, 0 });
                }
            },

            else => {},
        }
    }
};

pub fn generate(module: ir.Module, allocator: std.mem.Allocator) ![]u8 {
    var codegen = IrCodegen.init(allocator);
    defer codegen.deinit();
    return try codegen.generate(module);
}
