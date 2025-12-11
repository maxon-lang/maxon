---
feature: match-statements
status: experimental
keywords: match, then, gives, fallthrough, default
category: control-flow
---

## Developer Notes

Match statements and expressions provide pattern matching on values with support for multiple patterns, fallthrough behavior, and exhaustiveness checking for enums.

**Implementation Details:**
- Keywords: `match`, `fallthrough`, `default`, `gives` (lexer_keyword_matcher.h)
- Parser: `parseMatch()` and `parseMatchExpr()` in parser_stmt.cpp/parser_expr.cpp
- AST nodes: `MatchStmtAST`, `MatchExprAST`, `MatchCaseAST` (ast.h)
- Requires block identifiers for match statements/expressions
- Semantic analysis validates pattern types match scrutinee
- Exhaustiveness checking for enum types when no default present

**Syntax:**

**Match statement (each case is a single line with one statement):**
```text
match <expr> 'identifier'
    <pattern> then <statement>
    <pattern1> or <pattern2> then <statement>
    <pattern> then <statement> and fallthrough
    default then <statement>
end 'identifier'
```

**Match expression:**
```text
var result = match <expr> 'identifier'
    <pattern1> gives <expr1>
    <pattern2> or <pattern3> gives <expr2>
    default gives <defaultExpr>
end 'identifier'
```

**Key Semantic Rules:**
1. Each case is a single line containing exactly one statement
2. All patterns in a case must be compatible with the scrutinee type
3. `and fallthrough` causes execution to continue to the next case's statement
4. `and fallthrough` cannot be combined with `return` in the same case
5. Match expressions do not support `fallthrough`
6. For enum types without `default`, all cases must be matched (exhaustiveness)
7. Duplicate patterns are an error
8. `default` case matches any value not matched by previous patterns
9. `default` must be the last case if present

**Code Generation:**
- Creates chain of comparison blocks for pattern matching
- Each pattern generates equality check against scrutinee
- Multiple patterns (via `or`) generate OR'd comparisons
- Fallthrough branches directly to next case body (skipping its pattern check)
- Default case is the fallback when no patterns match
- Match expressions generate PHI nodes to collect result values

**MIR Structure:**
```text
entry:
    %scrutinee = <evaluate scrutinee>
    br case_0_check

case_0_check:
    %cmp0 = icmp eq %scrutinee, pattern0
    condbr %cmp0, case_0_body, case_1_check

case_0_body:
    <single statement>
    br merge  ; or next case body if fallthrough

case_1_check:
    %cmp1a = icmp eq %scrutinee, pattern1a
    %cmp1b = icmp eq %scrutinee, pattern1b
    %cmp1 = or %cmp1a, %cmp1b
    condbr %cmp1, case_1_body, default_body

default_body:
    <single statement>
    br merge

merge:
    ; continuation
```

## Documentation

# Match Statements

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
function main() returns int
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
function main() returns int
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
function main() returns int
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
function main() returns int
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

When matching on enum values without a `default` case, all enum cases must be covered:

```maxon
enum Direction
    north
    south
    east
    west
end 'Direction'

function main() returns int
    var dir = Direction.north
    match dir 'navigate'
        Direction.north then return 1
        Direction.south then return 2
        Direction.east then return 3
        Direction.west then return 4
    end 'navigate'
end 'main'
```

If any enum case is missing, the compiler will report an error listing the missing cases.

## String Matching

Match works with string values:

```maxon
function main() returns int
    var input = "Y"
    match input 'confirm'
        "y" or "Y" then return 1
        "n" or "N" then return 0
        default then return -1
    end 'confirm'
end 'main'
```
```exitcode
1
```

## Rules

- Block identifier required after `match <expression>` and on `end`
- Each case is a single line with exactly one statement
- All patterns in a case must be type-compatible with the scrutinee
- `and fallthrough` continues to the next case's statement
- `and fallthrough` not allowed in match expressions
- `and fallthrough` cannot be combined with `return`
- For enums, all cases must be covered unless `default` is present
- `default` matches any value not matched by previous patterns
- `default` must be the last case if present

## Tests

<!-- test: match-statements.simple -->
```maxon
function main() returns int
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
function main() returns int
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
function main() returns int
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
function main() returns int
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
function main() returns int
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
function main() returns int
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

<!-- test: match-statements.string -->
```maxon
function main() returns int
    var input = "Y"
    match input 'confirm'
        "y" or "Y" then return 1
        "n" or "N" then return 0
        default then return -1
    end 'confirm'
end 'main'
```
```exitcode
1
```

<!-- test: match-statements.string-lowercase -->
```maxon
function main() returns int
    var input = "n"
    match input 'confirm'
        "y" or "Y" then return 1
        "n" or "N" then return 0
        default then return -1
    end 'confirm'
end 'main'
```
```exitcode
0
```

<!-- test: match-expression.basic -->
```maxon
function main() returns int
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
function main() returns int
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
function main() returns int
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

<!-- test: match-expression.string -->
```maxon
function main() returns int
    var grade = "B"
    let points = match grade 'grade'
        "A" gives 4
        "B" gives 3
        "C" gives 2
        default gives 0
    end 'grade'
    return points
end 'main'
```
```exitcode
3
```

<!-- test: match-statements.fallthrough -->
```maxon
function main() returns int
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
function main() returns int
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
function main() returns int
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
function categorize(n int) returns int
    match n 'cat'
        1 or 2 or 3 then return 1
        4 or 5 or 6 then return 2
        default then return 0
    end 'cat'
end 'categorize'

function main() returns int
    return categorize(5)
end 'main'
```
```exitcode
2
```

<!-- test: match-statements.assignment -->
```maxon
function main() returns int
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
function double(n int) returns int
    return n * 2
end 'double'

function main() returns int
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

function main() returns int
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

<!-- test: match-enum.with-default -->
```maxon
enum Color
    red
    green
    blue
end 'Color'

function main() returns int
    var c = Color.blue
    match c 'check'
        Color.red then return 1
        default then return 0
    end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: match-enum.expression -->
```maxon
enum Status
    pending
    approved
    rejected
end 'Status'

function main() returns int
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
function main() returns int
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
function main() returns int
    var x = 1
    let result = match x 'eval'
        1 gives 10 and fallthrough
        default gives 0
    end 'eval'
    return result
end 'main'
```
```maxoncstderr
In file 'temp/temp_fragment.maxon':
Expected expression
  Found: 'fallthrough'
  Note: An expression can be a number, variable, function call, or arithmetic/comparison operation
  Location: line 5, column 24
```

<!-- test: error.match-fallthrough-with-return -->
```maxon
function main() returns int
    var x = 1
    match x 'check'
        1 then return 10 and fallthrough
        default then return 0
    end 'check'
end 'main'
```
```maxoncstderr
Semantic Error: line 5, column 9
Cannot combine 'fallthrough' with 'return' statement

  5 |         1 then return 10 and fallthrough
    |         ^
```

<!-- test: error.match-enum-not-exhaustive -->
```maxon
enum Color
    red
    green
    blue
end 'Color'

function main() returns int
    var c = Color.green
    match c 'check'
        Color.red then return 1
        Color.green then return 2
    end 'check'
end 'main'
```
```maxoncstderr
Semantic Error: line 10, column 5
Match on enum 'Color' is not exhaustive
  Missing cases: blue

  10 |     match c 'check'
     |     ^

Semantic Error: line 8, column 1
Function 'main' must return a value of type 'int'
  Note: All execution paths through the function must end with a return statement

  8 | function main() returns int
    | ^
```

<!-- test: error.match-duplicate-pattern -->
```maxon
function main() returns int
    var x = 1
    match x 'check'
        1 then return 10
        1 then return 20
        default then return 0
    end 'check'
end 'main'
```
```maxoncstderr
Semantic Error: line 6, column 9
Duplicate pattern '1' in match

  6 |         1 then return 20
    |         ^
```

<!-- test: error.match-type-mismatch -->
```maxon
function main() returns int
    var x = 1
    match x 'check'
        "one" then return 10
        default then return 0
    end 'check'
end 'main'
```
```maxoncstderr
Semantic Error: line 5, column 9
Pattern type 'string' does not match scrutinee type 'int'

  5 |         "one" then return 10
    |         ^
```

<!-- test: error.match-missing-block-id -->
```maxon
function main() returns int
    var x = 1
    match x
        1 then return 10
        default then return 0
    end
end 'main'
```
```maxoncstderr
In file 'temp/temp_fragment.maxon':
Expected block identifier after match expression
  Expected: block identifier
  Found: '1'
  Location: line 5, column 9
```

<!-- test: error.match-mismatched-block-id -->
```maxon
function main() returns int
    var x = 1
    match x 'check'
        1 then return 10
        default then return 0
    end 'wrong'
end 'main'
```
```maxoncstderr
In file 'temp/temp_fragment.maxon':
Block identifier mismatch in match statement
  Expected: 'check'
  Found: 'wrong'
  Location: line 7, column 9
```

<!-- test: error.match-default-not-last -->
```maxon
function main() returns int
    var x = 1
    match x 'check'
        default then return 0
        1 then return 10
        2 then return 20
    end 'check'
end 'main'
```
```maxoncstderr
Semantic Error: line 4, column 5
'default' case must be the last case in match

  4 |     match x 'check'
    |     ^
```
