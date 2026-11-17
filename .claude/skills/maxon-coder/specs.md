# Writing and re-pinning spec cases

Read this with the skill before writing or re-pinning a case under `specs/` or `ir-specs/`. The format —
markers, blocks, batching, tiers, lane exclusion, `TargetIr` pins — is `docs/SPECS.md`'s; these are the
traps in using it.

- **`specs/` pins no emitted code; `ir-specs/` is the emitted-code suite.** A change that moves emitted code
  re-mints the pins it moves with `--update-required --filter=<spec>/` on each lane, reads the diff, and
  commits it with the change.
- `--update-required` re-mints the trace-capture blocks (` ```mm-trace `, ` ```log-trace `) and the
  ` ```TargetIr:<lane> ` pins but **not** `maxoncstderr` blocks: an error-code renumber moves those by hand.
- A spec fence the harness does not read (` ```RequiredIR ` among them) is refused, naming the case.
- **A batched FAIL that passes alone is a batching gap**, fixed by a new `SoloReason` or a
  `<!-- process: own -->` marker. `SpecBatch.placementOf` is the one list of reasons a case runs alone.
- **`<!-- tier: stdlib -->` is for a case whose SUBJECT is a library-tier surface**; a case that merely uses
  one moves to the safe stdlib API. The compiler's own sources under `maxon-bin/` are user tier too.
- ⚠ **A diagnostic added to an early pass can pre-empt the subject of an older compile-error case**: the
  pipeline stops at the first pass that reports, so a case pinning a later pass's code starts failing with
  the new code. That reads as message churn, and re-minting it makes the case silently test nothing. Change
  the case's program so it no longer trips the new check and the original diagnostic fires again. A
  construct in a case's scaffolding that a lane refuses with E3104 does the same.
- ⚠ **A new refusal redirects programs onto the spellings that are left**, and those may be untested or
  absent. For each spelling it removes, ask what the program must now say and whether anything has ever said
  it: the suite wrote the closed spelling everywhere, so green measures the road being closed, never the one
  being opened. The refusal binds everything that generates Maxon too: `Testing/ScaleCorpus.maxon` emitting
  a now-illegal program takes `run_scale_test` down before it reports a number. Never sed a binding away
  file-wide when sweeping cases: a pinned diagnostic or a later read may name it.
- **Before changing a diagnostic, grep its current message in `specs/` and `ir-specs/` inside `maxoncstderr`
  and `Maxoncstderr:<target>` blocks only**: prose quotes old messages constantly, and an unfiltered grep
  over-reports. Read the pinned PROGRAMS, not just the sentences: what they share is the discriminator that
  lets the change narrow so every pinned shape keeps its wording. A pin inside a test PROGRAM shows up only
  in the run.
- ⚠ **Line endings.** A scripted edit in text mode on Windows (Python `open(p,'w')`) rewrites every line
  ending in the file, and `git diff` cannot show it because `.gitattributes` normalises to LF. A case whose
  `stdout` block gained a `\r` fails with `expected:` and `actual:` printing identically. Edit spec files
  binary-safe and check `file <path>` for `CRLF`; when expected and actual look identical, suspect line
  endings first. The same flip reaches compiler sources: the compiler reads the disk, and a tool writing
  platform line endings leaves a file `w/crlf` until the next checkout. A `b"…"` literal spanning a line
  break (`GtTraceNewlineText`) then holds `\r\n`, and the emitted runtime prints it. After an agent edits
  sources, `git ls-files --eol` over the changed files must show no `w/crlf`. Read a mismatch whose two sides
  look identical with `od -c`.
