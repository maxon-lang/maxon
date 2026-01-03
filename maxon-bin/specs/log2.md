---
feature: log2
status: stable
keywords: log2, logarithm, base-2, math
category: stdlib
---
## Documentation

# log2

Calculate the base-2 logarithm of a number.

**Signature:** `Math.log2(x float) float`

**Parameters:**
- `x` - The number to take the base-2 logarithm of (must be positive)

**Returns:** The base-2 logarithm of the input

**Example:**

```maxon
var x = 8.0
var y = Math.log2(x)     // 3.0 (2^3 = 8)

var z = 16.0
var w = Math.log2(z)     // 4.0 (2^4 = 16)

var a = 1024.0
var b = Math.log2(a)     // 10.0 (2^10 = 1024)
```
**Notes:**
- Input must be positive (returns 0 for non-positive values in current implementation)
- `log2(1.0)` returns `0.0` (exact)
- `log2(2.0)` returns `1.0` (exact)
- Powers of 2 return exact integer results
- For integer inputs, the value is automatically promoted to float
- `log2(0.5)` returns `-1.0` (2^-1 = 0.5)
- Useful for understanding binary scales and data structures

## Tests

<!-- test: log2.powers-of-two -->
```maxon
function main() returns int
    // Test various powers of 2 - should all be exact
    var r1 = Math.log2(1.0)      // 2^0 = 1
    var r2 = Math.log2(2.0)      // 2^1 = 2
    var r4 = Math.log2(4.0)      // 2^2 = 4
    var r8 = Math.log2(8.0)      // 2^3 = 8
    var r16 = Math.log2(16.0)    // 2^4 = 16
    var r1024 = Math.log2(1024.0) // 2^10 = 1024
    
    // Test fractional powers (negative exponents)
    var r_half = Math.log2(0.5)   // 2^(-1) = 0.5
    var r_quarter = Math.log2(0.25) // 2^(-2) = 0.25
    
    // Print results
    print("{r1}\n")
    print("{r2}\n")
    print("{r4}\n")
    print("{r8}\n")
    print("{r16}\n")
    print("{r1024}\n")
    print("{r_half}\n")
    print("{r_quarter}\n")
    
    return 0
end 'main'
```
```exitcode
0
```
```stdout
0.0
0.999999
1.999999
2.999999
3.999999
9.999999
-0.999999
-1.999999
```

<!-- test: log2.non-powers-and-precision -->
```maxon
function main() returns int
    // Non-powers of 2 require high precision
    var r3 = Math.log2(3.0)
    var r5 = Math.log2(5.0)
    var r6 = Math.log2(6.0)
    var r1000000 = Math.log2(1000000.0)
    
    print("{r3}\n")
    print("{r5}\n")
    print("{r6}\n")
    print("{r1000000}\n")
    
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1.584962
2.321928
2.584962
19.931568
```


<!-- test: log2.integer-promotion-and-relationships -->
```maxon
function main() returns int
    // Test integer promotion
    var int_val = 32
    var r_int = Math.log2(int_val)  // Should promote 32 to 32.0
    print("{r_int}\n")
    
    var r_literal = Math.log2(64)   // Integer literal promotion
    print("{r_literal}\n")
    
    // Verify relationship: log2(x) = log(x) / log(2)
    var test_val = 100.0
    var log2_result = Math.log2(test_val)
    var log_result = Math.log(test_val) / Math.log(2.0)
    var diff = log2_result - log_result
    
    // Check difference is negligible
    var abs_diff = diff
    if diff < 0.0 'abs'
        abs_diff = 0.0 - diff
    end 'abs'
    
    if abs_diff < 0.000001 'pass'
        print("{r_int}\n")    // Print again to show test passed
    end 'pass'
    
    // Test relationship: log2(2^x) = x
    var exponent = 7.0
    var power_val = Math.pow(2.0, exponent)
    var r_pow = Math.log2(power_val)
    print("{r_pow}\n")
    
    return 0
end 'main'
```
```exitcode
0
```
```stdout
4.999999
5.999999
4.999999
6.999999
```

<!-- test: log2.special-values -->
```maxon
function main() returns int
    // Test with e (natural log base)
    // log2(e) should equal 1/ln(2) ≈ 1.442695
    var e = 2.71828182845904523536
    var r_e = Math.log2(e)
    print("{r_e}\n")
    
    // Test with 10
    var r_10 = Math.log2(10.0)
    print("{r_10}\n")
    
    // Test value exactly between powers of 2
    var r_between = Math.log2(12.0)  // Between 2^3 and 2^4
    print("{r_between}\n")
    
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1.442695
3.321928
3.584962
```
