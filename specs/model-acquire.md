---
feature: model-acquire
status: experimental
keywords: [model, capmodel, region, capability]
category: semantics
---

# Acquired states

## Documentation

These cases are programs of the capmodel executable model, translated to Maxon by
`scripts/model-specs/generate.py` (`docs/SPECS.md`, "Model-derived specs") and pinned to the model's verdict on
acquire blocks over service states. A program the model accepts runs and exits 0; one it refuses fails to compile, with the diagnostic
the compiler gives. A case is written only where the compiler and the model agree, so the suite fails
the day the compiler stops agreeing with a verdict the model pinned. The rule set is `core-m3w-fa14`.

## Tests

<!-- test: model-finding-n-c2-no-wrapper -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `C2-no-wrapper` of redteamN/cand2.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function idb(bV Bt) returns Bt
	return bV
end 'idb'

function g(s Sv.handle)
	acquire s 'a1'
		let _ = idb(s.r.bytes)
		s.r.bytes.b = 1
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:36:11: 'idb(s.r.bytes)' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-c2-no-write-after -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `C2-no-write-after` of redteamN/cand2.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function idb(bV Bt) returns Bt
	return bV
end 'idb'

function g(s Sv.handle)
	acquire s 'a1'
		let _ = idb(s.r.bytes)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:36:11: 'idb(s.r.bytes)' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-c2-read-instead -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `C2-read-instead` of redteamN/cand2.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function idb(bV Bt) returns Bt
	return bV
end 'idb'

function g(s Sv.handle) returns Num
	acquire s 'a1'
		let _ = idb(s.r.bytes)
		return s.r.bytes.b
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	let _ = g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:36:11: 'idb(s.r.bytes)' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-c2-write-via-m -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `C2-write-via-m` of redteamN/cand2.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function idb(bV Bt) returns Bt
	return bV
end 'idb'

function g(s Sv.handle)
	acquire s 'a1'
		let m = idb(s.r.bytes)
		m.b = 1
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:36:11: 'idb(s.r.bytes)' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-c2-plain-fn-no-result-use -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `C2-plain-fn-no-result-use` of redteamN/cand2.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function rd(bV Bt) returns Num
	return bV.b
end 'rd'

function g(s Sv.handle)
	acquire s 'a1'
		let _ = rd(s.r.bytes)
		s.r.bytes.b = 1
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	g(sv)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-n-c2-mutself-no-wrapper-send -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `C2-mutself-no-wrapper-send` of redteamN/cand2.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function idb(bV Bt) returns Bt
	return bV
end 'idb'

function g(s Sv.handle)
	acquire s 'a1'
		let _ = idb(s.r.bytes)
		s.k = 1
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:36:11: 'idb(s.r.bytes)' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-d1-direct-alias-then-write -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `D1-direct-alias-then-write` of redteamN/cand2b.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function g(s Sv.handle)
	acquire s 'a1'
		let _ = s.r.bytes
		s.r.bytes.b = 1
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:32:11: 's.r.bytes' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-d2-direct-alias-read-after -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `D2-direct-alias-read-after` of redteamN/cand2b.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function g(s Sv.handle) returns Num
	acquire s 'a1'
		let m = s.r.bytes
		s.r.bytes.b = 1
		return m.b
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	let _ = g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:32:11: 's.r.bytes' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-d3-call-alias-then-write-read-after -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `D3-call-alias-then-write-read-after` of redteamN/cand2b.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function idb(bV Bt) returns Bt
	return bV
end 'idb'

function g(s Sv.handle) returns Num
	acquire s 'a1'
		let m = idb(s.r.bytes)
		s.r.bytes.b = 1
		return m.b
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	let _ = g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:36:11: 'idb(s.r.bytes)' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-d4-method-alias -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `D4-method-alias` of redteamN/cand2b.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'

	function data() returns Bt
		return self.bytes
	end 'data'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function g(s Sv.handle)
	acquire s 'a1'
		let _ = s.r.data()
		s.r.bytes.b = 1
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:36:11: 's.r.data()' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-d5-param-alias -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `D5-param-alias` of redteamN/cand2b.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function g(s Sv.handle, p Bt)
	acquire s 'a1'
		let _ = p
		s.r.bytes.b = 1
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	let q = Bt.create(1)
	g(sv, p: q)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-n-d6-state-field-alias-two -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `D6-state-field-alias-two` of redteamN/cand2b.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var o as Rd
	var k as Num

	static function create(r isolated Rd, o isolated Rd, k Num) returns Self
		return Self{r: merge r, o: merge o, k: k}
	end 'create'
end 'Sv'

function g(s Sv.handle)
	acquire s 'a1'
		let _ = s.o.bytes
		s.r.bytes.b = 1
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), o: Rd.create(Bt.create(8), pos: 0), k: 0)
	g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:33:11: 's.o.bytes' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-e1-replace-then-read-alias -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `E1-replace-then-read-alias` of redteamN/cand2c.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function g(s Sv.handle) returns Num
	acquire s 'a1'
		let m = s.r.bytes
		s.r.bytes = Bt.create(2)
		return m.b
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	let _ = g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:32:11: 's.r.bytes' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-e2-method-write-then-read-alias -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `E2-method-write-then-read-alias` of redteamN/cand2c.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'

	function wr()
		self.r.bytes.b = 5
	end 'wr'
end 'Sv'

function g(s Sv.handle) returns Num
	acquire s 'a1'
		let m = s.r.bytes
		s.wr()
		return m.b
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	let _ = g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:36:11: 's.r.bytes' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-e3-alias-of-whole-row -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `E3-alias-of-whole-row` of redteamN/cand2c.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function g(s Sv.handle) returns Num
	acquire s 'a1'
		let m = s.r
		s.r.pos = 3
		return m.pos
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	let _ = g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:32:11: 's.r' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-e4-modify-state-field-block -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `E4-modify-state-field-block` of redteamN/cand2c.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function g(s Sv.handle) returns Num
	acquire s 'a1'
		let m = s.r.bytes
		modify s.r 'm3'
			s.r.pos = 3
		end 'm3'
		return m.b
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	let _ = g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:32:11: 's.r.bytes' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-e5-int-field-write-alias-of-other-field -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `E5-int-field-write-alias-of-other-field` of redteamN/cand2c.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function g(s Sv.handle) returns Num
	acquire s 'a1'
		let m = s.r.bytes
		s.k = 3
		return m.b
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	let _ = g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:32:11: 's.r.bytes' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-e6-two-states-alias-of-first-write-second-field-of-first -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `E6-two-states-alias-of-first-write-second-field-of-first` of redteamN/cand2c.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function g(s Sv.handle, t Sv.handle) returns Num
	acquire s, t 'a1'
		let m = s.r.bytes
		t.r.bytes.b = 1
		return m.b
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	let _ = g(sv, t: sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:32:11: 's.r.bytes' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-e7-different-states-same -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `E7-different-states-same` of redteamN/cand2c.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var r as Rd
	var k as Num

	static function create(r isolated Rd, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function g(s Sv.handle, t Sv.handle) returns Num
	acquire s, t 'a1'
		let m = s.r.bytes
		t.r.bytes.b = 1
		return m.b
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0), k: 0)
	let sw = spawn Sv.create(Rd.create(Bt.create(8), pos: 0), k: 0)
	let _ = g(sv, t: sw)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:32:11: 's.r.bytes' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-rtn-h1-minimal -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `RTN-H1-minimal` of redteamN/hole1min.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'
end 'Sv'

function g(s Sv.handle)
	acquire s 'a1'
		let _ = s.r
		s.r.b = 1
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:22:11: 's.r' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-rtn-h1-visible -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `RTN-H1-visible` of redteamN/hole1min.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'
end 'Sv'

function g(s Sv.handle) returns Num
	acquire s 'a1'
		let m = s.r
		s.r.b = 1
		return m.b
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	let _ = g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:22:11: 's.r' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-rtn-h1-via-m1-mixed-call -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `RTN-H1-via-m1-mixed-call` of redteamN/hole1min.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'
end 'Sv'

function pick(bV Bt, c Num) returns Bt
	if c != 0 'i1'
		return Bt.create(1)
	end 'i1' else 'e2'
		return bV
	end 'e2'
end 'pick'

function g(s Sv.handle)
	acquire s 'a1'
		let _ = pick(s.r, c: 0)
		s.r.b = 1
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:30:11: 'pick(s.r, c: 0)' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-rtn-h1-control-no-alias -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `RTN-H1-control-no-alias` of redteamN/hole1min.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'
end 'Sv'

function g(s Sv.handle)
	acquire s 'a1'
		let _ = 5 as Num
		s.r.b = 1
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	g(sv)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-n-rtn-h1-control-alias-dead-before-write -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `RTN-H1-control-alias-dead-before-write` of redteamN/hole1min.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'
end 'Sv'

function g(s Sv.handle) returns Num
	acquire s 'a1'
		let m = s.r
		let x = m.b
		s.r.b = 1
		return x
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	let _ = g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:22:11: 's.r' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-n-rtn-h1-two-names -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `RTN-H1-two-names` of redteamN/hole1min.txt, in capmodel `reggio/REPORTRTN.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'
end 'Sv'

function g(s Sv.handle, t Sv.handle) returns Num
	acquire s, t 'a1'
		let m = s.r
		t.r.b = 1
		return m.b
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	let _ = g(sv, t: sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:22:11: 's.r' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-w-w1-minimal-method-view -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `W1-minimal-method-view` of redteamW/min1.txt, in capmodel `reggio/REPORTRTW.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'

	function peekR() returns Bt
		return self.r
	end 'peekR'
end 'Sv'

function g(s Sv.handle) returns Num
	acquire s 'a1'
		let m = s.peekR()
		s.r.b = 1
		return m.b
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	let _ = g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:26:11: 's.peekR()' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-w-w1-minimal-method-view-unused -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `W1-minimal-method-view-unused` of redteamW/min1.txt, in capmodel `reggio/REPORTRTW.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'

	function peekR() returns Bt
		return self.r
	end 'peekR'
end 'Sv'

function g(s Sv.handle)
	acquire s 'a1'
		let _ = s.peekR()
		s.r.b = 1
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:26:11: 's.peekR()' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-w-w1-control-fieldread -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `W1-control-fieldread` of redteamW/min1.txt, in capmodel `reggio/REPORTRTW.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'

	function peekR() returns Bt
		return self.r
	end 'peekR'
end 'Sv'

function g(s Sv.handle) returns Num
	acquire s 'a1'
		let m = s.r
		s.r.b = 1
		return m.b
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	let _ = g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:26:11: 's.r' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-w-w1-two-names-same-service -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `W1-two-names-same-service` of redteamW/min1.txt, in capmodel `reggio/REPORTRTW.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'

	function peekR() returns Bt
		return self.r
	end 'peekR'
end 'Sv'

function g(s Sv.handle, t Sv.handle) returns Num
	acquire s, t 'a1'
		let m = s.peekR()
		t.r.b = 1
		return m.b
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	let _ = g(sv, t: sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:26:11: 's.peekR()' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-w-w1-two-names-same-service-fieldread-control -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `W1-two-names-same-service-fieldread-control` of redteamW/min1.txt, in capmodel `reggio/REPORTRTW.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'

	function peekR() returns Bt
		return self.r
	end 'peekR'
end 'Sv'

function g(s Sv.handle, t Sv.handle) returns Num
	acquire s, t 'a1'
		let m = s.r
		t.r.b = 1
		return m.b
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	let _ = g(sv, t: sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:26:11: 's.r' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-w-x1-readonly-view-no-write -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `X1-readonly-view-no-write` of redteamW/min2.txt, in capmodel `reggio/REPORTRTW.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rd
	var bytes as Bt
	var pos as Num

	static function create(bytes Bt, pos Num) returns Self
		return Self{bytes: bytes, pos: pos}
	end 'create'
end 'Rd'

type Sv
	var rd as Rd

	static function create(rd isolated Rd) returns Self
		return Self{rd: merge rd}
	end 'create'
end 'Sv'

function g(s Sv.handle) returns Num
	acquire s 'a1'
		let m = s.rd
		return m.pos
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Rd.create(Bt.create(7), pos: 0))
	let _ = g(sv)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:31:11: 's.rd' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-w-x2-clone-across -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `X2-clone-across` of redteamW/min2.txt, in capmodel `reggio/REPORTRTW.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'
end 'Sv'

function g(s Sv.handle, t Sv.handle) returns Num
	acquire s, t 'a1'
		t.r = s.r.clone()
		return 0
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let a = spawn Sv.create(Bt.create(7))
	let bV = spawn Sv.create(Bt.create(8))
	let _ = g(a, t: bV)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:22:3: cannot write 'r' through 't', which is read-only while 'the isolated value' is open
```

<!-- test: model-finding-w-x3-two-statement-clone-across -->
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3201. Model program `X3-two-statement-clone-across` of redteamW/min2.txt, in capmodel `reggio/REPORTRTW.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'
end 'Sv'

function g(s Sv.handle, t Sv.handle) returns Num
	acquire s, t 'a1'
		let c = s.r.clone()
		t.r = c
		return 0
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let a = spawn Sv.create(Bt.create(7))
	let bV = spawn Sv.create(Bt.create(8))
	let _ = g(a, t: bV)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:23:9: 'c' is mutable outside 't'; a store into an isolated region takes values, immutable or isolated
```

<!-- test: model-finding-w-x4-two-statement-value-across -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `X4-two-statement-value-across` of redteamW/min3.txt, in capmodel `reggio/REPORTRTW.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt
	var k as Num

	static function create(r isolated Bt, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function g(s Sv.handle, t Sv.handle) returns Num
	acquire s, t 'a1'
		let v = s.k
		t.k = v
		return 0
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let a = spawn Sv.create(Bt.create(7), k: 1)
	let bV = spawn Sv.create(Bt.create(8), k: 2)
	let _ = g(a, t: bV)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-w-x5-one-statement-value-across -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `X5-one-statement-value-across` of redteamW/min3.txt, in capmodel `reggio/REPORTRTW.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt
	var k as Num

	static function create(r isolated Bt, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function g(s Sv.handle, t Sv.handle) returns Num
	acquire s, t 'a1'
		t.k = s.k
		return 0
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let a = spawn Sv.create(Bt.create(7), k: 1)
	let bV = spawn Sv.create(Bt.create(8), k: 2)
	let _ = g(a, t: bV)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:23:3: cannot write 'k' through 't', which is read-only while 'the isolated value' is open
```

<!-- test: model-finding-w-x6-clone-across-core-m3w-check -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `X6-clone-across-core-m3w-check` of redteamW/min3.txt, in capmodel `reggio/REPORTRTW.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt
	var k as Num

	static function create(r isolated Bt, k Num) returns Self
		return Self{r: merge r, k: k}
	end 'create'
end 'Sv'

function g(s Sv.handle, t Sv.handle) returns Num
	acquire s, t 'a1'
		t.r = s.r.clone()
		return 0
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let a = spawn Sv.create(Bt.create(7), k: 1)
	let bV = spawn Sv.create(Bt.create(8), k: 2)
	let _ = g(a, t: bV)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:23:3: cannot write 'r' through 't', which is read-only while 'the isolated value' is open
```

<!-- test: model-finding-x-x1-minimal-call-operands -->
The model (`core-m3w-fa14`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `X1-minimal-call-operands` of redteamX/min1.txt, in capmodel `reggio/REPORTRTX.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'
end 'Sv'

function wb2(a Bt, bV Bt) returns Num
	a.b = 5
	return bV.b
end 'wb2'

function g(s Sv.handle, t Sv.handle) returns Num
	acquire s, t 'a1'
		let n = wb2(s.r, bV: t.r)
		return n
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	let _ = g(sv, t: sv)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:27:24: one statement reaches through two acquired states, 's' and 't', which may be one service; bind a value read from one of them in its own statement first
```

<!-- test: model-finding-x-x1-minimal-call-operands-distinct-services -->
The model (`core-m3w-fa14`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `X1-minimal-call-operands-distinct-services` of redteamX/min1.txt, in capmodel `reggio/REPORTRTX.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'
end 'Sv'

function wb2(a Bt, bV Bt) returns Num
	a.b = 5
	return bV.b
end 'wb2'

function g(s Sv.handle, t Sv.handle) returns Num
	acquire s, t 'a1'
		let n = wb2(s.r, bV: t.r)
		return n
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	let sw = spawn Sv.create(Bt.create(8))
	let _ = g(sv, t: sw)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:27:24: one statement reaches through two acquired states, 's' and 't', which may be one service; bind a value read from one of them in its own statement first
```

<!-- test: model-finding-x-x1-view-kept-in-callee -->
The model (`core-m3w-fa14`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `X1-view-kept-in-callee` of redteamX/min1.txt, in capmodel `reggio/REPORTRTX.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'
end 'Sv'

function wbk(a Bt, bV Bt) returns Num
	let m = bV
	a.b = 5
	return m.b
end 'wbk'

function g(s Sv.handle, t Sv.handle) returns Num
	acquire s, t 'a1'
		let n = wbk(s.r, bV: t.r)
		return n
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	let _ = g(sv, t: sv)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:28:24: one statement reaches through two acquired states, 's' and 't', which may be one service; bind a value read from one of them in its own statement first
```

<!-- test: model-finding-x-x1-receiver -->
The model (`core-m3w-fa14`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `X1-receiver` of redteamX/min1.txt, in capmodel `reggio/REPORTRTX.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'

	function giveTo(x Bt) returns Num
		x.b = 5
		return self.b
	end 'giveTo'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'
end 'Sv'

function g(s Sv.handle, t Sv.handle) returns Num
	acquire s, t 'a1'
		let n = t.r.giveTo(s.r)
		return n
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	let _ = g(sv, t: sv)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:27:22: one statement reaches through two acquired states, 't' and 's', which may be one service; bind a value read from one of them in its own statement first
```

<!-- test: model-finding-x-x1-m1-pick -->
The model (`core-m3w-fa14`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `X1-m1-pick` of redteamX/min1.txt, in capmodel `reggio/REPORTRTX.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var r as Bt

	static function create(r isolated Bt) returns Self
		return Self{r: merge r}
	end 'create'
end 'Sv'

function wb2(a Bt, bV Bt) returns Num
	a.b = 5
	return bV.b
end 'wb2'

function pick(bV Bt, c Num) returns Bt
	if c != 0 'i1'
		return Bt.create(1)
	end 'i1' else 'e2'
		return bV
	end 'e2'
end 'pick'

function g(s Sv.handle, t Sv.handle) returns Num
	acquire s, t 'a1'
		let n = wb2(s.r, bV: pick(t.r, c: 0))
		return n
	end 'a1' otherwise 's2'
		return 0
	end 's2'
end 'g'

function main() returns ExitCode
	let sv = spawn Sv.create(Bt.create(7))
	let _ = g(sv, t: sv)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:35:29: one statement reaches through two acquired states, 's' and 't', which may be one service; bind a value read from one of them in its own statement first
```

<!-- test: model-finding-y-y1-unique-array-store -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `Y1-unique-array-store` of redteamY/min1.txt, in capmodel `reggio/REPORTRTY.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs
	var ar as TkArray

	static function create(ar isolated TkArray) returns Self
		return Self{ar: merge ar}
	end 'create'
end 'Hs'

function g(s Hs.handle)
	acquire s 'a1'
		s.ar = TkArray.create(0)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let s = spawn Hs.create(TkArray.create(0))
	g(s)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-y-y1-union-store -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `Y1-union-store` of redteamY/min1.txt, in capmodel `reggio/REPORTRTY.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	have(t Tk)
	none
end 'UT'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs
	var u as UT

	static function create(u isolated UT) returns Self
		return Self{u: merge u}
	end 'create'
end 'Hs'

function g(s Hs.handle)
	acquire s 'a1'
		s.u = UT.none
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let s = spawn Hs.create(UT.none)
	g(s)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-y-y1-plain-record-reaching-unique-store -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `Y1-plain-record-reaching-unique-store` of redteamY/min1.txt, in capmodel `reggio/REPORTRTY.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hold
	var tk as Tk

	static function create(tk Tk) returns Self
		return Self{tk: tk}
	end 'create'
end 'Hold'

type Hs
	var ho as Hold

	static function create(ho isolated Hold) returns Self
		return Self{ho: merge ho}
	end 'create'
end 'Hs'

function g(s Hs.handle)
	acquire s 'a1'
		s.ho = Hold.create(Tk.create(1))
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let s = spawn Hs.create(Hold.create(Tk.create(2)))
	g(s)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-y-y1-ctl-declared-unique-record-store -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `Y1-ctl-declared-unique-record-store` of redteamY/min1.txt, in capmodel `reggio/REPORTRTY.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs
	var tk as Tk

	static function create(tk isolated Tk) returns Self
		return Self{tk: merge tk}
	end 'create'
end 'Hs'

function g(s Hs.handle)
	acquire s 'a1'
		s.tk = Tk.create(5)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let s = spawn Hs.create(Tk.create(1))
	g(s)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-y-y1-ctl-isolated-slot-store -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `Y1-ctl-isolated-slot-store` of redteamY/min1.txt, in capmodel `reggio/REPORTRTY.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Hs
	var i as isolated Bt

	static function create(i isolated Bt) returns Self
		return Self{i: i}
	end 'create'
end 'Hs'

function g(s Hs.handle)
	acquire s 'a1'
		s.i = Bt.create(5)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'g'

function main() returns ExitCode
	let s = spawn Hs.create(Bt.create(1))
	g(s)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-z-send-view -->
The model (`core-m3w-fa14`) refuses it by `R3-SEND`; the compiler refuses it with E3217. Model program `send-view` of redteamZ/p2.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var n as Num
	var r as Bt

	static function create(n Num, r isolated Bt) returns Self
		return Self{n: n, r: merge r}
	end 'create'
end 'Sv'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(bV Bt)
		let _ = bV.b
	end 'take'
end 'Sk'

function g(s Sv.handle, k Sk.handle) returns Num
	acquire s 'a1'
		try k.take(s.r) otherwise ignore
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'g'

function main() returns ExitCode
	let s = spawn Sv.create(0, r: Bt.create(1))
	let k = spawn Sk.create(0)
	let _ = g(s, k: k)
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:44:10: parameter `bV` of the message `Sk.take` is a `Bt` with no capability word, and this `spawn` makes `Sk` a service — whose messages hand their arguments to another green thread, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `take(bV isolated …)` or `take(bV immutable …)`
```

<!-- test: model-finding-z-send-view-clone -->
The model (`core-m3w-fa14`) refuses it by `R3-SEND`; the compiler refuses it with E3217. Model program `send-view-clone` of redteamZ/p2.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var n as Num
	var r as Bt

	static function create(n Num, r isolated Bt) returns Self
		return Self{n: n, r: merge r}
	end 'create'
end 'Sv'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(bV Bt)
		let _ = bV.b
	end 'take'
end 'Sk'

function g(s Sv.handle, k Sk.handle) returns Num
	acquire s 'a1'
		try k.take(s.r.clone()) otherwise ignore
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'g'

function main() returns ExitCode
	let s = spawn Sv.create(0, r: Bt.create(1))
	let k = spawn Sk.create(0)
	let _ = g(s, k: k)
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:44:10: parameter `bV` of the message `Sk.take` is a `Bt` with no capability word, and this `spawn` makes `Sk` a service — whose messages hand their arguments to another green thread, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `take(bV isolated …)` or `take(bV immutable …)`
```

<!-- test: model-finding-z-spawn-view -->
The model (`core-m3w-fa14`) refuses it by `R3-SEND`; the compiler refuses it with E3200. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `spawn-view` of redteamZ/p2.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var n as Num
	var r as Bt

	static function create(n Num, r isolated Bt) returns Self
		return Self{n: n, r: merge r}
	end 'create'
end 'Sv'

type Sk
	var n as Num
	var b as Bt

	static function create(n Num, b isolated Bt) returns Self
		return Self{n: n, b: merge b}
	end 'create'
end 'Sk'

function g(s Sv.handle) returns Num
	acquire s 'a1'
		let _ = spawn Sk.create(0, b: s.r)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'g'

function main() returns ExitCode
	let s = spawn Sv.create(0, r: Bt.create(1))
	let _ = g(s)
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:32:33: 's.r' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-finding-z-cp-store-iso-local -->
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `cp-store-iso-local` of redteamZ/p3.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BtArray = Array with Bt

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Acc
	var bal as Num
	var hist as BtArray

	static function create(bal Num, hist isolated BtArray) returns Self
		return Self{bal: bal, hist: merge hist}
	end 'create'

	function entries() returns BtArray
		return self.hist
	end 'entries'
end 'Acc'

function mix(first Acc.handle, second Acc.handle) returns Num
	acquire first, second 'a1'
		let c = second.entries().clone() as isolated BtArray
		first.hist = c
	end 'a1' otherwise 's2'
		return 0
	end 's2'
	return 1
end 'mix'

function main() returns ExitCode
	let a = spawn Acc.create(1, hist: BtArray.create(0))
	let bV = spawn Acc.create(2, hist: BtArray.create(0))
	let _ = mix(a, second: bV)
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:29:16: cannot store an isolated 'BtArray' into 'hist', which holds a mutable 'BtArray'; `merge` it first
```

<!-- test: model-finding-z-cp-modify-store-iso-local -->
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `cp-modify-store-iso-local` of redteamZ/p3.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BtArray = Array with Bt

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Acc
	var bal as Num
	var hist as BtArray

	static function create(bal Num, hist isolated BtArray) returns Self
		return Self{bal: bal, hist: merge hist}
	end 'create'

	function entries() returns BtArray
		return self.hist
	end 'entries'
end 'Acc'

function mix(first Acc.handle, second Acc.handle) returns Num
	acquire first, second 'a1'
		let c = second.entries().clone() as isolated BtArray
		modify first 'm3'
			first.hist = c
		end 'm3'
	end 'a1' otherwise 's2'
		return 0
	end 's2'
	return 1
end 'mix'

function main() returns ExitCode
	let a = spawn Acc.create(1, hist: BtArray.create(0))
	let bV = spawn Acc.create(2, hist: BtArray.create(0))
	let _ = mix(a, second: bV)
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:30:17: cannot store an isolated 'BtArray' into 'hist', which holds a mutable 'BtArray'; `merge` it first
```

<!-- test: model-finding-z-cp-merge -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `cp-merge` of redteamZ/p3.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BtArray = Array with Bt

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Acc
	var bal as Num
	var hist as BtArray

	static function create(bal Num, hist isolated BtArray) returns Self
		return Self{bal: bal, hist: merge hist}
	end 'create'

	function entries() returns BtArray
		return self.hist
	end 'entries'
end 'Acc'

function mix(first Acc.handle, second Acc.handle) returns Num
	acquire first, second 'a1'
		let c = second.entries().clone() as isolated BtArray
		first.hist = merge c
	end 'a1' otherwise 's2'
		return 0
	end 's2'
	return 1
end 'mix'

function main() returns ExitCode
	let a = spawn Acc.create(1, hist: BtArray.create(0))
	let bV = spawn Acc.create(2, hist: BtArray.create(0))
	let _ = mix(a, second: bV)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-z-cp-record-slot-clone-inline -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `cp-record-slot-clone-inline` of redteamZ/p3.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Acc
	var bal as Num
	var r as Bt

	static function create(bal Num, r isolated Bt) returns Self
		return Self{bal: bal, r: merge r}
	end 'create'
end 'Acc'

function mix(first Acc.handle, second Acc.handle) returns Num
	acquire first, second 'a1'
		first.r = second.r.clone()
	end 'a1' otherwise 's2'
		return 0
	end 's2'
	return 1
end 'mix'

function main() returns ExitCode
	let a = spawn Acc.create(1, r: Bt.create(1))
	let bV = spawn Acc.create(2, r: Bt.create(2))
	let _ = mix(a, second: bV)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:23:3: cannot write 'r' through 'first', which is read-only while 'the isolated value' is open
```

<!-- test: model-finding-z-cp-record-slot-new -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `cp-record-slot-new` of redteamZ/p3.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Acc
	var bal as Num
	var r as Bt

	static function create(bal Num, r isolated Bt) returns Self
		return Self{bal: bal, r: merge r}
	end 'create'
end 'Acc'

function mix(first Acc.handle, second Acc.handle) returns Num
	acquire first, second 'a1'
		let v = second.r.b
		first.r = Bt.create(v)
	end 'a1' otherwise 's2'
		return 0
	end 's2'
	return 1
end 'mix'

function main() returns ExitCode
	let a = spawn Acc.create(1, r: Bt.create(1))
	let bV = spawn Acc.create(2, r: Bt.create(2))
	let _ = mix(a, second: bV)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-z-cp-record-slot-clone-two-lets -->
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3201. Model program `cp-record-slot-clone-two-lets` of redteamZ/p3.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Acc
	var bal as Num
	var r as Bt

	static function create(bal Num, r isolated Bt) returns Self
		return Self{bal: bal, r: merge r}
	end 'create'
end 'Acc'

function mix(first Acc.handle, second Acc.handle) returns Num
	acquire first, second 'a1'
		let c = second.r.clone()
		first.r = c
	end 'a1' otherwise 's2'
		return 0
	end 's2'
	return 1
end 'mix'

function main() returns ExitCode
	let a = spawn Acc.create(1, r: Bt.create(1))
	let bV = spawn Acc.create(2, r: Bt.create(2))
	let _ = mix(a, second: bV)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:24:13: 'c' is mutable outside 'first'; a store into an isolated region takes values, immutable or isolated
```

<!-- test: model-finding-z-cp-record-slot-clone-iso-two-lets -->
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `cp-record-slot-clone-iso-two-lets` of redteamZ/p3.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Acc
	var bal as Num
	var r as Bt

	static function create(bal Num, r isolated Bt) returns Self
		return Self{bal: bal, r: merge r}
	end 'create'
end 'Acc'

function mix(first Acc.handle, second Acc.handle) returns Num
	acquire first, second 'a1'
		let c = second.r.clone() as isolated Bt
		first.r = c
	end 'a1' otherwise 's2'
		return 0
	end 's2'
	return 1
end 'mix'

function main() returns ExitCode
	let a = spawn Acc.create(1, r: Bt.create(1))
	let bV = spawn Acc.create(2, r: Bt.create(2))
	let _ = mix(a, second: bV)
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:24:13: cannot store an isolated 'Bt' into 'r', which holds a mutable 'Bt'; `merge` it first
```

<!-- test: model-finding-z-cp-record-slot-clone-same-service-ctl -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `cp-record-slot-clone-same-service-ctl` of redteamZ/p3.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Acc
	var bal as Num
	var r as Bt
	var q as Bt

	static function create(bal Num, r isolated Bt, q isolated Bt) returns Self
		return Self{bal: bal, r: merge r, q: merge q}
	end 'create'
end 'Acc'

function mix(first Acc.handle) returns Num
	acquire first 'a1'
		first.q = first.r.clone()
	end 'a1' otherwise 's2'
		return 0
	end 's2'
	return 1
end 'mix'

function main() returns ExitCode
	let a = spawn Acc.create(1, r: Bt.create(1), q: Bt.create(2))
	let _ = mix(a)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-z-modify-iso-swap-then-read-other-state-view -->
The model (`core-m3w-fa14`) refuses it by `C-SLOT`; the compiler refuses it with E3199. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `modify-iso-swap-then-read-other-state-view` of redteamZ/p9.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var n as Num
	var i as isolated Bt

	static function create(n Num, i isolated Bt) returns Self
		return Self{n: n, i: i}
	end 'create'
end 'Sv'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(bV isolated Bt)
		let _ = bV.b
	end 'take'
end 'Sk'

function g(s Sv.handle, t Sv.handle, k Sk.handle) returns Num
	acquire s, t 'a1'
		modify s.i 'm3'
			let o = swap s.i with Bt.create(1)
			try k.take(o) otherwise ignore
			let _ = t.i.b
		end 'm3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'g'

function main() returns ExitCode
	let s = spawn Sv.create(0, i: Bt.create(5))
	let k = spawn Sk.create(0)
	let _ = g(s, t: s, k: k)
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:36:12: 's.i' cannot be swapped: the value that holds it is read-only while 'm3' is open
```

<!-- test: model-finding-z-modify-iso-read-view-first-then-swap-other -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3199. Model program `modify-iso-read-view-first-then-swap-other` of redteamZ/p9.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var n as Num
	var i as isolated Bt

	static function create(n Num, i isolated Bt) returns Self
		return Self{n: n, i: i}
	end 'create'
end 'Sv'

type Sk
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(bV isolated Bt)
		let _ = bV.b
	end 'take'
end 'Sk'

function g(s Sv.handle, t Sv.handle, k Sk.handle) returns Num
	acquire s, t 'a1'
		modify t.i 'm3'
			let o = swap s.i with Bt.create(1)
			try k.take(o) otherwise ignore
			t.i.b = 3
		end 'm3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'g'

function main() returns ExitCode
	let s = spawn Sv.create(0, i: Bt.create(5))
	let k = spawn Sk.create(0)
	let _ = g(s, t: s, k: k)
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:36:12: 's.i' cannot be swapped: the value that holds it is read-only while 'the isolated value' is open
```

<!-- test: model-finding-aa-send-state-swap-tk-local -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3198. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `send-state-swap-tk-local` of redteamAA/p10.txt, in capmodel `reggio/REPORTRTAA.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Rc
	var t as Tk

	static function create(t isolated Tk) returns Self
		return Self{t: merge t}
	end 'create'

	export function put(tV Tk)
		self.t = tV
	end 'put'
end 'Rc'

type Sv
	var t as Tk
	var k as Num

	static function create(t isolated Tk, k Num) returns Self
		return Self{t: merge t, k: k}
	end 'create'
end 'Sv'

function g(r Rc.handle, s Sv.handle) returns Num
	acquire s 'a1'
		let o = swap s.t with Tk.create(5)
		try r.put(o) otherwise ignore
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'g'

function main() returns ExitCode
	let r = spawn Rc.create(Tk.create(0))
	let s = spawn Sv.create(Tk.create(1), k: 0)
	let _ = g(r, s: s)
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:35:11: 's.t' is not an isolated field, so there is no region for `swap` to take out
```

<!-- test: model-finding-aa-send-state-swap-iso -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `send-state-swap-iso` of redteamAA/p10.txt, in capmodel `reggio/REPORTRTAA.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Rc
	var t as isolated Bt

	static function create(t isolated Bt) returns Self
		return Self{t: t}
	end 'create'

	export function put(tV isolated Bt)
		self.t = tV
	end 'put'
end 'Rc'

type Sv
	var t as isolated Bt
	var k as Num

	static function create(t isolated Bt, k Num) returns Self
		return Self{t: t, k: k}
	end 'create'
end 'Sv'

function g(r Rc.handle, s Sv.handle) returns Num
	acquire s 'a1'
		try r.put(swap s.t with Bt.create(5)) otherwise ignore
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'g'

function main() returns ExitCode
	let r = spawn Rc.create(Bt.create(0))
	let s = spawn Sv.create(Bt.create(1), k: 0)
	let _ = g(r, s: s)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-aa-take-tk-method-bind -->
The model (`core-m3w-fa14`) refuses it by `C-ONECALL`; the compiler refuses it with E3198, E3202. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `take-tk-method-bind` of redteamAA/p11.txt, in capmodel `reggio/REPORTRTAA.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Sv
	var t as Tk
	var k as Num

	static function create(t isolated Tk, k Num) returns Self
		return Self{t: merge t, k: k}
	end 'create'

	function takeTk() returns Tk
		return swap self.t with Tk.create(0)
	end 'takeTk'

	function putTk(x Tk)
		self.t = x
	end 'putTk'
end 'Sv'

function g(s Sv.handle, u Sv.handle) returns Num
	acquire s, u 'a1'
		let o = s.takeTk()
		u.putTk(o)
	end 'a1' otherwise 's2'
		return 0
	end 's2'
	return 0
end 'g'

function main() returns ExitCode
	let s = spawn Sv.create(Tk.create(1), k: 0)
	let u = spawn Sv.create(Tk.create(2), k: 0)
	let _ = g(s, u: u)
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:21:10: 'self.t' is not an isolated field, so there is no region for `swap` to take out
error E3202: <fragment>:31:11: 's.takeTk()' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-aa-take-tk-method-inline -->
The model (`core-m3w-fa14`) refuses it by `C-ONECALL`; the compiler refuses it with E3198, E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `take-tk-method-inline` of redteamAA/p11.txt, in capmodel `reggio/REPORTRTAA.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Sv
	var t as Tk
	var k as Num

	static function create(t isolated Tk, k Num) returns Self
		return Self{t: merge t, k: k}
	end 'create'

	function takeTk() returns Tk
		return swap self.t with Tk.create(0)
	end 'takeTk'

	function putTk(x Tk)
		self.t = x
	end 'putTk'
end 'Sv'

function g(s Sv.handle, u Sv.handle) returns Num
	acquire s, u 'a1'
		u.putTk(s.takeTk())
	end 'a1' otherwise 's2'
		return 0
	end 's2'
	return 0
end 'g'

function main() returns ExitCode
	let s = spawn Sv.create(Tk.create(1), k: 0)
	let u = spawn Sv.create(Tk.create(2), k: 0)
	let _ = g(s, u: u)
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:21:10: 'self.t' is not an isolated field, so there is no region for `swap` to take out
error E3201: <fragment>:31:11: 's.takeTk()' is mutable outside 'u'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-finding-aa-take-tk-method-same-service -->
The model (`core-m3w-fa14`) refuses it by `C-ONECALL`; the compiler refuses it with E3198, E3202. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `take-tk-method-same-service` of redteamAA/p11.txt, in capmodel `reggio/REPORTRTAA.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Sv
	var t as Tk
	var k as Num

	static function create(t isolated Tk, k Num) returns Self
		return Self{t: merge t, k: k}
	end 'create'

	function takeTk() returns Tk
		return swap self.t with Tk.create(0)
	end 'takeTk'

	function putTk(x Tk)
		self.t = x
	end 'putTk'
end 'Sv'

function g(s Sv.handle, u Sv.handle) returns Num
	acquire s, u 'a1'
		let o = s.takeTk()
		u.putTk(o)
	end 'a1' otherwise 's2'
		return 0
	end 's2'
	return 0
end 'g'

function main() returns ExitCode
	let s = spawn Sv.create(Tk.create(1), k: 0)
	let _ = g(s, u: s)
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:21:10: 'self.t' is not an isolated field, so there is no region for `swap` to take out
error E3202: <fragment>:31:11: 's.takeTk()' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-aa-acq-three-same -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `acq-three-same` of redteamAA/p8.txt, in capmodel `reggio/REPORTRTAA.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var k as Num
	var r as Bt

	static function create(k Num, r isolated Bt) returns Self
		return Self{k: k, r: merge r}
	end 'create'
end 'Sv'

function main() returns ExitCode
	let s = spawn Sv.create(0, r: Bt.create(1))
	acquire s, s, s 'a1'
		s.k = 1
		s.r.b = 2
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-aa-acq-in-main-shadow -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `acq-in-main-shadow` of redteamAA/p8.txt, in capmodel `reggio/REPORTRTAA.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var k as Num
	var r as Bt

	static function create(k Num, r isolated Bt) returns Self
		return Self{k: k, r: merge r}
	end 'create'
end 'Sv'

function main() returns ExitCode
	let s = spawn Sv.create(0, r: Bt.create(1))
	let t = s
	acquire s, t 'a1'
		let m = s.r.b
		t.k = m
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-aa-acq-var-handle-reassigned -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `acq-var-handle-reassigned` of redteamAA/p8.txt, in capmodel `reggio/REPORTRTAA.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var k as Num
	var r as Bt

	static function create(k Num, r isolated Bt) returns Self
		return Self{k: k, r: merge r}
	end 'create'
end 'Sv'

function main() returns ExitCode
	var s = spawn Sv.create(0, r: Bt.create(1))
	let u = spawn Sv.create(1, r: Bt.create(2))
	acquire s 'a1'
		s.k = 1
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	s = u
	acquire s 'a3'
		s.k = 2
	end 'a3' otherwise 's4'
		let _ = 0 as Num
	end 's4'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-aa-acq-outer-handle-use-inside -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `acq-outer-handle-use-inside` of redteamAA/p8.txt, in capmodel `reggio/REPORTRTAA.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Sv
	var k as Num
	var r as Bt

	static function create(k Num, r isolated Bt) returns Self
		return Self{k: k, r: merge r}
	end 'create'

	export function poke()
		self.k = 1
	end 'poke'
end 'Sv'

function g(s Sv.handle) returns Num
	let t = s
	acquire s 'a1'
		try t.poke() otherwise ignore
		s.k = 2
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'g'

function main() returns ExitCode
	let s = spawn Sv.create(0, r: Bt.create(1))
	let _ = g(s)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-aa-send-state-slot-read -->
The model (`core-m3w-fa14`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `send-state-slot-read` of redteamAA/p9.txt, in capmodel `reggio/REPORTRTAA.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Rc
	var t as Tk

	static function create(t isolated Tk) returns Self
		return Self{t: merge t}
	end 'create'

	export function put(tV Tk)
		self.t = tV
	end 'put'
end 'Rc'

type Sv
	var t as Tk
	var k as Num

	static function create(t isolated Tk, k Num) returns Self
		return Self{t: merge t, k: k}
	end 'create'
end 'Sv'

function g(r Rc.handle, s Sv.handle) returns Num
	acquire s 'a1'
		try r.put(s.t) otherwise ignore
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'g'

function main() returns ExitCode
	let r = spawn Rc.create(Tk.create(0))
	let s = spawn Sv.create(Tk.create(1), k: 0)
	let _ = g(r, s: s)
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:35:13: cannot hand 's.t' to argument `tV` of the message `Rc.put`: it keeps a unique value, and a unique value cannot be moved out of a field or an element it was only read from; move it out with `pop` or `remove`, or `swap` it out of an isolated field
```

<!-- test: model-finding-aa-send-state-swap -->
The model (`core-m3w-fa14`) refuses it by `C-RECOVER`; the compiler refuses it with E3198. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `send-state-swap` of redteamAA/p9.txt, in capmodel `reggio/REPORTRTAA.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Rc
	var t as Tk

	static function create(t isolated Tk) returns Self
		return Self{t: merge t}
	end 'create'

	export function put(tV Tk)
		self.t = tV
	end 'put'
end 'Rc'

type Sv
	var t as Tk
	var k as Num

	static function create(t isolated Tk, k Num) returns Self
		return Self{t: merge t, k: k}
	end 'create'
end 'Sv'

function g(r Rc.handle, s Sv.handle) returns Num
	acquire s 'a1'
		try r.put(swap s.t with Tk.create(5)) otherwise ignore
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'g'

function main() returns ExitCode
	let r = spawn Rc.create(Tk.create(0))
	let s = spawn Sv.create(Tk.create(1), k: 0)
	let _ = g(r, s: s)
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:35:13: 's.t' is not an isolated field, so there is no region for `swap` to take out
```

<!-- test: model-finding-ad-acq-state-method-discard -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3202. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `acq-state-method-discard` of redteamAD/t11.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

type Ck
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mk() returns Rl
		return Rl.create(0)
	end 'mk'
end 'Ck'

function main() returns ExitCode
	let a = spawn Ck.create(0)
	acquire a 'a1'
		let _ = a.mk()
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:32:11: 'a.mk()' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-finding-ad-acq-state-method-fin-direct -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `acq-state-method-fin-direct` of redteamAD/t11.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

type Ck
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mk() returns Rl
		return Rl.create(0)
	end 'mk'
end 'Ck'

function main() returns ExitCode
	let a = spawn Ck.create(0)
	acquire a 'a1'
		a.mk().fin(1)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ad-acq-state-method-fin-direct-twice -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `acq-state-method-fin-direct-twice` of redteamAD/t11.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

type Ck
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mk() returns Rl
		return Rl.create(0)
	end 'mk'
end 'Ck'

function main() returns ExitCode
	let a = spawn Ck.create(0)
	acquire a 'a1'
		a.mk().fin(1)
		a.mk().fin(2)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-ad-acq-state-method-pass-through -->
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `acq-state-method-pass-through` of redteamAD/t11.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

type Ck
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function pass(r Rl) returns Rl
		return r
	end 'pass'
end 'Ck'

function main() returns ExitCode
	let a = spawn Ck.create(0)
	acquire a 'a1'
		let r = Rl.create(0)
		let q = a.pass(r)
		q.fin(1)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:33:18: 'r' is mutable outside 'a'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-finding-ad-acq-state-method-pass-through-iso -->
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `acq-state-method-pass-through-iso` of redteamAD/t11.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

type Ck
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function pass(r Rl) returns Rl
		return r
	end 'pass'
end 'Ck'

function main() returns ExitCode
	let a = spawn Ck.create(0)
	acquire a 'a1'
		let r = Rl.create(0) as isolated Rl
		let q = a.pass(r)
		q.fin(1)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:33:18: one statement reaches through two isolated values, 'a' and 'r'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-finding-ad-acq-state-method-pass-through-iso-then -->
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `acq-state-method-pass-through-iso-then` of redteamAD/t11.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

type Ck
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function pass(r Rl) returns Rl
		return r
	end 'pass'
end 'Ck'

function main() returns ExitCode
	let a = spawn Ck.create(0)
	acquire a 'a1'
		let r = Rl.create(0) as isolated Rl
		let q = a.pass(r)
		q.fin(1)
		r.fin(2)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:33:18: one statement reaches through two isolated values, 'a' and 'r'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-finding-ad-acq-two-states-linear -->
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `acq-two-states-linear` of redteamAD/t11.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

type Ck
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(r Rl)
		r.fin(1)
	end 'eat'
end 'Ck'

function main() returns ExitCode
	let a = spawn Ck.create(0)
	let b = spawn Ck.create(1)
	acquire a, b 'a1'
		let r = Rl.create(0) as isolated Rl
		a.eat(r)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:34:9: one statement reaches through two isolated values, 'a' and 'r'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-finding-ad-acq-two-states-linear-two -->
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `acq-two-states-linear-two` of redteamAD/t11.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

type Ck
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(r Rl)
		r.fin(1)
	end 'eat'
end 'Ck'

function main() returns ExitCode
	let a = spawn Ck.create(0)
	let b = spawn Ck.create(1)
	acquire a, b 'a1'
		let r = Rl.create(0) as isolated Rl
		let s = Rl.create(1) as isolated Rl
		a.eat(r)
		b.eat(s)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:35:9: one statement reaches through two isolated values, 'a' and 'r'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-finding-ad-acq-two-states-same-twice -->
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `acq-two-states-same-twice` of redteamAD/t11.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

type Ck
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(r Rl)
		r.fin(1)
	end 'eat'
end 'Ck'

function main() returns ExitCode
	let a = spawn Ck.create(0)
	acquire a, a 'a1'
		let r = Rl.create(0) as isolated Rl
		a.eat(r)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:33:9: one statement reaches through two isolated values, 'a' and 'r'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-finding-ad-acq-two-states-same-twice-eat-twice -->
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `acq-two-states-same-twice-eat-twice` of redteamAD/t11.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

type Ck
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(r Rl)
		r.fin(1)
	end 'eat'
end 'Ck'

function main() returns ExitCode
	let a = spawn Ck.create(0)
	acquire a, a 'a1'
		let r = Rl.create(0) as isolated Rl
		a.eat(r)
		a.eat(r)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:33:9: one statement reaches through two isolated values, 'a' and 'r'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-finding-ad-recv-wrap-in-acquire-modify -->
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `recv-wrap-in-acquire-modify` of redteamAD/t12.txt, in capmodel `reggio/REPORTRTAD.txt`.

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

type S
	var k as Num
	var b as isolated Bx

	static function create(k Num, b isolated Bx) returns Self
		return Self{k: k, b: b}
	end 'create'
end 'S'

function useS(s Sn)
	match s 'x1'
		ok then break
		notSent(r) then r.fin(1)
	end 'x1'
end 'useS'

function main() returns ExitCode
	let s = spawn S.create(0, b: Bx.create(0))
	acquire s 'a1'
		let v = Rl.create(0) as isolated Rl
		modify s.b 'm3'
			useS(v.wrap())
		end 'm3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:55:9: an isolated value from outside 'm3' cannot be moved into the parameter 'self' of 'Rl.wrap', which keeps it while 'm3' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-finding-ad-l-state-method-acquire-window -->
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `l-state-method-acquire-window` of redteamAD/t6.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
	acquire s 'a1'
		let v = Rl.create(0) as isolated Rl
		s.go(v)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:49:8: one statement reaches through two isolated values, 's' and 'v'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-finding-ad-l-state-method-acquire-window-modify -->
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `l-state-method-acquire-window-modify` of redteamAD/t6.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
	acquire s 'a1'
		let v = Rl.create(0) as isolated Rl
		modify s.b 'm3'
			useS(giveS(v))
		end 'm3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:55:15: an isolated value from outside 'm3' cannot be moved into the parameter 'r' of 'giveS', which keeps it while 'm3' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-finding-ae-outer-plain-union-match-in-acquire -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-WHERE`; the compiler refuses it with E3207, E3224. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `outer-plain-union-match-in-acquire` of redteamAE/p13.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
	var n as Num
	var keep as Sn

	static function create(n Num, keep isolated Sn) returns Self
		return Self{n: n, keep: merge keep}
	end 'create'

	function hold(s Sn)
		let _ = s
		self.n = 1
	end 'hold'
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
	let s = spawn S.create(0, keep: Sn.ok)
	let u = Sn.notSent(Rl.create(1))
	acquire s 'a1'
		match u 'x3'
			ok then break
			notSent(q) then useS(giveS(q))
		end 'x3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:24:6: the field 'keep' holds a linear value, and a linear value lives only in locals, parameters and message arguments: a field would keep it past the code that must consume it
error E3207: <fragment>:53:31: 'giveS' needs a writable parameter 'r' at line 37; 'q' is read-only while 'a1' is open
```

<!-- test: model-finding-ae-w-match-consume-acquire -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3207. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w-match-consume-acquire` of redteamAE/p14.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function bump()
		self.n = 1
	end 'bump'
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
	let u = Sn.notSent(Rl.create(1))
	acquire s 'a1'
		match u 'x3'
			ok then break
			notSent(q) then useS(giveS(q))
		end 'x3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:51:31: 'giveS' needs a writable parameter 'r' at line 35; 'q' is read-only while 'a1' is open
```

<!-- test: model-finding-ae-state-union-iso-arm-write -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `state-union-iso-arm-write` of redteamAE/p16.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

type S
	var n as Num
	var u as Ub
	var c as Cnt

	static function create(n Num, u isolated Ub, c isolated Cnt) returns Self
		return Self{n: n, u: merge u, c: merge c}
	end 'create'

	function go()
		match self.u 'x1'
			u0 then break
			u1 then self.c.k = 3
		end 'x1'
	end 'go'
end 'S'

function main() returns ExitCode
	let s = spawn S.create(0, u: Ub.u1(Bx.create(0)), c: Cnt.create(0))
	acquire s 'a1'
		s.go()
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:37:12: cannot write 'k' through 'self.c', which is read-only while 'x1' is open
```

<!-- test: model-finding-ae-acquire-union-iso-arm-write -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `acquire-union-iso-arm-write` of redteamAE/p16.txt, in capmodel `reggio/REPORTRTAE.txt`.

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

type S
	var n as Num
	var u as Ub
	var c as Cnt

	static function create(n Num, u isolated Ub, c isolated Cnt) returns Self
		return Self{n: n, u: merge u, c: merge c}
	end 'create'
end 'S'

function main() returns ExitCode
	let s = spawn S.create(0, u: Ub.u1(Bx.create(0)), c: Cnt.create(0))
	acquire s 'a1'
		modify s 'm3'
			match s.u 'x4'
				u0 then break
				u1 then s.c.k = 3
			end 'x4'
		end 'm3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:41:13: cannot write 'k' through 's.c', which is read-only while 'x4' is open
```

<!-- test: model-hand-ab-w-acq-moves-outer -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `w-acq-moves-outer` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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

type Kp
	var b as isolated Box
	var n as Num

	static function create(b isolated Box, n Num) returns Self
		return Self{b: b, n: n}
	end 'create'

	function open1(r Resp)
		r.fin(1)
	end 'open1'

	export function touch()
		self.n = 1
	end 'touch'

	export function take(r Resp)
		r.fin(1)
	end 'take'
end 'Kp'

function main() returns ExitCode
	let a = spawn Kp.create(Box.create(0), n: 0)
	let r = Resp.create(0) as isolated Resp
	acquire a 'a1'
		r.fin(1)
	end 'a1' otherwise 's2'
		r.fin(2)
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:53:2: the linear value 'r' is live across this wait: a wait may never end, so a linear value is consumed before any call that waits, `await` or send that waits
```

<!-- test: model-hand-ab-w-acq-two-values -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `w-acq-two-values` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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

type Kp
	var b as isolated Box
	var n as Num

	static function create(b isolated Box, n Num) returns Self
		return Self{b: b, n: n}
	end 'create'

	function open1(r Resp)
		r.fin(1)
	end 'open1'

	export function touch()
		self.n = 1
	end 'touch'

	export function take(r Resp)
		r.fin(1)
	end 'take'
end 'Kp'

function main() returns ExitCode
	let a = spawn Kp.create(Box.create(0), n: 0)
	let r = Resp.create(0) as isolated Resp
	acquire a 'a1'
		let _ = 0 as Num
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:53:2: the linear value 'r' is live across this wait: a wait may never end, so a linear value is consumed before any call that waits, `await` or send that waits
```

<!-- test: model-hand-ab-w-acq-return-in-body -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `w-acq-return-in-body` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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

type Kp
	var b as isolated Box
	var n as Num

	static function create(b isolated Box, n Num) returns Self
		return Self{b: b, n: n}
	end 'create'

	function open1(r Resp)
		r.fin(1)
	end 'open1'

	export function touch()
		self.n = 1
	end 'touch'

	export function take(r Resp)
		r.fin(1)
	end 'take'
end 'Kp'

function f()
	let a = spawn Kp.create(Box.create(0), n: 0)
	acquire a 'a1'
		let _ = Resp.create(0)
		return
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'f'

function main() returns ExitCode
	f()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:54:3: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ab-w-acq-return-after-consume -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-acq-return-after-consume` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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

type Kp
	var b as isolated Box
	var n as Num

	static function create(b isolated Box, n Num) returns Self
		return Self{b: b, n: n}
	end 'create'

	function open1(r Resp)
		r.fin(1)
	end 'open1'

	export function touch()
		self.n = 1
	end 'touch'

	export function take(r Resp)
		r.fin(1)
	end 'take'
end 'Kp'

function f()
	let a = spawn Kp.create(Box.create(0), n: 0)
	acquire a 'a1'
		let r = Resp.create(0)
		r.fin(1)
		return
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
end 'f'

function main() returns ExitCode
	f()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-w-acq-body-consumes-outer-refill -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `w-acq-body-consumes-outer-refill` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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

type Kp
	var b as isolated Box
	var n as Num

	static function create(b isolated Box, n Num) returns Self
		return Self{b: b, n: n}
	end 'create'

	function open1(r Resp)
		r.fin(1)
	end 'open1'

	export function touch()
		self.n = 1
	end 'touch'

	export function take(r Resp)
		r.fin(1)
	end 'take'
end 'Kp'

function main() returns ExitCode
	let a = spawn Kp.create(Box.create(0), n: 0)
	var r = Resp.create(0) as isolated Resp
	acquire a 'a1'
		r.fin(1)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	r = Resp.create(1)
	r.fin(2)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:53:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
error E3223: <fragment>:53:2: the linear value 'r' is live across this wait: a wait may never end, so a linear value is consumed before any call that waits, `await` or send that waits
```

<!-- test: model-hand-ab-w-acq-handback-send-in-block -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-acq-handback-send-in-block` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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

type Kp
	var b as isolated Box
	var n as Num

	static function create(b isolated Box, n Num) returns Self
		return Self{b: b, n: n}
	end 'create'

	function open1(r Resp)
		r.fin(1)
	end 'open1'

	export function touch()
		self.n = 1
	end 'touch'

	export function take(r Resp)
		r.fin(1)
	end 'take'
end 'Kp'

function main() returns ExitCode
	let a = spawn(capacity: 1) Kp.create(Box.create(0), n: 0)
	let bV = spawn(capacity: 1) Kp.create(Box.create(0), n: 0)
	acquire a 'a1'
		let r = Resp.create(0) as isolated Resp
		try bV.take(r) otherwise (sendFailure) 'h3'
			match sendFailure 'x4'
				mailboxFull(q) then q.fin(1)
				serviceStopped(q) then q.fin(1)
			end 'x4'
		end 'h3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-w-acq-state-method-open1 -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w-acq-state-method-open1` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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

type Kp
	var b as isolated Box
	var n as Num

	static function create(b isolated Box, n Num) returns Self
		return Self{b: b, n: n}
	end 'create'

	function open1(r Resp)
		r.fin(1)
	end 'open1'

	export function touch()
		self.n = 1
	end 'touch'

	export function take(r Resp)
		r.fin(1)
	end 'take'
end 'Kp'

function main() returns ExitCode
	let a = spawn Kp.create(Box.create(0), n: 0)
	let r = Resp.create(0) as isolated Resp
	acquire a 'a1'
		a.open1(r)
	end 'a1' otherwise 's2'
		r.fin(2)
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:54:11: one statement reaches through two isolated values, 'a' and 'r'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-hand-ab-w-acq-state-method-open1-both -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w-acq-state-method-open1-both` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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

type Kp
	var b as isolated Box
	var n as Num

	static function create(b isolated Box, n Num) returns Self
		return Self{b: b, n: n}
	end 'create'

	function open1(r Resp)
		r.fin(1)
	end 'open1'

	export function touch()
		self.n = 1
	end 'touch'

	export function take(r Resp)
		r.fin(1)
	end 'take'
end 'Kp'

function main() returns ExitCode
	let a = spawn Kp.create(Box.create(0), n: 0)
	let r = Resp.create(0) as isolated Resp
	acquire a 'a1'
		a.open1(r)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:54:11: one statement reaches through two isolated values, 'a' and 'r'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-hand-ab-w-acq-two-names-one-service -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `w-acq-two-names-one-service` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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

type Kp
	var b as isolated Box
	var n as Num

	static function create(b isolated Box, n Num) returns Self
		return Self{b: b, n: n}
	end 'create'

	function open1(r Resp)
		r.fin(1)
	end 'open1'

	export function touch()
		self.n = 1
	end 'touch'

	export function take(r Resp)
		r.fin(1)
	end 'take'
end 'Kp'

function main() returns ExitCode
	let a = spawn Kp.create(Box.create(0), n: 0)
	let r = Resp.create(0)
	acquire a, a 'a1'
		r.fin(1)
	end 'a1' otherwise 's2'
		r.fin(2)
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:53:2: the linear value 'r' is live across this wait: a wait may never end, so a linear value is consumed before any call that waits, `await` or send that waits
```

<!-- test: model-hand-ab-w-acq-nested -->
The model (`core-m3w-fa14`) refuses it by `R13-NESTED`; the compiler refuses it with E3205. Model program `w-acq-nested` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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

type Kp
	var b as isolated Box
	var n as Num

	static function create(b isolated Box, n Num) returns Self
		return Self{b: b, n: n}
	end 'create'

	function open1(r Resp)
		r.fin(1)
	end 'open1'

	export function touch()
		self.n = 1
	end 'touch'

	export function take(r Resp)
		r.fin(1)
	end 'take'
end 'Kp'

function main() returns ExitCode
	let a = spawn Kp.create(Box.create(0), n: 0)
	let bV = spawn Kp.create(Box.create(0), n: 0)
	acquire a 'a1'
		let r = Resp.create(0)
		acquire bV 'a3'
			r.fin(1)
		end 'a3' otherwise 's4'
			r.fin(2)
		end 's4'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:55:3: cannot wait inside 'a1'; an acquire block holds its services and may not wait or acquire again. Await before or after the block
```

<!-- test: model-hand-ab-w-acq-loop-consume -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-acq-loop-consume` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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

type Kp
	var b as isolated Box
	var n as Num

	static function create(b isolated Box, n Num) returns Self
		return Self{b: b, n: n}
	end 'create'

	function open1(r Resp)
		r.fin(1)
	end 'open1'

	export function touch()
		self.n = 1
	end 'touch'

	export function take(r Resp)
		r.fin(1)
	end 'take'
end 'Kp'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let a = spawn Kp.create(Box.create(0), n: 0)
	var i = 2 as Num
	while i != 0 'w1'
		acquire a 'a2'
			let r = Resp.create(0)
			r.fin(1)
		end 'a2' otherwise 's3'
			let _ = 0 as Num
		end 's3'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-w-acq-yield -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `w-acq-yield` of redteamAB.hand_ab3, in capmodel `reggio/REPORTRTAB.txt`.

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

type Kp
	var b as isolated Box
	var n as Num

	static function create(b isolated Box, n Num) returns Self
		return Self{b: b, n: n}
	end 'create'

	function open1(r Resp)
		r.fin(1)
	end 'open1'

	export function touch()
		self.n = 1
	end 'touch'

	export function take(r Resp)
		r.fin(1)
	end 'take'
end 'Kp'

function main() returns ExitCode
	let a = spawn Kp.create(Box.create(0), n: 0)
	let r = Resp.create(0)
	var i = 1 as Num
	while i != 0 'w1'
		acquire a 'a2'
			let _ = 0 as Num
		end 'a2' otherwise 's3'
			let _ = 0 as Num
		end 's3'
		i = 0
	end 'w1'
	r.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:55:3: the linear value 'r' is live across this wait: a wait may never end, so a linear value is consumed before any call that waits, `await` or send that waits
```

<!-- test: model-hand-ab-s-state-method-mk-drop -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3202. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `s-state-method-mk-drop` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

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
	acquire a 'a1'
		let _ = a.mk()
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:84:11: 'a.mk()' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-hand-ab-s-acquire-state-mk -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `s-acquire-state-mk` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

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
	acquire a 'a1'
		let r = a.mk()
		r.fin(1)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:84:11: 'a.mk()' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-hand-ab-s-acquire-state-eat-fresh -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `s-acquire-state-eat-fresh` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

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
	acquire a 'a1'
		a.eat(Resp.create(0))
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ab-s-acquire-state-mk-leaves -->
The model (`core-m3w-fa14`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `s-acquire-state-mk-leaves` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

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
	var keep = Resp.create(9)
	keep.fin(0)
	acquire a 'a1'
		keep = a.mk()
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	keep.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:86:3: 'keep' holds a record from outside 'a1' and cannot be reassigned while 'a1' is open
```

<!-- test: model-hand-ab-s-acquire-state-mk-leaves-drop -->
The model (`core-m3w-fa14`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `s-acquire-state-mk-leaves-drop` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

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
	var keep = Resp.create(9)
	keep.fin(0)
	acquire a 'a1'
		keep = a.mk()
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:86:3: 'keep' holds a record from outside 'a1' and cannot be reassigned while 'a1' is open
```

<!-- test: model-hand-ab-s-acquire-state-mk-in-otherwise -->
The model (`core-m3w-fa14`) refuses it by `C-OUTER`; the compiler refuses it with E3204. Model program `s-acquire-state-mk-in-otherwise` of redteamAB.hand_ab5, in capmodel `reggio/REPORTRTAB.txt`.

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
	var keep = Resp.create(9)
	keep.fin(0)
	acquire a 'a1'
		keep = a.mk()
	end 'a1' otherwise 's2'
		keep = Resp.create(7)
	end 's2'
	keep.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:86:3: 'keep' holds a record from outside 'a1' and cannot be reassigned while 'a1' is open
```

<!-- test: model-hand-ac-hb-send-in-acquire-body -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hb-send-in-acquire-body` of redteamAC.hand_ac1, in capmodel `reggio/REPORTRTAC.txt`.

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
end 'Kq'

function main() returns ExitCode
	let a = spawn(capacity: 1) Hq.create(0)
	let k = spawn Kq.create(0)
	acquire k 'a1'
		try a.take(Resp.create(0)) otherwise (sendFailure) 'h3'
			match sendFailure 'x4'
				mailboxFull(q) then q.fin(1)
				serviceStopped(q) then q.fin(1)
			end 'x4'
		end 'h3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-li-state-method -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `li-state-method` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

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

type Kp
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function open1(r Resp)
		r.fin(1)
	end 'open1'
end 'Kp'

function main() returns ExitCode
	let a = spawn Kp.create(0)
	let r = Resp.create(0) as isolated Resp
	acquire a 'a1'
		a.open1(r)
		r.fin(2)
	end 'a1' otherwise 's2'
		r.fin(3)
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:37:11: one statement reaches through two isolated values, 'a' and 'r'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-hand-ac-li-state-method-twice -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `li-state-method-twice` of redteamAC.hand_ac2, in capmodel `reggio/REPORTRTAC.txt`.

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

type Kp
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function open1(r Resp)
		r.fin(1)
	end 'open1'
end 'Kp'

function main() returns ExitCode
	let a = spawn Kp.create(0)
	let r = Resp.create(0) as isolated Resp
	acquire a 'a1'
		a.open1(r)
		a.open1(r)
	end 'a1' otherwise 's2'
		r.fin(3)
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:37:11: one statement reaches through two isolated values, 'a' and 'r'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-hand-ac-min-h1-acquire -->
The model (`core-m3w-fa14`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `min-h1-acquire` of redteamAC.hand_ac20, in capmodel `reggio/REPORTRTAC.txt`.

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
end 'Kq'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let k = spawn Kq.create(0)
	acquire k 'a1'
		let r = Resp.create(0) as isolated Resp
		useR(r)
		r.fin(2)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:38:3: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ac-ld-acquire-lend -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `ld-acquire-lend` of redteamAC.hand_ac3, in capmodel `reggio/REPORTRTAC.txt`.

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
end 'Kq'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let k = spawn Kq.create(0)
	let r = Resp.create(0) as isolated Resp
	acquire k 'a1'
		useR(r)
	end 'a1' otherwise 's2'
		r.fin(1)
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:37:8: an isolated value from outside 'a1' cannot be moved into the parameter 'r' of 'useR', which keeps it while 'a1' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: model-hand-ac-x-acquire-same-two -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `x-acquire-same-two` of redteamAC.hand_ac4, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Sa'

function main() returns ExitCode
	let a = spawn Sa.create(0)
	acquire a, a 'a1'
		let _ = 0 as Num
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-sm-acquire-local-iso -->
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3201. Model program `sm-acquire-local-iso` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

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

	function eatM(r Resp)
		r.fin(1)
	end 'eatM'
end 'Kq'

function main() returns ExitCode
	let k = spawn Kq.create(0)
	acquire k 'a1'
		let r = Resp.create(0) as isolated Resp
		k.eatM(r)
		r.fin(2)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:37:10: one statement reaches through two isolated values, 'k' and 'r'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-hand-ac-sm-acquire-local-iso-fn -->
The model (`core-m3w-fa14`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `sm-acquire-local-iso-fn` of redteamAC.hand_ac6, in capmodel `reggio/REPORTRTAC.txt`.

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

	function eatM(r Resp)
		r.fin(1)
	end 'eatM'
end 'Kq'

function useR(r Resp)
	r.fin(1)
end 'useR'

function main() returns ExitCode
	let k = spawn Kq.create(0)
	acquire k 'a1'
		let r = Resp.create(0) as isolated Resp
		useR(r)
		r.fin(2)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:42:3: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-a-acquire-consume-body -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `a-acquire-consume-body` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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
	let a = spawn Auth.create(0)
	let r = Resp.create(0)
	acquire a 'a1'
		r.fin(1)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:48:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
error E3223: <fragment>:48:2: the linear value 'r' is live across this wait: a wait may never end, so a linear value is consumed before any call that waits, `await` or send that waits
```

<!-- test: model-hand-ad-a-acquire-consume-both -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `a-acquire-consume-both` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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
	let a = spawn Auth.create(0)
	let r = Resp.create(0)
	acquire a 'a1'
		r.fin(1)
	end 'a1' otherwise 's2'
		r.fin(2)
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:48:2: the linear value 'r' is live across this wait: a wait may never end, so a linear value is consumed before any call that waits, `await` or send that waits
```

<!-- test: model-hand-ad-a-acquire-consume-handler-only -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `a-acquire-consume-handler-only` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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
	let a = spawn Auth.create(0)
	let r = Resp.create(0)
	acquire a 'a1'
		let _ = 0 as Num
	end 'a1' otherwise 's2'
		r.fin(2)
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:48:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
error E3223: <fragment>:48:2: the linear value 'r' is live across this wait: a wait may never end, so a linear value is consumed before any call that waits, `await` or send that waits
```

<!-- test: model-hand-ad-a-acquire-return -->
<!-- unsupported-targets: wasm32-wasi -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3223. Model program `a-acquire-return` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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

function f(a Auth.handle)
	let r = Resp.create(0)
	acquire a 'a1'
		return
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	r.fin(1)
end 'f'

function main() returns ExitCode
	let a = spawn Auth.create(0)
	f(a)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:47:2: the linear value 'r' is live across this wait: a wait may never end, so a linear value is consumed before any call that waits, `await` or send that waits
error E3223: <fragment>:48:3: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: model-hand-ad-a-acquire-local-create -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a-acquire-local-create` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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
	let a = spawn Auth.create(0)
	acquire a 'a1'
		let r = Resp.create(0)
		r.fin(1)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-a-acquire-in-loop -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `a-acquire-in-loop` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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
	let a = spawn Auth.create(0)
	let r = Resp.create(0)
	var i = 2 as Num
	while i != 0 'w1'
		acquire a 'a2'
			r.fin(1)
		end 'a2' otherwise 's3'
			r.fin(1)
		end 's3'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:51:4: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ad-a-acquire-state-method-consume -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `a-acquire-state-method-consume` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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

type Ck
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(r Resp)
		r.fin(1)
	end 'eat'
end 'Ck'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let a = spawn Ck.create(0)
	let r = Resp.create(0)
	acquire a 'a1'
		a.eat(r)
	end 'a1' otherwise 's2'
		r.fin(1)
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:45:9: 'r' is mutable outside 'a'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ad-a-acquire-state-method-consume-then -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `a-acquire-state-method-consume-then` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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

type Ck
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(r Resp)
		r.fin(1)
	end 'eat'
end 'Ck'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let a = spawn Ck.create(0)
	let r = Resp.create(0)
	acquire a 'a1'
		a.eat(r)
		r.fin(2)
	end 'a1' otherwise 's2'
		r.fin(3)
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:45:9: 'r' is mutable outside 'a'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ad-a-acquire-state-method-result -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `a-acquire-state-method-result` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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

type Ck
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mk() returns Resp
		return Resp.create(0)
	end 'mk'
end 'Ck'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let a = spawn Ck.create(0)
	acquire a 'a1'
		let r = a.mk()
		r.fin(1)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:44:11: 'a.mk()' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-hand-ad-a-acquire-state-method-result-drop -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `a-acquire-state-method-result-drop` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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

type Ck
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mk() returns Resp
		return Resp.create(0)
	end 'mk'
end 'Ck'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let a = spawn Ck.create(0)
	acquire a 'a1'
		let _ = a.mk()
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:44:11: 'a.mk()' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-hand-ad-a-acquire-state-method-result-leak-out -->
The model (`core-m3w-fa14`) refuses it by `R-LIN-DROP`; the compiler refuses it with E3204. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `a-acquire-state-method-result-leak-out` of redteamAD.hand_ad1, in capmodel `reggio/REPORTRTAD.txt`.

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

type Ck
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mk() returns Resp
		return Resp.create(0)
	end 'mk'
end 'Ck'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let a = spawn Ck.create(0)
	var out = Resp.create(5)
	acquire a 'a1'
		out = a.mk()
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	out.fin(1)
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:45:3: 'out' holds a record from outside 'a1' and cannot be reassigned while 'a1' is open
```

<!-- test: model-hand-ad-k-swap-cell-twice-via-state -->
The model (`core-m3w-fa14`) refuses it by `C-HOLDER`; the compiler refuses it with E3198, E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `k-swap-cell-twice-via-state` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

type Hq
	var n as Num
	var t as Tok

	static function create(n Num, t isolated Tok) returns Self
		return Self{n: n, t: merge t}
	end 'create'

	function sw(x Tok)
		let _ = swap self.t with x
	end 'sw'
end 'Hq'

function main() returns ExitCode
	let h = spawn Hq.create(0, t: Tok.create(0))
	let tV = Tok.create(1) as isolated Tok
	acquire h 'a1'
		h.sw(tV)
		h.sw(tV)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:21:11: 'self.t' is not an isolated field, so there is no region for `swap` to take out
error E3201: <fragment>:29:8: one statement reaches through two isolated values, 'h' and 'tV'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: model-hand-ad-y-state-method-await-reply -->
The model (`core-m3w-fa14`) refuses it by `R2-BLOCKWAIT`; the compiler refuses it with E3205. Model program `y-state-method-await-reply` of redteamAD.hand_ad8, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Pl
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function mth() returns Num
		return self.n
	end 'mth'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Pl'

type Sm
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function y(p Pl.handle) returns Num
		let q = try p.plain() otherwise panic("send failed")
		let x = await q
		return x
	end 'y'
end 'Sm'

function main() returns ExitCode
	let s = spawn Sm.create(0)
	let pl = spawn Pl.create(0)
	acquire s 'a1'
		let _ = s.y(pl)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:38:13: cannot call 'y' inside 'a1'; it may wait (it awaits a reply from 'Pl' at line 29). Call it before or after the block
```

<!-- test: model-hand-ae-w-for-state-array -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `w-for-state-array` of redteamAE.hand_ae3, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type Ar
	var xs as NumArray
	var n as Num

	static function create(xs isolated NumArray, n Num) returns Self
		return Self{xs: merge xs, n: n}
	end 'create'

	function add(x Num)
		self.xs.push(x)
	end 'add'
end 'Ar'

function main() returns ExitCode
	let a = spawn Ar.create(NumArray.create(0), n: 0)
	acquire a 'a1'
		for _ in a.xs 'f3'
			a.add(1)
		end 'f3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ae-h-msg-acquire -->
The model (`core-m3w-fa14`) refuses it by `R13-NESTED`; the compiler refuses it with E3221. Model program `h-msg-acquire` of redteamAE.hand_ae3, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Q
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'

	export function ping() returns Num
		return 5
	end 'ping'

	export function poke()
		self.k = 1
	end 'poke'
end 'Q'

type Mw
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function go(o Q.handle) returns Num
		acquire o 'a1'
			let _ = 0 as Num
		end 'a1' otherwise 's2'
			let _ = 0 as Num
		end 's2'
		return 0
	end 'go'
end 'Mw'

function main() returns ExitCode
	let q = spawn Q.create(0)
	let m = spawn Mw.create(0)
	let r = try m.go(q.clone()) otherwise panic("send failed")
	let _ = await r
	return 0
end 'main'
```
```maxoncstderr
error E3221: <fragment>:27:18: the message 'go' may wait (it acquires services at line 28) while holding its own service
```

<!-- test: model-hand-af-p1-acquire-cross-state -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `p1-acquire-cross-state` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

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

type Ka
	var n as Num
	var k as Bx
	var l as Bx

	static function create(n Num, k isolated Bx, l isolated Bx) returns Self
		return Self{n: n, k: merge k, l: merge l}
	end 'create'

	function setK(b Bx)
		self.k = b
	end 'setK'

	function touch(b Bx)
		b.n = 9
	end 'touch'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Ka'

function main() returns ExitCode
	let a = spawn Ka.create(0, k: Bx.create(0), l: Bx.create(1))
	let b = spawn Ka.create(0, k: Bx.create(2), l: Bx.create(3))
	acquire a, b 'a1'
		let u = Mp.m1(a.k)
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then b.k = y
		end 'x3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:56:11: 'Mp.m1(a.k)' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-hand-af-p2-acquire-cross-state-direct -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `p2-acquire-cross-state-direct` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Ka
	var n as Num
	var k as Bx
	var l as Bx

	static function create(n Num, k isolated Bx, l isolated Bx) returns Self
		return Self{n: n, k: merge k, l: merge l}
	end 'create'

	function setK(b Bx)
		self.k = b
	end 'setK'

	function touch(b Bx)
		b.n = 9
	end 'touch'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Ka'

function main() returns ExitCode
	let a = spawn Ka.create(0, k: Bx.create(0), l: Bx.create(1))
	let b = spawn Ka.create(0, k: Bx.create(2), l: Bx.create(3))
	acquire a, b 'a1'
		b.k = a.k
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:38:3: cannot write 'k' through 'b', which is read-only while 'the isolated value' is open
```

<!-- test: model-hand-af-p3-acquire-cross-state-local -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `p3-acquire-cross-state-local` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Ka
	var n as Num
	var k as Bx
	var l as Bx

	static function create(n Num, k isolated Bx, l isolated Bx) returns Self
		return Self{n: n, k: merge k, l: merge l}
	end 'create'

	function setK(b Bx)
		self.k = b
	end 'setK'

	function touch(b Bx)
		b.n = 9
	end 'touch'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Ka'

function main() returns ExitCode
	let a = spawn Ka.create(0, k: Bx.create(0), l: Bx.create(1))
	let b = spawn Ka.create(0, k: Bx.create(2), l: Bx.create(3))
	acquire a, b 'a1'
		let w = a.k
		b.k = w
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:38:11: 'a.k' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-hand-af-p4-acquire-cross-state-call -->
The model (`core-m3w-fa14`) refuses it by `C-STATE`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `p4-acquire-cross-state-call` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Ka
	var n as Num
	var k as Bx
	var l as Bx

	static function create(n Num, k isolated Bx, l isolated Bx) returns Self
		return Self{n: n, k: merge k, l: merge l}
	end 'create'

	function setK(b Bx)
		self.k = b
	end 'setK'

	function touch(b Bx)
		b.n = 9
	end 'touch'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Ka'

function main() returns ExitCode
	let a = spawn Ka.create(0, k: Bx.create(0), l: Bx.create(1))
	let b = spawn Ka.create(0, k: Bx.create(2), l: Bx.create(3))
	acquire a, b 'a1'
		b.setK(a.k)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:38:10: 'a.k' is mutable outside 'b'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-af-p5-acquire-cross-state-via-union-call -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `p5-acquire-cross-state-via-union-call` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

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

type Ka
	var n as Num
	var k as Bx
	var l as Bx

	static function create(n Num, k isolated Bx, l isolated Bx) returns Self
		return Self{n: n, k: merge k, l: merge l}
	end 'create'

	function setK(b Bx)
		self.k = b
	end 'setK'

	function touch(b Bx)
		b.n = 9
	end 'touch'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Ka'

function main() returns ExitCode
	let a = spawn Ka.create(0, k: Bx.create(0), l: Bx.create(1))
	let b = spawn Ka.create(0, k: Bx.create(2), l: Bx.create(3))
	acquire a, b 'a1'
		let u = Mp.m1(a.k)
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then b.setK(y)
		end 'x3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:56:11: 'Mp.m1(a.k)' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-hand-af-p8-acquire-state-into-outer-plain -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `p8-acquire-state-into-outer-plain` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

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

type Ka
	var n as Num
	var k as Bx
	var l as Bx

	static function create(n Num, k isolated Bx, l isolated Bx) returns Self
		return Self{n: n, k: merge k, l: merge l}
	end 'create'

	function setK(b Bx)
		self.k = b
	end 'setK'

	function touch(b Bx)
		b.n = 9
	end 'touch'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Ka'

function main() returns ExitCode
	let a = spawn Ka.create(0, k: Bx.create(0), l: Bx.create(1))
	let _ = spawn Ka.create(0, k: Bx.create(2), l: Bx.create(3))
	let h = Hp.create(Bx.create(0), q: Bx.create(1))
	acquire a 'a1'
		let u = Mp.m1(a.k)
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then h.p = y
		end 'x3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:66:11: 'Mp.m1(a.k)' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-hand-af-p9-acquire-state-into-outer-var -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `p9-acquire-state-into-outer-var` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

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

type Ka
	var n as Num
	var k as Bx
	var l as Bx

	static function create(n Num, k isolated Bx, l isolated Bx) returns Self
		return Self{n: n, k: merge k, l: merge l}
	end 'create'

	function setK(b Bx)
		self.k = b
	end 'setK'

	function touch(b Bx)
		b.n = 9
	end 'touch'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Ka'

function main() returns ExitCode
	let a = spawn Ka.create(0, k: Bx.create(0), l: Bx.create(1))
	let _ = spawn Ka.create(0, k: Bx.create(2), l: Bx.create(3))
	var o = Bx.create(5)
	acquire a 'a1'
		let u = Mp.m1(a.k)
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then o = y
		end 'x3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	o.n = 1
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:57:11: 'Mp.m1(a.k)' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-hand-af-p10-acquire-state-keeping-fn -->
The model (`core-m3w-fa14`) refuses it by `C-EXIT`; the compiler refuses it with E3202. Model program `p10-acquire-state-keeping-fn` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

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

type Ka
	var n as Num
	var k as Bx
	var l as Bx

	static function create(n Num, k isolated Bx, l isolated Bx) returns Self
		return Self{n: n, k: merge k, l: merge l}
	end 'create'

	function setK(b Bx)
		self.k = b
	end 'setK'

	function touch(b Bx)
		b.n = 9
	end 'touch'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Ka'

function keep(b Bx, h Hp)
	h.p = b
end 'keep'

function main() returns ExitCode
	let a = spawn Ka.create(0, k: Bx.create(0), l: Bx.create(1))
	let _ = spawn Ka.create(0, k: Bx.create(2), l: Bx.create(3))
	let h = Hp.create(Bx.create(0), q: Bx.create(1))
	acquire a 'a1'
		let u = Mp.m1(a.k)
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then keep(y, h: h)
		end 'x3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:70:11: 'Mp.m1(a.k)' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: model-hand-af-p11-acquire-state-fn-built-union -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `p11-acquire-state-fn-built-union` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

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

type Ka
	var n as Num
	var k as Bx
	var l as Bx

	static function create(n Num, k isolated Bx, l isolated Bx) returns Self
		return Self{n: n, k: merge k, l: merge l}
	end 'create'

	function setK(b Bx)
		self.k = b
	end 'setK'

	function touch(b Bx)
		b.n = 9
	end 'touch'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Ka'

function mkMp(b Bx) returns Mp
	return Mp.m1(b)
end 'mkMp'

function main() returns ExitCode
	let a = spawn Ka.create(0, k: Bx.create(0), l: Bx.create(1))
	let b = spawn Ka.create(0, k: Bx.create(2), l: Bx.create(3))
	acquire a, b 'a1'
		match mkMp(a.k) 'x3'
			m0(x) then x.fin(1)
			m1(y) then b.k = y
		end 'x3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:62:15: cannot write 'k' through 'b', which is read-only while 'the isolated value' is open
```

<!-- test: model-hand-af-p24-lent-form-state-plain-store-then-use -->
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3201. Model program `p24-lent-form-state-plain-store-then-use` of redteamAF.hand_af6, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Ka
	var n as Num
	var k as Bx
	var l as Bx

	static function create(n Num, k isolated Bx, l isolated Bx) returns Self
		return Self{n: n, k: merge k, l: merge l}
	end 'create'

	function setK(b Bx)
		self.k = b
	end 'setK'

	function touch(b Bx)
		b.n = 9
	end 'touch'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Ka'

function main() returns ExitCode
	let a = spawn Ka.create(0, k: Bx.create(0), l: Bx.create(1))
	let _ = spawn Ka.create(0, k: Bx.create(2), l: Bx.create(3))
	let y = Bx.create(7)
	acquire a 'a1'
		a.k = y
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	y.n = 2
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:39:9: 'y' is mutable outside 'a'; a store into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-af-m6-param-union-state-method -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m6-param-union-state-method` of redteamAF.hand_af9, in capmodel `reggio/REPORTRTAF.txt`.

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

	function eat(u Sh)
		match u 'x1'
			none then break
			held(q) then q.fin(1)
		end 'x1'
	end 'eat'
end 'Kx'

function main() returns ExitCode
	let a = spawn Kx.create(0)
	acquire a 'a1'
		a.eat(Sh.held(Rl.create(1)))
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-t11-payload-into-acquire-state-iso -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `t11-payload-into-acquire-state-iso` of redteamAG.hand_ag1, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ac
	var n as Num
	var item as isolated Bx
	var plain as Bx

	static function create(n Num, item isolated Bx, plain isolated Bx) returns Self
		return Self{n: n, item: item, plain: merge plain}
	end 'create'

	function keepP(b Bx)
		self.plain = b
	end 'keepP'

	function keepI(b isolated Bx)
		self.item = b
	end 'keepI'

	function peek() returns Num
		return self.n
	end 'peek'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ac'

function main() returns ExitCode
	let s = spawn Ac.create(0, item: Bx.create(0), plain: Bx.create(1))
	let u = Mx.m1(Bx.create(1)) as isolated Mx
	acquire s 'a1'
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then s.keepI(y)
		end 'x3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-ac-modify-slot-keep -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ac-modify-slot-keep` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ab
	var n as Num
	var item as isolated Wo
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Wo, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ab'

function main() returns ExitCode
	let s = spawn Ab.create(0, item: Wo.create(Bx.create(0), item: Bx.create(1)), plain: Bx.create(2), hq: Hq.create(Bx.create(3), q: Bx.create(4)))
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	acquire s 'a1'
		modify s.item 'm3'
			match u 'x4'
				m0(x) then x.fin(1)
				m1(y) then s.item.keep = y
			end 'x4'
		end 'm3'
	end 'a1' otherwise 's2'
		match u 'x5'
			m0(x) then x.fin(1)
			m1(y) then y.n = 1
		end 'x5'
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-ac-body-keep -->
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3201. Model program `ac-body-keep` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ab
	var n as Num
	var item as isolated Wo
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Wo, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ab'

function main() returns ExitCode
	let s = spawn Ab.create(0, item: Wo.create(Bx.create(0), item: Bx.create(1)), plain: Bx.create(2), hq: Hq.create(Bx.create(3), q: Bx.create(4)))
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	acquire s 'a1'
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then s.item.keep = y
		end 'x3'
	end 'a1' otherwise 's2'
		match u 'x4'
			m0(x) then x.fin(1)
			m1(y) then y.n = 1
		end 'x4'
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:86:29: 'y' is mutable outside 's.item'; a store into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ag-ac-modify-slot-plain -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `ac-modify-slot-plain` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ab
	var n as Num
	var item as isolated Wo
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Wo, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ab'

function main() returns ExitCode
	let s = spawn Ab.create(0, item: Wo.create(Bx.create(0), item: Bx.create(1)), plain: Bx.create(2), hq: Hq.create(Bx.create(3), q: Bx.create(4)))
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	acquire s 'a1'
		modify s.item 'm3'
			match u 'x4'
				m0(x) then x.fin(1)
				m1(y) then s.plain = y
			end 'x4'
		end 'm3'
	end 'a1' otherwise 's2'
		match u 'x5'
			m0(x) then x.fin(1)
			m1(y) then y.n = 1
		end 'x5'
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:87:16: cannot write 'plain' through 's', which is read-only while 'm3' is open
```

<!-- test: model-hand-ag-ac-body-plain -->
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3201. Model program `ac-body-plain` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ab
	var n as Num
	var item as isolated Wo
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Wo, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ab'

function main() returns ExitCode
	let s = spawn Ab.create(0, item: Wo.create(Bx.create(0), item: Bx.create(1)), plain: Bx.create(2), hq: Hq.create(Bx.create(3), q: Bx.create(4)))
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	acquire s 'a1'
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then s.plain = y
		end 'x3'
	end 'a1' otherwise 's2'
		match u 'x4'
			m0(x) then x.fin(1)
			m1(y) then y.n = 1
		end 'x4'
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:86:25: 'y' is mutable outside 's'; a store into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ag-ac-modify-slot-iso-slot -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ac-modify-slot-iso-slot` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ab
	var n as Num
	var item as isolated Wo
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Wo, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ab'

function main() returns ExitCode
	let s = spawn Ab.create(0, item: Wo.create(Bx.create(0), item: Bx.create(1)), plain: Bx.create(2), hq: Hq.create(Bx.create(3), q: Bx.create(4)))
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	acquire s 'a1'
		modify s.item 'm3'
			match u 'x4'
				m0(x) then x.fin(1)
				m1(y) then s.item.item = y
			end 'x4'
		end 'm3'
	end 'a1' otherwise 's2'
		match u 'x5'
			m0(x) then x.fin(1)
			m1(y) then y.n = 1
		end 'x5'
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-ac-body-iso-slot -->
The model (`core-m3w-fa14`) refuses it by `C-RECOVER`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `ac-body-iso-slot` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ab
	var n as Num
	var item as isolated Wo
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Wo, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ab'

function main() returns ExitCode
	let s = spawn Ab.create(0, item: Wo.create(Bx.create(0), item: Bx.create(1)), plain: Bx.create(2), hq: Hq.create(Bx.create(3), q: Bx.create(4)))
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	acquire s 'a1'
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then s.item.item = y
		end 'x3'
	end 'a1' otherwise 's2'
		match u 'x4'
			m0(x) then x.fin(1)
			m1(y) then y.n = 1
		end 'x4'
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:86:29: 'y' is mutable outside 's.item'; a store into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ag-ac-modify-slot-none -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ac-modify-slot-none` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ab
	var n as Num
	var item as isolated Wo
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Wo, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ab'

function main() returns ExitCode
	let s = spawn Ab.create(0, item: Wo.create(Bx.create(0), item: Bx.create(1)), plain: Bx.create(2), hq: Hq.create(Bx.create(3), q: Bx.create(4)))
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	acquire s 'a1'
		modify s.item 'm3'
			match u 'x4'
				m0(x) then x.fin(1)
				m1 then break
			end 'x4'
		end 'm3'
	end 'a1' otherwise 's2'
		match u 'x5'
			m0(x) then x.fin(1)
			m1(y) then y.n = 1
		end 'x5'
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-ac-body-none -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ac-body-none` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ab
	var n as Num
	var item as isolated Wo
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Wo, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ab'

function main() returns ExitCode
	let s = spawn Ab.create(0, item: Wo.create(Bx.create(0), item: Bx.create(1)), plain: Bx.create(2), hq: Hq.create(Bx.create(3), q: Bx.create(4)))
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	acquire s 'a1'
		match u 'x3'
			m0(x) then x.fin(1)
			m1 then break
		end 'x3'
	end 'a1' otherwise 's2'
		match u 'x4'
			m0(x) then x.fin(1)
			m1(y) then y.n = 1
		end 'x4'
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-acr-modify-slot-keep-p -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `acr-modify-slot-keep-p` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ab
	var n as Num
	var item as isolated Wo
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Wo, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ab'

function main() returns ExitCode
	let s = spawn Ab.create(0, item: Wo.create(Bx.create(0), item: Bx.create(1)), plain: Bx.create(2), hq: Hq.create(Bx.create(3), q: Bx.create(4)))
	let u = Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))) as isolated Mr
	acquire s 'a1'
		modify s.item 'm3'
			match u 'x4'
				m0(x) then x.fin(1)
				m1(y) then s.item.keep = y.p
			end 'x4'
		end 'm3'
	end 'a1' otherwise 's2'
		match u 'x5'
			m0(x) then x.fin(1)
			m1(y) then y.q.n = 1
		end 'x5'
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-acr-modify-slot-plain-p -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `acr-modify-slot-plain-p` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ab
	var n as Num
	var item as isolated Wo
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Wo, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ab'

function main() returns ExitCode
	let s = spawn Ab.create(0, item: Wo.create(Bx.create(0), item: Bx.create(1)), plain: Bx.create(2), hq: Hq.create(Bx.create(3), q: Bx.create(4)))
	let u = Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))) as isolated Mr
	acquire s 'a1'
		modify s.item 'm3'
			match u 'x4'
				m0(x) then x.fin(1)
				m1(y) then s.plain = y.p
			end 'x4'
		end 'm3'
	end 'a1' otherwise 's2'
		match u 'x5'
			m0(x) then x.fin(1)
			m1(y) then y.q.n = 1
		end 'x5'
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:87:16: cannot write 'plain' through 's', which is read-only while 'm3' is open
```

<!-- test: model-hand-ag-acr-modify-slot-hq -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `acr-modify-slot-hq` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ab
	var n as Num
	var item as isolated Wo
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Wo, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ab'

function main() returns ExitCode
	let s = spawn Ab.create(0, item: Wo.create(Bx.create(0), item: Bx.create(1)), plain: Bx.create(2), hq: Hq.create(Bx.create(3), q: Bx.create(4)))
	let u = Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))) as isolated Mr
	acquire s 'a1'
		modify s.item 'm3'
			match u 'x4'
				m0(x) then x.fin(1)
				m1(y) then s.hq = y
			end 'x4'
		end 'm3'
	end 'a1' otherwise 's2'
		match u 'x5'
			m0(x) then x.fin(1)
			m1(y) then y.q.n = 1
		end 'x5'
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:87:16: cannot write 'hq' through 's', which is read-only while 'm3' is open
```

<!-- test: model-hand-ag-bm-acquire-other -->
The model (`core-m3w-fa14`) refuses it by `R13-NESTED`; the compiler refuses it with E3201. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `bm-acquire-other` of redteamAG.hand_ag12, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ab
	var n as Num
	var item as isolated Wo
	var plain as Bx
	var hq as Hq

	static function create(n Num, item isolated Wo, plain isolated Bx, hq isolated Hq) returns Self
		return Self{n: n, item: item, plain: merge plain, hq: merge hq}
	end 'create'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ab'

type Bn
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function eat(u Mr, t Ab.handle)
		acquire t 'a1'
			match u 'x3'
				m0(x) then x.fin(1)
				m1(y) then t.plain = y.p
			end 'x3'
		end 'a1' otherwise 's2'
			match u 'x4'
				m0(x) then x.fin(1)
				m1(y) then y.q.n = 1
			end 'x4'
		end 's2'
	end 'eat'
end 'Bn'

function eatMr(pV Mr)
	match pV 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.q.n = 1
	end 'x1'
end 'eatMr'

function main() returns ExitCode
	let t = spawn Ab.create(0, item: Wo.create(Bx.create(0), item: Bx.create(1)), plain: Bx.create(2), hq: Hq.create(Bx.create(3), q: Bx.create(4)))
	let k = spawn Bn.create(0)
	try k.eat(Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))), t: t.clone()) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull(z, _) then eatMr(z)
			serviceStopped(z, _) then eatMr(z)
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:91:26: 'y.p' is mutable outside 't'; a store into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ag-f1-control-acquire -->
The model (`core-m3w-fa14`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `f1-control-acquire` of redteamAG.hand_ag16, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ac
	var n as Num
	var plain as Bx

	static function create(n Num, plain isolated Bx) returns Self
		return Self{n: n, plain: merge plain}
	end 'create'

	function keepP(b Bx)
		self.plain = b
	end 'keepP'
end 'Ac'

function eatMp(p Mp)
	match p 'x1'
		m0(x) then x.fin(1)
		m1(y) then y.n = 1
	end 'x1'
end 'eatMp'

function main() returns ExitCode
	let s = spawn Ac.create(0, plain: Bx.create(0))
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	acquire s 'a1'
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then s.keepP(y)
		end 'x3'
	end 'a1' otherwise 's2'
		eatMp(u)
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:65:23: 'y' is mutable outside 's'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ag-acq-mp-store -->
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3201. Model program `acq-mp-store` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ac
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

	function keepI(b isolated Bx)
		self.item = b
	end 'keepI'

	function keepH(h Hq)
		self.hq = h
	end 'keepH'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ac'

function main() returns ExitCode
	let s = spawn Ac.create(0, item: Bx.create(0), plain: Bx.create(1), hq: Hq.create(Bx.create(1), q: Bx.create(1)))
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	acquire s 'a1'
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then s.plain = y
		end 'x3'
	end 'a1' otherwise 's2'
		match u 'x4'
			m0(x) then x.fin(1)
			m1(y) then y.n = 1
		end 'x4'
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:89:25: 'y' is mutable outside 's'; a store into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ag-acq-mp-keepp -->
The model (`core-m3w-fa14`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `acq-mp-keepP` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ac
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

	function keepI(b isolated Bx)
		self.item = b
	end 'keepI'

	function keepH(h Hq)
		self.hq = h
	end 'keepH'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ac'

function main() returns ExitCode
	let s = spawn Ac.create(0, item: Bx.create(0), plain: Bx.create(1), hq: Hq.create(Bx.create(1), q: Bx.create(1)))
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	acquire s 'a1'
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then s.keepP(y)
		end 'x3'
	end 'a1' otherwise 's2'
		match u 'x4'
			m0(x) then x.fin(1)
			m1(y) then y.n = 1
		end 'x4'
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:89:23: 'y' is mutable outside 's'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ag-acq-mp-keepi -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `acq-mp-keepI` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ac
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

	function keepI(b isolated Bx)
		self.item = b
	end 'keepI'

	function keepH(h Hq)
		self.hq = h
	end 'keepH'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ac'

function main() returns ExitCode
	let s = spawn Ac.create(0, item: Bx.create(0), plain: Bx.create(1), hq: Hq.create(Bx.create(1), q: Bx.create(1)))
	let u = Mp.m1(Bx.create(1)) as isolated Mp
	acquire s 'a1'
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then s.keepI(y)
		end 'x3'
	end 'a1' otherwise 's2'
		match u 'x4'
			m0(x) then x.fin(1)
			m1(y) then y.n = 1
		end 'x4'
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-acq-mr-store-hq -->
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3201. Model program `acq-mr-store-hq` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ac
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

	function keepI(b isolated Bx)
		self.item = b
	end 'keepI'

	function keepH(h Hq)
		self.hq = h
	end 'keepH'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ac'

function main() returns ExitCode
	let s = spawn Ac.create(0, item: Bx.create(0), plain: Bx.create(1), hq: Hq.create(Bx.create(1), q: Bx.create(1)))
	let u = Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))) as isolated Mr
	acquire s 'a1'
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then s.hq = y
		end 'x3'
	end 'a1' otherwise 's2'
		match u 'x4'
			m0(x) then x.fin(1)
			m1(y) then y.q.n = 1
		end 'x4'
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:89:22: 'y' is mutable outside 's'; a store into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ag-acq-mr-store-part -->
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3201. Model program `acq-mr-store-part` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ac
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

	function keepI(b isolated Bx)
		self.item = b
	end 'keepI'

	function keepH(h Hq)
		self.hq = h
	end 'keepH'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ac'

function main() returns ExitCode
	let s = spawn Ac.create(0, item: Bx.create(0), plain: Bx.create(1), hq: Hq.create(Bx.create(1), q: Bx.create(1)))
	let u = Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))) as isolated Mr
	acquire s 'a1'
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then s.plain = y.p
		end 'x3'
	end 'a1' otherwise 's2'
		match u 'x4'
			m0(x) then x.fin(1)
			m1(y) then y.q.n = 1
		end 'x4'
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:89:25: 'y.p' is mutable outside 's'; a store into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ag-acq-mr-keeph -->
The model (`core-m3w-fa14`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `acq-mr-keepH` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ac
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

	function keepI(b isolated Bx)
		self.item = b
	end 'keepI'

	function keepH(h Hq)
		self.hq = h
	end 'keepH'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ac'

function main() returns ExitCode
	let s = spawn Ac.create(0, item: Bx.create(0), plain: Bx.create(1), hq: Hq.create(Bx.create(1), q: Bx.create(1)))
	let u = Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))) as isolated Mr
	acquire s 'a1'
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then s.keepH(y)
		end 'x3'
	end 'a1' otherwise 's2'
		match u 'x4'
			m0(x) then x.fin(1)
			m1(y) then y.q.n = 1
		end 'x4'
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:89:23: 'y' is mutable outside 's'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-ag-acq-mr-keepp-part -->
The model (`core-m3w-fa14`) refuses it by `C-ONECALL`; the compiler refuses it with E3201. Model program `acq-mr-keepP-part` of redteamAG.hand_ag2, in capmodel `reggio/REPORTRTAG.txt`.

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

type Ac
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

	function keepI(b isolated Bx)
		self.item = b
	end 'keepI'

	function keepH(h Hq)
		self.hq = h
	end 'keepH'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ac'

function main() returns ExitCode
	let s = spawn Ac.create(0, item: Bx.create(0), plain: Bx.create(1), hq: Hq.create(Bx.create(1), q: Bx.create(1)))
	let u = Mr.m1(Hq.create(Bx.create(1), q: Bx.create(2))) as isolated Mr
	acquire s 'a1'
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then s.keepP(y.p)
		end 'x3'
	end 'a1' otherwise 's2'
		match u 'x4'
			m0(x) then x.fin(1)
			m1(y) then y.q.n = 1
		end 'x4'
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:89:23: 'y.p' is mutable outside 's'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: model-hand-fa14-v-join-acquire -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `v-join-acquire` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

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

type Hq
	var p as Bx
	var q as Bx

	static function create(p Bx, q Bx) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Hq'

type Ac
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

	function keepI(b isolated Bx)
		self.item = b
	end 'keepI'

	function keepH(h Hq)
		self.hq = h
	end 'keepH'

	export function poke()
		self.n = 1
	end 'poke'
end 'Ac'

function main() returns ExitCode
	let s = spawn Ac.create(0, item: Bx.create(0), plain: Bx.create(1), hq: Hq.create(Bx.create(0), q: Bx.create(1)))
	let u = Mx.m1(Bx.create(1)) as isolated Mx
	acquire s 'a1'
		match u 'x3'
			m0(x) then x.fin(1)
			m1(y) then s.keepI(y)
		end 'x3'
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa14-m-state-method-param -->
The model (`core-m3w-fa14`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `m-state-method-param` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Zm4
	var b as Bx

	static function create(b isolated Bx) returns Self
		return Self{b: merge b}
	end 'create'

	function keep(bV isolated Bx)
		self.b = bV
	end 'keep'
end 'Zm4'

function main() returns ExitCode
	let r = spawn Zm4.create(Bx.create(0))
	let bV = Bx.create(1) as isolated Bx
	acquire r 'a1'
		r.keep(bV)
	end 'a1' otherwise 's2'
		let _ = 0 as Num
	end 's2'
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:20:12: cannot store an isolated 'Bx' into 'b', which holds a mutable 'Bx'; `merge` it first
```
