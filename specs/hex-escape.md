---
feature: hex-escape
status: experimental
keywords: [escape, hex, byte, literal]
category: literals
---

# Hex Escape Sequences

## Documentation

The `\xNN` escape sequence allows specifying a byte value using two hexadecimal digits (00-FF). This works in string literals, byte string literals, and character literals.

### Syntax

```text
let bytes = b"\x41\x42"       // ByteArray containing [65, 66] (same as b"AB")
let ch = '\x41'                // Character 'A'
let s = "\x48\x69"            // String "Hi"
```

Both uppercase (`\x4A`) and lowercase (`\x4a`) hex digits are accepted.

### Use Cases

Hex escapes are useful for embedding arbitrary byte values in literals, especially non-printable characters:

```text
let null_byte = b"\x00"       // Null byte
let bell = "\x07"             // Bell character
let max_byte = b"\xFF"        // Byte 255
```

## Tests

<!-- test: hex-escape.byte-string-basic -->

```maxon
function main() returns ExitCode
    let bytes = b"\x41\x42"
    let a = try bytes.get(0) otherwise 0
    let b = try bytes.get(1) otherwise 0
    print("{a} {b}")
    return bytes.count()
end 'main'
```
```exitcode
2
```
```stdout
65 66
```

<!-- test: hex-escape.byte-string-ff -->

```maxon
function main() returns ExitCode
    let bytes = b"\xFF"
    let v = try bytes.get(0) otherwise 0
    print("{v}")
    return bytes.count()
end 'main'
```
```exitcode
1
```
```stdout
255
```

<!-- test: hex-escape.mixed-content -->

```maxon
function main() returns ExitCode
    let bytes = b"A\x42C"
    let a = try bytes.get(0) otherwise 0
    let b = try bytes.get(1) otherwise 0
    let c = try bytes.get(2) otherwise 0
    print("{a} {b} {c}")
    return bytes.count()
end 'main'
```
```exitcode
3
```
```stdout
65 66 67
```

<!-- test: hex-escape.character-literal -->

```maxon
function main() returns ExitCode
    let ch = '\x41'
    if ch == 'A' 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```

<!-- test: hex-escape.string-literal -->

```maxon
function main() returns ExitCode
    let s = "\x48\x69"
    print(s)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
Hi
```

<!-- test: hex-escape.lowercase-digits -->

```maxon
function main() returns ExitCode
    let bytes = b"\x0a"
    let v = try bytes.get(0) otherwise 0
    print("{v}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
10
```

<!-- test: hex-escape.invalid-short -->

```maxon
function main() returns ExitCode
    let bytes = b"\xG0"
    return 0
end 'main'
```
```maxoncstderr
error E1004: specs/fragments/hex-escape/hex-escape.invalid-short.test:3:17: Invalid hex escape '\xG0': expected 2 hex digits in byte string literal
```

<!-- test: hex-escape.invalid-truncated -->

```maxon
function main() returns ExitCode
    let bytes = b"\x4"
    return 0
end 'main'
```
```maxoncstderr
error E1004: specs/fragments/hex-escape/hex-escape.invalid-truncated.test:3:17: Invalid hex escape '\x4': expected 2 hex digits in byte string literal
```
