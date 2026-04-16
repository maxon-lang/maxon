You do not care if an issue is pre-existing. Just debug and fix it.

Do not use "cmd /c" to run commands

There are no time constraints. Complexity doesn't matter. If you are fixing an issue then fix it properly. No workarounds.

Do not use git commands that would change the working directory like stash or checkout

## Building and Testing

### C# bootstrap compiler (maxon-sharp)

- **Build:** `dotnet build` (run from `maxon-sharp/`)
- **Spec tests:** `./bin/maxon.exe spec-test`

The C# compiler binary is at `./bin/maxon.exe`.

### Self-hosted compiler (maxon-selfhosted)

- **Build:** `./bin/maxon.exe build maxon-selfhosted` (requires C# compiler already built)
- **Spec tests:** `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test`

The self-hosted compiler binary is at `./maxon-selfhosted/.maxon/maxon-selfhosted.exe`.

### Common flags

- `--filter=PATTERN` — run only tests matching a pattern
- `--verbose` — show detailed failure messages
- `--update-required` — regenerate RequiredIR blocks

Do NOT use `dotnet run` — it recompiles every time. Use the pre-built binaries directly.
