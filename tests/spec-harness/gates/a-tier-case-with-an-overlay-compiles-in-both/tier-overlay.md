---
feature: harness-gate-a-tier-case-with-an-overlay-compiles-in-both
---
# The library-tier-with-an-overlay gate

A case may carry both `<!-- tier: stdlib -->` and a `// --- stdlib-overlay:` section. The overlay is applied to the case's private copy of the library, and the case's own files are staged inside THAT copy, so the program is library-tier source and sees the overlaid module. The second case is the control: the same program without the marker is the author's code and is refused.

## Tests

<!-- test: tier-overlay.sees-the-overlay -->
<!-- tier: stdlib -->

```maxon
// --- stdlib-overlay: Clock.maxon
public typealias OverlayTick = int(0 to 9)

public function overlayProbe() returns OverlayTick
	return 7
end 'overlayProbe'
// --- file: main.maxon
function main() returns ExitCode
	if __Builtins.currentProcessId() == 0 'noProcess'
		return 99
	end 'noProcess'

	return overlayProbe() as ExitCode
end 'main'
```

```exitcode
7
```

<!-- test: tier-overlay.control -->

```maxon
// --- stdlib-overlay: Clock.maxon
public typealias OverlayTick = int(0 to 9)

public function overlayProbe() returns OverlayTick
	return 7
end 'overlayProbe'
// --- file: main.maxon
function main() returns ExitCode
	if __Builtins.currentProcessId() == 0 'noProcess'
		return 99
	end 'noProcess'

	return overlayProbe() as ExitCode
end 'main'
```

```maxoncstderr
error E3185: <fragment>:10:16: call to '__Builtins.currentProcessId' from outside the library tiers: '__Builtins' names a raw runtime intrinsic, callable only from a source file under stdlib/ or runtime/ — use 'Process.id()'
```
