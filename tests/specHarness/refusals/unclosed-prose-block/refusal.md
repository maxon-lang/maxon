---
feature: harness-refusal-unclosed-prose-block
---
# A prose block that is never closed

`SpecParser.readFence` refuses it. Unclosed, the block would pair with the next case's program fence
and swallow that case's marker, so the second case would run nowhere with nothing reported.

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

```
this prose block is never closed

<!-- test: second-case -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
