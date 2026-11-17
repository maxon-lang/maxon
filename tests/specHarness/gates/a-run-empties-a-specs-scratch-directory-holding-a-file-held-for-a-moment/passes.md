---
feature: harness-gate-a-run-waits-for-a-file-held-in-a-specs-scratch-directory
---
# The passes gate

A run case that passes and touches nothing. Its test plants, in this spec's scratch directory, a file nothing
holds and a file another process holds without delete sharing until shortly after the run's emptying of that
directory has removed the first: the run must wait for the held file and leave the directory empty, reporting
nothing about it.

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
