const std = @import("std");
const ast = @import("ast.zig");
const ir = @import("ir.zig");
const debug = @import("debug.zig");
const mutation_analysis = @import("mutation_analysis.zig");
const err = @import("error.zig");

// ============================================================================
// Type Definitions
// ============================================================================

/// Typed value - tracks IR value with its type
const TypedValue = struct {
    value: ir.Value,
    ty: ValueType,
};

/// Array storage kind
const ArrayStorage = enum {
    stack,
    heap,
};

/// Array type info
const ArrayInfo = struct {
    element_type: ir.Type,
    size: ?usize, // null for dynamic size
    storage: ArrayStorage,
};

/// Extended type info for variable tracking
const ValueType = union(enum) {
    primitive: ir.Type,
    struct_type: []const u8,
    array_type: ArrayInfo,

    fn toPrimitiveType(self: ValueType) ir.Type {
        return switch (self) {
            .primitive => |p| p,
            .struct_type, .array_type => .ptr,
        };
    }
};

/// Struct field info
const FieldInfo = struct {
    name: []const u8,
    ty: ir.Type,
    offset: i32,
    /// If the field is a struct type, this holds the struct name
    struct_type_name: ?[]const u8 = null,
};

/// Struct type info
const StructTypeInfo = struct {
    name: []const u8,
    fields: []const FieldInfo,
    size: i32,
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

/// Function signature info
const FuncInfo = struct {
    return_type: ir.Type,
    return_type_name: ?[]const u8,
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

/// Ownership state of a variable
const OwnershipState = enum {
    owned,
    moved,
};

/// Variable info - tracks allocation, type, and ownership
const VarInfo = struct {
    ptr: ir.Value,
    ty: ValueType,
    used: bool,
    is_mutable: bool,
    state: OwnershipState,
    moved_to: ?[]const u8,
    moved_line: usize,

    fn init(ptr: ir.Value, ty: ValueType, is_mutable: bool) VarInfo {
        return .{
            .ptr = ptr,
            .ty = ty,
            .used = false,
            .is_mutable = is_mutable,
            .state = .owned,
            .moved_to = null,
            .moved_line = 0,
        };
    }

    fn markMoved(self: *VarInfo, func_name: []const u8, line: usize) void {
        self.state = .moved;
        self.moved_to = func_name;
        self.moved_line = line;
    }

    fn resetOwnership(self: *VarInfo) void {
        self.state = .owned;
        self.moved_to = null;
        self.moved_line = 0;
    }
};

const ConvertError = error{
    OutOfMemory,
    UndefinedVariable,
    FloatModNotSupported,
    WrongArgumentCount,
    ExpectedFloat,
    UnknownType,
    UnknownField,
    UseAfterMove,
    ImmutableAssign,
    ImmutableMove,
};

// ============================================================================
// AST to IR Converter
// ============================================================================

pub const AstToIr = struct {
    allocator: std.mem.Allocator,
    module: ir.Module,
    current_func: ?*ir.Function,
    var_map: std.StringHashMapUnmanaged(VarInfo),
    type_map: std.StringHashMapUnmanaged(TypeInfo),
    func_map: std.StringHashMapUnmanaged(FuncInfo),
    enum_map: std.StringHashMapUnmanaged(EnumInfo),
    current_decl_is_mutable: bool,
    mutation_analyzer: ?*const mutation_analysis.MutationAnalyzer,
    current_line: usize,
    // For struct returns: pointer passed by caller for return value
    sret_ptr: ?ir.Value,
    sret_size: i32,
    // Error tracking
    source_file: ?[]const u8,
    last_error: ?err.CompileError,

    // ------------------------------------------------------------------------
    // Initialization / Cleanup
    // ------------------------------------------------------------------------

    pub fn init(allocator: std.mem.Allocator, mutation_analyzer: ?*const mutation_analysis.MutationAnalyzer) AstToIr {
        return .{
            .allocator = allocator,
            .module = ir.Module.init(allocator),
            .current_func = null,
            .var_map = .{},
            .type_map = .{},
            .func_map = .{},
            .enum_map = .{},
            .current_decl_is_mutable = false,
            .mutation_analyzer = mutation_analyzer,
            .current_line = 1,
            .sret_ptr = null,
            .sret_size = 0,
            .source_file = null,
            .last_error = null,
        };
    }

    fn reportError(self: *AstToIr, code: err.ErrorCode, message: []const u8) void {
        self.last_error = .{
            .code = code,
            .message = message,
            .location = .{
                .file = self.source_file,
                .line = @intCast(self.current_line),
                .column = 1, // Column not tracked in AST
            },
        };
    }

    pub fn deinit(self: *AstToIr) void {
        self.var_map.deinit(self.allocator);

        var type_iter = self.type_map.iterator();
        while (type_iter.next()) |entry| {
            if (entry.value_ptr.* == .struct_type) {
                self.allocator.free(entry.value_ptr.struct_type.fields);
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

    // ------------------------------------------------------------------------
    // Main Entry Point
    // ------------------------------------------------------------------------

    pub fn convert(self: *AstToIr, program: ast.Program) !ir.Module {
        // Register primitive types
        try self.type_map.put(self.allocator, "int", .{ .primitive = .i64 });
        try self.type_map.put(self.allocator, "float", .{ .primitive = .f64 });

        // Register declarations
        for (program.types) |type_decl| try self.registerType(type_decl);
        for (program.enums) |enum_decl| try self.registerEnum(enum_decl);
        for (program.functions) |fn_decl| try self.registerFunction(fn_decl);

        // Convert functions
        for (program.functions) |fn_decl| try self.convertFunction(fn_decl);

        // Transfer ownership of module
        const module = self.module;
        self.module = ir.Module.init(self.allocator);
        return module;
    }

    // ------------------------------------------------------------------------
    // Type Lookup Helpers
    // ------------------------------------------------------------------------

    fn lookupIrType(self: *AstToIr, name: []const u8) !ir.Type {
        const type_info = self.type_map.get(name) orelse return error.UnknownType;
        return type_info.irType();
    }

    fn lookupStructInfo(self: *AstToIr, type_name: []const u8) !StructTypeInfo {
        const type_info = self.type_map.get(type_name) orelse return error.UnknownType;
        return switch (type_info) {
            .struct_type => |s| s,
            .primitive => error.UnknownType,
        };
    }

    fn lookupField(struct_info: StructTypeInfo, field_name: []const u8) !FieldInfo {
        for (struct_info.fields) |f| {
            if (std.mem.eql(u8, f.name, field_name)) return f;
        }
        return error.UnknownField;
    }

    fn func(self: *AstToIr) *ir.Function {
        return self.current_func.?;
    }

    // ------------------------------------------------------------------------
    // Registration (Types, Enums, Functions)
    // ------------------------------------------------------------------------

    fn registerType(self: *AstToIr, type_decl: ast.TypeDecl) !void {
        var fields = try self.allocator.alloc(FieldInfo, type_decl.fields.len);
        var offset: i32 = 0;

        for (type_decl.fields, 0..) |field, i| {
            const field_type_info = self.type_map.get(field.type_name) orelse return error.UnknownType;
            const struct_type_name: ?[]const u8 = switch (field_type_info) {
                .struct_type => field.type_name,
                .primitive => null,
            };
            fields[i] = .{
                .name = field.name,
                .ty = field_type_info.irType(),
                .offset = offset,
                .struct_type_name = struct_type_name,
            };
            offset += 8; // All types are 8 bytes
        }

        try self.type_map.put(self.allocator, type_decl.name, .{
            .struct_type = .{ .name = type_decl.name, .fields = fields, .size = offset },
        });

        debug.astToIr("Registered type '{s}' with size {d}", .{ type_decl.name, offset });
    }

    fn registerEnum(self: *AstToIr, enum_decl: ast.EnumDecl) !void {
        var members: std.StringHashMapUnmanaged(i64) = .{};
        for (enum_decl.members, 0..) |member, i| {
            try members.put(self.allocator, member, @intCast(i));
        }
        try self.enum_map.put(self.allocator, enum_decl.name, .{ .members = members });
        debug.astToIr("Registered enum '{s}' with {d} members", .{ enum_decl.name, enum_decl.members.len });
    }

    fn registerFunction(self: *AstToIr, decl: ast.FunctionDecl) !void {
        var ret_type_name: ?[]const u8 = null;
        const ret_type: ir.Type = if (decl.return_type) |rt| blk: {
            const type_info = self.type_map.get(rt) orelse return error.UnknownType;
            if (type_info.isStruct()) ret_type_name = rt;
            break :blk type_info.irType();
        } else .void;

        var param_types = try self.allocator.alloc(ParamType, decl.params.len);
        for (decl.params, 0..) |param, i| {
            param_types[i] = .{
                .ty = try self.typeExprToValueType(param.type_expr),
            };
        }

        try self.func_map.put(self.allocator, decl.name, .{
            .return_type = ret_type,
            .return_type_name = ret_type_name,
            .param_types = param_types,
        });

        debug.astToIr("Registered function '{s}' returning {s}", .{ decl.name, ret_type.format() });
    }

    fn typeExprToValueType(self: *AstToIr, type_expr: ast.TypeExpr) !ValueType {
        switch (type_expr) {
            .simple => |type_name| {
                const type_info = self.type_map.get(type_name) orelse return error.UnknownType;
                return if (type_info.isStruct())
                    .{ .struct_type = type_name }
                else
                    .{ .primitive = type_info.irType() };
            },
            .array => |arr| {
                const elem_type = try self.lookupIrType(arr.element_type);
                return .{ .array_type = .{
                    .element_type = elem_type,
                    .size = if (arr.size) |s| @intCast(s) else null,
                    .storage = .stack,
                } };
            },
        }
    }

    // ------------------------------------------------------------------------
    // Function Conversion
    // ------------------------------------------------------------------------

    fn convertFunction(self: *AstToIr, decl: ast.FunctionDecl) !void {
        // Check if this function returns a struct (needs sret)
        var uses_sret = false;
        var sret_struct_size: i32 = 0;
        if (decl.return_type) |rt| {
            if (self.type_map.get(rt)) |type_info| {
                if (type_info == .struct_type) {
                    uses_sret = true;
                    sret_struct_size = type_info.struct_type.size;
                }
            }
        }

        const ret_type: ir.Type = if (decl.return_type) |rt|
            try self.lookupIrType(rt)
        else
            .void;

        const ir_func = try self.module.addFunction(decl.name, ret_type);
        self.current_func = ir_func;
        self.var_map.clearRetainingCapacity();
        _ = try ir_func.addBlock("entry");

        // Reset sret state
        self.sret_ptr = null;
        self.sret_size = 0;

        // If returning struct, first parameter is sret pointer
        var param_offset: i32 = 0;
        if (uses_sret) {
            self.sret_ptr = try ir_func.emitParam(0, .ptr);
            self.sret_size = sret_struct_size;
            param_offset = 1;
        }

        // Register parameters (offset by 1 if using sret)
        for (decl.params, 0..) |param, i| {
            try self.registerParameter(param, @as(i32, @intCast(i)) + param_offset);
        }

        // Convert body
        for (decl.body) |stmt| {
            try self.convertStatement(stmt);
        }

        // Check for unused variables
        var iter = self.var_map.iterator();
        while (iter.next()) |entry| {
            if (!entry.value_ptr.used) {
                debug.astToIr("error: unused variable '{s}'\n", .{entry.key_ptr.*});
                return error.UnusedVariable;
            }
        }
    }

    fn registerParameter(self: *AstToIr, param: ast.ParamDecl, idx: i32) !void {
        const value_type = try self.typeExprToValueType(param.type_expr);

        switch (value_type) {
            .struct_type => |struct_name| {
                const param_val = try self.func().emitParam(idx, .ptr);
                try self.var_map.put(self.allocator, param.name, VarInfo.init(
                    param_val,
                    .{ .struct_type = struct_name },
                    true,
                ));
            },
            .array_type => |arr_info| {
                // Array parameters are passed as pointers
                const param_val = try self.func().emitParam(idx, .ptr);
                try self.var_map.put(self.allocator, param.name, VarInfo.init(
                    param_val,
                    .{ .array_type = arr_info },
                    true,
                ));
            },
            .primitive => |prim_type| {
                const param_val = try self.func().emitParam(idx, prim_type);
                const ptr = try self.func().emitAlloca(prim_type);
                try self.func().emitStore(ptr, param_val);
                try self.var_map.put(self.allocator, param.name, VarInfo.init(
                    ptr,
                    .{ .primitive = prim_type },
                    true,
                ));
            },
        }
    }

    // ------------------------------------------------------------------------
    // Statement Conversion
    // ------------------------------------------------------------------------

    fn convertStatement(self: *AstToIr, stmt: ast.Statement) !void {
        switch (stmt) {
            .let_decl => |decl| {
                self.current_decl_is_mutable = false;
                try self.convertVarDecl(decl);
            },
            .var_decl => |decl| {
                self.current_decl_is_mutable = true;
                try self.convertVarDecl(decl);
            },
            .@"return" => |ret| try self.convertReturn(ret),
            .assign => |assign| try self.convertAssignment(assign),
            .field_assign => |assign| try self.convertFieldAssign(assign),
            .index_assign => |assign| try self.convertIndexAssign(assign),
            .call => |call| _ = try self.convertCall(call),
        }
        self.current_line += 1;
    }

    fn convertVarDecl(self: *AstToIr, decl: ast.VarDecl) !void {
        debug.astToIr("Converting var decl: {s}", .{decl.name});

        const init_typed = try self.convertExpression(decl.value);

        switch (init_typed.ty) {
            .struct_type, .array_type => {
                try self.var_map.put(self.allocator, decl.name, VarInfo.init(
                    init_typed.value,
                    init_typed.ty,
                    self.current_decl_is_mutable,
                ));
            },
            .primitive => |prim_ty| {
                const ptr = try self.func().emitAlloca(prim_ty);
                try self.func().emitStore(ptr, init_typed.value);
                try self.var_map.put(self.allocator, decl.name, VarInfo.init(
                    ptr,
                    init_typed.ty,
                    self.current_decl_is_mutable,
                ));
            },
        }
    }

    fn convertReturn(self: *AstToIr, ret: ast.ReturnStmt) !void {
        if (ret.value) |expr| {
            // If using sret and returning a struct literal, write directly to sret buffer
            if (self.sret_ptr) |sret| {
                if (expr == .struct_init) {
                    // Initialize struct directly into sret buffer (no intermediate copy)
                    try self.initStructInto(expr.struct_init, sret);
                    try self.func().emitRet(sret);
                    return;
                }
                // Returning an existing struct variable - copy to sret buffer
                const typed_val = try self.convertExpression(expr);
                try self.func().emitMemcpy(sret, typed_val.value, self.sret_size);
                try self.func().emitRet(sret);
                return;
            }

            const typed_val = try self.convertExpression(expr);

            // Convert float to int if needed
            if (self.func().return_type == .i64 and typed_val.ty.toPrimitiveType() == .f64) {
                const converted = try self.func().emitFpToSi(typed_val.value, .i64);
                try self.func().emitRet(converted);
            } else {
                try self.func().emitRet(typed_val.value);
            }
        } else {
            try self.func().emitRet(null);
        }
    }

    fn convertAssignment(self: *AstToIr, assign: ast.AssignStmt) ConvertError!void {
        const var_info = self.var_map.getPtr(assign.target) orelse {
            debug.astToIr("error: undefined variable '{s}'\n", .{assign.target});
            self.reportError(.E005, "undefined variable");
            return error.UndefinedVariable;
        };

        if (!var_info.is_mutable) {
            debug.astToIr("cannot assign to immutable variable '{s}'\n", .{assign.target});
            self.reportError(.E009, "cannot assign to immutable variable");
            return error.ImmutableAssign;
        }

        var_info.used = true;
        const value_typed = try self.convertExpression(assign.value);

        switch (var_info.ty) {
            .primitive => try self.func().emitStore(var_info.ptr, value_typed.value),
            .struct_type, .array_type => {
                var_info.ptr = value_typed.value;
                var_info.ty = value_typed.ty;
            },
        }

        var_info.resetOwnership();
    }

    fn convertFieldAssign(self: *AstToIr, assign: ast.FieldAssign) ConvertError!void {
        const base = try self.convertExpression(assign.base.*);
        const type_name = switch (base.ty) {
            .struct_type => |name| name,
            else => {
                self.reportError(.E006, "expected struct type for field access");
                return error.UnknownType;
            },
        };

        const struct_info = try self.lookupStructInfo(type_name);
        const field_info = try lookupField(struct_info, assign.field_name);
        const field_ptr = try self.func().emitGetFieldPtr(base.value, field_info.offset);
        const val_typed = try self.convertExpression(assign.value);
        try self.func().emitStore(field_ptr, val_typed.value);
    }

    fn convertIndexAssign(self: *AstToIr, assign: ast.IndexAssign) ConvertError!void {
        const base_typed = try self.convertExpression(assign.base.*);
        const idx_typed = try self.convertExpression(assign.index.*);
        const val_typed = try self.convertExpression(assign.value);

        const elem_ptr = try self.func().emitGetElemPtr(base_typed.value, idx_typed.value, 8);
        try self.func().emitStore(elem_ptr, val_typed.value);
    }

    // ------------------------------------------------------------------------
    // Expression Conversion
    // ------------------------------------------------------------------------

    fn convertExpression(self: *AstToIr, expr: ast.Expression) ConvertError!TypedValue {
        return switch (expr) {
            .integer => |v| .{ .value = try self.func().emitConstI64(v), .ty = .{ .primitive = .i64 } },
            .float_lit => |v| .{ .value = try self.func().emitConstF64(v), .ty = .{ .primitive = .f64 } },
            .identifier => |name| self.convertIdentifier(name),
            .binary => |bin| self.convertBinary(bin),
            .call => |call| self.convertCall(call),
            .struct_init => |sinit| self.convertStructInit(sinit),
            .field_access => |fa| self.convertFieldAccess(fa),
            .array_literal => |arr| self.convertArrayLiteral(arr),
            .index => |idx| self.convertIndex(idx),
            .sized_array => |sized| self.convertSizedArray(sized),
        };
    }

    fn convertIdentifier(self: *AstToIr, name: []const u8) ConvertError!TypedValue {
        const info = self.var_map.getPtr(name) orelse {
            self.reportError(.E005, "undefined variable");
            return error.UndefinedVariable;
        };

        if (info.state == .moved) {
            debug.astToIr("variable '{s}' was moved\n", .{name});
            self.reportError(.E008, "use of moved variable");
            return error.UseAfterMove;
        }

        info.used = true;

        return switch (info.ty) {
            .struct_type, .array_type => .{ .value = info.ptr, .ty = info.ty },
            .primitive => |prim_ty| .{
                .value = try self.func().emitLoad(info.ptr, prim_ty),
                .ty = info.ty,
            },
        };
    }

    fn convertBinary(self: *AstToIr, bin: ast.BinaryExpr) ConvertError!TypedValue {
        const left = try self.convertExpression(bin.left.*);
        const right = try self.convertExpression(bin.right.*);

        const left_prim = left.ty.toPrimitiveType();
        const right_prim = right.ty.toPrimitiveType();
        const result_ty: ir.Type = if (left_prim == .f64 or right_prim == .f64) .f64 else .i64;

        // Promote operands if needed
        const left_val = if (result_ty == .f64 and left_prim == .i64)
            try self.func().emitSiToFp(left.value, .f64)
        else
            left.value;
        const right_val = if (result_ty == .f64 and right_prim == .i64)
            try self.func().emitSiToFp(right.value, .f64)
        else
            right.value;

        const result = if (result_ty == .f64)
            try self.emitFloatOp(bin.op, left_val, right_val)
        else
            try self.emitIntOp(bin.op, left_val, right_val);

        return .{ .value = result, .ty = .{ .primitive = result_ty } };
    }

    fn emitFloatOp(self: *AstToIr, op: ast.BinaryOp, left: ir.Value, right: ir.Value) ConvertError!ir.Value {
        return switch (op) {
            .add => self.func().emitFAdd(left, right),
            .sub => self.func().emitFSub(left, right),
            .mul => self.func().emitFMul(left, right),
            .div => self.func().emitFDiv(left, right),
            .mod => error.FloatModNotSupported,
        };
    }

    fn emitIntOp(self: *AstToIr, op: ast.BinaryOp, left: ir.Value, right: ir.Value) ConvertError!ir.Value {
        return switch (op) {
            .add => self.func().emitAdd(left, right, .i64),
            .sub => self.func().emitSub(left, right, .i64),
            .mul => self.func().emitMul(left, right, .i64),
            .div => self.func().emitDiv(left, right, .i64),
            .mod => self.func().emitMod(left, right, .i64),
        };
    }

    fn convertStructInit(self: *AstToIr, sinit: ast.StructInitExpr) ConvertError!TypedValue {
        const struct_info = try self.lookupStructInfo(sinit.type_name);
        const struct_ptr = try self.func().emitAllocaSized(struct_info.size);
        try self.initStructInto(sinit, struct_ptr);
        return .{ .value = struct_ptr, .ty = .{ .struct_type = sinit.type_name } };
    }

    /// Initialize struct fields into an existing pointer (used for sret returns)
    fn initStructInto(self: *AstToIr, sinit: ast.StructInitExpr, dest_ptr: ir.Value) ConvertError!void {
        const struct_info = try self.lookupStructInfo(sinit.type_name);
        for (sinit.fields) |field_init| {
            const field_info = try lookupField(struct_info, field_init.name);
            const field_ptr = try self.func().emitGetFieldPtr(dest_ptr, field_info.offset);
            const field_val = try self.convertExpression(field_init.value.*);
            try self.func().emitStore(field_ptr, field_val.value);
        }
    }

    fn convertFieldAccess(self: *AstToIr, faccess: ast.FieldAccessExpr) ConvertError!TypedValue {
        // Check for enum access (e.g., Colors.Green)
        if (faccess.base.* == .identifier) {
            if (self.enum_map.get(faccess.base.identifier)) |enum_info| {
                const member_value = enum_info.members.get(faccess.field_name) orelse {
                    self.reportError(.E007, "unknown enum member");
                    return error.UnknownField;
                };
                return .{ .value = try self.func().emitConstI64(member_value), .ty = .{ .primitive = .i64 } };
            }
        }

        const base = try self.convertExpression(faccess.base.*);
        const type_name = switch (base.ty) {
            .struct_type => |name| name,
            else => {
                self.reportError(.E006, "expected struct type for field access");
                return error.UnknownType;
            },
        };

        const struct_info = try self.lookupStructInfo(type_name);
        const field_info = try lookupField(struct_info, faccess.field_name);
        const field_ptr = try self.func().emitGetFieldPtr(base.value, field_info.offset);

        // If the field is a struct, return the pointer with struct type info
        if (field_info.struct_type_name) |nested_struct_name| {
            return .{ .value = field_ptr, .ty = .{ .struct_type = nested_struct_name } };
        }

        // For primitive fields, load the value
        const val = try self.func().emitLoad(field_ptr, field_info.ty);
        return .{ .value = val, .ty = .{ .primitive = field_info.ty } };
    }

    fn convertCall(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        // Handle built-in: trunc
        if (std.mem.eql(u8, call.func_name, "trunc")) {
            return self.convertTrunc(call);
        }

        const func_info = self.func_map.get(call.func_name) orelse {
            const result = try self.func().emitCall(call.func_name, &.{}, .i64);
            return .{ .value = result orelse 0, .ty = .{ .primitive = .i64 } };
        };

        // Check if callee returns a struct (needs sret)
        const returns_struct = func_info.return_type_name != null;
        var sret_buffer: ?ir.Value = null;

        // Allocate args: +1 if using sret for hidden first parameter
        const num_args = call.args.len + @as(usize, if (returns_struct) 1 else 0);
        const args = try self.func().allocator.alloc(ir.Value, num_args);

        // If returning struct, allocate buffer in caller and pass as first arg
        if (returns_struct) {
            const struct_name = func_info.return_type_name.?;
            const struct_info = try self.lookupStructInfo(struct_name);
            sret_buffer = try self.func().emitAllocaSized(struct_info.size);
            args[0] = sret_buffer.?;
        }

        const arg_offset: usize = if (returns_struct) 1 else 0;
        for (call.args, 0..) |arg_expr, i| {
            const arg = try self.convertExpression(arg_expr);
            args[i + arg_offset] = arg.value;
            try self.checkOwnershipTransfer(call.func_name, arg_expr, i);
        }

        const result = try self.func().emitCall(call.func_name, args, func_info.return_type);

        if (func_info.return_type_name) |struct_name| {
            // Return the sret buffer we allocated (not the call result)
            return .{ .value = sret_buffer.?, .ty = .{ .struct_type = struct_name } };
        }
        return .{ .value = result orelse 0, .ty = .{ .primitive = func_info.return_type } };
    }

    fn convertTrunc(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        if (call.args.len != 1) {
            self.reportError(.E011, "trunc() expects exactly 1 argument");
            return error.WrongArgumentCount;
        }
        const arg = try self.convertExpression(call.args[0]);
        if (arg.ty.toPrimitiveType() != .f64) {
            self.reportError(.E011, "trunc() expects a float argument");
            return error.ExpectedFloat;
        }
        const result = try self.func().emitFpToSi(arg.value, .i64);
        return .{ .value = result, .ty = .{ .primitive = .i64 } };
    }

    fn checkOwnershipTransfer(self: *AstToIr, func_name: []const u8, arg_expr: ast.Expression, param_idx: usize) ConvertError!void {
        const analyzer = self.mutation_analyzer orelse return;
        if (!analyzer.doesMutateParam(func_name, param_idx)) return;

        if (arg_expr != .identifier) return;
        const var_name = arg_expr.identifier;
        const var_info = self.var_map.getPtr(var_name) orelse return;

        if (!var_info.is_mutable) {
            debug.astToIr("cannot move immutable variable '{s}'\n", .{var_name});
            self.reportError(.E010, "cannot move immutable variable");
            return error.ImmutableMove;
        }

        var_info.markMoved(func_name, self.current_line);
    }

    // ------------------------------------------------------------------------
    // Array Expression Conversion
    // ------------------------------------------------------------------------

    fn convertArrayLiteral(self: *AstToIr, arr_lit: ast.ArrayLiteralExpr) ConvertError!TypedValue {
        const elements = arr_lit.elements;

        if (elements.len == 0) {
            return .{
                .value = try self.func().emitConstI64(0),
                .ty = .{ .array_type = .{ .element_type = .i64, .size = 0, .storage = .stack } },
            };
        }

        const first_typed = try self.convertExpression(elements[0]);
        const elem_type = first_typed.ty.toPrimitiveType();
        const total_size = @as(i32, @intCast(elements.len)) * 8;
        const arr_ptr = try self.func().emitAllocaSized(total_size);

        for (elements, 0..) |elem, i| {
            const typed = if (i == 0) first_typed else try self.convertExpression(elem);
            const idx_val = try self.func().emitConstI64(@intCast(i));
            const elem_ptr = try self.func().emitGetElemPtr(arr_ptr, idx_val, 8);
            try self.func().emitStore(elem_ptr, typed.value);
        }

        return .{
            .value = arr_ptr,
            .ty = .{ .array_type = .{ .element_type = elem_type, .size = elements.len, .storage = .stack } },
        };
    }

    fn convertIndex(self: *AstToIr, idx: ast.IndexExpr) ConvertError!TypedValue {
        const base_typed = try self.convertExpression(idx.base.*);
        const arr_info = switch (base_typed.ty) {
            .array_type => |a| a,
            else => {
                self.reportError(.E006, "expected array type for index access");
                return error.UnknownType;
            },
        };

        const idx_typed = try self.convertExpression(idx.index.*);
        const elem_ptr = try self.func().emitGetElemPtr(base_typed.value, idx_typed.value, 8);
        const val = try self.func().emitLoad(elem_ptr, arr_info.element_type);

        return .{ .value = val, .ty = .{ .primitive = arr_info.element_type } };
    }

    fn convertSizedArray(self: *AstToIr, sized: ast.SizedArrayExpr) ConvertError!TypedValue {
        const size_typed = try self.convertExpression(sized.size.*);
        const elem_type = try self.lookupIrType(sized.element_type);

        // Calculate total size (unused for now - would be used for heap allocation)
        const eight = try self.func().emitConstI64(8);
        _ = try self.func().emitMul(size_typed.value, eight, .i64);

        // Fixed stack allocation for now
        const arr_ptr = try self.func().emitAllocaSized(64);

        return .{
            .value = arr_ptr,
            .ty = .{ .array_type = .{ .element_type = elem_type, .size = null, .storage = .stack } },
        };
    }
};

// ============================================================================
// Public API
// ============================================================================

pub fn convert(program: ast.Program, allocator: std.mem.Allocator, mutation_analyzer: ?*const mutation_analysis.MutationAnalyzer) !ir.Module {
    var converter = AstToIr.init(allocator, mutation_analyzer);
    defer converter.deinit();
    return try converter.convert(program);
}

pub fn convertWithFile(program: ast.Program, allocator: std.mem.Allocator, mutation_analyzer: ?*const mutation_analysis.MutationAnalyzer, source_file: ?[]const u8, out_error: *?err.CompileError) !ir.Module {
    var converter = AstToIr.init(allocator, mutation_analyzer);
    converter.source_file = source_file;
    defer converter.deinit();
    const result = converter.convert(program) catch {
        out_error.* = converter.last_error;
        return error.OutOfMemory; // Return the underlying error
    };
    return result;
}
