---
feature: harness-gate-batched-cases-keep-their-own-names
---
# The batched-cases-keep-their-own-names gate

Two run cases share the type name Point and two share no type name, so the four run batched in two programs.

## Tests

<!-- test: batched-names.first -->

```maxon
typealias Coord = int(0 to 100)

type Point
	export var x as Coord
	export var y as Coord

	static function create(x Coord, y Coord) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function helper(point Point) returns Coord
	return point.x + point.y
end 'helper'

function main() returns ExitCode
	let point = Point.create(1, y: 2)
	print("first {helper(point)}\n")
	return (helper(point) - 3) as ExitCode
end 'main'
```

```exitcode
0
```

```stdout
first 3
```

<!-- test: batched-names.second -->

```maxon
type Point
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Point'

function helper(point Point) returns String
	return "second {point.label}"
end 'helper'

function main() returns ExitCode
	let point = Point.create("north")
	print("{helper(point)}\n")
	return 3
end 'main'
```

```exitcode
3
```

```stdout
second north
```

<!-- test: batched-names.third -->

```maxon
typealias Coord = int(0 to 1000)

type Offset
	export var x as Coord

	static function create(x Coord) returns Self
		return Self{x: x}
	end 'create'
end 'Offset'

function helper(point Offset, factor Coord) returns Coord
	return point.x * factor
end 'helper'

function main() returns ExitCode
	let point = Offset.create(7)
	print("third {helper(point, factor: 10)}\n")
	return (helper(point, factor: 1)) as ExitCode
end 'main'
```

```exitcode
7
```

```stdout
third 70
```

<!-- test: batched-names.fourth -->

```maxon
typealias Coord = int(0 to 1000)

enum Heading
	north
	south
end 'Heading'

function helper(heading Heading) returns Coord
	match heading 'pick'
		north then return 9
		south then return 1
	end 'pick'
end 'helper'

function main() returns ExitCode
	print("fourth {helper(Heading.north)}\n")
	return helper(Heading.north) as ExitCode
end 'main'
```

```exitcode
9
```

```stdout
fourth 9
```
