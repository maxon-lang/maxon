---
feature: if-statements
status: stable
keywords: if, else, conditional, branching, control flow
category: control-flow
---
# If Statements

## Documentation

Execute code conditionally based on a boolean expression.

**Simple If (no else):**

```maxon
if <condition> 'identifier'
  <statements>
end 'identifier'
```

**If-Else:**

The `else` keyword comes after the closing `end` of the if-block, on the same line:

```maxon
if <condition> 'if_id'
  <statements>
end 'if_id' else 'else_id'
  <statements>
end 'else_id'
```

**Else-If Chain:**

```maxon
if <condition1> 'case1'
  <statements>
end 'case1' else if <condition2> 'case2'
  <statements>
end 'case2' else 'default'
  <statements>
end 'default'
```

**Example (simple if):**

```maxon
function main() returns Integer
  var x = 10
  if x > 5 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```


**Example (if-else):**

```maxon
function main() returns Integer
  var x = 3
  if x > 5 'gt5'
    return 1
  end 'gt5' else 'not_gt5'
    return 0
  end 'not_gt5'
end 'main'
```
```exitcode
0
```


**Example (else-if chain):**

```maxon
function main() returns Integer
  var x = 2
  if x == 1 'case1'
    return 1
  end 'case1' else if x == 2 'case2'
    return 2
  end 'case2' else 'default'
    return 0
  end 'default'
end 'main'
```
```exitcode
2
```


**Notes:**
- Block identifier required after `if` condition
- Each branch has its own block identifier
- The `else` keyword appears on the same line as `end 'if_id'`
- Block identifiers must be string literals
- Conditions can be any boolean expression
- Else clause is optional

## Tests

<!-- test: if-statements.simple -->
```maxon
function main() returns Integer
  var x = 10
  if x > 5 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: if-statements.else -->
```maxon
function main() returns Integer
  var x = 5
  if x == 5 'is5'
    return 1
  end 'is5' else 'not5'
    return 0
  end 'not5'
end 'main'
```
```exitcode
1
```

<!-- test: if-statements.else-false -->
```maxon
function main() returns Integer
  var x = 3
  if x > 5 'gt5'
    return 1
  end 'gt5' else 'not_gt5'
    return 0
  end 'not_gt5'
end 'main'
```
```exitcode
0
```

<!-- test: if-statements.else-if-chain -->
```maxon
function main() returns Integer
  var x = 2
  if x == 1 'case1'
    return 1
  end 'case1' else if x == 2 'case2'
    return 2
  end 'case2' else 'default'
    return 0
  end 'default'
end 'main'
```
```exitcode
2
```

<!-- test: if-statements.else-if-in-helper -->
else-if chains in helper functions must be correctly skipped during pre-scan.
```maxon
function classify(x Integer) returns Integer
  if x == 0 'zero'
    return 0
  end 'zero' else if x < 10 'small'
    return 1
  end 'small' else if x < 100 'medium'
    return 2
  end 'medium' else 'large'
    return 3
  end 'large'
end 'classify'

function main() returns Integer
  var a = classify(0)
  var b = classify(5)
  var c = classify(50)
  var d = classify(200)
  return a + b * 10 + c * 100 + d * 1000
end 'main'
```
```exitcode
3210
```

<!-- test: if-statements.nested -->
```maxon
function main() returns Integer
  var x = 3
  if x == 1 'outer'
    return 1
  end 'outer' else 'else_outer'
    if x == 2 'inner'
      return 2
    end 'inner' else 'else_inner'
      return 3
    end 'else_inner'
  end 'else_outer'
end 'main'
```
```exitcode
3
```

<!-- test: if-statements.nested-if-with-scoped-string -->
Variables declared inside if blocks go out of scope at the end of the block.
Return after the if should not attempt to clean up those variables.
```maxon
function test(x Integer) returns Integer
  if x == 0 'outer'
    let inner = "hello"
    if inner == "hello" 'checkInner'
      return 1
    end 'checkInner'
  end 'outer'
  return 42
end 'test'

function main() returns Integer
  return test(5)
end 'main'
```
```exitcode
42
```

<!-- test: if-statements.nested-if-with-multiple-returns -->
Nested if statements with returns inside should work correctly.
The outer if creates a variable that shouldn't be accessed after the if.
```maxon
function test(c Integer, next Integer) returns Integer
  if c == 0 'maybePrefix'
    if next == 1 'isHex'
      return 1
    end 'isHex'
    if next == 2 'isBinary'
      return 2
    end 'isBinary'
  end 'maybePrefix'
  return 42
end 'test'

function main() returns Integer
  return test(5, next: 0)
end 'main'
```
```exitcode
42
```

### Single-line block bodies are not allowed

Block bodies must start on a new line after the label.

<!-- test: if-statements.single-line-block-rejected -->
```maxon
function main() returns Integer
  if true 'x' return 1 end 'x'
  return 0
end 'main'
```
```maxoncstderr
error E2001: specs/fragments/if-statements/if-statements.single-line-block-rejected.test:3:15: Expected newline after block label, got 'return'
```
