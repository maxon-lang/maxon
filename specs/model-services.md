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
the day the compiler stops agreeing with a verdict the model pinned. The rule set is `core-m3w-fa14`.

## Tests

<!-- test: model-finding-z-scc-three-cycle-wait-deep -->
The model (`core-m3w-fa14`) refuses it by `R5-HOLDWAIT`; the compiler refuses it with E3221. Model program `scc-three-cycle-wait-deep` of redteamZ/p8.txt, in capmodel `reggio/REPORTRTZ.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R5-HOLDWAIT`; the compiler refuses it with E3221. Model program `scc-three-cycle-wait-deep-reordered` of redteamZ/p8.txt, in capmodel `reggio/REPORTRTZ.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R5-HOLDWAIT`; the compiler refuses it with E3221. Model program `scc-record-methods` of redteamZ/p8.txt, in capmodel `reggio/REPORTRTZ.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `send-fresh-tk` of redteamAA/p10.txt, in capmodel `reggio/REPORTRTAA.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `send-local-tk` of redteamAA/p10.txt, in capmodel `reggio/REPORTRTAA.txt`.

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

<!-- test: model-finding-aa-send-swap-tk-local-record -->
The model (`core-m3w-fa14`) refuses it by `C-RECOVER`; the compiler refuses it with E3198. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `send-swap-tk-local-record` of redteamAA/p10.txt, in capmodel `reggio/REPORTRTAA.txt`.

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
	let o = swap h.tk with Tk.create(2)
	try r.put(o) otherwise ignore
	return 0
end 'g'

function main() returns ExitCode
	let r = spawn Rc.create(Tk.create(0))
	let _ = g(r)
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:34:10: 'h.tk' is not an isolated field, so there is no region for `swap` to take out
```

<!-- test: model-finding-aa-send-swap-tk-inline -->
The model (`core-m3w-fa14`) refuses it by `C-RECOVER`; the compiler refuses it with E3198. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `send-swap-tk-inline` of redteamAA/p10.txt, in capmodel `reggio/REPORTRTAA.txt`.

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
	try r.put(swap h.tk with Tk.create(2)) otherwise ignore
	return 0
end 'g'

function main() returns ExitCode
	let r = spawn Rc.create(Tk.create(0))
	let _ = g(r)
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:34:12: 'h.tk' is not an isolated field, so there is no region for `swap` to take out
```

<!-- test: model-finding-aa-send-iso-slot-then-write -->
The model (`core-m3w-fa14`) refuses it by `R3-SEND`; the compiler refuses it with E3217. Model program `send-iso-slot-then-write` of redteamAA/p3.txt, in capmodel `reggio/REPORTRTAA.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R3-SEND`; the compiler refuses it with E3217. Model program `spawn-iso-slot-then-write` of redteamAA/p3.txt, in capmodel `reggio/REPORTRTAA.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `send-match-payload-unique` of redteamAA/p9.txt, in capmodel `reggio/REPORTRTAA.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `send-byref-param` of redteamAA/p9.txt, in capmodel `reggio/REPORTRTAA.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `send-record-field-unique` of redteamAA/p9.txt, in capmodel `reggio/REPORTRTAA.txt`.

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

<!-- test: model-finding-aa-send-record-field-unique-swap -->
The model (`core-m3w-fa14`) refuses it by `C-RECOVER`; the compiler refuses it with E3198. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `send-record-field-unique-swap` of redteamAA/p9.txt, in capmodel `reggio/REPORTRTAA.txt`.

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
	try r.put(swap h.tk with Tk.create(2)) otherwise ignore
	try r.put(swap h.tk with Tk.create(3)) otherwise ignore
	return 0
end 'g'

function main() returns ExitCode
	let r = spawn Rc.create(Tk.create(0))
	let _ = g(r)
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:34:12: 'h.tk' is not an isolated field, so there is no region for `swap` to take out
```

<!-- test: model-finding-ad-swap-field-self-state -->
The model (`core-m3w-fa14`) refuses it by `C-ISOREAD`; the compiler refuses it with E3198. Model program `swap-field-self-state` of redteamAD/t3.txt, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `frozen-field-iso-recv-call-waits` of redteamAE/p19.txt, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hx-handle-in-mailbox` of redteamAC.hand_ac12, in capmodel `reggio/REPORTRTAC.txt`.

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
The model (`core-m3w-fa14`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `k-send-iso-via-fn` of redteamAD.hand_ad3, in capmodel `reggio/REPORTRTAD.txt`.

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

<!-- test: model-hand-ad-hc-three-chain -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hc-three-chain` of redteamAD.hand_ad5, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `hc-spawn-in-factory-cycle` of redteamAD.hand_ad5, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `d-spawn-in-factory-chain` of redteamAD.hand_ad8, in capmodel `reggio/REPORTRTAD.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f-plain` of redteamAE.hand_ae3, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R5-HOLDWAIT`; the compiler refuses it with E3221. Model program `h-msg-await` of redteamAE.hand_ae3, in capmodel `reggio/REPORTRTAE.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R5-HOLDWAIT`; the compiler refuses it with E3221. Model program `p3-holder-await-payload-direct` of redteamAF.hand_af11, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `h5-owned-payload-carries-handle` of redteamAF.hand_af7, in capmodel `reggio/REPORTRTAF.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u1-none` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u2-none` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `u3-none` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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

<!-- test: model-hand-ag-u1-copy -->
The model (`core-m3w-fa14`) refuses it by `C-SLOT`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `u1-copy` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
		have then let _ = uV
	end 'x1'
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:89:21: cannot move or copy the unique value matched by 'uV' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```

<!-- test: model-hand-ag-u2-copy -->
The model (`core-m3w-fa14`) refuses it by `C-SLOT`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `u2-copy` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
		have then let _ = uV
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
```maxoncstderr
error E3214: <fragment>:88:21: cannot move or copy the unique value matched by 'uV' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```

<!-- test: model-hand-ag-u3-copy -->
The model (`core-m3w-fa14`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `u3-copy` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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

<!-- test: model-hand-ag-j1-none -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `j1-none` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `j1-inner-read` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `j1-inner-write` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `j1-inner-move` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f1-none` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `f2-none` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `f1-copy` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `f2-copy` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `f1-copy-read` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `f2-copy-read` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `f1-copy-write` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R18-UNIQUE`; the compiler refuses it with E3214. Model program `f2-copy-write` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `f1-call` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `f2-call` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `f1-call-write` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `R4-ARG`; the compiler refuses it with E3207. Model program `f2-call-write` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-RECOVER`; the compiler refuses it with E3102. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f1-send` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-RECOVER`; the compiler refuses it with E3214. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `f2-send` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `f2-assign` of redteamAG.hand_ag6, in capmodel `reggio/REPORTRTAG.txt`.

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

<!-- test: model-hand-fa14-m-unique-tk-store -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `m-unique-tk-store` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

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

function main() returns ExitCode
	let r = spawn Rc.create(Tk.create(0))
	try r.put(Tk.create(1)) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa14-m-store-twice -->
The model (`core-m3w-fa14`) refuses it by `E3102`; the compiler refuses it with E3196. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m-store-twice` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

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
The model (`core-m3w-fa14`) refuses it by `E3102`; the compiler refuses it with E3196. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m-store-then-read` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

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
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3196. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `m-lent-payload` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

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

<!-- test: model-hand-fa14-l-nonlinear-param-send -->
The model (`core-m3w-fa14`) refuses it by `C-RECOVER`; the compiler refuses it with E3200. Model program `l-nonlinear-param-send` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

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

<!-- test: model-hand-fa14-r-refill-then-send -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `r-refill-then-send` of redteamFA14.hand_fa14b, in capmodel `reggio/REPORTFA14.txt`.

```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Zb2
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function take(b isolated Bx)
		takeBx(b)
	end 'take'
end 'Zb2'

function takeBx(b isolated Bx)
	let _ = b.n
end 'takeBx'

function main() returns ExitCode
	let k = spawn Zb2.create(0)
	var w = Bx.create(1)
	w = Bx.create(2)
	try k.take(w) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: model-hand-fa14-o1-plain-send-handler-reads-handle -->
The model (`core-m3w-fa14`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0. Model program `o1-plain-send-handler-reads-handle` of redteamFA14.hand_fa14c, in capmodel `reggio/REPORTFA14.txt`.

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

<!-- test: model-hand-fa14-a-mp-alias-then-store -->
The model (`core-m3w-fa14`) refuses it by `E3102`; the compiler refuses it with E3102. Model program `a-mp-alias-then-store` of redteamFA14.hand_fa14d, in capmodel `reggio/REPORTFA14.txt`.

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
		let _ = bV
		self.b = bV
	end 'put'
end 'Zc'

function main() returns ExitCode
	let _ = spawn Zc.create(Bx.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:21:12: use of moved value 'bV': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: model-hand-fa14-a-mp-store-record-with-part -->
The model (`core-m3w-fa14`) refuses it by `C-WRITE`; the compiler refuses it with E3197. Model program `a-mp-store-record-with-part` of redteamFA14.hand_fa14d, in capmodel `reggio/REPORTFA14.txt`.

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

type Zc
	var w as Wo
	var k as Bx

	static function create(w isolated Wo, k isolated Bx) returns Self
		return Self{w: merge w, k: merge k}
	end 'create'

	export function put(wV isolated Wo)
		self.k = wV.keep
		self.w = wV
	end 'put'
end 'Zc'

function main() returns ExitCode
	let _ = spawn Zc.create(Wo.create(Bx.create(0), item: Bx.create(1)), k: Bx.create(2))
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:30:3: cannot write 'k' through 'self', which is read-only while 'the isolated value' is open
```

<!-- test: model-hand-fa14-a-mp-store-in-loop -->
The model (`core-m3w-fa14`) refuses it by `E3102`; the compiler refuses it with E3196. The model reports the first rule that fails and the compiler every independent error, so the codes differ. Model program `a-mp-store-in-loop` of redteamFA14.hand_fa14d, in capmodel `reggio/REPORTFA14.txt`.

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
