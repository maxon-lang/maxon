---
feature: array-copy-widths
status: experimental
keywords: [array, append, insert, remove, bytearray, copy, overlap]
category: memory
---

# Array copies at every length and alignment

## Documentation

An array move copies whole blocks, then whole machine words, then single bytes, in the direction the
overlap needs: `append` and `remove` copy low address first, `insert` copies high address first within one
buffer. Every length from empty through several blocks, and every start alignment of the destination within
a word, must leave exactly the source bytes in place.

## Tests

<!-- test: append-copies-every-length-at-every-alignment -->
### `append` of a source of any length lands every byte after a lead of any size
The lead moves the destination's first copied byte across the four alignments the block, word and byte
runs meet, and the length sweeps past three whole blocks so each run is empty, short and long in turn.
```maxon
function expectedAt(index Count) returns Byte
	return ((index * 7 + 3) and 0xFF) as Byte
end 'expectedAt'

function patterned(count Count) returns ByteArray
	var bytes = ByteArray.create()

	for i in 0 upto count 'eachByte'
		bytes.push(expectedAt(i))
	end 'eachByte'

	return bytes
end 'patterned'

function byteAt(bytes ByteArray, index Count) returns Byte
	return try bytes.get(index as ElementIndex) otherwise panic("the index was checked against the count")
end 'byteAt'

function main() returns ExitCode
	var wrong = 0 as Count

	for length in 0 upto 131 'eachLength'
		let source = patterned(length)

		for lead in 0 upto 4 'eachLead'
			var destination = ByteArray.create()

			for _ in 0 upto lead 'eachLeadByte'
				destination.push(200)
			end 'eachLeadByte'

			destination.append(source)

			if destination.count() != lead + length 'wrongCount'
				wrong = wrong + 1
			end 'wrongCount' else 'rightCount'
				for i in 0 upto length 'eachCopied'
					if byteAt(destination, index: lead + i) != expectedAt(i) 'wrongByte'
						wrong = wrong + 1
					end 'wrongByte'
				end 'eachCopied'
			end 'rightCount'
		end 'eachLead'
	end 'eachLength'

	print("wrong {wrong}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
wrong 0
```

<!-- test: insert-and-remove-shift-every-length -->
### `insert` at the front shifts up and `remove` at the front shifts down, at every length
Both move the array's own bytes within one buffer, in opposite directions, so each direction's block,
word and byte runs are exercised by the same sweep.
```maxon
function expectedAt(index Count) returns Byte
	return ((index * 7 + 3) and 0xFF) as Byte
end 'expectedAt'

function patterned(count Count) returns ByteArray
	var bytes = ByteArray.create()

	for i in 0 upto count 'eachByte'
		bytes.push(expectedAt(i))
	end 'eachByte'

	return bytes
end 'patterned'

function byteAt(bytes ByteArray, index Count) returns Byte
	return try bytes.get(index as ElementIndex) otherwise panic("the index was checked against the count")
end 'byteAt'

function main() returns ExitCode
	var wrong = 0 as Count

	for length in 0 upto 131 'eachLength'
		var shifted = patterned(length)
		shifted.insert(0, value: 255)

		if shifted.count() != length + 1 'wrongInsertCount'
			wrong = wrong + 1
		end 'wrongInsertCount' else 'rightInsertCount'
			if byteAt(shifted, index: 0) != 255 'wrongInserted'
				wrong = wrong + 1
			end 'wrongInserted'

			for i in 0 upto length 'eachShiftedUp'
				if byteAt(shifted, index: i + 1) != expectedAt(i) 'wrongUp'
					wrong = wrong + 1
				end 'wrongUp'
			end 'eachShiftedUp'
		end 'rightInsertCount'

		var shrunk = patterned(length + 1)
		let removed = try shrunk.remove(0) otherwise panic("a non-empty array has a front element")

		if removed != expectedAt(0) 'wrongRemoved'
			wrong = wrong + 1
		end 'wrongRemoved'

		if shrunk.count() != length 'wrongRemoveCount'
			wrong = wrong + 1
		end 'wrongRemoveCount' else 'rightRemoveCount'
			for i in 0 upto length 'eachShiftedDown'
				if byteAt(shrunk, index: i) != expectedAt(i + 1) 'wrongDown'
					wrong = wrong + 1
				end 'wrongDown'
			end 'eachShiftedDown'
		end 'rightRemoveCount'
	end 'eachLength'

	print("wrong {wrong}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
wrong 0
```

<!-- test: insert-and-remove-shift-every-position-of-two-byte-elements -->
### `insert(k)` and `remove(k)` at every position shift an array of 2-byte elements, so the copy starts mid-block
`Byte` is redeclared as a 2-byte range, so one element step is 2 bytes and `k` elements put the
copy's first byte at 2 times `k`, across every alignment of the block, word and byte runs. The array
grows past ten blocks.
```maxon
typealias Byte = int(0 to 1000)
typealias Bytes = Array with Byte

let Scale = 7
let Modulus = 997
let Marker = 1000

function expectedAt(index Count) returns Byte
	return ((index * Scale + 3) mod Modulus) as Byte
end 'expectedAt'

function patterned(count Count) returns Bytes
	var elements = Bytes.create()

	for i in 0 upto count 'eachElement'
		elements.push(expectedAt(i))
	end 'eachElement'

	return elements
end 'patterned'

function elementAt(elements Bytes, index Count) returns Byte
	return try elements.get(index as ElementIndex) otherwise panic("the index was checked against the count")
end 'elementAt'

function main() returns ExitCode
	var wrong = 0 as Count

	for length in 0 upto 41 'eachLength'
		for at in 0 to length 'eachInsertPosition'
			var grown = patterned(length)
			grown.insert(at, value: Marker)

			if grown.count() != length + 1 'wrongInsertCount'
				wrong = wrong + 1
			end 'wrongInsertCount' else 'rightInsertCount'
				if elementAt(grown, index: at) != Marker 'wrongMarker'
					wrong = wrong + 1
				end 'wrongMarker'

				for i in 0 upto at 'eachBelow'
					if elementAt(grown, index: i) != expectedAt(i) 'wrongBelow'
						wrong = wrong + 1
					end 'wrongBelow'
				end 'eachBelow'

				for i in at upto length 'eachShiftedUp'
					if elementAt(grown, index: i + 1) != expectedAt(i) 'wrongUp'
						wrong = wrong + 1
					end 'wrongUp'
				end 'eachShiftedUp'
			end 'rightInsertCount'
		end 'eachInsertPosition'

		for at in 0 upto length 'eachRemovePosition'
			var shrunk = patterned(length)
			let removed = try shrunk.remove(at) otherwise panic("the position is below the count")

			if removed != expectedAt(at) 'wrongRemoved'
				wrong = wrong + 1
			end 'wrongRemoved'

			if shrunk.count() + 1 != length 'wrongRemoveCount'
				wrong = wrong + 1
			end 'wrongRemoveCount' else 'rightRemoveCount'
				for i in 0 upto at 'eachKept'
					if elementAt(shrunk, index: i) != expectedAt(i) 'wrongKept'
						wrong = wrong + 1
					end 'wrongKept'
				end 'eachKept'

				for i in at upto shrunk.count() 'eachShiftedDown'
					if elementAt(shrunk, index: i) != expectedAt(i + 1) 'wrongDown'
						wrong = wrong + 1
					end 'wrongDown'
				end 'eachShiftedDown'
			end 'rightRemoveCount'
		end 'eachRemovePosition'
	end 'eachLength'

	print("wrong {wrong}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
wrong 0
```

<!-- test: insert-and-remove-shift-every-position-of-four-byte-elements -->
### `insert(k)` and `remove(k)` at every position shift an array of 4-byte elements, so the copy starts mid-block
`Byte` is redeclared as a 4-byte range, so one element step is 4 bytes and `k` elements put the
copy's first byte at 4 times `k`, across every alignment of the block, word and byte runs. The array
grows past ten blocks.
```maxon
typealias Byte = int(0 to u32.max)
typealias Bytes = Array with Byte

let Scale = 16777259
let Modulus = 4294967291
let Marker = 4294967295

function expectedAt(index Count) returns Byte
	return ((index * Scale + 3) mod Modulus) as Byte
end 'expectedAt'

function patterned(count Count) returns Bytes
	var elements = Bytes.create()

	for i in 0 upto count 'eachElement'
		elements.push(expectedAt(i))
	end 'eachElement'

	return elements
end 'patterned'

function elementAt(elements Bytes, index Count) returns Byte
	return try elements.get(index as ElementIndex) otherwise panic("the index was checked against the count")
end 'elementAt'

function main() returns ExitCode
	var wrong = 0 as Count

	for length in 0 upto 41 'eachLength'
		for at in 0 to length 'eachInsertPosition'
			var grown = patterned(length)
			grown.insert(at, value: Marker)

			if grown.count() != length + 1 'wrongInsertCount'
				wrong = wrong + 1
			end 'wrongInsertCount' else 'rightInsertCount'
				if elementAt(grown, index: at) != Marker 'wrongMarker'
					wrong = wrong + 1
				end 'wrongMarker'

				for i in 0 upto at 'eachBelow'
					if elementAt(grown, index: i) != expectedAt(i) 'wrongBelow'
						wrong = wrong + 1
					end 'wrongBelow'
				end 'eachBelow'

				for i in at upto length 'eachShiftedUp'
					if elementAt(grown, index: i + 1) != expectedAt(i) 'wrongUp'
						wrong = wrong + 1
					end 'wrongUp'
				end 'eachShiftedUp'
			end 'rightInsertCount'
		end 'eachInsertPosition'

		for at in 0 upto length 'eachRemovePosition'
			var shrunk = patterned(length)
			let removed = try shrunk.remove(at) otherwise panic("the position is below the count")

			if removed != expectedAt(at) 'wrongRemoved'
				wrong = wrong + 1
			end 'wrongRemoved'

			if shrunk.count() + 1 != length 'wrongRemoveCount'
				wrong = wrong + 1
			end 'wrongRemoveCount' else 'rightRemoveCount'
				for i in 0 upto at 'eachKept'
					if elementAt(shrunk, index: i) != expectedAt(i) 'wrongKept'
						wrong = wrong + 1
					end 'wrongKept'
				end 'eachKept'

				for i in at upto shrunk.count() 'eachShiftedDown'
					if elementAt(shrunk, index: i) != expectedAt(i + 1) 'wrongDown'
						wrong = wrong + 1
					end 'wrongDown'
				end 'eachShiftedDown'
			end 'rightRemoveCount'
		end 'eachRemovePosition'
	end 'eachLength'

	print("wrong {wrong}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
wrong 0
```

<!-- test: insert-and-remove-shift-every-position-of-eight-byte-elements -->
### `insert(k)` and `remove(k)` at every position shift an array of 8-byte elements, so the copy starts mid-block
`Byte` is redeclared as a 8-byte range, so one element step is 8 bytes and `k` elements put the
copy's first byte at 8 times `k`, across every alignment of the block, word and byte runs. The array
grows past ten blocks.
```maxon
typealias Byte = int(0 to u64.max)
typealias Bytes = Array with Byte

let Scale = 1099511627791
let Modulus = 9223372036854775783
let Marker = 9223372036854775807

function expectedAt(index Count) returns Byte
	return ((index * Scale + 3) mod Modulus) as Byte
end 'expectedAt'

function patterned(count Count) returns Bytes
	var elements = Bytes.create()

	for i in 0 upto count 'eachElement'
		elements.push(expectedAt(i))
	end 'eachElement'

	return elements
end 'patterned'

function elementAt(elements Bytes, index Count) returns Byte
	return try elements.get(index as ElementIndex) otherwise panic("the index was checked against the count")
end 'elementAt'

function main() returns ExitCode
	var wrong = 0 as Count

	for length in 0 upto 41 'eachLength'
		for at in 0 to length 'eachInsertPosition'
			var grown = patterned(length)
			grown.insert(at, value: Marker)

			if grown.count() != length + 1 'wrongInsertCount'
				wrong = wrong + 1
			end 'wrongInsertCount' else 'rightInsertCount'
				if elementAt(grown, index: at) != Marker 'wrongMarker'
					wrong = wrong + 1
				end 'wrongMarker'

				for i in 0 upto at 'eachBelow'
					if elementAt(grown, index: i) != expectedAt(i) 'wrongBelow'
						wrong = wrong + 1
					end 'wrongBelow'
				end 'eachBelow'

				for i in at upto length 'eachShiftedUp'
					if elementAt(grown, index: i + 1) != expectedAt(i) 'wrongUp'
						wrong = wrong + 1
					end 'wrongUp'
				end 'eachShiftedUp'
			end 'rightInsertCount'
		end 'eachInsertPosition'

		for at in 0 upto length 'eachRemovePosition'
			var shrunk = patterned(length)
			let removed = try shrunk.remove(at) otherwise panic("the position is below the count")

			if removed != expectedAt(at) 'wrongRemoved'
				wrong = wrong + 1
			end 'wrongRemoved'

			if shrunk.count() + 1 != length 'wrongRemoveCount'
				wrong = wrong + 1
			end 'wrongRemoveCount' else 'rightRemoveCount'
				for i in 0 upto at 'eachKept'
					if elementAt(shrunk, index: i) != expectedAt(i) 'wrongKept'
						wrong = wrong + 1
					end 'wrongKept'
				end 'eachKept'

				for i in at upto shrunk.count() 'eachShiftedDown'
					if elementAt(shrunk, index: i) != expectedAt(i + 1) 'wrongDown'
						wrong = wrong + 1
					end 'wrongDown'
				end 'eachShiftedDown'
			end 'rightRemoveCount'
		end 'eachRemovePosition'
	end 'eachLength'

	print("wrong {wrong}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
wrong 0
```
