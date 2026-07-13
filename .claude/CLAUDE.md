You do not care if an issue is pre-existing. Just debug and fix it.

Do not use "cmd /c" to run commands

There are no time constraints. Complexity doesn't matter. If you are fixing an issue then fix it properly. No workarounds.

## maxon-dev MCP tools (PREFER THESE)

When working in this repo, prefer the `maxon-dev` MCP tools over raw Bash invocations of the compiler binaries. They are faster (no shell startup), return structured results, and are the canonical way to drive builds, tests, and one-off snippets in this project. Bash invocation of `./bin/maxon.exe` should only be used when no MCP tool covers the case.

Two compilers are driveable, and every tool that drives one takes a `compiler`
(or `target`) naming which: `"csharp"` (the C# bootstrap) or `"shv2"` (the
ground-up rewrite, whose suite is `specs-shv2`). **The v1 self-hosted compiler
(`maxon-selfhosted`) is DEPRECATED and no longer reachable from the MCP** — no
`selfhosted` token, no build target, no spec-test runner. Drive it by hand if you
must (see Building and Testing below).

| Task | Use this tool |
|------|---------------|
| Build the C# compiler | `mcp__maxon-dev__build` with `target: "csharp"` |
| Build the shv2 compiler | `mcp__maxon-dev__build` with `target: "shv2"` (built BY the bootstrap — build `csharp` first if it is stale) |
| Run a spec-test suite | `mcp__maxon-dev__run_spec_test` (set `compiler` to pick the suite; `"shv2"` runs `specs-shv2`) |
| Gate compile-time + memory SCALING (shv2) | `mcp__maxon-dev__run_scale_test` (no `compiler` arg — shv2 only) |
| Get per-test PASS/FAIL detail for a filter | `mcp__maxon-dev__spec_test_outcome` (requires `filter`; either compiler) |
| Run an inline Maxon snippet or a file | `mcp__maxon-dev__run_program` (requires `compiler`) |
| Dump IR (optionally per-stage) | `mcp__maxon-dev__dump_ir` (requires `compiler`; `dumpStages: true` for stage-by-stage artifacts — csharp only) |
| Format a Maxon file or snippet | `mcp__maxon-dev__fmt` (csharp `maxon fmt`) |
| Look up a 4-digit error code | `mcp__maxon-dev__lookup_error_code` |
| Debug memory-management issues | `mcp__maxon-dev__mm_trace_analyze` |

`build` takes ONE compiler per call — the old `both` / `all` chains sequenced the bootstrap ahead of the retired self-hosted compiler and are gone.

Flags like `--filter`, `--update-required`, `--log`, `--mm-trace`, and `--target` are exposed as parameters on the relevant tools (`filter`, `updateRequired`, `log`, `mmTrace`, `target`). When iterating on a specific failing test, pass `filter` to `run_spec_test` or use `spec_test_outcome` for per-test detail. `target` cross-compiles to what the CHOSEN compiler can emit — the bootstrap has x64 and arm64 emitters (`"x64-windows"`, `"arm64-macos"`), and shv2 rejects the flag outright. **The wasm backend was only ever in `maxon-selfhosted`, so `target: "wasm32-wasi"` is no longer reachable through the MCP** — run it by hand against that compiler's binary if you need it.

### `run_scale_test` — the scaling gate (shv2 only)

`spec-test` proves the compiler is CORRECT. `scale-test` proves it is still LINEAR. It compiles a ladder of generated programs — six rungs, each double the last — and gates the growth curves. **Run it after any change to a pass, the IR, or a data structure the compiler indexes by.** A default run is ~20 s.

It returns four verdicts, and the last two are the point:

- **PASS** / **FAIL** — a named curve blew its budget, or a per-rung memory golden moved.
- **VOID** — the generated corpus was DEGENERATE (it folded away, so nothing was compiled and no verdict about the compiler can be drawn). Not a compiler regression.
- **NOISY** — the machine was too loaded for the TIME curves to mean anything. Not a verdict. The memory gates, which are exact, still ran.

What is gated, and what is not:

- **Per-rung memory goldens** (allocations / frees / bytes) — EXACT, committed, bit-for-bit reproducible. The strong gate. `updateRequired: true` rewrites them, and **the diff is the review** — a golden that moved means the compiler allocates differently for the same input.
- **Per-phase growth exponents**, in time AND in allocations — tight (~1.25) everywhere except `regalloc`'s `splitting`/`liveness`, which are KNOWN superlinear (the splitter recomputes liveness after every split — `ARCHITECTURE.md:1336-1345`) and are budgeted at 2.2.
- **Aggregate time / memory** — reported, loosely budgeted. They are sums of curves with different exponents, so they bend by construction; the teeth are in the per-phase rows.
- **Absolute milliseconds** — reported, never gated (machine-dependent).

`perType: true` adds an untimed `--mm-trace` pass that attributes allocations to the TYPE allocated — the way to answer "a memory gate fired, but of *what*?". It is slow (several minutes) and off by default.

There is **no `maxon clean` command** — it prints usage and exits 1. To force a from-source stdlib rebuild, delete `stdlib/.maxon/cache/*.mxc` yourself; the self-hosted compiler recompiles the stdlib whenever its cache is absent. The C# bootstrap's stdlib cache is in-memory only, so it always builds the stdlib fresh.

**shv2 does less, and the tools say so rather than pretending.** Its runner implements only `--filter`, `--update-required`, and `--log`; `mmTrace`, `target`, and `dumpStages` are REJECTED with an `invalidParams` error naming the gap, never silently dropped. It has no `fmt` and no `--dump-stages`. Always pair `updateRequired` with a `filter` — unfiltered, it rewrites every golden in the suite.

## Building and Testing

Binary names differ by host OS: Windows produces `maxon.exe` / `maxon-shv2.exe`, Linux and macOS produce `maxon` / `maxon-shv2` (no extension). Commands below show the Windows form; drop the `.exe` on Linux/macOS.

### C# bootstrap compiler (maxon-sharp)

- **Build:** `dotnet build` (run from `maxon-sharp/`)
- **Spec tests:** `./bin/maxon.exe spec-test`

The C# compiler binary is at `./bin/maxon.exe` (Windows) or `./bin/maxon` (Linux/macOS).

### shv2 compiler (maxon-shv2)

- **Build:** `./bin/maxon.exe build maxon-shv2` (requires C# compiler already built)
- **Spec tests:** `./maxon-shv2/.maxon/maxon-shv2.exe spec-test` (the `specs-shv2` suite)

The shv2 compiler binary is at `./maxon-shv2/.maxon/maxon-shv2.exe` (Windows) or `./maxon-shv2/.maxon/maxon-shv2` (Linux/macOS).

### Self-hosted compiler (maxon-selfhosted) — DEPRECATED

Not driveable from the `maxon-dev` MCP any more. The source tree is still in the checkout and still builds, so drive it by hand when you need something only it has (notably the wasm backend, and the complete 4-digit `ErrorCode.maxon` registry that `lookup_error_code` reads):

- **Build:** `./bin/maxon.exe build maxon-selfhosted` (requires C# compiler already built)
- **Spec tests:** `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test`

The self-hosted compiler binary is at `./maxon-selfhosted/.maxon/maxon-selfhosted.exe` (Windows) or `./maxon-selfhosted/.maxon/maxon-selfhosted` (Linux/macOS).

### Common flags

- `--filter=PATTERN` — run only tests matching a pattern
- `--update-required` — regenerate RequiredIR blocks
- `--log=CATEGORY:LEVEL` — enable detailed logging (e.g., `--log=ir:debug`, `--log=codegen:trace`)
- `--mm-trace` — trace memory management operations (useful for memory leak debugging)
- `--target=ARCH-OS` — test a specific target (`x64-windows`, `arm64-macos`; `wasm32-wasi` is self-hosted only)

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
- **No magic values** — replace bare literal constants (numbers, strings) with named `static` constants that describe their meaning. When a set of related constants belongs together, group them into a `static enum` instead of scattering individual constants.

## Spec Files

- Old 3-digit error codes (e.g. `E022`) in spec files must be updated to the new 4-digit codes.
- If tests that use RequiredIR fail, regenerate with `--update-required`.
