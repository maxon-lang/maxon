+++
name = "base64"
version = "1"
title = "Base64 Encoding (RFC 4648 Standard Alphabet, Strict)"
status = "draft"
keywords = ["base64", "encoding", "binary-to-text", "rfc4648"]
difficulty = "easy"

[api]
functions = ["encode", "decode"]
types = ["Base64Error"]

[references]
items = [
  { label = "RFC 4648", url = "https://www.rfc-editor.org/rfc/rfc4648.html" },
]

[test-vectors]
inline = true
external = []
+++

## Overview

This spec defines the base64 encoding of RFC 4648 section 4: the standard alphabet `A-Z a-z 0-9 + /` with `=` padding.

- **encode** -- converts bytes to padded base64 text.
- **decode** -- converts base64 text back to bytes, STRICTLY: it accepts exactly the strings that `encode` produces and rejects everything else.

**Out of scope:** the URL- and filename-safe alphabet (RFC 4648 section 5); unpadded base64; base32 and base16; MIME line wrapping (RFC 2045) and any whitespace or line-break tolerance; streaming. A format that wraps or embeds base64 (PEM, MIME) removes its own line breaks before calling `decode`.

## Types

### Alphabet

| Value | Symbol | Value | Symbol | Value | Symbol | Value | Symbol |
|---|---|---|---|---|---|---|---|
| 0-25 | `A`-`Z` | 26-51 | `a`-`z` | 52-61 | `0`-`9` | 62, 63 | `+`, `/` |

The pad character is `=`. Every other character, including whitespace, `-`, `_` and any non-ASCII character, is outside the alphabet.

### Base64Error

```pseudocode
type Base64Error =
  | InvalidSymbol    // a character that is neither in the alphabet nor "="
  | InvalidLength    // the text length is not a multiple of 4
  | InvalidPadding   // "=" in a wrong place, more than two "=", or non-zero pad bits
```

## Functions

### encode

```pseudocode
encode(data: Bytes) -> String
```

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `data` | Bytes | yes | Bytes to encode, any length including zero |

**Returns:** Base64 text of length `4 * ceil(length(data) / 3)`. There is no error case.

**Algorithm:**

1. For each complete group of 3 bytes `b0 b1 b2`, form the 24-bit value `(b0 << 16) | (b1 << 8) | b2` and output the four symbols for bits 23-18, 17-12, 11-6 and 5-0.
2. If 1 byte `b0` remains: output the symbols for `b0 >> 2` and `(b0 & 0x03) << 4`, then `==`.
3. If 2 bytes `b0 b1` remain: output the symbols for `b0 >> 2`, `((b0 & 0x03) << 4) | (b1 >> 4)` and `(b1 & 0x0f) << 2`, then `=`.

### decode

```pseudocode
decode(text: String) -> Bytes | Base64Error
```

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `text` | String | yes | Padded base64 text with no whitespace |

**Returns:** The decoded bytes, or a `Base64Error`.

**Algorithm:** The checks run in this order, and the first failing check decides the error.

1. **Symbols.** If any character of `text` is neither in the alphabet nor `=`, return `InvalidSymbol`.
2. **Length.** If `length(text) % 4 != 0`, return `InvalidLength`. (Missing padding always surfaces here.)
3. **Padding placement.** Let `p` be the number of `=` characters at the end of `text`. If `p > 2`, or if any `=` occurs before those final `p` characters, return `InvalidPadding`.
4. **Decode.** Decode each 4-symbol group to 3 bytes. The final group, when padded, yields 2 bytes (`p == 1`) or 1 byte (`p == 2`).
5. **Pad bits.** If `p == 1`, the low 2 bits of the last symbol's value MUST be zero; if `p == 2`, the low 4 bits of the last symbol's value (the one before `==`) MUST be zero. Otherwise return `InvalidPadding`.
6. Return the bytes.

## Behavioral Contracts

### encode()

**MUST:**

1. MUST use the standard alphabet and always pad the output to a multiple of 4 characters.
2. MUST set the unused pad bits of the last symbol to zero (the canonical encoding of RFC 4648 section 3.5).
3. MUST NOT insert line breaks or any other characters.

### decode()

**MUST:**

1. MUST accept exactly the strings `encode` can produce: `decode(t)` succeeds if and only if `t == encode(b)` for some `b`, and then returns `b`.
2. MUST reject whitespace (space, tab, CR, LF) and every other character outside the alphabet with `InvalidSymbol`; it MUST NOT skip them.
3. MUST reject a length that is not a multiple of 4 with `InvalidLength`, including unpadded input.
4. MUST reject `=` anywhere except as one or two final characters with `InvalidPadding`.
5. MUST reject non-zero pad bits (a non-canonical encoding) with `InvalidPadding`. RFC 4648 section 3.5 names these the pad bits; a decoder that ignores them would map several strings to the same bytes.
6. MUST apply the checks in the order of the algorithm, so every input has exactly one error.

### Both

**MUST:**

1. `decode(encode(b)) == b` for every byte string `b`.

## Test Vectors

Bytes are lowercase hex in `data_hex`; decoded bytes are `result_hex`. Strings are JSON strings, so `\n` is a line feed and a JSON `\u` escape followed by four hex digits is one Unicode character (`decode-err-011` contains U+00E9).

### Sources

- `rfc4648` vectors are RFC 4648 section 10 (`""`, `"f"`, `"fo"`, `"foo"`, `"foob"`, `"fooba"`, `"foobar"`), in both directions.
- `all-byte-values` encodes and decodes the 256 bytes `00 01 .. ff` in order: every symbol of the alphabet appears in the text.
- `alphabet-edge` covers the first and last symbols (`A`, `/`, `+`) and the two pad lengths.

### encode()

```test-vectors
{"id": "encode-001", "function": "encode", "input": {"args": {"data_hex": ""}}, "expected": {"result": ""}, "tags": ["rfc4648", "empty", "edge"]}
{"id": "encode-002", "function": "encode", "input": {"args": {"data_hex": "66"}}, "expected": {"result": "Zg=="}, "tags": ["rfc4648"]}
{"id": "encode-003", "function": "encode", "input": {"args": {"data_hex": "666f"}}, "expected": {"result": "Zm8="}, "tags": ["rfc4648"]}
{"id": "encode-004", "function": "encode", "input": {"args": {"data_hex": "666f6f"}}, "expected": {"result": "Zm9v"}, "tags": ["rfc4648"]}
{"id": "encode-005", "function": "encode", "input": {"args": {"data_hex": "666f6f62"}}, "expected": {"result": "Zm9vYg=="}, "tags": ["rfc4648"]}
{"id": "encode-006", "function": "encode", "input": {"args": {"data_hex": "666f6f6261"}}, "expected": {"result": "Zm9vYmE="}, "tags": ["rfc4648"]}
{"id": "encode-007", "function": "encode", "input": {"args": {"data_hex": "666f6f626172"}}, "expected": {"result": "Zm9vYmFy"}, "tags": ["rfc4648"]}
{"id": "encode-008", "function": "encode", "input": {"args": {"data_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff"}}, "expected": {"result": "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+P0BBQkNERUZHSElKS0xNTk9QUVJTVFVWV1hZWltcXV5fYGFiY2RlZmdoaWprbG1ub3BxcnN0dXZ3eHl6e3x9fn+AgYKDhIWGh4iJiouMjY6PkJGSk5SVlpeYmZqbnJ2en6ChoqOkpaanqKmqq6ytrq+wsbKztLW2t7i5uru8vb6/wMHCw8TFxsfIycrLzM3Oz9DR0tPU1dbX2Nna29zd3t/g4eLj5OXm5+jp6uvs7e7v8PHy8/T19vf4+fr7/P3+/w=="}, "tags": ["all-byte-values", "round-trip"]}
{"id": "encode-009", "function": "encode", "input": {"args": {"data_hex": "000000"}}, "expected": {"result": "AAAA"}, "tags": ["alphabet-edge"]}
{"id": "encode-010", "function": "encode", "input": {"args": {"data_hex": "ffffff"}}, "expected": {"result": "////"}, "tags": ["alphabet-edge"]}
{"id": "encode-011", "function": "encode", "input": {"args": {"data_hex": "fbff"}}, "expected": {"result": "+/8="}, "tags": ["alphabet-edge"]}
{"id": "encode-012", "function": "encode", "input": {"args": {"data_hex": "00"}}, "expected": {"result": "AA=="}, "tags": ["alphabet-edge"]}
```

### decode()

```test-vectors
{"id": "decode-001", "function": "decode", "input": {"args": {"text": ""}}, "expected": {"result_hex": ""}, "tags": ["rfc4648", "empty", "edge"]}
{"id": "decode-002", "function": "decode", "input": {"args": {"text": "Zg=="}}, "expected": {"result_hex": "66"}, "tags": ["rfc4648"]}
{"id": "decode-003", "function": "decode", "input": {"args": {"text": "Zm8="}}, "expected": {"result_hex": "666f"}, "tags": ["rfc4648"]}
{"id": "decode-004", "function": "decode", "input": {"args": {"text": "Zm9v"}}, "expected": {"result_hex": "666f6f"}, "tags": ["rfc4648"]}
{"id": "decode-005", "function": "decode", "input": {"args": {"text": "Zm9vYg=="}}, "expected": {"result_hex": "666f6f62"}, "tags": ["rfc4648"]}
{"id": "decode-006", "function": "decode", "input": {"args": {"text": "Zm9vYmE="}}, "expected": {"result_hex": "666f6f6261"}, "tags": ["rfc4648"]}
{"id": "decode-007", "function": "decode", "input": {"args": {"text": "Zm9vYmFy"}}, "expected": {"result_hex": "666f6f626172"}, "tags": ["rfc4648"]}
{"id": "decode-008", "function": "decode", "input": {"args": {"text": "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+P0BBQkNERUZHSElKS0xNTk9QUVJTVFVWV1hZWltcXV5fYGFiY2RlZmdoaWprbG1ub3BxcnN0dXZ3eHl6e3x9fn+AgYKDhIWGh4iJiouMjY6PkJGSk5SVlpeYmZqbnJ2en6ChoqOkpaanqKmqq6ytrq+wsbKztLW2t7i5uru8vb6/wMHCw8TFxsfIycrLzM3Oz9DR0tPU1dbX2Nna29zd3t/g4eLj5OXm5+jp6uvs7e7v8PHy8/T19vf4+fr7/P3+/w=="}}, "expected": {"result_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff"}, "tags": ["all-byte-values", "round-trip"]}
{"id": "decode-009", "function": "decode", "input": {"args": {"text": "AAAA"}}, "expected": {"result_hex": "000000"}, "tags": ["alphabet-edge"]}
{"id": "decode-010", "function": "decode", "input": {"args": {"text": "////"}}, "expected": {"result_hex": "ffffff"}, "tags": ["alphabet-edge"]}
{"id": "decode-011", "function": "decode", "input": {"args": {"text": "+/8="}}, "expected": {"result_hex": "fbff"}, "tags": ["alphabet-edge"]}
{"id": "decode-012", "function": "decode", "input": {"args": {"text": "AA=="}}, "expected": {"result_hex": "00"}, "tags": ["alphabet-edge"]}
```

### decode() errors

```test-vectors
{"id": "decode-err-001", "function": "decode", "input": {"args": {"text": "Zg"}}, "expected": {"error": "InvalidLength"}, "tags": ["missing-padding", "length"]}
{"id": "decode-err-002", "function": "decode", "input": {"args": {"text": "Zm9vY"}}, "expected": {"error": "InvalidLength"}, "tags": ["length"]}
{"id": "decode-err-003", "function": "decode", "input": {"args": {"text": "Zg="}}, "expected": {"error": "InvalidLength"}, "tags": ["missing-padding", "length"]}
{"id": "decode-err-004", "function": "decode", "input": {"args": {"text": "Zm9vYmFy="}}, "expected": {"error": "InvalidLength"}, "tags": ["length"]}
{"id": "decode-err-005", "function": "decode", "input": {"args": {"text": "Zm9v Zg=="}}, "expected": {"error": "InvalidSymbol"}, "tags": ["whitespace"]}
{"id": "decode-err-006", "function": "decode", "input": {"args": {"text": "Zm9v\nZg=="}}, "expected": {"error": "InvalidSymbol"}, "tags": ["whitespace", "line-break"]}
{"id": "decode-err-007", "function": "decode", "input": {"args": {"text": "Zm9v\r\n"}}, "expected": {"error": "InvalidSymbol"}, "tags": ["whitespace", "line-break"]}
{"id": "decode-err-008", "function": "decode", "input": {"args": {"text": "Zm9-"}}, "expected": {"error": "InvalidSymbol"}, "tags": ["url-safe-alphabet"]}
{"id": "decode-err-009", "function": "decode", "input": {"args": {"text": "Zm9_"}}, "expected": {"error": "InvalidSymbol"}, "tags": ["url-safe-alphabet"]}
{"id": "decode-err-010", "function": "decode", "input": {"args": {"text": "Zm9v!g=="}}, "expected": {"error": "InvalidSymbol"}, "tags": ["symbol"]}
{"id": "decode-err-011", "function": "decode", "input": {"args": {"text": "Zm9v\u00e9g=="}}, "expected": {"error": "InvalidSymbol"}, "tags": ["non-ascii"]}
{"id": "decode-err-012", "function": "decode", "input": {"args": {"text": "Zg=a"}}, "expected": {"error": "InvalidPadding"}, "tags": ["misplaced-padding"]}
{"id": "decode-err-013", "function": "decode", "input": {"args": {"text": "Zg==Zg=="}}, "expected": {"error": "InvalidPadding"}, "tags": ["padding-in-middle"]}
{"id": "decode-err-014", "function": "decode", "input": {"args": {"text": "=Zm9"}}, "expected": {"error": "InvalidPadding"}, "tags": ["misplaced-padding"]}
{"id": "decode-err-015", "function": "decode", "input": {"args": {"text": "Z==="}}, "expected": {"error": "InvalidPadding"}, "tags": ["too-much-padding"]}
{"id": "decode-err-016", "function": "decode", "input": {"args": {"text": "===="}}, "expected": {"error": "InvalidPadding"}, "tags": ["too-much-padding"]}
{"id": "decode-err-017", "function": "decode", "input": {"args": {"text": "Zh=="}}, "expected": {"error": "InvalidPadding"}, "tags": ["non-canonical"]}
{"id": "decode-err-018", "function": "decode", "input": {"args": {"text": "Zm9="}}, "expected": {"error": "InvalidPadding"}, "tags": ["non-canonical"]}
{"id": "decode-err-019", "function": "decode", "input": {"args": {"text": "Zg==="}}, "expected": {"error": "InvalidLength"}, "tags": ["too-much-padding", "length", "check-order"]}
```

## Edge Cases

- **Empty input.** `encode("")` is `""` and `decode("")` is empty bytes.
- **Missing padding.** `"Zg"` and `"Zg="` are `InvalidLength`, not `InvalidPadding`: a correctly padded string always has a length divisible by 4.
- **Whitespace.** `"Zm9v\nZg=="` is `InvalidSymbol` even though it would decode after removing the line break. Strip line breaks in the caller.
- **Wrong alphabet.** `-` and `_` (the URL-safe symbols) are `InvalidSymbol`.
- **Pad in the middle.** `"Zg==Zg=="` (two encodings concatenated) is `InvalidPadding`.
- **Too much padding.** `"Z==="` and `"===="` are `InvalidPadding`. `"Zg==="` is `InvalidLength`, because its length of 5 is checked before its padding.
- **Non-canonical encodings.** `"Zh=="` decodes, ignoring pad bits, to the same byte as `"Zg=="`, and `"Zm9="` to the same bytes as `"Zm8="`; both are `InvalidPadding`.
- **Non-ASCII input.** A character beyond ASCII is `InvalidSymbol`. Implementations that receive bytes rather than characters see one or more bytes above `0x7f`, each also outside the alphabet.

## Implementation Notes

- **Lookup table.** Decode with a 256-entry table mapping each byte to its value or to an "invalid" marker that is distinct from every value 0-63. Do not use 0 as the marker.
- **Order of checks.** The `InvalidSymbol` scan covers the whole text before the length check, so `"Zm9v\r\n"` is `InvalidSymbol`, not `InvalidLength`.
- **Pad bits.** For `xy==` check `value(y) & 0x0f == 0`; for `xyz=` check `value(z) & 0x03 == 0`.
- **Output size.** Pre-size the output: `3 * length(text) / 4 - p` bytes for decode, `4 * ceil(n / 3)` characters for encode.
- **Secrets.** When base64 carries key material, a table-free constant-time decoder avoids cache-timing leaks; this is optional and does not change any result.

## References

- [RFC 4648 -- The Base16, Base32, and Base64 Data Encodings](https://www.rfc-editor.org/rfc/rfc4648.html) -- section 4 (base64), section 3.5 (canonical encoding and pad bits), section 10 (test vectors).
