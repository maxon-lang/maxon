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

You can match on string values using string literals as patterns:

```maxon
function main() returns int
    var name = "alice"
    match name 'greet'
        "alice" then return 1
        "bob" then return 2
        default then return 0
    end 'greet'
end 'main'
```
```exitcode
1
```

String matching uses the `equals` method from the `Equatable` interface, so any type that implements `Equatable` can be used as a match scrutinee.

## Range Patterns

Range patterns allow matching values within an interval. This is useful for numeric ranges, character classification, and grading systems.

**Syntax:**
- `1..=5` - inclusive range (matches 1, 2, 3, 4, 5)
- `1..<5` - exclusive upper bound (matches 1, 2, 3, 4)
- `1..` - from 1 to infinity (open-ended upper)
- `..=5` - from negative infinity to 5 inclusive (open-ended lower)
- `..<5` - from negative infinity to 5 exclusive (open-ended lower)
- `..` - matches everything (wildcard)

**Integer ranges:**

```maxon
function grade(score int) returns int
    match score 'grade'
        90..=100 then return 65  // 'A'
        80..<90 then return 66   // 'B'
        70..<80 then return 67   // 'C'
        60..<70 then return 68   // 'D'
        0..<60 then return 70    // 'F'
        default then return 63   // '?'
    end 'grade'
end 'grade'

function main() returns int
    return grade(85)
end 'main'
```
```exitcode
66
```

**Character ranges:**

Characters implement the `Comparable` interface, so they can be used in range patterns.
The comparison is lexicographic (byte-by-byte).

```maxon
function charType(c Character) returns int
    match c 'classify'
        'a'..='z' then return 1  // lowercase
        'A'..='Z' then return 2  // uppercase
        '0'..='9' then return 3  // digit
        default then return 0    // other
    end 'classify'
end 'charType'

function main() returns int
    return charType('G')
end 'main'
```
```exitcode
2
```

**Open-ended ranges:**

```maxon
function classify(age int) returns int
    match age 'category'
        ..<0 then return 0       // invalid (negative)
        0..<18 then return 1     // minor
        18.. then return 2       // adult
    end 'category'
end 'classify'

function main() returns int
    return classify(25)
end 'main'
```
```exitcode
2
```

**Range patterns in match expressions:**

```maxon
function main() returns int
    var temp = 25
    let category = match temp 'weather'
        ..<0 gives 1      // freezing
        0..<15 gives 2    // cold  
        15..<25 gives 3   // mild
        25.. gives 4      // warm
    end 'weather'
    return category
end 'main'
```
```exitcode
4
```

**Combining ranges with `or`:**

```maxon
function main() returns int
    var x = 50
    match x 'check'
        1..=10 or 90..=100 then return 1   // extreme values
        default then return 0
    end 'check'
end 'main'
```
```exitcode
0
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
error E002: specs/fragments/match-statements.error.match-expression-fallthrough.1.test:5:20: unexpected token: 'and'
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
error E025: specs/fragments/match-statements.error.match-fallthrough-with-return.1.test:5:16: match fallthrough with return: 'cannot combine 'fallthrough' with 'return''
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
error E026: specs/fragments/match-statements.error.match-enum-not-exhaustive.1.test:10:5: match on enum 'Color' is not exhaustive, missing: blue
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
error E027: specs/fragments/match-statements.error.match-duplicate-pattern.1.test:6:9: duplicate pattern in match: '1'
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
error E028: specs/fragments/match-statements.error.match-type-mismatch.1.test:5:20: pattern type 'String' does not match scrutinee type 'int'
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
error E042: specs/fragments/match-statements.error.match-missing-block-id.1.test:4:12: missing block identifier
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
error E043: specs/fragments/match-statements.error.match-mismatched-block-id.1.test:7:16: block identifier mismatch: expected 'check', got 'wrong'
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
error E029: specs/fragments/match-statements.error.match-default-not-last.1.test:6:9: 'default' case must be the last case in match
```

<!-- test: match-string.simple -->
```maxon
function main() returns int
    var name = "alice"
    match name 'greet'
        "alice" then return 1
        "bob" then return 2
        default then return 0
    end 'greet'
end 'main'
```
```exitcode
1
```

<!-- test: match-string.second-case -->
```maxon
function main() returns int
    var name = "bob"
    match name 'greet'
        "alice" then return 1
        "bob" then return 2
        default then return 0
    end 'greet'
end 'main'
```
```exitcode
2
```

<!-- test: match-string.default -->
```maxon
function main() returns int
    var name = "charlie"
    match name 'greet'
        "alice" then return 1
        "bob" then return 2
        default then return 0
    end 'greet'
end 'main'
```
```exitcode
0
```

<!-- test: match-string.or-patterns -->
```maxon
function main() returns int
    var name = "carol"
    match name 'greet'
        "alice" or "bob" then return 1
        "carol" or "dave" then return 2
        default then return 0
    end 'greet'
end 'main'
```
```exitcode
2
```

<!-- test: match-string.expression -->
```maxon
function main() returns int
    var name = "bob"
    let code = match name 'lookup'
        "alice" gives 100
        "bob" gives 200
        default gives 0
    end 'lookup'
    return code
end 'main'
```
```exitcode
200
```

<!-- test: match-range.inclusive -->
```maxon
function main() returns int
    var x = 5
    match x 'check'
        1..=5 then return 1
        default then return 0
    end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.inclusive-boundary -->
```maxon
function main() returns int
    var x = 1
    match x 'check'
        1..=5 then return 1
        default then return 0
    end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.exclusive -->
```maxon
function main() returns int
    var x = 4
    match x 'check'
        1..<5 then return 1
        default then return 0
    end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.exclusive-boundary -->
```maxon
function main() returns int
    var x = 5
    match x 'check'
        1..<5 then return 1
        default then return 0
    end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: match-range.open-upper -->
```maxon
function main() returns int
    var x = 100
    match x 'check'
        10.. then return 1
        default then return 0
    end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.open-lower-inclusive -->
```maxon
function main() returns int
    var x = 5
    match x 'check'
        ..=5 then return 1
        default then return 0
    end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.open-lower-exclusive -->
```maxon
function main() returns int
    var x = 5
    match x 'check'
        ..<5 then return 1
        default then return 0
    end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: match-range.wildcard -->
```maxon
function main() returns int
    var x = 42
    match x 'check'
        .. then return 1
    end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.multiple-ranges -->
```maxon
function main() returns int
    var score = 85
    match score 'grade'
        90..=100 then return 65
        80..<90 then return 66
        70..<80 then return 67
        default then return 70
    end 'grade'
end 'main'
```
```exitcode
66
```

<!-- test: match-range.negative -->
```maxon
function main() returns int
    var x = -5
    match x 'check'
        -10..=-1 then return 1
        default then return 0
    end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.expression -->
```maxon
function main() returns int
    var temp = 22
    let category = match temp 'weather'
        ..<0 gives 1
        0..<15 gives 2
        15..<25 gives 3
        25.. gives 4
    end 'weather'
    return category
end 'main'
```
```exitcode
3
```

<!-- test: match-range.character -->
```maxon
function main() returns int
    var c = 'G'
    match c 'classify'
        'a'..='z' then return 1
        'A'..='Z' then return 2
        '0'..='9' then return 3
        default then return 0
    end 'classify'
end 'main'
```
```exitcode
2
```

<!-- test: match-range.character-lowercase -->
```maxon
function main() returns int
    var c = 'm'
    match c 'classify'
        'a'..='z' then return 1
        'A'..='Z' then return 2
        default then return 0
    end 'classify'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.character-digit -->
```maxon
function main() returns int
    var c = '7'
    match c 'classify'
        'a'..='z' then return 1
        'A'..='Z' then return 2
        '0'..='9' then return 3
        default then return 0
    end 'classify'
end 'main'
```
```exitcode
3
```

<!-- test: match-range.with-or -->
```maxon
function main() returns int
    var x = 95
    match x 'check'
        1..=10 or 90..=100 then return 1
        default then return 0
    end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.with-or-second -->
```maxon
function main() returns int
    var x = 5
    match x 'check'
        1..=10 or 90..=100 then return 1
        default then return 0
    end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.float -->
```maxon
function main() returns int
    var x = 2.5
    match x 'check'
        0.0..=5.0 then return 1
        default then return 0
    end 'check'
end 'main'
```
```exitcode
1
```
