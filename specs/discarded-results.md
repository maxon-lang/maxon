---
feature: discarded-results
status: stable
keywords: [functions, purity, discard, unused, results]
category: diagnostics
---

# Discarded Function Results

## Documentation

Maxon requires function return values to be used. The rules depend on whether the function is pure, impure, or chainable.

### Pure Functions

A function is **pure** if it has no side effects: it doesn't write to stdout/stderr, doesn't modify global state, doesn't mutate parameters, and only calls other pure functions. Pure function results **must** be used — they cannot be discarded, even with `_ =`.

```text
function double(x int(i64.min to i64.max)) returns int(i64.min to i64.max)
  return x * 2
end 'double'

// Error: result of pure function 'double' must be used
double(5)

// Error: result of pure function 'double' must be used
_ = double(5)

// OK: result is used
let result = double(5)
```

### Impure Functions

A function is **impure** if it has side effects (e.g., prints output, modifies global state, mutates parameters). Impure function results **must** be assigned, but can be explicitly discarded with `_ =`:

```text
// OK: result is used
let count = processAndCount(data)

// OK: explicitly discarded
_ = processAndCount(data)

// Error: result is not used
processAndCount(data)
```

### Chainable Functions (Methods Returning Own Type)

Methods that return their own type (e.g., builder pattern) are chainable — their results may be freely discarded:

```text
type Counter
  var value as int(0 to i64.max)

  function increment() returns Counter
    value = value + 1
    return self
  end 'increment'
end 'Counter'

var c = Counter{value: 0}
c.increment()  // OK: chainable, result can be discarded
```

### Discarding Tuple Elements

When destructuring a tuple, individual elements can be discarded with `_`. If the function is pure, at least one element must be assigned and used:

```text
// OK: one element used
var (result, _) = pureFunc()

// Error: all elements discarded for pure function
(_, _) = pureFunc()
```

### The `_` Discard

The variable name `_` is a special discard identifier. It does not create a binding and is not subject to unused variable checks. Only the exact name `_` is a discard — names like `_x` are regular variables subject to normal unused checks.

## Tests

<!-- test: pure-function-discarded -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function main() returns ExitCode
	double(5)
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/pure-function-discarded.maxon:10:2: result of pure function 'double' must be used
```

<!-- test: pure-function-let-discard -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function main() returns ExitCode
	_ = double(5)
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/pure-function-let-discard.maxon:10:2: result of pure function 'double' must be used
```

<!-- test: pure-method-underscore-discard -->
A pure STDLIB method is under the same rule as a pure declaration: `_ =` does not license a discard of a
result nobody reads.
```maxon
function main() returns ExitCode
	let s = "hello"
	_ = s.count()
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/pure-method-underscore-discard.maxon:4:2: result of pure function 'String.count' must be used
```

<!-- test: pure-function-used -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function main() returns ExitCode
	let result = double(5)
	return result
end 'main'
```
```exitcode
10
```

<!-- test: impure-function-discarded -->
```maxon

typealias Integer = int(i64.min to i64.max)

var counter = 0 as Integer

function incrementAndGet() returns Integer
	counter = counter + 1
	return counter
end 'incrementAndGet'

function main() returns ExitCode
	incrementAndGet()
	return 0
end 'main'
```
```maxoncstderr
error E3065: specs/discarded-results/impure-function-discarded.maxon:13:2: result of 'incrementAndGet' is not used (use '_ = expr' to discard)
```

<!-- test: impure-method-statement-discarded -->
A method that writes `self` is IMPURE, so its result may not fall off a statement: E3065, the same
cure as a free function.

A write the caller observes afterwards is an effect, so the compiler refuses under the impure code and
offers the discard: `_ = c.bump()` compiles, where the pure code E3064 would admit no `_ =` opt-out. The
subject is spelled under the name the method is keyed by (`Counter.bump`, as `String.count` is spelled above).
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump() returns Integer
		self.n = self.n + 1
		return self.n
	end 'bump'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.bump()
	return c.n as ExitCode
end 'main'
```
```maxoncstderr
error E3065: specs/discarded-results/impure-method-statement-discarded.maxon:19:4: result of 'Counter.bump' is not used (use '_ = expr' to discard)
```

⭐ **EVERY BARE-STATEMENT DOOR FILES THE SAME VERDICT, AND THIS PINS ONE THE TWO CASES ABOVE DO NOT REACH.**
Six call shapes reach the bare-statement door — a plain call, a one-hop method, a ≥2-hop field chain, a
static member's method, a qualified STATIC call, a namespace-qualified call — and each names its own position
when it files the site. E3064 never reads that position, so only an E3065 case can tell a shape that names it
correctly from one that does not. `impure-function-discarded` is the plain call,
`impure-method-statement-discarded` the one-hop method, this is the FIELD CHAIN, and
`impure-static-member-statement-discarded` below is the static call.

<!-- test: impure-field-chain-statement-discarded -->
```maxon
typealias Integer = int(i64.min to i64.max)

var counter = 0 as Integer

type Inner
	export var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump() returns Integer
		counter = counter + 1
		return counter
	end 'bump'
end 'Inner'

type Outer
	export var inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create()}
	end 'create'
end 'Outer'

function main() returns ExitCode
	var o = Outer.create()
	o.inner.bump()
	return 0
end 'main'
```
```maxoncstderr
error E3065: specs/discarded-results/impure-field-chain-statement-discarded.maxon:29:10: result of 'Inner.bump' is not used (use '_ = expr' to discard)
```

<!-- test: impure-static-member-statement-discarded -->
⭐⭐ **A STATIC CALLED ON A LINE OF ITS OWN.** `Clock.tick()` is a statement, not
`E2015: Unsupported: identifier statement` — the compiler lowers the same shape one line up in expression
position. The statement door claims a qualified static call
whatever it returns and files the site here, so the RESULT is what earns the refusal: an impure static is
E3065 with the `_ =` cure, exactly as the three shapes above are, the subject spelled under the
`Type.method` key it is stored by.
```maxon
typealias Integer = int(i64.min to i64.max)
var ticks = 0 as Integer

type Clock
	static function tick() returns Integer
		ticks = ticks + 1
		return ticks
	end 'tick'
end 'Clock'

function main() returns ExitCode
	Clock.tick()
	return ticks as ExitCode
end 'main'
```
```maxoncstderr
error E3065: specs/discarded-results/impure-static-member-statement-discarded.maxon:13:8: result of 'Clock.tick' is not used (use '_ = expr' to discard)
```

<!-- test: pure-static-member-statement-discarded -->
The PURE half of the same door, and the half that says the verdict is the discard rule's rather than the
statement door's: a static with nothing to do but compute is E3064, which admits no `_ =` opt-out at all.
Without it the door would be provable only in its impure direction.
```maxon
typealias Integer = int(i64.min to i64.max)

type Clock
	static function now() returns Integer
		return 7
	end 'now'
end 'Clock'

function main() returns ExitCode
	Clock.now()
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/pure-static-member-statement-discarded.maxon:11:8: result of pure function 'Clock.now' must be used
```

<!-- test: void-static-member-statement-ok -->
⭐ **A VOID RESULT IS NOT A DISCARD, and this is the case that keeps the statement door from refusing the
programs it exists to admit.** `recordDiscardedCallResult` drops a void result before filing anything, so a
static that returns nothing compiles clean through the same arm the two cases above are refused by. It must
RUN, not merely compile: `bump` is the only writer of `ticks`, so the exit code is what tells the two apart.
```maxon
typealias Integer = int(i64.min to i64.max)
var ticks = 0 as Integer

type Clock
	static function bump()
		ticks = ticks + 3
	end 'bump'
end 'Clock'

function main() returns ExitCode
	Clock.bump()
	return ticks as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: impure-function-let-discard -->
```maxon

typealias Integer = int(i64.min to i64.max)

var counter = 0 as Integer

function incrementAndGet() returns Integer
	counter = counter + 1
	return counter
end 'incrementAndGet'

function main() returns ExitCode
	_ = incrementAndGet()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: void-function-ok -->
```maxon

function doNothing()
end 'doNothing'

function main() returns ExitCode
	doNothing()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: chainable-method-discarded -->
```maxon

typealias Count = int(i64.min to i64.max)

type Counter
	export var value as Count

	function increment() returns Counter
		value = value + 1
		return self
	end 'increment'

	static function create(value Count) returns Self
		return Self{value: value}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create(0)
	c.increment()
	return c.value
end 'main'
```
```exitcode
1
```

<!-- test: impure-print-discarded -->
```maxon

typealias Integer = int(i64.min to i64.max)

function computeAndPrint(x Integer) returns Integer
	print("computing")
	return x * 2
end 'computeAndPrint'

function main() returns ExitCode
	_ = computeAndPrint(5)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
computing
```

<!-- test: impure-mutating-param -->
```maxon

typealias Integer = int(i64.min to i64.max)

function doubleInPlace(x Integer) returns Integer
	x = x * 2
	return x
end 'doubleInPlace'

function main() returns ExitCode
	var n = 5 as Integer
	_ = doubleInPlace(n)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: underscore-not-prefix-suppression -->
An unused BINDING is E3012 and has nothing to do with a discarded RESULT: the name is what went unread, not a
call's answer, and a `_` prefix does not suppress it.
```maxon

function main() returns ExitCode
	let x = 42
	return 0
end 'main'
```
```maxoncstderr
error E3012: specs/discarded-results/underscore-not-prefix-suppression.maxon:4:6: unused variable: 'x'
```

<!-- test: underscore-exact-discard -->
```maxon

function main() returns ExitCode
	_ = 42
	return 0
end 'main'
```
```maxoncstderr
error E3067: specs/discarded-results/underscore-exact-discard.maxon:4:2: expected a function call
```

<!-- test: tuple-partial-discard -->
```maxon

typealias Small = int(0 to 100)

function makePair() returns (Small, Small)
	return (10, 20)
end 'makePair'

function main() returns ExitCode
	let (a, _) = makePair()
	return a
end 'main'
```
```exitcode
10
```

<!-- test: tuple-all-discard-pure -->
```maxon

typealias Small = int(0 to 100)

function makePair() returns (Small, Small)
	return (10, 20)
end 'makePair'

function main() returns ExitCode
	(_, _) = makePair()
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/tuple-all-discard-pure.maxon:10:2: result of pure function 'makePair' must be used
```

<!-- test: math-intrinsic-discarded -->
⭐ **A MATH INTRINSIC DISCARDED IN STATEMENT POSITION IS E3064, AND IT NEEDS NO PURITY ANALYSIS TO SAY SO.**
`round`, `floor`, `ceil`, `sqrt`, `abs`, `trunc`, `min` and `max` are compiler-owned: each IS a machine
instruction over its arguments, reading no memory and writing none. A statement that takes none of the
answer therefore has no other reason to run, and the parser can prove it at the call — where the
whole-program effect summary the DECLARED-callee doors wait on cannot even be asked, since an intrinsic
emits no call and has no entry in the module's function index.
```maxon
function main() returns ExitCode
	round(1.5)
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/math-intrinsic-discarded.maxon:3:2: result of pure function 'round' must be used
```

<!-- test: transitive-impure -->
```maxon

typealias Integer = int(i64.min to i64.max)

function printValue(x Integer)
	print("{x}")
end 'printValue'

function computeAndPrint(x Integer) returns Integer
	printValue(x)
	return x * 2
end 'computeAndPrint'

function main() returns ExitCode
	computeAndPrint(5)
	return 0
end 'main'
```
```maxoncstderr
error E3065: specs/discarded-results/transitive-impure.maxon:15:2: result of 'computeAndPrint' is not used (use '_ = expr' to discard)
```

<!-- test: try-pure-let-discard -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum ParseError implements Error
	invalidFormat
end 'ParseError'

function parseNum(s String) returns Integer throws ParseError
	if s.byteLength() == 0 'empty'
		throw ParseError.invalidFormat
	end 'empty'
	return s.byteLength()
end 'parseNum'

function main() returns ExitCode
	_ = try parseNum("abc") otherwise 0
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/try-pure-let-discard.maxon:17:2: result of pure function 'parseNum' must be used
```

<!-- test: try-impure-let-discard -->
```maxon

typealias Integer = int(i64.min to i64.max)

var counter = 0 as Integer

enum ParseError implements Error
	invalidFormat
end 'ParseError'

function parseNum(s String) returns Integer throws ParseError
	counter = counter + (s.byteLength() as Integer)
	throw ParseError.invalidFormat
end 'parseNum'

function main() returns ExitCode
	_ = try parseNum("abc") otherwise 0
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: try-statement-impure-ok -->
```maxon

typealias Integer = int(i64.min to i64.max)

var counter = 0 as Integer

enum MyError implements Error
	failed
end 'MyError'

function doWork() returns Integer throws MyError
	counter = counter + 1
	throw MyError.failed
end 'doWork'

function main() returns ExitCode
	try doWork() otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: param-mutating-method-is-impure -->
A function that mutates a parameter through a mutating method (`arr.remove(i)`)
is IMPURE — even though it neither writes a global nor calls a known impure
builtin directly. Its `bool` result is therefore `_=`-discardable (E3065-style),
not must-use (E3064): the purity pass taints param-derived receivers and treats
a mutating method (`push`/`pop`/`insert`/`remove`/`set`/`add`/…) on one as a
side effect. The first `_ = removeFirst(...)` discard must compile; the function
removes `2` from `[2, 5]`, leaving one element. Returns `1`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function removeFirst(arr IntArray, value Integer) returns bool
	var i = 0
	while i < arr.count() 'scan'
		let cur = try arr.get(i) otherwise panic("oob")
		if cur == value 'hit'
			_ = try arr.remove(i) otherwise panic("remove failed")
			return true
		end 'hit'
		i = i + 1
	end 'scan'
	return false
end 'removeFirst'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(2)
	a.push(5)
	_ = removeFirst(a, value: 2)
	return a.count()
end 'main'
```
```exitcode
1
```

### Container reads the parser lowers straight to a runtime entry

`Array.first`/`get`/`count` are not corpus bodies in the compiler — the parser lowers each straight to a runtime
symbol — so the discarded-result site names `__managed_first` where the author wrote `items.first()`. The
rule is the same one door over and the SUBJECT is the member, never the symbol.

<!-- test: pure-array-read-underscore-discard -->
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function main() returns ExitCode
	var arr = TallyArray.create()
	arr.push(1)
	_ = try arr.first() otherwise 0
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/pure-array-read-underscore-discard.maxon:8:2: result of pure function 'Array.first' must be used
```

A read that MOVES the element out is not one of them: `pop` vacates the slot, so the call changes the
container and `_ =` is the explicit discard the language asks for.

<!-- test: move-out-read-underscore-discard -->
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function main() returns ExitCode
	var arr = TallyArray.create()
	arr.push(1)
	_ = try arr.pop() otherwise 0
	return arr.count()
end 'main'
```
```exitcode
0
```

### A generic container's read is pure through its constraint

`Map.get`, `Map.contains` and `Set.contains` each call `key.hash()` and `existing == key` on a constrained
type parameter. Those bind to a REQUIREMENT rather than to a callee, and the effect summary judges them by
the members that can fill the slot — so a probe that only reads the table is pure, and `_ =` does not
license dropping its answer.

<!-- test: map-get-underscore-discard -->
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyMap = Map with (String, Tally)

function main() returns ExitCode
	var m = TallyMap.create()
	m.upsert("a", value: 1)
	_ = try m.get("a") otherwise 0
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/map-get-underscore-discard.maxon:8:2: result of pure function 'Map.get' must be used
```

<!-- test: set-contains-underscore-discard -->
```maxon
typealias Tally = int(0 to u64.max)
typealias TallySet = Set with Tally

function main() returns ExitCode
	var s = TallySet.create()
	s.insert(1)
	_ = s.contains(1)
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/set-contains-underscore-discard.maxon:8:2: result of pure function 'Set.contains' must be used
```

⚠ The compiler reports the member's registration name, which for an un-overloaded member is the bare
`Set.contains` — never an overload key such as `Set.contains$element`.

The control is the read that CHANGES the table: `remove` tombstones a slot, so the call has a reason to run
and its `bool` answer may be discarded.

<!-- test: map-remove-underscore-discard -->
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyMap = Map with (String, Tally)

function main() returns ExitCode
	var m = TallyMap.create()
	m.upsert("a", value: 1)
	_ = m.remove("a")
	return m.count()
end 'main'
```
```exitcode
0
```

### A bare method-call statement is the same door

A method written on a line of its own takes none of what the call produced, exactly as a bare `f()` does —
so a pure callee reached that way is refused there too. The diagnostic anchors on the METHOD NAME rather
than on the receiver (at `arr.count()`, `b.ops.count()` and `utils.twice(4)` alike).

<!-- test: method-call-statement-discarded -->
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function main() returns ExitCode
	var arr = TallyArray.create()
	arr.push(1)
	arr.count()
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/method-call-statement-discarded.maxon:8:6: result of pure function 'Array.count' must be used
```

The chainable rule holds at this door too: a builder step written for its receiver is legal on a line of
its own, however pure its body is.

<!-- test: chainable-method-statement-ok -->
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function main() returns ExitCode
	var arr = TallyArray.create()
	arr.push(7)
	arr.clone()
	return arr.count() - 1
end 'main'
```
```exitcode
0
```

⭐ **AND AN ARGUMENT THAT FORKS IS NOT THE STATEMENT'S OWN PRODUCER.** `push` is void, so the statement
discards nothing — but the `try` inside its argument leaves the call on a merge block, and the probe that
names a discarded producer must not reach past `push` to it. Every `x.push(try y.get(i) otherwise …)` in
`stdlib/` and `maxon-bin/` is this shape.

<!-- test: void-method-statement-with-forking-argument -->
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function main() returns ExitCode
	var src = TallyArray.create()
	src.push(3)
	var dst = TallyArray.create()
	dst.push(try src.get(0) otherwise panic("src holds one element"))
	return dst.count() - 1
end 'main'
```
```exitcode
0
```

### A chainable method's result may be dropped

`Array.clone` takes the receiver and returns the receiver's own type, so it is a builder step: its result is
droppable however pure the body is. Every callee `clone` reaches is on the effect-free roster, so the
summary calls it pure — and the chainable rule is what keeps this legal program legal.

<!-- test: chainable-clone-underscore-discard -->
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function main() returns ExitCode
	var arr = TallyArray.create()
	arr.push(7)
	_ = arr.clone()
	return arr.count() - 1
end 'main'
```
```exitcode
0
```

### A conformer reached through an overload set is still a candidate

An interface requirement is implemented by whichever member matches it, and that member need not be the
FIRST declaration of its name: a second `digest` registers as `Loud.digest#`, a name no requirement is
spelled with. The candidate scan has to undo BOTH joins, or the conformer drops out of the set and a
dispatch that can land on its module-global write reads pure.

The control is the identical program with the extra overload deleted -- it compiles either way, so the
refusal this case forbids would turn on nothing but an unrelated declaration.

<!-- test: overloaded-conformer-is-a-candidate -->
```maxon
typealias Code = int(0 to u32.max)

var noise = 0 as Code

interface Digest
	function digest() returns Code
end 'Digest'

type Quiet implements Digest
	export var x as Code

	static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	function digest() returns Code
		return self.x
	end 'digest'
end 'Quiet'

type Loud implements Digest
	export var x as Code

	static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	function digest(salt Code) returns Code
		return self.x + salt
	end 'digest'

	function digest() returns Code
		noise = noise + 1
		return self.x
	end 'digest'
end 'Loud'

type Box uses T where T is Digest
	export var item as T

	static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	function itemDigest() returns Code
		return self.item.digest()
	end 'itemDigest'
end 'Box'

typealias LoudBox = Box with Loud
typealias QuietBox = Box with Quiet

function main() returns ExitCode
	let loud = LoudBox.create(Loud.create(3))
	_ = loud.itemDigest()
	let quiet = QuietBox.create(Quiet.create(3))
	return quiet.itemDigest() + noise - 4
end 'main'
```
```exitcode
0
```

⭐⭐ **`_ = <rhs>` OPTS OUT OF A VERDICT ON AN EFFECT, SO THERE MUST BE ONE — E3067, "expected a function
call".** The three cases below each compute a value nothing was ever going to complain about: an intrinsic
folds to an instruction, a field read loads a word, and `a + b` adds two. None can be the subject of E3064
or E3065, so `_` claims an exemption from a rule the statement is not under and the whole line does nothing.
All three are E3067 at the `_`, same message, same column.

⚠ The refusal reads the ops the statement EMITTED, not its tokens, which is why `_ = round(1.5)` is E3067
and not E3064 — an intrinsic emits no call, so there is no callee for a "result must be used" sentence to
name (contrast `math-intrinsic-discarded` above, where the same intrinsic in STATEMENT position is E3064).

<!-- test: error.underscore-discard-of-an-intrinsic -->
```maxon
function main() returns ExitCode
	_ = round(1.5)
	return 0
end 'main'
```
```maxoncstderr
error E3067: specs/discarded-results/error.underscore-discard-of-an-intrinsic.maxon:3:2: expected a function call
```

<!-- test: error.underscore-discard-of-a-field-read -->
```maxon
typealias Tally = int(0 to u64.max)

type Counter
	export var count as Tally

	static function create(count Tally) returns Self
		return Self{count: count}
	end 'create'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(3)
	_ = c.count
	return 0
end 'main'
```
```maxoncstderr
error E3067: specs/discarded-results/error.underscore-discard-of-a-field-read.maxon:14:2: expected a function call
```

<!-- test: error.underscore-discard-of-an-arithmetic-expression -->
```maxon
typealias Tally = int(0 to u64.max)

function main() returns ExitCode
	let a = 3 as Tally
	let b = 4 as Tally
	_ = a + b
	return 0
end 'main'
```
```maxoncstderr
error E3067: specs/discarded-results/error.underscore-discard-of-an-arithmetic-expression.maxon:7:2: expected a function call
```

⭐⭐ **THE ONE CALL-FREE DISCARD THAT STAYS LEGAL IS A BARE BINDING NAME, BECAUSE E3012 LEAVES NOWHERE ELSE
TO STAND.** `_ = seen` is the only spelling that names a binding without using it, and E3012 requires every
binding to be named — delete the acknowledgement and `unused variable` is what you get.
A spawned promise deliberately never awaited is the shape that needs it: the binding must live
to scope exit, because that is where the drop happens.

The line is drawn at ONE name token: every longer call-free right-hand side is
refused above, because `_ = c.count` already names `c` and so acknowledges nothing a bare `c` would not.

<!-- test: underscore-discard-of-a-bare-binding-is-the-e3012-acknowledgement -->
```maxon
typealias Tally = int(0 to u64.max)

function main() returns ExitCode
	let seen = 3 as Tally
	_ = seen
	return 0
end 'main'
```
```exitcode
0
```

⭐⭐ **THE CONTROL THAT PINS THE PROBE'S DIRECTION: A CALL THE `try` FORK LAUNDERED IS STILL A CALL.**
`try f() otherwise panic(…)` hands its value back from a block the failing arm never rejoins, so the
"which callee produced this value" probe E3064 files its site through cannot follow it. That probe answering
"none" is silence for E3064 and would be a REFUSAL here — `maxon-bin/` and `stdlib/` both spell this
shape. The fallback has to DIVERGE
for the launder: `otherwise 0` merges, and the probe's backward walk does follow that one, which is why
`try-pure-let-discard` above still answers E3064. So the discard asks the weaker question whose misses are
safe: did the statement call anything AT ALL.

<!-- test: try-otherwise-panic-underscore-discard -->
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function main() returns ExitCode
	var stack = TallyArray.create()
	stack.push(7)
	_ = try stack.pop() otherwise panic("pushed one element")
	return 0
end 'main'
```
```exitcode
0
```

⭐ **AND THE SAME WEAKER QUESTION MAKES A CALL FEEDING AN OPERATOR A DISCARD OF SOMETHING.** `_ = bump() + 1`
computes a sum nobody reads, but a call DID happen, so the statement is not the empty gesture E3067 refuses.
A rule that asked only whether the LAST op is a call would refuse it with E3067 — this case is what pins
the widening, which the laundered `try` statements above are the reason for.

<!-- test: call-feeding-an-operator-underscore-discard -->
```maxon
typealias Integer = int(i64.min to i64.max)

var hits = 0 as Integer

function bump() returns Integer
	hits = hits + 1
	return hits
end 'bump'

function main() returns ExitCode
	_ = bump() + 1
	return hits - 1
end 'main'
```
```exitcode
0
```

<!-- test: error.pure-throwing-call-statement-in-a-test-body -->
A bare call to a pure throwing function, written as a statement in a `test` body, takes the test's implied
handler and still discards the value it produced. The discard is refused exactly as it is outside a test.
```maxon
// --- file: suite.maxtest
typealias Integer = int(i64.min to i64.max)

enum ParseError implements Error
	invalidFormat
end 'ParseError'

function parseNum(s String) returns Integer throws ParseError
	if s.byteLength() == 0 'empty'
		throw ParseError.invalidFormat
	end 'empty'
	return s.byteLength()
end 'parseNum'

test 'discards a pure result'
	parseNum("abc")
end 'discards a pure result'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.pure-throwing-call-statement-in-a-test-body.maxon:17:2: result of pure function 'parseNum' must be used
```

<!-- test: try-pure-propagating-discard -->
The propagating twin of `try-pure-let-discard`: `_ = try parseNum("abc")` with no `otherwise`, inside a
function that throws the same error, still discards a pure result.
```maxon

typealias Integer = int(i64.min to i64.max)

enum ParseError implements Error
	invalidFormat
end 'ParseError'

function parseNum(s String) returns Integer throws ParseError
	if s.byteLength() == 0 'empty'
		throw ParseError.invalidFormat
	end 'empty'
	return s.byteLength()
end 'parseNum'

function check() returns Integer throws ParseError
	_ = try parseNum("abc")
	return 0
end 'check'

function main() returns ExitCode
	let n = try check() otherwise 1
	return n as ExitCode
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/try-pure-propagating-discard.maxon:17:2: result of pure function 'parseNum' must be used
```

<!-- test: try-block-pure-discard -->
The try-block twin: a pure throwing call routed to a block-form `try`'s handler and discarded with `_ =`.
```maxon

typealias Integer = int(i64.min to i64.max)

enum ParseError implements Error
	invalidFormat
end 'ParseError'

function parseNum(s String) returns Integer throws ParseError
	if s.byteLength() == 0 'empty'
		throw ParseError.invalidFormat
	end 'empty'
	return s.byteLength()
end 'parseNum'

function main() returns ExitCode
	try 'work'
		_ = parseNum("abc")
	end 'work' otherwise (e) 'h'
		match e 'why'
			invalidFormat then print("failed\n")
		end 'why'
	end 'h'

	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/try-block-pure-discard.maxon:18:3: result of pure function 'parseNum' must be used
```

<!-- test: error.try-otherwise-return-underscore-discard-of-a-pure-function -->
A pure throwing function whose result is discarded is refused in every form the discard can take. A
fallback that leaves the function (`otherwise return`, `otherwise panic(…)`) hands the value back from a
block the failing arm never rejoins, but the call was still made for nothing but its result.
```maxon
typealias Integer = int(i64.min to i64.max)

enum LookupError implements Error
	notFound
end 'LookupError'

function find(key Integer) returns Integer throws LookupError
	if key < 0 'missing'
		throw LookupError.notFound
	end 'missing'
	return key + 1
end 'find'

function check(key Integer) returns bool
	_ = try find(key) otherwise return false
	return true
end 'check'

function main() returns ExitCode
	if check(3) 'found'
		return 0
	end 'found'
	return 1
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.try-otherwise-return-underscore-discard-of-a-pure-function.maxon:16:2: result of pure function 'find' must be used
```

<!-- test: error.try-otherwise-return-underscore-discard-of-a-pure-method -->
A pure throwing function whose result is discarded is refused in every form the discard can take. A
fallback that leaves the function (`otherwise return`, `otherwise panic(…)`) hands the value back from a
block the failing arm never rejoins, but the call was still made for nothing but its result.
```maxon
typealias Integer = int(i64.min to i64.max)

enum LookupError implements Error
	notFound
end 'LookupError'

type Registry
	var base as Integer

	static function create(base Integer) returns Registry
		return Registry{base: base}
	end 'create'

	function lookup(key Integer) returns Integer throws LookupError
		if key < 0 'missing'
			throw LookupError.notFound
		end 'missing'
		return key + self.base
	end 'lookup'
end 'Registry'

type Holder
	var registry as Registry

	static function create(registry Registry) returns Holder
		return Holder{registry: registry}
	end 'create'

	function has(key Integer) returns bool
		_ = try self.registry.lookup(key) otherwise return false
		return true
	end 'has'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create(Registry.create(1))
	if holder.has(3) 'found'
		return 0
	end 'found'
	return 1
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.try-otherwise-return-underscore-discard-of-a-pure-method.maxon:31:3: result of pure function 'Registry.lookup' must be used
```

<!-- test: error.try-otherwise-panic-underscore-discard-of-a-pure-function -->
A pure throwing function whose result is discarded is refused in every form the discard can take. A
fallback that leaves the function (`otherwise return`, `otherwise panic(…)`) hands the value back from a
block the failing arm never rejoins, but the call was still made for nothing but its result.
```maxon
typealias Integer = int(i64.min to i64.max)

enum LookupError implements Error
	notFound
end 'LookupError'

function find(key Integer) returns Integer throws LookupError
	if key < 0 'missing'
		throw LookupError.notFound
	end 'missing'
	return key + 1
end 'find'

function main() returns ExitCode
	_ = try find(3) otherwise panic("find only fails below zero")
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.try-otherwise-panic-underscore-discard-of-a-pure-function.maxon:16:2: result of pure function 'find' must be used
```

<!-- test: error.pure-map-lookup-discarded-while-another-key-type-has-an-effectful-hash -->
A pure lookup in a `Map` is judged by the key type it is instantiated with. Another `Map` in the same
program keyed by a type whose `hash` has an effect must not make this one look impure.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Count = int(0 to 1000)
typealias Widths = Map with (String, Count)

var hashCalls = 0 as Integer

type Loud implements Hashable, Equatable
	export let id as Integer

	static function create(id Integer) returns Loud
		return Loud{id: id}
	end 'create'

	function hash() returns HashValue
		hashCalls = hashCalls + 1
		return (id and 0xFFFF) as HashValue
	end 'hash'

	function equals(other Loud) returns bool
		return id == other.id
	end 'equals'
end 'Loud'

typealias LoudMap = Map with (Loud, Integer)

type Registry
	var widths as Widths

	static function create() returns Registry
		return Registry{widths: Widths.create()}
	end 'create'

	function widthOf(name String) returns Count throws MapError
		return try self.widths.get(name)
	end 'widthOf'

	function declares(name String) returns bool
		_ = try self.widthOf(name) otherwise return false
		return true
	end 'declares'
end 'Registry'

function main() returns ExitCode
	var loud = LoudMap.create()
	try loud.insert(Loud.create(1), value: 2) otherwise return 2
	let registry = Registry.create()

	if registry.declares("a") 'found'
		return 1
	end 'found'

	return hashCalls as ExitCode
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.pure-map-lookup-discarded-while-another-key-type-has-an-effectful-hash.maxon:39:3: result of pure function 'Registry.widthOf' must be used
```

<!-- test: error.filepath-keyed-map-lookup-discarded-is-refused-on-every-target -->
A lookup in a `Map` keyed by `FilePath` is pure on every target. On Windows a `FilePath` compares in its lower-cased
spelling, which `toLower` builds in a string the call created itself, so the verdict must not turn on the host.
```maxon
typealias Count = int(0 to 1000)
typealias Sizes = Map with (FilePath, Count)

type Catalog
	var sizes as Sizes

	static function create() returns Catalog
		return Catalog{sizes: Sizes.create()}
	end 'create'

	function sizeOf(path FilePath) returns Count throws MapError
		return try self.sizes.get(path)
	end 'sizeOf'

	function knows(path FilePath) returns bool
		_ = try self.sizeOf(path) otherwise return false
		return true
	end 'knows'
end 'Catalog'

function main() returns ExitCode
	let catalog = Catalog.create()

	if catalog.knows(FilePath from "a.txt") 'found'
		return 1
	end 'found'

	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.filepath-keyed-map-lookup-discarded-is-refused-on-every-target.maxon:17:3: result of pure function 'Catalog.sizeOf' must be used
```

<!-- test: error.lowering-a-string-is-pure -->
`toLower` writes only into the string it builds, so discarding its result is refused whatever the host.
```maxon
function lowered(text String) returns String
	return text.toLower()
end 'lowered'

function main() returns ExitCode
	_ = lowered("ABC")
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.lowering-a-string-is-pure.maxon:7:2: result of pure function 'lowered' must be used
```

<!-- test: error.a-builder-writing-only-its-own-array-is-pure -->
A function that fills an array it created and returns it has no effect beyond its result.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function squares(count Integer) returns IntArray
	var out = IntArray.create()

	for i in 0 upto count 'each'
		out.push(i * i)
	end 'each'

	return out
end 'squares'

function main() returns ExitCode
	_ = squares(3)
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-builder-writing-only-its-own-array-is-pure.maxon:16:2: result of pure function 'squares' must be used
```

<!-- test: error.a-method-writing-its-receiver-is-pure-when-the-receiver-is-the-callers-own -->
A method that writes its receiver is an effect to a caller that was handed the receiver, and none to a caller that created it.
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	var total as Integer

	static function create() returns Counter
		return Counter{total: 0}
	end 'create'

	function bump() returns Integer
		self.total = self.total + 1
		return self.total
	end 'bump'
end 'Counter'

function once() returns Integer
	let counter = Counter.create()
	return counter.bump()
end 'once'

function main() returns ExitCode
	_ = once()
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-method-writing-its-receiver-is-pure-when-the-receiver-is-the-callers-own.maxon:23:2: result of pure function 'once' must be used
```

<!-- test: a-function-writing-an-array-it-was-handed-keeps-its-result-droppable -->
Writing an array that arrived as an argument is an effect, so `_ =` is the legal way to drop the count.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function fill(target IntArray, count Integer) returns Integer
	for i in 0 upto count 'each'
		target.push(i)
	end 'each'

	return count
end 'fill'

function main() returns ExitCode
	var values = IntArray.create()
	_ = fill(values, count: 3)

	if values.count() != 3 'wrong'
		return 1
	end 'wrong'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.a-function-writing-an-array-it-was-handed-is-impure -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function fill(target IntArray, count Integer) returns Integer
	for i in 0 upto count 'each'
		target.push(i)
	end 'each'

	return count
end 'fill'

function main() returns ExitCode
	var values = IntArray.create()
	fill(values, count: 3)
	return 0
end 'main'
```
```maxoncstderr
error E3065: specs/discarded-results/error.a-function-writing-an-array-it-was-handed-is-impure.maxon:15:2: result of 'fill' is not used (use '_ = expr' to discard)
```

<!-- test: a-function-that-stores-a-fresh-array-into-its-argument-then-writes-it-is-impure -->
An array stored into a record the caller holds has left the function, so the later write is visible.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Sink
	var items as IntArray

	static function create() returns Sink
		return Sink{items: IntArray.create()}
	end 'create'

	function attach(extra IntArray)
		self.items = extra
	end 'attach'

	function size() returns Integer
		return self.items.count() as Integer
	end 'size'
end 'Sink'

function stash(sink Sink, count Integer) returns Integer
	var extra = IntArray.create()
	sink.attach(extra)
	extra.push(count)
	return count
end 'stash'

function main() returns ExitCode
	let sink = Sink.create()
	_ = stash(sink, count: 4)

	if sink.size() != 1 'wrong'
		return 1
	end 'wrong'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.a-search-over-a-pure-closure-literal-is-pure -->
A call through a closure whose identity is known where it is written is judged by the closure's own body.
```maxon
typealias Position = int(0 to 1000)
typealias PositionTest = function(Position) returns bool

enum SearchError implements Error
	absent
end 'SearchError'

function firstWhere(limit Position, test PositionTest) returns Position throws SearchError
	for i in 0 upto limit 'each'
		if test(i) 'hit'
			return i
		end 'hit'
	end 'each'

	throw SearchError.absent
end 'firstWhere'

function firstOver(threshold Position) returns Position throws SearchError
	return try firstWhere(100, test: function(i) gives i > threshold)
end 'firstOver'

function exceeds(threshold Position) returns bool
	_ = try firstOver(threshold) otherwise return false
	return true
end 'exceeds'

function main() returns ExitCode
	if exceeds(5) 'found'
		return 0
	end 'found'

	return 1
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-search-over-a-pure-closure-literal-is-pure.maxon:24:2: result of pure function 'firstOver' must be used
```

<!-- test: error.a-search-over-a-named-function-is-pure -->
A named function passed as the predicate is as known as a closure literal.
```maxon
typealias Position = int(0 to 1000)
typealias PositionTest = function(Position) returns bool

enum SearchError implements Error
	absent
end 'SearchError'

function firstWhere(limit Position, test PositionTest) returns Position throws SearchError
	for i in 0 upto limit 'each'
		if test(i) 'hit'
			return i
		end 'hit'
	end 'each'

	throw SearchError.absent
end 'firstWhere'

function isLarge(position Position) returns bool
	return position > 50
end 'isLarge'

function firstLarge() returns Position throws SearchError
	return try firstWhere(100, test: isLarge)
end 'firstLarge'

function hasLarge() returns bool
	_ = try firstLarge() otherwise return false
	return true
end 'hasLarge'

function main() returns ExitCode
	if hasLarge() 'found'
		return 0
	end 'found'

	return 1
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-search-over-a-named-function-is-pure.maxon:28:2: result of pure function 'firstLarge' must be used
```

<!-- test: a-search-whose-predicate-prints-keeps-its-result-droppable -->
The same search with a predicate that prints is an effect, so discarding its result is legal.
```maxon
typealias Position = int(0 to 1000)
typealias PositionTest = function(Position) returns bool

enum SearchError implements Error
	absent
end 'SearchError'

function firstWhere(limit Position, test PositionTest) returns Position throws SearchError
	for i in 0 upto limit 'each'
		if test(i) 'hit'
			return i
		end 'hit'
	end 'each'

	throw SearchError.absent
end 'firstWhere'

function noisy(position Position) returns bool
	print("probe {position}\n")
	return position > 1
end 'noisy'

function firstNoisy() returns Position throws SearchError
	return try firstWhere(100, test: function(i) gives noisy(i))
end 'firstNoisy'

function main() returns ExitCode
	_ = try firstNoisy() otherwise return 1
	return 0
end 'main'
```
```exitcode
0
```
```stdout
probe 0
probe 1
probe 2
```

<!-- test: a-search-whose-predicate-writes-captured-state-keeps-its-result-droppable -->
A predicate that writes the receiver it captured writes state the caller can see.
```maxon
typealias Position = int(0 to 1000)
typealias PositionTest = function(Position) returns bool
typealias PositionArray = Array with Position

enum SearchError implements Error
	absent
end 'SearchError'

function firstWhere(limit Position, test PositionTest) returns Position throws SearchError
	for i in 0 upto limit 'each'
		if test(i) 'hit'
			return i
		end 'hit'
	end 'each'

	throw SearchError.absent
end 'firstWhere'

type Recorder
	var seen as PositionArray

	static function create() returns Recorder
		return Recorder{seen: PositionArray.create()}
	end 'create'

	function record(position Position) returns bool
		self.seen.push(position)
		return position > 1
	end 'record'

	function firstRecorded() returns Position throws SearchError
		return try firstWhere(100, test: function(i) gives self.record(i))
	end 'firstRecorded'

	function recorded() returns Position
		return self.seen.count() as Position
	end 'recorded'
end 'Recorder'

function main() returns ExitCode
	let recorder = Recorder.create()
	_ = try recorder.firstRecorded() otherwise return 1

	if recorder.recorded() != 3 'wrong'
		return 2
	end 'wrong'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-search-through-a-closure-of-unknown-origin-keeps-its-result-droppable -->
A closure read out of a record is not known at the call, so the search is judged by what could run.
```maxon
typealias Position = int(0 to 1000)
typealias PositionTest = function(Position) returns bool

enum SearchError implements Error
	absent
end 'SearchError'

type Probe
	var test as PositionTest

	static function create(test PositionTest) returns Probe
		return Probe{test: test}
	end 'create'

	function firstHit(limit Position) returns Position throws SearchError
		for i in 0 upto limit 'each'
			if self.test(i) 'hit'
				return i
			end 'hit'
		end 'each'

		throw SearchError.absent
	end 'firstHit'
end 'Probe'

function main() returns ExitCode
	let probe = Probe.create(function(i) gives i > 2)
	_ = try probe.firstHit(10) otherwise return 1
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.a-closure-handed-down-through-two-helpers-is-still-known -->
A closure literal passed to a helper that forwards it to the search is judged by its own body at the end of the chain.
The helper's own discard is left alone, because on its own the forwarded predicate is not known.
```maxon
typealias Position = int(0 to 1000)
typealias PositionTest = function(Position) returns bool

enum SearchError implements Error
	absent
end 'SearchError'

function firstWhere(limit Position, test PositionTest) returns Position throws SearchError
	for i in 0 upto limit 'each'
		if test(i) 'hit'
			return i
		end 'hit'
	end 'each'

	throw SearchError.absent
end 'firstWhere'

function anyWhere(limit Position, test PositionTest) returns bool
	_ = try firstWhere(limit, test: test) otherwise return false
	return true
end 'anyWhere'

function anyOver(threshold Position) returns bool
	return anyWhere(100, test: function(i) gives i > threshold)
end 'anyOver'

function main() returns ExitCode
	_ = anyOver(3)
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-closure-handed-down-through-two-helpers-is-still-known.maxon:29:2: result of pure function 'anyOver' must be used
```

<!-- test: a-registering-factory-keeps-writes-to-its-product-visible -->
A function that registers the record it creates in a module-level array and then writes it changes state the caller can see,
whatever it returns.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	var total as Integer

	static function create() returns Box
		return Box{total: 0}
	end 'create'

	function bump() returns Integer
		self.total = self.total + 1
		return self.total
	end 'bump'

	function read() returns Integer
		return self.total
	end 'read'
end 'Box'

typealias BoxArray = Array with Box

var registry = BoxArray.create()

function enroll() returns Box
	let box = Box.create()
	registry.push(box)
	return box
end 'enroll'

function tamper() returns Integer
	let box = enroll()
	return box.bump()
end 'tamper'

function main() returns ExitCode
	_ = tamper()

	if (try registry.get(0) otherwise return 2).read() != 1 'wrong'
		return 1
	end 'wrong'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.a-search-through-a-with-iterator-loop-is-pure -->
The iterator a `withIterator()` loop walks is created by the loop, so moving it is not an effect of the function.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

enum SearchError implements Error
	absent
end 'SearchError'

function positionOf(values IntArray, wanted Integer) returns Integer throws SearchError
	for (iter, value) in values.withIterator() 'each'
		if value == wanted 'hit'
			return iter.index() as Integer
		end 'hit'
	end 'each'

	throw SearchError.absent
end 'positionOf'

function contains(values IntArray, wanted Integer) returns bool
	_ = try positionOf(values, wanted: wanted) otherwise return false
	return true
end 'contains'

function main() returns ExitCode
	var values = IntArray.create()
	values.push(4)

	if contains(values, wanted: 4) 'found'
		return 0
	end 'found'

	return 1
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-search-through-a-with-iterator-loop-is-pure.maxon:20:2: result of pure function 'positionOf' must be used
```

<!-- test: a-with-iterator-loop-whose-body-writes-an-argument-keeps-its-result-droppable -->
A loop body that pushes into an array it was handed is an effect, whatever the iterator does.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function copyInto(values IntArray, target IntArray) returns Integer
	var copied = 0 as Integer

	for (iter, value) in values.withIterator() 'each'
		target.push(value + (iter.index() as Integer))
		copied = copied + 1
	end 'each'

	return copied
end 'copyInto'

function main() returns ExitCode
	var values = IntArray.create()
	values.push(4)
	values.push(5)
	var target = IntArray.create()
	_ = copyInto(values, target: target)

	if target.count() != 2 'wrong'
		return 1
	end 'wrong'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-with-iterator-loop-whose-body-prints-keeps-its-result-droppable -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function show(values IntArray) returns Integer
	var shown = 0 as Integer

	for (iter, value) in values.withIterator() 'each'
		print("{iter.index()}={value}\n")
		shown = shown + 1
	end 'each'

	return shown
end 'show'

function main() returns ExitCode
	var values = IntArray.create()
	values.push(7)
	_ = show(values)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0=7
```

<!-- test: error.a-with-iterator-search-stays-pure-beside-an-iterator-that-moves-records-between-its-fields -->
Another iterator type in the program that moves a record between its own fields does not make a search over an array effectful.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Slot
	export var number as Integer

	static function create(number Integer) returns Slot
		return Self{number: number}
	end 'create'
end 'Slot'

type SwapIter implements Iterator with Integer
	var front as Slot
	var back as Slot

	static function create() returns Self
		return Self{front: Slot.create(1), back: Slot.create(2)}
	end 'create'

	function current() returns Integer
		return self.front.number
	end 'current'

	function advance() throws IterationError
		self.front = self.back
		throw IterationError.exhausted
	end 'advance'
end 'SwapIter'

enum SearchError implements Error
	absent
end 'SearchError'

function positionOf(values IntArray, wanted Integer) returns Integer throws SearchError
	for (iter, value) in values.withIterator() 'each'
		if value == wanted 'hit'
			return iter.index() as Integer
		end 'hit'
	end 'each'

	throw SearchError.absent
end 'positionOf'

function holds(values IntArray, wanted Integer) returns bool
	_ = try positionOf(values, wanted: wanted) otherwise return false
	return true
end 'holds'

function main() returns ExitCode
	var swap = SwapIter.create()
	try swap.advance() otherwise ignore
	print("{swap.current()}\n")
	var values = IntArray.create()
	values.push(4)
	return 0 if holds(values, wanted: 4) else 1
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-with-iterator-search-stays-pure-beside-an-iterator-that-moves-records-between-its-fields.maxon:46:2: result of pure function 'positionOf' must be used
```

<!-- test: error.a-search-through-a-with-iterator-loop-over-a-list-is-pure -->
A pure search through `withIterator()` over a `List`, discarded, is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

enum SearchError implements Error
	absent
end 'SearchError'

function firstAbove(values IntList, floor Integer) returns Integer throws SearchError
	for (iter, value) in values.withIterator() 'each'
		if value > floor 'hit'
			return iter.current()
		end 'hit'
	end 'each'

	throw SearchError.absent
end 'firstAbove'

function hasAbove(values IntList, floor Integer) returns bool
	_ = try firstAbove(values, floor: floor) otherwise return false
	return true
end 'hasAbove'

function main() returns ExitCode
	var values = IntList.create()
	values.append(4)
	values.append(5)

	return 0 if hasAbove(values, floor: 3) else 1
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-search-through-a-with-iterator-loop-over-a-list-is-pure.maxon:20:2: result of pure function 'firstAbove' must be used
```

<!-- test: error.a-search-through-a-for-loop-over-a-list-is-pure -->
The same search through a plain `for ... in` loop.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

enum SearchError implements Error
	absent
end 'SearchError'

function firstAbove(values IntList, floor Integer) returns Integer throws SearchError
	for value in values 'each'
		if value > floor 'hit'
			return value
		end 'hit'
	end 'each'

	throw SearchError.absent
end 'firstAbove'

function hasAbove(values IntList, floor Integer) returns bool
	_ = try firstAbove(values, floor: floor) otherwise return false
	return true
end 'hasAbove'

function main() returns ExitCode
	var values = IntList.create()
	values.append(4)
	values.append(5)

	return 0 if hasAbove(values, floor: 3) else 1
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-search-through-a-for-loop-over-a-list-is-pure.maxon:20:2: result of pure function 'firstAbove' must be used
```

<!-- test: a-with-iterator-loop-over-a-list-whose-body-prints-keeps-its-result-droppable -->
A loop body that prints is an effect of the function, whatever the iterator does.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function show(values IntList) returns Integer
	var shown = 0 as Integer

	for (iter, value) in values.withIterator() 'each'
		print("{iter.current()}={value}\n")
		shown = shown + 1
	end 'each'

	return shown
end 'show'

function main() returns ExitCode
	var values = IntList.create()
	values.append(4)

	_ = show(values)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4=4
```

<!-- test: a-with-iterator-loop-over-a-list-whose-body-writes-an-argument-keeps-its-result-droppable -->
A loop body that pushes into an array it was handed is an effect, whatever the container.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer
typealias IntArray = Array with Integer

function copyInto(values IntList, target IntArray) returns Integer
	var copied = 0 as Integer

	for (iter, value) in values.withIterator() 'each'
		target.push(value + iter.current())
		copied = copied + 1
	end 'each'

	return copied
end 'copyInto'

function main() returns ExitCode
	var values = IntList.create()
	values.append(4)
	values.append(5)
	var target = IntArray.create()

	_ = copyInto(values, target: target)

	if target.count() != 2 'wrong'
		return 1
	end 'wrong'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.a-search-through-a-with-iterator-loop-over-a-vector-is-pure -->
A pure search through `withIterator()` over a `Vector`, discarded, is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntVector = Vector with 3 Integer

enum SearchError implements Error
	absent
end 'SearchError'

function firstAbove(values IntVector, floor Integer) returns Integer throws SearchError
	for (iter, value) in values.withIterator() 'each'
		if value > floor 'hit'
			return iter.current()
		end 'hit'
	end 'each'

	throw SearchError.absent
end 'firstAbove'

function hasAbove(values IntVector, floor Integer) returns bool
	_ = try firstAbove(values, floor: floor) otherwise return false
	return true
end 'hasAbove'

function main() returns ExitCode
	var values = IntVector.create()
	try values.set(0, value: 4) otherwise ignore
	try values.set(1, value: 5) otherwise ignore
	try values.set(2, value: 6) otherwise ignore

	return 0 if hasAbove(values, floor: 3) else 1
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-search-through-a-with-iterator-loop-over-a-vector-is-pure.maxon:20:2: result of pure function 'firstAbove' must be used
```

<!-- test: error.a-search-through-a-for-loop-over-a-vector-is-pure -->
The same search through a plain `for ... in` loop.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntVector = Vector with 3 Integer

enum SearchError implements Error
	absent
end 'SearchError'

function firstAbove(values IntVector, floor Integer) returns Integer throws SearchError
	for value in values 'each'
		if value > floor 'hit'
			return value
		end 'hit'
	end 'each'

	throw SearchError.absent
end 'firstAbove'

function hasAbove(values IntVector, floor Integer) returns bool
	_ = try firstAbove(values, floor: floor) otherwise return false
	return true
end 'hasAbove'

function main() returns ExitCode
	var values = IntVector.create()
	try values.set(0, value: 4) otherwise ignore
	try values.set(1, value: 5) otherwise ignore
	try values.set(2, value: 6) otherwise ignore

	return 0 if hasAbove(values, floor: 3) else 1
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-search-through-a-for-loop-over-a-vector-is-pure.maxon:20:2: result of pure function 'firstAbove' must be used
```

<!-- test: a-with-iterator-loop-over-a-vector-whose-body-prints-keeps-its-result-droppable -->
A loop body that prints is an effect of the function, whatever the iterator does.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntVector = Vector with 3 Integer

function show(values IntVector) returns Integer
	var shown = 0 as Integer

	for (iter, value) in values.withIterator() 'each'
		print("{iter.current()}={value}\n")
		shown = shown + 1
	end 'each'

	return shown
end 'show'

function main() returns ExitCode
	var values = IntVector.create()
	try values.set(0, value: 4) otherwise ignore
	try values.set(1, value: 5) otherwise ignore
	try values.set(2, value: 6) otherwise ignore

	_ = show(values)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4=4
5=5
6=6
```

<!-- test: a-with-iterator-loop-over-a-vector-whose-body-writes-an-argument-keeps-its-result-droppable -->
A loop body that pushes into an array it was handed is an effect, whatever the container.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntVector = Vector with 3 Integer
typealias IntArray = Array with Integer

function copyInto(values IntVector, target IntArray) returns Integer
	var copied = 0 as Integer

	for (iter, value) in values.withIterator() 'each'
		target.push(value + iter.current())
		copied = copied + 1
	end 'each'

	return copied
end 'copyInto'

function main() returns ExitCode
	var values = IntVector.create()
	try values.set(0, value: 4) otherwise ignore
	try values.set(1, value: 5) otherwise ignore
	try values.set(2, value: 6) otherwise ignore
	var target = IntArray.create()

	_ = copyInto(values, target: target)

	if target.count() != 3 'wrong'
		return 1
	end 'wrong'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.a-search-through-a-with-iterator-loop-over-a-set-is-pure -->
A pure search through `withIterator()` over a `Set`, discarded, is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntSet = Set with Integer

enum SearchError implements Error
	absent
end 'SearchError'

function firstAbove(values IntSet, floor Integer) returns Integer throws SearchError
	for (iter, value) in values.withIterator() 'each'
		if value > floor 'hit'
			return iter.current()
		end 'hit'
	end 'each'

	throw SearchError.absent
end 'firstAbove'

function hasAbove(values IntSet, floor Integer) returns bool
	_ = try firstAbove(values, floor: floor) otherwise return false
	return true
end 'hasAbove'

function main() returns ExitCode
	var values = IntSet.create()
	values.insert(4)
	values.insert(5)

	return 0 if hasAbove(values, floor: 3) else 1
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-search-through-a-with-iterator-loop-over-a-set-is-pure.maxon:20:2: result of pure function 'firstAbove' must be used
```

<!-- test: error.a-search-through-a-for-loop-over-a-set-is-pure -->
The same search through a plain `for ... in` loop.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntSet = Set with Integer

enum SearchError implements Error
	absent
end 'SearchError'

function firstAbove(values IntSet, floor Integer) returns Integer throws SearchError
	for value in values 'each'
		if value > floor 'hit'
			return value
		end 'hit'
	end 'each'

	throw SearchError.absent
end 'firstAbove'

function hasAbove(values IntSet, floor Integer) returns bool
	_ = try firstAbove(values, floor: floor) otherwise return false
	return true
end 'hasAbove'

function main() returns ExitCode
	var values = IntSet.create()
	values.insert(4)
	values.insert(5)

	return 0 if hasAbove(values, floor: 3) else 1
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-search-through-a-for-loop-over-a-set-is-pure.maxon:20:2: result of pure function 'firstAbove' must be used
```

<!-- test: a-with-iterator-loop-over-a-set-whose-body-prints-keeps-its-result-droppable -->
A loop body that prints is an effect of the function, whatever the iterator does.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntSet = Set with Integer

function show(values IntSet) returns Integer
	var shown = 0 as Integer

	for (iter, value) in values.withIterator() 'each'
		print("{iter.current()}={value}\n")
		shown = shown + 1
	end 'each'

	return shown
end 'show'

function main() returns ExitCode
	var values = IntSet.create()
	values.insert(4)

	_ = show(values)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4=4
```

<!-- test: a-with-iterator-loop-over-a-set-whose-body-writes-an-argument-keeps-its-result-droppable -->
A loop body that pushes into an array it was handed is an effect, whatever the container.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntSet = Set with Integer
typealias IntArray = Array with Integer

function copyInto(values IntSet, target IntArray) returns Integer
	var copied = 0 as Integer

	for (iter, value) in values.withIterator() 'each'
		target.push(value + iter.current())
		copied = copied + 1
	end 'each'

	return copied
end 'copyInto'

function main() returns ExitCode
	var values = IntSet.create()
	values.insert(4)
	values.insert(5)
	var target = IntArray.create()

	_ = copyInto(values, target: target)

	if target.count() != 2 'wrong'
		return 1
	end 'wrong'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.a-search-through-a-with-iterator-loop-over-a-map-is-pure -->
A pure search through the entries `withIterator()` yields over a `Map`, discarded, is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntMap = Map with (Integer, Integer)

enum SearchError implements Error
	absent
end 'SearchError'

function keyOf(table IntMap, wanted Integer) returns Integer throws SearchError
	for (iter, entry) in table.withIterator() 'each'
		let (key, value) = entry

		if value == wanted 'hit'
			return key + iter.current().0
		end 'hit'
	end 'each'

	throw SearchError.absent
end 'keyOf'

function holds(table IntMap, wanted Integer) returns bool
	_ = try keyOf(table, wanted: wanted) otherwise return false
	return true
end 'holds'

function main() returns ExitCode
	var table = IntMap.create()
	table.upsert(1, value: 4)
	table.upsert(2, value: 5)

	return 0 if holds(table, wanted: 4) else 1
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-search-through-a-with-iterator-loop-over-a-map-is-pure.maxon:22:2: result of pure function 'keyOf' must be used
```

<!-- test: error.a-search-through-a-for-loop-over-a-map-is-pure -->
The same search through a plain `for (key, value) in` loop.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntMap = Map with (Integer, Integer)

enum SearchError implements Error
	absent
end 'SearchError'

function keyOf(table IntMap, wanted Integer) returns Integer throws SearchError
	for (key, value) in table 'each'
		if value == wanted 'hit'
			return key
		end 'hit'
	end 'each'

	throw SearchError.absent
end 'keyOf'

function holds(table IntMap, wanted Integer) returns bool
	_ = try keyOf(table, wanted: wanted) otherwise return false
	return true
end 'holds'

function main() returns ExitCode
	var table = IntMap.create()
	table.upsert(1, value: 4)
	table.upsert(2, value: 5)

	return 0 if holds(table, wanted: 4) else 1
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-search-through-a-for-loop-over-a-map-is-pure.maxon:20:2: result of pure function 'keyOf' must be used
```

<!-- test: a-with-iterator-loop-over-a-map-whose-body-prints-keeps-its-result-droppable -->
A loop body that prints is an effect of the function, whatever the iterator does.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntMap = Map with (Integer, Integer)

function show(table IntMap) returns Integer
	var shown = 0 as Integer

	for (iter, entry) in table.withIterator() 'each'
		let (key, value) = entry
		print("{key}={value}/{iter.current().0}\n")
		shown = shown + 1
	end 'each'

	return shown
end 'show'

function main() returns ExitCode
	var table = IntMap.create()
	table.upsert(1, value: 4)

	_ = show(table)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1=4/1
```

<!-- test: a-with-iterator-loop-over-a-map-whose-body-writes-an-argument-keeps-its-result-droppable -->
A loop body that inserts into a map it was handed is an effect, whatever the container.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntMap = Map with (Integer, Integer)

function copyInto(table IntMap, target IntMap) returns Integer
	var copied = 0 as Integer

	for (iter, entry) in table.withIterator() 'each'
		let (key, value) = entry
		target.upsert(key, value: value + iter.current().0)
		copied = copied + 1
	end 'each'

	return copied
end 'copyInto'

function main() returns ExitCode
	var table = IntMap.create()
	table.upsert(1, value: 4)
	table.upsert(2, value: 5)

	var target = IntMap.create()
	_ = copyInto(table, target: target)

	if target.count() != 2 'wrong'
		return 1
	end 'wrong'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.a-record-holding-a-cursor-over-an-argument-takes-a-write-to-the-cursor-for-free -->
A write to a cursor the function made is its own, even when the cursor reads an argument.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Cursor
	var data as IntArray
	var position as Integer

	static function over(data IntArray) returns Cursor
		return Cursor{data: data, position: 0}
	end 'over'

	function step()
		self.position = self.position + 1
	end 'step'
end 'Cursor'

type Walker
	var cursor as Cursor

	static function over(data IntArray) returns Walker
		return Walker{cursor: Cursor.over(data)}
	end 'over'

	function step()
		self.cursor.step()
	end 'step'
end 'Walker'

function walked(data IntArray) returns Integer
	let walker = Walker.over(data)
	walker.step()
	return 1
end 'walked'

function main() returns ExitCode
	var values = IntArray.create()
	values.push(4)
	_ = walked(values)
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-record-holding-a-cursor-over-an-argument-takes-a-write-to-the-cursor-for-free.maxon:39:2: result of pure function 'walked' must be used
```

<!-- test: a-record-holding-a-cursor-over-an-argument-keeps-a-write-through-the-cursor-visible -->
A write through the cursor to the argument it reads is an effect.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Cursor
	var data as IntArray

	static function over(data IntArray) returns Cursor
		return Cursor{data: data}
	end 'over'

	function wipe()
		self.data.clear()
	end 'wipe'
end 'Cursor'

type Walker
	var cursor as Cursor

	static function over(data IntArray) returns Walker
		return Walker{cursor: Cursor.over(data)}
	end 'over'

	function wipe()
		self.cursor.wipe()
	end 'wipe'
end 'Walker'

function wiped(data IntArray) returns Integer
	let walker = Walker.over(data)
	walker.wipe()
	return 1
end 'wiped'

function main() returns ExitCode
	var values = IntArray.create()
	values.push(4)
	_ = wiped(values)

	if values.count() != 0 'wrong'
		return 1
	end 'wrong'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-record-another-call-handed-a-foreign-record-keeps-a-write-through-it-visible -->
A record stored below a record the function made is foreign afterwards, wherever it was reached from.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var number as Integer

	static function create(number Integer) returns Box
		return Box{number: number}
	end 'create'
end 'Box'

type Child
	export var box as Box

	static function create() returns Child
		return Child{box: Box.create(0)}
	end 'create'

	function bump()
		self.box.number = self.box.number + 1
	end 'bump'
end 'Child'

type Parent
	var child as Child

	static function over(child Child) returns Parent
		return Parent{child: child}
	end 'over'

	function adopt(box Box)
		self.child.box = box
	end 'adopt'
end 'Parent'

function bumped(target Box) returns Integer
	let child = Child.create()
	let parent = Parent.over(child)
	parent.adopt(target)
	child.bump()
	return 1
end 'bumped'

function main() returns ExitCode
	let target = Box.create(5)
	_ = bumped(target)

	if target.number != 6 'wrong'
		return 1
	end 'wrong'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.a-record-no-call-handed-a-foreign-record-takes-a-write-through-it-for-free -->
The same program with the argument only read: what is stored below the record is made by the function.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var number as Integer

	static function create(number Integer) returns Box
		return Box{number: number}
	end 'create'
end 'Box'

type Child
	export var box as Box

	static function create(number Integer) returns Child
		return Child{box: Box.create(number)}
	end 'create'

	function bump()
		self.box.number = self.box.number + 1
	end 'bump'
end 'Child'

function bumped(target Box) returns Integer
	let child = Child.create(target.number)
	child.bump()
	return 1
end 'bumped'

function main() returns ExitCode
	let target = Box.create(5)
	_ = bumped(target)

	if target.number != 5 'wrong'
		return 1
	end 'wrong'

	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-record-no-call-handed-a-foreign-record-takes-a-write-through-it-for-free.maxon:32:2: result of pure function 'bumped' must be used
```

<!-- test: a-write-two-levels-below-a-record-made-here-keeps-its-result-droppable -->
A record the function made holds an argument two levels down; a write that reaches it is an effect.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Child
	export var data as IntArray

	static function over(data IntArray) returns Child
		return Child{data: data}
	end 'over'
end 'Child'

type Parent
	var child as Child

	static function over(data IntArray) returns Parent
		return Parent{child: Child.over(data)}
	end 'over'

	function grow()
		self.child.data.push(1)
	end 'grow'
end 'Parent'

function grown(target IntArray) returns Integer
	let parent = Parent.over(target)
	parent.grow()
	return 1
end 'grown'

function main() returns ExitCode
	var values = IntArray.create()
	_ = grown(values)
	return 0 if values.count() == 1 else 1
end 'main'
```
```exitcode
0
```

<!-- test: a-call-two-levels-below-a-record-made-here-keeps-its-result-droppable -->
The same, reached through a call on the argument held two levels down.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Child
	export var data as IntArray

	static function over(data IntArray) returns Child
		return Child{data: data}
	end 'over'
end 'Child'

type Parent
	var child as Child

	static function over(data IntArray) returns Parent
		return Parent{child: Child.over(data)}
	end 'over'

	function shorten()
		self.child.data.truncate(1)
	end 'shorten'
end 'Parent'

function shortened(target IntArray) returns Integer
	let parent = Parent.over(target)
	parent.shorten()
	return 1
end 'shortened'

function main() returns ExitCode
	var values = IntArray.create()
	values.push(2)
	values.push(1)
	_ = shortened(values)
	return 0 if values.count() == 1 else 1
end 'main'
```
```exitcode
0
```

<!-- test: a-closure-calling-a-method-that-writes-two-levels-down-keeps-its-result-droppable -->
The same, reached from a closure that captured the record.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias Action = function() returns Integer

type Child
	export var data as IntArray

	static function over(data IntArray) returns Child
		return Child{data: data}
	end 'over'
end 'Child'

type Parent
	var child as Child

	static function over(data IntArray) returns Parent
		return Parent{child: Child.over(data)}
	end 'over'

	function growCount() returns Integer
		self.child.data.push(1)
		return 1
	end 'growCount'
end 'Parent'

function run(action Action) returns Integer
	return action()
end 'run'

function grown(target IntArray) returns Integer
	let parent = Parent.over(target)
	return run(function() gives parent.growCount())
end 'grown'

function main() returns ExitCode
	var values = IntArray.create()
	_ = grown(values)
	return 0 if values.count() == 1 else 1
end 'main'
```
```exitcode
0
```

<!-- test: a-write-through-records-holding-records-keeps-its-result-droppable -->
The same shape over records instead of arrays.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var number as Integer

	static function create(number Integer) returns Box
		return Box{number: number}
	end 'create'
end 'Box'

type Child
	export var box as Box

	static function over(box Box) returns Child
		return Child{box: box}
	end 'over'
end 'Child'

type Parent
	var child as Child

	static function over(child Child) returns Parent
		return Parent{child: child}
	end 'over'

	function bump()
		self.child.box.number = self.child.box.number + 1
	end 'bump'
end 'Parent'

function bumped(target Box) returns Integer
	let parent = Parent.over(Child.over(target))
	parent.bump()
	return 1
end 'bumped'

function main() returns ExitCode
	let target = Box.create(5)
	_ = bumped(target)
	return 0 if target.number == 6 else 1
end 'main'
```
```exitcode
0
```

<!-- test: an-argument-adopted-through-a-chain-of-calls-keeps-its-result-droppable -->
A record that adopts an argument several calls down is no longer made of what the function made, whatever else the chain does.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Holder
	var items as IntArray

	static function make() returns Holder
		return Holder{items: IntArray.create()}
	end 'make'

	function adopt(fresh IntArray)
		self.items = fresh
	end 'adopt'

	function grow()
		self.items.push(1)
	end 'grow'
end 'Holder'

type Wrapper
	var items as IntArray

	static function make() returns Wrapper
		return Wrapper{items: IntArray.create()}
	end 'make'

	function touchChild()
		self.items.push(1)
	end 'touchChild'
end 'Wrapper'

function relay(target Holder, items IntArray)
	target.adopt(items)
end 'relay'

function hand(target Holder, items IntArray)
	let scratch = Wrapper.make()
	scratch.touchChild()
	relay(target, items: items)
end 'hand'

function grown(items IntArray) returns Integer
	let holder = Holder.make()
	hand(holder, items: items)
	holder.grow()
	return 1
end 'grown'

function main() returns ExitCode
	var values = IntArray.create()
	_ = grown(values)
	return 0 if values.count() == 1 else 1
end 'main'
```
```exitcode
0
```

<!-- test: a-record-adopting-an-argument-through-a-chain-keeps-a-later-write-visible -->
The same shape over records instead of arrays.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var number as Integer

	static function create(number Integer) returns Box
		return Box{number: number}
	end 'create'
end 'Box'

type Holder
	var box as Box

	static function make() returns Holder
		return Holder{box: Box.create(0)}
	end 'make'

	function adopt(fresh Box)
		self.box = fresh
	end 'adopt'

	function bumpBox()
		self.box.number = self.box.number + 1
	end 'bumpBox'
end 'Holder'

type Wrapper
	var box as Box

	static function make() returns Wrapper
		return Wrapper{box: Box.create(0)}
	end 'make'

	function touchChild()
		self.box.number = 1
	end 'touchChild'
end 'Wrapper'

function relay(target Holder, box Box)
	target.adopt(box)
end 'relay'

function hand(target Holder, box Box)
	let scratch = Wrapper.make()
	scratch.touchChild()
	relay(target, box: box)
end 'hand'

function bumped(box Box) returns Integer
	let holder = Holder.make()
	hand(holder, box: box)
	holder.bumpBox()
	return 1
end 'bumped'

function main() returns ExitCode
	var box = Box.create(5)
	_ = bumped(box)
	return 0 if box.number == 6 else 1
end 'main'
```
```exitcode
0
```

<!-- test: an-adoption-through-a-function-parameter-keeps-a-later-write-visible -->
A record handed an argument by a function the caller passed in holds that argument afterwards, so a later write reaches it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias Adopt = function(Holder, IntArray)

type Holder
	var items as IntArray

	static function create() returns Holder
		return Holder{items: IntArray.create()}
	end 'create'

	function adopt(fresh IntArray)
		self.items = fresh
	end 'adopt'

	function grow()
		self.items.push(1)
	end 'grow'
end 'Holder'

function adoptInto(holder Holder, items IntArray)
	holder.adopt(items)
end 'adoptInto'

function apply(holder Holder, items IntArray, action Adopt)
	action(holder, items)
end 'apply'

function made(items IntArray, action Adopt) returns Holder
	var holder = Holder.create()
	apply(holder, items: items, action: action)
	return holder
end 'made'

function grown(target IntArray) returns Integer
	let holder = made(target, action: adoptInto)
	holder.grow()
	return 1
end 'grown'

function main() returns ExitCode
	var values = IntArray.create()
	_ = grown(values)
	return 0 if values.count() == 1 else 1
end 'main'
```
```exitcode
0
```

<!-- test: an-adoption-through-a-function-parameter-keeps-a-later-write-visible-over-records -->
The same shape over records instead of arrays.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Adopt = function(Holder, Box)

type Box
	export var number as Integer

	static function create(number Integer) returns Box
		return Box{number: number}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function create() returns Holder
		return Holder{box: Box.create(0)}
	end 'create'

	function bump()
		self.box.number = self.box.number + 1
	end 'bump'
end 'Holder'

function adoptInto(holder Holder, box Box)
	holder.box = box
end 'adoptInto'

function apply(holder Holder, box Box, action Adopt)
	action(holder, box)
end 'apply'

function made(box Box, action Adopt) returns Holder
	var holder = Holder.create()
	apply(holder, box: box, action: action)
	return holder
end 'made'

function bumped(target Box) returns Integer
	let holder = made(target, action: adoptInto)
	holder.bump()
	return 1
end 'bumped'

function main() returns ExitCode
	let target = Box.create(5)
	_ = bumped(target)
	return 0 if target.number == 6 else 1
end 'main'
```
```exitcode
0
```

<!-- test: an-adoption-in-a-generic-method-that-stops-at-a-drop-keeps-a-later-write-visible -->
A generic method whose body, read without its type arguments, stops at the drop of a local it may not own still stores its argument.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Shelf uses T
	typealias TArray = Array with T
	var items as IntArray
	var flagged as bool

	static function create() returns Self
		return Self{items: IntArray.create(), flagged: false}
	end 'create'

	function adopt(fresh IntArray, scratchy bool)
		if scratchy 'scratch'
			var scratch = TArray.create()

			if scratch.isEmpty() 'empty'
				self.flagged = true
			end 'empty'
		end 'scratch'

		self.items = fresh
	end 'adopt'

	static function filled(fresh IntArray) returns Self
		var made = Self.create()
		made.adopt(fresh, scratchy: false)
		return made
	end 'filled'

	function grow()
		self.items.push(1)
	end 'grow'
end 'Shelf'

typealias IntShelf = Shelf with Integer

function grown(target IntArray) returns Integer
	let shelf = IntShelf.filled(target)
	shelf.grow()
	return 1
end 'grown'

function main() returns ExitCode
	var values = IntArray.create()
	_ = grown(values)
	return 0 if values.count() == 1 else 1
end 'main'
```
```exitcode
0
```

<!-- test: moving-a-node-out-of-an-argument-chain-keeps-its-result-droppable -->
Reinserting a node moves it out of the chain it was in, which is the argument's chain, not the receiver's.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntChain = __ManagedList with Integer

function steal(source IntChain) returns Integer
	var mine = IntChain.create()

	let node = try source.head() otherwise 'empty'
		return 0
	end 'empty'

	mine.reinsertFirst(node)
	return mine.count() as Integer
end 'steal'

function main() returns ExitCode
	var chain = IntChain.create()
	_ = chain.insertLast(5)
	_ = steal(chain)
	return 0 if chain.count() == 0 else 1
end 'main'
```
```exitcode
0
```

<!-- test: moving-a-node-out-of-a-global-chain-keeps-its-result-droppable -->
The same move out of a module-level chain.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntChain = __ManagedList with Integer

var shelf = IntChain.create()

function takeFromShelf() returns Integer
	var mine = IntChain.create()

	let node = try shelf.head() otherwise 'empty'
		return 0
	end 'empty'

	mine.reinsertLast(node)
	return mine.count() as Integer
end 'takeFromShelf'

function main() returns ExitCode
	_ = shelf.insertLast(5)
	_ = takeFromShelf()
	return 0 if shelf.count() == 0 else 1
end 'main'
```
```exitcode
0
```

<!-- test: a-loop-that-reaches-a-global-through-a-co-owned-copy-keeps-its-result-droppable -->
A record the first trip made and a later trip swapped for a global is not made by the function, however many names it passes through.
```maxon
typealias Small = int(0 to 100)

type Box
	export var n as Small

	static function create(n Small) returns Box
		return Box{n: n}
	end 'create'
end 'Box'

var shared = Box.create(0)

function scribble() returns Small
	var held = Box.create(0)
	var other = Box.create(0)
	var i = 0 as Small

	while i < 3 'loop'
		let fresh = Box.create(0)
		let b = held
		let pick = fresh if i < 2 else b
		other = pick
		other.n = 7
		held = shared
		i = i + 1
	end 'loop'

	return other.n
end 'scribble'

function main() returns ExitCode
	_ = scribble()
	return 0 if shared.n == 7 else 1
end 'main'
```
```exitcode
0
```

<!-- test: error.a-static-returning-another-instance-of-its-type-is-judged-on-its-own-body -->
A static of a generic type that returns a different instance than the one it was called on is read without the called instance.
```maxon
typealias Integer = int(i64.min to i64.max)

typealias IntTally = Tally with Integer

type Tally uses T
	export var weight as Integer

	static function of(seed T) returns IntTally
		return IntTally.build(1)
	end 'of'

	static function build(weight Integer) returns Self
		return Self{weight: weight}
	end 'build'
end 'Tally'

typealias WordTally = Tally with String

function weigh(seed String) returns Integer
	let tally = WordTally.of(seed)
	return tally.weight
end 'weigh'

function main() returns ExitCode
	_ = weigh("word")
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-static-returning-another-instance-of-its-type-is-judged-on-its-own-body.maxon:26:2: result of pure function 'weigh' must be used
```

<!-- test: moving-a-node-out-of-a-field-chain-keeps-its-result-droppable -->
The same move out of a chain a record holds, where the method that moves it is the only thing that touches the chain.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntChain = __ManagedList with Integer

type Shelf
	var chain as IntChain

	static function create() returns Shelf
		return Shelf{chain: IntChain.create()}
	end 'create'

	function stock()
		_ = self.chain.insertLast(5)
	end 'stock'

	function left() returns Integer
		return self.chain.count() as Integer
	end 'left'

	function steal() returns Integer
		var mine = IntChain.create()

		let node = try self.chain.head() otherwise 'empty'
			return 0
		end 'empty'

		mine.reinsertFirst(node)
		return mine.count() as Integer
	end 'steal'
end 'Shelf'

function main() returns ExitCode
	let shelf = Shelf.create()
	shelf.stock()
	_ = shelf.steal()
	return 0 if shelf.left() == 0 else 1
end 'main'
```
```exitcode
0
```

<!-- test: an-overload-the-call-reached-keeps-its-result-droppable -->
A discard is judged by the overload the call reaches, not by the first one declared: the zero-argument digest bumps a counter.
```maxon
typealias Code = int(0 to u32.max)

var noise = 0 as Code

type Loud
	export var x as Code

	static function create(x Code) returns Self
		return Self{x: x}
	end 'create'

	function digest(salt Code) returns Code
		return self.x + salt
	end 'digest'

	function digest() returns Code
		noise = noise + 1
		return self.x
	end 'digest'
end 'Loud'

function main() returns ExitCode
	let loud = Loud.create(3)
	_ = loud.digest()
	return noise - 1
end 'main'
```
```exitcode
0
```

<!-- test: a-free-overload-the-call-reached-keeps-its-result-droppable -->
The same for free functions: the String member prints, the number member before it is pure.
```maxon
typealias Number = int(0 to u32.max)

function describe(x Number) returns Number
	return x + 1
end 'describe'

function describe(s String) returns Number
	print("{s}\n")
	return 0
end 'describe'

function main() returns ExitCode
	_ = describe("hi")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hi
```

<!-- test: error.a-pure-overload-the-call-reached-is-refused-though-the-first-one-prints -->
The converse: the member the call reaches is pure, so its discarded result is refused whatever the first member does.
```maxon
typealias Number = int(0 to u32.max)

function describe(x Number) returns Number
	print("{x}\n")
	return x
end 'describe'

function describe(s String) returns Number
	return s.count() as Number
end 'describe'

function main() returns ExitCode
	_ = describe("hi")
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-pure-overload-the-call-reached-is-refused-though-the-first-one-prints.maxon:14:2: result of pure function 'describe' must be used
```

<!-- test: error.a-pure-method-overload-is-reported-under-its-bare-name -->
The diagnostic names the member the author wrote, never the registration name an overload carries.
```maxon
typealias Code = int(0 to u32.max)

var noise = 0 as Code

type Loud
	export var x as Code

	static function create(x Code) returns Self
		return Self{x: x}
	end 'create'

	function digest(salt Code) returns Code
		noise = noise + salt
		return self.x
	end 'digest'

	function digest() returns Code
		return self.x
	end 'digest'
end 'Loud'

function main() returns ExitCode
	let loud = Loud.create(3)
	_ = loud.digest()
	return noise
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-pure-method-overload-is-reported-under-its-bare-name.maxon:25:2: result of pure function 'Loud.digest' must be used
```

<!-- test: error.a-bare-call-of-the-impure-overload-the-call-reached-earns-e3065 -->
A bare statement of the member that bumps a counter earns the impure code, not the pure one the first member would.
```maxon
typealias Code = int(0 to u32.max)

var noise = 0 as Code

type Loud
	export var x as Code

	static function create(x Code) returns Self
		return Self{x: x}
	end 'create'

	function digest(salt Code) returns Code
		return self.x + salt
	end 'digest'

	function digest() returns Code
		noise = noise + 1
		return self.x
	end 'digest'
end 'Loud'

function main() returns ExitCode
	let loud = Loud.create(3)
	loud.digest()
	return noise - 1
end 'main'
```
```maxoncstderr
error E3065: specs/discarded-results/error.a-bare-call-of-the-impure-overload-the-call-reached-earns-e3065.maxon:25:7: result of 'Loud.digest' is not used (use '_ = expr' to discard)
```

<!-- test: an-allocation-counter-read-keeps-its-result-droppable -->
A counter read is a read of a figure that moves, like a clock read, so its function is not pure.
```maxon
typealias Tally = int(0 to u64.max)

function allocated() returns Tally
	return __Builtins.processAllocTotal() as Tally
end 'allocated'

function main() returns ExitCode
	let before = allocated()
	var kept = ByteArray.create()
	kept.push(1)
	_ = allocated()
	return 0 if allocated() > before else 1
end 'main'
```
```exitcode
0
```

<!-- test: error.a-record-holding-only-what-the-function-made-takes-a-deep-write-for-free -->
A record that holds nothing the function was handed may be written through its fields; one that holds an argument may not.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Cursor
	var data as IntArray

	static function over(data IntArray) returns Cursor
		return Cursor{data: data}
	end 'over'

	function drop()
		self.data.clear()
	end 'drop'
end 'Cursor'

function scratch() returns Integer
	let cursor = Cursor.over(IntArray.create())
	cursor.drop()
	return 1
end 'scratch'

function main() returns ExitCode
	_ = scratch()
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/discarded-results/error.a-record-holding-only-what-the-function-made-takes-a-deep-write-for-free.maxon:24:2: result of pure function 'scratch' must be used
```

<!-- test: a-record-holding-an-argument-keeps-a-deep-write-visible -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Cursor
	var data as IntArray

	static function over(data IntArray) returns Cursor
		return Cursor{data: data}
	end 'over'

	function drop()
		self.data.clear()
	end 'drop'
end 'Cursor'

function wipe(values IntArray) returns Integer
	let cursor = Cursor.over(values)
	cursor.drop()
	return 1
end 'wipe'

function main() returns ExitCode
	var values = IntArray.create()
	values.push(4)
	_ = wipe(values)

	if values.count() != 0 'wrong'
		return 1
	end 'wrong'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-record-given-an-argument-after-it-was-made-keeps-a-deep-write-visible -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Cursor
	var data as IntArray

	static function over(data IntArray) returns Cursor
		return Cursor{data: data}
	end 'over'

	function adopt(extra IntArray)
		self.data = extra
	end 'adopt'

	function drop()
		self.data.clear()
	end 'drop'
end 'Cursor'

function reset(extra IntArray) returns Integer
	let cursor = Cursor.over(IntArray.create())
	cursor.adopt(extra)
	cursor.drop()
	return 1
end 'reset'

function main() returns ExitCode
	var values = IntArray.create()
	values.push(4)
	_ = reset(values)

	if values.count() != 0 'wrong'
		return 1
	end 'wrong'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-function-whose-record-drops-a-promise-it-made-keeps-its-result-droppable -->
Dropping a record that holds a promise cancels the green thread, which is more than bookkeeping: the worker below never
runs, so the flag stays 0.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

var flag = 0 as Integer

type Holder
	var promise as IntPromise

	static function wrap(promise IntPromise) returns Holder
		return Holder{promise: promise}
	end 'wrap'
end 'Holder'

function worker() returns Integer
	Scheduler.yield()
	flag = 1
	return 1
end 'worker'

function keep() returns Integer
	var promise = async worker()
	let holder = Holder.wrap(promise)
	_ = holder
	return 1
end 'keep'

function main() returns ExitCode
	_ = keep()
	return flag as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: a-function-whose-array-drops-a-promise-it-made-keeps-its-result-droppable -->
An array a function fills with a promise it made drops that promise, and the thread with it, when the function ends.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias PromiseArray = Array with IntPromise

var flag = 0 as Integer

function worker() returns Integer
	Scheduler.yield()
	flag = 1
	return 1
end 'worker'

function park() returns Integer
	var parked = PromiseArray.create()
	parked.push(async worker())
	return 1
end 'park'

function main() returns ExitCode
	_ = park()
	return flag as ExitCode
end 'main'
```
```exitcode
0
```
