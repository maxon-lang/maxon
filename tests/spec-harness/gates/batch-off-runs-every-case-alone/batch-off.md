---
feature: harness-gate-batch-off-runs-every-case-alone
---
# The batch-off-runs-every-case-alone gate

Three run cases declare the same names with different shapes, and under `--batch=off` each must run alone.

## Tests

<!-- test: batch-off.first -->

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

<!-- test: batch-off.second -->

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

<!-- test: batch-off.third -->

```maxon
typealias Coord = int(0 to 1000)

type Point
	export var x as Coord

	static function create(x Coord) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function helper(point Point, factor Coord) returns Coord
	return point.x * factor
end 'helper'

function main() returns ExitCode
	let point = Point.create(7)
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
