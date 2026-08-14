---
feature: parameter-mutation
status: experimental
keywords: [mutation, parameter, let, immutable, call, E3019]
category: semantics
---

# Parameter Mutation

## Documentation

A function that writes a parameter's data may not be handed an immutable (`let`) binding. This is the
CALL-SITE half of `E3019`; the receiver half — a receiver-writing method called directly on a `let`
binding — is `immutable-method-call.md`.

Whether a parameter is written is a property of the CALLEE, not of the call, so it is answered by a
whole-program summary: for each function, which of its parameters does its body write? A parameter is
written when a receiver-writing container method is called on it (`dest.push(9)`), when a self field is
assigned (`n = n + 1`, which writes parameter `self`), or when it is passed on to another function that
writes the parameter it lands in. That last clause makes the summary a FIXPOINT — `f` calling `g` calling
`h` mutates its parameter if `h` does — and it terminates for a recursive and a mutually recursive call
graph alike.

A `var` binding, a parameter, and a temporary may all be passed to a mutating parameter: each denotes
storage the program is allowed to write. Only an immutable binding — a `let` local, a `let` alias of a
parameter, or a top-level `let` — is refused, and only at the positions the callee actually writes.

### A method writing its OWN receiver is not a parameter mutation

A `let` on a struct binding refuses a rebind (`acc = other`) and a direct field write through it
(`acc.total = 1`) — both `E2013` — and a receiver-writing method on it *as a container*. It does **not**
reach inside the type's own methods: `let acc = Accumulator.create(0)` followed by `acc.add(10)` is legal
and returns what the accumulation says, whether `add` writes `self.total`, writes the bare `total`, or
pushes onto a container held in a field.

This is a deliberate divergence from the runnable oracle, taken because **the oracle disagrees with
itself**. Measured on one program with a `let` receiver: `self.total = self.total + value` is accepted
and returns 42, while `total = total + value` — the same write, the other spelling — is `E3019`. Its
analysis matches op TYPES, and only the bare spelling produces the operation its self-field check
inspects. shv2 has exactly one self-field store for both spellings (splitting them is what v1 did, and it
cost v1 a field-visibility check that was structurally blind to bare names), so it must give one answer
for both; it gives the one the corpus pins (`self-keyword.md`'s `self-with-params`, which both compilers
run at 42).

### Not checked: a call through a function VALUE

`let f = grow` followed by `f(a)` calls a function chosen at run time, so there is no callee to
summarise. Both reference compilers accept it (measured), and refusing every indirect call that carries
an immutable argument would refuse programs both compilers run. It is therefore accepted here too, and
pinned below so the hole is visible rather than assumed.

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
error E3019: specs/fragments/parameter-mutation/let-array-to-mutating-param-error.test:11:2: cannot pass 'a' to function that mutates parameter 'dest' (in main)
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
error E3019: specs/fragments/parameter-mutation/transitive-let-array-error.test:15:2: cannot pass 'a' to function that mutates parameter 'd' (in main)
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
error E3019: specs/fragments/parameter-mutation/recursive-let-array-error.test:15:2: cannot pass 'a' to function that mutates parameter 'd' (in main)
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
error E3019: specs/fragments/parameter-mutation/mutually-recursive-let-array-error.test:19:2: cannot pass 'a' to function that mutates parameter 'd' (in main)
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
error E3019: specs/fragments/parameter-mutation/labelled-arg-to-mutating-param-error.test:11:2: cannot pass 'a' to function that mutates parameter 'dest' (in main)
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
error E3019: specs/fragments/parameter-mutation/let-arg-at-mutated-position-error.test:12:2: cannot pass 'a' to function that mutates parameter 'dest' (in main)
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
error E3019: specs/fragments/parameter-mutation/let-string-to-appending-param-error.test:11:9: cannot pass 't' to function that mutates parameter 's' (in main)
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

<!-- test: let-set-to-mutating-param-ok -->
⭐⭐ **A `Set` PARAMETER NO LONGER OBEYS THIS RULE (W90), AND IT IS THE SAME RULING ITS RECEIVER TWIN
CARRIES** — see `immutable-method-call.md`'s Documentation, which owns it. The refusal here was never a
parameter rule of its own: `add`'s `s` counts as mutated only because `s.insert(1)` mutates the RECEIVER,
which the parser decides through the builtin `setMethodMutatesReceiver` roster. A declared type never
reaches that roster, so both shapes drop together and neither is left half-enforced.

⚠ The `Array` and `String` cases around this one are unaffected and stay green: both are still
builtin-dispatched.

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
```exitcode
1
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
error E3019: specs/fragments/parameter-mutation/let-global-string-to-appending-param-error.test:12:9: cannot pass 'GREETING' to function that mutates parameter 's' (in main)
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
error E3019: specs/fragments/parameter-mutation/let-alias-of-parameter-to-mutating-param-error.test:11:2: cannot pass 'a' to function that mutates parameter 'd' (in caller)
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

<!-- test: let-struct-to-self-mutating-method-ok -->
A method writing its OWN receiver's field is legal on a `let` struct — see the documentation above for the
ruling and for the oracle inconsistency it settles. This is the BARE spelling; the `self.`-prefixed one is
`self-keyword.md`'s `self-with-params`, and the two must mean the same thing.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create() returns Counter
		return Self{n: 0}
	end 'create'

	export function bump()
		n = n + 1
	end 'bump'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create()
	c.bump()
	return c.n as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: var-struct-to-self-mutating-method-ok -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create() returns Counter
		return Self{n: 0}
	end 'create'

	export function bump()
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

<!-- test: let-struct-to-transitive-self-mutating-method-ok -->
`bumpTwice` writes no field itself; it calls a sibling that does, passing `self` on. Legal for the same
reason the direct write is: `self` is the type's own, whichever method reaches it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create() returns Counter
		return Self{n: 0}
	end 'create'

	export function bump()
		n = n + 1
	end 'bump'

	export function bumpTwice()
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
```exitcode
2
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

	export function bump()
		n = n + 1
	end 'bump'

	export function bumpTwice()
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

	export function value() returns Integer
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

<!-- test: let-struct-with-array-field-to-mutating-method-ok -->
A method that pushes onto a container held in a self FIELD is the same ruling once more: there is no
principled line between writing `self.total` and writing the array `self.items` points at, so drawing one
would be the very spelling-dependent inconsistency the documentation above measures in the oracle.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export var items as IntArray

	static function create() returns Bag
		return Self{items: IntArray.create()}
	end 'create'

	export function add(v Integer)
		items.push(v)
	end 'add'

	export function size() returns Integer
		return items.count()
	end 'size'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	b.add(1)
	return b.size() as ExitCode
end 'main'
```
```exitcode
1
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

	export function add(v Integer)
		items.push(v)
	end 'add'

	export function size() returns Integer
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

<!-- test: function-value-call-is-not-checked -->
A call through a function VALUE names no callee to summarise, and both reference compilers accept it
(measured). Pinned so the hole is visible: `a` is a `let` and the push takes effect.

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
```exitcode
1
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

	export function add(v Integer)
		items.push(v)
		grow(items)
	end 'add'

	export function size() returns Integer
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

	export function add()
		grow(items)
	end 'add'

	export function size() returns Integer
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
error E3019: specs/fragments/parameter-mutation/let-self-field-passed-to-mutating-param-error.test:17:3: cannot pass 'items' to function that mutates parameter 'd' (in Bag.add)
```
