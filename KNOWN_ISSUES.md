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

## tests/specHarness's corpus pairing check fails: three fed-run gates have no fixture directory
- priority: normal
- status: open
- where: tests/specHarness/corpus.maxtest:7
- seen: the spec-suite speed plan's L2 land, by its case author; the three gates
  (`a-fed-run-that-ends-at-its-childs-exit-sees-the-exit-while-a-process-it-started-writes-without-pause`,
  `a-fed-run-whose-child-sleeps-without-reading-a-large-feed-ends-at-the-deadline`,
  `a-fed-run-whose-child-never-reads-its-feed-is-judged-by-its-own-outcome`) share a sibling's fixture.
  It fails in CI's corpora job on every lane that reaches the corpus; no run URL was read.
- repro: `maxon test tests/specHarness "--filter=every fixture has its test file"` exits 1 with 3 checks failed

## The gate a-fed-run-whose-child-never-reads-its-feed-is-judged-by-its-own-outcome fails on x64-linux
- priority: normal
- status: open
- where: tests/specHarness/gates/a-fed-run-whose-child-never-reads-its-feed-is-judged-by-its-own-outcome.maxtest:1
- seen: the spec-suite speed plan's L2 land; its probe, run through `execute`, is killed by signal 13.
  Reproduced on a clean clone of 02bced2 with the compiler built before L2, so L2 did not cause it.
- repro: `maxon test tests/specHarness "--filter=a-fed-run-whose-child-never-reads-its-feed"` exits 1

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
