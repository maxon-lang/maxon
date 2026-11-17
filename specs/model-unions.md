---
feature: model-unions
status: experimental
keywords: [model, capmodel, region, capability]
category: semantics
---

# Unions and matches

## Documentation

These cases are programs of the capmodel executable model, translated to Maxon by
`scripts/model-specs/generate.py` (`docs/SPECS.md`, "Model-derived specs") and pinned to the model's verdict on
unions, payload bindings and `match`. A program the model accepts runs and exits 0; one it refuses fails to compile, with the diagnostic
the compiler gives. A case is written only where the compiler and the model agree, so the suite fails
the day the compiler stops agreeing with a verdict the model pinned. The rule set is `core-m3w-fa24`.

## Tests

<!-- test: model-finding-ae-arm-outer-write-nonlinear-ctl -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `arm-outer-write-nonlinear-ctl` of redteamAE/p10.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r isolated Bk)
end 'Sh'

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
	let u = Sh.held(Bk.create(1))
	match u 'x1'
		none then break
		held then b.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-frozen-union-iso-arm-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `frozen-union-iso-arm-write` of redteamAE/p16.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Cnt
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'
end 'Cnt'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(0)) as isolated Ub
	let f = freeze u
	let c = Cnt.create(0)
	match f 'x1'
		u0 then break
		u1 then c.k = 3
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-param-union-iso-arm-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `param-union-iso-arm-write` of redteamAE/p16.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Cnt
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'
end 'Cnt'

function f(u Ub, c Cnt)
	match u 'x1'
		u0 then break
		u1 then c.k = 3
	end 'x1'
end 'f'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(0))
	let c = Cnt.create(0)
	f(u, c: c)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-imm-param-union-iso-arm-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `imm-param-union-iso-arm-write` of redteamAE/p18.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Cnt
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'
end 'Cnt'

function f(u immutable Ub, c Cnt)
	match u 'x1'
		u0 then break
		u1 then c.k = 3
	end 'x1'
end 'f'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(0)) as isolated Ub
	let fz = freeze u
	let c = Cnt.create(0)
	f(fz, c: c)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-frozen-union-no-iso-ctl -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `frozen-union-no-iso-ctl` of redteamAE/p18.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Cnt
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'
end 'Cnt'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(0)) as isolated Ub
	let f = freeze u
	let c = Cnt.create(0)
	match f 'x1'
		u0 then break
		u1 then c.k = 3
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-frozen-union-iso-pass-to-fn-writes -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `frozen-union-iso-pass-to-fn-writes` of redteamAE/p19.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Cnt
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'
end 'Cnt'

function touch(b Bx, c Cnt)
	let _ = b
	c.k = 3
end 'touch'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(0)) as isolated Ub
	let f = freeze u
	let c = Cnt.create(0)
	match f 'x1'
		u0 then break
		u1(b) then touch(b, c: c)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-varpayload-nonlinear-iso-param -->
The model (`core-m3w-fa24`) refuses it by `C-LEND`; the compiler refuses it with E3198. Model program `varpayload-nonlinear-iso-param` of redteamAE/p8.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sh
	none
	held(r isolated Bk)
end 'Sh'

type Bk
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'
end 'Bk'

function giveH(r isolated Bk) returns Sh
	return Sh.held(r)
end 'giveH'

function useI(r isolated Bk)
	let _ = r.code
end 'useI'

function main() returns ExitCode
	let v = giveH(Bk.create(3))
	match v 'x1'
		none then break
		held(q) then useI(q)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:29:21: 'q' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-af-n20-two-sequential -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `n20-two-sequential` of redteamAF.hand_af17, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let ub = Ub.u1(Bx.create(0))
	match ub 'x1'
		u0 then break
		u1 then break
	end 'x1'
	match ub 'x2'
		u0 then break
		u1 then break
	end 'x2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-a7-match-then-call-same-union -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a7-match-then-call-same-union` of redteamAF.hand_af18, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function touch()
		self.n = 1
	end 'touch'
end 'Bx'

function peek(u Ub)
	match u 'x1'
		u0 then break
		u1 then break
	end 'x1'
end 'peek'

function main() returns ExitCode
	let ub = Ub.u1(Bx.create(1))
	match ub 'x1'
		u0 then break
		u1 then peek(ub)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-a9-method-call-inside-arm -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a9-method-call-inside-arm` of redteamAF.hand_af18, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Vb
	u0
	u1(b isolated Bx)
end 'Vb'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function touch()
		self.n = 1
	end 'touch'
end 'Bx'

function main() returns ExitCode
	let ub = Vb.u1(Bx.create(1))
	match ub 'x1'
		u0 then break
		u1(b) then b.touch()
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-a11-record-field-nested-call -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a11-record-field-nested-call` of redteamAF.hand_af18, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function touch()
		self.n = 1
	end 'touch'
end 'Bx'

type Ho
	var u as Ub
	var n as Num

	static function create(u Ub, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Ho'

function peek(uV Ub)
	match uV 'x1'
		u0 then break
		u1 then break
	end 'x1'
end 'peek'

function main() returns ExitCode
	let h = Ho.create(Ub.u1(Bx.create(1)), n: 0)
	match h.u 'x1'
		u0 then break
		u1 then peek(h.u)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-s5-binder-lent-named-like-outer -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s5-binder-lent-named-like-outer` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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
	let b = Bx.create(9) as isolated Bx
	let u = Ub.u1(Bx.create(1))
	match u 'x1'
		u0 then break
		u1(b) then let _ = b.n
	end 'x1'
	takeB(b)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-m3-copy-use-after-arm -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m3-copy-use-after-arm` of redteamAG.hand_ag10, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function eatUT(u UT)
	match u 'x1'
		none then break
		have(t) then t.n = 2
	end 'x1'
end 'eatUT'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have then let _ = u
	end 'x1'
	eatUT(u)
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:28:21: cannot move or copy the unique value matched by 'u' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```

<!-- test: model-hand-ag-m4-copy-use-after-arm-peek -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m4-copy-use-after-arm-peek` of redteamAG.hand_ag10, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function peekUT(u UT) returns Num
	match u 'x1'
		none then return 0
		have(t) then return t.n
	end 'x1'
end 'peekUT'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have then let _ = u
	end 'x1'
	let _ = peekUT(u)
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:28:21: cannot move or copy the unique value matched by 'u' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```

<!-- test: model-hand-ag-m8-copy-into-array -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m8-copy-into-array` of redteamAG.hand_ag10, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function peekUT(u UT) returns Num
	match u 'x1'
		none then return 0
		have(t) then return t.n
	end 'x1'
end 'peekUT'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let keep = UTArray.create(0)
	match u 'x1'
		none then break
		have then keep.push(u)
	end 'x1'
	let _ = peekUT(u)
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:30:23: cannot move or copy the unique value matched by 'u' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```

<!-- test: model-hand-ag-m11-copy-return-from-fn -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m11-copy-return-from-fn` of redteamAG.hand_ag10, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function eatUT(u UT)
	match u 'x1'
		none then break
		have(t) then t.n = 2
	end 'x1'
end 'eatUT'

function f(u UT) returns UT
	match u 'x1'
		none then return u
		have then return u
	end 'x1'
end 'f'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let r = f(u)
	eatUT(r)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-r20-refill-assign-as-lent-arm -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r20-refill-assign-as-lent-arm` of redteamAG.hand_ag4, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Vc
	u0
	u1(b isolated Bx)
end 'Vc'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	var a = Vc.u1(Bx.create(0))
	match a 'x1'
		u0 then break
		u1 then a = Vc.u0
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-b14-nested-alias-moved-away-inside -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `b14-nested-alias-moved-away-inside` of redteamAG.hand_ag5, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function eatUb(p Ub)
	match p 'x1'
		u0 then break
		u1 then break
	end 'x1'
end 'eatUb'

function main() returns ExitCode
	let ub = Ub.u1(Bx.create(0))
	let ua = ub
	match ub 'x1'
		u0 then break
		u1 then eatUb(ua)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-var-call-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-var-call-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function eatUT(x UT)
	match x 'x1'
		none then break
		have(t) then t.n = 2
	end 'x1'
end 'eatUT'

function main() returns ExitCode
	let _ = UTArray.create(0)
	let _ = UT.have(Tk.create(2))
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have then eatUT(u)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-var-assign-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-var-assign-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let _ = UTArray.create(0)
	let w = UT.have(Tk.create(2))
	var u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have then u = w
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-var-slot-assign-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-var-slot-assign-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let _ = UTArray.create(0)
	let _ = UT.have(Tk.create(2))
	var u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have then u = UT.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-var-nothing-rd -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-var-nothing-rd` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let _ = UTArray.create(0)
	let _ = UT.have(Tk.create(2))
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have(t) then let _ = t.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-var-nothing-wr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-var-nothing-wr` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let _ = UTArray.create(0)
	let _ = UT.have(Tk.create(2))
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have(t) then t.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-var-nothing-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-var-nothing-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let _ = UTArray.create(0)
	let _ = UT.have(Tk.create(2))
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-rec-call-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-rec-call-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HnArray = Array with Hn

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hn
	var u as UT
	var k as Num

	static function create(u UT, k Num) returns Self
		return Self{u: u, k: k}
	end 'create'
end 'Hn'

function eatHn(x Hn)
	let _ = x.k
end 'eatHn'

function main() returns ExitCode
	let _ = HnArray.create(0)
	let _ = Hn.create(UT.none, k: 1)
	let h = Hn.create(UT.have(Tk.create(1)), k: 0)
	match h.u 'x1'
		none then break
		have then eatHn(h)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-rec-assign-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-rec-assign-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HnArray = Array with Hn

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hn
	var u as UT
	var k as Num

	static function create(u UT, k Num) returns Self
		return Self{u: u, k: k}
	end 'create'
end 'Hn'

function main() returns ExitCode
	let _ = HnArray.create(0)
	let w = Hn.create(UT.none, k: 1)
	var h = Hn.create(UT.have(Tk.create(1)), k: 0)
	match h.u 'x1'
		none then break
		have then h = w
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-rec-slot-copy-no -->
The model (`core-m3w-fa24`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `f2-rec-slot-copy-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HnArray = Array with Hn

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hn
	var u as UT
	var k as Num

	static function create(u UT, k Num) returns Self
		return Self{u: u, k: k}
	end 'create'
end 'Hn'

function main() returns ExitCode
	let _ = HnArray.create(0)
	let _ = Hn.create(UT.none, k: 1)
	let h = Hn.create(UT.have(Tk.create(1)), k: 0)
	match h.u 'x1'
		none then break
		have then let _ = h.u
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:33:17: cannot bind '_' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: model-hand-ah-f2-rec-slot-assign-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-rec-slot-assign-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HnArray = Array with Hn

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hn
	var u as UT
	var k as Num

	static function create(u UT, k Num) returns Self
		return Self{u: u, k: k}
	end 'create'
end 'Hn'

function main() returns ExitCode
	let _ = HnArray.create(0)
	let _ = Hn.create(UT.none, k: 1)
	let h = Hn.create(UT.have(Tk.create(1)), k: 0)
	match h.u 'x1'
		none then break
		have then h.u = UT.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-rec-slot-call-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-rec-slot-call-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HnArray = Array with Hn

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hn
	var u as UT
	var k as Num

	static function create(u UT, k Num) returns Self
		return Self{u: u, k: k}
	end 'create'
end 'Hn'

function eatUT(x UT)
	match x 'x1'
		none then break
		have(t) then t.n = 2
	end 'x1'
end 'eatUT'

function main() returns ExitCode
	let _ = HnArray.create(0)
	let _ = Hn.create(UT.none, k: 1)
	let h = Hn.create(UT.have(Tk.create(1)), k: 0)
	match h.u 'x1'
		none then break
		have then eatUT(h.u)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-rec-nothing-rd -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-rec-nothing-rd` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HnArray = Array with Hn

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hn
	var u as UT
	var k as Num

	static function create(u UT, k Num) returns Self
		return Self{u: u, k: k}
	end 'create'
end 'Hn'

function main() returns ExitCode
	let _ = HnArray.create(0)
	let _ = Hn.create(UT.none, k: 1)
	let h = Hn.create(UT.have(Tk.create(1)), k: 0)
	match h.u 'x1'
		none then break
		have(t) then let _ = t.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-rec-nothing-wr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-rec-nothing-wr` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HnArray = Array with Hn

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hn
	var u as UT
	var k as Num

	static function create(u UT, k Num) returns Self
		return Self{u: u, k: k}
	end 'create'
end 'Hn'

function main() returns ExitCode
	let _ = HnArray.create(0)
	let _ = Hn.create(UT.none, k: 1)
	let h = Hn.create(UT.have(Tk.create(1)), k: 0)
	match h.u 'x1'
		none then break
		have(t) then t.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-rec-nothing-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-rec-nothing-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HnArray = Array with Hn

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hn
	var u as UT
	var k as Num

	static function create(u UT, k Num) returns Self
		return Self{u: u, k: k}
	end 'create'
end 'Hn'

function main() returns ExitCode
	let _ = HnArray.create(0)
	let _ = Hn.create(UT.none, k: 1)
	let h = Hn.create(UT.have(Tk.create(1)), k: 0)
	match h.u 'x1'
		none then break
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-urec-call-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-urec-call-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HdArray = Array with Hd

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

function eatHd(x Hd)
	match x.u 'x1'
		none then break
		have(t) then t.n = 3
	end 'x1'
end 'eatHd'

function main() returns ExitCode
	let _ = HdArray.create(0)
	let _ = Hd.create(UT.none)
	let h = Hd.create(UT.have(Tk.create(1)))
	match h.u 'x1'
		none then break
		have then eatHd(h)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-urec-assign-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-urec-assign-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HdArray = Array with Hd

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

function main() returns ExitCode
	let _ = HdArray.create(0)
	let w = Hd.create(UT.none)
	var h = Hd.create(UT.have(Tk.create(1)))
	match h.u 'x1'
		none then break
		have then h = w
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-urec-slot-copy-no -->
The model (`core-m3w-fa24`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `f2-urec-slot-copy-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HdArray = Array with Hd

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

function main() returns ExitCode
	let _ = HdArray.create(0)
	let _ = Hd.create(UT.none)
	let h = Hd.create(UT.have(Tk.create(1)))
	match h.u 'x1'
		none then break
		have then let _ = h.u
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:32:17: cannot bind '_' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: model-hand-ah-f2-urec-slot-assign-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-urec-slot-assign-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HdArray = Array with Hd

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

function main() returns ExitCode
	let _ = HdArray.create(0)
	let _ = Hd.create(UT.none)
	let h = Hd.create(UT.have(Tk.create(1)))
	match h.u 'x1'
		none then break
		have then h.u = UT.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-urec-slot-call-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-urec-slot-call-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HdArray = Array with Hd

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

function eatUT(x UT)
	match x 'x1'
		none then break
		have(t) then t.n = 2
	end 'x1'
end 'eatUT'

function main() returns ExitCode
	let _ = HdArray.create(0)
	let _ = Hd.create(UT.none)
	let h = Hd.create(UT.have(Tk.create(1)))
	match h.u 'x1'
		none then break
		have then eatUT(h.u)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-urec-nothing-rd -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-urec-nothing-rd` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HdArray = Array with Hd

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

function main() returns ExitCode
	let _ = HdArray.create(0)
	let _ = Hd.create(UT.none)
	let h = Hd.create(UT.have(Tk.create(1)))
	match h.u 'x1'
		none then break
		have(t) then let _ = t.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-urec-nothing-wr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-urec-nothing-wr` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HdArray = Array with Hd

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

function main() returns ExitCode
	let _ = HdArray.create(0)
	let _ = Hd.create(UT.none)
	let h = Hd.create(UT.have(Tk.create(1)))
	match h.u 'x1'
		none then break
		have(t) then t.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-urec-nothing-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-urec-nothing-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HdArray = Array with Hd

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

function main() returns ExitCode
	let _ = HdArray.create(0)
	let _ = Hd.create(UT.none)
	let h = Hd.create(UT.have(Tk.create(1)))
	match h.u 'x1'
		none then break
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-nest-call-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-nest-call-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function eatHx(x Hx)
	let _ = x
	let _ = 1 as Num
end 'eatHx'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let _ = Hx.create(UT.none, h: Hd.create(UT.none))
	let hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.h.u 'x1'
		none then break
		have then eatHx(hV)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-nest-assign-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-nest-assign-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let w = Hx.create(UT.none, h: Hd.create(UT.none))
	var hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.h.u 'x1'
		none then break
		have then hV = w
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-nest-slot-copy-no -->
The model (`core-m3w-fa24`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `f2-nest-slot-copy-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let _ = Hx.create(UT.none, h: Hd.create(UT.none))
	let hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.h.u 'x1'
		none then break
		have then let _ = hV.h.u
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:41:17: cannot bind '_' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: model-hand-ah-f2-nest-slot-assign-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-nest-slot-assign-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let _ = Hx.create(UT.none, h: Hd.create(UT.none))
	let hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.h.u 'x1'
		none then break
		have then hV.h.u = UT.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-nest-slot-call-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-nest-slot-call-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function eatUT(x UT)
	match x 'x1'
		none then break
		have(t) then t.n = 2
	end 'x1'
end 'eatUT'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let _ = Hx.create(UT.none, h: Hd.create(UT.none))
	let hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.h.u 'x1'
		none then break
		have then eatUT(hV.h.u)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-nest-nothing-rd -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-nest-nothing-rd` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let _ = Hx.create(UT.none, h: Hd.create(UT.none))
	let hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.h.u 'x1'
		none then break
		have(t) then let _ = t.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-nest-nothing-wr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-nest-nothing-wr` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let _ = Hx.create(UT.none, h: Hd.create(UT.none))
	let hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.h.u 'x1'
		none then break
		have(t) then t.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-nest-nothing-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-nest-nothing-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let _ = Hx.create(UT.none, h: Hd.create(UT.none))
	let hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.h.u 'x1'
		none then break
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-nest2-call-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-nest2-call-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function eatHx(x Hx)
	let _ = x
	let _ = 1 as Num
end 'eatHx'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let _ = Hx.create(UT.none, h: Hd.create(UT.none))
	let hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.u 'x1'
		none then break
		have then eatHx(hV)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-nest2-assign-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-nest2-assign-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let w = Hx.create(UT.none, h: Hd.create(UT.none))
	var hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.u 'x1'
		none then break
		have then hV = w
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-nest2-slot-copy-no -->
The model (`core-m3w-fa24`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `f2-nest2-slot-copy-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let _ = Hx.create(UT.none, h: Hd.create(UT.none))
	let hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.u 'x1'
		none then break
		have then let _ = hV.u
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:41:17: cannot bind '_' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: model-hand-ah-f2-nest2-slot-assign-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-nest2-slot-assign-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let _ = Hx.create(UT.none, h: Hd.create(UT.none))
	let hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.u 'x1'
		none then break
		have then hV.u = UT.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-nest2-slot-call-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-nest2-slot-call-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function eatUT(x UT)
	match x 'x1'
		none then break
		have(t) then t.n = 2
	end 'x1'
end 'eatUT'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let _ = Hx.create(UT.none, h: Hd.create(UT.none))
	let hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.u 'x1'
		none then break
		have then eatUT(hV.u)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-nest2-nothing-rd -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-nest2-nothing-rd` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let _ = Hx.create(UT.none, h: Hd.create(UT.none))
	let hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.u 'x1'
		none then break
		have(t) then let _ = t.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-nest2-nothing-wr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-nest2-nothing-wr` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let _ = Hx.create(UT.none, h: Hd.create(UT.none))
	let hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.u 'x1'
		none then break
		have(t) then t.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-nest2-nothing-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-nest2-nothing-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HxArray = Array with Hx

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

type Hx is unique
	var u as UT
	var h as Hd

	static function create(u UT, h Hd) returns Self
		return Self{u: u, h: h}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let _ = HxArray.create(0)
	let _ = Hx.create(UT.none, h: Hd.create(UT.none))
	let hV = Hx.create(UT.have(Tk.create(1)), h: Hd.create(UT.have(Tk.create(3))))
	match hV.u 'x1'
		none then break
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-param-call-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-param-call-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function eatUT(x UT)
	match x 'x1'
		none then break
		have(t) then t.n = 2
	end 'x1'
end 'eatUT'

function f(u UT)
	match u 'x1'
		none then break
		have then eatUT(u)
	end 'x1'
end 'f'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	f(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-fparam-call-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-fparam-call-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function eatUT(x UT)
	match x 'x1'
		none then break
		have(t) then t.n = 2
	end 'x1'
end 'eatUT'

function f(u UT)
	match u 'x1'
		none then break
		have then eatUT(u)
	end 'x1'
end 'f'

function main() returns ExitCode
	f(UT.have(Tk.create(1)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-param-nothing-rd -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-param-nothing-rd` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function f(u UT)
	match u 'x1'
		none then break
		have(t) then let _ = t.n
	end 'x1'
end 'f'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	f(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-fparam-nothing-rd -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-fparam-nothing-rd` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function f(u UT)
	match u 'x1'
		none then break
		have(t) then let _ = t.n
	end 'x1'
end 'f'

function main() returns ExitCode
	f(UT.have(Tk.create(1)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-param-nothing-wr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-param-nothing-wr` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function f(u UT)
	match u 'x1'
		none then break
		have(t) then t.n = 5
	end 'x1'
end 'f'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	f(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-fparam-nothing-wr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-fparam-nothing-wr` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function f(u UT)
	match u 'x1'
		none then break
		have(t) then t.n = 5
	end 'x1'
end 'f'

function main() returns ExitCode
	f(UT.have(Tk.create(1)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-param-nothing-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-param-nothing-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function f(u UT)
	match u 'x1'
		none then break
		have then break
	end 'x1'
end 'f'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	f(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-fparam-nothing-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-fparam-nothing-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function f(u UT)
	match u 'x1'
		none then break
		have then break
	end 'x1'
end 'f'

function main() returns ExitCode
	f(UT.have(Tk.create(1)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-consself-self-call-no -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3102, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f2-consself-self-call-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'

	function go(consuming self)
		match self.u 'x1'
			none then break
			have then eatHs(self)
		end 'x1'
	end 'go'
end 'Hs'

function eatHs(x Hs)
	let _ = x
	let _ = 1 as Num
end 'eatHs'

function main() returns ExitCode
	let h = Hs.create(UT.have(Tk.create(1)))
	h.go()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:24:11: `consuming self` belongs to a method of a `type` that declares a finalizer, and no method of 'Hs' contains `destruct self`
error E3102: <fragment>:39:2: use of moved value 'h': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ah-f2-consself-self-copy-no -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3102, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f2-consself-self-copy-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'

	function go(consuming self)
		match self.u 'x1'
			none then break
			have then let _ = self
		end 'x1'
	end 'go'
end 'Hs'

function main() returns ExitCode
	let h = Hs.create(UT.have(Tk.create(1)))
	h.go()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:24:11: `consuming self` belongs to a method of a `type` that declares a finalizer, and no method of 'Hs' contains `destruct self`
error E3102: <fragment>:34:2: use of moved value 'h': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ah-f2-consself-self-slot-no -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3102, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f2-consself-self-slot-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'

	function go(consuming self)
		match self.u 'x1'
			none then break
			have then let _ = self.u
		end 'x1'
	end 'go'
end 'Hs'

function main() returns ExitCode
	let h = Hs.create(UT.have(Tk.create(1)))
	h.go()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:24:11: `consuming self` belongs to a method of a `type` that declares a finalizer, and no method of 'Hs' contains `destruct self`
error E3102: <fragment>:34:2: use of moved value 'h': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ah-f2-consself-self-nothing-rd -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3102, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f2-consself-self-nothing-rd` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'

	function go(consuming self)
		match self.u 'x1'
			none then break
			have(t) then let _ = t.n
		end 'x1'
	end 'go'
end 'Hs'

function main() returns ExitCode
	let h = Hs.create(UT.have(Tk.create(1)))
	h.go()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:24:11: `consuming self` belongs to a method of a `type` that declares a finalizer, and no method of 'Hs' contains `destruct self`
error E3102: <fragment>:34:2: use of moved value 'h': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ah-f2-consself-self-nothing-wr -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3102, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f2-consself-self-nothing-wr` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'

	function go(consuming self)
		match self.u 'x1'
			none then break
			have(t) then t.n = 5
		end 'x1'
	end 'go'
end 'Hs'

function main() returns ExitCode
	let h = Hs.create(UT.have(Tk.create(1)))
	h.go()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:24:11: `consuming self` belongs to a method of a `type` that declares a finalizer, and no method of 'Hs' contains `destruct self`
error E3102: <fragment>:34:2: use of moved value 'h': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ah-f2-consself-self-nothing-no -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3102, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f2-consself-self-nothing-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'

	function go(consuming self)
		match self.u 'x1'
			none then break
			have then break
		end 'x1'
	end 'go'
end 'Hs'

function main() returns ExitCode
	let h = Hs.create(UT.have(Tk.create(1)))
	h.go()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:24:11: `consuming self` belongs to a method of a `type` that declares a finalizer, and no method of 'Hs' contains `destruct self`
error E3102: <fragment>:34:2: use of moved value 'h': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ah-f2-for-copy-no -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `f2-for-copy-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let a = UTArray.create(0)
	a.push(UT.have(Tk.create(1)))
	for e in a 'f1'
		match e 'x2'
			none then break
			have then let _ = a
		end 'x2'
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:22: use of moved value 'a': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ah-f2-for-call-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-for-call-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function eatArr(x UTArray)
	let _ = x
	let _ = 1 as Num
end 'eatArr'

function main() returns ExitCode
	let a = UTArray.create(0)
	a.push(UT.have(Tk.create(1)))
	for e in a 'f1'
		match e 'x2'
			none then break
			have then eatArr(a)
		end 'x2'
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-for-copy-elem-no -->
The model (`core-m3w-fa24`) refuses it by `C-LEND`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f2-for-copy-elem-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let a = UTArray.create(0)
	a.push(UT.have(Tk.create(1)))
	for e in a 'f1'
		match e 'x2'
			none then break
			have then let _ = e
		end 'x2'
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:24:18: cannot bind '_' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: model-hand-ah-f2-for-move-elem-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-for-move-elem-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function eatUT(x UT)
	match x 'x1'
		none then break
		have(t) then t.n = 2
	end 'x1'
end 'eatUT'

function main() returns ExitCode
	let a = UTArray.create(0)
	a.push(UT.have(Tk.create(1)))
	for e in a 'f1'
		match e 'x2'
			none then break
			have then eatUT(e)
		end 'x2'
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-for-nothing-rd -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-for-nothing-rd` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let a = UTArray.create(0)
	a.push(UT.have(Tk.create(1)))
	for e in a 'f1'
		match e 'x2'
			none then break
			have(t) then let _ = t.n
		end 'x2'
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-for-nothing-wr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-for-nothing-wr` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let a = UTArray.create(0)
	a.push(UT.have(Tk.create(1)))
	for e in a 'f1'
		match e 'x2'
			none then break
			have(t) then t.n = 5
		end 'x2'
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-for-nothing-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-for-nothing-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let a = UTArray.create(0)
	a.push(UT.have(Tk.create(1)))
	for e in a 'f1'
		match e 'x2'
			none then break
			have then break
		end 'x2'
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-fs-peek-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fs-peek-no` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function peekArr(x UTArray) returns Num
	let _ = x
	return 1
end 'peekArr'

function main() returns ExitCode
	let a = UTArray.create(0)
	a.push(UT.have(Tk.create(1)))
	for e in a 'f1'
		match e 'x2'
			none then break
			have then let _ = peekArr(a)
		end 'x2'
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-idc-copy-no -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `idc-copy-no` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function id(x UT) returns UT
	return x
end 'id'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let _ = UT.have(Tk.create(2))
	match id(u) 'x1'
		none then break
		have then let _ = u
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:26:21: use of moved value 'u': its ownership moved to the parameter 'x' of 'id', which keeps it
```

<!-- test: model-hand-ah-idc-call-no -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `idc-call-no` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function eatUT(x UT)
	match x 'x1'
		none then break
		have then break
	end 'x1'
end 'eatUT'

function id(x UT) returns UT
	return x
end 'id'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let _ = UT.have(Tk.create(2))
	match id(u) 'x1'
		none then break
		have then eatUT(u)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:33:19: use of moved value 'u': its ownership moved to the parameter 'x' of 'id', which keeps it
```

<!-- test: model-hand-ah-cond3-call-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `cond3-call-no` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function eatUT(x UT)
	match x 'x1'
		none then break
		have then break
	end 'x1'
end 'eatUT'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let w = UT.have(Tk.create(2))
	match (u if lg(1) != 0 else w) 'x1'
		none then break
		have then eatUT(u)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-alias-before-call-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `alias-before-call-no` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function eatUT(x UT)
	match x 'x1'
		none then break
		have then break
	end 'x1'
end 'eatUT'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let _ = UT.none
	match u 'x1'
		none then break
		have then eatUT(u)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-idc-copy-w-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `idc-copy-w-no` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function id(x UT) returns UT
	return x
end 'id'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let w = UT.have(Tk.create(2))
	match id(u) 'x1'
		none then break
		have then let _ = w
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-alias-before-copy-w-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `alias-before-copy-w-no` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let w = UT.none
	match u 'x1'
		none then break
		have then let _ = w
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-idc-nothing-rd -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `idc-nothing-rd` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function id(x UT) returns UT
	return x
end 'id'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let _ = UT.have(Tk.create(2))
	match id(u) 'x1'
		none then break
		have(t) then let _ = t.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-cond3-nothing-rd -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `cond3-nothing-rd` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let w = UT.have(Tk.create(2))
	match (u if lg(1) != 0 else w) 'x1'
		none then break
		have(t) then let _ = t.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-alias-before-nothing-rd -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `alias-before-nothing-rd` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let _ = UT.none
	match u 'x1'
		none then break
		have(t) then let _ = t.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-idc-nothing-wr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `idc-nothing-wr` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function id(x UT) returns UT
	return x
end 'id'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let _ = UT.have(Tk.create(2))
	match id(u) 'x1'
		none then break
		have(t) then t.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-cond3-nothing-wr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `cond3-nothing-wr` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let w = UT.have(Tk.create(2))
	match (u if lg(1) != 0 else w) 'x1'
		none then break
		have(t) then t.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-alias-before-nothing-wr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `alias-before-nothing-wr` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let _ = UT.none
	match u 'x1'
		none then break
		have(t) then t.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-idc-nothing-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `idc-nothing-no` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function id(x UT) returns UT
	return x
end 'id'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let _ = UT.have(Tk.create(2))
	match id(u) 'x1'
		none then break
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-cond3-nothing-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `cond3-nothing-no` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let w = UT.have(Tk.create(2))
	match (u if lg(1) != 0 else w) 'x1'
		none then break
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-alias-before-nothing-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `alias-before-nothing-no` of redteamAH.hand_ah7, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let _ = UT.none
	match u 'x1'
		none then break
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-a1-lend-with-payload -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a1-lend-with-payload` of redteamAI.hand_ai1, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function bothT(x UT, y Tk)
	y.n = 5
	match x 'x1'
		none then break
		have(z) then let _ = z.n
	end 'x1'
end 'bothT'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have(t) then bothT(u, y: t)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-a2-lend-with-payload-ro -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a2-lend-with-payload-ro` of redteamAI.hand_ai1, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function bothR(x UT, y Tk)
	match x 'x1'
		none then break
		have(z) then let _ = z.n
	end 'x1'
	let _ = y.n
end 'bothR'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have(t) then bothR(u, y: t)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-b12-two-recv -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `b12-two-recv` of redteamAI.hand_ai1, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT5
	none
	have(t Tk)
	function cmp(o UT5) returns Num
		let _ = o
		return 1
	end 'cmp'
end 'UT5'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let u = UT5.have(Tk.create(1))
	match u 'x1'
		none then break
		have then let _ = u.cmp(u)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:25:27: 'u' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: model-hand-ai-b14-lend-and-cond -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `b14-lend-and-cond` of redteamAI.hand_ai1, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function lg(x Num) returns Num
	return x
end 'lg'

function lend2(x UT, y UT)
	let _ = x
	let _ = y
	let _ = 1 as Num
end 'lend2'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have then lend2(u, y: (u if lg(1) != 0 else UT.none))
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:31:26: 'u' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: model-hand-ai-d2-root-writer-fn-nouse -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d2-root-writer-fn-nouse` of redteamAI.hand_ai1, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

function clearHd(x Hd)
	x.u = UT.none
end 'clearHd'

function main() returns ExitCode
	let h = Hd.create(UT.have(Tk.create(1)))
	match h.u 'x1'
		none then break
		have then clearHd(h)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-d7-root-writer-payload-pass -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3102, E3207. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `d7-root-writer-payload-pass` of redteamAI.hand_ai1, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

function clearHdT(x Hd, y Tk)
	x.u = UT.none
	let _ = y.n
end 'clearHdT'

function main() returns ExitCode
	let h = Hd.create(UT.have(Tk.create(1)))
	match h.u 'x1'
		none then break
		have(t) then clearHdT(h, y: t)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:34:16: cannot lend 'h' to parameter 'x' of 'clearHdT', which writes through it, in a call that also names a binding of the arm: the binding views the matched value, and a unique value has one name; write through 'h' in a statement that names no binding of the arm
error E3207: <fragment>:34:25: 'clearHdT' needs a writable parameter 'x' at line 26; 'h' is read-only while 'x1' is open
```

<!-- test: model-hand-ai-e8-lend-then-return-var -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `e8-lend-then-return-var` of redteamAI.hand_ai1, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function readUT(x UT)
	let _ = x
	let _ = 1 as Num
end 'readUT'

function f() returns UT
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have then readUT(u)
	end 'x1'
	return u
end 'f'

function main() returns ExitCode
	let _ = f()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-i2-for-plain-array-match -->
The model (`core-m3w-fa24`) refuses it by `C-LEND`; the compiler refuses it with E3198. Model program `i2-for-plain-array-match` of redteamAI.hand_ai4, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UbArray = Array with Ub

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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
	let a = UbArray.create(0)
	a.push(Ub.u1(Bx.create(1)))
	for e in a 'f1'
		match e 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:28:21: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-ai-l3-loop-continue-before-refill -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3198. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `l3-loop-continue-before-refill` of redteamAI.hand_ai5, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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
	var u = Ub.u1(Bx.create(5)) as isolated Ub
	var i = 2 as Num
	while i != 0 'w1'
		i = lg(0)
		match u 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
		if lg(1) != 0 'i3'
			continue
		end 'i3'
		u = Ub.u1(Bx.create(2))
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:32:21: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-ai-l4-loop-refill-conditional -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3198. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `l4-loop-refill-conditional` of redteamAI.hand_ai5, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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
	var u = Ub.u1(Bx.create(5)) as isolated Ub
	var i = 2 as Num
	while i != 0 'w1'
		match u 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
		if lg(1) != 0 'i3'
			u = Ub.u1(Bx.create(2))
		end 'i3'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:31:21: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-ai-l5-loop-arm-refill -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3198. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `l5-loop-arm-refill` of redteamAI.hand_ai5, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	var u = Ub.u1(Bx.create(5)) as isolated Ub
	var i = 2 as Num
	while i != 0 'w1'
		match u 'x2'
			u0 then break
			u1(o) then u = Ub.u1(o)
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:27:25: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-ai-l7-loop-while-cond-matches -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `l7-loop-while-cond-matches` of redteamAI.hand_ai5, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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

function cnt(u isolated Ub) returns Num
	match u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return 0
end 'cnt'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	var i = 2 as Num
	while cnt(u) != 0 'w1'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:36:12: use of moved value 'u': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ai-i1-both-arms-of-if -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `i1-both-arms-of-if` of redteamAI.hand_ai5, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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

function eatUb(u Ub)
	match u 'x1'
		u0 then break
		u1(b) then let _ = b.n
	end 'x1'
end 'eatUb'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'i1' else 'e3'
		match u 'x4'
			u0 then break
			u1(o) then takeB(o)
		end 'x4'
	end 'e3'
	eatUb(u)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:45:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ai-i2-both-arms-of-if-var-refill-one -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3198. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `i2-both-arms-of-if-var-refill-one` of redteamAI.hand_ai5, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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
	var u = Ub.u1(Bx.create(5)) as isolated Ub
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
		u = Ub.u0
	end 'i1' else 'e3'
		match u 'x4'
			u0 then break
			u1(o) then takeB(o)
		end 'x4'
	end 'e3'
	match u 'x5'
		u0 then break
		u1(o) then takeB(o)
	end 'x5'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:30:21: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-ai-i3-match-in-cond-of-if -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `i3-match-in-cond-of-if` of redteamAI.hand_ai5, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function eatUb(u Ub)
	match u 'x1'
		u0 then break
		u1(b) then let _ = b.n
	end 'x1'
end 'eatUb'

function cnt(u isolated Ub) returns Num
	match u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return 1
end 'cnt'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	if cnt(u) != 0 'i1'
		eatUb(u)
	end 'i1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:39:9: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-aj-union-discard -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `union-discard` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Kp
	var k as Tk

	static function create(k Tk) returns Self
		return Self{k: k}
	end 'create'
end 'Kp'

function f(t Tk, h Kp)
	let _ = h
	let u = t
	let _ = UT.have(u)
end 'f'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let t = Tk.create(1)
	f(t, h: h)
	let _ = t.n
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:35:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-union-literal-stored -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `union-literal-stored` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hd is unique
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Hd'

function f(t Tk, h Hd)
	let uV = t
	h.u = UT.have(uV)
end 'f'

function main() returns ExitCode
	let h = Hd.create(UT.none)
	let t = Tk.create(1)
	f(t, h: h)
	let _ = t.n
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:34:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-ovo-arm-lend -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ovo-arm-lend` of redteamAJ.hand_aj5, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function sc()
	var b = Bx.create(1)
	let u = Ub.u1(Bx.create(2))
	match u 'x1'
		u0 then break
		u1 then b = Bx.create(3)
	end 'x1'
	b.n = 2
end 'sc'

function main() returns ExitCode
	sc()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-al-ca-union-cond -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ca-union-cond` of redteamAL/hand_al3.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

function main() returns ExitCode
	let q = Tq.create(1, b: Bx.create(2))
	let _ = UQ.have((q if 1 != 0 else q))
	q.n = 6
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:29:2: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-an-c1-cond-call-arg -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `c1-cond-call-arg` of redteamAN.hand_an1, in capmodel `reggio/REPORTRTAN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function idUT(x UT) returns UT
	return x
end 'idUT'

function main() returns ExitCode
	var u = UT.have(Tk.create(1))
	let v = UT.none
	match idUT((u if 1 != 0 else v)) 'x1'
		none then break
		have then u = UT.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-an-f1-for-elems-nb-move-root -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f1-for-elems-nb-move-root` of redteamAN.hand_an1, in capmodel `reggio/REPORTRTAN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

union UT
	none
	have(t Tk)
end 'UT'

union En
	a
	b
end 'En'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hq is unique
	var u as UT
	var e as En
	var xs as TkArray
	var n as Num

	static function create(u UT, e En, xs TkArray, n Num) returns Self
		return Self{u: u, e: e, xs: xs, n: n}
	end 'create'
end 'Hq'

function eatHq(h Hq)
	let _ = h.n
end 'eatHq'

function main() returns ExitCode
	let h = Hq.create(UT.none, e: En.b, xs: TkArray.create(0), n: 0)
	h.xs.push(Tk.create(1))
	for eV in h.xs 'f1'
		match h.u 'x2'
			none then eatHq(h)
			have then break
		end 'x2'
		eV.n = 2
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-an-f1-for-elems-nb-byref-root -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `f1-for-elems-nb-byref-root` of redteamAN.hand_an1, in capmodel `reggio/REPORTRTAN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

union UT
	none
	have(t Tk)
end 'UT'

union En
	a
	b
end 'En'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hq is unique
	var u as UT
	var e as En
	var xs as TkArray
	var n as Num

	static function create(u UT, e En, xs TkArray, n Num) returns Self
		return Self{u: u, e: e, xs: xs, n: n}
	end 'create'
end 'Hq'

function clrXs(h Hq)
	h.xs = TkArray.create(0)
end 'clrXs'

function main() returns ExitCode
	let h = Hq.create(UT.none, e: En.b, xs: TkArray.create(0), n: 0)
	h.xs.push(Tk.create(1))
	for eV in h.xs 'f1'
		match h.u 'x2'
			none then clrXs(h)
			have then break
		end 'x2'
		eV.n = 2
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:43:20: 'clrXs' needs a writable parameter 'h' at line 35; 'h' is read-only while 'f1' is open
```

<!-- test: model-hand-an-m1-move-unique-loop -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m1-move-unique-loop` of redteamAN.hand_an1, in capmodel `reggio/REPORTRTAN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	var it = 2 as Num
	while it != 0 'w1'
		match u 'x2'
			none then break
			have then let _ = u
		end 'x2'
		it = 0
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:23:22: cannot move or copy the unique value matched by 'u' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```

<!-- test: model-hand-an-m1-nb-arm-wild-then-bind -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m1-nb-arm-wild-then-bind` of redteamAN.hand_an1, in capmodel `reggio/REPORTRTAN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	var u = UT.have(Tk.create(1))
	match u 'x1'
		none then u = UT.have(Tk.create(3))
		have(t) then t.n = 1
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-an-n2-nb-iso-root-to-isoparam -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `n2-nb-iso-root-to-isoparam` of redteamAN.hand_an1, in capmodel `reggio/REPORTRTAN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function eatUb(u Ub)
	match u 'x1'
		u0 then break
		u1(b) then let _ = b.n
	end 'x1'
end 'eatUb'

function takeUbI(u isolated Ub)
	eatUb(u)
end 'takeUbI'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(1))
	match u 'x1'
		u0 then break
		u1 then takeUbI(u)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:32:19: 'u' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-an-p1-call-plain-merge-keep -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-call-plain-merge-keep` of redteamAN.hand_an3, in capmodel `reggio/REPORTRTAN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(t Pb)
end 'UP'

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

type Wq2
	var p as Pb

	static function create(p Pb) returns Self
		return Self{p: p}
	end 'create'
end 'Wq2'

function mkUP() returns isolated UP
	return UP.have(Pb.create(1))
end 'mkUP'

function main() returns ExitCode
	let wo = Wq2.create(Pb.create(0))
	match mkUP() 'x1'
		none then break
		have(t) then wo.p = t
	end 'x1'
	wo.p.n = 2
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-an-p1-cond-plain-payload-keep-other-sent -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `p1-cond-plain-payload-keep-other-sent` of redteamAN.hand_an3, in capmodel `reggio/REPORTRTAN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(t Pb)
end 'UP'

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

type Wq2
	var p as Pb

	static function create(p Pb) returns Self
		return Self{p: p}
	end 'create'
end 'Wq2'

function lg(x Num) returns Num
	return x
end 'lg'

function mkUP() returns isolated UP
	return UP.have(Pb.create(1))
end 'mkUP'

function takeUPI(x isolated UP)
	let _ = x
	let _ = 1 as Num
end 'takeUPI'

function main() returns ExitCode
	let pV = mkUP() as isolated UP
	let q = mkUP() as isolated UP
	let wo = Wq2.create(Pb.create(0))
	match (pV if lg(1) != 0 else q) 'x1'
		none then break
		have(t) then wo.p = t
	end 'x1'
	takeUPI(q)
	wo.p.n = 2
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:46:10: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-an-p1-method-iso-result -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-method-iso-result` of redteamAN.hand_an3, in capmodel `reggio/REPORTRTAN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Mk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mk() returns isolated Ub
		return Ub.u1(Bx.create(self.n))
	end 'mk'
end 'Mk'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let m = Mk.create(1)
	match m.mk() 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ao-n3-for-cond-elem-match-cond-binding-two -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `n3-for-cond-elem-match-cond-binding-two` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hb is unique
	var xs as TkArray
	var u as UT

	static function create(xs TkArray, u UT) returns Self
		return Self{xs: xs, u: u}
	end 'create'
end 'Hb'

function two(a Tk, b Tk)
	a.n = 3
	b.n = 4
end 'two'

function main() returns ExitCode
	let h = Hb.create(TkArray.create(0), u: UT.have(Tk.create(5)))
	let g = Hb.create(TkArray.create(0), u: UT.none)
	h.xs.push(Tk.create(1))
	for e in (h if 1 != 0 else g).xs 'f1'
		match (h if 1 != 0 else g).u 'x2'
			none then break
			have(t) then two(e, b: t)
		end 'x2'
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:39:24: 'two' needs a writable parameter 'b' at line 29; 't' is read-only while 'f1' is open
```

<!-- test: model-hand-ao-n4-match-cond-binding-plus-holder-call -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3102, E3207. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `n4-match-cond-binding-plus-holder-call` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var u as UT
	var n as Num

	static function create(u UT, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Ht'

function two(a Tk, b Ht)
	b.u = UT.none
	a.n = 4
end 'two'

function main() returns ExitCode
	let h = Ht.create(UT.have(Tk.create(1)), n: 0)
	let g = Ht.create(UT.none, n: 0)
	match (h if 1 != 0 else g).u 'x1'
		none then break
		have(t) then two(t, b: h)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:36:16: cannot lend 'h' to parameter 'b' of 'two', which writes through it, in a call that also names a binding of the arm: the binding views the matched value, and a unique value has one name; write through 'h' in a statement that names no binding of the arm
error E3207: <fragment>:36:23: 'two' needs a writable parameter 'b' at line 27; 'h' is read-only while 'x1' is open
```

<!-- test: model-hand-ao-n5-match-cond-binding-plus-cond-holder-call -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `n5-match-cond-binding-plus-cond-holder-call` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var u as UT
	var n as Num

	static function create(u UT, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Ht'

function two(a Tk, b Ht)
	b.u = UT.none
	a.n = 4
end 'two'

function main() returns ExitCode
	let h = Ht.create(UT.have(Tk.create(1)), n: 0)
	let g = Ht.create(UT.none, n: 0)
	match (h if 1 != 0 else g).u 'x1'
		none then break
		have(t) then two(t, b: (h if 1 != 0 else g))
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:36:23: 'two' needs a writable parameter 'b' at line 27; '(h if 1 != 0 else g)' is read-only while 'x1' is open
```

<!-- test: model-hand-ao-p1b-pin-match-path-nb-plain-field-write-control -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1b-pin-match-path-nb-plain-field-write-control` of redteamAO/hand_ao5.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var u as UT
	var n as Num

	static function create(u UT, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Ht'

function main() returns ExitCode
	let h = Ht.create(UT.have(Tk.create(1)), n: 0)
	let _ = Ht.create(UT.none, n: 0)
	match h.u 'x1'
		none then break
		have then h.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ao-p1ib-pin-match-path-nb-plain-field-write-iso-union-control -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1ib-pin-match-path-nb-plain-field-write-iso-union-control` of redteamAO/hand_ao5.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hi is unique
	var u as Ub
	var n as Num

	static function create(u Ub, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hi'

function main() returns ExitCode
	let h = Hi.create(Ub.u1(Bx.create(1)), n: 0)
	let _ = Hi.create(Ub.u0, n: 0)
	match h.u 'x1'
		u0 then break
		u1 then h.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ao-p1p-pin-match-cond-path-nb-plain-field-write-plain-union -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1p-pin-match-cond-path-nb-plain-field-write-plain-union` of redteamAO/hand_ao5.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(n Num)
end 'UP'

type Hp is unique
	var u as UP
	var n as Num

	static function create(u UP, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hp'

function main() returns ExitCode
	let h = Hp.create(UP.have(1), n: 0)
	let g = Hp.create(UP.none, n: 0)
	match (h if 1 != 0 else g).u 'x1'
		none then break
		have then h.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ao-p1pb-pin-match-cond-path-bind-plain-field-write-plain-union -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1pb-pin-match-cond-path-bind-plain-field-write-plain-union` of redteamAO/hand_ao5.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(n Num)
end 'UP'

type Hp is unique
	var u as UP
	var n as Num

	static function create(u UP, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hp'

function main() returns ExitCode
	let h = Hp.create(UP.have(1), n: 0)
	let g = Hp.create(UP.none, n: 0)
	match (h if 1 != 0 else g).u 'x1'
		none then break
		have(x) then h.n = x
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ao-p2c-pin-nested-cond-deep-path-nb-control -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2c-pin-nested-cond-deep-path-nb-control` of redteamAO/hand_ao5.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hr is unique
	var v as UT
	var n as Num

	static function create(v UT, n Num) returns Self
		return Self{v: v, n: n}
	end 'create'
end 'Hr'

type Hu is unique
	var u as Hr
	var n as Num

	static function create(u Hr, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hu'

function main() returns ExitCode
	let k = Hu.create(Hr.create(UT.have(Tk.create(2)), n: 0), n: 0)
	match k.u.v 'x1'
		none then break
		have then k.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ao-q1-nb-cond-arm-read-field -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `q1-nb-cond-arm-read-field` of redteamAO/hand_ao5.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var u as UT
	var n as Num

	static function create(u UT, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Ht'

function main() returns ExitCode
	let h = Ht.create(UT.have(Tk.create(1)), n: 0)
	let g = Ht.create(UT.none, n: 0)
	var z = 0 as Num
	match (h if 1 != 0 else g).u 'x1'
		none then break
		have then z = h.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ao-q1b-nb-bare-arm-read-field -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `q1b-nb-bare-arm-read-field` of redteamAO/hand_ao5.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var u as UT
	var n as Num

	static function create(u UT, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Ht'

function main() returns ExitCode
	let h = Ht.create(UT.have(Tk.create(1)), n: 0)
	var z = 0 as Num
	match h.u 'x1'
		none then break
		have then z = h.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ao-q2-nb-cond-arm-reader-call -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `q2-nb-cond-arm-reader-call` of redteamAO/hand_ao5.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var u as UT
	var n as Num

	static function create(u UT, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Ht'

function rd(h Ht) returns Num
	return h.n
end 'rd'

function main() returns ExitCode
	let h = Ht.create(UT.have(Tk.create(1)), n: 0)
	let g = Ht.create(UT.none, n: 0)
	var z = 0 as Num
	match (h if 1 != 0 else g).u 'x1'
		none then break
		have then z = rd(h)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ao-q2b-nb-bare-arm-reader-call -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `q2b-nb-bare-arm-reader-call` of redteamAO/hand_ao5.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var u as UT
	var n as Num

	static function create(u UT, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Ht'

function rd(h Ht) returns Num
	return h.n
end 'rd'

function main() returns ExitCode
	let h = Ht.create(UT.have(Tk.create(1)), n: 0)
	var z = 0 as Num
	match h.u 'x1'
		none then break
		have then z = rd(h)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ao-q5-nb-cond-arm-stmt-open-iso-local -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `q5-nb-cond-arm-stmt-open-iso-local` of redteamAO/hand_ao5.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Ht is unique
	var u as UT
	var n as Num

	static function create(u UT, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Ht'

function main() returns ExitCode
	let h = Ht.create(UT.have(Tk.create(1)), n: 0)
	let g = Ht.create(UT.none, n: 0)
	let o = Bx.create(1) as isolated Bx
	match (h if 1 != 0 else g).u 'x1'
		none then break
		have then o.n = 2
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ao-q5b-nb-bare-arm-stmt-open-iso-local -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `q5b-nb-bare-arm-stmt-open-iso-local` of redteamAO/hand_ao5.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Ht is unique
	var u as UT
	var n as Num

	static function create(u UT, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Ht'

function main() returns ExitCode
	let h = Ht.create(UT.have(Tk.create(1)), n: 0)
	let o = Bx.create(1) as isolated Bx
	match h.u 'x1'
		none then break
		have then o.n = 2
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa14-l-two-args -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `l-two-args` of redteamFA14.hand_fa14e, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function lend2(x UT, y UT)
	let _ = x
	let _ = y
	let _ = 1 as Num
end 'lend2'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have then lend2(u, y: u)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:25: 'u' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: model-hand-fa14-m-return -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-return` of redteamFA14.hand_fa14e, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function f() returns UT
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have then return u
	end 'x1'
	return UT.none
end 'f'

function main() returns ExitCode
	let _ = f()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa14-m-reassign -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-reassign` of redteamFA14.hand_fa14e, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	var u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have then u = UT.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa14-m-cond -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m-cond` of redteamFA14.hand_fa14e, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have then let _ = (u if lg(1) != 0 else u)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:25:44: cannot move or copy the unique value matched by 'u' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```

<!-- test: model-hand-fa15-d1-mx-example -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d1-mx-example` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Mq
	q0(x Bx)
	q1(y isolated Bx)
end 'Mq'

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
	let rb = Bx.create(5) as isolated Bx
	let u = Mq.q1(rb) as isolated Mq
	match u 'x1'
		q0(xa) then let _ = xa.n
		q1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-d2-ub-takeb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d2-ub-takeB` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	match u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-d3-pc-plain -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d3-pc-plain` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Pc
	u0
	u1(b Bx)
end 'Pc'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let u = Pc.u1(Bx.create(1)) as isolated Pc
	match u 'x1'
		u0 then break
		u1(b) then b.n = 2
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-d4-unique -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d4-unique` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let u = UT.have(Tk.create(1)) as isolated UT
	match u 'x1'
		none then break
		have(t) then t.n = 2
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-d5-iso-unique -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d5-iso-unique` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ui
	none
	have(t isolated Tk)
end 'Ui'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function takeT(t isolated Tk)
	let _ = t.n
end 'takeT'

function main() returns ExitCode
	let u = Ui.have(Tk.create(1)) as isolated Ui
	match u 'x1'
		none then break
		have(t) then takeT(t)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-d6-var-payload -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d6-var-payload` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Vc
	u0
	u1(b isolated Bx)
end 'Vc'

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
	let u = Vc.u1(Bx.create(1)) as isolated Vc
	match u 'x1'
		u0 then break
		u1(b) then takeB(b)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-d7-isolated-param -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d7-isolated-param` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function f(u isolated Ub)
	match u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Ub.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-d11-call-result -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d11-call-result` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function giveUb(b isolated Bx) returns isolated Ub
	return Ub.u1(b)
end 'giveUb'

function main() returns ExitCode
	let u = giveUb(Bx.create(2))
	match u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-e1-match-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `e1-match-twice` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	match u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	match u 'x2'
		u0 then break
		u1(o) then takeB(o)
	end 'x2'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-e2-bind-after -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `e2-bind-after` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	match u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	let _ = u
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:10: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-e3-call-after -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `e3-call-after` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function eatUb(u Ub)
	match u 'x1'
		u0 then break
		u1(b) then let _ = b.n
	end 'x1'
end 'eatUb'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	match u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	eatUb(u)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:34:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-e4-take-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `e4-take-twice` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	match u 'x1'
		u0 then break
		u1(o) then takeTwo(o, b: o)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:26:25: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-e9-param-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `e9-param-twice` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function f(u isolated Ub)
	match u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	match u 'x2'
		u0 then break
		u1(o) then takeB(o)
	end 'x2'
end 'f'

function main() returns ExitCode
	f(Ub.u1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:26:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-e13-pc-later -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `e13-pc-later` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Pc
	u0
	u1(b Bx)
end 'Pc'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function eatPc(u Pc)
	match u 'x1'
		u0 then break
		u1(b) then b.n = 1
	end 'x1'
end 'eatPc'

function main() returns ExitCode
	let u = Pc.u1(Bx.create(1)) as isolated Pc
	match u 'x1'
		u0 then break
		u1(b) then b.n = 2
	end 'x1'
	eatPc(u)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-l2-slot -->
The model (`core-m3w-fa24`) refuses it by `C-LEND`; the compiler refuses it with E3198. Model program `l2-slot` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	match h.u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:35:20: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-fa15-l3-plain-var -->
The model (`core-m3w-fa24`) refuses it by `C-LEND`; the compiler refuses it with E3198. Model program `l3-plain-var` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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
	let u = Ub.u1(Bx.create(1))
	match u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:25:20: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-fa15-l4-plain-param -->
The model (`core-m3w-fa24`) refuses it by `C-LEND`; the compiler refuses it with E3198. Model program `l4-plain-param` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function f(u Ub)
	match u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Ub.u1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:24:20: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-fa15-l6-lend-ok -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `l6-lend-ok` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function eatUb(u Ub)
	match u 'x1'
		u0 then break
		u1(b) then let _ = b.n
	end 'x1'
end 'eatUb'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	match u 'x1'
		u0 then break
		u1(o) then let _ = o.n
	end 'x1'
	eatUb(u)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-l7-cond-subject -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l7-cond-subject` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	let w = Ub.u1(Bx.create(6)) as isolated Ub
	match (u if lg(1) != 0 else w) 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-n-loop-outer-no-refill -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `n-loop-outer-no-refill` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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
	let u = Ub.u1(Bx.create(1)) as isolated Ub
	var i = 2 as Num
	while i != 0 'w1'
		match u 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:29:9: use of moved value 'u': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-fa15-n-cond-subject -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `n-cond-subject` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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
	let u = Ub.u1(Bx.create(1)) as isolated Ub
	let w = Ub.u1(Bx.create(2)) as isolated Ub
	match (u if lg(1) != 0 else w) 'x1'
		u0 then break
		u1(o) then let _ = o.n
	end 'x1'
	match u 'x2'
		u0 then break
		u1(o) then takeB(o)
	end 'x2'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-n-if-then-read -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `n-if-then-read` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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

function eatUb(u Ub)
	match u 'x1'
		u0 then break
		u1(b) then let _ = b.n
	end 'x1'
end 'eatUb'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(1)) as isolated Ub
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'i1'
	eatUb(u)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:40:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-c5-method-param -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `c5-method-param` of redteamFA15.hand_fa15c, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Mt
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then takeB(o)
		end 'x1'
	end 'eat'

	function eatTwice(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then takeB(o)
		end 'x1'
		let _ = 1 as Num
	end 'eatTwice'
end 'Mt'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let m = Mt.create(0)
	m.eat(Ub.u1(Bx.create(1)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-c13-slot-union-lent -->
The model (`core-m3w-fa24`) refuses it by `C-LEND`; the compiler refuses it with E3198. Model program `c13-slot-union-lent` of redteamFA15.hand_fa15c, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u1(Bx.create(2))) as isolated Wm
	match h.u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:35:20: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-fa15-c15-for-element -->
The model (`core-m3w-fa24`) refuses it by `C-LEND`; the compiler refuses it with E3198. Model program `c15-for-element` of redteamFA15.hand_fa15c, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UbArray = Array with Ub

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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
	let us = UbArray.create(0)
	us.push(Ub.u1(Bx.create(1)))
	for u in us 'f1'
		match u 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:28:21: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-fa15-c25-ret-payload -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `c25-ret-payload` of redteamFA15.hand_fa15c, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function pull(u isolated Ub) returns isolated Bx
	match u 'x1'
		u0 then return Bx.create(0)
		u1(o) then return o
	end 'x1'
	return Bx.create(1)
end 'pull'

function main() returns ExitCode
	let b = pull(Ub.u1(Bx.create(1)))
	takeB(b)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-c26-ret-var -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `c26-ret-var` of redteamFA15.hand_fa15c, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function pull(u isolated Ub) returns isolated Ub
	match u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return u
end 'pull'

function main() returns ExitCode
	let _ = pull(Ub.u1(Bx.create(1)))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:26:9: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-c27-param-then-caller-read -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `c27-param-then-caller-read` of redteamFA15.hand_fa15c, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function f(u isolated Ub)
	match u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
end 'f'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	f(u)
	f(u)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:31:4: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-c31-isolated-from-plain-union -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `c31-isolated-from-plain-union` of redteamFA15.hand_fa15c, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Pc
	u0
	u1(b Bx)
end 'Pc'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let p = Pc.u1(Bx.create(5))
	let u = p as isolated Pc
	match u 'x1'
		u0 then break
		u1(o) then o.n = 1
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-r-literal -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `r-literal` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Kp
	var k as Tk

	static function create(k Tk) returns Self
		return Self{k: k}
	end 'create'
end 'Kp'

function readUT(x UT)
	let _ = x
	let _ = 1 as Num
end 'readUT'

function f(t Tk, h Kp)
	let _ = h
	let u = t
	let x = UT.have(u)
	readUT(x)
end 'f'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let t = Tk.create(1)
	f(t, h: h)
	let _ = t.n
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:41:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-fa17-w-beside-binding-lent -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `w-beside-binding-lent` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function wrUTt(x UT, y Tk)
	match x 'x1'
		none then break
		have(z) then z.n = 5
	end 'x1'
	let _ = y.n
end 'wrUTt'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have(z) then wrUTt(u, y: z)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:29:16: cannot lend 'u' to parameter 'x' of 'wrUTt', which writes through it, in a call that also names a binding of the arm: the binding views the matched value, and a unique value has one name; write through 'u' in a statement that names no binding of the arm
```

<!-- test: model-hand-fa17-w-iso-payload -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-iso-payload` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ui
	none
	have(t isolated Tk)
end 'Ui'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function wrUi2(x Ui)
	match x 'x1'
		none then break
		have then let _ = 1 as Num
	end 'x1'
end 'wrUi2'

function main() returns ExitCode
	let u = Ui.have(Tk.create(1))
	match u 'x1'
		none then break
		have then wrUi2(u)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-o-match-window -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `o-match-window` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	var o = Bx.create(1)
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have then o = Bx.create(2)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa18-m8-root-writer-replaces-nested-under-binding-view -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m8-root-writer-replaces-nested-under-binding-view` of redteamFA18.hand_fa18, in capmodel `reggio/REPORTFA18.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UJ
	none
	have(j Tj)
end 'UJ'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Tj is unique
	var n as Num
	var t as Tk

	static function create(n Num, t Tk) returns Self
		return Self{n: n, t: t}
	end 'create'
end 'Tj'

function rd(tV Tk, z Num) returns Num
	let _ = z
	return tV.n
end 'rd'

function repJ(u UJ) returns Num
	match u 'x1'
		none then break
		have(j) then j.t = Tk.create(9)
	end 'x1'
	return 1
end 'repJ'

function both(u UJ, j Tj)
	let _ = rd(j.t, z: repJ(u))
end 'both'

function main() returns ExitCode
	let u = UJ.have(Tj.create(1, t: Tk.create(1)))
	match u 'x1'
		none then break
		have(j) then both(u, j: j)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:47:16: cannot lend 'u' to parameter 'u' of 'both', which writes through it, in a call that also names a binding of the arm: the binding views the matched value, and a unique value has one name; write through 'u' in a statement that names no binding of the arm
```

<!-- test: model-hand-fa19-x-arm-stored-then-holder-isolated -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-arm-stored-then-holder-isolated` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

function takeIsoU(x isolated UQ)
	match x 'x1'
		none then break
		have(t) then t.b.n = 9
	end 'x1'
end 'takeIsoU'

function main() returns ExitCode
	let o = Ko.create(Bx.create(0), n: 0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then break
		have(t) then o.keep = t.b
	end 'x1'
	takeIsoU(u)
	o.keep.n = 7
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:49:11: 'u' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa19-x-arm-callee-local-holder-isolated -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-arm-callee-local-holder-isolated` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

function takeIsoU(x isolated UQ)
	match x 'x1'
		none then break
		have(t) then t.b.n = 9
	end 'x1'
end 'takeIsoU'

function stash(o Ko) returns UQ
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then break
		have(t) then o.keep = t.b
	end 'x1'
	return u
end 'stash'

function main() returns ExitCode
	let o = Ko.create(Bx.create(0), n: 0)
	takeIsoU(stash(o))
	o.keep.n = 7
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:53:11: 'stash(o)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa19-k-isoarm-outer-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-isoarm-outer-write` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let o = Bx.create(1)
	let ub = Ub.u1(Bx.create(1))
	match ub 'x1'
		u0 then break
		u1 then o.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-k-mixed-arm -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-mixed-arm` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UMx
	none
	both(t Tq, i isolated Bx)
end 'UMx'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

function main() returns ExitCode
	let o = Bx.create(1)
	let u = UMx.both(Tq.create(1, b: Bx.create(2)), i: Bx.create(3))
	match u 'x1'
		none then break
		both then o.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p5-for-root-writer -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `p5-for-root-writer` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

union UT
	none
	have(t Tk)
end 'UT'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'

	function mkK() returns Ko
		return Ko.create(self.b, n: 1)
	end 'mkK'

	function part() returns Bx
		return self.b
	end 'part'
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hq is unique
	var u as UT
	var xs as TqArray
	var n as Num

	static function create(u UT, xs TqArray, n Num) returns Self
		return Self{u: u, xs: xs, n: n}
	end 'create'

	function clr()
		self.u = UT.none
	end 'clr'
end 'Hq'

function wrH(h Hq)
	h.n = 5
end 'wrH'

function main() returns ExitCode
	let h = Hq.create(UT.none, xs: TqArray.create(0), n: 0)
	h.xs.push(Tq.create(1, b: Bx.create(2)))
	for t in h.xs 'f1'
		wrH(h)
		t.n = 3
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:74:7: 'wrH' needs a writable parameter 'h' at line 67; 'h' is read-only while 'f1' is open
```

<!-- test: model-hand-fa22-nb-none-local-direct -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-none-local-direct` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

function main() returns ExitCode
	var u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then u = UQ.none
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-have-local-direct -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-have-local-direct` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

function main() returns ExitCode
	var u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then break
		have then u = UQ.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-both-local-direct -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-both-local-direct` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

function main() returns ExitCode
	var u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then u = UQ.none
		have then u = UQ.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-none-local-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-none-local-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

function wrQ(x UQ)
	match x 'x1'
		none then break
		have(z) then z.n = 0
	end 'x1'
end 'wrQ'

function main() returns ExitCode
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then wrQ(u)
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-have-local-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-have-local-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

function wrQ(x UQ)
	match x 'x1'
		none then break
		have(z) then z.n = 0
	end 'x1'
end 'wrQ'

function main() returns ExitCode
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then break
		have then wrQ(u)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-both-local-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-both-local-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

function wrQ(x UQ)
	match x 'x1'
		none then break
		have(z) then z.n = 0
	end 'x1'
end 'wrQ'

function main() returns ExitCode
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then wrQ(u)
		have then wrQ(u)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-none-param-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-none-param-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

function wrQ(x UQ)
	match x 'x1'
		none then break
		have(z) then z.n = 0
	end 'x1'
end 'wrQ'

function f(u UQ)
	match u 'x1'
		none then wrQ(u)
		have then break
	end 'x1'
end 'f'

function main() returns ExitCode
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	f(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-have-param-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-have-param-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

function wrQ(x UQ)
	match x 'x1'
		none then break
		have(z) then z.n = 0
	end 'x1'
end 'wrQ'

function f(u UQ)
	match u 'x1'
		none then break
		have then wrQ(u)
	end 'x1'
end 'f'

function main() returns ExitCode
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	f(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-both-param-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-both-param-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

function wrQ(x UQ)
	match x 'x1'
		none then break
		have(z) then z.n = 0
	end 'x1'
end 'wrQ'

function f(u UQ)
	match u 'x1'
		none then wrQ(u)
		have then wrQ(u)
	end 'x1'
end 'f'

function main() returns ExitCode
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	f(u)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-none-field-direct -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-none-field-direct` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	match h.u 'x1'
		none then h.n = 5
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-have-field-direct -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-have-field-direct` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	match h.u 'x1'
		none then break
		have then h.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-both-field-direct -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-both-field-direct` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	match h.u 'x1'
		none then h.n = 5
		have then h.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-none-field-reassign -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-none-field-reassign` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	match h.u 'x1'
		none then h.u = UQ.none
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-have-field-reassign -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-have-field-reassign` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	match h.u 'x1'
		none then break
		have then h.u = UQ.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-both-field-reassign -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-both-field-reassign` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	match h.u 'x1'
		none then h.u = UQ.none
		have then h.u = UQ.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-none-field-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-none-field-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function setH(h Hq)
	h.n = 7
end 'setH'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	match h.u 'x1'
		none then setH(h)
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-have-field-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-have-field-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function setH(h Hq)
	h.n = 7
end 'setH'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	match h.u 'x1'
		none then break
		have then setH(h)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-both-field-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-both-field-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function setH(h Hq)
	h.n = 7
end 'setH'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	match h.u 'x1'
		none then setH(h)
		have then setH(h)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-none-field-reader-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-none-field-reader-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function wrH(h Hq)
	match h.u 'x1'
		none then break
		have(z) then z.n = 0
	end 'x1'
end 'wrH'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	match h.u 'x1'
		none then wrH(h)
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-have-field-reader-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-have-field-reader-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function wrH(h Hq)
	match h.u 'x1'
		none then break
		have(z) then z.n = 0
	end 'x1'
end 'wrH'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	match h.u 'x1'
		none then break
		have then wrH(h)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-both-field-reader-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-both-field-reader-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function wrH(h Hq)
	match h.u 'x1'
		none then break
		have(z) then z.n = 0
	end 'x1'
end 'wrH'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	match h.u 'x1'
		none then wrH(h)
		have then wrH(h)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-none-param-field-direct -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-none-param-field-direct` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function f(h Hq)
	match h.u 'x1'
		none then h.n = 5
		have then break
	end 'x1'
end 'f'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	f(h)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-have-param-field-direct -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-have-param-field-direct` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function f(h Hq)
	match h.u 'x1'
		none then break
		have then h.n = 5
	end 'x1'
end 'f'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	f(h)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-both-param-field-direct -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-both-param-field-direct` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function f(h Hq)
	match h.u 'x1'
		none then h.n = 5
		have then h.n = 5
	end 'x1'
end 'f'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	f(h)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-none-param-field-reassign -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-none-param-field-reassign` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function f(h Hq)
	match h.u 'x1'
		none then h.u = UQ.none
		have then break
	end 'x1'
end 'f'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	f(h)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-have-param-field-reassign -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-have-param-field-reassign` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function f(h Hq)
	match h.u 'x1'
		none then break
		have then h.u = UQ.none
	end 'x1'
end 'f'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	f(h)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-both-param-field-reassign -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-both-param-field-reassign` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function f(h Hq)
	match h.u 'x1'
		none then h.u = UQ.none
		have then h.u = UQ.none
	end 'x1'
end 'f'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	f(h)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-none-param-field-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-none-param-field-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function setH(h Hq)
	h.n = 7
end 'setH'

function f(h Hq)
	match h.u 'x1'
		none then setH(h)
		have then break
	end 'x1'
end 'f'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	f(h)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-have-param-field-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-have-param-field-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function setH(h Hq)
	h.n = 7
end 'setH'

function f(h Hq)
	match h.u 'x1'
		none then break
		have then setH(h)
	end 'x1'
end 'f'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	f(h)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-both-param-field-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-both-param-field-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

type Hq is unique
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hq'

function setH(h Hq)
	h.n = 7
end 'setH'

function f(h Hq)
	match h.u 'x1'
		none then setH(h)
		have then setH(h)
	end 'x1'
end 'f'

function main() returns ExitCode
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0)
	f(h)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-k-iso-arm-outer-write-binding -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-iso-arm-outer-write-binding` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UI
	none
	have(b isolated Bx)
end 'UI'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let o = Bx.create(1)
	let u = UI.have(Bx.create(1))
	match u 'x1'
		none then break
		have then o.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-k-iso-arm-holder-write-binding -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-iso-arm-holder-write-binding` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UI
	none
	have(b isolated Bx)
end 'UI'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hi is unique
	var u as UI
	var n as Num

	static function create(u UI, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hi'

function main() returns ExitCode
	let h = Hi.create(UI.have(Bx.create(1)), n: 0)
	match h.u 'x1'
		none then break
		have then h.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-k-iso-arm-reassign-nonbinding -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-iso-arm-reassign-nonbinding` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UI
	none
	have(b isolated Bx)
end 'UI'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	var u = UI.have(Bx.create(1))
	match u 'x1'
		none then break
		have then u = UI.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-k-iso-arm-none-reassign -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-iso-arm-none-reassign` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UI
	none
	have(b isolated Bx)
end 'UI'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	var u = UI.have(Bx.create(1))
	match u 'x1'
		none then u = UI.none
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-k-plain-union-nonbinding-reassign -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-plain-union-nonbinding-reassign` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(p Pp)
end 'UP'

type Pp
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pp'

function main() returns ExitCode
	var u = UP.have(Pp.create(1))
	match u 'x1'
		none then break
		have then u = UP.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-k-iso-arm-none-outer-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-iso-arm-none-outer-write` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UI
	none
	have(b isolated Bx)
end 'UI'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let o = Bx.create(1)
	let u = UI.have(Bx.create(1))
	match u 'x1'
		none then o.n = 5
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-n-after-arm -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `n-after-arm` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

function main() returns ExitCode
	var u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then break
		have(t) then t.n = 2
	end 'x1'
	u = UQ.none
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-n-nonbinding-then-binding-sibling -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `n-nonbinding-then-binding-sibling` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

function main() returns ExitCode
	var u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then u = UQ.none
		have(t) then t.n = 2
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-n-nonbinding-moves-root -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `n-nonbinding-moves-root` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

function clr(x UQ)
	match x 'x1'
		none then break
		have(z) then z.n = 0
	end 'x1'
end 'clr'

function main() returns ExitCode
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then break
		have then clr(u)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-n-nonbinding-copies-root-then-reads -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `n-nonbinding-copies-root-then-reads` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UQ
	none
	have(t Tq)
end 'UQ'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'
end 'Tq'

function main() returns ExitCode
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then break
		have then let _ = u
	end 'x1'
	let _ = u
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:30:21: cannot move or copy the unique value matched by 'u' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```

<!-- test: model-hand-fa23-p1-plain-local-reassign-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-plain-local-reassign-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(t Pb)
end 'UP'

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

function main() returns ExitCode
	var u = UP.have(Pb.create(1))
	match u 'x1'
		none then break
		have then u = UP.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-plain-local-reassign-nbnone -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-plain-local-reassign-nbnone` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(t Pb)
end 'UP'

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

function main() returns ExitCode
	var u = UP.have(Pb.create(1))
	match u 'x1'
		none then u = UP.none
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-plain-local-reassign-sibling -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-plain-local-reassign-sibling` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(t Pb)
end 'UP'

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

function main() returns ExitCode
	var u = UP.have(Pb.create(1))
	match u 'x1'
		none then u = UP.none
		have(t) then t.n = 2
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-plain-field-reassign-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-plain-field-reassign-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(t Pb)
end 'UP'

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

type Hp is unique
	var u as UP
	var n as Num

	static function create(u UP, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hp'

function main() returns ExitCode
	let h = Hp.create(UP.have(Pb.create(1)), n: 0)
	match h.u 'x1'
		none then break
		have then h.u = UP.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-plain-local-move-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-plain-local-move-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(t Pb)
end 'UP'

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

function main() returns ExitCode
	let u = UP.have(Pb.create(1))
	match u 'x1'
		none then break
		have then let _ = u
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-plain-local-move-nbnone -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-plain-local-move-nbnone` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(t Pb)
end 'UP'

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

function main() returns ExitCode
	let u = UP.have(Pb.create(1))
	match u 'x1'
		none then let _ = u
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-plain-local-move-sibling -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-plain-local-move-sibling` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(t Pb)
end 'UP'

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

function main() returns ExitCode
	let u = UP.have(Pb.create(1))
	match u 'x1'
		none then let _ = u
		have(t) then t.n = 2
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-plain-cond-move-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-plain-cond-move-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(t Pb)
end 'UP'

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

function main() returns ExitCode
	let u = UP.have(Pb.create(1))
	let v = UP.none
	match (u if 1 != 0 else v) 'x1'
		none then break
		have then let _ = u
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-plain-field-move-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-plain-field-move-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(t Pb)
end 'UP'

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

type Hp is unique
	var u as UP
	var n as Num

	static function create(u UP, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hp'

function main() returns ExitCode
	let h = Hp.create(UP.have(Pb.create(1)), n: 0)
	match h.u 'x1'
		none then break
		have then h.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-iso-local-reassign-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-iso-local-reassign-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	var u = Ub.u1(Bx.create(1))
	match u 'x1'
		u0 then break
		u1 then u = Ub.u0
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-iso-local-reassign-nbnone -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-iso-local-reassign-nbnone` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	var u = Ub.u1(Bx.create(1))
	match u 'x1'
		u0 then u = Ub.u0
		u1 then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-iso-local-reassign-sibling -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-iso-local-reassign-sibling` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	var u = Ub.u1(Bx.create(1))
	match u 'x1'
		u0 then u = Ub.u0
		u1(t) then let _ = t.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-iso-field-reassign-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-iso-field-reassign-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hb2 is unique
	var u as Ub
	var n as Num

	static function create(u Ub, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hb2'

function main() returns ExitCode
	let h = Hb2.create(Ub.u1(Bx.create(1)), n: 0)
	match h.u 'x1'
		u0 then break
		u1 then h.u = Ub.u0
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-iso-local-move-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-iso-local-move-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(1))
	match u 'x1'
		u0 then break
		u1 then let _ = u
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-iso-local-move-nbnone -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-iso-local-move-nbnone` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(1))
	match u 'x1'
		u0 then let _ = u
		u1 then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-iso-local-move-sibling -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-iso-local-move-sibling` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(1))
	match u 'x1'
		u0 then let _ = u
		u1(t) then let _ = t.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-iso-cond-move-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-iso-cond-move-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let u = Ub.u1(Bx.create(1))
	let v = Ub.u0
	match (u if 1 != 0 else v) 'x1'
		u0 then break
		u1 then let _ = u
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-iso-field-move-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-iso-field-move-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hb2 is unique
	var u as Ub
	var n as Num

	static function create(u Ub, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hb2'

function main() returns ExitCode
	let h = Hb2.create(Ub.u1(Bx.create(1)), n: 0)
	match h.u 'x1'
		u0 then break
		u1 then h.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-unique-local-reassign-nbnone -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-unique-local-reassign-nbnone` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	var u = UT.have(Tk.create(1))
	match u 'x1'
		none then u = UT.none
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-unique-local-reassign-sibling -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-unique-local-reassign-sibling` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	var u = UT.have(Tk.create(1))
	match u 'x1'
		none then u = UT.none
		have(t) then t.n = 2
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-unique-field-reassign-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-unique-field-reassign-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var u as UT
	var n as Num

	static function create(u UT, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Ht'

function main() returns ExitCode
	let h = Ht.create(UT.have(Tk.create(1)), n: 0)
	match h.u 'x1'
		none then break
		have then h.u = UT.none
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-unique-field-move-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-unique-field-move-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var u as UT
	var n as Num

	static function create(u UT, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Ht'

function main() returns ExitCode
	let h = Ht.create(UT.have(Tk.create(1)), n: 0)
	match h.u 'x1'
		none then break
		have then h.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-none-local-reassign-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-none-local-reassign-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
union En
	a
	b
end 'En'

function main() returns ExitCode
	var u = En.b
	match u 'x1'
		a then break
		b then u = En.a
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-none-local-reassign-nbnone -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-none-local-reassign-nbnone` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
union En
	a
	b
end 'En'

function main() returns ExitCode
	var u = En.b
	match u 'x1'
		a then u = En.a
		b then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-none-field-reassign-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-none-field-reassign-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union En
	a
	b
end 'En'

type He is unique
	var u as En
	var n as Num

	static function create(u En, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'He'

function main() returns ExitCode
	let h = He.create(En.b, n: 0)
	match h.u 'x1'
		a then break
		b then h.u = En.a
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-none-local-move-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-none-local-move-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
union En
	a
	b
end 'En'

function main() returns ExitCode
	let u = En.b
	match u 'x1'
		a then break
		b then let _ = u
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-none-local-move-nbnone -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-none-local-move-nbnone` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
union En
	a
	b
end 'En'

function main() returns ExitCode
	let u = En.b
	match u 'x1'
		a then let _ = u
		b then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-none-cond-move-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-none-cond-move-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
union En
	a
	b
end 'En'

function main() returns ExitCode
	let u = En.b
	let v = En.a
	match (u if 1 != 0 else v) 'x1'
		a then break
		b then let _ = u
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-none-field-move-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-none-field-move-nb` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union En
	a
	b
end 'En'

type He is unique
	var u as En
	var n as Num

	static function create(u En, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'He'

function main() returns ExitCode
	let h = He.create(En.b, n: 0)
	match h.u 'x1'
		a then break
		b then h.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p1-move-then-read -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `p1-move-then-read` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have then let _ = u
	end 'x1'
	let _ = u
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:21:21: cannot move or copy the unique value matched by 'u' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```

<!-- test: model-hand-fa23-p1-in-for -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `p1-in-for` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

union UP
	none
	have(t Pb)
end 'UP'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

type Hx is unique
	var u as UP
	var xs as TkArray

	static function create(u UP, xs TkArray) returns Self
		return Self{u: u, xs: xs}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let h = Hx.create(UP.none, xs: TkArray.create(0))
	h.xs.push(Tk.create(1))
	for e in h.xs 'f1'
		match h.u 'x2'
			none then h.xs = TkArray.create(0)
			have then h.xs = TkArray.create(0)
		end 'x2'
		e.n = 2
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:40:14: cannot write 'xs' through 'h', which is read-only while 'f1' drains it
```

<!-- test: model-hand-fa23-p2-call -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-call` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function mkUb() returns isolated Ub
	return Ub.u1(Bx.create(1))
end 'mkUb'

function main() returns ExitCode
	match mkUb() 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p2-swap-field -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-swap-field` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Wu
	var u as isolated Ub
	var n as Num

	static function create(u isolated Ub, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Wu'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function mkUb() returns isolated Ub
	return Ub.u1(Bx.create(1))
end 'mkUb'

function main() returns ExitCode
	let w = Wu.create(mkUb(), n: 0)
	match swap w.u with Ub.u0 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	w.n = 2
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p2-cond-vars -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-cond-vars` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function mkUb() returns isolated Ub
	return Ub.u1(Bx.create(1))
end 'mkUb'

function main() returns ExitCode
	let p = mkUb() as isolated Ub
	let q = mkUb() as isolated Ub
	match (p if 1 != 0 else q) 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p2-cond-var-call -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-cond-var-call` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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

function mkUb() returns isolated Ub
	return Ub.u1(Bx.create(1))
end 'mkUb'

function main() returns ExitCode
	let p = mkUb() as isolated Ub
	match (p if lg(1) != 0 else mkUb()) 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p2-cond-moves-then -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `p2-cond-moves-then` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function eatUb(u Ub)
	match u 'x1'
		u0 then break
		u1(b) then let _ = b.n
	end 'x1'
end 'eatUb'

function mkUb() returns isolated Ub
	return Ub.u1(Bx.create(1))
end 'mkUb'

function takeUbI(u isolated Ub)
	eatUb(u)
end 'takeUbI'

function main() returns ExitCode
	let p = mkUb() as isolated Ub
	let q = mkUb() as isolated Ub
	match (p if 1 != 0 else q) 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	takeUbI(q)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:43:10: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa23-p2-cond-moves-in-arm -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `p2-cond-moves-in-arm` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function eatUb(u Ub)
	match u 'x1'
		u0 then break
		u1(b) then let _ = b.n
	end 'x1'
end 'eatUb'

function mkUb() returns isolated Ub
	return Ub.u1(Bx.create(1))
end 'mkUb'

function takeUbI(u isolated Ub)
	eatUb(u)
end 'takeUbI'

function main() returns ExitCode
	let p = mkUb() as isolated Ub
	let q = mkUb() as isolated Ub
	match (p if 1 != 0 else q) 'x1'
		u0 then takeUbI(p)
		u1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:40:19: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa23-p2-cond-slot-arm -->
The model (`core-m3w-fa24`) refuses it by `C-ISOREAD`; the compiler refuses it with E3198. Model program `p2-cond-slot-arm` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Wu
	var u as isolated Ub
	var n as Num

	static function create(u isolated Ub, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Wu'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function mkUb() returns isolated Ub
	return Ub.u1(Bx.create(1))
end 'mkUb'

function main() returns ExitCode
	let w = Wu.create(mkUb(), n: 0)
	let p = mkUb() as isolated Ub
	match (p if 1 != 0 else w.u) 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:39:20: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-fa23-p2-call-read-subject-again -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-call-read-subject-again` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function mkUb() returns isolated Ub
	return Ub.u1(Bx.create(1))
end 'mkUb'

function main() returns ExitCode
	match mkUb() 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	match mkUb() 'x2'
		u0 then break
		u1(o) then takeB(o)
	end 'x2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p2-slot-lends -->
The model (`core-m3w-fa24`) refuses it by `C-LEND`; the compiler refuses it with E3198. Model program `p2-slot-lends` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Wu
	var u as isolated Ub
	var n as Num

	static function create(u isolated Ub, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Wu'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function mkUb() returns isolated Ub
	return Ub.u1(Bx.create(1))
end 'mkUb'

function main() returns ExitCode
	let w = Wu.create(mkUb(), n: 0)
	match w.u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:38:20: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-fa23-p2-slot-lends-read -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-slot-lends-read` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Wu
	var u as isolated Ub
	var n as Num

	static function create(u isolated Ub, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Wu'

function mkUb() returns isolated Ub
	return Ub.u1(Bx.create(1))
end 'mkUb'

function main() returns ExitCode
	let w = Wu.create(mkUb(), n: 0)
	match w.u 'x1'
		u0 then break
		u1(o) then let _ = o.n
	end 'x1'
	w.n = 1
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p2-fresh-plain-lends -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p2-fresh-plain-lends` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(t Pb)
end 'UP'

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

function takePI(p isolated Pb)
	let _ = p.n
end 'takePI'

function main() returns ExitCode
	match UP.have(Pb.create(1)) 'x1'
		none then break
		have(t) then takePI(t)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:24:23: 't' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa23-p2-fresh-plain-read -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-fresh-plain-read` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(t Pb)
end 'UP'

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

function main() returns ExitCode
	match UP.have(Pb.create(1)) 'x1'
		none then break
		have(t) then t.n = 3
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p2-call-in-modify-of-arg -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-call-in-modify-of-arg` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function wrapB(b isolated Bx) returns isolated Ub
	return Ub.u1(b)
end 'wrapB'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	match wrapB(bx) 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa24-a5-match-cond-path-plain-nb-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a5-match-cond-path-plain-nb-write` of redteamFA24/hand_fa24.txt, in capmodel `reggio/REPORTFA24.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UP
	none
	have(n Num)
end 'UP'

type Hp is unique
	var u as UP
	var n as Num

	static function create(u UP, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hp'

function main() returns ExitCode
	let h = Hp.create(UP.have(1), n: 0)
	let g = Hp.create(UP.none, n: 0)
	match (h if 1 != 0 else g).u 'x1'
		none then break
		have then h.n = 4
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa24-a6-match-cond-path-binding-read -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a6-match-cond-path-binding-read` of redteamFA24/hand_fa24.txt, in capmodel `reggio/REPORTFA24.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var u as UT
	var n as Num

	static function create(u UT, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Ht'

function main() returns ExitCode
	let h = Ht.create(UT.have(Tk.create(1)), n: 0)
	let g = Ht.create(UT.none, n: 0)
	match (h if 1 != 0 else g).u 'x1'
		none then break
		have(t) then t.n = 3
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa24-d3b-match-path-mixed-union-bare-unique-binding-arm-field-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d3b-match-path-mixed-union-bare-unique-binding-arm-field-write` of redteamFA24/hand_fa24.txt, in capmodel `reggio/REPORTFA24.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UM
	none
	pl(n Num)
	uq(t Tk)
end 'UM'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hm is unique
	var u as UM
	var n as Num

	static function create(u UM, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hm'

function main() returns ExitCode
	let h = Hm.create(UM.pl(1), n: 0)
	match h.u 'x1'
		none then break
		pl then break
		uq then h.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa24-d3c-match-path-mixed-union-bare-plain-arm-field-write-control -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d3c-match-path-mixed-union-bare-plain-arm-field-write-control` of redteamFA24/hand_fa24.txt, in capmodel `reggio/REPORTFA24.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UM
	none
	pl(n Num)
	uq(t Tk)
end 'UM'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hm is unique
	var u as UM
	var n as Num

	static function create(u UM, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Hm'

function main() returns ExitCode
	let h = Hm.create(UM.pl(1), n: 0)
	match h.u 'x1'
		none then break
		pl then h.n = 5
		uq then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```
