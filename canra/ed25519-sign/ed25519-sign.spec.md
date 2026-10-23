+++
name = "ed25519-sign"
version = "1"
title = "Ed25519 Key Derivation and Signing (RFC 8032)"
status = "draft"
keywords = ["ed25519", "eddsa", "signature", "curve25519", "rfc8032", "constant-time", "cryptography"]
difficulty = "hard"

[api]
functions = ["sign", "public_key"]
types = ["Ed25519Error"]

[references]
items = [
  { label = "RFC 8032 section 5.1 (Ed25519)", url = "https://www.rfc-editor.org/rfc/rfc8032.html#section-5.1" },
  { label = "RFC 8032 section 7.1 (test vectors)", url = "https://www.rfc-editor.org/rfc/rfc8032.html#section-7.1" },
  { label = "RFC 8446 section 4.2.3 (ed25519 scheme)", url = "https://www.rfc-editor.org/rfc/rfc8446.html#section-4.2.3" },
  { label = "ed25519-verify (Canra spec)" },
  { label = "sha-2 (Canra spec)" },
]

[test-vectors]
inline = true
external = []
+++

## Overview

This spec defines pure Ed25519 (RFC 8032 section 5.1) key derivation and signing, as two functions:

- **public_key** -- the 32-byte public key `A` of a 32-byte private key.
- **sign** -- the 64-byte signature `R || S` of a message.

Ed25519 signing is deterministic: the output depends only on the private key and the message. A TLS endpoint with an Ed25519 certificate uses it for the `ed25519` (0x0807) scheme. Signatures verify under the `ed25519-verify` spec, whose curve constants, point encoding and group order `L` this spec uses.

**Out of scope:** verification, Ed25519ctx and Ed25519ph, Ed448, key generation (a private key is 32 bytes from the `secure-random-bytes` spec), and private keys in PKCS #8 or other containers.

## Types

### Ed25519Error

```pseudocode
type Ed25519Error =
  | InvalidLength   // the private key is not exactly 32 bytes
```

## Functions

### public_key

```pseudocode
public_key(private_key: Bytes) -> Bytes | Ed25519Error
```

1. If `length(private_key) != 32`, return `InvalidLength`.
2. `h = SHA-512(private_key)`. `a` is `h[0..32]` read little-endian after clearing the lowest 3 bits of byte 0, clearing the highest bit of byte 31 and setting the second-highest bit of byte 31.
3. Return `encode(a * B)`, with the point encoding of the `ed25519-verify` spec (`y` little-endian, the sign of `x` in the top bit).

### sign

```pseudocode
sign(private_key: Bytes, message: Bytes) -> Bytes | Ed25519Error
```

1. If `length(private_key) != 32`, return `InvalidLength`.
2. `h = SHA-512(private_key)`; `a` from `h[0..32]` as above; `prefix = h[32..64]`; `A = encode(a * B)`.
3. `r = int_le(SHA-512(prefix || message)) mod L`; `R = encode(r * B)`.
4. `k = int_le(SHA-512(R || A || message)) mod L`.
5. `S = (r + k * a) mod L`.
6. Return `R || le32(S)`.

## Behavioral Contracts

**MUST:**

1. MUST return exactly the RFC 8032 public key and signature; both are functions of the inputs only.
2. MUST hash the 32-byte private key with SHA-512 and clamp the first half as specified; a private key is never used directly as the scalar.
3. MUST reduce `r` and `k` modulo `L` from the full 64-byte hashes, and return `S < L`.
4. **Constant time.** `a * B` and `r * B` MUST NOT branch on or index memory by `a`, `r` or `prefix`; fixed-base comb or ladder with constant-time selection.
5. MUST produce signatures that `ed25519-verify` accepts with `public_key(private_key)`.

**SHOULD:**

1. SHOULD keep `a` and `prefix` with the key rather than rehash per signature, and overwrite them when the key is discarded.

## Test Vectors

`private_key_hex` and `message_hex` are lowercase hex; the result is `result_hex`.

### Sources

- `rfc8032` vectors (10) are RFC 8032 section 7.1 TEST 1, TEST 2, TEST 3, TEST 1024 and TEST SHA(abc), each as a `sign` and a `public_key` vector with the published values.
- `computed` vectors (6) use an all-zero key, an all-`ff` key and a TLS 1.3 CertificateVerify input; they were computed by a pure-Python RFC 8032 implementation and by pyca/cryptography 50.0.2, which agree.
- The error vectors are `constructed`.

### sign()

```test-vectors
{"id": "sign-001", "function": "sign", "input": {"args": {"private_key_hex": "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60", "message_hex": ""}}, "expected": {"result_hex": "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b"}, "tags": ["rfc8032", "rfc8032-7-1", "test-1"]}
{"id": "sign-002", "function": "sign", "input": {"args": {"private_key_hex": "4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb", "message_hex": "72"}}, "expected": {"result_hex": "92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00"}, "tags": ["rfc8032", "rfc8032-7-1", "test-2"]}
{"id": "sign-003", "function": "sign", "input": {"args": {"private_key_hex": "c5aa8df43f9f837bedb7442f31dcb7b166d38535076f094b85ce3a2e0b4458f7", "message_hex": "af82"}}, "expected": {"result_hex": "6291d657deec24024827e69c3abe01a30ce548a284743a445e3680d7db5ac3ac18ff9b538d16f290ae67f760984dc6594a7c15e9716ed28dc027beceea1ec40a"}, "tags": ["rfc8032", "rfc8032-7-1", "test-3"]}
{"id": "sign-004", "function": "sign", "input": {"args": {"private_key_hex": "f5e5767cf153319517630f226876b86c8160cc583bc013744c6bf255f5cc0ee5", "message_hex": "08b8b2b733424243760fe426a4b54908632110a66c2f6591eabd3345e3e4eb98fa6e264bf09efe12ee50f8f54e9f77b1e355f6c50544e23fb1433ddf73be84d879de7c0046dc4996d9e773f4bc9efe5738829adb26c81b37c93a1b270b20329d658675fc6ea534e0810a4432826bf58c941efb65d57a338bbd2e26640f89ffbc1a858efcb8550ee3a5e1998bd177e93a7363c344fe6b199ee5d02e82d522c4feba15452f80288a821a579116ec6dad2b3b310da903401aa62100ab5d1a36553e06203b33890cc9b832f79ef80560ccb9a39ce767967ed628c6ad573cb116dbefefd75499da96bd68a8a97b928a8bbc103b6621fcde2beca1231d206be6cd9ec7aff6f6c94fcd7204ed3455c68c83f4a41da4af2b74ef5c53f1d8ac70bdcb7ed185ce81bd84359d44254d95629e9855a94a7c1958d1f8ada5d0532ed8a5aa3fb2d17ba70eb6248e594e1a2297acbbb39d502f1a8c6eb6f1ce22b3de1a1f40cc24554119a831a9aad6079cad88425de6bde1a9187ebb6092cf67bf2b13fd65f27088d78b7e883c8759d2c4f5c65adb7553878ad575f9fad878e80a0c9ba63bcbcc2732e69485bbc9c90bfbd62481d9089beccf80cfe2df16a2cf65bd92dd597b0707e0917af48bbb75fed413d238f5555a7a569d80c3414a8d0859dc65a46128bab27af87a71314f318c782b23ebfe808b82b0ce26401d2e22f04d83d1255dc51addd3b75a2b1ae0784504df543af8969be3ea7082ff7fc9888c144da2af58429ec96031dbcad3dad9af0dcbaaaf268cb8fcffead94f3c7ca495e056a9b47acdb751fb73e666c6c655ade8297297d07ad1ba5e43f1bca32301651339e22904cc8c42f58c30c04aafdb038dda0847dd988dcda6f3bfd15c4b4c4525004aa06eeff8ca61783aacec57fb3d1f92b0fe2fd1a85f6724517b65e614ad6808d6f6ee34dff7310fdc82aebfd904b01e1dc54b2927094b2db68d6f903b68401adebf5a7e08d78ff4ef5d63653a65040cf9bfd4aca7984a74d37145986780fc0b16ac451649de6188a7dbdf191f64b5fc5e2ab47b57f7f7276cd419c17a3ca8e1b939ae49e488acba6b965610b5480109c8b17b80e1b7b750dfc7598d5d5011fd2dcc5600a32ef5b52a1ecc820e308aa342721aac0943bf6686b64b2579376504ccc493d97e6aed3fb0f9cd71a43dd497f01f17c0e2cb3797aa2a2f256656168e6c496afc5fb93246f6b1116398a346f1a641f3b041e989f7914f90cc2c7fff357876e506b50d334ba77c225bc307ba537152f3f1610e4eafe595f6d9d90d11faa933a15ef1369546868a7f3a45a96768d40fd9d03412c091c6315cf4fde7cb68606937380db2eaaa707b4c4185c32eddcdd306705e4dc1ffc872eeee475a64dfac86aba41c0618983f8741c5ef68d3a101e8a3b8cac60c905c15fc910840b94c00a0b9d0"}}, "expected": {"result_hex": "0aab4c900501b3e24d7cdf4663326a3a87df5e4843b2cbdb67cbf6e460fec350aa5371b1508f9f4528ecea23c436d94b5e8fcd4f681e30a6ac00a9704a188a03"}, "tags": ["rfc8032", "rfc8032-7-1", "test-1024"]}
{"id": "sign-005", "function": "sign", "input": {"args": {"private_key_hex": "833fe62409237b9d62ec77587520911e9a759cec1d19755b7da901b96dca3d42", "message_hex": "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f"}}, "expected": {"result_hex": "dc2a4459e7369633a52b1bf277839a00201009a3efbf3ecb69bea2186c26b58909351fc9ac90b3ecfdfbc7c66431e0303dca179c138ac17ad9bef1177331a704"}, "tags": ["rfc8032", "rfc8032-7-1", "test-sha-abc"]}
{"id": "sign-006", "function": "sign", "input": {"args": {"private_key_hex": "0000000000000000000000000000000000000000000000000000000000000000", "message_hex": "73616d706c65"}}, "expected": {"result_hex": "a498cdd2794ba9b77b8179f22881eeea2e290fef0b10bee981c5b322b0362f18231720097c0b5458a8b83e862f75ee759e434567739af771eff331ffa3733e03"}, "tags": ["computed", "zero-key"]}
{"id": "sign-007", "function": "sign", "input": {"args": {"private_key_hex": "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "message_hex": "73616d706c65"}}, "expected": {"result_hex": "60a6fc4660e4b05476914ce72bfd7ade52568ff7f43d031f8a7fca52b516be33dd1b6005366a7083e92da1bf8ae0e6341d4bf485f3311ebd7a8c195539692e0e"}, "tags": ["computed", "ff-key"]}
{"id": "sign-008", "function": "sign", "input": {"args": {"private_key_hex": "79266bf5b632aa1e98a5e7887c5f700259075d85d6441e5cf8bb344d6e190f0d", "message_hex": "20202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020202020544c5320312e332c207365727665722043657274696669636174655665726966790054e6289e14c7b0e7ad9acc2dfc4c1e3d027d0eef7f5c4c3fe7c292761d0e06a6"}}, "expected": {"result_hex": "626e2974b7b771ffc570e15d90e90e52a6761fcb5f18d592b325c9001968d6ca2f55b20167646fbf2b079b09afe72a1742b431f51a6e43df924eaca7a41a5607"}, "tags": ["computed", "tls13-certificate-verify"]}
```

### public_key()

```test-vectors
{"id": "public-key-001", "function": "public_key", "input": {"args": {"private_key_hex": "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60"}}, "expected": {"result_hex": "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a"}, "tags": ["rfc8032", "rfc8032-7-1", "test-1"]}
{"id": "public-key-002", "function": "public_key", "input": {"args": {"private_key_hex": "4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb"}}, "expected": {"result_hex": "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c"}, "tags": ["rfc8032", "rfc8032-7-1", "test-2"]}
{"id": "public-key-003", "function": "public_key", "input": {"args": {"private_key_hex": "c5aa8df43f9f837bedb7442f31dcb7b166d38535076f094b85ce3a2e0b4458f7"}}, "expected": {"result_hex": "fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025"}, "tags": ["rfc8032", "rfc8032-7-1", "test-3"]}
{"id": "public-key-004", "function": "public_key", "input": {"args": {"private_key_hex": "f5e5767cf153319517630f226876b86c8160cc583bc013744c6bf255f5cc0ee5"}}, "expected": {"result_hex": "278117fc144c72340f67d0f2316e8386ceffbf2b2428c9c51fef7c597f1d426e"}, "tags": ["rfc8032", "rfc8032-7-1", "test-1024"]}
{"id": "public-key-005", "function": "public_key", "input": {"args": {"private_key_hex": "833fe62409237b9d62ec77587520911e9a759cec1d19755b7da901b96dca3d42"}}, "expected": {"result_hex": "ec172b93ad5e563bf4932c70e1245034c35467ef2efd4d64ebf819683467e2bf"}, "tags": ["rfc8032", "rfc8032-7-1", "test-sha-abc"]}
{"id": "public-key-006", "function": "public_key", "input": {"args": {"private_key_hex": "0000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"result_hex": "3b6a27bcceb6a42d62a3a8d02a6f0d73653215771de243a63ac048a18b59da29"}, "tags": ["computed", "zero-key"]}
{"id": "public-key-007", "function": "public_key", "input": {"args": {"private_key_hex": "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"}}, "expected": {"result_hex": "76a1592044a6e4f511265bca73a604d90b0529d1df602be30a19a9257660d1f5"}, "tags": ["computed", "ff-key"]}
{"id": "public-key-008", "function": "public_key", "input": {"args": {"private_key_hex": "79266bf5b632aa1e98a5e7887c5f700259075d85d6441e5cf8bb344d6e190f0d"}}, "expected": {"result_hex": "ca15927026d53ff19bc5e98c28bbf3666169f71913040c0d19989d059ae4ec4d"}, "tags": ["computed", "tls13-certificate-verify"]}
```

### Errors

```test-vectors
{"id": "ed25519-err-001", "function": "sign", "input": {"args": {"private_key_hex": "00000000000000000000000000000000000000000000000000000000000000", "message_hex": "616263"}}, "expected": {"error": "InvalidLength"}, "tags": ["length-31"]}
{"id": "ed25519-err-002", "function": "sign", "input": {"args": {"private_key_hex": "000000000000000000000000000000000000000000000000000000000000000000", "message_hex": "616263"}}, "expected": {"error": "InvalidLength"}, "tags": ["length-33"]}
{"id": "ed25519-err-003", "function": "sign", "input": {"args": {"private_key_hex": "", "message_hex": "616263"}}, "expected": {"error": "InvalidLength"}, "tags": ["empty"]}
{"id": "ed25519-err-004", "function": "sign", "input": {"args": {"private_key_hex": "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000", "message_hex": "616263"}}, "expected": {"error": "InvalidLength"}, "tags": ["expanded-64-byte-key"]}
{"id": "ed25519-err-005", "function": "public_key", "input": {"args": {"private_key_hex": "00000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"error": "InvalidLength"}, "tags": ["length-31"]}
{"id": "ed25519-err-006", "function": "public_key", "input": {"args": {"private_key_hex": ""}}, "expected": {"error": "InvalidLength"}, "tags": ["empty"]}
```


## Edge Cases

- **A 64-byte "expanded" key** (seed and public key concatenated, as some libraries store it) is `InvalidLength`.
- **An all-zero or all-`ff` private key** is valid: it is hashed before use.
- **The empty message** is signed like any other.

## Implementation Notes

- **Reuse** the field and point code of `ed25519-verify`; signing needs only fixed-base multiplication.
- **Fault attacks.** A fault in `S` or a reused `R` with another message reveals `a`; verifying before returning blocks the first.

## References

- [RFC 8032](https://www.rfc-editor.org/rfc/rfc8032.html) -- key generation (5.1.5), signing (5.1.6), test vectors (7.1).
- [RFC 8446](https://www.rfc-editor.org/rfc/rfc8446.html) -- the `ed25519` signature scheme.
