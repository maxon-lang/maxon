---
feature: ownership-edge-cases
status: experimental
keywords: [refcount, memory, ownership, destructor, cleanup]
category: memory-safety
---

# Ownership & Memory Management Edge Cases

Tests for the refcount-based memory manager, ordered from simple to complex.
Uses `MmTrace: true` so the trace log verifies correct incref/decref/free behaviour.

## Tests

<!-- test: rc-single-alloc-freed -->
Single struct allocated and freed in the same function scope.
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
	@heap let p = Point.create(1, y: 2)
	return p.x
end 'main'
```
```exitcode
1
```

<!-- test: rc-alias-incref -->
Aliasing a struct increfs it; both variables share refcount and object is freed once.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	@heap let a = Box.create(42)
	let b = a
	return b.value
end 'main'
```
```exitcode
42
```

<!-- test: rc-reassign-decrefs-old -->
Reassigning a var decrefs the old object immediately; the old object must be freed before scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Tag
	export var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Tag'

function main() returns ExitCode
	var t = Tag.create(1)
	t = Tag.create(2)
	t = Tag.create(3)
	return t.id
end 'main'
```
```exitcode
3
```

<!-- test: rc-inner-block-freed -->
Struct created in an inner if-block is freed when that block exits, before the outer block ends.
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
	if true 'inner'
		@heap let w = Widget.create(7)
		result = w.id
	end 'inner'
	return result
end 'main'
```
```exitcode
7
```

<!-- test: rc-return-transfers-ownership -->
Returning a struct skips its decref; caller receives ownership and frees it at its own scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
	export var kind as Integer

	static function create(kind Integer) returns Self
		return Self{kind: kind}
	end 'create'
end 'Token'

function makeToken(k Integer) returns Token
	let t = Token.create(k)
	return t
end 'makeToken'

function main() returns ExitCode
	let tok = makeToken(99)
	return tok.kind
end 'main'
```
```exitcode
99
```

<!-- test: rc-alias-survives-reassign -->
Aliased reference keeps object alive when the original var is reassigned.
```maxon
typealias Integer = int(i64.min to i64.max)

type Num
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Num'

function main() returns ExitCode
	var a = Num.create(10)
	let b = a
	a = Num.create(20)
	return b.v + a.v
end 'main'
```
```exitcode
30
```

<!-- test: rc-loop-per-iteration-freed -->
A struct allocated each loop iteration is freed at loop-block exit before the next iteration.
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 4 'loop'
		@heap let c = Counter.create(i)
		total = total + c.n
		i = i + 1
	end 'loop'
	return total
end 'main'
```
```exitcode
6
```

<!-- test: rc-break-frees-before-exit -->
Struct allocated before a break is decref'd before the loop block is exited.
```maxon
typealias Integer = int(i64.min to i64.max)

type Step
	export var val as Integer

	static function create(val Integer) returns Self
		return Self{val: val}
	end 'create'
end 'Step'

function main() returns ExitCode
	var i = 0
	while i < 10 'loop'
		@heap let s = Step.create(i)
		if s.val == 3 'stop'
			break
		end 'stop'
		i = i + 1
	end 'loop'
	return i
end 'main'
```
```exitcode
3
```

<!-- test: rc-continue-frees-before-restart -->
Struct allocated before a continue is decref'd before the loop restarts.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Item'

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 5 'loop'
		@heap let item = Item.create(i)
		i = i + 1
		if item.v == 2 'skip'
			continue
		end 'skip'
		total = total + item.v
	end 'loop'
	return total
end 'main'
```
```exitcode
8
```

<!-- test: rc-nested-struct-field-incref -->
When a struct literal is assigned as a field, the field value is incref'd; both outer and inner are freed correctly.
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
	let inner = Inner.create(55)
	let outer = Outer.create(inner)
	return outer.child.val
end 'main'
```
```exitcode
55
```

<!-- test: rc-nested-struct-deep-freed -->
Three-level nested struct: all three levels are freed when the outermost var leaves scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type A
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'A'

type B
	export var a as A

	static function create(a A) returns Self
		return Self{a: a}
	end 'create'
end 'B'

type C
	export var b as B

	static function create(b B) returns Self
		return Self{b: b}
	end 'create'
end 'C'

function main() returns ExitCode
	let c = C.create(B.create(A.create(7)))
	return c.b.a.n
end 'main'
```
```exitcode
7
```

<!-- test: rc-field-overwrite-decrefs-old -->
Overwriting a struct field via a method decrefs the old field value and increfs the new one.
```maxon
typealias Integer = int(i64.min to i64.max)

type Payload
	export var data as Integer

	static function create(data Integer) returns Self
		return Self{data: data}
	end 'create'
end 'Payload'

type Container
	export var payload as Payload

	export function setPayload(p Payload)
		payload = p
	end 'setPayload'

	static function create(payload Payload) returns Self
		return Self{payload: payload}
	end 'create'
end 'Container'

function main() returns ExitCode
	let old = Payload.create(1)
	var c = Container.create(old)
	c.setPayload(Payload.create(2))
	return c.payload.data
end 'main'
```
```exitcode
2
```

<!-- test: rc-field-overwrite-managed-list -->
Overwriting a struct field three times; each old value must be freed promptly.
```maxon
typealias Integer = int(i64.min to i64.max)

type Val
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Val'

type Holder
	export var v as Val

	export function set(newV Val)
		v = newV
	end 'set'

	static function create(v Val) returns Self
		return Self{v: v}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create(Val.create(0))
	h.set(Val.create(10))
	h.set(Val.create(20))
	h.set(Val.create(30))
	return h.v.n
end 'main'
```
```exitcode
30
```

<!-- test: rc-container-push-incref -->
Pushing a struct into an array increfs it; after the local var leaves scope the element still lives.
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
	if true 'scope'
		let n = Node.create(10)
		arr.push(n)
	end 'scope'
	let got = try arr.get(0) otherwise Node.create(-1)
	return got.id
end 'main'
```
```exitcode
10
```

<!-- test: rc-container-pop-decrefs -->
Popping the last element and discarding the result frees the element at scope exit.
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
	arr.push(Node.create(1))
	arr.push(Node.create(2))
	let popped = try arr.remove(arr.count() - 1) otherwise 'err'
		return 99
	end 'err'
	return arr.count() + popped.id - popped.id
end 'main'
```
```exitcode
1
```

<!-- test: rc-container-overwrite-decrefs-old -->
Setting an element at an existing index must decref the old element and incref the new one.
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
	let got = try arr.get(0) otherwise Item.create(-1)
	return got.value
end 'main'
```
```exitcode
200
```

<!-- test: rc-container-clear-decrefs-all -->
Clearing an array decrefs every element; all elements freed when rc hits 0.
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
	arr.clear()
	return arr.count()
end 'main'
```
```exitcode
0
```

<!-- test: rc-container-scope-exit-decrefs-elements -->
When a container holding struct elements goes out of scope, all elements are decref'd.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function fill() returns Integer
	var arr = ItemArray.create()
	arr.push(Item.create(10))
	arr.push(Item.create(20))
	arr.push(Item.create(30))
	return arr.count()
end 'fill'

function main() returns ExitCode
	let n = fill()
	return n
end 'main'
```
```exitcode
3
```

<!-- test: rc-insert-then-remove-no-leak -->
Insert many structs then remove them all; zero elements remain in memory.
```maxon
typealias Integer = int(i64.min to i64.max)

type Entry
	export var key as Integer

	static function create(key Integer) returns Self
		return Self{key: key}
	end 'create'
end 'Entry'

typealias EntryArray = Array with Entry

function main() returns ExitCode
	var arr = EntryArray.create()
	var i = 0
	while i < 5 'push'
		arr.push(Entry.create(i))
		i = i + 1
	end 'push'
	var total = 0
	while arr.count() > 0 'pop'
		let e = try arr.remove(0) otherwise 'err'
			return 99
		end 'err'
		total = total + e.key
	end 'pop'
	return total
end 'main'
```
```exitcode
10
```

<!-- test: rc-insert-in-middle-no-leak -->
Insert at index 0 into an existing array; shiftRight zeroes the gap so no double-free occurs.
```maxon
typealias Integer = int(i64.min to i64.max)

type Val
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Val'

typealias ValArray = Array with Val

function main() returns ExitCode
	var arr = ValArray.create()
	arr.push(Val.create(10))
	arr.push(Val.create(30))
	arr.insert(1, value: Val.create(20))
	let a = try arr.get(0) otherwise Val.create(-1)
	let b = try arr.get(1) otherwise Val.create(-1)
	let c = try arr.get(2) otherwise Val.create(-1)
	return a.n + b.n + c.n
end 'main'
```
```exitcode
60
```

<!-- test: rc-remove-middle-no-double-free -->
Removing the middle element from an array; shiftLeft zeroes the trailing slot so setLength does not double-decref.
```maxon
typealias Integer = int(i64.min to i64.max)

type Val
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Val'

typealias ValArray = Array with Val

function main() returns ExitCode
	var arr = ValArray.create()
	arr.push(Val.create(1))
	arr.push(Val.create(2))
	arr.push(Val.create(3))
	let removed = try arr.remove(1) otherwise 'err'
		return 99
	end 'err'
	return removed.n + arr.count()
end 'main'
```
```exitcode
4
```

<!-- test: rc-nested-container-freed -->
An array whose element type itself contains a struct field; freeing the outer array frees all nested objects.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Inner'

type Wrapper
	export var inner as Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Wrapper'

typealias WrapperArray = Array with Wrapper

function main() returns ExitCode
	var arr = WrapperArray.create()
	arr.push(Wrapper.create(Inner.create(1)))
	arr.push(Wrapper.create(Inner.create(2)))
	return arr.count()
end 'main'
```
```exitcode
2
```

<!-- test: rc-return-from-inner-block-cleanup -->
Returning from inside a nested block must decref all locals in every enclosing block before returning.
```maxon
typealias Integer = int(i64.min to i64.max)

type Step
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Step'

function compute(flag bool) returns Integer
	@heap let outer = Step.create(1)
	if flag 'inner'
		@heap let inner = Step.create(2)
		return outer.n + inner.n
	end 'inner'
	return outer.n
end 'compute'

function main() returns ExitCode
	return compute(true)
end 'main'
```
```exitcode
3
```

<!-- test: rc-return-container-element -->
Getting an element from a container and returning it; element rc stays above 0 while container is freed.
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
	arr.push(Item.create(77))
	let result = getFirst(arr)
	return result.value
end 'main'
```
```exitcode
77
```

<!-- test: rc-global-struct-outlives-local -->
A global variable holds a struct that outlives the function that created it.
```maxon
typealias Integer = int(i64.min to i64.max)

type Cfg
	export var level as Integer

	static function create(level Integer) returns Self
		return Self{level: level}
	end 'create'
end 'Cfg'

var globalCfg = Cfg.create(0)

function setup()
	globalCfg = Cfg.create(42)
end 'setup'

function main() returns ExitCode
	setup()
	return globalCfg.level
end 'main'
```
```exitcode
42
```

<!-- test: rc-global-reassign-decrefs-old -->
Reassigning a global struct var decrefs the old object and increfs the new one.
```maxon
typealias Integer = int(i64.min to i64.max)

type State
	export var val as Integer

	static function create(val Integer) returns Self
		return Self{val: val}
	end 'create'
end 'State'

var g = State.create(0)

function step(n Integer)
	g = State.create(n)
end 'step'

function main() returns ExitCode
	step(10)
	step(20)
	step(30)
	return g.val
end 'main'
```
```exitcode
30
```

<!-- test: rc-enum-no-struct-payload-freed -->
A simple enum enum (no struct payload) is freed correctly at scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Color
	red
	green
	blue
end 'Color'

function colorCode(c Color) returns Integer
	let result = match c 'pick'
		red   gives 1
		green gives 2
		blue  gives 3
	end 'pick'
	return result
end 'colorCode'

function main() returns ExitCode
	let c = Color.green
	return colorCode(c)
end 'main'
```
```exitcode
2
```

<!-- test: rc-enum-struct-payload-freed -->
A enum case with a struct-typed associated value; when the enum is freed its payload must be decref'd.
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Shape
	empty
	solid(body Body)
end 'Shape'

function massOf(s Shape) returns Integer
	match s 'check'
		empty then return 0
		solid(b) then return b.mass
	end 'check'
end 'massOf'

function main() returns ExitCode
	let s = Shape.solid(Body.create(5))
	return massOf(s)
end 'main'
```
```exitcode
5
```

<!-- test: rc-closure-env-freed -->
Closure environment block is allocated as a struct and freed when the closure variable goes out of scope.
```maxon
typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let offset = 5
	let result = apply(function(n Integer) gives n + offset, x: 10)
	return result
end 'main'
```
```exitcode
15
```

<!-- test: rc-closure-captures-struct -->
Closure captures a struct variable by address; the closure env is freed at scope exit but the original struct lives on.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias FnTypeAlias1 = function(Integer) returns Integer

type Config
	export var level as Integer

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
	return result
end 'main'
```
```exitcode
3
```

<!-- test: rc-error-path-cleanup -->
On the error path of a try expression the locally allocated struct must still be freed.
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
	let got = try arr.get(0) otherwise Item.create(99)
	return got.value
end 'main'
```
```exitcode
99
```

<!-- test: rc-managed-list-insert-incref -->
Inserting a struct into a managed list increfs the value; the node holds the reference.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
	export var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Token'

typealias TokenManagedList = __ManagedList with Token

function main() returns ExitCode
	var managedList = TokenManagedList.create()
	let t = Token.create(7)
	let node = managedList.insertFirst(t)
	return node.value().id
end 'main'
```
```exitcode
7
```

<!-- test: rc-managed-list-remove-decrefs -->
Removing a node from a managed list transfers ownership; value is freed when the result var leaves scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
	export var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Token'

typealias TokenManagedList = __ManagedList with Token

function main() returns ExitCode
	var managedList = TokenManagedList.create()
	let node = managedList.insertFirst(Token.create(9))
	let removed = managedList.remove(node)
	return removed.id + managedList.count()
end 'main'
```
```exitcode
9
```

<!-- test: rc-managed-list-clear-decrefs-all -->
Clearing a managed list decrefs every node value; all values freed when rc hits 0.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
	export var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Token'

typealias TokenManagedList = __ManagedList with Token

function main() returns ExitCode
	var managedList = TokenManagedList.create()
	managedList.insertLast(Token.create(1))
	managedList.insertLast(Token.create(2))
	managedList.insertLast(Token.create(3))
	managedList.clear()
	return managedList.count()
end 'main'
```
```exitcode
0
```

<!-- test: rc-managed-list-node-set-value-decrefs-old -->
Calling `setValue` on a managed list node decrefs the old value and increfs the new one.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
	export var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Token'

typealias TokenManagedList = __ManagedList with Token

function main() returns ExitCode
	var managedList = TokenManagedList.create()
	var node = managedList.insertFirst(Token.create(1))
	node.setValue(Token.create(99))
	return node.value().id
end 'main'
```
```exitcode
99
```

<!-- test: rc-for-in-elem-decrefed -->
In a for-in loop over a struct array each element reference is decref'd at the end of the loop body.
```maxon
typealias Integer = int(i64.min to i64.max)

type Score
	export var pts as Integer

	static function create(pts Integer) returns Self
		return Self{pts: pts}
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
		total = total + s.pts
	end 'loop'
	return total
end 'main'
```
```exitcode
60
```

<!-- test: rc-multiple-aliases-freed-once -->
Three aliases to the same object; the object is freed exactly once when the last alias leaves scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Data
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Data'

function main() returns ExitCode
	@heap let a = Data.create(7)
	let b = a
	let c = a
	return a.n + b.n + c.n
end 'main'
```
```exitcode
21
```

<!-- test: rc-deep-container-of-containers -->
An array of arrays of structs; freeing the outer array cascades through all levels.
```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var val as Integer

	static function create(val Integer) returns Self
		return Self{val: val}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell
typealias Grid = Array with CellArray

function main() returns ExitCode
	var grid = Grid.create()
	var row1 = CellArray.create()
	row1.push(Cell.create(1))
	row1.push(Cell.create(2))
	var row2 = CellArray.create()
	row2.push(Cell.create(3))
	grid.push(row1)
	grid.push(row2)
	return grid.count()
end 'main'
```
```exitcode
2
```

<!-- test: rc-struct-with-array-field-freed -->
A struct that owns an array field; when the struct is freed the array (and its elements) are freed too.
```maxon
typealias Integer = int(i64.min to i64.max)

type Entry
	export var val as Integer

	static function create(val Integer) returns Self
		return Self{val: val}
	end 'create'
end 'Entry'

typealias EntryArray = Array with Entry

type Bucket
	export var items as EntryArray

	static function create(items EntryArray) returns Self
		return Self{items: items}
	end 'create'
end 'Bucket'

function fill() returns Integer
	var b = Bucket.create(EntryArray.create())
	b.items.push(Entry.create(10))
	b.items.push(Entry.create(20))
	return b.items.count()
end 'fill'

function main() returns ExitCode
	return fill()
end 'main'
```
```exitcode
2
```

<!-- test: rc-return-struct-literal -->
Returning a struct literal directly from a function must transfer ownership at rc=1.
The callee constructs the struct (rc=0), increfs it for the assignment, and transfers
ownership to the caller via KeepVars. The caller must not incref again.
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
	export var a as Integer
	export var b as Integer

	static function create(a Integer, b Integer) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Pair'

function makePair(x Integer, y Integer) returns Pair
	return Pair.create(x, b: y)
end 'makePair'

function main() returns ExitCode
	let p = makePair(3, y: 7)
	return p.a + p.b
end 'main'
```
```exitcode
10
```

<!-- test: rc-return-struct-with-managed-field -->
Returning a struct whose field is a shared managed reference. The callee increfs
the shared field when storing it, and transfers the outer struct at rc=1.
The caller must decref the outer struct, which cascades to decref the managed field.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Wrapper
	export var inner as Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Wrapper'

function wrap(i Inner) returns Wrapper
	return Wrapper.create(i)
end 'wrap'

function main() returns ExitCode
	let i = Inner.create(5)
	let w = wrap(i)
	return w.inner.value
end 'main'
```
```exitcode
5
```

<!-- test: rc-list-scope-cleanup -->
List (struct owning a managed list field) must walk and decref managed list node values on scope exit.
```maxon
typealias StringList = List with String

function main() returns ExitCode
	var list = StringList.create()
	list.append("hello")
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: match-string-pattern-cleanup -->
Match pattern string literals must be freed after comparison, even when a case matches.
```maxon
function main() returns ExitCode
	let name = "alice"
	match name 'greet'
		"alice" then return 1
		"bob" then return 2
		default then return 0
	end 'greet'
end 'main'
```
```exitcode
1
```

<!-- test: rc-char-single-alloc-freed -->
Single character allocated and freed in the same function scope; Character + child __ManagedMemory both cleaned up.
```maxon
function main() returns ExitCode
	let c = 'A'
	return c.byteLength()
end 'main'
```
```exitcode
1
```

<!-- test: rc-char-alias-incref -->
Aliasing a character increfs it; both variables share the same Character object.
```maxon
function main() returns ExitCode
	let a = 'X'
	let b = a
	return a.byteLength() + b.byteLength()
end 'main'
```
```exitcode
2
```

<!-- test: rc-char-reassign-decrefs-old -->
Reassigning a character var decrefs and frees the old Character (with its managed child) before storing the new one.
```maxon
function main() returns ExitCode
	var c = 'A'
	c = 'B'
	return c.byteLength()
end 'main'
```
```exitcode
1
```

<!-- test: rc-char-return-transfers-ownership -->
Returning a character from a function transfers ownership to the caller.
```maxon
function makeChar() returns Character
	return 'Z'
end 'makeChar'

function main() returns ExitCode
	let c = makeChar()
	return c.byteLength()
end 'main'
```
```exitcode
1
```

<!-- test: rc-char-inner-block-freed -->
A character created in an inner if-block is freed when that block exits.
```maxon
function main() returns ExitCode
	var result = 0
	if true 'inner'
		let c = 'Q'
		result = c.byteLength()
	end 'inner'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: rc-tuple-primitive-freed -->
A tuple of primitives is heap-allocated and freed at scope exit.
```maxon
function main() returns ExitCode
	@heap let t = (10, 32)
	return t.0
end 'main'
```
```exitcode
10
```

<!-- test: rc-tuple-alias-incref -->
Aliasing a tuple increfs it; both variables share the same tuple object.
```maxon
function main() returns ExitCode
	@heap let a = (3, 7)
	let b = a
	return b.0 + b.1
end 'main'
```
```exitcode
10
```

<!-- test: rc-tuple-reassign-decrefs-old -->
Reassigning a tuple var decrefs the old tuple before storing the new one.
```maxon
function main() returns ExitCode
	var t = (1, 2)
	t = (3, 4)
	return t.0 + t.1
end 'main'
```
```exitcode
7
```

<!-- test: rc-tuple-with-string-freed -->
A tuple containing a managed type (String); the destructor must cascade to decref the String field.
```maxon
function main() returns ExitCode
	let t = (42, "hello")
	return t.0
end 'main'
```
```exitcode
42
```

<!-- test: rc-tuple-return-transfers-ownership -->
Returning a tuple from a function transfers ownership to the caller.
```maxon
typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	let t = makePair(5, b: 3)
	return t.0 + t.1
end 'main'
```
```exitcode
8
```

<!-- test: rc-tuple-destructuring-cleanup -->
Destructuring a tuple frees the tuple wrapper while the bindings remain live.
```maxon
function main() returns ExitCode
	let t = (10, 20)
	let (x, y) = t
	return x + y
end 'main'
```
```exitcode
30
```

<!-- test: rc-tuple-with-struct-freed -->
A tuple containing a user-defined struct; the destructor cascades through the tuple into the struct.
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
	let t = (1, Point.create(10, y: 20))
	return t.0
end 'main'
```
```exitcode
1
```

<!-- test: rc-struct-literal-as-function-arg -->
Passing a struct literal directly as a function argument must still free the struct after use. Currently leaks (exit 101).
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function acceptPoint(p Point) returns Integer
	return p.x + p.y
end 'acceptPoint'

function main() returns ExitCode
	return acceptPoint(Point.create(3, y: 4))
end 'main'
```
```exitcode
7
```

<!-- test: rc-tuple-return-destructure-no-crash -->
Returning a tuple from a function and destructuring it must not crash. Currently the cleanup code attempts to decref the already-freed tuple, causing a segfault.
```maxon
typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	let (x, y) = makePair(10, b: 32)
	return x + y
end 'main'
```
```exitcode
42
```

<!-- test: rc-enum-char-rawvalue-from-function -->
Returning an enum's char rawValue through a function must not underflow the refcount. Currently the returned value is treated as a managed allocation when it's actually a raw constant, causing refcount underflow.
```maxon
enum Grade
	excellent = 'A'
	good = 'B'
	average = 'C'
end 'Grade'

function getLetter(g Grade) returns Character
	return g.rawValue
end 'getLetter'

function main() returns ExitCode
	let grade = Grade.good
	let letter = getLetter(grade)
	if letter == 'B' 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: rc-enum-name-from-function -->
Returning an enum's .name (String) through a function must not underflow the refcount. Currently the returned raw constant string is decremented as if it were a managed allocation.
```maxon
enum Direction
	north
	south
	east
	west
end 'Direction'

function getName(d Direction) returns String
	return d.name
end 'getName'

function main() returns ExitCode
	let d = Direction.west
	let n = getName(d)
	if n == "west" 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: rc-enum-string-rawvalue-from-function -->
Returning a string-backed enum's rawValue through a function must not underflow the refcount. Same root cause as the char variant: raw constant treated as managed allocation.
```maxon
enum Planet
	earth = "Earth"
	mars = "Mars"
	venus = "Venus"
end 'Planet'

function getName(p Planet) returns String
	return p.rawValue
end 'getName'

function main() returns ExitCode
	let p = Planet.mars
	let n = getName(p)
	if n == "Mars" 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: rc-discarded-self-return -->
When a self-returning method's result is discarded, the refcount must remain balanced. Currently the cleanup code double-decrefs the struct, causing a segfault.
```maxon
typealias Count = int(i64.min to i64.max)

type Counter
	export var value as Count

	function increment() returns Counter
		value = value + 1
		return self
	end 'increment'

	static function create(value Count) returns Self
		return Self{value: value}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create(0)
	c.increment()
	return c.value
end 'main'
```
```exitcode
1
```

<!-- test: rc-borrow-field-from-param -->
Extracting and returning a struct field from a borrowed parameter must not crash. Currently the cleanup code decrefs the returned borrowed field incorrectly, causing a segfault after printing the correct output.
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
	return result.value
end 'main'
```
```exitcode
42
```

<!-- test: rc-char-to-string-interpolation -->
Interpolating a character into a string must not leak. Currently the intermediate ManagedMemory allocation from the Character is not freed.
```maxon
function main() returns ExitCode
	let c = 'A'
	let s = "{c}"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
A
```

<!-- test: rc-match-char-range-cleanup -->
Using character range patterns in a match statement must clean up all allocated Characters. Currently the range bound Characters leak.
```maxon
function main() returns ExitCode
	let c = 'G'
	match c 'classify'
		'a' to 'z' then return 1
		'A' to 'Z' then return 2
		'0' to '9' then return 3
		default then return 0
	end 'classify'
end 'main'
```
```exitcode
2
```

<!-- test: rc-string-backed-enum-compare -->
Comparing two string-backed enum values must not leak. Currently the Character/String allocations for enum case values are not freed.
```maxon
enum ContentType
	json = "application/json"
	html = "text/html"
	plain = "text/plain"
end 'ContentType'

function main() returns ExitCode
	let ct = ContentType.json
	if ct == ContentType.json 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: rc-char-backed-enum-compare -->
Comparing two char-backed enum values must not leak. Currently the Character allocations for enum case values are not freed.
```maxon
enum Escape
	newline = '\n'
	tab = '\t'
end 'Escape'

function main() returns ExitCode
	let e = Escape.newline
	if e == Escape.newline 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: rc-nested-struct-clone-no-leak -->
Cloning a struct with a nested struct field must not leak the inner clone. Currently the cloned Inner's refcount is 1 when freed via Outer cascade, leaving 1 leaked allocation.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Outer
	export var a as Inner
	export var b as Integer

	static function create(a Inner, b Integer) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let x = Outer.create(Inner.create(42), b: 10)
	var y = x.clone()
	y.a.value = 99
	return x.a.value
end 'main'
```
```exitcode
42
```

<!-- test: rc-string-clone-no-leak -->
Cloning a string must not leak internal Slice/ManagedMemory allocations. Currently String.clone leaks 2 allocations (the Slice and its buffer).
```maxon
function main() returns ExitCode
	let a = "hello"
	let b = a.clone()
	print(b)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: rc-string-replace-no-leak -->
String.replace must not leak internal working allocations. Currently leaks 2 allocations (ManagedMemory buffers from the replace implementation).
```maxon
function main() returns ExitCode
	let s = "hello world"
	let result = s.replace("world", with: "there")
	print(result)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello there
```

<!-- test: rc-string-replacefirst-no-leak -->
String.replaceFirst must not leak internal working allocations. The intermediate ManagedMemory and Buffer created during the replacement must be freed.
```maxon
function main() returns ExitCode
	let s = "hello world"
	let result = s.replaceFirst("o", with: "0")
	print(result)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hell0 world
```

<!-- test: rc-string-concat-loop-no-leak -->
Repeatedly appending strings in a loop must not leak memory. Each append grows the buffer in-place; any old buffer freed during reallocation must be properly cleaned up.
```maxon
function main() returns ExitCode
	var s = ""
	let a = "x"
	var i = 0
	while i < 5 'loop'
		s.append(a)
		i = i + 1
	end 'loop'
	return s.byteLength()
end 'main'
```
```exitcode
5
```

<!-- test: rc-string-slice-no-leak -->
String.slice must not leak internal allocations. The slice operation creates managed memory that must be properly tracked and freed.
```maxon
function main() returns ExitCode
	let s = "hello world"
	let start = s.startIndex()
	let spaceIdx = try s.findFirst(" ") otherwise s.endIndex()
	let sub = s.slice(start, endIndex: spaceIdx)
	print(sub)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: rc-enum-name-no-leak -->
Accessing enum .name must not leak. The getName function allocates a String wrapper around the name data; both the wrapper and its managed memory must be freed.
```maxon
enum Color
	Red
	Green
	Blue
end 'Color'

function main() returns ExitCode
	let c = Color.Green
	if c.name == "Green" 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: rc-enum-name-reassign-no-leak -->
Accessing enum .name after reassignment must not leak. Both the old and new enum name string allocations must be properly freed.
```maxon
enum Status
	pending
	active
	done
end 'Status'

function main() returns ExitCode
	var s = Status.pending
	s = Status.done
	if s.name == "done" 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: rc-array-of-structs-get-no-leak -->
Getting a struct from an array via try/otherwise must not leak. When the array is freed, its element destructors must decref all contained structs.
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
	export var first as Integer
	export var second as Integer

	static function create(first Integer, second Integer) returns Self
		return Self{first: first, second: second}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create(10, second: 20)
	let arr = [p]
	let elem = try arr.get(0) otherwise Pair.create(0, second: 0)
	return elem.first + elem.second
end 'main'
```
```exitcode
30
```

<!-- test: rc-array-of-structs-literal-no-leak -->
Creating an array literal of structs must not leak. All struct elements must be decreffed when the array is freed.
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
	let p1 = Point.create(1, y: 2)
	let p2 = Point.create(3, y: 4)
	let points = [p1, p2]
	let pt0 = try points.get(0) otherwise Point.create(0, y: 0)
	let pt1 = try points.get(1) otherwise Point.create(0, y: 0)
	return pt0.x + pt1.y
end 'main'
```
```exitcode
5
```

<!-- test: rc-global-array-push-local-no-leak -->
Pushing a local struct into a global array must not leak. When the global array is cleaned up, all elements (including those pushed from other function scopes) must be freed.
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
	return elem.value
end 'main'
```
```exitcode
123
```

<!-- test: rc-global-array-push-remove-loop-no-leak -->
Pushing many structs into a global array and then removing them all must not leak. Each removed element must be properly decreffed.
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
	while i < 10 'push'
		globalArr.push(Item.create(i))
		i = i + 1
	end 'push'
	var total = 0
	while globalArr.count() > 0 'remove'
		let elem = try globalArr.remove(0) otherwise 'err'
			return 99
		end 'err'
		total = total + elem.value
		i = i + 1
	end 'remove'
	return total
end 'main'
```
```exitcode
45
```

<!-- test: rc-struct-field-overwrite-in-if-no-leak -->
Assigning a new struct to a struct field inside an if block must decref the old value and not leak the old struct's managed children (e.g., arrays).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

export type Inner
		export var items as IntArray
		export var value as Integer

		static function create(items IntArray, value Integer) returns Self
			return Self{items: items, value: value}
		end 'create'
end 'Inner'

export type Outer
		export var inner as Inner
		export var initialized as bool

		static function create(inner Inner, initialized bool) returns Self
			return Self{inner: inner, initialized: initialized}
		end 'create'
end 'Outer'

function initOuter(o Outer)
		if not o.initialized 'init'
				o.inner = Inner.create(IntArray.create(), value: 42)
				o.initialized = true
		end 'init'
end 'initOuter'

function main() returns ExitCode
		var o = Outer.create(Inner.create(IntArray.create(), value: 0), initialized: false)
		initOuter(o)
		o.inner.items.push(1)
		o.inner.items.push(2)
		o.inner.items.push(3)
		return o.inner.items.count()
end 'main'
```
```exitcode
3
```

<!-- test: rc-map-string-keys-no-leak -->
A map with string keys must free all string key allocations when the map is destroyed. The string used as a key is increffed into the map's key array; when the map is freed, these strings must be decreffed.
```maxon
function main() returns ExitCode
		let m = ["hello": 42]
		return try m.get("hello") otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: rc-map-string-keys-multiple-no-leak -->
A map with multiple string keys must free all key and value allocations. Each insert increfs the key string; the map destructor must decref all of them.
```maxon
function main() returns ExitCode
		let m = ["a": 1, "b": 2, "c": 3]
		let a = try m.get("a") otherwise 0
		let b = try m.get("b") otherwise 0
		let c = try m.get("c") otherwise 0
		return a + b + c
end 'main'
```
```exitcode
6
```

<!-- test: rc-closure-capture-string-no-crash -->
A closure that captures a string variable must properly manage the string's refcount. The closure environment holds a reference to the string; when the environment is freed, it must decref the string without crashing.
```maxon
typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns String
function apply(f FnTypeAlias1, x Integer) returns String
	return f(x)
end 'apply'

function main() returns ExitCode
	let prefix = "hello"
	let result = apply(function(_ Integer) gives prefix, x: 0)
	print(result)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: rc-managed-list-remove-single-no-leak -->
Removing a node from a managed list must properly decref the node's value. The managed list node itself and the stored value must both be freed.
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
	return managedList.count()
end 'main'
```
```exitcode
0
```

<!-- test: rc-module-level-struct-nested-field-assign -->
Assigning to a nested field of a module-level struct variable must not leak. The struct access chain must properly manage refcounts for intermediate accesses.
```maxon
typealias SmallInt = int(0 to 255)

type Inner
		export var x as SmallInt

		static function create(x SmallInt) returns Self
			return Self{x: x}
		end 'create'
end 'Inner'

type Outer
		export var inner as Inner

		static function create(inner Inner) returns Self
			return Self{inner: inner}
		end 'create'
end 'Outer'

var state = Outer.create(Inner.create(0))

function main() returns ExitCode
		state.inner.x = 99
		return state.inner.x
end 'main'
```
```exitcode
99
```

<!-- test: rc-top-level-array-literal-no-leak -->
A module-level array literal must not leak. The array and its element storage must be freed during global cleanup.
```maxon
var items = [10, 20, 30]

function main() returns ExitCode
	let a = try items.get(0) otherwise 0
	let b = try items.get(1) otherwise 0
	let c = try items.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: rc-array-append-no-leak -->
Array.append must not leak. Appending one array to another must properly manage the element storage and not leak the source array's data.
```maxon
function main() returns ExitCode
	var a = [1, 2, 3]
	let b = [4, 5, 6]
	a.append(b)
	var sum = 0
	var i = 0
	while i < a.count() 'loop'
		sum = sum + (try a.get(i) otherwise 0)
		i = i + 1
	end 'loop'
	return sum
end 'main'
```
```exitcode
21
```

<!-- test: rc-struct-with-string-enum-in-array -->
Pushing structs that contain enums with string payloads into an array must not leak. The enum destructors must handle string payload cleanup during array destruction.
```maxon
export union QueryKey
		sourceFile(path String)
		allModule
end 'QueryKey'

export type Dependency
		export var key as QueryKey

		static function create(key QueryKey) returns Self
			return Self{key: key}
		end 'create'
end 'Dependency'

typealias DependencyArray = Array with Dependency

function main() returns ExitCode
		var deps = DependencyArray.create()
		deps.push(Dependency.create(QueryKey.sourceFile("test.maxon")))
		deps.push(Dependency.create(QueryKey.allModule))
		deps.push(Dependency.create(QueryKey.sourceFile("other.maxon")))
		return deps.count()
end 'main'
```
```exitcode
3
```

<!-- test: rc-custom-hashable-map-key-no-leak -->
A map using a custom Hashable struct as key must not leak. The map's internal arrays (keys, values, states) and all managed elements must be freed.
```maxon
typealias Integer = int(i64.min to i64.max)

type MyKey implements Hashable, Equatable
		var value as Integer

		function hash() returns HashValue
				return self.value * 31
		end 'hash'

		function equals(other MyKey) returns bool
				return self.value == other.value
		end 'equals'

		static function create(value Integer) returns Self
			return Self{value: value}
		end 'create'
end 'MyKey'

typealias MyKeyMap = Map with (MyKey, Integer)

function main() returns ExitCode
		var m = MyKeyMap.create()
		try m.insert(MyKey.create(1), value: 42) otherwise ignore
		return m.count()
end 'main'
```
```exitcode
1
```
