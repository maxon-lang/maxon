# Maxon-Zig Compiler: Feature Roadmap for Self-Hosted Compilation

## Goal
Enable the maxon-zig compiler to compile the self-hosted compiler in `maxon-bin-selfhosted/`.

## Current State Summary

**Maxon-zig has:**
- Variables (`let`/`var`), primitives (`int`, `float`, `bool`)
- Structs, enums, arrays (basic)
- Functions with ownership/borrow checking
- If/else, while loops, break/continue
- Arithmetic, comparison operators
- x86-64 codegen, PE writer

**Self-hosted compiler needs (missing in maxon-zig):**
- Strings (literals, interpolation, methods)
- Optional types (`T or nil`, if-let binding)
- Maps (`map from K to V`)
- Match expressions
- For-in loops
- Bitwise operators (`>>`, `&`, `|`)
- Character type
- Static functions on types
- Export/import system
- Standard library (print, file I/O, etc.)

---

## Implementation Phases

### Phase 1: Core Type System Extensions
**Goal:** Add foundational types needed by everything else.

#### 1.1 String Type
- [ ] Add `string` as a built-in type in AST and IR
- [ ] String literals in lexer/parser
- [ ] String comparison (`==`, `!=`)
- [ ] String concatenation (`+`)
- [ ] Runtime representation (ptr + length, heap-allocated)
- [ ] Files: `1-lexer.zig`, `2-parser.zig`, `4-ast_to_ir.zig`, `ir.zig`, `ir_codegen.zig`

#### 1.2 Character Type
- [ ] Add `character` type (grapheme cluster support can be deferred)
- [ ] Character literals (`'A'`, `'\n'`, etc.)
- [ ] Character comparison
- [ ] Files: Same as strings

#### 1.3 Byte Type
- [ ] Add `byte` type (8-bit unsigned)
- [ ] Byte literals and hex literals (`0xFF`)
- [ ] Files: `ast.zig`, `4-ast_to_ir.zig`, `ir_codegen.zig`

#### 1.4 Bitwise Operators
- [ ] `>>` (right shift), `<<` (left shift)
- [ ] `&` (bitwise AND), `|` (bitwise OR)
- [ ] `as` type cast operator (e.g., `x as byte`)
- [ ] Files: `1-lexer.zig`, `2-parser.zig`, `4-ast_to_ir.zig`, `ir.zig`, `ir_codegen.zig`

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

### Phase 3: Optional Types
**Goal:** Add nil-safety and optional type handling.

#### 3.1 Optional Type Syntax
- [ ] Parse `T or nil` type expressions
- [ ] Internal representation (tagged union or nullable pointer)
- [ ] Files: `2-parser.zig`, `ast.zig`, `4-ast_to_ir.zig`

#### 3.2 If-Let Binding
- [ ] Parse `if let name = expr 'label' ... end 'label' else 'label2' ... end 'label2'`
- [ ] Unwrap optional into binding
- [ ] Files: `2-parser.zig`, `4-ast_to_ir.zig`

#### 3.3 Nil Literal
- [ ] Add `nil` keyword and literal
- [ ] Type checking: `nil` only valid for optional types
- [ ] Files: `1-lexer.zig`, `2-parser.zig`, `4-ast_to_ir.zig`

---

### Phase 4: String Methods & Built-ins
**Goal:** String manipulation methods used by lexer/parser.

#### 4.1 Core String Methods
- [ ] `.count()` - string length
- [ ] `.charAt(index)` - character at index (returns `character or nil`)
- [ ] `.slice(start, end)` - substring
- [ ] `.bytes()` - convert to `array of byte`
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
**Goal:** Map type for symbol tables.

#### 5.1 Map Type
- [ ] Parse `map from K to V` type syntax
- [ ] Runtime representation (hash table)
- [ ] Files: `2-parser.zig`, `ast.zig`, `4-ast_to_ir.zig`

#### 5.2 Map Operations
- [ ] `.insert(key, value)` - add entry
- [ ] `.get(key)` - retrieve (returns `V or nil`)
- [ ] `.contains(key)` - check key existence
- [ ] Files: `4-ast_to_ir.zig`, runtime library

#### 5.3 Array Extensions
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

## Estimated Order of Implementation

1. **Phase 1** (Strings, characters, bytes, bitwise) - Foundation
2. **Phase 3.3** (Nil literal) - Quick win
3. **Phase 2.1** (For-in loops) - Common pattern
4. **Phase 2.2** (Match) - Used everywhere
5. **Phase 3** (Optionals complete) - Required for safe APIs
6. **Phase 4** (String methods) - Lexer needs these
7. **Phase 5** (Maps) - Semantic analyzer needs these
8. **Phase 6** (Static functions, export) - Code organization
9. **Phase 7** (Modules) - Multi-file
10. **Phase 8** (Stdlib) - Runtime functionality

---

## Critical Files to Modify

| File | Changes |
|------|---------|
| `src/compiler/1-lexer.zig` | New tokens (string interpolation, `match`, `for`, `as`, bitwise ops) |
| `src/compiler/2-parser.zig` | Parse all new syntax |
| `src/compiler/ast.zig` | New AST nodes for match, for-in, optionals, static functions |
| `src/compiler/4-ast_to_ir.zig` | Semantic analysis & IR gen for all new features |
| `src/compiler/ir.zig` | New IR instructions if needed |
| `src/compiler/ir_codegen.zig` | x86-64 codegen for new IR |
| `src/compiler/main.zig` | Multi-file compilation support |

---

## Implementation Approach

**Runtime Strategy: Inline Codegen**
- All string, map, and I/O operations will be implemented as x86-64 code generation
- No external runtime library dependency
- Self-contained executables
- More complex but fully self-hosted

---

## Success Criteria

The implementation is complete when:
1. `./bin/maxon-zig compile maxon-bin-selfhosted/main.maxon` succeeds
2. The generated executable can compile a simple Maxon program
3. All existing tests continue to pass

---

## Next Steps

Ready to begin implementation with **Phase 1: Core Type System Extensions** starting with strings.
