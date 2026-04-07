---
feature: challenge-array-of-structs
status: stable
keywords: array, struct, elements, memory
category: semantics
---
# Challenge Array Of Structs

## Documentation

## Arrays of Structs

Arrays can contain struct values. Each element is a complete copy of the struct.

## Tests

<!-- test: array-of-structs-literal -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let p1 = Point.create(x: 1, y: 2)
	let p2 = Point.create(x: 3, y: 4)
	let points = [p1, p2]
	let pt0 = try points.get(0) otherwise Point.create(x: 0, y: 0)
	let pt1 = try points.get(1) otherwise Point.create(x: 0, y: 0)
	return pt0.x + pt1.y
end 'main'
```
```exitcode
5
```

<!-- test: array-of-structs-indexed-access -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Pair
	export var first Integer
	export var second Integer

	static function create(first Integer, second Integer) returns Self
		return Self{first: first, second: second}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create(first: 10, second: 20)
	let arr = [p]
	let elem = try arr.get(0) otherwise Pair.create(first: 0, second: 0)
	return elem.first + elem.second
end 'main'
```
```exitcode
30
```

<!-- test: array-of-structs-with-enum-field -->
```maxon
// Regression test: structs with enum fields stored correctly in arrays
// Previously, 8-byte structs were stored by pointer instead of by value
enum Color
	red
	green
	blue
end 'Color'

type Item
	export var color Color

	static function create(color Color) returns Self
		return Self{color: color}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
	var items = ItemArray.create()
	items.push(Item.create(color: Color.red))
	items.push(Item.create(color: Color.green))
	items.push(Item.create(color: Color.blue))

	// Verify enum values are stored correctly (not pointers)
	let item0 = try items.get(0) otherwise Item.create(color: Color.blue)
	let item1 = try items.get(1) otherwise Item.create(color: Color.blue)
	let item2 = try items.get(2) otherwise Item.create(color: Color.red)

	// red=0, green=1, blue=2
	return item0.color.rawValue + item1.color.rawValue * 10 + item2.color.rawValue * 100
end 'main'
```
```exitcode
210
```

<!-- test: array-of-structs-enum-for-in-loop -->
```maxon
// Regression test: enum match works in for-in loop over struct array
enum Status
	pending
	active
	done
end 'Status'

type Task
	export var status Status

	static function create(status Status) returns Self
		return Self{status: status}
	end 'create'
end 'Task'

typealias TaskArray = Array with Task

function main() returns ExitCode
	var tasks = TaskArray.create()
	tasks.push(Task.create(status: Status.pending))
	tasks.push(Task.create(status: Status.active))
	tasks.push(Task.create(status: Status.done))

	var activeCount = 0
	for task in tasks 'loop'
		match task.status 'check'
			active then activeCount = activeCount + 1
			pending then break
			done then break
		end 'check'
	end 'loop'

	return activeCount
end 'main'
```
```exitcode
1
```
