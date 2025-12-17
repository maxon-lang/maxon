const std = @import("std");
const ast = @import("ast.zig");
const ir = @import("ir.zig");

/// Converts AST to IR
pub const AstToIr = struct {
    allocator: std.mem.Allocator,
    module: ir.Module,
    current_func: ?*ir.Function,
    // Map variable names to their alloca values (pointers)
    var_map: std.StringHashMapUnmanaged(ir.Value),

    pub fn init(allocator: std.mem.Allocator) AstToIr {
        return .{
            .allocator = allocator,
            .module = ir.Module.init(allocator),
            .current_func = null,
            .var_map = .{},
        };
    }

    pub fn deinit(self: *AstToIr) void {
        self.var_map.deinit(self.allocator);
    }

    pub fn convert(self: *AstToIr, program: ast.Program) !ir.Module {
        for (program.functions) |func| {
            try self.convertFunction(func);
        }

        // Transfer ownership
        const module = self.module;
        self.module = ir.Module.init(self.allocator);
        return module;
    }

    fn convertFunction(self: *AstToIr, func: ast.FunctionDecl) !void {
        // Determine return type
        const ret_type: ir.Type = if (func.return_type) |rt| blk: {
            if (std.mem.eql(u8, rt, "int")) {
                break :blk .i32;
            }
            break :blk .void;
        } else .void;

        // Create function
        const ir_func = try self.module.addFunction(func.name, ret_type);
        self.current_func = ir_func;

        // Clear variable map
        self.var_map.clearRetainingCapacity();

        // Create entry block
        _ = try ir_func.addBlock("entry");

        // Convert body
        for (func.body) |stmt| {
            try self.convertStatement(stmt);
        }
    }

    fn convertStatement(self: *AstToIr, stmt: ast.Statement) !void {
        switch (stmt) {
            .let_decl, .var_decl => |decl| {
                try self.convertVarDecl(decl);
            },
            .@"return" => |ret| {
                try self.convertReturn(ret);
            },
        }
    }

    fn convertVarDecl(self: *AstToIr, decl: ast.VarDecl) !void {
        const func = self.current_func.?;

        // Allocate stack slot
        const ptr = try func.emitAlloca(.i32);

        // Convert initializer
        const init_val = try self.convertExpression(decl.value);

        // Store value
        try func.emitStore(ptr, init_val);

        // Record variable location
        try self.var_map.put(self.allocator, decl.name, ptr);
    }

    fn convertReturn(self: *AstToIr, ret: ast.ReturnStmt) !void {
        const func = self.current_func.?;

        if (ret.value) |expr| {
            const val = try self.convertExpression(expr);
            try func.emitRet(val);
        } else {
            try func.emitRet(null);
        }
    }

    fn convertExpression(self: *AstToIr, expr: ast.Expression) !ir.Value {
        const func = self.current_func.?;

        switch (expr) {
            .integer => |value| {
                return try func.emitConstI32(@intCast(value));
            },
            .identifier => |name| {
                if (self.var_map.get(name)) |ptr| {
                    return try func.emitLoad(ptr, .i32);
                } else {
                    return error.UndefinedVariable;
                }
            },
        }
    }
};

pub fn convert(program: ast.Program, allocator: std.mem.Allocator) !ir.Module {
    var converter = AstToIr.init(allocator);
    defer converter.deinit();
    return try converter.convert(program);
}
