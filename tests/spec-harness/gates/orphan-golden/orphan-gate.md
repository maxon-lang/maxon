---
feature: harness-gate-orphaned-goldens
---
# The orphaned-golden gate

`orphan-golden.test.maxon`, beside this directory, runs `spec-test` over a copy of it TWICE — once with
`.test` files planted under `fragments/x64-windows/`, three that no case here can ever read and one for
the first case below, and once with them removed — and requires the suite to NAME each unreadable file,
NOT to name the first case's, and then to say nothing at all. What is under test is
`GoldenCensus.reportUncomparedGoldens`, not the programs below.

**The goldens are planted by the test rather than committed**, which is what buys the negative control: a
census that named every golden in the tree would satisfy the first run and fail the second, and one that
named nothing fails the first. They land in the copy under the host temp directory, never in this
directory.

**The first case's expected stdout is DELIBERATELY WRONG, and it must stay wrong.** A failing case mints
nothing, so the only goldens in the copy are the ones the test plants, while it still gives the run a
selected test, which this fixture needs — a run that selects nothing returns before the golden reports
are reached.

**The second case exists to be UNSELECTABLE on the planted lane.** Its
`<!-- unsupported-targets: -->` marker names `x64-windows` — the test's `OrphanGateLane`, fixed on every
host because the census covers every lane — so the golden the test plants for it is one the spec
genuinely declares and that lane can genuinely never compare: the census's second arm, which a case that
simply did not exist could not reach. It names the ONE lane that must not select it, so a backend landing later cannot
quietly take the case out of the gate.

## Tests

<!-- test: orphan-gate.selected -->

```maxon
function main() returns ExitCode
	print("selected\n")
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong — see this fixture's header
```

<!-- test: orphan-gate.restricted-elsewhere -->
<!-- unsupported-targets: x64-windows -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
