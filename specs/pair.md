---
feature: pair
status: stable
keywords: [pair, key, value, generic, tuple, collections]
category: stdlib
---

# Pair

## Documentation

### Overview

`Pair` is a generic key-value type from the standard library. It holds two values: `key` and `value`, whose types are determined by its associated types `Key` and `Value`.

**Definition:**

```text
export type Pair uses Key, Value
  export var key Key
  export var value Value
end 'Pair'
```

### Usage

Create a concrete Pair type using `typealias` with specific types:

```text
typealias IntPair is Pair with (int, int)
var p = IntPair{key: 1, value: 2}
```

Both fields are exported and mutable, so they can be read and written from outside:

```text
p.key = 10
p.value = 20
```

### Map Iteration

`Pair` is used as the entry type when iterating over a `Map`. Each iteration yields a `Pair` containing the key and value of each entry.

## Tests

<!-- test: int-pair-basic -->
```maxon
typealias IntPair is Pair with (int, int)

function main() returns int
  var p = IntPair{key: 10, value: 32}
  return p.key + p.value
end 'main'
```
```exitcode
42
```

<!-- test: int-pair-field-write -->
```maxon
typealias IntPair is Pair with (int, int)

function main() returns int
  var p = IntPair{key: 0, value: 0}
  p.key = 20
  p.value = 22
  return p.key + p.value
end 'main'
```
```exitcode
42
```

<!-- test: int-pair-as-param -->
```maxon
typealias IntPair is Pair with (int, int)

function sum(p IntPair) returns int
  return p.key + p.value
end 'sum'

function main() returns int
  var p = IntPair{key: 10, value: 32}
  return sum(p)
end 'main'
```
```exitcode
42
```

<!-- test: int-pair-as-return -->
```maxon
typealias IntPair is Pair with (int, int)

function makePair(k int, v int) returns IntPair
  return {key: k, value: v}
end 'makePair'

function main() returns int
  var p = makePair(10, v: 32)
  return p.key + p.value
end 'main'
```
```exitcode
42
```

<!-- test: mixed-type-pair -->
```maxon
typealias MixedPair is Pair with (int, float)

function main() returns int
  var p = MixedPair{key: 40, value: 2.5}
  return p.key + trunc(p.value)
end 'main'
```
```exitcode
42
```
