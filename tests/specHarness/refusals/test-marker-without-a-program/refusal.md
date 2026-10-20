---
feature: harness-refusal-test-marker-without-a-program
---
# A test marker with no ```maxon block

`SpecParser.scanTestFromMarker` refuses it. With no program there is nothing to compile, so the case
would run nowhere while its marker reads as coverage.

## Tests

<!-- test: first-case -->

```exitcode
0
```
