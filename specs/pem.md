---
feature: pem
status: stable
keywords: [pem, Pem, PemBlock, PemError, rfc7468, certificate, private key, base64, encoding]
category: stdlib
---

# `Pem` — RFC 7468 textual encoding

## Documentation

| Call | Meaning |
|---|---|
| `Pem.decode(text)` | every `-----BEGIN <label>-----` … `-----END <label>-----` block in `text`, in order, as an `Array with PemBlock` |
| `Pem.encode(label, data: data)` | one block in the strict RFC 7468 form: the BEGIN line, base64 lines of exactly 64 characters but the last, the END line, each ending in one LF |
| `block.label()` | the text between `-----BEGIN ` and `-----` |
| `block.bytes()` | the block's decoded body |

`decode` ignores text outside blocks, accepts LF and CRLF line endings and a last line without a line break,
ignores trailing blanks on a boundary line and whitespace anywhere in a body, and decodes the body with the
strict `Base64.decode`. It throws a `PemError`:

| Error | When |
|---|---|
| `unterminatedBlock` | a BEGIN line has no END line before the next BEGIN line or the end of the text |
| `labelMismatch` | a line inside a block begins `-----END ` but is not the END line of the block's label, exactly |
| `invalidBody` | the body, with its whitespace removed, is not strict base64 |
| `invalidLabel` | `encode` only: the label is not an RFC 7468 label |

The vectors are the RFC 7468 example blocks (figures 6, 7, 11, 12 and 15), rearranged line endings, line
lengths and whitespace, and small bodies. A decode prints a vector's id with its block count, then one line per
block with its label and bytes.

## Tests

<!-- test: pem.decode -->
The RFC 7468 figures, CRLF line endings, no final line break, two blocks with text around them, one long body line, many short ones, whitespace in the body and after the boundaries, an empty body, an empty label, no blocks, and a malformed BEGIN line and a stray END line read as text.
```maxon
function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure PemError) returns String
	return match failure 'name'
		unterminatedBlock gives "unterminatedBlock"
		labelMismatch gives "labelMismatch"
		invalidBody gives "invalidBody"
		invalidLabel gives "invalidLabel"
	end 'name'
end 'errorName'

function report(id String, text String)
	if let blocks = try Pem.decode(text) 'decoded'
		print("{id} {blocks.count()}\n")

		for block in blocks 'eachBlock'
			print("{id} \"{block.label()}\" [{toHex(block.bytes())}]\n")
		end 'eachBlock'
	end 'decoded' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("decode-001", text: "-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END PUBLIC KEY-----\n")
	report("decode-002", text: "-----BEGIN CERTIFICATE-----\nMIICLDCCAdKgAwIBAgIBADAKBggqhkjOPQQDAjB9MQswCQYDVQQGEwJCRTEPMA0G\nA1UEChMGR251VExTMSUwIwYDVQQLExxHbnVUTFMgY2VydGlmaWNhdGUgYXV0aG9y\naXR5MQ8wDQYDVQQIEwZMZXV2ZW4xJTAjBgNVBAMTHEdudVRMUyBjZXJ0aWZpY2F0\nZSBhdXRob3JpdHkwHhcNMTEwNTIzMjAzODIxWhcNMTIxMjIyMDc0MTUxWjB9MQsw\nCQYDVQQGEwJCRTEPMA0GA1UEChMGR251VExTMSUwIwYDVQQLExxHbnVUTFMgY2Vy\ndGlmaWNhdGUgYXV0aG9yaXR5MQ8wDQYDVQQIEwZMZXV2ZW4xJTAjBgNVBAMTHEdu\ndVRMUyBjZXJ0aWZpY2F0ZSBhdXRob3JpdHkwWTATBgcqhkjOPQIBBggqhkjOPQMB\nBwNCAARS2I0jiuNn14Y2sSALCX3IybqiIJUvxUpj+oNfzngvj/Niyv2394BWnW4X\nuQ4RTEiywK87WRcWMGgJB5kX/t2no0MwQTAPBgNVHRMBAf8EBTADAQH/MA8GA1Ud\nDwEB/wQFAwMHBgAwHQYDVR0OBBYEFPC0gf6YEr+1KLlkQAPLzB9mTigDMAoGCCqG\nSM49BAMCA0gAMEUCIDGuwD1KPyG+hRf88MeyMQcqOFZD0TbVleF+UsAGQ4enAiEA\nl4wOuDwKQa+upc8GftXE2C//4mKANBC6It01gUaTIpo=\n-----END CERTIFICATE-----\n")
	report("decode-003", text: "Subject: CN=Atlantis\nIssuer: CN=Atlantis\nValidity: from 7/9/2012 3:10:38 AM UTC to 7/9/2013 3:10:37 AM UTC\n-----BEGIN CERTIFICATE-----\nMIIBmTCCAUegAwIBAgIBKjAJBgUrDgMCHQUAMBMxETAPBgNVBAMTCEF0bGFudGlz\nMB4XDTEyMDcwOTAzMTAzOFoXDTEzMDcwOTAzMTAzN1owEzERMA8GA1UEAxMIQXRs\nYW50aXMwXDANBgkqhkiG9w0BAQEFAANLADBIAkEAu+BXo+miabDIHHx+yquqzqNh\nRyn/XtkJIIHVcYtHvIX+S1x5ErgMoHehycpoxbErZmVR4GCq1S2diNmRFZCRtQID\nAQABo4GJMIGGMAwGA1UdEwEB/wQCMAAwIAYDVR0EAQH/BBYwFDAOMAwGCisGAQQB\ngjcCARUDAgeAMB0GA1UdJQQWMBQGCCsGAQUFBwMCBggrBgEFBQcDAzA1BgNVHQEE\nLjAsgBA0jOnSSuIHYmnVryHAdywMoRUwEzERMA8GA1UEAxMIQXRsYW50aXOCASow\nCQYFKw4DAh0FAANBAKi6HRBaNEL5R0n56nvfclQNaXiDT174uf+lojzA4lhVInc0\nILwpnZ1izL4MlI9eCSHhVQBHEp2uQdXJB+d5Byg=\n-----END CERTIFICATE-----\n")
	report("decode-004", text: "-----BEGIN PUBLIC KEY-----\r\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\r\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\r\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\r\n-----END PUBLIC KEY-----\r\n")
	report("decode-005", text: "-----BEGIN CMS-----\nMIGDBgsqhkiG9w0BCRABCaB0MHICAQAwDQYLKoZIhvcNAQkQAwgwXgYJKoZIhvcN\nAQcBoFEET3icc87PK0nNK9ENqSxItVIoSa0o0S/ISczMs1ZIzkgsKk4tsQ0N1nUM\ndvb05OXi5XLPLEtViMwvLVLwSE0sKlFIVHAqSk3MBkkBAJv0Fx0=\n-----END CMS-----")
	report("decode-006", text: "Two keys follow.\n-----BEGIN PRIVATE KEY-----\nMIGEAgEAMBAGByqGSM49AgEGBSuBBAAKBG0wawIBAQQgVcB/UNPxalR9zDYAjQIf\njojUDiQuGnSJrFEEzZPT/92hRANCAASc7UJtgnF/abqWM60T3XNJEzBv5ez9TdwK\nH0M6xpM2q+53wmsN/eYLdgtjgBd3DBmHtPilCkiFICXyaA8z9LkJ\n-----END PRIVATE KEY-----\n\nand the matching public key:\n-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END PUBLIC KEY-----\ntrailing text\n")
	report("decode-007", text: "-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLekLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4UNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END PUBLIC KEY-----\n")
	report("decode-008", text: "-----BEGIN PUBLIC KEY-----\nMHYwEAY\nHKoZIzj\n0CAQYFK\n4EEACID\nYgAEn1L\nlwLN/KB\nYQRVH6H\nfIMTzfE\nqJOVztL\nekLchp2\nhi78cCa\nMY81FBl\nYs8J9l7\nkrc+M4a\nBeCGYFj\nba+hiXt\ntJWPL7y\ndlE+5UG\n4UNkn3E\nos8EiZB\nyi9DVsy\nfy9eejh\n+8AXgp\n-----END PUBLIC KEY-----\n")
	report("decode-009", text: "-----BEGIN PUBLIC KEY-----\n  MHYwEAYHKoZIzj0CAQYFK4EEACIDYg \tAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\n\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U   \n\tNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END PUBLIC KEY-----\n")
	report("decode-010", text: "-----BEGIN PUBLIC KEY----- \t\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END PUBLIC KEY-----  \n")
	report("decode-011", text: "-----BEGIN X509 CRL-----\n-----END X509 CRL-----\n")
	report("decode-012", text: "")
	report("decode-013", text: "Just some text.\nNo PEM here.\n")
	report("decode-014", text: "-----BEGIN -----\nAAEC\n-----END -----\n")
	report("decode-015", text: "-----BEGIN CERTIFICATE----- trailing\nAAEC\n-----END CERTIFICATE-----\n")
	report("decode-016", text: "-----END X-----\n-----BEGIN DATA-----\nAAEC\n-----END DATA-----\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
decode-001 1
decode-001 "PUBLIC KEY" [3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829]
decode-002 1
decode-002 "CERTIFICATE" [3082022c308201d2a003020102020100300a06082a8648ce3d040302307d310b3009060355040613024245310f300d060355040a1306476e75544c5331253023060355040b131c476e75544c5320636572746966696361746520617574686f72697479310f300d060355040813064c657576656e312530230603550403131c476e75544c5320636572746966696361746520617574686f72697479301e170d3131303532333230333832315a170d3132313232323037343135315a307d310b3009060355040613024245310f300d060355040a1306476e75544c5331253023060355040b131c476e75544c5320636572746966696361746520617574686f72697479310f300d060355040813064c657576656e312530230603550403131c476e75544c5320636572746966696361746520617574686f726974793059301306072a8648ce3d020106082a8648ce3d0301070342000452d88d238ae367d78636b1200b097dc8c9baa220952fc54a63fa835fce782f8ff362cafdb7f780569d6e17b90e114c48b2c0af3b591716306809079917fedda7a3433041300f0603551d130101ff040530030101ff300f0603551d0f0101ff04050303070600301d0603551d0e04160414f0b481fe9812bfb528b9644003cbcc1f664e2803300a06082a8648ce3d0403020348003045022031aec03d4a3f21be8517fcf0c7b231072a385643d136d595e17e52c0064387a7022100978c0eb83c0a41afaea5cf067ed5c4d82fffe262803410ba22dd35814693229a]
decode-003 1
decode-003 "CERTIFICATE" [3082019930820147a00302010202012a300906052b0e03021d050030133111300f0603550403130841746c616e746973301e170d3132303730393033313033385a170d3133303730393033313033375a30133111300f0603550403130841746c616e746973305c300d06092a864886f70d0101010500034b003048024100bbe057a3e9a269b0c81c7c7ecaabaacea3614729ff5ed9092081d5718b47bc85fe4b5c7912b80ca077a1c9ca68c5b12b666551e060aad52d9d88d991159091b50203010001a38189308186300c0603551d130101ff0402300030200603551d040101ff04163014300e300c060a2b06010401823702011503020780301d0603551d250416301406082b0601050507030206082b0601050507030330350603551d01042e302c8010348ce9d24ae2076269d5af21c0772c0ca11530133111300f0603550403130841746c616e74697382012a300906052b0e03021d0500034100a8ba1d105a3442f94749f9ea7bdf72540d6978834f5ef8b9ffa5a23cc0e2585522773420bc299d9d62ccbe0c948f5e0921e1550047129dae41d5c907e7790728]
decode-004 1
decode-004 "PUBLIC KEY" [3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829]
decode-005 1
decode-005 "CMS" [308183060b2a864886f70d0109100109a0743072020100300d060b2a864886f70d0109100308305e06092a864886f70d010701a051044f789c73cecf2b49cd2bd10da92c48b5522849ad28d12fc849ccccb35648ce482c2a4e2db10d0dd6750c76f6f4e4e5e2e572cf2c4b5588cc2f2d52f0484d2c2a514854702a4a4dcc064901009bf4171d]
decode-006 2
decode-006 "PRIVATE KEY" [308184020100301006072a8648ce3d020106052b8104000a046d306b020101042055c07f50d3f16a547dcc36008d021f8e88d40e242e1a7489ac5104cd93d3ffdda144034200049ced426d82717f69ba9633ad13dd734913306fe5ecfd4ddc0a1f433ac69336abee77c26b0dfde60b760b638017770c1987b4f8a50a48852025f2680f33f4b909]
decode-006 "PUBLIC KEY" [3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829]
decode-007 1
decode-007 "PUBLIC KEY" [3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829]
decode-008 1
decode-008 "PUBLIC KEY" [3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829]
decode-009 1
decode-009 "PUBLIC KEY" [3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829]
decode-010 1
decode-010 "PUBLIC KEY" [3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829]
decode-011 1
decode-011 "X509 CRL" []
decode-012 0
decode-013 0
decode-014 1
decode-014 "" [000102]
decode-015 0
decode-016 1
decode-016 "DATA" [000102]
```

<!-- test: pem.decode-errors -->
A missing END line, a BEGIN line inside a block, an END line naming another label, the label in another case, or with four hyphens, RFC 1421 header lines, and bodies that are not strict base64.
```maxon
function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure PemError) returns String
	return match failure 'name'
		unterminatedBlock gives "unterminatedBlock"
		labelMismatch gives "labelMismatch"
		invalidBody gives "invalidBody"
		invalidLabel gives "invalidLabel"
	end 'name'
end 'errorName'

function report(id String, text String)
	if let blocks = try Pem.decode(text) 'decoded'
		print("{id} {blocks.count()}\n")

		for block in blocks 'eachBlock'
			print("{id} \"{block.label()}\" [{toHex(block.bytes())}]\n")
		end 'eachBlock'
	end 'decoded' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("decode-err-001", text: "-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n")
	report("decode-err-002", text: "-----BEGIN PUBLIC KEY-----")
	report("decode-err-003", text: "-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----BEGIN PRIVATE KEY-----\nMIGEAgEAMBAGByqGSM49AgEGBSuBBAAKBG0wawIBAQQgVcB/UNPxalR9zDYAjQIf\njojUDiQuGnSJrFEEzZPT/92hRANCAASc7UJtgnF/abqWM60T3XNJEzBv5ez9TdwK\nH0M6xpM2q+53wmsN/eYLdgtjgBd3DBmHtPilCkiFICXyaA8z9LkJ\n-----END PRIVATE KEY-----\n")
	report("decode-err-004", text: "-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END PRIVATE KEY-----\n")
	report("decode-err-005", text: "-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END public key-----\n")
	report("decode-err-006", text: "-----BEGIN PUBLIC KEY-----\nMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe\nkLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U\nNkn3Eos8EiZByi9DVsyfy9eejh+8AXgp\n-----END PUBLIC KEY----\n")
	report("decode-err-007", text: "-----BEGIN RSA PRIVATE KEY-----\nProc-Type: 4,ENCRYPTED\nDEK-Info: DES-EDE3-CBC,F8C3A3A8B1D5A4F0\n\nAAEC\n-----END RSA PRIVATE KEY-----\n")
	report("decode-err-008", text: "-----BEGIN DATA-----\nAA*C\n-----END DATA-----\n")
	report("decode-err-009", text: "-----BEGIN DATA-----\nAAE\n-----END DATA-----\n")
	report("decode-err-010", text: "-----BEGIN DATA-----\nAA==\nAAEC\n-----END DATA-----\n")
	report("decode-err-011", text: "-----BEGIN DATA-----\nAB==\n-----END DATA-----\n")
	report("decode-err-012", text: "-----BEGIN DATA-----\nAA-C\n-----END DATA-----\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
decode-err-001 unterminatedBlock
decode-err-002 unterminatedBlock
decode-err-003 unterminatedBlock
decode-err-004 labelMismatch
decode-err-005 labelMismatch
decode-err-006 labelMismatch
decode-err-007 invalidBody
decode-err-008 invalidBody
decode-err-009 invalidBody
decode-err-010 invalidBody
decode-err-011 invalidBody
decode-err-012 invalidBody
```

<!-- test: pem.encode -->
RFC 7468 figures 6, 12 and 15 exactly, an empty body, and bodies of one byte, exactly one 64-character line, one line plus one, and two lines, an empty label, and a label with a hyphen inside it.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function errorName(failure PemError) returns String
	return match failure 'name'
		unterminatedBlock gives "unterminatedBlock"
		labelMismatch gives "labelMismatch"
		invalidBody gives "invalidBody"
		invalidLabel gives "invalidLabel"
	end 'name'
end 'errorName'

function report(id String, label String, dataHex String)
	if let text = try Pem.encode(label, data: fromHex(dataHex)) 'encoded'
		print("{id}\n{text}")
	end 'encoded' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("encode-001", label: "PUBLIC KEY", dataHex: "3076301006072a8648ce3d020106052b81040022036200049f52e5c0b37f2816104551fa1df20c4f37c4a89395ced2de90b721a76862efc70268c63cd4506562cf09f65ee4adcf8ce1a05e0866058db6be8625edb4958f2fbc9d944fb9506e143649f7128b3c122641ca2f4356cc9fcbd79e8e1fbc017829")
	report("encode-002", label: "CERTIFICATE", dataHex: "3082022c308201d2a003020102020100300a06082a8648ce3d040302307d310b3009060355040613024245310f300d060355040a1306476e75544c5331253023060355040b131c476e75544c5320636572746966696361746520617574686f72697479310f300d060355040813064c657576656e312530230603550403131c476e75544c5320636572746966696361746520617574686f72697479301e170d3131303532333230333832315a170d3132313232323037343135315a307d310b3009060355040613024245310f300d060355040a1306476e75544c5331253023060355040b131c476e75544c5320636572746966696361746520617574686f72697479310f300d060355040813064c657576656e312530230603550403131c476e75544c5320636572746966696361746520617574686f726974793059301306072a8648ce3d020106082a8648ce3d0301070342000452d88d238ae367d78636b1200b097dc8c9baa220952fc54a63fa835fce782f8ff362cafdb7f780569d6e17b90e114c48b2c0af3b591716306809079917fedda7a3433041300f0603551d130101ff040530030101ff300f0603551d0f0101ff04050303070600301d0603551d0e04160414f0b481fe9812bfb528b9644003cbcc1f664e2803300a06082a8648ce3d0403020348003045022031aec03d4a3f21be8517fcf0c7b231072a385643d136d595e17e52c0064387a7022100978c0eb83c0a41afaea5cf067ed5c4d82fffe262803410ba22dd35814693229a")
	report("encode-003", label: "PRIVATE KEY", dataHex: "308184020100301006072a8648ce3d020106052b8104000a046d306b020101042055c07f50d3f16a547dcc36008d021f8e88d40e242e1a7489ac5104cd93d3ffdda144034200049ced426d82717f69ba9633ad13dd734913306fe5ecfd4ddc0a1f433ac69336abee77c26b0dfde60b760b638017770c1987b4f8a50a48852025f2680f33f4b909")
	report("encode-004", label: "X509 CRL", dataHex: "")
	report("encode-005", label: "DATA", dataHex: "00")
	report("encode-006", label: "DATA", dataHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f")
	report("encode-007", label: "DATA", dataHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f30")
	report("encode-008", label: "DATA", dataHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f")
	report("encode-009", label: "", dataHex: "000102")
	report("encode-010", label: "A-B", dataHex: "00")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
encode-001
-----BEGIN PUBLIC KEY-----
MHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEn1LlwLN/KBYQRVH6HfIMTzfEqJOVztLe
kLchp2hi78cCaMY81FBlYs8J9l7krc+M4aBeCGYFjba+hiXttJWPL7ydlE+5UG4U
Nkn3Eos8EiZByi9DVsyfy9eejh+8AXgp
-----END PUBLIC KEY-----
encode-002
-----BEGIN CERTIFICATE-----
MIICLDCCAdKgAwIBAgIBADAKBggqhkjOPQQDAjB9MQswCQYDVQQGEwJCRTEPMA0G
A1UEChMGR251VExTMSUwIwYDVQQLExxHbnVUTFMgY2VydGlmaWNhdGUgYXV0aG9y
aXR5MQ8wDQYDVQQIEwZMZXV2ZW4xJTAjBgNVBAMTHEdudVRMUyBjZXJ0aWZpY2F0
ZSBhdXRob3JpdHkwHhcNMTEwNTIzMjAzODIxWhcNMTIxMjIyMDc0MTUxWjB9MQsw
CQYDVQQGEwJCRTEPMA0GA1UEChMGR251VExTMSUwIwYDVQQLExxHbnVUTFMgY2Vy
dGlmaWNhdGUgYXV0aG9yaXR5MQ8wDQYDVQQIEwZMZXV2ZW4xJTAjBgNVBAMTHEdu
dVRMUyBjZXJ0aWZpY2F0ZSBhdXRob3JpdHkwWTATBgcqhkjOPQIBBggqhkjOPQMB
BwNCAARS2I0jiuNn14Y2sSALCX3IybqiIJUvxUpj+oNfzngvj/Niyv2394BWnW4X
uQ4RTEiywK87WRcWMGgJB5kX/t2no0MwQTAPBgNVHRMBAf8EBTADAQH/MA8GA1Ud
DwEB/wQFAwMHBgAwHQYDVR0OBBYEFPC0gf6YEr+1KLlkQAPLzB9mTigDMAoGCCqG
SM49BAMCA0gAMEUCIDGuwD1KPyG+hRf88MeyMQcqOFZD0TbVleF+UsAGQ4enAiEA
l4wOuDwKQa+upc8GftXE2C//4mKANBC6It01gUaTIpo=
-----END CERTIFICATE-----
encode-003
-----BEGIN PRIVATE KEY-----
MIGEAgEAMBAGByqGSM49AgEGBSuBBAAKBG0wawIBAQQgVcB/UNPxalR9zDYAjQIf
jojUDiQuGnSJrFEEzZPT/92hRANCAASc7UJtgnF/abqWM60T3XNJEzBv5ez9TdwK
H0M6xpM2q+53wmsN/eYLdgtjgBd3DBmHtPilCkiFICXyaA8z9LkJ
-----END PRIVATE KEY-----
encode-004
-----BEGIN X509 CRL-----
-----END X509 CRL-----
encode-005
-----BEGIN DATA-----
AA==
-----END DATA-----
encode-006
-----BEGIN DATA-----
AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4v
-----END DATA-----
encode-007
-----BEGIN DATA-----
AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4v
MA==
-----END DATA-----
encode-008
-----BEGIN DATA-----
AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4v
MDEyMzQ1Njc4OTo7PD0+P0BBQkNERUZHSElKS0xNTk9QUVJTVFVWV1hZWltcXV5f
-----END DATA-----
encode-009
-----BEGIN -----
AAEC
-----END -----
encode-010
-----BEGIN A-B-----
AA==
-----END A-B-----
```

<!-- test: pem.encode-refuses-an-invalid-label -->
Consecutive hyphens, a leading space, a trailing hyphen, consecutive spaces, a line feed and a tab.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function errorName(failure PemError) returns String
	return match failure 'name'
		unterminatedBlock gives "unterminatedBlock"
		labelMismatch gives "labelMismatch"
		invalidBody gives "invalidBody"
		invalidLabel gives "invalidLabel"
	end 'name'
end 'errorName'

function report(id String, label String, dataHex String)
	if let text = try Pem.encode(label, data: fromHex(dataHex)) 'encoded'
		print("{id}\n{text}")
	end 'encoded' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("encode-err-001", label: "BAD--LABEL", dataHex: "00")
	report("encode-err-002", label: " LEADING", dataHex: "00")
	report("encode-err-003", label: "TRAILING-", dataHex: "00")
	report("encode-err-004", label: "TWO  SPACES", dataHex: "00")
	report("encode-err-005", label: "LINE\nBREAK", dataHex: "00")
	report("encode-err-006", label: "TAB\tLABEL", dataHex: "00")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
encode-err-001 invalidLabel
encode-err-002 invalidLabel
encode-err-003 invalidLabel
encode-err-004 invalidLabel
encode-err-005 invalidLabel
encode-err-006 invalidLabel
```

<!-- test: pem.decode-of-encode-gives-back-the-block -->
`Pem.decode` of what `Pem.encode` writes is one block with the label and bytes that went in: an empty body, a one-byte body under a hyphenated label, three bytes under the empty label, and a 100-byte body that spans three lines.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure PemError) returns String
	return match failure 'name'
		unterminatedBlock gives "unterminatedBlock"
		labelMismatch gives "labelMismatch"
		invalidBody gives "invalidBody"
		invalidLabel gives "invalidLabel"
	end 'name'
end 'errorName'

function report(id String, label String, dataHex String)
	if let text = try Pem.encode(label, data: fromHex(dataHex)) 'encoded'
		if let blocks = try Pem.decode(text) 'decoded'
			print("{id} {blocks.count()}\n")

			for block in blocks 'eachBlock'
				print("{id} \"{block.label()}\" [{toHex(block.bytes())}]\n")
			end 'eachBlock'
		end 'decoded' else (failure) 'decodeRefused'
			print("{id} decode {errorName(failure)}\n")
		end 'decodeRefused'
	end 'encoded' else (failure) 'encodeRefused'
		print("{id} encode {errorName(failure)}\n")
	end 'encodeRefused'
end 'report'

function main() returns ExitCode
	report("roundtrip-001", label: "PRIVATE KEY", dataHex: "")
	report("roundtrip-002", label: "A-B", dataHex: "ff")
	report("roundtrip-003", label: "", dataHex: "000102")
	report("roundtrip-004", label: "ROUND TRIP", dataHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f60616263")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
roundtrip-001 1
roundtrip-001 "PRIVATE KEY" []
roundtrip-002 1
roundtrip-002 "A-B" [ff]
roundtrip-003 1
roundtrip-003 "" [000102]
roundtrip-004 1
roundtrip-004 "ROUND TRIP" [000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f60616263]
```
