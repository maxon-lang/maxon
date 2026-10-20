---
feature: harness-fixture-unterminated-test-marker
---
# A test marker that is never closed

The case below is written the way a spec author COMMENTS ONE OUT: the `-->` that should end the
marker sits after the case's fences instead, so the whole case reads as one HTML comment.
`markerValueOf` reads an unclosed marker to the end of its line, so without the refusal this fixture
proves, the marker would still parse and the harness would compile and run a case the file says is
switched off.

⭐ A test marker REQUIRES its `-->` end token: the marker pattern is
`<!--\s*test:\s*(\S+)\s*-->`, and `\s*` cannot
span the `NOTE:` line below, so the marker at the foot of this file never closes.

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
