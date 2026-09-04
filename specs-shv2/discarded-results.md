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
error E3064: specs/fragments/discarded-results/pure-function-discarded.test:10:2: result of pure function 'double' must be used
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
error E3064: specs/fragments/discarded-results/pure-function-let-discard.test:10:2: result of pure function 'double' must be used
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
error E3064: specs/fragments/discarded-results/pure-method-underscore-discard.test:4:2: result of pure function 'String.count' must be used
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
error E3065: specs/fragments/discarded-results/impure-function-discarded.test:13:2: result of 'incrementAndGet' is not used (use '_ = expr' to discard)
```

<!-- test: impure-method-statement-discarded -->
A method that writes `self` is IMPURE, so its result may not fall off a statement: E3065, the same
cure as a free function.

⚠ **THE ORACLE DIVERGES HERE, AND IT DIVERGES TWICE** (MEASURED 2026-09-03 on this exact program). It
classifies a `self` field write as PURE and answers `E3064: result of pure function 'bump' must be used` —
a different code, and the subject unqualified where shv2 spells the method under the name it is keyed by
(`Counter.bump`, as `String.count` is spelled above). The consequence is that the CURE differs: `_ = c.bump()`
compiles here and is refused there, since E3064 admits no `_ =` opt-out. A write the caller observes
afterwards is an effect, so shv2 refuses under the impure code and offers the discard.
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
error E3065: specs/fragments/discarded-results/impure-method-statement-discarded.test:19:4: result of 'Counter.bump' is not used (use '_ = expr' to discard)
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
error E3065: specs/fragments/discarded-results/impure-field-chain-statement-discarded.test:29:10: result of 'Inner.bump' is not used (use '_ = expr' to discard)
```

<!-- test: impure-static-member-statement-discarded -->
⭐⭐ **A STATIC CALLED ON A LINE OF ITS OWN, AND THE CASE THAT OPENED THAT DOOR.** `Clock.tick()` was
`E2015: Unsupported: identifier statement` — a message about the SHAPE, said of a shape the same compiler
lowers perfectly one line up in expression position. The statement door claims a qualified static call
whatever it returns and files the site here, so the RESULT is what earns the refusal: an impure static is
E3065 with the `_ =` cure, exactly as the three shapes above are. The oracle answers E3065 for this program
too, spelling the subject `tick` where shv2 spells it under the `Type.method` key it is stored by.
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
error E3065: specs/fragments/discarded-results/impure-static-member-statement-discarded.test:13:8: result of 'Clock.tick' is not used (use '_ = expr' to discard)
```

<!-- test: pure-static-member-statement-discarded -->
The PURE half of the same door, and the half that says the verdict is the discard rule's rather than the
statement door's: a static with nothing to do but compute is E3064, which admits no `_ =` opt-out at all.
Without it the door would be provable only in its impure direction. Measured identical on the oracle.
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
error E3064: specs/fragments/discarded-results/pure-static-member-statement-discarded.test:11:8: result of pure function 'Clock.now' must be used
```

<!-- test: void-static-member-statement-ok -->
⭐ **A VOID RESULT IS NOT A DISCARD, and this is the case that keeps the widened door from refusing the
programs it was widened for.** `recordDiscardedCallResult` drops a void result before filing anything, so a
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
call's answer, and a `_` prefix does not suppress it. Both compilers report it here (MEASURED).
```maxon

function main() returns ExitCode
	let x = 42
	return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/discarded-results/underscore-not-prefix-suppression.test:4:6: unused variable: 'x'
```

<!-- test: underscore-exact-discard -->
```maxon

function main() returns ExitCode
	_ = 42
	return 0
end 'main'
```
```maxoncstderr
error E3067: specs/fragments/discarded-results/underscore-exact-discard.test:4:2: expected a function call
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
error E3064: specs/fragments/discarded-results/tuple-all-discard-pure.test:10:2: result of pure function 'makePair' must be used
```

<!-- test: math-intrinsic-discarded -->
⭐ **A MATH INTRINSIC DISCARDED IN STATEMENT POSITION IS E3064, AND IT NEEDS NO PURITY ANALYSIS TO SAY SO.**
`round`, `floor`, `ceil`, `sqrt`, `abs`, `trunc`, `min` and `max` are compiler-owned: each IS a machine
instruction over its arguments, reading no memory and writing none. A statement that takes none of the
answer therefore has no other reason to run, and the parser can prove it at the call — where the
whole-program effect summary the DECLARED-callee doors wait on cannot even be asked, since an intrinsic
emits no call and has no entry in the module's function index.

⛔ It used to compile SILENTLY, exit 0, emitting the instruction and throwing the answer away.
```maxon
function main() returns ExitCode
	round(1.5)
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/fragments/discarded-results/math-intrinsic-discarded.test:3:2: result of pure function 'round' must be used
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
error E3065: specs/fragments/discarded-results/transitive-impure.test:15:2: result of 'computeAndPrint' is not used (use '_ = expr' to discard)
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
error E3064: specs/fragments/discarded-results/try-pure-let-discard.test:17:2: result of pure function 'parseNum' must be used
```

<!-- test: try-impure-let-discard -->
```maxon

typealias Integer = int(i64.min to i64.max)

var counter = 0 as Integer

enum ParseError implements Error
	invalidFormat
end 'ParseError'

function parseNum(s String) returns Integer throws ParseError
	counter = counter + s.byteLength()
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

`Array.first`/`get`/`count` are not corpus bodies in shv2 — the parser lowers each straight to a runtime
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
error E3064: specs/fragments/discarded-results/pure-array-read-underscore-discard.test:8:2: result of pure function 'Array.first' must be used
```

A read that MOVES the element out is not one of them: `pop` vacates the slot, so the call changed the
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
error E3064: specs/fragments/discarded-results/map-get-underscore-discard.test:8:2: result of pure function 'Map.get' must be used
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
error E3064: specs/fragments/discarded-results/set-contains-underscore-discard.test:8:2: result of pure function 'Set.contains' must be used
```

⚠ The reference names this member `Set.contains$element` — its own overload key, in a message. shv2 reports
the member's registration name, which for an un-overloaded member is the bare `Set.contains`. Same member,
same code, same position; only the subject is spelled without the key.

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
than on the receiver, which is where the reference puts it (measured at `arr.count()`, `b.ops.count()` and
`utils.twice(4)` alike).

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
error E3064: specs/fragments/discarded-results/method-call-statement-discarded.test:8:6: result of pure function 'Array.count' must be used
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
`stdlib/` and `maxon-shv2/` is this shape.

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

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export function digest() returns Code
		return self.x
	end 'digest'
end 'Quiet'

type Loud implements Digest
	export var x as Code

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export function digest(salt Code) returns Code
		return self.x + salt
	end 'digest'

	export function digest() returns Code
		noise = noise + 1
		return self.x
	end 'digest'
end 'Loud'

type Box uses T where T is Digest
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function itemDigest() returns Code
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
MEASURED against the runnable oracle: all three are E3067 at the `_`, same message, same column.

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
error E3067: specs/fragments/discarded-results/error.underscore-discard-of-an-intrinsic.test:3:2: expected a function call
```

<!-- test: error.underscore-discard-of-a-field-read -->
```maxon
typealias Tally = int(0 to u64.max)

type Counter
	export var count as Tally

	export static function create(count Tally) returns Self
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
error E3067: specs/fragments/discarded-results/error.underscore-discard-of-a-field-read.test:14:2: expected a function call
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
error E3067: specs/fragments/discarded-results/error.underscore-discard-of-an-arithmetic-expression.test:7:2: expected a function call
```

⭐⭐ **THE ONE CALL-FREE DISCARD THAT STAYS LEGAL IS A BARE BINDING NAME, BECAUSE E3012 LEAVES NOWHERE ELSE
TO STAND.** `_ = seen` is the only spelling that names a binding without using it, and E3012 requires every
binding to be named — delete the acknowledgement and `unused variable` is what you get, on BOTH compilers
(MEASURED). A spawned promise deliberately never awaited is the shape that needs it: the binding must live
to scope exit, because that is where the drop happens.

⚠ **THE ORACLE REFUSES THIS ONE**, and it is the single shape where matching it is impossible — it demands
the statement through E3012 and refuses it through E3067, escaping its own contradiction only by having no
program of this shape. The line is drawn at ONE name token: every longer call-free right-hand side is
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
"none" is silence for E3064 and would be a REFUSAL here — MEASURED 2026-09-03, 54 statements are exactly this
shape (52 in `maxon-shv2/`, 2 in `stdlib/`), and the oracle compiles every one. The fallback has to DIVERGE
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
⚠ The oracle refuses it (MEASURED 2026-09-03: `E3067: expected a function call`, because its rule asks only
whether the LAST op is a call) — this case is what pins the widening, which the 54 laundered `try` statements
above are the reason for.

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
