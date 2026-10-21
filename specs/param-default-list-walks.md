---
feature: param-default-list-walks
status: selfhosted
keywords: [default, default-values, parameters, ownership, generics, token-scan]
category: core
---

# A Parameter Default Does Not Truncate the Parameter List

## Documentation

the compiler answers several whole-program questions about a function from its TOKENS, before any file is
parsed — which parameters a body moves into durable storage, which type parameter each parameter
feeds, which are bare inner aliases. Each of those is a peek-only walk of the parameter list, and each
advances from one parameter to the next by finding the token that ENDS the current one.

A `= <default>` sits between a parameter's type and that end. A walk that steps by the end of the
TYPE therefore stops at the first defaulted parameter and reports a parameter list with every later
parameter missing — silently, because a short list is a well-formed list. The facts those scans exist
to publish then go unrecorded for the dropped parameters, and every one of them is an OWNERSHIP fact.

These cases pin that the scans see the whole list. Each pairs a program that declares a default
BEFORE the parameter the fact belongs to with the identical program that does not, so the two must
agree; the second is the control, and it passed throughout.

## Tests

<!-- test: generic-feed-after-defaulted-param -->
A defaulted parameter ahead of a type-parameter-typed one. `item` feeds `T`, so a concrete
instantiation over `String` must CONSUME the argument — the box takes ownership of the heap record.
Unrecorded, the caller keeps ownership too, drops the String at scope exit, and the next iteration
reads a freed record: a use-after-free.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var item as T

	static function create(_ Integer = 1, item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	var total = 0 as ExitCode
	var i = 0 as Integer
	while i < 50 'loop'
		let s = "hello!"
		let b = StrBox.create(2, item: s)
		print("{b.item}")
		total = 6
		i = i + 1
	end 'loop'
	return total
end 'main'
```
```exitcode
6
```
```stdout
hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!

```

<!-- test: generic-feed-without-default-control -->
The control: the identical program with the default removed. It must answer the same, which is what
makes the case above a statement about the walk rather than about generics.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var item as T

	static function create(_ Integer, item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	var total = 0 as ExitCode
	var i = 0 as Integer
	while i < 50 'loop'
		let s = "hello!"
		let b = StrBox.create(2, item: s)
		print("{b.item}")
		total = 6
		i = i + 1
	end 'loop'
	return total
end 'main'
```
```exitcode
6
```
```stdout
hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!

```

<!-- test: generic-feed-after-two-defaulted-params -->
Two defaults ahead of the fed parameter, one of them a String — so the walk has to step over a
default whose expression is not a bare literal token, and the fed parameter sits at index 2.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var item as T

	static function create(_ Integer = 1, _ String = "n", item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	var total = 0 as ExitCode
	var i = 0 as Integer
	while i < 50 'loop'
		let s = "hello!"
		let b = StrBox.create(2, item: s)
		print("{b.item}")
		total = 6
		i = i + 1
	end 'loop'
	return total
end 'main'
```
```exitcode
6
```
```stdout
hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!

```

<!-- test: error.a-discarded-parameter-names-nothing-to-read -->
Two `_` parameters ahead of a named one, both defaulted and both labelled `_` at the call. `_` is the discard and binds nothing, so no body can read one, and a position that cannot be read carries no fact the call could attribute to the wrong one of the two.
```maxon
typealias Num = int(0 to 1000)

type Bag
	export var n as Num

	static function create() returns Self
		return Self{n: 9}
	end 'create'
end 'Bag'

type Keeper
	export var bag as Bag
	export var tag as Num

	static function make(_ Num = 1, _ Bag = Bag.create(), tag Num) returns Self
		return Self{bag: _, tag: tag}
	end 'make'
end 'Keeper'

function main() returns ExitCode
	let k = Keeper.make(tag: 3)
	return (k.bag.n + k.tag) as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:17:20: Undefined variable '_'
```

<!-- test: error.a-discarded-parameter-is-not-the-last-underscore -->
Two `_` parameters, and a body that adds `_` to a named one. There is no `_` to read, so the sum is refused rather than answering with whichever `_` a name-keyed lookup lands on.
```maxon
typealias Num = int(0 to 1000)

function add(_ Num = 1, _ Num = 2, tag Num) returns Num
	return _ + tag
end 'add'

function main() returns ExitCode
	return add(tag: 3) as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:5:9: Undefined variable '_'
```

<!-- test: two-discarded-defaulted-parameters-leak-nothing -->
The program above without the read: two defaulted `_` parameters, the second a record, ahead of a named one, called with both defaults filled. Neither `_` is bound, enrolled or recorded under a shared name, so the record the default builds is dropped once and the run ends clean.
```maxon
typealias Num = int(0 to 1000)

type Bag
	export var n as Num

	static function create() returns Self
		return Self{n: 9}
	end 'create'
end 'Bag'

type Keeper
	export var bag as Bag
	export var tag as Num

	static function make(_ Num = 1, _ Bag = Bag.create(), tag Num) returns Self
		return Self{bag: Bag.create(), tag: tag}
	end 'make'
end 'Keeper'

function main() returns ExitCode
	let k = Keeper.make(tag: 3)
	return (k.bag.n + k.tag) as ExitCode
end 'main'
```
```exitcode
12
```

<!-- test: a-discard-binds-nothing-in-every-other-position -->
`_` outside a parameter list still discards: a closure parameter, a `for` variable, a tuple element, a union payload beside a named one, and the `_ =` statement.
```maxon
typealias Num = int(0 to 1000)
typealias Probe = function(Num) returns bool

union Shape
	circle(radius Num)
	rect(width Num, height Num)
end 'Shape'

function ask(f Probe, v Num) returns bool
	return f(v)
end 'ask'

function pair() returns (Num, Num)
	return (4, 5)
end 'pair'

function sides(shape Shape) returns Num
	var found = 0 as Num

	match shape 'pick'
		circle(radius) then found = radius
		rect(_, height) then found = height
	end 'pick'

	return found
end 'sides'

function bump() returns Num
	print("bump\n")
	return 2
end 'bump'

function main() returns ExitCode
	var total = 0 as Num

	if ask(function(_) gives true, v: 1) 'yes'
		total = total + 1
	end 'yes'

	for _ in 0 upto 3 'rep'
		total = total + 1
	end 'rep'

	let (first, _) = pair()
	total = total + first
	total = total + sides(Shape.rect(2, height: 7))
	total = total + sides(Shape.circle(9))
	_ = bump()

	return total as ExitCode
end 'main'
```
```exitcode
24
```
```stdout
bump

```

<!-- test: each-defaulted-discard-fills-its-own-slot -->
Two `_` parameters, the second defaulted to the caller's line. Both defaults are supplied under the one name `_`, so a reader that finds a slot by that name would put the line number into the first `_` and refuse it against `Small`. The line lands in the `SourceLineNumber` slot.
```maxon
typealias Small = int(0 to 5)
typealias Num = int(0 to 1000)

function f(_ Small = 3, _ SourceLineNumber = __line__, tag Num) returns Num
	return tag
end 'f'

function main() returns ExitCode
	return f(tag: 7) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-discard-defaulted-by-a-call-fills-its-own-slot -->
The same two slots with the wider default produced by a call rather than a caller-location constant.
```maxon
typealias Small = int(0 to 5)
typealias Big = int(0 to 1000)
typealias Num = int(0 to 1000)

function wide() returns Big
	return 500
end 'wide'

function f(_ Small = 3, _ Big = wide(), tag Num) returns Num
	return tag
end 'f'

function main() returns ExitCode
	return f(tag: 7) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-message-with-two-defaulted-discards-fills-each-slot -->
A service message declaring two `_` parameters: the request's payload slots are filled by position, not by the shared name `_`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Small = int(0 to 5)

type Meter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function bump(_ Small = 3, _ SourceLineNumber = __line__, by Integer)
		self.n = self.n + by
	end 'bump'

	export function total() returns Integer
		return self.n
	end 'total'
end 'Meter'

function main() returns ExitCode
	let h = spawn Meter.create()
	try h.bump(by: 4) otherwise panic("an open mailbox with room takes the send")
	let total = try await h.total() otherwise panic("the service is running")
	print("total={total}\n")
	h.shutdown()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
total=4
```

<!-- test: a-module-scope-factory-fills-each-defaulted-discard-into-its-own-slot -->
The same two slots reached from a module-scope initializer, which fills a factory's defaults through its own door instead of a body's: both defaults must reach their own `_`, not the first one.
```maxon
typealias Num = int(0 to 1000)

type Box
	export var n as Num

	static function make(_ Num = 1, _ SourceLineNumber = __line__, b Num, c Num) returns Self
		return Self{n: b + c}
	end 'make'
end 'Box'

var shared = Box.make(b: 3, c: 4)

function main() returns ExitCode
	return shared.n as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-spawned-call-fills-each-defaulted-discard-into-its-own-slot -->
`async f(tag: 5)` supplies both defaulted `_` arguments to the spawn.
```maxon
typealias Small = int(0 to 5)
typealias Num = int(0 to 1000)

function f(_ Small = 3, _ SourceLineNumber = __line__, tag Num) returns Num
	sleep(1)
	return tag
end 'f'

function main() returns ExitCode
	let p = async f(tag: 5)
	let r = await p
	return r as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: overloads-with-defaulted-discards-each-fill-their-own-slots -->
Two members of one name declaring the same two defaulted `_` parameters and differing in the type of the named one.
```maxon
typealias Small = int(0 to 5)
typealias Num = int(0 to 1000)

function f(_ Small = 3, _ SourceLineNumber = __line__, tag Num) returns Num
	return tag
end 'f'

function f(_ Small = 3, _ SourceLineNumber = __line__, tag String) returns Num
	return tag.count() as Num
end 'f'

function main() returns ExitCode
	return (f(tag: 7) + f(tag: "two")) as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: a-generic-instance-method-fills-each-defaulted-discard-into-its-own-slot -->
An instance method of a generic type with two defaulted `_` parameters before the one that carries the type parameter.
```maxon
typealias Small = int(0 to 5)
typealias Num = int(0 to 1000)

type Box uses T
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function put(_ Small = 3, _ SourceLineNumber = __line__, value T) returns T
		return value
	end 'put'
end 'Box'

typealias NumBox = Box with Num

function main() returns ExitCode
	let b = NumBox.create(4)
	let got = b.put(value: 6)
	return got as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: a-destructured-discard-of-a-managed-element-leaks-nothing -->
`let (label, _) = …` over a tuple whose discarded element is a record: the element is neither bound nor released by the pattern, and the tuple's owner drops it once.
```maxon
typealias Num = int(0 to 1000)

type Bag
	export var n as Num

	static function create() returns Self
		return Self{n: 9}
	end 'create'
end 'Bag'

function pair() returns (String, Bag)
	return ("kept", Bag.create())
end 'pair'

function main() returns ExitCode
	let (label, _) = pair()
	print("{label}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
kept
```

<!-- test: error.a-message-whose-handler-writes-its-parameter-after-a-discard-refuses-a-shared-value -->
The message's first parameter is a defaulted `_`, so `p` is the second parameter and the second payload slot. A handler that writes `p` may not be sent a read-only value both threads reach.
```maxon
type Payload
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Payload'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function bump(_ Integer = 1, p Payload) returns Integer
		p.n = p.n + 1
		self.seen = self.seen + 1
		return p.n
	end 'bump'
end 'Store'

let Shared = Payload.create(7)

function main() returns ExitCode
	let h = spawn Store.create()
	return (try await h.bump(p: Shared) otherwise 0) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3138: <fragment>:28:30: argument `p` of the message `Store.bump` is a read-only value shared with the service, and the handler `Store.bump` writes or keeps its parameter `p`. A record both threads reach may be written by neither. Send a `.clone()`, or make `Store.bump` only read `p`
```

<!-- test: a-message-whose-handler-only-reads-its-parameter-after-a-discard-accepts-a-shared-value -->
The same message with a handler that only reads `p`.
```maxon
type Payload
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Payload'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(_ Integer = 1, p Payload) returns Integer
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

let Shared = Payload.create(7)

function main() returns ExitCode
	let h = spawn Store.create()
	let got = try await h.keep(p: Shared) otherwise 0
	print("{got}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
7
```

<!-- test: error.a-message-with-an-interface-parameter-before-a-discard-refuses-a-shared-value -->
An interface-typed first parameter takes a witness half in the payload slot after its own, so `p` is the fourth payload slot but the third parameter.
```maxon
interface Describable
	function describe() returns Integer
end 'Describable'

type Tag implements Describable
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function describe() returns Integer
		return self.n
	end 'describe'
end 'Tag'

type Payload
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Payload'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function bump(d Describable, _ Integer = 1, p Payload) returns Integer
		p.n = p.n + d.describe()
		self.seen = self.seen + 1
		return p.n
	end 'bump'
end 'Store'

let Shared = Payload.create(7)

function main() returns ExitCode
	let h = spawn Store.create()
	return (try await h.bump(Tag.create(), p: Shared) otherwise 0) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3138: <fragment>:44:44: argument `p` of the message `Store.bump` is a read-only value shared with the service, and the handler `Store.bump` writes or keeps its parameter `p`. A record both threads reach may be written by neither. Send a `.clone()`, or make `Store.bump` only read `p`
```

<!-- test: a-promise-forwarded-by-a-method-with-discards-through-a-later-function-parameter-is-handed-on -->
An instance method with a receiver and two `_` parameters hands its promise to a function-typed parameter that comes after them. The function-typed parameter's position is read in the method's own numbering, receiver included, and the promise is taken by the function it reaches.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

typealias Sink = function(pending IntPromiseArray, p keep IntPromise)

function keep(pending IntPromiseArray, p IntPromise)
	pending.push(p)
end 'keep'

type Hub
	var relayed as Integer

	static function create() returns Self
		return Self{relayed: 0}
	end 'create'

	function relay(_ Integer = 1, _ Integer = 2, sink Sink, pending IntPromiseArray, p IntPromise)
		self.relayed = self.relayed + 1
		sink(pending, p)
	end 'relay'
end 'Hub'

function main() returns ExitCode
	var hub = Hub.create()
	var pending = IntPromiseArray.create()
	hub.relay(sink: keep, pending: pending, p: async work())

	var sum = 0 as Integer

	for q in pending 'drain'
		sum = sum + await q
	end 'drain'

	print("{sum}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: a-conformer-that-discards-a-parameter-another-conformer-stores-leaks-nothing -->
One requirement, two conformers: `Keeper` stores the record it is handed, `Dropper` declares the same parameter `_`. Called through dispatch, the record the caller built is owned by `Keeper` and released by the caller for `Dropper`, so the program ends clean.
```maxon
typealias Integer = int(i64.min to i64.max)

type Record
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Record'

interface Taker
	function take(item keep Record)
end 'Taker'

type Keeper implements Taker
	var held as Record

	static function create() returns Self
		return Self{held: Record.create(0)}
	end 'create'

	function take(item Record)
		self.held = item
	end 'take'
end 'Keeper'

type Dropper implements Taker
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	function take(_ Record)
		self.count = self.count + 1
	end 'take'
end 'Dropper'

function feed(sink Taker)
	sink.take(Record.create(1))
end 'feed'

function main() returns ExitCode
	var keeper = Keeper.create()
	var dropper = Dropper.create()
	feed(keeper)
	feed(dropper)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.two-labelled-discards-name-the-discard-and-never-a-synthesized-label -->
Labelling `_` twice, the second time out of declaration order, fills the first `_` twice. The diagnostic spells the parameter `_`; the second `_`, which the call left to its default, never appears in it under a name of its own.
```maxon
typealias Num = int(0 to 1000)

function f(_ Num = 1, _ Num = 2, tag Num) returns Num
	return tag
end 'f'

function main() returns ExitCode
	return f(_: 1, tag: 3, _: 2) as ExitCode
end 'main'
```
```maxoncstderr
error E3038: <fragment>:9:25: duplicate argument for parameter '_' of 'f'
```

<!-- test: a-module-scope-overloaded-factory-runs-the-default-of-the-member-it-binds -->
Two members of one factory name with different defaults for `n`. A module-scope call fills its omitted argument at parse time from the first member, so it has to be re-pointed at the member the argument types pick, exactly as a body's call is: `two` runs, and `one` does not.
```maxon
typealias Num = int(0 to 1000)

function one() returns Num
	print("one\n")
	return 1
end 'one'

function two() returns Num
	print("two\n")
	return 2
end 'two'

type Maker
	export var n as Num

	static function make(a Num, n Num = one()) returns Self
		return Self{n: a + n}
	end 'make'

	static function make(a String, n Num = two()) returns Self
		return Self{n: n + a.count() as Num}
	end 'make'
end 'Maker'

var g = Maker.make("s")

function main() returns ExitCode
	return g.n as ExitCode
end 'main'
```
```exitcode
3
```
```stdout
two
```

<!-- test: a-module-scope-factory-keeps-the-record-its-default-builds -->
A module-scope factory call whose omitted argument is a helper returning a record, handed to a parameter the factory keeps.
```maxon
typealias Num = int(0 to 1000)

type Bag
	export var n as Num

	static function create() returns Self
		return Self{n: 9}
	end 'create'
end 'Bag'

type Box
	export var bag as Bag
	export var b as Num

	static function make(bag Bag = Bag.create(), b Num) returns Self
		return Self{bag: bag, b: b}
	end 'make'
end 'Box'

var shared = Box.make(b: 3)

function main() returns ExitCode
	return (shared.bag.n + shared.b) as ExitCode
end 'main'
```
```exitcode
12
```

<!-- test: overloaded-instance-methods-run-the-default-of-the-member-they-bind -->
Two overloaded instance methods with a defaulted `_` each. The receiver sits at position 0 of both signatures, and the default of the member each call binds is the one that runs.
```maxon
typealias Small = int(0 to 5)
typealias Num = int(0 to 1000)

function note(n Num) returns Small
	print("note {n}\n")
	return 1
end 'note'

type Gadget
	var n as Num

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function m(_ Small = note(1), tag Num) returns Num
		return self.n + tag
	end 'm'

	function m(_ Small = note(2), tag String) returns Num
		return self.n + tag.count() as Num
	end 'm'
end 'Gadget'

function main() returns ExitCode
	let g = Gadget.create()
	return (g.m(tag: 3) + g.m(tag: "s")) as ExitCode
end 'main'
```
```exitcode
4
```
```stdout
note 1
note 2
```

<!-- test: a-body-discard-statement-does-not-write-a-discarded-parameter -->
`_ = bump()` in the body of a function that declares a `_` parameter is the discard statement. It writes nothing, so the function takes its parameter by value and a `let` argument is accepted.
```maxon
typealias Num = int(0 to 1000)

function bump() returns Num
	print("bump\n")
	return 2
end 'bump'

function f(_ Num, tag Num) returns Num
	_ = bump()
	return tag
end 'f'

function main() returns ExitCode
	let x = 5
	return f(x, tag: 1) as ExitCode
end 'main'
```
```exitcode
1
```
```stdout
bump
```

<!-- test: a-match-payload-discard-leaves-a-discarded-parameter-alone -->
A payload `_` in a body that also declares a `_` parameter rebinds nothing.
```maxon
typealias Num = int(0 to 1000)

union Shape
	circle(radius Num)
	rect(width Num, height Num)
end 'Shape'

function g(_ Num, shape Shape) returns Num
	var found = 0 as Num

	match shape 'pick'
		circle(radius) then found = radius
		rect(_, height) then found = height
	end 'pick'

	return found
end 'g'

function main() returns ExitCode
	let x = 5
	return g(x, shape: Shape.rect(2, height: 7)) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: error.a-destructured-discard-names-nothing-to-read -->
`let (first, _) = …` takes the first element and throws the second away; `_` is not bound, so it cannot be read afterwards.
```maxon
typealias Num = int(0 to 1000)

function pair() returns (Num, Num)
	return (4, 5)
end 'pair'

function main() returns ExitCode
	let (first, _) = pair()
	return (first + _) as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:10:18: Undefined variable '_'
```

<!-- test: error.a-declaring-assignment-discard-names-nothing-to-read -->
`(let _, let second) = …` discards its first element exactly as `(_, let second) = …` does, so `_` is not bound there either.
```maxon
typealias Num = int(0 to 1000)

function pair() returns (Num, Num)
	return (4, 5)
end 'pair'

function main() returns ExitCode
	(let _, let second) = pair()
	return (second + _) as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:10:19: Undefined variable '_'
```

<!-- test: default-holding-commas-does-not-split-the-list -->
A default whose expression carries commas of its own — a constructor call — must not be read as
further parameters. `flag` is the third parameter and is reached only if the walk steps over the
whole `Point.create(1, y: 2)` rather than stopping at its first comma.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function pick(base Integer, origin Point = Point.create(1, y: 2), flag bool = true) returns Integer
	if flag 'yes'
		return base + origin.x + origin.y
	end 'yes'
	return base
end 'pick'

function main() returns ExitCode
	return pick(39)
end 'main'
```
```exitcode
42
```
