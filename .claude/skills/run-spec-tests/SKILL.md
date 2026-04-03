---
name: run-spec-tests
description: Run Maxon spec tests for either the C# bootstrap compiler or the self-hosted compiler. Use this skill whenever the user asks to run tests, check if tests pass, validate compiler changes, or verify spec test results. Triggers on phrases like "run tests", "spec tests", "check tests", "do tests pass", "run the specs", "test the compiler".
---

Run the Maxon spec tests. This project has two compilers that share the same spec test suite:

- **C# bootstrap compiler** (`maxon-sharp/`) — the original compiler
- **Self-hosted compiler** (`maxon-selfhosted/`) — the Maxon-in-Maxon compiler

## Which compiler to test

- If the user says "self-hosted", "selfhosted", or made changes in `maxon-selfhosted/`, use the self-hosted compiler
- If the user says "C#", "csharp", "sharp", or "bootstrap", or made changes in `maxon-sharp/`, use the C# compiler
- If the user says "both", run both (self-hosted first, then C#)
- If unclear, check which files were recently modified with `git diff --name-only` and test the compiler that was changed. If still ambiguous, ask the user.

## Running spec tests

On Windows, the executables have `.exe` extensions. On macOS/Linux, omit the extension.

### Self-hosted compiler

```bash
# Windows
./maxon-selfhosted/bin/maxon-selfhosted.exe spec-test
# macOS/Linux
./maxon-selfhosted/bin/maxon-selfhosted spec-test
```

### C# compiler

```bash
# Windows
./bin/maxon.exe spec-test
# macOS/Linux
./bin/maxon spec-test
```

Do NOT use `dotnet run` — it recompiles the project every time. Use the pre-built executables directly.

## Useful flags

- `--filter=PATTERN` — run only tests whose name matches the pattern
- `--verbose` — show detailed failure messages (use this when debugging failures)
- `--log=CATEGORY:LEVEL` — get more detail for a specific subsystem (e.g., `--log=mlir:debug`)
- `--update-required` — regenerate and update RequiredLowering blocks in spec fragments
- `--target=ARCH-OS` — target a specific architecture (e.g., `x64-windows`, `arm64-macos`)

## Reading test output

Do not filter spec test output with grep, head, or tail. The test runner already only shows failing test names and a summary by default. Let the output flow naturally. Use `--verbose` when you need detailed failure messages.

## Spec test structure

Spec files live in `specs/*.md`. Each spec file contains multiple test cases marked with `<!-- test: name -->` HTML comments, with Maxon source code and expected outputs (ExitCode, Stdout, Stderr).

Generated test fragments live in `specs/fragments-{arch}-{os}/{spec_name}/{test_name}.test`.
