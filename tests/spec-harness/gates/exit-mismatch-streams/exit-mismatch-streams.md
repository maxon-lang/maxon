---
feature: harness-gate-exit-mismatch-streams
---
# The exit-mismatch streams gate

A case whose program exits with a code its `exitcode` block does not pin fails with the child's own stdout
and stderr appended, so a crash and a wrong exit code can be told apart from the failure text alone.
`exit-mismatch-streams.maxtest`, beside this directory, runs `spec-test` over a copy of this file and
requires `exit-mismatch-streams.short` and `exit-mismatch-streams.long` to FAIL with that text and
`exit-mismatch-streams.agrees` to PASS without it.

The `short` case prints a marker on each stream and exits 3 against a pinned 1. The `long` case prints
fifty lines on stderr, so the tail bound shows. The `agrees` case is the control: the same program with the
right exit code, which must not carry the appended streams.

## Tests

<!-- test: exit-mismatch-streams.short -->

```maxon
function main() returns ExitCode
	print("stdout-marker\n")
	printError("stderr-marker\n")
	return 3
end 'main'
```

```exitcode
1
```

<!-- test: exit-mismatch-streams.long -->

```maxon
function main() returns ExitCode
	for i in 10 to 59 'eachLine'
		printError("tail-line-{i}\n")
	end 'eachLine'

	return 3
end 'main'
```

```exitcode
1
```

<!-- test: exit-mismatch-streams.agrees -->

```maxon
function main() returns ExitCode
	print("stdout-marker\n")
	return 3
end 'main'
```

```exitcode
3
```

```stdout
stdout-marker
```
