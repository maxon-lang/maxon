+++
name = "ecdsa-sign-p256"
version = "1"
title = "Deterministic ECDSA Signing on P-256 with SHA-256 (RFC 6979)"
status = "draft"
keywords = ["ecdsa", "signature", "rfc6979", "deterministic", "p-256", "sha-256", "constant-time", "cryptography"]
difficulty = "hard"

[api]
functions = ["sign"]
types = ["EcdsaError"]

[references]
items = [
  { label = "RFC 6979", url = "https://www.rfc-editor.org/rfc/rfc6979.html" },
  { label = "FIPS 186-5", url = "https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.186-5.pdf" },
  { label = "NIST SP 800-186 (P-256 domain parameters)", url = "https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-186.pdf" },
  { label = "RFC 3279 section 2.2.3 (ECDSA-Sig-Value)", url = "https://www.rfc-editor.org/rfc/rfc3279.html#section-2.2.3" },
  { label = "RFC 8446 section 4.4.3", url = "https://www.rfc-editor.org/rfc/rfc8446.html#section-4.4.3" },
  { label = "NIST CAVP ECDSA test vectors", url = "https://csrc.nist.gov/projects/cryptographic-algorithm-validation-program/digital-signatures" },
]

[test-vectors]
inline = true
external = []
+++

## Overview

This spec defines ECDSA signature generation on NIST P-256 with SHA-256, with the per-signature nonce `k` derived deterministically from the private key and the message hash as RFC 6979 section 3.2 specifies, as one function:

- **sign** -- returns the DER-encoded ECDSA signature of a message under a P-256 private key.

Deterministic nonces remove the need for a random source at signing time and the key-recovery failures that come with a weak or repeated random `k`. The output is a standard ECDSA signature: any FIPS 186-5 verifier (the `ecdsa-verify` spec) accepts it. A TLS 1.3 server with a P-256 certificate uses it to produce `CertificateVerify` with the `ecdsa_secp256r1_sha256` scheme.

**Out of scope:** verification (the `ecdsa-verify` spec), other curves and hashes, signing a precomputed digest, randomized or hedged nonces (RFC 6979 section 3.6 additional data), low-S normalization, raw `r || s` output, and private keys in PKCS#8 or SEC 1 `ECPrivateKey` containers.

## Types

### EcdsaError

```pseudocode
type EcdsaError =
  | InvalidPrivateKey   // the private key is not 32 bytes, or its value is 0 or at least n
```

### Curve P-256

`y^2 = x^3 - 3x + b` over `GF(p)`, base point `G` of prime order `n`, cofactor 1 (SP 800-186 section 3.2.1.3):

```pseudocode
p  = 0xffffffff00000001000000000000000000000000ffffffffffffffffffffffff
b  = 0x5ac635d8aa3a93e7b3ebbd55769886bc651d06b0cc53b0f63bce3c3e27d2604b
Gx = 0x6b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c296
Gy = 0x4fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5
n  = 0xffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551
```

### Encodings

| Value | Encoding |
|---|---|
| Private key `x` | Exactly 32 bytes, the big-endian integer `x`; valid when `1 <= x <= n - 1` |
| Signature | DER `ECDSA-Sig-Value ::= SEQUENCE { r INTEGER, s INTEGER }` (RFC 3279 section 2.2.3): each INTEGER minimal, with a `00` prefix exactly when its top bit is set; 8 to 72 bytes in all |

`int2octets(v)` is `v` as 32 big-endian bytes. `bits2octets(h) = int2octets(int(h) mod n)`, where `int(h)` reads the 32-byte hash as a big-endian integer (SHA-256 and `n` both have 256 bits, so `bits2int` needs no shift).

## Functions

### sign

```pseudocode
sign(private_key: Bytes, message: Bytes) -> Bytes | EcdsaError
```

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `private_key` | Bytes | yes | The private key, 32 bytes big-endian |
| `message` | Bytes | yes | The message, any length; hashed by this function with SHA-256 |

**Returns:** The DER-encoded signature, or `InvalidPrivateKey`.

**Algorithm:**

1. If `length(private_key) != 32`, return `InvalidPrivateKey`. `x = int(private_key)`. If `x == 0` or `x >= n`, return `InvalidPrivateKey`.
2. `h1 = SHA-256(message)`; `e = int(h1)`.
3. Nonce generation, RFC 6979 section 3.2, with `HMAC_K` = HMAC-SHA-256 keyed with `K`:
   1. `V = 0x01` repeated 32 times; `K = 0x00` repeated 32 times.
   2. `K = HMAC_K(V || 0x00 || int2octets(x) || bits2octets(h1))`; `V = HMAC_K(V)`.
   3. `K = HMAC_K(V || 0x01 || int2octets(x) || bits2octets(h1))`; `V = HMAC_K(V)`.
   4. Loop: `V = HMAC_K(V)`; `k = int(V)`. If `1 <= k <= n - 1`, try `k` in step 4; if step 4 rejects it, or `k` is out of range, set `K = HMAC_K(V || 0x00)`, `V = HMAC_K(V)` and repeat this loop.
4. `(x1, y1) = k * G`; `r = x1 mod n`; `s = k^(-1) * (e + x * r) mod n`. If `r == 0` or `s == 0`, reject `k` (back to step 3.4).
5. Return `DER(SEQUENCE { INTEGER r, INTEGER s })`.

Since each HMAC-SHA-256 output is 256 bits, one `V` gives a whole candidate `k`; the RFC's inner loop that concatenates outputs until `qlen` bits runs once. The retry branches have probability about `2^-32` and no vector reaches them.

## Behavioral Contracts

### sign()

**MUST:**

1. MUST return exactly the RFC 6979 signature: the nonce from section 3.2 with HMAC-SHA-256, and `(r, s)` as computed, DER-encoded. The output is a function of `private_key` and `message` only.
2. MUST NOT normalize `s` to the low half: when `s > n / 2` the result keeps `s`, not `n - s`. (The RFC 6979 `sample` vector has a high `s`.)
3. MUST encode each integer minimally: no leading zero bytes except one `00` before a value whose top bit is set. Short `r` or `s` values give a shorter signature.
4. MUST return `InvalidPrivateKey` for a key that is not 32 bytes or whose value is 0 or at least `n`; it MUST NOT reduce the key modulo `n`.
5. **Constant time.** The computation of `k * G`, `k^(-1) mod n`, `x * r` and the sum MUST NOT branch on or index memory by `x`, `k` or intermediate values: a fixed-window or ladder scalar multiplication over all 256 bits with constant-time table lookups, and inversion as `k^(n - 2) mod n` with a fixed addition chain or a constant-time algorithm. The HMAC computations are constant time by construction. The message and its hash are not secret.
6. MUST produce signatures that `ecdsa-verify` accepts with the matching public key: `verify("p256", x * G, message, sign(x, message)) == true`.

**SHOULD:**

1. SHOULD verify the signature, or recompute it, before returning it when the platform is exposed to fault attacks: a faulty `k * G` can leak the key.
2. SHOULD overwrite `k`, `K`, `V` and copies of `x` before returning.

## Test Vectors

`private_key_hex` and `message_hex` are lowercase hex. Expected output is the DER signature as `result_hex`.

### Sources

- `rfc6979-a-2-5` vectors are the two P-256 signatures with SHA-256 of RFC 6979 appendix A.2.5, for the messages `sample` and `test`, with the published `r` and `s` DER-encoded. The published nonces `k` were reproduced too.
- `computed` vectors have no published signature; they were computed with two independent RFC 6979 implementations, which agreed, and each signature was verified:
  `rfc6979-a-2-5-key` vectors sign other messages under the appendix A.2.5 key (empty, one zero byte, `abc`, a 1024-byte message of the bytes 0 to 255 four times, and the messages `short-integer-5` and `short-integer-396`, whose `r` and `s` respectively are 31-byte values: that `r` has its top bit set and is encoded as `00` and 31 bytes, that `s` as 31 bytes).
  `nist-cavp-siggen-inputs` vectors take the private key `d` and message `Msg` of the 15 `[P-256,SHA-256]` records of `SigGen.txt` in NIST CAVP `186-4ecdsatestvectors.zip`; the published `R` and `S` used a random `k` and are not the expected values here, but each `d` was checked against the published public key.
  `edge-private-key` vectors sign `sample` with private keys 1, 2 and `n - 1`.
- The error vectors are `constructed`, signing `sample`.
- The tag `high-s` marks every vector whose `s` is above `n / 2`.

### sign()

```test-vectors
{"id": "sign-001", "function": "sign", "input": {"args": {"private_key_hex": "c9afa9d845ba75166b5c215767b1d6934e50c3db36e89b127b8a622b120f6721", "message_hex": "73616d706c65"}}, "expected": {"result_hex": "3046022100efd48b2aacb6a8fd1140dd9cd45e81d69d2c877b56aaf991c34d0ea84eaf3716022100f7cb1c942d657c41d436c7a1b6e29f65f3e900dbb9aff4064dc4ab2f843acda8"}, "tags": ["rfc6979", "rfc6979-a-2-5", "message-sample", "high-s"]}
{"id": "sign-002", "function": "sign", "input": {"args": {"private_key_hex": "c9afa9d845ba75166b5c215767b1d6934e50c3db36e89b127b8a622b120f6721", "message_hex": "74657374"}}, "expected": {"result_hex": "3045022100f1abb023518351cd71d881567b1ea663ed3efcf6c5132b354f28d3b0b7d383670220019f4113742a2b14bd25926b49c649155f267e60d3814b4c0cc84250e46f0083"}, "tags": ["rfc6979", "rfc6979-a-2-5", "message-test"]}
{"id": "sign-003", "function": "sign", "input": {"args": {"private_key_hex": "c9afa9d845ba75166b5c215767b1d6934e50c3db36e89b127b8a622b120f6721", "message_hex": ""}}, "expected": {"result_hex": "304502200338197042a13192bec427db63c8d2dece6a08dbcc3d5181a9983e62032b023002210098feda6c583d409233023308d3848aa21b64381d85ee6e1c090a5d11fb7be0c7"}, "tags": ["computed", "rfc6979-a-2-5-key", "empty-message", "high-s"]}
{"id": "sign-004", "function": "sign", "input": {"args": {"private_key_hex": "c9afa9d845ba75166b5c215767b1d6934e50c3db36e89b127b8a622b120f6721", "message_hex": "00"}}, "expected": {"result_hex": "304502200971976013635f35a5c258cb94f1249670b5044f5fe847b216099cc6b7ebd88a022100fc8c0ae0a2a3217f5bdaf7264bf3d155351f1e0fc77bb695bd47eb63c00bf03e"}, "tags": ["computed", "rfc6979-a-2-5-key", "one-zero-byte", "high-s"]}
{"id": "sign-005", "function": "sign", "input": {"args": {"private_key_hex": "c9afa9d845ba75166b5c215767b1d6934e50c3db36e89b127b8a622b120f6721", "message_hex": "616263"}}, "expected": {"result_hex": "3046022100d0720b69059e029f2001100cdb2eee9914d7d685e03584313f867bc963740a42022100e2ac01dcb1e1c5f1cb1954f2fd8e408f0bc6c82c59d94ff46067187679ac14e7"}, "tags": ["computed", "rfc6979-a-2-5-key", "abc", "high-s"]}
{"id": "sign-006", "function": "sign", "input": {"args": {"private_key_hex": "c9afa9d845ba75166b5c215767b1d6934e50c3db36e89b127b8a622b120f6721", "message_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff"}}, "expected": {"result_hex": "304402202de860ad8fd2dc9cda12d10102dc4eb744c23fc37207ec2b0b00ca2e65d33c4b0220556b36ad92ec4dcdadd6f413c8716452697644ce6bbeae581140b4390eaeb0fb"}, "tags": ["computed", "rfc6979-a-2-5-key", "long-message"]}
{"id": "sign-007", "function": "sign", "input": {"args": {"private_key_hex": "519b423d715f8b581f4fa8ee59f4771a5b44c8130b4e3eacca54a56dda72b464", "message_hex": "5905238877c77421f73e43ee3da6f2d9e2ccad5fc942dcec0cbd25482935faaf416983fe165b1a045ee2bcd2e6dca3bdf46c4310a7461f9a37960ca672d3feb5473e253605fb1ddfd28065b53cb5858a8ad28175bf9bd386a5e471ea7a65c17cc934a9d791e91491eb3754d03799790fe2d308d16146d5c9b0d0debd97d79ce8"}}, "expected": {"result_hex": "3045022100ad13dc37c7dbf41d1e3397f7d8a340acb4e0a94c2c5dc9404a4c1cde3726f59e0220122700937f5ca31e0a8b282c4ece8cae768629eedfae3b522f618b56e408fc95"}, "tags": ["computed", "nist-cavp-siggen-inputs", "record-0"]}
{"id": "sign-008", "function": "sign", "input": {"args": {"private_key_hex": "0f56db78ca460b055c500064824bed999a25aaf48ebb519ac201537b85479813", "message_hex": "c35e2f092553c55772926bdbe87c9796827d17024dbb9233a545366e2e5987dd344deb72df987144b8c6c43bc41b654b94cc856e16b96d7a821c8ec039b503e3d86728c494a967d83011a0e090b5d54cd47f4e366c0912bc808fbb2ea96efac88fb3ebec9342738e225f7c7c2b011ce375b56621a20642b4d36e060db4524af1"}}, "expected": {"result_hex": "3046022100f645c40dd498d3c49f066d2d35eb016f65149d9bdee9f57e01323db042027a3f022100a12ffc3299707d0111ced366fd2fd6a34ea932a9c63c6c25bdca275b8b5c0f29"}, "tags": ["computed", "nist-cavp-siggen-inputs", "record-1", "high-s"]}
{"id": "sign-009", "function": "sign", "input": {"args": {"private_key_hex": "e283871239837e13b95f789e6e1af63bf61c918c992e62bca040d64cad1fc2ef", "message_hex": "3c054e333a94259c36af09ab5b4ff9beb3492f8d5b4282d16801daccb29f70fe61a0b37ffef5c04cd1b70e85b1f549a1c4dc672985e50f43ea037efa9964f096b5f62f7ffdf8d6bfb2cc859558f5a393cb949dbd48f269343b5263dcdb9c556eca074f2e98e6d94c2c29a677afaf806edf79b15a3fcd46e7067b7669f83188ee"}}, "expected": {"result_hex": "3045022100fc6c5e2ebe126981c406b55000d492207f6026a91ad08415d964c489856872f8022076e2da88317fec3f07e63ab5687109d14d52c7235731999bd9c4f36811a3f7f5"}, "tags": ["computed", "nist-cavp-siggen-inputs", "record-2"]}
{"id": "sign-010", "function": "sign", "input": {"args": {"private_key_hex": "a3d2d3b7596f6592ce98b4bfe10d41837f10027a90d7bb75349490018cf72d07", "message_hex": "0989122410d522af64ceb07da2c865219046b4c3d9d99b01278c07ff63eaf1039cb787ae9e2dd46436cc0415f280c562bebb83a23e639e476a02ec8cff7ea06cd12c86dcc3adefbf1a9e9a9b6646c7599ec631b0da9a60debeb9b3e19324977f3b4f36892c8a38671c8e1cc8e50fcd50f9e51deaf98272f9266fc702e4e57c30"}}, "expected": {"result_hex": "3045022100baa788d513c6de4c9dd95c109582e39c8d907a73f3e3dd9d61940d4d36c8e7b102206e06b3cf7c228eb7ec571af8995367df6978440a83d18b6c54af32ec65fe307e"}, "tags": ["computed", "nist-cavp-siggen-inputs", "record-3"]}
{"id": "sign-011", "function": "sign", "input": {"args": {"private_key_hex": "53a0e8a8fe93db01e7ae94e1a9882a102ebd079b3a535827d583626c272d280d", "message_hex": "dc66e39f9bbfd9865318531ffe9207f934fa615a5b285708a5e9c46b7775150e818d7f24d2a123df3672fff2094e3fd3df6fbe259e3989dd5edfcccbe7d45e26a775a5c4329a084f057c42c13f3248e3fd6f0c76678f890f513c32292dd306eaa84a59abe34b16cb5e38d0e885525d10336ca443e1682aa04a7af832b0eee4e7"}}, "expected": {"result_hex": "3045022052a8658dcccdc4897607319b5580e6895e0e904192cc80a3517c4ad1d270dbc6022100f6c821b895eec2618871d8dab9d22934fa148e28399975cffc8fd562fafba887"}, "tags": ["computed", "nist-cavp-siggen-inputs", "record-4", "high-s"]}
{"id": "sign-012", "function": "sign", "input": {"args": {"private_key_hex": "4af107e8e2194c830ffb712a65511bc9186a133007855b49ab4b3833aefc4a1d", "message_hex": "600974e7d8c5508e2c1aab0783ad0d7c4494ab2b4da265c2fe496421c4df238b0be25f25659157c8a225fb03953607f7df996acfd402f147e37aee2f1693e3bf1c35eab3ae360a2bd91d04622ea47f83d863d2dfecb618e8b8bdc39e17d15d672eee03bb4ce2cc5cf6b217e5faf3f336fdd87d972d3a8b8a593ba85955cc9d71"}}, "expected": {"result_hex": "3045022032832edb70065f6f8a83ec49beb7067ebc4a4d87ee296b00e4f70c1d77b629b1022100a61ee4fb323c2d8c0be8a08778c12a979008b672427e4f45021a54146f5d47cf"}, "tags": ["computed", "nist-cavp-siggen-inputs", "record-5", "high-s"]}
{"id": "sign-013", "function": "sign", "input": {"args": {"private_key_hex": "78dfaa09f1076850b3e206e477494cddcfb822aaa0128475053592c48ebaf4ab", "message_hex": "dfa6cb9b39adda6c74cc8b2a8b53a12c499ab9dee01b4123642b4f11af336a91a5c9ce0520eb2395a6190ecbf6169c4cba81941de8e76c9c908eb843b98ce95e0da29c5d4388040264e05e07030a577cc5d176387154eabae2af52a83e85c61c7c61da930c9b19e45d7e34c8516dc3c238fddd6e450a77455d534c48a152010b"}}, "expected": {"result_hex": "3046022100bab2e8a13db64f7281039ee54b1874edb92c37111c3e6719ff7477eedcfc9b91022100916eab132ba8eb74e2d44bac613f469b8061c988d796e1ac19d14e7af358fe39"}, "tags": ["computed", "nist-cavp-siggen-inputs", "record-6", "high-s"]}
{"id": "sign-014", "function": "sign", "input": {"args": {"private_key_hex": "80e692e3eb9fcd8c7d44e7de9f7a5952686407f90025a1d87e52c7096a62618a", "message_hex": "51d2547cbff92431174aa7fc7302139519d98071c755ff1c92e4694b58587ea560f72f32fc6dd4dee7d22bb7387381d0256e2862d0644cdf2c277c5d740fa089830eb52bf79d1e75b8596ecf0ea58a0b9df61e0c9754bfcd62efab6ea1bd216bf181c5593da79f10135a9bc6e164f1854bc8859734341aad237ba29a81a3fc8b"}}, "expected": {"result_hex": "304502210091770d9e7c068580b7c39477973be8ce520ef2822d13a8538b2765dbeed82e7702203201c3a44323b2aa2c621f6b2e874a9aed15b1616d1435339b68a8e9695abfda"}, "tags": ["computed", "nist-cavp-siggen-inputs", "record-7"]}
{"id": "sign-015", "function": "sign", "input": {"args": {"private_key_hex": "5e666c0db0214c3b627a8e48541cc84a8b6fd15f300da4dff5d18aec6c55b881", "message_hex": "558c2ac13026402bad4a0a83ebc9468e50f7ffab06d6f981e5db1d082098065bcff6f21a7a74558b1e8612914b8b5a0aa28ed5b574c36ac4ea5868432a62bb8ef0695d27c1e3ceaf75c7b251c65ddb268696f07c16d2767973d85beb443f211e6445e7fe5d46f0dce70d58a4cd9fe70688c035688ea8c6baec65a5fc7e2c93e8"}}, "expected": {"result_hex": "30460221009dd32644cec2c742ea7cc235202af602b3e7985c9b624c225cce751d9c2468de0221009a8b5896cdba7df5a41cacc6f2b763cd9758ce684066f1ef5626340a7589dda5"}, "tags": ["computed", "nist-cavp-siggen-inputs", "record-8", "high-s"]}
{"id": "sign-016", "function": "sign", "input": {"args": {"private_key_hex": "f73f455271c877c4d5334627e37c278f68d143014b0a05aa62f308b2101c5308", "message_hex": "4d55c99ef6bd54621662c3d110c3cb627c03d6311393b264ab97b90a4b15214a5593ba2510a53d63fb34be251facb697c973e11b665cb7920f1684b0031b4dd370cb927ca7168b0bf8ad285e05e9e31e34bc24024739fdc10b78586f29eff94412034e3b606ed850ec2c1900e8e68151fc4aee5adebb066eb6da4eaa5681378e"}}, "expected": {"result_hex": "3045022100d0bee6ca556dda6fc4cc0da6d8da6a8d86659f628373543dc35317813b290c5902202ce57cd74c26b53fa0bab84aa81f1b973da0ef611b79627b90eac48b9a93652c"}, "tags": ["computed", "nist-cavp-siggen-inputs", "record-9"]}
{"id": "sign-017", "function": "sign", "input": {"args": {"private_key_hex": "b20d705d9bd7c2b8dc60393a5357f632990e599a0975573ac67fd89b49187906", "message_hex": "f8248ad47d97c18c984f1f5c10950dc1404713c56b6ea397e01e6dd925e903b4fadfe2c9e877169e71ce3c7fe5ce70ee4255d9cdc26f6943bf48687874de64f6cf30a012512e787b88059bbf561162bdcc23a3742c835ac144cc14167b1bd6727e940540a9c99f3cbb41fb1dcb00d76dda04995847c657f4c19d303eb09eb48a"}}, "expected": {"result_hex": "304402200c6478e9be5585482fca6dd26ff541a2c7bdfaf90e8629981505fdc6048101ff02204f25491c47bb37c6e161b6543eb456f3cad47142a581499717891908613d0e43"}, "tags": ["computed", "nist-cavp-siggen-inputs", "record-10"]}
{"id": "sign-018", "function": "sign", "input": {"args": {"private_key_hex": "d4234bebfbc821050341a37e1240efe5e33763cbbb2ef76a1c79e24724e5a5e7", "message_hex": "3b6ee2425940b3d240d35b97b6dcd61ed3423d8e71a0ada35d47b322d17b35ea0472f35edd1d252f87b8b65ef4b716669fc9ac28b00d34a9d66ad118c9d94e7f46d0b4f6c2b2d339fd6bcd351241a387cc82609057048c12c4ec3d85c661975c45b300cb96930d89370a327c98b67defaa89497aa8ef994c77f1130f752f94a4"}}, "expected": {"result_hex": "3046022100ac128c62638003b1193e0cc943f7c0eab16953e754245a0546f192873d2f19b7022100fbf1e01938540f48fb57baa0c3ce4f831f977d09aff95b830cf47f06d8d3aeaa"}, "tags": ["computed", "nist-cavp-siggen-inputs", "record-11", "high-s"]}
{"id": "sign-019", "function": "sign", "input": {"args": {"private_key_hex": "b58f5211dff440626bb56d0ad483193d606cf21f36d9830543327292f4d25d8c", "message_hex": "c5204b81ec0a4df5b7e9fda3dc245f98082ae7f4efe81998dcaa286bd4507ca840a53d21b01e904f55e38f78c3757d5a5a4a44b1d5d4e480be3afb5b394a5d2840af42b1b4083d40afbfe22d702f370d32dbfd392e128ea4724d66a3701da41ae2f03bb4d91bb946c7969404cb544f71eb7a49eb4c4ec55799bda1eb545143a7"}}, "expected": {"result_hex": "3044022054b75b219b2d9f9494d0c70248aac84710b13adc2f29b25799ce7e2dc367fbcc0220244854387f8bf7101f400582742f3c5172678e3bf8ad0a3cf442cf28597c0f3a"}, "tags": ["computed", "nist-cavp-siggen-inputs", "record-12"]}
{"id": "sign-020", "function": "sign", "input": {"args": {"private_key_hex": "54c066711cdb061eda07e5275f7e95a9962c6764b84f6f1f3ab5a588e0a2afb1", "message_hex": "72e81fe221fb402148d8b7ab03549f1180bcc03d41ca59d7653801f0ba853add1f6d29edd7f9abc621b2d548f8dbf8979bd16608d2d8fc3260b4ebc0dd42482481d548c7075711b5759649c41f439fad69954956c9326841ea6492956829f9e0dc789f73633b40f6ac77bcae6dfc7930cfe89e526d1684365c5b0be2437fdb01"}}, "expected": {"result_hex": "3046022100c32226664d9c0d84ee92451e4432473a2d99aa662c19085ce33462dab4c8ea6402210083550d55233f0faeaf6c51a57801ba07f7728eacb9cf3382752a19965dc66bd5"}, "tags": ["computed", "nist-cavp-siggen-inputs", "record-13", "high-s"]}
{"id": "sign-021", "function": "sign", "input": {"args": {"private_key_hex": "34fa4682bf6cb5b16783adcd18f0e6879b92185f76d7c920409f904f522db4b1", "message_hex": "21188c3edd5de088dacc1076b9e1bcecd79de1003c2414c3866173054dc82dde85169baa77993adb20c269f60a5226111828578bcc7c29e6e8d2dae81806152c8ba0c6ada1986a1983ebeec1473a73a04795b6319d48662d40881c1723a706f516fe75300f92408aa1dc6ae4288d2046f23c1aa2e54b7fb6448a0da922bd7f34"}}, "expected": {"result_hex": "3044022033ef4c2c284336a260660950a0784b2be5898ccb8698f74ca35a468d065da87902200326224b57ffb2066a8f12a11696a6c35bf3e0addd4488cd93c6513289527d11"}, "tags": ["computed", "nist-cavp-siggen-inputs", "record-14"]}
{"id": "sign-022", "function": "sign", "input": {"args": {"private_key_hex": "0000000000000000000000000000000000000000000000000000000000000001", "message_hex": "73616d706c65"}}, "expected": {"result_hex": "304502200466341174d59e93eb984c2a7c923a80ab99a9e91555bc73ebd8073d4c722121022100998f2b7bb63082e976215e6ae46344d66d2d4edea67d65d91595f21311df5030"}, "tags": ["computed", "edge-private-key", "private-key-one", "high-s"]}
{"id": "sign-023", "function": "sign", "input": {"args": {"private_key_hex": "0000000000000000000000000000000000000000000000000000000000000002", "message_hex": "73616d706c65"}}, "expected": {"result_hex": "304602210097569c6112a79ad900149760bc633fa7d0cfcc8c87d31bc4eef69faec8d6a206022100c72c3e8b4a182f53db2bc9b306e21932ecb52acd12d0c65ef27ac5967f4ae4f2"}, "tags": ["computed", "edge-private-key", "private-key-two", "high-s"]}
{"id": "sign-024", "function": "sign", "input": {"args": {"private_key_hex": "ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632550", "message_hex": "73616d706c65"}}, "expected": {"result_hex": "3046022100a2e09df104aafc802a50c6684f6db83355a0a2588585a778b311e7e6c7c600ce022100b783aa569666ddbb05d0a5b9ce18b2757b27e2f6a4b4b1a3e4b4ced21f4174b2"}, "tags": ["computed", "edge-private-key", "private-key-n-minus-1", "high-s"]}
{"id": "sign-025", "function": "sign", "input": {"args": {"private_key_hex": "c9afa9d845ba75166b5c215767b1d6934e50c3db36e89b127b8a622b120f6721", "message_hex": "73686f72742d696e74656765722d35"}}, "expected": {"result_hex": "3044022000ece04f4fc4a4b2be8038d568ae6f9f5f5fe7a160dc3dc38c7a47b429635544022004b8e05bd4c12c40b7a588534f25032e926cd732502f8ff7b618f79f853a6f71"}, "tags": ["computed", "rfc6979-a-2-5-key", "short-r"]}
{"id": "sign-026", "function": "sign", "input": {"args": {"private_key_hex": "c9afa9d845ba75166b5c215767b1d6934e50c3db36e89b127b8a622b120f6721", "message_hex": "73686f72742d696e74656765722d333936"}}, "expected": {"result_hex": "3044022100d278113f1eeb22bfb9220cb3429de13773989ea95503d44b4e5e45744e7cbed7021f75df483442820473b017936bf9c8a544b911e59b8520fdeabb426f5ce795f9"}, "tags": ["computed", "rfc6979-a-2-5-key", "short-s"]}
```

### Errors

```test-vectors
{"id": "sign-err-001", "function": "sign", "input": {"args": {"private_key_hex": "0000000000000000000000000000000000000000000000000000000000000000", "message_hex": "73616d706c65"}}, "expected": {"error": "InvalidPrivateKey"}, "tags": ["constructed", "invalid-private-key", "zero"]}
{"id": "sign-err-002", "function": "sign", "input": {"args": {"private_key_hex": "ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551", "message_hex": "73616d706c65"}}, "expected": {"error": "InvalidPrivateKey"}, "tags": ["constructed", "invalid-private-key", "order-n"]}
{"id": "sign-err-003", "function": "sign", "input": {"args": {"private_key_hex": "ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632552", "message_hex": "73616d706c65"}}, "expected": {"error": "InvalidPrivateKey"}, "tags": ["constructed", "invalid-private-key", "n-plus-1"]}
{"id": "sign-err-004", "function": "sign", "input": {"args": {"private_key_hex": "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "message_hex": "73616d706c65"}}, "expected": {"error": "InvalidPrivateKey"}, "tags": ["constructed", "invalid-private-key", "all-ones"]}
{"id": "sign-err-005", "function": "sign", "input": {"args": {"private_key_hex": "afa9d845ba75166b5c215767b1d6934e50c3db36e89b127b8a622b120f6721", "message_hex": "73616d706c65"}}, "expected": {"error": "InvalidPrivateKey"}, "tags": ["constructed", "invalid-private-key", "length-31"]}
{"id": "sign-err-006", "function": "sign", "input": {"args": {"private_key_hex": "00c9afa9d845ba75166b5c215767b1d6934e50c3db36e89b127b8a622b120f6721", "message_hex": "73616d706c65"}}, "expected": {"error": "InvalidPrivateKey"}, "tags": ["constructed", "invalid-private-key", "length-33"]}
{"id": "sign-err-007", "function": "sign", "input": {"args": {"private_key_hex": "", "message_hex": "73616d706c65"}}, "expected": {"error": "InvalidPrivateKey"}, "tags": ["constructed", "invalid-private-key", "empty"]}
```

## Edge Cases

- **Empty message.** Valid; SHA-256 of the empty string is signed.
- **High `s`.** Returned unchanged; about half of all signatures have it.
- **Short `r` or `s`.** A value below `2^248` has an INTEGER of at most 32 content bytes even with a `00` prefix, and the signature is correspondingly shorter.
- **Private key 1 or `n - 1`.** Valid keys; the nonce derivation and the signature equation handle them like any other.
- **Private key 0, `n`, `n + 1` or `2^256 - 1`.** `InvalidPrivateKey`.
- **33-byte key with a leading `00`, or 31 bytes.** `InvalidPrivateKey`: the length is exactly 32.

## Implementation Notes

- **Two hashes.** The message hash `h1` is SHA-256; the nonce generator is HMAC with SHA-256 too. With other curves or hashes these differ in length and `bits2int` must shift; on P-256 with SHA-256 nothing is truncated, but `bits2octets` still reduces `h1` modulo `n`.
- **Where `x` appears.** `int2octets(x)` is always 32 bytes, including leading zero bytes of a small key (the `private-key-one` vector).
- **Retry path.** Implement the loop of step 3.4 as written even though it practically never runs; a missing reseed (`K = HMAC_K(V || 0x00)`) would repeat the same `k`.
- **Scalar arithmetic.** `s` needs multiplication and inversion modulo `n` in constant time; Montgomery arithmetic modulo `n` works, and `k^(-1)` can be computed as `k^(n - 2) mod n`.
- **Shared code.** The scalar multiplication is the same as `ecdh-p256` uses for `public_key`; `k * G` with a precomputed table of multiples of `G` is the fast path.
- **Testing a signer.** Because the output is deterministic, the RFC 6979 vectors check the nonce derivation byte for byte; any deviation in HMAC keying, `bits2octets` or the loop shows as a different `r`.

## References

- [RFC 6979 -- Deterministic Usage of the Digital Signature Algorithm (DSA) and Elliptic Curve Digital Signature Algorithm (ECDSA)](https://www.rfc-editor.org/rfc/rfc6979.html) -- nonce generation (3.2), conversions (2.3), and the P-256 vectors (A.2.5).
- [FIPS 186-5 -- Digital Signature Standard (DSS)](https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.186-5.pdf) -- ECDSA signature generation, including its deterministic variant based on RFC 6979.
- [NIST SP 800-186](https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-186.pdf) -- the P-256 parameters (3.2.1.3).
- [RFC 3279](https://www.rfc-editor.org/rfc/rfc3279.html) -- `ECDSA-Sig-Value` (2.2.3).
- [RFC 8446](https://www.rfc-editor.org/rfc/rfc8446.html) -- `CertificateVerify` (4.4.3) and `ecdsa_secp256r1_sha256` (4.2.3).
- [NIST CAVP digital signatures](https://csrc.nist.gov/projects/cryptographic-algorithm-validation-program/digital-signatures) -- `186-4ecdsatestvectors.zip`.
