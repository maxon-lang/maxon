# 🚀 START HERE - Maxon LSP Quick Setup

## What is This?

A complete **Language Server Protocol (LSP)** implementation for the Maxon programming language. This adds VS Code IDE features like:

- ✨ Syntax highlighting
- 🔍 Code completion (Ctrl+Space)
- 📝 Hover documentation
- 🎯 Go to definition (F12)
- ⚠️ Real-time error detection
- 📋 Document outline (Ctrl+Shift+O)

## Quick Start (5 Minutes)

### Option 1: Automated Build (Recommended)

```powershell
cd lsp
.\build.ps1
```

This will:
1. Download required JSON library
2. Build the C++ LSP server
3. Build the VS Code extension

### Option 2: Step by Step

```powershell
# 1. Download JSON library
cd lsp
Invoke-WebRequest -Uri "https://github.com/nlohmann/json/releases/download/v3.11.3/json.hpp" -OutFile "include/json.hpp"

# 2. Build C++ server
mkdir build; cd build
cmake .. -G "Visual Studio 17 2022"
cmake --build . --config Release
cd ..

# 3. Build VS Code extension
cd vscode-extension
npm install
npm run compile
cd ..
```

## Test It

1. Open `lsp/vscode-extension/` in VS Code
2. Press **F5** (launches Extension Development Host)
3. Open `lsp/test.maxon` in the new window
4. Try the features:
   - Type `func` + Ctrl+Space → see completions
   - Hover over keywords → see docs
   - Press F12 on a variable → jump to definition
   - Make a syntax error → see red squiggle

## Requirements

- Windows 10/11
- Visual Studio 2022 (C++ Desktop Development)
- CMake 3.15+
- Node.js 14+
- VS Code 1.75+

## Files Created

```
lsp/
├── C++ Server (src/, include/)      # The LSP server
├── VS Code Extension (vscode-extension/) # The client
├── Documentation (*.md files)       # Guides
└── test.maxon                       # Test file
```

## What Each File Does

| File | Purpose |
|------|---------|
| `build.ps1` | Automated build script - **RUN THIS FIRST** |
| `README.md` | Complete documentation |
| `QUICKSTART.md` | Detailed setup guide |
| `INSTALL_CHECKLIST.md` | Step-by-step checklist |
| `ARCHITECTURE.md` | Technical architecture |
| `TROUBLESHOOTING.md` | Fix common issues |
| `test.maxon` | Test all LSP features |

## Troubleshooting

### Build fails?
```powershell
# Ensure Visual Studio 2022 is installed with C++ tools
# Verify CMake is in PATH: cmake --version
```

### Extension won't start?
```powershell
# Check server path in vscode-extension/src/extension.ts
# Verify build/Release/maxon-lsp.exe exists
```

### Features not working?
- Check Output panel: View → Output → "Maxon Language Server"
- Try: Ctrl+Shift+P → "Reload Window"
- See TROUBLESHOOTING.md for detailed help

## Next Steps

1. ✅ **Build** - Run `build.ps1`
2. ✅ **Test** - Press F5 and try features
3. ✅ **Use** - Start coding in Maxon with LSP support!

## Documentation Guide

- **New to LSP?** → Start with `README.md`
- **Want to build now?** → Use `build.ps1` or `QUICKSTART.md`
- **Step-by-step?** → Follow `INSTALL_CHECKLIST.md`
- **Having issues?** → Check `TROUBLESHOOTING.md`
- **How does it work?** → Read `ARCHITECTURE.md`

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

With LSP, you get:
- Keywords highlighted
- Errors shown immediately
- Completions as you type
- Hover for info
- F12 to jump to definitions

## Support

| Question | Answer |
|----------|--------|
| How to build? | Run `build.ps1` or see `QUICKSTART.md` |
| How to test? | Open extension folder, press F5 |
| How to debug? | See Output panel in VS Code |
| How does it work? | See `ARCHITECTURE.md` |
| Build errors? | See `TROUBLESHOOTING.md` |
| What features? | See `README.md` |

## Project Structure

```
lsp/
├── include/          → C++ headers
├── src/             → C++ implementation  
├── vscode-extension/ → VS Code extension
│   ├── src/         → TypeScript code
│   └── syntaxes/    → Syntax highlighting
├── build/           → Build output (created)
└── *.md             → Documentation
```

## Summary

This is a **complete, production-ready LSP** for Maxon that:
- Integrates with existing Maxon compiler (lexer/parser)
- Provides modern IDE features in VS Code
- Is well-documented and maintainable
- Can be extended with more features

**Total:** ~1,500 lines of C++, ~200 lines TypeScript, comprehensive docs

---

## 🎯 Ready? Let's Go!

```powershell
cd lsp
.\build.ps1
```

Then press **F5** in VS Code to test!

**Happy Coding! 🚀**
