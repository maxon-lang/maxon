---
feature: variables
status: stable
keywords: variables, let, var
category: declaration
---
# Variables

## Documentation

Maxon supports two kinds of variable declarations:

- `let` - immutable binding
- `var` - mutable binding

## Tests

<!-- test: let-declaration -->
```maxon
function main() returns ExitCode
	let x = 42
	return x
end 'main'
```
```exitcode
42
```

<!-- test: var-declaration -->
```maxon
function main() returns ExitCode
	let x = 10
	return x
end 'main'
```
```exitcode
10
```

<!-- test: multiple-variables -->
```maxon
function main() returns ExitCode
	let a = 10
	let b = 20
	return a + b
end 'main'
```
```exitcode
30
```

<!-- test: top-level-string-constant -->
Top-level `let` with a string literal value.
```maxon
let GREETING = "hello"

function main() returns ExitCode
	if GREETING == "hello" 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: var-explicit-type-error -->
Explicit type annotations are not allowed on var declarations.
```maxon
function main() returns ExitCode
	let x: int = 0
	return x
end 'main'
```
```maxoncstderr
error E2010: specs/variables/var-explicit-type-error.maxon:3:7: Expected '=' but got ':'
```

<!-- test: let-explicit-type-error -->
Explicit type annotations are not allowed on let declarations.
```maxon
function main() returns ExitCode
	let x: int = 0
	return x
end 'main'
```
```maxoncstderr
error E2010: specs/variables/let-explicit-type-error.maxon:3:7: Expected '=' but got ':'
```

<!-- test: error.a-local-var-bound-to-a-bare-integer -->
A local `var` initialized with a bare integer literal is refused at the literal.
```maxon
function main() returns ExitCode
	var n = 0
	n = n + 1
	return n
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:10: A var cannot start from a bare number: write 'var name = expr as Type'.
```

<!-- test: error.a-local-var-bound-to-a-bare-float -->
A local `var` initialized with a bare float literal is refused at the literal.
```maxon
function main() returns ExitCode
	var r = 0.5
	r = r * 4.0
	return trunc(r) as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:10: A var cannot start from a bare number: write 'var name = expr as Type'.
```

<!-- test: error.a-local-var-bound-to-literal-arithmetic -->
A local `var` initialized with arithmetic over literals alone is refused at the initializer's first token.
```maxon
function main() returns ExitCode
	var s = 16 * 1024
	s = s / 1024
	return s
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:10: A var cannot start from a bare number: write 'var name = expr as Type'.
```

<!-- test: error.a-local-var-bound-to-a-plain-let-constant -->
A local `var` initialized from a plain `let` constant is refused, because the constant is a literal.
```maxon
function main() returns ExitCode
	let k = 5
	var n = k
	n = n + 1
	return n
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:10: A var cannot start from a bare number: write 'var name = expr as Type'.
```

<!-- test: error.a-local-var-bound-to-an-unheaded-array-literal -->
A local `var` initialized with an array literal that names no element type is refused at the bracket.
```maxon
function main() returns ExitCode
	var xs = [1, 2]
	xs.push(3)
	return xs.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:11: A var cannot start from a bare number: write 'var name = expr as Type'.
```

<!-- test: error.a-let-constant-outside-a-slots-range -->
A plain `let` constant is range-checked at each use like the literal it holds.
```maxon
typealias Small = int(0 to 100)

function take(v Small) returns Small
	return v
end 'take'

function main() returns ExitCode
	let Big = 1000
	let t = take(Big)
	return t as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:10: Value 1000 is outside the range of 'Small' (int(0 to 100))
```

<!-- test: a-let-constant-acts-like-a-literal -->
A plain `let` constant passes to a ranged parameter its value fits, as the literal would.
```maxon
function show(v Count)
	print("{v}\n")
end 'show'

function main() returns ExitCode
	let Max = 1024
	show(Max)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1024
```

<!-- test: a-let-array-constant-acts-like-a-literal -->
A plain `let` array constant passes to a typed-array parameter its elements fit, as the literal would.
```maxon
typealias Counts = Array with Count

function total(xs Counts) returns Count
	var sum = 0 as Count
	for x in xs 'each'
		sum = sum + x
	end 'each'
	return sum
end 'total'

function main() returns ExitCode
	let xs = [1, 2]
	print("{total(xs)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: a-var-rebound-from-a-literal-keeps-its-alias -->
A literal stored into a ranged `var` takes the var's alias, so a `var` initialized from it is born ranged.
```maxon
function main() returns ExitCode
	var x = 10 as FullRangeInt
	x = 19
	var y = x
	y = y + 1
	print("{y}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
20
```

<!-- test: error.a-local-var-bound-to-a-map-literal-of-bare-numbers -->
A local `var` initialized with a map literal whose values are bare numbers is refused at the bracket, as an array of them is.
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20]
	m.upsert(3, value: 30)
	return m.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:10: A var cannot start from a bare number: write 'var name = expr as Type'.
```

<!-- test: error.a-local-var-bound-to-a-map-literal-with-a-bare-key -->
A bare number in the key column is as much a plain number as one in the value column.
```maxon
function main() returns ExitCode
	var m = [1: 10 as Count]
	m.upsert(2, value: 20)
	return m.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:10: A var cannot start from a bare number: write 'var name = expr as Type'.
```

<!-- test: error.a-local-var-bound-to-a-tuple-of-bare-numbers -->
A local `var` initialized with a tuple of bare numbers is refused at the parenthesis.
```maxon
function main() returns ExitCode
	var t = (3, 4)
	t.0 = 9
	return t.0 as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:10: A var cannot start from a bare number: write 'var name = expr as Type'.
```

<!-- test: a-local-var-map-of-typed-numbers-is-accepted -->
A `var` map whose columns are cast to an alias is a typed map, and can grow.
```maxon
function main() returns ExitCode
	var m = [1 as Count: 10 as Count]
	m.upsert(2, value: 20)
	return m.count() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: a-local-var-tuple-of-typed-numbers-is-accepted -->
A `var` tuple whose elements are cast to an alias is a typed tuple, and its element can be written.
```maxon
function main() returns ExitCode
	var t = (3 as Count, 4 as Count)
	t.0 = 9
	print("{t.0}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9
```

<!-- test: a-var-born-from-an-unproven-sum-is-a-runtime-door -->
A `var` is born a value of its alias, so the birth is a range door: a sum of two `Small`s wears the name without proving the range, and the `var` made from it checks the value at the birth.
```maxon
typealias Small = int(0 to 100)

function main() returns ExitCode
	let n = CommandLine.args().count()
	let a = (n + 59) as Small
	var s = a + a

	if n > 5 'never'
		s = a
	end 'never'

	return s as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at a-var-born-from-an-unproven-sum-is-a-runtime-door.test:7: Range check failed: value outside typealias 'Small'
Stack trace:
  in main
  in mrt_start
```

<!-- test: error.a-let-bound-to-a-number-of-unknown-range -->
A `let` of a number whose range the compiler cannot know is refused, because nothing could check its uses: the cast names the type and checks the value once.
```maxon
function main() returns ExitCode
	let k = CommandLine.args().count()
	let m = 1 shl k
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:10: A let cannot start from a number whose range is unknown: write 'let name = expr as Type'.
```
