---
feature: nil-optional-types
status: stable
keywords: [nil, optional, if let, else unwrap]
category: types
---

# Nil and Optional Types

## Documentation

Optional types represent values that may or may not be present. Use `T or nil` to declare an optional type, where `T` is any type.

### Syntax

```maxon
function mayFail() returns int or nil
    return nil
end 'mayFail'

function maySucceed() returns int or nil
    return 42
end 'maySucceed'
```

### If-Let Unwrapping

Safely unwrap optional values with `if let`:

```maxon
var result = mayFail()
if let value = result 'check'
    // value is unwrapped int here
    return value
end 'check' else 'nil_case'
    // result was nil
    return 0
end 'nil_case'
```

### Else-Unwrap with Default

Provide a default value when the optional is nil:

```maxon
var result = mayFail() else 'default'
    result = 10  // Must assign default value
end 'default'
// result is guaranteed to be int (non-optional) here
```

### Type Safety

The compiler prevents using optional values without unwrapping:

```maxon
var x = mayFail()
return x + 5  // ERROR: Cannot use optional without unwrapping
```

## Tests

<!-- test: nil-literal -->
```maxon
function returnsNil() returns int or nil
	return nil
end 'returnsNil'

function main() returns int
	var x = returnsNil()
	if let val = x 'check'
		return val
	end 'check' else 'nil_case'
		return 99
	end 'nil_case'
end 'main'
```
```exitcode
99
```

<!-- test: optional-with-value -->
```maxon
function returnsValue() returns int or nil
	return 42
end 'returnsValue'

function main() returns int
	var x = returnsValue()
	if let val = x 'check'
		return val
	end 'check' else 'nil_case'
		return 0
	end 'nil_case'
end 'main'
```
```exitcode
42
```

<!-- test: iflet-then-branch -->
```maxon
function safeDivide(a int, b int) returns int or nil
	if b == 0 'check'
		return nil
	end 'check'
	return trunc(a / b)
end 'safeDivide'

function main() returns int
	var opt = safeDivide(10, 2)
	if let result = opt 'unwrap'
		return result
	end 'unwrap' else 'nil_case'
		return 999
	end 'nil_case'
end 'main'
```
```exitcode
5
```

<!-- test: iflet-else-branch -->
```maxon
function safeDivide(a int, b int) returns int or nil
	if b == 0 'check'
		return nil
	end 'check'
	return trunc(a / b)
end 'safeDivide'

function main() returns int
	var opt = safeDivide(10, 0)
	if let result = opt 'unwrap'
		return result
	end 'unwrap' else 'nil_case'
		return 88
	end 'nil_case'
end 'main'
```
```exitcode
88
```

<!-- test: else-unwrap-nil -->
```maxon
function returnsNil() returns int or nil
	return nil
end 'returnsNil'

function main() returns int
	var opt = returnsNil()
	var result = opt else 'default'
		result = 7
	end 'default'
	return result
end 'main'
```
```exitcode
7
```

<!-- test: else-unwrap-value -->
```maxon
function returnsValue() returns int or nil
	return 5
end 'returnsValue'

function main() returns int
	var opt = returnsValue()
	var result = opt else 'default'
		result = 99
	end 'default'
	return result
end 'main'
```
```exitcode
5
```

<!-- test: implicit-wrapping -->
```maxon
function makeOptional(x int) returns int or nil
	return x
end 'makeOptional'

function main() returns int
	var opt = makeOptional(33)
	if let val = opt 'check'
		return val
	end 'check'
	return 0
end 'main'
```
```exitcode
33
```

<!-- test: multiple-optionals -->
```maxon
function first() returns int or nil
	return 10
end 'first'

function second() returns int or nil
	return nil
end 'second'

function main() returns int
	var a = first()
	var b = second()

	var sum = 0
	if let val = a 'checkA'
		sum = val
	end 'checkA'

	if let val = b 'checkB'
		sum = sum + val
	end 'checkB'

	return sum
end 'main'
```
```exitcode
10
```

<!-- test: nested-iflet -->
```maxon
function makeOpt(n int) returns int or nil
	return n
end 'makeOpt'

function main() returns int
	var a = makeOpt(5)
	if let x = a 'checkOuter'
		var b = makeOpt(3)
		if let y = b 'checkInner'
			return x + y
		end 'checkInner'
	end 'checkOuter'
	return 0
end 'main'
```
```exitcode
8
```

<!-- test: error.use-optional-without-unwrap -->
```maxon
function returnsOptional() returns int or nil
	return 42
end 'returnsOptional'

function main() returns int
	var x = returnsOptional()
	return x + 5
end 'main'
```
```maxoncstderr
error E038: specs/fragments/nil-optional-types.error.use-optional-without-unwrap.1.test:8:2: optional type used without unwrapping: 'x'
```

<!-- test: error.return-nil-to-non-optional -->
```maxon
function main() returns int
	return nil
end 'main'
```
```maxoncstderr
error E041: specs/fragments/nil-optional-types.error.return-nil-to-non-optional.1.test:3:2: cannot return nil from non-optional: 'int'
```

<!-- test: error.iflet-non-optional -->
```maxon
function main() returns int
	var x = 42
	if let val = x 'check'
		return val
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E039: specs/fragments/nil-optional-types.error.iflet-non-optional.1.test:4:2: if let requires optional type: ''x' has type 'int', not optional'
```

<!-- test: error.else-unwrap-non-optional -->
```maxon
function main() returns int
	var x = 42
	var result = x else 'default'
		result = 99
	end 'default'
	return result
end 'main'
```
```maxoncstderr
error E040: specs/fragments/nil-optional-types.error.else-unwrap-non-optional.1.test:4:2: else unwrap requires optional type: ''x' has type 'int', not optional'
```

<!-- test: error.else-unwrap-missing-assignment -->
```maxon
function returnsNil() returns int or nil
	return nil
end 'returnsNil'

function main() returns int
	var opt = returnsNil()
	var result = opt else 'default'
		var temp = 7
	end 'default'
	return result
end 'main'
```
```maxoncstderr
error E044: specs/fragments/nil-optional-types.error.else-unwrap-missing-assignment.1.test:8:2: else unwrap missing assignment: 'result'
```

<!-- test: error.nested-optional -->
```maxon
function broken() returns int or nil or nil
	return nil
end 'broken'
```
```maxoncstderr
error E002: specs/fragments/nil-optional-types.error.nested-optional.1.test:2:38: unexpected token: 'or'
```

<!-- test: optional-param-nil -->
```maxon
function checkValue(x int or nil) returns int
	if let val = x 'check'
		return val
	end 'check' else 'nil_case'
		return 99
	end 'nil_case'
end 'checkValue'

function main() returns int
	return checkValue(nil)
end 'main'
```
```exitcode
99
```

<!-- test: optional-param-value -->
```maxon
function checkValue(x int or nil) returns int
	if let val = x 'check'
		return val
	end 'check' else 'nil_case'
		return 99
	end 'nil_case'
end 'checkValue'

function main() returns int
	return checkValue(42)
end 'main'
```
```exitcode
42
```

<!-- test: optional-param-implicit-wrap -->
```maxon
function add(a int, b int or nil) returns int
	var result = b else 'default'
		result = 0
	end 'default'
	return a + result
end 'add'

function main() returns int
	return add(5, 10)
end 'main'
```
```exitcode
15
```

<!-- test: optional-struct-field-nil -->
```maxon
type Person
	export var name String
	export var age int or nil
end 'Person'

function main() returns int
	var p = Person{name: "Bob", age: nil}
	if let a = p.age 'check'
		return a
	end 'check' else 'nil_case'
		return 99
	end 'nil_case'
end 'main'
```
```exitcode
99
```

<!-- test: optional-struct-field-value -->
```maxon
type Person
	export var name String
	export var age int or nil
end 'Person'

function main() returns int
	var p = Person{name: "Alice", age: 30}
	if let a = p.age 'check'
		return a
	end 'check' else 'nil_case'
		return 0
	end 'nil_case'
end 'main'
```
```exitcode
30
```

<!-- test: optional-struct-field-implicit-wrap -->
```maxon
type Point
	export var x int
	export var y int or nil
end 'Point'

function main() returns int
	var p = Point{x: 10, y: 20}
	var result = p.y else 'default'
		result = 0
	end 'default'
	return p.x + result
end 'main'
```
```exitcode
30
```
<!-- test: iflet-array-access-borrowed -->
<!-- TrackMemory: true -->
```maxon
function main() returns int
    var arr = [10, 20, 30]
    if let val = arr[1] 'check'
        return val
    end 'check'
    return 0
end 'main'
```
```exitcode
20
```
```stdout
ALLOC #1: 24 bytes (set buffer)
FREE #1: 24 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 24 bytes
Freed:     24 bytes
Leaked:    0 bytes
Moves:     0
Increfs:   0
Decrefs:   0
```

<!-- test: iflet-array-string-borrowed -->
<!-- TrackMemory: true -->
```maxon
function main() returns int
    var arr = ["hello", "world"]
    if let s = arr[0] 'check'
        return s.count()
    end 'check'
    return 0
end 'main'
```
```exitcode
5
```
```stdout
ALLOC #1: 14 bytes (string buffer)
MOVE: managed
ALLOC #2: 64 bytes (set buffer)
ALLOC #3: 14 bytes (string buffer)
MOVE: managed
DECREF: <array element> -> rc=0
FREE #1: 14 bytes (string cleanup)
DECREF: <array element> -> rc=0
FREE #3: 14 bytes (string cleanup)
FREE #2: 64 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 92 bytes
Freed:     92 bytes
Leaked:    0 bytes
Moves:     2
Increfs:   0
Decrefs:   2
```

<!-- test: iflet-map-get-borrowed -->
<!-- TrackMemory: true -->
```maxon
function main() returns int
    var m = ["key": 42]
    if let val = m.get("key") 'check'
        return val
    end 'check'
    return 0
end 'main'
```
```exitcode
42
```
```stdout
ALLOC #1: 12 bytes (string buffer)
MOVE: managed
ALLOC #2: 32 bytes (map buffer)
ALLOC #3: 8 bytes (map buffer)
ALLOC #4: 512 bytes (array buffer)
MOVE: managed
ALLOC #5: 128 bytes (array buffer)
MOVE: managed
ALLOC #6: 128 bytes (array buffer)
MOVE: managed
MOVE: ks
MOVE: vs
MOVE: sts
INCREF: <array index String> -> rc=2
INCREF: <array_store> -> rc=3
MOVE: result
FREE #2: 32 bytes (map literal keys cleanup)
FREE #3: 8 bytes (map literal values cleanup)
ALLOC #7: 12 bytes (string buffer)
MOVE: managed
INCREF: <array index String> -> rc=4
DECREF: <temp> -> rc=0
FREE #7: 12 bytes (string cleanup)
DECREF: <map key> -> rc=3
FREE #4: 512 bytes (map keys cleanup)
FREE #5: 128 bytes (map values cleanup)
FREE #6: 128 bytes (map states cleanup)

=== MEMORY STATS ===
Allocated: 832 bytes
Freed:     820 bytes
Leaked:    12 bytes
Moves:     9
Increfs:   3
Decrefs:   2
```

<!-- test: iflet-map-string-value-borrowed -->
<!-- TrackMemory: true -->
```maxon
function main() returns int
    var m = [1: "hello", 2: "world"]
    if let s = m.get(1) 'check'
        return s.count()
    end 'check'
    return 0
end 'main'
```
```exitcode
5
```
```stdout
ALLOC #1: 14 bytes (string buffer)
MOVE: managed
ALLOC #2: 16 bytes (map buffer)
ALLOC #3: 64 bytes (map buffer)
ALLOC #4: 14 bytes (string buffer)
MOVE: managed
ALLOC #5: 128 bytes (array buffer)
MOVE: managed
ALLOC #6: 512 bytes (array buffer)
MOVE: managed
ALLOC #7: 128 bytes (array buffer)
MOVE: managed
MOVE: ks
MOVE: vs
MOVE: sts
INCREF: <array index String> -> rc=2
INCREF: <array_store> -> rc=3
INCREF: <array index String> -> rc=2
INCREF: <array_store> -> rc=3
MOVE: result
FREE #2: 16 bytes (map literal keys cleanup)
FREE #3: 64 bytes (map literal values cleanup)
INCREF: <array index String> -> rc=4
FREE #5: 128 bytes (map keys cleanup)
FREE #6: 512 bytes (map values cleanup)
FREE #7: 128 bytes (map states cleanup)

=== MEMORY STATS ===
Allocated: 876 bytes
Freed:     848 bytes
Leaked:    28 bytes
Moves:     9
Increfs:   5
Decrefs:   0
```

<!-- test: iflet-nested-array-access -->
<!-- TrackMemory: true -->
```maxon
function main() returns int
    var arr = [10, 20, 30]
    var sum = 0
    if let a = arr[0] 'check1'
        sum = sum + a
    end 'check1'
    if let b = arr[1] 'check2'
        sum = sum + b
    end 'check2'
    if let c = arr[2] 'check3'
        sum = sum + c
    end 'check3'
    return sum
end 'main'
```
```exitcode
60
```
```stdout
ALLOC #1: 24 bytes (set buffer)
FREE #1: 24 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 24 bytes
Freed:     24 bytes
Leaked:    0 bytes
Moves:     0
Increfs:   0
Decrefs:   0
```

<!-- test: iflet-early-return-from-map -->
<!-- TrackMemory: true -->
```maxon
function lookup(m Map from String to int, key String) returns int
    if let val = m.get(key) 'found'
        return val
    end 'found'
    return 0 - 1
end 'lookup'

function main() returns int
    var m = ["a": 10, "b": 20, "c": 30]
    return lookup(m, "b")
end 'main'
```
```exitcode
20
```
```stdout
ALLOC #1: 10 bytes (string buffer)
MOVE: managed
ALLOC #2: 96 bytes (map buffer)
ALLOC #3: 24 bytes (map buffer)
ALLOC #4: 10 bytes (string buffer)
MOVE: managed
ALLOC #5: 10 bytes (string buffer)
MOVE: managed
ALLOC #6: 512 bytes (array buffer)
MOVE: managed
ALLOC #7: 128 bytes (array buffer)
MOVE: managed
ALLOC #8: 128 bytes (array buffer)
MOVE: managed
MOVE: ks
MOVE: vs
MOVE: sts
INCREF: <array index String> -> rc=2
INCREF: <array_store> -> rc=3
INCREF: <array index String> -> rc=2
INCREF: <array_store> -> rc=3
INCREF: <array index String> -> rc=2
INCREF: <array_store> -> rc=3
MOVE: result
FREE #2: 96 bytes (map literal keys cleanup)
FREE #3: 24 bytes (map literal values cleanup)
ALLOC #9: 10 bytes (string buffer)
MOVE: managed
INCREF: <array index String> -> rc=4
DECREF: <map key> -> rc=2
DECREF: <map key> -> rc=3
DECREF: <map key> -> rc=2
FREE #6: 512 bytes (map keys cleanup)
FREE #7: 128 bytes (map values cleanup)
FREE #8: 128 bytes (map states cleanup)
DECREF: <temp> -> rc=0
FREE #9: 10 bytes (string cleanup)

=== MEMORY STATS ===
Allocated: 928 bytes
Freed:     898 bytes
Leaked:    30 bytes
Moves:     11
Increfs:   7
Decrefs:   4
```
