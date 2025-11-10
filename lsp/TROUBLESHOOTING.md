# Troubleshooting Guide - Maxon LSP

## Common Issues and Solutions

### Build Issues

#### Issue: CMake can't find compiler
```
Error: No CMAKE_CXX_COMPILER could be found
```

**Solution:**
- Install Visual Studio 2022 with C++ Desktop Development workload
- Or specify compiler manually:
  ```powershell
  cmake .. -G "Visual Studio 17 2022" -A x64
  ```

#### Issue: json.hpp not found
```
fatal error: json.hpp: No such file or directory
```

**Solution:**
Download nlohmann/json:
```powershell
cd lsp
Invoke-WebRequest -Uri "https://github.com/nlohmann/json/releases/download/v3.11.3/json.hpp" -OutFile "include/json.hpp"
```

#### Issue: Can't find lexer.h or parser.h
```
fatal error: lexer.h: No such file or directory
```

**Solution:**
- Verify that `maxon-bin/` directory exists at same level as `lsp/`
- Check CMakeLists.txt includes correct path:
  ```cmake
  include_directories(${CMAKE_CURRENT_SOURCE_DIR}/../maxon-bin)
  ```

### Extension Issues

#### Issue: Extension doesn't activate
**Symptoms:** No syntax highlighting, no LSP features

**Solution:**
1. Check file extension is `.maxon`
2. Check Output panel: View → Output → Select "Maxon Language Server"
3. Reload window: Ctrl+Shift+P → "Reload Window"
4. Check extension is in list: Ctrl+Shift+X → Search "Maxon"

#### Issue: "Cannot find module 'vscode'"
**Solution:**
```bash
cd lsp/vscode-extension
npm install
npm run compile
```

#### Issue: LSP server not found
```
Error: spawn ENOENT
```

**Solution:**
Edit `vscode-extension/src/extension.ts`:
```typescript
const serverExecutable = path.join(
    context.extensionPath, 
    '..',
    'build',
    'Release',  // or 'Debug'
    'maxon-lsp.exe'
);
```

Verify the path exists:
```powershell
Test-Path "lsp\build\Release\maxon-lsp.exe"
```

### Runtime Issues

#### Issue: Server starts but no features work
**Solution:**
1. Check server logs in Output panel
2. Test with simple file:
   ```maxon
   function main() int
       var x = 5
       return x
   end
   ```
3. Check for errors in Problems panel

#### Issue: Completions not showing
**Symptoms:** Ctrl+Space does nothing

**Solution:**
1. Check server is running (Output panel)
2. Try typing a keyword: `func` then Ctrl+Space
3. Verify completion provider is registered:
   - Check extension.ts has correct language ID: `maxon`

#### Issue: Syntax highlighting wrong colors
**Solution:**
1. Check VS Code theme
2. Verify `maxon.tmLanguage.json` is loaded:
   - Open Command Palette
   - Type "Developer: Inspect Editor Tokens and Scopes"
   - Hover over token to see scope
3. Ensure scopes match grammar:
   - `keyword.control.maxon`
   - `storage.type.maxon`
   - etc.

#### Issue: Diagnostics not appearing
**Symptoms:** Syntax errors not underlined

**Solution:**
1. Verify server is analyzing:
   ```cpp
   // In lsp_server.cpp handleDidChange
   auto diagnostics = analyzer->analyze(doc);
   publishDiagnostics(uri, diagnostics);
   ```
2. Check Lexer/Parser are working:
   - Create intentional error: `functin main()`
   - Should show error in Problems panel
3. Check Output panel for exceptions

### Performance Issues

#### Issue: High CPU usage
**Solution:**
- LSP analyzes on every keystroke
- For large files, consider incremental parsing
- Check for infinite loops in analyzer

#### Issue: Slow completions
**Solution:**
- Reduce completion items
- Cache lexer results
- Use incremental tokenization

### Debugging Tips

#### Enable verbose logging
In `extension.ts`:
```typescript
const clientOptions: LanguageClientOptions = {
    // ... existing config
    outputChannel: window.createOutputChannel('Maxon LSP'),
    traceOutputChannel: window.createOutputChannel('Maxon LSP Trace'),
};
```

#### Debug C++ server
Add logging to `lsp_server.cpp`:
```cpp
std::cerr << "Processing request: " << method << std::endl;
std::cerr << "Document URI: " << uri << std::endl;
```

Check stderr in Output panel.

#### Debug TypeScript extension
1. Open `vscode-extension` folder
2. Set breakpoints in `extension.ts`
3. Press F5
4. Extension runs in debug mode

#### Test JSON-RPC manually
```powershell
# Start server manually
cd lsp\build\Release
.\maxon-lsp.exe

# Send initialize request
Content-Length: 123

{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}
```

### Platform-Specific Issues

#### Windows: maxon-lsp.exe blocked by antivirus
**Solution:**
- Add exception for build directory
- Or use Debug build instead of Release

#### Windows: Permission denied
**Solution:**
```powershell
# Run PowerShell as Administrator
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

#### Path separators
**Solution:**
Use CMake's path handling:
```cpp
path.join(...) // Good
"..\\path\\to" // Avoid (Windows-only)
```

### Verification Steps

Run these to verify installation:

```powershell
# 1. Check server exists
Test-Path "lsp\build\Release\maxon-lsp.exe"

# 2. Check extension compiled
Test-Path "lsp\vscode-extension\out\extension.js"

# 3. Check json.hpp exists
Test-Path "lsp\include\json.hpp"

# 4. Test server starts
cd lsp\build\Release
.\maxon-lsp.exe
# Should start and wait for input (Ctrl+C to exit)

# 5. Check extension files
Get-ChildItem "lsp\vscode-extension\syntaxes"
# Should show maxon.tmLanguage.json
```

### Getting Help

If issues persist:

1. **Check logs:**
   - VS Code Output panel
   - stderr from maxon-lsp.exe
   - VS Code Developer Tools (Help → Toggle Developer Tools)

2. **Minimal reproduction:**
   - Create minimal .maxon file
   - Test each feature individually
   - Note exact error messages

3. **Verify environment:**
   - CMake version: `cmake --version`
   - Node version: `node --version`
   - VS Code version: Check Help → About

4. **Clean rebuild:**
   ```powershell
   # Clean C++
   Remove-Item -Recurse -Force lsp\build
   
   # Clean TypeScript
   Remove-Item -Recurse -Force lsp\vscode-extension\out
   Remove-Item -Recurse -Force lsp\vscode-extension\node_modules
   
   # Rebuild
   cd lsp
   .\build.ps1
   ```

### Known Limitations

- Full document sync (not incremental)
- No multi-file analysis
- Basic symbol resolution
- No type inference
- No workspace symbols
- Windows-focused (portable with minor changes)

### Success Indicators

✅ Server compiles without warnings
✅ Extension loads without errors
✅ .maxon files get syntax highlighting
✅ Typing creates no crashes
✅ Completions appear
✅ Hover shows content
✅ F12 works for simple cases
✅ Syntax errors show in Problems panel

If all checked, LSP is working correctly! 🎉
