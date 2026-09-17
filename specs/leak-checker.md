---
feature: leak-checker
status: experimental
keywords: [leak, memory, allocation, debug]
category: memory
---

# Leak Checker

## Documentation

The Maxon runtime counts live heap allocations, and checks the count when the program finishes. The check is compiled into every program that uses the heap.

This is a diagnostic tool for verifying that the compiler's automatic memory management is working correctly. Since Maxon manages memory automatically through reference counting and scope-based cleanup, leaks indicate a compiler bug rather than a user error.

The leak checker runs after `main()` returns and before the process exits. A clean program keeps the exit code `main` returned.

### Reporting

The checker prints nothing. It reports through the exit code, which REPLACES the one `main` returned:

| Exit code | Meaning |
|-----------|---------|
| `101` | The live heap allocation count is not zero — an allocation was never freed, or one was freed too often. |
| `75` | The heap is clean, but the live green-thread count is not zero — a spawned green thread was neither awaited nor dropped, or was reclaimed twice. |

The two counts are checked separately and never summed, so a green-thread over-reclaim cannot cancel a heap under-release. When both are wrong, the heap's `101` is reported.

## Tests

<!-- test: no-leak.trivial -->
### Trivial program has no leaks
A program with no heap allocations should produce no leak output.
```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: no-leak.string-scope -->
### Strings cleaned up at scope exit
Heap-allocated strings should be properly freed when they go out of scope.
```maxon
function make_string()
	let s = "this string is long enough to require heap allocation for sure and even more text"
	print(s)
end 'make_string'

function main() returns ExitCode
	make_string()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
this string is long enough to require heap allocation for sure and even more text
```

<!-- test: no-leak.array-scope -->
### Arrays cleaned up at scope exit
Arrays should be properly freed when they go out of scope.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function use_array()
	var arr = IntArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
end 'use_array'

function main() returns ExitCode
	use_array()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: no-leak.string-return -->
### Returned strings transfer ownership
A string returned from a function should not leak.
```maxon
function greet(name String) returns String
	return "Hello, {name}!"
end 'greet'

function main() returns ExitCode
	let msg = greet("World")
	print(msg)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Hello, World!
```

<!-- test: no-leak.exit-code-preserved -->
### Exit code is preserved
A program that leaks nothing keeps the exit code `main` returned; only a leak replaces it.
```maxon
function main() returns ExitCode
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: no-leak.if-let-try-union-map -->
### `if let X = try map.get(K)` on union-valued maps
Assigning to an outer `var` from a `match` destructuring of a value bound by
`if let X = try map.get(K) 'L'` must release both the bound union and the
extracted inner payload: the if-let block's scope-end cleanup runs for X even
when control falls through the assignment.
```maxon
typealias TypeNameId = int(0 to u64.max)
typealias Integer = int(0 to 100)

union MaxonType
	boolean
	integer
	named(id TypeNameId)
end 'MaxonType'

union ReturnType
	void
	value(inner MaxonType)
end 'ReturnType'

typealias TypeMap = Map with (String, ReturnType)

function dispatch(m TypeMap, key String) returns MaxonType
	var resultType = MaxonType.integer
	if let rt = try m.get(key) 'haveRT'
		resultType = match rt 'unwrap'
			void gives MaxonType.integer
			value(inner) gives inner
		end 'unwrap'
	end 'haveRT'
	return resultType
end 'dispatch'

function decode(t MaxonType) returns Integer
	return match t 'unwrap'
		named(id) gives id as Integer
		boolean gives 1
		integer gives 2
	end 'unwrap'
end 'decode'

function main() returns ExitCode
	var m = TypeMap.create()
	m.upsert("a", value: ReturnType.value(MaxonType.named(42)))
	let t = dispatch(m, key: "a")
	return decode(t)
end 'main'
```
```exitcode
42
```

<!-- test: no-leak.enum-arg-conditional-use -->
### Enum arguments cleaned up when conditionally used
When a function receives two heap-allocated enum arguments but only uses one
based on a condition, the unused argument must still be freed by the caller.
```maxon
typealias Integer = int(i64.min to i64.max)

union Op
	addOp(left Integer, right Integer)
	mulOp(left Integer, right Integer)
end 'Op'

function applyOp(useAdd bool, addArg Op, mulArg Op) returns Integer
	if useAdd 'branch'
		return match addArg 'matchAdd'
			addOp(left, right) gives left + right
			mulOp(left, right) gives left * right
		end 'matchAdd'
	end 'branch' else 'other'
		return match mulArg 'matchMul'
			addOp(left, right) gives left + right
			mulOp(left, right) gives left * right
		end 'matchMul'
	end 'other'
end 'applyOp'

function main() returns ExitCode
	let result = applyOp(true, addArg: Op.addOp(3, right: 4), mulArg: Op.mulOp(5, right: 6))
	print("{result}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: no-leak.closure-returns-managed -->
### Function-value (closure) results are freed at last use
A function-value call whose result is a heap-allocated `String` (built inside the
closure body from a captured value) must have that result released at its last
use — the indirect-call result owns a `+1` exactly like a direct call. Before the
fix, `lowerIndirectCall` never classified the result, so every closure-returned
String leaked; the compiler's own `logInfo`/`logDebug`/`logTrace` `function() gives
"..."` message thunks were the dominant offenders.
```maxon
typealias LazyMessage = function() returns String

function callThunk(message LazyMessage)
	let msg = message()
	print("{msg}\n")
end 'callThunk'

function main() returns ExitCode
	let captured = "captured payload long enough to require a real heap allocation"
	callThunk(function() gives "thunk: {captured}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
thunk: captured payload long enough to require a real heap allocation
```
