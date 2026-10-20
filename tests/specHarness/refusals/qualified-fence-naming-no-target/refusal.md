---
feature: harness-refusal-qualified-fence-naming-no-target
---
# A result fence qualified by no supported target

`SpecParser.recordTargetQualifiedText` refuses it. A misspelt target matches no lane, so the block would
be read on none and dropped without a word.

## Tests

<!-- test: first-case -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```

```Stdout:x64-windwos
something
```
