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

A function is **pure** if it has no side effects: it doesn't write to stdout/stderr, doesn't modify global state, doesn't mutate parameters, and only calls other pure functions. Pure function results **must** be used — they cannot be discarded, even with `let _ =`.

```text
function double(x int(i64.min to i64.max)) returns int(i64.min to i64.max)
  return x * 2
end 'double'

// Error: result of pure function 'double' must be used
double(5)

// Error: result of pure function 'double' must be used
let _ = double(5)

// OK: result is used
let result = double(5)
```

### Impure Functions

A function is **impure** if it has side effects (e.g., prints output, modifies global state, mutates parameters). Impure function results **must** be assigned, but can be explicitly discarded with `let _ =`:

```text
// OK: result is used
let count = processAndCount(data)

// OK: explicitly discarded
let _ = processAndCount(data)

// Error: result is not used
processAndCount(data)
```

### Chainable Functions (Methods Returning Own Type)

Methods that return their own type (e.g., builder pattern) are chainable — their results may be freely discarded:

```text
type Counter
  var value int(0 to i64.max)

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
var (_, _) = pureFunc()
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
error E3064: specs/fragments/discarded-results/pure-function-discarded.test:10:3: result of pure function 'double' must be used
```

<!-- test: pure-function-let-discard -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
  return x * 2
end 'double'

function main() returns ExitCode
  let _ = double(5)
  return 0
end 'main'
```
```maxoncstderr
error E3064: specs/fragments/discarded-results/pure-function-let-discard.test:10:7: result of pure function 'double' must be used
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
error E3065: specs/fragments/discarded-results/impure-function-discarded.test:13:3: result of 'incrementAndGet' is not used (assign to '_' to discard)
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
  let _ = incrementAndGet()
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
  export var value Count

  function increment() returns Counter
    value = value + 1
    return self
  end 'increment'
end 'Counter'

function main() returns ExitCode
  var c = Counter{value: 0}
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
  let _ = computeAndPrint(5)
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
  let _ = doubleInPlace(n)
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: underscore-not-prefix-suppression -->
```maxon

function main() returns ExitCode
  var _x = 42
  return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/discarded-results/underscore-not-prefix-suppression.test:4:7: unused variable: '_x'
```

<!-- test: underscore-exact-discard -->
```maxon

function main() returns ExitCode
  let _ = 42
  return 0
end 'main'
```
```maxoncstderr
error E3067: specs/fragments/discarded-results/underscore-exact-discard.test:4:7: discarding a non-call expression has no effect
```

<!-- test: tuple-partial-discard -->
```maxon

typealias Small = int(0 to 100)

function makePair() returns (Small, Small)
  return (10, 20)
end 'makePair'

function main() returns ExitCode
  var (a, _) = makePair()
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
  var (_, _) = makePair()
  return 0
end 'main'
```
```maxoncstderr
error E3064: specs/fragments/discarded-results/tuple-all-discard-pure.test:10:7: result of pure function 'makePair' must be used
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
error E3065: specs/fragments/discarded-results/transitive-impure.test:15:3: result of 'computeAndPrint' is not used (assign to '_' to discard)
```

<!-- test: try-pure-let-discard -->
```maxon

typealias Integer = int(i64.min to i64.max)

union ParseError implements Error
  invalidFormat
end 'ParseError'

function parseNum(s String) returns Integer throws ParseError
  if s.byteLength() == 0 'empty'
    throw ParseError.invalidFormat
  end 'empty'
  return s.byteLength()
end 'parseNum'

function main() returns ExitCode
  let _ = try parseNum("abc") otherwise 0
  return 0
end 'main'
```
```maxoncstderr
error E3064: specs/fragments/discarded-results/try-pure-let-discard.test:17:7: result of pure function 'parseNum' must be used
```

<!-- test: try-impure-let-discard -->
```maxon

typealias Integer = int(i64.min to i64.max)

var counter = 0 as Integer

union ParseError implements Error
  invalidFormat
end 'ParseError'

function parseNum(s String) returns Integer throws ParseError
  counter = counter + s.byteLength()
  throw ParseError.invalidFormat
end 'parseNum'

function main() returns ExitCode
  let _ = try parseNum("abc") otherwise 0
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

union MyError implements Error
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
