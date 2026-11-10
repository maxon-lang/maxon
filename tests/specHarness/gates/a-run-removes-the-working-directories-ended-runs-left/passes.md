---
feature: harness-gate-a-run-sweeps-ended-runs
---
# The passes gate

A run case that passes and touches nothing.

## Tests

<!-- test: passes.does-nothing -->

```maxon
function main() returns ExitCode
	print("done\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
done
```
