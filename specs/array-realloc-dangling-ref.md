---
feature: array-realloc-dangling-ref
status: stable
keywords: [array, realloc, dangling, reference, memory-safety, growth, counted]
category: memory
---
# Array Realloc Dangling Reference

## Documentation

When an array grows, its backing buffer may be reallocated to a new address. A value obtained before the growth (e.g., from `arr.get(index)`) holds its own counted reference to the element, so it stays valid however the source is mutated afterwards: grown, reallocated or cleared.

## Tests

<!-- test: string-ref-survives-array-growth -->
### A string read survives growth and clear
Get a string from an array, push enough elements to reallocate the buffer several times, then clear the array. The string read first is still intact.
```maxon
function main() returns ExitCode
	var arr = ["hello world {40 + 2} this is a long string for heap allocation"]
	let s = try arr.get(0) otherwise ""

	// Push enough elements to force multiple growths (capacity: 4 -> 8 -> 16 -> 32)
	var i = 0 as Count
	while i < 30 'grow'
		arr.push("padding string that is long enough for heap allocation too")
		i = i + 1
	end 'grow'

	arr.clear()

	// The original reference must still work
	if s == "hello world 42 this is a long string for heap allocation" 'check'
		print("{s}\n")
		print("{s.byteLength()}\n")
		return 0
	end 'check'
	print("FAIL: got {s}\n")
	return 1
end 'main'
```
```exitcode
0
```
```stdout
hello world 42 this is a long string for heap allocation
56
```

<!-- test: struct-ref-survives-array-growth -->
### A struct read survives growth and clear
Get a struct from an array, grow the array, then clear it. The struct and its string field are still intact.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var name as String
	export var value as Integer

	static function create(name String, value Integer) returns Self
		return Self{name: name, value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
	var arr = ItemArray.create()
	arr.push(Item.create("first item {40 + 2} with a long name for heap allocation", value: 42))

	view item = try arr.get(0) otherwise Item.create("", value: 0)

	// Force many growths
	var i = 0 as Integer
	while i < 30 'grow'
		arr.push(Item.create("padding item with long name for heap allocation too", value: i))
		i = i + 1
	end 'grow'

	arr.clear()

	// The original reference must still work
	if item.name == "first item 42 with a long name for heap allocation" 'check_name'
		if item.value == 42 'check_val'
			print("{item.name} {item.value}\n")
			return 0
		end 'check_val'
	end 'check_name'
	print("FAIL: name={item.name} value={item.value}\n")
	return 1
end 'main'
```
```exitcode
0
```
```stdout
first item 42 with a long name for heap allocation 42
```

<!-- test: multiple-refs-survive-array-growth -->
### Several reads survive growth and clear
Get several elements, grow the array, then clear it. Every value read first is still intact.
```maxon
function main() returns ExitCode
	var arr = ["alpha {40 + 2} string that is long enough for heap allocation purposes",
						 "beta {40 + 2} string that is long enough for heap allocation purposes",
						 "gamma {40 + 2} string that is long enough for heap allocation purposes"]

	let a = try arr.get(0) otherwise ""
	let b = try arr.get(1) otherwise ""
	let c = try arr.get(2) otherwise ""

	// Force many growths
	var i = 0 as Count
	while i < 30 'grow'
		arr.push("padding string that is long enough for heap allocation too x")
		i = i + 1
	end 'grow'

	arr.clear()

	// All original references must still work
	if a == "alpha 42 string that is long enough for heap allocation purposes" 'a'
		if b == "beta 42 string that is long enough for heap allocation purposes" 'b'
			if c == "gamma 42 string that is long enough for heap allocation purposes" 'c'
				print("{a}\n{b}\n{c}\n")
				return 0
			end 'c'
		end 'b'
	end 'a'
	print("FAIL\n")
	return 1
end 'main'
```
```exitcode
0
```
```stdout
alpha 42 string that is long enough for heap allocation purposes
beta 42 string that is long enough for heap allocation purposes
gamma 42 string that is long enough for heap allocation purposes
```
