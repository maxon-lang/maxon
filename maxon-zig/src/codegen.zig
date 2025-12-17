const std = @import("std");
const ast = @import("ast.zig");

pub const Codegen = struct {
    allocator: std.mem.Allocator,

    pub fn init(allocator: std.mem.Allocator) Codegen {
        return .{
            .allocator = allocator,
        };
    }

    pub fn generate(self: *Codegen, program: ast.Program) ![]u8 {
        var code: std.ArrayListUnmanaged(u8) = .empty;
        errdefer code.deinit(self.allocator);

        // Find main function
        for (program.functions) |func| {
            if (std.mem.eql(u8, func.name, "main")) {
                try self.generateFunction(&code, func);
                break;
            }
        }

        return code.toOwnedSlice(self.allocator);
    }

    fn generateFunction(self: *Codegen, code: *std.ArrayListUnmanaged(u8), func: ast.FunctionDecl) !void {
        // Windows x64 calling convention requires:
        // 1. 16-byte stack alignment
        // 2. 32-byte shadow space for callee

        // sub rsp, 40  (32 shadow + 8 for alignment since call pushes 8)
        // 48 83 EC 28
        try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x83, 0xEC, 0x28 });

        // Generate function body
        for (func.body) |stmt| {
            switch (stmt) {
                .@"return" => |ret| {
                    if (ret.value) |expr| {
                        switch (expr) {
                            .integer => |value| {
                                // mov ecx, value  (Windows x64: first arg in rcx, but ecx works for 32-bit values)
                                try code.append(self.allocator, 0xB9); // mov ecx, imm32
                                const val32: u32 = @intCast(value);
                                try code.appendSlice(self.allocator, &@as([4]u8, @bitCast(val32)));

                                // call [rip + offset] to ExitProcess
                                // FF 15 xx xx xx xx
                                try code.append(self.allocator, 0xFF);
                                try code.append(self.allocator, 0x15);
                                // Offset will be filled in by PE writer (relative to next instruction)
                                try code.appendSlice(self.allocator, &[4]u8{ 0, 0, 0, 0 });
                            },
                        }
                    }
                },
            }
        }
    }
};
