---
feature: generic-type-nested-array-typealias
status: experimental
keywords: [generics, typealias, array, nested, uses, with, monomorphization]
category: type-system
---

# Generic Type with Nested Array Typealias

## Documentation

When a generic type declares a typealias that references its type parameter (e.g., `typealias ElementArray = Array with Element`), monomorphization must correctly resolve the element size for array allocation.

## Tests

### Basic generic type with nested array typealias

<!-- test: basic-nested-array -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)
typealias SmallInt = int(0 to 100)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray
	export var name as String

	export static function create(name String) returns Self
		return Self{
			items: ElementArray.create(),
			name: name
		}
	end 'create'

	export function count() returns Count
		return self.items.count()
	end 'count'

	export function push(item Element)
		self.items.push(item)
	end 'push'
end 'Container'

typealias IntContainer = Container with SmallInt

function main() returns ExitCode
	var ic = IntContainer.create("numbers")
	ic.push(10)
	ic.push(20)
	let c = ic.count()
	if c == 2 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Generic type with string element

<!-- test: string-element -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{
			items: ElementArray.create()
		}
	end 'create'

	export function count() returns Count
		return self.items.count()
	end 'count'

	export function push(item Element)
		self.items.push(item)
	end 'push'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("hello")
	sc.push("world")
	let c = sc.count()
	if c == 2 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Forward a managed element to a consuming sibling method

A method that FORWARDS its type-parameter element argument to a consuming sibling (`add` → `store` → `self.items.push`) must promote-and-consume the managed argument at its OWN concrete call site — otherwise the borrowed String is stored into the owning array and freed by the array's decref, a double-free. The transitive feed fixpoint marks `add`'s `item` a feed because it forwards to `store`'s feed parameter, so `sc.add("alpha")` consumes exactly as `sc.store("alpha")` would.

<!-- test: forward-to-consuming-sibling -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	function store(item Element)
		self.items.push(item)
	end 'store'

	export function add(item Element)
		self.store(item)
	end 'add'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.add("alpha")
	sc.add("beta")
	return 0
end 'main'
```
```exitcode
0
```

### Multi-hop forward of a managed element

The feed fixpoint closes over a chain of any depth: `a` forwards to `b` forwards to `c` forwards to the array push, so every hop's parameter is a feed and the outermost concrete call promotes-and-consumes.

<!-- test: multi-hop-forward -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	function c(item Element)
		self.items.push(item)
	end 'c'

	function b(item Element)
		self.c(item)
	end 'b'

	export function a(item Element)
		self.b(item)
	end 'a'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.a("alpha")
	return 0
end 'main'
```
```exitcode
0
```

### Trivial-element forward is inert

The SAME forwarding shape on a TRIVIAL element (`Container with SmallInt`) generates no consume traffic — the element owns no heap, so the concrete call borrows it exactly as before the fixpoint. This pins that the transitive feed is inert for a trivial instantiation.

<!-- test: trivial-element-forward -->
```maxon
typealias ExitCode = int(0 to 125)
typealias SmallInt = int(0 to 100)
typealias Count = int(0 to u64.max)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function count() returns Count
		return self.items.count()
	end 'count'

	function store(item Element)
		self.items.push(item)
	end 'store'

	export function add(item Element)
		self.store(item)
	end 'add'
end 'Container'

typealias IntContainer = Container with SmallInt

function main() returns ExitCode
	var ic = IntContainer.create()
	ic.add(10)
	ic.add(20)
	let c = ic.count()
	if c == 2 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```


### Double move of an opaque managed element is use-after-move

A generic method that pushes the SAME type-parameter element into `self.items` twice consumes it at the first
push (the array owns it), so the second push is use-after-move — rejected E3102 rather than storing the value
into two array slots that would both free it (the double-free the guard replaces). The shared body move-tracks
the opaque element for every instantiation, so this is rejected uniformly (a type parameter is move-only — it
carries no copy).

<!-- test: double-move-is-use-after-move -->
```maxon
typealias ExitCode = int(0 to 125)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function pushTwice(item Element)
		self.items.push(item)
		self.items.push(item)
	end 'pushTwice'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.pushTwice("hello")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:19: use of moved value 'item': its ownership moved to another binding at an earlier bind or assignment
```

### Conditionally-moved managed element is dropped, not leaked

A method that pushes a consumed type-parameter element into `self.items` only on one branch leaves it LIVE on
the other. The shared body enrols the element owned and the path-sensitive join drops it once on the un-pushed
edge — through the runtime descriptor gate (`__drop_type_param` reads the instance's `destroyFunc@40`) — so the
String is freed exactly once and the false branch does not leak.

<!-- test: conditional-move-leak-free -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function pushIf(item Element, flag bool)
		if flag 'maybe'
			self.items.push(item)
		end 'maybe'
	end 'pushIf'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.pushIf("hello", flag: false)
	return 0
end 'main'
```
```exitcode
0
```

### Conditionally-moved managed element on the taken branch is owned by the array

The same method with the branch TAKEN moves the element into the array, which owns and frees it once. The join
marks it moved on the pushed edge, so no second drop is emitted.

<!-- test: conditional-move-into-array -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function pushIf(item Element, flag bool)
		if flag 'maybe'
			self.items.push(item)
		end 'maybe'
	end 'pushIf'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.pushIf("hello", flag: true)
	return 0
end 'main'
```
```exitcode
0
```

### Conditional move on a trivial element is inert

The SAME conditional-push shape on a TRIVIAL element (`Container with SmallInt`) shares the generic body's
runtime drop gate, which reads the instance's `destroyFunc@40` as 0 and destroys nothing — so an int element
left un-pushed owns no heap and the program exits 0, byte-for-byte the same shared body the managed
instantiation runs.

<!-- test: trivial-conditional-move-inert -->
```maxon
typealias ExitCode = int(0 to 125)
typealias SmallInt = int(0 to 100)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function pushIf(item Element, flag bool)
		if flag 'maybe'
			self.items.push(item)
		end 'maybe'
	end 'pushIf'
end 'Container'

typealias IntContainer = Container with SmallInt

function main() returns ExitCode
	var ic = IntContainer.create()
	ic.pushIf(10, flag: false)
	return 0
end 'main'
```
```exitcode
0
```

### Move an opaque managed element OUT with `pop`

`pop` MOVES the opaque type-parameter element out of `self.items`: the runtime nulls the vacated slot (so the
array's `__arr_decref` walk skips it) and the caller becomes the sole owner of the returned opaque word. The
shared body has no static type for the element, so the moved-out value is enrolled owned and dropped at scope
exit through the descriptor-gated `__drop_type_param` (`computeTypeDescriptorNeeds` reserves the descriptor off
the same `self.items.pop()` shape). Here `drainOne` pops one of two Strings and drops it; the container drops
the remaining String on `main`'s scope exit — each String is freed exactly once (no leak, no double-free of the
moved-out element).

<!-- test: pop-moves-opaque-element-out -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function drainOne()
		let x = try self.items.pop() otherwise return
	end 'drainOne'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("hello string long enough to force a heap allocation")
	sc.push("world string long enough to force a heap allocation")
	sc.drainOne()
	return 0
end 'main'
```
```exitcode
0
```

### Move out through a local bound to the opaque array field

The opaque array field can be aliased to a local (`let arr = self.items`, a borrow) and moved out through the
local. The descriptor-need pre-scan runs before the body is parsed and cannot resolve the receiver of a
`pop`/`remove`, so it reserves the descriptor for ANY move-out in the shared body — the owned element still
drops through the descriptor-gated `__drop_type_param` whether the receiver is the field directly or a local
bound to it. `drainViaLocal` pops one of two Strings through the local and drops it; the container drops the
survivor — each freed once.

<!-- test: pop-via-local-binding -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function drainViaLocal()
		let arr = self.items
		let x = try arr.pop() otherwise return
	end 'drainViaLocal'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("hello string long enough to force a heap allocation")
	sc.push("world string long enough to force a heap allocation")
	sc.drainViaLocal()
	return 0
end 'main'
```
```exitcode
0
```

### Read an opaque managed element with `get` (a borrow)

`get` yields a BORROW of the opaque element: the array keeps ownership, so the borrowed value is dropped by
nothing — the element stays live in the array and is freed once when the container drops. `peekCount` reads
element 0 and then reports the count, which is still 1 (the borrow did not remove it); `main` asserts that and
exits leak-free. A borrow that were mistakenly tracked owned would free the element AND leave the array's walk
to free it again — a double-free the exit-0 run rules out.

<!-- test: get-borrows-opaque-element -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function peekCount() returns Count
		let x = try self.items.get(0) otherwise return 0
		return self.items.count()
	end 'peekCount'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("a string long enough to force a heap allocation")
	let c = sc.peekCount()
	if c == 1 'stillThere'
		return 0
	end 'stillThere'
	return 1
end 'main'
```
```exitcode
0
```

### Read an opaque managed element with `first` and `last` (borrows)

`first` and `last` are borrows exactly like `get`: they hand back the opaque element without moving it out of
the array. `peekEnds` borrows both ends of a one-element array and drops nothing; the container frees the single
String once on scope exit.

<!-- test: first-last-borrow-opaque-element -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function peekEnds()
		let a = try self.items.first() otherwise return
		let b = try self.items.last() otherwise return
	end 'peekEnds'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("only element long enough to force a heap allocation")
	sc.peekEnds()
	return 0
end 'main'
```
```exitcode
0
```

### Move an opaque managed element OUT with `remove`

`remove(i)` moves the element at index `i` out exactly as `pop` moves the tail: the slot is vacated and the
returned opaque word is owned by the caller. `dropAt` removes and drops element 0 of two Strings; the container
drops the survivor on scope exit — each String freed once.

<!-- test: remove-moves-opaque-element-out -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function dropAt()
		let x = try self.items.remove(0) otherwise return
	end 'dropAt'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("first string long enough to force a heap allocation")
	sc.push("second string long enough to force a heap allocation")
	sc.dropAt()
	return 0
end 'main'
```
```exitcode
0
```

### Popping an opaque struct element drops its managed field

When the opaque element is itself a struct that owns a String, moving it out and dropping it must run the
struct's own destructor — the descriptor's `destroyFunc@40` is the struct's `__destruct_<Pair>`, which frees the
nested String. `drainOne` pops one `Pair` of two and drops it (freeing its String); the container drops the
survivor. No String leaks.

<!-- test: pop-opaque-struct-element -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Integer = int(i64.min to i64.max)

type Pair
	export var name as String
	export var value as Integer

	static function create(name String, value Integer) returns Self
		return Self{name: name, value: value}
	end 'create'
end 'Pair'

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function drainOne()
		let x = try self.items.pop() otherwise return
	end 'drainOne'
end 'Container'

typealias PairContainer = Container with Pair

function main() returns ExitCode
	var pc = PairContainer.create()
	pc.push(Pair.create("first pair string long enough for heap", value: 1))
	pc.push(Pair.create("second pair string long enough for heap", value: 2))
	pc.drainOne()
	return 0
end 'main'
```
```exitcode
0
```

### Reusing a moved-out opaque element is use-after-move

A `pop`/`remove` result is a single-owner move — a type parameter carries no copy — so moving it back into the
array and then using it again is use-after-move, rejected E3102 rather than storing the value into two slots
that would both free it. The shared body move-tracks the opaque element for every instantiation, so the reject
is uniform and needs no codegen (parse-time, so every target agrees).

<!-- test: reuse-moved-out-opaque-element -->
```maxon
typealias ExitCode = int(0 to 125)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function reinsertTwice()
		let x = try self.items.pop() otherwise return
		self.items.push(x)
		self.items.push(x)
	end 'reinsertTwice'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("a string long enough to force a heap allocation")
	sc.reinsertTwice()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:20:19: use of moved value 'x': its ownership moved to another binding at an earlier bind or assignment
```

### Pop and get on a trivial element are inert

The SAME move-out and read shapes on a TRIVIAL element (`Container with SmallInt`) share the generic body, whose
descriptor `destroyFunc@40` is 0 — so the moved-out int owns no heap and its scope-exit drop destroys nothing.
`drainOne` pops one of two ints and `peek` reads element 0; the program exits 0, byte-for-byte the same shared
body the managed instantiation runs, on every target.

<!-- test: trivial-pop-get-inert -->
```maxon
typealias ExitCode = int(0 to 125)
typealias SmallInt = int(0 to 1000)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function drainOne()
		let x = try self.items.pop() otherwise return
	end 'drainOne'

	export function peek()
		let y = try self.items.get(0) otherwise return
	end 'peek'
end 'Container'

typealias IntContainer = Container with SmallInt

function main() returns ExitCode
	var ic = IntContainer.create()
	ic.push(7)
	ic.push(9)
	ic.drainOne()
	ic.peek()
	return 0
end 'main'
```
```exitcode
0
```

### A VALUE `otherwise` on an opaque accessor is rejected

The plain borrow (`get`/`first`/`last`) and move-out (`pop`/`remove`) of an opaque element are supported with a
DIVERGING `otherwise` (`return`/`throw`/`panic`), which never merges a value at the `try` continuation. A VALUE
`otherwise <expr>` DOES merge, and reconciling ownership there (incref the borrowed element, or move/incref the
fallback) is a descriptor-gated operation the shared body cannot pick statically — the element is a raw scalar
for a trivial instantiation (a plain incref would fault) and a managed pointer for another. So a value
`otherwise` on an opaque accessor is a clean E2015 until a descriptor-gated reconciliation lands; here `pop`
supplies the owned fallback that the following `get` merges. (The concrete-element `try items.get(0) otherwise
Item.create(…)` is unaffected — its element type is known, so the incref-on-get already resolves it.)

<!-- test: opaque-accessor-value-otherwise-rejected -->
```maxon
typealias ExitCode = int(0 to 125)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function firstOrPopped()
		let owned = try self.items.pop() otherwise return
		let x = try self.items.get(0) otherwise owned
	end 'firstOrPopped'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("first string long enough to force a heap allocation")
	sc.push("second string long enough to force a heap allocation")
	sc.firstOrPopped()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:19:11: Unsupported: a `try` on an opaque type-parameter array accessor (`get`/`first`/`last`/`pop`/`remove` on an `Array with <type parameter>` field) with a VALUE `otherwise <expr>` — reconciling the borrowed/moved-out element with the fallback at the `try` continuation needs a descriptor-gated incref/copy the shared body cannot pick statically (the element is a raw scalar for a trivial instantiation and a managed pointer for another), a distinct future slice. Use a DIVERGING `otherwise` (`otherwise return`/`throw`/`panic`) instead — the plain borrow (`get`/`first`/`last`) and move-out (`pop`/`remove`) are supported that way (P1.7 slice 3b-vi-a).
```

### Returning an OWNED moved-out opaque element is rejected

`pop`/`remove` hand back an OWNED opaque element the method body drops through the descriptor-gated
`__drop_type_param`. RETURNING that owned element out of the generic method would make the CALLER its owner, but
a generic method's opaque `typeParameter` return type is not resolved to the instantiation's concrete type at
the call site, so the caller neither adopts nor drops it and the moved-out element LEAKS. Returning an opaque
`T` is a distinct future slice (symmetric to the opaque value-`otherwise` deferral); until it lands the owned
move-out must be dropped in the body or moved back into the array (`push`), so returning it is a clean E2015.
(A BORROWED opaque return — `return item` for a borrowed `Element` parameter — owns nothing and is unaffected.)

<!-- test: return-owned-opaque-element-rejected -->
```maxon
typealias ExitCode = int(0 to 125)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function takeOne() returns Element
		return try self.items.pop() otherwise panic("empty")
	end 'takeOne'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("alpha string long enough to force a heap allocation")
	let taken = sc.takeOne()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:18:3: Unsupported: returning an OWNED opaque type-parameter value — an element moved out of an `Array with <type parameter>` field by `pop`/`remove` inside a generic body. The caller cannot resolve the opaque `T` return to the instantiation's concrete type, so it would neither adopt nor drop the moved-out element and the value would leak. Returning an opaque `T` is a distinct future slice; drop the moved-out element in the method body, or move it back into the array with `push` (P1.7 slice 3b-vi-a).
```

### Clone a managed opaque array field (deep, source freed)

`.clone()` on an opaque `Array with <type parameter>` field DEEP-CLONES each element through the enclosing
instance's descriptor `copyFunc@32` (P1.7 slice 3b-vi-b-β): the shared body compiles once and reads the
element cloner at run time, so the ONE compiled `duplicate` serves every managed instantiation. `makeDuplicate`
builds a `StringContainer`, pushes two heap Strings, clones the field into a FRESH container and returns it; the
source container `a` drops at `makeDuplicate` exit, freeing ITS two Strings. If the clone were shallow (a shared
buffer or shared String pointers), the returned container's Strings would now be freed — reading its count and
then dropping it would double-free (exit 101 / a poison fault). The exit-0 run proves the clone is an
independent deep copy that outlives its source.

<!-- test: opaque-clone-source-freed -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function count() returns Count
		return self.items.count()
	end 'count'

	export function duplicate() returns Self
		return Self{ items: self.items.clone() }
	end 'duplicate'
end 'Container'

typealias StringContainer = Container with String

function makeDuplicate() returns StringContainer
	var a = StringContainer.create()
	a.push("alpha string long enough to force a heap allocation")
	a.push("beta string long enough to force a heap allocation")
	return a.duplicate()
end 'makeDuplicate'

function main() returns ExitCode
	var b = makeDuplicate()
	let c = b.count()
	if c == 2 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

### Clone a managed opaque array field with the source AND clone both live

The deep clone and its source are fully independent: both may live at once and both drop at scope exit, each
freeing its own two Strings exactly once. A shallow clone (shared element pointers) would double-free at the
second container's drop — under the always-on free-poison this is a fault or an exit-101 leak, which the exit-0
run rules out.

<!-- test: opaque-clone-both-live -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function count() returns Count
		return self.items.count()
	end 'count'

	export function duplicate() returns Self
		return Self{ items: self.items.clone() }
	end 'duplicate'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var a = StringContainer.create()
	a.push("alpha string long enough to force a heap allocation")
	a.push("beta string long enough to force a heap allocation")
	var b = a.duplicate()
	let ca = a.count()
	let cb = b.count()
	if ca == 2 'aok'
		if cb == 2 'bok'
			return 0
		end 'bok'
	end 'aok'
	return 1
end 'main'
```
```exitcode
0
```

### Slice a managed opaque array field (deep)

`.slice(start, endIndex: end)` on an opaque array field is the THROWING copy: it forwards the inner slice's
bounds check and deep-clones the sub-range through the descriptor's `copyFunc@32`. `sliceFirst` slices `[0, 1)`
into a fresh container and returns it; the source `a` (two Strings) drops at `buildSliced` exit. The one sliced
String is an independent deep copy — the exit-0 run with the source freed proves it.

<!-- test: opaque-slice-managed -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function count() returns Count
		return self.items.count()
	end 'count'

	export function sliceFirst() returns Self
		let s = try self.items.slice(0, endIndex: 1) otherwise panic("in bounds")
		return Self{ items: s }
	end 'sliceFirst'
end 'Container'

typealias StringContainer = Container with String

function buildSliced() returns StringContainer
	var a = StringContainer.create()
	a.push("slice one long enough to force a heap allocation")
	a.push("slice two long enough to force a heap allocation")
	return a.sliceFirst()
end 'buildSliced'

function main() returns ExitCode
	var sliced = buildSliced()
	let c = sliced.count()
	if c == 1 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

### Append into a managed opaque array field (deep, source preserved)

`.append(other)` on an opaque array field DEEP-CLONES `other`'s elements into the receiver, leaving `other`
untouched — so both end up sole owners of independent elements. `mergeExtra` appends the container's `extra`
field (two Strings) into its `items` field (one String); the container then owns 1 + 2 = 3 items, and both
fields drop at container drop, each freeing its own Strings exactly once. A shallow append (the appended tail
sharing `extra`'s pointers) would double-free at drop — the exit-0 run rules it out.

<!-- test: opaque-append-managed -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray
	export var extra as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create(), extra: ElementArray.create() }
	end 'create'

	export function pushItem(item Element)
		self.items.push(item)
	end 'pushItem'

	export function pushExtra(item Element)
		self.extra.push(item)
	end 'pushExtra'

	export function count() returns Count
		return self.items.count()
	end 'count'

	export function mergeExtra()
		self.items.append(self.extra)
	end 'mergeExtra'
end 'Container'

typealias StringContainer = Container with String

function buildAppended() returns StringContainer
	var dest = StringContainer.create()
	dest.pushItem("dest string long enough to force a heap allocation")
	dest.pushExtra("extra one long enough to force a heap allocation")
	dest.pushExtra("extra two long enough to force a heap allocation")
	dest.mergeExtra()
	return dest
end 'buildAppended'

function main() returns ExitCode
	var appended = buildAppended()
	let c = appended.count()
	if c == 3 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

### Clone a managed opaque array of struct-with-String elements

The element cloner reached through `copyFunc@32` may itself cascade: a `Container with Item` (where `Item` owns
a String) clones each element through the synthesized `__clone_Item`, which deep-clones the `name` String. The
struct cloner is referenced ONLY by the descriptor `copyFunc@32` relocation (the shared body names it nowhere),
so this exercises the cloner DCE-root. Source freed, clone survives — exit 0.

<!-- test: opaque-clone-struct-element -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Item
	export var name as String

	export static function create(name String) returns Self
		return Self{ name: name }
	end 'create'
end 'Item'

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function count() returns Count
		return self.items.count()
	end 'count'

	export function duplicate() returns Self
		return Self{ items: self.items.clone() }
	end 'duplicate'
end 'Container'

typealias ItemContainer = Container with Item

function makeDuplicate() returns ItemContainer
	var a = ItemContainer.create()
	a.push(Item.create("first item name long enough to force a heap allocation"))
	a.push(Item.create("second item name long enough to force a heap allocation"))
	return a.duplicate()
end 'makeDuplicate'

function main() returns ExitCode
	var b = makeDuplicate()
	let c = b.count()
	if c == 2 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

### A trivial opaque instantiation copy is inert

A TRIVIAL instantiation (`Container with SmallInt`) has a `copyFunc@32` of 0, so the opaque-copy wrapper takes
the byte-blit / COW path — a scalar element copies correctly with no cloner. The clone is inert (a plain array
copy) and leak-free on EVERY target, so this case is unrestricted (it runs on wasm too, where the managed cases
are x64-only).

<!-- test: opaque-copy-trivial-inert -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)
typealias SmallInt = int(0 to 100)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function count() returns Count
		return self.items.count()
	end 'count'

	export function duplicate() returns Self
		return Self{ items: self.items.clone() }
	end 'duplicate'
end 'Container'

typealias IntContainer = Container with SmallInt

function makeDuplicate() returns IntContainer
	var a = IntContainer.create()
	a.push(10)
	a.push(20)
	return a.duplicate()
end 'makeDuplicate'

function main() returns ExitCode
	var b = makeDuplicate()
	let c = b.count()
	if c == 2 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

### Copying an opaque array of a non-deep-cloneable instantiation is rejected

The descriptor `copyFunc@32` can hold only a SINGLE `(box) -> newBox` cloner, so an instantiation whose managed
element cannot be deep-cloned as a single-function element — a managed-element array (`Array with (Array with
String)`), whose clone needs the two-argument `__arr_clone_managed` — has no single `copyFunc`. Copying such an
opaque array in the shared body would byte-blit a managed pointer and double-free it, so the enclosing generic
type's copy method is rejected with a positioned E2015 when SOME instantiation is not single-function-cloneable.
(A DROP-only instantiation of the same shape is fine — it needs no `copyFunc` — and is covered above.)

<!-- test: opaque-copy-uncopyable-instantiation-rejected -->
```maxon
typealias ExitCode = int(0 to 125)
typealias StringArray = Array with String

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function duplicate() returns Self
		return Self{ items: self.items.clone() }
	end 'duplicate'
end 'Container'

typealias NestedContainer = Container with StringArray

function main() returns ExitCode
	var sa = StringArray.create()
	sa.push("a string long enough to force a heap allocation")
	var nc = NestedContainer.create()
	nc.push(sa)
	var dup = nc.duplicate()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:19:34: Unsupported: `clone` COPIES each element of an `Array with <type parameter>` field, but this generic type is instantiated with a type whose managed element cannot be deep-cloned as a single-function element — a managed-element array (`Array with (Array with String)`) or a non-Array generic instance (`Box with String`, whose per-instance cloner is a later slice). String / struct / boxed-union / trivial-element-array / trivial instantiations ARE supported (P1.7 slice 3b-vi-b).
```
