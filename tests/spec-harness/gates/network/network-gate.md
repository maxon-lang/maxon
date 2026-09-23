---
feature: harness-gate-live-network
---
# The live-network gate

The one case below carries the per-case network marker. A DEFAULT `spec-test` must not select it and
must NAME it in the exclusion census; a `--network` run must select it and run it.
`network.test.maxon`, beside this directory, runs `spec-test` over a copy of it twice and asserts
exactly those three things — the marker is what is under test, not the program, and nothing here
reaches a network.

**Its expected stdout is DELIBERATELY WRONG, and it must stay wrong.** What the test needs from the
`--network` run is evidence that the case RAN, and it reads that off the case's FAIL verdict line: a
failing case's verdict says as clearly as a passing one that it was selected, compiled and executed, and
a failing case mints no golden (`SpecTestRunner.checkTestFragment` mints only when the verdict allows it).

## Tests

<!-- test: network-gate.live -->
<!-- network: live -->

```maxon
function main() returns ExitCode
	print("selected\n")
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong — see this fixture's header
```
