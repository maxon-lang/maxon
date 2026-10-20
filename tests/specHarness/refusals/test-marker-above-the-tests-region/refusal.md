---
feature: harness-refusal-test-marker-above-the-tests-region
---
# A test marker above the tests region

`SpecParser.extractTests` refuses it. Only the cases below the tests heading run, so the first case
here would run nowhere, with nothing reported.

<!-- test: above-the-region -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```

## Tests

<!-- test: below-the-region -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
