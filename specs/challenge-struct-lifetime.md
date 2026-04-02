---
feature: challenge-struct-lifetime
status: stable
keywords: struct, lifetime, return, stack, heap
category: semantics
---
# Challenge Struct Lifetime

## Documentation

## Struct Lifetime

Structs returned from functions must have their data survive beyond the function's stack frame.

## Tests

<!-- test: return-struct-with-computed-fields -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Result
	export var sum Integer
	export var product Integer

	static function create(sum Integer, product Integer) returns Self
		return Self{sum: sum, product: product}
	end 'create'
end 'Result'

function compute(a Integer, b Integer) returns Result
	return Result.create(sum: a + b, product: a * b)
end 'compute'

function main() returns ExitCode
	var r = compute(3, b: 4)
	return r.sum + r.product
end 'main'
```
```exitcode
19
```

<!-- test: pass-struct-to-function-and-return -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Counter'

function increment(c Counter) returns Counter
	return Counter.create(value: c.value + 1)
end 'increment'

function main() returns ExitCode
	var c1 = Counter.create(value: 10)
	var c2 = increment(c1)
	return c2.value
end 'main'
```
```exitcode
11
```

<!-- test: multiple-struct-returns -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Value
	export var n Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Value'

function step1() returns Value
	return Value.create(n: 1)
end 'step1'

function step2(v Value) returns Value
	return Value.create(n: v.n + 10)
end 'step2'

function step3(v Value) returns Value
	return Value.create(n: v.n + 100)
end 'step3'

function main() returns ExitCode
	var v1 = step1()
	var v2 = step2(v1)
	var v3 = step3(v2)
	return v3.n
end 'main'
```
```exitcode
111
```
