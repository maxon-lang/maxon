---
feature: int-parse
status: stable
keywords: [int, parse, string, convert, optional]
category: types
---

# Int Parse

## Developer Notes

The `int.parse()` static method converts a string to an integer, returning an optional `int or nil` to handle invalid input safely.

### Implementation Details

- **Syntax**: `int.parse(str)` where `str` is a `string`
- **Returns**: `int or nil` - the parsed integer or `nil` if parsing fails
- **Valid input**: Optional leading `-` or `+`, followed by one or more digits
- **Invalid input**: Returns `nil` for empty strings, non-numeric characters, overflow

### Key Files

- `semantic_analyzer.cpp`: Register `int.parse` as a static method with `isStaticMethod = true`
- `codegen_mir/codegen_mir_expr.cpp`: Handle `int.parse` call, invoke runtime function
- `maxon-runtime/runtime_windows.mir`: Implement `__int_parse` runtime function

### Runtime Function

The `__int_parse` function signature:
```
%optional_int @__int_parse(ptr %string_ptr)
```

Returns an optional int (discriminated union with tag + value).

## Documentation

The `int.parse()` static method converts a string to an integer. Since parsing can fail (e.g., for non-numeric strings), it returns an optional `int or nil`.

### Syntax

```text
var result = int.parse("42")    // int or nil
```

### Example

```maxon
function main() returns int
    var input = "123"
    if let value = int.parse(input) 'success'
        print("{value}\n")
        return value
    end 'success' else 'failure'
        print("Failed to parse")
        return -1
    end 'failure'
end 'main'
```
```exitcode
123
```
```stdout
123
```

### Handling Invalid Input

```maxon
function main() returns int
    var invalid = "abc"
    if let value = int.parse(invalid) 'check'
        return value
    end 'check' else 'failed'
        return 0
    end 'failed'
end 'main'
```
```exitcode
0
```

### Supported Formats

- Positive integers: `"123"`, `"+456"`
- Negative integers: `"-789"`
- Leading/trailing whitespace is not allowed
- Empty strings return `nil`

## Tests

<!-- test: basic-parse -->
```maxon
function main() returns int
    if let value = int.parse("42") 'check'
        return value
    end 'check' else 'fail'
        return -1
    end 'fail'
end 'main'
```
```exitcode
42
```

<!-- test: parse-negative -->
```maxon
function main() returns int
    if let value = int.parse("-123") 'check'
        // Return 1 if value is -123, else 0
        if value == -123 'match'
            return 1
        end 'match'
        return 0
    end 'check' else 'fail'
        return 0
    end 'fail'
end 'main'
```
```exitcode
1
```

<!-- test: parse-positive-sign -->
```maxon
function main() returns int
    if let value = int.parse("+99") 'check'
        return value
    end 'check' else 'fail'
        return 0
    end 'fail'
end 'main'
```
```exitcode
99
```

<!-- test: parse-zero -->
```maxon
function main() returns int
    if let value = int.parse("0") 'check'
        return value
    end 'check' else 'fail'
        return -1
    end 'fail'
end 'main'
```
```exitcode
0
```

<!-- test: parse-invalid-returns-nil -->
```maxon
function main() returns int
    if let value = int.parse("abc") 'check'
        return value
    end 'check' else 'fail'
        return 42
    end 'fail'
end 'main'
```
```exitcode
42
```

<!-- test: parse-empty-returns-nil -->
```maxon
function main() returns int
    if let value = int.parse("") 'check'
        return value
    end 'check' else 'fail'
        return 99
    end 'fail'
end 'main'
```
```exitcode
99
```

<!-- test: parse-mixed-content-returns-nil -->
```maxon
function main() returns int
    if let value = int.parse("12abc") 'check'
        return value
    end 'check' else 'fail'
        return 77
    end 'fail'
end 'main'
```
```exitcode
77
```

<!-- test: parse-with-else-unwrap -->
```maxon
function main() returns int
    var result = int.parse("not_a_number") else 'default'
        result = 100
    end 'default'
    return result
end 'main'
```
```exitcode
100
```

<!-- test: parse-large-number -->
```maxon
function main() returns int
    if let value = int.parse("2000000000") 'check'
        if value > 1999999999 'big'
            return 1
        end 'big'
        return 0
    end 'check' else 'fail'
        return -1
    end 'fail'
end 'main'
```
```exitcode
1
```
