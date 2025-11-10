# Installation Checklist - Maxon LSP

Use this checklist to ensure proper installation of the Maxon Language Server.

## Prerequisites

- [ ] Windows 10/11
- [ ] Visual Studio 2022 (with C++ Desktop Development)
- [ ] CMake 3.15 or higher (`cmake --version`)
- [ ] Node.js 14+ and npm (`node --version`)
- [ ] VS Code 1.75+ installed
- [ ] PowerShell available

## Build Steps

### C++ LSP Server

- [ ] Navigate to `lsp/` directory
- [ ] Download nlohmann/json:
  ```powershell
  Invoke-WebRequest -Uri "https://github.com/nlohmann/json/releases/download/v3.11.3/json.hpp" -OutFile "include/json.hpp"
  ```
- [ ] Verify json.hpp exists: `Test-Path include/json.hpp`
- [ ] Create build directory: `mkdir build`
- [ ] Configure CMake: `cd build; cmake .. -G "Visual Studio 17 2022"`
- [ ] Build: `cmake --build . --config Release`
- [ ] Verify executable exists: `Test-Path build/Release/maxon-lsp.exe`
- [ ] Test server starts: `.\build\Release\maxon-lsp.exe` (Ctrl+C to exit)

### VS Code Extension

- [ ] Navigate to `lsp/vscode-extension/`
- [ ] Install dependencies: `npm install`
- [ ] Verify no npm errors
- [ ] Compile TypeScript: `npm run compile`
- [ ] Verify output exists: `Test-Path out/extension.js`
- [ ] Check for compilation errors (should be none)

### Server Path Configuration

- [ ] Open `vscode-extension/src/extension.ts`
- [ ] Verify `serverExecutable` path points to:
  - Windows: `../build/Release/maxon-lsp.exe`
  - Or adjust to your actual build location
- [ ] Save changes
- [ ] Recompile if changed: `npm run compile`

## Testing

### Launch Extension Development Host

- [ ] Open `lsp/vscode-extension/` folder in VS Code
- [ ] Press F5 (or Run → Start Debugging)
- [ ] New VS Code window opens (Extension Development Host)
- [ ] No errors in Debug Console

### Test File

- [ ] In Extension Development Host, open `lsp/test.maxon`
- [ ] Or create new file: `test.maxon`
- [ ] Paste sample code:
  ```maxon
  function main() int
      var x = 5
      return x
  end
  ```

### Feature Tests

- [ ] **Syntax Highlighting**
  - Keywords (`function`, `var`, `return`, `int`) are colored
  - Numbers (`5`) are colored
  - Comments with `//` are colored

- [ ] **Diagnostics**
  - Create error: change `function` to `functon`
  - Red squiggle appears
  - Error shows in Problems panel (View → Problems)
  - Fix error, squiggle disappears

- [ ] **Code Completion**
  - Type `func` then Ctrl+Space
  - Completion list appears with `function`
  - Select and insert works
  - Type `var` then Ctrl+Space
  - See `var` in completions

- [ ] **Hover**
  - Hover mouse over `function` keyword
  - Tooltip appears with information
  - Hover over `int` type
  - See type information

- [ ] **Go to Definition**
  - Add: `var y = x` after `var x = 5`
  - Place cursor on second `x`
  - Press F12
  - Cursor jumps to `var x = 5` declaration

- [ ] **Document Symbols**
  - Press Ctrl+Shift+O
  - Outline shows: `main` function
  - Shows variables: `x`
  - Can click to navigate

- [ ] **Comments**
  - Select a line
  - Press Ctrl+/
  - Line becomes comment: `// ...`
  - Press Ctrl+/ again
  - Comment removed

- [ ] **Auto-closing**
  - Type `(`
  - Closing `)` auto-inserted
  - Type `"`
  - Closing `"` auto-inserted

- [ ] **Folding**
  - Click arrow next to `function`
  - Function body folds
  - Click again to unfold

## Output Panel Checks

- [ ] Open Output panel: View → Output
- [ ] Select "Maxon Language Server" from dropdown
- [ ] See "Maxon LSP Server starting..."
- [ ] See server initialization messages
- [ ] No error messages (warnings OK)

## Troubleshooting Checks

If anything fails:

- [ ] Check all prerequisites installed
- [ ] Run `.\build.ps1` from `lsp/` directory
- [ ] Check for build errors
- [ ] Verify all paths are correct
- [ ] Check Output panel for errors
- [ ] Try "Reload Window" (Ctrl+Shift+P)
- [ ] Consult TROUBLESHOOTING.md

## Package Extension (Optional)

For permanent installation:

- [ ] Install vsce: `npm install -g @vscode/vsce`
- [ ] Navigate to `vscode-extension/`
- [ ] Package: `vsce package`
- [ ] Creates `maxon-lsp-client-0.1.0.vsix`
- [ ] Install: `code --install-extension maxon-lsp-client-0.1.0.vsix`
- [ ] Restart VS Code
- [ ] Extension now available for all workspaces
- [ ] Open any `.maxon` file to activate

## Final Verification

- [ ] All checkboxes above are checked ✓
- [ ] No errors in any step
- [ ] All LSP features work
- [ ] Can edit `.maxon` files comfortably
- [ ] Ready to use for Maxon development!

## Success! 🎉

If all items are checked, your Maxon LSP is fully installed and functional!

You can now:
- Write Maxon code with syntax highlighting
- Get real-time error detection
- Use code completion
- Navigate code with go-to-definition
- View document structure

Enjoy your enhanced Maxon development experience!

---

**Need Help?**
- See README.md for overview
- See QUICKSTART.md for detailed steps
- See TROUBLESHOOTING.md for common issues
- See ARCHITECTURE.md for technical details
