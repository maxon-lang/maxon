---
feature: harness-gate-marker-spaced-opener
---
# A marker opened with TWO spaces

`<!--\s*test:` lets any run of whitespace sit between the comment opener and the keyword, so the
reference reads `<!--  test: NAME -->` as an ordinary marker. shv2 matched the literal `<!-- test:` and
saw NO MARKER AT ALL: the line fell through the scanner as prose, the case was silently DROPPED, and
its fences were swallowed by whatever case sat above it. One `.md`, two meanings — and this is the
half that reads as coverage while testing nothing.
`HarnessSelfTest.requireReferenceMarkerShapesParse` spawns this harness here and requires the case
below to be SELECTED, COMPILED and RUN.

**Its expected stdout is DELIBERATELY WRONG, and it must stay wrong** — see `marker-tab.md`, which
carries the same rule for the same reason.

⚠ **The marker below has TWO spaces after `<!--`. Do not let a formatter collapse them** — the second
space IS the subject.

## Tests

<!--  test: marker-spaced.opened-with-two-spaces -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong — see this fixture's header
```
