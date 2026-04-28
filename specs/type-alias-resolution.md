---
feature: type-alias-resolution
status: selfhosted
keywords: [typealias, type-resolution, fixed-point, cycle]
category: diagnostics
---

# Type alias resolution

## Documentation

Type aliases are resolved by the dedicated `TypeResolution` pass after the
parser has captured every `typealias` declaration into the project's
unresolved-typealias registry. The pass runs a fixed-point loop: each
iteration moves every entry whose right-hand side is fully resolvable into
the resolved registry, and stops when either the unresolved map is empty
(success) or no progress was made for a full iteration (cycle / unknown
reference).

This means:

- An alias may textually precede or follow the alias it references — the
  resolver doesn't depend on source-text order.
- A typealias whose right-hand side names another typealias is allowed; the
  resolver picks it up on whichever iteration the target alias is itself
  resolved.
- A reference to an undefined name produces E3011 (`semanticUnknownType`).
- A cyclic `typealias A = B; typealias B = A` chain produces E3014
  (`semanticTypeResolutionCycle`) on every entry that cannot break the
  cycle.

## Tests

<!-- test: alias-of-alias-backward -->
```maxon
typealias Score = int(0 to 100)
typealias BoundedScore = Score

function main() returns ExitCode
	let s = 42 as BoundedScore
	return s
end 'main'
```
```exitcode
42
```

<!-- test: alias-of-alias-forward -->
```maxon
typealias Counter = Tally
typealias Tally = int(0 to 1000)

function main() returns ExitCode
	let c = 7 as Counter
	return c
end 'main'
```
```exitcode
7
```

<!-- test: error.unknown-target -->
```maxon
typealias Foo = Bar

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3011: specs/fragments/type-alias-resolution/error.unknown-target.test:2:11: typealias 'Foo' references unknown type 'Bar'
```

<!-- test: error.cycle -->
```maxon
typealias A = B
typealias B = A

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3014: specs/fragments/type-alias-resolution/error.cycle.test:2:11: typealias 'A' depends on 'B', which is itself unresolved (cycle)
error E3014: specs/fragments/type-alias-resolution/error.cycle.test:3:11: typealias 'B' depends on 'A', which is itself unresolved (cycle)
```
