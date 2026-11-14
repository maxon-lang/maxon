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
