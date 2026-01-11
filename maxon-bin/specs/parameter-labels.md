---
feature: parameter-labels
status: stable
keywords: [parameters, named-arguments, arguments, call-site, default-values]
category: core
---

# Named Arguments

## Documentation

All function and method calls require named arguments using colon syntax. This improves code clarity at call sites by explicitly naming parameter values.

### Named Arguments (Required)

All arguments must be named using `name: value` syntax:

```maxon
function add(a int, b int) returns int
    return a + b
end 'add'

function main() returns int
    return add(a: 3, b: 4)
end 'main'
```
```exitcode
7
```


### Named Arguments in Any Order

Named arguments can appear in any order:

```maxon
function subtract(a int, b int) returns int
    return a - b
end 'subtract'

function main() returns int
    return subtract(b: 3, a: 10)
end 'main'
```
```exitcode
7
```


### Default Parameter Values

Parameters with default values can be omitted:

```maxon
function repeat(value int, times int = 1) returns int
    return value * times
end 'repeat'

function main() returns int
    return repeat(value: 7, times: 6)
end 'main'
```
```exitcode
42
```


## Tests

<!-- test: named-args -->
```maxon
function add(a int, b int) returns int
    return a + b
end 'add'

function main() returns int
    return add(a: 3, b: 4)
end 'main'
```
```exitcode
7
```

<!-- test: named-args-multiply -->
```maxon
function multiply(x int, y int) returns int
    return x * y
end 'multiply'

function main() returns int
    return multiply(x: 6, y: 7)
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
    return subtract(b: 3, a: 10)
end 'main'
```
```exitcode
7
```

<!-- test: method-named-arg -->
```maxon
type Counter
    export var value int

    function add(amount int) returns Counter
        return Counter{value: value + amount}
    end 'add'
end 'Counter'

function main() returns int
    var c = Counter{value: 10}
    c = c.add(amount: 5)
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
    return repeat(value: 7, times: 6)
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
    return repeat(value: 21)
end 'main'
```
```exitcode
42
```

<!-- test: error-missing-param-name -->
```maxon
function add(a int, b int) returns int
    return a + b
end 'add'

function main() returns int
    return add(3, 4)
end 'main'
```
```maxoncstderr
error E052: specs/fragments/parameter-labels.error-missing-param-name.1.test:7:5: Second and subsequent arguments must be named. Use 'name: value' syntax
```

<!-- test: error-unknown-param-name -->
```maxon
function greet(name int) returns int
    return name
end 'greet'

function main() returns int
    return greet(person: 42)
end 'main'
```
```maxoncstderr
error E045: specs/fragments/parameter-labels.error-unknown-param-name.1.test:7:5: unknown parameter name: 'person'
```

