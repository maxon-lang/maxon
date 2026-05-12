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
- `--log=CATEGORY:LEVEL` — enable detailed logging (e.g., `--log=ir:debug`, `--log=codegen:trace`)
- `--mm-trace` — trace memory management operations (useful for memory leak debugging)
- `--target=ARCH-OS` — test a specific target (self-hosted only, e.g. `x64-windows`, `arm64-macos`, `wasm32-wasi`)

Do NOT use `dotnet run` — it recompiles every time. Use the pre-built binaries directly.

Exit code 101 means a memory leak was detected.

## Debugging

For assembly-level debugging of a compiled Maxon executable, use `./scripts/lldb.sh <program.exe>`. The wrapper sets the env vars required by the bundled `llvm-project/bin/lldb.exe` (Python 3.10 home and site-packages). Maxon emits COFF symbols, so functions are addressable by name — e.g. `b <module>.main`, `b stdlib.Print.print`, `b __destruct_String`.

## Code Quality

Apply these standards when writing or reviewing any code:

- **Eliminate duplicated code** — refactor shared logic into helper methods. This includes pre-existing duplication.
- **No silent unhandled cases** — `match`/`if` chains that don't cover all cases must throw on the unhandled path, not return a default value. Never use a bare `default` case in `match` — use `default throws` or `default panic("msg")`.
- **No silent `else` fallthrough** — if an `else` branch should never be reached, throw an error instead.
- **`try/otherwise` that should never fail** must use `otherwise panic("reason")`.
- **Comments explain "why", not "what"** — don't restate the code.
- **No skipped work** — look for comments implying something was skipped, deferred, or not fully implemented, and address them.
- **typealias names describe purpose**, not type — e.g. `BytePos` not `Offset`.
- **Typed ranges should be as specific as possible** — e.g. `int(0 to 100)` instead of `int(0 to u64.max)`. Use the narrowest range correct for the domain. Wide ranges are fine when there is no clear limit.
- **Fix all IDE-reported problems and compiler warnings.**
- **Cross-target consistency** — any change to target-specific code (e.g. x64) must have an equivalent change in all other targets (e.g. arm64) where applicable.
- **Consolidate redundant match arms** — if a `match` has multiple cases with the same result, collapse them into a single case.
- **No thin wrapper functions** — remove functions that do nothing but delegate to one other call.
- **No sentinel return values** — functions that cannot return a valid value must throw, not return `""`, `-1`, `null`, or similar.
- **Blank lines for readability** — add blank lines around control flow statements and between logical sections.

## Spec Files

- Old 3-digit error codes (e.g. `E022`) in spec files must be updated to the new 4-digit codes.
- If tests that use RequiredIR fail, regenerate with `--update-required`.
