---
feature: map
status: stable
keywords: map, dictionary, hash, key-value, contains, insert, remove, get
category: collections
---
# Map

## Documentation

A `map` is a collection that stores key-value pairs. It provides fast lookup, insertion, and removal using hash-based indexing.

## Creating a Map

Use map literal syntax with key-value pairs:

```text
var scores = [1: 100, 2: 85, 3: 92]                 // map<int,int>
var lookup = [10: 1000, 20: 2000, 30: 3000]         // map<int,int>
```

The key and value types are automatically inferred from the literal values.

You can also create an empty map with a type alias:

```text
typealias Integer = int(i64.min to i64.max)
typealias IntIntMap = Map with (Integer, Integer)
var m = IntIntMap.create()
```

## Methods

### insert(key, value) throws MapError

Add a key-value pair to the map. Throws `MapError.keyAlreadyExists` if the key is already present.

```text
var m = [1: 100, 2: 200]
try m.insert(3, value: 300) otherwise ignore    // Map now has {1: 100, 2: 200, 3: 300}
try m.insert(1, value: 150) otherwise ignore    // Throws MapError.keyAlreadyExists
```

### upsert(key, value)

Insert or update a key-value pair. If the key already exists, updates the value.

```text
var m = [1: 100, 2: 200]
m.upsert(3, value: 300)    // Map now has {1: 100, 2: 200, 3: 300}
m.upsert(1, value: 150)    // Updates key 1 to 150
```

### get(key) returns Value throws MapError

Get the value for a key. Throws `MapError.keyNotFound` if the key is not in the map.

```text
var m = [10: 5, 20: 3]
var v = try m.get(10) otherwise 0    // 5
var w = try m.get(30) otherwise 0    // 0 (key not found, fallback used)
```

### contains(key) returns bool

Check if a key exists in the map. Returns `true` if found, `false` otherwise.

```text
var m = [1: 10, 2: 20]
m.contains(1)      // true
m.contains(3)      // false
```

### remove(key) returns bool

Remove a key-value pair from the map. Returns `true` if the key was present and removed, `false` if it wasn't in the map.

```text
var m = [1: 10, 2: 20, 3: 30]
m.remove(2)        // Returns true, map is now {1: 10, 3: 30}
m.remove(9)        // Returns false, key wasn't present
```

### count() returns int

Get the number of key-value pairs in the map.

```text
var m = [1: 10, 2: 20, 3: 30]
m.count()          // 3
```

### capacity() returns int

Get the current capacity (number of slots) of the internal hash table.

```text
var m = [1: 100]
m.capacity()       // 16 (initial capacity)
```

## Automatic Growth

The map automatically grows when the load factor (count/capacity) exceeds 75%. When this happens, the capacity doubles and all entries are rehashed.

## Tests

<!-- test: literal.basic -->
```maxon
function main() returns ExitCode
	let m = [1: 10, 2: 20, 3: 30]
	return m.count()
end 'main'
```
```exitcode
3
```

<!-- test: literal.int-keys -->
```maxon
function main() returns ExitCode
	let m = [1: 10, 2: 20, 3: 30]
	let result = try m.get(2) otherwise 0
	return result
end 'main'
```
```exitcode
20
```

<!-- test: contains.true -->
```maxon
function main() returns ExitCode
	let m = [10: 100, 20: 200, 30: 300]
	if m.contains(20) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: contains.false -->
```maxon
function main() returns ExitCode
	let m = [10: 100, 20: 200, 30: 300]
	if m.contains(40) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: get.existing -->
```maxon
function main() returns ExitCode
	let m = [1: 10, 2: 20, 3: 30]
	let result = try m.get(2) otherwise 0
	return result
end 'main'
```
```exitcode
20
```

<!-- test: get.missing -->
```maxon
function main() returns ExitCode
	let m = [1: 10, 2: 20]
	let result = try m.get(0) otherwise 0
	return result
end 'main'
```
```exitcode
0
```

<!-- test: insert.new -->
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20]
	try m.insert(3, value: 30) otherwise ignore
	return m.count()
end 'main'
```
```exitcode
3
```

<!-- test: upsert.update -->
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20]
	m.upsert(1, value: 100)
	let result = try m.get(1) otherwise 0
	return result
end 'main'
```
```exitcode
100
```

<!-- test: insert.then-contains -->
```maxon
function main() returns ExitCode
	var m = [10: 1]
	try m.insert(20, value: 2) otherwise ignore
	if m.contains(20) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: remove.existing -->
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20, 3: 30]
	let removed = m.remove(2)
	if removed 'check'
		return m.count()
	end 'check'
	return 1
end 'main'
```
```exitcode
2
```

<!-- test: remove.nonexistent -->
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20]
	let removed = m.remove(99)
	if removed 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: remove.then-contains -->
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20, 3: 30]
	_ = m.remove(2)
	if m.contains(2) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: empty-map.from-syntax -->
```maxon
function main() returns ExitCode
	var m = [0: 0]
	_ = m.remove(0)
	try m.insert(1, value: 100) otherwise ignore
	let result = try m.get(1) otherwise 0
	return result
end 'main'
```
```exitcode
100
```

<!-- test: single-entry -->
```maxon
function main() returns ExitCode
	let m = [42: 99]
	let result = try m.get(42) otherwise 0
	return result
end 'main'
```
```exitcode
99
```

<!-- test: negative-keys -->
```maxon
function main() returns ExitCode
	let m = [-5: 50, -3: 30, -1: 10]
	let result = try m.get(-3) otherwise 0
	return result
end 'main'
```
```exitcode
30
```

<!-- test: remove-reinsert -->
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20, 3: 30]
	_ = m.remove(2)
	try m.insert(2, value: 99) otherwise ignore
	let result = try m.get(2) otherwise 0
	return result
end 'main'
```
```exitcode
99
```

<!-- test: map-type-in-field -->
```maxon
typealias StrMap = Map with (String, String)

type Container
	export var data StrMap

	static function create(data StrMap) returns Self
		return Self{data: data}
	end 'create'
end 'Container'

function main() returns ExitCode
	var m = StrMap.create()
	try m.insert("key", value: "val") otherwise ignore
	let c = Container.create(data: m)
	let result = try c.data.get("key") otherwise ""
	if result == "val" 'check'
		return 42
	end 'check'
	return 0
end 'main'
```
```exitcode
42
```
<!-- test: string-keys-basic -->
```maxon
function main() returns ExitCode
	let m = ["a": 1, "b": 2]
	let result = try m.get("a") otherwise 0
	return result
end 'main'
```
```exitcode
1
```

<!-- test: string-keys-get-multiple -->
```maxon
function main() returns ExitCode
	let m = ["hello": 10, "world": 20, "foo": 30]
	let a = try m.get("hello") otherwise 0
	let b = try m.get("world") otherwise 0
	return a + b
end 'main'
```
```exitcode
30
```

<!-- test: string-keys-contains -->
```maxon
function main() returns ExitCode
	let m = ["key1": 100, "key2": 200]
	if m.contains("key1") 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: string-keys-upsert-update -->
```maxon
function main() returns ExitCode
	var m = ["x": 10]
	m.upsert("x", value: 99)
	let result = try m.get("x") otherwise 0
	return result
end 'main'
```
```exitcode
99
```

<!-- test: string-keys-remove -->
```maxon
function main() returns ExitCode
	var m = ["alpha": 1, "beta": 2, "gamma": 3]
	_ = m.remove("beta")
	if m.contains("beta") 'check'
		return 1
	end 'check'
	return m.count()
end 'main'
```
```exitcode
2
```

<!-- test: string-keys-early-return -->
```maxon
function main() returns ExitCode
	let m = ["test": 42]
	let v = try m.get("test") otherwise 0
	return v
end 'main'
```
```exitcode
42
```

<!-- test: multiline-map-literal -->
```maxon
function main() returns ExitCode
	let m = [
		1: 100,
		2: 200,
		3: 300
	]
	return m.count()
end 'main'
```
```exitcode
3
```

<!-- test: map-literal-with-enum-values -->
```maxon
enum TokenKind
	Function
	Var
end 'TokenKind'

function main() returns ExitCode
	let keywords = ["function": TokenKind.Function, "var": TokenKind.Var]
	let kind = try keywords.get("function") otherwise TokenKind.Var
	match kind 'match'
		Function then return 1
		Var then return 2
	end 'match'
end 'main'
```
```exitcode
1
```

<!-- test: insert.duplicate-throws -->
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20]
	try m.insert(1, value: 99) otherwise 'err'
		return 42
	end 'err'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: insert.duplicate-error-binding -->
```maxon
function main() returns ExitCode
	var m = [1: 10]
	try m.insert(1, value: 99) otherwise (e) 'err'
		match e 'check'
			keyAlreadyExists then return 1
			keyNotFound then return 2
		end 'check'
	end 'err'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: insert.duplicate-does-not-update -->
```maxon
function main() returns ExitCode
	var m = [1: 10]
	try m.insert(1, value: 99) otherwise ignore
	let result = try m.get(1) otherwise 0
	return result
end 'main'
```
```exitcode
10
```

<!-- test: upsert.new-key -->
```maxon
function main() returns ExitCode
	var m = [1: 10]
	m.upsert(2, value: 20)
	return m.count()
end 'main'
```
```exitcode
2
```

<!-- test: upsert.existing-key -->
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20]
	m.upsert(1, value: 100)
	let result = try m.get(1) otherwise 0
	return result
end 'main'
```
```exitcode
100
```

<!-- test: upsert.then-contains -->
```maxon
function main() returns ExitCode
	var m = [10: 1]
	m.upsert(20, value: 2)
	if m.contains(20) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: upsert.preserves-count -->
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20]
	m.upsert(1, value: 100)
	return m.count()
end 'main'
```
```exitcode
2
```

<!-- test: function-valued.dispatch -->
A `Map<String, FunctionType>` lets the caller dispatch by string key.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = (Integer) returns Integer
typealias HandlerMap = Map with String, UnaryOp

function double(x Integer) returns Integer
	return x * 2
end 'double'

function triple(x Integer) returns Integer
	return x * 3
end 'triple'

function main() returns ExitCode
	var m = HandlerMap.create()
	m.upsert(key: "d", value: double)
	m.upsert(key: "t", value: triple)
	let f = try m.get("d") otherwise panic("missing 'd'")
	let g = try m.get("t") otherwise panic("missing 't'")
	return f(7) + g(7)
end 'main'
```
```exitcode
35
```

<!-- test: for-in.nested -->
Nested for-in loops on the same map must not corrupt each other's iteration state.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntMap = Map with (Integer, Integer)

function innerSum(m IntMap) returns Integer
	var total = 0
	for (_, v) in m 'inner'
		total = total + v
	end 'inner'
	return total
end 'innerSum'

function main() returns ExitCode
	let m = [1: 1, 2: 2, 3: 3]
	var outerTotal = 0
	for (_, v) in m 'outer'
		outerTotal = outerTotal + v + innerSum(m)
	end 'outer'
	// Each outer iteration: v + innerSum = v + 6
	// (1+6) + (2+6) + (3+6) = 7+8+9 = 24
	if outerTotal == 24 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```
