---
feature: harness-refusal-directive-after-a-fence
---
# A directive below a case's fences

`SpecParser.scanTestFromMarker` refuses it. Written after the first case's blocks and above the second
case's marker, it reads as the second case's while it applies to the first.

## Tests

<!-- test: first-case -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```

<!-- procs: 1 -->

<!-- test: second-case -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
