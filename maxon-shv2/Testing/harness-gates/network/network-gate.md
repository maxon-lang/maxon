---
feature: harness-gate-live-network
---
# The live-network gate

The one case below carries the per-case network marker. A DEFAULT `spec-test` must not select it and
must NAME it in the exclusion census; a `--network` run must select it and run it.
`HarnessSelfTest.requireNetworkGateHolds` spawns this harness at this directory twice and asserts
exactly those three things — the marker is what is under test, not the program, and nothing here
reaches a network.

**Its expected stdout is DELIBERATELY WRONG, and it must stay wrong.** What the gate needs from the
`--network` run is evidence that the case RAN, which is the verdict line the report prints for it — and
a case that PASSED would mint a committed `.test` golden into this fixture the first time the suite
ever ran (`SpecTestRunner.checkTestFragment` mints when the verdict allows it), leaving a file behind
that nothing here wants to own or review. A failing case mints nothing, and its verdict line says just
as clearly that the case was selected, compiled and executed.

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
