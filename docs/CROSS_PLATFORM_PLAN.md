# Cross-Platform Support for Maxon (Windows + Linux via Dev Container)

## Architecture Decisions

- **LLVM**: Download precompiled binaries (saves 30-40 min build time!)
- **Runtime**: Single runtime.ll with platform-specific generation
- **Executables**: Self-contained (no libc dependency)
- **Windows SDK**: Keep current usage (kernel32.lib, shell32.lib)
- **CI/CD**: GitHub Actions with LLVM caching
- **LSP/Compiler**: Share same LLVM build

## Phase 1: LLVM Download System (Integrated into Make)

### 1. Create `llvm-config.txt`
- LLVM version (e.g., `18.1.0`)
- Single source of truth

### 2. Create `scripts/download-llvm.sh`
- Read version from `llvm-config.txt`
- Check if already downloaded (skip if version matches)
- Platform detection:
  - Windows: Download from https://github.com/llvm/llvm-project/releases (LLVM-{version}-win64.exe or .zip)
  - Linux: Download Ubuntu pre-built binaries or use apt
- Extract to `./llvm-build/`
- Create version marker file
- Verify required components present (clang, lld, llc, includes, libs)

### 3. Update Makefile
- Add `llvm` target calling `scripts/download-llvm.sh`
- Make `llvm` prerequisite of `all`
- Set `LLVM_DIR = ./llvm-build`
- Update tool paths: `CC`, `CXX`, `LLC` use `$(LLVM_DIR)/bin/`
- Platform detection for `.exe` extension on Windows
- Add clean targets: `clean`, `clean-llvm`, `clean-all`

### 4. Update `.gitignore`
- Add `llvm-build/`

## Phase 2: Bash Scripts (Replace PowerShell)

### 5. Convert `run-all-tests.ps1` → `scripts/run-all-tests.sh`
- Port all test execution logic to bash
- Handle exit codes properly
- Update Makefile `test` target to call `.sh`
- Delete `.ps1` file

### 6. Convert `validate-specs.ps1` → `scripts/validate-specs.sh`
- Use `jq` for JSON parsing or python fallback
- Port validation logic
- Update Makefile to call `.sh`
- Delete `.ps1` file

### 7. Update Makefile bash usage
- Replace `powershell -Command` with bash commands
- Use `mkdir -p` for directory creation
- Use bash conditionals throughout

## Phase 3: CMake Build System Updates

### 8. Update `maxon-bin/CMakeLists.txt`
- Replace hardcoded `C:/Users/Eric/Dev/llvm-project/build` with `$ENV{LLVM_DIR}`
- Default `LLVM_DIR` to `${CMAKE_SOURCE_DIR}/../llvm-build`
- Platform detection: link `lldCOFF` on Windows, `lldELF` on Linux
- Make rc.exe optional (Windows only)

### 9. Update `lsp-server/CMakeLists.txt`
- Replace hardcoded LLVM paths with `$ENV{LLVM_DIR}`
- Same platform detection as maxon-bin
- Share LLVM build with compiler

### 10. Update `lsp-server/tests/CMakeLists.txt`
- Replace hardcoded LLVM paths with `$ENV{LLVM_DIR}`

### 11. Update `debugger-tests/CMakeLists.txt`
- Replace hardcoded LLVM paths with `$ENV{LLVM_DIR}`
- Existing platform detection for Windows/LLDB should work

### 12. Update `lsp-server/finalize_maxon_lsp_server.cmake`
- Platform detection: `taskkill` on Windows, `pkill` on Linux

## Phase 4: Compiler Cross-Platform Support

### 13. Update `maxon-bin/codegen/codegen_output.cpp`
- Add platform detection at compile time:
  ```cpp
  #ifdef _WIN32
  llvm::Triple targetTriple("x86_64-pc-windows-msvc");
  #else
  llvm::Triple targetTriple("x86_64-pc-linux-gnu");
  #endif
  ```
- Use `lld::coff::link` on Windows, `lld::elf::link` on Linux
- Platform-specific linker flags:
  - Windows: `/NOLOGO`, `/MACHINE:X64`, `/SUBSYSTEM:CONSOLE`, `/ENTRY:_start`, Windows SDK libs
  - Linux: `-o`, `--entry=_start`, no libc (self-contained)
- Ensure no libc calls on Linux (already self-contained via runtime.ll)

### 14. Update `maxon-runtime/runtime.ll` build
- Modify Makefile to generate platform-specific runtime:
  - Windows: target triple `x86_64-pc-windows-msvc`, include `@_fltused`
  - Linux: target triple `x86_64-pc-linux-gnu`, omit `@_fltused`
- Single source file, platform-specific generation via sed/script
- Output: `runtime.obj` (Windows) vs `runtime.o` (Linux)

## Phase 5: Dev Container Setup

### 15. Create `.devcontainer/devcontainer.json`
- Base image: Ubuntu 24.04 LTS
- Build from Dockerfile
- Post-create command: Run `make llvm` to download LLVM
- VS Code extensions: C/C++, Maxon language support
- Forward ports for debugging if needed

### 16. Create `.devcontainer/Dockerfile`
- Install build essentials: build-essential, cmake, ninja-build, git, bash
- Install .NET 8.0 SDK (for DocGen)
- Install Node.js 20+ and npm (for VS Code extension)
- Install jq (for JSON parsing in bash scripts)
- Install wget/curl (for downloading LLVM)
- Install python3 (may be needed for LLVM tools)

## Phase 6: GitHub Actions CI/CD

### 17. Create `.github/workflows/build-test.yml`
- Matrix build: Windows (latest) and Linux (Ubuntu 24.04)
- Steps:
  1. Checkout code
  2. Cache LLVM download (keyed by `llvm-config.txt` content)
  3. Install dependencies (platform-specific)
  4. Run `make all` (downloads LLVM if not cached, then builds maxon)
  5. Run `make test`
  6. Upload artifacts (maxon binaries)
- Windows: Use Git Bash for all steps
- Linux: Use native bash

### 18. Create `.github/workflows/validate-specs.yml`
- Run `scripts/validate-specs.sh`
- Ensure all test fragments have corresponding specs

## Phase 7: Documentation

### 19. Update `README.md`
- Prerequisites:
  - Windows: Git for Windows (Git Bash required), CMake, Ninja
  - Linux: Use dev container
- Quick start: `make all` (auto-downloads LLVM)
- LLVM update: Edit `llvm-config.txt`, run `make clean-llvm && make all`
- Link to detailed setup docs

### 20. Create `docs/SETUP.md` (or update existing)
- Detailed Windows setup (Git Bash, CMake, Ninja, .NET, Node.js)
- Dev container setup and usage
- LLVM download process explanation
- Troubleshooting common issues

### 21. Update `.claude/CLAUDE.md`
- Git Bash required on Windows (not PowerShell)
- All commands run in bash
- Same workflow on both platforms
- LLVM auto-downloaded via Makefile

## Phase 8: Platform-Specific Details

### 22. Verify/update platform detection in existing files
- `maxon-bin/file_utils.cpp` - already has platform detection ✓
- `maxon-bin/main.cpp` - already has isatty platform detection ✓
- `maxon-bin/test_utils.cpp` - verify CreateProcessA vs fork/exec
- `maxon-bin/test_regenerate.cpp` - verify process creation
- `maxon-bin/test_runner.cpp` - verify process creation
- `lsp-server/src/lsp_server.cpp` - verify path detection

### 23. Add platform-specific process spawning for Linux
- Implement fork/exec equivalents for CreateProcessA usage
- Use POSIX APIs (already partially present via #ifdef)

## Phase 9: Testing & Validation

### 24. Test on Windows
- Fresh clone in Git Bash
- Run `make all` → downloads LLVM (~1-2 min), builds maxon
- Run `make test` → all tests pass
- Compile and run simple maxon program
- Verify LSP server works

### 25. Test on Linux (dev container)
- Open project in VS Code with dev container
- Run `make all` → downloads LLVM (~1-2 min), builds maxon
- Run `make test` → all tests pass
- Compile and run simple maxon program
- Verify LSP server works

### 26. Test GitHub Actions
- Push to branch
- Verify both Windows and Linux CI jobs pass
- Verify LLVM caching works (second run uses cached LLVM)

## Makefile Targets Summary

```makefile
make all          # Download LLVM (if needed) + build maxon + LSP + extension
make llvm         # Download/update LLVM only
make compiler     # Build maxon compiler only
make lsp          # Build LSP server only
make extension    # Build VS Code extension only
make test         # Run all tests
make clean        # Clean maxon build (keep LLVM)
make clean-llvm   # Remove LLVM download
make clean-all    # Remove everything
```

## Developer Workflow

### First Time Setup
- **Windows**: Install Git for Windows, clone repo, `make all` in Git Bash
- **Linux**: Open in dev container, `make all`
- LLVM auto-downloaded in ~1-2 minutes (not 30-40 min build!)

### Daily Development
- Edit code, `make`, run tests
- LLVM already downloaded, only rebuilds changed code

### Update LLVM
1. Edit `llvm-config.txt` with new version
2. `make clean-llvm`
3. `make all` (downloads new LLVM, builds maxon)

## Expected Timeline

- **LLVM download**: ~1-2 min (vs 30-40 min to build!)
- **Maxon full rebuild**: ~2-5 min
- **Incremental builds**: seconds

## Key Advantages of Precompiled LLVM

✅ 95% faster setup (1-2 min vs 30-40 min)
✅ No build dependencies (Python, CMake for LLVM build)
✅ Same binaries across machines (reproducible)
✅ Easier for new contributors
✅ Faster CI/CD (quicker downloads than builds)
✅ Official LLVM releases are well-tested
