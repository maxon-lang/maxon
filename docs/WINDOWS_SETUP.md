# Windows Setup Guide

This guide covers Windows-specific setup requirements for building the Maxon compiler.

## Prerequisites

1. **Git Bash** - Required for running all build commands
   - Download from [git-scm.com](https://git-scm.com/downloads)
   - All `make` commands must be run in Git Bash (not PowerShell or CMD)

2. **Visual Studio 2022 with C++ Development Tools**
   - Required for the Windows SDK and DIA SDK
   - Install "Desktop development with C++" workload

## DIA SDK Path Fix

LLVM requires the Debug Interface Access (DIA) SDK for debugging support on Windows. However, LLVM looks for DIA SDK in the Visual Studio 2019 Professional path, which may not exist if you have Visual Studio 2022 Community installed.

### Symptoms
If you see this error during `make all`:
```
ninja: error: 'C:/Program Files (x86)/Microsoft Visual Studio/2019/Professional/DIA SDK/lib/amd64/diaguids.lib', needed by 'bin/maxon.exe', missing and no known rule to make it
```

### Solution

Run the included PowerShell script as Administrator:

1. Right-click on `fix-diasdk.ps1` in the project root
2. Select **"Run with PowerShell"**
3. When prompted, select **"Run as Administrator"**
4. The script will:
   - Verify you have admin privileges
   - Check that Visual Studio 2022 is installed
   - Create the VS2019 Professional directory structure
   - Copy `diaguids.lib` from VS2022 to the VS2019 path
   - Verify the file was copied successfully

### What the script does

The script copies the DIA SDK library from:
```
C:\Program Files\Microsoft Visual Studio\2022\Community\DIA SDK\lib\amd64\diaguids.lib
```

To:
```
C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\DIA SDK\lib\amd64\diaguids.lib
```

This is a one-time setup step. Once the file is copied, you won't need to run the script again unless you reinstall Visual Studio.

### Alternative: Manual Setup

If you prefer to do this manually, run these commands in an Administrator PowerShell:

```powershell
# Create directory
New-Item -ItemType Directory -Force -Path "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\DIA SDK\lib\amd64"

# Copy file
Copy-Item "C:\Program Files\Microsoft Visual Studio\2022\Community\DIA SDK\lib\amd64\diaguids.lib" `
          "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\DIA SDK\lib\amd64\diaguids.lib"
```

## Building

After completing the DIA SDK setup, you can build the project in Git Bash:

```bash
make all
```

This will:
1. Download LLVM (first time only)
2. Build the Maxon compiler
3. Build the LSP server
4. Build and install the VS Code extension

## Troubleshooting

### "LLVM not found" error
Make sure you ran `make all` or `make llvm` to download LLVM first.

### Permission errors with DIA SDK
The script must be run as Administrator because it writes to `C:\Program Files (x86)`.

### Build fails with "ninja: error"
Make sure you're running the build in Git Bash, not PowerShell or CMD.

## See Also

- [BUILD.md](../BUILD.md) - Cross-platform build instructions
- [LANGUAGE_REFERENCE.md](LANGUAGE_REFERENCE.md) - Maxon language documentation
