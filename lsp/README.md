# Maxon Language Server Protocol (LSP)

A complete Language Server Protocol implementation for the Maxon programming language, written in C++.

## Features

- **Syntax Highlighting**: Full TextMate grammar for Maxon syntax
- **Diagnostics**: Real-time error and warning detection
- **Code Completion**: Intelligent suggestions for keywords, types, and identifiers
- **Hover Information**: Type and documentation information on hover
- **Go to Definition**: Jump to variable and function declarations
- **Document Symbols**: Outline view of functions and variables
- **Comment Support**: Line comments with `//`

## Project Structure

```
lsp/
├── include/           # Header files
│   ├── analyzer.h     # Semantic analysis
│   ├── document_manager.h
│   ├── json_rpc.h     # JSON-RPC protocol
│   ├── lsp_server.h   # Main LSP server
│   ├── lsp_types.h    # LSP data structures
│   └── json.hpp       # JSON library (placeholder)
├── src/               # Implementation files
│   ├── analyzer.cpp
│   ├── document_manager.cpp
│   ├── json_rpc.cpp
│   ├── lsp_server.cpp
│   └── main.cpp
├── vscode-extension/  # VS Code extension
│   ├── src/
│   │   └── extension.ts
│   ├── syntaxes/
│   │   └── maxon.tmLanguage.json
│   ├── language-configuration.json
│   ├── package.json
│   └── tsconfig.json
├── build/             # Build output directory
└── CMakeLists.txt     # CMake configuration
```

## Building the LSP Server

### Prerequisites

- CMake 3.15 or higher
- C++17 compatible compiler (GCC, Clang, MSVC)
- [nlohmann/json](https://github.com/nlohmann/json) library

### Step 1: Install nlohmann/json

Download the single-header library:
```bash
# Download json.hpp
curl -o include/json.hpp https://github.com/nlohmann/json/releases/download/v3.11.3/json.hpp
```

Or on Windows with PowerShell:
```powershell
Invoke-WebRequest -Uri "https://github.com/nlohmann/json/releases/download/v3.11.3/json.hpp" -OutFile "include/json.hpp"
```

### Step 2: Build the Server

#### On Windows:
```powershell
# From the lsp directory
mkdir build
cd build
cmake .. -G "Visual Studio 17 2022"  # Or your VS version
cmake --build . --config Release
```

#### On Linux/Mac:
```bash
# From the lsp directory
mkdir build
cd build
cmake ..
make
```

The compiled executable will be at:
- Windows: `build/Release/maxon-lsp.exe`
- Linux/Mac: `build/maxon-lsp`

## Installing the VS Code Extension

### Step 1: Install Dependencies

```bash
cd vscode-extension
npm install
```

### Step 2: Compile TypeScript

```bash
npm run compile
```

### Step 3: Update Server Path

Edit `vscode-extension/src/extension.ts` and update the `serverExecutable` path to point to your compiled LSP server.

### Step 4: Install Extension

#### Option A: Development Mode
1. Open the `vscode-extension` folder in VS Code
2. Press F5 to launch Extension Development Host
3. Open a `.maxon` file to test

#### Option B: Package and Install
```bash
# Install vsce if you don't have it
npm install -g @vscode/vsce

# Package the extension
vsce package

# Install the .vsix file
code --install-extension maxon-lsp-client-0.1.0.vsix
```

## Usage

1. Open a `.maxon` file in VS Code
2. The LSP server will automatically start
3. You'll see:
   - Syntax highlighting
   - Real-time diagnostics (errors/warnings)
   - Code completion (Ctrl+Space)
   - Hover information (mouse over symbols)
   - Go to Definition (F12)
   - Document outline (Ctrl+Shift+O)

## LSP Capabilities

### Implemented Features

- ✅ `textDocument/completion` - Code completion
- ✅ `textDocument/hover` - Hover information
- ✅ `textDocument/definition` - Go to definition
- ✅ `textDocument/documentSymbol` - Document symbols
- ✅ `textDocument/publishDiagnostics` - Error reporting

### Future Enhancements

- ❌ Code formatting
- ❌ Find references
- ❌ Rename symbol
- ❌ Code actions/quick fixes
- ❌ Signature help

## Maxon Language Keywords

- `function` - Function declaration
- `var` - Variable declaration
- `if/else/end` - Conditional statements
- `while/end` - Loop statements
- `return` - Return statement
- `int` - Integer type
- `string` - String type

## Example Maxon Code

```maxon
function main() int
    var x = 5
    var i = 3
    
    while i > 0
        if i = 3
            x = x + 5
        else
            x = x + 2
        end
        i = i - 1
    end
    
    return x
end
```

## Troubleshooting

### LSP Server Not Starting

1. Check that the server executable path in `extension.ts` is correct
2. Ensure the server was compiled successfully
3. Check VS Code's Output panel (select "Maxon Language Server" from dropdown)

### No Syntax Highlighting

1. Verify the `.maxon` file extension is recognized
2. Check that `maxon.tmLanguage.json` is properly formatted
3. Reload VS Code window (Ctrl+Shift+P → "Reload Window")

### Build Errors

1. Ensure nlohmann/json is properly installed
2. Check that the maxon-bin lexer/parser files are accessible
3. Verify C++17 compiler support

## Contributing

The LSP server integrates with the existing Maxon compiler infrastructure:
- `../maxon-bin/lexer.h` - Tokenization
- `../maxon-bin/parser.h` - AST generation
- `../maxon-bin/ast.h` - AST node definitions

## License

MIT License
