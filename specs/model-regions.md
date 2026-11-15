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
the day the compiler stops agreeing with a verdict the model pinned. The rule set is `core-m3w-fa13`.

## Tests

<!-- test: model-finding-n-c1-no-throw -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `C1-no-throw` of redteamN/cand1.txt, in capmodel `reggio/REPORTRTN.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `C1-min` of redteamN/cand1.txt, in capmodel `reggio/REPORTRTN.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `V1-main-direct` of redteamN/cand1b.txt, in capmodel `reggio/REPORTRTN.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `V2-fresh-arg` of redteamN/cand1b.txt, in capmodel `reggio/REPORTRTN.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `V3-no-read-after` of redteamN/cand1b.txt, in capmodel `reggio/REPORTRTN.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `V4-no-f` of redteamN/cand1b.txt, in capmodel `reggio/REPORTRTN.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `V5-call-only-f-no-pick` of redteamN/cand1b.txt, in capmodel `reggio/REPORTRTN.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `V6-tk-only` of redteamN/cand1b.txt, in capmodel `reggio/REPORTRTN.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `V7-use-a0-after` of redteamN/cand1b.txt, in capmodel `reggio/REPORTRTN.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `R6-unique-in-array-for-window: a for over unique elements is a window; the builder is handed an outer plain record` of redteamR/holes6.txt, in capmodel `reggio/REPORTRTR.txt`.

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
The model (`core-m3w-fa13`) refuses it by `R4-ARG`; the compiler refuses it with E3197. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `K2-cond-of-bytes-two-holders: the same with a plain cond of two mixed values` of redteamS/h_ks.txt, in capmodel `reggio/REPORTRTS.txt`.

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
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `U1-unique-into-holder-then-reuse: a callee moves its unique parameter into a holder; the caller uses the token after` of redteamS/h_uniq.txt, in capmodel `reggio/REPORTRTS.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `U2-unique-lent-read: a callee only reads its unique parameter; the caller reuses it` of redteamS/h_uniq.txt, in capmodel `reggio/REPORTRTS.txt`.

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
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `U3-unique-in-holder-two-calls: two calls, each moving the token into a holder` of redteamS/h_uniq.txt, in capmodel `reggio/REPORTRTS.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `U7-unique-returned-through-holder: the holder is returned and the token read out by the caller twice` of redteamS/h_uniq.txt, in capmodel `reggio/REPORTRTS.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `Y2-ctl-plain-same` of redteamY/min6.txt, in capmodel `reggio/REPORTRTY.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `polyrec-record-in-state` of redteamAA/p4.txt, in capmodel `reggio/REPORTRTAA.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `inline-1` of reggio/REPORTRTAB.txt, in capmodel `reggio/REPORTRTAB.txt`.

```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ad-swap-field-self -->
The model (`core-m3w-fa13`) refuses it by `C-ISOREAD`; the compiler refuses it with E3198. Model program `swap-field-self` of redteamAD/t3.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `frozen-field-iso-recv-call-writes-outer` of redteamAE/p19.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa13`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `mutable-field-iso-recv-call-writes-outer-ctl` of redteamAE/p19.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-freeze-box-with-nothing` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `fz-clone-linear-field-of-nonlinear` of redteamAB.hand_ab8, in capmodel `reggio/REPORTRTAB.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ub-iter-over-array-of-lin` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `cond-fresh-arm-effect` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-local-isolated-once` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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

<!-- test: model-hand-ad-k-arr -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `k-arr` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `k-cell` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-id` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-id-then-use` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-put` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-put-iso-cell` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-put-iso-cell-once` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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

<!-- test: model-hand-ad-k-swap-in-callee -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3198, E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-swap-in-callee` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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

function sw(c Cell2, tV Tok)
	let _ = swap c.t with tV
end 'sw'

function main() returns ExitCode
	let c = Cell2.create(0, t: Tok.create(0)) as isolated Cell2
	let tV = Tok.create(1) as isolated Tok
	sw(c, tV: tV)
	sw(c, tV: tV)
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:22:10: 'c.t' is not an isolated field, so there is no region for `swap` to take out
error E3201: <fragment>:28:12: one statement reaches through two isolated values, 'c' and 'tV'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-hand-ad-k-swap-in-callee-plain -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3198. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-swap-in-callee-plain` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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

function sw(c Cell2, tV Tok)
	let _ = swap c.t with tV
end 'sw'

function main() returns ExitCode
	let c = Cell2.create(0, t: Tok.create(0))
	let tV = Tok.create(1)
	sw(c, tV: tV)
	sw(c, tV: tV)
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:22:10: 'c.t' is not an isolated field, so there is no region for `swap` to take out
```

<!-- test: model-hand-ad-k-swap-in-callee-then-read -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3198, E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-swap-in-callee-then-read` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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

function sw(c Cell2, tV Tok)
	let _ = swap c.t with tV
end 'sw'

function main() returns ExitCode
	let c = Cell2.create(0, t: Tok.create(0))
	let tV = Tok.create(1) as isolated Tok
	sw(c, tV: tV)
	let _ = rd(tV)
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:26:10: 'c.t' is not an isolated field, so there is no region for `swap` to take out
error E3201: <fragment>:32:5: 'c' is mutable outside 'tV'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ad-k-swap-in-callee-then-read-iso-cell -->
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3198, E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-swap-in-callee-then-read-iso-cell` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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

function sw(c Cell2, tV Tok)
	let _ = swap c.t with tV
end 'sw'

function main() returns ExitCode
	let c = Cell2.create(0, t: Tok.create(0)) as isolated Cell2
	let tV = Tok.create(1) as isolated Tok
	sw(c, tV: tV)
	let _ = rd(tV)
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:26:10: 'c.t' is not an isolated field, so there is no region for `swap` to take out
error E3201: <fragment>:32:12: one statement reaches through two isolated values, 'c' and 'tV'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-hand-ad-k-new-cell -->
The model (`core-m3w-fa13`) refuses it by `C-STORE`; the compiler refuses it with E3201. Model program `k-new-cell` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa13`) refuses it by `C-STORE`; the compiler refuses it with E3201. Model program `k-new-cell-twice` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa13`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `k-array-push` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-method-keep` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-method-keep-iso-recv` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `k-cond` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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

<!-- test: model-hand-af-sh7-inner-let-iso-bx -->
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `sh7-inner-let-iso-bx` of redteamAF.hand_af16, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa13`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m17-for-unique-array-copy` of redteamAG.hand_ag10, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m18-for-unique-array-bare-read-call` of redteamAG.hand_ag10, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa13`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u18-nested-for-unique-array` of redteamAG.hand_ag14, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa13`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `u19-nested-for-unique-array-write` of redteamAG.hand_ag14, in capmodel `reggio/REPORTRTAG.txt`.

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
