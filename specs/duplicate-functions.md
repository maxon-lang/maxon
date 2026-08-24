---
feature: duplicate-functions
status: stable
keywords: [duplicate, function, main, semantic, validation]
category: basics
---

## Documentation

### Duplicate Function Detection

The compiler detects duplicate function definitions and reports an error.

#### Same-file duplicates

If the same function name is defined more than once in a single file:

```text
error E3006: file.maxon:5:10: Duplicate function 'helper'
```

#### Multiple main functions across files

In a multi-file build, if more than one file defines a `main` function:

```text
error E3006: b.maxon:1:10: Duplicate function 'main'
```

#### No main function

Every program must have a `main` function. This is tested in the `basics` spec.

## Tests

<!-- test: error.same-file-duplicate -->
```maxon

typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
	return 1
end 'helper'

function helper() returns Integer
	return 2
end 'helper'

function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/duplicate-functions/error.same-file-duplicate.test:9:10: Duplicate function 'helper'
```

<!-- test: error.same-file-duplicate-main -->
```maxon
function main() returns ExitCode
	return 0
end 'main'

function main() returns ExitCode
	return 1
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/duplicate-functions/error.same-file-duplicate-main.test:6:10: Duplicate function 'main'
```

<!-- test: error.multi-file-duplicate-main -->
```maxon
// --- file: a.maxon
function main() returns ExitCode
	return 0
end 'main'

// --- file: b.maxon
function main() returns ExitCode
	return 1
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/duplicate-functions/error.multi-file-duplicate-main.test:8:10: Duplicate function 'main'
```

### Two files declaring one TYPE collide on the members the compiler wrote for it

⚠ **THE DIAGNOSTIC NAMES A MEMBER NOBODY WROTE, AT NO POSITION, AND THAT IS NOT WHAT IT SHOULD SAY.**
What has gone wrong is that two files declare a type of the same name; what is REPORTED is a duplicate
`hash`, with no file and no line, because the collision is only noticed when the two files' modules are
merged and each has built that type's synthesized body. These cases pin the REFUSAL, not the wording —
a diagnostic naming the second DECLARATION would be strictly better and would replace both messages.

⭐ **THE ENUM ARM IS LONG-STANDING AND WAS NEVER GATED; THE UNION ARM IS NEW, AND IT REPLACES A SILENT
MISCOMPILE.** A union has no synthesized `hash` of its own, so until its `.unionCases` companion was
minted by the union's own declaration there was nothing to collide and the program COMPILED — into a
binary that printed the right answer and then reported `MM leak: 1 allocation(s) remain`, exit 101.
MEASURED, with a single-file program using the identical construct exiting 0 as the control. Refusing
at compile time is what the enum arm already did.

<!-- test: error.two-files-declare-one-enum -->
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

enum Shape
	circle
	square
end 'Shape'

export function fromA() returns Integer
	return match Shape.circle 'm'
		circle gives 1
		square gives 2
	end 'm'
end 'fromA'

// --- file: b.maxon
enum Shape
	dot
	line
end 'Shape'

function main() returns ExitCode
	let t = Shape.dot
	print("{t.name} {fromA()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3006: Duplicate function 'Shape.hash'
```

<!-- test: error.two-files-declare-one-union -->
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

union Shape
	circle(r Integer)
	square(s Integer)
end 'Shape'

export function fromA() returns Integer
	return match Shape.circle(1) 'm'
		circle(r) gives r
		square(s) gives s
	end 'm'
end 'fromA'

// --- file: b.maxon
union Shape
	dot
	line
end 'Shape'

function main() returns ExitCode
	let t = Shape.dot
	print("{t.name} {fromA()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3006: Duplicate function 'Shape.unionCases.hash'
```
