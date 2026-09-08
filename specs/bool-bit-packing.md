---
feature: bool-bit-packing
status: stable
keywords: [bool, bit-packing, array, managed-memory]
category: types
---
# Bool Bit-Packing

## Documentation

When `Array with bool` stores elements, they are bit-packed: each bool occupies a single bit instead of a full byte, giving 8x memory reduction. This is transparent to the user — the Array API works identically. **Every** `Array with bool` is packed the same way, whether built from a constant literal, a non-constant literal, or dynamically via `Array.create()` + `push`.

`bool` is the first *sub-byte-packed* element type. The `__ManagedMemory` record marks such an element with a **negative** `elementSize`: the value is `-(bit width)`, so `bool` is `elementSize = -1` (1 bit per element). The general encoding is `elementSize > 0` = byte stride, `elementSize == 0` = unset/non-container, `elementSize < 0` = `(-elementSize)` bits per element. A future 2-bit or nibble type would reuse the same machinery with `-2` / `-4`. Element `i`'s field is at bit `i * width`, i.e. byte `(i * width) >> 3`, bit offset `(i * width) & 7`; widths are restricted to those dividing 8 (1, 2, 4) so a field never straddles a byte boundary.

A *constant* bool-array literal (e.g. `[true, false, true]`) is lowered to a bit-packed `.rdata` buffer (`elementSize = -1`). Structurally mutating such a literal (`remove` / `insert` / `slice` / `append`) must copy-on-write the read-only buffer and perform the shift/copy at *bit* granularity — scaling those operations by a byte `elementSize` would corrupt the data. Dynamically-built bool arrays get `elementSize = -1` at their first `grow`, stamped from the layout descriptor's `bitPacked` flag, so they take the identical bit-addressed path. Non-bool narrow elements (Byte, u16, u32) keep their positive byte stride.

## Tests

<!-- test: push-and-get -->
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var arr = BoolArray.create()
	arr.push(true)
	arr.push(false)
	arr.push(true)
	let v0 = try arr.get(0) otherwise false
	let v1 = try arr.get(1) otherwise true
	let v2 = try arr.get(2) otherwise false
	var sum = 0
	if v0 'c0'
		sum = sum + 1
	end 'c0'
	if v1 'c1'
		sum = sum + 1
	end 'c1'
	if v2 'c2'
		sum = sum + 1
	end 'c2'
	return sum
end 'main'
```
```exitcode
2
```

<!-- test: set-bit -->
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var arr = BoolArray.create()
	arr.push(false)
	arr.push(false)
	arr.push(false)
	try arr.set(1, value: true) otherwise panic("test invariant: set OOB")
	let v = try arr.get(1) otherwise false
	if v 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: cross-byte-boundary -->
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var arr = BoolArray.create()
	var i = 0
	while i < 9 'push'
		arr.push(false)
		i = i + 1
	end 'push'
	try arr.set(7, value: true) otherwise panic("test invariant: set OOB")
	try arr.set(8, value: true) otherwise panic("test invariant: set OOB")
	let v7 = try arr.get(7) otherwise false
	let v8 = try arr.get(8) otherwise false
	var sum = 0
	if v7 'c7'
		sum = sum + 1
	end 'c7'
	if v8 'c8'
		sum = sum + 1
	end 'c8'
	return sum
end 'main'
```
```exitcode
2
```

<!-- test: large-array -->
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var arr = BoolArray.create()
	var i = 0
	while i < 100 'push'
		arr.push(i < 50)
		i = i + 1
	end 'push'
	var count = 0
	var j = 0
	while j < 100 'count'
		let v = try arr.get(j) otherwise false
		if v 'inc'
			count = count + 1
		end 'inc'
		j = j + 1
	end 'count'
	return count
end 'main'
```
```exitcode
50
```

<!-- test: insert -->
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var arr = BoolArray.create()
	arr.push(true)
	arr.push(true)
	arr.insert(1, value: false)
	let v0 = try arr.get(0) otherwise false
	let v1 = try arr.get(1) otherwise true
	let v2 = try arr.get(2) otherwise false
	var sum = 0
	if v0 'c0'
		sum = sum + 1
	end 'c0'
	if v1 'c1'
		sum = sum + 10
	end 'c1'
	if v2 'c2'
		sum = sum + 1
	end 'c2'
	return sum
end 'main'
```
```exitcode
2
```

<!-- test: remove -->
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var arr = BoolArray.create()
	arr.push(true)
	arr.push(false)
	arr.push(true)
	let removed = try arr.remove(1) otherwise true
	let v0 = try arr.get(0) otherwise false
	let v1 = try arr.get(1) otherwise false
	var sum = 0
	if removed 'r'
		sum = sum + 10
	end 'r'
	if v0 'c0'
		sum = sum + 1
	end 'c0'
	if v1 'c1'
		sum = sum + 1
	end 'c1'
	return sum
end 'main'
```
```exitcode
2
```

<!-- test: iterate -->
```maxon
function main() returns ExitCode
	let arr = [true, false, true, true, false]
	var count = 0
	for v in arr 'loop'
		if v 'check'
			count = count + 1
		end 'check'
	end 'loop'
	return count
end 'main'
```
```exitcode
3
```

<!-- test: iterate-aliased-bool -->
```maxon
// Regression: iterating an Array<bool> via for-in loads each element from the
// bit-packed buffer and branches on it. The get path has to return a real bool
// (i1), not a raw i64 {0,1} value, or cond_br fails to lower (StdI64→StdBool cast).
// Also exercises the ForLoopIteratorElisionPass bool path: the optimized loop
// reads directly from __ManagedMemory with EmitBitGetAsBool.
typealias BoolArray = Array with bool

function main() returns ExitCode
	var arr = BoolArray.create()
	arr.push(true)
	arr.push(false)
	arr.push(true)
	arr.push(false)
	arr.push(true)
	arr.push(true)
	arr.push(false)
	arr.push(true)
	arr.push(true)
	// Iterate and count trues via boolean composition, not just `if v`.
	// The `not v` path stresses the StdBool typing — `not` is defined on i1.
	var trueCount = 0
	var consecutiveTrue = 0
	var maxRun = 0
	for v in arr 'scan'
		if v 'hit'
			trueCount = trueCount + 1
			consecutiveTrue = consecutiveTrue + 1
			if consecutiveTrue > maxRun 'grow'
				maxRun = consecutiveTrue
			end 'grow'
		end 'hit'
		if not v 'miss'
			consecutiveTrue = 0
		end 'miss'
	end 'scan'
	// Expect 6 trues, longest run of 2.
	return trueCount * 10 + maxRun
end 'main'
```
```exitcode
62
```

<!-- test: clear -->
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var arr = BoolArray.create()
	arr.push(true)
	arr.push(false)
	arr.clear()
	let c = arr.count()
	arr.push(true)
	let c2 = arr.count()
	return c * 10 + c2
end 'main'
```
```exitcode
1
```

<!-- test: count -->
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var arr = BoolArray.create()
	arr.push(true)
	arr.push(false)
	arr.push(true)
	return arr.count()
end 'main'
```
```exitcode
3
```

<!-- test: pop -->
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var arr = BoolArray.create()
	arr.push(true)
	arr.push(false)
	arr.push(true)
	let popped = try arr.pop() otherwise false
	var sum = 0
	if popped 'p'
		sum = sum + 1
	end 'p'
	return sum * 10 + arr.count()
end 'main'
```
```exitcode
12
```

<!-- test: clone -->
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var arr = BoolArray.create()
	arr.push(true)
	arr.push(false)
	var copy = arr.clone()
	try copy.set(0, value: false) otherwise panic("test invariant: set OOB")
	let orig = try arr.get(0) otherwise false
	let cloned = try copy.get(0) otherwise true
	var sum = 0
	if orig 'o'
		sum = sum + 1
	end 'o'
	if cloned 'c'
		sum = sum + 10
	end 'c'
	return sum
end 'main'
```
```exitcode
1
```

<!-- test: literal -->
```maxon
function main() returns ExitCode
	let arr = [true, false, true, false]
	let v0 = try arr.get(0) otherwise false
	let v2 = try arr.get(2) otherwise false
	var sum = 0
	if v0 'c0'
		sum = sum + 1
	end 'c0'
	if v2 'c2'
		sum = sum + 1
	end 'c2'
	return sum
end 'main'
```
```exitcode
2
```

<!-- test: mutable-literal-cow -->
```maxon
function main() returns ExitCode
	var arr = [true, false, true]
	try arr.set(1, value: true) otherwise panic("test invariant: set OOB")
	let v1 = try arr.get(1) otherwise false
	if v1 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: slice -->
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var arr = BoolArray.create()
	arr.push(true)
	arr.push(false)
	arr.push(true)
	arr.push(false)
	arr.push(true)
	let sliced = try arr.slice(1, endIndex: 4) otherwise return 99
	var sum = 0
	let v0 = try sliced.get(0) otherwise true
	let v1 = try sliced.get(1) otherwise false
	let v2 = try sliced.get(2) otherwise true
	if v0 'c0'
		sum = sum + 10
	end 'c0'
	if v1 'c1'
		sum = sum + 1
	end 'c1'
	if v2 'c2'
		sum = sum + 10
	end 'c2'
	return sum
end 'main'
```
```exitcode
1
```

<!-- test: literal-remove -->
```maxon
// Structurally mutate a CONSTANT bool-array literal (bit-packed rdata,
// element_size 0). remove must copy-on-write the read-only buffer and shift the
// tail DOWN one bit; the byte-scaled shift would be a no-op and leave the removed
// element in place while length is decremented -> corrupted bits.
typealias BoolArray = Array with bool

function printBits(a BoolArray)
	for b in a 'each'
		print("1" if b else "0")
	end 'each'
	print(" len={a.count()}\n")
end 'printBits'

function main() returns ExitCode
	var a = [true, false, true, false, true]
	_ = try a.remove(1) otherwise panic("test invariant: remove OOB")
	printBits(a)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1101 len=4
```

<!-- test: literal-slice -->
```maxon
// Slice a CONSTANT bool-array literal. The new record must stay bit-packed
// (element_size 0), allocate a bit-sized buffer, and copy bits [start, end) at
// bit granularity — the byte-scaled copy would move zero bytes and return a
// length-N record over an all-false buffer.
typealias BoolArray = Array with bool

function printBits(a BoolArray)
	for b in a 'each'
		print("1" if b else "0")
	end 'each'
	print(" len={a.count()}\n")
end 'printBits'

function main() returns ExitCode
	let c = [true, false, true, false, true]
	let s = try c.slice(1, endIndex: 4) otherwise panic("test invariant: slice OOB")
	printBits(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
010 len=3
```

<!-- test: literal-insert -->
```maxon
// insert into a CONSTANT bool-array literal exercises the bit-packed grow (buffer
// sized in bits, not bytes) plus shift_right at bit granularity plus the bit-set.
typealias BoolArray = Array with bool

function printBits(a BoolArray)
	for b in a 'each'
		print("1" if b else "0")
	end 'each'
	print(" len={a.count()}\n")
end 'printBits'

function main() returns ExitCode
	var d = [true, true, true]
	d.insert(1, value: false)
	printBits(d)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1011 len=4
```

<!-- test: literal-append -->
```maxon
// append onto a CONSTANT bool-array literal grows the bit-packed buffer (sized in
// bits) and copies the other literal's bits onto the end.
typealias BoolArray = Array with bool

function printBits(a BoolArray)
	for b in a 'each'
		print("1" if b else "0")
	end 'each'
	print(" len={a.count()}\n")
end 'printBits'

function main() returns ExitCode
	var e = [true, false]
	let f = [true, true]
	e.append(f)
	printBits(e)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1011 len=4
```

<!-- test: literal-remove-crossbyte -->
```maxon
// Edge cases for the bit-packed remove shift-down loop: removing index 0 starts
// the loop at the first bit, and removing from a 9-element literal shifts bits
// across the byte-0/byte-1 boundary (bit 8 -> bit 7). A byte-scaled shift would
// no-op both; the bit-loop must slide every trailing bit down one position.
typealias BoolArray = Array with bool

function printBits(a BoolArray)
	for b in a 'each'
		print("1" if b else "0")
	end 'each'
	print(" len={a.count()}\n")
end 'printBits'

function main() returns ExitCode
	var front = [false, true, true]
	_ = try front.remove(0) otherwise panic("test invariant: remove OOB")
	printBits(front)

	var wide = [true, false, true, false, true, false, true, false, true]
	_ = try wide.remove(4) otherwise panic("test invariant: remove OOB")
	printBits(wide)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11 len=2
10100101 len=8
```
