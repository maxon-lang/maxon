---
title: Expressions
description: Operators, precedence, the ternary conditional expression, and array access.
sidebar:
  order: 8
---

### Operator Precedence (highest to lowest)

1. **Postfix**: `.` (member access), `as` (cast), function call `()`
2. **Unary**: `-` (negation), `not` (logical/bitwise not)
3. **Multiplicative**: `*` `/` `mod`
4. **Additive**: `+` `-`
5. **Shift**: `shl` `shr`
6. **Comparison**: `==` `!=` `<` `>` `<=` `>=` `is` `is not`
7. **AND**: `and`
8. **XOR**: `xor`
9. **OR**: `or`
10. **Conditional**: `<true_value> if <condition> else <false_value>` (lowest precedence)

### Arithmetic Operators

| Operator | Description | Types | Example |
|----------|-------------|-------|---------|
| `+` | Addition | int, float | `a + b` |
| `-` | Subtraction | int, float | `a - b` |
| `*` | Multiplication | int, float | `a * b` |
| `/` | Division | int, float | `a / b` |
| `mod` | Modulo | int only | `a mod b` |

**Notes:**
- Mixed int/float operations promote int to float

### Comparison Operators

| Operator | Description | Result Type |
|----------|-------------|-------------|
| `==` | Equal to | bool |
| `!=` | Not equal to | bool |
| `<` | Less than | bool |
| `>` | Greater than | bool |
| `<=` | Less than or equal | bool |
| `>=` | Greater than or equal | bool |

Using `==` or `!=` on struct types requires the type to implement the `Equatable` interface (error E3069 if it does not). Primitives, `String`, and `Array` support `==` and `!=` without restriction. For reference identity comparison (same heap object), use `is` and `is not` instead.

### Reference Identity Operators

| Operator | Description | Result Type |
|----------|-------------|-------------|
| `is` | Same reference (same heap object) | bool |
| `is not` | Different references | bool |

`is` and `is not` compare whether two struct-typed variables refer to the same heap object. They cannot be used on primitive types (`int`, `float`, `bool`, `byte`) — using them produces error E3068.

```maxon
function areSame(a Point, b Point) returns bool
	return a is b
end 'areSame'
```

### Logical / Bitwise Operators

The keyword operators `and`, `or`, `xor`, and `not` are context-dependent: they perform logical operations on `bool` operands and bitwise operations on `int` operands.

| Operator | On `bool` | On `int` | Example |
|----------|-----------|----------|---------|
| `and` | Logical AND | Bitwise AND | `a > 0 and b < 10` / `flags and 0xff` |
| `or` | Logical OR | Bitwise OR | `x == 1 or x == 2` / `flags or 0x01` |
| `xor` | Logical XOR | Bitwise XOR | `a xor b` / `value xor mask` |
| `not` | Logical NOT (unary) | Bitwise NOT (unary) | `not done` / `not mask` |

`and` and `or` short-circuit when both operands are `bool`: the right-hand side
is evaluated only if the left-hand side does not already determine the result
(`false and _` skips the right; `true or _` skips the right). This lets a
left-hand guard make the right-hand side safe to evaluate, e.g.
`i < arr.count() and (try arr.get(i) otherwise default) > 0`. Integer `and`/`or`
remain bitwise and always evaluate both sides.

### Shift Operators

Shift operators work on integers only.

| Operator | Description | Example |
|----------|-------------|---------|
| `shl` | Shift left | `1 shl 4` (result: 16) |
| `shr` | Shift right | `256 shr 4` (result: 16) |

### Unary Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `-` | Negation | `-x` |
| `not` | Logical NOT / Bitwise NOT | `not condition` / `not mask` |

The `-` operator cannot be chained directly. Use parentheses for nested negation:
```maxon
var y = -(-x)      // OK: parenthesized
var z = -(x + 1)   // OK: subexpression
// var w = --x      // Error: consecutive negation operators
```

The `not` operator can be applied repeatedly:
```maxon
var a = not not x  // OK: double bitwise NOT (identity for integers)
```

### Parentheses
Override precedence:
```maxon
(2 + 3) * 5    // 25, not 17
```

### Conditional (Ternary) Expression

The conditional expression evaluates one of two values based on a boolean condition:

```text
<true_value> if <condition> else <false_value>
```

The condition must be `bool`. Both arms must produce the same type. The conditional expression binds looser than all binary operators, so operands are evaluated naturally without extra parentheses:

```maxon
let x = a + b if flag else c * d    // (a + b) if flag else (c * d)
let abs = x if x > 0 else -x
let label = "yes" if enabled else "no"
```

Conditional expressions can be chained. They associate to the right:

```maxon
let tier = "gold" if score > 90 else "silver" if score > 70 else "bronze"
// equivalent to: "gold" if score > 90 else ("silver" if score > 70 else "bronze")
```

Conditional expressions work inside string interpolation, including with nested string literals:

```maxon
print("Status: {"on" if flag else "off"}")
```

### Array Access

Array elements are accessed using the `.get()` method, which throws `ArrayError.indexOutOfBounds` if the index is out of range, or `ArrayError.emptySlot` if the slot at that index is empty (null pointer, e.g. after `resize()` without filling every slot):
```maxon
var arr = [1, 2, 3, 4, 5]
var first = try arr.get(0) otherwise 0
var last = try arr.get(arr.count() - 1) otherwise 0
```

Array elements are modified using the `.set()` method:
```maxon
var arr = [1, 2, 3]
arr.set(0, value: 100)  // First positional, second named
```

### Creating Empty Arrays

Create an empty typed array using a type alias:
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var numbers = IntArray.create()         // Empty array
numbers.push(42)                 // Add elements with push
```

To preallocate with a specific length (elements zero-initialized):
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var buffer = IntArray.create()
buffer.resize(100)               // Length is now 100
buffer.set(0, value: 42)         // Can set any index 0-99
```

To preallocate capacity without changing length (for performance):
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var buffer = IntArray.create()
buffer.reserve(100)              // Capacity is 100, length is 0
buffer.push(42)                  // Now length is 1
```

---
