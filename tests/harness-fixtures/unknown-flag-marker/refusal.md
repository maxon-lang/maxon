---
feature: harness-refusal-unknown-flag-marker
---
# A valueless flag marker spelt one character wrong

`SpecParser.scanTestFromMarker` refuses it. A flag marker carries no payload, so its comment's whole body
must be the flag: matched by prefix, `<!-- DebugInfos -->` would be claimed as `<!-- DebugInfo -->` and
the case compiled with debug info it never asked for. Matched whole, the misspelling is a comment line
no marker reads, and the same refusal that catches a misspelt valued marker catches it.

<!-- expect-refusal: carries a comment line no marker reads -->

## Tests

<!-- test: unknown-flag-marker -->
<!-- DebugInfos -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
