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
array's `__managed_decref` walk skips it) and the caller becomes the sole owner of the returned opaque word. The
shared body has no static type for the element, so the moved-out value is enrolled owned and dropped at scope
exit through the descriptor-gated `__drop_type_param` (`computeTypeDescriptorNeeds` reserves the descriptor off
the same `self.items.pop()` shape). Here `drainOne` pops one of two Strings and drops it; the container drops
the remaining String on `main`'s scope exit — each String is freed exactly once (no leak, no double-free of the
moved-out element).

<!-- test: pop-moves-opaque-element-out -->
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
		_ = try self.items.pop() otherwise return
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

The opaque array field can be aliased to a local (`var arr = self.items`) and moved out through the
local. The descriptor-need pre-scan runs before the body is parsed and cannot resolve the receiver of a
`pop`/`remove`, so it reserves the descriptor for ANY move-out in the shared body — the owned element still
drops through the descriptor-gated `__drop_type_param` whether the receiver is the field directly or a local
bound to it. `drainViaLocal` pops one of two Strings through the local and drops it; the container drops the
survivor — each freed once.

⚠ The alias must be a `var`. This case was written `let arr = self.items`, which is **not legal Maxon**:
`pop` mutates its receiver, and a mutating method on a `let` binding is E3019 in the reference compiler
(measured on the equivalent non-generic program) — shv2 simply did not enforce the rule until the
top-level-managed-`let` rung needed it. The property under test is unchanged: the move-out still goes
through a LOCAL bound to the field rather than through the field directly.

<!-- test: pop-via-local-binding -->
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
		var arr = self.items
		_ = try arr.pop() otherwise return
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

The read is a bare `try` STATEMENT rather than `_ = try …`. A container read is a PURE call, so `_ =` does not
license dropping its result (`discarded-results.md`, E3064) — a statement `try` is the spelling that reads an
element for its side effects on nothing and keeps none of it, and it is what the reference accepts. The
property under test is unchanged: element 0 is still read and its borrow still dropped by nobody.

<!-- test: get-borrows-opaque-element -->
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
		try self.items.get(0) otherwise return 0
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

	// Both ends are BOUND and read: an end borrow is a pure read, so discarding one is E3064
	// (`discarded-results.md`), and binding it exercises the same take-and-release this case exists for.
	export function peekEnds() returns Element throws ArrayError
		let front = try self.items.first()
		let back = try self.items.last()
		if self.items.count() == 1 'oneElement'
			return front
		end 'oneElement'
		return back
	end 'peekEnds'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("only element long enough to force a heap allocation")
	let seen = try sc.peekEnds() otherwise return 1
	return 0 if seen.byteLength() > 0 else 2
end 'main'
```
```exitcode
0
```

### Merge a borrowed and an owned opaque element through a ternary

The two ends disagree about OWNERSHIP: `first()` is an arm-lowered container read that yields a BORROW,
while `last()` is a corpus body whose return discharges the hand-off and gives the caller a `+1`. A ternary
joins them into ONE result phi, which has ONE drop discipline — so the borrowed edge must take a reference
of its own, through the enclosing instance's descriptor (`__retain_type_param`, whose `retainFunc@64` is
`__str_clone` here and 0 for a trivial instantiation). It is the same door a `return` of a borrowed `T`
uses, asked through the same predicate: written as an `if`/`return` pair the case above compiles, and only
the merge edge lacked an answer.

Both arms are taken and each is checked against the element it must hand back — the two-element container
returns its FRONT (the promoted borrow) and the three-element one its BACK (the already-owned edge) — and
the program exits leak-free, so neither String is freed twice and none is stranded.

<!-- test: first-last-borrow-opaque-element-ternary -->
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

	export function peekEnds() returns Element throws ArrayError
		let front = try self.items.first()
		let back = try self.items.last()
		return front if self.items.count() == 2 else back
	end 'peekEnds'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var pair = StringContainer.create()
	pair.push("front element, long enough to force a heap allocation")
	pair.push("back element, also long enough to force a heap allocation")
	let fromBorrowedArm = try pair.peekEnds() otherwise return 1

	var trio = StringContainer.create()
	trio.push("front element, long enough to force a heap allocation")
	trio.push("middle element, long enough to force a heap allocation")
	trio.push("back element, also long enough to force a heap allocation")
	let fromOwnedArm = try trio.peekEnds() otherwise return 2

	if not fromBorrowedArm.equals("front element, long enough to force a heap allocation") 'wrongFront'
		return 3
	end 'wrongFront'

	if not fromOwnedArm.equals("back element, also long enough to force a heap allocation") 'wrongBack'
		return 4
	end 'wrongBack'

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
		_ = try self.items.remove(0) otherwise return
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
		_ = try self.items.pop() otherwise return
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
		_ = try self.items.pop() otherwise return
	end 'drainOne'

	export function peek()
		try self.items.get(0) otherwise return
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

### Returning an OWNED moved-out opaque element transfers it to the caller

`pop`/`remove` hand back an OWNED opaque element. RETURNING it out of the generic method makes the CALLER its
owner, which is the same `+1` hand-off a `returns String` method makes: the body moves the element out of its
own drop sets and the caller's binding releases it at scope exit. This was a clean `E2015` until P1.7 slice
3b-vi-a, on the premise that a caller cannot resolve an opaque `T` return — it resolved it perfectly well and
then took a SECOND reference to it, which is the leak the refusal was standing in front of. The exit-0 run says
the element is released exactly once: a missed release is exit 101 and a doubled one faults on the poison.

<!-- test: return-owned-opaque-element-transfers-to-the-caller -->
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
	print("{taken}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
alpha string long enough to force a heap allocation

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

### Copying an opaque array of a NESTED-CONTAINER instantiation

⭐⭐ **G18 — THE OPAQUE HALF, AND THE ROUTE THE CONCRETE CASES CANNOT REACH.** `Container`'s shared body
compiles once against an opaque `Element` and copies each element through the enclosing instance's descriptor
`copyFunc@32`, which holds a SINGLE `(box) -> newBox`. A managed-element container element
(`Array with String`) has a 2-argument copy, so until G18 it had no `copyFunc` and the whole method was
refused; it now stamps the element's per-instance one-argument thunk
(`ProgramSignatures.managedOpaqueArrayElementCloneCallee` → `__clone_<mangled>`), whose body makes that
2-argument call. This is a DIFFERENT stamp from the concrete one
(`Parser.arrayElementCloneValue`, exercised by `array-clone-managed-elements`), which is why the case exists
here and not only there.

⚠ **THE EXIT CODE IS THE WHOLE ASSERTION, AND IT DISCRIMINATES BOTH WAYS.** The source container, its inner
array and both Strings are freed before the duplicate is read, so a byte-blitted copy double-frees them
(MEASURED as `0xC0000005` / 139 before the cloner existed) and a copy that never happened leaks (exit 101).
Only a real deep clone exits 0 — the inner row cannot be read from outside the shared body, so the answer has
to come from the memory manager rather than from a value.
<!-- test: opaque-copy-of-a-nested-container-instantiation -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Idx = int(0 to 1000)
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

	export function count() returns Idx
		return self.items.count()
	end 'count'
end 'Container'

typealias NestedContainer = Container with StringArray

function makeDuplicate() returns NestedContainer
	var sa = StringArray.create()
	sa.push("a string long enough to force a heap allocation")
	sa.push("a second string, also long enough to allocate")
	var nc = NestedContainer.create()
	nc.push(sa)
	return nc.duplicate()
	// nc, its inner array and both String records are freed when this function returns
end 'makeDuplicate'

function main() returns ExitCode
	let dup = makeDuplicate()
	print("{dup.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

### Copying an opaque array of a non-deep-cloneable instantiation is rejected

The descriptor `copyFunc@32` can hold only a SINGLE `(box) -> newBox` cloner, so an instantiation whose
managed element has NO cloner at all — an OS handle, which cannot be deep-copied by anything (duplicating one
hands two owners a descriptor whose `__mf_destruct` closes it once) — has nothing to stamp there. Copying such
an opaque array in the shared body would byte-blit a managed pointer and double-free it, so the enclosing
generic type's copy method is rejected with a positioned E2015 when SOME instantiation is not cloneable.
(A DROP-only instantiation of the same shape is fine — it needs no `copyFunc` — and is covered below.)

⛔ **THIS CASE'S ELEMENT WAS `Array with String` UNTIL G18 AND HAD TO CHANGE, WHICH IS THE POINT OF THE CASE
ABOVE.** A managed-element container is cloneable now, so the old program is the POSITIVE case and this one
needs an element the gate still refuses. The refusal has to be blamed at the `NestedContainer` line, so the
uncopyable thing must be an instance the program never WROTE (`Array with Handle`, minted by `Container`'s
inner `typealias ElementArray = Array with Element`) — a bare `typealias HandleArray = Array with
__ManagedFile` would be refused at its OWN line instead. One user struct around the handle buys both.

⚠ **THE REFUSAL IS THE LIBRARY'S SINCE ARRH STRUCK `clone` FROM THE `Array` ROSTER, AND BLAME GIVES IT
THE USER'S SPAN BACK** — `arr.clone()` is the library's own declaration now, so this program is refused by the
OPAQUE copy gate inside that body rather than by the concrete gate at the call, and the sentence printed is
the opaque one. What the refusal is POSITIONED at is the user's own instantiation, with `stdlib/Array.maxon`'s
line kept as a `note:`; `specs-shv2/array-conditional-conformance-withheld.md` explains that relocation and
the blame edge once, for all four cases ARRH touched.

<!-- test: opaque-copy-uncopyable-instantiation-rejected -->
```maxon
typealias ExitCode = int(0 to 125)

type Handle
	export var f as __ManagedFile

	export static function create(f __ManagedFile) returns Self
		return Self{f: f}
	end 'create'
end 'Handle'

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

typealias NestedContainer = Container with Handle

function main() returns ExitCode
	var nc = NestedContainer.create()
	nc.push(Handle.create(try __ManagedFile.openRead(b"DATA.BIN".managed) otherwise return 3))
	var dup = nc.duplicate()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:30:11: Unsupported: `slice` COPIES each element of an `Array with <type parameter>` field, but this generic type is instantiated with a type whose managed element cannot be deep-cloned — a compiler-owned aggregate or a base-struct-less generic instance with no runtime copy of its own (`__ManagedFile`, a `Vector`), a value held at an interface type, or a generic instance that owns one of those. String / struct / boxed-union / container (`Array with int`, `List with String`, `Array with (Array with String)`) / trivial instantiations, and a declared generic's instance whose own substituted fields are all deep-cloneable (`Box with String`), ARE supported (P1.7 slice 3b-vi-b, W162, W173, G18).
note: stdlib/Array.maxon:165:32: raised inside the library, on behalf of the construct above
```

### A DROP-only opaque struct element whose own field is not deep-cloneable compiles

`Container with Item` where `Item` is a struct that OWNS a non-deep-cloneable field (`Array with (Array with
String)`) is used DROP-ONLY here — never `.clone()`/`.slice()`/`.append()`. The `copyFunc@32` stamp and its
cloner DCE-root (`rootManagedOpaqueArrayElementClones`) synthesize the element's `__clone_<T>` UNCONDITIONALLY
for every copyable-opaque instance, including a program with no copy site. They must therefore gate on the SAME
full-graph classifier (`typeSupportsDeepClone(asElement: true)`) the copy-site reject uses — NOT the element's
top-level clone strategy: `Item` is `direct` at the top but owns a non-clonable field, so a top-level-only gate
would root a `__clone_Item` the cloner synthesizer cannot build and PANIC on this valid drop-only program
(`noteFieldCloneNeeds`). Gating on the full-graph classifier leaves `Item`'s `copyFunc@32` at 0 (never read —
the element is never copied) while its `destroyFunc@40` still drops it through `__managed_decref`, so the program
compiles and exits 0. (Adding a copy method to this same type is rejected — see the uncopyable-instantiation
test above.)

<!-- test: opaque-drop-only-uncopyable-struct-element -->
```maxon
typealias ExitCode = int(0 to 125)
typealias StringArray = Array with String
typealias RowGrid = Array with StringArray

type Item
	export var rows as RowGrid

	export static function create() returns Self
		return Self{ rows: RowGrid.create() }
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
end 'Container'

typealias ItemContainer = Container with Item

function main() returns ExitCode
	var c = ItemContainer.create()
	c.push(Item.create())
	return 0
end 'main'
```
```exitcode
0
```

### An IMPLICIT-self array mutator feeds the opaque element exactly as `self.items.push` does

`items.push(item)` and `self.items.push(item)` are the SAME store — implicit-self resolves the bare field read
to `self.items` — so the whole-program feed fact must be the same for both. Recorded off a `self`-headed token
run only, the implicit spelling left `item` unmarked: the concrete call site handed the array a BORROWED
`.rdata` String it believed it solely owned, and the array's one-shot element drop wrote a refcount into a
read-only section (`0xC0000005`). The mutator call is recognized by its own `<receiver> . push (` shape, so
neither spelling can be the one that is seen.

<!-- test: implicit-self-push-feeds-opaque-element -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function count() returns Count
		return items.count()
	end 'count'

	export function push(item Element)
		items.push(item)
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

### An implicit-self mutator feed of a HEAP element is freed once, not twice

The same implicit-self push with an INTERPOLATED (heap) String element. The `.rdata` arm above faults on the
first release because the section is read-only; a heap element takes the OTHER arm of the same missing transfer
— two `__str_decref` on one live record, a genuine double free. Both arms are the one unbalanced `+0` on the way
in, so both are pinned.

<!-- test: implicit-self-push-heap-element -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function count() returns Count
		return items.count()
	end 'count'

	export function push(item Element)
		items.push(item)
	end 'push'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	for i in 0 upto 3 'fill'
		sc.push("element number {i} of a heap-allocated string")
	end 'fill'
	let c = sc.count()
	if c == 3 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### A STATIC factory feeds an opaque element through a LOCAL array

A `static function of(first Element) returns Self` that pushes its type-parameter argument into a LOCAL
`ElementArray` and then hands that array to the returned `Self` durably stores `first`: the array owns the
element wherever the array itself lives, and the instance the static returns owns the array. The sweep once
declared that *"a `static` method has no `self`, so it contributes no direct feed"* — false, and this is the
shape that disproves it. The parameter is recorded as a callee-storage feed and enrolled owned in the shared
body, so the concrete call site transfers.

<!-- test: static-factory-push-into-local-array -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Holder uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function of(first Element) returns Self
		var xs = ElementArray.create()
		xs.push(first)
		return Self{ items: xs }
	end 'of'

	export function count() returns Count
		return items.count()
	end 'count'
end 'Holder'

typealias StringHolder = Holder with String

function main() returns ExitCode
	let h = StringHolder.of("hello")
	let c = h.count()
	if c == 1 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### A static factory feed of a HEAP element is freed once, not twice

The static-factory shape with an interpolated (heap) String element — the double-free arm of the same missing
transfer, pinned beside its `.rdata` twin above.

<!-- test: static-factory-push-heap-element -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Holder uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function of(first Element) returns Self
		var xs = ElementArray.create()
		xs.push(first)
		return Self{ items: xs }
	end 'of'

	export function count() returns Count
		return items.count()
	end 'count'
end 'Holder'

typealias StringHolder = Holder with String

function main() returns ExitCode
	let n = 7
	let h = StringHolder.of("a heap-allocated element numbered {n}")
	let c = h.count()
	if c == 1 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Pushing a POPPED opaque element back into the array frees it once

`self.items.push(self.items.pop())` moves the element OUT (the runtime nulls the vacated slot and the caller
becomes its sole owner) and straight back IN (the array becomes its sole owner again). The moved-out value is an
owned TEMPORARY, so the push must drain it from the statement's pending drops as well as poison a bare-local
source — otherwise the statement drops it once and the array's element walk drops it again, a double free on a
live record.

<!-- test: push-a-popped-opaque-element -->
```maxon
typealias ExitCode = int(0 to 125)
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

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function rotate()
		self.items.push(try self.items.pop() otherwise panic("rotate on an empty container"))
	end 'rotate'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("a string long enough to force a heap allocation")
	sc.rotate()
	let c = sc.count()
	if c == 1 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Pushing a BORROWED opaque element back into the SAME array

⛔⛔ **THIS CASE WAS A REFUSAL UNTIL W60, AND THE SENTENCE IT PINNED WAS TOO WIDE BY ONE WORD.** It read
*"an element moved into an opaque array must be a value this frame OWNS"*, and the half that is true is that
the CONTAINER must end up owning one: `self.items.get(0)` is a BORROW, so the store cannot give a reference
up and TAKES one instead, through the enclosing instance's `retainFunc@64`
(`referenceBorrowedOpaqueElement`). See `specs-shv2/generic-opaque-borrowed-element-store.md`, which owns
that rule.

The source array and the destination array are the SAME array here, which is the sharpest arithmetic the
shape offers: it ends holding two slots over one record with a refcount of two, and its own element walk
releases both. A missing retain frees it twice; an unpaired one leaks it.

<!-- test: push-borrowed-opaque-element-takes-a-reference -->
```maxon
typealias ExitCode = int(0 to 125)
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

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function duplicateFirst()
		self.items.push(try self.items.get(0) otherwise panic("empty container"))
	end 'duplicateFirst'

	export function at(i Count) returns Element throws ArrayError
		return try self.items.get(i)
	end 'at'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("a string long enough to force a heap allocation")
	sc.duplicateFirst()
	let copy = try sc.at(1) otherwise return 1
	print("{copy}\n")
	return sc.count() as ExitCode
end 'main'
```
```exitcode
2
```
```stdout
a string long enough to force a heap allocation
```

### A CONDITIONAL implicit-self feed is dropped on the un-pushed path, not leaked

The half of the fix a wrong answer cannot show: recording the feed without ENROLLING it leaves the caller
transferring a `+1` that nobody releases — a LEAK, which the process-exit gate reports as 101 rather than as a
fault. `addMaybe` pushes on one branch only, so the enrolled opaque parameter is live at the merge on the other
and the join drops it once through the descriptor-gated `__drop_type_param`. Two hundred heap elements are
allocated and none survives.

<!-- test: conditional-implicit-self-feed-leak-free -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function addMaybe(item Element, flag bool)
		if flag 'maybe'
			items.push(item)
		end 'maybe'
	end 'addMaybe'

	export function count() returns Count
		return items.count()
	end 'count'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var kept = 0
	for i in 0 upto 200 'loop'
		var sc = StringContainer.create()
		sc.addMaybe("a heap-allocated element numbered {i}", flag: false)
		kept = kept + sc.count()
	end 'loop'
	if kept == 0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### A CONDITIONAL static-factory feed is dropped on the un-pushed path, not leaked

The same leak arm with no receiver at all. A `static` whose feed can be left live at an exit reserves the layout
descriptor off its own feed fact rather than off "is this an instance method" — the descriptor-need seed asks
the feed's SINK, so the reservation and the drop are decided by the same fact and cannot disagree.

<!-- test: conditional-static-factory-feed-leak-free -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Holder uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function ofMaybe(first Element, flag bool) returns Self
		var xs = ElementArray.create()
		if flag 'maybe'
			xs.push(first)
		end 'maybe'
		return Self{ items: xs }
	end 'ofMaybe'

	export function count() returns Count
		return items.count()
	end 'count'
end 'Holder'

typealias StringHolder = Holder with String

function main() returns ExitCode
	var kept = 0
	for i in 0 upto 200 'loop'
		let h = StringHolder.ofMaybe("a heap-allocated element numbered {i}", flag: false)
		kept = kept + h.count()
	end 'loop'
	if kept == 0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Pushing a `for` element of one opaque array into another

A `for … in self.items` element is a BORROW the source array keeps and destroys. Stored into a SECOND opaque
array it takes a reference of its own, so the two arrays hold two references to one record and each releases
the one it holds — the third spelling of the borrow (`get`, an opaque field read, a `for` element), served by
the same `retainFunc@64`.

⛔⛔ **THIS CASE WAS A REFUSAL, AND ITS OWN HEADER RECORDED THAT THE REFUSAL TURNED A COMPILING PROGRAM RED.**
*(On the merge base of the rung that added it (`cad4cf30d`) the program compiled and exited 0 — not because
it was sound, but because `main` never called `copyInto`, so the unsound body was never reached.)* W60 makes
the body sound rather than unreachable, so `main` now CALLS it: without a per-element retain the two element
walks free one record twice, and with an unpaired one the copy leaks.

⭐ The destination is a second container's own `items`, because the parameter is declared
`Container.ElementArray` and an external `StringArray` is `E3005` at the call site (board row `W25`) — the
gap that made the old case uncallable, and it is a TYPE gap rather than an ownership one.

<!-- test: push-for-element-into-second-opaque-array -->
```maxon
typealias ExitCode = int(0 to 125)
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

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function copyInto(dst ElementArray)
		for e in self.items 'each'
			dst.push(e)
		end 'each'
	end 'copyInto'

	export function at(i Count) returns Element throws ArrayError
		return try self.items.get(i)
	end 'at'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("a string long enough to force a heap allocation")
	sc.push("a second string, also long enough to force a heap allocation")
	var dst = StringContainer.create()
	sc.copyInto(dst.items)
	let copied = try dst.at(1) otherwise return 1
	let original = try sc.at(1) otherwise return 1
	print("{copied}\n")
	print("{original}\n")
	return dst.count() as ExitCode
end 'main'
```
```exitcode
2
```
```stdout
a second string, also long enough to force a heap allocation
a second string, also long enough to force a heap allocation
```

### A feed handed to a BORROWING `push` that is not an array's releases what it took

The receiver of a `push`/`set`/`insert` is not resolvable when the feed sweep runs — it precedes every body
parse — so a call on a USER type whose `push` merely borrows marks its argument a feed exactly as a real array
move-in does. The parameter is then enrolled owned and nothing moves it, so it is released at the method's exit
through the descriptor-gated `__drop_type_param`. The two bodies are the same token shape one receiver TYPE
apart, so no pre-scan can separate them.

⭐⭐ **THIS CASE WAS A REFUSAL, AND IT WAS PINNING A MISSING RESERVATION RATHER THAN AN UNSOUND BODY (W58).**
The release above is perfectly well-defined; what `add` lacked was the descriptor to read the destructor out
of, because the reservation was seeded only for a method whose feed can be left LIVE and this straight-line
body's cannot. Its receiver `Sink.push(x S) returns S` is a bare type-parameter return, so `add` ADOPTS the
`+1` that hand-off owes (`specs-shv2/generic-opaque-call-result.md`) — and the seed for THAT is what now gives
`add` a descriptor. The adopted `kept` and the enrolled `item` are then released once each at the method's
exit, through the same descriptor.

⛔ **THE PREDICTION THIS CONTRADICTS IS RECORDED IN `referenceBorrowedOpaqueElement`'s HEADER, AND IT WAS TRUE WHEN
IT WAS WRITTEN.** *"Reserving the descriptor such a co-own needs turns `feed-into-borrowing-user-push-rejected`
from a clean REFUSAL into a compiling program that LEAKS (exit 101) — the descriptor it gains has a ZERO
`destroyFunc@40`."* That zero was the `destroyFunc@40`/`retainFunc@64` asymmetry, and it has since been closed
at its source (`ProgramSignatures.typeParamOwnershipProtocol` — both words off ONE protocol answer). MEASURED
on this tip: 100 adds of a heap-built element print and exit **0**, not 101. A blocker note ages faster than
the code it is about.

<!-- test: feed-into-a-borrowing-user-push-releases-what-it-took -->
```maxon
typealias ExitCode = int(0 to 125)

type Sink uses S
	export var n as ExitCode

	export static function create() returns Self
		return Self{ n: 0 }
	end 'create'

	// `Container.add` DISCARDS this result — the shape under test — so the method must have an effect, or
	// the discard is E3064 (`discarded-results.md`).
	export function push(x S) returns S
		self.n = self.n + 1
		return x
	end 'push'
end 'Sink'

type Container uses Element
	typealias ElementSink = Sink with Element

	export var sink as ElementSink

	export static function create() returns Self
		return Self{ sink: ElementSink.create() }
	end 'create'

	export function add(item Element)
		_ = self.sink.push(item)
	end 'add'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	var i = 0 as ExitCode
	while i < 100 'addMany'
		sc.add("a string number {i} long enough to force a heap allocation")
		i = i + 1
	end 'addMany'
	print("added\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
added
```

### A TYPE EXTENSION's array mutator feeds the opaque element exactly as the type's own body does

An `extension Container` on a generic type is inside that type's parameter scope, so `items.push(item)`
written there is the same store, marks the same feed and must emit the same program. It did not: the
extension body's mutator went unrecognized for the same reason the type body's implicit spelling did, and the
concrete call site handed the array a String it had not transferred (**measured on `cad4cf30d`: exit 139**;
the `self.`-spelled twin in the same body exits 0, which is how narrow the hole was).

⚠ **THE ELEMENTS ARE BUILT AT RUN TIME AND NOT WRITTEN AS LITERALS, DELIBERATELY.** A literal String can live
in `.rdata` and never be freed at all, so a missed transfer over one is silent — a case spelled that way would
pass for the wrong reason. Interpolating a loop-carried `var` gives a genuinely heap-owned record, so a missed
transfer is a double free (SIGSEGV) or an unbalanced drop (exit 101), and the run says which.

⚠ Extension scope is only pinned here for a DIRECT feed. A forwarder in an extension body
(`add(item)` → `store(item)`) still segfaults on this tip and on `cad4cf30d` alike — the transitive feed
fixpoint (`recordTransitiveArrayFeeds`) is run for a `type` declaration's body and never for an extension's —
and so is a conditional one, which is refused because `computeTypeDescriptorNeeds` is likewise never run
there. Both are filed, not closed here.

<!-- test: extension-body-push-feeds-opaque-element -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function count() returns Count
		return items.count()
	end 'count'
end 'Container'

extension Container
	export function stash(item Element)
		items.push(item)
	end 'stash'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var seed = 0
	while seed < 3 'grow'
		seed = seed + 1
	end 'grow'

	var sc = StringContainer.create()
	let owned = "heap-built element number {seed} long enough to escape any small-string envelope"
	sc.stash(owned)
	let second = "heap-built element number {seed + 1} long enough to escape any small-string envelope"
	sc.stash(second)
	if sc.count() == 2 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### A static that GAINS a forwarded feed keeps the constructor feed it already had

The transitive fixpoint republishes a method's whole consume record, and W10 made a receiverless function
reachable there for the first time — a `static` is the one kind of function scanned with
`detectFieldStores: true`, so it is the one kind carrying facts the fixpoint never computes: its plain consume
bits, and its `Self{f: p}` CONSTRUCTOR feeds. Rebuilt from the solved nodes alone, `single`'s constructor feed
vanished the moment `first` made the method a gainer, and the concrete call site then borrowed a String the
box destroys (**measured: exit 139**). Its direct-feed twin — `xs.push(first)` in place of the forward, so the
method never enters the fixpoint's publish path at all — was correct throughout, which is the giveaway: one
spelling of the same store cannot cost another parameter its ownership. The fixpoint may only turn feeds ON.

<!-- test: static-forward-keeps-its-constructor-feed -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Holder uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray
	export var single as Element

	static function fill(xs ElementArray, first Element)
		xs.push(first)
	end 'fill'

	export static function of(first Element, single Element) returns Self
		var xs = ElementArray.create()
		fill(xs, first: first)
		return Self{ items: xs, single: single }
	end 'of'

	export function count() returns Count
		return items.count()
	end 'count'
end 'Holder'

typealias StringHolder = Holder with String

function main() returns ExitCode
	var seed = 0
	while seed < 3 'grow'
		seed = seed + 1
	end 'grow'

	let h = StringHolder.of("forwarded element number {seed} long enough to escape the envelope", single: "constructed element number {seed} long enough to escape the envelope")
	if h.count() == 1 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### The same static WITHOUT the forward — the control that was always green

The direct-feed spelling of the case above: `of` pushes into the local array itself rather than through
`fill`, so it never becomes a gainer and the fixpoint never republishes it. It passes on `cad4cf30d`'s
successor and on this tip, and it is what makes the case above a REGRESSION rather than a missing feature —
the two programs differ only in which method spells the push.

<!-- test: static-direct-feed-keeps-its-constructor-feed -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Holder uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray
	export var single as Element

	export static function of(first Element, single Element) returns Self
		var xs = ElementArray.create()
		xs.push(first)
		return Self{ items: xs, single: single }
	end 'of'

	export function count() returns Count
		return items.count()
	end 'count'
end 'Holder'

typealias StringHolder = Holder with String

function main() returns ExitCode
	var seed = 0
	while seed < 3 'grow'
		seed = seed + 1
	end 'grow'

	let h = StringHolder.of("pushed element number {seed} long enough to escape the envelope", single: "constructed element number {seed} long enough to escape the envelope")
	if h.count() == 1 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### An UNCONDITIONAL extension on a `where`-constrained generic type carries the type's witnesses

⭐⭐ **THE EXTENSION BODY RESERVES ONE WITNESS LIST AND THE CALL SITE SUPPLIES ANOTHER, AND THAT IS A
SEGFAULT ON A LEGAL PROGRAM (W23).** `Parser.openTypeExtensionBodyScope` opened the body with the
EXTENSION's own `where` clause, which for an unconditional `extension Pair` is EMPTY — so `bothAre`
reserved **0** hidden witness parameters — while `ProgramSignatures.witnessConstraintsOfMethod` fell back
to the TYPE's `where A is Equatable, B is Equatable` and every caller duly passed **2**. A witness-slot
count is ABI, so the two disagreeing is an argument-register mismatch, not a type error: **MEASURED on the
merge base, this program compiled clean (no diagnostic, 2,646 bytes of code) and the binary SEGFAULTED
after printing `a=true`.** The oracle prints all three lines and exits 0.

⚠ **THE BODY HAS TWO DOORS TO A WITNESS AND ONLY ONE WAS GUARDED.** A bare `self.first == x` written in
the same body is a clean refusal (`E3005`, "requires type parameter 'A' to be constrained"); routing the
identical dispatch through `self.firstIs(x)` — a call, not an operator — compiled and crashed. The cure is
that BOTH the reservation and the supply read one derived list (`witnessConstraintsOfMethod`, the type's
constraints then the extension's extras), not that a third check is added at the method-call door.

<!-- test: unconditional-extension-on-constrained-type-dispatches-a-witness -->
```maxon
typealias Num = int(i64.min to i64.max)

type Pair uses A, B where A is Equatable, B is Equatable
	export var first as A
	export var second as B
	export static function create(a A, b B) returns Self
		return Self{first: a, second: b}
	end 'create'
	export function firstIs(x A) returns bool
		return self.first == x
	end 'firstIs'
end 'Pair'

extension Pair
	export function bothAre(x A) returns bool
		return self.firstIs(x)
	end 'bothAre'
end 'Pair'

typealias NumPair = Pair with (Num, Num)

function main() returns ExitCode
	let p = NumPair.create(1, b: 2)
	if p.firstIs(1) 'a'
		print("a=true\n")
	end 'a'
	if p.bothAre(1) 'c'
		print("both=true\n")
	end 'c'
	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=true
both=true
done
```

### The same, with TWO witnesses actually dereferenced through the unconditional extension

The case above proves the slot COUNT; this one proves the ORDER, because both hidden witnesses are
followed to a real `equals` impl. With the extension's empty clause chosen, `bothAre` reserved nothing and
read its two forwarded slots off whatever the argument registers happened to hold — **MEASURED: exit 139
after `a=true`**, the same shape as the single-witness case, which is what says the count and the order are
one defect and not two.

<!-- test: unconditional-extension-forwards-two-witnesses-in-order -->
```maxon
typealias Num = int(i64.min to i64.max)

type Pair uses A, B where A is Equatable, B is Equatable
	export var first as A
	export var second as B
	export static function create(a A, b B) returns Self
		return Self{first: a, second: b}
	end 'create'
	export function firstIs(x A) returns bool
		return self.first == x
	end 'firstIs'
	export function secondIs(y B) returns bool
		return self.second == y
	end 'secondIs'
end 'Pair'

extension Pair
	export function bothAre(x A, y B) returns bool
		if self.firstIs(x) 'f'
			return self.secondIs(y)
		end 'f'
		return false
	end 'bothAre'
end 'Pair'

typealias NumPair = Pair with (Num, Num)

function main() returns ExitCode
	let p = NumPair.create(1, b: 2)
	if p.firstIs(1) 'a'
		print("a=true\n")
	end 'a'
	if p.bothAre(1, y: 2) 'c'
		print("both=true\n")
	end 'c'
	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=true
both=true
done
```

### An extension clause DISJOINT from the type's own is a UNION, not a replacement

⛔ **THE REPLACE-RULE'S WORST FORM: the extension's `where A is Comparable` DISPLACED the type's
`where A is Equatable, B is Equatable` entirely, so the body reserved ONE witness where its callee wanted
TWO — and the compiler did not survive its own IR.** MEASURED on the merge base: **`panic at
RegisterAllocator.maxon:1913: colorOpForward: use of value 3 before it was colored`**, which is not the
allocator's defect at all. The one-variable control is the SUPERSET case below, which differs in the
extension's clause ALONE and compiled clean on the same binary.

Under the union the body carries `[A is Equatable, B is Equatable, A is Comparable]` — the type's list
FIRST, in declaration order, then the extension's extras — so `self.firstIs(x)`, whose own list is exactly
the type's, forwards slots 0 and 1 and finds them index-aligned by construction.

<!-- test: extension-clause-disjoint-from-the-types-own -->
```maxon
typealias Num = int(i64.min to i64.max)

type Pair uses A, B where A is Equatable, B is Equatable
	export var first as A
	export var second as B
	export static function create(a A, b B) returns Self
		return Self{first: a, second: b}
	end 'create'
	export function firstIs(x A) returns bool
		return self.first == x
	end 'firstIs'
end 'Pair'

extension Pair where A is Comparable
	export function firstIsVia(x A) returns bool
		return self.firstIs(x)
	end 'firstIsVia'
end 'Pair'

typealias NumPair = Pair with (Num, Num)

function main() returns ExitCode
	let p = NumPair.create(1, b: 2)
	if p.firstIs(1) 'a'
		print("a=true\n")
	end 'a'
	if p.firstIsVia(1) 'c'
		print("via=true\n")
	end 'c'
	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=true
via=true
done
```

### The extension declared BEFORE the type it extends — the ORDER-INDEPENDENCE proof

⭐⭐ **A WITNESS-SLOT COUNT IS ABI, SO IT MAY NOT BE DECIDED BY WHICH DECLARATION THE COMPILER MET FIRST.**
The union is taken where the whole-program struct registry is already complete — `foldExtensionDeclarations`
runs after every file's declarations are folded, which is why `readExtensionHeader` can ask
`extensionTargetOf` about a type declared later at all — so the answer cannot depend on declaration order.
This case is the twin of the disjoint-clause case above with the two declarations exchanged, and it is the
case that fails first if the union is ever computed from a partially folded registry.

<!-- test: extension-declared-before-the-type-it-extends -->
```maxon
typealias Num = int(i64.min to i64.max)

extension Pair where A is Comparable
	export function firstIsVia(x A) returns bool
		return self.firstIs(x)
	end 'firstIsVia'
end 'Pair'

type Pair uses A, B where A is Equatable, B is Equatable
	export var first as A
	export var second as B
	export static function create(a A, b B) returns Self
		return Self{first: a, second: b}
	end 'create'
	export function firstIs(x A) returns bool
		return self.first == x
	end 'firstIs'
end 'Pair'

typealias NumPair = Pair with (Num, Num)

function main() returns ExitCode
	let p = NumPair.create(1, b: 2)
	if p.firstIs(1) 'a'
		print("a=true\n")
	end 'a'
	if p.firstIsVia(1) 'c'
		print("via=true\n")
	end 'c'
	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=true
via=true
done
```

### CONTROL — an extension clause EQUAL to the type's own

The one shape the replace-rule was accidentally right for, kept as a control so the union cannot regress
it: replacing a list with a copy of itself is the identity, and so is unioning it with itself. It was green
on the merge base and it stays green.

<!-- test: extension-clause-equal-to-the-types-own -->
```maxon
typealias Num = int(i64.min to i64.max)

type Pair uses A, B where A is Equatable, B is Equatable
	export var first as A
	export var second as B
	export static function create(a A, b B) returns Self
		return Self{first: a, second: b}
	end 'create'
	export function firstIs(x A) returns bool
		return self.first == x
	end 'firstIs'
end 'Pair'

extension Pair where A is Equatable, B is Equatable
	export function bothAre(x A) returns bool
		return self.firstIs(x)
	end 'bothAre'
end 'Pair'

typealias NumPair = Pair with (Num, Num)

function main() returns ExitCode
	let p = NumPair.create(1, b: 2)
	if p.firstIs(1) 'a'
		print("a=true\n")
	end 'a'
	if p.bothAre(1) 'c'
		print("both=true\n")
	end 'c'
	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=true
both=true
done
```

### CONTROL — an extension clause that is a SUPERSET of the type's own

⚠ **THIS ONE WAS GREEN BY LUCK, AND THE UNION IS WHAT MAKES IT GREEN BY CONSTRUCTION.** Under the
replace-rule the body carried `[A is Equatable, A is Comparable, B is Equatable]` — the clause's own source
order — while `firstIs` carried the type's `[A is Equatable, B is Equatable]`, so the forward of slot 1
handed `firstIs` the `A is Comparable` table where the `B is Equatable` one belonged. The program only ever
dereferences slot 0, so the wrong table at slot 1 was never followed and the run said nothing. Under the
union the body carries `[A is Equatable, B is Equatable, A is Comparable]` and every prefix aligns.

<!-- test: extension-clause-superset-of-the-types-own -->
```maxon
typealias Num = int(i64.min to i64.max)

type Pair uses A, B where A is Equatable, B is Equatable
	export var first as A
	export var second as B
	export static function create(a A, b B) returns Self
		return Self{first: a, second: b}
	end 'create'
	export function firstIs(x A) returns bool
		return self.first == x
	end 'firstIs'
end 'Pair'

extension Pair where A is Equatable and Comparable, B is Equatable
	export function firstIsVia(x A) returns bool
		return self.firstIs(x)
	end 'firstIsVia'
end 'Pair'

typealias NumPair = Pair with (Num, Num)

function main() returns ExitCode
	let p = NumPair.create(1, b: 2)
	if p.firstIs(1) 'a'
		print("a=true\n")
	end 'a'
	if p.firstIsVia(1) 'c'
		print("via=true\n")
	end 'c'
	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=true
via=true
done
```

### CONTROL — an unconditional extension whose body dispatches NO witness

The other half of the trigger: a body that reserves the wrong number of witness slots is harmless while it
never forwards one. Green on the merge base, and it stays green — which is what pins that the union changed
the ABI of the methods that need it and of no others.

<!-- test: unconditional-extension-body-that-dispatches-no-witness -->
```maxon
typealias Num = int(i64.min to i64.max)

type Pair uses A, B where A is Equatable, B is Equatable
	export var first as A
	export var second as B
	export static function create(a A, b B) returns Self
		return Self{first: a, second: b}
	end 'create'
	export function firstIs(x A) returns bool
		return self.first == x
	end 'firstIs'
end 'Pair'

extension Pair
	export function firstCopy() returns A
		return self.first
	end 'firstCopy'
end 'Pair'

typealias NumPair = Pair with (Num, Num)

function main() returns ExitCode
	let p = NumPair.create(1, b: 2)
	if p.firstIs(1) 'a'
		print("a=true\n")
	end 'a'
	if p.firstCopy() == 1 'c'
		print("copy=true\n")
	end 'c'
	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=true
copy=true
done
```

### An INTERFACE extension's body self-calling a descriptor-needing method of a GENERIC conformer

⭐⭐ **THE THIRD FACE OF THE RESERVE/SUPPLY DRIFT, AND THE ONE THAT ABORTS THE COMPILER (W23c).** The
layout-descriptor need was a fixpoint over ONE type declaration's own body — `computeTypeDescriptorNeeds`
ran from `parseTypeDeclaration` and from nowhere else — so no method contributed by an `extension` could
ever reserve the slot. `Bag.absorb` copies an opaque array and reserves a descriptor; `Bag.absorbTwice`,
declared in an `extension Appendable`, calls it on `self` and reserves nothing. **MEASURED on the merge
base: `panic at LowerMaxonToStd.maxon:1587: forwardCallerLayout: caller 'Bag.absorbTwice' has no layout
descriptor to forward to 'Bag.absorb'`**, no position, no `error E….` — on a program the oracle compiles
and runs to exit 0.

⚠ An INTERFACE extension is the sharpest form of it: its body is monomorphized per conformer, and
`openExtensionBodyScope` deliberately leaves `enclosingTypeParams` EMPTY there (its own header argues why —
an associated type is bound concretely per conformer and must not resolve as the conformer's type
parameter). So the reservation gate, which tested exactly that field, could not even ask the question. The
need is now a WHOLE-PROGRAM fixpoint keyed by qualified method name, and `Bag.absorbTwice`'s self-call edge
to `Bag.absorb` is an edge like any other.

<!-- test: interface-extension-body-forwards-a-descriptor -->
```maxon
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

interface Appendable uses Func
	function opsCount() returns Count
	function absorb(other Func)
end 'Appendable'

extension Appendable
	export function absorbTwice(a Func, b Func)
		self.absorb(a)
		self.absorb(b)
	end 'absorbTwice'
end 'Appendable'

type Bag uses Op implements Appendable with Integer
	typealias OpArray = Array with Op
	export var ops as OpArray

	export static function create() returns Self
		return Self{ops: OpArray.create()}
	end 'create'

	export function opsCount() returns Count
		return self.ops.count()
	end 'opsCount'

	export function absorb(other Integer)
		self.ops.append(self.ops)
		_ = other
	end 'absorb'
end 'Bag'

typealias IntBag = Bag with Integer

function main() returns ExitCode
	var a = IntBag.create()
	a.absorbTwice(1, b: 2)
	if a.opsCount() == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

### A CONDITIONAL feed inside a TYPE extension — the regression the W10 note recorded and could not close

⛔ **`functionCarriesLayoutParam`'s own header carried this as a MEASURED regression against `cad4cf30d`,
filed rather than closed**: a conditional `items.push(item)` inside an `extension Container` on a generic
type compiled and exited 0 on the predecessor and was REFUSED on the tip. The refusal is W10's residual
check firing correctly — the body drops an opaque feed at the guarded exit and the function has no
descriptor to drop it through — but the half that was actually missing is the RESERVATION: no extension
method could reserve the slot at all. With the need computed whole-program, `Container.stashIf` reserves it
and the program compiles again.

⚠ It is deliberately the CONDITIONAL form. The straight-line twin
(`extension-body-push-feeds-opaque-element`, above) always consumes its feed and needs no descriptor, which
is why that one was green throughout and this one was not — the guard is the whole difference.

<!-- test: conditional-feed-inside-a-type-extension -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function count() returns Count
		return items.count()
	end 'count'
end 'Container'

extension Container
	export function stashIf(item Element, keep bool)
		if keep 'wanted'
			items.push(item)
		end 'wanted'
	end 'stashIf'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var seed = 0
	while seed < 3 'grow'
		seed = seed + 1
	end 'grow'

	var sc = StringContainer.create()
	sc.stashIf("heap-built element number {seed} long enough to escape any small-string envelope", keep: true)
	sc.stashIf("heap-built element number {seed + 1} long enough to escape any small-string envelope", keep: false)
	if sc.count() == 1 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### The extension declared BEFORE the type it extends, IN ANOTHER FILE — the descriptor's order proof

⭐⭐ **A DESCRIPTOR PARAMETER IS ABI, SO IT MAY NOT BE DECIDED BY WHICH DECLARATION THE COMPILER MET
FIRST** — the same demand the witness union answers one mechanism over, and a sharper one here, because the
old fixpoint was *structurally* order-bound: it ran at the moment `parseTypeDeclaration` opened a body, from
that body's own tokens, so a method declared anywhere else could not be in it whatever the order. The need
is now solved once, after `ProgramSignatures.allFilesFolded`, over every generic type body AND every
extension body in the program — so this case, whose extension precedes its type and sits in a file the
sweep reaches first, reserves exactly what the same program written the other way round reserves.

<!-- test: cross-file-extension-before-its-type-reserves-a-descriptor -->
```maxon
// --- file: a_ext.maxon
extension Vault
	export function stashIf(item Element, keep bool)
		if keep 'wanted'
			items.push(item)
		end 'wanted'
	end 'stashIf'
end 'Vault'
// --- file: b_type.maxon
public typealias Count = int(0 to u64.max)

export type Vault uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function count() returns Count
		return items.count()
	end 'count'
end 'Vault'
// --- file: main.maxon
typealias StringVault = Vault with String

function main() returns ExitCode
	var seed = 0
	while seed < 3 'grow'
		seed = seed + 1
	end 'grow'

	var sv = StringVault.create()
	sv.stashIf("heap-built element number {seed} long enough to escape any small-string envelope", keep: true)
	sv.stashIf("heap-built element number {seed + 1} long enough to escape any small-string envelope", keep: false)
	if sv.count() == 1 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### A CONDITIONAL extension WITHHELD from a conformer contributes NOTHING to the descriptor fixpoint

⛔⛔ **THE WHOLE-PROGRAM FIXPOINT SCANNED EXTENSION BODIES THE CONFORMER NEVER RECEIVES, AND ONE OF THEIR
GHOST METHOD NAMES ERASED A REAL METHOD'S ABI SLOT (found at review).** `foldExtensionDeclarationInto`
recorded a `DescriptorScanSite` for every generic conformer *before* `extensionConformerVerdict` was taken,
so a `where Item is Comparable` extension withheld from `Bag` was still walked in `Bag`'s scope and still
filed a `Bag.slotSize` seed — for a method `Bag` does not have. The fixpoint's columns are keyed by that
name alone, so the ghost's `false` replaced the real `Bag.slotSize`'s `true`, the method reserved no layout
descriptor, and its own `sizeof(T)` had nothing to read: **MEASURED on this rung's tip, `panic at
LowerMaxonToStd.maxon:1644: lowerSizeofType: sizeof(T) in 'Bag.slotSize' but the function carries no layout
descriptor parameter`**, with no position and no `error E….`, on a program the MERGE BASE and the oracle
both compile and run (printing `1`).

⚠ **IT CANNOT BE CAUGHT AS A DUPLICATE DECLARATION.** A withheld method publishes no signature at all —
`foldWithheldExtensionMethod` files a `ConditionalExtensionSkip` instead — so E3006 never sees two `Bag.slotSize`
and there is no diagnostic anywhere between the two names colliding and the compiler aborting. The gate is the
VERDICT: a conformer the extension is withheld from is not scanned, because those methods are not its.

<!-- test: a-withheld-conditional-extension-scans-no-descriptor-site -->
```maxon
typealias Num = int(i64.min to i64.max)

interface Holder uses Item
	function get() returns Item
end 'Holder'

extension Holder where Item is Comparable
	function slotSize() returns Num
		return 1
	end 'slotSize'
end 'Holder'

type Plain
	export var x as Num

	export static function create(x Num) returns Self
		return Self{x: x}
	end 'create'
end 'Plain'

type Bag uses T implements Holder with Plain
	export var value as T
	export var tag as Plain

	export static function create(v T, tag Plain) returns Self
		return Self{value: v, tag: tag}
	end 'create'

	export function get() returns Plain
		return self.tag
	end 'get'

	export function slotSize() returns Num
		return sizeof(T)
	end 'slotSize'
end 'Bag'

typealias NumBag = Bag with Num

function main() returns ExitCode
	let b = NumBag.create(7, tag: Plain.create(1))
	print("{b.get().x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

### TWO METHOD DECLARATIONS WEARING ONE NAME MERGE THEIR SEEDS — the last one in is not the answer

⛔ **THE FIXPOINT'S COLUMNS ARE KEYED BY `Type.method` AND `upsert` MADE THE LAST WRITER THE WHOLE ANSWER**
(found at review; PRE-EXISTING — the merge base panics identically, with the key merely spelled bare).
`Bag` declares `slotSize()` twice; shv2 keeps the FIRST signature and resolves `b.slotSize()` to it, so the
body that runs is the one reading `sizeof(T)` — while the seed recorded for `Bag.slotSize` was the SECOND
declaration's `false`. **MEASURED: `panic at LowerMaxonToStd.maxon:1644: lowerSizeofType: sizeof(T) in
'Bag.slotSize' but the function carries no layout descriptor parameter`**, on a program the oracle compiles
and runs (printing `8`).

⭐ **THE MERGE IS A UNION, AND THE DIRECTION IS FORCED BY THE ASYMMETRY OF BEING WRONG.** Reserving a
descriptor no body reads costs one ignored parameter and the supply side still agrees, because it reads the
reservation itself (`LowerMaxonToStd.buildLayoutNeedingFuncs` counts emitted params). NOT reserving one a
body does read has no error path — it aborts the compiler. So `solveDescriptorNeeds`' claim that same-named
methods "share one answer" is now MADE true where the columns are filled, rather than assumed.

<!-- test: two-method-declarations-of-one-name-merge-their-descriptor-seeds -->
```maxon
typealias Num = int(i64.min to i64.max)

type Bag uses T
	export var value as T

	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'

	export function slotSize() returns Num
		return sizeof(T)
	end 'slotSize'

	export function slotSize(scale Num) returns Num
		return scale
	end 'slotSize'
end 'Bag'

typealias NumBag = Bag with Num

function main() returns ExitCode
	let b = NumBag.create(7)
	print("{b.slotSize()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8
```

### Forwarding a BORROWED opaque element to a sibling that stores it takes a reference for it

⛔ **This is the `Map.grow()` double free in its smallest form, and it once COMPILED.** The sibling's
parameter is a callee-storage feed: it is enrolled OWNED and moved into durable storage, so the callee's
whole ownership story rests on the caller having handed over a reference. A borrowed element handed over at
that position is a reference nobody transferred — freed once by the sibling's container and once by the
container it was read out of.

The caller is the only frame that can see the borrow, so that is where the reference is taken
(`handleEscapingBorrowFeed`), through the enclosing instance's layout descriptor.

⭐⭐ **THIS CASE WAS A REFUSAL, AND WHAT IT WAS REALLY PINNING WAS A MISSING RESERVATION (W58).** `relay`
reserved no descriptor, so the reference it owed had nothing to be taken through and the body was refused
with a position. Its own paragraph said what was missing — *"a descriptor seed for 'forwards a borrowed
opaque value to a storing sibling'"* — and named the reason no seed could exist: the forwarded value is an
EXPRESSION, so no feed scan records it. The seed that closes it does not look at the forward at all. It
looks at the CALL the expression is: `items.get(i)` names a method some declaration in the program returns
a bare type parameter from, and a body that calls one ADOPTS the `+1` it hands back
(`ProgramSignatures.methodNameEverReturnsBareTypeParameter`). `relay` therefore carries a descriptor for
its own adopted result, and the reference it owes the forward is taken through the same one.

⚠ **MEASURED, and against the runnable oracle**, because a refusal traded for a leak would be strictly
worse than the refusal: 100 relays of one element print `spare 100` and exit **0** under both `maxon-shv2`
and the C# bootstrap. The loop is what makes that reading worth having — a per-call leak would be a hundred
records rather than one, and a doubled release faults on the poison byte.

<!-- test: a-borrowed-opaque-element-forwarded-to-a-storing-sibling-is-referenced -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Idx = int(0 to u64.max)

type GHolder uses T
	typealias TArray = Array with T

	export var items as TArray
	export var spare as TArray

	export static function create() returns Self
		return Self{ items: TArray.create(), spare: TArray.create() }
	end 'create'

	export function seed(s T)
		items.push(s)
	end 'seed'

	function stash(v T)
		spare.push(v)
	end 'stash'

	export function relay(i Idx)
		stash(try items.get(i) otherwise panic("out of range"))
	end 'relay'

	export function spareCount() returns Idx
		return spare.count()
	end 'spareCount'
end 'GHolder'

typealias StringHolder = GHolder with String

function main() returns ExitCode
	var h = StringHolder.create()
	h.seed("a value long enough to force a heap allocation")
	var n = 0 as Idx
	while n < 100 'relayMany'
		h.relay(0)
		n = n + 1
	end 'relayMany'
	print("spare {h.spareCount()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
spare 100
```
