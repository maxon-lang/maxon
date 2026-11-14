---
feature: global-file-private-same-name
status: stable
keywords: let, global, unmarked, visibility, directory, data-section, aliasing
category: language
---
# File-Private Globals With The Same Name

## Documentation

A top-level binding that is not `export`ed is visible only inside its declaring directory and its
subdirectories. Two files in different directories may therefore each declare a binding of the same
bare name with different values, and each file's reads and writes resolve to its OWN directory's storage.
Two files of ONE directory may not: a top-level name is declared once per directory, whatever its
visibility (`top-level-name-scope.md`), and the second declaration is E3006.

This mirrors the rule `top-level-let.md` states for an unmarked `let`
(`unmarked-same-name-cross-file`), and it is the same rule: the directory is the scope a name is
declared in and resolved from.

The distinction a binding with storage adds is that its storage is REAL. A scalar `let` inlines as a
literal at every use, so there is no slot for two constants to collide in; a managed binding has a `.data`
slot, and that slot's identity must be per-DIRECTORY, exactly as the name resolution is. A compiler whose
resolver is directory-scoped while its data label is global-by-name will alias the two declarations onto
one slot and silently return a wrong number.

## Tests

<!-- test: file-private-same-name-in-two-directories-through-a-generic-instance -->
Two files in two DIRECTORIES each declare an unmarked `limit`, `tag` and `calls` (a `SharedValue` cell). A generic type
declared in `a.maxon` has a method that reads them, and `app/main.maxon` instantiates it and calls that method
from beside its own three declarations of the same names. The method reads `a.maxon`'s declarations
whichever file instantiated it or called it, and bumps `a.maxon`'s `calls`, never `main.maxon`'s.

The answers are distinct on purpose: `weighA` is `5 + 2 + 3`, and the method's second call is
`5 + 3 + 3`, so reading `main.maxon`'s `limit` or `tag` or bumping its `calls` changes the total.
```maxon
// --- file: lib/a.maxon
let limit = 5
let tag = b"AAA"
let calls = CallCell.create(1 as Count)

export typealias CallCell = SharedValue with Count

export type Box uses T
	export var value as T

	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'

	export function weigh() returns Count
		let next = calls.current() + 1
		calls.publish(next)
		return limit + next + tag.count()
	end 'weigh'
end 'Box'

export typealias CountBox = Box with Count

export function weighA() returns Count
	let box = CountBox.create(1)
	return box.weigh()
end 'weighA'

// --- file: app/main.maxon
let limit = 70
let tag = b"BBBBBB"
let calls = CallCell.create(500 as Count)

function weighMain() returns Count
	let box = CountBox.create(2)
	let next = calls.current() + 1
	calls.publish(next)
	return box.weigh() + limit + next + tag.count()
end 'weighMain'

function main() returns ExitCode
	print("{weighA()} {weighMain()}\n")
	return 0
end 'main'
```
```stdout
10 588
```

<!-- test: exported-globals-of-one-name-in-two-directories-are-read-qualified -->
An `export`ed top-level binding is declared in its directory's scope, so two directories that each `export let
LIMIT` declare two bindings, and a file that sees both reads each qualified: `alpha.LIMIT` is 42 and `beta.LIMIT`
is 34.
```maxon
// --- file: alpha/a.maxon
export let LIMIT = 42

// --- file: beta/b.maxon
export let LIMIT = 34

// --- file: app/main.maxon
function main() returns ExitCode
	return alpha.LIMIT + beta.LIMIT
end 'main'
```
```exitcode
76
```

<!-- test: error.a-bare-read-of-a-name-two-directories-export-is-ambiguous -->
`app/main.maxon` sees both directories' `LIMIT`, so a bare read of it is ambiguous and is E3095, as a bare call to a
function two directories export is.
```maxon
// --- file: alpha/a.maxon
export let LIMIT = 42

// --- file: beta/b.maxon
export let LIMIT = 34

// --- file: app/main.maxon
function main() returns ExitCode
	return LIMIT
end 'main'
```
```maxoncstderr
error E3095: app/<fragment>:10:9: Ambiguous bare-name read of 'LIMIT': more than one visible declaration matches it. Qualify it as one of: alpha.LIMIT, beta.LIMIT
```

<!-- test: a-root-binding-owns-the-bare-name-beside-a-subdirectorys -->
The root declares an unmarked `let LIMIT` and `sub/` exports another. A file in a third directory reads the bare name as
the root's (40) and the subdirectory's as `sub.LIMIT` (2), as a bare call reaches the root's function before any
directory's.
```maxon
// --- file: main.maxon
let LIMIT = 40

function main() returns ExitCode
	return fromOther()
end 'main'

// --- file: sub/s.maxon
export let LIMIT = 2

// --- file: other/o.maxon
export function fromOther() returns ExitCode
	return LIMIT + sub.LIMIT
end 'fromOther'
```
```exitcode
42
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
error E3006: lib/<fragment>:6:12: duplicate definition of 'LIMIT' — already declared as `let LIMIT`
```

<!-- test: global-file-private-same-name.error.two-files-of-one-directory-each-declare-a-private-helper-of-one-name -->
Each file declares an unmarked `pick` and calls its own, and the two share one directory, so the name is
declared twice there: the later in path order is refused at its name.
```maxon
// --- file: a.maxon
function pick(s String) returns String
	return s
end 'pick'

export function useA(s String) returns String
	return pick(s)
end 'useA'

// --- file: main.maxon
typealias Cnt = int(0 to 100)

function pick(n Cnt) returns Cnt
	return n
end 'pick'

function main() returns ExitCode
	print("{useA("hi")}\n")
	print("{pick(1)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:14:10: duplicate definition of 'pick' — already declared as `function pick`
```

<!-- test: global-file-private-same-name.two-directories-each-declare-a-private-helper-of-one-name -->
The same two helpers in two DIRECTORIES are two names, and each file means its own. Typing every call to the
name from the last declaration the sweep read (`app/main.maxon`'s, returning a `Cnt`) would refuse `lib/a.maxon`
— the file that is CORRECT — with an `E3005` on its `return`. The program prints `hi` then `1`, exit 0.
```maxon
// --- file: lib/a.maxon
function pick(s String) returns String
	return s
end 'pick'

export function useA(s String) returns String
	return pick(s)
end 'useA'

// --- file: app/main.maxon
typealias Cnt = int(0 to 100)

function pick(n Cnt) returns Cnt
	return n
end 'pick'

function main() returns ExitCode
	print("{useA("hi")}\n")
	print("{pick(1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hi
1
```

<!-- test: global-file-private-same-name.two-private-helpers-of-one-name-in-two-directories-returning-different-managed-types -->
The `stripTrailingCR` shape: two files of two directories, one unmarked helper name, parameter types that differ
AND return types that differ — and both of them MANAGED, so the divergence is not one any later retype could
repair. A result typed from the wrong member here is a leak in one direction and a drop of a record the
callee never wrote in the other, which is why the pair is pinned by `exitcode` and not by `stdout` alone.
```maxon
// --- file: lib/a.maxon
export typealias Integer = int(i64.min to i64.max)

function stripTrailingCR(bytes ByteArray) returns ByteArray
	return bytes
end 'stripTrailingCR'

export function byteCount(b ByteArray) returns Integer
	return stripTrailingCR(b).count()
end 'byteCount'

// --- file: app/main.maxon
function stripTrailingCR(line String) returns String
	return line
end 'stripTrailingCR'

function main() returns ExitCode
	print("{byteCount(b"ab\r")}\n")
	print("{stripTrailingCR("xy")}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
xy
```

<!-- test: a-field-and-a-struct-literal-label-named-like-a-contested-binding-keep-their-spelling -->
`alpha/` and `beta/` each export a `let counter`, and a type in `alpha/` has a field of the same name. A name
that is not a reference to the top-level binding is never respelled: the field's declaration, the `counter:` label
of the struct literal and `self.counter` stay `counter`, so `app/main.maxon`'s `cell.counter` reaches it.
```maxon
// --- file: alpha/cell.maxon
export let counter = 1

export type Cell
	export var counter as ExitCode

	export static function make() returns Cell
		return Cell{counter: 5}
	end 'make'

	export function bump()
		self.counter = self.counter + 1
	end 'bump'
end 'Cell'

// --- file: beta/count.maxon
export let counter = 10

// --- file: app/main.maxon
function main() returns ExitCode
	let cell = Cell.make()
	cell.bump()

	return cell.counter + alpha.counter + beta.counter
end 'main'
```
```exitcode
17
```

<!-- test: a-method-named-like-a-contested-binding-keeps-its-spelling -->
A method named `counter` in `alpha/`, beside `alpha/`'s own `let counter` and `beta/`'s: the method's declaration is
not a reference to the binding, so `meter.counter()` in another directory still finds it.
```maxon
// --- file: alpha/meter.maxon
export let counter = 1

export type Meter
	export let base as ExitCode

	export static function make() returns Meter
		return Meter{base: 20}
	end 'make'

	export function counter() returns ExitCode
		return self.base + 1
	end 'counter'
end 'Meter'

// --- file: beta/count.maxon
export let counter = 10

// --- file: app/main.maxon
function main() returns ExitCode
	let meter = Meter.make()

	return meter.counter() + alpha.counter + beta.counter + 10
end 'main'
```
```exitcode
42
```

<!-- test: a-parameter-and-an-argument-label-named-like-a-contested-binding-keep-their-spelling -->
A parameter named `counter` in `alpha/`, beside two directories' `let counter`: the parameter is not the binding,
so the call's `counter:` label in `app/main.maxon` still names it.
```maxon
// --- file: alpha/seed.maxon
export let counter = 1

export function seed(counter ExitCode = 3, step ExitCode = 1) returns ExitCode
	return counter * 10 + step
end 'seed'

// --- file: beta/count.maxon
export let counter = 10

// --- file: app/main.maxon
function main() returns ExitCode
	return seed(counter: 4, step: 2) + alpha.counter + beta.counter - 11
end 'main'
```
```exitcode
42
```

<!-- test: error.a-local-and-a-parameter-named-like-a-contested-binding-are-reported-by-their-own-name -->
A local and a parameter that shadow `alpha/`'s `let counter` are block-scope names: the diagnostics name them as
written, and the read of the binding before the local that shadows it stays a read of the binding.
```maxon
// --- file: alpha/shadow.maxon
export let counter = 1

export function pad(counter ExitCode) returns ExitCode
	return 1
end 'pad'

export function note() returns ExitCode
	let before = counter
	let counter = before + 10

	return before
end 'note'

// --- file: beta/count.maxon
export let counter = 10

// --- file: app/main.maxon
function main() returns ExitCode
	return pad(1) + note() + alpha.counter + beta.counter
end 'main'
```
```maxoncstderr
error E3012: alpha/<fragment>:5:21: unused variable: 'counter'
error E3012: alpha/<fragment>:11:6: unused variable: 'counter'
```

<!-- test: a-bare-binding-read-means-the-readers-own-directorys-function -->
`alpha/` declares a free function `limit` and `beta/` exports a `let limit`. A bare `limit` in `alpha/` means the
reader's own directory's declaration, so it is a function value there; `beta`'s binding is read as `beta.limit`.
```maxon
// --- file: alpha/limit.maxon
export function limit() returns ExitCode
	return 7
end 'limit'

export function viaValue() returns ExitCode
	let f = limit

	return f()
end 'viaValue'

// --- file: beta/limit.maxon
export let limit = 100

// --- file: app/main.maxon
function main() returns ExitCode
	return viaValue() + alpha.limit() + beta.limit - 72
end 'main'
```
```exitcode
42
```

<!-- test: a-bare-binding-read-means-the-readers-own-directorys-function-beside-two-lets -->
The same pair with a second directory exporting a `let limit`: two bindings of the name are visible to `alpha/`,
and still its own function is the bare name's meaning, because the reader's own directory decides first.
```maxon
// --- file: alpha/limit.maxon
export function limit() returns ExitCode
	return 7
end 'limit'

export function viaValue() returns ExitCode
	let f = limit

	return f()
end 'viaValue'

// --- file: beta/limit.maxon
export let limit = 100

// --- file: gamma/limit.maxon
export let limit = 3

// --- file: app/main.maxon
function main() returns ExitCode
	return viaValue() + alpha.limit() + beta.limit + gamma.limit - 75
end 'main'
```
```exitcode
42
```

<!-- test: a-programs-export-let-shadows-a-library-binding-of-its-name -->
`stdlib/helpers/sort/driftQuicksort.maxon` declares `public let SmallSortThreshold = 32` and its sorts read it. A
program's own `export let SmallSortThreshold` is a different scope: the program reads its own value, and the
library's sort of forty elements still reads the library's, so the array comes out ordered.
```maxon
// --- file: lib/sizes.maxon
export let SmallSortThreshold = 7

// --- file: app/main.maxon
typealias Numbers = Array with ExitCode

function main() returns ExitCode
	let xs = Numbers.create(0)

	for i in 0 upto 40 'fill'
		xs.push((40 - i) as ExitCode)
	end 'fill'

	xs.sort()

	let first = try xs.get(0) otherwise 0
	let last = try xs.get(39) otherwise 0

	return first + SmallSortThreshold + last - 7
end 'main'
```
```exitcode
41
```

<!-- test: every-block-scope-form-named-like-a-contested-binding-keeps-its-own-meaning -->
A loop variable, a closure parameter, a union payload with its match-arm binding, a tuple destructuring and an enum
case are each spelled `counter` in `alpha/`, beside `alpha/`'s own `let counter` and `beta/`'s. Each is a block-scope name that
shadows the binding exactly as far as its scope runs: the reads inside it mean the local, and the reads before or after
it mean `alpha`'s binding.
```maxon
// --- file: alpha/forms.maxon
export let counter = 1

union Pick
	one(counter ExitCode)
	empty
end 'Pick'

enum Mode
	counter
	other
end 'Mode'

export function viaLoop() returns ExitCode
	var total = 0 as ExitCode

	for counter in 0 upto 3 'loop'
		total = total + (counter as ExitCode)
	end 'loop'

	return total + counter
end 'viaLoop'

export function viaClosure() returns ExitCode
	let add = function(counter ExitCode) gives counter + 10

	return add(5) + counter
end 'viaClosure'

export function viaMatch() returns ExitCode
	let pick = Pick.one(7)

	let doubled = match pick 'arm'
		one(counter) gives counter * 2
		empty gives 0
	end 'arm'

	return doubled + counter
end 'viaMatch'

export function viaTuple() returns ExitCode
	let before = counter
	let (counter, other) = (5, 6)

	return before + counter + other
end 'viaTuple'

export function viaCase() returns ExitCode
	let mode = Mode.counter

	let picked = match mode 'arm'
		counter gives 20
		other gives 0
	end 'arm'

	return picked + counter
end 'viaCase'

// --- file: beta/count.maxon
export let counter = 10

// --- file: app/main.maxon
function main() returns ExitCode
	return viaLoop() + viaClosure() + viaMatch() + viaTuple() + viaCase() + alpha.counter + beta.counter
end 'main'
```
```exitcode
79
```

<!-- test: two-library-directories-each-read-their-own-unmarked-scalar-let-of-one-name -->
The library root and `helpers/string/` each declare an unmarked `Limit`, and each directory's public function
reads its own: the nearer declaration is the one a bare read in `helpers/string/` means.
```maxon
// --- stdlib-overlay: Builtins.maxon
let Limit = 7

public function overlayLimitA() returns Count
	return Limit
end 'overlayLimitA'
// --- stdlib-overlay: helpers/string/hash.maxon
let Limit = 900

public function overlayLimitB() returns Count
	return Limit
end 'overlayLimitB'
// --- file: main.maxon
function main() returns ExitCode
	print("{overlayLimitA()} {overlayLimitB()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 900
```

<!-- test: two-sibling-library-directories-each-read-their-own-unmarked-scalar-let-of-one-name -->
`helpers/string/` and `helpers/hashtable/` each declare an unmarked `Limit`; neither directory contains the other,
so no file can name both, and each public function reads its own directory's.
```maxon
// --- stdlib-overlay: helpers/string/hash.maxon
let Limit = 7

public function overlayLimitA() returns Count
	return Limit
end 'overlayLimitA'
// --- stdlib-overlay: helpers/hashtable/slotScan.maxon
let Limit = 900

public function overlayLimitB() returns Count
	return Limit
end 'overlayLimitB'
// --- file: main.maxon
function main() returns ExitCode
	print("{overlayLimitA()} {overlayLimitB()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 900
```

<!-- test: two-library-directories-each-read-their-own-unmarked-managed-let-of-one-name -->
The same shape with a managed binding, which owns a `.data` slot per directory rather than inlining.
```maxon
// --- stdlib-overlay: Builtins.maxon
let Tagline = "alpha"

public function overlayTaglineA() returns String
	return Tagline
end 'overlayTaglineA'
// --- stdlib-overlay: helpers/string/hash.maxon
let Tagline = "be"

public function overlayTaglineB() returns String
	return Tagline
end 'overlayTaglineB'
// --- file: main.maxon
function main() returns ExitCode
	print("{overlayTaglineA()} {overlayTaglineB()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
alpha be
```

<!-- test: two-sibling-library-directories-each-read-their-own-unmarked-managed-let-of-one-name -->
The managed binding in two sibling library directories.
```maxon
// --- stdlib-overlay: helpers/string/hash.maxon
let Tagline = "alpha"

public function overlayTaglineA() returns String
	return Tagline
end 'overlayTaglineA'
// --- stdlib-overlay: helpers/hashtable/slotScan.maxon
let Tagline = "be"

public function overlayTaglineB() returns String
	return Tagline
end 'overlayTaglineB'
// --- file: main.maxon
function main() returns ExitCode
	print("{overlayTaglineA()} {overlayTaglineB()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
alpha be
```
