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

/// Optional type info
const OptionalInfo = struct {
    wrapped: ir.Type, // The underlying type (i64, f64, ptr)
    wrapped_struct_type: ?[]const u8 = null, // struct name if wrapped is a struct
};

/// Extended type info for variable tracking
const ValueType = union(enum) {
    primitive: ir.Type,
    struct_type: []const u8,
    array_type: ArrayInfo,
    enum_type: []const u8,
    optional_type: OptionalInfo,

    fn toPrimitiveType(self: ValueType) ir.Type {
        return switch (self) {
            .primitive => |p| p,
            .enum_type => .i64,
            .struct_type, .array_type => .ptr,
            .optional_type => .ptr, // Optionals are pointers to 16-byte structures
        };
    }

    fn isStruct(self: ValueType) bool {
        return self == .struct_type;
    }

    fn isOptional(self: ValueType) bool {
        return self == .optional_type;
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

/// External function signature - for cross-module compilation
pub const ExternalFuncSignature = struct {
    name: []const u8,
    return_type: ir.Type,
    return_type_name: ?[]const u8 = null, // struct type name if returning a struct
    return_value_type: ?ValueType = null, // Full return type info (for optionals, etc.)
};

/// External type info - for cross-module compilation
pub const ExternalTypeInfo = struct {
    name: []const u8,
    size: i32,
    fields: []const ExternalFieldInfo,
    /// Original type declaration for generic types (needed for monomorphization)
    type_decl: ?*const ast.TypeDecl = null,
};

/// External field info - for cross-module compilation
pub const ExternalFieldInfo = struct {
    name: []const u8,
    offset: i32,
    size: i32,
    type_name: []const u8, // "int", "float", "bool", "ptr", or struct name
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
    /// If true, ptr is a slot containing a pointer that must be loaded
    uses_slot: bool,

    fn init(ptr: ir.Value, ty: ValueType, is_mutable: bool, uses_slot: bool) VarInfo {
        return .{
            .ptr = ptr,
            .ty = ty,
            .used = false,
            .is_mutable = is_mutable,
            .state = .owned,
            .moved_to = null,
            .moved_line = 0,
            .uses_slot = uses_slot,
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

/// Helper for managing intrinsic loop blocks (cond/body/end pattern).
/// Ensures correct block ordering and branch emission to avoid infinite loops.
///
/// Usage:
/// 1. Create LoopBlocks
/// 2. Emit condition code (cond block is current)
/// 3. Call emitCondBranch with condition value
/// 4. Emit body code (body block is now current)
/// 5. Emit branch back to cond block
/// 6. Call finish() to restore end block
const LoopBlocks = struct {
    cond_block_idx: u32,
    body_block_idx: u32,
    end_block_idx: u32,
    end_block: ir.BasicBlock,
    body_block: ir.BasicBlock,

    /// Create loop blocks and emit entry branch to cond block.
    /// After this, cond block is the current block.
    fn create(self_ir: *AstToIr, cond_name: []const u8, body_name: []const u8, end_name: []const u8) !LoopBlocks {
        // Create cond block
        const cond_block_idx: u32 = @intCast(self_ir.func().blocks.items.len);
        _ = try self_ir.func().addBlock(cond_name);

        // Emit unconditional branch from previous block to cond block
        try self_ir.func().blocks.items[cond_block_idx - 1].instructions.append(self_ir.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = cond_block_idx }, .none },
            .result = undefined,
        });

        // Create body and end blocks
        const body_block_idx: u32 = @intCast(self_ir.func().blocks.items.len);
        _ = try self_ir.func().addBlock(body_name);
        const end_block_idx: u32 = @intCast(self_ir.func().blocks.items.len);
        _ = try self_ir.func().addBlock(end_name);

        // Pop end and body blocks (cond block becomes current)
        const end_block = self_ir.func().blocks.pop().?;
        const body_block = self_ir.func().blocks.pop().?;

        return .{
            .cond_block_idx = cond_block_idx,
            .body_block_idx = body_block_idx,
            .end_block_idx = end_block_idx,
            .end_block = end_block,
            .body_block = body_block,
        };
    }

    /// Emit conditional branch in cond block, then restore body block as current.
    fn emitCondBranch(self: *LoopBlocks, self_ir: *AstToIr, cond_value: ir.Value) !void {
        try self_ir.func().blocks.items[self.cond_block_idx].instructions.append(self_ir.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = cond_value }, .{ .block_ref = self.body_block_idx } },
            .result = self.end_block_idx,
        });

        // Restore body block as current
        try self_ir.func().blocks.append(self_ir.allocator, self.body_block);
    }

    /// Restore end block after body code is complete.
    fn finish(self: *LoopBlocks, self_ir: *AstToIr) !void {
        try self_ir.func().blocks.append(self_ir.allocator, self.end_block);
    }
};

const ConvertError = error{
    OutOfMemory,
    UndefinedVariable,
    FloatModNotSupported,
    WrongArgumentCount,
    UnknownType,
    UnknownField,
    UnknownFunction,
    UseAfterMove,
    ImmutableAssign,
    ImmutableMove,
    SemanticError,
    NotABuiltin,
    TypeMismatch,
    ZeroSizeAllocation,
    UnusedVariable,
};

// ============================================================================
// AST to IR Converter
// ============================================================================

/// Interface info - stores method signatures for conformance checking
const InterfaceInfo = struct {
    name: []const u8,
    methods: []const ast.InterfaceMethod,
};

pub const AstToIr = struct {
    allocator: std.mem.Allocator,
    module: ir.Module,
    current_func: ?*ir.Function,
    var_map: std.StringHashMapUnmanaged(VarInfo),
    type_map: std.StringHashMapUnmanaged(TypeInfo),
    func_map: std.StringHashMapUnmanaged(FuncInfo),
    interface_map: std.StringHashMapUnmanaged(InterfaceInfo),
    type_decl_map: std.StringHashMapUnmanaged(ast.TypeDecl), // Type declarations for conformance lookup
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
    // Current function name (for mutation analysis lookup)
    current_func_name: ?[]const u8 = null,
    // Method context: current type name when converting methods
    current_type_name: ?[]const u8 = null,
    // Self pointer value when inside a method
    self_ptr: ?ir.Value = null,
    // Generic type parameters (e.g., "Element" -> "int" for Array of int)
    generic_params: std.StringHashMapUnmanaged([]const u8) = .{},
    // Flag for allowing stdlib-only builtins (set when converting monomorphized stdlib methods)
    in_stdlib_method: bool = false,

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
            .interface_map = .{},
            .type_decl_map = .{},
            .current_decl_is_mutable = false,
            .mutation_analyzer = mutation_analyzer,
            .current_line = 1,
            .sret_ptr = null,
            .sret_size = 0,
            .source_file = null,
            .last_error = null,
            .loop_end_block = null,
            .loop_cond_block = null,
            .current_func_name = null,
            .current_type_name = null,
            .self_ptr = null,
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

    fn reportErrorWithDetails(self: *AstToIr, code: err.ErrorCode, details: []const u8) void {
        // Format: "base message: 'details'"
        const base_msg = code.message();
        const formatted = std.fmt.allocPrint(self.allocator, "{s}: '{s}'", .{ base_msg, details }) catch {
            self.reportError(code);
            return;
        };
        self.last_error = .{
            .code = code,
            .message = formatted,
            .location = .{
                .file = self.source_file,
                .line = @intCast(self.current_line),
                .column = 1,
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

        // Interface map doesn't own its data (references AST nodes)
        self.interface_map.deinit(self.allocator);

        // Type decl map doesn't own its data (references AST nodes)
        self.type_decl_map.deinit(self.allocator);

        // Generic params map doesn't own its data (references AST strings)
        self.generic_params.deinit(self.allocator);
    }

    // ------------------------------------------------------------------------
    // Main Entry Point
    // ------------------------------------------------------------------------

    pub fn convert(self: *AstToIr, program: ast.Program) !ir.Module {
        // Register primitive types
        try self.type_map.put(self.allocator, "int", .{ .primitive = .i64 });
        try self.type_map.put(self.allocator, "float", .{ .primitive = .f64 });
        try self.type_map.put(self.allocator, "bool", .{ .primitive = .i64 }); // bool is represented as i64

        // Register __ManagedArray compiler-internal type (24 bytes: ptr + len + capacity)
        // This is a parameterized type - element type is tracked separately in ArrayInfo
        const managed_array_fields = try self.allocator.alloc(FieldInfo, 3);
        managed_array_fields[0] = .{ .name = "_buffer", .offset = 0, .size = 8, .value_type = .{ .primitive = .ptr } };
        managed_array_fields[1] = .{ .name = "_len", .offset = 8, .size = 8, .value_type = .{ .primitive = .i64 } };
        managed_array_fields[2] = .{ .name = "_capacity", .offset = 16, .size = 8, .value_type = .{ .primitive = .i64 } };
        try self.type_map.put(self.allocator, "__ManagedArray", .{
            .struct_type = .{ .name = "__ManagedArray", .fields = managed_array_fields, .size = 24 },
        });

        // Register interfaces first (needed for conformance checking)
        for (program.interfaces) |iface| try self.registerInterface(iface);

        // Register declarations
        for (program.types) |type_decl| try self.registerType(type_decl);
        for (program.enums) |enum_decl| try self.registerEnum(enum_decl);
        for (program.functions) |fn_decl| try self.registerFunction(fn_decl);

        // Register methods from types
        for (program.types) |type_decl| {
            // Set up generic params for this type
            for (type_decl.generic_params) |param| {
                try self.generic_params.put(self.allocator, param, "int");
            }
            for (type_decl.methods) |method| {
                try self.registerMethod(type_decl.name, method);
            }
            // Clean up generic params
            for (type_decl.generic_params) |param| {
                _ = self.generic_params.remove(param);
            }
        }

        // Check interface conformance for all types
        for (program.types) |type_decl| {
            try self.checkInterfaceConformance(type_decl);
        }

        // Convert functions
        for (program.functions) |fn_decl| try self.convertFunction(fn_decl);

        // Convert methods from types
        for (program.types) |type_decl| {
            // Set up generic params for this type
            for (type_decl.generic_params) |param| {
                try self.generic_params.put(self.allocator, param, "int");
            }
            for (type_decl.methods) |method| {
                try self.convertMethod(type_decl.name, method);
            }
            // Clean up generic params
            for (type_decl.generic_params) |param| {
                _ = self.generic_params.remove(param);
            }
        }

        // Transfer ownership of module
        const module = self.module;
        self.module = ir.Module.init(self.allocator);
        return module;
    }

    // ------------------------------------------------------------------------
    // Type Lookup Helpers
    // ------------------------------------------------------------------------

    fn lookupIrType(self: *AstToIr, name: []const u8) !ir.Type {
        const type_info = self.type_map.get(name) orelse {
            std.debug.print("[AST->IR] lookupIrType: unknown type '{s}'\n", .{name});
            return error.UnknownType;
        };
        return type_info.irType();
    }

    fn lookupStructInfo(self: *AstToIr, type_name: []const u8) !StructTypeInfo {
        const type_info = self.type_map.get(type_name) orelse {
            std.debug.print("[AST->IR] lookupStructInfo: unknown type '{s}'\n", .{type_name});
            return error.UnknownType;
        };
        return switch (type_info) {
            .struct_type => |s| s,
            .primitive, .enum_type => {
                std.debug.print("[AST->IR] lookupStructInfo: '{s}' is not a struct\n", .{type_name});
                return error.UnknownType;
            },
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
        // Store the type declaration for conformance checking
        try self.type_decl_map.put(self.allocator, type_decl.name, type_decl);

        // Set up generic type parameters (e.g., "Element" for Array uses Element)
        // For now, map all generic params to i64 as a default
        for (type_decl.generic_params) |param| {
            try self.generic_params.put(self.allocator, param, "int");
        }
        defer {
            // Clean up generic params after type registration
            for (type_decl.generic_params) |param| {
                _ = self.generic_params.remove(param);
            }
        }

        // Check if this type was already registered externally - we'll need to free old fields after registering new ones
        const old_fields: ?[]const FieldInfo = if (self.type_map.get(type_decl.name)) |existing|
            switch (existing) {
                .struct_type => |s| s.fields,
                else => null,
            }
        else
            null;

        var fields = try self.allocator.alloc(FieldInfo, type_decl.fields.len);
        var offset: i32 = 0;

        for (type_decl.fields, 0..) |field, i| {
            const value_type: ValueType = switch (field.type_expr) {
                .simple, .generic => blk: {
                    const base_name = typeExprBaseTypeName(field.type_expr).?;
                    const resolved = self.resolveTypeName(base_name);
                    const field_type_info = self.type_map.get(resolved) orelse {
                        std.debug.print("[AST->IR] registerType field '{s}': unknown type '{s}'\n", .{ field.name, resolved });
                        return error.UnknownType;
                    };
                    break :blk switch (field_type_info) {
                        .struct_type => .{ .struct_type = resolved },
                        .primitive => |p| .{ .primitive = p },
                        .enum_type => .{ .enum_type = resolved },
                    };
                },
                .array => |arr| .{ .array_type = .{
                    .element_type = try self.lookupIrType(arr.element_type),
                    .size = if (arr.size) |s| @intCast(s) else null,
                    .storage = .stack,
                } },
                .optional => |wrapped| blk: {
                    const wrapped_value_type = try self.typeExprToValueType(wrapped.*);
                    break :blk .{ .optional_type = .{
                        .wrapped = wrapped_value_type.toPrimitiveType(),
                    } };
                },
            };
            const field_size: i32 = switch (value_type) {
                .struct_type => |name| blk: {
                    const info = self.type_map.get(name) orelse {
                        std.debug.print("[AST->IR] registerType field size: unknown struct type '{s}'\n", .{name});
                        return error.UnknownType;
                    };
                    break :blk switch (info) {
                        .struct_type => |s| s.size,
                        else => 8,
                    };
                },
                .primitive, .enum_type, .array_type => 8, // arrays stored as pointers
                .optional_type => 8, // optionals stored as pointers to 16-byte structures
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

        // Now that the new entry is in the map, free the old fields if this was a re-registration
        if (old_fields) |of| {
            self.allocator.free(of);
        }

        debug.astToIr("Registered type '{s}' with size {d}", .{ type_decl.name, offset });
    }

    /// Register an external type (from stdlib or other modules)
    fn registerExternalType(self: *AstToIr, ext_type: ExternalTypeInfo) !void {
        // Skip if already registered (avoid duplicate allocations)
        if (self.type_map.contains(ext_type.name)) return;

        var fields = try self.allocator.alloc(FieldInfo, ext_type.fields.len);

        for (ext_type.fields, 0..) |field, i| {
            const value_type: ValueType = blk: {
                // Map type name to ValueType
                if (std.mem.eql(u8, field.type_name, "int")) {
                    break :blk .{ .primitive = .i64 };
                } else if (std.mem.eql(u8, field.type_name, "float")) {
                    break :blk .{ .primitive = .f64 };
                } else if (std.mem.eql(u8, field.type_name, "bool")) {
                    break :blk .{ .primitive = .i64 };
                } else if (std.mem.eql(u8, field.type_name, "ptr")) {
                    break :blk .{ .primitive = .ptr };
                } else if (std.mem.eql(u8, field.type_name, "__ManagedArray")) {
                    // Managed arrays are structs, but we treat them specially
                    break :blk .{ .struct_type = field.type_name };
                } else {
                    // Assume it's a struct type
                    break :blk .{ .struct_type = field.type_name };
                }
            };

            fields[i] = .{
                .name = field.name,
                .offset = field.offset,
                .size = field.size,
                .value_type = value_type,
            };
        }

        try self.type_map.put(self.allocator, ext_type.name, .{
            .struct_type = .{ .name = ext_type.name, .fields = fields, .size = ext_type.size },
        });

        // Store type declaration for generic types (needed for monomorphization)
        if (ext_type.type_decl) |type_decl| {
            try self.type_decl_map.put(self.allocator, ext_type.name, type_decl.*);
        }

        debug.astToIr("Registered external type '{s}' with size {d}", .{ ext_type.name, ext_type.size });
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

    fn registerInterface(self: *AstToIr, iface: ast.InterfaceDecl) !void {
        try self.interface_map.put(self.allocator, iface.name, .{
            .name = iface.name,
            .methods = iface.methods,
        });
        debug.astToIr("Registered interface '{s}' with {d} methods", .{ iface.name, iface.methods.len });
    }

    fn checkInterfaceConformance(self: *AstToIr, type_decl: ast.TypeDecl) !void {
        for (type_decl.conformances) |conformance| {
            const iface = self.interface_map.get(conformance.interface_name) orelse {
                // Unknown interface - skip conformance check
                // This allows stdlib types to declare conformance to interfaces
                // that may not be loaded yet (e.g., Collection, InitableFromArrayLiteral)
                debug.astToIr("Skipping unknown interface '{s}'", .{conformance.interface_name});
                continue;
            };

            // Check that all required interface methods are implemented
            for (iface.methods) |iface_method| {
                // Skip methods with default implementations (they're optional to override)
                if (iface_method.has_default_impl) continue;

                // Look for matching method in type
                var found = false;
                for (type_decl.methods) |type_method| {
                    if (std.mem.eql(u8, type_method.name, iface_method.name)) {
                        found = true;
                        break;
                    }
                }

                if (!found) {
                    // Missing required method - report detailed error
                    const msg = std.fmt.allocPrint(self.allocator, "type '{s}' does not implement interface '{s}': missing method '{s}'", .{
                        type_decl.name,
                        conformance.interface_name,
                        iface_method.name,
                    }) catch {
                        self.reportError(.E015);
                        return error.SemanticError;
                    };
                    self.last_error = .{
                        .code = .E015,
                        .message = msg,
                        .location = .{
                            .file = self.source_file,
                            .line = 1, // Type declaration line not tracked
                            .column = 1,
                        },
                    };
                    return error.SemanticError;
                }
            }
        }
    }

    /// Check if a type conforms to a specific interface by name
    fn typeConformsTo(self: *AstToIr, type_name: []const u8, interface_name: []const u8) bool {
        const type_decl = self.type_decl_map.get(type_name) orelse return false;
        for (type_decl.conformances) |conformance| {
            if (std.mem.eql(u8, conformance.interface_name, interface_name)) {
                return true;
            }
        }
        return false;
    }

    fn registerFunction(self: *AstToIr, decl: ast.FunctionDecl) !void {
        var ret_type_name: ?[]const u8 = null;
        var ret_value_type: ?ValueType = null;
        const ret_type: ir.Type = if (decl.return_type) |rt| blk: {
            switch (rt) {
                .simple, .generic => {
                    const base_name = typeExprBaseTypeName(rt).?;
                    const type_info = self.type_map.get(base_name) orelse {
                        std.debug.print("[AST->IR] registerFunction '{s}': unknown return type '{s}'\n", .{ decl.name, base_name });
                        return error.UnknownType;
                    };
                    if (type_info.isStruct()) ret_type_name = base_name;
                    break :blk type_info.irType();
                },
                .array => |arr| {
                    // Array return types are returned as pointers
                    const elem_type = try self.lookupIrType(arr.element_type);
                    ret_value_type = .{
                        .array_type = .{
                            .element_type = elem_type,
                            .size = if (arr.size) |s| @intCast(s) else null,
                            .storage = .heap, // Returned arrays are heap-allocated
                        },
                    };
                    break :blk .ptr;
                },
                .optional => |wrapped| {
                    // Optional return types are returned as pointers
                    const wrapped_value_type = try self.typeExprToValueType(wrapped.*);
                    ret_value_type = .{
                        .optional_type = .{
                            .wrapped = wrapped_value_type.toPrimitiveType(),
                        },
                    };
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

    /// Extract base type name from simple or generic type expressions
    fn typeExprBaseTypeName(type_expr: ast.TypeExpr) ?[]const u8 {
        return switch (type_expr) {
            .simple => |name| name,
            .generic => |gen| gen.base_type,
            .array, .optional => null,
        };
    }

    fn typeExprToValueType(self: *AstToIr, type_expr: ast.TypeExpr) !ValueType {
        switch (type_expr) {
            .simple, .generic => {
                const base_name = typeExprBaseTypeName(type_expr).?;
                const resolved = self.resolveTypeName(base_name);
                const type_info = self.type_map.get(resolved) orelse {
                    std.debug.print("[AST->IR] typeExprToValueType: unknown type '{s}' (resolved from '{s}')\n", .{ resolved, base_name });
                    return error.UnknownType;
                };
                return if (type_info.isStruct())
                    .{ .struct_type = resolved }
                else
                    .{ .primitive = type_info.irType() };
            },
            .array => |arr| {
                const resolved_elem = self.resolveTypeName(arr.element_type);
                const elem_type = try self.lookupIrType(resolved_elem);
                return .{ .array_type = .{
                    .element_type = elem_type,
                    .size = if (arr.size) |s| @intCast(s) else null,
                    .storage = .stack,
                } };
            },
            .optional => |wrapped| {
                const wrapped_value_type = try self.typeExprToValueType(wrapped.*);
                return .{ .optional_type = .{
                    .wrapped = wrapped_value_type.toPrimitiveType(),
                } };
            },
        }
    }

    /// Resolve type name, handling 'Self' substitution and generic type parameters
    fn resolveTypeName(self: *AstToIr, type_name: []const u8) []const u8 {
        if (std.mem.eql(u8, type_name, "Self")) {
            return self.current_type_name orelse type_name;
        }
        // Check if this is a generic type parameter (e.g., "Element")
        if (self.generic_params.get(type_name)) |resolved| {
            return resolved;
        }
        return type_name;
    }

    /// Get or create a monomorphized version of a generic type
    /// e.g., "Container" + ["int"] -> "Container$int"
    fn getOrCreateMonomorphizedType(self: *AstToIr, base_type: []const u8, type_args: []const []const u8) ConvertError![]const u8 {
        // Build the monomorphized name: TypeName$Arg1$Arg2
        var name_parts: std.ArrayListUnmanaged(u8) = .empty;
        defer name_parts.deinit(self.allocator);

        name_parts.appendSlice(self.allocator, base_type) catch return error.OutOfMemory;
        for (type_args) |arg| {
            name_parts.append(self.allocator, '$') catch return error.OutOfMemory;
            name_parts.appendSlice(self.allocator, arg) catch return error.OutOfMemory;
        }

        const mono_name = self.allocator.dupe(u8, name_parts.items) catch return error.OutOfMemory;
        try self.module.trackString(mono_name);

        // Check if already registered
        if (self.type_map.contains(mono_name)) {
            return mono_name;
        }

        // Get the original generic type declaration
        const type_decl = self.type_decl_map.get(base_type) orelse {
            debug.astToIr("Unknown generic type: {s}\n", .{base_type});
            return error.UnknownType;
        };

        // Verify we have the right number of type arguments
        if (type_decl.generic_params.len != type_args.len) {
            debug.astToIr("Wrong number of type args for {s}: expected {d}, got {d}\n", .{ base_type, type_decl.generic_params.len, type_args.len });
            return error.TypeMismatch;
        }

        // Set up generic parameter mappings (needed for field type resolution AND method registration)
        for (type_decl.generic_params, type_args) |param, arg| {
            try self.generic_params.put(self.allocator, param, arg);
        }
        // Note: We clean up generic_params at the very end, after method registration

        // Register the monomorphized type
        var fields = try self.allocator.alloc(FieldInfo, type_decl.fields.len);
        var offset: i32 = 0;

        for (type_decl.fields, 0..) |field, i| {
            const value_type: ValueType = try self.typeExprToValueType(field.type_expr);
            const field_size: i32 = switch (value_type) {
                .struct_type => |name| blk: {
                    const info = self.type_map.get(name) orelse {
                        debug.astToIr("Monomorphize: unknown struct type '{s}'\n", .{name});
                        // Clean up on error
                        for (type_decl.generic_params) |param| {
                            _ = self.generic_params.remove(param);
                        }
                        return error.UnknownType;
                    };
                    break :blk switch (info) {
                        .struct_type => |s| s.size,
                        else => 8,
                    };
                },
                .primitive, .enum_type, .array_type => 8,
                .optional_type => 8,
            };

            fields[i] = .{
                .name = field.name,
                .offset = offset,
                .value_type = value_type,
                .size = field_size,
            };
            offset += field_size;
        }

        const struct_info = StructTypeInfo{
            .name = mono_name,
            .fields = fields,
            .size = offset,
        };

        try self.type_map.put(self.allocator, mono_name, .{ .struct_type = struct_info });
        try self.type_decl_map.put(self.allocator, mono_name, type_decl);

        // Register methods for monomorphized type (generic_params still active here)
        for (type_decl.methods) |method| {
            try self.registerMethod(mono_name, method);
        }

        // Convert method bodies for monomorphized type (generic_params still active)
        // Must save/restore function context since we're called during expression conversion
        // IMPORTANT: Save the function INDEX, not the pointer! Adding new functions may cause
        // the module's functions ArrayList to reallocate, invalidating existing pointers.
        const saved_func_idx: ?usize = if (self.current_func) |f| blk: {
            for (self.module.functions.items, 0..) |*fn_ptr, i| {
                if (fn_ptr == f) break :blk i;
            }
            break :blk null;
        } else null;
        const saved_func_name = self.current_func_name;
        const saved_sret_ptr = self.sret_ptr;
        const saved_sret_size = self.sret_size;
        const saved_self_ptr = self.self_ptr;
        const saved_in_stdlib = self.in_stdlib_method;

        // Allow stdlib builtins when converting methods from external types (stdlib)
        self.in_stdlib_method = true;

        for (type_decl.methods) |method| {
            try self.convertMethod(mono_name, method);
        }

        // Restore function context - recompute pointer from saved index
        self.in_stdlib_method = saved_in_stdlib;
        self.current_func = if (saved_func_idx) |idx| &self.module.functions.items[idx] else null;
        self.current_func_name = saved_func_name;
        self.sret_ptr = saved_sret_ptr;
        self.sret_size = saved_sret_size;
        self.self_ptr = saved_self_ptr;

        // Clean up generic params after everything is done
        for (type_decl.generic_params) |param| {
            _ = self.generic_params.remove(param);
        }

        return mono_name;
    }

    /// Register a method as a function with mangled name: TypeName$methodName
    fn registerMethod(self: *AstToIr, type_name: []const u8, method: ast.MethodDecl) !void {
        // Save current type context for Self resolution
        const saved_type = self.current_type_name;
        self.current_type_name = type_name;
        defer self.current_type_name = saved_type;

        // Generate mangled name and track in module for cleanup
        const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, method.name });
        try self.module.trackString(mangled_name);

        // Determine return type
        var ret_type_name: ?[]const u8 = null;
        var ret_value_type: ?ValueType = null;
        const ret_type: ir.Type = if (method.return_type) |rt| blk: {
            switch (rt) {
                .simple, .generic => {
                    const base_name = typeExprBaseTypeName(rt).?;
                    const resolved = self.resolveTypeName(base_name);
                    const type_info = self.type_map.get(resolved) orelse {
                        std.debug.print("[AST->IR] Unknown return type: {s} (resolved from {s})\n", .{ resolved, base_name });
                        return error.UnknownType;
                    };
                    if (type_info.isStruct()) ret_type_name = resolved;
                    break :blk type_info.irType();
                },
                .array => |arr| {
                    const elem_type = try self.lookupIrType(arr.element_type);
                    ret_value_type = .{
                        .array_type = .{
                            .element_type = elem_type,
                            .size = if (arr.size) |s| @intCast(s) else null,
                            .storage = .heap,
                        },
                    };
                    break :blk .ptr;
                },
                .optional => |wrapped| {
                    const wrapped_value_type = try self.typeExprToValueType(wrapped.*);
                    ret_value_type = .{
                        .optional_type = .{
                            .wrapped = wrapped_value_type.toPrimitiveType(),
                        },
                    };
                    break :blk .ptr;
                },
            }
        } else .void;

        // Count params: +1 for implicit self if not static
        const extra_params: usize = if (method.is_static) 0 else 1;
        var param_types = try self.allocator.alloc(ParamType, method.params.len + extra_params);

        // First param is self (pointer to type instance) for instance methods
        if (!method.is_static) {
            param_types[0] = .{ .ty = .{ .struct_type = type_name } };
        }

        // Register explicit params
        for (method.params, 0..) |param, i| {
            param_types[i + extra_params] = .{
                .ty = try self.typeExprToValueType(param.type_expr),
            };
        }

        try self.func_map.put(self.allocator, mangled_name, .{
            .return_type = ret_type,
            .return_type_name = ret_type_name,
            .return_value_type = ret_value_type,
            .param_types = param_types,
        });

        debug.astToIr("Registered method '{s}' as '{s}' returning {s}", .{ method.name, mangled_name, ret_type.format() });
    }

    /// Convert a method to IR
    fn convertMethod(self: *AstToIr, type_name: []const u8, method: ast.MethodDecl) !void {
        // Save current type context
        const saved_type = self.current_type_name;
        self.current_type_name = type_name;
        defer self.current_type_name = saved_type;

        // Generate mangled name and track in module for cleanup
        const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, method.name });
        try self.module.trackString(mangled_name);

        // Determine return type (same as registerMethod)
        var uses_sret = false;
        var sret_struct_size: i32 = 0;
        if (method.return_type) |rt| {
            switch (rt) {
                .simple, .generic => {
                    const base_name = typeExprBaseTypeName(rt).?;
                    const resolved = self.resolveTypeName(base_name);
                    if (self.type_map.get(resolved)) |type_info| {
                        if (type_info == .struct_type) {
                            uses_sret = true;
                            sret_struct_size = type_info.struct_type.size;
                        }
                    }
                },
                .array => {},
                .optional => {
                    // Optionals are 16 bytes (tag + value), use sret
                    uses_sret = true;
                    sret_struct_size = 16;
                },
            }
        }

        const ret_type: ir.Type = if (method.return_type) |rt| blk: {
            switch (rt) {
                .simple, .generic => {
                    const base_name = typeExprBaseTypeName(rt).?;
                    const resolved = self.resolveTypeName(base_name);
                    break :blk try self.lookupIrType(resolved);
                },
                .array, .optional => break :blk .ptr,
            }
        } else .void;

        const ir_func = try self.module.addFunctionWithExport(mangled_name, ret_type, method.is_export);
        self.current_func = ir_func;
        self.current_func_name = mangled_name;
        self.var_map.clearRetainingCapacity();
        _ = try ir_func.addBlock("entry");

        // Reset sret state
        self.sret_ptr = null;
        self.sret_size = 0;
        self.self_ptr = null;

        // Parameter offset for sret
        var param_offset: i32 = 0;
        if (uses_sret) {
            self.sret_ptr = try ir_func.emitParam(0, .ptr);
            self.sret_size = sret_struct_size;
            param_offset = 1;
        }

        // Register implicit self parameter for instance methods
        if (!method.is_static) {
            const self_val = try ir_func.emitParam(param_offset, .ptr);
            try ir_func.setValueName(self_val, "self");
            self.self_ptr = self_val;

            // Register self as a variable for explicit self.field access
            try self.var_map.put(self.allocator, "self", VarInfo.init(
                self_val,
                .{ .struct_type = type_name },
                false, // self is immutable
                false,
            ));

            param_offset += 1;
        }

        // Register explicit parameters
        for (method.params, 0..) |param, i| {
            try self.registerParameter(param, @as(i32, @intCast(i)) + param_offset);
        }

        // Convert body
        for (method.body) |stmt| {
            try self.convertStatement(stmt);
        }

        // For void methods, add implicit return
        if (ret_type == .void) {
            const block = self.func().currentBlock() orelse return;
            const needs_implicit_ret = block.instructions.items.len == 0 or
                block.instructions.items[block.instructions.items.len - 1].op != .ret;
            if (needs_implicit_ret) {
                try self.func().emitRet(null);
            }
        }

        // Skip unused variable check for 'self' - it's always implicitly used
        if (self.var_map.getPtr("self")) |self_info| {
            self_info.used = true;
        }

        // Check for unused variables
        var iter = self.var_map.iterator();
        while (iter.next()) |entry| {
            if (!entry.value_ptr.used) {
                debug.astToIr("error: unused variable '{s}'\n", .{entry.key_ptr.*});
                self.reportErrorWithDetails(.E014, entry.key_ptr.*);
                return error.UnusedVariable;
            }
        }

        // Clear method context
        self.self_ptr = null;
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
                .simple, .generic => {
                    const base_name = typeExprBaseTypeName(rt).?;
                    if (self.type_map.get(base_name)) |type_info| {
                        if (type_info == .struct_type) {
                            uses_sret = true;
                            sret_struct_size = type_info.struct_type.size;
                        }
                    }
                },
                .array => {}, // Arrays returned as pointers, no sret needed
                .optional => {
                    // Optionals are 16 bytes (tag + value), use sret
                    uses_sret = true;
                    sret_struct_size = 16;
                },
            }
        }

        const ret_type: ir.Type = if (decl.return_type) |rt| blk: {
            switch (rt) {
                .simple, .generic => {
                    const base_name = typeExprBaseTypeName(rt).?;
                    break :blk try self.lookupIrType(base_name);
                },
                .array, .optional => break :blk .ptr,
            }
        } else .void;

        const ir_func = try self.module.addFunctionWithExport(decl.name, ret_type, decl.is_export);
        self.current_func = ir_func;
        self.current_func_name = decl.name;
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

        // For void functions, add implicit return if the body doesn't end with a return
        if (ret_type == .void) {
            const block = self.func().currentBlock() orelse return;
            const needs_implicit_ret = block.instructions.items.len == 0 or
                block.instructions.items[block.instructions.items.len - 1].op != .ret;
            if (needs_implicit_ret) {
                try self.func().emitRet(null);
            }
        }

        // Check for unused variables
        var iter = self.var_map.iterator();
        while (iter.next()) |entry| {
            if (!entry.value_ptr.used) {
                debug.astToIr("error: unused variable '{s}'\n", .{entry.key_ptr.*});
                self.reportErrorWithDetails(.E014, entry.key_ptr.*);
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
                    false,
                ));
            },
            .array_type, .optional_type => {
                // Reference types are passed as pointers - store in a stack slot
                const param_val = try self.func().emitParam(idx, .ptr);
                const slot_ptr = try self.func().emitAlloca(.ptr);
                try self.func().setValueName(slot_ptr, param.name);
                try self.func().emitStore(slot_ptr, param_val);

                // For dynamic arrays, check if mutation transfers ownership
                var final_type = value_type;
                if (value_type == .array_type) {
                    const arr_info = value_type.array_type;
                    if (arr_info.size == null) {
                        if (self.mutation_analyzer) |analyzer| {
                            if (self.current_func_name) |func_name| {
                                const source_param_idx: usize = if (self.sret_ptr != null)
                                    @intCast(idx - 1)
                                else
                                    @intCast(idx);
                                if (analyzer.doesMutateParam(func_name, source_param_idx)) {
                                    var modified = arr_info;
                                    modified.storage = .heap;
                                    final_type = .{ .array_type = modified };
                                }
                            }
                        }
                    }
                }

                try self.var_map.put(self.allocator, param.name, VarInfo.init(
                    slot_ptr,
                    final_type,
                    true,
                    true,
                ));
            },
            .primitive, .enum_type => {
                const ir_type = value_type.toPrimitiveType();
                const param_val = try self.func().emitParam(idx, ir_type);
                try self.func().setValueName(param_val, param.name);
                const ptr = try self.func().emitAlloca(ir_type);
                try self.func().setValueName(ptr, param.name);
                try self.func().emitStore(ptr, param_val);
                try self.var_map.put(self.allocator, param.name, VarInfo.init(
                    ptr,
                    value_type,
                    true,
                    false,
                ));
            },
        }
    }

    // ------------------------------------------------------------------------
    // Statement Conversion
    // ------------------------------------------------------------------------

    fn convertStatement(self: *AstToIr, stmt: ast.Statement) !void {
        // Track current line from AST for error reporting
        self.current_line = stmt.line;

        switch (stmt.kind) {
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
            .index_assign => |assign| {
                debug.astToIr("Converting index_assign", .{});
                try self.convertIndexAssign(assign);
            },
            .call => |call| _ = try self.convertCall(call),
            .method_call => |mcall| try self.convertMethodCall(mcall),
            .if_stmt => |if_s| try self.convertIfStmt(if_s),
            .while_stmt => |while_s| try self.convertWhileStmt(while_s),
            .for_stmt => |for_s| try self.convertForStmt(for_s),
            .break_stmt => try self.convertBreakStmt(),
            .continue_stmt => try self.convertContinueStmt(),
            .else_unwrap_decl => |unwrap| try self.convertElseUnwrapDecl(unwrap),
        }
    }

    fn convertVarDecl(self: *AstToIr, decl: ast.VarDecl) !void {
        debug.astToIr("Converting var decl: {s}", .{decl.name});

        // Sized arrays cannot be immutable (they have no initial contents)
        if (!self.current_decl_is_mutable and decl.value == .sized_array) {
            self.reportError(.E013);
            return error.SemanticError;
        }

        // Check for InitableFromArrayLiteral transformation
        // Syntax: var arr Array of int = [1, 2, 3] (generic) or var arr IntArray = [...] (simple)
        if (decl.type_annotation) |type_ann| {
            const type_name: ?[]const u8 = switch (type_ann) {
                .generic => |gen| gen.base_type,
                .simple => |name| name,
                .array, .optional => null,
            };
            if (type_name) |t_name| {
                if (self.typeConformsTo(t_name, "InitableFromArrayLiteral")) {
                    if (decl.value == .array_literal) {
                        try self.convertInitableFromArrayLiteralSimple(decl, t_name);
                        return;
                    }
                }
            }
        }

        const init_typed = try self.convertExpression(decl.value);

        // Heap arrays and optionals use a slot to hold the pointer
        const uses_slot = (init_typed.ty == .array_type and init_typed.ty.array_type.storage == .heap) or
            init_typed.ty == .optional_type;

        // Primitives/enums need alloca for the value; slot types need alloca for the pointer
        const needs_alloca = init_typed.ty == .primitive or init_typed.ty == .enum_type or uses_slot;

        const ptr = if (needs_alloca) blk: {
            const alloca_type = if (uses_slot) .ptr else init_typed.ty.toPrimitiveType();
            const p = try self.func().emitAlloca(alloca_type);
            try self.func().setValueName(p, decl.name);
            try self.func().emitStore(p, init_typed.value);
            break :blk p;
        } else blk: {
            try self.func().setValueName(init_typed.value, decl.name);
            break :blk init_typed.value;
        };

        try self.var_map.put(self.allocator, decl.name, VarInfo.init(
            ptr,
            init_typed.ty,
            self.current_decl_is_mutable,
            uses_slot,
        ));
    }

    fn convertReturn(self: *AstToIr, ret: ast.ReturnStmt) !void {
        // Evaluate return expression first (before cleanup)
        var ret_value: ?ir.Value = null;

        if (ret.value) |expr| {
            // If using sret and returning a struct literal, write directly to sret buffer
            if (self.sret_ptr) |sret| {
                if (expr == .struct_init) {
                    // Initialize struct directly into sret buffer (no intermediate copy)
                    try self.initStructInto(expr.struct_init, sret);
                    try self.freeHeapAllocations();
                    try self.func().emitRet(sret);
                    return;
                }
                // Returning an existing struct variable or optional - copy to sret buffer
                const typed_val = try self.convertExpression(expr);

                // If returning a non-optional value from an optional-returning function (sret_size == 16),
                // wrap the value in a Some optional
                if (self.sret_size == 16 and typed_val.ty != .optional_type) {
                    // Write tag = 1 (has value) to sret
                    const one = try self.func().emitConstI64(1);
                    try self.func().emitStore(sret, one);
                    // Write value at offset 8
                    const value_ptr = try self.func().emitGetFieldPtr(sret, 8);
                    try self.func().emitStore(value_ptr, typed_val.value);
                } else {
                    try self.func().emitMemcpy(sret, typed_val.value, self.sret_size);
                }
                try self.freeHeapAllocations();
                try self.func().emitRet(sret);
                return;
            }

            const typed_val = try self.convertExpression(expr);

            // Convert float to int if needed
            if (self.func().return_type == .i64 and typed_val.ty.toPrimitiveType() == .f64) {
                ret_value = try self.func().emitUnaryOp(.fptosi, typed_val.value, .i64);
            } else {
                ret_value = typed_val.value;
            }
        }

        // Free heap allocations before return
        try self.freeHeapAllocations();

        try self.func().emitRet(ret_value);
    }

    /// Free heap-allocated variables, optionally filtering to only loop-scoped vars
    fn freeHeapVars(self: *AstToIr, exclude_vars: ?*std.StringHashMapUnmanaged(void)) !void {
        var iter = self.var_map.iterator();
        while (iter.next()) |entry| {
            if (exclude_vars) |excluded| {
                if (excluded.contains(entry.key_ptr.*)) continue;
            }
            const var_info = entry.value_ptr.*;
            if (var_info.ty == .array_type) {
                const arr_info = var_info.ty.array_type;
                if (arr_info.storage == .heap and var_info.state != .moved) {
                    const heap_ptr = try self.func().emitLoad(var_info.ptr, .ptr);
                    try self.func().emitHeapFree(heap_ptr);
                }
            }
        }
    }

    fn freeHeapAllocations(self: *AstToIr) !void {
        try self.freeHeapVars(null);
    }

    fn freeLoopScopedHeapVars(self: *AstToIr, pre_loop_vars: *std.StringHashMapUnmanaged(void)) !void {
        try self.freeHeapVars(pre_loop_vars);
    }

    fn convertAssignment(self: *AstToIr, assign: ast.AssignStmt) ConvertError!void {
        // First try as a regular variable
        if (self.var_map.getPtr(assign.target)) |var_info| {
            if (!var_info.is_mutable) {
                debug.astToIr("cannot assign to immutable variable '{s}'\n", .{assign.target});
                self.reportError(.E009);
                return error.ImmutableAssign;
            }

            var_info.used = true;

            // For heap arrays, free old memory before evaluating RHS (to get correct alloc order)
            if (var_info.ty == .array_type) {
                const arr_info = var_info.ty.array_type;
                if (arr_info.storage == .heap and var_info.state != .moved) {
                    const old_ptr = try self.func().emitLoad(var_info.ptr, .ptr);
                    try self.func().emitHeapFree(old_ptr);
                }
            }

            const value_typed = try self.convertExpression(assign.value);

            const is_reference = var_info.ty == .struct_type or var_info.ty == .array_type or var_info.ty == .optional_type;
            if (is_reference and !var_info.uses_slot) {
                var_info.ptr = value_typed.value;
            } else {
                try self.func().emitStore(var_info.ptr, value_typed.value);
            }
            if (is_reference) {
                var_info.ty = value_typed.ty;
            }

            var_info.resetOwnership();
            return;
        }

        // If inside a method, check if target is a field (implicit self)
        if (self.self_ptr != null and self.current_type_name != null) {
            const type_name = self.current_type_name.?;
            if (self.type_map.get(type_name)) |type_info| {
                if (type_info == .struct_type) {
                    const struct_info = type_info.struct_type;
                    for (struct_info.fields) |field| {
                        if (std.mem.eql(u8, field.name, assign.target)) {
                            // Check if field is mutable
                            if (!self.isFieldMutable(type_name, assign.target)) {
                                debug.astToIr("cannot assign to immutable field '{s}'\n", .{assign.target});
                                self.reportError(.E009);
                                return error.ImmutableAssign;
                            }

                            // Mark self as used
                            if (self.var_map.getPtr("self")) |info| {
                                info.used = true;
                            }

                            const value_typed = try self.convertExpression(assign.value);
                            const self_val = self.self_ptr.?;
                            const field_ptr = try self.func().emitGetFieldPtr(self_val, field.offset);
                            try self.func().emitStore(field_ptr, value_typed.value);
                            return;
                        }
                    }
                }
            }
        }

        // Variable not found
        debug.astToIr("error: undefined variable '{s}'\n", .{assign.target});
        self.reportError(.E005);
        return error.UndefinedVariable;
    }

    /// Check if a field is mutable in a type declaration
    fn isFieldMutable(self: *AstToIr, type_name: []const u8, field_name: []const u8) bool {
        _ = self;
        _ = type_name;
        _ = field_name;
        // For now, assume all fields are mutable (var fields)
        // TODO: Track field mutability in FieldInfo
        return true;
    }

    fn convertFieldAssign(self: *AstToIr, assign: ast.FieldAssign) ConvertError!void {
        const base = try self.convertExpression(assign.base.*);
        const type_name = switch (base.ty) {
            .struct_type => |name| name,
            else => {
                std.debug.print("[AST->IR] convertFieldAssign: expected struct type for field '{s}'\n", .{assign.field_name});
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
        // Convert condition expression
        const cond_typed = try self.convertExpression(if_stmt.condition);

        // For if-let, we need to check the optional's tag
        const is_if_let = if_stmt.binding_name != null;
        const condition_value = if (is_if_let) blk: {
            // Load tag from optional (offset 0) and compare with 1
            const tag = try self.func().emitLoad(cond_typed.value, .i64);
            const one = try self.func().emitConstI64(1);
            break :blk try self.func().emitBinaryOp(.icmp_eq, tag, one, .i64);
        } else cond_typed.value;

        // Determine what blocks we need
        const has_else = if_stmt.else_body != null or if_stmt.else_if != null;

        // Create then block
        const then_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock(if (is_if_let) "if_let_then" else "then");

        // Create else block (if needed)
        var else_block_idx: u32 = undefined;
        if (has_else or is_if_let) {
            else_block_idx = @intCast(self.func().blocks.items.len);
            _ = try self.func().addBlock(if (is_if_let) "if_let_else" else "else");
        }

        // Create end block
        const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock(if (is_if_let) "if_let_end" else "end");

        // Emit conditional branch in the previous block
        const branch_target_if_false = if (has_else or is_if_let) else_block_idx else end_block_idx;
        const entry_block = &self.func().blocks.items[then_block_idx - 1];
        try entry_block.instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = condition_value }, .{ .block_ref = then_block_idx } },
            .result = branch_target_if_false,
        });

        // Save end block, remove it temporarily
        const end_block = self.func().blocks.pop().?;

        // If we have else, save it too
        var else_block: ?ir.BasicBlock = null;
        if (has_else or is_if_let) {
            else_block = self.func().blocks.pop().?;
        }

        // Now current block is "then" block
        // For if-let, bind the unwrapped value before executing body
        if (if_stmt.binding_name) |binding_name| {
            const opt_info = switch (cond_typed.ty) {
                .optional_type => |info| info,
                else => OptionalInfo{ .wrapped = .i64 },
            };
            const wrapped_type = opt_info.wrapped;
            const value_ptr = try self.func().emitGetFieldPtr(cond_typed.value, 8);
            const unwrapped_value = try self.func().emitLoad(value_ptr, wrapped_type);

            const binding_ptr = try self.func().emitAlloca(wrapped_type);
            try self.func().setValueName(binding_ptr, binding_name);
            try self.func().emitStore(binding_ptr, unwrapped_value);

            if (opt_info.wrapped_struct_type) |struct_name| {
                try self.var_map.put(self.allocator, binding_name, VarInfo.init(binding_ptr, .{ .struct_type = struct_name }, true, true));
            } else {
                try self.var_map.put(self.allocator, binding_name, VarInfo.init(
                    binding_ptr,
                    .{ .primitive = wrapped_type },
                    true,
                    false,
                ));
            }
        }

        // Convert then body statements
        for (if_stmt.body) |stmt| {
            try self.convertStatement(stmt);
        }

        // Remove binding from var_map after then body
        if (if_stmt.binding_name) |binding_name| {
            _ = self.var_map.remove(binding_name);
        }

        // Track the block that needs a branch to end (we'll add it after restoring end block)
        var then_exit_block_idx: ?u32 = null;
        if (self.func().currentBlock()) |block| {
            if (block.instructions.items.len == 0 or block.instructions.items[block.instructions.items.len - 1].op != .ret) {
                then_exit_block_idx = @intCast(self.func().blocks.items.len - 1);
            }
        }

        // Restore and generate else block if needed
        var else_exit_block_idx: ?u32 = null;
        if (has_else or is_if_let) {
            try self.func().blocks.append(self.allocator, else_block.?);

            // Check for else-if chain (not allowed for if-let)
            if (if_stmt.else_if) |else_if| {
                try self.convertIfStmt(else_if.*);
            } else if (if_stmt.else_body) |else_body| {
                for (else_body) |stmt| {
                    try self.convertStatement(stmt);
                }
            }

            // Track the block that needs a branch to end
            if (self.func().currentBlock()) |block| {
                if (block.instructions.items.len == 0 or block.instructions.items[block.instructions.items.len - 1].op != .ret) {
                    else_exit_block_idx = @intCast(self.func().blocks.items.len - 1);
                }
            }
        }

        // Restore end block - NOW get its actual index
        const actual_end_block_idx: u32 = @intCast(self.func().blocks.items.len);
        try self.func().blocks.append(self.allocator, end_block);

        // Now emit the deferred branches with the correct end block index
        if (then_exit_block_idx) |idx| {
            try self.func().blocks.items[idx].instructions.append(self.allocator, .{
                .op = .br,
                .operands = .{ .{ .block_ref = actual_end_block_idx }, .none },
                .result = undefined,
            });
        }
        if (else_exit_block_idx) |idx| {
            try self.func().blocks.items[idx].instructions.append(self.allocator, .{
                .op = .br,
                .operands = .{ .{ .block_ref = actual_end_block_idx }, .none },
                .result = undefined,
            });
        }
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

        // Save variable names before entering loop body (for scoped cleanup)
        var pre_loop_vars = std.StringHashMapUnmanaged(void){};
        defer pre_loop_vars.deinit(self.allocator);
        var iter = self.var_map.keyIterator();
        while (iter.next()) |key| {
            try pre_loop_vars.put(self.allocator, key.*, {});
        }

        // Convert body statements (body block is current)
        for (while_stmt.body) |stmt| {
            try self.convertStatement(stmt);
        }

        // Check for variables moved inside loop body - they would be invalid on next iteration
        var pre_iter = pre_loop_vars.keyIterator();
        while (pre_iter.next()) |key| {
            if (self.var_map.getPtr(key.*)) |var_info| {
                if (var_info.state == .moved) {
                    // Variable was moved in loop body - next iteration would use moved value
                    debug.astToIr("variable '{s}' was moved in loop body\n", .{key.*});
                    self.reportError(.E008);
                    return error.UseAfterMove;
                }
            }
        }

        // Free heap-allocated loop-scoped variables before branching back
        try self.freeLoopScopedHeapVars(&pre_loop_vars);

        // Emit unconditional branch back to condition block (if not already terminated)
        if (self.func().currentBlock()) |block| {
            const len = block.instructions.items.len;
            if (len == 0 or (block.instructions.items[len - 1].op != .ret and block.instructions.items[len - 1].op != .br and block.instructions.items[len - 1].op != .br_cond)) {
                try self.func().emitBr(cond_block_idx);
            }
        }

        // Remove loop-scoped variables from var_map
        var to_remove = std.ArrayListUnmanaged([]const u8){};
        defer to_remove.deinit(self.allocator);
        var var_iter = self.var_map.keyIterator();
        while (var_iter.next()) |key| {
            if (!pre_loop_vars.contains(key.*)) {
                try to_remove.append(self.allocator, key.*);
            }
        }
        for (to_remove.items) |key| {
            _ = self.var_map.remove(key);
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

    /// Convert a for-in loop by desugaring to iterator pattern:
    /// for item in collection 'loop'
    ///     body
    /// end 'loop'
    ///
    /// Desugars to:
    /// while true 'loop'
    ///     var __next_result = collection.next()
    ///     if __next_result == nil then break
    ///     var item = unwrap(__next_result)
    ///     body
    /// end 'loop'
    fn convertForStmt(self: *AstToIr, for_stmt: ast.ForStmt) ConvertError!void {
        // Create condition block
        const cond_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("forcond");

        // Emit unconditional branch from previous block to condition block
        try self.func().blocks.items[cond_block_idx - 1].instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = cond_block_idx }, .none },
            .result = undefined,
        });

        // In condition block: call .next() on the iterable
        const iterable_typed = try self.convertExpression(for_stmt.iterable);

        // Call the next() method on the iterable
        const next_result = try self.convertMethodCallOnTyped(iterable_typed, "next", &.{});

        // Load the tag from the optional result (offset 0) to check if nil
        const tag = try self.func().emitLoad(next_result.value, .i64);

        // Compare tag with 0 (nil - no more elements)
        const zero = try self.func().emitConstI64(0);
        const is_nil = try self.func().emitBinaryOp(.icmp_eq, tag, zero, .i64);

        // Compute is_not_nil (is_nil == 0) while still in condition block
        const is_not_nil = try self.func().emitBinaryOp(.icmp_eq, is_nil, zero, .i64);

        // Create body block
        const body_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("forbody");

        // Save loop context
        const saved_end_block = self.loop_end_block;
        const saved_cond_block = self.loop_cond_block;
        self.loop_cond_block = cond_block_idx;
        self.loop_end_block = 0xFFFFFFFF; // sentinel for patching

        // Save variable names before entering loop body
        var pre_loop_vars = std.StringHashMapUnmanaged(void){};
        defer pre_loop_vars.deinit(self.allocator);
        var iter = self.var_map.keyIterator();
        while (iter.next()) |key| {
            try pre_loop_vars.put(self.allocator, key.*, {});
        }

        // Extract the value from the optional result (offset 8)
        const opt_info = switch (next_result.ty) {
            .optional_type => |info| info,
            else => OptionalInfo{ .wrapped = .i64 },
        };
        const value_offset = try self.func().emitConstI64(8);
        const value_ptr = try self.func().emitBinaryOp(.add, next_result.value, value_offset, .ptr);
        const element_value = try self.func().emitLoad(value_ptr, opt_info.wrapped);

        // Register the loop variable - allocate a slot and store the value there
        const var_slot = try self.func().emitAlloca(opt_info.wrapped);
        try self.func().emitStore(var_slot, element_value);
        try self.var_map.put(self.allocator, for_stmt.var_name, VarInfo.init(var_slot, .{ .primitive = opt_info.wrapped }, false, false));

        // Convert body statements
        for (for_stmt.body) |stmt| {
            try self.convertStatement(stmt);
        }

        // Check for variables moved inside loop body
        var pre_iter = pre_loop_vars.keyIterator();
        while (pre_iter.next()) |key| {
            if (self.var_map.getPtr(key.*)) |var_info| {
                if (var_info.state == .moved) {
                    debug.astToIr("variable '{s}' was moved in loop body\n", .{key.*});
                    self.reportError(.E008);
                    return error.UseAfterMove;
                }
            }
        }

        // Free heap-allocated loop-scoped variables
        try self.freeLoopScopedHeapVars(&pre_loop_vars);

        // Emit unconditional branch back to condition block
        if (self.func().currentBlock()) |block| {
            const len = block.instructions.items.len;
            if (len == 0 or (block.instructions.items[len - 1].op != .ret and block.instructions.items[len - 1].op != .br and block.instructions.items[len - 1].op != .br_cond)) {
                try self.func().emitBr(cond_block_idx);
            }
        }

        // Remove loop-scoped variables from var_map
        var to_remove = std.ArrayListUnmanaged([]const u8){};
        defer to_remove.deinit(self.allocator);
        var var_iter = self.var_map.keyIterator();
        while (var_iter.next()) |key| {
            if (!pre_loop_vars.contains(key.*)) {
                try to_remove.append(self.allocator, key.*);
            }
        }
        for (to_remove.items) |key| {
            _ = self.var_map.remove(key);
        }

        // Now create continuation block
        const cont_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("forcont");

        // Emit conditional branch in condition block: if is_not_nil, go to body; else fall through to cont
        try self.func().blocks.items[cond_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_not_nil }, .{ .block_ref = body_block_idx } },
            .result = cont_block_idx,
        });

        // Patch any break statements
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

    fn convertElseUnwrapDecl(self: *AstToIr, unwrap: ast.ElseUnwrapDecl) ConvertError!void {
        // Convert the optional expression
        const opt_typed = try self.convertExpression(unwrap.optional_expr.*);

        // Load the tag from the optional (offset 0)
        const tag = try self.func().emitLoad(opt_typed.value, .i64);

        // Compare tag with 1 (has value)
        const one = try self.func().emitConstI64(1);
        const has_value = try self.func().emitBinaryOp(.icmp_eq, tag, one, .i64);

        // Get the wrapped type info
        const opt_info = switch (opt_typed.ty) {
            .optional_type => |info| info,
            else => OptionalInfo{ .wrapped = .i64 },
        };
        const wrapped_type = opt_info.wrapped;

        // Create a stack slot for the variable being declared
        const var_ptr = try self.func().emitAlloca(wrapped_type);
        try self.func().setValueName(var_ptr, unwrap.var_name);

        // Add to var_map so the default body can assign to it
        if (opt_info.wrapped_struct_type) |struct_name| {
            try self.var_map.put(self.allocator, unwrap.var_name, VarInfo.init(
                var_ptr,
                .{ .struct_type = struct_name },
                true, // mutable
                true,
            ));
        } else {
            // Primitive binding: use init (no slot indirection needed)
            try self.var_map.put(self.allocator, unwrap.var_name, VarInfo.init(var_ptr, .{ .primitive = wrapped_type }, true, // mutable
                false));
        }

        // Create blocks
        const some_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("else_unwrap_some");

        const nil_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("else_unwrap_nil");

        const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("else_unwrap_end");

        // Emit conditional branch
        const entry_block = &self.func().blocks.items[some_block_idx - 1];
        try entry_block.instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = has_value }, .{ .block_ref = some_block_idx } },
            .result = nil_block_idx,
        });

        // Save end and nil blocks
        const end_block = self.func().blocks.pop().?;
        const nil_block = self.func().blocks.pop().?;

        // In "some" block: unwrap and store value
        const value_ptr = try self.func().emitGetFieldPtr(opt_typed.value, 8);
        const unwrapped_value = try self.func().emitLoad(value_ptr, wrapped_type);
        try self.func().emitStore(var_ptr, unwrapped_value);
        try self.func().emitBr(end_block_idx);

        // Restore nil block
        try self.func().blocks.append(self.allocator, nil_block);

        // Execute default body (which should assign to var_name)
        for (unwrap.default_body) |stmt| {
            try self.convertStatement(stmt);
        }

        // Branch to end
        if (self.func().currentBlock()) |block| {
            if (block.instructions.items.len == 0 or block.instructions.items[block.instructions.items.len - 1].op != .ret) {
                try self.func().emitBr(end_block_idx);
            }
        }

        // Restore end block
        try self.func().blocks.append(self.allocator, end_block);
    }

    // ------------------------------------------------------------------------
    // Expression Conversion
    // ------------------------------------------------------------------------

    fn convertExpression(self: *AstToIr, expr: ast.Expression) ConvertError!TypedValue {
        return switch (expr) {
            .integer => |v| .{ .value = try self.func().emitConstI64(v), .ty = .{ .primitive = .i64 } },
            .float_lit => |v| .{ .value = try self.func().emitConstF64(v), .ty = .{ .primitive = .f64 } },
            .bool_lit => |v| .{ .value = try self.func().emitConstI64(if (v) 1 else 0), .ty = .{ .primitive = .i64 } },
            .nil_lit => self.createNilOptional(.i64),
            .self_expr => self.convertSelfExpr(),
            .identifier => |name| self.convertIdentifierOrField(name),
            .unary => |un| self.convertUnary(un),
            .binary => |bin| self.convertBinary(bin),
            .compare => |cmp| self.convertCompare(cmp),
            .logical => |log| self.convertLogical(log),
            .call => |call| self.convertCall(call),
            .struct_init => |sinit| self.convertStructInit(sinit),
            .field_access => |fa| self.convertFieldAccess(fa),
            .array_literal => |arr| self.convertArrayLiteral(arr),
            .index => |idx| self.convertIndex(idx),
            .sized_array => |sized| self.convertSizedArray(sized),
            .method_call => |mcall| self.convertMethodCallExpr(mcall),
            .nil_coalesce => |nc| self.convertNilCoalesce(nc),
        };
    }

    /// Convert 'self' expression - reference to current instance
    fn convertSelfExpr(self: *AstToIr) ConvertError!TypedValue {
        const self_val = self.self_ptr orelse {
            self.reportError(.E005);
            return error.UndefinedVariable;
        };
        const type_name = self.current_type_name orelse {
            self.reportError(.E005);
            return error.UndefinedVariable;
        };
        // Mark self as used
        if (self.var_map.getPtr("self")) |info| {
            info.used = true;
        }
        return .{ .value = self_val, .ty = .{ .struct_type = type_name } };
    }

    /// Convert identifier, with implicit self field resolution for methods
    fn convertIdentifierOrField(self: *AstToIr, name: []const u8) ConvertError!TypedValue {
        // First try to find it as a regular variable
        if (self.var_map.contains(name)) {
            return self.convertIdentifier(name);
        }

        // If inside a method, check if it's a field of the current type (implicit self)
        if (self.self_ptr != null and self.current_type_name != null) {
            const type_name = self.current_type_name.?;
            if (self.type_map.get(type_name)) |type_info| {
                if (type_info == .struct_type) {
                    const struct_info = type_info.struct_type;
                    // Check if name matches a field
                    for (struct_info.fields) |field| {
                        if (std.mem.eql(u8, field.name, name)) {
                            // It's a field - access via self
                            const self_val = self.self_ptr.?;
                            const field_ptr = try self.func().emitGetFieldPtr(self_val, field.offset);

                            // Mark self as used
                            if (self.var_map.getPtr("self")) |info| {
                                info.used = true;
                            }

                            // Struct fields are embedded; others need to be loaded
                            const value = if (field.value_type == .struct_type)
                                field_ptr
                            else
                                try self.func().emitLoad(field_ptr, field.value_type.toPrimitiveType());

                            return .{ .value = value, .ty = field.value_type };
                        }
                    }
                }
            }
        }

        // Fall back to original identifier behavior (will produce error for undefined)
        return self.convertIdentifier(name);
    }

    /// Create a "some" optional from a value
    fn createSomeOptional(self: *AstToIr, value: ir.Value, wrapped_type: ir.Type) ConvertError!TypedValue {
        // Allocate 16 bytes: [tag: i64][value: i64]
        const opt_ptr = try self.func().emitAllocaSized(16);

        // Store tag = 1 (has value)
        const one = try self.func().emitConstI64(1);
        try self.func().emitStore(opt_ptr, one);

        // Store value at offset 8
        const value_ptr = try self.func().emitGetFieldPtr(opt_ptr, 8);
        try self.func().emitStore(value_ptr, value);

        return .{
            .value = opt_ptr,
            .ty = .{ .optional_type = .{ .wrapped = wrapped_type } },
        };
    }

    /// Create a nil optional with a specific wrapped type
    fn createNilOptional(self: *AstToIr, wrapped_type: ir.Type) ConvertError!TypedValue {
        const opt_ptr = try self.func().emitAllocaSized(16);
        const zero = try self.func().emitConstI64(0);
        try self.func().emitStore(opt_ptr, zero); // tag = 0 (nil)
        return .{
            .value = opt_ptr,
            .ty = .{ .optional_type = .{ .wrapped = wrapped_type } },
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

        // Reference types may use a slot indirection; value types always load
        const value = if (info.ty == .struct_type or info.ty == .array_type or info.ty == .optional_type)
            if (info.uses_slot) try self.func().emitLoad(info.ptr, .ptr) else info.ptr
        else
            try self.func().emitLoad(info.ptr, info.ty.toPrimitiveType());

        return .{ .value = value, .ty = info.ty };
    }

    fn convertUnary(self: *AstToIr, un: ast.UnaryExpr) ConvertError!TypedValue {
        const operand = try self.convertExpression(un.operand.*);
        const is_float = operand.ty.toPrimitiveType() == .f64;

        switch (un.op) {
            .negate => {
                // Negate: 0 - x
                const zero = if (is_float) try self.func().emitConstF64(0.0) else try self.func().emitConstI64(0);
                const op: ir.Instruction.Op = if (is_float) .fsub else .sub;
                const ty: ir.Type = if (is_float) .f64 else .i64;
                const result = try self.func().emitBinaryOp(op, zero, operand.value, ty);
                return .{ .value = result, .ty = .{ .primitive = ty } };
            },
            .not => {
                // Logical not: x == 0
                const zero = try self.func().emitConstI64(0);
                const result = try self.func().emitBinaryOp(.icmp_eq, operand.value, zero, .i64);
                return .{ .value = result, .ty = .{ .primitive = .i64 } };
            },
        }
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

        // Handle optional == nil or optional != nil comparisons
        // An optional's tag is at offset 0: 0 means nil, 1 means has value
        const left_is_optional = left.ty == .optional_type;
        const right_is_optional = right.ty == .optional_type;

        if (left_is_optional and right_is_optional) {
            // Both are optional - comparing optional to nil (or nil to optional)
            // Compare the tag field to 0
            const opt_value = left.value;
            const tag = try self.func().emitLoad(opt_value, .i64);
            const zero = try self.func().emitConstI64(0);

            const result = switch (cmp.op) {
                .eq => try self.func().emitBinaryOp(.icmp_eq, tag, zero, .i64),
                .ne => try self.func().emitBinaryOp(.icmp_ne, tag, zero, .i64),
                else => {
                    // < > <= >= don't make sense for optional/nil comparison
                    self.reportError(.E003);
                    return error.TypeMismatch;
                },
            };
            return .{ .value = result, .ty = .{ .primitive = .i64 } };
        } else if (left_is_optional) {
            // Left is optional, right is a value - unwrap left and compare
            // Load the value from offset 8 of the optional
            const value_ptr = try self.func().emitGetFieldPtr(left.value, 8);
            const left_val = try self.func().emitLoad(value_ptr, left.ty.optional_type.wrapped);

            const op: ir.Instruction.Op = switch (cmp.op) {
                .eq => .icmp_eq,
                .ne => .icmp_ne,
                .lt => .icmp_lt,
                .le => .icmp_le,
                .gt => .icmp_gt,
                .ge => .icmp_ge,
            };
            const result = try self.func().emitBinaryOp(op, left_val, right.value, .i64);
            return .{ .value = result, .ty = .{ .primitive = .i64 } };
        } else if (right_is_optional) {
            // Right is optional, left is a value - unwrap right and compare
            // Load the value from offset 8 of the optional
            const value_ptr = try self.func().emitGetFieldPtr(right.value, 8);
            const right_val = try self.func().emitLoad(value_ptr, right.ty.optional_type.wrapped);

            const op: ir.Instruction.Op = switch (cmp.op) {
                .eq => .icmp_eq,
                .ne => .icmp_ne,
                .lt => .icmp_lt,
                .le => .icmp_le,
                .gt => .icmp_gt,
                .ge => .icmp_ge,
            };
            const result = try self.func().emitBinaryOp(op, left.value, right_val, .i64);
            return .{ .value = result, .ty = .{ .primitive = .i64 } };
        }

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

    fn convertLogical(self: *AstToIr, log: ast.LogicalExpr) ConvertError!TypedValue {
        const left = try self.convertExpression(log.left.*);
        const right = try self.convertExpression(log.right.*);

        // Logical 'and' - both values must be non-zero (truthy)
        // We implement this as: (left != 0) & (right != 0)
        // Using bitwise AND to combine two boolean results
        switch (log.op) {
            .@"and" => {
                // Convert left to boolean: left != 0
                const zero = try self.func().emitConstI64(0);
                const left_bool = try self.func().emitBinaryOp(.icmp_ne, left.value, zero, .i64);
                // Convert right to boolean: right != 0
                const right_bool = try self.func().emitBinaryOp(.icmp_ne, right.value, zero, .i64);
                // AND the two boolean values using multiplication (0*x=0, 1*1=1)
                const result = try self.func().emitBinaryOp(.mul, left_bool, right_bool, .i64);
                return .{ .value = result, .ty = .{ .primitive = .i64 } };
            },
        }
    }

    /// Convert nil coalescing expression: `optional or default`
    /// Returns the unwrapped value if optional has a value, otherwise returns default
    fn convertNilCoalesce(self: *AstToIr, nc: ast.NilCoalesceExpr) ConvertError!TypedValue {
        // Evaluate the optional expression
        const opt_typed = try self.convertExpression(nc.optional.*);

        // Get optional type info - must be an optional type
        const opt_info = switch (opt_typed.ty) {
            .optional_type => |info| info,
            else => {
                // Left operand is not an optional type - this is an error
                self.reportError(.E017);
                return error.TypeMismatch;
            },
        };

        const wrapped_type = opt_info.wrapped;

        // Allocate result storage in entry block BEFORE branching
        const result_ptr = try self.func().emitAlloca(wrapped_type);

        // Load tag from optional (offset 0) and compare with 1 (has value)
        const tag = try self.func().emitLoad(opt_typed.value, .i64);
        const one = try self.func().emitConstI64(1);
        const has_value = try self.func().emitBinaryOp(.icmp_eq, tag, one, .i64);

        // Create blocks for branching
        const has_value_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("coalesce_has_value");
        const use_default_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("coalesce_default");
        const merge_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("coalesce_merge");

        // Emit conditional branch from current block
        const entry_block = &self.func().blocks.items[has_value_block_idx - 1];
        try entry_block.instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = has_value }, .{ .block_ref = has_value_block_idx } },
            .result = use_default_block_idx,
        });

        // Pop merge block temporarily
        const merge_block = self.func().blocks.pop().?;
        // Pop default block temporarily
        const default_block = self.func().blocks.pop().?;

        // Now in has_value block: extract the unwrapped value
        const value_ptr = try self.func().emitGetFieldPtr(opt_typed.value, 8);
        const unwrapped_value = try self.func().emitLoad(value_ptr, wrapped_type);

        // Store unwrapped value to result
        try self.func().emitStore(result_ptr, unwrapped_value);

        // Branch to merge
        try self.func().currentBlock().?.instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = merge_block_idx }, .none },
            .result = null,
        });

        // Push default block back and switch to it
        try self.func().blocks.append(self.allocator, default_block);

        // Evaluate default expression here (short-circuit evaluation)
        const default_typed = try self.convertExpression(nc.default.*);

        // Store default value to result
        try self.func().emitStore(result_ptr, default_typed.value);

        // Branch to merge
        try self.func().currentBlock().?.instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = merge_block_idx }, .none },
            .result = null,
        });

        // Push merge block back and switch to it
        try self.func().blocks.append(self.allocator, merge_block);

        // Load result
        const result = try self.func().emitLoad(result_ptr, wrapped_type);
        return .{ .value = result, .ty = default_typed.ty };
    }

    fn convertStructInit(self: *AstToIr, sinit: ast.StructInitExpr) ConvertError!TypedValue {
        // Build monomorphized type name if there are type arguments
        const type_name = if (sinit.type_args.len > 0) blk: {
            // Check if monomorphized version exists, if not create it
            const mono_name = try self.getOrCreateMonomorphizedType(sinit.type_name, sinit.type_args);
            break :blk mono_name;
        } else self.resolveTypeName(sinit.type_name);

        const struct_info = try self.lookupStructInfo(type_name);
        const struct_ptr = try self.func().emitAllocaSized(struct_info.size);
        try self.initStructIntoResolved(sinit, struct_ptr, type_name);
        return .{ .value = struct_ptr, .ty = .{ .struct_type = type_name } };
    }

    /// Initialize struct fields into an existing pointer (used for sret returns)
    fn initStructInto(self: *AstToIr, sinit: ast.StructInitExpr, dest_ptr: ir.Value) ConvertError!void {
        // Build monomorphized type name if there are type arguments
        const type_name = if (sinit.type_args.len > 0) blk: {
            const mono_name = try self.getOrCreateMonomorphizedType(sinit.type_name, sinit.type_args);
            break :blk mono_name;
        } else self.resolveTypeName(sinit.type_name);
        try self.initStructIntoResolved(sinit, dest_ptr, type_name);
    }

    /// Initialize struct fields into an existing pointer with resolved type name
    fn initStructIntoResolved(self: *AstToIr, sinit: ast.StructInitExpr, dest_ptr: ir.Value, type_name: []const u8) ConvertError!void {
        const struct_info = try self.lookupStructInfo(type_name);

        // Zero the entire struct first to ensure uninitialized fields are zero
        // This is important for types like Array that need zeroed memory
        try self.func().emitMemset(dest_ptr, 0, struct_info.size);

        for (sinit.fields) |field_init| {
            const field_info = try lookupField(struct_info, field_init.name);
            const field_ptr = try self.func().emitGetFieldPtr(dest_ptr, field_info.offset);
            const field_val = try self.convertExpression(field_init.value.*);

            // Track ownership transfer for array/struct fields
            try self.trackFieldOwnershipTransfer(field_init.value.*, type_name);

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
        if (expr != .identifier) return;

        const var_name = expr.identifier;
        const var_info = self.var_map.getPtr(var_name) orelse return;

        // Reference types are moved, not copied
        const is_reference = var_info.ty == .array_type or var_info.ty == .struct_type or var_info.ty == .optional_type;
        if (!is_reference) return;

        if (!var_info.is_mutable) {
            debug.astToIr("cannot move immutable variable '{s}' into struct\n", .{var_name});
            self.reportError(.E010);
            return error.ImmutableMove;
        }
        var_info.markMoved(target_type, self.current_line);
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
                std.debug.print("[AST->IR] convertFieldAccess: expected struct type for field '{s}'\n", .{faccess.field_name});
                self.reportError(.E006);
                return error.UnknownType;
            },
        };

        const struct_info = try self.lookupStructInfo(type_name);
        const field_info = try lookupField(struct_info, faccess.field_name);
        const field_ptr = try self.func().emitGetFieldPtr(base.value, field_info.offset);

        // Struct fields are embedded (return ptr directly); others are loaded
        const value = if (field_info.value_type == .struct_type)
            field_ptr
        else
            try self.func().emitLoad(field_ptr, field_info.value_type.toPrimitiveType());

        return .{ .value = value, .ty = field_info.value_type };
    }

    fn convertCall(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        // Handle built-in functions
        if (self.convertBuiltin(call)) |result| {
            return result;
        } else |builtin_err| switch (builtin_err) {
            error.NotABuiltin => {},
            else => return builtin_err,
        }

        // If inside a method body, check if calling a method of the current type (implicit self)
        // Only do this if we have an active function (not during registration)
        if (self.self_ptr != null and self.current_type_name != null and self.current_func != null) {
            const type_name = self.current_type_name.?;
            // Build mangled name on stack to avoid allocation
            var mangled_buf: [256]u8 = undefined;
            if (std.fmt.bufPrint(&mangled_buf, "{s}${s}", .{ type_name, call.func_name })) |mangled_name| {
                if (self.func_map.contains(mangled_name)) {
                    // It's a method of the current type - call via self
                    debug.astToIr("Implicit method call: {s} (self_ptr=%{d})", .{ mangled_name, self.self_ptr.? });
                    return self.emitMethodCall(type_name, call.func_name, call.args, self.self_ptr.?);
                }
            } else |_| {
                // Name too long, skip implicit method check
            }
        }

        const func_info = self.func_map.get(call.func_name) orelse {
            // Unknown function (e.g., from stdlib) - still pass the arguments
            const args = try self.func().allocator.alloc(ir.Value, call.args.len);
            for (call.args, 0..) |arg_expr, i| {
                const arg = try self.convertExpression(arg_expr);
                args[i] = arg.value;
            }
            // Assume i64 return type - this will be resolved at link time
            const result = try self.func().emitCall(call.func_name, args, .i64);
            return .{ .value = result orelse 0, .ty = .{ .primitive = .i64 } };
        };

        // Check if return type is optional (needs sret for 16-byte struct)
        const returns_optional = if (func_info.return_value_type) |vt|
            vt == .optional_type
        else
            false;

        // Check if callee returns a struct or optional (needs sret)
        const returns_struct = func_info.return_type_name != null or returns_optional;
        var sret_buffer: ?ir.Value = null;

        // Allocate args: +1 if using sret for hidden first parameter
        const num_args = call.args.len + @as(usize, if (returns_struct) 1 else 0);
        const args = try self.func().allocator.alloc(ir.Value, num_args);

        // If returning struct or optional, allocate buffer in caller and pass as first arg
        if (returns_struct) {
            if (returns_optional) {
                // Optional is 16 bytes (tag + value)
                sret_buffer = try self.func().emitAllocaSized(16);
            } else {
                const struct_name = func_info.return_type_name.?;
                const struct_info = try self.lookupStructInfo(struct_name);
                sret_buffer = try self.func().emitAllocaSized(struct_info.size);
            }
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
        // If the function returns an optional with sret, return the sret buffer
        if (returns_optional) {
            return .{ .value = sret_buffer.?, .ty = func_info.return_value_type.? };
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

    /// Check if current source file is part of stdlib
    /// Check if we're in stdlib code (either compiling a stdlib file or converting stdlib methods)
    fn isStdlibFile(self: *AstToIr) bool {
        // Allow stdlib builtins when converting monomorphized stdlib methods
        if (self.in_stdlib_method) return true;

        const path = self.source_file orelse return false;
        // Check for /stdlib/ or \stdlib\ in path
        return std.mem.indexOf(u8, path, "/stdlib/") != null or
            std.mem.indexOf(u8, path, "\\stdlib\\") != null;
    }

    fn convertBuiltin(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        // Check for __managed_array_* intrinsics (stdlib-only)
        if (std.mem.startsWith(u8, call.func_name, "__managed_array_")) {
            if (!self.isStdlibFile()) {
                self.reportErrorWithDetails(.E016, call.func_name);
                return error.SemanticError;
            }
            return self.convertManagedArrayIntrinsic(call);
        }

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

    // ------------------------------------------------------------------------
    // __ManagedArray Intrinsics (stdlib-only)
    // ------------------------------------------------------------------------

    fn convertManagedArrayIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        const name = call.func_name;

        if (std.mem.eql(u8, name, "__managed_array_len")) {
            return self.intrinsicManagedArrayLen(call);
        } else if (std.mem.eql(u8, name, "__managed_array_capacity")) {
            return self.intrinsicManagedArrayCapacity(call);
        } else if (std.mem.eql(u8, name, "__managed_array_set_at")) {
            return self.intrinsicManagedArraySetAt(call);
        } else if (std.mem.eql(u8, name, "__managed_array_set_length")) {
            return self.intrinsicManagedArraySetLength(call);
        } else if (std.mem.eql(u8, name, "__managed_array_grow")) {
            return self.intrinsicManagedArrayGrow(call);
        } else if (std.mem.eql(u8, name, "__managed_array_shift_right")) {
            return self.intrinsicManagedArrayShiftRight(call);
        } else if (std.mem.eql(u8, name, "__managed_array_shift_left")) {
            return self.intrinsicManagedArrayShiftLeft(call);
        } else {
            self.reportErrorWithDetails(.E016, name);
            return error.SemanticError;
        }
    }

    /// __managed_array_len(managed) -> int
    /// Returns the length field of the __ManagedArray
    fn intrinsicManagedArrayLen(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        if (call.args.len != 1) {
            self.reportError(.E011);
            return error.WrongArgumentCount;
        }

        const managed = try self.convertExpression(call.args[0]);
        // Load _len field (offset 8 in __ManagedArray)
        const len_ptr = try self.func().emitGetFieldPtr(managed.value, 8);
        const len = try self.func().emitLoad(len_ptr, .i64);

        return .{ .value = len, .ty = .{ .primitive = .i64 } };
    }

    /// __managed_array_capacity(managed) -> int
    /// Returns the capacity field of the __ManagedArray
    fn intrinsicManagedArrayCapacity(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        if (call.args.len != 1) {
            self.reportError(.E011);
            return error.WrongArgumentCount;
        }

        const managed = try self.convertExpression(call.args[0]);
        // Load _capacity field (offset 16 in __ManagedArray)
        const cap_ptr = try self.func().emitGetFieldPtr(managed.value, 16);
        const cap = try self.func().emitLoad(cap_ptr, .i64);

        return .{ .value = cap, .ty = .{ .primitive = .i64 } };
    }

    /// __managed_array_set_at(managed, index, value) -> void
    /// Sets element at index (no bounds checking - caller must verify)
    fn intrinsicManagedArraySetAt(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        if (call.args.len != 3) {
            self.reportError(.E011);
            return error.WrongArgumentCount;
        }

        const managed = try self.convertExpression(call.args[0]);
        const index = try self.convertExpression(call.args[1]);
        const value = try self.convertExpression(call.args[2]);

        // Load buffer pointer (offset 0)
        const buf_ptr = try self.func().emitLoad(managed.value, .ptr);

        // Calculate element address: buf_ptr + index * 8
        const elem_ptr = try self.func().emitGetElemPtr(buf_ptr, index.value, 8);

        // Store value
        try self.func().emitStore(elem_ptr, value.value);

        return .{ .value = 0, .ty = .{ .primitive = .void } };
    }

    /// __managed_array_set_length(managed, new_len) -> void
    /// Sets the length field of the __ManagedArray
    fn intrinsicManagedArraySetLength(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        if (call.args.len != 2) {
            self.reportError(.E011);
            return error.WrongArgumentCount;
        }

        const managed = try self.convertExpression(call.args[0]);
        const new_len = try self.convertExpression(call.args[1]);

        // Store to _len field (offset 8)
        const len_ptr = try self.func().emitGetFieldPtr(managed.value, 8);
        try self.func().emitStore(len_ptr, new_len.value);

        return .{ .value = 0, .ty = .{ .primitive = .void } };
    }

    /// __managed_array_grow(managed, new_capacity) -> void
    /// Reallocates buffer to new_capacity (must be > current capacity)
    fn intrinsicManagedArrayGrow(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        if (call.args.len != 2) {
            self.reportError(.E011);
            return error.WrongArgumentCount;
        }

        const managed = try self.convertExpression(call.args[0]);
        const new_capacity = try self.convertExpression(call.args[1]);

        // Calculate new buffer size: new_capacity * 8
        const eight = try self.func().emitConstI64(8);
        const new_size = try self.func().emitBinaryOp(.mul, new_capacity.value, eight, .i64);

        // Load current buffer pointer
        const buf_ptr = try self.func().emitLoad(managed.value, .ptr);

        // Realloc: new_buf = heap_realloc(buf_ptr, new_size)
        const new_buf = try self.func().emitHeapRealloc(buf_ptr, new_size);

        // Store new buffer pointer (offset 0)
        try self.func().emitStore(managed.value, new_buf);

        // Store new capacity (offset 16)
        const cap_ptr = try self.func().emitGetFieldPtr(managed.value, 16);
        try self.func().emitStore(cap_ptr, new_capacity.value);

        return .{ .value = 0, .ty = .{ .primitive = .void } };
    }

    /// __managed_array_shift_right(managed, start_index, count) -> void
    /// Shifts elements from start_index right by count positions
    /// Iterates backwards from end to start to handle overlap correctly
    fn intrinsicManagedArrayShiftRight(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        if (call.args.len != 3) {
            self.reportError(.E011);
            return error.WrongArgumentCount;
        }

        const managed = try self.convertExpression(call.args[0]);
        const start_index = try self.convertExpression(call.args[1]);
        const count = try self.convertExpression(call.args[2]);

        // Load buffer and length
        const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
        const len_ptr = try self.func().emitGetFieldPtr(managed.value, 8);
        const len = try self.func().emitLoad(len_ptr, .i64);

        const eight = try self.func().emitConstI64(8);
        const one = try self.func().emitConstI64(1);

        // Loop from i = len-1 down to start_index, moving each element right by count
        const i_ptr = try self.func().emitAllocaSized(8);
        const init_i = try self.func().emitBinaryOp(.sub, len, one, .i64);
        try self.func().emitStore(i_ptr, init_i);

        // Create loop blocks using helper
        var loop = try LoopBlocks.create(self, "shift_right_cond", "shift_right_body", "shift_right_end");

        // Condition: i >= start_index (cond block is current)
        const i_val = try self.func().emitLoad(i_ptr, .i64);
        const cond = try self.func().emitBinaryOp(.icmp_ge, i_val, start_index.value, .i64);

        // Emit conditional branch and switch to body block
        try loop.emitCondBranch(self, cond);

        // Body: arr[i + count] = arr[i]; i--;
        const i_val2 = try self.func().emitLoad(i_ptr, .i64);
        const src_offset = try self.func().emitBinaryOp(.mul, i_val2, eight, .i64);
        const src_ptr = try self.func().emitBinaryOp(.add, buf_ptr, src_offset, .ptr);
        const elem_val = try self.func().emitLoad(src_ptr, .i64);

        const dst_idx = try self.func().emitBinaryOp(.add, i_val2, count.value, .i64);
        const dst_offset = try self.func().emitBinaryOp(.mul, dst_idx, eight, .i64);
        const dst_ptr = try self.func().emitBinaryOp(.add, buf_ptr, dst_offset, .ptr);
        try self.func().emitStore(dst_ptr, elem_val);

        const new_i = try self.func().emitBinaryOp(.sub, i_val2, one, .i64);
        try self.func().emitStore(i_ptr, new_i);
        try self.func().emitBr(loop.cond_block_idx);

        // Finish loop and restore end block
        try loop.finish(self);

        return .{ .value = 0, .ty = .{ .primitive = .void } };
    }

    /// __managed_array_shift_left(managed, start_index, count) -> void
    /// Shifts elements from start_index+count left by count positions
    /// Iterates forwards from start to end to handle overlap correctly
    fn intrinsicManagedArrayShiftLeft(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        if (call.args.len != 3) {
            self.reportError(.E011);
            return error.WrongArgumentCount;
        }

        const managed = try self.convertExpression(call.args[0]);
        const start_index = try self.convertExpression(call.args[1]);
        const count = try self.convertExpression(call.args[2]);

        // Load buffer and length
        const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
        const len_ptr = try self.func().emitGetFieldPtr(managed.value, 8);
        const len = try self.func().emitLoad(len_ptr, .i64);

        const eight = try self.func().emitConstI64(8);
        const one = try self.func().emitConstI64(1);

        // Source start index: start_index + count
        const src_start = try self.func().emitBinaryOp(.add, start_index.value, count.value, .i64);

        // Loop from i = 0, while src_start + i < len
        const i_ptr = try self.func().emitAllocaSized(8);
        const zero = try self.func().emitConstI64(0);
        try self.func().emitStore(i_ptr, zero);

        // Create loop blocks using helper
        var loop = try LoopBlocks.create(self, "shift_left_cond", "shift_left_body", "shift_left_end");

        // Condition: src_start + i < len (cond block is current)
        const i_val = try self.func().emitLoad(i_ptr, .i64);
        const src_idx = try self.func().emitBinaryOp(.add, src_start, i_val, .i64);
        const cond = try self.func().emitBinaryOp(.icmp_lt, src_idx, len, .i64);

        // Emit conditional branch and switch to body block
        try loop.emitCondBranch(self, cond);

        // Body: arr[start_index + i] = arr[src_start + i]; i++;
        const i_val2 = try self.func().emitLoad(i_ptr, .i64);
        const src_idx2 = try self.func().emitBinaryOp(.add, src_start, i_val2, .i64);
        const src_offset = try self.func().emitBinaryOp(.mul, src_idx2, eight, .i64);
        const src_ptr = try self.func().emitBinaryOp(.add, buf_ptr, src_offset, .ptr);
        const elem_val = try self.func().emitLoad(src_ptr, .i64);

        const dst_idx = try self.func().emitBinaryOp(.add, start_index.value, i_val2, .i64);
        const dst_offset = try self.func().emitBinaryOp(.mul, dst_idx, eight, .i64);
        const dst_ptr = try self.func().emitBinaryOp(.add, buf_ptr, dst_offset, .ptr);
        try self.func().emitStore(dst_ptr, elem_val);

        const new_i = try self.func().emitBinaryOp(.add, i_val2, one, .i64);
        try self.func().emitStore(i_ptr, new_i);
        try self.func().emitBr(loop.cond_block_idx);

        // Finish loop and restore end block
        try loop.finish(self);

        return .{ .value = 0, .ty = .{ .primitive = .void } };
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

    /// Index into a __ManagedArray: managed[i]
    /// Returns the element as an optional (bounds-checked)
    fn convertManagedArrayIndex(self: *AstToIr, managed_ptr: ir.Value, index_expr: ast.Expression) ConvertError!TypedValue {
        const idx_typed = try self.convertExpression(index_expr);

        // Load buffer pointer (offset 0) and length (offset 8)
        const buf_ptr = try self.func().emitLoad(managed_ptr, .ptr);
        const len_ptr = try self.func().emitGetFieldPtr(managed_ptr, 8);
        const len = try self.func().emitLoad(len_ptr, .i64);

        // Allocate the result optional (16 bytes)
        const opt_ptr = try self.func().emitAllocaSized(16);

        // Bounds check: index >= 0 AND index < len
        const zero = try self.func().emitConstI64(0);
        const is_non_negative = try self.func().emitBinaryOp(.icmp_ge, idx_typed.value, zero, .i64);
        const is_less_than_len = try self.func().emitBinaryOp(.icmp_lt, idx_typed.value, len, .i64);
        const in_bounds = try self.func().emitBinaryOp(.mul, is_non_negative, is_less_than_len, .i64);

        // Create blocks for branching
        const in_bounds_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("managed_index_in_bounds");

        const out_of_bounds_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("managed_index_out_of_bounds");

        const merge_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("managed_index_merge");

        // Emit conditional branch in previous block
        const entry_block = &self.func().blocks.items[in_bounds_block_idx - 1];
        try entry_block.instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = in_bounds }, .{ .block_ref = in_bounds_block_idx } },
            .result = out_of_bounds_block_idx,
        });

        // Save merge and out_of_bounds blocks
        const merge_block = self.func().blocks.pop().?;
        const out_of_bounds_block = self.func().blocks.pop().?;

        // In-bounds block: create some(element)
        const one_val = try self.func().emitConstI64(1);
        try self.func().emitStore(opt_ptr, one_val); // tag = 1

        // Calculate element pointer: buf_ptr + index * 8
        const eight = try self.func().emitConstI64(8);
        const offset = try self.func().emitBinaryOp(.mul, idx_typed.value, eight, .i64);
        const elem_ptr = try self.func().emitBinaryOp(.add, buf_ptr, offset, .ptr);
        const val = try self.func().emitLoad(elem_ptr, .i64);

        const value_slot = try self.func().emitGetFieldPtr(opt_ptr, 8);
        try self.func().emitStore(value_slot, val);

        try self.func().emitBr(merge_block_idx);

        // Restore out-of-bounds block
        try self.func().blocks.append(self.allocator, out_of_bounds_block);

        // Out-of-bounds block: create nil
        const zero_val = try self.func().emitConstI64(0);
        try self.func().emitStore(opt_ptr, zero_val); // tag = 0

        try self.func().emitBr(merge_block_idx);

        // Restore merge block
        try self.func().blocks.append(self.allocator, merge_block);

        // Return the optional (element type is i64 for now)
        return .{
            .value = opt_ptr,
            .ty = .{ .optional_type = .{
                .wrapped = .i64,
                .wrapped_struct_type = null,
            } },
        };
    }

    // ------------------------------------------------------------------------
    // Array Expression Conversion
    // ------------------------------------------------------------------------

    /// Convert InitableFromArrayLiteral with simple type annotation: var arr IntArray = [1, 2, 3]
    fn convertInitableFromArrayLiteralSimple(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
        try self.convertInitableFromArrayLiteralImpl(decl, type_name);
    }

    /// Convert InitableFromArrayLiteral: var arr Array of int = [1, 2, 3]
    /// Creates a __ManagedArray and calls Type$init(managed)
    fn convertInitableFromArrayLiteral(self: *AstToIr, decl: ast.VarDecl, gen: ast.GenericTypeExpr) !void {
        try self.convertInitableFromArrayLiteralImpl(decl, gen.base_type);
    }

    /// Implementation of InitableFromArrayLiteral transformation
    fn convertInitableFromArrayLiteralImpl(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
        const arr_lit = decl.value.array_literal;
        const elements = arr_lit.elements;

        debug.astToIr("InitableFromArrayLiteral: {s} with {d} elements", .{ type_name, elements.len });

        // Allocate __ManagedArray on stack (24 bytes: ptr + len + capacity)
        const managed_ptr = try self.func().emitAllocaSized(24);

        if (elements.len == 0) {
            // Empty array: buffer=null, len=0, capacity=0
            const null_ptr = try self.func().emitConstI64(0);
            const buffer_slot = try self.func().emitGetElemPtr(managed_ptr, null_ptr, 0);
            try self.func().emitStore(buffer_slot, null_ptr);

            const len_offset = try self.func().emitConstI64(1);
            const len_slot = try self.func().emitGetElemPtr(managed_ptr, len_offset, 8);
            try self.func().emitStore(len_slot, null_ptr);

            const cap_offset = try self.func().emitConstI64(2);
            const cap_slot = try self.func().emitGetElemPtr(managed_ptr, cap_offset, 8);
            try self.func().emitStore(cap_slot, null_ptr);
        } else {
            // Allocate buffer for elements (on heap for ownership)
            const elem_count = try self.func().emitConstI64(@intCast(elements.len));
            const elem_size: i64 = 8; // All elements are 8 bytes (i64, f64, or pointer)
            const buffer_size = try self.func().emitConstI64(@intCast(elements.len * @as(usize, @intCast(elem_size))));
            const buffer = try self.func().emitHeapAlloc(buffer_size);

            // Store elements into buffer
            for (elements, 0..) |elem, i| {
                const typed = try self.convertExpression(elem);
                const idx_val = try self.func().emitConstI64(@intCast(i));
                const elem_ptr = try self.func().emitGetElemPtr(buffer, idx_val, @intCast(elem_size));
                try self.func().emitStore(elem_ptr, typed.value);
            }

            // Store buffer pointer into __ManagedArray
            const zero = try self.func().emitConstI64(0);
            const buffer_slot = try self.func().emitGetElemPtr(managed_ptr, zero, 0);
            try self.func().emitStore(buffer_slot, buffer);

            // Store length
            const len_offset = try self.func().emitConstI64(1);
            const len_slot = try self.func().emitGetElemPtr(managed_ptr, len_offset, 8);
            try self.func().emitStore(len_slot, elem_count);

            // Store capacity (same as length for literals)
            const cap_offset = try self.func().emitConstI64(2);
            const cap_slot = try self.func().emitGetElemPtr(managed_ptr, cap_offset, 8);
            try self.func().emitStore(cap_slot, elem_count);
        }

        // Call Type$init(managed) - the static init method
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
        try self.module.trackString(init_func_name);

        // Look up the function and type info
        const func_info = self.func_map.get(init_func_name) orelse {
            self.reportErrorWithDetails(.E003, init_func_name);
            return error.UnknownFunction;
        };
        const type_info = self.type_map.get(type_name) orelse {
            std.debug.print("[AST->IR] InitableFromArrayLiteral: unknown type '{s}'\n", .{type_name});
            return error.UnknownType;
        };

        // Check if return type is a struct (needs sret)
        const uses_sret = type_info == .struct_type;

        if (uses_sret) {
            // Allocate space for returned struct
            const struct_size = type_info.struct_type.size;
            const result_ptr = try self.func().emitAllocaSized(struct_size);

            // Build args: [sret_ptr, managed_ptr]
            var args = try self.func().allocator.alloc(ir.Value, 2);
            args[0] = result_ptr;
            args[1] = managed_ptr;

            // Call init with sret
            _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

            // Store result in var_map
            try self.func().setValueName(result_ptr, decl.name);
            try self.var_map.put(self.allocator, decl.name, VarInfo.init(
                result_ptr,
                .{ .struct_type = type_name },
                self.current_decl_is_mutable,
                false,
            ));
        } else {
            // Non-struct return type (unlikely for InitableFromArrayLiteral)
            var args = try self.func().allocator.alloc(ir.Value, 1);
            args[0] = managed_ptr;
            const result = try self.func().emitCall(init_func_name, args, func_info.return_type);
            const result_ptr = try self.func().emitAlloca(func_info.return_type);
            try self.func().emitStore(result_ptr, result orelse 0);
            try self.func().setValueName(result_ptr, decl.name);
            try self.var_map.put(self.allocator, decl.name, VarInfo.init(
                result_ptr,
                .{ .primitive = func_info.return_type },
                self.current_decl_is_mutable,
                false,
            ));
        }
    }

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

        // Handle __ManagedArray indexing (stdlib only)
        if (base_typed.ty == .struct_type) {
            if (std.mem.eql(u8, base_typed.ty.struct_type, "__ManagedArray")) {
                return self.convertManagedArrayIndex(base_typed.value, idx.index.*);
            }
        }

        const arr_info = switch (base_typed.ty) {
            .array_type => |a| a,
            else => {
                std.debug.print("[AST->IR] convertIndexExpr: expected array type\n", .{});
                self.reportError(.E006);
                return error.UnknownType;
            },
        };

        const idx_typed = try self.convertExpression(idx.index.*);

        // Get the array size for bounds checking
        const arr_size: ir.Value = if (arr_info.size) |size|
            try self.func().emitConstI64(@intCast(size))
        else blk: {
            // Dynamic array - need to get the size from companion variable
            // First, find the array name from the base expression
            const arr_name = switch (idx.base.*) {
                .identifier => |name| name,
                else => {
                    // For non-identifier bases, we can't do bounds checking with dynamic size
                    // Fall back to direct access (unsafe)
                    const elem_ptr = try self.func().emitGetElemPtr(base_typed.value, idx_typed.value, 8);
                    const val = try self.func().emitLoad(elem_ptr, arr_info.element_type);
                    return self.createSomeOptional(val, arr_info.element_type);
                },
            };
            const size_var_name = try std.fmt.allocPrint(self.allocator, "{s}_size", .{arr_name});
            defer self.allocator.free(size_var_name);

            const size_var_entry = self.var_map.getPtr(size_var_name) orelse {
                // No size variable - can't do bounds checking, fall back to direct access
                const elem_ptr = try self.func().emitGetElemPtr(base_typed.value, idx_typed.value, 8);
                const val = try self.func().emitLoad(elem_ptr, arr_info.element_type);
                return self.createSomeOptional(val, arr_info.element_type);
            };
            size_var_entry.used = true;
            break :blk try self.func().emitLoad(size_var_entry.ptr, .i64);
        };

        // Allocate the result optional (16 bytes)
        const opt_ptr = try self.func().emitAllocaSized(16);

        // Bounds check: index < size AND index >= 0
        // Since we use i64, negative check is: index >= 0
        const zero = try self.func().emitConstI64(0);
        const is_non_negative = try self.func().emitBinaryOp(.icmp_ge, idx_typed.value, zero, .i64);
        const is_less_than_size = try self.func().emitBinaryOp(.icmp_lt, idx_typed.value, arr_size, .i64);

        // Combine both conditions with AND
        // Since we don't have a direct AND instruction, use multiplication (both are 0 or 1)
        const in_bounds = try self.func().emitBinaryOp(.mul, is_non_negative, is_less_than_size, .i64);

        // Create blocks for branching
        const in_bounds_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("index_in_bounds");

        const out_of_bounds_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("index_out_of_bounds");

        const merge_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("index_merge");

        // Emit conditional branch in previous block
        const entry_block = &self.func().blocks.items[in_bounds_block_idx - 1];
        try entry_block.instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = in_bounds }, .{ .block_ref = in_bounds_block_idx } },
            .result = out_of_bounds_block_idx,
        });

        // Save merge and out_of_bounds blocks
        const merge_block = self.func().blocks.pop().?;
        const out_of_bounds_block = self.func().blocks.pop().?;

        // In-bounds block: create some(element)
        const one_val = try self.func().emitConstI64(1);
        try self.func().emitStore(opt_ptr, one_val); // tag = 1

        const elem_ptr = try self.func().emitGetElemPtr(base_typed.value, idx_typed.value, 8);
        const val = try self.func().emitLoad(elem_ptr, arr_info.element_type);

        const value_slot = try self.func().emitGetFieldPtr(opt_ptr, 8);
        try self.func().emitStore(value_slot, val);

        try self.func().emitBr(merge_block_idx);

        // Restore out-of-bounds block
        try self.func().blocks.append(self.allocator, out_of_bounds_block);

        // Out-of-bounds block: create nil
        const zero_val = try self.func().emitConstI64(0);
        try self.func().emitStore(opt_ptr, zero_val); // tag = 0

        try self.func().emitBr(merge_block_idx);

        // Restore merge block
        try self.func().blocks.append(self.allocator, merge_block);

        // Return the optional
        return .{
            .value = opt_ptr,
            .ty = .{ .optional_type = .{
                .wrapped = arr_info.element_type,
                .wrapped_struct_type = arr_info.element_struct_type,
            } },
        };
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

    fn convertMethodCall(self: *AstToIr, mcall: ast.MethodCallExpr) ConvertError!void {
        // Check if base is a type name (static method call: TypeName.method())
        if (mcall.base.* == .identifier) {
            const base_name = mcall.base.identifier;
            if (self.type_map.contains(base_name)) {
                _ = try self.emitMethodCall(base_name, mcall.method_name, mcall.args, null);
                return;
            }
        }

        // Convert base and get its type
        const base_typed = try self.convertExpression(mcall.base.*);

        // Check if this is a struct type with methods
        if (base_typed.ty == .struct_type) {
            // Use emitMethodCall which handles sret correctly for struct-returning methods
            _ = try self.emitMethodCall(base_typed.ty.struct_type, mcall.method_name, mcall.args, base_typed.value);
            return;
        }

        debug.astToIr("error: method call on non-struct type", .{});
        return error.SemanticError;
    }

    /// Emit a method call (static or instance)
    /// self_value is null for static methods, otherwise it's the instance pointer
    fn emitMethodCall(self: *AstToIr, type_name: []const u8, method_name: []const u8, arg_exprs: []const ast.Expression, self_value: ?ir.Value) ConvertError!TypedValue {
        const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, method_name });
        try self.module.trackString(mangled_name);

        const func_info = self.func_map.get(mangled_name) orelse {
            debug.astToIr("error: unknown method '{s}' on type '{s}'", .{ method_name, type_name });
            return error.SemanticError;
        };

        // Check if return type is optional (needs sret for 16-byte struct)
        const returns_optional = if (func_info.return_value_type) |vt|
            vt == .optional_type
        else
            false;

        // Determine argument layout
        const returns_struct = func_info.return_type_name != null or returns_optional;
        const has_self = self_value != null;
        const sret_offset: usize = if (returns_struct) 1 else 0;
        const self_offset: usize = if (has_self) 1 else 0;
        const num_args = arg_exprs.len + sret_offset + self_offset;

        const args = try self.allocator.alloc(ir.Value, num_args);
        var arg_idx: usize = 0;

        // Sret buffer as first arg if returning struct or optional
        var sret_buffer: ?ir.Value = null;
        if (returns_struct) {
            if (returns_optional) {
                // Optional is 16 bytes (tag + value)
                sret_buffer = try self.func().emitAllocaSized(16);
            } else {
                const struct_info = try self.lookupStructInfo(func_info.return_type_name.?);
                sret_buffer = try self.func().emitAllocaSized(struct_info.size);
            }
            args[arg_idx] = sret_buffer.?;
            arg_idx += 1;
        }

        // Self pointer for instance methods
        if (self_value) |sv| {
            args[arg_idx] = sv;
            arg_idx += 1;
        }

        // Explicit arguments
        for (arg_exprs) |arg_expr| {
            const arg = try self.convertExpression(arg_expr);
            args[arg_idx] = arg.value;
            arg_idx += 1;
        }

        const result = try self.func().emitCall(mangled_name, args, func_info.return_type);

        // Return appropriate value
        if (sret_buffer) |buf| {
            // For optionals, return the buffer with optional type
            if (returns_optional) {
                return .{ .value = buf, .ty = func_info.return_value_type.? };
            }
            // For structs, return the buffer with struct type
            return .{ .value = buf, .ty = .{ .struct_type = func_info.return_type_name.? } };
        }
        const ret_ty: ValueType = if (func_info.return_value_type) |vt|
            vt
        else if (func_info.return_type_name) |name|
            .{ .struct_type = name }
        else
            .{ .primitive = func_info.return_type };

        return .{ .value = result orelse try self.func().emitConstI64(0), .ty = ret_ty };
    }

    /// Call a method on an already-converted TypedValue (used for for-in loop desugaring)
    fn convertMethodCallOnTyped(self: *AstToIr, base_typed: TypedValue, method_name: []const u8, arg_exprs: []const ast.Expression) ConvertError!TypedValue {
        // Get the type name from the TypedValue
        const type_name = switch (base_typed.ty) {
            .struct_type => |name| name,
            else => {
                debug.astToIr("error: method call on non-struct type", .{});
                return error.SemanticError;
            },
        };

        return self.emitMethodCall(type_name, method_name, arg_exprs, base_typed.value);
    }

    fn convertMethodCallExpr(self: *AstToIr, mcall: ast.MethodCallExpr) ConvertError!TypedValue {
        // Check if base is a type name (static method call: TypeName.method())
        if (mcall.base.* == .identifier) {
            const base_name = mcall.base.identifier;
            if (self.type_map.contains(base_name)) {
                return self.emitMethodCall(base_name, mcall.method_name, mcall.args, null);
            }
        }

        // Convert base and get its type (instance method call)
        const base_typed = try self.convertExpression(mcall.base.*);

        // Check if this is a struct type with methods
        if (base_typed.ty == .struct_type) {
            return self.emitMethodCall(base_typed.ty.struct_type, mcall.method_name, mcall.args, base_typed.value);
        }

        debug.astToIr("error: method call on non-struct type", .{});
        return error.SemanticError;
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

pub fn convertWithFile(program: ast.Program, allocator: std.mem.Allocator, mutation_analyzer: ?*const mutation_analysis.MutationAnalyzer, source_file: ?[]const u8, out_error: *?err.CompileError) ConvertError!ir.Module {
    return convertWithExternals(program, allocator, mutation_analyzer, source_file, null, out_error);
}

/// Convert AST to IR with external function signatures from other modules
pub fn convertWithExternals(
    program: ast.Program,
    allocator: std.mem.Allocator,
    mutation_analyzer: ?*const mutation_analysis.MutationAnalyzer,
    source_file: ?[]const u8,
    external_funcs: ?[]const ExternalFuncSignature,
    external_types: ?[]const ExternalTypeInfo,
    out_error: *?err.CompileError,
) ConvertError!ir.Module {
    var converter = AstToIr.init(allocator, mutation_analyzer);
    converter.source_file = source_file;
    defer converter.deinit();

    // Register external types before conversion
    if (external_types) |types| {
        for (types) |ext_type| {
            try converter.registerExternalType(ext_type);
        }
    }

    // Register external function signatures before conversion
    if (external_funcs) |funcs| {
        for (funcs) |ext_func| {
            // Skip if already registered (avoid duplicates from multiple source files)
            if (converter.func_map.contains(ext_func.name)) continue;

            try converter.func_map.put(allocator, ext_func.name, .{
                .return_type = ext_func.return_type,
                .return_type_name = ext_func.return_type_name,
                .return_value_type = ext_func.return_value_type,
                .param_types = &.{},
            });
            debug.astToIr("Registered external function '{s}' returning {s}", .{ ext_func.name, ext_func.return_type.format() });
        }
    }

    const result = converter.convert(program) catch |e| {
        out_error.* = converter.last_error;
        return e;
    };
    return result;
}

/// Extract function signatures from an IR module (for cross-module compilation)
pub fn extractFunctionSignatures(module: *const ir.Module, allocator: std.mem.Allocator) ![]ExternalFuncSignature {
    var signatures = std.ArrayListUnmanaged(ExternalFuncSignature){};
    errdefer signatures.deinit(allocator);

    for (module.functions.items) |func| {
        try signatures.append(allocator, .{
            .name = func.name,
            .return_type = func.return_type,
        });
    }

    return signatures.toOwnedSlice(allocator);
}

/// Extract type info from a parsed program (for cross-module compilation)
pub fn extractTypeInfo(program: ast.Program, allocator: std.mem.Allocator) ![]ExternalTypeInfo {
    var types = std.ArrayListUnmanaged(ExternalTypeInfo){};
    errdefer types.deinit(allocator);

    for (program.types) |*type_decl| {
        var fields = std.ArrayListUnmanaged(ExternalFieldInfo){};
        errdefer fields.deinit(allocator);

        // Calculate fields and offsets
        var offset: i32 = 0;
        for (type_decl.fields) |field| {
            const type_name: []const u8 = switch (field.type_expr) {
                .simple => |name| name,
                .array => "ptr", // Arrays stored as pointers
                .generic => |gen| gen.base_type, // Use base type for generics
                .optional => "ptr", // Optionals stored as pointers
            };

            // Most types are 8 bytes (i64, f64, ptr)
            // __ManagedArray is a special compiler-internal type that is 24 bytes
            const field_size: i32 = if (std.mem.eql(u8, type_name, "__ManagedArray")) 24 else 8;

            try fields.append(allocator, .{
                .name = field.name,
                .offset = offset,
                .size = field_size,
                .type_name = type_name,
            });

            offset += field_size;
        }

        // Include type declaration for generic types (needed for monomorphization)
        const decl_ptr: ?*const ast.TypeDecl = if (type_decl.generic_params.len > 0) type_decl else null;

        try types.append(allocator, .{
            .name = type_decl.name,
            .size = offset,
            .fields = try fields.toOwnedSlice(allocator),
            .type_decl = decl_ptr,
        });
    }

    return types.toOwnedSlice(allocator);
}

/// Extract function signatures from a parsed program (for cross-module compilation)
/// This includes methods from type declarations
pub fn extractFunctionSignaturesFromAst(program: ast.Program, allocator: std.mem.Allocator) ![]ExternalFuncSignature {
    var signatures = std.ArrayListUnmanaged(ExternalFuncSignature){};
    errdefer signatures.deinit(allocator);

    // Extract standalone functions
    for (program.functions) |func| {
        const return_type: ir.Type = if (func.return_type) |rt|
            getIrTypeFromTypeExpr(rt)
        else
            .void;

        const return_type_name: ?[]const u8 = if (func.return_type) |rt|
            getStructNameFromTypeExpr(rt)
        else
            null;

        const return_value_type: ?ValueType = if (func.return_type) |rt|
            getValueTypeFromTypeExpr(rt)
        else
            null;

        // Allocate copy of name for uniform ownership (all names are owned)
        const name_copy = try allocator.dupe(u8, func.name);

        try signatures.append(allocator, .{
            .name = name_copy,
            .return_type = return_type,
            .return_type_name = return_type_name,
            .return_value_type = return_value_type,
        });
    }

    // Extract methods from types
    for (program.types) |type_decl| {
        for (type_decl.methods) |method| {
            const mangled_name = try std.fmt.allocPrint(allocator, "{s}${s}", .{ type_decl.name, method.name });

            const return_type: ir.Type = if (method.return_type) |rt|
                getIrTypeFromTypeExpr(rt)
            else
                .void;

            // For methods returning the containing type (like init() -> Self)
            var return_type_name: ?[]const u8 = if (method.return_type) |rt|
                getStructNameFromTypeExpr(rt)
            else
                null;

            // Handle "Self" return type
            if (return_type_name) |name| {
                if (std.mem.eql(u8, name, "Self")) {
                    return_type_name = type_decl.name;
                }
            }

            const return_value_type: ?ValueType = if (method.return_type) |rt|
                getValueTypeFromTypeExpr(rt)
            else
                null;

            try signatures.append(allocator, .{
                .name = mangled_name,
                .return_type = return_type,
                .return_type_name = return_type_name,
                .return_value_type = return_value_type,
            });
        }
    }

    return signatures.toOwnedSlice(allocator);
}

/// Helper: get IR type from TypeExpr (simplified)
fn getIrTypeFromTypeExpr(te: ast.TypeExpr) ir.Type {
    return switch (te) {
        .simple => |name| {
            if (std.mem.eql(u8, name, "int")) return .i64;
            if (std.mem.eql(u8, name, "float")) return .f64;
            if (std.mem.eql(u8, name, "bool")) return .i64;
            if (std.mem.eql(u8, name, "string")) return .ptr;
            if (std.mem.eql(u8, name, "character")) return .i64;
            if (std.mem.eql(u8, name, "byte")) return .i64;
            // Struct types return ptr
            return .ptr;
        },
        .array => .ptr,
        .generic => .ptr,
        .optional => .ptr,
    };
}

/// Helper: get ValueType from TypeExpr (for optional/array types)
fn getValueTypeFromTypeExpr(te: ast.TypeExpr) ?ValueType {
    return switch (te) {
        .optional => |wrapped| {
            const wrapped_ir_type = getIrTypeFromTypeExpr(wrapped.*);
            return .{
                .optional_type = .{
                    .wrapped = wrapped_ir_type,
                },
            };
        },
        else => null,
    };
}

/// Helper: get struct name from TypeExpr if it's a struct type
fn getStructNameFromTypeExpr(te: ast.TypeExpr) ?[]const u8 {
    return switch (te) {
        .simple => |name| {
            // Known primitives don't have struct names
            if (std.mem.eql(u8, name, "int") or
                std.mem.eql(u8, name, "float") or
                std.mem.eql(u8, name, "bool") or
                std.mem.eql(u8, name, "string") or
                std.mem.eql(u8, name, "character") or
                std.mem.eql(u8, name, "byte"))
            {
                return null;
            }
            return name;
        },
        .generic => |gen| gen.base_type,
        .array, .optional => null,
    };
}
