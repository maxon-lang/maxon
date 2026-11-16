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
the day the compiler stops agreeing with a verdict the model pinned. The rule set is `core-m3w-fa14`.

## Tests

<!-- test: model-finding-q-q2-ring3-the-noro-mark-is-not-part-of-sig-snap-so-a-ring-of-three-neve -->
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `Q2-ring3: the noro mark is not part of Sig.snap, so a ring of three never learns it` of redteamQ/holes.txt, in capmodel `reggio/REPORTRTQ.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `Q2-ring3-ctl-ring2: control, the ring of two (refused)` of redteamQ/holes.txt, in capmodel `reggio/REPORTRTQ.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `Q2-line-ctl: control, a line of three with no back edge (refused)` of redteamQ/holes.txt, in capmodel `reggio/REPORTRTQ.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `R6-unique-lent-builder-named: an outer unique token lent in a window to a named builder over its Bytes` of redteamR/holes6.txt, in capmodel `reggio/REPORTRTR.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `R6-unique-lent-builder-method: the same through a method of the token` of redteamR/holes6.txt, in capmodel `reggio/REPORTRTR.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `U4-unique-window-made-into-holder: a window-made token moved into a holder inside the window` of redteamS/h_uniq.txt, in capmodel `reggio/REPORTRTS.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `U5-unique-paused-into-holder: a paused unique token handed to a keeping callee in a window` of redteamS/h_uniq.txt, in capmodel `reggio/REPORTRTS.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `modify-nested-window` of redteamAD/t10.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `modify-in-if-both` of redteamAD/t10.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `recv-wrap-in-window` of redteamAD/t12.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `recv-fin-in-window` of redteamAD/t12.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `finalizer-param-in-window` of redteamAD/t12.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `lit-in-window` of redteamAD/t13.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `iso-param-window` of redteamAD/t13.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `plain-param-window-local` of redteamAD/t13.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `plain-param-window-fresh` of redteamAD/t13.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `h1-min` of redteamAD/t4.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `h1-plain-var` of redteamAD/t4.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `h1-bind-first` of redteamAD/t4.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `h1-direct-fin` of redteamAD/t4.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `h1-box` of redteamAD/t4.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3207. Model program `v-state-modify-ok` of redteamAD/t5.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3204. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `v-hold-and-return-from-window` of redteamAD/t5.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
error E3204: <fragment>:46:3: 'out' holds a record from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-finding-ad-l-store-into-window-object -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3201, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `l-store-into-window-object` of redteamAD/t6.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `plain-modify-fresh-pair` of redteamAD/t7.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `tok-unique-into-window-lend` of redteamAD/t8.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `union-payload-launder` of redteamAE/p1.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

<!-- test: model-finding-ae-union-payload-launder-iso -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `union-payload-launder-iso` of redteamAE/p1.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
		let u = Sh.held(v) as isolated Sh
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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `union-payload-fn-wrap` of redteamAE/p1.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `union-payload-fn-wrap-consumer` of redteamAE/p1.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `outer-plain-union-match-in-modify` of redteamAE/p13.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `outer-plain-linear-pass-in-modify` of redteamAE/p13.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `outer-plain-linear-recv-in-modify` of redteamAE/p13.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `w-match-consume-recv-wrap` of redteamAE/p14.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `w-match-consume-param` of redteamAE/p14.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `w-match-consume-store-in-window-obj` of redteamAE/p14.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `ro-union-iso-arm-write-modify` of redteamAE/p16.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
```maxoncstderr
error E3197: <fragment>:23:12: cannot write 'n' through 'bx', which is read-only while 'x2' is open
```

<!-- test: model-finding-ae-ro-union-iso-arm-wait-modify -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `ro-union-iso-arm-wait-modify` of redteamAE/p16.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
```maxoncstderr
error E3197: <fragment>:36:12: cannot write 'n' through 'bx', which is read-only while 'x2' is open
```

<!-- test: model-finding-ae-iso-nonlinear-arm-write-own-root -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `iso-nonlinear-arm-write-own-root` of redteamAE/p16.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `w-match-consume-in-modify-else` of redteamAE/p17.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3202. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w-match-consume-return` of redteamAE/p17.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-ISOREAD`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `ro-field-iso-recv-call-writes-outer` of redteamAE/p19.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `iso-return-from-window` of redteamAE/p21.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

<!-- test: model-finding-ae-window-fresh-plain-into-outer-plain-var-use -->
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3204. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `window-fresh-plain-into-outer-plain-var-use` of redteamAE/p21.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
error E3204: <fragment>:46:3: 'out' holds a record from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-finding-ae-nested-modify-write-outer-root -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `nested-modify-write-outer-root` of redteamAE/p24.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nested-modify-read-outer-root` of redteamAE/p24.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `modify-match-field-union-write-root` of redteamAE/p24.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
```maxoncstderr
error E3197: <fragment>:31:12: cannot write 'n' through 'h', which is read-only while 'x2' is open
```

<!-- test: model-finding-ae-modify-match-field-union-write-other-field -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `modify-match-field-union-write-other-field` of redteamAE/p24.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
```maxoncstderr
error E3197: <fragment>:31:12: cannot write 'u' through 'h', which is read-only while 'x2' is open
```

<!-- test: model-hand-ab-w-modify-root-linear-write -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-modify-root-linear-write` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-SLOT`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w-modify-root-then-use` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `w-modify-return-inside` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `w-modify-return-fresh-inside` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
The model (`core-m3w-fa14`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `w-modify-outer-var-consumed-inside` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
The model (`core-m3w-fa14`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `w-modify-outer-var-consumed-twice` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `w-modify-outer-var-consumed-in-if` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3204. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w-modify-assign-outer` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
error E3204: <fragment>:33:3: 'r' holds a record from outside 'm1' and cannot be reassigned while 'm1' is open
```

<!-- test: model-hand-ab-w-modify-nested -->
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `w-modify-nested` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `w-modify-nested-consume-twice` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-modify-live-across-window` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
The model (`core-m3w-fa14`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `w-modify-moved-before-in-window` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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

<!-- test: model-hand-ab-w-modify-two-linears -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `w-modify-two-linears` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `ub-modify-target-temp-linear` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `ld-modify-lend` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-SLOT`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w-modify-consume-root-after` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-modify-bump` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-SLOT`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w-modify-pass-root-after` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `w-modify-inner-isolated` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `w-modify-inner-isolated-twice` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `w-modify-window-in-loop` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `o-modify-union` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `hole-modify-in-if` of redteamAD.hand_ad9, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `hole-receiver` of redteamAD.hand_ad9, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `method-root-window` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `method-pass-window` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `pass-result-local` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `pass-result-in-giveS` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `merge-param` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `merge-param-bound` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `merge-into-window-root` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R-LIN-COND`; the compiler refuses it with E3223. Model program `cond-in-window` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R-LIN-COND`; the compiler refuses it with E3223. Model program `cond-bound` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R-LIN-COND`; the compiler refuses it with E3223. Model program `cond-fresh-first` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3223. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `in-if-else-join` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `in-loop-window` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `local-then-modify-local` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `local-outside-then-window` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `local-outside-two` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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

<!-- test: model-hand-ae-recv-param-keeps -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `recv-param-keeps` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `recv-field-int` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `recv-two-arg-sends` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `window-local-outer-recv-result-iso` of redteamAE.hand_ae1, in capmodel `reggio/REPORTRTAE.txt`.

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

<!-- test: model-hand-af-m3-union-method-in-modify -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-SELF`; the compiler refuses it with E3224. Model program `m3-union-method-in-modify` of redteamAF.hand_af1, in capmodel `reggio/REPORTRTAF.txt`.

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

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let u = Um.held(Rl.create(1))
	modify bx 'm1'
		u.eat()
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:14:4: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
```

<!-- test: model-hand-af-m-keep-plain-fn -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-keep-plain-fn` of redteamAF.hand_af12, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-LEND`; the compiler refuses it with E3198. Model program `m-iso-fn` of redteamAF.hand_af12, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `m-store-iso-slot` of redteamAF.hand_af12, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m-keep-into-window-obj` of redteamAF.hand_af12, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `u-direct-keep` of redteamAF.hand_af12, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `u-direct-keep-into` of redteamAF.hand_af12, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `n9-modify-then-match` of redteamAF.hand_af17, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a14-modify-iso-var-then-match-inside` of redteamAF.hand_af18, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3224. Model program `w10-linear-swap-out-modify` of redteamAF.hand_af20, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3214, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w12-linear-swap-out-store-back-other-region` of redteamAF.hand_af20, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s10-binder-shadows-in-window` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s11-binder-shadows-outer-in-window-moved` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s12-binder-shadows-then-outer-used` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s16-plain-payload-binder-shadow-in-modify` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `s17-plain-payload-binder-shadow-in-modify-write-after` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `p2-plain-mixed-in-fn-in-modify` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p3-plain-mixed-fresh-in-modify` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `p5-plain-mixed-share-payload-window` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p9-plain-mixed-immutable-payload-moved-to-window` of redteamAF.hand_af3, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `p12-modify-region-into-outer` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `p13-modify-region-into-outer-var` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `p14-modify-region-into-keeping-fn` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p15-modify-region-sibling-store` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `p16-modify-region-leaves-by-return` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `p17-modify-region-leaves-by-return-nonowned` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `E3102`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `t3-twin-in-modify-write` of redteamAG.hand_ag1, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3196. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `t5-plain-var-mixed-store` of redteamAG.hand_ag1, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3196, E3201. Model program `bm-modify-keep-p` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3197, E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `bm-modify-plain` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3197, E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `bm-modify-hq` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `mi-modify-merge-keep` of redteamAG.hand_ag15, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `mi-beh-merge-keep` of redteamAG.hand_ag15, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `mp-modify-freeze` of redteamAG.hand_ag15, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `mp-modify-merge` of redteamAG.hand_ag15, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `mr-modify-freeze-part` of redteamAG.hand_ag15, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f1-keep` of redteamAG.hand_ag16, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f1-part-into-window` of redteamAG.hand_ag16, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f1-linear-wrap` of redteamAG.hand_ag16, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f1-fn-param` of redteamAG.hand_ag16, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-ONECALL`; the compiler refuses it with E3199. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f1-control-iso-payload` of redteamAG.hand_ag16, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `f1-control-plain-var` of redteamAG.hand_ag16, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-modify-store` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-modifyhw-store` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-modify-keepfn` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-modifyhw-keepfn` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-ISOREAD`; the compiler refuses it with E3196. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `y-modify-push-arr` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-modifyhw-push-arr` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-modify-none` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `y-modifyhw-none` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hq-modify-store-p` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hq-modify-store-q` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hq-modify-keepfn-p` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `t-modify-store-a` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `t-modify-store-cp` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `k3-nested-windows-both` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `k4-nested-windows-outer-store` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `beh-win-mr-modify-item-store` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fn-mr-keep` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3196. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `fn-plain-mr-keep` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fn-mr-keepfn` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `fn-plain-mr-keepfn` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3197. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `fn-plain-mr-write` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-pass-store` of redteamAG.hand_ag20, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-pick-store` of redteamAG.hand_ag20, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-pick-store-other` of redteamAG.hand_ag20, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-cond-store` of redteamAG.hand_ag20, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-useHq-new` of redteamAG.hand_ag20, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l1-wrap-in-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l2-giveS-in-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l3-mkSn-in-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l4-fin-in-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l6-wrap-fn-param-in-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l7-wrap-behaviour-param-in-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l8-wrap-in-modify-state-slot` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3199. Model program `l10-wrap-in-nested-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `l14-plain-var-union-in-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l15-wn-union-plain-linear` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l17-plain-nonlinear-new-record-pushed` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `l18-plain-nonlinear-returned-from-modify` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `l19-plain-nonlinear-into-outer-var` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `l20-plain-nonlinear-into-outer-var-then-used-after` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `l21-plain-nonlinear-into-outer-record-field` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `l22-plain-nonlinear-into-outer-record-field-via-fn` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `l24-plain-payload-frozen` of redteamAG.hand_ag8, in capmodel `reggio/REPORTRTAG.txt`.

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

<!-- test: model-hand-fa14-m-store-in-window -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `m-store-in-window` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

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

type Zm8
	var b as Bx
	var w as isolated Wo

	static function create(b isolated Bx, w isolated Wo) returns Self
		return Self{b: merge b, w: w}
	end 'create'

	export function put(bV isolated Bx)
		modify self.w 'm1'
			self.b = bV
		end 'm1'
	end 'put'
end 'Zm8'

function main() returns ExitCode
	let _ = spawn Zm8.create(Bx.create(0), w: Wo.create(Bx.create(0), item: Bx.create(1)))
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:31:4: cannot write 'b' through 'self', which is read-only while 'm1' is open
```

<!-- test: model-hand-fa14-a-rf-window -->
The model (`core-m3w-fa14`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `a-rf-window` of redteamFA14.hand_fa14d, in capmodel `reggio/REPORTFA14.txt`.

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
	let _ = b.n
end 'takeBx'

function main() returns ExitCode
	let h = Wo.create(Bx.create(0), item: Bx.create(1)) as isolated Wo
	var w = Bx.create(1)
	w = Bx.create(2)
	modify h 'm1'
		takeBx(w)
	end 'm1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:30:10: 'w' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```
