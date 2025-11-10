# Quick Start Guide - Maxon LSP

This guide will help you build and install the Maxon Language Server.

## Windows Quick Start

1. **Run the build script:**
   ```powershell
   cd lsp
   .\build.ps1
   ```

2. **Test in VS Code:**
   - Open the `lsp/vscode-extension` folder in VS Code
   - Press `F5` to launch Extension Development Host
   - Open or create a `.maxon` file
   - Test the features (completion, hover, etc.)

## Manual Build Steps

### Building the C++ LSP Server

1. **Download nlohmann/json:**
   ```powershell
   # PowerShell
   Invoke-WebRequest -Uri "https://github.com/nlohmann/json/releases/download/v3.11.3/json.hpp" -OutFile "lsp/include/json.hpp"
   ```

2. **Configure and build:**
   ```powershell
   cd lsp
   mkdir build
   cd build
   cmake .. -G "Visual Studio 17 2022"
   cmake --build . --config Release
   ```

3. **Verify the build:**
   - Check that `build/Release/maxon-lsp.exe` exists

### Building the VS Code Extension

1. **Install dependencies:**
   ```bash
   cd lsp/vscode-extension
   npm install
   ```

2. **Compile TypeScript:**
   ```bash
   npm run compile
   ```

3. **Update server path (if needed):**
   - Edit `src/extension.ts`
   - Update `serverExecutable` path to point to your compiled `maxon-lsp.exe`

## Testing

Create a test file `test.maxon`:

```maxon
function main() int
    var x = 5
    var y = 10
    
    if x > 3
        y = y + x
    else
        y = y - x
    end
    
    return y
end
```

### Expected LSP Features:

1. **Syntax Highlighting** - Keywords, numbers, operators colored
2. **Diagnostics** - Syntax errors underlined in red
3. **Completion** - Type `var` and press Ctrl+Space
4. **Hover** - Hover over keywords to see documentation
5. **Go to Definition** - F12 on a variable/function name
6. **Outline** - Ctrl+Shift+O to see document symbols

## Troubleshooting

### Server won't start
- Check Output panel: View → Output → "Maxon Language Server"
- Verify `maxon-lsp.exe` path in `extension.ts`
- Ensure executable has proper permissions

### No syntax highlighting
- Verify `.maxon` file extension
- Reload window: Ctrl+Shift+P → "Reload Window"

### Build errors
- Ensure Visual Studio 2022 is installed
- Check CMake version (3.15+)
- Verify json.hpp was downloaded

## Next Steps

Once everything works:

1. **Package the extension:**
   ```bash
   npm install -g @vscode/vsce
   cd lsp/vscode-extension
   vsce package
   ```

2. **Install globally:**
   ```bash
   code --install-extension maxon-lsp-client-0.1.0.vsix
   ```

3. **Share with others** - The `.vsix` file can be distributed

## Architecture

```
User types in VS Code (.maxon file)
         ↓
VS Code Extension (TypeScript)
         ↓ JSON-RPC over stdio
C++ LSP Server (maxon-lsp.exe)
         ↓
Maxon Lexer/Parser (existing code)
         ↓
LSP Responses (diagnostics, completions, etc.)
```

## Resources

- [LSP Specification](https://microsoft.github.io/language-server-protocol/)
- [VS Code Extension API](https://code.visualstudio.com/api)
- [nlohmann/json](https://github.com/nlohmann/json)
