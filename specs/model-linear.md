---
feature: model-linear
status: experimental
keywords: [model, capmodel, region, capability]
category: semantics
---

# Linear values and finalizers

## Documentation

These cases are programs of the capmodel executable model, translated to Maxon by
`scripts/model-specs/generate.py` (`docs/SPECS.md`, "Model-derived specs") and pinned to the model's verdict on
linear types, finalizers and their hand-backs. A program the model accepts runs and exits 0; one it refuses fails to compile, with the diagnostic
the compiler gives. A case is written only where the compiler and the model agree, so the suite fails
the day the compiler stops agreeing with a verdict the model pinned. The rule set is `core-m3w-fa13`.

## Tests

<!-- test: model-finding-ad-match-window-both-arms -->
The model (`core-m3w-fa13`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `match-window-both-arms` of redteamAD/t10.txt, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Ub
	u0
	u1(b isolated Box)
end 'Ub'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function useR(r Rl)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let u = Ub.u1(Box.create(0)) as isolated Ub
	let v = Rl.create(0) as isolated Rl
	match u 'x1'
		u0 then useR(v)
		u1 then useS(giveS(v))
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:55:16: 'giveS' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: model-finding-ad-match-window-both-arms-gives -->
The model (`core-m3w-fa13`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `match-window-both-arms-giveS` of redteamAD/t10.txt, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Ub
	u0
	u1(b isolated Box)
end 'Ub'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let u = Ub.u1(Box.create(0)) as isolated Ub
	let v = Rl.create(0) as isolated Rl
	match u 'x1'
		u0 then useS(giveS(v))
		u1 then useS(giveS(v))
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:50:16: 'giveS' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: model-finding-ad-state-window-reply-union-state -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `state-window-reply-union-state` of redteamAD/t10.txt, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

type S
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'

	function go(r Rl)
		useS(giveS(r))
	end 'go'

	export function bgo(r Rl)
		self.go(r)
	end 'bgo'
end 'S'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let s = spawn S.create(0)
	try s.bgo(Rl.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ad-lit-no-window -->
The model (`core-m3w-fa13`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `lit-no-window` of redteamAD/t13.txt, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let v = Rl.create(0) as isolated Rl
	useS(Sn.notSent(v))
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:31:18: cannot store an isolated 'Rl' into 'Sn.notSent.r', which holds a mutable 'Rl'; `merge` it first
```

<!-- test: model-finding-ad-v-match-window -->
The model (`core-m3w-fa13`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `v-match-window` of redteamAD/t5.txt, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Ub
	u0
	u1(b isolated Box)
end 'Ub'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let u = Ub.u1(Box.create(0)) as isolated Ub
	let v = Rl.create(0) as isolated Rl
	match u 'x1'
		u0 then break
		u1 then useS(giveS(v))
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:51:16: 'giveS' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: model-finding-ad-oneopen-state-field-path -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `oneopen-state-field-path` of redteamAD/t7.txt, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type S
	var k as Num
	var b as isolated Bx

	static function create(k Num, b isolated Bx) returns Self
		return Self{k: k, b: b}
	end 'create'

	export function go(r Rl)
		let _ = self.b.n
		useS(giveS(r))
	end 'go'
end 'S'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let s = spawn S.create(0, b: Bx.create(0))
	try s.go(Rl.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-rec-list-drop-tail -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224, E4014. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `rec-list-drop-tail` of redteamAE/p11.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ll
	nil
	cons(h Rl, t Ll)
end 'Ll'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function drain(l Ll)
	match l 'x1'
		nil then break
		cons(h, _) then h.fin(1)
	end 'x1'
end 'drain'

function main() returns ExitCode
	drain(Ll.cons(Rl.create(1), t: Ll.cons(Rl.create(2), t: Ll.nil)))
	return 0
end 'main'
```
```maxoncstderr
error E4014: <fragment>:4:7: type 'Ll' contains a reference cycle (via Ll → cons.t: Ll); recursive type references are not allowed
error E3224: <fragment>:25:3: the case 'cons' carries the linear value 't', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-finding-ae-mutual-union-drop -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224, E4014. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `mutual-union-drop` of redteamAE/p11.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union A
	a0
	a1(b B)
end 'A'

union B
	b0
	b1(a A, r Rl)
end 'B'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function drainA(a A)
	match a 'x1'
		a0 then break
		a1 then break
	end 'x1'
end 'drainA'

function main() returns ExitCode
	drainA(A.a1(B.b1(A.a0, r: Rl.create(1))))
	return 0
end 'main'
```
```maxoncstderr
error E4014: <fragment>:4:7: type 'A' contains a reference cycle (via A → a1.b: B → b1.a: A); recursive type references are not allowed
error E3224: <fragment>:30:3: the case 'a1' carries the linear value 'b', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-finding-ae-union-method-plain -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-SELF`; the compiler refuses it with E3224. Model program `union-method-plain` of redteamAE/p11.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
	function isOk() returns Num
		match self 'x1'
			ok then return 1
			notSent then return 0
		end 'x1'
	end 'isOk'
end 'Sn'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function main() returns ExitCode
	let u = Sn.notSent(Rl.create(1))
	let _ = u.isOk()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:9:4: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
```

<!-- test: model-finding-ae-mixed-union-iso-nonlinear-arm-write -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `mixed-union-iso-nonlinear-arm-write` of redteamAE/p12.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Mx
	m0(x Rl)
	m1(y isolated Bk)
end 'Mx'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

type Bk
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'
end 'Bk'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let b = Bx.create(0)
	let u = Mx.m1(Bk.create(1))
	match u 'x1'
		m0(x) then x.fin(1)
		m1 then b.n = 3
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-mixed-union-iso-nonlinear-arm-move -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `mixed-union-iso-nonlinear-arm-move` of redteamAE/p12.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Mx
	m0(x Rl)
	m1(y isolated Bk)
end 'Mx'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

type Bk
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'
end 'Bk'

function takeI(b isolated Bk)
	let _ = b.code
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
```exitcode
0
```

<!-- test: model-finding-ae-w-match-consume-in-one-call-open -->
The model (`core-m3w-fa13`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `w-match-consume-in-one-call-open` of redteamAE/p14.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

type Hd
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'

	function run(u Sn)
		match u 'x1'
			ok then break
			notSent(q) then useS(giveS(q))
		end 'x1'
	end 'run'
end 'Hd'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let bx = Hd.create(0) as isolated Hd
	let u = Sn.notSent(Rl.create(1))
	bx.run(u)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:51:9: 'u' is mutable outside 'bx'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-finding-ae-match-in-loop-refill -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `match-in-loop-refill` of redteamAE/p15.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function main() returns ExitCode
	var u = Sn.notSent(Rl.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		match u 'x2'
			ok then break
			notSent(r) then r.fin(1)
		end 'x2'
		u = Sn.notSent(Rl.create(3))
		i = 0
	end 'w1'
	match u 'x3'
		ok then break
		notSent(r) then r.fin(2)
	end 'x3'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-match-call-temp -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `match-call-temp` of redteamAE/p15.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function mkS() returns Sn
	return Sn.notSent(Rl.create(0))
end 'mkS'

function main() returns ExitCode
	match mkS() 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-rep-h2a-iso-payload-moved -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `rep-H2a-iso-payload-moved` of redteamAE/p20.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function giveH(r isolated Rl) returns Sh
	return Sh.held(r)
end 'giveH'

function useI(r isolated Rl)
	r.fin(1)
end 'useI'

function main() returns ExitCode
	let v = giveH(Rl.create(3))
	match v 'x1'
		none then break
		held(q) then useI(q)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-linear-union-method-self-twice -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-SELF`; the compiler refuses it with E3224. Model program `linear-union-method-self-twice` of redteamAE/p23.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
	function drop()
		match self 'x1'
			ok then break
			notSent(q) then q.fin(1)
		end 'x1'
	end 'drop'
end 'Sn'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function main() returns ExitCode
	let u = Sn.notSent(Rl.create(1))
	u.drop()
	u.drop()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:9:4: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
```

<!-- test: model-finding-ae-record-method-linear-self-field-via-match -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `record-method-linear-self-field-via-match` of redteamAE/p23.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

type Hs
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(s Sn)
		match s 'x1'
			ok then break
			notSent(q) then q.fin(1)
		end 'x1'
	end 'eat'
end 'Hs'

function main() returns ExitCode
	let h = Hs.create(0)
	let u = Sn.notSent(Rl.create(1))
	h.eat(u)
	h.eat(u)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:41:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-finding-ae-arm-payload-pair-iso -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `arm-payload-pair-iso` of redteamAE/p25.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Tw
	none
	both(a isolated Rl, b isolated Rl)
end 'Tw'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Rl'

function use2(a isolated Rl, b isolated Rl)
	a.fin(1)
	b.fin(2)
end 'use2'

function main() returns ExitCode
	let u = Tw.both(Rl.create(1), b: Rl.create(2))
	match u 'x1'
		none then break
		both(a, b) then use2(a, b: b)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-iso-payload-plain -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `iso-payload-plain` of redteamAE/p5.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function main() returns ExitCode
	let u = Sh.held(Rl.create(0))
	match u 'x1'
		none then break
		held(r) then r.fin(1)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-iso-payload-iso-local -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `iso-payload-iso-local` of redteamAE/p5.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function main() returns ExitCode
	let u = Sh.held(Rl.create(0)) as isolated Sh
	match u 'x1'
		none then break
		held(r) then r.fin(1)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-iso-payload-drop -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `iso-payload-drop` of redteamAE/p5.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function main() returns ExitCode
	let u = Sh.held(Rl.create(0))
	match u 'x1'
		none then break
		held then break
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:26:3: the case 'held' carries the linear value 'r', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-finding-ae-iso-payload-two-matches -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `iso-payload-two-matches` of redteamAE/p5.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function main() returns ExitCode
	let u = Sh.held(Rl.create(0))
	match u 'x1'
		none then break
		held(r) then r.fin(1)
	end 'x1'
	match u 'x2'
		none then break
		held(r) then r.fin(2)
	end 'x2'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-finding-ae-iso-payload-return -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `iso-payload-return` of redteamAE/p5.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function unwrap(u Sh) returns isolated Rl
	match u 'x1'
		none then return Rl.create(0)
		held(r) then return r
	end 'x1'
end 'unwrap'

function main() returns ExitCode
	let r = unwrap(Sh.held(Rl.create(1)))
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-iso-payload-return-nofin -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `iso-payload-return-nofin` of redteamAE/p5.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function unwrap(u Sh) returns isolated Rl
	match u 'x1'
		none then return Rl.create(0)
		held(r) then return r
	end 'x1'
end 'unwrap'

function main() returns ExitCode
	let _ = unwrap(Sh.held(Rl.create(1)))
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:31:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-finding-ae-iso-payload-break -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `iso-payload-break` of redteamAE/p5.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function main() returns ExitCode
	var i = 2 as Num
	while i != 0 'w1'
		let u = Sh.held(Rl.create(0))
		match u 'x2'
			none then break
			held then break 'w1'
		end 'x2'
		i = 0
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:28:4: the case 'held' carries the linear value 'r', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-finding-ae-iso-payload-array-of-union -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3224. Model program `iso-payload-array-of-union` of redteamAE/p5.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias ShArray = Array with Sh

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function main() returns ExitCode
	let xs = ShArray.create(0)
	xs.push(Sh.held(Rl.create(0)))
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:3:11: 'Sh' is a linear value and cannot be a type argument of 'Array': a linear value lives only in locals, parameters and message arguments, so no container, promise, generic record or generic function holds one
```

<!-- test: model-finding-ae-varpayload-iso-param-twice -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `varpayload-iso-param-twice` of redteamAE/p7.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function giveH(r isolated Rl) returns Sh
	return Sh.held(r)
end 'giveH'

function useI(r isolated Rl)
	r.fin(1)
end 'useI'

function main() returns ExitCode
	let v = giveH(Rl.create(3))
	match v 'x1'
		none then break
		held(q) then useI(q)
	end 'x1'
	match v 'x2'
		none then break
		held(q) then useI(q)
	end 'x2'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:36:8: use of moved value 'v': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-finding-ae-varpayload-plain-param -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `varpayload-plain-param` of redteamAE/p7.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function use(r Rl)
	r.fin(1)
end 'use'

function main() returns ExitCode
	let v = Sh.held(Rl.create(3))
	match v 'x1'
		none then break
		held(q) then use(q)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-varpayload-plain-param-twice -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `varpayload-plain-param-twice` of redteamAE/p7.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function use(r Rl)
	r.fin(1)
end 'use'

function main() returns ExitCode
	let v = Sh.held(Rl.create(3))
	match v 'x1'
		none then break
		held(q) then use(q)
	end 'x1'
	match v 'x2'
		none then break
		held(q) then use(q)
	end 'x2'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:8: use of moved value 'v': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-finding-ae-varpayload-iso-recv -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `varpayload-iso-recv` of redteamAE/p7.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function giveH(r isolated Rl) returns Sh
	return Sh.held(r)
end 'giveH'

function main() returns ExitCode
	let v = giveH(Rl.create(3))
	match v 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-varpayload-iso-return -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `varpayload-iso-return` of redteamAE/p7.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function unwrap(h Sh) returns isolated Rl
	match h 'x1'
		none then return Rl.create(0)
		held(q) then return q
	end 'x1'
end 'unwrap'

function main() returns ExitCode
	let r = unwrap(Sh.held(Rl.create(3)))
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-varpayload-iso-param-fn -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `varpayload-iso-param-fn` of redteamAE/p8.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function giveH(r isolated Rl) returns Sh
	return Sh.held(r)
end 'giveH'

function useI(r isolated Rl)
	r.fin(1)
end 'useI'

function eat(h Sh)
	match h 'x1'
		none then break
		held(q) then useI(q)
	end 'x1'
end 'eat'

function main() returns ExitCode
	eat(giveH(Rl.create(3)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-record-field-linear-inferred -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `record-field-linear-inferred` of redteamAE/p9.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

type Hold
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hold'

function main() returns ExitCode
	let _ = Hold.create(0)
	let r = Rl.create(1)
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-array-linear-inferred -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3224. Model program `array-linear-inferred` of redteamAE/p9.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias RlArray = Array with Rl

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function main() returns ExitCode
	let _ = RlArray.create(0)
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:3:11: 'Rl' is a linear value and cannot be a type argument of 'Array': a linear value lives only in locals, parameters and message arguments, so no container, promise, generic record or generic function holds one
```

<!-- test: model-finding-ae-freeze-linear-union -->
The model (`core-m3w-fa13`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `freeze-linear-union` of redteamAE/p9.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function main() returns ExitCode
	let u = Sh.held(Rl.create(1)) as isolated Sh
	let _ = freeze u
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:24:17: 'Sh' is unique (its field 'r' holds a unique 'Rl') and cannot be frozen
```

<!-- test: model-finding-ae-merge-linear-iso -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3224. Model program `merge-linear-iso` of redteamAE/p9.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

type Hold
	var keep as Rl

	static function create(keep Rl) returns Self
		return Self{keep: keep}
	end 'create'
end 'Hold'

function main() returns ExitCode
	let h = Hold.create(Rl.create(0))
	let r = Rl.create(1) as isolated Rl
	h.keep = merge r
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:18:6: the field 'keep' holds a linear value, and a linear value lives only in locals, parameters and message arguments: a field would keep it past the code that must consume it
```

<!-- test: model-finding-ae-merge-linear-into-union -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `merge-linear-into-union` of redteamAE/p9.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let r = Rl.create(1) as isolated Rl
	let s = Sn.notSent(merge r)
	useS(s)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-merge-linear-twice -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `merge-linear-twice` of redteamAE/p9.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let r = Rl.create(1) as isolated Rl
	let s = Sn.notSent(merge r)
	let t = Sn.notSent(merge r)
	useS(s)
	useS(t)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:27: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-finding-ae-freeze-linear-record -->
The model (`core-m3w-fa13`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `freeze-linear-record` of redteamAE/p9.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function main() returns ExitCode
	let r = Rl.create(1) as isolated Rl
	let _ = freeze r
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:19:17: 'Rl' is unique and cannot be frozen
```

<!-- test: model-finding-ae-clone-linear-record -->
The model (`core-m3w-fa13`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `clone-linear-record` of redteamAE/p9.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

function main() returns ExitCode
	let r = Rl.create(1)
	let c = r.clone()
	r.fin(1)
	c.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:19:10: 'Rl' is unique and cannot be cloned
```

<!-- test: model-hand-ab-t-send-arg-then-throw -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `t-send-arg-then-throw` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Auth
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function check(r Resp)
		r.fin(1)
	end 'check'

	export function bump()
		self.n = 1
	end 'bump'
end 'Auth'

function main() returns ExitCode
	let a = spawn Auth.create(0)
	try a.check(Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(b) then b.fin(1)
			serviceStopped(b) then b.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-m-peek-in-arg -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-peek-in-arg` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0)
	r.fin(r.peek())
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-m-fin-arg-self -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m-fin-arg-self` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Q
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self, o Q)
		let _ = o.n
		destruct self
	end 'fin'
end 'Q'

function main() returns ExitCode
	let q = Q.create(0)
	q.fin(q)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:19:8: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-m-fin-arg-other-moves -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-fin-arg-other-moves` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Q
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self, o Resp)
		o.fin(1)
		destruct self
	end 'fin'
end 'Q'

function main() returns ExitCode
	let q = Q.create(0)
	let r = Resp.create(0)
	q.fin(r)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-m-fin-arg-other-dropped -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `m-fin-arg-other-dropped` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Q
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self, o Resp)
		let _ = o.code
		destruct self
	end 'fin'
end 'Q'

function main() returns ExitCode
	let q = Q.create(0)
	let r = Resp.create(0)
	q.fin(r)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:28:11: this drops 'o', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-m-fin-recv-moved-then-arg -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-fin-recv-moved-then-arg` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Q
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Q'

function main() returns ExitCode
	let q = Q.create(0)
	q.fin(q.n)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-m-return-self -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-return-self` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Q
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'

	function keep(consuming self) returns Q
		return self
	end 'keep'
end 'Q'

function main() returns ExitCode
	let q = Q.create(0)
	let q2 = q.keep()
	q2.fin()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-m-return-self-dropped -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `m-return-self-dropped` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Q
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'

	function keep(consuming self) returns Q
		return self
	end 'keep'
end 'Q'

function main() returns ExitCode
	let q = Q.create(0)
	let _ = q.keep()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:23:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-m-fin-in-peek -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-fin-in-peek` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Q
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'

	function pk()
		let c = Q.create(1)
		c.fin()
	end 'pk'
end 'Q'

function main() returns ExitCode
	let q = Q.create(0)
	q.pk()
	q.fin()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-m-use-self-field-before -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-use-self-field-before` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Q
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		let _ = self.n
		destruct self
	end 'fin'
end 'Q'

function main() returns ExitCode
	let q = Q.create(0)
	q.fin()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-m-self-moved-then-destruct -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-self-moved-then-destruct` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Q
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'

	function g(consuming self)
		self.fin()
		destruct self
	end 'g'
end 'Q'

function main() returns ExitCode
	let q = Q.create(0)
	q.g()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:17:12: use of moved value '__self': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-u-nested-union-dropped -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `u-nested-union-dropped` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

union Outer
	a(s Sent)
	b
end 'Outer'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let o = Outer.a(Sent.notSent(Resp.create(0)))
	match o 'x1'
		b then break
		a then break
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:35:3: the case 'a' carries the linear value 's', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ab-u-array-of-union -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3224. Model program `u-array-of-union` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias SentArray = Array with Sent

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let _ = SentArray.create(0)
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:3:11: 'Sent' is a linear value and cannot be a type argument of 'Array': a linear value lives only in locals, parameters and message arguments, so no container, promise, generic record or generic function holds one
```

<!-- test: model-hand-ab-u-union-method -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u-union-method` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union W
	full(r Resp)
	empty
	function isFull() returns Num
		return 1
	end 'isFull'
end 'W'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let w = W.full(Resp.create(0))
	let z = w.isFull()
	match w 'x1'
		empty then break
		full(r) then r.fin(z)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-u-union-method-drop -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `u-union-method-drop` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union W
	full(r Resp)
	empty
	function isFull() returns Num
		return 1
	end 'isFull'
end 'W'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let w = W.full(Resp.create(0))
	let _ = w.isFull()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:32:2: this drops 'w', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-u-match-in-loop-break -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `u-match-in-loop-break` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let i = 1 as Num
	while i != 0 'w1'
		let s = giveS(Resp.create(0))
		match s 'x2'
			ok then break
			notSent then break 'w1'
		end 'x2'
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:36:4: the case 'notSent' carries the linear value 'r', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ab-u-match-scrut-expr -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u-match-scrut-expr` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	match giveS(Resp.create(0)) 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-u-match-scrut-expr-bare -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `u-match-scrut-expr-bare` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	match giveS(Resp.create(0)) 'x1'
		ok then break
		notSent then break
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:33:3: the case 'notSent' carries the linear value 'r', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ab-u-match-param -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u-match-param` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function g(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'g'

function main() returns ExitCode
	g(giveS(Resp.create(0)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-u-match-param-drop -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `u-match-param-drop` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function g(s Sent)
	let _ = s
end 'g'

function main() returns ExitCode
	g(giveS(Resp.create(0)))
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:30:10: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-u-return-union-from-arm -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u-return-union-from-arm` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function g(s Sent) returns Sent
	match s 'x1'
		ok then return Sent.ok
		notSent(r) then return Sent.notSent(r)
	end 'x1'
end 'g'

function main() returns ExitCode
	useS(g(giveS(Resp.create(0))))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-u-union-ok-case-only -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u-union-ok-case-only` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function mkOk() returns Sent
	return Sent.ok
end 'mkOk'

function main() returns ExitCode
	match mkOk() 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-u-union-two-payloads-one -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `u-union-two-payloads-one` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Two
	both(a Resp, b Resp)
	none
end 'Two'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let t = Two.both(Resp.create(0), b: Resp.create(1))
	match t 'x1'
		none then break
		both(a, _) then a.fin(1)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:30:3: the case 'both' carries the linear value 'b', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ab-u-union-same-name -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u-union-same-name` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Tw
	l(a Resp)
	r(a Resp)
end 'Tw'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let t = Tw.l(Resp.create(0))
	match t 'x1'
		l(a) then a.fin(1)
		r(a) then a.fin(2)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-y-try-handler-var -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-try-handler-var` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Auth
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function check(r Resp)
		r.fin(1)
	end 'check'

	export function bump()
		self.n = 1
	end 'bump'
end 'Auth'

function main() returns ExitCode
	let a = spawn(capacity: 1) Auth.create(0)
	let r = Resp.create(0) as isolated Resp
	try a.check(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(r) then r.fin(2)
			serviceStopped(r) then r.fin(2)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-y-try-handback-original-after -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `y-try-handback-original-after` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Auth
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function check(r Resp)
		r.fin(1)
	end 'check'

	export function bump()
		self.n = 1
	end 'bump'
end 'Auth'

function main() returns ExitCode
	let a = spawn(capacity: 1) Auth.create(0)
	let r = Resp.create(0) as isolated Resp
	try a.check(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(b) then b.fin(3)
			serviceStopped(b) then b.fin(3)
		end 'x2'
	end 'h1'
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:46:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-y-try-handback-other-linear -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-try-handback-other-linear` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Auth
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function check(r Resp)
		r.fin(1)
	end 'check'

	export function bump()
		self.n = 1
	end 'bump'
end 'Auth'

function main() returns ExitCode
	let a = spawn(capacity: 1) Auth.create(0)
	let o = Resp.create(5)
	let r = Resp.create(0) as isolated Resp
	try a.check(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(b) then b.fin(3)
			serviceStopped(b) then b.fin(3)
		end 'x2'
	end 'h1'
	o.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-y-try-handback-in-loop -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-try-handback-in-loop` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Auth
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function check(r Resp)
		r.fin(1)
	end 'check'

	export function bump()
		self.n = 1
	end 'bump'
end 'Auth'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let a = spawn(capacity: 1) Auth.create(0)
	var i = 3 as Num
	while i != 0 'w1'
		let r = Resp.create(0) as isolated Resp
		try a.check(r) otherwise (sendFailure) 'h2'
			match sendFailure 'x3'
				mailboxFull(b) then b.fin(3)
				serviceStopped(b) then b.fin(3)
			end 'x3'
		end 'h2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-y-try-handback-loop-carried -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `y-try-handback-loop-carried` of redteamAB.hand_ab1, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Auth
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function check(r Resp)
		r.fin(1)
	end 'check'

	export function bump()
		self.n = 1
	end 'bump'
end 'Auth'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let a = spawn(capacity: 1) Auth.create(0)
	let r = Resp.create(0) as isolated Resp
	var i = 3 as Num
	while i != 0 'w1'
		try a.check(r) otherwise (sendFailure) 'h2'
			match sendFailure 'x3'
				mailboxFull(b) then b.fin(3)
				serviceStopped(b) then b.fin(3)
			end 'x3'
		end 'h2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:46:15: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ab-k-isolated-union-drop -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `k-isolated-union-drop` of redteamAB.hand_ab10, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let _ = giveS(Resp.create(0)) as isolated Sent
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:32:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-k-ret-immutable -->
The model (`core-m3w-fa13`) refuses it by `R4-RESULT`; the compiler refuses it with E3209. Model program `k-ret-immutable` of redteamAB.hand_ab10, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function f() returns immutable Resp
	return Resp.create(0)
end 'f'

function main() returns ExitCode
	let _ = f()
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:22:9: a return does not fit the declared result: 'Resp.create(0)' is mutable, the result is immutable; freeze it
```

<!-- test: model-hand-ab-min-temp-receiver -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `min-temp-receiver` of redteamAB.hand_ab10, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function mkR() returns Resp
	return Resp.create(0)
end 'mkR'

function main() returns ExitCode
	let _ = mkR().peek()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:26:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-min-temp-field -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `min-temp-field` of redteamAB.hand_ab10, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let _ = Resp.create(0).code
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:22:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-min-discard-var -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `min-discard-var` of redteamAB.hand_ab10, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0)
	let _ = r
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:24:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-s-slot-param-var-after -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `s-slot-param-var-after` of redteamAB.hand_ab12, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function eatM(r mutable Resp)
	r.fin(1)
end 'eatM'

function main() returns ExitCode
	var r = Resp.create(0)
	eatM(r)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:22:4: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
error E3224: <fragment>:28:4: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
```

<!-- test: model-hand-ab-s-slot-param-two -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `s-slot-param-two` of redteamAB.hand_ab12, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function eat2(a mutable Resp, b mutable Resp)
	a.fin(1)
	b.fin(2)
end 'eat2'

function main() returns ExitCode
	var r = Resp.create(0)
	eat2(r, b: r)
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:22:4: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
```

<!-- test: model-hand-ab-f-factory-finalizer -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f-factory-finalizer` of redteamAB.hand_ab12, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Sx
	var n as Num

	static function create(k Num) returns Self
		let r = Resp.create(k)
		r.fin(1)
		return Self{n: k}
	end 'create'
end 'Sx'

function main() returns ExitCode
	let _ = spawn Sx.create(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-f-factory-drops -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `f-factory-drops` of redteamAB.hand_ab12, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Sx
	var n as Num

	static function create(k Num) returns Self
		let _ = Resp.create(k)
		return Self{n: k}
	end 'create'
end 'Sx'

function main() returns ExitCode
	let _ = spawn Sx.create(1)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:26:3: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-f-behave-param-wrap-union -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f-behave-param-wrap-union` of redteamAB.hand_ab12, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Sx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function m(r Resp)
		let s = giveS(r)
		useS(s)
	end 'm'
end 'Sx'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let a = spawn Sx.create(0)
	let r = Resp.create(0) as isolated Resp
	try a.m(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(b) then b.fin(1)
			serviceStopped(b) then b.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-f-for-body-linear-local -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f-for-body-linear-local` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BoxArray = Array with Box

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = BoxArray.create(0)
	a.push(Box.create(1))
	for _ in a 'f1'
		let r = Resp.create(0)
		r.fin(1)
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-f-for-break-linear -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `f-for-break-linear` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BoxArray = Array with Box

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = BoxArray.create(0)
	a.push(Box.create(1))
	for _ in a 'f1'
		let _ = Resp.create(0)
		break
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:35:3: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-f-for-continue-linear -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `f-for-continue-linear` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BoxArray = Array with Box

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = BoxArray.create(0)
	a.push(Box.create(1))
	for _ in a 'f1'
		let _ = Resp.create(0)
		continue
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:35:3: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-f-for-outer-consume -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `f-for-outer-consume` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BoxArray = Array with Box

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = BoxArray.create(0)
	a.push(Box.create(1))
	let r = Resp.create(0)
	for _ in a 'f1'
		r.fin(1)
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:35:3: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ab-f-for-outer-consume-after -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f-for-outer-consume-after` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BoxArray = Array with Box

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = BoxArray.create(0)
	a.push(Box.create(1))
	let r = Resp.create(0)
	for _ in a 'f1'
		let _ = 0 as Num
	end 'f1'
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-f-for-return-outer -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `f-for-return-outer` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BoxArray = Array with Box

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function f()
	let a = BoxArray.create(0)
	a.push(Box.create(1))
	let r = Resp.create(0)
	for _ in a 'f1'
		return
	end 'f1'
	r.fin(1)
end 'f'

function main() returns ExitCode
	f()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:35:3: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-f-while-two-iterations -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `f-while-two-iterations` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0)
	var i = 2 as Num
	while i != 0 'w1'
		if i != 0 'i2'
			r.fin(1)
		end 'i2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:4: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ab-f-while-var-refill-conditional -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3223. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f-while-var-refill-conditional` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var r = Resp.create(0)
	var i = 2 as Num
	while i != 0 'w1'
		r.fin(1)
		if i != 0 'i2'
			r = Resp.create(1)
		end 'i2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:35:2: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-f-while-var-refill-nonetwork -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f-while-var-refill-nonetwork` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var r = Resp.create(0)
	var i = 2 as Num
	while i != 0 'w1'
		r.fin(1)
		r = Resp.create(1)
		i = lg(0)
	end 'w1'
	r.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-f-while-break-after-consume -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `f-while-break-after-consume` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0)
	let i = 2 as Num
	while i != 0 'w1'
		r.fin(1)
		break
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:24:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-f-while-break-before-consume -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `f-while-break-before-consume` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0)
	let i = 2 as Num
	while i != 0 'w1'
		if i != 0 'i2'
			break
		end 'i2'
		r.fin(1)
	end 'w1'
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:3: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ab-f-while-cond-consume -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `f-while-cond-consume` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function ckr(r Resp) returns Num
	r.fin(1)
	return 0
end 'ckr'

function main() returns ExitCode
	let r = Resp.create(0)
	while ckr(r) != 0 'w1'
		let _ = 0 as Num
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:12: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ab-f-while-cond-fresh -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f-while-cond-fresh` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function ckr(r Resp) returns Num
	r.fin(1)
	return 0
end 'ckr'

function main() returns ExitCode
	while ckr(Resp.create(0)) != 0 'w1'
		let _ = 0 as Num
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-f-nested-loop-break-outer -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f-nested-loop-break-outer` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	var i = 1 as Num
	while i != 0 'w1'
		let r = Resp.create(0)
		var j = 1 as Num
		while j != 0 'w2'
			j = 0
			break
		end 'w2'
		r.fin(1)
		i = 0
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-f-loop-continue-after-consume -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f-loop-continue-after-consume` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var i = 2 as Num
	while i != 0 'w1'
		let r = Resp.create(0)
		r.fin(1)
		i = lg(0)
		continue
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-f-loop-continue-before-consume-else -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `f-loop-continue-before-consume-else` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var i = 2 as Num
	while i != 0 'w1'
		let r = Resp.create(0)
		i = lg(0)
		if 1 != 0 'i2'
			continue
		end 'i2'
		r.fin(1)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:31:4: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-f-match-arm-in-loop-continue -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `f-match-arm-in-loop-continue` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	var i = 2 as Num
	while i != 0 'w1'
		i = lg(0)
		let s = giveS(Resp.create(0))
		match s 'x2'
			ok then break
			notSent then continue
		end 'x2'
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:41:4: the case 'notSent' carries the linear value 'r', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ab-s-handback-twice-one-full -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-handback-twice-one-full` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Sv
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mk() returns Resp
		return Resp.create(0)
	end 'mk'

	function eat(r Resp)
		r.fin(1)
	end 'eat'

	export function one(r Resp)
		r.fin(1)
	end 'one'

	export function mix(x Num, r Resp, y Num, s Resp)
		r.fin(x)
		s.fin(y)
	end 'mix'

	export function rx(r Resp, x Num)
		r.fin(x)
	end 'rx'

	export function two(r Resp, q Resp)
		r.fin(1)
		q.fin(2)
	end 'two'

	export function touch()
		self.n = 1
	end 'touch'

	export function viaSelf(me Sv.handle)
		try me.one(Resp.create(0)) otherwise (sendFailure) 'h1'
			match sendFailure 'x2'
				mailboxFull(b) then b.fin(9)
				serviceStopped(b) then b.fin(9)
			end 'x2'
		end 'h1'
	end 'viaSelf'

	export function useState()
		self.eat(Resp.create(0))
	end 'useState'

	export function useState2()
		let r = self.mk()
		r.fin(1)
	end 'useState2'

	export function touch2()
		self.n = 2
	end 'touch2'
end 'Sv'

function main() returns ExitCode
	let a = spawn(capacity: 1) Sv.create(0)
	let r = Resp.create(0) as isolated Resp
	let q = Resp.create(1) as isolated Resp
	try a.one(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(b) then b.fin(1)
			serviceStopped(b) then b.fin(1)
		end 'x2'
	end 'h1'
	try a.one(q) otherwise (sendFailure) 'h3'
		match sendFailure 'x4'
			mailboxFull(b) then b.fin(2)
			serviceStopped(b) then b.fin(2)
		end 'x4'
	end 'h3'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-s-handback-fresh-nested -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-handback-fresh-nested` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Sv
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mk() returns Resp
		return Resp.create(0)
	end 'mk'

	function eat(r Resp)
		r.fin(1)
	end 'eat'

	export function one(r Resp)
		r.fin(1)
	end 'one'

	export function mix(x Num, r Resp, y Num, s Resp)
		r.fin(x)
		s.fin(y)
	end 'mix'

	export function rx(r Resp, x Num)
		r.fin(x)
	end 'rx'

	export function two(r Resp, q Resp)
		r.fin(1)
		q.fin(2)
	end 'two'

	export function touch()
		self.n = 1
	end 'touch'

	export function viaSelf(me Sv.handle)
		try me.one(Resp.create(0)) otherwise (sendFailure) 'h1'
			match sendFailure 'x2'
				mailboxFull(b) then b.fin(9)
				serviceStopped(b) then b.fin(9)
			end 'x2'
		end 'h1'
	end 'viaSelf'

	export function useState()
		self.eat(Resp.create(0))
	end 'useState'

	export function useState2()
		let r = self.mk()
		r.fin(1)
	end 'useState2'

	export function touch2()
		self.n = 2
	end 'touch2'
end 'Sv'

function mkR() returns Resp
	return Resp.create(0)
end 'mkR'

function main() returns ExitCode
	let a = spawn(capacity: 1) Sv.create(0)
	try a.one(mkR()) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(b) then b.fin(1)
			serviceStopped(b) then b.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-s-handback-in-message-body -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-handback-in-message-body` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Sv
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mk() returns Resp
		return Resp.create(0)
	end 'mk'

	function eat(r Resp)
		r.fin(1)
	end 'eat'

	export function one(r Resp)
		r.fin(1)
	end 'one'

	export function mix(x Num, r Resp, y Num, s Resp)
		r.fin(x)
		s.fin(y)
	end 'mix'

	export function rx(r Resp, x Num)
		r.fin(x)
	end 'rx'

	export function two(r Resp, q Resp)
		r.fin(1)
		q.fin(2)
	end 'two'

	export function touch()
		self.n = 1
	end 'touch'

	export function viaSelf(me Sv.handle)
		try me.one(Resp.create(0)) otherwise (sendFailure) 'h1'
			match sendFailure 'x2'
				mailboxFull(b) then b.fin(9)
				serviceStopped(b) then b.fin(9)
			end 'x2'
		end 'h1'
	end 'viaSelf'

	export function useState()
		self.eat(Resp.create(0))
	end 'useState'

	export function useState2()
		let r = self.mk()
		r.fin(1)
	end 'useState2'

	export function touch2()
		self.n = 2
	end 'touch2'
end 'Sv'

function main() returns ExitCode
	let a = spawn(capacity: 2) Sv.create(0)
	try a.viaSelf(a.clone()) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull then break
			serviceStopped then break
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-s-handback-send-to-stopped -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-handback-send-to-stopped` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Sv
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mk() returns Resp
		return Resp.create(0)
	end 'mk'

	function eat(r Resp)
		r.fin(1)
	end 'eat'

	export function one(r Resp)
		r.fin(1)
	end 'one'

	export function mix(x Num, r Resp, y Num, s Resp)
		r.fin(x)
		s.fin(y)
	end 'mix'

	export function rx(r Resp, x Num)
		r.fin(x)
	end 'rx'

	export function two(r Resp, q Resp)
		r.fin(1)
		q.fin(2)
	end 'two'

	export function touch()
		self.n = 1
	end 'touch'

	export function viaSelf(me Sv.handle)
		try me.one(Resp.create(0)) otherwise (sendFailure) 'h1'
			match sendFailure 'x2'
				mailboxFull(b) then b.fin(9)
				serviceStopped(b) then b.fin(9)
			end 'x2'
		end 'h1'
	end 'viaSelf'

	export function useState()
		self.eat(Resp.create(0))
	end 'useState'

	export function useState2()
		let r = self.mk()
		r.fin(1)
	end 'useState2'

	export function touch2()
		self.n = 2
	end 'touch2'
end 'Sv'

function main() returns ExitCode
	let a = spawn(capacity: 1) Sv.create(0)
	try a.touch() otherwise ignore
	try a.touch() otherwise ignore
	try a.touch() otherwise ignore
	try a.one(Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(b) then b.fin(1)
			serviceStopped(b) then b.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-s-state-method-eat -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-state-method-eat` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Sv
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mk() returns Resp
		return Resp.create(0)
	end 'mk'

	function eat(r Resp)
		r.fin(1)
	end 'eat'

	export function one(r Resp)
		r.fin(1)
	end 'one'

	export function mix(x Num, r Resp, y Num, s Resp)
		r.fin(x)
		s.fin(y)
	end 'mix'

	export function rx(r Resp, x Num)
		r.fin(x)
	end 'rx'

	export function two(r Resp, q Resp)
		r.fin(1)
		q.fin(2)
	end 'two'

	export function touch()
		self.n = 1
	end 'touch'

	export function viaSelf(me Sv.handle)
		try me.one(Resp.create(0)) otherwise (sendFailure) 'h1'
			match sendFailure 'x2'
				mailboxFull(b) then b.fin(9)
				serviceStopped(b) then b.fin(9)
			end 'x2'
		end 'h1'
	end 'viaSelf'

	export function useState()
		self.eat(Resp.create(0))
	end 'useState'

	export function useState2()
		let r = self.mk()
		r.fin(1)
	end 'useState2'

	export function touch2()
		self.n = 2
	end 'touch2'
end 'Sv'

function main() returns ExitCode
	let a = spawn Sv.create(0)
	try a.useState() otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-s-state-method-mk -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-state-method-mk` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Sv
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mk() returns Resp
		return Resp.create(0)
	end 'mk'

	function eat(r Resp)
		r.fin(1)
	end 'eat'

	export function one(r Resp)
		r.fin(1)
	end 'one'

	export function mix(x Num, r Resp, y Num, s Resp)
		r.fin(x)
		s.fin(y)
	end 'mix'

	export function rx(r Resp, x Num)
		r.fin(x)
	end 'rx'

	export function two(r Resp, q Resp)
		r.fin(1)
		q.fin(2)
	end 'two'

	export function touch()
		self.n = 1
	end 'touch'

	export function viaSelf(me Sv.handle)
		try me.one(Resp.create(0)) otherwise (sendFailure) 'h1'
			match sendFailure 'x2'
				mailboxFull(b) then b.fin(9)
				serviceStopped(b) then b.fin(9)
			end 'x2'
		end 'h1'
	end 'viaSelf'

	export function useState()
		self.eat(Resp.create(0))
	end 'useState'

	export function useState2()
		let r = self.mk()
		r.fin(1)
	end 'useState2'

	export function touch2()
		self.n = 2
	end 'touch2'
end 'Sv'

function main() returns ExitCode
	let a = spawn Sv.create(0)
	try a.useState2() otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-s-merge-linear -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-merge-linear` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let q = merge r
	q.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-s-merge-linear-use -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `s-merge-linear-use` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let _ = merge r
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-s-merge-drop -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `s-merge-drop` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let _ = merge r
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:24:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-s-freeze-union -->
The model (`core-m3w-fa13`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `s-freeze-union` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let s = giveS(Resp.create(0)) as isolated Sent
	let _ = freeze s
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:32:17: 'Sent' is unique (its field 'r' holds a unique 'Resp') and cannot be frozen
```

<!-- test: model-hand-ab-s-reassign-after-move-branch-ok -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-reassign-after-move-branch-ok` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var r = Resp.create(0)
	r.fin(1)
	if lg(1) != 0 'i1'
		r = Resp.create(1)
	end 'i1' else 'e2'
		r = Resp.create(2)
	end 'e2'
	r.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-s-consume-in-branch-refill-after -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `s-consume-in-branch-refill-after` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var r = Resp.create(0)
	if lg(1) != 0 'i1'
		r.fin(1)
	end 'i1'
	r = Resp.create(1)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:27:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-s-conditional-move-then-assign -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `s-conditional-move-then-assign` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var r = Resp.create(0)
	if lg(1) != 0 'i1'
		r.fin(1)
	end 'i1'
	r = Resp.create(2)
	r.fin(3)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:27:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-s-while-assign -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-while-assign` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var r = Resp.create(0)
	var i = 2 as Num
	while i != 0 'w1'
		r.fin(1)
		r = Resp.create(2)
		i = lg(0)
	end 'w1'
	r.fin(3)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-s-two-vars-aliased-by-union -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `s-two-vars-aliased-by-union` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let r = Resp.create(0)
	let s = giveS(r)
	r.fin(1)
	useS(s)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:40:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-s-shadow-in-nested-block -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-shadow-in-nested-block` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0)
	if lg(1) != 0 'i1'
		let r = Resp.create(1)
		r.fin(2)
	end 'i1'
	r.fin(3)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-s-shadow-in-nested-block-both-consumed-by-name -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `s-shadow-in-nested-block-both-consumed-by-name` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0)
	if lg(1) != 0 'i1'
		let r = Resp.create(1)
		r.fin(2)
	end 'i1' else 'e2'
		r.fin(4)
	end 'e2'
	r.fin(3)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:33:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-s-shadow-then-outer-moved-inside -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-shadow-then-outer-moved-inside` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0)
	if lg(1) != 0 'i1'
		r.fin(1)
		let r = Resp.create(1)
		r.fin(2)
	end 'i1' else 'e2'
		r.fin(4)
	end 'e2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-s-while-body-shadow -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-while-body-shadow` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0)
	var i = 1 as Num
	while i != 0 'w1'
		let r = Resp.create(1)
		r.fin(2)
		i = 0
	end 'w1'
	r.fin(3)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-s-two-scopes-same-name-sequence -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `s-two-scopes-same-name-sequence` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	if lg(1) != 0 'i1'
		let r = Resp.create(0)
		r.fin(1)
	end 'i1'
	if lg(1) != 0 'i2'
		let _ = Resp.create(0)
	end 'i2'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:32:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-r-temp-union-field -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `r-temp-union-field` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union W
	full(r Resp)
	empty
	function isFull() returns Num
		return 1
	end 'isFull'
end 'W'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let _ = W.full(Resp.create(0)).isFull()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:30:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-r-temp-as-arg-peek -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `r-temp-as-arg-peek` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function pk(r Resp) returns Num
	return r.peek()
end 'pk'

function main() returns ExitCode
	let _ = pk(Resp.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:22:2: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-d-let-underscore-field -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d-let-underscore-field` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0)
	let _ = r.code
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-d-let-underscore-passr -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `d-let-underscore-passR` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function passR(r Resp) returns Resp
	return r
end 'passR'

function main() returns ExitCode
	let r = Resp.create(0)
	let _ = passR(r)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:28:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-d-cond-var-fresh -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-COND`; the compiler refuses it with E3223. Model program `d-cond-var-fresh` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let a = Resp.create(0)
	let c = (a if 1 != 0 else Resp.create(1))
	c.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:25:2: this drops 'a', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-d-two-args-same -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `d-two-args-same` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function twoR(a Resp, b Resp)
	a.fin(1)
	b.fin(2)
end 'twoR'

function main() returns ExitCode
	let r = Resp.create(0)
	twoR(r, b: r)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:10: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-d-assign-passr -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d-assign-passr` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function passR(r Resp) returns Resp
	return r
end 'passR'

function main() returns ExitCode
	var r = Resp.create(0)
	r = passR(r)
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-d-assign-from-other -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `d-assign-from-other` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	var r = Resp.create(0)
	let q = Resp.create(1)
	r = q
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:24:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-d-assign-from-other-moved -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `d-assign-from-other-moved` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	var r = Resp.create(0)
	let q = Resp.create(1)
	r.fin(1)
	r = q
	q.fin(2)
	r.fin(3)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:26:2: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-d-let-alias-then-both -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `d-let-alias-then-both` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0)
	let a = r
	r.fin(1)
	a.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-d-param-alias-callee -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `d-param-alias-callee` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let r = Resp.create(0)
	let u = giveS(r)
	let w = giveS(r)
	useS(u)
	useS(w)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:40:16: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-d-iter-bind-loop-var -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `d-iter-bind-loop-var` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0)
	var i = 2 as Num
	while i != 0 'w1'
		let q = r
		q.fin(1)
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:29:11: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ab-d-consume-in-cond-expr -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `d-consume-in-cond-expr` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function passR(r Resp) returns Resp
	return r
end 'passR'

function main() returns ExitCode
	let r = Resp.create(0)
	let _ = (1 if passR(r).peek() != 0 else 2)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:27:13: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-d-cond-throw-arm-consume -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `d-cond-throw-arm-consume` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function passR(r Resp) returns Resp
	return r
end 'passR'

function main() returns ExitCode
	let r = Resp.create(0)
	let _ = (passR(r).peek() if lg(1) != 0 else 2)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:31:27: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-d-cond-arm-moves-var -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `d-cond-arm-moves-var` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function passR(r Resp) returns Resp
	return r
end 'passR'

function main() returns ExitCode
	let r = Resp.create(0)
	let _ = (passR(r).peek() if lg(0) != 0 else 2)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-d-cond-arm-moves-var-handled -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `d-cond-arm-moves-var-handled` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function passR(r Resp) returns Resp
	return r
end 'passR'

function g(k Num)
	let r = Resp.create(0)
	let _ = (passR(r).peek() if k != 0 else 2)
end 'g'

function main() returns ExitCode
	g(0)
	g(1)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:27:27: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-fz-two-finalizers -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `fz-two-finalizers` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Q
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f1(consuming self)
		destruct self
	end 'f1'

	function f2(consuming self)
		destruct self
	end 'f2'
end 'Q'

function main() returns ExitCode
	let q = Q.create(0)
	q.f1()
	q.f2()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:23:2: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-fz-two-finalizers-branches -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fz-two-finalizers-branches` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Q
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f1(consuming self)
		destruct self
	end 'f1'

	function f2(consuming self)
		destruct self
	end 'f2'
end 'Q'

function g(c Num)
	let q = Q.create(0)
	if c != 0 'i1'
		q.f1()
	end 'i1' else 'e2'
		q.f2()
	end 'e2'
end 'g'

function main() returns ExitCode
	g(0)
	g(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-fz-finalizer-recursive -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fz-finalizer-recursive` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Q
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self, k Num)
		if k != 0 'i1'
			self.f(0)
		end 'i1' else 'e2'
			destruct self
		end 'e2'
	end 'f'
end 'Q'

function main() returns ExitCode
	let q = Q.create(0)
	q.f(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-fz-finalizer-recursive-both -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `fz-finalizer-recursive-both` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Q
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self, k Num)
		if k != 0 'i1'
			self.f(0)
		end 'i1'
		destruct self
	end 'f'
end 'Q'

function main() returns ExitCode
	let q = Q.create(0)
	q.f(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:12: use of moved value '__self': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-fz-finalizer-fields-linear -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `fz-finalizer-fields-linear` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Q
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self, r Resp)
		let _ = r.code
		destruct self
	end 'f'
end 'Q'

function main() returns ExitCode
	let q = Q.create(0)
	q.f(Resp.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:28:11: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-fz-destruct-self-not-last -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fz-destruct-self-not-last` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Q
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self, r Resp)
		destruct self
		r.fin(1)
	end 'f'
end 'Q'

function main() returns ExitCode
	let q = Q.create(0)
	q.f(Resp.create(0))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-fz-consuming-in-record-other -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fz-consuming-in-record-other` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type B
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self)
		destruct self
	end 'f'
end 'B'

type A
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self, b B)
		b.f()
		destruct self
	end 'f'
end 'A'

function main() returns ExitCode
	let a = A.create(0)
	let b = B.create(1)
	a.f(b)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-fz-linear-record-with-int-fields-clone -->
The model (`core-m3w-fa13`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `fz-linear-record-with-int-fields-clone` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0)
	let c = r.clone()
	r.fin(1)
	c.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:23:10: 'Resp' is unique and cannot be cloned
```

<!-- test: model-hand-ac-fin-return-before -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `fin-return-before` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self, k Num)
		if k != 0 'i1'
			return
		end 'i1'
		destruct self
	end 'f'
end 'Fz'

function main() returns ExitCode
	let t = Fz.create(0)
	t.f(1)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:13:4: this drops 'self', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-fin-self-into-union -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fin-self-into-union` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Hs
	none
	h(q Fz)
end 'Hs'

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self)
		let s = Hs.h(self)
		match s 'x1'
			none then break
			h(q) then q.g()
		end 'x1'
	end 'f'

	function g(consuming self)
		destruct self
	end 'g'
end 'Fz'

function main() returns ExitCode
	let t = Fz.create(0)
	t.f()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-fin-self-into-union-dropped -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `fin-self-into-union-dropped` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Hs
	none
	h(q Fz)
end 'Hs'

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self)
		let _ = Hs.h(self)
	end 'f'

	function g(consuming self)
		destruct self
	end 'g'
end 'Fz'

function main() returns ExitCode
	let t = Fz.create(0)
	t.f()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:16:11: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-fin-self-to-fn -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fin-self-to-fn` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self)
		useFz(self)
	end 'f'

	function g(consuming self)
		destruct self
	end 'g'
end 'Fz'

function useFz(t Fz)
	t.g()
end 'useFz'

function main() returns ExitCode
	let t = Fz.create(0)
	t.f()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-fin-self-to-fn-nonconsuming -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `fin-self-to-fn-nonconsuming` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self)
		useFz(self)
	end 'f'

	function g(consuming self)
		destruct self
	end 'g'
end 'Fz'

function useFz(t Fz)
	let _ = t.n
end 'useFz'

function main() returns ExitCode
	let t = Fz.create(0)
	t.f()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:20:10: this drops 't', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-fin-self-send-drop-handler -->
The model (`core-m3w-fa13`) refuses it by `C-RECOVER`; the compiler refuses it with E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `fin-self-send-drop-handler` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self, a Hq.handle)
		try a.take(self) otherwise (sendFailure) 'h1'
			match sendFailure 'x2'
				mailboxFull then break
				serviceStopped then break
			end 'x2'
		end 'h1'
	end 'f'

	function g(consuming self)
		destruct self
	end 'g'
end 'Fz'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Fz)
		t.g()
	end 'take'
end 'Hq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	let t = Fz.create(0)
	t.f(a)
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:14:5: the case 'mailboxFull' carries the linear value 't', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ac-fin-recv-other-instance -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fin-recv-other-instance` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self, o Fz)
		o.g()
		destruct self
	end 'f'

	function g(consuming self)
		destruct self
	end 'g'
end 'Fz'

function main() returns ExitCode
	let t = Fz.create(0)
	t.f(Fz.create(1))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-fin-method-returns-self-moved -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fin-method-returns-self-moved` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self) returns Fz
		return self
	end 'f'

	function g(consuming self)
		destruct self
	end 'g'
end 'Fz'

function main() returns ExitCode
	let t = Fz.create(0)
	let u = t.f()
	u.g()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-fin-method-returns-self-dropped -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `fin-method-returns-self-dropped` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self) returns Fz
		return self
	end 'f'

	function g(consuming self)
		destruct self
	end 'g'
end 'Fz'

function main() returns ExitCode
	let t = Fz.create(0)
	let _ = t.f()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:23:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-fin-method-returns-self-chain -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fin-method-returns-self-chain` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self) returns Fz
		return self
	end 'f'

	function g(consuming self)
		destruct self
	end 'g'
end 'Fz'

function main() returns ExitCode
	Fz.create(0).f().f().g()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-fin-nonconsuming-with-self-peek -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fin-nonconsuming-with-self-peek` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function pk() returns Num
		return self.n
	end 'pk'

	function g(consuming self)
		destruct self
	end 'g'
end 'Fz'

function main() returns ExitCode
	let t = Fz.create(0)
	let _ = t.pk()
	let _ = t.pk()
	t.g()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-hb-two-linear-two-names -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `hb-two-linear-two-names` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type H2
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function two(a Resp, b Resp)
		a.fin(1)
		b.fin(1)
	end 'two'
end 'H2'

function main() returns ExitCode
	let s = spawn(capacity: 1) H2.create(0)
	try s.two(Resp.create(0), b: Resp.create(1)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q, _) then q.fin(1)
			serviceStopped(q, _) then q.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:38:4: the case 'mailboxFull' carries the linear value 'b', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ac-hb-nonlinear-first-one-name -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hb-nonlinear-first-one-name` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type H2
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function m(nV Num, a Resp)
		let _ = nV
		a.fin(1)
	end 'm'
end 'H2'

function main() returns ExitCode
	let s = spawn(capacity: 1) H2.create(0)
	try s.m(1, a: Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(_, q) then q.fin(1)
			serviceStopped(_, q) then q.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-hb-refill-var-in-handler -->
The model (`core-m3w-fa13`) refuses it by `C-STORE`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `hb-refill-var-in-handler` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	var r = Resp.create(0)
	try a.take(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then r = q
			serviceStopped(q) then r = q
		end 'x2'
	end 'h1'
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:42:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-hb-refill-var-in-handler-no-use -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa13`) refuses it by `C-STORE`; the compiler refuses it with E3223. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `hb-refill-var-in-handler-no-use` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	var r = Resp.create(0)
	try a.take(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then r = q
			serviceStopped(q) then r = q
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:36:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-hb-loop-refill -->
The model (`core-m3w-fa13`) refuses it by `C-STORE`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `hb-loop-refill` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	var r = Resp.create(0)
	var i = 2 as Num
	while i != 0 'w1'
		try a.take(r) otherwise (sendFailure) 'h2'
			match sendFailure 'x3'
				mailboxFull(q) then r = q
				serviceStopped(q) then r = q
			end 'x3'
		end 'h2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:42:14: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ac-hb-send-in-loop-fresh -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hb-send-in-loop-fresh` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	var i = 3 as Num
	while i != 0 'w1'
		try a.take(Resp.create(0)) otherwise (sendFailure) 'h2'
			match sendFailure 'x3'
				mailboxFull(q) then q.fin(1)
				serviceStopped(q) then q.fin(1)
			end 'x3'
		end 'h2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-hb-handler-breaks -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `hb-handler-breaks` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	var i = 3 as Num
	while i != 0 'w1'
		try a.take(Resp.create(0)) otherwise (sendFailure) 'h2'
			match sendFailure 'x3'
				mailboxFull then break
				serviceStopped then break
			end 'x3'
		end 'h2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:43:5: the case 'mailboxFull' carries the linear value 't', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ac-hb-handler-returns -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `hb-handler-returns` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function f(a Hq.handle)
	try a.take(Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull then return
			serviceStopped then return
		end 'x2'
	end 'h1'
end 'f'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	f(a)
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:36:4: the case 'mailboxFull' carries the linear value 't', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ac-hb-union-arg -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hb-union-arg` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeS(s Sent)
		match s 'x1'
			ok then break
			notSent(r) then r.fin(1)
		end 'x1'
	end 'takeS'
end 'Hq'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	try a.takeS(giveS(Resp.create(0))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then useS(q)
			serviceStopped(q) then useS(q)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-hb-union-arg-handler-drops -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `hb-union-arg-handler-drops` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeS(s Sent)
		match s 'x1'
			ok then break
			notSent(r) then r.fin(1)
		end 'x1'
	end 'takeS'
end 'Hq'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	try a.takeS(giveS(Resp.create(0))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull then break
			serviceStopped then break
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:49:4: the case 'mailboxFull' carries the linear value 's', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ac-hb-send-twice-same-var -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `hb-send-twice-same-var` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	let r = Resp.create(0)
	try a.take(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	try a.take(r) otherwise (sendFailure) 'h3'
		match sendFailure 'x4'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x4'
	end 'h3'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:42:13: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-hb-send-var-then-peek -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `hb-send-var-then-peek` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	let r = Resp.create(0)
	try a.take(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	let _ = r.peek()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:42:10: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-hb-send-in-behaviour -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hb-send-in-behaviour` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

type Kq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function go(a Hq.handle)
		try a.take(Resp.create(0)) otherwise (sendFailure) 'h1'
			match sendFailure 'x2'
				mailboxFull(q) then q.fin(1)
				serviceStopped(q) then q.fin(1)
			end 'x2'
		end 'h1'
	end 'go'
end 'Kq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	let k = spawn Kq.create(0)
	try k.go(a.clone()) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-hb-send-param-in-behaviour -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hb-send-param-in-behaviour` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

type Kq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function go(a Hq.handle, r Resp)
		try a.take(r) otherwise (sendFailure) 'h1'
			match sendFailure 'x2'
				mailboxFull(q) then q.fin(1)
				serviceStopped(q) then q.fin(1)
			end 'x2'
		end 'h1'
	end 'go'
end 'Kq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	let k = spawn Kq.create(0)
	try k.go(a.clone(), r: Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(_, q) then q.fin(1)
			serviceStopped(_, q) then q.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-hb-forward-param-no-handler-consume -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `hb-forward-param-no-handler-consume` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

type Kq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function go(a Hq.handle, r Resp)
		try a.take(r) otherwise (sendFailure) 'h1'
			match sendFailure 'x2'
				mailboxFull then break
				serviceStopped then break
			end 'x2'
		end 'h1'
	end 'go'
end 'Kq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	let k = spawn Kq.create(0)
	try k.go(a.clone(), r: Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(_, q) then q.fin(1)
			serviceStopped(_, q) then q.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:43:5: the case 'mailboxFull' carries the linear value 't', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ac-ub-while-cond-temp -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `ub-while-cond-temp` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function mkR() returns Resp
	return Resp.create(0)
end 'mkR'

function main() returns ExitCode
	var i = 2 as Num
	while mkR().peek() != 0 'w1'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:31:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-ub-union-nonlinear-from-temp -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `ub-union-nonlinear-from-temp` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Wi
	w(n Num)
	e
end 'Wi'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function mkR() returns Resp
	return Resp.create(0)
end 'mkR'

function main() returns ExitCode
	let _ = Wi.w(mkR().peek())
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:31:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-ub-cond-cond-arms -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `ub-cond-cond-arms` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function mkR() returns Resp
	return Resp.create(0)
end 'mkR'

function main() returns ExitCode
	let _ = (mkR() if 1 != 0 else mkR()).peek()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:26:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-ub-send-operand-temp -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `ub-send-operand-temp` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Pk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function m(x Num)
		self.n = x
	end 'm'
end 'Pk'

function mkR() returns Resp
	return Resp.create(0)
end 'mkR'

function main() returns ExitCode
	let p = spawn Pk.create(0)
	try p.m(mkR().peek()) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:39:8: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-ub-await-operand-temp -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `ub-await-operand-temp` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Pk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function m(x Num) returns Num
		return x
	end 'm'
end 'Pk'

function mkR() returns Resp
	return Resp.create(0)
end 'mkR'

function main() returns ExitCode
	let p = spawn Pk.create(0)
	let _ = try await p.m(mkR().peek()) otherwise panic("send failed")
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:39:22: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-ub-spawn-operand-temp -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `ub-spawn-operand-temp` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Pk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pk'

function mkR() returns Resp
	return Resp.create(0)
end 'mkR'

function main() returns ExitCode
	let _ = spawn Pk.create(mkR().peek())
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:34:19: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-ub-new-operand-temp -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `ub-new-operand-temp` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function mkR() returns Resp
	return Resp.create(0)
end 'mkR'

function main() returns ExitCode
	let _ = Box.create(mkR().peek())
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:34:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-ub-for-iterable-temp -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `ub-for-iterable-temp` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function mkR() returns Resp
	return Resp.create(0)
end 'mkR'

function main() returns ExitCode
	let a = NumArray.create(0)
	for _ in a 'f1'
		let _ = mkR().peek()
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:29:3: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-ub-return-temp-receiver -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `ub-return-temp-receiver` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function f() returns Num
	return Resp.create(0).code
end 'f'

function main() returns ExitCode
	let _ = f()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:22:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-ub-return-union-temp-ok -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ub-return-union-temp-ok` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function f() returns Sent
	return giveS(Resp.create(0))
end 'f'

function main() returns ExitCode
	useS(f())
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-ub-array-of-linear -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3224. Model program `ub-array-of-linear` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias RespArray = Array with Resp

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let a = RespArray.create(0)
	a.push(Resp.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:3:11: 'Resp' is a linear value and cannot be a type argument of 'Array': a linear value lives only in locals, parameters and message arguments, so no container, promise, generic record or generic function holds one
```

<!-- test: model-hand-ac-ub-array-of-linear-union -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3224. Model program `ub-array-of-linear-union` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias SentArray = Array with Sent

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let a = SentArray.create(0)
	a.push(Sent.ok)
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:3:11: 'Sent' is a linear value and cannot be a type argument of 'Array': a linear value lives only in locals, parameters and message arguments, so no container, promise, generic record or generic function holds one
```

<!-- test: model-hand-ac-wc-underscore-binding -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `wc-underscore-binding` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let s = giveS(Resp.create(0))
	match s 'x1'
		ok then break
		notSent then break
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:34:3: the case 'notSent' carries the linear value 'r', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ac-wc-underscore-handler -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `wc-underscore-handler` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq5
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq5'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq5.create(0)
	try a.take(Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull then break
			serviceStopped then break
		end 'x2'
	end 'h1'
	try a.take(Resp.create(1)) otherwise (sendFailure) 'h3'
		match sendFailure 'x4'
			mailboxFull then break
			serviceStopped then break
		end 'x4'
	end 'h3'
	try a.take(Resp.create(2)) otherwise (sendFailure) 'h5'
		match sendFailure 'x6'
			mailboxFull then break
			serviceStopped then break
		end 'x6'
	end 'h5'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:37:4: the case 'mailboxFull' carries the linear value 't', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ac-wc-underscore-let -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `wc-underscore-let` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let _ = Resp.create(0)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:23:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-wc-underscore-let-call -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `wc-underscore-let-call` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function mkR() returns Resp
	return Resp.create(0)
end 'mkR'

function main() returns ExitCode
	let _ = mkR()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:27:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-wc-underscore-let-iso-var -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `wc-underscore-let-iso-var` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let _ = r
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:24:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-wc-underscore-let-union -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `wc-underscore-let-union` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let _ = giveS(Resp.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:32:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-wc-non-exhaustive-no-linear-case -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `wc-non-exhaustive-no-linear-case` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let s = giveS(Resp.create(0))
	match s 'x1'
		notSent(r) then r.fin(1)
		ok then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-wc-non-exhaustive-ok-value -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `wc-non-exhaustive-ok-value` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let s = Sent.ok
	match s 'x1'
		notSent(r) then r.fin(1)
		ok then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-wc-match-in-loop -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `wc-match-in-loop` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	var i = 3 as Num
	while i != 0 'w1'
		match giveS(Resp.create(0)) 'x2'
			ok then break
			notSent(r) then r.fin(1)
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-wc-match-arm-assign-scrutinee-var -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `wc-match-arm-assign-scrutinee-var` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	var s = giveS(Resp.create(0))
	match s 'x1'
		ok then break
		notSent(r) then s = giveS(r)
	end 'x1'
	match s 'x2'
		ok then break
		notSent(r) then r.fin(1)
	end 'x2'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:36:8: use of moved value 's': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-wc-match-var-arm-refill-path -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `wc-match-var-arm-refill-path` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	var s = giveS(Resp.create(0))
	match s 'x1'
		ok then s = Sent.ok
		notSent(r) then r.fin(1)
	end 'x1'
	useS(s)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:43:7: use of moved value 's': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-wc-match-two-vars-same-arm-names -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `wc-match-two-vars-same-arm-names` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let r = Resp.create(5)
	let s = giveS(Resp.create(0))
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
	r.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-wc-match-on-param-union-linear-returned -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `wc-match-on-param-union-linear-returned` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function unwrap(s Sent) returns Resp
	match s 'x1'
		ok then return Resp.create(0)
		notSent(r) then return r
	end 'x1'
end 'unwrap'

function main() returns ExitCode
	let r = unwrap(giveS(Resp.create(1)))
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-wc-match-on-param-union-linear-returned-dropped -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `wc-match-on-param-union-linear-returned-dropped` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function unwrap(s Sent) returns Resp
	match s 'x1'
		ok then return Resp.create(0)
		notSent(r) then return r
	end 'x1'
end 'unwrap'

function main() returns ExitCode
	let _ = unwrap(giveS(Resp.create(1)))
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:39:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-wc-return-in-arm-leaves-other-payload -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `wc-return-in-arm-leaves-other-payload` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function f(s Sent, t Sent)
	match s 'x1'
		ok then return
		notSent(r) then r.fin(1)
	end 'x1'
	useS(t)
end 'f'

function main() returns ExitCode
	f(Sent.ok, t: Sent.ok)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:35:11: this drops 't', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-wc-send-linear-without-handback -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-SEND`; the compiler refuses it with E3223. Model program `wc-send-linear-without-handback` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq5
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq5'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq5.create(0)
	try a.take(Resp.create(0)) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:35:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-wc-spawn-linear-arg -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `wc-spawn-linear-arg` of redteamAC.hand_ac11, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq6
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hq6'

function mkR() returns Resp
	return Resp.create(0)
end 'mkR'

function main() returns ExitCode
	let _ = spawn Hq6.create(mkR().peek())
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:34:20: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-sw-merge-linear -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `sw-merge-linear` of redteamAC.hand_ac15, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let _ = Box.create(0)
	let _ = merge r
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:33:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-ua-spawn-and-exit-with-pending-handback -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ua-spawn-and-exit-with-pending-handback` of redteamAC.hand_ac18, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq9
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq9'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq9.create(0)
	try a.take(Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-li-use-then-fin -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `li-use-then-fin` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	useR(r)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-li-use-twice -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `li-use-twice` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	useR(r)
	useR(r)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:7: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-li-useri-then-fin -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `li-useRI-then-fin` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useRI2(r isolated Resp)
	r.fin(1)
end 'useRI2'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	useRI2(r)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-li-useri-twice -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `li-useRI-twice` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useRI2(r isolated Resp)
	r.fin(1)
end 'useRI2'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	useRI2(r)
	useRI2(r)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:9: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-li-use-no-more -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `li-use-no-more` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	useR(r)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-li-pass-then-fin -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `li-pass-then-fin` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function passRI(r isolated Resp) returns isolated Resp
	return r
end 'passRI'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let q = passRI(r)
	r.fin(1)
	q.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-li-pass-bound-twice -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `li-pass-bound-twice` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function passRI(r isolated Resp) returns isolated Resp
	return r
end 'passRI'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let q = passRI(r)
	let w = passRI(r)
	q.fin(1)
	w.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:17: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-li-peek-fin-then-fin -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `li-peek-fin-then-fin` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function peekRI(r isolated Resp) returns Num
	let z = r.peek()
	r.fin(1)
	return z
end 'peekRI'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let z = peekRI(r)
	r.fin(z)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-li-in-loop -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `li-in-loop` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	var i = 2 as Num
	while i != 0 'w1'
		useR(r)
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:33:8: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ac-li-in-if -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `li-in-if` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	if 1 != 0 'i1'
		useR(r)
	end 'i1'
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-li-method-consume -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `li-method-consume` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useBoth(r isolated Resp, q isolated Resp)
	r.fin(1)
	q.fin(2)
end 'useBoth'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	useBoth(r, q: r)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:13: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-li-param-forward -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `li-param-forward` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function fw(r isolated Resp)
	useR(r)
	r.fin(1)
end 'fw'

function main() returns ExitCode
	fw(Resp.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-li-param-forward-once -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `li-param-forward-once` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function fw(r isolated Resp)
	useR(r)
end 'fw'

function main() returns ExitCode
	fw(Resp.create(0))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-li-union-lend -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `li-union-lend` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useSI(s isolated Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useSI'

function main() returns ExitCode
	let s = giveS(Resp.create(0)) as isolated Sent
	useSI(s)
	useSI(s)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:40:8: use of moved value 's': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-hb-iso-var -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hb-iso-var` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	let r = Resp.create(0) as isolated Resp
	try a.take(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-hb-iso-var-twice -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `hb-iso-var-twice` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	let r = Resp.create(0) as isolated Resp
	try a.take(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	try a.take(r) otherwise (sendFailure) 'h3'
		match sendFailure 'x4'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x4'
	end 'h3'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:42:13: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-hb-iso-var-use-after -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `hb-iso-var-use-after` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	let r = Resp.create(0) as isolated Resp
	try a.take(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	let _ = r.peek()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:42:10: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-hb-iso-refill -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `hb-iso-refill` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	var r = Resp.create(0) as isolated Resp
	try a.take(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then r = q
			serviceStopped(q) then r = q
		end 'x2'
	end 'h1'
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:38:28: 'q' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Resp`, or store a copy
```

<!-- test: model-hand-ac-hb-iso-refill-no-use -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `hb-iso-refill-no-use` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	var r = Resp.create(0) as isolated Resp
	try a.take(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then r = q
			serviceStopped(q) then r = q
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:38:28: 'q' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Resp`, or store a copy
```

<!-- test: model-hand-ac-hb-iso-loop-refill -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `hb-iso-loop-refill` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	var r = Resp.create(0) as isolated Resp
	var i = 2 as Num
	while i != 0 'w1'
		try a.take(r) otherwise (sendFailure) 'h2'
			match sendFailure 'x3'
				mailboxFull(q) then r = q
				serviceStopped(q) then r = q
			end 'x3'
		end 'h2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:44:29: 'q' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Resp`, or store a copy
```

<!-- test: model-hand-ac-hb-iso-loop-refill-final -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `hb-iso-loop-refill-final` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	var r = Resp.create(0) as isolated Resp
	var i = 2 as Num
	while i != 0 'w1'
		try a.take(r) otherwise (sendFailure) 'h2'
			match sendFailure 'x3'
				mailboxFull(q) then r = q
				serviceStopped(q) then r = q
			end 'x3'
		end 'h2'
		i = lg(0)
	end 'w1'
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:44:29: 'q' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Resp`, or store a copy
```

<!-- test: model-hand-ac-hb-iso-handler-consume-then-sender-consume -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `hb-iso-handler-consume-then-sender-consume` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	let r = Resp.create(0) as isolated Resp
	try a.take(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:42:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-hb-iso-handler-names-shadow -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hb-iso-handler-names-shadow` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	let r = Resp.create(0) as isolated Resp
	try a.take(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(r) then r.fin(1)
			serviceStopped(r) then r.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-hb-iso-handler-shadow-then-use -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `hb-iso-handler-shadow-then-use` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	let r = Resp.create(0) as isolated Resp
	try a.take(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(r) then r.fin(1)
			serviceStopped(r) then r.fin(1)
		end 'x2'
	end 'h1'
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:42:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-min-h1-loop -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `min-h1-loop` of redteamAC.hand_ac20, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	var i = 1 as Num
	while i != 0 'w1'
		useR(r)
		i = 0
	end 'w1'
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:29:8: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ac-min-h1-call-result -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `min-h1-call-result` of redteamAC.hand_ac20, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function mkI() returns isolated Resp
	return Resp.create(0)
end 'mkI'

function main() returns ExitCode
	let r = mkI()
	useR(r)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-min-h1-behaviour -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `min-h1-behaviour` of redteamAC.hand_ac20, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Kq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function go(r Resp)
		useR(r)
		r.fin(1)
	end 'go'
end 'Kq'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let k = spawn Kq.create(0)
	try k.go(Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:3: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-min-h1-finalizer -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `min-h1-finalizer` of redteamAC.hand_ac20, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self, r isolated Resp)
		useR(r)
		r.fin(1)
		destruct self
	end 'f'
end 'Fz'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let t = Fz.create(0)
	t.f(Resp.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:3: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-min-h1-recursion -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `min-h1-recursion` of redteamAC.hand_ac20, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function f1(r Resp, c Num)
	if c != 0 'i1'
		g1(r, c: 0)
	end 'i1' else 'e2'
		r.fin(1)
	end 'e2'
end 'f1'

function g1(r Resp, c Num)
	f1(r, c: c)
end 'g1'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	f1(r, c: 1)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:36:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-min-h1-control-plain -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `min-h1-control-plain` of redteamAC.hand_ac20, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0)
	useR(r)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-min-h1-control-iso-param -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `min-h1-control-iso-param` of redteamAC.hand_ac20, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useRI(r isolated Resp)
	r.fin(1)
end 'useRI'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	useRI(r)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ld-call-result-iso -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ld-call-result-iso` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function mkI2() returns isolated Resp
	return Resp.create(0)
end 'mkI2'

function main() returns ExitCode
	let r = mkI2()
	useR(r)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ld-method-arg -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `ld-method-arg` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Eat
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(r Resp)
		r.fin(1)
	end 'eat'

	function eat2(r Resp, x Num)
		r.fin(x)
	end 'eat2'

	function wrap(r Resp) returns Sent
		return giveS(r)
	end 'wrap'
end 'Eat'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let e = Eat.create(0)
	let r = Resp.create(0) as isolated Resp
	e.eat(r)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:53:2: 'e' is mutable outside 'r'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ac-ld-method-arg2 -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `ld-method-arg2` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Eat
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(r Resp)
		r.fin(1)
	end 'eat'

	function eat2(r Resp, x Num)
		r.fin(x)
	end 'eat2'

	function wrap(r Resp) returns Sent
		return giveS(r)
	end 'wrap'
end 'Eat'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let e = Eat.create(0)
	let r = Resp.create(0) as isolated Resp
	e.eat2(r, x: 1)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:53:2: 'e' is mutable outside 'r'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ac-ld-method-wrap-union -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `ld-method-wrap-union` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Eat
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(r Resp)
		r.fin(1)
	end 'eat'

	function eat2(r Resp, x Num)
		r.fin(x)
	end 'eat2'

	function wrap(r Resp) returns Sent
		return giveS(r)
	end 'wrap'
end 'Eat'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let e = Eat.create(0)
	let r = Resp.create(0) as isolated Resp
	let s = e.wrap(r)
	useS(s)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:60:10: 'e' is mutable outside 'r'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ac-ld-wrap-fn-union -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `ld-wrap-fn-union` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function wrapI(r Resp) returns Sent
	return Sent.notSent(r)
end 'wrapI'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let s = wrapI(r)
	useS(s)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:39:10: 'wrapI' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: model-hand-ac-ld-wrap-fn-union-twice -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `ld-wrap-fn-union-twice` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function wrapI(r Resp) returns Sent
	return Sent.notSent(r)
end 'wrapI'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let s = wrapI(r)
	let t = wrapI(r)
	useS(s)
	useS(t)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:39:10: 'wrapI' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: model-hand-ac-ld-union-var -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ld-union-var` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useS2(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS2'

function main() returns ExitCode
	let s = giveS(Resp.create(0)) as isolated Sent
	useS2(s)
	useS2(s)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:40:8: use of moved value 's': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ld-union-var-match-after -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ld-union-var-match-after` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useS2(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS2'

function main() returns ExitCode
	let s = giveS(Resp.create(0)) as isolated Sent
	useS2(s)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:40:8: use of moved value 's': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ld-in-if-both-arms -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ld-in-if-both-arms` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	if 1 != 0 'i1'
		useR(r)
	end 'i1' else 'e2'
		useR(r)
	end 'e2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-ld-in-if-lent-else-fin -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ld-in-if-lent-else-fin` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	if 1 != 0 'i1'
		useR(r)
	end 'i1' else 'e2'
		r.fin(1)
	end 'e2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-ld-in-if-lent-else-fin-after -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ld-in-if-lent-else-fin-after` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	if 1 != 0 'i1'
		useR(r)
	end 'i1' else 'e2'
		r.fin(1)
	end 'e2'
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ld-param-forward-twice -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ld-param-forward-twice` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function fw(r isolated Resp)
	useR(r)
	useR(r)
end 'fw'

function main() returns ExitCode
	fw(Resp.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:7: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ld-behaviour-param-twice -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ld-behaviour-param-twice` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Kq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function go(r Resp)
		useR(r)
		useR(r)
	end 'go'
end 'Kq'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let k = spawn Kq.create(0)
	try k.go(Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:8: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ld-for-array-lend -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ld-for-array-lend` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BoxArray = Array with Box

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let a = BoxArray.create(0)
	a.push(Box.create(1))
	let r = Resp.create(0) as isolated Resp
	for _ in a 'f1'
		useR(r)
	end 'f1'
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:39:8: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ac-sm-behaviour-self-method -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `sm-behaviour-self-method` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Kb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eatM(r Resp)
		r.fin(1)
	end 'eatM'

	export function go(r Resp)
		self.eatM(r)
		r.fin(2)
	end 'go'

	export function go1(r Resp)
		self.eatM(r)
	end 'go1'

	export function go2(r Resp)
		useR(r)
		let _ = r.peek()
	end 'go2'
end 'Kb'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let k = spawn Kb.create(0)
	try k.go(Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:34:3: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
error E3102: <fragment>:43:11: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-sm-behaviour-self-method-once -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `sm-behaviour-self-method-once` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Kb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eatM(r Resp)
		r.fin(1)
	end 'eatM'

	export function go(r Resp)
		self.eatM(r)
		r.fin(2)
	end 'go'

	export function go1(r Resp)
		self.eatM(r)
	end 'go1'

	export function go2(r Resp)
		useR(r)
		let _ = r.peek()
	end 'go2'
end 'Kb'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let k = spawn Kb.create(0)
	try k.go1(Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:34:3: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
error E3102: <fragment>:43:11: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-sm-behaviour-use-then-peek -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `sm-behaviour-use-then-peek` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Kb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eatM(r Resp)
		r.fin(1)
	end 'eatM'

	export function go(r Resp)
		self.eatM(r)
		r.fin(2)
	end 'go'

	export function go1(r Resp)
		self.eatM(r)
	end 'go1'

	export function go2(r Resp)
		useR(r)
		let _ = r.peek()
	end 'go2'
end 'Kb'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let k = spawn Kb.create(0)
	try k.go2(Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:34:3: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
error E3102: <fragment>:43:11: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-sm-main-var-iso-fn-result -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `sm-main-var-iso-fn-result` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function mkI3() returns isolated Resp
	return Resp.create(0)
end 'mkI3'

function main() returns ExitCode
	let r = mkI3()
	useR(r)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-cond-two-fresh -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `cond-two-fresh` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function mkR() returns Resp
	return Resp.create(0)
end 'mkR'

function main() returns ExitCode
	let r = (mkR() if 1 != 0 else mkR())
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-cond-fresh-and-var -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-COND`; the compiler refuses it with E3223. Model program `cond-fresh-and-var` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function mkR() returns Resp
	return Resp.create(0)
end 'mkR'

function main() returns ExitCode
	let a = Resp.create(0)
	let r = (a if 1 != 0 else mkR())
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:29:2: this drops 'a', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-cond-union-fresh -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `cond-union-fresh` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let s = (giveS(Resp.create(0)) if 1 != 0 else Sent.ok)
	useS(s)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-cond-in-arg -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `cond-in-arg` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function mkR() returns Resp
	return Resp.create(0)
end 'mkR'

function main() returns ExitCode
	useR((mkR() if 1 != 0 else mkR()))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-sh-inner-block-shadow -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `sh-inner-block-shadow` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0)
	if 1 != 0 'i1'
		let r = Resp.create(1)
		r.fin(1)
	end 'i1'
	r.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-sh-same-scope-rebind -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `sh-same-scope-rebind` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0)
	let r = Resp.create(1)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:25:2: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-sh-match-payload-shadow -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `sh-match-payload-shadow` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let r = Resp.create(0)
	match giveS(Resp.create(1)) 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
	r.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-sh-match-payload-shadow-outer-moved-inside -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `sh-match-payload-shadow-outer-moved-inside` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let r = Resp.create(0)
	match giveS(Resp.create(1)) 'x1'
		ok then r.fin(5)
		notSent(r) then r.fin(1)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:32:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-sh-handler-name-shadow -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `sh-handler-name-shadow` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq2
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq2'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq2.create(0)
	let r = Resp.create(0)
	try a.take(Resp.create(1)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(r) then r.fin(1)
			serviceStopped(r) then r.fin(1)
		end 'x2'
	end 'h1'
	r.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-sh-handler-name-shadow-live-outer-dropped -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `sh-handler-name-shadow-live-outer-dropped` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq2
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq2'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq2.create(0)
	let r = Resp.create(0)
	try a.take(Resp.create(1)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(r) then r.fin(1)
			serviceStopped(r) then r.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:42:2: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-sh-loop-body-continue -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `sh-loop-body-continue` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var i = 2 as Num
	while i != 0 'w1'
		let r = Resp.create(0)
		i = lg(0)
		if i != 0 'i2'
			continue
		end 'i2'
		r.fin(1)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:31:4: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-sh-loop-body-fresh-each-iter -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `sh-loop-body-fresh-each-iter` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var i = 3 as Num
	while i != 0 'w1'
		let r = Resp.create(0)
		r.fin(1)
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-sh-param-shadow-let -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `sh-param-shadow-let` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function f(r Resp)
	let r = Resp.create(1)
	r.fin(1)
end 'f'

function main() returns ExitCode
	f(Resp.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:21:10: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-sh-param-shadow-inner -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `sh-param-shadow-inner` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function f(r Resp)
	if 1 != 0 'i1'
		let r = Resp.create(1)
		r.fin(1)
	end 'i1'
	r.fin(2)
end 'f'

function main() returns ExitCode
	f(Resp.create(0))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-sh-var-assign-inner-shadow -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `sh-var-assign-inner-shadow` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	var r = Resp.create(0)
	if 1 != 0 'i1'
		var r = Resp.create(1)
		r.fin(1)
		r = Resp.create(2)
		r.fin(2)
	end 'i1'
	r.fin(3)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-un-two-linear-payloads-one-consumed -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `un-two-linear-payloads-one-consumed` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Pair
	none
	both(a Resp, b Resp)
end 'Pair'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function usePair(p Pair)
	match p 'x1'
		none then break
		both(x, _) then x.fin(1)
	end 'x1'
end 'usePair'

function main() returns ExitCode
	usePair(Pair.both(Resp.create(0), b: Resp.create(1)))
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:29:3: the case 'both' carries the linear value 'b', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ac-un-mixed-payloads -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `un-mixed-payloads` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Mx
	none
	m(n Num, r Resp)
end 'Mx'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useMx(p Mx)
	match p 'x1'
		none then break
		m(k, x) then x.fin(k)
	end 'x1'
end 'useMx'

function main() returns ExitCode
	useMx(Mx.m(1, r: Resp.create(0)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-un-nested-wrap -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `un-nested-wrap` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

union Outer
	o(s Sent)
	n
end 'Outer'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useS2(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS2'

function useO(p Outer)
	match p 'x1'
		n then break
		o(s) then useS2(s)
	end 'x1'
end 'useO'

function main() returns ExitCode
	useO(Outer.o(giveS(Resp.create(0))))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-un-nested-wrap-arm-ignores -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `un-nested-wrap-arm-ignores` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

union Outer
	o(s Sent)
	n
end 'Outer'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useO(p Outer)
	match p 'x1'
		n then break
		o then break
	end 'x1'
end 'useO'

function main() returns ExitCode
	useO(Outer.o(giveS(Resp.create(0))))
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:38:3: the case 'o' carries the linear value 's', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ac-un-match-twice-same-var -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `un-match-twice-same-var` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let s = giveS(Resp.create(0))
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
	match s 'x2'
		ok then break
		notSent(r) then r.fin(1)
	end 'x2'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:36:8: use of moved value 's': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ul-union-literal-iso -->
The model (`core-m3w-fa13`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `ul-union-literal-iso` of redteamAC.hand_ac7, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let s = Sent.notSent(r)
	r.fin(1)
	useS(s)
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:35:23: cannot store an isolated 'Resp' into 'Sent.notSent.r', which holds a mutable 'Resp'; `merge` it first
```

<!-- test: model-hand-ac-ul-union-literal-iso-return -->
The model (`core-m3w-fa13`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `ul-union-literal-iso-return` of redteamAC.hand_ac7, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function f() returns Sent
	let r = Resp.create(0) as isolated Resp
	let s = Sent.notSent(r)
	r.fin(1)
	return s
end 'f'

function main() returns ExitCode
	useS(f())
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:35:23: cannot store an isolated 'Resp' into 'Sent.notSent.r', which holds a mutable 'Resp'; `merge` it first
```

<!-- test: model-hand-ac-ul-union-literal-iso-dropped-s -->
The model (`core-m3w-fa13`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `ul-union-literal-iso-dropped-s` of redteamAC.hand_ac7, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let _ = Sent.notSent(r)
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:28:23: cannot store an isolated 'Resp' into 'Sent.notSent.r', which holds a mutable 'Resp'; `merge` it first
```

<!-- test: model-hand-ac-ul-union-literal-plain -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ul-union-literal-plain` of redteamAC.hand_ac7, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let r = Resp.create(0)
	let s = Sent.notSent(r)
	r.fin(1)
	useS(s)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:36:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ul-iso-let-copy -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ul-iso-let-copy` of redteamAC.hand_ac7, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let q = r
	r.fin(1)
	q.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ul-iso-return-then-use -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ul-iso-return-then-use` of redteamAC.hand_ac7, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function f() returns isolated Resp
	let r = Resp.create(0) as isolated Resp
	r.fin(1)
	return r
end 'f'

function main() returns ExitCode
	let q = f()
	q.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:9: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ul-iso-iso-param-conversion -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ul-iso-iso-param-conversion` of redteamAC.hand_ac7, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function useRI2(r isolated Resp)
	r.fin(1)
end 'useRI2'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	useRI2(r)
	useR(r)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:7: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ul-iso-plain-then-iso-param -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ul-iso-plain-then-iso-param` of redteamAC.hand_ac7, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function useRI2(r isolated Resp)
	r.fin(1)
end 'useRI2'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	useR(r)
	useRI2(r)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:9: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ul-iso-peek-in-fn-after -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ul-iso-peek-in-fn-after` of redteamAC.hand_ac7, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	useR(r)
	let z = r.peek()
	r.fin(z)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:10: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ul-iso-handback-after-lend -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ul-iso-handback-after-lend` of redteamAC.hand_ac7, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq3'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq3.create(0)
	let r = Resp.create(0) as isolated Resp
	useR(r)
	try a.take(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:41:13: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ul-iso-then-return -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ul-iso-then-return` of redteamAC.hand_ac7, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function useR(r Resp)
	r.fin(1)
end 'useR'

function f() returns isolated Resp
	let r = Resp.create(0) as isolated Resp
	useR(r)
	return r
end 'f'

function main() returns ExitCode
	let q = f()
	q.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:9: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ul-iso-lend-in-while-cond -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ul-iso-lend-in-while-cond` of redteamAC.hand_ac7, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function consuming(r Resp) returns Num
	r.fin(1)
	return 0
end 'consuming'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	while consuming(r) != 0 'w1'
		let _ = 0 as Num
	end 'w1'
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:18: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ac-gh-loopbrk-then-drop -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `gh-loopbrk-then-drop` of redteamAC.hand_ac8, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function loopbrk()
	let i = 1 as Num
	while i != 0 'w1'
		break
	end 'w1'
end 'loopbrk'

function main() returns ExitCode
	let _ = Resp.create(0)
	loopbrk()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:31:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-gh-loopbrk-then-consume -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `gh-loopbrk-then-consume` of redteamAC.hand_ac8, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function loopbrk()
	let i = 1 as Num
	while i != 0 'w1'
		break
	end 'w1'
end 'loopbrk'

function main() returns ExitCode
	let r = Resp.create(0)
	loopbrk()
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-rec-mutual-plain -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `rec-mutual-plain` of redteamAC.hand_ac8, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function f1(r Resp, c Num)
	if c != 0 'i1'
		g1(r, c: 0)
	end 'i1' else 'e2'
		r.fin(1)
	end 'e2'
end 'f1'

function g1(r Resp, c Num)
	f1(r, c: c)
end 'g1'

function main() returns ExitCode
	let r = Resp.create(0)
	f1(r, c: 1)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:36:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-rec-self-plain -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `rec-self-plain` of redteamAC.hand_ac8, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function h1(r Resp, c Num)
	if c != 0 'i1'
		h1(r, c: 0)
	end 'i1' else 'e2'
		r.fin(1)
	end 'e2'
end 'h1'

function main() returns ExitCode
	let r = Resp.create(0)
	h1(r, c: 1)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-rec-self-iso -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `rec-self-iso` of redteamAC.hand_ac8, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function h1(r Resp, c Num)
	if c != 0 'i1'
		h1(r, c: 0)
	end 'i1' else 'e2'
		r.fin(1)
	end 'e2'
end 'h1'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	h1(r, c: 1)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-rec-chain-plain -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `rec-chain-plain` of redteamAC.hand_ac8, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function k1(r Resp, c Num)
	if c != 0 'i1'
		k2(r, c: 0)
	end 'i1' else 'e2'
		k2(r, c: 1)
	end 'e2'
end 'k1'

function k2(r Resp, c Num)
	if c != 0 'i1'
		r.fin(1)
	end 'i1' else 'e2'
		r.fin(2)
	end 'e2'
end 'k2'

function main() returns ExitCode
	let r = Resp.create(0)
	k1(r, c: 1)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:40:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-rec-chain-iso -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `rec-chain-iso` of redteamAC.hand_ac8, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function k1(r Resp, c Num)
	if c != 0 'i1'
		k2(r, c: 0)
	end 'i1' else 'e2'
		k2(r, c: 1)
	end 'e2'
end 'k1'

function k2(r Resp, c Num)
	if c != 0 'i1'
		r.fin(1)
	end 'i1' else 'e2'
		r.fin(2)
	end 'e2'
end 'k2'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	k1(r, c: 1)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:40:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-rec-forward-ref -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `rec-forward-ref` of redteamAC.hand_ac8, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function early(r Resp)
	late(r)
	r.fin(1)
end 'early'

function late(r Resp)
	r.fin(1)
end 'late'

function main() returns ExitCode
	early(Resp.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:23:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-lp-break-skips-refill -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `lp-break-skips-refill` of redteamAC.hand_ac8, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var r = Resp.create(0)
	var i = 2 as Num
	while i != 0 'w1'
		r.fin(1)
		if lg(1) != 0 'i2'
			break
		end 'i2'
		r = Resp.create(2)
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:28:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-lp-continue-skips-refill -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `lp-continue-skips-refill` of redteamAC.hand_ac8, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var r = Resp.create(0)
	var i = 2 as Num
	while i != 0 'w1'
		r.fin(1)
		i = lg(0)
		if lg(1) != 0 'i2'
			r = Resp.create(2)
			continue
		end 'i2'
		r = Resp.create(3)
	end 'w1'
	r.fin(9)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-lp-zero-iter-for -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `lp-zero-iter-for` of redteamAC.hand_ac8, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let a = NumArray.create(0)
	let r = Resp.create(0)
	for _ in a 'f1'
		r.fin(1)
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:26:3: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ac-lp-while-false-consumes -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `lp-while-false-consumes` of redteamAC.hand_ac8, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0)
	while 0 != 0 'w1'
		r.fin(1)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:3: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ac-lp-while-true-consumes-break -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `lp-while-true-consumes-break` of redteamAC.hand_ac8, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0)
	while 1 != 0 'w1'
		r.fin(1)
		break
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:23:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-lp-while-true-consume-after -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `lp-while-true-consume-after` of redteamAC.hand_ac8, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0)
	while 1 != 0 'w1'
		r.fin(1)
		break
	end 'w1'
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-lp-loop-condition-consumes-fresh -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `lp-loop-condition-consumes-fresh` of redteamAC.hand_ac8, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function ckr(r Resp) returns Num
	r.fin(1)
	return 0
end 'ckr'

function main() returns ExitCode
	var i = 1 as Num
	while ckr(Resp.create(0)) != 0 'w1'
		i = 0
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-hq-fin-only -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hq-fin-only` of redteamAC.hand_ac9, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq4
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq4'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq4.create(0)
	try a.take(Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x2'
	end 'h1'
	try a.take(Resp.create(1)) otherwise (sendFailure) 'h3'
		match sendFailure 'x4'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x4'
	end 'h3'
	try a.take(Resp.create(2)) otherwise (sendFailure) 'h5'
		match sendFailure 'x6'
			mailboxFull(q) then q.fin(1)
			serviceStopped(q) then q.fin(1)
		end 'x6'
	end 'h5'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-hq-use-only -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hq-use-only` of redteamAC.hand_ac9, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq4
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(t Resp)
		t.fin(1)
	end 'take'
end 'Hq4'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq4.create(0)
	try a.take(Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then useR(q)
			serviceStopped(q) then useR(q)
		end 'x2'
	end 'h1'
	try a.take(Resp.create(1)) otherwise (sendFailure) 'h3'
		match sendFailure 'x4'
			mailboxFull(q) then useR(q)
			serviceStopped(q) then useR(q)
		end 'x4'
	end 'h3'
	try a.take(Resp.create(2)) otherwise (sendFailure) 'h5'
		match sendFailure 'x6'
			mailboxFull(q) then useR(q)
			serviceStopped(q) then useR(q)
		end 'x6'
	end 'h5'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-hq-union-sent -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hq-union-sent` of redteamAC.hand_ac9, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hu
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeS(s Sent)
		useS(s)
	end 'takeS'
end 'Hu'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hu.create(0)
	try a.takeS(giveS(Resp.create(0))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(q) then useS(q)
			serviceStopped(q) then useS(q)
		end 'x2'
	end 'h1'
	try a.takeS(giveS(Resp.create(1))) otherwise (sendFailure) 'h3'
		match sendFailure 'x4'
			mailboxFull(q) then useS(q)
			serviceStopped(q) then useS(q)
		end 'x4'
	end 'h3'
	try a.takeS(giveS(Resp.create(2))) otherwise (sendFailure) 'h5'
		match sendFailure 'x6'
			mailboxFull(q) then useS(q)
			serviceStopped(q) then useS(q)
		end 'x6'
	end 'h5'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-n-wrap-main -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `n-wrap-main` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

union Wrap
	w0
	w1(s Sent)
end 'Wrap'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function mkW() returns Wrap
	return Wrap.w1(Sent.notSent(Resp.create(0)))
end 'mkW'

function useW(w Wrap)
	match w 'x1'
		w0 then break
		w1(s) then useS(s)
	end 'x1'
end 'useW'

function main() returns ExitCode
	let w = mkW()
	useW(w)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-n-wrap-drop -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `n-wrap-drop` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

union Wrap
	w0
	w1(s Sent)
end 'Wrap'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function mkW() returns Wrap
	return Wrap.w1(Sent.notSent(Resp.create(0)))
end 'mkW'

function main() returns ExitCode
	let _ = mkW()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:45:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-n-wrap-twice -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `n-wrap-twice` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

union Wrap
	w0
	w1(s Sent)
end 'Wrap'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function mkW() returns Wrap
	return Wrap.w1(Sent.notSent(Resp.create(0)))
end 'mkW'

function useW(w Wrap)
	match w 'x1'
		w0 then break
		w1(s) then useS(s)
	end 'x1'
end 'useW'

function main() returns ExitCode
	let w = mkW()
	useW(w)
	useW(w)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:60:7: use of moved value 'w': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-n-wrap-match-leak-payload -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `n-wrap-match-leak-payload` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

union Wrap
	w0
	w1(s Sent)
end 'Wrap'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function mkW() returns Wrap
	return Wrap.w1(Sent.notSent(Resp.create(0)))
end 'mkW'

function main() returns ExitCode
	let w = mkW()
	match w 'x1'
		w0 then break
		w1 then break
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:47:3: the case 'w1' carries the linear value 's', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ad-n-wrap-match-s-and-w -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `n-wrap-match-s-and-w` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

union Wrap
	w0
	w1(s Sent)
end 'Wrap'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function mkW() returns Wrap
	return Wrap.w1(Sent.notSent(Resp.create(0)))
end 'mkW'

function useW(w Wrap)
	match w 'x1'
		w0 then break
		w1(s) then useS(s)
	end 'x1'
end 'useW'

function main() returns ExitCode
	let w = mkW()
	match w 'x1'
		w0 then break
		w1(s) then useS(s)
	end 'x1'
	useW(w)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:63:7: use of moved value 'w': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-n-two-pair -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `n-two-pair` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Two
	none
	pair(a Resp, b Resp)
end 'Two'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let t = Two.pair(Resp.create(0), b: Resp.create(1))
	match t 'x1'
		none then break
		pair(a, _) then a.fin(1)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:38:3: the case 'pair' carries the linear value 'b', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ad-c-break-drop -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `c-break-drop` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var i = 2 as Num
	while i != 0 'w1'
		let r = Resp.create(0)
		if lg(1) != 0 'i2'
			break
		end 'i2'
		r.fin(1)
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:34:4: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-c-continue-drop -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `c-continue-drop` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var i = 2 as Num
	while i != 0 'w1'
		let r = Resp.create(0)
		i = lg(0)
		if lg(1) != 0 'i2'
			continue
		end 'i2'
		r.fin(1)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:35:4: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-c-return-in-loop -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `c-return-in-loop` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function f()
	let r = Resp.create(0)
	var i = 2 as Num
	while i != 0 'w1'
		if lg(1) != 0 'i2'
			return
		end 'i2'
		i = lg(0)
	end 'w1'
	r.fin(1)
end 'f'

function main() returns ExitCode
	f()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:34:4: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-c-loop-create-consume -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `c-loop-create-consume` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var i = 2 as Num
	while i != 0 'w1'
		let r = Resp.create(0)
		r.fin(1)
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-c-loop-consume-outer -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `c-loop-consume-outer` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0)
	var i = 2 as Num
	while i != 0 'w1'
		r.fin(1)
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:33:3: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ad-c-loop-consume-and-refill -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `c-loop-consume-and-refill` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var r = Resp.create(0)
	var i = 2 as Num
	while i != 0 'w1'
		r.fin(1)
		r = Resp.create(2)
		i = lg(0)
	end 'w1'
	r.fin(3)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-c-loop-refill-break-before -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `c-loop-refill-break-before` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var r = Resp.create(0)
	var i = 2 as Num
	while i != 0 'w1'
		r.fin(1)
		if lg(1) != 0 'i2'
			break
		end 'i2'
		r = Resp.create(2)
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:32:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-c-loop-refill-break-after-consume -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `c-loop-refill-break-after-consume` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var r = Resp.create(0)
	var i = 2 as Num
	while i != 0 'w1'
		r.fin(1)
		if lg(1) != 0 'i2'
			break
		end 'i2'
		r = Resp.create(2)
		i = lg(0)
	end 'w1'
	r.fin(5)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:40:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-c-if-else-consume -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `c-if-else-consume` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0)
	if lg(1) != 0 'i1'
		r.fin(1)
	end 'i1' else 'e2'
		r.fin(2)
	end 'e2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-c-if-one-arm -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `c-if-one-arm` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0)
	if lg(1) != 0 'i1'
		r.fin(1)
	end 'i1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:31:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-c-nested-if -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `c-nested-if` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0)
	if lg(1) != 0 'i1'
		if lg(2) != 0 'i2'
			r.fin(1)
		end 'i2' else 'e3'
			r.fin(2)
		end 'e3'
	end 'i1' else 'e4'
		if lg(3) != 0 'i5'
			r.fin(3)
		end 'i5'
	end 'e4'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:38:3: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-c-while-cond-consume -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `c-while-cond-consume` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function fin2(r Resp) returns Num
	r.fin(1)
	return 0
end 'fin2'

function main() returns ExitCode
	let r = Resp.create(0)
	while fin2(r) != 0 'w1'
		let _ = 0 as Num
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:36:13: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ad-c-while-cond-consume-once -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `c-while-cond-consume-once` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function fin2(r Resp) returns Num
	r.fin(1)
	return 1
end 'fin2'

function main() returns ExitCode
	let r = Resp.create(0)
	var i = 2 as Num
	while fin2(r) != 0 'w1'
		i = lg(0)
		if lg(i) == 0 'i2'
			break
		end 'i2'
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:37:13: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ad-c-if-cond-consume -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `c-if-cond-consume` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function fin2(r Resp) returns Num
	r.fin(1)
	return 1
end 'fin2'

function main() returns ExitCode
	let r = Resp.create(0)
	if fin2(r) != 0 'i1'
		r.fin(1)
	end 'i1' else 'e2'
		r.fin(1)
	end 'e2'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:37:3: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-s-shadow -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-shadow` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0)
	if lg(1) != 0 'i1'
		let r = Resp.create(1)
		r.fin(1)
	end 'i1'
	r.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-s-shadow-drop-inner -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `s-shadow-drop-inner` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0)
	if lg(1) != 0 'i1'
		let _ = Resp.create(1)
	end 'i1'
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:33:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-s-overwrite -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `s-overwrite` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var r = Resp.create(0)
	r = Resp.create(1)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:31:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-s-overwrite-moved -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-overwrite-moved` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var r = Resp.create(0)
	r.fin(1)
	r = Resp.create(1)
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-s-overwrite-in-if -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `s-overwrite-in-if` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var r = Resp.create(0)
	if lg(1) != 0 'i1'
		r.fin(1)
	end 'i1'
	r = Resp.create(1)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:31:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-s-overwrite-with-self-call -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-overwrite-with-self-call` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function nxt(r Resp) returns Resp
	r.fin(1)
	return Resp.create(2)
end 'nxt'

function main() returns ExitCode
	var r = Resp.create(0)
	r = nxt(r)
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-s-overwrite-with-keep -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-overwrite-with-keep` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function keepR(r Resp) returns Resp
	return r
end 'keepR'

function main() returns ExitCode
	var r = Resp.create(0)
	r = keepR(r)
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-s-let-from-var -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `s-let-from-var` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0)
	let a = r
	a.fin(1)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:33:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-s-alias-in-if -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `s-alias-in-if` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0)
	if lg(1) != 0 'i1'
		let a = r
		a.fin(1)
	end 'i1'
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:35:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-s-alias-in-loop -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `s-alias-in-loop` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0)
	var i = 2 as Num
	while i != 0 'w1'
		let a = r
		a.fin(1)
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:33:11: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ad-s-assign-alias -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `s-assign-alias` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0)
	var a = Resp.create(1)
	a = r
	a.fin(1)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:34:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-w-one-call-open-consume -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `w-one-call-open-consume` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	r.fin(1)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-w-one-stmt-read -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-one-stmt-read` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let z = r.code
	r.fin(z)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-w-one-stmt-bump -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-one-stmt-bump` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	r.bump()
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-w-one-stmt-assign -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-one-stmt-assign` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	r.code = 5
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-o-cond-new -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `o-cond-new` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let r = (Resp.create(0) if lg(1) != 0 else Resp.create(1))
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-o-cond-new-drop -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `o-cond-new-drop` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = (Resp.create(0) if lg(1) != 0 else Resp.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:31:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-o-cond-var-fresh -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-COND`; the compiler refuses it with E3223. Model program `o-cond-var-fresh` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let a = Resp.create(0)
	let r = (a if lg(1) != 0 else Resp.create(1))
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:33:2: this drops 'a', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-o-iter-array -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3224. Model program `o-iter-array` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias SentArray = Array with Sent

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function bump()
		self.code = lg(1)
	end 'bump'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let xs = SentArray.create(0)
	xs.push(giveS(Resp.create(0)))
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:3:11: 'Sent' is a linear value and cannot be a type argument of 'Array': a linear value lives only in locals, parameters and message arguments, so no container, promise, generic record or generic function holds one
```

<!-- test: model-hand-ad-o-record-method-passes-self -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-SELF`; the compiler refuses it with E3224. Model program `o-record-method-passes-self` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Rs
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function go()
		eatRs(self)
	end 'go'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Rs'

function eatRs(x Rs)
	x.fin()
end 'eatRs'

function main() returns ExitCode
	let r = Rs.create(0)
	r.go()
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:12:3: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
```

<!-- test: model-hand-ad-o-consuming-method-passes-self-twice -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `o-consuming-method-passes-self-twice` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Rs
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function go(consuming self)
		eatRs(self)
		eatRs(self)
	end 'go'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Rs'

function eatRs(x Rs)
	x.fin()
end 'eatRs'

function main() returns ExitCode
	let r = Rs.create(0)
	r.go()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:13:9: use of moved value '__self': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-o-consuming-method-keeps-self-out -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `o-consuming-method-keeps-self-out` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Rs
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function go(consuming self) returns Rs
		return self
	end 'go'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Rs'

function main() returns ExitCode
	let r = Rs.create(0)
	let a = r.go()
	a.fin()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-o-consuming-method-self-in-union -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `o-consuming-method-self-in-union` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Us
	u0
	u1(r Rs)
end 'Us'

type Rs
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function go(consuming self) returns Us
		return Us.u1(self)
	end 'go'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Rs'

function main() returns ExitCode
	let r = Rs.create(0)
	let a = r.go()
	match a 'x1'
		u0 then break
		u1(x) then x.fin()
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-o-consuming-method-self-destruct-after-pass -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `o-consuming-method-self-destruct-after-pass` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Rs
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function go(consuming self)
		eatRs(self)
		destruct self
	end 'go'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Rs'

function eatRs(x Rs)
	x.fin()
end 'eatRs'

function main() returns ExitCode
	let r = Rs.create(0)
	r.go()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:13:12: use of moved value '__self': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-o-consuming-method-peek-after-destruct -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `o-consuming-method-peek-after-destruct` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Rs
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function pk() returns Num
		return self.n
	end 'pk'

	function go(consuming self) returns Num
		destruct self
		return self.pk()
	end 'go'
end 'Rs'

function main() returns ExitCode
	let r = Rs.create(0)
	let _ = r.go()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:17:10: use of moved value 'self': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-n-handler-shadows-outer -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `n-handler-shadows-outer` of redteamAD.hand_ad4, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(r Resp)
		r.fin(1)
	end 'take'

	export function two(r Resp, s Resp)
		r.fin(1)
		s.fin(1)
	end 'two'

	export function mix(x Num, r Resp, y Num)
		let _ = y
		r.fin(x)
	end 'mix'

	export function takeS(s Sent)
		useS(s)
	end 'takeS'
end 'Hq'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let r = Resp.create(0)
	let a = spawn(capacity: 1) Hq.create(0)
	try a.take(Resp.create(1)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(r) then r.fin(1)
			serviceStopped(r) then r.fin(1)
		end 'x2'
	end 'h1'
	try a.take(Resp.create(2)) otherwise (sendFailure) 'h3'
		match sendFailure 'x4'
			mailboxFull(r) then r.fin(1)
			serviceStopped(r) then r.fin(1)
		end 'x4'
	end 'h3'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:74:2: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-n-handler-name-is-sent-var-use-after -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `n-handler-name-is-sent-var-use-after` of redteamAD.hand_ad4, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(r Resp)
		r.fin(1)
	end 'take'

	export function two(r Resp, s Resp)
		r.fin(1)
		s.fin(1)
	end 'two'

	export function mix(x Num, r Resp, y Num)
		let _ = y
		r.fin(x)
	end 'mix'

	export function takeS(s Sent)
		useS(s)
	end 'takeS'
end 'Hq'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let r = Resp.create(0)
	let a = spawn(capacity: 1) Hq.create(0)
	try a.take(r) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(r) then r.fin(1)
			serviceStopped(r) then r.fin(1)
		end 'x2'
	end 'h1'
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:68:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-n-arm-name-is-scrutinee -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `n-arm-name-is-scrutinee` of redteamAD.hand_ad4, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let s = giveS(Resp.create(0))
	match s 'x1'
		ok then break
		notSent(s) then s.fin(1)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-n-arm-name-is-scrutinee-after -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `n-arm-name-is-scrutinee-after` of redteamAD.hand_ad4, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let s = giveS(Resp.create(0))
	match s 'x1'
		ok then break
		notSent(s) then s.fin(1)
	end 'x1'
	useS(s)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:43:7: use of moved value 's': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-n-arm-name-is-outer-linear -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `n-arm-name-is-outer-linear` of redteamAD.hand_ad4, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let r = Resp.create(7)
	let s = giveS(Resp.create(0))
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
	r.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-n-arm-name-is-outer-linear-drop -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `n-arm-name-is-outer-linear-drop` of redteamAD.hand_ad4, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let r = Resp.create(7)
	let s = giveS(Resp.create(0))
	match s 'x1'
		ok then break
		notSent then break
	end 'x1'
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:35:3: the case 'notSent' carries the linear value 'r', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ad-n-arm-name-is-outer-linear-use-outer -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `n-arm-name-is-outer-linear-use-outer` of redteamAD.hand_ad4, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	let r = Resp.create(7)
	let s = giveS(Resp.create(0))
	match s 'x1'
		ok then r.fin(1)
		notSent(r) then r.fin(1)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:33:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-j-break-in-arm -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `j-break-in-arm` of redteamAD.hand_ad4, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function main() returns ExitCode
	var i = 2 as Num
	while i != 0 'w1'
		let s = giveS(Resp.create(0))
		match s 'x2'
			ok then break
			notSent then break 'w1'
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:40:4: the case 'notSent' carries the linear value 'r', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ad-j-return-in-arm -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `j-return-in-arm` of redteamAD.hand_ad4, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function f()
	let s = giveS(Resp.create(0))
	match s 'x1'
		ok then break
		notSent then return
	end 'x1'
end 'f'

function main() returns ExitCode
	f()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:34:3: the case 'notSent' carries the linear value 'r', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ad-j-handler-return -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3224. Model program `j-handler-return` of redteamAD.hand_ad4, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(r Resp)
		r.fin(1)
	end 'take'

	export function two(r Resp, s Resp)
		r.fin(1)
		s.fin(1)
	end 'two'

	export function mix(x Num, r Resp, y Num)
		let _ = y
		r.fin(x)
	end 'mix'

	export function takeS(s Sent)
		useS(s)
	end 'takeS'
end 'Hq'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function f(a Hq.handle)
	try a.take(Resp.create(0)) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull then return
			serviceStopped then return
		end 'x2'
	end 'h1'
end 'f'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	f(a)
	f(a)
	f(a)
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:62:4: the case 'mailboxFull' carries the linear value 'r', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: model-hand-ad-j-handler-in-loop-no-refill -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `j-handler-in-loop-no-refill` of redteamAD.hand_ad4, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(r Resp)
		r.fin(1)
	end 'take'

	export function two(r Resp, s Resp)
		r.fin(1)
		s.fin(1)
	end 'two'

	export function mix(x Num, r Resp, y Num)
		let _ = y
		r.fin(x)
	end 'mix'

	export function takeS(s Sent)
		useS(s)
	end 'takeS'
end 'Hq'

function lg(x Num) returns Num
	return x
end 'lg'

function useS(s Sent)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	let r = Resp.create(0)
	var i = 3 as Num
	while i != 0 'w1'
		try a.take(r) otherwise (sendFailure) 'h2'
			match sendFailure 'x3'
				mailboxFull(q) then q.fin(1)
				serviceStopped(q) then q.fin(1)
			end 'x3'
		end 'h2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:68:14: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ad-k-forward-keeper -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `k-forward-keeper` of redteamAD.hand_ad7, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function later1(r Resp)
	r.fin(1)
end 'later1'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	later1(r)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-k-forward-keeper-chain -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `k-forward-keeper-chain` of redteamAD.hand_ad7, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function c1(r Resp)
	c2(r)
end 'c1'

function c2(r Resp)
	c3(r)
end 'c2'

function c3(r Resp)
	r.fin(1)
end 'c3'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	c1(r)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:36:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-k-forward-keeper-mutual -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `k-forward-keeper-mutual` of redteamAD.hand_ad7, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function m1(r Resp, n Num)
	if n != 0 'i1'
		m2(r, n: lg(0))
	end 'i1' else 'e2'
		r.fin(1)
	end 'e2'
end 'm1'

function m2(r Resp, n Num)
	m1(r, n: n)
end 'm2'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	m1(r, n: 3)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:40:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-k-forward-keeper-self -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `k-forward-keeper-self` of redteamAD.hand_ad7, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function m1(r Resp, n Num)
	if n != 0 'i1'
		m1(r, n: lg(0))
	end 'i1' else 'e2'
		r.fin(1)
	end 'e2'
end 'm1'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	m1(r, n: 3)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:36:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-k-forward-keeper-self-no-keep -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-forward-keeper-self-no-keep` of redteamAD.hand_ad7, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function lg(x Num) returns Num
	return x
end 'lg'

function m1(r Resp, n Num)
	if n != 0 'i1'
		m1(r, n: lg(0))
	end 'i1'
end 'm1'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	m1(r, n: 3)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:34:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-k-forward-keeper-through-method -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-forward-keeper-through-method` of redteamAD.hand_ad7, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hd
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'

	function eat(r Resp)
		r.fin(1)
	end 'eat'

	function eatBoth(r Resp, s Resp)
		r.fin(1)
		s.fin(1)
	end 'eatBoth'
end 'Hd'

function viaH(h Hd, r Resp)
	h.eat(r)
end 'viaH'

function main() returns ExitCode
	let h = Hd.create(0)
	let r = Resp.create(0) as isolated Resp
	viaH(h, r: r)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:45:7: 'h' is mutable outside 'r'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ad-k-forward-keeper-in-spawn -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-SEND`; the compiler refuses it with E3224. Model program `k-forward-keeper-in-spawn` of redteamAD.hand_ad7, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Kf
	var n as Num

	static function create(r Resp) returns Self
		r.fin(1)
		return Self{n: 1}
	end 'create'
end 'Kf'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let _ = spawn Kf.create(r)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:32:19: a `spawn` takes no linear argument: a factory that cannot start the service drops the value it was given, and a linear value is consumed exactly once. Send the value to the service in a message once it is running
```

<!-- test: model-hand-ad-k-forward-keeper-in-new -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-forward-keeper-in-new` of redteamAD.hand_ad7, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

type Hh
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hh'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let _ = Hh.create(0)
	useR(r)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-k-forward-keeper-union-fn -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `k-forward-keeper-union-fn` of redteamAD.hand_ad7, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Resp)
end 'Sn'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function later2(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'later2'

function main() returns ExitCode
	let s = Sn.notSent(Resp.create(0)) as isolated Sn
	later2(s)
	later2(s)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:36:9: use of moved value 's': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-k-forward-keeper-union-fn-once -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-forward-keeper-union-fn-once` of redteamAD.hand_ad7, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Resp)
end 'Sn'

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'
end 'Resp'

function later2(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'later2'

function main() returns ExitCode
	let s = Sn.notSent(Resp.create(0)) as isolated Sn
	later2(s)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-hole-match-window -->
The model (`core-m3w-fa13`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `hole-match-window` of redteamAD.hand_ad9, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(0)) as isolated Ub
	let v = Rl.create(0) as isolated Rl
	match u 'x1'
		u0 then useS(giveS(v))
		u1 then useS(giveS(v))
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:50:16: 'giveS' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: model-hand-af-m1-union-method-match-self -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-SELF`; the compiler refuses it with E3224. Model program `m1-union-method-match-self` of redteamAF.hand_af1, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Um
	none
	held(r isolated Rl)
	function eat()
		match self 'x1'
			none then break
			held(q) then q.fin(1)
		end 'x1'
	end 'eat'
end 'Um'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function main() returns ExitCode
	let u = Um.held(Rl.create(1))
	u.eat()
	match u 'x1'
		none then break
		held(q) then q.fin(2)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:14:4: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
```

<!-- test: model-hand-af-m2-union-method-match-self-once -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-SELF`; the compiler refuses it with E3224. Model program `m2-union-method-match-self-once` of redteamAF.hand_af1, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Um
	none
	held(r isolated Rl)
	function eat()
		match self 'x1'
			none then break
			held(q) then q.fin(1)
		end 'x1'
	end 'eat'
end 'Um'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function main() returns ExitCode
	let u = Um.held(Rl.create(1))
	u.eat()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:14:4: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
```

<!-- test: model-hand-af-m4-union-method-peek-then-match -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m4-union-method-peek-then-match` of redteamAF.hand_af1, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Uq
	none
	held(r isolated Rl)
	function peekit() returns Num
		return 1
	end 'peekit'
end 'Uq'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function main() returns ExitCode
	let u = Uq.held(Rl.create(1))
	let _ = u.peekit()
	match u 'x1'
		none then break
		held(q) then q.fin(2)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-b1-byref-union-param -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `b1-byref-union-param` of redteamAF.hand_af1, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function eatM(u mutable Sh)
	match u 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatM'

function main() returns ExitCode
	var u = Sh.held(Rl.create(1))
	eatM(u)
	match u 'x1'
		none then break
		held(q) then q.fin(2)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:41:3: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
error E3224: <fragment>:50:3: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
```

<!-- test: model-hand-af-c1-cond-union-scrutinee -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-COND`; the compiler refuses it with E3223. Model program `c1-cond-union-scrutinee` of redteamAF.hand_af1, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let a = Sh.held(Rl.create(1))
	let b = Sh.held(Rl.create(2))
	match (a if lg(1) != 0 else b) 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
	match a 'x2'
		none then break
		held(q) then q.fin(3)
	end 'x2'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:54:2: this drops 'b', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-af-r1-record-field-union-match -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3224. Model program `r1-record-field-union-match` of redteamAF.hand_af1, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Uq
	none
	held(r isolated Rl)
	function peekit() returns Num
		return 1
	end 'peekit'
end 'Uq'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Pk
	var u as Uq

	static function create(u Uq) returns Self
		return Self{u: u}
	end 'create'
end 'Pk'

function main() returns ExitCode
	let k = Pk.create(Uq.held(Rl.create(1)))
	match k.u 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:43:6: the field 'u' holds a linear value, and a linear value lives only in locals, parameters and message arguments: a field would keep it past the code that must consume it
error E3224: <fragment>:53:3: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
```

<!-- test: model-hand-af-b4-binder-named-like-linear-outer -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `b4-binder-named-like-linear-outer` of redteamAF.hand_af15, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function main() returns ExitCode
	let x = Rl.create(9)
	let u = Sh.held(Rl.create(1))
	match u 'x1'
		none then break
		held(x) then x.fin(1)
	end 'x1'
	x.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-sh1-binder-iso-param -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `sh1-binder-iso-param` of redteamAF.hand_af16, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function useI(r isolated Rl)
	r.fin(1)
end 'useI'

function main() returns ExitCode
	let q = Rl.create(9) as isolated Rl
	let u = Sh.held(Rl.create(1))
	match u 'x1'
		none then break
		held(q) then useI(q)
	end 'x1'
	q.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-sh4-inner-let-iso-param -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `sh4-inner-let-iso-param` of redteamAF.hand_af16, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function useI(r isolated Rl)
	r.fin(1)
end 'useI'

function main() returns ExitCode
	let q = Rl.create(9) as isolated Rl
	if lg(1) != 0 'i1'
		let q = Rl.create(3) as isolated Rl
		useI(q)
	end 'i1'
	q.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-sh5-inner-let-send -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `sh5-inner-let-send` of redteamAF.hand_af16, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type S
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function plain() returns Num
		return 5
	end 'plain'

	export function go(r Rl)
		r.fin(1)
	end 'go'
end 'S'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let k = spawn S.create(0)
	let q = Rl.create(9) as isolated Rl
	if lg(1) != 0 'i1'
		let q = Rl.create(3) as isolated Rl
		try k.go(q) otherwise (sendFailure) 'h2'
			match sendFailure 'x3'
				mailboxFull(z) then z.fin(2)
				serviceStopped(z) then z.fin(2)
			end 'x3'
		end 'h2'
	end 'i1'
	q.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-sh6-inner-let-in-loop -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `sh6-inner-let-in-loop` of redteamAF.hand_af16, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function useI(r isolated Rl)
	r.fin(1)
end 'useI'

function main() returns ExitCode
	let q = Rl.create(9) as isolated Rl
	var i = 2 as Num
	while i != 0 'w1'
		let q = Rl.create(3) as isolated Rl
		useI(q)
		i = lg(0)
	end 'w1'
	q.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-sh8-binder-iso-bx -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `sh8-binder-iso-bx` of redteamAF.hand_af16, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mq
	m0(x Rl)
	m1(y isolated Bx)
end 'Mq'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let y = Bx.create(9) as isolated Bx
	let m = Mq.m1(Bx.create(1))
	match m 'x1'
		m0(x) then x.fin(1)
		m1(y) then takeB(y)
	end 'x1'
	takeB(y)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-sh12-param-shadow -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `sh12-param-shadow` of redteamAF.hand_af16, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mq
	m0(x Rl)
	m1(y isolated Bx)
end 'Mq'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function f(y isolated Bx, m Mq)
	match m 'x1'
		m0(x) then x.fin(1)
		m1(y) then takeB(y)
	end 'x1'
	takeB(y)
end 'f'

function main() returns ExitCode
	f(Bx.create(1), m: Mq.m1(Bx.create(2)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-sh14-loop-reuse -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `sh14-loop-reuse` of redteamAF.hand_af16, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mq
	m0(x Rl)
	m1(y isolated Bx)
end 'Mq'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function lg(x Num) returns Num
	return x
end 'lg'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let y = Bx.create(9) as isolated Bx
	var i = 2 as Num
	while i != 0 'w1'
		let m = Mq.m1(Bx.create(1))
		match m 'x2'
			m0(x) then x.fin(1)
			m1(y) then takeB(y)
		end 'x2'
		i = lg(0)
	end 'w1'
	takeB(y)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-l1-continue-after-consume -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `l1-continue-after-consume` of redteamAF.hand_af22, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let u = Sh.held(Rl.create(1))
	var i = 2 as Num
	while i != 0 'w1'
		match u 'x2'
			none then break
			held(q) then q.fin(1)
		end 'x2'
		i = lg(0)
		continue
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:47:9: use of moved value 'u': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-af-l2-conditional-continue-after-consume -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `l2-conditional-continue-after-consume` of redteamAF.hand_af22, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let u = Sh.held(Rl.create(1))
	let i = 2 as Num
	while i != 0 'w1'
		match u 'x2'
			none then break
			held(q) then q.fin(1)
		end 'x2'
		if lg(1) != 0 'i3'
			continue
		end 'i3'
		break
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:47:9: use of moved value 'u': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-af-l3-break-after-consume -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `l3-break-after-consume` of redteamAF.hand_af22, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function main() returns ExitCode
	let u = Sh.held(Rl.create(1))
	let i = 2 as Num
	while i != 0 'w1'
		match u 'x2'
			none then break
			held(q) then q.fin(1)
		end 'x2'
		break
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:42:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-af-l4-continue-before-consume -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `l4-continue-before-consume` of redteamAF.hand_af22, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let u = Sh.held(Rl.create(1))
	let i = 2 as Num
	while i != 0 'w1'
		if lg(0) != 0 'i2'
			continue
		end 'i2'
		match u 'x3'
			none then break
			held(q) then q.fin(1)
		end 'x3'
		break
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:46:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-af-l5-continue-skips-consume-then-consume-after-loop -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `l5-continue-skips-consume-then-consume-after-loop` of redteamAF.hand_af22, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let u = Sh.held(Rl.create(1))
	var i = 2 as Num
	while i != 0 'w1'
		if lg(1) != 0 'i2'
			i = lg(0)
			continue
		end 'i2'
		match u 'x3'
			none then break
			held(q) then q.fin(1)
		end 'x3'
		break
	end 'w1'
	match u 'x4'
		none then break
		held(q) then q.fin(1)
	end 'x4'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:57:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-af-l6-refill-before-continue -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l6-refill-before-continue` of redteamAF.hand_af22, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var u = Sh.held(Rl.create(1))
	var i = 2 as Num
	while i != 0 'w1'
		match u 'x2'
			none then break
			held(q) then q.fin(1)
		end 'x2'
		u = Sh.held(Rl.create(2))
		i = lg(0)
		continue
	end 'w1'
	match u 'x3'
		none then break
		held(q) then q.fin(1)
	end 'x3'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-l7-refill-only-on-one-path -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `l7-refill-only-on-one-path` of redteamAF.hand_af22, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var u = Sh.held(Rl.create(1))
	let i = 2 as Num
	while i != 0 'w1'
		match u 'x2'
			none then break
			held(q) then q.fin(1)
		end 'x2'
		if lg(1) != 0 'i3'
			u = Sh.held(Rl.create(2))
			continue
		end 'i3'
		break
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:46:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-af-l8-refill-only-on-one-path-then-use -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `l8-refill-only-on-one-path-then-use` of redteamAF.hand_af22, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var u = Sh.held(Rl.create(1))
	let i = 2 as Num
	while i != 0 'w1'
		match u 'x2'
			none then break
			held(q) then q.fin(1)
		end 'x2'
		if lg(1) != 0 'i3'
			u = Sh.held(Rl.create(2))
			continue
		end 'i3'
		break
	end 'w1'
	match u 'x4'
		none then break
		held(q) then q.fin(1)
	end 'x4'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:57:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-af-l11-for-break -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `l11-for-break` of redteamAF.hand_af22, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function main() returns ExitCode
	let u = Sh.held(Rl.create(1))
	let xs = NumArray.create(0)
	xs.push(1)
	xs.push(2)
	for _ in xs 'f1'
		match u 'x2'
			none then break
			held(q) then q.fin(1)
		end 'x2'
		break
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:45:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-af-l12-for-continue-consume -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `l12-for-continue-consume` of redteamAF.hand_af22, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function main() returns ExitCode
	let u = Sh.held(Rl.create(1))
	let xs = NumArray.create(0)
	xs.push(1)
	xs.push(2)
	for _ in xs 'f1'
		match u 'x2'
			none then break
			held(q) then q.fin(1)
		end 'x2'
		continue
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:46:9: use of moved value 'u': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-af-s1-binder-named-like-outer-iso -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s1-binder-named-like-outer-iso` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function main() returns ExitCode
	let q = Rl.create(9) as isolated Rl
	let u = Sh.held(Rl.create(1))
	match u 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
	q.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-s2-binder-named-like-outer-plain -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s2-binder-named-like-outer-plain` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function main() returns ExitCode
	let q = Rl.create(9)
	let u = Sh.held(Rl.create(1))
	match u 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
	q.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-s3-binder-named-like-scrutinee -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s3-binder-named-like-scrutinee` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function main() returns ExitCode
	let u = Sh.held(Rl.create(1))
	match u 'x1'
		none then break
		held(u) then u.fin(1)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-s6-binder-two-arms-same-name -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s6-binder-two-arms-same-name` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mx
	m0(x Rl)
	m1(y isolated Bx)
end 'Mx'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let u = Mx.m1(Bx.create(1))
	match u 'x1'
		m0(x) then x.fin(1)
		m1(x) then takeB(x)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-s7-binder-reuse-after -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s7-binder-reuse-after` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function main() returns ExitCode
	let u = Sh.held(Rl.create(1))
	match u 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
	let q = Rl.create(2)
	q.fin(3)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-s8-binder-shadows-param -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `s8-binder-shadows-param` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function f(q isolated Rl, u Sh)
	match u 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Rl.create(5), u: Sh.held(Rl.create(1)))
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:39:10: this drops 'q', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-af-s9-binder-shadows-iso-param-kept -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s9-binder-shadows-iso-param-kept` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function f(q isolated Rl, u Sh)
	match u 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
	q.fin(2)
end 'f'

function main() returns ExitCode
	f(Rl.create(5), u: Sh.held(Rl.create(1)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-s14-binder-shadows-then-moved-away -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s14-binder-shadows-then-moved-away` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function useI(r isolated Rl)
	r.fin(1)
end 'useI'

function main() returns ExitCode
	var q = Rl.create(9) as isolated Rl
	useI(q)
	let u = Sh.held(Rl.create(1))
	match u 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
	q = Rl.create(3)
	q.fin(4)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-s15-plain-payload-binder-shadow -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s15-plain-payload-binder-shadow` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let y = Bx.create(5)
	let u = Mp.m1(Bx.create(1))
	match u 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.n = 2
	end 'x1'
	y.n = 3
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-p1-plain-mixed-in-fn -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-plain-mixed-in-fn` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function eatMp(u Mp)
	match u 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.n = 2
	end 'x1'
end 'eatMp'

function main() returns ExitCode
	let u = Mp.m1(Bx.create(1))
	eatMp(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-p4-plain-mixed-share-payload -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p4-plain-mixed-share-payload` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let b = Bx.create(1)
	let u = Mp.m1(b)
	match u 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.n = 2
	end 'x1'
	b.n = 3
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-p6-plain-mixed-readonly-payload-in-union-from-param -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p6-plain-mixed-readonly-payload-in-union-from-param` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function buildw(b Bx) returns Mp
	return Mp.m1(b)
end 'buildw'

function main() returns ExitCode
	let b = Bx.create(1)
	match buildw(b) 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.n = 2
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-p7-plain-mixed-frozen-payload -->
The model (`core-m3w-fa13`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `p7-plain-mixed-frozen-payload` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let b = freeze (Bx.create(1))
	let u = Mp.m1(b)
	match u 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.n = 2
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:49:16: cannot store an immutable 'Bx' into 'Mp.m1.y', which holds a mutable 'Bx'; declare the payload `immutable Bx`
```

<!-- test: model-hand-af-p8-plain-mixed-immutable-payload-field -->
The model (`core-m3w-fa13`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `p8-plain-mixed-immutable-payload-field` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mq
	m0(x Rl)
	m1(y immutable Bx)
end 'Mq'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let u = Mq.m1(freeze (Bx.create(1)))
	match u 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.n = 2
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:51:14: cannot write 'n' through 'y', which is immutable
```

<!-- test: model-hand-af-r1-union-result-adapting -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r1-union-result-adapting` of redteamAF.hand_af5, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function idSh(p Sh) returns Sh
	return p
end 'idSh'

function main() returns ExitCode
	let u = idSh(Sh.held(Rl.create(1)))
	match u 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-r2-union-result-twice -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `r2-union-result-twice` of redteamAF.hand_af5, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function idSh(p Sh) returns Sh
	return p
end 'idSh'

function main() returns ExitCode
	let u = Sh.held(Rl.create(1))
	let v = idSh(u)
	match v 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
	match u 'x2'
		none then break
		held(q) then q.fin(2)
	end 'x2'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:50:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-af-r11-two-match-same-temp-call -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r11-two-match-same-temp-call` of redteamAF.hand_af5, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function fresh() returns Sh
	return Sh.held(Rl.create(1))
end 'fresh'

function main() returns ExitCode
	match fresh() 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
	match fresh() 'x2'
		none then break
		held(q) then q.fin(2)
	end 'x2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-r12-match-in-condition -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r12-match-in-condition` of redteamAF.hand_af5, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let u = (Sh.held(Rl.create(1)) if lg(1) != 0 else Sh.held(Rl.create(2)))
	match u 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-r17-loop-break-in-arm -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `r17-loop-break-in-arm` of redteamAF.hand_af5, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var i = 2 as Num
	var u = Sh.held(Rl.create(1))
	while i != 0 'w1'
		match u 'x2'
			none then break
			held(q) then q.fin(1)
		end 'x2'
		u = Sh.none
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:54:2: this drops 'u', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-af-r18-loop-refill-with-none -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r18-loop-refill-with-none` of redteamAF.hand_af5, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var i = 2 as Num
	var u = Sh.held(Rl.create(1))
	while i != 0 'w1'
		match u 'x2'
			none then break
			held(q) then q.fin(1)
		end 'x2'
		u = Sh.held(Rl.create(2))
		i = lg(0)
	end 'w1'
	match u 'x3'
		none then break
		held(q) then q.fin(3)
	end 'x3'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-w4-method-on-outer-record -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w4-method-on-outer-record` of redteamAF.hand_af8, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Own
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eatI(r isolated Rl)
		r.fin(1)
	end 'eatI'

	function eatU(u Sh)
		match u 'x1'
			none then break
			held(q) then q.fin(1)
		end 'x1'
	end 'eatU'
end 'Own'

function main() returns ExitCode
	let o = Own.create(0)
	match Sh.held(Rl.create(1)) 'x1'
		none then break
		held(q) then o.eatI(q)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-w5-union-to-record-method -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w5-union-to-record-method` of redteamAF.hand_af8, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Own
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eatI(r isolated Rl)
		r.fin(1)
	end 'eatI'

	function eatU(u Sh)
		match u 'x1'
			none then break
			held(q) then q.fin(1)
		end 'x1'
	end 'eatU'
end 'Own'

function main() returns ExitCode
	let o = Own.create(0)
	o.eatU(Sh.held(Rl.create(1)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-w10-two-cases-two-sinks -->
The model (`core-m3w-fa13`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `w10-two-cases-two-sinks` of redteamAF.hand_af8, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mx
	m0(x Rl)
	m1(y isolated Bx)
end 'Mx'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function useI(r isolated Rl)
	r.fin(1)
end 'useI'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let m = Mx.m1(Bx.create(1))
	match m 'x1'
		m0(x) then useI(x)
		m1(y) then takeB(y)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:58:19: 'x' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-af-w11-mixed-nonlinear-moved-to-send -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w11-mixed-nonlinear-moved-to-send` of redteamAF.hand_af8, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mx
	m0(x Rl)
	m1(y isolated Bx)
end 'Mx'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Bk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		b.n = 1
	end 'takeB'
end 'Bk'

function main() returns ExitCode
	let k = spawn Bk.create(0)
	let m = Mx.m1(Bx.create(1))
	match m 'x1'
		m0(x) then x.fin(1)
		m1(y) then try k.takeB(y) otherwise ignore
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-w12-early-return-in-arm -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w12-early-return-in-arm` of redteamAF.hand_af8, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function first(u Sh, c Num)
	match u 'x1'
		none then return
		held(q) then q.fin(c)
	end 'x1'
end 'first'

function main() returns ExitCode
	first(Sh.held(Rl.create(1)), c: 2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-w13-union-built-in-branch -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w13-union-built-in-branch` of redteamAF.hand_af8, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var u = Sh.none
	if lg(1) != 0 'i1'
		u = Sh.held(Rl.create(1))
	end 'i1' else 'e2'
		u = Sh.held(Rl.create(2))
	end 'e2'
	match u 'x3'
		none then break
		held(q) then q.fin(1)
	end 'x3'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-w14-match-then-rebuild-same-var -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `w14-match-then-rebuild-same-var` of redteamAF.hand_af8, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function giveH(r isolated Rl) returns Sh
	return Sh.held(r)
end 'giveH'

function main() returns ExitCode
	var u = Sh.held(Rl.create(1))
	match u 'x1'
		none then break
		held(q) then u = giveH(q)
	end 'x1'
	match u 'x2'
		none then break
		held(q) then q.fin(1)
	end 'x2'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:49:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-af-m1-param-union-match-fin -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-SEND`; the compiler refuses it with E3223. Model program `m1-param-union-match-fin` of redteamAF.hand_af9, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Kx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function eat(u Sh)
		match u 'x1'
			none then break
			held(q) then q.fin(1)
		end 'x1'
	end 'eat'
end 'Kx'

function main() returns ExitCode
	let a = spawn Kx.create(0)
	try a.eat(Sh.held(Rl.create(1))) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:56:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-af-m5-param-union-two-matches-in-two-behaviours -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-SEND`; the compiler refuses it with E3223. Model program `m5-param-union-two-matches-in-two-behaviours` of redteamAF.hand_af9, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Kx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function a(u Sh)
		match u 'x1'
			none then break
			held(q) then q.fin(1)
		end 'x1'
	end 'a'

	export function b(u Sh)
		match u 'x1'
			none then break
			held(q) then q.fin(2)
		end 'x1'
	end 'b'
end 'Kx'

function main() returns ExitCode
	let a = spawn Kx.create(0)
	try a.a(Sh.held(Rl.create(1))) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:63:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-af-m7-param-union-in-function -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m7-param-union-in-function` of redteamAF.hand_af9, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function eat(u Sh)
	match u 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eat'

function main() returns ExitCode
	eat(Sh.held(Rl.create(1)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-m8-param-union-in-function-in-behaviour -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m8-param-union-in-function-in-behaviour` of redteamAF.hand_af9, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Kx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function go()
		eat(Sh.held(Rl.create(1)))
	end 'go'
end 'Kx'

function eat(u Sh)
	match u 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eat'

function main() returns ExitCode
	let a = spawn Kx.create(0)
	try a.go() otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-m9-behaviour-local-union -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m9-behaviour-local-union` of redteamAF.hand_af9, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Kx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function go()
		let u = Sh.held(Rl.create(1))
		match u 'x1'
			none then break
			held(q) then q.fin(1)
		end 'x1'
	end 'go'
end 'Kx'

function main() returns ExitCode
	let a = spawn Kx.create(0)
	try a.go() otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-m10-behaviour-param-union-iso-fn -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-SEND`; the compiler refuses it with E3223. Model program `m10-behaviour-param-union-iso-fn` of redteamAF.hand_af9, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Kx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function eat(u Sh)
		match u 'x1'
			none then break
			held(q) then useI(q)
		end 'x1'
	end 'eat'
end 'Kx'

function useI(r isolated Rl)
	r.fin(1)
end 'useI'

function main() returns ExitCode
	let a = spawn Kx.create(0)
	try a.eat(Sh.held(Rl.create(1))) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:60:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-af-m12-behaviour-param-mixed-store-assign -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-SEND`; the compiler refuses it with E3223. Model program `m12-behaviour-param-mixed-store-assign` of redteamAF.hand_af9, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mx
	m0(x Rl)
	m1(y isolated Bx)
end 'Mx'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Kx
	var n as Num
	var b as isolated Bx

	static function create(n Num, b isolated Bx) returns Self
		return Self{n: n, b: b}
	end 'create'

	export function eat(u Mx)
		match u 'x1'
			m0(x) then x.fin(1)
			m1(y) then self.b = y
		end 'x1'
	end 'eat'
end 'Kx'

function main() returns ExitCode
	let a = spawn Kx.create(0, b: Bx.create(0))
	try a.eat(Mx.m1(Bx.create(1))) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:65:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-af-m13-behaviour-param-plain-store -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-SEND`; the compiler refuses it with E3223. Model program `m13-behaviour-param-plain-store` of redteamAF.hand_af9, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Kx
	var n as Num
	var b as Bx

	static function create(n Num, b isolated Bx) returns Self
		return Self{n: n, b: merge b}
	end 'create'

	export function eat(u Mp)
		match u 'x1'
			m0(x) then x.fin(1)
			m1(y) then self.b = y
		end 'x1'
	end 'eat'
end 'Kx'

function main() returns ExitCode
	let a = spawn Kx.create(0, b: Bx.create(0))
	try a.eat(Mp.m1(Bx.create(1))) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:65:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-bm-nowin-keep-p -->
The model (`core-m3w-fa13`) refuses it by `C-STORE`; the compiler refuses it with E3201. Model program `bm-nowin-keep-p` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mr
	m0(x Rl)
	m1(y Hq)
end 'Mr'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hq
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hq'

type Bm
	var n as Num
	var item as isolated Wo
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Wo, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	function putKeep(b Bx)
		self.item.keep = b
	end 'putKeep'

	export function eat(u Mr)
		match u 'x1'
			m0(x) then x.fin(1)
			m1(y) then self.item.keep = y.p
		end 'x1'
	end 'eat'
end 'Bm'

function eatMr(pV Mr)
	match pV 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.q.n = 1
	end 'x1'
end 'eatMr'

function main() returns ExitCode
	let k = spawn Bm.create(0, item: Wo.create(Bx.create(0), item: Bx.create(1)), plain: Bx.create(2), hq: Hq.create(Bx.create(3), q: Bx.create(4)))
	try k.eat(Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2)))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z) then eatMr(z)
			serviceStopped(z) then eatMr(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:76:20: 'b' is mutable outside 'self.item'; a store into an isolated region takes values, immutable or isolated
error E3201: <fragment>:82:32: 'y.p' is mutable outside 'self.item'; a store into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ag-bm-nowin-plain -->
The model (`core-m3w-fa13`) refuses it by `C-STORE`; the compiler refuses it with E3201. Model program `bm-nowin-plain` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mr
	m0(x Rl)
	m1(y Hq)
end 'Mr'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hq
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hq'

type Bm
	var n as Num
	var item as isolated Wo
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Wo, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	function putKeep(b Bx)
		self.item.keep = b
	end 'putKeep'

	export function eat(u Mr)
		match u 'x1'
			m0(x) then x.fin(1)
			m1(y) then self.plain = y.p
		end 'x1'
	end 'eat'
end 'Bm'

function eatMr(pV Mr)
	match pV 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.q.n = 1
	end 'x1'
end 'eatMr'

function main() returns ExitCode
	let k = spawn Bm.create(0, item: Wo.create(Bx.create(0), item: Bx.create(1)), plain: Bx.create(2), hq: Hq.create(Bx.create(3), q: Bx.create(4)))
	try k.eat(Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2)))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z) then eatMr(z)
			serviceStopped(z) then eatMr(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:76:20: 'b' is mutable outside 'self.item'; a store into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ag-bm-nowin-hq -->
The model (`core-m3w-fa13`) refuses it by `C-STORE`; the compiler refuses it with E3201. Model program `bm-nowin-hq` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mr
	m0(x Rl)
	m1(y Hq)
end 'Mr'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hq
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hq'

type Bm
	var n as Num
	var item as isolated Wo
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Wo, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	function putKeep(b Bx)
		self.item.keep = b
	end 'putKeep'

	export function eat(u Mr)
		match u 'x1'
			m0(x) then x.fin(1)
			m1(y) then self.hq = y
		end 'x1'
	end 'eat'
end 'Bm'

function eatMr(pV Mr)
	match pV 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.q.n = 1
	end 'x1'
end 'eatMr'

function main() returns ExitCode
	let k = spawn Bm.create(0, item: Wo.create(Bx.create(0), item: Bx.create(1)), plain: Bx.create(2), hq: Hq.create(Bx.create(3), q: Bx.create(4)))
	try k.eat(Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2)))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z) then eatMr(z)
			serviceStopped(z) then eatMr(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:76:20: 'b' is mutable outside 'self.item'; a store into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ag-f1-control-no-window -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f1-control-no-window` of redteamAG.hand_ag16, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

function keepIn(b Bx, w Wo)
	w.keep = b
end 'keepIn'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1))
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	match u 'x1'
		m0(x) then x.fin(1)
		m1(y) then keepIn(y, w: h)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-s6-w4-none-in-both-then-held -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s6-w4-none-in-both-then-held` of redteamAG.hand_ag17, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function main() returns ExitCode
	var u = Sh.none
	if lg(1) != 0 'i1'
		u = Sh.held(Rl.create(1))
	end 'i1' else 'e2'
		u = Sh.held(Rl.create(2))
	end 'e2'
	eatSh(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-s7-w4-none-loop-fill-consume -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s7-w4-none-loop-fill-consume` of redteamAG.hand_ag17, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function main() returns ExitCode
	var u = Sh.none
	var i = 2 as Num
	while i != 0 'w1'
		u = Sh.held(Rl.create(1))
		eatSh(u)
		u = Sh.none
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-s11-w3-local-inline-new -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s11-w3-local-inline-new` of redteamAG.hand_ag17, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mq
	m0(x Rl)
	m1(y Wo)
end 'Mq'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

function main() returns ExitCode
	let u = Mq.m1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Mq
	match u 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.keep.n = 1
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-s14-w2-refill-from-arm-fn -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s14-w2-refill-from-arm-fn` of redteamAG.hand_ag17, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function again(q isolated Rl) returns Sh
	return Sh.held(q)
end 'again'

function main() returns ExitCode
	var u = Sh.held(Rl.create(1))
	match u 'x1'
		none then u = Sh.none
		held(q) then u = again(q)
	end 'x1'
	eatSh(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-h9-handback-overwrite-vacant -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa13`) refuses it by `C-STORE`; the compiler refuses it with E3223. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `h9-handback-overwrite-vacant` of redteamAG.hand_ag18, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

union Mx
	m0(x Rl)
	m1(y isolated Bx)
end 'Mx'

union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

union Mr
	m0(x Rl)
	m1(y Hq)
end 'Mr'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hq
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hq'

type Cap1
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeSh(u Sh)
		match u 'x1'
			none then break
			held(qV) then qV.fin(1)
		end 'x1'
	end 'takeSh'

	export function takeMp(u Mp)
		match u 'x1'
			m0(x) then x.fin(1)
			m1(y) then y.n = 1
		end 'x1'
	end 'takeMp'

	export function takeMx(u Mx)
		match u 'x1'
			m0(x) then x.fin(1)
			m1(y) then takeB(y)
		end 'x1'
	end 'takeMx'

	export function takeMr(u Mr)
		match u 'x1'
			m0(x) then x.fin(1)
			m1(y) then y.q.n = 1
		end 'x1'
	end 'takeMr'

	export function poke()
		self.n = 1
	end 'poke'
end 'Cap1'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function eatSh(pV Sh)
	match pV 'x1'
		none then break
		held(qV) then qV.fin(1)
	end 'x1'
end 'eatSh'

function main() returns ExitCode
	let k = spawn(capacity: 1) Cap1.create(0)
	try k.poke() otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull then break
			serviceStopped then break
		end 'x2'
	end 'h1'
	var v = Sh.none
	try k.takeSh(Sh.held(Rl.create(1))) otherwise (sendFailure) 'h3'
		match sendFailure 'x4'
			mailboxFull(z) then v = z
			serviceStopped(z) then v = z
		end 'x4'
	end 'h3'
	v = Sh.none
	eatSh(v)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:137:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-b5-binder-named-like-scrutinee -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `b5-binder-named-like-scrutinee` of redteamAG.hand_ag19, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function main() returns ExitCode
	var u = Sh.held(Rl.create(1))
	match u 'x1'
		none then u = Sh.none
		held(u) then u.fin(1)
	end 'x1'
	match u 'x2'
		none then break
		held(q) then q.fin(2)
	end 'x2'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:45:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ag-b6-binder-named-like-scrutinee-refill -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `b6-binder-named-like-scrutinee-refill` of redteamAG.hand_ag19, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function main() returns ExitCode
	var u = Sh.held(Rl.create(1))
	match u 'x1'
		none then u = Sh.none
		held(u) then u.fin(1)
	end 'x1'
	eatSh(u)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:52:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ag-beh-mp-state-plain -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `beh-mp-state-plain` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hq
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hq'

type Bm
	var n as Num
	var item as isolated Bx
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Bx, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	function keepP(b Bx)
		self.plain = b
	end 'keepP'

	export function eat(u Mp)
		match u 'x1'
			m0(x) then x.fin(1)
			m1(y) then self.plain = y
		end 'x1'
	end 'eat'
end 'Bm'

function eatMp(pV Mp)
	match pV 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.n = 1
	end 'x1'
end 'eatMp'

function main() returns ExitCode
	let k = spawn Bm.create(0, item: Bx.create(0), plain: Bx.create(1), hq: Hq.create(Bx.create(1), q: Bx.create(1)))
	try k.eat(Mp.m1(Bx.create(1))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z) then eatMp(z)
			serviceStopped(z) then eatMp(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-beh-mp-state-write -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `beh-mp-state-write` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hq
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hq'

type Bm
	var n as Num
	var item as isolated Bx
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Bx, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	function keepP(b Bx)
		self.plain = b
	end 'keepP'

	export function eat(u Mp)
		match u 'x1'
			m0(x) then x.fin(1)
			m1(y) then y.n = 3
		end 'x1'
	end 'eat'
end 'Bm'

function eatMp(pV Mp)
	match pV 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.n = 1
	end 'x1'
end 'eatMp'

function main() returns ExitCode
	let k = spawn Bm.create(0, item: Bx.create(0), plain: Bx.create(1), hq: Hq.create(Bx.create(1), q: Bx.create(1)))
	try k.eat(Mp.m1(Bx.create(1))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z) then eatMp(z)
			serviceStopped(z) then eatMp(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-beh-mp-state-method -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `beh-mp-state-method` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hq
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hq'

type Bm
	var n as Num
	var item as isolated Bx
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Bx, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	function keepP(b Bx)
		self.plain = b
	end 'keepP'

	export function eat(u Mp)
		match u 'x1'
			m0(x) then x.fin(1)
			m1(y) then self.keepP(y)
		end 'x1'
	end 'eat'
end 'Bm'

function eatMp(pV Mp)
	match pV 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.n = 1
	end 'x1'
end 'eatMp'

function main() returns ExitCode
	let k = spawn Bm.create(0, item: Bx.create(0), plain: Bx.create(1), hq: Hq.create(Bx.create(1), q: Bx.create(1)))
	try k.eat(Mp.m1(Bx.create(1))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z) then eatMp(z)
			serviceStopped(z) then eatMp(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-beh-mp-state-none -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `beh-mp-state-none` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hq
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hq'

type Bm
	var n as Num
	var item as isolated Bx
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Bx, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	function keepP(b Bx)
		self.plain = b
	end 'keepP'

	export function eat(u Mp)
		match u 'x1'
			m0(x) then x.fin(1)
			m1 then break
		end 'x1'
	end 'eat'
end 'Bm'

function eatMp(pV Mp)
	match pV 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.n = 1
	end 'x1'
end 'eatMp'

function main() returns ExitCode
	let k = spawn Bm.create(0, item: Bx.create(0), plain: Bx.create(1), hq: Hq.create(Bx.create(1), q: Bx.create(1)))
	try k.eat(Mp.m1(Bx.create(1))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z) then eatMp(z)
			serviceStopped(z) then eatMp(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-beh-mr-state-hq -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `beh-mr-state-hq` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mr
	m0(x Rl)
	m1(y Hq)
end 'Mr'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hq
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hq'

type Bm
	var n as Num
	var item as isolated Bx
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Bx, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	export function eat(u Mr)
		match u 'x1'
			m0(x) then x.fin(1)
			m1(y) then self.hq = y
		end 'x1'
	end 'eat'
end 'Bm'

function eatMr(pV Mr)
	match pV 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.q.n = 1
	end 'x1'
end 'eatMr'

function main() returns ExitCode
	let k = spawn Bm.create(0, item: Bx.create(0), plain: Bx.create(1), hq: Hq.create(Bx.create(1), q: Bx.create(1)))
	try k.eat(Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2)))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z) then eatMr(z)
			serviceStopped(z) then eatMr(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-beh-mr-state-hq-part -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `beh-mr-state-hq-part` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mr
	m0(x Rl)
	m1(y Hq)
end 'Mr'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hq
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hq'

type Bm
	var n as Num
	var item as isolated Bx
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Bx, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	export function eat(u Mr)
		match u 'x1'
			m0(x) then x.fin(1)
			m1(y) then self.plain = y.p
		end 'x1'
	end 'eat'
end 'Bm'

function eatMr(pV Mr)
	match pV 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.q.n = 1
	end 'x1'
end 'eatMr'

function main() returns ExitCode
	let k = spawn Bm.create(0, item: Bx.create(0), plain: Bx.create(1), hq: Hq.create(Bx.create(1), q: Bx.create(1)))
	try k.eat(Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2)))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z) then eatMr(z)
			serviceStopped(z) then eatMr(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-v5-unique-case-overwritten -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `v5-unique-case-overwritten` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Uk
	a(x Rl)
	k(t Tk)
end 'Uk'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	var u = Uk.k(Tk.create(1))
	u = Uk.a(Rl.create(1))
	match u 'x1'
		a(x) then x.fin(1)
		k then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-v6-iso-unique-case-overwritten -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `v6-iso-unique-case-overwritten` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Ui
	a(x Rl)
	k(t isolated Tk)
end 'Ui'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	var u = Ui.k(Tk.create(1))
	u = Ui.a(Rl.create(1))
	match u 'x1'
		a(x) then x.fin(1)
		k then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-v7-union-holding-union-none -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `v7-union-holding-union-none` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

union Un
	a(x Rl)
	n(u Sh)
end 'Un'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function eatUn(p Un)
	match p 'x1'
		a(x) then x.fin(1)
		n(u) then eatSh(u)
	end 'x1'
end 'eatUn'

function main() returns ExitCode
	var u = Un.n(Sh.none)
	u = Un.a(Rl.create(1))
	eatUn(u)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:60:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-v8-union-holding-held-union -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `v8-union-holding-held-union` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

union Un
	a(x Rl)
	n(u Sh)
end 'Un'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function eatUn(p Un)
	match p 'x1'
		a(x) then x.fin(1)
		n(u) then eatSh(u)
	end 'x1'
end 'eatUn'

function main() returns ExitCode
	var u = Un.n(Sh.held(Rl.create(1)))
	u = Un.a(Rl.create(2))
	eatUn(u)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:60:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-v9-isolated-plain-case -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `v9-isolated-plain-case` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Uh
	a(x Rl)
	h(b isolated Bx)
	z
end 'Uh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function eatUh(p Uh)
	match p 'x1'
		a(x) then x.fin(1)
		h(b) then takeB(b)
		z then break
	end 'x1'
end 'eatUh'

function main() returns ExitCode
	var u = Uh.h(Bx.create(1))
	u = Uh.z
	eatUh(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-v10-no-payload-case-of-three -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `v10-no-payload-case-of-three` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Uq
	z
	r(x Rl)
	s(b Bx)
end 'Uq'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function eatUq(p Uq)
	match p 'x1'
		z then break
		r(x) then x.fin(1)
		s(b) then b.n = 1
	end 'x1'
end 'eatUq'

function main() returns ExitCode
	var u = Uq.z
	u = Uq.r(Rl.create(1))
	eatUq(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-v11-plain-payload-case -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `v11-plain-payload-case` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Uq
	z
	r(x Rl)
	s(b Bx)
end 'Uq'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function eatUq(p Uq)
	match p 'x1'
		z then break
		r(x) then x.fin(1)
		s(b) then b.n = 1
	end 'x1'
end 'eatUq'

function main() returns ExitCode
	var u = Uq.s(Bx.create(1))
	u = Uq.r(Rl.create(1))
	eatUq(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-v12-plain-payload-aliased -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `v12-plain-payload-aliased` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Uq
	z
	r(x Rl)
	s(b Bx)
end 'Uq'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function eatUq(p Uq)
	match p 'x1'
		z then break
		r(x) then x.fin(1)
		s(b) then b.n = 1
	end 'x1'
end 'eatUq'

function main() returns ExitCode
	let b = Bx.create(1)
	var u = Uq.s(b)
	u = Uq.r(Rl.create(1))
	b.n = 3
	eatUq(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-v21-vacant-loop-fill-then-none -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `v21-vacant-loop-fill-then-none` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function main() returns ExitCode
	var u = Sh.none
	var i = 3 as Num
	while i != 0 'w1'
		if lg(1) != 0 'i2'
			u = Sh.held(Rl.create(i))
		end 'i2' else 'e3'
			u = Sh.none
		end 'e3'
		i = lg(0)
	end 'w1'
	eatSh(u)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:55:4: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
error E3223: <fragment>:57:4: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-v22-vacant-loop-break-held -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `v22-vacant-loop-break-held` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function main() returns ExitCode
	var u = Sh.none
	let i = 3 as Num
	while i != 0 'w1'
		u = Sh.held(Rl.create(i))
		break
	end 'w1'
	u = Sh.none
	eatSh(u)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:53:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-v23-vacant-loop-continue -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `v23-vacant-loop-continue` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function main() returns ExitCode
	var u = Sh.none
	var i = 3 as Num
	while i != 0 'w1'
		u = Sh.none
		u = Sh.held(Rl.create(i))
		i = lg(0)
		continue
	end 'w1'
	eatSh(u)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:54:3: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-v24-vacant-for-loop -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `v24-vacant-for-loop` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function main() returns ExitCode
	let xs = NumArray.create(0)
	xs.push(1)
	xs.push(2)
	var u = Sh.none
	for x in xs 'f1'
		u = Sh.none
		if lg(x) != 0 'i2'
			u = Sh.held(Rl.create(x))
		end 'i2'
	end 'f1'
	eatSh(u)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:57:3: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-v29-vacant-shadow-same-scope -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `v29-vacant-shadow-same-scope` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function main() returns ExitCode
	var u = Sh.none
	if lg(1) != 0 'i1'
		let u = Sh.held(Rl.create(1))
		eatSh(u)
	end 'i1'
	u = Sh.none
	eatSh(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-v31-vacant-in-behaviour-state-swap -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3224. Model program `v31-vacant-in-behaviour-state-swap` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Sx
	var n as Num
	var s as Sh

	static function create(n Num, s isolated Sh) returns Self
		return Self{n: n, s: merge s}
	end 'create'

	export function go()
		var u = Sh.none
		u = swap self.s with Sh.held(Rl.create(2))
		u = Sh.none
		eatSh(u)
	end 'go'
end 'Sx'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function main() returns ExitCode
	let k = spawn Sx.create(0, s: Sh.held(Rl.create(1)))
	try k.go() otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:41:6: the field 's' holds a linear value, and a linear value lives only in locals, parameters and message arguments: a field would keep it past the code that must consume it
error E3224: <fragment>:63:19: a `spawn` takes no linear argument: a factory that cannot start the service drops the value it was given, and a linear value is consumed exactly once. Send the value to the service in a message once it is running
```

<!-- test: model-hand-ag-v32-vacant-fill-from-state-then-overwrite -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3224. Model program `v32-vacant-fill-from-state-then-overwrite` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Sx
	var n as Num
	var s as Sh

	static function create(n Num, s isolated Sh) returns Self
		return Self{n: n, s: merge s}
	end 'create'

	export function go()
		var u = Sh.none
		let t = swap self.s with Sh.none
		u = t
		u = Sh.none
		eatSh(u)
	end 'go'
end 'Sx'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function main() returns ExitCode
	let k = spawn Sx.create(0, s: Sh.held(Rl.create(1)))
	try k.go() otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:41:6: the field 's' holds a linear value, and a linear value lives only in locals, parameters and message arguments: a field would keep it past the code that must consume it
error E3224: <fragment>:64:19: a `spawn` takes no linear argument: a factory that cannot start the service drops the value it was given, and a linear value is consumed exactly once. Send the value to the service in a message once it is running
```

<!-- test: model-hand-ag-v33-vacant-moved-then-refilled-then-overwritten -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `v33-vacant-moved-then-refilled-then-overwritten` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function main() returns ExitCode
	var u = Sh.none
	let w = u
	u = Sh.held(Rl.create(1))
	u = Sh.none
	eatSh(w)
	eatSh(u)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:50:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-v34-vacant-cond -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `v34-vacant-cond` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function main() returns ExitCode
	var u = Sh.none
	if lg(1) != 0 'i1'
		u = Sh.held(Rl.create(1))
	end 'i1' else 'e2'
		u = Sh.none
	end 'e2'
	u = Sh.none
	eatSh(u)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:57:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-v35-vacant-match-arm-join -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `v35-vacant-match-arm-join` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function main() returns ExitCode
	let w = Sh.held(Rl.create(2))
	var u = Sh.none
	match w 'x1'
		none then u = Sh.none
		held(q) then u = Sh.held(q)
	end 'x1'
	u = Sh.none
	eatSh(u)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:53:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-v37-vacant-return-path -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `v37-vacant-return-path` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function f(c Num)
	var u = Sh.none
	if c != 0 'i1'
		u = Sh.held(Rl.create(1))
		return
	end 'i1'
	u = Sh.none
	eatSh(u)
end 'f'

function main() returns ExitCode
	f(lg(1))
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:54:3: this drops 'u', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-v39-vacant-scope-end-none -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `v39-vacant-scope-end-none` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function main() returns ExitCode
	var u = Sh.none
	u = Sh.none
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-v40-vacant-scope-end-after-if -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `v40-vacant-scope-end-after-if` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var u = Sh.none
	if lg(1) != 0 'i1'
		u = Sh.held(Rl.create(1))
	end 'i1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:48:2: this drops 'u', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-v44-vacant-pair-none-overwrite-pair -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `v44-vacant-pair-none-overwrite-pair` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sp
	none
	pair(a isolated Rl, b isolated Rl)
end 'Sp'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function main() returns ExitCode
	var u = Sp.none
	u = Sp.pair(Rl.create(1), b: Rl.create(2))
	u = Sp.none
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:42:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-v45-vacant-twice-with-held-between-in-expr -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `v45-vacant-twice-with-held-between-in-expr` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function idSh(s Sh) returns Sh
	return s
end 'idSh'

function main() returns ExitCode
	var u = Sh.none
	u = idSh(u)
	u = Sh.none
	eatSh(u)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:53:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-v46-vacant-assign-from-call-returning-u -->
The model (`core-m3w-fa13`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `v46-vacant-assign-from-call-returning-u` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function mkH(s Sh) returns Sh
	eatSh(s)
	return Sh.held(Rl.create(3))
end 'mkH'

function main() returns ExitCode
	var u = Sh.none
	u = mkH(u)
	u = Sh.none
	eatSh(u)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:54:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ag-r1-refill-before-payload-moved-into-it -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r1-refill-before-payload-moved-into-it` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Sh
	none
	held(r isolated Rl)
end 'Sh'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function main() returns ExitCode
	var u = Sh.held(Rl.create(1))
	match u 'x1'
		none then u = Sh.none
		held(q) then u = Sh.held(q)
	end 'x1'
	eatSh(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-l5-wrap-no-window -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l5-wrap-no-window` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mx
	m0(x Rl)
	m1(y isolated Bx)
end 'Mx'

type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function peek() returns Num
		return self.code
	end 'peek'

	function set(c Num)
		self.code = c
	end 'set'

	function wrap(consuming self) returns Sn
		return Sn.notSent(self)
	end 'wrap'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let u = Mx.m0(Rl.create(1)) as isolated Mx
	match u 'x1'
		m0(x) then useS(x.wrap())
		m1(y) then takeB(y)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```
