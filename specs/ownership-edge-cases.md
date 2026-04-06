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
<!-- MmTrace -->
Single struct allocated and freed in the same function scope.
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
	@heap let p = Point.create(x: 1, y: 2)
	return p.x
end 'main'
```
```exitcode
1
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Point #1 size=16 [Point.create]
  sl_alloc Point #1 size=48 class=4
mm_incref Point #1 rc=1 [Point.create]
mm_transfer Point #1 rc=1 [Point.create]
mm_decref Point #1 rc=0 [main]
  mm_free Point #1
    sl_free Point #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-alias-incref -->
<!-- MmTrace -->
Aliasing a struct increfs it; both variables share refcount and object is freed once.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	@heap let a = Box.create(value: 42)
	let b = a
	return b.value
end 'main'
```
```exitcode
42
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Box #1 size=8 [Box.create]
  sl_alloc Box #1 size=40 class=4
mm_incref Box #1 rc=1 [Box.create]
mm_transfer Box #1 rc=1 [Box.create]
mm_incref Box #1 rc=2 [main]
mm_decref Box #1 rc=1 [main]
mm_decref Box #1 rc=0 [main]
  mm_free Box #1
    sl_free Box #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-reassign-decrefs-old -->
<!-- MmTrace -->
Reassigning a var decrefs the old object immediately; the old object must be freed before scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Tag
	export var id Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Tag'

function main() returns ExitCode
	var t = Tag.create(id: 1)
	t = Tag.create(id: 2)
	t = Tag.create(id: 3)
	return t.id
end 'main'
```
```exitcode
3
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Tag #1 size=8 [Tag.create]
  sl_alloc Tag #1 size=40 class=4
mm_incref Tag #1 rc=1 [Tag.create]
mm_transfer Tag #1 rc=1 [Tag.create]
mm_alloc Tag #2 size=8 [Tag.create]
  sl_alloc Tag #2 size=40 class=4
mm_incref Tag #2 rc=1 [Tag.create]
mm_transfer Tag #2 rc=1 [Tag.create]
mm_decref Tag #1 rc=0 [main]
  mm_free Tag #1
    sl_free Tag #1 size=48 class=4
mm_alloc Tag #3 size=8 [Tag.create]
  sl_alloc Tag #3 size=40 class=4
mm_incref Tag #3 rc=1 [Tag.create]
mm_transfer Tag #3 rc=1 [Tag.create]
mm_decref Tag #2 rc=0 [main]
  mm_free Tag #2
    sl_free Tag #2 size=48 class=4
mm_decref Tag #3 rc=0 [main]
  mm_free Tag #3
    sl_free Tag #3 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-inner-block-freed -->
<!-- MmTrace -->
Struct created in an inner if-block is freed when that block exits, before the outer block ends.
```maxon
typealias Integer = int(i64.min to i64.max)

type Widget
	export var id Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Widget'

function main() returns ExitCode
	var result = 0
	if true 'inner'
		@heap let w = Widget.create(id: 7)
		result = w.id
	end 'inner'
	return result
end 'main'
```
```exitcode
7
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Widget #1 size=8 [Widget.create]
  sl_alloc Widget #1 size=40 class=4
mm_incref Widget #1 rc=1 [Widget.create]
mm_transfer Widget #1 rc=1 [Widget.create]
mm_decref Widget #1 rc=0 [main]
  mm_free Widget #1
    sl_free Widget #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-return-transfers-ownership -->
<!-- MmTrace -->
Returning a struct skips its decref; caller receives ownership and frees it at its own scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
	export var kind Integer

	static function create(kind Integer) returns Self
		return Self{kind: kind}
	end 'create'
end 'Token'

function makeToken(k Integer) returns Token
	let t = Token.create(kind: k)
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Token #1 size=8 [Token.create]
  sl_alloc Token #1 size=40 class=4
mm_incref Token #1 rc=1 [Token.create]
mm_transfer Token #1 rc=1 [Token.create]
mm_transfer Token #1 rc=1 [ownership-edge-cases.makeToken]
mm_decref Token #1 rc=0 [main]
  mm_free Token #1
    sl_free Token #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-alias-survives-reassign -->
<!-- MmTrace -->
Aliased reference keeps object alive when the original var is reassigned.
```maxon
typealias Integer = int(i64.min to i64.max)

type Num
	export var v Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Num'

function main() returns ExitCode
	var a = Num.create(v: 10)
	let b = a
	a = Num.create(v: 20)
	return b.v + a.v
end 'main'
```
```exitcode
30
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Num #1 size=8 [Num.create]
  sl_alloc Num #1 size=40 class=4
mm_incref Num #1 rc=1 [Num.create]
mm_transfer Num #1 rc=1 [Num.create]
mm_incref Num #1 rc=2 [main]
mm_alloc Num #2 size=8 [Num.create]
  sl_alloc Num #2 size=40 class=4
mm_incref Num #2 rc=1 [Num.create]
mm_transfer Num #2 rc=1 [Num.create]
mm_decref Num #1 rc=1 [main]
mm_decref Num #2 rc=0 [main]
  mm_free Num #2
    sl_free Num #2 size=48 class=4
mm_decref Num #1 rc=0 [main]
  mm_free Num #1
    sl_free Num #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-loop-per-iteration-freed -->
<!-- MmTrace -->
A struct allocated each loop iteration is freed at loop-block exit before the next iteration.
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 4 'loop'
		@heap let c = Counter.create(n: i)
		total = total + c.n
		i = i + 1
	end 'loop'
	return total
end 'main'
```
```exitcode
6
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Counter #1 size=8 [Counter.create]
  sl_alloc Counter #1 size=40 class=4
mm_incref Counter #1 rc=1 [Counter.create]
mm_transfer Counter #1 rc=1 [Counter.create]
mm_decref Counter #1 rc=0 [main]
  mm_free Counter #1
    sl_free Counter #1 size=48 class=4
mm_alloc Counter #2 size=8 [Counter.create]
  sl_alloc Counter #2 size=40 class=4
mm_incref Counter #2 rc=1 [Counter.create]
mm_transfer Counter #2 rc=1 [Counter.create]
mm_decref Counter #2 rc=0 [main]
  mm_free Counter #2
    sl_free Counter #2 size=48 class=4
mm_alloc Counter #3 size=8 [Counter.create]
  sl_alloc Counter #3 size=40 class=4
mm_incref Counter #3 rc=1 [Counter.create]
mm_transfer Counter #3 rc=1 [Counter.create]
mm_decref Counter #3 rc=0 [main]
  mm_free Counter #3
    sl_free Counter #3 size=48 class=4
mm_alloc Counter #4 size=8 [Counter.create]
  sl_alloc Counter #4 size=40 class=4
mm_incref Counter #4 rc=1 [Counter.create]
mm_transfer Counter #4 rc=1 [Counter.create]
mm_decref Counter #4 rc=0 [main]
  mm_free Counter #4
    sl_free Counter #4 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-break-frees-before-exit -->
<!-- MmTrace -->
Struct allocated before a break is decref'd before the loop block is exited.
```maxon
typealias Integer = int(i64.min to i64.max)

type Step
	export var val Integer

	static function create(val Integer) returns Self
		return Self{val: val}
	end 'create'
end 'Step'

function main() returns ExitCode
	var i = 0
	while i < 10 'loop'
		@heap let s = Step.create(val: i)
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Step #1 size=8 [Step.create]
  sl_alloc Step #1 size=40 class=4
mm_incref Step #1 rc=1 [Step.create]
mm_transfer Step #1 rc=1 [Step.create]
mm_decref Step #1 rc=0 [main]
  mm_free Step #1
    sl_free Step #1 size=48 class=4
mm_alloc Step #2 size=8 [Step.create]
  sl_alloc Step #2 size=40 class=4
mm_incref Step #2 rc=1 [Step.create]
mm_transfer Step #2 rc=1 [Step.create]
mm_decref Step #2 rc=0 [main]
  mm_free Step #2
    sl_free Step #2 size=48 class=4
mm_alloc Step #3 size=8 [Step.create]
  sl_alloc Step #3 size=40 class=4
mm_incref Step #3 rc=1 [Step.create]
mm_transfer Step #3 rc=1 [Step.create]
mm_decref Step #3 rc=0 [main]
  mm_free Step #3
    sl_free Step #3 size=48 class=4
mm_alloc Step #4 size=8 [Step.create]
  sl_alloc Step #4 size=40 class=4
mm_incref Step #4 rc=1 [Step.create]
mm_transfer Step #4 rc=1 [Step.create]
mm_decref Step #4 rc=0 [main]
  mm_free Step #4
    sl_free Step #4 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-continue-frees-before-restart -->
<!-- MmTrace -->
Struct allocated before a continue is decref'd before the loop restarts.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var v Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Item'

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 5 'loop'
		@heap let item = Item.create(v: i)
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Item #1 size=8 [Item.create]
  sl_alloc Item #1 size=40 class=4
mm_incref Item #1 rc=1 [Item.create]
mm_transfer Item #1 rc=1 [Item.create]
mm_decref Item #1 rc=0 [main]
  mm_free Item #1
    sl_free Item #1 size=48 class=4
mm_alloc Item #2 size=8 [Item.create]
  sl_alloc Item #2 size=40 class=4
mm_incref Item #2 rc=1 [Item.create]
mm_transfer Item #2 rc=1 [Item.create]
mm_decref Item #2 rc=0 [main]
  mm_free Item #2
    sl_free Item #2 size=48 class=4
mm_alloc Item #3 size=8 [Item.create]
  sl_alloc Item #3 size=40 class=4
mm_incref Item #3 rc=1 [Item.create]
mm_transfer Item #3 rc=1 [Item.create]
mm_decref Item #3 rc=0 [main]
  mm_free Item #3
    sl_free Item #3 size=48 class=4
mm_alloc Item #4 size=8 [Item.create]
  sl_alloc Item #4 size=40 class=4
mm_incref Item #4 rc=1 [Item.create]
mm_transfer Item #4 rc=1 [Item.create]
mm_decref Item #4 rc=0 [main]
  mm_free Item #4
    sl_free Item #4 size=48 class=4
mm_alloc Item #5 size=8 [Item.create]
  sl_alloc Item #5 size=40 class=4
mm_incref Item #5 rc=1 [Item.create]
mm_transfer Item #5 rc=1 [Item.create]
mm_decref Item #5 rc=0 [main]
  mm_free Item #5
    sl_free Item #5 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-nested-struct-field-incref -->
<!-- MmTrace -->
When a struct literal is assigned as a field, the field value is incref'd; both outer and inner are freed correctly.
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

function main() returns ExitCode
	let inner = Inner.create(val: 55)
	let outer = Outer.create(child: inner)
	return outer.child.val
end 'main'
```
```exitcode
55
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Inner #1 size=8 [Inner.create]
  sl_alloc Inner #1 size=40 class=4
mm_incref Inner #1 rc=1 [Inner.create]
mm_transfer Inner #1 rc=1 [Inner.create]
mm_alloc Outer #2 size=8 [Outer.create]
  sl_alloc Outer #2 size=40 class=4
mm_incref Inner #1 rc=2 [Outer.create]
mm_incref Outer #2 rc=1 [Outer.create]
mm_transfer Outer #2 rc=1 [Outer.create]
mm_decref Outer #2 rc=0 [main]
  mm_decref Inner #1 rc=1 [~Outer]
  mm_free Outer #2
    sl_free Outer #2 size=48 class=4
mm_decref Inner #1 rc=0 [main]
  mm_free Inner #1
    sl_free Inner #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-nested-struct-deep-freed -->
<!-- MmTrace -->
Three-level nested struct: all three levels are freed when the outermost var leaves scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type A
	export var n Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'A'

type B
	export var a A

	static function create(a A) returns Self
		return Self{a: a}
	end 'create'
end 'B'

type C
	export var b B

	static function create(b B) returns Self
		return Self{b: b}
	end 'create'
end 'C'

function main() returns ExitCode
	let c = C.create(b: B.create(a: A.create(n: 7)))
	return c.b.a.n
end 'main'
```
```exitcode
7
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc A #1 size=8 [A.create]
  sl_alloc A #1 size=40 class=4
mm_incref A #1 rc=1 [A.create]
mm_transfer A #1 rc=1 [A.create]
mm_alloc B #2 size=8 [B.create]
  sl_alloc B #2 size=40 class=4
mm_incref A #1 rc=2 [B.create]
mm_incref B #2 rc=1 [B.create]
mm_transfer B #2 rc=1 [B.create]
mm_alloc C #3 size=8 [C.create]
  sl_alloc C #3 size=40 class=4
mm_incref B #2 rc=2 [C.create]
mm_incref C #3 rc=1 [C.create]
mm_transfer C #3 rc=1 [C.create]
mm_decref B #2 rc=1 [main]
mm_decref A #1 rc=1 [main]
mm_decref C #3 rc=0 [main]
  mm_decref B #2 rc=0 [~C]
    mm_decref A #1 rc=0 [~B]
      mm_free A #1
        sl_free A #1 size=48 class=4
    mm_free B #2
      sl_free B #2 size=48 class=4
  mm_free C #3
    sl_free C #3 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-field-overwrite-decrefs-old -->
<!-- MmTrace -->
Overwriting a struct field via a method decrefs the old field value and increfs the new one.
```maxon
typealias Integer = int(i64.min to i64.max)

type Payload
	export var data Integer

	static function create(data Integer) returns Self
		return Self{data: data}
	end 'create'
end 'Payload'

type Container
	export var payload Payload

	export function setPayload(p Payload)
		payload = p
	end 'setPayload'

	static function create(payload Payload) returns Self
		return Self{payload: payload}
	end 'create'
end 'Container'

function main() returns ExitCode
	let old = Payload.create(data: 1)
	let c = Container.create(payload: old)
	c.setPayload(Payload.create(data: 2))
	return c.payload.data
end 'main'
```
```exitcode
2
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Payload #1 size=8 [Payload.create]
  sl_alloc Payload #1 size=40 class=4
mm_incref Payload #1 rc=1 [Payload.create]
mm_transfer Payload #1 rc=1 [Payload.create]
mm_alloc Container #2 size=8 [Container.create]
  sl_alloc Container #2 size=40 class=4
mm_incref Payload #1 rc=2 [Container.create]
mm_incref Container #2 rc=1 [Container.create]
mm_transfer Container #2 rc=1 [Container.create]
mm_alloc Payload #3 size=8 [Payload.create]
  sl_alloc Payload #3 size=40 class=4
mm_incref Payload #3 rc=1 [Payload.create]
mm_transfer Payload #3 rc=1 [Payload.create]
mm_decref Payload #1 rc=1 [Container.setPayload]
mm_incref Payload #3 rc=2 [Container.setPayload]
mm_decref Payload #3 rc=1 [main]
mm_decref Container #2 rc=0 [main]
  mm_decref Payload #3 rc=0 [~Container]
    mm_free Payload #3
      sl_free Payload #3 size=48 class=4
  mm_free Container #2
    sl_free Container #2 size=48 class=4
mm_decref Payload #1 rc=0 [main]
  mm_free Payload #1
    sl_free Payload #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-field-overwrite-managed-list -->
<!-- MmTrace -->
Overwriting a struct field three times; each old value must be freed promptly.
```maxon
typealias Integer = int(i64.min to i64.max)

type Val
	export var n Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Val'

type Holder
	export var v Val

	export function set(newV Val)
		v = newV
	end 'set'

	static function create(v Val) returns Self
		return Self{v: v}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(v: Val.create(n: 0))
	h.set(Val.create(n: 10))
	h.set(Val.create(n: 20))
	h.set(Val.create(n: 30))
	return h.v.n
end 'main'
```
```exitcode
30
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Val #1 size=8 [Val.create]
  sl_alloc Val #1 size=40 class=4
mm_incref Val #1 rc=1 [Val.create]
mm_transfer Val #1 rc=1 [Val.create]
mm_alloc Holder #2 size=8 [Holder.create]
  sl_alloc Holder #2 size=40 class=4
mm_incref Val #1 rc=2 [Holder.create]
mm_incref Holder #2 rc=1 [Holder.create]
mm_transfer Holder #2 rc=1 [Holder.create]
mm_alloc Val #3 size=8 [Val.create]
  sl_alloc Val #3 size=40 class=4
mm_incref Val #3 rc=1 [Val.create]
mm_transfer Val #3 rc=1 [Val.create]
mm_decref Val #1 rc=1 [Holder.set]
mm_incref Val #3 rc=2 [Holder.set]
mm_alloc Val #4 size=8 [Val.create]
  sl_alloc Val #4 size=40 class=4
mm_incref Val #4 rc=1 [Val.create]
mm_transfer Val #4 rc=1 [Val.create]
mm_decref Val #3 rc=1 [Holder.set]
mm_incref Val #4 rc=2 [Holder.set]
mm_alloc Val #5 size=8 [Val.create]
  sl_alloc Val #5 size=40 class=4
mm_incref Val #5 rc=1 [Val.create]
mm_transfer Val #5 rc=1 [Val.create]
mm_decref Val #4 rc=1 [Holder.set]
mm_incref Val #5 rc=2 [Holder.set]
mm_decref Val #5 rc=1 [main]
mm_decref Val #4 rc=0 [main]
  mm_free Val #4
    sl_free Val #4 size=48 class=4
mm_decref Val #3 rc=0 [main]
  mm_free Val #3
    sl_free Val #3 size=48 class=4
mm_decref Val #1 rc=0 [main]
  mm_free Val #1
    sl_free Val #1 size=48 class=4
mm_decref Holder #2 rc=0 [main]
  mm_decref Val #5 rc=0 [~Holder]
    mm_free Val #5
      sl_free Val #5 size=48 class=4
  mm_free Holder #2
    sl_free Holder #2 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-container-push-incref -->
<!-- MmTrace -->
Pushing a struct into an array increfs it; after the local var leaves scope the element still lives.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
	export var id Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Node'

typealias NodeArray = Array with Node

function main() returns ExitCode
	let arr = NodeArray.create()
	if true 'scope'
		let n = Node.create(id: 10)
		arr.push(n)
	end 'scope'
	let got = try arr.get(0) otherwise Node.create(id: -1)
	return got.id
end 'main'
```
```exitcode
10
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Node #1 size=32 [NodeArray.create]
  sl_alloc __ManagedMemory_Node #1 size=64 class=5
mm_alloc NodeArray #2 size=16 [NodeArray.create]
  sl_alloc NodeArray #2 size=48 class=4
mm_incref __ManagedMemory_Node #1 rc=1 [NodeArray.create]
mm_incref NodeArray #2 rc=1 [NodeArray.create]
mm_transfer NodeArray #2 rc=1 [NodeArray.create]
mm_alloc Node #3 size=8 [Node.create]
  sl_alloc Node #3 size=40 class=4
mm_incref Node #3 rc=1 [Node.create]
mm_transfer Node #3 rc=1 [Node.create]
mm_realloc __ManagedMemory_Node #1 size=32
  mm_raw_alloc #R1 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref Node #3 rc=2 [NodeArray.push]
mm_decref Node #3 rc=1 [main]
mm_incref Node #3 rc=2 [NodeArray.get]
mm_incref Node #3 rc=3 [main]
mm_decref Node #3 rc=2 [main]
mm_decref Node #3 rc=1 [main]
mm_decref NodeArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Node #1 rc=0 [~NodeArray]
    mm_decref Node #3 rc=0 [~ManagedElements]
      mm_free Node #3
        sl_free Node #3 size=48 class=4
    mm_raw_free #R1
      sl_free size=32 class=3
    mm_free __ManagedMemory_Node #1
      sl_free __ManagedMemory_Node #1 size=64 class=5
  mm_free NodeArray #2
    sl_free NodeArray #2 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
```

<!-- test: rc-container-pop-decrefs -->
<!-- MmTrace -->
Popping the last element and discarding the result frees the element at scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
	export var id Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Node'

typealias NodeArray = Array with Node

function main() returns ExitCode
	let arr = NodeArray.create()
	arr.push(Node.create(id: 1))
	arr.push(Node.create(id: 2))
	let popped = try arr.remove(arr.count() - 1) otherwise 'err'
		return 99
	end 'err'
	return arr.count() + popped.id - popped.id
end 'main'
```
```exitcode
1
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Node #1 size=32 [NodeArray.create]
  sl_alloc __ManagedMemory_Node #1 size=64 class=5
mm_alloc NodeArray #2 size=16 [NodeArray.create]
  sl_alloc NodeArray #2 size=48 class=4
mm_incref __ManagedMemory_Node #1 rc=1 [NodeArray.create]
mm_incref NodeArray #2 rc=1 [NodeArray.create]
mm_transfer NodeArray #2 rc=1 [NodeArray.create]
mm_alloc Node #3 size=8 [Node.create]
  sl_alloc Node #3 size=40 class=4
mm_incref Node #3 rc=1 [Node.create]
mm_transfer Node #3 rc=1 [Node.create]
mm_realloc __ManagedMemory_Node #1 size=32
  mm_raw_alloc #R1 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref Node #3 rc=2 [NodeArray.push]
mm_alloc Node #4 size=8 [Node.create]
  sl_alloc Node #4 size=40 class=4
mm_incref Node #4 rc=1 [Node.create]
mm_transfer Node #4 rc=1 [Node.create]
mm_incref Node #4 rc=2 [NodeArray.push]
mm_incref Node #4 rc=3 [main]
mm_decref Node #4 rc=2 [main]
mm_decref Node #4 rc=1 [main]
mm_decref Node #3 rc=1 [main]
mm_decref Node #4 rc=0 [main]
  mm_free Node #4
    sl_free Node #4 size=48 class=4
mm_decref NodeArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Node #1 rc=0 [~NodeArray]
    mm_decref Node #3 rc=0 [~ManagedElements]
      mm_free Node #3
        sl_free Node #3 size=48 class=4
    mm_raw_free #R1
      sl_free size=32 class=3
    mm_free __ManagedMemory_Node #1
      sl_free __ManagedMemory_Node #1 size=64 class=5
  mm_free NodeArray #2
    sl_free NodeArray #2 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
```

<!-- test: rc-container-overwrite-decrefs-old -->
<!-- MmTrace -->
Setting an element at an existing index must decref the old element and incref the new one.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
	let arr = ItemArray.create()
	arr.push(Item.create(value: 100))
	arr.set(0, value: Item.create(value: 200))
	let got = try arr.get(0) otherwise Item.create(value: -1)
	return got.value
end 'main'
```
```exitcode
200
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Item #1 size=32 [ItemArray.create]
  sl_alloc __ManagedMemory_Item #1 size=64 class=5
mm_alloc ItemArray #2 size=16 [ItemArray.create]
  sl_alloc ItemArray #2 size=48 class=4
mm_incref __ManagedMemory_Item #1 rc=1 [ItemArray.create]
mm_incref ItemArray #2 rc=1 [ItemArray.create]
mm_transfer ItemArray #2 rc=1 [ItemArray.create]
mm_alloc Item #3 size=8 [Item.create]
  sl_alloc Item #3 size=40 class=4
mm_incref Item #3 rc=1 [Item.create]
mm_transfer Item #3 rc=1 [Item.create]
mm_realloc __ManagedMemory_Item #1 size=32
  mm_raw_alloc #R1 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref Item #3 rc=2 [ItemArray.push]
mm_alloc Item #4 size=8 [Item.create]
  sl_alloc Item #4 size=40 class=4
mm_incref Item #4 rc=1 [Item.create]
mm_transfer Item #4 rc=1 [Item.create]
mm_decref Item #3 rc=1 [ItemArray.set]
mm_incref Item #4 rc=2 [ItemArray.set]
mm_incref Item #4 rc=3 [ItemArray.get]
mm_incref Item #4 rc=4 [main]
mm_decref Item #4 rc=3 [main]
mm_decref Item #4 rc=2 [main]
mm_decref Item #3 rc=0 [main]
  mm_free Item #3
    sl_free Item #3 size=48 class=4
mm_decref Item #4 rc=1 [main]
mm_decref ItemArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Item #1 rc=0 [~ItemArray]
    mm_decref Item #4 rc=0 [~ManagedElements]
      mm_free Item #4
        sl_free Item #4 size=48 class=4
    mm_raw_free #R1
      sl_free size=32 class=3
    mm_free __ManagedMemory_Item #1
      sl_free __ManagedMemory_Item #1 size=64 class=5
  mm_free ItemArray #2
    sl_free ItemArray #2 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
```

<!-- test: rc-container-clear-decrefs-all -->
<!-- MmTrace -->
Clearing an array decrefs every element; all elements freed when rc hits 0.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
	let arr = ItemArray.create()
	arr.push(Item.create(value: 1))
	arr.push(Item.create(value: 2))
	arr.push(Item.create(value: 3))
	arr.clear()
	return arr.count()
end 'main'
```
```exitcode
0
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Item #1 size=32 [ItemArray.create]
  sl_alloc __ManagedMemory_Item #1 size=64 class=5
mm_alloc ItemArray #2 size=16 [ItemArray.create]
  sl_alloc ItemArray #2 size=48 class=4
mm_incref __ManagedMemory_Item #1 rc=1 [ItemArray.create]
mm_incref ItemArray #2 rc=1 [ItemArray.create]
mm_transfer ItemArray #2 rc=1 [ItemArray.create]
mm_alloc Item #3 size=8 [Item.create]
  sl_alloc Item #3 size=40 class=4
mm_incref Item #3 rc=1 [Item.create]
mm_transfer Item #3 rc=1 [Item.create]
mm_realloc __ManagedMemory_Item #1 size=32
  mm_raw_alloc #R1 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref Item #3 rc=2 [ItemArray.push]
mm_alloc Item #4 size=8 [Item.create]
  sl_alloc Item #4 size=40 class=4
mm_incref Item #4 rc=1 [Item.create]
mm_transfer Item #4 rc=1 [Item.create]
mm_incref Item #4 rc=2 [ItemArray.push]
mm_alloc Item #5 size=8 [Item.create]
  sl_alloc Item #5 size=40 class=4
mm_incref Item #5 rc=1 [Item.create]
mm_transfer Item #5 rc=1 [Item.create]
mm_incref Item #5 rc=2 [ItemArray.push]
mm_decref Item #3 rc=1 [~ManagedElements]
mm_decref Item #4 rc=1 [~ManagedElements]
mm_decref Item #5 rc=1 [~ManagedElements]
mm_decref Item #5 rc=0 [main]
  mm_free Item #5
    sl_free Item #5 size=48 class=4
mm_decref Item #4 rc=0 [main]
  mm_free Item #4
    sl_free Item #4 size=48 class=4
mm_decref Item #3 rc=0 [main]
  mm_free Item #3
    sl_free Item #3 size=48 class=4
mm_decref ItemArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Item #1 rc=0 [~ItemArray]
    mm_raw_free #R1
      sl_free size=32 class=3
    mm_free __ManagedMemory_Item #1
      sl_free __ManagedMemory_Item #1 size=64 class=5
  mm_free ItemArray #2
    sl_free ItemArray #2 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
```

<!-- test: rc-container-scope-exit-decrefs-elements -->
<!-- MmTrace -->
When a container holding struct elements goes out of scope, all elements are decref'd.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function fill() returns Integer
	let arr = ItemArray.create()
	arr.push(Item.create(value: 10))
	arr.push(Item.create(value: 20))
	arr.push(Item.create(value: 30))
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Item #1 size=32 [ItemArray.create]
  sl_alloc __ManagedMemory_Item #1 size=64 class=5
mm_alloc ItemArray #2 size=16 [ItemArray.create]
  sl_alloc ItemArray #2 size=48 class=4
mm_incref __ManagedMemory_Item #1 rc=1 [ItemArray.create]
mm_incref ItemArray #2 rc=1 [ItemArray.create]
mm_transfer ItemArray #2 rc=1 [ItemArray.create]
mm_alloc Item #3 size=8 [Item.create]
  sl_alloc Item #3 size=40 class=4
mm_incref Item #3 rc=1 [Item.create]
mm_transfer Item #3 rc=1 [Item.create]
mm_realloc __ManagedMemory_Item #1 size=32
  mm_raw_alloc #R1 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref Item #3 rc=2 [ItemArray.push]
mm_alloc Item #4 size=8 [Item.create]
  sl_alloc Item #4 size=40 class=4
mm_incref Item #4 rc=1 [Item.create]
mm_transfer Item #4 rc=1 [Item.create]
mm_incref Item #4 rc=2 [ItemArray.push]
mm_alloc Item #5 size=8 [Item.create]
  sl_alloc Item #5 size=40 class=4
mm_incref Item #5 rc=1 [Item.create]
mm_transfer Item #5 rc=1 [Item.create]
mm_incref Item #5 rc=2 [ItemArray.push]
mm_decref Item #5 rc=1 [ownership-edge-cases.fill]
mm_decref Item #4 rc=1 [ownership-edge-cases.fill]
mm_decref Item #3 rc=1 [ownership-edge-cases.fill]
mm_decref ItemArray #2 rc=0 [ownership-edge-cases.fill]
  mm_decref __ManagedMemory_Item #1 rc=0 [~ItemArray]
    mm_decref Item #3 rc=0 [~ManagedElements]
      mm_free Item #3
        sl_free Item #3 size=48 class=4
    mm_decref Item #4 rc=0 [~ManagedElements]
      mm_free Item #4
        sl_free Item #4 size=48 class=4
    mm_decref Item #5 rc=0 [~ManagedElements]
      mm_free Item #5
        sl_free Item #5 size=48 class=4
    mm_raw_free #R1
      sl_free size=32 class=3
    mm_free __ManagedMemory_Item #1
      sl_free __ManagedMemory_Item #1 size=64 class=5
  mm_free ItemArray #2
    sl_free ItemArray #2 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
```

<!-- test: rc-insert-then-remove-no-leak -->
<!-- MmTrace -->
Insert many structs then remove them all; zero elements remain in memory.
```maxon
typealias Integer = int(i64.min to i64.max)

type Entry
	export var key Integer

	static function create(key Integer) returns Self
		return Self{key: key}
	end 'create'
end 'Entry'

typealias EntryArray = Array with Entry

function main() returns ExitCode
	let arr = EntryArray.create()
	var i = 0
	while i < 5 'push'
		arr.push(Entry.create(key: i))
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Entry #1 size=32 [EntryArray.create]
  sl_alloc __ManagedMemory_Entry #1 size=64 class=5
mm_alloc EntryArray #2 size=16 [EntryArray.create]
  sl_alloc EntryArray #2 size=48 class=4
mm_incref __ManagedMemory_Entry #1 rc=1 [EntryArray.create]
mm_incref EntryArray #2 rc=1 [EntryArray.create]
mm_transfer EntryArray #2 rc=1 [EntryArray.create]
mm_alloc Entry #3 size=8 [Entry.create]
  sl_alloc Entry #3 size=40 class=4
mm_incref Entry #3 rc=1 [Entry.create]
mm_transfer Entry #3 rc=1 [Entry.create]
mm_realloc __ManagedMemory_Entry #1 size=32
  mm_raw_alloc #R1 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref Entry #3 rc=2 [EntryArray.push]
mm_decref Entry #3 rc=1 [main]
mm_alloc Entry #4 size=8 [Entry.create]
  sl_alloc Entry #4 size=40 class=4
mm_incref Entry #4 rc=1 [Entry.create]
mm_transfer Entry #4 rc=1 [Entry.create]
mm_incref Entry #4 rc=2 [EntryArray.push]
mm_decref Entry #4 rc=1 [main]
mm_alloc Entry #5 size=8 [Entry.create]
  sl_alloc Entry #5 size=40 class=4
mm_incref Entry #5 rc=1 [Entry.create]
mm_transfer Entry #5 rc=1 [Entry.create]
mm_incref Entry #5 rc=2 [EntryArray.push]
mm_decref Entry #5 rc=1 [main]
mm_alloc Entry #6 size=8 [Entry.create]
  sl_alloc Entry #6 size=40 class=4
mm_incref Entry #6 rc=1 [Entry.create]
mm_transfer Entry #6 rc=1 [Entry.create]
mm_incref Entry #6 rc=2 [EntryArray.push]
mm_decref Entry #6 rc=1 [main]
mm_alloc Entry #7 size=8 [Entry.create]
  sl_alloc Entry #7 size=40 class=4
mm_incref Entry #7 rc=1 [Entry.create]
mm_transfer Entry #7 rc=1 [Entry.create]
mm_realloc __ManagedMemory_Entry #1 size=64
  mm_raw_alloc #R2 size=64 [realloc]
    sl_alloc size=64 class=5
  mm_raw_free #R1 [realloc]
    sl_free size=32 class=3
mm_incref Entry #7 rc=2 [EntryArray.push]
mm_decref Entry #7 rc=1 [main]
mm_decref Entry #3 rc=0 [main]
  mm_free Entry #3
    sl_free Entry #3 size=48 class=4
mm_decref Entry #4 rc=0 [main]
  mm_free Entry #4
    sl_free Entry #4 size=48 class=4
mm_decref Entry #5 rc=0 [main]
  mm_free Entry #5
    sl_free Entry #5 size=48 class=4
mm_decref Entry #6 rc=0 [main]
  mm_free Entry #6
    sl_free Entry #6 size=48 class=4
mm_decref Entry #7 rc=0 [main]
  mm_free Entry #7
    sl_free Entry #7 size=48 class=4
mm_decref EntryArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Entry #1 rc=0 [~EntryArray]
    mm_raw_free #R2
      sl_free size=64 class=5
    mm_free __ManagedMemory_Entry #1
      sl_free __ManagedMemory_Entry #1 size=64 class=5
  mm_free EntryArray #2
    sl_free EntryArray #2 size=48 class=4
mm_raw_alloc #R3 size=40
  sl_alloc size=40 class=4
mm_raw_free #R3
  sl_free size=48 class=4
```

<!-- test: rc-insert-in-middle-no-leak -->
<!-- MmTrace -->
Insert at index 0 into an existing array; shiftRight zeroes the gap so no double-free occurs.
```maxon
typealias Integer = int(i64.min to i64.max)

type Val
	export var n Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Val'

typealias ValArray = Array with Val

function main() returns ExitCode
	let arr = ValArray.create()
	arr.push(Val.create(n: 10))
	arr.push(Val.create(n: 30))
	arr.insert(1, value: Val.create(n: 20))
	let a = try arr.get(0) otherwise Val.create(n: -1)
	let b = try arr.get(1) otherwise Val.create(n: -1)
	let c = try arr.get(2) otherwise Val.create(n: -1)
	return a.n + b.n + c.n
end 'main'
```
```exitcode
60
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Val #1 size=32 [ValArray.create]
  sl_alloc __ManagedMemory_Val #1 size=64 class=5
mm_alloc ValArray #2 size=16 [ValArray.create]
  sl_alloc ValArray #2 size=48 class=4
mm_incref __ManagedMemory_Val #1 rc=1 [ValArray.create]
mm_incref ValArray #2 rc=1 [ValArray.create]
mm_transfer ValArray #2 rc=1 [ValArray.create]
mm_alloc Val #3 size=8 [Val.create]
  sl_alloc Val #3 size=40 class=4
mm_incref Val #3 rc=1 [Val.create]
mm_transfer Val #3 rc=1 [Val.create]
mm_realloc __ManagedMemory_Val #1 size=32
  mm_raw_alloc #R1 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref Val #3 rc=2 [ValArray.push]
mm_alloc Val #4 size=8 [Val.create]
  sl_alloc Val #4 size=40 class=4
mm_incref Val #4 rc=1 [Val.create]
mm_transfer Val #4 rc=1 [Val.create]
mm_incref Val #4 rc=2 [ValArray.push]
mm_alloc Val #5 size=8 [Val.create]
  sl_alloc Val #5 size=40 class=4
mm_incref Val #5 rc=1 [Val.create]
mm_transfer Val #5 rc=1 [Val.create]
mm_incref Val #5 rc=2 [ValArray.insert]
mm_incref Val #3 rc=3 [ValArray.get]
mm_incref Val #3 rc=4 [main]
mm_incref Val #5 rc=3 [ValArray.get]
mm_incref Val #5 rc=4 [main]
mm_incref Val #4 rc=3 [ValArray.get]
mm_incref Val #4 rc=4 [main]
mm_decref Val #4 rc=3 [main]
mm_decref Val #5 rc=3 [main]
mm_decref Val #3 rc=3 [main]
mm_decref Val #5 rc=2 [main]
mm_decref Val #4 rc=2 [main]
mm_decref Val #3 rc=2 [main]
mm_decref Val #4 rc=1 [main]
mm_decref Val #5 rc=1 [main]
mm_decref Val #3 rc=1 [main]
mm_decref ValArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Val #1 rc=0 [~ValArray]
    mm_decref Val #3 rc=0 [~ManagedElements]
      mm_free Val #3
        sl_free Val #3 size=48 class=4
    mm_decref Val #5 rc=0 [~ManagedElements]
      mm_free Val #5
        sl_free Val #5 size=48 class=4
    mm_decref Val #4 rc=0 [~ManagedElements]
      mm_free Val #4
        sl_free Val #4 size=48 class=4
    mm_raw_free #R1
      sl_free size=32 class=3
    mm_free __ManagedMemory_Val #1
      sl_free __ManagedMemory_Val #1 size=64 class=5
  mm_free ValArray #2
    sl_free ValArray #2 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
```

<!-- test: rc-remove-middle-no-double-free -->
<!-- MmTrace -->
Removing the middle element from an array; shiftLeft zeroes the trailing slot so setLength does not double-decref.
```maxon
typealias Integer = int(i64.min to i64.max)

type Val
	export var n Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Val'

typealias ValArray = Array with Val

function main() returns ExitCode
	let arr = ValArray.create()
	arr.push(Val.create(n: 1))
	arr.push(Val.create(n: 2))
	arr.push(Val.create(n: 3))
	let removed = try arr.remove(1) otherwise 'err'
		return 99
	end 'err'
	return removed.n + arr.count()
end 'main'
```
```exitcode
4
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Val #1 size=32 [ValArray.create]
  sl_alloc __ManagedMemory_Val #1 size=64 class=5
mm_alloc ValArray #2 size=16 [ValArray.create]
  sl_alloc ValArray #2 size=48 class=4
mm_incref __ManagedMemory_Val #1 rc=1 [ValArray.create]
mm_incref ValArray #2 rc=1 [ValArray.create]
mm_transfer ValArray #2 rc=1 [ValArray.create]
mm_alloc Val #3 size=8 [Val.create]
  sl_alloc Val #3 size=40 class=4
mm_incref Val #3 rc=1 [Val.create]
mm_transfer Val #3 rc=1 [Val.create]
mm_realloc __ManagedMemory_Val #1 size=32
  mm_raw_alloc #R1 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref Val #3 rc=2 [ValArray.push]
mm_alloc Val #4 size=8 [Val.create]
  sl_alloc Val #4 size=40 class=4
mm_incref Val #4 rc=1 [Val.create]
mm_transfer Val #4 rc=1 [Val.create]
mm_incref Val #4 rc=2 [ValArray.push]
mm_alloc Val #5 size=8 [Val.create]
  sl_alloc Val #5 size=40 class=4
mm_incref Val #5 rc=1 [Val.create]
mm_transfer Val #5 rc=1 [Val.create]
mm_incref Val #5 rc=2 [ValArray.push]
mm_incref Val #4 rc=3 [main]
mm_decref Val #4 rc=2 [main]
mm_decref Val #5 rc=1 [main]
mm_decref Val #4 rc=1 [main]
mm_decref Val #3 rc=1 [main]
mm_decref Val #4 rc=0 [main]
  mm_free Val #4
    sl_free Val #4 size=48 class=4
mm_decref ValArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Val #1 rc=0 [~ValArray]
    mm_decref Val #3 rc=0 [~ManagedElements]
      mm_free Val #3
        sl_free Val #3 size=48 class=4
    mm_decref Val #5 rc=0 [~ManagedElements]
      mm_free Val #5
        sl_free Val #5 size=48 class=4
    mm_raw_free #R1
      sl_free size=32 class=3
    mm_free __ManagedMemory_Val #1
      sl_free __ManagedMemory_Val #1 size=64 class=5
  mm_free ValArray #2
    sl_free ValArray #2 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
```

<!-- test: rc-nested-container-freed -->
<!-- MmTrace -->
An array whose element type itself contains a struct field; freeing the outer array frees all nested objects.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var v Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Inner'

type Wrapper
	export var inner Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Wrapper'

typealias WrapperArray = Array with Wrapper

function main() returns ExitCode
	let arr = WrapperArray.create()
	arr.push(Wrapper.create(inner: Inner.create(v: 1)))
	arr.push(Wrapper.create(inner: Inner.create(v: 2)))
	return arr.count()
end 'main'
```
```exitcode
2
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Wrapper #1 size=32 [WrapperArray.create]
  sl_alloc __ManagedMemory_Wrapper #1 size=64 class=5
mm_alloc WrapperArray #2 size=16 [WrapperArray.create]
  sl_alloc WrapperArray #2 size=48 class=4
mm_incref __ManagedMemory_Wrapper #1 rc=1 [WrapperArray.create]
mm_incref WrapperArray #2 rc=1 [WrapperArray.create]
mm_transfer WrapperArray #2 rc=1 [WrapperArray.create]
mm_alloc Inner #3 size=8 [Inner.create]
  sl_alloc Inner #3 size=40 class=4
mm_incref Inner #3 rc=1 [Inner.create]
mm_transfer Inner #3 rc=1 [Inner.create]
mm_alloc Wrapper #4 size=8 [Wrapper.create]
  sl_alloc Wrapper #4 size=40 class=4
mm_incref Inner #3 rc=2 [Wrapper.create]
mm_incref Wrapper #4 rc=1 [Wrapper.create]
mm_transfer Wrapper #4 rc=1 [Wrapper.create]
mm_realloc __ManagedMemory_Wrapper #1 size=32
  mm_raw_alloc #R1 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref Wrapper #4 rc=2 [WrapperArray.push]
mm_alloc Inner #5 size=8 [Inner.create]
  sl_alloc Inner #5 size=40 class=4
mm_incref Inner #5 rc=1 [Inner.create]
mm_transfer Inner #5 rc=1 [Inner.create]
mm_alloc Wrapper #6 size=8 [Wrapper.create]
  sl_alloc Wrapper #6 size=40 class=4
mm_incref Inner #5 rc=2 [Wrapper.create]
mm_incref Wrapper #6 rc=1 [Wrapper.create]
mm_transfer Wrapper #6 rc=1 [Wrapper.create]
mm_incref Wrapper #6 rc=2 [WrapperArray.push]
mm_decref Wrapper #6 rc=1 [main]
mm_decref Inner #5 rc=1 [main]
mm_decref Wrapper #4 rc=1 [main]
mm_decref Inner #3 rc=1 [main]
mm_decref WrapperArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Wrapper #1 rc=0 [~WrapperArray]
    mm_decref Wrapper #4 rc=0 [~ManagedElements]
      mm_decref Inner #3 rc=0 [~Wrapper]
        mm_free Inner #3
          sl_free Inner #3 size=48 class=4
      mm_free Wrapper #4
        sl_free Wrapper #4 size=48 class=4
    mm_decref Wrapper #6 rc=0 [~ManagedElements]
      mm_decref Inner #5 rc=0 [~Wrapper]
        mm_free Inner #5
          sl_free Inner #5 size=48 class=4
      mm_free Wrapper #6
        sl_free Wrapper #6 size=48 class=4
    mm_raw_free #R1
      sl_free size=32 class=3
    mm_free __ManagedMemory_Wrapper #1
      sl_free __ManagedMemory_Wrapper #1 size=64 class=5
  mm_free WrapperArray #2
    sl_free WrapperArray #2 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
```

<!-- test: rc-return-from-inner-block-cleanup -->
<!-- MmTrace -->
Returning from inside a nested block must decref all locals in every enclosing block before returning.
```maxon
typealias Integer = int(i64.min to i64.max)

type Step
	export var n Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Step'

function compute(flag bool) returns Integer
	@heap let outer = Step.create(n: 1)
	if flag 'inner'
		@heap let inner = Step.create(n: 2)
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Step #1 size=8 [Step.create]
  sl_alloc Step #1 size=40 class=4
mm_incref Step #1 rc=1 [Step.create]
mm_transfer Step #1 rc=1 [Step.create]
mm_alloc Step #2 size=8 [Step.create]
  sl_alloc Step #2 size=40 class=4
mm_incref Step #2 rc=1 [Step.create]
mm_transfer Step #2 rc=1 [Step.create]
mm_decref Step #2 rc=0 [ownership-edge-cases.compute]
  mm_free Step #2
    sl_free Step #2 size=48 class=4
mm_decref Step #1 rc=0 [ownership-edge-cases.compute]
  mm_free Step #1
    sl_free Step #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-return-container-element -->
<!-- MmTrace -->
Getting an element from a container and returning it; element rc stays above 0 while container is freed.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function getFirst(arr ItemArray) returns Item
	let elem = try arr.get(0) otherwise Item.create(value: -1)
	return elem
end 'getFirst'

function main() returns ExitCode
	let arr = ItemArray.create()
	arr.push(Item.create(value: 77))
	let result = getFirst(arr)
	return result.value
end 'main'
```
```exitcode
77
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Item #1 size=32 [ItemArray.create]
  sl_alloc __ManagedMemory_Item #1 size=64 class=5
mm_alloc ItemArray #2 size=16 [ItemArray.create]
  sl_alloc ItemArray #2 size=48 class=4
mm_incref __ManagedMemory_Item #1 rc=1 [ItemArray.create]
mm_incref ItemArray #2 rc=1 [ItemArray.create]
mm_transfer ItemArray #2 rc=1 [ItemArray.create]
mm_alloc Item #3 size=8 [Item.create]
  sl_alloc Item #3 size=40 class=4
mm_incref Item #3 rc=1 [Item.create]
mm_transfer Item #3 rc=1 [Item.create]
mm_realloc __ManagedMemory_Item #1 size=32
  mm_raw_alloc #R1 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref Item #3 rc=2 [ItemArray.push]
mm_incref Item #3 rc=3 [ItemArray.get]
mm_incref Item #3 rc=4 [ownership-edge-cases.getFirst]
mm_decref Item #3 rc=3 [ownership-edge-cases.getFirst]
mm_transfer Item #3 rc=3 [ownership-edge-cases.getFirst]
mm_decref Item #3 rc=2 [main]
mm_decref Item #3 rc=1 [main]
mm_decref ItemArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Item #1 rc=0 [~ItemArray]
    mm_decref Item #3 rc=0 [~ManagedElements]
      mm_free Item #3
        sl_free Item #3 size=48 class=4
    mm_raw_free #R1
      sl_free size=32 class=3
    mm_free __ManagedMemory_Item #1
      sl_free __ManagedMemory_Item #1 size=64 class=5
  mm_free ItemArray #2
    sl_free ItemArray #2 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
```

<!-- test: rc-global-struct-outlives-local -->
<!-- MmTrace -->
A global variable holds a struct that outlives the function that created it.
```maxon
typealias Integer = int(i64.min to i64.max)

type Cfg
	export var level Integer

	static function create(level Integer) returns Self
		return Self{level: level}
	end 'create'
end 'Cfg'

var globalCfg = Cfg.create(level: 0)

function setup()
	globalCfg = Cfg.create(level: 42)
end 'setup'

function main() returns ExitCode
	setup()
	return globalCfg.level
end 'main'
```
```exitcode
42
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Cfg #1 size=8 [Cfg.create]
  sl_alloc Cfg #1 size=40 class=4
mm_incref Cfg #1 rc=1 [Cfg.create]
mm_transfer Cfg #1 rc=1 [Cfg.create]
mm_incref Cfg #1 rc=2 [__module_init]
mm_decref Cfg #1 rc=1 [__module_init]
mm_alloc Cfg #2 size=8 [Cfg.create]
  sl_alloc Cfg #2 size=40 class=4
mm_incref Cfg #2 rc=1 [Cfg.create]
mm_transfer Cfg #2 rc=1 [Cfg.create]
mm_decref Cfg #1 rc=0 [ownership-edge-cases.setup]
  mm_free Cfg #1
    sl_free Cfg #1 size=48 class=4
mm_incref Cfg #2 rc=2 [ownership-edge-cases.setup]
mm_decref Cfg #2 rc=1 [ownership-edge-cases.setup]
mm_incref Cfg #2 rc=2 [main]
mm_decref Cfg #2 rc=1 [main]
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
mm_decref Cfg #2 rc=0 [__maxon_global_cleanup]
  mm_free Cfg #2
    sl_free Cfg #2 size=48 class=4
```

<!-- test: rc-global-reassign-decrefs-old -->
<!-- MmTrace -->
Reassigning a global struct var decrefs the old object and increfs the new one.
```maxon
typealias Integer = int(i64.min to i64.max)

type State
	export var val Integer

	static function create(val Integer) returns Self
		return Self{val: val}
	end 'create'
end 'State'

var g = State.create(val: 0)

function step(n Integer)
	g = State.create(val: n)
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc State #1 size=8 [State.create]
  sl_alloc State #1 size=40 class=4
mm_incref State #1 rc=1 [State.create]
mm_transfer State #1 rc=1 [State.create]
mm_incref State #1 rc=2 [__module_init]
mm_decref State #1 rc=1 [__module_init]
mm_alloc State #2 size=8 [State.create]
  sl_alloc State #2 size=40 class=4
mm_incref State #2 rc=1 [State.create]
mm_transfer State #2 rc=1 [State.create]
mm_decref State #1 rc=0 [ownership-edge-cases.step]
  mm_free State #1
    sl_free State #1 size=48 class=4
mm_incref State #2 rc=2 [ownership-edge-cases.step]
mm_decref State #2 rc=1 [ownership-edge-cases.step]
mm_alloc State #3 size=8 [State.create]
  sl_alloc State #3 size=40 class=4
mm_incref State #3 rc=1 [State.create]
mm_transfer State #3 rc=1 [State.create]
mm_decref State #2 rc=0 [ownership-edge-cases.step]
  mm_free State #2
    sl_free State #2 size=48 class=4
mm_incref State #3 rc=2 [ownership-edge-cases.step]
mm_decref State #3 rc=1 [ownership-edge-cases.step]
mm_alloc State #4 size=8 [State.create]
  sl_alloc State #4 size=40 class=4
mm_incref State #4 rc=1 [State.create]
mm_transfer State #4 rc=1 [State.create]
mm_decref State #3 rc=0 [ownership-edge-cases.step]
  mm_free State #3
    sl_free State #3 size=48 class=4
mm_incref State #4 rc=2 [ownership-edge-cases.step]
mm_decref State #4 rc=1 [ownership-edge-cases.step]
mm_incref State #4 rc=2 [main]
mm_decref State #4 rc=1 [main]
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
mm_decref State #4 rc=0 [__maxon_global_cleanup]
  mm_free State #4
    sl_free State #4 size=48 class=4
```

<!-- test: rc-enum-no-struct-payload-freed -->
<!-- MmTrace -->
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
```stderr
sl_init
  os_alloc size=67108864
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-enum-struct-payload-freed -->
<!-- MmTrace -->
A enum case with a struct-typed associated value; when the enum is freed its payload must be decref'd.
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

enum Shape
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
	let s = Shape.solid(Body.create(mass: 5))
	return massOf(s)
end 'main'
```
```exitcode
5
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Body #1 size=8 [Body.create]
  sl_alloc Body #1 size=40 class=4
mm_incref Body #1 rc=1 [Body.create]
mm_transfer Body #1 rc=1 [Body.create]
mm_alloc Shape #2 size=16 [main]
  sl_alloc Shape #2 size=48 class=4
mm_incref Body #1 rc=2 [main]
mm_incref Shape #2 rc=1 [main]
mm_incref Shape #2 rc=2 [ownership-edge-cases.massOf]
mm_incref Body #1 rc=3 [ownership-edge-cases.massOf]
mm_decref Shape #2 rc=1 [ownership-edge-cases.massOf]
mm_decref Body #1 rc=2 [ownership-edge-cases.massOf]
mm_decref Body #1 rc=1 [main]
mm_decref Shape #2 rc=0 [main]
  mm_decref Body #1 rc=0 [~Shape]
    mm_free Body #1
      sl_free Body #1 size=48 class=4
  mm_free Shape #2
    sl_free Shape #2 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-closure-env-freed -->
<!-- MmTrace -->
Closure environment block is allocated as a struct and freed when the closure variable goes out of scope.
```maxon
typealias Integer = int(i64.min to i64.max)

function apply(f (Integer) returns Integer, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let offset = 5
	let result = apply(f: (n Integer) gives n + offset, x: 10)
	return result
end 'main'
```
```exitcode
15
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc ClosureEnv #1 size=8 [main]
  sl_alloc ClosureEnv #1 size=40 class=4
mm_incref ClosureEnv #1 rc=1 [main]
mm_decref ClosureEnv #1 rc=0 [main]
  mm_free ClosureEnv #1
    sl_free ClosureEnv #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-closure-captures-struct -->
<!-- MmTrace -->
Closure captures a struct variable by address; the closure env is freed at scope exit but the original struct lives on.
```maxon
typealias Integer = int(i64.min to i64.max)

type Config
	export var level Integer

	static function create(level Integer) returns Self
		return Self{level: level}
	end 'create'
end 'Config'

function apply(f (Integer) returns Integer, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let cfg = Config.create(level: 3)
	let result = apply(f: (_ Integer) gives cfg.level, x: 0)
	return result
end 'main'
```
```exitcode
3
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Config #1 size=8 [Config.create]
  sl_alloc Config #1 size=40 class=4
mm_incref Config #1 rc=1 [Config.create]
mm_transfer Config #1 rc=1 [Config.create]
mm_alloc ClosureEnv #2 size=8 [main]
  sl_alloc ClosureEnv #2 size=40 class=4
mm_incref ClosureEnv #2 rc=1 [main]
mm_decref Config #1 rc=0 [main]
  mm_free Config #1
    sl_free Config #1 size=48 class=4
mm_decref ClosureEnv #2 rc=0 [main]
  mm_free ClosureEnv #2
    sl_free ClosureEnv #2 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-error-path-cleanup -->
<!-- MmTrace -->
On the error path of a try expression the locally allocated struct must still be freed.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
	let arr = ItemArray.create()
	let got = try arr.get(0) otherwise Item.create(value: 99)
	return got.value
end 'main'
```
```exitcode
99
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Item #1 size=32 [ItemArray.create]
  sl_alloc __ManagedMemory_Item #1 size=64 class=5
mm_alloc ItemArray #2 size=16 [ItemArray.create]
  sl_alloc ItemArray #2 size=48 class=4
mm_incref __ManagedMemory_Item #1 rc=1 [ItemArray.create]
mm_incref ItemArray #2 rc=1 [ItemArray.create]
mm_transfer ItemArray #2 rc=1 [ItemArray.create]
mm_alloc Item #3 size=8 [Item.create]
  sl_alloc Item #3 size=40 class=4
mm_incref Item #3 rc=1 [Item.create]
mm_transfer Item #3 rc=1 [Item.create]
mm_incref Item #3 rc=2 [main]
mm_incref Item #3 rc=3 [main]
mm_decref Item #3 rc=2 [main]
mm_decref Item #3 rc=1 [main]
mm_decref Item #3 rc=0 [main]
  mm_free Item #3
    sl_free Item #3 size=48 class=4
mm_decref ItemArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Item #1 rc=0 [~ItemArray]
    mm_free __ManagedMemory_Item #1
      sl_free __ManagedMemory_Item #1 size=64 class=5
  mm_free ItemArray #2
    sl_free ItemArray #2 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-managed-list-insert-incref -->
<!-- MmTrace -->
Inserting a struct into a managed list increfs the value; the node holds the reference.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
	export var id Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Token'

typealias TokenManagedList = __ManagedList with Token

function main() returns ExitCode
	let managedList = TokenManagedList.create()
	let t = Token.create(id: 7)
	let node = managedList.insertFirst(t)
	return node.value().id
end 'main'
```
```exitcode
7
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc TokenManagedList #1 size=32 [main]
  sl_alloc TokenManagedList #1 size=64 class=5
mm_incref TokenManagedList #1 rc=1 [main]
mm_alloc Token #2 size=8 [Token.create]
  sl_alloc Token #2 size=40 class=4
mm_incref Token #2 rc=1 [Token.create]
mm_transfer Token #2 rc=1 [Token.create]
mm_alloc __ManagedListNode #3 size=32 [main]
  sl_alloc __ManagedListNode #3 size=64 class=5
mm_incref Token #2 rc=2 [main]
mm_incref __ManagedListNode #3 rc=1 [managed_list_insert]
mm_incref __ManagedListNode #3 rc=2 [main]
mm_decref Token #2 rc=1 [main]
mm_decref __ManagedListNode #3 rc=1 [main]
mm_decref TokenManagedList #1 rc=0 [main]
  mm_decref Token #2 rc=0 [managed_list_clear]
    mm_free Token #2
      sl_free Token #2 size=48 class=4
  mm_decref __ManagedListNode #3 rc=0 [managed_list_clear]
    mm_free __ManagedListNode #3
      sl_free __ManagedListNode #3 size=64 class=5
  mm_free TokenManagedList #1
    sl_free TokenManagedList #1 size=64 class=5
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-managed-list-remove-decrefs -->
<!-- MmTrace -->
Removing a node from a managed list transfers ownership; value is freed when the result var leaves scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
	export var id Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Token'

typealias TokenManagedList = __ManagedList with Token

function main() returns ExitCode
	let managedList = TokenManagedList.create()
	let node = managedList.insertFirst(Token.create(id: 9))
	let removed = managedList.remove(node)
	return removed.id + managedList.count()
end 'main'
```
```exitcode
9
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc TokenManagedList #1 size=32 [main]
  sl_alloc TokenManagedList #1 size=64 class=5
mm_incref TokenManagedList #1 rc=1 [main]
mm_alloc Token #2 size=8 [Token.create]
  sl_alloc Token #2 size=40 class=4
mm_incref Token #2 rc=1 [Token.create]
mm_transfer Token #2 rc=1 [Token.create]
mm_alloc __ManagedListNode #3 size=32 [main]
  sl_alloc __ManagedListNode #3 size=64 class=5
mm_incref Token #2 rc=2 [main]
mm_incref __ManagedListNode #3 rc=1 [managed_list_insert]
mm_incref __ManagedListNode #3 rc=2 [main]
mm_decref __ManagedListNode #3 rc=1 [main]
mm_decref Token #2 rc=1 [main]
mm_decref __ManagedListNode #3 rc=0 [main]
  mm_free __ManagedListNode #3
    sl_free __ManagedListNode #3 size=64 class=5
mm_incref Token #2 rc=2 [main]
mm_decref Token #2 rc=1 [main]
mm_decref Token #2 rc=0 [main]
  mm_free Token #2
    sl_free Token #2 size=48 class=4
mm_decref TokenManagedList #1 rc=0 [main]
  mm_free TokenManagedList #1
    sl_free TokenManagedList #1 size=64 class=5
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-managed-list-clear-decrefs-all -->
<!-- MmTrace -->
Clearing a managed list decrefs every node value; all values freed when rc hits 0.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
	export var id Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Token'

typealias TokenManagedList = __ManagedList with Token

function main() returns ExitCode
	let managedList = TokenManagedList.create()
	managedList.insertLast(Token.create(id: 1))
	managedList.insertLast(Token.create(id: 2))
	managedList.insertLast(Token.create(id: 3))
	managedList.clear()
	return managedList.count()
end 'main'
```
```exitcode
0
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc TokenManagedList #1 size=32 [main]
  sl_alloc TokenManagedList #1 size=64 class=5
mm_incref TokenManagedList #1 rc=1 [main]
mm_alloc Token #2 size=8 [Token.create]
  sl_alloc Token #2 size=40 class=4
mm_incref Token #2 rc=1 [Token.create]
mm_transfer Token #2 rc=1 [Token.create]
mm_alloc __ManagedListNode #3 size=32 [main]
  sl_alloc __ManagedListNode #3 size=64 class=5
mm_incref Token #2 rc=2 [main]
mm_incref __ManagedListNode #3 rc=1 [managed_list_insert]
mm_alloc Token #4 size=8 [Token.create]
  sl_alloc Token #4 size=40 class=4
mm_incref Token #4 rc=1 [Token.create]
mm_transfer Token #4 rc=1 [Token.create]
mm_alloc __ManagedListNode #5 size=32 [main]
  sl_alloc __ManagedListNode #5 size=64 class=5
mm_incref Token #4 rc=2 [main]
mm_incref __ManagedListNode #5 rc=1 [managed_list_insert]
mm_alloc Token #6 size=8 [Token.create]
  sl_alloc Token #6 size=40 class=4
mm_incref Token #6 rc=1 [Token.create]
mm_transfer Token #6 rc=1 [Token.create]
mm_alloc __ManagedListNode #7 size=32 [main]
  sl_alloc __ManagedListNode #7 size=64 class=5
mm_incref Token #6 rc=2 [main]
mm_incref __ManagedListNode #7 rc=1 [managed_list_insert]
mm_decref Token #2 rc=1 [managed_list_clear]
mm_decref __ManagedListNode #3 rc=0 [managed_list_clear]
  mm_free __ManagedListNode #3
    sl_free __ManagedListNode #3 size=64 class=5
mm_decref Token #4 rc=1 [managed_list_clear]
mm_decref __ManagedListNode #5 rc=0 [managed_list_clear]
  mm_free __ManagedListNode #5
    sl_free __ManagedListNode #5 size=64 class=5
mm_decref Token #6 rc=1 [managed_list_clear]
mm_decref __ManagedListNode #7 rc=0 [managed_list_clear]
  mm_free __ManagedListNode #7
    sl_free __ManagedListNode #7 size=64 class=5
mm_decref Token #6 rc=0 [main]
  mm_free Token #6
    sl_free Token #6 size=48 class=4
mm_decref Token #4 rc=0 [main]
  mm_free Token #4
    sl_free Token #4 size=48 class=4
mm_decref Token #2 rc=0 [main]
  mm_free Token #2
    sl_free Token #2 size=48 class=4
mm_decref TokenManagedList #1 rc=0 [main]
  mm_free TokenManagedList #1
    sl_free TokenManagedList #1 size=64 class=5
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-managed-list-node-set-value-decrefs-old -->
<!-- MmTrace -->
Calling `setValue` on a managed list node decrefs the old value and increfs the new one.
```maxon
typealias Integer = int(i64.min to i64.max)

type Token
	export var id Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Token'

typealias TokenManagedList = __ManagedList with Token

function main() returns ExitCode
	let managedList = TokenManagedList.create()
	let node = managedList.insertFirst(Token.create(id: 1))
	node.setValue(Token.create(id: 99))
	return node.value().id
end 'main'
```
```exitcode
99
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc TokenManagedList #1 size=32 [main]
  sl_alloc TokenManagedList #1 size=64 class=5
mm_incref TokenManagedList #1 rc=1 [main]
mm_alloc Token #2 size=8 [Token.create]
  sl_alloc Token #2 size=40 class=4
mm_incref Token #2 rc=1 [Token.create]
mm_transfer Token #2 rc=1 [Token.create]
mm_alloc __ManagedListNode #3 size=32 [main]
  sl_alloc __ManagedListNode #3 size=64 class=5
mm_incref Token #2 rc=2 [main]
mm_incref __ManagedListNode #3 rc=1 [managed_list_insert]
mm_incref __ManagedListNode #3 rc=2 [main]
mm_alloc Token #4 size=8 [Token.create]
  sl_alloc Token #4 size=40 class=4
mm_incref Token #4 rc=1 [Token.create]
mm_transfer Token #4 rc=1 [Token.create]
mm_decref Token #2 rc=1 [main]
mm_incref Token #4 rc=2 [main]
mm_decref Token #4 rc=1 [main]
mm_decref Token #2 rc=0 [main]
  mm_free Token #2
    sl_free Token #2 size=48 class=4
mm_decref __ManagedListNode #3 rc=1 [main]
mm_decref TokenManagedList #1 rc=0 [main]
  mm_decref Token #4 rc=0 [managed_list_clear]
    mm_free Token #4
      sl_free Token #4 size=48 class=4
  mm_decref __ManagedListNode #3 rc=0 [managed_list_clear]
    mm_free __ManagedListNode #3
      sl_free __ManagedListNode #3 size=64 class=5
  mm_free TokenManagedList #1
    sl_free TokenManagedList #1 size=64 class=5
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-for-in-elem-decrefed -->
<!-- MmTrace -->
In a for-in loop over a struct array each element reference is decref'd at the end of the loop body.
```maxon
typealias Integer = int(i64.min to i64.max)

type Score
	export var pts Integer

	static function create(pts Integer) returns Self
		return Self{pts: pts}
	end 'create'
end 'Score'

typealias ScoreArray = Array with Score

function main() returns ExitCode
	let scores = ScoreArray.create()
	scores.push(Score.create(pts: 10))
	scores.push(Score.create(pts: 20))
	scores.push(Score.create(pts: 30))
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Score #1 size=32 [ScoreArray.create]
  sl_alloc __ManagedMemory_Score #1 size=64 class=5
mm_alloc ScoreArray #2 size=16 [ScoreArray.create]
  sl_alloc ScoreArray #2 size=48 class=4
mm_incref __ManagedMemory_Score #1 rc=1 [ScoreArray.create]
mm_incref ScoreArray #2 rc=1 [ScoreArray.create]
mm_transfer ScoreArray #2 rc=1 [ScoreArray.create]
mm_alloc Score #3 size=8 [Score.create]
  sl_alloc Score #3 size=40 class=4
mm_incref Score #3 rc=1 [Score.create]
mm_transfer Score #3 rc=1 [Score.create]
mm_realloc __ManagedMemory_Score #1 size=32
  mm_raw_alloc #R1 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref Score #3 rc=2 [ScoreArray.push]
mm_alloc Score #4 size=8 [Score.create]
  sl_alloc Score #4 size=40 class=4
mm_incref Score #4 rc=1 [Score.create]
mm_transfer Score #4 rc=1 [Score.create]
mm_incref Score #4 rc=2 [ScoreArray.push]
mm_alloc Score #5 size=8 [Score.create]
  sl_alloc Score #5 size=40 class=4
mm_incref Score #5 rc=1 [Score.create]
mm_transfer Score #5 rc=1 [Score.create]
mm_incref Score #5 rc=2 [ScoreArray.push]
mm_alloc __Array_Iter #6 size=16 [ScoreArray.__createIter]
  sl_alloc __Array_Iter #6 size=48 class=4
mm_incref ScoreArray #2 rc=2 [ScoreArray.__createIter]
mm_incref __Array_Iter #6 rc=1 [ScoreArray.__createIter]
mm_transfer __Array_Iter #6 rc=1 [ScoreArray.__createIter]
mm_incref Score #3 rc=3 [__ScoreArray_Iter.next]
mm_transfer Score #3 rc=3 [__ScoreArray_Iter.next]
mm_decref Score #3 rc=2 [main]
mm_incref Score #4 rc=3 [__ScoreArray_Iter.next]
mm_transfer Score #4 rc=3 [__ScoreArray_Iter.next]
mm_decref Score #4 rc=2 [main]
mm_incref Score #5 rc=3 [__ScoreArray_Iter.next]
mm_transfer Score #5 rc=3 [__ScoreArray_Iter.next]
mm_decref Score #5 rc=2 [main]
mm_decref __Array_Iter #6 rc=0 [main]
  mm_decref ScoreArray #2 rc=1 [~__Array_Iter]
  mm_free __Array_Iter #6
    sl_free __Array_Iter #6 size=48 class=4
mm_decref Score #5 rc=1 [main]
mm_decref Score #4 rc=1 [main]
mm_decref Score #3 rc=1 [main]
mm_decref ScoreArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Score #1 rc=0 [~ScoreArray]
    mm_decref Score #3 rc=0 [~ManagedElements]
      mm_free Score #3
        sl_free Score #3 size=48 class=4
    mm_decref Score #4 rc=0 [~ManagedElements]
      mm_free Score #4
        sl_free Score #4 size=48 class=4
    mm_decref Score #5 rc=0 [~ManagedElements]
      mm_free Score #5
        sl_free Score #5 size=48 class=4
    mm_raw_free #R1
      sl_free size=32 class=3
    mm_free __ManagedMemory_Score #1
      sl_free __ManagedMemory_Score #1 size=64 class=5
  mm_free ScoreArray #2
    sl_free ScoreArray #2 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
```

<!-- test: rc-multiple-aliases-freed-once -->
<!-- MmTrace -->
Three aliases to the same object; the object is freed exactly once when the last alias leaves scope.
```maxon
typealias Integer = int(i64.min to i64.max)

type Data
	export var n Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Data'

function main() returns ExitCode
	@heap let a = Data.create(n: 7)
	let b = a
	let c = a
	return a.n + b.n + c.n
end 'main'
```
```exitcode
21
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Data #1 size=8 [Data.create]
  sl_alloc Data #1 size=40 class=4
mm_incref Data #1 rc=1 [Data.create]
mm_transfer Data #1 rc=1 [Data.create]
mm_incref Data #1 rc=2 [main]
mm_incref Data #1 rc=3 [main]
mm_decref Data #1 rc=2 [main]
mm_decref Data #1 rc=1 [main]
mm_decref Data #1 rc=0 [main]
  mm_free Data #1
    sl_free Data #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-deep-container-of-containers -->
<!-- MmTrace -->
An array of arrays of structs; freeing the outer array cascades through all levels.
```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var val Integer

	static function create(val Integer) returns Self
		return Self{val: val}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell
typealias Grid = Array with CellArray

function main() returns ExitCode
	let grid = Grid.create()
	let row1 = CellArray.create()
	row1.push(Cell.create(val: 1))
	row1.push(Cell.create(val: 2))
	let row2 = CellArray.create()
	row2.push(Cell.create(val: 3))
	grid.push(row1)
	grid.push(row2)
	return grid.count()
end 'main'
```
```exitcode
2
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_CellArray #1 size=32 [Grid.create]
  sl_alloc __ManagedMemory_CellArray #1 size=64 class=5
mm_alloc Grid #2 size=16 [Grid.create]
  sl_alloc Grid #2 size=48 class=4
mm_incref __ManagedMemory_CellArray #1 rc=1 [Grid.create]
mm_incref Grid #2 rc=1 [Grid.create]
mm_transfer Grid #2 rc=1 [Grid.create]
mm_alloc __ManagedMemory_Cell #3 size=32 [CellArray.create]
  sl_alloc __ManagedMemory_Cell #3 size=64 class=5
mm_alloc CellArray #4 size=16 [CellArray.create]
  sl_alloc CellArray #4 size=48 class=4
mm_incref __ManagedMemory_Cell #3 rc=1 [CellArray.create]
mm_incref CellArray #4 rc=1 [CellArray.create]
mm_transfer CellArray #4 rc=1 [CellArray.create]
mm_alloc Cell #5 size=8 [Cell.create]
  sl_alloc Cell #5 size=40 class=4
mm_incref Cell #5 rc=1 [Cell.create]
mm_transfer Cell #5 rc=1 [Cell.create]
mm_realloc __ManagedMemory_Cell #3 size=32
  mm_raw_alloc #R1 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref Cell #5 rc=2 [CellArray.push]
mm_alloc Cell #6 size=8 [Cell.create]
  sl_alloc Cell #6 size=40 class=4
mm_incref Cell #6 rc=1 [Cell.create]
mm_transfer Cell #6 rc=1 [Cell.create]
mm_incref Cell #6 rc=2 [CellArray.push]
mm_alloc __ManagedMemory_Cell #7 size=32 [CellArray.create]
  sl_alloc __ManagedMemory_Cell #7 size=64 class=5
mm_alloc CellArray #8 size=16 [CellArray.create]
  sl_alloc CellArray #8 size=48 class=4
mm_incref __ManagedMemory_Cell #7 rc=1 [CellArray.create]
mm_incref CellArray #8 rc=1 [CellArray.create]
mm_transfer CellArray #8 rc=1 [CellArray.create]
mm_alloc Cell #9 size=8 [Cell.create]
  sl_alloc Cell #9 size=40 class=4
mm_incref Cell #9 rc=1 [Cell.create]
mm_transfer Cell #9 rc=1 [Cell.create]
mm_realloc __ManagedMemory_Cell #7 size=32
  mm_raw_alloc #R2 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref Cell #9 rc=2 [CellArray.push]
mm_realloc __ManagedMemory_CellArray #1 size=32
  mm_raw_alloc #R3 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref CellArray #4 rc=2 [Grid.push]
mm_incref CellArray #8 rc=2 [Grid.push]
mm_decref Cell #9 rc=1 [main]
mm_decref Cell #6 rc=1 [main]
mm_decref Cell #5 rc=1 [main]
mm_decref CellArray #8 rc=1 [main]
mm_decref CellArray #4 rc=1 [main]
mm_decref Grid #2 rc=0 [main]
  mm_decref __ManagedMemory_CellArray #1 rc=0 [~Grid]
    mm_decref CellArray #4 rc=0 [~ManagedElements]
      mm_decref __ManagedMemory_Cell #3 rc=0 [~CellArray]
        mm_decref Cell #5 rc=0 [~ManagedElements]
          mm_free Cell #5
            sl_free Cell #5 size=48 class=4
        mm_decref Cell #6 rc=0 [~ManagedElements]
          mm_free Cell #6
            sl_free Cell #6 size=48 class=4
        mm_raw_free #R1
          sl_free size=32 class=3
        mm_free __ManagedMemory_Cell #3
          sl_free __ManagedMemory_Cell #3 size=64 class=5
      mm_free CellArray #4
        sl_free CellArray #4 size=48 class=4
    mm_decref CellArray #8 rc=0 [~ManagedElements]
      mm_decref __ManagedMemory_Cell #7 rc=0 [~CellArray]
        mm_decref Cell #9 rc=0 [~ManagedElements]
          mm_free Cell #9
            sl_free Cell #9 size=48 class=4
        mm_raw_free #R2
          sl_free size=32 class=3
        mm_free __ManagedMemory_Cell #7
          sl_free __ManagedMemory_Cell #7 size=64 class=5
      mm_free CellArray #8
        sl_free CellArray #8 size=48 class=4
    mm_raw_free #R3
      sl_free size=32 class=3
    mm_free __ManagedMemory_CellArray #1
      sl_free __ManagedMemory_CellArray #1 size=64 class=5
  mm_free Grid #2
    sl_free Grid #2 size=48 class=4
mm_raw_alloc #R4 size=40
  sl_alloc size=40 class=4
mm_raw_free #R4
  sl_free size=48 class=4
```

<!-- test: rc-struct-with-array-field-freed -->
<!-- MmTrace -->
A struct that owns an array field; when the struct is freed the array (and its elements) are freed too.
```maxon
typealias Integer = int(i64.min to i64.max)

type Entry
	export var val Integer

	static function create(val Integer) returns Self
		return Self{val: val}
	end 'create'
end 'Entry'

typealias EntryArray = Array with Entry

type Bucket
	export var items EntryArray

	static function create(items EntryArray) returns Self
		return Self{items: items}
	end 'create'
end 'Bucket'

function fill() returns Integer
	let b = Bucket.create(items: EntryArray.create())
	b.items.push(Entry.create(val: 10))
	b.items.push(Entry.create(val: 20))
	return b.items.count()
end 'fill'

function main() returns ExitCode
	return fill()
end 'main'
```
```exitcode
2
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Entry #1 size=32 [EntryArray.create]
  sl_alloc __ManagedMemory_Entry #1 size=64 class=5
mm_alloc EntryArray #2 size=16 [EntryArray.create]
  sl_alloc EntryArray #2 size=48 class=4
mm_incref __ManagedMemory_Entry #1 rc=1 [EntryArray.create]
mm_incref EntryArray #2 rc=1 [EntryArray.create]
mm_transfer EntryArray #2 rc=1 [EntryArray.create]
mm_alloc Bucket #3 size=8 [Bucket.create]
  sl_alloc Bucket #3 size=40 class=4
mm_incref EntryArray #2 rc=2 [Bucket.create]
mm_incref Bucket #3 rc=1 [Bucket.create]
mm_transfer Bucket #3 rc=1 [Bucket.create]
mm_alloc Entry #4 size=8 [Entry.create]
  sl_alloc Entry #4 size=40 class=4
mm_incref Entry #4 rc=1 [Entry.create]
mm_transfer Entry #4 rc=1 [Entry.create]
mm_realloc __ManagedMemory_Entry #1 size=32
  mm_raw_alloc #R1 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref Entry #4 rc=2 [EntryArray.push]
mm_alloc Entry #5 size=8 [Entry.create]
  sl_alloc Entry #5 size=40 class=4
mm_incref Entry #5 rc=1 [Entry.create]
mm_transfer Entry #5 rc=1 [Entry.create]
mm_incref Entry #5 rc=2 [EntryArray.push]
mm_decref Entry #5 rc=1 [ownership-edge-cases.fill]
mm_decref Entry #4 rc=1 [ownership-edge-cases.fill]
mm_decref EntryArray #2 rc=1 [ownership-edge-cases.fill]
mm_decref Bucket #3 rc=0 [ownership-edge-cases.fill]
  mm_decref EntryArray #2 rc=0 [~Bucket]
    mm_decref __ManagedMemory_Entry #1 rc=0 [~EntryArray]
      mm_decref Entry #4 rc=0 [~ManagedElements]
        mm_free Entry #4
          sl_free Entry #4 size=48 class=4
      mm_decref Entry #5 rc=0 [~ManagedElements]
        mm_free Entry #5
          sl_free Entry #5 size=48 class=4
      mm_raw_free #R1
        sl_free size=32 class=3
      mm_free __ManagedMemory_Entry #1
        sl_free __ManagedMemory_Entry #1 size=64 class=5
    mm_free EntryArray #2
      sl_free EntryArray #2 size=48 class=4
  mm_free Bucket #3
    sl_free Bucket #3 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
```

<!-- test: rc-return-struct-literal -->
<!-- MmTrace -->
Returning a struct literal directly from a function must transfer ownership at rc=1.
The callee constructs the struct (rc=0), increfs it for the assignment, and transfers
ownership to the caller via KeepVars. The caller must not incref again.
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
	export var a Integer
	export var b Integer

	static function create(a Integer, b Integer) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Pair'

function makePair(x Integer, y Integer) returns Pair
	return Pair.create(a: x, b: y)
end 'makePair'

function main() returns ExitCode
	let p = makePair(x: 3, y: 7)
	return p.a + p.b
end 'main'
```
```exitcode
10
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Pair #1 size=16 [Pair.create]
  sl_alloc Pair #1 size=48 class=4
mm_incref Pair #1 rc=1 [Pair.create]
mm_transfer Pair #1 rc=1 [Pair.create]
mm_transfer Pair #1 rc=1 [ownership-edge-cases.makePair]
mm_decref Pair #1 rc=0 [main]
  mm_free Pair #1
    sl_free Pair #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-return-struct-with-managed-field -->
<!-- MmTrace -->
Returning a struct whose field is a shared managed reference. The callee increfs
the shared field when storing it, and transfers the outer struct at rc=1.
The caller must decref the outer struct, which cascades to decref the managed field.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Wrapper
	export var inner Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Wrapper'

function wrap(i Inner) returns Wrapper
	return Wrapper.create(inner: i)
end 'wrap'

function main() returns ExitCode
	let i = Inner.create(value: 5)
	let w = wrap(i: i)
	return w.inner.value
end 'main'
```
```exitcode
5
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Inner #1 size=8 [Inner.create]
  sl_alloc Inner #1 size=40 class=4
mm_incref Inner #1 rc=1 [Inner.create]
mm_transfer Inner #1 rc=1 [Inner.create]
mm_alloc Wrapper #2 size=8 [Wrapper.create]
  sl_alloc Wrapper #2 size=40 class=4
mm_incref Inner #1 rc=2 [Wrapper.create]
mm_incref Wrapper #2 rc=1 [Wrapper.create]
mm_transfer Wrapper #2 rc=1 [Wrapper.create]
mm_transfer Wrapper #2 rc=1 [ownership-edge-cases.wrap]
mm_decref Wrapper #2 rc=0 [main]
  mm_decref Inner #1 rc=1 [~Wrapper]
  mm_free Wrapper #2
    sl_free Wrapper #2 size=48 class=4
mm_decref Inner #1 rc=0 [main]
  mm_free Inner #1
    sl_free Inner #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-list-scope-cleanup -->
<!-- MmTrace -->
List (struct owning a managed list field) must walk and decref managed list node values on scope exit.
```maxon
typealias StringList = List with String

function main() returns ExitCode
	let list = StringList.create()
	list.append("hello")
	return 0
end 'main'
```
```exitcode
0
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedList_String #1 size=32 [StringList.create]
  sl_alloc __ManagedList_String #1 size=64 class=5
mm_alloc StringList #2 size=24 [StringList.create]
  sl_alloc StringList #2 size=56 class=5
mm_incref __ManagedList_String #1 rc=1 [StringList.create]
mm_incref StringList #2 rc=1 [StringList.create]
mm_transfer StringList #2 rc=1 [StringList.create]
mm_alloc String #3 size=32 [main]
  sl_alloc String #3 size=64 class=5
mm_alloc __ManagedMemory #4 size=32 [main]
  sl_alloc __ManagedMemory #4 size=64 class=5
mm_incref __ManagedMemory #4 rc=1 [main]
mm_incref String #3 rc=1 [main]
mm_alloc __ManagedListNode #5 size=32 [StringList.append]
  sl_alloc __ManagedListNode #5 size=64 class=5
mm_incref String #3 rc=2 [StringList.append]
mm_incref __ManagedListNode #5 rc=1 [managed_list_insert]
mm_decref String #3 rc=1 [main]
mm_decref StringList #2 rc=0 [main]
  mm_decref __ManagedList_String #1 rc=0 [~StringList]
    mm_decref String #3 rc=0 [managed_list_clear]
      mm_decref __ManagedMemory #4 rc=0 [~String]
        mm_free __ManagedMemory #4
          sl_free __ManagedMemory #4 size=64 class=5
      mm_free String #3
        sl_free String #3 size=64 class=5
    mm_decref __ManagedListNode #5 rc=0 [managed_list_clear]
      mm_free __ManagedListNode #5
        sl_free __ManagedListNode #5 size=64 class=5
    mm_free __ManagedList_String #1
      sl_free __ManagedList_String #1 size=64 class=5
  mm_free StringList #2
    sl_free StringList #2 size=64 class=5
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: match-string-pattern-cleanup -->
<!-- MmTrace -->
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc String #1 size=32 [main]
  sl_alloc String #1 size=64 class=5
mm_alloc __ManagedMemory #2 size=32 [main]
  sl_alloc __ManagedMemory #2 size=64 class=5
mm_incref __ManagedMemory #2 rc=1 [main]
mm_incref String #1 rc=1 [main]
mm_incref String #1 rc=2 [main]
mm_incref String #1 rc=3 [main]
mm_alloc String #3 size=32 [main]
  sl_alloc String #3 size=64 class=5
mm_alloc __ManagedMemory #4 size=32 [main]
  sl_alloc __ManagedMemory #4 size=64 class=5
mm_incref __ManagedMemory #4 rc=1 [main]
mm_incref String #3 rc=1 [main]
mm_decref String #3 rc=0 [main]
  mm_decref __ManagedMemory #4 rc=0 [~String]
    mm_free __ManagedMemory #4
      sl_free __ManagedMemory #4 size=64 class=5
  mm_free String #3
    sl_free String #3 size=64 class=5
mm_decref String #1 rc=2 [main]
mm_decref String #1 rc=1 [main]
mm_decref String #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [~String]
    mm_free __ManagedMemory #2
      sl_free __ManagedMemory #2 size=64 class=5
  mm_free String #1
    sl_free String #1 size=64 class=5
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-char-single-alloc-freed -->
<!-- MmTrace -->
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Character #1 size=8 [main]
  sl_alloc Character #1 size=40 class=4
mm_alloc __ManagedMemory #2 size=32 [main]
  sl_alloc __ManagedMemory #2 size=64 class=5
mm_incref __ManagedMemory #2 rc=1 [main]
mm_incref Character #1 rc=1 [main]
mm_incref Character #1 rc=2 [main]
mm_decref Character #1 rc=1 [main]
mm_decref Character #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [~Character]
    mm_free __ManagedMemory #2
      sl_free __ManagedMemory #2 size=64 class=5
  mm_free Character #1
    sl_free Character #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-char-alias-incref -->
<!-- MmTrace -->
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Character #1 size=8 [main]
  sl_alloc Character #1 size=40 class=4
mm_alloc __ManagedMemory #2 size=32 [main]
  sl_alloc __ManagedMemory #2 size=64 class=5
mm_incref __ManagedMemory #2 rc=1 [main]
mm_incref Character #1 rc=1 [main]
mm_incref Character #1 rc=2 [main]
mm_incref Character #1 rc=3 [main]
mm_decref Character #1 rc=2 [main]
mm_decref Character #1 rc=1 [main]
mm_decref Character #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [~Character]
    mm_free __ManagedMemory #2
      sl_free __ManagedMemory #2 size=64 class=5
  mm_free Character #1
    sl_free Character #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-char-reassign-decrefs-old -->
<!-- MmTrace -->
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Character #1 size=8 [main]
  sl_alloc Character #1 size=40 class=4
mm_alloc __ManagedMemory #2 size=32 [main]
  sl_alloc __ManagedMemory #2 size=64 class=5
mm_incref __ManagedMemory #2 rc=1 [main]
mm_incref Character #1 rc=1 [main]
mm_incref Character #1 rc=2 [main]
mm_alloc Character #3 size=8 [main]
  sl_alloc Character #3 size=40 class=4
mm_alloc __ManagedMemory #4 size=32 [main]
  sl_alloc __ManagedMemory #4 size=64 class=5
mm_incref __ManagedMemory #4 rc=1 [main]
mm_incref Character #3 rc=1 [main]
mm_decref Character #1 rc=1 [main]
mm_incref Character #3 rc=2 [main]
mm_decref Character #3 rc=1 [main]
mm_decref Character #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [~Character]
    mm_free __ManagedMemory #2
      sl_free __ManagedMemory #2 size=64 class=5
  mm_free Character #1
    sl_free Character #1 size=48 class=4
mm_decref Character #3 rc=0 [main]
  mm_decref __ManagedMemory #4 rc=0 [~Character]
    mm_free __ManagedMemory #4
      sl_free __ManagedMemory #4 size=64 class=5
  mm_free Character #3
    sl_free Character #3 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-char-return-transfers-ownership -->
<!-- MmTrace -->
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Character #1 size=8 [ownership-edge-cases.makeChar]
  sl_alloc Character #1 size=40 class=4
mm_alloc __ManagedMemory #2 size=32 [ownership-edge-cases.makeChar]
  sl_alloc __ManagedMemory #2 size=64 class=5
mm_incref __ManagedMemory #2 rc=1 [ownership-edge-cases.makeChar]
mm_incref Character #1 rc=1 [ownership-edge-cases.makeChar]
mm_transfer Character #1 rc=1 [ownership-edge-cases.makeChar]
mm_decref Character #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [~Character]
    mm_free __ManagedMemory #2
      sl_free __ManagedMemory #2 size=64 class=5
  mm_free Character #1
    sl_free Character #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-char-inner-block-freed -->
<!-- MmTrace -->
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Character #1 size=8 [main]
  sl_alloc Character #1 size=40 class=4
mm_alloc __ManagedMemory #2 size=32 [main]
  sl_alloc __ManagedMemory #2 size=64 class=5
mm_incref __ManagedMemory #2 rc=1 [main]
mm_incref Character #1 rc=1 [main]
mm_incref Character #1 rc=2 [main]
mm_decref Character #1 rc=1 [main]
mm_decref Character #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [~Character]
    mm_free __ManagedMemory #2
      sl_free __ManagedMemory #2 size=64 class=5
  mm_free Character #1
    sl_free Character #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-tuple-primitive-freed -->
<!-- MmTrace -->
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __Tuple_i64_i64 #1 size=16 [main]
  sl_alloc __Tuple_i64_i64 #1 size=48 class=4
mm_incref __Tuple_i64_i64 #1 rc=1 [main]
mm_decref __Tuple_i64_i64 #1 rc=0 [main]
  mm_free __Tuple_i64_i64 #1
    sl_free __Tuple_i64_i64 #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-tuple-alias-incref -->
<!-- MmTrace -->
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __Tuple_i64_i64 #1 size=16 [main]
  sl_alloc __Tuple_i64_i64 #1 size=48 class=4
mm_incref __Tuple_i64_i64 #1 rc=1 [main]
mm_incref __Tuple_i64_i64 #1 rc=2 [main]
mm_decref __Tuple_i64_i64 #1 rc=1 [main]
mm_decref __Tuple_i64_i64 #1 rc=0 [main]
  mm_free __Tuple_i64_i64 #1
    sl_free __Tuple_i64_i64 #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-tuple-reassign-decrefs-old -->
<!-- MmTrace -->
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __Tuple_i64_i64 #1 size=16 [main]
  sl_alloc __Tuple_i64_i64 #1 size=48 class=4
mm_incref __Tuple_i64_i64 #1 rc=1 [main]
mm_alloc __Tuple_i64_i64 #2 size=16 [main]
  sl_alloc __Tuple_i64_i64 #2 size=48 class=4
mm_incref __Tuple_i64_i64 #2 rc=1 [main]
mm_decref __Tuple_i64_i64 #1 rc=0 [main]
  mm_free __Tuple_i64_i64 #1
    sl_free __Tuple_i64_i64 #1 size=48 class=4
mm_incref __Tuple_i64_i64 #2 rc=2 [main]
mm_decref __Tuple_i64_i64 #2 rc=1 [main]
mm_decref __Tuple_i64_i64 #2 rc=0 [main]
  mm_free __Tuple_i64_i64 #2
    sl_free __Tuple_i64_i64 #2 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-tuple-with-string-freed -->
<!-- MmTrace -->
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc String #1 size=32 [main]
  sl_alloc String #1 size=64 class=5
mm_alloc __ManagedMemory #2 size=32 [main]
  sl_alloc __ManagedMemory #2 size=64 class=5
mm_incref __ManagedMemory #2 rc=1 [main]
mm_incref String #1 rc=1 [main]
mm_alloc __Tuple_i64_String #3 size=16 [main]
  sl_alloc __Tuple_i64_String #3 size=48 class=4
mm_incref String #1 rc=2 [main]
mm_incref __Tuple_i64_String #3 rc=1 [main]
mm_decref String #1 rc=1 [main]
mm_decref __Tuple_i64_String #3 rc=0 [main]
  mm_decref String #1 rc=0 [~__Tuple_i64_String]
    mm_decref __ManagedMemory #2 rc=0 [~String]
      mm_free __ManagedMemory #2
        sl_free __ManagedMemory #2 size=64 class=5
    mm_free String #1
      sl_free String #1 size=64 class=5
  mm_free __Tuple_i64_String #3
    sl_free __Tuple_i64_String #3 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-tuple-return-transfers-ownership -->
<!-- MmTrace -->
Returning a tuple from a function transfers ownership to the caller.
```maxon
typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	let t = makePair(a: 5, b: 3)
	return t.0 + t.1
end 'main'
```
```exitcode
8
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __Tuple_i64_i64 #1 size=16 [ownership-edge-cases.makePair]
  sl_alloc __Tuple_i64_i64 #1 size=48 class=4
mm_incref __Tuple_i64_i64 #1 rc=1 [ownership-edge-cases.makePair]
mm_transfer __Tuple_i64_i64 #1 rc=1 [ownership-edge-cases.makePair]
mm_decref __Tuple_i64_i64 #1 rc=0 [main]
  mm_free __Tuple_i64_i64 #1
    sl_free __Tuple_i64_i64 #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-tuple-destructuring-cleanup -->
<!-- MmTrace -->
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __Tuple_i64_i64 #1 size=16 [main]
  sl_alloc __Tuple_i64_i64 #1 size=48 class=4
mm_incref __Tuple_i64_i64 #1 rc=1 [main]
mm_incref __Tuple_i64_i64 #1 rc=2 [main]
mm_decref __Tuple_i64_i64 #1 rc=1 [main]
mm_decref __Tuple_i64_i64 #1 rc=0 [main]
  mm_free __Tuple_i64_i64 #1
    sl_free __Tuple_i64_i64 #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-tuple-with-struct-freed -->
<!-- MmTrace -->
A tuple containing a user-defined struct; the destructor cascades through the tuple into the struct.
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
	let t = (1, Point.create(x: 10, y: 20))
	return t.0
end 'main'
```
```exitcode
1
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Point #1 size=16 [Point.create]
  sl_alloc Point #1 size=48 class=4
mm_incref Point #1 rc=1 [Point.create]
mm_transfer Point #1 rc=1 [Point.create]
mm_alloc __Tuple_i64_Point #2 size=16 [main]
  sl_alloc __Tuple_i64_Point #2 size=48 class=4
mm_incref Point #1 rc=2 [main]
mm_incref __Tuple_i64_Point #2 rc=1 [main]
mm_decref Point #1 rc=1 [main]
mm_decref __Tuple_i64_Point #2 rc=0 [main]
  mm_decref Point #1 rc=0 [~__Tuple_i64_Point]
    mm_free Point #1
      sl_free Point #1 size=48 class=4
  mm_free __Tuple_i64_Point #2
    sl_free __Tuple_i64_Point #2 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: rc-struct-literal-as-function-arg -->
Passing a struct literal directly as a function argument must still free the struct after use. Currently leaks (exit 101).
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function acceptPoint(p Point) returns Integer
	return p.x + p.y
end 'acceptPoint'

function main() returns ExitCode
	return acceptPoint(Point.create(x: 3, y: 4))
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
	export var value Count

	function increment() returns Counter
		value = value + 1
		return self
	end 'increment'

	static function create(value Count) returns Self
		return Self{value: value}
	end 'create'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(value: 0)
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
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Data'

type Wrapper
	export var data Data

	static function create(data Data) returns Self
		return Self{data: data}
	end 'create'
end 'Wrapper'

function extractData(w Wrapper) returns Data
	return w.data
end 'extractData'

function main() returns ExitCode
	let d = Data.create(value: 42)
	let w = Wrapper.create(data: d)
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
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Outer
	export var a Inner
	export var b Integer

	static function create(a Inner, b Integer) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let x = Outer.create(a: Inner.create(value: 42), b: 10)
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
Repeatedly concatenating strings in a loop must free intermediate ManagedMemory/Buffer pairs. Each concat creates a new string; the old one must be fully freed including its managed backing storage.
```maxon
function main() returns ExitCode
	var s = ""
	let a = "x"
	var i = 0
	while i < 5 'loop'
		s = s.concat(a)
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

<!-- test: rc-array-of-structs-literal-no-leak -->
Creating an array literal of structs must not leak. All struct elements must be decreffed when the array is freed.
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

<!-- test: rc-global-array-push-local-no-leak -->
Pushing a local struct into a global array must not leak. When the global array is cleaned up, all elements (including those pushed from other function scopes) must be freed.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

var globalArr = ItemArray.create()

function pushLocal()
	let item = Item.create(value: 123)
	globalArr.push(item)
end 'pushLocal'

function main() returns ExitCode
	pushLocal()
	let elem = try globalArr.get(0) otherwise Item.create(value: -1)
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
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

var globalArr = ItemArray.create()

function main() returns ExitCode
	var i = 0
	while i < 10 'push'
		globalArr.push(Item.create(value: i))
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
		export var items IntArray
		export var value Integer

		static function create(items IntArray, value Integer) returns Self
			return Self{items: items, value: value}
		end 'create'
end 'Inner'

export type Outer
		export var inner Inner
		export var initialized bool

		static function create(inner Inner, initialized bool) returns Self
			return Self{inner: inner, initialized: initialized}
		end 'create'
end 'Outer'

function initOuter(o Outer)
		if not o.initialized 'init'
				o.inner = Inner.create(items: IntArray.create(), value: 42)
				o.initialized = true
		end 'init'
end 'initOuter'

function main() returns ExitCode
		let o = Outer.create(inner: Inner.create(items: IntArray.create(), value: 0), initialized: false)
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

function apply(f (Integer) returns String, x Integer) returns String
	return f(x)
end 'apply'

function main() returns ExitCode
	let prefix = "hello"
	let result = apply(f: (_ Integer) gives prefix, x: 0)
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
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemManagedList = __ManagedList with Item

function main() returns ExitCode
	let managedList = ItemManagedList.create()
	let node = managedList.insertFirst(Item.create(value: 50))
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
		export var x SmallInt

		static function create(x SmallInt) returns Self
			return Self{x: x}
		end 'create'
end 'Inner'

type Outer
		export var inner Inner

		static function create(inner Inner) returns Self
			return Self{inner: inner}
		end 'create'
end 'Outer'

var state = Outer.create(inner: Inner.create(x: 0))

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
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

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
	let a = [1, 2, 3]
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
export enum QueryKey
		sourceFile(path String)
		allModule
end 'QueryKey'

export type Dependency
		export var key QueryKey

		static function create(key QueryKey) returns Self
			return Self{key: key}
		end 'create'
end 'Dependency'

typealias DependencyArray = Array with Dependency

function main() returns ExitCode
		let deps = DependencyArray.create()
		deps.push(Dependency.create(key: QueryKey.sourceFile("test.maxon")))
		deps.push(Dependency.create(key: QueryKey.allModule))
		deps.push(Dependency.create(key: QueryKey.sourceFile("other.maxon")))
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
		var value Integer

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
		let m = MyKeyMap.create()
		try m.insert(key: MyKey.create(value: 1), value: 42) otherwise ignore
		return m.count()
end 'main'
```
```exitcode
1
```
