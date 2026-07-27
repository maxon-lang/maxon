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

<!-- disabled-test: pure-function-discarded -->
<!-- purity analysis (E3064) - shv2 has no purity classification -->
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

<!-- disabled-test: pure-function-let-discard -->
<!-- purity analysis (E3064) + the `_ =` discard binding -->
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

<!-- disabled-test: impure-function-discarded -->
<!-- top-level `var` globals + purity analysis (E3065) -->
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

<!-- disabled-test: impure-function-let-discard -->
<!-- top-level `var` globals + the `_ =` discard binding -->
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

<!-- disabled-test: chainable-method-discarded -->
<!-- P1.1 structs (methods) -->
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

<!-- disabled-test: impure-print-discarded -->
<!-- P1.2 String + the `print` builtin -->
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

<!-- disabled-test: impure-mutating-param -->
<!-- P1.9 `as` cast + the `_ =` discard binding -->
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

<!-- disabled-test: underscore-not-prefix-suppression -->
<!-- E3012 unused-variable check -->
```maxon

function main() returns ExitCode
	let x = 42
	return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/discarded-results/underscore-not-prefix-suppression.test:4:6: unused variable: 'x'
```

<!-- disabled-test: underscore-exact-discard -->
<!-- E3067 + the `_ =` discard binding -->
```maxon

function main() returns ExitCode
	_ = 42
	return 0
end 'main'
```
```maxoncstderr
error E3067: specs/fragments/discarded-results/underscore-exact-discard.test:4:2: expected a function call
```

<!-- disabled-test: tuple-partial-discard -->
<!-- tuple types and destructuring -->
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

<!-- disabled-test: tuple-all-discard-pure -->
<!-- tuple types and destructuring -->
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

<!-- disabled-test: transitive-impure -->
<!-- purity analysis (E3065) -->
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

<!-- disabled-test: try-pure-let-discard -->
<!-- P1.4 errors (`throws`/`try`/`otherwise`) + P1.2 String -->
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

<!-- disabled-test: try-impure-let-discard -->
<!-- P1.4 errors + top-level `var` globals -->
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
