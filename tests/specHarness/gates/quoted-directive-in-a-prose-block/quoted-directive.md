---
feature: harness-gate-quoted-directive-in-a-prose-block
---
# A directive quoted inside a prose block outside any case

The text block below sits outside any case and quotes a directive on a line of its own. The scan skips
the block whole, so the quoted line is neither refused nor read, and the case after it is selected,
compiled and run. Its expected stdout is deliberately wrong, so the FAIL verdict is what proves it ran.

## Tests

```text
<!-- procs: 2 -->
```

<!-- test: quoted-directive.runs-after-a-prose-block -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong — see this fixture's header
```
