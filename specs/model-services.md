---
feature: model-services
status: experimental
keywords: [model, capmodel, region, capability]
category: semantics
---

# Services and messages

## Documentation

These cases are programs of the capmodel executable model, translated to Maxon by
`scripts/model-specs/generate.py` (`docs/SPECS.md`, "Model-derived specs") and pinned to the model's verdict on
spawned services, messages and replies. A program the model accepts runs and exits 0; one it refuses fails to compile, with the diagnostic
the compiler gives. A case is written only where the compiler and the model agree, so the suite fails
the day the compiler stops agreeing with a verdict the model pinned. The rule set is `core-m3w-fa24`.

## Tests

<!-- test: model-finding-z-scc-three-cycle-wait-deep -->
The model (`core-m3w-fa24`) refuses it by `R5-HOLDWAIT`; the compiler refuses it with E3221. Model program `scc-three-cycle-wait-deep` of redteamZ/p8.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Sv
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function ask() returns Num
		return self.n
	end 'ask'
end 'Sv'

type Sw
	var n as Num
	var r as Sv.handle

	static function create(n Num, r Sv.handle) returns Self
		return Self{n: n, r: r}
	end 'create'

	export function m()
		self.n = a(self.r, nV: 1)
	end 'm'
end 'Sw'

function a(rV Sv.handle, nV Num) returns Num
	if nV != 0 'i1'
		return b(rV, nV: 0)
	end 'i1' else 'e2'
		return 1
	end 'e2'
end 'a'

function b(rV Sv.handle, nV Num) returns Num
	if nV != 0 'i1'
		return c(rV, nV: 0)
	end 'i1' else 'e2'
		return 2
	end 'e2'
end 'b'

function c(rV Sv.handle, nV Num) returns Num
	if nV != 0 'i1'
		return a(rV, nV: 0)
	end 'i1' else 'e2'
		return try await rV.ask() otherwise panic("send failed")
	end 'e2'
end 'c'

function main() returns ExitCode
	let rV = spawn Sv.create(0)
	let s = spawn Sw.create(0, r: rV.clone())
	try s.m() otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3221: <fragment>:24:18: the message 'm' may wait (it calls 'a', which calls 'b', which calls 'c', which awaits a reply from 'Sv' at line 49) while holding its own service; send the request with this service's handle and handle the reply in another message
```

<!-- test: model-finding-z-scc-three-cycle-wait-deep-reordered -->
The model (`core-m3w-fa24`) refuses it by `R5-HOLDWAIT`; the compiler refuses it with E3221. Model program `scc-three-cycle-wait-deep-reordered` of redteamZ/p8.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Sv
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function ask() returns Num
		return self.n
	end 'ask'
end 'Sv'

type Sw
	var n as Num
	var r as Sv.handle

	static function create(n Num, r Sv.handle) returns Self
		return Self{n: n, r: r}
	end 'create'

	export function m()
		self.n = a(self.r, nV: 1)
	end 'm'
end 'Sw'

function c(rV Sv.handle, nV Num) returns Num
	if nV != 0 'i1'
		return a(rV, nV: 0)
	end 'i1' else 'e2'
		return try await rV.ask() otherwise panic("send failed")
	end 'e2'
end 'c'

function a(rV Sv.handle, nV Num) returns Num
	if nV != 0 'i1'
		return b(rV, nV: 0)
	end 'i1' else 'e2'
		return 1
	end 'e2'
end 'a'

function b(rV Sv.handle, nV Num) returns Num
	if nV != 0 'i1'
		return c(rV, nV: 0)
	end 'i1' else 'e2'
		return 2
	end 'e2'
end 'b'

function main() returns ExitCode
	let rV = spawn Sv.create(0)
	let s = spawn Sw.create(0, r: rV.clone())
	try s.m() otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3221: <fragment>:24:18: the message 'm' may wait (it calls 'a', which calls 'b', which calls 'c', which awaits a reply from 'Sv' at line 33) while holding its own service; send the request with this service's handle and handle the reply in another message
```

<!-- test: model-finding-z-scc-record-methods -->
The model (`core-m3w-fa24`) refuses it by `R5-HOLDWAIT`; the compiler refuses it with E3221. Model program `scc-record-methods` of redteamZ/p8.txt, in capmodel `reggio/REPORTRTZ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Ka
	var z as Num

	static function create(z Num) returns Self
		return Self{z: z}
	end 'create'

	function m(rV Sv.handle, nV Num) returns Num
		if nV != 0 'i1'
			let k = Kb.create(0)
			return k.m(rV, nV: 0)
		end 'i1' else 'e2'
			return 1
		end 'e2'
	end 'm'
end 'Ka'

type Kb
	var z as Num

	static function create(z Num) returns Self
		return Self{z: z}
	end 'create'

	function m(rV Sv.handle, nV Num) returns Num
		if nV != 0 'i1'
			let k = Ka.create(0)
			return k.m(rV, nV: 0)
		end 'i1' else 'e2'
			return try await rV.ask() otherwise panic("send failed")
		end 'e2'
	end 'm'
end 'Kb'

type Sv
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function ask() returns Num
		return self.n
	end 'ask'
end 'Sv'

type Sw
	var n as Num
	var r as Sv.handle

	static function create(n Num, r Sv.handle) returns Self
		return Self{n: n, r: r}
	end 'create'

	export function m()
		let k = Ka.create(0)
		self.n = k.m(self.r, nV: 1)
	end 'm'
end 'Sw'

function main() returns ExitCode
	let rV = spawn Sv.create(0)
	let s = spawn Sw.create(0, r: rV.clone())
	try s.m() otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3221: <fragment>:58:18: the message 'm' may wait (it calls 'm', which calls 'm', which awaits a reply from 'Sv' at line 33) while holding its own service; send the request with this service's handle and handle the reply in another message
```

<!-- test: model-finding-aa-send-fresh-tk -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `send-fresh-tk` of redteamAA/p10.txt, in capmodel `reggio/REPORTRTAA.txt`.

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

function g(r Rc.handle) returns Num
	try r.put(Tk.create(1)) otherwise ignore
	return 0
end 'g'

function main() returns ExitCode
	let r = spawn Rc.create(Tk.create(0))
	let _ = g(r)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-aa-send-local-tk -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `send-local-tk` of redteamAA/p10.txt, in capmodel `reggio/REPORTRTAA.txt`.

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

function g(r Rc.handle) returns Num
	let tV = Tk.create(1)
	try r.put(tV) otherwise ignore
	return 0
end 'g'

function main() returns ExitCode
	let r = spawn Rc.create(Tk.create(0))
	let _ = g(r)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-finding-aa-send-iso-slot-then-write -->
The model (`core-m3w-fa24`) refuses it by `R3-SEND`; the compiler refuses it with E3217. Model program `send-iso-slot-then-write` of redteamAA/p3.txt, in capmodel `reggio/REPORTRTAA.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Hh
	var i as isolated Bt

	static function create(i isolated Bt) returns Self
		return Self{i: i}
	end 'create'
end 'Hh'

type Rcv
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(bV Bt)
		let _ = bV
		self.n = 1
	end 'take'
end 'Rcv'

function main() returns ExitCode
	let h = Hh.create(Bt.create(1))
	let r = spawn Rcv.create(0)
	try r.take(h.i) otherwise ignore
	h.i.b = 2
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:35:10: parameter `bV` of the message `Rcv.take` is a `Bt` with no capability word, and this `spawn` makes `Rcv` a service — whose messages hand their arguments to another green thread, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `take(bV isolated …)` or `take(bV immutable …)`
```

<!-- test: model-finding-aa-spawn-iso-slot-then-write -->
The model (`core-m3w-fa24`) refuses it by `R3-SEND`; the compiler refuses it with E3217. Model program `spawn-iso-slot-then-write` of redteamAA/p3.txt, in capmodel `reggio/REPORTRTAA.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bt
	var b as Num

	static function create(b Num) returns Self
		return Self{b: b}
	end 'create'
end 'Bt'

type Hh
	var i as isolated Bt

	static function create(i isolated Bt) returns Self
		return Self{i: i}
	end 'create'
end 'Hh'

type Rcv
	var n as Num

	static function create(bV Bt) returns Self
		let _ = bV
		return Self{n: 1}
	end 'create'
end 'Rcv'

function main() returns ExitCode
	let h = Hh.create(Bt.create(1))
	let _ = spawn Rcv.create(h.i)
	h.i.b = 2
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:31:10: parameter `bV` of the factory `Rcv.create` is a `Bt` with no capability word, and this `spawn` moves its arguments into the state of `Rcv`, a service — whose state is run by whichever green thread takes its next message, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `create(bV isolated …)` or `create(bV immutable …)`
```

<!-- test: model-finding-aa-send-match-payload-unique -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `send-match-payload-unique` of redteamAA/p9.txt, in capmodel `reggio/REPORTRTAA.txt`.

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

type Rc
	var t as Tk

	static function create(t isolated Tk) returns Self
		return Self{t: merge t}
	end 'create'

	export function put(tV Tk)
		self.t = tV
	end 'put'
end 'Rc'

function g(r Rc.handle) returns Num
	let u = UT.have(Tk.create(1))
	match u 'x1'
		have(q) then try r.put(q) otherwise ignore
		none then break
	end 'x1'
	return 0
end 'g'

function main() returns ExitCode
	let r = spawn Rc.create(Tk.create(0))
	let _ = g(r)
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:32:26: cannot hand 'q' to argument `tV` of the message `Rc.put`: it keeps a unique value, and 'q' is only borrowed by this code, and only the code that owns a unique value hands it to another green thread; move it in from its owner
```

<!-- test: model-finding-aa-send-byref-param -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `send-byref-param` of redteamAA/p9.txt, in capmodel `reggio/REPORTRTAA.txt`.

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

function g(r Rc.handle, tV mutable Tk) returns Num
	try r.put(tV) otherwise ignore
	return 0
end 'g'

function main() returns ExitCode
	var tV = Tk.create(1)
	let r = spawn Rc.create(Tk.create(0))
	let _ = g(r, tV: tV)
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:25:12: cannot hand 'tV' to argument `tV` of the message `Rc.put`: it keeps a unique value, and 'tV' is only borrowed by this code, and only the code that owns a unique value hands it to another green thread; move it in from its owner
```

<!-- test: model-finding-aa-send-record-field-unique -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `send-record-field-unique` of redteamAA/p9.txt, in capmodel `reggio/REPORTRTAA.txt`.

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

type Rc
	var t as Tk

	static function create(t isolated Tk) returns Self
		return Self{t: merge t}
	end 'create'

	export function put(tV Tk)
		self.t = tV
	end 'put'
end 'Rc'

function g(r Rc.handle) returns Num
	let h = Hold.create(Tk.create(1))
	try r.put(h.tk) otherwise ignore
	return 0
end 'g'

function main() returns ExitCode
	let r = spawn Rc.create(Tk.create(0))
	let _ = g(r)
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:34:12: cannot hand 'h.tk' to argument `tV` of the message `Rc.put`: it keeps a unique value, and a unique value cannot be moved out of a field or an element it was only read from; move it out with `pop` or `remove`, or `swap` it out of an isolated field
```

<!-- test: model-finding-ad-swap-field-self-state -->
The model (`core-m3w-fa24`) refuses it by `C-ISOREAD`; the compiler refuses it with E3198. Model program `swap-field-self-state` of redteamAD/t3.txt, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Box
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type S
	var k as Num
	var b as isolated Box

	static function create(k Num, b isolated Box) returns Self
		return Self{k: k, b: b}
	end 'create'

	export function go()
		let _ = swap self.b with self.b
	end 'go'
end 'S'

function main() returns ExitCode
	let s = spawn S.create(0, b: Box.create(1))
	try s.go() otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:21:28: 'self.b' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: model-finding-ae-frozen-field-iso-recv-call-waits -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `frozen-field-iso-recv-call-waits` of redteamAE/p19.txt, in capmodel `reggio/REPORTRTAE.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function touch(s S.handle) returns Num
		return waitIt(s)
	end 'touch'
end 'Bx'

type Hd
	var item as isolated Bx

	static function create(item isolated Bx) returns Self
		return Self{item: item}
	end 'create'
end 'Hd'

type S
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function plain() returns Num
		return 5
	end 'plain'
end 'S'

function waitIt(s S.handle) returns Num
	let r = try s.plain() otherwise panic("send failed")
	return await r
end 'waitIt'

function main() returns ExitCode
	let sv = spawn S.create(0)
	let h = Hd.create(Bx.create(1)) as isolated Hd
	let fh = freeze h
	let _ = fh.item.touch(sv)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ac-hx-handle-in-mailbox -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hx-handle-in-mailbox` of redteamAC.hand_ac12, in capmodel `reggio/REPORTRTAC.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Sf
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function go(me Sf.handle)
		try me.again(me.clone()) otherwise (sendFailure) 'h1'
			match sendFailure 'x2'
				mailboxFull then break
				serviceStopped then break
			end 'x2'
		end 'h1'
	end 'go'

	export function again(me Sf.handle)
		let _ = me
		self.n = 1
	end 'again'
end 'Sf'

function main() returns ExitCode
	let s = spawn Sf.create(0)
	try s.go(s.clone()) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-k-send-iso-via-fn -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `k-send-iso-via-fn` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

type Hr2
	var n as Num
	var t as isolated Tok

	static function create(n Num, t isolated Tok) returns Self
		return Self{n: n, t: t}
	end 'create'

	export function take(x isolated Tok)
		self.t = x
	end 'take'
end 'Hr2'

function give(h Hr2.handle, tV isolated Tok)
	try h.take(tV) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull then break
			serviceStopped then break
		end 'x2'
	end 'h1'
end 'give'

function main() returns ExitCode
	let h = spawn Hr2.create(0, t: Tok.create(0))
	let tV = Tok.create(1) as isolated Tok
	give(h, tV: tV)
	give(h, tV: tV)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:38:14: use of moved value 'tV': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-ad-k-behaviour-param -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `k-behaviour-param` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tok is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tok'

type Kq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function go(t Tok)
		keepLocal(t)
		keepLocal(t)
	end 'go'
end 'Kq'

function keepLocal(t Tok)
	let _ = t
end 'keepLocal'

function main() returns ExitCode
	let k = spawn Kq.create(0)
	try k.go(Tok.create(0)) otherwise (sendFailure) 'h1'
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

<!-- test: model-hand-ad-hc-three-chain -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hc-three-chain` of redteamAD.hand_ad5, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias B_handleArray = Array with B.handle
typealias C_handleArray = Array with C.handle

type A
	var k as Num
	var xs as B_handleArray

	static function create(k Num, xs isolated B_handleArray) returns Self
		return Self{k: k, xs: merge xs}
	end 'create'
end 'A'

type B
	var k as Num
	var xs as C_handleArray

	static function create(k Num, xs isolated C_handleArray) returns Self
		return Self{k: k, xs: merge xs}
	end 'create'
end 'B'

type C
	var k as Num
	var n as Num

	static function create(k Num, n Num) returns Self
		return Self{k: k, n: n}
	end 'create'
end 'C'

function main() returns ExitCode
	let _ = spawn C.create(0, n: 1)
	let _ = spawn B.create(0, xs: C_handleArray.create(0))
	let _ = spawn A.create(0, xs: B_handleArray.create(0))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-hc-spawn-in-factory-cycle -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hc-spawn-in-factory-cycle` of redteamAD.hand_ad5, in capmodel `reggio/REPORTRTAD.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type B
	var k as Num

	static function create(x Num) returns Self
		return Self{k: x}
	end 'create'
end 'B'

type A
	var k as Num
	var b as B.handle

	static function create(x Num) returns Self
		return Self{k: x, b: spawn B.create(x)}
	end 'create'
end 'A'

function main() returns ExitCode
	let _ = spawn A.create(0)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ad-d-spawn-in-factory-chain -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d-spawn-in-factory-chain` of redteamAD.hand_ad8, in capmodel `reggio/REPORTRTAD.txt`.

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

type Qq
	var n as Num

	static function create(p Pl.handle) returns Self
		let _ = p
		return Self{n: 1}
	end 'create'
end 'Qq'

type Rr
	var n as Num
	var q as Qq.handle

	static function create(p Pl.handle) returns Self
		return Self{q: spawn Qq.create(p.clone()), n: 2}
	end 'create'
end 'Rr'

function main() returns ExitCode
	let p = spawn Pl.create(0)
	let _ = spawn Rr.create(p.clone())
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ae-f-plain -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f-plain` of redteamAE.hand_ae3, in capmodel `reggio/REPORTRTAE.txt`.

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

type Fw
	var n as Num

	static function create(o Q.handle) returns Self
		let _ = o
		return Self{n: 1}
	end 'create'
end 'Fw'

function main() returns ExitCode
	let q = spawn Q.create(0)
	let _ = spawn Fw.create(q.clone())
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ae-h-msg-await -->
The model (`core-m3w-fa24`) refuses it by `R5-HOLDWAIT`; the compiler refuses it with E3221. Model program `h-msg-await` of redteamAE.hand_ae3, in capmodel `reggio/REPORTRTAE.txt`.

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
		let r = try o.ping() otherwise panic("send failed")
		let _ = await r
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
error E3221: <fragment>:27:18: the message 'go' may wait (it awaits a reply from 'Q' at line 29) while holding its own service; send the request with this service's handle and handle the reply in another message
```

<!-- test: model-hand-af-p3-holder-await-payload-direct -->
The model (`core-m3w-fa24`) refuses it by `R5-HOLDWAIT`; the compiler refuses it with E3221. Model program `p3-holder-await-payload-direct` of redteamAF.hand_af11, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type S2
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function plain() returns Num
		return 5
	end 'plain'
end 'S2'

type D
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function go(s S2.handle)
		let q = try s.plain() otherwise panic("send failed")
		let _ = await q
	end 'go'
end 'D'

function main() returns ExitCode
	let s = spawn S2.create(0)
	let d = spawn D.create(0)
	try d.go(s.clone()) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3221: <fragment>:23:18: the message 'go' may wait (it awaits a reply from 'S2' at line 25) while holding its own service; send the request with this service's handle and handle the reply in another message
```

<!-- test: model-hand-af-h5-owned-payload-carries-handle -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `h5-owned-payload-carries-handle` of redteamAF.hand_af7, in capmodel `reggio/REPORTRTAF.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union Hu
	none
	has(h isolated Hx)
end 'Hu'

type Hx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Hx'

type Fx
	var n as Num
	var h as isolated Hx

	static function create(n Num, h isolated Hx) returns Self
		return Self{n: n, h: h}
	end 'create'

	export function put(me Fx.handle)
		let _ = me
		let u = Hu.has(Hx.create(1))
		match u 'x1'
			none then break
			has(q) then self.h = q
		end 'x1'
	end 'put'
end 'Fx'

function main() returns ExitCode
	let f = spawn Fx.create(0, h: Hx.create(0))
	try f.put(f.clone()) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:30:16: cannot write 'h' through 'self', which is read-only while 'x1' is open
```

<!-- test: model-hand-ag-u1-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u1-none` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

union UI
	none
	have(t isolated Tk)
end 'UI'

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

type Holdu
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Holdu'

type Su
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(uV UT)
		match uV 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'take'

	export function takeI(uV isolated UI)
		match uV 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'takeI'

	export function takeJ(uV UJ)
		match uV 'x1'
			none then break
			have(j) then j.n = 1
		end 'x1'
	end 'takeJ'

	export function takeK(tV Tk)
		tV.n = 3
	end 'takeK'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Su'

function main() returns ExitCode
	let _ = spawn Su.create(0)
	let uV = UT.have(Tk.create(1))
	let _ = Holdu.create(UT.none)
	let _ = UTArray.create(0)
	match uV 'x1'
		none then break
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-u2-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u2-none` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

union UI
	none
	have(t isolated Tk)
end 'UI'

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

type Holdu
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Holdu'

type Su
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(uV UT)
		match uV 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'take'

	export function takeI(uV isolated UI)
		match uV 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'takeI'

	export function takeJ(uV UJ)
		match uV 'x1'
			none then break
			have(j) then j.n = 1
		end 'x1'
	end 'takeJ'

	export function takeK(tV Tk)
		tV.n = 3
	end 'takeK'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Su'

function f(uV UT, su Su.handle, hd Holdu, ua UTArray)
	let _ = su
	let _ = hd
	let _ = ua
	match uV 'x1'
		none then break
		have then break
	end 'x1'
end 'f'

function main() returns ExitCode
	let su = spawn Su.create(0)
	let hd = Holdu.create(UT.none)
	let ua = UTArray.create(0)
	f(UT.have(Tk.create(1)), su: su, hd: hd, ua: ua)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-u3-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u3-none` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

union UI
	none
	have(t isolated Tk)
end 'UI'

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

type Holdu
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Holdu'

type Su
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(uV UT)
		match uV 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'take'

	export function takeI(uV isolated UI)
		match uV 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'takeI'

	export function takeJ(uV UJ)
		match uV 'x1'
			none then break
			have(j) then j.n = 1
		end 'x1'
	end 'takeJ'

	export function takeK(tV Tk)
		tV.n = 3
	end 'takeK'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Su'

function main() returns ExitCode
	let _ = spawn Su.create(0)
	let hd = Holdu.create(UT.have(Tk.create(1)))
	let _ = UTArray.create(0)
	match hd.u 'x1'
		none then break
		have then break
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-u3-copy -->
The model (`core-m3w-fa24`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `u3-copy` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

union UI
	none
	have(t isolated Tk)
end 'UI'

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

type Holdu
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Holdu'

type Su
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(uV UT)
		match uV 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'take'

	export function takeI(uV isolated UI)
		match uV 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'takeI'

	export function takeJ(uV UJ)
		match uV 'x1'
			none then break
			have(j) then j.n = 1
		end 'x1'
	end 'takeJ'

	export function takeK(tV Tk)
		tV.n = 3
	end 'takeK'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Su'

function main() returns ExitCode
	let _ = spawn Su.create(0)
	let hd = Holdu.create(UT.have(Tk.create(1)))
	let _ = UTArray.create(0)
	match hd.u 'x1'
		none then break
		have then let _ = hd.u
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:88:17: cannot bind '_' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: model-hand-ag-u1-call -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u1-call` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

union UI
	none
	have(t isolated Tk)
end 'UI'

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

type Holdu
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Holdu'

type Su
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(uV UT)
		match uV 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'take'

	export function takeI(uV isolated UI)
		match uV 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'takeI'

	export function takeJ(uV UJ)
		match uV 'x1'
			none then break
			have(j) then j.n = 1
		end 'x1'
	end 'takeJ'

	export function takeK(tV Tk)
		tV.n = 3
	end 'takeK'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Su'

function eatUT(uV UT)
	match uV 'x1'
		none then break
		have(tV) then tV.n = 2
	end 'x1'
end 'eatUT'

function main() returns ExitCode
	let _ = spawn Su.create(0)
	let uV = UT.have(Tk.create(1))
	let _ = Holdu.create(UT.none)
	let _ = UTArray.create(0)
	match uV 'x1'
		none then break
		have then eatUT(uV)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-u2-call -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u2-call` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

union UI
	none
	have(t isolated Tk)
end 'UI'

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

type Holdu
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Holdu'

type Su
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(uV UT)
		match uV 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'take'

	export function takeI(uV isolated UI)
		match uV 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'takeI'

	export function takeJ(uV UJ)
		match uV 'x1'
			none then break
			have(j) then j.n = 1
		end 'x1'
	end 'takeJ'

	export function takeK(tV Tk)
		tV.n = 3
	end 'takeK'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Su'

function eatUT(uV UT)
	match uV 'x1'
		none then break
		have(tV) then tV.n = 2
	end 'x1'
end 'eatUT'

function f(uV UT, su Su.handle, hd Holdu, ua UTArray)
	let _ = su
	let _ = hd
	let _ = ua
	match uV 'x1'
		none then break
		have then eatUT(uV)
	end 'x1'
end 'f'

function main() returns ExitCode
	let su = spawn Su.create(0)
	let hd = Holdu.create(UT.none)
	let ua = UTArray.create(0)
	f(UT.have(Tk.create(1)), su: su, hd: hd, ua: ua)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-u3-call -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u3-call` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias UTArray = Array with UT

union UT
	none
	have(t Tk)
end 'UT'

union UI
	none
	have(t isolated Tk)
end 'UI'

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

type Holdu
	var u as UT

	static function create(u UT) returns Self
		return Self{u: u}
	end 'create'
end 'Holdu'

type Su
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(uV UT)
		match uV 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'take'

	export function takeI(uV isolated UI)
		match uV 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'takeI'

	export function takeJ(uV UJ)
		match uV 'x1'
			none then break
			have(j) then j.n = 1
		end 'x1'
	end 'takeJ'

	export function takeK(tV Tk)
		tV.n = 3
	end 'takeK'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Su'

function eatUT(uV UT)
	match uV 'x1'
		none then break
		have(tV) then tV.n = 2
	end 'x1'
end 'eatUT'

function main() returns ExitCode
	let _ = spawn Su.create(0)
	let hd = Holdu.create(UT.have(Tk.create(1)))
	let _ = UTArray.create(0)
	match hd.u 'x1'
		none then break
		have then eatUT(hd.u)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-j1-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `j1-none` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

union UI
	none
	have(t isolated Tk)
end 'UI'

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

type Su
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(u UT)
		match u 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'take'

	export function takeI(u isolated UI)
		match u 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'takeI'

	export function takeJ(u UJ)
		match u 'x1'
			none then break
			have(j) then j.n = 1
		end 'x1'
	end 'takeJ'

	export function takeK(tV Tk)
		tV.n = 3
	end 'takeK'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Su'

function main() returns ExitCode
	let _ = spawn Su.create(0)
	let u = UJ.have(Tj.create(1, t: Tk.create(2)))
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

<!-- test: model-hand-ag-j1-inner-read -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `j1-inner-read` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

union UI
	none
	have(t isolated Tk)
end 'UI'

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

type Su
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(u UT)
		match u 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'take'

	export function takeI(u isolated UI)
		match u 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'takeI'

	export function takeJ(u UJ)
		match u 'x1'
			none then break
			have(j) then j.n = 1
		end 'x1'
	end 'takeJ'

	export function takeK(tV Tk)
		tV.n = 3
	end 'takeK'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Su'

function main() returns ExitCode
	let _ = spawn Su.create(0)
	let u = UJ.have(Tj.create(1, t: Tk.create(2)))
	match u 'x1'
		none then break
		have(j) then let _ = j.t.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-j1-inner-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `j1-inner-write` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

union UI
	none
	have(t isolated Tk)
end 'UI'

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

type Su
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(u UT)
		match u 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'take'

	export function takeI(u isolated UI)
		match u 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'takeI'

	export function takeJ(u UJ)
		match u 'x1'
			none then break
			have(j) then j.n = 1
		end 'x1'
	end 'takeJ'

	export function takeK(tV Tk)
		tV.n = 3
	end 'takeK'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Su'

function main() returns ExitCode
	let _ = spawn Su.create(0)
	let u = UJ.have(Tj.create(1, t: Tk.create(2)))
	match u 'x1'
		none then break
		have(j) then j.t.n = 5
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-j1-inner-move -->
The model (`core-m3w-fa24`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `j1-inner-move` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

union UT
	none
	have(t Tk)
end 'UT'

union UI
	none
	have(t isolated Tk)
end 'UI'

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

type Su
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(u UT)
		match u 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'take'

	export function takeI(u isolated UI)
		match u 'x1'
			none then break
			have(tV) then tV.n = 1
		end 'x1'
	end 'takeI'

	export function takeJ(u UJ)
		match u 'x1'
			none then break
			have(j) then j.n = 1
		end 'x1'
	end 'takeJ'

	export function takeK(tV Tk)
		tV.n = 3
	end 'takeK'

	export function plain() returns Num
		return 1
	end 'plain'
end 'Su'

function main() returns ExitCode
	let _ = spawn Su.create(0)
	let u = UJ.have(Tj.create(1, t: Tk.create(2)))
	match u 'x1'
		none then break
		have(j) then let _ = j.t
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:78:20: cannot bind '_' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: model-hand-ag-f1-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f1-none` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(xs TkArray)
		for x in xs 'f1'
			x.n = 1
		end 'f1'
	end 'takeA'
end 'Sa'

function main() returns ExitCode
	let _ = spawn Sa.create(0)
	let xs = TkArray.create(0)
	xs.push(Tk.create(1))
	for _ in xs 'f1'
		let _ = 0 as Num
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-f2-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-none` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs2
	var xs as TkArray

	static function create(xs TkArray) returns Self
		return Self{xs: xs}
	end 'create'
end 'Hs2'

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(xsV TkArray)
		for x in xsV 'f1'
			x.n = 1
		end 'f1'
	end 'takeA'
end 'Sa'

function main() returns ExitCode
	let _ = spawn Sa.create(0)
	let h = Hs2.create(TkArray.create(0))
	h.xs.push(Tk.create(1))
	for _ in h.xs 'f1'
		let _ = 0 as Num
	end 'f1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ag-f1-copy -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `f1-copy` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(xs TkArray)
		for x in xs 'f1'
			x.n = 1
		end 'f1'
	end 'takeA'
end 'Sa'

function main() returns ExitCode
	let _ = spawn Sa.create(0)
	let xs = TkArray.create(0)
	xs.push(Tk.create(1))
	for _ in xs 'f1'
		let _ = xs
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:11: use of moved value 'xs': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ag-f2-copy -->
The model (`core-m3w-fa24`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `f2-copy` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs2
	var xs as TkArray

	static function create(xs TkArray) returns Self
		return Self{xs: xs}
	end 'create'
end 'Hs2'

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(xsV TkArray)
		for x in xsV 'f1'
			x.n = 1
		end 'f1'
	end 'takeA'
end 'Sa'

function main() returns ExitCode
	let _ = spawn Sa.create(0)
	let h = Hs2.create(TkArray.create(0))
	h.xs.push(Tk.create(1))
	for _ in h.xs 'f1'
		let _ = h.xs
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:40:7: cannot bind '_' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: model-hand-ag-f1-copy-read -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `f1-copy-read` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(xs TkArray)
		for x in xs 'f1'
			x.n = 1
		end 'f1'
	end 'takeA'
end 'Sa'

function main() returns ExitCode
	let _ = spawn Sa.create(0)
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
error E3102: <fragment>:32:11: use of moved value 'xs': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ag-f2-copy-read -->
The model (`core-m3w-fa24`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `f2-copy-read` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs2
	var xs as TkArray

	static function create(xs TkArray) returns Self
		return Self{xs: xs}
	end 'create'
end 'Hs2'

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(xsV TkArray)
		for x in xsV 'f1'
			x.n = 1
		end 'f1'
	end 'takeA'
end 'Sa'

function main() returns ExitCode
	let _ = spawn Sa.create(0)
	let h = Hs2.create(TkArray.create(0))
	h.xs.push(Tk.create(1))
	for t in h.xs 'f1'
		let _ = h.xs
		let _ = t.n
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:40:7: cannot bind '_' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: model-hand-ag-f1-copy-write -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `f1-copy-write` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(xs TkArray)
		for x in xs 'f1'
			x.n = 1
		end 'f1'
	end 'takeA'
end 'Sa'

function main() returns ExitCode
	let _ = spawn Sa.create(0)
	let xs = TkArray.create(0)
	xs.push(Tk.create(1))
	for t in xs 'f1'
		let _ = xs
		t.n = 4
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:11: use of moved value 'xs': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ag-f2-copy-write -->
The model (`core-m3w-fa24`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `f2-copy-write` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs2
	var xs as TkArray

	static function create(xs TkArray) returns Self
		return Self{xs: xs}
	end 'create'
end 'Hs2'

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(xsV TkArray)
		for x in xsV 'f1'
			x.n = 1
		end 'f1'
	end 'takeA'
end 'Sa'

function main() returns ExitCode
	let _ = spawn Sa.create(0)
	let h = Hs2.create(TkArray.create(0))
	h.xs.push(Tk.create(1))
	for t in h.xs 'f1'
		let _ = h.xs
		t.n = 4
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:40:7: cannot bind '_' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: model-hand-ag-f1-call -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `f1-call` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(xs TkArray)
		for x in xs 'f1'
			x.n = 1
		end 'f1'
	end 'takeA'
end 'Sa'

function eatArr(xs TkArray)
	for x in xs 'f1'
		x.n = 2
	end 'f1'
end 'eatArr'

function main() returns ExitCode
	let _ = spawn Sa.create(0)
	let xs = TkArray.create(0)
	xs.push(Tk.create(1))
	for _ in xs 'f1'
		eatArr(xs)
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:38:10: 'eatArr' needs a writable parameter 'xs' at line 29; 'xs' is read-only while 'f1' is open
```

<!-- test: model-hand-ag-f2-call -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `f2-call` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs2
	var xs as TkArray

	static function create(xs TkArray) returns Self
		return Self{xs: xs}
	end 'create'
end 'Hs2'

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(xsV TkArray)
		for x in xsV 'f1'
			x.n = 1
		end 'f1'
	end 'takeA'
end 'Sa'

function eatArr(xsV TkArray)
	for x in xsV 'f1'
		x.n = 2
	end 'f1'
end 'eatArr'

function main() returns ExitCode
	let _ = spawn Sa.create(0)
	let h = Hs2.create(TkArray.create(0))
	h.xs.push(Tk.create(1))
	for _ in h.xs 'f1'
		eatArr(h.xs)
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:46:10: 'eatArr' needs a writable parameter 'xsV' at line 37; 'h' is read-only while 'f1' is open
```

<!-- test: model-hand-ag-f1-call-write -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `f1-call-write` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(xs TkArray)
		for x in xs 'f1'
			x.n = 1
		end 'f1'
	end 'takeA'
end 'Sa'

function eatArr(xs TkArray)
	for x in xs 'f1'
		x.n = 2
	end 'f1'
end 'eatArr'

function main() returns ExitCode
	let _ = spawn Sa.create(0)
	let xs = TkArray.create(0)
	xs.push(Tk.create(1))
	for t in xs 'f1'
		eatArr(xs)
		t.n = 4
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:38:10: 'eatArr' needs a writable parameter 'xs' at line 29; 'xs' is read-only while 'f1' is open
```

<!-- test: model-hand-ag-f2-call-write -->
The model (`core-m3w-fa24`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `f2-call-write` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs2
	var xs as TkArray

	static function create(xs TkArray) returns Self
		return Self{xs: xs}
	end 'create'
end 'Hs2'

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(xsV TkArray)
		for x in xsV 'f1'
			x.n = 1
		end 'f1'
	end 'takeA'
end 'Sa'

function eatArr(xsV TkArray)
	for x in xsV 'f1'
		x.n = 2
	end 'f1'
end 'eatArr'

function main() returns ExitCode
	let _ = spawn Sa.create(0)
	let h = Hs2.create(TkArray.create(0))
	h.xs.push(Tk.create(1))
	for t in h.xs 'f1'
		eatArr(h.xs)
		t.n = 4
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:46:10: 'eatArr' needs a writable parameter 'xsV' at line 37; 'h' is read-only while 'f1' is open
```

<!-- test: model-hand-ag-f1-send -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f1-send` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(xs TkArray)
		for x in xs 'f1'
			x.n = 1
		end 'f1'
	end 'takeA'
end 'Sa'

function main() returns ExitCode
	let su = spawn Sa.create(0)
	let xs = TkArray.create(0)
	xs.push(Tk.create(1))
	for t in xs 'f1'
		try su.takeA(xs) otherwise (sendFailure) 'h2'
			match sendFailure 'x3'
				mailboxFull then break
				serviceStopped then break
			end 'x3'
		end 'h2'
		t.n = 5
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:16: use of moved value 'xs': it was moved in an earlier iteration of this loop
```

<!-- test: model-hand-ag-f2-send -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f2-send` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs2
	var xs as TkArray

	static function create(xs TkArray) returns Self
		return Self{xs: xs}
	end 'create'
end 'Hs2'

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(xsV TkArray)
		for x in xsV 'f1'
			x.n = 1
		end 'f1'
	end 'takeA'
end 'Sa'

function main() returns ExitCode
	let su = spawn Sa.create(0)
	let h = Hs2.create(TkArray.create(0))
	h.xs.push(Tk.create(1))
	for t in h.xs 'f1'
		try su.takeA(h.xs) otherwise (sendFailure) 'h2'
			match sendFailure 'x3'
				mailboxFull then break
				serviceStopped then break
			end 'x3'
		end 'h2'
		t.n = 5
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:40:16: cannot hand 'h.xs' to argument `xsV` of the message `Sa.takeA`: it keeps a unique value, and a unique value cannot be moved out of a field or an element it was only read from; move it out with `pop` or `remove`, or `swap` it out of an isolated field
```

<!-- test: model-hand-ag-f2-assign -->
The model (`core-m3w-fa24`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `f2-assign` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias TkArray = Array with Tk

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Hs2
	var xs as TkArray

	static function create(xs TkArray) returns Self
		return Self{xs: xs}
	end 'create'
end 'Hs2'

type Sa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(xsV TkArray)
		for x in xsV 'f1'
			x.n = 1
		end 'f1'
	end 'takeA'
end 'Sa'

function main() returns ExitCode
	let _ = spawn Sa.create(0)
	let h = Hs2.create(TkArray.create(0))
	h.xs.push(Tk.create(1))
	for t in h.xs 'f1'
		h.xs = TkArray.create(0)
		t.n = 6
	end 'f1'
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:40:3: cannot write 'xs' through 'h', which is read-only while 'f1' drains it
```

<!-- test: model-hand-ah-i3-fresh-into-send -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `i3-fresh-into-send` of redteamAH.hand_ah5, in capmodel `reggio/REPORTRTAH.txt`.

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
	let w = Bx.create(1)
	let k = spawn Sk.create(0)
	try k.take(w) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ah-plain-try-full-mailbox-handle-in-handler -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `plain-try-full-mailbox-handle-in-handler` of redteamAH.hand_ah9, in capmodel `reggio/REPORTRTAH.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Sc
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function poke()
		self.n = 1
	end 'poke'
end 'Sc'

type Cap1
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeH(h Sc.handle)
		let _ = h
		self.n = 1
	end 'takeH'

	export function busy()
		var i = 1 as Num
		var j = 1 as Num
		while i != 0 'w1'
			i = j
			j = lg(0)
		end 'w1'
	end 'busy'

	export function poke()
		self.n = 1
	end 'poke'
end 'Cap1'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let k = spawn(capacity: 1) Cap1.create(0)
	let s = spawn Sc.create(0)
	try k.busy() otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull then break
			serviceStopped then break
		end 'x2'
	end 'h1'
	try k.poke() otherwise (sendFailure) 'h3'
		match sendFailure 'x4'
			mailboxFull then break
			serviceStopped then break
		end 'x4'
	end 'h3'
	try k.takeH(s.clone()) otherwise (sendFailure) 'h5'
		match sendFailure 'x6'
			mailboxFull then try s.poke() otherwise ignore
			serviceStopped then try s.poke() otherwise ignore
		end 'x6'
	end 'h5'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-a7-lend-result-sent -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `a7-lend-result-sent` of redteamAI.hand_ai1, in capmodel `reggio/REPORTRTAI.txt`.

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

type Sy
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'

	export function take(x UT)
		let _ = x
		let _ = 1 as Num
	end 'take'
end 'Sy'

function idUT(x UT) returns UT
	return x
end 'idUT'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let s = spawn Sy.create(0)
	match u 'x1'
		none then break
		have then try s.take(idUT(u)) otherwise ignore
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:39:24: cannot pass 'u' to parameter 'x' of 'idUT': it keeps a unique value, and 'u' is matched by the arm this call is in, which lends it to its bindings; move it before or after the `match`
```

<!-- test: model-hand-ai-c6-send-payload -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `c6-send-payload` of redteamAI.hand_ai1, in capmodel `reggio/REPORTRTAI.txt`.

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

type Sw
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function putU(x UT)
		let _ = x
		let _ = 1 as Num
	end 'putU'

	export function putT(x Tk)
		let _ = x.n
	end 'putT'
end 'Sw'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let s = spawn Sw.create(0)
	match u 'x1'
		none then break
		have(t) then try s.putT(t) otherwise ignore
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:39:27: cannot hand 't' to argument `x` of the message `Sw.putT`: it keeps a unique value, and 't' is only borrowed by this code, and only the code that owns a unique value hands it to another green thread; move it in from its owner
```

<!-- test: model-hand-ai-c7-spawn-payload -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `c7-spawn-payload` of redteamAI.hand_ai1, in capmodel `reggio/REPORTRTAI.txt`.

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

type Sq
	var t as Tk

	static function create(x Tk) returns Self
		return Self{t: x}
	end 'create'
end 'Sq'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	match u 'x1'
		none then break
		have(tV) then let _ = spawn Sq.create(tV)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:29:41: cannot hand 'tV' to argument `x` of `spawn Sq.create(…)`: it keeps a unique value, and 'tV' is only borrowed by this code, and only the code that owns a unique value hands it to another green thread; move it in from its owner
```

<!-- test: model-hand-ai-p-none-none-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-none-none-none` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1 then let _ = 1 as Num
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-none-none-fb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-none-none-fb` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1 then let _ = 1 as Num
	end 'x1'
	fb.n = 2
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-none-none-fr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-none-none-fr` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let fr = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1 then let _ = 1 as Num
	end 'x1'
	fr.keep.n = 4
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-keepin-none-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-keepin-none-none` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then o.keep = fb
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-keepin-none-fb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-keepin-none-fb` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then o.keep = fb
	end 'x1'
	fb.n = 2
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-keepin-none-fr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-keepin-none-fr` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let fr = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then o.keep = fb
	end 'x1'
	fr.keep.n = 4
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-keepin-fr-none-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-keepin-fr-none-none` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let fr = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then o.keep = fr.keep
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-keepin-fr-none-fb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-keepin-fr-none-fb` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let fr = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then o.keep = fr.keep
	end 'x1'
	fb.n = 2
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-keepin-fr-none-fr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-keepin-fr-none-fr` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let fr = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then o.keep = fr.keep
	end 'x1'
	fr.keep.n = 4
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-out-none-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-out-none-none` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then let _ = o.keep
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-out-none-fb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-out-none-fb` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then let _ = o.keep
	end 'x1'
	fb.n = 2
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-out-none-fr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-out-none-fr` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let fr = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then let _ = o.keep
	end 'x1'
	fr.keep.n = 4
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-out-fr-none-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-out-fr-none-none` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let fr = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then fr.keep = o.keep
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-out-fr-none-fb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-out-fr-none-fb` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let fr = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then fr.keep = o.keep
	end 'x1'
	fb.n = 2
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-out-fr-none-fr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-out-fr-none-fr` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let fr = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then fr.keep = o.keep
	end 'x1'
	fr.keep.n = 4
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-newin-none-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-newin-none-none` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then o.keep = Bx.create(5)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-newin-none-fb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-newin-none-fb` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then o.keep = Bx.create(5)
	end 'x1'
	fb.n = 2
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-newin-none-fr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-newin-none-fr` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let fr = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then o.keep = Bx.create(5)
	end 'x1'
	fr.keep.n = 4
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-fieldint-none-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-fieldint-none-none` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then o.keep.n = 6
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-fieldint-none-fb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-fieldint-none-fb` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then o.keep.n = 6
	end 'x1'
	fb.n = 2
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-fieldint-none-fr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-fieldint-none-fr` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let fr = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
	match u 'x1'
		q0 then break
		q1(o) then o.keep.n = 6
	end 'x1'
	fr.keep.n = 4
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-i-none-none-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `i-none-none-none` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

union Iq
	i0
	i1(w isolated Wo)
end 'Iq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Iq.i1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Iq
	match u 'x1'
		i0 then break
		i1 then let _ = 1 as Num
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-i-none-none-fb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `i-none-none-fb` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

union Iq
	i0
	i1(w isolated Wo)
end 'Iq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Iq.i1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Iq
	match u 'x1'
		i0 then break
		i1 then let _ = 1 as Num
	end 'x1'
	fb.n = 2
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-i-none-none-fr -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `i-none-none-fr` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

union Iq
	i0
	i1(w isolated Wo)
end 'Iq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let fr = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Iq.i1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Iq
	match u 'x1'
		i0 then break
		i1 then let _ = 1 as Num
	end 'x1'
	fr.keep.n = 4
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-i-swap-item-fb-none-none -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `i-swap-item-fb-none-none` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

union Iq
	i0
	i1(w isolated Wo)
end 'Iq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Iq.i1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Iq
	match u 'x1'
		i0 then break
		i1(o) then let _ = swap o.item with fb
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:64:39: 'fb' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted. Declare it `as isolated Bx`, or pass a copy
```

<!-- test: model-hand-ai-i-swap-item-fb-none-fb -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `i-swap-item-fb-none-fb` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

union Iq
	i0
	i1(w isolated Wo)
end 'Iq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Iq.i1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Iq
	match u 'x1'
		i0 then break
		i1(o) then let _ = swap o.item with fb
	end 'x1'
	fb.n = 2
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:64:39: 'fb' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted. Declare it `as isolated Bx`, or pass a copy
```

<!-- test: model-hand-ai-i-swap-item-fb-none-fr -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `i-swap-item-fb-none-fr` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

union Iq
	i0
	i1(w isolated Wo)
end 'Iq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let fr = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let u = Iq.i1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Iq
	match u 'x1'
		i0 then break
		i1(o) then let _ = swap o.item with fb
	end 'x1'
	fr.keep.n = 4
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:64:39: 'fb' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted. Declare it `as isolated Bx`, or pass a copy
```

<!-- test: model-hand-ai-a-none-none-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a-none-none-none` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

union Aq
	a0
	a1(xs BxArray)
end 'Aq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let _ = BxArray.create(0)
	let u = Aq.a1(BxArray.create(0)) as isolated Aq
	match u 'x1'
		a0 then break
		a1 then let _ = 1 as Num
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-a-none-none-fb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a-none-none-fb` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

union Aq
	a0
	a1(xs BxArray)
end 'Aq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let _ = BxArray.create(0)
	let u = Aq.a1(BxArray.create(0)) as isolated Aq
	match u 'x1'
		a0 then break
		a1 then let _ = 1 as Num
	end 'x1'
	fb.n = 2
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-a-push-fb-none-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a-push-fb-none-none` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

union Aq
	a0
	a1(xs BxArray)
end 'Aq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let _ = BxArray.create(0)
	let u = Aq.a1(BxArray.create(0)) as isolated Aq
	match u 'x1'
		a0 then break
		a1(o) then o.push(fb)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-a-push-fb-none-fb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a-push-fb-none-fb` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

union Aq
	a0
	a1(xs BxArray)
end 'Aq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let _ = BxArray.create(0)
	let u = Aq.a1(BxArray.create(0)) as isolated Aq
	match u 'x1'
		a0 then break
		a1(o) then o.push(fb)
	end 'x1'
	fb.n = 2
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-a-push-new-none-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a-push-new-none-none` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

union Aq
	a0
	a1(xs BxArray)
end 'Aq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let _ = BxArray.create(0)
	let u = Aq.a1(BxArray.create(0)) as isolated Aq
	match u 'x1'
		a0 then break
		a1(o) then o.push(Bx.create(1))
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-a-push-new-none-fb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `a-push-new-none-fb` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

union Aq
	a0
	a1(xs BxArray)
end 'Aq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let _ = BxArray.create(0)
	let u = Aq.a1(BxArray.create(0)) as isolated Aq
	match u 'x1'
		a0 then break
		a1(o) then o.push(Bx.create(1))
	end 'x1'
	fb.n = 2
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-ia-none-none-none -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ia-none-none-none` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

union Ia
	j0
	j1(xs isolated BxArray)
end 'Ia'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let _ = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let _ = BxArray.create(0)
	let u = Ia.j1(BxArray.create(0)) as isolated Ia
	match u 'x1'
		j0 then break
		j1 then let _ = 1 as Num
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-ia-none-none-fb -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ia-none-none-fb` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

union Ia
	j0
	j1(xs isolated BxArray)
end 'Ia'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function main() returns ExitCode
	let fb = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let _ = BxArray.create(0)
	let u = Ia.j1(BxArray.create(0)) as isolated Ia
	match u 'x1'
		j0 then break
		j1 then let _ = 1 as Num
	end 'x1'
	fb.n = 2
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-ai-p-loop-carry-out -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p-loop-carry-out` of redteamAI.hand_ai3, in capmodel `reggio/REPORTRTAI.txt`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias BxArray = Array with Bx

union Pq
	q0
	q1(w Wo)
end 'Pq'

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

type Sv3
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeW(w isolated Wo)
		let _ = w
		let _ = 1 as Num
	end 'takeW'

	export function takeA(a isolated BxArray)
		let _ = a
		let _ = 1 as Num
	end 'takeA'

	export function eatPq(u isolated Pq)
		match u 'x1'
			q0 then break
			q1(o) then o.keep.n = 3
		end 'x1'
	end 'eatPq'
end 'Sv3'

function lg(x Num) returns Num
	return x
end 'lg'

function takeWo(w isolated Wo)
	let _ = w
	let _ = 1 as Num
end 'takeWo'

function main() returns ExitCode
	let _ = Bx.create(7)
	let _ = Wo.create(Bx.create(8), item: Bx.create(9))
	let _ = spawn Sv3.create(0)
	let keepc = Bx.create(1)
	var i = 2 as Num
	while i != 0 'w1'
		let u = Pq.q1(Wo.create(Bx.create(0), item: Bx.create(1))) as isolated Pq
		match u 'x2'
			q0 then break
			q1(o) then takeWo(o)
		end 'x2'
		keepc.n = 3
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-aj-send-closure -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `send-closure` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

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

type Sw
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function putU(x UT)
		let _ = x
		let _ = 1 as Num
	end 'putU'

	export function putT(x Tk)
		let _ = x.n
	end 'putT'
end 'Sw'

function f(t Tk, h Kp)
	let _ = h
	let u = t
	let s = spawn Sw.create(0)
	try s.putU(UT.have(u)) otherwise ignore
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
error E3102: <fragment>:53:10: use of moved value 't': its ownership moved to the parameter 't' of 'f', which keeps it
```

<!-- test: model-hand-aj-spawn-rename-2 -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `spawn-rename` of redteamAJ.hand_aj3, in capmodel `reggio/REPORTRTAJ.txt`.

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

type Sk4
	var k as Tk

	static function create(x Tk) returns Self
		return Self{k: x}
	end 'create'
end 'Sk4'

function f(t Tk, h Kp)
	let _ = h
	let u = t
	let _ = spawn Sk4.create(u)
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
error E3214: <fragment>:31:27: cannot hand 'u' to argument `x` of `spawn Sk4.create(…)`: it keeps a unique value, and 'u' is only borrowed by this code, and only the code that owns a unique value hands it to another green thread; move it in from its owner
```

<!-- test: model-hand-aj-ipm-factory-iso-store -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `ipm-factory-iso-store` of redteamAJ.hand_aj5, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Sf2
	var b as Bx
	var n as Num

	static function create(bV isolated Bx) returns Self
		return Self{b: bV, n: 1}
	end 'create'
end 'Sf2'

function main() returns ExitCode
	let _ = spawn Sf2.create(Bx.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:17:18: cannot store an isolated 'Bx' into 'Sf2.b', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-aj-ipm-factory-iso-merge -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ipm-factory-iso-merge` of redteamAJ.hand_aj5, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Sf2
	var b as Bx
	var n as Num

	static function create(bV isolated Bx) returns Self
		return Self{b: merge bV, n: 1}
	end 'create'
end 'Sf2'

function main() returns ExitCode
	let _ = spawn Sf2.create(Bx.create(1))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-aj-ipm-msg-plain-store -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ipm-msg-plain-store` of redteamAJ.hand_aj5, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Sm2
	var t as Tk
	var n as Num

	static function create(t isolated Tk, n Num) returns Self
		return Self{t: merge t, n: n}
	end 'create'

	export function put(tV Tk)
		self.t = tV
	end 'put'
end 'Sm2'

function main() returns ExitCode
	let s = spawn Sm2.create(Tk.create(0), n: 0)
	try s.put(Tk.create(1)) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-aj-ipm-msg-iso-rename-store -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `ipm-msg-iso-rename-store` of redteamAJ.hand_aj5, in capmodel `reggio/REPORTRTAJ.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Sm3
	var b as Bx
	var n as Num

	static function create(b isolated Bx, n Num) returns Self
		return Self{b: merge b, n: n}
	end 'create'

	export function put(bV isolated Bx)
		let c = bV
		self.b = c
	end 'put'
end 'Sm3'

function main() returns ExitCode
	let s = spawn Sm3.create(Bx.create(0), n: 0)
	try s.put(Bx.create(1)) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:22:12: cannot store an isolated 'Bx' into 'b', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-aj-ipm-msg-iso-union-store -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `ipm-msg-iso-union-store` of redteamAJ.hand_aj5, in capmodel `reggio/REPORTRTAJ.txt`.

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

type Sm4
	var u as Pc
	var n as Num

	static function create(u isolated Pc, n Num) returns Self
		return Self{u: merge u, n: n}
	end 'create'

	export function put(b isolated Bx)
		self.u = Pc.u1(b)
	end 'put'
end 'Sm4'

function main() returns ExitCode
	let s = spawn Sm4.create(Pc.u0, n: 0)
	try s.put(Bx.create(1)) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:26:18: cannot store an isolated 'Bx' into 'Pc.u1.b', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-aj-ipm-msg-iso-match-store -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `ipm-msg-iso-match-store` of redteamAJ.hand_aj5, in capmodel `reggio/REPORTRTAJ.txt`.

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

type Sm5
	var b as Bx
	var n as Num

	static function create(b isolated Bx, n Num) returns Self
		return Self{b: merge b, n: n}
	end 'create'

	export function put(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(bV) then self.b = bV
		end 'x1'
	end 'put'
end 'Sm5'

function main() returns ExitCode
	let s = spawn Sm5.create(Bx.create(0), n: 0)
	try s.put(Pc.u1(Bx.create(1))) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-al-u-unique-result -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `u-unique-result` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

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

	export function takeT(t Tq)
		t.b.n = 9
	end 'takeT'
end 'Stq'

function wrap(t Tq) returns Tq
	return Tq.create(2, b: t.b)
end 'wrap'

function main() returns ExitCode
	let s = spawn Stq.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let r = wrap(q)
	try s.takeT(r) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:58:14: 'r' crosses to the service it is sent to only as the whole of its region, and 'Tq' is not closed: 'Tq.b' is a plain 'Bx' that other code may still name. Declare 'b' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: model-hand-al-u-union-result -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `u-union-result` of redteamAL/hand_al1.txt, in capmodel `reggio/REPORTRTAL.txt`.

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

type Su
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeU(x UQ)
		match x 'x1'
			none then break
			have(t) then t.b.n = 9
		end 'x1'
	end 'takeU'
end 'Su'

function wrapU(t Tq) returns UQ
	return UQ.have(Tq.create(2, b: t.b))
end 'wrapU'

function main() returns ExitCode
	let s = spawn Su.create(0)
	let q = Tq.create(1, b: Bx.create(2))
	let r = wrapU(q)
	try s.takeU(r) otherwise ignore
	q.b.n = 3
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:66:14: 'r' crosses to the service it is sent to only as the whole of its region, and 'UQ' is not closed: 'Tq.b' is a plain 'Bx' that other code may still name. Declare 'b' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: model-hand-an-p1-swap-state-plain-payload -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p1-swap-state-plain-payload` of redteamAN.hand_an3, in capmodel `reggio/REPORTRTAN.txt`.

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

type Ks2
	var u as isolated UP
	var pk as Pb

	static function create(u isolated UP, pk isolated Pb) returns Self
		return Self{u: u, pk: merge pk}
	end 'create'

	export function go()
		match swap self.u with UP.none 'x1'
			none then break
			have(t) then self.pk = t
		end 'x1'
	end 'go'

	export function peek()
		self.pk.n = 1
	end 'peek'
end 'Ks2'

function mkUP() returns isolated UP
	return UP.have(Pb.create(1))
end 'mkUP'

function main() returns ExitCode
	let s = spawn Ks2.create(mkUP(), pk: Pb.create(0))
	try s.go() otherwise ignore
	try s.peek() otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa14-m-isolated-bx-store -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `m-isolated-bx-store` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Zm
	var b as Bx

	static function create(b isolated Bx) returns Self
		return Self{b: merge b}
	end 'create'

	export function put(bV isolated Bx)
		self.b = bV
	end 'put'
end 'Zm'

function main() returns ExitCode
	let r = spawn Zm.create(Bx.create(0))
	let bV = Bx.create(1) as isolated Bx
	try r.put(bV) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:20:12: cannot store an isolated 'Bx' into 'b', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa14-m-factory-param-store -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `m-factory-param-store` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Zm3
	var b as Bx

	static function create(bV isolated Bx) returns Self
		return Self{b: bV}
	end 'create'
end 'Zm3'

function main() returns ExitCode
	let _ = spawn Zm3.create(Bx.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:16:18: cannot store an isolated 'Bx' into 'Zm3.b', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa14-m-store-twice -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `m-store-twice` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Zm2
	var b as Bx
	var c as Bx

	static function create(b isolated Bx, c isolated Bx) returns Self
		return Self{b: merge b, c: merge c}
	end 'create'

	export function put(bV isolated Bx)
		self.b = bV
		self.c = bV
	end 'put'
end 'Zm2'

function main() returns ExitCode
	let r = spawn Zm2.create(Bx.create(0), c: Bx.create(0))
	let bV = Bx.create(1) as isolated Bx
	try r.put(bV) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:21:12: cannot store an isolated 'Bx' into 'b', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa14-m-store-then-read -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `m-store-then-read` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Zm5
	var b as Bx

	static function create(b isolated Bx) returns Self
		return Self{b: merge b}
	end 'create'

	export function put(bV isolated Bx)
		self.b = bV
		takeBx(bV)
	end 'put'
end 'Zm5'

function takeBx(bV isolated Bx)
	let _ = bV.n
end 'takeBx'

function main() returns ExitCode
	let _ = spawn Zm5.create(Bx.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:20:12: cannot store an isolated 'Bx' into 'b', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa14-m-lent-payload -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `m-lent-payload` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

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

type Zm6
	var b as Bx

	static function create(b isolated Bx) returns Self
		return Self{b: merge b}
	end 'create'

	export function put(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(bV) then self.b = bV
		end 'x1'
	end 'put'
end 'Zm6'

function main() returns ExitCode
	let _ = spawn Zm6.create(Bx.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:27:25: cannot store an isolated 'Bx' into 'b', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa14-m-store-into-local-record -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `m-store-into-local-record` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

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

type Zm7
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function put(bV isolated Bx)
		let h = Zp.create(Bx.create(0))
		h.b = bV
	end 'put'
end 'Zm7'

function main() returns ExitCode
	let _ = spawn Zm7.create(0)
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:29:9: cannot store an isolated 'Bx' into 'b', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa14-l-nonlinear-param-send -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `l-nonlinear-param-send` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Zb
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		takeBx(b)
	end 'take'
end 'Zb'

function takeBx(b isolated Bx)
	let _ = b.n
end 'takeBx'

function sendB(b Bx, a Zb.handle)
	try a.take(b) otherwise ignore
end 'sendB'

function main() returns ExitCode
	let a = spawn Zb.create(0)
	sendB(Bx.create(1), a: a)
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:29:13: 'b' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa14-a-mp-store-then-modify -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `a-mp-store-then-modify` of redteamFA14.hand_fa14d, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Zc
	var b as Bx

	static function create(b isolated Bx) returns Self
		return Self{b: merge b}
	end 'create'

	export function put(bV isolated Bx)
		self.b = bV
		self.b.n = 3
	end 'put'
end 'Zc'

function main() returns ExitCode
	let _ = spawn Zc.create(Bx.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:20:12: cannot store an isolated 'Bx' into 'b', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa14-a-mp-store-in-loop -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `a-mp-store-in-loop` of redteamFA14.hand_fa14d, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Zc
	var b as Bx

	static function create(b isolated Bx) returns Self
		return Self{b: merge b}
	end 'create'

	export function put(bV isolated Bx)
		var i = 2 as Num
		while i != 0 'w1'
			self.b = bV
			i = lg(0)
		end 'w1'
	end 'put'
end 'Zc'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Zc.create(Bx.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:22:13: cannot store an isolated 'Bx' into 'b', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa14-a-mp-store-sent-on -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `a-mp-store-sent-on` of redteamFA14.hand_fa14d, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Zm
	var b as Bx

	static function create(b isolated Bx) returns Self
		return Self{b: merge b}
	end 'create'

	export function put(bV isolated Bx)
		self.b = bV
	end 'put'
end 'Zm'

function main() returns ExitCode
	let _ = spawn Zm.create(Bx.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:20:12: cannot store an isolated 'Bx' into 'b', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa15-d8-message-param -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d8-message-param` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

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

type Kz
	var n as Num
	var b as Bx

	static function create(n Num, b isolated Bx) returns Self
		return Self{n: n, b: merge b}
	end 'create'

	export function take(bV isolated Bx)
		takeB(bV)
	end 'take'

	export function eat(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then takeB(o)
		end 'x1'
	end 'eat'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then x.n = 1
			q1(y) then takeB(y)
		end 'x1'
	end 'eatMq'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(bV) then self.b = bV
		end 'x1'
	end 'eatPc'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kz'

function takeB(bV isolated Bx)
	let _ = bV.n
end 'takeB'

function main() returns ExitCode
	let k = spawn Kz.create(0, b: Bx.create(0))
	try k.eat(Ub.u1(Bx.create(1))) otherwise (sendFailure) 'h1'
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

<!-- test: model-hand-fa15-e10-message-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `e10-message-twice` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

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

type Kt
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function eatTwice(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then takeB(o)
		end 'x1'
		match u 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'eatTwice'
end 'Kt'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let k = spawn Kt.create(0)
	try k.eatTwice(Ub.u1(Bx.create(1))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull then break
			serviceStopped then break
		end 'x2'
	end 'h1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:29:9: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-e12-send-after -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `e12-send-after` of redteamFA15.hand_fa15a, in capmodel `reggio/REPORTFA15.txt`.

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

type Kz
	var n as Num
	var b as Bx

	static function create(n Num, b isolated Bx) returns Self
		return Self{n: n, b: merge b}
	end 'create'

	export function take(bV isolated Bx)
		takeB(bV)
	end 'take'

	export function eat(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then takeB(o)
		end 'x1'
	end 'eat'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then x.n = 1
			q1(y) then takeB(y)
		end 'x1'
	end 'eatMq'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(bV) then self.b = bV
		end 'x1'
	end 'eatPc'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kz'

function takeB(bV isolated Bx)
	let _ = bV.n
end 'takeB'

function main() returns ExitCode
	let k = spawn Kz.create(0, b: Bx.create(0))
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	match u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
	try k.eat(u) otherwise (sendFailure) 'h2'
		match sendFailure 'x3'
			mailboxFull then break
			serviceStopped then break
		end 'x3'
	end 'h2'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:76:12: use of moved value 'u': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-ub-let-top-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-let-top-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	match u 'x1'
		u0 then break
		u1(o) then let _ = o.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ub-let-top-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-let-top-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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

<!-- test: model-hand-fa15-m-ub-let-top-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-ub-let-top-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	match u 'x1'
		u0 then break
		u1(o) then takeTwo(o, b: o)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:76:25: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-ub-let-top-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-let-top-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	match u 'x1'
		u0 then break
		u1(o) then o.n = 3
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ub-let-iffy-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-let-iffy-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ub-let-iffy-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-let-iffy-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ub-let-iffy-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-ub-let-iffy-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:81:26: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-ub-let-iffy-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-let-iffy-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Ub.u1(Bx.create(5)) as isolated Ub
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then o.n = 3
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ub-let-loopfresh-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-let-loopfresh-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Ub.u1(Bx.create(5)) as isolated Ub
		match w 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ub-let-loopfresh-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-let-loopfresh-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Ub.u1(Bx.create(5)) as isolated Ub
		match w 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ub-let-loopfresh-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-ub-let-loopfresh-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Ub.u1(Bx.create(5)) as isolated Ub
		match w 'x2'
			u0 then break
			u1(o) then takeTwo(o, b: o)
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:82:26: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-ub-let-loopfresh-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-let-loopfresh-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Ub.u1(Bx.create(5)) as isolated Ub
		match w 'x2'
			u0 then break
			u1(o) then o.n = 3
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ub-param-top-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-param-top-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(u isolated Ub)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		u0 then break
		u1(o) then let _ = o.n
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

<!-- test: model-hand-fa15-m-ub-param-top-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-param-top-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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

function f(u isolated Ub)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
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

<!-- test: model-hand-fa15-m-ub-param-top-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-ub-param-top-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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

function f(u isolated Ub)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		u0 then break
		u1(o) then takeTwo(o, b: o)
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Ub.u1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:75:25: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-ub-param-top-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-param-top-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(u isolated Ub)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		u0 then break
		u1(o) then o.n = 3
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

<!-- test: model-hand-fa15-m-ub-param-iffy-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-param-iffy-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function f(u isolated Ub)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Ub.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ub-param-iffy-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-param-iffy-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function f(u isolated Ub)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Ub.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ub-param-iffy-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-ub-param-iffy-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function f(u isolated Ub)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Ub.u1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:80:26: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-ub-param-iffy-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ub-param-iffy-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function f(u isolated Ub)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then o.n = 3
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Ub.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-let-top-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-let-top-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Mq.q1(Bx.create(5)) as isolated Mq
	match u 'x1'
		q0(xa) then xa.n = 1
		q1(o) then let _ = o.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-let-top-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-let-top-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Mq.q1(Bx.create(5)) as isolated Mq
	match u 'x1'
		q0(xa) then xa.n = 1
		q1(o) then takeB(o)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-let-top-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-mq-let-top-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Mq.q1(Bx.create(5)) as isolated Mq
	match u 'x1'
		q0(xa) then xa.n = 1
		q1(o) then takeTwo(o, b: o)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:76:25: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-mq-let-top-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-let-top-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Mq.q1(Bx.create(5)) as isolated Mq
	match u 'x1'
		q0(xa) then xa.n = 1
		q1(o) then o.n = 3
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-let-iffy-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-let-iffy-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Mq.q1(Bx.create(5)) as isolated Mq
	if lg(1) != 0 'i1'
		match u 'x2'
			q0(xa) then xa.n = 1
			q1(o) then let _ = o.n
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-let-iffy-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-let-iffy-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Mq.q1(Bx.create(5)) as isolated Mq
	if lg(1) != 0 'i1'
		match u 'x2'
			q0(xa) then xa.n = 1
			q1(o) then takeB(o)
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-let-iffy-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-mq-let-iffy-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Mq.q1(Bx.create(5)) as isolated Mq
	if lg(1) != 0 'i1'
		match u 'x2'
			q0(xa) then xa.n = 1
			q1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:81:26: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-mq-let-iffy-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-let-iffy-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Mq.q1(Bx.create(5)) as isolated Mq
	if lg(1) != 0 'i1'
		match u 'x2'
			q0(xa) then xa.n = 1
			q1(o) then o.n = 3
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-let-loopfresh-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-let-loopfresh-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Mq.q1(Bx.create(5)) as isolated Mq
		match w 'x2'
			q0(xa) then xa.n = 1
			q1(o) then let _ = o.n
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-let-loopfresh-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-let-loopfresh-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Mq.q1(Bx.create(5)) as isolated Mq
		match w 'x2'
			q0(xa) then xa.n = 1
			q1(o) then takeB(o)
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-let-loopfresh-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-mq-let-loopfresh-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Mq.q1(Bx.create(5)) as isolated Mq
		match w 'x2'
			q0(xa) then xa.n = 1
			q1(o) then takeTwo(o, b: o)
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:82:26: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-mq-let-loopfresh-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-let-loopfresh-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Mq.q1(Bx.create(5)) as isolated Mq
		match w 'x2'
			q0(xa) then xa.n = 1
			q1(o) then o.n = 3
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-param-top-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-param-top-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(u isolated Mq)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		q0(xa) then xa.n = 1
		q1(o) then let _ = o.n
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Mq.q1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-param-top-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-param-top-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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

function f(u isolated Mq)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		q0(xa) then xa.n = 1
		q1(o) then takeB(o)
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Mq.q1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-param-top-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-mq-param-top-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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

function f(u isolated Mq)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		q0(xa) then xa.n = 1
		q1(o) then takeTwo(o, b: o)
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Mq.q1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:75:25: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-mq-param-top-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-param-top-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(u isolated Mq)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		q0(xa) then xa.n = 1
		q1(o) then o.n = 3
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Mq.q1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-param-iffy-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-param-iffy-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function f(u isolated Mq)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			q0(xa) then xa.n = 1
			q1(o) then let _ = o.n
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Mq.q1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-param-iffy-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-param-iffy-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function f(u isolated Mq)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			q0(xa) then xa.n = 1
			q1(o) then takeB(o)
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Mq.q1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-mq-param-iffy-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-mq-param-iffy-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function f(u isolated Mq)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			q0(xa) then xa.n = 1
			q1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Mq.q1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:80:26: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-mq-param-iffy-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-mq-param-iffy-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function f(u isolated Mq)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			q0(xa) then xa.n = 1
			q1(o) then o.n = 3
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Mq.q1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-let-top-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-let-top-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Pc.u1(Bx.create(5)) as isolated Pc
	match u 'x1'
		u0 then break
		u1(o) then let _ = o.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-let-top-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-let-top-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Pc.u1(Bx.create(5)) as isolated Pc
	match u 'x1'
		u0 then break
		u1(o) then o.n = 3
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-let-top-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-let-top-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Pc.u1(Bx.create(5)) as isolated Pc
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

<!-- test: model-hand-fa15-m-pc-let-top-readfn -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-let-top-readfn` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Pc.u1(Bx.create(5)) as isolated Pc
	match u 'x1'
		u0 then break
		u1(o) then readB(o)
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-let-top-twice -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `m-pc-let-top-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Pc.u1(Bx.create(5)) as isolated Pc
	match u 'x1'
		u0 then break
		u1(o) then takeTwo(o, b: o)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:76:22: 'o' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa15-m-pc-let-iffy-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-let-iffy-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Pc.u1(Bx.create(5)) as isolated Pc
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-let-iffy-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-let-iffy-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Pc.u1(Bx.create(5)) as isolated Pc
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then o.n = 3
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-let-iffy-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-let-iffy-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Pc.u1(Bx.create(5)) as isolated Pc
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-let-iffy-readfn -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-let-iffy-readfn` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function readB(b Bx)
	let _ = b.n
end 'readB'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Pc.u1(Bx.create(5)) as isolated Pc
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then readB(o)
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-let-iffy-twice -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `m-pc-let-iffy-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Pc.u1(Bx.create(5)) as isolated Pc
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:81:23: 'o' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa15-m-pc-let-loopfresh-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-let-loopfresh-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Pc.u1(Bx.create(5)) as isolated Pc
		match w 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-let-loopfresh-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-let-loopfresh-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Pc.u1(Bx.create(5)) as isolated Pc
		match w 'x2'
			u0 then break
			u1(o) then o.n = 3
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-let-loopfresh-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-let-loopfresh-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Pc.u1(Bx.create(5)) as isolated Pc
		match w 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-let-loopfresh-readfn -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-let-loopfresh-readfn` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function readB(b Bx)
	let _ = b.n
end 'readB'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Pc.u1(Bx.create(5)) as isolated Pc
		match w 'x2'
			u0 then break
			u1(o) then readB(o)
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-let-loopfresh-twice -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `m-pc-let-loopfresh-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Pc.u1(Bx.create(5)) as isolated Pc
		match w 'x2'
			u0 then break
			u1(o) then takeTwo(o, b: o)
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:82:23: 'o' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa15-m-pc-param-top-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-param-top-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(u isolated Pc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		u0 then break
		u1(o) then let _ = o.n
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Pc.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-param-top-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-param-top-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(u isolated Pc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		u0 then break
		u1(o) then o.n = 3
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Pc.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-param-top-readfn -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-param-top-readfn` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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

function f(u isolated Pc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		u0 then break
		u1(o) then readB(o)
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Pc.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-param-top-twice -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `m-pc-param-top-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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

function f(u isolated Pc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		u0 then break
		u1(o) then takeTwo(o, b: o)
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Pc.u1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:75:22: 'o' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa15-m-pc-param-iffy-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-param-iffy-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function f(u isolated Pc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Pc.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-param-iffy-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-param-iffy-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function f(u isolated Pc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then o.n = 3
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Pc.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-param-iffy-readfn -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-pc-param-iffy-readfn` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function readB(b Bx)
	let _ = b.n
end 'readB'

function f(u isolated Pc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then readB(o)
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Pc.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-pc-param-iffy-twice -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `m-pc-param-iffy-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function f(u isolated Pc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Pc.u1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:80:23: 'o' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: model-hand-fa15-m-ut-let-top-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ut-let-top-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = UT.have(Tk.create(5)) as isolated UT
	match u 'x1'
		none then break
		have(o) then let _ = o.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ut-let-top-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ut-let-top-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = UT.have(Tk.create(5)) as isolated UT
	match u 'x1'
		none then break
		have(o) then o.n = 3
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ut-let-iffy-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ut-let-iffy-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = UT.have(Tk.create(5)) as isolated UT
	if lg(1) != 0 'i1'
		match u 'x2'
			none then break
			have(o) then let _ = o.n
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ut-let-iffy-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ut-let-iffy-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = UT.have(Tk.create(5)) as isolated UT
	if lg(1) != 0 'i1'
		match u 'x2'
			none then break
			have(o) then o.n = 3
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ut-let-loopfresh-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ut-let-loopfresh-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = UT.have(Tk.create(5)) as isolated UT
		match w 'x2'
			none then break
			have(o) then let _ = o.n
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ut-let-loopfresh-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ut-let-loopfresh-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = UT.have(Tk.create(5)) as isolated UT
		match w 'x2'
			none then break
			have(o) then o.n = 3
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ut-param-top-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ut-param-top-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(u isolated UT)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		none then break
		have(o) then let _ = o.n
	end 'x1'
end 'f'

function main() returns ExitCode
	f(UT.have(Tk.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ut-param-top-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ut-param-top-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(u isolated UT)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		none then break
		have(o) then o.n = 3
	end 'x1'
end 'f'

function main() returns ExitCode
	f(UT.have(Tk.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ut-param-iffy-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ut-param-iffy-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function f(u isolated UT)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			none then break
			have(o) then let _ = o.n
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(UT.have(Tk.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-ut-param-iffy-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-ut-param-iffy-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function f(u isolated UT)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			none then break
			have(o) then o.n = 3
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(UT.have(Tk.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-let-top-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-let-top-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Vc.u1(Bx.create(5)) as isolated Vc
	match u 'x1'
		u0 then break
		u1(o) then let _ = o.n
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-let-top-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-let-top-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Vc.u1(Bx.create(5)) as isolated Vc
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

<!-- test: model-hand-fa15-m-vc-let-top-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-vc-let-top-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Vc.u1(Bx.create(5)) as isolated Vc
	match u 'x1'
		u0 then break
		u1(o) then takeTwo(o, b: o)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:81:25: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-vc-let-top-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-let-top-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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
	let u = Vc.u1(Bx.create(5)) as isolated Vc
	match u 'x1'
		u0 then break
		u1(o) then o.n = 3
	end 'x1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-let-iffy-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-let-iffy-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Vc.u1(Bx.create(5)) as isolated Vc
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-let-iffy-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-let-iffy-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Vc.u1(Bx.create(5)) as isolated Vc
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-let-iffy-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-vc-let-iffy-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Vc.u1(Bx.create(5)) as isolated Vc
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:86:26: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-vc-let-iffy-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-let-iffy-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	let u = Vc.u1(Bx.create(5)) as isolated Vc
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then o.n = 3
		end 'x2'
	end 'i1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-let-loopfresh-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-let-loopfresh-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Vc.u1(Bx.create(5)) as isolated Vc
		match w 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-let-loopfresh-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-let-loopfresh-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Vc.u1(Bx.create(5)) as isolated Vc
		match w 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-let-loopfresh-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-vc-let-loopfresh-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Vc.u1(Bx.create(5)) as isolated Vc
		match w 'x2'
			u0 then break
			u1(o) then takeTwo(o, b: o)
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:87:26: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-vc-let-loopfresh-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-let-loopfresh-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function main() returns ExitCode
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	var i = 2 as Num
	while i != 0 'w1'
		let w = Vc.u1(Bx.create(5)) as isolated Vc
		match w 'x2'
			u0 then break
			u1(o) then o.n = 3
		end 'x2'
		i = lg(0)
	end 'w1'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-param-top-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-param-top-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(u isolated Vc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		u0 then break
		u1(o) then let _ = o.n
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Vc.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-param-top-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-param-top-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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

function f(u isolated Vc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		u0 then break
		u1(o) then takeB(o)
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Vc.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-param-top-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-vc-param-top-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
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

function f(u isolated Vc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		u0 then break
		u1(o) then takeTwo(o, b: o)
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Vc.u1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:80:25: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-vc-param-top-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-param-top-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function f(u isolated Vc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	match u 'x1'
		u0 then break
		u1(o) then o.n = 3
	end 'x1'
end 'f'

function main() returns ExitCode
	f(Vc.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-param-iffy-use -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-param-iffy-use` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function f(u isolated Vc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then let _ = o.n
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Vc.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-param-iffy-take -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-param-iffy-take` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeB(b isolated Bx)
	let _ = b.n
end 'takeB'

function f(u isolated Vc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then takeB(o)
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Vc.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-m-vc-param-iffy-twice -->
The model (`core-m3w-fa24`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `m-vc-param-iffy-twice` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function takeTwo(a isolated Bx, b isolated Bx)
	let _ = b
	let _ = a.n
end 'takeTwo'

function f(u isolated Vc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then takeTwo(o, b: o)
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Vc.u1(Bx.create(5)))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:85:26: use of moved value 'o': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa15-m-vc-param-iffy-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-vc-param-iffy-write` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function lg(x Num) returns Num
	return x
end 'lg'

function f(u isolated Vc)
	let _ = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	if lg(1) != 0 'i1'
		match u 'x2'
			u0 then break
			u1(o) then o.n = 3
		end 'x2'
	end 'i1'
end 'f'

function main() returns ExitCode
	f(Vc.u1(Bx.create(5)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa15-b-ub-state -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `b-ub-state` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function main() returns ExitCode
	let k = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	try k.eatUb(Ub.u1(Bx.create(1))) otherwise (sendFailure) 'h1'
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

<!-- test: model-hand-fa15-b-pc-state -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `b-pc-state` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function main() returns ExitCode
	let k = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	try k.eatPc(Pc.u1(Bx.create(1))) otherwise (sendFailure) 'h1'
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

<!-- test: model-hand-fa15-b-mq-state -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `b-mq-state` of redteamFA15.hand_fa15b, in capmodel `reggio/REPORTFA15.txt`.

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

	export function eatUb(u isolated Ub)
		match u 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'eatUb'

	export function eatPc(u isolated Pc)
		match u 'x1'
			u0 then break
			u1(o) then self.keep = o
		end 'x1'
	end 'eatPc'

	export function eatMq(u isolated Mq)
		match u 'x1'
			q0(x) then self.keep = x
			q1(y) then self.item = y
		end 'x1'
	end 'eatMq'

	export function plain() returns Num
		return 5
	end 'plain'
end 'Kw'

function main() returns ExitCode
	let k = spawn Kw.create(0, keep: Bx.create(0), item: Bx.create(0))
	try k.eatMq(Mq.q1(Bx.create(1))) otherwise (sendFailure) 'h1'
		match sendFailure 'x2'
			mailboxFull then break
			serviceStopped then break
		end 'x2'
	end 'h1'
	try k.eatMq(Mq.q0(Bx.create(2))) otherwise (sendFailure) 'h3'
		match sendFailure 'x4'
			mailboxFull then break
			serviceStopped then break
		end 'x4'
	end 'h3'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-r-message -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r-message` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Sr
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'

	export function tk(t Tk)
		let u = t
		u.n = 2
	end 'tk'
end 'Sr'

function main() returns ExitCode
	let s = spawn Sr.create(0)
	try s.tk(Tk.create(1)) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-r-send -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `r-send` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

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

type Sr
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'

	export function tk(t Tk)
		let u = t
		u.n = 2
	end 'tk'
end 'Sr'

function f(t Tk, h Kp)
	let _ = h
	let u = t
	let s = spawn Sr.create(0)
	try s.tk(u) otherwise ignore
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
error E3214: <fragment>:37:11: cannot hand 'u' to argument `t` of the message `Sr.tk`: it keeps a unique value, and 'u' is only borrowed by this code, and only the code that owns a unique value hands it to another green thread; move it in from its owner
```

<!-- test: model-hand-fa17-w-send -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w-send` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

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

type Sy3
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'

	export function take(x UT)
		let _ = x
		let _ = 1 as Num
	end 'take'
end 'Sy3'

function main() returns ExitCode
	let u = UT.have(Tk.create(1))
	let s = spawn Sy3.create(0)
	match u 'x1'
		none then break
		have then try s.take(u) otherwise ignore
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:35:25: cannot move or copy the unique value matched by 'u' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```

<!-- test: model-hand-fa17-w-spawn -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `w-spawn` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

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

type Sk3
	var u as UT

	static function create(x UT) returns Self
		return Self{u: x}
	end 'create'
end 'Sk3'

function main() returns ExitCode
	let uV = UT.have(Tk.create(1))
	match uV 'x1'
		none then break
		have then let _ = spawn Sk3.create(uV)
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:29:40: cannot move or copy the unique value matched by 'uV' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```

<!-- test: model-hand-fa17-i-msg-store -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `i-msg-store` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

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

type Kq
	var keep as Bx
	var kt as Tk
	var item as isolated Bx

	static function create(keep isolated Bx, kt isolated Tk, item isolated Bx) returns Self
		return Self{keep: merge keep, kt: merge kt, item: item}
	end 'create'

	export function put(b isolated Bx)
		self.keep = b
	end 'put'

	export function putM(b isolated Bx)
		self.keep = merge b
	end 'putM'

	export function putT(t Tk)
		self.kt = t
	end 'putT'

	export function putI(b isolated Bx)
		self.item = b
	end 'putI'
end 'Kq'

function main() returns ExitCode
	let k = spawn Kq.create(Bx.create(0), kt: Tk.create(0), item: Bx.create(1))
	try k.put(Bx.create(1)) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:30:15: cannot store an isolated 'Bx' into 'keep', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa17-i-msg-merge -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `i-msg-merge` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

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

type Kq
	var keep as Bx
	var kt as Tk
	var item as isolated Bx

	static function create(keep isolated Bx, kt isolated Tk, item isolated Bx) returns Self
		return Self{keep: merge keep, kt: merge kt, item: item}
	end 'create'

	export function putM(b isolated Bx)
		self.keep = merge b
	end 'putM'

	export function putT(t Tk)
		self.kt = t
	end 'putT'

	export function putI(b isolated Bx)
		self.item = b
	end 'putI'
end 'Kq'

function main() returns ExitCode
	let k = spawn Kq.create(Bx.create(0), kt: Tk.create(0), item: Bx.create(1))
	try k.putM(Bx.create(1)) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-i-msg-unique-noword -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `i-msg-unique-noword` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

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

type Kq
	var keep as Bx
	var kt as Tk
	var item as isolated Bx

	static function create(keep isolated Bx, kt isolated Tk, item isolated Bx) returns Self
		return Self{keep: merge keep, kt: merge kt, item: item}
	end 'create'

	export function putM(b isolated Bx)
		self.keep = merge b
	end 'putM'

	export function putT(t Tk)
		self.kt = t
	end 'putT'

	export function putI(b isolated Bx)
		self.item = b
	end 'putI'
end 'Kq'

function main() returns ExitCode
	let k = spawn Kq.create(Bx.create(0), kt: Tk.create(0), item: Bx.create(1))
	try k.putT(Tk.create(1)) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-i-msg-iso-slot -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `i-msg-iso-slot` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

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

type Kq
	var keep as Bx
	var kt as Tk
	var item as isolated Bx

	static function create(keep isolated Bx, kt isolated Tk, item isolated Bx) returns Self
		return Self{keep: merge keep, kt: merge kt, item: item}
	end 'create'

	export function putM(b isolated Bx)
		self.keep = merge b
	end 'putM'

	export function putT(t Tk)
		self.kt = t
	end 'putT'

	export function putI(b isolated Bx)
		self.item = b
	end 'putI'
end 'Kq'

function main() returns ExitCode
	let k = spawn Kq.create(Bx.create(0), kt: Tk.create(0), item: Bx.create(1))
	try k.putI(Bx.create(1)) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-i-factory-store -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `i-factory-store` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Kf
	var keep as Bx

	static function create(b isolated Bx) returns Self
		return Self{keep: b}
	end 'create'
end 'Kf'

function main() returns ExitCode
	let _ = spawn Kf.create(Bx.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:16:21: cannot store an isolated 'Bx' into 'Kf.keep', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa17-i-factory-merge -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `i-factory-merge` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Kf
	var keep as Bx

	static function create(b isolated Bx) returns Self
		return Self{keep: merge b}
	end 'create'
end 'Kf'

function main() returns ExitCode
	let _ = spawn Kf.create(Bx.create(1))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa17-i-msg-local -->
The model (`core-m3w-fa24`) refuses it by `C-STORE`; the compiler refuses it with E3196. Model program `i-msg-local` of redteamFA17.hand_fa17a, in capmodel `reggio/REPORTFA17.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Kl
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function put(b isolated Bx)
		var x = Bx.create(0)
		x = b
	end 'put'
end 'Kl'

function main() returns ExitCode
	let k = spawn Kl.create(0)
	try k.put(Bx.create(1)) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:21:7: cannot store an isolated 'Bx' into 'x', which holds a mutable 'Bx'; `merge` it first
```

<!-- test: model-hand-fa19-arm-local-holder-to-writer -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `arm-local-holder-to-writer` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

function wrQ(x UQ)
	match x 'x1'
		none then break
		have(z) then z.n = 0
	end 'x1'
end 'wrQ'

function main() returns ExitCode
	let o = Ko.create(Bx.create(0), n: 0)
	let _ = spawn Sac.create(Bx.create(0), n: 0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then break
		have then wrQ(u)
	end 'x1'
	o.n = 1
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-arm-param-holder-to-writer -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `arm-param-holder-to-writer` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

function wrQ(x UQ)
	match x 'x1'
		none then break
		have(z) then z.n = 0
	end 'x1'
end 'wrQ'

function f(u UQ, o Ko, s Sac.handle)
	let _ = o
	let _ = s
	match u 'x1'
		none then break
		have then wrQ(u)
	end 'x1'
end 'f'

function main() returns ExitCode
	let o = Ko.create(Bx.create(0), n: 0)
	let s = spawn Sac.create(Bx.create(0), n: 0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	f(u, o: o, s: s)
	o.n = 1
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-arm-field-holder-to-writer -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `arm-field-holder-to-writer` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Hq is unique
	var u as UQ
	var xs as TqArray
	var n as Num

	static function create(u UQ, xs TqArray, n Num) returns Self
		return Self{u: u, xs: xs, n: n}
	end 'create'
end 'Hq'

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

function wrQ(x UQ)
	match x 'x1'
		none then break
		have(z) then z.n = 0
	end 'x1'
end 'wrQ'

function main() returns ExitCode
	let o = Ko.create(Bx.create(0), n: 0)
	let _ = spawn Sac.create(Bx.create(0), n: 0)
	let h = Hq.create(UQ.have(Tq.create(1, b: Bx.create(2))), xs: TqArray.create(0), n: 0)
	match h.u 'x1'
		none then break
		have then wrQ(h.u)
	end 'x1'
	o.n = 1
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-arm-state-holder-to-writer -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `arm-state-holder-to-writer` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

type Sv
	var u as UQ
	var k as Num

	static function create(u isolated UQ, k Num) returns Self
		return Self{u: merge u, k: k}
	end 'create'

	export function go(s Sac.handle) returns Num
		let _ = s
		let o = Ko.create(Bx.create(0), n: 0)
		match self.u 'x1'
			none then break
			have then wrQ(self.u)
		end 'x1'
		o.n = 1
		return 1
	end 'go'
end 'Sv'

function wrQ(x UQ)
	match x 'x1'
		none then break
		have(z) then z.n = 0
	end 'x1'
end 'wrQ'

function main() returns ExitCode
	let s = spawn Sac.create(Bx.create(0), n: 0)
	let v = spawn Sv.create(UQ.have(Tq.create(1, b: Bx.create(2))), k: 0)
	let r = try v.go(s.clone()) otherwise panic("send failed")
	let _ = await r
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-for-local-outer-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `for-local-outer-write` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

function main() returns ExitCode
	let o = Ko.create(Bx.create(0), n: 0)
	let _ = spawn Sac.create(Bx.create(0), n: 0)
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	for t in xs 'f1'
		o.n = 5
		t.n = 2
	end 'f1'
	o.n = 1
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-for-local-outer-call -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `for-local-outer-call` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

function wrK(k Ko)
	k.n = 5
end 'wrK'

function main() returns ExitCode
	let o = Ko.create(Bx.create(0), n: 0)
	let _ = spawn Sac.create(Bx.create(0), n: 0)
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	for t in xs 'f1'
		wrK(o)
		t.n = 2
	end 'f1'
	wrK(o)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-for-local-plain-part-stored -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `for-local-plain-part-stored` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

function main() returns ExitCode
	let o = Ko.create(Bx.create(0), n: 0)
	let _ = spawn Sac.create(Bx.create(0), n: 0)
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	for t in xs 'f1'
		o.keep = t.b
		t.b.n = 3
	end 'f1'
	o.keep.n = 7
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-for-param-outer-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `for-param-outer-write` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

function f(xs TqArray, o Ko, s Sac.handle)
	let _ = s
	for t in xs 'f1'
		o.n = 5
		t.n = 2
	end 'f1'
end 'f'

function main() returns ExitCode
	let o = Ko.create(Bx.create(0), n: 0)
	let s = spawn Sac.create(Bx.create(0), n: 0)
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	f(xs, o: o, s: s)
	o.n = 1
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-for-param-outer-call -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `for-param-outer-call` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

function wrK(k Ko)
	k.n = 5
end 'wrK'

function f(xs TqArray, o Ko, s Sac.handle)
	let _ = s
	for t in xs 'f1'
		wrK(o)
		t.n = 2
	end 'f1'
end 'f'

function main() returns ExitCode
	let o = Ko.create(Bx.create(0), n: 0)
	let s = spawn Sac.create(Bx.create(0), n: 0)
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	f(xs, o: o, s: s)
	wrK(o)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-for-param-plain-part-stored -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `for-param-plain-part-stored` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

function f(xs TqArray, o Ko, s Sac.handle)
	let _ = s
	for t in xs 'f1'
		o.keep = t.b
		t.b.n = 3
	end 'f1'
end 'f'

function main() returns ExitCode
	let o = Ko.create(Bx.create(0), n: 0)
	let s = spawn Sac.create(Bx.create(0), n: 0)
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	f(xs, o: o, s: s)
	o.keep.n = 7
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-for-field-outer-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `for-field-outer-write` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Hq is unique
	var u as UQ
	var xs as TqArray
	var n as Num

	static function create(u UQ, xs TqArray, n Num) returns Self
		return Self{u: u, xs: xs, n: n}
	end 'create'
end 'Hq'

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

function main() returns ExitCode
	let o = Ko.create(Bx.create(0), n: 0)
	let _ = spawn Sac.create(Bx.create(0), n: 0)
	let h = Hq.create(UQ.none, xs: TqArray.create(0), n: 0)
	h.xs.push(Tq.create(1, b: Bx.create(2)))
	for t in h.xs 'f1'
		o.n = 5
		t.n = 2
	end 'f1'
	o.n = 1
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-for-field-outer-call -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `for-field-outer-call` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Hq is unique
	var u as UQ
	var xs as TqArray
	var n as Num

	static function create(u UQ, xs TqArray, n Num) returns Self
		return Self{u: u, xs: xs, n: n}
	end 'create'
end 'Hq'

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

function wrK(k Ko)
	k.n = 5
end 'wrK'

function main() returns ExitCode
	let o = Ko.create(Bx.create(0), n: 0)
	let _ = spawn Sac.create(Bx.create(0), n: 0)
	let h = Hq.create(UQ.none, xs: TqArray.create(0), n: 0)
	h.xs.push(Tq.create(1, b: Bx.create(2)))
	for t in h.xs 'f1'
		wrK(o)
		t.n = 2
	end 'f1'
	wrK(o)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-for-field-plain-part-stored -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `for-field-plain-part-stored` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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
end 'Tq'

type Ko
	var keep as Bx
	var n as Num

	static function create(keep Bx, n Num) returns Self
		return Self{keep: keep, n: n}
	end 'create'
end 'Ko'

type Hq is unique
	var u as UQ
	var xs as TqArray
	var n as Num

	static function create(u UQ, xs TqArray, n Num) returns Self
		return Self{u: u, xs: xs, n: n}
	end 'create'
end 'Hq'

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

function main() returns ExitCode
	let o = Ko.create(Bx.create(0), n: 0)
	let _ = spawn Sac.create(Bx.create(0), n: 0)
	let h = Hq.create(UQ.none, xs: TqArray.create(0), n: 0)
	h.xs.push(Tq.create(1, b: Bx.create(2)))
	for t in h.xs 'f1'
		o.keep = t.b
		t.b.n = 3
	end 'f1'
	o.keep.n = 7
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-for-state-outer-write -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `for-state-outer-write` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

type Sv
	var xs as TqArray
	var k as Num

	static function create(xs isolated TqArray, k Num) returns Self
		return Self{xs: merge xs, k: k}
	end 'create'

	export function add()
		self.xs.push(Tq.create(1, b: Bx.create(2)))
	end 'add'

	export function go(s Sac.handle) returns Num
		let _ = s
		let o = Ko.create(Bx.create(0), n: 0)
		for t in self.xs 'f1'
			o.n = 5
			t.n = 2
		end 'f1'
		o.n = 1
		return 1
	end 'go'
end 'Sv'

function main() returns ExitCode
	let s = spawn Sac.create(Bx.create(0), n: 0)
	let v = spawn Sv.create(TqArray.create(0), k: 0)
	try v.add() otherwise ignore
	let r = try v.go(s.clone()) otherwise panic("send failed")
	let _ = await r
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-for-state-outer-call -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `for-state-outer-call` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

type Sv
	var xs as TqArray
	var k as Num

	static function create(xs isolated TqArray, k Num) returns Self
		return Self{xs: merge xs, k: k}
	end 'create'

	export function add()
		self.xs.push(Tq.create(1, b: Bx.create(2)))
	end 'add'

	export function go(s Sac.handle) returns Num
		let _ = s
		let o = Ko.create(Bx.create(0), n: 0)
		for t in self.xs 'f1'
			wrK(o)
			t.n = 2
		end 'f1'
		wrK(o)
		return 1
	end 'go'
end 'Sv'

function wrK(kV Ko)
	kV.n = 5
end 'wrK'

function main() returns ExitCode
	let s = spawn Sac.create(Bx.create(0), n: 0)
	let v = spawn Sv.create(TqArray.create(0), k: 0)
	try v.add() otherwise ignore
	let r = try v.go(s.clone()) otherwise panic("send failed")
	let _ = await r
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-for-state-plain-part-stored -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `for-state-plain-part-stored` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sac
	var keep as Bx
	var n as Num

	static function create(keep isolated Bx, n Num) returns Self
		return Self{keep: merge keep, n: n}
	end 'create'
end 'Sac'

type Sv
	var xs as TqArray
	var k as Num

	static function create(xs isolated TqArray, k Num) returns Self
		return Self{xs: merge xs, k: k}
	end 'create'

	export function add()
		self.xs.push(Tq.create(1, b: Bx.create(2)))
	end 'add'

	export function go(s Sac.handle) returns Num
		let _ = s
		let o = Ko.create(Bx.create(0), n: 0)
		for t in self.xs 'f1'
			o.keep = t.b
			t.b.n = 3
		end 'f1'
		o.keep.n = 7
		return 1
	end 'go'
end 'Sv'

function main() returns ExitCode
	let s = spawn Sac.create(Bx.create(0), n: 0)
	let v = spawn Sv.create(TqArray.create(0), k: 0)
	try v.add() otherwise ignore
	let r = try v.go(s.clone()) otherwise panic("send failed")
	let _ = await r
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa19-x-arm-stored-then-holder-sent -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-arm-stored-then-holder-sent` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Squ
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeU(x UQ)
		match x 'x1'
			none then break
			have(t) then t.b.n = 9
		end 'x1'
	end 'takeU'
end 'Squ'

function main() returns ExitCode
	let s = spawn Squ.create(0)
	let o = Ko.create(Bx.create(0), n: 0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then break
		have(t) then o.keep = t.b
	end 'x1'
	try s.takeU(u) otherwise ignore
	o.keep.n = 7
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:58:14: 'u' crosses to the service it is sent to only as the whole of its region, and 'UQ' is not closed: 'Tq.b' is a plain 'Bx' that other code may still name. Declare 'b' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: model-hand-fa19-x-arm-outer-into-binding-then-sent -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-arm-outer-into-binding-then-sent` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Squ
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeU(x UQ)
		match x 'x1'
			none then break
			have(t) then t.b.n = 9
		end 'x1'
	end 'takeU'
end 'Squ'

function main() returns ExitCode
	let s = spawn Squ.create(0)
	let o = Bx.create(5)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then break
		have(t) then t.b = o
	end 'x1'
	try s.takeU(u) otherwise ignore
	o.n = 1
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:49:14: 'u' crosses to the service it is sent to only as the whole of its region, and 'UQ' is not closed: 'Tq.b' is a plain 'Bx' that other code may still name. Declare 'b' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: model-hand-fa19-x-arm-param-then-sent -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-arm-param-then-sent` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Squ
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeU(x UQ)
		match x 'x1'
			none then break
			have(t) then t.b.n = 9
		end 'x1'
	end 'takeU'
end 'Squ'

function stash(u UQ, o Ko)
	match u 'x1'
		none then break
		have(t) then o.keep = t.b
	end 'x1'
end 'stash'

function main() returns ExitCode
	let s = spawn Squ.create(0)
	let o = Ko.create(Bx.create(0), n: 0)
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	stash(u, o: o)
	try s.takeU(u) otherwise ignore
	o.keep.n = 7
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:62:14: 'u' crosses to the service it is sent to only as the whole of its region, and 'UQ' is not closed: 'Tq.b' is a plain 'Bx' that other code may still name. Declare 'b' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: model-hand-fa19-x-arm-callee-local-holder-returned -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-arm-callee-local-holder-returned` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Squ
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeU(x UQ)
		match x 'x1'
			none then break
			have(t) then t.b.n = 9
		end 'x1'
	end 'takeU'
end 'Squ'

function stash(o Ko) returns UQ
	let u = UQ.have(Tq.create(1, b: Bx.create(2)))
	match u 'x1'
		none then break
		have(t) then o.keep = t.b
	end 'x1'
	return u
end 'stash'

function main() returns ExitCode
	let s = spawn Squ.create(0)
	let o = Ko.create(Bx.create(0), n: 0)
	let u = stash(o)
	try s.takeU(u) otherwise ignore
	o.keep.n = 7
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:63:14: 'u' crosses to the service it is sent to only as the whole of its region, and 'UQ' is not closed: 'Tq.b' is a plain 'Bx' that other code may still name. Declare 'b' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: model-hand-fa19-x-for-stored-then-holder-sent -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-for-stored-then-holder-sent` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sqa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(x TqArray)
		for e in x 'f1'
			e.b.n = 9
		end 'f1'
	end 'takeA'
end 'Sqa'

function main() returns ExitCode
	let s = spawn Sqa.create(0)
	let o = Ko.create(Bx.create(0), n: 0)
	let xs = TqArray.create(0)
	xs.push(Tq.create(1, b: Bx.create(2)))
	for t in xs 'f1'
		o.keep = t.b
	end 'f1'
	try s.takeA(xs) otherwise ignore
	o.keep.n = 7
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:53:14: 'xs' crosses to the service it is sent to only as the whole of its region, and 'TqArray' is not closed: 'Tq.b' is a plain 'Bx' that other code may still name. Declare 'b' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: model-hand-fa19-x-for-callee-container-returned -->
The model (`core-m3w-fa24`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `x-for-callee-container-returned` of redteamFA19.hand_fa19, in capmodel `reggio/REPORTFA19.txt`.

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

type Sqa
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeA(x TqArray)
		for e in x 'f1'
			e.b.n = 9
		end 'f1'
	end 'takeA'
end 'Sqa'

function stash(xs TqArray, o Ko) returns TqArray
	for e in xs 'f1'
		o.keep = e.b
	end 'f1'
	return xs
end 'stash'

function mkA() returns TqArray
	let a = TqArray.create(0)
	a.push(Tq.create(1, b: Bx.create(2)))
	return a
end 'mkA'

function main() returns ExitCode
	let s = spawn Sqa.create(0)
	let o = Ko.create(Bx.create(0), n: 0)
	let xs = stash(mkA(), o: o)
	try s.takeA(xs) otherwise ignore
	o.keep.n = 7
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:62:14: 'xs' crosses to the service it is sent to only as the whole of its region, and 'TqArray' is not closed: 'Tq.b' is a plain 'Bx' that other code may still name. Declare 'b' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: model-hand-fa22-nb-none-self-direct -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-none-self-direct` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

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

type Sv
	var u as UQ
	var k as Num

	static function create(u isolated UQ, k Num) returns Self
		return Self{u: merge u, k: k}
	end 'create'

	function bump()
		self.k = 6
	end 'bump'

	export function go() returns Num
		match self.u 'x1'
			none then self.k = 5
			have then break
		end 'x1'
		return 1
	end 'go'
end 'Sv'

function main() returns ExitCode
	let v = spawn Sv.create(UQ.have(Tq.create(1, b: Bx.create(2))), k: 0)
	let r = try v.go() otherwise panic("send failed")
	let _ = await r
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-have-self-direct -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-have-self-direct` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

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

type Sv
	var u as UQ
	var k as Num

	static function create(u isolated UQ, k Num) returns Self
		return Self{u: merge u, k: k}
	end 'create'

	function bump()
		self.k = 6
	end 'bump'

	export function go() returns Num
		match self.u 'x1'
			none then break
			have then self.k = 5
		end 'x1'
		return 1
	end 'go'
end 'Sv'

function main() returns ExitCode
	let v = spawn Sv.create(UQ.have(Tq.create(1, b: Bx.create(2))), k: 0)
	let r = try v.go() otherwise panic("send failed")
	let _ = await r
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-both-self-direct -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-both-self-direct` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

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

type Sv
	var u as UQ
	var k as Num

	static function create(u isolated UQ, k Num) returns Self
		return Self{u: merge u, k: k}
	end 'create'

	function bump()
		self.k = 6
	end 'bump'

	export function go() returns Num
		match self.u 'x1'
			none then self.k = 5
			have then self.k = 5
		end 'x1'
		return 1
	end 'go'
end 'Sv'

function main() returns ExitCode
	let v = spawn Sv.create(UQ.have(Tq.create(1, b: Bx.create(2))), k: 0)
	let r = try v.go() otherwise panic("send failed")
	let _ = await r
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-none-self-reassign -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-none-self-reassign` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

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

type Sv
	var u as UQ
	var k as Num

	static function create(u isolated UQ, k Num) returns Self
		return Self{u: merge u, k: k}
	end 'create'

	function bump()
		self.k = 6
	end 'bump'

	export function go() returns Num
		match self.u 'x1'
			none then self.u = UQ.none
			have then break
		end 'x1'
		return 1
	end 'go'
end 'Sv'

function main() returns ExitCode
	let v = spawn Sv.create(UQ.have(Tq.create(1, b: Bx.create(2))), k: 0)
	let r = try v.go() otherwise panic("send failed")
	let _ = await r
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-have-self-reassign -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-have-self-reassign` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

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

type Sv
	var u as UQ
	var k as Num

	static function create(u isolated UQ, k Num) returns Self
		return Self{u: merge u, k: k}
	end 'create'

	function bump()
		self.k = 6
	end 'bump'

	export function go() returns Num
		match self.u 'x1'
			none then break
			have then self.u = UQ.none
		end 'x1'
		return 1
	end 'go'
end 'Sv'

function main() returns ExitCode
	let v = spawn Sv.create(UQ.have(Tq.create(1, b: Bx.create(2))), k: 0)
	let r = try v.go() otherwise panic("send failed")
	let _ = await r
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-both-self-reassign -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-both-self-reassign` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

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

type Sv
	var u as UQ
	var k as Num

	static function create(u isolated UQ, k Num) returns Self
		return Self{u: merge u, k: k}
	end 'create'

	function bump()
		self.k = 6
	end 'bump'

	export function go() returns Num
		match self.u 'x1'
			none then self.u = UQ.none
			have then self.u = UQ.none
		end 'x1'
		return 1
	end 'go'
end 'Sv'

function main() returns ExitCode
	let v = spawn Sv.create(UQ.have(Tq.create(1, b: Bx.create(2))), k: 0)
	let r = try v.go() otherwise panic("send failed")
	let _ = await r
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-none-self-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-none-self-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

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

type Sv
	var u as UQ
	var k as Num

	static function create(u isolated UQ, k Num) returns Self
		return Self{u: merge u, k: k}
	end 'create'

	function bump()
		self.k = 6
	end 'bump'

	export function go() returns Num
		match self.u 'x1'
			none then self.bump()
			have then break
		end 'x1'
		return 1
	end 'go'
end 'Sv'

function main() returns ExitCode
	let v = spawn Sv.create(UQ.have(Tq.create(1, b: Bx.create(2))), k: 0)
	let r = try v.go() otherwise panic("send failed")
	let _ = await r
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-have-self-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-have-self-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

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

type Sv
	var u as UQ
	var k as Num

	static function create(u isolated UQ, k Num) returns Self
		return Self{u: merge u, k: k}
	end 'create'

	function bump()
		self.k = 6
	end 'bump'

	export function go() returns Num
		match self.u 'x1'
			none then break
			have then self.bump()
		end 'x1'
		return 1
	end 'go'
end 'Sv'

function main() returns ExitCode
	let v = spawn Sv.create(UQ.have(Tq.create(1, b: Bx.create(2))), k: 0)
	let r = try v.go() otherwise panic("send failed")
	let _ = await r
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa22-nb-both-self-callee -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `nb-both-self-callee` of redteamFA22.hand_fa22, in capmodel `reggio/REPORTFA22.txt`.

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

type Sv
	var u as UQ
	var k as Num

	static function create(u isolated UQ, k Num) returns Self
		return Self{u: merge u, k: k}
	end 'create'

	function bump()
		self.k = 6
	end 'bump'

	export function go() returns Num
		match self.u 'x1'
			none then self.bump()
			have then self.bump()
		end 'x1'
		return 1
	end 'go'
end 'Sv'

function main() returns ExitCode
	let v = spawn Sv.create(UQ.have(Tq.create(1, b: Bx.create(2))), k: 0)
	let r = try v.go() otherwise panic("send failed")
	let _ = await r
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa23-p2-call-stored-state -->
The model (`core-m3w-fa24`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `p2-call-stored-state` of redteamFA23.hand_fa23, in capmodel `reggio/REPORTFA23.txt`.

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

type Kq
	var n as Num
	var item as isolated Bx

	static function create(n Num, item isolated Bx) returns Self
		return Self{n: n, item: item}
	end 'create'

	export function go()
		match mkUb() 'x1'
			u0 then break
			u1(o) then self.item = o
		end 'x1'
	end 'go'
end 'Kq'

function mkUb() returns isolated Ub
	return Ub.u1(Bx.create(1))
end 'mkUb'

function main() returns ExitCode
	let k = spawn Kq.create(0, item: Bx.create(0))
	try k.go() otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```
