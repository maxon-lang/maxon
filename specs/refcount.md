---
feature: refcount
status: experimental
keywords: [refcount, memory, ownership, scope]
category: memory-safety
---

# Reference Counting

## Documentation

### Scope-Based Ownership with Reference Counting

Maxon uses reference counting to manage heap-allocated struct lifetimes. Every heap object has an associated reference count that tracks how many variables currently point to it. When the count reaches zero, the object is freed.

**Key rules:**

- **Construction** -- `var a = Point{x: 1, y: 2}` allocates on the heap with refcount 1.
- **Aliasing** -- `var b = a` increments the refcount (incref). Both `a` and `b` point to the same object.
- **Reassignment** -- `a = Point{x: 5, y: 6}` decrements the old object's refcount (decref) and sets `a` to the new object (refcount 1). If the old object's count reaches zero, it is freed.
- **Scope exit** -- When a variable goes out of scope, its refcount is decremented. This applies to block scopes (if, while, for), function scopes, and early exits (break, continue, return, throw).
- **Clone** -- `var b = a.clone()` creates an independent copy with its own refcount of 1. The original's count is unchanged.
- **Return** -- Returning a struct from a function transfers ownership to the caller without an extra incref/decref cycle.
- **Parameters** -- Struct parameters are incref'd on entry to the callee and decref'd when the callee's scope exits.

```text
var a = Point{x: 1, y: 2}   // rc(a) = 1
var b = a                    // rc(a) = 2 (b is alias)
a = Point{x: 5, y: 6}       // rc(old) = 1 (b still holds it), rc(new) = 1
// scope exit: rc(new) -> 0 (freed), rc(old) -> 0 (freed)
```

### Scope Cleanup Guarantees

Reference counts are decremented correctly on all scope exit paths:

- Normal block exit (end of if/while/for body)
- `break` and `continue` statements
- `return` from within nested blocks
- Error propagation via `try` (throwing path)

This ensures no leaks regardless of control flow.

### Nested and Transferred Ownership

When a struct is created in an inner scope and assigned to an outer variable, the outer variable's incref keeps the object alive past the inner scope's exit. The inner scope's decref reduces the count but does not free the object because the outer reference still holds it.

```text
var result = Box{value: 0}
if true 'blk'
    var inner = Box{value: 42}
    result = inner             // rc(inner) = 2
end 'blk'                     // inner goes out of scope, rc = 1
// result still valid with value 42
```

## Tests

<!-- test: basic-aliasing -->
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
		let a = Point.create(x: 10, y: 20)
		let b = a
		return a.x + b.y
end 'main'
```
```exitcode
30
```

<!-- test: alias-survives-inner-scope -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Box
		export var value Integer

		static function create(value Integer) returns Self
			return Self{value: value}
		end 'create'
end 'Box'
function main() returns ExitCode
		var result = Box.create(value: 0)
		if true 'blk'
				let inner = Box.create(value: 42)
				result = inner
		end 'blk'
		return result.value
end 'main'
```
```exitcode
42
```

<!-- test: reassignment-decrefs-old -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Item
		export var val Integer

		static function create(val Integer) returns Self
			return Self{val: val}
		end 'create'
end 'Item'
function main() returns ExitCode
		var a = Item.create(val: 10)
		a = Item.create(val: 20)
		return a.val
end 'main'
```
```exitcode
20
```

<!-- test: multiple-aliases -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Data
		export var n Integer

		static function create(n Integer) returns Self
			return Self{n: n}
		end 'create'
end 'Data'
function main() returns ExitCode
		let a = Data.create(n: 7)
		let b = a
		let c = a
		return a.n + b.n + c.n
end 'main'
```
```exitcode
21
```

<!-- test: clone-independent-refcount -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Pair
		export var a Integer
		export var b Integer

		static function create(a Integer, b Integer) returns Self
			return Self{a: a, b: b}
		end 'create'
end 'Pair'
function main() returns ExitCode
		var x = Pair.create(a: 3, b: 4)
		let y = x.clone()
		x = Pair.create(a: 0, b: 0)
		return y.a + y.b
end 'main'
```
```exitcode
7
```

<!-- test: struct-field-store -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Inner
		export var val Integer

		static function create(val Integer) returns Self
			return Self{val: val}
		end 'create'
end 'Inner'
type Outer
		export var child Inner
		export function setChild(c Inner)
				child = c
		end 'setChild'

		static function create(child Inner) returns Self
			return Self{child: child}
		end 'create'
end 'Outer'
function main() returns ExitCode
		let o = Outer.create(child: Inner.create(val: 0))
		let i = Inner.create(val: 55)
		o.setChild(i)
		return o.child.val
end 'main'
```
```exitcode
55
```

<!-- test: return-value-ownership -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Result
		export var code Integer

		static function create(code Integer) returns Self
			return Self{code: code}
		end 'create'
end 'Result'
function makeResult(n Integer) returns Result
		let r = Result.create(code: n)
		return r
end 'makeResult'
function main() returns ExitCode
		let r = makeResult(33)
		return r.code
end 'main'
```
```exitcode
33
```

<!-- test: array-push-struct -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Entry
		export var id Integer

		static function create(id Integer) returns Self
			return Self{id: id}
		end 'create'
end 'Entry'
typealias EntryArray = Array with Entry
function main() returns ExitCode
		let arr = EntryArray.create()
		let e = Entry.create(id: 15)
		arr.push(e)
		let got = try arr.get(0) otherwise Entry.create(id: 0)
		return got.id
end 'main'
```
```exitcode
15
```

<!-- test: break-cleans-refs -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Counter
		export var val Integer

		static function create(val Integer) returns Self
			return Self{val: val}
		end 'create'
end 'Counter'
function main() returns ExitCode
		var total = 0
		var i = 0
		while i < 5 'loop'
				let c = Counter.create(val: i)
				if i == 3 'brk'
						total = total + c.val
						break
				end 'brk'
				total = total + c.val
				i = i + 1
		end 'loop'
		return total
end 'main'
```
```exitcode
6
```

<!-- test: error-propagation-cleans-refs -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Wrapper
		export var n Integer

		static function create(n Integer) returns Self
			return Self{n: n}
		end 'create'
end 'Wrapper'
typealias WrapperArray = Array with Wrapper
function getFirst(arr WrapperArray) returns Wrapper throws ArrayError
		let result = try arr.get(0)
		return result
end 'getFirst'
function main() returns ExitCode
		let arr = WrapperArray.create()
		arr.push(Wrapper.create(n: 99))
		let w = try getFirst(arr) otherwise Wrapper.create(n: 0)
		return w.n
end 'main'
```
```exitcode
99
```

<!-- test: decref-reclaims-same-scope -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Val
		export var n Integer

		static function create(n Integer) returns Self
			return Self{n: n}
		end 'create'
end 'Val'
function main() returns ExitCode
		var a = Val.create(n: 10)
		let b = a
		a = Val.create(n: 20)
		return a.n + b.n
end 'main'
```
```exitcode
30
```

<!-- test: decref-drops-last-ref -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Tag
		export var id Integer

		static function create(id Integer) returns Self
			return Self{id: id}
		end 'create'
end 'Tag'
function main() returns ExitCode
		var a = Tag.create(id: 1)
		a = Tag.create(id: 2)
		a = Tag.create(id: 3)
		return a.id
end 'main'
```
```exitcode
3
```

<!-- test: decref-reclaims-inner-scope -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Cell
		export var value Integer

		static function create(value Integer) returns Self
			return Self{value: value}
		end 'create'
end 'Cell'
function main() returns ExitCode
		var a = Cell.create(value: 10)
		var result = 0
		if true 'inner'
				let b = a
				a = Cell.create(value: 20)
				result = b.value
		end 'inner'
		return result + a.value
end 'main'
```
```exitcode
30
```

<!-- test: ownership-transfer-to-outer -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Token
		export var kind Integer

		static function create(kind Integer) returns Self
			return Self{kind: kind}
		end 'create'
end 'Token'
function main() returns ExitCode
		var t = Token.create(kind: 0)
		if true 'a'
				let inner = Token.create(kind: 50)
				t = inner
		end 'a'
		return t.kind
end 'main'
```
```exitcode
50
```

<!-- test: try-otherwise-struct -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Fallback
		export var n Integer

		static function create(n Integer) returns Self
			return Self{n: n}
		end 'create'
end 'Fallback'
typealias FallbackArray = Array with Fallback
function main() returns ExitCode
		let arr = FallbackArray.create()
		arr.push(Fallback.create(n: 10))
		let a = try arr.get(0) otherwise Fallback.create(n: 99)
		let b = try arr.get(5) otherwise Fallback.create(n: 42)
		return a.n + b.n
end 'main'
```
```exitcode
52
```

<!-- test: for-in-struct-array -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Score
		export var points Integer

		static function create(points Integer) returns Self
			return Self{points: points}
		end 'create'
end 'Score'
typealias ScoreArray = Array with Score
function main() returns ExitCode
		let scores = ScoreArray.create()
		scores.push(Score.create(points: 10))
		scores.push(Score.create(points: 20))
		scores.push(Score.create(points: 30))
		var total = 0
		for s in scores 'loop'
				total = total + s.points
		end 'loop'
		return total
end 'main'
```
```exitcode
60
```

<!-- test: nested-call-return -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Num
		export var v Integer

		static function create(v Integer) returns Self
			return Self{v: v}
		end 'create'
end 'Num'
function makeNum(n Integer) returns Num
		return Num.create(v: n)
end 'makeNum'
function addOne(x Num) returns Num
		return Num.create(v: x.v + 1)
end 'addOne'
function main() returns ExitCode
		let result = addOne(makeNum(10))
		return result.v
end 'main'
```
```exitcode
11
```

<!-- test: function-param-incref -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Config
		export var level Integer

		static function create(level Integer) returns Self
			return Self{level: level}
		end 'create'
end 'Config'
function readLevel(c Config) returns Integer
		return c.level
end 'readLevel'
function main() returns ExitCode
		let cfg = Config.create(level: 77)
		let l = readLevel(cfg)
		return l + cfg.level - 77
end 'main'
```
```exitcode
77
```

<!-- test: return-move-nested-scopes -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Item
		export var name String
		export var id Integer

		static function create(name String, id Integer) returns Self
			return Self{name: name, id: id}
		end 'create'
end 'Item'

function findItem(a Integer, b Integer, target Integer) returns Item
		if a == target 'check_a'
				let result = Item.create(name: "first", id: a)
				return result
		end 'check_a'
		if b == target 'check_b'
				if true 'inner'
						let result = Item.create(name: "second", id: b)
						return result
				end 'inner'
		end 'check_b'
		return Item.create(name: "default", id: 0)
end 'findItem'

function main() returns ExitCode
		let item = findItem(10, b: 20, target: 20)
		print("{item.name}\n")
		return item.id
end 'main'
```
```exitcode
20
```
```stdout
second
```

<!-- test: field-access-reference -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Inner
		export var val Integer

		static function create(val Integer) returns Self
			return Self{val: val}
		end 'create'
end 'Inner'
type Outer
		export var child Inner

		static function create(child Inner) returns Self
			return Self{child: child}
		end 'create'
end 'Outer'
function readChild(o Outer) returns Integer
		let c = o.child
		return c.val
end 'readChild'
function main() returns ExitCode
		let o = Outer.create(child: Inner.create(val: 88))
		let result = readChild(o)
		return result
end 'main'
```
```exitcode
88
```
