---
feature: harness-fixture-unterminated-test-marker
---
# A test marker that is never closed

The case below is written the way a spec author COMMENTS ONE OUT: the `-->` that should end the
marker sits after the case's fences instead, so the whole case reads as one HTML comment. Until the
refusal this fixture proves, `markerValue` fell back to end-of-line, the marker parsed anyway, and
the harness compiled and ran a case the file says is switched off. That is not a formatting nit —
`specs-shv2/unary-operators.md` carried exactly this shape and `negate-float` ran on every suite.

⭐ The BOOTSTRAP is the oracle here and it declines this marker: `SpecParser.cs`'s
`TestMarkerRegex` is `<!--\s*test:\s*(\S+)\s*-->`, which requires the end token, and `\s*` cannot
span the `NOTE:` line below. Measured 2026-08-23: the bootstrap ran three cases of
`specs/unary-operators.md` and `negate-float` was not one of them. So this fixture pins a
DIVERGENCE FROM THE REFERENCE, not merely a shv2 preference.

<!-- expect-refusal: this line opens a test marker and never closes it -->

## Tests

<!-- test: never-closed 
NOTE: this note is what keeps the reference regex from spanning to the `-->` below
```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
-->
