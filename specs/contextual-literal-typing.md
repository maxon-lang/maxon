---
feature: contextual-literal-typing
status: stable
keywords: [literals, types, contextual, byte, int, type-inference]
category: type-system
---

# Contextual Literal Typing

## Developer Notes

Contextual literal typing allows integer and byte literals to adapt to their expected type context, without implicit promotion between different type categories.

Implementation:
- Located in `SemanticAnalyzer::analyzeComparisonOperands()` in `semantic_analyzer_expr.cpp`
- Applies to comparison operators: `==`, `!=`, `<`, `>`, `<=`, `>=`
- Also applies to function argument type checking

Rules:
1. **Int literal in byte context**: An `int` literal (0-255) can match a `byte` type
2. **Byte literal in int context**: A `byte` literal can match an `int` type
3. **No int↔float adaptation**: Int literals cannot adapt to float; float literals cannot adapt to int
4. **No implicit promotion**: Types must match exactly (after contextual adaptation)

Key distinction from implicit promotion:
- Contextual typing only affects LITERALS at compile time
- Variables never change type - a `byte` variable cannot be compared to an `int` variable
- Mixed int/float comparisons are compile errors, not implicit promotions

AST nodes involved:
- `NumberExprAST` - integer literals
- `ByteExprAST` - byte literals
- `FloatExprAST` - float literals

## Documentation

Maxon uses contextual literal typing to allow integer and byte literals to adapt to their expected type context in comparisons and function calls.

### Byte and Int Literals

Integer literals in the range 0-255 can be compared directly with byte values:

```maxon
function main() int
    var b = 100b
    if b == 50 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

Byte literals can be compared directly with int values:

```maxon
function main() int
    var x = 200
    if x == 200b 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

### No Int/Float Mixing

Comparisons between int and float types require explicit casts:

```text
var x = 5
var y = 5.0
if x == y 'check'    // Error: type mismatch
    return 1
end 'check'
```

To compare, cast explicitly:

```maxon
function main() int
    var x = 5
    var y = 5.0
    if (x as float) == y 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

### Math Intrinsics

Math intrinsics like `sin`, `cos`, `sqrt`, etc. accept both int and float arguments (int is promoted to float):

```maxon
function main() int
    var x = sqrt(16.0)
    return trunc(x)
end 'main'
```
```exitcode
4
```

## Tests

<!-- test: int-literal-vs-byte-valid -->
```maxon
function main() int
    var b = 42b
    if b == 42 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: int-literal-vs-byte-out-of-range -->
```maxon
function main() int
    var b = 100b
    if b == 300 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 4, column 10
Comparison operators require operands of compatible types
  Left operand type: byte
  Right operand type: int

  4 |     if b == 300 'check'
    |          ^

Semantic Error: line 4, column 5
If condition must be a boolean or integer expression
  Found type: error
  Note: Conditions should evaluate to true/false or use comparison operators (=, !=, <, >, <=, >=)

  4 |     if b == 300 'check'
    |     ^
```

<!-- test: byte-literal-vs-int -->
```maxon
function main() int
    var x = 100
    if x == 100b 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: int-vs-float-error -->
```maxon
function main() int
    var x = 5
    var y = 5.0
    if x == y 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 5, column 10
Comparison operators require operands of compatible types
  Left operand type: int
  Right operand type: float

  5 |     if x == y 'check'
    |          ^

Semantic Error: line 5, column 5
If condition must be a boolean or integer expression
  Found type: error
  Note: Conditions should evaluate to true/false or use comparison operators (=, !=, <, >, <=, >=)

  5 |     if x == y 'check'
    |     ^
```

<!-- test: float-vs-int-error -->
```maxon
function main() int
    var x = 5.0
    var y = 5
    if x == y 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 5, column 10
Comparison operators require operands of compatible types
  Left operand type: float
  Right operand type: int

  5 |     if x == y 'check'
    |          ^

Semantic Error: line 5, column 5
If condition must be a boolean or integer expression
  Found type: error
  Note: Conditions should evaluate to true/false or use comparison operators (=, !=, <, >, <=, >=)

  5 |     if x == y 'check'
    |     ^
```

<!-- test: int-literal-vs-float-error -->
```maxon
function main() int
    var x = 5.0
    if x == 5 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 4, column 10
Comparison operators require operands of compatible types
  Left operand type: float
  Right operand type: int

  4 |     if x == 5 'check'
    |          ^

Semantic Error: line 4, column 5
If condition must be a boolean or integer expression
  Found type: error
  Note: Conditions should evaluate to true/false or use comparison operators (=, !=, <, >, <=, >=)

  4 |     if x == 5 'check'
    |     ^
```

<!-- test: float-literal-vs-int-error -->
```maxon
function main() int
    var x = 5
    if x == 5.0 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 4, column 10
Comparison operators require operands of compatible types
  Left operand type: int
  Right operand type: float

  4 |     if x == 5.0 'check'
    |          ^

Semantic Error: line 4, column 5
If condition must be a boolean or integer expression
  Found type: error
  Note: Conditions should evaluate to true/false or use comparison operators (=, !=, <, >, <=, >=)

  4 |     if x == 5.0 'check'
    |     ^
```

<!-- test: explicit-cast-int-to-float -->
```maxon
function main() int
    var x = 5
    var y = 5.0
    if (x as float) == y 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: explicit-cast-float-to-int -->
```maxon
function main() int
    var x = 5
    var y = 5.0
    if x == trunc(y) 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: math-intrinsic-with-int -->
```maxon
function main() int
    var x = 16
    var result = sqrt(x)
    return trunc(result)
end 'main'
```
```exitcode
4
```

<!-- test: math-intrinsic-with-float-literal -->
```maxon
function main() int
    var x = sqrt(16.0)
    return trunc(x)
end 'main'
```
```exitcode
4
```

<!-- test: byte-vs-byte -->
```maxon
function main() int
    var a = 50b
    var b = 50b
    if a == b 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: int-vs-int -->
```maxon
function main() int
    var a = 1000
    var b = 1000
    if a == b 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: float-vs-float -->
```maxon
function main() int
    var a = 3.14
    var b = 3.14
    if a == b 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

