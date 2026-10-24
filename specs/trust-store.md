---
feature: trust-store
status: stable
keywords: [trust store, TrustStore, TrustStoreError, root certificates, certificate authorities, tls, https]
category: stdlib
---

# `TrustStore` — the root certificates the operating system trusts

## Documentation

| Call | Meaning |
|---|---|
| `TrustStore.systemRoots()` | the DER bytes of every root certificate the operating system's store holds, one `ByteArray` each, as a `ByteArrays` |

`systemRoots` throws `TrustStoreError.unavailable` when the machine holds no store to read, and `TrustStoreError.malformed` when the
store is there but is not certificates. On wasm32-wasi the call is refused (E3104): the target has no operating-system store.

The store is read from the operating system on every other target:

| Target | Source |
|---|---|
| x64-windows | the `ROOT` system store, through `crypt32.dll` (`CertOpenSystemStoreW`, `CertEnumCertificatesInStore`) |
| arm64-macos | the system, admin and user trust-settings domains (`SecTrustSettingsCopyCertificates`) over the Security and CoreFoundation frameworks, filtered by their SSL trust settings and linked only into a program that uses `TrustStore` |
| x64-linux, arm64-linux | the first existing file of the distribution CA bundle locations, read as PEM `CERTIFICATE` blocks |

The answers are the machine's own, so the case below asserts properties of them rather than values.

## Tests

<!-- test: trust-store.system-roots-are-der-certificates -->
The machine holds at least one root, and every root is one DER `SEQUENCE` whose declared length is exactly the bytes that follow its header.
```maxon
typealias DerSpan = int(0 to u32.max)

let DerSequenceTag = 0x30
let DerLongFormFlag = 0x80
let DerLengthOctetsMask = 0x7F
let DerOctetRadix = 256

function declaredSpan(root ByteArray) returns DerSpan
	let first = try root.get(1) otherwise return 0
	let lengthOctets = (first and DerLengthOctetsMask) as DerSpan

	if first < DerLongFormFlag 'shortForm'
		return (first as DerSpan) + 2
	end 'shortForm'

	var length = 0 as DerSpan

	for index in 0 upto lengthOctets 'eachLengthOctet'
		let octet = try root.get((2 + index) as ElementIndex) otherwise return 0
		length = length * DerOctetRadix + (octet as DerSpan)
	end 'eachLengthOctet'

	return length + 2 + lengthOctets
end 'declaredSpan'

function main() returns ExitCode
	let roots = try TrustStore.systemRoots() otherwise return 1
	print("count > 0: {roots.count() > 0}\n")

	var allSequences = true
	var allSized = true

	for root in roots 'eachRoot'
		let first = try root.get(0) otherwise return 2

		if first != DerSequenceTag 'notASequence'
			allSequences = false
		end 'notASequence'

		if declaredSpan(root) != (root.count() as DerSpan) 'wrongSize'
			allSized = false
		end 'wrongSize'
	end 'eachRoot'

	print("every entry starts with 0x30: {allSequences}\n")
	print("every entry is exactly its declared length: {allSized}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
count > 0: true
every entry starts with 0x30: true
every entry is exactly its declared length: true
```
