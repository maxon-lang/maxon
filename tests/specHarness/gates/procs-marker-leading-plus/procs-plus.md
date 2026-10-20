---
feature: harness-gate-procs-marker-leading-plus
---
# A procs marker with a leading plus

The scheduler reads `MAXON_MAX_PROCS=+4` as 4, as Go's `strconv.ParseInt` reads `+4`, so the harness
accepts the marker `<!-- procs: +4 -->` and runs the case on four processors. The case prints the count
the scheduler resolved; `procs-marker-leading-plus.maxtest`, beside this directory, runs `spec-test` over
a copy of it and requires the case to RUN and to print `procs=4`.

**Its expected stdout is DELIBERATELY WRONG, and it must stay wrong**: the test reads what it printed out
of the failure's `actual:`, which a passing case does not print.

## Tests

<!-- test: procs-plus.a-leading-plus -->
<!-- procs: +4 -->

```maxon
function main() returns ExitCode
	print("procs={Scheduler.processorCount()}\n")
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong — see this fixture's header
```
