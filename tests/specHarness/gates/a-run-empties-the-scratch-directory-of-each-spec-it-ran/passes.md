---
feature: harness-gate-a-run-empties-each-specs-scratch-directory
---
# The passes gate

A run case that passes and touches nothing. Its test plants a file in this spec's scratch directory before
the run: the run must leave that directory in place and empty.

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
