---
feature: parameter-labels
status: stable
keywords: [parameters, named-arguments, arguments, call-site, default-values]
category: core
---

# Named Arguments

## Documentation

In function and method calls, the first argument is positional and every subsequent argument must be named using `name: value` syntax. The first argument carries no label; labels on the remaining arguments improve clarity at the call site by making each parameter's role explicit.

### First Argument Positional, Rest Named

The first argument is passed positionally. Every argument after the first must be named:

```maxon
typealias Score = int(i64.min to i64.max)

function add(a Score, b Score) returns Score
	return a + b
end 'add'

function main() returns ExitCode
	return add(3, b: 4)
end 'main'
```
```exitcode
7
```


### Named Arguments in Any Order

After the first (positional) argument, named arguments can appear in any order:

```maxon
typealias Score = int(i64.min to i64.max)

function subtract(a Score, b Score) returns Score
	return a - b
end 'subtract'

function main() returns ExitCode
	return subtract(10, b: 3)
end 'main'
```
```exitcode
7
```


### Default Parameter Values

Parameters with default values can be omitted. The first parameter is still passed positionally:

```maxon
typealias Score = int(i64.min to i64.max)

function repeat(value Score, times Score = 1) returns Score
	return value * times
end 'repeat'

function main() returns ExitCode
	return repeat(7, times: 6)
end 'main'
```
```exitcode
42
```


## Tests

<!-- test: named-args -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(3, b: 4)
end 'main'
```
```exitcode
7
```

<!-- test: named-args-multiply -->
```maxon

typealias Integer = int(i64.min to i64.max)

function multiply(x Integer, y Integer) returns Integer
	return x * y
end 'multiply'

function main() returns ExitCode
	return multiply(6, y: 7)
end 'main'
```
```exitcode
42
```

<!-- test: default-param-named -->
```maxon

typealias Integer = int(i64.min to i64.max)

function repeat(value Integer, times Integer = 1) returns Integer
	return value * times
end 'repeat'

function main() returns ExitCode
	return repeat(7, times: 6)
end 'main'
```
```exitcode
42
```

<!-- test: default-param-omitted -->
```maxon

typealias Integer = int(i64.min to i64.max)

function repeat(value Integer, times Integer = 2) returns Integer
	return value * times
end 'repeat'

function main() returns ExitCode
	return repeat(21)
end 'main'
```
```exitcode
42
```

<!-- test: error-missing-param-name -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(3, 4)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/parameter-labels/error-missing-param-name.test:10:9: Second and subsequent arguments must be named. Use 'name: value' syntax
```

<!-- test: error-unknown-param-name -->
```maxon

typealias Integer = int(i64.min to i64.max)

function greet(name Integer, suffix Integer) returns Integer
	return name + suffix
end 'greet'

function main() returns ExitCode
	return greet(42, person: 1)
end 'main'
```
```maxoncstderr
error E3003: specs/fragments/parameter-labels/error-unknown-param-name.test:10:19: unknown parameter name: 'person'
```

<!-- test: error-first-arg-named -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(a: 3, b: 4)
end 'main'
```
```maxoncstderr
error E2052: specs/fragments/parameter-labels/error-first-arg-named.test:10:13: first arg cannot be named
```

