# Line Ending Configuration

The Maxon project enforces **LF (Unix-style)** line endings for all source files, including on Windows. This ensures consistency across platforms and prevents formatting issues.

## Git Configuration

The repository includes a [.gitattributes](../.gitattributes) file that automatically enforces LF line endings for all text files. Git will:

- Store all text files with LF in the repository
- Convert line endings appropriately on checkout based on your platform
- Ensure Maxon source files (`.maxon`) always use LF

## Editor Configuration

The [.editorconfig](../.editorconfig) file configures most modern editors to use LF line endings automatically. Supported editors include:

- Visual Studio Code
- Visual Studio
- IntelliJ IDEA / CLion
- Sublime Text
- Atom
- And many others

## VSCode Extension

The Maxon VSCode extension automatically enforces LF line endings when formatting. No additional configuration needed.

## For Windows Developers

### One-Time Setup

If you're on Windows and want to work with LF line endings everywhere (recommended), run this once:

```powershell
git config --global core.autocrlf input
```

This tells Git to:
- Convert CRLF → LF when committing (input)
- Leave LF as-is when checking out (no conversion)

### After Cloning

If you cloned the repository before these configuration files were added, refresh your working directory:

```powershell
# Remove all files from Git's index (but keep them in your working directory)
git rm --cached -r .

# Restore files with proper line endings
git reset --hard
```

## Verification

To check if files have correct line endings:

### On Windows (PowerShell)
```powershell
# Should show "LF" for Maxon files
Get-Content examples\nbody.maxon -Raw | Select-String -Pattern "`r`n" -Quiet
# Returns nothing if using LF (correct)
```

### On Linux/Mac
```bash
# Should show only LF
file examples/nbody.maxon
# Should output: "ASCII text" (not "ASCII text, with CRLF line terminators")
```

## Why LF?

1. **Cross-platform compatibility**: LF works everywhere (Unix, Linux, macOS, Windows)
2. **Tool compatibility**: Many build tools and compilers expect LF
3. **Git diffs**: CRLF causes unnecessary diff noise
4. **Consistency**: The Maxon formatter enforces LF, so source files should match

## Troubleshooting

### Git shows all files as modified after setup

Run the refresh commands above:
```powershell
git rm --cached -r .
git reset --hard
```

### VSCode keeps converting to CRLF

Check your VSCode settings and ensure:
```json
{
  "[maxon]": {
    "files.eol": "\n"
  }
}
```

The Maxon extension should handle this automatically.

### Makefile errors on Windows

Makefiles **must** use LF even on Windows. The `.gitattributes` file enforces this. If you get errors, check the line endings with `file Makefile`.
