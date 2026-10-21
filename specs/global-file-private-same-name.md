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
Two files each declare a file-private `var counter` with a different value and mutate it by a
different amount. Each file's `counter` is its own slot: `bumpA` sees 7 and `bumpB` sees 100.

The three answers are deliberately distinct, so that aliasing cannot pass by coincidence:
correct is `8 + 110 = 118`; aliasing onto featA's 7 gives `8 + 18 = 26`; aliasing onto featB's
100 gives `101 + 111 = 212`, which is the aliasing this case exists to catch: `bumpA()` reading
featB's slot and answering 101.
```maxon
// --- file: featA/a.maxon
export typealias Num = int(i64.min to i64.max)

var counter = 7 as Num

export function bumpA() returns Num
	counter = counter + 1
	return counter
end 'bumpA'

// --- file: featB/b.maxon
var counter = 100 as Num

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

<!-- test: file-private-same-name-same-directory-through-a-generic-instance -->
Two files of ONE directory each declare a file-private `limit`, `tag` and `var calls`. A generic type
declared in `a.maxon` has a method that reads them, and `main.maxon` instantiates it and calls that method
from beside its own three declarations of the same names. The method reads `a.maxon`'s declarations
whichever file instantiated it or called it, and bumps `a.maxon`'s `calls`, never `main.maxon`'s.

The answers are distinct on purpose: `weighA` is `5 + 2 + 3`, and the method's second call is
`5 + 3 + 3`, so reading `main.maxon`'s `limit` or `tag` or bumping its `calls` changes the total.
```maxon
// --- file: a.maxon
let limit = 5
let tag = b"AAA"
var calls = 1 as Count

export type Box uses T
	export var value as T

	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'

	export function weigh() returns Count
		calls = calls + 1
		return limit + calls + tag.count()
	end 'weigh'
end 'Box'

export typealias CountBox = Box with Count

export function weighA() returns Count
	let box = CountBox.create(1)
	return box.weigh()
end 'weighA'

// --- file: main.maxon
let limit = 70
let tag = b"BBBBBB"
var calls = 500 as Count

function weighMain() returns Count
	let box = CountBox.create(2)
	calls = calls + 1
	return box.weigh() + limit + calls + tag.count()
end 'weighMain'

function main() returns ExitCode
	print("{weighA()} {weighMain()}\n")
	return 0
end 'main'
```
```stdout
10 588
```


<!-- test: error.exported-global-in-two-directories -->
The INVERSE of the rule above, and the reason that rule needs the word "file-private". An
`export`ed top-level binding is ONE name for the whole program — there is no directory-qualified
spelling of a global — so two files that each `export let LIMIT`
are declaring one name twice, and the second is refused wherever it sits.

Two DIRECTORIES are used deliberately: namespacing scopes typealiases and functions, and it
would be an easy mistake to extend it to bindings, where neither reference does. A reader would
then get whichever declaration was folded first, silently.

Without the refusal the program below would compile clean and exit **42** — `alpha`'s
declaration winning and `beta`'s 34 unreachable — where the same program is a hard duplicate in
the self-hosted reference (`reportDuplicateConstant`).
```maxon
// --- file: alpha/a.maxon
export let LIMIT = 42

// --- file: beta/b.maxon
export let LIMIT = 34

// --- file: app/main.maxon
function main() returns ExitCode
	return LIMIT as ExitCode
end 'main'
```
```maxoncstderr
error E3006: beta/<fragment>:6:12: duplicate definition of 'LIMIT'
```


<!-- test: error.exported-global-in-two-files-one-directory -->
The same refusal with both declarations in ONE directory, pinning that it is not a directory
rule wearing a duplicate's clothes: a global's name is flat and project-wide, so the two files'
relative position never enters into it.
```maxon
// --- file: lib/a.maxon
export let LIMIT = 42

// --- file: lib/b.maxon
export let LIMIT = 34

// --- file: app/main.maxon
function main() returns ExitCode
	return LIMIT as ExitCode
end 'main'
```
```maxoncstderr
error E3006: lib/<fragment>:6:12: duplicate definition of 'LIMIT'
```
