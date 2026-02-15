---
feature: match-statements
status: experimental
keywords: match, then, gives, fallthrough, default
category: control-flow
---
# Match Statements

## Documentation

Match statements provide pattern matching on values, allowing you to execute different code based on the value of an expression. Each case is a single line with exactly one statement.

## Match Statement Syntax

```maxon
match <expression> 'identifier'
  <pattern> then <statement>
  <pattern1> or <pattern2> then <statement>
  default then <statement>
end 'identifier'
```

**Example (simple match):**

```maxon
function main() returns ExitCode
  var x = 2
  match x 'check'
    1 then return 10
    2 then return 20
    default then return 0
  end 'check'
end 'main'
```
```exitcode
20
```

## Multiple Patterns with `or`

You can match multiple patterns in a single case using the `or` keyword:

```maxon
function main() returns ExitCode
  var input = 3
  match input 'eval'
    1 or 2 then return 10
    3 or 4 or 5 then return 20
    default then return 0
  end 'eval'
end 'main'
```
```exitcode
20
```

## Match Expressions

Match expressions return a value and can be used in variable assignments. Use `gives` instead of `then`:

```maxon
function main() returns ExitCode
  var x = 2
  let result = match x 'convert'
    1 gives 10
    2 gives 20
    3 gives 30
    default gives 0
  end 'convert'
  return result
end 'main'
```
```exitcode
20
```

## Fallthrough

By default, only the matching case executes. Use `and fallthrough` to continue to the next case's statement:

```maxon
function main() returns ExitCode
  var role = 1
  var permissions = 0
  match role 'auth'
    1 then permissions = permissions + 100 and fallthrough
    2 then permissions = permissions + 10 and fallthrough
    3 then permissions = permissions + 1
    default then permissions = 0
  end 'auth'
  return permissions
end 'main'
```
```exitcode
111
```

When `role = 1`, the first case matches (adds 100), falls through to case 2 (adds 10), falls through to case 3 (adds 1), giving a total of 111.

**Note:** Fallthrough is NOT allowed in match expressions since they must return a single value.

## Exhaustiveness for Enums

When matching on enum values, all enum cases must be covered. The `default` keyword is not allowed when matching on enums — every case must be listed explicitly:

```maxon
enum Direction
  north
  south
  east
  west
end 'Direction'

function main() returns ExitCode
  var dir = Direction.north
  match dir 'navigate'
    Direction.north then return 1
    Direction.south then return 2
    Direction.east then return 3
    Direction.west then return 4
  end 'navigate'
end 'main'
```
```exitcode
1
```

If any enum case is missing, the compiler will report an error listing the missing cases.


## Rules

- Block identifier required after `match <expression>` and on `end`
- Each case is a single line with exactly one statement
- All patterns in a case must be type-compatible with the scrutinee
- `and fallthrough` continues to the next case's statement
- `and fallthrough` not allowed in match expressions
- `and fallthrough` cannot be combined with `return`
- For enums, all cases must be covered explicitly — `default` is not allowed
- `default` matches any value not matched by previous patterns (non-enum types only)
- `default` must be the last case if present

## Tests

<!-- test: match-statements.simple -->
```maxon
function main() returns ExitCode
  var x = 2
  match x 'check'
    1 then return 10
    2 then return 20
    default then return 0
  end 'check'
end 'main'
```
```exitcode
20
```

<!-- test: match-statements.default -->
```maxon
function main() returns ExitCode
  var x = 99
  match x 'check'
    1 then return 10
    2 then return 20
    default then return 0
  end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: match-statements.first-case -->
```maxon
function main() returns ExitCode
  var x = 1
  match x 'check'
    1 then return 10
    2 then return 20
    default then return 0
  end 'check'
end 'main'
```
```exitcode
10
```

<!-- test: match-statements.or-patterns -->
```maxon
function main() returns ExitCode
  var x = 3
  match x 'check'
    1 or 2 then return 10
    3 or 4 or 5 then return 20
    default then return 0
  end 'check'
end 'main'
```
```exitcode
20
```

<!-- test: match-statements.or-patterns-first -->
```maxon
function main() returns ExitCode
  var x = 1
  match x 'check'
    1 or 2 then return 10
    3 or 4 then return 20
    default then return 0
  end 'check'
end 'main'
```
```exitcode
10
```

<!-- test: match-statements.or-patterns-second -->
```maxon
function main() returns ExitCode
  var x = 2
  match x 'check'
    1 or 2 then return 10
    3 or 4 then return 20
    default then return 0
  end 'check'
end 'main'
```
```exitcode
10
```


<!-- test: match-expression.basic -->
```maxon
function main() returns ExitCode
  var x = 2
  let result = match x 'eval'
    1 gives 10
    2 gives 20
    default gives 0
  end 'eval'
  return result
end 'main'
```
```exitcode
20
```

<!-- test: match-expression.or-patterns -->
```maxon
function main() returns ExitCode
  var x = 4
  let result = match x 'eval'
    1 or 2 gives 10
    3 or 4 gives 20
    default gives 0
  end 'eval'
  return result
end 'main'
```
```exitcode
20
```

<!-- test: match-expression.default -->
```maxon
function main() returns ExitCode
  var x = 99
  let result = match x 'eval'
    1 gives 10
    2 gives 20
    default gives 0
  end 'eval'
  return result
end 'main'
```
```exitcode
0
```


<!-- test: match-statements.fallthrough -->
```maxon
function main() returns ExitCode
  var x = 1
  var result = 0
  match x 'check'
    1 then result = result + 10 and fallthrough
    2 then result = result + 20
    default then result = result + 100
  end 'check'
  return result
end 'main'
```
```exitcode
30
```

<!-- test: match-statements.fallthrough-chain -->
```maxon
function main() returns ExitCode
  var x = 1
  var result = 0
  match x 'cascade'
    1 then result = result + 10 and fallthrough
    2 then result = result + 20 and fallthrough
    3 then result = result + 30
    default then result = 100
  end 'cascade'
  return result
end 'main'
```
```exitcode
60
```

<!-- test: match-statements.fallthrough-to-default -->
```maxon
function main() returns ExitCode
  var x = 3
  var result = 0
  match x 'check'
    1 then result = 10
    2 then result = 20
    3 then result = result + 30 and fallthrough
    default then result = result + 100
  end 'check'
  return result
end 'main'
```
```exitcode
130
```

<!-- test: match-statements.nested-in-function -->
```maxon
function categorize(n Integer) returns Integer
  match n 'cat'
    1 or 2 or 3 then return 1
    4 or 5 or 6 then return 2
    default then return 0
  end 'cat'
end 'categorize'

function main() returns ExitCode
  return categorize(5)
end 'main'
```
```exitcode
2
```

<!-- test: match-statements.assignment -->
```maxon
function main() returns ExitCode
  var x = 2
  var result = 0
  match x 'process'
    1 then result = 100
    2 then result = 200
    default then result = 0
  end 'process'
  return result
end 'main'
```
```exitcode
200
```

<!-- test: match-statements.function-call -->
```maxon
function double(n Integer) returns Integer
  return n * 2
end 'double'

function main() returns ExitCode
  var x = 2
  var result = 0
  match x 'process'
    1 then result = double(10)
    2 then result = double(20)
    default then result = 0
  end 'process'
  return result
end 'main'
```
```exitcode
40
```

<!-- test: match-enum.exhaustive -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var c = Color.green
  match c 'check'
    Color.red then return 1
    Color.green then return 2
    Color.blue then return 3
  end 'check'
end 'main'
```
```exitcode
2
```

<!-- test: error.match-enum-default -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var c = Color.blue
  match c 'check'
    Color.red then return 1
    default then return 0
  end 'check'
end 'main'
```
```maxoncstderr
error E2044: specs/fragments/match-simple/error.match-enum-default.test:13:3: 'default' is not allowed when matching on enum 'Color', all cases must be listed explicitly
```

<!-- test: match-enum.expression -->
```maxon
enum Status
  pending
  approved
  rejected
end 'Status'

function main() returns ExitCode
  var s = Status.approved
  let code = match s 'eval'
    Status.pending gives 0
    Status.approved gives 1
    Status.rejected gives 2
  end 'eval'
  return code
end 'main'
```
```exitcode
1
```

<!-- test: match-expression.used-in-expression -->
```maxon
function main() returns ExitCode
  var x = 2
  let doubled = match x 'eval'
    1 gives 10
    2 gives 20
    default gives 0
  end 'eval' * 2
  return doubled
end 'main'
```
```exitcode
40
```

<!-- test: error.match-expression-fallthrough -->
```maxon
function main() returns ExitCode
  var x = 1
  let result = match x 'eval'
    1 gives 10 and fallthrough
    default gives 0
  end 'eval'
  return result
end 'main'
```
```maxoncstderr
error E2001: specs/fragments/match-simple/error.match-expression-fallthrough.test:5:16: unexpected token: 'and'
```

<!-- test: error.match-fallthrough-with-return -->
```maxon
function main() returns ExitCode
  var x = 1
  match x 'check'
    1 then return 10 and fallthrough
    default then return 0
  end 'check'
end 'main'
```
```maxoncstderr
error E2025: specs/fragments/match-simple/error.match-fallthrough-with-return.test:5:22: match fallthrough with return: 'cannot combine 'fallthrough' with 'return''
```

<!-- test: error.match-enum-not-exhaustive -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var c = Color.green
  match c 'check'
    Color.red then return 1
    Color.green then return 2
  end 'check'
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/match-simple/error.match-enum-not-exhaustive.test:13:3: match on enum 'Color' is not exhaustive, missing: blue
```

<!-- test: error.match-duplicate-pattern -->
```maxon
function main() returns ExitCode
  var x = 1
  match x 'check'
    1 then return 10
    1 then return 20
    default then return 0
  end 'check'
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/match-simple/error.match-duplicate-pattern.test:6:5: duplicate pattern in match: '1'
```

<!-- test: error.match-missing-block-id -->
```maxon
function main() returns ExitCode
  var x = 1
  match x
    1 then return 10
    default then return 0
  end
end 'main'
```
```maxoncstderr
error E2042: specs/fragments/match-simple/error.match-missing-block-id.test:4:10: missing block identifier
```

<!-- test: error.match-mismatched-block-id -->
```maxon
function main() returns ExitCode
  var x = 1
  match x 'check'
    1 then return 10
    default then return 0
  end 'wrong'
end 'main'
```
```maxoncstderr
error E2043: specs/fragments/match-simple/error.match-mismatched-block-id.test:7:3: block identifier mismatch: expected 'check', got 'wrong'
```

<!-- test: error.match-default-not-last -->
```maxon
function main() returns ExitCode
  var x = 1
  match x 'check'
    default then return 0
    1 then return 10
    2 then return 20
  end 'check'
end 'main'
```
```maxoncstderr
error E2029: specs/fragments/match-simple/error.match-default-not-last.test:6:5: 'default' case must be the last case in match
```
