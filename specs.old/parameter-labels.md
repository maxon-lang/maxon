---
feature: parameter-labels
status: stable
keywords: [parameters, named-arguments, arguments, call-site, default-values]
category: core
---

# Named Arguments

## Developer Notes

Maxon uses named arguments:
- All parameters are positional by default
- Callers can optionally name any argument using `name = value` syntax
- Parameters with default values can ONLY be provided via named arguments (not positionally)
- Positional arguments must come before named arguments
- Named arguments can appear in any order

Implementation:
- `FunctionParameter` has `name`, `type`, and `defaultValue` fields
- `CallArgument` has `label` field for named arguments at call sites
- Parser parses parameters as `name type [= default]`
- Call sites use `name = value` syntax for named arguments
- Semantic validation in `semantic_analyzer_expr.cpp`:
  1. Processes positional args first, matching required params in order
  2. Skips params with defaults when matching positional args
  3. Named args can match any param by name (any order)
  4. Validates no positional args after named args
  5. Tracks filled params and validates all required params are provided
- `CallExprAST.argToParamMapping` maps call-site args to parameter positions for codegen reordering

Key rules:
1. **Positional by default**: `function foo(x int)` allows `foo(5)` at call site
2. **Optional naming**: Caller can use `foo(x = 5)` if desired
3. **Default params are named-only**: Parameters with defaults cannot be passed positionally
4. **Positional before named**: `foo(1, x = 2)` is valid, `foo(x = 2, 1)` is an error
5. **Named args any order**: `foo(b = 2, a = 1)` is valid even if `a` is declared first

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

