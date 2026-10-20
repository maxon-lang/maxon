---
feature: harness-gate-trace-remint-past-a-prose-block
---
# Re-minted capture blocks land where their cases are

The first case asks for an allocation trace and carries no block yet, so `--update-required` inserts
one. Its text block holds a heading line and a quoted test marker, and the new block must land after
the case's last block rather than at either of those lines. The second case carries a stale block,
which the same run replaces, so one file takes an insertion above a replacement.

## Tests

<!-- test: remint.lands-after-the-case -->
<!-- MmTrace -->

```maxon
function main() returns ExitCode
	let greeting = "hello {1 + 1}"
	return 0 if greeting.count() > 0 else 1
end 'main'
```

```text
## Not a heading
<!-- test: not-a-marker -->
```

```exitcode
0
```

<!-- test: remint.replaces-a-stale-block -->

```maxon
function main() returns ExitCode
	let farewell = "bye {2 + 2}"
	return 0 if farewell.count() > 0 else 1
end 'main'
```

```exitcode
0
```

```mm-trace
stale line the re-mint replaces
```

<!-- test: remint.the-last-case -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
