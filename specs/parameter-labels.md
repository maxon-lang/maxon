---
feature: parameter-labels
status: stable
keywords: [parameters, named-arguments, arguments, call-site, default-values]
category: core
---

# Named Arguments

## Documentation

In function and method calls, the first argument is positional and every subsequent argument must be named using `name: value` syntax. The first argument carries a label only when the callee's first parameter has a default (see `default-values`); labels on the remaining arguments improve clarity at the call site by making each parameter's role explicit.

### First Argument Positional, Rest Named

The first argument is passed positionally. Every argument after the first must be named:

```maxon
typealias Score = int(i64.min to i64.max)

function add(a Score, b Score) returns Score
	return a + b
end 'add'

function main() returns ExitCode
	return add(3, b: 4)
end 'main'
```
```exitcode
7
```


### Named Arguments in Any Order

After the first (positional) argument, named arguments can appear in any order:

```maxon
typealias Score = int(i64.min to i64.max)

function subtract(a Score, b Score) returns Score
	return a - b
end 'subtract'

function main() returns ExitCode
	return subtract(10, b: 3)
end 'main'
```
```exitcode
7
```


### Default Parameter Values

Parameters with default values can be omitted. A required first parameter is passed positionally:

```maxon
typealias Score = int(i64.min to i64.max)

function repeat(value Score, times Score = 1) returns Score
	return value * times
end 'repeat'

function main() returns ExitCode
	return repeat(7, times: 6)
end 'main'
```
```exitcode
42
```

### the compiler note on the four expected-error blocks

The four `error-*` cases below carry the compiler's own DEDICATED codes rather than generic ones.
`ErrorCodeRegistry.maxon` defines **E2053** (`callArgMissingLabel`) and **E3037**
(`callUnknownArgLabel`) for exactly these two rules and claims them for the compiler alone. The compiler also
positions the missing-label error at the OFFENDING ARGUMENT rather than at the call, which is what
`consumeArgLabel`'s per-argument anchor buys. The two E2052 cases keep their registered code and
differ only in wording.

## Tests

<!-- test: named-args -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(3, b: 4)
end 'main'
```
```exitcode
7
```

<!-- test: named-args-multiply -->
```maxon

typealias Integer = int(i64.min to i64.max)

function multiply(x Integer, y Integer) returns Integer
	return x * y
end 'multiply'

function main() returns ExitCode
	return multiply(6, y: 7)
end 'main'
```
```exitcode
42
```

<!-- test: default-param-named -->
```maxon

typealias Integer = int(i64.min to i64.max)

function repeat(value Integer, times Integer = 1) returns Integer
	return value * times
end 'repeat'

function main() returns ExitCode
	return repeat(7, times: 6)
end 'main'
```
```exitcode
42
```

<!-- test: default-param-omitted -->
```maxon

typealias Integer = int(i64.min to i64.max)

function repeat(value Integer, times Integer = 2) returns Integer
	return value * times
end 'repeat'

function main() returns ExitCode
	return repeat(21)
end 'main'
```
```exitcode
42
```

<!-- test: error-missing-param-name -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(3, 4)
end 'main'
```
```maxoncstderr
error E2053: <fragment>:10:16: the second and later arguments must be named ('name: value')
```

<!-- test: error-unknown-param-name -->
```maxon

typealias Integer = int(i64.min to i64.max)

function greet(name Integer, suffix Integer) returns Integer
	return name + suffix
end 'greet'

function main() returns ExitCode
	return greet(42, person: 1)
end 'main'
```
```maxoncstderr
error E3037: <fragment>:10:19: 'greet' has no parameter named 'person'
```

<!-- test: error-first-arg-named -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(a: 3, b: 4)
end 'main'
```
```maxoncstderr
error E2052: <fragment>:10:13: the first argument cannot be named because its parameter has no default
```

<!-- test: error-method-first-arg-named -->
```maxon

typealias Integer = int(i64.min to i64.max)

// A CONCRETE-STRUCT method call rejects a named first arg whose parameter
// (the first after the receiver) has no default, exactly like a free-function
// call. The parser raises E2052 once the argument list is parsed, against the
// method's declaration.
// Interface / type-parameter receivers are exempt (see interface-dispatch/dispatch-named-first-arg).
type Pair
	let a as Integer
	let b as Integer

	function combine(first Integer, second Integer) returns Integer
		return a + b + first + second
	end 'combine'

	static function create(a Integer, b Integer) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create(1, b: 2)
	return p.combine(first: 3, second: 4)
end 'main'
```
```maxoncstderr
error E2052: <fragment>:25:19: the first argument cannot be named because its parameter has no default
```


<!-- test: named-args-out-of-order -->
Named arguments may be supplied in any order; the call binds each label to its
declared parameter regardless of source position. The compiler must type-check
each argument against the parameter its LABEL names, not the parameter at the
argument's source position — so `pick(1, c: true, b: "x")` checks `c` against
the `bool` param and `b` against the `String` param even though they appear in
the opposite declared order.
```maxon
typealias Num = int(0 to 100)

function pick(a Num, b String, c bool) returns Num
	if c 'isTrue'
		return a
	end 'isTrue'
	return b.byteLength() as Num
end 'pick'

function main() returns ExitCode
	return pick(1, c: true, b: "longer")
end 'main'
```
```exitcode
1
```


<!-- test: named-args-out-of-order-second -->
Same call with `c: false` selects the other branch, returning the byte length
of the String argument — confirming the out-of-order labels bound to the right
parameters at runtime, not just at the type level.
```maxon
typealias Num = int(0 to 100)

function pick(a Num, b String, c bool) returns Num
	if c 'isTrue'
		return a
	end 'isTrue'
	return b.byteLength() as Num
end 'pick'

function main() returns ExitCode
	return pick(1, c: false, b: "abcde")
end 'main'
```
```exitcode
5
```


<!-- test: multi-line-call-empty-list-free-call -->
A call's argument list may span lines: line breaks, blank lines and comment-only lines between the parentheses are layout. An empty list split over two lines is an empty list.
```maxon
typealias Integer = int(i64.min to i64.max)

function ping() returns Integer
	print("ping\n")
	return 3
end 'ping'

function main() returns ExitCode
	let r = ping(
	)
	return r as ExitCode
end 'main'
```
```exitcode
3
```
```stdout
ping
```

<!-- test: multi-line-call-empty-list-blank-lines -->
```maxon
typealias Integer = int(i64.min to i64.max)

function ping() returns Integer
	print("ping\n")
	return 4
end 'ping'

function main() returns ExitCode
	let r = ping(


	)
	return r as ExitCode
end 'main'
```
```exitcode
4
```
```stdout
ping
```

<!-- test: multi-line-call-empty-list-comment-line -->
```maxon
typealias Integer = int(i64.min to i64.max)

function ping() returns Integer
	print("ping\n")
	return 5
end 'ping'

function main() returns ExitCode
	let r = ping(
		// nothing to pass
	)
	return r as ExitCode
end 'main'
```
```exitcode
5
```
```stdout
ping
```

<!-- test: multi-line-call-empty-list-doc-comment-line -->
```maxon
typealias Integer = int(i64.min to i64.max)

function ping() returns Integer
	print("ping\n")
	return 6
end 'ping'

function main() returns ExitCode
	let r = ping(
		/// nothing to pass
	)
	return r as ExitCode
end 'main'
```
```exitcode
6
```
```stdout
ping
```

<!-- test: multi-line-call-empty-list-method-call -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	var n as Integer

	static function create() returns Self
		return Self{n: 40}
	end 'create'

	function bump() returns Integer
		print("bump\n")
		return self.n + 1
	end 'bump'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create()
	let r = c.bump(
	)
	return r as ExitCode
end 'main'
```
```exitcode
41
```
```stdout
bump
```

<!-- test: multi-line-call-empty-list-static-call-statement -->
```maxon
type Adder
	static function bump()
		print("bumped\n")
	end 'bump'
end 'Adder'

function main() returns ExitCode
	Adder.bump(
	)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
bumped
```

<!-- test: multi-line-call-empty-list-create -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	var n as Integer

	static function create() returns Self
		print("created\n")
		return Self{n: 9}
	end 'create'

	function value() returns Integer
		return self.n
	end 'value'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(
	)
	return c.value() as ExitCode
end 'main'
```
```exitcode
9
```
```stdout
created
```

<!-- test: multi-line-call-empty-list-generic-static-call -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntCell = Cell with Integer

type Cell uses U
	var slot as U

	static function width() returns Integer
		print("width\n")
		return 8
	end 'width'
end 'Cell'

function main() returns ExitCode
	let w = IntCell.width(
	)
	return w as ExitCode
end 'main'
```
```exitcode
8
```
```stdout
width
```

<!-- test: multi-line-call-empty-list-container-create -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var xs = IntArray.create(
	)
	xs.push(5)
	return xs.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: multi-line-call-empty-list-builtin-method -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var xs = IntArray.create()
	xs.push(5)
	xs.push(6)
	let n = xs.count(
	)
	print("n={n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=2
```

<!-- test: multi-line-call-builtin-method-with-argument -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var xs = IntArray.create()
	xs.push(
		7
	)
	xs.push(8
	)
	xs.push(
		9)
	let first = try xs.get(
		0
	) otherwise 0
	let last = try xs.get(
		2
	) otherwise 0
	print("{first} {last} {xs.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 9 3
```

<!-- test: multi-line-call-union-payload -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Reply
	ok(value Integer, extra Integer)
	none
end 'Reply'

function main() returns ExitCode
	let r = Reply.ok(
		5,
		extra: 6
	)
	match r 'show'
		ok(value, extra) then print("ok {value} {extra}\n")
		none then print("none\n")
	end 'show'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ok 5 6
```

<!-- test: multi-line-call-function-value -->
```maxon
typealias Integer = int(i64.min to i64.max)

function pair(a Integer, b Integer) returns Integer
	print("a={a} b={b}\n")
	return a * 10 + b
end 'pair'

function main() returns ExitCode
	let combine = pair
	let r = combine(
		4,
		2
	)
	return r as ExitCode
end 'main'
```
```exitcode
42
```
```stdout
a=4 b=2
```

<!-- test: multi-line-call-async-call -->
```maxon
typealias Integer = int(i64.min to i64.max)

function work(a Integer, b Integer) returns Integer
	Scheduler.yield()
	return a * 10 + b
end 'work'

function main() returns ExitCode
	let p = async work(
		2,
		b: 1
	)
	let r = await p
	return r as ExitCode
end 'main'
```
```exitcode
21
```

<!-- test: multi-line-call-service-send -->
```maxon
// --- file: sub/calc.maxon
export type Calc
	var count as Integer

	export static function create() returns Self
		return Self{count: 7}
	end 'create'

	export function add(n Integer, extra Integer) returns Integer
		return self.count + n * 10 + extra
	end 'add'
end 'Calc'

export typealias Integer = int(i64.min to i64.max)
// --- file: main.maxon
function main() returns ExitCode
	let h = spawn sub.Calc.create(
	)
	let total = try await h.add(
		1,
		extra: 2
	) otherwise 0
	return total as ExitCode
end 'main'
```
```exitcode
19
```

<!-- test: multi-line-call-arguments-each-on-a-line -->
```maxon
typealias Integer = int(i64.min to i64.max)

function pair(a Integer, b Integer) returns Integer
	print("a={a} b={b}\n")
	return a * 10 + b
end 'pair'

function main() returns ExitCode
	let r = pair(
		1,
		b: 2
	)
	return r as ExitCode
end 'main'
```
```exitcode
12
```
```stdout
a=1 b=2
```

<!-- test: multi-line-call-newline-after-a-comma -->
```maxon
typealias Integer = int(i64.min to i64.max)

function pair(a Integer, b Integer) returns Integer
	print("a={a} b={b}\n")
	return a * 10 + b
end 'pair'

function main() returns ExitCode
	let r = pair(3,
		b: 4)
	return r as ExitCode
end 'main'
```
```exitcode
34
```
```stdout
a=3 b=4
```

<!-- test: multi-line-call-newline-before-the-close -->
```maxon
typealias Integer = int(i64.min to i64.max)

function pair(a Integer, b Integer) returns Integer
	print("a={a} b={b}\n")
	return a * 10 + b
end 'pair'

function main() returns ExitCode
	let r = pair(5, b: 6
	)
	return r as ExitCode
end 'main'
```
```exitcode
56
```
```stdout
a=5 b=6
```

<!-- test: multi-line-call-comma-leads-the-next-line -->
```maxon
typealias Integer = int(i64.min to i64.max)

function pair(a Integer, b Integer) returns Integer
	print("a={a} b={b}\n")
	return a * 10 + b
end 'pair'

function main() returns ExitCode
	let r = pair(7
		, b: 8
	)
	return r as ExitCode
end 'main'
```
```exitcode
78
```
```stdout
a=7 b=8
```

<!-- test: multi-line-call-nested -->
```maxon
typealias Integer = int(i64.min to i64.max)

function pair(a Integer, b Integer) returns Integer
	print("a={a} b={b}\n")
	return a * 10 + b
end 'pair'

function main() returns ExitCode
	let r = pair(
		pair(
			1,
			b: 2
		),
		b: 3
	)
	return r as ExitCode
end 'main'
```
```exitcode
123
```
```stdout
a=1 b=2
a=12 b=3
```

<!-- test: multi-line-call-as-a-statement -->
```maxon
typealias Integer = int(i64.min to i64.max)

function show(a Integer, b Integer)
	print("a={a} b={b}\n")
end 'show'

function main() returns ExitCode
	show(
		5,
		b: 6
	)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=5 b=6
```

<!-- test: multi-line-call-in-an-if-condition -->
```maxon
typealias Integer = int(i64.min to i64.max)

function pair(a Integer, b Integer) returns Integer
	return a * 10 + b
end 'pair'

function main() returns ExitCode
	if pair(
		1,
		b: 2
	) == 12 'hit'
		print("hit\n")
	end 'hit' else 'miss'
		print("miss\n")
	end 'miss'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hit
```

<!-- test: multi-line-call-in-a-return -->
```maxon
typealias Integer = int(i64.min to i64.max)

function pair(a Integer, b Integer) returns Integer
	return a * 10 + b
end 'pair'

function main() returns ExitCode
	return pair(
		0,
		b: 7
	) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: multi-line-call-moves-a-managed-argument -->
A managed argument on a line of its own is moved into the container exactly as on one line, and the run is leak-free.
```maxon
typealias NameArray = Array with String

function main() returns ExitCode
	var names = NameArray.create()
	var name = "ada"
	names.push(
		name
	)
	print("{names.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: multi-line-call-match-expression-argument -->
A `match` expression written over several lines inside a split argument list keeps its own line structure.
```maxon
function main() returns ExitCode
	let n = 2
	print(
		match n 'pick'
			1 gives "one\n"
			default gives "many\n"
		end 'pick'
	)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
many
```

<!-- test: multi-line-call-after-an-inactive-branch-with-an-unclosed-call -->
An unclosed call in an inactive `#if` branch does not leak into the live code after it.
```maxon
typealias Integer = int(i64.min to i64.max)

function pair(a Integer, b Integer) returns Integer
	return a * 10 + b
end 'pair'

#if testing(true)
function dead() returns Integer
	return pair(1,
#endif

function main() returns ExitCode
	let r = pair(
		1,
		b: 3
	)
	return r as ExitCode
end 'main'
```
```exitcode
13
```

<!-- test: multi-line-parenthesized-expression-split-over-lines -->
A parenthesized expression is layout-insensitive like a call's argument list: a line break inside it does not end the statement.
```maxon
function main() returns ExitCode
	let x = (1 +
		2)
	return x
end 'main'
```
```exitcode
3
```

<!-- test: multi-line-call-handler-block-argument-holding-nested-blocks -->
A labelled `otherwise` handler written as an argument keeps its own line structure through every block nested inside it: an `if`, a `for`, a `while`, a `match` statement and a `try` block each end at their own `end`, and the statements after them stay separate statements.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function pair(a Integer, b Integer) returns Integer
	return a * 10 + b
end 'pair'

enum Mode
	quiet
	loud
end 'Mode'

function main() returns ExitCode
	var xs = IntArray.create()
	xs.push(4)
	let mode = Mode.loud
	let r = pair(try xs.get(5) otherwise 'missing'
		if xs.count() > 0 'some'
			print("some\n")
		end 'some' else 'none'
			print("none\n")
		end 'none'
		for x in xs 'each'
			print("x={x}\n")
		end 'each'
		var n = 0 as Integer
		while n < 2 'spin'
			n = n + 1
		end 'spin'
		match mode 'say'
			quiet then print("quiet\n")
			loud then print("loud\n")
		end 'say'
		try 'probe'
			print("{xs.get(0)}\n")
		end 'probe' otherwise (e) 'failed'
			match e 'why'
				indexOutOfBounds or
				emptySlot then print("failed\n")
			end 'why'
		end 'failed'
		print("n={n}\n")
		return 3
	end 'missing', b: 1)
	return r as ExitCode
end 'main'
```
```exitcode
3
```
```stdout
some
x=4
loud
4
n=2
```

<!-- test: multi-line-call-match-argument-with-keyword-named-cases -->
A `match` expression written as an argument keeps its own line structure when its arms are cases named by block keywords: `end(m) gives` and `if gives` are arms, not the match's end or a new block.
```maxon
typealias Num = int(0 to 1000)

union Kw
	alpha(n Num)
	end(m Num)
	while(m Num)
	if
	omega
end 'Kw'

function show(text String, tail String)
	print("{text}{tail}")
end 'show'

function main() returns ExitCode
	let k = Kw.end(2)
	show(match k 'm'
		alpha(n) gives "alpha {n}"
		end(m) gives "end {m}"
		while(m) gives "while {m}"
		if gives "if"
		omega gives "omega"
	end 'm',
		tail: "\n"
	)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
end 2
```

<!-- test: multi-line-call-panic -->
`panic(…)` is a call, and its argument list may span lines like any other.
```maxon
function main() returns ExitCode
	if true 'stop'
		panic(
			"stopped"
		)
	end 'stop'
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at multi-line-call-panic.test:4: stopped
Stack trace:
  in main
  in mrt_start
```

<!-- test: multi-line-call-keyword-named-function -->
A call to a function whose name is spelled by a keyword takes a split argument list like any other call.
```maxon
typealias Integer = int(i64.min to i64.max)

function from(n Integer) returns Integer
	return n + 1
end 'from'

function main() returns ExitCode
	return from(
		41
	)
end 'main'
```
```exitcode
42
```

<!-- test: multi-line-call-sizeof -->
`sizeof(…)` takes a split argument list like a call.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let n = sizeof(
		Integer
	)
	return n as ExitCode
end 'main'
```
```exitcode
8
```
