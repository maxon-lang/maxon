---
feature: harness-gate-a-shared-library-parse-rebase-leaves-the-index-at-its-baseline
---
# The shared-library-parse-rebase gate

Nine run cases that batch into one program, compiled by a single worker whose shared store already holds the library parses of the program before it. Reusing one of those parses rebases it against the settled signature index, and the rebase must leave that index exactly at its baseline for the next library parse it reuses.

## Tests

<!-- test: shared-rebase.case1 -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	var a = Point.create(1, y: 2)
	var b = a
	b.x = 99
	a = b
	print("{a.x}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```

<!-- test: shared-rebase.case2 -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(10, y: 20)
	var b = a.clone()
	b.x = 99
	if a is not b 'diff'
		return a.x + a.y
	end 'diff'
	return 0
end 'main'
```
```exitcode
30
```


<!-- test: shared-rebase.case3 -->
```maxon
function main() returns ExitCode
	let a = "hello"
	let b = a.clone()
	if a is not b 'diff'
		return 1
	end 'diff'
	return 0
end 'main'
```
```exitcode
1
```


<!-- test: shared-rebase.case4 -->
```maxon
function main() returns ExitCode
	let list = List from [10, 20, 30]
	return list.count()
end 'main'
```
```exitcode
3
```


<!-- test: shared-rebase.case5 -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
	var list = IntList.create()
	if list.isEmpty() 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```


<!-- test: shared-rebase.case6 -->
```maxon
function main() returns ExitCode
	let list = List from [10, 20, 30]
	let f = try list.first() otherwise 0
	let l = try list.last() otherwise 0
	print("{f}\n")
	print("{l}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
30
```

<!-- test: shared-rebase.case7 -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
		export var name as String
		export var value as Integer

		static function create(name String, value Integer) returns Self
			return Self{name: name, value: value}
		end 'create'
end 'Item'

typealias ItemArray = Array with Item

function makeClone() returns ItemArray
		var src = ItemArray.create()
		src.push(Item.create("first clone item long enough for heap", value: 10))
		src.push(Item.create("second clone item long enough for heap", value: 20))
		return src.clone()
		// src is freed when this function returns
end 'makeClone'

function main() returns ExitCode
		let cloned = makeClone()

		if cloned.count() != 2 'badCount'
				return 99
		end 'badCount'

		let item = try cloned.get(1) otherwise Item.create("", value: 0)
		return item.value
end 'main'
```
```exitcode
20
```


<!-- test: shared-rebase.case8 -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Leaf
		export var label as String
		export var value as Integer

		static function create(label String, value Integer) returns Self
			return Self{label: label, value: value}
		end 'create'
end 'Leaf'

type Mid
		export var leaf as Leaf

		static function create(leaf Leaf) returns Self
			return Self{leaf: leaf}
		end 'create'
end 'Mid'

type Outer
		export var mid as Mid

		static function create(mid Mid) returns Self
			return Self{mid: mid}
		end 'create'
end 'Outer'

typealias OuterArray = Array with Outer

function makeClone() returns OuterArray
		var src = OuterArray.create()
		src.push(Outer.create(Mid.create(Leaf.create("a nested label long enough to reach the heap", value: 7))))
		return src.clone()
		// src and its whole three-level element graph are freed when this function returns
end 'makeClone'

function main() returns ExitCode
		let cloned = makeClone()

		if cloned.count() != 1 'badCount'
				return 99
		end 'badCount'

		let outer = try cloned.get(0) otherwise Outer.create(Mid.create(Leaf.create("", value: 0)))
		return outer.mid.leaf.value
end 'main'
```
```exitcode
7
```


<!-- test: shared-rebase.case9 -->
```maxon
type Holder
	export var f as __ManagedFile
	static function create(f __ManagedFile) returns Self
		return Self{f: f}
	end 'create'
end 'Holder'
typealias Holders = Array with Holder
function main() returns ExitCode
	var a = Holders.create()
	print("{a.count()}\n")
	a.push(Holder.create(try __ManagedFile.openRead(b"no-such-file.bin".managed) otherwise return 3))

	for _ in a 'each'
		print("walked\n")
	end 'each'

	return a.count() as ExitCode
end 'main'
```
```exitcode
3
```
```stdout
0
```

