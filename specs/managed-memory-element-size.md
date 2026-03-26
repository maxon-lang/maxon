---
feature: managed-memory-element-size
status: stable
keywords: [managed-memory, element-size, intrinsics]
category: dev
---

## Documentation

Arrays internally use managed memory with element sizes that determine how much space
each element occupies. The runtime correctly handles element sizing when creating and
growing arrays, ensuring elements are stored and retrieved at the correct positions.

## Tests

<!-- test: element-size-respected-in-create -->
Test that element sizes are respected when creating arrays.
Array elements should be stored and retrieved correctly at their positions.
```maxon
function main() returns ExitCode
	var arr = [0]
	arr.push(65)
	arr.push(66)
	arr.push(67)

	let b0 = try arr.get(1) otherwise 0
	let b1 = try arr.get(2) otherwise 0
	let b2 = try arr.get(3) otherwise 0

	if b0 != 65 'check0'
		return 1
	end 'check0'
	if b1 != 66 'check1'
		return 2
	end 'check1'
	if b2 != 67 'check2'
		return 3
	end 'check2'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: element-size-in-grow -->
Test that growing an array preserves existing elements and allows new ones.
```maxon
function main() returns ExitCode
	var arr = [0]
	arr.push(10)
	arr.push(20)
	arr.push(30)
	arr.push(40)
	arr.push(50)

	var sum = try arr.get(1) otherwise 0
	sum = sum + (try arr.get(2) otherwise 0)
	sum = sum + (try arr.get(3) otherwise 0)
	sum = sum + (try arr.get(4) otherwise 0)
	sum = sum + (try arr.get(5) otherwise 0)

	// 10 + 20 + 30 + 40 + 50 = 150
	return sum
end 'main'
```
```exitcode
150
```
