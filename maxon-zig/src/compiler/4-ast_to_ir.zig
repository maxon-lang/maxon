const std = @import("std");
const ast = @import("ast.zig");
const ir = @import("ir.zig");
const debug = @import("debug.zig");
const mutation_analysis = @import("3-mutation_analysis.zig");
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
    element_struct_type: ?[]const u8 = null, // struct name if elements are structs
};

/// Extended type info for variable tracking
const ValueType = union(enum) {
    primitive: ir.Type,
    struct_type: []const u8,
    array_type: ArrayInfo,
    enum_type: []const u8,

    fn toPrimitiveType(self: ValueType) ir.Type {
        return switch (self) {
            .primitive => |p| p,
            .enum_type => .i64,
            .struct_type, .array_type => .ptr,
        };
    }

    fn isStruct(self: ValueType) bool {
        return self == .struct_type;
    }
};

/// Struct field info
const FieldInfo = struct {
    name: []const u8,
    offset: i32,
    size: i32,
    value_type: ValueType,

    fn irType(self: FieldInfo) ir.Type {
        return self.value_type.toPrimitiveType();
    }

    fn isStruct(self: FieldInfo) bool {
        return self.value_type.isStruct();
    }

    fn structName(self: FieldInfo) ?[]const u8 {
        return switch (self.value_type) {
            .struct_type => |name| name,
            else => null,
        };
    }
};

/// Struct type info
const StructTypeInfo = struct {
    name: []const u8,
    fields: []const FieldInfo,
    size: i32,
};

/// Enum type info - maps member names to their integer values
const EnumTypeInfo = struct {
    name: []const u8,
    members: std.StringHashMapUnmanaged(i64),
};

/// Type info - primitives, structs, or enums
const TypeInfo = union(enum) {
    primitive: ir.Type,
    struct_type: StructTypeInfo,
    enum_type: EnumTypeInfo,

    fn irType(self: TypeInfo) ir.Type {
        return switch (self) {
            .primitive => |t| t,
            .struct_type => .ptr,
            .enum_type => .i64,
        };
    }

    fn isStruct(self: TypeInfo) bool {
        return self == .struct_type;
    }

    fn isEnum(self: TypeInfo) bool {
        return self == .enum_type;
    }
};

/// Function signature info
const FuncInfo = struct {
    return_type: ir.Type,
    return_type_name: ?[]const u8,
    return_value_type: ?ValueType, // Full type info for arrays
    param_types: []const ParamType,
};

/// Parameter type info
const ParamType = struct {
    ty: ValueType,
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
    UnknownType,
    UnknownField,
    UseAfterMove,
    ImmutableAssign,
    ImmutableMove,
    SemanticError,
    NotABuiltin,
    TypeMismatch,
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
    current_decl_is_mutable: bool,
    mutation_analyzer: ?*const mutation_analysis.MutationAnalyzer,
    current_line: usize,
    // For struct returns: pointer passed by caller for return value
    sret_ptr: ?ir.Value,
    sret_size: i32,
    // Error tracking
    source_file: ?[]const u8,
    last_error: ?err.CompileError,
    // Loop context for break/continue
    loop_end_block: ?u32 = null,
    loop_cond_block: ?u32 = null,

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
            .current_decl_is_mutable = false,
            .mutation_analyzer = mutation_analyzer,
            .current_line = 1,
            .sret_ptr = null,
            .sret_size = 0,
            .source_file = null,
            .last_error = null,
            .loop_end_block = null,
            .loop_cond_block = null,
        };
    }

    fn reportError(self: *AstToIr, code: err.ErrorCode) void {
        self.last_error = .{
            .code = code,
            .message = code.message(),
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
            switch (entry.value_ptr.*) {
                .struct_type => |s| self.allocator.free(s.fields),
                .enum_type => |*e| e.members.deinit(self.allocator),
                .primitive => {},
            }
        }
        self.type_map.deinit(self.allocator);

        var func_iter = self.func_map.iterator();
        while (func_iter.next()) |entry| {
            self.allocator.free(entry.value_ptr.param_types);
        }
        self.func_map.deinit(self.allocator);
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
            .primitive, .enum_type => error.UnknownType,
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
            const value_type: ValueType = switch (field.type_expr) {
                .simple => |type_name| blk: {
                    const field_type_info = self.type_map.get(type_name) orelse return error.UnknownType;
                    break :blk switch (field_type_info) {
                        .struct_type => .{ .struct_type = type_name },
                        .primitive => |p| .{ .primitive = p },
                        .enum_type => .{ .enum_type = type_name },
                    };
                },
                .array => |arr| .{ .array_type = .{
                    .element_type = try self.lookupIrType(arr.element_type),
                    .size = if (arr.size) |s| @intCast(s) else null,
                    .storage = .stack,
                } },
            };
            const field_size: i32 = switch (value_type) {
                .struct_type => |name| blk: {
                    const info = self.type_map.get(name) orelse return error.UnknownType;
                    break :blk switch (info) {
                        .struct_type => |s| s.size,
                        else => 8,
                    };
                },
                .primitive, .enum_type, .array_type => 8, // arrays stored as pointers
            };
            fields[i] = .{
                .name = field.name,
                .offset = offset,
                .size = field_size,
                .value_type = value_type,
            };
            offset += field_size;
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
        try self.type_map.put(self.allocator, enum_decl.name, .{
            .enum_type = .{ .name = enum_decl.name, .members = members },
        });
        debug.astToIr("Registered enum '{s}' with {d} members", .{ enum_decl.name, enum_decl.members.len });
    }

    fn registerFunction(self: *AstToIr, decl: ast.FunctionDecl) !void {
        var ret_type_name: ?[]const u8 = null;
        var ret_value_type: ?ValueType = null;
        const ret_type: ir.Type = if (decl.return_type) |rt| blk: {
            switch (rt) {
                .simple => |type_name| {
                    const type_info = self.type_map.get(type_name) orelse return error.UnknownType;
                    if (type_info.isStruct()) ret_type_name = type_name;
                    break :blk type_info.irType();
                },
                .array => |arr| {
                    // Array return types are returned as pointers
                    const elem_type = try self.lookupIrType(arr.element_type);
                    ret_value_type = .{ .array_type = .{
                        .element_type = elem_type,
                        .size = if (arr.size) |s| @intCast(s) else null,
                        .storage = .heap, // Returned arrays are heap-allocated
                    } };
                    break :blk .ptr;
                },
            }
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
            .return_value_type = ret_value_type,
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
            switch (rt) {
                .simple => |type_name| {
                    if (self.type_map.get(type_name)) |type_info| {
                        if (type_info == .struct_type) {
                            uses_sret = true;
                            sret_struct_size = type_info.struct_type.size;
                        }
                    }
                },
                .array => {}, // Arrays returned as pointers, no sret needed
            }
        }

        const ret_type: ir.Type = if (decl.return_type) |rt| blk: {
            switch (rt) {
                .simple => |type_name| break :blk try self.lookupIrType(type_name),
                .array => break :blk .ptr,
            }
        } else .void;

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
                try self.func().setValueName(param_val, param.name);
                try self.var_map.put(self.allocator, param.name, VarInfo.init(
                    param_val,
                    .{ .struct_type = struct_name },
                    true,
                ));
            },
            .array_type => |arr_info| {
                // Array parameters are passed as pointers
                const param_val = try self.func().emitParam(idx, .ptr);
                try self.func().setValueName(param_val, param.name);
                try self.var_map.put(self.allocator, param.name, VarInfo.init(
                    param_val,
                    .{ .array_type = arr_info },
                    true,
                ));
            },
            .primitive => |prim_type| {
                const param_val = try self.func().emitParam(idx, prim_type);
                try self.func().setValueName(param_val, param.name);
                const ptr = try self.func().emitAlloca(prim_type);
                try self.func().setValueName(ptr, param.name);
                try self.func().emitStore(ptr, param_val);
                try self.var_map.put(self.allocator, param.name, VarInfo.init(
                    ptr,
                    .{ .primitive = prim_type },
                    true,
                ));
            },
            .enum_type => |enum_name| {
                // Enums are passed as i64 values
                const param_val = try self.func().emitParam(idx, .i64);
                try self.func().setValueName(param_val, param.name);
                const ptr = try self.func().emitAlloca(.i64);
                try self.func().setValueName(ptr, param.name);
                try self.func().emitStore(ptr, param_val);
                try self.var_map.put(self.allocator, param.name, VarInfo.init(
                    ptr,
                    .{ .enum_type = enum_name },
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
            .if_stmt => |if_s| try self.convertIfStmt(if_s),
            .while_stmt => |while_s| try self.convertWhileStmt(while_s),
            .break_stmt => try self.convertBreakStmt(),
            .continue_stmt => try self.convertContinueStmt(),
        }
        self.current_line += 1;
    }

    fn convertVarDecl(self: *AstToIr, decl: ast.VarDecl) !void {
        debug.astToIr("Converting var decl: {s}", .{decl.name});

        // Sized arrays cannot be immutable (they have no initial contents)
        if (!self.current_decl_is_mutable and decl.value == .sized_array) {
            self.reportError(.E011);
            return error.SemanticError;
        }

        const init_typed = try self.convertExpression(decl.value);

        switch (init_typed.ty) {
            .struct_type, .array_type => {
                try self.func().setValueName(init_typed.value, decl.name);
                try self.var_map.put(self.allocator, decl.name, VarInfo.init(
                    init_typed.value,
                    init_typed.ty,
                    self.current_decl_is_mutable,
                ));
            },
            .primitive => |prim_ty| {
                const ptr = try self.func().emitAlloca(prim_ty);
                try self.func().setValueName(ptr, decl.name);
                try self.func().emitStore(ptr, init_typed.value);
                try self.var_map.put(self.allocator, decl.name, VarInfo.init(
                    ptr,
                    init_typed.ty,
                    self.current_decl_is_mutable,
                ));
            },
            .enum_type => {
                // Enums are stored like primitives (i64)
                const ptr = try self.func().emitAlloca(.i64);
                try self.func().setValueName(ptr, decl.name);
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
                const converted = try self.func().emitUnaryOp(.fptosi, typed_val.value, .i64);
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
            self.reportError(.E005);
            return error.UndefinedVariable;
        };

        if (!var_info.is_mutable) {
            debug.astToIr("cannot assign to immutable variable '{s}'\n", .{assign.target});
            self.reportError(.E009);
            return error.ImmutableAssign;
        }

        var_info.used = true;
        const value_typed = try self.convertExpression(assign.value);

        switch (var_info.ty) {
            .primitive, .enum_type => try self.func().emitStore(var_info.ptr, value_typed.value),
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
                self.reportError(.E006);
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
        // Check if base is an immutable variable
        if (assign.base.* == .identifier) {
            const var_name = assign.base.identifier;
            if (self.var_map.get(var_name)) |var_info| {
                if (!var_info.is_mutable) {
                    self.reportError(.E009);
                    return error.ImmutableAssign;
                }
            }
        }

        const base_typed = try self.convertExpression(assign.base.*);
        const idx_typed = try self.convertExpression(assign.index.*);
        const val_typed = try self.convertExpression(assign.value);

        const elem_ptr = try self.func().emitGetElemPtr(base_typed.value, idx_typed.value, 8);
        try self.func().emitStore(elem_ptr, val_typed.value);
    }

    fn convertIfStmt(self: *AstToIr, if_stmt: ast.IfStmt) ConvertError!void {
        // Convert condition
        const cond_typed = try self.convertExpression(if_stmt.condition);

        // Determine what blocks we need
        const has_else = if_stmt.else_body != null or if_stmt.else_if != null;

        // Create then block
        const then_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("then");

        // Create else block (if needed) or end block
        var else_block_idx: u32 = undefined;
        if (has_else) {
            else_block_idx = @intCast(self.func().blocks.items.len);
            _ = try self.func().addBlock("else");
        }

        // Create end block
        const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("end");

        // Emit conditional branch in the previous block
        const branch_target_if_false = if (has_else) else_block_idx else end_block_idx;
        const entry_block = &self.func().blocks.items[then_block_idx - 1];
        try entry_block.instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = cond_typed.value }, .{ .block_ref = then_block_idx } },
            .result = branch_target_if_false,
        });

        // Save end block, remove it temporarily
        const end_block = self.func().blocks.pop().?;

        // If we have else, save it too
        var else_block: ?ir.BasicBlock = null;
        if (has_else) {
            else_block = self.func().blocks.pop().?;
        }

        // Now current block is "then" block - convert body statements
        for (if_stmt.body) |stmt| {
            try self.convertStatement(stmt);
        }

        // Add unconditional branch to end block (if not already terminated)
        if (self.func().currentBlock()) |block| {
            if (block.instructions.items.len == 0 or block.instructions.items[block.instructions.items.len - 1].op != .ret) {
                try self.func().emitBr(end_block_idx);
            }
        }

        // Restore and generate else block if needed
        if (has_else) {
            try self.func().blocks.append(self.allocator, else_block.?);

            // Check for else-if chain
            if (if_stmt.else_if) |else_if| {
                // Recursively convert the else-if
                try self.convertIfStmt(else_if.*);
            } else if (if_stmt.else_body) |else_body| {
                // Convert else body statements
                for (else_body) |stmt| {
                    try self.convertStatement(stmt);
                }
            }

            // Add unconditional branch to end block (if not already terminated)
            if (self.func().currentBlock()) |block| {
                if (block.instructions.items.len == 0 or block.instructions.items[block.instructions.items.len - 1].op != .ret) {
                    try self.func().emitBr(end_block_idx);
                }
            }
        }

        // Restore end block
        try self.func().blocks.append(self.allocator, end_block);
    }

    fn convertWhileStmt(self: *AstToIr, while_stmt: ast.WhileStmt) ConvertError!void {
        // Create condition block - this will be the current block after creation
        const cond_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("whilecond");

        // Emit unconditional branch from previous block to condition block
        try self.func().blocks.items[cond_block_idx - 1].instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = cond_block_idx }, .none },
            .result = undefined,
        });

        // Convert condition expression in condition block
        const cond_typed = try self.convertExpression(while_stmt.condition);

        // Create body block
        const body_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("whilebody");

        // Save loop context
        const saved_end_block = self.loop_end_block;
        const saved_cond_block = self.loop_cond_block;
        self.loop_cond_block = cond_block_idx;
        // We use a sentinel value for loop_end_block that we'll patch later
        // Use max u32 as sentinel to indicate "needs patching"
        self.loop_end_block = 0xFFFFFFFF;

        // Convert body statements (body block is current)
        for (while_stmt.body) |stmt| {
            try self.convertStatement(stmt);
        }

        // Emit unconditional branch back to condition block (if not already terminated)
        if (self.func().currentBlock()) |block| {
            const len = block.instructions.items.len;
            if (len == 0 or (block.instructions.items[len - 1].op != .ret and block.instructions.items[len - 1].op != .br and block.instructions.items[len - 1].op != .br_cond)) {
                try self.func().emitBr(cond_block_idx);
            }
        }

        // Now create continuation block - this is its final index
        const cont_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("whilecont");

        // Now emit the conditional branch in the condition block with the correct cont_block_idx
        try self.func().blocks.items[cond_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = cond_typed.value }, .{ .block_ref = body_block_idx } },
            .result = cont_block_idx,
        });

        // Patch any break statements that used the sentinel value
        for (self.func().blocks.items[body_block_idx..cont_block_idx]) |*block| {
            for (block.instructions.items) |*instr| {
                if (instr.op == .br) {
                    if (instr.operands[0] == .block_ref and instr.operands[0].block_ref == 0xFFFFFFFF) {
                        instr.operands[0] = .{ .block_ref = cont_block_idx };
                    }
                }
            }
        }

        // Restore loop context
        self.loop_end_block = saved_end_block;
        self.loop_cond_block = saved_cond_block;
    }

    fn convertBreakStmt(self: *AstToIr) ConvertError!void {
        if (self.loop_end_block) |end_block| {
            try self.func().emitBr(end_block);
        } else {
            self.reportError(.E012);
            return error.SemanticError;
        }
    }

    fn convertContinueStmt(self: *AstToIr) ConvertError!void {
        if (self.loop_cond_block) |cond_block| {
            try self.func().emitBr(cond_block);
        } else {
            self.reportError(.E012);
            return error.SemanticError;
        }
    }

    // ------------------------------------------------------------------------
    // Expression Conversion
    // ------------------------------------------------------------------------

    fn convertExpression(self: *AstToIr, expr: ast.Expression) ConvertError!TypedValue {
        return switch (expr) {
            .integer => |v| .{ .value = try self.func().emitConstI64(v), .ty = .{ .primitive = .i64 } },
            .float_lit => |v| .{ .value = try self.func().emitConstF64(v), .ty = .{ .primitive = .f64 } },
            .bool_lit => |v| .{ .value = try self.func().emitConstI64(if (v) 1 else 0), .ty = .{ .primitive = .i64 } },
            .identifier => |name| self.convertIdentifier(name),
            .binary => |bin| self.convertBinary(bin),
            .compare => |cmp| self.convertCompare(cmp),
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
            self.reportError(.E005);
            return error.UndefinedVariable;
        };

        if (info.state == .moved) {
            debug.astToIr("variable '{s}' was moved\n", .{name});
            self.reportError(.E008);
            return error.UseAfterMove;
        }

        info.used = true;

        return switch (info.ty) {
            .struct_type, .array_type => .{ .value = info.ptr, .ty = info.ty },
            .primitive => |prim_ty| .{
                .value = try self.func().emitLoad(info.ptr, prim_ty),
                .ty = info.ty,
            },
            .enum_type => .{
                .value = try self.func().emitLoad(info.ptr, .i64),
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
            try self.func().emitUnaryOp(.sitofp, left.value, .f64)
        else
            left.value;
        const right_val = if (result_ty == .f64 and right_prim == .i64)
            try self.func().emitUnaryOp(.sitofp, right.value, .f64)
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
            .add => self.func().emitBinaryOp(.fadd, left, right, .f64),
            .sub => self.func().emitBinaryOp(.fsub, left, right, .f64),
            .mul => self.func().emitBinaryOp(.fmul, left, right, .f64),
            .div => self.func().emitBinaryOp(.fdiv, left, right, .f64),
            .mod => error.FloatModNotSupported,
        };
    }

    fn emitIntOp(self: *AstToIr, op: ast.BinaryOp, left: ir.Value, right: ir.Value) ConvertError!ir.Value {
        return switch (op) {
            .add => self.func().emitBinaryOp(.add, left, right, .i64),
            .sub => self.func().emitBinaryOp(.sub, left, right, .i64),
            .mul => self.func().emitBinaryOp(.mul, left, right, .i64),
            .div => self.func().emitBinaryOp(.div, left, right, .i64),
            .mod => self.func().emitBinaryOp(.mod, left, right, .i64),
        };
    }

    fn convertCompare(self: *AstToIr, cmp: ast.CompareExpr) ConvertError!TypedValue {
        const left = try self.convertExpression(cmp.left.*);
        const right = try self.convertExpression(cmp.right.*);

        const left_prim = left.ty.toPrimitiveType();
        const right_prim = right.ty.toPrimitiveType();

        // If either operand is float, use float comparison
        if (left_prim == .f64 or right_prim == .f64) {
            // Promote int to float if needed
            const left_val = if (left_prim == .i64)
                try self.func().emitUnaryOp(.sitofp, left.value, .f64)
            else
                left.value;
            const right_val = if (right_prim == .i64)
                try self.func().emitUnaryOp(.sitofp, right.value, .f64)
            else
                right.value;

            const op: ir.Instruction.Op = switch (cmp.op) {
                .eq => .fcmp_eq,
                .ne => .fcmp_ne,
                .lt => .fcmp_lt,
                .le => .fcmp_le,
                .gt => .fcmp_gt,
                .ge => .fcmp_ge,
            };
            const result = try self.func().emitBinaryOp(op, left_val, right_val, .i64);
            return .{ .value = result, .ty = .{ .primitive = .i64 } };
        }

        // Integer comparison
        const op: ir.Instruction.Op = switch (cmp.op) {
            .eq => .icmp_eq,
            .ne => .icmp_ne,
            .lt => .icmp_lt,
            .le => .icmp_le,
            .gt => .icmp_gt,
            .ge => .icmp_ge,
        };
        const result = try self.func().emitBinaryOp(op, left.value, right.value, .i64);
        return .{ .value = result, .ty = .{ .primitive = .i64 } };
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

            // Track ownership transfer for array/struct fields
            try self.trackFieldOwnershipTransfer(field_init.value.*, sinit.type_name);

            if (field_info.isStruct()) {
                // Struct fields are embedded inline - copy the data
                try self.func().emitMemcpy(field_ptr, field_val.value, field_info.size);
            } else {
                try self.func().emitStore(field_ptr, field_val.value);
            }
        }
    }

    /// Track ownership when a variable is moved into a struct field
    fn trackFieldOwnershipTransfer(self: *AstToIr, expr: ast.Expression, target_type: []const u8) ConvertError!void {
        // Only track identifier expressions (variable references)
        if (expr != .identifier) return;

        const var_name = expr.identifier;
        const var_info = self.var_map.getPtr(var_name) orelse return;

        // Check if this is an array or struct - these types are moved, not copied
        switch (var_info.ty) {
            .array_type, .struct_type => {
                // Must be mutable to be moved
                if (!var_info.is_mutable) {
                    debug.astToIr("cannot move immutable variable '{s}' into struct\n", .{var_name});
                    self.reportError(.E010);
                    return error.ImmutableMove;
                }
                var_info.markMoved(target_type, self.current_line);
            },
            .primitive, .enum_type => {
                // Primitives are copied, not moved
            },
        }
    }

    fn convertFieldAccess(self: *AstToIr, faccess: ast.FieldAccessExpr) ConvertError!TypedValue {
        // Check for enum member access (e.g., Colors.Green)
        if (faccess.base.* == .identifier) {
            if (self.type_map.get(faccess.base.identifier)) |type_info| {
                if (type_info == .enum_type) {
                    const member_value = type_info.enum_type.members.get(faccess.field_name) orelse {
                        self.reportError(.E007);
                        return error.UnknownField;
                    };
                    return .{
                        .value = try self.func().emitConstI64(member_value),
                        .ty = .{ .enum_type = faccess.base.identifier },
                    };
                }
            }
        }

        // Struct field access
        const base = try self.convertExpression(faccess.base.*);
        const type_name = switch (base.ty) {
            .struct_type => |name| name,
            else => {
                self.reportError(.E006);
                return error.UnknownType;
            },
        };

        const struct_info = try self.lookupStructInfo(type_name);
        const field_info = try lookupField(struct_info, faccess.field_name);
        const field_ptr = try self.func().emitGetFieldPtr(base.value, field_info.offset);

        return switch (field_info.value_type) {
            .struct_type => |name| .{ .value = field_ptr, .ty = .{ .struct_type = name } },
            .primitive => |prim| .{
                .value = try self.func().emitLoad(field_ptr, prim),
                .ty = .{ .primitive = prim },
            },
            .array_type => |arr| .{
                // Array field stores a pointer - load it
                .value = try self.func().emitLoad(field_ptr, .ptr),
                .ty = .{ .array_type = arr },
            },
            .enum_type => |name| .{
                .value = try self.func().emitLoad(field_ptr, .i64),
                .ty = .{ .enum_type = name },
            },
        };
    }

    fn convertCall(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        // Handle built-in functions
        if (self.convertBuiltin(call)) |result| {
            return result;
        } else |builtin_err| switch (builtin_err) {
            error.NotABuiltin => {},
            else => return builtin_err,
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
        // If the function returns an array, use the full array type info
        if (func_info.return_value_type) |vtype| {
            return .{ .value = result orelse 0, .ty = vtype };
        }
        return .{ .value = result orelse 0, .ty = .{ .primitive = func_info.return_type } };
    }

    // ------------------------------------------------------------------------
    // Built-in Functions
    // ------------------------------------------------------------------------

    const Builtin = struct {
        name: []const u8,
        op: ir.Instruction.Op,
        arg_type: ir.Type,
        ret_type: ir.Type,
    };

    const builtins = [_]Builtin{
        .{ .name = "trunc", .op = .fptosi, .arg_type = .f64, .ret_type = .i64 },
        .{ .name = "abs", .op = .fabs, .arg_type = .f64, .ret_type = .f64 },
    };

    fn convertBuiltin(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        const builtin = for (builtins) |b| {
            if (std.mem.eql(u8, call.func_name, b.name)) break b;
        } else return error.NotABuiltin;

        if (call.args.len != 1) {
            self.reportError(.E011);
            return error.WrongArgumentCount;
        }

        const arg = try self.convertExpression(call.args[0]);
        if (arg.ty.toPrimitiveType() != builtin.arg_type) {
            self.reportError(.E011);
            return error.TypeMismatch;
        }

        const result = self.func().emitUnaryOp(builtin.op, arg.value, builtin.ret_type) catch return error.OutOfMemory;

        return .{ .value = result, .ty = .{ .primitive = builtin.ret_type } };
    }

    fn checkOwnershipTransfer(self: *AstToIr, func_name: []const u8, arg_expr: ast.Expression, param_idx: usize) ConvertError!void {
        const analyzer = self.mutation_analyzer orelse return;
        if (!analyzer.doesMutateParam(func_name, param_idx)) return;

        if (arg_expr != .identifier) return;
        const var_name = arg_expr.identifier;
        const var_info = self.var_map.getPtr(var_name) orelse return;

        if (!var_info.is_mutable) {
            debug.astToIr("cannot move immutable variable '{s}'\n", .{var_name});
            self.reportError(.E010);
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
        const elem_struct_type: ?[]const u8 = switch (first_typed.ty) {
            .struct_type => |name| name,
            else => null,
        };
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
            .ty = .{ .array_type = .{ .element_type = elem_type, .size = elements.len, .storage = .stack, .element_struct_type = elem_struct_type } },
        };
    }

    fn convertIndex(self: *AstToIr, idx: ast.IndexExpr) ConvertError!TypedValue {
        const base_typed = try self.convertExpression(idx.base.*);
        const arr_info = switch (base_typed.ty) {
            .array_type => |a| a,
            else => {
                self.reportError(.E006);
                return error.UnknownType;
            },
        };

        const idx_typed = try self.convertExpression(idx.index.*);
        const elem_ptr = try self.func().emitGetElemPtr(base_typed.value, idx_typed.value, 8);
        const val = try self.func().emitLoad(elem_ptr, arr_info.element_type);

        // If the array element is a struct, return struct type
        if (arr_info.element_struct_type) |struct_name| {
            return .{ .value = val, .ty = .{ .struct_type = struct_name } };
        }

        return .{ .value = val, .ty = .{ .primitive = arr_info.element_type } };
    }

    fn convertSizedArray(self: *AstToIr, sized: ast.SizedArrayExpr) ConvertError!TypedValue {
        const elem_type = try self.lookupIrType(sized.element_type);

        // Check if size is a constant - we can compute total size at compile time
        if (sized.size.* == .integer) {
            const size = sized.size.integer;
            const total_size: i32 = @intCast(size * 8);
            const arr_ptr = try self.func().emitAllocaSized(total_size);
            return .{
                .value = arr_ptr,
                .ty = .{ .array_type = .{ .element_type = elem_type, .size = @intCast(size), .storage = .stack } },
            };
        }

        // For variable sizes, use heap allocation
        const size_typed = try self.convertExpression(sized.size.*);

        // Calculate total size: size * 8 (all elements are 8 bytes)
        const eight = try self.func().emitConstI64(8);
        const total_size = try self.func().emitBinaryOp(.mul, size_typed.value, eight, .i64);

        // Heap allocation for variable-sized arrays
        const arr_ptr = try self.func().emitHeapAlloc(total_size);

        return .{
            .value = arr_ptr,
            .ty = .{ .array_type = .{ .element_type = elem_type, .size = null, .storage = .heap } },
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
