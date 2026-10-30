---
feature: harness-gate-a-case-left-one-lane-by-its-marker-fails-where-the-target-refuses-it
---
# The a-case-left-one-lane-by-its-marker-fails-where-the-target-refuses-it gate

Three run cases call a construct wasm32-wasi refuses with E3104. The one whose marker leaves wasm32-wasi
as its only lane exists for that lane, so the refusal fails it. The two beside it are the control: one
with a marker that leaves two lanes and one with no marker, both still skipped.

## Tests

<!-- test: single-lane-refusal.only-lane -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->

```maxon
function main() returns ExitCode
	return __Builtins.threadCpuTicks() as ExitCode
end 'main'
```

```exitcode
0
```

<!-- test: single-lane-refusal.two-lanes -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos -->

```maxon
function main() returns ExitCode
	return __Builtins.threadCpuTicks() as ExitCode
end 'main'
```

```exitcode
0
```

<!-- test: single-lane-refusal.unmarked -->
<!-- tier: stdlib -->

```maxon
function main() returns ExitCode
	return __Builtins.threadCpuTicks() as ExitCode
end 'main'
```

```exitcode
0
```
