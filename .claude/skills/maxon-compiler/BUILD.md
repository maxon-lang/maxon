# Build System Reference

Use the top-level `Makefile` for all builds. Run `make help` for the full list.

## Key Build Targets

| Command | Description |
|---------|-------------|
| `make all` | Build everything (compiler, LSP, extension) |
| `make compiler` | Build only the Maxon compiler |
| `make lsp` | Build LSP server and install VS Code extension |
| `make runtime` | Build the runtime library |
| `make test` | Run all test suites |
| `make fragments` | Extract specs, regenerate IR, run fragment tests |
| `make backend-test` | Run backend C++ unit tests (MIR, x86, encoding) |
| `make lsp-test` | Run LSP C++ unit tests |
| `make docs` | Generate HTML documentation from specs |
| `make clean` | Clean build artifacts |

## Compiler CLI Commands

| Command | Description |
|---------|-------------|
| `maxon <file.maxon>` | Compile and run (no temp files) |
| `maxon compile <file>` | Compile to executable |
| `maxon compile <file> --emit-ir` | Also generate `.ir` file (MIR output) |
| `maxon compile <file> -O` | Enable optimizations |
| `maxon compile <file> -g` | Generate debug information |
| `maxon compile <file> -v/-vv/-vvv` | Verbosity levels 1/2/3 |
| `maxon self-test` | Run compiler self-tests |
| `maxon extract-specs` | Extract test fragments from spec files |
| `maxon regen-fragments` | Regenerate IR for test fragments |
| `maxon test-fragments` | Run all language tests |
| `maxon generate-docs` | Generate HTML docs from specs |

## Testing Commands

| Test Type | Command | Location |
|-----------|---------|----------|
| Compiler self-tests | `maxon self-test` | Built into compiler |
| Language fragments | `maxon test-fragments` | `language-tests/fragments/` |
| Backend unit tests | `make backend-test` | `maxon-bin/backend/tests/` |
| LSP unit tests | `make lsp-test` | `lsp-server/tests/` |
| Extension tests | `make extension-test` | `vscode-extension/src/test/` |
| Debugger tests | `make debugger-test` | `debugger-tests/` |
| All tests | `make test` | Runs all above |

### Running Specific Tests

```bash
# Run fragment tests with verbose output
maxon test-fragments --verbose

# Run a specific backend test
cd build && ctest -R "test_name" -V

# Validate no orphaned fragments
make validate-specs
```

## Platform Notes

- **Windows**: Uses Git Bash for shell commands
- **Runtime**: Platform-specific `.obj` (Windows) or `.o` (Linux) in `bin/`
