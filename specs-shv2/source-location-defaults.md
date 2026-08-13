---
feature: source-location-defaults
status: experimental
keywords: [__line__, __file__, caller location, default, parameters, source location, assertion]
category: core
---

# Caller-Location Default Arguments

## Documentation

`__line__` and `__file__` are **caller-location defaults**. Each one is legal in exactly
one place — as the default value of a function parameter — and each expands **at the call
site**, so every caller supplies its own location without writing anything.

```text
function check(value Tally, from String = __file__, at SourceLineNumber = __line__) returns bool
	print("checked at {from}:{at}\n")
	return value > 0
end 'check'
```

A caller writes `check(n)` and the callee receives the caller's file and the caller's line.

### Both, or neither

`__file__` is not decoration. A failure raised inside a shared helper resolves `__line__`
against *that helper's* file, so a line number without a file names a line in a file nobody
identified. Report the pair or report neither.

### What each one expands to

| | Value | Type to declare the parameter as |
|---|---|---|
| `__line__` | The line of the token naming the callee at the call site | `SourceLineNumber` |
| `__file__` | The calling file's path relative to the compile root, `/`-separated | `String` |

`SourceLineNumber` is declared in the stdlib as `int(1 to i32.max)`: line numbers are
1-based, and the compiler counts them in a 32-bit counter. There is no `SourceFilePath`
typealias because Maxon has no typealias over a struct type — `__file__` produces a
`String`, and the parameter is declared `String`.

`__file__` is deliberately **relative, not absolute**. An absolute path is a property of the
machine that ran the compiler, not of the program: baking one into the binary makes the
output differ between two checkouts of the same commit, which breaks byte-parity gates and
golden transcripts. The path is spelled the same way compiler diagnostics spell theirs —
relative to the compile root, with `/` separators on every host.

### The call site is the whole call

Maxon expressions are newline-delimited, so a call always sits on one line and `__line__` has
only one line to name. The rule is nevertheless stated against a specific token — the one
naming the callee, i.e. the function name, or the method name after the final `.` — so that
it stays unambiguous if the grammar ever admits a call that spans lines.

### An explicit argument wins

Passing the argument explicitly suppresses the expansion entirely; the caller's value is used
unchanged. That is what lets one assertion helper forward a location it was itself given.

```text
function fail(message String, from String = __file__, at SourceLineNumber = __line__)
	printError("{from}:{at}: {message}\n")
end 'fail'

function expectTrue(ok bool, from String = __file__, at SourceLineNumber = __line__)
	if not ok 'bad'
		fail("expected true", from: from, at: at)   // forwards the CALLER's location
	end 'bad'
end 'expectTrue'
```

Without the explicit forward, `fail` would report the line inside `expectTrue` — which is
exactly the bug the feature exists to prevent.

### Nowhere else

Anywhere other than a parameter default is an error (E2060), including an ordinary
expression and a struct field default. This is what keeps `__line__` and `__file__` from
becoming a general reflection facility: they are a calling convention, not a way to ask the
compiler where you are.

```text
let here = __line__                     // E2060
type Marker
	let at as SourceLineNumber = __line__   // E2060 — a field default is not a parameter default
end 'Marker'
```

Both names are reserved words, so no program can declare or shadow them.

## Tests

<!-- test: caller-line -->
```maxon
// --- file: main.maxon
function reportLine(tag String, at SourceLineNumber = __line__) returns SourceLineNumber
	print("{tag}@{at}\n")
	return at
end 'reportLine'

function main() returns ExitCode
	let n = reportLine("call")
	return (n - 1) as ExitCode
end 'main'
```
```exitcode
6
```
```stdout
call@7
```

Single-file, deliberately: its line numbers are those of the generated fragment, which begins
with a `// Test:` header line. That only holds while the test compiles as its own unit, so
this test is also what keeps the spec harness from batching caller-location tests together —
batched, every line here would shift by whatever precedes it in the batch.

<!-- test: caller-line-single-file -->
```maxon
function markHere(tag String, at SourceLineNumber = __line__) returns SourceLineNumber
	print("{tag}@{at}\n")
	return at
end 'markHere'

function main() returns ExitCode
	let n = markHere("single")
	return (n - 1) as ExitCode
end 'main'
```
```exitcode
7
```
```stdout
single@8
```

<!-- test: caller-line-differs-per-call-site -->
```maxon
// --- file: main.maxon
function lineOf(tag String, at SourceLineNumber = __line__) returns SourceLineNumber
	print("{tag}@{at}\n")
	return at
end 'lineOf'

function main() returns ExitCode
	let first = lineOf("first")
	let second = lineOf("second")
	return (second - first) as ExitCode
end 'main'
```
```exitcode
1
```
```stdout
first@7
second@8
```

<!-- test: caller-file-and-line-across-files -->
```maxon
// --- file: helper.maxon
export function whereAmI(tag String, file String = __file__, at SourceLineNumber = __line__) returns SourceLineNumber
	print("{tag}: {file}:{at}\n")
	return at
end 'whereAmI'

export function callFromHelper() returns SourceLineNumber
	return whereAmI("insideHelper")
end 'callFromHelper'

// --- file: main.maxon
function main() returns ExitCode
	let inHelper = callFromHelper()
	let inMain = whereAmI("insideMain")
	return (inMain + inHelper) as ExitCode
end 'main'
```
```exitcode
10
```
```stdout
insideHelper: helper.maxon:7
insideMain: main.maxon:3
```

<!-- test: explicit-argument-overrides -->
```maxon
// --- file: main.maxon
function tell(tag String, file String = __file__, at SourceLineNumber = __line__) returns SourceLineNumber
	print("{tag}: {file}:{at}\n")
	return at
end 'tell'

function main() returns ExitCode
	let d = tell("defaulted")
	let e = tell("explicit", file: "hand-written.maxon", at: 99)
	return (e - d) as ExitCode
end 'main'
```
```exitcode
92
```
```stdout
defaulted: main.maxon:7
explicit: hand-written.maxon:99
```

<!-- test: ordinary-default-alongside -->
```maxon
// --- file: main.maxon
typealias Severity = int(0 to 9)

function note(tag String, level Severity = 3, file String = __file__, at SourceLineNumber = __line__) returns SourceLineNumber
	print("{tag} level={level} {file}:{at}\n")
	return at
end 'note'

function main() returns ExitCode
	let a = note("bothDefaulted")
	let b = note("levelGiven", level: 5)
	return (b - a) as ExitCode
end 'main'
```
```exitcode
1
```
```stdout
bothDefaulted level=3 main.maxon:9
levelGiven level=5 main.maxon:10
```

<!-- test: method-call-sites -->
```maxon
// --- file: main.maxon
type Probe
	let tag as String

	export static function create(tag String) returns Self
		return Self{tag: tag}
	end 'create'

	export static function mark(tag String, at SourceLineNumber = __line__) returns SourceLineNumber
		print("{tag}@{at}\n")
		return at
	end 'mark'

	export function hit(at SourceLineNumber = __line__) returns SourceLineNumber
		print("{self.tag}#{at}\n")
		return at
	end 'hit'
end 'Probe'

function main() returns ExitCode
	let s = Probe.mark("static")
	let p = Probe.create("inst")
	let i = p.hit()
	return (i - s) as ExitCode
end 'main'
```
```exitcode
2
```
```stdout
static@20
inst#22
```

<!-- test: error.line-outside-default -->
```maxon
function main() returns ExitCode
	let here = __line__
	return here as ExitCode
end 'main'
```
```maxoncstderr
error E2060: specs/fragments/source-location-defaults/error.line-outside-default.test:3:13: '__line__' is only valid as a function parameter's default value, where it expands to the caller's location at each call site. Declare a parameter such as 'at SourceLineNumber = __line__' or 'from String = __file__' and read the value from there.
```

<!-- test: error.file-outside-default -->
```maxon
function main() returns ExitCode
	let spot = __file__
	print("{spot}\n")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2060: specs/fragments/source-location-defaults/error.file-outside-default.test:3:13: '__file__' is only valid as a function parameter's default value, where it expands to the caller's location at each call site. Declare a parameter such as 'at SourceLineNumber = __line__' or 'from String = __file__' and read the value from there.
```

<!-- test: error.field-default -->
```maxon
type Marker
	let at as SourceLineNumber = __line__
end 'Marker'

function main() returns ExitCode
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2060: specs/fragments/source-location-defaults/error.field-default.test:3:31: '__line__' is only valid as a function parameter's default value, where it expands to the caller's location at each call site. Declare a parameter such as 'at SourceLineNumber = __line__' or 'from String = __file__' and read the value from there.
```
