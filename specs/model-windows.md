---
feature: model-windows
status: experimental
keywords: [model, capmodel, region, capability]
category: semantics
---

# Windows over isolated regions

## Documentation

These cases are programs of the capmodel executable model, translated to Maxon by
`scripts/model-specs/generate.py` (`docs/SPECS.md`, "Model-derived specs") and pinned to the model's verdict on
`modify` windows and one-statement opens. A program the model accepts runs and exits 0; one it refuses fails to compile, with the diagnostic
the compiler gives. A case is written only where the compiler and the model agree, so the suite fails
the day the compiler stops agreeing with a verdict the model pinned. The rule set is `core-m3w-fa24`.

## Tests

<!-- test: model-finding-q-q2-ring3-the-noro-mark-is-not-part-of-sig-snap-so-a-ring-of-three-neve -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `Q2-ring3: the noro mark is not part of Sig.snap, so a ring of three never learns it` of redteamQ/holes.txt, in capmodel `reggio/REPORTRTQ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bytes
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bytes'

type Reader
	var bytes as Bytes
	var pos as Num

	static function create(bytes Bytes, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Reader'

function f0(bV Bytes, j Num) returns Num
	return f1(bV, j: j)
end 'f0'

function f1(bV Bytes, j Num) returns Num
	return f2(bV, j: j)
end 'f1'

function f2(bV Bytes, j Num) returns Num
	let _ = Reader.create(bV, pos: 0)
	if j != 0 'i1'
		return f0(bV, j: 0)
	end 'i1' else 'e2'
		return 1
	end 'e2'
end 'f2'

function main() returns ExitCode
	let o = Bytes.create(7)
	let ib = Reader.create(Bytes.create(0), pos: 0) as isolated Reader
	modify ib 'm1'
		let _ = f0(o, j: 0)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:42:14: 'f0' builds a record that holds 'bV', so it takes no read-only data; 'o' is read-only
```

<!-- test: model-finding-q-q2-ring3-ctl-ring2-control-the-ring-of-two-refused -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `Q2-ring3-ctl-ring2: control, the ring of two (refused)` of redteamQ/holes.txt, in capmodel `reggio/REPORTRTQ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bytes
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bytes'

type Reader
	var bytes as Bytes
	var pos as Num

	static function create(bytes Bytes, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Reader'

function f0(bV Bytes, j Num) returns Num
	return f1(bV, j: j)
end 'f0'

function f1(bV Bytes, j Num) returns Num
	let _ = Reader.create(bV, pos: 0)
	if j != 0 'i1'
		return f0(bV, j: 0)
	end 'i1' else 'e2'
		return 1
	end 'e2'
end 'f1'

function main() returns ExitCode
	let o = Bytes.create(7)
	let ib = Reader.create(Bytes.create(0), pos: 0) as isolated Reader
	modify ib 'm1'
		let _ = f0(o, j: 0)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:38:14: 'f0' builds a record that holds 'bV', so it takes no read-only data; 'o' is read-only
```

<!-- test: model-finding-q-q2-line-ctl-control-a-line-of-three-with-no-back-edge-refused -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `Q2-line-ctl: control, a line of three with no back edge (refused)` of redteamQ/holes.txt, in capmodel `reggio/REPORTRTQ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bytes
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bytes'

type Reader
	var bytes as Bytes
	var pos as Num

	static function create(bytes Bytes, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Reader'

function f0(bV Bytes, j Num) returns Num
	return f1(bV, j: j)
end 'f0'

function f1(bV Bytes, j Num) returns Num
	return f2(bV, j: j)
end 'f1'

function f2(bV Bytes, j Num) returns Num
	let _ = j
	let _ = Reader.create(bV, pos: 0)
	return 1
end 'f2'

function main() returns ExitCode
	let o = Bytes.create(7)
	let ib = Reader.create(Bytes.create(0), pos: 0) as isolated Reader
	modify ib 'm1'
		let _ = f0(o, j: 0)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:39:14: 'f0' builds a record that holds 'bV', so it takes no read-only data; 'o' is read-only
```

<!-- test: model-finding-r-r6-unique-lent-builder-named-an-outer-unique-token-lent-in-a-window-to -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `R6-unique-lent-builder-named: an outer unique token lent in a window to a named builder over its Bytes` of redteamR/holes6.txt, in capmodel `reggio/REPORTRTR.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bytes
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bytes'

type Reader
	var bytes as Bytes
	var pos as Num

	static function create(bytes Bytes, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Reader'

type Tk2 is unique
	var bytes as Bytes
	var n as Num

	static function create(bytes Bytes, n Num) returns Self
		return Self{bytes: bytes, n: n}
	end 'create'
end 'Tk2'

function mkr(t Tk2) returns Num
	let _ = Reader.create(t.bytes, pos: 0)
	return 0
end 'mkr'

function main() returns ExitCode
	let t = Tk2.create(Bytes.create(1), n: 0)
	let ib = Reader.create(Bytes.create(0), pos: 0) as isolated Reader
	modify ib 'm1'
		let _ = mkr(t)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:39:15: 'mkr' builds a record that holds 't', so it takes no read-only data; 't' is read-only
```

<!-- test: model-finding-r-r6-unique-lent-builder-method-the-same-through-a-method-of-the-token -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `R6-unique-lent-builder-method: the same through a method of the token` of redteamR/holes6.txt, in capmodel `reggio/REPORTRTR.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bytes
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bytes'

type Reader
	var bytes as Bytes
	var pos as Num

	static function create(bytes Bytes, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Reader'

type Tk2 is unique
	var bytes as Bytes
	var n as Num

	static function create(bytes Bytes, n Num) returns Self
		return Self{bytes: bytes, n: n}
	end 'create'

	function mk() returns Num
		let _ = Reader.create(self.bytes, pos: 0)
		return 0
	end 'mk'
end 'Tk2'

function main() returns ExitCode
	let t = Tk2.create(Bytes.create(1), n: 0)
	let ib = Reader.create(Bytes.create(0), pos: 0) as isolated Reader
	modify ib 'm1'
		let _ = t.mk()
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:39:11: 'mk' builds a record that holds its receiver, so it takes no read-only data; 't' is read-only
```

<!-- test: model-finding-s-u4-unique-window-made-into-holder-a-window-made-token-moved-into-a-hol -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `U4-unique-window-made-into-holder: a window-made token moved into a holder inside the window` of redteamS/h_uniq.txt, in capmodel `reggio/REPORTRTS.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Th
	var t as Tk
	var pos as Num

	static function create(t Tk, pos Num) returns Self
		return Self{t: t, pos: pos}
	end 'create'
end 'Th'

function view(tV Tk) returns Num
	let h = Th.create(tV, pos: 0)
	h.pos = 1
	return 0
end 'view'

function main() returns ExitCode
	let ib = Th.create(Tk.create(0), pos: 0) as isolated Th
	modify ib 'm1'
		let tV = Tk.create(2)
		let _ = view(tV)
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-s-u5-unique-paused-into-holder-a-paused-unique-token-handed-to-a-keeping -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `U5-unique-paused-into-holder: a paused unique token handed to a keeping callee in a window` of redteamS/h_uniq.txt, in capmodel `reggio/REPORTRTS.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Th
	var t as Tk
	var pos as Num

	static function create(t Tk, pos Num) returns Self
		return Self{t: t, pos: pos}
	end 'create'
end 'Th'

function view(tV Tk) returns Num
	let h = Th.create(tV, pos: 0)
	h.pos = 1
	return 0
end 'view'

function main() returns ExitCode
	let tV = Tk.create(1)
	let ib = Th.create(Tk.create(0), pos: 0) as isolated Th
	modify ib 'm1'
		let _ = view(tV)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:31:16: 'view' needs a writable parameter 'tV'; 'tV' is read-only while 'm1' is open
```

<!-- test: model-finding-ad-modify-nested-window -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `modify-nested-window` of redteamAD/t10.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let by = Bx.create(1) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		modify by 'm2'
			useS(giveS(v))
		end 'm2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:46:10: 'by' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-finding-ad-modify-in-if-both -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `modify-in-if-both` of redteamAD/t10.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

function lg(x Num) returns Num
	return x
end 'lg'

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
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	if lg(1) != 0 'i1'
		modify bx 'm2'
			useS(giveS(v))
		end 'm2'
	end 'i1' else 'e3'
		useS(giveS(v))
	end 'e3'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:50:15: an isolated value from outside 'm2' cannot be moved into the parameter 'r' of 'giveS', which keeps it while 'm2' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-finding-ad-recv-wrap-in-window -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `recv-wrap-in-window` of redteamAD/t12.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		useS(v.wrap())
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:45:8: an isolated value from outside 'm1' cannot be moved into the parameter 'self' of 'Rl.wrap', which keeps it while 'm1' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-finding-ad-recv-fin-in-window -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `recv-fin-in-window` of redteamAD/t12.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		v.fin(1)
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ad-finalizer-param-in-window -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `finalizer-param-in-window` of redteamAD/t12.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self, r Rl)
		useS(Sn.notSent(r))
		destruct self
	end 'f'
end 'Fz'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	let z = Fz.create(0)
	modify bx 'm1'
		z.f(v)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:55:3: 'z' is mutable outside 'v'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-finding-ad-lit-in-window -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `lit-in-window` of redteamAD/t13.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		useS(Sn.notSent(v))
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:41:19: cannot store an isolated 'Rl' into 'Sn.notSent.r', which holds a mutable 'Rl'; `merge` it first
```

<!-- test: model-finding-ad-iso-param-window -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `iso-param-window` of redteamAD/t13.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function giveSI(r isolated Rl) returns Sn
	return Sn.notSent(r)
end 'giveSI'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		useS(giveSI(v))
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:38:20: cannot store an isolated 'Rl' into 'Sn.notSent.r', which holds a mutable 'Rl'; `merge` it first
```

<!-- test: model-finding-ad-plain-param-window-local -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `plain-param-window-local` of redteamAD/t13.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	modify bx 'm1'
		let v = Rl.create(0) as isolated Rl
		useS(giveS(v))
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ad-plain-param-window-fresh -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `plain-param-window-fresh` of redteamAD/t13.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	modify bx 'm1'
		useS(giveS(Rl.create(0)))
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ad-h1-min -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `h1-min` of redteamAD/t4.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		useS(giveS(v))
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:45:14: an isolated value from outside 'm1' cannot be moved into the parameter 'r' of 'giveS', which keeps it while 'm1' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-finding-ad-h1-plain-var -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `h1-plain-var` of redteamAD/t4.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0)
	modify bx 'm1'
		useS(giveS(v))
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:45:14: 'giveS' needs a writable parameter 'r' at line 31; 'v' is read-only while 'm1' is open
```

<!-- test: model-finding-ad-h1-bind-first -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `h1-bind-first` of redteamAD/t4.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		let s = giveS(v)
		useS(s)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:45:17: an isolated value from outside 'm1' cannot be moved into the parameter 'r' of 'giveS', which keeps it while 'm1' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-finding-ad-h1-direct-fin -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `h1-direct-fin` of redteamAD/t4.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function useR(r Rl)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		useR(v)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:33:8: an isolated value from outside 'm1' cannot be moved into the parameter 'r' of 'useR', which keeps it while 'm1' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-finding-ad-h1-box -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `h1-box` of redteamAD/t4.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

function wrap(b Bx) returns Ub
	return Ub.u1(b)
end 'wrap'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Bx.create(1) as isolated Bx
	modify bx 'm1'
		let _ = wrap(v)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:25:16: 'v' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-finding-ad-v-state-modify-ok -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3207. Model program `v-state-modify-ok` of redteamAD/t5.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
		modify self.b 'm1'
			useS(giveS(r))
		end 'm1'
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
```maxoncstderr
error E3207: <fragment>:40:15: 'giveS' needs a writable parameter 'r' at line 46; 'r' is read-only while 'm1' is open
```

<!-- test: model-finding-ad-v-hold-and-return-from-window -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3204. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `v-hold-and-return-from-window` of redteamAD/t5.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	var out = Sn.ok as isolated Sn
	modify bx 'm1'
		out = giveS(v)
	end 'm1'
	useS(out)
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:46:3: 'out' holds an isolated value from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-finding-ad-l-store-into-window-object -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3201, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `l-store-into-window-object` of redteamAD/t6.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
	var n as Num
	var s as Sn

	static function create(n Num, s Sn) returns Self
		return Self{n: n, s: s}
	end 'create'
end 'Hd'

function putS(h Hd, r Rl)
	h.s = Sn.notSent(r)
end 'putS'

function main() returns ExitCode
	let h = Hd.create(0, s: Sn.ok) as isolated Hd
	let v = Rl.create(0) as isolated Rl
	modify h 'm1'
		putS(h, r: v)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:24:6: the field 's' holds a linear value, and a linear value lives only in locals, parameters and message arguments: a field would keep it past the code that must consume it
error E3201: <fragment>:39:8: 'h' is mutable outside 'v'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-finding-ad-plain-modify-fresh-pair -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `plain-modify-fresh-pair` of redteamAD/t7.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
	let v = Rl.create(0) as isolated Rl
	let w = Rl.create(1) as isolated Rl
	modify v 'm1'
		useS(giveS(w))
	end 'm1'
	v.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:37:14: an isolated value from outside 'm1' cannot be moved into the parameter 'r' of 'giveS', which keeps it while 'm1' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-finding-ad-tok-unique-into-window-lend -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `tok-unique-into-window-lend` of redteamAD/t8.txt, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

type Hd
	var n as Num
	var t as Tok

	static function create(n Num, t Tok) returns Self
		return Self{n: n, t: t}
	end 'create'
end 'Hd'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function putT(h Hd, tV Tok)
	h.t = tV
end 'putT'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let h = Hd.create(0, t: Tok.create(0))
	let tV = Tok.create(1) as isolated Tok
	modify bx 'm1'
		putT(h, tV: tV)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:38:15: 'tV' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-finding-ae-union-payload-launder -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `union-payload-launder` of redteamAE/p1.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		let u = Sh.held(v)
		match u 'x2'
			none then break
			held(r) then useS(giveS(r))
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-union-payload-fn-wrap -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `union-payload-fn-wrap` of redteamAE/p1.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function wrapH(r isolated Rl) returns Sh
	return Sh.held(r)
end 'wrapH'

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
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		match wrapH(v) 'x2'
			none then break
			held(r) then useS(giveS(r))
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-union-payload-fn-wrap-consumer -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `union-payload-fn-wrap-consumer` of redteamAE/p1.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function wrapH(r isolated Rl) returns Sh
	return Sh.held(r)
end 'wrapH'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function unwrapKeep(h Sh)
	match h 'x1'
		none then break
		held(r) then useS(giveS(r))
	end 'x1'
end 'unwrapKeep'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		unwrapKeep(wrapH(v))
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-outer-plain-union-match-in-modify -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `outer-plain-union-match-in-modify` of redteamAE/p13.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let u = Sn.notSent(Rl.create(1))
	modify bx 'm1'
		match u 'x2'
			ok then break
			notSent(q) then useS(giveS(q))
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:47:31: 'giveS' needs a writable parameter 'r' at line 31; 'q' is read-only while 'm1' is open
```

<!-- test: model-finding-ae-outer-plain-linear-pass-in-modify -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `outer-plain-linear-pass-in-modify` of redteamAE/p13.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(1)
	modify bx 'm1'
		useS(giveS(v))
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:45:14: 'giveS' needs a writable parameter 'r' at line 31; 'v' is read-only while 'm1' is open
```

<!-- test: model-finding-ae-outer-plain-linear-recv-in-modify -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `outer-plain-linear-recv-in-modify` of redteamAE/p13.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(1)
	modify bx 'm1'
		useS(v.wrap())
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:45:8: 'wrap' needs a writable receiver at line 22; 'v' is read-only while 'm1' is open
```

<!-- test: model-finding-ae-w-match-consume-recv-wrap -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `w-match-consume-recv-wrap` of redteamAE/p14.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let u = Sn.notSent(Rl.create(1))
	modify bx 'm1'
		match u 'x2'
			ok then break
			notSent(q) then useS(q.wrap())
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:47:25: 'wrap' needs a writable receiver at line 22; 'q' is read-only while 'm1' is open
```

<!-- test: model-finding-ae-w-match-consume-param -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `w-match-consume-param` of redteamAE/p14.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function f(u Sn, bx isolated Bx)
	modify bx 'm1'
		match u 'x2'
			ok then break
			notSent(q) then useS(giveS(q))
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	f(Sn.notSent(Rl.create(1)), bx: bx)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:45:31: 'giveS' needs a writable parameter 'r' at line 31; 'q' is read-only while 'm1' is open
```

<!-- test: model-finding-ae-w-match-consume-store-in-window-obj -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `w-match-consume-store-in-window-obj` of redteamAE/p14.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

	function eat(r Rl)
		useS(giveS(r))
	end 'eat'
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
	modify bx 'm1'
		match u 'x2'
			ok then break
			notSent(q) then bx.eat(q)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:51:27: 'eat' needs a writable parameter 'r' (it calls 'giveS', which writes 'r'); 'q' is read-only while 'm1' is open
```

<!-- test: model-finding-ae-ro-union-iso-arm-write-modify -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ro-union-iso-arm-write-modify` of redteamAE/p16.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let u = Ub.u1(Bx.create(1))
	modify bx 'm1'
		match u 'x2'
			u0 then break
			u1 then bx.n = 3
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-ro-union-iso-arm-wait-modify -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ro-union-iso-arm-wait-modify` of redteamAE/p16.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

type S
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function plain() returns Num
		return 5
	end 'plain'
end 'S'

function main() returns ExitCode
	let _ = spawn S.create(0)
	let bx = Bx.create(0) as isolated Bx
	let u = Ub.u1(Bx.create(1))
	modify bx 'm1'
		match u 'x2'
			u0 then break
			u1 then bx.n = 3
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-iso-nonlinear-arm-write-own-root -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `iso-nonlinear-arm-write-own-root` of redteamAE/p16.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
	let u = Ub.u1(Bx.create(1)) as isolated Ub
	modify u 'm1'
		match u 'x2'
			u0 then break
			u1(b) then b.n = 3
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-w-match-consume-in-modify-else -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `w-match-consume-in-modify-else` of redteamAE/p17.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let u = Sn.notSent(Rl.create(1))
	if lg(1) != 0 'i1'
		modify bx 'm2'
			match u 'x3'
				ok then break
				notSent(q) then useS(giveS(q))
			end 'x3'
		end 'm2'
	end 'i1' else 'e4'
		match u 'x5'
			ok then break
			notSent(q) then q.fin(2)
		end 'x5'
	end 'e4'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:52:32: 'giveS' needs a writable parameter 'r' at line 31; 'q' is read-only while 'm2' is open
```

<!-- test: model-finding-ae-w-match-consume-return -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3202. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w-match-consume-return` of redteamAE/p17.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

function pick(u Sn, bx isolated Bx) returns Rl
	modify bx 'm1'
		match u 'x2'
			ok then return Rl.create(0)
			notSent(q) then return q
		end 'x2'
	end 'm1'
	return Rl.create(9)
end 'pick'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let r = pick(Sn.notSent(Rl.create(1)), bx: bx)
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:34:27: 'q' is mutable and cannot leave 'm1'; a value leaving a modify block must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-ae-ro-field-iso-recv-call-writes-outer -->
The model (`core-m3w-fa24`) refuses it by `C-ISOREAD`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `ro-field-iso-recv-call-writes-outer` of redteamAE/p19.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Cnt
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'
end 'Cnt'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function touch(c Cnt)
		c.k = 3
	end 'touch'
end 'Bx'

type Hd
	var item as isolated Bx

	static function create(item isolated Bx) returns Self
		return Self{item: item}
	end 'create'
end 'Hd'

function main() returns ExitCode
	let bx = Cnt.create(0) as isolated Cnt
	let h = Hd.create(Bx.create(1))
	let c = Cnt.create(5)
	modify bx 'm1'
		h.item.touch(c)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:37:16: 'c' is mutable outside 'item'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-finding-ae-iso-return-from-window -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `iso-return-from-window` of redteamAE/p21.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function mkR(bx isolated Bx) returns isolated Rl
	modify bx 'm1'
		return Rl.create(1)
	end 'm1'
	return Rl.create(2)
end 'mkR'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let r = mkR(bx)
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-window-fresh-plain-into-outer-plain-var -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `window-fresh-plain-into-outer-plain-var` of redteamAE/p21.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	var out = Rl.create(0)
	out.fin(1)
	modify bx 'm1'
		out = Rl.create(7)
	end 'm1'
	out.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:30:3: 'out' holds a linear value from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-finding-ae-window-fresh-plain-into-outer-plain-var-use -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `window-fresh-plain-into-outer-plain-var-use` of redteamAE/p21.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	var out = Rl.create(0)
	out.fin(1)
	modify bx 'm1'
		out = Rl.create(7)
		useS(giveS(out))
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:46:3: 'out' holds a linear value from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-finding-ae-window-local-union-into-outer -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `window-local-union-into-outer` of redteamAE/p21.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	var out = Sn.ok
	modify bx 'm1'
		out = Sn.notSent(Rl.create(7))
	end 'm1'
	useS(out)
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:41:3: 'out' holds a linear value from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-finding-ae-nested-modify-write-outer-root -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `nested-modify-write-outer-root` of redteamAE/p24.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hd2
	var n as Num
	var item as isolated Bx

	static function create(n Num, item isolated Bx) returns Self
		return Self{n: n, item: item}
	end 'create'
end 'Hd2'

function main() returns ExitCode
	let h = Hd2.create(0, item: Bx.create(1)) as isolated Hd2
	modify h 'm1'
		modify h.item 'm2'
			h.n = 3
		end 'm2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:25:4: cannot write 'n' through 'h', which is read-only while 'm2' is open
```

<!-- test: model-finding-ae-nested-modify-read-outer-root -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nested-modify-read-outer-root` of redteamAE/p24.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hd2
	var n as Num
	var item as isolated Bx

	static function create(n Num, item isolated Bx) returns Self
		return Self{n: n, item: item}
	end 'create'
end 'Hd2'

function main() returns ExitCode
	let h = Hd2.create(0, item: Bx.create(1)) as isolated Hd2
	modify h 'm1'
		modify h.item 'm2'
			let _ = h.n
		end 'm2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-modify-match-field-union-write-root -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `modify-match-field-union-write-root` of redteamAE/p24.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

type Hd3
	var n as Num
	var u as Ub

	static function create(n Num, u Ub) returns Self
		return Self{n: n, u: u}
	end 'create'
end 'Hd3'

function main() returns ExitCode
	let h = Hd3.create(0, u: Ub.u1(Bx.create(1))) as isolated Hd3
	modify h 'm1'
		match h.u 'x2'
			u0 then break
			u1 then h.n = 3
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-modify-match-field-union-write-other-field -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `modify-match-field-union-write-other-field` of redteamAE/p24.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

type Hd3
	var n as Num
	var u as Ub

	static function create(n Num, u Ub) returns Self
		return Self{n: n, u: u}
	end 'create'
end 'Hd3'

function main() returns ExitCode
	let h = Hd3.create(0, u: Ub.u1(Bx.create(1))) as isolated Hd3
	modify h 'm1'
		match h.u 'x2'
			u0 then break
			u1 then h.u = Ub.u0
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-w-modify-root-linear-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-modify-root-linear-write` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
	modify r 'm1'
		r.code = 5
	end 'm1'
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-w-modify-root-then-use -->
The model (`core-m3w-fa24`) refuses it by `C-SLOT`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w-modify-root-then-use` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
	modify r 'm1'
		r.fin(1)
	end 'm1'
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:26:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-w-modify-return-inside -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `w-modify-return-inside` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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

function f()
	let r = Resp.create(0) as isolated Resp
	let b = Box.create(0) as isolated Box
	modify b 'm1'
		return
	end 'm1'
	r.fin(1)
end 'f'

function main() returns ExitCode
	f()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:33:3: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-w-modify-return-fresh-inside -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `w-modify-return-fresh-inside` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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

function f()
	let b = Box.create(0) as isolated Box
	modify b 'm1'
		let _ = Resp.create(0)
		return
	end 'm1'
end 'f'

function main() returns ExitCode
	f()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:33:3: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-w-modify-outer-var-consumed-inside -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `w-modify-outer-var-consumed-inside` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
	let b = Box.create(0) as isolated Box
	modify b 'm1'
		r.fin(1)
	end 'm1'
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:35:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-w-modify-outer-var-consumed-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `w-modify-outer-var-consumed-twice` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
	let b = Box.create(0) as isolated Box
	modify b 'm1'
		r.fin(1)
		r.fin(2)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:34:3: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-w-modify-outer-var-consumed-in-if -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `w-modify-outer-var-consumed-in-if` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
	let b = Box.create(0) as isolated Box
	modify b 'm1'
		if 1 != 0 'i2'
			r.fin(1)
		end 'i2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:33:3: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-w-modify-assign-outer -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `w-modify-assign-outer` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
	var r = Resp.create(0) as isolated Resp
	let b = Box.create(0) as isolated Box
	modify b 'm1'
		r = Resp.create(1)
	end 'm1'
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:33:3: 'r' holds an isolated value from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-hand-ab-w-modify-assign-outer-after-consume -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `w-modify-assign-outer-after-consume` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
	var r = Resp.create(0) as isolated Resp
	let b = Box.create(0) as isolated Box
	r.fin(0)
	modify b 'm1'
		r = Resp.create(1)
	end 'm1'
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:34:3: 'r' holds an isolated value from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-hand-ab-w-modify-assign-outer-consume-refill -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `w-modify-assign-outer-consume-refill` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
	var r = Resp.create(0) as isolated Resp
	let b = Box.create(0) as isolated Box
	modify b 'm1'
		r.fin(0)
		r = Resp.create(1)
	end 'm1'
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:34:3: 'r' holds an isolated value from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-hand-ab-w-modify-nested -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `w-modify-nested` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
	let a = Box.create(0) as isolated Box
	let b = Box.create(1) as isolated Box
	modify a 'm1'
		let r = Resp.create(0)
		modify b 'm2'
			r.fin(1)
		end 'm2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:34:10: 'b' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ab-w-modify-nested-consume-twice -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `w-modify-nested-consume-twice` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
	let a = Box.create(0) as isolated Box
	let b = Box.create(1) as isolated Box
	modify a 'm1'
		let r = Resp.create(0)
		modify b 'm2'
			r.fin(1)
		end 'm2'
		r.fin(2)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:34:10: 'b' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ab-w-modify-live-across-window -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-modify-live-across-window` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
	let r = Resp.create(0)
	let b = Box.create(0) as isolated Box
	modify b 'm1'
		b.n = 1
	end 'm1'
	r.fin(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-w-modify-moved-before-in-window -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `w-modify-moved-before-in-window` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
	let r = Resp.create(0)
	r.fin(1)
	let b = Box.create(0) as isolated Box
	modify b 'm1'
		let _ = r.peek()
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:34:11: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ab-w-modify-result-moved-out -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `w-modify-result-moved-out` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
	var out = Resp.create(9)
	out.fin(0)
	let b = Box.create(0) as isolated Box
	modify b 'm1'
		out = Resp.create(1)
	end 'm1'
	out.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:34:3: 'out' holds a linear value from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-hand-ab-w-modify-two-linears -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `w-modify-two-linears` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
	let b = Box.create(0) as isolated Box
	modify b 'm1'
		let r = Resp.create(0)
		let _ = Resp.create(1)
		r.fin(1)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:35:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-ub-modify-target-temp-linear -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `ub-modify-target-temp-linear` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

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
	let b = Box.create(0) as isolated Box
	modify b 'm1'
		let _ = mkR().peek()
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:36:3: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ac-ld-modify-lend -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `ld-modify-lend` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

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

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let b = Box.create(0) as isolated Box
	modify b 'm1'
		useR(r)
	end 'm1'
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:37:8: an isolated value from outside 'm1' cannot be moved into the parameter 'r' of 'useR', which keeps it while 'm1' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-hand-ad-w-modify-consume-root-after -->
The model (`core-m3w-fa24`) refuses it by `C-SLOT`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w-modify-consume-root-after` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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
	modify r 'm1'
		r.fin(1)
	end 'm1'
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:34:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-w-modify-bump -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-modify-bump` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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
	modify r 'm1'
		r.bump()
	end 'm1'
	r.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-w-modify-pass-root-after -->
The model (`core-m3w-fa24`) refuses it by `C-SLOT`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w-modify-pass-root-after` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	modify r 'm1'
		useR(r)
	end 'm1'
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:38:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-w-modify-inner-isolated -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `w-modify-inner-isolated` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function lg(x Num) returns Num
	return x
end 'lg'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let b = Box.create(0) as isolated Box
	modify b 'm1'
		useR(r)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:45:8: an isolated value from outside 'm1' cannot be moved into the parameter 'r' of 'useR', which keeps it while 'm1' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-hand-ad-w-modify-inner-isolated-twice -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `w-modify-inner-isolated-twice` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function lg(x Num) returns Num
	return x
end 'lg'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let b = Box.create(0) as isolated Box
	modify b 'm1'
		useR(r)
		useR(r)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:45:8: an isolated value from outside 'm1' cannot be moved into the parameter 'r' of 'useR', which keeps it while 'm1' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-hand-ad-w-modify-window-in-loop -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `w-modify-window-in-loop` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function lg(x Num) returns Num
	return x
end 'lg'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let b = Box.create(0) as isolated Box
	var i = 2 as Num
	while i != 0 'w1'
		modify b 'm2'
			useR(r)
		end 'm2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:47:9: an isolated value from outside 'm2' cannot be moved into the parameter 'r' of 'useR', which keeps it while 'm2' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-hand-ad-o-modify-union -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `o-modify-union` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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
	let s = giveS(Resp.create(0)) as isolated Sent
	modify s 'm1'
		let _ = 0 as Num
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:43:2: this drops 's', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-hole-modify-in-if -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `hole-modify-in-if` of redteamAD.hand_ad9, in capmodel `reggio/REPORTRTAD.txt`.

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

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	if lg(1) != 0 'i1'
		modify bx 'm2'
			useS(giveS(v))
		end 'm2'
	end 'i1' else 'e3'
		useS(giveS(v))
	end 'e3'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:50:15: an isolated value from outside 'm2' cannot be moved into the parameter 'r' of 'giveS', which keeps it while 'm2' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-hand-ad-hole-receiver -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `hole-receiver` of redteamAD.hand_ad9, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sw
	ok
	notSent(r Rw)
end 'Sw'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Rw
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function wrap(consuming self) returns Sw
		return Sw.notSent(self)
	end 'wrap'
end 'Rw'

function useSw(s Sw)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useSw'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rw.create(0) as isolated Rw
	modify bx 'm1'
		useSw(v.wrap())
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:45:9: an isolated value from outside 'm1' cannot be moved into the parameter 'self' of 'Rw.wrap', which keeps it while 'm1' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-hand-ae-method-root-window -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `method-root-window` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
end 'Rl'

type Hd
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'

	function eat(r Rl)
		useS(giveS(r))
	end 'eat'

	function eatI(r isolated Rl)
		r.fin(1)
	end 'eatI'

	function pass(r Rl) returns Rl
		return r
	end 'pass'
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
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		bx.eat(v)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:61:3: 'bx' is mutable outside 'v'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ae-method-pass-window -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `method-pass-window` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
end 'Rl'

type Hd
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'

	function eat(r Rl)
		useS(giveS(r))
	end 'eat'

	function eatI(r isolated Rl)
		r.fin(1)
	end 'eatI'

	function pass(r Rl) returns Rl
		return r
	end 'pass'
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

function useR(r Rl)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let bx = Hd.create(0) as isolated Hd
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		let w = bx.pass(v)
		useR(w)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:65:11: 'bx' is mutable outside 'v'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ae-pass-result-local -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `pass-result-local` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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

	function peek() returns Num
		return self.code
	end 'peek'
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function useR(r Rl)
	r.fin(1)
end 'useR'

function idP(r Rl) returns Rl
	return r
end 'idP'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		let w = idP(v)
		useR(w)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:41:15: an isolated value from outside 'm1' cannot be moved into the parameter 'r' of 'idP', which keeps it while 'm1' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-hand-ae-pass-result-in-gives -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `pass-result-in-giveS` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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

function idP(r Rl) returns Rl
	return r
end 'idP'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		useS(giveS(idP(v)))
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:53:18: an isolated value from outside 'm1' cannot be moved into the parameter 'r' of 'idP', which keeps it while 'm1' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-hand-ae-merge-param -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `merge-param` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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

function mgI(r isolated Rl) returns Sn
	return Sn.notSent(merge r)
end 'mgI'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		useS(mgI(v))
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ae-merge-param-bound -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `merge-param-bound` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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

function mgI(r isolated Rl) returns Sn
	return Sn.notSent(merge r)
end 'mgI'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		let s = mgI(v)
		useS(s)
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ae-merge-into-window-root -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `merge-into-window-root` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hold2
	var keep as Bx

	static function create(keep Bx) returns Self
		return Self{keep: keep}
	end 'create'
end 'Hold2'

function main() returns ExitCode
	let bh = Hold2.create(Bx.create(0)) as isolated Hold2
	let w = Bx.create(1) as isolated Bx
	modify bh 'm1'
		bh.keep = merge w
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ae-cond-in-window -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-COND`; the compiler refuses it with E3223. Model program `cond-in-window` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		useS(giveS((v if lg(1) != 0 else Rl.create(2))))
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:53:17: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ae-cond-bound -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-COND`; the compiler refuses it with E3223. Model program `cond-bound` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		let w = (v if lg(1) != 0 else Rl.create(2)) as isolated Rl
		useS(giveS(w))
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:53:14: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ae-cond-fresh-first -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-COND`; the compiler refuses it with E3223. Model program `cond-fresh-first` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		let w = (Rl.create(2) if lg(0) != 0 else v) as isolated Rl
		useS(giveS(w))
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:53:25: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ae-in-if-else-join -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3223. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `in-if-else-join` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		var w = Rl.create(3) as isolated Rl
		if lg(1) != 0 'i2'
			w.fin(1)
			w = v
		end 'i2'
		useS(giveS(w))
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:54:3: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ae-in-loop-window -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `in-loop-window` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		var i = 1 as Num
		while i != 0 'w2'
			useS(giveS(v))
			i = 0
		end 'w2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:51:15: an isolated value from outside 'm1' cannot be moved into the parameter 'r' of 'giveS', which keeps it while 'm1' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-hand-ae-local-then-modify-local -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `local-then-modify-local` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify bx 'm1'
		let w = v as isolated Rl
		let by = Bx.create(1) as isolated Bx
		modify by 'm2'
			useS(giveS(w))
		end 'm2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:52:15: an isolated value from outside 'm2' cannot be moved into the parameter 'r' of 'giveS', which keeps it while 'm2' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-hand-ae-local-outside-then-window -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `local-outside-then-window` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	let w = v as isolated Rl
	modify bx 'm1'
		useS(giveS(w))
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:50:14: an isolated value from outside 'm1' cannot be moved into the parameter 'r' of 'giveS', which keeps it while 'm1' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-hand-ae-local-outside-two -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `local-outside-two` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	var w = Rl.create(0) as isolated Rl
	if lg(1) != 0 'i1'
		let v = Rl.create(1) as isolated Rl
		w.fin(1)
		w = v
	end 'i1'
	modify bx 'm2'
		useS(giveS(w))
	end 'm2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:58:14: an isolated value from outside 'm2' cannot be moved into the parameter 'r' of 'giveS', which keeps it while 'm2' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-hand-ae-swap-field-iso -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `swap-field-iso` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Hs
	var item as isolated Rl2

	static function create(item isolated Rl2) returns Self
		return Self{item: item}
	end 'create'
end 'Hs'

type Rl2
	var c as Num

	static function create(c Num) returns Self
		return Self{c: c}
	end 'create'
end 'Rl2'

function main() returns ExitCode
	let hs = Hs.create(Rl2.create(0)) as isolated Hs
	var v = Rl2.create(1) as isolated Rl2
	modify hs 'm1'
		let o = swap hs.item with v
		v = o
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:25:3: 'v' holds an isolated value from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-hand-ae-recv-param-keeps -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `recv-param-keeps` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Rz
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function put(consuming self, x Rl)
		useS(giveS(x))
		destruct self
	end 'put'
end 'Rz'

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
	let bx = Bx.create(0) as isolated Bx
	let v = Rz.create(0) as isolated Rz
	modify bx 'm1'
		v.put(Rl.create(1))
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ae-recv-field-int -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `recv-field-int` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Rz
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		let _ = self.code
		destruct self
	end 'fin'
end 'Rz'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rz.create(0) as isolated Rz
	modify bx 'm1'
		v.fin(1)
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ae-recv-two-arg-sends -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `recv-two-arg-sends` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Rz
	var code as Num
	var k as Kz.handle

	static function create(code Num, k Kz.handle) returns Self
		return Self{code: code, k: k}
	end 'create'

	function fin(consuming self, c Num)
		try self.k.go(c) otherwise (sendFailure) 'h1'
			match sendFailure 'x2'
				mailboxFull then break
				serviceStopped then break
			end 'x2'
		end 'h1'
		destruct self
	end 'fin'
end 'Rz'

type Kz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function go(c Num)
		self.n = c
	end 'go'
end 'Kz'

function main() returns ExitCode
	let kV = spawn Kz.create(0)
	let bx = Bx.create(0) as isolated Bx
	let v = Rz.create(0, k: kV) as isolated Rz
	modify bx 'm1'
		v.fin(1)
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ae-window-local-outer-recv-result-iso -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `window-local-outer-recv-result-iso` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Rz
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self, c Num)
		let _ = c
		destruct self
	end 'fin'

	function mk2(consuming self) returns isolated Bx
		let nV = self.code
		destruct self
		return Bx.create(nV)
	end 'mk2'
end 'Rz'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rz.create(0) as isolated Rz
	modify bx 'm1'
		let w = v.mk2() as isolated Bx
		let _ = w.n
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-m-keep-plain-fn -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-keep-plain-fn` of redteamAF.hand_af12, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Mx
	m0(x Bx)
	m1(y isolated Bx)
end 'Mx'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hs
	var item as isolated Bx
	var keep as Bx

	static function create(item isolated Bx, keep Bx) returns Self
		return Self{item: item, keep: keep}
	end 'create'
end 'Hs'

type Sx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		b.n = 1
	end 'takeB'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Sx'

function keepB(b Bx)
	b.n = 7
end 'keepB'

function main() returns ExitCode
	let hi = Hs.create(Bx.create(1), keep: Bx.create(2)) as isolated Hs
	let rb = Bx.create(5) as isolated Bx
	let _ = spawn Sx.create(0)
	modify hi 'm1'
		let u = Mx.m1(rb)
		match u 'x2'
			m0(x) then keepB(x)
			m1(o) then keepB(o)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-m-iso-fn -->
The model (`core-m3w-fa24`) refuses it by `C-LEND`; the compiler refuses it with E3198. Model program `m-iso-fn` of redteamAF.hand_af12, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Mx
	m0(x Bx)
	m1(y isolated Bx)
end 'Mx'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hs
	var item as isolated Bx
	var keep as Bx

	static function create(item isolated Bx, keep Bx) returns Self
		return Self{item: item, keep: keep}
	end 'create'
end 'Hs'

type Sx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		b.n = 1
	end 'takeB'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Sx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function keepB(b Bx)
	b.n = 7
end 'keepB'

function main() returns ExitCode
	let hi = Hs.create(Bx.create(1), keep: Bx.create(2)) as isolated Hs
	let rb = Bx.create(5) as isolated Bx
	let _ = spawn Sx.create(0)
	modify hi 'm1'
		let u = Mx.m1(rb)
		match u 'x2'
			m0(x) then keepB(x)
			m1(o) then takeB(o)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:58:21: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-af-m-store-iso-slot -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `m-store-iso-slot` of redteamAF.hand_af12, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Mx
	m0(x Bx)
	m1(y isolated Bx)
end 'Mx'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hs
	var item as isolated Bx
	var keep as Bx

	static function create(item isolated Bx, keep Bx) returns Self
		return Self{item: item, keep: keep}
	end 'create'
end 'Hs'

type Sx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		b.n = 1
	end 'takeB'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Sx'

function keepB(b Bx)
	b.n = 7
end 'keepB'

function main() returns ExitCode
	let hi = Hs.create(Bx.create(1), keep: Bx.create(2)) as isolated Hs
	let rb = Bx.create(5) as isolated Bx
	let _ = spawn Sx.create(0)
	modify hi 'm1'
		let u = Mx.m1(rb)
		match u 'x2'
			m0(x) then keepB(x)
			m1(o) then hi.item = o
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:54:15: cannot write 'item' through 'hi', which is read-only while 'x2' is open
```

<!-- test: model-hand-af-m-keep-into-window-obj -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m-keep-into-window-obj` of redteamAF.hand_af12, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Mx
	m0(x Bx)
	m1(y isolated Bx)
end 'Mx'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hs
	var item as isolated Bx
	var keep as Bx

	static function create(item isolated Bx, keep Bx) returns Self
		return Self{item: item, keep: keep}
	end 'create'
end 'Hs'

type Sx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		b.n = 1
	end 'takeB'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Sx'

function keepB(b Bx)
	b.n = 7
end 'keepB'

function keepInto(b Bx, h Hs)
	h.keep = b
end 'keepInto'

function main() returns ExitCode
	let hi = Hs.create(Bx.create(1), keep: Bx.create(2)) as isolated Hs
	let rb = Bx.create(5) as isolated Bx
	let _ = spawn Sx.create(0)
	modify hi 'm1'
		let u = Mx.m1(rb)
		match u 'x2'
			m0(x) then keepB(x)
			m1(o) then keepInto(o, h: hi)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:58:30: 'hi' is mutable outside 'o'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-af-u-direct-keep -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `u-direct-keep` of redteamAF.hand_af12, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hs
	var item as isolated Bx
	var keep as Bx

	static function create(item isolated Bx, keep Bx) returns Self
		return Self{item: item, keep: keep}
	end 'create'
end 'Hs'

type Sx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		b.n = 1
	end 'takeB'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Sx'

function keepB(b Bx)
	b.n = 7
end 'keepB'

function main() returns ExitCode
	let hi = Hs.create(Bx.create(1), keep: Bx.create(2)) as isolated Hs
	let rb = Bx.create(5) as isolated Bx
	let _ = spawn Sx.create(0)
	modify hi 'm1'
		keepB(rb)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:46:9: 'rb' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-af-u-direct-keep-into -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `u-direct-keep-into` of redteamAF.hand_af12, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hs
	var item as isolated Bx
	var keep as Bx

	static function create(item isolated Bx, keep Bx) returns Self
		return Self{item: item, keep: keep}
	end 'create'
end 'Hs'

type Sx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		b.n = 1
	end 'takeB'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Sx'

function keepInto(b Bx, h Hs)
	h.keep = b
end 'keepInto'

function main() returns ExitCode
	let hi = Hs.create(Bx.create(1), keep: Bx.create(2)) as isolated Hs
	let rb = Bx.create(5) as isolated Bx
	let _ = spawn Sx.create(0)
	modify hi 'm1'
		keepInto(rb, h: hi)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:46:12: 'rb' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-af-n9-modify-then-match -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `n9-modify-then-match` of redteamAF.hand_af17, in capmodel `reggio/REPORTRTAF.txt`.

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
	let ub = Ub.u1(Bx.create(0)) as isolated Ub
	modify ub 'm1'
		match ub 'x2'
			u0 then break
			u1 then break
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-a14-modify-iso-var-then-match-inside -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a14-modify-iso-var-then-match-inside` of redteamAF.hand_af18, in capmodel `reggio/REPORTRTAF.txt`.

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

function main() returns ExitCode
	let ub = Ub.u1(Bx.create(1)) as isolated Ub
	modify ub 'm1'
		match ub 'x2'
			u0 then break
			u1 then break
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-w10-linear-swap-out-modify -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3224. Model program `w10-linear-swap-out-modify` of redteamAF.hand_af20, in capmodel `reggio/REPORTRTAF.txt`.

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

type Lh
	var s as isolated Sh

	static function create(s isolated Sh) returns Self
		return Self{s: s}
	end 'create'
end 'Lh'

function main() returns ExitCode
	let h = Lh.create(Sh.held(Rl.create(1))) as isolated Lh
	modify h 'm1'
		match swap h.s with Sh.none 'x2'
			none then break
			held(q) then q.fin(1)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:23:6: the field 's' holds a linear value, and a linear value lives only in locals, parameters and message arguments: a field would keep it past the code that must consume it
```

<!-- test: model-hand-af-w12-linear-swap-out-store-back-other-region -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3214, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w12-linear-swap-out-store-back-other-region` of redteamAF.hand_af20, in capmodel `reggio/REPORTRTAF.txt`.

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

type Lh
	var s as isolated Sh

	static function create(s isolated Sh) returns Self
		return Self{s: s}
	end 'create'
end 'Lh'

function main() returns ExitCode
	let h = Lh.create(Sh.held(Rl.create(1))) as isolated Lh
	let g = Lh.create(Sh.none) as isolated Lh
	modify h 'm1'
		match swap h.s with Sh.none 'x2'
			none then break
			held(q) then let _ = swap g.s with Sh.held(q)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:23:6: the field 's' holds a linear value, and a linear value lives only in locals, parameters and message arguments: a field would keep it past the code that must consume it
error E3214: <fragment>:36:21: cannot bind '_' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: model-hand-af-s10-binder-shadows-in-window -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s10-binder-shadows-in-window` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let q = Rl.create(9) as isolated Rl
	let u = Sh.held(Rl.create(1))
	modify bx 'm1'
		let v = Sh.held(Rl.create(2))
		match v 'x2'
			none then break
			held(q) then q.fin(1)
		end 'x2'
	end 'm1'
	q.fin(2)
	match u 'x3'
		none then break
		held(w) then w.fin(3)
	end 'x3'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-s11-binder-shadows-outer-in-window-moved -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s11-binder-shadows-outer-in-window-moved` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function giveH(r isolated Rl) returns Sh
	return Sh.held(r)
end 'giveH'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let q = Rl.create(9) as isolated Rl
	modify bx 'm1'
		let v = giveH(q)
		match v 'x2'
			none then break
			held(q) then q.fin(1)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-s12-binder-shadows-then-outer-used -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s12-binder-shadows-then-outer-used` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let ov = Rl.create(9) as isolated Rl
	modify bx 'm1'
		let v = Sh.held(Rl.create(2))
		match v 'x2'
			none then break
			held(ov) then ov.fin(1)
		end 'x2'
	end 'm1'
	ov.fin(2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-s16-plain-payload-binder-shadow-in-modify -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s16-plain-payload-binder-shadow-in-modify` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let y = Bx.create(5)
	modify bx 'm1'
		let u = Mp.m1(Bx.create(1))
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then y.n = 2
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-s17-plain-payload-binder-shadow-in-modify-write-after -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `s17-plain-payload-binder-shadow-in-modify-write-after` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let y = Bx.create(5)
	modify bx 'm1'
		let u = Mp.m1(Bx.create(1))
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then y.n = 2
		end 'x2'
		y.n = 7
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:56:3: cannot write 'n' through 'y', which is read-only while 'm1' is open
```

<!-- test: model-hand-af-p2-plain-mixed-in-fn-in-modify -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `p2-plain-mixed-in-fn-in-modify` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let u = Mp.m1(Bx.create(1))
	modify bx 'm1'
		eatMp(u)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:58:9: 'eatMp' needs a writable parameter 'u' at line 50; 'u' is read-only while 'm1' is open
```

<!-- test: model-hand-af-p3-plain-mixed-fresh-in-modify -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p3-plain-mixed-fresh-in-modify` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	modify bx 'm1'
		eatMp(Mp.m1(Bx.create(1)))
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-p5-plain-mixed-share-payload-window -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `p5-plain-mixed-share-payload-window` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let b = Bx.create(1)
	modify bx 'm1'
		let u = Mp.m1(b)
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then y.n = 2
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:51:17: cannot store a read-only 'Bx' into 'Mp.m1.y' while 'm1' is open; store a copy
```

<!-- test: model-hand-af-p9-plain-mixed-immutable-payload-moved-to-window -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p9-plain-mixed-immutable-payload-moved-to-window` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	let u = Mq.m1(freeze (Bx.create(1)))
	modify bx 'm1'
		let _ = 1 as Num
	end 'm1'
	match u 'x2'
		m0(x) then x.fin(1)
		m1(y) then let _ = y.n
	end 'x2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-p12-modify-region-into-outer -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `p12-modify-region-into-outer` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hp
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hp'

function main() returns ExitCode
	let hi = Hp.create(Bx.create(0), q: Bx.create(1)) as isolated Hp
	let h = Hp.create(Bx.create(2), q: Bx.create(3))
	modify hi 'm1'
		let u = Mp.m1(hi.p)
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.p = y
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:46:15: cannot write 'p' through 'h', which is read-only while 'm1' is open
```

<!-- test: model-hand-af-p13-modify-region-into-outer-var -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `p13-modify-region-into-outer-var` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hp
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hp'

function main() returns ExitCode
	let hi = Hp.create(Bx.create(0), q: Bx.create(1)) as isolated Hp
	var o = Bx.create(5)
	modify hi 'm1'
		let u = Mp.m1(hi.p)
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then o = y
		end 'x2'
	end 'm1'
	o.n = 1
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:46:15: 'o' holds a record from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-hand-af-p14-modify-region-into-keeping-fn -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `p14-modify-region-into-keeping-fn` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hp
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hp'

function keep(b Bx, h Hp)
	h.p = b
end 'keep'

function main() returns ExitCode
	let hi = Hp.create(Bx.create(0), q: Bx.create(1)) as isolated Hp
	let h = Hp.create(Bx.create(2), q: Bx.create(3))
	modify hi 'm1'
		let u = Mp.m1(hi.p)
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keep(y, h: h)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:50:23: 'keep' needs a writable parameter 'h' at line 40; 'h' is read-only while 'm1' is open
```

<!-- test: model-hand-af-p15-modify-region-sibling-store -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p15-modify-region-sibling-store` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hp
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hp'

function main() returns ExitCode
	let hi = Hp.create(Bx.create(0), q: Bx.create(1)) as isolated Hp
	modify hi 'm1'
		let u = Mp.m1(hi.p)
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then hi.q = y
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-p16-modify-region-leaves-by-return -->
The model (`core-m3w-fa24`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `p16-modify-region-leaves-by-return` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Rl'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hp
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hp'

function leak(hi isolated Hp) returns Bx
	modify hi 'm1'
		match Mp.m1(hi.p) 'x2'
			m0(x) then x.fin(1)
			m1(y) then return y
		end 'x2'
	end 'm1'
	return Bx.create(0)
end 'leak'

function main() returns ExitCode
	let hi = Hp.create(Bx.create(0), q: Bx.create(1)) as isolated Hp
	let r = leak(hi)
	r.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:43:22: 'y' is mutable and cannot leave 'm1'; a value leaving a modify block must be a value, immutable or a fresh isolated
```

<!-- test: model-hand-af-p17-modify-region-leaves-by-return-nonowned -->
The model (`core-m3w-fa24`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `p17-modify-region-leaves-by-return-nonowned` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

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

type Hp
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hp'

function leak(hi isolated Hp) returns Bx
	modify hi 'm1'
		match Ub.u1(hi.p) 'x2'
			u0 then break
			u1(y) then return y
		end 'x2'
	end 'm1'
	return Bx.create(0)
end 'leak'

function main() returns ExitCode
	let hi = Hp.create(Bx.create(0), q: Bx.create(1)) as isolated Hp
	let r = leak(hi)
	r.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:30:22: 'y' is mutable and cannot leave 'm1'; a value leaving a modify block must be a value, immutable or a fresh isolated
```

<!-- test: model-hand-ag-t3-twin-in-modify-write -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `t3-twin-in-modify-write` of redteamAG.hand_ag1, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mz
	m0(x Rl)
	m1(a Bx, b Bx)
end 'Mz'

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
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let p = Bx.create(1)
	let u = Mz.m1(p, b: p) as isolated Mz
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(a, _) then a.n = 5
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:59:10: 'Mz.m1(p, b: p) as isolated Mz' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-ag-t5-plain-var-mixed-store -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3196. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `t5-plain-var-mixed-store` of redteamAG.hand_ag1, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mz
	m0(x Rl)
	m1(a Bx, b Bx)
end 'Mz'

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
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let p = Bx.create(1)
	let u = Mz.m1(p, b: p)
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(a, _) then h.keep = a
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:63:27: cannot store a read-only 'Bx' into 'keep' while 'm1' is open; store a copy
```

<!-- test: model-hand-ag-bm-modify-keep-p -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196, E3201. Model program `bm-modify-keep-p` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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
		modify self.item 'm1'
			match u 'x2'
				m0(x) then x.fin(1)
				m1(y) then self.item.keep = y.p
			end 'x2'
		end 'm1'
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
error E3196: <fragment>:83:33: cannot store a read-only 'Bx' into 'keep' while 'm1' is open; store a copy
```

<!-- test: model-hand-ag-bm-modify-plain -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3197, E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `bm-modify-plain` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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
		modify self.item 'm1'
			match u 'x2'
				m0(x) then x.fin(1)
				m1(y) then self.plain = y.p
			end 'x2'
		end 'm1'
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
error E3197: <fragment>:83:16: cannot write 'plain' through 'self', which is read-only while 'm1' is open
```

<!-- test: model-hand-ag-bm-modify-hq -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3197, E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `bm-modify-hq` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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
		modify self.item 'm1'
			match u 'x2'
				m0(x) then x.fin(1)
				m1(y) then self.hq = y
			end 'x2'
		end 'm1'
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
error E3197: <fragment>:83:16: cannot write 'hq' through 'self', which is read-only while 'm1' is open
```

<!-- test: model-hand-ag-mi-modify-merge-keep -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `mi-modify-merge-keep` of redteamAG.hand_ag15, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mi
	m0(x Rl)
	m1(y isolated Bx)
end 'Mi'

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

type Hf
	var f as immutable Bx
	var keep as Bx

	static function create(f immutable Bx, keep Bx) returns Self
		return Self{f: f, keep: keep}
	end 'create'
end 'Hf'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeI(b isolated Bx)
		b.n = 1
	end 'takeI'
end 'Sk'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let _ = Hf.create(freeze (Bx.create(0)), keep: Bx.create(1)) as isolated Hf
	let _ = spawn Sk.create(0)
	let u = Mi.m1(Bx.create(1)) as isolated Mi
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.keep = merge y
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-mi-beh-merge-keep -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `mi-beh-merge-keep` of redteamAG.hand_ag15, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mi
	m0(x Rl)
	m1(y isolated Bx)
end 'Mi'

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

type Hf
	var f as immutable Bx
	var keep as Bx

	static function create(f immutable Bx, keep Bx) returns Self
		return Self{f: f, keep: keep}
	end 'create'
end 'Hf'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeI(b isolated Bx)
		b.n = 1
	end 'takeI'
end 'Sk'

type Bm
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function eat(u Mi, sk Sk.handle)
		let _ = sk
		let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
		let _ = Hf.create(freeze (Bx.create(0)), keep: Bx.create(1)) as isolated Hf
		modify h 'm1'
			match u 'x2'
				m0(x) then x.fin(1)
				m1(y) then h.keep = merge y
			end 'x2'
		end 'm1'
	end 'eat'
end 'Bm'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function eatMi(p Mi)
	match p 'x1'
		m0(x) then x.fin(1)
		m1(y) then takeB(y)
	end 'x1'
end 'eatMi'

function main() returns ExitCode
	let sk = spawn Sk.create(0)
	let k = spawn Bm.create(0)
	try k.eat(Mi.m1(Bx.create(1)), sk: sk.clone()) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z, _) then eatMi(z)
			serviceStopped(z, _) then eatMi(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-mp-modify-freeze -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `mp-modify-freeze` of redteamAG.hand_ag15, in capmodel `reggio/REPORTRTAG.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hf
	var f as immutable Bx
	var keep as Bx

	static function create(f immutable Bx, keep Bx) returns Self
		return Self{f: f, keep: keep}
	end 'create'
end 'Hf'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let _ = Hf.create(freeze (Bx.create(0)), keep: Bx.create(1)) as isolated Hf
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then let _ = freeze y
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-mp-modify-merge -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `mp-modify-merge` of redteamAG.hand_ag15, in capmodel `reggio/REPORTRTAG.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hf
	var f as immutable Bx
	var keep as Bx

	static function create(f immutable Bx, keep Bx) returns Self
		return Self{f: f, keep: keep}
	end 'create'
end 'Hf'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let _ = Hf.create(freeze (Bx.create(0)), keep: Bx.create(1)) as isolated Hf
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.keep = merge y
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-mr-modify-freeze-part -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `mr-modify-freeze-part` of redteamAG.hand_ag15, in capmodel `reggio/REPORTRTAG.txt`.

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

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))) as isolated Mr
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then let _ = freeze y
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-f1-keep -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f1-keep` of redteamAG.hand_ag16, in capmodel `reggio/REPORTRTAG.txt`.

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
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keepIn(y, w: h)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-f1-part-into-window -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f1-part-into-window` of redteamAG.hand_ag16, in capmodel `reggio/REPORTRTAG.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))) as isolated Mr
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.keep = y.p
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-f1-linear-wrap -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f1-linear-wrap` of redteamAG.hand_ag16, in capmodel `reggio/REPORTRTAG.txt`.

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
	let h = Bx.create(0) as isolated Bx
	let u = Mx.m0(Rl.create(1)) as isolated Mx
	modify h 'm1'
		match u 'x2'
			m0(x) then useS(x.wrap())
			m1(y) then takeB(y)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-f1-fn-param -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f1-fn-param` of redteamAG.hand_ag16, in capmodel `reggio/REPORTRTAG.txt`.

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

function f(u isolated Mp)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keepIn(y, w: h)
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Mp.m1(Bx.create(1)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-f1-control-iso-payload -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `f1-control-iso-payload` of redteamAG.hand_ag16, in capmodel `reggio/REPORTRTAG.txt`.

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
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mx.m1(Bx.create(1)) as isolated Mx
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keepIn(y, w: h)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:58:22: 'y' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ag-f1-control-plain-var -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `f1-control-plain-var` of redteamAG.hand_ag16, in capmodel `reggio/REPORTRTAG.txt`.

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
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mp.m1(Bx.create(1))
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keepIn(y, w: h)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:58:22: 'keepIn' needs a writable parameter 'b' at line 49; 'y' is read-only while 'm1' is open
```

<!-- test: model-hand-ag-y-modify-store -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-modify-store` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hw
	var arr as BxArray
	var k as Bx

	static function create(arr BxArray, k Bx) returns Self
		return Self{arr: arr, k: k}
	end 'create'
end 'Hw'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let _ = Hw.create(BxArray.create(0), k: Bx.create(2)) as isolated Hw
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.keep = y
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-y-modifyhw-store -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-modifyhw-store` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hw
	var arr as BxArray
	var k as Bx

	static function create(arr BxArray, k Bx) returns Self
		return Self{arr: arr, k: k}
	end 'create'
end 'Hw'

function main() returns ExitCode
	let _ = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let hw = Hw.create(BxArray.create(0), k: Bx.create(2)) as isolated Hw
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify hw 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then hw.k = y
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-y-modify-keepfn -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-modify-keepfn` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hw
	var arr as BxArray
	var k as Bx

	static function create(arr BxArray, k Bx) returns Self
		return Self{arr: arr, k: k}
	end 'create'
end 'Hw'

function keepIn(b Bx, w Wo)
	w.keep = b
end 'keepIn'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let _ = Hw.create(BxArray.create(0), k: Bx.create(2)) as isolated Hw
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keepIn(y, w: h)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-y-modifyhw-keepfn -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-modifyhw-keepfn` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hw
	var arr as BxArray
	var k as Bx

	static function create(arr BxArray, k Bx) returns Self
		return Self{arr: arr, k: k}
	end 'create'
end 'Hw'

function keepInArr(b Bx, w Hw)
	w.arr.push(b)
end 'keepInArr'

function main() returns ExitCode
	let _ = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let hw = Hw.create(BxArray.create(0), k: Bx.create(2)) as isolated Hw
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify hw 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keepInArr(y, w: hw)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-y-modify-push-arr -->
The model (`core-m3w-fa24`) refuses it by `C-ISOREAD`; the compiler refuses it with E3196. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `y-modify-push-arr` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hw
	var arr as BxArray
	var k as Bx

	static function create(arr BxArray, k Bx) returns Self
		return Self{arr: arr, k: k}
	end 'create'
end 'Hw'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let hw = Hw.create(BxArray.create(0), k: Bx.create(2)) as isolated Hw
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then hw.arr.push(y)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:73:27: cannot store a read-only 'Bx' into 'an element of the container push writes' while 'the isolated value' is open; store a copy
```

<!-- test: model-hand-ag-y-modifyhw-push-arr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-modifyhw-push-arr` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hw
	var arr as BxArray
	var k as Bx

	static function create(arr BxArray, k Bx) returns Self
		return Self{arr: arr, k: k}
	end 'create'
end 'Hw'

function main() returns ExitCode
	let _ = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let hw = Hw.create(BxArray.create(0), k: Bx.create(2)) as isolated Hw
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify hw 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then hw.arr.push(y)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-y-modify-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-modify-none` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hw
	var arr as BxArray
	var k as Bx

	static function create(arr BxArray, k Bx) returns Self
		return Self{arr: arr, k: k}
	end 'create'
end 'Hw'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let _ = Hw.create(BxArray.create(0), k: Bx.create(2)) as isolated Hw
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1 then break
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-y-modifyhw-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-modifyhw-none` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hw
	var arr as BxArray
	var k as Bx

	static function create(arr BxArray, k Bx) returns Self
		return Self{arr: arr, k: k}
	end 'create'
end 'Hw'

function main() returns ExitCode
	let _ = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let hw = Hw.create(BxArray.create(0), k: Bx.create(2)) as isolated Hw
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify hw 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1 then break
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-hq-modify-store-p -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hq-modify-store-p` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

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

type Hw
	var arr as BxArray
	var k as Bx

	static function create(arr BxArray, k Bx) returns Self
		return Self{arr: arr, k: k}
	end 'create'
end 'Hw'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let _ = Hw.create(BxArray.create(0), k: Bx.create(2)) as isolated Hw
	let u = Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))) as isolated Mr
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.keep = y.p
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-hq-modify-store-q -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hq-modify-store-q` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

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

type Hw
	var arr as BxArray
	var k as Bx

	static function create(arr BxArray, k Bx) returns Self
		return Self{arr: arr, k: k}
	end 'create'
end 'Hw'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let _ = Hw.create(BxArray.create(0), k: Bx.create(2)) as isolated Hw
	let u = Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))) as isolated Mr
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.keep = y.q
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-hq-modify-keepfn-p -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hq-modify-keepfn-p` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

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

type Hw
	var arr as BxArray
	var k as Bx

	static function create(arr BxArray, k Bx) returns Self
		return Self{arr: arr, k: k}
	end 'create'
end 'Hw'

function keepIn(b Bx, w Wo)
	w.keep = b
end 'keepIn'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let _ = Hw.create(BxArray.create(0), k: Bx.create(2)) as isolated Hw
	let u = Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))) as isolated Mr
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keepIn(y.p, w: h)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-t-modify-store-a -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `t-modify-store-a` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mt
	m0(x Rl)
	m1(a Bx, c Hq)
end 'Mt'

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

type Hw
	var arr as BxArray
	var k as Bx

	static function create(arr BxArray, k Bx) returns Self
		return Self{arr: arr, k: k}
	end 'create'
end 'Hw'

function mkT() returns Mt
	let bx = Bx.create(1)
	return Mt.m1(bx, c: Hq.create(bx, q: bx))
end 'mkT'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let _ = Hw.create(BxArray.create(0), k: Bx.create(2)) as isolated Hw
	let u = mkT() as isolated Mt
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(a, _) then h.keep = a
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-t-modify-store-cp -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `t-modify-store-cp` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mt
	m0(x Rl)
	m1(a Bx, c Hq)
end 'Mt'

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

type Hw
	var arr as BxArray
	var k as Bx

	static function create(arr BxArray, k Bx) returns Self
		return Self{arr: arr, k: k}
	end 'create'
end 'Hw'

function mkT() returns Mt
	let bx = Bx.create(1)
	return Mt.m1(bx, c: Hq.create(bx, q: bx))
end 'mkT'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let _ = Hw.create(BxArray.create(0), k: Bx.create(2)) as isolated Hw
	let u = mkT() as isolated Mt
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(_, c) then h.keep = c.p
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-k3-nested-windows-both -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `k3-nested-windows-both` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hw
	var arr as BxArray
	var k as Bx

	static function create(arr BxArray, k Bx) returns Self
		return Self{arr: arr, k: k}
	end 'create'
end 'Hw'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let _ = Hw.create(BxArray.create(0), k: Bx.create(2)) as isolated Hw
	let g = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify g 'm1'
		modify h 'm2'
			match u 'x3'
				m0(x) then x.fin(1)
				m1(y) then h.keep = y
			end 'x3'
		end 'm2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:72:10: 'h' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ag-k4-nested-windows-outer-store -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `k4-nested-windows-outer-store` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hw
	var arr as BxArray
	var k as Bx

	static function create(arr BxArray, k Bx) returns Self
		return Self{arr: arr, k: k}
	end 'create'
end 'Hw'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let _ = Hw.create(BxArray.create(0), k: Bx.create(2)) as isolated Hw
	let g = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify g 'm1'
		modify h 'm2'
			match u 'x3'
				m0(x) then x.fin(1)
				m1(y) then g.keep = y
			end 'x3'
		end 'm2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:72:10: 'h' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ag-beh-win-mr-modify-item-store -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `beh-win-mr-modify-item-store` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
	var hq as Hq

	static function create(n Num, hq isolated Hq) returns Self
		return Self{n: n, hq: merge hq}
	end 'create'

	export function eat(u Mr)
		let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
		modify h 'm1'
			match u 'x2'
				m0(x) then x.fin(1)
				m1(y) then self.hq.p = y.p
			end 'x2'
		end 'm1'
	end 'eat'
end 'Bm'

function eatMr(pV Mr)
	match pV 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.q.n = 1
	end 'x1'
end 'eatMr'

function main() returns ExitCode
	let k = spawn Bm.create(0, hq: Hq.create(Bx.create(1), q: Bx.create(1)))
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
error E3197: <fragment>:78:16: cannot write 'p' through 'self.hq', which is read-only while 'm1' is open
```

<!-- test: model-hand-ag-fn-mr-keep -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fn-mr-keep` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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

function eat(u isolated Mr)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.keep = y.p
		end 'x2'
	end 'm1'
end 'eat'

function main() returns ExitCode
	eat(Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-fn-plain-mr-keep -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3196. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `fn-plain-mr-keep` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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

function eat(u Mr)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.keep = y.p
		end 'x2'
	end 'm1'
end 'eat'

function main() returns ExitCode
	eat(Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))))
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:70:24: cannot store a read-only 'Bx' into 'keep' while 'm1' is open; store a copy
```

<!-- test: model-hand-ag-fn-mr-keepfn -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fn-mr-keepfn` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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

function keepIn(b Bx, w Wo)
	w.keep = b
end 'keepIn'

function eat(u isolated Mr)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keepIn(y.p, w: h)
		end 'x2'
	end 'm1'
end 'eat'

function main() returns ExitCode
	eat(Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-fn-plain-mr-keepfn -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `fn-plain-mr-keepfn` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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

function keepIn(b Bx, w Wo)
	w.keep = b
end 'keepIn'

function eat(u Mr)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keepIn(y.p, w: h)
		end 'x2'
	end 'm1'
end 'eat'

function main() returns ExitCode
	eat(Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))))
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:74:22: 'keepIn' needs a writable parameter 'b' at line 66; 'y' is read-only while 'm1' is open
```

<!-- test: model-hand-ag-fn-plain-mr-write -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3197. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `fn-plain-mr-write` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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

function eat(u Mr)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then y.q.n = 5
		end 'x2'
	end 'm1'
end 'eat'

function main() returns ExitCode
	eat(Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))))
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:70:15: cannot write 'n' through 'y.q', which is read-only while 'm1' is open
```

<!-- test: model-hand-ag-p-pass-store -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-pass-store` of redteamAG.hand_ag20, in capmodel `reggio/REPORTRTAG.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

function pass(b Bx) returns Bx
	return b
end 'pass'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.keep = pass(y)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-p-pick-store -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-pick-store` of redteamAG.hand_ag20, in capmodel `reggio/REPORTRTAG.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

function lg(x Num) returns Num
	return x
end 'lg'

function pick(a Bx, b Bx, c Num) returns Bx
	if c != 0 'i1'
		return a
	end 'i1' else 'e2'
		return b
	end 'e2'
end 'pick'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.keep = pick(y, b: Bx.create(3), c: lg(1))
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-p-pick-store-other -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-pick-store-other` of redteamAG.hand_ag20, in capmodel `reggio/REPORTRTAG.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

function lg(x Num) returns Num
	return x
end 'lg'

function pick(a Bx, b Bx, c Num) returns Bx
	if c != 0 'i1'
		return a
	end 'i1' else 'e2'
		return b
	end 'e2'
end 'pick'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.keep = pick(Bx.create(3), b: y, c: lg(1))
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-p-cond-store -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-cond-store` of redteamAG.hand_ag20, in capmodel `reggio/REPORTRTAG.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.keep = (y if lg(1) != 0 else y)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-p-usehq-new -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-useHq-new` of redteamAG.hand_ag20, in capmodel `reggio/REPORTRTAG.txt`.

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

function useHq(h Hq)
	h.q = h.p
end 'useHq'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then useHq(Hq.create(y, q: Bx.create(2)))
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-l1-wrap-in-modify -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l1-wrap-in-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

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
	let h = Hx.create(0) as isolated Hx
	let u = Mx.m0(Rl.create(1)) as isolated Mx
	modify h 'm1'
		match u 'x2'
			m0(x) then useS(x.wrap())
			m1(y) then takeB(y)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-l2-gives-in-modify -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l2-giveS-in-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

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
	let h = Hx.create(0) as isolated Hx
	let u = Mx.m0(Rl.create(1)) as isolated Mx
	modify h 'm1'
		match u 'x2'
			m0(x) then useS(giveS(x))
			m1(y) then takeB(y)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-l3-mksn-in-modify -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l3-mkSn-in-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function mkSn(r Rl) returns Sn
	return Sn.notSent(r)
end 'mkSn'

function main() returns ExitCode
	let h = Hx.create(0) as isolated Hx
	let u = Mx.m0(Rl.create(1)) as isolated Mx
	modify h 'm1'
		match u 'x2'
			m0(x) then useS(mkSn(x))
			m1(y) then takeB(y)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-l4-fin-in-modify -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l4-fin-in-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let h = Hx.create(0) as isolated Hx
	let u = Mx.m0(Rl.create(1)) as isolated Mx
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then takeB(y)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-l6-wrap-fn-param-in-modify -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l6-wrap-fn-param-in-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function f(u isolated Mx)
	let h = Hx.create(0) as isolated Hx
	modify h 'm1'
		match u 'x2'
			m0(x) then useS(x.wrap())
			m1(y) then takeB(y)
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Mx.m0(Rl.create(1)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-l7-wrap-behaviour-param-in-modify -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l7-wrap-behaviour-param-in-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

type Bm
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function eat(u Mx)
		let h = Hx.create(0) as isolated Hx
		modify h 'm1'
			match u 'x2'
				m0(x) then useS(x.wrap())
				m1(y) then takeB(y)
			end 'x2'
		end 'm1'
	end 'eat'
end 'Bm'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function eatMx(p Mx)
	match p 'x1'
		m0(x) then x.fin(1)
		m1(y) then takeB(y)
	end 'x1'
end 'eatMx'

function main() returns ExitCode
	let q = spawn Bm.create(0)
	try q.eat(Mx.m0(Rl.create(1))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z) then eatMx(z)
			serviceStopped(z) then eatMx(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-l8-wrap-in-modify-state-slot -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l8-wrap-in-modify-state-slot` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

type Bm
	var n as Num
	var h as isolated Hx

	static function create(n Num, h isolated Hx) returns Self
		return Self{n: n, h: h}
	end 'create'

	export function eat(u Mx)
		modify self.h 'm1'
			match u 'x2'
				m0(x) then useS(x.wrap())
				m1(y) then takeB(y)
			end 'x2'
		end 'm1'
	end 'eat'
end 'Bm'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function eatMx(p Mx)
	match p 'x1'
		m0(x) then x.fin(1)
		m1(y) then takeB(y)
	end 'x1'
end 'eatMx'

function main() returns ExitCode
	let q = spawn Bm.create(0, h: Hx.create(0))
	try q.eat(Mx.m0(Rl.create(1))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z) then eatMx(z)
			serviceStopped(z) then eatMx(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-l10-wrap-in-nested-modify -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `l10-wrap-in-nested-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

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
	let h = Hx.create(0) as isolated Hx
	let g = Hx.create(1) as isolated Hx
	let u = Mx.m0(Rl.create(1)) as isolated Mx
	modify h 'm1'
		modify g 'm2'
			match u 'x3'
				m0(x) then useS(x.wrap())
				m1(y) then takeB(y)
			end 'x3'
		end 'm2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:71:10: 'g' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ag-l14-plain-var-union-in-modify -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `l14-plain-var-union-in-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

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
	let h = Hx.create(0) as isolated Hx
	let u = Mx.m0(Rl.create(1))
	modify h 'm1'
		match u 'x2'
			m0(x) then useS(x.wrap())
			m1(y) then takeB(y)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:71:20: 'wrap' needs a writable receiver at line 35; 'x' is read-only while 'm1' is open
```

<!-- test: model-hand-ag-l15-wn-union-plain-linear -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l15-wn-union-plain-linear` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Wn
	w0
	w1(r Rl)
end 'Wn'

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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let h = Hx.create(0) as isolated Hx
	let u = Wn.w1(Rl.create(1)) as isolated Wn
	modify h 'm1'
		match u 'x2'
			w0 then break
			w1(r) then useS(r.wrap())
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-l17-plain-nonlinear-new-record-pushed -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l17-plain-nonlinear-new-record-pushed` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias HpArray = Array with Hp

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mq
	m0(x Rl)
	m1(y Bx)
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

type Hp
	var p as Bx

	static function create(p Bx) returns Self
		return Self{p: p}
	end 'create'
end 'Hp'

type Hw
	var xs as HpArray

	static function create(xs HpArray) returns Self
		return Self{xs: xs}
	end 'create'
end 'Hw'

function main() returns ExitCode
	let h = Hw.create(HpArray.create(0)) as isolated Hw
	let u = Mq.m1(Bx.create(1)) as isolated Mq
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.xs.push(Hp.create(y))
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-l18-plain-nonlinear-returned-from-modify -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `l18-plain-nonlinear-returned-from-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mq
	m0(x Rl)
	m1(y Bx)
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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let h = Hx.create(0) as isolated Hx
	let u = Mq.m1(Bx.create(1)) as isolated Mq
	var out = Bx.create(0)
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then out = y
		end 'x2'
	end 'm1'
	out.n = 5
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:62:15: 'out' holds a record from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-hand-ag-l19-plain-nonlinear-into-outer-var -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `l19-plain-nonlinear-into-outer-var` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mq
	m0(x Rl)
	m1(y Bx)
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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let h = Hx.create(0) as isolated Hx
	let u = Mq.m1(Bx.create(1)) as isolated Mq
	let out = Bx.create(0)
	let keep = BxArray.create(0)
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keep.push(y)
		end 'x2'
	end 'm1'
	out.n = 5
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:64:20: cannot write through a read-only value while 'm1' is open
```

<!-- test: model-hand-ag-l20-plain-nonlinear-into-outer-var-then-used-after -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `l20-plain-nonlinear-into-outer-var-then-used-after` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mq
	m0(x Rl)
	m1(y Bx)
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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let h = Hx.create(0) as isolated Hx
	let u = Mq.m1(Bx.create(1)) as isolated Mq
	let keep = BxArray.create(0)
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keep.push(y)
		end 'x2'
	end 'm1'
	for k in keep 'f3'
		k.n = 5
	end 'f3'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:63:20: cannot write through a read-only value while 'm1' is open
```

<!-- test: model-hand-ag-l21-plain-nonlinear-into-outer-record-field -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `l21-plain-nonlinear-into-outer-record-field` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mq
	m0(x Rl)
	m1(y Bx)
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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let h = Hx.create(0) as isolated Hx
	let u = Mq.m1(Bx.create(1)) as isolated Mq
	let o = Wo.create(Bx.create(0), item: Bx.create(1))
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then o.keep = y
		end 'x2'
	end 'm1'
	o.keep.n = 5
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:71:15: cannot write 'keep' through 'o', which is read-only while 'm1' is open
```

<!-- test: model-hand-ag-l22-plain-nonlinear-into-outer-record-field-via-fn -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `l22-plain-nonlinear-into-outer-record-field-via-fn` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mq
	m0(x Rl)
	m1(y Bx)
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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

function keepIn(b Bx, w Wo)
	w.keep = b
end 'keepIn'

function main() returns ExitCode
	let h = Hx.create(0) as isolated Hx
	let u = Mq.m1(Bx.create(1)) as isolated Mq
	let o = Wo.create(Bx.create(0), item: Bx.create(1))
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keepIn(y, w: o)
		end 'x2'
	end 'm1'
	o.keep.n = 5
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:75:25: 'keepIn' needs a writable parameter 'w' at line 65; 'o' is read-only while 'm1' is open
```

<!-- test: model-hand-ag-l24-plain-payload-frozen -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l24-plain-payload-frozen` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Sn
	ok
	notSent(r Rl)
end 'Sn'

union Mq
	m0(x Rl)
	m1(y Bx)
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

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

function main() returns ExitCode
	let h = Hx.create(0) as isolated Hx
	let u = Mq.m1(Bx.create(1)) as isolated Mq
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then let _ = freeze y
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-win-copy-no -->
The model (`core-m3w-fa24`) refuses it by `C-SLOT`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f2-win-copy-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

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

function main() returns ExitCode
	let h = Hd.create(UT.have(Tk.create(1))) as isolated Hd
	modify h 'm1'
		match h.u 'x2'
			none then break
			have then let _ = h
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:30:22: cannot move or copy the unique value matched by 'h.u' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```

<!-- test: model-hand-ah-f2-win-call-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-win-call-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

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

function eatHd(x Hd)
	match x.u 'x1'
		none then break
		have(t) then t.n = 3
	end 'x1'
end 'eatHd'

function main() returns ExitCode
	let h = Hd.create(UT.have(Tk.create(1))) as isolated Hd
	modify h 'm1'
		match h.u 'x2'
			none then break
			have then eatHd(h)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-win-assignslot-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-win-assignslot-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

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

function main() returns ExitCode
	let h = Hd.create(UT.have(Tk.create(1))) as isolated Hd
	modify h 'm1'
		match h.u 'x2'
			none then break
			have then h.u = UT.none
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-win-copyslot-no -->
The model (`core-m3w-fa24`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `f2-win-copyslot-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

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

function main() returns ExitCode
	let h = Hd.create(UT.have(Tk.create(1))) as isolated Hd
	modify h 'm1'
		match h.u 'x2'
			none then break
			have then let _ = h.u
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:30:18: cannot bind '_' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: model-hand-ah-f2-win-nothing-rd -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-win-nothing-rd` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

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

function main() returns ExitCode
	let h = Hd.create(UT.have(Tk.create(1))) as isolated Hd
	modify h 'm1'
		match h.u 'x2'
			none then break
			have(t) then let _ = t.n
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-win-nothing-wr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-win-nothing-wr` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

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

function main() returns ExitCode
	let h = Hd.create(UT.have(Tk.create(1))) as isolated Hd
	modify h 'm1'
		match h.u 'x2'
			none then break
			have(t) then t.n = 5
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-f2-win-nothing-no -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-win-nothing-no` of redteamAH.hand_ah1, in capmodel `reggio/REPORTRTAH.txt`.

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

function main() returns ExitCode
	let h = Hd.create(UT.have(Tk.create(1))) as isolated Hd
	modify h 'm1'
		match h.u 'x2'
			none then break
			have then break
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-m-cond-store -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-cond-store` of redteamAH.hand_ah3, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Hp
	var p as Bx

	static function create(p Bx) returns Self
		return Self{p: p}
	end 'create'
end 'Hp'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(w isolated Wo)
		let _ = w.keep.n
	end 'takeW'
end 'Sk'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let _ = BxArray.create(0)
	let _ = Hp.create(Bx.create(0))
	let _ = spawn Sk.create(0)
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.keep = (y if lg(1) != 0 else y)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-m-svc-wk -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `m-svc-wk` of redteamAH.hand_ah3, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(wV isolated Wo)
		let _ = wV.keep.n
	end 'takeW'
end 'Sk'

type HldW
	var n as Num
	var plain as Bx
	var item as isolated Bx
	var w as isolated Wo

	static function create(n Num, plain isolated Bx, item isolated Bx, w isolated Wo) returns Self
		return Self{n: n, plain: merge plain, item: item, w: w}
	end 'create'

	function keepP(b Bx)
		self.plain = b
	end 'keepP'

	export function go(u Mp, k2 Sk.handle)
		let _ = k2
		modify self.w 'm1'
			match u 'x2'
				m0(x) then x.fin(1)
				m1(y) then self.keepP(y)
			end 'x2'
		end 'm1'
	end 'go'
end 'HldW'

function eatMp(p Mp)
	match p 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.n = 1
	end 'x1'
end 'eatMp'

function main() returns ExitCode
	let k = spawn HldW.create(0, plain: Bx.create(0), item: Bx.create(1), w: Wo.create(Bx.create(0), item: Bx.create(1)))
	let sk = spawn Sk.create(0)
	try k.go(Mp.m1(Bx.create(1)), k2: sk.clone()) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z, _) then eatMp(z)
			serviceStopped(z, _) then eatMp(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:91:16: 'keepP' needs a writable receiver at line 83; 'self' is read-only while 'm1' is open
```

<!-- test: model-hand-ah-m-svc-wi -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `m-svc-wi` of redteamAH.hand_ah3, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(wV isolated Wo)
		let _ = wV.keep.n
	end 'takeW'
end 'Sk'

type HldW
	var n as Num
	var plain as Bx
	var item as isolated Bx
	var w as isolated Wo

	static function create(n Num, plain isolated Bx, item isolated Bx, w isolated Wo) returns Self
		return Self{n: n, plain: merge plain, item: item, w: w}
	end 'create'

	function keepP(b Bx)
		self.plain = b
	end 'keepP'

	export function go(u Mp, k2 Sk.handle)
		let _ = k2
		modify self.w 'm1'
			match u 'x2'
				m0(x) then x.fin(1)
				m1(y) then self.item = y
			end 'x2'
		end 'm1'
	end 'go'
end 'HldW'

function eatMp(p Mp)
	match p 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.n = 1
	end 'x1'
end 'eatMp'

function main() returns ExitCode
	let k = spawn HldW.create(0, plain: Bx.create(0), item: Bx.create(1), w: Wo.create(Bx.create(0), item: Bx.create(1)))
	let sk = spawn Sk.create(0)
	try k.go(Mp.m1(Bx.create(1)), k2: sk.clone()) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z, _) then eatMp(z)
			serviceStopped(z, _) then eatMp(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:91:16: cannot write 'item' through 'self', which is read-only while 'm1' is open
```

<!-- test: model-hand-ah-m-call-same -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-call-same` of redteamAH.hand_ah3, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

function f(w Wo, u isolated Mp)
	match u 'x1'
		m0(x) then x.fin(1)
		m1(y) then w.keep = y
	end 'x1'
end 'f'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	f(h, u: u)
	modify h 'm1'
		h.keep.n = 4
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-m-call-nested-open -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `m-call-nested-open` of redteamAH.hand_ah3, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

function f(w Wo, u isolated Mp)
	match u 'x1'
		m0(x) then x.fin(1)
		m1(y) then w.keep = y
	end 'x1'
end 'f'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let g = Wo.create(Bx.create(2), item: Bx.create(3)) as isolated Wo
	modify h 'm1'
		f(g, u: Mp.m1(Bx.create(1)))
		h.keep.n = 3
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:67:5: 'g' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ah-w-modify-field-store -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-modify-field-store` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type HsW
	var item as isolated Wo

	static function create(item isolated Wo) returns Self
		return Self{item: item}
	end 'create'
end 'HsW'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(w isolated Wo)
		let _ = w.keep.n
	end 'takeW'
end 'Sk'

function main() returns ExitCode
	let _ = spawn Sk.create(0)
	let hs = HsW.create(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated HsW
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify hs.item 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then hs.item.keep = y
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-w-modify-field-keepfn -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-modify-field-keepfn` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type HsW
	var item as isolated Wo

	static function create(item isolated Wo) returns Self
		return Self{item: item}
	end 'create'
end 'HsW'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(w isolated Wo)
		let _ = w.keep.n
	end 'takeW'
end 'Sk'

function keepIn(b Bx, w Wo)
	w.keep = b
end 'keepIn'

function main() returns ExitCode
	let _ = spawn Sk.create(0)
	let hs = HsW.create(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated HsW
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify hs.item 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keepIn(y, w: hs.item)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-w-modify-field-store-iso-slot -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-modify-field-store-iso-slot` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type HsW
	var item as isolated Wo

	static function create(item isolated Wo) returns Self
		return Self{item: item}
	end 'create'
end 'HsW'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(w isolated Wo)
		let _ = w.keep.n
	end 'takeW'
end 'Sk'

function main() returns ExitCode
	let _ = spawn Sk.create(0)
	let hs = HsW.create(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated HsW
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify hs.item 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then hs.item.item = y
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-w-modify-field-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-modify-field-take` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type HsW
	var item as isolated Wo

	static function create(item isolated Wo) returns Self
		return Self{item: item}
	end 'create'
end 'HsW'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(w isolated Wo)
		let _ = w.keep.n
	end 'takeW'
end 'Sk'

function takeBx(b isolated Bx)
	let _ = b.n
end 'takeBx'

function main() returns ExitCode
	let _ = spawn Sk.create(0)
	let hs = HsW.create(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated HsW
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify hs.item 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then takeBx(y)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-w-modify-store -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-modify-store` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(w isolated Wo)
		let _ = w.keep.n
	end 'takeW'
end 'Sk'

function main() returns ExitCode
	let _ = spawn Sk.create(0)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.keep = y
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-w-modify-keepfn -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-modify-keepfn` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(w isolated Wo)
		let _ = w.keep.n
	end 'takeW'
end 'Sk'

function keepIn(b Bx, w Wo)
	w.keep = b
end 'keepIn'

function main() returns ExitCode
	let _ = spawn Sk.create(0)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keepIn(y, w: h)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-w-modify-store-iso-slot -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-modify-store-iso-slot` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(w isolated Wo)
		let _ = w.keep.n
	end 'takeW'
end 'Sk'

function main() returns ExitCode
	let _ = spawn Sk.create(0)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.item = y
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-w-modify-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-modify-take` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(w isolated Wo)
		let _ = w.keep.n
	end 'takeW'
end 'Sk'

function takeBx(b isolated Bx)
	let _ = b.n
end 'takeBx'

function main() returns ExitCode
	let _ = spawn Sk.create(0)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then takeBx(y)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-w-nested-modify-modify-outer-store -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `w-nested-modify-modify-outer-store` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(w isolated Wo)
		let _ = w.keep.n
	end 'takeW'
end 'Sk'

function main() returns ExitCode
	let _ = spawn Sk.create(0)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let g = Wo.create(Bx.create(2), item: Bx.create(3)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify g 'm1'
		modify h 'm2'
			match u 'x3'
				m0(x) then x.fin(1)
				m1(y) then g.keep = y
			end 'x3'
		end 'm2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:78:10: 'h' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ah-w-nested-modify-modify-outer-keepfn -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `w-nested-modify-modify-outer-keepfn` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(w isolated Wo)
		let _ = w.keep.n
	end 'takeW'
end 'Sk'

function keepIn(b Bx, w Wo)
	w.keep = b
end 'keepIn'

function main() returns ExitCode
	let _ = spawn Sk.create(0)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let g = Wo.create(Bx.create(2), item: Bx.create(3)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify g 'm1'
		modify h 'm2'
			match u 'x3'
				m0(x) then x.fin(1)
				m1(y) then keepIn(y, w: g)
			end 'x3'
		end 'm2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:82:10: 'h' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ah-w-nested-modify-modify-outer-write -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `w-nested-modify-modify-outer-write` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(w isolated Wo)
		let _ = w.keep.n
	end 'takeW'
end 'Sk'

function main() returns ExitCode
	let _ = spawn Sk.create(0)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let g = Wo.create(Bx.create(2), item: Bx.create(3)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify g 'm1'
		modify h 'm2'
			match u 'x3'
				m0(x) then x.fin(1)
				m1(y) then y.n = 3
			end 'x3'
		end 'm2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:78:10: 'h' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ah-w-nested-modify-modify-outer-store-iso-slot -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `w-nested-modify-modify-outer-store-iso-slot` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(w isolated Wo)
		let _ = w.keep.n
	end 'takeW'
end 'Sk'

function main() returns ExitCode
	let _ = spawn Sk.create(0)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let g = Wo.create(Bx.create(2), item: Bx.create(3)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify g 'm1'
		modify h 'm2'
			match u 'x3'
				m0(x) then x.fin(1)
				m1(y) then g.item = y
			end 'x3'
		end 'm2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:78:10: 'h' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ah-w-nested-modify-modify-outer-take -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `w-nested-modify-modify-outer-take` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(w isolated Wo)
		let _ = w.keep.n
	end 'takeW'
end 'Sk'

function takeBx(b isolated Bx)
	let _ = b.n
end 'takeBx'

function main() returns ExitCode
	let _ = spawn Sk.create(0)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let g = Wo.create(Bx.create(2), item: Bx.create(3)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify g 'm1'
		modify h 'm2'
			match u 'x3'
				m0(x) then x.fin(1)
				m1(y) then takeBx(y)
			end 'x3'
		end 'm2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:82:10: 'h' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ah-w-beh-store-iso-slot -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `w-beh-store-iso-slot` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(wV isolated Wo)
		let _ = wV.keep.n
	end 'takeW'
end 'Sk'

type Bw
	var n as Num
	var w as isolated Wo

	static function create(n Num, w isolated Wo) returns Self
		return Self{n: n, w: w}
	end 'create'

	export function go(u Mp, k Sk.handle)
		let _ = k
		modify self.w 'm1'
			match u 'x2'
				m0(x) then x.fin(1)
				m1(y) then self.w.item = y
			end 'x2'
		end 'm1'
	end 'go'
end 'Bw'

function eatMp(p Mp)
	match p 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.n = 1
	end 'x1'
end 'eatMp'

function main() returns ExitCode
	let b = spawn Bw.create(0, w: Wo.create(Bx.create(0), item: Bx.create(1)))
	let sk = spawn Sk.create(0)
	try b.go(Mp.m1(Bx.create(1)), k: sk.clone()) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z, _) then eatMp(z)
			serviceStopped(z, _) then eatMp(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:85:30: 'y' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Bx`, or store a copy
```

<!-- test: model-hand-ah-w-beh-take -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `w-beh-take` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(wV isolated Wo)
		let _ = wV.keep.n
	end 'takeW'
end 'Sk'

type Bw
	var n as Num
	var w as isolated Wo

	static function create(n Num, w isolated Wo) returns Self
		return Self{n: n, w: w}
	end 'create'

	export function go(u Mp, k Sk.handle)
		let _ = k
		modify self.w 'm1'
			match u 'x2'
				m0(x) then x.fin(1)
				m1(y) then takeBx(y)
			end 'x2'
		end 'm1'
	end 'go'
end 'Bw'

function eatMp(p Mp)
	match p 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.n = 1
	end 'x1'
end 'eatMp'

function takeBx(b isolated Bx)
	let _ = b.n
end 'takeBx'

function main() returns ExitCode
	let b = spawn Bw.create(0, w: Wo.create(Bx.create(0), item: Bx.create(1)))
	let sk = spawn Sk.create(0)
	try b.go(Mp.m1(Bx.create(1)), k: sk.clone()) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z, _) then eatMp(z)
			serviceStopped(z, _) then eatMp(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:85:23: 'y' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-ah-w-call-arg-match2 -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `w-call-arg-match2` of redteamAH.hand_ah4, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

function put(w Wo, b Bx)
	w.keep = b
end 'put'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let g = Wo.create(Bx.create(2), item: Bx.create(3)) as isolated Wo
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify g 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then put(h, b: y)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:67:19: 'h' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ah-f1-whole-record-into-window-array -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f1-whole-record-into-window-array` of redteamAH.hand_ah5, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

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

type Bag
	var xs as BxArray

	static function create(xs BxArray) returns Self
		return Self{xs: xs}
	end 'create'
end 'Bag'

function main() returns ExitCode
	let h = Bag.create(BxArray.create(0)) as isolated Bag
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.xs.push(y)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-wg-recovered-local-kept-in-window -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `wg-recovered-local-kept-in-window` of redteamAH.hand_ah6, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

function keepPl(b Bx, w Wo)
	w.keep = b
end 'keepPl'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let w = Bx.create(2)
	let u = Mp.m1(w) as isolated Mp
	modify h 'm1'
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keepPl(y, w: h)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-wg-recovered-local-in-window -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `wg-recovered-local-in-window` of redteamAH.hand_ah6, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

function keepPl(b Bx, w Wo)
	w.keep = b
end 'keepPl'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		let w = Bx.create(2)
		let u = Mp.m1(w) as isolated Mp
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then keepPl(y, w: h)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-wg-recovered-local-in-window-outer-alias -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `wg-recovered-local-in-window-outer-alias` of redteamAH.hand_ah6, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let w = Bx.create(2)
	let _ = w
	modify h 'm1'
		let _ = Mp.m1(w) as isolated Mp
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:61:17: cannot store a read-only 'Bx' into 'Mp.m1.y' while 'm1' is open; store a copy
```

<!-- test: model-hand-ah-wg-recover-outer-in-window -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `wg-recover-outer-in-window` of redteamAH.hand_ah6, in capmodel `reggio/REPORTRTAH.txt`.

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

type Wo
	var keep as Bx
	var item as isolated Bx

	static function create(keep Bx, item isolated Bx) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let w = Bx.create(2)
	modify h 'm1'
		let u = Mp.m1(w) as isolated Mp
		match u 'x2'
			m0(x) then x.fin(1)
			m1(y) then h.keep = y
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:60:17: cannot store a read-only 'Bx' into 'Mp.m1.y' while 'm1' is open; store a copy
```

<!-- test: model-hand-ah-wg-recover-outer-send-in-window -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `wg-recover-outer-send-in-window` of redteamAH.hand_ah6, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		let _ = b.n
	end 'take'

	export function takeW(w isolated Wo)
		let _ = w.keep.n
	end 'takeW'
end 'Sk'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let w = Bx.create(2)
	let k = spawn Sk.create(0)
	modify h 'm1'
		try k.take(w) otherwise ignore
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:42:14: 'w' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-ai-i1-for-isolated-array -->
The model (`core-m3w-fa24`) refuses it by `C-LEND`; the compiler refuses it with E3198. Model program `i1-for-isolated-array` of redteamAI.hand_ai4, in capmodel `reggio/REPORTRTAI.txt`.

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
	let a = UbArray.create(0) as isolated UbArray
	modify a 'm1'
		a.push(Ub.u1(Bx.create(1)))
	end 'm1'
	modify a 'm2'
		for e in a 'f3'
			match e 'x4'
				u0 then break
				u1(o) then takeB(o)
			end 'x4'
		end 'f3'
	end 'm2'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:31:22: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-ai-o1-open-then-consume-branches -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `o1-open-then-consume-branches` of redteamAI.hand_ai5, in capmodel `reggio/REPORTRTAI.txt`.

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
	if lg(1) != 0 'i1'
		modify u 'm2'
			match u 'x3'
				u0 then break
				u1(o) then let _ = o.n
			end 'x3'
		end 'm2'
	end 'i1' else 'e4'
		match u 'x5'
			u0 then break
			u1(o) then takeB(o)
		end 'x5'
	end 'e4'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-o2-open-then-consume-branches-read-after -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `o2-open-then-consume-branches-read-after` of redteamAI.hand_ai5, in capmodel `reggio/REPORTRTAI.txt`.

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
		modify u 'm2'
			match u 'x3'
				u0 then break
				u1(o) then let _ = o.n
			end 'x3'
		end 'm2'
	end 'i1' else 'e4'
		match u 'x5'
			u0 then break
			u1(o) then takeB(o)
		end 'x5'
	end 'e4'
	eatUb(u)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:47:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ai-o4-consume-in-own-modify-arm -->
The model (`core-m3w-fa24`) refuses it by `C-LEND`; the compiler refuses it with E3198. Model program `o4-consume-in-own-modify-arm` of redteamAI.hand_ai5, in capmodel `reggio/REPORTRTAI.txt`.

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
	modify u 'm1'
		match u 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:26:21: 'o' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-hand-ai-o7-own-modify-other-var-bare -->
The model (`core-m3w-fa24`) refuses it by `C-SLOT`; the compiler refuses it with E3203. Model program `o7-own-modify-other-var-bare` of redteamAI.hand_ai5, in capmodel `reggio/REPORTRTAI.txt`.

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
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	let w = Ub.u1(Bx.create(1)) as isolated Ub
	modify u 'm1'
		match w 'x2'
			u0 then break
			u1(o) then u = Ub.u1(o)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3203: <fragment>:23:15: 'u' is open in 'm1' and cannot be reassigned there
```

<!-- test: model-hand-ai-h3-control-outer-local -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `h3-control-outer-local` of redteamAI.hand_ai7, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx6
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx6'

type Wo6
	var keep as Bx6
	var item as isolated Bx6

	static function create(keep Bx6, item isolated Bx6) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo6'

function scn()
	let h = Wo6.create(Bx6.create(0), item: Bx6.create(1)) as isolated Wo6
	let o = Wo6.create(Bx6.create(0), item: Bx6.create(1)) as isolated Wo6
	modify h 'm1'
		modify o 'm2'
			let _ = Wo6.create(o.keep, item: Bx6.create(1))
		end 'm2'
	end 'm1'
end 'scn'

function main() returns ExitCode
	scn()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:25:10: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ai-h3-control-local-in-window -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `h3-control-local-in-window` of redteamAI.hand_ai7, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx6
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx6'

type Wo6
	var keep as Bx6
	var item as isolated Bx6

	static function create(keep Bx6, item isolated Bx6) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo6'

function scn()
	let h = Wo6.create(Bx6.create(0), item: Bx6.create(1)) as isolated Wo6
	modify h 'm1'
		let ib = Wo6.create(Bx6.create(0), item: Bx6.create(1)) as isolated Wo6
		modify ib 'm2'
			let _ = Wo6.create(ib.keep, item: Bx6.create(1))
		end 'm2'
	end 'm1'
end 'scn'

function main() returns ExitCode
	scn()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-aj-byref-mut-word -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `byref-mut-word` of redteamAJ.hand_aj2, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx6
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx6'

type Wo6
	var keep as Bx6
	var item as isolated Bx6

	static function create(keep Bx6, item isolated Bx6) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo6'

function takeWo(w isolated Wo6)
	let _ = w
	let _ = 1 as Num
end 'takeWo'

function g(o mutable Wo6)
	let h = Wo6.create(Bx6.create(0), item: Bx6.create(1)) as isolated Wo6
	modify h 'm1'
		o.keep = h.keep
	end 'm1'
	takeWo(h)
end 'g'

function scn()
	let o = Wo6.create(Bx6.create(0), item: Bx6.create(1))
	g(o)
end 'scn'

function main() returns ExitCode
	scn()
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:29:3: cannot write 'keep' through 'o', which is read-only while 'm1' is open
```

<!-- test: model-hand-aj-ovo-int -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ovo-int` of redteamAJ.hand_aj5, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function sc()
	var k = 1 as Num
	let h = Bx.create(1) as isolated Bx
	modify h 'm1'
		k = h.n
	end 'm1'
end 'sc'

function main() returns ExitCode
	sc()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-aj-control-plain-param -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `control-plain-param` of redteamAJ.hand_aj6, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

	function put()
		self.keep = Bx.create(9)
	end 'put'

	function bld()
		let _ = Wo.create(self.keep, item: Bx.create(1))
	end 'bld'
end 'Wo'

function takeWo(w isolated Wo)
	let _ = w.keep.n
end 'takeWo'

function g(o isolated Wo)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		modify o 'm2'
			o.keep = Bx.create(9)
		end 'm2'
	end 'm1'
	takeWo(h)
	takeWo(o)
end 'g'

function main() returns ExitCode
	g(Wo.create(Bx.create(0), item: Bx.create(1)))
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:36:10: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-aj-control-outer-var -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `control-outer-var` of redteamAJ.hand_aj6, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

	function put()
		self.keep = Bx.create(9)
	end 'put'

	function bld()
		let _ = Wo.create(self.keep, item: Bx.create(1))
	end 'bld'
end 'Wo'

function takeWo(w isolated Wo)
	let _ = w.keep.n
end 'takeWo'

function lg(x Num) returns Num
	return x
end 'lg'

function g()
	var o = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		modify o 'm2'
			o.keep = Bx.create(9)
		end 'm2'
	end 'm1'
	takeWo(h)
	if lg(0) != 0 'i3'
		o = Wo.create(Bx.create(0), item: Bx.create(1))
	end 'i3'
	takeWo(o)
end 'g'

function main() returns ExitCode
	g()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:41:10: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ak-isoparam-in-window-var -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `isoparam-in-window-var` of redteamAK.hand_ak1, in capmodel `reggio/REPORTRTAK.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

	function put()
		self.keep = Bx.create(9)
	end 'put'

	function bld()
		let _ = Wo.create(self.keep, item: Bx.create(1))
	end 'bld'
end 'Wo'

function takeWo(w isolated Wo)
	let _ = w.keep.n
end 'takeWo'

function wrWo(w isolated Wo)
	modify w 'm1'
		w.keep = Bx.create(9)
	end 'm1'
end 'wrWo'

function main() returns ExitCode
	let o = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		wrWo(o)
	end 'm1'
	takeWo(h)
	takeWo(o)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:46:9: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ak-isoparam-in-window-param -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `isoparam-in-window-param` of redteamAK.hand_ak1, in capmodel `reggio/REPORTRTAK.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

	function put()
		self.keep = Bx.create(9)
	end 'put'

	function bld()
		let _ = Wo.create(self.keep, item: Bx.create(1))
	end 'bld'
end 'Wo'

function takeWo(w isolated Wo)
	let _ = w.keep.n
end 'takeWo'

function wrWo(w isolated Wo)
	modify w 'm1'
		w.keep = Bx.create(9)
	end 'm1'
end 'wrWo'

function g(o isolated Wo)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		wrWo(o)
	end 'm1'
	takeWo(h)
	takeWo(o)
end 'g'

function main() returns ExitCode
	g(Wo.create(Bx.create(0), item: Bx.create(1)))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:45:9: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ak-byref-param-shadowed -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `byref-param-shadowed` of redteamAK.hand_ak1, in capmodel `reggio/REPORTRTAK.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

	function put()
		self.keep = Bx.create(9)
	end 'put'

	function bld()
		let _ = Wo.create(self.keep, item: Bx.create(1))
	end 'bld'
end 'Wo'

function takeWo(w isolated Wo)
	let _ = w.keep.n
end 'takeWo'

function g(o isolated Wo)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		modify o 'm2'
			o.keep = Bx.create(9)
		end 'm2'
	end 'm1'
	takeWo(h)
	var o2 = 1 as Num
	o2 = 2
end 'g'

function main() returns ExitCode
	g(Wo.create(Bx.create(0), item: Bx.create(1)))
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:36:10: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ak-field-path-outer-iso-slot -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3199. Model program `field-path-outer-iso-slot` of redteamAK.hand_ak2, in capmodel `reggio/REPORTRTAK.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Pb
	var b as isolated Bx

	static function create(b isolated Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Pb'

function takeBx(bV isolated Bx)
	modify bV 'm1'
		bV.n = 3
	end 'm1'
end 'takeBx'

function main() returns ExitCode
	let w = Pb.create(Bx.create(0))
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		let x = swap w.b with Bx.create(3)
		takeBx(x)
	end 'm1'
	takeBx(swap w.b with Bx.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:39:11: 'w.b' cannot be swapped: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ak-field-path-outer-iso-var-slot -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3199. Model program `field-path-outer-iso-var-slot` of redteamAK.hand_ak2, in capmodel `reggio/REPORTRTAK.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function takeBx(b isolated Bx)
	modify b 'm1'
		b.n = 3
	end 'm1'
end 'takeBx'

function main() returns ExitCode
	let o = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		let x = swap o.item with Bx.create(3)
		takeBx(x)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:31:16: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ak-field-path-outer-iso-var-slot-opened -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `field-path-outer-iso-var-slot-opened` of redteamAK.hand_ak2, in capmodel `reggio/REPORTRTAK.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function takeBx(b isolated Bx)
	modify b 'm1'
		b.n = 3
	end 'm1'
end 'takeBx'

function main() returns ExitCode
	let o = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		modify o 'm2'
			let x = swap o.item with Bx.create(3)
			takeBx(x)
		end 'm2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:31:10: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-ak-byref-linear-in-window-write -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `byref-linear-in-window-write` of redteamAK.hand_ak2, in capmodel `reggio/REPORTRTAK.txt`.

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

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function eatSh(p Sh)
	match p 'x1'
		none then break
		held(q) then q.fin(1)
	end 'x1'
end 'eatSh'

function g(r mutable Sh)
	let h = Bx.create(0) as isolated Bx
	modify h 'm1'
		match r 'x2'
			none then break
			held(q) then q.set(2)
		end 'x2'
	end 'm1'
end 'g'

function main() returns ExitCode
	var s = Sh.held(Rl.create(1))
	g(s)
	eatSh(s)
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:58:4: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
error E3224: <fragment>:67:2: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
```

<!-- test: model-hand-ak-svc-behave-param-open-state-window -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `svc-behave-param-open-state-window` of redteamAK.hand_ak3, in capmodel `reggio/REPORTRTAK.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

	function put()
		self.keep = Bx.create(9)
	end 'put'
end 'Wo'

type Sv
	var w as isolated Wo
	var n as Num

	static function create(w isolated Wo, n Num) returns Self
		return Self{w: w, n: n}
	end 'create'

	export function b(r isolated Wo)
		modify self.w 'm1'
			modify r 'm2'
				r.keep = Bx.create(4)
			end 'm2'
		end 'm1'
		takeWo(r)
	end 'b'
end 'Sv'

function takeWo(wV isolated Wo)
	let _ = wV.keep.n
end 'takeWo'

function main() returns ExitCode
	let s = spawn Sv.create(Wo.create(Bx.create(0), item: Bx.create(1)), n: 0)
	try s.b(Wo.create(Bx.create(0), item: Bx.create(1))) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:35:11: 'r' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-al-d-direct -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `d-direct` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = stash(q) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:59:10: 'stash(q) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-d-two-step -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `d-two-step` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function stash2(t Tq) returns Ko
	return stash(t)
end 'stash2'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = stash2(q) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:63:10: 'stash2(q) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-d-part -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `d-part` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = part(q) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:59:10: 'part(q) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-d-recv-mkk -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `d-recv-mkK` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = q.mkK() as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:55:10: 'q.mkK() as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-d-recv-part -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `d-recv-part` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = q.part() as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:55:10: 'q.part() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-d-cond-arg -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `d-cond-arg` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let c = 1 as Num
	let k = stash((q if c != 0 else q2)) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'stash((q if c != 0 else q2)) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-d-cond-recv -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `d-cond-recv` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let c = 1 as Num
	let k = (q if c != 0 else q2).mkK() as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: '(q if c != 0 else q2).mkK() as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-d-param-store -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `d-param-store` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function st(t Tq, o Ko)
	o.keep = t.b
end 'st'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let o = Ko.create(Bx.create(0), n: 0)
	st(q, o: o)
	let k = o as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'o as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-d-new-direct -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `d-new-direct` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = Ko.create(q.b, n: 1) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:55:10: 'Ko.create(q.b, n: 1) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-d-new-of-call -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `d-new-of-call` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = Ko.create(part(q), n: 1) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:59:10: 'Ko.create(part(q), n: 1) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-d-iso-param -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `d-iso-param` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function takeIsoK(k isolated Ko)
	modify k 'm1'
		k.keep.n = 9
	end 'm1'
end 'takeIsoK'

function main() returns ExitCode
	let q = Tq.create(1, b: Bx.create(2))
	takeIsoK(stash(q))
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:50:11: 'stash(q)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-al-d-return-iso -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `d-return-iso` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function mkIso(t Tq) returns isolated Ko
	return stash(t)
end 'mkIso'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = mkIso(q)
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:9: 'stash(t)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; use a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-d-return-iso-local -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `d-return-iso-local` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function mkIso() returns isolated Bx
	let q = Tq.create(1, b: Bx.create(2))
	let r = part(q)
	q.b.n = 5
	return r
end 'mkIso'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let k = mkIso()
	try s.takeB(k) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:60:9: 'r' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted. Declare it `as isolated Bx`, or pass a copy
```

<!-- test: model-hand-al-c-outer-rec -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `c-outer-rec` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Oq is unique
	var q as Tq
	var n as Num

	static function create(q Tq, n Num) returns Self
		return Self{q: q, n: n}
	end 'create'
end 'Oq'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function sOq(o Oq) returns Ko
	return Ko.create(o.q.b, n: 1)
end 'sOq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let o = Oq.create(Tq.create(1, b: Bx.create(2)), n: 0)
	let k = sOq(o) as isolated Ko
	try s.takeK(k) otherwise ignore
	o.q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:68:10: 'sOq(o) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-c-outer-union -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `c-outer-union` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

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

type Ow is unique
	var u as UQ

	static function create(u UQ) returns Self
		return Self{u: u}
	end 'create'
end 'Ow'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function sOw(o Ow) returns Bx
	match o.u 'x1'
		none then return Bx.create(0)
		have(t) then return t.b
	end 'x1'
end 'sOw'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let o = Ow.create(UQ.have(Tq.create(1, b: Bx.create(2))))
	let k = sOw(o) as isolated Bx
	try s.takeB(k) otherwise ignore
	match o.u 'x1'
		none then break
		have(t) then t.b.n = 3
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:75:10: 'sOw(o) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-c-outer-arr -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `c-outer-arr` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

type Oa is unique
	var xs as TqArray

	static function create(xs TqArray) returns Self
		return Self{xs: xs}
	end 'create'
end 'Oa'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function sOa(o Oa) returns Bx
	let r = Bx.create(0)
	for e in o.xs 'f1'
		return e.b
	end 'f1'
	return r
end 'sOa'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let a = TqArray.create(0)
	a.push(Tq.create(1, b: Bx.create(2)))
	let o = Oa.create(a)
	let k = sOa(o) as isolated Bx
	try s.takeB(k) otherwise ignore
	for e in o.xs 'f1'
		e.b.n = 3
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:74:10: 'sOa(o) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-c-array-direct -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `c-array-direct` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function sA(xs TqArray) returns Bx
	let r = Bx.create(0)
	for e in xs 'f1'
		return e.b
	end 'f1'
	return r
end 'sA'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let a = TqArray.create(0)
	a.push(Tq.create(1, b: Bx.create(2)))
	let k = sA(a) as isolated Bx
	try s.takeB(k) otherwise ignore
	for e in a 'f1'
		e.b.n = 3
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:65:10: 'sA(a) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-f-field-arg -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f-field-arg` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

type Hq is unique
	var q as Tq
	var u as UQ
	var xs as TqArray
	var n as Num

	static function create(q Tq, u UQ, xs TqArray, n Num) returns Self
		return Self{q: q, u: u, xs: xs, n: n}
	end 'create'
end 'Hq'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let h = Hq.create(Tq.create(1, b: Bx.create(2)), u: UQ.none, xs: TqArray.create(0), n: 0)
	let k = stash(h.q) as isolated Ko
	try s.takeK(k) otherwise ignore
	h.q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:76:10: 'stash(h.q) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-f-field-recv -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f-field-recv` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

type Hq is unique
	var q as Tq
	var u as UQ
	var xs as TqArray
	var n as Num

	static function create(q Tq, u UQ, xs TqArray, n Num) returns Self
		return Self{q: q, u: u, xs: xs, n: n}
	end 'create'
end 'Hq'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let h = Hq.create(Tq.create(1, b: Bx.create(2)), u: UQ.none, xs: TqArray.create(0), n: 0)
	let k = h.q.mkK() as isolated Ko
	try s.takeK(k) otherwise ignore
	h.q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:72:10: 'h.q.mkK() as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-f-whole-holder -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `f-whole-holder` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

type Hq is unique
	var q as Tq
	var u as UQ
	var xs as TqArray
	var n as Num

	static function create(q Tq, u UQ, xs TqArray, n Num) returns Self
		return Self{q: q, u: u, xs: xs, n: n}
	end 'create'
end 'Hq'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function sH(h Hq) returns Ko
	return Ko.create(h.q.b, n: 1)
end 'sH'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let h = Hq.create(Tq.create(1, b: Bx.create(2)), u: UQ.none, xs: TqArray.create(0), n: 0)
	let k = sH(h) as isolated Ko
	try s.takeK(k) otherwise ignore
	h.q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:76:10: 'sH(h) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-f-param-field -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f-param-field` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

type Hq is unique
	var q as Tq
	var u as UQ
	var xs as TqArray
	var n as Num

	static function create(q Tq, u UQ, xs TqArray, n Num) returns Self
		return Self{q: q, u: u, xs: xs, n: n}
	end 'create'
end 'Hq'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function g(h Hq) returns isolated Ko
	return stash(h.q)
end 'g'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let h = Hq.create(Tq.create(1, b: Bx.create(2)), u: UQ.none, xs: TqArray.create(0), n: 0)
	let k = g(h)
	try s.takeK(k) otherwise ignore
	h.q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:74:9: 'stash(h.q)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; use a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-f-self-field -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f-self-field` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Hs is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'

	function mk() returns isolated Ko
		return stash(self.q)
	end 'mk'
end 'Hs'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let h = Hs.create(Tq.create(1, b: Bx.create(2)))
	let k = h.mk()
	try s.takeK(k) otherwise ignore
	h.q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:46:10: 'stash(self.q)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; use a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-f-self -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `f-self` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Ts is unique
	var b as Bx

	static function create(b Bx) returns Self
		return Self{b: b}
	end 'create'

	function mk() returns isolated Ko
		return mkT(self)
	end 'mk'
end 'Ts'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function mkT(t Ts) returns Ko
	return Ko.create(t.b, n: 1)
end 'mkT'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let h = Ts.create(Bx.create(1))
	let k = h.mk()
	try s.takeK(k) otherwise ignore
	h.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:29:10: 'mkT(self)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; use a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-b-for-binding -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `b-for-binding` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let a = TqArray.create(0)
	a.push(Tq.create(1, b: Bx.create(2)))
	for t in a 'f1'
		let k = stash(t) as isolated Ko
		try s.takeK(k) otherwise ignore
	end 'f1'
	for t in a 'f2'
		t.b.n = 3
	end 'f2'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:62:11: 'stash(t) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-b-arm-param-ret -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `b-arm-param-ret` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function g(u UQ) returns isolated Ko
	match u 'x1'
		none then break
		have(t) then return stash(t)
	end 'x1'
	return Ko.create(Bx.create(0), n: 0)
end 'g'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	let k = g(u)
	try s.takeK(k) otherwise ignore
	match u 'x1'
		none then break
		have(t) then t.b.n = 3
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:64:23: 'stash(t)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; use a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-b-for-param-ret -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `b-for-param-ret` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function g(xs TqArray) returns isolated Ko
	for t in xs 'f1'
		return stash(t)
	end 'f1'
	return Ko.create(Bx.create(0), n: 0)
end 'g'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let a = TqArray.create(0)
	a.push(Tq.create(1, b: Bx.create(2)))
	let k = g(a)
	try s.takeK(k) otherwise ignore
	for t in a 'f1'
		t.b.n = 3
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:59:10: 'stash(t)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; use a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-u-unique-result-iso -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `u-unique-result-iso` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Stq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeT(t isolated Tq)
		modify t 'm1'
			t.b.n = 9
		end 'm1'
	end 'takeT'
end 'Stq'

function wrap(t Tq) returns Tq
	return Tq.create(2, b: t.b)
end 'wrap'

function main() returns ExitCode
	let s = spawn Stq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let r = wrap(q) as isolated Tq
	try s.takeT(r) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:59:10: 'wrap(q) as isolated Tq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-r-renamed -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `r-renamed` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q0 = Tq.create(1, b: Bx.create(2))
	let q = q0
	let k = stash(q) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:60:10: 'stash(q) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-r-refilled -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `r-refilled` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function eat(t Tq)
	t.n = 0
end 'eat'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	var q = Tq.create(1, b: Bx.create(2))
	eat(q)
	q = Tq.create(1, b: Bx.create(2))
	let k = stash(q) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:65:10: 'stash(q) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-r-moved-branch -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `r-moved-branch` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function eat(t Tq)
	t.n = 0
end 'eat'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	var q = Tq.create(1, b: Bx.create(2))
	let c = 1 as Num
	if c != 0 'i1'
		eat(q)
		q = Tq.create(1, b: Bx.create(2))
	end 'i1'
	let k = stash(q) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:68:10: 'stash(q) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-r-loop -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `r-loop` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	var i = 0 as Num
	while i != 0 'w1'
		let k = stash(q) as isolated Ko
		try s.takeK(k) otherwise ignore
		q.b.n = 3
		i = 0
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:11: 'stash(q) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-k-closed -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-closed` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tc is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tc'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		modify b 'm1'
			b.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function mkB(t Tc) returns Bx
	return Bx.create(t.n)
end 'mkB'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tc.create(1)
	let k = mkB(q) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.n = 3
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-al-k-moved -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `k-moved` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function eatK(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'eatK'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = eatK(q) as isolated Ko
	try s.takeK(k) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:59:10: 'eatK(q) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-h1-min -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `h1-min` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = stash((q if 1 != 0 else q)) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:55:10: 'stash((q if 1 != 0 else q)) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-h1-two -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `h1-two` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let k = stash((q if 1 != 0 else q2)) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:56:10: 'stash((q if 1 != 0 else q2)) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-h1-fresh-arm -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `h1-fresh-arm` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = stash((q if 1 != 0 else Tq.create(1, b: Bx.create(2)))) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:55:10: 'stash((q if 1 != 0 else Tq.create(1, b: Bx.create(2)))) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-h1-part -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `h1-part` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = part((q if 1 != 0 else q)) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:55:10: 'part((q if 1 != 0 else q)) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-h1-recv -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `h1-recv` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = (q if 1 != 0 else q).mkK() as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:51:10: '(q if 1 != 0 else q).mkK() as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-h1-iso-param -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `h1-iso-param` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function takeIsoK(k isolated Ko)
	modify k 'm1'
		k.keep.n = 9
	end 'm1'
end 'takeIsoK'

function main() returns ExitCode
	let q = Tq.create(1, b: Bx.create(2))
	takeIsoK(stash((q if 1 != 0 else q)))
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:46:11: 'stash((q if 1 != 0 else q))' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-al-h1-return-iso -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `h1-return-iso` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function mk1() returns isolated Ko
	let q = Tq.create(1, b: Bx.create(2))
	let k = stash((q if 1 != 0 else q))
	return k
end 'mk1'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let k = mk1()
	try s.takeK(k) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:55:9: 'k' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted. Declare it `as isolated Ko`, or pass a copy
```

<!-- test: model-hand-al-h1-return-iso-kept -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `h1-return-iso-kept` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Hk is unique
	var q as Tq
	var k as isolated Ko

	static function create(q Tq, k isolated Ko) returns Self
		return Self{q: q, k: k}
	end 'create'
end 'Hk'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(kV isolated Ko)
		modify kV 'm1'
			kV.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function mk2(h Hk)
	let qV = Tq.create(1, b: Bx.create(2))
	h.k = stash((qV if 1 != 0 else qV))
	h.q = qV
end 'mk2'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let h = Hk.create(Tq.create(1, b: Bx.create(2)), k: Ko.create(Bx.create(0), n: 0))
	mk2(h)
	let kV = swap h.k with Ko.create(Bx.create(0), n: 0)
	try s.takeK(kV) otherwise ignore
	h.q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:63:8: 'stash((qV if 1 != 0 else qV))' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Ko`, or store a copy
```

<!-- test: model-hand-al-h1-nested-cond -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `h1-nested-cond` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let k = stash(((q if 1 != 0 else q2) if 1 != 0 else q2)) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:56:10: 'stash(((q if 1 != 0 else q2) if 1 != 0 else q2)) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-h1-cond-call-arm -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `h1-cond-call-arm` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = (stash(q) if 1 != 0 else stash(q)) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:55:10: '(stash(q) if 1 != 0 else stash(q)) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-h1-arm-store -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `h1-arm-store` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

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

	function mkK() returns Ko
		return Ko.create(self.b, n: 1)
	end 'mkK'
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	let q = Tq.create(1, b: Bx.create(2))
	let o = Ko.create(Bx.create(0), n: 0)
	match u 'x1'
		none then break
		have then o.keep = part((q if 1 != 0 else q))
	end 'x1'
	let k = o as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:66:10: 'o as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-h1-for-body -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `h1-for-body` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	for _ in xs 'f1'
		let k = stash((q if 1 != 0 else q)) as isolated Ko
		try s.takeK(k) otherwise ignore
	end 'f1'
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:59:11: 'stash((q if 1 != 0 else q)) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-h1-onecall -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `h1-onecall` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function put(k Ko, t Tq)
	k.n = t.n
end 'put'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = Ko.create(Bx.create(0), n: 0) as isolated Ko
	put(k, t: (q if 1 != 0 else q))
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:56:12: '(q if 1 != 0 else q)' is mutable outside 'k'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-al-h1-onecall-store -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `h1-onecall-store` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function put(k Ko, t Tq)
	k.keep = t.b
end 'put'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = Ko.create(Bx.create(0), n: 0) as isolated Ko
	put(k, t: (q if 1 != 0 else q))
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:56:12: '(q if 1 != 0 else q)' is mutable outside 'k'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-al-h1-param-store -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `h1-param-store` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function st(t Tq, o Ko)
	o.keep = t.b
end 'st'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let o = Ko.create(Bx.create(0), n: 0)
	st((q if 1 != 0 else q), o: o)
	let k = o as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: 'o as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-al-h1-wrapper-field -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `h1-wrapper-field` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let w = Wq.create(Tq.create(1, b: Bx.create(2)))
	let w2 = Wq.create(Tq.create(1, b: Bx.create(2)))
	let k = stash((w.q if 1 != 0 else w2.q)) as isolated Ko
	try s.takeK(k) otherwise ignore
	w.q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:64:21: cannot join a unique value read out of a field or an element with another value: the field or element goes on owning it, and a unique value has one name; apply the use inside each arm instead, as `f(t) if c else f(fresh)` does
```

<!-- test: model-hand-al-k-closed-cond -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-closed-cond` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tc is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tc'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		modify b 'm1'
			b.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function mkB(t Tc) returns Bx
	return Bx.create(t.n)
end 'mkB'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tc.create(1)
	let k = mkB((q if 1 != 0 else q)) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.n = 3
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-am-un-wrap-alias -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `un-wrap-alias` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function partU(u UQ) returns Bx
	match u 'x1'
		none then return Bx.create(0)
		have(t) then return t.b
	end 'x1'
end 'partU'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let a = q.b
	let k = partU(UQ.have(q)) as isolated Bx
	try s.takeB(k) otherwise ignore
	a.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:68:10: 'partU(UQ.have(q)) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-un-wrap-cond -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `un-wrap-cond` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function partU(u UQ) returns Bx
	match u 'x1'
		none then return Bx.create(0)
		have(t) then return t.b
	end 'x1'
end 'partU'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = partU(UQ.have((q if 1 != 0 else q))) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:67:10: 'partU(UQ.have((q if 1 != 0 else q))) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-un-wrap-bshared -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `un-wrap-bshared` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function partU(u UQ) returns Bx
	match u 'x1'
		none then return Bx.create(0)
		have(t) then return t.b
	end 'x1'
end 'partU'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let bV = Bx.create(2)
	let k = partU(UQ.have(Tq.create(1, b: bV))) as isolated Bx
	try s.takeB(k) otherwise ignore
	bV.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:67:10: 'partU(UQ.have(Tq.create(1, b: bV))) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-fp-oq-part -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `fp-oq-part` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Oq is unique
	var q as Tq
	var n as Num

	static function create(q Tq, n Num) returns Self
		return Self{q: q, n: n}
	end 'create'
end 'Oq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let o = Oq.create(Tq.create(1, b: Bx.create(2)), n: 0)
	let k = part(o.q) as isolated Bx
	try s.takeB(k) otherwise ignore
	o.q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:68:10: 'part(o.q) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-fp-oq-recv -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `fp-oq-recv` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Oq is unique
	var q as Tq
	var n as Num

	static function create(q Tq, n Num) returns Self
		return Self{q: q, n: n}
	end 'create'
end 'Oq'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let o = Oq.create(Tq.create(1, b: Bx.create(2)), n: 0)
	let k = o.q.mkK() as isolated Ko
	try s.takeK(k) otherwise ignore
	o.q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:64:10: 'o.q.mkK() as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-fp-oq-part-recv -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `fp-oq-part-recv` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Oq is unique
	var q as Tq
	var n as Num

	static function create(q Tq, n Num) returns Self
		return Self{q: q, n: n}
	end 'create'
end 'Oq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let o = Oq.create(Tq.create(1, b: Bx.create(2)), n: 0)
	let k = o.q.part() as isolated Bx
	try s.takeB(k) otherwise ignore
	o.q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:64:10: 'o.q.part() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-fp-oq-idb -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `fp-oq-idB` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Oq is unique
	var q as Tq
	var n as Num

	static function create(q Tq, n Num) returns Self
		return Self{q: q, n: n}
	end 'create'
end 'Oq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function idB(bV Bx) returns Bx
	return bV
end 'idB'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let o = Oq.create(Tq.create(1, b: Bx.create(2)), n: 0)
	let k = idB(o.q.b) as isolated Bx
	try s.takeB(k) otherwise ignore
	o.q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:68:10: 'idB(o.q.b) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-rc-part-idb -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `rc-part-idB` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function idB(bV Bx) returns Bx
	return bV
end 'idB'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = idB(q.part()) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:59:10: 'idB(q.part()) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-rc-me -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `rc-me` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tm is unique
	var b as Bx

	static function create(b Bx) returns Self
		return Self{b: b}
	end 'create'

	function me() returns Tm
		return self
	end 'me'
end 'Tm'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function partM(t Tm) returns Bx
	return t.b
end 'partM'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tm.create(Bx.create(2))
	let k = partM(q.me()) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:45:10: 'partM(q.me()) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-rc-me-consuming -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3102, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `rc-me-consuming` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tm is unique
	var b as Bx

	static function create(b Bx) returns Self
		return Self{b: b}
	end 'create'

	function me(consuming self) returns Tm
		return self
	end 'me'
end 'Tm'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function partM(t Tm) returns Bx
	return t.b
end 'partM'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tm.create(Bx.create(2))
	let a = q.b
	let k = partM(q.me()) as isolated Bx
	try s.takeB(k) otherwise ignore
	a.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:19:11: `consuming self` belongs to a method of a `type` that declares a finalizer, and no method of 'Tm' contains `destruct self`
error E3102: <fragment>:46:16: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-am-rc-consuming-part -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3102, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `rc-consuming-part` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tm is unique
	var b as Bx

	static function create(b Bx) returns Self
		return Self{b: b}
	end 'create'

	function take(consuming self) returns Bx
		return self.b
	end 'take'
end 'Tm'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tm.create(Bx.create(2))
	let a = q.b
	let k = q.take() as isolated Bx
	try s.takeB(k) otherwise ignore
	a.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:19:11: `consuming self` belongs to a method of a `type` that declares a finalizer, and no method of 'Tm' contains `destruct self`
error E3102: <fragment>:42:10: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-am-rc-consuming-part-first -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3102, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `rc-consuming-part-first` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tm is unique
	var b as Bx

	static function create(b Bx) returns Self
		return Self{b: b}
	end 'create'

	function take(consuming self) returns Bx
		return self.b
	end 'take'
end 'Tm'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tm.create(Bx.create(2))
	let k = q.take() as isolated Bx
	try s.takeB(k) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:19:11: `consuming self` belongs to a method of a `type` that declares a finalizer, and no method of 'Tm' contains `destruct self`
error E3102: <fragment>:41:10: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-am-rc-consuming-bshared -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3102, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `rc-consuming-bshared` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tm is unique
	var b as Bx

	static function create(b Bx) returns Self
		return Self{b: b}
	end 'create'

	function take(consuming self) returns Bx
		return self.b
	end 'take'
end 'Tm'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let bV = Bx.create(2)
	let q = Tm.create(bV)
	let k = q.take() as isolated Bx
	try s.takeB(k) otherwise ignore
	bV.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:19:11: `consuming self` belongs to a method of a `type` that declares a finalizer, and no method of 'Tm' contains `destruct self`
error E3102: <fragment>:42:10: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-am-rc-consuming-cond -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3200, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `rc-consuming-cond` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tm is unique
	var b as Bx

	static function create(b Bx) returns Self
		return Self{b: b}
	end 'create'

	function take(consuming self) returns Bx
		return self.b
	end 'take'
end 'Tm'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tm.create(Bx.create(2))
	let q2 = Tm.create(Bx.create(2))
	let k = (q if 1 != 0 else q2).take() as isolated Bx
	try s.takeB(k) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:19:11: `consuming self` belongs to a method of a `type` that declares a finalizer, and no method of 'Tm' contains `destruct self`
error E3200: <fragment>:42:10: '(q if 1 != 0 else q2).take() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-id-part-alias -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `id-part-alias` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function idT(t Tq) returns Tq
	return t
end 'idT'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let a = q.b
	let k = part(idT(q)) as isolated Bx
	try s.takeB(k) otherwise ignore
	a.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:64:10: 'part(idT(q)) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-id-part-after -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `id-part-after` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function idT(t Tq) returns Tq
	return t
end 'idT'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = part(idT(q)) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:63:10: 'part(idT(q)) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-id-keep-alias -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `id-keep-alias` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function idT(t Tq) returns Tq
	return t
end 'idT'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	let qV = Tq.create(1, b: Bx.create(2))
	let a = qV.b
	let k = keepW(idT(qV)) as isolated Wq
	try s.takeW(k) otherwise ignore
	a.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:72:10: 'keepW(idT(qV)) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-id-cond-part -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `id-cond-part` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function idT(t Tq) returns Tq
	return t
end 'idT'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let a = q.b
	let k = part(idT((q if 1 != 0 else q))) as isolated Bx
	try s.takeB(k) otherwise ignore
	a.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:64:10: 'part(idT((q if 1 != 0 else q))) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-id-bshared -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `id-bshared` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function idT(t Tq) returns Tq
	return t
end 'idT'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let bV = Bx.create(2)
	let k = part(idT(Tq.create(1, b: bV))) as isolated Bx
	try s.takeB(k) otherwise ignore
	bV.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:63:10: 'part(idT(Tq.create(1, b: bV))) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-nest-stash-part -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `nest-stash-part` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function idB(bV Bx) returns Bx
	return bV
end 'idB'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = Ko.create(idB(part(q)), n: 1) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:63:10: 'Ko.create(idB(part(q)), n: 1) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-nest-idb-part -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `nest-idB-part` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function idB(bV Bx) returns Bx
	return bV
end 'idB'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = idB(part(q)) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:63:10: 'idB(part(q)) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-kt-b-direct -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `kt-b-direct` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	let bV = Bx.create(2)
	let k = keepW(Tq.create(1, b: bV)) as isolated Wq
	try s.takeW(k) otherwise ignore
	bV.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:67:10: 'keepW(Tq.create(1, b: bV)) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-kt-b-alias-before -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `kt-b-alias-before` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	let bV = Bx.create(2)
	let c = bV
	let k = keepW(Tq.create(1, b: bV)) as isolated Wq
	try s.takeW(k) otherwise ignore
	c.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:68:10: 'keepW(Tq.create(1, b: bV)) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-kt-b-holder -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `kt-b-holder` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	let o = Ko.create(Bx.create(2), n: 0)
	let k = keepW(Tq.create(1, b: o.keep)) as isolated Wq
	try s.takeW(k) otherwise ignore
	o.keep.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:67:10: 'keepW(Tq.create(1, b: o.keep)) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-kt-b-written -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `kt-b-written` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	let bV = Bx.create(2)
	bV.n = 4
	let k = keepW(Tq.create(1, b: bV)) as isolated Wq
	try s.takeW(k) otherwise ignore
	bV.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:68:10: 'keepW(Tq.create(1, b: bV)) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-kt-b-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `kt-b-twice` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	let bV = Bx.create(2)
	let k = keepW(Tq.create(bV.n, b: bV)) as isolated Wq
	try s.takeW(k) otherwise ignore
	bV.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:67:10: 'keepW(Tq.create(bV.n, b: bV)) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-kt-b-mkfn -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `kt-b-mkfn` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function mkT(bV Bx) returns Tq
	return Tq.create(1, b: bV)
end 'mkT'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	let bV = Bx.create(2)
	let c = bV
	let k = keepW(mkT(bV)) as isolated Wq
	try s.takeW(k) otherwise ignore
	c.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:72:10: 'keepW(mkT(bV)) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-kt-b-param -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `kt-b-param` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function wrapP(bV Bx) returns isolated Wq
	return keepW(Tq.create(1, b: bV))
end 'wrapP'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	let bV = Bx.create(2)
	let k = wrapP(bV)
	try s.takeW(k) otherwise ignore
	bV.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:65:9: 'keepW(Tq.create(1, b: bV))' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; use a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-kt-refill -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `kt-refill` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	var bV = Bx.create(2)
	let c = bV
	bV = Bx.create(5)
	let k = keepW(Tq.create(1, b: c)) as isolated Wq
	try s.takeW(k) otherwise ignore
	c.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:69:10: 'keepW(Tq.create(1, b: c)) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-kt-refill2 -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `kt-refill2` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	var bV = Bx.create(2)
	let c = bV
	bV = c
	let k = keepW(Tq.create(1, b: bV)) as isolated Wq
	try s.takeW(k) otherwise ignore
	c.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:69:10: 'keepW(Tq.create(1, b: bV)) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-kt-cond-b -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `kt-cond-b` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	let bV = Bx.create(2)
	let c = Bx.create(3)
	let k = keepW(Tq.create(1, b: (bV if 1 != 0 else c))) as isolated Wq
	try s.takeW(k) otherwise ignore
	bV.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:68:10: 'keepW(Tq.create(1, b: (bV if 1 != 0 else c))) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-kt-cond-b-alias -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `kt-cond-b-alias` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	let bV = Bx.create(2)
	let d = bV
	let k = keepW(Tq.create(1, b: (bV if 1 != 0 else bV))) as isolated Wq
	try s.takeW(k) otherwise ignore
	d.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:68:10: 'keepW(Tq.create(1, b: (bV if 1 != 0 else bV))) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-ma-union-part -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `ma-union-part` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function partU(u UQ) returns Bx
	match u 'x1'
		none then return Bx.create(0)
		have(t) then return t.b
	end 'x1'
end 'partU'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	let k = partU(u) as isolated Bx
	try s.takeB(k) otherwise ignore
	match u 'x1'
		none then break
		have(t) then t.b.n = 3
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:67:10: 'partU(u) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-ma-cond-subject -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `ma-cond-subject` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function partU(u UQ) returns Bx
	match u 'x1'
		none then return Bx.create(0)
		have(t) then return t.b
	end 'x1'
end 'partU'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	let u2 = UQ.none
	let k = partU((u if 1 != 0 else u2)) as isolated Bx
	try s.takeB(k) otherwise ignore
	match u 'x1'
		none then break
		have(t) then t.b.n = 3
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:68:10: 'partU((u if 1 != 0 else u2)) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-ma-for-part -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `ma-for-part` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	for t in xs 'f1'
		let k = part(t) as isolated Bx
		try s.takeB(k) otherwise ignore
		t.b.n = 3
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:62:11: 'part(t) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-ca-part-sync -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `ca-part-sync` of redteamAM/hand_am2.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

	function part() returns Bx
		return self.b
	end 'part'
end 'Tq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = part(q) as isolated Bx
	try s.takeB(k) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:46:10: 'part(q) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-ca-keep-sync -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `ca-keep-sync` of redteamAM/hand_am2.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

	function part() returns Bx
		return self.b
	end 'part'
end 'Tq'

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	let qV = Tq.create(1, b: Bx.create(2))
	let k = keepW(qV) as isolated Wq
	try s.takeW(k) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:54:10: 'keepW(qV) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-in-param-part-sync -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `in-param-part-sync` of redteamAM/hand_am2.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

	function part() returns Bx
		return self.b
	end 'part'
end 'Tq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function f(t Tq) returns isolated Bx
	return part(t)
end 'f'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let a = q.b
	let k = f(q)
	try s.takeB(k) otherwise ignore
	a.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:44:9: 'part(t)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; use a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-li-take-first -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `li-take-first` of redteamAM/hand_am3.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Lq
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'

	function take(consuming self) returns Bx
		let x = self.b
		destruct self
		return x
	end 'take'

	function look() returns Bx
		return self.b
	end 'look'
end 'Lq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Lq.create(1, b: Bx.create(2))
	let k = q.take() as isolated Bx
	try s.takeB(k) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:52:10: 'q.take() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-li-take-alias -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `li-take-alias` of redteamAM/hand_am3.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Lq
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'

	function take(consuming self) returns Bx
		let x = self.b
		destruct self
		return x
	end 'take'

	function look() returns Bx
		return self.b
	end 'look'
end 'Lq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Lq.create(1, b: Bx.create(2))
	let a = q.b
	let k = q.take() as isolated Bx
	try s.takeB(k) otherwise ignore
	a.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:53:10: 'q.take() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-li-take-cond -->
The model (`core-m3w-fa24`) refuses it by `R-LIN-COND`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `li-take-cond` of redteamAM/hand_am3.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Lq
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'

	function take(consuming self) returns Bx
		let x = self.b
		destruct self
		return x
	end 'take'

	function look() returns Bx
		return self.b
	end 'look'
end 'Lq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Lq.create(1, b: Bx.create(2))
	let q2 = Lq.create(1, b: Bx.create(2))
	let k = (q if 1 != 0 else q2).take() as isolated Bx
	try s.takeB(k) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:53:10: '(q if 1 != 0 else q2).take() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-li-look -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `li-look` of redteamAM/hand_am3.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Lq
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'

	function take(consuming self) returns Bx
		let x = self.b
		destruct self
		return x
	end 'take'

	function look() returns Bx
		return self.b
	end 'look'
end 'Lq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Lq.create(1, b: Bx.create(2))
	let k = q.look() as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	q.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:52:10: 'q.look() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-li-fn-take -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `li-fn-take` of redteamAM/hand_am3.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Lq
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'

	function take(consuming self) returns Bx
		let x = self.b
		destruct self
		return x
	end 'take'

	function look() returns Bx
		return self.b
	end 'look'
end 'Lq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function tk(l Lq) returns Bx
	return l.take()
end 'tk'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Lq.create(1, b: Bx.create(2))
	let k = tk(q) as isolated Bx
	try s.takeB(k) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:56:10: 'tk(q) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-li-fn-take-alias -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `li-fn-take-alias` of redteamAM/hand_am3.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Lq
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'

	function take(consuming self) returns Bx
		let x = self.b
		destruct self
		return x
	end 'take'

	function look() returns Bx
		return self.b
	end 'look'
end 'Lq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function tk(l Lq) returns Bx
	return l.take()
end 'tk'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Lq.create(1, b: Bx.create(2))
	let a = q.b
	let k = tk(q) as isolated Bx
	try s.takeB(k) otherwise ignore
	a.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: 'tk(q) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-li-param-owned -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `li-param-owned` of redteamAM/hand_am3.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Lq
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'

	function take(consuming self) returns Bx
		let x = self.b
		destruct self
		return x
	end 'take'

	function look() returns Bx
		return self.b
	end 'look'
end 'Lq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function tk(l Lq, s Sbq.handle)
	let k = l.take() as isolated Bx
	try s.takeB(k) otherwise ignore
end 'tk'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Lq.create(1, b: Bx.create(2))
	let a = q.b
	tk(q, s: s)
	a.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:50:10: 'l.take() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-ip-swap-out -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ip-swap-out` of redteamAM/hand_am3.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Stq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeT(w isolated Tq)
		modify w 'm1'
			w.b.n = 9
		end 'm1'
	end 'takeT'
end 'Stq'

function so(t isolated Tq) returns isolated Tq
	return t
end 'so'

function main() returns ExitCode
	let s = spawn Stq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = so(q)
	try s.takeT(k) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-am-ip-swap-out-alias -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `ip-swap-out-alias` of redteamAM/hand_am3.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Stq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeT(w isolated Tq)
		modify w 'm1'
			w.b.n = 9
		end 'm1'
	end 'takeT'
end 'Stq'

function so(t isolated Tq) returns isolated Tq
	return t
end 'so'

function main() returns ExitCode
	let s = spawn Stq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let a = q.b
	let k = so(q)
	try s.takeT(k) otherwise ignore
	a.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:43:13: 'q' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-am-ip-plain-result -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `ip-plain-result` of redteamAM/hand_am3.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function so(t isolated Tq) returns Ko
	let _ = t.n
	return Ko.create(Bx.create(1), n: 1)
end 'so'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let a = q.b
	let k = so(q) as isolated Ko
	try s.takeB(k.keep) otherwise ignore
	a.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:53:13: 'q' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-am-cl-plain-holds-closed -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `cl-plain-holds-closed` of redteamAM/hand_am3.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tc is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tc'

type Px
	var n as Num
	var t as Tc
	var b as Bx

	static function create(n Num, t Tc, b Bx) returns Self
		return Self{n: n, t: t, b: b}
	end 'create'
end 'Px'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function pp(p Px) returns Bx
	return p.b
end 'pp'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let p = Px.create(1, t: Tc.create(1), b: Bx.create(2))
	let k = pp(p) as isolated Bx
	try s.takeB(k) otherwise ignore
	p.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:51:10: 'pp(p) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-cl-plain-holds-closed2 -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `cl-plain-holds-closed2` of redteamAM/hand_am3.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tc is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tc'

type Px
	var t as Tc

	static function create(t Tc) returns Self
		return Self{t: t}
	end 'create'
end 'Px'

type Py
	var p as Px
	var b as Bx

	static function create(p Px, b Bx) returns Self
		return Self{p: p, b: b}
	end 'create'
end 'Py'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function pp(pV Py) returns Bx
	return pV.b
end 'pp'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let pV = Py.create(Px.create(Tc.create(1)), b: Bx.create(2))
	let k = pp(pV) as isolated Bx
	try s.takeB(k) otherwise ignore
	pV.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:58:10: 'pp(pV) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-cl-union-closed-head -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `cl-union-closed-head` of redteamAM/hand_am3.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ux
	none
	one(t Tc)
	two(b Bx)
end 'Ux'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tc is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tc'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		modify b 'm1'
			b.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function pu(u Ux) returns Bx
	match u 'x1'
		none then return Bx.create(0)
		one then return Bx.create(0)
		two(b) then return b
	end 'x1'
end 'pu'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let u = Ux.two(Bx.create(2))
	let k = pu(u) as isolated Bx
	try s.takeB(k) otherwise ignore
	match u 'x1'
		none then break
		one then break
		two(b) then b.n = 3
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:51:10: 'pu(u) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-ha-handle-closed -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ha-handle-closed` of redteamAM/hand_am4.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Th is unique
	var n as Num
	var h as Sh.handle

	static function create(n Num, h Sh.handle) returns Self
		return Self{n: n, h: h}
	end 'create'
end 'Th'

type Sh
	var b as Bx

	static function create(b isolated Bx) returns Self
		return Self{b: merge b}
	end 'create'

	export function get() returns Num
		return self.b.n
	end 'get'
end 'Sh'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function mkB(t Th) returns Bx
	return Bx.create(t.n)
end 'mkB'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let t = Th.create(1, h: spawn Sh.create(Bx.create(1)))
	let k = mkB(t) as isolated Bx
	try s.takeB(k) otherwise ignore
	t.n = 3
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-an-m1-iso-modify-nb-moves-whole -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `m1-iso-modify-nb-moves-whole` of redteamAN.hand_an1, in capmodel `reggio/REPORTRTAN.txt`.

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

function mkUb() returns isolated Ub
	return Ub.u1(Bx.create(1))
end 'mkUb'

function takeUbI(u isolated Ub)
	eatUb(u)
end 'takeUbI'

function main() returns ExitCode
	let k = mkUb() as isolated Ub
	modify k 'm1'
		match k 'x2'
			u0 then break
			u1 then takeUbI(k)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:37:20: 'k' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-an-n2-modify-holder-nb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `n2-modify-holder-nb` of redteamAN.hand_an1, in capmodel `reggio/REPORTRTAN.txt`.

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
	let k = Hb2.create(Ub.u1(Bx.create(1)), n: 0) as isolated Hb2
	modify k 'm1'
		match k.u 'x2'
			u0 then break
			u1 then k.u = Ub.u0
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-an-s1-call-outer-plain-payload-stored -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s1-call-outer-plain-payload-stored` of redteamAN.hand_an3, in capmodel `reggio/REPORTRTAN.txt`.

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

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

type Bk
	var n as Num
	var p as Pb
	var item as isolated Bx

	static function create(n Num, p Pb, item isolated Bx) returns Self
		return Self{n: n, p: p, item: item}
	end 'create'
end 'Bk'

function wrapI(b isolated Bx) returns isolated Ub
	return Ub.u1(b)
end 'wrapI'

function main() returns ExitCode
	let pV = Bx.create(1) as isolated Bx
	let bk = Bk.create(0, p: Pb.create(0), item: Bx.create(0)) as isolated Bk
	modify bk 'm1'
		match wrapI(pV) 'x2'
			u0 then break
			u1(t) then bk.item = t
		end 'x2'
	end 'm1'
	let _ = bk as isolated Bk
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-an-s1-call-outer-payload-stored-iso -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s1-call-outer-payload-stored-iso` of redteamAN.hand_an3, in capmodel `reggio/REPORTRTAN.txt`.

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

type Pb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Pb'

type Bk
	var n as Num
	var p as Pb
	var item as isolated Bx

	static function create(n Num, p Pb, item isolated Bx) returns Self
		return Self{n: n, p: p, item: item}
	end 'create'
end 'Bk'

function wrapI(b isolated Bx) returns isolated Ub
	return Ub.u1(b)
end 'wrapI'

function main() returns ExitCode
	let pV = Bx.create(1) as isolated Bx
	let bk = Bk.create(0, p: Pb.create(0), item: Bx.create(0)) as isolated Bk
	modify bk 'm1'
		match wrapI(pV) 'x2'
			u0 then break
			u1(o) then bk.item = o
		end 'x2'
	end 'm1'
	let _ = bk as isolated Bk
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-an-p1-swap-state -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-swap-state` of redteamAN.hand_an3, in capmodel `reggio/REPORTRTAN.txt`.

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

type Ks
	var u as isolated Ub
	var item as isolated Bx

	static function create(u isolated Ub, item isolated Bx) returns Self
		return Self{u: u, item: item}
	end 'create'

	export function go()
		match swap self.u with Ub.u0 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'go'

	export function peek()
		modify self.item 'm1'
			self.item.n = 1
		end 'm1'
	end 'peek'
end 'Ks'

function mkUb() returns isolated Ub
	return Ub.u1(Bx.create(1))
end 'mkUb'

function main() returns ExitCode
	let s = spawn Ks.create(mkUb(), item: Bx.create(0))
	try s.go() otherwise ignore
	try s.peek() otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-an-p1-cond-open-and-closed -->
The model (`core-m3w-fa24`) refuses it by `R4-PICK`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `p1-cond-open-and-closed` of redteamAN.hand_an3, in capmodel `reggio/REPORTRTAN.txt`.

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
	let q = mkUb() as isolated Ub
	modify p 'm1'
		match (p if lg(1) != 0 else q) 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:33:12: a join of an isolated value with data that is not isolated is refused: every arm must be isolated, or a fresh value
```

<!-- test: model-hand-fa15-m-ub-let-otherwin-use -->
The model (`core-m3w-fa24`) refuses it by `C-ISOREAD`; the compiler refuses it with E3199. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m-ub-let-otherwin-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Ub.u1(Bx.create(5)) as isolated Ub
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:83:23: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa15-m-ub-let-otherwin-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-let-otherwin-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Ub.u1(Bx.create(5)) as isolated Ub
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ub-let-otherwin-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-ub-let-otherwin-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Ub.u1(Bx.create(5)) as isolated Ub
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:88:26: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-ub-let-otherwin-write -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3199. Model program `m-ub-let-otherwin-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Ub.u1(Bx.create(5)) as isolated Ub
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then o.n = 3
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:83:15: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa15-m-ub-param-otherwin-use -->
The model (`core-m3w-fa24`) refuses it by `C-ISOREAD`; the compiler refuses it with E3199. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m-ub-param-otherwin-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(uV isolated Ub)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Ub.u1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:82:23: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa15-m-ub-param-otherwin-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-param-otherwin-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function f(uV isolated Ub)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Ub.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ub-param-otherwin-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-ub-param-otherwin-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function f(uV isolated Ub)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Ub.u1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:87:26: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-ub-param-otherwin-write -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3199. Model program `m-ub-param-otherwin-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(uV isolated Ub)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then o.n = 3
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Ub.u1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:82:15: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa15-m-mq-let-otherwin-use -->
The model (`core-m3w-fa24`) refuses it by `C-ISOREAD`; the compiler refuses it with E3199. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m-mq-let-otherwin-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Mq.q1(Bx.create(5)) as isolated Mq
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			q0(xa) then xa.n = 1
			q1(o) then let _ = o.n
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:83:23: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa15-m-mq-let-otherwin-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-let-otherwin-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Mq.q1(Bx.create(5)) as isolated Mq
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			q0(xa) then xa.n = 1
			q1(o) then takeB(o)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-let-otherwin-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-mq-let-otherwin-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Mq.q1(Bx.create(5)) as isolated Mq
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			q0(xa) then xa.n = 1
			q1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:88:26: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-mq-let-otherwin-write -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3199. Model program `m-mq-let-otherwin-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Mq.q1(Bx.create(5)) as isolated Mq
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			q0(xa) then xa.n = 1
			q1(o) then o.n = 3
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:83:15: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa15-m-mq-param-otherwin-use -->
The model (`core-m3w-fa24`) refuses it by `C-ISOREAD`; the compiler refuses it with E3199. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m-mq-param-otherwin-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(uV isolated Mq)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			q0(xa) then xa.n = 1
			q1(o) then let _ = o.n
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Mq.q1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:82:23: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa15-m-mq-param-otherwin-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-param-otherwin-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function f(uV isolated Mq)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			q0(xa) then xa.n = 1
			q1(o) then takeB(o)
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Mq.q1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-param-otherwin-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-mq-param-otherwin-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function f(uV isolated Mq)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			q0(xa) then xa.n = 1
			q1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Mq.q1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:87:26: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-mq-param-otherwin-write -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3199. Model program `m-mq-param-otherwin-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(uV isolated Mq)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			q0(xa) then xa.n = 1
			q1(o) then o.n = 3
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Mq.q1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:82:15: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa15-m-pc-let-otherwin-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-let-otherwin-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Pc.u1(Bx.create(5)) as isolated Pc
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-let-otherwin-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-let-otherwin-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Pc.u1(Bx.create(5)) as isolated Pc
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-let-otherwin-readfn -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-let-otherwin-readfn` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function readB(b Bx)
	let _ = b.n
end 'readB'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Pc.u1(Bx.create(5)) as isolated Pc
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then readB(o)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-let-otherwin-twice -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `m-pc-let-otherwin-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Pc.u1(Bx.create(5)) as isolated Pc
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:88:23: 'o' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa15-m-pc-param-otherwin-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-param-otherwin-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(uV isolated Pc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Pc.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-param-otherwin-readfn -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-param-otherwin-readfn` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function readB(b Bx)
	let _ = b.n
end 'readB'

function f(uV isolated Pc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then readB(o)
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Pc.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-param-otherwin-twice -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `m-pc-param-otherwin-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function f(uV isolated Pc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Pc.u1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:87:23: 'o' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa15-m-ut-let-otherwin-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ut-let-otherwin-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

union UT
	none
	have(t Tk)
end 'UT'

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

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = UT.have(Tk.create(5)) as isolated UT
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			none then break
			have(o) then let _ = o.n
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ut-param-otherwin-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ut-param-otherwin-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Pc
	u0
	u1(b Bx)
end 'Pc'

union UT
	none
	have(t Tk)
end 'UT'

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

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(uV isolated UT)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			none then break
			have(o) then let _ = o.n
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(UT.have(Tk.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-let-otherwin-use -->
The model (`core-m3w-fa24`) refuses it by `C-ISOREAD`; the compiler refuses it with E3199. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m-vc-let-otherwin-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Vc
	u0
	u1(b isolated Bx)
end 'Vc'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Vc.u1(Bx.create(5)) as isolated Vc
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:88:23: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa15-m-vc-let-otherwin-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-let-otherwin-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Vc
	u0
	u1(b isolated Bx)
end 'Vc'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Vc.u1(Bx.create(5)) as isolated Vc
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-let-otherwin-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-vc-let-otherwin-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Vc
	u0
	u1(b isolated Bx)
end 'Vc'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Vc.u1(Bx.create(5)) as isolated Vc
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:93:26: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-vc-let-otherwin-write -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3199. Model program `m-vc-let-otherwin-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Vc
	u0
	u1(b isolated Bx)
end 'Vc'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let uV = Vc.u1(Bx.create(5)) as isolated Vc
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then o.n = 3
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:88:15: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa15-m-vc-param-otherwin-use -->
The model (`core-m3w-fa24`) refuses it by `C-ISOREAD`; the compiler refuses it with E3199. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m-vc-param-otherwin-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Vc
	u0
	u1(b isolated Bx)
end 'Vc'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(uV isolated Vc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Vc.u1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:87:23: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa15-m-vc-param-otherwin-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-param-otherwin-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Vc
	u0
	u1(b isolated Bx)
end 'Vc'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function f(uV isolated Vc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Vc.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-param-otherwin-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-vc-param-otherwin-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Vc
	u0
	u1(b isolated Bx)
end 'Vc'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function f(uV isolated Vc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Vc.u1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:92:26: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-vc-param-otherwin-write -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3199. Model program `m-vc-param-otherwin-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

union Vc
	u0
	u1(b isolated Bx)
end 'Vc'

union Pc
	u0
	u1(b Bx)
end 'Pc'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

type Kw
	var n as Num
	var keep as Bx
	var item as isolated Bx

	static function create(n Num, keep isolated Bx, item isolated Bx) returns Self
		return Self{n: n, keep: merge keep, item: item}
	end 'create'

	export function putB(b isolated Bx)
		self.item = b
	end 'putB'

	export function eatUb(uV isolated Ub)
		match uV 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(uV isolated Pc)
		match uV 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(uV isolated Mq)
		match uV 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(uV isolated Vc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then o.n = 3
		end 'x2'
	end 'm1'
end 'f'

function main() returns ExitCode
	f(Vc.u1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:87:15: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa15-o-own-modify-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `o-own-modify-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	modify u 'm1'
		match u 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-o-own-modify-then-consume -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `o-own-modify-then-consume` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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
	modify u 'm1'
		let _ = 1 as Num
	end 'm1'
	match u 'x2'
		u0 then break
		u1(o) then takeB(o)
	end 'x2'
	match u 'x3'
		u0 then break
		u1(o) then takeB(o)
	end 'x3'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:8: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-o-own-modify-plain -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `o-own-modify-plain` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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
	let u = Pc.u1(Bx.create(5)) as isolated Pc
	modify u 'm1'
		match u 'x2'
			u0 then break
			u1(o) then o.n = 2
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-c10-window-iso-store -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `c10-window-iso-store` of redteamFA15.hand_fa15c, in capmodel `reggio/REPORTFA15.txt`.

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

function main() returns ExitCode
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	let uV = Ub.u1(Bx.create(5)) as isolated Ub
	modify h 'm1'
		match uV 'x2'
			u0 then break
			u1(o) then h.item = o
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-c29-mixed-plain-arm-keep -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `c29-mixed-plain-arm-keep` of redteamFA15.hand_fa15c, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Ub
	u0
	u1(b isolated Bx)
end 'Ub'

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

type Wm
	var keep as Bx
	var item as isolated Bx
	var u as Ub

	static function create(keep Bx, item isolated Bx, u Ub) returns Self
		return Self{keep: keep, item: item, u: u}
	end 'create'
end 'Wm'

function main() returns ExitCode
	let h = Wm.create(Bx.create(0), item: Bx.create(1), u: Ub.u0) as isolated Wm
	let uV = Mq.q0(Bx.create(5)) as isolated Mq
	modify h 'm1'
		match uV 'x2'
			q0(x) then h.keep = x
			q1(y) then h.item = y
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa16-h3-onecall -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `h3-onecall` of redteamFA16.hand_fa16c, in capmodel `reggio/REPORTFA16.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Iq6
	i0
	i1(w isolated Wo6)
end 'Iq6'

type Bx6
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx6'

type Wo6
	var keep as Bx6
	var item as isolated Bx6

	static function create(keep Bx6, item isolated Bx6) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo6'

function readWo(w Wo6)
	let _ = w.keep.n
end 'readWo'

function mkW() returns isolated Wo6
	return Wo6.create(Bx6.create(0), item: Bx6.create(1))
end 'mkW'

function scn()
	let h = mkW() as isolated Wo6
	let u = Iq6.i1(mkW()) as isolated Iq6
	modify h 'm1'
		match u 'x2'
			i0 then break
			i1(o) then readWo(o)
		end 'x2'
	end 'm1'
end 'scn'

function main() returns ExitCode
	scn()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:40:22: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa16-h3-onestmt -->
The model (`core-m3w-fa24`) refuses it by `C-ISOREAD`; the compiler refuses it with E3199. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `h3-onestmt` of redteamFA16.hand_fa16c, in capmodel `reggio/REPORTFA16.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Iq6
	i0
	i1(w isolated Wo6)
end 'Iq6'

type Bx6
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx6'

type Wo6
	var keep as Bx6
	var item as isolated Bx6

	static function create(keep Bx6, item isolated Bx6) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo6'

function mkW() returns isolated Wo6
	return Wo6.create(Bx6.create(0), item: Bx6.create(1))
end 'mkW'

function scn()
	let h = mkW() as isolated Wo6
	let u = Iq6.i1(mkW()) as isolated Iq6
	modify h 'm1'
		match u 'x2'
			i0 then break
			i1(o) then o.keep.n = 4
		end 'x2'
	end 'm1'
end 'scn'

function main() returns ExitCode
	scn()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:36:15: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa16-rename-let -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `rename-let` of redteamFA16.hand_fa16c, in capmodel `reggio/REPORTFA16.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx6
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx6'

type Wo6
	var keep as Bx6
	var item as isolated Bx6

	static function create(keep Bx6, item isolated Bx6) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo6'

function mkW() returns isolated Wo6
	return Wo6.create(Bx6.create(0), item: Bx6.create(1))
end 'mkW'

function scn()
	let h = mkW() as isolated Wo6
	let o = mkW() as isolated Wo6
	modify h 'm1'
		let p = o as isolated Wo6
		modify p 'm2'
			let _ = Wo6.create(p.keep, item: Bx6.create(1))
		end 'm2'
	end 'm1'
end 'scn'

function main() returns ExitCode
	scn()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:30:10: 'p' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa16-rename-call -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `rename-call` of redteamFA16.hand_fa16c, in capmodel `reggio/REPORTFA16.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx6
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx6'

type Wo6
	var keep as Bx6
	var item as isolated Bx6

	static function create(keep Bx6, item isolated Bx6) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo6'

function idWo(w isolated Wo6) returns isolated Wo6
	return w
end 'idWo'

function mkW() returns isolated Wo6
	return Wo6.create(Bx6.create(0), item: Bx6.create(1))
end 'mkW'

function scn()
	let h = mkW() as isolated Wo6
	let o = mkW() as isolated Wo6
	modify h 'm1'
		let p = idWo(o)
		modify p 'm2'
			let _ = Wo6.create(p.keep, item: Bx6.create(1))
		end 'm2'
	end 'm1'
end 'scn'

function main() returns ExitCode
	scn()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:34:10: 'p' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa16-plain-payload-window-data -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `plain-payload-window-data` of redteamFA16.hand_fa16c, in capmodel `reggio/REPORTFA16.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Pq6
	p0
	p1(w Wo6)
end 'Pq6'

type Bx6
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx6'

type Wo6
	var keep as Bx6
	var item as isolated Bx6

	static function create(keep Bx6, item isolated Bx6) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo6'

function mkW() returns isolated Wo6
	return Wo6.create(Bx6.create(0), item: Bx6.create(1))
end 'mkW'

function scn()
	let h = mkW() as isolated Wo6
	let u = Pq6.p1(Wo6.create(Bx6.create(0), item: Bx6.create(1))) as isolated Pq6
	modify h 'm1'
		match u 'x2'
			p0 then break
			p1(o) then let _ = Wo6.create(o.keep, item: Bx6.create(1))
		end 'x2'
	end 'm1'
end 'scn'

function main() returns ExitCode
	scn()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa16-ctl-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ctl-take` of redteamFA16.hand_fa16c, in capmodel `reggio/REPORTFA16.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Iq6
	i0
	i1(w isolated Wo6)
end 'Iq6'

type Bx6
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx6'

type Wo6
	var keep as Bx6
	var item as isolated Bx6

	static function create(keep Bx6, item isolated Bx6) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo6'

function takeWo(w isolated Wo6)
	let _ = w
	let _ = 1 as Num
end 'takeWo'

function mkW() returns isolated Wo6
	return Wo6.create(Bx6.create(0), item: Bx6.create(1))
end 'mkW'

function scn()
	let h = mkW() as isolated Wo6
	let u = Iq6.i1(mkW()) as isolated Iq6
	modify h 'm1'
		match u 'x2'
			i0 then break
			i1(o) then takeWo(o)
		end 'x2'
	end 'm1'
end 'scn'

function main() returns ExitCode
	scn()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa16-ctl-slot -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ctl-slot` of redteamFA16.hand_fa16c, in capmodel `reggio/REPORTFA16.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Iq6
	i0
	i1(w isolated Wo6)
end 'Iq6'

type Bx6
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx6'

type Wo6
	var keep as Bx6
	var item as isolated Bx6

	static function create(keep Bx6, item isolated Bx6) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo6'

type Hq6
	var slot as isolated Wo6

	static function create(slot isolated Wo6) returns Self
		return Self{slot: slot}
	end 'create'
end 'Hq6'

function mkW() returns isolated Wo6
	return Wo6.create(Bx6.create(0), item: Bx6.create(1))
end 'mkW'

function scn()
	let _ = mkW() as isolated Wo6
	let u = Iq6.i1(mkW()) as isolated Iq6
	let g = Hq6.create(mkW()) as isolated Hq6
	modify g 'm1'
		match u 'x2'
			i0 then break
			i1(o) then g.slot = o
		end 'x2'
	end 'm1'
end 'scn'

function main() returns ExitCode
	scn()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa16-ctl-rename-fresh -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ctl-rename-fresh` of redteamFA16.hand_fa16c, in capmodel `reggio/REPORTFA16.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx6
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx6'

type Wo6
	var keep as Bx6
	var item as isolated Bx6

	static function create(keep Bx6, item isolated Bx6) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo6'

function mkW() returns isolated Wo6
	return Wo6.create(Bx6.create(0), item: Bx6.create(1))
end 'mkW'

function scn()
	let h = mkW() as isolated Wo6
	modify h 'm1'
		let p = mkW() as isolated Wo6
		modify p 'm2'
			let _ = Wo6.create(p.keep, item: Bx6.create(1))
		end 'm2'
	end 'm1'
end 'scn'

function main() returns ExitCode
	scn()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa16-ctl-rename-then-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ctl-rename-then-take` of redteamFA16.hand_fa16c, in capmodel `reggio/REPORTFA16.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx6
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx6'

type Wo6
	var keep as Bx6
	var item as isolated Bx6

	static function create(keep Bx6, item isolated Bx6) returns Self
		return Self{keep: keep, item: item}
	end 'create'
end 'Wo6'

function takeWo(w isolated Wo6)
	let _ = w
	let _ = 1 as Num
end 'takeWo'

function mkW() returns isolated Wo6
	return Wo6.create(Bx6.create(0), item: Bx6.create(1))
end 'mkW'

function scn()
	let h = mkW() as isolated Wo6
	let o = mkW() as isolated Wo6
	modify h 'm1'
		let p = o as isolated Wo6
		takeWo(p)
	end 'm1'
end 'scn'

function main() returns ExitCode
	scn()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-w-outer-further -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `w-outer-further` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

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

function wrUT(x UT)
	match x 'x1'
		none then break
		have(z) then z.n = 5
	end 'x1'
end 'wrUT'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let g = Bx.create(1) as isolated Bx
	modify g 'm1'
		match u 'x2'
			none then break
			have then wrUT(u)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:38:19: 'wrUT' needs a writable parameter 'x' at line 28; 'u' is read-only while 'm1' is open
```

<!-- test: model-hand-fa17-o-plain-fresh -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `o-plain-fresh` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	var o = Bx.create(1)
	let h = Bx.create(2) as isolated Bx
	modify h 'm1'
		o = Bx.create(3)
	end 'm1'
	let _ = o.n
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:16:3: 'o' holds a record from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-hand-fa17-o-union -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `o-union` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

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
	var o = Ub.u0
	let h = Bx.create(2) as isolated Bx
	modify h 'm1'
		o = Ub.u0
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:21:3: 'o' holds a union from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-hand-fa17-o-iso -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `o-iso` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	var o = Bx.create(1) as isolated Bx
	let h = Bx.create(2) as isolated Bx
	modify h 'm1'
		o = Bx.create(3)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:16:3: 'o' holds an isolated value from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-hand-fa17-o-unique -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `o-unique` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
	var o = Tk.create(1)
	let h = Bx.create(2) as isolated Bx
	modify h 'm1'
		o = Tk.create(3)
	end 'm1'
	let _ = o.n
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:24:3: 'o' holds a unique value from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-hand-fa17-o-linear -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `o-linear` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

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

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	var o = Rl.create(1)
	o.fin(1)
	let h = Bx.create(2) as isolated Bx
	modify h 'm1'
		o = Rl.create(3)
	end 'm1'
	o.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:47:3: 'o' holds a linear value from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-hand-fa17-o-val -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `o-val` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	var o = 1 as Num
	let h = Bx.create(2) as isolated Bx
	modify h 'm1'
		o = 3
	end 'm1'
	let _ = o
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-o-before-after -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `o-before-after` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	var o = Bx.create(1)
	o = Bx.create(2)
	let h = Bx.create(2) as isolated Bx
	modify h 'm1'
		h.n = 3
	end 'm1'
	o = Bx.create(4)
	let _ = o.n
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-o-inner-local -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `o-inner-local` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let h = Bx.create(2) as isolated Bx
	modify h 'm1'
		var o = Bx.create(1)
		o = Bx.create(3)
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa18-aj-h1-control-plain-param -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `aj-h1-control-plain-param` of redteamFA18.hand_fa18, in capmodel `reggio/REPORTFA18.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function g(o isolated Wo)
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	modify h 'm1'
		modify o 'm2'
			let _ = Wo.create(o.keep, item: Bx.create(1))
		end 'm2'
	end 'm1'
end 'g'

function main() returns ExitCode
	g(Wo.create(Bx.create(0), item: Bx.create(1)))
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:24:10: 'o' cannot be opened: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa18-m7-swap-outer-iso-field-in-window -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3199. Model program `m7-swap-outer-iso-field-in-window` of redteamFA18.hand_fa18, in capmodel `reggio/REPORTFA18.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Wo
	var keep as isolated Bx

	static function create(keep isolated Bx) returns Self
		return Self{keep: keep}
	end 'create'
end 'Wo'

function takeBx(b isolated Bx)
	modify b 'm1'
		b.n = 3
	end 'm1'
end 'takeBx'

function main() returns ExitCode
	let w = Wo.create(Bx.create(0))
	let b = Bx.create(1) as isolated Bx
	modify b 'm1'
		let o = swap w.keep with Bx.create(2)
		takeBx(o)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:30:11: 'w.keep' cannot be swapped: the value that holds it is read-only while 'm1' is open
```

<!-- test: model-hand-fa18-m7-outer-immutable-var-assigned -->
The model (`core-m3w-fa24`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `m7-outer-immutable-var-assigned` of redteamFA18.hand_fa18, in capmodel `reggio/REPORTFA18.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	var p = freeze (Bx.create(1))
	let b = Bx.create(1) as isolated Bx
	modify b 'm1'
		p = freeze (Bx.create(2))
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:16:3: 'p' holds immutable data from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-hand-fa19-x-arm-stored-then-outer-sent -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-arm-stored-then-outer-sent` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let o = Ko.create(Bx.create(0), n: 0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then break
		have(t) then o.keep = t.b
	end 'x1'
	try s.takeK(o) otherwise ignore
	match u 'x2'
		none then break
		have(t) then t.b.n = 3
	end 'x2'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:14: 'o' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa19-x-arm-outer-var-refilled-then-sent -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-arm-outer-var-refilled-then-sent` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	var o = Ko.create(Bx.create(0), n: 0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then break
		have(t) then o = Ko.create(t.b, n: 1)
	end 'x1'
	try s.takeK(o) otherwise ignore
	match u 'x2'
		none then break
		have(t) then t.b.n = 3
	end 'x2'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:14: 'o' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa19-x-arm-plain-part-sent -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-arm-plain-part-sent` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then break
		have(t) then try s.takeB(t.b) otherwise ignore
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:45:28: 't.b' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa19-x-arm-param-then-outer-isolated -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-arm-param-then-outer-isolated` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(u UQ, o Ko)
	match u 'x1'
		none then break
		have(t) then o.keep = t.b
	end 'x1'
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let o = Ko.create(Bx.create(0), n: 0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	stash(u, o: o)
	let k = o as isolated Ko
	try s.takeK(k) otherwise ignore
	match u 'x1'
		none then break
		have(t) then t.b.n = 3
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'o as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa19-x-arm-callee-fresh-result -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-arm-callee-fresh-result` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(u UQ) returns Ko
	let o = Ko.create(Bx.create(0), n: 0)
	match u 'x1'
		none then break
		have(t) then o.keep = t.b
	end 'x1'
	return o
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	let k = stash(u) as isolated Ko
	try s.takeK(k) otherwise ignore
	match u 'x1'
		none then break
		have(t) then t.b.n = 3
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'stash(u) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa19-x-arm-callee-returns-plain-part -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-arm-callee-returns-plain-part` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(u UQ) returns Bx
	match u 'x1'
		none then break
		have(t) then return t.b
	end 'x1'
	return Bx.create(0)
end 'part'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	let k = part(u) as isolated Bx
	try s.takeB(k) otherwise ignore
	match u 'x1'
		none then break
		have(t) then t.b.n = 3
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:51:10: 'part(u) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa19-x-arm-onecall-into-opened -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `x-arm-onecall-into-opened` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function put(k Ko, q Tq)
	k.keep = q.b
end 'put'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	let k = Ko.create(Bx.create(0), n: 0) as isolated Ko
	match u 'x1'
		none then break
		have(t) then put(k, q: t)
	end 'x1'
	try s.takeK(k) otherwise ignore
	match u 'x2'
		none then break
		have(t) then t.b.n = 3
	end 'x2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:59:26: 't' is mutable outside 'k'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-fa19-x-for-stored-then-outer-sent -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-for-stored-then-outer-sent` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let o = Ko.create(Bx.create(0), n: 0)
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	for t in xs 'f1'
		o.keep = t.b
	end 'f1'
	try s.takeK(o) otherwise ignore
	for e in xs 'f2'
		e.b.n = 3
	end 'f2'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:53:14: 'o' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa19-x-for-plain-part-sent -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-for-plain-part-sent` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	for t in xs 'f1'
		try s.takeB(t.b) otherwise ignore
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:41:15: 't.b' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa19-x-for-callee-fresh-result -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-for-callee-fresh-result` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(xs TqArray) returns Ko
	let o = Ko.create(Bx.create(0), n: 0)
	for e in xs 'f1'
		o.keep = e.b
	end 'f1'
	return o
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	let k = stash(xs) as isolated Ko
	try s.takeK(k) otherwise ignore
	for e in xs 'f1'
		e.b.n = 3
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: 'stash(xs) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa19-x-for-onecall-into-opened -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `x-for-onecall-into-opened` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function put(k Ko, q Tq)
	k.keep = q.b
end 'put'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	let k = Ko.create(Bx.create(0), n: 0) as isolated Ko
	for t in xs 'f1'
		put(k, q: t)
	end 'f1'
	try s.takeK(k) otherwise ignore
	for e in xs 'f2'
		e.b.n = 3
	end 'f2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:55:13: 't' is mutable outside 'k'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-fa19-x-for-modify-stores-plain-part -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `x-for-modify-stores-plain-part` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

function main() returns ExitCode
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	let k = Ko.create(Bx.create(0), n: 0) as isolated Ko
	for t in xs 'f1'
		modify k 'm2'
			k.keep = t.b
		end 'm2'
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:37:13: cannot store a read-only 'Bx' into 'keep' while 'm2' is open; store a copy
```

<!-- test: model-hand-fa19-x-direct-lent-fresh-result -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-direct-lent-fresh-result` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	let o = Ko.create(Bx.create(0), n: 0)
	o.keep = t.b
	return o
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = stash(q) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:53:10: 'stash(q) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa19-n-modify-arm-outer-write -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `n-modify-arm-outer-write` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Wu
	var u as UQ
	var n as Num

	static function create(u UQ, n Num) returns Self
		return Self{u: u, n: n}
	end 'create'
end 'Wu'

function main() returns ExitCode
	let o = Bx.create(1)
	let w = Wu.create(UQ.have(Tq.create(1, b: Bx.create(2))), n: 0) as isolated Wu
	modify w 'm1'
		match w.u 'x2'
			none then break
			have then o.n = 5
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:41:14: cannot write 'n' through 'o', which is read-only while 'm1' is open
```

<!-- test: model-hand-fa19-n-modify-for-outer-write -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `n-modify-for-outer-write` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

type Wa
	var xs as TqArray
	var n as Num

	static function create(xs TqArray, n Num) returns Self
		return Self{xs: xs, n: n}
	end 'create'
end 'Wa'

function main() returns ExitCode
	let o = Bx.create(1)
	let w = Wa.create(TqArray.create(0), n: 0) as isolated Wa
	modify w 'm1'
		w.xs.push(Tq.create(1, b: Bx.create(2)))
		for _ in w.xs 'f2'
			o.n = 5
		end 'f2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:37:4: cannot write 'n' through 'o', which is read-only while 'm1' is open
```

<!-- test: model-hand-fa19-n-modify-for-binding-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `n-modify-for-binding-write` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TqArray = Array with Tq

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

type Wa
	var xs as TqArray
	var n as Num

	static function create(xs TqArray, n Num) returns Self
		return Self{xs: xs, n: n}
	end 'create'
end 'Wa'

function main() returns ExitCode
	let w = Wa.create(TqArray.create(0), n: 0) as isolated Wa
	modify w 'm1'
		w.xs.push(Tq.create(1, b: Bx.create(2)))
		for t in w.xs 'f2'
			t.n = 2
			t.b.n = 3
		end 'f2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p1-stash-same -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-stash-same` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = stash((q if 1 != 0 else q)) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'stash((q if 1 != 0 else q)) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-part-same -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-part-same` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = part((q if 1 != 0 else q)) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'part((q if 1 != 0 else q)) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-recv-mkk-same -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-recv-mkK-same` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = (q if 1 != 0 else q).mkK() as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: '(q if 1 != 0 else q).mkK() as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-recv-part-same -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-recv-part-same` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = (q if 1 != 0 else q).part() as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: '(q if 1 != 0 else q).part() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-stash-two -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-stash-two` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = stash((q if 1 != 0 else q2)) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'stash((q if 1 != 0 else q2)) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-part-two -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-part-two` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = part((q if 1 != 0 else q2)) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'part((q if 1 != 0 else q2)) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-recv-mkk-two -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-recv-mkK-two` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = (q if 1 != 0 else q2).mkK() as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: '(q if 1 != 0 else q2).mkK() as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-recv-part-two -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-recv-part-two` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = (q if 1 != 0 else q2).part() as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: '(q if 1 != 0 else q2).part() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-stash-fresh -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-stash-fresh` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = stash((q if 1 != 0 else Tq.create(1, b: Bx.create(2)))) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'stash((q if 1 != 0 else Tq.create(1, b: Bx.create(2)))) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-part-fresh -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-part-fresh` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = part((q if 1 != 0 else Tq.create(1, b: Bx.create(2)))) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'part((q if 1 != 0 else Tq.create(1, b: Bx.create(2)))) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-recv-mkk-fresh -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-recv-mkK-fresh` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = (q if 1 != 0 else Tq.create(1, b: Bx.create(2))).mkK() as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: '(q if 1 != 0 else Tq.create(1, b: Bx.create(2))).mkK() as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-recv-part-fresh -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-recv-part-fresh` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = (q if 1 != 0 else Tq.create(1, b: Bx.create(2))).part() as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: '(q if 1 != 0 else Tq.create(1, b: Bx.create(2))).part() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-stash-fresh-first -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-stash-fresh-first` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = stash((Tq.create(1, b: Bx.create(2)) if 1 != 0 else q)) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'stash((Tq.create(1, b: Bx.create(2)) if 1 != 0 else q)) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-part-fresh-first -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-part-fresh-first` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = part((Tq.create(1, b: Bx.create(2)) if 1 != 0 else q)) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'part((Tq.create(1, b: Bx.create(2)) if 1 != 0 else q)) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-recv-mkk-fresh-first -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-recv-mkK-fresh-first` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = (Tq.create(1, b: Bx.create(2)) if 1 != 0 else q).mkK() as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: '(Tq.create(1, b: Bx.create(2)) if 1 != 0 else q).mkK() as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-recv-part-fresh-first -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-recv-part-fresh-first` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = (Tq.create(1, b: Bx.create(2)) if 1 != 0 else q).part() as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: '(Tq.create(1, b: Bx.create(2)) if 1 != 0 else q).part() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-stash-nested -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-stash-nested` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let q3 = Tq.create(1, b: Bx.create(2))
	let k = stash(((q2 if 0 != 0 else q) if 1 != 0 else q3)) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'stash(((q2 if 0 != 0 else q) if 1 != 0 else q3)) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-part-nested -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-part-nested` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let q3 = Tq.create(1, b: Bx.create(2))
	let k = part(((q2 if 0 != 0 else q) if 1 != 0 else q3)) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'part(((q2 if 0 != 0 else q) if 1 != 0 else q3)) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-recv-mkk-nested -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-recv-mkK-nested` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let q3 = Tq.create(1, b: Bx.create(2))
	let k = ((q2 if 0 != 0 else q) if 1 != 0 else q3).mkK() as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: '((q2 if 0 != 0 else q) if 1 != 0 else q3).mkK() as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-recv-part-nested -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-recv-part-nested` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let q3 = Tq.create(1, b: Bx.create(2))
	let k = ((q2 if 0 != 0 else q) if 1 != 0 else q3).part() as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: '((q2 if 0 != 0 else q) if 1 != 0 else q3).part() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-stash-nested-right -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-stash-nested-right` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function stash(t Tq) returns Ko
	return Ko.create(t.b, n: 1)
end 'stash'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let q3 = Tq.create(1, b: Bx.create(2))
	let k = stash((q3 if 1 != 0 else (q if 0 != 0 else q2))) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'stash((q3 if 1 != 0 else (q if 0 != 0 else q2))) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-part-nested-right -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-part-nested-right` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let q3 = Tq.create(1, b: Bx.create(2))
	let k = part((q3 if 1 != 0 else (q if 0 != 0 else q2))) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'part((q3 if 1 != 0 else (q if 0 != 0 else q2))) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-recv-mkk-nested-right -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-recv-mkK-nested-right` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let q3 = Tq.create(1, b: Bx.create(2))
	let k = (q3 if 1 != 0 else (q if 0 != 0 else q2)).mkK() as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: '(q3 if 1 != 0 else (q if 0 != 0 else q2)).mkK() as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-recv-part-nested-right -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-recv-part-nested-right` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let q3 = Tq.create(1, b: Bx.create(2))
	let k = (q3 if 1 != 0 else (q if 0 != 0 else q2)).part() as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: '(q3 if 1 != 0 else (q if 0 != 0 else q2)).part() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-cond-two-args -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-cond-two-args` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part2(t Tq, u Tq) returns Bx
	let _ = u.n
	return t.b
end 'part2'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = part2((q if 1 != 0 else q2), u: Tq.create(1, b: Bx.create(2))) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:62:10: 'part2((q if 1 != 0 else q2), u: Tq.create(1, b: Bx.create(2))) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p1-cond-in-method-chain -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p1-cond-in-method-chain` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let k = Ko.create((q if 1 != 0 else q2).part(), n: 1) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:57:10: 'Ko.create((q if 1 != 0 else q2).part(), n: 1) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p2-closed-cond-lent -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-closed-cond-lent` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tc is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mkB() returns Bx
		return Bx.create(self.n)
	end 'mkB'
end 'Tc'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		modify b 'm1'
			b.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function mkB(t Tc) returns Bx
	return Bx.create(t.n)
end 'mkB'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tc.create(1)
	let q2 = Tc.create(2)
	let k = mkB((q if 1 != 0 else q2)) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.n = 3
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p2-closed-cond-recv -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-closed-cond-recv` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tc is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mkB() returns Bx
		return Bx.create(self.n)
	end 'mkB'
end 'Tc'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		modify b 'm1'
			b.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tc.create(1)
	let q2 = Tc.create(2)
	let k = (q if 1 != 0 else q2).mkB() as isolated Bx
	try s.takeB(k) otherwise ignore
	q.n = 3
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p2-cond-of-values -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-cond-of-values` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		modify b 'm1'
			b.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function fb(b Bx) returns Bx
	return b
end 'fb'

function main() returns ExitCode
	let _ = spawn Sbq.create(0)
	let a = Bx.create(1)
	let b = Bx.create(2)
	let k = fb((a if 1 != 0 else b))
	k.n = 4
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p3-lent-closed-direct -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p3-lent-closed-direct` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tc is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mkB() returns Bx
		return Bx.create(self.n)
	end 'mkB'
end 'Tc'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		modify b 'm1'
			b.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function mkB(t Tc) returns Bx
	return Bx.create(t.n)
end 'mkB'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tc.create(1)
	let k = mkB(q) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.n = 3
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p3-lent-closed-recv -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p3-lent-closed-recv` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tc is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mkB() returns Bx
		return Bx.create(self.n)
	end 'mkB'
end 'Tc'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		modify b 'm1'
			b.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tc.create(1)
	let k = q.mkB() as isolated Bx
	try s.takeB(k) otherwise ignore
	q.n = 3
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p3-lent-closed-two-step -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p3-lent-closed-two-step` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tc is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mkB() returns Bx
		return Bx.create(self.n)
	end 'mkB'
end 'Tc'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		modify b 'm1'
			b.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function mkB(t Tc) returns Bx
	return Bx.create(t.n)
end 'mkB'

function mkB2(t Tc) returns Bx
	return mkB(t)
end 'mkB2'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tc.create(1)
	let k = mkB2(q) as isolated Bx
	try s.takeB(k) otherwise ignore
	q.n = 3
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p3-kept-plain -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p3-kept-plain` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	let qV = Tq.create(1, b: Bx.create(2))
	let w = keepW(qV) as isolated Wq
	try s.takeW(w) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:67:10: 'keepW(qV) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p3-kept-plain-returned -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p3-kept-plain-returned` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Stq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeT(w isolated Tq)
		modify w 'm1'
			w.b.n = 9
		end 'm1'
	end 'takeT'
end 'Stq'

function idT(t Tq) returns Tq
	return t
end 'idT'

function main() returns ExitCode
	let s = spawn Stq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let w = idT(q) as isolated Tq
	try s.takeT(w) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:59:10: 'idT(q) as isolated Tq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p3-kept-plain-two-step -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p3-kept-plain-two-step` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function keepW2(t Tq) returns Wq
	return keepW(t)
end 'keepW2'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	let qV = Tq.create(1, b: Bx.create(2))
	let w = keepW2(qV) as isolated Wq
	try s.takeW(w) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:71:10: 'keepW2(qV) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p3-kept-plain-alias -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `p3-kept-plain-alias` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	let qV = Tq.create(1, b: Bx.create(2))
	let bV = qV.b
	let w = keepW(qV) as isolated Wq
	try s.takeW(w) otherwise ignore
	bV.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:68:10: 'keepW(qV) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa20-p3-kept-closed -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p3-kept-closed` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tc is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mkB() returns Bx
		return Bx.create(self.n)
	end 'mkB'
end 'Tc'

type Wc is unique
	var c as Tc

	static function create(c Tc) returns Self
		return Self{c: c}
	end 'create'
end 'Wc'

type Swc
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wc)
		modify w 'm1'
			w.c.n = 9
		end 'm1'
	end 'takeW'
end 'Swc'

function keepC(t Tc) returns Wc
	return Wc.create(t)
end 'keepC'

function main() returns ExitCode
	let s = spawn Swc.create(0)
	let q = Tc.create(1)
	let w = keepC(q) as isolated Wc
	try s.takeW(w) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p3-kept-temp -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p3-kept-temp` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Wq is unique
	var q as Tq

	static function create(q Tq) returns Self
		return Self{q: q}
	end 'create'
end 'Wq'

type Swq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wq)
		modify w 'm1'
			w.q.b.n = 9
		end 'm1'
	end 'takeW'
end 'Swq'

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function main() returns ExitCode
	let s = spawn Swq.create(0)
	let w = keepW(Tq.create(1, b: Bx.create(2))) as isolated Wq
	try s.takeW(w) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa21-a1-sync-control -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `a1-sync-control` of redteamFA21.hand_fa21, in capmodel `reggio/REPORTFA21.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let k = part(q) as isolated Bx
	try s.takeB(k) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:42:10: 'part(q) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa21-r3-take-first -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `r3-take-first` of redteamFA21.hand_fa21, in capmodel `reggio/REPORTFA21.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Lq
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'

	function take(consuming self) returns Bx
		let x = self.b
		destruct self
		return x
	end 'take'

	function whole(consuming self) returns Ko
		let o = Ko.create(self.b, n: 1)
		destruct self
		return o
	end 'whole'

	function look() returns Bx
		return self.b
	end 'look'
end 'Lq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Lq.create(1, b: Bx.create(2))
	let k = q.take() as isolated Bx
	try s.takeB(k) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:63:10: 'q.take() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa21-r3-whole -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `r3-whole` of redteamFA21.hand_fa21, in capmodel `reggio/REPORTFA21.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Lq
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'

	function take(consuming self) returns Bx
		let x = self.b
		destruct self
		return x
	end 'take'

	function whole(consuming self) returns Ko
		let o = Ko.create(self.b, n: 1)
		destruct self
		return o
	end 'whole'

	function look() returns Bx
		return self.b
	end 'look'
end 'Lq'

type Skq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeK(k isolated Ko)
		modify k 'm1'
			k.keep.n = 9
		end 'm1'
	end 'takeK'
end 'Skq'

function main() returns ExitCode
	let s = spawn Skq.create(0)
	let q = Lq.create(1, b: Bx.create(2))
	let k = q.whole() as isolated Ko
	try s.takeK(k) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:63:10: 'q.whole() as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa21-r3-take-alias -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `r3-take-alias` of redteamFA21.hand_fa21, in capmodel `reggio/REPORTFA21.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Lq
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'

	function take(consuming self) returns Bx
		let x = self.b
		destruct self
		return x
	end 'take'

	function whole(consuming self) returns Ko
		let o = Ko.create(self.b, n: 1)
		destruct self
		return o
	end 'whole'

	function look() returns Bx
		return self.b
	end 'look'
end 'Lq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Lq.create(1, b: Bx.create(2))
	let a = q.b
	let k = q.take() as isolated Bx
	try s.takeB(k) otherwise ignore
	a.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:64:10: 'q.take() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa21-r3-fn-take -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `r3-fn-take` of redteamFA21.hand_fa21, in capmodel `reggio/REPORTFA21.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Lq
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'

	function take(consuming self) returns Bx
		let x = self.b
		destruct self
		return x
	end 'take'

	function whole(consuming self) returns Ko
		let o = Ko.create(self.b, n: 1)
		destruct self
		return o
	end 'whole'

	function look() returns Bx
		return self.b
	end 'look'
end 'Lq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function tk(l Lq) returns Bx
	return l.take()
end 'tk'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Lq.create(1, b: Bx.create(2))
	let k = tk(q) as isolated Bx
	try s.takeB(k) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:67:10: 'tk(q) as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa21-r3-closed -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r3-closed` of redteamFA21.hand_fa21, in capmodel `reggio/REPORTFA21.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Lc
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mk(consuming self) returns Bx
		let x = Bx.create(self.n)
		destruct self
		return x
	end 'mk'
end 'Lc'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(b isolated Bx)
		modify b 'm1'
			b.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Lc.create(1)
	let k = q.mk() as isolated Bx
	try s.takeB(k) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa21-r3-temp -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r3-temp` of redteamFA21.hand_fa21, in capmodel `reggio/REPORTFA21.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Lq
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'

	function take(consuming self) returns Bx
		let x = self.b
		destruct self
		return x
	end 'take'

	function whole(consuming self) returns Ko
		let o = Ko.create(self.b, n: 1)
		destruct self
		return o
	end 'whole'

	function look() returns Bx
		return self.b
	end 'look'
end 'Lq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let k = Lq.create(1, b: Bx.create(2)).take() as isolated Bx
	try s.takeB(k) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa21-r3-recv-param-control -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `r3-recv-param-control` of redteamFA21.hand_fa21, in capmodel `reggio/REPORTFA21.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Lq
	var n as Num
	var b as Bx

	static function create(n Num, b Bx) returns Self
		return Self{n: n, b: b}
	end 'create'

	function take(consuming self) returns Bx
		let x = self.b
		destruct self
		return x
	end 'take'

	function whole(consuming self) returns Ko
		let o = Ko.create(self.b, n: 1)
		destruct self
		return o
	end 'whole'

	function look() returns Bx
		return self.b
	end 'look'
end 'Lq'

type Sbq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeB(bV isolated Bx)
		modify bV 'm1'
			bV.n = 9
		end 'm1'
	end 'takeB'
end 'Sbq'

function tk2(l Lq, s Sbq.handle)
	let k = l.take() as isolated Bx
	try s.takeB(k) otherwise ignore
end 'tk2'

function main() returns ExitCode
	let s = spawn Sbq.create(0)
	let q = Lq.create(1, b: Bx.create(2))
	tk2(q, s: s)
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:61:10: 'l.take() as isolated Bx' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-fa23-p1-nb-iso-union-in-modify -->
The model (`core-m3w-fa24`) refuses it by `C-SLOT`; the compiler refuses it with E3203. Model program `p1-nb-iso-union-in-modify` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

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

function mkUb() returns isolated Ub
	return Ub.u1(Bx.create(1))
end 'mkUb'

function main() returns ExitCode
	let k = mkUb() as isolated Ub
	modify k 'm1'
		match k 'x2'
			u0 then break
			u1 then k = Ub.u0
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3203: <fragment>:26:12: 'k' is open in 'm1' and cannot be reassigned there
```

<!-- test: model-hand-fa23-p2-standing-cond-outer-sent -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-standing-cond-outer-sent` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

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
	let bx = Bx.create(0) as isolated Bx
	modify bx 'm1'
		match (p if 1 != 0 else q) 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'm1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p2-payload-into-window -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-payload-into-window` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

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

type Bh
	var n as Num
	var p as Pb

	static function create(n Num, p Pb) returns Self
		return Self{n: n, p: p}
	end 'create'
end 'Bh'

function mkUP() returns isolated UP
	return UP.have(Pb.create(1))
end 'mkUP'

function main() returns ExitCode
	let bh = Bh.create(0, p: Pb.create(0)) as isolated Bh
	modify bh 'm1'
		match mkUP() 'x2'
			none then break
			have(t) then bh.p = t
		end 'x2'
	end 'm1'
	let _ = bh as isolated Bh
	return 0
end 'main'
```
```exitcode
0
```
