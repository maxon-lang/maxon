---
feature: format-int
status: stable
keywords: [format, int, conversion, string]
category: stdlib
---

# Format Integer Function

## Developer Notes

The `format_int_array()` function converts an integer to a string representation, storing it in a character array buffer.

Implementation:
- Defined in `stdlib/fmt/integer.maxon`
- Signature: `format_int_array(value int, buffer []byte) int`
- Returns the number of characters written
- Handles negative numbers, zero, and positive values
- Uses modulo and division to extract digits
- Builds digits in reverse order, then copies forward
- Auto-discovered by compiler stdlib system

Algorithm:
1. Handle zero case specially
2. Handle negative sign if needed
3. Extract digits using modulo 10 and division
4. Store digits in reverse in temp buffer
5. Copy from temp to output buffer in correct order

## Documentation

The `format_int_array()` function converts an integer to a string representation.

### Syntax

```maxon
format_int_array(value, buffer)
```
Parameters:
- `value` - The integer to convert
- `buffer` - Character array to store result (must be at least 12 bytes)

Returns the number of characters written to the buffer.

### Example

```maxon
function main() int
    var buffer = [12]byte
    var len = format_int_array(42, buffer)
    // buffer now contains "42"
    return len  // Returns 2
end 'main'
```
```exitcode
2
```


### Notes

- Buffer must be at least 12 bytes to handle maximum negative value (-2147483648)
- No null terminator is added
- Returned length can be used to determine actual string size

## Tests

<!-- test: basic -->
```maxon
function main() int
    var buffer = [12]byte
    var len = format_int_array(42, buffer)
    return len
end 'main'
```
```exitcode
2
```


<!-- test: zero -->
```maxon
function main() int
    var buffer = [12]byte
    var len = format_int_array(0, buffer)
    return len
end 'main'
```
```exitcode
1
```


<!-- test: negative -->
```maxon
function main() int
    var buffer = [12]byte
    var len = format_int_array(-123, buffer)
    return len
end 'main'
```
```exitcode
4
```


<!-- test: large-value -->
```maxon
function main() int
    var buffer = [12]byte
    var len = format_int_array(123456, buffer)
    return len
end 'main'
```
```exitcode
6
```


<!-- test: single-digit -->
```maxon
function main() int
    var buffer = [12]byte
    var len = format_int_array(7, buffer)
    return len
end 'main'
```
```exitcode
1
```

