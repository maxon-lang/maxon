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
    /// Size in bytes for stack/memory allocation (always 8 for register operations)
    size: i32,
    /// Size in bytes when stored in an array (e.g., byte=1, int=8)
    array_element_size: i32,
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
    .{ .maxon_name = "int", .ir_type = .i64, .size = 8, .array_element_size = 8, .is_numeric = true, .is_integral = true, .is_floating_point = false, .is_signed = true },
    .{ .maxon_name = "float", .ir_type = .f64, .size = 8, .array_element_size = 8, .is_numeric = true, .is_integral = false, .is_floating_point = true, .is_signed = true },
    .{ .maxon_name = "bool", .ir_type = .i64, .size = 8, .array_element_size = 8, .is_numeric = false, .is_integral = true, .is_floating_point = false, .is_signed = false },
    .{ .maxon_name = "byte", .ir_type = .i64, .size = 8, .array_element_size = 1, .is_numeric = true, .is_integral = true, .is_floating_point = false, .is_signed = false },
};

/// Canonical primitive type name constants (reference into primitive_types for consistency)
pub const INT: []const u8 = primitive_types[0].maxon_name;
pub const FLOAT: []const u8 = primitive_types[1].maxon_name;
pub const BOOL: []const u8 = primitive_types[2].maxon_name;
pub const BYTE: []const u8 = primitive_types[3].maxon_name;

/// Internal type names - NOT user-facing, used only by the compiler
pub const VOID: []const u8 = "void";
pub const PTR: []const u8 = "ptr";

/// Primitive type enum - type-safe representation of Maxon primitive types.
/// Using an enum instead of a string prevents bugs like `.primitive = "cstring"`
/// when cstring is actually a struct type.
pub const Primitive = enum {
    int,
    float,
    bool,
    byte,
    ptr, // internal type
    void, // internal type

    /// Convert to IR type
    pub fn toIrType(self: Primitive) ir.Type {
        return switch (self) {
            .int, .bool => .i64,
            .float => .f64,
            .byte => .i64, // Stored as i64 in registers, but 1 byte in arrays
            .ptr => .ptr,
            .void => .void,
        };
    }

    /// Get the Maxon language name for this primitive
    pub fn toMaxonName(self: Primitive) []const u8 {
        return switch (self) {
            .int => INT,
            .float => FLOAT,
            .bool => BOOL,
            .byte => BYTE,
            .ptr => PTR,
            .void => VOID,
        };
    }

    /// Parse a string into a Primitive, returns null if not a valid primitive
    pub fn fromString(name: []const u8) ?Primitive {
        if (std.mem.eql(u8, name, "int")) return .int;
        if (std.mem.eql(u8, name, "float")) return .float;
        if (std.mem.eql(u8, name, "bool")) return .bool;
        if (std.mem.eql(u8, name, "byte")) return .byte;
        if (std.mem.eql(u8, name, "ptr")) return .ptr;
        if (std.mem.eql(u8, name, "void")) return .void;
        return null;
    }

    /// Convert from IR type to Primitive (best effort - i64 maps to int, not bool)
    pub fn fromIrType(ir_ty: ir.Type) Primitive {
        return switch (ir_ty) {
            .i64 => .int,
            .f64 => .float,
            .i8 => .byte,
            .i32 => .int,
            .ptr => .ptr,
            .void => .void,
        };
    }

    /// Returns true if this is a floating-point type
    pub fn isFloatingPoint(self: Primitive) bool {
        return self == .float;
    }

    /// Returns true if this is an integral type (int, bool, byte)
    pub fn isIntegral(self: Primitive) bool {
        return switch (self) {
            .int, .bool, .byte => true,
            .float, .ptr, .void => false,
        };
    }

    /// Returns true if this is a numeric type (can do arithmetic)
    pub fn isNumeric(self: Primitive) bool {
        return switch (self) {
            .int, .float, .byte => true,
            .bool, .ptr, .void => false,
        };
    }

    /// Get the element size when stored in an array
    pub fn arrayElementSize(self: Primitive) i32 {
        return switch (self) {
            .byte => 1,
            .int, .float, .bool, .ptr => 8,
            .void => 0,
        };
    }
};

/// Struct type names for backing types
pub const STRING: []const u8 = "String";
pub const CHARACTER: []const u8 = "Character";

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
    element_primitive_type: ?Primitive = null, // primitive type if elements are primitives (e.g., .byte)

    /// Get the element size in bytes for array indexing.
    /// For structs, looks up the size from type_map.
    /// For primitives, uses the correct array element size (e.g., byte=1, int=8).
    /// Returns null if element type cannot be determined - indicates a compiler bug.
    pub fn getElementSize(self: ArrayInfo, type_map: anytype) ?i32 {
        // First check for struct types
        if (self.element_struct_type) |struct_name| {
            if (type_map.get(struct_name)) |type_info| {
                if (type_info == .struct_type) {
                    return type_info.struct_type.size;
                }
            }
        }
        // Then check for primitive types
        if (self.element_primitive_type) |prim| {
            return prim.arrayElementSize();
        }
        // Cannot determine element size - this is a compiler error
        return null;
    }
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
    success_primitive_type: ?Primitive = null, // Primitive type if success is a primitive (preserves byte vs int distinction)
    success_struct_type: ?[]const u8 = null, // struct name if success is a struct
    success_enum_type: ?[]const u8 = null, // enum name if success is an enum
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
    primitive: Primitive,
    struct_type: []const u8,
    array_type: ArrayInfo,
    enum_type: []const u8,
    error_union_type: ErrorUnionInfo, // T or E where E conforms to Error
    function_type: FunctionTypeInfo, // First-class function types

    pub fn toIrType(self: ValueType) ir.Type {
        return switch (self) {
            .primitive => |p| p.toIrType(),
            .enum_type => .i64,
            .struct_type, .array_type => .ptr,
            .error_union_type => .ptr, // Error unions are pointers to discriminated union structures
            .function_type => .ptr, // Function pointers are always pointers
        };
    }

    pub fn isStruct(self: ValueType) bool {
        return self == .struct_type;
    }

    /// Returns true if this is a floating-point primitive type
    pub fn isFloatingPoint(self: ValueType) bool {
        return switch (self) {
            .primitive => |p| p.isFloatingPoint(),
            else => false,
        };
    }

    /// Returns true if this is an integral primitive type (int, bool, byte)
    pub fn isIntegral(self: ValueType) bool {
        return switch (self) {
            .primitive => |p| p.isIntegral(),
            else => false,
        };
    }

    /// Get the canonical type name for this ValueType
    /// Returns the primitive name ("int", "float", etc.) or struct type name
    pub fn getTypeName(self: ValueType) ?[]const u8 {
        return switch (self) {
            .primitive => |p| p.toMaxonName(),
            .struct_type => |name| name,
            .enum_type => |name| name,
            .array_type, .error_union_type, .function_type => null,
        };
    }

    pub fn isFunctionType(self: ValueType) bool {
        return self == .function_type;
    }

    /// Returns true if this type requires slot indirection (ptr -> pointer -> data).
    /// Heap arrays and function types use slots; structs and primitives don't.
    pub fn usesSlot(self: ValueType) bool {
        return switch (self) {
            .array_type => |arr| arr.storage == .heap,
            .function_type => true,
            .primitive, .struct_type, .enum_type, .error_union_type => false,
        };
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
            // Note: Only frees monomorphized names to avoid double-free.
            // Simple struct names are freed by the caller when appropriate.
            if (std.mem.indexOf(u8, name, "$")) |_| {
                allocator.free(name);
            }
        },
        .error_union_type => {
            // Note: success_struct_type is typically a reference to an existing type name,
            // not a separately allocated string, so we don't free it here.
            // The monomorphized type names are freed when struct_type values are processed.
        },
        // Other variants don't have heap allocations
        .primitive, .enum_type, .array_type => {},
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
        return self.value_type.toIrType();
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

/// Backing values for enums - stores the mapping from ordinal to raw value
pub const BackingValues = union(enum) {
    none, // Simple enum (implicit int ordinal)
    int, // Explicit int values (stored in members map)
    float: std.AutoHashMapUnmanaged(i64, f64),
    string: std.AutoHashMapUnmanaged(i64, []const u8),
    character: std.AutoHashMapUnmanaged(i64, []const u8),

    pub fn toValueType(self: BackingValues) ValueType {
        return switch (self) {
            .none, .int => ValueType{ .primitive = .int },
            .float => ValueType{ .primitive = .float },
            .string => ValueType{ .struct_type = STRING },
            .character => ValueType{ .struct_type = CHARACTER },
        };
    }

    pub fn displayName(self: BackingValues) []const u8 {
        return switch (self) {
            .none => "none",
            .int => "int",
            .float => "float",
            .string => "String",
            .character => "character",
        };
    }

    /// Infer backing type from an expression (returns empty maps for semantic analysis)
    pub fn fromExpr(expr: ast.Expression) BackingValues {
        return switch (expr) {
            .integer => .int,
            .float_lit => .{ .float = .{} },
            .string_literal => .{ .string = .{} },
            .char_literal => .{ .character = .{} },
            else => .none,
        };
    }
};

/// Enum type info - maps member names to their integer values
/// For backed enums, also stores the backing values
pub const EnumTypeInfo = struct {
    name: []const u8,
    members: std.StringHashMapUnmanaged(i64),
    /// Extended case info including associated values
    case_info: std.StringHashMapUnmanaged(EnumCaseInfo) = .{},
    /// Backing values for raw values (includes type tag and value map)
    backing: BackingValues = .none,
    /// True if this enum conforms to the Error interface
    is_error: bool = false,
    /// True if any case has associated values
    has_associated_values: bool = false,
    /// Maximum payload size for associated values (used for memory layout)
    max_payload_size: i32 = 0,
    decl_line: u32 = 0,
    decl_column: u32 = 0,
    source_file: ?[]const u8 = null,
    is_export: bool = true, // false for private enums

    /// Returns the ValueType for this enum's rawValue
    pub fn rawValueType(self: EnumTypeInfo) ValueType {
        return self.backing.toValueType();
    }

    /// Returns the array element size for this enum type.
    /// Simple enums: 8 bytes (i64 tag value)
    /// Enums with associated values: 8 bytes (pointer to heap-allocated storage)
    pub fn arrayElementSize(self: EnumTypeInfo) i32 {
        // Both simple enums and enums with associated values use 8 bytes per element:
        // - Simple enums store an i64 tag directly
        // - Enums with associated values store a pointer to heap-allocated [tag][payload...]
        _ = self;
        return 8;
    }
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
    is_external: bool = false, // true for external/stdlib functions (param type names are allocated)
};

/// Pending method info for lazy generation of monomorphized type methods
pub const PendingMethod = struct {
    type_name: []const u8, // "Array$int" - the monomorphized type
    method: *const ast.MethodDecl, // Pointer to method AST
    generic_bindings: []const GenericBinding, // ["Element" -> "int"]
    source_file: ?[]const u8, // Source file path for error reporting

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
    return_value_type: ?ValueType = null, // Full return type info (for error unions, etc.)
    is_exported: bool = false, // Whether this function is exported from its module
    source_path: ?[]const u8 = null, // Source file path (to distinguish stdlib vs user)
    param_types: []const ParamType = &.{}, // Parameter types for type checking
    doc_comment: ?[]const u8 = null, // Doc comment (/// or /** */) from source
};

/// External type info - for cross-module compilation
/// Size is computed during registration when all type info is available.
pub const ExternalTypeInfo = struct {
    name: []const u8,
    /// Original type declaration (needed for computing size and monomorphization)
    type_decl: *const ast.TypeDecl,
    is_exported: bool = false, // Whether this type is exported from its module
    source_path: ?[]const u8 = null, // Source file path (to distinguish stdlib vs user)
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
    /// Storage indirection: true = ptr holds a pointer to the data (needs load before access)
    ///                      false = ptr directly holds the data (access directly)
    /// Examples:
    ///   - Stack-allocated struct via alloca.sized: uses_slot = false (data is at ptr)
    ///   - Heap-allocated array via alloca ptr: uses_slot = true (ptr -> pointer -> data)
    uses_slot: bool,
    /// If true, this is a function parameter (caller owns memory, don't free)
    is_parameter: bool,
    /// For slices: name of parent variable this was borrowed from
    borrowed_from: ?[]const u8 = null,
    /// Current borrow state
    borrow_state: BorrowState = .none,

    /// Create a VarInfo for a stack-allocated value (data stored directly at ptr).
    /// Use this for values created with alloca.sized or stack structs.
    pub fn initStackAllocated(ptr: ?ir.Value, ty: ValueType, is_mutable: bool) VarInfo {
        return .{
            .ptr = ptr,
            .ty = ty,
            .used = false,
            .is_mutable = is_mutable,
            .state = .owned,
            .moved_to = null,
            .moved_line = 0,
            .uses_slot = false,
            .is_parameter = false,
        };
    }

    /// Create a VarInfo for a heap-allocated value (ptr holds a pointer to the data).
    /// Use this for values where ptr points to a slot containing a pointer.
    pub fn initHeapAllocated(ptr: ?ir.Value, ty: ValueType, is_mutable: bool) VarInfo {
        return .{
            .ptr = ptr,
            .ty = ty,
            .used = false,
            .is_mutable = is_mutable,
            .state = .owned,
            .moved_to = null,
            .moved_line = 0,
            .uses_slot = true,
            .is_parameter = false,
        };
    }

    /// Legacy init function - prefer initStackAllocated or initHeapAllocated for clarity.
    /// uses_slot: false = stack-allocated (data at ptr), true = heap-allocated (ptr -> pointer -> data)
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

    /// Create a VarInfo for a function parameter.
    /// uses_slot: false = passed by value on stack, true = passed by pointer
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

    /// Create a VarInfo with uses_slot automatically determined from the type.
    /// Heap arrays, error unions, and function types use a slot; structs and primitives don't.
    pub fn initFromType(ptr: ?ir.Value, ty: ValueType, is_mutable: bool) VarInfo {
        return .{
            .ptr = ptr,
            .ty = ty,
            .used = false,
            .is_mutable = is_mutable,
            .state = .owned,
            .moved_to = null,
            .moved_line = 0,
            .uses_slot = ty.usesSlot(),
            .is_parameter = false,
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
    // Keep parser allocations alive (freed by freeProgram or deinit)
    expr_ptrs: std.ArrayListUnmanaged(*ast.Expression) = .{},

    pub fn deinit(self: *SemanticInfo) void {
        self.allocator.free(self.variables);

        // Free AST if present
        if (self.program) |program| {
            ast.freeProgram(program, self.allocator);
        }

        // Free parser-allocated expression pointers
        for (self.expr_ptrs.items) |ptr| {
            self.allocator.destroy(ptr);
        }
        self.expr_ptrs.deinit(self.allocator);

        // Free type_map data (struct fields, enum members)
        var type_iter = self.types.iterator();
        while (type_iter.next()) |entry| {
            switch (entry.value_ptr.*) {
                .struct_type => |s| {
                    self.allocator.free(s.fields);
                },
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
                    switch (e.backing) {
                        .float => |*m| m.deinit(self.allocator),
                        .string => |*m| m.deinit(self.allocator),
                        .character => |*m| m.deinit(self.allocator),
                        .none, .int => {},
                    }
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

                    // For external functions, free param type allocations including struct names
                    if (entry.value_ptr.is_external) {
                        for (entry.value_ptr.param_types) |param| {
                            // Free allocated struct type names (all of them for external functions)
                            switch (param.ty) {
                                .struct_type => |name| {
                                    self.allocator.free(name);
                                },
                                .error_union_type => |eu| {
                                    // Free success_struct_type if allocated
                                    if (eu.success_struct_type) |name| {
                                        if (std.mem.indexOf(u8, name, "$")) |_| {
                                            self.allocator.free(name);
                                        }
                                    }
                                },
                                .function_type => |ft| {
                                    // Free nested function type allocations
                                    for (ft.param_types) |param_vt| {
                                        freeValueTypeAllocations(self.allocator, param_vt);
                                    }
                                    if (ft.param_types.len > 0) {
                                        self.allocator.free(ft.param_types);
                                    }
                                    if (ft.return_type) |rt| {
                                        freeValueTypeAllocations(self.allocator, rt.*);
                                        self.allocator.destroy(@constCast(rt));
                                    }
                                },
                                else => {},
                            }
                        }
                    } else {
                        // For user functions, only free nested allocations (not struct names)
                        for (entry.value_ptr.param_types) |param| {
                            freeValueTypeAllocations(self.allocator, param.ty);
                        }
                    }
                    self.allocator.free(entry.value_ptr.param_types);
                }
            }
        }
        self.functions.deinit(self.allocator);

        // Interface map doesn't own its data (references AST nodes)
        self.interfaces.deinit(self.allocator);

        // Now safe to free the source strings that type/func names point into

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
