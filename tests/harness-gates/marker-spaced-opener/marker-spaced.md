---
feature: harness-gate-marker-spaced-opener
---
# A marker opened with TWO spaces

`<!--\s*test:` lets any run of whitespace sit between the comment opener and the keyword, so the
reference reads a two-space marker as an ordinary one. shv2 matched a one-space literal and
saw NO MARKER AT ALL: the line fell through the scanner as prose, the case was silently DROPPED, and
its fences were swallowed by whatever case sat above it. One `.md`, two meanings — and this is the
half that reads as coverage while testing nothing.
`HarnessSelfTest.requireReferenceMarkerShapesParse` spawns this harness here and requires the case
below to be SELECTED, COMPILED and RUN.

**Its expected stdout is DELIBERATELY WRONG, and it must stay wrong** — see `marker-tab.md`, which
carries the same rule for the same reason.

⚠ **The marker below has TWO spaces after the comment opener. Do not let a formatter collapse them**
— the second space IS the subject.

⚠ **AND THE PROSE ABOVE DELIBERATELY DOES NOT SPELL ONE OUT.** It used to, and a spelled-out example
IS a marker: `extractTests` never reaches this far up the file, but `traceSliceOf` and
`traceMarkerLineOf` scan every line, so the sentence yielded a phantom case called `NAME`. A fixture
documenting a grammar must not be written in it.

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
