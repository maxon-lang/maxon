+++
name = "aes"
version = "1"
title = "AES Block Cipher (FIPS 197), Constant-Time"
status = "draft"
keywords = ["aes", "block-cipher", "rijndael", "fips197", "constant-time", "cryptography"]
difficulty = "hard"

[api]
functions = ["encrypt_block", "decrypt_block"]
types = ["AesError"]

[references]
items = [
  { label = "FIPS 197 Advanced Encryption Standard", url = "https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.197.pdf" },
  { label = "FIPS 197-upd1 (2023 editorial update)", url = "https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.197-upd1.pdf" },
  { label = "NIST AESAVS and KAT_AES.zip", url = "https://csrc.nist.gov/projects/cryptographic-algorithm-validation-program/block-ciphers" },
  { label = "Bernstein, Cache-timing attacks on AES", url = "https://cr.yp.to/antiforgery/cachetiming-20050414.pdf" },
  { label = "Kasper and Schwabe, Faster and Timing-Attack Resistant AES-GCM", url = "https://eprint.iacr.org/2009/129" },
  { label = "Boyar and Peralta, A depth-16 circuit for the AES S-box", url = "https://eprint.iacr.org/2011/332" },
]

[test-vectors]
inline = true
external = []
+++

## Overview

This spec defines the AES block cipher of FIPS 197 for its three key sizes (AES-128, AES-192 and AES-256), as two functions on a single 16-byte block:

- **encrypt_block** -- the Cipher of FIPS 197 section 5.1.
- **decrypt_block** -- the Inverse Cipher of FIPS 197 section 5.3, so that `decrypt_block(k, encrypt_block(k, b)) == b`.

The implementation MUST run in constant time: its sequence of instructions and memory addresses MUST NOT depend on the key or on the block (see Behavioral Contracts). This rules out the classic table-driven AES.

**Out of scope:** every mode of operation (ECB over several blocks, CBC, CTR, GCM, XTS, key wrap; GCM is the `aes-gcm` spec), padding, Rijndael block sizes other than 128 bits, key sizes other than 128, 192 and 256 bits, and an interface that exposes the expanded key schedule. An implementation MAY cache the expanded key internally; the result MUST be the same.

## Types

### Key sizes

| Key length | Variant | `Nk` (32-bit key words) | `Nr` (rounds) | Round keys |
|---|---|---|---|---|
| 16 bytes | AES-128 | 4 | 10 | 11 |
| 24 bytes | AES-192 | 6 | 12 | 13 |
| 32 bytes | AES-256 | 8 | 14 | 15 |

### AesError

```pseudocode
type AesError =
  | InvalidKeyLength     // the key is not 16, 24 or 32 bytes
  | InvalidBlockLength   // the block is not exactly 16 bytes
```

## Functions

### encrypt_block

```pseudocode
encrypt_block(key: Bytes, block: Bytes) -> Bytes | AesError
```

Encrypts one 16-byte block.

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `key` | Bytes | yes | 16, 24 or 32 bytes |
| `block` | Bytes | yes | Exactly 16 bytes of plaintext |

**Returns:** The 16-byte ciphertext, or `InvalidKeyLength`, or `InvalidBlockLength`.

**Algorithm:**

1. If `length(key)` is not 16, 24 or 32, return `InvalidKeyLength`.
2. If `length(block) != 16`, return `InvalidBlockLength`.
3. `w = KeyExpansion(key)` (FIPS 197 section 5.2), giving `Nr + 1` round keys of 16 bytes.
4. Load `block` into the 4x4 byte state column by column: `state[r][c] = block[r + 4c]`.
5. `AddRoundKey(state, w[0])`.
6. For round `1 .. Nr-1`: `SubBytes`, `ShiftRows`, `MixColumns`, `AddRoundKey(state, w[round])`.
7. Final round: `SubBytes`, `ShiftRows`, `AddRoundKey(state, w[Nr])` (no `MixColumns`).
8. Return the state read out column by column: `out[r + 4c] = state[r][c]`.

### decrypt_block

```pseudocode
decrypt_block(key: Bytes, block: Bytes) -> Bytes | AesError
```

Decrypts one 16-byte block.

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `key` | Bytes | yes | 16, 24 or 32 bytes |
| `block` | Bytes | yes | Exactly 16 bytes of ciphertext |

**Returns:** The 16-byte plaintext, or `InvalidKeyLength`, or `InvalidBlockLength`.

**Algorithm:**

1. Check the key and then the block length exactly as `encrypt_block` does.
2. `w = KeyExpansion(key)`.
3. Load the state; `AddRoundKey(state, w[Nr])`.
4. For round `Nr-1` down to `1`: `InvShiftRows`, `InvSubBytes`, `AddRoundKey(state, w[round])`, `InvMixColumns`.
5. Final round: `InvShiftRows`, `InvSubBytes`, `AddRoundKey(state, w[0])`.
6. Return the state read out column by column.

The Equivalent Inverse Cipher of FIPS 197 section 5.3.5 MAY be used instead; it gives the same result.

### Round transformations

Bytes are elements of GF(2^8) with the reduction polynomial `m(x) = x^8 + x^4 + x^3 + x + 1` (`0x11b`); addition is XOR.

- **SubBytes** replaces each byte `b` by `S(b)`: take the multiplicative inverse of `b` in GF(2^8) (`0` maps to `0`), then apply the affine map `b'_i = b_i XOR b_(i+4 mod 8) XOR b_(i+5 mod 8) XOR b_(i+6 mod 8) XOR b_(i+7 mod 8) XOR c_i` with `c = 0x63`. **InvSubBytes** applies the inverse map `S^-1`.
- **ShiftRows** rotates row `r` left by `r` positions: `state'[r][c] = state[r][(c + r) mod 4]`. **InvShiftRows** rotates right.
- **MixColumns** multiplies each column by the matrix with first row `[02 03 01 01]` (each next row rotated right by one). **InvMixColumns** uses first row `[0e 0b 0d 09]`.
- **AddRoundKey** XORs the 16-byte round key into the state, column by column.

### KeyExpansion

```pseudocode
KeyExpansion(key):                     // Nk = length(key) / 4, Nr = Nk + 6
  w[0 .. Nk-1] = the key as Nk 4-byte words
  for i in Nk .. 4*(Nr+1)-1:
    temp = w[i-1]
    if i mod Nk == 0:
      temp = SubWord(RotWord(temp)) XOR [Rcon[i/Nk], 0, 0, 0]
    else if Nk > 6 and i mod Nk == 4:
      temp = SubWord(temp)              // AES-256 only
    w[i] = w[i-Nk] XOR temp
  round key j = w[4j] || w[4j+1] || w[4j+2] || w[4j+3]
```

`RotWord([a,b,c,d]) = [b,c,d,a]`; `SubWord` applies `S` to each byte; `Rcon[1..10] = 01 02 04 08 10 20 40 80 1b 36`.

### S-box reference values

`S(b)` for `b = 0x00 .. 0xff`, row by row. These values are for testing a computed S-box (for example a bitsliced circuit); under the constant-time contract they MUST NOT be stored as a table indexed by secret data.

```pseudocode
S = [
  0x63, 0x7c, 0x77, 0x7b, 0xf2, 0x6b, 0x6f, 0xc5, 0x30, 0x01, 0x67, 0x2b, 0xfe, 0xd7, 0xab, 0x76,
  0xca, 0x82, 0xc9, 0x7d, 0xfa, 0x59, 0x47, 0xf0, 0xad, 0xd4, 0xa2, 0xaf, 0x9c, 0xa4, 0x72, 0xc0,
  0xb7, 0xfd, 0x93, 0x26, 0x36, 0x3f, 0xf7, 0xcc, 0x34, 0xa5, 0xe5, 0xf1, 0x71, 0xd8, 0x31, 0x15,
  0x04, 0xc7, 0x23, 0xc3, 0x18, 0x96, 0x05, 0x9a, 0x07, 0x12, 0x80, 0xe2, 0xeb, 0x27, 0xb2, 0x75,
  0x09, 0x83, 0x2c, 0x1a, 0x1b, 0x6e, 0x5a, 0xa0, 0x52, 0x3b, 0xd6, 0xb3, 0x29, 0xe3, 0x2f, 0x84,
  0x53, 0xd1, 0x00, 0xed, 0x20, 0xfc, 0xb1, 0x5b, 0x6a, 0xcb, 0xbe, 0x39, 0x4a, 0x4c, 0x58, 0xcf,
  0xd0, 0xef, 0xaa, 0xfb, 0x43, 0x4d, 0x33, 0x85, 0x45, 0xf9, 0x02, 0x7f, 0x50, 0x3c, 0x9f, 0xa8,
  0x51, 0xa3, 0x40, 0x8f, 0x92, 0x9d, 0x38, 0xf5, 0xbc, 0xb6, 0xda, 0x21, 0x10, 0xff, 0xf3, 0xd2,
  0xcd, 0x0c, 0x13, 0xec, 0x5f, 0x97, 0x44, 0x17, 0xc4, 0xa7, 0x7e, 0x3d, 0x64, 0x5d, 0x19, 0x73,
  0x60, 0x81, 0x4f, 0xdc, 0x22, 0x2a, 0x90, 0x88, 0x46, 0xee, 0xb8, 0x14, 0xde, 0x5e, 0x0b, 0xdb,
  0xe0, 0x32, 0x3a, 0x0a, 0x49, 0x06, 0x24, 0x5c, 0xc2, 0xd3, 0xac, 0x62, 0x91, 0x95, 0xe4, 0x79,
  0xe7, 0xc8, 0x37, 0x6d, 0x8d, 0xd5, 0x4e, 0xa9, 0x6c, 0x56, 0xf4, 0xea, 0x65, 0x7a, 0xae, 0x08,
  0xba, 0x78, 0x25, 0x2e, 0x1c, 0xa6, 0xb4, 0xc6, 0xe8, 0xdd, 0x74, 0x1f, 0x4b, 0xbd, 0x8b, 0x8a,
  0x70, 0x3e, 0xb5, 0x66, 0x48, 0x03, 0xf6, 0x0e, 0x61, 0x35, 0x57, 0xb9, 0x86, 0xc1, 0x1d, 0x9e,
  0xe1, 0xf8, 0x98, 0x11, 0x69, 0xd9, 0x8e, 0x94, 0x9b, 0x1e, 0x87, 0xe9, 0xce, 0x55, 0x28, 0xdf,
  0x8c, 0xa1, 0x89, 0x0d, 0xbf, 0xe6, 0x42, 0x68, 0x41, 0x99, 0x2d, 0x0f, 0xb0, 0x54, 0xbb, 0x16
]
```

## Behavioral Contracts

### encrypt_block(), decrypt_block()

**MUST:**

1. MUST return the value FIPS 197 defines, for each of the three key sizes.
2. MUST check the key length before the block length, and return `InvalidKeyLength` when both are wrong.
3. MUST NOT accept a key of any length other than 16, 24 or 32 bytes: no padding, truncation or hashing of the key.
4. MUST satisfy `decrypt_block(k, encrypt_block(k, b)) == b` and `encrypt_block(k, decrypt_block(k, b)) == b` for every valid `k` and `b`.
5. **Constant time.** For keys of the same length, the sequence of executed instructions and the sequence of memory addresses read or written MUST be identical for every key and every block. In particular:
   - MUST NOT perform any table lookup whose index depends on the key, the block or any intermediate state. This includes S-box tables, inverse S-box tables, T-tables (combined SubBytes and MixColumns tables), `xtime` or multiplication tables, and log/antilog tables, in key expansion as well as in the rounds.
   - MUST NOT branch on a secret-dependent condition, and MUST NOT use an early exit, a secret-dependent loop bound, or a conditional select compiled as a branch.
   - MUST NOT use instructions whose timing depends on their operands with secret operands (for example integer division).
6. MUST NOT modify `key` or `block`.

**SHOULD:**

1. SHOULD overwrite the expanded key and the state before returning or when a cached key schedule is discarded.

**MAY:**

1. MAY use the processor's AES instructions (for example AES-NI on x86-64 or the ARMv8 Cryptography Extensions); they are constant time and satisfy contract 5. A software fallback MUST still satisfy contract 5.
2. MAY process several independent blocks at once internally (bitslicing does), as long as each block's result is the one defined here.

## Test Vectors

Keys and blocks are lowercase hex in `key_hex` and `block_hex`; the expected output block is `result_hex`. The tags `aes-128`, `aes-192` and `aes-256` give the key size.

### Sources

- `fips197-c1` to `fips197-c3` are FIPS 197 (2001) appendix C.1 to C.3 (`round[Nr].output` and the matching inverse-cipher `round[Nr].ioutput`), each in both directions. The 2023 update FIPS 197-upd1 removed appendix C and points to the same values on the NIST site.
- `fips197-appendix-b` is the FIPS 197 appendix B cipher example (`3243f6a8...` under `2b7e1516...`), each in both directions.
- `aesavs` vectors are from the NIST CAVP `KAT_AES.zip` ECB response files (`ECBGFSbox`, `ECBKeySbox`, `ECBVarKey`, `ECBVarTxt` for 128, 192 and 256 bits), the Known Answer Tests of the AESAVS. Each `encrypt_block` sample is the first and the last `COUNT` of a file's `[ENCRYPT]` section; each `decrypt_block` sample is the middle `COUNT` of its `[DECRYPT]` section.
  The tag `kat-count-N` names the `COUNT`.
- Every expected value was recomputed with two independent implementations and matched the source.

### encrypt_block()

```test-vectors
{"id": "encrypt-001", "function": "encrypt_block", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f", "block_hex": "00112233445566778899aabbccddeeff"}}, "expected": {"result_hex": "69c4e0d86a7b0430d8cdb78070b4c55a"}, "tags": ["fips197", "fips197-c1", "aes-128"]}
{"id": "encrypt-002", "function": "encrypt_block", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f1011121314151617", "block_hex": "00112233445566778899aabbccddeeff"}}, "expected": {"result_hex": "dda97ca4864cdfe06eaf70a0ec0d7191"}, "tags": ["fips197", "fips197-c2", "aes-192"]}
{"id": "encrypt-003", "function": "encrypt_block", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "block_hex": "00112233445566778899aabbccddeeff"}}, "expected": {"result_hex": "8ea2b7ca516745bfeafc49904b496089"}, "tags": ["fips197", "fips197-c3", "aes-256"]}
{"id": "encrypt-004", "function": "encrypt_block", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "block_hex": "3243f6a8885a308d313198a2e0370734"}}, "expected": {"result_hex": "3925841d02dc09fbdc118597196a0b32"}, "tags": ["fips197", "fips197-appendix-b", "aes-128"]}
{"id": "encrypt-005", "function": "encrypt_block", "input": {"args": {"key_hex": "00000000000000000000000000000000", "block_hex": "f34481ec3cc627bacd5dc3fb08f273e6"}}, "expected": {"result_hex": "0336763e966d92595a567cc9ce537f5e"}, "tags": ["aesavs", "gfsbox", "aes-128", "kat-count-0"]}
{"id": "encrypt-006", "function": "encrypt_block", "input": {"args": {"key_hex": "00000000000000000000000000000000", "block_hex": "58c8e00b2631686d54eab84b91f0aca1"}}, "expected": {"result_hex": "08a4e2efec8a8e3312ca7460b9040bbf"}, "tags": ["aesavs", "gfsbox", "aes-128", "kat-count-6"]}
{"id": "encrypt-007", "function": "encrypt_block", "input": {"args": {"key_hex": "000000000000000000000000000000000000000000000000", "block_hex": "1b077a6af4b7f98229de786d7516b639"}}, "expected": {"result_hex": "275cfc0413d8ccb70513c3859b1d0f72"}, "tags": ["aesavs", "gfsbox", "aes-192", "kat-count-0"]}
{"id": "encrypt-008", "function": "encrypt_block", "input": {"args": {"key_hex": "000000000000000000000000000000000000000000000000", "block_hex": "941a4773058224e1ef66d10e0a6ee782"}}, "expected": {"result_hex": "067cd9d3749207791841562507fa9626"}, "tags": ["aesavs", "gfsbox", "aes-192", "kat-count-5"]}
{"id": "encrypt-009", "function": "encrypt_block", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000000000000000000000000000", "block_hex": "014730f80ac625fe84f026c60bfd547d"}}, "expected": {"result_hex": "5c9d844ed46f9885085e5d6a4f94c7d7"}, "tags": ["aesavs", "gfsbox", "aes-256", "kat-count-0"]}
{"id": "encrypt-010", "function": "encrypt_block", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000000000000000000000000000", "block_hex": "91fbef2d15a97816060bee1feaa49afe"}}, "expected": {"result_hex": "1bc704f1bce135ceb810341b216d7abe"}, "tags": ["aesavs", "gfsbox", "aes-256", "kat-count-4"]}
{"id": "encrypt-011", "function": "encrypt_block", "input": {"args": {"key_hex": "10a58869d74be5a374cf867cfb473859", "block_hex": "00000000000000000000000000000000"}}, "expected": {"result_hex": "6d251e6944b051e04eaa6fb4dbf78465"}, "tags": ["aesavs", "keysbox", "aes-128", "kat-count-0"]}
{"id": "encrypt-012", "function": "encrypt_block", "input": {"args": {"key_hex": "febd9a24d8b65c1c787d50a4ed3619a9", "block_hex": "00000000000000000000000000000000"}}, "expected": {"result_hex": "f4a70d8af877f9b02b4c40df57d45b17"}, "tags": ["aesavs", "keysbox", "aes-128", "kat-count-20"]}
{"id": "encrypt-013", "function": "encrypt_block", "input": {"args": {"key_hex": "e9f065d7c13573587f7875357dfbb16c53489f6a4bd0f7cd", "block_hex": "00000000000000000000000000000000"}}, "expected": {"result_hex": "0956259c9cd5cfd0181cca53380cde06"}, "tags": ["aesavs", "keysbox", "aes-192", "kat-count-0"]}
{"id": "encrypt-014", "function": "encrypt_block", "input": {"args": {"key_hex": "c88f5b00a4ef9a6840e2acaf33f00a3bdc4e25895303fa72", "block_hex": "00000000000000000000000000000000"}}, "expected": {"result_hex": "a67cf333b314d411d3c0ae6e1cfcd8f5"}, "tags": ["aesavs", "keysbox", "aes-192", "kat-count-23"]}
{"id": "encrypt-015", "function": "encrypt_block", "input": {"args": {"key_hex": "c47b0294dbbbee0fec4757f22ffeee3587ca4730c3d33b691df38bab076bc558", "block_hex": "00000000000000000000000000000000"}}, "expected": {"result_hex": "46f2fb342d6f0ab477476fc501242c5f"}, "tags": ["aesavs", "keysbox", "aes-256", "kat-count-0"]}
{"id": "encrypt-016", "function": "encrypt_block", "input": {"args": {"key_hex": "fca02f3d5011cfc5c1e23165d413a049d4526a991827424d896fe3435e0bf68e", "block_hex": "00000000000000000000000000000000"}}, "expected": {"result_hex": "179a49c712154bbffbe6e7a84a18e220"}, "tags": ["aesavs", "keysbox", "aes-256", "kat-count-15"]}
{"id": "encrypt-017", "function": "encrypt_block", "input": {"args": {"key_hex": "80000000000000000000000000000000", "block_hex": "00000000000000000000000000000000"}}, "expected": {"result_hex": "0edd33d3c621e546455bd8ba1418bec8"}, "tags": ["aesavs", "varkey", "aes-128", "kat-count-0"]}
{"id": "encrypt-018", "function": "encrypt_block", "input": {"args": {"key_hex": "ffffffffffffffffffffffffffffffff", "block_hex": "00000000000000000000000000000000"}}, "expected": {"result_hex": "a1f6258c877d5fcd8964484538bfc92c"}, "tags": ["aesavs", "varkey", "aes-128", "kat-count-127"]}
{"id": "encrypt-019", "function": "encrypt_block", "input": {"args": {"key_hex": "800000000000000000000000000000000000000000000000", "block_hex": "00000000000000000000000000000000"}}, "expected": {"result_hex": "de885dc87f5a92594082d02cc1e1b42c"}, "tags": ["aesavs", "varkey", "aes-192", "kat-count-0"]}
{"id": "encrypt-020", "function": "encrypt_block", "input": {"args": {"key_hex": "ffffffffffffffffffffffffffffffffffffffffffffffff", "block_hex": "00000000000000000000000000000000"}}, "expected": {"result_hex": "dd8a493514231cbf56eccee4c40889fb"}, "tags": ["aesavs", "varkey", "aes-192", "kat-count-191"]}
{"id": "encrypt-021", "function": "encrypt_block", "input": {"args": {"key_hex": "8000000000000000000000000000000000000000000000000000000000000000", "block_hex": "00000000000000000000000000000000"}}, "expected": {"result_hex": "e35a6dcb19b201a01ebcfa8aa22b5759"}, "tags": ["aesavs", "varkey", "aes-256", "kat-count-0"]}
{"id": "encrypt-022", "function": "encrypt_block", "input": {"args": {"key_hex": "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "block_hex": "00000000000000000000000000000000"}}, "expected": {"result_hex": "4bf85f1b5d54adbc307b0a048389adcb"}, "tags": ["aesavs", "varkey", "aes-256", "kat-count-255"]}
{"id": "encrypt-023", "function": "encrypt_block", "input": {"args": {"key_hex": "00000000000000000000000000000000", "block_hex": "80000000000000000000000000000000"}}, "expected": {"result_hex": "3ad78e726c1ec02b7ebfe92b23d9ec34"}, "tags": ["aesavs", "vartxt", "aes-128", "kat-count-0"]}
{"id": "encrypt-024", "function": "encrypt_block", "input": {"args": {"key_hex": "00000000000000000000000000000000", "block_hex": "ffffffffffffffffffffffffffffffff"}}, "expected": {"result_hex": "3f5b8cc9ea855a0afa7347d23e8d664e"}, "tags": ["aesavs", "vartxt", "aes-128", "kat-count-127"]}
{"id": "encrypt-025", "function": "encrypt_block", "input": {"args": {"key_hex": "000000000000000000000000000000000000000000000000", "block_hex": "80000000000000000000000000000000"}}, "expected": {"result_hex": "6cd02513e8d4dc986b4afe087a60bd0c"}, "tags": ["aesavs", "vartxt", "aes-192", "kat-count-0"]}
{"id": "encrypt-026", "function": "encrypt_block", "input": {"args": {"key_hex": "000000000000000000000000000000000000000000000000", "block_hex": "ffffffffffffffffffffffffffffffff"}}, "expected": {"result_hex": "b13db4da1f718bc6904797c82bcf2d32"}, "tags": ["aesavs", "vartxt", "aes-192", "kat-count-127"]}
{"id": "encrypt-027", "function": "encrypt_block", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000000000000000000000000000", "block_hex": "80000000000000000000000000000000"}}, "expected": {"result_hex": "ddc6bf790c15760d8d9aeb6f9a75fd4e"}, "tags": ["aesavs", "vartxt", "aes-256", "kat-count-0"]}
{"id": "encrypt-028", "function": "encrypt_block", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000000000000000000000000000", "block_hex": "ffffffffffffffffffffffffffffffff"}}, "expected": {"result_hex": "acdace8078a32b1a182bfa4987ca1347"}, "tags": ["aesavs", "vartxt", "aes-256", "kat-count-127"]}
```

### decrypt_block()

```test-vectors
{"id": "decrypt-001", "function": "decrypt_block", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f", "block_hex": "69c4e0d86a7b0430d8cdb78070b4c55a"}}, "expected": {"result_hex": "00112233445566778899aabbccddeeff"}, "tags": ["fips197", "fips197-c1", "aes-128"]}
{"id": "decrypt-002", "function": "decrypt_block", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f1011121314151617", "block_hex": "dda97ca4864cdfe06eaf70a0ec0d7191"}}, "expected": {"result_hex": "00112233445566778899aabbccddeeff"}, "tags": ["fips197", "fips197-c2", "aes-192"]}
{"id": "decrypt-003", "function": "decrypt_block", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "block_hex": "8ea2b7ca516745bfeafc49904b496089"}}, "expected": {"result_hex": "00112233445566778899aabbccddeeff"}, "tags": ["fips197", "fips197-c3", "aes-256"]}
{"id": "decrypt-004", "function": "decrypt_block", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "block_hex": "3925841d02dc09fbdc118597196a0b32"}}, "expected": {"result_hex": "3243f6a8885a308d313198a2e0370734"}, "tags": ["fips197", "fips197-appendix-b", "aes-128"]}
{"id": "decrypt-005", "function": "decrypt_block", "input": {"args": {"key_hex": "00000000000000000000000000000000", "block_hex": "dc43be40be0e53712f7e2bf5ca707209"}}, "expected": {"result_hex": "6a118a874519e64e9963798a503f1d35"}, "tags": ["aesavs", "gfsbox", "aes-128", "kat-count-3"]}
{"id": "decrypt-006", "function": "decrypt_block", "input": {"args": {"key_hex": "000000000000000000000000000000000000000000000000", "block_hex": "4f354592ff7c8847d2d0870ca9481b7c"}}, "expected": {"result_hex": "51719783d3185a535bd75adc65071ce1"}, "tags": ["aesavs", "gfsbox", "aes-192", "kat-count-3"]}
{"id": "decrypt-007", "function": "decrypt_block", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000000000000000000000000000", "block_hex": "623a52fcea5d443e48d9181ab32c7421"}}, "expected": {"result_hex": "761c1fe41a18acf20d241650611d90f1"}, "tags": ["aesavs", "gfsbox", "aes-256", "kat-count-2"]}
{"id": "decrypt-008", "function": "decrypt_block", "input": {"args": {"key_hex": "da84367f325d42d601b4326964802e8e", "block_hex": "bba071bcb470f8f6586e5d3add18bc66"}}, "expected": {"result_hex": "00000000000000000000000000000000"}, "tags": ["aesavs", "keysbox", "aes-128", "kat-count-10"]}
{"id": "decrypt-009", "function": "decrypt_block", "input": {"args": {"key_hex": "b0ab0a6a818baef2d11fa33eac947284fb7d748cfb75e570", "block_hex": "d2eafd86f63b109b91f5dbb3a3fb7e13"}}, "expected": {"result_hex": "00000000000000000000000000000000"}, "tags": ["aesavs", "keysbox", "aes-192", "kat-count-12"]}
{"id": "decrypt-010", "function": "decrypt_block", "input": {"args": {"key_hex": "797f8b3d176dac5b7e34a2d539c4ef367a16f8635f6264737591c5c07bf57a3e", "block_hex": "a74289fe73a4c123ca189ea1e1b49ad5"}}, "expected": {"result_hex": "00000000000000000000000000000000"}, "tags": ["aesavs", "keysbox", "aes-256", "kat-count-8"]}
{"id": "decrypt-011", "function": "decrypt_block", "input": {"args": {"key_hex": "ffffffffffffffff8000000000000000", "block_hex": "32cd652842926aea4aa6137bb2be2b5e"}}, "expected": {"result_hex": "00000000000000000000000000000000"}, "tags": ["aesavs", "varkey", "aes-128", "kat-count-64"]}
{"id": "decrypt-012", "function": "decrypt_block", "input": {"args": {"key_hex": "ffffffffffffffffffffffff800000000000000000000000", "block_hex": "e34ec71d6128d4871865d617c30b37e3"}}, "expected": {"result_hex": "00000000000000000000000000000000"}, "tags": ["aesavs", "varkey", "aes-192", "kat-count-96"]}
{"id": "decrypt-013", "function": "decrypt_block", "input": {"args": {"key_hex": "ffffffffffffffffffffffffffffffff80000000000000000000000000000000", "block_hex": "e3714e94a5778955cc0346358e94783a"}}, "expected": {"result_hex": "00000000000000000000000000000000"}, "tags": ["aesavs", "varkey", "aes-256", "kat-count-128"]}
{"id": "decrypt-014", "function": "decrypt_block", "input": {"args": {"key_hex": "00000000000000000000000000000000", "block_hex": "41f992a856fb278b389a62f5d274d7e9"}}, "expected": {"result_hex": "ffffffffffffffff8000000000000000"}, "tags": ["aesavs", "vartxt", "aes-128", "kat-count-64"]}
{"id": "decrypt-015", "function": "decrypt_block", "input": {"args": {"key_hex": "000000000000000000000000000000000000000000000000", "block_hex": "c8aa80a7850675bc007c46df06b49868"}}, "expected": {"result_hex": "ffffffffffffffff8000000000000000"}, "tags": ["aesavs", "vartxt", "aes-192", "kat-count-64"]}
{"id": "decrypt-016", "function": "decrypt_block", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000000000000000000000000000", "block_hex": "77f392089042e478ac16c0c86a0b5db5"}}, "expected": {"result_hex": "ffffffffffffffff8000000000000000"}, "tags": ["aesavs", "vartxt", "aes-256", "kat-count-64"]}
```

### Errors

```test-vectors
{"id": "aes-err-001", "function": "encrypt_block", "input": {"args": {"key_hex": "", "block_hex": "3243f6a8885a308d313198a2e0370734"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "aes-err-002", "function": "encrypt_block", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e", "block_hex": "3243f6a8885a308d313198a2e0370734"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "aes-err-003", "function": "encrypt_block", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f10", "block_hex": "3243f6a8885a308d313198a2e0370734"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "aes-err-004", "function": "encrypt_block", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f10111213141516", "block_hex": "3243f6a8885a308d313198a2e0370734"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "aes-err-005", "function": "encrypt_block", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e", "block_hex": "3243f6a8885a308d313198a2e0370734"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "aes-err-006", "function": "encrypt_block", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20", "block_hex": "3243f6a8885a308d313198a2e0370734"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "aes-err-007", "function": "encrypt_block", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "block_hex": ""}}, "expected": {"error": "InvalidBlockLength"}, "tags": ["invalid-block-length", "edge"]}
{"id": "aes-err-008", "function": "encrypt_block", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "block_hex": "000000000000000000000000000000"}}, "expected": {"error": "InvalidBlockLength"}, "tags": ["invalid-block-length", "edge"]}
{"id": "aes-err-009", "function": "encrypt_block", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "block_hex": "0000000000000000000000000000000000"}}, "expected": {"error": "InvalidBlockLength"}, "tags": ["invalid-block-length", "edge"]}
{"id": "aes-err-010", "function": "encrypt_block", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "block_hex": "0000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"error": "InvalidBlockLength"}, "tags": ["invalid-block-length", "edge"]}
{"id": "aes-err-011", "function": "encrypt_block", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000", "block_hex": "000000000000000000000000000000"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "invalid-block-length", "check-order"]}
{"id": "aes-err-012", "function": "decrypt_block", "input": {"args": {"key_hex": "", "block_hex": "3243f6a8885a308d313198a2e0370734"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "aes-err-013", "function": "decrypt_block", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f10", "block_hex": "3243f6a8885a308d313198a2e0370734"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "aes-err-014", "function": "decrypt_block", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20", "block_hex": "3243f6a8885a308d313198a2e0370734"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "aes-err-015", "function": "decrypt_block", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "block_hex": ""}}, "expected": {"error": "InvalidBlockLength"}, "tags": ["invalid-block-length", "edge"]}
{"id": "aes-err-016", "function": "decrypt_block", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "block_hex": "000000000000000000000000000000"}}, "expected": {"error": "InvalidBlockLength"}, "tags": ["invalid-block-length", "edge"]}
{"id": "aes-err-017", "function": "decrypt_block", "input": {"args": {"key_hex": "2b7e151628aed2a6abf7158809cf4f3c", "block_hex": "0000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"error": "InvalidBlockLength"}, "tags": ["invalid-block-length", "edge"]}
{"id": "aes-err-018", "function": "decrypt_block", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000", "block_hex": "000000000000000000000000000000"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "invalid-block-length", "check-order"]}
```

## Edge Cases

- **Key lengths.** Only 16, 24 and 32 bytes are valid. 0, 15, 17, 20, 23, 31 and 33 bytes are `InvalidKeyLength`; so is a 64-byte key (an XTS key pair is not an AES key).
- **Block lengths.** Exactly 16 bytes. An empty block, 15 or 17 bytes, and two blocks (32 bytes) are `InvalidBlockLength`; this spec never pads and never processes more than one block.
- **Both wrong.** A 20-byte key with a 15-byte block is `InvalidKeyLength`.
- **All-zero key and block.** Valid and not special: `ECBGFSbox` and `ECBVarTxt` use the all-zero key; `ECBKeySbox` and `ECBVarKey` use the all-zero block.
- **AES-256 key expansion.** The extra `SubWord` at `i mod 8 == 4` exists only for 32-byte keys; omitting it passes every AES-128 and AES-192 vector and fails AES-256.
- **AES-192 key expansion.** `Nk = 6` does not divide the 4-word round key, so round keys straddle the words produced by one expansion step; an expansion written as "one round key per step" is wrong for AES-192.

## Implementation Notes

- **Why not T-tables.** The classic fast software AES merges SubBytes, ShiftRows and MixColumns into four 256-entry 32-bit tables indexed by state bytes. Which cache lines are loaded then depends on `plaintext XOR key`, and Bernstein (2005) and Osvik, Shamir and Tromer (2006) recovered full AES keys from the timing of such lookups, including across processes and over a network.
  A plain 256-byte S-box table leaks the same way. Both are forbidden here.
- **Bitslicing.** Represent the state so that each machine word holds one bit position of many bytes: bit `i` of every state byte (of one or several blocks) lives in word `i`. SubBytes then becomes a fixed Boolean circuit of AND, XOR and NOT over eight words, the same instructions for every input; the Boyar-Peralta circuit computes the S-box in little more than a hundred gates.
  ShiftRows becomes fixed bit permutations within words, MixColumns becomes XORs of rotated words, and AddRoundKey is a XOR. With 32-bit words one can process the 16 bytes of a block in parallel; with 64-bit words, two or four blocks at once. Transposing into and out of bitsliced form costs fixed shifts and masks.
- **Decryption.** With `S = A(inverse(b))` for the affine map `A`, the inverse S-box is `inverse(A^-1(b))`, which equals `A^-1(S(A^-1(b)))`: the inverse affine map, the forward S-box circuit, and the inverse affine map again, all fixed XORs around one circuit.
  InvMixColumns equals MixColumns applied after multiplying each column by the polynomial with coefficients `[05 00 04 00]`, so it costs one MixColumns plus a few `xtime` steps.
- **xtime without a branch.** Multiply-by-2 on a byte `b` is `((b << 1) XOR (0x1b AND (0 - (b >> 7)))) AND 0xff`, with no conditional; a bitsliced implementation applies it to whole words.
- **Key expansion is secret too.** It uses SubWord, so it needs the same constant-time S-box. A table-based key schedule leaks the key even when the rounds are bitsliced.
- **State order.** The input fills the state column by column, so the first four bytes are column 0. Treating them as row 0 transposes the state and fails every vector, which is also the easiest way to catch the bug.
- **Verify the compiled code.** Compilers may turn a masked select into a branch. Inspect the generated code, or test with a constant-time checker such as dudect or ctgrind.

## References

- [FIPS 197 -- Advanced Encryption Standard (AES)](https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.197.pdf) -- the governing standard; appendix B (cipher example) and appendix C (example vectors with intermediate values).
- [FIPS 197-upd1](https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.197-upd1.pdf) -- the 2023 editorial update; the algorithm is unchanged.
- [NIST CAVP block cipher test vectors](https://csrc.nist.gov/projects/cryptographic-algorithm-validation-program/block-ciphers) -- `KAT_AES.zip` and the AESAVS document describing the GFSbox, KeySbox, VarKey and VarTxt tests.
- [D. J. Bernstein, Cache-timing attacks on AES (2005)](https://cr.yp.to/antiforgery/cachetiming-20050414.pdf) -- the key-recovery attack on table-driven AES.
- [E. Kasper and P. Schwabe, Faster and Timing-Attack Resistant AES-GCM (2009)](https://eprint.iacr.org/2009/129) -- bitsliced AES.
- [J. Boyar and R. Peralta, A depth-16 circuit for the AES S-box (2011)](https://eprint.iacr.org/2011/332) -- a compact S-box circuit suited to bitslicing.
