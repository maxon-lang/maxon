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

<!-- test: a-writing-lend-of-the-matched-variable-to-a-function-value-beside-an-arm-binding-is-refused -->
`BothFn` declares its first parameter `mutable`, so a call through `f` writes through `uV` while naming `tV`.
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
error E3102: <fragment>:32:18: cannot lend 'uV' to a `mutable` parameter of 'BothFn', which writes through it, in a call that also names a binding of the arm: the binding views the matched value, and a unique value has one name; write through 'uV' in a statement that names no binding of the arm
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

<!-- test: a-writing-lend-of-the-matched-variable-to-an-interface-method-beside-an-arm-binding-is-refused -->
`Both.both` declares its first parameter `mutable`, so a call through the interface value `w` writes through `uV` while naming `tV`, whichever conformer `w` holds.
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
error E3102: <fragment>:41:19: cannot lend 'uV' to a `mutable` parameter of 'Both.both', which writes through it, in a call that also names a binding of the arm: the binding views the matched value, and a unique value has one name; write through 'uV' in a statement that names no binding of the arm
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
