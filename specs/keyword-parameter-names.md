---
feature: keyword-parameter-names
status: stable
keywords: [parser, parameter, keyword, type, enum, union, interface]
category: parser-edge-cases
---

# Keyword Parameter Names

## Documentation

Reserved keywords such as `type`, `enum`, `union`, and `interface` may be
used as function parameter names. Parameter lists are always enclosed in
parentheses, so they cannot be confused with top-level type declarations.

Historically the compiler's cross-file name pre-scanner walked tokens
linearly without tracking parenthesis nesting, so a parameter pair like
`type StdType` inside a function signature was misread as a top-level
`type StdType` declaration. That created a phantom non-exported type that
shadowed the real `StdType` across every other file in the project.
These tests lock in the fix — using `type`/`enum`/`union`/`interface`
as a parameter name must not shadow a real exported type declared in a
different file.

## Tests

<!-- test: type-as-parameter-name-crossfile -->
```maxon
// --- file: api/shared.maxon
module typealias StdType = int(i64.min to i64.max)

// --- file: api/helper.maxon
export function identity(type StdType) returns StdType
	return type
end 'identity'

// --- file: app/main.maxon
function main() returns ExitCode
	return identity(42)
end 'main'
```
```exitcode
42
```

<!-- test: enum-as-parameter-name-crossfile -->
```maxon
// --- file: api/shared.maxon
module typealias StdType = int(i64.min to i64.max)

// --- file: api/helper.maxon
export function pickOne(enum StdType) returns StdType
	return enum
end 'pickOne'

// --- file: app/main.maxon
function main() returns ExitCode
	return pickOne(7)
end 'main'
```
```exitcode
7
```

<!-- test: union-as-parameter-name-crossfile -->
```maxon
// --- file: api/shared.maxon
module typealias StdType = int(i64.min to i64.max)

// --- file: api/helper.maxon
export function asis(union StdType) returns StdType
	return union
end 'asis'

// --- file: app/main.maxon
function main() returns ExitCode
	return asis(3)
end 'main'
```
```exitcode
3
```

<!-- test: interface-as-parameter-name-crossfile -->
```maxon
// --- file: api/shared.maxon
module typealias StdType = int(i64.min to i64.max)

// --- file: api/helper.maxon
export function passthrough(interface StdType) returns StdType
	return interface
end 'passthrough'

// --- file: app/main.maxon
function main() returns ExitCode
	return passthrough(5)
end 'main'
```
```exitcode
5
```
