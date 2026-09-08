---
feature: closure-self
status: experimental
keywords: [closure, self, capture, instance-method, gives]
category: functions
---
# Closures Capturing `self`

## Documentation

A closure declared inside an instance method may reference `self` (and therefore `self.field` and `self.method(...)`) inside its body. `self` is captured the same way any other struct local is — by reference to the slot holding the heap pointer — so the closure observes the current state of the instance when it runs.

```text
type Counter
    export var value as Integer

    function getReader() returns (() returns Integer)
        return function() gives self.value
    end 'getReader'
end 'Counter'
```

`self` is only in scope where it is in scope of the enclosing function. A closure inside a free function or static method cannot reference `self` and will fail to compile with `E2001`.

## Tests

<!-- test: bare-self-field -->
### Closure body reads self.field
A closure inside an instance method captures `self` and reads a field through it. The compiler must accept the reference and emit a working env-load.
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

type Holder
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'

	export function readViaClosure() returns Integer
		return apply(function(_ Integer) gives self.value, x: 0)
	end 'readViaClosure'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(7)
	return h.readViaClosure()
end 'main'
```
```exitcode
7
```

<!-- test: self-method-call-in-closure -->
### Closure body calls self.method
A closure may invoke an instance method on the captured `self`. This proves the captured value is a usable struct receiver, not just a field-load source.
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

type Doubler
	export var base as Integer

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'

	function doubled() returns Integer
		return self.base + self.base
	end 'doubled'

	export function run() returns Integer
		return apply(function(_ Integer) gives self.doubled(), x: 0)
	end 'run'
end 'Doubler'

function main() returns ExitCode
	let d = Doubler.create(5)
	return d.run()
end 'main'
```
```exitcode
10
```

<!-- test: bare-self-field-name-in-closure -->
### Closure body reads a self-field by bare name
Inside an instance method, a field may be referenced by its bare name (the compiler implicitly prepends `self`). This must keep working inside a closure body: the bare name resolves to a field read on the captured `self`, identical to the explicit `self.field` form. This is the shape the compiler's own logging closures use (`function() gives "...{field}..."`).
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function() returns Integer
function callIt(f FnTypeAlias1) returns Integer
	return f()
end 'callIt'

type Widget
	export var n as Integer
	export var bump as Integer

	static function create(n Integer, bump Integer) returns Self
		return Self{n: n, bump: bump}
	end 'create'

	export function compute() returns Integer
		// `n` and `bump` are bare self-field names captured through `self`.
		return callIt(function() gives n + bump)
	end 'compute'
end 'Widget'

function main() returns ExitCode
	let w = Widget.create(4, bump: 6)
	return w.compute()
end 'main'
```
```exitcode
10
```

<!-- test: error-self-in-free-function-closure -->
### `self` in a free-function closure still errors
The fix only enables `self` capture when the enclosing function actually has `self`. A closure inside a free function must still be rejected with E2001.
```maxon
function main() returns ExitCode
	let f = function() gives self
	return 0
end 'main'
```
```maxoncstderr
error E2001: specs/fragments/closure-self/error-self-in-free-function-closure.test:3:27: 'self' can only be used inside instance methods
```
