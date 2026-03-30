---
feature: enum-struct-field-match
status: stable
keywords: [enum, struct, field, match, associated-values]
category: enums
---
# Enum Struct Field Match

## Documentation

Matching on an enum with associated values that was loaded from a struct field.

## Tests

<!-- test: enum-struct-field-match-let -->
Match an enum with associated values from struct field via let binding.
```maxon
typealias SmallInt = int(0 to u8.max)

export enum Color
		red(intensity SmallInt)
		blue
end 'Color'

export type Palette
		export var primary Color
		export var secondary Color
end 'Palette'

function checkPrimary(p Palette) returns bool
		let c = p.primary
		match c 'mc'
				red(_) then return true
				blue then return false
		end 'mc'
end 'checkPrimary'

function main() returns ExitCode
		let p = Palette{primary: Color.red(255), secondary: Color.blue}
		if checkPrimary(p) 'ok'
				return 1
		end 'ok'
		return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-struct-field-match-direct -->
Match an enum with associated values from struct field directly.
```maxon
typealias SmallInt = int(0 to u8.max)

export enum Shape
		circle(radius SmallInt)
		square(side SmallInt)
end 'Shape'

export type Drawing
		export var shape Shape
end 'Drawing'

function isCircle(d Drawing) returns bool
		match d.shape 'ms'
				circle(_) then return true
				square(_) then return false
		end 'ms'
end 'isCircle'

function main() returns ExitCode
		let d = Drawing{shape: Shape.circle(10)}
		if isCircle(d) 'ok'
				return 1
		end 'ok'
		return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-struct-field-match-extract -->
Extract associated value from an enum struct field.
```maxon
typealias SmallInt = int(0 to u8.max)

export enum Value
		number(n SmallInt)
		empty
end 'Value'

export type Container
		export var item Value
end 'Container'

function getNumber(c Container) returns SmallInt
		let v = c.item
		match v 'mv'
				number(n) then return n
				empty then return 0
		end 'mv'
end 'getNumber'

function main() returns ExitCode
		let c = Container{item: Value.number(42)}
		return getNumber(c)
end 'main'
```
```exitcode
42
```
