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
