---
feature: format-float
status: stable
keywords: [format, float, conversion, string]
category: stdlib
---

# Format Float Function

## Developer Notes

The `formatFloatArray()` function converts a float to a string representation with specified precision, storing it in a character array buffer.

Implementation:
- Defined in `stdlib/fmt/format_float.maxon`
- Signature: `formatFloatArray(value float, buffer array of byte, precision int) int`
- Returns the number of characters written
- Handles negative numbers, zero, and positive values
- Uses `trunc()` to separate integer and fractional parts
- Auto-discovered by compiler stdlib system

Algorithm:
1. Handle zero case specially
2. Add minus sign for negative values
3. Extract integer part with `trunc()`
4. Convert integer digits
5. Add decimal point
6. Convert fractional digits based on precision

## Documentation

The `formatFloatArray()` function converts a float to a string with specified decimal precision.

### Syntax

```maxon
formatFloatArray(value, buffer, precision)
```
Parameters:
- `value` - The float to convert
- `buffer` - Character array to store result
- `precision` - Number of decimal places

Returns the number of characters written to the buffer.

### Example

```maxon
function main() int
    var buffer = array of 50 byte
    var len = formatFloatArray(3.14159, buffer, 2)
    // buffer now contains "3.14"
    return len  // Returns 4
end 'main'
```
```exitcode
4
```


## Tests

<!-- test: basic -->
```maxon
function main() int
    var buffer = array of 50 byte
    var len = formatFloatArray(3.14, buffer, 2)
    return len
end 'main'
```
```exitcode
4
```


<!-- test: zero -->
```maxon
function main() int
    var buffer = array of 50 byte
    var len = formatFloatArray(0.0, buffer, 6)
    return len
end 'main'
```
```exitcode
8
```


<!-- test: negative -->
```maxon
function main() int
    var buffer = array of 50 byte
    var len = formatFloatArray(-2.5, buffer, 1)
    return len
end 'main'
```
```exitcode
4
```


<!-- test: high-precision -->
```maxon
function main() int
    var buffer = array of 50 byte
    var len = formatFloatArray(3.14159, buffer, 5)
    return len
end 'main'
```
```exitcode
7
```

