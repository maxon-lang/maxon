# Maxon LSP Architecture

## Component Overview

```
┌─────────────────────────────────────────────────────────┐
│                    VS Code Editor                        │
│  ┌────────────────────────────────────────────────┐    │
│  │  User edits .maxon file                         │    │
│  │  - Typing code                                  │    │
│  │  - Requesting completions (Ctrl+Space)          │    │
│  │  - Hovering over symbols                        │    │
│  │  - Go to definition (F12)                       │    │
│  └────────────────┬───────────────────────────────┘    │
└───────────────────┼──────────────────────────────────────┘
                    │
                    │ LSP Protocol (JSON-RPC)
                    ▼
┌─────────────────────────────────────────────────────────┐
│         VS Code Extension (TypeScript)                   │
│  ┌────────────────────────────────────────────────┐    │
│  │  extension.ts                                   │    │
│  │  - Starts LSP client                            │    │
│  │  - Manages connection to server                 │    │
│  │  - Routes requests/notifications                │    │
│  └────────────────┬───────────────────────────────┘    │
└───────────────────┼──────────────────────────────────────┘
                    │
                    │ stdio (stdin/stdout)
                    │ Content-Length: NNN\r\n\r\n{json}
                    ▼
┌─────────────────────────────────────────────────────────┐
│         C++ LSP Server (maxon-lsp.exe)                   │
│  ┌────────────────────────────────────────────────┐    │
│  │  main.cpp                                       │    │
│  │  └─→ LspServer                                  │    │
│  │      ├─→ JsonRpcHandler                         │    │
│  │      │   └─→ Reads/writes JSON-RPC messages     │    │
│  │      │                                           │    │
│  │      ├─→ DocumentManager                        │    │
│  │      │   └─→ Tracks open documents              │    │
│  │      │                                           │    │
│  │      └─→ Analyzer                                │    │
│  │          ├─→ analyze() - diagnostics            │    │
│  │          ├─→ getCompletions()                   │    │
│  │          ├─→ getHover()                         │    │
│  │          ├─→ getDefinition()                    │    │
│  │          └─→ getSymbols()                       │    │
│  └────────────────┬───────────────────────────────┘    │
└───────────────────┼──────────────────────────────────────┘
                    │
                    │ Uses existing compiler components
                    ▼
┌─────────────────────────────────────────────────────────┐
│         Maxon Compiler (maxon-bin/)                      │
│  ┌────────────────────────────────────────────────┐    │
│  │  Lexer                                          │    │
│  │  ├─→ Tokenizes source code                      │    │
│  │  └─→ Returns vector<Token>                      │    │
│  │                                                  │    │
│  │  Parser                                         │    │
│  │  ├─→ Builds AST from tokens                     │    │
│  │  └─→ Returns ProgramAST                         │    │
│  │                                                  │    │
│  │  AST Nodes                                      │    │
│  │  └─→ FunctionAST, VarDeclAST, etc.             │    │
│  └─────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────┘
```

## Request Flow Example: Code Completion

```
1. User types "var x = " and presses Ctrl+Space

2. VS Code → Extension:
   "Give me completions at line 5, character 8"

3. Extension → LSP Server (JSON-RPC):
   {
     "jsonrpc": "2.0",
     "id": 1,
     "method": "textDocument/completion",
     "params": {
       "textDocument": {"uri": "file:///path/to/file.maxon"},
       "position": {"line": 5, "character": 8}
     }
   }

4. LSP Server:
   a) DocumentManager gets the document
   b) Analyzer.getCompletions():
      - Runs Lexer on document
      - Extracts all identifiers
      - Adds keywords (function, var, if, etc.)
      - Returns completion items

5. LSP Server → Extension (JSON-RPC):
   {
     "jsonrpc": "2.0",
     "id": 1,
     "result": [
       {"label": "function", "kind": 14},
       {"label": "var", "kind": 14},
       {"label": "x", "kind": 6},
       ...
     ]
   }

6. Extension → VS Code:
   Displays completion popup with suggestions
```

## Key LSP Methods Implemented

### Requests (client → server → response)
- `initialize` - Setup server capabilities
- `textDocument/completion` - Code completion
- `textDocument/hover` - Hover information
- `textDocument/definition` - Go to definition
- `textDocument/documentSymbol` - Document outline

### Notifications (one-way messages)
- `initialized` - Server ready
- `textDocument/didOpen` - File opened
- `textDocument/didChange` - File modified
- `textDocument/didSave` - File saved
- `textDocument/didClose` - File closed
- `textDocument/publishDiagnostics` - Send errors/warnings

## File Organization

```
lsp/
├── include/              # C++ Headers
│   ├── json.hpp          # JSON library
│   ├── lsp_types.h       # LSP data structures
│   ├── json_rpc.h        # Protocol handler
│   ├── document_manager.h
│   ├── analyzer.h
│   └── lsp_server.h
│
├── src/                  # C++ Implementation
│   ├── main.cpp          # Entry point
│   ├── json_rpc.cpp      # Read/write JSON-RPC
│   ├── document_manager.cpp
│   ├── analyzer.cpp      # Core language intelligence
│   └── lsp_server.cpp    # Request routing
│
├── vscode-extension/     # VS Code Extension
│   ├── src/
│   │   └── extension.ts  # Extension entry point
│   ├── syntaxes/
│   │   └── maxon.tmLanguage.json  # Syntax highlighting
│   ├── language-configuration.json # Brackets, comments
│   └── package.json      # Extension manifest
│
└── build/                # Build output
    └── maxon-lsp.exe     # Compiled server
```

## Data Flow: Diagnostics

```
1. User opens or edits file.maxon

2. Extension sends didOpen/didChange notification

3. LSP Server:
   DocumentManager.updateDocument(uri, text)
   
4. LSP Server:
   diagnostics = Analyzer.analyze(document)
   └─→ Lexer.tokenize(text)
       └─→ Check for UNKNOWN tokens (errors)
   └─→ Parser.parse(tokens)
       └─→ Catch syntax errors
   
5. LSP Server sends notification:
   textDocument/publishDiagnostics
   {
     "uri": "file:///path/to/file.maxon",
     "diagnostics": [
       {
         "range": {...},
         "severity": 1,
         "message": "Parse error: unexpected token"
       }
     ]
   }

6. VS Code displays red squiggles under errors
```

## Dependencies

### C++ LSP Server
- **nlohmann/json**: JSON parsing/generation
- **Maxon lexer/parser**: Language analysis
- **STL**: Standard library (string, vector, map, etc.)

### VS Code Extension
- **vscode**: VS Code API types
- **vscode-languageclient**: LSP client library
- **TypeScript**: Extension language

## Communication Protocol

All messages use JSON-RPC 2.0 over stdio:

```
Content-Length: 123\r\n
\r\n
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{...}}
```

The server reads from stdin and writes to stdout.
Logs/errors go to stderr.
