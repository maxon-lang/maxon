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

- **The divisor is provably non-zero** — a non-zero literal (`x / 4`), or a value whose ranged type excludes 0
  — and the divide compiles as-is, with no check.
- **The divisor might be zero** — the divide throws, and must be written `try (a / b) otherwise …` (or
  propagated from a function that `throws`). A bare divide is **E3057** (`throwing division requires try`).
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
(**E3066**). Values of different types never compare (**E3005**).

## Reference Identity Operators

`a is b` is `true` when two names refer to the **same** record; `a is not b` is its negation. They apply only
to records — on numbers or `bool` they are **E3068**.

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
`-0.0`. Negating a signed integer alias keeps the alias; negating an unsigned alias gives an unaliased value.

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

Both take a **type** and produce a compile-time integer.

- `sizeof(T)` is the size of a value of type `T` in bytes: `sizeof(int)` and `sizeof(float)` are `8`,
  `sizeof(bool)` is `1`, and a record type is the size of its fields.
- `countof(T)` is the number of elements a **fixed-size** container type holds: `countof(Vector with 3 Int)`
  is `3`. Inside such a container's own body, `countof(Self)` is the receiver's count. A type with no fixed
  element count — a record, a primitive, a growable `Array` — is refused (**E2015**); ask an `Array` for its
  `count()` instead.

```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

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
methods, and **every access is bounds-checked**:

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
	var values = TallyArray.create()
	values.push(10)
	values.push(20)

	let second = try values.get(1) otherwise 0           // 20
	let missing = try values.get(5) otherwise 0          // 0: index 5 is out of range
	try values.set(0, value: 99) otherwise panic("index out of range")
	print("{second} {missing} {try values.first() otherwise 0}\n")   // 20 0 99
	return 0
end 'main'
```

**Sizing an array.** `create()` makes an empty array; `push(value)` appends; `reserve(n)` grows capacity
without changing `count()`. `resize(n)` changes the length and fills new slots with zero, so it is available
only for elements stored inline — integers, floats, `bool`, payload-free enums. For an array of records,
strings or other managed values, `resize` is **E3106**; grow with `push` or `growFilled(n, value:)` and shrink
with `truncate(n)`:

```maxon
typealias Names = Array with String

function main() returns ExitCode
	var names = Names.create()
	names.growFilled(3, value: "")    // three empty strings
	names.push("ada")
	names.truncate(2)
	print("{names.count()}\n")        // 2
	return 0
end 'main'
```

The collection types and their full method lists are in the [standard library reference](/docs/stdlib/).
