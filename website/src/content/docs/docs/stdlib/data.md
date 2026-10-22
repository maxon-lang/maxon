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

## Der

`DerReader` reads DER, the strict binary encoding of ASN.1 (ITU-T X.690 section 10) that certificates and
keys use, one element at a time. Each read consumes the next element, checks its tag, and checks it against
the DER rules; a constructed element is entered as a new reader over its content.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `DerReader.create(bytes ByteArray)` | `DerReader` | — | A reader positioned at the first element of `bytes`. |
| `isAtEnd()` | `bool` | — | Whether every element has been read. |
| `finish()` | — | `DerError` | `trailingData` unless every element has been read. |
| `peekHeader()` | `DerHeader` | `DerError` | The next element's header, leaving the element unread. |
| `enterSequence()` | `DerReader` | `DerError` | A reader over the content of the next element, a SEQUENCE. |
| `enterSet()` | `DerReader` | `DerError` | A reader over the content of the next element, a SET. The order of a SET OF is not checked. |
| `enterExplicit(tagNumber DerTagNumber)` | `DerReader` | `DerError` | A reader over the content of the next element, a constructed context-specific `[tagNumber]`, the explicit tag X.509 writes. |
| `readInteger()` | `ByteArray` | `DerError` | A non-negative INTEGER's big-endian magnitude, without its sign octet. Zero is one zero octet. |
| `readObjectIdentifier()` | `ByteArray` | `DerError` | An OBJECT IDENTIFIER's content octets, such as `2a864886f70d010101` for `rsaEncryption`. |
| `readNull()` | — | `DerError` | Consumes a NULL. |
| `readBitString()` | `DerBitString` | `DerError` | A primitive BIT STRING. |
| `readOctetString()` | `ByteArray` | `DerError` | A primitive OCTET STRING's content. |
| `readUtcTime()` | `UnixSeconds` | `DerError` | A UTCTime `YYMMDDHHMMSSZ`. Years `50` to `99` are 1950 to 1999 and `00` to `49` are 2000 to 2049 (RFC 5280 section 4.1.2.5.1). |
| `readGeneralizedTime()` | `UnixSeconds` | `DerError` | A GeneralizedTime `YYYYMMDDHHMMSSZ`. Fractional seconds, which RFC 5280 forbids, are refused. |
| `readSignedInteger()` | `ByteArray` | `DerError` | An INTEGER of either sign, as its content octets: minimal big-endian two's complement. |
| `readBoolean()` | `bool` | `DerError` | A BOOLEAN, whose one content octet is `00` or `ff`. |
| `readString()` | `String` | `DerError` | A UTF8String, PrintableString, IA5String, TeletexString (each octet one ISO 8859-1 character), BMPString (UTF-16BE) or UniversalString (UTF-32BE), decoded. |
| `readElement()` | `ByteArray` | `DerError` | The next element, whatever its tag, as its whole encoding: identifier, length and content octets. |
| `readImplicit(tagNumber DerTagNumber)` | `ByteArray` | `DerError` | The content of a primitive context-specific `[tagNumber]`, the implicit tag X.509 writes on a key identifier or a dNSName. |
| `readImplicitBitString(tagNumber DerTagNumber)` | `DerBitString` | `DerError` | A primitive context-specific `[tagNumber]` holding an implicitly tagged BIT STRING. |
| `readImplicitSignedInteger(tagNumber DerTagNumber)` | `ByteArray` | `DerError` | A primitive context-specific `[tagNumber]` holding an implicitly tagged INTEGER, as `readSignedInteger` answers it. |

A `DerHeader` has `tagClass()`, a `DerTagClass` (`universal`, `application`, `contextSpecific` or
`private`), `isConstructed()`, `tagNumber()` and `length()`, the number of content octets. A `DerBitString`
has `bytes()`, the octets after the unused-bits octet, and `unusedBits()`, how many low bits of the last
octet are not part of the string.

| Name | Definition |
|------|------------|
| `DerTagNumber` | `int(0 to u32.max)` |
| `DerLength` | `int(0 to u64.max)`, implementing `Count` |
| `DerUnusedBits` | `int(0 to 7)` |

| `DerError` case | When |
|-----------------|------|
| `truncated` | The input ends inside a header, or an element's content runs past the input or past the element holding it. |
| `indefiniteLength` | The length octet `0x80`, BER's indefinite form. |
| `nonMinimalLength` | A long-form length the short form or fewer octets could carry. |
| `nonMinimalTagNumber` | A high-tag-number form holding a number below 31, or one with a leading `0x80` octet. |
| `tagNumberTooLarge` | A tag number above `DerTagNumber`. |
| `unexpectedTag` | The element is not the type the read asked for, the constructed form of a primitive type included. |
| `trailingData` | `finish` found an element left. |
| `emptyInteger` | An INTEGER with no content octets. |
| `nonMinimalInteger` | An INTEGER with a redundant leading `00` or `ff` octet. |
| `negativeInteger` | An INTEGER whose sign bit is set. |
| `invalidNull` | A NULL with content. |
| `invalidBitString` | A BIT STRING with no unused-bits octet, more than 7 unused bits, unused bits and no data, or a set unused bit. |
| `invalidObjectIdentifier` | An empty OBJECT IDENTIFIER, a subidentifier with a leading `0x80` octet, or a last octet with its continuation bit set. |
| `invalidTime` | A time not in the form above, or a date or clock time that does not exist. |
| `invalidBoolean` | A BOOLEAN that is not one octet of `00` or `ff`. |
| `invalidString` | Text outside its string type: malformed UTF-8, a PrintableString character outside `A-Z a-z 0-9`, space and `' ( ) + , - . / : = ?`, an IA5String octet above `7f`, a BMPString or UniversalString of partial units, or a surrogate or a value above U+10FFFF. |

```maxon
function hex(bytes ByteArray) returns String
	var text = ""

	for b in bytes 'each'
		text.append("{b:02x}")
	end 'each'

	return text
end 'hex'

function main() returns ExitCode
	var outer = DerReader.create(ByteArray from [0x30, 0x06, 0x02, 0x01, 0x05, 0x02, 0x01, 0x80])
	var numbers = try outer.enterSequence() otherwise return 1
	let first = try numbers.readInteger() otherwise return 1
	print("{hex(first)} {numbers.isAtEnd()}\n")

	_ = try numbers.readInteger() otherwise (e) 'refused'
		print("{e}\n")
		return 0
	end 'refused'

	return 1
end 'main'
```

Output: `05 false` and `negativeInteger`.

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

## Rsa

`Rsa` verifies RSA signatures under RSASSA-PKCS1-v1_5 and RSASSA-PSS, and makes RSASSA-PSS signatures
(RFC 8017), over SHA-256, SHA-384 or SHA-512. PSS uses MGF1 over the same hash and a salt as long as the
digest, the parameters of TLS 1.3's `rsa_pss_rsae_*` schemes.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Rsa.verifyPkcs1v15(publicKey ByteArray, hash HashAlgorithm, message ByteArray, signature ByteArray)` | `bool` | `RsaError` | Whether `signature` is the PKCS #1 v1.5 signature of `message`. The expected encoded block is built and compared whole, so no lenient parse of the signature's block can accept a forgery. |
| `Rsa.verifyPss(publicKey ByteArray, hash HashAlgorithm, message ByteArray, signature ByteArray)` | `bool` | `RsaError` | Whether `signature` is the PSS signature of `message`. A signature made with another salt length is `false`. |
| `Rsa.signPss(privateKey ByteArray, hash HashAlgorithm, message ByteArray, salt ByteArray)` | `ByteArray` | `RsaError` | The PSS signature of `message`, exactly as long as the modulus. Pass `hash.digestBytes()` fresh bytes from `Random.bytes` as `salt` for every signature. |

A public key is a DER SubjectPublicKeyInfo (RFC 5280), the form a certificate carries: `rsaEncryption` with
NULL parameters, a modulus of 2048 to 4096 bits that is odd, and an odd exponent `e` with `3 <= e < n`. A
private key is a DER PKCS #8 `PrivateKeyInfo` without attributes, holding a two-prime `RSAPrivateKey` with
the same modulus and exponent rules whose values agree: `p * q == n`, `e * d == 1` modulo `p - 1` and
`q - 1`, `dP` and `dQ` are `d` reduced modulo `p - 1` and `q - 1`, and `qInv * q == 1` modulo `p`. PEM keys
are decoded with [`Pem`](#pem) first.

The checks run in order and the first that fails is thrown:

| `RsaError` case | When |
|-----------------|------|
| `invalidPublicKey` | The public key breaks any rule above, or is not strict DER. |
| `invalidPrivateKey` | The private key breaks any rule above, or is not strict DER. |
| `invalidSignatureLength` | Verification only: the signature is not exactly as long as the modulus. |
| `invalidSaltLength` | `signPss` only: the salt is not exactly `hash.digestBytes()` long. |

Every other wrong signature, including one whose value is not below the modulus, verifies as `false`.

`signPss` takes time independent of the private key: the exponentiation walks the exponent in fixed
4-bit windows, picks each table entry by masking every entry, and never branches or indexes memory on a
secret value. The input is blinded by a random factor drawn from `Random.bytes`, and the signature is
checked with the public exponent before it is returned. When the operating system cannot supply random
bytes, `signPss` panics.

```maxon
function main() returns ExitCode
	let message = "hello".toByteArray()

	_ = try Rsa.verifyPss(ByteArray.create(), hash: HashAlgorithm.sha256, message: message, signature: ByteArray.create()) otherwise (e) 'refused'
		print("{e}\n")
		return 0
	end 'refused'

	return 1
end 'main'
```

Output: `invalidPublicKey`.

## Aead

`Aead` is authenticated encryption with associated data: `seal` encrypts a message and appends a 16-byte
tag over it and the associated data, and `open` checks the tag before decrypting. [`AesGcm`](#aesgcm) and
[`ChaCha20Poly1305`](#chacha20poly1305) implement it. Every key, nonce and message is a `ByteArray`, and
the work takes the same time whatever the key and the data hold.

| Requirement | Returns | Throws | Description |
|-------------|---------|--------|-------------|
| `seal(nonce ByteArray, plaintext ByteArray, additionalData ByteArray)` | `ByteArray` | `AeadError` | The ciphertext of `plaintext`, as long as it, followed by the tag. `additionalData` is authenticated and not encrypted. |
| `open(nonce ByteArray, sealed ByteArray, additionalData ByteArray)` | `ByteArray` | `AeadError` | The plaintext of what `seal` returned, given the same nonce and associated data. |

`AeadError` has four cases: `invalidKeyLength` and `invalidNonceLength` for a key or a 12-byte nonce of
the wrong size, `plaintextTooLong` from `seal` past the cipher's limit, and `authenticationFailed` from
`open` when the tag does not match or `sealed` is shorter than a tag. `open` returns no plaintext unless
the tag matches.

A nonce must never be used twice under one key: two messages sealed under one key and nonce reveal the
XOR of their plaintexts, and let an attacker forge tags.

```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	key.resize(32)
	var nonce = ByteArray.create()
	nonce.resize(12)

	let aead = try ChaCha20Poly1305.create(key) otherwise return 1
	let sealed = try aead.seal(nonce, plaintext: "hello".toByteArray(), additionalData: "v1".toByteArray()) otherwise return 1
	let opened = try aead.open(nonce, sealed: sealed, additionalData: "v1".toByteArray()) otherwise return 1
	print("{sealed.count()} {opened.count()}\n")

	_ = try aead.open(nonce, sealed: sealed, additionalData: "v2".toByteArray()) otherwise (e) 'refused'
		print("{e}\n")
		return 0
	end 'refused'

	return 1
end 'main'
```

Output: `21 5` and `authenticationFailed`.

## AesGcm

AES-GCM (NIST SP 800-38D) with a 12-byte nonce and a 16-byte tag, implementing [`Aead`](#aead).
`AesGcm.create(key ByteArray)` takes a 16-, 24- or 32-byte key and throws `AeadError.invalidKeyLength`
for any other. `seal` accepts up to 2^36 − 32 bytes of plaintext, the most its 32-bit block counter
covers.

## ChaCha20Poly1305

ChaCha20-Poly1305 (RFC 8439) with a 12-byte nonce and a 16-byte tag, implementing [`Aead`](#aead).
`ChaCha20Poly1305.create(key ByteArray)` takes a 32-byte key, keeping its own copy, and throws
`AeadError.invalidKeyLength` for any other. `seal` accepts up to 2^38 − 64 bytes of plaintext.

## Aes

The AES block cipher (FIPS 197), the primitive under [`AesGcm`](#aesgcm). It encrypts one 16-byte block
at a time, or a message in counter mode. The key schedule is computed once by `Aes.create`.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Aes.create(key ByteArray)` | `Aes` | `AesError` | The cipher for a 16-, 24- or 32-byte key: AES-128, AES-192 or AES-256. |
| `encryptBlock(block ByteArray)` | `ByteArray` | `AesError` | The encryption of one 16-byte block. |
| `decryptBlock(block ByteArray)` | `ByteArray` | `AesError` | The decryption of one 16-byte block. |
| `xorKeyStream(nonce ByteArray, counter AesCounter, data ByteArray)` | `ByteArray` | `AesKeyStreamError` | `data` XORed with the counter-mode key stream: each block's input is the 12-byte nonce followed by a 32-bit big-endian counter, `counter` for the first block. Encrypting and decrypting are the same call. |

`AesError` is `invalidKeyLength` or `invalidBlockLength`. `AesKeyStreamError` is `invalidNonceLength`, or
`counterOverflow` when `data` needs a block counter past 2^32 − 1. `AesCounter` is `int(0 to u32.max)`.

A bare block cipher is not authenticated: use [`AesGcm`](#aesgcm) to encrypt messages.

## ChaCha20

The ChaCha20 stream cipher (RFC 8439), the primitive under [`ChaCha20Poly1305`](#chacha20poly1305).

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `ChaCha20.xorKeyStream(key ByteArray, nonce ByteArray, counter ChaCha20Counter, data ByteArray)` | `ByteArray` | `ChaCha20Error` | `data` XORed with the key stream of a 32-byte key and a 12-byte nonce, starting at 64-byte block `counter`. Encrypting and decrypting are the same call. |

`ChaCha20Error` is `invalidKeyLength`, `invalidNonceLength`, or `counterOverflow` when `data` needs a block
counter past 2^32 − 1. `ChaCha20Counter` is `int(0 to u32.max)`.

## Poly1305

The Poly1305 one-time authenticator (RFC 8439), the tag under [`ChaCha20Poly1305`](#chacha20poly1305).

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `Poly1305.mac(key ByteArray, message ByteArray)` | `ByteArray` | `Poly1305Error` | The 16-byte tag of `message` under a 32-byte key. |

`Poly1305Error` has one case, `invalidKeyLength`. A key tags exactly one message: two tags under one key
let an attacker forge a third. Compare tags with [`constantTimeEqual`](#hmac).

## X25519

X25519 Diffie-Hellman key agreement (RFC 7748). Keys and the shared secret are 32 bytes, little-endian.

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `X25519.publicKey(privateKey ByteArray)` | `ByteArray` | `X25519Error` | The public key of a 32-byte private key: the clamped key times the base point `u = 9`. |
| `X25519.sharedSecret(privateKey ByteArray, peerPublicKey ByteArray)` | `ByteArray` | `X25519Error` | The secret shared by a private key and a peer's public key. |

`X25519Error` is `invalidLength` when a key is not exactly 32 bytes, or `allZeroSharedSecret` when the
secret is 32 zero bytes, which a low-order public key forces (TLS 1.3 requires the check). The private key
is clamped, bit 255 of the public key is ignored, a public key at or above 2^255 − 19 is reduced, and a
public key on the curve's twist is accepted. Both functions run in time independent of the private key.
Derive keys from the shared secret with [`hkdfExtract`](#hkdf), never use it directly.

## Ed25519

Ed25519 signature verification (RFC 8032).

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `Ed25519.verify(publicKey ByteArray, message ByteArray, signature ByteArray)` | `bool` | `Ed25519Error` | Whether a 64-byte signature of `message` is valid under a 32-byte public key. |

`Ed25519Error` has one case, `invalidLength`, for a public key that is not 32 bytes or a signature that
is not 64. Any other input is answered with a `bool`: the check is the cofactorless `[S]B = R + [k]A`, with
`k` the SHA-512 digest of `R`, the public key and the message reduced mod `L`. A public key that does not
decode, and an `S` at or above `L`, are `false`. Verification handles public data only and runs in
variable time.

## EcdhP256

Elliptic-curve Diffie-Hellman over NIST P-256 (secp256r1): the ECC CDH primitive of SP 800-56A with
cofactor 1, as the TLS 1.3 `secp256r1` key share uses it.

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `EcdhP256.publicKey(privateKey ByteArray)` | `ByteArray` | `EcdhError` | The 65-byte uncompressed SEC 1 point `04 ‖ x ‖ y` of `d · G`, for a 32-byte big-endian private key `d`. |
| `EcdhP256.sharedSecret(privateKey ByteArray, peerPublicKey ByteArray)` | `ByteArray` | `EcdhError` | The shared secret: the 32-byte big-endian x-coordinate of `d · Q`, leading zero bytes kept. |

`EcdhError` is `invalidPrivateKey` for a key that is not 32 bytes or whose value is 0 or at least the group
order `n` (it is never reduced), or `invalidPublicKey` for a peer key that is not 65 bytes starting with
`04` (compressed points included), has a coordinate at least `p`, or is not on the curve. The private key is
checked first. The private key is the caller's to choose, uniformly from `[1, n − 1]`.

Both calls run in time independent of the private key. Derive keys from the shared secret with
[`hkdfExtract`](#hkdf); never use it as a key directly.

```maxon
function main() returns ExitCode
	var alice = ByteArray.create()
	alice.growFilled(32, value: 1)
	var bob = ByteArray.create()
	bob.growFilled(32, value: 2)

	try 'agree'
		let fromAlice = EcdhP256.sharedSecret(alice, peerPublicKey: EcdhP256.publicKey(bob))
		let fromBob = EcdhP256.sharedSecret(bob, peerPublicKey: EcdhP256.publicKey(alice))
		print("{constantTimeEqual(fromAlice, b: fromBob)}\n")
	end 'agree' otherwise (failure) 'refused'
		match failure 'why'
			invalidPrivateKey then print("bad private key\n")
			invalidPublicKey then print("bad public key\n")
		end 'why'
	end 'refused'

	return 0
end 'main'
```

Output: `true`.

## Ecdsa

ECDSA signature verification (FIPS 186-5) for the two pairs TLS 1.3 uses, `ecdsa_secp256r1_sha256` and
`ecdsa_secp384r1_sha384`.

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `Ecdsa.verify(curve EcdsaCurve, publicKey ByteArray, message ByteArray, signature ByteArray)` | `bool` | `EcdsaError` | Whether `signature`, a DER `ECDSA-Sig-Value`, is valid for `message` under the uncompressed point `publicKey`. The message is hashed with the curve's hash. |

`EcdsaCurve` is `p256` (P-256 with SHA-256, 65-byte keys) or `p384` (P-384 with SHA-384, 97-byte keys).
`EcdsaError` is the first failure of, in order:

1. `invalidPublicKey`: the key is not `04 ‖ x ‖ y` at the curve's size, has a coordinate at least `p`, or
   is not on the curve;
2. `invalidSignatureEncoding`: the signature is not exactly one strict-DER `SEQUENCE` of two `INTEGER`s,
   each minimal and non-negative, with minimal definite lengths. A negative `INTEGER` is an encoding error.

A well-formed signature whose `r` or `s` is 0 or at least `n` returns `false`; neither is reduced. Both
`s` and `n − s` verify. Verification handles only public data.

## EcdsaP256

Deterministic ECDSA signing on P-256 with SHA-256 (RFC 6979), as a TLS 1.3 server with a P-256 certificate
signs `CertificateVerify`.

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `EcdsaP256.sign(privateKey ByteArray, message ByteArray)` | `ByteArray` | `EcdsaSignError` | The DER signature of `message` under a 32-byte big-endian private key, with the nonce of RFC 6979 section 3.2 over HMAC-SHA-256. |

`EcdsaSignError` has one case, `invalidPrivateKey`: a key that is not 32 bytes or whose value is 0 or at
least `n`. The signature is a function of the key and the message alone; `s` is not normalized to the low
half, and each `INTEGER` is minimal. The nonce, its inverse and the private key are handled in constant
time. [`Ecdsa.verify`](#ecdsa) with `EcdsaCurve.p256` accepts every signature it makes.

## X509

`X509` decodes X.509 certificates (RFC 5280) and validates the chain a TLS server presents, the way a TLS
client decides whether to trust it. Everything is strict DER, read with [`DerReader`](#der); signatures
are checked with [`Ecdsa`](#ecdsa), [`Rsa`](#rsa) and [`Ed25519`](#ed25519).

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `X509.parse(der ByteArray)` | `Certificate` | `X509Error` | The certificate one DER X.509 v1 or v3 certificate encodes. |
| `X509.verify(leaf ByteArray, intermediates ByteArrays, roots ByteArrays, host String, now UnixSeconds)` | `ByteArrays` | `X509VerifyError` | The chain from `leaf` to one of `roots`, leaf first, each certificate exactly as passed in, valid at `now` for the TLS server `host`. |

`parse` runs its steps in order and throws the first failure: the outer structure, the version (v1 or
v3), the serial, the signature algorithm (the TBSCertificate's copy must equal the outer one byte for
byte), issuer, validity, subject, the public key, the unique identifiers, the extensions, and the signature.

| `X509Error` case | When |
|------------------|------|
| `invalidEncoding` | Anything that is not strict DER (definite minimal lengths, single-octet tags, minimal INTEGERs, no encoded DEFAULT, a SET OF in order), an OBJECT IDENTIFIER arc above 2^63 - 1, or not the RFC 5280 structure. RSASSA-PSS parameters are read whole first, so an encoded DEFAULT there is `invalidEncoding` even when the parameters are also unsupported. |
| `unsupportedVersion` | A version other than v1 or v3. |
| `unsupportedAlgorithm` | A signature or key algorithm outside the tables below, or RSASSA-PSS parameters other than SHA-2 with MGF1 over the same hash and a salt as long as the digest. |
| `unhandledCriticalExtension` | A critical extension not listed below, or a critical nameConstraints holding a subtree that is neither a dNSName nor an iPAddress. |
| `duplicateExtension` | Two extensions with the same OID. |

The signature algorithms are `X509SignatureAlgorithm` cases: `ecdsaWithSha256`, `ecdsaWithSha384`,
`sha256WithRsaEncryption`, `sha384WithRsaEncryption`, `sha512WithRsaEncryption` (with NULL or absent
parameters), `rsassaPssSha256`, `rsassaPssSha384`, `rsassaPssSha512` and `ed25519`. The keys are
`X509KeyAlgorithm` cases: `rsa`, `ecP256` and `ecP384` (an uncompressed point) and `ed25519`. Key sizes and
whether a point is on its curve are not checked by `parse`.

A `Certificate` answers each field:

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `version()` | `CertificateVersion` | — | 1 or 3. |
| `serialNumber()` | `ByteArray` | — | The serial's content octets as encoded: two's complement, of any sign and length. |
| `signatureAlgorithm()` | `X509SignatureAlgorithm` | — | The algorithm the issuer signed with. |
| `issuer()`, `subject()` | `X509Name` | — | `der()`, the whole Name DER names are compared by, and `attributes()`, an `X509Attributes` in encoded order, each with `oid()` (dotted decimal), `value()` (the decoded text) and `shortName()` (`CN`, `O`, `OU` or `C`, throwing `X509FieldError.absent` for other types). |
| `notBefore()`, `notAfter()` | `UnixSeconds` | — | The validity period, both ends inclusive. |
| `publicKey()` | `X509PublicKey` | — | `algorithm()`, `der()` (the SubjectPublicKeyInfo, the form `Rsa` takes) and `keyBytes()` (the point, the Ed25519 key or the `RSAPublicKey` DER). |
| `basicConstraints()` | `X509BasicConstraints` | `X509FieldError` | `isCertificateAuthority()` and `pathLength()`, an `X509PathLength` that throws `absent` when there is none. |
| `keyUsage()` | `X509KeyUsages` | `X509FieldError` | The `X509KeyUsage` bits that are set, in bit order: `digitalSignature`, `contentCommitment`, `keyEncipherment`, `dataEncipherment`, `keyAgreement`, `keyCertSign`, `crlSign`, `encipherOnly`, `decipherOnly`. |
| `extendedKeyUsage()` | `StringArray` | `X509FieldError` | The purposes, dotted decimal, in encoded order. |
| `subjectAltName()` | `X509SubjectAltName` | `X509FieldError` | `dnsNames()`, a `StringArray`, and `ipAddresses()`, 4- or 16-byte `ByteArrays`. Other name forms are skipped. |
| `authorityKeyId()`, `subjectKeyId()` | `ByteArray` | `X509FieldError` | The key identifiers. |
| `nameConstraints()` | `X509NameConstraints` | `X509FieldError` | `permittedDnsNames()` and `excludedDnsNames()`, the dNSName subtrees as encoded, and `permittedIpRanges()` and `excludedIpRanges()`, each an address followed by its mask. |
| `tbsCertificate()` | `ByteArray` | — | The whole TBSCertificate DER: the signed message. |
| `signature()` | `ByteArray` | — | The signature octets. |

An extension-backed member throws `X509FieldError.absent` when the certificate does not carry the
extension.

`verify` first checks the leaf: it parses (otherwise `invalidCertificate`, or `unsupportedAlgorithm` when
that is why), is valid at `now`, its extended key usage allows TLS server authentication, and one of its
subjectAltName entries names `host` — the Common Name is never read. `host` is an IPv4 or IPv6 literal,
matched against `ipAddresses()` byte for byte, or a DNS name, matched case-insensitively against
`dnsNames()`, where `*` as a whole first label stands for exactly one label and must be followed by at least
two. Then it builds candidate chains depth first, roots before intermediates, each in the order given, at
most 10 certificates long, linking a certificate to an issuer whose subject DER equals its issuer DER and
whose subjectKeyIdentifier does not contradict its authorityKeyIdentifier. Certificates that do not parse
are left out of the pools. Each link is checked in order: the issuer is valid at `now`, is a CA whose
keyUsage (when present) allows certificate signing, has not exceeded its path length, holds the leaf's names
inside its name constraints, allows TLS server authentication, and its key verifies the child's signature.
The root is checked like any issuer, except that its own signature is not. The first candidate that passes
is returned; when none does, the first candidate's error is thrown. Every candidate issuer the search
considers counts against a budget of 100, as Go's `maxChainSignatureChecks` does, so a pool of same-named
intermediates cannot make the search exponential; each signature is verified at most once per call.

| `X509VerifyError` case | When |
|------------------------|------|
| `invalidCertificate` | The leaf does not parse. |
| `unsupportedAlgorithm` | The leaf's algorithm is not supported, a P-256 key signs with SHA-384 or a P-384 key with SHA-256, or an issuer's RSA modulus is outside 2048 to 4096 bits. |
| `expired`, `notYetValid` | A certificate in the chain is not valid at `now`. |
| `incompatibleKeyUsage` | A certificate's extended key usage lists neither serverAuth nor anyExtendedKeyUsage. |
| `hostnameMismatch` | No subjectAltName entry of the leaf names `host`. |
| `unknownAuthority` | No chain links the leaf to a root. |
| `notAuthorizedToSign` | An issuer is not a CA, or its keyUsage lacks keyCertSign. |
| `pathLengthExceeded` | More intermediates sit below an issuer than its pathLenConstraint allows. |
| `nameConstraintViolation` | A dNSName or iPAddress of the leaf, or `host`, falls outside an issuer's name constraints. |
| `badSignature` | A signature does not verify under its issuer's key. |
| `signatureCheckLimitExceeded` | The search considered more than 100 candidate issuers before a chain passed. |

Name constraints apply to every dNSName and iPAddress of the leaf and to `host`, from every CA in the chain,
the root included. A DNS name matches a subtree when its last labels equal the subtree's; a subtree with a
leading `.` needs at least one more label, and an empty one matches every name. A wildcard leaf name is
refused when an excluded subtree lies under it. An address matches a range of its own family whose masked
bits it shares. A CA with only dNSName subtrees leaves addresses unconstrained, and the reverse.

`verify` does not check revocation, certificate policies or Certificate Transparency. Pass the wall clock as
`now` in production; `X509.verify` takes the trusted roots as an argument.

| Name | Definition |
|------|------------|
| `ByteArrays` | `Array with ByteArray` |
| `CertificateVersion` | `int(1 to 3)` |
| `X509PathLength` | `int(0 to i64.max)` |
| `X509KeyUsages` | `Array with X509KeyUsage` |
| `X509Attributes` | `Array with X509Attribute` |

```maxon
function main() returns ExitCode
	let chain = try X509.verify(ByteArray.create(), intermediates: ByteArrays.create(), roots: ByteArrays.create(), host: "example.com", now: 1767225600) otherwise (e) 'refused'
		print("{e}\n")
		return 0
	end 'refused'

	print("{chain.count()}\n")
	return 1
end 'main'
```

Output: `invalidCertificate`.

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
