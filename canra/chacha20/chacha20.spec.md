+++
name = "chacha20"
version = "1"
title = "ChaCha20 Stream Cipher (RFC 8439)"
status = "draft"
keywords = ["chacha20", "stream-cipher", "rfc8439", "constant-time", "cryptography"]
difficulty = "moderate"

[api]
functions = ["xor_key_stream"]
types = ["ChaCha20Error"]

[references]
items = [
  { label = "RFC 8439", url = "https://www.rfc-editor.org/rfc/rfc8439.html" },
  { label = "Bernstein, ChaCha, a variant of Salsa20", url = "https://cr.yp.to/chacha/chacha-20080128.pdf" },
]

[test-vectors]
inline = true
external = []
+++

## Overview

This spec defines the ChaCha20 cipher of RFC 8439 section 2.4, with a 256-bit key, a 96-bit nonce and a 32-bit block counter, as one function:

- **xor_key_stream** -- XORs `data` with the ChaCha20 keystream that starts at block `counter`. Encryption and decryption are the same operation.

The ChaCha20 block function (RFC 8439 section 2.3) is specified here as part of the algorithm; its test vectors are expressed as keystream, that is `xor_key_stream` over 64 zero bytes.

**Out of scope:** the original 64-bit-nonce variant with a 64-bit counter, XChaCha20 (24-byte nonce), HChaCha20, reduced-round variants (ChaCha8, ChaCha12), seeking within a block (the keystream always starts at the beginning of block `counter`), streaming state across calls, and authentication (the `poly1305` and `chacha20-poly1305` specs).

## Types

### ChaCha20Error

```pseudocode
type ChaCha20Error =
  | InvalidKeyLength     // the key is not exactly 32 bytes
  | InvalidNonceLength   // the nonce is not exactly 12 bytes
  | CounterOverflow      // the keystream would need a block counter above 2^32 - 1
```

### The counter rule

The block counter is a 32-bit unsigned integer and never wraps. `xor_key_stream` uses the blocks `counter, counter + 1, ..., counter + ceil(length(data) / 64) - 1`, and returns `CounterOverflow` exactly when

```pseudocode
counter > 4294967295  or  counter + ceil(length(data) / 64) - 1 > 4294967295
```

Equivalently, `length(data)` may be at most `64 * (2^32 - counter)` bytes. Empty data uses no block and is valid for every `counter` from 0 to `2^32 - 1`; a block whose counter is exactly `2^32 - 1` is valid. A statically typed implementation MAY give `counter` an unsigned 32-bit type; the vectors with `counter` 4294967296 are then compile-time rejections, like a negative counter (see Edge Cases).

## Functions

### xor_key_stream

```pseudocode
xor_key_stream(key: Bytes, nonce: Bytes, counter: Int, data: Bytes) -> Bytes | ChaCha20Error
```

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `key` | Bytes | yes | Exactly 32 bytes |
| `nonce` | Bytes | yes | Exactly 12 bytes; MUST be unique per key for a given counter range |
| `counter` | Int | yes | Initial block counter, `0 <= counter <= 2^32 - 1` |
| `data` | Bytes | yes | Plaintext or ciphertext, any length within the counter rule |

**Returns:** `length(data)` bytes, or `InvalidKeyLength`, or `InvalidNonceLength`, or `CounterOverflow`.

**Algorithm:**

1. If `length(key) != 32`, return `InvalidKeyLength`.
2. If `length(nonce) != 12`, return `InvalidNonceLength`.
3. If the counter rule is violated, return `CounterOverflow`, before producing any output.
4. For `j` in `0 .. ceil(length(data) / 64) - 1`: `ks = chacha20_block(key, counter + j, nonce)`; output `data[64j .. 64j + 63] XOR ks`, truncating `ks` for the final partial block.
5. Return the output.

### chacha20_block

```pseudocode
chacha20_block(key, block_counter, nonce) -> 64 bytes
  state[0..3]   = 0x61707865, 0x3320646e, 0x79622d32, 0x6b206574   // "expand 32-byte k"
  state[4..11]  = key as eight 32-bit little-endian words
  state[12]     = block_counter
  state[13..15] = nonce as three 32-bit little-endian words
  working = state
  repeat 10 times:                       // 20 rounds
    QR(working, 0, 4,  8, 12); QR(working, 1, 5,  9, 13)   // column rounds
    QR(working, 2, 6, 10, 14); QR(working, 3, 7, 11, 15)
    QR(working, 0, 5, 10, 15); QR(working, 1, 6, 11, 12)   // diagonal rounds
    QR(working, 2, 7,  8, 13); QR(working, 3, 4,  9, 14)
  return (working + state), each word added mod 2^32, serialized as 16 little-endian words

QR(s, a, b, c, d):
  s[a] += s[b]; s[d] ^= s[a]; s[d] <<<= 16
  s[c] += s[d]; s[b] ^= s[c]; s[b] <<<= 12
  s[a] += s[b]; s[d] ^= s[a]; s[d] <<<= 8
  s[c] += s[d]; s[b] ^= s[c]; s[b] <<<= 7
```

`+=` is addition modulo 2^32, `^=` is XOR, and `<<<=` is a left rotation of a 32-bit word.

## Behavioral Contracts

### xor_key_stream()

**MUST:**

1. MUST return the RFC 8439 section 2.4 output, with block `j` of the keystream generated with block counter `counter + j`.
2. MUST check the key length, then the nonce length, then the counter rule, and return an error before writing any output.
3. MUST return `CounterOverflow` under the counter rule, and MUST NOT wrap the counter to 0, carry it into the nonce, or return a truncated result.
4. MUST be its own inverse: `xor_key_stream(k, n, c, xor_key_stream(k, n, c, d)) == d`.
5. MUST return output of exactly `length(data)` bytes, discarding the unused keystream of the final block.
6. MUST run in constant time: it uses only 32-bit additions, XORs and fixed rotations, and MUST NOT branch on or index memory by the key, the nonce, the data or the keystream. The length of `data` and the value of `counter` are not secret.
7. MUST NOT modify its inputs (an in-place variant is out of scope).

**SHOULD:**

1. SHOULD overwrite the state and keystream buffers before returning.

The caller MUST NOT use the same key, nonce and block counter for two different messages: the XOR of the two ciphertexts is the XOR of the plaintexts.

## Test Vectors

`key_hex`, `nonce_hex` and `data_hex` are lowercase hex; `counter` is an integer. Expected output is `result_hex`.

### Sources

- `rfc8439-2-3-2` is the block function test vector of RFC 8439 section 2.3.2 (block counter 1), as `xor_key_stream` over 64 zero bytes; its result is the serialized block.
- `rfc8439-2-4-2` is the encryption example of section 2.4.2, in both directions.
- `rfc8439-a1-tvN` are the five block function vectors of appendix A.1, each over 64 zero bytes; the results are the published keystreams.
- `rfc8439-a2-tv2` and `rfc8439-a2-tv3` are appendix A.2 test vectors #2 and #3. Test vector #1 of A.2 has the same inputs and output as A.1 test vector #1 and is not repeated.
- `prefix` vectors take the first 1 byte of the section 2.4.2 plaintext, or the first 63, 64, 65 and 128 bytes of the A.2 test vector #2 plaintext; their expected outputs are the same prefixes of the published ciphertexts, because the keystream does not depend on the data length.
- `computed` vectors (the `counter-max` vectors with data) have no published source; their outputs were computed with two independent implementations, and they test that the block with counter `2^32 - 1` is produced and that the counter is not wrapped before it.
- The error vectors follow from the counter rule and the length checks, and were confirmed with the oracle.

### xor_key_stream()

```test-vectors
{"id": "xor-key-stream-001", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "000000090000004a00000000", "counter": 1, "data_hex": "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"result_hex": "10f1e7e4d13b5915500fdd1fa32071c4c7d1f4c733c068030422aa9ac3d46c4ed2826446079faa0914c2d705d98b02a2b5129cd1de164eb9cbd083e8a2503c4e"}, "tags": ["rfc8439", "rfc8439-2-3-2", "block-function", "keystream"]}
{"id": "xor-key-stream-002", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "000000000000004a00000000", "counter": 1, "data_hex": "4c616469657320616e642047656e746c656d656e206f662074686520636c617373206f66202739393a204966204920636f756c64206f6666657220796f75206f6e6c79206f6e652074697020666f7220746865206675747572652c2073756e73637265656e20776f756c642062652069742e"}}, "expected": {"result_hex": "6e2e359a2568f98041ba0728dd0d6981e97e7aec1d4360c20a27afccfd9fae0bf91b65c5524733ab8f593dabcd62b3571639d624e65152ab8f530c359f0861d807ca0dbf500d6a6156a38e088a22b65e52bc514d16ccf806818ce91ab77937365af90bbf74a35be6b40b8eedf2785e42874d"}, "tags": ["rfc8439", "rfc8439-2-4-2"]}
{"id": "xor-key-stream-003", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "000000000000004a00000000", "counter": 1, "data_hex": "6e2e359a2568f98041ba0728dd0d6981e97e7aec1d4360c20a27afccfd9fae0bf91b65c5524733ab8f593dabcd62b3571639d624e65152ab8f530c359f0861d807ca0dbf500d6a6156a38e088a22b65e52bc514d16ccf806818ce91ab77937365af90bbf74a35be6b40b8eedf2785e42874d"}}, "expected": {"result_hex": "4c616469657320616e642047656e746c656d656e206f662074686520636c617373206f66202739393a204966204920636f756c64206f6666657220796f75206f6e6c79206f6e652074697020666f7220746865206675747572652c2073756e73637265656e20776f756c642062652069742e"}, "tags": ["rfc8439", "rfc8439-2-4-2", "decrypt"]}
{"id": "xor-key-stream-004", "function": "xor_key_stream", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000000000000000000000000000", "nonce_hex": "000000000000000000000000", "counter": 0, "data_hex": "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"result_hex": "76b8e0ada0f13d90405d6ae55386bd28bdd219b8a08ded1aa836efcc8b770dc7da41597c5157488d7724e03fb8d84a376a43b8f41518a11cc387b669b2ee6586"}, "tags": ["rfc8439", "rfc8439-a1-tv1", "block-function", "keystream"]}
{"id": "xor-key-stream-005", "function": "xor_key_stream", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000000000000000000000000000", "nonce_hex": "000000000000000000000000", "counter": 1, "data_hex": "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"result_hex": "9f07e7be5551387a98ba977c732d080dcb0f29a048e3656912c6533e32ee7aed29b721769ce64e43d57133b074d839d531ed1f28510afb45ace10a1f4b794d6f"}, "tags": ["rfc8439", "rfc8439-a1-tv2", "block-function", "keystream"]}
{"id": "xor-key-stream-006", "function": "xor_key_stream", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000000000000000000000000001", "nonce_hex": "000000000000000000000000", "counter": 1, "data_hex": "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"result_hex": "3aeb5224ecf849929b9d828db1ced4dd832025e8018b8160b82284f3c949aa5a8eca00bbb4a73bdad192b5c42f73f2fd4e273644c8b36125a64addeb006c13a0"}, "tags": ["rfc8439", "rfc8439-a1-tv3", "block-function", "keystream"]}
{"id": "xor-key-stream-007", "function": "xor_key_stream", "input": {"args": {"key_hex": "00ff000000000000000000000000000000000000000000000000000000000000", "nonce_hex": "000000000000000000000000", "counter": 2, "data_hex": "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"result_hex": "72d54dfbf12ec44b362692df94137f328fea8da73990265ec1bbbea1ae9af0ca13b25aa26cb4a648cb9b9d1be65b2c0924a66c54d545ec1b7374f4872e99f096"}, "tags": ["rfc8439", "rfc8439-a1-tv4", "block-function", "keystream"]}
{"id": "xor-key-stream-008", "function": "xor_key_stream", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000000000000000000000000000", "nonce_hex": "000000000000000000000002", "counter": 0, "data_hex": "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"result_hex": "c2c64d378cd536374ae204b9ef933fcd1a8b2288b3dfa49672ab765b54ee27c78a970e0e955c14f3a88e741b97c286f75f8fc299e8148362fa198a39531bed6d"}, "tags": ["rfc8439", "rfc8439-a1-tv5", "block-function", "keystream"]}
{"id": "xor-key-stream-009", "function": "xor_key_stream", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000000000000000000000000001", "nonce_hex": "000000000000000000000002", "counter": 1, "data_hex": "416e79207375626d697373696f6e20746f20746865204945544620696e74656e6465642062792074686520436f6e7472696275746f7220666f72207075626c69636174696f6e20617320616c6c206f722070617274206f6620616e204945544620496e7465726e65742d4472616674206f722052464320616e6420616e792073746174656d656e74206d6164652077697468696e2074686520636f6e74657874206f6620616e204945544620616374697669747920697320636f6e7369646572656420616e20224945544620436f6e747269627574696f6e222e20537563682073746174656d656e747320696e636c756465206f72616c2073746174656d656e747320696e20494554462073657373696f6e732c2061732077656c6c206173207772697474656e20616e6420656c656374726f6e696320636f6d6d756e69636174696f6e73206d61646520617420616e792074696d65206f7220706c6163652c207768696368206172652061646472657373656420746f"}}, "expected": {"result_hex": "a3fbf07df3fa2fde4f376ca23e82737041605d9f4f4f57bd8cff2c1d4b7955ec2a97948bd3722915c8f3d337f7d370050e9e96d647b7c39f56e031ca5eb6250d4042e02785ececfa4b4bb5e8ead0440e20b6e8db09d881a7c6132f420e52795042bdfa7773d8a9051447b3291ce1411c680465552aa6c405b7764d5e87bea85ad00f8449ed8f72d0d662ab052691ca66424bc86d2df80ea41f43abf937d3259dc4b2d0dfb48a6c9139ddd7f76966e928e635553ba76c5c879d7b35d49eb2e62b0871cdac638939e25e8a1e0ef9d5280fa8ca328b351c3c765989cbcf3daa8b6ccc3aaf9f3979c92b3720fc88dc95ed84a1be059c6499b9fda236e7e818b04b0bc39c1e876b193bfe5569753f88128cc08aaa9b63d1a16f80ef2554d7189c411f5869ca52c5b83fa36ff216b9c1d30062bebcfd2dc5bce0911934fda79a86f6e698ced759c3ff9b6477338f3da4f9cd8514ea9982ccafb341b2384dd902f3d1ab7ac61dd29c6f21ba5b862f3730e37cfdc4fd806c22f221"}, "tags": ["rfc8439", "rfc8439-a2-tv2"]}
{"id": "xor-key-stream-010", "function": "xor_key_stream", "input": {"args": {"key_hex": "1c9240a5eb55d38af333888604f6b5f0473917c1402b80099dca5cbc207075c0", "nonce_hex": "000000000000000000000002", "counter": 42, "data_hex": "2754776173206272696c6c69672c20616e642074686520736c6974687920746f7665730a446964206779726520616e642067696d626c6520696e2074686520776162653a0a416c6c206d696d737920776572652074686520626f726f676f7665732c0a416e6420746865206d6f6d65207261746873206f757467726162652e"}}, "expected": {"result_hex": "62e6347f95ed87a45ffae7426f27a1df5fb69110044c0d73118effa95b01e5cf166d3df2d721caf9b21e5fb14c616871fd84c54f9d65b283196c7fe4f60553ebf39c6402c42234e32a356b3e764312a61a5532055716ead6962568f87d3f3f7704c6a8d1bcd1bf4d50d6154b6da731b187b58dfd728afa36757a797ac188d1"}, "tags": ["rfc8439", "rfc8439-a2-tv3"]}
{"id": "xor-key-stream-011", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "000000000000004a00000000", "counter": 1, "data_hex": ""}}, "expected": {"result_hex": ""}, "tags": ["empty", "edge"]}
{"id": "xor-key-stream-012", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "000000000000004a00000000", "counter": 1, "data_hex": "4c"}}, "expected": {"result_hex": "6e"}, "tags": ["rfc8439-2-4-2", "prefix", "one-byte", "edge"]}
{"id": "xor-key-stream-013", "function": "xor_key_stream", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000000000000000000000000001", "nonce_hex": "000000000000000000000002", "counter": 1, "data_hex": "416e79207375626d697373696f6e20746f20746865204945544620696e74656e6465642062792074686520436f6e7472696275746f7220666f72207075626c"}}, "expected": {"result_hex": "a3fbf07df3fa2fde4f376ca23e82737041605d9f4f4f57bd8cff2c1d4b7955ec2a97948bd3722915c8f3d337f7d370050e9e96d647b7c39f56e031ca5eb625"}, "tags": ["rfc8439-a2-tv2", "prefix", "length-63", "edge"]}
{"id": "xor-key-stream-014", "function": "xor_key_stream", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000000000000000000000000001", "nonce_hex": "000000000000000000000002", "counter": 1, "data_hex": "416e79207375626d697373696f6e20746f20746865204945544620696e74656e6465642062792074686520436f6e7472696275746f7220666f72207075626c69"}}, "expected": {"result_hex": "a3fbf07df3fa2fde4f376ca23e82737041605d9f4f4f57bd8cff2c1d4b7955ec2a97948bd3722915c8f3d337f7d370050e9e96d647b7c39f56e031ca5eb6250d"}, "tags": ["rfc8439-a2-tv2", "prefix", "length-64", "edge"]}
{"id": "xor-key-stream-015", "function": "xor_key_stream", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000000000000000000000000001", "nonce_hex": "000000000000000000000002", "counter": 1, "data_hex": "416e79207375626d697373696f6e20746f20746865204945544620696e74656e6465642062792074686520436f6e7472696275746f7220666f72207075626c6963"}}, "expected": {"result_hex": "a3fbf07df3fa2fde4f376ca23e82737041605d9f4f4f57bd8cff2c1d4b7955ec2a97948bd3722915c8f3d337f7d370050e9e96d647b7c39f56e031ca5eb6250d40"}, "tags": ["rfc8439-a2-tv2", "prefix", "length-65", "edge"]}
{"id": "xor-key-stream-016", "function": "xor_key_stream", "input": {"args": {"key_hex": "0000000000000000000000000000000000000000000000000000000000000001", "nonce_hex": "000000000000000000000002", "counter": 1, "data_hex": "416e79207375626d697373696f6e20746f20746865204945544620696e74656e6465642062792074686520436f6e7472696275746f7220666f72207075626c69636174696f6e20617320616c6c206f722070617274206f6620616e204945544620496e7465726e65742d4472616674206f722052464320616e6420616e792073"}}, "expected": {"result_hex": "a3fbf07df3fa2fde4f376ca23e82737041605d9f4f4f57bd8cff2c1d4b7955ec2a97948bd3722915c8f3d337f7d370050e9e96d647b7c39f56e031ca5eb6250d4042e02785ececfa4b4bb5e8ead0440e20b6e8db09d881a7c6132f420e52795042bdfa7773d8a9051447b3291ce1411c680465552aa6c405b7764d5e87bea85a"}, "tags": ["rfc8439-a2-tv2", "prefix", "length-128", "edge"]}
{"id": "xor-key-stream-017", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "000000000000004a00000000", "counter": 4294967295, "data_hex": "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"result_hex": "6d29da5bd16a472910e8c0bdb47edfc8499c3222cc168d3721747fc2b21266d9f15c8339f10f354d16cc9b8e118eb182bf858ce5718fa4e76389ea4eb50a9475"}, "tags": ["counter-max", "last-block", "edge", "computed"]}
{"id": "xor-key-stream-018", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "000000000000004a00000000", "counter": 4294967295, "data_hex": "00"}}, "expected": {"result_hex": "6d"}, "tags": ["counter-max", "last-block", "edge", "computed"]}
{"id": "xor-key-stream-019", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "000000000000004a00000000", "counter": 4294967294, "data_hex": "0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"result_hex": "143d2a137837a2a369b90769dd68f5ae394a28786b03f80c2a1e8d3d1ebdf4f0181e597e89f42939e94c717d60b681d34cf82dda79827ab2455b13428e525fd96d29da5bd16a472910e8c0bdb47edfc8499c3222cc168d3721747fc2b21266d9f15c8339f10f354d16cc9b8e118eb182bf858ce5718fa4e76389ea4eb50a9475"}, "tags": ["counter-max", "last-block", "edge", "computed"]}
{"id": "xor-key-stream-020", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "000000000000004a00000000", "counter": 4294967295, "data_hex": ""}}, "expected": {"result_hex": ""}, "tags": ["counter-max", "empty", "edge"]}
```

### Errors

```test-vectors
{"id": "chacha20-err-001", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "000000000000004a00000000", "counter": 4294967295, "data_hex": "0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"error": "CounterOverflow"}, "tags": ["counter-overflow", "edge"]}
{"id": "chacha20-err-002", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "000000000000004a00000000", "counter": 4294967294, "data_hex": "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"error": "CounterOverflow"}, "tags": ["counter-overflow", "edge"]}
{"id": "chacha20-err-003", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "000000000000004a00000000", "counter": 4294967295, "data_hex": "0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"error": "CounterOverflow"}, "tags": ["counter-overflow", "edge"]}
{"id": "chacha20-err-004", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "000000000000004a00000000", "counter": 4294967296, "data_hex": ""}}, "expected": {"error": "CounterOverflow"}, "tags": ["counter-overflow", "counter-out-of-range", "edge"]}
{"id": "chacha20-err-005", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "000000000000004a00000000", "counter": 4294967296, "data_hex": "00"}}, "expected": {"error": "CounterOverflow"}, "tags": ["counter-overflow", "counter-out-of-range", "edge"]}
{"id": "chacha20-err-006", "function": "xor_key_stream", "input": {"args": {"key_hex": "", "nonce_hex": "000000000000004a00000000", "counter": 1, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "chacha20-err-007", "function": "xor_key_stream", "input": {"args": {"key_hex": "00000000000000000000000000000000", "nonce_hex": "000000000000004a00000000", "counter": 1, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "chacha20-err-008", "function": "xor_key_stream", "input": {"args": {"key_hex": "00000000000000000000000000000000000000000000000000000000000000", "nonce_hex": "000000000000004a00000000", "counter": 1, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "chacha20-err-009", "function": "xor_key_stream", "input": {"args": {"key_hex": "000000000000000000000000000000000000000000000000000000000000000000", "nonce_hex": "000000000000004a00000000", "counter": 1, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "edge"]}
{"id": "chacha20-err-010", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "", "counter": 1, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidNonceLength"}, "tags": ["invalid-nonce-length", "edge"]}
{"id": "chacha20-err-011", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "0000000000000000", "counter": 1, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidNonceLength"}, "tags": ["invalid-nonce-length", "edge"]}
{"id": "chacha20-err-012", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "0000000000000000000000", "counter": 1, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidNonceLength"}, "tags": ["invalid-nonce-length", "edge"]}
{"id": "chacha20-err-013", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "00000000000000000000000000", "counter": 1, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidNonceLength"}, "tags": ["invalid-nonce-length", "edge"]}
{"id": "chacha20-err-014", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "00000000000000000000000000000000", "counter": 1, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidNonceLength"}, "tags": ["invalid-nonce-length", "edge"]}
{"id": "chacha20-err-015", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "000000000000000000000000000000000000000000000000", "counter": 1, "data_hex": "00000000000000000000000000000000"}}, "expected": {"error": "InvalidNonceLength"}, "tags": ["invalid-nonce-length", "edge"]}
{"id": "chacha20-err-016", "function": "xor_key_stream", "input": {"args": {"key_hex": "00000000000000000000000000000000", "nonce_hex": "0000000000000000", "counter": 4294967295, "data_hex": "0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"error": "InvalidKeyLength"}, "tags": ["invalid-key-length", "check-order"]}
{"id": "chacha20-err-017", "function": "xor_key_stream", "input": {"args": {"key_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "nonce_hex": "0000000000000000", "counter": 4294967295, "data_hex": "0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"}}, "expected": {"error": "InvalidNonceLength"}, "tags": ["invalid-nonce-length", "check-order"]}
```

## Edge Cases

- **Empty data.** Returns empty bytes for any valid key, nonce and counter, including `2^32 - 1`.
- **One byte.** The first byte of the keystream block `counter`, XORed with the data byte.
- **Block boundaries.** 63, 64 and 65 bytes take one, one and two blocks; the 65th byte uses block `counter + 1` from its first byte.
- **Last counter.** `counter = 2^32 - 1` with 1 to 64 bytes uses exactly that block and is valid. With 65 bytes it would need block `2^32`: `CounterOverflow`. `counter = 2^32 - 2` allows up to 128 bytes.
- **Counter out of range.** `counter = 2^32` is `CounterOverflow` even for empty data.
- **Negative counter.** `counter` is non-negative; an implementation SHOULD give it an unsigned or range-restricted type. Where the type admits negatives, a negative counter MUST fail loudly as an argument error and MUST NOT be reduced modulo 2^32. It is a type error, not a vector.
- **Other nonce lengths.** 8 bytes (the original ChaCha nonce) and 24 bytes (XChaCha20) are `InvalidNonceLength`; this spec never converts between variants.
- **Key and nonce both wrong.** `InvalidKeyLength`. A wrong nonce with an overflowing counter is `InvalidNonceLength`.

## Implementation Notes

- **Endianness.** Key words, nonce words, the counter and the output are all little-endian. Reading the key as big-endian words fails the `rfc8439-2-3-2` vector.
- **Nonce layout.** RFC 8439 puts the 32-bit counter in word 12 and the 96-bit nonce in words 13-15. The original ChaCha used a 64-bit counter in words 12-13; mixing the layouts passes only the vectors with an all-zero nonce.
- **Add the input state.** The block output is `rounds(state) + state`. Omitting the final addition makes the block function invertible and fails every vector.
- **Overflow arithmetic.** All additions are modulo 2^32. In languages that trap on overflow, use wrapping addition; widening `counter + j` past 32 bits silently is exactly what the counter rule forbids.
- **Performance.** Process several blocks at once with vector registers (four or eight independent blocks with counters `counter + j`); the result is unchanged.
- **Counter overflow in practice.** With `counter` 0 or 1 the limit is about 256 GiB per nonce, far above typical messages; the check matters for callers that choose a high initial counter.

## References

- [RFC 8439 -- ChaCha20 and Poly1305 for IETF Protocols](https://www.rfc-editor.org/rfc/rfc8439.html) -- the governing specification: the quarter round (2.1), the block function (2.3), the cipher (2.4) and the test vectors (2.3.2, 2.4.2, appendix A.1 and A.2).
- [D. J. Bernstein, ChaCha, a variant of Salsa20 (2008)](https://cr.yp.to/chacha/chacha-20080128.pdf) -- the original design.
