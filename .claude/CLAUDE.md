# Maxon Compiler Development

## Project Overview

Maxon is a statically-typed programming language with a custom native x86-64 backend (no LLVM dependency for code generation):

- **Compiler** (`maxon-bin/`) - Zig compiler generating native x86-64 via IR
- **VS Code Extension** (`vscode-extension/`) - Syntax highlighting, debugging
- **Standard Library** (`stdlib/`) - Maxon standard library modules
- **Specs & Tests** (`maxon-bin/specs/`) - Spec-driven development with test fragments

## Building Quick Reference

| Task | Command |
|------|---------|
| Build compiler | `cd maxon-bin && zig build` |
| Run all tests | `cd maxon-bin && zig build test` |
| Compile and run | `./bin/maxon run file.maxon` |
| Compile only | `./bin/maxon compile file.maxon` |
| Compile with IR output | `./bin/maxon compile file.maxon --emit-ir` |

## Documentation

- **`docs/LANGUAGE_REFERENCE.md`** - Complete Maxon language syntax and semantics
- **`docs/SPECS.md`** - Spec file format, workflow, and how to write specs

## Directory Structure

```
maxon/
├── maxon-bin/           # Compiler (Zig)
│   ├── src/
│   │   ├── main.zig     # Entry point
│   │   ├── compiler/    # Compiler pipeline
│   │   │   ├── 0-compiler.zig    # Main compiler orchestration
│   │   │   ├── 1-lexer.zig       # Tokenization
│   │   │   ├── 2-parser.zig      # Recursive descent parser
│   │   │   ├── 3-mutation_analysis.zig  # Ownership/mutation analysis
│   │   │   ├── 4-ast_to_ir.zig   # AST to IR translation
│   │   │   ├── 5-optimizer.zig   # Optimization passes
│   │   │   ├── 6-codegen.zig     # IR to x86-64 machine code
│   │   │   ├── 7-pe.zig          # Windows PE executable writer
│   │   │   ├── ast.zig           # AST definitions
│   │   │   ├── ir.zig            # IR definitions
│   │   │   └── x86.zig           # x86 instruction encoding
│   │   └── testing/     # Test infrastructure
│   ├── specs/           # Language specifications (source of truth)
│   │   └── fragments/   # Generated test fragments
│   └── build.zig        # Zig build configuration
├── stdlib/              # Standard library (Maxon source)
├── vscode-extension/    # VS Code extension (TypeScript)
└── docs/                # Documentation
```

## Spec-Driven Development

**All language features must have a spec file in `maxon-bin/specs/`** - this is the single source of truth.

Each spec contains:
1. **Developer Notes** - Implementation details for maintainers
2. **Documentation** - User-facing docs
3. **Tests** - Test cases (extracted to `maxon-bin/specs/fragments/`)

Workflow for new features:
1. Create `maxon-bin/specs/feature-name.md` with frontmatter, notes, docs, and tests
2. Run `maxon test` to extract and run test fragments
3. Implement until tests pass

See `docs/SPECS.md` for the complete spec file format and detailed workflow.

## Constraints
- **Clean up temp files** - Create test files in `/temp` and delete after
- **Don't use here documents** - Write files directly with file tools
- **Comments explain "why"** - Not "what" the code does
- **Absolute paths** - Always use absolute paths for file operations
- **LF line endings** - All source files use Unix-style line endings
- Don't create new documentation files unless instructed
- Do not edit test fragments (in `maxon-bin/specs/fragments/`). These are generated from the spec files, edit the spec file.
- **Do not use git** - Ignore any git history just fix the current tree
- **Always fix tests** - Don't worry about it pre-existing or not, if you find a failing test then fix it

## Development Notes
- Build system uses Zig build
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
// omit returns clause for void functions
function name(p1 type, p2 type) returns returnType
    return value
end 'name'

function name(p1 type, p2 type = default) returns returnType  // default value
    return value
end 'name'

function voidFunc(p1 type)  // no return type = void
    // statements
end 'voidFunc'

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
"Hello {name}!"             // string interpolation
true false                  // bool
nil                         // nil (for optionals)
```


See `docs/LANGUAGE_REFERENCE.md` for complete syntax and semantics.
