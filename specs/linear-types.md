---
feature: linear-types
status: experimental
keywords: [linear, finalizer, destruct, consuming, unique, move, E3223, E3224, E3102]
category: semantics
---

# Finalizers and linear types

## Documentation

A `destruct self` statement ends the life of the value a method is called on. A method that contains one is a
**finalizer**, and it takes `consuming self`: a call to it moves the receiver, and reading the receiver afterwards is
E3102. A type with at least one finalizer is **linear**, and linear types are `unique`: never cloned, frozen or aliased
(E3214).

A linear value is consumed exactly once on every path out of its scope, throws, early returns and propagating `try`s
included, by a finalizer call or by being moved on (to a function that takes it, into a `return`, a `throw` or a
message). A value dropped, overwritten, rebound while live, discarded with `let _ = r`, or left in a temporary nobody
consumes is E3223. A linear argument always moves, so a function that takes one must consume it.

A linear value lives only in locals, parameters and message arguments. A field, a container or any other type
argument, a tuple item, a closure capture, a promise payload, a message reply, a union stored in a field and an
argument of an `async` call or of an interface or function value call are E3224. No linear value is live across a wait:
an `await`, a send with a `sendTimeout:` or a call to a function that waits (E3223).

A union with a linear payload is linear. A `match` over one consumes it: every arm for a case with a linear payload binds
it to a name and consumes it, and a `default` arm cannot cover such a case. A failed send hands a linear argument back in
`mailboxFull` or `serviceStopped`, and the handler must consume it.

## Tests

<!-- test: a-finalizer-ends-the-value-and-every-branch-consumes-it -->
`fin` is a finalizer: it takes `consuming self`, and `destruct self` ends the value. Every path of `branches` and `viaReturn` consumes its `Resp` once, by a finalizer call or by handing it to a function that takes it.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function use(r Resp)
	print("use {r.peek()}\n")
	r.fin()
end 'use'

function branches(r Resp, flag bool)
	if flag 'a'
		r.fin()
	end 'a' else 'b'
		use(r)
	end 'b'
end 'branches'

function viaReturn(r Resp, flag bool) returns Code
	if flag 'a'
		let c = r.peek()
		r.fin()
		return c
	end 'a'

	r.fin()
	return 0
end 'viaReturn'

function main() returns ExitCode
	let r = Resp.create(1)
	print("peek {r.peek()}\n")
	r.fin()

	let q = Resp.create(2)
	use(q)

	branches(Resp.create(3), flag: true)
	branches(Resp.create(4), flag: false)
	print("{viaReturn(Resp.create(5), flag: true)}\n")
	return 0
end 'main'
```
```stdout
peek 1
fin 1
use 2
fin 2
fin 3
use 4
fin 4
fin 5
5
```
```exitcode
0
```

<!-- test: a-linear-value-moves-through-calls-returns-and-a-loop-body -->
A function that returns a linear value hands it on, and a loop body that makes and consumes one per iteration is accepted.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function make(code Code) returns Resp
	return Resp.create(code)
end 'make'

function pass(r Resp) returns Resp
	return r
end 'pass'

function main() returns ExitCode
	let a = make(1)
	let b = pass(a)
	b.fin()

	var count = 0 as Code

	while count < 2 'loop'
		let r = make(count)
		r.fin()
		count = count + 1
	end 'loop'

	return 0
end 'main'
```
```stdout
fin 1
fin 0
fin 1
```
```exitcode
0
```

<!-- test: a-handler-consumes-the-value-a-try-body-refilled -->
`r` is consumed before the block, refilled inside it, and the handler sees the refill: the throwing `thrv(0)` comes after the refill, so the handler consumes the second `Resp`.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

enum Oops implements Error
	bad
end 'Oops'

function thrv(c Code) returns Code throws Oops
	if c == 0 'zero'
		throw Oops.bad
	end 'zero'

	return c
end 'thrv'

function f()
	var r = Resp.create(1)
	r.fin()

	try 'blk'
		r = Resp.create(2)
		let c = thrv(0)
		print("{c}\n")
	end 'blk'
	otherwise (e) 'h'
		match e 'k'
			bad then r.fin()
		end 'k'

		return
	end 'h'

	r.fin()
end 'f'

function main() returns ExitCode
	f()
	return 0
end 'main'
```
```stdout
fin 1
fin 2
```
```exitcode
0
```

<!-- test: operands-bound-before-the-try-let-a-send-hand-the-value-back -->
The throwing operand is bound before the `Resp` exists and before the `try`, so no linear value is live at a throw point.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

enum Oops implements Error
	bad
end 'Oops'

function thrv(c Code) returns Code throws Oops
	if c == 0 'zero'
		throw Oops.bad
	end 'zero'

	return c
end 'thrv'

type Sink
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(r Resp, extra Code)
		self.n = extra
		r.fin()
	end 'take'
end 'Sink'

function send(s Sink.handle) throws Oops
	let extra = try thrv(1)
	let r = Resp.create(extra)

	try s.take(r, extra: extra) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(back, _) then back.fin()
			serviceStopped(back, _) then back.fin()
		end 'why'
	end 'refused'
end 'send'

function main() returns ExitCode
	let s = spawn Sink.create()
	try send(s) otherwise ignore
	return 0
end 'main'
```
```stdout
fin 1
```
```exitcode
0
```

<!-- test: a-failed-send-hands-a-linear-argument-back-to-the-handler -->
The send queues `r`, which the service consumes. A failed send would carry it back in `mailboxFull` or `serviceStopped`, and each arm of the handler consumes it.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Sink
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(r Resp)
		self.n = self.n + 1
		r.fin()
	end 'take'
end 'Sink'

function main() returns ExitCode
	let s = spawn Sink.create()
	let r = Resp.create(1)

	try s.take(r) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(back) then back.fin()
			serviceStopped(back) then back.fin()
		end 'why'
	end 'refused'

	return 0
end 'main'
```
```stdout
fin 1
```
```exitcode
0
```

<!-- test: a-linear-error-carries-its-value-to-the-handler -->
A union that carries a linear value is linear and may implement `Error`. `throw` and a propagating `try` move the value on, and the handler's `match` binds and consumes it.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Back implements Error
	refused(r Resp)
end 'Back'

function trySend(r Resp, flag bool) throws Back
	if flag 'f'
		throw Back.refused(r)
	end 'f'

	r.fin()
end 'trySend'

function viaPropagate(r Resp, flag bool) throws Back
	try trySend(r, flag: flag)
end 'viaPropagate'

function main() returns ExitCode
	let r = Resp.create(1)

	try trySend(r, flag: true) otherwise (e) 'h'
		match e 'k'
			refused(back) then back.fin()
		end 'k'
	end 'h'

	let q = Resp.create(2)

	try viaPropagate(q, flag: true) otherwise (e) 'h2'
		match e 'k2'
			refused(back) then back.fin()
		end 'k2'
	end 'h2'

	try viaPropagate(Resp.create(3), flag: false) otherwise (e) 'h3'
		match e 'k3'
			refused(back) then back.fin()
		end 'k3'
	end 'h3'

	return 0
end 'main'
```
```stdout
fin 1
fin 2
fin 3
```
```exitcode
0
```

<!-- test: a-match-binds-and-consumes-the-linear-payload-of-a-union -->
A union with a linear payload is linear. The `some` arm binds the payload and consumes it, and the match consumes the union itself.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function main() returns ExitCode
	settle(Maybe.some(Resp.create(1)))
	settle(Maybe.nothing)
	return 0
end 'main'
```
```stdout
fin 1
nothing
```
```exitcode
0
```

<!-- test: an-isolated-linear-variable-is-moved-by-the-function-that-consumes-it -->
An `isolated` linear variable handed to a parameter that takes it is moved, exactly as a plain one is.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function use(r Resp)
	r.fin()
end 'use'

function main() returns ExitCode
	let r = Resp.create(1) as isolated Resp
	use(r)
	return 0
end 'main'
```
```stdout
fin 1
```
```exitcode
0
```

<!-- test: a-send-without-a-timeout-is-no-wait-for-a-live-linear-value -->
A send without `sendTimeout:` fails `mailboxFull` instead of waiting, so `r` stays live across it.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Sink
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function bump()
		self.n = self.n + 1
	end 'bump'
end 'Sink'

function main() returns ExitCode
	let s = spawn Sink.create()
	let r = Resp.create(1)
	try s.bump() otherwise ignore
	r.fin()
	return 0
end 'main'
```
```stdout
fin 1
```
```exitcode
0
```

<!-- test: a-let-of-a-linear-value-moves-it -->
`let second = first` moves the linear value into `second`, which consumes it.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let first = Resp.create(1)
	let second = first
	second.fin()
	return 0
end 'main'
```
```stdout
fin 1
```
```exitcode
0
```

<!-- test: a-linear-value-dropped-at-the-end-of-its-scope-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(1)
	print("x\n")
	return 0
end 'main'
```
```maxoncstderr
error E3012: <fragment>:22:6: unused variable: 'r'
error E3223: <fragment>:24:2: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: a-linear-value-left-by-an-early-return-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function f(r Resp, flag bool) returns Code
	if flag 'early'
		return 1
	end 'early'

	r.fin()
	return 0
end 'f'

function main() returns ExitCode
	print("{f(Resp.create(1), flag: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:23:3: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: a-linear-value-left-by-a-throw-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

enum Oops implements Error
	bad
end 'Oops'

function f(r Resp, flag bool) throws Oops
	if flag 'early'
		throw Oops.bad
	end 'early'

	r.fin()
end 'f'

function main() returns ExitCode
	try f(Resp.create(1), flag: true) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:27:3: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: a-linear-value-left-by-a-propagating-try-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

enum Oops implements Error
	bad
end 'Oops'

function mayFail(flag bool) throws Oops
	if flag 'f'
		throw Oops.bad
	end 'f'
end 'mayFail'

function f(r Resp, flag bool) throws Oops
	try mayFail(flag)
	r.fin()
end 'f'

function main() returns ExitCode
	try f(Resp.create(1), flag: true) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:32:2: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: overwriting-a-live-linear-variable-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	var r = Resp.create(1)
	r = Resp.create(2)
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:23:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: discarding-a-linear-value-with-let-underscore-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(1)
	let _ = r
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:24:2: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: rebinding-the-name-of-a-live-linear-value-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(1)
	let r = Resp.create(2)
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:25:2: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: reading-a-linear-value-after-its-finalizer-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(1)
	r.fin()
	print("{r.peek()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:10: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: calling-a-finalizer-twice-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(1)
	r.fin()
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-finalizer-called-twice-on-a-parameter-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function settle(r Resp)
	r.fin()
	r.fin()
end 'settle'

function main() returns ExitCode
	settle(Resp.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:23:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-linear-value-consumed-in-a-loop-body-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(1)

	for i in 0 upto 2 'loop'
		r.fin()
	end 'loop'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:25:3: use of moved value 'r': it was moved in an earlier iteration of this loop
```

<!-- test: a-let-of-a-linear-value-moves-it-and-the-source-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let first = Resp.create(1)
	let second = first
	first.fin()
	second.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:2: use of moved value 'first': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-linear-argument-always-moves-so-the-caller-cannot-read-it-again -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function inspect(r Resp) returns Code
	return r.peek()
end 'inspect'

function main() returns ExitCode
	let r = Resp.create(2)
	print("{inspect(r)}\n")
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: destruct-self-outside-a-consuming-method-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Sloppy
	var n as Code

	static function create(n Code) returns Self
		return Self{n: n}
	end 'create'

	function bad()
		destruct self
	end 'bad'
end 'Sloppy'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:29:3: `destruct self` is written in a method that takes `consuming self`: only a finalizer ends the life of the value it is called on
```

<!-- test: consuming-self-on-a-type-with-no-finalizer-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Plain
	var n as Code

	static function create(n Code) returns Self
		return Self{n: n}
	end 'create'

	function take(consuming self)
		print("{self.n}\n")
	end 'take'
end 'Plain'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:28:11: `consuming self` belongs to a method of a `type` that declares a finalizer, and no method of 'Plain' contains `destruct self`
```

<!-- test: a-method-that-does-not-take-consuming-self-cannot-consume-its-receiver -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Cut
	var n as Code

	static function create(n Code) returns Self
		return Self{n: n}
	end 'create'

	function close(consuming self)
		destruct self
	end 'close'

	function closeAgain()
		self.close()
	end 'closeAgain'
end 'Cut'

function main() returns ExitCode
	let c = Cut.create(3)
	c.closeAgain()
	c.close()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:33:8: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
```

<!-- test: a-linear-value-cannot-be-cloned -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(1)
	let c = r.clone()
	c.fin()
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:23:10: 'Resp' is unique and cannot be cloned
```

<!-- test: a-linear-value-is-refused-in-a-field -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Holder
	var r as Resp
	var n as Code

	static function create(r Resp, n Code) returns Self
		return Self{r: r, n: n}
	end 'create'

	function show() returns Code
		return self.n
	end 'show'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(Resp.create(1), n: 2)
	print("{h.show()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:22:6: the field 'r' holds a linear value, and a linear value lives only in locals, parameters and message arguments: a field would keep it past the code that must consume it
```

<!-- test: a-union-carrying-a-linear-value-is-refused-in-a-field -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

type Holder
	var slot as Maybe

	static function create() returns Self
		return Self{slot: Maybe.nothing}
	end 'create'

	function show() returns Code
		return 0
	end 'show'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	print("{h.show()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:27:6: the field 'slot' holds a linear value, and a linear value lives only in locals, parameters and message arguments: a field would keep it past the code that must consume it
```

<!-- test: a-linear-value-is-refused-as-a-type-argument -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

typealias RespArray = Array with Resp

function main() returns ExitCode
	let items = RespArray.create(0)
	print("{items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:21:11: 'Resp' is a linear value and cannot be a type argument of 'Array': a linear value lives only in locals, parameters and message arguments, so no container, promise, generic record or generic function holds one
```

<!-- test: a-closure-cannot-capture-a-linear-value -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(1)
	let show = function() gives r.peek()
	print("{show()}\n")
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:23:30: a closure cannot capture 'r', a linear value: a linear value lives only in locals, parameters and message arguments, so no closure keeps one
```

<!-- test: a-tuple-item-cannot-be-a-linear-value -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let pair = (Resp.create(1), 2)
	print("{pair.1}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:22:14: a tuple holds no linear value: a linear value lives only in locals, parameters and message arguments, so no tuple item keeps one
```

<!-- test: an-async-call-takes-no-linear-argument -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function finish(r Resp)
	r.fin()
end 'finish'

function main() returns ExitCode
	let p = async finish(Resp.create(1))
	await p
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:26:16: an `async` call takes no linear argument: a coroutine that cannot start drops the value it was given, and a linear value is consumed exactly once
```

<!-- test: an-async-call-of-a-function-with-a-linear-result-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function make() returns Resp
	return Resp.create(1)
end 'make'

function main() returns ExitCode
	let p = async make()
	let r = await p
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:26:16: an `async` call of a function that returns a linear value is refused: the promise would hold a linear value, and nothing may be left to drop it
```

<!-- test: a-function-with-no-result-type-cannot-return-a-linear-value -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function leak(r Resp)
	return r
end 'leak'

function main() returns ExitCode
	leak(Resp.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:22:2: type mismatch: 'leak' declares no return type, so it cannot return a value
```

<!-- test: a-message-reply-cannot-be-a-linear-value -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Factory
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function make() returns Resp
		return Resp.create(self.n)
	end 'make'
end 'Factory'

function main() returns ExitCode
	let f = spawn Factory.create()
	let asked = try f.make() otherwise panic("sent")
	let r = try await asked otherwise panic("answered")
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:34:10: the message `Factory.make` returns a linear value, which lives only in locals, parameters and message arguments, and this `spawn` makes `Factory` a service — whose reply is handed to the green thread that awaits it. `await` a promise in the handler and return the result it yields, return a value whose graph holds no function value or thread-bound handle, or drop the `returns` clause
```

<!-- test: a-linear-value-is-not-passed-through-an-interface-method -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

interface Taker
	function take(r Resp)
end 'Taker'

type Eater implements Taker
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function take(r Resp)
		r.fin()
	end 'take'
end 'Eater'

function feed(t Taker, r Resp)
	t.take(r)
end 'feed'

function main() returns ExitCode
	feed(Eater.create(), r: Resp.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:38:4: a linear value is never passed to an interface method: its callee is not known here, and the callee is what consumes a linear value. Call a function that takes it
```

<!-- test: a-linear-value-live-across-an-await-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function slow() returns Code
	sleep(1)
	return 1
end 'slow'

function main() returns ExitCode
	let r = Resp.create(1)
	let p = async slow()
	let c = await p
	print("{c}\n")
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:29:10: the linear value 'r' is live across this wait: a wait may never end, so a linear value is consumed before any call that waits, `await` or send that waits
```

<!-- test: a-send-with-a-timeout-is-a-wait-a-live-linear-value-may-not-cross -->
<!-- unsupported-targets: wasm32-wasi -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Sink
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function bump()
		self.n = self.n + 1
	end 'bump'
end 'Sink'

function main() returns ExitCode
	let s = spawn Sink.create()
	let r = Resp.create(1)
	try s.bump(sendTimeout: 5) otherwise ignore
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:36:6: the linear value 'r' is live across this wait: a wait may never end, so a linear value is consumed before any call that waits, `await` or send that waits
```

<!-- test: a-linear-temporary-live-across-an-await-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function slow() returns Code
	sleep(1)
	return 1
end 'slow'

function use2(r Resp, c Code)
	print("{c}\n")
	r.fin()
end 'use2'

function main() returns ExitCode
	let p = async slow()
	use2(Resp.create(1), c: await p)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:33:26: a linear value is live across this wait: a wait may never end, so a linear value is consumed before any call that waits, `await` or send that waits
```

<!-- test: a-linear-temporary-live-at-a-throw-point-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

enum Oops implements Error
	bad
end 'Oops'

function thrv(c Code) returns Code throws Oops
	if c == 0 'zero'
		throw Oops.bad
	end 'zero'

	return c
end 'thrv'

function use2(r Resp, c Code)
	print("{c}\n")
	r.fin()
end 'use2'

function f() throws Oops
	use2(Resp.create(1), c: try thrv(1))
end 'f'

function main() returns ExitCode
	try f() otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:39:26: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: a-linear-temporary-nobody-consumes-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	print("{Resp.create(1).peek()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:22:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: a-send-with-a-throwing-operand-while-a-linear-value-is-live-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

enum Oops implements Error
	bad
end 'Oops'

function thrv(c Code) returns Code throws Oops
	if c == 0 'zero'
		throw Oops.bad
	end 'zero'

	return c
end 'thrv'

type Sink
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(r Resp, extra Code)
		self.n = extra
		r.fin()
	end 'take'
end 'Sink'

function send(s Sink.handle, r Resp) throws Oops
	try s.take(r, extra: try thrv(1)) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(back, _) then back.fin()
			serviceStopped(back, _) then back.fin()
		end 'why'
	end 'refused'
end 'send'

function main() returns ExitCode
	let s = spawn Sink.create()
	try send(s, r: Resp.create(1)) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:47:23: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: otherwise-ignore-on-a-failed-send-drops-the-linear-argument -->
<!-- unsupported-targets: wasm32-wasi -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Sink
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(r Resp)
		self.n = self.n + 1
		r.fin()
	end 'take'
end 'Sink'

function main() returns ExitCode
	let s = spawn Sink.create()
	let r = Resp.create(1)
	try s.take(r) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:37:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: a-match-arm-that-leaves-the-linear-payload-unconsumed-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then print("{r.peek()}\n")
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function main() returns ExitCode
	settle(Maybe.nothing)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:28:3: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: a-match-arm-that-binds-no-name-for-a-linear-payload-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some then print("some\n")
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function main() returns ExitCode
	settle(Maybe.nothing)
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:28:3: the case 'some' carries the linear value 'r', and this arm must bind it to a name and consume it: a pattern that leaves it behind drops it
```

<!-- test: a-default-arm-cannot-cover-a-case-with-a-linear-payload -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		nothing then print("nothing\n")
		default panic("other")
	end 'which'
end 'settle'

function main() returns ExitCode
	settle(Maybe.nothing)
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:30:2: `default` covers the case 'some', which carries the linear value 'r': give the case an arm that binds it and consumes it
```

<!-- test: an-isolated-linear-variable-handed-to-a-consuming-callee-is-moved-not-lent -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function use(r Resp)
	r.fin()
end 'use'

function main() returns ExitCode
	let r = Resp.create(1) as isolated Resp
	use(r)
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-reply-handed-to-a-function-that-answers-it-is-moved-not-lent -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

typealias CodeReply = Reply with Code

function ansNow(waiting CodeReply)
	waiting.answer(3)
end 'ansNow'

type Dr
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ask() returns Code throws ReplyStopped
		let pending = reply.later()
		ansNow(pending)
		pending.answer(4)
		return
	end 'ask'
end 'Dr'

function main() returns ExitCode
	let d = spawn Dr.create()
	let asked = try d.ask() otherwise panic("sent")
	let answer = try await asked otherwise panic("answered")
	print("{answer}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:37:3: use of moved value 'pending': its ownership moved to the parameter 'waiting' of 'ansNow', which keeps it
```

<!-- test: a-finalizer-that-throws-after-destruct-is-caught-without-the-value-live -->
A finalizer may throw once it has ended the value: the receiver moved into the call, so the handler of the `try` holds nothing live.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

enum Oops implements Error
	bad
end 'Oops'

type Gate
	var n as Code

	static function create(n Code) returns Self
		return Self{n: n}
	end 'create'

	function close(consuming self) throws Oops
		let code = self.n
		destruct self

		if code == 0 'zero'
			throw Oops.bad
		end 'zero'
	end 'close'
end 'Gate'

function main() returns ExitCode
	Resp.create(1).fin()

	try Gate.create(0).close() otherwise ignore

	let g = Gate.create(1)
	try g.close() otherwise ignore

	let h = Gate.create(0)

	try h.close() otherwise (e) 'failed'
		print("closed with {e.name}\n")
	end 'failed'

	return 0
end 'main'
```
```stdout
fin 1
closed with bad
```
```exitcode
0
```

<!-- test: a-generic-type-with-a-finalizer-is-linear -->
`Cell with Number` is linear because `Cell` declares a finalizer, and `drop` consumes it.
```maxon
typealias Number = int(0 to 999)

type Cell uses Item
	var item as Item

	static function holding(item Item) returns Self
		return Self{item: item}
	end 'holding'

	function release(consuming self)
		destruct self
	end 'release'
end 'Cell'

typealias NumberCell = Cell with Number

function drop(cell NumberCell)
	cell.release()
end 'drop'

function main() returns ExitCode
	let cell = NumberCell.holding(5)
	drop(cell)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-linear-instance-of-a-generic-type-dropped-is-refused -->

```maxon
typealias Number = int(0 to 999)

type Cell uses Item
	var item as Item

	static function holding(item Item) returns Self
		return Self{item: item}
	end 'holding'

	function peek() returns Item
		return self.item
	end 'peek'

	function release(consuming self)
		destruct self
	end 'release'
end 'Cell'

typealias NumberCell = Cell with Number

function main() returns ExitCode
	let cell = NumberCell.holding(5)
	print("held {cell.peek()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:25:2: this drops 'cell', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: a-linear-value-live-across-an-acquire-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Ledger
	var total as Code

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function size() returns Code
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, r Resp) returns Code
	acquire s 'held'
		print("inside\n")
	end 'held' otherwise 'stopped'
		print("stopped\n")
	end 'stopped'

	r.fin()

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create()
	print("{work(a, r: Resp.create(1))} {try await a.size() otherwise 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:34:2: the linear value 'r' is live across this wait: a wait may never end, so a linear value is consumed before any call that waits, `await` or send that waits
```

<!-- test: an-isolated-linear-value-moves-into-an-isolated-parameter-inside-a-modify-block -->
An `isolated` parameter takes an outer isolated linear value inside an open window: that move is one of the destinations a window allows.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Bag'

function takeIso(r isolated Resp)
	let plain = merge r
	plain.fin()
end 'takeIso'

function main() returns ExitCode
	let v = Resp.create(1) as isolated Resp
	let bx = Bag.create() as isolated Bag

	modify bx 'w'
		bx.bump()
		takeIso(v)
	end 'w'

	return 0
end 'main'
```
```stdout
fin 1
```
```exitcode
0
```

<!-- test: an-outer-isolated-linear-value-cannot-enter-a-modify-block-through-a-keeping-parameter -->
`giveS` keeps its plain parameter, so moving `v` into it inside `modify bx` would put a value of another region into the open one.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

function giveS(r Resp) returns Resp
	return r
end 'giveS'

function useS(r Resp)
	r.fin()
end 'useS'

function main() returns ExitCode
	let v = Resp.create(1) as isolated Resp
	let bx = Bag.create() as isolated Bag

	modify bx 'w'
		useS(giveS(v))
	end 'w'

	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:42:14: an isolated value from outside 'w' cannot be moved into the parameter 'r' of 'giveS', which keeps it while 'w' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: an-outer-isolated-linear-value-cannot-enter-a-modify-block-through-a-consuming-receiver -->
`wrap` takes `consuming self` and returns it, which moves `v` into the open region just as a keeping parameter does.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'

	function wrap(consuming self) returns Resp
		return self
	end 'wrap'
end 'Resp'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

function useS(r Resp)
	r.fin()
end 'useS'

function main() returns ExitCode
	let v = Resp.create(1) as isolated Resp
	let bx = Bag.create() as isolated Bag

	modify bx 'w'
		useS(v.wrap())
	end 'w'

	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:42:8: an isolated value from outside 'w' cannot be moved into the parameter 'self' of 'Resp.wrap', which keeps it while 'w' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: an-outer-isolated-linear-value-cannot-enter-a-modify-block-through-the-isolated-result-of-a-call -->
`idI` takes `v` into an `isolated` parameter and hands it back as an `isolated` result, which is still `v`'s own region. Moving that result into the keeping parameter of `giveS` puts the outer value into the open region as surely as moving `v` does.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

function idI(r isolated Resp) returns isolated Resp
	return r
end 'idI'

function giveS(r Resp) returns Resp
	return r
end 'giveS'

function useS(r Resp)
	r.fin()
end 'useS'

function main() returns ExitCode
	let v = Resp.create(1) as isolated Resp
	let bx = Bag.create() as isolated Bag

	modify bx 'w'
		let w = idI(v)
		useS(giveS(w))
	end 'w'

	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:47:14: an isolated value from outside 'w' cannot be moved into the parameter 'r' of 'giveS', which keeps it while 'w' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: an-outer-isolated-linear-value-cannot-enter-a-modify-block-through-a-chain-of-isolated-results -->
Each `idI` call hands the region on, so the value two calls down is still the outer one.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

function idI(r isolated Resp) returns isolated Resp
	return r
end 'idI'

function giveS(r Resp) returns Resp
	return r
end 'giveS'

function useS(r Resp)
	r.fin()
end 'useS'

function main() returns ExitCode
	let v = Resp.create(1) as isolated Resp
	let bx = Bag.create() as isolated Bag

	modify bx 'w'
		let first = idI(v)
		let second = idI(first)
		useS(giveS(second))
	end 'w'

	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:48:14: an isolated value from outside 'w' cannot be moved into the parameter 'r' of 'giveS', which keeps it while 'w' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: a-fresh-isolated-value-passed-through-an-isolated-result-moves-freely-inside-a-modify-block -->
A value made inside the block has no outer region to leave, so the same two calls are accepted for it.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

function idI(r isolated Resp) returns isolated Resp
	return r
end 'idI'

function giveS(r Resp) returns Resp
	return r
end 'giveS'

function useS(r Resp)
	r.fin()
end 'useS'

function main() returns ExitCode
	let bx = Bag.create() as isolated Bag

	modify bx 'w'
		let made = Resp.create(2) as isolated Resp
		let w = idI(made)
		useS(giveS(w))
	end 'w'

	return 0
end 'main'
```
```stdout
fin 2
```
```exitcode
0
```

<!-- test: an-outer-isolated-linear-value-handed-back-by-a-failed-send-cannot-enter-a-modify-block-through-a-keeping-parameter -->
A failed send hands the sent value back in `mailboxFull` or `serviceStopped`. `v` was outside `w` when it was sent, so what comes back is still the outer region and may not enter the open one.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

type Sink
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(r isolated Resp)
		self.n = self.n + 1
		let plain = merge r
		plain.fin()
	end 'take'
end 'Sink'

function giveS(r Resp) returns Resp
	return r
end 'giveS'

function useS(r Resp)
	r.fin()
end 'useS'

function main() returns ExitCode
	let s = spawn Sink.create()
	let v = Resp.create(1) as isolated Resp
	let bx = Bag.create() as isolated Bag

	modify bx 'w'
		try s.take(v) otherwise (e) 'refused'
			match e 'why'
				mailboxFull(back) then useS(giveS(back))
				serviceStopped(back) then useS(giveS(back))
			end 'why'
		end 'refused'
	end 'w'

	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:59:39: an isolated value from outside 'w' cannot be moved into the parameter 'r' of 'giveS', which keeps it while 'w' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: an-outer-isolated-linear-value-handed-back-by-a-failed-send-in-a-try-block-cannot-enter-a-modify-block-through-a-keeping-parameter -->
A bare send inside a block-form `try` routes its failure to the block's handler, and the box the handler binds carries the same hand-back as an explicit `try` does: `v` was outside `w` when it was sent, so what comes back may not enter the open region.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

type Sink
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(r isolated Resp)
		self.n = self.n + 1
		let plain = merge r
		plain.fin()
	end 'take'
end 'Sink'

function giveS(r Resp) returns Resp
	return r
end 'giveS'

function useS(r Resp)
	r.fin()
end 'useS'

function main() returns ExitCode
	let s = spawn Sink.create()
	let v = Resp.create(1) as isolated Resp
	let bx = Bag.create() as isolated Bag

	modify bx 'w'
		try 'send'
			s.take(v)
		end 'send'
		otherwise (e) 'refused'
			match e 'why'
				mailboxFull(back) then useS(giveS(back))
				serviceStopped(back) then useS(giveS(back))
			end 'why'
		end 'refused'
	end 'w'

	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:62:39: an isolated value from outside 'w' cannot be moved into the parameter 'r' of 'giveS', which keeps it while 'w' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: a-try-block-that-moves-an-outer-isolated-value-in-a-send-and-routes-a-second-error-is-refused-inside-a-modify-block -->
A send that hands its argument back shares its handler with a second throwing call only if no outer value moves between the two edges, and an isolated value sent from outside `w` does move, so the union of the send error and another error never reaches a handler inside the open region.
```maxon
typealias Code = int(0 to 999)

enum FileError implements Error
	notFound
end 'FileError'

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

type Sink
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(r isolated Resp)
		self.n = self.n + 1
		let plain = merge r
		plain.fin()
	end 'take'
end 'Sink'

function giveS(r Resp) returns Resp
	return r
end 'giveS'

function useS(r Resp)
	r.fin()
end 'useS'

function mayFail(code Code) returns Code throws FileError
	if code == 0 'absent'
		throw FileError.notFound
	end 'absent'

	return code
end 'mayFail'

function main() returns ExitCode
	let s = spawn Sink.create()
	let v = Resp.create(1) as isolated Resp
	let bx = Bag.create() as isolated Bag

	modify bx 'w'
		try 'both'
			let n = mayFail(1)
			s.take(v)
			print("sent {n}\n")
		end 'both'
		otherwise (e) 'refused'
			match e 'why'
				Sink.take.sendError.mailboxFull(back) then useS(giveS(back))
				Sink.take.sendError.serviceStopped(back) then useS(giveS(back))
				FileError.notFound then print("missing\n")
			end 'why'
		end 'refused'
	end 'w'

	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:69:3: Unsupported: a block-form `try` whose body MOVES a value declared outside the block (into a field, a union payload, a consuming call or a `return`) BETWEEN two throwing calls — the block's throw edges share one handler, and they would reach it with that value still owned on the earlier edges and already given away on the later ones, so its drop belongs on some throw edges and not others. Move the value before the `try` block or after it, or give the two throwing calls their own `try` blocks
```

<!-- test: an-await-of-a-send-that-hands-an-outer-isolated-value-back-cannot-run-inside-a-modify-block -->
`try await` sends and then waits for the reply, and the wait is refused inside an open `modify`, so the reply leg's hand-back never reaches a handler inside the window.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

type Sink
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(r isolated Resp) returns Code
		self.n = self.n + 1
		let plain = merge r
		plain.fin()
		return self.n
	end 'take'
end 'Sink'

function giveS(r Resp) returns Resp
	return r
end 'giveS'

function useS(r Resp)
	r.fin()
end 'useS'

function main() returns ExitCode
	let s = spawn Sink.create()
	let v = Resp.create(1) as isolated Resp
	let bx = Bag.create() as isolated Bag

	modify bx 'w'
		try await s.take(v) otherwise (e) 'refused'
			match e 'why'
				mailboxFull(back) then useS(giveS(back))
				serviceStopped(back) then useS(giveS(back))
			end 'why'
		end 'refused'
	end 'w'

	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:58:7: cannot wait inside 'w'; another coroutine could run while 'bx' is open. Await before or after the block
```

<!-- test: a-finalizer-that-only-reads-its-receiver-is-accepted-inside-a-modify-block -->
`fin` reads `self.code` and ends in `destruct self`, so `self` goes nowhere and the outer value is destroyed without joining the open region.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Bag'

function main() returns ExitCode
	let v = Resp.create(1) as isolated Resp
	let bx = Bag.create() as isolated Bag

	modify bx 'w'
		bx.bump()
		v.fin()
	end 'w'

	return 0
end 'main'
```
```stdout
fin 1
```
```exitcode
0
```

<!-- test: a-consuming-method-that-returns-a-field-and-destroys-its-receiver-is-accepted-inside-a-modify-block -->
`close` neither stores, returns nor passes `self`: it copies a number out of it and destroys it.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function close(consuming self) returns Code
		let kept = self.code
		destruct self
		return kept
	end 'close'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Resp'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

function main() returns ExitCode
	let v = Resp.create(7) as isolated Resp
	let bx = Bag.create() as isolated Bag

	modify bx 'w'
		print("{v.close()}\n")
	end 'w'

	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: an-outer-isolated-linear-value-cannot-enter-a-modify-block-through-a-receiver-that-hands-itself-to-a-keeping-function -->
`forward` neither returns nor stores `self` itself, but it moves it into `giveS`, which keeps it, so the receiver counts as kept.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'

	function forward(consuming self) returns Resp
		return giveS(self)
	end 'forward'
end 'Resp'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

function giveS(r Resp) returns Resp
	return r
end 'giveS'

function useS(r Resp)
	r.fin()
end 'useS'

function main() returns ExitCode
	let v = Resp.create(1) as isolated Resp
	let bx = Bag.create() as isolated Bag

	modify bx 'w'
		useS(v.forward())
	end 'w'

	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:46:8: an isolated value from outside 'w' cannot be moved into the parameter 'self' of 'Resp.forward', which keeps it while 'w' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: a-linear-value-is-refused-in-an-array-literal -->
A bare array literal infers `Array with Resp`, an instance whose drop frees its records with no finalizer, so the literal is refused like the written type argument is.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let xs = [Resp.create(1), Resp.create(2)]
	print("{xs.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:22:12: 'Resp' is a linear value and cannot be a type argument of 'Array': a linear value lives only in locals, parameters and message arguments, so no container, promise, generic record or generic function holds one
```

<!-- test: a-linear-value-is-refused-in-a-map-literal -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let m = ["a": Resp.create(1)]
	print("{m.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:22:11: 'Resp' is a linear value and cannot be a type argument of 'Map': a linear value lives only in locals, parameters and message arguments, so no container, promise, generic record or generic function holds one
```

<!-- test: a-generic-record-instance-inferred-from-a-linear-argument-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Cell uses Item
	var item as Item

	static function holding(item Item) returns Self
		return Self{item: item}
	end 'holding'

	function count() returns Code
		return 1
	end 'count'
end 'Cell'

function main() returns ExitCode
	let c = Cell.holding(Resp.create(1))
	print("{c.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:34:15: 'Resp' is a linear value and cannot be a type argument of 'Cell': a linear value lives only in locals, parameters and message arguments, so no container, promise, generic record or generic function holds one
```

<!-- test: a-closure-that-builds-a-linear-value-cannot-fill-a-mapped-array -->
`map` answers an array of its own element type, so a closure returning a linear value for an integer array is a type mismatch and no `Array with Resp` is ever built.
```maxon
typealias Code = int(0 to 999)
typealias CodeArray = Array with Code

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let codes = CodeArray from [1, 2, 3]
	let made = codes.map(function(n) gives Resp.create(n))
	print("{made.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:24:35: Cannot return 'Resp' from function declared to return 'Code'
```

<!-- test: a-linear-argument-to-a-generic-function-is-refused -->
Each copy of a generic function is checked against its concrete type, but a linear type argument is refused at the call for the reason a written one is: no generic holds a linear value.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function keep(x T) uses T returns T
	return x
end 'keep'

function main() returns ExitCode
	let r = Resp.create(1)
	let s = keep(r)
	s.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:27:10: 'Resp' is a linear value and cannot be a type argument of 'keep': a linear value lives only in locals, parameters and message arguments, so no container, promise, generic record or generic function holds one
```

<!-- test: a-module-let-of-a-linear-type-is-refused -->
A module-level `let` lives for the whole program and any function can call its finalizer, so it could be consumed twice.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

let shared = Resp.create(7)

function main() returns ExitCode
	shared.fin()
	shared.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:21:14: 'shared' holds a linear value, and a module-level or `static` `let` lives for the whole program: a linear value lives only in locals, parameters and message arguments, so it can be consumed exactly once
```

<!-- test: a-static-let-of-a-linear-type-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static let shared = Self{code: 7}

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'

	static function useShared()
		Resp.shared.fin()
	end 'useShared'
end 'Resp'

function main() returns ExitCode
	Resp.useShared()
	Resp.useShared()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:7:22: 'Resp.shared' holds a linear value, and a module-level or `static` `let` lives for the whole program: a linear value lives only in locals, parameters and message arguments, so it can be consumed exactly once
```

<!-- test: a-linear-value-cannot-be-passed-to-an-interface-typed-parameter -->

```maxon
typealias Code = int(0 to 999)

interface Describable
	function peek() returns Code
end 'Describable'

type Resp implements Describable
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function show(d Describable) returns Code
	return d.peek()
end 'show'

function main() returns ExitCode
	let r = Resp.create(1)
	print("{show(r)}\n")
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:31:10: Cannot pass a value as 'd', which is declared at the interface type 'Describable': a linear value cannot stand at an interface type: the interface value would drop it with no finalizer, and only a concrete linear type has finalizers to call. Keep the linear type concrete, or consume the value before it is widened to 'Describable'
```

<!-- test: a-linear-value-cannot-be-returned-as-an-interface-value -->

```maxon
typealias Code = int(0 to 999)

interface Describable
	function peek() returns Code
end 'Describable'

type Resp implements Describable
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function widen(r Resp) returns Describable
	return r
end 'widen'

function main() returns ExitCode
	let d = widen(Resp.create(1))
	print("{d.peek()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:26:2: Cannot return a value from 'widen' as the interface type 'Describable': a linear value cannot stand at an interface type: the interface value would drop it with no finalizer, and only a concrete linear type has finalizers to call. Keep the linear type concrete, or consume the value before it is widened to 'Describable'
```

<!-- test: a-linear-value-cannot-be-cast-to-an-interface -->

```maxon
typealias Code = int(0 to 999)

interface Describable
	function peek() returns Code
end 'Describable'

type Resp implements Describable
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function widen(r Resp) returns Describable
	return r as Describable
end 'widen'

function main() returns ExitCode
	let d = widen(Resp.create(1))
	print("{d.peek()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3009: <fragment>:26:11: Cannot cast from Resp to interface
```

<!-- test: a-conformer-that-waits-is-refused-so-a-call-through-an-interface-is-no-wait -->
A requirement never waits, so a conformer whose method awaits a reply is refused where it is declared, and a call through an interface is never a wait a live linear value must not cross.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Doubler
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function twice(x Code) returns Code
		return x
	end 'twice'
end 'Doubler'

interface Asker
	function ask(x Code) returns Code
end 'Asker'

type Waiting implements Asker
	var d as Doubler.handle

	static function create(d Doubler.handle) returns Self
		return Self{d: d}
	end 'create'

	function ask(x Code) returns Code
		return try await self.d.twice(x) otherwise 0
	end 'ask'
end 'Waiting'

function viaInterface(a Asker, r Resp) returns Code
	let n = a.ask(1)
	r.fin()
	return n
end 'viaInterface'

function main() returns ExitCode
	let d = spawn Doubler.create()
	print("{viaInterface(Waiting.create(d), r: Resp.create(1))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3210: <fragment>:44:11: 'Waiting.ask' may wait (it awaits a reply from 'Doubler' at line 45), but a requirement never waits or yields
```

<!-- test: a-timed-send-takes-no-linear-argument -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Sink
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(r Resp)
		self.n = self.n + 1
		r.fin()
	end 'take'
end 'Sink'

function main() returns ExitCode
	let s = spawn Sink.create()
	let r = Resp.create(1)

	try s.take(r, sendTimeout: 5) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(back) then back.fin()
			serviceStopped(back) then back.fin()
		end 'why'
	end 'refused'

	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:38:8: a linear value is never sent with a `sendTimeout:`: a timed send waits for room in the mailbox, and a wait may never end, so the sender could neither hand the value over nor consume it. Send it without a timeout, and consume the value `mailboxFull` and `serviceStopped` hand back
```

<!-- test: a-spawn-of-a-factory-that-throws-takes-no-linear-argument -->
A factory that throws may not start the service, and then it would drop the linear value.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

enum SinkError implements Error
	refused
end 'SinkError'

type Sink
	var n as Code

	static function create(r Resp) returns Self throws SinkError
		if r.peek() > 500 'tooHigh'
			r.fin()
			throw SinkError.refused
		end 'tooHigh'

		r.fin()
		return Self{n: 0}
	end 'create'

	export function bump()
		self.n = self.n + 1
	end 'bump'
end 'Sink'

function main() returns ExitCode
	let s = try spawn Sink.create(Resp.create(1)) otherwise return 1
	try s.bump() otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:44:25: a `spawn` takes no linear argument: a factory that cannot start the service drops the value it was given, and a linear value is consumed exactly once. Send the value to the service in a message once it is running
```

<!-- test: cond-over-two-linear-locals-is-refused -->
The conditional expression moves both locals and uses one, so the other is dropped and each is reported where the function ends.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function pick(flag bool) returns Code
	let a = Resp.create(1)
	let b = Resp.create(2)
	let c = a if flag else b
	let n = c.peek()
	c.fin()
	return n
end 'pick'

function main() returns ExitCode
	print("{pick(true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:27:2: this drops 'a', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
error E3223: <fragment>:27:2: this drops 'b', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: a-conditional-expression-whose-arms-build-the-value-is-accepted -->
Each arm builds a fresh `Resp`, so the join holds exactly one value and `c` consumes it.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function pick(flag bool) returns Code
	let c = Resp.create(1) if flag else Resp.create(2)
	let n = c.peek()
	c.fin()
	return n
end 'pick'

function main() returns ExitCode
	print("{pick(true)}\n")
	print("{pick(false)}\n")
	return 0
end 'main'
```
```stdout
fin 1
1
fin 2
2
```
```exitcode
0
```

<!-- test: a-break-out-of-a-loop-with-a-live-linear-value-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	for i in 0 upto 3 'loop'
		let r = Resp.create(i)

		if i == 1 'brk'
			break
		end 'brk'

		r.fin()
	end 'loop'

	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:26:4: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: a-continue-past-a-live-linear-value-is-refused -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	for i in 0 upto 3 'loop'
		let r = Resp.create(i)

		if i == 1 'skip'
			continue
		end 'skip'

		r.fin()
	end 'loop'

	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:26:4: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: a-default-arm-over-a-linear-union-covers-cases-with-no-linear-payload -->
`count` and `nothing` carry no linear value, so the `default` arm may take them, and the match ends the scrutinee on that path as the `data` arm does.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Msg
	data(r Resp)
	count(n Code)
	nothing
end 'Msg'

function handle(m Msg)
	match m 'k'
		data(r) then r.fin()
		default panic("not data")
	end 'k'
end 'handle'

function main() returns ExitCode
	handle(Msg.data(Resp.create(1)))
	return 0
end 'main'
```
```stdout
fin 1
```
```exitcode
0
```

<!-- test: a-default-that-throws-over-a-linear-union-does-not-drop-the-scrutinee -->
The throwing `default` leaves the function with `m` holding only a non-linear payload, so no linear value is dropped on that path.
```maxon
typealias Code = int(0 to 999)

enum HandleError implements Error
	notData
end 'HandleError'

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Msg
	data(r Resp)
	count(n Code)
	nothing
end 'Msg'

function handle(m Msg) throws HandleError
	match m 'k'
		data(r) then r.fin()
		default throws HandleError.notData
	end 'k'
end 'handle'

function main() returns ExitCode
	try handle(Msg.data(Resp.create(1))) otherwise ignore
	try handle(Msg.count(5)) otherwise ignore
	try handle(Msg.nothing) otherwise ignore
	return 0
end 'main'
```
```stdout
fin 1
```
```exitcode
0
```

<!-- test: a-consuming-method-that-never-consumes-its-receiver-names-self -->

```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'

	function wasteful(consuming self)
		print("wasteful {self.code}\n")
	end 'wasteful'
end 'Resp'

function main() returns ExitCode
	Resp.create(1).wasteful()
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:20:11: this drops 'self', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: a-linear-message-queued-when-the-service-shuts-down-is-consumed-by-the-service -->
<!-- procs: 1 -->
One processor and no yield in `main` keep the service from running while two sends queue behind its start. `shutdown()` closes the mailbox once it has drained, so both queued messages still reach `take`, which consumes each, and a send after the shutdown hands its value back in `serviceStopped`. The refused send is consumed first because the service has not run yet.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

type Sink
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(r Resp)
		self.n = self.n + 1
		r.fin()
	end 'take'
end 'Sink'

function sendOne(s Sink.handle, code Code)
	let r = Resp.create(code)

	try s.take(r) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(back) then back.fin()
			serviceStopped(back) then back.fin()
		end 'why'
	end 'refused'
end 'sendOne'

function main() returns ExitCode
	let s = spawn Sink.create()
	sendOne(s, code: 1)
	sendOne(s, code: 2)
	s.shutdown()
	sendOne(s, code: 3)
	return 0
end 'main'
```
```stdout
fin 3
fin 1
fin 2
```
```exitcode
0
```

<!-- test: dropped-promises-and-linear-values-are-reported-in-the-order-of-the-drops -->
<!-- unsupported-targets: wasm32-wasi -->
A linear value and a promise are each dropped twice, by an overwrite in the middle of the function and at its end. The four refusals come in the order the drops happen, whichever kind each is.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function work() returns Code
	sleep(1)
	return 1
end 'work'

function main() returns ExitCode
	var r = Resp.create(1)
	r = Resp.create(2)
	var p = async work()
	p = async work()
	print("{p.isComplete()} {r.peek()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:28:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
error E3213: <fragment>:30:2: this drops a promise that was never awaited, cancelled, stored or returned: `await` it, call `.cancel()` on it, store it or return it
error E3213: <fragment>:32:2: this drops 'p', which was never awaited, cancelled, stored or returned: `await p`, call `p.cancel()`, store it or return it
error E3223: <fragment>:32:2: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: an-outer-linear-union-matched-in-a-modify-block-cannot-hand-its-payload-to-a-keeping-parameter -->
`u` is declared outside `modify bx`, so inside the block it and the payload its `match` binds are read-only, and `giveS` keeps its parameter: the payload would become part of a union built inside the open region.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

function giveS(r Resp) returns Sent
	return Sent.notSent(r)
end 'giveS'

function useS(s Sent)
	match s 'm'
		ok then print("ok\n")
		notSent(r) then r.fin()
	end 'm'
end 'useS'

function main() returns ExitCode
	let bx = Bag.create() as isolated Bag
	let u = Sent.notSent(Resp.create(1))

	modify bx 'w'
		match u 'm'
			ok then print("ok\n")
			notSent(q) then useS(giveS(q))
		end 'm'
	end 'w'

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:48:31: 'giveS' needs a writable parameter 'r' at line 31; 'q' is read-only while 'w' is open
```

<!-- test: an-outer-linear-union-matched-in-a-modify-block-cannot-hand-its-payload-to-a-consuming-receiver-that-keeps-it -->
`wrap` takes `consuming self` and returns it inside a new union, so its receiver is a parameter it keeps, and the payload bound from the outer `u` is read-only inside `modify bx`.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'

	function wrap(consuming self) returns Sent
		return Sent.notSent(self)
	end 'wrap'
end 'Resp'

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

function useS(s Sent)
	match s 'm'
		ok then print("ok\n")
		notSent(r) then r.fin()
	end 'm'
end 'useS'

function main() returns ExitCode
	let bx = Bag.create() as isolated Bag
	let u = Sent.notSent(Resp.create(1))

	modify bx 'w'
		match u 'm'
			ok then print("ok\n")
			notSent(q) then useS(q.wrap())
		end 'm'
	end 'w'

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:48:25: 'wrap' needs a writable receiver at line 17; 'q' is read-only while 'w' is open
```

<!-- test: an-outer-linear-union-matched-in-a-modify-block-cannot-build-a-union-around-its-payload -->
Building `Sent.notSent(q)` inside `modify bx` stores the read-only payload into a value of the open region.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

function useS(s Sent)
	match s 'm'
		ok then print("ok\n")
		notSent(r) then r.fin()
	end 'm'
end 'useS'

function main() returns ExitCode
	let bx = Bag.create() as isolated Bag
	let u = Sent.notSent(Resp.create(1))

	modify bx 'w'
		match u 'm'
			ok then print("ok\n")
			notSent(q) then useS(Sent.notSent(q))
		end 'm'
	end 'w'

	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:44:38: cannot store a read-only 'Resp' into 'Sent.notSent.r' while 'w' is open; store a copy
```

<!-- test: an-outer-linear-union-matched-in-a-modify-block-may-consume-its-payload -->
<!-- procs: 1 -->
A finalizer that only destroys its receiver keeps nothing, so the payload of the outer `u` is consumed inside `modify bx` and the block leaves the union moved.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Bag
	export var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

function main() returns ExitCode
	let bx = Bag.create() as isolated Bag
	let u = Sent.notSent(Resp.create(1))

	modify bx 'w'
		match u 'm'
			ok then print("ok\n")
			notSent(q) then q.fin()
		end 'm'

		bx.n = 2
	end 'w'

	print("done\n")
	return 0
end 'main'
```
```stdout
fin 1
done
```
```exitcode
0
```

<!-- test: a-variable-holding-a-case-without-a-linear-payload-owes-nothing-when-assigned -->
`m` holds `Maybe.nothing`, a case that carries no linear value, so assigning it the case that does is no drop.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function main() returns ExitCode
	var m = Maybe.nothing
	m = Maybe.some(Resp.create(1))
	settle(m)
	return 0
end 'main'
```
```stdout
fin 1
```
```exitcode
0
```

<!-- test: a-branch-that-assigns-the-linear-case-leaves-the-variable-owing-it -->
After the `if` joins, `m` holds the linear case on one path, so `settle(m)` consumes it and the other path has nothing to consume.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function ready() returns bool
	return true
end 'ready'

function main() returns ExitCode
	var m = Maybe.nothing

	if ready() 'fill'
		m = Maybe.some(Resp.create(1))
	end 'fill'

	settle(m)
	return 0
end 'main'
```
```stdout
fin 1
```
```exitcode
0
```

<!-- test: a-variable-assigned-the-linear-case-in-both-branches-is-consumed -->
Both branches overwrite the `none` that `m` held before the `if`.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function ready() returns bool
	return true
end 'ready'

function main() returns ExitCode
	var m = Maybe.nothing

	if ready() 'fill'
		m = Maybe.some(Resp.create(1))
	end 'fill' else 'other'
		m = Maybe.some(Resp.create(2))
	end 'other'

	settle(m)
	return 0
end 'main'
```
```stdout
fin 1
```
```exitcode
0
```

<!-- test: a-loop-that-ends-every-lap-holding-a-case-without-a-linear-payload-owes-nothing -->
`m` holds `nothing` when the loop is entered and at the end of every lap, so overwriting it at the top of a lap drops nothing.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function main() returns ExitCode
	var m = Maybe.nothing

	for _ in 0 upto 2 'lap'
		m = Maybe.some(Resp.create(1))
		settle(m)
		m = Maybe.nothing
	end 'lap'
	return 0
end 'main'
```
```stdout
fin 1
fin 1
```
```exitcode
0
```

<!-- test: a-handler-that-assigns-the-variable-a-case-without-a-linear-payload-owes-nothing -->
The `try` body overwrites `m` with the linear case before it can throw, so the handler may not assume `m` is still empty; it assigns the case that carries nothing and `settle` consumes whichever `m` holds.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

enum Oops implements Error
	bad
end 'Oops'

function fail() returns Code throws Oops
	throw Oops.bad
end 'fail'

function main() returns ExitCode
	var m = Maybe.nothing

	try 'body'
		m = Maybe.some(Resp.create(1))
		let code = fail()
		print("{code}\n")
	end 'body'
	otherwise (e) 'h'
		match e 'k'
			bad then settle(m)
		end 'k'

		m = Maybe.nothing
	end 'h'

	settle(m)
	return 0
end 'main'
```
```stdout
fin 1
nothing
```
```exitcode
0
```

<!-- test: overwriting-a-variable-that-holds-the-linear-case-is-refused -->
`m` holds `Maybe.some`, which carries a `Resp` nobody consumed, so assigning it again drops that value.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function main() returns ExitCode
	var m = Maybe.some(Resp.create(1))
	m = Maybe.nothing
	settle(m)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:31:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: overwriting-a-variable-that-holds-the-linear-case-on-one-path-is-refused -->
The `if` leaves `m` holding the linear case on one path, so the assignment after it can drop a `Resp`.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function ready() returns bool
	return true
end 'ready'

function main() returns ExitCode
	var m = Maybe.nothing

	if ready() 'fill'
		m = Maybe.some(Resp.create(1))
	end 'fill'

	m = Maybe.nothing
	settle(m)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:40:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: overwriting-a-variable-bound-to-a-call-result-is-refused -->
A call's result may hold the linear case, so overwriting the variable it was bound to is a drop.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function recycle(r Resp) returns Maybe
	r.fin()
	return Maybe.some(Resp.create(9))
end 'recycle'

function main() returns ExitCode
	var m = recycle(Resp.create(3))
	m = Maybe.nothing
	settle(m)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:36:2: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: an-isolated-value-is-merged-into-a-variable-both-branches-leave-holding-a-case-without-a-linear-payload -->
Both branches give `m` the case that carries nothing, so after the `if` it holds no linear value and the isolated `filled` is merged into it.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function ready() returns bool
	return true
end 'ready'

function main() returns ExitCode
	var m = Maybe.nothing

	if ready() 'one'
		m = Maybe.nothing
	end 'one' else 'other'
		m = Maybe.nothing
	end 'other'

	let filled = Maybe.some(Resp.create(1)) as isolated Maybe
	m = filled
	settle(m)
	return 0
end 'main'
```
```stdout
fin 1
```
```exitcode
0
```

<!-- test: an-isolated-value-is-merged-into-a-variable-every-lap-leaves-holding-a-case-without-a-linear-payload -->
`m` holds `nothing` when the loop is entered and at the end of every lap, so each lap's isolated `filled` is merged into it.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function main() returns ExitCode
	var m = Maybe.nothing

	for _ in 0 upto 2 'lap'
		let filled = Maybe.some(Resp.create(1)) as isolated Maybe
		m = filled
		settle(m)
		m = Maybe.nothing
	end 'lap'

	settle(m)
	return 0
end 'main'
```
```stdout
fin 1
fin 1
nothing
```
```exitcode
0
```

<!-- test: an-isolated-value-is-not-merged-into-a-variable-one-branch-leaves-holding-the-linear-case -->
The `then` branch leaves `m` holding the linear case, so `m` may hold a linear value when `filled` is assigned to it.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function ready() returns bool
	return true
end 'ready'

function main() returns ExitCode
	var m = Maybe.nothing

	if ready() 'one'
		m = Maybe.some(Resp.create(1))
	end 'one' else 'other'
		m = Maybe.nothing
	end 'other'

	let filled = Maybe.some(Resp.create(2)) as isolated Maybe
	m = filled
	settle(m)
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:43:6: cannot store an isolated 'Maybe' into 'm', which holds a mutable 'Maybe'; `merge` it first
```

<!-- test: an-isolated-value-is-not-merged-into-a-variable-the-previous-lap-left-holding-it -->
The lap ends with `m` holding the `filled` it was given, so the next lap's `filled` is assigned to a variable that may hold a linear value.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function main() returns ExitCode
	var m = Maybe.nothing

	for _ in 0 upto 2 'lap'
		let filled = Maybe.some(Resp.create(1)) as isolated Maybe
		m = filled
	end 'lap'

	settle(m)
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:34:7: cannot store an isolated 'Maybe' into 'm', which holds a mutable 'Maybe'; `merge` it first
```

<!-- test: an-arm-reassigns-the-linear-union-it-matched-after-consuming-the-payload -->
The match consumes `m`, so the arms may give it a new value: `recycle` consumes the payload and answers a fresh `Maybe`, and the `nothing` arm refills it with another.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function recycle(r Resp) returns Maybe
	r.fin()
	return Maybe.some(Resp.create(9))
end 'recycle'

function main() returns ExitCode
	var m = Maybe.some(Resp.create(1))

	match m 'which'
		some(r) then m = recycle(r)
		nothing then m = Maybe.some(Resp.create(4))
	end 'which'

	settle(m)
	return 0
end 'main'
```
```stdout
fin 1
fin 9
```
```exitcode
0
```

<!-- test: an-arm-rebuilds-the-linear-union-from-its-own-payload -->
The payload the arm binds is owned, so the arm may wrap it into the union it matched.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function rewrap(r Resp) returns Maybe
	return Maybe.some(r)
end 'rewrap'

function main() returns ExitCode
	var m = Maybe.some(Resp.create(1))

	match m 'which'
		some(r) then m = rewrap(r)
		nothing then m = Maybe.nothing
	end 'which'

	settle(m)
	return 0
end 'main'
```
```stdout
fin 1
```
```exitcode
0
```

<!-- test: a-union-refilled-in-only-one-arm-is-consumed-on-the-other -->
Only the `some` arm refills `m`, so reading it after the match is a use of the union the `nothing` arm consumed.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function recycle(r Resp) returns Maybe
	r.fin()
	return Maybe.some(Resp.create(9))
end 'recycle'

function main() returns ExitCode
	var m = Maybe.some(Resp.create(1))

	match m 'which'
		some(r) then m = recycle(r)
		nothing then print("nothing\n")
	end 'which'

	settle(m)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:42:9: use of moved value 'm': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: an-arm-that-refills-the-union-without-consuming-its-payload-is-refused -->
The `some` arm binds `r` and drops it: refilling `m` does not consume the payload.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function main() returns ExitCode
	var m = Maybe.some(Resp.create(1))

	match m 'which'
		some(r) then m = Maybe.some(Resp.create(5))
		nothing then m = Maybe.nothing
	end 'which'

	settle(m)
	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:33:3: this drops 'r', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
error E3012: <fragment>:33:8: unused variable: 'r'
```

<!-- test: a-loop-refills-the-union-it-matched-in-every-arm -->
Every arm gives `m` a new value before the lap ends, so the next lap matches a live union.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function recycle(r Resp) returns Maybe
	r.fin()
	return Maybe.some(Resp.create(9))
end 'recycle'

function main() returns ExitCode
	var m = Maybe.some(Resp.create(1))

	for _ in 0 upto 2 'lap'
		match m 'm'
			some(r) then m = recycle(r)
			nothing then m = Maybe.nothing
		end 'm'
	end 'lap'

	settle(m)
	return 0
end 'main'
```
```stdout
fin 1
fin 9
fin 9
```
```exitcode
0
```

<!-- test: a-loop-refills-the-union-it-matched-in-only-one-arm -->
Only the `some` arm refills `m`, so the `nothing` arm leaves it consumed at the end of the lap.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function recycle(r Resp) returns Maybe
	r.fin()
	return Maybe.some(Resp.create(9))
end 'recycle'

function main() returns ExitCode
	var m = Maybe.some(Resp.create(1))

	for _ in 0 upto 2 'lap'
		match m 'm'
			some(r) then m = recycle(r)
			nothing then print("nothing\n")
		end 'm'
	end 'lap'

	settle(m)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:38:9: use of moved value 'm': it was moved in an earlier iteration of this loop
```

<!-- test: a-loop-continues-before-refilling-the-union-it-matched -->
The `continue` leaves the lap with `m` consumed, and the refill below it is skipped.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function main() returns ExitCode
	var m = Maybe.some(Resp.create(1))

	for i in 0 upto 3 'lap'
		match m 'm'
			some(r) then r.fin()
			nothing then print("nothing\n")
		end 'm'

		if i == 0 'first'
			continue
		end 'first'

		m = Maybe.some(Resp.create(2))
	end 'lap'

	settle(m)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:33:9: use of moved value 'm': it was moved in an earlier iteration of this loop
```

<!-- test: a-loop-continues-after-refilling-the-union-it-matched -->
The refill comes before the `continue`, so both ways out of the lap leave `m` live.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function recycle(r Resp) returns Maybe
	r.fin()
	return Maybe.some(Resp.create(9))
end 'recycle'

function main() returns ExitCode
	var m = Maybe.some(Resp.create(1))

	for i in 0 upto 3 'lap'
		match m 'm'
			some(r) then m = recycle(r)
			nothing then m = Maybe.nothing
		end 'm'

		if i == 0 'first'
			continue
		end 'first'

		print("lap {i}\n")
	end 'lap'

	settle(m)
	return 0
end 'main'
```
```stdout
fin 1
fin 9
lap 1
fin 9
lap 2
fin 9
```
```exitcode
0
```

<!-- test: a-while-loop-refills-the-union-it-matched-in-every-arm -->
The same refill under a `while`.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function recycle(r Resp) returns Maybe
	r.fin()
	return Maybe.some(Resp.create(9))
end 'recycle'

function main() returns ExitCode
	var m = Maybe.some(Resp.create(1))
	var laps = 0 as Code

	while laps < 2 'again'
		match m 'm'
			some(r) then m = recycle(r)
			nothing then m = Maybe.nothing
		end 'm'

		laps = laps + 1
	end 'again'

	settle(m)
	return 0
end 'main'
```
```stdout
fin 1
fin 9
fin 9
```
```exitcode
0
```

<!-- test: a-while-loop-refills-the-union-it-matched-in-only-one-arm -->
Only the `some` arm refills `m`, so the `nothing` arm leaves it consumed at the end of the lap.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function recycle(r Resp) returns Maybe
	r.fin()
	return Maybe.some(Resp.create(9))
end 'recycle'

function main() returns ExitCode
	var m = Maybe.some(Resp.create(1))
	var laps = 0 as Code

	while laps < 2 'again'
		match m 'm'
			some(r) then m = recycle(r)
			nothing then print("nothing\n")
		end 'm'

		laps = laps + 1
	end 'again'

	settle(m)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:39:9: use of moved value 'm': it was moved in an earlier iteration of this loop
```

<!-- test: a-nested-loop-refills-the-union-it-matched-in-every-arm -->
The match sits in the inner loop and the union lives across both.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function recycle(r Resp) returns Maybe
	r.fin()
	return Maybe.some(Resp.create(9))
end 'recycle'

function main() returns ExitCode
	var m = Maybe.some(Resp.create(1))

	for _ in 0 upto 2 'outer'
		for _ in 0 upto 2 'inner'
			match m 'm'
				some(r) then m = recycle(r)
				nothing then m = Maybe.nothing
			end 'm'
		end 'inner'
	end 'outer'

	settle(m)
	return 0
end 'main'
```
```stdout
fin 1
fin 9
fin 9
fin 9
fin 9
```
```exitcode
0
```

<!-- test: a-nested-loop-refills-the-union-it-matched-in-only-one-arm -->
The inner loop's `nothing` arm leaves `m` consumed when the lap ends.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	some(r Resp)
	nothing
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		some(r) then r.fin()
		nothing then print("nothing\n")
	end 'which'
end 'settle'

function recycle(r Resp) returns Maybe
	r.fin()
	return Maybe.some(Resp.create(9))
end 'recycle'

function main() returns ExitCode
	var m = Maybe.some(Resp.create(1))

	for _ in 0 upto 2 'outer'
		for _ in 0 upto 2 'inner'
			match m 'm'
				some(r) then m = recycle(r)
				nothing then print("nothing\n")
			end 'm'
		end 'inner'
	end 'outer'

	settle(m)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:39:10: use of moved value 'm': it was moved in an earlier iteration of this loop
```

<!-- test: a-linear-value-rebound-as-isolated-is-consumed-through-the-new-name -->
`as isolated` on a bare local hands the linear value to the new name, whose finalizer consumes it.
```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let a = r as isolated Resp
	a.fin()

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: the-source-of-an-isolated-rebind-of-a-linear-value-is-refused -->
The rebind moved the value, so its source cannot be finalized too.
```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(0) as isolated Resp
	let a = r as isolated Resp
	r.fin()
	a.fin()

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:19:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-union-holding-a-linear-value-rebound-as-isolated-is-consumed-by-its-match -->
The rebound union is consumed by the `match` over it, in the arm that binds the linear payload.
```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Resp'

union Wrapped
	empty
	filled(resp Resp)
end 'Wrapped'

function main() returns ExitCode
	let r = Wrapped.filled(Resp.create(0)) as isolated Wrapped
	let a = r as isolated Wrapped

	match a 'which'
		empty then print("empty\n")
		filled(held) then held.fin()
	end 'which'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-linear-parameter-discarded-with-let-underscore-is-refused -->
A function that takes a linear value must consume it, and a discard does not.
```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Resp'

function drop(r Resp)
	let _ = r
end 'drop'

function main() returns ExitCode
	let r = Resp.create(0)
	drop(r)

	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:16:10: this drops '_', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: destruct-self-in-a-loop-is-refused-on-the-next-lap -->
The lap after the first reaches `destruct self` with the value already destructed.
```maxon
typealias Num = int(i64.min to i64.max)

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self)
		var i = 2 as Num

		while i != 0 'w1'
			destruct self
			i = 0
		end 'w1'
	end 'f'
end 'Fz'

function main() returns ExitCode
	let t = Fz.create(0)
	t.f()

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:13: use of moved value 'self': it was moved in an earlier iteration of this loop
```

<!-- test: destruct-self-in-a-for-loop-is-refused-on-the-next-lap -->
A `for` loop repeats the body as a `while` does.
```maxon
typealias Num = int(i64.min to i64.max)

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self)
		for i in 0 upto 2 'laps'
			destruct self
			print("{i}\n")
		end 'laps'
	end 'f'
end 'Fz'

function main() returns ExitCode
	let t = Fz.create(0)
	t.f()

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:13:13: use of moved value 'self': it was moved in an earlier iteration of this loop
```

<!-- test: destruct-self-in-a-nested-loop-is-refused-on-the-next-lap -->
The inner loop repeats the body, whatever the outer loop does.
```maxon
typealias Num = int(i64.min to i64.max)

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self)
		var i = 2 as Num

		while i != 0 'outer'
			var j = 2 as Num

			while j != 0 'inner'
				destruct self
				j = 0
			end 'inner'

			i = 0
		end 'outer'
	end 'f'
end 'Fz'

function main() returns ExitCode
	let t = Fz.create(0)
	t.f()

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:18:14: use of moved value 'self': it was moved in an earlier iteration of this loop
```

<!-- test: destruct-self-before-a-return-in-a-loop-runs -->
A `destruct self` that leaves the function repeats nothing, and the path past the loop destructs the value too.
```maxon
typealias Num = int(i64.min to i64.max)

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function f(consuming self)
		var i = 2 as Num

		while i != 0 'w1'
			if i == 1 'last'
				destruct self
				return
			end 'last'

			i = i - 1
		end 'w1'

		destruct self
	end 'f'
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

<!-- test: a-consuming-receiver-handed-on-in-a-loop-is-refused-on-the-next-lap -->
Every way of moving the receiver out reads it first, so a loop that moves it is judged by its next lap.
```maxon
typealias Num = int(i64.min to i64.max)

type Fz
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'

	function f(consuming self)
		var i = 2 as Num

		while i != 0 'w1'
			sink(self)
			i = i - 1
		end 'w1'
	end 'f'
end 'Fz'

function sink(z Fz)
	z.fin()
end 'sink'

function main() returns ExitCode
	let t = Fz.create(0)
	t.f()

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:19:9: use of moved value 'self': it was moved in an earlier iteration of this loop
```

<!-- test: a-linear-value-rebound-as-isolated-in-a-branch-and-a-loop-is-consumed -->
The rebind is judged where it is written: on one arm of an `if`, and once per lap of a loop.
```maxon
typealias Num = int(i64.min to i64.max)

type Resp
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Resp'

function pick(flag bool)
	let r = Resp.create(0) as isolated Resp

	if flag 'whether'
		let a = r as isolated Resp
		a.fin()
	end 'whether' else 'otherwise'
		r.fin()
	end 'otherwise'
end 'pick'

function laps()
	var i = 0 as Num

	while i != 2 'each'
		let r = Resp.create(i) as isolated Resp
		let a = r as isolated Resp
		a.fin()
		i = i + 1
	end 'each'
end 'laps'

function main() returns ExitCode
	pick(true)
	pick(false)
	laps()

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-finalizer-reads-nothing-of-its-receiver-after-destruct -->
`destruct self` moves the receiver, so a read of it afterwards is use after move.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self) returns Code
		destruct self
		return self.code
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(1)
	let _ = r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:13:10: use of moved value 'self': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-finalizer-writes-nothing-of-its-receiver-after-destruct -->
A field write through the receiver is a use of it.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		destruct self
		self.code = 3
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(1)
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:13:3: use of moved value 'self': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-method-that-does-not-take-consuming-self-cannot-return-it -->
`self` of a method that does not take `consuming self` is only lent, so returning it would give the caller a second owner of a value one finalizer ends.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function me() returns Resp
		return self
	end 'me'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(1)
	let again = r.me()
	again.fin()
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:12:10: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
```

<!-- test: a-method-that-does-not-take-consuming-self-cannot-build-a-union-around-it -->
A union literal holding the lent receiver moves it into the union, which only the code that owns it may do.
```maxon
typealias Code = int(0 to 999)

union Sent
	ok
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function wrap() returns Sent
		return Sent.notSent(self)
	end 'wrap'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Resp'

function main() returns ExitCode
	let r = Resp.create(1)
	let s = r.wrap()
	match s 'x'
		ok then break
		notSent(held) then held.fin()
	end 'x'
	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:17:27: this linear value is only lent to this code (a parameter of a function that does not take it, or `self` of a method that does not take `consuming self`), and only the code that owns a linear value consumes it
```

<!-- test: a-union-carrying-a-linear-value-cannot-be-cloned -->
A union with a linear payload is linear and unique, so `clone()` is refused as it is for the record.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Resp'

union Held
	none
	held(r Resp)
end 'Held'

function settle(h Held)
	match h 'x'
		none then break
		held(r) then r.fin()
	end 'x'
end 'settle'

function main() returns ExitCode
	let u = Held.held(Resp.create(1))
	let c = u.clone()
	settle(u)
	settle(c)
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:30:10: 'Held' is unique (its field 'r' holds a unique 'Resp') and cannot be cloned
```

<!-- test: a-variable-refilled-in-only-one-branch-is-moved-after-the-branch -->
`r` is moved before the `if` and refilled only in its `then` branch, so after the `if` it is moved on the path that skipped the refill.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Resp'

function again() returns bool
	return false
end 'again'

function main() returns ExitCode
	var r = Resp.create(0)
	r.fin()

	if again() 'refill'
		r = Resp.create(1)
	end 'refill'

	r.fin()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:2: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-variable-refilled-in-every-branch-is-live-after-the-branches -->
The `else` refills too, so `r` is live on both paths and the last `fin` consumes it.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function again() returns bool
	return false
end 'again'

function main() returns ExitCode
	var r = Resp.create(0)
	r.fin()

	if again() 'refill'
		r = Resp.create(1)
	end 'refill' else 'refillOther'
		r = Resp.create(2)
	end 'refillOther'

	r.fin()
	return 0
end 'main'
```
```stdout
fin 0
fin 2
```
```exitcode
0
```

<!-- test: a-match-in-the-then-branch-leaves-the-union-to-the-else-branch -->
The `then` branch consumes `m` by matching it, and the `else` branch is a different path that still owns it.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	nothing
	some(r isolated Resp)
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		nothing then break
		some(r) then r.fin()
	end 'which'
end 'settle'

function ready(n Code) returns bool
	return n > 0
end 'ready'

function main() returns ExitCode
	let m = Maybe.some(Resp.create(1))

	if ready(0) 'pick'
		match m 'direct'
			nothing then break
			some(r) then r.fin()
		end 'direct'
	end 'pick' else 'other'
		settle(m)
	end 'other'

	return 0
end 'main'
```
```stdout
fin 1
```
```exitcode
0
```

<!-- test: each-branch-of-an-if-may-match-the-same-linear-union -->
Neither branch sees the other's consumption of `m`.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	nothing
	some(r isolated Resp)
end 'Maybe'

function ready(n Code) returns bool
	return n > 0
end 'ready'

function main() returns ExitCode
	let m = Maybe.some(Resp.create(2))

	if ready(1) 'pick'
		match m 'first'
			nothing then break
			some(r) then r.fin()
		end 'first'
	end 'pick' else 'other'
		match m 'second'
			nothing then break
			some(r) then r.fin()
		end 'second'
	end 'other'

	return 0
end 'main'
```
```stdout
fin 2
```
```exitcode
0
```

<!-- test: a-union-consumed-on-one-path-is-consumed-after-the-join -->
After the `if` joins, one path consumed `m`, so reading it again is a use after the move.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	nothing
	some(r isolated Resp)
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		nothing then break
		some(r) then r.fin()
	end 'which'
end 'settle'

function ready(n Code) returns bool
	return n > 0
end 'ready'

function main() returns ExitCode
	let m = Maybe.some(Resp.create(3))

	if ready(1) 'pick'
		match m 'direct'
			nothing then break
			some(r) then r.fin()
		end 'direct'
	end 'pick'

	settle(m)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:43:9: use of moved value 'm': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-union-with-a-managed-payload-is-matched-in-each-branch-of-an-if -->
The same holds for a union that is not linear: a branch that consumed the payload does not stop the other branch matching it.
```maxon
typealias Code = int(0 to 999)

union Name
	none
	some(text String)
end 'Name'

function ready(n Code) returns bool
	return n > 0
end 'ready'

function main() returns ExitCode
	let n = Name.some("left")

	if ready(1) 'pick'
		match n 'first'
			none then break
			some(text) then print("first {text}\n")
		end 'first'
	end 'pick' else 'other'
		match n 'second'
			none then break
			some(text) then print("second {text}\n")
		end 'second'
	end 'other'

	return 0
end 'main'
```
```stdout
first left
```
```exitcode
0
```

<!-- test: a-linear-union-held-in-an-isolated-local-is-matched-where-it-stands -->
`m` is an `isolated` local that holds a linear union, and the `match` consumes it.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	nothing
	some(r isolated Resp)
end 'Maybe'

function ready(n Code) returns bool
	return n > 0
end 'ready'

function main() returns ExitCode
	let m = Maybe.some(Resp.create(4)) as isolated Maybe

	if ready(1) 'pick'
		match m 'direct'
			nothing then break
			some(r) then r.fin()
		end 'direct'
	end 'pick' else 'other'
		match m 'again'
			nothing then break
			some(r) then r.fin()
		end 'again'
	end 'other'

	return 0
end 'main'
```
```stdout
fin 4
```
```exitcode
0
```

<!-- test: a-behaviour-matches-its-own-linear-union-parameter-in-both-branches -->
The message parameter `m` is owned by the behaviour, which consumes it on whichever branch runs.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	nothing
	some(r isolated Resp)
end 'Maybe'

function settle(m Maybe)
	match m 'which'
		nothing then break
		some(r) then r.fin()
	end 'which'
end 'settle'

type Desk
	var served as Code

	static function create() returns Self
		return Self{served: 0}
	end 'create'

	export function serve(m Maybe, direct bool)
		self.served = 1

		if direct 'pick'
			match m 'inline'
				nothing then break
				some(r) then r.fin()
			end 'inline'
		end 'pick' else 'other'
			settle(m)
		end 'other'
	end 'serve'
end 'Desk'

function main() returns ExitCode
	let desk = spawn Desk.create()

	try desk.serve(Maybe.some(Resp.create(5)), direct: true) otherwise (failure) 'refused'
		match failure 'why'
			mailboxFull(back, _) then settle(back)
			serviceStopped(back, _) then settle(back)
		end 'why'
	end 'refused'

	return 0
end 'main'
```
```stdout
fin 5
```
```exitcode
0
```

<!-- test: a-handler-refills-the-union-the-body-refilled-before-it-threw -->
`m` holds `Maybe.nothing` at the only point the body throws from, so the handler assigns it the linear case without dropping one.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

union Maybe
	nothing
	some(r isolated Resp)
end 'Maybe'

enum Oops implements Error
	bad
end 'Oops'

function trouble() throws Oops
	throw Oops.bad
end 'trouble'

function settle(m Maybe)
	match m 'which'
		nothing then break
		some(r) then r.fin()
	end 'which'
end 'settle'

function main() returns ExitCode
	var m = Maybe.nothing

	try 'body'
		m = Maybe.nothing
		trouble()
	end 'body'
	otherwise (e) 'handler'
		match e 'kind'
			bad then m = Maybe.some(Resp.create(6))
		end 'kind'
	end 'handler'

	settle(m)
	return 0
end 'main'
```
```stdout
fin 6
```
```exitcode
0
```

<!-- test: an-outer-isolated-linear-value-cannot-enter-a-modify-block-through-a-consuming-receiver-that-builds-a-union-around-it -->
`wrap` takes `consuming self` and builds a `Sent` around it, so the call moves `v` into the open region just as a keeping parameter does. A union with one payload is held in a word, and the word still keeps what it is built from.

```maxon
typealias Code = int(0 to 999)

union Sent
	delivered
	notSent(r Resp)
end 'Sent'

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'

	function wrap(consuming self) returns Sent
		return Sent.notSent(self)
	end 'wrap'
end 'Resp'

type Bag
	var n as Code

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Bag'

function settle(s Sent)
	match s 'x1'
		delivered then break
		notSent(r) then r.fin()
	end 'x1'
end 'settle'

function main() returns ExitCode
	let v = Resp.create(1) as isolated Resp
	let bx = Bag.create() as isolated Bag

	modify bx 'w'
		settle(v.wrap())
	end 'w'

	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:46:10: an isolated value from outside 'w' cannot be moved into the parameter 'self' of 'Resp.wrap', which keeps it while 'w' is open: it would become part of the open region. Move it into an `isolated` parameter or an `isolated` slot, send it, or `merge` it
```

<!-- test: an-isolated-linear-local-moves-into-a-method-of-an-opened-isolated-receiver -->
`bx.eat(v)` opens `bx` for the call; `v` is an un-opened `isolated` local and `eat` owns its linear parameter, so `v` is moved, not opened, and its region joins the opened one.
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

union Sn
	ok
	notSent(r Rl)
end 'Sn'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'm'
		ok then return
		notSent(r) then r.fin(1)
	end 'm'
end 'useS'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(r Rl)
		r.fin(1)
	end 'eat'
end 'Bx'

function main() returns ExitCode
	useS(giveS(Rl.create(0)))
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	bx.eat(v)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-isolated-linear-local-moves-into-a-method-that-builds-a-union-around-it -->
The same move, with the method wrapping its parameter in a union it consumes.
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

union Sn
	ok
	notSent(r Rl)
end 'Sn'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'm'
		ok then return
		notSent(r) then r.fin(1)
	end 'm'
end 'useS'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(r Rl)
		useS(giveS(r))
	end 'eat'
end 'Bx'

function main() returns ExitCode
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	bx.eat(v)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-non-linear-isolated-local-into-an-unmarked-parameter-beside-an-opened-receiver-is-refused -->
An unmarked parameter of a record type lends its argument, so `v` would be a second region opened by the call.
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

union Sn
	ok
	notSent(r Rl)
end 'Sn'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'm'
		ok then return
		notSent(r) then r.fin(1)
	end 'm'
end 'useS'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(r Bx)
		r.n = 1
	end 'eat'
end 'Bx'

function main() returns ExitCode
	useS(giveS(Rl.create(0)))
	let bx = Bx.create(0) as isolated Bx
	let v = Bx.create(0) as isolated Bx
	bx.eat(v)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:49:9: one statement reaches through two isolated values, 'bx' and 'v'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: a-plain-linear-local-beside-an-opened-receiver-is-refused -->
`v` is ordinary data of the caller's region, which a call into `bx`'s region does not take.
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

union Sn
	ok
	notSent(r Rl)
end 'Sn'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'm'
		ok then return
		notSent(r) then r.fin(1)
	end 'm'
end 'useS'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(r Rl)
		r.fin(1)
	end 'eat'
end 'Bx'

function main() returns ExitCode
	useS(giveS(Rl.create(0)))
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0)
	bx.eat(v)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:49:9: 'v' is mutable outside 'bx'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: a-move-into-a-method-of-an-outer-isolated-receiver-inside-a-window-is-refused -->
Inside 'mh' the receiver `bx` is outer data and cannot be opened.
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

union Sn
	ok
	notSent(r Rl)
end 'Sn'

function giveS(r Rl) returns Sn
	return Sn.notSent(r)
end 'giveS'

function useS(s Sn)
	match s 'm'
		ok then return
		notSent(r) then r.fin(1)
	end 'm'
end 'useS'

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function eat(r Rl)
		r.fin(1)
	end 'eat'
end 'Bx'

function main() returns ExitCode
	useS(giveS(Rl.create(0)))
	let h = Bx.create(0) as isolated Bx
	let bx = Bx.create(0) as isolated Bx
	let v = Rl.create(0) as isolated Rl
	modify h 'mh'
		bx.eat(v)
	end 'mh'
	let _ = h
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:51:3: 'bx' cannot be opened: the value that holds it is read-only while 'mh' is open
```

<!-- test: a-spawn-of-a-factory-that-cannot-throw-takes-a-linear-argument -->
`create` cannot throw, so the service always starts and its factory consumes `r`; `Rl` is closed, so `r` crosses whole.
```maxon
typealias Num = int(i64.min to i64.max)
type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Rl'

type Starter
	var k as Num

	static function create(r Rl) returns Self
		let c = r.code
		r.fin()
		return Self{k: c}
	end 'create'

	export function poke()
		print("poke {self.k}\n")
	end 'poke'
end 'Starter'

function main() returns ExitCode
	let r = Rl.create(3)
	let s = spawn Starter.create(r)
	try s.poke() otherwise ignore
	return 0
end 'main'
```
```stdout
fin 3
poke 3
```
```exitcode
0
```

<!-- test: a-linear-factory-argument-of-a-type-that-is-not-closed-does-not-cross -->
`Mp` holds the plain payload `y`, which `b` still names after the spawn.
```maxon
typealias Num = int(i64.min to i64.max)
type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Rl'
type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'
union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

function drain(m Mp)
	match m 'drained'
		m0(x) then x.fin()
		m1(y) then print("drained {y.n}\n")
	end 'drained'
end 'drain'

type Starter
	var k as Num

	static function create(m Mp) returns Self
		drain(m)
		return Self{k: 0}
	end 'create'

	export function poke()
		print("poke {self.k}\n")
	end 'poke'
end 'Starter'

function main() returns ExitCode
	let b = Bx.create(1)
	let m = Mp.m1(b)
	let s = spawn Starter.create(m)
	try s.poke() otherwise ignore
	b.n = 2
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:50:31: 'm' crosses to the service the `spawn` starts only as the whole of its region, and 'Mp' is not closed: 'Mp.y' is a plain 'Bx' that other code may still name. Declare 'y' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: a-local-refilled-on-every-lap-is-recovered-at-the-start-of-each-lap -->
Every value `w` holds at the loop's header is a fresh one nothing has read: the one before the loop and the one each lap assigns.
```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeIso(b isolated Bx)
	let _ = b
end 'takeIso'

function main() returns ExitCode
	var w = Bx.create(1)
	var i = 0 as Num
	while i < 3 'l'
		takeIso(w)
		w = Bx.create(2)
		i = i + 1
	end 'l'
	w.n = 4
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-local-refilled-with-an-alias-is-not-recovered-in-the-loop -->
After `takeIso` recovers `w`, the variable takes only isolated values, and `keep` is a record another name holds.
```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

function takeIso(b isolated Bx)
	let _ = b
end 'takeIso'

function main() returns ExitCode
	let keep = Bx.create(9)
	var w = Bx.create(1)
	var i = 0 as Num
	while i < 3 'l'
		takeIso(w)
		w = keep
		i = i + 1
	end 'l'
	w.n = 4
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:22:7: 'keep' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Bx`, or store a copy
```

<!-- test: a-unique-local-sent-on-every-lap-is-refused-when-a-lap-reads-the-value-it-refills -->
`t` crosses whole only because the value it holds at the header is fresh, and the value each lap assigns is read by `h.k = t.b` before the next lap sends it, so `h` would still name a part of what the service holds.
```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tp is unique
	var b as Bx

	static function create(n Num) returns Self
		return Self{b: Bx.create(n)}
	end 'create'
end 'Tp'

type Holder
	var k as Bx

	static function create() returns Self
		return Self{k: Bx.create(0)}
	end 'create'
end 'Holder'

type TpTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(t Tp)
		self.seen = t.b.n
	end 'take'
end 'TpTaker'

function main() returns ExitCode
	let s = spawn TpTaker.create(0)
	let h = Holder.create()
	var t = Tp.create(1)
	var i = 0 as Num

	while i < 3 'laps'
		try s.take(t) otherwise ignore
		t = Tp.create(2)
		h.k = t.b
		i = i + 1
	end 'laps'

	h.k.n = 4
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:47:15: 't' is recovered into an isolated position inside a loop, so every lap must start with a fresh value in it that nothing has read: assign it a fresh value on every path to the end of the lap, `continue` included
```

<!-- test: a-unique-local-sent-in-an-inner-loop-is-refused-when-an-outer-lap-reads-the-value-it-refills -->
The inner loop's header holds the value the outer loop's header holds, so relying on it in the inner loop relies on the outer header too: `h.k = t.b` reads what the last inner lap assigned, and the next outer lap sends it.
```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tp is unique
	var b as Bx

	static function create(n Num) returns Self
		return Self{b: Bx.create(n)}
	end 'create'
end 'Tp'

type Holder
	var k as Bx

	static function create() returns Self
		return Self{k: Bx.create(0)}
	end 'create'
end 'Holder'

type TpTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(t Tp)
		self.seen = t.b.n
	end 'take'
end 'TpTaker'

function main() returns ExitCode
	let s = spawn TpTaker.create(0)
	let h = Holder.create()
	var t = Tp.create(1)
	var i = 0 as Num

	while i < 3 'outer'
		var j = 0 as Num

		while j < 2 'inner'
			try s.take(t) otherwise ignore
			t = Tp.create(2)
			j = j + 1
		end 'inner'

		h.k = t.b
		i = i + 1
	end 'outer'

	h.k.n = 4
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:50:16: 't' is recovered into an isolated position inside a loop, so every lap must start with a fresh value in it that nothing has read: assign it a fresh value on every path to the end of the lap, `continue` included
```

<!-- test: a-unique-local-sent-in-an-inner-loop-is-accepted-when-every-outer-lap-refills-it-after-reading -->
Each outer lap reads what the last inner lap assigned and then assigns `t` a fresh value, so the value `h.k` names a part of is never sent.
```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tp is unique
	var b as Bx

	static function create(n Num) returns Self
		return Self{b: Bx.create(n)}
	end 'create'
end 'Tp'

type Holder
	var k as Bx

	static function create() returns Self
		return Self{k: Bx.create(0)}
	end 'create'
end 'Holder'

type TpTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(t Tp)
		self.seen = t.b.n
	end 'take'
end 'TpTaker'

function main() returns ExitCode
	let s = spawn TpTaker.create(0)
	let h = Holder.create()
	var t = Tp.create(1)
	var i = 0 as Num

	while i < 3 'outer'
		var j = 0 as Num

		while j < 2 'inner'
			try s.take(t) otherwise ignore
			t = Tp.create(2)
			j = j + 1
		end 'inner'

		h.k = t.b
		t = Tp.create(3)
		i = i + 1
	end 'outer'

	h.k.n = 4
	print("{h.k.n}\n")
	return 0
end 'main'
```
```stdout
4
```
```exitcode
0
```

<!-- test: a-unique-local-sent-on-every-lap-of-a-for-loop-is-refused-when-a-lap-reads-the-value-it-refills -->
A `for` loop's header joins `t` the way a `while` loop's does.
```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tp is unique
	var b as Bx

	static function create(n Num) returns Self
		return Self{b: Bx.create(n)}
	end 'create'
end 'Tp'

type Holder
	var k as Bx

	static function create() returns Self
		return Self{k: Bx.create(0)}
	end 'create'
end 'Holder'

type TpTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(t Tp)
		self.seen = t.b.n
	end 'take'
end 'TpTaker'

function main() returns ExitCode
	let s = spawn TpTaker.create(0)
	let h = Holder.create()
	var t = Tp.create(1)

	for i in 0 upto 3 'laps'
		try s.take(t) otherwise ignore
		t = Tp.create(i)
		h.k = t.b
	end 'laps'

	h.k.n = 4
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:46:15: 't' is recovered into an isolated position inside a loop, so every lap must start with a fresh value in it that nothing has read: assign it a fresh value on every path to the end of the lap, `continue` included
```

<!-- test: a-unique-local-sent-on-every-lap-and-refilled-with-a-fresh-value-crosses -->
Every value `t` holds at the header is fresh and unread when the lap sends it.
```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tp is unique
	var b as Bx

	static function create(n Num) returns Self
		return Self{b: Bx.create(n)}
	end 'create'
end 'Tp'

type TpTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(t Tp)
		self.seen = t.b.n
	end 'take'
end 'TpTaker'

function main() returns ExitCode
	let s = spawn TpTaker.create(0)
	var t = Tp.create(1)
	var i = 0 as Num

	while i < 3 'laps'
		try s.take(t) otherwise ignore
		t = Tp.create(i)
		i = i + 1
	end 'laps'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-vacant-linear-union-is-copied-into-a-callee-that-sends-it -->
`s` holds the payload-free case on every path, so it owes nothing: each call takes a copy, and `relay` sends its parameter, whose type is closed.
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

union Sn
	ok
	notSent(r Rl)
end 'Sn'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeS(s Sn)
		match s 'm'
			ok then return
			notSent(r) then r.fin(1)
		end 'm'
	end 'takeS'
end 'Hq'

function relay(a Hq.handle, s Sn)
	try a.takeS(s) otherwise (q) 'h'
		match q 'x'
			mailboxFull(z) then drain(z)
			serviceStopped(z) then drain(z)
		end 'x'
	end 'h'
end 'relay'

function drain(s Sn)
	match s 'm'
		ok then return
		notSent(r) then r.fin(1)
	end 'm'
end 'drain'

function main() returns ExitCode
	let a = spawn Hq.create(0)
	let s = Sn.ok as isolated Sn
	relay(a, s: s)
	relay(a, s: s)
	drain(Sn.notSent(Rl.create(1)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-vacant-linear-union-local-is-passed-twice-to-a-callee-that-sends-it -->
The same with an unmarked `let`.
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

union Sn
	ok
	notSent(r Rl)
end 'Sn'

type Hq
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	export function takeS(s Sn)
		match s 'm'
			ok then return
			notSent(r) then r.fin(1)
		end 'm'
	end 'takeS'
end 'Hq'

function relay(a Hq.handle, s Sn)
	try a.takeS(s) otherwise (q) 'h'
		match q 'x'
			mailboxFull(z) then drain(z)
			serviceStopped(z) then drain(z)
		end 'x'
	end 'h'
end 'relay'

function drain(s Sn)
	match s 'm'
		ok then return
		notSent(r) then r.fin(1)
	end 'm'
end 'drain'

function main() returns ExitCode
	let a = spawn Hq.create(0)
	let s = Sn.ok
	relay(a, s: s)
	relay(a, s: s)
	drain(Sn.notSent(Rl.create(1)))
	return 0
end 'main'
```
```exitcode
0
```
