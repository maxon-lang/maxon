---
feature: harness-gate-spec-workers-respell-the-same-from-a-warm-library-cache
---
# The spec-workers-respell-the-same-from-a-warm-library-cache gate

One run case, run twice under one private run-cache root: a staged stdlib file spells a name the compiler mints, and a program declares its own `Clock`, so a diagnostic in that file goes through shadow respelling.

## Tests

<!-- test: shadow-respelling.library-file-spells-its-own-mint -->

```maxon
// --- stdlib-overlay: Clock.maxon
export typealias OverlayTick = int(0 to 9)

export function overlayProbe() returns OverlayTick
	return Clock.nope()
end 'overlayProbe'

#if os(Windows) and os(Linux)
let overlayMention = __Clock
#endif
// --- file: main.maxon
typealias Tick = int(0 to 1000)

type Clock
	export var ticks as Tick

	static function create(ticks Tick) returns Self
		return Self{ticks: ticks}
	end 'create'
end 'Clock'

function main() returns ExitCode
	return overlayProbe() as ExitCode
end 'main'
```

```maxoncstderr
error E3004: <fragment>:6:15: call to undefined function '__Clock.nope': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```
