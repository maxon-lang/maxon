---
feature: model-regions
status: experimental
keywords: [model, capmodel, region, capability]
category: semantics
---

# Regions and capabilities

## Documentation

These cases are programs of the capmodel executable model, translated to Maxon by
`scripts/model-specs/generate.py` (`docs/SPECS.md`, "Model-derived specs") and pinned to the model's verdict on
records, frozen data and capability inference. A program the model accepts runs and exits 0; one it refuses fails to compile, with the diagnostic
the compiler gives. A case is written only where the compiler and the model agree, so the suite fails
the day the compiler stops agreeing with a verdict the model pinned. The rule set is `core-m3w-fa24`.

## Tests

<!-- test: model-finding-n-c1-no-throw -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `C1-no-throw` of redteamN/cand1.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Hd13
	var t as Tk13

	static function create(t Tk13) returns Self
		return Self{t: t}
	end 'create'
end 'Hd13'

type Rd13
	var bytes as Bt13
	var pos as Num

	static function create(bytes Bt13, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd13'

type Tk13 is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk13'

type Bt13
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt13'

function nowT13(tV Tk13) returns Num
	return tV.n
end 'nowT13'

function keepX13(h Hd13, tV Tk13, c Num)
	let _ = h
	let _ = tV.n
	let _ = c
end 'keepX13'

function pickT13(tV Tk13, c Num) returns Tk13
	let _ = tV.n
	let _ = c
	return Tk13.create(5)
end 'pickT13'

function f13_3(p0 Tk13, p1 Rd13, p2 Hd13, c Num) returns Rd13
	keepX13(p2, tV: pickT13(p0, c: c), c: 0)
	return p1
end 'f13_3'

function main() returns ExitCode
	let a13x0 = Tk13.create(7)
	let a13x1 = Rd13.create(Bt13.create(7), pos: 0)
	let a13x2 = Hd13.create(Tk13.create(7))
	let _ = f13_3(a13x0, p1: a13x1, p2: a13x2, c: 1)
	let _ = nowT13(a13x2.t)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-n-c1-min -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `C1-min` of redteamN/cand1.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Hd
	var t as Tk

	static function create(t Tk) returns Self
		return Self{t: t}
	end 'create'
end 'Hd'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function nowT(tV Tk) returns Num
	return tV.n
end 'nowT'

function keepX(h Hd, tV Tk)
	let _ = h
	let _ = tV.n
end 'keepX'

function pickT(tV Tk) returns Tk
	let _ = tV.n
	return Tk.create(5)
end 'pickT'

function f(p0 Tk, p2 Hd)
	keepX(p2, tV: pickT(p0))
end 'f'

function main() returns ExitCode
	let a0 = Tk.create(7)
	let a2 = Hd.create(Tk.create(7))
	f(a0, p2: a2)
	let _ = nowT(a2.t)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-n-v1-main-direct -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `V1-main-direct` of redteamN/cand1b.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Hd
	var t as Tk

	static function create(t Tk) returns Self
		return Self{t: t}
	end 'create'
end 'Hd'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function nowT(tV Tk) returns Num
	return tV.n
end 'nowT'

function keepX(h Hd, tV Tk)
	let _ = h
	let _ = tV.n
end 'keepX'

function pickT(tV Tk) returns Tk
	let _ = tV.n
	return Tk.create(5)
end 'pickT'

function main() returns ExitCode
	let a0 = Tk.create(7)
	let a2 = Hd.create(Tk.create(7))
	keepX(a2, tV: pickT(a0))
	let _ = nowT(a2.t)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-n-v2-fresh-arg -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `V2-fresh-arg` of redteamN/cand1b.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Hd
	var t as Tk

	static function create(t Tk) returns Self
		return Self{t: t}
	end 'create'
end 'Hd'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function nowT(tV Tk) returns Num
	return tV.n
end 'nowT'

function keepX(h Hd, tV Tk)
	let _ = h
	let _ = tV.n
end 'keepX'

function main() returns ExitCode
	let a2 = Hd.create(Tk.create(7))
	keepX(a2, tV: Tk.create(1))
	let _ = nowT(a2.t)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-n-v3-no-read-after -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `V3-no-read-after` of redteamN/cand1b.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Hd
	var t as Tk

	static function create(t Tk) returns Self
		return Self{t: t}
	end 'create'
end 'Hd'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function keepX(h Hd, tV Tk)
	let _ = h
	let _ = tV.n
end 'keepX'

function pickT(tV Tk) returns Tk
	let _ = tV.n
	return Tk.create(5)
end 'pickT'

function f(p0 Tk, p2 Hd)
	keepX(p2, tV: pickT(p0))
end 'f'

function main() returns ExitCode
	let a0 = Tk.create(7)
	let a2 = Hd.create(Tk.create(7))
	f(a0, p2: a2)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-n-v4-no-f -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `V4-no-f` of redteamN/cand1b.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Hd
	var t as Tk

	static function create(t Tk) returns Self
		return Self{t: t}
	end 'create'
end 'Hd'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function nowT(tV Tk) returns Num
	return tV.n
end 'nowT'

function main() returns ExitCode
	let a2 = Hd.create(Tk.create(7))
	let _ = nowT(a2.t)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-n-v5-call-only-f-no-pick -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `V5-call-only-f-no-pick` of redteamN/cand1b.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Hd
	var t as Tk

	static function create(t Tk) returns Self
		return Self{t: t}
	end 'create'
end 'Hd'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function nowT(tV Tk) returns Num
	return tV.n
end 'nowT'

function keepX(h Hd, tV Tk)
	let _ = h
	let _ = tV.n
end 'keepX'

function f(p0 Tk, p2 Hd)
	keepX(p2, tV: p0)
end 'f'

function main() returns ExitCode
	let a0 = Tk.create(7)
	let a2 = Hd.create(Tk.create(7))
	f(a0, p2: a2)
	let _ = nowT(a2.t)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-n-v6-tk-only -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `V6-tk-only` of redteamN/cand1b.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function keepX(t Tk)
	let _ = t.n
end 'keepX'

function pickT(t Tk) returns Tk
	let _ = t.n
	return Tk.create(5)
end 'pickT'

function main() returns ExitCode
	let a0 = Tk.create(7)
	keepX(pickT(a0))
	let _ = 1 as Num
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-n-v7-use-a0-after -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `V7-use-a0-after` of redteamN/cand1b.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Hd
	var t as Tk

	static function create(t Tk) returns Self
		return Self{t: t}
	end 'create'
end 'Hd'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function nowT(tV Tk) returns Num
	return tV.n
end 'nowT'

function keepX(h Hd, tV Tk)
	let _ = h
	let _ = tV.n
end 'keepX'

function pickT(tV Tk) returns Tk
	let _ = tV.n
	return Tk.create(5)
end 'pickT'

function f(p0 Tk, p2 Hd)
	keepX(p2, tV: pickT(p0))
end 'f'

function main() returns ExitCode
	let a0 = Tk.create(7)
	let a2 = Hd.create(Tk.create(7))
	f(a0, p2: a2)
	let _ = nowT(a0)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-r-r6-unique-in-array-for-window-a-for-over-unique-elements-is-a-window-t -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `R6-unique-in-array-for-window: a for over unique elements is a window; the builder is handed an outer plain record` of redteamR/holes6.txt, in capmodel `reggio/REPORTRTR.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias Tk2Array = Array with Tk2

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

function mkr(bV Bytes) returns Num
	let _ = Reader.create(bV, pos: 0)
	return 0
end 'mkr'

function main() returns ExitCode
	let _ = Bytes.create(7)
	let ta = Tk2Array.create(0)
	ta.push(Tk2.create(Bytes.create(1), n: 0))
	for t in ta 'f1'
		let _ = mkr(t.bytes)
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-s-k2-cond-of-bytes-two-holders-the-same-with-a-plain-cond-of-two-mixed-v -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3197. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `K2-cond-of-bytes-two-holders: the same with a plain cond of two mixed values` of redteamS/h_ks.txt, in capmodel `reggio/REPORTRTS.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bytes
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bytes'

type Two
	var x as Bytes
	var y as Bytes

	static function create(x Bytes, y Bytes) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Two'

function f(a Bytes, bV Bytes, c Bytes, j Num)
	let h1 = Two.create(a, y: c)
	let h2 = Two.create(a, y: bV)
	let v = (h1.x if j != 0 else h2.x)
	v.b = 9
end 'f'

function main() returns ExitCode
	let fb = freeze (Bytes.create(1))
	let ma = Bytes.create(2)
	let mc = Bytes.create(3)
	f(ma, bV: fb, c: mc, j: 0)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:25:2: cannot write 'b' through 'v', which is read-only, as it joins immutable and mutable data
```

<!-- test: model-finding-s-u1-unique-into-holder-then-reuse-a-callee-moves-its-unique-parameter-i -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `U1-unique-into-holder-then-reuse: a callee moves its unique parameter into a holder; the caller uses the token after` of redteamS/h_uniq.txt, in capmodel `reggio/REPORTRTS.txt`.

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
	let _ = view(tV)
	let _ = tV.n
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:10: use of moved value 'tV': its ownership moved to the parameter 'tV' of 'view', which keeps it
```

<!-- test: model-finding-s-u2-unique-lent-read-a-callee-only-reads-its-unique-parameter-the-calle -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `U2-unique-lent-read: a callee only reads its unique parameter; the caller reuses it` of redteamS/h_uniq.txt, in capmodel `reggio/REPORTRTS.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function look(t Tk) returns Num
	return t.n
end 'look'

function main() returns ExitCode
	let t = Tk.create(1)
	let _ = look(t)
	let _ = t.n
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-s-u3-unique-in-holder-two-calls-two-calls-each-moving-the-token-into-a-h -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `U3-unique-in-holder-two-calls: two calls, each moving the token into a holder` of redteamS/h_uniq.txt, in capmodel `reggio/REPORTRTS.txt`.

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
	let _ = view(tV)
	let _ = view(tV)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:15: use of moved value 'tV': its ownership moved to the parameter 'tV' of 'view', which keeps it
```

<!-- test: model-finding-s-u7-unique-returned-through-holder-the-holder-is-returned-and-the-token -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `U7-unique-returned-through-holder: the holder is returned and the token read out by the caller twice` of redteamS/h_uniq.txt, in capmodel `reggio/REPORTRTS.txt`.

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

function wrap(tV Tk) returns Th
	return Th.create(tV, pos: 0)
end 'wrap'

function main() returns ExitCode
	let tV = Tk.create(1)
	let h = wrap(tV)
	let _ = h.t.n
	let _ = h.t.n
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-y-y2-ctl-plain-same -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `Y2-ctl-plain-same` of redteamY/min6.txt, in capmodel `reggio/REPORTRTY.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Plain
	var h as Bt

	static function create(h Bt) returns Self
		return Self{h: h}
	end 'create'
end 'Plain'

function leak(p Plain) returns Bt
	return p.h
end 'leak'

function main() returns ExitCode
	let x = Plain.create(Bt.create(1))
	let a = leak(x)
	a.b = 5
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-aa-polyrec-record-in-state -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `polyrec-record-in-state` of redteamAA/p4.txt, in capmodel `reggio/REPORTRTAA.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

function main() returns ExitCode
	let _ = 1 as Num
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-report-ab-inline-1 -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `inline-1` of reggio/REPORTRTAB.txt, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ad-f -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f` of redteamAD/t1.txt, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

function keepLocal(t Tok)
	let _ = t
end 'keepLocal'

function main() returns ExitCode
	let t = Tok.create(0) as isolated Tok
	keepLocal(t)
	keepLocal(t)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ad-swap-field-self -->
The model (`core-m3w-fa24`) refuses it by `C-ISOREAD`; the compiler refuses it with E3198. Model program `swap-field-self` of redteamAD/t3.txt, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Hd
	var b as isolated Box

	static function create(b isolated Box) returns Self
		return Self{b: b}
	end 'create'
end 'Hd'

function main() returns ExitCode
	let h = Hd.create(Box.create(0))
	let _ = swap h.b with h.b
	let _ = swap h.b with Box.create(1)
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:22:24: 'h.b' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-finding-ae-frozen-field-iso-recv-call-writes-outer -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `frozen-field-iso-recv-call-writes-outer` of redteamAE/p19.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
	let h = Hd.create(Bx.create(1)) as isolated Hd
	let fh = freeze h
	let c = Cnt.create(0)
	fh.item.touch(c)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ae-mutable-field-iso-recv-call-writes-outer-ctl -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `mutable-field-iso-recv-call-writes-outer-ctl` of redteamAE/p19.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
	let h = Hd.create(Bx.create(1))
	let c = Cnt.create(5)
	h.item.touch(c)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:35:15: 'c' is mutable outside 'item'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ab-s-freeze-box-with-nothing -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-freeze-box-with-nothing` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let b = Box.create(0) as isolated Box
	let _ = freeze b
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-fz-clone-linear-field-of-nonlinear -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fz-clone-linear-field-of-nonlinear` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let b = Box.create(0)
	let _ = b.clone()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-ub-iter-over-array-of-lin -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ub-iter-over-array-of-lin` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BoxArray = Array with Box

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = BoxArray.create(0)
	for _ in a 'f1'
		let _ = 0 as Num
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-cond-fresh-arm-effect -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `cond-fresh-arm-effect` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

function side() returns Num
	return 1
end 'side'

function main() returns ExitCode
	let _ = (side() if 1 != 0 else side())
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-k-local-isolated-once -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-local-isolated-once` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

function keepLocal(t Tok)
	let _ = t
end 'keepLocal'

function main() returns ExitCode
	let t = Tok.create(0) as isolated Tok
	keepLocal(t)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-k-local-isolated-then-read -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-local-isolated-then-read` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

function rd(t Tok) returns Num
	return t.n
end 'rd'

function keepLocal(t Tok)
	let _ = t
end 'keepLocal'

function main() returns ExitCode
	let t = Tok.create(0) as isolated Tok
	keepLocal(t)
	let _ = rd(t)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-k-var-isolated-twice -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-var-isolated-twice` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

function keepVar(t Tok)
	let _ = t
end 'keepVar'

function main() returns ExitCode
	let t = Tok.create(0) as isolated Tok
	keepVar(t)
	keepVar(t)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-k-plain-twice -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-plain-twice` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

function keepLocal(t Tok)
	let _ = t
end 'keepLocal'

function main() returns ExitCode
	let t = Tok.create(0)
	keepLocal(t)
	keepLocal(t)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-k-arr -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `k-arr` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TokArray = Array with Tok

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

function keepArr(t Tok)
	let xs = TokArray.create(0)
	xs.push(t)
end 'keepArr'

function main() returns ExitCode
	let t = Tok.create(0) as isolated Tok
	keepArr(t)
	keepArr(t)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:21:10: use of moved value 't': its ownership moved to the parameter 't' of 'keepArr', which keeps it
```

<!-- test: model-hand-ad-k-cell -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `k-cell` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

type Cell
	var t as Tok

	static function create(t Tok) returns Self
		return Self{t: t}
	end 'create'
end 'Cell'

function keepCell(tV Tok)
	let _ = Cell.create(tV)
end 'keepCell'

function main() returns ExitCode
	let tV = Tok.create(0) as isolated Tok
	keepCell(tV)
	keepCell(tV)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:11: use of moved value 'tV': its ownership moved to the parameter 'tV' of 'keepCell', which keeps it
```

<!-- test: model-hand-ad-k-id -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-id` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

function idt(t Tok) returns Tok
	return t
end 'idt'

function main() returns ExitCode
	let t = Tok.create(0) as isolated Tok
	let _ = idt(t)
	let _ = idt(t)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:18:10: 'idt' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: model-hand-ad-k-id-then-use -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-id-then-use` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

function rd(t Tok) returns Num
	return t.n
end 'rd'

function idt(t Tok) returns Tok
	return t
end 'idt'

function main() returns ExitCode
	let t = Tok.create(0) as isolated Tok
	let _ = idt(t)
	let _ = rd(t)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:22:10: 'idt' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: model-hand-ad-k-put -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-put` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

type Cell2
	var n as Num
	var t as Tok

	static function create(n Num, t Tok) returns Self
		return Self{n: n, t: t}
	end 'create'
end 'Cell2'

function put(c Cell2, tV Tok)
	c.t = tV
end 'put'

function main() returns ExitCode
	let c = Cell2.create(0, t: Tok.create(0))
	let tV = Tok.create(1) as isolated Tok
	put(c, tV: tV)
	put(c, tV: tV)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:28:6: 'c' is mutable outside 'tV'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ad-k-put-iso-cell -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-put-iso-cell` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

type Cell2
	var n as Num
	var t as Tok

	static function create(n Num, t Tok) returns Self
		return Self{n: n, t: t}
	end 'create'
end 'Cell2'

function put(c Cell2, tV Tok)
	c.t = tV
end 'put'

function main() returns ExitCode
	let c = Cell2.create(0, t: Tok.create(0)) as isolated Cell2
	let tV = Tok.create(1) as isolated Tok
	put(c, tV: tV)
	put(c, tV: tV)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:28:13: one statement reaches through two isolated values, 'c' and 'tV'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-hand-ad-k-put-iso-cell-once -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-put-iso-cell-once` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

type Cell2
	var n as Num
	var t as Tok

	static function create(n Num, t Tok) returns Self
		return Self{n: n, t: t}
	end 'create'
end 'Cell2'

function rd(tV Tok) returns Num
	return tV.n
end 'rd'

function put(c Cell2, tV Tok)
	c.t = tV
end 'put'

function main() returns ExitCode
	let c = Cell2.create(0, t: Tok.create(0)) as isolated Cell2
	let tV = Tok.create(1) as isolated Tok
	put(c, tV: tV)
	let _ = rd(tV)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:32:13: one statement reaches through two isolated values, 'c' and 'tV'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-hand-ad-k-new-cell -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3201. Model program `k-new-cell` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

type Cell
	var t as Tok

	static function create(t Tok) returns Self
		return Self{t: t}
	end 'create'
end 'Cell'

function rd(tV Tok) returns Num
	return tV.n
end 'rd'

function main() returns ExitCode
	let tV = Tok.create(1) as isolated Tok
	let _ = Cell.create(tV)
	let _ = rd(tV)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:26:15: 'create' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: model-hand-ad-k-new-cell-twice -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3201. Model program `k-new-cell-twice` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

type Cell
	var t as Tok

	static function create(t Tok) returns Self
		return Self{t: t}
	end 'create'
end 'Cell'

function main() returns ExitCode
	let tV = Tok.create(1) as isolated Tok
	let _ = Cell.create(tV)
	let _ = Cell.create(tV)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:22:15: 'create' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: model-hand-ad-k-array-push -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `k-array-push` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TokArray = Array with Tok

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

function rd(t Tok) returns Num
	return t.n
end 'rd'

function main() returns ExitCode
	let t = Tok.create(1) as isolated Tok
	let xs = TokArray.create(0)
	xs.push(t)
	let _ = rd(t)
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:20:10: cannot store an isolated 'Tok' into 'an element of the container push writes', which holds a mutable 'Tok'; `merge` it first
```

<!-- test: model-hand-ad-k-method-keep -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `k-method-keep` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

type Ek
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(t Tok)
		let _ = t
	end 'eat'
end 'Ek'

function main() returns ExitCode
	let e = Ek.create(0)
	let t = Tok.create(1) as isolated Tok
	e.eat(t)
	e.eat(t)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:27:2: 'e' is mutable outside 't'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ad-k-method-keep-iso-recv -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `k-method-keep-iso-recv` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

type Ek
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(t Tok)
		let _ = t
	end 'eat'
end 'Ek'

function main() returns ExitCode
	let e = Ek.create(0) as isolated Ek
	let t = Tok.create(1) as isolated Tok
	e.eat(t)
	e.eat(t)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:27:8: one statement reaches through two isolated values, 'e' and 't'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-hand-ad-k-cond -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-cond` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

function lg(x Num) returns Num
	return x
end 'lg'

function rd(t Tok) returns Num
	return t.n
end 'rd'

function keepLocal(t Tok)
	let _ = t
end 'keepLocal'

function main() returns ExitCode
	let t = Tok.create(1) as isolated Tok
	let u = Tok.create(2) as isolated Tok
	keepLocal((t if lg(1) != 0 else u))
	let _ = rd(t)
	let _ = rd(u)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:13: use of moved value 't': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-k-keeper-in-loop -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-keeper-in-loop` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

function lg(x Num) returns Num
	return x
end 'lg'

function keepLocal(t Tok)
	let _ = t
end 'keepLocal'

function main() returns ExitCode
	let t = Tok.create(1) as isolated Tok
	var i = 2 as Num
	while i != 0 'w1'
		keepLocal(t)
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-k-keeper-in-for -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-keeper-in-for` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

function keepLocal(t Tok)
	let _ = t
end 'keepLocal'

function main() returns ExitCode
	let t = Tok.create(1) as isolated Tok
	let xs = NumArray.create(0)
	xs.push(1)
	xs.push(2)
	for _ in xs 'f1'
		keepLocal(t)
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-k-param-keeper-twice -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-param-keeper-twice` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

function keepLocal(t Tok)
	let _ = t
end 'keepLocal'

function two(t isolated Tok)
	keepLocal(t)
	keepLocal(t)
end 'two'

function main() returns ExitCode
	two(Tok.create(1))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-af-sh7-inner-let-iso-bx -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `sh7-inner-let-iso-bx` of redteamAF.hand_af16, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
	let q = Bx.create(9) as isolated Bx
	if lg(1) != 0 'i1'
		let q = Bx.create(3) as isolated Bx
		takeB(q)
	end 'i1'
	takeB(q)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-m17-for-unique-array-copy -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m17-for-unique-array-copy` of redteamAG.hand_ag10, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let xs = TkArray.create(0)
	xs.push(Tk.create(1))
	for t in xs 'f1'
		let _ = xs
		let _ = t.n
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:17:11: use of moved value 'xs': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ag-m18-for-unique-array-bare-read-call -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m18-for-unique-array-bare-read-call` of redteamAG.hand_ag10, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function len(xs TkArray) returns Num
	let _ = xs
	return 1
end 'len'

function main() returns ExitCode
	let xs = TkArray.create(0)
	xs.push(Tk.create(1))
	for t in xs 'f1'
		let _ = len(xs)
		let _ = t.n
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-u18-nested-for-unique-array -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u18-nested-for-unique-array` of redteamAG.hand_ag14, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let xs = TkArray.create(0)
	xs.push(Tk.create(1))
	for _ in xs 'f1'
		for t2 in xs 'f2'
			let _ = t2.n
		end 'f2'
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-u19-nested-for-unique-array-write -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `u19-nested-for-unique-array-write` of redteamAG.hand_ag14, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let xs = TkArray.create(0)
	xs.push(Tk.create(1))
	for _ in xs 'f1'
		for t2 in xs 'f2'
			t2.n = 3
		end 'f2'
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:18:4: cannot write 'n' through 't2', which is read-only while 'f2' drains it
```

<!-- test: model-hand-ah-i3-fresh-into-param -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `i3-fresh-into-param` of redteamAH.hand_ah5, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeBx(b isolated Bx)
	let _ = b.n
end 'takeBx'

function main() returns ExitCode
	let w = Bx.create(1)
	takeBx(w)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-i3-fresh-twice-in-one-call -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `i3-fresh-twice-in-one-call` of redteamAH.hand_ah5, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function two(a isolated Bx, b Bx)
	let _ = a
	let _ = b.n
end 'two'

function main() returns ExitCode
	let w = Bx.create(1)
	two(w, b: w)
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:19:6: 'w' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-ah-i3-fresh-in-loop-new-each -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `i3-fresh-in-loop-new-each` of redteamAH.hand_ah5, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function lg(x Num) returns Num
	return x
end 'lg'

function takeBx(b isolated Bx)
	let _ = b.n
end 'takeBx'

function main() returns ExitCode
	var i = 2 as Num
	var j = 1 as Num
	while i != 0 'w1'
		let w = Bx.create(1)
		takeBx(w)
		i = j
		j = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-i3-fresh-then-reassigned-after-recovery -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `i3-fresh-then-reassigned-after-recovery` of redteamAH.hand_ah5, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeBx(b isolated Bx)
	let _ = b.n
end 'takeBx'

function main() returns ExitCode
	var w = Bx.create(1)
	takeBx(w)
	w = Bx.create(2)
	takeBx(w)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-i3-fresh-clone-before -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `i3-fresh-clone-before` of redteamAH.hand_ah5, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeBx(b isolated Bx)
	let _ = b.n
end 'takeBx'

function main() returns ExitCode
	let w = Bx.create(1)
	let c = w.clone()
	c.n = 3
	takeBx(w)
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:20:9: 'w' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-ah-b-array-push-then-take -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `b-array-push-then-take` of redteamAH.hand_ah8, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeArr(a isolated BxArray)
	let _ = a
	let _ = 1 as Num
end 'takeArr'

function main() returns ExitCode
	let a = BxArray.create(0)
	a.push(Bx.create(1))
	takeArr(a)
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:21:10: 'a' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-ah-b-array-loop-push -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `b-array-loop-push` of redteamAH.hand_ah8, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function lg(x Num) returns Num
	return x
end 'lg'

function takeArr(a isolated BxArray)
	let _ = a
	let _ = 1 as Num
end 'takeArr'

function main() returns ExitCode
	let a = BxArray.create(0)
	var i = 2 as Num
	var j = 1 as Num
	while i != 0 'w1'
		a.push(Bx.create(i))
		i = j
		j = lg(0)
	end 'w1'
	takeArr(a)
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:31:10: 'a' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-ah-b-array-for-write -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `b-array-for-write` of redteamAH.hand_ah8, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeArr(a isolated BxArray)
	let _ = a
	let _ = 1 as Num
end 'takeArr'

function main() returns ExitCode
	let a = BxArray.create(0)
	a.push(Bx.create(1))
	for e in a 'f1'
		e.n = 4
	end 'f1'
	takeArr(a)
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:24:10: 'a' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-ah-b-record-push-into-field-array -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `b-record-push-into-field-array` of redteamAH.hand_ah8, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Bag
	var xs as BxArray
	var n as Num

	static function create(xs BxArray, n Num) returns Self
		return Self{xs: xs, n: n}
	end 'create'
end 'Bag'

function takeBag(b isolated Bag)
	let _ = b.n
end 'takeBag'

function main() returns ExitCode
	let b = Bag.create(BxArray.create(0), n: 0)
	b.xs.push(Bx.create(1))
	takeBag(b)
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:29:10: 'b' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-ah-b-fresh-var-loop-reset -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `b-fresh-var-loop-reset` of redteamAH.hand_ah8, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function lg(x Num) returns Num
	return x
end 'lg'

function takeBx(b isolated Bx)
	let _ = b.n
end 'takeBx'

function main() returns ExitCode
	var w = Bx.create(1)
	var i = 2 as Num
	var j = 1 as Num
	while i != 0 'w1'
		takeBx(w)
		w = Bx.create(2)
		i = j
		j = lg(0)
	end 'w1'
	takeBx(w)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-b-two-recoveries-one-statement -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `b-two-recoveries-one-statement` of redteamAH.hand_ah8, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeBx(b isolated Bx)
	let _ = b.n
end 'takeBx'

function main() returns ExitCode
	let a = Bx.create(1)
	let b = Bx.create(2)
	takeBx(a)
	takeBx(b)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-b-fresh-in-both-branches -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `b-fresh-in-both-branches` of redteamAH.hand_ah8, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function lg(x Num) returns Num
	return x
end 'lg'

function takeBx(b isolated Bx)
	let _ = b.n
end 'takeBx'

function main() returns ExitCode
	let a = Bx.create(1)
	if lg(1) != 0 'i1'
		takeBx(a)
	end 'i1' else 'e2'
		takeBx(a)
	end 'e2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-b-fresh-recover-in-one-branch -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `b-fresh-recover-in-one-branch` of redteamAH.hand_ah8, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function lg(x Num) returns Num
	return x
end 'lg'

function takeBx(b isolated Bx)
	let _ = b.n
end 'takeBx'

function main() returns ExitCode
	let a = Bx.create(1)
	if lg(1) != 0 'i1'
		takeBx(a)
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-h2-no-try -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `h2-no-try` of redteamAI.hand_ai7, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx10
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx10'

function lg10(x Num) returns Num
	return x
end 'lg10'

function takeB10(b isolated Bx10)
	let _ = b.n
end 'takeB10'

function soup()
	var w = Bx10.create(1)
	let a = w as isolated Bx10
	takeB10(a)
	w = Bx10.create(lg10(2))
	let _ = w.n
end 'soup'

function main() returns ExitCode
	soup()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-aj-m-store-self -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-store-self` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tm is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function put(h Km)
		h.k = self
	end 'put'
end 'Tm'

type Km
	var k as Tm

	static function create(k Tm) returns Self
		return Self{k: k}
	end 'create'
end 'Km'

function f(t Tm, h Km)
	let u = t
	u.put(h)
end 'f'

function main() returns ExitCode
	let h = Km.create(Tm.create(0))
	let t = Tm.create(1)
	f(t, h: h)
	let _ = t.n
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:33:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-m-returns-self -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-returns-self` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tm is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function me() returns Tm
		return self
	end 'me'
end 'Tm'

type Km
	var k as Tm

	static function create(k Tm) returns Self
		return Self{k: k}
	end 'create'
end 'Km'

function f(t Tm, h Km)
	let u = t
	let w = u.me()
	h.k = w
end 'f'

function main() returns ExitCode
	let h = Km.create(Tm.create(0))
	let t = Tm.create(1)
	f(t, h: h)
	let _ = t.n
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:34:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-m-returns-self-direct -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-returns-self-direct` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tm is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function me() returns Tm
		return self
	end 'me'
end 'Tm'

type Km
	var k as Tm

	static function create(k Tm) returns Self
		return Self{k: k}
	end 'create'
end 'Km'

function f(t Tm, h Km)
	let w = t.me()
	h.k = w
end 'f'

function main() returns ExitCode
	let h = Km.create(Tm.create(0))
	let t = Tm.create(1)
	f(t, h: h)
	let _ = t.n
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:33:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-m-bump -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-bump` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tm is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function bump()
		self.n = 2
	end 'bump'
end 'Tm'

type Km
	var k as Tm

	static function create(k Tm) returns Self
		return Self{k: k}
	end 'create'
end 'Km'

function f(t Tm, h Km)
	let _ = h
	let u = t
	u.bump()
end 'f'

function main() returns ExitCode
	let h = Km.create(Tm.create(0))
	let t = Tm.create(1)
	f(t, h: h)
	let _ = t.n
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-aj-id-call -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `id-call` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function idTk(x Tk) returns Tk
	return x
end 'idTk'

function f(t Tk, h Kp)
	let u = t
	let w = idTk(u)
	h.k = w
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
error E3102: <fragment>:34:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-id-call-rename -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `id-call-rename` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function idTk(x Tk) returns Tk
	return x
end 'idTk'

function f(t Tk, h Kp)
	let w = idTk(t)
	let u = w
	h.k = u
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
error E3102: <fragment>:34:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-pass2-store -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `pass2-store` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function pass2(x Tk, h Kp) returns Tk
	let _ = h
	let y = x
	return y
end 'pass2'

function f(t Tk, h Kp)
	let u = t
	h.k = pass2(u, h: h)
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

<!-- test: model-hand-aj-keep2 -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `keep2` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function keepTk(x Tk, h Kp)
	h.k = x
end 'keepTk'

function keepTk2(x Tk, h Kp)
	let y = x
	keepTk(y, h: h)
end 'keepTk2'

function f(t Tk, h Kp)
	let u = t
	keepTk2(u, h: h)
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
error E3102: <fragment>:38:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-push -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `push` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function f(t Tk, a TkArray)
	let u = t
	a.push(u)
end 'f'

function main() returns ExitCode
	let a = TkArray.create(0)
	let t = Tk.create(1)
	f(t, a: a)
	let _ = t.n
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:22:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-record-literal-field -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `record-literal-field` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Tw2 is unique
	var n as Num
	var t as Tk

	static function create(n Num, t Tk) returns Self
		return Self{n: n, t: t}
	end 'create'
end 'Tw2'

type Kw2
	var w as Tw2

	static function create(w Tw2) returns Self
		return Self{w: w}
	end 'create'
end 'Kw2'

function f(tV Tk, h Kw2)
	let u = tV
	h.w = Tw2.create(1, t: u)
end 'f'

function main() returns ExitCode
	let h = Kw2.create(Tw2.create(0, t: Tk.create(0)))
	let tV = Tk.create(1)
	f(tV, h: h)
	let _ = tV.n
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:38:10: use of moved value 'tV': its ownership moved to the parameter 'tV' of 'f', which keeps it
```

<!-- test: model-hand-aj-cond-store -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `cond-store` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function lg(x Num) returns Num
	return x
end 'lg'

function f(t Tk, h Kp)
	let u = t
	let w = Tk.create(0)
	h.k = (u if lg(1) != 0 else w)
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
error E3102: <fragment>:34:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-cond-rename-store -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `cond-rename-store` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function lg(x Num) returns Num
	return x
end 'lg'

function f2(t Tk, t2 Tk, h Kp)
	let u = (t if lg(1) != 0 else t2)
	h.k = u
end 'f2'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let t = Tk.create(1)
	f2(t, t2: Tk.create(2), h: h)
	let _ = t.n
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:33:10: use of moved value 't': its ownership moved to the parameter 't' of 'f2', which keeps it
```

<!-- test: model-hand-aj-loop-store-then-rename -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `loop-store-then-rename` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
	var u = Tk.create(0)
	var go = 1 as Num
	var once = 1 as Num
	while go != 0 'w1'
		h.k = u
		if once != 0 'i2'
			once = 0
			u = t
		end 'i2' else 'e3'
			u = Tk.create(5)
			go = 0
		end 'e3'
	end 'w1'
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
error E3102: <fragment>:28:8: use of moved value 't': it was moved in an earlier iteration of this loop
error E3102: <fragment>:40:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-loop-break -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `loop-break` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function lg(x Num) returns Num
	return x
end 'lg'

function f(t Tk, h Kp)
	var u = Tk.create(0)
	var i = 0 as Num
	while lg(1) != 0 'w1'
		if i != 0 'i2'
			h.k = u
			break
		end 'i2'
		u = t
		i = 1
	end 'w1'
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
error E3102: <fragment>:32:7: use of moved value 't': it was moved in an earlier iteration of this loop
error E3102: <fragment>:41:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-loop-for -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `loop-for` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

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

function f(t Tk, h Kp, xs NumArray)
	var u = Tk.create(0)
	for _ in xs 'f1'
		h.k = u
		u = t
	end 'f1'
end 'f'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let t = Tk.create(1)
	let xs = NumArray.create(0)
	xs.push(1)
	xs.push(2)
	f(t, h: h, xs: xs)
	let _ = t.n
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:25:7: use of moved value 't': it was moved in an earlier iteration of this loop
error E3102: <fragment>:36:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-recursion -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `recursion` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function lg(x Num) returns Num
	return x
end 'lg'

function r1(t Tk, h Kp, nV Num)
	let u = t
	if nV != 0 'i1'
		r1(u, h: h, nV: lg(0))
	end 'i1' else 'e2'
		h.k = u
	end 'e2'
end 'r1'

function f(t Tk, h Kp)
	r1(t, h: h, nV: 2)
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

<!-- test: model-hand-aj-mutual -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `mutual` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function lg(x Num) returns Num
	return x
end 'lg'

function ra(t Tk, h Kp, nV Num)
	let u = t
	if nV != 0 'i1'
		rb(u, h: h, nV: lg(0))
	end 'i1' else 'e2'
		rb(u, h: h, nV: lg(0))
	end 'e2'
end 'ra'

function rb(t Tk, h Kp, nV Num)
	let w = t
	if nV != 0 'i1'
		ra(w, h: h, nV: lg(0))
	end 'i1' else 'e2'
		h.k = w
	end 'e2'
end 'rb'

function f(t Tk, h Kp)
	ra(t, h: h, nV: 3)
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
error E3102: <fragment>:50:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-fits-local-two-vars -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `fits-local-two-vars` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
	var a = Tk.create(0)
	var b = Tk.create(0)
	a = t
	b = a
	h.k = b
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
error E3102: <fragment>:32:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-merge-renamed -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `merge-renamed` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
	let u = t
	let w = merge u
	h.k = w
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
error E3200: <fragment>:22:10: 'u' is not an isolated value; `merge` moves an isolated region into the current one
```

<!-- test: model-hand-aj-iso-renamed -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `iso-renamed` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function takeT(t isolated Tk)
	let _ = t.n
end 'takeT'

function f(t Tk, h Kp)
	let _ = h
	let u = t
	let w = u as isolated Tk
	takeT(w)
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
error E3200: <fragment>:27:10: 'u as isolated Tk' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-aj-lend-then-keep-alias -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `lend-then-keep-alias` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function idTk(x Tk) returns Tk
	return x
end 'idTk'

function f(t Tk, h Kp)
	let u = t
	h.k = idTk(u)
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
error E3102: <fragment>:33:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-ovo-loop-window -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ovo-loop-window` of redteamAJ.hand_aj5, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function sc(xs NumArray)
	var b = Bx.create(1)
	for x in xs 'f1'
		b = Bx.create(x)
	end 'f1'
	b.n = 2
end 'sc'

function main() returns ExitCode
	let xs = NumArray.create(0)
	xs.push(1)
	sc(xs)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-al-k-value-cond -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-value-cond` of redteamAL/hand_al2.txt, in capmodel `reggio/REPORTRTAL.txt`.

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

function n(t Tq) returns Num
	return t.b.n
end 'n'

function main() returns ExitCode
	let q = Tq.create(1, b: Bx.create(2))
	let z = n((q if 1 != 0 else q))
	q.b.n = z
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-al-ca-let-cond-alias -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ca-let-cond-alias` of redteamAL/hand_al3.txt, in capmodel `reggio/REPORTRTAL.txt`.

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

function main() returns ExitCode
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = (q if 1 != 0 else q)
	q2.n = 5
	q.n = 6
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:25:2: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-al-ca-push-cond -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `ca-push-cond` of redteamAL/hand_al3.txt, in capmodel `reggio/REPORTRTAL.txt`.

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

function main() returns ExitCode
	let q = Tq.create(1, b: Bx.create(2))
	let xs = TqArray.create(0)
	xs.push((q if 1 != 0 else q))
	q.n = 6
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:26:2: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-al-ca-lend-cond-write-other -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ca-lend-cond-write-other` of redteamAL/hand_al3.txt, in capmodel `reggio/REPORTRTAL.txt`.

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

function rdQ(t Tq) returns Num
	return t.n
end 'rdQ'

function wrT(t Tq)
	t.n = 7
end 'wrT'

function main() returns ExitCode
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let _ = rdQ((q if 1 != 0 else q2))
	wrT(q)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-al-nl-for-for-same -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `nl-for-for-same` of redteamAL/hand_al3.txt, in capmodel `reggio/REPORTRTAL.txt`.

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

function rdQ(t Tq) returns Num
	return t.n
end 'rdQ'

function main() returns ExitCode
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	for a in xs 'f1'
		for bV in xs 'f2'
			a.n = rdQ(bV)
			bV.n = 2
		end 'f2'
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:32:4: cannot write 'n' through 'bV', which is read-only while 'f2' drains it
```

<!-- test: model-hand-al-nl-for-for-read -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nl-for-for-read` of redteamAL/hand_al3.txt, in capmodel `reggio/REPORTRTAL.txt`.

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

function rdQ(t Tq) returns Num
	return t.n
end 'rdQ'

function main() returns ExitCode
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	for a in xs 'f1'
		for bV in xs 'f2'
			a.n = rdQ(bV)
		end 'f2'
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-al-nl-for-for-inner-lent-writer -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `nl-for-for-inner-lent-writer` of redteamAL/hand_al3.txt, in capmodel `reggio/REPORTRTAL.txt`.

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

function wrT(t Tq)
	t.n = 7
end 'wrT'

function main() returns ExitCode
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	for a in xs 'f1'
		for bV in xs 'f2'
			wrT(bV)
		end 'f2'
		a.n = 3
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:31:8: 'wrT' needs a writable parameter 't' at line 23; 'bV' is read-only while 'f2' is open
```

<!-- test: model-hand-am-kt-b-param-let -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `kt-b-param-let` of redteamAM/hand_am1.txt, in capmodel `reggio/REPORTRTAM.txt`.

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

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function wrapP(bV Bx) returns Wq
	let k = keepW(Tq.create(1, b: bV)) as isolated Wq
	return k
end 'wrapP'

function main() returns ExitCode
	let bV = Bx.create(2)
	let _ = wrapP(bV)
	bV.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:51:10: 'keepW(Tq.create(1, b: bV)) as isolated Wq' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: model-hand-am-cl-array-closed -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `cl-array-closed` of redteamAM/hand_am3.txt, in capmodel `reggio/REPORTRTAM.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TcArray = Array with Tc

type Tc is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tc'

function pa(xs TcArray) returns Num
	let _ = xs
	return 1
end 'pa'

function main() returns ExitCode
	let xs = TcArray.create(0)
	xs.push(Tc.create(1))
	let _ = pa(xs)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ao-f2-onecall-open-cond-path -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-onecall-open-cond-path` of redteamAO/hand_ao1.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hi is unique
	var b as isolated Bx

	static function create(b isolated Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Hi'

function wr(x Bx)
	x.n = 5
end 'wr'

function main() returns ExitCode
	let h = Hi.create(Bx.create(1))
	let g = Hi.create(Bx.create(2))
	wr((h if 1 != 0 else g).b)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ao-o1-onecall-open-cond-field-plus-holder -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `o1-onecall-open-cond-field-plus-holder` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var t as Tk

	static function create(t Tk) returns Self
		return Self{t: t}
	end 'create'
end 'Ht'

function two(a Tk, b Ht)
	b.t = Tk.create(9)
	a.n = 3
end 'two'

function main() returns ExitCode
	let h = Ht.create(Tk.create(1))
	let g = Ht.create(Tk.create(2))
	two((h if 1 != 0 else g).t, b: h)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:33: 'h' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: model-hand-ao-o1c-onecall-open-field-plus-holder-control -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `o1c-onecall-open-field-plus-holder-control` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var t as Tk

	static function create(t Tk) returns Self
		return Self{t: t}
	end 'create'
end 'Ht'

function two(a Tk, b Ht)
	b.t = Tk.create(9)
	a.n = 3
end 'two'

function main() returns ExitCode
	let h = Ht.create(Tk.create(1))
	let _ = Ht.create(Tk.create(2))
	two(h.t, b: h)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:14: 'h' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: model-hand-ao-o1e-onecall-open-cond-field-else-arm -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `o1e-onecall-open-cond-field-else-arm` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var t as Tk

	static function create(t Tk) returns Self
		return Self{t: t}
	end 'create'
end 'Ht'

function two(a Tk, b Ht)
	b.t = Tk.create(9)
	a.n = 3
end 'two'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let h = Ht.create(Tk.create(1))
	let g = Ht.create(Tk.create(2))
	two((h if lg(0) != 0 else g).t, b: g)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:37: 'g' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: model-hand-ao-o1r-onecall-open-cond-field-holder-first -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `o1r-onecall-open-cond-field-holder-first` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var t as Tk

	static function create(t Tk) returns Self
		return Self{t: t}
	end 'create'
end 'Ht'

function two(b Ht, a Tk)
	b.t = Tk.create(9)
	a.n = 3
end 'two'

function main() returns ExitCode
	let h = Ht.create(Tk.create(1))
	let g = Ht.create(Tk.create(2))
	two(h, a: (h if 1 != 0 else g).t)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:13: 'h' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: model-hand-ao-o3-onecall-open-cond-iso-plus-holder -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `o3-onecall-open-cond-iso-plus-holder` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hi is unique
	var b as isolated Bx

	static function create(b isolated Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Hi'

function wr2(x Bx, hh Hi)
	let _ = swap hh.b with Bx.create(9)
	x.n = 5
end 'wr2'

function main() returns ExitCode
	let h = Hi.create(Bx.create(1))
	let g = Hi.create(Bx.create(2))
	wr2((h if 1 != 0 else g).b, hh: h)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:28:34: 'h' is mutable outside 'b'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ao-o3c-onecall-open-iso-plus-holder-control -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `o3c-onecall-open-iso-plus-holder-control` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hi is unique
	var b as isolated Bx

	static function create(b isolated Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Hi'

function wr2(x Bx, hh Hi)
	let _ = swap hh.b with Bx.create(9)
	x.n = 5
end 'wr2'

function main() returns ExitCode
	let h = Hi.create(Bx.create(1))
	let _ = Hi.create(Bx.create(2))
	wr2(h.b, hh: h)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:28:15: 'h' is mutable outside 'b'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ao-o3p-onecall-open-cond-iso-plain-holder -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `o3p-onecall-open-cond-iso-plain-holder` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hq
	var b as isolated Bx

	static function create(b isolated Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Hq'

function wr2(x Bx, hh Hq)
	let _ = swap hh.b with Bx.create(9)
	x.n = 5
end 'wr2'

function main() returns ExitCode
	let h = Hq.create(Bx.create(1))
	let g = Hq.create(Bx.create(2))
	wr2((h if 1 != 0 else g).b, hh: h)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:28:34: 'h' is mutable outside 'b'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ao-o3pc-onecall-open-iso-plain-holder-control -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `o3pc-onecall-open-iso-plain-holder-control` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hq
	var b as isolated Bx

	static function create(b isolated Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Hq'

function wr2(x Bx, hh Hq)
	let _ = swap hh.b with Bx.create(9)
	x.n = 5
end 'wr2'

function main() returns ExitCode
	let h = Hq.create(Bx.create(1))
	let _ = Hq.create(Bx.create(2))
	wr2(h.b, hh: h)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:28:15: 'h' is mutable outside 'b'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ao-o3pa-onecall-open-cond-iso-plain-alias -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `o3pa-onecall-open-cond-iso-plain-alias` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hq
	var b as isolated Bx

	static function create(b isolated Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Hq'

function wr2(x Bx, hh Hq)
	let _ = swap hh.b with Bx.create(9)
	x.n = 5
end 'wr2'

function main() returns ExitCode
	let h = Hq.create(Bx.create(1))
	let g = Hq.create(Bx.create(2))
	let a = h
	wr2((h if 1 != 0 else g).b, hh: a)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:29:34: 'a' is mutable outside 'b'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ao-o3pac-onecall-open-iso-plain-alias-control -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `o3pac-onecall-open-iso-plain-alias-control` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hq
	var b as isolated Bx

	static function create(b isolated Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Hq'

function wr2(x Bx, hh Hq)
	let _ = swap hh.b with Bx.create(9)
	x.n = 5
end 'wr2'

function main() returns ExitCode
	let h = Hq.create(Bx.create(1))
	let _ = Hq.create(Bx.create(2))
	let a = h
	wr2(h.b, hh: a)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:29:15: 'a' is mutable outside 'b'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ao-o6-two-opens-same-cond-slot -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `o6-two-opens-same-cond-slot` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var t as Tk

	static function create(t Tk) returns Self
		return Self{t: t}
	end 'create'
end 'Ht'

function two(a Tk, b Tk)
	a.n = 3
	b.n = 4
end 'two'

function main() returns ExitCode
	let h = Ht.create(Tk.create(1))
	let g = Ht.create(Tk.create(2))
	two((h if 1 != 0 else g).t, b: (h if 1 != 0 else g).t)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:34: 'h' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: model-hand-ao-o6c-two-opens-same-slot-control -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `o6c-two-opens-same-slot-control` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var t as Tk

	static function create(t Tk) returns Self
		return Self{t: t}
	end 'create'
end 'Ht'

function two(a Tk, b Tk)
	a.n = 3
	b.n = 4
end 'two'

function main() returns ExitCode
	let h = Ht.create(Tk.create(1))
	let _ = Ht.create(Tk.create(2))
	two(h.t, b: h.t)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:14: 'h' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: model-hand-ao-o6m-open-cond-slot-plus-path-slot -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `o6m-open-cond-slot-plus-path-slot` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ht is unique
	var t as Tk

	static function create(t Tk) returns Self
		return Self{t: t}
	end 'create'
end 'Ht'

function two(a Tk, b Tk)
	a.n = 3
	b.n = 4
end 'two'

function main() returns ExitCode
	let h = Ht.create(Tk.create(1))
	let g = Ht.create(Tk.create(2))
	two((h if 1 != 0 else g).t, b: h.t)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:33: 'h' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: model-hand-ao-o6i-two-iso-opens-cond -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `o6i-two-iso-opens-cond` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Hq
	var b as isolated Bx

	static function create(b isolated Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Hq'

function two(x Bx, y Bx)
	x.n = 3
	y.n = 4
end 'two'

function main() returns ExitCode
	let h = Hq.create(Bx.create(1))
	let g = Hq.create(Bx.create(2))
	two((h if 1 != 0 else g).b, y: h.b)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:28:33: one statement reaches through two isolated values, 'b' and 'b'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-hand-ao-n1-nested-for-cond-and-path-two-lends -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3207. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `n1-nested-for-cond-and-path-two-lends` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ha is unique
	var xs as TkArray

	static function create(xs TkArray) returns Self
		return Self{xs: xs}
	end 'create'
end 'Ha'

function two(a Tk, b Tk)
	a.n = 3
	b.n = 4
end 'two'

function main() returns ExitCode
	let h = Ha.create(TkArray.create(0))
	let g = Ha.create(TkArray.create(0))
	h.xs.push(Tk.create(1))
	for e in (h if 1 != 0 else g).xs 'f1'
		for f in h.xs 'f2'
			two(e, b: f)
		end 'f2'
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:32:11: 'two' needs a writable parameter 'b' at line 23; 'f' is read-only while 'f2' is open
```

<!-- test: model-hand-ao-n1c-nested-for-path-path-two-lends-control -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3207. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `n1c-nested-for-path-path-two-lends-control` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ha is unique
	var xs as TkArray

	static function create(xs TkArray) returns Self
		return Self{xs: xs}
	end 'create'
end 'Ha'

function two(a Tk, b Tk)
	a.n = 3
	b.n = 4
end 'two'

function main() returns ExitCode
	let h = Ha.create(TkArray.create(0))
	let _ = Ha.create(TkArray.create(0))
	h.xs.push(Tk.create(1))
	for e in h.xs 'f1'
		for f in h.xs 'f2'
			two(e, b: f)
		end 'f2'
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:32:11: 'two' needs a writable parameter 'b' at line 23; 'f' is read-only while 'f2' is open
```

<!-- test: model-hand-ao-n2-nested-for-cond-cond-two-lends -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3207. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `n2-nested-for-cond-cond-two-lends` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ha is unique
	var xs as TkArray

	static function create(xs TkArray) returns Self
		return Self{xs: xs}
	end 'create'
end 'Ha'

function two(a Tk, b Tk)
	a.n = 3
	b.n = 4
end 'two'

function main() returns ExitCode
	let h = Ha.create(TkArray.create(0))
	let g = Ha.create(TkArray.create(0))
	h.xs.push(Tk.create(1))
	for e in (h if 1 != 0 else g).xs 'f1'
		for f in (g if 1 != 0 else h).xs 'f2'
			two(e, b: f)
		end 'f2'
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:32:11: 'two' needs a writable parameter 'b' at line 23; 'f' is read-only while 'f2' is open
```

<!-- test: model-hand-ao-s2-stmt-open-cond-iso-field-push-holder -->
The model (`core-m3w-fa24`) refuses it by `C-HOLDER`; the compiler refuses it with E3207. Model program `s2-stmt-open-cond-iso-field-push-holder` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type Bx
	var n as Num
	var ys as NumArray

	static function create(n Num, ys NumArray) returns Self
		return Self{n: n, ys: ys}
	end 'create'
end 'Bx'

type Hi is unique
	var b as isolated Bx

	static function create(b isolated Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Hi'

function bump(h Hi) returns Num
	let _ = swap h.b with Bx.create(7, ys: NumArray.create(0))
	return 1
end 'bump'

function main() returns ExitCode
	let h = Hi.create(Bx.create(1, ys: NumArray.create(0)))
	let g = Hi.create(Bx.create(2, ys: NumArray.create(0)))
	(h if 1 != 0 else g).b.ys.push(bump(h))
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:30:38: 'bump' needs a writable parameter 'h' at line 23; 'h' is read-only while 'b' is open
```

<!-- test: model-hand-ao-s2c-stmt-open-iso-field-push-holder-control -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `s2c-stmt-open-iso-field-push-holder-control` of redteamAO/hand_ao2.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type Bx
	var n as Num
	var ys as NumArray

	static function create(n Num, ys NumArray) returns Self
		return Self{n: n, ys: ys}
	end 'create'
end 'Bx'

type Hi is unique
	var b as isolated Bx

	static function create(b isolated Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Hi'

function bump(h Hi) returns Num
	let _ = swap h.b with Bx.create(7, ys: NumArray.create(0))
	return 1
end 'bump'

function main() returns ExitCode
	let h = Hi.create(Bx.create(1, ys: NumArray.create(0)))
	let _ = Hi.create(Bx.create(2, ys: NumArray.create(0)))
	h.b.ys.push(bump(h))
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:30:19: 'bump' needs a writable parameter 'h' at line 23; 'h' is read-only while 'b' is open
```

<!-- test: model-hand-ao-m4-method-receiver-cond-field -->
The model (`core-m3w-fa24`) refuses it by `C-ONECALL`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m4-method-receiver-cond-field` of redteamAO/hand_ao3.txt, in capmodel `reggio/REPORTRTAO.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function bump(h Ht)
		h.t = Tk.create(7)
		self.n = 3
	end 'bump'
end 'Tk'

type Ht is unique
	var t as Tk

	static function create(t Tk) returns Self
		return Self{t: t}
	end 'create'
end 'Ht'

function main() returns ExitCode
	let h = Ht.create(Tk.create(1))
	let g = Ht.create(Tk.create(2))
	(h if 1 != 0 else g).t.bump(g)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:30: 'g' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: model-hand-fa14-v-fill-plain-var -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `v-fill-plain-var` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeBx(b isolated Bx)
	let _ = b.n
end 'takeBx'

function f(z isolated Bx)
	var v = Bx.create(1)
	v = z
	takeBx(v)
end 'f'

function main() returns ExitCode
	f(Bx.create(2))
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:18:6: cannot store an isolated 'Bx' into 'v', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa14-m-fn-param -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `m-fn-param` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Zp
	var b as Bx

	static function create(b Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Zp'

function zkeep(h Zp, bV isolated Bx)
	h.b = bV
end 'zkeep'

function main() returns ExitCode
	let h = Zp.create(Bx.create(0))
	zkeep(h, bV: Bx.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:21:8: cannot store an isolated 'Bx' into 'b', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa14-m-local-iso -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `m-local-iso` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Zp
	var b as Bx

	static function create(b Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Zp'

function main() returns ExitCode
	let h = Zp.create(Bx.create(0))
	let bV = Bx.create(1) as isolated Bx
	h.b = bV
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:23:8: cannot store an isolated 'Bx' into 'b', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa15-h2-no-try -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `h2-no-try` of redteamFA15.hand_fa15d, in capmodel `reggio/REPORTFA15.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function soup()
	var w = Bx.create(1)
	let a = w as isolated Bx
	takeB(a)
	w = Bx.create(lg(2))
	let _ = w.n
end 'soup'

function main() returns ExitCode
	soup()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-r-let-dies -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r-let-dies` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
	u.n = 3
end 'f'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let t = Tk.create(1)
	f(t, h: h)
	let _ = t.n
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-r-var-overwritten -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r-var-overwritten` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
	var u = t
	u.n = 3
	u = Tk.create(2)
	u.n = 4
end 'f'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let t = Tk.create(1)
	f(t, h: h)
	let _ = t.n
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-r-chain -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r-chain` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
	let w = u
	let x = w
	x.n = 1
end 'f'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let t = Tk.create(1)
	f(t, h: h)
	let _ = t.n
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-r-assign -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r-assign` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
	var u = Tk.create(0)
	u = t
	u.n = 1
end 'f'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let t = Tk.create(1)
	f(t, h: h)
	let _ = t.n
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-r-lent-on -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r-lent-on` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function rdTk(x Tk)
	let _ = x.n
end 'rdTk'

function f(t Tk, h Kp)
	let _ = h
	let u = t
	rdTk(u)
	rdTk(u)
end 'f'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let t = Tk.create(1)
	f(t, h: h)
	let _ = t.n
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-r-in-branch -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r-in-branch` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function lg(x Num) returns Num
	return x
end 'lg'

function f(t Tk, h Kp)
	let _ = h
	if lg(1) != 0 'i1'
		let u = t
		u.n = 2
	end 'i1'
end 'f'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let t = Tk.create(1)
	f(t, h: h)
	let _ = t.n
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-r-method -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r-method` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Mm
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function g(t Tk)
		let u = t
		u.n = 2
	end 'g'
end 'Mm'

function main() returns ExitCode
	let m = Mm.create(0)
	let t = Tk.create(1)
	m.g(t)
	let _ = t.n
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-r-slot-lend -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r-slot-lend` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function fs(t Tk)
	let u = t
	u.n = 3
end 'fs'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	fs(h.k)
	let _ = h.k.n
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-r-slot-lend-store -->
The model (`core-m3w-fa24`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `r-slot-lend-store` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function fs2(t Tk, h2 Kp)
	let u = t
	h2.k = u
end 'fs2'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let g = Kp.create(Tk.create(1))
	fs2(h.k, h2: g)
	let _ = h.k.n
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:28:2: cannot pass 'h.k' to parameter 't' of 'fs2': it keeps a unique value, and a unique value cannot be moved out of a field or an element it was only read from; move it out with `pop` or `remove`, or `swap` it out of an isolated field
```

<!-- test: model-hand-fa17-r-store -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `r-store` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
	let u = t
	h.k = u
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
error E3102: <fragment>:29:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-fa17-r-chain-store -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `r-chain-store` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
	let u = t
	let w = u
	h.k = w
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
error E3102: <fragment>:30:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-fa17-r-assign-store -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `r-assign-store` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
	var u = Tk.create(0)
	u = t
	h.k = u
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
error E3102: <fragment>:30:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-fa17-r-var2-assign-store -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `r-var2-assign-store` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function f2(t Tk, t2 Tk, h Kp)
	var u = t2
	u = t
	h.k = u
end 'f2'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let t = Tk.create(1)
	f2(t, t2: Tk.create(2), h: h)
	let _ = t.n
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:10: use of moved value 't': its ownership moved to the parameter 't' of 'f2', which keeps it
```

<!-- test: model-hand-fa17-r-return -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `r-return` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function f(t Tk, h Kp) returns Tk
	let _ = h
	let u = t
	return u
end 'f'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let t = Tk.create(1)
	let _ = f(t, h: h)
	let _ = t.n
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-fa17-r-record-literal -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `r-record-literal` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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
	let _ = Kp.create(u)
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
error E3102: <fragment>:30:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-fa17-r-keeping-callee -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `r-keeping-callee` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

function keepTk(x Tk, h Kp)
	h.k = x
end 'keepTk'

function f(t Tk, h Kp)
	let u = t
	keepTk(u, h: h)
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
error E3102: <fragment>:33:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-fa17-r-method-store -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `r-method-store` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

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

type Mm
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function g(t Tk, h Kp)
		let u = t
		h.k = u
	end 'g'
end 'Mm'

function main() returns ExitCode
	let m = Mm.create(0)
	let h = Kp.create(Tk.create(0))
	let t = Tk.create(1)
	m.g(t, h: h)
	let _ = t.n
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:38:10: use of moved value 't': its ownership moved to the parameter 't' of 'Mm.g', which keeps it
```

<!-- test: model-hand-fa17-i-fn-store -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `i-fn-store` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

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

function st(b isolated Bx, w Wo)
	w.keep = b
end 'st'

function main() returns ExitCode
	st(Bx.create(1), w: Wo.create(Bx.create(0), item: Bx.create(2)))
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:22:11: cannot store an isolated 'Bx' into 'keep', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa17-i-method-store -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `i-method-store` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Mqq
	var keep as Bx

	static function create(keep Bx) returns Self
		return Self{keep: keep}
	end 'create'

	function st(b isolated Bx)
		self.keep = b
	end 'st'
end 'Mqq'

function main() returns ExitCode
	let m = Mqq.create(Bx.create(0))
	m.st(Bx.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:20:15: cannot store an isolated 'Bx' into 'keep', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa17-m-plain-mutable -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-plain-mutable` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function g(b mutable Bx)
	b.n = 3
end 'g'

function main() returns ExitCode
	let b = Bx.create(1)
	g(b)
	let _ = b.n
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-x-direct-lent-plain-value-control -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `x-direct-lent-plain-value-control` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

function n(t Tq) returns Num
	return t.b.n
end 'n'

function main() returns ExitCode
	let q = Tq.create(1, b: Bx.create(2))
	let z = n(q)
	q.b.n = z
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p2-value-result -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-value-result` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

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

function size(t Tq) returns Num
	return t.n
end 'size'

function main() returns ExitCode
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let r = size((q if 1 != 0 else q2))
	q.n = r
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p2-not-isolated -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-not-isolated` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

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

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let q = Tq.create(1, b: Bx.create(2))
	let q2 = Tq.create(1, b: Bx.create(2))
	let _ = Tq.create(1, b: Bx.create(2))
	let r = part((q if 1 != 0 else q2))
	r.n = 4
	q.n = 3
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p3-kept-not-isolated -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p3-kept-not-isolated` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

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

function keepW(t Tq) returns Wq
	return Wq.create(t)
end 'keepW'

function main() returns ExitCode
	let qV = Tq.create(1, b: Bx.create(2))
	let w = keepW(qV)
	w.q.n = 4
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p4-read-part -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p4-read-part` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

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

function rdPart(t Tq) returns Num
	let x = t.b
	return x.n
end 'rdPart'

function main() returns ExitCode
	let q = Tq.create(1, b: Bx.create(2))
	let r = rdPart(q)
	q.n = r
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p4-return-part -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p4-return-part` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

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

function part(t Tq) returns Bx
	return t.b
end 'part'

function main() returns ExitCode
	let q = Tq.create(1, b: Bx.create(2))
	let _ = part(q)
	q.n = 3
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p4-store-part -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p4-store-part` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

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

function stPart(t Tq, o Ko)
	o.keep = t.b
end 'stPart'

function main() returns ExitCode
	let q = Tq.create(1, b: Bx.create(2))
	let o = Ko.create(Bx.create(0), n: 0)
	stPart(q, o: o)
	q.n = 3
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa20-p4-lent-twice-read -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p4-lent-twice-read` of redteamFA20.hand_fa20, in capmodel `reggio/REPORTFA20.txt`.

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

function part(t Tq) returns Bx
	return t.b
end 'part'

function rdPart(t Tq) returns Num
	let x = t.b
	return x.n
end 'rdPart'

function main() returns ExitCode
	let q = Tq.create(1, b: Bx.create(2))
	let a = rdPart(q)
	q.n = a
	let _ = part(q)
	q.n = 4
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-k-for-field-container-write -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `k-for-field-container-write` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

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

type Ha is unique
	var xs as TqArray
	var n as Num

	static function create(xs TqArray, n Num) returns Self
		return Self{xs: xs, n: n}
	end 'create'
end 'Ha'

function main() returns ExitCode
	let h = Ha.create(TqArray.create(0), n: 0)
	h.xs.push(Tq.create(1, b: Bx.create(2)))
	for t in h.xs 'f1'
		h.n = 5
		t.n = 2
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:35:3: cannot write 'n' through 'h', which is read-only while 'f1' drains it
```

<!-- test: model-hand-fa24-a1-for-cond-path-elem-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a1-for-cond-path-elem-write` of redteamFA24/hand_fa24.txt, in capmodel `reggio/REPORTFA24.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ha is unique
	var xs as TkArray

	static function create(xs TkArray) returns Self
		return Self{xs: xs}
	end 'create'
end 'Ha'

function main() returns ExitCode
	let h = Ha.create(TkArray.create(0))
	let g = Ha.create(TkArray.create(0))
	h.xs.push(Tk.create(1))
	for e in (h if 1 != 0 else g).xs 'f1'
		e.n = 2
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa24-a3-for-cond-locals-unrelated-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a3-for-cond-locals-unrelated-write` of redteamFA24/hand_fa24.txt, in capmodel `reggio/REPORTFA24.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Ha is unique
	var xs as TkArray
	var c as Num

	static function create(xs TkArray, c Num) returns Self
		return Self{xs: xs, c: c}
	end 'create'
end 'Ha'

function main() returns ExitCode
	let h = Ha.create(TkArray.create(0), c: 0)
	let g = Ha.create(TkArray.create(0), c: 0)
	let o = Ha.create(TkArray.create(0), c: 0)
	h.xs.push(Tk.create(1))
	for e in (h if 1 != 0 else g).xs 'f1'
		o.c = 5
		e.n = 2
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```
