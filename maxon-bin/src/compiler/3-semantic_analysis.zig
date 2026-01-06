const std = @import("std");
const ast = @import("ast.zig");
const types = @import("ast_to_ir_types.zig");
const ir = @import("ir.zig");
const intrinsics_registry = @import("intrinsics_registry.zig");

// Re-export types for external use
pub const SemanticInfo = types.SemanticInfo;
pub const SemanticVarInfo = types.SemanticVarInfo;
pub const TypeInfo = types.TypeInfo;
pub const FuncInfo = types.FuncInfo;
pub const InterfaceInfo = types.InterfaceInfo;
pub const ValueType = types.ValueType;
pub const FieldInfo = types.FieldInfo;
pub const ParamType = types.ParamType;
pub const ExternalTypeInfo = types.ExternalTypeInfo;
pub const ExternalFuncSignature = types.ExternalFuncSignature;
pub const ExternalInterfaceInfo = types.ExternalInterfaceInfo;

/// Analyzes which function parameters are mutated (assigned to) within their function body.
/// This is used for ownership/borrow checking: if a function mutates a parameter,
/// passing a value to that parameter transfers ownership (move semantics).
pub const MutationAnalyzer = struct {
    allocator: std.mem.Allocator,
    /// Map: function name -> bitset of mutated parameter indices
    function_mutations: std.StringHashMapUnmanaged(MutatedParams),

    const MutatedParams = struct {
        bits: u64, // Supports up to 64 parameters
    };

    pub fn init(allocator: std.mem.Allocator) MutationAnalyzer {
        return .{
            .allocator = allocator,
            .function_mutations = .{},
        };
    }

    pub fn deinit(self: *MutationAnalyzer) void {
        self.function_mutations.deinit(self.allocator);
    }

    /// Analyze all functions in the program to determine parameter mutations
    pub fn analyze(self: *MutationAnalyzer, program: ast.Program) !void {
        for (program.functions) |func| {
            try self.analyzeFunction(func);
        }
    }

    /// Analyze a single function for parameter mutations
    fn analyzeFunction(self: *MutationAnalyzer, func: ast.FunctionDecl) !void {
        var mutated: MutatedParams = .{ .bits = 0 };

        // Build map of parameter names to indices
        var param_indices: std.StringHashMapUnmanaged(usize) = .{};
        defer param_indices.deinit(self.allocator);

        for (func.params, 0..) |param, idx| {
            try param_indices.put(self.allocator, param.name, idx);
        }

        // Scan all statements for assignments to parameters
        for (func.body) |stmt| {
            self.checkStatementForMutation(stmt, &param_indices, &mutated);
        }

        try self.function_mutations.put(self.allocator, func.name, mutated);
    }

    fn checkStatementForMutation(
        self: *MutationAnalyzer,
        stmt: ast.Statement,
        param_indices: *std.StringHashMapUnmanaged(usize),
        mutated: *MutatedParams,
    ) void {
        switch (stmt.kind) {
            .assign => |assign| {
                // Direct assignment to a variable - check if it's a parameter
                if (param_indices.get(assign.target)) |idx| {
                    if (idx < 64) {
                        mutated.bits |= @as(u64, 1) << @intCast(idx);
                    }
                }
            },
            .index_assign => |idx_assign| {
                // Array index assignment - check if base is a parameter
                self.checkExpressionForParamMutation(idx_assign.base.*, param_indices, mutated);
            },
            .var_decl, .let_decl => {
                // Variable declarations don't mutate existing parameters
                // (they may shadow them, but that's a different concern)
            },
            .@"return" => {
                // Return statements don't mutate parameters
            },
            .call => {
                // Standalone call statements don't directly mutate parameters
                // (mutations happen inside the called function)
            },
            .method_call => |mcall| {
                // Method calls like arr.push(x) mutate the base array
                self.checkExpressionForParamMutation(mcall.base.*, param_indices, mutated);
            },
            .field_assign => |assign| {
                // Field assignment - check if base is a parameter (e.g., d.value = 100)
                self.checkExpressionForParamMutation(assign.base.*, param_indices, mutated);
            },
            .if_stmt => |if_s| {
                // Check mutations inside if/if-let body and else clauses
                for (if_s.children) |child| {
                    switch (child.role) {
                        .primary, .else_clause => {
                            for (child.statements) |body_stmt| {
                                self.checkStatementForMutation(body_stmt, param_indices, mutated);
                            }
                        },
                        .else_if => {
                            // child.statements[0] is a nested if_stmt
                            if (child.statements.len > 0) {
                                self.checkStatementForMutation(child.statements[0], param_indices, mutated);
                            }
                        },
                        else => {},
                    }
                }
            },
            .while_stmt => |while_s| {
                // Check mutations inside while loop body
                for (while_s.children) |child| {
                    for (child.statements) |body_stmt| {
                        self.checkStatementForMutation(body_stmt, param_indices, mutated);
                    }
                }
            },
            .for_stmt => |for_s| {
                // Check mutations inside for loop body
                for (for_s.children) |child| {
                    for (child.statements) |body_stmt| {
                        self.checkStatementForMutation(body_stmt, param_indices, mutated);
                    }
                }
            },
            .break_stmt, .continue_stmt => {
                // Control flow statements don't mutate parameters
            },
            .else_unwrap_decl => |unwrap| {
                // Check mutations inside else-unwrap default body
                for (unwrap.children) |child| {
                    for (child.statements) |body_stmt| {
                        self.checkStatementForMutation(body_stmt, param_indices, mutated);
                    }
                }
            },
            .guard_let_decl => |guard| {
                // Check mutations inside guard-let nil body
                for (guard.children) |child| {
                    for (child.statements) |body_stmt| {
                        self.checkStatementForMutation(body_stmt, param_indices, mutated);
                    }
                }
            },
            .throw_stmt => {
                // Throw statements don't mutate parameters
            },
            .do_catch_stmt => |do_catch| {
                // Check mutations inside do block and catch blocks
                for (do_catch.children) |child| {
                    for (child.statements) |body_stmt| {
                        self.checkStatementForMutation(body_stmt, param_indices, mutated);
                    }
                }
            },
            .match_stmt => |match_s| {
                // Check mutations inside match case bodies
                for (match_s.children) |child| {
                    for (child.statements) |body_stmt| {
                        self.checkStatementForMutation(body_stmt, param_indices, mutated);
                    }
                }
            },
        }
    }

    fn checkExpressionForParamMutation(
        self: *MutationAnalyzer,
        expr: ast.Expression,
        param_indices: *std.StringHashMapUnmanaged(usize),
        mutated: *MutatedParams,
    ) void {
        switch (expr) {
            .identifier => |name| {
                // Direct reference to a parameter being mutated
                if (param_indices.get(name)) |idx| {
                    if (idx < 64) {
                        mutated.bits |= @as(u64, 1) << @intCast(idx);
                    }
                }
            },
            .field_access => |fa| {
                // Field access mutation also mutates the base struct
                // Recursively check if the ultimate base is a parameter
                self.checkExpressionForParamMutation(fa.base.*, param_indices, mutated);
            },
            .index => |idx| {
                // Index access mutation also mutates the base array
                self.checkExpressionForParamMutation(idx.base.*, param_indices, mutated);
            },
            .method_call => |mcall| {
                // Method call like arr.push(x) mutates the base
                self.checkExpressionForParamMutation(mcall.base.*, param_indices, mutated);
            },
            // self_expr - treat like identifier but self cannot be mutated as a whole
            .self_expr => {},
            // nil_coalesce - check both optional and default
            .nil_coalesce => |nc| {
                self.checkExpressionForParamMutation(nc.optional.*, param_indices, mutated);
                self.checkExpressionForParamMutation(nc.default.*, param_indices, mutated);
            },
            // Cast expressions - check the inner expression
            .cast => |c| {
                self.checkExpressionForParamMutation(c.expr.*, param_indices, mutated);
            },
            // Closures - check body for mutations
            .closure => |clos| {
                self.checkExpressionForParamMutation(clos.body.*, param_indices, mutated);
            },
            // Try expressions - check inner expression
            .try_expr => |te| {
                self.checkExpressionForParamMutation(te.expr.*, param_indices, mutated);
            },
            // Match expressions - check scrutinee, patterns and results
            .match_expr => |me| {
                self.checkExpressionForParamMutation(me.scrutinee.*, param_indices, mutated);
                for (me.cases) |match_case| {
                    for (match_case.patterns) |pattern| {
                        self.checkExpressionForParamMutation(pattern, param_indices, mutated);
                    }
                    self.checkExpressionForParamMutation(match_case.result, param_indices, mutated);
                }
                if (me.default_expr) |default| {
                    self.checkExpressionForParamMutation(default.*, param_indices, mutated);
                }
            },
            // Enum case construction - check args for mutations
            .enum_case => |ec| {
                for (ec.args) |arg| {
                    self.checkExpressionForParamMutation(arg, param_indices, mutated);
                }
            },
            // Literals and compound expressions cannot be mutation targets
            // Only identifier, field_access, index can be mutated
            .integer, .float_lit, .bool_lit, .nil_lit, .string_literal, .char_literal, .unary, .binary, .compare, .logical, .call, .struct_init, .array_literal, .map_literal, .set_from, .array_type, .interpolated_string => {},
        }
    }

    /// Query if a function mutates a specific parameter
    pub fn doesMutateParam(self: *const MutationAnalyzer, func_name: []const u8, param_idx: usize) bool {
        if (self.function_mutations.get(func_name)) |mutated| {
            if (param_idx < 64) {
                return (mutated.bits & (@as(u64, 1) << @intCast(param_idx))) != 0;
            }
        }
        // Unknown function or param index out of range - assume no mutation (borrow)
        return false;
    }
};

// ============================================================================
// Semantic Analyzer - Type Analysis Pass
// ============================================================================

/// Performs semantic analysis: type registration, interface conformance checking,
/// and variable collection. Runs before IR generation.
pub const SemanticAnalyzer = struct {
    allocator: std.mem.Allocator,

    // Type registration maps
    type_map: std.StringHashMapUnmanaged(TypeInfo),
    func_map: std.StringHashMapUnmanaged(FuncInfo),
    interface_map: std.StringHashMapUnmanaged(InterfaceInfo),

    // AST declarations for conformance checking
    type_decl_map: std.StringHashMapUnmanaged(ast.TypeDecl),
    enum_decl_map: std.StringHashMapUnmanaged(ast.EnumDecl),

    // Generic type parameter bindings (e.g., "Element" -> "int" for Array of int)
    generic_params: std.StringHashMapUnmanaged([]const u8),

    // Variables captured for LSP
    captured_vars: std.ArrayListUnmanaged(SemanticVarInfo),

    // Track temporary string allocations during type checking
    allocated_type_strings: std.ArrayListUnmanaged([]const u8),

    // Current context for variable collection
    current_func_name: ?[]const u8 = null,
    current_type_name: ?[]const u8 = null,

    // Mutation analyzer (embedded)
    mutation_analyzer: MutationAnalyzer,

    pub fn init(allocator: std.mem.Allocator) SemanticAnalyzer {
        return .{
            .allocator = allocator,
            .type_map = .{},
            .func_map = .{},
            .interface_map = .{},
            .type_decl_map = .{},
            .enum_decl_map = .{},
            .generic_params = .{},
            .captured_vars = .{},
            .allocated_type_strings = .{},
            .mutation_analyzer = MutationAnalyzer.init(allocator),
        };
    }

    pub fn deinit(self: *SemanticAnalyzer) void {
        self.mutation_analyzer.deinit();
        self.type_map.deinit(self.allocator);
        self.func_map.deinit(self.allocator);
        self.interface_map.deinit(self.allocator);
        self.type_decl_map.deinit(self.allocator);
        self.enum_decl_map.deinit(self.allocator);
        self.generic_params.deinit(self.allocator);
        self.captured_vars.deinit(self.allocator);
        for (self.allocated_type_strings.items) |str| {
            self.allocator.free(str);
        }
        self.allocated_type_strings.deinit(self.allocator);
    }

    // ------------------------------------------------------------------------
    // Main Analysis Entry Point
    // ------------------------------------------------------------------------

    /// Analyze the program and return semantic information.
    /// This is the main entry point for semantic analysis.
    pub fn analyze(
        self: *SemanticAnalyzer,
        program: ast.Program,
        external_types: []const ExternalTypeInfo,
        external_funcs: []const ExternalFuncSignature,
        external_interfaces: []const ExternalInterfaceInfo,
    ) !SemanticInfo {
        // 1. Register primitive types
        try self.registerPrimitives();

        // 2. Register compiler-internal types
        try self.registerCompilerInternalTypes();

        // 3. Register compiler-internal intrinsics (for LSP hover)
        try self.registerIntrinsics();

        // 4. Register externals (stdlib)
        for (external_types) |ext_type| {
            try self.registerExternalType(ext_type);
        }
        for (external_funcs) |ext_func| {
            try self.registerExternalFunction(ext_func);
        }
        for (external_interfaces) |ext_iface| {
            try self.registerExternalInterface(ext_iface);
        }

        // 4. Register user interfaces (must be before types for conformance checking)
        for (program.interfaces) |iface| {
            try self.registerInterface(iface);
        }

        // 5. Register user types and enums
        for (program.types) |type_decl| {
            try self.registerType(type_decl);
        }
        for (program.enums) |enum_decl| {
            try self.registerEnum(enum_decl);
        }

        // 6. Register functions
        for (program.functions) |fn_decl| {
            try self.registerFunction(fn_decl);
        }

        // 7. Register methods
        for (program.types) |type_decl| {
            // Set up generic params for the type
            for (type_decl.generic_params) |param| {
                try self.generic_params.put(self.allocator, param, "int");
            }
            for (type_decl.methods) |method| {
                try self.registerMethod(type_decl.name, method);
            }
            // Clear generic params
            for (type_decl.generic_params) |param| {
                _ = self.generic_params.remove(param);
            }
        }
        for (program.enums) |enum_decl| {
            for (enum_decl.methods) |method| {
                try self.registerMethod(enum_decl.name, method);
            }
        }

        // 8. Discover and register monomorphized types
        try self.discoverMonomorphizedTypes(program);

        // 9. Mutation analysis
        try self.mutation_analyzer.analyze(program);

        // 10. Collect variables from function and method bodies
        try self.collectAllVariables(program);

        // 11. Build and return SemanticInfo
        return self.buildSemanticInfo();
    }

    // ------------------------------------------------------------------------
    // Primitive and Internal Type Registration
    // ------------------------------------------------------------------------

    fn registerPrimitives(self: *SemanticAnalyzer) !void {
        try self.type_map.put(self.allocator, "int", .{ .primitive = .i64 });
        try self.type_map.put(self.allocator, "float", .{ .primitive = .f64 });
        try self.type_map.put(self.allocator, "bool", .{ .primitive = .i64 });
        try self.type_map.put(self.allocator, "byte", .{ .primitive = .i64 });
        try self.type_map.put(self.allocator, "ptr", .{ .primitive = .ptr });
    }

    fn registerCompilerInternalTypes(self: *SemanticAnalyzer) !void {
        // __ManagedArray
        const managed_array_fields = try self.allocator.alloc(FieldInfo, 3);
        managed_array_fields[0] = .{ .name = "_buffer", .offset = 0, .size = 8, .value_type = .{ .primitive = "ptr" } };
        managed_array_fields[1] = .{ .name = "_len", .offset = 8, .size = 8, .value_type = .{ .primitive = "int" } };
        managed_array_fields[2] = .{ .name = "_capacity", .offset = 16, .size = 8, .value_type = .{ .primitive = "int" } };
        try self.type_map.put(self.allocator, "__ManagedArray", .{
            .struct_type = .{ .name = "__ManagedArray", .fields = managed_array_fields, .size = 24 },
        });

        // __ManagedString
        const managed_string_fields = try self.allocator.alloc(FieldInfo, 5);
        managed_string_fields[0] = .{ .name = "_buffer", .offset = 0, .size = 8, .value_type = .{ .primitive = "ptr" } };
        managed_string_fields[1] = .{ .name = "_len", .offset = 8, .size = 4, .value_type = .{ .primitive = "int" } };
        managed_string_fields[2] = .{ .name = "_cap_flags", .offset = 12, .size = 4, .value_type = .{ .primitive = "int" } };
        managed_string_fields[3] = .{ .name = "_refcount", .offset = 16, .size = 4, .value_type = .{ .primitive = "int" } };
        managed_string_fields[4] = .{ .name = "_parent_off", .offset = 20, .size = 4, .value_type = .{ .primitive = "int" } };
        try self.type_map.put(self.allocator, "__ManagedString", .{
            .struct_type = .{ .name = "__ManagedString", .fields = managed_string_fields, .size = 24 },
        });

        // cstring
        const cstring_fields = try self.allocator.alloc(FieldInfo, 3);
        cstring_fields[0] = .{ .name = "data", .offset = 0, .size = 8, .value_type = .{ .primitive = "ptr" } };
        cstring_fields[1] = .{ .name = "length", .offset = 8, .size = 8, .value_type = .{ .primitive = "int" } };
        cstring_fields[2] = .{ .name = "managed", .offset = 16, .size = 8, .value_type = .{ .primitive = "ptr" } };
        try self.type_map.put(self.allocator, "cstring", .{
            .struct_type = .{ .name = "cstring", .fields = cstring_fields, .size = 24 },
        });
    }

    fn registerIntrinsics(self: *SemanticAnalyzer) !void {
        // Register all intrinsics from the central registry
        for (intrinsics_registry.allIntrinsics()) |intr| {
            // Convert registry params to ParamType slice
            const param_types = try self.allocator.alloc(ParamType, intr.params.len);
            for (intr.params, 0..) |param, i| {
                param_types[i] = .{ .name = param.name, .ty = .{ .primitive = param.type_name } };
            }

            try self.func_map.put(self.allocator, intr.name, .{
                .return_type = intr.return_ir_type,
                .return_type_name = intr.return_type_name,
                .return_value_type = null,
                .param_types = param_types,
                .doc_comment = if (intr.help_text.len > 0) intr.help_text else null,
            });
        }
    }

    // ------------------------------------------------------------------------
    // External Registration (from stdlib)
    // ------------------------------------------------------------------------

    fn registerExternalType(self: *SemanticAnalyzer, ext_type: ExternalTypeInfo) !void {
        // Convert external fields to FieldInfo
        const fields = try self.allocator.alloc(FieldInfo, ext_type.fields.len);
        for (ext_type.fields, 0..) |ef, i| {
            fields[i] = .{
                .name = ef.name,
                .offset = ef.offset,
                .size = ef.size,
                .value_type = .{ .primitive = ef.type_name },
            };
        }
        try self.type_map.put(self.allocator, ext_type.name, .{
            .struct_type = .{ .name = ext_type.name, .fields = fields, .size = ext_type.size },
        });

        // Store type_decl if provided (for generic types)
        if (ext_type.type_decl) |td| {
            try self.type_decl_map.put(self.allocator, ext_type.name, td.*);
        }
    }

    fn registerExternalFunction(self: *SemanticAnalyzer, ext_func: ExternalFuncSignature) !void {
        try self.func_map.put(self.allocator, ext_func.name, .{
            .return_type = ext_func.return_type,
            .return_type_name = ext_func.return_type_name,
            .return_value_type = ext_func.return_value_type,
            .param_types = ext_func.param_types,
            .doc_comment = ext_func.doc_comment,
            .ir_generated = true,
        });
    }

    fn registerExternalInterface(self: *SemanticAnalyzer, ext_iface: ExternalInterfaceInfo) !void {
        try self.interface_map.put(self.allocator, ext_iface.interface_decl.name, .{
            .name = ext_iface.interface_decl.name,
            .methods = ext_iface.interface_decl.methods,
            .associated_types = ext_iface.interface_decl.generic_params,
            .extends = ext_iface.interface_decl.extends,
        });
    }

    // ------------------------------------------------------------------------
    // User Type Registration
    // ------------------------------------------------------------------------

    fn registerInterface(self: *SemanticAnalyzer, iface: ast.InterfaceDecl) !void {
        try self.interface_map.put(self.allocator, iface.name, .{
            .name = iface.name,
            .methods = iface.methods,
            .associated_types = iface.generic_params,
            .extends = iface.extends,
            .decl_line = iface.block.start_line,
            .decl_column = iface.block.start_column,
        });
    }

    fn registerType(self: *SemanticAnalyzer, type_decl: ast.TypeDecl) !void {
        // Calculate field offsets and build FieldInfo array
        const fields = try self.buildFieldInfos(type_decl.fields);
        const size = self.calculateStructSize(fields);

        try self.type_map.put(self.allocator, type_decl.name, .{
            .struct_type = .{
                .name = type_decl.name,
                .fields = fields,
                .size = size,
                .decl_line = type_decl.block.start_line,
                .decl_column = type_decl.block.start_column,
            },
        });

        // Store for conformance checking
        try self.type_decl_map.put(self.allocator, type_decl.name, type_decl);
    }

    fn registerEnum(self: *SemanticAnalyzer, enum_decl: ast.EnumDecl) !void {
        var members: std.StringHashMapUnmanaged(i64) = .{};
        var tag: i64 = 0;

        for (enum_decl.members) |member| {
            // If member has explicit value, use it; otherwise use auto-incrementing tag
            if (member.value) |value_expr| {
                if (value_expr.* == .integer) {
                    tag = value_expr.integer;
                }
            }
            try members.put(self.allocator, member.name, tag);
            tag += 1;
        }

        // Check if this enum conforms to Error interface
        var is_error = false;
        for (enum_decl.conformances) |conf| {
            if (std.mem.eql(u8, conf.interface_name, "Error")) {
                is_error = true;
                break;
            }
        }

        try self.type_map.put(self.allocator, enum_decl.name, .{
            .enum_type = .{
                .name = enum_decl.name,
                .members = members,
                .is_error = is_error,
                .decl_line = enum_decl.block.start_line,
                .decl_column = enum_decl.block.start_column,
            },
        });

        // Store for conformance checking
        try self.enum_decl_map.put(self.allocator, enum_decl.name, enum_decl);
    }

    fn registerFunction(self: *SemanticAnalyzer, decl: ast.FunctionDecl) !void {
        const param_types = try self.buildParamTypes(decl.params);
        const return_info: ReturnInfo = if (decl.return_type) |rt| try self.typeExprToReturnInfo(rt) else .{ .ir_type = .void, .type_name = null, .value_type = null };

        try self.func_map.put(self.allocator, decl.name, .{
            .return_type = return_info.ir_type,
            .return_type_name = return_info.type_name,
            .return_value_type = return_info.value_type,
            .param_types = param_types,
            .doc_comment = decl.doc_comment,
            .ir_generated = false, // Will be set true when IR is generated
            .decl_line = decl.block.start_line,
            .decl_column = decl.block.start_column,
        });
    }

    fn registerMethod(self: *SemanticAnalyzer, type_name: []const u8, method: ast.MethodDecl) !void {
        const param_types = try self.buildParamTypes(method.params);
        const return_info: ReturnInfo = if (method.return_type) |rt| try self.typeExprToReturnInfo(rt) else .{ .ir_type = .void, .type_name = null, .value_type = null };

        // Build mangled method name: TypeName$methodName
        const mangled = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, method.name });
        try self.allocated_type_strings.append(self.allocator, mangled);

        try self.func_map.put(self.allocator, mangled, .{
            .return_type = return_info.ir_type,
            .return_type_name = return_info.type_name,
            .return_value_type = return_info.value_type,
            .param_types = param_types,
            .doc_comment = method.doc_comment,
            .ir_generated = false,
        });
    }

    // ------------------------------------------------------------------------
    // Helper Functions
    // ------------------------------------------------------------------------

    fn buildFieldInfos(self: *SemanticAnalyzer, fields: []const ast.FieldDecl) ![]const FieldInfo {
        const result = try self.allocator.alloc(FieldInfo, fields.len);
        var offset: i32 = 0;

        for (fields, 0..) |field, i| {
            const value_type = try self.typeExprToValueType(field.type_expr);
            const display_name = self.typeExprToDisplayName(field.type_expr);
            const size: i32 = 8; // All types are 8 bytes (pointers or i64/f64)

            result[i] = .{
                .name = field.name,
                .offset = offset,
                .size = size,
                .value_type = value_type,
                .display_name = display_name,
                .is_mutable = field.is_mutable,
                .is_export = field.is_export,
            };
            offset += size;
        }

        return result;
    }

    fn calculateStructSize(self: *SemanticAnalyzer, fields: []const FieldInfo) i32 {
        _ = self;
        if (fields.len == 0) return 0;
        const last = fields[fields.len - 1];
        return last.offset + last.size;
    }

    fn buildParamTypes(self: *SemanticAnalyzer, params: []const ast.ParamDecl) ![]const ParamType {
        const result = try self.allocator.alloc(ParamType, params.len);
        for (params, 0..) |param, i| {
            result[i] = .{
                .ty = try self.typeExprToValueType(param.type_expr),
                .name = param.name,
                .display_name = self.typeExprToDisplayName(param.type_expr),
                .default_value = param.default_value,
            };
        }
        return result;
    }

    const ReturnInfo = struct {
        ir_type: ir.Type,
        type_name: ?[]const u8,
        value_type: ?ValueType,
    };

    fn typeExprToReturnInfo(self: *SemanticAnalyzer, type_expr: ast.TypeExpr) !ReturnInfo {
        const vt = try self.typeExprToValueType(type_expr);
        return .{
            .ir_type = vt.toPrimitiveType(),
            .type_name = vt.getTypeName(),
            .value_type = vt,
        };
    }

    fn typeExprToValueType(self: *SemanticAnalyzer, type_expr: ast.TypeExpr) !ValueType {
        return switch (type_expr) {
            .simple => |name| {
                // Check for generic parameter substitution
                if (self.generic_params.get(name)) |concrete| {
                    return self.simpleNameToValueType(concrete);
                }
                return self.simpleNameToValueType(name);
            },
            .generic => |g| {
                // For generic types like "Array of int", return array type info
                if (std.mem.eql(u8, g.base_type, "Array") or std.mem.eql(u8, g.base_type, "array")) {
                    if (g.type_args.len > 0) {
                        const elem_name = g.type_args[0];
                        const resolved = self.generic_params.get(elem_name) orelse elem_name;
                        return ValueType{ .array_type = .{
                            .element_type = types.nameToIrType(resolved),
                            .size = null,
                            .storage = .heap,
                            .element_struct_type = if (self.type_map.get(resolved)) |ti| if (ti == .struct_type) resolved else null else null,
                        } };
                    }
                }
                // Other generic types treated as struct
                return ValueType{ .struct_type = g.base_type };
            },
            .optional => |wrapped| {
                const wrapped_vt = try self.typeExprToValueType(wrapped.*);
                return ValueType{ .optional_type = .{
                    .wrapped = wrapped_vt.toPrimitiveType(),
                    .wrapped_struct_type = if (wrapped_vt == .struct_type) wrapped_vt.struct_type else null,
                    .wrapped_enum_type = if (wrapped_vt == .enum_type) wrapped_vt.enum_type else null,
                } };
            },
            .error_union => |eu| {
                const success_vt = try self.typeExprToValueType(eu.success_type.*);
                return ValueType{ .error_union_type = .{
                    .success_type = success_vt.toPrimitiveType(),
                    .success_struct_type = if (success_vt == .struct_type) success_vt.struct_type else null,
                    .error_enum_type = eu.error_type,
                } };
            },
            .function_type => |ft| {
                const param_types = try self.allocator.alloc(ValueType, ft.param_types.len);
                for (ft.param_types, 0..) |pt, i| {
                    param_types[i] = try self.typeExprToValueType(pt);
                }
                var return_type: ?*const ValueType = null;
                var return_ir_type: ir.Type = .void;
                if (ft.return_type) |rt| {
                    const rt_ptr = try self.allocator.create(ValueType);
                    rt_ptr.* = try self.typeExprToValueType(rt.*);
                    return_type = rt_ptr;
                    return_ir_type = rt_ptr.toPrimitiveType();
                }
                return ValueType{ .function_type = .{
                    .param_types = param_types,
                    .return_type = return_type,
                    .return_ir_type = return_ir_type,
                } };
            },
        };
    }

    fn simpleNameToValueType(self: *SemanticAnalyzer, name: []const u8) ValueType {
        // Check if it's a known type
        if (self.type_map.get(name)) |type_info| {
            return switch (type_info) {
                .primitive => ValueType{ .primitive = name },
                .struct_type => ValueType{ .struct_type = name },
                .enum_type => ValueType{ .enum_type = name },
            };
        }
        // Default to primitive for unknown types
        return ValueType{ .primitive = name };
    }

    /// Convert a TypeExpr to a human-readable display string (e.g., "Array of Point")
    fn typeExprToDisplayName(self: *SemanticAnalyzer, type_expr: ast.TypeExpr) ?[]const u8 {
        return switch (type_expr) {
            .simple => |name| name,
            .generic => |gen| {
                // Format as "BaseType of Arg1, Arg2"
                var buf: std.ArrayListUnmanaged(u8) = .empty;
                buf.appendSlice(self.allocator, gen.base_type) catch return null;
                if (gen.type_args.len > 0) {
                    buf.appendSlice(self.allocator, " of ") catch return null;
                    for (gen.type_args, 0..) |arg, i| {
                        if (i > 0) buf.appendSlice(self.allocator, ", ") catch return null;
                        buf.appendSlice(self.allocator, arg) catch return null;
                    }
                }
                const result = buf.toOwnedSlice(self.allocator) catch return null;
                self.allocated_type_strings.append(self.allocator, result) catch {
                    self.allocator.free(result);
                    return null;
                };
                return result;
            },
            .optional => |opt| {
                const inner = self.typeExprToDisplayName(opt.*) orelse return null;
                const result = std.fmt.allocPrint(self.allocator, "{s}?", .{inner}) catch return null;
                self.allocated_type_strings.append(self.allocator, result) catch {
                    self.allocator.free(result);
                    return null;
                };
                return result;
            },
            .error_union => |eu| {
                const success = self.typeExprToDisplayName(eu.success_type.*) orelse return null;
                const result = std.fmt.allocPrint(self.allocator, "{s} or {s}", .{ success, eu.error_type }) catch return null;
                self.allocated_type_strings.append(self.allocator, result) catch {
                    self.allocator.free(result);
                    return null;
                };
                return result;
            },
            .function_type => "function",
        };
    }

    // ------------------------------------------------------------------------
    // Monomorphization Discovery
    // ------------------------------------------------------------------------

    /// Walk the AST to discover all generic type instantiations and register them.
    fn discoverMonomorphizedTypes(self: *SemanticAnalyzer, program: ast.Program) !void {
        // Walk function bodies
        for (program.functions) |fn_decl| {
            try self.discoverInStatements(fn_decl.body);
        }

        // Walk type method bodies
        for (program.types) |type_decl| {
            // Set up generic params for the type context
            for (type_decl.generic_params) |param| {
                try self.generic_params.put(self.allocator, param, "int");
            }
            for (type_decl.methods) |method| {
                try self.discoverInStatements(method.body);
            }
            for (type_decl.generic_params) |param| {
                _ = self.generic_params.remove(param);
            }
        }

        // Walk enum method bodies
        for (program.enums) |enum_decl| {
            for (enum_decl.methods) |method| {
                try self.discoverInStatements(method.body);
            }
        }
    }

    fn discoverInStatements(self: *SemanticAnalyzer, stmts: []const ast.Statement) std.mem.Allocator.Error!void {
        for (stmts) |stmt| {
            switch (stmt.kind) {
                .var_decl, .let_decl => |decl| {
                    try self.discoverInExpression(decl.value);
                },
                .assign => |assign| {
                    try self.discoverInExpression(assign.value);
                },
                .index_assign => |idx_assign| {
                    try self.discoverInExpression(idx_assign.base.*);
                    try self.discoverInExpression(idx_assign.index.*);
                    try self.discoverInExpression(idx_assign.value);
                },
                .field_assign => |fa| {
                    try self.discoverInExpression(fa.base.*);
                    try self.discoverInExpression(fa.value);
                },
                .@"return" => |ret| {
                    if (ret.value) |val| {
                        try self.discoverInExpression(val);
                    }
                },
                .call => |call| {
                    for (call.args) |arg| {
                        try self.discoverInExpression(arg);
                    }
                },
                .method_call => |mcall| {
                    try self.discoverInExpression(mcall.base.*);
                    for (mcall.args) |arg| {
                        try self.discoverInExpression(arg);
                    }
                },
                .if_stmt => |if_s| {
                    try self.discoverInExpression(if_s.condition);
                    for (if_s.children) |child| {
                        switch (child.role) {
                            .primary, .else_clause => try self.discoverInStatements(child.statements),
                            .else_if => {
                                // child.statements[0] is a nested if_stmt
                                if (child.statements.len > 0) {
                                    if (child.statements[0].kind == .if_stmt) {
                                        const nested_if = child.statements[0].kind.if_stmt;
                                        try self.discoverInExpression(nested_if.condition);
                                        try self.discoverInIfChildren(nested_if.children);
                                    }
                                }
                            },
                            else => {},
                        }
                    }
                },
                .while_stmt => |while_s| {
                    try self.discoverInExpression(while_s.condition);
                    for (while_s.children) |child| {
                        try self.discoverInStatements(child.statements);
                    }
                },
                .for_stmt => |for_s| {
                    try self.discoverInExpression(for_s.iterable);
                    for (for_s.children) |child| {
                        try self.discoverInStatements(child.statements);
                    }
                },
                .do_catch_stmt => |dc| {
                    for (dc.children) |child| {
                        try self.discoverInStatements(child.statements);
                    }
                },
                .match_stmt => |ms| {
                    try self.discoverInExpression(ms.scrutinee);
                    for (ms.children) |child| {
                        try self.discoverInStatements(child.statements);
                    }
                },
                .throw_stmt => |throw_s| {
                    try self.discoverInExpression(throw_s.error_expr);
                },
                .else_unwrap_decl => |unwrap| {
                    try self.discoverInExpression(unwrap.optional_expr.*);
                    for (unwrap.children) |child| {
                        try self.discoverInStatements(child.statements);
                    }
                },
                .guard_let_decl => |guard| {
                    try self.discoverInExpression(guard.optional_expr.*);
                    for (guard.children) |child| {
                        try self.discoverInStatements(child.statements);
                    }
                },
                .break_stmt, .continue_stmt => {},
            }
        }
    }

    /// Helper to recursively discover in if statement children (handles else-if chains)
    fn discoverInIfChildren(self: *SemanticAnalyzer, children: []const ast.ChildBlock) std.mem.Allocator.Error!void {
        for (children) |child| {
            switch (child.role) {
                .primary, .else_clause => try self.discoverInStatements(child.statements),
                .else_if => {
                    if (child.statements.len > 0) {
                        if (child.statements[0].kind == .if_stmt) {
                            const nested_if = child.statements[0].kind.if_stmt;
                            try self.discoverInExpression(nested_if.condition);
                            try self.discoverInIfChildren(nested_if.children);
                        }
                    }
                },
                else => {},
            }
        }
    }

    fn discoverInExpression(self: *SemanticAnalyzer, expr: ast.Expression) std.mem.Allocator.Error!void {
        switch (expr) {
            .struct_init => |sinit| {
                // This is the main monomorphization trigger
                if (sinit.type_args.len > 0) {
                    _ = try self.getOrCreateMonomorphizedType(sinit.type_name, sinit.type_args);
                }
                for (sinit.fields) |field| {
                    try self.discoverInExpression(field.value.*);
                }
            },
            .array_literal => |arr| {
                // Array literals create Array$ElementType
                if (arr.elements.len > 0) {
                    const elem_type = self.inferExpressionType(arr.elements[0]);
                    if (elem_type) |et| {
                        if (et.getTypeName()) |elem_name| {
                            var type_args = [_][]const u8{elem_name};
                            _ = try self.getOrCreateMonomorphizedType("Array", &type_args);
                        }
                    }
                }
                for (arr.elements) |elem| {
                    try self.discoverInExpression(elem);
                }
            },
            .map_literal => |map| {
                // Map literals create Map$KeyType$ValueType
                if (map.entries.len > 0) {
                    const key_type = self.inferExpressionType(map.entries[0].key.*);
                    const value_type = self.inferExpressionType(map.entries[0].value.*);
                    if (key_type) |kt| {
                        if (value_type) |vt| {
                            if (kt.getTypeName()) |key_name| {
                                if (vt.getTypeName()) |val_name| {
                                    var type_args = [_][]const u8{ key_name, val_name };
                                    _ = try self.getOrCreateMonomorphizedType("Map", &type_args);
                                }
                            }
                        }
                    }
                }
                for (map.entries) |entry| {
                    try self.discoverInExpression(entry.key.*);
                    try self.discoverInExpression(entry.value.*);
                }
            },
            .set_from => |sf| {
                // Set literals create Set$ElementType
                if (sf.type_name.len > 0) {
                    // Type is explicit: Set from [elements]
                    const elem_type = self.inferExpressionType(sf.elements.*);
                    if (elem_type) |et| {
                        if (et == .array_type) {
                            const elem_name = if (et.array_type.element_struct_type) |st| st else @tagName(et.array_type.element_type);
                            var type_args = [_][]const u8{elem_name};
                            _ = try self.getOrCreateMonomorphizedType(sf.type_name, &type_args);
                        }
                    }
                }
                try self.discoverInExpression(sf.elements.*);
            },
            .binary => |bin| {
                try self.discoverInExpression(bin.left.*);
                try self.discoverInExpression(bin.right.*);
            },
            .compare => |cmp| {
                try self.discoverInExpression(cmp.left.*);
                try self.discoverInExpression(cmp.right.*);
            },
            .logical => |log| {
                try self.discoverInExpression(log.left.*);
                try self.discoverInExpression(log.right.*);
            },
            .unary => |un| {
                try self.discoverInExpression(un.operand.*);
            },
            .call => |call| {
                for (call.args) |arg| {
                    try self.discoverInExpression(arg);
                }
            },
            .method_call => |mcall| {
                try self.discoverInExpression(mcall.base.*);
                for (mcall.args) |arg| {
                    try self.discoverInExpression(arg);
                }
            },
            .field_access => |fa| {
                try self.discoverInExpression(fa.base.*);
            },
            .index => |idx| {
                try self.discoverInExpression(idx.base.*);
                try self.discoverInExpression(idx.index.*);
            },
            .cast => |c| {
                try self.discoverInExpression(c.expr.*);
            },
            .nil_coalesce => |nc| {
                try self.discoverInExpression(nc.optional.*);
                try self.discoverInExpression(nc.default.*);
            },
            .try_expr => |te| {
                try self.discoverInExpression(te.expr.*);
            },
            .closure => |clos| {
                try self.discoverInExpression(clos.body.*);
            },
            .match_expr => |me| {
                try self.discoverInExpression(me.scrutinee.*);
                for (me.cases) |case| {
                    try self.discoverInExpression(case.result);
                }
                if (me.default_expr) |de| {
                    try self.discoverInExpression(de.*);
                }
            },
            .interpolated_string => |interp| {
                for (interp.parts) |part| {
                    if (part.expr) |e| {
                        try self.discoverInExpression(e.*);
                    }
                }
            },
            .enum_case => |ec| {
                for (ec.args) |arg| {
                    try self.discoverInExpression(arg);
                }
            },
            // Literals and simple expressions don't trigger monomorphization
            .integer, .float_lit, .bool_lit, .nil_lit, .string_literal, .char_literal, .identifier, .self_expr, .array_type => {},
        }
    }

    /// Get or create a monomorphized version of a generic type.
    /// Returns the monomorphized type name (e.g., "Array$int").
    fn getOrCreateMonomorphizedType(self: *SemanticAnalyzer, base_type: []const u8, type_args: []const []const u8) ![]const u8 {
        // Build the monomorphized name: TypeName$Arg1$Arg2
        var name_parts: std.ArrayListUnmanaged(u8) = .empty;
        defer name_parts.deinit(self.allocator);

        try name_parts.appendSlice(self.allocator, base_type);
        for (type_args) |arg| {
            // Resolve generic parameter if needed
            const resolved = self.generic_params.get(arg) orelse arg;
            try name_parts.append(self.allocator, '$');
            try name_parts.appendSlice(self.allocator, resolved);
        }

        const mono_name = try self.allocator.dupe(u8, name_parts.items);
        errdefer self.allocator.free(mono_name);

        // Check if already registered
        if (self.type_map.contains(mono_name)) {
            self.allocator.free(mono_name);
            // Return the existing name from the map
            var iter = self.type_map.iterator();
            while (iter.next()) |entry| {
                if (std.mem.eql(u8, entry.key_ptr.*, name_parts.items)) {
                    return entry.key_ptr.*;
                }
            }
            unreachable;
        }

        // Track the allocated name
        try self.allocated_type_strings.append(self.allocator, mono_name);

        // Get the original generic type declaration
        const type_decl = self.type_decl_map.get(base_type) orelse {
            // Type not found - might be a stdlib type not yet loaded
            return mono_name;
        };

        // Verify we have the right number of type arguments
        if (type_decl.generic_params.len != type_args.len) {
            return mono_name;
        }

        // Set up generic parameter mappings
        var saved_params = try self.allocator.alloc(?[]const u8, type_decl.generic_params.len);
        defer self.allocator.free(saved_params);
        for (type_decl.generic_params, 0..) |param, i| {
            saved_params[i] = self.generic_params.get(param);
            const resolved = self.generic_params.get(type_args[i]) orelse type_args[i];
            try self.generic_params.put(self.allocator, param, resolved);
        }

        // Register the monomorphized type
        const fields = try self.buildFieldInfos(type_decl.fields);
        const size = self.calculateStructSize(fields);

        try self.type_map.put(self.allocator, mono_name, .{
            .struct_type = .{ .name = mono_name, .fields = fields, .size = size },
        });

        // Also store the type_decl for the monomorphized type (needed for method lookup)
        try self.type_decl_map.put(self.allocator, mono_name, type_decl);

        // Register method signatures for the monomorphized type
        for (type_decl.methods) |method| {
            try self.registerMethod(mono_name, method);
        }

        // Restore generic params
        for (type_decl.generic_params, 0..) |param, i| {
            if (saved_params[i]) |prev_value| {
                try self.generic_params.put(self.allocator, param, prev_value);
            } else {
                _ = self.generic_params.remove(param);
            }
        }

        return mono_name;
    }

    // ------------------------------------------------------------------------
    // Variable Collection (for LSP)
    // ------------------------------------------------------------------------

    fn collectAllVariables(self: *SemanticAnalyzer, program: ast.Program) !void {
        // Collect from functions
        for (program.functions) |fn_decl| {
            self.current_func_name = fn_decl.name;
            self.current_type_name = null;
            try self.collectParametersAsVariables(fn_decl.params);
            try self.collectVariablesFromBody(fn_decl.body);
        }

        // Collect from type methods
        for (program.types) |type_decl| {
            for (type_decl.generic_params) |param| {
                self.generic_params.put(self.allocator, param, "int") catch {};
            }
            for (type_decl.methods) |method| {
                self.current_func_name = method.name;
                self.current_type_name = type_decl.name;
                try self.collectParametersAsVariables(method.params);
                try self.collectVariablesFromBody(method.body);
            }
            for (type_decl.generic_params) |param| {
                _ = self.generic_params.remove(param);
            }
        }

        // Collect from enum methods
        for (program.enums) |enum_decl| {
            for (enum_decl.methods) |method| {
                self.current_func_name = method.name;
                self.current_type_name = enum_decl.name;
                try self.collectParametersAsVariables(method.params);
                try self.collectVariablesFromBody(method.body);
            }
        }
    }

    fn collectParametersAsVariables(self: *SemanticAnalyzer, params: []const ast.ParamDecl) !void {
        for (params) |param| {
            const param_type = self.typeExprToValueType(param.type_expr) catch continue;
            const display_name = self.typeExprToDisplayName(param.type_expr);
            try self.addVariableInfo(param.name, param_type, display_name, true, true, 0, 0);
        }
    }

    /// Helper to add a variable to the captured_vars list with current context
    fn addVariableInfo(
        self: *SemanticAnalyzer,
        name: []const u8,
        ty: ValueType,
        display_name: ?[]const u8,
        is_mutable: bool,
        is_parameter: bool,
        line: u32,
        column: u32,
    ) !void {
        try self.captured_vars.append(self.allocator, .{
            .name = name,
            .ty = ty,
            .display_name = display_name,
            .is_mutable = is_mutable,
            .is_parameter = is_parameter,
            .borrow_state = .none,
            .borrowed_from = null,
            .decl_line = line,
            .decl_column = column,
            .function_name = self.current_func_name orelse "",
            .type_name = self.current_type_name,
        });
    }

    fn collectVariablesFromBody(self: *SemanticAnalyzer, body: []const ast.Statement) std.mem.Allocator.Error!void {
        for (body) |stmt| {
            switch (stmt.kind) {
                .var_decl, .let_decl => |decl| {
                    const var_type, const display_name = if (decl.type_annotation) |ann| blk: {
                        break :blk .{ self.typeExprToValueType(ann) catch continue, self.typeExprToDisplayName(ann) };
                    } else blk: {
                        const vt = self.inferExpressionType(decl.value) orelse continue;
                        // Infer display name from the expression type
                        const dn = self.inferDisplayName(decl.value);
                        break :blk .{ vt, dn };
                    };
                    const is_mutable = stmt.kind == .var_decl;
                    try self.addVariableInfo(decl.name, var_type, display_name, is_mutable, false, stmt.line, stmt.column);
                },
                .if_stmt => |if_s| {
                    try self.collectVariablesFromIfChildren(if_s.children);
                },
                .while_stmt => |while_s| {
                    for (while_s.children) |child| {
                        try self.collectVariablesFromBody(child.statements);
                    }
                },
                .for_stmt => |for_s| {
                    // For loop variable - get element type from the iterable
                    const iter_type = self.inferExpressionType(for_s.iterable) orelse ValueType{ .primitive = "int" };
                    const elem_type: ValueType, const elem_display: ?[]const u8 = if (iter_type == .array_type) blk: {
                        const arr = iter_type.array_type;
                        // Use element_struct_type if available, otherwise use IR type name
                        const display = arr.element_struct_type orelse @tagName(arr.element_type);
                        break :blk .{ ValueType{ .primitive = @tagName(arr.element_type) }, display };
                    } else .{ ValueType{ .primitive = "int" }, "int" };
                    try self.addVariableInfo(for_s.var_name, elem_type, elem_display, false, false, 0, 0);
                    for (for_s.children) |child| {
                        try self.collectVariablesFromBody(child.statements);
                    }
                },
                .do_catch_stmt => |dc| {
                    for (dc.children) |child| {
                        try self.collectVariablesFromBody(child.statements);
                    }
                },
                .match_stmt => |ms| {
                    for (ms.children) |child| {
                        try self.collectVariablesFromBody(child.statements);
                    }
                },
                else => {},
            }
        }
    }

    /// Helper to recursively collect variables from if statement children (handles else-if chains)
    fn collectVariablesFromIfChildren(self: *SemanticAnalyzer, children: []const ast.ChildBlock) std.mem.Allocator.Error!void {
        for (children) |child| {
            switch (child.role) {
                .primary, .else_clause => try self.collectVariablesFromBody(child.statements),
                .else_if => {
                    if (child.statements.len > 0) {
                        if (child.statements[0].kind == .if_stmt) {
                            const nested_if = child.statements[0].kind.if_stmt;
                            try self.collectVariablesFromIfChildren(nested_if.children);
                        }
                    }
                },
                else => {},
            }
        }
    }

    fn inferExpressionType(self: *SemanticAnalyzer, expr: ast.Expression) ?ValueType {
        return switch (expr) {
            .integer => ValueType{ .primitive = "int" },
            .float_lit => ValueType{ .primitive = "float" },
            .bool_lit => ValueType{ .primitive = "bool" },
            .nil_lit => ValueType{ .optional_type = .{ .wrapped = .i64 } },
            .string_literal => ValueType{ .struct_type = "String" },
            .char_literal => ValueType{ .struct_type = "Character" },
            .identifier => |name| blk: {
                // Check if it's a known type or enum
                if (self.type_map.get(name)) |ti| {
                    break :blk switch (ti) {
                        .primitive => ValueType{ .primitive = name },
                        .struct_type => ValueType{ .struct_type = name },
                        .enum_type => ValueType{ .enum_type = name },
                    };
                }
                break :blk null;
            },
            .array_literal => |arr| blk: {
                if (arr.elements.len > 0) {
                    if (self.inferExpressionType(arr.elements[0])) |elem_ty| {
                        break :blk ValueType{ .array_type = .{
                            .element_type = elem_ty.toPrimitiveType(),
                            .size = null,
                            .element_struct_type = if (elem_ty == .struct_type) elem_ty.struct_type else null,
                            .storage = .heap,
                        } };
                    }
                }
                break :blk ValueType{ .array_type = .{ .element_type = .i64, .size = null, .element_struct_type = null, .storage = .heap } };
            },
            .struct_init => |sinit| ValueType{ .struct_type = sinit.type_name },
            .binary => |bin| self.inferExpressionType(bin.left.*),
            .unary => |un| self.inferExpressionType(un.operand.*),
            .compare => ValueType{ .primitive = "bool" },
            .logical => ValueType{ .primitive = "bool" },
            .method_call => |mc| blk: {
                // Get base type and look up method return type
                const base_ty = self.inferExpressionType(mc.base.*) orelse break :blk null;
                const type_name = base_ty.getTypeName() orelse break :blk null;
                const mangled = std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, mc.method_name }) catch break :blk null;
                defer self.allocator.free(mangled);
                const func_info = self.func_map.get(mangled) orelse break :blk null;
                break :blk func_info.return_value_type;
            },
            .call => |call| blk: {
                const func_info = self.func_map.get(call.func_name) orelse break :blk null;
                break :blk func_info.return_value_type;
            },
            else => null,
        };
    }

    /// Infer a human-readable display name from an expression
    fn inferDisplayName(self: *SemanticAnalyzer, expr: ast.Expression) ?[]const u8 {
        return switch (expr) {
            .integer => "int",
            .float_lit => "float",
            .bool_lit => "bool",
            .nil_lit => null,
            .string_literal => "String",
            .char_literal => "Character",
            .identifier => |name| name,
            .array_literal => |arr| blk: {
                // Format as "Array of ElementType"
                if (arr.elements.len > 0) {
                    const elem_display = self.inferDisplayName(arr.elements[0]) orelse "int";
                    const result = std.fmt.allocPrint(self.allocator, "Array of {s}", .{elem_display}) catch break :blk null;
                    self.allocated_type_strings.append(self.allocator, result) catch {
                        self.allocator.free(result);
                        break :blk null;
                    };
                    break :blk result;
                }
                break :blk "Array of int";
            },
            .struct_init => |sinit| sinit.type_name,
            .binary => |bin| self.inferDisplayName(bin.left.*),
            .unary => |un| self.inferDisplayName(un.operand.*),
            .compare => "bool",
            .logical => "bool",
            .method_call => |mc| blk: {
                const base_ty = self.inferExpressionType(mc.base.*) orelse break :blk null;
                const type_name = base_ty.getTypeName() orelse break :blk null;
                const mangled = std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, mc.method_name }) catch break :blk null;
                defer self.allocator.free(mangled);
                const func_info = self.func_map.get(mangled) orelse break :blk null;
                break :blk func_info.return_type_name;
            },
            .call => |call| blk: {
                const func_info = self.func_map.get(call.func_name) orelse break :blk null;
                break :blk func_info.return_type_name;
            },
            else => null,
        };
    }

    // ------------------------------------------------------------------------
    // Build Final Result
    // ------------------------------------------------------------------------

    fn buildSemanticInfo(self: *SemanticAnalyzer) !SemanticInfo {
        // Transfer ownership of collected data to SemanticInfo
        const result = SemanticInfo{
            .allocator = self.allocator,
            .variables = try self.captured_vars.toOwnedSlice(self.allocator),
            .functions = self.func_map,
            .types = self.type_map,
            .interfaces = self.interface_map,
            .allocated_type_strings = self.allocated_type_strings,
        };

        // Clear our maps to prevent double-free
        self.func_map = .{};
        self.type_map = .{};
        self.interface_map = .{};
        self.allocated_type_strings = .{};

        return result;
    }

    /// Query if a function mutates a specific parameter
    pub fn doesMutateParam(self: *const SemanticAnalyzer, func_name: []const u8, param_idx: usize) bool {
        return self.mutation_analyzer.doesMutateParam(func_name, param_idx);
    }
};
