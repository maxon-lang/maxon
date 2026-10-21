---
feature: harness-refusal-repeated-unsupported-target
---
# An unsupported-targets marker naming one target twice

`SpecParser.parseUnsupportedTargetKeys` refuses it, naming the repeated key. A key written twice excludes
nothing the first did not, so the marker reads as ruling out one more lane than it does — here it lists
five keys, the size of the roster, while `wasm32-wasi` still runs the case.

## Tests

<!-- test: repeated-unsupported-target -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, x64-linux, arm64-linux -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
