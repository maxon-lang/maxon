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
