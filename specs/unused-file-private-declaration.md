---
feature: unused-file-private-declaration
status: stable
keywords: [file-private, unused, diagnostics, errors, dead code]
category: diagnostics
---

# Unused File-Private Declarations

## Documentation

A top-level `function`, `let`, `type`, `enum`, `union` or `interface` with no visibility modifier is visible only in its
own file. When nothing live in that file names it, the declaration is dead and the compiler reports it
(**E3219**). Delete it, or use it.

"Live" is decided by reachability inside the file. A file-private declaration is live when it is named
by code that is not itself a file-private declaration (an `export`, `module` or `public` declaration, a
test, `main`, a method), or by a live file-private declaration. A group of file-private declarations that
only name each other, including a function that only names itself, is dead as a whole and is reported
together.

A name is judged by where it is spelled, in the tokens of the file. A spelling in a branch of `#if` the
target excludes counts, so a declaration used only under one `#if os(...)` branch is live on every target.

`typealias` is not part of this check: an alias nothing names is E3062. Members of a type, a `static let`
included, are not part of it either.

## Tests

<!-- test: error.unused-function -->
```maxon
function unusedHelper() returns ExitCode
	return 3
end 'unusedHelper'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3219: specs/unused-file-private-declaration/error.unused-function.maxon:2:10: unused function: 'unusedHelper' is file-private and nothing live in this file names it; delete it
```

<!-- test: error.unused-constant -->
```maxon
let UnusedLimit = 7

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3219: specs/unused-file-private-declaration/error.unused-constant.maxon:2:5: unused constant: 'UnusedLimit' is file-private and nothing live in this file names it; delete it
```

<!-- test: error.unused-type -->
```maxon
typealias Coord = int(0 to 100)

type UnusedPoint
	export let x as Coord
end 'UnusedPoint'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3219: specs/unused-file-private-declaration/error.unused-type.maxon:4:6: unused type: 'UnusedPoint' is file-private and nothing live in this file names it; delete it
```

<!-- test: error.unused-enum -->
```maxon
enum UnusedShade
	light
	dark
end 'UnusedShade'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3219: specs/unused-file-private-declaration/error.unused-enum.maxon:2:6: unused enum: 'UnusedShade' is file-private and nothing live in this file names it; delete it
```

<!-- test: error.unused-union -->
```maxon
union UnusedOutcome
	success
	failure
end 'UnusedOutcome'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3219: specs/unused-file-private-declaration/error.unused-union.maxon:2:7: unused union: 'UnusedOutcome' is file-private and nothing live in this file names it; delete it
```

<!-- test: error.a-function-that-only-names-itself-is-unused -->
```maxon
function spin() returns ExitCode
	return spin()
end 'spin'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3219: specs/unused-file-private-declaration/error.a-function-that-only-names-itself-is-unused.maxon:2:10: unused function: 'spin' is file-private and nothing live in this file names it; delete it
```

<!-- test: error.declarations-that-only-name-each-other-are-all-unused -->
```maxon
let UnusedLimit = 7

function first() returns ExitCode
	return second() + UnusedLimit
end 'first'

function second() returns ExitCode
	return first()
end 'second'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3219: specs/unused-file-private-declaration/error.declarations-that-only-name-each-other-are-all-unused.maxon:2:5: unused constant: 'UnusedLimit' is file-private and nothing live in this file names it; delete it
error E3219: specs/unused-file-private-declaration/error.declarations-that-only-name-each-other-are-all-unused.maxon:4:10: unused function: 'first' is file-private and nothing live in this file names it; delete it
error E3219: specs/unused-file-private-declaration/error.declarations-that-only-name-each-other-are-all-unused.maxon:8:10: unused function: 'second' is file-private and nothing live in this file names it; delete it
```

<!-- test: error.reported-in-source-order-whatever-the-kind -->
```maxon
enum DeadShade
	light
	dark
end 'DeadShade'

function deadHelper() returns ExitCode
	return 3
end 'deadHelper'

let DeadLimit = 7

union DeadOutcome
	success
	failure
end 'DeadOutcome'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3219: specs/unused-file-private-declaration/error.reported-in-source-order-whatever-the-kind.maxon:2:6: unused enum: 'DeadShade' is file-private and nothing live in this file names it; delete it
error E3219: specs/unused-file-private-declaration/error.reported-in-source-order-whatever-the-kind.maxon:7:10: unused function: 'deadHelper' is file-private and nothing live in this file names it; delete it
error E3219: specs/unused-file-private-declaration/error.reported-in-source-order-whatever-the-kind.maxon:11:5: unused constant: 'DeadLimit' is file-private and nothing live in this file names it; delete it
error E3219: specs/unused-file-private-declaration/error.reported-in-source-order-whatever-the-kind.maxon:13:7: unused union: 'DeadOutcome' is file-private and nothing live in this file names it; delete it
```

<!-- test: error.the-same-name-in-another-file-does-not-keep-it-alive -->
```maxon
// --- file: api/lib.maxon
function helper() returns ExitCode
	return 4
end 'helper'

export function publicEntry() returns ExitCode
	return 5
end 'publicEntry'

// --- file: app/main.maxon
function helper() returns ExitCode
	return 6
end 'helper'

function main() returns ExitCode
	return helper() + publicEntry()
end 'main'
```
```maxoncstderr
error E3219: api/<fragment>:3:10: unused function: 'helper' is file-private and nothing live in this file names it; delete it
```

<!-- test: error.unused-interface -->
```maxon
interface UnusedShape
	function area() returns ExitCode
end 'UnusedShape'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3219: specs/unused-file-private-declaration/error.unused-interface.maxon:2:11: unused interface: 'UnusedShape' is file-private and nothing live in this file names it; delete it
```

<!-- test: error.an-unused-function-spelled-by-a-keyword -->
```maxon
function from() returns ExitCode
	return 3
end 'from'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3219: specs/unused-file-private-declaration/error.an-unused-function-spelled-by-a-keyword.maxon:2:10: unused function: 'from' is file-private and nothing live in this file names it; delete it
```

<!-- test: used.a-static-binding-in-a-type-body-is-not-audited -->
```maxon
type Counter
	static let unread = 5

	static function one() returns ExitCode
		return 1
	end 'one'
end 'Counter'

function main() returns ExitCode
	return Counter.one()
end 'main'
```
```exitcode
1
```

<!-- test: used.a-function-spelled-by-a-keyword -->
```maxon
typealias Integer = int(i64.min to i64.max)

function from(n Integer) returns Integer
	return n + 1
end 'from'

function main() returns ExitCode
	return from(41)
end 'main'
```
```exitcode
42
```

<!-- test: used.every-kind-named-by-main -->
```maxon
let Limit = 7

enum Shade
	light
	dark
end 'Shade'

union Outcome
	success
	failure
end 'Outcome'

type Counter
	export let start as ExitCode

	static function create() returns Counter
		return Counter{start: Limit}
	end 'create'
end 'Counter'

function pick(shade Shade) returns ExitCode
	match shade 'which'
		light then return 1
		dark then return 2
	end 'which'
end 'pick'

function describe(outcome Outcome) returns ExitCode
	match outcome 'which'
		success then return 10
		failure then return 20
	end 'which'
end 'describe'

function main() returns ExitCode
	return Counter.create().start + pick(Shade.dark) + describe(Outcome.success)
end 'main'
```
```exitcode
19
```

<!-- test: used.after-a-declaration-closed-by-a-bare-end -->
```maxon
enum Shade
	light
	dark
end 'Shade'

function one() returns ExitCode
	return 1
end

function main() returns ExitCode
	match Shade.dark 'which'
		light then return one()
		dark then return one() + 1
	end 'which'
end 'main'
```
```exitcode
2
```

<!-- test: used.through-a-chain-of-file-private-declarations -->
```maxon
let Limit = 4

let Doubled = Limit + Limit

function inner() returns ExitCode
	return Doubled
end 'inner'

function outer() returns ExitCode
	return inner() + 1
end 'outer'

function main() returns ExitCode
	return outer()
end 'main'
```
```exitcode
9
```

<!-- test: used.only-as-a-function-value -->
```maxon
typealias Step = function() returns ExitCode

function target() returns ExitCode
	return 6
end 'target'

function run(step Step) returns ExitCode
	return step()
end 'run'

function main() returns ExitCode
	return run(target)
end 'main'
```
```exitcode
6
```

<!-- test: used.only-in-a-string-interpolation -->
```maxon
let Greeting = "hello"

function main() returns ExitCode
	print("{Greeting}\n")
	return 0
end 'main'
```
```stdout
hello
```

<!-- test: used.only-by-an-exported-function -->
```maxon
// --- file: api/lib.maxon
function privateHelper() returns ExitCode
	return 5
end 'privateHelper'

export function publicEntry() returns ExitCode
	return privateHelper()
end 'publicEntry'

// --- file: app/main.maxon
function main() returns ExitCode
	return publicEntry()
end 'main'
```
```exitcode
5
```

<!-- test: used.only-in-one-branch-of-a-conditional-region -->
```maxon
function windowsOnly() returns ExitCode
	return 0
end 'windowsOnly'

function elsewhereOnly() returns ExitCode
	return 0
end 'elsewhereOnly'

function main() returns ExitCode
	#if os(Windows)
		return windowsOnly()
	#else
		return elsewhereOnly()
	#endif
end 'main'
```
```exitcode
0
```

<!-- test: used.declared-and-used-in-one-conditional-region -->
```maxon
#if os(Windows)
	function onlyHere() returns ExitCode
		return 0
	end 'onlyHere'
#else
	function onlyHere() returns ExitCode
		return 0
	end 'onlyHere'
#endif

function main() returns ExitCode
	return onlyHere()
end 'main'
```
```exitcode
0
```

<!-- test: used.declarations-indented-inside-a-conditional-region -->
```maxon
#if not os(Wasi)
	let Step = 3

	enum Shade
		light
		dark
	end 'Shade'

	function lift(value ExitCode) returns ExitCode
		return value + Step
	end 'lift'

	function pick(shade Shade) returns ExitCode
		match shade 'which'
			light then return 1
			dark then return lift(2)
		end 'which'
	end

	function main() returns ExitCode
		return pick(Shade.dark)
	end 'main'
#endif
```
```exitcode
5
```

<!-- test: error.unused-in-a-file-with-conditional-regions -->
```maxon
function unusedHere() returns ExitCode
	return 1
end 'unusedHere'

function main() returns ExitCode
	#if os(Windows)
		return 0
	#else
		return 0
	#endif
end 'main'
```
```maxoncstderr
error E3219: specs/unused-file-private-declaration/error.unused-in-a-file-with-conditional-regions.maxon:2:10: unused function: 'unusedHere' is file-private and nothing live in this file names it; delete it
```
