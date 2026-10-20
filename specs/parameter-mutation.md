---
feature: parameter-mutation
status: experimental
keywords: [mutation, parameter, let, immutable, call, E3159]
category: semantics
---

# Parameter Mutation

## Documentation

A function that writes a parameter's data may not be handed a read-only (`let`) value. This is the
CALL-SITE half of `E3159`; the receiver half — a receiver-writing method called directly on a `let`
binding — is `immutable-method-call.md`.

Whether a parameter is written is a property of the CALLEE, not of the call, so it is answered by a
whole-program summary: for each function, which of its parameters does its body write? A parameter is
written when a receiver-writing container method is called on it (`dest.push(9)`), when a FIELD of it is
assigned (`b.n = 99`, at whatever depth the chain reaches), when a self field is
assigned (`n = n + 1`, which writes parameter `self`), or when it is passed on to another function that
writes the parameter it lands in. That last clause makes the summary a FIXPOINT — `f` calling `g` calling
`h` mutates its parameter if `h` does — and it terminates for a recursive and a mutually recursive call
graph alike. It counts what is passed on BY VALUE, not by spelling: a value that may be the parameter's
own record — the result of a call that hands it back — writes the parameter when it reaches a written
position, so `var x = pass(p)` followed by `x.append(…)` or `x.push(…)` writes `p`.

A `var` binding, a parameter, and a temporary may all be passed to a mutating parameter: each denotes
storage the program is allowed to write. Only a read-only value — a `let` local, a `let` alias of a
parameter, or a top-level `let` — is refused, and only at the positions the callee actually writes. The
refusal follows the RECORD, not only the name: a value that may be the `let`'s own record — a call result
that hands it back — is refused at a written position too, blaming the `let`, while that `let` is read
afterwards (`specs/var-should-be-let.md`).

### A method writing its receiver writes the `let`

A `let` on a struct binding refuses a rebind (`acc = other`), a direct field write through it
(`acc.total = 1`) and a receiver-writing method on it, whether the receiver is a container or a user
type: `let acc = Accumulator.create(0)` followed by `acc.add(10)` is `E3159` whether `add` writes
`self.total`, writes the bare `total`, or pushes onto a container held in a field. A `let` holder is read-only
all the way down, so `h.inner.bump()` on a `let h` is `E3159` too.

The rule is the same one frame down. A callee that does nothing but call a receiver-writing method on the
parameter it was handed writes that parameter, so handing it a `let` is `E3159` at the call; the same call
with a `var` is legal and runs.

`self.total = self.total + value` and `total = total + value` are the same write in two spellings. The
compiler has exactly one self-field store for both spellings, so it must give one answer
for both; it gives the one the corpus pins (`self-keyword.md`'s `self-with-params`, which runs at 42).

### A call through a function VALUE

`let f = grow` followed by `f(a)` calls a function chosen at run time, so there is no single callee to summarise.
The call is judged against every function whose type can flow into the value's type: if any of them writes the
parameter, `f(a)` writes `a`. A `let` argument is then `E3159`, as for a direct call; a `var` runs.
A function that forwards a parameter to such a value is summarised the same way, so `apply(xs, f)` whose body
is `f(xs)` writes `xs` when any of those functions does. A position some candidate writes is passed as the
argument's own storage whichever body the call reaches, so a candidate that writes it updates the caller's
variable exactly as a direct call does (`pass-by-reference.md` pins the runs).

## Tests

<!-- test: let-array-to-mutating-param-error -->
Passing a `let` array to a parameter the callee pushes onto is refused.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(dest IntArray)
	dest.push(9)
end 'grow'

function main() returns ExitCode
	let a = IntArray.create()
	grow(a)
	return try a.get(0) otherwise -1
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/let-array-to-mutating-param-error.maxon:11:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'dest' (in main); pass a clone()
```

<!-- test: var-array-to-mutating-param-ok -->
A `var` array may be passed to the same parameter, and the caller observes the push.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(dest IntArray)
	dest.push(9)
end 'grow'

function main() returns ExitCode
	var a = IntArray.create()
	grow(a)
	return try a.get(0) otherwise -1
end 'main'
```
```exitcode
9
```

<!-- test: read-only-param-let-array-ok -->
A parameter the callee only READS takes a `let` argument.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function total(d IntArray) returns Integer
	return d.count()
end 'total'

function main() returns ExitCode
	let a = IntArray.create()
	return total(a) as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: transitive-let-array-error -->
The mutation is two calls away: `outer` writes nothing itself, but hands its parameter to `inner`, which
does.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function inner(d IntArray)
	d.push(7)
end 'inner'

function outer(d IntArray)
	inner(d)
end 'outer'

function main() returns ExitCode
	let a = IntArray.create()
	outer(a)
	return try a.get(0) otherwise -1
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/transitive-let-array-error.maxon:15:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'd' (in main); pass a clone()
```

<!-- test: let-to-field-writing-callee-error -->
A `let` is immutable, and a callee writing a FIELD of its parameter writes the caller's record just as an
assignment to the parameter itself would. The binding is what the rule is about, not the depth of the write.

```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

function bump(b Box)
	b.n = 99
end 'bump'

function main() returns ExitCode
	let a = Box.create()
	bump(a)
	return a.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/let-to-field-writing-callee-error.maxon:18:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: transitive-let-field-write-error -->
The field write is two calls away: `relay` writes nothing itself and hands its parameter to `poke`, which
writes a field of it. The whole-program fixpoint carries the bit back to the call the `let` is named at.

```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

function poke(b Box)
	b.n = 99
end 'poke'

function relay(b Box)
	poke(b)
end 'relay'

function main() returns ExitCode
	let a = Box.create()
	relay(a)
	return a.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/transitive-let-field-write-error.maxon:22:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: async-transitive-let-array-error -->
The same mutation, one `async` further away: a spawn hands the coroutine a reference to what its caller was
handed, so `outer` writes `d` whether the write runs before the await or after it.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function inner(d IntArray) returns Integer
	Scheduler.yield()
	d.push(7)
	return d.count() as Integer
end 'inner'

function outer(d IntArray) returns Integer
	let pending = async inner(d)
	return await pending
end 'outer'

function main() returns ExitCode
	let a = IntArray.create()
	return outer(a) as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/async-transitive-let-array-error.maxon:18:9: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'd' (in main); pass a clone()
```

<!-- test: recursive-let-array-error -->
A self-recursive callee: the summary must reach its fixpoint rather than chase the cycle.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function rec(d IntArray, n Integer)
	if n <= 0 'base'
		d.push(5)
		return
	end 'base'
	rec(d, n: n - 1)
end 'rec'

function main() returns ExitCode
	let a = IntArray.create()
	rec(a, n: 3)
	return try a.get(0) otherwise -1
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/recursive-let-array-error.maxon:15:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'd' (in main); pass a clone()
```

<!-- test: mutually-recursive-let-array-error -->
`ping` and `pong` call each other; the write is inside `ping`'s base case.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ping(d IntArray, n Integer)
	if n <= 0 'base'
		d.push(4)
		return
	end 'base'
	pong(d, n: n - 1)
end 'ping'

function pong(d IntArray, n Integer)
	ping(d, n: n)
end 'pong'

function main() returns ExitCode
	let a = IntArray.create()
	ping(a, n: 3)
	return try a.get(0) otherwise -1
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/mutually-recursive-let-array-error.maxon:19:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'd' (in main); pass a clone()
```

<!-- test: mutually-recursive-var-array-ok -->
The same mutually recursive graph with a `var` argument compiles and runs — the fixpoint terminates on
the accepting path too.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ping(d IntArray, n Integer)
	if n <= 0 'base'
		d.push(4)
		return
	end 'base'
	pong(d, n: n - 1)
end 'ping'

function pong(d IntArray, n Integer)
	ping(d, n: n)
end 'pong'

function main() returns ExitCode
	var a = IntArray.create()
	ping(a, n: 3)
	return try a.get(0) otherwise -1
end 'main'
```
```exitcode
4
```

<!-- test: labelled-arg-to-mutating-param-error -->
The mutating parameter is filled by a LABELLED argument, so the check must read the slotted position and
not the source order.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(n Integer, dest IntArray)
	dest.push(n)
end 'grow'

function main() returns ExitCode
	let a = IntArray.create()
	grow(1, dest: a)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/labelled-arg-to-mutating-param-error.maxon:11:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'dest' (in main); pass a clone()
```

<!-- test: let-arg-at-unmutated-position-ok -->
Only the WRITTEN position is refused: `src` is read, so a `let` fills it happily while the `var` fills
`dest`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function copyInto(src IntArray, dest IntArray)
	dest.push(src.count())
end 'copyInto'

function main() returns ExitCode
	let a = IntArray.create()
	var b = IntArray.create()
	copyInto(a, dest: b)
	return b.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: let-arg-at-mutated-position-error -->
The same two parameters with the roles swapped: now the `let` lands on the written one.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function copyInto(dest IntArray, src IntArray)
	dest.push(src.count())
end 'copyInto'

function main() returns ExitCode
	let a = IntArray.create()
	var b = IntArray.create()
	copyInto(a, src: b)
	return b.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/let-arg-at-mutated-position-error.maxon:12:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'dest' (in main); pass a clone()
```

<!-- test: temporary-arg-to-mutating-param-ok -->
A temporary has no binding to be immutable: it may be written freely.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(d IntArray) returns Integer
	d.push(9)
	return d.count()
end 'grow'

function main() returns ExitCode
	return grow(IntArray.create()) as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: let-string-to-appending-param-error -->
A `let` String is the ONE immortal `.rdata` record every read of that literal shares, so appending
through it writes read-only memory. Refusing the call is what makes that unreachable.

```maxon
typealias Integer = int(i64.min to i64.max)

function grow(s String) returns Integer
	s.append("XY")
	return s.byteLength()
end 'grow'

function main() returns ExitCode
	let t = "ab"
	return grow(t) as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/let-string-to-appending-param-error.maxon:11:9: cannot pass 't', which is read-only (a `let` binding), to a function that writes parameter 's' (in main); pass a clone()
```

<!-- test: var-string-to-appending-param-ok -->
A `var` String owns a real heap record, so the same helper is legal on it.

```maxon
typealias Integer = int(i64.min to i64.max)

function grow(s String) returns Integer
	s.append("XY")
	return s.byteLength()
end 'grow'

function main() returns ExitCode
	var t = "ab"
	return grow(t) as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: let-set-to-mutating-param-error -->
A `Set` parameter obeys the same rule as an `Array` or a `String`: `add`'s `s` counts as written through
`s.insert(1)` writing the RECEIVER, so a `let` Set handed to it is refused at the call. The receiver twin is
`immutable-method-call.md`'s `insert-on-let-set-error`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntSet = Set with Integer

function add(s IntSet)
	s.insert(1)
end 'add'

function main() returns ExitCode
	let s = IntSet.create()
	add(s)
	return s.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/let-set-to-mutating-param-error.maxon:11:2: cannot pass 's', which is read-only (a `let` binding), to a function that writes parameter 's' (in main); pass a clone()
```

<!-- test: var-set-to-mutating-param-ok -->
The same call with a `var` Set writes it, and the caller observes the insert.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntSet = Set with Integer

function add(s IntSet)
	s.insert(1)
end 'add'

function main() returns ExitCode
	var s = IntSet.create()
	add(s)
	return s.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: let-to-callee-calling-a-self-writing-method-error -->
The receiver-writing rule survives a frame: `bump` writes `self.n`, and `tick` does nothing but call it on
the parameter it was handed, so `tick` writes `c` and a `let` handed there is refused at the call, for the
reason `c.bump()` is refused on it directly.

```maxon
typealias Tally = int(0 to 1000)

type Counter
	export var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Counter'

function tick(c Counter)
	c.bump()
end 'tick'

function main() returns ExitCode
	let c = Counter.create()
	tick(c)
	tick(c)
	return c.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/let-to-callee-calling-a-self-writing-method-error.maxon:22:2: cannot pass 'c', which is read-only (a `let` binding), to a function that writes parameter 'c' (in main); pass a clone()
error E3159: specs/parameter-mutation/let-to-callee-calling-a-self-writing-method-error.maxon:23:2: cannot pass 'c', which is read-only (a `let` binding), to a function that writes parameter 'c' (in main); pass a clone()
```

<!-- test: var-to-callee-calling-a-self-writing-method-ok -->
The same two calls with a `var` run, and the caller observes both bumps.

```maxon
typealias Tally = int(0 to 1000)

type Counter
	export var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Counter'

function tick(c Counter)
	c.bump()
end 'tick'

function main() returns ExitCode
	var c = Counter.create()
	tick(c)
	tick(c)
	return c.n as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: let-to-self-writing-method-on-a-field-error -->
The receiver door one record down: `bump` writes its own `self`, and here its `self` is `h.inner`. A `let`
holder is read-only all the way down, so the call is refused whatever expression fills the receiver slot.

```maxon
typealias Tally = int(0 to 1000)

type Counter
	export var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Counter'

type Holder
	export var inner as Counter

	static function create() returns Self
		return Self{inner: Counter.create()}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	h.inner.bump()
	return h.inner.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/let-to-self-writing-method-on-a-field-error.maxon:26:10: cannot pass 'h', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: var-to-self-writing-method-on-a-field-ok -->
The same call on a `var` holder writes the record inside it.

```maxon
typealias Tally = int(0 to 1000)

type Counter
	export var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Counter'

type Holder
	export var inner as Counter

	static function create() returns Self
		return Self{inner: Counter.create()}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	h.inner.bump()
	return h.inner.n as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: let-to-callee-calling-a-self-writing-method-on-a-field-error -->
The same door one frame down: `nudge` writes nothing itself and only calls a self-writing method on a record
inside what it was handed, so it writes `t` and a `let` handed there is refused at the call.

```maxon
typealias Tally = int(0 to 1000)

type Counter
	export var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Counter'

type Holder
	export var inner as Counter

	static function create() returns Self
		return Self{inner: Counter.create()}
	end 'create'
end 'Holder'

function nudge(t Holder)
	t.inner.bump()
end 'nudge'

function main() returns ExitCode
	let h = Holder.create()
	nudge(h)
	nudge(h)
	return h.inner.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/let-to-callee-calling-a-self-writing-method-on-a-field-error.maxon:30:2: cannot pass 'h', which is read-only (a `let` binding), to a function that writes parameter 't' (in main); pass a clone()
error E3159: specs/parameter-mutation/let-to-callee-calling-a-self-writing-method-on-a-field-error.maxon:31:2: cannot pass 'h', which is read-only (a `let` binding), to a function that writes parameter 't' (in main); pass a clone()
```

<!-- test: var-to-callee-calling-a-self-writing-method-on-a-field-ok -->
The same two calls with a `var` holder run, and the caller observes both bumps.

```maxon
typealias Tally = int(0 to 1000)

type Counter
	export var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Counter'

type Holder
	export var inner as Counter

	static function create() returns Self
		return Self{inner: Counter.create()}
	end 'create'
end 'Holder'

function nudge(t Holder)
	t.inner.bump()
end 'nudge'

function main() returns ExitCode
	var h = Holder.create()
	nudge(h)
	nudge(h)
	return h.inner.n as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: let-global-string-to-appending-param-error -->
A top-level `let` is immutable wherever it is read, and its String is the same immortal record a local
`let`'s is.

```maxon
typealias Integer = int(i64.min to i64.max)

let GREETING = "hi"

function grow(s String) returns Integer
	s.append("XY")
	return s.byteLength()
end 'grow'

function main() returns ExitCode
	return grow(GREETING) as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/let-global-string-to-appending-param-error.maxon:12:9: cannot pass 'GREETING', which is read-only (a module-level `let`), to a function that writes parameter 's' (in main); pass a clone()
```

<!-- test: var-global-string-to-appending-param-ok -->
A top-level `var` holds an owned heap record and may be grown through a helper.

```maxon
typealias Integer = int(i64.min to i64.max)

var greeting = "hi"

function grow(s String) returns Integer
	s.append("XY")
	return s.byteLength()
end 'grow'

function main() returns ExitCode
	return grow(greeting) as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: let-global-array-to-reading-param-ok -->
A top-level `let` at a parameter the callee only reads stays legal — the refusal is per written
position, so a global argument is not refused for being a global.

```maxon
typealias Integer = int(i64.min to i64.max)

function size(s String) returns Integer
	return s.byteLength()
end 'size'

let GREETING = "hi"

function main() returns ExitCode
	return size(GREETING) as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: let-alias-of-parameter-to-mutating-param-error -->
A `let` that ALIASES a parameter carries the parameter's own SSA value while being a `let`, so a rule
derived from the value rather than from the binding would wrongly accept this.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(d IntArray)
	d.push(3)
end 'grow'

function caller(p IntArray) returns Integer
	let a = p
	grow(a)
	return a.count()
end 'caller'

function main() returns ExitCode
	var v = IntArray.create()
	return caller(v) as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/let-alias-of-parameter-to-mutating-param-error.maxon:11:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'd' (in caller); pass a clone()
```

<!-- test: var-alias-of-parameter-to-mutating-param-ok -->
A `var` alias of a parameter is writable, exactly as the parameter is.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(d IntArray)
	d.push(3)
end 'grow'

function caller(p IntArray) returns Integer
	var a = p
	grow(a)
	return a.count()
end 'caller'

function main() returns ExitCode
	var v = IntArray.create()
	return caller(v) as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: parameter-passed-on-to-mutating-param-ok -->
A parameter is a borrowed reference to the caller's record, so passing it on to a mutating parameter is
ordinary Maxon — the refusal happens at the OUTERMOST call, against the binding that owns the value.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function inner(d IntArray)
	d.push(7)
end 'inner'

function outer(d IntArray) returns Integer
	inner(d)
	return d.count()
end 'outer'

function main() returns ExitCode
	var a = IntArray.create()
	return outer(a) as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: let-struct-to-self-mutating-method-error -->
A method writing its receiver's field is refused on a `let` struct — see the documentation above for the
ruling. This is the BARE spelling; the `self.`-prefixed one is
`self-keyword.md`'s `self-with-params`, and the two must mean the same thing.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create() returns Counter
		return Self{n: 0}
	end 'create'

	function bump()
		n = n + 1
	end 'bump'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create()
	c.bump()
	return c.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/let-struct-to-self-mutating-method-error.maxon:18:4: cannot pass 'c', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: var-struct-to-self-mutating-method-ok -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create() returns Counter
		return Self{n: 0}
	end 'create'

	function bump()
		n = n + 1
	end 'bump'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.bump()
	return c.n as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: let-struct-to-transitive-self-mutating-method-error -->
`bumpTwice` writes no field itself; it calls a sibling that does, passing `self` on. Refused for the same
reason the direct write is: it writes `self`, whichever method reaches the field.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create() returns Counter
		return Self{n: 0}
	end 'create'

	function bump()
		n = n + 1
	end 'bump'

	function bumpTwice()
		bump()
		bump()
	end 'bumpTwice'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create()
	c.bumpTwice()
	return c.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/let-struct-to-transitive-self-mutating-method-error.maxon:23:4: cannot pass 'c', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: self-to-sibling-mutating-method-ok -->
`self` is a parameter, so a sibling call that passes it on is never refused — the caller's own binding is
where the rule applies.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create() returns Counter
		return Self{n: 0}
	end 'create'

	function bump()
		n = n + 1
	end 'bump'

	function bumpTwice()
		bump()
		bump()
	end 'bumpTwice'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.bumpTwice()
	return c.n as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: let-struct-read-only-method-ok -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create() returns Counter
		return Self{n: 7}
	end 'create'

	function value() returns Integer
		return n
	end 'value'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create()
	return c.value() as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: let-struct-with-array-field-to-mutating-method-error -->
A method that pushes onto a container held in a self FIELD is the same ruling once more: there is no
principled line between writing `self.total` and writing the array `self.items` points at, so both are
refused on a `let` receiver.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export var items as IntArray

	static function create() returns Bag
		return Self{items: IntArray.create()}
	end 'create'

	function add(v Integer)
		items.push(v)
	end 'add'

	function size() returns Integer
		return items.count()
	end 'size'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	b.add(1)
	return b.size() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/let-struct-with-array-field-to-mutating-method-error.maxon:23:4: cannot pass 'b', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: var-struct-with-array-field-to-mutating-method-ok -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export var items as IntArray

	static function create() returns Bag
		return Self{items: IntArray.create()}
	end 'create'

	function add(v Integer)
		items.push(v)
	end 'add'

	function size() returns Integer
		return items.count()
	end 'size'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.add(1)
	return b.size() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.function-value-call-writes-a-let -->
A call through a function VALUE is judged against every function that may flow into the value's type. `grow`
writes its parameter, so `f(a)` writes `a`, a `let`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(d IntArray)
	d.push(9)
end 'grow'

function main() returns ExitCode
	let a = IntArray.create()
	let f = grow
	f(a)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:12:3: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'd' (in main); pass a clone()
```

<!-- test: function-value-call-writes-a-var -->
The accepted twin: the same call with a `var`, which the callee may write.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(d IntArray)
	d.push(9)
end 'grow'

function main() returns ExitCode
	var a = IntArray.create()
	let f = grow
	f(a)
	return a.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.function-value-forwarded-through-a-parameter-writes-a-let -->
The function value arrives as a parameter, so `apply` names no callee at all: it writes `d` if any function that
may flow into `Grower` does, and `grow` does.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias Grower = function(IntArray)

function grow(d IntArray)
	d.push(9)
end 'grow'

function apply(g Grower, d IntArray)
	g(d)
end 'apply'

function main() returns ExitCode
	let a = IntArray.create()
	apply(grow, d: a)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:16:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'd' (in main); pass a clone()
```

<!-- test: function-value-call-of-an-unrelated-type-leaves-a-let-alone -->
Only the functions that may flow into the called value's type are judged. `grow` writes its parameter, but it is
not a `Sizer`, so calling a `Sizer` through `apply` hands `a` to a reader and the `let` is accepted.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias Sizer = function(IntArray) returns Integer

function grow(d IntArray)
	d.push(9)
end 'grow'

function size(d IntArray) returns Integer
	return d.count() as Integer
end 'size'

function apply(s Sizer, d IntArray) returns Integer
	return s(d)
end 'apply'

function main() returns ExitCode
	let a = IntArray.create()
	var b = IntArray.create()
	let g = grow
	g(b)
	let total = apply(size, d: a)
	return (total + (b.count() as Integer)) as ExitCode
end 'main'
```
```exitcode
1
```
<!-- test: error.function-value-call-keeps-its-argument-in-a-let-field -->
The same judgement covers what a function value KEEPS: `shelve` stores its parameter in a `let` field, so the
call through `build`'s `Maker` keeps `v`, which is read again afterwards.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Shelf
	export let items as IntArray

	static function create(items IntArray) returns Self
		return Self{items: items}
	end 'create'
end 'Shelf'

typealias Maker = function(IntArray) returns Shelf

function shelve(d IntArray) returns Shelf
	return Shelf.create(d)
end 'shelve'

function build(m Maker, d IntArray) returns Shelf
	return m(d)
end 'build'

function main() returns ExitCode
	var v = IntArray.create()
	v.push(1)
	let shelf = build(shelve, d: v)
	v.push(2)
	return shelf.items.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:26:14: cannot pass the `var` 'v', which is read again, to parameter 'd' of 'build', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: function-value-call-keeps-an-argument-it-is-last-used-in -->
The accepted twin: `v` is not read after the call, so the `let` field takes it by a move.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Shelf
	export let items as IntArray

	static function create(items IntArray) returns Self
		return Self{items: items}
	end 'create'
end 'Shelf'

typealias Maker = function(IntArray) returns Shelf

function shelve(d IntArray) returns Shelf
	return Shelf.create(d)
end 'shelve'

function build(m Maker, d IntArray) returns Shelf
	return m(d)
end 'build'

function main() returns ExitCode
	var v = IntArray.create()
	v.push(1)
	let shelf = build(shelve, d: v)
	return shelf.items.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.function-value-call-with-a-literal-container-is-refused -->
A call through a function VALUE may still not carry a LITERAL container: `a` is a `let` array literal, a
constant the compiler read its range columns from, and the call may write it.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(d IntArray)
	d.push(9)
end 'grow'

function main() returns ExitCode
	let a = [1 as Integer]
	let f = grow
	f(a)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:12:3: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'd' (in main); pass a clone()
```

<!-- test: function-value-call-with-a-let-array-to-a-read-only-callback-is-accepted -->
The same call through a value naming a callback that only reads is accepted.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function size(d IntArray) returns Count
	return d.count()
end 'size'

function main() returns ExitCode
	let a = [1 as Integer, 2 as Integer, 3 as Integer]
	let f = size
	return f(a) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: function-value-call-with-a-let-map-to-a-read-only-callback-is-accepted -->
A `let` map literal is accepted by a read-only callback too.

```maxon
typealias CountMap = Map with (Count, Count)

function size(m CountMap) returns Count
	return m.count()
end 'size'

function main() returns ExitCode
	let m = [1 as Count: 10 as Count]
	let f = size
	return f(m) as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: function-value-call-with-a-let-record-to-a-read-only-callback-is-accepted -->
So is a `let` record.

```maxon
type Box
	export var n = 7 as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

function read(b Box) returns Count
	return b.n
end 'read'

function main() returns ExitCode
	let b = Box.create()
	let f = read
	return f(b) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: error.function-value-call-with-a-let-record-to-a-writing-callback-is-refused -->
A `let` record handed to a callback that writes one of its fields is refused.

```maxon
type Box
	export var n = 7 as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

function bump(b Box)
	b.n = 99
end 'bump'

function main() returns ExitCode
	let b = Box.create()
	let f = bump
	f(b)
	return b.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:17:3: cannot pass 'b', which is read-only (a `let` binding), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: error.a-closure-literal-callee-that-writes-its-parameter-refuses-a-let -->
A closure literal is the exact callee, and its body's write counts.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function growAndCount(d IntArray) returns Count
	d.push(9)
	return d.count()
end 'growAndCount'

function main() returns ExitCode
	let a = IntArray.create()
	let f = function(d IntArray) gives growAndCount(d)
	return f(a) as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:13:10: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'd' (in main); pass a clone()
```

<!-- test: error.a-callback-parameter-that-may-write-refuses-a-let-handed-through-the-caller -->
`apply` forwards its array to an unknown callback; some function of the callback's shape writes it, so `apply` writes its parameter and a `let` handed to `apply` is refused.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias Mutator = function(IntArray)

function grow(d IntArray)
	d.push(9)
end 'grow'

function apply(xs IntArray, f Mutator)
	f(xs)
end 'apply'

function main() returns ExitCode
	let a = IntArray.create()
	let g = grow
	apply(a, f: g)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:17:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'xs' (in main); pass a clone()
```

<!-- test: a-callback-parameter-whose-candidates-only-read-accepts-a-let -->
When no function of the callback's shape that is used as a value writes the parameter, the `let` is accepted.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias Reader = function(IntArray) returns Count

function look(d IntArray) returns Count
	return d.count()
end 'look'

function apply(xs IntArray, f Reader) returns Count
	return f(xs)
end 'apply'

function main() returns ExitCode
	let a = IntArray.create()
	let g = look
	_ = apply(a, f: g)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.a-higher-order-call-is-written-through-two-levels -->
The write is carried through a second forwarding function.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias Mutator = function(IntArray)

function grow(d IntArray)
	d.push(9)
end 'grow'

function apply(xs IntArray, f Mutator)
	f(xs)
end 'apply'

function relay(ys IntArray, g Mutator)
	apply(ys, f: g)
end 'relay'

function main() returns ExitCode
	let a = IntArray.create()
	let w = grow
	relay(a, g: w)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:21:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'ys' (in main); pass a clone()
```

<!-- test: var-self-field-passed-to-mutating-param-ok -->
A `var` field is writable BOTH ways — as a method receiver (`items.push(v)`) and as an ARGUMENT handed to a
callee that writes it (`grow(items)`). They are one question about one field, and the two askers derive the
answer from one place (`Parser.selfFieldIsWritable`); derived twice, and once INVERTED, a `var` field could
become one a method may push onto but no callee may be handed.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(d IntArray)
	d.push(9)
end 'grow'

type Bag
	export var items as IntArray

	static function create() returns Bag
		return Self{items: IntArray.create()}
	end 'create'

	function add(v Integer)
		items.push(v)
		grow(items)
	end 'add'

	function size() returns Integer
		return items.count()
	end 'size'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.add(1)
	return b.size() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: let-self-field-passed-to-mutating-param-error -->
The `let` half of the same pair: the field's own `let` is what refuses it, and the diagnostic blames the
FIELD's name. The receiver half of this (`items.push(v)`) is `immutable-method-call.md`'s
`push-on-let-self-field-array-error`; both must answer from the same reading of `layout.fieldIsMutable`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(d IntArray)
	d.push(9)
end 'grow'

type Bag
	export let items as IntArray

	static function create() returns Bag
		return Self{items: IntArray.create()}
	end 'create'

	function add()
		grow(items)
	end 'add'

	function size() returns Integer
		return items.count()
	end 'size'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.add()
	return b.size() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:17:3: cannot pass a `let` field of 'items', which is read-only, to a function that writes parameter 'd' (in Bag.add); pass a clone()
```

<!-- test: a-returned-string-bound-to-a-var-is-a-copy-the-callee-writes-nothing-through -->
A `String` a name does not own is copied when it is bound to a `var`, so the `var` made from `pass(p)` is the
callee's own string: `tag` appends to the copy, writes nothing through `p`, and a `let` argument is accepted.
The same shape over a record (`let-record-written-through-an-alias-in-the-callee`) names the parameter's record
and stays refused.

```maxon
function pass(s String) returns String
	return s
end 'pass'

function tag(p String)
	var x = pass(p)
	x.append(" TAGGED")
	print("{x}\n")
end 'tag'

function main() returns ExitCode
	let n = 7
	let a = "lit{n}"
	tag(a)
	print("{a}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
lit7 TAGGED
lit7
```

<!-- test: let-record-written-through-an-alias-in-the-callee -->
A container method called through a mutable name made from a call's result writes every parameter
that result may be, so `tag` writes `p`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function pass(s IntArray) returns IntArray
	return s
end 'pass'

function tag(p IntArray)
	var x = pass(p)
	x.push(9)
end 'tag'

function main() returns ExitCode
	let a = IntArray.create()
	tag(a)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/let-record-written-through-an-alias-in-the-callee.maxon:16:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'p' (in main); pass a clone()
```

<!-- test: a-write-through-a-var-bound-to-a-parameter-writes-that-parameter -->
A `var` made from a parameter names the caller's record, so writing a field of it writes the parameter and a
`let` argument is refused at the call.

```maxon
typealias Integer = int(i64.min to i64.max)

type Blk
	export var id as Integer

	static function create(id Integer) returns Blk
		return Blk{id: id}
	end 'create'
end 'Blk'

function retarget(block Blk, id Integer)
	var target = block
	target.id = id
end 'retarget'

function main() returns ExitCode
	let a = Blk.create(1)
	retarget(a, id: 5)
	return a.id as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/a-write-through-a-var-bound-to-a-parameter-writes-that-parameter.maxon:19:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'block' (in main); pass a clone()
```

<!-- test: a-write-through-a-var-bound-to-a-parameter-accepts-a-var-argument -->
The same call with a `var` argument writes it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Blk
	export var id as Integer

	static function create(id Integer) returns Blk
		return Blk{id: id}
	end 'create'
end 'Blk'

function retarget(block Blk, id Integer)
	var target = block
	target.id = id
end 'retarget'

function main() returns ExitCode
	var a = Blk.create(1)
	retarget(a, id: 5)
	return a.id as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: a-write-through-a-var-bound-to-a-field-of-a-parameter-writes-that-parameter -->
A `var` made from a field of a parameter names a record inside the caller's, so writing it writes the
parameter.

```maxon
typealias Integer = int(i64.min to i64.max)

type Blk
	export var id as Integer

	static function create(id Integer) returns Blk
		return Blk{id: id}
	end 'create'
end 'Blk'

type Outer
	export var inner as Blk

	static function create(id Integer) returns Outer
		return Outer{inner: Blk.create(id)}
	end 'create'
end 'Outer'

function retargetInner(outer Outer, id Integer)
	var target = outer.inner
	target.id = id
end 'retargetInner'

function main() returns ExitCode
	let o = Outer.create(2)
	retargetInner(o, id: 6)
	return o.inner.id as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/a-write-through-a-var-bound-to-a-field-of-a-parameter-writes-that-parameter.maxon:27:2: cannot pass 'o', which is read-only (a `let` binding), to a function that writes parameter 'outer' (in main); pass a clone()
```

<!-- test: a-write-through-a-var-bound-to-a-field-of-a-parameter-accepts-a-var-argument -->
The same call with a `var` argument writes it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Blk
	export var id as Integer

	static function create(id Integer) returns Blk
		return Blk{id: id}
	end 'create'
end 'Blk'

type Outer
	export var inner as Blk

	static function create(id Integer) returns Outer
		return Outer{inner: Blk.create(id)}
	end 'create'
end 'Outer'

function retargetInner(outer Outer, id Integer)
	var target = outer.inner
	target.id = id
end 'retargetInner'

function main() returns ExitCode
	var o = Outer.create(2)
	retargetInner(o, id: 6)
	return o.inner.id as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: a-write-through-a-merge-of-two-parameters-writes-both -->
A `var` that is one parameter or the other writes whichever it is, so both parameters are written and a `let`
argument at either is refused.

```maxon
typealias Integer = int(i64.min to i64.max)

type Blk
	export var id as Integer

	static function create(id Integer) returns Blk
		return Blk{id: id}
	end 'create'
end 'Blk'

function retargetEither(first Blk, second Blk, which bool, id Integer)
	var target = first if which else second
	target.id = id
end 'retargetEither'

function main() returns ExitCode
	let c = Blk.create(4)
	var b = Blk.create(3)
	retargetEither(c, second: b, which: false, id: 7)
	return b.id as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/parameter-mutation/a-write-through-a-merge-of-two-parameters-writes-both.maxon:20:2: cannot pass 'c', which is read-only (a `let` binding), to a function that writes parameter 'first' (in main); pass a clone()
```

<!-- test: a-write-through-a-merge-of-two-parameters-accepts-var-arguments -->
The same call with `var` arguments writes the one the merge picks.

```maxon
typealias Integer = int(i64.min to i64.max)

type Blk
	export var id as Integer

	static function create(id Integer) returns Blk
		return Blk{id: id}
	end 'create'
end 'Blk'

function retargetEither(first Blk, second Blk, which bool, id Integer)
	var target = first if which else second
	target.id = id
end 'retargetEither'

function main() returns ExitCode
	var c = Blk.create(4)
	var b = Blk.create(3)
	retargetEither(c, second: b, which: false, id: 7)
	return b.id as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: let-array-in-parentheses-to-mutating-param-error -->
Parentheses around the argument change nothing: `grow((g))` hands `g` to the parameter the callee pushes onto, as `grow(g)` does.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(dest IntArray)
	dest.push(9)
end 'grow'

function main() returns ExitCode
	let g = IntArray.create()
	grow((g))
	return try g.get(0) otherwise -1
end 'main'
```
```maxoncstderr
error E3159: <fragment>:11:2: cannot pass 'g', which is read-only (a `let` binding), to a function that writes parameter 'dest' (in main); pass a clone()
```
