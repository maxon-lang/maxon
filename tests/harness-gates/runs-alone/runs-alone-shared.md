---
feature: harness-gate-runs-alone-shared
---
# The runs-alone gate: the shared job

`HarnessSelfTest.requireAloneCasesRunWithNothingBeside` spawns this harness at this directory. This spec's
one case is an ordinary SHARED job: it marks itself busy, holds that mark for a second, then replaces it
with a done mark. The `runs-alone-gate` spec beside it reads the two marks, and what it prints says whether
it ran after this job or beside it.

**Its expected stdout is DELIBERATELY WRONG, and it must stay wrong**, so it mints no golden into this
fixture (see this corpus's `.gitignore`).

## Tests

<!-- test: runs-alone-shared.holds-the-pool -->

```maxon
function main() returns ExitCode
	let busy = FilePath from "harness-gate-runs-alone.busy"
	let done = FilePath from "harness-gate-runs-alone.done"
	try File.delete(done) otherwise ignore
	try File.writeText(busy, content: "") otherwise panic("the runs-alone gate could not write its busy mark")
	sleep(1000)
	try File.delete(busy) otherwise panic("the runs-alone gate could not remove its busy mark")
	try File.writeText(done, content: "") otherwise panic("the runs-alone gate could not write its done mark")
	print("shared\n")
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong — see this fixture's header
```
