---
feature: duplicate-typealias
status: stable
keywords: [parser, typealias, non-exported, cross-file, duplicate]
category: parser-edge-cases
---

# Duplicate Non-Exported Typealiases

## Documentation

Multiple files may independently define the same non-exported typealias
(e.g. `typealias MyInt = int(0 to 100)`). Because the aliases are not
exported, they are file-local and must not interfere with each other.

## Tests

<!-- test: non-exported-same-name-crossfile -->
```maxon
// --- file: a.maxon
typealias MyInt = int(0 to 1000)

export function doubleIt(x MyInt) returns MyInt
	return x + x
end 'doubleIt'

// --- file: b.maxon
typealias MyInt = int(0 to 1000)

export function tripleIt(x MyInt) returns MyInt
	return x + x + x
end 'tripleIt'

// --- file: main.maxon
function main() returns ExitCode
	let a = doubleIt(5)
	let b = tripleIt(3)
	return a + b
end 'main'
```
```exitcode
19
```

<!-- test: non-exported-same-name-different-range -->
```maxon
// --- file: a.maxon
typealias Limit = int(0 to 500)

export function clampA(x Limit) returns Limit
	return x
end 'clampA'

// --- file: b.maxon
typealias Limit = int(0 to 2000)

export function clampB(x Limit) returns Limit
	return x
end 'clampB'

// --- file: main.maxon
function main() returns ExitCode
	let a = clampA(100)
	let b = clampB(200)
	return a + b
end 'main'
```
```exitcode
300
```
