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

- The first argument is positional. Labelling it is **E2052** (`the first argument cannot be named`).
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

Parameters with defaults come after the parameters without them.

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

## Function Overloads

Several functions may share a name when their parameters differ.

**By parameter type** — the argument types choose the overload:

```maxon
typealias Tally = int(0 to u64.max)

function measure(value Tally) returns Tally
	return value * 2
end 'measure'

function measure(value String) returns Tally
	return value.count()
end 'measure'

function main() returns ExitCode
	print("{measure(21)} {measure("hello")}\n")    // 42 5
	return 0
end 'main'
```

**By parameter name** — overloads whose later parameters have different names are chosen by the labels
at the call:

```maxon
typealias Integer = int(i64.min to i64.max)

function slice(start Integer, endIndex Integer) returns Integer
	return endIndex - start
end 'slice'

function slice(start Integer, length Integer) returns Integer
	return start + length
end 'slice'

function main() returns ExitCode
	print("{slice(10, endIndex: 32)} {slice(10, length: 32)}\n")    // 22 42
	return 0
end 'main'
```

- A call that more than one overload matches is **E3007** (`Ambiguous overload`). Because the first argument
  is never labelled, two single-parameter overloads of the same type cannot be told apart.
- Declaring the same overload twice, with the same parameter names and types, is a duplicate
  definition (**E3006**). Overloads with the same parameter types but different parameter names are
  separate declarations; a call whose labels cannot tell them apart is **E3007**.
- A type may declare a `static` method and an instance method with the same name and parameters:
  `Type.name()` calls the static one and `value.name()` the instance one.

## Parameter Passing

A parameter the function only reads is passed by value. A parameter the function **assigns to** — directly,
or through one of its fields or elements — is passed by reference, so the write reaches the caller's
variable:

```maxon
typealias Tally = int(0 to u64.max)

function increment(n Tally)
	n = n + 1
end 'increment'

function main() returns ExitCode
	var x = 10
	increment(x)
	print("{x}\n")     // 11
	return 0
end 'main'
```

- Passing a `var` lets the callee's writes propagate. Any storage you could assign to at that point may be
  passed and is written in place: a local `var`, a module-level `var`, a field of a binding (`p.x`), a field
  of the receiver (`count` or `self.count`), and a chain of them (`p.a.b`). The rule is exactly the
  assignment rule — an argument the callee writes is accepted here if and only if writing the same thing at
  the call site would be accepted.
- Passing a `let` to a parameter the callee writes is **E3019** (`cannot pass 'y' to function that mutates
  parameter 'n'`). A method writing a field of its **own** receiver is not a parameter write, so
  `let acc = Accumulator.create()` followed by `acc.add(10)` is legal. An immutable field, or a field of an
  immutable instance, is refused for the same reason the equivalent assignment is.
- Passing a collection to a parameter the callee **reassigns** while a reference into that collection is
  still live is **E3070**: the reassignment frees what the reference points into.
- Passing a literal or another expression gives the callee a temporary; its writes have no visible effect.

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

Omit `returns` for a function type that returns nothing: `typealias Callback = function()`. A function type cannot express `throws`, so a
throwing function cannot be used as a value (**E3101**) — wrap it in a function that handles the error.
Function-type aliases are [brands](/docs/language/ranged-typealiases/#generic-instance-and-function-type-aliases-are-brands).

## Closures

A closure is an anonymous function written `function(parameters) gives expression`:

```maxon
typealias Score = int(i64.min to i64.max)
typealias UnaryOp = function(Score) returns Score

function apply(f UnaryOp, x Score) returns Score
	return f(x)
end 'apply'

function main() returns ExitCode
	var offset = 10
	let addOffset = function(n Score) gives n + offset
	offset = 20
	print("{apply(addOffset, x: 5)}\n")     // 25: the closure sees the current offset
	return 0
end 'main'
```

- **Captures are by reference.** A closure reads a captured variable's current value when it runs.
- **A closure that captures cannot outlive its frame.** Returning one, or storing it in a field, a
  global, a container or a union payload, is **E3099**. Passing it down to a function that calls it is fine.
  A closure that captures nothing is a plain function reference and can go anywhere.
- A parameter's type may be omitted when the closure is written directly as a call argument whose parameter
  is declared with a function type: the closure's parameters take that type's parameter types, in order —
  `scores.sort(function(a, b) gives b.compare(a))`. A parameter past that function type's arity is **E2003**,
  and an omitted type anywhere else is **E2015**. When overloads of the callee declare different function
  types at that argument, none is offered.
- Closure parameters must be used (**E3012**); write `_` for an unused one.
- Inside an instance method a closure may use `self`; elsewhere `self` is **E2001**.
- Assigning to a captured `let` is an error, as it is outside the closure.

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

var counter = 0

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

A function that returns nothing has no result to discard. Destructuring a pure function's tuple result must
keep at least one element (`(_, _) = pure()` is **E3064**). A throwing call is judged the same way: a pure
one discarded as a bare statement inside a [`try` block](/docs/language/error-handling/#try-blocks) or a
[`test` body](/docs/language/testing/#uncaught-errors-in-tests), where it needs no `try`, is **E3064**.
