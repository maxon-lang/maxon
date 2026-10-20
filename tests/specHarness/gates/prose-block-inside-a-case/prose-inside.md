---
feature: harness-gate-prose-block-inside-a-case
---
# A prose block inside a case

The text block between the program and the expected output holds a heading line and a quoted test
marker. The scan skips the block whole, so neither ends the case and its expected output is still
read. That expected stdout is deliberately wrong, so the FAIL verdict is what proves the case ran.

## Tests

<!-- test: prose-inside.runs-past-its-prose-block -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```text
## Not a heading
<!-- test: not-a-marker -->
```

```stdout
this expectation is deliberately wrong — see this fixture's header
```
