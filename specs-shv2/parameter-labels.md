---
feature: parameter-labels
status: stable
keywords: [parameters, named-arguments, arguments, call-site, default-values]
category: core
---

# Named Arguments

## Documentation

In function and method calls, the first argument is positional and every subsequent argument must be named using `name: value` syntax. The first argument carries no label; labels on the remaining arguments improve clarity at the call site by making each parameter's role explicit.

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

Parameters with default values can be omitted. The first parameter is still passed positionally:

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

### shv2 note on the four expected-error blocks

The four `error-*` cases below carry shv2's own DEDICATED codes rather than the generic ones the C#
bootstrap falls back to. This is a ratified divergence, not a drift: `docs/error-codes.txt` defines
**E2053** (`callArgMissingLabel`) and **E3037** (`callUnknownArgLabel`) for exactly these two rules and
claims them for shv2 alone, while the bootstrap reports the first through its general type-mismatch
E3005 and the second through its general E3003. shv2 also positions the missing-label error at the
OFFENDING ARGUMENT rather than at the call, which is what `consumeArgLabel`'s per-argument anchor buys.
The two E2052 cases keep the bootstrap's code and differ only in wording.

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
error E2052: <fragment>:10:13: the first argument cannot be named; only the second and later arguments take 'name:' labels
```

<!-- test: error-method-first-arg-named -->
```maxon

typealias Integer = int(i64.min to i64.max)

// A CONCRETE-STRUCT method call rejects a named first arg, exactly like a
// free-function call. The parser defers E2052 for all method calls (the
// receiver type isn't known at parse time), and TypeResolution re-applies it
// once the receiver resolves to a concrete struct. Interface / type-parameter
// receivers are exempt (see interface-dispatch/dispatch-named-first-arg).
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
error E2052: <fragment>:25:19: the first argument cannot be named; only the second and later arguments take 'name:' labels
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

