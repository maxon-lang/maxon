---
feature: harness-gate-a-staged-tier-diagnostic-is-respelled-through-a-link
---
# The staged-tier respelling gate

The gate runs this spec with its directory named through a link. The case's expectation is planted wrong in its
message and right in its position: the case fails, and the `actual:` line the harness prints must spell the
diagnostic's file as `<fragment>`, never as the path of the staged source tier the compiler named.

## Tests

<!-- test: tier-respelling.overlay-collision -->

```maxon
// --- stdlib-overlay: Builtins.maxon
export typealias WrittenByteCount = int(0 to u64.max)

export function __write_stdout(value String) returns WrittenByteCount
	return value.byteLength()
end '__write_stdout'
// --- file: main.maxon
function main() returns ExitCode
	print("hi")
	return 0
end 'main'
```
```maxoncstderr
error E4015: <fragment>:5:17: planted message that the compiler never prints
```
