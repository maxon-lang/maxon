---
feature: interface-dispatch-throws-match
status: experimental
keywords: [interface, dispatch, throws, match, return, monomorphization]
category: type-system
---

# Interface Dispatch from Throwing Function in Match Arm

## Documentation

When a function that takes an interface-typed parameter also declares `throws`,
calling it via `return try` inside a match arm previously triggered an ICE
(E9001: key not found in dictionary) during MaxonToStandard conversion.

The crash occurred because the monomorphized specialization of the throwing
helper was not correctly wired up when invoked from inside a match arm with
`return try`.

## Tests

<!-- test: interface-dispatch-throws-in-match -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

interface Backend
	function compute(x Integer) returns Integer
end 'Backend'

enum Kind
	alpha
	beta
end 'Kind'

type AlphaBackend implements Backend
	export let tag as Integer

	function compute(x Integer) returns Integer
		return x + 1
	end 'compute'

	export static function create() returns Self
		return Self{tag: 0}
	end 'create'
end 'AlphaBackend'

type BetaBackend implements Backend
	export let tag as Integer

	function compute(x Integer) returns Integer
		return x + 2
	end 'compute'

	export static function create() returns Self
		return Self{tag: 0}
	end 'create'
end 'BetaBackend'

function helperThatThrows(value Integer, backend Backend) returns Integer throws MyError
	let result = backend.compute(value)
	if result < 0 'bad'
		throw MyError.failed
	end 'bad'
	return result
end 'helperThatThrows'

function dispatchWithReturnTry(kind Kind, value Integer) returns Integer throws MyError
	match kind 'dispatch'
		alpha then return try helperThatThrows(value, backend: AlphaBackend.create())
		beta then return try helperThatThrows(value, backend: BetaBackend.create())
	end 'dispatch'
end 'dispatchWithReturnTry'

function main() returns ExitCode
	let a = try dispatchWithReturnTry(Kind.alpha, value: 40) otherwise 0
	let b = try dispatchWithReturnTry(Kind.beta, value: 40) otherwise 0
	return a + b
end 'main'
```
```exitcode
83
```
