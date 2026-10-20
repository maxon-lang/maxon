---
feature: harness-gate-runs-alone
---
# The runs-alone gate: the alone job

The one case below carries `<!-- runs: alone -->`, so the pool must dispatch it only once the shared job in
`runs-alone-shared` has finished. It prints `ran-after-every-other-job` when that job's done mark is there
and its busy mark is not, and `ran-beside-another-job` otherwise; it consumes the done mark either way, so a
stale one cannot answer for a later run. `runs-alone.test.maxon`, beside this directory, reads which it
printed.

**Its expected stdout is DELIBERATELY WRONG, and it must stay wrong**: the test reads what it printed out
of the failure's `actual:`, which a passing case does not print.

## Tests

<!-- test: runs-alone-gate.alone -->
<!-- runs: alone -->

```maxon
function main() returns ExitCode
	let busy = FilePath from "harness-gate-runs-alone.busy"
	let done = FilePath from "harness-gate-runs-alone.done"
	let ranAfter = File.exists(done) and not File.exists(busy)

	if File.exists(done) 'consumeTheMark'
		try File.delete(done) otherwise panic("the runs-alone gate could not consume the done mark")
	end 'consumeTheMark'

	print("ran-after-every-other-job\n" if ranAfter else "ran-beside-another-job\n")
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong — see this fixture's header
```
