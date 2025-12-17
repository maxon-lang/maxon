const std = @import("std");
const ast = @import("ast.zig");
const ir = @import("ir.zig");

/// Converts AST to IR
pub const AstToIr = struct {
    allocator: std.mem.Allocator,
    module: ir.Module,
    current_func: ?*ir.Function,
    // Map variable names to their alloca values (pointers)
    var_map: std.StringHashMapUnmanaged(VarInfo),

    const VarInfo = struct {
        ptr: ir.Value,
        used: bool,
    };

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
                break :blk .i64;
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

        // Check for unused variables
        var iter = self.var_map.iterator();
        while (iter.next()) |entry| {
            if (!entry.value_ptr.used) {
                std.debug.print("error: unused variable '{s}'\n", .{entry.key_ptr.*});
                return error.UnusedVariable;
            }
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
        const ptr = try func.emitAlloca(.i64);

        // Convert initializer
        const init_val = try self.convertExpression(decl.value);

        // Store value
        try func.emitStore(ptr, init_val);

        // Record variable location (not yet used)
        try self.var_map.put(self.allocator, decl.name, .{ .ptr = ptr, .used = false });
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
                return try func.emitConstI64(value);
            },
            .identifier => |name| {
                if (self.var_map.getPtr(name)) |info| {
                    info.used = true;
                    return try func.emitLoad(info.ptr, .i64);
                } else {
                    return error.UndefinedVariable;
                }
            },
            .binary => |bin| {
                const left = try self.convertExpression(bin.left.*);
                const right = try self.convertExpression(bin.right.*);
                return switch (bin.op) {
                    .add => try func.emitAdd(left, right, .i64),
                    .sub => try func.emitSub(left, right, .i64),
                    .mul => try func.emitMul(left, right, .i64),
                    .div => try func.emitDiv(left, right, .i64),
                    .mod => try func.emitMod(left, right, .i64),
                };
            },
        }
    }
};

pub fn convert(program: ast.Program, allocator: std.mem.Allocator) !ir.Module {
    var converter = AstToIr.init(allocator);
    defer converter.deinit();
    return try converter.convert(program);
}
