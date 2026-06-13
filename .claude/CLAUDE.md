You do not care if an issue is pre-existing. Just debug and fix it.

Do not use "cmd /c" to run commands

There are no time constraints. Complexity doesn't matter. If you are fixing an issue then fix it properly. No workarounds.

Do not use git commands that would change the working directory like stash or checkout

## maxon-dev MCP tools (PREFER THESE)

When working in this repo, prefer the `maxon-dev` MCP tools over raw Bash invocations of the compiler binaries. They are faster (no shell startup), return structured results, and are the canonical way to drive builds, tests, and one-off snippets in this project. Bash invocation of `./bin/maxon.exe` or `./maxon-selfhosted/.maxon/maxon-selfhosted.exe` should only be used when no MCP tool covers the case.

| Task | Use this tool |
|------|---------------|
| Build the C# compiler | `mcp__maxon-dev__build` with `target: "csharp"` |
| Build the self-hosted compiler | `mcp__maxon-dev__build` with `target: "selfhosted"` |
| Build both, in order | `mcp__maxon-dev__build` with `target: "both"` |
| Run the full spec-test suite | `mcp__maxon-dev__run_spec_test` (set `compiler: "selfhosted"` for the self-hosted runner) |
| Run self-hosted spec tests | `mcp__maxon-dev__run_self_hosted_test` |
| Get per-test PASS/FAIL detail for a filter | `mcp__maxon-dev__spec_test_outcome` (requires `filter`) |
| Run an inline Maxon snippet or a file | `mcp__maxon-dev__run_program` (requires `compiler: "csharp"` or `"selfhosted"`) |
| Dump IR (optionally per-stage) | `mcp__maxon-dev__dump_ir` (requires `compiler: "csharp"` or `"selfhosted"`; set `dumpStages: true` for stage-by-stage artifacts) |
| Dump all per-stage IR (self-hosted) | `mcp__maxon-dev__dump_stages` (self-hosted, always emits every stage artifact + the final `.ir`) |
| Format a Maxon file or snippet | `mcp__maxon-dev__fmt` |
| Look up a 4-digit error code | `mcp__maxon-dev__lookup_error_code` |
| Debug memory-management issues | `mcp__maxon-dev__mm_trace_analyze` |

Flags like `--filter`, `--update-required`, `--log`, `--mm-trace`, and `--target` are exposed as parameters on the relevant tools (`filter`, `updateRequired`, `log`, `mmTrace`, `target`). When iterating on a specific failing test, pass `filter` to `run_spec_test`/`run_self_hosted_test` or use `spec_test_outcome` for per-test verbose output. Cross-compile tests (e.g. wasm) via `target: "wasm32-wasi"` on `run_self_hosted_test`/`spec_test_outcome`. To force a from-source stdlib rebuild, pass `noStdlibCache: true` to `build` — it deletes `maxon-selfhosted/.maxon/cache/stdlib-*.mxc` before the build (no-op for `target: "csharp"`, whose stdlib cache is in-memory).

## Building and Testing

Binary names differ by host OS: Windows produces `maxon.exe` / `maxon-selfhosted.exe`, Linux and macOS produce `maxon` / `maxon-selfhosted` (no extension). Commands below show the Windows form; drop the `.exe` on Linux/macOS.

### C# bootstrap compiler (maxon-sharp)

- **Build:** `dotnet build` (run from `maxon-sharp/`)
- **Spec tests:** `./bin/maxon.exe spec-test`

The C# compiler binary is at `./bin/maxon.exe` (Windows) or `./bin/maxon` (Linux/macOS).

### Self-hosted compiler (maxon-selfhosted)

- **Build:** `./bin/maxon.exe build maxon-selfhosted` (requires C# compiler already built)
- **Spec tests:** `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test`

The self-hosted compiler binary is at `./maxon-selfhosted/.maxon/maxon-selfhosted.exe` (Windows) or `./maxon-selfhosted/.maxon/maxon-selfhosted` (Linux/macOS).

### Common flags

- `--filter=PATTERN` — run only tests matching a pattern
- `--update-required` — regenerate RequiredIR blocks
- `--log=CATEGORY:LEVEL` — enable detailed logging (e.g., `--log=ir:debug`, `--log=codegen:trace`)
- `--mm-trace` — trace memory management operations (useful for memory leak debugging)
- `--target=ARCH-OS` — test a specific target (self-hosted only, e.g. `x64-windows`, `arm64-macos`, `wasm32-wasi`)

Do NOT use `dotnet run` — it recompiles every time. Use the pre-built binaries directly.

Exit code 101 means a memory leak was detected.

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
