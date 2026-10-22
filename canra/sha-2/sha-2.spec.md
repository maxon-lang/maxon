+++
name = "sha-2"
version = "1"
title = "SHA-2 Hash Functions (SHA-256, SHA-384, SHA-512)"
status = "draft"
keywords = ["hash", "digest", "sha256", "sha384", "sha512", "cryptography"]
difficulty = "moderate"

[api]
functions = ["sha256", "sha384", "sha512", "sha256_init", "sha384_init", "sha512_init", "update", "finalize"]
types = ["HashState"]

[references]
items = [
  { label = "FIPS 180-4 Secure Hash Standard", url = "https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.180-4.pdf" },
  { label = "RFC 6234", url = "https://www.rfc-editor.org/rfc/rfc6234.html" },
  { label = "NIST CAVP SHS byte-oriented test vectors", url = "https://csrc.nist.gov/projects/cryptographic-algorithm-validation-program/secure-hashing" },
]

[test-vectors]
inline = true
external = []
+++

## Overview

This spec defines the SHA-2 message digests SHA-256, SHA-384 and SHA-512 as specified in FIPS 180-4, in two forms:

- **One-shot** -- `sha256`, `sha384` and `sha512` take a whole message and return its digest.
- **Incremental** -- `sha256_init`, `sha384_init` and `sha512_init` create a `HashState`; `update` feeds it any number of byte chunks; `finalize` returns the digest. The incremental digest MUST equal the one-shot digest of the concatenated chunks, however the message was split.

Messages are byte strings. Digests are byte strings of 32 (SHA-256), 48 (SHA-384) or 64 (SHA-512) bytes.

**Out of scope:** SHA-224, SHA-512/224, SHA-512/256 and SHA-512/t in general, SHA-1, SHA-3, messages whose length is not a whole number of bytes, keyed hashing (HMAC is a separate spec), hex or base64 formatting of digests, and hardware-accelerated paths (they are permitted, but are not specified here).

## Types

### HashState

An opaque, mutable hashing state. It records which algorithm created it, the chaining value (eight words), the bytes fed so far that do not yet fill a block, and the total message length in bytes.

```pseudocode
type HashState  // opaque; created by sha256_init, sha384_init or sha512_init
```

### Parameters per algorithm

| Algorithm | Word size | Block size | Rounds | Length field | Digest size | Initial value | Round constants |
|---|---|---|---|---|---|---|---|
| SHA-256 | 32 bits | 64 bytes | 64 | 8 bytes | 32 bytes | `H256` | `K256` |
| SHA-384 | 64 bits | 128 bytes | 80 | 16 bytes | 48 bytes | `H384` | `K512` |
| SHA-512 | 64 bits | 128 bytes | 80 | 16 bytes | 64 bytes | `H512` | `K512` |

SHA-384 is SHA-512 with a different initial value and its output truncated to the first 48 bytes (the first six words).

## Functions

### sha256

```pseudocode
sha256(data: Bytes) -> Bytes
```

Returns the 32-byte SHA-256 digest of `data`.

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `data` | Bytes | yes | The message, of any length including zero |

**Returns:** 32 bytes. There is no error case.

**Algorithm:**

1. `state = sha256_init()`.
2. `update(state, data)`.
3. Return `finalize(state)`.

### sha384

```pseudocode
sha384(data: Bytes) -> Bytes
```

Returns the 48-byte SHA-384 digest of `data`. Same as `sha256` with `sha384_init`.

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `data` | Bytes | yes | The message, of any length including zero |

**Returns:** 48 bytes. There is no error case.

**Algorithm:**

1. `state = sha384_init()`; `update(state, data)`; return `finalize(state)`.

### sha512

```pseudocode
sha512(data: Bytes) -> Bytes
```

Returns the 64-byte SHA-512 digest of `data`. Same as `sha256` with `sha512_init`.

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `data` | Bytes | yes | The message, of any length including zero |

**Returns:** 64 bytes. There is no error case.

**Algorithm:**

1. `state = sha512_init()`; `update(state, data)`; return `finalize(state)`.

### sha256_init, sha384_init, sha512_init

```pseudocode
sha256_init() -> HashState
sha384_init() -> HashState
sha512_init() -> HashState
```

Creates a fresh hashing state for the named algorithm.

**Parameters:** None.

**Returns:** A `HashState` whose chaining value is the algorithm's initial value (`H256`, `H384` or `H512`), with an empty pending buffer and a message length of 0.

**Algorithm:**

1. Set the chaining value `H[0..7]` to the algorithm's initial value.
2. Set the pending buffer to empty and the byte count to 0.

### update

```pseudocode
update(state: HashState, data: Bytes) -> Null
```

Appends `data` to the message being hashed.

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `state` | HashState | yes | A state from one of the `*_init` functions that has not been finalized |
| `data` | Bytes | yes | The next chunk of the message; MAY be empty |

**Returns:** Nothing. The state is modified in place.

**Algorithm:**

1. Add `length(data)` to the byte count.
2. Append `data` to the pending buffer.
3. While the pending buffer holds at least one full block (64 or 128 bytes), remove the first block and apply the compression function to it.

### finalize

```pseudocode
finalize(state: HashState) -> Bytes
```

Pads the message and returns its digest. The state is consumed.

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `state` | HashState | yes | A state that has not been finalized |

**Returns:** The digest: 32, 48 or 64 bytes depending on the algorithm that created the state.

**Algorithm:**

1. Let `L` be the message length in bits (`8 * byte count`).
2. Append the byte `0x80` to the pending buffer.
3. Append zero bytes until the pending buffer length is congruent to `block_size - length_field_size` modulo `block_size` (56 mod 64 for SHA-256, 112 mod 128 for SHA-384/512). This MAY require one extra block.
4. Append `L` as a big-endian unsigned integer of `length_field_size` bytes (8 bytes for SHA-256, 16 bytes for SHA-384/512).
5. Apply the compression function to each remaining block.
6. Serialize the chaining value words `H[0..7]` big-endian, and return the first 32 bytes (SHA-256), 48 bytes (SHA-384) or 64 bytes (SHA-512).

### Compression function

All arithmetic is on unsigned words of the algorithm's word size (32 or 64 bits); `+` is addition modulo 2^w, `ROTR(x, n)` is a right rotation, `SHR(x, n)` is a right shift, `XOR`, `AND` and `NOT` are bitwise.

```pseudocode
compress(H: [Word; 8], block: Bytes)
  W[0..15] = the block split into 16 big-endian words
  for t in 16 .. rounds-1:
    W[t] = sigma1(W[t-2]) + W[t-7] + sigma0(W[t-15]) + W[t-16]
  a, b, c, d, e, f, g, h = H[0..7]
  for t in 0 .. rounds-1:
    T1 = h + SIGMA1(e) + Ch(e, f, g) + K[t] + W[t]
    T2 = SIGMA0(a) + Maj(a, b, c)
    h = g; g = f; f = e; e = d + T1
    d = c; c = b; b = a; a = T1 + T2
  H[0..7] = H[0..7] + (a, b, c, d, e, f, g, h)   // word-wise

Ch(x, y, z)  = (x AND y) XOR ((NOT x) AND z)
Maj(x, y, z) = (x AND y) XOR (x AND z) XOR (y AND z)
```

| Function | SHA-256 (32-bit words) | SHA-384 / SHA-512 (64-bit words) |
|---|---|---|
| `SIGMA0(x)` | `ROTR(x,2) XOR ROTR(x,13) XOR ROTR(x,22)` | `ROTR(x,28) XOR ROTR(x,34) XOR ROTR(x,39)` |
| `SIGMA1(x)` | `ROTR(x,6) XOR ROTR(x,11) XOR ROTR(x,25)` | `ROTR(x,14) XOR ROTR(x,18) XOR ROTR(x,41)` |
| `sigma0(x)` | `ROTR(x,7) XOR ROTR(x,18) XOR SHR(x,3)` | `ROTR(x,1) XOR ROTR(x,8) XOR SHR(x,7)` |
| `sigma1(x)` | `ROTR(x,17) XOR ROTR(x,19) XOR SHR(x,10)` | `ROTR(x,19) XOR ROTR(x,61) XOR SHR(x,6)` |

### Constants

`H256` and `H512` are the first 32 and 64 bits of the fractional parts of the square roots of the first eight primes (2 to 19). `H384` is the same for the ninth to sixteenth primes (23 to 53). `K256` (64 words) and `K512` (80 words) are the first 32 and 64 bits of the fractional parts of the cube roots of the first 64 and 80 primes. All values are hexadecimal, in index order.

```pseudocode
H256 = [
  0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19
]
```

```pseudocode
H384 = [
  0xcbbb9d5dc1059ed8, 0x629a292a367cd507, 0x9159015a3070dd17, 0x152fecd8f70e5939,
  0x67332667ffc00b31, 0x8eb44a8768581511, 0xdb0c2e0d64f98fa7, 0x47b5481dbefa4fa4
]
```

```pseudocode
H512 = [
  0x6a09e667f3bcc908, 0xbb67ae8584caa73b, 0x3c6ef372fe94f82b, 0xa54ff53a5f1d36f1,
  0x510e527fade682d1, 0x9b05688c2b3e6c1f, 0x1f83d9abfb41bd6b, 0x5be0cd19137e2179
]
```

```pseudocode
K256 = [
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
  0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
  0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
  0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
]
```

```pseudocode
K512 = [
  0x428a2f98d728ae22, 0x7137449123ef65cd, 0xb5c0fbcfec4d3b2f, 0xe9b5dba58189dbbc,
  0x3956c25bf348b538, 0x59f111f1b605d019, 0x923f82a4af194f9b, 0xab1c5ed5da6d8118,
  0xd807aa98a3030242, 0x12835b0145706fbe, 0x243185be4ee4b28c, 0x550c7dc3d5ffb4e2,
  0x72be5d74f27b896f, 0x80deb1fe3b1696b1, 0x9bdc06a725c71235, 0xc19bf174cf692694,
  0xe49b69c19ef14ad2, 0xefbe4786384f25e3, 0x0fc19dc68b8cd5b5, 0x240ca1cc77ac9c65,
  0x2de92c6f592b0275, 0x4a7484aa6ea6e483, 0x5cb0a9dcbd41fbd4, 0x76f988da831153b5,
  0x983e5152ee66dfab, 0xa831c66d2db43210, 0xb00327c898fb213f, 0xbf597fc7beef0ee4,
  0xc6e00bf33da88fc2, 0xd5a79147930aa725, 0x06ca6351e003826f, 0x142929670a0e6e70,
  0x27b70a8546d22ffc, 0x2e1b21385c26c926, 0x4d2c6dfc5ac42aed, 0x53380d139d95b3df,
  0x650a73548baf63de, 0x766a0abb3c77b2a8, 0x81c2c92e47edaee6, 0x92722c851482353b,
  0xa2bfe8a14cf10364, 0xa81a664bbc423001, 0xc24b8b70d0f89791, 0xc76c51a30654be30,
  0xd192e819d6ef5218, 0xd69906245565a910, 0xf40e35855771202a, 0x106aa07032bbd1b8,
  0x19a4c116b8d2d0c8, 0x1e376c085141ab53, 0x2748774cdf8eeb99, 0x34b0bcb5e19b48a8,
  0x391c0cb3c5c95a63, 0x4ed8aa4ae3418acb, 0x5b9cca4f7763e373, 0x682e6ff3d6b2b8a3,
  0x748f82ee5defb2fc, 0x78a5636f43172f60, 0x84c87814a1f0ab72, 0x8cc702081a6439ec,
  0x90befffa23631e28, 0xa4506cebde82bde9, 0xbef9a3f7b2c67915, 0xc67178f2e372532b,
  0xca273eceea26619c, 0xd186b8c721c0c207, 0xeada7dd6cde0eb1e, 0xf57d4f7fee6ed178,
  0x06f067aa72176fba, 0x0a637dc5a2c898a6, 0x113f9804bef90dae, 0x1b710b35131c471b,
  0x28db77f523047d84, 0x32caab7b40c72493, 0x3c9ebe0a15c9bebc, 0x431d67c49c100d4c,
  0x4cc5d4becb3e42b6, 0x597f299cfc657e2a, 0x5fcb6fab3ad6faec, 0x6c44198c4a475817
]
```

## Behavioral Contracts

### sha256(), sha384(), sha512()

**MUST:**

1. MUST return exactly the digest FIPS 180-4 defines for the message: 32, 48 and 64 bytes respectively.
2. MUST accept a message of any length, including the empty message, and never fail.
3. MUST NOT modify the input.

### sha256_init(), sha384_init(), sha512_init()

**MUST:**

1. Each call MUST return a new state that shares nothing with any other state; two states fed different data MUST NOT affect each other.

### update()

**MUST:**

1. MUST accept an empty chunk, which leaves the digest unchanged.
2. The digest MUST depend only on the concatenation of the chunks, never on how the message was split: for every split of a message `m` into chunks `c1 .. cn` (n >= 0), `init; update(c1); ...; update(cn); finalize` MUST equal the one-shot digest of `m`.
3. MUST NOT retain a reference to `data` after it returns; the caller MAY reuse or modify the buffer.

### finalize()

**MUST:**

1. MUST return the digest of every byte passed to `update` since the state was created, in order.
2. MUST consume the state. Where the language can express consumption (move semantics, linear or affine types), a finalized state MUST NOT be usable. Otherwise, any later `update` or `finalize` on that state MUST fail loudly as a programming error (an exception, panic or trap); it MUST NOT return a digest and MUST NOT silently reset the state.

**SHOULD:**

1. SHOULD overwrite the pending buffer and chaining value when the state is finalized or discarded, so the message does not linger in memory.

**MAY:**

1. MAY offer a separate operation that copies a state, so a common prefix is hashed once; the copy MUST be independent of the original.

## Test Vectors

All messages and digests are lowercase hex. One-shot vectors call `sha256`, `sha384` or `sha512` with a `data_hex` argument.

### Test harness functions

The incremental contract is tested through three harness functions. They are not part of the public API; a test suite implements each in terms of it:

```pseudocode
sha256_chunked(chunks: [Bytes]) -> Bytes   // state = sha256_init(); update(state, c) for each c in order; return finalize(state)
sha384_chunked(chunks: [Bytes]) -> Bytes   // the same with sha384_init
sha512_chunked(chunks: [Bytes]) -> Bytes   // the same with sha512_init
```

Their vectors pass the list as `chunks_hex`, a list of hex strings; an empty list means `update` is never called. Every chunked vector's expected digest equals the one-shot digest of the concatenated chunks, which is itself a published value below.

### Sources

- Empty-message and padding-boundary messages (55, 56, 63, 64 bytes for SHA-256; 111, 112, 127, 128 bytes for SHA-384/512) are the `Len = 0, 440, 448, 504, 512, 888, 896, 1016, 1024` entries of the NIST CAVP byte-oriented `SHA256ShortMsg.rsp`, `SHA384ShortMsg.rsp` and `SHA512ShortMsg.rsp` files.
- `"abc"`, the 448-bit message `"abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq"` (SHA-256) and the 896-bit message `"abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu"` (SHA-384/512) are the FIPS 180-4 examples, with digests from RFC 6234 section 8.5 (TEST1, TEST2_1, TEST2_2).

### sha256()

```test-vectors
{"id": "sha256-001", "function": "sha256", "input": {"args": {"data_hex": ""}}, "expected": {"result_hex": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"}, "tags": ["empty", "edge", "nist-cavp"]}
{"id": "sha256-002", "function": "sha256", "input": {"args": {"data_hex": "616263"}}, "expected": {"result_hex": "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"}, "tags": ["abc", "fips-180-4-example"]}
{"id": "sha256-003", "function": "sha256", "input": {"args": {"data_hex": "6162636462636465636465666465666765666768666768696768696a68696a6b696a6b6c6a6b6c6d6b6c6d6e6c6d6e6f6d6e6f706e6f7071"}}, "expected": {"result_hex": "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"}, "tags": ["two-block-448-bit", "fips-180-4-example"]}
{"id": "sha256-004", "function": "sha256", "input": {"args": {"data_hex": "3ebfb06db8c38d5ba037f1363e118550aad94606e26835a01af05078533cc25f2f39573c04b632f62f68c294ab31f2a3e2a1a0d8c2be51"}}, "expected": {"result_hex": "6595a2ef537a69ba8583dfbf7f5bec0ab1f93ce4c8ee1916eff44a93af5749c4"}, "tags": ["padding-boundary", "length-55", "edge", "nist-cavp"]}
{"id": "sha256-005", "function": "sha256", "input": {"args": {"data_hex": "2d52447d1244d2ebc28650e7b05654bad35b3a68eedc7f8515306b496d75f3e73385dd1b002625024b81a02f2fd6dffb6e6d561cb7d0bd7a"}}, "expected": {"result_hex": "cfb88d6faf2de3a69d36195acec2e255e2af2b7d933997f348e09f6ce5758360"}, "tags": ["padding-boundary", "length-56", "edge", "nist-cavp"]}
{"id": "sha256-006", "function": "sha256", "input": {"args": {"data_hex": "e2f76e97606a872e317439f1a03fcd92e632e5bd4e7cbc4e97f1afc19a16fde92d77cbe546416b51640cddb92af996534dfd81edb17c4424cf1ac4d75aceeb"}}, "expected": {"result_hex": "18041bd4665083001fba8c5411d2d748e8abbfdcdfd9218cb02b68a78e7d4c23"}, "tags": ["padding-boundary", "length-63", "edge", "nist-cavp"]}
{"id": "sha256-007", "function": "sha256", "input": {"args": {"data_hex": "5a86b737eaea8ee976a0a24da63e7ed7eefad18a101c1211e2b3650c5187c2a8a650547208251f6d4237e661c7bf4c77f335390394c37fa1a9f9be836ac28509"}}, "expected": {"result_hex": "42e61e174fbb3897d6dd6cef3dd2802fe67b331953b06114a65c772859dfc1aa"}, "tags": ["padding-boundary", "length-64", "edge", "nist-cavp"]}
```

### sha384()

```test-vectors
{"id": "sha384-001", "function": "sha384", "input": {"args": {"data_hex": ""}}, "expected": {"result_hex": "38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da274edebfe76f65fbd51ad2f14898b95b"}, "tags": ["empty", "edge", "nist-cavp"]}
{"id": "sha384-002", "function": "sha384", "input": {"args": {"data_hex": "616263"}}, "expected": {"result_hex": "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7"}, "tags": ["abc", "fips-180-4-example"]}
{"id": "sha384-003", "function": "sha384", "input": {"args": {"data_hex": "61626364656667686263646566676869636465666768696a6465666768696a6b65666768696a6b6c666768696a6b6c6d6768696a6b6c6d6e68696a6b6c6d6e6f696a6b6c6d6e6f706a6b6c6d6e6f70716b6c6d6e6f7071726c6d6e6f707172736d6e6f70717273746e6f707172737475"}}, "expected": {"result_hex": "09330c33f71147e83d192fc782cd1b4753111b173b3b05d22fa08086e3b0f712fcc7c71a557e2db966c3e9fa91746039"}, "tags": ["two-block-896-bit", "fips-180-4-example"]}
{"id": "sha384-004", "function": "sha384", "input": {"args": {"data_hex": "a04f390a9cc2effad05db80d9076a8d4b6cc8bba97b27b423670b290b8e69c2b187230011c1481ac88d090f39154659494db5e410851c6e8b2b8a93717cae76037e0881978124fe7e1a0929d8891491f4e99646cc94062dc82411fa66130eda46560e75b98048236439465125e737b"}}, "expected": {"result_hex": "e7089d72945cef851e689b4409cfb63d135f0b5cdfb0dac6c3a292dd70371ab4b79da1997d7992906ac7213502662920"}, "tags": ["padding-boundary", "length-111", "edge", "nist-cavp"]}
{"id": "sha384-005", "function": "sha384", "input": {"args": {"data_hex": "f419494c3c6d0727b3395a483a2167182a7252f4fd099c2d4b71b053f94bb8b3adf3b51e8460cfec084ce9415c95798fbae4975c208c544645b54c44d2b97f2ecfce5c805be61f5ba1d35dcc07afdd51a87baa990506668cf710e18be9b0ebf943f366fa29c69f7a6616de72a3353b66"}}, "expected": {"result_hex": "aead8688c58c6ba4e9cadb4756b465dce0fb06f1cfaa478197f2ea89414e47e9572034adfed160703c79b82b3fd7ab78"}, "tags": ["padding-boundary", "length-112", "edge", "nist-cavp"]}
{"id": "sha384-006", "function": "sha384", "input": {"args": {"data_hex": "dbed7612448d46cbe0a384d1c93233f02ffd1c984ba765299518656d3723b766c1658d4b1e7047cdc729459e366ef9349efc40cbd990f2a9a24db7a5045e1dea12dce8f9d9f2aaed933f93031e7b8959ac5e7bf6bbbdf30b48f7eb783f8fe292371a2f245c5c94b4acae160767a20ce7c0ea7723d97691d8eedda9efd1fe2d"}}, "expected": {"result_hex": "fb531a1ed181c732311e56f4b56ed91dcacc0dd6bf1eb4a44be6f87dd7cb1ef9dfb0310f4a79eaaa3f32bf3914d8624e"}, "tags": ["padding-boundary", "length-127", "edge", "nist-cavp"]}
{"id": "sha384-007", "function": "sha384", "input": {"args": {"data_hex": "3bf52cc5ee86b9a0190f390a5c0366a560b557000dbe5115fd9ee11630a62769011575f15881198f227876e8fe685a6939bc8b89fd48a34ec5e71e131462b2886794dffa68ccc6d564733e67ffef25e627c6f4b5460796e3bce67bf58ca6e8e555bc916a8531697ac948b90dc8616f25101db90b50c3d3dbc9e21e42ff387187"}}, "expected": {"result_hex": "12b6cb35eda92ee37356ddee77781a17b3d90e563824a984faffc6fdd1693bd7626039635563cfc3b9a2b00f9c65eefd"}, "tags": ["padding-boundary", "length-128", "edge", "nist-cavp"]}
```

### sha512()

```test-vectors
{"id": "sha512-001", "function": "sha512", "input": {"args": {"data_hex": ""}}, "expected": {"result_hex": "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e"}, "tags": ["empty", "edge", "nist-cavp"]}
{"id": "sha512-002", "function": "sha512", "input": {"args": {"data_hex": "616263"}}, "expected": {"result_hex": "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f"}, "tags": ["abc", "fips-180-4-example"]}
{"id": "sha512-003", "function": "sha512", "input": {"args": {"data_hex": "61626364656667686263646566676869636465666768696a6465666768696a6b65666768696a6b6c666768696a6b6c6d6768696a6b6c6d6e68696a6b6c6d6e6f696a6b6c6d6e6f706a6b6c6d6e6f70716b6c6d6e6f7071726c6d6e6f707172736d6e6f70717273746e6f707172737475"}}, "expected": {"result_hex": "8e959b75dae313da8cf4f72814fc143f8f7779c6eb9f7fa17299aeadb6889018501d289e4900f7e4331b99dec4b5433ac7d329eeb6dd26545e96e55b874be909"}, "tags": ["two-block-896-bit", "fips-180-4-example"]}
{"id": "sha512-004", "function": "sha512", "input": {"args": {"data_hex": "324533e685f1852e358eea8ea8b81c288b3f3beb1f2bc2b8d3fdbac318382e3d7120de30c9c237aa0a34831deb1e5e060a7969cd3a9742ec1e64b354f7eb290cba1c681c66cc7ea994fdf5614f604d1a2718aab581c1c94931b1387e4b7dc73635bf3a7301174075fa70a9227d85d3"}}, "expected": {"result_hex": "3b26c5170729d0814153becb95f1b65cd42f9a6d0649d914e4f69d938b5e9dc041cd0f5c8da0b484d7c7bc7b1bdefb08fe8b1bfedc81109345bc9e9a399feedf"}, "tags": ["padding-boundary", "length-111", "edge", "nist-cavp"]}
{"id": "sha512-005", "function": "sha512", "input": {"args": {"data_hex": "518985977ee21d2bf622a20567124fcbf11c72df805365835ab3c041f4a9cd8a0ad63c9dee1018aa21a9fa3720f47dc48006f1aa3dba544950f87e627f369bc2793ede21223274492cceb77be7eea50e5a509059929a16d33a9f54796cde5770c74bd3ecc25318503f1a41976407aff2"}}, "expected": {"result_hex": "c00926a374cde55b8fbd77f50da1363da19744d3f464e07ce31794c5a61b6f9c85689fa1cfe136553527fd876be91673c2cac2dd157b2defea360851b6d92cf4"}, "tags": ["padding-boundary", "length-112", "edge", "nist-cavp"]}
{"id": "sha512-006", "function": "sha512", "input": {"args": {"data_hex": "c13e6ca3abb893aa5f82c4a8ef754460628af6b75af02168f45b72f8f09e45ed127c203bc7bb80ff0c7bd96f8cc6d8110868eb2cfc01037d8058992a6cf2effcbfe498c842e53a2e68a793867968ba18efc4a78b21cdf6a11e5de821dcabab14921ddb33625d48a13baffad6fe8272dbdf4433bd0f7b813c981269c388f001"}}, "expected": {"result_hex": "6e56f77f6883d0bd4face8b8d557f144661989f66d51b1fe4b8fc7124d66d9d20218616fea1bcf86c08d63bf8f2f21845a3e519083b937e70aa7c358310b5a7c"}, "tags": ["padding-boundary", "length-127", "edge", "nist-cavp"]}
{"id": "sha512-007", "function": "sha512", "input": {"args": {"data_hex": "fd2203e467574e834ab07c9097ae164532f24be1eb5d88f1af7748ceff0d2c67a21f4e4097f9d3bb4e9fbf97186e0db6db0100230a52b453d421f8ab9c9a6043aa3295ea20d2f06a2f37470d8a99075f1b8a8336f6228cf08b5942fc1fb4299c7d2480e8e82bce175540bdfad7752bc95b577f229515394f3ae5cec870a4b2f8"}}, "expected": {"result_hex": "a21b1077d52b27ac545af63b32746c6e3c51cb0cb9f281eb9f3580a6d4996d5c9917d2a6e484627a9d5a06fa1b25327a9d710e027387fc3e07d7c4d14c6086cc"}, "tags": ["padding-boundary", "length-128", "edge", "nist-cavp"]}
```

### sha256_chunked()

```test-vectors
{"id": "sha256-chunked-001", "function": "sha256_chunked", "input": {"args": {"chunks_hex": []}}, "expected": {"result_hex": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"}, "tags": ["zero-chunks", "empty", "edge"]}
{"id": "sha256-chunked-002", "function": "sha256_chunked", "input": {"args": {"chunks_hex": [""]}}, "expected": {"result_hex": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"}, "tags": ["empty-chunk", "empty", "edge"]}
{"id": "sha256-chunked-003", "function": "sha256_chunked", "input": {"args": {"chunks_hex": ["61", "62", "63"]}}, "expected": {"result_hex": "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"}, "tags": ["byte-at-a-time"]}
{"id": "sha256-chunked-004", "function": "sha256_chunked", "input": {"args": {"chunks_hex": ["", "6162", "", "63", ""]}}, "expected": {"result_hex": "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"}, "tags": ["empty-chunks", "edge"]}
{"id": "sha256-chunked-005", "function": "sha256_chunked", "input": {"args": {"chunks_hex": ["61", "62636462636465636465666465666765666768666768696768696a68696a6b696a6b6c6a6b6c6d6b6c6d6e6c6d6e6f6d6e6f706e6f7071"]}}, "expected": {"result_hex": "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"}, "tags": ["split-1"]}
{"id": "sha256-chunked-006", "function": "sha256_chunked", "input": {"args": {"chunks_hex": ["6162636462636465636465666465666765666768666768696768696a68696a6b", "696a6b6c6a6b6c6d6b6c6d6e6c6d6e6f6d6e6f706e6f7071"]}}, "expected": {"result_hex": "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"}, "tags": ["split-mid-block"]}
{"id": "sha256-chunked-007", "function": "sha256_chunked", "input": {"args": {"chunks_hex": ["5a86b737eaea8ee976a0a24da63e7ed7eefad18a101c1211e2b3650c5187c2a8a650547208251f6d4237e661c7bf4c77f335390394c37fa1a9f9be836ac285", "09"]}}, "expected": {"result_hex": "42e61e174fbb3897d6dd6cef3dd2802fe67b331953b06114a65c772859dfc1aa"}, "tags": ["split-at-block-end", "edge"]}
{"id": "sha256-chunked-008", "function": "sha256_chunked", "input": {"args": {"chunks_hex": ["e2f76e97606a872e317439f1a03fcd92e632e5bd4e7cbc4e97f1afc19a16fde9", "2d77cbe546416b51640cddb92af996534dfd81edb17c4424cf1ac4d75aceeb"]}}, "expected": {"result_hex": "18041bd4665083001fba8c5411d2d748e8abbfdcdfd9218cb02b68a78e7d4c23"}, "tags": ["padding-boundary", "edge"]}
{"id": "sha256-chunked-009", "function": "sha256_chunked", "input": {"args": {"chunks_hex": ["61", "62", "63", "64", "62", "63", "64", "65", "63", "64", "65", "66", "64", "65", "66", "67", "65", "66", "67", "68", "66", "67", "68", "69", "67", "68", "69", "6a", "68", "69", "6a", "6b", "69", "6a", "6b", "6c", "6a", "6b", "6c", "6d", "6b", "6c", "6d", "6e", "6c", "6d", "6e", "6f", "6d", "6e", "6f", "70", "6e", "6f", "70", "71"]}}, "expected": {"result_hex": "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"}, "tags": ["byte-at-a-time"]}
```

### sha384_chunked()

```test-vectors
{"id": "sha384-chunked-001", "function": "sha384_chunked", "input": {"args": {"chunks_hex": []}}, "expected": {"result_hex": "38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da274edebfe76f65fbd51ad2f14898b95b"}, "tags": ["zero-chunks", "empty", "edge"]}
{"id": "sha384-chunked-002", "function": "sha384_chunked", "input": {"args": {"chunks_hex": [""]}}, "expected": {"result_hex": "38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da274edebfe76f65fbd51ad2f14898b95b"}, "tags": ["empty-chunk", "empty", "edge"]}
{"id": "sha384-chunked-003", "function": "sha384_chunked", "input": {"args": {"chunks_hex": ["61", "62", "63"]}}, "expected": {"result_hex": "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7"}, "tags": ["byte-at-a-time"]}
{"id": "sha384-chunked-004", "function": "sha384_chunked", "input": {"args": {"chunks_hex": ["", "6162", "", "63", ""]}}, "expected": {"result_hex": "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7"}, "tags": ["empty-chunks", "edge"]}
{"id": "sha384-chunked-005", "function": "sha384_chunked", "input": {"args": {"chunks_hex": ["61", "626364656667686263646566676869636465666768696a6465666768696a6b65666768696a6b6c666768696a6b6c6d6768696a6b6c6d6e68696a6b6c6d6e6f696a6b6c6d6e6f706a6b6c6d6e6f70716b6c6d6e6f7071726c6d6e6f707172736d6e6f70717273746e6f707172737475"]}}, "expected": {"result_hex": "09330c33f71147e83d192fc782cd1b4753111b173b3b05d22fa08086e3b0f712fcc7c71a557e2db966c3e9fa91746039"}, "tags": ["split-1"]}
{"id": "sha384-chunked-006", "function": "sha384_chunked", "input": {"args": {"chunks_hex": ["61626364656667686263646566676869636465666768696a6465666768696a6b65666768696a6b6c666768696a6b6c6d6768696a6b6c6d6e68696a6b6c6d6e6f", "696a6b6c6d6e6f706a6b6c6d6e6f70716b6c6d6e6f7071726c6d6e6f707172736d6e6f70717273746e6f707172737475"]}}, "expected": {"result_hex": "09330c33f71147e83d192fc782cd1b4753111b173b3b05d22fa08086e3b0f712fcc7c71a557e2db966c3e9fa91746039"}, "tags": ["split-mid-block"]}
{"id": "sha384-chunked-007", "function": "sha384_chunked", "input": {"args": {"chunks_hex": ["3bf52cc5ee86b9a0190f390a5c0366a560b557000dbe5115fd9ee11630a62769011575f15881198f227876e8fe685a6939bc8b89fd48a34ec5e71e131462b2886794dffa68ccc6d564733e67ffef25e627c6f4b5460796e3bce67bf58ca6e8e555bc916a8531697ac948b90dc8616f25101db90b50c3d3dbc9e21e42ff3871", "87"]}}, "expected": {"result_hex": "12b6cb35eda92ee37356ddee77781a17b3d90e563824a984faffc6fdd1693bd7626039635563cfc3b9a2b00f9c65eefd"}, "tags": ["split-at-block-end", "edge"]}
{"id": "sha384-chunked-008", "function": "sha384_chunked", "input": {"args": {"chunks_hex": ["dbed7612448d46cbe0a384d1c93233f02ffd1c984ba765299518656d3723b766c1658d4b1e7047cdc729459e366ef9349efc40cbd990f2a9a24db7a5045e1dea", "12dce8f9d9f2aaed933f93031e7b8959ac5e7bf6bbbdf30b48f7eb783f8fe292371a2f245c5c94b4acae160767a20ce7c0ea7723d97691d8eedda9efd1fe2d"]}}, "expected": {"result_hex": "fb531a1ed181c732311e56f4b56ed91dcacc0dd6bf1eb4a44be6f87dd7cb1ef9dfb0310f4a79eaaa3f32bf3914d8624e"}, "tags": ["padding-boundary", "edge"]}
{"id": "sha384-chunked-009", "function": "sha384_chunked", "input": {"args": {"chunks_hex": ["61", "62", "63", "64", "65", "66", "67", "68", "62", "63", "64", "65", "66", "67", "68", "69", "63", "64", "65", "66", "67", "68", "69", "6a", "64", "65", "66", "67", "68", "69", "6a", "6b", "65", "66", "67", "68", "69", "6a", "6b", "6c", "66", "67", "68", "69", "6a", "6b", "6c", "6d", "67", "68", "69", "6a", "6b", "6c", "6d", "6e", "68", "69", "6a", "6b", "6c", "6d", "6e", "6f", "69", "6a", "6b", "6c", "6d", "6e", "6f", "70", "6a", "6b", "6c", "6d", "6e", "6f", "70", "71", "6b", "6c", "6d", "6e", "6f", "70", "71", "72", "6c", "6d", "6e", "6f", "70", "71", "72", "73", "6d", "6e", "6f", "70", "71", "72", "73", "74", "6e", "6f", "70", "71", "72", "73", "74", "75"]}}, "expected": {"result_hex": "09330c33f71147e83d192fc782cd1b4753111b173b3b05d22fa08086e3b0f712fcc7c71a557e2db966c3e9fa91746039"}, "tags": ["byte-at-a-time"]}
```

### sha512_chunked()

```test-vectors
{"id": "sha512-chunked-001", "function": "sha512_chunked", "input": {"args": {"chunks_hex": []}}, "expected": {"result_hex": "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e"}, "tags": ["zero-chunks", "empty", "edge"]}
{"id": "sha512-chunked-002", "function": "sha512_chunked", "input": {"args": {"chunks_hex": [""]}}, "expected": {"result_hex": "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e"}, "tags": ["empty-chunk", "empty", "edge"]}
{"id": "sha512-chunked-003", "function": "sha512_chunked", "input": {"args": {"chunks_hex": ["61", "62", "63"]}}, "expected": {"result_hex": "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f"}, "tags": ["byte-at-a-time"]}
{"id": "sha512-chunked-004", "function": "sha512_chunked", "input": {"args": {"chunks_hex": ["", "6162", "", "63", ""]}}, "expected": {"result_hex": "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f"}, "tags": ["empty-chunks", "edge"]}
{"id": "sha512-chunked-005", "function": "sha512_chunked", "input": {"args": {"chunks_hex": ["61", "626364656667686263646566676869636465666768696a6465666768696a6b65666768696a6b6c666768696a6b6c6d6768696a6b6c6d6e68696a6b6c6d6e6f696a6b6c6d6e6f706a6b6c6d6e6f70716b6c6d6e6f7071726c6d6e6f707172736d6e6f70717273746e6f707172737475"]}}, "expected": {"result_hex": "8e959b75dae313da8cf4f72814fc143f8f7779c6eb9f7fa17299aeadb6889018501d289e4900f7e4331b99dec4b5433ac7d329eeb6dd26545e96e55b874be909"}, "tags": ["split-1"]}
{"id": "sha512-chunked-006", "function": "sha512_chunked", "input": {"args": {"chunks_hex": ["61626364656667686263646566676869636465666768696a6465666768696a6b65666768696a6b6c666768696a6b6c6d6768696a6b6c6d6e68696a6b6c6d6e6f", "696a6b6c6d6e6f706a6b6c6d6e6f70716b6c6d6e6f7071726c6d6e6f707172736d6e6f70717273746e6f707172737475"]}}, "expected": {"result_hex": "8e959b75dae313da8cf4f72814fc143f8f7779c6eb9f7fa17299aeadb6889018501d289e4900f7e4331b99dec4b5433ac7d329eeb6dd26545e96e55b874be909"}, "tags": ["split-mid-block"]}
{"id": "sha512-chunked-007", "function": "sha512_chunked", "input": {"args": {"chunks_hex": ["fd2203e467574e834ab07c9097ae164532f24be1eb5d88f1af7748ceff0d2c67a21f4e4097f9d3bb4e9fbf97186e0db6db0100230a52b453d421f8ab9c9a6043aa3295ea20d2f06a2f37470d8a99075f1b8a8336f6228cf08b5942fc1fb4299c7d2480e8e82bce175540bdfad7752bc95b577f229515394f3ae5cec870a4b2", "f8"]}}, "expected": {"result_hex": "a21b1077d52b27ac545af63b32746c6e3c51cb0cb9f281eb9f3580a6d4996d5c9917d2a6e484627a9d5a06fa1b25327a9d710e027387fc3e07d7c4d14c6086cc"}, "tags": ["split-at-block-end", "edge"]}
{"id": "sha512-chunked-008", "function": "sha512_chunked", "input": {"args": {"chunks_hex": ["c13e6ca3abb893aa5f82c4a8ef754460628af6b75af02168f45b72f8f09e45ed127c203bc7bb80ff0c7bd96f8cc6d8110868eb2cfc01037d8058992a6cf2effc", "bfe498c842e53a2e68a793867968ba18efc4a78b21cdf6a11e5de821dcabab14921ddb33625d48a13baffad6fe8272dbdf4433bd0f7b813c981269c388f001"]}}, "expected": {"result_hex": "6e56f77f6883d0bd4face8b8d557f144661989f66d51b1fe4b8fc7124d66d9d20218616fea1bcf86c08d63bf8f2f21845a3e519083b937e70aa7c358310b5a7c"}, "tags": ["padding-boundary", "edge"]}
{"id": "sha512-chunked-009", "function": "sha512_chunked", "input": {"args": {"chunks_hex": ["61", "62", "63", "64", "65", "66", "67", "68", "62", "63", "64", "65", "66", "67", "68", "69", "63", "64", "65", "66", "67", "68", "69", "6a", "64", "65", "66", "67", "68", "69", "6a", "6b", "65", "66", "67", "68", "69", "6a", "6b", "6c", "66", "67", "68", "69", "6a", "6b", "6c", "6d", "67", "68", "69", "6a", "6b", "6c", "6d", "6e", "68", "69", "6a", "6b", "6c", "6d", "6e", "6f", "69", "6a", "6b", "6c", "6d", "6e", "6f", "70", "6a", "6b", "6c", "6d", "6e", "6f", "70", "71", "6b", "6c", "6d", "6e", "6f", "70", "71", "72", "6c", "6d", "6e", "6f", "70", "71", "72", "73", "6d", "6e", "6f", "70", "71", "72", "73", "74", "6e", "6f", "70", "71", "72", "73", "74", "75"]}}, "expected": {"result_hex": "8e959b75dae313da8cf4f72814fc143f8f7779c6eb9f7fa17299aeadb6889018501d289e4900f7e4331b99dec4b5433ac7d329eeb6dd26545e96e55b874be909"}, "tags": ["byte-at-a-time"]}
```

## Edge Cases

- **No error vectors.** Every byte string is a valid message, so SHA-2 has no error case. The vectors tagged `edge` stand in for error vectors: the empty message, zero chunks, empty chunks, and the padding boundaries.
- **Empty message.** `sha256("")` is `e3b0c442...b855`. The padded message is one block: `0x80`, zeros, and a zero length field.
- **Padding boundary.** A message whose last block has at most `block_size - length_field_size - 1` bytes (55 for SHA-256, 111 for SHA-384/512) pads within that block. One more byte (56 or 112) needs an extra block of padding. A message that exactly fills a block (64 or 128) is followed by a whole padding block.
- **Chunks that straddle a block.** A chunk that completes a pending partial block and continues into the next MUST be split correctly; the `split-at-block-end` and `split-mid-block` vectors cover this.
- **Zero chunks and empty chunks.** `init` followed directly by `finalize` is the empty-message digest. Empty chunks anywhere in the sequence MUST be no-ops.
- **Use after finalize.** Not expressible as a vector: the behaviour is the loud failure in the `finalize()` contract.
- **Very long messages.** The length field is 64 bits (SHA-256) or 128 bits (SHA-384/512) of bit count. A byte count held in a 64-bit integer covers every practical message for SHA-256 up to 2^61 - 1 bytes; SHA-384/512 MUST still write a 16-byte length field, with the upper 8 bytes holding `byte_count >> 61`.

## Implementation Notes

- **The million-'a' vector.** RFC 6234 (TEST3) also publishes the digest of 1,000,000 repetitions of `"a"`. Its message exceeds the 10,000-character string limit of the vector format, so it is not a vector; a test suite SHOULD generate the message and check these digests:
  - SHA-256: `cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0`
  - SHA-384: `9d0e1809716474cb086e834e310a4a1ced149e9c00f248527972cec5704c2a5b07b8b3dc38ecc4ebae97ddd87f3d8985`
  - SHA-512: `e718483d0ce769644e2e42c7bc15b4638e1f98b13b2044285632a803afa973ebde0ff244877ea60a4cb0432ce577c31beb009c5c2c49aa2e4eadb217ad8cc09b`
- **Endianness.** Message words, the length field and the output are all big-endian. On little-endian hardware, reading the block as native words is the most common bug; the `abc` vector catches it.
- **Overflow.** Every addition is modulo 2^32 or 2^64. In languages that trap on overflow, use explicit wrapping addition.
- **Rotation, not shift.** `ROTR` and `SHR` differ only in what fills the vacated bits; mixing them up passes no vector.
- **Buffering.** Process full blocks straight from the caller's chunk when the pending buffer is empty, and copy only the remainder; this avoids a copy per byte.
- **Truncation for SHA-384.** SHA-384 uses the SHA-512 compression function with `H384` and outputs only `H[0..5]`. Truncating a SHA-512 digest (computed from `H512`) to 48 bytes gives a different value, which is not SHA-384.
- **Timing.** SHA-2 has no data-dependent branches or table lookups, so a straightforward implementation does not leak its input through timing. Keep it that way: no early exits on input content.

## References

- [FIPS 180-4 -- Secure Hash Standard (SHS)](https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.180-4.pdf) -- the governing standard: padding (5.1), parsing (5.2), initial values (5.3), constants (4.2) and computation (6.2, 6.4, 6.5).
- [RFC 6234 -- US Secure Hash Algorithms (SHA and SHA-based HMAC and HKDF)](https://www.rfc-editor.org/rfc/rfc6234.html) -- reference C code and the test digests used here (section 8.5).
- [NIST CAVP Secure Hashing test vectors](https://csrc.nist.gov/projects/cryptographic-algorithm-validation-program/secure-hashing) -- the `ShortMsg` byte-oriented response files that supply the empty and padding-boundary vectors.
