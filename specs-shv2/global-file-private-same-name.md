---
feature: global-file-private-same-name
status: stable
keywords: var, global, file-private, visibility, data-section, aliasing
category: language
---
# File-Private Globals With The Same Name

## Documentation

A top-level `var` that is not `export`ed is **file-private**: it is visible only inside its
declaring file. Two files may therefore each declare a `var` of the same bare name with
different values, and each file's reads and writes resolve to its OWN storage.

This mirrors the rule `top-level-let.md` states for a file-private `let`
(`file-private-same-name-cross-file`), and it is the same rule: the name resolver is
file-scoped. Rejecting the second declaration is **not** an available reading of that rule —
the two declarations are both legal.

The distinction a `var` adds is that its storage is REAL. A `let` inlines as a literal at every
use, so there is no slot for two constants to collide in; a `var` has a `.data` slot, and that
slot's identity must be per-FILE, exactly as the name resolution is. A compiler whose resolver
is file-scoped while its data label is global-by-name will alias the two declarations onto one
slot and silently return a wrong number.

## Tests

<!-- test: file-private-same-name-cross-file-var -->
<!-- targets: wasm32-wasi -->
Two files each declare a file-private `var counter` with a different value and mutate it by a
different amount. Each file's `counter` is its own slot: `bumpA` sees 7 and `bumpB` sees 100.

The three answers are deliberately distinct, so that aliasing cannot pass by coincidence:
correct is `8 + 110 = 118`; aliasing onto featA's 7 gives `8 + 18 = 26`; aliasing onto featB's
100 gives `101 + 111 = 212`. MEASURED against the C# bootstrap, which has exactly this defect:
it returns **212** — `bumpA()` reads featB's slot and answers 101.

`Num` is declared in BOTH files rather than once, because a `typealias` does not resolve across
files in the reference compiler (`E2003: Unknown type: Num`). The repetition is load-bearing, not
an oversight.
```maxon
// --- file: featA/a.maxon
typealias Num = int(i64.min to i64.max)

var counter = 7

export function bumpA() returns Num
	counter = counter + 1
	return counter
end 'bumpA'

// --- file: featB/b.maxon
typealias Num = int(i64.min to i64.max)

var counter = 100

export function bumpB() returns Num
	counter = counter + 10
	return counter
end 'bumpB'

// --- file: app/main.maxon
function main() returns ExitCode
	return bumpA() + bumpB()
end 'main'
```
```exitcode
118
```
