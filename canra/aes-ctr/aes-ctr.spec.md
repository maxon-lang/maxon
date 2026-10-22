+++
name = "aes-ctr"
version = "1"
title = "AES Counter Mode (NIST SP 800-38A) with a 32-bit Block Counter, Constant-Time"
status = "draft"
keywords = ["aes", "ctr", "counter-mode", "stream-cipher", "sp800-38a", "constant-time", "cryptography"]
difficulty = "moderate"

[api]
functions = ["xor_key_stream"]
types = ["AesKeyStreamError"]

[references]
items = [
  { label = "NIST SP 800-38A Recommendation for Block Cipher Modes of Operation", url = "https://nvlpubs.nist.gov/nistpubs/Legacy/SP/nistspecialpublication800-38a.pdf" },
  { label = "NIST SP 800-38D (GCM), the inc32 counter function", url = "https://nvlpubs.nist.gov/nistpubs/Legacy/SP/nistspecialpublication800-38d.pdf" },
  { label = "FIPS 197 Advanced Encryption Standard", url = "https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.197-upd1.pdf" },
]

[test-vectors]
inline = true
external = []
+++

## Overview

This spec defines AES in the counter (CTR) mode of NIST SP 800-38A section 6.5, with the counter block layout of GCM: a 96-bit nonce followed by a 32-bit big-endian block counter that is incremented with `inc32`. It is one function:

- **xor_key_stream** -- XORs `data` with the AES-CTR keystream that starts at block `counter`. Encryption and decryption are the same operation.

The block cipher is the AES of FIPS 197 (the `aes` spec), used only in the forward direction, for 16-, 24- and 32-byte keys.

**Out of scope:** other counter block layouts (a 64-bit nonce with a 64-bit counter, or a 128-bit counter that carries into the nonce), seeking within a block (the keystream always starts at the beginning of block `counter`), streaming state across calls, the inverse cipher, and authentication (the `aes-gcm` spec). The function never wraps the counter.

## Types

### AesKeyStreamError

```pseudocode
type AesKeyStreamError =
  | InvalidKeyLength     // the key is not 16, 24 or 32 bytes
  | InvalidNonceLength   // the nonce is not exactly 12 bytes
  | CounterOverflow      // the keystream would need a block counter above 2^32 - 1
```

### The counter block

Counter block `j` of a call is the 16 bytes

```pseudocode
T_j = nonce || be32(counter + j)
```

where `be32(x)` is `x` as 4 big-endian bytes. The nonce occupies bytes 0 to 11 and never changes during a call; only the last 4 bytes count. This is `inc32` of SP 800-38D section 6.2 applied `j` times to `T_0`, with the restriction that the 32-bit field never wraps.

### The counter rule

`xor_key_stream` uses the blocks `counter, counter + 1, ..., counter + ceil(length(data) / 16) - 1`, and returns `CounterOverflow` exactly when

```pseudocode
counter > 4294967295  or  counter + ceil(length(data) / 16) - 1 > 4294967295
```

Equivalently, `length(data)` may be at most `16 * (2^32 - counter)` bytes. Empty data uses no block and is valid for every `counter` from 0 to `2^32 - 1`; a block whose counter is exactly `2^32 - 1` is valid. A statically typed implementation MAY give `counter` an unsigned 32-bit type; the vectors with `counter` 4294967296 are then compile-time rejections, like a negative counter (see Edge Cases).

## Functions

### xor_key_stream

```pseudocode
xor_key_stream(key: Bytes, nonce: Bytes, counter: Int, data: Bytes) -> Bytes | AesKeyStreamError
```

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `key` | Bytes | yes | 16, 24 or 32 bytes (AES-128, AES-192, AES-256) |
| `nonce` | Bytes | yes | Exactly 12 bytes; MUST be unique per key for a given counter range |
| `counter` | Int | yes | Initial block counter, `0 <= counter <= 2^32 - 1` |
| `data` | Bytes | yes | Plaintext or ciphertext, any length within the counter rule |

**Returns:** `length(data)` bytes, or `InvalidKeyLength`, or `InvalidNonceLength`, or `CounterOverflow`.

**Algorithm:**

1. If `length(key)` is not 16, 24 or 32, return `InvalidKeyLength`.
2. If `length(nonce) != 12`, return `InvalidNonceLength`.
3. If the counter rule is violated, return `CounterOverflow`, before producing any output.
4. For `j` in `0 .. ceil(length(data) / 16) - 1`: `ks = AES_encrypt(key, nonce || be32(counter + j))`; output `data[16j .. 16j + 15] XOR ks`, truncating `ks` for the final partial block.
5. Return the output.

`AES_encrypt` is the FIPS 197 Cipher (the `encrypt_block` of the `aes` spec). The key schedule MAY be computed once per call.

## Behavioral Contracts

### xor_key_stream()

**MUST:**

1. MUST return the SP 800-38A section 6.5 output, with the counter blocks `T_j = nonce || be32(counter + j)`.
2. MUST check the key length, then the nonce length, then the counter rule, and return an error before writing any output.
3. MUST return `CounterOverflow` under the counter rule, and MUST NOT wrap the 32-bit counter to 0, carry it into the nonce, or return a truncated result.
4. MUST be its own inverse: `xor_key_stream(k, n, c, xor_key_stream(k, n, c, d)) == d`.
5. MUST return output of exactly `length(data)` bytes, discarding the unused keystream of the final block.
6. **Constant time.** The AES computation MUST meet the constant-time contract of the `aes` spec: for keys of the same length, the sequence of executed instructions and of memory addresses MUST NOT depend on the key, the nonce, the data or the keystream.
   In particular MUST NOT perform any table lookup indexed by secret data (S-box, T-tables, `xtime` or log tables, in the key schedule as well as in the rounds), and MUST NOT branch on secret data. The length of `data` and the value of `counter` are not secret.
7. MUST NOT modify its inputs (an in-place variant is out of scope).

**SHOULD:**

1. SHOULD overwrite the expanded key, the counter blocks and the keystream buffers before returning.

**MAY:**

1. MAY use the processor's AES instructions (for example AES-NI or the ARMv8 Cryptography Extensions), and MAY encrypt several counter blocks at once; the result MUST be the same.

The caller MUST NOT use the same key, nonce and block counter for two different messages: the XOR of the two ciphertexts is the XOR of the plaintexts.

## Test Vectors

`key_hex`, `nonce_hex` and `data_hex` are lowercase hex; `counter` is an integer. Expected output is `result_hex`. The tags `aes-128`, `aes-192` and `aes-256` give the key size.

### Sources

- `sp800-38a-f-5-1`, `sp800-38a-f-5-3` and `sp800-38a-f-5-5` are the SP 800-38A appendix F.5.1, F.5.3 and F.5.5 CTR encryption examples for AES-128, AES-192 and AES-256 (four blocks each).
  Their initial counter block `f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff` is the nonce `f0f1f2f3f4f5f6f7f8f9fafb` with `counter` 4244504319 (`0xfcfdfeff`); the four input blocks of each example end in `fcfdfeff`, `fcfdff00`, `fcfdff01` and `fcfdff02`, so the second block carries from the last byte of the counter into the one before.
- `sp800-38a-f-5-2`, `sp800-38a-f-5-4` and `sp800-38a-f-5-6` are the matching decryption examples F.5.2, F.5.4 and F.5.6, run through the same function.
- `prefix` vectors take the first 17, 16 and 1 bytes of the F.5.1 plaintext; their expected outputs are the same prefixes of the published ciphertext, because the keystream does not depend on the data length.
- `computed` vectors have no published source. They carry the counter across two and three bytes (from `0x0000ffff` and `0x00ffffff`), start at counter 0, and use the last valid counter `2^32 - 1` (one of them over 16 zero bytes, so its result is the raw keystream block `AES_encrypt(key, nonce || ffffffff)`).
  Their outputs were computed with an independent implementation, both as AES-CTR over the 16-byte counter block and as AES encryption of each explicit counter block `nonce || be32(counter + j)`.
- Every expected value of a `sp800-38a` vector was recomputed in the same two ways and matched the source, including each published input block and output block.
- The error vectors follow from the counter rule and the length checks.

### xor_key_stream()

```test-vectors
{"id": "xor-key-stream-001", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4244504319, "data_hex": "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710"}}, "expected": {"result_hex": "874d6191b620e3261bef6864990db6ce9806f66b7970fdff8617187bb9fffdff5ae4df3edbd5d35e5b4f09020db03eab1e031dda2fbe03d1792170a0f3009cee"}, "tags": ["sp800-38a", "sp800-38a-f-5-1", "aes-128", "encrypt", "counter-carry"]}
{"id": "xor-key-stream-002", "function": "xor_key_stream", "input": {"args": {"key_hex": "8e73b0f7da0e6452c810f32b809079e562f8ead2522c6b7b", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4244504319, "data_hex": "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710"}}, "expected": {"result_hex": "1abc932417521ca24f2b0459fe7e6e0b090339ec0aa6faefd5ccc2c6f4ce8e941e36b26bd1ebc670d1bd1d665620abf74f78a7f6d29809585a97daec58c6b050"}, "tags": ["sp800-38a", "sp800-38a-f-5-3", "aes-192", "encrypt", "counter-carry"]}
{"id": "xor-key-stream-003", "function": "xor_key_stream", "input": {"args": {"key_hex": "603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4244504319, "data_hex": "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710"}}, "expected": {"result_hex": "601ec313775789a5b7a7f504bbf3d228f443e3ca4d62b59aca84e990cacaf5c52b0930daa23de94ce87017ba2d84988ddfc9c58db67aada613c2dd08457941a6"}, "tags": ["sp800-38a", "sp800-38a-f-5-5", "aes-256", "encrypt", "counter-carry"]}
{"id": "xor-key-stream-004", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4244504319, "data_hex": "874d6191b620e3261bef6864990db6ce9806f66b7970fdff8617187bb9fffdff5ae4df3edbd5d35e5b4f09020db03eab1e031dda2fbe03d1792170a0f3009cee"}}, "expected": {"result_hex": "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710"}, "tags": ["sp800-38a", "sp800-38a-f-5-2", "aes-128", "decrypt", "counter-carry"]}
{"id": "xor-key-stream-005", "function": "xor_key_stream", "input": {"args": {"key_hex": "8e73b0f7da0e6452c810f32b809079e562f8ead2522c6b7b", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4244504319, "data_hex": "1abc932417521ca24f2b0459fe7e6e0b090339ec0aa6faefd5ccc2c6f4ce8e941e36b26bd1ebc670d1bd1d665620abf74f78a7f6d29809585a97daec58c6b050"}}, "expected": {"result_hex": "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710"}, "tags": ["sp800-38a", "sp800-38a-f-5-4", "aes-192", "decrypt", "counter-carry"]}
{"id": "xor-key-stream-006", "function": "xor_key_stream", "input": {"args": {"key_hex": "603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4244504319, "data_hex": "601ec313775789a5b7a7f504bbf3d228f443e3ca4d62b59aca84e990cacaf5c52b0930daa23de94ce87017ba2d84988ddfc9c58db67aada613c2dd08457941a6"}}, "expected": {"result_hex": "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710"}, "tags": ["sp800-38a", "sp800-38a-f-5-6", "aes-256", "decrypt", "counter-carry"]}
{"id": "xor-key-stream-007", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4244504319, "data_hex": "6bc1bee22e409f96e93d7e117393172aae"}}, "expected": {"result_hex": "874d6191b620e3261bef6864990db6ce98"}, "tags": ["sp800-38a", "sp800-38a-f-5-1", "aes-128", "prefix", "partial-block", "edge"]}
{"id": "xor-key-stream-008", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4244504319, "data_hex": "6bc1bee22e409f96e93d7e117393172a"}}, "expected": {"result_hex": "874d6191b620e3261bef6864990db6ce"}, "tags": ["sp800-38a", "sp800-38a-f-5-1", "aes-128", "prefix", "one-block", "edge"]}
{"id": "xor-key-stream-009", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4244504319, "data_hex": "6b"}}, "expected": {"result_hex": "87"}, "tags": ["sp800-38a", "sp800-38a-f-5-1", "aes-128", "prefix", "one-byte", "edge"]}
{"id": "xor-key-stream-010", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4244504319, "data_hex": ""}}, "expected": {"result_hex": ""}, "tags": ["sp800-38a", "sp800-38a-f-5-1", "aes-128", "empty", "edge"]}
{"id": "xor-key-stream-011", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 16777215, "data_hex": "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710"}}, "expected": {"result_hex": "8d4e90eec6a695a5fdf93fdef74efe9df9a02f8960af3aab2deb37c2f6b49031795d790820f900cad5c62a1e9bdb80b9d99899bde561000aaaa4fa1339f4e769"}, "tags": ["computed", "aes-128", "counter-carry", "edge"]}
{"id": "xor-key-stream-012", "function": "xor_key_stream", "input": {"args": {"key_hex": "603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 65535, "data_hex": "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e51"}}, "expected": {"result_hex": "c05611164b5bb6bb6e3321c3317927b39b4906065e04b614e67d9c3334096e61"}, "tags": ["computed", "aes-256", "counter-carry", "edge"]}
{"id": "xor-key-stream-013", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4294967295, "data_hex": "6bc1bee22e409f96e93d7e117393172a"}}, "expected": {"result_hex": "5720de614e98a465919711117cf295e8"}, "tags": ["computed", "aes-128", "counter-max", "last-block", "edge"]}
{"id": "xor-key-stream-014", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4294967295, "data_hex": "6b"}}, "expected": {"result_hex": "57"}, "tags": ["computed", "aes-128", "counter-max", "last-block", "one-byte", "edge"]}
{"id": "xor-key-stream-015", "function": "xor_key_stream", "input": {"args": {"key_hex": "8e73b0f7da0e6452c810f32b809079e562f8ead2522c6b7b", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4294967294, "data_hex": "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e51"}}, "expected": {"result_hex": "bf957dfd0f973ac50d5a20047b9ee11f92df49f01c73de9a6be9bc582273c0fd"}, "tags": ["computed", "aes-192", "counter-max", "last-block", "edge"]}
{"id": "xor-key-stream-016", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 0, "data_hex": "6bc1bee22e409f96e93d7e117393172a"}}, "expected": {"result_hex": "22e52fb177d865b2f7c6b512692d114d"}, "tags": ["computed", "aes-128", "counter-zero", "edge"]}
{"id": "xor-key-stream-017", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4294967295, "data_hex": ""}}, "expected": {"result_hex": ""}, "tags": ["counter-max", "empty", "edge"]}
{"id": "xor-key-stream-018", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4294967295, "data_hex": "00000000000000000000000000000000"}}, "expected": {"result_hex": "3ce1608360d83bf378aa6f000f6182c2"}, "tags": ["computed", "aes-128", "counter-max", "last-block", "keystream", "edge"]}
```

### Errors

```test-vectors
{"id": "aes-ctr-err-001", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4294967295, "data_hex": "0000000000000000000000000000000000"}}, "expected": {"error": "CounterOverflow"}, "tags": ["counter-overflow", "edge"]}
{"id": "aes-ctr-err-002", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4294967295, "data_hex": "0000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"error": "CounterOverflow"}, "tags": ["counter-overflow", "edge"]}
{"id": "aes-ctr-err-003", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4294967294, "data_hex": "000000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"error": "CounterOverflow"}, "tags": ["counter-overflow", "edge"]}
{"id": "aes-ctr-err-004", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4294967296, "data_hex": ""}}, "expected": {"error": "CounterOverflow"}, "tags": ["counter-overflow", "counter-out-of-range", "edge"]}
{"id": "aes-ctr-err-005", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 4294967296, "data_hex": "00"}}, "expected": {"error": "CounterOverflow"}, "tags": ["counter-overflow", "counter-out-of-range", "edge"]}
{"id": "aes-ctr-err-006", "function": "xor_key_stream", "input": {"args": {"key_hex": "", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 0, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "aes-ctr-err-007", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 0, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "aes-ctr-err-008", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c00", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 0, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "aes-ctr-err-009", "function": "xor_key_stream", "input": {"args": {"key_hex": "603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914df", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 0, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "aes-ctr-err-010", "function": "xor_key_stream", "input": {"args": {"key_hex": "603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff400", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafb", "counter": 0, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "aes-ctr-err-011", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "", "counter": 0, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidNonceLength"}, "tags": ["invalid-nonce-length", "edge"]}
{"id": "aes-ctr-err-012", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7", "counter": 0, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidNonceLength"}, "tags": ["invalid-nonce-length", "edge"]}
{"id": "aes-ctr-err-013", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fa", "counter": 0, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidNonceLength"}, "tags": ["invalid-nonce-length", "edge"]}
{"id": "aes-ctr-err-014", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafbfc", "counter": 0, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidNonceLength"}, "tags": ["invalid-nonce-length", "edge"]}
{"id": "aes-ctr-err-015", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff", "counter": 0, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidNonceLength"}, "tags": ["invalid-nonce-length", "edge"]}
{"id": "aes-ctr-err-016", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f", "nonce_hex": "f0f1f2f3f4f5f6f7f8f9fa", "counter": 4294967295, "data_hex": "0000000000000000000000000000000000"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "invalid-nonce-length", "counter-overflow", "check-order"]}
{"id": "aes-ctr-err-017", "function": "xor_key_stream", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "nonce_hex": "f0f1f2f3f4f5f6f7", "counter": 4294967295, "data_hex": "0000000000000000000000000000000000"}}, "expected": {"error": "InvalidNonceLength"}, "tags": ["invalid-nonce-length", "counter-overflow", "check-order"]}
```

## Edge Cases

- **Empty data.** Returns empty bytes for any valid key, nonce and counter, including `2^32 - 1`.
- **Partial blocks.** 1 and 17 bytes use the first 1 and 17 bytes of the keystream; the 17th byte comes from block `counter + 1`.
- **Counter carry.** The 32-bit counter is a big-endian integer: `0x0000ffff + 1` is `0x00010000` and `0x00ffffff + 1` is `0x01000000`. Incrementing only the last byte, or the counter as little-endian, fails the F.5 vectors at their second block.
- **Last counter.** `counter = 2^32 - 1` with 1 to 16 bytes uses exactly that block and is valid. With 17 bytes it would need block `2^32`: `CounterOverflow`. `counter = 2^32 - 2` allows up to 32 bytes.
- **No carry into the nonce.** A 128-bit counter (as SP 800-38A section B.1 allows in general) would turn block `2^32` into `nonce + 1 || 00000000`; this spec refuses it with `CounterOverflow` instead.
- **Counter out of range.** `counter = 2^32` is `CounterOverflow` even for empty data.
- **Negative counter.** `counter` is non-negative; an implementation SHOULD give it an unsigned or range-restricted type. Where the type admits negatives, a negative counter MUST fail loudly as an argument error and MUST NOT be reduced modulo 2^32. It is a type error, not a vector.
- **Other nonce lengths.** 0, 8, 11, 13 and 16 bytes are `InvalidNonceLength`; this spec never pads, hashes or truncates a nonce (GCM's hashing of a nonce that is not 12 bytes is out of scope).
- **Key and nonce both wrong.** `InvalidKeyLength`. A wrong nonce with an overflowing counter is `InvalidNonceLength`.

## Implementation Notes

- **Byte order.** The counter is big-endian in the last 4 bytes of the block. ChaCha20 stores its counter little-endian in the first word after the key; carrying that habit over fails every vector here.
- **Only 32 bits count.** Library CTR modes often increment the whole 128-bit block; they agree with this spec until the low 32 bits wrap, then they carry into the nonce. The counter rule makes that case an error, so a library CTR mode can only be used after the rule is checked.
- **Use only the forward cipher.** Decryption is the same XOR with the same keystream; the AES inverse cipher is never needed.
- **Constant time.** CTR mode adds only XOR and counter arithmetic on public values, so the constant-time burden is entirely in AES; see the `aes` spec for bitslicing and why table-driven AES is forbidden. A bitsliced AES that processes several blocks at once suits CTR well, since the counter blocks are independent.
- **Overflow arithmetic.** Compute `counter + j` in a type wider than 32 bits, or check the counter rule first; a 32-bit wrapping addition silently reuses keystream.

## References

- [NIST SP 800-38A -- Recommendation for Block Cipher Modes of Operation](https://nvlpubs.nist.gov/nistpubs/Legacy/SP/nistspecialpublication800-38a.pdf) -- the governing standard: the CTR mode (section 6.5), counter block generation (appendix B) and the CTR example vectors (appendix F.5).
- [NIST SP 800-38D -- Galois/Counter Mode](https://nvlpubs.nist.gov/nistpubs/Legacy/SP/nistspecialpublication800-38d.pdf) -- the `inc32` function and the 96-bit nonce counter layout (section 6.2 and 7.1).
- [FIPS 197 -- Advanced Encryption Standard (AES)](https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.197-upd1.pdf) -- the block cipher.
