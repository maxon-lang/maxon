---
feature: stdlib-basic
status: stable
keywords: [stdlib, multi-file, import, exp]
category: infrastructure
---

# Stdlib Basic

## Developer Notes

This tests basic stdlib integration - the ability to call functions from stdlib modules in user code.

**How it works:**
1. CLI finds stdlib path relative to executable
2. Loads `stdlib/Math.maxon` module
3. Compiles stdlib first, then user code
4. IR modules are merged before codegen
5. User code can call `Math.exp()` function

**Key Components:**
- `findStdlibPath()` - Locates stdlib directory
- `loadStdlibModule()` - Loads a stdlib module by name
- `compileWithStdlib()` - Multi-file compilation with stdlib first
- `Module.merge()` - Merges IR from multiple sources

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
function main() returns int
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
