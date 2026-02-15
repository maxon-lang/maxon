---
feature: stdlib-basic
status: stable
keywords: [stdlib, multi-file, import, exp]
category: infrastructure
---

# Stdlib Basic

## Documentation

### Standard Library

The Maxon standard library provides commonly used functions and types. Functions from stdlib are automatically available in your code.

### Math Functions

The `Math.exp(x)` function computes e^x (the exponential function).

```text
var result = Math.exp(0.0)  // returns 1.0
var e = Math.exp(1.0)       // returns ~2.718
```

## Tests

<!-- test: stdlib-call-exp -->
```maxon
function main() returns Integer
  var result = Math.exp(0.0)
  if result == 1.0 'check'
    return 42
  end 'check'
  return 0
end 'main'
```
```exitcode
42
```
