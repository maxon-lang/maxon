# Maxon Compiler: Feature Roadmap for Self-Hosted Compilation

## Goal
Enable the maxon compiler to compile the self-hosted compiler in `maxon-bin-selfhosted/`.

## Self-hosted compiler needs (missing in maxon):**
- Strings (literals, interpolation, methods)
- Maps (`map from K to V`)
- Match expressions
- For-in loops
- Logical operators (`and`, `or`, `not`)
- Bitwise operators (`>>`, `&`, `|`, `<<`)
- Character type
- Byte type
- Static functions on types
- Export/import system
- Standard library (print, file I/O, etc.)

---

## Implementation Phases

### Phase 1: Core Type System Extensions
**Goal:** Add foundational types needed by everything else.

#### 1.1 String Type
- [ ] Add `string` as a built-in type in AST and IR
- [ ] String literals in lexer/parser (double-quoted)
- [ ] String comparison (`==`, `!=`)
- [ ] String concatenation (`+`)
- [ ] Runtime representation (ptr + length, heap-allocated)
- [ ] Files: `1-lexer.zig`, `2-parser.zig`, `4-ast_to_ir.zig`, `ir.zig`, `ir_codegen.zig`

#### 1.2 Character Type
- [ ] Add `character` type (grapheme cluster support can be deferred)
- [ ] Character literals (`'A'`, `'\n'`, etc.) - Note: currently single quotes are used for labels
- [ ] Character comparison
- [ ] Files: Same as strings

#### 1.3 Byte Type
- [ ] Add `byte` type (8-bit unsigned)
- [ ] Byte literals and hex literals (`0xFF`)
- [ ] Files: `ast.zig`, `4-ast_to_ir.zig`, `ir_codegen.zig`

#### 1.4 Bitwise Operators
- [ ] `>>` (right shift), `<<` (left shift)
- [ ] `&` (bitwise AND), `|` (bitwise OR), `^` (XOR)
- [ ] `~` (bitwise NOT)
- [ ] `as` type cast operator (e.g., `x as byte`)
- [ ] Files: `1-lexer.zig`, `2-parser.zig`, `4-ast_to_ir.zig`, `ir.zig`, `ir_codegen.zig`

#### 1.5 Logical Operators
- [ ] `and` (logical AND with short-circuit)
- [ ] `or` (logical OR with short-circuit)
- [ ] `not` (logical NOT)
- [ ] Files: `1-lexer.zig`, `2-parser.zig`, `4-ast_to_ir.zig`

---

### Phase 2: Control Flow Extensions
**Goal:** Add remaining control flow constructs.

#### 2.1 For-In Loops
- [ ] Parse `for item in iterable 'label' ... end 'label'`
- [ ] Desugar to while loop with iterator
- [ ] Support iteration over arrays
- [ ] Files: `2-parser.zig`, `4-ast_to_ir.zig`

#### 2.2 Match Expressions
- [ ] Parse `match expr 'label' ... end 'label'`
- [ ] Parse match arms: `pattern gives value` / `pattern then statement`
- [ ] Support `default` case
- [ ] Pattern types: literals (int, string), enum variants
- [ ] Files: `2-parser.zig`, `ast.zig`, `4-ast_to_ir.zig`

---

### Phase 3: Optional Types ✅ COMPLETE
**Goal:** Add nil-safety and optional type handling.

#### 3.1 Optional Type Syntax ✅
- [x] Parse `T or nil` type expressions
- [x] Internal representation (tagged union: 16-byte structure with tag + value)
- [x] Files: `2-parser.zig`, `ast.zig`, `4-ast_to_ir.zig`

#### 3.2 If-Let Binding ✅
- [x] Parse `if let name = expr 'label' ... end 'label' else 'label2' ... end 'label2'`
- [x] Unwrap optional into binding
- [x] Files: `2-parser.zig`, `4-ast_to_ir.zig`

#### 3.3 Nil Literal ✅
- [x] Add `nil` keyword and literal
- [x] Type checking: `nil` only valid for optional types
- [x] Files: `1-lexer.zig`, `2-parser.zig`, `4-ast_to_ir.zig`

#### 3.4 Additional Features ✅
- [x] Else-unwrap declarations (`var x = expr else 'label' ... end 'label'`)
- [x] Array indexing returns optionals for bounds-safe access

---

### Phase 4: String Methods & Built-ins
**Goal:** String manipulation methods used by lexer/parser.

#### 4.1 Core String Methods
- [ ] `.count()` - string length
- [ ] `.charAt(index)` - character at index (returns `character or nil`)
- [ ] `.slice(start, end)` - substring
- [ ] `.bytes()` - convert to `Array of byte`
- [ ] Files: `4-ast_to_ir.zig`, runtime library

#### 4.2 String Interpolation
- [ ] Parse `"text {expr} more"` syntax
- [ ] Desugar to concatenation of string conversions
- [ ] `.toString()` method on types
- [ ] Files: `1-lexer.zig`, `2-parser.zig`, `4-ast_to_ir.zig`

#### 4.3 Additional String Methods
- [ ] `.replace(old, new)` - string replacement
- [ ] `.contains(substring)` - check substring presence
- [ ] `.startIndex()`, `.endIndex()`, `.indexAfter(index)` - index operations
- [ ] Files: Runtime library or built-in codegen

---

### Phase 5: Collections
**Goal:** Map type for symbol tables, managed arrays for dynamic collections.

#### 5.1 Managed Arrays (Array of T)
- [ ] Parse `Array of T` type syntax (uppercase for stdlib types)
- [ ] Parse `Array of T` empty initialization expression
- [ ] Internal `__ManagedArray` struct (24 bytes: data_ptr, length, capacity)
- [ ] Compiler builtins: `__managed_array_len`, `__managed_array_capacity`, `__managed_array_set_length`
- [ ] Compiler builtins: `__managed_array_grow`, `__managed_array_set_at`
- [ ] Compiler builtins: `__managed_array_shift_left`, `__managed_array_shift_right`
- [ ] `memmove` IR instruction for overlapping memory operations
- [ ] stdlib `Array` type uses builtins for push/pop/insert/remove

**Note:** Static arrays (`Array of N T`, literals `[1, 2, 3]`) and bounds-checked indexing are implemented.

#### 5.2 Map Type
- [ ] Parse `map from K to V` type syntax
- [ ] Runtime representation (hash table)
- [ ] Files: `2-parser.zig`, `ast.zig`, `4-ast_to_ir.zig`

#### 5.3 Map Operations
- [ ] `.insert(key, value)` - add entry
- [ ] `.get(key)` - retrieve (returns `V or nil`)
- [ ] `.contains(key)` - check key existence
- [ ] Files: `4-ast_to_ir.zig`, runtime library

#### 5.4 Array Extensions
- [ ] `.append(array)` - append another array
- [ ] `.slice(start, end)` - array slicing
- [ ] `.prefix(n)` - first n elements
- [ ] Files: `4-ast_to_ir.zig`

---

### Phase 6: Type System Extensions
**Goal:** Static methods and visibility.

#### 6.1 Static Functions
- [ ] Parse `static function` within type declarations
- [ ] Call syntax: `TypeName.staticMethod(args)`
- [ ] No `self` parameter
- [ ] Files: `2-parser.zig`, `ast.zig`, `4-ast_to_ir.zig`

#### 6.2 Export Keyword
- [ ] Parse `export` on functions, types, enums
- [ ] Track visibility in AST
- [ ] (Enforce in multi-file compilation)
- [ ] Files: `2-parser.zig`, `ast.zig`

---

### Phase 7: Module System
**Goal:** Multi-file compilation.

#### 7.1 Import Syntax
- [ ] Parse import statements (determine exact syntax from existing compiler)
- [ ] Module name resolution
- [ ] Files: `1-lexer.zig`, `2-parser.zig`

#### 7.2 Multi-File Compilation
- [ ] Compile multiple files
- [ ] Cross-file symbol resolution
- [ ] Link multiple object files or combined codegen
- [ ] Files: `main.zig`, new module resolver

---

### Phase 8: Standard Library
**Goal:** I/O and process execution.

#### 8.1 Console I/O
- [ ] `print(string)` - output text
- [ ] Files: Runtime library, built-in function registration

#### 8.2 File I/O
- [ ] `readTextFile(path)` - returns `string or nil`
- [ ] `writeTextFile(path, content)` - write text
- [ ] `writeBinaryFile(path, bytes)` - write binary
- [ ] Files: Runtime library

#### 8.3 Process Execution
- [ ] `executeProcess(exePath)` - run subprocess, return exit code
- [ ] Files: Runtime library

#### 8.4 Type Conversions
- [ ] `int.parse(string)` - parse integer from string
- [ ] Files: Runtime or built-in

---

