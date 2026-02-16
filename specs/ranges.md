---
feature: ranges
status: experimental
keywords: [range, to, upto, strideable, iteration]
category: control-flow
---

## Documentation

# Ranges

Ranges create sequences of values that can be iterated over. They use the `to` (inclusive) and `upto` (exclusive upper bound) keywords.

**Inclusive range** — includes both endpoints:

```text
for i in 1 to 5 'loop'   // iterates: 1, 2, 3, 4, 5
    print("{i}")
end 'loop'
```

**Exclusive range** — excludes the upper bound:

```text
for i in 1 upto 5 'loop'   // iterates: 1, 2, 3, 4
    print("{i}")
end 'loop'
```

Ranges work with any type that implements the `Strideable` interface. The standard library provides `Strideable` conformance for `int` and `Character`.

### The Strideable Interface

```text
interface Strideable
    function advancedBy(n int) returns Self
end 'Strideable'
```

Types implementing `Strideable` can be used with range expressions. The `advancedBy` method advances the value by `n` steps.

### Range Types

The standard library defines two range types:

- `Range uses Bound` — inclusive range (`start to end`), implements `Iterable with Bound`
- `OpenRange uses Bound` — exclusive upper bound (`start upto end`), implements `Iterable with Bound`

Both require `Bound` to implement `Strideable` and `Comparable`.

## Tests

<!-- test: ranges.basic-inclusive -->
```maxon
function main() returns ExitCode
    var sum = 0
    for i in 1 to 5 'loop'
        sum = sum + i
    end 'loop'
    return sum
end 'main'
```
```exitcode
15
```

<!-- test: ranges.basic-exclusive -->
```maxon
function main() returns ExitCode
    var sum = 0
    for i in 1 upto 5 'loop'
        sum = sum + i
    end 'loop'
    return sum
end 'main'
```
```exitcode
10
```

<!-- test: ranges.zero-start -->
```maxon
function main() returns ExitCode
    var sum = 0
    for i in 0 to 3 'loop'
        sum = sum + i
    end 'loop'
    return sum
end 'main'
```
```exitcode
6
```

<!-- test: ranges.single-element-inclusive -->
```maxon
function main() returns ExitCode
    var count = 0
    for i in 5 to 5 'loop'
        count = count + 1
    end 'loop'
    return count
end 'main'
```
```exitcode
1
```

<!-- test: ranges.single-element-exclusive -->
```maxon
function main() returns ExitCode
    var count = 0
    for i in 5 upto 6 'loop'
        count = count + 1
    end 'loop'
    return count
end 'main'
```
```exitcode
1
```

<!-- test: ranges.empty-inclusive -->
```maxon
function main() returns ExitCode
    var count = 0
    for i in 5 to 3 'loop'
        count = count + 1
    end 'loop'
    return count
end 'main'
```
```exitcode
0
```

<!-- test: ranges.empty-exclusive -->
```maxon
function main() returns ExitCode
    var count = 0
    for i in 5 upto 5 'loop'
        count = count + 1
    end 'loop'
    return count
end 'main'
```
```exitcode
0
```

<!-- test: ranges.variable-bounds -->
```maxon
function main() returns ExitCode
    var start = 2
    var finish = 4
    var sum = 0
    for i in start to finish 'loop'
        sum = sum + i
    end 'loop'
    return sum
end 'main'
```
```exitcode
9
```

<!-- test: ranges.expression-bounds -->
```maxon

typealias Integer = i64

function getStart() returns Integer
    return 1
end 'getStart'

function getEnd() returns Integer
    return 4
end 'getEnd'

function main() returns ExitCode
    var sum = 0
    for i in getStart() to getEnd() 'loop'
        sum = sum + i
    end 'loop'
    return sum
end 'main'
```
```exitcode
10
```

<!-- test: ranges.negative-bounds -->
```maxon
function main() returns ExitCode
    var sum = 0
    for i in 0 - 2 to 2 'loop'
        sum = sum + i
    end 'loop'
    return sum
end 'main'
```
```exitcode
0
```

<!-- test: ranges.break-in-range -->
```maxon
function main() returns ExitCode
    var last = 0
    for i in 1 to 100 'loop'
        last = i
        if i == 5 'done'
            break
        end 'done'
    end 'loop'
    return last
end 'main'
```
```exitcode
5
```

<!-- test: ranges.continue-in-range -->
```maxon
function main() returns ExitCode
    var sum = 0
    for i in 1 to 10 'loop'
        if i mod 2 == 0 'skip'
            continue
        end 'skip'
        sum = sum + i
    end 'loop'
    return sum
end 'main'
```
```exitcode
25
```

<!-- test: ranges.nested-ranges -->
```maxon
function main() returns ExitCode
    var sum = 0
    for i in 1 to 3 'outer'
        for j in 1 to 3 'inner'
            sum = sum + i * j
        end 'inner'
    end 'outer'
    return sum
end 'main'
```
```exitcode
36
```

<!-- test: ranges.large-range -->
```maxon
function main() returns ExitCode
    var sum = 0
    for i in 1 to 1000 'loop'
        sum = sum + 1
    end 'loop'
    print("{sum}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1000
```

<!-- test: ranges.character-range -->
```maxon
function main() returns ExitCode
    var count = 0
    for c in 'a' to 'z' 'loop'
        count = count + 1
    end 'loop'
    return count
end 'main'
```
```exitcode
26
```

<!-- test: ranges.character-range-print -->
```maxon
function main() returns ExitCode
    for c in 'a' to 'e' 'loop'
        print("{c}\n")
    end 'loop'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
a
b
c
d
e
```
