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
the day the compiler stops agreeing with a verdict the model pinned. The rule set is `core-m3w-fa14`.

## Tests

<!-- test: model-finding-ae-arm-outer-write-nonlinear-ctl -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `arm-outer-write-nonlinear-ctl` of redteamAE/p10.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
```maxoncstderr
error E3197: <fragment>:30:13: cannot write 'n' through 'b', which is read-only while 'x1' is open
```

<!-- test: model-finding-ae-frozen-union-iso-arm-write -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `frozen-union-iso-arm-write` of redteamAE/p16.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `param-union-iso-arm-write` of redteamAE/p16.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
```maxoncstderr
error E3197: <fragment>:28:11: cannot write 'k' through 'c', which is read-only while 'x1' is open
```

<!-- test: model-finding-ae-imm-param-union-iso-arm-write -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `imm-param-union-iso-arm-write` of redteamAE/p18.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `frozen-union-no-iso-ctl` of redteamAE/p18.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `frozen-union-iso-pass-to-fn-writes` of redteamAE/p19.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-LEND`; the compiler refuses it with E3198. Model program `varpayload-nonlinear-iso-param` of redteamAE/p8.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `n20-two-sequential` of redteamAF.hand_af17, in capmodel `reggio/REPORTRTAF.txt`.

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

<!-- test: model-hand-af-a9-method-call-inside-arm -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a9-method-call-inside-arm` of redteamAF.hand_af18, in capmodel `reggio/REPORTRTAF.txt`.

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

<!-- test: model-hand-af-s5-binder-lent-named-like-outer -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s5-binder-lent-named-like-outer` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-SLOT`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m3-copy-use-after-arm` of redteamAG.hand_ag10, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-SLOT`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m4-copy-use-after-arm-peek` of redteamAG.hand_ag10, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m8-copy-into-array` of redteamAG.hand_ag10, in capmodel `reggio/REPORTRTAG.txt`.

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

<!-- test: model-hand-ag-f2-control-copy -->
The model (`core-m3w-fa14`) refuses it by `C-SLOT`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f2-control-copy` of redteamAG.hand_ag16, in capmodel `reggio/REPORTRTAG.txt`.

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
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:21:21: cannot move or copy the unique value matched by 'u' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```
