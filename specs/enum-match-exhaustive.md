---
feature: enum-match-exhaustive
status: experimental
keywords: [enum, match, exhaustive, range, to, upto]
category: control-flow
---

# Enum Match Exhaustiveness

## Documentation

Enum match expressions require exhaustive case coverage, just like union matches. Every enum case must be matched by either an explicit case pattern or a range pattern. Plain `default` is not allowed — use `default then throws` if you want a catch-all that throws an error.

Range patterns use enum case references as bounds:

```text
match priority 'check'
    Priority.low to Priority.medium then print("not urgent")
    Priority.high to Priority.critical then print("urgent")
end 'check'
```

Ranges use the enum's ordinal values. `to` is inclusive on both ends, `upto` excludes the upper bound.

Overlapping patterns are not allowed — each enum case must be covered by exactly one arm.

## Tests

<!-- test: enum-exhaustive.all-explicit -->
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

<!-- test: enum-exhaustive.single-range -->
```maxon
enum Color
    red
    green
    blue
end 'Color'

function main() returns ExitCode
  var c = Color.green
  match c 'check'
    Color.red to Color.blue then return 1
  end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: enum-exhaustive.multiple-ranges -->
```maxon
enum Priority
    low
    medium
    high
    critical
end 'Priority'

function main() returns ExitCode
  var p = Priority.high
  match p 'check'
    Priority.low to Priority.medium then return 1
    Priority.high to Priority.critical then return 2
  end 'check'
end 'main'
```
```exitcode
2
```

<!-- test: enum-exhaustive.mix-explicit-and-range -->
```maxon
enum Priority
    low
    medium
    high
    critical
end 'Priority'

function main() returns ExitCode
  var p = Priority.low
  match p 'check'
    Priority.low then return 1
    Priority.medium to Priority.critical then return 2
  end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: enum-exhaustive.expression -->
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
    Status.approved to Status.rejected gives 1
  end 'eval'
  return code
end 'main'
```
```exitcode
1
```

<!-- test: enum-exhaustive.upto-range -->
```maxon
enum Color
    red
    green
    blue
end 'Color'

function main() returns ExitCode
  var c = Color.red
  match c 'check'
    Color.red upto Color.blue then return 1
    Color.blue then return 2
  end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: enum-exhaustive.default-throws -->
```maxon
enum Color
    red
    green
    blue
end 'Color'

enum AppError
    unmatched
end 'AppError'

typealias ColorCode = int(0 to 10)

function checkColor(c Color) returns ColorCode throws AppError
  match c 'check'
    Color.red then return ColorCode{1}
    default then throws AppError.unmatched
  end 'check'
  return ColorCode{0}
end 'checkColor'

function main() returns ExitCode
  let result = try checkColor(Color.red) otherwise 99
  return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-exhaustive.float-backed-range -->
```maxon
enum Threshold
    low = 0.1
    medium = 0.5
    high = 0.9
end 'Threshold'

function main() returns ExitCode
  var t = Threshold.medium
  match t 'check'
    Threshold.low to Threshold.high then return 1
  end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: error.enum-not-exhaustive -->
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
error E2026: specs/fragments/enum-match-exhaustive/error.enum-not-exhaustive.test:13:3: match on enum 'Color' is not exhaustive, missing: blue
```

<!-- test: error.enum-default-without-throws -->
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
error E2046: specs/fragments/enum-match-exhaustive/error.enum-default-without-throws.test:12:5: 'default' in a match on enum 'Color' must be followed by 'throws <error>' or 'panic("message")'
```

<!-- test: error.enum-gap-in-ranges -->
```maxon
enum Priority
    low
    medium
    high
    critical
end 'Priority'

function main() returns ExitCode
  var p = Priority.high
  match p 'check'
    Priority.low to Priority.medium then return 1
    Priority.critical then return 2
  end 'check'
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/enum-match-exhaustive/error.enum-gap-in-ranges.test:14:3: match on enum 'Priority' is not exhaustive, missing: high
```

<!-- test: error.enum-overlapping-ranges -->
```maxon
enum Priority
    low
    medium
    high
    critical
end 'Priority'

function main() returns ExitCode
  var p = Priority.high
  match p 'check'
    Priority.low to Priority.high then return 1
    Priority.medium to Priority.critical then return 2
  end 'check'
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/enum-match-exhaustive/error.enum-overlapping-ranges.test:13:5: overlapping pattern in match: 'Priority.medium' is already covered
```

<!-- test: error.enum-explicit-overlaps-range -->
```maxon
enum Color
    red
    green
    blue
end 'Color'

function main() returns ExitCode
  var c = Color.green
  match c 'check'
    Color.red to Color.blue then return 1
    Color.green then return 2
  end 'check'
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/enum-match-exhaustive/error.enum-explicit-overlaps-range.test:12:5: overlapping pattern in match: 'Color.green' is already covered
```
