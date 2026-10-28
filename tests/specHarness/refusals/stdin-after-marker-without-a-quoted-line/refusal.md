---
feature: harness-refusal-stdin-after-marker-without-a-quoted-line
---
# A stdin after marker whose line is not one quoted run

`SpecParser.parseStdinValue` refuses it. `after go` names a line the way a shell would, and a reader that took
the unquoted word for the line would feed on a line the author never delimited: whatever the quotes were meant
to keep, such as the trailing spaces of the line, would be guessed at.

## Tests

<!-- test: stdin-after-marker-without-a-quoted-line -->
<!-- stdin: after go -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
