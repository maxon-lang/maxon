---
feature: harness-gate-a-run-waits-for-a-file-held-in-an-ended-runs-working-directory
---
# The passes gate

A run case that passes and touches nothing. Its test plants, beside this spec, the working directory of a
run that has ended, holding a file nothing holds and a file another process holds without delete sharing
until shortly after the run's sweep has removed the first: the run must wait for the held file and remove
the directory, reporting nothing about it.

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
