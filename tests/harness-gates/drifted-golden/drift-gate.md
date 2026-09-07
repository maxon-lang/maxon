---
feature: harness-gate-drifted-goldens
---
# The drifted-golden gate

`HarnessSelfTest.requireDriftedGoldensAreReported` spawns this harness at this directory THREE times —
once to let the three cases below MINT their goldens, once with TWO of those goldens deliberately
CORRUPTED, and once with both put back — and requires the corrupted run to NAME both, to show what
differs, to count them, to leave their cases PASSING and the exit code 0, and then to say nothing at all.
What is under test is `SpecTestRunner.reportGoldenDrift`, not the programs below.

**The goldens are MINTED by the gate rather than committed**, which is what buys the negative control: the
gate never has to know what this compiler emits, and the same fixture can be run again with the references
correct. They land under this corpus's gitignored `fragments/`, so an interrupted run leaves nothing
tracked behind.

⚠ **THE GATE CLEARS `fragments/` BEFORE IT MINTS, and that is not tidiness.** Minting only writes a golden
that is ABSENT, so a stray or half-corrupted `.test` under here survives every later run — and the gate's
own count of what it minted would then read as a defect in the fixture, aborting the whole suite before a
single spec test ran, on a gitignored path `git status` never mentions. Clearing first is what makes that
state unreachable and what makes the count below a real positive control.

**All three cases PASS, and that is what this fixture is for** — the gates beside it need their cases to
FAIL so that nothing is minted there, and a case that fails never reaches the golden step at all
(`SpecTestRunner.recordFragmentAndCleanup`). Drift is only observable on a case that got as far as
comparing a committed reference.

**Neither of the pinned expectations may be stdout.** A case that pins only its exit code is asserting that
stdout is EMPTY, so a `print` here fails it — and a failing case mints no golden.

**TWO cases are corrupted, not one, and the second is what makes the COUNT mean anything.** With a single
drift, a summary that says `1` whenever anything drifted at all and a trailer capped at one entry both look
exactly like a working report.

**The third case exists to be LEFT ALONE.** Its golden is minted, compared and correct in every run, so a
reporter that printed every result once ANY result drifted would name it. The corrupted run requires it
never to appear.

## Tests

<!-- test: drift-gate.pinned -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```

<!-- test: drift-gate.also-pinned -->

```maxon
function main() returns ExitCode
	return 5
end 'main'
```

```exitcode
5
```

<!-- test: drift-gate.intact -->

```maxon
function main() returns ExitCode
	return 7
end 'main'
```

```exitcode
7
```
