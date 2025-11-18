# Variables

A variable is a unit of Memory storage.

It is generally preferable to use const rather than var when declaring a variable. This causes less work for both humans and computers to do when reading code, and creates more optimization opportunities.

The extern keyword or @extern builtin function can be used to link against a variable that is exported from another object. The export keyword or @export builtin function can be used to make a variable available to other objects at link time. In both cases, the type of the variable must be C ABI compatible.

See also:

Exporting a C Library


## Identifiers 
Variable identifiers are never allowed to shadow identifiers from an outer scope.

~~~
function main() int
    var x = 1
    return x
end 'main'

ExitCode: 1
~~~

Identifiers must start with an alphabetic character or underscore and may be followed by any number of alphanumeric characters or underscores. They must not overlap with any keywords. See Keyword Reference.

~~~
function main() int
    var x = 4
    return x * 3
end 'main'

ExitCode: 12
~~~

## Types

Variables in Maxon are statically typed. The type must be specified in the declaration.

### Integer Variables

The `int` type is a 32-bit signed integer:

~~~
function main() int
    var count = 42
    var negative = -100
    return count + negative + 158
end 'main'

ExitCode: 100
~~~

### Float Variables

The `float` type is a 64-bit floating-point number (IEEE 754 double precision). Float literals **must** include a decimal point:

~~~
function main() int
    var pi = 3.14159
    var half = 0.5
    var result = pi + half
    return trunc(result)  // Truncate to int: 3
end 'main'

ExitCode: 3
~~~

**Important:** Float literals must use a leading zero before the decimal point:

```maxon
var valid = 0.5      // ✓ Valid
var invalid = .5     // ✗ Invalid - must use 0.5
```

To create a float from an integer literal, add `.0`:

~~~
function main() int
    var x = 42.0         // float
    var y = 1            // int
    var z = x + y        // y promoted to float, z is float (43.0)
    return trunc(z)      // 43
end 'main'

ExitCode: 43
~~~

### Type Coercion

In mixed int/float expressions, integers are automatically promoted to floats:

~~~
function main() int
    var x = 5            // int
    var y = 2.5          // float
    var result = x + y   // x promoted to 5.0, result is 7.5
    return trunc(result) // 7
end 'main'

ExitCode: 7
~~~
