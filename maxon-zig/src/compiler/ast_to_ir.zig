const std = @import("std");
const ast = @import("ast.zig");
const ir = @import("ir.zig");
const debug = @import("debug.zig");

/// Typed value - tracks IR value with its type
const TypedValue = struct {
    value: ir.Value,
    ty: ir.Type,
};

/// Converts AST to IR
pub const AstToIr = struct {
    allocator: std.mem.Allocator,
    module: ir.Module,
    current_func: ?*ir.Function,
    // Map variable names to their alloca values (pointers) and types
    var_map: std.StringHashMapUnmanaged(VarInfo),

    const VarInfo = struct {
        ptr: ir.Value,
        ty: ir.Type,
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
            if (std.mem.eql(u8, rt, "float")) {
                break :blk .f64;
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

        debug.astToIr("Converting var decl: {s}", .{decl.name});

        // Convert initializer first to infer type
        const init_typed = try self.convertExpression(decl.value);
        debug.astToIr("  Init value: %{d}, type: {s}", .{ init_typed.value, init_typed.ty.format() });

        // Allocate stack slot with the appropriate type
        const ptr = try func.emitAlloca(init_typed.ty);
        debug.astToIr("  Alloca: %{d}", .{ptr});

        // Store value
        try func.emitStore(ptr, init_typed.value);

        // Record variable location with type (not yet used)
        try self.var_map.put(self.allocator, decl.name, .{ .ptr = ptr, .ty = init_typed.ty, .used = false });
    }

    fn convertReturn(self: *AstToIr, ret: ast.ReturnStmt) !void {
        const func = self.current_func.?;

        if (ret.value) |expr| {
            const typed_val = try self.convertExpression(expr);
            // If returning an int but we have float, need to convert
            if (func.return_type == .i64 and typed_val.ty == .f64) {
                const converted = try func.emitFpToSi(typed_val.value, .i64);
                try func.emitRet(converted);
            } else {
                try func.emitRet(typed_val.value);
            }
        } else {
            try func.emitRet(null);
        }
    }

    const ConvertError = error{ OutOfMemory, UndefinedVariable, FloatModNotSupported, WrongArgumentCount, ExpectedFloat };

    fn convertExpression(self: *AstToIr, expr: ast.Expression) ConvertError!TypedValue {
        const func = self.current_func.?;

        switch (expr) {
            .integer => |value| {
                const val = try func.emitConstI64(value);
                return .{ .value = val, .ty = .i64 };
            },
            .float_lit => |value| {
                const val = try func.emitConstF64(value);
                return .{ .value = val, .ty = .f64 };
            },
            .identifier => |name| {
                if (self.var_map.getPtr(name)) |info| {
                    info.used = true;
                    const val = try func.emitLoad(info.ptr, info.ty);
                    return .{ .value = val, .ty = info.ty };
                } else {
                    return error.UndefinedVariable;
                }
            },
            .binary => |bin| {
                const left = try self.convertExpression(bin.left.*);
                const right = try self.convertExpression(bin.right.*);

                // Determine result type - if either operand is float, use float
                const result_ty: ir.Type = if (left.ty == .f64 or right.ty == .f64) .f64 else .i64;

                // Convert operands if needed
                const left_val = if (result_ty == .f64 and left.ty == .i64)
                    try func.emitSiToFp(left.value, .f64)
                else
                    left.value;
                const right_val = if (result_ty == .f64 and right.ty == .i64)
                    try func.emitSiToFp(right.value, .f64)
                else
                    right.value;

                const result = if (result_ty == .f64) switch (bin.op) {
                    .add => try func.emitFAdd(left_val, right_val),
                    .sub => try func.emitFSub(left_val, right_val),
                    .mul => try func.emitFMul(left_val, right_val),
                    .div => try func.emitFDiv(left_val, right_val),
                    .mod => return error.FloatModNotSupported,
                } else switch (bin.op) {
                    .add => try func.emitAdd(left_val, right_val, .i64),
                    .sub => try func.emitSub(left_val, right_val, .i64),
                    .mul => try func.emitMul(left_val, right_val, .i64),
                    .div => try func.emitDiv(left_val, right_val, .i64),
                    .mod => try func.emitMod(left_val, right_val, .i64),
                };

                return .{ .value = result, .ty = result_ty };
            },
            .call => |call| {
                return try self.convertCall(call);
            },
        }
    }

    fn convertCall(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        const func = self.current_func.?;

        // Handle built-in functions
        if (std.mem.eql(u8, call.func_name, "trunc")) {
            // trunc(float) -> int
            if (call.args.len != 1) {
                return error.WrongArgumentCount;
            }
            const arg = try self.convertExpression(call.args[0]);
            if (arg.ty != .f64) {
                return error.ExpectedFloat;
            }
            const result = try func.emitFpToSi(arg.value, .i64);
            return .{ .value = result, .ty = .i64 };
        }

        // For other functions, emit a call instruction
        // For now, assume all user functions return i64
        const result = try func.emitCall(call.func_name, .i64);
        return .{ .value = result orelse 0, .ty = .i64 };
    }
};

pub fn convert(program: ast.Program, allocator: std.mem.Allocator) !ir.Module {
    var converter = AstToIr.init(allocator);
    defer converter.deinit();
    return try converter.convert(program);
}
