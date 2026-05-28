---
feature: closure-capture
status: experimental
keywords: [closure, capture, environment, gives]
category: functions
---
# Closure Variable Capture

## Documentation

Closures can capture variables from their enclosing scope. When a closure references a variable that is not one of its parameters, the variable is captured by reference.

```text
var offset = 10
var f = function(x int) gives x + offset
```

Because captures are by reference, the closure always sees the current value of the captured variable, even if it changes after the closure is created.

This is especially useful with higher-order functions like `map`:

```text
var multiplier = 3
var results = numbers.map(function(x) gives x * multiplier)
```

Use `_` as a parameter name to ignore the parameter:

```text
var values = items.map(function(_) gives defaultValue)
```

## Tests

<!-- test: closure-capture.basic -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let offset = 7
	let result = apply(function(n Integer) gives n + offset, x: 10)
	return result
end 'main'
```
```exitcode
17
```

<!-- test: closure-capture.ignore-param -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let value = 42
	let result = apply(function(_ Integer) gives value, x: 99)
	return result
end 'main'
```
```exitcode
42
```

<!-- test: closure-capture.struct-field -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

type Level
	export var rawValue as Integer

	static function create(rawValue Integer) returns Self
		return Self{rawValue: rawValue}
	end 'create'
end 'Level'

function main() returns ExitCode
	let level = Level.create(5)
	let result = apply(function(_ Integer) gives level.rawValue, x: 0)
	return result
end 'main'
```
```exitcode
5
```

<!-- test: closure-capture.map-with-capture -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Level
	export var rawValue as Integer

	static function create(rawValue Integer) returns Self
		return Self{rawValue: rawValue}
	end 'create'
end 'Level'

function main() returns ExitCode
	let level = Level.create(5)
	let arr = [1, 2, 3]
	let result = arr.map(function(_ Integer) gives level.rawValue)
	return result.count()
end 'main'
```
```exitcode
3
```

<!-- test: closure-capture.multiple-captures -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let a = 10
	let b = 20
	let result = apply(function(x Integer) gives x + a + b, x: 5)
	return result
end 'main'
```
```exitcode
35
```

<!-- test: closure-capture.no-capture-regression -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let result = apply(function(n Integer) gives n * 3, x: 10)
	return result
end 'main'
```
```exitcode
30
```

<!-- test: closure-capture.capture-string -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns String
function apply(f FnTypeAlias1, x Integer) returns String
	return f(x)
end 'apply'

function main() returns ExitCode
	let prefix = "hello"
	let result = apply(function(_ Integer) gives prefix, x: 0)
	print(result)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```


<!-- test: closure-capture.interface-method-with-captured-field -->
A closure declared inside an interface-conforming method body that captures
a `let`-bound copy of a self-field. The method `Box.greet()` is the
interface-witness target for `Greeter.greet`, so the call ABI carries the
boxed self pointer; the inner closure receives an env containing the
captured local `myv` (a copy of `self.v`). Historically the self-hosted
x64 backend's regalloc panicked here with
`colorLookupGpr: vreg v0 in func=Box.greet … NO live range was built for v0`
— a `mov-arg` for the closure's call-arg setup referenced a value the
backend hadn't defined, because the env-pointer arg slot wasn't being
registered alongside the captured-value arg. Compiling at all confirms
the regalloc allocates a live range for the env pointer's arg setup.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet() returns Integer
end 'Greeter'

type Box implements Greeter
	var v as Integer

	static function make(v Integer) returns Self
		return Self{v: v}
	end 'make'

	function greet() returns Integer
		let myv = v
		let adder = function(x Integer) gives x + myv
		return adder(10)
	end 'greet'
end 'Box'

function main() returns ExitCode
	let m = Box.make(5)
	return m.greet()
end 'main'
```
```exitcode
15
```
