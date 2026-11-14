# Known issues

Open defects nobody has landed a fix for yet. `/land` §10 is the procedure: how an entry is recorded,
claimed and drained. An entry is deleted by the commit that fixes it.

`ci-red` entries stay above every `normal` one. Every edit to this file is committed at once, alone:
`git commit -F <msg> -- KNOWN_ISSUES.md`.

```
## <one-line symptom>
- priority: ci-red | normal
- suspects: <sha> <subject>, …          (ci-red only)
- status: open | claimed <session> <ISO date>
- where: file:line
- seen: how it was found, and by which task
- repro: a program that reproduces it, or for ci-red the run URL, lane and failing case
```

## Nothing checks that spec-test prints the same stdout at one worker and at several
- priority: normal
- status: open
- where: maxon-bin/AGENTS.md:252
- seen: the spec-suite speed plan's gate survey; the contract is stated there and in the worker pool's
  header, and every tests/specHarness gate passes only `--workers=1`.
- repro: a tests/specHarness gate that runs one fixture at `--workers=1` and at `--workers=4` and compares stdout; none exists

## A lex sweep that fails drops any phase span its project left open
- priority: normal
- status: open
- where: maxon-bin/Compiler/Compiler.maxon:424
- seen: the spec-suite speed plan's L2 review; `sweepProject` creates its project without the job's span
  array and `beginSweep` sets no trace unit, so `JobRun.failed()` has nothing to close. Read off the
  code, not run. Visible only in a debug-stream trace of the compiler.
- repro: a debug-stream trace of a sweep whose project throws after opening a span shows a begin with no end

## `--workers=1` is spelled as a private constant in five tests/specHarness gates
- priority: normal
- status: open
- where: tests/specHarness/gates/spec-workers-start-from-the-library-cache.maxtest:3
- seen: the spec-suite speed plan's L2 land; the fifth copy came with its gate.
- repro: `grep -n '"--workers=1"' tests/specHarness/gates/*.maxtest` lists five declarations

## The spec worker pool's header says the serial suite takes about three seconds
- priority: normal
- status: open
- where: maxon-bin/Testing/SpecWorkerPool.maxon:8
- seen: the spec-suite speed plan's survey; a lane's suite takes tens of minutes.
- repro: read the comment against any CI suite job's duration

## ProgramSignatures.reassembled leaves two fields at their defaults on a worker's copy
- priority: normal
- status: open
- where: maxon-bin/Compiler/SignatureIndex.maxon:8975
- seen: the spec-suite speed plan's L3c review, by reading; the constructor omits
  corpusByRefConventionDigestAnswer and liveSpawnedTypeNames. No wrong answer was found: parse keys are
  computed on the main thread and the second field is refilled when reach is full.
- repro: a front-end pool worker that reads either field off its reassembled index gets 0 or an empty set

## The corpus-only function-valued field names classify a type by name, not by declaring file
- priority: normal
- status: open
- where: maxon-bin/Compiler/SignatureIndexSettle.maxon:1696
- seen: the spec-suite speed plan's L3c review, by reading; computedFunctionValuedFieldNames(corpusOnly: true)
  asks stdlibDeclaredTypeNames, so a scriptlib type landing in that set would have its field names treated
  as the corpus's own. Not run.
- repro: a scriptlib package declaring a type with a function-valued field, compiled by a single-file program

## Two signature hash marks hold the same value
- priority: normal
- status: open
- where: maxon-bin/Compiler/SignatureIndex.maxon:9396
- seen: the spec-suite speed plan's L3c documenter pass, by reading; DeclShapeGenericInstances and
  LivenessMutableFunctionValues are both 0x3C6EF372FE94F82B, and the comment above the marks says a new
  one is checked against all of them by hand. They feed different chains and no wrong answer was found.
- repro: `grep -n 0x3C6EF372FE94F82B maxon-bin/Compiler/SignatureIndex.maxon` lists two declarations

## A regions-modify spec case pins column 31 where the compiler reports column 25
- priority: normal
- status: open
- where: specs/regions-modify.md (case `one-statement-open-refuses-a-read-through-a-region-beside-a-one-call-open-of-another`)
- seen: the directory-scoped visibility land; it fails the same way with a compiler built from c8773e3, so
  either the diagnostic's anchor moved or the pin is stale.
- repro: `./maxon-bin/.maxon/maxon spec-test --filter=regions-modify/one-statement-open-refuses-a-read-through-a-region-beside-a-one-call-open-of-another`

## Identifiers on `#if` directive lines count as spellings in the unused-declaration audits
- priority: normal
- status: open
- where: maxon-bin/Compiler/QueryDatabase.maxon (`rawSpellingCountsIn` counts them; `conditionalSpellingsIn` excludes them)
- seen: the directory-scoped visibility land's code review; read off the code, not run. An unused
  declaration named like a directive word escapes E3092/E3219.
- repro: one file with `#if debug` … `#end`, another in the same directory declaring an unused unmarked
  `let debug = 1`; expect E3219, observe none
