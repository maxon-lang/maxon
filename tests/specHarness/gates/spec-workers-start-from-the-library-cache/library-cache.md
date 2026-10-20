---
feature: harness-gate-spec-workers-start-from-the-library-cache
---
# The spec-workers-start-from-the-library-cache gate

One run case, run twice under one private run-cache root: the second run's workers load the library the first run's workers wrote.

## Tests

<!-- test: library-cache.trivial -->

```maxon
function main() returns ExitCode
	print("trivial\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
trivial
```
