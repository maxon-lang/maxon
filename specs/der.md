---
feature: der
status: stable
keywords: [der, DerReader, readString, readBoolean, readElement, readImplicit, DerHeader, DerBitString, DerError, DerTagClass, asn.1, x.690, encoding]
category: stdlib
---

# `DerReader` — strict DER (ITU-T X.690)

## Documentation

| Call | Meaning |
|---|---|
| `DerReader.create(bytes)` | a reader over `bytes`, positioned at its first element |
| `reader.peekHeader()` | the next element's `DerHeader`: `tagClass()`, `isConstructed()`, `tagNumber()` and `length()`, without consuming it |
| `reader.isAtEnd()` | whether no element remains |
| `reader.finish()` | `trailingData` unless no element remains |
| `reader.enterSequence()`, `enterSet()`, `enterExplicit(n)` | a reader over a SEQUENCE's, a SET's or an explicit context-specific `[n]` element's content |
| `reader.readInteger()` | a non-negative INTEGER's big-endian magnitude, without its sign octet |
| `reader.readObjectIdentifier()` | an OBJECT IDENTIFIER's content octets |
| `reader.readNull()` | consumes a NULL |
| `reader.readBitString()` | a `DerBitString`: `bytes()` and `unusedBits()` |
| `reader.readOctetString()` | an OCTET STRING's content |
| `reader.readUtcTime()`, `readGeneralizedTime()` | the time as `UnixSeconds` |
| `reader.readSignedInteger()` | an INTEGER of either sign, as its minimal two's-complement content octets |
| `reader.readBoolean()` | a BOOLEAN, whose one content octet is `00` or `ff` |
| `reader.readElement()` | the next element, whatever its tag, as its whole encoding |
| `reader.readImplicit(n)`, `readImplicitBitString(n)`, `readImplicitSignedInteger(n)` | a primitive context-specific `[n]`: its content, or that content read as a BIT STRING or an INTEGER |
| `reader.readString()` | a UTF8String, PrintableString, IA5String, TeletexString, BMPString or UniversalString, decoded to a `String`; text outside its type's characters is `invalidString` |

A read refuses anything outside DER with a `DerError`. A length is definite and minimal; a tag number of 31
or more is in the high-tag-number form, minimally; an INTEGER has at least one octet and no redundant
leading octet; the string and time types are primitive. A read also refuses an element of another type
(`unexpectedTag`), and one whose content runs past the input or past the element that holds it
(`truncated`).

These cases have no canra source. They come from the DER rules of ITU-T X.690 (sections 8.1, 8.3, 8.6,
8.8, 8.19, 10, 11.2, 11.7 and 11.8) and the time profile of RFC 5280 section 4.1.2.5, with expected values
worked out by hand from those rules.

## Tests

<!-- test: der.reads-a-subject-public-key-info -->
A SubjectPublicKeyInfo walked element by element, nested readers over a BIT STRING's content, `isAtEnd`, and `finish` refusing what follows the last element.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure DerError) returns String
	return match failure 'name'
		truncated gives "truncated"
		indefiniteLength gives "indefiniteLength"
		nonMinimalLength gives "nonMinimalLength"
		nonMinimalTagNumber gives "nonMinimalTagNumber"
		tagNumberTooLarge gives "tagNumberTooLarge"
		unexpectedTag gives "unexpectedTag"
		trailingData gives "trailingData"
		emptyInteger gives "emptyInteger"
		nonMinimalInteger gives "nonMinimalInteger"
		negativeInteger gives "negativeInteger"
		invalidNull gives "invalidNull"
		invalidBitString gives "invalidBitString"
		invalidObjectIdentifier gives "invalidObjectIdentifier"
		invalidTime gives "invalidTime"
		invalidBoolean gives "invalidBoolean"
		invalidString gives "invalidString"
	end 'name'
end 'errorName'

function readKey(id String, hex String) throws DerError
	var outer = DerReader.create(fromHex(hex))
	var info = try outer.enterSequence()
	try outer.finish()

	var algorithm = try info.enterSequence()
	let identifier = try algorithm.readObjectIdentifier()
	try algorithm.readNull()
	print("{id} algorithm [{toHex(identifier)}] {algorithm.isAtEnd()}\n")
	try algorithm.finish()

	let key = try info.readBitString()
	try info.finish()
	print("{id} key {key.unusedBits()} [{toHex(key.bytes())}]\n")

	var inner = DerReader.create(key.bytes())
	var numbers = try inner.enterSequence()
	try inner.finish()

	while not numbers.isAtEnd() 'eachInteger'
		let value = try numbers.readInteger()
		print("{id} integer [{toHex(value)}]\n")
	end 'eachInteger'

	try numbers.finish()
end 'readKey'

function report(id String, hex String)
	try readKey(id, hex: hex) otherwise (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("structure-001", hex: "3020300d06092a864886f70d0101010500030f00300c020500c10000010203010001")
	report("structure-002", hex: "3021300d06092a864886f70d0101010500030f00300c020500c1000001020301000100")
	report("structure-003", hex: "3021300d06092a864886f70d0101010500031000300c020500c1000001020301000100")
	report("structure-004", hex: "3003300102")
	report("structure-005", hex: "3003020100")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
structure-001 algorithm [2a864886f70d010101] true
structure-001 key 0 [300c020500c10000010203010001]
structure-001 integer [c1000001]
structure-001 integer [010001]
structure-002 algorithm [2a864886f70d010101] true
structure-002 trailingData
structure-003 algorithm [2a864886f70d010101] true
structure-003 key 0 [300c020500c1000001020301000100]
structure-003 trailingData
structure-004 truncated
structure-005 unexpectedTag
```

<!-- test: der.headers -->
`peekHeader` reads every tag class, the constructed bit, the low and high tag-number forms and the short and long length forms without consuming them, and refuses each length and tag-number encoding X.690 section 10.1 and 8.1.2 rule out of DER.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure DerError) returns String
	return match failure 'name'
		truncated gives "truncated"
		indefiniteLength gives "indefiniteLength"
		nonMinimalLength gives "nonMinimalLength"
		nonMinimalTagNumber gives "nonMinimalTagNumber"
		tagNumberTooLarge gives "tagNumberTooLarge"
		unexpectedTag gives "unexpectedTag"
		trailingData gives "trailingData"
		emptyInteger gives "emptyInteger"
		nonMinimalInteger gives "nonMinimalInteger"
		negativeInteger gives "negativeInteger"
		invalidNull gives "invalidNull"
		invalidBitString gives "invalidBitString"
		invalidObjectIdentifier gives "invalidObjectIdentifier"
		invalidTime gives "invalidTime"
		invalidBoolean gives "invalidBoolean"
		invalidString gives "invalidString"
	end 'name'
end 'errorName'

function describe(header DerHeader) returns String
	return "{header.tagClass().name} {header.isConstructed()} {header.tagNumber()} {header.length()}"
end 'describe'

function report(id String, hex String)
	let reader = DerReader.create(fromHex(hex))

	if let header = try reader.peekHeader() 'read'
		let again = try reader.peekHeader() otherwise panic("a second peek reads the same header")
		print("{id} {describe(header)} {describe(again) == describe(header)} {reader.isAtEnd()}\n")
	end 'read' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("header-001", hex: "020105")
	report("header-002", hex: "3000")
	report("header-003", hex: "a003020102")
	report("header-004", hex: "4100")
	report("header-005", hex: "fe00")
	report("header-006", hex: "1f1f00")
	report("header-007", hex: "9f810000")
	report("header-008", hex: "1f8fffffff7f00")
	report("header-009", hex: "048180abababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababab")
	report("header-010", hex: "04820100abababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababab")
	report("header-011", hex: "")
	report("header-012", hex: "04")
	report("header-013", hex: "040501020304")
	report("header-014", hex: "04800000")
	report("header-015", hex: "0481050102030405")
	report("header-016", hex: "04820080abababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababab")
	report("header-017", hex: "0484ffffffff00")
	report("header-018", hex: "04890100000000000000000000")
	report("header-019", hex: "1f1e00")
	report("header-020", hex: "1f800100")
	report("header-021", hex: "1f908080800000")
	report("header-022", hex: "1f81")
	report("header-023", hex: "0481")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
header-001 universal false 2 1 true false
header-002 universal true 16 0 true false
header-003 contextSpecific true 0 3 true false
header-004 application false 1 0 true false
header-005 private true 30 0 true false
header-006 universal false 31 0 true false
header-007 contextSpecific false 128 0 true false
header-008 universal false 4294967295 0 true false
header-009 universal false 4 128 true false
header-010 universal false 4 256 true false
header-011 truncated
header-012 truncated
header-013 truncated
header-014 indefiniteLength
header-015 nonMinimalLength
header-016 nonMinimalLength
header-017 truncated
header-018 truncated
header-019 nonMinimalTagNumber
header-020 nonMinimalTagNumber
header-021 tagNumberTooLarge
header-022 truncated
header-023 truncated
```

<!-- test: der.integers -->
`readInteger` gives the magnitude without its sign octet and refuses an empty, non-minimal (X.690 section 8.3.2) or negative INTEGER, and a constructed or wrongly tagged element.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure DerError) returns String
	return match failure 'name'
		truncated gives "truncated"
		indefiniteLength gives "indefiniteLength"
		nonMinimalLength gives "nonMinimalLength"
		nonMinimalTagNumber gives "nonMinimalTagNumber"
		tagNumberTooLarge gives "tagNumberTooLarge"
		unexpectedTag gives "unexpectedTag"
		trailingData gives "trailingData"
		emptyInteger gives "emptyInteger"
		nonMinimalInteger gives "nonMinimalInteger"
		negativeInteger gives "negativeInteger"
		invalidNull gives "invalidNull"
		invalidBitString gives "invalidBitString"
		invalidObjectIdentifier gives "invalidObjectIdentifier"
		invalidTime gives "invalidTime"
		invalidBoolean gives "invalidBoolean"
		invalidString gives "invalidString"
	end 'name'
end 'errorName'

function report(id String, hex String)
	var reader = DerReader.create(fromHex(hex))

	if let value = try reader.readInteger() 'read'
		print("{id} [{toHex(value)}] {reader.isAtEnd()}\n")
	end 'read' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("integer-001", hex: "020100")
	report("integer-002", hex: "02017f")
	report("integer-003", hex: "02020080")
	report("integer-004", hex: "0203010001")
	report("integer-005", hex: "020300ff00")
	report("integer-006", hex: "0200")
	report("integer-007", hex: "0202007f")
	report("integer-008", hex: "02020000")
	report("integer-009", hex: "0202ff80")
	report("integer-010", hex: "020180")
	report("integer-011", hex: "0201ff")
	report("integer-012", hex: "0202ff7f")
	report("integer-013", hex: "2203020100")
	report("integer-014", hex: "030100")
	report("integer-015", hex: "02020001ff")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
integer-001 [00] true
integer-002 [7f] true
integer-003 [80] true
integer-004 [010001] true
integer-005 [ff00] true
integer-006 emptyInteger
integer-007 nonMinimalInteger
integer-008 nonMinimalInteger
integer-009 nonMinimalInteger
integer-010 negativeInteger
integer-011 negativeInteger
integer-012 negativeInteger
integer-013 unexpectedTag
integer-014 unexpectedTag
integer-015 nonMinimalInteger
```

<!-- test: der.primitives -->
OBJECT IDENTIFIER (X.690 section 8.19), NULL (8.8), BIT STRING (8.6 and 11.2) and OCTET STRING: their content rules, and the constructed forms DER forbids.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure DerError) returns String
	return match failure 'name'
		truncated gives "truncated"
		indefiniteLength gives "indefiniteLength"
		nonMinimalLength gives "nonMinimalLength"
		nonMinimalTagNumber gives "nonMinimalTagNumber"
		tagNumberTooLarge gives "tagNumberTooLarge"
		unexpectedTag gives "unexpectedTag"
		trailingData gives "trailingData"
		emptyInteger gives "emptyInteger"
		nonMinimalInteger gives "nonMinimalInteger"
		negativeInteger gives "negativeInteger"
		invalidNull gives "invalidNull"
		invalidBitString gives "invalidBitString"
		invalidObjectIdentifier gives "invalidObjectIdentifier"
		invalidTime gives "invalidTime"
		invalidBoolean gives "invalidBoolean"
		invalidString gives "invalidString"
	end 'name'
end 'errorName'

function readOid(id String, hex String)
	var reader = DerReader.create(fromHex(hex))

	if let identifier = try reader.readObjectIdentifier() 'read'
		print("{id} [{toHex(identifier)}]\n")
	end 'read' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'readOid'

function readNull(id String, hex String)
	var reader = DerReader.create(fromHex(hex))

	try reader.readNull() otherwise (failure) 'refused'
		print("{id} {errorName(failure)}\n")
		return
	end 'refused'

	print("{id} null\n")
end 'readNull'

function readBits(id String, hex String)
	var reader = DerReader.create(fromHex(hex))

	if let bits = try reader.readBitString() 'read'
		print("{id} {bits.unusedBits()} [{toHex(bits.bytes())}]\n")
	end 'read' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'readBits'

function readOctets(id String, hex String)
	var reader = DerReader.create(fromHex(hex))

	if let octets = try reader.readOctetString() 'read'
		print("{id} [{toHex(octets)}]\n")
	end 'read' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'readOctets'

function main() returns ExitCode
	readOid("oid-001", hex: "06092a864886f70d010101")
	readOid("oid-002", hex: "06032b6570")
	readOid("oid-003", hex: "0600")
	readOid("oid-004", hex: "06032a8001")
	readOid("oid-005", hex: "06022a86")
	readOid("oid-006", hex: "06028001")
	readOid("oid-007", hex: "0500")
	readNull("null-001", hex: "0500")
	readNull("null-002", hex: "050100")
	readNull("null-003", hex: "2500")
	readBits("bits-001", hex: "030100")
	readBits("bits-002", hex: "030206c0")
	readBits("bits-003", hex: "030300ffee")
	readBits("bits-004", hex: "0300")
	readBits("bits-005", hex: "030101")
	readBits("bits-006", hex: "03020800")
	readBits("bits-007", hex: "03020101")
	readBits("bits-008", hex: "2303030100")
	readOctets("octets-001", hex: "0403010203")
	readOctets("octets-002", hex: "0400")
	readOctets("octets-003", hex: "24050403010203")
	readOctets("octets-004", hex: "0403")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
oid-001 [2a864886f70d010101]
oid-002 [2b6570]
oid-003 invalidObjectIdentifier
oid-004 invalidObjectIdentifier
oid-005 invalidObjectIdentifier
oid-006 invalidObjectIdentifier
oid-007 unexpectedTag
null-001 null
null-002 invalidNull
null-003 unexpectedTag
bits-001 0 []
bits-002 6 [c0]
bits-003 0 [ffee]
bits-004 invalidBitString
bits-005 invalidBitString
bits-006 invalidBitString
bits-007 invalidBitString
bits-008 unexpectedTag
octets-001 [010203]
octets-002 []
octets-003 unexpectedTag
octets-004 truncated
```

<!-- test: der.constructed -->
SET and SEQUENCE readers, an explicit `[n]` tag as X.509 writes its version, and an element running past its parent.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure DerError) returns String
	return match failure 'name'
		truncated gives "truncated"
		indefiniteLength gives "indefiniteLength"
		nonMinimalLength gives "nonMinimalLength"
		nonMinimalTagNumber gives "nonMinimalTagNumber"
		tagNumberTooLarge gives "tagNumberTooLarge"
		unexpectedTag gives "unexpectedTag"
		trailingData gives "trailingData"
		emptyInteger gives "emptyInteger"
		nonMinimalInteger gives "nonMinimalInteger"
		negativeInteger gives "negativeInteger"
		invalidNull gives "invalidNull"
		invalidBitString gives "invalidBitString"
		invalidObjectIdentifier gives "invalidObjectIdentifier"
		invalidTime gives "invalidTime"
		invalidBoolean gives "invalidBoolean"
		invalidString gives "invalidString"
	end 'name'
end 'errorName'

function readSet(id String, hex String) throws DerError
	var outer = DerReader.create(fromHex(hex))
	var members = try outer.enterSet()
	try outer.finish()
	var count = 0 as Count

	while not members.isAtEnd() 'eachMember'
		_ = try members.readOctetString()
		count = count + 1
	end 'eachMember'

	print("{id} set of {count}\n")
end 'readSet'

function readVersion(id String, hex String, tagNumber DerTagNumber) throws DerError
	var outer = DerReader.create(fromHex(hex))
	var tbs = try outer.enterSequence()
	var explicit = try tbs.enterExplicit(tagNumber)
	let version = try explicit.readInteger()
	try explicit.finish()
	let serial = try tbs.readInteger()
	try tbs.finish()
	try outer.finish()
	print("{id} [{tagNumber}] [{toHex(version)}] [{toHex(serial)}]\n")
end 'readVersion'

function reportSet(id String, hex String)
	try readSet(id, hex: hex) otherwise (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'reportSet'

function reportVersion(id String, hex String, tagNumber DerTagNumber)
	try readVersion(id, hex: hex, tagNumber: tagNumber) otherwise (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'reportVersion'

function main() returns ExitCode
	reportSet("constructed-001", hex: "3109040004010104020102")
	reportSet("constructed-002", hex: "3100")
	reportSet("constructed-003", hex: "3000")
	reportSet("constructed-004", hex: "1100")
	reportSet("constructed-005", hex: "310304010100")
	reportSet("constructed-006", hex: "3103040201")
	reportVersion("constructed-007", hex: "3008a003020102020101", tagNumber: 0)
	reportVersion("constructed-008", hex: "3008a303020102020101", tagNumber: 3)
	reportVersion("constructed-009", hex: "3008a003020102020101", tagNumber: 3)
	reportVersion("constructed-010", hex: "30088003020102020101", tagNumber: 0)
	reportVersion("constructed-011", hex: "3009a00402010200020101", tagNumber: 0)
	reportVersion("constructed-012", hex: "3008a0030201020201", tagNumber: 0)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
constructed-001 set of 3
constructed-002 set of 0
constructed-003 unexpectedTag
constructed-004 unexpectedTag
constructed-005 trailingData
constructed-006 truncated
constructed-007 [0] [02] [01]
constructed-008 [3] [02] [01]
constructed-009 unexpectedTag
constructed-010 unexpectedTag
constructed-011 trailingData
constructed-012 truncated
```

<!-- test: der.times -->
UTCTime and GeneralizedTime in the form X.690 section 11.7 and 11.8 and RFC 5280 section 4.1.2.5 require, as Unix seconds: two-digit years from 1950 to 2049, the Gregorian calendar, and seconds and a `Z` always present.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure DerError) returns String
	return match failure 'name'
		truncated gives "truncated"
		indefiniteLength gives "indefiniteLength"
		nonMinimalLength gives "nonMinimalLength"
		nonMinimalTagNumber gives "nonMinimalTagNumber"
		tagNumberTooLarge gives "tagNumberTooLarge"
		unexpectedTag gives "unexpectedTag"
		trailingData gives "trailingData"
		emptyInteger gives "emptyInteger"
		nonMinimalInteger gives "nonMinimalInteger"
		negativeInteger gives "negativeInteger"
		invalidNull gives "invalidNull"
		invalidBitString gives "invalidBitString"
		invalidObjectIdentifier gives "invalidObjectIdentifier"
		invalidTime gives "invalidTime"
		invalidBoolean gives "invalidBoolean"
		invalidString gives "invalidString"
	end 'name'
end 'errorName'

function readUtc(id String, hex String)
	var reader = DerReader.create(fromHex(hex))

	if let seconds = try reader.readUtcTime() 'read'
		print("{id} {seconds}\n")
	end 'read' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'readUtc'

function readGeneralized(id String, hex String)
	var reader = DerReader.create(fromHex(hex))

	if let seconds = try reader.readGeneralizedTime() 'read'
		print("{id} {seconds}\n")
	end 'read' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'readGeneralized'

function main() returns ExitCode
	readUtc("time-001", hex: "170d3439313233313233353935395a")
	readUtc("time-002", hex: "170d3530303130313030303030305a")
	readUtc("time-003", hex: "170d3730303130313030303030305a")
	readUtc("time-004", hex: "170d3234303730343038333031355a")
	readUtc("time-005", hex: "170d3030303232393132303030305a")
	readUtc("time-006", hex: "170b343931323331323335395a")
	readUtc("time-007", hex: "17113439313233313233353935392b30303030")
	readUtc("time-008", hex: "170d3439313233313233353935397a")
	readUtc("time-009", hex: "170d3439313333313233353935395a")
	readUtc("time-010", hex: "170d3439303233303030303030305a")
	readUtc("time-011", hex: "170d3439313233313234303030305a")
	readUtc("time-012", hex: "170d3439313233313233363030305a")
	readUtc("time-013", hex: "170d3439313233313233353936305a")
	readUtc("time-014", hex: "170d3439313233313233353920395a")
	readUtc("time-015", hex: "170d3439303130303030303030305a")
	readUtc("time-016", hex: "180f32303530303130313030303030305a")
	readGeneralized("time-017", hex: "180f32303530303130313030303030305a")
	readGeneralized("time-018", hex: "180f31393730303130313030303030305a")
	readGeneralized("time-019", hex: "180f39393939313233313233353935395a")
	readGeneralized("time-020", hex: "180f30303030303130313030303030305a")
	readGeneralized("time-021", hex: "180f32303030303232393132303030305a")
	readGeneralized("time-022", hex: "180f32313030303232393030303030305a")
	readGeneralized("time-023", hex: "180f32303031303232393030303030305a")
	readGeneralized("time-024", hex: "181132303530303130313030303030302e355a")
	readGeneralized("time-025", hex: "180d3230353030313031303030305a")
	readGeneralized("time-026", hex: "180e3230353030313031303030303030")
	readGeneralized("time-027", hex: "180f32303530303433313030303030305a")
	readGeneralized("time-028", hex: "170d3530303130313030303030305a")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
time-001 2524607999
time-002 -631152000
time-003 0
time-004 1720081815
time-005 951825600
time-006 invalidTime
time-007 invalidTime
time-008 invalidTime
time-009 invalidTime
time-010 invalidTime
time-011 invalidTime
time-012 invalidTime
time-013 invalidTime
time-014 invalidTime
time-015 invalidTime
time-016 unexpectedTag
time-017 2524608000
time-018 0
time-019 253402300799
time-020 -62167219200
time-021 951825600
time-022 invalidTime
time-023 invalidTime
time-024 invalidTime
time-025 invalidTime
time-026 invalidTime
time-027 invalidTime
time-028 unexpectedTag
```

<!-- test: der.x509-reads -->
The reads X.509 needs beyond the universal types: BOOLEAN (X.690 sections 8.2 and 11.1), an INTEGER of either sign (8.3), an implicitly tagged primitive (8.14), a whole element, and the six string types with their character rules.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure DerError) returns String
	return match failure 'name'
		truncated gives "truncated"
		indefiniteLength gives "indefiniteLength"
		nonMinimalLength gives "nonMinimalLength"
		nonMinimalTagNumber gives "nonMinimalTagNumber"
		tagNumberTooLarge gives "tagNumberTooLarge"
		unexpectedTag gives "unexpectedTag"
		trailingData gives "trailingData"
		emptyInteger gives "emptyInteger"
		nonMinimalInteger gives "nonMinimalInteger"
		negativeInteger gives "negativeInteger"
		invalidNull gives "invalidNull"
		invalidBitString gives "invalidBitString"
		invalidObjectIdentifier gives "invalidObjectIdentifier"
		invalidTime gives "invalidTime"
		invalidBoolean gives "invalidBoolean"
		invalidString gives "invalidString"
	end 'name'
end 'errorName'

function readBoolean(id String, hex String)
	var reader = DerReader.create(fromHex(hex))

	if let value = try reader.readBoolean() 'read'
		print("{id} {value}\n")
	end 'read' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'readBoolean'

function readSigned(id String, hex String)
	var reader = DerReader.create(fromHex(hex))

	if let content = try reader.readSignedInteger() 'read'
		print("{id} [{toHex(content)}]\n")
	end 'read' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'readSigned'

function readImplicit(id String, tagNumber DerTagNumber, hex String)
	var reader = DerReader.create(fromHex(hex))

	if let content = try reader.readImplicit(tagNumber) 'read'
		print("{id} [{toHex(content)}]\n")
	end 'read' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'readImplicit'

function readImplicitBits(id String, tagNumber DerTagNumber, hex String)
	var reader = DerReader.create(fromHex(hex))

	if let bits = try reader.readImplicitBitString(tagNumber) 'read'
		print("{id} {bits.unusedBits()} [{toHex(bits.bytes())}]\n")
	end 'read' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'readImplicitBits'

function readImplicitSigned(id String, tagNumber DerTagNumber, hex String)
	var reader = DerReader.create(fromHex(hex))

	if let content = try reader.readImplicitSignedInteger(tagNumber) 'read'
		print("{id} [{toHex(content)}]\n")
	end 'read' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'readImplicitSigned'

function readElements(id String, hex String)
	var reader = DerReader.create(fromHex(hex))

	while not reader.isAtEnd() 'eachElement'
		let element = try reader.readElement() otherwise (failure) 'refused'
			print("{id} {errorName(failure)}\n")
			return
		end 'refused'

		print("{id} [{toHex(element)}]\n")
	end 'eachElement'
end 'readElements'

function readText(id String, hex String)
	var reader = DerReader.create(fromHex(hex))

	if let text = try reader.readString() 'read'
		print("{id} [{toHex(text.toByteArray())}]\n")
	end 'read' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'readText'

function main() returns ExitCode
	readBoolean("bool-001", hex: "0101ff")
	readBoolean("bool-002", hex: "010100")
	readBoolean("bool-003", hex: "010101")
	readBoolean("bool-004", hex: "0102ffff")
	readBoolean("bool-005", hex: "0100")
	readBoolean("bool-006", hex: "020100")
	readSigned("signed-001", hex: "0202fb2e")
	readSigned("signed-002", hex: "020100")
	readSigned("signed-003", hex: "0201ff")
	readSigned("signed-004", hex: "0202ff80")
	readSigned("signed-005", hex: "02020001")
	readSigned("signed-006", hex: "0200")
	readImplicit("implicit-001", tagNumber: 2, hex: "820461626364")
	readImplicit("implicit-002", tagNumber: 2, hex: "a2020500")
	readImplicit("implicit-003", tagNumber: 2, hex: "8100")
	readImplicit("implicit-004", tagNumber: 0, hex: "0400")
	readImplicitBits("implicit-bits-001", tagNumber: 1, hex: "810300abcd")
	readImplicitBits("implicit-bits-002", tagNumber: 1, hex: "81020101")
	readImplicitSigned("implicit-signed-001", tagNumber: 2, hex: "820105")
	readImplicitSigned("implicit-signed-002", tagNumber: 2, hex: "82020005")
	readElements("element-001", hex: "300302010504020102")
	readElements("element-002", hex: "3005020105")
	readText("string-001", hex: "0c03e282ac")
	readText("string-002", hex: "0c02c328")
	readText("string-003", hex: "0c02c0af")
	readText("string-004", hex: "0c03eda080")
	readText("string-005", hex: "0c04f4908080")
	readText("string-006", hex: "0c01e2")
	readText("string-007", hex: "13054162632d3f")
	readText("string-008", hex: "13012a")
	readText("string-009", hex: "160140")
	readText("string-010", hex: "160180")
	readText("string-011", hex: "1401c9")
	readText("string-012", hex: "1e0400e9d55c")
	readText("string-013", hex: "1e03006100")
	readText("string-014", hex: "1e02d800")
	readText("string-015", hex: "1c040001f600")
	readText("string-016", hex: "1c0400110000")
	readText("string-017", hex: "1c03000041")
	readText("string-018", hex: "0400")
	readText("string-019", hex: "2c00")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
bool-001 true
bool-002 false
bool-003 invalidBoolean
bool-004 invalidBoolean
bool-005 invalidBoolean
bool-006 unexpectedTag
signed-001 [fb2e]
signed-002 [00]
signed-003 [ff]
signed-004 nonMinimalInteger
signed-005 nonMinimalInteger
signed-006 emptyInteger
implicit-001 [61626364]
implicit-002 unexpectedTag
implicit-003 unexpectedTag
implicit-004 unexpectedTag
implicit-bits-001 0 [abcd]
implicit-bits-002 invalidBitString
implicit-signed-001 [05]
implicit-signed-002 nonMinimalInteger
element-001 [3003020105]
element-001 [04020102]
element-002 truncated
string-001 [e282ac]
string-002 invalidString
string-003 invalidString
string-004 invalidString
string-005 invalidString
string-006 invalidString
string-007 [4162632d3f]
string-008 invalidString
string-009 [40]
string-010 invalidString
string-011 [c389]
string-012 [c3a9ed959c]
string-013 invalidString
string-014 invalidString
string-015 [f09f9880]
string-016 invalidString
string-017 invalidString
string-018 unexpectedTag
string-019 unexpectedTag
```
