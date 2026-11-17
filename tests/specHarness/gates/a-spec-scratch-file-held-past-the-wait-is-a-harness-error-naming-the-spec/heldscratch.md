---
feature: harness-gate-a-spec-scratch-file-held-past-the-wait-is-a-harness-error-naming-the-spec
---
# The heldscratch gate

A run case that passes and touches nothing. Its test plants, in this spec's scratch directory, a file nothing
holds and a file another process holds without delete sharing for the whole run, so the emptying of that
directory when the spec's cases finish removes the first and gives up on the second once its wait is spent.
The run must report the held file as one harness error naming it and this spec, exit non-zero, and say
nothing about it as a worker's stderr.

## Tests

<!-- test: heldscratch.passes -->

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
