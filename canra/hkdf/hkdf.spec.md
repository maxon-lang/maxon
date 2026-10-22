+++
name = "hkdf"
version = "1"
title = "HKDF (HMAC-based Extract-and-Expand Key Derivation Function)"
status = "draft"
keywords = ["kdf", "key-derivation", "hkdf", "hmac", "sha256", "sha384", "sha512", "cryptography"]
difficulty = "easy"

[api]
functions = ["extract", "expand", "derive"]
types = ["HkdfError"]

[references]
items = [
  { label = "RFC 5869", url = "https://www.rfc-editor.org/rfc/rfc5869.html" },
  { label = "hmac (Canra spec)" },
  { label = "Project Wycheproof HKDF test vectors", url = "https://github.com/C2SP/wycheproof/tree/main/testvectors_v1" },
]

[test-vectors]
inline = true
external = []
+++

## Overview

This spec defines HKDF as specified in RFC 5869, over HMAC-SHA-256, HMAC-SHA-384 and HMAC-SHA-512:

- **extract** -- condenses input keying material and an optional salt into a pseudorandom key (PRK).
- **expand** -- stretches a PRK and context information into output keying material (OKM) of a requested length.
- **derive** -- `extract` followed by `expand`, in one call.

HMAC is the `hmac` spec's `hmac` function.

**Out of scope:** HKDF over SHA-1, SHA-224 or SHA-3; other KDFs (PBKDF2, scrypt, Argon2, the TLS 1.3 `HKDF-Expand-Label` wrapper, NIST SP 800-108 counter mode); password hashing; streaming output.

## Types

### Algorithm names

`algorithm` is one of exactly three lowercase strings, with `HashLen` the hash output size:

| `algorithm` | Hash | `HashLen` | Maximum `length` (`255 * HashLen`) |
|---|---|---|---|
| `"sha256"` | SHA-256 | 32 | 8160 |
| `"sha384"` | SHA-384 | 48 | 12240 |
| `"sha512"` | SHA-512 | 64 | 16320 |

A statically typed implementation MAY use an enumeration instead of a string; the `UnsupportedAlgorithm` vectors are then compile-time rejections.

### HkdfError

```pseudocode
type HkdfError =
  | UnsupportedAlgorithm   // algorithm is not one of "sha256", "sha384", "sha512"
  | OutputTooLong          // length > 255 * HashLen
```

## Functions

### extract

```pseudocode
extract(algorithm: String, salt: Bytes, ikm: Bytes) -> Bytes | HkdfError
```

HKDF-Extract (RFC 5869 section 2.2).

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `algorithm` | String | yes | `"sha256"`, `"sha384"` or `"sha512"` |
| `salt` | Bytes | yes | Optional salt; pass empty bytes for "no salt" |
| `ikm` | Bytes | yes | Input keying material, any length including zero |

**Returns:** The `HashLen`-byte PRK, or `UnsupportedAlgorithm`.

**Algorithm:**

1. If `algorithm` is not exactly `"sha256"`, `"sha384"` or `"sha512"`, return `UnsupportedAlgorithm`.
2. If `salt` is empty, set `salt` to `HashLen` zero bytes.
3. Return `hmac(algorithm, key = salt, message = ikm)`.

### expand

```pseudocode
expand(algorithm: String, prk: Bytes, info: Bytes, length: Int) -> Bytes | HkdfError
```

HKDF-Expand (RFC 5869 section 2.3).

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `algorithm` | String | yes | `"sha256"`, `"sha384"` or `"sha512"` |
| `prk` | Bytes | yes | Pseudorandom key, normally the output of `extract` |
| `info` | Bytes | yes | Context and application-specific information; MAY be empty |
| `length` | Int | yes | Number of output bytes, `0 <= length <= 255 * HashLen` |

**Returns:** Exactly `length` bytes of OKM, or `UnsupportedAlgorithm`, or `OutputTooLong`.

**Algorithm:**

1. If `algorithm` is not supported, return `UnsupportedAlgorithm`.
2. If `length > 255 * HashLen`, return `OutputTooLong`.
3. `N = ceil(length / HashLen)`; `T = empty`; `okm = empty`.
4. For `i` in `1 .. N`: `T = hmac(algorithm, key = prk, message = T || info || byte(i))`; `okm = okm || T`.
5. Return the first `length` bytes of `okm`. For `length == 0` this is empty and no HMAC is computed.

### derive

```pseudocode
derive(algorithm: String, salt: Bytes, ikm: Bytes, info: Bytes, length: Int) -> Bytes | HkdfError
```

Full HKDF: `expand(algorithm, extract(algorithm, salt, ikm), info, length)`.

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `algorithm` | String | yes | `"sha256"`, `"sha384"` or `"sha512"` |
| `salt` | Bytes | yes | Optional salt; empty means `HashLen` zero bytes |
| `ikm` | Bytes | yes | Input keying material |
| `info` | Bytes | yes | Context information; MAY be empty |
| `length` | Int | yes | Number of output bytes, `0 <= length <= 255 * HashLen` |

**Returns:** Exactly `length` bytes of OKM, or `UnsupportedAlgorithm`, or `OutputTooLong`.

**Algorithm:**

1. If `algorithm` is not supported, return `UnsupportedAlgorithm`.
2. If `length > 255 * HashLen`, return `OutputTooLong`, before computing anything.
3. `prk = extract(algorithm, salt, ikm)`.
4. Return `expand(algorithm, prk, info, length)`.

## Behavioral Contracts

### extract()

**MUST:**

1. MUST return `HMAC-Hash(salt, ikm)`: the salt is the HMAC key and the IKM is the message, in that order.
2. MUST treat an empty salt exactly as `HashLen` zero bytes; both MUST give the same PRK.
3. MUST accept an empty IKM.

### expand()

**MUST:**

1. MUST return exactly `length` bytes for every `length` from 0 to `255 * HashLen` inclusive.
2. MUST return `OutputTooLong` for any `length` greater than `255 * HashLen`; it MUST NOT wrap the one-byte block counter or return a shorter output.
3. MUST use the block counter as a single byte starting at `0x01`, appended after `info`.
4. MUST accept a PRK of any length: it is an HMAC key. RFC 5869 asks callers for a PRK of at least `HashLen` bytes; enforcing that is the caller's concern.
5. The output for a given `length` MUST be a prefix of the output for any greater `length` with the same inputs.

### derive()

**MUST:**

1. MUST equal `expand(algorithm, extract(algorithm, salt, ikm), info, length)` for all inputs.
2. MUST check `length` before deriving anything.

**SHOULD:**

1. SHOULD overwrite the PRK and intermediate blocks before returning.

### Common

**MUST:**

1. Every function MUST match `algorithm` exactly and case-sensitively, and return `UnsupportedAlgorithm` for anything else, before any other check.

## Test Vectors

Binary arguments are lowercase hex in `salt_hex`, `ikm_hex`, `prk_hex` and `info_hex`; `length` is an integer. Expected output is `result_hex`.

### Sources

- `rfc5869-a1` to `rfc5869-a3` are RFC 5869 appendix A.1 to A.3 (SHA-256), each as an `extract` (PRK), `expand` (OKM) and `derive` (OKM) vector.
- `wycheproof-tc-N` vectors are from Project Wycheproof `hkdf_sha256_test.json`, `hkdf_sha384_test.json` and `hkdf_sha512_test.json` (test-case id `N`). Those files give `derive` outputs only.
- `zero-salt-equals-empty-salt` vectors pair with an `empty-salt` vector of the same IKM and info, and expect the same output. For SHA-256 the pair is RFC 5869 A.3 (as `extract`) and Wycheproof tcId 76/77; for SHA-384 and SHA-512 it is Wycheproof tcId 73/74.
- `length-zero` vectors take the inputs of Wycheproof tcId 1 (or RFC 5869 A.1 for `expand`) with `length` 0; the empty output follows directly from the definition.

### Maximum-length vectors

The `maximum-length` vectors request `255 * HashLen` bytes (Wycheproof "maximal output size"). Their output (8160, 12240 or 16320 bytes) exceeds the 10,000-character string limit of the vector format as hex, so they use an assertion instead of `result_hex`:

```pseudocode
length(result) == N and hex(sha256(result)) == '<64 hex digits>'
```

`length(result)` is the output length in bytes, `sha256` is SHA-256 of the whole output (the `sha-2` spec), and `hex` is lowercase hex. The digest was computed over the Wycheproof `okm` value, which was itself recomputed and matched.

### extract()

```test-vectors
{"id": "extract-001", "function": "extract", "input": {"args": {"algorithm": "sha256", "salt_hex": "000102030405060708090a0b0c", "ikm_hex": "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b"}}, "expected": {"result_hex": "077709362c2e32df0ddc3f0dc47bba6390b6c73bb50f9c3122ec844ad7c2b3e5"}, "tags": ["rfc5869", "rfc5869-a1"]}
{"id": "extract-002", "function": "extract", "input": {"args": {"algorithm": "sha256", "salt_hex": "606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeaf", "ikm_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f"}}, "expected": {"result_hex": "06a6b88c5853361a06104c9ceb35b45cef760014904671014a193f40c15fc244"}, "tags": ["rfc5869", "rfc5869-a2"]}
{"id": "extract-003", "function": "extract", "input": {"args": {"algorithm": "sha256", "salt_hex": "", "ikm_hex": "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b"}}, "expected": {"result_hex": "19ef24a32c717b167f33a91d6f648bdf96596776afdb6377ac434c1c293ccb04"}, "tags": ["rfc5869", "rfc5869-a3", "empty-salt", "empty-info"]}
{"id": "extract-004", "function": "extract", "input": {"args": {"algorithm": "sha256", "salt_hex": "0000000000000000000000000000000000000000000000000000000000000000", "ikm_hex": "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b"}}, "expected": {"result_hex": "19ef24a32c717b167f33a91d6f648bdf96596776afdb6377ac434c1c293ccb04"}, "tags": ["rfc5869", "rfc5869-a3", "zero-salt-equals-empty-salt", "edge"]}
```

### expand()

```test-vectors
{"id": "expand-001", "function": "expand", "input": {"args": {"algorithm": "sha256", "prk_hex": "077709362c2e32df0ddc3f0dc47bba6390b6c73bb50f9c3122ec844ad7c2b3e5", "info_hex": "f0f1f2f3f4f5f6f7f8f9", "length": 42}}, "expected": {"result_hex": "3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865"}, "tags": ["rfc5869", "rfc5869-a1"]}
{"id": "expand-002", "function": "expand", "input": {"args": {"algorithm": "sha256", "prk_hex": "06a6b88c5853361a06104c9ceb35b45cef760014904671014a193f40c15fc244", "info_hex": "b0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff", "length": 82}}, "expected": {"result_hex": "b11e398dc80327a1c8e7f78c596a49344f012eda2d4efad8a050cc4c19afa97c59045a99cac7827271cb41c65e590e09da3275600c2f09b8367793a9aca3db71cc30c58179ec3e87c14c01d5c1f3434f1d87"}, "tags": ["rfc5869", "rfc5869-a2"]}
{"id": "expand-003", "function": "expand", "input": {"args": {"algorithm": "sha256", "prk_hex": "19ef24a32c717b167f33a91d6f648bdf96596776afdb6377ac434c1c293ccb04", "info_hex": "", "length": 42}}, "expected": {"result_hex": "8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d9d201395faa4b61a96c8"}, "tags": ["rfc5869", "rfc5869-a3", "empty-salt", "empty-info"]}
{"id": "expand-004", "function": "expand", "input": {"args": {"algorithm": "sha256", "prk_hex": "077709362c2e32df0ddc3f0dc47bba6390b6c73bb50f9c3122ec844ad7c2b3e5", "info_hex": "f0f1f2f3f4f5f6f7f8f9", "length": 0}}, "expected": {"result_hex": ""}, "tags": ["length-zero", "edge"]}
```

### derive()

```test-vectors
{"id": "derive-001", "function": "derive", "input": {"args": {"algorithm": "sha256", "salt_hex": "000102030405060708090a0b0c", "ikm_hex": "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b", "info_hex": "f0f1f2f3f4f5f6f7f8f9", "length": 42}}, "expected": {"result_hex": "3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865"}, "tags": ["rfc5869", "rfc5869-a1"]}
{"id": "derive-002", "function": "derive", "input": {"args": {"algorithm": "sha256", "salt_hex": "606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeaf", "ikm_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f", "info_hex": "b0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff", "length": 82}}, "expected": {"result_hex": "b11e398dc80327a1c8e7f78c596a49344f012eda2d4efad8a050cc4c19afa97c59045a99cac7827271cb41c65e590e09da3275600c2f09b8367793a9aca3db71cc30c58179ec3e87c14c01d5c1f3434f1d87"}, "tags": ["rfc5869", "rfc5869-a2"]}
{"id": "derive-003", "function": "derive", "input": {"args": {"algorithm": "sha256", "salt_hex": "", "ikm_hex": "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b", "info_hex": "", "length": 42}}, "expected": {"result_hex": "8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d9d201395faa4b61a96c8"}, "tags": ["rfc5869", "rfc5869-a3", "empty-salt", "empty-info"]}
{"id": "derive-004", "function": "derive", "input": {"args": {"algorithm": "sha256", "salt_hex": "", "ikm_hex": "3ee1fc0d8fac49d494c4a1b8cf6bf290a4a2c19a27c3ab1914d0d21c841577e0", "info_hex": "4dc991623624fadef207587e42776cf3e0fdf4e5", "length": 32}}, "expected": {"result_hex": "f41f703259063d401de67cea9192038ddddc71ede5cdb383aad71894c1a39e8c"}, "tags": ["wycheproof", "empty-salt", "edge", "wycheproof-tc-76"]}
{"id": "derive-005", "function": "derive", "input": {"args": {"algorithm": "sha256", "salt_hex": "0000000000000000000000000000000000000000000000000000000000000000", "ikm_hex": "3ee1fc0d8fac49d494c4a1b8cf6bf290a4a2c19a27c3ab1914d0d21c841577e0", "info_hex": "4dc991623624fadef207587e42776cf3e0fdf4e5", "length": 32}}, "expected": {"result_hex": "f41f703259063d401de67cea9192038ddddc71ede5cdb383aad71894c1a39e8c"}, "tags": ["wycheproof", "zero-salt-equals-empty-salt", "edge", "wycheproof-tc-77"]}
{"id": "derive-006", "function": "derive", "input": {"args": {"algorithm": "sha384", "salt_hex": "", "ikm_hex": "24aeff2645e3e0f5494a9a102778c43a", "info_hex": "", "length": 20}}, "expected": {"result_hex": "4b7045423d9156424b0b85d95a7d602fba3924b1"}, "tags": ["wycheproof", "wycheproof-tc-1"]}
{"id": "derive-007", "function": "derive", "input": {"args": {"algorithm": "sha384", "salt_hex": "", "ikm_hex": "d04f44faa4542b34f958d8a863801f2c", "info_hex": "ff6b5b655cbf2421a66d2f28408395a2ce57264f76bd60befd51cbfd9474faa47a97a9aa0f0e4338635633023cb36da12a3051f1f369355b687304e74c5218b315", "length": 80}}, "expected": {"result_hex": "77565d63e3ca67928bb69a3ac37b19014f77a48a0faae7310730b5186f31498e9797120eab415d26d0084662174cd1d589f0c6d8226ce9870ebdd19b84838e047bb5613eb530a3ebd50be02b2e984a74"}, "tags": ["wycheproof", "empty-salt", "edge", "wycheproof-tc-23"]}
{"id": "derive-008", "function": "derive", "input": {"args": {"algorithm": "sha384", "salt_hex": "", "ikm_hex": "b77c1b4a865339d78c8e3ed7454ac6823b678d81126ce613c9e82ffab9e38ea88328d9eab8e88b409c1cedba7a74e8d7", "info_hex": "6a5253cb802bf83c3b3941aabc08d524beedf003", "length": 32}}, "expected": {"result_hex": "e376973755ee66c610613b236f3e1f999ed724d5651c79fdbd0389180be39cda"}, "tags": ["wycheproof", "empty-salt", "edge", "wycheproof-tc-73"]}
{"id": "derive-009", "function": "derive", "input": {"args": {"algorithm": "sha384", "salt_hex": "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000", "ikm_hex": "b77c1b4a865339d78c8e3ed7454ac6823b678d81126ce613c9e82ffab9e38ea88328d9eab8e88b409c1cedba7a74e8d7", "info_hex": "6a5253cb802bf83c3b3941aabc08d524beedf003", "length": 32}}, "expected": {"result_hex": "e376973755ee66c610613b236f3e1f999ed724d5651c79fdbd0389180be39cda"}, "tags": ["wycheproof", "zero-salt-equals-empty-salt", "edge", "wycheproof-tc-74"]}
{"id": "derive-010", "function": "derive", "input": {"args": {"algorithm": "sha384", "salt_hex": "1ce459b88ed2529c24ade0334476e89f869de08e7814604027a9aa1cbbed99b5691898ea46c8ca8f51ab970d6bffb6983a8158b0b82bdbcb98e3014e41d6e625c7", "ikm_hex": "b77c1b4a865339d78c8e3ed7454ac6823b678d81126ce613c9e82ffab9e38ea88328d9eab8e88b409c1cedba7a74e8d7", "info_hex": "6a5253cb802bf83c3b3941aabc08d524beedf003", "length": 32}}, "expected": {"result_hex": "7d3f1a70a9190cd64613f5eb9d18b6f050b4113cba21a99b68c0af395dd7601e"}, "tags": ["wycheproof", "salt-longer-than-block", "wycheproof-tc-75"]}
{"id": "derive-011", "function": "derive", "input": {"args": {"algorithm": "sha512", "salt_hex": "", "ikm_hex": "24aeff2645e3e0f5494a9a102778c43a", "info_hex": "", "length": 20}}, "expected": {"result_hex": "dd2599840b09699c6200b5cba79002b3aa75c61b"}, "tags": ["wycheproof", "wycheproof-tc-1"]}
{"id": "derive-012", "function": "derive", "input": {"args": {"algorithm": "sha512", "salt_hex": "", "ikm_hex": "d04f44faa4542b34f958d8a863801f2c", "info_hex": "ff6b5b655cbf2421a66d2f28408395a2ce57264f76bd60befd51cbfd9474faa47a97a9aa0f0e4338635633023cb36da12a3051f1f369355b687304e74c5218b315", "length": 80}}, "expected": {"result_hex": "76ebb3133cbf07afb653393bea4e4ecdab9d8e8fcc5e79e67d5346312ce9e46e937b3393170b811e25f7621923f74e519b604ae2b9884fe2d7202782ee9e20100f95341d8b6ff1f39db23bab7b7edcd6"}, "tags": ["wycheproof", "empty-salt", "edge", "wycheproof-tc-23"]}
{"id": "derive-013", "function": "derive", "input": {"args": {"algorithm": "sha512", "salt_hex": "", "ikm_hex": "ddba77f227f237c791c07ee7e1ce9d933ecad3614212cc98d4f12afc90a3b21f4c7c3b4c54f44ad7d1e8ce45cd36aad5cb0bbf84e90cb054a1e7dc558690c5fa", "info_hex": "6b478027231379b6bbed6c61732aad5636b5aab2", "length": 32}}, "expected": {"result_hex": "1a4d9bb4aebb50780a57fe8e922dfbf5ba6018236b2196a0f96d704b063f739e"}, "tags": ["wycheproof", "empty-salt", "edge", "wycheproof-tc-73"]}
{"id": "derive-014", "function": "derive", "input": {"args": {"algorithm": "sha512", "salt_hex": "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000", "ikm_hex": "ddba77f227f237c791c07ee7e1ce9d933ecad3614212cc98d4f12afc90a3b21f4c7c3b4c54f44ad7d1e8ce45cd36aad5cb0bbf84e90cb054a1e7dc558690c5fa", "info_hex": "6b478027231379b6bbed6c61732aad5636b5aab2", "length": 32}}, "expected": {"result_hex": "1a4d9bb4aebb50780a57fe8e922dfbf5ba6018236b2196a0f96d704b063f739e"}, "tags": ["wycheproof", "zero-salt-equals-empty-salt", "edge", "wycheproof-tc-74"]}
{"id": "derive-015", "function": "derive", "input": {"args": {"algorithm": "sha512", "salt_hex": "bd37d35e915d6a3431e8ab0fe1f1a3d129d9b6dfcdcb627ae7eb6d15068145cff05fa37e6bca0721e9ae696281759f6f1f6784db7c8d30c4b75342ec6d6a1cc2af", "ikm_hex": "ddba77f227f237c791c07ee7e1ce9d933ecad3614212cc98d4f12afc90a3b21f4c7c3b4c54f44ad7d1e8ce45cd36aad5cb0bbf84e90cb054a1e7dc558690c5fa", "info_hex": "6b478027231379b6bbed6c61732aad5636b5aab2", "length": 32}}, "expected": {"result_hex": "54b3b9a5d7f1713194b8e0621d8d33c6f2034b5005d47bbc90e5883d6af9bc42"}, "tags": ["wycheproof", "salt-longer-than-block", "wycheproof-tc-75"]}
{"id": "derive-016", "function": "derive", "input": {"args": {"algorithm": "sha256", "salt_hex": "000102030405060708090a0b0c", "ikm_hex": "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b", "info_hex": "f0f1f2f3f4f5f6f7f8f9", "length": 0}}, "expected": {"result_hex": ""}, "tags": ["length-zero", "edge"]}
{"id": "derive-017", "function": "derive", "input": {"args": {"algorithm": "sha384", "salt_hex": "", "ikm_hex": "24aeff2645e3e0f5494a9a102778c43a", "info_hex": "", "length": 0}}, "expected": {"result_hex": ""}, "tags": ["length-zero", "edge"]}
{"id": "derive-018", "function": "derive", "input": {"args": {"algorithm": "sha512", "salt_hex": "", "ikm_hex": "24aeff2645e3e0f5494a9a102778c43a", "info_hex": "", "length": 0}}, "expected": {"result_hex": ""}, "tags": ["length-zero", "edge"]}
{"id": "derive-019", "function": "derive", "input": {"args": {"algorithm": "sha256", "salt_hex": "ccdc54836e6046d5c851f917c37b0baeabd225e996b8f71d452cff5036345c3e", "ikm_hex": "91556a70e96dccb6a2fbee28250bdbd5", "info_hex": "2b90f142b2310b23", "length": 8160}}, "expected": {"assertion": "length(result) == 8160 and hex(sha256(result)) == 'a14afe5e70c25e95339abe6739f41cd27b0ab5f87480ee7e22edb5d419abaeab'"}, "tags": ["maximum-length", "edge", "wycheproof", "wycheproof-tc-24"]}
{"id": "derive-020", "function": "derive", "input": {"args": {"algorithm": "sha384", "salt_hex": "be99dc17920c2da449ed11592d1a134f5d97b631ce06a8b4070113ca5f18e6aa7b129a2c8d2120b38779c6ec8ac43e08", "ikm_hex": "d19f24915e80b5ba3b0a9f87b9acd21b", "info_hex": "aecafc9c85b224c2", "length": 12240}}, "expected": {"assertion": "length(result) == 12240 and hex(sha256(result)) == 'ab5f06d6c2745381d8ae492f43ed951d772d8146c5d9fc0a2616adc78f2bef7d'"}, "tags": ["maximum-length", "edge", "wycheproof", "wycheproof-tc-21"]}
{"id": "derive-021", "function": "derive", "input": {"args": {"algorithm": "sha512", "salt_hex": "7ea9bd3ed65fdbec456e4859209d0e87336b0a04bd342fccede6fa4cb12331789a9af13d3c96e955a74550883aa5a80e055ad7682a26c8b2343088d05f15f80d", "ikm_hex": "83c6fa101a922bf4e0ab38c1853b82f8", "info_hex": "c893d162bf0036e4", "length": 16320}}, "expected": {"assertion": "length(result) == 16320 and hex(sha256(result)) == 'f1d2571780c8a83db38dd812b9cab70a8c2e66bfaf1a2fb3632a154ce63a524a'"}, "tags": ["maximum-length", "edge", "wycheproof", "wycheproof-tc-21"]}
```

### Errors

```test-vectors
{"id": "hkdf-err-001", "function": "derive", "input": {"args": {"algorithm": "sha256", "salt_hex": "d5efc88adf3d5afc970284aab51690bdfedfa40be98e374efa3060ccf97fc650", "ikm_hex": "db89f54af757f8c7e57248a1718105b1", "info_hex": "134f085797b1ae2e", "length": 8161}}, "expected": {"error": "OutputTooLong"}, "tags": ["output-too-long", "wycheproof", "wycheproof-tc-25"]}
{"id": "hkdf-err-002", "function": "derive", "input": {"args": {"algorithm": "sha384", "salt_hex": "519ba8ec8bbccca06a7dbb54ea2dd6fc7074a7caa700101092272ec85113d0fff39a32d960d4ccc4990a3467ec935bd8", "ikm_hex": "2ea7f38feeb05b81dcae938af2f8e5ab", "info_hex": "7bd521a6fc9c4e24", "length": 12241}}, "expected": {"error": "OutputTooLong"}, "tags": ["output-too-long", "wycheproof", "wycheproof-tc-22"]}
{"id": "hkdf-err-003", "function": "derive", "input": {"args": {"algorithm": "sha512", "salt_hex": "c4aed79aa346a7914b43668ddc839a249652dcf11b8aa051bcd1796d1ef5e5f766380b4349333359a1177d0c082b1f0de90bdff1fb60646ede4f08a2b1f63786", "ikm_hex": "68563b0e902eb3cc65fbf7f115845b12", "info_hex": "b0aaceb3b0b8c885", "length": 16321}}, "expected": {"error": "OutputTooLong"}, "tags": ["output-too-long", "wycheproof", "wycheproof-tc-22"]}
{"id": "hkdf-err-004", "function": "expand", "input": {"args": {"algorithm": "sha256", "prk_hex": "19ef24a32c717b167f33a91d6f648bdf96596776afdb6377ac434c1c293ccb04", "info_hex": "", "length": 8161}}, "expected": {"error": "OutputTooLong"}, "tags": ["output-too-long"]}
{"id": "hkdf-err-005", "function": "expand", "input": {"args": {"algorithm": "sha384", "prk_hex": "10e40cf072a4c5626e43dd22c1cf727d4bb140975c9ad0cbc8e45b40068f8f0ba57cdb598af9dfa6963a96899af047e5", "info_hex": "", "length": 12241}}, "expected": {"error": "OutputTooLong"}, "tags": ["output-too-long"]}
{"id": "hkdf-err-006", "function": "expand", "input": {"args": {"algorithm": "sha512", "prk_hex": "fd200c4987ac491313bd4a2a13287121247239e11c9ef82802044b66ef357e5b194498d0682611382348572a7b1611de54764094286320578a863f36562b0df6", "info_hex": "", "length": 16321}}, "expected": {"error": "OutputTooLong"}, "tags": ["output-too-long"]}
{"id": "hkdf-err-007", "function": "extract", "input": {"args": {"algorithm": "sha1", "salt_hex": "", "ikm_hex": "0b0b0b0b"}}, "expected": {"error": "UnsupportedAlgorithm"}, "tags": ["unsupported-algorithm"]}
{"id": "hkdf-err-008", "function": "expand", "input": {"args": {"algorithm": "SHA256", "prk_hex": "077709362c2e32df0ddc3f0dc47bba6390b6c73bb50f9c3122ec844ad7c2b3e5", "info_hex": "", "length": 32}}, "expected": {"error": "UnsupportedAlgorithm"}, "tags": ["unsupported-algorithm"]}
{"id": "hkdf-err-009", "function": "derive", "input": {"args": {"algorithm": "md5", "salt_hex": "", "ikm_hex": "0b0b0b0b", "info_hex": "", "length": 16}}, "expected": {"error": "UnsupportedAlgorithm"}, "tags": ["unsupported-algorithm"]}
```

## Edge Cases

- **Empty salt.** Becomes `HashLen` zero bytes. Because HMAC zero-pads a short key to the block size, any all-zero salt no longer than the block size gives the same PRK as the empty salt.
- **Length 0.** Returns empty bytes and is not an error.
- **Length exactly `255 * HashLen`.** Valid: the counter reaches `0xff`.
- **Length `255 * HashLen + 1`.** `OutputTooLong`, for `expand` and for `derive`, for every algorithm.
- **Negative length.** `length` is a non-negative count, and an implementation SHOULD give it an unsigned or range-restricted type so a negative value cannot be passed. Where the type admits negatives, a negative `length` MUST fail loudly as an argument error; it MUST NOT be treated as 0 or wrap to a large value. It is a type error, not a vector.
- **Long salt.** A salt longer than the hash block size is hashed by HMAC first (Wycheproof `salt-longer-than-block`).
- **Empty IKM and empty info.** Both are legal.

## Implementation Notes

- **Argument order in extract.** RFC 5869 writes `HMAC-Hash(salt, IKM)`: salt is the key. Swapping them fails the `rfc5869-a1` vectors.
- **Counter placement.** The counter byte follows `info`: `T(i) = HMAC(PRK, T(i-1) || info || i)`, with `T(0)` empty. Starting the counter at 0 or placing it before `info` is a common bug.
- **Counter width.** The counter is one byte. The 255-block limit is why the output is capped; never widen it.
- **PRK reuse.** When deriving several keys from one secret, call `extract` once and `expand` once per `info` value.
- **Not for passwords.** HKDF is fast by design. Low-entropy passwords need a slow password hash, not HKDF.

## References

- [RFC 5869 -- HMAC-based Extract-and-Expand Key Derivation Function (HKDF)](https://www.rfc-editor.org/rfc/rfc5869.html) -- the governing specification and appendix A test cases.
- [Project Wycheproof HKDF test vectors](https://github.com/C2SP/wycheproof/tree/main/testvectors_v1) -- `hkdf_sha256_test.json`, `hkdf_sha384_test.json`, `hkdf_sha512_test.json`.
- [RFC 2104 -- HMAC](https://www.rfc-editor.org/rfc/rfc2104.html) -- the underlying MAC.
