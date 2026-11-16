---
feature: harness-gate-one-worker-and-several-print-the-same-stdout-charlie
---
# The one-worker-and-several gate: charlie

A run case that waits beside a compile-error case.

## Tests

<!-- test: charlie.waits -->

```maxon
function main() returns ExitCode
	sleep(0)
	print("charlie waited\n")
	return 0
end 'main'
```

```stdout
charlie waited
```

<!-- test: charlie.refused -->

```maxon
function main() returns ExitCode
	return missing() as ExitCode
end 'main'
```

```maxoncstderr
error E3004: <fragment>:3:9: call to undefined function 'missing'
```
