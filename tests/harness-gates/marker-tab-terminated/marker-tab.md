---
feature: harness-gate-marker-tab-terminated
---
# A marker closed with a TAB

The reference's marker grammar is `<!--\s*test:\s*(\S+)\s*-->`, so a TAB before the `-->` closes a
marker exactly as a space does. shv2 matched the literal `" -->"` and did not — and once this rung's
unterminated-marker refusal landed, that mismatch became a PANIC that took the WHOLE SUITE down on a
file the reference reads without complaint. `HarnessSelfTest.requireReferenceMarkerShapesParse` spawns
this harness here and requires the case below to be SELECTED, COMPILED and RUN.

**Its expected stdout is DELIBERATELY WRONG, and it must stay wrong** — the rule `network-gate.md`
states, for its reason: a case that PASSED would mint a committed golden into this fixture. What the
gate needs is evidence that the case RAN, and the FAIL verdict line carries that just as well.

⚠ **The marker below ends with a literal TAB before its `-->`. Do not let an editor turn it into
spaces** — the tab IS the subject, and with it gone this fixture passes for a reason unrelated to what
it pins.

## Tests

<!-- test: marker-tab.closed-with-a-tab	-->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong — see this fixture's header
```
