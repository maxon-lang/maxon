---
feature: regions-match-lend
status: experimental
keywords: [isolated, match, payload, lend, window, alias, linear, E3198, E3102]
category: semantics
---

# A match over a plain union lends its `isolated` payload

## Documentation

A `match` over a union that is not linear lends each `isolated` payload to its arm. The arm opens the payload under
the window rule, as it opens any `isolated` value it does not own, and the payload never goes where an owned
`isolated` value is wanted: an `isolated` parameter, an `isolated` slot, a send. The union is ordinary region data
held by a plain `let`, a `var`, a parameter, a field or an element, so it aliases freely and a second name for it
reaches the same payload.

A linear union, and a union that holds a `Reply`, is never aliased. The `match` consumes it and hands each payload
over as an owned value, which goes anywhere an `isolated` value goes.

## Tests

<!-- test: error.a-plain-union-lends-its-isolated-payload-where-an-isolated-value-is-wanted -->
`u` is a plain union built from an `isolated` value, and the arm hands the payload to an `isolated` parameter.

```maxon
typealias Tally = int(0 to 1000)

union Mx
	m0(x Bx)
	m1(y isolated Bx)
end 'Mx'

type Bx
	var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	print("took {b.n}\n")
end 'takeB'

function main() returns ExitCode
	let rb = Bx.create(5) as isolated Bx
	let u = Mx.m1(rb)

	match u 'x'
		m0(xa) then print("m0 {xa.n}\n")
		m1(o) then takeB(o)
	end 'x'

	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:27:20: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.two-names-for-a-plain-union-cannot-give-its-isolated-payload-two-owners -->
`v` is a second name for the union `u` holds, so each `match` would hand the one payload to its own owner.

```maxon
typealias Tally = int(0 to 1000)

union Mx
	m0(x Bx)
	m1(y isolated Bx)
end 'Mx'

type Bx
	var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	print("took {b.n}\n")
end 'takeB'

function main() returns ExitCode
	let rb = Bx.create(5) as isolated Bx
	let u = Mx.m1(rb)
	let v = u

	match u 'a'
		m0(xa) then print("m0 {xa.n}\n")
		m1(oa) then takeB(oa)
	end 'a'

	match v 'b'
		m0(xb) then print("m0 {xb.n}\n")
		m1(ob) then takeB(ob)
	end 'b'

	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:28:21: 'oa' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-alias-of-a-plain-union-read-after-a-hand-over-is-refused-at-the-hand-over -->
The second `match` only reads the payload, and the first still hands it over, which is where the refusal is.

```maxon
typealias Tally = int(0 to 1000)

union Mx
	m0(x Bx)
	m1(y isolated Bx)
end 'Mx'

type Bx
	var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	print("took {b.n}\n")
end 'takeB'

function main() returns ExitCode
	let rb = Bx.create(5) as isolated Bx
	let u = Mx.m1(rb)
	let v = u

	match u 'a'
		m0(xa) then print("m0 {xa.n}\n")
		m1(oa) then takeB(oa)
	end 'a'

	match v 'b'
		m0(xb) then print("m0 {xb.n}\n")
		m1(ob) then print("read {ob.n}\n")
	end 'b'

	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:28:21: 'oa' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.a-call-that-keeps-a-plain-union-leaves-its-isolated-payload-lent -->
`keepU` stores the union in a holder, which then names the payload the `match` over `u` would hand away.

```maxon
typealias Tally = int(0 to 1000)

union Mx
	m0(x Bx)
	m1(y isolated Bx)
end 'Mx'

type Bx
	var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hs
	var k as Mx

	static function create(k Mx) returns Self
		return Self{k: k}
	end 'create'
end 'Hs'

function takeB(b isolated Bx)
	print("took {b.n}\n")
end 'takeB'

function keepU(u Mx, h Hs)
	h.k = u
end 'keepU'

function main() returns ExitCode
	let rb = Bx.create(5) as isolated Bx
	let u = Mx.m1(rb)
	let h = Hs.create(Mx.m0(Bx.create(0)))

	keepU(u, h: h)

	match u 'a'
		m0(xa) then print("m0 {xa.n}\n")
		m1(oa) then takeB(oa)
	end 'a'

	match h.k 'b'
		m0(xb) then print("m0 {xb.n}\n")
		m1(ob) then print("read {ob.n}\n")
	end 'b'

	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:42:21: 'oa' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.a-call-that-returns-a-plain-union-leaves-its-isolated-payload-lent -->
`idU` answers the union it was given, so `v` and `u` name one union.

```maxon
typealias Tally = int(0 to 1000)

union Mx
	m0(x Bx)
	m1(y isolated Bx)
end 'Mx'

type Bx
	var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	print("took {b.n}\n")
end 'takeB'

function idU(u Mx) returns Mx
	return u
end 'idU'

function main() returns ExitCode
	let rb = Bx.create(5) as isolated Bx
	let u = Mx.m1(rb)
	let v = idU(u)

	match u 'a'
		m0(xa) then print("m0 {xa.n}\n")
		m1(oa) then takeB(oa)
	end 'a'

	match v 'b'
		m0(xb) then print("m0 {xb.n}\n")
		m1(ob) then print("read {ob.n}\n")
	end 'b'

	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:32:21: 'oa' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: an-arm-opens-the-payload-a-plain-union-lends -->
A write through the payload and a call that only reads it open the payload in the arm, for a `let` union and for a parameter matched twice.

```maxon
typealias Tally = int(0 to 1000)

union Mx
	m0(x Bx)
	m1(y isolated Bx)
end 'Mx'

type Bx
	var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function show(b Bx)
	print("show {b.n}\n")
end 'show'

function bump(u Mx)
	match u 'a'
		m0(xa) then show(xa)
		m1(oa) then oa.n = 3
	end 'a'

	match u 'b'
		m0(xb) then show(xb)
		m1(ob) then show(ob)
	end 'b'
end 'bump'

function main() returns ExitCode
	let rb = Bx.create(5) as isolated Bx
	let u = Mx.m1(rb)

	match u 'c'
		m0(xc) then show(xc)
		m1(oc) then oc.n = 4
	end 'c'

	bump(Mx.m1(Bx.create(1)))

	return 0
end 'main'
```
```stdout
show 3
```
```exitcode
0
```

<!-- test: a-linear-union-hands-its-isolated-payload-over-as-an-owned-value -->
`Mx` holds a linear case, so the `match` consumes it and the `isolated` payload of its other case goes to an `isolated` parameter.

```maxon
typealias Tally = int(0 to 1000)

union Mx
	m0(x Rl)
	m1(y isolated Bk)
end 'Mx'

type Rl
	var code as Tally

	static function create(code Tally) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Tally)
		print("fin {self.code} {c}\n")
		destruct self
	end 'fin'
end 'Rl'

type Bk
	var code as Tally

	static function create(code Tally) returns Self
		return Self{code: code}
	end 'create'
end 'Bk'

function takeI(b isolated Bk)
	print("took {b.code}\n")
end 'takeI'

function main() returns ExitCode
	let u = Mx.m1(Bk.create(1))

	match u 'x1'
		m0(x) then x.fin(1)
		m1(y) then takeI(y)
	end 'x1'

	return 0
end 'main'
```
```stdout
took 1
```
```exitcode
0
```

<!-- test: error.a-payload-of-an-isolated-union-goes-to-one-isolated-parameter-once -->
`u` is held in an `isolated` local, so the `match` hands `o` over as an owned value, and the first argument moves it.

```maxon
typealias Tally = int(0 to 1000)

union Mx
	m0(x Bx)
	m1(y isolated Bx)
end 'Mx'

type Bx
	var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeTwo(a isolated Bx, b isolated Bx)
	print("two {a.n} {b.n}\n")
end 'takeTwo'

function main() returns ExitCode
	let rb = Bx.create(5) as isolated Bx
	let u = Mx.m1(rb) as isolated Mx

	match u 'x'
		m0(xa) then print("m0 {xa.n}\n")
		m1(o) then takeTwo(o, b: o)
	end 'x'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:25: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.a-box-union-lends-its-isolated-payload-where-an-isolated-value-is-wanted -->
A case with a second payload makes the union a heap box rather than one word, and the payload is lent all the same.

```maxon
typealias Tally = int(0 to 1000)

union Mx
	m0(x Bx, tag Tally)
	m1(y isolated Bx, tag Tally)
end 'Mx'

type Bx
	var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	print("took {b.n}\n")
end 'takeB'

function main() returns ExitCode
	let rb = Bx.create(5) as isolated Bx
	let u = Mx.m1(rb, tag: 1)

	match u 'x'
		m0(xa, _) then print("m0 {xa.n}\n")
		m1(o, _) then takeB(o)
	end 'x'

	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:27:23: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.a-payload-of-a-box-union-held-in-an-isolated-variable-goes-to-one-isolated-parameter-once -->
The same refusal for a union that is a heap box.

```maxon
typealias Tally = int(0 to 1000)

union Mx
	m0(x Bx, tag Tally)
	m1(y isolated Bx, tag Tally)
end 'Mx'

type Bx
	var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeTwo(a isolated Bx, b isolated Bx)
	print("two {a.n} {b.n}\n")
end 'takeTwo'

function main() returns ExitCode
	let rb = Bx.create(5) as isolated Bx
	let u = Mx.m1(rb, tag: 1) as isolated Mx

	match u 'x'
		m0(xa, _) then print("m0 {xa.n}\n")
		m1(o, _) then takeTwo(o, b: o)
	end 'x'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:28: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-writing-lend-of-the-matched-variable-beside-an-arm-binding-is-refused -->
`both` writes through `u`, and the same call names `tV`, the arm's view of the payload `u` holds.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

union Ut
	none
	have(t Tk)
end 'Ut'

function both(u Ut, t Tk)
	t.n = 4
	match u 'inner'
		none then print("none\n")
		have(x) then x.n = 5
	end 'inner'
end 'both'

function main() returns ExitCode
	let uV = Ut.have(Tk.create(1))
	match uV 'outer'
		none then print("none\n")
		have(tV) then both(uV, t: tV)
	end 'outer'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:29:17: cannot lend 'uV' to parameter 'u' of 'both', which writes through it, in a call that also names a binding of the arm: the binding views the matched value, and a unique value has one name; write through 'uV' in a statement that names no binding of the arm
```

<!-- test: a-writing-lend-of-the-matched-variable-as-the-receiver-beside-an-arm-binding-is-refused -->
The receiver is an argument: `both` writes through `self`, which is `uV`, and the same call names `tV`.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

union Ut
	none
	have(t Tk)

	function both(t Tk)
		t.n = 4
		match self 'inner'
			none then print("none\n")
			have(x) then x.n = 5
		end 'inner'
	end 'both'
end 'Ut'

function main() returns ExitCode
	let uV = Ut.have(Tk.create(1))
	match uV 'outer'
		none then print("none\n")
		have(tV) then uV.both(tV)
	end 'outer'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:29:20: cannot lend 'uV' to parameter 'self' of 'Ut.both', which writes through it, in a call that also names a binding of the arm: the binding views the matched value, and a unique value has one name; write through 'uV' in a statement that names no binding of the arm
```

<!-- test: a-writing-lend-of-the-matched-variable-beside-an-arm-binding-that-is-the-receiver-is-refused -->
The receiver `tV` is the arm's binding, and `absorb` writes through `u`, which is `uV`.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function absorb(u Ut)
		self.n = 4
		match u 'inner'
			none then print("none\n")
			have(x) then x.n = 5
		end 'inner'
	end 'absorb'
end 'Tk'

union Ut
	none
	have(t Tk)
end 'Ut'

function main() returns ExitCode
	let uV = Ut.have(Tk.create(1))
	match uV 'outer'
		none then print("none\n")
		have(tV) then tV.absorb(uV)
	end 'outer'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:29:20: cannot lend 'uV' to parameter 'u' of 'Tk.absorb', which writes through it, in a call that also names a binding of the arm: the binding views the matched value, and a unique value has one name; write through 'uV' in a statement that names no binding of the arm
```

<!-- test: a-by-reference-pass-of-the-matched-variable-to-a-function-value-in-a-binding-arm-is-refused -->
`BothFn` declares its first parameter `mutable`, so a call through `f` passes `uV` by reference, and `uV` stays put in the `have(tV)` arm that binds its payload.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

union Ut
	none
	have(t Tk)
end 'Ut'

function both(u mutable Ut, t Tk)
	match u 'inner'
		none then print("none\n")
		have(x) then x.n = 5
	end 'inner'
	print("{t.n}\n")
end 'both'

typealias BothFn = function(u mutable Ut, t Tk)

function main() returns ExitCode
	let f = both as BothFn
	var uV = Ut.have(Tk.create(1))
	match uV 'outer'
		none then print("none\n")
		have(tV) then f(uV, tV)
	end 'outer'
	return 0
end 'main'
```
```maxoncstderr
error E3203: <fragment>:32:18: 'uV' roots what 'outer' lends or iterates and cannot be reassigned or passed by reference inside it
```

<!-- test: a-read-only-lend-of-the-matched-variable-as-the-receiver-beside-an-arm-binding-is-accepted -->
`total` only reads `self` and `t`.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function plus(u Ut) returns Num
		return match u 'inner'
			none gives self.n
			have(x) gives self.n + x.n
		end 'inner'
	end 'plus'
end 'Tk'

union Ut
	none
	have(t Tk)

	function total(t Tk) returns Num
		return match self 'inner'
			none gives t.n
			have(x) gives t.n + x.n
		end 'inner'
	end 'total'
end 'Ut'

function main() returns ExitCode
	let uV = Ut.have(Tk.create(1))
	match uV 'outer'
		none then print("none\n")
		have(tV) then print("{uV.total(tV)} {tV.plus(uV)}\n")
	end 'outer'
	return 0
end 'main'
```
```stdout
2 2
```
```exitcode
0
```

<!-- test: a-read-only-lend-of-the-matched-variable-to-a-function-value-beside-an-arm-binding-is-accepted -->
`SumFn` declares no parameter `mutable`, so a call through `f` only reads `uV` and `tV`.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

union Ut
	none
	have(t Tk)
end 'Ut'

function sum(u Ut, t Tk) returns Num
	return match u 'inner'
		none gives t.n
		have(x) gives t.n + x.n
	end 'inner'
end 'sum'

typealias SumFn = function(u Ut, t Tk) returns Num

function main() returns ExitCode
	let f = sum as SumFn
	let uV = Ut.have(Tk.create(1))
	match uV 'outer'
		none then print("none\n")
		have(tV) then print("{f(uV, tV)}\n")
	end 'outer'
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: a-by-reference-pass-of-the-matched-variable-to-an-interface-method-in-a-binding-arm-is-refused -->
`Both.both` declares its first parameter `mutable`, so a call through the interface value `w` passes `uV` by reference, whichever conformer `w` holds, and `uV` stays put in the `have(tV)` arm that binds its payload.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

union Ut
	none
	have(t Tk)
end 'Ut'

interface Both
	function both(u mutable Ut, t Tk)
end 'Both'

type W implements Both
	var k as Num

	static function create() returns Self
		return Self{k: 0}
	end 'create'

	function both(u mutable Ut, t Tk)
		match u 'inner'
			none then print("none\n")
			have(x) then x.n = 5
		end 'inner'
		print("{t.n}\n")
	end 'both'
end 'W'

function run(w Both)
	var uV = Ut.have(Tk.create(1))
	match uV 'outer'
		none then print("none\n")
		have(tV) then w.both(uV, t: tV)
	end 'outer'
end 'run'

function main() returns ExitCode
	run(W.create())
	return 0
end 'main'
```
```maxoncstderr
error E3203: <fragment>:41:19: 'uV' roots what 'outer' lends or iterates and cannot be reassigned or passed by reference inside it
```

<!-- test: a-read-only-lend-of-the-matched-variable-to-an-interface-method-beside-an-arm-binding-is-accepted -->
`Sums.total` declares no parameter `mutable`, so a call through the interface value `w` only reads `uV` and `tV`.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

union Ut
	none
	have(t Tk)
end 'Ut'

interface Sums
	function total(u Ut, t Tk) returns Num
end 'Sums'

type W implements Sums
	var k as Num

	static function create() returns Self
		return Self{k: 0}
	end 'create'

	function total(u Ut, t Tk) returns Num
		return match u 'inner'
			none gives self.k + t.n
			have(x) gives self.k + t.n + x.n
		end 'inner'
	end 'total'
end 'W'

function run(w Sums)
	let uV = Ut.have(Tk.create(1))
	match uV 'outer'
		none then print("none\n")
		have(tV) then print("{w.total(uV, t: tV)}\n")
	end 'outer'
end 'run'

function main() returns ExitCode
	run(W.create())
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: a-writing-lend-of-the-matched-variable-in-an-arm-that-names-no-binding-is-accepted -->
`poke` writes through `u` and keeps nothing, and the arm names no binding.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

union Ut
	none
	have(t Tk)
end 'Ut'

function poke(u Ut)
	match u 'inner'
		none then print("none\n")
		have(x) then x.n = 5
	end 'inner'
end 'poke'

function main() returns ExitCode
	let uV = Ut.have(Tk.create(1))
	match uV 'outer'
		none then print("none\n")
		have then poke(uV)
	end 'outer'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-read-only-lend-of-the-matched-variable-beside-an-arm-binding-is-accepted -->
`both` only reads `u` and `t`.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

union Ut
	none
	have(t Tk)
end 'Ut'

function both(u Ut, t Tk) returns Num
	let a = t.n
	let b = match u 'inner'
		none gives 0
		have(x) gives x.n
	end 'inner'
	return a + b
end 'both'

function main() returns ExitCode
	let uV = Ut.have(Tk.create(1))
	match uV 'outer'
		none then print("none\n")
		have(tV) then print("{both(uV, t: tV)}\n")
	end 'outer'
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: a-write-through-the-holder-of-a-matched-field-in-the-arm-that-lends-it-is-refused -->
`match h.u` lends a unique value that `h` holds, so `h` is read-only in the arm.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

union Ut
	none
	have(t Tk)
end 'Ut'

type H
	var u as Ut
	var n as Num

	static function create() returns Self
		return Self{u: Ut.have(Tk.create(1)), n: 0}
	end 'create'
end 'H'

function main() returns ExitCode
	let h = H.create()

	match h.u 'arm'
		none then print("none\n")
		have(t) then h.n = t.n
	end 'arm'

	print("{h.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:31:16: cannot write 'n' through 'h', which is read-only while 'arm' lends a unique value out of it
```

<!-- test: the-holder-of-a-matched-field-passed-to-a-writing-parameter-in-the-arm-that-lends-it-is-refused -->
`bump` writes `h`, which is read-only in the arm that `match h.u` lends from.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

union Ut
	none
	have(t Tk)
end 'Ut'

type H
	var u as Ut
	var n as Num

	static function create() returns Self
		return Self{u: Ut.have(Tk.create(1)), n: 0}
	end 'create'
end 'H'

function bump(h H)
	h.n = h.n + 1
end 'bump'

function main() returns ExitCode
	let h = H.create()

	match h.u 'arm'
		none then print("none\n")
		have(t) then bump(h)
	end 'arm'

	print("{h.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:35:21: 'bump' needs a writable parameter 'h' at line 27; 'h' is read-only while 'arm' is open
```

<!-- test: a-discard-bound-from-an-isolated-local-after-a-linear-union-match-releases-it -->
Inside a block, a bare `match` of an outer `isolated` linear union consumes it and its arm sinks the payload. The
later `let _ = h` moves `h` into the discard, which releases it.

```maxon
typealias Num = int(i64.min to i64.max)

union Iq7
	i0
	i1(w isolated Lw7)
end 'Iq7'

type Bx7
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx7'

type Lw7
	var keep as Bx7

	static function create(keep Bx7) returns Self
		return Self{keep: keep}
	end 'create'

	function fin(consuming self)
		print("fin {self.keep.n}\n")
		destruct self
	end 'fin'
end 'Lw7'

function mk() returns isolated Lw7
	return Lw7.create(Bx7.create(4))
end 'mk'

function sink(o isolated Lw7)
	o.fin()
end 'sink'

function main() returns ExitCode
	let h = Bx7.create(0) as isolated Bx7
	let u = Iq7.i1(mk()) as isolated Iq7
	modify h 'mh'
		match u 'x1'
			i0 then break
			i1(o) then sink(o)
		end 'x1'
	end 'mh'
	let _ = h
	return 0
end 'main'
```
```stdout
fin 4
```
```exitcode
0
```

<!-- test: an-arm-that-binds-no-isolated-payload-writes-outer-data -->
The `have` arm covers a case with an `isolated` payload and binds nothing, so it opens no window and `o` stays writable.
```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

union Iu
	none
	have(b isolated Bx)
end 'Iu'

type Holder
	var u as Iu
	var n as Num

	static function create() returns Self
		return Self{u: Iu.have(Bx.create(1)), n: 0}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	let o = Bx.create(0)

	match h.u 'm'
		none then o.n = 1
		have then o.n = 5
	end 'm'

	print("{o.n}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: an-arm-that-binds-no-isolated-payload-writes-the-root-it-matches-through -->
`match h.u` lends nothing to an arm that binds no payload, so `h` stays writable there.
```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

union Iu
	none
	have(b isolated Bx)
end 'Iu'

type Holder
	var u as Iu
	var n as Num

	static function create() returns Self
		return Self{u: Iu.have(Bx.create(1)), n: 0}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()

	match h.u 'r'
		none then h.n = 1
		have then h.n = 7
	end 'r'

	print("{h.n}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: an-arm-that-binds-an-isolated-payload-refuses-a-write-to-outer-data -->
The `have(b)` arm binds the `isolated` payload, so it is a window and `o` is read-only in it.
```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

union Iu
	none
	have(b isolated Bx)
end 'Iu'

type Holder
	var u as Iu
	var n as Num

	static function create() returns Self
		return Self{u: Iu.have(Bx.create(1)), n: 0}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	let o = Bx.create(0)

	match h.u 'm'
		none then o.n = 1
		have(b) then o.n = 5
	end 'm'

	print("{o.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:32:16: cannot write 'n' through 'o', which is read-only while 'm' is open
```

<!-- test: a-match-on-a-field-of-an-isolated-variable-opens-it-for-the-whole-statement -->
`job` is `isolated` and not open, so `match job.state` opens it for the whole statement, and every arm sees `o` as outer data whether or not it binds a payload.
```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

union St
	idle
	busy(b isolated Bx)
end 'St'

type Job
	var state as St
	var n as Num

	static function create() returns Self
		return Self{state: St.busy(Bx.create(1)), n: 0}
	end 'create'
end 'Job'

function main() returns ExitCode
	let job = Job.create() as isolated Job
	let o = Bx.create(0)

	match job.state 'm'
		idle then o.n = 1
		busy then o.n = 5
	end 'm'

	print("{o.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:31:13: cannot write 'n' through 'o', which is read-only while 'the isolated value' is open
```

<!-- test: a-consumed-isolated-union-hands-its-lone-payload-to-a-send -->
`h` is an `isolated` `let` the `match` consumes, so its lone plain payload `d` is the whole of its region: the arm sends it, the service keeps and writes it, and no name in the sender reaches it again.
```maxon
typealias Integer = int(i64.min to i64.max)

type Data
	var n as Integer
	var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "data {n}"}
	end 'create'
end 'Data'

union Holder
	empty
	full(d Data)
end 'Holder'

type Keeper
	var kept as Data

	static function create() returns Self
		return Self{kept: Data.create(0)}
	end 'create'

	export function take(d isolated Data) returns Integer
		self.kept = merge d
		self.kept.n = self.kept.n + 100
		return self.kept.n
	end 'take'

	export function peek() returns String
		return "{self.kept.label} {self.kept.n}"
	end 'peek'
end 'Keeper'

function hand(svc Keeper.handle, n Integer) returns Integer
	let h = Holder.full(Data.create(n)) as isolated Holder
	var got = 0 as Integer

	match h 'k'
		empty then got = 0
		full(d) then got = try await svc.take(d) otherwise 0
	end 'k'

	return got
end 'hand'

function main() returns ExitCode
	let svc = spawn Keeper.create()

	for i in 1 to 200 'each'
		let got = hand(svc, n: i)
		let seen = try await svc.peek() otherwise ""

		if seen != "data {i} {got}" 'wrong'
			print("mismatch at {i}: {seen}\n")
			return 1
		end 'wrong'
	end 'each'

	print("ok\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ok
```

<!-- test: error.a-cond-subjects-roots-are-read-only-in-an-arm-that-binds-nothing -->
The subject is a field path over a `cond`, and the union has a unique payload, so `h` and `g` are read-only holders in every arm, the arm that binds nothing included (ruling 48).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Holder is unique
	var slot as Slot
	var n as Count

	static function create(slot Slot, n Count) returns Self
		return Self{slot: slot, n: n}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(Slot.held(Token.create(1)), n: 0)
	let g = Holder.create(Slot.empty, n: 0)

	match (h if h.n == 0 else g).slot 'which'
		empty then h.n = 5
		held then break
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:31:14: cannot write 'n' through 'h', which is read-only while 'which' lends a unique value out of it
```

<!-- test: error.a-cond-subjects-roots-stay-put-over-a-plain-union -->
Over a union with only plain payloads the roots of a `cond` subject stay put in every arm: `first` is not reassigned there.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

function pick(pickFirst bool) returns ExitCode
	var first = Slot.held(Box.create(1))
	let second = Slot.empty

	match (first if pickFirst else second) 'which'
		empty then break
		held then first = Slot.empty
	end 'which'

	return 0
end 'pick'

function main() returns ExitCode
	return pick(true)
end 'main'
```
```maxoncstderr
error E3203: <fragment>:23:13: 'first' roots what 'which' lends or iterates and cannot be reassigned or passed by reference inside it
```

<!-- test: an-arm-that-binds-nothing-may-move-the-matched-variable -->
A bare `match` guards its variable only in an arm that binds a payload (ruling 45): the `empty` arm hands `s` to a call that keeps it.
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

function passOn(s Slot) returns Slot
	return s
end 'passOn'

function count(s Slot) returns Count
	return match s 'c'
		empty gives 7
		held(t) gives t.n
	end 'c'
end 'count'

function main() returns ExitCode
	let s = Slot.empty
	var total = 0 as Count

	match s 'which'
		empty then total = count(passOn(s))
		held(t) then total = t.n
	end 'which'

	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: error.an-arm-that-binds-a-plain-payload-may-not-reassign-the-matched-variable -->
The `held(b)` arm binds a payload of `s`, so `s` stays put there even though `Slot` holds only plain data (ruling 29).
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

function main() returns ExitCode
	var s = Slot.held(Box.create(1))

	match s 'which'
		empty then break
		held(b) then s = Slot.held(Box.create(b.n + 1))
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3203: <fragment>:22:16: 's' roots what 'which' lends or iterates and cannot be reassigned or passed by reference inside it
```

<!-- test: error.an-arm-that-binds-a-plain-payload-may-not-pass-the-matched-variable-by-reference -->
`refill` may replace `s`, so `s` is not passed by reference inside the `held(b)` arm that binds its payload.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

function refill(s mutable Slot, n Count)
	s = Slot.held(Box.create(n))
end 'refill'

function main() returns ExitCode
	var s = Slot.held(Box.create(1))

	match s 'which'
		empty then break
		held(b) then refill(s, n: b.n)
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3203: <fragment>:26:16: 's' roots what 'which' lends or iterates and cannot be reassigned or passed by reference inside it
```

<!-- test: an-arm-that-binds-a-plain-payload-writes-a-field-of-the-matched-root -->
The `plain(n)` arm binds a plain payload, so `h` stays put but stays writable: only an arm that binds a unique or isolated payload makes it read-only.
```maxon
typealias Num = int(i64.min to i64.max)

type Token is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Mixed
	none
	plain(n Num)
	owned(t Token)
end 'Mixed'

type Holder is unique
	var u as Mixed
	var n as Num

	static function create(u Mixed) returns Self
		return Self{u: u, n: 0}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(Mixed.plain(5))

	match h.u 'which'
		none then break
		plain(n) then h.n = n
		owned then break
	end 'which'

	print("{h.n}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: error.a-writing-lend-of-the-matched-variable-beside-a-binding-path-is-refused -->
The statement names the arm's binding `t` as the root of `t.n`, not as a bare argument: `clear` still may not write through `s` there (ruling 45).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

function clear(s Slot, k Count)
	match s 'c'
		empty then break
		held(t) then t.n = k
	end 'c'
end 'clear'

function main() returns ExitCode
	let s = Slot.held(Token.create(1))

	match s 'which'
		empty then break
		held(t) then clear(s, k: t.n)
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:29:16: cannot lend 's' to parameter 's' of 'clear', which writes through it, in a call that also names a binding of the arm: the binding views the matched value, and a unique value has one name; write through 's' in a statement that names no binding of the arm
```

<!-- test: error.a-bare-match-on-an-isolated-variable-moves-it-at-the-head -->
The `match` consumes the `isolated` variable as it begins, whether or not an arm binds, so the arm that binds nothing cannot read it (ruling 45).
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

function peek(s Slot) returns Count
	return match s 'p'
		empty gives 0
		held(b) gives b.n
	end 'p'
end 'peek'

function main() returns ExitCode
	let s = Slot.held(Box.create(3)) as isolated Slot

	match s 'which'
		empty then break
		held then print("{peek(s)}\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:29:26: use of moved value 's': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-lending-match-leaves-a-plain-union-readable -->
A `match` on a plain union lends and never consumes it, so a second `match` reads it again.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

function main() returns ExitCode
	let s = Slot.held(Box.create(3))
	var total = 0 as Count

	match s 'first'
		empty then break
		held(b) then total = b.n
	end 'first'

	match s 'second'
		empty then break
		held(b) then total = total + b.n
	end 'second'

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

<!-- test: a-match-inside-its-own-modify-leaves-the-isolated-variable-readable -->
Inside its own `modify` the variable is open region data and the `match` lends; after the block the bare `match` consumes it and hands the payload over owned.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

function take(b isolated Box) returns Count
	return b.n
end 'take'

function main() returns ExitCode
	let s = Slot.held(Box.create(3)) as isolated Slot

	modify s 'open'
		match s 'inner'
			empty then break
			held(b) then b.n = 8
		end 'inner'
	end 'open'

	match s 'after'
		empty then break
		held(b) then print("{take(b)}\n")
	end 'after'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
8
```

<!-- test: a-consumed-isolated-unions-lone-unique-payload-goes-to-an-isolated-parameter -->
The bare `match` consumes the `isolated` variable, so its lone unique payload is owned and goes to an `isolated` parameter.
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

function take(t isolated Token) returns Count
	return t.n
end 'take'

function main() returns ExitCode
	let s = Slot.held(Token.create(5)) as isolated Slot

	match s 'which'
		empty then break
		held(t) then print("{take(t)}\n")
	end 'which'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: a-consumed-isolated-var-refilled-on-every-path-is-matched-again -->
The first `match` consumes `s`; both branches refill it before the second `match` consumes it again (ruling 33's W2).
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

function take(b isolated Box) returns Count
	return b.n
end 'take'

function main() returns ExitCode
	var s = Slot.held(Box.create(3)) as isolated Slot
	var total = 0 as Count

	if total == 0 'refill'
		match s 'first'
			empty then break
			held(b) then total = take(b)
		end 'first'

		s = Slot.empty
	end 'refill' else 'other'
		s = Slot.empty
	end 'other'

	match s 'second'
		empty then break
		held(b) then total = total + take(b)
	end 'second'

	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: a-consumed-isolated-union-falls-through-into-a-payload-free-arm -->
The bare `match` consumes `p` once: the `full` arm releases it and falls through, and the `empty` arm, entered either way, releases nothing more.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Pack
	empty
	full(b Box, label String)
end 'Pack'

function fromFull(n Count) returns Count
	let p = Pack.full(Box.create(n), label: "full") as isolated Pack
	var total = 0 as Count

	match p 'which'
		full(b, _) then total = b.n and fallthrough
		empty then total = total + 1
	end 'which'

	return total
end 'fromFull'

function fromEmpty() returns Count
	let p = Pack.empty as isolated Pack
	var total = 0 as Count

	match p 'which'
		full(b, _) then total = b.n and fallthrough
		empty then total = total + 1
	end 'which'

	return total
end 'fromEmpty'

function main() returns ExitCode
	print("{fromFull(3)} {fromEmpty()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 1
```

<!-- test: error.a-writing-lend-of-the-matched-variable-beside-a-binding-in-a-gives-arm-is-refused -->
The `gives` arm's call names the arm's binding `t` as the root of `t.n`, so `bump` may not write through `s` there either.
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

function bump(s Slot, k Count) returns Count
	match s 'c'
		empty then break
		held(t) then t.n = k
	end 'c'

	return k
end 'bump'

function main() returns ExitCode
	let s = Slot.held(Token.create(1))

	let total = match s 'which'
		empty gives 0
		held(t) gives bump(s, k: t.n)
	end 'which'

	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:31:17: cannot lend 's' to parameter 's' of 'bump', which writes through it, in a call that also names a binding of the arm: the binding views the matched value, and a unique value has one name; write through 's' in a statement that names no binding of the arm
```

<!-- test: a-consumed-isolated-union-from-a-co-owned-holder-releases-its-payloads-once -->
Each bare `match` consumes an `isolated` union this frame may not own alone: a call's result, a word union from a call, and an `isolated` parameter. The payload is retained out of the box and the box is released at the arm's head, so the `_` payload goes with the box.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Pack
	empty
	full(b Box, label String)
end 'Pack'

union Slot
	empty
	held(b Box)
end 'Slot'

function makePack(n Count) returns isolated Pack
	return Pack.full(Box.create(n), label: "pack {n}")
end 'makePack'

function makeSlot(n Count) returns isolated Slot
	return Slot.held(Box.create(n))
end 'makeSlot'

function openPack(p isolated Pack) returns Count
	return match p 'm'
		empty gives 0
		full(b, _) gives b.n
	end 'm'
end 'openPack'

function main() returns ExitCode
	var total = 0 as Count

	match makePack(1) 'fromACall'
		empty then break
		full(b, label) then print("{label} {b.n}\n")
	end 'fromACall'

	match makeSlot(2) 'aWord'
		empty then break
		held(b) then total = total + b.n
	end 'aWord'

	let p = Pack.full(Box.create(4), label: "four") as isolated Pack
	total = total + openPack(p)

	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
pack 1 1
6
```

<!-- test: a-consumed-isolated-message-parameter-releases-its-payloads-once -->
The message's bare `match` consumes its `isolated` parameter and hands the managed payload over; the `_` payload goes with the box.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Pack
	empty
	full(b Box, label String)
end 'Pack'

type Opener
	var opened as Count

	static function create() returns Self
		return Self{opened: 0}
	end 'create'

	export function open(p isolated Pack) returns Count
		self.opened = self.opened + 1

		return match p 'm'
			empty gives 0
			full(b, _) gives b.n
		end 'm'
	end 'open'
end 'Opener'

function main() returns ExitCode
	let o = spawn Opener.create()
	var total = 0 as Count

	for i in 1 to 3 'each'
		total = total + (try await o.open(Pack.full(Box.create(i), label: "pack")) otherwise 0)
	end 'each'

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

<!-- test: error.a-tuple-rebind-of-a-cond-subjects-root-is-refused -->
A tuple assignment rebinds `first` as a plain assignment does, so it may not rebind a root of the `cond` subject inside an arm.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

function pick(pickFirst bool) returns Count
	var first = Slot.held(Box.create(1))
	var n = 0 as Count
	let second = Slot.empty

	match (first if pickFirst else second) 'which'
		empty then break
		held then (first, n) = (Slot.empty, 1)
	end 'which'

	return n
end 'pick'

function main() returns ExitCode
	print("{pick(true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3203: <fragment>:24:14: 'first' roots what 'which' lends or iterates and cannot be reassigned or passed by reference inside it
```

<!-- test: error.a-by-reference-pass-of-a-cond-subjects-root-is-refused -->
A `mutable` parameter may replace `first`, so `first` is not passed by reference inside an arm of a `match` whose `cond` subject it roots.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

function clear(s mutable Slot)
	s = Slot.empty
end 'clear'

function pick(pickFirst bool) returns Count
	var first = Slot.held(Box.create(1))
	let second = Slot.empty

	match (first if pickFirst else second) 'which'
		empty then break
		held then clear(first)
	end 'which'

	return 0
end 'pick'

function main() returns ExitCode
	print("{pick(true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3203: <fragment>:27:13: 'first' roots what 'which' lends or iterates and cannot be reassigned or passed by reference inside it
```

<!-- test: error.a-match-expression-subjects-roots-are-read-only-in-every-arm -->
A `match` expression in the subject counts as a `cond`: `h` and `g` are read-only holders in every arm of a union with a unique payload, the arm that binds nothing included (ruling 48).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Holder is unique
	var slot as Slot
	var n as Count

	static function create(slot Slot, n Count) returns Self
		return Self{slot: slot, n: n}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(Slot.held(Token.create(1)), n: 0)
	let g = Holder.create(Slot.empty, n: 0)

	match (match h.n 'pick'
		0 gives h
		default gives g
	end 'pick').slot 'which'
		empty then h.n = 5
		held then break
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:34:14: cannot write 'n' through 'h', which is read-only while 'which' lends a unique value out of it
```

<!-- test: error.a-match-read-again-on-the-next-lap-is-a-read-of-a-moved-value -->
The `match` is the last mention of `s` in the text, so the bind moves its payload out; the loop reads `s` again on the next lap, and that read is refused rather than reaching an emptied slot.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

function main() returns ExitCode
	let s = Slot.held(Box.create(3))
	var total = 0 as Count

	for _ in 0 upto 2 'lap'
		match s 'which'
			empty then break
			held(b) then total = total + b.n
		end 'which'
	end 'lap'

	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:22:9: use of moved value 's': it was moved in an earlier iteration of this loop
```

<!-- test: error.a-match-read-again-through-an-earlier-closure-is-a-read-of-a-moved-value -->
The closure made before the `match` reads `s` without the text naming `s` after the `match`, and the closure owns what it captures, so the `match` reads a moved value.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

function count(s Slot) returns Count
	return match s 'p'
		empty gives 0
		held(b) gives b.n
	end 'p'
end 'count'

function main() returns ExitCode
	let s = Slot.held(Box.create(3))
	let peek = function() gives count(s)
	var total = 0 as Count

	match s 'which'
		empty then break
		held(b) then total = b.n
	end 'which'

	print("{total + peek()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:29:8: use of moved value 's': its ownership moved to the closure that captures it
```

<!-- test: error.a-held-matched-variable-is-not-sent-from-a-binding-arm -->
`u` is held by its `match` in every arm, so it is not a send's argument there (ruling 49).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function take(s Slot) returns Count
		let n = match s 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + n
		return self.taken
	end 'take'
end 'Taker'

function main() returns ExitCode
	let u = Slot.held(Token.create(1))
	let s = spawn Taker.create()
	var got = 0 as Count

	match u 'm'
		empty then break
		held(t) then got = try await s.take(u) otherwise 0
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:42:39: cannot hand 'u' to the service it is sent to: 'u' is matched by the arm this call is in, which lends it to its bindings; move it before or after the `match`
```

<!-- test: error.a-held-matched-variable-is-not-sent-from-an-arm-that-binds-nothing -->
An arm that binds nothing may move `u` in this thread (ruling 45), but `u` is still held by its `match`, so it is not a send's argument (ruling 49).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function take(s Slot) returns Count
		let n = match s 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + n
		return self.taken
	end 'take'
end 'Taker'

function main() returns ExitCode
	let u = Slot.held(Token.create(1))
	let s = spawn Taker.create()
	var got = 0 as Count

	match u 'm'
		empty then got = try await s.take(u) otherwise 0
		held(t) then got = t.n
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:41:37: cannot hand 'u' to the service it is sent to: 'u' is matched by the arm this call is in, which lends it to its bindings; move it before or after the `match`
```

<!-- test: error.a-held-matched-variable-is-not-a-spawn-argument -->
`u` is held by its `match` in every arm, so it is not a `spawn` factory's argument there (ruling 49).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Keeper
	var u as Slot

	static function create(x Slot) returns Self
		return Self{u: x}
	end 'create'

	export function peek() returns Count
		return match self.u 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'
	end 'peek'
end 'Keeper'

function main() returns ExitCode
	let u = Slot.held(Token.create(1))
	var k = spawn Keeper.create(Slot.empty)

	match u 'm'
		empty then break
		held(t) then k = spawn Keeper.create(u)
	end 'm'

	print("{try await k.peek() otherwise 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:38:40: cannot hand 'u' to the service the `spawn` starts: 'u' is matched by the arm this call is in, which lends it to its bindings; move it before or after the `match`
```

<!-- test: error.a-call-result-that-may-be-the-held-matched-variable-is-not-sent -->
`idSlot(u)` may hand back `u`, which its `match` still holds, so the result is not the whole of its region at the send (ruling 49).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function take(s Slot) returns Count
		let n = match s 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + n
		return self.taken
	end 'take'
end 'Taker'

function idSlot(x Slot) returns Slot
	return x
end 'idSlot'

function main() returns ExitCode
	let u = Slot.held(Token.create(1))
	let s = spawn Taker.create()
	var got = 0 as Count

	match u 'm'
		empty then got = try await s.take(idSlot(u)) otherwise 0
		held(t) then got = t.n
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:45:37: 'idSlot(u)' crosses to the service it is sent to only as the whole of its region, and it may still name 'u', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-call-result-that-may-be-the-held-matched-variable-is-not-sent-from-a-binding-arm -->
The binding arm sends `idSlot(u)`, which may hand back `u` while its `match` holds it, so the result is not the whole of its region at the send (ruling 49, the model's AI a7).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function take(s Slot) returns Count
		let n = match s 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + n
		return self.taken
	end 'take'
end 'Taker'

function idSlot(x Slot) returns Slot
	return x
end 'idSlot'

function main() returns ExitCode
	var u = Slot.held(Token.create(1))
	let s = spawn Taker.create()
	var got = 0 as Count

	match u 'm'
		empty then break
		held(t) then got = try await s.take(idSlot(u)) otherwise 0
	end 'm'

	u = Slot.empty
	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:46:39: 'idSlot(u)' crosses to the service it is sent to only as the whole of its region, and it may still name 'u', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.an-element-read-subject-holds-its-container -->
`match try a.get(0) otherwise panic("one")` reads an element of `a`, so the `match` holds `a` itself, and a binding arm does not send it (rulings 49 and 50).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function takeAll(a Slots) returns Count
		self.taken = self.taken + (a.count() as Count)
		return self.taken
	end 'takeAll'
end 'Taker'

function main() returns ExitCode
	let a = Slots.create(0)
	a.push(Slot.held(Token.create(2)))
	let s = spawn Taker.create()
	var got = 0 as Count

	match try a.get(0) otherwise panic("one") 'm'
		empty then break
		held(t) then got = (try await s.takeAll(a) otherwise 0) + t.n
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:40:43: cannot hand 'a' to the service it is sent to: 'a' is matched by the arm this call is in, which lends it to its bindings; move it before or after the `match`
```

<!-- test: error.a-pop-from-an-element-read-subject-is-not-sent -->
`a.pop()` in an arm that binds nothing is legal (ruling 45.1), but its result is taken out of `a`, which the `match` holds, so it is not the whole of its region at the send (ruling 50 point 2).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function take(s Slot) returns Count
		let n = match s 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + n
		return self.taken
	end 'take'
end 'Taker'

function main() returns ExitCode
	let a = Slots.create(0)
	a.push(Slot.held(Token.create(2)))
	let s = spawn Taker.create()
	var got = 0 as Count

	match try a.get(0) otherwise panic("one") 'm'
		empty then break
		held then got = try await s.take(try a.pop() otherwise panic("one")) otherwise 0
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:45:36: 'try a.pop() otherwise panic("one")' crosses to the service it is sent to only as the whole of its region, and it may still name 'a', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-swap-out-of-a-matched-self-field-is-not-sent -->
`match h.u` holds `h`, the root of the path, so what `swap h.u with Slot.empty` takes out of the matched field is held and is not sent (ruling 50 points 1 and 2).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Holder
	var u as isolated Slot

	static function create(u isolated Slot) returns Self
		return Self{u: u}
	end 'create'
end 'Holder'

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function take(s Slot) returns Count
		let n = match s 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + n
		return self.taken
	end 'take'
end 'Taker'

function main() returns ExitCode
	let h = Holder.create(Slot.held(Token.create(1)))
	let s = spawn Taker.create()
	var got = 0 as Count

	match h.u 'm'
		empty then break
		held then got = try await s.take(swap h.u with Slot.empty) otherwise 0
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:50:36: 'swap h.u with Slot.empty' crosses to the service it is sent to only as the whole of its region, and it may still name 'h', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-pop-result-is-not-a-spawn-argument -->
What `a.pop()` takes out of `a` is held by the `match` over `a.get(0)`, so it is not a `spawn` factory's argument (ruling 50 point 2).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

type Keeper
	var u as Slot

	static function create(x Slot) returns Self
		return Self{u: x}
	end 'create'

	export function peek() returns Count
		return match self.u 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'
	end 'peek'
end 'Keeper'

function main() returns ExitCode
	let a = Slots.create(0)
	a.push(Slot.held(Token.create(2)))
	var k = spawn Keeper.create(Slot.empty)

	match try a.get(0) otherwise panic("one") 'm'
		empty then break
		held then k = spawn Keeper.create(try a.pop() otherwise panic("one"))
	end 'm'

	print("{try await k.peek() otherwise 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:41:37: 'try a.pop() otherwise panic("one")' crosses to the service the `spawn` starts only as the whole of its region, and it may still name 'a', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-pop-result-is-not-a-reply -->
What `a.pop()` takes out of `a` is held by the `match` over `a.get(0)`, so a service method does not return it to its sender from the arm (ruling 50 point 2).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

type Vault
	var served as Count

	static function create() returns Self
		return Self{served: 0}
	end 'create'

	export function give() returns Slot
		let a = Slots.create(0)
		a.push(Slot.held(Token.create(1)))
		self.served = self.served + 1

		match try a.get(0) otherwise panic("one") 'm'
			empty then break
			held then return try a.pop() otherwise panic("one")
		end 'm'

		return Slot.empty
	end 'give'
end 'Vault'

function main() returns ExitCode
	let v = spawn Vault.create()
	let r = try await v.give() otherwise Slot.empty

	let n = match r 'r'
		empty gives 0
		held(t) gives t.n
	end 'r'

	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:33:21: 'try a.pop() otherwise panic("one")' crosses to the sender awaiting the reply only as the whole of its region, and it may still name 'a', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-pop-result-is-not-in-an-isolated-position -->
What `a.pop()` takes out of `a` is held by the `match` over `a.get(0)`, so it is not an `isolated` parameter's argument (ruling 50 point 2).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

function isoSlot(x isolated Slot) returns Count
	return match x 'c'
		empty gives 0
		held(t) gives t.n
	end 'c'
end 'isoSlot'

function main() returns ExitCode
	let a = Slots.create(0)
	a.push(Slot.held(Token.create(2)))
	var got = 0 as Count

	match try a.get(0) otherwise panic("one") 'm'
		empty then break
		held then got = isoSlot(try a.pop() otherwise panic("one"))
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:33:27: 'try a.pop() otherwise panic("one")' crosses to an `isolated` position only as the whole of its region, and it may still name 'a', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-swap-result-is-not-an-isolated-factory-argument -->
What `swap h.u with Slot.empty` takes out of the matched field is held by the `match` over `h.u`, so it is not an `isolated` factory parameter's argument (ruling 50 point 2).
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b isolated Box)
end 'Slot'

type Holder
	var u as isolated Slot

	static function create(u isolated Slot) returns Self
		return Self{u: u}
	end 'create'
end 'Holder'

type Keeper
	var u as isolated Slot

	static function create(x isolated Slot) returns Self
		return Self{u: x}
	end 'create'

	export function peek() returns Count
		return match self.u 'c'
			empty gives 0
			held(b) gives b.n
		end 'c'
	end 'peek'
end 'Keeper'

function main() returns ExitCode
	let h = Holder.create(Slot.held(Box.create(1)))
	var k = spawn Keeper.create(Slot.empty)

	match h.u 'm'
		empty then break
		held then k = spawn Keeper.create(swap h.u with Slot.empty)
	end 'm'

	print("{try await k.peek() otherwise 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:46:37: 'swap h.u with Slot.empty' crosses to an `isolated` position only as the whole of its region, and it may still name 'h', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-swap-result-is-not-an-isolated-reply -->
What `swap h.u with Slot.empty` takes out of the matched field is held by the `match` over `h.u`, so a method that `returns isolated` does not return it from the arm (ruling 50 point 2).
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b isolated Box)
end 'Slot'

type Holder
	var u as isolated Slot

	static function create(u isolated Slot) returns Self
		return Self{u: u}
	end 'create'
end 'Holder'

type Vault
	var served as Count

	static function create() returns Self
		return Self{served: 0}
	end 'create'

	export function give() returns isolated Slot
		let h = Holder.create(Slot.held(Box.create(1)))
		self.served = self.served + 1

		match h.u 'm'
			empty then break
			held then return swap h.u with Slot.empty
		end 'm'

		return Slot.empty
	end 'give'
end 'Vault'

function main() returns ExitCode
	let v = spawn Vault.create()
	let r = try await v.give() otherwise panic("gone")

	let n = match r 'r'
		empty gives 0
		held(b) gives b.n
	end 'r'

	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:38:21: 'swap h.u with Slot.empty' crosses to an `isolated` position only as the whole of its region, and it may still name 'h', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-swap-result-is-not-in-an-isolated-position -->
What `swap h.u with Slot.empty` takes out of the matched field is held by the `match` over `h.u`, so it is not an `isolated` parameter's argument (ruling 50 point 2).
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b isolated Box)
end 'Slot'

type Holder
	var u as isolated Slot

	static function create(u isolated Slot) returns Self
		return Self{u: u}
	end 'create'
end 'Holder'

function isoSlot(x isolated Slot) returns Count
	return match x 'c'
		empty gives 0
		held(b) gives b.n
	end 'c'
end 'isoSlot'

function main() returns ExitCode
	let h = Holder.create(Slot.held(Box.create(1)))
	var got = 0 as Count

	match h.u 'm'
		empty then break
		held then got = isoSlot(swap h.u with Slot.empty)
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:38:27: 'swap h.u with Slot.empty' crosses to an `isolated` position only as the whole of its region, and it may still name 'h', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-call-result-is-not-a-spawn-argument -->
`idSlot(u)` may hand back `u`, which its `match` still holds, so the result is not a `spawn` factory's argument (ruling 50 point 1).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Keeper
	var u as Slot

	static function create(x Slot) returns Self
		return Self{u: x}
	end 'create'

	export function peek() returns Count
		return match self.u 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'
	end 'peek'
end 'Keeper'

function idSlot(x Slot) returns Slot
	return x
end 'idSlot'

function main() returns ExitCode
	let u = Slot.held(Token.create(1))
	var k = spawn Keeper.create(Slot.empty)

	match u 'm'
		empty then k = spawn Keeper.create(idSlot(u))
		held then break
	end 'm'

	print("{try await k.peek() otherwise 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:41:38: 'idSlot(u)' crosses to the service the `spawn` starts only as the whole of its region, and it may still name 'u', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-call-result-is-not-a-reply -->
`idSlot(u)` may hand back `u`, which its `match` still holds, so a service method does not return it to its sender from the arm (ruling 50 point 1).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Vault
	var served as Count

	static function create() returns Self
		return Self{served: 0}
	end 'create'

	export function give() returns Slot
		let u = Slot.held(Token.create(1))
		self.served = self.served + 1

		match u 'm'
			empty then return idSlot(u)
			held then break
		end 'm'

		return Slot.empty
	end 'give'
end 'Vault'

function idSlot(x Slot) returns Slot
	return x
end 'idSlot'

function main() returns ExitCode
	let v = spawn Vault.create()
	let r = try await v.give() otherwise Slot.empty

	let n = match r 'r'
		empty gives 0
		held(t) gives t.n
	end 'r'

	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:29:22: 'idSlot(u)' crosses to the sender awaiting the reply only as the whole of its region, and it may still name 'u', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-call-result-is-not-in-an-isolated-position -->
`idSlot(u)` may hand back `u`, which its `match` still holds, so the result is not an `isolated` parameter's argument (ruling 50 point 1).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

function isoSlot(x isolated Slot) returns Count
	return match x 'c'
		empty gives 0
		held(t) gives t.n
	end 'c'
end 'isoSlot'

function idSlot(x Slot) returns Slot
	return x
end 'idSlot'

function main() returns ExitCode
	let u = Slot.held(Token.create(1))
	var got = 0 as Count

	match u 'm'
		empty then got = isoSlot(idSlot(u))
		held then break
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:33:28: 'idSlot(u)' crosses to an `isolated` position only as the whole of its region, and it may still name 'u', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-renamed-held-result-is-not-sent -->
`r` is assigned what `a.pop()` took out of `a`, which the `match` holds, so `r` is held too and is not sent (ruling 50 point 1: a binding a held value is moved into).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function take(s Slot) returns Count
		let n = match s 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + n
		return self.taken
	end 'take'
end 'Taker'

function main() returns ExitCode
	let a = Slots.create(0)
	a.push(Slot.held(Token.create(2)))
	let s = spawn Taker.create()
	var r = Slot.empty
	var got = 0 as Count

	match try a.get(0) otherwise panic("one") 'm'
		empty then r = try a.pop() otherwise panic("one") and fallthrough
		held then got = try await s.take(r) otherwise 0
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:46:36: 'r' crosses to the service it is sent to only as the whole of its region, and it may still name 'a', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-holder-that-stored-a-held-value-is-not-sent -->
`box` is a holder that `a.pop()`'s held result was stored into a field of, so `box` is held too and is not sent (ruling 50 point 1: a holder a held value is stored into).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

type Bag
	var item as Slot

	static function create() returns Self
		return Self{item: Slot.empty}
	end 'create'
end 'Bag'

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function takeBag(b Bag) returns Count
		let weight = match b.item 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + weight
		return self.taken
	end 'takeBag'
end 'Taker'

function main() returns ExitCode
	let a = Slots.create(0)
	a.push(Slot.held(Token.create(2)))
	let box = Bag.create()
	let s = spawn Taker.create()
	var got = 0 as Count

	match try a.get(0) otherwise panic("one") 'm'
		empty then box.item = try a.pop() otherwise panic("one") and fallthrough
		held then got = try await s.takeBag(box) otherwise 0
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:54:39: 'box' crosses to the service it is sent to only as the whole of its region, and it may still name 'a', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-root-whose-field-alias-stored-a-held-value-is-not-sent -->
`inner` is a destructured alias of the tuple field `crate.pr`, and what `a.pop()` took out of `a` was stored into it, so `crate`, the root the alias names, is held too and is not sent (ruling H-10: a holder chain through a destructured alias).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

type Bag
	var item as Slot

	static function create() returns Self
		return Self{item: Slot.empty}
	end 'create'
end 'Bag'

typealias Pair = (Bag, Count)

type Crate
	var pr as Pair

	static function create() returns Self
		return Self{pr: (Bag.create(), 0)}
	end 'create'
end 'Crate'

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function takeCrate(c Crate) returns Count
		let (b, _) = c.pr
		let weight = match b.item 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + weight
		return self.taken
	end 'takeCrate'
end 'Taker'

function main() returns ExitCode
	let a = Slots.create(0)
	a.push(Slot.held(Token.create(2)))
	let crate = Crate.create()
	let (inner, _) = crate.pr
	let s = spawn Taker.create()
	var got = 0 as Count

	match try a.get(0) otherwise panic("one") 'm'
		empty then inner.item = try a.pop() otherwise panic("one") and fallthrough
		held then got = try await s.takeCrate(crate) otherwise 0
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:66:41: 'crate' crosses to the service it is sent to only as the whole of its region, and it may still name 'a', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-root-whose-field-alias-is-matched-is-not-sent -->
`inner` is a destructured alias of the tuple field `crate.pr` and the `match` holds it across its arms, so `crate`, the root the alias names, is held and is not sent from an arm (ruling H-10: the subject's alias chain).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Bag
	var item as Slot

	static function create() returns Self
		return Self{item: Slot.empty}
	end 'create'
end 'Bag'

typealias Pair = (Bag, Count)

type Crate
	var pr as Pair

	static function create() returns Self
		return Self{pr: (Bag.create(), 0)}
	end 'create'
end 'Crate'

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function takeCrate(c Crate) returns Count
		let (b, _) = c.pr
		let weight = match b.item 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + weight
		return self.taken
	end 'takeCrate'
end 'Taker'

function main() returns ExitCode
	let crate = Crate.create()
	crate.pr.0.item = Slot.held(Token.create(2))
	let (inner, _) = crate.pr
	let s = spawn Taker.create()
	var got = 0 as Count

	match inner.item 'm'
		empty then break
		held then got = try await s.takeCrate(crate) otherwise 0
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:63:41: cannot hand 'crate' to the service it is sent to: 'crate' is matched by the arm this call is in, which lends it to its bindings; move it before or after the `match`
```

<!-- test: error.a-root-two-holders-up-whose-field-alias-stored-a-held-value-is-not-sent -->
`inner` aliases a tuple field of `shelf.cr`, a holder two hops below `shelf`, and what `a.pop()` took out of `a` was stored into it, so `shelf` is held too and is not sent (ruling H-10: the chain is followed to the root).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

type Bag
	var item as Slot

	static function create() returns Self
		return Self{item: Slot.empty}
	end 'create'
end 'Bag'

typealias Pair = (Bag, Count)

type Crate
	var pr as Pair

	static function create() returns Self
		return Self{pr: (Bag.create(), 0)}
	end 'create'
end 'Crate'

type Shelf
	var cr as Crate

	static function create() returns Self
		return Self{cr: Crate.create()}
	end 'create'
end 'Shelf'

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function takeShelf(sh Shelf) returns Count
		let (b, _) = sh.cr.pr
		let weight = match b.item 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + weight
		return self.taken
	end 'takeShelf'
end 'Taker'

function main() returns ExitCode
	let a = Slots.create(0)
	a.push(Slot.held(Token.create(2)))
	let shelf = Shelf.create()
	let (inner, _) = shelf.cr.pr
	let s = spawn Taker.create()
	var got = 0 as Count

	match try a.get(0) otherwise panic("one") 'm'
		empty then inner.item = try a.pop() otherwise panic("one") and fallthrough
		held then got = try await s.takeShelf(shelf) otherwise 0
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:74:41: 'shelf' crosses to the service it is sent to only as the whole of its region, and it may still name 'a', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: a-root-whose-field-alias-stored-an-unheld-value-is-sent -->
`inner` is a destructured alias of the tuple field `crate.pr`, but what is stored into it is a fresh `Slot.empty`, which no `match` holds, so `crate` stays free and is sent (ruling H-10, accepted control).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

type Bag
	var item as Slot

	static function create() returns Self
		return Self{item: Slot.empty}
	end 'create'
end 'Bag'

typealias Pair = (Bag, Count)

type Crate
	var pr as Pair

	static function create() returns Self
		return Self{pr: (Bag.create(), 0)}
	end 'create'
end 'Crate'

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function takeCrate(c Crate) returns Count
		let (b, _) = c.pr
		let weight = match b.item 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + weight
		return self.taken
	end 'takeCrate'
end 'Taker'

function main() returns ExitCode
	let a = Slots.create(0)
	a.push(Slot.empty)
	let crate = Crate.create()
	let (inner, _) = crate.pr
	let s = spawn Taker.create()
	var got = 0 as Count

	match try a.get(0) otherwise panic("one") 'm'
		empty then inner.item = Slot.empty and fallthrough
		held then got = try await s.takeCrate(crate) otherwise 0
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```


<!-- test: error.a-root-whose-plain-field-alias-stored-a-held-value-is-not-an-isolated-argument -->
`inner` is a plain alias of the field `crate.bag`, whose `Bag` holds no unique value, and what `a.pop()` took out of `a` was stored through it, so `crate`, the root the alias names, is held and is not an `isolated` parameter's argument (ruling H-11). The case pins the order of two refusals: without the holder chain the same program is refused as "'crate' is not isolated".
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

typealias Slots = Array with Slot

type Bag
	var item as Slot

	static function create() returns Self
		return Self{item: Slot.empty}
	end 'create'
end 'Bag'

type Crate
	var bag as Bag
	var n as Count

	static function create() returns Self
		return Self{bag: Bag.create(), n: 3}
	end 'create'
end 'Crate'

function take(c isolated Crate) returns Count
	return c.n
end 'take'

function main() returns ExitCode
	let a = Slots.create(0)
	a.push(Slot.held(Box.create(2)))
	let crate = Crate.create()
	let inner = crate.bag
	var got = 0 as Count

	match try a.get(0) otherwise panic("one") 'm'
		empty then inner.item = try a.pop() otherwise panic("one") and fallthrough
		held then got = take(crate)
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:49:24: 'crate' crosses to an `isolated` position only as the whole of its region, and it may still name 'a', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-root-whose-plain-field-alias-is-matched-is-not-an-isolated-argument -->
`inner` is a plain alias of the field `crate.bag`, whose `Bag` holds no unique value, and the `match` holds `inner.item` across its arms, so `crate`, the root the alias names, is matched by the arm too and is not an `isolated` parameter's argument from it (ruling H-11). The case pins the order of two refusals: without the holder chain the same program is refused as "'crate' is not isolated".
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

type Bag
	var item as Slot

	static function create() returns Self
		return Self{item: Slot.held(Box.create(2))}
	end 'create'
end 'Bag'

type Crate
	var bag as Bag
	var n as Count

	static function create() returns Self
		return Self{bag: Bag.create(), n: 3}
	end 'create'
end 'Crate'

function take(c isolated Crate) returns Count
	return c.n
end 'take'

function main() returns ExitCode
	let crate = Crate.create()
	let inner = crate.bag
	var got = 0 as Count

	match inner.item 'm'
		empty then break
		held then got = take(crate)
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:45:24: cannot hand 'crate' to an `isolated` position: 'crate' is matched by the arm this call is in, which lends it to its bindings; move it before or after the `match`
```

<!-- test: a-swap-and-a-pop-in-an-arm-that-binds-nothing-are-accepted -->
In an arm that binds no payload, whether it names a payload case (`held then`) or not (`empty then`), `pop` and `swap` on the matched subject stay legal, and what they take out goes on after the `match` (ruling 45.1; ruling 50 point 2).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Pod
	empty
	held(b isolated Box)
end 'Pod'

type Holder
	var u as isolated Pod

	static function create(u isolated Pod) returns Self
		return Self{u: u}
	end 'create'
end 'Holder'

function weigh(x Slot) returns Count
	return match x 'c'
		empty gives 0
		held(t) gives t.n
	end 'c'
end 'weigh'

function pour(x isolated Pod) returns Count
	return match x 'c'
		empty gives 0
		held(b) gives b.n
	end 'c'
end 'pour'

function main() returns ExitCode
	let a = Slots.create(0)
	a.push(Slot.held(Token.create(2)))
	let h = Holder.create(Pod.empty)
	var popped = Slot.empty
	var moved = Pod.empty as isolated Pod

	match try a.get(0) otherwise panic("one") 'p'
		empty then break
		held then popped = try a.pop() otherwise panic("one")
	end 'p'

	match h.u 'q'
		empty then moved = swap h.u with Pod.held(Box.create(3))
		held then break
	end 'q'

	let first = weigh(popped)
	let second = pour(moved)
	let third = pour(swap h.u with Pod.empty)
	print("{first} {second} {third}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 0 3
```

<!-- test: error.a-held-value-crosses-from-a-subject-that-is-a-var -->
A `var` is held like a `let`: what `a.pop()` takes out of the `var` `a` is held by the `match` over `a.get(0)`, so it is not sent, and `a` is reassigned only after the `match` (ruling 50 point 2).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function take(s Slot) returns Count
		let n = match s 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + n
		return self.taken
	end 'take'
end 'Taker'

function main() returns ExitCode
	var a = Slots.create(0)
	a.push(Slot.held(Token.create(2)))
	let s = spawn Taker.create()
	var got = 0 as Count

	match try a.get(0) otherwise panic("one") 'm'
		empty then break
		held then got = try await s.take(try a.pop() otherwise panic("one")) otherwise 0
	end 'm'

	a = Slots.create(0)
	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:45:36: 'try a.pop() otherwise panic("one")' crosses to the service it is sent to only as the whole of its region, and it may still name 'a', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-value-crosses-from-a-subject-that-is-a-mutable-parameter -->
A `mutable` parameter is held like a local: what `a.pop()` takes out of the `mutable` parameter `a` is held by the `match` over `a.get(0)`, so it is not sent (ruling 50 point 2).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function take(s Slot) returns Count
		let n = match s 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + n
		return self.taken
	end 'take'
end 'Taker'

typealias Slots = Array with Slot

function relay(a mutable Slots, s Taker.handle) returns Count
	var got = 0 as Count

	match try a.get(0) otherwise panic("one") 'm'
		empty then break
		held then got = try await s.take(try a.pop() otherwise panic("one")) otherwise 0
	end 'm'

	return got
end 'relay'

function main() returns ExitCode
	let a = Slots.create(0)
	a.push(Slot.held(Token.create(2)))
	let s = spawn Taker.create()
	print("{relay(a, s: s)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:42:36: 'try a.pop() otherwise panic("one")' crosses to the service it is sent to only as the whole of its region, and it may still name 'a', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-value-crosses-from-a-subject-that-is-a-self-field -->
`match self.u` in a method holds `self`, the root of the path, so what `swap self.u with Slot.empty` takes out of the matched field is not sent (ruling 48 point 1; ruling 50 point 2).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function take(s Slot) returns Count
		let n = match s 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + n
		return self.taken
	end 'take'
end 'Taker'

type Shelf
	var u as isolated Slot

	static function create(x isolated Slot) returns Self
		return Self{u: x}
	end 'create'

	function relay(s Taker.handle) returns Count
		var got = 0 as Count

		match self.u 'm'
			empty then break
			held then got = try await s.take(swap self.u with Slot.empty) otherwise 0
		end 'm'

		return got
	end 'relay'
end 'Shelf'

function main() returns ExitCode
	let shelf = Shelf.create(Slot.held(Token.create(1)))
	let s = spawn Taker.create()
	print("{shelf.relay(s)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:47:37: 'swap self.u with Slot.empty' crosses to the service it is sent to only as the whole of its region, and it may still name 'self', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-value-crosses-from-a-subject-that-is-a-local-inside-modify -->
A local declared inside a `modify` is held like any other: what `a.pop()` takes out of it is held by the `match` over `a.get(0)`, so it is not a `spawn` factory's argument (ruling 50 point 2).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Keeper
	var n as Count

	static function create(x Slot) returns Self
		let n = match x 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		return Self{n: n}
	end 'create'

	export function size() returns Count
		return self.n
	end 'size'
end 'Keeper'

function main() returns ExitCode
	let b = Box.create(1) as isolated Box

	modify b 'open'
		let a = Slots.create(0)
		a.push(Slot.held(Token.create(2)))

		match try a.get(0) otherwise panic("one") 'm'
			empty then break
			held then let _ = spawn Keeper.create(try a.pop() otherwise panic("one"))
		end 'm'
	end 'open'

	let k = spawn Keeper.create(Slot.empty)
	print("{try await k.size() otherwise 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:53:42: 'try a.pop() otherwise panic("one")' crosses to the service the `spawn` starts only as the whole of its region, and it may still name 'a', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-value-crosses-from-a-subject-that-is-an-acquired-state-inside-acquire -->
Inside `acquire`, the name `s` is the acquired state and resolves to the binding it shadows, so what `s.a.pop()` takes out of the state is held by the `match` over `s.a.get(0)` and is not a `spawn` factory's argument (#235; ruling 50 point 2).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

type Vault
	var a as Slots

	static function create(x Slot) returns Self
		let a = Slots.create(0)
		a.push(x)
		return Self{a: a}
	end 'create'
end 'Vault'

type Keeper
	var n as Count

	static function create(x Slot) returns Self
		let n = match x 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		return Self{n: n}
	end 'create'

	export function size() returns Count
		return self.n
	end 'size'
end 'Keeper'

function main() returns ExitCode
	let s = spawn Vault.create(Slot.held(Token.create(2)))

	acquire s 'a1'
		match try s.a.get(0) otherwise panic("one") 'm'
			empty then break
			held then let _ = spawn Keeper.create(try s.a.pop() otherwise panic("one"))
		end 'm'
	end 'a1' otherwise 'gone'
		let _ = 0 as Count
	end 'gone'

	let k = spawn Keeper.create(Slot.empty)
	print("{try await k.size() otherwise 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:52:42: 'try s.a.pop() otherwise panic("one")' crosses to the service the `spawn` starts only as the whole of its region, and it may still name 's', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-value-crosses-from-a-subject-that-is-an-isolated-local-inside-its-own-modify -->
Inside its own `modify` an `isolated` local is open region data and the `match` lends from it, so what `a.pop()` takes out of it is held by the `match` over `a.get(0)` and is not a `spawn` factory's argument (#235; ruling 50 point 2).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

type Keeper
	var n as Count

	static function create(x Slot) returns Self
		let n = match x 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		return Self{n: n}
	end 'create'

	export function size() returns Count
		return self.n
	end 'size'
end 'Keeper'

function main() returns ExitCode
	let a = Slots.create(0) as isolated Slots

	modify a 'open'
		a.push(Slot.held(Token.create(2)))

		match try a.get(0) otherwise panic("one") 'm'
			empty then break
			held then let _ = spawn Keeper.create(try a.pop() otherwise panic("one"))
		end 'm'
	end 'open'

	let k = spawn Keeper.create(Slot.empty)
	print("{try await k.size() otherwise 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:44:42: 'try a.pop() otherwise panic("one")' crosses to the service the `spawn` starts only as the whole of its region, and it may still name 'a', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-swap-result-is-not-a-spawn-argument -->
A crossing door is chosen by the parameter's declaration, so a held `swap` result handed to an unmarked `spawn` factory parameter meets the `spawn` door, which refuses it because it may still name the holder the `match` holds (ruling 50 point 2). The case pins the order of two refusals: without the hold the same `swap` result is refused at the unmarked factory parameter as "'create' hands back mutable data of the isolated region it opens", as `error.an-unheld-swap-result-is-opened-by-an-unmarked-spawn-parameter` shows.
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Holder
	var u as isolated Slot

	static function create(u isolated Slot) returns Self
		return Self{u: u}
	end 'create'
end 'Holder'

type Keeper
	var u as Slot

	static function create(x Slot) returns Self
		return Self{u: x}
	end 'create'

	export function peek() returns Count
		return match self.u 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'
	end 'peek'
end 'Keeper'

function main() returns ExitCode
	let h = Holder.create(Slot.held(Token.create(1)))
	var k = spawn Keeper.create(Slot.empty)

	match h.u 'm'
		empty then break
		held then k = spawn Keeper.create(swap h.u with Slot.empty)
	end 'm'

	print("{try await k.peek() otherwise 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:46:37: 'swap h.u with Slot.empty' crosses to the service the `spawn` starts only as the whole of its region, and it may still name 'h', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.an-unheld-swap-result-is-opened-by-an-unmarked-spawn-parameter -->
A swap result is isolated, so an unmarked `spawn` factory parameter opens it rather than crossing it, and this pin goes red if a swap result ever slips through the unmarked spawn door.
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Holder
	var u as isolated Slot

	static function create(u isolated Slot) returns Self
		return Self{u: u}
	end 'create'
end 'Holder'

type Keeper
	var u as Slot

	static function create(x Slot) returns Self
		return Self{u: x}
	end 'create'

	export function peek() returns Count
		return match self.u 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'
	end 'peek'
end 'Keeper'

function main() returns ExitCode
	let h = Holder.create(Slot.held(Token.create(1)))
	var k = spawn Keeper.create(Slot.empty)

	k = spawn Keeper.create(swap h.u with Slot.empty)

	print("{try await k.peek() otherwise 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:44:19: 'create' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: error.a-held-swap-result-is-not-a-reply -->
What `swap h.u with Slot.empty` takes out of the matched field is held by the `match` over `h.u`, so a service method does not return it to its sender from the arm through an unmarked return (ruling 50 point 2; #282). The case pins the order of two refusals: without the hold the same `return` is refused as "'swap h.u with Slot.empty' is isolated, the result is not `isolated`".
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Holder
	var u as isolated Slot

	static function create(u isolated Slot) returns Self
		return Self{u: u}
	end 'create'
end 'Holder'

type Vault
	var served as Count

	static function create() returns Self
		return Self{served: 0}
	end 'create'

	export function give() returns Slot
		let h = Holder.create(Slot.held(Token.create(1)))
		self.served = self.served + 1

		match h.u 'm'
			empty then break
			held then return swap h.u with Slot.empty
		end 'm'

		return Slot.empty
	end 'give'
end 'Vault'

function main() returns ExitCode
	let v = spawn Vault.create()
	let r = try await v.give() otherwise Slot.empty

	let n = match r 'r'
		empty gives 0
		held(t) gives t.n
	end 'r'

	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:38:21: 'swap h.u with Slot.empty' crosses to the sender awaiting the reply only as the whole of its region, and it may still name 'h', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-call-subjects-argument-stays-put-in-a-binding-arm -->
A call subject holds the arguments that can reach its result, so `match next(v)` holds `v` and the `held(x)` arm may not reassign it (ruling 50 point 4, the roots a call subject adds stay put).
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

function next(b Box) returns Slot
	if b.n < 3 'more'
		return Slot.held(Box.create(b.n + 1))
	end 'more'
	return Slot.empty
end 'next'

function main() returns ExitCode
	var v = Box.create(0)

	for _ in 0 upto 5 'lap'
		match next(v) 'm'
			empty then break
			held(x) then v = Box.create(x.n)
		end 'm'
	end 'lap'

	print("{v.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3203: <fragment>:30:17: 'v' roots what 'm' lends or iterates and cannot be reassigned or passed by reference inside it
```

<!-- test: error.an-element-read-subjects-container-stays-put-in-a-binding-arm -->
An element read subject `match try xs.get(i) otherwise …` holds `xs` (the container it reads, not the index `i`), so the `held(b)` arm may not reassign it (ruling 50 point 4, the roots a call subject adds stay put).
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

typealias Slots = Array with Slot

function main() returns ExitCode
	var xs = Slots.create(0)
	xs.push(Slot.held(Box.create(2)))
	let i = 0 as ElementIndex

	match try xs.get(i) otherwise panic("none") 'm'
		empty then break
		held(b) then xs = Slots.create(b.n)
	end 'm'

	print("{xs.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3203: <fragment>:26:16: 'xs' roots what 'm' lends or iterates and cannot be reassigned or passed by reference inside it
```

<!-- test: a-call-result-bound-before-the-match-leaves-its-argument-free -->
Binding the call's result first (`let r = next(v)`) makes `r` the subject, so `v` is free to be reassigned in the binding arm.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

function next(b Box) returns Slot
	if b.n < 3 'more'
		return Slot.held(Box.create(b.n + 1))
	end 'more'
	return Slot.empty
end 'next'

function main() returns ExitCode
	var v = Box.create(0)

	for _ in 0 upto 5 'lap'
		let r = next(v)
		match r 'm'
			empty then break
			held(x) then v = Box.create(x.n)
		end 'm'
	end 'lap'

	print("{v.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: a-value-type-field-of-the-matched-record-is-sent-from-a-binding-arm -->
A value-type field (`generation`) of the held root `self` is a copy, so a binding arm of `match self.slot` may send it to a service.
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function note(g Count) returns Count
		self.taken = self.taken + g
		return self.taken
	end 'note'
end 'Taker'

type Shelf
	var slot as Slot
	var generation as Count

	static function create(x Slot) returns Self
		return Self{slot: x, generation: 4}
	end 'create'

	function relay(s Taker.handle) returns Count
		var got = 0 as Count

		match self.slot 'm'
			empty then break
			held(t) then got = (try await s.note(self.generation) otherwise 0) + t.n
		end 'm'

		return got
	end 'relay'
end 'Shelf'

function main() returns ExitCode
	let shelf = Shelf.create(Slot.held(Token.create(1)))
	let s = spawn Taker.create()
	print("{shelf.relay(s)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: a-value-type-argument-of-a-call-subject-is-reassigned-in-a-binding-arm -->
`k` is an `int` argument of the call subject, and a value type is never a root (ruling 50 point 4), so the `held(x)` arm reassigns it.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

function next(b Box, step Count) returns Slot
	if step < 3 'more'
		return Slot.held(Box.create(step + b.n))
	end 'more'
	return Slot.empty
end 'next'

function main() returns ExitCode
	let v = Box.create(0)
	var k = 0 as Count

	for _ in 0 upto 5 'lap'
		match next(v, step: k) 'm'
			empty then break
			held(x) then k = x.n + 1
		end 'm'
	end 'lap'

	print("{k}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: a-value-type-argument-of-a-call-subject-is-sent-from-a-binding-arm -->
`k` is an `int` argument of the call subject, so it is no root of the `match` and the `held(x)` arm sends it to a service (ruling 50 point 1).
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function note(g Count) returns Count
		self.taken = self.taken + g
		return self.taken
	end 'note'
end 'Taker'

function next(b Box, step Count) returns Slot
	return Slot.held(Box.create(b.n + step))
end 'next'

function main() returns ExitCode
	let v = Box.create(1)
	let s = spawn Taker.create()
	let k = 4 as Count
	var got = 0 as Count

	match next(v, step: k) 'm'
		empty then break
		held(x) then got = (try await s.note(k) otherwise 0) + x.n
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9
```

<!-- test: an-element-read-subjects-index-is-reassigned-in-a-binding-arm -->
`match try xs.get(i) otherwise …` holds `xs` and not the index `i`, so the `held` arm reassigns `i` (ruling 50 point 4).
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	held(b Box)
end 'Slot'

typealias Slots = Array with Slot

function main() returns ExitCode
	let xs = Slots.create(0)
	xs.push(Slot.held(Box.create(2)))
	var i = 0 as ElementIndex

	match try xs.get(i) otherwise panic("none") 'm'
		empty then break
		held then i = i + 1
	end 'm'

	print("{i}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: error.a-copy-of-a-matched-root-is-not-sent-after-the-statement -->
`w = a` copies the root `a` of the holding `match` in one statement, and the copy is sent in the next, so it is held too (ruling 1: a value's hold is decided when it is minted).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

typealias Slots = Array with Slot

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function takeAll(a Slots) returns Count
		self.taken = self.taken + (a.count() as Count)
		return self.taken
	end 'takeAll'
end 'Taker'

function main() returns ExitCode
	let a = Slots.create(0)
	a.push(Slot.held(Token.create(2)))
	let s = spawn Taker.create()
	var w = Slots.create(0)
	var got = 0 as Count

	match try a.get(0) otherwise panic("one") 'm'
		held then w = a and fallthrough
		empty then got = try await s.takeAll(w) otherwise 0
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:41:40: 'w' crosses to the service it is sent to only as the whole of its region, and it may still name 'a', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-closures-call-result-is-not-sent -->
`f` captures `u`, which the `match` holds, so what `f()` returns may still name `u` and is not sent to a service (ruling 50 points 3 and 1).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Taker
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function take(s Slot) returns Count
		let n = match s 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + n
		return self.taken
	end 'take'
end 'Taker'

function idSlot(x Slot) returns Slot
	return x
end 'idSlot'

function main() returns ExitCode
	let u = Slot.held(Token.create(1))
	let s = spawn Taker.create()
	var f = function() gives Slot.empty
	var got = 0 as Count

	match u 'm'
		empty then f = function() gives idSlot(u) and fallthrough
		held then got = try await s.take(f()) otherwise 0
	end 'm'

	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:47:36: 'f()' crosses to the service it is sent to only as the whole of its region, and it may still name 'u', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-closures-call-result-is-not-a-spawn-argument -->
`f` captures `u`, which the `match` holds, so what `f()` returns may still name `u` and is not a `spawn` factory's argument (ruling 50 points 3 and 1).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Keeper
	var u as Slot

	static function create(x Slot) returns Self
		return Self{u: x}
	end 'create'

	export function peek() returns Count
		return 0
	end 'peek'
end 'Keeper'

function idSlot(x Slot) returns Slot
	return x
end 'idSlot'

function main() returns ExitCode
	let u = Slot.held(Token.create(1))
	var k = spawn Keeper.create(Slot.empty)
	var f = function() gives Slot.empty
	let got = 0 as Count

	match u 'm'
		empty then f = function() gives idSlot(u) and fallthrough
		held then k = spawn Keeper.create(f())
	end 'm'

	print("{got}{try await k.peek() otherwise 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:41:37: 'f()' crosses to the service the `spawn` starts only as the whole of its region, and it may still name 'u', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```

<!-- test: error.a-held-closures-call-result-is-not-a-reply -->
`f` captures `u`, which the `match` holds, so what `f()` returns may still name `u` and a service method does not return it to its sender (ruling 50 points 3 and 1).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	held(t Token)
end 'Slot'

type Vault
	var taken as Count

	static function create() returns Self
		return Self{taken: 0}
	end 'create'

	export function give() returns Slot
		let u = Slot.held(Token.create(1))
		var f = function() gives Slot.empty

		match u 'm'
			empty then f = function() gives idSlot(u) and fallthrough
			held then return f()
		end 'm'
	end 'give'

	export function take(s Slot) returns Count
		let n = match s 'c'
			empty gives 0
			held(t) gives t.n
		end 'c'

		self.taken = self.taken + n
		return self.taken
	end 'take'
end 'Vault'

function idSlot(x Slot) returns Slot
	return x
end 'idSlot'

function main() returns ExitCode
	let v = spawn Vault.create()
	let got = try await v.give() otherwise Slot.empty
	let n = match got 'g'
		empty gives 0
		held(t) gives t.n
	end 'g'

	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:30:21: 'f()' crosses to the sender awaiting the reply only as the whole of its region, and it may still name 'u', which the `match` 'm' holds across its arms; hand it over before or after the `match`
```
