---
feature: base64
status: stable
keywords: [base64, Base64, Base64Error, encode, decode, rfc4648, encoding]
category: stdlib
---

# `Base64` — RFC 4648 base64, standard alphabet, strict

## Documentation

| Call | Meaning |
|---|---|
| `Base64.encode(data)` | the padded base64 text of `data`, `A-Z a-z 0-9 + /` with `=` padding and no line breaks |
| `Base64.decode(text)` | the bytes `text` encodes; throws a `Base64Error` for any text `encode` cannot produce |

`decode` accepts exactly the strings `encode` produces. Its checks run in this order, and the first that fails
decides the error:

1. a character that is neither in the alphabet nor `=`, whitespace included: `invalidSymbol`;
2. a length that is not a multiple of 4, unpadded text included: `invalidLength`;
3. `=` anywhere but as one or two final characters: `invalidPadding`;
4. non-zero pad bits in the last symbol before the padding: `invalidPadding`.

The vectors are RFC 4648 section 10, all 256 byte values, and the alphabet's edges.

## Tests

<!-- test: base64.encode -->
RFC 4648 section 10, all 256 byte values in order, and the first and last symbols with both pad lengths.
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

function report(id String, dataHex String)
	print("{id} [{Base64.encode(fromHex(dataHex))}]\n")
end 'report'

function main() returns ExitCode
	report("encode-001", dataHex: "")
	report("encode-002", dataHex: "66")
	report("encode-003", dataHex: "666f")
	report("encode-004", dataHex: "666f6f")
	report("encode-005", dataHex: "666f6f62")
	report("encode-006", dataHex: "666f6f6261")
	report("encode-007", dataHex: "666f6f626172")
	report("encode-008", dataHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff")
	report("encode-009", dataHex: "000000")
	report("encode-010", dataHex: "ffffff")
	report("encode-011", dataHex: "fbff")
	report("encode-012", dataHex: "00")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
encode-001 []
encode-002 [Zg==]
encode-003 [Zm8=]
encode-004 [Zm9v]
encode-005 [Zm9vYg==]
encode-006 [Zm9vYmE=]
encode-007 [Zm9vYmFy]
encode-008 [AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+P0BBQkNERUZHSElKS0xNTk9QUVJTVFVWV1hZWltcXV5fYGFiY2RlZmdoaWprbG1ub3BxcnN0dXZ3eHl6e3x9fn+AgYKDhIWGh4iJiouMjY6PkJGSk5SVlpeYmZqbnJ2en6ChoqOkpaanqKmqq6ytrq+wsbKztLW2t7i5uru8vb6/wMHCw8TFxsfIycrLzM3Oz9DR0tPU1dbX2Nna29zd3t/g4eLj5OXm5+jp6uvs7e7v8PHy8/T19vf4+fr7/P3+/w==]
encode-009 [AAAA]
encode-010 [////]
encode-011 [+/8=]
encode-012 [AA==]
```

<!-- test: base64.decode -->
The texts `encode` produces for the same inputs decode back to them.
```maxon
function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure Base64Error) returns String
	return match failure 'name'
		invalidSymbol gives "invalidSymbol"
		invalidLength gives "invalidLength"
		invalidPadding gives "invalidPadding"
	end 'name'
end 'errorName'

function report(id String, text String)
	if let bytes = try Base64.decode(text) 'decoded'
		print("{id} [{toHex(bytes)}]\n")
	end 'decoded' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("decode-001", text: "")
	report("decode-002", text: "Zg==")
	report("decode-003", text: "Zm8=")
	report("decode-004", text: "Zm9v")
	report("decode-005", text: "Zm9vYg==")
	report("decode-006", text: "Zm9vYmE=")
	report("decode-007", text: "Zm9vYmFy")
	report("decode-008", text: "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+P0BBQkNERUZHSElKS0xNTk9QUVJTVFVWV1hZWltcXV5fYGFiY2RlZmdoaWprbG1ub3BxcnN0dXZ3eHl6e3x9fn+AgYKDhIWGh4iJiouMjY6PkJGSk5SVlpeYmZqbnJ2en6ChoqOkpaanqKmqq6ytrq+wsbKztLW2t7i5uru8vb6/wMHCw8TFxsfIycrLzM3Oz9DR0tPU1dbX2Nna29zd3t/g4eLj5OXm5+jp6uvs7e7v8PHy8/T19vf4+fr7/P3+/w==")
	report("decode-009", text: "AAAA")
	report("decode-010", text: "////")
	report("decode-011", text: "+/8=")
	report("decode-012", text: "AA==")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
decode-001 []
decode-002 [66]
decode-003 [666f]
decode-004 [666f6f]
decode-005 [666f6f62]
decode-006 [666f6f6261]
decode-007 [666f6f626172]
decode-008 [000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff]
decode-009 [000000]
decode-010 [ffffff]
decode-011 [fbff]
decode-012 [00]
```

<!-- test: base64.decode-refuses-what-encode-cannot-produce -->
Missing padding and bad lengths, whitespace, URL-safe and non-ASCII symbols, misplaced and excess padding, and non-zero pad bits, each with the one error the check order gives it.
```maxon
function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure Base64Error) returns String
	return match failure 'name'
		invalidSymbol gives "invalidSymbol"
		invalidLength gives "invalidLength"
		invalidPadding gives "invalidPadding"
	end 'name'
end 'errorName'

function report(id String, text String)
	if let bytes = try Base64.decode(text) 'decoded'
		print("{id} [{toHex(bytes)}]\n")
	end 'decoded' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("decode-err-001", text: "Zg")
	report("decode-err-002", text: "Zm9vY")
	report("decode-err-003", text: "Zg=")
	report("decode-err-004", text: "Zm9vYmFy=")
	report("decode-err-005", text: "Zm9v Zg==")
	report("decode-err-006", text: "Zm9v\nZg==")
	report("decode-err-007", text: "Zm9v\r\n")
	report("decode-err-008", text: "Zm9-")
	report("decode-err-009", text: "Zm9_")
	report("decode-err-010", text: "Zm9v!g==")
	report("decode-err-011", text: "Zm9v\u00E9g==")
	report("decode-err-012", text: "Zg=a")
	report("decode-err-013", text: "Zg==Zg==")
	report("decode-err-014", text: "=Zm9")
	report("decode-err-015", text: "Z===")
	report("decode-err-016", text: "====")
	report("decode-err-017", text: "Zh==")
	report("decode-err-018", text: "Zm9=")
	report("decode-err-019", text: "Zg===")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
decode-err-001 invalidLength
decode-err-002 invalidLength
decode-err-003 invalidLength
decode-err-004 invalidLength
decode-err-005 invalidSymbol
decode-err-006 invalidSymbol
decode-err-007 invalidSymbol
decode-err-008 invalidSymbol
decode-err-009 invalidSymbol
decode-err-010 invalidSymbol
decode-err-011 invalidSymbol
decode-err-012 invalidPadding
decode-err-013 invalidPadding
decode-err-014 invalidPadding
decode-err-015 invalidPadding
decode-err-016 invalidPadding
decode-err-017 invalidPadding
decode-err-018 invalidPadding
decode-err-019 invalidLength
```
