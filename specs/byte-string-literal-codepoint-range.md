---
feature: byte-string-literal-codepoint-range
status: selfhosted
status-reason: all 3 cases pin a compile error this compiler does not raise - they compile clean here (measured 2026-08-06, BATCH29/A3a). Already re-homed: specs-shv2/byte-string-literal-codepoint-range.md, 3 of 3 active.
keywords: [byte, string, literal, codepoint, latin1, diagnostics]
category: diagnostics
---

# Byte String Literal Codepoint Range

## Documentation

A byte string literal (`b"..."`) is a raw byte sequence, so each source
character must have a single-byte encoding. Codepoints `0x00`–`0xFF` (Latin-1)
encode directly to that byte — `b"À"` (U+00C0) is the single byte `0xC0`.

A literal character, or a `\uNNNN` escape, whose codepoint is **above `0xFF`**
has no single-byte encoding and is rejected at compile time with **E1004
`lexerInvalidEscape`**. To embed an exact raw byte regardless of its Unicode
interpretation, use the `\xNN` hex escape.

This is a self-hosted-only diagnostic. The C# bootstrap lowers a byte string
literal through .NET's `Encoding.Latin1`, which silently *best-fit* transliterates
an out-of-range codepoint to an ASCII approximation (`Ā` → `A`, `Ł` → `L`) or a
`?` placeholder — a lossy surprise in what is meant to be a raw-byte literal. The
self-hosted compiler rejects the input instead, so this whole spec is marked
`status: selfhosted` to skip it in the C# runner.

## Tests

<!-- test: error.byte-string-literal.codepoint-literal-latin-extended -->
```maxon
function main() returns ExitCode
	let bytes = b"Ā"
	return bytes.count()
end 'main'
```
```maxoncstderr
error E1004: Codepoint U+0100 exceeds the byte range 0-255 in byte string literal; use a \xNN hex escape to embed a raw byte
```

<!-- test: error.byte-string-literal.codepoint-uni-escape -->
```maxon
function main() returns ExitCode
	let bytes = b"\u0100"
	return bytes.count()
end 'main'
```
```maxoncstderr
error E1004: Codepoint U+0100 exceeds the byte range 0-255 in byte string literal; use a \xNN hex escape to embed a raw byte
```

<!-- test: error.byte-string-literal.codepoint-emoji -->
```maxon
function main() returns ExitCode
	let bytes = b"😀"
	return bytes.count()
end 'main'
```
```maxoncstderr
error E1004: Codepoint U+1f600 exceeds the byte range 0-255 in byte string literal; use a \xNN hex escape to embed a raw byte
```
