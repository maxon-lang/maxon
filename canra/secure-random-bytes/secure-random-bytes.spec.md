+++
name = "secure-random-bytes"
version = "1"
title = "Secure Random Bytes from the Operating System CSPRNG"
status = "draft"
keywords = ["random", "csprng", "entropy", "getrandom", "cryptography"]
difficulty = "easy"

[api]
functions = ["random_bytes"]
types = ["RandomError"]

[references]
items = [
  { label = "Linux getrandom(2)", url = "https://man7.org/linux/man-pages/man2/getrandom.2.html" },
  { label = "Windows ProcessPrng", url = "https://learn.microsoft.com/en-us/windows/win32/seccng/processprng" },
  { label = "Windows BCryptGenRandom", url = "https://learn.microsoft.com/en-us/windows/win32/api/bcrypt/nf-bcrypt-bcryptgenrandom" },
  { label = "OpenBSD getentropy(2)", url = "https://man.openbsd.org/getentropy.2" },
  { label = "macOS arc4random(3)", url = "https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man3/arc4random.3.html" },
  { label = "WASI random_get", url = "https://github.com/WebAssembly/WASI/blob/main/legacy/preview1/docs.md" },
  { label = "RFC 4086" , url = "https://www.rfc-editor.org/rfc/rfc4086.html" },
]

[test-vectors]
inline = true
external = []
+++

## Overview

This spec defines one function, `random_bytes`, which returns a requested number of cryptographically secure random bytes taken from the operating system's cryptographically secure pseudorandom number generator (CSPRNG). It is the primitive under key generation, nonces, salts, session tokens and random identifiers.

**Out of scope:** user-space pseudorandom generators of any kind (seeded, deterministic, or fast non-cryptographic ones); random integers in a range, shuffling and other derived distributions (they are built on top of this function); hardware RNG instructions used directly (`RDRAND`, `RNDR`); entropy estimation; reseeding policy; blocking until boot-time entropy is available on systems where the OS interface does not already do so.

## Types

### RandomError

```pseudocode
type RandomError =
  | Unavailable   // the operating system CSPRNG could not be used
```

## Functions

### random_bytes

```pseudocode
random_bytes(count: Int) -> Bytes | RandomError
```

Returns `count` bytes from the operating system CSPRNG.

**Parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `count` | Int | yes | Number of bytes, `count >= 0` |

**Returns:** Exactly `count` random bytes, or `Unavailable`.

**Algorithm:**

1. If `count == 0`, return empty bytes without calling the operating system.
2. Allocate a buffer of `count` bytes.
3. Fill it from the platform source below. Every source has a per-call limit or may return fewer bytes than requested, so loop: request at most the per-call limit, advance by the number of bytes actually written, and repeat until the buffer is full. Retry a call interrupted by a signal (`EINTR`).
4. If the source reports any other failure, return `Unavailable`. Never return a partly filled buffer.
5. Return the buffer.

### Platform sources

| Platform | Source | Per-call limit and notes |
|---|---|---|
| Linux, Android | `getrandom(buf, len, 0)` | Returns at most 33554431 bytes per call and may return fewer after a signal; loop. Blocks only until the kernel pool is first initialized. On `ENOSYS` (kernels before 3.17) fall back to reading `/dev/urandom`. |
| Windows 10 and later | `ProcessPrng` from `bcryptprimitives.dll` | Documented never to fail. |
| Older Windows | `BCryptGenRandom(NULL, buf, len, BCRYPT_USE_SYSTEM_PREFERRED_RNG)` | `len` is a 32-bit `ULONG`; split larger requests. |
| macOS, iOS | `arc4random_buf`, or `getentropy` | `arc4random_buf` has no limit and cannot fail; `getentropy` is limited to 256 bytes per call. |
| OpenBSD, FreeBSD, NetBSD, illumos | `getentropy`, or `arc4random_buf` | `getentropy` is limited to 256 bytes per call; loop. `getrandom` MAY be used where present. |
| WASI | `random_get` (preview 1) or `wasi:random/random.get-random-bytes` (preview 2) | The host's CSPRNG. |
| Browser and other JavaScript hosts | `crypto.getRandomValues` | Limited to 65536 bytes per call; loop. |

## Behavioral Contracts

### random_bytes()

**MUST:**

1. MUST take every byte from the operating system CSPRNG named for the platform above, or an interface documented to be equivalent. It MUST NOT use a user-space generator seeded from it, the time, the process id, addresses, or any other predictable value.
2. MUST return exactly `count` bytes, for every `count` from 0 up to the largest buffer the platform can allocate.
3. MUST loop over short reads and per-call limits as described in the algorithm; a request larger than one call's limit MUST NOT fail or return fewer bytes.
4. MUST return `Unavailable` when the source fails, and MUST NOT fall back to a weaker source, return zeros, or return a partly filled buffer.
5. MUST be safe to call from multiple threads at once and in a child process after `fork`, without two callers receiving the same bytes.
6. MUST NOT cache or reuse output: every call returns fresh bytes.

**SHOULD:**

1. SHOULD NOT open a file descriptor per call; prefer the system call or library function over reading a device file.
2. SHOULD treat `Unavailable` as fatal in callers that need randomness for security; there is no safe default to substitute.

## Test Vectors

The output is random, so every vector is an assertion run `repeat` times. `result` is the bytes of one call; `results` is the list of all `repeat` results of the vector. The assertion helpers are:

```pseudocode
length(result)          // number of bytes
all_unique(results)     // true when no two results are equal
count_distinct(result)  // number of distinct byte values in result, 0..256
```

The statistical vectors have negligible false-failure rates: two equal 32-byte draws occur with probability 2^-256, a repeat among 1000 16-byte draws with probability below 2^-108, and a byte value missing from 65536 random bytes with probability below 2^-360.

```test-vectors
{"id": "random-bytes-001", "function": "random_bytes", "input": {"args": {"count": 0}}, "expected": {"assertion": "length(result) == 0"}, "repeat": 10, "tags": ["length", "zero", "edge"]}
{"id": "random-bytes-002", "function": "random_bytes", "input": {"args": {"count": 1}}, "expected": {"assertion": "length(result) == 1"}, "repeat": 100, "tags": ["length", "edge"]}
{"id": "random-bytes-003", "function": "random_bytes", "input": {"args": {"count": 16}}, "expected": {"assertion": "length(result) == 16"}, "repeat": 100, "tags": ["length"]}
{"id": "random-bytes-004", "function": "random_bytes", "input": {"args": {"count": 32}}, "expected": {"assertion": "length(result) == 32"}, "repeat": 100, "tags": ["length"]}
{"id": "random-bytes-005", "function": "random_bytes", "input": {"args": {"count": 64}}, "expected": {"assertion": "length(result) == 64"}, "repeat": 100, "tags": ["length"]}
{"id": "random-bytes-006", "function": "random_bytes", "input": {"args": {"count": 256}}, "expected": {"assertion": "length(result) == 256"}, "repeat": 10, "tags": ["length", "getentropy-limit", "edge"]}
{"id": "random-bytes-007", "function": "random_bytes", "input": {"args": {"count": 257}}, "expected": {"assertion": "length(result) == 257"}, "repeat": 10, "tags": ["length", "beyond-getentropy-limit", "edge"]}
{"id": "random-bytes-008", "function": "random_bytes", "input": {"args": {"count": 1000}}, "expected": {"assertion": "length(result) == 1000"}, "repeat": 10, "tags": ["length"]}
{"id": "random-bytes-009", "function": "random_bytes", "input": {"args": {"count": 65537}}, "expected": {"assertion": "length(result) == 65537"}, "repeat": 3, "tags": ["length", "beyond-get-random-values-limit", "edge"]}
{"id": "random-bytes-010", "function": "random_bytes", "input": {"args": {"count": 1048576}}, "expected": {"assertion": "length(result) == 1048576"}, "repeat": 1, "tags": ["length", "large"]}
{"id": "random-bytes-011", "function": "random_bytes", "input": {"args": {"count": 33554432}}, "expected": {"assertion": "length(result) == 33554432"}, "repeat": 1, "tags": ["length", "large", "beyond-getrandom-limit", "edge"]}
{"id": "random-bytes-012", "function": "random_bytes", "input": {"args": {"count": 32}}, "expected": {"assertion": "all_unique(results)"}, "repeat": 2, "tags": ["two-draws-differ"]}
{"id": "random-bytes-013", "function": "random_bytes", "input": {"args": {"count": 16}}, "expected": {"assertion": "all_unique(results)"}, "repeat": 1000, "tags": ["uniqueness"]}
{"id": "random-bytes-014", "function": "random_bytes", "input": {"args": {"count": 65536}}, "expected": {"assertion": "count_distinct(result) == 256"}, "repeat": 5, "tags": ["every-byte-value"]}
```

## Edge Cases

- **Error vectors.** This spec cannot meet a literal bar of five error vectors. Its one error, `Unavailable`, cannot be triggered by any argument: it depends on the operating system failing, which a portable vector cannot arrange. Negative counts are a type error, not a vector. The vectors tagged `edge` stand in for error vectors: `count` 0, 1, 256 and 257 (either side of the `getentropy` limit), 65537 (beyond one `getRandomValues` call), and 33554432 (beyond one `getrandom` call).
- **Count 0.** Returns empty bytes and succeeds even where the source is unavailable, because the source is not called.
- **Negative count.** `count` is a non-negative count; an implementation SHOULD give it an unsigned or range-restricted type. Where the type admits negatives, a negative `count` MUST fail loudly as an argument error, never be treated as 0 or converted to a huge unsigned size.
- **Very large counts.** Limited only by memory. An allocation failure is the platform's out-of-memory behaviour, not `Unavailable`.
- **Early boot.** On Linux, `getrandom` with flags 0 blocks until the pool is initialized and then never blocks again; this is the intended behaviour. Do not pass `GRND_NONBLOCK`.
- **Fork.** A user-space buffer of pre-fetched random bytes would be duplicated into the child; that is why caching is forbidden.

## Implementation Notes

- **Typical failure modes.** Using a language's general-purpose `random` module, seeding a fast generator from the clock, reading `/dev/random` and blocking needlessly, ignoring the return value of a short read, and falling back to a non-cryptographic generator when the system call is missing.
- **Language libraries.** Most standard libraries already wrap the right source: `crypto/rand.Read` in Go, `getrandom` crate or `OsRng` in Rust, `secrets.token_bytes` or `os.urandom` in Python, `crypto.randomBytes` in Node.js, `SecureRandom` with the native provider in Java, `RandomNumberGenerator.Fill` in .NET.
- **Testing.** Statistical tests cannot prove a generator is secure; they only catch gross failures such as a constant, truncated or zeroed buffer. The real requirement is the source, and review is how it is checked.
- **Zeroization.** Callers that use the bytes as keys SHOULD overwrite them when done; this function cannot know.

## References

- [getrandom(2) -- Linux manual page](https://man7.org/linux/man-pages/man2/getrandom.2.html) -- the Linux interface and its 33554431-byte per-call limit.
- [ProcessPrng](https://learn.microsoft.com/en-us/windows/win32/seccng/processprng) and [BCryptGenRandom](https://learn.microsoft.com/en-us/windows/win32/api/bcrypt/nf-bcrypt-bcryptgenrandom) -- the Windows interfaces.
- [getentropy(2) -- OpenBSD manual page](https://man.openbsd.org/getentropy.2) -- the BSD interface and its 256-byte limit.
- [arc4random(3)](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man3/arc4random.3.html) -- the Apple and BSD library interface.
- [WASI preview 1 `random_get`](https://github.com/WebAssembly/WASI/blob/main/legacy/preview1/docs.md) -- the WebAssembly System Interface source.
- [RFC 4086 -- Randomness Requirements for Security](https://www.rfc-editor.org/rfc/rfc4086.html) -- why predictable seeds are fatal.
