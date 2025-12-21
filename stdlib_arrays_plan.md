# Plan: Full stdlib Array Type Support in maxon-zig

## Goal
Enable the compiler to compile the stdlib `Array` type from `stdlib/collections/array.maxon`, including:
- Type declarations with methods
- Generic type parameters (`uses Element`) with monomorphization
- Interface conformance with full checking (`is Collection with Element`)
- Method calls on custom types (`arr.push(42)`)
- Stdlib-only builtin restriction

---

## Phase 1: Type Declarations with Methods

### 1.1 AST Changes (`ast.zig`)

Add `MethodDecl` struct:
```zig
pub const MethodDecl = struct {
    name: []const u8,
    qualified_name: ?[]const u8,  // "Collection.count" for interface methods
    is_static: bool,
    is_export: bool,
    params: []const ParamDecl,
    return_type: ?TypeExpr,
    body: []Statement,
};
```

Extend `TypeDecl`:
```zig
pub const TypeDecl = struct {
    name: []const u8,
    is_export: bool,
    generic_params: []const []const u8,     // ["Element"] for `uses Element`
    conformances: []const InterfaceConformance,
    fields: []FieldDecl,
    methods: []MethodDecl,
};

pub const InterfaceConformance = struct {
    interface_name: []const u8,
    type_args: []const []const u8,
};
```

### 1.2 Lexer Changes (`1-lexer.zig`)

Add keywords: `uses`, `is`, `with`, `static`, `export`, `self`, `Self`, `interface`, `extends`

### 1.3 Parser Changes (`2-parser.zig`)

Modify `parseTypeDecl()`:
1. Parse `export? type Name`
2. Parse optional `uses TypeParam, ...`
3. Parse optional `is InterfaceName with TypeArg, ...`
4. Loop until `end`:
   - `var`/`let` → field
   - `export? static? function` → method (qualified name like `Collection.count` allowed)
5. Parse `end 'label'`

---

## Phase 2: `self` and `Self` Keywords

### 2.1 Expression Parsing
- `self` → special identifier referencing current instance
- `Self` → type name of enclosing type

### 2.2 IR Generation (`4-ast_to_ir.zig`)
- Methods get implicit `self` as first parameter
- `self` identifier loads from first parameter
- `Self` type substitutes to enclosing type name
- Field access: `self.field` or just `field` (implicit self)

---

## Phase 3: Interface Parsing

### 3.1 AST Changes
```zig
pub const InterfaceDecl = struct {
    name: []const u8,
    is_export: bool,
    generic_params: []const []const u8,
    extends: []const []const u8,
    required_methods: []InterfaceMethod,
};

pub const InterfaceMethod = struct {
    name: []const u8,
    params: []const ParamDecl,
    return_type: ?TypeExpr,
    has_default_impl: bool,
    default_body: ?[]Statement,
};
```

### 3.2 Parser Changes
Add `parseInterfaceDecl()`:
1. Parse `export? interface Name`
2. Parse optional `uses TypeParam, ...`
3. Parse optional `extends Interface, ...`
4. Parse method signatures (with optional default implementations)
5. Parse `end 'label'`

---

## Phase 4: Method Call Resolution

### 4.1 Type Registration
Extend `StructTypeInfo` with method table:
```zig
const MethodInfo = struct {
    name: []const u8,
    qualified_name: ?[]const u8,
    is_static: bool,
    params: []const ParamType,
    return_type: ir.Type,
    return_value_type: ?ValueType,
};

methods: std.StringHashMapUnmanaged(MethodInfo),
```

### 4.2 Method Code Generation
For each method in type:
1. Generate IR function with mangled name: `TypeName$methodName`
2. First parameter is implicit `self` pointer
3. Register in `func_map`

### 4.3 Method Call Conversion
Modify `convertMethodCallExpr()`:
1. Get base expression type
2. Look up method in type's method table
3. Generate call with self as first argument

---

## Phase 5: Monomorphization

### 5.1 Strategy
Lazy monomorphization during type resolution:
1. When seeing `Array of int`:
   - Check if `Array$int` exists
   - If not, clone `Array` with `Element` → `int`
   - Register as `Array$int`

### 5.2 Substitution Rules
- `Element` in params/returns → concrete type
- `Element or nil` → `int or nil`
- `Self` → `Array$int`
- Method `push(value Element)` → `Array$int$push(self, value int)`

### 5.3 Data Structures
```zig
// In AstToIr:
generic_type_decls: StringHashMap(*TypeDecl),  // "Array" -> original decl
monomorphized_types: StringHashMap(StructTypeInfo),  // "Array$int" -> specialized
```

---

## Phase 6: Stdlib-Only Builtins

In `convertManagedArrayBuiltin()`:
```zig
if (std.mem.startsWith(u8, name, "__managed_array_")) {
    if (!self.isStdlibFile()) {
        return error.StdlibOnlyBuiltin;
    }
}
```

---

## Phase 7: Interface Conformance Checking

After registering a type:
1. For each interface conformance
2. Load interface definition
3. Verify all required methods are implemented
4. Report error if missing

---

### Phase 8: Multi-file Compilation Infrastructure

**Goal**: Compile stdlib before user code, merge IR modules.

**Files**: `0-compiler.zig`

1. Add `compileMultiple(sources: []Source, ...)` function
2. Each source produces an IR module
3. Merge IR modules before codegen
4. Stdlib path resolution (find `stdlib/` relative to executable)

**Key changes**:
```zig
pub fn compileWithStdlib(user_source: []const u8, stdlib_dir: []const u8, ...) !void {
    // 1. Compile stdlib/collections/array.maxon
    const stdlib_ir = try runFrontend(stdlib_source, ...);

    // 2. Compile user code
    const user_ir = try runFrontend(user_source, ...);

    // 3. Merge IR modules
    const merged = try mergeModules(stdlib_ir, user_ir);

    // 4. Codegen
    ...
}
```

### Phase 9: Export Keyword

**Goal**: Only exported symbols visible outside the module.

**Files**: `2-parser.zig`, `ast.zig`, `4-ast_to_ir.zig`

1. Parse `export` keyword on types and functions
2. Add `is_exported: bool` to `TypeDecl`, `FunctionDecl`
3. During IR generation, mark exported symbols
4. When linking, only resolve to exported symbols from other modules


### Phase 10: Static Functions

**Goal**: `static function init(...)` - no implicit self.

**Files**: `2-parser.zig`, `ast.zig`, `4-ast_to_ir.zig`

1. Parse `static` keyword before `function` in type body
2. Add `is_static: bool` to `MethodDecl`
3. Static methods don't get implicit `self` parameter
4. Call syntax: `TypeName.method()` or within type just `method()`

### Phase 11: Interfaces (Basic)

**Goal**: `is Collection with Element` - type conforms to interface.

**Files**: `2-parser.zig`, `ast.zig`, `4-ast_to_ir.zig`

1. Parse interface declarations: `interface Collection uses Element`
2. Parse conformance: `type Array ... is Collection with Element`
3. Method name syntax: `function Collection.count()` means this implements the interface method
4. At compile time, verify all interface methods are implemented

### Phase 12: Internal/Opaque Types

**Goal**: `__ManagedArray` - compiler-known internal type.

**Files**: `2-parser.zig`, `4-ast_to_ir.zig`

1. Types starting with `_` are internal
2. `__ManagedArray` is specially recognized by compiler
3. Maps to the 24-byte managed array struct

### Phase 13: Wire Up Array Type

**Goal**: `Array of T` uses stdlib Array type.

**Files**: `4-ast_to_ir.zig`

1. When seeing `Array of T`, look up `Array` in type_map (from stdlib)
2. Instantiate generic with concrete type T
3. Method calls resolve to stdlib methods
4. Methods call `__managed_array_*` builtins
