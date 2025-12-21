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

## Files to Modify

| File | Changes |
|------|---------|
| `1-lexer.zig` | Add keywords: uses, is, with, static, export, self, Self, interface, extends |
| `ast.zig` | Add MethodDecl, InterfaceDecl, InterfaceConformance, extend TypeDecl |
| `2-parser.zig` | parseTypeDecl with methods, parseInterfaceDecl, self/Self handling |
| `4-ast_to_ir.zig` | Method tables, self handling, monomorphization, stdlib restriction |

---

## Implementation Order

1. **Phase 1+2**: Type methods with self/Self (test with non-generic types)
2. **Phase 4**: Method call resolution (test basic methods)
3. **Phase 5**: Monomorphization (test generic Array)
4. **Phase 3**: Interface parsing
5. **Phase 6**: Stdlib-only builtins
6. **Phase 7**: Interface conformance checking

---

## Test Cases

### Phase 1-2: Basic Type with Methods
```maxon
type Counter
    var count int
    function increment()
        count = count + 1
    end 'increment'
    function get() returns int
        return count
    end 'get'
end 'Counter'

function main() returns int
    var c = Counter{count: 0}
    c.increment()
    return c.get()
end 'main'
// expects: 1
```

### Phase 5: Generic Array
```maxon
function main() returns int
    var arr = Array of int
    arr.push(42)
    return arr.count()
end 'main'
// expects: 1
```

### Phase 6: Stdlib Restriction
```maxon
// User code should fail:
function main() returns int
    var arr = Array of int
    return __managed_array_len(arr.managed)  // ERROR: stdlib-only builtin
end 'main'
```
