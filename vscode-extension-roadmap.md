# VS Code Extension Enhancement Roadmap

This document outlines the plan to implement missing features in the Maxon VS Code extension and Language Server Protocol (LSP) server.

## Priority Levels
- **P0**: Critical for basic productivity
- **P1**: High value, frequently used
- **P2**: Nice to have, enhances experience
- **P3**: Future enhancements

---

## Phase 1: High-Impact LSP Features (P0-P1)

### 1.1 Find References (P0)
**Effort**: Medium | **Impact**: High

**Implementation Steps**:
1. Add `textDocument/references` handler in `lsp_server.cpp`
2. Implement `Analyzer::getReferences()` in `analyzer.cpp`
   - Parse document to AST
   - Walk AST to find all identifier usages
   - Match identifiers to the symbol at requested position
   - Return locations of all matching references
3. Support workspace-wide references (requires document cache)
4. Register capability in `initialize` response

**Files to Modify**:
- `lsp-server/include/lsp_server.h` - Add handler declaration
- `lsp-server/include/analyzer.h` - Add `getReferences()` method
- `lsp-server/src/lsp_server.cpp` - Implement handler
- `lsp-server/src/analyzer.cpp` - Implement reference finding logic

### 1.2 Rename Symbol (P0)
**Effort**: Medium-High | **Impact**: High

**Implementation Steps**:
1. Add `textDocument/rename` and `textDocument/prepareRename` handlers
2. Implement `Analyzer::prepareRename()` - validate rename position
3. Implement `Analyzer::getRenameEdits()` 
   - Find all references to symbol
   - Generate `WorkspaceEdit` with text edits for each location
4. Handle edge cases (keywords, stdlib functions should not be renameable)

**Files to Modify**:
- `lsp-server/include/lsp_types.h` - Add `WorkspaceEdit` structure
- `lsp-server/include/analyzer.h` - Add rename methods
- `lsp-server/src/lsp_server.cpp` - Add handlers
- `lsp-server/src/analyzer.cpp` - Implement rename logic

### 1.3 Signature Help (P1)
**Effort**: Medium | **Impact**: High

**Implementation Steps**:
1. Add `textDocument/signatureHelp` handler
2. Implement `Analyzer::getSignatureHelp()`
   - Detect when cursor is inside function call `()`
   - Extract function name being called
   - Look up function signature from:
     - Document-defined functions
     - Stdlib functions
   - Determine active parameter based on comma count
3. Return signature information with parameter highlighting

**Files to Modify**:
- `lsp-server/include/lsp_types.h` - Add `SignatureHelp` structures
- `lsp-server/include/analyzer.h` - Add method
- `lsp-server/src/lsp_server.cpp` - Add handler
- `lsp-server/src/analyzer.cpp` - Implement signature detection

**Extension Changes**:
- No changes needed (LSP client handles automatically)

### 1.4 Document Formatting (P1)
**Effort**: High | **Impact**: Medium

**Implementation Steps**:
1. Create formatter module in compiler
   - Define Maxon style guide (indentation, spacing, line breaks)
   - Implement AST-based formatter
2. Add `textDocument/formatting` and `textDocument/rangeFormatting` handlers
3. Options to consider:
   - Indent size (default: 4 spaces)
   - Max line length
   - Space around operators
   - End block identifier style

**Files to Create**:
- `maxon-bin/formatter.h` - Formatter interface
- `maxon-bin/formatter.cpp` - Formatter implementation
- Or create separate `maxon format` command and invoke from LSP

**Files to Modify**:
- `lsp-server/src/lsp_server.cpp` - Add formatting handlers
- `lsp-server/src/analyzer.cpp` - Integrate with formatter

---

## Phase 2: Enhanced IDE Experience (P1)

### 2.1 Code Snippets (P1)
**Effort**: Low | **Impact**: High

**Implementation Steps**:
1. Create `vscode-extension/snippets/maxon.json`
2. Define snippets for:
   - `function` - function declaration with parameters
   - `if`, `ifelse` - conditional blocks with end markers
   - `while` - loop with end marker
   - `var` - variable declaration
   - `print` - print statement with format string
   - `main` - main function template
   - `array` - array declaration

**Files to Create**:
- `vscode-extension/snippets/maxon.json`

**Files to Modify**:
- `vscode-extension/package.json` - Add `snippets` contribution point

**Example Snippet**:
```json
{
  "Function Declaration": {
    "prefix": "function",
    "body": [
      "function ${1:name}(${2:params}) ${3:int}",
      "    ${4:// body}",
      "    return ${5:0}",
      "end '${1:name}'"
    ]
  }
}
```

### 2.2 Semantic Tokens (P1)
**Effort**: Medium-High | **Impact**: Medium

**Implementation Steps**:
1. Add `textDocument/semanticTokens/full` handler
2. Implement `Analyzer::getSemanticTokens()`
   - Parse document to AST
   - Walk AST and classify each token:
     - Functions (definition vs call)
     - Variables (definition vs usage)
     - Parameters
     - Types
     - Keywords
     - Stdlib symbols
3. Return encoded token array per LSP spec
4. Define semantic token legend in capabilities

**Benefits**:
- Distinguish function calls from function definitions
- Highlight unused variables differently
- Show parameter names distinctly
- Better stdlib function highlighting

**Files to Modify**:
- `lsp-server/include/lsp_types.h` - Add semantic token types
- `lsp-server/src/lsp_server.cpp` - Add handler and legend
- `lsp-server/src/analyzer.cpp` - Implement token classification

### 2.3 Folding Ranges (P1)
**Effort**: Low-Medium | **Impact**: Medium

**Implementation Steps**:
1. Add `textDocument/foldingRange` handler
2. Implement `Analyzer::getFoldingRanges()`
   - Parse document to AST
   - Identify foldable regions:
     - Function bodies (from declaration to `end 'name'`)
     - If/else blocks
     - While loops
     - Comment blocks (consecutive lines)
3. Return ranges with fold kind (region, comment)

**Files to Modify**:
- `lsp-server/include/lsp_types.h` - Add `FoldingRange` structure
- `lsp-server/src/lsp_server.cpp` - Add handler
- `lsp-server/src/analyzer.cpp` - Implement range detection

### 2.4 Document Highlights (P1)
**Effort**: Low | **Impact**: Medium

**Implementation Steps**:
1. Add `textDocument/documentHighlight` handler
2. Implement `Analyzer::getDocumentHighlights()`
   - Find all references to symbol at cursor (reuse reference logic)
   - Return ranges within current document
   - Distinguish read vs write highlights
3. Highlight all occurrences when cursor is on identifier

**Files to Modify**:
- `lsp-server/src/lsp_server.cpp` - Add handler
- `lsp-server/src/analyzer.cpp` - Implement (leverage references code)

---

## Phase 3: Language Configuration Improvements (P1)

### 3.1 Enhanced Language Configuration (P1)
**Effort**: Low | **Impact**: Medium

**Implementation Steps**:
1. Update `vscode-extension/language-configuration.json`:
   - Add `[]` and `{}` to brackets (future-proofing)
   - Add auto-closing pairs for brackets
   - Define indentation rules (increase after `function`, `if`, `while`)
   - Add on-enter rules for automatic indentation
   - Define word pattern for better word selection

**Files to Modify**:
- `vscode-extension/language-configuration.json`

**Example Configuration**:
```json
{
  "brackets": [
    ["(", ")"],
    ["[", "]"],
    ["{", "}"]
  ],
  "indentationRules": {
    "increaseIndentPattern": "^\\s*(function|if|else|while)\\b",
    "decreaseIndentPattern": "^\\s*end\\b"
  },
  "onEnterRules": [
    {
      "beforeText": "^\\s*(function|if|else|while)\\b.*$",
      "action": { "indent": "indent" }
    }
  ]
}
```

### 3.2 Enhanced TextMate Grammar (P1)
**Effort**: Low-Medium | **Impact**: Medium

**Implementation Steps**:
1. Update `vscode-extension/syntaxes/maxon.tmLanguage.json`:
   - Add float literal pattern: `\b[0-9]+\.[0-9]+\b`
   - Add array syntax highlighting for `[]`
   - Add pointer dereference operator `@`
   - Add qualified name pattern for `stdlib.fmt.printf` style
   - Add string interpolation pattern for format strings
   - Add boolean literals (`true`, `false`)
   - Add more operators (`&`, `|`, `!`, etc.)

**Files to Modify**:
- `vscode-extension/syntaxes/maxon.tmLanguage.json`

**Patterns to Add**:
```json
{
  "name": "constant.numeric.float.maxon",
  "match": "\\b[0-9]+\\.[0-9]+([eE][+-]?[0-9]+)?\\b"
},
{
  "name": "meta.qualified-name.maxon",
  "match": "\\b([a-zA-Z_][a-zA-Z0-9_]*\\.)+[a-zA-Z_][a-zA-Z0-9_]*\\b"
}
```

---

## Phase 4: Extension Configuration & Quality of Life (P2)

### 4.1 Extension Settings (P2)
**Effort**: Low | **Impact**: Low-Medium

**Implementation Steps**:
1. Add configuration schema to `package.json`
2. Define settings:
   - `maxon.lspServerPath` - Custom LSP server path
   - `maxon.compilerPath` - Custom compiler path
   - `maxon.trace.server` - LSP trace level
   - `maxon.format.indentSize` - Formatting indent size
   - `maxon.stdlib.path` - Custom stdlib path
3. Read settings in extension and pass to LSP server

**Files to Modify**:
- `vscode-extension/package.json` - Add configuration contribution
- `vscode-extension/src/extension.ts` - Read and use settings

### 4.2 Tasks and Build Support (P2)
**Effort**: Low-Medium | **Impact**: Medium

**Implementation Steps**:
1. Create task provider in extension
2. Auto-detect `.maxon` files and offer:
   - "Build" task: `maxon compile file.maxon`
   - "Run" task: `maxon file.maxon`
   - "Run with arguments" task (prompt for args)
3. Add problem matcher for compiler errors
4. Register task provider in `activate()`

**Files to Create**:
- `vscode-extension/src/taskProvider.ts`

**Files to Modify**:
- `vscode-extension/src/extension.ts` - Register task provider
- `vscode-extension/package.json` - Add task definition

### 4.3 File Icon (P2)
**Effort**: Low | **Impact**: Low

**Implementation Steps**:
1. Create icon for `.maxon` files (SVG recommended)
2. Add icon theme contribution to `package.json`
3. Place icon in `vscode-extension/icons/`

**Files to Create**:
- `vscode-extension/icons/maxon-file.svg`

**Files to Modify**:
- `vscode-extension/package.json` - Add icon theme

---

## Phase 5: Advanced LSP Features (P2-P3)

### 5.1 Workspace Symbols (P2)
**Effort**: Medium | **Impact**: Medium

**Implementation Steps**:
1. Add `workspace/symbol` handler
2. Cache parsed ASTs for all workspace documents
3. Implement symbol search across all documents
4. Support fuzzy matching on symbol names

**Files to Modify**:
- `lsp-server/include/document_manager.h` - Add workspace cache
- `lsp-server/src/lsp_server.cpp` - Add handler
- `lsp-server/src/analyzer.cpp` - Implement workspace search

### 5.2 Inlay Hints (P2)
**Effort**: Medium | **Impact**: Low-Medium

**Implementation Steps**:
1. Add `textDocument/inlayHint` handler
2. Implement hints for:
   - Parameter names in function calls
   - Inferred types for `var` declarations (future)
3. Make hints configurable via settings

**Files to Modify**:
- `lsp-server/src/lsp_server.cpp` - Add handler
- `lsp-server/src/analyzer.cpp` - Implement hint generation

### 5.3 Call Hierarchy (P2)
**Effort**: Medium-High | **Impact**: Low-Medium

**Implementation Steps**:
1. Add `textDocument/prepareCallHierarchy` handler
2. Add `callHierarchy/incomingCalls` handler
3. Add `callHierarchy/outgoingCalls` handler
4. Implement call graph analysis

**Files to Modify**:
- `lsp-server/src/lsp_server.cpp` - Add handlers
- `lsp-server/src/analyzer.cpp` - Implement call analysis

### 5.4 Selection Range (P3)
**Effort**: Low-Medium | **Impact**: Low

**Implementation Steps**:
1. Add `textDocument/selectionRange` handler
2. Implement smart selection expansion based on AST
3. Support expanding from identifier → expression → statement → block → function

### 5.5 Document Links (P3)
**Effort**: Low | **Impact**: Low

**Implementation Steps**:
1. Add `textDocument/documentLink` handler
2. Detect patterns like:
   - File paths in comments
   - URLs in documentation
   - Import paths (future)
3. Return clickable links

---

## Phase 6: Debugging Support (P1-P2)

### 6.1 Debug Adapter Protocol (DAP) (P1)
**Effort**: Very High | **Impact**: High

**Implementation Steps**:
1. Implement Debug Adapter for Maxon
2. Options:
   - Use LLVM debugger integration
   - Wrap GDB/LLDB
   - Custom interpreter with debug mode
3. Support features:
   - Breakpoints
   - Step through code
   - Variable inspection
   - Call stack
   - Expression evaluation
4. Create debug configuration in extension

**Files to Create**:
- `debug-adapter/` - New directory for debug adapter
- `vscode-extension/src/debugAdapter.ts` - Debug adapter implementation

**Files to Modify**:
- `vscode-extension/package.json` - Add debug contribution
- `maxon-bin/` - Add debug info generation flags

**Note**: This is a major undertaking and may warrant its own project phase.

---

## Implementation Priority Order

### Immediate (Next Sprint)
1. Code Snippets (quick win, high impact)
2. Enhanced Language Configuration
3. Enhanced TextMate Grammar
4. Extension Settings (foundation for other features)

### Short Term (1-2 months)
1. Find References
2. Signature Help
3. Document Highlights
4. Folding Ranges

### Medium Term (2-4 months)
1. Rename Symbol
2. Semantic Tokens
3. Document Formatting
4. Task Provider

### Long Term (4-6 months)
1. Workspace Symbols
2. Inlay Hints
3. Call Hierarchy
4. Debug Adapter (major project)

---

## Testing Strategy

### For Each Feature:
1. **Unit Tests**: Test LSP handlers with mock documents
2. **Integration Tests**: Test extension with VS Code test framework
3. **Manual Testing**: Real-world usage scenarios
4. **Performance Testing**: Ensure features don't slow down editor

### Test Files Locations:
- LSP tests: `lsp-server/tests/`
- Extension tests: `vscode-extension/src/test/suite/`

### Key Test Scenarios:
- Large files (1000+ lines)
- Multiple workspace folders
- Concurrent edits
- Edge cases (empty files, syntax errors, etc.)

---

## Documentation Updates

For each implemented feature, update:
1. `vscode-extension/README.md` - User-facing features
2. `vscode-extension/CHANGELOG.md` - Version history
3. Project documentation in `docs/` if applicable
4. LSP server capabilities in code comments

---

## Dependencies and Prerequisites

### Required Tools:
- ✅ C++17 compiler
- ✅ CMake/Ninja
- ✅ Node.js/npm
- ✅ TypeScript

### Optional:
- VS Code Extension Test Runner
- Code coverage tools
- Performance profilers

### External Libraries:
- ✅ nlohmann/json (already in use)
- Consider: clang-format for formatter implementation

---

## Maintenance Considerations

### Code Organization:
- Keep LSP handlers small, delegate to Analyzer
- Separate parsing/analysis from LSP protocol concerns
- Maintain consistent error handling
- Add logging for debugging LSP issues

### Performance:
- Cache parsed ASTs for open documents
- Use incremental parsing where possible
- Implement timeout for long-running operations
- Consider async operations for workspace-wide features

### Compatibility:
- Test with multiple VS Code versions
- Ensure LSP protocol version compatibility
- Handle missing optional capabilities gracefully

---

## Success Metrics

### User Experience:
- Time to implement common refactoring tasks
- Number of manual navigation actions saved
- Code formatting consistency
- Reduced syntax errors through snippets

### Technical:
- LSP response times < 100ms for document features
- LSP response times < 1s for workspace features
- Zero crashes in normal usage
- Test coverage > 80%

---

## Notes

- This roadmap is flexible and priorities may shift based on user feedback
- Some features (like rename) depend on others (find references)
- Consider user surveys to validate priority assumptions
- Breaking changes should be avoided once extension is published

## Estimated Total Effort
- **Phase 1-3**: ~6-8 weeks (1 developer)
- **Phase 4-5**: ~4-6 weeks
- **Phase 6**: ~8-12 weeks (major undertaking)
- **Total**: ~4-6 months for comprehensive feature set
