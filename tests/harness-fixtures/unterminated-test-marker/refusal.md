---
feature: harness-fixture-unterminated-test-marker
---
# A test marker that is never closed

The case below is written the way a spec author COMMENTS ONE OUT: the `-->` that should end the
marker sits after the case's fences instead, so the whole case reads as one HTML comment. Until the
refusal this fixture proves, `markerValue` fell back to end-of-line, the marker parsed anyway, and
the harness compiled and ran a case the file says is switched off. That is not a formatting nit —
`specs/unary-operators.md` carried exactly this shape and `negate-float` ran on every suite.

⭐ A test marker REQUIRES its `-->` end token: the marker pattern is
`<!--\s*test:\s*(\S+)\s*-->`, and `\s*` cannot
span the `NOTE:` line below, so the marker at the foot of this file never closes.

<!-- expect-refusal: this test marker is never closed -->

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
