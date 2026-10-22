+++
name = "hmac"
version = "1"
title = "HMAC (Keyed-Hash Message Authentication Code) with SHA-2"
status = "draft"
keywords = ["mac", "hmac", "authentication", "sha256", "sha384", "sha512", "constant-time", "cryptography"]
difficulty = "easy"

[api]
functions = ["hmac", "constant_time_equal"]
types = ["HmacError"]

[references]
items = [
  { label = "RFC 2104", url = "https://www.rfc-editor.org/rfc/rfc2104.html" },
  { label = "FIPS 198-1", url = "https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.198-1.pdf" },
  { label = "RFC 4231", url = "https://www.rfc-editor.org/rfc/rfc4231.html" },
  { label = "sha-2 (Canra spec)" },
]

[test-vectors]
inline = true
external = []
+++

## Overview

This spec defines HMAC (RFC 2104, FIPS 198-1) over the SHA-2 hashes SHA-256, SHA-384 and SHA-512, and a constant-time byte comparison for checking a received tag:

- **hmac** -- computes the full-length HMAC of a message under a key.
- **constant_time_equal** -- compares two byte strings in time that depends only on their lengths.

The hash functions are those of the `sha-2` spec.

**Out of scope:** HMAC over SHA-1, SHA-224, SHA-512/t, MD5 or SHA-3; truncated output (callers truncate, see Edge Cases); an incremental (streaming) HMAC interface; tag verification as a single call (compose `hmac` and `constant_time_equal`); key generation and key storage.

## Types

### Algorithm names

`algorithm` is one of exactly three lowercase strings:

| `algorithm` | Hash | Block size `B` | Output size `L` |
|---|---|---|---|
| `"sha256"` | SHA-256 | 64 bytes | 32 bytes |
| `"sha384"` | SHA-384 | 128 bytes | 48 bytes |
| `"sha512"` | SHA-512 | 128 bytes | 64 bytes |

A statically typed implementation MAY use an enumeration instead of a string; it then has no `UnsupportedAlgorithm` case at run time, and the vectors that expect it are rejected at compile time instead.

### HmacError

```pseudocode
type HmacError =
  | UnsupportedAlgorithm   // algorithm is not one of "sha256", "sha384", "sha512"
```

## Functions

### hmac

```pseudocode
hmac(algorithm: String, key: Bytes, message: Bytes) -> Bytes | HmacError
```

Computes `HMAC-H(key, message)`.

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `algorithm` | String | yes | `"sha256"`, `"sha384"` or `"sha512"` |
| `key` | Bytes | yes | The secret key, of any length including zero |
| `message` | Bytes | yes | The message, of any length including zero |

**Returns:** The `L`-byte tag (32, 48 or 64 bytes), or `UnsupportedAlgorithm`.

**Algorithm:**

1. If `algorithm` is not exactly `"sha256"`, `"sha384"` or `"sha512"`, return `UnsupportedAlgorithm`. Let `H`, `B` and `L` be its hash, block size and output size.
2. If `length(key) > B`, set `key = H(key)`.
3. Right-pad `key` with zero bytes to exactly `B` bytes, giving `K0`.
4. `ipad_key = K0 XOR (0x36 repeated B times)`; `opad_key = K0 XOR (0x5c repeated B times)`.
5. `inner = H(ipad_key || message)`.
6. Return `H(opad_key || inner)`.

### constant_time_equal

```pseudocode
constant_time_equal(a: Bytes, b: Bytes) -> Bool
```

Returns true when `a` and `b` have the same length and the same bytes.

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `a` | Bytes | yes | First byte string, typically the expected tag |
| `b` | Bytes | yes | Second byte string, typically the received tag |

**Returns:** `true` if equal, otherwise `false`. There is no error case.

**Algorithm:**

1. If `length(a) != length(b)`, return `false`.
2. `diff = 0`.
3. For each index `i` in `0 .. length(a)-1`: `diff = diff OR (a[i] XOR b[i])`.
4. Return `diff == 0`.

## Behavioral Contracts

### hmac()

**MUST:**

1. MUST produce the value RFC 2104 / FIPS 198-1 define, at full length `L`; it MUST NOT truncate.
2. MUST accept keys of every length: an empty key, a key shorter than `B`, exactly `B` bytes, and longer than `B` (hashed first).
3. MUST accept an empty message.
4. MUST match `algorithm` exactly and case-sensitively; `"SHA256"`, `"sha-256"` and `""` are `UnsupportedAlgorithm`.
5. MUST NOT modify `key` or `message`.

**SHOULD:**

1. SHOULD overwrite its copies of the padded key and intermediate hash values before returning.
2. SHOULD offer a way to precompute the inner and outer hash states for a key that signs many messages; the result MUST be identical.

### constant_time_equal()

**MUST:**

1. MUST return `false` for inputs of different lengths, `true` for two empty inputs, and otherwise `true` exactly when every byte matches.
2. For inputs of equal length, the running time and memory access pattern MUST NOT depend on the byte values or on the position of the first difference: no early exit, no data-dependent branch, no data-dependent table lookup.

**MAY:**

1. MAY return early when the lengths differ: the length of a tag is not secret.

## Test Vectors

Keys, messages and tags are lowercase hex in `key_hex`, `message_hex`, `a_hex` and `b_hex`. Expected tags are `result_hex`.

### Sources

- `rfc4231-case-N` vectors are RFC 4231 section 4 test cases 1 to 7, each for SHA-256, SHA-384 and SHA-512. RFC 4231 prints test case 3's key line without its `=` sign; the key is twenty `0xaa` bytes as stated.
- Test case 5 (tagged `untruncated`) is published only as the first 128 bits of each tag, because RFC 4231 uses it to illustrate truncation. This spec never truncates, so its vectors carry the full tag; the first 16 bytes of each equal the RFC 4231 value (`a3b6167473100ee06e0c796c2955552b`, `3abf34c3503b2a23a46efc619baef897` and `415fad6271580a531d4179bc891d87a6`).
- `empty-message` vectors are Wycheproof `hmac_sha256_test.json`, `hmac_sha384_test.json` and `hmac_sha512_test.json`, tcId 1.
- `key-one-block` vectors are the first full-length entries with `Klen` equal to the block size (64 for SHA-256, 128 for SHA-384/512) in the NIST CAVP `HMAC.rsp` file.
- `empty-key` vectors use an empty key and an empty message. The SHA-256 value is the widely published `HMAC_SHA256("", "")`; the SHA-384 and SHA-512 values were computed from the definition with two independent implementations.

### hmac()

```test-vectors
{"id": "hmac-001", "function": "hmac", "input": {"args": {"algorithm": "sha256", "key_hex": "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b", "message_hex": "4869205468657265"}}, "expected": {"result_hex": "b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7"}, "tags": ["rfc4231", "rfc4231-case-1"]}
{"id": "hmac-002", "function": "hmac", "input": {"args": {"algorithm": "sha384", "key_hex": "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b", "message_hex": "4869205468657265"}}, "expected": {"result_hex": "afd03944d84895626b0825f4ab46907f15f9dadbe4101ec682aa034c7cebc59cfaea9ea9076ede7f4af152e8b2fa9cb6"}, "tags": ["rfc4231", "rfc4231-case-1"]}
{"id": "hmac-003", "function": "hmac", "input": {"args": {"algorithm": "sha512", "key_hex": "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b", "message_hex": "4869205468657265"}}, "expected": {"result_hex": "87aa7cdea5ef619d4ff0b4241a1d6cb02379f4e2ce4ec2787ad0b30545e17cdedaa833b7d6b8a702038b274eaea3f4e4be9d914eeb61f1702e696c203a126854"}, "tags": ["rfc4231", "rfc4231-case-1"]}
{"id": "hmac-004", "function": "hmac", "input": {"args": {"algorithm": "sha256", "key_hex": "4a656665", "message_hex": "7768617420646f2079612077616e7420666f72206e6f7468696e673f"}}, "expected": {"result_hex": "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843"}, "tags": ["rfc4231", "rfc4231-case-2"]}
{"id": "hmac-005", "function": "hmac", "input": {"args": {"algorithm": "sha384", "key_hex": "4a656665", "message_hex": "7768617420646f2079612077616e7420666f72206e6f7468696e673f"}}, "expected": {"result_hex": "af45d2e376484031617f78d2b58a6b1b9c7ef464f5a01b47e42ec3736322445e8e2240ca5e69e2c78b3239ecfab21649"}, "tags": ["rfc4231", "rfc4231-case-2"]}
{"id": "hmac-006", "function": "hmac", "input": {"args": {"algorithm": "sha512", "key_hex": "4a656665", "message_hex": "7768617420646f2079612077616e7420666f72206e6f7468696e673f"}}, "expected": {"result_hex": "164b7a7bfcf819e2e395fbe73b56e0a387bd64222e831fd610270cd7ea2505549758bf75c05a994a6d034f65f8f0e6fdcaeab1a34d4a6b4b636e070a38bce737"}, "tags": ["rfc4231", "rfc4231-case-2"]}
{"id": "hmac-007", "function": "hmac", "input": {"args": {"algorithm": "sha256", "key_hex": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "message_hex": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"}}, "expected": {"result_hex": "773ea91e36800e46854db8ebd09181a72959098b3ef8c122d9635514ced565fe"}, "tags": ["rfc4231", "rfc4231-case-3"]}
{"id": "hmac-008", "function": "hmac", "input": {"args": {"algorithm": "sha384", "key_hex": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "message_hex": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"}}, "expected": {"result_hex": "88062608d3e6ad8a0aa2ace014c8a86f0aa635d947ac9febe83ef4e55966144b2a5ab39dc13814b94e3ab6e101a34f27"}, "tags": ["rfc4231", "rfc4231-case-3"]}
{"id": "hmac-009", "function": "hmac", "input": {"args": {"algorithm": "sha512", "key_hex": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "message_hex": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"}}, "expected": {"result_hex": "fa73b0089d56a284efb0f0756c890be9b1b5dbdd8ee81a3655f83e33b2279d39bf3e848279a722c806b485a47e67c807b946a337bee8942674278859e13292fb"}, "tags": ["rfc4231", "rfc4231-case-3"]}
{"id": "hmac-010", "function": "hmac", "input": {"args": {"algorithm": "sha256", "key_hex": "0102030405060708090a0b0c0d0e0f10111213141516171819", "message_hex": "cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd"}}, "expected": {"result_hex": "82558a389a443c0ea4cc819899f2083a85f0faa3e578f8077a2e3ff46729665b"}, "tags": ["rfc4231", "rfc4231-case-4"]}
{"id": "hmac-011", "function": "hmac", "input": {"args": {"algorithm": "sha384", "key_hex": "0102030405060708090a0b0c0d0e0f10111213141516171819", "message_hex": "cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd"}}, "expected": {"result_hex": "3e8a69b7783c25851933ab6290af6ca77a9981480850009cc5577c6e1f573b4e6801dd23c4a7d679ccf8a386c674cffb"}, "tags": ["rfc4231", "rfc4231-case-4"]}
{"id": "hmac-012", "function": "hmac", "input": {"args": {"algorithm": "sha512", "key_hex": "0102030405060708090a0b0c0d0e0f10111213141516171819", "message_hex": "cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd"}}, "expected": {"result_hex": "b0ba465637458c6990e5a8c5f61d4af7e576d97ff94b872de76f8050361ee3dba91ca5c11aa25eb4d679275cc5788063a5f19741120c4f2de2adebeb10a298dd"}, "tags": ["rfc4231", "rfc4231-case-4"]}
{"id": "hmac-013", "function": "hmac", "input": {"args": {"algorithm": "sha256", "key_hex": "0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c", "message_hex": "546573742057697468205472756e636174696f6e"}}, "expected": {"result_hex": "a3b6167473100ee06e0c796c2955552bfa6f7c0a6a8aef8b93f860aab0cd20c5"}, "tags": ["rfc4231", "rfc4231-case-5", "untruncated"]}
{"id": "hmac-014", "function": "hmac", "input": {"args": {"algorithm": "sha384", "key_hex": "0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c", "message_hex": "546573742057697468205472756e636174696f6e"}}, "expected": {"result_hex": "3abf34c3503b2a23a46efc619baef897f4c8e42c934ce55ccbae9740fcbc1af4ca62269e2a37cd88ba926341efe4aeea"}, "tags": ["rfc4231", "rfc4231-case-5", "untruncated"]}
{"id": "hmac-015", "function": "hmac", "input": {"args": {"algorithm": "sha512", "key_hex": "0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c", "message_hex": "546573742057697468205472756e636174696f6e"}}, "expected": {"result_hex": "415fad6271580a531d4179bc891d87a650188707922a4fbb36663a1eb16da008711c5b50ddd0fc235084eb9d3364a1454fb2ef67cd1d29fe6773068ea266e96b"}, "tags": ["rfc4231", "rfc4231-case-5", "untruncated"]}
{"id": "hmac-016", "function": "hmac", "input": {"args": {"algorithm": "sha256", "key_hex": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "message_hex": "54657374205573696e67204c6172676572205468616e20426c6f636b2d53697a65204b6579202d2048617368204b6579204669727374"}}, "expected": {"result_hex": "60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54"}, "tags": ["rfc4231", "rfc4231-case-6"]}
{"id": "hmac-017", "function": "hmac", "input": {"args": {"algorithm": "sha384", "key_hex": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "message_hex": "54657374205573696e67204c6172676572205468616e20426c6f636b2d53697a65204b6579202d2048617368204b6579204669727374"}}, "expected": {"result_hex": "4ece084485813e9088d2c63a041bc5b44f9ef1012a2b588f3cd11f05033ac4c60c2ef6ab4030fe8296248df163f44952"}, "tags": ["rfc4231", "rfc4231-case-6"]}
{"id": "hmac-018", "function": "hmac", "input": {"args": {"algorithm": "sha512", "key_hex": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "message_hex": "54657374205573696e67204c6172676572205468616e20426c6f636b2d53697a65204b6579202d2048617368204b6579204669727374"}}, "expected": {"result_hex": "80b24263c7c1a3ebb71493c1dd7be8b49b46d1f41b4aeec1121b013783f8f3526b56d037e05f2598bd0fd2215d6a1e5295e64f73f63f0aec8b915a985d786598"}, "tags": ["rfc4231", "rfc4231-case-6"]}
{"id": "hmac-019", "function": "hmac", "input": {"args": {"algorithm": "sha256", "key_hex": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "message_hex": "5468697320697320612074657374207573696e672061206c6172676572207468616e20626c6f636b2d73697a65206b657920616e642061206c6172676572207468616e20626c6f636b2d73697a6520646174612e20546865206b6579206e6565647320746f20626520686173686564206265666f7265206265696e6720757365642062792074686520484d414320616c676f726974686d2e"}}, "expected": {"result_hex": "9b09ffa71b942fcb27635fbcd5b0e944bfdc63644f0713938a7f51535c3a35e2"}, "tags": ["rfc4231", "rfc4231-case-7"]}
{"id": "hmac-020", "function": "hmac", "input": {"args": {"algorithm": "sha384", "key_hex": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "message_hex": "5468697320697320612074657374207573696e672061206c6172676572207468616e20626c6f636b2d73697a65206b657920616e642061206c6172676572207468616e20626c6f636b2d73697a6520646174612e20546865206b6579206e6565647320746f20626520686173686564206265666f7265206265696e6720757365642062792074686520484d414320616c676f726974686d2e"}}, "expected": {"result_hex": "6617178e941f020d351e2f254e8fd32c602420feb0b8fb9adccebb82461e99c5a678cc31e799176d3860e6110c46523e"}, "tags": ["rfc4231", "rfc4231-case-7"]}
{"id": "hmac-021", "function": "hmac", "input": {"args": {"algorithm": "sha512", "key_hex": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "message_hex": "5468697320697320612074657374207573696e672061206c6172676572207468616e20626c6f636b2d73697a65206b657920616e642061206c6172676572207468616e20626c6f636b2d73697a6520646174612e20546865206b6579206e6565647320746f20626520686173686564206265666f7265206265696e6720757365642062792074686520484d414320616c676f726974686d2e"}}, "expected": {"result_hex": "e37b6a775dc87dbaa4dfa9f96e5e3ffddebd71f8867289865df5a32d20cdc944b6022cac3c4982b10d5eeb55c3e4de15134676fb6de0446065c97440fa8c6a58"}, "tags": ["rfc4231", "rfc4231-case-7"]}
{"id": "hmac-022", "function": "hmac", "input": {"args": {"algorithm": "sha256", "key_hex": "1e225cafb90339bba1b24076d4206c3e79c355805d851682bc818baa4f5a7779", "message_hex": ""}}, "expected": {"result_hex": "b175b57d89ea6cb606fb3363f2538abd73a4c00b4a1386905bac809004cf1933"}, "tags": ["empty-message", "edge", "wycheproof"]}
{"id": "hmac-023", "function": "hmac", "input": {"args": {"algorithm": "sha384", "key_hex": "ee8df067857df2300fa71a10c30997178bb3796127b5ece5f2ccc170932be0e78ea9b0a5936c09157e671ce7ec9fc510", "message_hex": ""}}, "expected": {"result_hex": "a655184daf3346ffc6629d493c8442644e4996a2799e42e3306fa6f5b0967b6cf3a6f819bab89bce297d1d1a5907b2d0"}, "tags": ["empty-message", "edge", "wycheproof"]}
{"id": "hmac-024", "function": "hmac", "input": {"args": {"algorithm": "sha512", "key_hex": "5365244bb43f23f18dfc86c09d62db4741138bec1fbddc282d295e0a098eb5c3e37bd6f4cc16d5ce7d77b1d474a1eb4db313cc0c24e48992ac125196549df9a8", "message_hex": ""}}, "expected": {"result_hex": "d0a556bd1afa8df1ebf9e3ee683a8a2450a7c83eba2daf2e2ff2f953f0cd64da216e67134cf55578b205c8a1e241ba1369516a5ef4298b9c1d31e9d59fc04fe4"}, "tags": ["empty-message", "edge", "wycheproof"]}
{"id": "hmac-025", "function": "hmac", "input": {"args": {"algorithm": "sha256", "key_hex": "992868504d2564c4fb47bcbd4ae482d8fb0e8e56d7b81864e61986a0e25682daeb5b50177c095edc9e971da95c3210c376e723365ac33d1b4f391817f4c35124", "message_hex": "ed4f269a8851eb3154771516b27228155200778049b2dc1963f3ac32ba46ea1387cfbb9c39151a2cc406cdc13c3c9860a27eb0b7fe8a7201ad11552afd041e33f70e53d97c62f17194b66117028fa9071cc0e04bd92de4972cd54f719010a694e414d4977abed7ca6b90ba612df6c3d467cded85032598a48546804f9cf2ecfe"}}, "expected": {"result_hex": "2f8321f416b9bb249f113b13fc12d70e1668dc332839c10daa5717896cb70ddf"}, "tags": ["key-one-block", "edge", "nist-cavp"]}
{"id": "hmac-026", "function": "hmac", "input": {"args": {"algorithm": "sha384", "key_hex": "b619d9d07461c11bc9fb66117d61ed90001366bbffdbff583556777584b0d65244af5c7bdbf3b7358d7c791b966cc809760e57398d1896ace72d26cc59a6904fcd92365edafb8af7986c7d90b2af3bfdbcdb1593c78fbe8e3378bbb0c519152bf9cb51c19a02a12a8fd35cb6f8b3ac337a828711d6c8e0c4c13e1e6af090cdae", "message_hex": "5a81e711adfe5077dd8c8b57c95e8e1f3de39f4fc448c523bd3e7c72b1fdacd6e489dc0d2a34a39ffc6460c1cb962b7a94a30c04b5426a75ffcfc69f0c4ba934d3a3da2e7935d56d6b9079a2a97b016d653a35c2cc0ce19124f887a617c951ce4e58493b4209cc294f983cc20b16f63fd52e8451b1ad13bf5342275079818deb"}}, "expected": {"result_hex": "8b4258be4c094aa4056f332ede8c733772664b088ba22ef8caae7cefd77eceb35e83af8d9c1283cbbffe4a372b699c21"}, "tags": ["key-one-block", "edge", "nist-cavp"]}
{"id": "hmac-027", "function": "hmac", "input": {"args": {"algorithm": "sha512", "key_hex": "e9e4480d1c4a621e0c4e1505992556347a7ab34fd2b289910474766cc969116f8040d96dc5f66cdc4454fa7bcfb9f838af19195038467ab8a16e1cbc12e598e6fd250e21b2145f1e2e859cf73400be12a0c69749f7100847429875351d5a76970b9ccf700c2ca3ad72e9e4c0f0840e8cf488158136989b0891f867211350134a", "message_hex": "b82eefb2081bd14dab0e9e345248a34ade73f3291886b91ea3e8cc742fd884f6ee0ccdaf4c9879f4db12dba58cf491af2541a1d5ef6cc8b1af750ef5d8559ef7ff9cd56d8f599974be3aecd8c0f4c08f3ae50d86f9f822a1e4ca39fd2f0b4d78d22630733a24d8d63ecdf9555411daf205a761c39ef46ff6292e74129bc13a7f"}}, "expected": {"result_hex": "90093bdcc45da7338bd2efe92e30933b14f75582739c747f7572b3270b104f33af0c939e3c8ae53b2066fc8c97ccf38785cd2ec3d79e6946499d36121e44a3e7"}, "tags": ["key-one-block", "edge", "nist-cavp"]}
{"id": "hmac-028", "function": "hmac", "input": {"args": {"algorithm": "sha256", "key_hex": "", "message_hex": ""}}, "expected": {"result_hex": "b613679a0814d9ec772f95d778c35fc5ff1697c493715653c6c712144292c5ad"}, "tags": ["empty-key", "empty-message", "edge"]}
{"id": "hmac-029", "function": "hmac", "input": {"args": {"algorithm": "sha384", "key_hex": "", "message_hex": ""}}, "expected": {"result_hex": "6c1f2ee938fad2e24bd91298474382ca218c75db3d83e114b3d4367776d14d3551289e75e8209cd4b792302840234adc"}, "tags": ["empty-key", "empty-message", "edge"]}
{"id": "hmac-030", "function": "hmac", "input": {"args": {"algorithm": "sha512", "key_hex": "", "message_hex": ""}}, "expected": {"result_hex": "b936cee86c9f87aa5d3c6f2e84cb5a4239a5fe50480a6ec66b70ab5b1f4ac6730c6c515421b327ec1d69402e53dfb49ad7381eb067b338fd7b0cb22247225d47"}, "tags": ["empty-key", "empty-message", "edge"]}
```

### hmac() errors

```test-vectors
{"id": "hmac-err-001", "function": "hmac", "input": {"args": {"algorithm": "sha1", "key_hex": "0b0b0b0b", "message_hex": "6869"}}, "expected": {"error": "UnsupportedAlgorithm"}, "tags": ["unsupported-algorithm"]}
{"id": "hmac-err-002", "function": "hmac", "input": {"args": {"algorithm": "md5", "key_hex": "0b0b0b0b", "message_hex": "6869"}}, "expected": {"error": "UnsupportedAlgorithm"}, "tags": ["unsupported-algorithm"]}
{"id": "hmac-err-003", "function": "hmac", "input": {"args": {"algorithm": "SHA256", "key_hex": "0b0b0b0b", "message_hex": "6869"}}, "expected": {"error": "UnsupportedAlgorithm"}, "tags": ["unsupported-algorithm"]}
{"id": "hmac-err-004", "function": "hmac", "input": {"args": {"algorithm": "sha-256", "key_hex": "0b0b0b0b", "message_hex": "6869"}}, "expected": {"error": "UnsupportedAlgorithm"}, "tags": ["unsupported-algorithm"]}
{"id": "hmac-err-005", "function": "hmac", "input": {"args": {"algorithm": "sha224", "key_hex": "0b0b0b0b", "message_hex": "6869"}}, "expected": {"error": "UnsupportedAlgorithm"}, "tags": ["unsupported-algorithm"]}
{"id": "hmac-err-006", "function": "hmac", "input": {"args": {"algorithm": "", "key_hex": "0b0b0b0b", "message_hex": "6869"}}, "expected": {"error": "UnsupportedAlgorithm"}, "tags": ["unsupported-algorithm"]}
```

### constant_time_equal()

```test-vectors
{"id": "cte-001", "function": "constant_time_equal", "input": {"args": {"a_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "b_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"}}, "expected": {"result": true}, "tags": ["equal"]}
{"id": "cte-002", "function": "constant_time_equal", "input": {"args": {"a_hex": "00", "b_hex": "00"}}, "expected": {"result": true}, "tags": ["equal", "one-byte"]}
{"id": "cte-003", "function": "constant_time_equal", "input": {"args": {"a_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "b_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e20"}}, "expected": {"result": false}, "tags": ["unequal", "last-byte"]}
{"id": "cte-004", "function": "constant_time_equal", "input": {"args": {"a_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "b_hex": "800102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"}}, "expected": {"result": false}, "tags": ["unequal", "first-byte"]}
{"id": "cte-005", "function": "constant_time_equal", "input": {"args": {"a_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "b_hex": "fffefdfcfbfaf9f8f7f6f5f4f3f2f1f0efeeedecebeae9e8e7e6e5e4e3e2e1e0"}}, "expected": {"result": false}, "tags": ["unequal", "all-bytes"]}
{"id": "cte-006", "function": "constant_time_equal", "input": {"args": {"a_hex": "", "b_hex": ""}}, "expected": {"result": true}, "tags": ["empty", "edge"]}
{"id": "cte-007", "function": "constant_time_equal", "input": {"args": {"a_hex": "", "b_hex": "00"}}, "expected": {"result": false}, "tags": ["empty", "different-length", "edge"]}
{"id": "cte-008", "function": "constant_time_equal", "input": {"args": {"a_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "b_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e"}}, "expected": {"result": false}, "tags": ["different-length", "prefix", "edge"]}
{"id": "cte-009", "function": "constant_time_equal", "input": {"args": {"a_hex": "000102030405060708090a0b0c0d0e0f", "b_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"}}, "expected": {"result": false}, "tags": ["different-length", "prefix", "edge"]}
```

## Edge Cases

- **Truncated tags (RFC 4231 test case 5).** `hmac` always returns the full `L` bytes. A protocol that sends a truncated tag truncates the output itself (take the leftmost bytes, never fewer than half of `L` and never fewer than 10 bytes per RFC 2104 section 5), and the receiver compares the same number of leftmost bytes with `constant_time_equal`.
- **Empty key.** It is zero-padded to `B` bytes like any short key, so HMAC with an empty key equals HMAC with `B` zero bytes. The empty key is legal, though weak.
- **Key exactly `B` bytes.** It is used as is, not hashed; the `key-one-block` vectors catch an off-by-one in the `length(key) > B` test.
- **Key longer than `B`.** It is replaced by `H(key)`, which is shorter than `B` and then zero-padded (test cases 6 and 7). Two keys `k` and `H(k)` therefore produce the same tag; this is a known property of HMAC, not a bug.
- **Algorithm name spelling.** Only the three lowercase names are accepted; aliases such as `"SHA-256"` or `"sha2-256"` are `UnsupportedAlgorithm` rather than being guessed.
- **Comparing tags of different lengths.** `constant_time_equal` returns `false` without comparing bytes.
- **Comparing two empty strings.** `constant_time_equal("", "")` is `true`. A verifier MUST still reject an empty received tag on its own, because any expected tag from `hmac` is non-empty.

## Implementation Notes

- **Never compare tags with ordinary equality.** A byte-by-byte comparison that stops at the first mismatch lets an attacker learn a valid tag one byte at a time from response timing. Use `constant_time_equal`.
- **Defeat the optimizer.** Compilers can turn the accumulate-OR loop back into an early-exit loop. Use the platform primitive where one exists (for example `crypto/subtle.ConstantTimeCompare` in Go, `hmac.compare_digest` in Python, `CRYPTO_memcmp` in OpenSSL, `crypto.timingSafeEqual` in Node.js), or an optimization barrier on `diff`.
- **Hash the long key with the same algorithm.** A key longer than `B` is hashed with `H`, not with a fixed hash.
- **Block size, not output size.** The key is padded to the block size `B` (64 or 128), not to the output size `L`. SHA-384 has `B = 128`, the same as SHA-512.
- **Reuse.** For many messages under one key, precompute the hash states after absorbing `ipad_key` and `opad_key`, and copy them per message.

## References

- [RFC 2104 -- HMAC: Keyed-Hashing for Message Authentication](https://www.rfc-editor.org/rfc/rfc2104.html) -- the construction and the truncation guidance (section 5).
- [FIPS 198-1 -- The Keyed-Hash Message Authentication Code (HMAC)](https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.198-1.pdf) -- the NIST definition, identical in output.
- [RFC 4231 -- Identifiers and Test Vectors for HMAC-SHA-224, HMAC-SHA-256, HMAC-SHA-384, and HMAC-SHA-512](https://www.rfc-editor.org/rfc/rfc4231.html) -- test cases 1 to 7.
- [NIST CAVP Message Authentication test vectors](https://csrc.nist.gov/projects/cryptographic-algorithm-validation-program/message-authentication) -- `HMAC.rsp`, source of the block-sized-key vectors.
- [Project Wycheproof](https://github.com/C2SP/wycheproof) -- source of the empty-message vectors.
