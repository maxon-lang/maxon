const std = @import("std");
const ast = @import("ast.zig");

pub const Codegen = struct {
    allocator: std.mem.Allocator,
    // Map variable names to stack offsets (negative from rbp)
    variables: std.StringHashMapUnmanaged(i32),
    next_stack_offset: i32,

    pub fn init(allocator: std.mem.Allocator) Codegen {
        return .{
            .allocator = allocator,
            .variables = .{},
            .next_stack_offset = -8, // First variable at rbp-8
        };
    }

    pub fn deinit(self: *Codegen) void {
        self.variables.deinit(self.allocator);
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
        // Reset variable tracking for each function
        self.variables.clearAndFree(self.allocator);
        self.next_stack_offset = -8;

        // Windows x64 calling convention requires:
        // 1. 16-byte stack alignment
        // 2. 32-byte shadow space for callee

        // Function prologue:
        // push rbp
        try code.append(self.allocator, 0x55);
        // mov rbp, rsp
        try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x89, 0xE5 });
        // sub rsp, 48  (32 shadow + 16 for locals, keeps 16-byte alignment)
        try code.appendSlice(self.allocator, &[_]u8{ 0x48, 0x83, 0xEC, 0x30 });

        // Generate function body
        for (func.body) |stmt| {
            try self.generateStatement(code, stmt);
        }
    }

    fn generateStatement(self: *Codegen, code: *std.ArrayListUnmanaged(u8), stmt: ast.Statement) !void {
        switch (stmt) {
            .@"return" => |ret| {
                if (ret.value) |expr| {
                    // Generate expression, result in eax/rax
                    try self.generateExpression(code, expr);
                    // mov ecx, eax  (Windows x64: first arg in rcx)
                    try code.appendSlice(self.allocator, &[_]u8{ 0x89, 0xC1 });
                    // call [rip + offset] to ExitProcess
                    try code.append(self.allocator, 0xFF);
                    try code.append(self.allocator, 0x15);
                    // Offset will be filled in by PE writer
                    try code.appendSlice(self.allocator, &[4]u8{ 0, 0, 0, 0 });
                }
            },
            .let_decl, .var_decl => |decl| {
                // Generate expression, result in eax
                try self.generateExpression(code, decl.value);
                // Allocate stack slot for variable
                const offset = self.next_stack_offset;
                try self.variables.put(self.allocator, decl.name, offset);
                self.next_stack_offset -= 8;
                // mov [rbp + offset], eax
                // For offset -8: mov [rbp-8], eax = 89 45 F8
                try code.appendSlice(self.allocator, &[_]u8{ 0x89, 0x45 });
                try code.append(self.allocator, @bitCast(@as(i8, @intCast(offset))));
            },
        }
    }

    fn generateExpression(self: *Codegen, code: *std.ArrayListUnmanaged(u8), expr: ast.Expression) !void {
        switch (expr) {
            .integer => |value| {
                // mov eax, value
                try code.append(self.allocator, 0xB8);
                const val32: u32 = @intCast(value);
                try code.appendSlice(self.allocator, &@as([4]u8, @bitCast(val32)));
            },
            .identifier => |name| {
                // Look up variable
                if (self.variables.get(name)) |offset| {
                    // mov eax, [rbp + offset]
                    // For offset -8: mov eax, [rbp-8] = 8B 45 F8
                    try code.appendSlice(self.allocator, &[_]u8{ 0x8B, 0x45 });
                    try code.append(self.allocator, @bitCast(@as(i8, @intCast(offset))));
                } else {
                    return error.UndefinedVariable;
                }
            },
        }
    }
};
