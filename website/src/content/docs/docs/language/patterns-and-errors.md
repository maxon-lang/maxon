---
title: Patterns & Common Errors
description: Common code patterns, compile-time and runtime error examples, and best practices for writing Maxon.
sidebar:
  order: 16
---

## Common Patterns

### Program Template

```maxon
function main() returns ExitCode
	print("Hello, world!\n")
	return 0
end 'main'
```

### Loop With an Early Exit

```maxon
function main() returns ExitCode
	var i = 0
	while true 'forever'
		if i >= 3 'done'
			break
		end 'done'

		print("{i}\n")
		i = i + 1
	end 'forever'

	return 0
end 'main'
```

### Iterating With an Index

```maxon
function main() returns ExitCode
	let names = ["ada", "alan", "grace"]
	for (iter, name) in names.withIterator() 'each'
		print("{iter.index()}: {name}\n")
	end 'each'

	return 0
end 'main'
```

### Recursion

```maxon
typealias Operand = int(i64.min to i64.max)

function factorial(n Operand) returns Operand
	if n <= 1 'base'
		return 1
	end 'base'

	return n * factorial(n - 1)
end 'factorial'

function main() returns ExitCode
	print("{factorial(5)}\n")     // 120
	return 0
end 'main'
```

### Building a String

`String` has no `+`; interpolate, or `append` in place:

```maxon
function main() returns ExitCode
	var csv = ""
	for n in 1 to 3 'each'
		csv.append("{n},")
	end 'each'

	print("{csv}\n")     // 1,2,3,
	return 0
end 'main'
```

### A Lookup With a Fallback

```maxon
typealias Age = int(0 to 150)
typealias Ages = Map with (String, Age)

function main() returns ExitCode
	var ages = Ages.create()
	ages.upsert("ada", value: 36)
	let known = try ages.get("ada") otherwise 0
	let unknown = try ages.get("bob") otherwise 0
	print("{known} {unknown}\n")     // 36 0
	return 0
end 'main'
```

### A Factory That Validates

```maxon
typealias Percent = int(0 to 100)

enum PercentError implements Error
	outOfRange
end 'PercentError'

type Progress
	export let done as Percent

	static function create(done Percent) returns Self
		return Self{done: done}
	end 'create'

	static function parse(text String) returns Self throws PercentError
		let value = try int.fromString(text) otherwise throw PercentError.outOfRange
		if value < 0 or value > 100 'range'
			throw PercentError.outOfRange
		end 'range'

		return Self{done: value as Percent}
	end 'parse'
end 'Progress'

function main() returns ExitCode
	let p = try Progress.parse("42") otherwise Progress.create(0)
	print("{p.done}%\n")     // 42%
	return 0
end 'main'
```

## Common Errors

### Compile-Time Errors

Each example below is refused with the diagnostic shown.

**Type mismatch**

```maxon
let x = 5 + "string"          // E2004: Cannot operate on int and String
```

**Missing return**

```maxon
function compute() returns Tally
	print("working\n")
end 'compute'                 // E3013: missing return statement: 'compute'
```

**Assigning to a `let`**

```maxon
let x = 5
x = 10                        // E2013: cannot assign to immutable variable: 'x'
```

**Self-assignment**

```maxon
x = x                         // E3067: self-assignment has no effect: 'x = x'
```

**A `var` that never changes**

```maxon
var x = 10
return x                      // E3077: variable 'x' is never reassigned; use 'let' instead of 'var'
```

**An unused variable**

```maxon
let unused = 3                // E3012: unused variable: 'unused'
```

**Discarding a result**

```maxon
double(5)                     // E3064: result of pure function 'double' must be used
incrementAndGet()             // E3065: result of 'incrementAndGet' is not used (use '_ = expr' to discard)
_ = 42                        // E3067: expected a function call
```

**A missing `try`**

```maxon
parseDigit("7")               // E3057: throwing function requires try: 'parseDigit'
```

**Mutating through an immutable name**

```maxon
let items = TallyArray.create()
items.push(1)                 // E3019: cannot pass 'items' to function that mutates parameter 'self'
```

**Moving out of a `let`**

```maxon
let a = Point.create(1)
var b = a
b.x = 2
print("{a.x}\n")              // E3102: use of moved value 'a'
```

**Sharing a record between a `var` and a live `let`**

```maxon
let a = Point.create(1)
let b = a
var c = a                     // E3078: cannot assign immutable variable 'a' to mutable binding 'c'; use 'let' instead of 'var', or use clone()
```

**Mutating a borrowed collection**

```maxon
var arr = ["hello"]
let s = try arr.get(0) otherwise ""
arr.push("world")             // E3070: cannot mutate 'arr' via 'push' while it is borrowed by 's'
print("{s}\n")
```

**A closure escaping its frame**

```maxon
function makeAdder(bump Score) returns UnaryOp
	let f = function(n Score) gives n + bump
	return f                  // E3099: cannot return a closure that captures
end 'makeAdder'
```

**A bare primitive type**

```maxon
function half(n int) returns int     // E3005: Cannot use bare 'int' as a type. Define a typealias with range constraints
```

**Mixing typealiases**

```maxon
let bad = score + meters      // E3005: operator '+' requires both operands to be the same type: 'Score' and 'Meters' are different typealiases — cast one side with 'as'
```

**A mismatched block label**

```maxon
match x 'check'
	1 then print("one\n")
	default then print("other\n")
end 'wrong'                   // E2043: block identifier mismatch: expected 'check', got 'wrong'
```

**An empty block**

```maxon
if x > 0 'check'
end 'check'                   // E3082: empty block: 'check'
```

A block holding only a comment is empty too.

**A non-exhaustive match**

```maxon
match level 'filter'
	error then print("error!\n")
end 'filter'                  // E2026: match on enum 'Level' is not exhaustive, missing: trace, info
```

**A redundant loop label**

```maxon
while i < 3 'loop'
	break 'loop'              // E2048: 'break' with label 'loop' targets its own loop
end 'loop'
```

### Run-Time Behavior

Nothing in Maxon is undefined behaviour. At run time:

| Event | Behavior |
|-------|----------|
| `panic("…")`, a failed range check, a negative shift count | the program prints `panic at <file>:<line>: <message>` and a stack trace to stderr and exits with code **1** |
| an index past the end of a collection | `get`/`set` throw `ArrayError.indexOutOfBounds`, handled with `try` |
| division or `mod` by zero | throws `DivisionByZero`, handled with `try` |
| integer overflow | wraps around (two's complement), with no error |
| an allocation never released | exit code **101** |
| a green thread neither awaited nor dropped | exit code **75** |
| a promise consumed through a second read of one container slot or struct field | exit code **118** |
| `__Builtins.slabCensusTally` asked for a mode it does not implement, or walking a heap it cannot describe | exit code **119** |
| a deep copy of an interface-typed field whose conformer cannot be duplicated — reachable only if a `.clone()` the front end should have refused was compiled | exit code **120** |
| deadlock | exit code **92** |

`maxon execute` and `maxon test` report these exit codes; see the [CLI reference](/docs/cli/).

## Best Practices for AI Agents

These rules cover the mistakes code generators make most often when writing Maxon.

1. **Label every block and repeat the label on `end`.** `if x > 0 'positive'` … `end 'positive'`. Choose
   labels that say what the block does.

2. **Never use bare `int` or `float` in a declaration.** Declare a typealias named for the purpose and use
   it for parameters, returns, fields and type arguments:

   ```maxon
   typealias Tally = int(0 to u64.max)
   typealias TallyArray = Array with Tally
   ```

3. **Pass the first argument positionally and name the rest.** `connect("localhost", port: 8080)`. Naming
   the first argument is an error.

4. **Construct records through a static factory.** `Point{x: 1}` is legal only inside `Point`'s own body;
   elsewhere call `Point.create(1)`.

5. **Prefer `let`.** A `var` that is never reassigned or mutated is an error. Every declared name must be
   used; write `_` for one you do not need.

6. **Handle every throwing call.** Write `try call() otherwise <fallback>`, `otherwise panic("why")` when
   failure is impossible, or a bare `try` inside a function that `throws` the same error type.

7. **Access collections through methods, with `try`.** There is no `items[i]`:

   ```maxon
   let value = try items.get(index) otherwise 0
   ```

8. **Guard divisions.** Use `try (a / b) otherwise …`, or give the divisor a range that excludes zero.

9. **Build strings with interpolation.** `"{name}: {count}"`; there is no `+` on `String`.

10. **Match exhaustively.** Name every enum or union case; put several on one arm with `or`, one per line;
    use `default panic("…")` or `default throws` instead of a plain `default`.

11. **Cast between typealiases explicitly.** Two different aliases never mix; write `value as Target`.

12. **Use `clone()` for an independent copy.** Assigning a record shares it.

13. **Keep tests in `*.maxtest` files.** A test body calls assertions without `try`:

    ```maxon
    test 'adds two numbers'
    	Expect.equal(2 + 2, expected: 4)
    end 'adds two numbers'
    ```
