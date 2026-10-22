---
title: Data, Encoding & Crypto
description: JSON, Base64, PEM, the SHA-2 hash functions, HMAC, HKDF, and FNV-1a hashing.
sidebar:
  order: 6
---

## Json

An RFC 8259 JSON parser and serializer. A `JsonDoc` owns a flat arena of `JsonNode`s; an array or object
refers to its children by `JsonNodeId`. Walk a document through the `JsonDoc` accessors.

### Json

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Json.parse(text String)` | `JsonDoc` | `JsonError` | Parse a document; `doc.root` is the top-level value. A number reads as the float nearest its decimal value; one too large for a float is `invalidNumber`, and one too small reads as zero. |
| `Json.stringify(doc JsonDoc)` | `String` | — | Compact output of the tree at `doc.root`. NaN and infinities are written as `null`. |
| `Json.stringifyPrettyNode(doc JsonDoc, root JsonNodeId)` | `String` | — | Indented output (two spaces per level) of the subtree at `root`. |
| `Json.quote(text String)` | `String` | — | `text` as one quoted JSON string value, with `"`, `\` and control characters escaped. |

### JsonDoc

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `JsonDoc.create()` | `JsonDoc` | — | An empty document. |
| `nodes` | field, `Array with JsonNode` | — | The arena. |
| `root` | field, `JsonNodeId` | — | The top-level node's id, set by a parse. For an array or object it is the last node added, because children are added before their parent. Assign it when building. |
| `add(node JsonNode)` | `JsonNodeId` | — | Append a node and return its id. |
| `get(id JsonNodeId)` | `JsonNode` | — | The node; an id outside the arena panics. |
| `rootKind()` | `JsonKind` | — | The kind of the root node. |
| `getChild(parent JsonNodeId, key String)` | `JsonNodeId` | `JsonAccessError` | An object member's id: `notObject` or `missingKey`. |
| `hasChild(parent JsonNodeId, key String)` | `bool` | — | Whether `parent` is an object holding `key`; `false` for any other kind of node. |
| `getString(parent, key:)` | `String` | `JsonAccessError` | A string member; `wrongType` for another kind. |
| `getInt(parent, key:)` | `int(i64.min to i64.max)` | `JsonAccessError` | A number member, truncated toward zero. |
| `getBool(parent, key:)` | `bool` | `JsonAccessError` | A boolean member. |
| `arrayLength(id JsonNodeId)` | `Count` | `JsonAccessError` | `notArray` for another kind. |
| `arrayAt(id JsonNodeId, index)` | `JsonNodeId` | `JsonAccessError` | `outOfBounds` past the end. |

### JsonNode

| Member | Description |
|--------|-------------|
| `kind` | `JsonKind` |
| `boolValue` | Set for `jsonBool` |
| `numberValue` | Set for `jsonNumber` (a `JsonFloat`) |
| `stringValue` | Set for `jsonString` |
| `children` | `JsonNodeIdArray`, for `jsonArray` and `jsonObject` |
| `keys` | `StringArray`, parallel to `children`, for `jsonObject` |
| `JsonNode.nullNode()` | A `null` |
| `JsonNode.boolNode(value bool)` | A boolean |
| `JsonNode.numberNode(value JsonFloat)` | A number |
| `JsonNode.stringNode(value String)` | A string |
| `JsonNode.arrayNode(children JsonNodeIdArray)` | An array of already-added nodes |
| `JsonNode.objectNode(keys StringArray, children JsonNodeIdArray)` | An object; `keys[i]` names `children[i]` |

### Kinds and errors

| Enum | Cases |
|------|-------|
| `JsonKind` | `jsonNull`, `jsonBool`, `jsonNumber`, `jsonString`, `jsonArray`, `jsonObject` |
| `JsonError` (from `parse`) | `unexpectedChar`, `unexpectedEof`, `invalidEscape`, `invalidNumber`, `invalidSurrogate`, `trailingContent` |
| `JsonAccessError` (from accessors) | `notObject`, `notArray`, `missingKey`, `wrongType`, `outOfBounds` |

`JsonNodeId` is `int(0 to u64.max)` and `JsonNodeIdArray` is `Array with JsonNodeId`.

```maxon
function main() returns ExitCode
	let doc = try Json.parse("\{\"name\": \"maxon\", \"stars\": 42, \"tags\": [\"a\", \"b\"]\}") otherwise (e) 'bad'
		print("invalid JSON: {e}\n")
		return 1
	end 'bad'

	let name = try doc.getString(doc.root, key: "name") otherwise "?"
	let stars = try doc.getInt(doc.root, key: "stars") otherwise 0
	let tags = try doc.getChild(doc.root, key: "tags") otherwise 0
	let second = try doc.arrayAt(tags, index: 1) otherwise 0
	print("{doc.rootKind()} {name} {stars} {doc.get(second).stringValue}\n")

	var built = JsonDoc.create()
	var keys = StringArray.create()
	var children = JsonNodeIdArray.create()
	keys.push("ok")
	children.push(built.add(JsonNode.boolNode(true)))
	keys.push("score")
	children.push(built.add(JsonNode.numberNode(1.5)))
	built.root = built.add(JsonNode.objectNode(keys, children: children))
	print("{Json.stringify(built)}\n")
	return 0
end 'main'
```

Output: `jsonObject maxon 42 b` and `{"ok":true,"score":1.5}`.

## Base64

`Base64` converts between bytes and base64 text with the standard alphabet and `=` padding (RFC 4648
section 4).

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Base64.encode(data ByteArray)` | `String` | — | The canonical encoding: padded to a multiple of 4 characters, on one line, with the unused bits of the last symbol zero. |
| `Base64.decode(text String)` | `ByteArray` | `Base64Error` | The bytes `text` encodes. It accepts exactly the strings `encode` produces. |

Decoding is strict, and its checks run in a fixed order, so every input has exactly one error:

| `Base64Error` case | When |
|--------------------|------|
| `invalidSymbol` | A character outside the alphabet, whitespace and line breaks included. |
| `invalidLength` | A length other than a multiple of 4, unpadded input included. |
| `invalidPadding` | `=` anywhere but as one or two final characters, or non-zero pad bits in the last symbol. |

```maxon
function main() returns ExitCode
	let text = Base64.encode("Maxon".toByteArray())
	let back = try Base64.decode(text) otherwise return 1
	print("{text} {String.from(back)}\n")

	let unpadded = try Base64.decode("TWF4b24") otherwise (e) 'refused'
		print("{e}\n")
		return 0
	end 'refused'

	print("{unpadded.count()}\n")
	return 1
end 'main'
```

Output: `TWF4b24= Maxon` and `invalidLength`.

## Pem

`Pem` reads and writes PEM text (RFC 7468): the `-----BEGIN CERTIFICATE-----` blocks that carry
certificates and keys, each a label and a base64 body.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Pem.decode(text String)` | `PemBlocks` | `PemError` | Every block in `text`, in order, duplicate labels kept. A text with no blocks gives an empty list. |
| `Pem.encode(label String, data ByteArray)` | `String` | `PemError` | One block: the BEGIN line, the base64 of `data` in lines of 64 characters, and the END line, each ending in LF. |

A `PemBlock` has `label()`, the text between `-----BEGIN ` and `-----` (such as `CERTIFICATE`), and
`bytes()`, the decoded body. `PemBlocks` is `Array with PemBlock`.

`decode` accepts LF and CRLF line endings and a last line with no line break. Text before, between and
after blocks is skipped, including a stray END line and a malformed BEGIN line. Trailing spaces and tabs
after a boundary's `-----` are allowed, and whitespace anywhere in a body is removed before the body is
decoded with `Base64.decode`.

| `PemError` case | When |
|-----------------|------|
| `unterminatedBlock` | A BEGIN line with no END line before the next BEGIN line or the end of the text. |
| `labelMismatch` | A line inside a block that starts with `-----END ` and names anything but the block's exact label. |
| `invalidBody` | A body that fails strict base64 decoding once whitespace is removed. |
| `invalidLabel` | `encode` only: a label RFC 7468 refuses. A label is empty, or printable ASCII characters joined by single spaces or hyphens. |

```maxon
function main() returns ExitCode
	let text = try Pem.encode("PUBLIC KEY", data: "key bytes".toByteArray()) otherwise return 1
	print(text)

	let blocks = try Pem.decode("a comment line\n{text}") otherwise return 1

	for block in blocks 'each'
		print("{block.label()}: {String.from(block.bytes())}\n")
	end 'each'

	return 0
end 'main'
```

Output:

```
-----BEGIN PUBLIC KEY-----
a2V5IGJ5dGVz
-----END PUBLIC KEY-----
PUBLIC KEY: key bytes
```

## Sha2

The SHA-2 hash functions of FIPS 180-4. `sha256`, `sha384` and `sha512` hash a whole `ByteArray`; a
`Digest` takes the message in chunks.

| Function | Returns | Description |
|----------|---------|-------------|
| `sha256(data ByteArray)` | `ByteArray` | The 32-byte SHA-256 digest. |
| `sha384(data ByteArray)` | `ByteArray` | The 48-byte SHA-384 digest. |
| `sha512(data ByteArray)` | `ByteArray` | The 64-byte SHA-512 digest. |

`HashAlgorithm` names one of them — `sha256`, `sha384` or `sha512` — for `Digest`, [`hmac`](#hmac) and
the [HKDF](#hkdf) functions.

| Member | Returns | Description |
|--------|---------|-------------|
| `blockBytes()` | `HashBlockBytes` | The block the function compresses: 64 bytes for `sha256`, 128 for `sha384` and `sha512`. |
| `digestBytes()` | `HashDigestBytes` | The digest size: 32, 48 or 64 bytes. |

| Name | Definition |
|------|------------|
| `HashBlockBytes` | `int(64 to 128)`, implementing `Count` |
| `HashDigestBytes` | `int(32 to 64)`, implementing `Count` |

A `Digest` hashes a message fed in chunks. The digest depends only on the bytes passed to `update`, so
every way of splitting a message gives its one-call digest.

| Member | Returns | Description |
|--------|---------|-------------|
| `Digest.create(algorithm HashAlgorithm)` | `Digest` | A new hash of the empty message, sharing nothing with any other. |
| `update(data ByteArray)` | — | Feed `data` into the hash. An empty `data` leaves it unchanged. |
| `finalize()` | `ByteArray` | The digest of every byte passed to `update`, in order. |

`finalize` ends a `Digest`: a later `update` or `finalize` on it panics.

```maxon
function hex(bytes ByteArray) returns String
	var text = ""

	for b in bytes 'each'
		text.append("{b:02x}")
	end 'each'

	return text
end 'hex'

function main() returns ExitCode
	var digest = Digest.create(HashAlgorithm.sha256)
	digest.update("a".toByteArray())
	digest.update("bc".toByteArray())

	print("{hex(digest.finalize())}\n")
	print("{sha384("abc".toByteArray()).count()} {sha512("abc".toByteArray()).count()}\n")
	return 0
end 'main'
```

Output: `ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad`, the SHA-256 digest of `abc`,
and `48 64`.

## Hmac

`hmac` computes a message authentication code (RFC 2104, FIPS 198-1) over a SHA-2 function, and
`constantTimeEqual` checks a received code against the computed one.

| Function | Returns | Description |
|----------|---------|-------------|
| `hmac(algorithm HashAlgorithm, key ByteArray, message ByteArray)` | `ByteArray` | The full-length HMAC of `message` under `key`: `algorithm.digestBytes()` bytes. A key of any length is accepted; one longer than `algorithm.blockBytes()` is hashed first. |
| `constantTimeEqual(a ByteArray, b ByteArray)` | `bool` | Whether `a` and `b` hold the same bytes. For equal lengths every byte pair is compared, so the running time reveals the length and nothing else. |

Compare codes with `constantTimeEqual`: a comparison that stops at the first differing byte tells an
attacker how much of a forged code was right.

```maxon
function main() returns ExitCode
	let key = "Jefe".toByteArray()
	let tag = hmac(HashAlgorithm.sha256, key: key, message: "what do ya want for nothing?".toByteArray())
	let received = hmac(HashAlgorithm.sha256, key: key, message: "what do ya want for nothing?".toByteArray())
	var hex = ""

	for b in tag 'each'
		hex.append("{b:02x}")
	end 'each'

	print("{hex} {constantTimeEqual(tag, b: received)}\n")
	return 0
end 'main'
```

Output: `5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843 true`.

## Hkdf

HKDF (RFC 5869) derives keys from secret input: `hkdfExtract` concentrates it into a pseudorandom key,
and `hkdfExpand` stretches that key into the bytes asked for, bound to an `info` string. `hkdf` does
both.

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `hkdfExtract(algorithm HashAlgorithm, salt ByteArray, inputKeyMaterial ByteArray)` | `ByteArray` | — | The pseudorandom key: the HMAC of `inputKeyMaterial` keyed by `salt`. An empty salt gives the same key as `algorithm.digestBytes()` zero bytes. |
| `hkdfExpand(algorithm HashAlgorithm, pseudoRandomKey ByteArray, info ByteArray, length HkdfOutputLength)` | `ByteArray` | `HkdfError` | `length` bytes of key material. A shorter output is a prefix of a longer one from the same inputs. |
| `hkdf(algorithm HashAlgorithm, salt ByteArray, inputKeyMaterial ByteArray, info ByteArray, length HkdfOutputLength)` | `ByteArray` | `HkdfError` | `hkdfExpand` of `hkdfExtract`, with `length` checked before anything is derived. |

`length` reaches 255 times `algorithm.digestBytes()`: 8160 bytes for SHA-256, 12240 for SHA-384 and
16320 for SHA-512. A longer one throws `HkdfError.outputTooLong`, the one case of `HkdfError`.
`HkdfOutputLength` is `int(0 to 16321)`, implementing `Count`.

```maxon
function main() returns ExitCode
	let secret = "shared secret".toByteArray()
	let key = try hkdf(HashAlgorithm.sha256, salt: ByteArray.create(), inputKeyMaterial: secret, info: "client key".toByteArray(), length: 16) otherwise return 1
	print("{key.count()}\n")

	_ = try hkdf(HashAlgorithm.sha256, salt: ByteArray.create(), inputKeyMaterial: secret, info: ByteArray.create(), length: 8161) otherwise (e) 'refused'
		print("{e}\n")
		return 0
	end 'refused'

	return 1
end 'main'
```

Output: `16` and `outputTooLong`.

## Hasher

`Hasher` is an incremental FNV-1a 64-bit hash, for content keys and cache digests. It is not cryptographic.
Its result is a `HashDigest`, `bits(64)`.

| Member | Returns | Description |
|--------|---------|-------------|
| `Hasher.create()` | `Hasher` | A hasher at the FNV-1a offset basis. |
| `Hasher.resume(state HashDigest)` | `Hasher` | Continue from a value `finalize()` returned. |
| `combine(value HashDigest)` | — | Fold in one value. |
| `combine(bytes ByteArray)` | — | Fold in each byte. |
| `finalize()` | `HashDigest` | The running state, not post-processed, so `resume(finalize())` continues exactly. |
| `Hasher.empty()` | `HashDigest` | The state of an empty fold. |
| `Hasher.combined(state HashDigest, value HashDigest)` | `HashDigest` | One step, as an expression. |
| `Hasher.combined(state HashDigest, bytes ByteArray)` | `HashDigest` | Each byte, as an expression. |

FNV-1a folds a flat byte sequence and records no lengths, so `"ab"` then `"c"` hashes the same as `"a"`
then `"bc"`. When hashing a sequence of parts, combine each part's length too.

```maxon
function main() returns ExitCode
	var hasher = Hasher.create()
	hasher.combine("ab".toByteArray())
	hasher.combine(7)

	let direct = Hasher.combined(Hasher.combined(Hasher.empty(), bytes: "ab".toByteArray()), value: 7)
	print("{hasher.finalize() == direct}\n")
	return 0
end 'main'
```

Output: `true`.
