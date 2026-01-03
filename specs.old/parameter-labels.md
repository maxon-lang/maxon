---
feature: parameter-labels
status: stable
keywords: [parameters, named-arguments, arguments, call-site, default-values]
category: core
---

# Named Arguments

## Documentation

Named arguments improve code clarity at call sites by explicitly naming parameter values.

### Positional Arguments

By default, all parameters accept positional arguments:

```maxon
function add(a int, b int) returns int
    return a + b
end 'add'

function main() returns int
    return add(3, 4)
end 'main'
```
```exitcode
7
```


### Named Arguments

Callers can optionally name any argument using `=`:

```maxon
function multiply(x int, y int) returns int
    return x * y
end 'multiply'

function main() returns int
    return multiply(x = 6, y = 7)
end 'main'
```
```exitcode
42
```


### Named Arguments in Any Order

Named arguments can appear in any order after positional arguments:

```maxon
function subtract(a int, b int) returns int
    return a - b
end 'subtract'

function main() returns int
    return subtract(b = 3, a = 10)
end 'main'
```
```exitcode
7
```


### Default Parameter Values

Parameters with default values must be provided via named arguments:

```maxon
function repeat(value int, times int = 1) returns int
    return value * times
end 'repeat'

function main() returns int
    return repeat(7, times = 6)
end 'main'
```
```exitcode
42
```


## Tests

<!-- test: positional-args -->
```maxon
function add(a int, b int) returns int
    return a + b
end 'add'

function main() returns int
    return add(3, 4)
end 'main'
```
```exitcode
7
```

<!-- test: named-args -->
```maxon
function multiply(x int, y int) returns int
    return x * y
end 'multiply'

function main() returns int
    return multiply(x = 6, y = 7)
end 'main'
```
```exitcode
42
```

<!-- test: named-args-any-order -->
```maxon
function subtract(a int, b int) returns int
    return a - b
end 'subtract'

function main() returns int
    return subtract(b = 3, a = 10)
end 'main'
```
```exitcode
7
```

<!-- test: mixed-positional-named -->
```maxon
function process(value int, scale int) returns int
    return value * scale
end 'process'

function main() returns int
    return process(5, scale = 3)
end 'main'
```
```exitcode
15
```

<!-- test: method-named-arg -->
```maxon
type Counter
    var value int

    function add(amount int) returns Counter
        return Counter{value: value + amount}
    end 'add'
end 'Counter'

function main() returns int
    var c = Counter{value: 10}
    c = c.add(amount = 5)
    return c.value
end 'main'
```
```exitcode
15
```

<!-- test: default-param-named -->
```maxon
function repeat(value int, times int = 1) returns int
    return value * times
end 'repeat'

function main() returns int
    return repeat(7, times = 6)
end 'main'
```
```exitcode
42
```

<!-- test: default-param-omitted -->
```maxon
function repeat(value int, times int = 2) returns int
    return value * times
end 'repeat'

function main() returns int
    return repeat(21)
end 'main'
```
```exitcode
42
```

<!-- test: error-positional-after-named -->
```maxon
function add(a int, b int) returns int
    return a + b
end 'add'

function main() returns int
    return add(a = 3, 4)
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:7:23
Positional argument after named argument
  All positional arguments must come before named arguments

  7 |     return add(a = 3, 4)
    |                       ^

Semantic Error: temp_fragment.maxon:7:12
Missing required argument for parameter 'b'
  Add: b = <value>

  7 |     return add(a = 3, 4)
    |            ^
```

<!-- test: error-unknown-param-name -->
```maxon
function greet(name int) returns int
    return name
end 'greet'

function main() returns int
    return greet(person = 42)
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:7:18
Unknown parameter name 'person'
  Function 'greet' has no parameter with this name

  7 |     return greet(person = 42)
    |                  ^

Semantic Error: temp_fragment.maxon:7:12
Missing required argument for parameter 'name'
  Add: name = <value>

  7 |     return greet(person = 42)
    |            ^
```

<!-- test: error-default-positional -->
```maxon
function repeat(value int, times int = 1) returns int
    return value * times
end 'repeat'

function main() returns int
    return repeat(7, 6)
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:7:22
Too many positional arguments
  Function 'repeat' has 1 required parameter

  7 |     return repeat(7, 6)
    |                      ^
```

