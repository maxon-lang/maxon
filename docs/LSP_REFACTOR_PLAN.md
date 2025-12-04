# LSP Server Refactor Plan

**Goal:** Eliminate all hardcoded language information from the LSP server. The compiler becomes the single source of truth for all language knowledge (keywords, types, stdlib). Making it impossible to add a keyword without also providing LSP metadata.

**Branch:** `lsp-refactor`

---

## Phase 1: Single Source of Truth for Keywords ✅

### 1.1 Extend KeywordEntry with LSP metadata
- [x] Add `KeywordCompletionKind` enum to `lexer_keyword_matcher.h` (matching LSP CompletionItemKind values)
- [x] Add fields to `KeywordEntry`: `documentation`, `insertText`, `completionKind`
- [x] Update `add_keyword()` signature to require all LSP fields (compile error if missing)
- [x] Update `add_math_keyword()` signature similarly

### 1.2 Update all keyword registrations
- [x] Update Type keywords (int, float, byte, bool) with documentation, insertText, completionKind
- [x] Update ControlFlow keywords (if, then, else, while, for, etc.) with snippets
- [x] Update Declaration keywords (function, var, let, struct, enum, interface, etc.) with snippets
- [x] Update MathIntrinsic keywords (sqrt, abs, floor, etc.) with documentation
- [x] Update Literal keywords (true, false, nil)
- [x] Update Operator keywords (as, and, or, not, mod, is)

### 1.3 Add LSP accessor methods
- [x] Add `KeywordMatcher::getLSPKeywordInfo()` returning vector of keyword LSP data
- [x] Add `KeywordMatcher::getKeywordsForCompletion()` for completion filtering

---

## Phase 2: AST Source Range Tracking ✅

### 2.1 Add SourceRange struct to ast.h
- [x] Define `SourceRange { startLine, startCol, endLine, endCol }` with helper methods
- [x] Add `contains(line, col)` and `overlaps(other)` methods

### 2.2 Extend AST nodes with end positions
- [x] Add `endLine`, `endColumn` to `ExprAST`
- [x] Add `endLine`, `endColumn` to `StmtAST`
- [x] Add `endLine`, `endColumn` to `FunctionAST`
- [x] Add `endLine`, `endColumn` to `StructDefAST`
- [x] Add `endLine`, `endColumn` to `EnumDefAST`
- [x] Add `endLine`, `endColumn` to `InterfaceDefAST`

### 2.3 Update parser to record end positions
- [x] Update `parseFunction()` to set end position after `end 'name'`
- [x] Update `parseStruct()` to set end position
- [x] Update `parseEnum()` to set end position
- [x] Update `parseInterface()` to set end position
- [x] Update statement parsers (if, while, for, etc.) to set end positions
- [x] Update expression parsers to set end positions

---

## Phase 3: Parser Error Recovery ✅

### 3.1 Add error infrastructure
- [x] Add `ErrorStmtAST` node to `ast.h` with source range
- [x] Add `ParseError` struct with message, line, column
- [x] Add `std::vector<ParseError> parseErrors` to `ProgramAST`

### 3.2 Implement synchronization
- [x] Add `Parser::synchronize()` method that skips to sync tokens
- [x] Sync tokens: `function`, `struct`, `enum`, `interface`, `end`, `var`, `let`, `if`, `while`, `for`, `return`, EOF
- [x] Use `KeywordMatcher::get_category()` to identify sync points

### 3.3 Add try-catch recovery
- [x] Wrap `parseStatement()` body in try-catch, create `ErrorStmtAST` on failure
- [x] Wrap top-level declaration parsing in try-catch
- [x] Continue parsing after error instead of throwing

---

## Phase 4: Incremental Parsing Infrastructure ✅

### 4.1 Extend TokenStream for incremental updates
- [x] Add `TokenStream::getTokenIndicesForLineRange(startLine, endLine)`
- [x] Add `TokenStream::splice(startIdx, endIdx, newTokens)`
- [x] Add `TokenStream::buildLineIndex()` for fast line-to-token mapping

### 4.2 Extend BlockBoundaryAnalyzer
- [x] Add `BlockBoundaryAnalyzer::invalidateFrom(tokenIndex)`
- [x] Add incremental boundary recomputation

### 4.3 Create IncrementalParser class
- [x] Create `maxon-bin/incremental_parser.h`
- [x] Define `EditRegion { startLine, startCol, endLine, endCol, newText }`
- [x] Implement `findAffectedDeclarations(oldAST, editRegion)` using SourceRange overlap
- [x] Implement `findAffectedStatements(functionAST, editRegion)` for statement-level updates
- [x] Implement `update(oldAST, oldTokens, editRegion)` returning new AST with preserved nodes

---

## Phase 5: Compiler API for LSP ✅

### 5.1 Create LSP result types
- [x] Create `maxon-bin/compiler_api.h` with LSP-specific types
- [x] Define `LSPSymbolInfo { name, kind, type, documentation, sourceRange }`
- [x] Define `LSPAnalysisResult { ast, symbols, parseErrors, semanticErrors }`
- [x] Define `StdlibSymbols { functions, structs, enums, interfaces }`

### 5.2 Add analysis API to compiler
- [x] Add `analyzeForLSP(source, filename)`
- [x] Add `loadStdlib(stdlibPath)` returning `StdlibSymbols`
- [x] Add `getKeywordInfo()` delegating to `KeywordMatcher`

### 5.3 Documentation extraction
- [x] Add `extractDocComment(source, declarationLine)` to parse `///` comments
- [x] Integrate doc extraction into stdlib loading

---

## Phase 6: Per-Function Semantic Caching ✅

### 6.1 Add semantic result caching
- [x] Add `FunctionSemanticResult { localVariables, errors, warnings }`
- [x] Add `std::map<std::string, FunctionSemanticResult> functionCache_` to SemanticAnalyzer
- [x] Track which functions are dirty based on edit region

### 6.2 Implement incremental semantic analysis
- [x] Add `SemanticAnalyzer::analyzeIncremental(program, dirtyFunctions)`
- [x] Reuse cached results for clean functions
- [x] Update global symbols incrementally based on edit region

---

## Phase 7: LSP Server Refactor ✅

### 7.1 Remove hardcoded language info from Analyzer
- [x] Remove `keywords` vector from Analyzer
- [x] Remove `keywordMetadata` map from Analyzer
- [x] Get keywords from compiler API (`getKeywordInfo()`)
- [x] Get stdlib info from compiler API

### 7.2 Implement DocumentCache
- [x] Add `DocumentCache { ast, symbols, parseErrors, semanticErrors, lastAnalysisMs, version }`
- [x] Add `std::map<std::string, DocumentCache> documentCaches_`
- [x] Add `std::map<std::string, DocumentCache> lastGoodCaches_` for error resilience
- [x] Implement cache invalidation on document change

### 7.3 Implement adaptive throttling
- [x] Track `lastAnalysisMs` per document
- [x] Scale debounce delay to `max(50ms, 2 * lastAnalysisMs)`
- [x] Skip intermediate changes during rapid typing

### 7.4 Wire compiler API
- [x] Use `analyzeForLSP()` for document analysis
- [x] Get keyword completions from `getKeywordInfo()`
- [x] Math intrinsics show function signatures in hover

### 7.5 Error region handling
- [x] Use `lastGoodCache_` when current analysis has errors
- [x] Limit suggestions to known context

---

## Phase 8: Incremental Sync and File Watching ✅

### 8.1 Enable incremental document sync
- [x] Change `TextDocumentSyncKind` to `Incremental` (2) in `handleInitialize()`
- [x] Parse `contentChanges[].range` in `handleDidChange()`
- [x] Apply incremental changes via `DocumentManager::applyIncrementalChange()`
- [x] Fall back to full parse if range unavailable

### 8.2 Add stdlib file watching
- [x] Register `workspace/didChangeWatchedFiles` capability in `handleInitialize()`
- [x] Add glob pattern `**/stdlib/**/*.maxon`
- [x] Add `handleDidChangeWatchedFiles()` handler
- [x] On stdlib change: call `analyzer->reloadStdlib()`, invalidate all document caches

---

## Phase 9: Testing and Validation ✅

### 9.1 Unit tests
- [x] Test `KeywordMatcher::getLSPKeywordInfo()` returns all keywords with metadata
- [x] Test `SourceRange::contains()` and `overlaps()`
- [x] Test parser error recovery produces partial AST
- [x] Test incremental document sync (10 new tests in DocumentManager)
- [x] Test incremental parsing preserves unaffected nodes
- [x] Test semantic caching returns correct results

### 9.2 Integration tests
- [x] Test completion returns keywords from compiler API
- [x] Test hover shows documentation from `KeywordEntry`
- [x] Test completions work inside error regions (limited to known context)
- [x] Test stdlib file change triggers reload
- [x] Test adaptive throttling scales with file size

### 9.3 Performance validation
- [x] Incremental parsing infrastructure in place
- [x] Semantic cache with dirty function tracking
- [x] Throttle delay adapts to analysis time

---

## Implementation Order

1. **Phase 1** - Single source of truth (blocking: enables all other phases)
2. **Phase 2** - Source ranges (blocking: needed for incremental parsing)
3. **Phase 3** - Error recovery (parallel with Phase 2)
4. **Phase 5.1-5.2** - Compiler API types (blocking: needed for LSP refactor)
5. **Phase 7.1-7.4** - LSP refactor core (main deliverable)
6. **Phase 4** - Incremental parsing (enhancement)
7. **Phase 6** - Semantic caching (enhancement)
8. **Phase 8** - Incremental sync and watching (enhancement)
9. **Phase 9** - Testing (throughout)

---

## Success Criteria

1. **No hardcoded language info in LSP**: All keyword/type info comes from compiler
2. **Single source of truth**: Adding a keyword requires providing LSP metadata (compile error otherwise)
3. **Graceful degradation**: Completions work on broken code using last-known-good analysis
4. **Responsive editing**: Throttling adapts to file size, small files feel instant
5. **Stdlib changes detected**: Modifying stdlib triggers re-analysis of open documents
