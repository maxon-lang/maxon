# Canra specs

Language-agnostic specifications of the stdlib primitives, in the [Canra](https://canra.dev) format: one
`<name>/<name>.spec.md` per primitive, with TOML front matter and JSONL test vectors. The format and its
content rules are Canra's `CONTRIBUTING.md`; every file here is ASCII only and carries no HTML comments.

Each spec is written before the Maxon implementation, from the published vectors of its governing standard.
The `specs/` cases for that stdlib module pin the same vectors, so the stdlib is a verified implementation
of the spec here.

| Spec | Stdlib | Maxon cases |
|---|---|---|
| `sha-2` | `stdlib/Sha2.maxon` | `specs/sha2.md` |
| `hmac` | `stdlib/Hmac.maxon` | `specs/hmac.md` |
| `hkdf` | `stdlib/Hkdf.maxon` | `specs/hkdf.md` |
| `base64` | `stdlib/Base64.maxon` | `specs/base64.md` |
| `pem` | `stdlib/Pem.maxon` | `specs/pem.md` |
| `secure-random-bytes` | `stdlib/Random.maxon` | `specs/random.md` |
| `aes` | `stdlib/Aes.maxon` | `specs/aes.md` |
| `aes-ctr` | `stdlib/Aes.maxon` | `specs/aes.md` |
| `aes-gcm` | `stdlib/AesGcm.maxon` | `specs/aes-gcm.md` |
| `chacha20` | `stdlib/ChaCha20.maxon` | `specs/chacha20.md` |
| `poly1305` | `stdlib/Poly1305.maxon` | `specs/poly1305.md` |
| `chacha20-poly1305` | `stdlib/ChaCha20Poly1305.maxon` | `specs/chacha20-poly1305.md` |
| `x25519` | `stdlib/Curve25519.maxon` | `specs/x25519.md` |
| `ed25519-verify` | `stdlib/Curve25519.maxon` | `specs/ed25519.md` |
| `ed25519-sign` | `stdlib/Curve25519.maxon` | `specs/ed25519.md` |
| `ecdh-p256` | `stdlib/NistCurves.maxon` | `specs/ecdh.md` |
| `ecdsa-verify` | `stdlib/NistCurves.maxon` | `specs/ecdsa.md` |
| `ecdsa-sign-p256` | `stdlib/NistCurves.maxon` | `specs/ecdsa.md` |
| `ecdsa-sign-p384` | `stdlib/NistCurves.maxon` | `specs/ecdsa.md` |
| `rsa-verify` | `stdlib/Rsa.maxon` | `specs/rsa.md` |
| `rsa-pss-sign` | `stdlib/Rsa.maxon` | `specs/rsa.md` |
| `rsa-pkcs1-sign` | `stdlib/Rsa.maxon` | `specs/rsa.md` |
| `x509-parse` | `stdlib/X509.maxon` | `specs/x509.md` |
| `x509-verify` | `stdlib/X509.maxon` | `specs/x509.md` |
| `x509-verify-go` | `stdlib/X509.maxon` | `specs/x509.md` |
| `x509-verify-client-auth` | `stdlib/X509.maxon` | `specs/x509.md` |
