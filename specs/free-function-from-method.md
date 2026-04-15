---
feature: free-function-from-method
status: stable
keywords: [free function, method, type, scope resolution]
category: type-system
---

# Free Function Called from Method

## Documentation

A method inside a type can call a free (module-level) function defined in the same file. The compiler must not assume that all unqualified calls inside a type body refer to methods of that type.

## Tests

<!-- test: method-calls-free-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(n Integer) returns Integer
	return n * 2
end 'double'

type Box
	export var value Integer

	export static function create(v Integer) returns Box
		return Box{value: v}
	end 'create'

	export function doubled() returns Integer
		return double(self.value)
	end 'doubled'
end 'Box'

function main() returns ExitCode
	let b = Box.create(21)
	return b.doubled()
end 'main'
```
```exitcode
42
```
