const std = @import("std");
const ast = @import("ast.zig");
const ir = @import("ir.zig");
const debug = @import("debug.zig");

/// Typed value - tracks IR value with its type
const TypedValue = struct {
    value: ir.Value,
    ty: ValueType,
};

/// Extended type info for struct tracking
const ValueType = union(enum) {
    primitive: ir.Type,
    struct_type: []const u8, // struct type name

    fn toPrimitiveType(self: ValueType) ir.Type {
        return switch (self) {
            .primitive => |p| p,
            .struct_type => .ptr, // structs are represented as pointers
        };
    }
};

/// Struct field info
const FieldInfo = struct {
    name: []const u8,
    ty: ir.Type,
    offset: i32,
};

/// Type info - can be primitive or struct
const TypeInfo = union(enum) {
    primitive: ir.Type,
    struct_type: StructTypeInfo,

    fn irType(self: TypeInfo) ir.Type {
        return switch (self) {
            .primitive => |t| t,
            .struct_type => .ptr,
        };
    }

    fn isStruct(self: TypeInfo) bool {
        return self == .struct_type;
    }
};

/// Struct type info
const StructTypeInfo = struct {
    name: []const u8,
    fields: []const FieldInfo,
    size: i32,
};

/// Function signature info
const FuncInfo = struct {
    return_type: ir.Type,
    return_type_name: ?[]const u8, // struct type name if returning a struct
    param_types: []const ParamType,
};

/// Parameter type info
const ParamType = struct {
    ty: ValueType,
};

/// Enum info - maps member names to their integer values
const EnumInfo = struct {
    members: std.StringHashMapUnmanaged(i64),
};

/// Converts AST to IR
pub const AstToIr = struct {
    allocator: std.mem.Allocator,
    module: ir.Module,
    current_func: ?*ir.Function,
    // Map variable names to their alloca values (pointers) and types
    var_map: std.StringHashMapUnmanaged(VarInfo),
    // Map type names to type info (primitives and structs)
    type_map: std.StringHashMapUnmanaged(TypeInfo),
    // Map function names to signatures
    func_map: std.StringHashMapUnmanaged(FuncInfo),
    // Map enum names to their info
    enum_map: std.StringHashMapUnmanaged(EnumInfo),

    const VarInfo = struct {
        ptr: ir.Value,
        ty: ValueType,
        used: bool,
    };

    pub fn init(allocator: std.mem.Allocator) AstToIr {
        return .{
            .allocator = allocator,
            .module = ir.Module.init(allocator),
            .current_func = null,
            .var_map = .{},
            .type_map = .{},
            .func_map = .{},
            .enum_map = .{},
        };
    }

    pub fn deinit(self: *AstToIr) void {
        self.var_map.deinit(self.allocator);
        var type_iter = self.type_map.iterator();
        while (type_iter.next()) |entry| {
            switch (entry.value_ptr.*) {
                .struct_type => |s| self.allocator.free(s.fields),
                .primitive => {},
            }
        }
        self.type_map.deinit(self.allocator);
        var func_iter = self.func_map.iterator();
        while (func_iter.next()) |entry| {
            self.allocator.free(entry.value_ptr.param_types);
        }
        self.func_map.deinit(self.allocator);
        var enum_iter = self.enum_map.iterator();
        while (enum_iter.next()) |entry| {
            entry.value_ptr.members.deinit(self.allocator);
        }
        self.enum_map.deinit(self.allocator);
    }

    pub fn convert(self: *AstToIr, program: ast.Program) !ir.Module {
        // Register primitive types
        try self.type_map.put(self.allocator, "int", .{ .primitive = .i64 });
        try self.type_map.put(self.allocator, "float", .{ .primitive = .f64 });

        // Register all type declarations
        for (program.types) |type_decl| {
            try self.registerType(type_decl);
        }

        // Register all enum declarations
        for (program.enums) |enum_decl| {
            try self.registerEnum(enum_decl);
        }

        // Second, register all function signatures
        for (program.functions) |func| {
            try self.registerFunction(func);
        }

        // Then convert functions
        for (program.functions) |func| {
            try self.convertFunction(func);
        }

        // Transfer ownership
        const module = self.module;
        self.module = ir.Module.init(self.allocator);
        return module;
    }

    fn registerEnum(self: *AstToIr, enum_decl: ast.EnumDecl) !void {
        var members: std.StringHashMapUnmanaged(i64) = .{};

        for (enum_decl.members, 0..) |member, i| {
            try members.put(self.allocator, member, @intCast(i));
        }

        try self.enum_map.put(self.allocator, enum_decl.name, .{
            .members = members,
        });

        debug.astToIr("Registered enum '{s}' with {d} members", .{ enum_decl.name, enum_decl.members.len });
    }

    fn registerFunction(self: *AstToIr, func: ast.FunctionDecl) !void {
        // Determine return type from type_map
        var ret_type_name: ?[]const u8 = null;
        const ret_type: ir.Type = if (func.return_type) |rt| blk: {
            const type_info = self.type_map.get(rt) orelse return error.UnknownType;
            if (type_info.isStruct()) ret_type_name = rt;
            break :blk type_info.irType();
        } else .void;

        // Build parameter types
        var param_types = try self.allocator.alloc(ParamType, func.params.len);
        for (func.params, 0..) |param, i| {
            const type_info = self.type_map.get(param.type_name) orelse return error.UnknownType;
            if (type_info.isStruct()) {
                param_types[i] = .{ .ty = .{ .struct_type = param.type_name } };
            } else {
                param_types[i] = .{ .ty = .{ .primitive = type_info.irType() } };
            }
        }

        try self.func_map.put(self.allocator, func.name, .{
            .return_type = ret_type,
            .return_type_name = ret_type_name,
            .param_types = param_types,
        });

        debug.astToIr("Registered function '{s}' returning {s}", .{ func.name, ret_type.format() });
    }

    fn registerType(self: *AstToIr, type_decl: ast.TypeDecl) !void {
        var fields = try self.allocator.alloc(FieldInfo, type_decl.fields.len);
        var offset: i32 = 0;

        for (type_decl.fields, 0..) |field, i| {
            const field_type = try self.lookupIrType(field.type_name);
            fields[i] = .{
                .name = field.name,
                .ty = field_type,
                .offset = offset,
            };
            // All types are 8 bytes for simplicity
            offset += 8;
        }

        try self.type_map.put(self.allocator, type_decl.name, .{ .struct_type = .{
            .name = type_decl.name,
            .fields = fields,
            .size = offset,
        } });

        debug.astToIr("Registered type '{s}' with size {d}", .{ type_decl.name, offset });
    }

    fn lookupIrType(self: *AstToIr, name: []const u8) !ir.Type {
        const type_info = self.type_map.get(name) orelse return error.UnknownType;
        return type_info.irType();
    }

    fn convertFunction(self: *AstToIr, func: ast.FunctionDecl) !void {
        // Determine return type from type_map
        const ret_type: ir.Type = if (func.return_type) |rt|
            try self.lookupIrType(rt)
        else
            .void;

        // Create function
        const ir_func = try self.module.addFunction(func.name, ret_type);
        self.current_func = ir_func;

        // Clear variable map
        self.var_map.clearRetainingCapacity();

        // Create entry block
        _ = try ir_func.addBlock("entry");

        // Handle function parameters
        for (func.params, 0..) |param, i| {
            const param_idx: i32 = @intCast(i);
            const type_info = self.type_map.get(param.type_name);

            if (type_info != null and type_info.?.isStruct()) {
                // Struct parameter - emit param instruction (pointer) and register
                const param_val = try ir_func.emitParam(param_idx, .ptr);
                try self.var_map.put(self.allocator, param.name, .{
                    .ptr = param_val,
                    .ty = .{ .struct_type = type_info.?.struct_type.name },
                    .used = false,
                });
            } else {
                // Primitive parameter
                const param_type = try self.lookupIrType(param.type_name);
                const param_val = try ir_func.emitParam(param_idx, param_type);
                // Allocate stack slot and store parameter
                const ptr = try ir_func.emitAlloca(param_type);
                try ir_func.emitStore(ptr, param_val);
                try self.var_map.put(self.allocator, param.name, .{
                    .ptr = ptr,
                    .ty = .{ .primitive = param_type },
                    .used = false,
                });
            }
        }

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

        // For structs, the init_typed.value is already the pointer to the allocated struct
        // We store this pointer directly
        switch (init_typed.ty) {
            .struct_type => |_| {
                // Struct: value is already a pointer to the struct data
                // Just record the variable pointing to this struct
                try self.var_map.put(self.allocator, decl.name, .{
                    .ptr = init_typed.value,
                    .ty = init_typed.ty,
                    .used = false,
                });
            },
            .primitive => |prim_ty| {
                // Primitive: allocate slot and store value
                const ptr = try func.emitAlloca(prim_ty);
                try func.emitStore(ptr, init_typed.value);
                try self.var_map.put(self.allocator, decl.name, .{
                    .ptr = ptr,
                    .ty = init_typed.ty,
                    .used = false,
                });
            },
        }
    }

    fn convertReturn(self: *AstToIr, ret: ast.ReturnStmt) !void {
        const func = self.current_func.?;

        if (ret.value) |expr| {
            const typed_val = try self.convertExpression(expr);
            const prim_ty = typed_val.ty.toPrimitiveType();
            // If returning an int but we have float, need to convert
            if (func.return_type == .i64 and prim_ty == .f64) {
                const converted = try func.emitFpToSi(typed_val.value, .i64);
                try func.emitRet(converted);
            } else {
                try func.emitRet(typed_val.value);
            }
        } else {
            try func.emitRet(null);
        }
    }

    const ConvertError = error{ OutOfMemory, UndefinedVariable, FloatModNotSupported, WrongArgumentCount, ExpectedFloat, UnknownType, UnknownField };

    fn convertExpression(self: *AstToIr, expr: ast.Expression) ConvertError!TypedValue {
        const func = self.current_func.?;

        switch (expr) {
            .integer => |value| {
                const val = try func.emitConstI64(value);
                return .{ .value = val, .ty = .{ .primitive = .i64 } };
            },
            .float_lit => |value| {
                const val = try func.emitConstF64(value);
                return .{ .value = val, .ty = .{ .primitive = .f64 } };
            },
            .identifier => |name| {
                if (self.var_map.getPtr(name)) |info| {
                    info.used = true;
                    // For structs, return the pointer directly
                    // For primitives, load the value
                    switch (info.ty) {
                        .struct_type => {
                            return .{ .value = info.ptr, .ty = info.ty };
                        },
                        .primitive => |prim_ty| {
                            const val = try func.emitLoad(info.ptr, prim_ty);
                            return .{ .value = val, .ty = info.ty };
                        },
                    }
                } else {
                    return error.UndefinedVariable;
                }
            },
            .binary => |bin| {
                const left = try self.convertExpression(bin.left.*);
                const right = try self.convertExpression(bin.right.*);

                const left_prim = left.ty.toPrimitiveType();
                const right_prim = right.ty.toPrimitiveType();

                // Determine result type - if either operand is float, use float
                const result_ty: ir.Type = if (left_prim == .f64 or right_prim == .f64) .f64 else .i64;

                // Convert operands if needed
                const left_val = if (result_ty == .f64 and left_prim == .i64)
                    try func.emitSiToFp(left.value, .f64)
                else
                    left.value;
                const right_val = if (result_ty == .f64 and right_prim == .i64)
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

                return .{ .value = result, .ty = .{ .primitive = result_ty } };
            },
            .call => |call| {
                return try self.convertCall(call);
            },
            .struct_init => |sinit| {
                return try self.convertStructInit(sinit);
            },
            .field_access => |faccess| {
                return try self.convertFieldAccess(faccess);
            },
        }
    }

    fn convertStructInit(self: *AstToIr, sinit: ast.StructInitExpr) ConvertError!TypedValue {
        const func = self.current_func.?;

        // Look up struct type
        const type_info = self.type_map.get(sinit.type_name) orelse {
            return error.UnknownType;
        };
        const struct_info = switch (type_info) {
            .struct_type => |s| s,
            .primitive => return error.UnknownType,
        };

        // Allocate space for the struct
        const struct_ptr = try func.emitAllocaSized(struct_info.size);

        // Initialize each field
        for (sinit.fields) |field_init| {
            // Find field info
            const field_info = for (struct_info.fields) |f| {
                if (std.mem.eql(u8, f.name, field_init.name)) break f;
            } else return error.UnknownField;

            // Get pointer to field
            const field_ptr = try func.emitGetFieldPtr(struct_ptr, field_info.offset);

            // Convert and store field value
            const field_val = try self.convertExpression(field_init.value.*);
            try func.emitStore(field_ptr, field_val.value);
        }

        return .{ .value = struct_ptr, .ty = .{ .struct_type = sinit.type_name } };
    }

    fn convertFieldAccess(self: *AstToIr, faccess: ast.FieldAccessExpr) ConvertError!TypedValue {
        const func = self.current_func.?;

        // Check if base is an identifier that's an enum type (e.g., Colors.Green)
        if (faccess.base.* == .identifier) {
            const base_name = faccess.base.identifier;
            if (self.enum_map.get(base_name)) |enum_info| {
                // Look up the member value
                const member_value = enum_info.members.get(faccess.field_name) orelse return error.UnknownField;
                const val = try func.emitConstI64(member_value);
                return .{ .value = val, .ty = .{ .primitive = .i64 } };
            }
        }

        // Get the base struct pointer
        const base = try self.convertExpression(faccess.base.*);

        // Must be a struct type
        const type_name = switch (base.ty) {
            .struct_type => |name| name,
            .primitive => return error.UnknownType,
        };

        // Look up struct type
        const type_info = self.type_map.get(type_name) orelse return error.UnknownType;
        const struct_info = switch (type_info) {
            .struct_type => |s| s,
            .primitive => return error.UnknownType,
        };

        // Find field info
        const field_info = for (struct_info.fields) |f| {
            if (std.mem.eql(u8, f.name, faccess.field_name)) break f;
        } else return error.UnknownField;

        // Get pointer to field
        const field_ptr = try func.emitGetFieldPtr(base.value, field_info.offset);

        // Load the field value
        const val = try func.emitLoad(field_ptr, field_info.ty);

        return .{ .value = val, .ty = .{ .primitive = field_info.ty } };
    }

    fn convertCall(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        const ir_func = self.current_func.?;

        // Handle built-in functions
        if (std.mem.eql(u8, call.func_name, "trunc")) {
            // trunc(float) -> int
            if (call.args.len != 1) {
                return error.WrongArgumentCount;
            }
            const arg = try self.convertExpression(call.args[0]);
            if (arg.ty.toPrimitiveType() != .f64) {
                return error.ExpectedFloat;
            }
            const result = try ir_func.emitFpToSi(arg.value, .i64);
            return .{ .value = result, .ty = .{ .primitive = .i64 } };
        }

        // Look up user function signature
        const func_info = self.func_map.get(call.func_name) orelse {
            // Unknown function - fall back to i64 return
            const result = try ir_func.emitCall(call.func_name, &.{}, .i64);
            return .{ .value = result orelse 0, .ty = .{ .primitive = .i64 } };
        };

        // Convert arguments - don't free since they're stored in the IR instruction
        const args = try self.allocator.alloc(ir.Value, call.args.len);

        for (call.args, 0..) |arg_expr, i| {
            const arg = try self.convertExpression(arg_expr);
            // For structs, we pass the pointer directly
            args[i] = arg.value;
        }

        // Emit call with arguments
        const result = try ir_func.emitCall(call.func_name, args, func_info.return_type);

        // Return with correct type - struct or primitive
        if (func_info.return_type_name) |struct_name| {
            return .{ .value = result orelse 0, .ty = .{ .struct_type = struct_name } };
        }
        return .{ .value = result orelse 0, .ty = .{ .primitive = func_info.return_type } };
    }
};

pub fn convert(program: ast.Program, allocator: std.mem.Allocator) !ir.Module {
    var converter = AstToIr.init(allocator);
    defer converter.deinit();
    return try converter.convert(program);
}
