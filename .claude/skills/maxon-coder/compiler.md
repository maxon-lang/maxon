# Writing the compiler, `runtime/` and diagnostics

Read this with the skill before changing anything under `maxon-bin/` or `runtime/`. Building, the seed and
self-compiles are `maxon-bin/AGENTS.md`'s.

## Tier edits — `runtime/`

Before changing `runtime/` or the compiler's handling of it, read `runtime/README.md`: the tier's families,
the reserved-name doors, reach and dead-function elimination, the `.data` layout, the `__Raw` rows and the
pinned geometry. Every tier edit is bound by these:

- No managed value in tier source (E3153).
- A restated geometry figure owes its pin, and moving one takes a staged build.
- Tier source does not discard a declared callee's result.
- A new unprefixed tier entry owes the declaration and call doors `maxon_force_segfault` has.
- A new family the compiler reaches by minting a call owes a line in
  `StdlibSource.runtimeTierFileIsCompilerCalled`.

## Memory safety

⛔ **THE REFERENCE COUNT IS THE SAFETY MECHANISM. NEVER ADD A REFUSAL WHOSE ONLY JOB IS TO PROTECT AN
UNCOUNTED REFERENCE: COUNT IT.** An analysis mistake must cost speed, never a use-after-free, so a count is
left out only where it is proved unneeded. Counts are added where needed, never added and then cancelled: the
parser emits hold markers and `HoldResolution.maxon` decides each one once the whole-program summaries exist.
Trading soundness for speed is never an option; a self-compile slowdown is cured by a better proof. The
models are reference counting with elision (Swift, Perceus, Lobster) and no shared mutable data between
threads (Pony, Erlang), not Rust's borrow checker.

**A leak can be masking a double free:** a spurious reference pins a record at rc=1 so its destructor never
runs. Fixing such a leak turns exit 101 into an access violation. Follow the fault; the fix was right.

## Ranged aliases

**A ranged alias is entered by its own type or by a cast, and checked at run time where it is not proven.**

- A literal outside the range is E3005.
- A value of another named integer type (another alias, `ExitCode`) is E3005, and the message names the
  crossing: `as T`.
- A value typed `T`, including arithmetic on `T` values (`a - b` over a narrower `T`), a plain unnamed `int`,
  and a value cast with `as T` all land in `T` with a range check. The check is elided where the compiler
  proves the value fits, and panics ("Range check failed: value outside typealias 'T'") where it does not.
- **`+`, `-` and `*` over a full-width unsigned alias are checked AT THE OPERATION, not at a door.** That is
  `int(0 to u64.max)` (`Count`, `ElementIndex`, `BytePos`; `aliasChecksItsArithmetic`, which excludes
  `bits(64)`). Every 64-bit pattern is a value of the alias, so a wrapped result is indistinguishable from a
  real one at any later door. The check is elided where `Parser.unsignedArithmeticIsProvablyInRange` proves
  the result fits, and a constant expression that wraps is E3005. Unary `-` over one is E3218 and `shl` is
  unchecked; arithmetic meant to wrap is written over `bits(64)` (`MachineWord`).

"Proves" is a normative spec rule over the parser's analysis (`valueIsProvablyInRange`), never an optimizer
outcome, so whether a check is emitted is a property of the program. A stronger prover removes checks; it
never changes which programs compile.

## Error codes — one registry, and it is the enum itself

**`maxon-bin/Compiler/ErrorCodeRegistry.maxon` IS the registry.** It carries the number, the canonical name
and the doc text for every code, and it is authored — edit it directly. **To add a diagnostic:** take the
next free number in the right band, add a case, write the code that emits it.

- ⛔ **Not its doc comment.** That block is a documentation surface — `Mcp/McpErrorCodes.maxon` re-parses
  the file at runtime to serve `lookup_error_code`, and the site's Error Codes page is generated from it —
  so the `documenter` skill writes it, with every other comment, just before the commit.
  `website/scripts/sync-docs.mjs` fails closed on a case with no doc comment, which is how it finds them.
- **A duplicate NAME does not compile**, and `ErrorCode.Foo` does not compile unless the enum declares it —
  both are structural, so neither needs a checker.
- **A duplicate NUMBER compiles**: two cases may carry one `"E3099"`. `website/scripts/sync-docs.mjs`
  refuses one while it reads the registry, naming both lines; `website.yml` runs it on every change to the
  registry, and `/land`'s gates run it with `--check`. `spec-test` does not check this.
- **The stage is derived from the leading digit** (1xxx lexer … 9xxx internal) and is never written down,
  so it cannot disagree.
- ⛔ **NEVER REFERENCE A CODE BY ITS NUMBER OUTSIDE THE REGISTRY.** Use the member
  (`ErrorCode.semanticUnneededCast`, plus `.rawValue` for the `"E3010"` spelling). A literal `"E3010"` in a
  source file is a second copy of the number space: renumber the code and every gate stays green while the
  code that matched it silently matches nothing.
- **A diagnostic that lists what the compiler supplies is DERIVED.** Join the sentence from the constants
  the dispatch arms match on where you can (`specs/builtin-member-rosters.md`), and never state what is
  absent: an "are not built" clause rots the moment someone builds the thing, across every pin that quotes
  it. Before widening a roster, grep the message's stable half across `specs/` and `ir-specs/`; re-pin those
  cases with the change, and grep again after a rebase, which can bring fresh pins of the old wording.

## Targets

⛔ **`maxon-bin/targets.tsv` IS THE ONE PLACE A TARGET IS LISTED.** `scripts/targets/generate.sh` writes
`Compiler/TargetsGenerated.maxon` from it (`supportedTargets()`, `debuggerTargets()`) — run it after editing
the table, never edit its output; `--check` is CI's drift gate. The scripts, workflows and installers read
the same table. What a target MEANS — its `CpuArch`/`Os` case, backend, runtime and debug interface — stays
compiler code, so a row naming a cpu or os the compiler lacks fails the build. `docs/RELEASING.md` owns
adding a target.

⚠ **A CHANGE THAT GROWS A FUNCTION'S LIVE-VALUE COUNT IS A CROSS-TARGET CHANGE** even when it touches no
target-specific code: arm64 has 31 general registers and x64 about 14, so a self-compile on one host cannot
see the other's E5001 — CI's lanes are the gate. An E5001 in straight-line, loop-depth-0 code is an
allocator defect (it should have spilled), not a reason to shrink the function.

⛔ **NO NEW MECHANISM MAY CAP ARGUMENTS BELOW `MaxAbiArgs` (64).** A small cap refuses ordinary programs and
regresses them silently, so arguments past the register files ride the stack. The 64 ceiling, its `bits(64)`
position masks and its refusals stay; do not propose lifting them. A ceiling hit is a clear refusal, never
silent mishandling.

## Front end

⚠ **Every file's tokens are read twice:** the declaration sweep (`foldDeclaredSignaturesInto`) and the real
parse. A feature that changes WHICH DECLARATIONS EXIST belongs in the token tier (`queryActiveTokens`), where
both consumers read one array, because the two disagreeing is a wrong field offset with no diagnostic.

⚠ **A `mutable` parameter marker is a whole-program switch.** `ProgramSignatures.programTakesAParamByRef` is
false until some function marks a parameter `mutable`, and true turns on the name-keyed, over-approximating
cell-local prescan in every body of the program, stdlib included. The prescan reads its callee out of tokens
before the body parses, so it cannot resolve a method's receiver type. Read and write of a by-ref parameter
are specified in `specs/pass-by-reference.md`.

A `__Builtins` intrinsic that can be refused signals it through per-handle STATE answered as a corpus enum
(`__SubprocessStreamState`; ordinals read through `requireCorpusDeclaredEnum`), never through an empty
record, a null or a last-error global: a managed-returning builtin cannot carry "refused" in-band.

## Re-checking a program

⛔ **ANYTHING THAT RE-CHECKS A PROGRAM (THE LSP, A WATCH LOOP, A RE-RUN) STAYS ON THE QUERY SPINE** and costs
in proportion to what changed; a fresh `Project` per check is the wrong foundation even though it is `maxon
build`'s pipeline. Read `docs/internals/query-spine.md`, name which stages are memoized, and measure warm
against cold. A stage that cannot be incremental is a finding to raise.

⚠ **Run `maxon verify-recheck <file|dir>` after a change to the pipeline, the query spine or per-`Project`
state.** The suite compiles each program once, so it stays green over a memo the pipeline mutated or a table
no fold resets. A memo's content hash cannot see a mutation, and cloning per serve is too costly: drop the
memo as the pipeline takes it, as `invalidateAllModuleCache` does.

## Measuring

After any change to a pass, the IR, or a data structure the compiler indexes by, run `run_scale_test`; to ask
whether the compiler reproduces itself byte for byte, `scripts/fixpoint.sh`. Load the `compiler-workflow`
skill before acting on either.
