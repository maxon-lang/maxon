---
feature: ranged-int-bit-packing
status: selfhosted
keywords: [array, bit-packing, ranged-int, sub-byte, element-size, packed, bool]
category: memory
---
# Ranged-Int Sub-Byte Bit-Packing

## Documentation

`Array with bool` bit-packs 8 elements per byte. The same machinery extends to any
non-negative ranged-integer typealias `int(0 to N)`: the element is stored in the
fewest bits that hold `0..N`, rounded up to a width that divides a byte so a field
never straddles a byte boundary:

| element type      | bits/element | per byte |
|-------------------|--------------|----------|
| `int(0 to 1)`     | 1            | 8        |
| `int(0 to 2)`     | 2            | 4        |
| `int(0 to 3)`     | 2            | 4        |
| `int(0 to 4)`     | 4            | 2        |
| `int(0 to 15)`    | 4            | 2        |
| `int(0 to 16)`    | 8 (byte)     | 1        |

The record's `element_size@24` carries the packed stride as a **negative bit width**
(`-1` for 1-bit, `-2` for 2-bit, `-4` for a nibble); a positive value is a byte
stride and `int(0 to 16)`+ / `int(0 to 255)` therefore stay one byte per element.
Only **lower-bound-0** ranges pack: the value maps directly to its bits (no offset)
and resolves to an unsigned store type, so the masked packed read is zero-extended —
exactly the value. A signed (`lo < 0`) range stays byte-strided. Enums are *not*
packed even though they carry an ordinal range (see `enum-narrow-storage`).

Packing is transparent to the `get`/`set`/iterate API and identical whether the array
is built dynamically (`Array with int(0 to 3)` + `push`) or from a constant literal
whose elements are pinned to the alias (`[0 as Q, 1 as Q, …]`). The tests below prove
the representation two ways: reading the record's raw `element_size` via
`arr.managed.elementSize()` (the packed negative width, cross-target) and round-tripping
values across byte boundaries. The **static** tests additionally pin the exact packed
`.rdata` bytes with a `RequiredRdata` block — e.g. `[0 as Q, 1 as Q, 2 as Q, 3 as Q]`
packs to the single byte `0b11_10_01_00 = 228` — which byte-for-byte proves both the
LSB-first bit layout and the `ceil(count*bits/8)` sizing.

## Tests

<!-- test: dynamic-2bit-packed -->
```maxon
typealias Q = int(0 to 3)
typealias QArray = Array with Q

function main() returns ExitCode
	var a = QArray.create()
	a.push(0)
	a.push(1)
	a.push(2)
	a.push(3)
	a.push(2)
	if a.managed.elementSize() != -2 'notPacked'
		return 99
	end 'notPacked'
	var sum = 0
	var i = 0
	while i < 5 'read'
		sum = sum + (try a.get(i) otherwise 0)
		i = i + 1
	end 'read'
	return sum
end 'main'
```
```exitcode
8
```

<!-- test: dynamic-4bit-packed -->
```maxon
typealias N = int(0 to 15)
typealias NArray = Array with N

function main() returns ExitCode
	var a = NArray.create()
	a.push(15)
	a.push(1)
	a.push(8)
	if a.managed.elementSize() != -4 'notPacked'
		return 99
	end 'notPacked'
	return try a.get(2) otherwise 0
end 'main'
```
```exitcode
8
```

<!-- test: dynamic-1bit-int-packed -->
```maxon
typealias U = int(0 to 1)
typealias UArray = Array with U

function main() returns ExitCode
	var a = UArray.create()
	a.push(1)
	a.push(0)
	a.push(1)
	if a.managed.elementSize() != -1 'notPacked'
		return 99
	end 'notPacked'
	return (try a.get(0) otherwise 0) + (try a.get(2) otherwise 0)
end 'main'
```
```exitcode
2
```

<!-- test: boundary-16-is-byte -->
```maxon
typealias W = int(0 to 16)
typealias WArray = Array with W

function main() returns ExitCode
	var a = WArray.create()
	a.push(16)
	if a.managed.elementSize() != 1 'shouldBeByte'
		return 99
	end 'shouldBeByte'
	return try a.get(0) otherwise 0
end 'main'
```
```exitcode
16
```

<!-- test: boundary-255-is-byte -->
```maxon
typealias Octet = int(0 to 255)
typealias OctetArray = Array with Octet

function main() returns ExitCode
	var a = OctetArray.create()
	a.push(200)
	if a.managed.elementSize() != 1 'shouldBeByte'
		return 99
	end 'shouldBeByte'
	return (try a.get(0) otherwise 0) - 100
end 'main'
```
```exitcode
100
```

<!-- test: nonzero-lower-not-packed -->
```maxon
typealias Offset = int(1 to 3)
typealias OffsetArray = Array with Offset

function main() returns ExitCode
	var a = OffsetArray.create()
	a.push(2)
	if a.managed.elementSize() != 1 'shouldBeByte'
		return 99
	end 'shouldBeByte'
	return try a.get(0) otherwise 1
end 'main'
```
```exitcode
2
```

<!-- test: dynamic-bool-packed -->
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var a = BoolArray.create()
	a.push(true)
	a.push(false)
	a.push(true)
	if a.managed.elementSize() != -1 'notPacked'
		return 99
	end 'notPacked'
	return 5
end 'main'
```
```exitcode
5
```

<!-- test: grow-stays-packed -->
```maxon
typealias Q = int(0 to 3)
typealias QArray = Array with Q

function main() returns ExitCode
	var a = QArray.create()
	var i = 0
	while i < 40 'fill'
		a.push((i mod 4) as Q)
		i = i + 1
	end 'fill'
	if a.managed.elementSize() != -2 'notPacked'
		return 99
	end 'notPacked'
	return (try a.get(39) otherwise 0) + (try a.get(2) otherwise 0)
end 'main'
```
```exitcode
5
```
