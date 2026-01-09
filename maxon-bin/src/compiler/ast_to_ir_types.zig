const std = @import("std");
const ir = @import("ir.zig");
const ast = @import("ast.zig");

// ============================================================================
// Primitive Type Metadata (Single Source of Truth)
// ============================================================================

/// Complete metadata for a Maxon primitive type
pub const PrimitiveTypeInfo = struct {
    /// Maxon language name ("int", "float", "bool", "byte", "void", "ptr")
    maxon_name: []const u8,
    /// Corresponding IR type
    ir_type: ir.Type,
    /// Size in bytes for stack/memory allocation
    size: i32,
    /// Whether this type supports arithmetic operations
    is_numeric: bool,
    /// Whether this type is integral (int, bool, byte)
    is_integral: bool,
    /// Whether this type is floating-point (float)
    is_floating_point: bool,
    /// Whether values of this type are signed
    is_signed: bool,
};

/// Compile-time lookup table for user-facing Maxon primitive types
/// Note: ptr and void are intentionally excluded - they are internal types
pub const primitive_types = [_]PrimitiveTypeInfo{
    .{ .maxon_name = "int", .ir_type = .i64, .size = 8, .is_numeric = true, .is_integral = true, .is_floating_point = false, .is_signed = true },
    .{ .maxon_name = "float", .ir_type = .f64, .size = 8, .is_numeric = true, .is_integral = false, .is_floating_point = true, .is_signed = true },
    .{ .maxon_name = "bool", .ir_type = .i64, .size = 8, .is_numeric = false, .is_integral = true, .is_floating_point = false, .is_signed = false },
    .{ .maxon_name = "byte", .ir_type = .i64, .size = 8, .is_numeric = true, .is_integral = true, .is_floating_point = false, .is_signed = false },
};

/// Canonical primitive type name constants (reference into primitive_types for consistency)
pub const INT: []const u8 = primitive_types[0].maxon_name;
pub const FLOAT: []const u8 = primitive_types[1].maxon_name;
pub const BOOL: []const u8 = primitive_types[2].maxon_name;
pub const BYTE: []const u8 = primitive_types[3].maxon_name;

/// Internal type names - NOT user-facing, used only by the compiler
pub const VOID: []const u8 = "void";
pub const PTR: []const u8 = "ptr";

/// Look up primitive type info by Maxon name
/// Returns null if not a primitive type
pub fn getPrimitiveTypeInfo(name: []const u8) ?PrimitiveTypeInfo {
    for (primitive_types) |info| {
        if (std.mem.eql(u8, info.maxon_name, name)) {
            return info;
        }
    }
    return null;
}

// ============================================================================
// Type Definitions for AST to IR Conversion
// ============================================================================

/// Typed value - tracks IR value with its type
pub const TypedValue = struct {
    value: ir.Value,
    ty: ValueType,

    // ========================================================================
    // Typed Pointer Conversion Helpers
    // ========================================================================

    /// Convert to generic struct pointer
    pub fn asStructPtr(self: TypedValue) ir.StructPtr {
        return .{ .val = self.value };
    }

    /// Convert to raw pointer for buffer operations
    pub fn asRawPtr(self: TypedValue) ir.RawPtr {
        return .{ .val = self.value };
    }

    /// Convert to StringPtr (use when you know the type is String)
    pub fn asStringPtr(self: TypedValue) ir.StringPtr {
        return .{ .val = self.value };
    }

    /// Convert to ManagedArrayPtr (use when you know the type is an array)
    pub fn asManagedArrayPtr(self: TypedValue) ir.ManagedArrayPtr {
        return .{ .val = self.value };
    }

    /// Convert to OptionalPtr (use when you know the type is optional)
    pub fn asOptionalPtr(self: TypedValue) ir.OptionalPtr {
        return .{ .val = self.value };
    }

    /// Convert to MapPtr (use when you know the type is a Map)
    pub fn asMapPtr(self: TypedValue) ir.MapPtr {
        return .{ .val = self.value };
    }

    /// Convert to ErrorUnionPtr (use when you know the type is an error union)
    pub fn asErrorUnionPtr(self: TypedValue) ir.ErrorUnionPtr {
        return .{ .val = self.value };
    }

    /// Convert to FuncPtr (use when you know the type is a function pointer)
    pub fn asFuncPtr(self: TypedValue) ir.FuncPtr {
        return .{ .val = self.value };
    }
};

/// Array storage kind
pub const ArrayStorage = enum {
    stack,
    heap,
};

/// Array type info
pub const ArrayInfo = struct {
    element_type: ir.Type,
    size: ?usize, // null for dynamic size
    storage: ArrayStorage,
    element_struct_type: ?[]const u8 = null, // struct name if elements are structs
};

/// Optional type info
pub const OptionalInfo = struct {
    wrapped: ir.Type, // The underlying type (i64, f64, ptr)
    wrapped_struct_type: ?[]const u8 = null, // struct name if wrapped is a struct
    wrapped_enum_type: ?[]const u8 = null, // enum name if wrapped is an enum
};

/// Function type info - for first-class functions
pub const FunctionTypeInfo = struct {
    param_types: []const ValueType, // Parameter types
    return_type: ?*const ValueType, // null for void
    return_ir_type: ir.Type, // IR type for return value
};

/// Error union type info - for T or E where E conforms to Error
pub const ErrorUnionInfo = struct {
    success_type: ir.Type, // The success type's IR type
    success_struct_type: ?[]const u8, // struct name if success is a struct
    error_enum_type: []const u8, // The error enum type name (must be an enum conforming to Error)
};

/// Check if a type name is a known primitive type
pub fn isPrimitiveTypeName(name: []const u8) bool {
    return getPrimitiveTypeInfo(name) != null;
}

/// Maps a Maxon type name to its IR type representation
pub fn nameToIrType(name: []const u8) ir.Type {
    if (getPrimitiveTypeInfo(name)) |info| {
        return info.ir_type;
    }
    // Handle internal types not in primitive_types
    if (std.mem.eql(u8, name, VOID)) return .void;
    // All other types (structs, __ManagedString, etc.) are represented as pointers
    return .ptr;
}

/// Maps an IR type back to the canonical Maxon type name (for numeric types)
pub fn irTypeToName(ir_ty: ir.Type) []const u8 {
    return switch (ir_ty) {
        .f64 => FLOAT,
        .i64 => INT,
        else => unreachable,
    };
}

/// Extended type info for variable tracking
pub const ValueType = union(enum) {
    primitive: []const u8,
    struct_type: []const u8,
    array_type: ArrayInfo,
    enum_type: []const u8,
    optional_type: OptionalInfo,
    error_union_type: ErrorUnionInfo, // T or E where E conforms to Error
    function_type: FunctionTypeInfo, // First-class function types

    pub fn toPrimitiveType(self: ValueType) ir.Type {
        return switch (self) {
            .primitive => |name| nameToIrType(name),
            .enum_type => .i64,
            .struct_type, .array_type => .ptr,
            .optional_type => .ptr, // Optionals are pointers to 16-byte structures
            .error_union_type => .ptr, // Error unions are pointers to discriminated union structures
            .function_type => .ptr, // Function pointers are always pointers
        };
    }

    pub fn isStruct(self: ValueType) bool {
        return self == .struct_type;
    }

    pub fn isOptional(self: ValueType) bool {
        return self == .optional_type;
    }

    /// Returns true if this is a floating-point primitive type
    pub fn isFloatingPoint(self: ValueType) bool {
        return switch (self) {
            .primitive => |name| if (getPrimitiveTypeInfo(name)) |info| info.is_floating_point else false,
            else => false,
        };
    }

    /// Returns true if this is an integral primitive type (int, bool, byte)
    pub fn isIntegral(self: ValueType) bool {
        return switch (self) {
            .primitive => |name| if (getPrimitiveTypeInfo(name)) |info| info.is_integral else false,
            else => false,
        };
    }

    /// Get the canonical type name for this ValueType
    /// Returns the primitive name ("int", "float", etc.) or struct type name
    pub fn getTypeName(self: ValueType) ?[]const u8 {
        return switch (self) {
            .primitive => |name| name,
            .struct_type => |name| name,
            .enum_type => |name| name,
            .array_type, .optional_type, .error_union_type, .function_type => null,
        };
    }

    pub fn isFunctionType(self: ValueType) bool {
        return self == .function_type;
    }
};

/// Free any heap allocations inside a ValueType (for function types with nested allocations)
pub fn freeValueTypeAllocations(allocator: std.mem.Allocator, vt: ValueType) void {
    switch (vt) {
        .function_type => |ft| {
            // Recursively free nested function type allocations in param_types
            for (ft.param_types) |param_vt| {
                freeValueTypeAllocations(allocator, param_vt);
            }
            if (ft.param_types.len > 0) {
                allocator.free(ft.param_types);
            }
            // Free return type pointer
            if (ft.return_type) |rt| {
                freeValueTypeAllocations(allocator, rt.*);
                allocator.destroy(@constCast(rt));
            }
        },
        .struct_type => |name| {
            // Free allocated monomorphized type names (contain '$')
            if (std.mem.indexOf(u8, name, "$")) |_| {
                allocator.free(name);
            }
        },
        // Other variants don't have heap allocations
        .primitive, .enum_type, .array_type, .optional_type, .error_union_type => {},
    }
}

/// Struct field info
pub const FieldInfo = struct {
    name: []const u8,
    offset: i32,
    size: i32,
    value_type: ValueType,
    display_name: ?[]const u8 = null, // Human-readable type name (e.g., "Array of Point")
    is_mutable: bool = true,
    is_export: bool = false, // Whether field is accessible outside the type

    pub fn irType(self: FieldInfo) ir.Type {
        return self.value_type.toPrimitiveType();
    }

    pub fn isStruct(self: FieldInfo) bool {
        return self.value_type.isStruct();
    }

    pub fn structName(self: FieldInfo) ?[]const u8 {
        return switch (self.value_type) {
            .struct_type => |name| name,
            else => null,
        };
    }
};

/// Struct type info
pub const StructTypeInfo = struct {
    name: []const u8,
    fields: []const FieldInfo,
    size: i32,
    decl_line: u32 = 0,
    decl_column: u32 = 0,
    source_file: ?[]const u8 = null,
    is_export: bool = true, // false for private types
};

/// Associated value info for enum cases
pub const AssociatedValueInfo = struct {
    name: []const u8,
    type_name: []const u8,
    ir_type: ir.Type,
};

/// Enum case info - includes associated values if any
pub const EnumCaseInfo = struct {
    tag: i64, // Ordinal/tag value
    associated_values: []const AssociatedValueInfo, // Empty for simple cases
};

/// Enum type info - maps member names to their integer values
/// For string-backed enums, also stores the backing string values
pub const EnumTypeInfo = struct {
    name: []const u8,
    members: std.StringHashMapUnmanaged(i64),
    /// Extended case info including associated values
    case_info: std.StringHashMapUnmanaged(EnumCaseInfo) = .{},
    backing_type: BackingType = .int,
    /// For string-backed enums: maps ordinal to string value
    string_values: std.AutoHashMapUnmanaged(i64, []const u8) = .{},
    /// True if this enum conforms to the Error interface
    is_error: bool = false,
    /// True if any case has associated values
    has_associated_values: bool = false,
    /// Maximum payload size for associated values (used for memory layout)
    max_payload_size: i32 = 0,
    /// True if enum was declared with explicit backing type (e.g., "enum Status int")
    has_explicit_backing_type: bool = false,
    decl_line: u32 = 0,
    decl_column: u32 = 0,
    source_file: ?[]const u8 = null,
    is_export: bool = true, // false for private enums

    pub const BackingType = enum { int, string };
};

/// Type info - primitives, structs, or enums
pub const TypeInfo = union(enum) {
    primitive: ir.Type,
    struct_type: StructTypeInfo,
    enum_type: EnumTypeInfo,

    pub fn irType(self: TypeInfo) ir.Type {
        return switch (self) {
            .primitive => |t| t,
            .struct_type => .ptr,
            .enum_type => .i64,
        };
    }

    pub fn isStruct(self: TypeInfo) bool {
        return self == .struct_type;
    }

    pub fn isEnum(self: TypeInfo) bool {
        return self == .enum_type;
    }
};

/// Function signature info
pub const FuncInfo = struct {
    return_type: ir.Type,
    return_type_name: ?[]const u8,
    return_value_type: ?ValueType, // Full type info for arrays
    param_types: []const ParamType,
    doc_comment: ?[]const u8 = null, // Doc comment (/// or /** */) from source
    ir_generated: bool = true, // false for pending lazy-generated methods
    decl_line: u32 = 0,
    decl_column: u32 = 0,
};

/// Pending method info for lazy generation of monomorphized type methods
pub const PendingMethod = struct {
    type_name: []const u8, // "Array$int" - the monomorphized type
    method: *const ast.MethodDecl, // Pointer to method AST
    generic_bindings: []const GenericBinding, // ["Element" -> "int"]

    pub const GenericBinding = struct {
        param: []const u8,
        concrete: []const u8,
    };
};

/// External function signature - for cross-module compilation
pub const ExternalFuncSignature = struct {
    name: []const u8,
    return_type: ir.Type,
    return_type_name: ?[]const u8 = null, // struct type name if returning a struct
    return_value_type: ?ValueType = null, // Full return type info (for optionals, etc.)
    is_exported: bool = false, // Whether this function is exported from its module
    source_path: ?[]const u8 = null, // Source file path (to distinguish stdlib vs user)
    param_types: []const ParamType = &.{}, // Parameter types for type checking
    doc_comment: ?[]const u8 = null, // Doc comment (/// or /** */) from source
};

/// External type info - for cross-module compilation
pub const ExternalTypeInfo = struct {
    name: []const u8,
    size: i32,
    fields: []const ExternalFieldInfo,
    /// Original type declaration for generic types (needed for monomorphization)
    type_decl: ?*const ast.TypeDecl = null,
    is_exported: bool = false, // Whether this type is exported from its module
    source_path: ?[]const u8 = null, // Source file path (to distinguish stdlib vs user)
};

/// External field info - for cross-module compilation
pub const ExternalFieldInfo = struct {
    name: []const u8,
    offset: i32,
    size: i32,
    type_name: []const u8, // "int", "float", "bool", "ptr", or struct name
};

/// External interface info - for cross-module compilation
pub const ExternalInterfaceInfo = struct {
    /// Original interface declaration (needed for method signatures)
    interface_decl: *const ast.InterfaceDecl,
};

/// External enum info - for cross-module compilation
pub const ExternalEnumInfo = struct {
    /// Original enum declaration (needed for validation)
    enum_decl: *const ast.EnumDecl,
    source_path: ?[]const u8 = null, // Source file path (for error reporting)
};

/// Parameter type info
pub const ParamType = struct {
    ty: ValueType,
    name: []const u8 = "", // Parameter name for named argument resolution
    display_name: ?[]const u8 = null, // Human-readable type name (e.g., "Array of Point")
    default_value: ?*const ast.Expression = null, // Default value expression
};

/// Ownership state of a variable
pub const OwnershipState = enum {
    owned,
    moved,
};

/// Borrow state of a variable (for string borrow checking)
pub const BorrowState = enum {
    none, // Not borrowed
    borrowed, // Has active borrows from it
    slice, // Is itself a slice/borrow from another variable
};

/// Variable info - tracks allocation, type, and ownership
pub const VarInfo = struct {
    /// IR value pointer (null in type-only mode when skip_ir is true)
    ptr: ?ir.Value,
    ty: ValueType,
    used: bool,
    is_mutable: bool,
    state: OwnershipState,
    moved_to: ?[]const u8,
    moved_line: usize,
    /// If true, ptr is a slot containing a pointer that must be loaded
    uses_slot: bool,
    /// If true, this is a function parameter (caller owns memory, don't free)
    is_parameter: bool,
    /// For slices: name of parent variable this was borrowed from
    borrowed_from: ?[]const u8 = null,
    /// Current borrow state
    borrow_state: BorrowState = .none,

    pub fn init(ptr: ?ir.Value, ty: ValueType, is_mutable: bool, uses_slot: bool) VarInfo {
        return .{
            .ptr = ptr,
            .ty = ty,
            .used = false,
            .is_mutable = is_mutable,
            .state = .owned,
            .moved_to = null,
            .moved_line = 0,
            .uses_slot = uses_slot,
            .is_parameter = false,
        };
    }

    pub fn initParam(ptr: ?ir.Value, ty: ValueType, is_mutable: bool, uses_slot: bool) VarInfo {
        return .{
            .ptr = ptr,
            .ty = ty,
            .used = false,
            .is_mutable = is_mutable,
            .state = .owned,
            .moved_to = null,
            .moved_line = 0,
            .uses_slot = uses_slot,
            .is_parameter = true,
        };
    }

    pub fn markMoved(self: *VarInfo, func_name: []const u8, line: usize) void {
        self.state = .moved;
        self.moved_to = func_name;
        self.moved_line = line;
    }

    pub fn resetOwnership(self: *VarInfo) void {
        self.state = .owned;
        self.moved_to = null;
        self.moved_line = 0;
    }
};

pub const ConvertError = error{
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
    UnknownParameter, // Named argument references unknown parameter
    DuplicateArgument, // Same parameter specified multiple times
    MissingArgument, // Required parameter not provided
    MissingReturn, // Non-void function doesn't return on all paths
};

/// Interface info - stores method signatures for conformance checking
pub const InterfaceInfo = struct {
    name: []const u8,
    methods: []const ast.InterfaceMethod,
    associated_types: []const []const u8, // ["Element", "Key", "Value"] from `uses Element, Key, Value`
    extends: []const []const u8, // ["BaseInterface", "OtherInterface"] from `extends BaseInterface, OtherInterface`
    decl_line: u32 = 0,
    decl_column: u32 = 0,
};

/// Method info with source interface - used for transitive interface conformance checking
pub const InterfaceMethodInfo = struct {
    interface_name: []const u8, // The interface that originally defined this method
    method: ast.InterfaceMethod,
};

// ============================================================================
// Semantic Analysis Types (for LSP)
// ============================================================================

/// Variable info for LSP - position-aware, without IR values
pub const SemanticVarInfo = struct {
    name: []const u8,
    ty: ValueType,
    display_name: ?[]const u8, // Human-readable type name from AST (e.g., "Array of Point")
    is_mutable: bool,
    is_parameter: bool,
    borrow_state: BorrowState,
    borrowed_from: ?[]const u8,
    decl_line: u32,
    decl_column: u32,
    function_name: []const u8,
    type_name: ?[]const u8, // If in a method, the type name
};

/// Result of semantic analysis for LSP
pub const SemanticInfo = struct {
    allocator: std.mem.Allocator,
    variables: []const SemanticVarInfo,
    functions: std.StringHashMapUnmanaged(FuncInfo),
    types: std.StringHashMapUnmanaged(TypeInfo),
    interfaces: std.StringHashMapUnmanaged(InterfaceInfo),
    allocated_type_strings: std.ArrayListUnmanaged([]const u8) = .{},
    // Allocated function name strings (for registered external functions)
    allocated_func_names: std.ArrayListUnmanaged([]const u8) = .{},
    // Allocated return_type_name strings (for registered external functions)
    allocated_return_type_names: std.ArrayListUnmanaged([]const u8) = .{},
    // Keep stdlib source strings alive (they're referenced by type/func names)
    stdlib_sources: []const []const u8 = &.{},
    // Keep user source string alive (variable names are slices into this)
    user_source: ?[]const u8 = null,
    // Keep AST alive for LSP features (hover, find references, etc.)
    program: ?ast.Program = null,

    pub fn deinit(self: *SemanticInfo) void {
        self.allocator.free(self.variables);

        // Free AST if present
        if (self.program) |program| {
            ast.freeProgram(program, self.allocator);
        }

        // Free user source string
        if (self.user_source) |src| {
            self.allocator.free(src);
        }

        // Free stdlib source strings
        for (self.stdlib_sources) |src| {
            self.allocator.free(src);
        }
        if (self.stdlib_sources.len > 0) {
            self.allocator.free(self.stdlib_sources);
        }

        // Free allocated type name strings (e.g., "Array$int")
        for (self.allocated_type_strings.items) |str| {
            self.allocator.free(str);
        }
        self.allocated_type_strings.deinit(self.allocator);

        // Free allocated function name strings
        for (self.allocated_func_names.items) |str| {
            self.allocator.free(str);
        }
        self.allocated_func_names.deinit(self.allocator);

        // Free allocated return_type_name strings
        for (self.allocated_return_type_names.items) |str| {
            self.allocator.free(str);
        }
        self.allocated_return_type_names.deinit(self.allocator);

        // Free type_map data (struct fields, enum members)
        var type_iter = self.types.iterator();
        while (type_iter.next()) |entry| {
            switch (entry.value_ptr.*) {
                .struct_type => |s| self.allocator.free(s.fields),
                .enum_type => |*e| {
                    e.members.deinit(self.allocator);
                    // Free associated_values slices inside case_info entries
                    var case_iter = e.case_info.iterator();
                    while (case_iter.next()) |case_entry| {
                        if (case_entry.value_ptr.associated_values.len > 0) {
                            self.allocator.free(case_entry.value_ptr.associated_values);
                        }
                    }
                    e.case_info.deinit(self.allocator);
                    e.string_values.deinit(self.allocator);
                },
                .primitive => {},
            }
        }
        self.types.deinit(self.allocator);

        // Free func_map param_types (avoiding double-free for shared pointers)
        var freed_ptrs = std.AutoHashMapUnmanaged(*const ParamType, void){};
        defer freed_ptrs.deinit(self.allocator);
        var func_iter = self.functions.iterator();
        while (func_iter.next()) |entry| {
            if (entry.value_ptr.param_types.len > 0) {
                const ptr = &entry.value_ptr.param_types[0];
                if (freed_ptrs.get(ptr) == null) {
                    freed_ptrs.put(self.allocator, ptr, {}) catch {};
                    // Free nested function type allocations within param_types
                    for (entry.value_ptr.param_types) |param| {
                        freeValueTypeAllocations(self.allocator, param.ty);
                    }
                    self.allocator.free(entry.value_ptr.param_types);
                }
            }
        }
        self.functions.deinit(self.allocator);

        // Interface map doesn't own its data (references AST nodes)
        self.interfaces.deinit(self.allocator);
    }

    /// Find a variable by name
    pub fn findVariable(self: SemanticInfo, var_name: []const u8) ?SemanticVarInfo {
        for (self.variables) |v| {
            if (std.mem.eql(u8, v.name, var_name)) {
                return v;
            }
        }
        return null;
    }

    /// Get type info by name
    pub fn getType(self: SemanticInfo, type_name: []const u8) ?TypeInfo {
        return self.types.get(type_name);
    }
};
