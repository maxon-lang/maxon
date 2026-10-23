+++
name = "ecdsa-sign-p384"
version = "1"
title = "Deterministic ECDSA Signing on P-384 with SHA-384 (RFC 6979)"
status = "draft"
keywords = ["ecdsa", "signature", "rfc6979", "deterministic", "p-384", "sha-384", "constant-time", "cryptography"]
difficulty = "hard"

[api]
functions = ["sign"]
types = ["EcdsaError"]

[references]
items = [
  { label = "RFC 6979", url = "https://www.rfc-editor.org/rfc/rfc6979.html" },
  { label = "FIPS 186-5", url = "https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.186-5.pdf" },
  { label = "NIST SP 800-186 (P-384 domain parameters)", url = "https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-186.pdf" },
  { label = "RFC 3279 section 2.2.3 (ECDSA-Sig-Value)", url = "https://www.rfc-editor.org/rfc/rfc3279.html#section-2.2.3" },
  { label = "RFC 8446 section 4.2.3 (ecdsa_secp384r1_sha384)", url = "https://www.rfc-editor.org/rfc/rfc8446.html#section-4.2.3" },
  { label = "ecdsa-sign-p256 (Canra spec)" },
  { label = "ecdsa-verify (Canra spec)" },
]

[test-vectors]
inline = true
external = []
+++

## Overview

This spec defines ECDSA signature generation on NIST P-384 with SHA-384, with the nonce `k` derived deterministically as RFC 6979 section 3.2 specifies, as one function:

- **sign** -- returns the DER-encoded ECDSA signature of a message under a P-384 private key.

It is the `ecdsa-sign-p256` spec with the curve, the hash and the sizes of P-384: the same nonce derivation, encoding and contracts. A TLS server or client with a P-384 certificate uses it for the `ecdsa_secp384r1_sha384` (0x0503) scheme. Its output verifies under the `ecdsa-verify` spec with curve `"p384"`.

**Out of scope:** verification, other curves and hashes (P-384 with SHA-256 included), signing a precomputed digest, hedged nonces, low-S normalization, raw `r || s` output, and private keys in PKCS #8 or SEC 1 containers.

## Types

### EcdsaError

```pseudocode
type EcdsaError =
  | InvalidPrivateKey   // the private key is not 48 bytes, or its value is 0 or at least n
```

### Curve P-384

`y^2 = x^3 - 3x + b` over `GF(p)`, base point `G` of prime order `n`, cofactor 1 (SP 800-186 section 3.2.1.4):

```pseudocode
p  = 0xfffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffeffffffff0000000000000000ffffffff
b  = 0xb3312fa7e23ee7e4988e056be3f82d19181d9c6efe8141120314088f5013875ac656398d8a2ed19d2a85c8edd3ec2aef
Gx = 0xaa87ca22be8b05378eb1c71ef320ad746e1d3b628ba79b9859f741e082542a385502f25dbf55296c3a545e3872760ab7
Gy = 0x3617de4a96262c6f5d9e98bf9292dc29f8f41dbd289a147ce9da3113b5f0b8c00a60b1ce1d7e819d7a431d7c90ea0e5f
n  = 0xffffffffffffffffffffffffffffffffffffffffffffffffc7634d81f4372ddf581a0db248b0a77aecec196accc52973
```

### Encodings

| Value | Encoding |
|---|---|
| Private key `x` | Exactly 48 bytes, the big-endian integer `x`; valid when `1 <= x <= n - 1` |
| Signature | DER `ECDSA-Sig-Value ::= SEQUENCE { r INTEGER, s INTEGER }`: each INTEGER minimal, with a `00` prefix exactly when its top bit is set; 8 to 104 bytes in all |

`int2octets(v)` is `v` as 48 big-endian bytes. `bits2octets(h) = int2octets(int(h) mod n)`; SHA-384 and `n` both have 384 bits, so `bits2int` needs no shift.

## Functions

### sign

```pseudocode
sign(private_key: Bytes, message: Bytes) -> Bytes | EcdsaError
```

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `private_key` | Bytes | yes | The private key, 48 bytes big-endian |
| `message` | Bytes | yes | Any length; hashed by this function with SHA-384 |

**Algorithm:**

1. If `length(private_key) != 48`, return `InvalidPrivateKey`. `x = int(private_key)`. If `x == 0` or `x >= n`, return `InvalidPrivateKey`.
2. `h1 = SHA-384(message)`; `e = int(h1)`.
3. RFC 6979 section 3.2 with `HMAC_K` = HMAC-SHA-384 keyed with `K`:
   1. `V = 0x01` repeated 48 times; `K = 0x00` repeated 48 times.
   2. `K = HMAC_K(V || 0x00 || int2octets(x) || bits2octets(h1))`; `V = HMAC_K(V)`.
   3. `K = HMAC_K(V || 0x01 || int2octets(x) || bits2octets(h1))`; `V = HMAC_K(V)`.
   4. Loop: `V = HMAC_K(V)`; `k = int(V)`. If `1 <= k <= n - 1`, try `k` in step 4; otherwise, or if step 4 rejects it, `K = HMAC_K(V || 0x00)`, `V = HMAC_K(V)` and repeat.
4. `(x1, y1) = k * G`; `r = x1 mod n`; `s = k^(-1) * (e + x * r) mod n`. If `r == 0` or `s == 0`, reject `k`.
5. Return `DER(SEQUENCE { INTEGER r, INTEGER s })`.

Each HMAC-SHA-384 output is 384 bits, so one `V` gives a whole candidate `k`.

## Behavioral Contracts

### sign()

**MUST:**

1. MUST return exactly the RFC 6979 signature with HMAC-SHA-384; the output is a function of `private_key` and `message` only.
2. MUST NOT normalize `s` to the low half.
3. MUST encode each integer minimally.
4. MUST return `InvalidPrivateKey` for a key that is not 48 bytes or whose value is 0 or at least `n`, and MUST NOT reduce it modulo `n`.
5. **Constant time**, as the `ecdsa-sign-p256` spec requires: no branch or memory index on `x`, `k` or intermediate values.
6. MUST produce signatures that `ecdsa-verify` accepts: `verify("p384", x * G, message, sign(x, message)) == true`.

**SHOULD:**

1. SHOULD verify the signature before returning it where faults are a threat, and overwrite `k`, `K`, `V` and copies of `x`.

## Test Vectors

`private_key_hex` and `message_hex` are lowercase hex. Expected output is the DER signature as `result_hex`. The tag `high-s` marks a signature whose `s` is above `n / 2`.

### Sources

- `rfc6979` vectors (2) are the two P-384 signatures with SHA-384 of RFC 6979 appendix A.2.6, for the messages `sample` and `test`, with the published `r` and `s` DER-encoded; the published nonces `k` were reproduced too.
- `computed` vectors (15) have no published signature: other messages under the appendix A.2.6 key, the private key `d` and message `Msg` of the first 8 `[P-384,SHA-384]` records of `SigGen.txt` in NIST CAVP `186-4ecdsatestvectors.zip` (each `d` checked against the published public key; the published signatures used a random `k`), and private keys 1, 2 and `n - 1`. They were
  computed by a pure-Python RFC 6979 implementation and by pyca/cryptography 50.0.2 with `deterministic_signing=True`, which agree, and each verifies.
- The error vectors are `constructed`, signing `sample`.

### sign()

```test-vectors
{"id": "sign-001", "function": "sign", "input": {"args": {"private_key_hex": "6b9d3dad2e1b8c1c05b19875b6659f4de23c3b667bf297ba9aa47740787137d896d5724e4c70a825f872c9ea60d2edf5", "message_hex": "73616d706c65"}}, "expected": {"result_hex": "306602310094edbb92a5ecb8aad4736e56c691916b3f88140666ce9fa73d64c4ea95ad133c81a648152e44acf96e36dd1e80fabe4602310099ef4aeb15f178cea1fe40db2603138f130e740a19624526203b6351d0a3a94fa329c145786e679e7b82c71a38628ac8"}, "tags": ["rfc6979", "rfc6979-a-2-6", "message-sample", "high-s"]}
{"id": "sign-002", "function": "sign", "input": {"args": {"private_key_hex": "6b9d3dad2e1b8c1c05b19875b6659f4de23c3b667bf297ba9aa47740787137d896d5724e4c70a825f872c9ea60d2edf5", "message_hex": "74657374"}}, "expected": {"result_hex": "30660231008203b63d3c853e8d77227fb377bcf7b7b772e97892a80f36ab775d509d7a5feb0542a7f0812998da8f1dd3ca3cf023db023100ddd0760448d42d8a43af45af836fce4de8be06b485e9b61b827c2f13173923e06a739f040649a667bf3b828246baa5a5"}, "tags": ["rfc6979", "rfc6979-a-2-6", "message-test", "high-s"]}
{"id": "sign-003", "function": "sign", "input": {"args": {"private_key_hex": "6b9d3dad2e1b8c1c05b19875b6659f4de23c3b667bf297ba9aa47740787137d896d5724e4c70a825f872c9ea60d2edf5", "message_hex": ""}}, "expected": {"result_hex": "3065023035533bf285d9f75c246b89680ba0cb6681aa491fedd71d90ca46670477cac6967399571259ccf0858d6fd051820f7109023100b5bc3402dd257b8fffd2599cf966d4a9bc4d947a030a52b193a9347f3ba6bae8fc2fa1e168e5e7d698d91a927df6baa2"}, "tags": ["computed", "rfc6979-a-2-6-key", "empty-message", "high-s"]}
{"id": "sign-004", "function": "sign", "input": {"args": {"private_key_hex": "6b9d3dad2e1b8c1c05b19875b6659f4de23c3b667bf297ba9aa47740787137d896d5724e4c70a825f872c9ea60d2edf5", "message_hex": "00"}}, "expected": {"result_hex": "306502306cc15528e083b83cd896ecc9a909a3bac31a10bf2fb21adc16f98a93c6a493aa0bcdcdbd702dc21cd195acb42e35d6ef023100f4ab08e32eb6741ec0143bf4c0ed2010080924dac17c2abb28bd2c556974121db520a5121395fd83794454d8b5180cbc"}, "tags": ["computed", "rfc6979-a-2-6-key", "one-zero-byte", "high-s"]}
{"id": "sign-005", "function": "sign", "input": {"args": {"private_key_hex": "6b9d3dad2e1b8c1c05b19875b6659f4de23c3b667bf297ba9aa47740787137d896d5724e4c70a825f872c9ea60d2edf5", "message_hex": "616263"}}, "expected": {"result_hex": "3065023100aec7df349f78b8929757fc0b067aac6515af088cab73812cc5d8ed7da61b18fcd79d2c08d7c7428bb5804c56f3e4225c02304e8849c97526c39f8f4e66492f01a65c2349a093270780e72ab8a9b119c59991d14cec47c37015f9bf116564e219113a"}, "tags": ["computed", "rfc6979-a-2-6-key", "abc"]}
{"id": "sign-006", "function": "sign", "input": {"args": {"private_key_hex": "6b9d3dad2e1b8c1c05b19875b6659f4de23c3b667bf297ba9aa47740787137d896d5724e4c70a825f872c9ea60d2edf5", "message_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff"}}, "expected": {"result_hex": "3065023100f1892d125287f6e32bc5e7e13f7514eb0ac4560e6973257d974ab59ea49f10fc0cc9e4dac0c615fa4cb37b3ba312ce21023077354494aa96f52cd751926692f1a23861bdb3ad25db78f322510102a10bef32a4240ea805599a094225e1a1f3c4ee55"}, "tags": ["computed", "rfc6979-a-2-6-key", "1024-bytes"]}
{"id": "sign-007", "function": "sign", "input": {"args": {"private_key_hex": "201b432d8df14324182d6261db3e4b3f46a8284482d52e370da41e6cbdf45ec2952f5db7ccbce3bc29449f4fb080ac97", "message_hex": "6b45d88037392e1371d9fd1cd174e9c1838d11c3d6133dc17e65fa0c485dcca9f52d41b60161246039e42ec784d49400bffdb51459f5de654091301a09378f93464d52118b48d44b30d781eb1dbed09da11fb4c818dbd442d161aba4b9edc79f05e4b7e401651395b53bd8b5bd3f2aaa6a00877fa9b45cadb8e648550b4c6cbe"}}, "expected": {"result_hex": "306402307bf62261872a9e532217a66b343e5820f23fb33d32248aadd6efefb78558c9be15708cd84170a2d479a79a51fa2b2c11023038d7d4e8e519c7559c11bce00a7b9343e2763e83a7f74c7e7eefa4bc8a0c72b5e3e9c7533306bb38aa3db3f7b85cd9d3"}, "tags": ["computed", "nist-cavp-siggen-inputs"]}
{"id": "sign-008", "function": "sign", "input": {"args": {"private_key_hex": "23d9f4ea6d87b7d6163d64256e3449255db14786401a51daa7847161bf56d494325ad2ac8ba928394e01061d882c3528", "message_hex": "d768f41e6e8ec2125d6cf5786d1ba96668ac6566c5cdbbe407f7f2051f3ad6b1acdbfe13edf0d0a86fa110f405406b69085219b5a234ebdb93153241f785d45811b3540d1c37424cc7194424787a51b79679266484c787fb1ded6d1a26b9567d5ea68f04be416caf3be9bd2cafa208fe2a9e234d3ae557c65d3fe6da4cb48da4"}}, "expected": {"result_hex": "30650230568499815b5c019416814366b403e46770ab2fd7c90e32ad4c574efddf0e260488643687c0eb3cf5112886bf1a9b1fa1023100fa331d918b507211493194403a1f3c9711ae19a9d294e4750f5a3b1bca505d54ebea0fc2dc563396aefc2aac33b163b6"}, "tags": ["computed", "nist-cavp-siggen-inputs", "high-s"]}
{"id": "sign-009", "function": "sign", "input": {"args": {"private_key_hex": "b5f670e98d8befc46f6f51fb2997069550c2a52ebfb4e5e25dd905352d9ef89eed5c2ecd16521853aadb1b52b8c42ae6", "message_hex": "6af6652e92a17b7898e40b6776fabaf0d74cf88d8f0ebfa6088309cbe09fac472eeac2aa8ea96b8c12e993d14c93f8ef4e8b547afe7ae5e4f3973170b35deb3239898918c70c1056332c3f894cd643d2d9b93c2561aac069577bbab45803250a31cd62226cab94d8cba7261dce9fe88c210c212b54329d76a273522c8ba91ddf"}}, "expected": {"result_hex": "3065023100ce1b50a045c7a382d658664c2bfb363cdb695e236d73c7e8118d2cfc687992d7b9b6aa608cda888e9902561893382f4502303ca549102198243bdb08816b41a66e1f2a8e6ccba323641ce75de960d0c9a04e4f6850fb485e46b404d8bdef6d58f5ac"}, "tags": ["computed", "nist-cavp-siggen-inputs"]}
{"id": "sign-010", "function": "sign", "input": {"args": {"private_key_hex": "de5975d8932533f092e76295ed6b23f10fc5fba48bfb82c6cc714826baf0126813247f8bd51d5738503654ab22459976", "message_hex": "b96d74b2265dd895d94e25092fb9262dc4f2f7a328a3c0c3da134b2d0a4e2058ca994e3445c5ff4f812738e1b0c0f7a126486942a12e674a21f22d0886d68df2375f41685d694d487a718024933a7c4306f33f1a4267d469c530b0fed4e7dea520a19dd68bf0203cc87cad652260ed43b7b23f6ed140d3085875190191a0381a"}}, "expected": {"result_hex": "3066023100fd85063c3bedf8129cb77a8d48ee214682c48591b97e1295fc241eacd3b9758e0fc3b790c621b7a6fc7a775444a5a5ba023100d53ee84b0241a93d9f0659067dcc96013c8e65ff84d2604ca89a97f5298e58559f9a7e49b62fcc73b7eae706f9798ba6"}, "tags": ["computed", "nist-cavp-siggen-inputs", "high-s"]}
{"id": "sign-011", "function": "sign", "input": {"args": {"private_key_hex": "11e0d470dc31fab0f5722f87b74a6c8d7414115e58ceb38bfcdced367beac3adbf1fe9ba5a04f72e978b1eb54597eabc", "message_hex": "7cec7480a037ff40c232c1d2d6e8cd4c080bbeecdaf3886fccc9f129bb6d202c316eca76c8ad4e76079afe622f833a16f4907e817260c1fa68b10c7a151a37eb8c036b057ed4652c353db4b4a34b37c9a2b300fb5f5fcfb8aa8adae13db359160f70a9241546140e550af0073468683377e6771b6508327408c245d78911c2cc"}}, "expected": {"result_hex": "3065023100be002129f378572739b2c91344ce9bfc3c2713c627b67aac6df9bbd67fc630b58948adde5eb1e63e70977529132faf6602305829a5b7db702ec09bd23be010bb1427be44d86724df140ba54aaf9648e4c2aaa9aef613eeca01422acf5cb49181990d"}, "tags": ["computed", "nist-cavp-siggen-inputs"]}
{"id": "sign-012", "function": "sign", "input": {"args": {"private_key_hex": "5c6bbf9fbcbb7b97c9535f57b431ed1ccae1945b7e8a4f1b032016b07810bd24a9e20055c0e9306650df59ef7e2cd8c2", "message_hex": "00ce978603229710345c9ad7c1c2dba3596b196528eea25bd822d43ca8f76a024e29217703dd0652c8a615284fc3edcc1c5ad1c8d5a8521c8e104c016a24e50c2e25066dcb56596f913b872767e3627aa3e55ec812e9fdac7c2f1beade83aef093e24c9c953982adf431a776880ae4583be158e11cdab1cbca3ad3a66900213d"}}, "expected": {"result_hex": "3065023100e77d183eefc67cfbf93139155e5cf76c02a4a2e28eef63af03a64688e512f2923f349ff1714417e02b91eb0eae2f6245023064b8427bbe3e1f5f82d86b014ec5a401f1b0ad4f7229ef2b0044591ffea68426db27c06c0239e470683323615c6b5a31"}, "tags": ["computed", "nist-cavp-siggen-inputs"]}
{"id": "sign-013", "function": "sign", "input": {"args": {"private_key_hex": "ffc7dedeff8343721f72046bc3c126626c177b0e48e247f44fd61f8469d4d5f0a74147fabaa334495cc1f986ebc5f0b1", "message_hex": "54a255c18692c6162a46add176a0ae8361dcb8948f092d8d7bac83e160431794d3b9812849bf1994bcdcfba56e8540c8a9ee5b93414548f2a653191b6bb28bda8dc70d45cc1b92a489f58a2d54f85766cb3c90de7dd88e690d8ebc9a79987eee1989df35af5e35522f83d85c48dda89863171c8b0bf4853ae28c2ac45c764416"}}, "expected": {"result_hex": "30640230660f15ef745a2207b78622599e6d2d3aad57e828ccdaf78415886c558b74545f58263c91a51239b11ceeb87af2af502202306c5e6cf47aea405eb6047c0f2a11c6dbae6cfae0eca1345d831d258353f933ce2ffde172b5555da6905f08d9d918a82e"}, "tags": ["computed", "nist-cavp-siggen-inputs"]}
{"id": "sign-014", "function": "sign", "input": {"args": {"private_key_hex": "adca364ef144a21df64b163615e8349cf74ee9dbf728104215c532073a7f74e2f67385779f7f74ab344cc3c7da061cf6", "message_hex": "692a78f90d4f9d5aee5da536314a78d68c1feabbfe5d1ccea7f6059a66c4b310f8051c411c409ccf6e19a0cbd8b8e100c48317fe8c6d4f8a638b9551ce7ee178020f04f7da3001a0e6855225fb3c9b375e4ed964588a1a41a095f3f476c42d52ffd23ce1702c93b56d4425d3befcf75d0951b6fd5c05b05455bdaf205fe70ca2"}}, "expected": {"result_hex": "3066023100fd427a668a43ee4b9fe08321a34110a294634bf10e86c914ef2da676fcd4013be24979e5549a8a220d479435b1bb50e902310082f19db71e61027359ac85710e6d27685a1ce5cc44fb19cccf3031736b8dc8c1369eba4c4bfd7870c325baa72069e94d"}, "tags": ["computed", "nist-cavp-siggen-inputs", "high-s"]}
{"id": "sign-015", "function": "sign", "input": {"args": {"private_key_hex": "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001", "message_hex": "73616d706c65"}}, "expected": {"result_hex": "3065023054b5983ba30e46c17271d99afe2d86fbbe7a975964cba523682723f47e8fcfe60e1e98d0d0e37000ec084a6ade61875a023100da3bbaf2b833559782fea9c883db613d09155f4de38037e09ddd683a5edd8c686e0a507257856c9ea26ebedcd4c72f9a"}, "tags": ["computed", "edge-private-key", "one", "high-s"]}
{"id": "sign-016", "function": "sign", "input": {"args": {"private_key_hex": "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000002", "message_hex": "73616d706c65"}}, "expected": {"result_hex": "306402304e286750fdedde14854f71b33b0ce676e87e078eb00024f94d3e0f0252bd480240b1d28ec9a125d854d0a3732533eb3d02300b232e6a02672e8169ac0ba9a719667281ed42d68426570fc8165c20ac5a86c0ddccbccc2625b88b7c5de7c69c52728a"}, "tags": ["computed", "edge-private-key", "two"]}
{"id": "sign-017", "function": "sign", "input": {"args": {"private_key_hex": "ffffffffffffffffffffffffffffffffffffffffffffffffc7634d81f4372ddf581a0db248b0a77aecec196accc52972", "message_hex": "73616d706c65"}}, "expected": {"result_hex": "3065023100e114e5c63592a2af1874173850859bf9211d51ea410e5e973b0b56438b0fb754eee0d8f0bdb5e913810b99d4271ca3a40230249fc834c9e532f19753d11f35e7b171d2186ef48e1f926f13fe69f1a63b7ea2ebfc40440fa063fec23674a3f8dd2e9f"}, "tags": ["computed", "edge-private-key", "n-minus-one"]}
```

### Errors

```test-vectors
{"id": "sign-err-001", "function": "sign", "input": {"args": {"private_key_hex": "6b9d3dad2e1b8c1c05b19875b6659f4de23c3b667bf297ba9aa47740787137d896d5724e4c70a825f872c9ea60d2ed", "message_hex": "73616d706c65"}}, "expected": {"error": "InvalidPrivateKey"}, "tags": ["constructed", "length-47"]}
{"id": "sign-err-002", "function": "sign", "input": {"args": {"private_key_hex": "6b9d3dad2e1b8c1c05b19875b6659f4de23c3b667bf297ba9aa47740787137d896d5724e4c70a825f872c9ea60d2edf500", "message_hex": "73616d706c65"}}, "expected": {"error": "InvalidPrivateKey"}, "tags": ["constructed", "length-49"]}
{"id": "sign-err-003", "function": "sign", "input": {"args": {"private_key_hex": "6b9d3dad2e1b8c1c05b19875b6659f4de23c3b667bf297ba9aa47740787137d8", "message_hex": "73616d706c65"}}, "expected": {"error": "InvalidPrivateKey"}, "tags": ["constructed", "length-32", "p256-sized-key"]}
{"id": "sign-err-004", "function": "sign", "input": {"args": {"private_key_hex": "", "message_hex": "73616d706c65"}}, "expected": {"error": "InvalidPrivateKey"}, "tags": ["constructed", "empty"]}
{"id": "sign-err-005", "function": "sign", "input": {"args": {"private_key_hex": "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000", "message_hex": "73616d706c65"}}, "expected": {"error": "InvalidPrivateKey"}, "tags": ["constructed", "zero"]}
{"id": "sign-err-006", "function": "sign", "input": {"args": {"private_key_hex": "ffffffffffffffffffffffffffffffffffffffffffffffffc7634d81f4372ddf581a0db248b0a77aecec196accc52973", "message_hex": "73616d706c65"}}, "expected": {"error": "InvalidPrivateKey"}, "tags": ["constructed", "equal-to-n"]}
{"id": "sign-err-007", "function": "sign", "input": {"args": {"private_key_hex": "ffffffffffffffffffffffffffffffffffffffffffffffffc7634d81f4372ddf581a0db248b0a77aecec196accc52974", "message_hex": "73616d706c65"}}, "expected": {"error": "InvalidPrivateKey"}, "tags": ["constructed", "n-plus-one"]}
{"id": "sign-err-008", "function": "sign", "input": {"args": {"private_key_hex": "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "message_hex": "73616d706c65"}}, "expected": {"error": "InvalidPrivateKey"}, "tags": ["constructed", "all-ff"]}
```


## Edge Cases

- **A P-256-sized (32-byte) key** is `InvalidPrivateKey`, not zero-extended.
- **Private keys 1, 2 and `n - 1`** are valid.
- **Short `r` or `s`** give a signature shorter than 104 bytes.

## Implementation Notes

- **Share the code** with `ecdsa-sign-p256`: only the field, the order, the hash and the octet length differ.
- **Nonce bias.** `k` comes from whole 384-bit HMAC outputs and is rejected, never reduced, when out of range.

## References

- [RFC 6979](https://www.rfc-editor.org/rfc/rfc6979.html) -- deterministic nonce (3.2) and the P-384 examples (A.2.6).
- [FIPS 186-5](https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.186-5.pdf) -- ECDSA signature generation (6.4.1).
- [NIST CAVP digital signatures](https://csrc.nist.gov/projects/cryptographic-algorithm-validation-program/digital-signatures) -- `186-4ecdsatestvectors.zip`.
