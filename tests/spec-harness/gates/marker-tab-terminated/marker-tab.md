---
feature: harness-gate-marker-tab-terminated
---
# A marker closed with a TAB

The reference's marker grammar is `<!--\s*test:\s*(\S+)\s*-->`, so a TAB before the `-->` closes a
marker exactly as a space does. A compiler that matched only the literal `" -->"` would read this line as
an unterminated marker, and that refusal is a PANIC that takes the WHOLE SUITE down on a file the
reference reads without complaint. `marker-tab-terminated.test.maxon`, beside this directory, runs
`spec-test` over a copy of it and requires the case below to be SELECTED, COMPILED and RUN.

**Its expected stdout is DELIBERATELY WRONG, and it must stay wrong** — the rule `network-gate.md`
states, for its reason: the test reads the case's FAIL verdict line as the evidence that it RAN.

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
