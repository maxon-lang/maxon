---
feature: managed-memory-builtin
status: selfhosted
keywords: [managed_memory, builtin, slab, allocator, memory]
category: memory-safety
---

# `__ManagedMemory` builtin

End-to-end tests for the `__ManagedMemory` builtin struct and its method dispatch
(steps 6b.1 - 6b.6). These tests exercise the registered methods (`create`,
`length`, `capacity`, `setLength`, `get`, `set`, `grow`, `append`, `slice`,
`byteAt`, `setByte`, `clear`) plus the throw-on-bounds-error path through
`mrt_set_error` / `try ... otherwise`.

These tests run only under the self-hosted compiler. The C# bootstrap compiler
does not register the `__ManagedMemory.*` method signatures.

## Tests

<!-- test: create-and-length -->
`__ManagedMemory.create(4, elementSize: 8)` allocates a struct with capacity 4
and length 0. We verify both fields read back through the `length()` and
`capacity()` getters (which lower to inline `loadIndirect` ops).
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 8) otherwise return 1
	if mm.length() != 0 'badLen'
		return 2
	end 'badLen'
	if mm.capacity() != 4 'badCap'
		return 3
	end 'badCap'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: set-and-get -->
After `setLength(3)` we set three values and read them back; all three must
match. Exercises both the `set` and `get` runtime helpers' word path
(elementSize=8).
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(3, elementSize: 8) otherwise return 1
	try mm.setLength(3) otherwise return 2
	try mm.set(0, value: 11) otherwise return 3
	try mm.set(1, value: 22) otherwise return 3
	try mm.set(2, value: 33) otherwise return 3
	let a = try mm.get(0) otherwise return 4
	let b = try mm.get(1) otherwise return 4
	let c = try mm.get(2) otherwise return 4
	if a != 11 'badA'
		return 5
	end 'badA'
	if b != 22 'badB'
		return 6
	end 'badB'
	if c != 33 'badC'
		return 7
	end 'badC'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: setlength-then-get -->
`create(8) + setLength(3)` makes length 3 while capacity stays 8. After three
sets, length and capacity are independently observable.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(8, elementSize: 8) otherwise return 1
	try mm.setLength(3) otherwise return 2
	try mm.set(0, value: 100) otherwise return 3
	try mm.set(1, value: 200) otherwise return 3
	try mm.set(2, value: 300) otherwise return 3
	if mm.length() != 3 'badLen'
		return 4
	end 'badLen'
	if mm.capacity() != 8 'badCap'
		return 5
	end 'badCap'
	let v = try mm.get(2) otherwise return 6
	if v != 300 'badVal'
		return 7
	end 'badVal'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: grow -->
`grow(8)` raises capacity from 2 to 8. Previously written values must still be
readable through the (potentially relocated) buffer.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(2, elementSize: 8) otherwise return 1
	try mm.setLength(2) otherwise return 2
	try mm.set(0, value: 77) otherwise return 3
	try mm.set(1, value: 88) otherwise return 3
	try mm.grow(8) otherwise return 4
	if mm.capacity() != 8 'badCap'
		return 5
	end 'badCap'
	let a = try mm.get(0) otherwise return 6
	let b = try mm.get(1) otherwise return 6
	if a != 77 'badA'
		return 7
	end 'badA'
	if b != 88 'badB'
		return 8
	end 'badB'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: append -->
Appending another `__ManagedMemory` concatenates its bytes onto the end of the
receiver. Length grows by `other.length`, capacity grows automatically if
needed.
```maxon
function main() returns ExitCode
	let a = try __ManagedMemory.create(4, elementSize: 8) otherwise return 1
	try a.setLength(2) otherwise return 2
	try a.set(0, value: 1) otherwise return 3
	try a.set(1, value: 2) otherwise return 3
	let b = try __ManagedMemory.create(4, elementSize: 8) otherwise return 4
	try b.setLength(2) otherwise return 5
	try b.set(0, value: 3) otherwise return 6
	try b.set(1, value: 4) otherwise return 6
	try a.append(b) otherwise return 7
	if a.length() != 4 'badLen'
		return 8
	end 'badLen'
	let v0 = try a.get(0) otherwise return 9
	let v1 = try a.get(1) otherwise return 9
	let v2 = try a.get(2) otherwise return 9
	let v3 = try a.get(3) otherwise return 9
	if v0 != 1 'badV0'
		return 10
	end 'badV0'
	if v1 != 2 'badV1'
		return 11
	end 'badV1'
	if v2 != 3 'badV2'
		return 12
	end 'badV2'
	if v3 != 4 'badV3'
		return 13
	end 'badV3'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slice -->
`slice(1, endIndex: 4)` produces a fresh `__ManagedMemory` of length 3 (and
capacity 3), holding a copy of indices 1..3 of the source.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(5, elementSize: 8) otherwise return 1
	try mm.setLength(5) otherwise return 2
	try mm.set(0, value: 10) otherwise return 3
	try mm.set(1, value: 20) otherwise return 3
	try mm.set(2, value: 30) otherwise return 3
	try mm.set(3, value: 40) otherwise return 3
	try mm.set(4, value: 50) otherwise return 3
	let s = try mm.slice(1, endIndex: 4) otherwise return 4
	if s.length() != 3 'badLen'
		return 5
	end 'badLen'
	let v0 = try s.get(0) otherwise return 6
	let v1 = try s.get(1) otherwise return 6
	let v2 = try s.get(2) otherwise return 6
	if v0 != 20 'badV0'
		return 7
	end 'badV0'
	if v1 != 30 'badV1'
		return 8
	end 'badV1'
	if v2 != 40 'badV2'
		return 9
	end 'badV2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: byte-at -->
A byte-sized `__ManagedMemory` (`elementSize: 1`) supports `setByte` /
`byteAt`. Each byte is independently addressable.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(8, elementSize: 1) otherwise return 1
	try mm.setLength(8) otherwise return 2
	try mm.setByte(0, value: 11) otherwise return 3
	try mm.setByte(1, value: 22) otherwise return 3
	try mm.setByte(7, value: 99) otherwise return 3
	let a = try mm.byteAt(0) otherwise return 4
	let b = try mm.byteAt(1) otherwise return 4
	let z = try mm.byteAt(7) otherwise return 4
	if a != 11 'badA'
		return 5
	end 'badA'
	if b != 22 'badB'
		return 6
	end 'badB'
	if z != 99 'badZ'
		return 7
	end 'badZ'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: clear -->
`clear()` sets length to 0 without touching capacity. After clear, `length()`
must read 0 and `capacity()` must still report the original allocated count.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 8) otherwise return 1
	try mm.setLength(3) otherwise return 2
	try mm.set(0, value: 42) otherwise return 3
	try mm.set(1, value: 43) otherwise return 3
	try mm.set(2, value: 44) otherwise return 3
	mm.clear()
	if mm.length() != 0 'badLen'
		return 4
	end 'badLen'
	if mm.capacity() != 4 'badCap'
		return 5
	end 'badCap'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: out-of-bounds-get -->
`get(99)` on a length-1 `__ManagedMemory` triggers the index-out-of-bounds
error path. The `try ... otherwise -1` branch must execute and the function
must return -1 cast through ExitCode.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(2, elementSize: 8) otherwise return 1
	try mm.setLength(1) otherwise return 2
	try mm.set(0, value: 7) otherwise return 3
	let _ = try mm.get(99) otherwise return 0
	return 5
end 'main'
```
```exitcode
0
```

<!-- test: out-of-bounds-set -->
`set(99, value: 0)` on a length-1 `__ManagedMemory` triggers the OOB path.
The `try ... otherwise return 0` branch executes; the trailing return must not
fire.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(2, elementSize: 8) otherwise return 1
	try mm.setLength(1) otherwise return 2
	try mm.set(99, value: 42) otherwise return 0
	return 5
end 'main'
```
```exitcode
0
```

<!-- test: slice-out-of-bounds -->
`slice(0, endIndex: 99)` triggers the slice-out-of-bounds error path (error
2). The `otherwise return 0` branch must execute.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 8) otherwise return 1
	try mm.setLength(2) otherwise return 2
	let _ = try mm.slice(0, endIndex: 99) otherwise return 0
	return 5
end 'main'
```
```exitcode
0
```

