---
feature: ownership
status: experimental
keywords: [refcount, memory, ownership, scope, cycle, destructor]
category: memory-safety
---

# Ownership & Memory Management

## Documentation

### Refcount-Based Ownership

Every heap-allocated struct has a reference count. Assigning to a variable increments the count (incref); when a variable goes out of scope the count is decremented (decref). When the count reaches zero the object is freed immediately. No garbage collector or scope frame is involved.

**Rules summary:**

- `var p = Point{x: 1}` — allocates with rc=1.
- `let q = p` — same object, rc=2.
- `p = Point{x: 2}` — old Point decref'd (rc→1 since q holds it), new Point rc=1.
- Block exit — decref every variable declared in that block.
- Return — skips the returned variable's decref (ownership transfers to caller).
- Struct field assignment — decrefs old field value, increfs new.
- Container push — increfs the element; container holds the reference.
- Container remove / managed list remove — transfers the container's reference to the caller (no extra incref).
- Container overwrite (`set`) — decrefs old element, increfs new.
- `clear()` / container freed at scope exit — decrefs all elements.

### Cycle Detection

Reference cycles are a **compile error**. A type may not reference itself directly or indirectly through struct fields, enum associated values, or container element types. The compiler reports `E4014` with the cycle path.

## Tests

<!-- test: local-struct-freed -->
Struct created and used inside a function; no leaks.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function testLocal()
	@heap let p = Point.create(1, y: 2)
	print("{p.x}\n")
end 'testLocal'

function main() returns ExitCode
	testLocal()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: local-alias -->
Two variables alias the same object; freed once when both go out of scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function testAlias()
	let p = Point.create(3, y: 4)
	let q = p
	print("{q.x}\n")
	print("{p.y}\n")
end 'testAlias'

function main() returns ExitCode
	testAlias()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
4
```

<!-- test: reassignment-frees-old -->
Reassigning a var decrefs the old object; new object lives until block exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function testReassign()
	var x = Box.create(1)
	x = Box.create(2)
	print("{x.value}\n")
end 'testReassign'

function main() returns ExitCode
	testReassign()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: nested-block-frees -->
Struct created inside an if-block is freed when that block exits.
```maxon
typealias Integer = int(i64.min to i64.max)

type Widget
	export var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Widget'

function testNestedBlock(cond bool)
	var result = 0
	if cond 'check'
		@heap let w = Widget.create(42)
		result = w.id
	end 'check'
	print("{result}\n")
end 'testNestedBlock'

function main() returns ExitCode
	testNestedBlock(true)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

<!-- test: return-local-struct -->
Returning a struct transfers ownership to the caller without an extra incref/decref.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function makePoint(x Integer, y Integer) returns Point
	let p = Point.create(x, y: y)
	return p
end 'makePoint'

function main() returns ExitCode
	let p = makePoint(10, y: 20)
	print("{p.x}\n")
	print("{p.y}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
```

<!-- test: return-container-element-get -->
Returning a container element (`get`) keeps the element alive past container scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function getFirst(arr ItemArray) returns Item
	let elem = try arr.get(0) otherwise Item.create(-1)
	return elem
end 'getFirst'

function main() returns ExitCode
	var arr = ItemArray.create()
	arr.push(Item.create(99))
	let result = getFirst(arr)
	print("{result.value}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```

<!-- test: return-container-element-remove -->
Removing and returning an element transfers its refcount to the caller.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function popFirst(arr ItemArray) returns Item throws ArrayError
	return try arr.remove(0)
end 'popFirst'

function main() returns ExitCode
	var arr = ItemArray.create()
	arr.push(Item.create(77))
	arr.push(Item.create(88))
	let first = try popFirst(arr) otherwise 'err'
		return 99
	end 'err'
	print("{first.value}\n")
	print("{arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
77
1
```

<!-- test: return-struct-with-field-ref -->
Returning a struct whose field references another heap object keeps that object alive.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function makeOuter(v Integer) returns Outer
	let inner = Inner.create(v)
	let outer = Outer.create(inner)
	return outer
end 'makeOuter'

function main() returns ExitCode
	let o = makeOuter(55)
	print("{o.inner.value}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
55
```

<!-- test: return-param-borrow -->
Returning a field from a parameter; parameter outlives the call so the field stays valid.
```maxon
typealias Integer = int(i64.min to i64.max)

type Data
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Data'

type Wrapper
	export var data as Data

	static function create(data Data) returns Self
		return Self{data: data}
	end 'create'
end 'Wrapper'

function extractData(w Wrapper) returns Data
	return w.data
end 'extractData'

function main() returns ExitCode
	let d = Data.create(42)
	let w = Wrapper.create(d)
	let result = extractData(w)
	print("{result.value}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

<!-- test: container-push-incref -->
After pushing a struct into a container, the element stays alive past the push site's scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
	export var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Node'

typealias NodeArray = Array with Node

function main() returns ExitCode
	var arr = NodeArray.create()
	var count = 0
	if true 'scope'
		let n = Node.create(10)
		arr.push(n)
		count = arr.count()
	end 'scope'
	print("{count}\n")
	let elem = try arr.get(0) otherwise Node.create(-1)
	print("{elem.id}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
10
```

<!-- test: container-remove-decref -->
Removing an element and assigning it to a var; var owns it and frees it at scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
	var arr = ItemArray.create()
	arr.push(Item.create(1))
	arr.push(Item.create(2))
	arr.push(Item.create(3))
	let removed = try arr.remove(1) otherwise 'err'
		return 99
	end 'err'
	print("{removed.value}\n")
	print("{arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
2
```

<!-- test: container-push-remove-cycle -->
Many push/remove cycles with no leaks.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
	var arr = ItemArray.create()
	var i = 0
	while i < 10 'push'
		arr.push(Item.create(i))
		i = i + 1
	end 'push'
	var total = 0
	while arr.count() > 0 'remove'
		let elem = try arr.remove(0) otherwise 'err'
			return 99
		end 'err'
		total = total + elem.value
	end 'remove'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
45
```

<!-- test: container-overwrite-decrefs-old -->
Setting an element at an existing index decrefs the old element.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
	var arr = ItemArray.create()
	arr.push(Item.create(100))
	try arr.set(0, value: Item.create(200)) otherwise panic("test invariant: set OOB")
	let elem = try arr.get(0) otherwise Item.create(-1)
	print("{elem.value}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
200
```

<!-- test: container-freed-decrefs-elements -->
When a container goes out of scope all its elements are decref'd.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function fillArray() returns Integer
	var arr = ItemArray.create()
	arr.push(Item.create(1))
	arr.push(Item.create(2))
	arr.push(Item.create(3))
	return arr.count()
end 'fillArray'

function main() returns ExitCode
	let count = fillArray()
	print("{count}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: struct-field-assign-incref -->
Assigning a struct to a field increfs it; both outer and inner live until scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let inner = Inner.create(7)
	let outer = Outer.create(inner)
	print("{outer.inner.value}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: struct-field-overwrite -->
Overwriting a struct field decrefs the old value and increfs the new value.
```maxon
typealias Integer = int(i64.min to i64.max)

type Data
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Data'

type Container
	export var data as Data

	export function setData(newData Data)
		data = newData
	end 'setData'

	static function create(data Data) returns Self
		return Self{data: data}
	end 'create'
end 'Container'

function main() returns ExitCode
	let old = Data.create(10)
	var c = Container.create(old)
	c.setData(Data.create(20))
	print("{c.data.value}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
20
```

<!-- test: managed-list-insert-incref -->
Inserting a struct into a managed list increfs the value; the node holds the reference.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemManagedList = __ManagedList with Item

function main() returns ExitCode
	var managedList = ItemManagedList.create()
	let item = Item.create(99)
	let node = managedList.insertFirst(item)
	print("{node.value().value}\n")
	print("{managedList.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
1
```

<!-- test: managed-list-remove-decref -->
Removing a node and discarding the result frees the value at scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemManagedList = __ManagedList with Item

function main() returns ExitCode
	var managedList = ItemManagedList.create()
	let node = managedList.insertFirst(Item.create(50))
	managedList.remove(node)
	print("{managedList.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: managed-list-clear-decrefs-all -->
Clearing a managed list decrefs all node values.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemManagedList = __ManagedList with Item

function main() returns ExitCode
	var managedList = ItemManagedList.create()
	managedList.insertFirst(Item.create(1))
	managedList.insertLast(Item.create(2))
	managedList.insertLast(Item.create(3))
	managedList.clear()
	print("{managedList.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: global-container-push-local -->
Pushing a local struct into a global container keeps it alive beyond the local scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

var globalArr = ItemArray.create()

function pushLocal()
	let item = Item.create(123)
	globalArr.push(item)
end 'pushLocal'

function main() returns ExitCode
	pushLocal()
	let elem = try globalArr.get(0) otherwise Item.create(-1)
	print("{elem.value}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
123
```

<!-- test: global-container-remove-frees -->
Removing from a global container transfers ownership; element freed at scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

var globalArr = ItemArray.create()

function main() returns ExitCode
	globalArr.push(Item.create(10))
	globalArr.push(Item.create(20))
	let removed = try globalArr.remove(0) otherwise 'err'
		return 99
	end 'err'
	print("{removed.value}\n")
	print("{globalArr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
1
```

<!-- test: global-container-many-push-remove -->
Many push/remove cycles on a global container leave no leaks.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

var globalArr = ItemArray.create()

function main() returns ExitCode
	var i = 0
	while i < 20 'push'
		globalArr.push(Item.create(i))
		i = i + 1
	end 'push'
	var total = 0
	while globalArr.count() > 0 'remove'
		let elem = try globalArr.remove(0) otherwise 'err'
			return 99
		end 'err'
		total = total + elem.value
	end 'remove'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
190
```

<!-- test: if-then-block-cleanup -->
A struct created in an if-block is freed when the block exits.
```maxon
typealias Integer = int(i64.min to i64.max)

type Widget
	export var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Widget'

function main() returns ExitCode
	var result = 0
	if true 'check'
		@heap let w = Widget.create(5)
		result = w.id
	end 'check'
	print("{result}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: if-else-both-assign -->
Both branches of an if/else assign to an outer var; the correct value survives.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function choose(flag bool) returns Integer
	var result = Box.create(0)
	if flag 'branch'
		result = Box.create(1)
	end 'branch'
	if flag == false 'branch2'
		result = Box.create(2)
	end 'branch2'
	return result.value
end 'choose'

function main() returns ExitCode
	print("{choose(true)}\n")
	print("{choose(false)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
2
```

<!-- test: while-loop-iteration-cleanup -->
A struct created each loop iteration is freed before the next iteration begins.
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var val as Integer

	static function create(val Integer) returns Self
		return Self{val: val}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 5 'loop'
		@heap let c = Counter.create(i)
		total = total + c.val
		i = i + 1
	end 'loop'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
```

<!-- test: for-loop-elem-borrow -->
For-loop element variable is decref'd each iteration; no leaks.
```maxon
typealias Integer = int(i64.min to i64.max)

type Score
	export var points as Integer

	static function create(points Integer) returns Self
		return Self{points: points}
	end 'create'
end 'Score'

typealias ScoreArray = Array with Score

function main() returns ExitCode
	var scores = ScoreArray.create()
	scores.push(Score.create(10))
	scores.push(Score.create(20))
	scores.push(Score.create(30))
	var total = 0
	for s in scores 'loop'
		total = total + s.points
	end 'loop'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
60
```

<!-- test: match-case-cleanup -->
A struct created in a match arm is freed when the arm's block exits.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Color
	red
	blue
end 'Color'

type Paint
	export var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Paint'

function main() returns ExitCode
	let c = Color.red
	var result = 0
	var p = Paint.create(0)
	match c 'pick'
		red then p = Paint.create(7)
		blue then p = Paint.create(0)
	end 'pick'
	result = p.id
	print("{result}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: break-cleanup -->
A struct created before a break is freed when the break exits the loop block.
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var val as Integer

	static function create(val Integer) returns Self
		return Self{val: val}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 5 'loop'
		let c = Counter.create(i)
		if i == 3 'brk'
			total = total + c.val
			break
		end 'brk'
		total = total + c.val
		i = i + 1
	end 'loop'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

<!-- test: continue-cleanup -->
A struct created before a continue is freed before the loop restarts.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 5 'loop'
		let item = Item.create(i)
		i = i + 1
		if item.value == 2 'skip'
			continue
		end 'skip'
		total = total + item.value
	end 'loop'
	// 0+1+3+4 = 8
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8
```

<!-- test: closure-env-freed -->
The closure environment block is freed at block exit like any other struct.
```maxon
typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let offset = 5
	let result = apply(function(n Integer) gives n + offset, x: 10)
	print("{result}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
15
```

<!-- test: closure-captures-borrow -->
A closure captures a struct variable by address; the original var owns the struct.
```maxon
typealias Integer = int(i64.min to i64.max)

type Config
	export var level as Integer

typealias FnTypeAlias1 = function(Integer) returns Integer
	static function create(level Integer) returns Self
		return Self{level: level}
	end 'create'
end 'Config'

function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let cfg = Config.create(3)
	let result = apply(function(_ Integer) gives cfg.level, x: 0)
	print("{result}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: no-leak-nested-structs -->
A struct containing a struct field; both are freed with no leaks.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var val as Integer

	static function create(val Integer) returns Self
		return Self{val: val}
	end 'create'
end 'Inner'

type Outer
	export var child as Inner

	static function create(child Inner) returns Self
		return Self{child: child}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let o = Outer.create(Inner.create(42))
	print("{o.child.val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

<!-- test: no-leak-nested-containers -->
A container holding another container; all freed with no leaks.
```maxon
typealias Integer = int(i64.min to i64.max)

type Row
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Row'

typealias RowArray = Array with Row

type Table
	export var rows as RowArray

	static function create(rows RowArray) returns Self
		return Self{rows: rows}
	end 'create'
end 'Table'

typealias TableArray = Array with Table

function main() returns ExitCode
	var tables = TableArray.create()
	var rows1 = RowArray.create()
	rows1.push(Row.create(1))
	rows1.push(Row.create(2))
	tables.push(Table.create(rows1))
	var rows2 = RowArray.create()
	rows2.push(Row.create(3))
	tables.push(Table.create(rows2))
	print("{tables.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: no-leak-returned-managed-list -->
Build a managed list in a function, return it to the caller, caller frees it.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemManagedList = __ManagedList with Item

function buildManagedList() returns ItemManagedList
	var managedList = ItemManagedList.create()
	managedList.insertLast(Item.create(10))
	managedList.insertLast(Item.create(20))
	managedList.insertLast(Item.create(30))
	return managedList
end 'buildManagedList'

function main() returns ExitCode
	let managedList = buildManagedList()
	print("{managedList.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: cycle-direct-self-ref -->
A struct with a field of its own type is a compile error.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
	export var value as Integer
	export var next as Node
end 'Node'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E4014: specs/fragments/ownership/cycle-direct-self-ref.test:4:6: type 'Node' contains a reference cycle (via Node → next: Node); recursive type references are not allowed
```

<!-- test: cycle-enum-self-ref -->
An enum with a case that references its own enum type is a compile error.
```maxon
typealias Integer = int(i64.min to i64.max)

union Link
	tail
	link(value Integer, next Link)
end 'Link'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E4014: specs/fragments/ownership/cycle-enum-self-ref.test:4:7: type 'Link' contains a reference cycle (via Link → link.next: Link); recursive type references are not allowed
```

<!-- test: cycle-indirect-via-container -->
A struct with a container of itself is a compile error.
```maxon
typealias FolderArray = Array with Folder

type Folder
	export var name as String
	export var children as FolderArray
end 'Folder'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E4014: specs/fragments/ownership/cycle-indirect-via-container.test:4:6: type 'Folder' contains a reference cycle (via Folder → children: FolderArray → Folder); recursive type references are not allowed
```

<!-- test: cycle-mutual-recursion -->
Two structs that reference each other is a compile error.
```maxon
type A
	export var b as B
end 'A'

type B
	export var a as A
end 'B'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E4014: specs/fragments/ownership/cycle-mutual-recursion.test:2:6: type 'A' contains a reference cycle (via A → b: B → a: A); recursive type references are not allowed
```

<!-- test: a-parameter-reassigned-then-consumed-releases-each-value-once -->
⛔⛔ **A PARAMETER THE CALLEE REASSIGNS IS THE CALLER'S CELL, AND A CELL IS NEVER CONSUMED.** Both facts
hold of `p` below — the rebind makes it by-reference, the field store makes it consumed — and they decide
opposite things about who owns what. By-reference wins, because the cell outlives the frame: the caller
reads what the callee wrote (`s` is `"replaced"` here, so the sum is `8 + 8`) and still drops it. So the
callee's field store takes its OWN reference off a borrow, the caller transfers nothing at the call, and
`"outer"` is released exactly once by the rebind that displaced it.
```maxon
type Wrap
	export var v as String

	export static function make(p String) returns Wrap
		p = "replaced"
		return Wrap{v: p}
	end 'make'
end 'Wrap'

function main() returns ExitCode
	var s = "outer"
	let w = Wrap.make(s)
	return (w.v.count() + s.count()) as ExitCode
end 'main'
```
```exitcode
16
```

<!-- test: a-parameter-reassigned-from-another-parameter-then-consumed -->
The rebind's source is another PARAMETER, so three names end on one record — the cell, the field and the
caller's `t` — and each owes exactly one release. `"second"` is 6 bytes and all three names spell it.
```maxon
type Wrap
	export var v as String

	export static function make(p String, q String) returns Wrap
		p = q
		return Wrap{v: p}
	end 'make'
end 'Wrap'

function main() returns ExitCode
	var s = "outer"
	let t = "second"
	let w = Wrap.make(s, q: t)
	return (w.v.count() + s.count() + t.count()) as ExitCode
end 'main'
```
```exitcode
18
```

<!-- test: a-parameter-reassigned-in-a-loop-then-consumed -->
The rebind runs N times, so N-1 intermediate records are displaced and released before the consume ever
runs — the accounting cannot be settled once at the store's TEXT. `"x"` grows to `"x012"`, which the
caller sees too.
```maxon
type Wrap
	export var v as String

	export static function make(p String) returns Wrap
		for i in 0 upto 3 'grow'
			p = "{p}{i}"
		end 'grow'

		return Wrap{v: p}
	end 'make'
end 'Wrap'

function main() returns ExitCode
	var s = "x"
	let w = Wrap.make(s)
	return (w.v.count() + s.count()) as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: a-caller-may-rebind-what-a-consuming-callee-wrote-through-its-cell -->
⭐ **THE ARGUMENT WAS NOT MOVED, so reading it after the call is not E3102 and rebinding it is an ordinary
`var` rebind.** The caller still owns the cell's occupant, so the rebind below is what releases
`"replaced"` — and `w.v`'s own reference is why it is still there to be counted afterwards.
```maxon
type Wrap
	export var v as String

	export static function make(p String) returns Wrap
		p = "replaced"
		return Wrap{v: p}
	end 'make'
end 'Wrap'

function main() returns ExitCode
	var s = "outer"
	let w = Wrap.make(s)
	s = "{s}-again"
	return (w.v.count() + s.count()) as ExitCode
end 'main'
```
```exitcode
22
```

<!-- test: a-handler-binder-takes-a-parameters-name -->
A `try … otherwise (e)` handler binder is the fourth keyword that can stand in front of a binder list's
`(`, and it shadows for its handler BLOCK. `boom` always throws, so the store that runs is the handler's:
it moves the caught error into `why` and the parameter reaches no consume door at all.
```maxon
enum Fail implements Error
	nope
end 'Fail'

function boom() throws Fail
	throw Fail.nope
end 'boom'

type Wrap
	export var v as String
	export var why as Fail

	export static function make(p String) returns Wrap
		try boom() otherwise (p) 'caught'
			return Wrap{v: "handled", why: p}
		end 'caught'

		return Wrap{v: "unreached", why: Fail.nope}
	end 'make'
end 'Wrap'

function main() returns ExitCode
	let w = Wrap.make("the argument")
	return w.v.count() as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-closure-parameters-shadow-ends-at-its-line -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐ **A CLOSURE'S PARAMETERS BIND FOR THE ONE EXPRESSION AFTER `gives`, so the shadow is gone at the next
newline.** `p` on the last line below is the promise parameter again, and its `await` is what enrols this
frame the promise's owner. A shadow that outlived its line would leave nothing enrolled and
`requireConsumedPromiseIsOwned` refuses the body at that very `await` (E3141) — which is the loud half of
what a line column that never cleared would cost; the quiet half is a promise nobody reclaims.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Runtime.yield()
	return n + 1
end 'work'

function finish(p IntPromise) returns Integer
	let bump = function(p Integer) gives p + 1
	let extra = bump(4)
	return (await p) + extra
end 'finish'

function main() returns ExitCode
	let outer = async work(1)
	return finish(outer) as ExitCode
end 'main'
```
```exitcode
7
```
