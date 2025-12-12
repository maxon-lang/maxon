# Maxon Compiler Development

## Project Overview

Maxon is a statically-typed programming language with a custom native x86-64 backend (no LLVM dependency for code generation):

- **Compiler** (`maxon-bin/`) - C++ compiler generating native x86-64 via IR
- **Language Server** (`lsp-server/`) - C++ LSP for IDE integration
- **VS Code Extension** (`vscode-extension/`) - Syntax highlighting, debugging
- **Runtime Library** (`maxon-runtime/`) - Platform-specific runtime (no C runtime)
- **Standard Library** (`stdlib/`) - Maxon standard library modules
- **Tests** (`language-tests/`, `backend-tests/`, `specs/`) - Spec-driven development

## Building things Quick Reference

| Task | Command |
|------|---------|
| Build everything | `make all` |
| Build compiler only | `make compiler` |
| Run all tests | `make test` |
| Run backend tests | `make backend-test` |
| Compile and run | `./bin/maxon file.maxon` |
| Compile with IR output | `./bin/maxon compile file.maxon --emit-ir` |
| Compile and run lsp tests | `make lsp-test` |

## Documentation

- **`docs/LANGUAGE_REFERENCE.md`** - Complete Maxon language syntax and semantics
- **`docs/SPECS.md`** - Spec file format, workflow, and how to write specs
- **`maxon-runtime/README.md`** - Runtime library details

## Directory Structure

```
maxon/
├── maxon-bin/           # Compiler source (C++)
│   ├── backend/         # x86-64 native code generator
│   ├── codegen_mir/     # IR code generation from AST
│   ├── mir/             # IR infrastructure & optimizer passes
│   ├── parser/          # Parser implementation
│   └── semantic_analyzer/
├── maxon-runtime/       # Runtime library (handwritten IR)
├── stdlib/              # Standard library (Maxon source)
├── lsp-server/          # Language server (C++)
├── vscode-extension/    # VS Code extension (TypeScript)
├── language-tests/      # Language test suite
├── backend-tests/       # Backend-specific tests
├── specs/               # Language specifications (source of truth)
└── docs/                # Documentation
```

## Key Compiler Files

- `lexer.cpp/h` - Tokenization with SIMD optimizations
- `parser.cpp` + `parser/` - Recursive descent parser
- `semantic_analyzer.cpp` - Type checking, name resolution
- `codegen_mir.cpp` + `codegen_mir/` - AST to IR translation
- `mir/optimizer.cpp` + `mir/opt_*.cpp` - Optimization passes
- `backend/x86_codegen.cpp` - IR to x86-64 machine code
- `backend/pe_writer.cpp` - Windows PE executable writer
- `backend/elf_writer.cpp` - Linux ELF executable writer

## Spec-Driven Development

**All language features must have a spec file in `specs/`** - this is the single source of truth.

Each spec contains three sections:
1. **Developer Notes** - Implementation details for maintainers
2. **Documentation** - User-facing docs (extracted to HTML)
3. **Tests** - Test cases (extracted to `language-tests/fragments/`)

Workflow for new features:
1. Create `specs/feature-name.md` with frontmatter, notes, docs, and tests
2. `maxon extract-specs` - Extract test fragments from spec
3. `maxon regen-fragments` - Generate IR for test fragments
4. Implement until tests pass
5. `make docs` - Generate HTML documentation

See `docs/SPECS.md` for the complete spec file format and detailed workflow.

## Debugging

The compiler generates DWARF debug info for source-level debugging. Use `-g` flag to include debug symbols.

Debugging tools:
- **Windows**: Use VS Code with the Maxon extension (integrates Windows Debug API)
- **Linux**: Use LLDB or GDB with the compiled executable

Debugger tests are in `debugger-tests/` - run with `make debugger-test`.

See `docs/COMPILER_DEBUGGING.md` for detailed workflow.

## Constraints
- **No C Runtime** - Use `maxon-runtime/` for all system functionality
- **Clean up temp files** - Create test files in `/temp` and delete after
- **Don't use here documents** - Write files directly with file tools
- **Comments explain "why"** - Not "what" the code does
- **Absolute paths** - Always use absolute paths for file operations
- **LF line endings** - All source files use Unix-style line endings
- Don't create new documentation files unless instructed
- Do not edit test fragments (in /language-tests/fragments). These are generated from the spec files, edit the spec file.
- **Do not use git** - Ignore any git history just fix the current tree
- **Always fix tests** - Don't worry about it pre-existing or not, if you find a failing test then fix it

## Development Notes
- Build system uses CMake with Ninja generator
- Windows requires Git Bash for Make commands
- Linux development uses dev container (recommended)
- All tests must pass before commits to main

## Writing VSCode Extension Tests
- Do not set timeouts
- Do not use arbitrary delays, wait for what you are expecting

## Syntax Quick Reference

```
// Variables
var name = value            // mutable variable
let name = value            // immutable variable

// Type Declarations (composite types)
type Name
    var field1 int          // mutable field
    let field2 string       // immutable field
    var field3 int = 0      // field with default value
end 'Name'

var instance = Name{field1: 1, field2: "hello"}  // instantiation

// Functions
// returnType can be 'nothing' for no return value
function name(p1 type, p2 type) returns returnType
    return value
end 'name'

function name(p1 type, p2 type = default) returns returnType  // default value
    return value
end 'name'

// Function Calls
foo(1, 2)                   // positional arguments
foo(x = 1, y = 2)           // named arguments (optional)
foo(1, y = 2)               // mixed: positional first, then named
foo(y = 2, x = 1)           // named args in any order
foo(1)                      // omit param with default

// Control Flow
if condition 'label'
    statements
end 'label'

if condition 'label1'
    statements
end 'label1' else if othercondition 'label2'
    statements
end 'label2' else 'label3'
    statements
end 'label3'

while condition 'label'
    statements
end 'label'

for item in iterable 'label'
    statements
end 'label'

match expr 'label'
    pattern then statement
    default then statement
end 'label'

// Arrays
var arr = array of 10 int   // sized array
let vals = [1, 2, 3]        // array literal
var elem = arr[0]           // indexing
var size = arr.count()      // length

// Types
int float bool byte character string
array of T
T or nil                    // optional type

// Operators
+ - * / mod                 // arithmetic
== != < > <= >=             // comparison
and or not                  // logical
as                          // type cast

// Literals
42                          // int
3.14                        // float
'A'                         // character (grapheme)
"text"                      // string
true false                  // bool
nil                         // nil (for optionals)
```


See `docs/LANGUAGE_REFERENCE.md` for complete syntax and semantics.
