# Standard Library Implementation Plan for Maxon

## Overview

This plan outlines the implementation of a standard library system for Maxon, enabling programs to call functions defined in `stdlib/` directory files. The initial implementation will focus on making `format_int_array` callable from user programs.

## Design Philosophy

**Implicit Namespaces from File Paths**: Each file's namespace is automatically derived from its directory path relative to the project root. Functions can be called without namespace qualification unless there's a naming collision.

**Examples**:
- `stdlib/fmt/integer.maxon` → functions belong to `stdlib.fmt` namespace
- `stdlib/sys/memory.maxon` → functions belong to `stdlib.sys` namespace
- `myproject/utils/helpers.maxon` → functions belong to `myproject.utils` namespace

**Calling Convention**:
```maxon
// Preferred: unqualified call (works if no collision)
var length int = format_int_array(42, buffer)

// Explicit: fully qualified call (required if collision exists)
var length int = stdlib.fmt.format_int_array(42, buffer)
```

## Current State

### What Works
- **Namespace syntax**: Namespaces are already implemented as block constructs
- **Namespace function calls**: Can call namespace functions using `namespace.function()` syntax
- **Qualified name resolution**: Compiler generates qualified names like `fmt::format_int_array`
- **Test infrastructure**: Fragment tests work and can validate LLVM IR and exit codes

### Issues Identified
1. **Manual namespace declarations**: Files require explicit namespace blocks
2. **No implicit namespace derivation**: No automatic namespace from file path
3. **No stdlib auto-discovery**: Compiler doesn't automatically compile/link stdlib files
4. **No multi-file compilation**: Compiler only processes single input files
5. **Array passing**: Need to verify array-by-reference semantics work correctly
6. **No collision detection**: Can't detect naming conflicts between namespaces

## Requirements

### User Story
A Maxon programmer should be able to:
```maxon
// File: myproject/main.maxon
function main() int
    var buffer [12]char = 0
    // Call without namespace - compiler finds it in stdlib/fmt/integer.maxon
    var length int = format_int_array(42, buffer)
    return length
end 'main'
```

The compiler should:
1. Automatically find and link `stdlib/fmt/integer.maxon`
2. Derive that `format_int_array` is in the `stdlib.fmt` namespace from the file path
3. Allow unqualified calls when there's no ambiguity
4. Require qualification if multiple namespaces export `format_int_array`

## Implementation Phases

### Phase 1: Implicit Namespace from File Path
**Goal**: Automatically derive namespace from file path. No explicit namespace declarations needed in source files.

**File Path to Namespace Mapping**:
```
stdlib/fmt/integer.maxon       → namespace: stdlib.fmt
stdlib/sys/memory.maxon        → namespace: stdlib.sys
myproject/utils/helpers.maxon  → namespace: myproject.utils
main.maxon                     → namespace: (global/root)
```

**Implementation**:
```cpp
// In main.cpp during file compilation
std::string deriveNamespace(const std::string& filePath) {
    // Get directory path relative to current working directory
    std::filesystem::path p(filePath);
    std::filesystem::path dir = p.parent_path();
    
    // Convert path separators to dots
    std::string ns = dir.string();
    std::replace(ns.begin(), ns.end(), '/', '.');
    std::replace(ns.begin(), ns.end(), '\\', '.');
    
    // Examples:
    // "stdlib/fmt" → "stdlib.fmt"
    // "." or "" → "" (global namespace)
    
    return ns;
}

// When parsing a file:
Parser parser(tokens);
std::string fileNamespace = deriveNamespace(inputFilePath);
parser.setDefaultNamespace(fileNamespace);
```

**Source File Format** (no namespace declaration needed):
```maxon
// File: stdlib/fmt/integer.maxon
// Namespace "stdlib.fmt" is automatically inferred

function format_int_array(value int, buffer [12]char) int
    // ... implementation
end 'format_int_array'

function format_int_hex(value int, buffer [12]char) int
    // ... implementation
end 'format_int_hex'
```

**Changes Required**:
- `main.cpp`: Add `deriveNamespace()` function
- `parser.h`: Add `defaultNamespace` member and `setDefaultNamespace()` method
- `parser.cpp`: Apply `defaultNamespace` to all parsed functions automatically
- `ast.h`: Store namespace information with each function in AST
- Remove requirement for explicit `namespace` blocks (but keep support for backward compatibility)

### Phase 2: Unqualified Function Call Resolution
**Goal**: Allow calling functions without namespace qualification. Compiler resolves to the correct namespace automatically.

**Resolution Strategy**:
```cpp
// During semantic analysis of function call:
1. Check if function name exists in current file's namespace
2. Search all imported/linked namespaces for the function name
3. If exactly one match found → use it
4. If multiple matches found → report ambiguity error, require qualification
5. If no matches found → report undefined function error
```

**Example Scenarios**:
```maxon
// File: myapp/main.maxon (namespace: myapp)

function main() int
    var buf [12]char = 0
    
    // Case 1: Unique function name - works!
    var len int = format_int_array(42, buf)  // Resolved to stdlib.fmt.format_int_array
    
    // Case 2: Ambiguous (both myapp.print and stdlib.io.print exist)
    print("hello")  // ERROR: Ambiguous call, multiple definitions found
    
    // Case 3: Explicit qualification resolves ambiguity
    stdlib.io.print("hello")  // OK - explicitly qualified
    
    return len
end 'main'
```

**Ambiguity Error Message**:
```
Error: Ambiguous function call 'print'
Found multiple definitions:
  - stdlib.io.print (in stdlib/io/console.maxon)
  - myapp.print (in myapp/debug.maxon)

Use fully qualified name to resolve:
  stdlib.io.print(...)
  or
  myapp.print(...)
```

**Changes Required**:
- `semantic_analyzer.h/cpp`: 
  - Build global function registry: `map<string, vector<FunctionInfo>>`
  - `FunctionInfo` stores: name, fully qualified name, source file, namespace
  - Implement unqualified name resolution with ambiguity checking
  - Track which functions are used (for auto-discovery phase)
- `parser.cpp`:
  - Support both qualified (`stdlib.fmt.function`) and unqualified (`function`) calls
  - Parse dotted names for qualified calls

### Phase 3: Multi-File Compilation Support
**Goal**: Allow compiler to process multiple source files and link them together.

**Command line syntax**:
```bash
maxonc main.maxon stdlib/fmt/integer.maxon -o output.exe
# or
maxonc main.maxon -stdlib fmt/integer -o output.exe
```

**Implementation Options**:

#### Option A: Explicit File List
```cpp
// main.cpp changes
std::vector<std::string> inputFiles;
for each file:
    - Lex and parse
    - Run semantic analysis
    - Generate to separate LLVM module
- Link all modules together with llvm::Linker
- Generate single executable
```

#### Option B: Automatic Stdlib Discovery
```cpp
// Scan source for namespace references (fmt.*, sys.*, etc.)
// For each referenced namespace:
//   - Look in stdlib/<namespace>/ for .maxon files
//   - Compile those files
//   - Link with main program
```

**Recommended**: Start with Option A (explicit), add Option B later.

**Changes Required**:
- `main.cpp`:
  - Accept multiple input files
  - Create vector of `ProgramAST` objects
  - Use `llvm::Linker` to merge modules
- `codegen.h/cpp`:
  - Modify to support merging multiple modules
  - Ensure no symbol conflicts

### Phase 4: Automatic Stdlib Discovery and Linking
**Goal**: Automatically find and compile stdlib files based on function usage.

**Discovery Strategy**:
```cpp
// After semantic analysis of main file:
1. Get list of unresolved function calls
2. For each unresolved function:
   - Search stdlib/**/*.maxon files for matching function names
   - Parse just the function signatures (quick scan)
3. Compile files that contain needed functions
4. Re-run semantic analysis to resolve references
5. Repeat until all functions resolved or error
```

**Implementation**:
```cpp
// In main.cpp - iterative compilation
std::set<std::string> compiledFiles;
std::vector<std::string> unresolvedFunctions;

do {
    // Run semantic analysis
    SemanticAnalyzer analyzer;
    analyzer.analyze(allPrograms);
    
    unresolvedFunctions = analyzer.getUnresolvedFunctions();
    
    if (unresolvedFunctions.empty()) break;
    
    // Find stdlib files that define these functions
    for (const auto& funcName : unresolvedFunctions) {
        auto stdlibFiles = findStdlibFilesDefining(funcName);
        for (const auto& file : stdlibFiles) {
            if (compiledFiles.count(file) == 0) {
                // Compile this stdlib file
                compileAndAddToProgram(file);
                compiledFiles.insert(file);
            }
        }
    }
    
    // If no new files found, we have an error
    if (no new files added) {
        reportUndefinedFunctions(unresolvedFunctions);
        break;
    }
} while (!unresolvedFunctions.empty());
```

**Optimization - Function Registry Cache**:
```cpp
// Build a registry of all stdlib functions on first use
struct FunctionSignature {
    std::string name;
    std::string filePath;
    std::string namespace;
    std::vector<std::string> paramTypes;
    std::string returnType;
};

std::map<std::string, std::vector<FunctionSignature>> stdlibRegistry;

// Scan stdlib/ directory once and cache
void buildStdlibRegistry() {
    for (auto& file : findAllFilesIn("stdlib/")) {
        // Quick parse for function signatures only
        auto functions = extractFunctionSignatures(file);
        for (auto& func : functions) {
            stdlibRegistry[func.name].push_back(func);
        }
    }
}
```

**Changes Required**:
- `main.cpp`:
  - Implement `findStdlibFilesDefining()`
  - Implement iterative compilation loop
  - Add stdlib registry cache
- `semantic_analyzer.h/cpp`:
  - Track unresolved function calls
  - Public getter `getUnresolvedFunctions()`
- New file `stdlib_scanner.h/cpp`:
  - Quick function signature extraction without full compilation
  - Build and maintain stdlib registry

### Phase 5: Explicit Namespace Support (for disambiguation)
**Goal**: Support explicit namespace qualification when needed to resolve ambiguities.

**Syntax**:
```maxon
// Unqualified (preferred)
format_int_array(42, buffer)

// Partially qualified (if needed)
fmt.format_int_array(42, buffer)

// Fully qualified (explicit)
stdlib.fmt.format_int_array(42, buffer)
```

**Parser Enhancement**:
```cpp
// In parsePrimary() for function calls
if (check(TokenType::IDENTIFIER)) {
    std::vector<std::string> nameParts;
    nameParts.push_back(currentToken().value);
    advance();
    
    // Parse dotted name: a.b.c
    while (check(TokenType::DOT)) {
        advance();
        nameParts.push_back(expect(TokenType::IDENTIFIER).value);
    }
    
    // If followed by '(', it's a function call
    if (check(TokenType::LPAREN)) {
        // Join with :: for internal representation
        std::string callee = join(nameParts, "::");
        // Parse arguments...
    }
}
```

**Resolution with Partial Qualification**:
```cpp
// User writes: fmt.format_int_array(...)
// Semantic analyzer searches for:
//   1. Exact match: "fmt::format_int_array"
//   2. Suffix match: "*.fmt::format_int_array" (e.g., stdlib::fmt::format_int_array)
//   3. If unique suffix match found → use it
//   4. If multiple suffix matches → still ambiguous, require full qualification
```

**Changes Required**:
- `parser.cpp`: Parse dotted names for function calls
- `semantic_analyzer.cpp`: Implement partial namespace resolution
- Support both `::` (internal) and `.` (source code) as namespace separators

### Phase 6: Array Passing Verification
**Goal**: Ensure arrays can be passed to functions correctly (by reference).

**Current Array Type**: `[12]char` becomes `[12 x i8]` in LLVM

**Function Signature**:
```maxon
function format_int_array(value int, buffer [12]char) int
```

**Expected LLVM IR**:
```llvm
define i32 @"fmt::format_int_array"(i32 %value, [12 x i8]* %buffer)
```

**Issue**: Arrays in C/LLVM are typically passed by pointer, not by value.

**Solution**: Maxon should automatically convert array parameters to pointers in function signatures.

**Changes Required**:
- `codegen.cpp` in function declaration:
  - Detect array types in parameters
  - Convert to pointer types: `[N x T]` → `T*` (or opaque `ptr`)
- Update calling convention to pass array address

### Phase 7: LSP Integration for Code Completion
**Goal**: Enable IDE code completion for stdlib functions and namespaced functions through the Language Server Protocol.

**Core Features**:
1. **Function Completion**: Suggest available functions as user types
2. **Namespace-aware Completion**: Show functions from all available namespaces
3. **Signature Help**: Display function parameters and return types
4. **Smart Filtering**: Prioritize unambiguous functions, mark ambiguous ones

**LSP Capabilities to Implement**:
```typescript
// In lsp_server.cpp
- textDocument/completion: Provide function completions
- completionItem/resolve: Get detailed info for selected completion
- textDocument/signatureHelp: Show function signature while typing parameters
- textDocument/hover: Display function documentation on hover
```

**Function Registry for LSP**:
```cpp
// In analyzer.cpp or new lsp_context.cpp
class LSPFunctionRegistry {
public:
    struct FunctionInfo {
        std::string name;              // "format_int_array"
        std::string qualifiedName;     // "stdlib::fmt::format_int_array"
        std::string displayName;       // For UI: "format_int_array"
        std::string namespace_;        // "stdlib.fmt"
        std::string sourceFile;        // "stdlib/fmt/integer.maxon"
        std::vector<Parameter> params;
        std::string returnType;
        std::string documentation;     // Extracted from comments
        bool isAmbiguous;             // Multiple definitions exist?
    };
    
    // Build registry from all compiled files + stdlib
    void buildRegistry(const std::vector<ProgramAST*>& programs);
    
    // Get completions for current context
    std::vector<FunctionInfo> getCompletions(const std::string& prefix, 
                                             const std::string& currentNamespace);
    
    // Check if function name is ambiguous
    bool isAmbiguous(const std::string& functionName);
    
    // Get all candidates for a function name
    std::vector<FunctionInfo> getCandidates(const std::string& functionName);
};
```

**Completion Strategy**:
```cpp
// When user types "form" in their code:
1. Get all functions starting with "form"
2. Check if any are unambiguous (only 1 definition exists)
3. For unambiguous functions:
   - Show as primary completions with unqualified name
   - Add detail: "(from stdlib.fmt)"
4. For ambiguous functions:
   - Show all variants with qualified names
   - Mark with icon/indicator
   - Detail: "(ambiguous - requires qualification)"

// Example completion list:
[
  {
    label: "format_int_array",
    kind: Function,
    detail: "(value int, buffer [12]char) int",
    documentation: "Format an integer to ASCII in a buffer",
    insertText: "format_int_array",
    additionalTextEdits: null,  // No need to add namespace
    sortText: "0_format_int_array"  // Priority sorting
  },
  {
    label: "stdlib.fmt.format_int_array",
    kind: Function,
    detail: "(value int, buffer [12]char) int",
    documentation: "Format an integer to ASCII in a buffer\nNamespace: stdlib.fmt",
    insertText: "stdlib.fmt.format_int_array",
    sortText: "1_format_int_array"  // Lower priority
  }
]
```

**Stdlib Discovery for LSP**:
```cpp
// LSP server needs to know about stdlib functions without compiling
// Option 1: Parse all stdlib files on startup (lazy load)
// Option 2: Maintain a stdlib index file (faster)

// Lazy loading approach:
class StdlibCache {
private:
    std::map<std::string, FunctionInfo> cachedFunctions;
    bool initialized = false;
    
public:
    void initialize() {
        if (initialized) return;
        
        // Scan stdlib directory
        for (auto& file : findAllFiles("stdlib/")) {
            // Quick parse for function signatures only
            auto functions = extractSignatures(file);
            for (auto& func : functions) {
                std::string ns = deriveNamespace(file);
                func.namespace_ = ns;
                func.qualifiedName = ns.empty() ? func.name : ns + "::" + func.name;
                cachedFunctions[func.qualifiedName] = func;
            }
        }
        initialized = true;
    }
    
    std::vector<FunctionInfo> getAllFunctions() {
        initialize();
        // Return all cached functions
    }
};
```

**Signature Help Implementation**:
```cpp
// When user types "format_int_array(" and triggers signature help
SignatureHelp getSignatureHelp(const std::string& functionName, int paramIndex) {
    auto candidates = registry.getCandidates(functionName);
    
    SignatureHelp help;
    for (auto& candidate : candidates) {
        Signature sig;
        sig.label = candidate.name + "(";
        
        for (size_t i = 0; i < candidate.params.size(); i++) {
            if (i > 0) sig.label += ", ";
            sig.label += candidate.params[i].name + " " + candidate.params[i].type;
            
            // Mark parameter ranges for highlighting
            ParameterInfo param;
            param.label = candidate.params[i].name + " " + candidate.params[i].type;
            sig.parameters.push_back(param);
        }
        sig.label += ") " + candidate.returnType;
        sig.documentation = candidate.documentation;
        
        help.signatures.push_back(sig);
    }
    
    help.activeSignature = 0;
    help.activeParameter = paramIndex;
    return help;
}
```

**Document Symbol Provider** (for outline view):
```cpp
// Show functions organized by namespace in document outline
std::vector<DocumentSymbol> getDocumentSymbols(const std::string& uri) {
    auto program = parseDocument(uri);
    std::string fileNamespace = deriveNamespace(uri);
    
    std::vector<DocumentSymbol> symbols;
    for (auto& func : program->functions) {
        DocumentSymbol sym;
        sym.name = func->name;
        sym.kind = SymbolKind::Function;
        sym.detail = fileNamespace.empty() ? "(global)" : fileNamespace;
        // Add range information...
        symbols.push_back(sym);
    }
    return symbols;
}
```

**Changes Required**:
- `lsp/src/analyzer.cpp`:
  - Add `LSPFunctionRegistry` class
  - Build function registry from parsed files
  - Track ambiguous function names
  
- `lsp/src/lsp_server.cpp`:
  - Implement `textDocument/completion` handler
  - Implement `textDocument/signatureHelp` handler
  - Implement `textDocument/hover` handler for function documentation
  - Add stdlib cache initialization on server startup
  
- `lsp/src/completion_provider.cpp` (new file):
  - Function completion logic
  - Filter and rank completions based on context
  - Generate snippet completions for function calls
  
- `lsp/src/stdlib_cache.cpp` (new file):
  - Lazy-load stdlib function signatures
  - Parse function signatures without full compilation
  - Cache results for performance
  
- `lsp/src/document_manager.cpp`:
  - Track open documents and their derived namespaces
  - Maintain per-document function registries
  
- Update VS Code extension:
  - Ensure completion trigger characters include '.' for qualified names
  - Configure signature help trigger characters: '(' and ','

**Testing**:
- Test unqualified completion: type "form" → suggests "format_int_array"
- Test qualified completion: type "stdlib." → suggests "stdlib.fmt", "stdlib.sys", etc.
- Test nested completion: type "stdlib.fmt." → suggests functions in that namespace
- Test signature help: type "format_int_array(" → shows parameter info
- Test ambiguity indication: functions with same name show qualification requirement
- Test hover: hover over function → shows signature and documentation

## Test Fragments to Create

### 1. Implicit namespace from file path
**File**: `language-tests/fragments/implicit-namespace.test`
```maxon
// This test simulates having stdlib/fmt/integer.maxon
// In real implementation, this would be in separate file

// Pretend this function is from stdlib/fmt/integer.maxon
// It would automatically be in stdlib.fmt namespace
function format_int_array(value int, buffer [12]char) int
    if value = 0 'zero'
        buffer[0] = '0' as char
        return 1
    end 'zero'
    return 0
end 'format_int_array'

function main() int
    var buf [12]char = 0
    // Unqualified call - should resolve automatically
    return format_int_array(0, buf)
end 'main'
```
**Expected**: ExitCode: 1

### 2. Unqualified stdlib call
**File**: `language-tests/fragments/stdlib-unqualified-call.test`
```maxon
// Test calling stdlib function without namespace qualification
<Copy full implementation from stdlib/fmt/integer.maxon>

function main() int
    var buffer [12]char = 0
    // No stdlib.fmt prefix needed!
    return format_int_array(42, buffer)
end 'main'
```
**Expected**: ExitCode: 2 (length of "42")

### 3. Ambiguity detection
**File**: `language-tests/fragments/ambiguous-function-call.test`
```maxon
// Define format_int_array in two different "namespaces"
// (simulated with different contexts)

function format_int_array(value int, buffer [12]char) int
    // Local version
    return 1
end 'format_int_array'

// This should cause a compilation error about ambiguity
// when calling format_int_array() if another one exists
function main() int
    var buf [12]char = 0
    return format_int_array(42, buf)
end 'main'
```
**Expected**: Compilation error about ambiguous call

### 4. Explicit qualification resolves ambiguity
**File**: `language-tests/fragments/qualified-resolves-ambiguity.test`
```maxon
// When there's ambiguity, explicit qualification should work
function main() int
    var buffer [12]char = 0
    // Explicitly qualified call
    return stdlib.fmt.format_int_array(42, buffer)
end 'main'
```
**Expected**: ExitCode: 2 (length of "42")

### 5. Partial qualification
**File**: `language-tests/fragments/partial-namespace-qualification.test`
```maxon
function main() int
    var buffer [12]char = 0
    // Partial qualification: fmt.function (not full stdlib.fmt.function)
    return fmt.format_int_array(42, buffer)
end 'main'
```
**Expected**: ExitCode: 2

### 6. Array pointer test
**File**: `language-tests/fragments/array-function-param.test`
```maxon
function fill_array(arr [5]int, value int) int
    var i int = 0
    while i < 5 'fill'
        arr[i] = value
        i = i + 1
    end 'fill'
    return 0
end 'fill_array'

function main() int
    var nums [5]int = 0
    fill_array(nums, 42)
    return nums[2]
end 'main'
```
**Expected**: ExitCode: 42

### 7. Backward compatibility with explicit namespace blocks
**File**: `language-tests/fragments/explicit-namespace-block.test`
```maxon
// Test that old-style explicit namespace blocks still work
namespace utils 'utils'
    function double(x int) int
        return x * 2
    end 'double'
end 'utils'

function main() int
    // Can call with or without qualification
    return double(21)  // Should resolve to utils.double
end 'main'
```
**Expected**: ExitCode: 42

## Implementation Order

1. **Phase 1**: Implicit namespace from file path (2-3 hours)
   - Core feature that enables everything else
   - Derive namespace from directory structure
   - Remove requirement for explicit namespace declarations
   - Create test fragment 1, 7
   
2. **Phase 6**: Fix array passing (2-3 hours)
   - Critical for `format_int_array` to work
   - Create test fragment 6
   
3. **Phase 3**: Multi-file compilation (3-4 hours)
   - Basic support for explicit file lists
   - Build function registry across all files
   - Link modules together
   
4. **Phase 2**: Unqualified function resolution (3-4 hours)
   - Allow calling functions without namespace
   - Detect and report ambiguities
   - Create test fragments 2, 3, 4
   
5. **Phase 5**: Explicit namespace support (1-2 hours)
   - Support partial and full qualification
   - Create test fragment 5

6. **Phase 4**: Automatic stdlib discovery (2-3 hours)
   - Scan stdlib directory
   - Auto-compile referenced functions
   - Iterative resolution

7. **Phase 7**: LSP integration for code completion (3-4 hours)
   - Implement completion provider
   - Add signature help
   - Build stdlib function cache
   - Test in VS Code

8. **Integration Testing**: (1-2 hours)
   - Comprehensive end-to-end tests
   - Verify all stdlib functions work
   - Test IDE features
   - Document usage examples

## Total Estimated Time: 18-25 hours

## Success Criteria

### Compiler
✓ User can write a program that calls `format_int_array()` without namespace qualification
✓ Compiler automatically derives namespace from file path (`stdlib/fmt/*.maxon` → `stdlib.fmt`)
✓ Compiler automatically finds and links stdlib files based on function usage
✓ Unqualified function calls work when unambiguous
✓ Explicit qualification works: `stdlib.fmt.format_int_array()`
✓ Partial qualification works: `fmt.format_int_array()`
✓ Ambiguous calls produce clear error messages
✓ Arrays pass correctly by reference to functions
✓ All test fragments pass

### LSP / IDE
✓ Code completion suggests stdlib functions without namespace prefix
✓ Typing "format_int_array" shows completion for unambiguous functions
✓ Typing "stdlib." shows namespace completions
✓ Typing "stdlib.fmt." shows functions in that namespace
✓ Signature help displays parameters when typing function calls
✓ Hover shows function documentation and signature
✓ Ambiguous functions are marked/indicated in completions
✓ Document outline shows functions with their inferred namespace

### Documentation
✓ Documentation updated with usage examples
✓ Guide for unqualified vs qualified function calls
✓ Examples of resolving ambiguity
✓ IDE usage guide with screenshots

## Key Benefits of This Approach

1. **Clean, minimal syntax**: Just `format_int_array()` instead of `stdlib.fmt.format_int_array()`
2. **Namespaces are automatic**: Derived from directory structure, no boilerplate
3. **Explicit when needed**: Can still use full qualification to resolve collisions
4. **Discoverable**: Compiler tells you when qualification is needed
5. **Scalable**: Works for any project structure, not just stdlib

## Future Enhancements

- [ ] Precompiled stdlib modules (.o files)
- [ ] Module system with explicit imports
- [ ] Namespace aliases: `using fmt as f`
- [ ] Conditional compilation for different platforms
- [ ] Package manager integration

## Notes

- Keep backward compatibility with block-style namespaces
- Maintain zero-dependency philosophy (no CRT)
- All stdlib functions should be in LLVM IR (no external C deps)
- Consider performance implications of linking large stdlib
