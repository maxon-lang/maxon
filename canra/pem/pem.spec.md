+++
name = "pem"
version = "1"
title = "PEM Textual Encoding (RFC 7468)"
status = "draft"
keywords = ["pem", "encoding", "certificates", "keys", "rfc7468", "base64"]
difficulty = "easy"

[api]
functions = ["decode", "encode"]
types = ["PemBlock", "PemError"]

[references]
items = [
  { label = "RFC 7468", url = "https://www.rfc-editor.org/rfc/rfc7468.html" },
  { label = "RFC 4648", url = "https://www.rfc-editor.org/rfc/rfc4648.html" },
  { label = "base64 (Canra spec)" },
]

[test-vectors]
inline = true
external = []
+++

## Overview

This spec defines the PEM textual encoding of RFC 7468: binary data, usually DER, wrapped in base64 between `-----BEGIN <label>-----` and `-----END <label>-----` lines.

- **decode** -- finds every PEM block in a text and returns its label and decoded bytes.
- **encode** -- writes one block in the strict RFC 7468 format.

Base64 is the `base64` spec's strict `decode` and `encode` (RFC 4648 section 4, standard alphabet, padded). This spec does not interpret the bytes: a `CERTIFICATE` block is returned as bytes, not parsed as X.509.

**Out of scope:** RFC 1421 encapsulated headers (`Proc-Type:`, `DEK-Info:` and similar, as written by legacy encrypted keys), which `decode` rejects as `InvalidBody`; decrypting legacy encrypted PEM; OpenPGP ASCII armor and its checksum line; the OpenSSH private key format's own framing; parsing the DER content; any interpretation of the label.

## Types

### PemBlock

```pseudocode
type PemBlock = {
  label: String,   // the text between "-----BEGIN " and "-----", e.g. "CERTIFICATE"
  bytes: Bytes,    // the decoded body
}
```

### PemError

```pseudocode
type PemError =
  | UnterminatedBlock   // a BEGIN line with no END line before the next BEGIN line or the end of the text
  | LabelMismatch       // the END line does not name the BEGIN line's label exactly
  | InvalidBody         // the body is not valid base64 once whitespace is removed
  | InvalidLabel        // encode only: the label is not a valid RFC 7468 label
```

### Labels

A valid label follows RFC 7468 section 3:

```pseudocode
labelchar = any printable ASCII character 0x21-0x7E except "-" (0x2D)
label     = empty | labelchar *( [ "-" | " " ] labelchar )
```

So a label is empty or starts and ends with a `labelchar`, and contains no two consecutive spaces or hyphens and no control characters. Labels are case-sensitive.

### Lines

The text is split into lines at each line feed (LF, `0x0A`), and one carriage return (CR, `0x0D`) at the end of a line is removed, so LF and CRLF line endings are both accepted. The last line of the text does not need a line break. "Blank" means space (`0x20`) or tab (`0x09`).

A line "is the boundary `-----BEGIN ` + label + `-----`" when, after removing trailing blanks, it starts with `-----BEGIN `, ends with `-----`, is at least 16 characters long, and the characters between those two parts form a valid label. Because a label never ends with `-`, the split is unambiguous. The same applies to `-----END ` (at least 14 characters).

## Functions

### decode

```pseudocode
decode(text: String) -> [PemBlock] | PemError
```

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `text` | String | yes | Text containing zero or more PEM blocks |

**Returns:** The blocks in the order they appear, or the first `PemError`. A text with no blocks gives an empty list.

**Algorithm:**

1. Split `text` into lines as described above, removing the line breaks.
2. Outside a block, for each line: remove trailing blanks. If the result is exactly `"-----BEGIN " + label + "-----"` for a valid `label`, start a block with that label. Otherwise the line is explanatory text and is ignored.
3. Inside a block, for each line:
   1. If the line starts with `"-----END "`: remove trailing blanks. If the result is exactly `"-----END " + label + "-----"` with the block's label, end the block. Otherwise return `LabelMismatch`.
   2. Else if the line starts with `"-----BEGIN "`, return `UnterminatedBlock`.
   3. Else the line is part of the body.
4. When a block ends: concatenate its body lines, remove every whitespace character (space, tab, CR, LF, vertical tab `0x0B`, form feed `0x0C`), and base64-decode the result strictly. If decoding fails, return `InvalidBody`. Append `PemBlock { label, bytes }` to the result.
5. If the text ends inside a block, return `UnterminatedBlock`.
6. Return the list of blocks.

### encode

```pseudocode
encode(label: String, data: Bytes) -> String | PemError
```

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `label` | String | yes | A valid RFC 7468 label, e.g. `"CERTIFICATE"`, `"PRIVATE KEY"`; MAY be empty |
| `data` | Bytes | yes | The bytes to encode, any length including zero |

**Returns:** The PEM text, or `InvalidLabel`.

**Algorithm:**

1. If `label` is not a valid label, return `InvalidLabel`.
2. `b64 = base64.encode(data)`.
3. Output `"-----BEGIN " + label + "-----"` and LF.
4. Output `b64` in lines of exactly 64 characters, the last line holding the remainder (1 to 64 characters), each followed by LF. Empty `data` gives no body lines.
5. Output `"-----END " + label + "-----"` and LF.

## Behavioral Contracts

### decode()

**MUST:**

1. MUST ignore text before the first block, between blocks and after the last block, whatever it contains, including lines that look like a malformed BEGIN line.
2. MUST accept LF and CRLF line endings, and a final line with no line break.
3. MUST accept base64 body lines of any length, including the whole body on one line or many short lines.
4. MUST ignore whitespace anywhere inside the body, including blank lines and blanks at the start or end of a line.
5. MUST ignore trailing blanks after the `-----` of a BEGIN or END line.
6. MUST require the END line's label to equal the BEGIN line's label exactly, case-sensitively; anything else on a line beginning `-----END ` is `LabelMismatch`. (RFC 7468 allows a parser to disregard a mismatch; this spec does not.)
7. MUST reject a body that is not strict base64 after whitespace removal as `InvalidBody`: a character outside the alphabet (this includes RFC 1421 header lines such as `Proc-Type: 4,ENCRYPTED`, whose `:` and `,` are outside it), a length that is not a multiple of 4, misplaced or excess padding, or non-zero pad bits.
8. MUST return `UnterminatedBlock` when a block has no END line before the next BEGIN line or the end of the text.
9. MUST return an empty list for a text with no blocks, including the empty text.
10. MUST return blocks in input order and keep duplicate labels.

### encode()

**MUST:**

1. MUST emit the strict format of RFC 7468 figure 3: BEGIN line, base64 lines of exactly 64 characters except a shorter last line, END line, each ending in a single LF, and nothing else.
2. MUST reject an invalid label with `InvalidLabel` rather than emit a block `decode` would not read back.
3. `decode(encode(label, data))` MUST be `[PemBlock { label, data }]`.

**SHOULD:**

1. Callers SHOULD use the labels of RFC 7468 section 4 (`CERTIFICATE`, `X509 CRL`, `CERTIFICATE REQUEST`, `PKCS7`, `CMS`, `PRIVATE KEY`, `ENCRYPTED PRIVATE KEY`, `ATTRIBUTE CERTIFICATE`, `PUBLIC KEY`) for those formats.

## Test Vectors

`text` and expected strings are JSON strings: `\n` is LF, `\r` is CR, `\t` is tab. A decode result is a list of objects with `label` and `bytes_hex` (lowercase hex of `bytes`). Encode takes `label` and `data_hex`.

### Sources

- `rfc7468-figure-N` vectors use the example blocks of RFC 7468: figure 6 (certificate), figure 7 (certificate with explanatory text), figure 11 (CMS), figure 12 (private key) and figure 15 (public key). Their expected bytes are the base64 decoding of the figure bodies.
- The `encode` vectors for figures 6, 12 and 15 reproduce the figures exactly, which confirms the 64-character line rule against the RFC.
- The other vectors rearrange those figures (CRLF, long lines, short lines, extra whitespace) or use small bodies whose bytes are stated in the vector.

### decode()

```test-vectors
{"id": "decode-001", "function": "decode", "input": {"args": {"text": "-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END PUBLIC KEY-----\n"}}, "expected": {"result": [{"label": "PUBLIC KEY", "bytes_hex": "3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829"}]}, "tags": ["rfc7468-figure-15"]}
{"id": "decode-002", "function": "decode", "input": {"args": {"text": "-----BEGIN CERTIFICATE-----\nMIICLDCCAdKgAwIBAgIBADAKBggqhkjOPQQDAjB9MQswCQYDVQQGEwJCRTEPMA0G\nA1UEChMGR251VExTMSUwIwYDVQQLExxHbnVUTFMgY2VydGlmaWNhdGUgYXV0aG9y\naXR5MQ8wDQYDVQQIEwZMZXV2ZW4xJTAjBgNVBAMTHEdudVRMUyBjZXJ0aWZpY2F0\nZSBhdXRob3JpdHkwHhcNMTEwNTIzMjAzODIxWhcNMTIxMjIyMDc0MTUxWjB9MQsw\nCQYDVQQGEwJCRTEPMA0GA1UEChMGR251VExTMSUwIwYDVQQLExxHbnVUTFMgY2Vy\ndGlmaWNhdGUgYXV0aG9yaXR5MQ8wDQYDVQQIEwZMZXV2ZW4xJTAjBgNVBAMTHEdu\ndVRMUyBjZXJ0aWZpY2F0ZSBhdXRob3JpdHkwWTATBgcqhkjOPQIBBggqhkjOPQMB\nBwNCAARS2I0jiuNn14Y2sSALCX3IybqiIJUvxUpj+oNfzngvj/Niyv2394BWnW4X\nuQ4RTEiywK87WRcWMGgJB5kX/t2no0MwQTAPBgNVHRMBAf8EBTADAQH/MA8GA1Ud\nDwEB/wQFAwMHBgAwHQYDVR0OBBYEFPC0gf6YEr+1KLlkQAPLzB9mTigDMAoGCCqG\nSM49BAMCA0gAMEUCIDGuwD1KPyG+hRf88MeyMQcqOFZD0TbVleF+UsAGQ4enAiEA\nl4wOuDwKQa+upc8GftXE2C//4mKANBC6It01gUaTIpo=\n-----END CERTIFICATE-----\n"}}, "expected": {"result": [{"label": "CERTIFICATE", "bytes_hex": "3082022c308201d2a003020102020100300a06082a8648ce3d040302307d310b3009060355040613024245310f300d060355040a1306476e75544c5331253023060355040b131c476e75544c5320636572746966696361746520617574686f72697479310f300d060355040813064c657576656e312530230603550403131c476e75544c5320636572746966696361746520617574686f72697479301e170d3131303532333230333832315a170d3132313232323037343135315a307d310b3009060355040613024245310f300d060355040a1306476e75544c5331253023060355040b131c476e75544c5320636572746966696361746520617574686f72697479310f300d060355040813064c657576656e312530230603550403131c476e75544c5320636572746966696361746520617574686f726974793059301306072a8648ce3d020106082a8648ce3d0301070342000452d88d238ae367d78636b1200b097dc8c9baa220952fc54a63fa835fce782f8ff362cafdb7f780569d6e17b90e114c48b2c0af3b591716306809079917fedda7a3433041300f0603551d130101ff040530030101ff300f0603551d0f0101ff04050303070600301d0603551d0e04160414f0b481fe9812bfb528b9644003cbcc1f664e2803300a06082a8648ce3d0403020348003045022031aec03d4a3f21be8517fcf0c7b231072a385643d136d595e17e52c0064387a7022100978c0eb83c0a41afaea5cf067ed5c4d82fffe262803410ba22dd35814693229a"}]}, "tags": ["rfc7468-figure-6"]}
{"id": "decode-003", "function": "decode", "input": {"args": {"text": "Subject: CN=Atlantis\nIssuer: CN=Atlantis\nValidity: from 7/9/2012 3:10:38 AM UTC to 7/9/2013 3:10:37 AM UTC\n-----BEGIN CERTIFICATE-----\nMIIBmTCCAUegAwIBAgIBKjAJBgUrDgMCHQUAMBMxETAPBgNVBAMTCEF0bGFudGlz\nMB4XDTEyMDcwOTAzMTAzOFoXDTEzMDcwOTAzMTAzN1owEzERMA8GA1UEAxMIQXRs\nYW50aXMwXDANBgkqhkiG9w0BAQEFAANLADBIAkEAu+BXo+miabDIHHx+yquqzqNh\nRyn/XtkJIIHVcYtHvIX+S1x5ErgMoHehycpoxbErZmVR4GCq1S2diNmRFZCRtQID\nAQABo4GJMIGGMAwGA1UdEwEB/wQCMAAwIAYDVR0EAQH/BBYwFDAOMAwGCisGAQQB\ngjcCARUDAgeAMB0GA1UdJQQWMBQGCCsGAQUFBwMCBggrBgEFBQcDAzA1BgNVHQEE\nLjAsgBA0jOnSSuIHYmnVryHAdywMoRUwEzERMA8GA1UEAxMIQXRsYW50aXOCASow\nCQYFKw4DAh0FAANBAKi6HRBaNEL5R0n56nvfclQNaXiDT174uf+lojzA4lhVInc0\nILwpnZ1izL4MlI9eCSHhVQBHEp2uQdXJB+d5Byg=\n-----END CERTIFICATE-----\n"}}, "expected": {"result": [{"label": "CERTIFICATE", "bytes_hex": "3082019930820147a00302010202012a300906052b0e03021d050030133111300f0603550403130841746c616e746973301e170d3132303730393033313033385a170d3133303730393033313033375a30133111300f0603550403130841746c616e746973305c300d06092a864886f70d0101010500034b003048024100bbe057a3e9a269b0c81c7c7ecaabaacea3614729ff5ed9092081d5718b47bc85fe4b5c7912b80ca077a1c9ca68c5b12b666551e060aad52d9d88d991159091b50203010001a38189308186300c0603551d130101ff0402300030200603551d040101ff04163014300e300c060a2b06010401823702011503020780301d0603551d250416301406082b0601050507030206082b0601050507030330350603551d01042e302c8010348ce9d24ae2076269d5af21c0772c0ca11530133111300f0603550403130841746c616e74697382012a300906052b0e03021d0500034100a8ba1d105a3442f94749f9ea7bdf72540d6978834f5ef8b9ffa5a23cc0e2585522773420bc299d9d62ccbe0c948f5e0921e1550047129dae41d5c907e7790728"}]}, "tags": ["rfc7468-figure-7", "explanatory-text"]}
{"id": "decode-004", "function": "decode", "input": {"args": {"text": "-----BEGIN PUBLIC KEY-----\r\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\r\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\r\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\r\n-----END PUBLIC KEY-----\r\n"}}, "expected": {"result": [{"label": "PUBLIC KEY", "bytes_hex": "3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829"}]}, "tags": ["crlf"]}
{"id": "decode-005", "function": "decode", "input": {"args": {"text": "-----BEGIN CMS-----\nMIGDBgsqhkiG9w0BCRABCaB0MHICAQAwDQYLKoZIhvcNAQkQAwgwXgYJKoZIhvcN\nAQcBoFEET3icc87PK0nNK9ENqSxItVIoSa0o0S/ISczMs1ZIzkgsKk4tsQ0N1nUM\ndvb05OXi5XLPLEtViMwvLVLwSE0sKlFIVHAqSk3MBkkBAJv0Fx0=\n-----END CMS-----"}}, "expected": {"result": [{"label": "CMS", "bytes_hex": "308183060b2a864886f70d0109100109a0743072020100300d060b2a864886f70d0109100308305e06092a864886f70d010701a051044f789c73cecf2b49cd2bd10da92c48b5522849ad28d12fc849ccccb35648ce482c2a4e2db10d0dd6750c76f6f4e4e5e2e572cf2c4b5588cc2f2d52f0484d2c2a514854702a4a4dcc064901009bf4171d"}]}, "tags": ["no-final-line-break", "rfc7468-figure-11"]}
{"id": "decode-006", "function": "decode", "input": {"args": {"text": "Two keys follow.\n-----BEGIN PRIVATE KEY-----\nMIGEAgEAMBAGByqGSM49AgEGBSuBBAAKBG0wawIBAQQgVcB/UNPxalR9zDYAjQIf\njojUDiQuGnSJrFEEzZPT/92hRANCAASc7UJtgnF/abqWM60T3XNJEzBv5ez9TdwK\nH0M6xpM2q+53wmsN/eYLdgtjgBd3DBmHtPilCkiFICXyaA8z9LkJ\n-----END PRIVATE KEY-----\n\nand the matching public key:\n-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END PUBLIC KEY-----\ntrailing text\n"}}, "expected": {"result": [{"label": "PRIVATE KEY", "bytes_hex": "308184020100301006072a8648ce3d020106052b8104000a046d306b020101042055c07f50d3f16a547dcc36008d021f8e88d40e242e1a7489ac5104cd93d3ffdda144034200049ced426d82717f69ba9633ad13dd734913306fe5ecfd4ddc0a1f433ac69336abee77c26b0dfde60b760b638017770c1987b4f8a50a48852025f2680f33f4b909"}, {"label": "PUBLIC KEY", "bytes_hex": "3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829"}]}, "tags": ["multiple-blocks", "different-labels", "explanatory-text"]}
{"id": "decode-007", "function": "decode", "input": {"args": {"text": "-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLekLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4UNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END PUBLIC KEY-----\n"}}, "expected": {"result": [{"label": "PUBLIC KEY", "bytes_hex": "3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829"}]}, "tags": ["long-line"]}
{"id": "decode-008", "function": "decode", "input": {"args": {"text": "-----BEGIN PUBLIC KEY-----\nMHYwEAY\nHKoZIzj\n0CAQYFK\n4EEACID\nYgAEn1L\nlwLN/KB\nYQRVH6H\nfIMTzfE\nqJOVztL\nekLchp2\nhi78cCa\nMY81FBl\nYs8J9l7\nkrc+M4a\nBeCGYFj\nba+hiXt\ntJWPL7y\ndlE+5UG\n4UNkn3E\nos8EiZB\nyi9DVsy\nfy9eejh\n+8AXgp\n-----END PUBLIC KEY-----\n"}}, "expected": {"result": [{"label": "PUBLIC KEY", "bytes_hex": "3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829"}]}, "tags": ["short-lines"]}
{"id": "decode-009", "function": "decode", "input": {"args": {"text": "-----BEGIN PUBLIC KEY-----\n  MHYwEAYHKoZIzj0CAQYFK4EEACIDYg \tAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\n\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U   \n\tNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END PUBLIC KEY-----\n"}}, "expected": {"result": [{"label": "PUBLIC KEY", "bytes_hex": "3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829"}]}, "tags": ["whitespace-in-body"]}
{"id": "decode-010", "function": "decode", "input": {"args": {"text": "-----BEGIN PUBLIC KEY----- \t\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END PUBLIC KEY-----  \n"}}, "expected": {"result": [{"label": "PUBLIC KEY", "bytes_hex": "3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829"}]}, "tags": ["trailing-blanks-on-boundary"]}
{"id": "decode-011", "function": "decode", "input": {"args": {"text": "-----BEGIN X509 CRL-----\n-----END X509 CRL-----\n"}}, "expected": {"result": [{"label": "X509 CRL", "bytes_hex": ""}]}, "tags": ["empty-body", "edge"]}
{"id": "decode-012", "function": "decode", "input": {"args": {"text": ""}}, "expected": {"result": []}, "tags": ["empty-input", "zero-blocks", "edge"]}
{"id": "decode-013", "function": "decode", "input": {"args": {"text": "Just some text.\nNo PEM here.\n"}}, "expected": {"result": []}, "tags": ["zero-blocks", "edge"]}
{"id": "decode-014", "function": "decode", "input": {"args": {"text": "-----BEGIN -----\nAAEC\n-----END -----\n"}}, "expected": {"result": [{"label": "", "bytes_hex": "000102"}]}, "tags": ["empty-label", "edge"]}
{"id": "decode-015", "function": "decode", "input": {"args": {"text": "-----BEGIN CERTIFICATE----- trailing\nAAEC\n-----END CERTIFICATE-----\n"}}, "expected": {"result": []}, "tags": ["malformed-begin-is-text", "edge"]}
{"id": "decode-016", "function": "decode", "input": {"args": {"text": "-----END X-----\n-----BEGIN DATA-----\nAAEC\n-----END DATA-----\n"}}, "expected": {"result": [{"label": "DATA", "bytes_hex": "000102"}]}, "tags": ["stray-end-is-text", "edge"]}
{"id": "decode-017", "function": "decode", "input": {"args": {"text": "-----BEGIN ROUND TRIP-----\nAAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4v\nMDEyMzQ1Njc4OTo7PD0+P0BBQkNERUZHSElKS0xNTk9QUVJTVFVWV1hZWltcXV5f\nYGFiYw==\n-----END ROUND TRIP-----\n"}}, "expected": {"result": [{"label": "ROUND TRIP", "bytes_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f60616263"}]}, "tags": ["round-trip"]}
```

### decode() errors

```test-vectors
{"id": "decode-err-001", "function": "decode", "input": {"args": {"text": "-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n"}}, "expected": {"error": "UnterminatedBlock"}, "tags": ["missing-end"]}
{"id": "decode-err-002", "function": "decode", "input": {"args": {"text": "-----BEGIN PUBLIC KEY-----"}}, "expected": {"error": "UnterminatedBlock"}, "tags": ["missing-end", "begin-only"]}
{"id": "decode-err-003", "function": "decode", "input": {"args": {"text": "-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----BEGIN PRIVATE KEY-----\nMIGEAgEAMBAGByqGSM49AgEGBSuBBAAKBG0wawIBAQQgVcB/UNPxalR9zDYAjQIf\njojUDiQuGnSJrFEEzZPT/92hRANCAASc7UJtgnF/abqWM60T3XNJEzBv5ez9TdwK\nH0M6xpM2q+53wmsN/eYLdgtjgBd3DBmHtPilCkiFICXyaA8z9LkJ\n-----END PRIVATE KEY-----\n"}}, "expected": {"error": "UnterminatedBlock"}, "tags": ["begin-inside-block"]}
{"id": "decode-err-004", "function": "decode", "input": {"args": {"text": "-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END PRIVATE KEY-----\n"}}, "expected": {"error": "LabelMismatch"}, "tags": ["label-mismatch"]}
{"id": "decode-err-005", "function": "decode", "input": {"args": {"text": "-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END public key-----\n"}}, "expected": {"error": "LabelMismatch"}, "tags": ["label-mismatch", "case-sensitive"]}
{"id": "decode-err-006", "function": "decode", "input": {"args": {"text": "-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END PUBLIC KEY----\n"}}, "expected": {"error": "LabelMismatch"}, "tags": ["malformed-end"]}
{"id": "decode-err-007", "function": "decode", "input": {"args": {"text": "-----BEGIN RSA PRIVATE KEY-----\nProc-Type: 4,ENCRYPTED\nDEK-Info: DES-EDE3-CBC,F8C3A3A8B1D5A4F0\n\nAAEC\n-----END RSA PRIVATE KEY-----\n"}}, "expected": {"error": "InvalidBody"}, "tags": ["encapsulated-headers", "rfc1421"]}
{"id": "decode-err-008", "function": "decode", "input": {"args": {"text": "-----BEGIN DATA-----\nAA*C\n-----END DATA-----\n"}}, "expected": {"error": "InvalidBody"}, "tags": ["invalid-symbol"]}
{"id": "decode-err-009", "function": "decode", "input": {"args": {"text": "-----BEGIN DATA-----\nAAE\n-----END DATA-----\n"}}, "expected": {"error": "InvalidBody"}, "tags": ["invalid-length"]}
{"id": "decode-err-010", "function": "decode", "input": {"args": {"text": "-----BEGIN DATA-----\nAA==\nAAEC\n-----END DATA-----\n"}}, "expected": {"error": "InvalidBody"}, "tags": ["padding-in-middle"]}
{"id": "decode-err-011", "function": "decode", "input": {"args": {"text": "-----BEGIN DATA-----\nAB==\n-----END DATA-----\n"}}, "expected": {"error": "InvalidBody"}, "tags": ["non-canonical"]}
{"id": "decode-err-012", "function": "decode", "input": {"args": {"text": "-----BEGIN DATA-----\nAA-C\n-----END DATA-----\n"}}, "expected": {"error": "InvalidBody"}, "tags": ["url-safe-alphabet"]}
```

### encode()

```test-vectors
{"id": "encode-001", "function": "encode", "input": {"args": {"label": "PUBLIC KEY", "data_hex": "3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829"}}, "expected": {"result": "-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END PUBLIC KEY-----\n"}, "tags": ["rfc7468-figure-15"]}
{"id": "encode-002", "function": "encode", "input": {"args": {"label": "CERTIFICATE", "data_hex": "3082022c308201d2a003020102020100300a06082a8648ce3d040302307d310b3009060355040613024245310f300d060355040a1306476e75544c5331253023060355040b131c476e75544c5320636572746966696361746520617574686f72697479310f300d060355040813064c657576656e312530230603550403131c476e75544c5320636572746966696361746520617574686f72697479301e170d3131303532333230333832315a170d3132313232323037343135315a307d310b3009060355040613024245310f300d060355040a1306476e75544c5331253023060355040b131c476e75544c5320636572746966696361746520617574686f72697479310f300d060355040813064c657576656e312530230603550403131c476e75544c5320636572746966696361746520617574686f726974793059301306072a8648ce3d020106082a8648ce3d0301070342000452d88d238ae367d78636b1200b097dc8c9baa220952fc54a63fa835fce782f8ff362cafdb7f780569d6e17b90e114c48b2c0af3b591716306809079917fedda7a3433041300f0603551d130101ff040530030101ff300f0603551d0f0101ff04050303070600301d0603551d0e04160414f0b481fe9812bfb528b9644003cbcc1f664e2803300a06082a8648ce3d0403020348003045022031aec03d4a3f21be8517fcf0c7b231072a385643d136d595e17e52c0064387a7022100978c0eb83c0a41afaea5cf067ed5c4d82fffe262803410ba22dd35814693229a"}}, "expected": {"result": "-----BEGIN CERTIFICATE-----\nMIICLDCCAdKgAwIBAgIBADAKBggqhkjOPQQDAjB9MQswCQYDVQQGEwJCRTEPMA0G\nA1UEChMGR251VExTMSUwIwYDVQQLExxHbnVUTFMgY2VydGlmaWNhdGUgYXV0aG9y\naXR5MQ8wDQYDVQQIEwZMZXV2ZW4xJTAjBgNVBAMTHEdudVRMUyBjZXJ0aWZpY2F0\nZSBhdXRob3JpdHkwHhcNMTEwNTIzMjAzODIxWhcNMTIxMjIyMDc0MTUxWjB9MQsw\nCQYDVQQGEwJCRTEPMA0GA1UEChMGR251VExTMSUwIwYDVQQLExxHbnVUTFMgY2Vy\ndGlmaWNhdGUgYXV0aG9yaXR5MQ8wDQYDVQQIEwZMZXV2ZW4xJTAjBgNVBAMTHEdu\ndVRMUyBjZXJ0aWZpY2F0ZSBhdXRob3JpdHkwWTATBgcqhkjOPQIBBggqhkjOPQMB\nBwNCAARS2I0jiuNn14Y2sSALCX3IybqiIJUvxUpj+oNfzngvj/Niyv2394BWnW4X\nuQ4RTEiywK87WRcWMGgJB5kX/t2no0MwQTAPBgNVHRMBAf8EBTADAQH/MA8GA1Ud\nDwEB/wQFAwMHBgAwHQYDVR0OBBYEFPC0gf6YEr+1KLlkQAPLzB9mTigDMAoGCCqG\nSM49BAMCA0gAMEUCIDGuwD1KPyG+hRf88MeyMQcqOFZD0TbVleF+UsAGQ4enAiEA\nl4wOuDwKQa+upc8GftXE2C//4mKANBC6It01gUaTIpo=\n-----END CERTIFICATE-----\n"}, "tags": ["rfc7468-figure-6"]}
{"id": "encode-003", "function": "encode", "input": {"args": {"label": "PRIVATE KEY", "data_hex": "308184020100301006072a8648ce3d020106052b8104000a046d306b020101042055c07f50d3f16a547dcc36008d021f8e88d40e242e1a7489ac5104cd93d3ffdda144034200049ced426d82717f69ba9633ad13dd734913306fe5ecfd4ddc0a1f433ac69336abee77c26b0dfde60b760b638017770c1987b4f8a50a48852025f2680f33f4b909"}}, "expected": {"result": "-----BEGIN PRIVATE KEY-----\nMIGEAgEAMBAGByqGSM49AgEGBSuBBAAKBG0wawIBAQQgVcB/UNPxalR9zDYAjQIf\njojUDiQuGnSJrFEEzZPT/92hRANCAASc7UJtgnF/abqWM60T3XNJEzBv5ez9TdwK\nH0M6xpM2q+53wmsN/eYLdgtjgBd3DBmHtPilCkiFICXyaA8z9LkJ\n-----END PRIVATE KEY-----\n"}, "tags": ["rfc7468-figure-12"]}
{"id": "encode-004", "function": "encode", "input": {"args": {"label": "X509 CRL", "data_hex": ""}}, "expected": {"result": "-----BEGIN X509 CRL-----\n-----END X509 CRL-----\n"}, "tags": ["empty", "edge"]}
{"id": "encode-005", "function": "encode", "input": {"args": {"label": "DATA", "data_hex": "00"}}, "expected": {"result": "-----BEGIN DATA-----\nAA==\n-----END DATA-----\n"}, "tags": ["one-byte"]}
{"id": "encode-006", "function": "encode", "input": {"args": {"label": "DATA", "data_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f"}}, "expected": {"result": "-----BEGIN DATA-----\nAAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4v\n-----END DATA-----\n"}, "tags": ["one-full-line", "edge"]}
{"id": "encode-007", "function": "encode", "input": {"args": {"label": "DATA", "data_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f30"}}, "expected": {"result": "-----BEGIN DATA-----\nAAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4v\nMA==\n-----END DATA-----\n"}, "tags": ["full-line-plus-one", "edge"]}
{"id": "encode-008", "function": "encode", "input": {"args": {"label": "DATA", "data_hex": "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f"}}, "expected": {"result": "-----BEGIN DATA-----\nAAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4v\nMDEyMzQ1Njc4OTo7PD0+P0BBQkNERUZHSElKS0xNTk9QUVJTVFVWV1hZWltcXV5f\n-----END DATA-----\n"}, "tags": ["two-full-lines", "edge"]}
{"id": "encode-009", "function": "encode", "input": {"args": {"label": "", "data_hex": "000102"}}, "expected": {"result": "-----BEGIN -----\nAAEC\n-----END -----\n"}, "tags": ["empty-label", "edge"]}
{"id": "encode-010", "function": "encode", "input": {"args": {"label": "A-B", "data_hex": "00"}}, "expected": {"result": "-----BEGIN A-B-----\nAA==\n-----END A-B-----\n"}, "tags": ["hyphen-in-label", "edge"]}
```

### encode() errors

```test-vectors
{"id": "encode-err-001", "function": "encode", "input": {"args": {"label": "BAD--LABEL", "data_hex": "00"}}, "expected": {"error": "InvalidLabel"}, "tags": ["consecutive-hyphens"]}
{"id": "encode-err-002", "function": "encode", "input": {"args": {"label": " LEADING", "data_hex": "00"}}, "expected": {"error": "InvalidLabel"}, "tags": ["leading-space"]}
{"id": "encode-err-003", "function": "encode", "input": {"args": {"label": "TRAILING-", "data_hex": "00"}}, "expected": {"error": "InvalidLabel"}, "tags": ["trailing-hyphen"]}
{"id": "encode-err-004", "function": "encode", "input": {"args": {"label": "TWO  SPACES", "data_hex": "00"}}, "expected": {"error": "InvalidLabel"}, "tags": ["consecutive-spaces"]}
{"id": "encode-err-005", "function": "encode", "input": {"args": {"label": "LINE\nBREAK", "data_hex": "00"}}, "expected": {"error": "InvalidLabel"}, "tags": ["control-character"]}
{"id": "encode-err-006", "function": "encode", "input": {"args": {"label": "TAB\tLABEL", "data_hex": "00"}}, "expected": {"error": "InvalidLabel"}, "tags": ["control-character"]}
```

## Edge Cases

- **Two blocks with different labels.** Both are returned, in order, each with its own label (`decode-006`).
- **Zero blocks.** Plain text and the empty string give `[]`, not an error.
- **Empty body.** `-----BEGIN X509 CRL-----` followed directly by `-----END X509 CRL-----` is a block with empty bytes.
- **Empty label.** `-----BEGIN -----` is a valid boundary with label `""`.
- **Hyphen inside a label.** `A-B` is a valid label: a single `-` between two label characters is allowed, so `encode` accepts it.
- **Round trip.** `decode-017` is exactly what `encode` writes for a 100-byte body, three body lines long, and decodes to that label and those bytes.
- **Malformed BEGIN line.** `-----BEGIN CERTIFICATE----- trailing` is not a boundary, so it and the lines after it are explanatory text until a valid BEGIN line; a stray END line outside a block is also text. A text containing only such lines gives `[]`.
- **Malformed END line.** Inside a block, `-----END PUBLIC KEY----` (four hyphens) is `LabelMismatch`, because the line begins `-----END ` but is not the matching boundary.
- **Missing END before the next BEGIN.** `UnterminatedBlock`, not a merged body.
- **Encrypted legacy keys.** `Proc-Type: 4,ENCRYPTED` and `DEK-Info:` lines are `InvalidBody`.
- **Padding mid-body.** Two base64 strings concatenated inside one block (`AA==` then `AAEC`) are `InvalidBody`.
- **Encode boundaries.** 48 bytes make exactly one 64-character line; 49 bytes make a 64-character line and a 4-character line; 0 bytes make no body line.

## Implementation Notes

- **Reuse the strict base64 decoder.** Remove whitespace from the body first, then decode it in one call; do not decode line by line, which mishandles lines whose length is not a multiple of 4.
- **Scan, do not regex the whole text.** A line-oriented scanner with a two-state machine (outside, inside) is linear and gives exact error positions. A single multi-line regex tends to be quadratic on hostile input and to merge blocks.
- **Bounds.** A PEM file can be large (a CA bundle holds hundreds of certificates). Keep decoding linear in the input size.
- **Label check on encode.** Reject a label containing a line break, `--`, a leading or trailing space or hyphen, or a control character; otherwise a caller-supplied label could inject an extra boundary.
- **Secrets.** Private keys pass through `decode`; overwrite intermediate buffers when the bytes are secret.

## References

- [RFC 7468 -- Textual Encodings of PKIX, PKCS, and CMS Structures](https://www.rfc-editor.org/rfc/rfc7468.html) -- the governing specification: section 2 (general rules), section 3 (ABNF, including the strict format of figure 3), sections 5 to 13 (labels and examples).
- [RFC 4648 -- The Base16, Base32, and Base64 Data Encodings](https://www.rfc-editor.org/rfc/rfc4648.html) -- the base64 alphabet and padding.
- [RFC 1421 -- Privacy Enhancement for Internet Electronic Mail](https://www.rfc-editor.org/rfc/rfc1421.html) -- the original PEM, whose encapsulated headers are out of scope.
