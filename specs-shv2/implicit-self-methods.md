---
feature: implicit-self-methods
status: experimental
keywords: [type, method, self, implicit, bare call, static, instance, sibling]
category: type-system
---

# Implicit-self method resolution

## Documentation

Inside a type's method body a bare call `foo(...)` — no `self.` and no `Type.`
qualifier — resolves to the sibling method `foo` of the enclosing type, exactly
as a bare field name resolves to `self.<field>`.

- An **instance** sibling receives the enclosing `self` at parameter 0, so a bare
  `bump()` inside `Counter.twice` means `self.bump()`.
- A **static** sibling receives no `self`: a bare `mk()` resolves to `Type.mk()`.
- The enclosing type's method takes **precedence** over a free function of the
  same name.
- Resolution is by name across the whole type body, so a bare call may name a
  method declared **later** in the type, or the method **itself** (recursion).

A bare name that is not a method of the enclosing type stays a free/direct call
(`E3004` when it names nothing declared). A `static` method has no `self`, so a
bare call there to an **instance** sibling has no receiver to prepend and fails
the arity check on the hidden `self` parameter (`E3036`).

## Tests

<!-- test: sibling-instance-method -->
An instance method calls a sibling instance method with a bare call; `self` is
prepended. `bump()` reads `n + 1 = 1`, so `twice()` is `1 + 1 = 2`.
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump() returns Integer
		return n + 1
	end 'bump'

	function twice() returns Integer
		return bump() + bump()
	end 'twice'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create()
	return c.twice()
end 'main'
```
```exitcode
2
```

<!-- test: recursion -->
A method calls ITSELF with a bare call. `sumTo(3)` is `3 + 2 + 1 + 0 = 6`.
```maxon

typealias Integer = int(i64.min to i64.max)

type Rec
	export var seed as Integer

	static function create() returns Self
		return Self{seed: 0}
	end 'create'

	function sumTo(k Integer) returns Integer
		if k <= 0 'base'
			return 0
		end 'base'
		return k + sumTo(k - 1)
	end 'sumTo'
end 'Rec'

function main() returns ExitCode
	let r = Rec.create()
	return r.sumTo(3)
end 'main'
```
```exitcode
6
```

<!-- test: forward-reference -->
A bare call names a sibling declared LATER in the same type body — the whole
type's method set is known before any body resolves. `first()` is `second() + 1 = 11`.
```maxon

typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function first() returns Integer
		return second() + 1
	end 'first'

	function second() returns Integer
		return 10
	end 'second'
end 'Box'

function main() returns ExitCode
	let b = Box.create()
	return b.first()
end 'main'
```
```exitcode
11
```

<!-- test: bare-call-with-args -->
A bare sibling call carries arguments. `add(x)` is `base + x`; `addTwice(3)` is
`(1 + 3) + (1 + 3) = 8`.
```maxon

typealias Integer = int(i64.min to i64.max)

type Adder
	export var base as Integer

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'

	function add(x Integer) returns Integer
		return base + x
	end 'add'

	function addTwice(x Integer) returns Integer
		return add(x) + add(x)
	end 'addTwice'
end 'Adder'

function main() returns ExitCode
	let a = Adder.create(1)
	return a.addTwice(3)
end 'main'
```
```exitcode
8
```

<!-- test: bare-static-call-gets-no-self -->
A bare call to a STATIC sibling from an instance method gets NO `self` — it
resolves to `Type.mk()`. `mk()` returns 7.
```maxon

typealias Integer = int(i64.min to i64.max)

type Maker
	export var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	static function mk() returns Integer
		return 7
	end 'mk'

	function useIt() returns Integer
		return mk()
	end 'useIt'
end 'Maker'

function main() returns ExitCode
	let m = Maker.create()
	return m.useIt()
end 'main'
```
```exitcode
7
```

<!-- test: bare-call-inside-static-method -->
A bare call inside a STATIC method resolves to a sibling static method (no `self`
exists to prepend). `build()` is `mk() + 1 = 8`.
```maxon

typealias Integer = int(i64.min to i64.max)

type Maker
	export var n as Integer

	static function mk() returns Integer
		return 7
	end 'mk'

	static function build() returns Integer
		return mk() + 1
	end 'build'
end 'Maker'

function main() returns ExitCode
	return Maker.build()
end 'main'
```
```exitcode
8
```

<!-- test: method-wins-over-free-function -->
When a free function and an instance method share a name, the bare call inside
the type resolves to the METHOD. The free `val()` returns 10, the method `val()`
returns 3; `get()` calls the method and returns 3.
```maxon

typealias Integer = int(i64.min to i64.max)

function val() returns Integer
	return 10
end 'val'

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function val() returns Integer
		return 3
	end 'val'

	function get() returns Integer
		return val()
	end 'get'
end 'Box'

function main() returns ExitCode
	let b = Box.create()
	return b.get()
end 'main'
```
```exitcode
3
```

<!-- test: free-function-still-resolves -->
A bare call to a name that is NOT a method of the enclosing type stays a free
call. `Box` has no `helper`, so `helper()` resolves to the free function (5), and
`get()` returns `5 + 1 = 6`.
```maxon

typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
	return 5
end 'helper'

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function get() returns Integer
		return helper() + 1
	end 'get'
end 'Box'

function main() returns ExitCode
	let b = Box.create()
	return b.get()
end 'main'
```
```exitcode
6
```

<!-- test: inline-ternary-before-later-sibling -->
Regression: an inline ternary `1 if c else 2` inside a method body must not be
counted as a block opener by the sibling-method walk, or a sibling declared after
it fails to register. `n` is 0, so `r` is 2; `pick()` is `2 + later() = 11`.
```maxon

typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function pick() returns Integer
		let r = 1 if n > 0 else 2
		return r + later()
	end 'pick'

	function later() returns Integer
		return 9
	end 'later'
end 'Box'

function main() returns ExitCode
	let b = Box.create()
	return b.pick()
end 'main'
```
```exitcode
11
```

<!-- test: if-else-block-before-later-sibling -->
Regression: a statement `if ... end ... else ... end` block inside a method body
balances in the sibling-method walk, so a sibling declared after it still
registers. `branchy(1)` takes the positive arm: `helper() + 1 = 6`.
```maxon

typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function branchy(x Integer) returns Integer
		if x > 0 'pos'
			return helper() + 1
		end 'pos' else 'neg'
			return helper()
		end 'neg'
	end 'branchy'

	function helper() returns Integer
		return 5
	end 'helper'
end 'Box'

function main() returns ExitCode
	let b = Box.create()
	return b.branchy(1)
end 'main'
```
```exitcode
6
```

<!-- test: generic-descriptor-forwarding-through-bare-call -->
A generic type: an instance method reaches a `sizeof(T)`-reading sibling through
a BARE call. The layout descriptor must forward through the implicit-self call
exactly as it does through an explicit `self.typeSize()`. `sizeof(bool)` is 1.
```maxon

typealias Integer = int(i64.min to i64.max)

type Sizer uses T
	export var dummy as T

	export static function create(dummy T) returns Self
		return Self{dummy: dummy}
	end 'create'

	export function typeSize() returns Integer
		return sizeof(T)
	end 'typeSize'

	export function indirectSize() returns Integer
		return typeSize()
	end 'indirectSize'
end 'Sizer'

typealias BoolSizer = Sizer with bool

function main() returns ExitCode
	let s = BoolSizer.create(false)
	return s.indirectSize()
end 'main'
```
```exitcode
1
```

<!-- test: error.instance-sibling-bare-called-in-static -->
A bare call to an INSTANCE sibling from a STATIC method has no `self` to prepend,
so it resolves receiver-less and fails the arity check on the hidden `self`
parameter (`E3036`), the same code the oracle reports.
```maxon

typealias Integer = int(i64.min to i64.max)

type Maker
	export var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump() returns Integer
		return n + 1
	end 'bump'

	static function build() returns Integer
		return bump()
	end 'build'
end 'Maker'

function main() returns ExitCode
	return Maker.build()
end 'main'
```
```maxoncstderr
error E3036: <fragment>:17:10: 'Maker.bump' expects 1 argument(s) but 0 were provided
```
