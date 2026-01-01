const std = @import("std");
const ir = @import("ir.zig");
const ast = @import("ast.zig");

// ============================================================================
// Type Definitions for AST to IR Conversion
// ============================================================================

/// Typed value - tracks IR value with its type
pub const TypedValue = struct {
    value: ir.Value,
    ty: ValueType,
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

/// Maps a Maxon type name to its IR type representation
pub fn nameToIrType(name: []const u8) ir.Type {
    if (std.mem.eql(u8, name, "int")) return .i64;
    if (std.mem.eql(u8, name, "float")) return .f64;
    if (std.mem.eql(u8, name, "bool")) return .i64;
    if (std.mem.eql(u8, name, "byte")) return .i64;
    if (std.mem.eql(u8, name, "void")) return .void;
    if (std.mem.eql(u8, name, "ptr") or std.mem.eql(u8, name, "pointer")) return .ptr;
    // All other types (structs, __ManagedString, etc.) are represented as pointers
    return .ptr;
}

/// Extended type info for variable tracking
pub const ValueType = union(enum) {
    primitive: []const u8, // type name: "int", "float", "bool", "byte", "__ManagedString", etc.
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

/// Struct field info
pub const FieldInfo = struct {
    name: []const u8,
    offset: i32,
    size: i32,
    value_type: ValueType,
    is_mutable: bool = true,

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
};

/// Enum type info - maps member names to their integer values
/// For string-backed enums, also stores the backing string values
pub const EnumTypeInfo = struct {
    name: []const u8,
    members: std.StringHashMapUnmanaged(i64),
    backing_type: BackingType = .int,
    /// For string-backed enums: maps ordinal to string value
    string_values: std.AutoHashMapUnmanaged(i64, []const u8) = .{},
    /// True if this enum conforms to the Error interface
    is_error: bool = false,

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
    ir_generated: bool = true, // false for pending lazy-generated methods
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

/// Parameter type info
pub const ParamType = struct {
    ty: ValueType,
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
    ptr: ir.Value,
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

    pub fn init(ptr: ir.Value, ty: ValueType, is_mutable: bool, uses_slot: bool) VarInfo {
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

    pub fn initParam(ptr: ir.Value, ty: ValueType, is_mutable: bool, uses_slot: bool) VarInfo {
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
};

/// Interface info - stores method signatures for conformance checking
pub const InterfaceInfo = struct {
    name: []const u8,
    methods: []const ast.InterfaceMethod,
    associated_types: []const []const u8, // ["Element", "Key", "Value"] from `uses Element, Key, Value`
};
