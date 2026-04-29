---
feature: union-cases
status: experimental
keywords: [union, unionCases, discriminant, exhaustive, match, serialization]
category: type-system
---

## Documentation

# Union unionCases

Every `union` with associated values has a compiler-synthesized companion type `U.unionCases` — a simple enum with one bare case per variant of `U`, in declaration order. It exposes the union's discriminant as a first-class enum value so reader/decoder code can match exhaustively on the tag.

```text
union Shape
  circle(radius i64)
  square(side i64)
  point
end 'Shape'

// Shape.unionCases is conceptually:
//   enum Shape.unionCases
//     circle    // rawValue 0
//     square    // rawValue 1
//     point     // rawValue 2
//   end
```

Because `Shape.unionCases` is a regular enum it inherits `.allCases`, `.allCaseNames`, `.rawValue`, `.fromRawValue`, `.name`, and `.ordinal`. Match arms over a `Shape.unionCases` value are exhaustiveness-checked, just like match arms over the union itself.

The intended use is symmetric (de)serialization: write the variant's `rawValue` to a buffer alongside its payload; on read, lift the raw `int` back to a `U.unionCases` via `fromRawValue` and match on it to dispatch the payload reader. Adding a new variant to the union forces a non-exhaustive-match build error in *both* writer and reader.

`.unionCases` is only synthesized for unions with associated values. Plain enums (no payloads) already expose `.allCases` / `.fromRawValue` directly.

## Tests

### Basic case construction

<!-- test: union-cases.basic-construct -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Shape
	circle(radius Integer)
	square(side Integer)
	point
end 'Shape'

function main() returns ExitCode
	let c = Shape.unionCases.circle
	print("{c.name}={c.rawValue}\n")
	let s = Shape.unionCases.square
	print("{s.name}={s.rawValue}\n")
	let p = Shape.unionCases.point
	print("{p.name}={p.rawValue}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
circle=0
square=1
point=2
```

### allCases iteration

<!-- test: union-cases.allcases-iteration -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Shape
	circle(radius Integer)
	square(side Integer)
	point
end 'Shape'

function main() returns ExitCode
	for kase in Shape.unionCases.allCases 'loop'
		print("{kase.name}\n")
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
circle
square
point
```

### fromRawValue round-trip

<!-- test: union-cases.fromrawvalue-roundtrip -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Shape
	circle(radius Integer)
	square(side Integer)
	point
end 'Shape'

function main() returns ExitCode
	let k0 = try Shape.unionCases.fromRawValue(0) otherwise return 1
	let k1 = try Shape.unionCases.fromRawValue(1) otherwise return 2
	let k2 = try Shape.unionCases.fromRawValue(2) otherwise return 3
	print("{k0.name}\n")
	print("{k1.name}\n")
	print("{k2.name}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
circle
square
point
```

### Exhaustive match dispatch

<!-- test: union-cases.match-exhaustive -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Shape
	circle(radius Integer)
	square(side Integer)
	point
end 'Shape'

function describe(k Shape.unionCases) returns Integer
	match k 'tag'
		circle then return 100
		square then return 200
		point then return 300
	end 'tag'
end 'describe'

function main() returns ExitCode
	let c = Shape.unionCases.circle
	let s = Shape.unionCases.square
	let p = Shape.unionCases.point
	let total = describe(c) + describe(s) + describe(p)
	if total == 600 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```
