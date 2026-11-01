---
title: Functions
description: "Declaring and calling functions: named arguments, defaults, overloads, closures, and purity."
sidebar:
  order: 7
---

## Declaration

```text
function name(param Type, other Type = default) returns ReturnType throws ErrorType
	statements
end 'name'
```

```maxon
typealias Amount = int(i64.min to i64.max)

function add(a Amount, b Amount) returns Amount
	return a + b
end 'add'

function greet(name String)
	print("Hello, {name}\n")
end 'greet'
```

- The label after `end` repeats the function's name.
- A function that returns a value declares `returns Type`; one that returns nothing omits the clause.
- A parameter is written `name Type`. Parameter types follow the
  [typealias rule](/docs/language/types/#primitives-go-through-a-typealias).
- A function that can fail declares `throws ErrorType` (see
  [Error Handling](/docs/language/error-handling/)).
- Name a parameter `_` to accept and ignore an argument: `function onClick(_ MouseEvent)`.
- A function is private to its file unless marked `export`, `module` or `public` (see
  [Namespaces](/docs/language/namespaces/)).

## Named Arguments

Maxon calls use a **first-positional, rest-named** rule:

- The first argument is positional. When the callee's first parameter has a default, the first argument
  may also be written `name: value`, naming that parameter or any later one. Labelling a first argument
  whose parameter is required is **E2052** (`the first argument cannot be named because its parameter
  has no default`). For a method the first parameter is the one after the receiver; for a service
  message, the one after `self`.
- Every later argument is written `name: value`; omitting a label is **E2053**.
- Named arguments may appear in any order, and parameters with defaults may be omitted.

```maxon
typealias Amount = int(i64.min to i64.max)

function connect(host String, port Amount, secure bool = false) returns String
	return "{host}:{port} secure={secure}"
end 'connect'

function main() returns ExitCode
	print("{connect("localhost", port: 8080)}\n")                    // localhost:8080 secure=false
	print("{connect("example.com", secure: true, port: 443)}\n")     // example.com:443 secure=true
	return 0
end 'main'
```

## Default Values

A parameter may declare a default, used when the call omits that argument. The default is any expression —
a literal, an enum case, a factory call, a byte string — and is evaluated at each call that needs it.

```maxon
typealias Retries = int(0 to 10)

enum Priority
	low
	medium
	high
end 'Priority'

function greet(name String, title String = "Mr.")
	print("Hello, {title} {name}\n")
end 'greet'

function schedule(job String, retries Retries = 3, level Priority = Priority.medium, separator Character = '/')
	print("{job}{separator}{retries}{separator}{level.name}\n")
end 'schedule'

function main() returns ExitCode
	greet("Smith")                          // Hello, Mr. Smith
	greet("Smith", title: "Dr.")            // Hello, Dr. Smith
	schedule("backup")                      // backup/3/medium
	schedule("sync", level: Priority.high)  // sync/3/high
	return 0
end 'main'
```

A default may sit on any parameter, before or after the required ones. When the first parameter has a
default, a call may omit it and name a later argument first:

```maxon
typealias Port = int(0 to 65535)
typealias Retries = int(0 to 10)

function connect(port Port = 80, retries Retries = 3)
	print("port {port}, {retries} retries\n")
end 'connect'

function main() returns ExitCode
	connect()                    // port 80, 3 retries
	connect(8080)                // port 8080, 3 retries
	connect(port: 8080)          // port 8080, 3 retries
	connect(retries: 5)          // port 80, 5 retries
	return 0
end 'main'
```

A required parameter after a defaulted first one is named: `function f(a Integer = 1, b Integer)` is
called `f(b: 2)` or `f(5, b: 2)`.

## Caller-Location Defaults (`__line__`, `__file__`)

`__line__` and `__file__` are legal **only** as a parameter's default value. They expand at each **call
site**, so a helper — an assertion, a logger — can report where it was called from:

| Default | Value at the call site | Declare the parameter as |
|---------|------------------------|--------------------------|
| `__line__` | the line of the callee's name | `SourceLineNumber` |
| `__file__` | the calling file's path, relative to the compile root, with `/` separators | `String` |

```maxon
function check(ok bool, message String, from String = __file__, at SourceLineNumber = __line__)
	if not ok 'failed'
		print("{from}:{at}: {message}\n")
	end 'failed'
end 'check'

function main() returns ExitCode
	check(1 + 1 == 2, message: "arithmetic")
	check(2 + 2 == 5, message: "expected 5")                          // main.maxon:9: expected 5
	check(false, message: "forwarded", from: "other.maxon", at: 42)   // other.maxon:42: forwarded
	return 0
end 'main'
```

- Declare both or neither: a line number without its file names a line in no particular file.
- An explicit argument replaces the default, which is how a helper forwards the location it was given:
  `check(ok, message: m, from: from, at: at)`.
- `__file__` is always relative, so the same source builds the same binary on any machine.
- Anywhere else — an ordinary expression, a struct field default — is **E2060**.

## One Declaration per Name

Maxon has no overloading. A function name is declared once per directory, a method name once per type, and
an interface requirement name once per interface. A second declaration is
[E3006](/docs/cli/error-codes/#e3006--duplicatedefinition), reported at its name:

```text
error E3006: main.maxon:5:10: duplicate definition of 'measure' — already declared as `function measure`
```

Give each function its own name, or make the differing argument optional with a default parameter:

```maxon
typealias Tally = int(0 to u64.max)

function measureNumber(value Tally) returns Tally
	return value * 2
end 'measureNumber'

function measureText(value String) returns Tally
	return value.count()
end 'measureText'

function main() returns ExitCode
	print("{measureNumber(21)} {measureText("hello")}\n")    // 42 5
	return 0
end 'main'
```

- A method of a type and a method an `extension` of it adds share the type's one namespace, so they are one
  declaration of one name. A `static` and an instance method of one name are two declarations of it, and a
  field and a method of a type never share a name.
- A standard-library operation that varies by argument type has one name per form: `Array.containsSequence`,
  `Array.sortBy`, `String.containsCharacter`, `String.sliceLength` and the like. See
  [STDLIB_REFERENCE.md](/docs/stdlib/).

## Parameter Passing

A parameter is a `let` binding passed by value: reassigning it is **E2013** (`'n' is a parameter and cannot be
reassigned; mark it mutable or copy it into a local var`). A parameter of a value type — a number, `bool`, an
enum or `String` — that the function reassigns is marked `mutable` before its type. A `mutable` parameter is
passed by reference, so the write reaches the caller's variable:

```maxon
typealias Tally = int(0 to u64.max)

function increment(n mutable Tally)
	n = n + 1
end 'increment'

function main() returns ExitCode
	var x = 10 as Tally
	increment(x)
	print("{x}\n")     // 11
	return 0
end 'main'
```

- A record-type parameter is never rebound, marked or not: reassigning one is **E2013** (`'r' is a record
  parameter and cannot be reassigned; copy it into a local var`). Writing through it — a field store
  (`p.x = 99`) or a mutating call on a field (`p.items.push(x)`) — is not a reassignment and reaches the
  caller's record. A generic body is checked against the bound of its type parameter only, so a parameter
  whose type is a type parameter is never reassigned either: the function returns the new value.
- A message parameter and a factory parameter cannot be marked `mutable` (**E2076**): a message hands its
  arguments to another green thread and a `spawn` hands the factory's arguments to the service, which owns
  them and writes nothing back.
- Passing a `var` lets the callee's writes propagate. Any storage you could assign to at that point may be
  passed and is written in place: a local `var`, a module-level `var`, a field of a binding (`p.x`), a field
  of the receiver (`count` or `self.count`), and a chain of them (`p.a.b`). The rule is exactly the
  assignment rule — an argument the callee writes is accepted here if and only if writing the same thing at
  the call site would be accepted.
- Passing a [read-only](/docs/language/memory-model/#read-only-values) value to a `mutable` parameter is **E3159** (`cannot pass
  'y', which is read-only (a `let` binding), to a function that writes parameter 'n'`). A literal or another
  expression is no storage for the write to reach, so it is **E3159** too (`cannot pass a literal to a
  function that writes parameter 'n'; pass a `var``). The receiver of a method counts as a parameter, so
  `let acc = Accumulator.create()` followed by `acc.add(10)`, where `add` writes a field of `acc`, is refused.
  A `let` field, or anything reached through a read-only value, is refused for the same reason the
  equivalent assignment is.
- A function type spells the marker: `typealias Step = function(mutable Tally)`. A function, closure or
  interface conformer fits a function type or requirement only when every parameter's marker is the same as
  the type's, so a function that reassigns its parameter does not convert to `function(Tally)` (**E3005**),
  and one that declares `mutable` but only reads does not either. A call through a function value or an
  interface passes the argument's storage at each `mutable` position, and a read-only value handed to such
  a position is **E3159**, as at a direct call; a literal or another expression is **E3159** too. Through
  a function value or an interface requirement the message names the position (`to a function that writes
  argument 1`).
- A variable passed by reference to a parameter whose alias is wider than its own, or that names no alias,
  is range-checked when the call returns.
- Passing storage to a parameter the callee **reassigns**, while another argument of the same call reaches
  that storage, is **E3186** (see [Aliasing Between Arguments](/docs/language/memory-model/#aliasing-between-arguments)).

## Function Types and Function Values

Functions are values: a bare function name (no parentheses) is a reference to it, and it can be stored,
passed and returned. A function type is written with `function` and is always named by a typealias; the
alias is what appears in parameters, returns, fields and generic arguments.

```maxon
typealias Score = int(i64.min to i64.max)
typealias UnaryOp = function(Score) returns Score

function double(x Score) returns Score
	return x * 2
end 'double'

function apply(f UnaryOp, x Score) returns Score
	return f(x)
end 'apply'

function pickDouble() returns UnaryOp
	return double
end 'pickDouble'

function main() returns ExitCode
	let f = pickDouble()
	print("{f(21)} {apply(double, x: 4)}\n")    // 42 8
	return 0
end 'main'
```

A function value fits a function type only by variance. A read-only parameter
may be wider than the type's (the function accepts every value the type's callers pass), a `mutable`
parameter must be the same type and carry the same marker, and the result may be narrower (everything the function
returns is something the type promises). A bare `int` in the type's declaration is an unranged integer, so
a function over a narrower alias does not fit it. That is checked wherever the value meets the type: a
variable, a global, a field, a tuple or container element, a union payload, an argument and a `return`
are **E3005**, and an `as` cast is **E3009**. A call through a function value is checked against the type
the value is held at, and dispatches to the function it holds.

Omit `returns` for a function type that returns nothing: `typealias Callback = function()`. A function type cannot express `throws`, so a
throwing function cannot be used as a value (**E3101**) — wrap it in a function that handles the error.
`Type.method` written without its call — a static method, an instance method read through its type, or a
static the compiler provides such as `int.fromString` — is **E2078**, and assigning to one (`Ops.twice = f`)
is **E2013**; pass a closure that calls it, `apply(function(n) gives Ops.twice(n), x: 4)`. A function type says
which promise parameters its callee takes: see [Capability Qualifiers](#capability-qualifiers-keep-sole-returns-readonly).
Function-type aliases are [brands](/docs/language/ranged-typealiases/#generic-instance-and-function-type-aliases-are-brands).

## Capability Qualifiers (`keep`, `sole`, `returns readonly`)

A call through a function value or an interface method has no body the compiler can read, so the boundary
declares what the callee does with its values. The qualifier is written before the type:

| Qualifier | Meaning |
|-----------|---------|
| `keep` on a parameter | the callee keeps the argument where something may write it |
| `sole` on a parameter | the callee consumes the argument and owns it, so it may keep it anywhere |
| `returns sole T` | the result is fresh, reached by no other name |
| `returns readonly T` | the result is read-only |

```maxon
typealias Count = int(0 to 1000)
typealias Sink = function(b keep Box)
typealias Maker = function() returns sole Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

interface Source
	function current() returns readonly Box
end 'Source'
```

- They are written only on a parameter or result of a function-type alias or an
  [interface](/docs/language/composite-types/#interfaces) requirement, and on the result of a [generic function](/docs/language/composite-types/#generic-functions). Anywhere
  else — a parameter of a function with a body, a cast, a binding — a qualifier is **E2076**.
- A caller trusts the declaration. An argument to a `keep` parameter follows the rule for a function that keeps
  it, so a `let` read again afterwards is **E3078**. A `sole` parameter consumes its argument: pass a fresh value,
  or a `let` or `var` at its last use (a later use is **E3102**); a read-only value, a parameter, a field of a
  record that stays reachable, or a `var` a [view](/docs/language/variables/#views-view) has seen is **E3078**. The `let` of a
  `returns sole` result is fresh, and a `let` of a `returns readonly` result is read-only.
- A promise parameter of a function type is taken by the callee when it is written `keep` or `sole`, and only
  then: a call through the type moves the promise into the callee, which stores, awaits, cancels or returns it,
  and an unqualified promise parameter stays with the caller, who still owns it after the call. A named
  function used as a value has its own type, so the call honours what the function does. A function value
  converted to an alias whose qualifier disagrees with the function — it takes a promise the alias leaves
  unqualified, or the alias declares `keep` or `sole` on a promise the function only reads — is **E3189**.
- A function value converted to the alias, a conformer's method, or a generic function's body that does more
  than its boundary declares — keeps a parameter not declared `keep` or `sole`, or returns state something may
  write where `returns sole` or `returns readonly` is declared — is **E3189**. A function value typed by one
  alias converts to another, by a `return`, a store or an argument, only when it keeps no parameter the target
  does not declare and declares at least the result capability the target declares.

## Closures

A closure is an anonymous function written `function(parameters) gives expression`:

```maxon
typealias Score = int(i64.min to i64.max)
typealias UnaryOp = function(Score) returns Score

function apply(f UnaryOp, x Score) returns Score
	return f(x)
end 'apply'

function main() returns ExitCode
	var offset = 10 as Score
	let addOffset = function(n Score) gives n + offset
	offset = 20
	print("{apply(addOffset, x: 5)} {offset}\n")     // 15 20: the closure keeps the offset it captured
	return 0
end 'main'
```

**A closure owns what it captures.** What a capture does depends on what is captured:

| Captured | Effect |
|----------|--------|
| a scalar | copied into the closure; writes on either side do not reach the other |
| a local that holds a record, a `String`, an array or a promise | **moved** into the closure when the closure is created; reading the local afterwards is **E3102** (`its ownership moved to the closure that captures it`) |
| a parameter, `self`, a field or a borrowed value | retained: the closure holds a counted reference to the same record, so a write through the receiver is what the closure reads |
| a name an enclosing closure captured | taken from that closure's captures |

Clone a local before the closure when the function still needs it (`let mine = word.clone()`). A local
captured inside a loop is moved on the first trip, so the second is **E3102**. A capture moves its local
before the call it is an argument of runs, so that call may not also consume the local, take it by
reference or write it as its receiver (**E3102**); a call that only reads it may. Inside a closure, a captured name is the closure's own
copy, so passing it to a parameter the callee writes is **E3159**.

A closure that escapes — stored anywhere or returned — keeps every record it captures, so handing a read-only
value to a function whose parameter such a closure captures is **E3078**. A closure that stays local — it is
only called, where it is made or by a parameter that only calls it — writes what it writes of a parameter it
captures, so handing that function a read-only value is **E3159**. A record [view](/docs/language/variables/#views-view) cannot be
captured (**E3078**); a view of a number can, since the closure captures a copy.

**A closure can go anywhere a value can.** A function value is one reference to a record holding the code
and the captures, so a closure may be returned, stored in a field, a module-level variable, a container or
a union payload, and passed to `async`. A plain function is a record that
lives for the whole program. Copies share: `var g = f` and `f.clone()` hold the same closure, and a function
value is exempt from **E3078**. A module-level `let` or `var` may hold a function value, including a closure
a factory call returns.

**A closure may not be stored into what it captures.** The record would hold the closure and the closure the
record, and neither would ever be released, so storing a closure — directly, or inside a value holding it —
into a record it captures or a record that record reaches is **E3183**. The store is followed through fields,
elements, payloads, tuples, literals, module variables, clones, returned values and the functions a
closure is handed to, in the order the stores run, and each cycle is reported once, at the store that closes
it. Two names for one record that only exist at run time are beyond what the compiler can follow.

- A parameter's type may be omitted when the closure is written directly as a call argument whose parameter
  is declared with a function type: the closure's parameters take that type's parameter types, in order —
  `scores.sortBy(function(a, b) gives b.compare(a))`. A parameter past that function type's arity is **E2003**,
  and an omitted type anywhere else is **E2015**. A member called on an undefined
  function's result accepts an untyped closure argument, and the call's
  [E3004](/docs/cli/error-codes/#e3004--callunknownfunction) is the error reported.
- Closure parameters must be used (**E3012**); write `_` for an unused one.
- Inside an instance method a closure may use `self`; elsewhere `self` is **E2001**.
- Assigning to a captured `let` is an error, as it is outside the closure.
- A captured value answers the same fields, methods and enum or union accessors it answers outside the
  closure.

## Function Purity and Discarded Results

A function's result must be used. The compiler infers whether a function is **pure** (no output, no writes
to globals or parameters, only pure callees) or **impure**, and the rules for ignoring a result differ:

| Callee | Bare call statement | `_ = call()` |
|--------|---------------------|--------------|
| pure function | **E3064** — the call does nothing | **E3064** |
| impure function | **E3065** — result not used | allowed |
| chainable method (returns its own receiver type) | allowed | allowed |

```maxon
typealias Tally = int(0 to u64.max)

var counter = 0 as Tally

function incrementAndGet() returns Tally
	counter = counter + 1
	return counter
end 'incrementAndGet'

function main() returns ExitCode
	_ = incrementAndGet()              // explicitly discarded
	let now = incrementAndGet()        // used
	// incrementAndGet()               // E3065: result of 'incrementAndGet' is not used
	print("{now}\n")                   // 2
	return 0
end 'main'
```

Writes to a value the function created itself — an array it fills, a string it lowers, a record it builds and
returns — leave it pure; only writes to parameters, globals and records it was handed make it impure. A call through a
closure is judged by the closure's own body when the closure is known where the call is made: a closure
literal, or a named function, handed to a helper that only calls it. A closure the call cannot identify (one
read out of a field, say) makes the caller impure. Releasing a value that can own a green thread, a mailbox or an
operating-system handle (a promise, a file, a socket, a type parameter's value) is an effect of the function that
releases it.

A function that returns nothing has no result to discard. Destructuring a pure function's tuple result must
keep at least one element (`(_, _) = pure()` is **E3064**). A throwing call is judged the same way: a pure
one discarded as a bare statement inside a [`try` block](/docs/language/error-handling/#try-blocks) or a
[`test` body](/docs/language/testing/#uncaught-errors-in-tests), where it needs no `try`, is **E3064**.
