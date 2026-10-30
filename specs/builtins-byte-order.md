---
feature: builtins-byte-order
status: stable
keywords: [builtins, __Builtins, byte order, endian, readLittleEndian, readBigEndian, appendLittleEndian, appendBigEndian, byte swap, intrinsics]
category: system
---

# `__Builtins` byte-order intrinsics

## Documentation

Four library-tier intrinsics move a whole 2-, 4- or 8-byte word between a `ByteArray` and a `bits(64)` value
in one memory access, where assembling the word one byte at a time costs a call, a bounds check and a shift
per byte:

| Intrinsic | Meaning |
|---|---|
| `__Builtins.readLittleEndian(source, at, width)` | the `width` bytes of `source` from index `at`, least significant byte first, zero-extended to an `OrderedWord` |
| `__Builtins.readBigEndian(source, at, width)` | the same bytes, most significant byte first |
| `__Builtins.appendLittleEndian(output, word, width)` | appends the low `width` bytes of `word` to `output`, least significant byte first |
| `__Builtins.appendBigEndian(output, word, width)` | appends them, most significant byte first |

`OrderedWord` is `bits(64)`. `width` is an integer CONSTANT at the call site and is 2, 4 or 8: any other value,
and a width known only at run time, is refused with E3005. `source` and `output` must be a
`ByteArray` (E3005), and a call takes exactly three arguments (E3036). Every `__Builtins` member is library-tier,
so a call outside `stdlib/` and `runtime/` is E3185.

A read throws exactly as `ByteArray.get` throws when any of its bytes is out of range: `ArrayError.indexOutOfBounds`,
caught by `try … otherwise …`. The bound is `at + width <= source.count()` decided without overflow, so an `at`
near `u64.max` fails. An append always succeeds: it grows `output` by `width` once, detaching a shared buffer first,
exactly as `width` `push` calls would.

An append writes its `output` argument, and `__Builtins.fillRandom`, `__ManagedFile.read`,
`__ManagedFile.readAppending` and `__ManagedSocket.recv` write their buffer argument. Each follows `push`'s
rules: a read-only buffer, such as a `let` binding, is E3159, and a function that hands one of them its
parameter writes that parameter, so passing that function a read-only value is E3159 too.

## Tests

<!-- test: builtins-byte-order.read-little-endian-at-an-unaligned-index -->
<!-- tier: stdlib -->
A read from a non-zero, unaligned index at each width. Bytes 1..2 are `11 22`, bytes 1..4 are `11 22 33 44`
and bytes 3..10 are `33 44 55 66 77 88 99 AA`; the least significant byte comes first.
```maxon
function main() returns ExitCode
	var data = ByteArray.create()

	for byte in [0x10, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA] 'fill'
		data.push(byte)
	end 'fill'

	let two = try __Builtins.readLittleEndian(data, 1, 2) otherwise panic("a two-byte read inside the array failed")
	let four = try __Builtins.readLittleEndian(data, 1, 4) otherwise panic("a four-byte read inside the array failed")
	let eight = try __Builtins.readLittleEndian(data, 3, 8) otherwise panic("an eight-byte read inside the array failed")
	print("{two as Count} {four as Count} {eight as Count}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8721 1144201745 12293006704283829299
```

<!-- test: builtins-byte-order.read-big-endian-at-an-unaligned-index -->
<!-- tier: stdlib -->
The same three reads, most significant byte first.
```maxon
function main() returns ExitCode
	var data = ByteArray.create()

	for byte in [0x10, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA] 'fill'
		data.push(byte)
	end 'fill'

	let two = try __Builtins.readBigEndian(data, 1, 2) otherwise panic("a two-byte read inside the array failed")
	let four = try __Builtins.readBigEndian(data, 1, 4) otherwise panic("a four-byte read inside the array failed")
	let eight = try __Builtins.readBigEndian(data, 3, 8) otherwise panic("an eight-byte read inside the array failed")
	print("{two as Count} {four as Count} {eight as Count}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4386 287454020 3694171492931115434
```

<!-- test: builtins-byte-order.read-whose-last-byte-is-the-last-byte-of-the-array -->
<!-- tier: stdlib -->
The bound is inclusive of the array's final byte: a four-byte read at 4 and an eight-byte read at 0 of an
eight-byte array both succeed.
```maxon
function main() returns ExitCode
	var data = ByteArray.create()

	for byte in [1, 2, 3, 4, 5, 6, 7, 8] 'fill'
		data.push(byte)
	end 'fill'

	let tail = try __Builtins.readBigEndian(data, 4, 4) otherwise panic("a read ending at the last byte failed")
	let whole = try __Builtins.readLittleEndian(data, 0, 8) otherwise panic("a read of the whole array failed")
	print("{tail as Count} {whole as Count}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
84281096 578437695752307201
```

<!-- test: builtins-byte-order.read-one-byte-past-the-end-fails-the-way-get-fails -->
<!-- tier: stdlib -->
A read whose last byte is one past the array's end fails with the error a checked `get` of that index
throws, so a caller writes the same `try … otherwise …`. The error is named from each side and the two agree.
```maxon
function readFailure(data ByteArray, at Count) returns String
	try __Builtins.readLittleEndian(data, at, 4) otherwise (e) 'caught'
		return "{e.name}"
	end 'caught'

	return "none"
end 'readFailure'

function getFailure(data ByteArray, at Count) returns String
	try data.get(at) otherwise (e) 'caught'
		return "{e.name}"
	end 'caught'

	return "none"
end 'getFailure'

function main() returns ExitCode
	var data = ByteArray.create()

	for byte in [1, 2, 3, 4, 5, 6, 7, 8] 'fill'
		data.push(byte)
	end 'fill'

	print("{readFailure(data, at: 4)} {readFailure(data, at: 5)}\n")
	print("{getFailure(data, at: 7)} {getFailure(data, at: 8)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
none indexOutOfBounds
none indexOutOfBounds
```

<!-- test: builtins-byte-order.read-with-an-index-near-u64-max-does-not-wrap -->
<!-- tier: stdlib -->
`at + width` wraps to a small number for an `at` within `width` of `u64.max`, which a naive
`at + width <= count` accepts. The bound is decided without the addition.
```maxon
function readFailure(data ByteArray, at Count, wide bool) returns String
	if wide 'eight'
		try __Builtins.readBigEndian(data, at, 8) otherwise (e) 'caught'
			return "{e.name}"
		end 'caught'
	end 'eight' else 'two'
		try __Builtins.readBigEndian(data, at, 2) otherwise (e) 'caught'
			return "{e.name}"
		end 'caught'
	end 'two'

	return "none"
end 'readFailure'

function main() returns ExitCode
	var data = ByteArray.create()

	for byte in [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16] 'fill'
		data.push(byte)
	end 'fill'

	let top = u64.max as Count
	let nearTop = (u64.max - 6) as Count
	print("{readFailure(data, at: top, wide: true)} {readFailure(data, at: nearTop, wide: true)} {readFailure(data, at: top, wide: false)} {readFailure(data, at: 14, wide: true)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
indexOutOfBounds indexOutOfBounds indexOutOfBounds indexOutOfBounds
```

<!-- test: builtins-byte-order.append-little-endian-onto-a-non-empty-array -->
<!-- tier: stdlib -->
Each width appends exactly `width` bytes after the existing one, least significant byte first.
```maxon
function main() returns ExitCode
	var out = ByteArray.create()
	out.push(0xEE)
	__Builtins.appendLittleEndian(out, 0x0102, 2)
	__Builtins.appendLittleEndian(out, 0x03040506, 4)
	__Builtins.appendLittleEndian(out, 0x0102030405060708, 8)
	print("{out.count()}:")

	for byte in out 'each'
		print(" {byte}")
	end 'each'

	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
15: 238 2 1 6 5 4 3 8 7 6 5 4 3 2 1
```

<!-- test: builtins-byte-order.append-big-endian-onto-a-non-empty-array -->
<!-- tier: stdlib -->
The same three appends, most significant byte first.
```maxon
function main() returns ExitCode
	var out = ByteArray.create()
	out.push(0xEE)
	__Builtins.appendBigEndian(out, 0x0102, 2)
	__Builtins.appendBigEndian(out, 0x03040506, 4)
	__Builtins.appendBigEndian(out, 0x0102030405060708, 8)
	print("{out.count()}:")

	for byte in out 'each'
		print(" {byte}")
	end 'each'

	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
15: 238 1 2 3 4 5 6 1 2 3 4 5 6 7 8
```

<!-- test: builtins-byte-order.append-keeps-only-the-low-bytes-of-the-word -->
<!-- tier: stdlib -->
Only the low `width` bytes of the word are appended, in the named order, whatever the bytes above them hold.
```maxon
function main() returns ExitCode
	var little = ByteArray.create()
	var big = ByteArray.create()
	__Builtins.appendLittleEndian(little, 0x1122334455, 2)
	__Builtins.appendLittleEndian(little, 0x1122334455, 4)
	__Builtins.appendBigEndian(big, 0x1122334455, 2)
	__Builtins.appendBigEndian(big, 0x1122334455, 4)
	print("{little.count()}:")

	for byte in little 'eachLittle'
		print(" {byte}")
	end 'eachLittle'

	print("\n{big.count()}:")

	for byte in big 'eachBig'
		print(" {byte}")
	end 'eachBig'

	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6: 85 68 85 68 51 34
6: 68 85 34 51 68 85
```

<!-- test: builtins-byte-order.append-onto-an-empty-array -->
<!-- tier: stdlib -->
An array that has never held an element has no buffer; the append allocates one.
```maxon
function main() returns ExitCode
	var out = ByteArray.create()
	__Builtins.appendBigEndian(out, 0xAABBCCDD, 4)
	print("{out.count()}:")

	for byte in out 'each'
		print(" {byte}")
	end 'each'

	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4: 170 187 204 221
```

<!-- test: builtins-byte-order.append-to-a-copy-leaves-the-original-unchanged -->
<!-- tier: stdlib -->
A `clone` shares its source's buffer until either side writes. The append must detach the copy's buffer first,
or it would write through into the original's.
```maxon
function main() returns ExitCode
	var original = ByteArray.create()
	original.reserve(64)

	for byte in [1, 2, 3, 4] 'fill'
		original.push(byte)
	end 'fill'

	var copy = original.clone()
	__Builtins.appendLittleEndian(copy, 0x08070605, 4)
	print("{original.count()} {copy.count()}:")

	for byte in original 'eachOriginal'
		print(" {byte}")
	end 'eachOriginal'

	print(" |")

	for byte in copy 'eachCopy'
		print(" {byte}")
	end 'eachCopy'

	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 8: 1 2 3 4 | 1 2 3 4 5 6 7 8
```

<!-- test: builtins-byte-order.append-to-the-owner-of-a-viewed-buffer-leaves-the-view-unchanged -->
<!-- tier: stdlib -->
The same sharing seen from the other side: the append goes to the array whose buffer a `clone` is still reading.
```maxon
function main() returns ExitCode
	var original = ByteArray.create()
	original.reserve(64)

	for byte in [1, 2, 3, 4] 'fill'
		original.push(byte)
	end 'fill'

	let copy = original.clone()
	__Builtins.appendBigEndian(original, 0x05060708, 4)
	print("{original.count()} {copy.count()}:")

	for byte in original 'eachOriginal'
		print(" {byte}")
	end 'eachOriginal'

	print(" |")

	for byte in copy 'eachCopy'
		print(" {byte}")
	end 'eachCopy'

	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8 4: 1 2 3 4 5 6 7 8 | 1 2 3 4
```

<!-- test: builtins-byte-order.append-then-read-round-trips-across-growth -->
<!-- tier: stdlib -->
Appending past the capacity again and again grows the buffer between appends; every word read back is the word
appended, in both orders.
```maxon
function main() returns ExitCode
	var little = ByteArray.create()
	var big = ByteArray.create()

	for n in 0 upto 100 'append'
		__Builtins.appendLittleEndian(little, n * 1000003, 8)
		__Builtins.appendBigEndian(big, n * 1000003, 4)
	end 'append'

	var bad = 0 as Count

	for n in 0 upto 100 'check'
		let expected = (n * 1000003) as Count
		let fromLittle = try __Builtins.readLittleEndian(little, n * 8, 8) otherwise panic("a read of an appended word failed")
		let fromBig = try __Builtins.readBigEndian(big, n * 4, 4) otherwise panic("a read of an appended word failed")

		if (fromLittle as Count) != expected or (fromBig as Count) != (expected and 0xFFFFFFFF) 'differs'
			bad = bad + 1
		end 'differs'
	end 'check'

	print("{little.count()} {big.count()} {bad}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
800 400 0
```

<!-- test: builtins-byte-order.a-word-strided-byte-array-reads-and-appends-element-wise -->
<!-- tier: stdlib -->
`Bag with Byte`'s `items` is created inside a shared generic body, so its record is word-strided (`elementSize()`
is 8) however its type reads. The access is over ELEMENTS, not over the buffer's bytes: a four-byte read from index 2
sees elements 2 to 5, and an append adds one element per byte.
```maxon
type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	function add(value Element)
		items.push(value)
	end 'add'
end 'Bag'

typealias ByteBag = Bag with Byte

function main() returns ExitCode
	var bag = ByteBag.create()

	for byte in [1, 2, 3, 4, 5, 6, 7, 8] 'fill'
		bag.add(byte)
	end 'fill'

	let little = try __Builtins.readLittleEndian(bag.items, 2, 4) otherwise panic("a read inside the array failed")
	let big = try __Builtins.readBigEndian(bag.items, 1, 2) otherwise panic("a read inside the array failed")
	print("{bag.items.managed.elementSize()} {little as Count} {big as Count}\n")
	__Builtins.appendBigEndian(bag.items, 0x090A0B0C, 4)
	print("{bag.items.count()}:")

	for byte in bag.items 'each'
		print(" {byte}")
	end 'each'

	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8 100992003 515
12: 1 2 3 4 5 6 7 8 9 10 11 12
```

<!-- test: builtins-byte-order.an-append-to-a-let-binding-is-refused-as-a-push-is -->
<!-- tier: stdlib -->
An append mutates its `output`, so a `let` binding is refused for it exactly as for `push`.
```maxon
function main() returns ExitCode
	let out = ByteArray.create()
	__Builtins.appendLittleEndian(out, 1, 2)
	print("{out.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:4:13: cannot write through 'out': 'out' is read-only (a `let` binding) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: builtins-byte-order.a-let-handed-to-a-function-that-appends-is-refused-as-for-a-push -->
<!-- tier: stdlib -->
A function that writes its `ByteArray` parameter only through an append intrinsic writes that parameter, so handing it
a `let` binding is refused at the call exactly as for a function that pushes.
```maxon
function grow(output ByteArray)
	__Builtins.appendLittleEndian(output, 1, 2)
end 'grow'

function main() returns ExitCode
	let out = ByteArray.create()
	grow(out)
	print("{out.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:8:2: cannot pass 'out', which is read-only (a `let` binding), to a function that writes parameter 'output' (in main); pass a clone()
```

<!-- test: builtins-byte-order.fill-random-into-a-let-binding-is-refused-as-a-push-is -->
<!-- tier: stdlib -->
`fillRandom` writes the buffer it is given, so a `let` binding is refused for it exactly as for `push`.
```maxon
function main() returns ExitCode
	let drawn = ByteArray.create()
	let status = __Builtins.fillRandom(drawn.managed)
	print("{status}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:4:26: cannot write through 'drawn.managed': 'drawn' is read-only (a `let` binding) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: builtins-byte-order.a-file-read-into-a-let-binding-is-refused-as-a-push-is -->
<!-- tier: stdlib -->
`read` and `readAppending` fill the buffer they are given, so a `let` binding is refused for each.
```maxon
function main() returns ExitCode
	let file = try __ManagedFile.openRead("input.bin".toByteArray()) otherwise return 1
	let managed = try __ManagedMemory.create(16, 1) otherwise return 1
	let got = try file.read(managed, 16) otherwise return 1
	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:5:21: cannot write through 'managed': 'managed' is read-only (a `let` binding) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: builtins-byte-order.a-socket-recv-into-a-let-binding-is-refused-as-a-push-is -->
<!-- tier: stdlib -->
`recv` fills the buffer it is given, so a `let` binding is refused for it.
```maxon
function receive(socket __ManagedSocket) returns ExitCode
	let managed = try __ManagedMemory.create(16, 1) otherwise return 1
	let got = try socket.recv(managed) otherwise return 1
	print("{got}\n")
	return 0
end 'receive'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:4:23: cannot write through 'managed': 'managed' is read-only (a `let` binding) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: builtins-byte-order.a-let-receiver-whose-method-appends-to-its-own-field-is-refused-as-for-a-push -->
<!-- tier: stdlib -->
A method that appends to a field of its own receiver writes the receiver, so a `let` receiver is refused at the call
whichever way the field is spelled, exactly as for a method that pushes.
```maxon
type Sink
	var bytes as ByteArray

	static function create() returns Sink
		return Sink{bytes: ByteArray.create()}
	end 'create'

	function put()
		__Builtins.appendLittleEndian(self.bytes, 1, 2)
	end 'put'

	function putBare()
		__Builtins.appendLittleEndian(bytes, 1, 2)
	end 'putBare'
end 'Sink'

function main() returns ExitCode
	let sink = Sink.create()
	sink.put()
	sink.putBare()
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:20:7: cannot pass 'sink', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
error E3159: <fragment>:21:7: cannot pass 'sink', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: builtins-byte-order.a-let-receiver-whose-method-fills-its-own-field-is-refused-as-for-a-push -->
<!-- tier: stdlib -->
`fillRandom` into a field of the receiver writes the receiver, so a `let` receiver is refused at the call.
```maxon
type Sink
	var bytes as ByteArray

	static function create() returns Sink
		return Sink{bytes: ByteArray.create()}
	end 'create'

	function fill()
		_ = __Builtins.fillRandom(self.bytes.managed)
	end 'fill'
end 'Sink'

function main() returns ExitCode
	let sink = Sink.create()
	sink.fill()
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:16:7: cannot pass 'sink', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: builtins-byte-order.a-let-handed-to-a-function-that-fills-its-parameter-is-refused-as-for-a-push -->
<!-- tier: stdlib -->
A function that fills its `ByteArray` parameter's buffer writes that parameter, so handing it a `let` binding is refused
at the call.
```maxon
function fill(drawn ByteArray)
	_ = __Builtins.fillRandom(drawn.managed)
end 'fill'

function main() returns ExitCode
	let drawn = ByteArray.create()
	fill(drawn)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:8:2: cannot pass 'drawn', which is read-only (a `let` binding), to a function that writes parameter 'drawn' (in main); pass a clone()
```

<!-- test: builtins-byte-order.width-three-is-refused -->
<!-- tier: stdlib -->
Only 2, 4 and 8 name a word a single access moves.
```maxon
function main() returns ExitCode
	var data = ByteArray.create()
	data.push(1)
	let word = try __Builtins.readLittleEndian(data, 0, 3) otherwise panic("unreachable")
	print("{word as Count}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:5:28: '__Builtins.readLittleEndian' requires a constant width of 2, 4 or 8, but its argument is 3
```

<!-- test: builtins-byte-order.width-must-be-a-constant -->
<!-- tier: stdlib -->
The width selects the access at compile time; a width read from a variable names none.
```maxon
function main() returns ExitCode
	var data = ByteArray.create()
	__Builtins.appendBigEndian(data, 1, data.count() + 2)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:4:13: '__Builtins.appendBigEndian' requires a constant width of 2, 4 or 8, but its argument is a value not known at compile time
```

<!-- test: builtins-byte-order.source-must-be-a-byte-array -->
<!-- tier: stdlib -->
An array of any other element is refused: the access is over bytes.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntegerArray = Array with Integer

function main() returns ExitCode
	var data = IntegerArray.create()
	data.push(1)
	let word = try __Builtins.readLittleEndian(data, 0, 2) otherwise panic("unreachable")
	print("{word as Count}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:28: '__Builtins.readLittleEndian' requires a ByteArray, but its argument is IntegerArray
```

<!-- test: builtins-byte-order.arity-is-checked -->
<!-- tier: stdlib -->
Every one of the four takes exactly three arguments.
```maxon
function main() returns ExitCode
	var data = ByteArray.create()
	__Builtins.appendLittleEndian(data, 1)
	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:4:13: '__Builtins.appendLittleEndian' takes exactly 3 arguments, but 2 were given
```

<!-- test: builtins-byte-order.library-tier-only -->
A program outside `stdlib/` and `runtime/` cannot name the intrinsics.
```maxon
function main() returns ExitCode
	var data = ByteArray.create()
	data.push(1)
	data.push(2)
	let word = try __Builtins.readLittleEndian(data, 0, 2) otherwise panic("unreachable")
	print("{word as Count}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:6:28: call to '__Builtins.readLittleEndian' from outside the library tiers: '__Builtins' names a raw runtime intrinsic, callable only from a source file under stdlib/ or runtime/ — it has no public replacement
```
