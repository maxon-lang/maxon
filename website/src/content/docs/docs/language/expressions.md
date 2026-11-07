---
title: Expressions
description: Operators, precedence, the ternary conditional expression, and array access.
sidebar:
  order: 8
---

## Operator Precedence

From highest to lowest:

1. **Postfix**: `.` (member access), `()` (call)
2. **Unary**: `-` (negation), `not`
3. **Cast**: `as`
4. **Multiplicative**: `*` `/` `mod`
5. **Additive**: `+` `-`
6. **Shift**: `shl` `shr`
7. **Comparison**: `==` `!=` `<` `>` `<=` `>=` `is` `is not`
8. **AND**: `and`
9. **XOR**: `xor`
10. **OR**: `or`
11. **Range**: `to` `upto`
12. **Conditional**: `value if condition else other`

Parentheses override precedence: `(2 + 3) * 5` is `25`. A cast applies to the negated value:
`-x as Small` is `(-x) as Small`.

## Arithmetic Operators

| Operator | Meaning | Operands |
|----------|---------|----------|
| `+` | addition | integers, floats |
| `-` | subtraction | integers, floats |
| `*` | multiplication | integers, floats |
| `/` | division (integers truncate toward zero) | integers, floats |
| `mod` | remainder | integers |

- Both operands have the same type. An unaliased integer mixed with a float is promoted to float; two
  different typealiases are **E3005** until one side is cast (see
  [Arithmetic](/docs/language/ranged-typealiases/#arithmetic)).
- Integer arithmetic is 64-bit two's complement and **wraps** on overflow.
- `+` is not defined on `String`; use interpolation or `append`.
- `bool` values do not take part in arithmetic (**E2004**).

## Division by Zero

Dividing by zero is not undefined behaviour and does not crash: `/` and `mod` whose divisor **might** be zero
**throw** `DivisionByZero`, so the failure is part of the type system and behaves identically on every target.

- **The divisor is provably non-zero** — a non-zero literal (`x / 4`), a value whose ranged type excludes 0,
  or a value whose known range excludes 0: the counter of a counted `for` loop between compile-time bounds
  (`i` in `for i in 1 upto 10`), or a sum, difference or product of values with known ranges (constants,
  ranged types, such counters) — and the divide compiles as-is, with no check. A `try` on it is **E3055**.
- **The divisor might be zero** — the divide throws, and must be written `try (a / b) otherwise …` (or
  propagated from a function that `throws`), unless a [`try` block](/docs/language/error-handling/#try-blocks) or a
  [`test` body](/docs/language/testing/#uncaught-errors-in-tests) handles it. A bare divide anywhere else is **E3057**
  (`throwing division requires try`).
- **The divisor is always zero** — a literal `0`, `0.0`, or a constant bound to one — is **E3103**
  (`division by zero: the divisor of '/' is always 0`).

```maxon
typealias Amount = int(i64.min to i64.max)
typealias NonZero = int(1 to 1000)

function ratio(a Amount, b Amount) returns Amount
	return try (a / b) otherwise 0          // b may be zero: supply a fallback
end 'ratio'

function share(total NonZero, parts NonZero) returns NonZero
	return total / parts                    // parts excludes 0: no try
end 'share'

function main() returns ExitCode
	print("{ratio(10, b: 0)} {ratio(10, b: 3)} {share(100, parts: 4)}\n")   // 0 3 25
	return 0
end 'main'
```

The error value is the case `divisionByZero`, which a handler can match:

```maxon
try (total / count) otherwise (e) 'handle'
	match e 'kind'
		divisionByZero then print("nothing to divide by\n")
	end 'kind'
end 'handle'
```

- **`try` applies to the division itself.** `try (10 + a / b)` is **E2015** (`try must be applied to a
  call`); compute the quotient first, then use it.
- **Float division** throws on a zero divisor too, including `-0.0`. `inf` and `NaN` produced any other way
  are ordinary IEEE values. There is no float `mod`.
- `i64.min mod -1` is `0` on every target. `i64.min / -1` has no representable quotient, and a `try`
  cannot catch it: on every target the program stops with `panic: integer overflow`, a stack trace and
  exit code 1.

## Comparison Operators

| Operator | Meaning |
|----------|---------|
| `==` `!=` | equal, not equal |
| `<` `>` `<=` `>=` | ordering |

All produce `bool`. Numbers, `bool` and `Character` support every comparison; `String` supports only `==`
and `!=`. Comparing two records with `==` calls the type's `equals` method (see
[Equality and Copying](/docs/language/composite-types/#equality-and-copying)); unions cannot be compared
(**E3066**). Comparing values of different types is **E3005**, two brands of one instance included.

`==` and `!=` call `equals`, and `<`, `<=`, `>` and `>=` call `compare`. A method of either name that takes
one operand besides its receiver may not write that operand or its receiver, directly or through a
function it calls (**E3187**, reported at the method). Operands are only read, so a `let`, a
`var`, a field or an expression compares alike.

## Reference Identity Operators

`a is b` is `true` when two names refer to the **same** record; `a is not b` is its negation. They apply only
to heap records — on numbers, on `bool`, and on an
[inline packed record](/docs/language/composite-types/#inline-packed-records) (whose value is a word, not a record) they are **E3068**.

```maxon
function sameRecord(a Point, b Point) returns bool
	return a is b
end 'sameRecord'
```

## Logical and Bitwise Operators

The word operators are logical on `bool` operands and bitwise on integer operands:

| Operator | On `bool` | On integers | Example |
|----------|-----------|-------------|---------|
| `and` | logical AND | bitwise AND | `0xF0 and 0x3C` is `48` |
| `or` | logical OR | bitwise OR | `0xF0 or 0x0F` is `255` |
| `xor` | logical XOR | bitwise XOR | `0xF0 xor 0xFF` is `15` |
| `not` | logical NOT | bitwise NOT | `not 0` is `-1` |

On `bool` operands `and` and `or` **short-circuit**: the right side runs only if the left side does not
already decide the result, so a guard on the left can protect the right:

```maxon
let ok = i < items.count() and (try items.get(i) otherwise 0) > 0
```

Integer `and`/`or` always evaluate both sides.

## Shift Operators

| Operator | Meaning | Example |
|----------|---------|---------|
| `shl` | shift left | `1 shl 4` is `16` |
| `shr` | shift right | `256 shr 4` is `16` |

- **`shr` fills according to the left operand's type.** A signed value shifts arithmetically
  (`(0 - 8) shr 1` is `-4`); an unsigned alias or a `bits(n)` pattern fills with zeros
  (`u64.max as Word shr 60` is `15`). `shl` always fills with zeros.
- **A count of 64 or more shifts every bit out** — `1 shl 64` is `0`. It is not reduced modulo 64.
- **A negative count is an error.** A count known at compile time is **E2054**; one that turns out
  negative at run time panics with `negative shift count`.

## Unary Operators

| Operator | Meaning |
|----------|---------|
| `-x` | negation |
| `not x` | logical NOT (`bool`) or bitwise NOT (integer) |

`-` does not chain: write `-(-x)`, since `--x` is a syntax error. `not not x` is allowed.

On a float, `-x` flips the sign bit (IEEE-754 negation), so `-0.0` is a distinct value and `-(0.0)` prints
`-0.0`. Negating an integer alias keeps the alias only where its range is closed under negation; otherwise the result is an unaliased signed value.

## Conditional Expression

```text
<value> if <condition> else <other>
```

The condition is `bool` and both branches have the same type. The conditional binds more loosely than every
operator, and chains to the right:

```maxon
let magnitude = x if x >= 0 else -x
let tier = "gold" if score > 90 else "silver" if score > 70 else "bronze"
print("Status: {"on" if enabled else "off"}\n")
```

## Ranges

`a to b` (inclusive) and `a upto b` (exclusive) produce a range. In a `for` header or a `match` pattern a
range is syntax; elsewhere an integer range is a `Range` or `OpenRange` value that can be stored and
iterated (see [For Loop](/docs/language/statements/#for-loop)).

## `sizeof` and `countof`

Both take a **type** and produce a compile-time `Count`.

- `sizeof(T)` is the size of a value of type `T` in bytes: `sizeof(int)` and `sizeof(float)` are `8`,
  `sizeof(bool)` is `1`, and a record type is the size of its fields — except an
  [inline packed record](/docs/language/composite-types/#inline-packed-records), which is `8` whatever its fields sum to. Inside a
  generic body, `sizeof` of any of its type parameters is the size of that parameter's argument: in
  `type Holder uses T, U`, `sizeof(T)` and `sizeof(U)` each answer for their own argument.
- `countof(T)` is the number of elements a **fixed-size** container type holds: `countof(FixedArray with 3 Int)`
  is `3`. Inside such a container's own body, `countof(Self)` is the count of the instance a method is
  called on: the number its type states, or the record's length for a
  [runtime-sized](#runtime-sized-fixed-arrays) receiver. In a `static function` it is the count of the
  instance the call names, so a static that reads `countof(Self)` or returns `Self`, called through a
  runtime-sized alias, is **E2015**. A default argument reading `countof(Self)` is **E2015** too, because a
  default runs in a helper function of its own that has no count to read. A type with no fixed element
  count — a record, a primitive, a growable `Array`, a runtime-sized `FixedArray with Int` — is refused
  (**E2015**); ask the value for its `count()`, or name a sized alias
  (`typealias Sized = FixedArray with 4 Int`) and take `countof(Sized)`.
- `countof(T)` is also a constant in a [top-level initializer](/docs/language/variables/#top-level-bindings), alone or folded
  (`let last = countof(Vec3) - 1`), and the binding is a `Count`. Such a `let` is a compile-time bound inside a
  function: over `let slots = countof(Vec4)`, `for i in 0 upto slots` proves its counter a
  [`FixedArray` index](#fixed-arrays-and-proven-indices).

```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

type Pair
	export var left as Int
	export var right as Int
end 'Pair'

function main() returns ExitCode
	print("{countof(Vec3)} {sizeof(Pair)} {sizeof(bool)}\n")    // 3 16 1
	return 0
end 'main'
```

## Collection Access

Maxon has no subscript operator: `items[0]` is a syntax error. Elements are read and written through
methods, and **every access is bounds-checked** — at run time, or at compile time for a
[`FixedArray` index proven inside its size](#fixed-arrays-and-proven-indices):

- `get(index)` returns the element, or **throws** `ArrayError.indexOutOfBounds` when the index is at or past
  `count()`. Like any throwing call it needs `try`.
- `set(index, value:)` replaces an element and throws the same error for an index past the end.
- `first()`, `last()`, `pop()` and `remove(index)` throw when there is no such element.
- An index is a non-negative integer: a negative literal is **E3005**, and a computed negative index panics
  with a range check.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function main() returns ExitCode
	var values = TallyArray.create(0)
	values.push(10)
	values.push(20)

	let second = try values.get(1) otherwise 0           // 20
	let missing = try values.get(5) otherwise 0          // 0: index 5 is out of range
	try values.set(0, value: 99) otherwise panic("index out of range")
	print("{second} {missing} {try values.first() otherwise 0}\n")   // 20 0 99
	return 0
end 'main'
```

**Sizing an array.** An `Array` or `ByteArray` states its capacity when it is created: `create()` with no
argument is **E3195**. `create(0)` makes an empty array. `create(n)` makes `n` zero elements and
`create(n, zeroed: false)` an empty array with room for `n`, each in one allocation when the elements take at
most 256 bytes; `n` is an `ElementIndex` and `zeroed:` a literal `true` or `false`. `push(value)` appends; `reserve(n)` grows the capacity of an existing
array without changing `count()`. `resize(n)` changes the length and fills new slots with zero, so `resize` and
`create(n)` are available only for elements stored inline — integers, floats, `bool`, payload-free enums and
unions — whose type admits zero. For an array of records, strings, promises or other managed values, of an alias whose
range excludes 0 (`int(1 to 100)`), or of an enum or union whose raw values (tags) include no 0, `resize` and
`create(n)` are **E3106**, as are `create(n, zeroed: true)` and `FixedArray.create` (which takes
`value: v` for such an element, as [runtime-sized fixed arrays](#runtime-sized-fixed-arrays) states); create with
`create(n, zeroed: false)`, grow with `push` or `growFilled(n, value:)` naming a case, and shrink with
`truncate(n)`:

```maxon
typealias Names = Array with String

function main() returns ExitCode
	var names = Names.create(0)
	names.growFilled(3, value: "")    // three empty strings
	names.push("ada")
	names.truncate(2)
	print("{names.count()}\n")        // 2
	return 0
end 'main'
```

A generic body is compiled once for every instantiation, so it is judged by all of them: `create(n)` over its
type parameter is **E3106** when any instantiation of the type is at an element with no zero value, and so is
`resize` in a body you write. The standard library's own `resize` is not refused, because `Map` and `Set` fill
the slots they read.

## Fixed Arrays and Proven Indices

A `FixedArray with N T` holds exactly `N` elements, and `N` is part of its type: `FixedArray with 3 Int`
and `FixedArray with 4 Int` are different types. The compiler therefore answers at each `get` or `set`
whether the index lies inside `0 to N-1`:

- **A proven index makes the access total.** The call cannot throw, is written without `try`, and a `try`
  on it is **E3055**. An index is proven when it is a constant inside the size, a value whose ranged type
  lies inside (`typealias Slot = int(0 to 3) implements ElementIndex` for a `FixedArray with 4`), the
  counter of a counted `for` loop whose bounds are known at compile time and lie inside (literals,
  `countof(T)`, folded constants), or a sum, difference or product of values with known ranges whose
  computed range lies inside.
- **A constant index at or past the size** is **E3005** (`Value 4 is outside the range of the indices of
  'FixedArray with 4' (int(0 to 3))`).
- **Any other index** is checked at run time and throws `ArrayError.indexOutOfBounds`, so the call needs
  `try`; a bare call is **E3057**, with the hint to give the index a ranged type within `int(0 to N-1)`.
  An `ElementIndex` or `Count` parameter, a `var` stepped by a `while` loop, a call result and a
  reassigned `var` (except at a read where it still holds a known constant, which is that constant) are
  such indices, and so is every index inside an `extension FixedArray`, where the count is known only at
  run time.

The same known ranges prove a divisor non-zero (see [Division by Zero](#division-by-zero)).

```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int
typealias Slot = int(0 to 3) implements ElementIndex

function read(v Vec4, at Slot) returns Int
	return v.get(at)                              // proven by the parameter's type
end 'read'

function main() returns ExitCode
	var v = Vec4.create()

	for i in 0 upto countof(Vec4) 'fill'
		v.set(i, value: (i * 10) as Int)          // proven by the loop bounds
	end 'fill'

	print("{read(v, at: 2)} {v.get(3)}\n")        // 20 30
	return 0
end 'main'
```

### Runtime-sized fixed arrays

A `FixedArray with T` with no count is **runtime-sized**: its length is chosen when it is built and belongs
to the value, not to the type. `Buf.create(n)` through `typealias Buf = FixedArray with Int` builds one
zeroed heap record of `n` elements, and `Buf.create(n, value: v)` one whose every element starts as `v`. The
length is fixed at creation, so `push` and `reserve` are refused. `refill(v)` writes `v` into every element of an
existing fixed array in place. `count()` loads the record's length, and `get` and `set` check against it at run time: no index is proven inside a
runtime-sized array, so both need `try` (a bare call is **E3057**).

- **The length `n`** is checked as `Array.create(n)`'s is. A plain integer, an `ElementIndex` or a `Count`
  is accepted, range-checked at run time where the compiler cannot prove it non-negative. A value of another
  named integer type is **E3005**, with the hint to cast it `as ElementIndex`.
- **A constant `n`** gives the type `FixedArray with n T`, unbranded, so every property of a sized fixed array
  applies: `count()` is the constant and indices are proven against it. A constant is a literal, a `let`
  bound to a constant, a top-level `let`, a folded `countof(...)` expression, or a `var` read where it holds a
  constant. A constant `0`, or one above `4294967295`, is **E2069**.
- **The element** is an integer, a `bool`, a float or a managed value (a `String`, a record, an array or a
  union); any other element is **E2015**. A managed element, an element whose range excludes 0, and an enum or
  union whose raw values include no 0 are **E3106** unless `value: v` is given, because otherwise every slot
  starts as zero. A generic type holding a `FixedArray with N T` over its type parameter `T` is **E3106** at
  its `create()` when any instantiation of it is at such an element.
- **A sized array widens to a runtime-sized one.** A `FixedArray with N T` is accepted where a
  `FixedArray with T` is declared, in an argument, a return or a field store; the reverse is **E3005**. A
  conditional, a `match`, a `try ... otherwise` and the type arguments inferred for a generic type that meet
  a sized and a runtime-sized array give the runtime-sized type, whichever is written first.
- **A literal through a runtime-sized alias is sized.** `Buf from [10, 20, 30]` is a `FixedArray with 3 Int`.
- **`create` takes the form its type has (E3194).** A type-sized alias takes no length (`Vec3.create()`, or
  `Vec3.create(value: v)`), a runtime-sized alias takes one and an optional `value:` (`Buf.create(n)`,
  `Buf.create(n, value: v)`), and any other argument is refused: for a growable buffer write
  `Array.create(n, zeroed: false)`.
- **A count is an integer literal.** `FixedArray with K Int`, `with -3 Int` and `with 3.5 Int` are **E2069**.

```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Buf = FixedArray with Int

function total(values Buf) returns Int
	var sum = 0 as Int

	for i in 0 upto values.count() 'each'
		sum = sum + (try values.get(i) otherwise 0)
	end 'each'

	return sum
end 'total'

function main() returns ExitCode
	var v = Vec3.create()
	v.set(1, value: 7)

	let literal = Buf from [10, 20, 30]
	let zeros = Buf.create(literal.count())

	print("{total(v)} {literal.get(2)} {total(zeros)}\n")    // 7 30 0
	return 0
end 'main'
```

The collection types and their full method lists are in the [standard library reference](/docs/stdlib/).
