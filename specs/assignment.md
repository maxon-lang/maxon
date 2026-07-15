---
feature: assignment
status: stable
keywords: [assignment, equals, mutation]
category: statements
---

# Assignment Statement

## Documentation

The assignment operator `=` updates the value of a mutable variable.

### Syntax

```maxon
variable = expression
```
### Example

```maxon
function main() returns ExitCode
	var x = 10
	x = 20          // Update x
	x = x + 5       // x is now 25
	return x
end 'main'
```
```exitcode
25
```


### Restrictions

- Cannot assign to `let` variables
- Variable must be declared with `var`
- Expression type must match variable type

### A variable's type is its DECLARED type

A variable's type is fixed by its declaration and an assignment never re-infers it. Assigning a
value of a different type is an error (E3005) — `var` means the VALUE may change, not the TYPE:

```maxon
var x = 0 as Integer
x = "hello"        // ERROR E3005: cannot assign a value of type 'struct' to variable 'x',
                   //              which holds 'int'
```

The declaration is the single source of truth for the type, and an assignment coerces the value
to it. A value of a *widenable* type is promoted to the declared type on the way in — so the
stored value always carries the declared type, and `var f = 1.5; f = 5` holds `5.0`, not `5`:

```maxon
var f = 1.5
f = 5              // OK: promoted to float. f is 5.0
```

The rule is the same for a local, a global, a `static` and a struct field — they differ only in
where the store lands, never in what may be stored.

## Tests

<!-- test: basic-assignment -->
```maxon
function main() returns ExitCode
	var x = 3
	x = x + 2
	return x
end 'main'
```
```exitcode
5
```


<!-- test: multiple-assignments -->
```maxon
function main() returns ExitCode
	var x = 10
	var y = 20
	x = y
	y = 30
	return x + y
end 'main'
```
```exitcode
50
```


<!-- test: assignment-in-loop -->
```maxon
function main() returns ExitCode
	var sum = 0
	var i = 1
	while i <= 5 'loop'
		sum = sum + i
		i = i + 1
	end 'loop'
	return sum
end 'main'
```
```exitcode
15
```

### The type rule — rejections

<!-- test: error.retype-local-errors -->
A `var`'s type is its declared type. This program compiled CLEAN and printed `hello` — the
assignment was silently accepted, because a local's readers forward the assigned value and so
never consult the declared type at all. The variable appeared to re-infer; nothing re-inferred it.
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var x = 0 as Integer
	x = "hello"
	print("{x}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-local-errors.test:7:2: cannot assign a value of type 'struct' to variable 'x', which holds 'int'
```

<!-- test: error.retype-global-errors -->
The SAME store to a GLOBAL, and the same error — this test and the local one above are the pair
that pins the two paths together. They used to disagree: a global cannot forward the assigned
value, it must go through a typed load using the declared kind, so this program compiled clean
and printed a raw heap pointer (`140696866942976`) with exit 0. One rule, one answer, either way.
```maxon

typealias Integer = int(i64.min to i64.max)

var g = 0 as Integer

function main() returns ExitCode
	g = "hello"
	print("{g}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-global-errors.test:8:2: cannot assign a value of type 'struct' to global 'g', which holds 'int'
```

<!-- test: error.retype-conditional-errors -->
A retype the program never executes is still a type error: the rule is about the ASSIGNMENT, not
about whether control reaches it. This branch is dead (`c` is `false`), and the program still
panicked with a nil pointer — `z` was typed String lexically while holding the int `0`.
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let c = false
	var z = 0 as Integer
	if c 'maybe'
		z = "hello"
	end 'maybe'
	print("{z}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-conditional-errors.test:9:3: cannot assign a value of type 'struct' to variable 'z', which holds 'int'
```

<!-- test: error.retype-in-loop-errors -->
The same inside a loop body, where the variable is legitimately reassigned on every iteration:
being reassignable is what `var` grants, and it is not permission to change type.
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var acc = 0 as Integer
	var i = 0 as Integer
	while i < 3 'loop'
		acc = "hello"
		i = i + 1
	end 'loop'
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-in-loop-errors.test:9:3: cannot assign a value of type 'struct' to variable 'acc', which holds 'int'
```

<!-- test: error.retype-struct-to-int-errors -->
A struct into an `int`. Unchecked, this reached the arithmetic below and died as
`E9001: Unhandled cast combination: Struct -> Integer` — an INTERNAL error with a .NET stack
trace, naming no source position, for a plain type error. The check fires at the assignment,
which is both where the defect is and where the user can see it.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	var n = 0 as Integer
	n = Point.create(1)
	let sum = n + 1
	print("{sum}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-struct-to-int-errors.test:15:2: cannot assign a value of type 'struct' to variable 'n', which holds 'int'
```

<!-- test: error.retype-struct-to-other-struct-errors -->
Two different structs are two different types, even though both are "a struct". A kind alone
cannot tell them apart, so this one survived the type check and died in the LOWERING as
`E9001: The given key 'p.x' was not present in the dictionary` — the assignment's defect
reported as an internal error against a field access on the line after it.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Other
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Other'

function main() returns ExitCode
	var p = Point.create(1)
	p = Other.create(2)
	print("{p.x}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-struct-to-other-struct-errors.test:23:2: cannot assign a value of type 'Other' to variable 'p', which holds 'Point'
```

<!-- test: error.retype-struct-field-errors -->
A FIELD is a place with a declared type too, and the rule does not change because the store lands
in a struct instead of a frame slot. This site carried only the WIDENING half of the rule — a
mismatch it could not widen was stored anyway — so this program compiled clean and printed a raw
heap pointer (`1827332661328`) with exit 0, exactly as the global did.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(1)
	p.x = "hello"
	print("{p.x}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-struct-field-errors.test:15:4: cannot assign a value of type 'struct' to field 'x' of 'Point', which holds 'int'
```

### The type rule — what stays legal

<!-- test: assign-ranged-typealias -->
A literal into a ranged typealias — the most common assignment there is. The declared type is the
alias; a literal in range is the same type, not a retype.
```maxon

typealias Score = int(0 to 100)

function main() returns ExitCode
	var s = 0 as Score
	s = 5
	s = s + 20
	return s
end 'main'
```
```exitcode
25
```

<!-- test: assign-widening-byte-to-int -->
Widening stays implicit: a `Byte` is assignable to an `int` because every `Byte` IS an `int`.
```maxon

typealias Byte = int(0 to u8.max)
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var i = 0 as Integer
	let b = 7 as Byte
	i = b
	return i as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: assign-widening-int-to-float-promotes -->
An `int` assigned to a `float` is PROMOTED, not merely permitted. The stored value carries the
declared type, so this prints `5.0`. Before the declared type was made the single source of
truth this printed `5`: the widening was legal, went unapplied, and the reader saw the raw int.
```maxon
function main() returns ExitCode
	var f = 1.5
	f = 5
	print("{f}")
	return 0 as ExitCode
end 'main'
```
```stdout
5.0
```

<!-- test: assign-struct-to-same-struct -->
A struct value into a variable of that struct type, from a factory, from a function, and from
another variable.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function makePoint(v Integer) returns Point
	return Point.create(v, y: v)
end 'makePoint'

function main() returns ExitCode
	var p = Point.create(1, y: 2)
	p = makePoint(7)
	p.x = 9
	let q = Point.create(5, y: 5)
	p = q
	return p.x as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: assign-constants-enum-to-backing -->
A constants-enum where its numeric backing type is declared coerces to the raw backing value.
```maxon

typealias Byte = int(0 to u8.max)

enum JsonByte as Byte
	lBracket = 91
	rBracket = 93
end 'JsonByte'

function main() returns ExitCode
	var b = 0 as Byte
	b = JsonByte.lBracket
	return b
end 'main'
```
```exitcode
91
```

<!-- test: assign-self-field-and-iterator -->
A `self` field assignment and a `for`-loop iterator binding are assignments too, and are governed
by the same rule.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Counter
	export var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function addAll(values IntArray) returns Integer
		for v in values 'each'
			self.total = self.total + v
		end 'each'
		return self.total
	end 'addAll'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	let nums = [1, 2, 3]
	return c.addAll(nums) as ExitCode
end 'main'
```
```exitcode
6
```

