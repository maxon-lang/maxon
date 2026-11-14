---
feature: deferred-reply
status: experimental
keywords: [service, message, reply, later, answer, continuation, deferred]
category: concurrency
---

# A message may answer later

## Documentation

A message that returns a value answers when its handler returns. `reply.later()` lets a message answer from somewhere
else instead: written as the whole initializer of a `let` at the top level of the message's body, it takes the message's
reply and hands back a `Reply with T`, where `T` is the message's reply type. The caller's `await` stays pending until some
service calls `answer(value)` on that `Reply`, which consumes it and completes the caller's reply. A `Reply` is unique: it
lives in a service's state, in a container, or in a message argument, and it moves like any unique value, so it is
answered at most once. A message that defers declares `throws ReplyStopped`, because a `Reply` dropped unanswered, which
includes a holder freed with it still inside, fails the caller's `await` with `ReplyStopped.serviceStopped`.

After `reply.later()` the message's own `return` carries no value and no `throw` is written: the answer travels through the
`Reply`. `reply.later()` is refused anywhere but the first-level body of a message of a service, in a message with no
`returns` clause or with an interface-typed one, in a generic service, and a deferring message cannot be called other than
by a send through the service's handle. `answer` is a send-like operation that never waits, so any message body may call it.
A `Reply` is not a promise: it cannot be awaited, and it is not what E3216 or E3217 refuse.

A `Reply` never leaves holding code. No function declares a result whose type can hold a `Reply` (E3222) — which covers a
message's reply, a promise's payload and a helper's result — and no `async` call takes a parameter whose type can hold one;
a `Reply` taken out of an acquired state cannot outlive the statement that opens the state (E3202). No closure captures a
value that can hold a `Reply`, and no such value is widened to an interface type, because a function value and an interface
value both carry what they hold to whatever code receives them (E3222). Inside service code it moves freely: state, fields,
containers and message arguments. The check walks declarations — a service's own state is
held by its cown and is not walked — the way the service handle cycle check does.

## Tests

<!-- test: a-reply-answered-later-from-a-continuation -->
`ask` parks its `Reply` in the service's state and returns; `settle` is the continuation, and answers it with what the
deposits made. `main` awaits the stored reply, so its `await` completes only when `settle` runs.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally
typealias TallyReplyArray = Array with TallyReply

type Ledger
	var total as Tally
	var waiting as TallyReplyArray

	static function create() returns Self
		return Self{total: 0, waiting: TallyReplyArray.create(0)}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		self.waiting.push(pending)
		return
	end 'ask'

	export function deposit(amount Tally)
		self.total = self.total + amount
	end 'deposit'

	export function settle()
		let pending = try self.waiting.pop() otherwise return
		pending.answer(self.total)
	end 'settle'
end 'Ledger'

function main() returns ExitCode
	let ledger = spawn Ledger.create()
	let asked = try ledger.ask() otherwise panic("a running service with room takes the send")
	try ledger.deposit(40) otherwise panic("a running service with room takes the send")
	try ledger.deposit(2) otherwise panic("a running service with room takes the send")
	try ledger.settle() otherwise panic("a running service with room takes the send")
	let total = try await asked otherwise panic("the continuation answered")
	print("{total}\n")
	return 0
end 'main'
```
```stdout
42
```

<!-- test: a-reply-answered-by-another-service -->
`Desk.ask` hands its `Reply` to `Clerk.look` in a message of its own, and `Clerk` answers it from there. Neither message
waits: `Desk` returns as soon as it has sent, and the answer reaches `main` from the `Clerk`.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

type Clerk
	var served as Tally

	static function create() returns Self
		return Self{served: 0}
	end 'create'

	export function look(waiting TallyReply)
		self.served = self.served + 1
		waiting.answer(self.served * 21)
	end 'look'
end 'Clerk'

type Desk
	let clerk as Clerk.handle

	static function create(clerk Clerk.handle) returns Self
		return Self{clerk: clerk}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		try self.clerk.look(pending) otherwise ignore
		return
	end 'ask'
end 'Desk'

function main() returns ExitCode
	let clerk = spawn Clerk.create()
	let desk = spawn Desk.create(clerk.clone())
	let total = try await desk.ask() otherwise panic("the clerk answered")
	print("{total}\n")
	return 0
end 'main'
```
```stdout
21
```

<!-- test: a-reply-parameter-answered-on-each-branch-is-answered-once -->
`Clerk.look` receives the `Reply` as a parameter and answers it in one arm of an `if` or in the other, so each path answers it once.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

type Clerk
	static function create() returns Self
		return Self{}
	end 'create'

	export function look(waiting TallyReply, large bool)
		if large 'big'
			waiting.answer(100)
		end 'big' else 'small'
			waiting.answer(1)
		end 'small'
	end 'look'
end 'Clerk'

type Desk
	let clerk as Clerk.handle

	static function create(clerk Clerk.handle) returns Self
		return Self{clerk: clerk}
	end 'create'

	export function ask(large bool) returns Tally throws ReplyStopped
		let pending = reply.later()
		try self.clerk.look(pending, large: large) otherwise ignore
		return
	end 'ask'
end 'Desk'

function main() returns ExitCode
	let clerk = spawn Clerk.create()
	let desk = spawn Desk.create(clerk.clone())
	let first = try await desk.ask(true) otherwise panic("the clerk answered")
	let second = try await desk.ask(false) otherwise panic("the clerk answered")
	print("{first} {second}\n")
	return 0
end 'main'
```
```stdout
100 1
```

<!-- test: a-reply-parameter-answered-in-a-loop-that-returns-is-answered-once -->
The loop answers the `Reply` parameter and returns in the same arm, so no path reaches an answer after one was made; the answer after the loop is reached only when the loop ran out.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

type Clerk
	static function create() returns Self
		return Self{}
	end 'create'

	export function look(waiting TallyReply, limit Tally)
		for step in 0 upto limit 'each'
			if step == 3 'hit'
				waiting.answer(step)
				return
			end 'hit'
		end 'each'

		waiting.answer(limit)
	end 'look'
end 'Clerk'

type Desk
	let clerk as Clerk.handle

	static function create(clerk Clerk.handle) returns Self
		return Self{clerk: clerk}
	end 'create'

	export function ask(limit Tally) returns Tally throws ReplyStopped
		let pending = reply.later()
		try self.clerk.look(pending, limit: limit) otherwise ignore
		return
	end 'ask'
end 'Desk'

function main() returns ExitCode
	let clerk = spawn Clerk.create()
	let desk = spawn Desk.create(clerk.clone())
	let hit = try await desk.ask(10) otherwise panic("the clerk answered")
	let ran = try await desk.ask(2) otherwise panic("the clerk answered")
	print("{hit} {ran}\n")
	return 0
end 'main'
```
```stdout
3 2
```

<!-- test: error.a-reply-parameter-answered-in-a-loop-is-moved-in-an-earlier-lap -->
A `Reply` parameter answered inside a loop that goes round again is answered once per lap.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

type Sink
	static function create() returns Self
		return Self{}
	end 'create'

	export function take(parked TallyReply, laps Tally)
		for lap in 0 upto laps 'each'
			parked.answer(lap)
		end 'each'
	end 'take'
end 'Sink'

function main() returns ExitCode
	let sink = spawn Sink.create()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:12:21: use of moved value 'parked': it was moved in an earlier iteration of this loop
```

<!-- test: a-reply-dropped-with-its-holder-fails-the-await-with-service-stopped -->
`Parker` keeps its `Reply` in its state and `main` shuts the service down without anything answering it. The service's
state is dropped when it finishes, the `Reply` inside it is dropped unanswered, and the caller's `await` receives
`ReplyStopped.serviceStopped`.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally
typealias TallyReplyArray = Array with TallyReply

type Parker
	var waiting as TallyReplyArray

	static function create() returns Self
		return Self{waiting: TallyReplyArray.create(0)}
	end 'create'

	export function park() returns Tally throws ReplyStopped
		let pending = reply.later()
		self.waiting.push(pending)
		return
	end 'park'
end 'Parker'

function main() returns ExitCode
	let parker = spawn Parker.create()
	let parked = try parker.park() otherwise panic("a running service with room takes the send")
	parker.shutdown()

	try await parked otherwise (e) 'dropped'
		match e 'why'
			serviceStopped then return 3
		end 'why'
	end 'dropped'

	return 0
end 'main'
```
```exitcode
3
```

<!-- test: a-reply-dropped-at-the-end-of-its-own-message-fails-the-await -->
`ask(wake)` answers at once when told to and otherwise lets the `Reply` go out of scope with the message. The first
`await` yields the answer and the second the stop.
```maxon
typealias Tally = int(0 to u64.max)

type Waker
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask(wake bool) returns Tally throws ReplyStopped
		let pending = reply.later()

		if wake 'answerNow'
			pending.answer(7)
		end 'answerNow'

		return
	end 'ask'
end 'Waker'

function main() returns ExitCode
	let waker = spawn Waker.create()
	let answered = try waker.ask(true) otherwise panic("a running service with room takes the send")
	let dropped = try waker.ask(false) otherwise panic("a running service with room takes the send")
	let seven = try await answered otherwise panic("the first message answered itself")

	try await dropped otherwise (e) 'stopped'
		match e 'why'
			serviceStopped then return (seven + 1) as ExitCode
		end 'why'
	end 'stopped'

	return 0
end 'main'
```
```exitcode
8
```

<!-- test: a-reply-answered-twice-is-refused -->
`answer` consumes the `Reply`, so the second call reads a moved value.
```maxon
typealias Tally = int(0 to u64.max)

type Twice
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		pending.answer(1)
		pending.answer(2)
		return
	end 'ask'
end 'Twice'

function main() returns ExitCode
	let twice = spawn Twice.create()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:12:3: use of moved value 'pending': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.a-reply-message-parameter-answered-twice-is-refused -->
A `Reply` a message receives is as unique as one it makes, so a second `answer` on the parameter reads a moved value.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

type Sink
	static function create() returns Self
		return Self{}
	end 'create'

	export function take(parked TallyReply)
		parked.answer(1)
		parked.answer(2)
	end 'take'
end 'Sink'

function main() returns ExitCode
	let sink = spawn Sink.create()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:12:3: use of moved value 'parked': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.a-reply-function-parameter-answered-twice-is-refused -->
A `Reply` a helper function receives is answered at most once there too.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

function answerTwice(waiting TallyReply)
	waiting.answer(1)
	waiting.answer(2)
end 'answerTwice'

type Gate
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		answerTwice(pending)
		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:7:2: use of moved value 'waiting': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.a-reply-parameter-answered-then-passed-on-is-refused -->
A function that answers its `Reply` parameter has no `Reply` left to hand to another function.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

function answerNow(waiting TallyReply)
	waiting.answer(3)
end 'answerNow'

function relay(waiting TallyReply)
	waiting.answer(1)
	answerNow(waiting)
end 'relay'

type Gate
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		relay(pending)
		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:11:12: use of moved value 'waiting': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.a-union-parameter-holding-a-reply-matched-twice-is-refused -->
A union parameter that carries a `Reply` is consumed by the `match` that answers its payload, so a second `match` reads a moved value.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

union Slot
	held(waiting TallyReply)
	empty
end 'Slot'

function drain(slot Slot)
	match slot 'first'
		held(q) then q.answer(1)
		empty then print("empty\n")
	end 'first'

	match slot 'second'
		held(q) then q.answer(2)
		empty then print("empty\n")
	end 'second'
end 'drain'

type Gate
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		drain(Slot.held(pending))
		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:16:8: use of moved value 'slot': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-reply-cannot-be-awaited -->
A `Reply` is an obligation to answer, not a coroutine handle, so `await` refuses it.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

type Sink
	static function create() returns Self
		return Self{}
	end 'create'

	export function take(waiting TallyReply)
		let n = await waiting
	end 'take'
end 'Sink'

function main() returns ExitCode
	let sink = spawn Sink.create()
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:11: 'await' requires a promise value from 'async', got genericInstance
```

<!-- test: error.reply-later-inside-a-block-is-refused -->
`reply.later()` is a statement of the message's own body: inside an `if` it would defer on some paths and not others.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally
typealias TallyReplyArray = Array with TallyReply

type Gate
	var waiting as TallyReplyArray

	static function create() returns Self
		return Self{waiting: TallyReplyArray.create(0)}
	end 'create'

	export function ask(now bool) returns Tally throws ReplyStopped
		if now 'sometimes'
			let pending = reply.later()
			self.waiting.push(pending)
		end 'sometimes'

		return 0
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:15:18: `reply.later()` is a statement of the message's own body, not of an `if`, a loop, a `match`, a `try` or any other block inside it
```

<!-- test: error.reply-later-needs-the-stopped-clause -->
A `Reply` dropped unanswered fails the caller's `await` with `ReplyStopped`, so the message declares it.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally
typealias TallyReplyArray = Array with TallyReply

type Gate
	var waiting as TallyReplyArray

	static function create() returns Self
		return Self{waiting: TallyReplyArray.create(0)}
	end 'create'

	export function ask() returns Tally
		let pending = reply.later()
		self.waiting.push(pending)
		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:14:17: a message that defers its reply declares `throws ReplyStopped`: the caller's `await` receives it when the Reply is dropped unanswered
```

<!-- test: error.a-deferring-message-returns-no-value -->
After `reply.later()` the answer travels through the `Reply`, so `return` carries nothing.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally
typealias TallyReplyArray = Array with TallyReply

type Gate
	var waiting as TallyReplyArray

	static function create() returns Self
		return Self{waiting: TallyReplyArray.create(0)}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		self.waiting.push(pending)
		return 5
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:16:3: after `reply.later()` the message answers through its Reply, so `return` carries no value: pass the value to `<reply>.answer(value)`
```

<!-- test: error.a-message-with-no-reply-cannot-defer -->
A message that returns nothing has no reply to take.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally
typealias TallyReplyArray = Array with TallyReply

type Gate
	var waiting as TallyReplyArray

	static function create() returns Self
		return Self{waiting: TallyReplyArray.create(0)}
	end 'create'

	export function ask() throws ReplyStopped
		let pending = reply.later()
		self.waiting.push(pending)
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:14:17: a message with no `returns` clause has no reply to defer: give it one, or send the answer another way
```

<!-- test: error.reply-later-outside-a-service-message-is-refused -->
`Plain` is never spawned, so its method is no message and has no reply to take.
```maxon
type Plain
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask() returns Count throws ReplyStopped
		let pending = reply.later()
		pending.answer(1)
		return
	end 'ask'
end 'Plain'

function main() returns ExitCode
	let plain = Plain.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:7:18: `reply.later()` appears in 'ask', which is not a message of a service: only an `export` or `public` method of a type a `spawn` starts can defer its reply
```

<!-- test: error.reply-later-is-the-whole-initializer-of-a-let -->
The `Reply` has to be bound, so it is plain which name owes the answer.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally
typealias TallyReplyArray = Array with TallyReply

type Gate
	var waiting as TallyReplyArray

	static function create() returns Self
		return Self{waiting: TallyReplyArray.create(0)}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		self.waiting.push(reply.later())
		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:14:21: `reply.later()` is the whole initializer of a `let`: `let pending = reply.later()`
```

<!-- test: error.a-message-defers-its-reply-once -->
The first `reply.later()` takes the message's reply, so a second has nothing left to take.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally
typealias TallyReplyArray = Array with TallyReply

type Gate
	var waiting as TallyReplyArray

	static function create() returns Self
		return Self{waiting: TallyReplyArray.create(0)}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let first = reply.later()
		let second = reply.later()
		self.waiting.push(first)
		self.waiting.push(second)
		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:15:16: a message defers its reply once: the second `reply.later()` has no reply left to take
```

<!-- test: error.a-deferring-message-throws-nothing-itself -->
A throw would complete the reply the `Reply` already owns, so a deferring message throws nothing: its one error is
`ReplyStopped`, delivered when the `Reply` is dropped.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally
typealias TallyReplyArray = Array with TallyReply

type Gate
	var waiting as TallyReplyArray

	static function create() returns Self
		return Self{waiting: TallyReplyArray.create(0)}
	end 'create'

	export function ask(refuse bool) returns Tally throws ReplyStopped
		let pending = reply.later()
		self.waiting.push(pending)

		if refuse 'now'
			throw ReplyStopped.serviceStopped
		end 'now'

		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:13:18: the message 'ask' defers its reply and so throws nothing itself: its one error is `ReplyStopped`, which the caller's `await` receives when the Reply is dropped unanswered. Remove the `throw` or the propagating `try`
```

<!-- test: error.a-message-cannot-reply-with-a-reply -->
A `Reply` never leaves the service code that holds it. A message's reply is awaited by code that may wait, and a
caller that awaited a coroutine before answering the `Reply` it was handed would wait on its own answer.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally
typealias TallyReplyArray = Array with TallyReply

type Gate
	var waiting as TallyReplyArray

	static function create() returns Self
		return Self{waiting: TallyReplyArray.create(0)}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		self.waiting.push(pending)
		return
	end 'ask'

	export function hand() returns TallyReply
		return try self.waiting.pop() otherwise panic("one is parked")
	end 'hand'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:19:18: 'hand' returns a type that can hold a Reply: a Reply never leaves the service code that holds it, so no function returns one (a message's reply, a promise's payload and a helper's result all carry it to code that may wait). Keep it in state, a container or a message argument, and answer it where it is held
```

<!-- test: error.a-message-cannot-reply-with-a-generic-over-a-reply -->
The walk substitutes a generic's type arguments, so `Box with TallyReply` holds a `Reply` exactly as its argument does.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally
typealias TallyReplyArray = Array with TallyReply

type Box uses T
	var held as T

	static function create(held T) returns Self
		return Self{held: held}
	end 'create'
end 'Box'

typealias ReplyBox = Box with TallyReply

type Gate
	var waiting as TallyReplyArray

	static function create() returns Self
		return Self{waiting: TallyReplyArray.create(0)}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		self.waiting.push(pending)
		return
	end 'ask'

	export function hand() returns ReplyBox
		return ReplyBox.create(try self.waiting.pop() otherwise panic("one is parked"))
	end 'hand'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:29:18: 'hand' returns a type that can hold a Reply: a Reply never leaves the service code that holds it, so no function returns one (a message's reply, a promise's payload and a helper's result all carry it to code that may wait). Keep it in state, a container or a message argument, and answer it where it is held
```

<!-- test: error.a-helper-cannot-return-a-reply -->
No function returns a type that can hold a `Reply`, a helper called from a message body included.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally
typealias TallyReplyArray = Array with TallyReply

type Gate
	var waiting as TallyReplyArray

	static function create() returns Self
		return Self{waiting: TallyReplyArray.create(0)}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		self.waiting.push(pending)
		return
	end 'ask'

	export function drop()
		_ = unpark(self.waiting)
	end 'drop'
end 'Gate'

function unpark(source TallyReplyArray) returns TallyReply
	return try source.pop() otherwise panic("one is parked")
end 'unpark'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:24:10: 'unpark' returns a type that can hold a Reply: a Reply never leaves the service code that holds it, so no function returns one (a message's reply, a promise's payload and a helper's result all carry it to code that may wait). Keep it in state, a container or a message argument, and answer it where it is held
```

<!-- test: error.a-reply-cannot-be-handed-to-a-coroutine -->
A coroutine is not holding code, so it takes no parameter that can hold a `Reply`.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

type Gate
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		let helper = async answerLater(pending)
		helper.cancel()
		return
	end 'ask'
end 'Gate'

function answerLater(waiting TallyReply) returns Tally
	waiting.answer(1)
	sleep(1)
	return 1
end 'answerLater'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:12:22: `async answerLater(…)` takes a parameter that can hold a Reply: a Reply never leaves the service code that holds it, and a coroutine is not holding code — a coroutine awaiting a Reply while its starter awaits the coroutine would wait on each other for ever
```

<!-- test: error.a-reply-cannot-leave-an-acquire-block -->
A `Reply` taken out of an acquired state is mutable data the statement that opens the state hands back, which the window rule
(E3202) already refuses to let outlive the statement; the shape stays refused for a `Reply`.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally
typealias TallyReplyArray = Array with TallyReply

type Gate
	export var waiting as TallyReplyArray

	static function create() returns Self
		return Self{waiting: TallyReplyArray.create(0)}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		self.waiting.push(pending)
		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	let sink = TallyReplyArray.create(0)

	acquire gate 'held'
		let moved = try gate.waiting.pop() otherwise return 1
		sink.push(moved)
	end 'held' otherwise 'stopped'
		return 2
	end 'stopped'

	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:25:15: 'try gate.waiting.pop() otherwise return 1' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: error.a-deferring-message-cannot-be-called-directly -->
A direct call has no reply cell to take, so only a send through the handle runs a deferring message.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally
typealias TallyReplyArray = Array with TallyReply

type Gate
	var waiting as TallyReplyArray

	static function create() returns Self
		return Self{waiting: TallyReplyArray.create(0)}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		self.waiting.push(pending)
		return
	end 'ask'

	export function again()
		try self.ask() otherwise ignore
	end 'again'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:20:12: 'Gate.ask' defers its reply, so it cannot be called directly: only a send through the service's handle runs it
```

<!-- test: error.a-closure-cannot-capture-a-reply -->
A closure travels to whatever code receives it, so it never captures a value that can hold a `Reply`.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

type Gate
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		let hold = function() gives peek(pending)
		hold()
		return
	end 'ask'
end 'Gate'

function peek(waiting TallyReply) returns Tally
	waiting.answer(1)
	return 1
end 'peek'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:12:14: this closure captures 'pending', which can hold a Reply: a closure travels to whatever code receives it, and a Reply never leaves the service code that holds it. Answer the Reply in the message that holds it, or pass what the answer needs as a message argument
```

<!-- test: error.a-reply-holder-cannot-be-passed-as-an-interface-value -->
An interface value carries its conformer to whatever code receives it, so a value that can hold a `Reply` is not widened to an interface type.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

interface Sink
	function weight() returns Tally
end 'Sink'

union Slot implements Sink
	empty
	waiting(pending TallyReply)

	function weight() returns Tally
		return 1
	end 'weight'
end 'Slot'

function keep(sink Sink) returns Tally
	return sink.weight()
end 'keep'

type Gate
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		_ = keep(Slot.waiting(pending))
		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:29:7: Cannot pass a value as 'sink', which is declared at the interface type 'Sink': a value that can hold a Reply cannot stand at an interface type: a Reply never leaves the service code that holds it, and an interface value carries its conformer to whatever code receives it. Keep the Reply-holding type concrete, or answer the Reply before the value is widened to 'Sink'
```

<!-- test: error.a-reply-holder-cannot-be-returned-as-an-interface-value -->
The same holds at a `return` into an interface-typed result.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

interface Sink
	function weight() returns Tally
end 'Sink'

union Slot implements Sink
	empty
	waiting(pending TallyReply)

	function weight() returns Tally
		return 1
	end 'weight'
end 'Slot'

function wrap(pending TallyReply) returns Sink
	return Slot.waiting(pending)
end 'wrap'

type Gate
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		let sink = wrap(pending)
		_ = sink.weight()
		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:19:2: Cannot return a value from 'wrap' as the interface type 'Sink': a value that can hold a Reply cannot stand at an interface type: a Reply never leaves the service code that holds it, and an interface value carries its conformer to whatever code receives it. Keep the Reply-holding type concrete, or answer the Reply before the value is widened to 'Sink'
```

<!-- test: error.a-reply-holder-cannot-be-stored-in-an-interface-field -->
The same holds at a store into an interface-typed field.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

interface Sink
	function weight() returns Tally
end 'Sink'

union Slot implements Sink
	empty
	waiting(pending TallyReply)

	function weight() returns Tally
		return 1
	end 'weight'
end 'Slot'

type Shelf
	export var sink as Sink

	static function create(pending TallyReply) returns Self
		return Self{sink: Slot.waiting(pending)}
	end 'create'
end 'Shelf'

type Gate
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		let shelf = Shelf.create(pending)
		_ = shelf.sink.weight()
		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3222: <fragment>:22:10: Cannot store a value into the field 'Shelf.sink', which is declared at the interface type 'Sink': a value that can hold a Reply cannot stand at an interface type: a Reply never leaves the service code that holds it, and an interface value carries its conformer to whatever code receives it. Keep the Reply-holding type concrete, or answer the Reply before the value is widened to 'Sink'
```

<!-- test: error.a-reply-given-to-a-function-that-answers-it-is-moved -->
A function that answers a `Reply` consumes it, so the caller that handed it over has no `Reply` left to answer.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

function answerNow(waiting TallyReply)
	waiting.answer(3)
end 'answerNow'

type Gate
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		answerNow(pending)
		pending.answer(4)
		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:17:3: use of moved value 'pending': its ownership moved to the parameter 'waiting' of 'answerNow', which keeps it
```

<!-- test: error.a-reply-message-parameter-given-to-a-function-that-answers-it-is-moved -->
A `Reply` a message receives moves the same way: the function that answers it takes it from the message's body.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

function answerNow(waiting TallyReply)
	waiting.answer(3)
end 'answerNow'

type Sink
	static function create() returns Self
		return Self{}
	end 'create'

	export function take(parked TallyReply)
		answerNow(parked)
		parked.answer(4)
	end 'take'
end 'Sink'

function main() returns ExitCode
	let sink = spawn Sink.create()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:16:3: use of moved value 'parked': its ownership moved to the parameter 'waiting' of 'answerNow', which keeps it
```

<!-- test: error.a-reply-given-to-a-function-that-parks-it-is-moved -->
A function that keeps the `Reply` in a container owns it too, so the caller cannot answer it afterwards.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally
typealias TallyReplyArray = Array with TallyReply

function parkIn(waiting TallyReply, shelf TallyReplyArray)
	shelf.push(waiting)
end 'parkIn'

type Gate
	var shelf as TallyReplyArray

	static function create() returns Self
		return Self{shelf: TallyReplyArray.create(0)}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		parkIn(pending, shelf: self.shelf)
		pending.answer(4)
		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:20:3: use of moved value 'pending': its ownership moved to the parameter 'waiting' of 'parkIn', which keeps it
```

<!-- test: error.a-reply-given-to-a-function-in-a-loop-is-moved-in-an-earlier-lap -->
A loop that hands the same `Reply` to an answering function again on its next lap reads a moved value.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

function answerNow(waiting TallyReply)
	waiting.answer(3)
end 'answerNow'

type Gate
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()

		for _ in 0 upto 2 'laps'
			answerNow(pending)
		end 'laps'

		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:18:14: use of moved value 'pending': it was moved in an earlier iteration of this loop
```

<!-- test: error.a-reply-given-to-a-function-on-one-branch-is-moved-after-the-branch -->
A `Reply` handed over on one branch is moved once the branches join, so answering it afterwards is refused.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

function answerNow(waiting TallyReply)
	waiting.answer(3)
end 'answerNow'

type Gate
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask(flag bool) returns Tally throws ReplyStopped
		let pending = reply.later()

		if flag 'maybe'
			answerNow(pending)
		end 'maybe'

		pending.answer(2)
		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:21:3: use of moved value 'pending': its ownership moved to the parameter 'waiting' of 'answerNow', which keeps it
```

<!-- test: error.a-reply-cannot-be-bound-as-isolated -->
A `Reply` is unique and stays in the code that holds it, so it is never an `isolated` value a function could borrow.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

function answerNow(waiting TallyReply)
	waiting.answer(3)
end 'answerNow'

type Gate
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		let held = pending as isolated TallyReply
		answerNow(held)
		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:16:14: 'pending as isolated TallyReply' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: error.a-reply-sent-to-a-service-is-moved-whether-or-not-the-send-lands -->
A send takes the `Reply` with it even when the send is refused, so the sender cannot answer it afterwards.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

type Sink
	static function create() returns Self
		return Self{}
	end 'create'

	export function take(parked TallyReply)
		parked.answer(1)
	end 'take'
end 'Sink'

type Gate
	let sink as Sink.handle

	static function create(sink Sink.handle) returns Self
		return Self{sink: sink}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		try self.sink.take(pending, sendTimeout: 0) otherwise ignore
		pending.answer(4)
		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let sink = spawn Sink.create()
	let gate = spawn Gate.create(sink)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:25:3: use of moved value 'pending': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.a-reply-answered-in-a-branch-and-after-it-is-refused-without-the-word-reply -->
No declaration in the program names `Reply`, yet `reply.later()` makes a unique value, so the answer on the branch that ran moves it and the answer after the branch reads a moved value.
```maxon
typealias Tally = int(0 to u64.max)

type Twice
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask(flag bool) returns Tally throws ReplyStopped
		let pending = reply.later()

		if flag 'early'
			pending.answer(1)
		end 'early'

		pending.answer(2)
		return
	end 'ask'
end 'Twice'

function main() returns ExitCode
	let twice = spawn Twice.create()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:16:3: use of moved value 'pending': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.a-reply-bound-to-a-second-name-is-refused-without-the-word-reply -->
`let other = pending` moves the unique `Reply`, so answering `pending` afterwards reads a moved value although no declaration in the program names `Reply`.
```maxon
typealias Tally = int(0 to u64.max)

type Twice
	static function create() returns Self
		return Self{}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		let other = pending
		pending.answer(1)
		other.answer(2)
		return
	end 'ask'
end 'Twice'

function main() returns ExitCode
	let twice = spawn Twice.create()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:12:3: use of moved value 'pending': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.an-outer-union-holding-a-reply-matched-in-a-modify-block-cannot-store-the-reply-into-the-open-region -->
`pe` is declared outside `modify bx`, so the `Reply` its `match` binds is read-only there, and building `Pe.waiting(q)` into the open `Hold` would store it.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyReply = Reply with Tally

union Pe
	none
	waiting(r TallyReply)
end 'Pe'

type Hold
	export var p as Pe

	static function create() returns Self
		return Self{p: Pe.none}
	end 'create'
end 'Hold'

type Gate
	var served as Tally

	static function create() returns Self
		return Self{served: 0}
	end 'create'

	export function ask() returns Tally throws ReplyStopped
		let pending = reply.later()
		let pe = Pe.waiting(pending)
		let bx = Hold.create() as isolated Hold

		modify bx 'w'
			match pe 'm'
				none then print("none\n")
				waiting(q) then bx.p = Pe.waiting(q)
			end 'm'
		end 'w'

		return
	end 'ask'
end 'Gate'

function main() returns ExitCode
	let gate = spawn Gate.create()
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:33:39: cannot store a read-only 'TallyReply' into 'Pe.waiting.r' while 'w' is open; store a copy
```
