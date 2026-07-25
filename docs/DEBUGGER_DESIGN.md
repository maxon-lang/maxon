# The Maxon Debugger — design + implementation plan

## Context

Maxon has no debugger. Today, debugging a Maxon program means `print`, the `maxon monitor`
shared-memory trace stream, and the runtime fault backtrace. There is no way to set a breakpoint,
step source lines, or inspect a variable by name — because **no PC→source-line table and no
symbol/locals/type descriptor exists**. Source positions die at the parser (`Token.Line/Column`),
IR ops carry no location (bar two cosmetic trace strings on struct/enum literals), and codegen writes
`NumberOfLinenumbers = 0` into the PE.

Maxon is going to be **its own debugger**. Not gdb, not lldb, no DWARF, no PDB, and — deliberately —
**no compatibility with any existing debugger or debug format**. We own the format end to end, which
lets it fit Maxon's green-thread runtime and reuse the mature DebugStream shared-memory machinery
instead of fighting a format designed for C.

The initial implementation lands in **maxon-sharp** (the C# bootstrap), mirroring the existing
`DebugStreamMonitor.cs` (the driver) and `RuntimeEmitter.DebugStream.cs` (the emitted runtime). It is
ported to **shv2** (the ground-up Maxon rewrite) once shv2 is ready. Code coverage and profiling are
first-class, built on the same substrate.

This document is the design of record. The work is phased (P0–P10); each phase is one rung of the
normal loop — red-before-green spec, implement, independent review, gate battery, merge.

---

## The two governing invariants

Everything below follows from these. If a design choice threatens either, the choice is wrong.

### 1. Identical executables

**`maxon build foo.maxon` and `maxon build --no-debug-info foo.maxon` produce a byte-identical
`foo.exe`.** There is no separate "debug build". The sidecar is **on by default** (opt-out): a normal
build writes the detachable **`foo.exe.mxdbg` sidecar** next to the binary, and `--no-debug-info`
suppresses it (for clean/release output or a measured-hot build). Either way the `foo.exe` is
identical — the flag controls *one* thing, whether the sidecar *file* is written, never a byte of code.

Default-on holds only while it is cheap. The file write is trivial (the sidecar is built from data
codegen already has); the one cost paid on every build is per-op source-span capture during
parse/lowering. That cost is **measured with `run_scale_test`** before default-on is locked in — if it
proves material, `--no-debug-info` must fully skip span capture (not merely the file write), or the
default reverts to opt-in. Measurement decides, not assertion.

Every binary already contains the debug agent (see below); it is dormant. Debug information is
*observed and described*, never *injected* — the sidecar is a pure description of the already-emitted
`.text`, and emitting it must not change a single byte of code. Byte-identity is a headline test gate.

The one sanctioned exception is `--no-debug-agent`, a build flag for hardened deployments that omits
the agent entirely (see Security, below). That is opt-*out*; the default is agent-present.

### 2. ~Zero idle cost

The agent adds **no per-line instrumentation**. Breakpoints are `INT3`/`BRK` bytes the agent patches
into its own `.text` **at runtime** — not compiled-in checks at every statement. When no debugger is
attached, the agent is dark: one `getenv("MAXON_DEBUG")` at startup, and then nothing. Nothing is
mapped, no handler fires, the steady-state instruction stream is untouched.

This is the crucial difference from DebugStream. DebugStream is opt-in (`--debugstream`) *because* its
event emission inserts a `load __ds_base; jump-if-zero` branch at every trace site plus calls on the
alloc/free/refcount paths — that is hot-path cost. The debug agent has no analog: it patches code, it
does not instrument it. So the agent can be always-on where DebugStream's event hooks cannot.

---

## Architecture

The binary carries a dormant agent; the sidecar describes it; the external `maxon debug` driver
supplies *meaning* while the in-process agent supplies *mechanism*.

```
  BUILD TIME                                RUN / DEBUG TIME
  ──────────                                ────────────────────────────────────
  source → IR (+source spans,               debuggee (the SAME identical exe)
    metadata only) → codegen                 ├─ dormant debug agent (always emitted)
      │  ALWAYS emits the agent +            │    getenv(MAXON_DEBUG):
      │  shared-mem substrate (dormant)      │      unset → dark, ~zero cost
      │  .text identical with/without -g     │      set   → map shared mem, arm handler
      ├─ foo.exe        (agent inside)       │    · patches INT3/BRK into own .text (W^X)
      └─ foo.exe.mxdbg  (sidecar, -g only)   │    · in-proc trap handler (VEH/SIGTRAP),
           · build-id (= hash of .text)      │      chains with __gt_fault_handler
           · file/func/line tables           │    · walks GT stacks in-process; per-GT park
           · locals (loclists) + types       │    · reads mem/regs; simple in-proc eval
                                              │    · control + stop events over shared mem
                                              └──────────────┬─────────────────────
                                                   shared memory (extends DebugStream:
                                                   ring + handshake + control channel)
                                                              │
                                               maxon debug    ▼  (driver engine, C#)
                                                · sets MAXON_DEBUG, maps shared mem
                                                · mmaps foo.exe.mxdbg, validates build-id
                                                · file:line↔addr, name↔location, bytes↔typed
                                                · sends commands, receives stop events
                                                · ALSO decodes DebugStream trace events
                                                · reused by CLI / MCP / DAP / TUI
```

**Agent (in-process, always present) = mechanism.** Patch/step, read memory & registers, walk green
threads, park/resume individual GTs, evaluate simple expressions in-context, stream stop events. It
knows the runtime layout intrinsically because it is compiled with it.

**Driver (`maxon debug`, external) = meaning.** Loads and validates the sidecar, translates
addresses↔source and raw bytes↔typed values, and drives the surfaces. One `MaxonDebugger` engine is
the single brain; every surface (CLI, MCP, DAP, TUI) is a thin front-end over it.

**DebugStream** stays as-is for high-frequency tracing: its *event-emission hooks* remain opt-in
(`--debugstream`, hot-path). Only the cheap shared-memory *substrate* (init, ring, versioned
handshake, reserve/commit) becomes always-emitted, because the agent's control channel extends it.

---

## The sidecar format — `<binary>.mxdbg`

Position-independent, little-endian, mmap-friendly. A fixed header with absolute section offsets, then
arrays of fixed-width records plus one shared string/blob pool addressed by `(offset, len)`. No
pointers; the driver `mmap`s the file and indexes it without parsing or allocating. This is the shape
already proven by the runtime `__symtable` and the `MXDS_STRS`/`MXDS_TAGS` name blobs — and it carries
their discipline: **state every field width exactly once** (see `DsNameBlobFieldSize`), because a `2`
spelled at each end is a number that can drift and silently desynchronise the parse.

The sidecar holds **static source information only**. The agent supplies runtime access, so no
green-thread/processor layout descriptor is needed here — the agent knows it intrinsically.

**Header:** magic `"MXDBG\0"`, format version, **build-id** (FNV-1a content hash of the binary's
`.text`, following the compiler-fingerprint content-hash convention), target triple, and
`(offset, size)` for each section.

**Sections:**
- **String pool** — every name/path, UTF-8, addressed by `(offset, len)`.
- **File table** — source file paths (string-pool indices).
- **Function table** — name, code-offset range `[start, end)` in `.text`, frame size, param count,
  and index ranges into the line and local tables.
- **Line table** — `(codeOffset, fileId, line, col, flags)`, sorted by `codeOffset`, delta-encoded.
  `flags` marks statement boundaries (for stepping) and coverage points. Binary-searchable both ways
  (PC→line and line→PC).
- **Local table** — per local, a **location list**: `(scopeStart, scopeEnd) → rbp-relative slot |
  register | optimized-out`. Usually a single stable stack slot in the bootstrap (which has no
  aggressive register allocator); shv2's real allocator leans on multi-entry lists.
- **Type table** — `(nameId, kind, size, align, fieldCount, fieldIndex)`; fields are
  `(nameId, offset, typeId)`. Kinds cover int-ranged / struct / enum / union / String / Array /
  managed-record. Enough for type-aware value rendering (a String's fused 48-byte record, the
  `__ManagedMemory` header offsets, an enum's discriminant).
- **Coverage-point table** — `(counterIndex → codeOffset, fileId, line, col-span, funcId)`.

**Binding, and the "instrument that lies" discipline.** The driver **refuses** a sidecar whose
build-id does not match the binary's embedded `__build_id`, exactly as `DebugStreamMonitor`'s schema
handshake refuses a version mismatch and `lookup_error_code` re-hashes its registry before trusting
it. An instrument that lies is worse than no instrument; a mismatch is reported and refused, never
papered over with wrong line numbers.

**Emitter failure policy.** Writing the sidecar is report-and-swallow — **never a build gate**,
modelled on shv2's `MetricsEmit.writeMetrics`. The record layout is driven off an enum (à la
`CompilePhase.allCases`) so a newly added section cannot be silently omitted.

**The sidecar subsumes the COFF symbol table.** See Compiler changes.

---

## Compiler changes (maxon-sharp)

Every change here is passive metadata capture. `.text` must stay byte-identical to a non-debug build.

- **Source spans (metadata only).** A compact `SourceSpan (fileId, line, col)` attached to ops via a
  per-function side-table keyed by op — *not* a field on the release op path, so op ordering and
  release codegen are untouched. Set at parse time from `Token.Line/Column`, generalising the existing
  `MaxonStructLiteralOp.SourceLocation` / `SetSourceLocation` idiom, and copied through both lowering
  passes (Maxon→Standard, Standard→X86/ARM64).
- **Capture at emit.** Machine ops already resolve to code offsets (`GetLabelOffset`, `_labels`).
  Emit a `(codeOffset, span)` line entry whenever the span changes, and record each named local's
  actual emitted location into the loclist. Read-only with respect to the emitted bytes.
- **Always emit the dormant agent + shared-memory substrate** into every binary, unconditionally. It
  is hook-free and dark unless `MAXON_DEBUG` is set. DebugStream's per-site event hooks stay
  `--debugstream`-gated. Embed the tiny `__build_id` (hash of `.text`).
- **Emit the sidecar by default**, as a separate file kept out of the PE/Mach-O; `--no-debug-info`
  opts out and (once measured) skips span capture entirely. `BuildConfig.debug_info` already flows
  from `stdlib/Build.maxon` to the C# `BuildConfig.Debug_info` but is currently never read — read it,
  flip its default to true, and add a `--no-debug-info` flag in `ParseOptions`. It gates only whether
  the sidecar is produced, never a code byte.
- **`--no-debug-agent`** — a hardened-build opt-out that omits the agent; the one case where two
  binaries differ.
- **Retire the COFF symbol table.** Stop emitting it. Nothing in-tree reads it (there is no linker;
  external-debugger compatibility is out of scope; the one `PointerToSymbolTable` read in the test
  runner merely skips the header field). Its `name→codeOffset` payload now lives in the sidecar, and
  every binary shrinks — partly offsetting the agent's size. **Do not touch the runtime `__symtable`**
  (the `.symtab` section blob): it is a *different* table, read at runtime by the panic backtrace, and
  must stay so a release binary with no sidecar still symbolizes panics.

---

## The in-process debug agent (emitted, always present, dormant)

A family of `__dbg_*` runtime functions, emitted the way the `__ds_*` DebugStream functions are.

- **Init & activation.** `__dbg_init` does one `getenv("MAXON_DEBUG")` at startup; unset → dark. Set →
  map the shared-memory control segment (reusing the DebugStream segment and handshake) and install
  the trap handler.
- **Trap handler, chained with the fault handler.** A Vectored Exception Handler (Windows) /
  `sigaction(SIGTRAP)` (POSIX). The runtime already installs `__gt_fault_handler`, so reuse that
  install path. Dispatch: an `INT3`/`BRK` at a known breakpoint address → debug logic; anything else →
  the existing fault handler. Async-signal-safe; identifies the current green thread via the current P.
- **Breakpoints via runtime code patching.** `__dbg_set_bp(addr)` flips the page RX→RW
  (`VirtualProtect`/`mprotect`), writes `0xCC` / `BRK #0`, saves the original byte, restores RX. On
  hit, the handler emits a stop event over shared memory and **parks the current GT**, optionally
  letting the scheduler run other GTs (per-GT stop) or halting the M (stop-the-world) per the driver's
  mode. Continue restores the byte, single-steps, re-arms.
- **Stepping, memory, registers, per-GT control.** Line-based stepping via temporary breakpoints at
  the successor statement boundaries the line table identifies; direct in-process reads/writes of
  target memory and register context; park/resume named GTs by cooperating with the scheduler.
- **In-process stack walk & eval.** Generalise `mrt_fault_backtrace` to walk any GT's saved-rbp chain
  (every function has a frame pointer), reading locals from `[rbp − slot]`/registers per the driver's
  loclist requests. Simple expression evaluation can call runtime/type methods in-process — the
  capability an external OS-level debugger could not safely offer on a cooperative M.
- **Async break.** A console-ctrl (Windows) / `SIGINT` (POSIX) handler lets the driver "pause now"
  even mid-loop, since signals preempt the cooperative M.
- **Control channel.** Extend the DebugStream shared segment with a bidirectional command/response
  mailbox plus a doorbell, alongside async stop events on the existing committed-entry ring. Reuse the
  versioned handshake and reserve/commit discipline verbatim.

---

## The driver engine — `MaxonDebugger` (C#)

A sibling to `DebugStreamMonitor.cs`. Spawns the target with `MAXON_DEBUG` set; creates and maps the
control+event shared memory (reusing the shared-mapping helper — a named section on Windows, a
`MAP_SHARED` temp file elsewhere); `mmap`s the sidecar and validates its build-id. It translates
meaning — file:line↔address, name↔location, raw bytes↔typed values — sends control commands, and
decodes stop events. It also decodes DebugStream trace events when the target was built
`--debugstream`, correlating live mm/sched/log/coverage/profile events with the stopped state. Its
public API (`SetBreakpoint`, `Run`, `Continue`, `Step*`, `Backtrace`, `Locals`, `ReadMemory`, `Eval`,
`Symbolize`, `Park/ResumeGt`) is reused by every surface.

---

## Surfaces

### CLI (first) — a rich guided REPL, deliberately more ergonomic than gdb

gdb's ergonomics are the anti-target: cryptic commands, no context on stop, flat value dumps, weak
discoverability, clunky thread UX. `maxon debug` inverts each:

- Full-word canonical commands with short aliases and fuzzy prefixes; a typo yields a "did you mean"
  suggestion; `help <cmd>`/`?`/`commands` make it discoverable.
- **Auto-orientation on every stop** — a source-context window with the current line marked, the
  location, the stop reason, the active GT, and inline value annotations on the stopped line.
- A location-aware prompt (`(maxon:withdraw 42)›`).
- **Structured, lazy value trees** — `print` renders type-aware trees (String as text+length,
  Array/Map as sized collections, struct/enum as named-field trees) with lazy expansion and path
  navigation (`print self.history`, `print user.address.city`); raw bytes only on explicit `mem`.
- **Smart fuzzy targeting** — `break Account.withdraw` (qualified/fuzzy), `break foo.maxon:42`,
  `break 42`, bare `break`, `break … if <cond>` (conditional, evaluated in-process); tab-completion of
  functions/files/locals from the sidecar.
- **First-class green threads** — `threads`/`gts` list GTs with status + top frame; `gt <id>` switches
  context; per-GT breakpoints; a `--this-gt`/`--stop-others` toggle.
- Sensible defaults — `run` auto-(re)builds if the source is newer; Ctrl-C = async break, not kill;
  persistent history; color/Unicode with graceful ASCII/`NO_COLOR`/non-tty fallback.
- **One engine, two faces** — the interactive REPL and `--batch --commands=… → JSON stops` share the
  same engine and content. The source-window and value-tree renderers are shared primitives, reused by
  the DAP variables view and the TUI, so the ergonomics carry across every surface.

This rich REPL is line-based scrollback; the full-screen TUI is a later, separate surface.

### MCP

New tools under `maxon-dev-mcp/mcp/`, modelled on `RunProgramTool.maxon`, registered as `ToolSpec`
rows in `toolRegistry()`. MCP is stateless per call, so a batch shape suits agents:
`debug_run(compiler, source, breakpoints[], commands[])` → structured stops with backtrace + locals.
High value given Maxon is designed to be written by AI agents.

### VSCode (DAP)

The extension already declares the `Debuggers` category and a `breakpoints` contribution but has no
debug adapter. Add a `debuggers` contribution and a thin DAP↔engine bridge that spawns
`maxon debug --dap`, mirroring how the extension spawns `maxon lsp-server`.

### Text GUI (TUI, last)

A greenfield full-screen UI (source + call stack + GTs + locals + breakpoints + command line) over the
same engine and the same shared renderers. Deferred until the engine and CLI are proven.

---

## Coverage

- **Default (agent-driven, pristine binary):** the driver has the agent set breakpoints at every
  statement boundary and records which fire — cheap now that trap handling is in-process (an external
  OS-level design would trap-storm here). Exhaustive line/branch coverage, no extra build.
- **Fast path (`--coverage`, an instrumented variant, like gcov):** a `__cov_counters` array + one
  increment per point, dumped to `<binary>.mxcov` on exit; optionally streamed as a `COV` DebugStream
  family for live coverage. Opt-in, a separate axis from debug info, never the production default.

The driver merges hits/counters against the sidecar coverage-point table into a report / LCOV-ish JSON.

## Profiling

- **Default (agent sampling, pristine binary):** the agent periodically captures each GT's PC + rbp
  chain in-process; the driver symbolizes via the sidecar and aggregates into a call tree / flamegraph.
- **Instrumentation / phase (opt-in):** reuse the zero-alloc `__DebugStream.phaseBegin/End` +
  `LOG_EVENT` builtins under `--debugstream`; the driver aggregates from the ring.

---

## shv2 port

shv2 already has the hooks: `CodeResult.DebugSymbol` / `DebugSymbolIndexMap`, a block-granular
`BlockTextOffsetMap`, the `LayoutDescriptor` runtime type metadata, the `MetricsEmit` sidecar-writer
template, and object writers for all three OSes. Port: source-span threading through the pass pipeline;
`.mxdbg` emission in the same format (so the C# driver debugs shv2 binaries unchanged); the `__dbg_*`
agent written in Maxon; richer loclists (shv2 has a real register allocator); and a **different
breakpoint mechanism for wasm** — wasm code is immutable, so `INT3` patching does not apply, and
`wasm32-wasi` needs compiled safepoints or wasmtime debug hooks. Read the maxon-sharp implementation
first; port the lessons, not the cost.

---

## Phases

| # | Milestone | Acceptance |
|---|-----------|-----------|
| P0 ✅ | This design doc + format spec | doc committed |
| P1 ✅ | Source spans; `.mxdbg` header/strings/files/funcs/line table + `__build_id`; retire COFF; sidecar default-on (`--no-debug-info` opts out) once scale-test clears the cost; `maxon debug --dump-info`/`--symbolize` | exe byte-identical with/without `--no-debug-info`; PC↔file:line round-trips; span-capture cost measured (scale-test); suite green |
| P2a ✅ | Type table + fields + per-function frame size (v1→v2 format); folds in the P1 line-precision fix | dump shows each type's kind/size/fields + each function's frame size; byte-identical; deterministic |
| P2b ✅ | **Local location-list capture.** A per-function `(name→sourceType)` side-table captured in `MaxonToStandard` (single-threaded) before ABI erasure, carried on `IrFunction` through lowering, joined with the emitter's `varOffsets` (name→rbp slot); one `AddLocal` per named user-code local. Struct/enum/String/`self` bind to their real type; int/float/bool and ranged aliases bind to the honest base primitive; a slot whose name is reused with a conflicting type, or a value the optimizer kept only in registers, is **omitted** (honest "can't say") rather than mislabeled. dump prints the frame-pointer register per triple (`rbp`/`x29`). | dump shows each user local's location + type; byte-identical; deterministic |
| P2b-resid ◑ | **Two accepted residuals of P2b** (not defects): (1) **type-table reachability filter** — the table still emits every renderable `typeDefs` type (~300; size-only, and signatures are erased at emit so filtering can't recover struct types from them). (2) **scope-accurate multi-entry loclists** — the one-slot-per-name model omits conflicting/register-only locals rather than describing them precisely; the richer per-scope loclist is a later rung (shv2's real regalloc needs it anyway). | — |
| P3a ✅ | **Dormant agent substrate.** `__dbg_init` (one `getenv(MAXON_DEBUG)`, dark-when-unset) → maps a control segment + announces a handshake (magic/version/`alive`, released last) + installs a trap handler that chains ahead of `__gt_fault_handler` (Windows VEH front-of-chain / POSIX distinct SIGTRAP). Always emitted; `--no-debug-agent` opts out (the one exe-changing flag; `--debug-info` stays byte-neutral). Shared-mem helpers extracted and shared with `__ds_*`. `maxon debug --attach-probe` seeds the driver. | agent dark when unset; probe reads handshake; panic backtrace byte-identical dark-vs-armed; spec 3103/0; `--no-debug-agent` smaller |
| P3b ✅ | **Breakpoints + execution control.** Mailbox (command/ack + stop-event, segment v2), `__dbg_set_bp(offset)` patches `INT3`/`BRK` into `.text` (W^X flip: x64 `VirtualProtect`+`FlushInstructionCache`, arm64 `mprotect`+`sys_icache_invalidate`), park (stop-the-world spin+yield) / continue with single-step-over (x86 trap-flag; arm64 temp-bp-at-pc+4), non-bp faults defer to `__gt_fault_handler`, shutdown disarms all + guards on `__dbg_base`. **Bounds-checked** (`set_bp` offset validated against `&symtable − &mrt_start` — no arbitrary write). `maxon debug --bp-test` proves stop→continue on x64. Reviewed: 5 findings fixed (the security bounds-check, clear-stopped-bp corruption, W^X len, harness verdict, watermark). | `--bp-test`: STOP at exact offset, continue, correct exit; byte-identical; panic-intact; spec 3103/0 |
| P3b-resid ◑ | **P3b residuals** (host-unverifiable / P4): the entire **arm64** patch/trap/single-step path is emit-verified only (no macOS host); **arm64 branch-first-instruction** breakpoints need displaced stepping (pc+4 assumes fall-through — fine for entry/prologue); ~~**stop-the-world** is the AGENT's single-M MVP — its park blocks the trapping worker OS thread, which halts every GT multiplexed onto it.~~ ✅ **CLOSED by P4d-2b**: a stop still parks only the trapping processor (`--this-gt`, the default, now a NAMED setting rather than an unstated MVP), and `--stop-others` holds every other green thread for the duration of a stop. Per-GT `gt-park` / `gt-resume` give the finer grain the residual asked for. All three are COOPERATIVE — the hold is applied at `__gt_dequeue`, the one place the scheduler decides what runs — so a thread already ON a processor keeps running until it next reaches the scheduler, which is reported as `pending` rather than `held` and REFUSED rather than queued. ⚠ The **RUNTIME is genuinely multi-M** (`SpawnWorker` issues a real `CreateThread`, `X86CodeEmitter.Backend.cs:546-574`; `__gt_enqueue` spawns workers up to `__sched_max_procs`, seeded from `dwNumberOfProcessors`), so per-GT park/resume is a CONCURRENCY rung, not bookkeeping — plan P4d-2 against that, not against a single-M model; macOS JIT-entitlement friction for the `mprotect` flip on hardened-runtime binaries. | — |
| P3c ✅ | **The `MaxonDebugger` driver + rich REPL core.** A single engine (`MaxonDebugger`) spawns the target stopped-at-entry over the control segment, validates the `.mxdbg` build-id against the binary's own `.text` (`BinaryBuildId`, PE + Mach-O), and posts mailbox commands / reads stops through the ONE set of `RuntimeEmitter.Dbg*` constants. The agent grew a `backtrace` command (segment v2→**v3**): `__dbg_backtrace` walks the stopped frame's saved-rbp chain with `mrt_fault_backtrace`'s discipline (stack-window + ascending-guard + `.text` bounds) into a bounded frame array; the driver symbolizes each (frame 0 exact, 1..N return-address-biased). The REPL (`MaxonDebugRepl`) — interactive `break file:line`(b)/`run`(r)/`continue`(c)/`backtrace`(bt)/`quit`(q) with a location-aware prompt, plus `--batch --commands=<file\|inline>` emitting one JSON event per stop — auto-renders a source-context window (current line marked `→`) + a symbolized backtrace on every stop, through shared renderers. Breakpoints are set by **file:line** (resolved via the sidecar line table; "no code at that line" is honest). `DebugAgentProbe` (`--attach-probe`/`--bp-test`) refactored onto the same engine. | headline batch demo: STOP at file:line symbolized + source window + backtrace g←f←main + continue→right exit; byte-identical; dark-when-unset; panic intact; `--attach-probe`/`--bp-test` green; spec 3103/0; empty fragment diff |
| P3c-resid ◑ | **P3c residuals** (accepted, not defects): (1) **frameless-leaf blind spot** — a leaf function the bootstrap emits with no frame pointer (`push rbp` omitted, `frame=0x0`) is invisible to rbp-chain unwinding and makes its frame appear to be its caller's caller — the SAME limitation `mrt_fault_backtrace` has; forcing frame pointers everywhere is a codegen change that would break byte-identity, so it is out of P3c's scope (a future rung: frame-pointer forcing or CFI-based unwinding). The backtrace stays HONEST (it shows what the rbp chain actually holds); the headline demo breaks in a non-leaf so the chain is genuinely 3-deep. (2) **arm64 backtrace + Mach-O build-id host-unverifiable** — `__dbg_backtrace` emits cleanly through the ARM64 backend (platform-neutral) and `BinaryBuildId`'s Mach-O parser validates a real arm64 binary's build-id, but neither runtime path can be RUN on the x64 host (same status as the rest of the arm64 agent). | — |
| P4 (sliced) ◑ | The largest rung, split into sequential slices P4a–P4e, each its own wave — and P4d itself further sliced into P4d-1 (breakpoint semantics ✅) and P4d-2 (green threads). | step + navigate a value tree by path; `break … if`; break one GT while others run |
| P4a ✅ | **Value inspection.** Agent `read-mem` command (control segment v3→**v4**: `__dbg_read_mem` does a bounded, allocation-free copy of debuggee memory at the parked stop into a segment buffer at `0x280`, cap 512B); driver `ReadMemory` (chunked, version-gated on `DbgReadMemMinVersion`) + `DbgValueRenderer` that resolves the stopped function's named stack-slot locals via the P2 loclist (`fp + signed slot`) and renders type-aware **value trees** from the sidecar type/field tables (int/ranged/bool/float, `String` as text+length, struct as named-field subtree, enum/union by case matched on the **runtime discriminant tag**, Array/managed as pointer/length), with lazy path navigation (`print a.b.c`) and honest error nodes. REPL `print`(p)/`locals` + batch JSON through shared text/JSON renderers. First runtime consumer of the P2a/P2b tables. Reviewed: dedup of 6 window-scans; **a wrong-answer fixed** — the enum sidecar tag recorded the ordinal while codegen stored the raw value, mislabeling int-backed enums, cured by single-sourcing `IrEnumCase.TagValue`. | headline `--batch` over `DebugSamples/values.maxon`: `locals` + `print <struct>` render a named-field tree, `print a.b.c` navigates by path, String prints as text+len, an int-backed enum renders the correct case; byte-identical; dark-when-unset (exit 42); panic-intact; spec 3103/0; empty fragment diff; golden `values.expected.txt` |
| P4a-resid ◑ | **P4a residuals** (accepted, honest — none is a wrong answer): (1) **ranged-alias collapse** — a local/field typed `Age`/`Population` resolves to its base `i64` before `DebugInfoBuilder` sees it, so it renders as a `Primitive i64` (the VALUE is right; the range-type name is lost). The `IntRanged`/`FloatRanged` render paths are correct and reached only via enum-case payloads. Fixing local/field ranged capture is a P2 enhancement. (2) **enum tag u32 truncation** — a negative or >2³² int-backed enum raw value truncates in the u32 field-offset slot and renders as an honest `Enum(#tag)` (never a wrong case; a full-i64 discriminant cannot collide with a non-negative stored tag). (3) **float-backed enum** — stores f64 bits the u32 tag slot can't hold, so it renders as honest `Enum(#tag)` unresolved. (4) **Array typed-element expansion** — the Array type entry carries no element typeId, so only `Array(len=N)` is shown. (5) **in-agent fault guard** — `__dbg_read_mem` does a raw copy; the driver renders null pointers as `null` without asking and only reads addresses derived from valid locations, so a live stop is safe, but following a dangling pointer (path-nav on a freed value) could fault the target. A hardware fault guard is deferred. | — |
| P4b ✅ | **Source-line stepping.** One new agent primitive — `DbgCmdStep` (single-step one instruction, publish reason=step; control segment v4→**v5**, adds a tri-state `__dbg_step_mode` None/OverBp/User read by the trap handler, and `DbgOffTextBase` so the driver converts a stack return-address to a code offset) — reusing the P3b single-step (x86 EFLAGS.TF / arm64 temp-bp-at-pc+4). The driver holds the POLICY: `StepInto`/`StepOver` (via `WalkToNextLine`, stopping on the next statement-flagged line), `Finish` (temp bp at the caller's return via the P3c backtrace), `Until <line>`, all bounded and clearing every temp bp in a `finally`. REPL `step`/`next`/`finish`/`until` through the shared stop renderer. Optimizer fixed a real triggerable O(N·F): `FunctionAt`'s per-instruction linear scan → O(log F) lazily-sorted partition-point. Review + coordinator closed **three bp-interaction defects**: a step-onto-a-bp double-report (continue now steps over any armed bp at the resume PC), a temp-bp/user-bp collision that deleted the user's bp (driver now tracks user-bp offsets and never clears a coinciding temp), and the FunctionAt dedup through the shared `PartitionPoint`. Caught + fixed a latent arm64 unconditional-`step_addr`-rearm bug. | headline `--batch` over `DebugSamples/step.maxon`: `step`→helper:15 (descends), `next`→helper:16 (runs the call), `finish`→main:21 (returns); byte-identical; dark-when-unset (exit 8); panic-intact; continue-past-bp intact; spec 3103/0; empty fragment diff; golden `step.expected.txt` |
| P4b-resid ◑ | **P4b residuals** (accepted — none corrupts state or misreports; each degrades honestly): (1) **`finish`/`next` do not pause at a user breakpoint hit during their run** — gdb's "breakpoints win" semantics; the run reaches the correct return and PRESERVES the bp (per the collision fix), it just does not stop en route. Cleanly fixable now via the user-bp registry; **folded into P4d** (bp-aware execution belongs with the breakpoint machinery). (2) **arm64 branch-first stepping** needs displaced stepping (temp-bp-at-pc+4 assumes fall-through) — inherits the P3b limitation; x86 EFLAGS.TF is unaffected. (3) **`step`-into a no-line runtime frame** single-steps it (bounded by `MaxStepInstructions`, reports `LimitReached`) rather than stepping over — a "skip no-debug-info frames" refinement. (4) **tail-call callee entry** in step-over reads a non-return `[Sp]` and degrades to run-to-completion (never a crash/hang). (5) **frameless-leaf `finish`** reports `NoCallerFrame` honestly (rbp-walk can't unwind it). (6) the whole **arm64** step path is emit-verified only (no host). | — |
| P4c ✅ | **REPL ergonomics.** Fuzzy `break <function>` — `SetBreakpointAtFunction` ranks exact → qualified `Type.method` → leaf → prefix (edit-distance is NOT a resolution tier; a typo suggests but never arms), reports **ambiguity** with the candidate list (never silently resolves), and arms at the function's first-statement offset (past the prologue, `CodeStart` fallback for a no-line-row stdlib fn). One shared `DebugFuzzy.ClosestMatch` (Levenshtein) drives "did you mean" for both an unknown command and a no-match break. An interactive `LineEditor` — Tab-completion (commands, then funcs/files by context, then the stop's locals), ↑/↓ persistent history, Ctrl-R reverse-search — over a **pure** `DebugCompletion.Complete` engine, with a plain-`ReadLine` fallback when stdin is not a TTY. `maxon debug --complete '<partial>' <exe>` exposes the engine for batch tests + editors. Command vocabulary collapsed to one `CommandTable`. Driver/REPL only — no compiler/codegen/agent touch, so byte-identity/dark/codegen-neutral hold by construction. Review single-sourced two cross-boundary leaf/word-boundary duplications. | `--batch`/`--complete` over `DebugSamples/step.maxon`: `break helper`→stops in helper:15; `break init`→ambiguous `[City.init,Person.init]` (unarmed); `break deepr`→no-match+`deeper`; `--complete` lists commands/funcs/files; unknown cmd→"did you mean"; spec 3103/0; P4a/P4b goldens intact; golden `complete.expected.txt` |
| P4c-resid ◑ | **P4c residuals** (accepted — none is a wrong answer; all honest MVP limits): (1) the interactive **key-loop** (Tab/↑↓/Ctrl-R keypresses) is manually-verified only — `--batch` cannot drive keystrokes; the completion LOGIC is gated by `--complete`. (2) **Ctrl-C** is left at the OS default (terminates) — "Ctrl-C = async break, not kill" needs the running-target interrupt, which is **P4d**. (3) single-line repaint — a console-width-wrapping line isn't repositioned perfectly (cosmetic; the buffer/submit text is always correct). (4) `--complete` offers no **locals** (no live stopped frame in the static path). (5) the "did you mean" pool is full names not leaf segments (`break ini` suggests nothing), file completion offers the leaf but `break <file>` still needs `:line`, and a broad prefix (`break s`) lists many candidates — all verbose/rough, never wrong. | — |
| P4d ◑ | **Green threads + conditional breakpoints + bp-aware stepping.** SLICED into **P4d-1** (breakpoint semantics ✅) and **P4d-2** (green threads), and P4d-2 further into **P4d-2a** (visibility ✅) and **P4d-2b** (control ✅). ◑ only until **P4d-GT-STACK slice B** (the g0 cleanup) lands; slice A shipped, and P4d-2b closed the P3b stop-the-world residual. | `break … if`; break one GT while others run; `finish` stops at an intervening breakpoint |
| P4d-1 ✅ | **Breakpoint semantics.** (a) **`finish`/`next` honor an intervening user breakpoint** (closes P4b-resid #1): `RunUntilReturn`'s frame guard mistook every equal-or-deeper hit for recursion, so an armed bp inside a callee was silently run past. One shared predicate, read at STOP time. (b) **`break <target> if <local> <op> <literal>`, evaluated IN-PROCESS**: control segment v5→**v7**, a `__dbg_bp_cond` table parallel to the breakpoint table (same slot index, so `__dbg_bp_slot` stays the ONE address→slot map), and `__dbg_cond_holds` called from `__dbg_on_breakpoint` between the slot lookup and the stop publish — a false condition skips publish+park and still returns 1, so **neither trap thunk changed on either architecture**. Grammar deliberately narrow (scalar stack local vs int/bool literal); a dotted path, float, String, or local-vs-local is REFUSED **unarmed**, never approximated. Unrecognized record ⇒ STOP (over-stopping is visible; silent skipping is not). **Measured: a false hit costs ≈8 µs and is LINEAR** (2 kernel exception dispatches + 4 `VirtualProtect` + 2 `FlushInstructionCache` per hit — the emitted evaluator is branch-only and is NOT the cost; removing it means hardware debug registers, a future design rung). Review + optimizer + coordinator closed **five reachable wrong answers the green suite never touched**: a step runner sharing a CONDITIONAL user bp that may never fire (`until`/`finish` fell through to the entry stub); a breakpoint the agent DROPPED at the 16-slot limit reported as `set` (cured by `DbgOffCmdResult` — the ack now says "I could do it", not just "I processed it"); a stale-stop report after a timeout; a stale condition inherited on re-arm; and a `--stop-timeout=1e30` overflow crash. Duplication single-sourced: operand widths + operator vocabulary (one table the emitter emits arms from AND the driver validates against), and "how is this type read as a machine integer" (was in `DbgValueRenderer` and again in the driver — divergence would make `print x` disagree with `break … if x == <that value>`). | headline: `bpstep` (4 cases) + `cond` (6) + `timeout` (3) goldens; 3103/0; 21/21 golden checks; byte-identical; empty fragment diff |
| P4d-1-resid ◑ | **P4d-1 residuals** (accepted, none a wrong answer): (1) **operand widths 2 and 4 are UNREACHABLE** today — a ranged local collapses to base `i64` before `DebugInfoBuilder` sees it (**P4a residual #1**), so those arms are emit-only; they were probed correct via a temporary forced override, and fixing P4a-resid #1 activates them. (2) A conditional bp **wins a `step`/`next` walk without its condition being evaluated** — the trap never runs during a single-step walk and the driver must not become a second evaluator; errs toward a SPURIOUS stop, the same direction the agent takes for any shape it cannot recognise. (3) `BreakKind.Unacknowledged` covers two causes (no ack / agent refused); the outcome is correct (the bp is **not** set), only the reason is coarse — splitting it needs a reason code in `DbgOffCmdResult`. (4) No fault guard on the condition read (inherits P4a residual #5), bounded because a condition only arms at a statement offset in a function with a recorded stack local. (5) `DebugStreamMonitor.cs:297-302` hand-rolls the "target gone before joining pipes" pairing that `EndSession` now single-sources — a third copy in a different subsystem, wanting its own rung. | — |
| **P4d-GT-STACK-A** ✅ | **FIXED 2026-07-25 — a breakpoint (and a fault) on a green-thread stack now works.** The reserve is `GtOsFaultReserve = 0x1800` (6 KB), counted TWICE — `GtInitialStackSize = GtMaxonStackSize + reserve` and `GtStackGuardMargin = GtUncheckedFrameMargin + reserve` — so `__gt_morestack` maintains it across grow/relocate. **DERIVED, not copied from Go:** a native VEH probe measured **2577 B consumed before any handler code runs** (`EXCEPTION_RECORD` 152 B, `CONTEXT` 1232 B with no XSAVE area appended, plus ntdll dispatch frames), and the runtime's deepest trap chain adds **1336 B** ⇒ 3913 B worst case, rounded to a page + 2048 margin. **The margin is FREE:** 6 KB makes the stack exactly two 4 KB pages and `VirtualAlloc` commits whole pages, so 4 KB would cost the same memory for less safety. **Cost measured independently: +3937 B/thread = one extra page** (the old 2 KB request already committed 4096); spawn throughput unchanged (40.47→40.48 µs); peak committed stays O(depth) and relocations HALVE (4→2 at depth 640, identical peak). Also: `SA_ONSTACK` on the arm64 debug TRAP handler (the fault handler already had it; the omission's own comment — *"a breakpoint is not a stack-overflow fault"* — was the bug stated as a reason), and a driver-side **crash classification** (`{"event":"crash","status":"0x…"}`, driver exits NONZERO) closing the contract violation where a crashed debuggee reported success. ⭐⭐ **THREE MORE DEFECTS FOUND AND FIXED, none of them breakpoints:** (1) the stopped-thread backtrace faulted INSIDE the trap handler (`__dbg_backtrace` bounded by the 64 MiB `FaultStackWindowBytes`, right for an OS stack, one word off the end of an 8 KB one); (2) `mrt_panic` and `mrt_fault_backtrace` did the same on BOTH arches; (3) ⭐ **the stack-overflow diagnostic could never print** — `__gt_fault_handler`'s redirect used a bare `gt->stack_base + 4096`, and that branch is reached ONLY when `stack_base == 0` (a P's inline main-thread GT), so it always computed `0x1000` in the NULL PAGE. **Measured: unbounded recursion in `main` died `0xC0000005` with NO OUTPUT AT ALL while the runtime held a `panic: stack overflow` it could not print; now it prints the panic + backtrace and exits 1.** Cured by one derived two-arm rule and one shared `__gt_stack_high` / `FrameLinkBytes`, leaving `FaultStackWindowBytes` exactly ONE use. The review verified the two "my change caused this" claims by WIDENING THE WINDOW (forcing the pre-fix bound back and watching each crash) rather than taking them on trust. Pre-existing since P3b; found 2026-07-24 while writing P4d-2a's sample, and **reproduced by the coordinator**: in one program, breakpoints in `main` (lines 29/31/32/33, the OS-thread stack) arm, stop and exit 3, while a breakpoint at `slowTask:18` (a spawned green thread) arms and then dies with **`0xC0000005` STATUS_ACCESS_VIOLATION — no stop event AND no panic backtrace**, i.e. the process is gone before any handler runs. Reproduces under `MAXON_MAX_PROCS=1`, so it is not a multi-M race. **Mechanism:** Windows delivers an exception on the *current thread's* stack — `KiUserExceptionDispatcher` writes a `CONTEXT` (1232 B) + `EXCEPTION_RECORD` (152 B) + machine frame, ~1.5 KB below RSP, **before any VEH runs**. A green-thread stack is `GtInitialStackSize` = **2048 B** total (`GtLayout.cs`) and `__gt_morestack` sizes only to fit the *frame*, leaving `GtStackGuardMargin` = **928 B** of slack — so the kernel's write runs off the stack into unmapped memory. **Graded, not binary** (measured): raising the margin alone to 2560 B lets the stop publish but then dies deeper in the park loop, so **a margin bump is NOT a sufficient fix**. **⭐ P4d-2a DOES NOT WORSEN THIS — measured, not reasoned.** The async-breakpoint crash is **identical on base and branch** (`{"event":"exit","code":-1073741819}` both), because the debuggee dies **before any stop is published** — i.e. before any agent frame exists (coordinator-reproduced on the merged branch at two separate armable lines). What changed is the headroom a FIX must provide: **+736 B more**, of which **432 B is recoverable** by trimming the nine new `__dbg_*` frames (provisioned 8–16 slots for 1–7 used) — deliberately not trimmed, because `0x40/0x60/0x80` is a uniform convention across all 29 `__dbg_*` functions and a hand slot-audit inside a trap handler fails nondeterministically. **⭐⭐ USER RULING 2026-07-25: ADOPT GO'S STRUCTURE** ("we should just copy what Go does") — per-M system stack with **g0 = the M's own OS thread stack** (no synthesized region, and no TIB repointing, since the thread's own bounds are already right), `sigaltstack`+`SA_ONSTACK` on POSIX, and the Windows fault headroom reserved **inside** the stack counted in both the allocation and the guard. The coordinator's earlier "red zone below the stack base" idea is **withdrawn** — shv2's morestack relocates stacks, so a below-base zone would need re-establishing on every relocation, whereas an in-stack reserve is maintained by `morestack` for free. ⚠ **Copy the STRUCTURE, MEASURE the NUMBERS** — `2048`/`928`/`512*PtrSize` are Go's frame sizes, and copying Go's constants without Go's structure is the bug being fixed here; doing it again would mirror it. Full ruling + the shv2 twin: `maxon-shv2/PLAN.md`, "Future rungs". **⭐ THE FIX IS TO FINISH A HALF-DONE PORT, NOT TO INVENT ONE.** Go hit exactly this and solves it two ways, and Maxon has half of each. **(1) POSIX — `sigaltstack`:** Go gives each M a dedicated signal stack and registers handlers `SA_ONSTACK`, so the kernel switches stacks and never touches the 2 KB goroutine stack. **Maxon ALREADY DOES THIS for the FAULT handler** (`ARM64CodeEmitter.Runtime.cs:7467` `SaSiginfo|SaOnstack|SaRestart`, plus a per-worker altstack at `:7499`) — but the **DEBUG TRAP handler deliberately omits `SA_ONSTACK`** (`:7674-7680`, reasoning "a breakpoint is not a stack-overflow fault"). That reasoning is the bug: the hazard is not an *overflowed* stack, it is a *tiny* stack meeting a *large* signal frame (Darwin arm64 siginfo+ucontext+mcontext includes 528 B of NEON state alone). **(2) WINDOWS — there is no altstack for VEH**, so Go instead RESERVES the space inside every goroutine stack via `_StackSystem` (= `512*PtrSize` = **4 KB** on windows/amd64), counted **TWICE**: in the allocation (`_FixedStack0 = _StackMin + _StackSystem`) **and** in the guard (`_StackGuard = 928*mult + _StackSystem`), so `morestack` fires early enough that the reserve is still intact. **Maxon copied `_StackMin` (2048) and the `928` from `_StackGuard` but DROPPED `_StackSystem` entirely** — and that term exists for precisely this problem. The comment "matches Go's `_StackGuard` on amd64" is therefore wrong: Go's is 928+`_StackSystem` (5024 on Windows), not 928. Classic ported-the-value-not-the-reason. ⚠⚠ **THE COROLLARY WAS WRONG FOR THE BOOTSTRAP — corrected by measurement 2026-07-25.** I predicted a CPU fault on a green-thread stack would lose its panic on Windows. **It did not:** on the PRE-fix compiler a `forceSegfault()` inside an `async` function printed a full symbolized panic and exited 1. The reason is page granularity — a 2048-byte `VirtualAlloc` already commits 4096, and the FAULT chain is only 216 B deep, so it fit; the BREAKPOINT chain is 1336 B, which is what pushed past the page. Same mechanism, opposite outcome, decided entirely by which handler chain runs. **It WAS true for shv2** (measured: empty stderr, `0xC0000005`), which is filed on shv2's own ladder. Reasoning from the mechanism got this wrong; measuring settled it. Original per-platform note kept below for the record:  on **Windows** `__gt_fault_handler` is a VEH on the current stack, so the panic backtrace has the SAME exposure (corollary likely TRUE — a runtime defect hitting programs that never attach a debugger); on **macOS/arm64** the fault handler already has `SA_ONSTACK` + per-worker altstack, so it is likely PROTECTED (corollary likely FALSE there); the macOS/arm64 **trap** handler shares the Windows exposure but is host-unverifiable. Any Windows fix carries a global memory-footprint tradeoff that must be MEASURED (a `_StackSystem` equivalent means every GT in every program pays; gating it on `__dbg_base != 0` keeps dark cost at zero and byte-identity intact but does NOT fix the panic-backtrace exposure). **⭐ SHIPS WITH THIS RUNG — `maxon debug --batch` EXITS 0 WHEN THE DEBUGGEE CRASHES** (coordinator-verified 2026-07-25: `break <async fn>; run` ⇒ `{"event":"exit","code":-1073741819}` and the **driver returns 0**). That contradicts the batch surface's own documented contract (`MaxonDebugRepl.cs:445-448`: *"The driver exits NONZERO for all three: CI must not read any of them as a pass, and a missed breakpoint least of all"*) — `BatchContinue` maps `StopWaitStatus.Exited` to `Finished` and never to `Incomplete`, so a crashed debuggee is exactly a missed breakpoint that reports success. **Pre-existing and untouched by P4d-2a** (identical on `80553e65a`), but this defect makes it the COMMON case. It ships HERE, not separately, because fixing only the reporting turns a silent crash into a loud crash without making the breakpoint work — they are one user-visible story. Needs a per-OS abnormal-termination classification (NTSTATUS on Windows, `WIFSIGNALED`/signum on POSIX), a `{"event":"crash",…}` shape distinct from `exit`, and a non-zero driver exit — which moves EVERY batch golden's final line, so it must land with an acceptance case that can actually produce a crash. This rung is the first that has one. **P4d CANNOT be marked ✅ until this lands.** **SLICED by the coordinator (2026-07-25), because the ruling's cleanup is not needed to fix the defect:** **SLICE A (the FIX)** — Windows fault headroom reserved INSIDE each GT stack counted in both the allocation and the guard (the `_StackSystem` shape), plus `SA_ONSTACK` on the arm64 debug TRAP handler (the fault handler already has it), plus the crash-exit-code reporting above. **SLICE B (the CLEANUP)** — adopt g0 = the M's OWN OS thread stack, retiring the per-P 64 KB `VirtualAlloc` region and the TIB `StackBase`/`StackLimit` repointing it forces on every Win32 call. B is a measurable simplification and a memory win, but A is what makes a breakpoint in a green thread work, so A goes first and B does not block P4d. | a breakpoint inside an `async` function stops normally; a CPU fault on a GT stack still symbolizes its panic; a crashed debuggee makes the driver exit NONZERO |
| P4d-2a ✅ | **Green threads: VISIBILITY.** **⭐ OPTIMIZER — measured, NO code changes.** Trap-path depth **+736 B worst case** (`gt-backtrace`) and **+344 B** on `backtrace` (which the REPL renders at EVERY stop), measured by decoding the literal prologue bytes of every `__dbg_*` function via the binary's own `.symtab` on both commits; every pre-existing frame is byte-for-byte identical and nine functions are new. **+736 B is 79% of `GtStackGuardMargin` (928 B).** The `backtrace` growth is not the GT code — it is `__dbg_backtrace` refactored from a flat call-free loop into `→ __dbg_walk_frames → __dbg_frame_ra → __dbg_text_offset`. **The publish (304 B) and park (240 B) paths are UNCHANGED**, so what decides whether a stop can be taken at all did not move. Constant, not growth: `.text` **+1,505 B**. Stepping **+4.5 µs (+2.3%)**, inside noise; the only per-resume cost is one integer increment. ⚠ The coordinator's premise that `DbgOffGtStopped` is computed on every stop was **WRONG** — it is written only inside `__dbg_gt_scan`, reached solely from `DbgCmdGtList`/`__dbg_gt_backtrace`; `__dbg_publish_stop` is unchanged from base. **MEASURED DEBT (not fixed, deliberately): `__dbg_gt_scan` is O(P² + G·P)** — each active P's record calls `__dbg_gt_on_cpu`, which rewalks all P — but **flat across `MAXON_MAX_PROCS` 1/2/4/8/16** (1070/930/1010/1035/1110 µs; 10 µs in-process) and bounded at `DbgMaxGreenThreads`=32 with an allocation-free, cycle-safe walk. Removing the P² term would need a SECOND statement of the park gate (which `__dbg_p_at`'s comment exists to prevent) and still leave O(32·P); the real fix is an owning-processor field in the `GreenThread` struct — a RUNTIME change. **⭐ TRIGGER, written into P4d-2b's contract: a many-processor host combined with P4d-2b polling the list in a loop, or raising `DbgMaxGreenThreads`.** One reading reported WITHOUT an explanation: 1000 `threads` commands run ~200 ms longer in WALL time than 1000 `backtrace` commands at IDENTICAL CPU (60 ms both) — a wait, not compute; bounded at ~200 µs/command, invisible to the in-process instrument, not the enumeration. Recorded as an open reading rather than a guess. `threads`/`gts` lists every live green thread — driver id, the runtime's own status word, whether a processor is executing it, and its top frame — the stop event carries WHICH green thread is stopped and the list marks it, and `gt-backtrace <id>` walks one PARKED thread's stack. Control segment v7→**v8**: `DbgCmdGtList`/`DbgCmdGtBacktrace` plus a bounded record array at `0x4C0`, gated by `DbgGtMinVersion` because a v7 agent acks the unknown command and its zeroed count reads as a believable "this program has no green threads". ⭐ Enumeration is a **UNION** — the `__gt_all_head` walk PLUS each active P's inline `POffMainThread` GT — and the golden stops in `main` so the half `__gt_all_head` omits IS the stopped thread. ⭐ The **PARK GATE** is processor ownership ("is this any active P's `currentGt`"), **NOT `GtOffIoYielded`**: `__gt_spawn` initialises that flag to 1 and nothing clears it on resume, so a RUNNING thread carries it and gating on it would admit exactly the case it looks like it excludes; a running thread is refused honestly instead of walked. ⭐ The list walk is deliberately **UNLOCKED** — on POSIX the trap handler is a SIGTRAP handler, so taking `__sched_all_cs` would be a lock in a signal handler — so it validates and bounds instead, following `__gt_cleanup`'s own precedent. ⭐ Ids are the DRIVER's: identity = (handle, entry, processor, **resume epoch**), because measured twice, a completed thread's struct is popped straight back off the per-P free list — once handed back CROSSED (the entry function caught it) and once with the SAME entry function, where a handle+entry identity silently transferred a dead thread's id to a live one. One shared walk (`__dbg_frame_ra`/`__dbg_walk_frames`) now serves the stopped-thread and per-thread backtraces, and one shared `__dbg_text_offset` validates every absolute code address. New `--target-env=NAME=VALUE` sets a variable in the debuggee (gdb's `set environment`), which is what makes a concurrency transcript reproducible at all. | golden `threads.expected.txt`: the union with the stopped scheduler thread marked, two parked threads backtraced, an unknown id refused by name, and second-wave ids that do not reuse the completed pair's; **25/25 byte-identical**; 3103/0; 22/22 golden checks; byte-identical exe; dark-when-unset; empty fragment diff |
| P4d-2a-resid ◑ | **P4d-2a residuals** (accepted; none is a wrong answer): (1) **multi-processor enumeration is not covered by the golden** — `MAXON_MAX_PROCS=1` is what makes the transcript byte-stable, since the scheduler otherwise spawns workers on demand and the `kind:"scheduler"` row count becomes machine- and timing-dependent. The single-processor run still covers both halves of the union and both sides of the park gate; the multi-processor case, and the one refusal that needs it (a thread found RUNNING on another processor), belong to P4d-2b, which can hold a thread in that state deliberately. (2) **id transfer inside one epoch** — with several processors a thread can complete and another be spawned into its struct for the same entry function WHILE the target is parked; closing that needs a monotonic spawn ordinal in the thread struct (`GtOffTraceId` is exactly one but is `--async-trace`-only), i.e. a runtime change rather than a driver one. Today an id is a display label, so the cost is a mislabelled row. (3) ⚠ **a breakpoint INSIDE an async function kills the debuggee** (`0xC0000005`, no stop event and no panic) — Windows writes its ~1.5 KB exception frame below RSP before any handler runs and a green-thread stack is 2 KB with only `GtStackGuardMargin` of slack, so the kernel's write runs off the bottom. It is upstream of the agent (a runtime/scheduler defect with its own rung) but it lands HERE in practice, because green threads are this rung's whole subject and stopping inside one is the first thing a user will try; the sample's header says so and the golden stops in `main` instead. P4d-2a also deepens the trap-time call chain on that path, tightening the same margin. Related: the driver renders an access violation as an ordinary `{"event":"exit","code":-1073741819}`, so a consumer cannot tell a crash from a return — pre-existing, but newly common. (4) **ids renumber on every resume** for every spawned thread, recycled or not, so `threads; next; threads` renames the same live threads and an id from before the step is refused. That is the epoch doing its job, but it is what a user actually experiences and it wants a friendlier answer than a bare refusal. (5) an **idle worker reads `status:"running" cpu:"on-cpu"`** and its backtrace is refused, because its M really is executing its scheduler thread — honest under the stated definition, but the least informative reading in the most common multi-processor case; `POffIdleFlag` answers it exactly and is not carried in the record. (6) a parked thread's TOP frame is usually the stdlib frame it parked in (`stdlib.sleep`), not the user function — honest, and `gt-backtrace` shows the user frame directly beneath it. (7) the whole path is **x64-verified only**; it is emitted through the platform-neutral builder and `LoadCurrentP`, so arm64 is emit-verified like the rest of the agent. |
| P4d-2b ✅ | **Green threads: CONTROL — and the P3b stop-the-world residual CLOSED.** `gt <id>` switches the inspection surface to another green thread (`backtrace`, `print` and `locals` all follow it, and stepping is REFUSED while it is on — `gt` moves what the debugger LOOKS AT, never what the target RUNS); `gt-park` / `gt-resume` hold and release one thread; `--this-gt` (default) / `--stop-others` decide what a stop does to the rest. Control segment v8→**v9**. ⭐ **A HOLD IS A REFUSAL TO SCHEDULE, TAKEN AT `__gt_dequeue`'s RESULT** — the ONE place the scheduler decides what runs next, so every context switch INTO a green thread passes it, and the single exception, a P's own inline scheduler thread, is not a thread anyone can hold. **The choke point is DERIVED, not enumerated** (review-verified against all eleven `__gt_dequeue` callers — worker loop, `__gt_await`, `__gt_try_await`, `__gt_yield`, `maxon_sleep`, `__gt_cleanup`, and the net/pipe/io submit paths): every `__gt_context_switch` whose `to` is a green thread takes it from a dequeue a few instructions above, and every other switch targets `P->mainThread`. A LIST of callers is a second place to keep in step, and the one this rung first shipped named five of the eleven. A caught thread HAS NOT RUN YET, which is what makes the hold safe rather than racy: its saved rsp/rbp are the ones its last context switch wrote, and it cannot complete out from under a hold because completing requires running. `SuspendThread` was REFUSED outright (it can stop an M inside the allocator or holding the scheduler lock). ⭐ **THE COOPERATIVE LIMIT IS REPORTED, NOT HIDDEN**: a thread already on a processor reads `hold:"pending"`, a DISTINCT word from `held`, and `gt` / `gt-park` / `gt-backtrace` all REFUSE it in one shared sentence rather than queueing a request behind a hopeful timeout. ⭐ **THE TRAP HANDLER NEVER TOUCHES A QUEUE**: it stores agent words only; every queue manipulation — the held chain, and `__gt_enqueue` on release — happens on an ordinary scheduler thread inside `__gt_dequeue`, under the scheduler's own lock, which is what keeps the agent async-signal-safe while still being able to take it. A release is a doorbell, so the common case (a hold in force, nothing to release) costs ONE load. ⚠ **The version gate MOVED rather than gaining a sibling** (`DbgGtMinVersion` 8→**9**): the RECORD GREW (`TopFp`, which is what lets `print` read a selected thread's frame at all, and `Hold`), and a stride change is not a capability that can be missing — a v9 driver striding a v8 array reads the NEXT thread's handle as this thread's frame pointer — so visibility cannot outlive the stride and two gates here would be one number written twice. Dark cost: one load and one not-taken branch in `__gt_dequeue`. Also: the LISTING and the SCHEDULER read the same `__dbg_gt_should_hold`, so what the user is told and what the scheduler does cannot disagree; an unknown id is now told apart from a STALE one (ids are re-minted on every resume — P4d-2a-resid #4, made actionable); a processor's scheduler thread with no walkable frames says so instead of "not started" (measurably wrong the moment a second processor appeared); and one shared `__dbg_frame_next` states where a frame chain ends for both the walk and the per-thread top frame. | **BOTH P4d-2a coverage gaps closed.** `gtcontrol` (1 processor): an A/B pair differing by one `gt-park` — with it heldTask is still alive and parked at the second breakpoint, without it the thread has finished and the listing holds only `main`; plus `locals`/`print mark` reading **41 out of the selected thread's own parked frame** while the target is stopped in `main`, a stale-id refusal, a scheduler-thread refusal, a step refusal, and **exit 44** proving the round trip neither leaks nor wedges. `gtmulti` (2 processors): a **five-row multi-processor list** with a green thread `cpu:"on-cpu"` on the worker, every refusal that turns on it, and `--stop-others` showing `held` on the two off-cpu threads beside `pending` on the compute-loop one. **20/20 byte-identical for each of the four blocks**; 3103/0; shv2 1604/0; 27/27 golden checks; byte-identical exe; dark-when-unset; empty fragment diff |
| P4d-2b-resid ◑ | **P4d-2b residuals** (accepted; none is a wrong answer): (1) **`gt <id>` selects a THREAD, not a FRAME** — the frame is that thread's frame 0, so a `print` can only reach locals live at the point it parked. A `frame <n>` command would need the agent to publish a frame pointer per backtrace entry rather than the one it publishes per thread; deliberately not built, because nothing yet asks for it. (2) **An individually-parked handle can outlive its thread in one narrow race**: `gt-park` refuses an on-cpu thread, so a hold is always installed on a thread the dequeue filter will catch BEFORE it can run — except in the instruction window between another processor's dequeue returning a thread and that thread becoming its `currentGt`. A hold that misses that way leaves a stale handle which a recycled struct could match; the listing SHOWS the resulting hold and `gt-resume` clears it, so it is visible and curable rather than silent. Closing it entirely needs the same runtime spawn ordinal P4d-2a-resid #2 wants. (3) ~~**A thread still held when the program EXITS keeps its stack until process exit** — bounded by the process lifetime.~~ ⭐ **THAT WAS NOT A LEAK, IT WAS A WEDGE, and the REVIEW fixed it.** A held thread is off every queue, so `__gt_cleanup`'s drain never finds it and `__gt_live_count` never reaches zero — and the drain's "threads alive, nothing runnable" arm then waits on `__io_done_event` with an **INFINITE** timeout, so the debuggee never exits at all and the driver has to kill it. **Measured: `gt-park` on a green thread `main` does not await wedged the process 3/3 (killed at 15 s); after the fix, 10/10 clean exits at ~0.8 s** — and with a BETTER exit code than the un-parked control, which leaks the orphan (101) where the held one is now fully drained. Cured by ONE rule in the one predicate that already existed: `__dbg_gt_should_hold` stops holding once `__sched_shutdown_flag` is set, and `__dbg_gt_dequeue_filtered` reads that same flag as a second readmit doorbell (nothing calls the agent when the scheduler winds down, so a thread already on the held chain has no other way back). A hold is a tool for inspecting a RUNNING program; after `main` returns there is nothing left to hold it for. (4) **`POffIdleFlag` is still not in the record**, so an IDLE worker's scheduler thread reads `cpu:"on-cpu"` (P4d-2a-resid #5). Deliberately not added HERE: neither acceptance sample can produce an idle worker at a stop — `gtcontrol` pins one processor and `gtmulti`'s worker is busy spinning by construction — so it would be gate-untested surface, and what it fixes is a display imprecision rather than anything the park gate acts on. (5) **`--stop-others` is not lifted by a STEP**, only by `continue`: a step is still a stop, and one source-line step is up to `MaxStepInstructions` round trips that would otherwise re-enqueue and re-catch every thread in the program. (6) the whole path is **x64-verified only**; it is emitted entirely through the platform-neutral builder — no hand-asm on either backend — so arm64 is emit-verified like the rest of the agent. |
| P4e | **DebugStream correlation.** Driver decodes DebugStream trace events (`--debugstream` builds) alongside debug stops, correlating live mm/sched/log events with the stopped state. | a stop shows the correlated recent trace events |
| P5 | MCP `debug_run` + inspection | agent sets bp, gets stop+locals |
| P6 | Coverage (agent-driven + optional `--coverage`) | line/branch report |
| P7 | Profiling (sampling + optional phase) | flamegraph |
| P8 | VSCode DAP | breakpoints/step/vars in-editor |
| P9 | shv2 port (sidecar + agent-in-Maxon + loclists; wasm mechanism) | shv2 native binary debugs identically |
| P10 | TUI | full-screen session |

---

## Risks / notes

- **Self-modifying code / W^X.** Patching `INT3` into `.text` needs RX→RW→RX page flips; on macOS the
  hardened runtime treats this as JIT (`allow-jit` / `MAP_JIT`). Real signing friction — moved, not
  removed, versus the OS-debugger entitlement it replaces.
- **Security surface.** An always-present, env-activated debug-control channel grants roughly the same
  trust boundary as same-user OS debugging (shared memory is same-user ACL'd), but it survives in
  environments where OS debugging is disabled — hence the `--no-debug-agent` hardened opt-out, and
  optionally an activation token.
- **Handler coexistence.** The debug trap handler must be async-signal-safe and chain cleanly with
  `__gt_fault_handler`; install ordering matters.
- **Byte-identity is load-bearing.** Span/loclist capture and sidecar emission must be pure observers;
  guard it with the P1 gate and see it red before green.
- **Agent size.** A few KB in every binary, partly offset by retiring the COFF symbol table — the
  accepted cost of one uniform mechanism.
- **wasm** needs its own breakpoint mechanism (P9), since wasm code cannot be patched.
- **Honest "optimized out".** Some locals lack a readable location at some PCs; the sidecar says so
  rather than lying. Worse under shv2's register allocator.
- **ARM64 / macOS parity** is mandatory (`BRK #0`, Mach-O sidecar, STLR release ordering) and needs
  on-target verification.

## Error codes

Most debugger failures are *tool-side* (the driver refusing a stale sidecar, failing to attach) and are
reported as tool refusals with a nonzero exit — the way `DebugStreamMonitor` uses a dedicated
schema-mismatch exit code — **not** as compiler error codes. Any genuinely compile-time diagnostic
(e.g. an unrepresentable debug-info case) takes the next free number in the codegen band (5xxx) via
`docs/error-codes.txt` + `maxon error-codes generate`, with a live `csharp` claim in the same commit
that emits it. Reservations are deferred until a phase has such a diagnostic, to avoid dead claims.
