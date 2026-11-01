---
feature: harness-refusal-poison-marker-on-an-unpinned-case
---
# A freed-box fill marker on a case that pins no Target IR

`SpecParser.CaseBuilder.build` refuses it. A case that pins no Target IR is already built with the freed-box
fill, so the marker would change nothing about it and would read as a claim the case does not make. The
marker means something only on a case that pins Target IR, which is otherwise built the way a user's
`maxon build` is.

## Tests

<!-- test: poison-marker-on-an-unpinned-case -->
<!-- PoisonFreed -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
