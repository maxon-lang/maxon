---
feature: regions-freeze
status: experimental
keywords: [freeze, merge, isolated, immutable, region, use after move]
category: semantics
---

# `freeze` and `merge`

## Documentation

`freeze x` turns an `isolated` region into frozen data: the result is `immutable`, shared freely, and never
written again. It consumes a local, so reading the local afterwards is E3102. An expression whose inputs are
all values, `immutable` data or moved `isolated` data cannot point back into the current region, so
`freeze Point.create(2, y: 3)` needs no `isolated` declaration. A local that was written as ordinary mutable
data is part of its region and cannot be frozen: freeze a `clone()` of it instead.

`merge x` is the reverse: it moves an `isolated` region into the current one and the result is ordinary
mutable data.

## Tests

<!-- test: freeze-of-a-fresh-call-runs -->
The call's inputs are plain numbers, so its result is a region of its own and `freeze` accepts it; `plot` only reads its parameter and takes the frozen point and the mutable one alike.

```maxon
typealias Coord = int(0 to 1024)

type Point
	export var x as Coord
	export var y as Coord

	static function create(x Coord, y Coord) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function plot(p Point) returns String
	return "({p.x}, {p.y})"
end 'plot'

function main() returns ExitCode
	var cursor = Point.create(0, y: 0)
	cursor.x = 7
	let pinned = freeze Point.create(2, y: 3)
	print("{plot(pinned)} {plot(cursor)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
(2, 3) (7, 0)
```

<!-- test: freeze-of-a-mutable-local-refused -->
`cursor` was written as ordinary mutable data, so it belongs to the current region and cannot be frozen in place.

```maxon
typealias Coord = int(0 to 1024)

type Point
	export var x as Coord
	export var y as Coord

	static function create(x Coord, y Coord) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	var cursor = Point.create(0, y: 0)
	cursor.x = 1
	let pinned = freeze cursor
	print("{pinned.x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:16:22: 'cursor' is used as mutable at line 15; freeze a copy: `freeze cursor.clone()`
```

<!-- test: use-after-freeze -->
`freeze` consumes the isolated local, so the read of `bag` after it is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Bag
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function add(n Tally)
		self.total = self.total + n
	end 'add'

	function size() returns Tally
		return self.total
	end 'size'
end 'Bag'

function main() returns ExitCode
	var bag = Bag.create() as isolated Bag

	modify bag 'fill'
		bag.add(3)
	end 'fill'

	let done = freeze bag
	let after = bag.size()
	print("{done.size()} {after}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:14: use of moved value 'bag': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: let-of-an-isolated-local-moves-it -->
`let b = a` hands the isolated region from `a` to `b`, so the read of `a` afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = Box.create(1) as isolated Box
	let b = a
	let again = a.n
	print("{b.n} {again}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:14: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: let-of-an-isolated-local-runs-through-the-new-name -->
After `let b = a` only `b` names the region, and it is read through a one-statement open and released once at the end of the function.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = Box.create(4) as isolated Box
	let b = a
	print("{b.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: merge-isolated-into-region-runs -->
`merge chunk` moves the filled region into the current one, where the result is ordinary mutable data that `add` writes.

```maxon
typealias Tally = int(0 to u64.max)

type Bag
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function add(n Tally)
		self.total = self.total + n
	end 'add'

	function size() returns Tally
		return self.total
	end 'size'
end 'Bag'

function main() returns ExitCode
	var chunk = Bag.create() as isolated Bag

	modify chunk 'fill'
		chunk.add(5)
	end 'fill'

	var mine = merge chunk
	mine.add(7)
	print("{mine.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
12
```
