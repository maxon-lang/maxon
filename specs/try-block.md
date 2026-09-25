---
feature: try-block
status: experimental
keywords: [try, otherwise, block, error, match, union]
category: error-handling
---

# Try Block (Multi-Call Error Handling)

## Documentation

The `try { } otherwise (e) { match e { } }` block construct lets you wrap a sequence of statements containing several throwing operations under a single error handler. Within the `try` block, a call to a throwing function, a call to a throwing interface method and an `await` of a throwing promise do **not** require the `try` keyword; the parser implicitly routes their errors to the shared `otherwise` clause.

The `otherwise (e)` clause receives the synthesized error union of every distinct error type thrown within the block. It must contain a `match` on `e` somewhere in its body; the match arms must exhaustively cover every `(EnumName, case)` pair across the union members.

```maxon
try 'reading'
    let raw = readFile("config.json")
    let parsed = parseJson(raw)
    let value = parsed.get("port")
    print(value)
end 'reading'
otherwise (e) 'handler'
    match e 'kind'
        FileError.notFound        then print("missing")
        FileError.permissionDenied then print("perm")
        ParseError.unexpectedToken then print("bad json")
        MapError.missingKey       then print("no port")
    end 'kind'
end 'handler'
```

If the block contains throwing calls of only one error type, the binding `e` is just that enum type and patterns match it directly (no qualification needed).

Inside the block, an explicit `try expr otherwise ...` form still works for any single call — its error is consumed by its own `otherwise` and does not contribute to the synthesized union. An explicit `try` covers only the operation that produces its value: a throwing argument, operand, chain receiver or range bound inside it is routed to the block's handler like any other bare operation.

Blocks nest, and a throwing operation routes to the innermost block around it. Inside a `test` body, the test's implied handler (`specs/test-uncaught-throw.md`) takes only what no enclosing block catches.

## Tests

<!-- test: try-block.single-enum-success-path -->
Single error type, all calls succeed; otherwise body is not entered.
```maxon
typealias Score = int(0 to 100)

enum MyError implements Error
    failed
end 'MyError'

function maybeFail(x bool) returns Score throws MyError
    if x 'check'
        throw MyError.failed
    end 'check'
    return 7
end 'maybeFail'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = maybeFail(false)
        let b = maybeFail(false)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            failed then sum = 99
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
14
```

<!-- test: try-block.single-enum-error-path -->
Single error type, second call throws; otherwise body fires.
```maxon
typealias Score = int(0 to 100)

enum MyError implements Error
    failed
end 'MyError'

function maybeFail(x bool) returns Score throws MyError
    if x 'check'
        throw MyError.failed
    end 'check'
    return 7
end 'maybeFail'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = maybeFail(false)
        let b = maybeFail(true)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            failed then sum = 42
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
42
```

<!-- test: try-block.multi-enum-first-error -->
Two distinct error types; the first call throws, the handler matches.
```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    kaboom
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.kaboom
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = callA(true)
        let b = callB(false)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            ErrA.kaboom then sum = 11
            ErrB.splat  then sum = 22
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
11
```

<!-- test: try-block.multi-enum-second-error -->
Two distinct error types; the second call throws, the handler matches.
```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    kaboom
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.kaboom
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = callA(false)
        let b = callB(true)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            ErrA.kaboom then sum = 11
            ErrB.splat  then sum = 22
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
22
```

<!-- test: try-block.multi-enum-success-path -->
Two distinct error types; no calls throw — otherwise body is skipped.
```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    kaboom
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.kaboom
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = callA(false)
        let b = callB(false)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            ErrA.kaboom then sum = 99
            ErrB.splat  then sum = 99
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
11
```

<!-- test: error.try-block-no-throws -->
A try block with no throwing calls is a compile error.
```maxon
function main() returns ExitCode
    try 'work'
        print("hi")
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            default panic("unreachable")
        end 'k'
    end 'h'
    return 0
end 'main'
```
```maxoncstderr
error E3083: specs/fragments/try-block/error.try-block-no-throws.test:3:5: try block contains no throwing calls: 'work'
```

<!-- test: error.try-block-no-match -->
The otherwise body must match on the binding.
```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    kaboom
end 'ErrA'

function callA() returns Score throws ErrA
    throw ErrA.kaboom
end 'callA'

function main() returns ExitCode
    var x = 0
    try 'work'
        let a = callA()
        x = a
    end 'work'
    otherwise (e) 'h'
        print("oops")
    end 'h'
    return x
end 'main'
```
```maxoncstderr
error E3084: specs/fragments/try-block/error.try-block-no-match.test:18:19: otherwise block must contain a match on the error binding 'e'
```

<!-- test: error.try-block-non-exhaustive-union -->
Non-exhaustive match on an error union must fail with a specific missing-cases list.
```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    kaboom
    bang
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.kaboom
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var x = 0
    try 'work'
        let a = callA(false)
        let b = callB(false)
        x = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            ErrA.kaboom then x = 1
        end 'k'
    end 'h'
    return x
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/try-block/error.try-block-non-exhaustive-union.test:37:9: match on error union is not exhaustive, missing: ErrA.bang, ErrB.splat
```

<!-- test: error.try-block-default-plain-union -->
A plain `default then ...` arm in an error-union match must be rejected. Adding a new error variant must surface as a compile error so handlers stay honest; only `default throws` / `default panic` opt out.
```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    kaboom
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.kaboom
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var x = 0
    try 'work'
        let a = callA(false)
        let b = callB(false)
        x = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            default then x = 1
        end 'k'
    end 'h'
    return x
end 'main'
```
```maxoncstderr
error E2046: specs/fragments/try-block/error.try-block-default-plain-union.test:35:13: 'default' in a match on an error union must be followed by 'throws <error>' or 'panic("message")'
```

<!-- test: try-block.bare-unambiguous-patterns -->
Bare case names work when unambiguous across union members.
```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    kaboom
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.kaboom
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = callA(false)
        let b = callB(true)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            kaboom then sum = 11
            splat  then sum = 22
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
22
```

<!-- test: error.try-block-ambiguous-bare -->
Bare case names fail when shared between union members.
```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    notFound
end 'ErrA'

enum ErrB implements Error
    notFound
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.notFound
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.notFound
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var x = 0
    try 'work'
        let a = callA(false)
        let b = callB(false)
        x = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            notFound then x = 1
        end 'k'
    end 'h'
    return x
end 'main'
```
```maxoncstderr
error E3085: specs/fragments/try-block/error.try-block-ambiguous-bare.test:35:13: case 'notFound' is shared by multiple union members; qualify with 'EnumName.notFound'
```

<!-- test: try-block.array-get-success -->
Bare `Array.get` calls inside a try block route to the shared handler — happy path.
```maxon
typealias Score = int(0 to 100)
typealias ScoreArray = Array with Score

function main() returns ExitCode
    var arr = ScoreArray.create()
    arr.push(7)
    arr.push(11)

    var sum = 0
    try 'work'
        let a = arr.get(0)
        let b = arr.get(1)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            indexOutOfBounds then sum = 99
            emptySlot then sum = 99
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
18
```

<!-- test: try-block.single-enum-assoc-value -->
A try block whose only throwing-error enum has associated values: the binding `e` is
the typed enum, and pattern bindings extract the payload via the legacy single-enum
match path.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
    bad(code Score)
end 'ErrA'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.bad(50)
    end 'c'
    return 5
end 'callA'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = callA(true)
        sum = a
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            bad(code) then sum = code
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
50
```

<!-- test: try-block.multi-union-assoc-success -->
Multi-member error union with one assoc-value member: the assoc member throws,
the case-binding extracts the payload, and the heap object is freed via the
per-arm typed-enum binding's scope-end decref. No leak.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
    bad(code Score)
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.bad(50)
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = callA(true)
        let b = callB(false)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            ErrA.bad(code) then sum = code
            ErrB.splat then sum = 99
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
50
```

<!-- test: try-block.multi-union-assoc-second-error -->
Multi-member error union: the simple-enum sibling throws (not the assoc-value
member). The simple match arm fires, no heap object exists to leak.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
    bad(code Score)
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.bad(50)
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = callA(false)
        let b = callB(true)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            ErrA.bad(code) then sum = code
            ErrB.splat then sum = 99
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
99
```

<!-- test: try-block.multi-union-assoc-success-path -->
Multi-member error union: no calls throw, otherwise body is skipped. Sanity
check that the try-block construct doesn't leak when nothing happens.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
    bad(code Score)
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.bad(50)
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = callA(false)
        let b = callB(false)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            ErrA.bad(code) then sum = code
            ErrB.splat then sum = 99
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
11
```

<!-- test: try-block.multi-union-assoc-default -->
Multi-member error union with a `default panic` arm: the assoc-value member
throws but the user only covers the simple-enum case. Default fires; pre-default
cleanup block decrefs the heap pointer (incref-then-decref pair to balance the
rc=0 at delivery → free). No leak, no underflow before the panic.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
    bad(code Score)
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.bad(50)
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = callA(true)
        let b = callB(false)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            ErrB.splat then sum = 99
            default panic("unhandled error variant")
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
1
```
```stderr
panic at try-block.multi-union-assoc-default.test:36: unhandled error variant
Stack trace:
  in main
  in mrt_start
```

<!-- test: try-block.multi-union-assoc-discard-bindings -->
Multi-member error union: assoc-value case matched without payload bindings
(`EnumName.case` form, no parens). Heap object is still freed via the per-arm
typed-enum binding's scope-end decref.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
    bad(code Score)
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.bad(50)
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = callA(true)
        let b = callB(false)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            ErrA.bad then sum = 77
            ErrB.splat then sum = 99
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
77
```

<!-- test: try-block.multi-union-assoc-multiple-payloads -->
Multi-member error union with an assoc-value member carrying TWO payloads. Both
bindings extract correctly.
```maxon
typealias Score = int(0 to 1000)
typealias Msg = int(0 to 100)

union ErrA implements Error
    bad(code Score, msg Msg)
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.bad(50, msg: 7)
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = callA(true)
        let b = callB(false)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            ErrA.bad(code, msg) then sum = code + (msg as Score)
            ErrB.splat then sum = 99
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
57
```

<!-- test: try-block.multi-union-assoc-mixed-bindings -->
Mix assoc-value and bare patterns of the same enum (different cases) in one
union match. The discard-binding case still gets its heap object freed.
```maxon
typealias Score = int(0 to 1000)
typealias Sel = int(0 to 2)

union ErrA implements Error
    bad(code Score)
    worse(level Score)
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x Sel) returns Score throws ErrA
    if x == 1 'c1'
        throw ErrA.bad(50)
    end 'c1'
    if x == 2 'c2'
        throw ErrA.worse(200)
    end 'c2'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = callA(2)
        let b = callB(false)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            ErrA.bad(code) then sum = code
            ErrA.worse then sum = 120
            ErrB.splat then sum = 99
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
120
```

<!-- test: error.try-block-multi-union-assoc-wrong-binding-count -->
Wrong binding count on an error-union assoc-value pattern: error E3035 fires.
```maxon
typealias Score = int(0 to 1000)
typealias Msg = int(0 to 100)

union ErrA implements Error
    bad(code Score, msg Msg)
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.bad(50, msg: 1)
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = callA(false)
        let b = callB(false)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            ErrA.bad(code) then sum = code
            ErrB.splat then sum = 99
        end 'k'
    end 'h'
    return sum
end 'main'
```
```maxoncstderr
error E3035: specs/fragments/try-block/error.try-block-multi-union-assoc-wrong-binding-count.test:36:18: wrong binding count: 'ErrA.bad' expects 2 associated value(s), got 1
```

<!-- test: try-block.array-many-ops -->
A try block wrapping several Array operations — the typical "I know none of these
can fail" use case the construct was designed for. Uses a narrow ranged element
type (int(0..100), one byte per element) to also exercise width-correct
load/store.
```maxon
typealias Score = int(0 to 100)
typealias ScoreArray = Array with Score

function main() returns ExitCode
    var arr = ScoreArray.create()
    arr.push(0)
    arr.push(0)
    arr.push(0)

    var sum = 0
    try 'work'
        arr.set(0, value: 10)
        arr.set(1, value: 20)
        arr.set(2, value: 30)
        let a = arr.get(0)
        let b = arr.get(1)
        let c = arr.get(2)
        sum = a + b + c
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            indexOutOfBounds then sum = 99
            emptySlot then sum = 99
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
60
```

<!-- test: try-block.array-narrow-signed -->
Signed narrow element types (int(-50..50), stored as i8) round-trip negative
values correctly through Array.get — the load must sign-extend, not zero-extend.
```maxon
typealias Signed = int(-50 to 50)
typealias SignedArray = Array with Signed

function main() returns ExitCode
    var arr = SignedArray.create()
    arr.push(-7)
    arr.push(11)
    arr.push(-25)
    var sum = 100
    try 'work'
        let a = arr.get(0)
        let b = arr.get(1)
        let c = arr.get(2)
        sum = sum + a + b + c
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            indexOutOfBounds then sum = 999
            emptySlot then sum = 999
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
79
```

<!-- test: try-block.array-get-out-of-bounds -->
Bare `Array.get` triggers the shared otherwise handler on out-of-bounds.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
    var arr = IntArray.create()
    arr.push(7)
    arr.push(11)

    var sum = 0
    try 'work'
        let a = arr.get(0)
        let b = arr.get(99)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            indexOutOfBounds then sum = 42
            emptySlot then sum = 99
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
42
```

<!-- test: try-block.nested -->
A try block nested inside another try block: the inner block absorbs its own throws,
the outer block sees only what its own bare calls throw.
```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    kaboom
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.kaboom
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var sum = 0
    try 'outer'
        try 'inner'
            let a = callA(true)
            sum = sum + a
        end 'inner'
        otherwise (ie) 'ih'
            match ie 'ik'
                kaboom then sum = sum + 1
            end 'ik'
        end 'ih'
        let b = callB(true)
        sum = sum + b
    end 'outer'
    otherwise (e) 'h'
        match e 'k'
            splat then sum = sum + 40
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
41
```

<!-- test: try-block.explicit-try-otherwise-shadows -->
Inside a try block, an explicit `try expr otherwise ...` consumes its own call's error.
```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    kaboom
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.kaboom
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.splat
    end 'c'
    return 6
end 'callB'

function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = try callA(true) otherwise 0
        let b = callB(false)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        match e 'k'
            splat then sum = 99
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
6
```

<!-- test: try-block.bare-throw-routed -->
A bare `throw Enum.case` statement inside the try body must route to the enclosing
`otherwise` handler — not escape to the function exit. Covers both paths: a direct
`throw` statement, and a `default throws` arm in a `match` inside the body.
```maxon
typealias Count = int(0 to 1000)

enum Kind
    alpha
    beta
    gamma
end 'Kind'

enum MyError implements Error
    fooErr
    barErr
end 'MyError'

function dispatch(k Kind) returns Count throws MyError
    var result = 0
    try 'd'
        match k 'm'
            alpha then result = 1
            beta then throw MyError.barErr
            default throws MyError.fooErr
        end 'm'
    end 'd' otherwise (e) 'h'
        match e 'he'
            fooErr then result = 40
            barErr then result = 80
        end 'he'
    end 'h'

    if result == 0 'unset'
        throw MyError.fooErr
    end 'unset'

    return result
end 'dispatch'

function main() returns ExitCode
    let a = try dispatch(Kind.alpha) otherwise panic("alpha escaped")
    let b = try dispatch(Kind.beta) otherwise panic("beta escaped")
    let c = try dispatch(Kind.gamma) otherwise panic("gamma escaped")
    return a + b + c
end 'main'
```
```exitcode
121
```

<!-- test: try-block.managed-var-success-path -->
Regression: a `var` of a managed (heap-allocated) type declared inside a try block
body must drop its allocation at the body's success-path live tail. Without the fix
in `ParseTryBlock`, the var leaked. `VarRegistry.KeysSince` now excludes routed
`__try_block_result_*` temps (created by `RouteEmittedTryCallToTryBlock`) so the
body's `MaxonScopeEndOp` only decref's user vars — the user var is assigned the
same call-return value as the temp without an extra incref, so a single decref via
the user var is enough to balance the original allocation.
```maxon
typealias Score = int(-1000 to 1000)
typealias Inner = Array with Score
typealias Outer = Array with Inner

function main() returns ExitCode
    var outer = Outer.create()
    outer.push(Inner.create())
    outer.push(Inner.create())

    try 'work'
        var inner = outer.get(0)
        inner.push(7)
    end 'work' otherwise (e) 'h'
        match e 'k'
            indexOutOfBounds then panic("oob")
            emptySlot then panic("empty")
        end 'k'
    end 'h'

    let first = try outer.get(0) otherwise Inner.create()
    let v = try first.get(0) otherwise -1
    return v as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: try-block.managed-var-in-nested-if -->
Regression: when an inner construct (here, an `if` block) sits inside a try block and
declares a `var` of a managed type via a bare throwing call, the inner construct's
scope-end must not double-decref the routed `__try_block_result_N` temp.
`RouteEmittedTryCallToTryBlock` injects the temp into the active parser scope (which
is the innermost construct, not the try-block itself). Centralising the filter in
`VarRegistry.KeysSince` covers every callsite (try-block body, if/else, while/for,
match arms) without per-construct fixes.
```maxon
typealias Score = int(-1000 to 1000)
typealias Inner = Array with Score
typealias Outer = Array with Inner

function main() returns ExitCode
    var outer = Outer.create()
    outer.push(Inner.create())
    outer.push(Inner.create())

    try 'wrap'
        if true 'gate'
            var inner = outer.get(0)
            inner.push(7)
        end 'gate'
    end 'wrap' otherwise (e) 'h'
        match e 'k'
            indexOutOfBounds then panic("oob")
            emptySlot then panic("empty")
        end 'k'
    end 'h'

    let first = try outer.get(0) otherwise Inner.create()
    let v = try first.get(0) otherwise -1
    return v as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: try-block.inline-managed-arg-throws -->
Regression: a bare throwing call inside a try-block whose argument is an
inline allocation (`workFunc(IntArray.create())`) must release the
argument's allocation when the call throws. Previously the argument was
incref'd into the call but the routed-error path branched to the shared
error block without including the call's `__call_tmp_*` temp in the
scope-end set, leaking the IntArray + its backing __ManagedMemory on
every error-path throw.
```maxon
typealias Idx = int(0 to u64.max)
typealias IntArray = Array with Idx

enum MyError implements Error
    failed
end 'MyError'

function workFunc(arr IntArray) throws MyError
    if arr.count() == 0 'empty'
        throw MyError.failed
    end 'empty'
    arr.push(1)
end 'workFunc'

function main() returns ExitCode
    var sum = 0
    try 'work'
        workFunc(IntArray.create())
    end 'work' otherwise (e) 'h'
        match e 'k'
            failed then sum = 42
        end 'k'
    end 'h'
    return sum as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: try-block.bare-throw-in-nested-if -->
Regression guard: a bare `throw` routed to the block handler from inside a
NESTED construct (`if`/`while`/`match`) within the try body. The throw routes
to the shared error block from the nested block, not the body's entry block.
This shape used to crash the self-hosted compiler with an entry-block
use-after-free: when the try body held a nested control-flow construct, the
body's entry block was over-released by one (parseTryBlock threaded its
borrowed `block` param into the inner `parseStatements`, whose first
reassignment decref'd the borrow without a balancing incref) and freed while
`module.blocks` still referenced it — a later parser pass then walked the
dangling block. Fixed by re-fetching the body block as an owned value before
the inner `parseStatements` (see parseTryBlock's `bodyBlock`). Flat bodies
(no nested construct) never triggered it.
```maxon
enum MyError implements Error
    failed
end 'MyError'

function main() returns ExitCode
    var sum = 0
    try 'work'
        if true 'gate'
            throw MyError.failed
        end 'gate'
    end 'work' otherwise (e) 'h'
        match e 'k'
            failed then sum = 5
        end 'k'
    end 'h'
    return sum as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: try-block.routed-union-result-is-released -->
### A routed call returning an associated-value union releases its result

A bare throwing call inside a `try` block has its success value hoisted into a routed
`__try_block_result_N` temp, which receives the callee's *transferred* reference. That temp
therefore owns a reference and must release it at scope end, exactly like the temp the
single-statement `try` forms use.

It did not. `VarRegistry.KeysSince` excluded `__try_block_result_*` from scope-end cleanup on
the theory that a downstream `let x = <call>` aliased the same slot without increfing — true
for a struct return, which was separately handed a `CallReturn` `__call_tmp_` that turned the
alias into a *move*, and false for an associated-value union, which was handed nothing and so
increfed like any other alias. The result: one reference per routed union-returning call was
owned by a temp nobody ever decrefed, and the union leaked.

Nothing in the suite returned a union from a bare call inside a `try` block, so the leak went
unseen. This test is that shape and nothing more: the success path leaks a `Shape` if the
routed temp's reference is dropped, and the leak checker fails the run with exit 101. Both
paths are walked because the error path stores null into the same slot and must not
double-release it.

```maxon
typealias Num = int(0 to 1000)

union Shape
    circle(r Num)
    square(s Num)
end 'Shape'

union ShapeError
    tooBig(limit Num)
end 'ShapeError'

function makeShape(r Num) returns Shape throws ShapeError
    if r > 500 'tooBig'
        throw ShapeError.tooBig(500)
    end 'tooBig'

    return Shape.circle(r)
end 'makeShape'

function classify(r Num) returns Num
    var out = 0

    // The success path must FALL OUT of the try block rather than return from inside it.
    // An early return runs the function-exit cleanup, which released the routed temp anyway;
    // only falling through reaches the try block's own inner scope-end, which is the one that
    // dropped it.
    try 'blk'
        let s = makeShape(r)
        match s 'm'
            circle(v) then out = v
            square(v) then out = 0
        end 'm'
    end 'blk' otherwise (e) 'h'
        match e 'k'
            tooBig(limit) then out = limit - 499
        end 'k'
    end 'h'

    return out
end 'classify'

function main() returns ExitCode
    var total = 0
    total = total + classify(3)
    total = total + classify(4)
    total = total + classify(900)
    return total
end 'main'
```
```exitcode
8
```


<!-- test: try-block.union-member-names-join-injectively -->
A synthesized error union's NAME is a table KEY, not a label: `ParseTryBlock` writes the union into
the type registry under it and the handler's `match` reads the union back out by it. So the join over
the member enum names has to be INJECTIVE, or a second union silently REPLACES the first and a
handler resolves its patterns against the wrong member list. `_` is inside the identifier alphabet,
so joining with it spells `{A_B, C}` and `{A, B_C}` identically. The nested `try` is what makes it
observable: it is parsed inside the OUTER handler and BEFORE the outer `match`, so its own union is
the one sitting under the shared name at the moment `match e` resolves. Measured on the `_` join, the
outer handler was rejected with `'A_B' is not a member of the error union` for a member it plainly
has; renaming the four enums so no name holds an `_` compiled and returned 11, which is the control
saying the underscore is the entire difference. The outer block throws `A_B.kaboom`, so the arm that
must win sets 11. WRITTEN AS TWO FILES ON PURPOSE, and it is not decoration: the runner's batch
rewriter gives every top-level declaration in a batched test a per-test prefix, which pulls the two
member names apart and dissolves the very collision this case is about — a multi-file test is never
batched (`FragmentGenerator.IsBatchable`), so this is the shape in which the case can still fail.
```maxon
// --- file: errors.maxon
export typealias Score = int(0 to 100)

export enum A_B implements Error
    kaboom
end 'A_B'

export enum C implements Error
    splat
end 'C'

export enum A implements Error
    zonk
end 'A'

export enum B_C implements Error
    whap
end 'B_C'

export function callAB(x bool) returns Score throws A_B
    if x 'c'
        throw A_B.kaboom
    end 'c'
    return 5
end 'callAB'

export function callC(x bool) returns Score throws C
    if x 'c'
        throw C.splat
    end 'c'
    return 6
end 'callC'

export function callA(x bool) returns Score throws A
    if x 'c'
        throw A.zonk
    end 'c'
    return 7
end 'callA'

export function callBC(x bool) returns Score throws B_C
    if x 'c'
        throw B_C.whap
    end 'c'
    return 8
end 'callBC'

// --- file: main.maxon
function main() returns ExitCode
    var sum = 0
    try 'work'
        let a = callAB(true)
        let b = callC(false)
        sum = a + b
    end 'work'
    otherwise (e) 'h'
        try 'inner'
            let p = callA(false)
            let q = callBC(false)
            sum = p + q
        end 'inner'
        otherwise (f) 'ih'
            match f 'ik'
                A.zonk then sum = 1
                B_C.whap then sum = 2
            end 'ik'
        end 'ih'
        match e 'k'
            A_B.kaboom then sum = 11
            C.splat then sum = 22
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
11
```

<!-- test: error.mismatched-try-end-label -->
A label written after `end` must repeat the `try` block's opening label.
```maxon
enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns ExitCode throws MyError
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	var result = 0
	try 'work'
		result = mayFail()
	end 'job'
	otherwise (e) 'handler'
		match e 'kind'
			failed then result = 7
		end 'kind'
	end 'handler'
	return result
end 'main'
```
```maxoncstderr
error E2008: <fragment>:14:2: Mismatched end label: expected 'work', got 'job'
```

<!-- test: try-block.or-arm-joins-cases-of-two-error-types -->
An arm of a combined error's `match` joins cases with `or`, across error types as well as within one. `ErrA`
is a union, so its error arrives as a box: the arm naming both types cannot keep it, and releases it on the
path where `ErrA` is the error in flight — a leak exits 101, a double release faults.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(code Score)
	worse
end 'ErrA'

enum ErrB implements Error
	splat
	crash
end 'ErrB'

function callA(fail bool) returns Score throws ErrA
	if fail 'c'
		throw ErrA.bad(50)
	end 'c'

	return 5
end 'callA'

function callB(fail bool) returns Score throws ErrB
	if fail 'c'
		throw ErrB.splat
	end 'c'

	return 6
end 'callB'

function classify(failA bool, failB bool) returns Score
	var sum = 0

	try 'work'
		let a = callA(failA)
		let b = callB(failB)
		sum = a + b
	end 'work'
	otherwise (e) 'h'
		match e 'k'
			ErrA.bad or
				ErrB.splat then sum = 100
			ErrA.worse or
				crash then sum = 200
		end 'k'
	end 'h'

	return sum
end 'classify'

function main() returns ExitCode
	print("{classify(true, failB: false)}\n")
	print("{classify(false, failB: true)}\n")
	print("{classify(false, failB: false)}\n")
	return 0
end 'main'
```
```stdout
100
100
11
```
```exitcode
0
```

<!-- test: try-block.or-arm-within-one-union-member-binds-its-payload -->
An arm whose alternatives all belong to one error type binds that type's payload by the ordinary `or`-arm rule:
`worse` carries no slot 0, so on its path `code` reads the zero every box is filled with.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(code Score)
	worse
end 'ErrA'

enum ErrB implements Error
	splat
end 'ErrB'

function callA(which Score) returns Score throws ErrA
	if which == 1 'bad'
		throw ErrA.bad(50)
	end 'bad'

	if which == 2 'worse'
		throw ErrA.worse
	end 'worse'

	return 5
end 'callA'

function callB(which Score) returns Score throws ErrB
	if which > 100 'big'
		throw ErrB.splat
	end 'big'

	return 6
end 'callB'

function classify(which Score) returns Score
	var sum = 0

	try 'work'
		let a = callA(which)
		let b = callB(which)
		sum = a + b
	end 'work'
	otherwise (e) 'h'
		match e 'k'
			ErrA.bad(code) or
				ErrA.worse then sum = code + 1
			splat then sum = 999
		end 'k'
	end 'h'

	return sum
end 'classify'

function main() returns ExitCode
	print("{classify(1)}\n")
	print("{classify(2)}\n")
	return 0
end 'main'
```
```stdout
51
1
```
```exitcode
0
```

<!-- test: error.try-block-or-arm-across-error-types-binds-a-payload -->
An arm naming cases of two error types cannot bind a payload: `ErrB.splat` is an enum ordinal, not a box with a
slot 0. The refusal names `code`, the binding that reads, rather than the `_` before it.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(note Score, code Score)
end 'ErrA'

enum ErrB implements Error
	splat
end 'ErrB'

function callA() returns Score throws ErrA
	throw ErrA.bad(50, code: 7)
end 'callA'

function callB() returns Score throws ErrB
	return 6
end 'callB'

function main() returns ExitCode
	var sum = 0

	try 'work'
		let a = callA()
		let b = callB()
		sum = a + b
	end 'work'
	otherwise (e) 'h'
		match e 'k'
			ErrA.bad(_, code) or
				ErrB.splat then sum = code
		end 'k'
	end 'h'

	return sum
end 'main'
```
```maxoncstderr
error E3129: <fragment>:30:16: the payload binding 'code' cannot be honoured: this arm names cases of more than one error type, and its bindings read slots of one type's value, which a case of another type does not hold. Give each error type an arm of its own
```

<!-- test: try-block.fallthrough-between-arms-of-a-boxed-error-member -->
`and fallthrough` between arms of a combined error's `match` releases a boxed member's error exactly once on
every path: out of the arm that named it into an arm naming several types or into a `default throws`, and
from an arm of another type into the arm that names it. A leak exits 101; a double release, or a release of
an enum's ordinal as though it were a box, faults.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(code Score)
	worse
end 'ErrA'

enum ErrB implements Error
	splat
	crash
end 'ErrB'

function callA(which Score) returns Score throws ErrA
	if which == 1 'bad'
		throw ErrA.bad(50)
	end 'bad'

	if which == 2 'worse'
		throw ErrA.worse
	end 'worse'

	return 5
end 'callA'

function callB(which Score) returns Score throws ErrB
	if which == 3 'splat'
		throw ErrB.splat
	end 'splat'

	if which == 4 'crash'
		throw ErrB.crash
	end 'crash'

	return 6
end 'callB'

function intoSeveral(which Score) returns Score
	var sum = 0

	try 'work'
		let a = callA(which)
		let b = callB(which)
		sum = a + b
	end 'work'
	otherwise (e) 'h'
		match e 'k'
			ErrA.bad then sum = 1 and fallthrough
			ErrA.worse or
				splat then sum = sum + 10
			crash then sum = 500
		end 'k'
	end 'h'

	return sum
end 'intoSeveral'

function intoBoxedArm(which Score) returns Score
	var sum = 0

	try 'work'
		let a = callA(which)
		let b = callB(which)
		sum = a + b
	end 'work'
	otherwise (e) 'h'
		match e 'k'
			splat then sum = 3 and fallthrough
			ErrA.bad then sum = sum + 30
			ErrA.worse or
				crash then sum = 700
		end 'k'
	end 'h'

	return sum
end 'intoBoxedArm'

enum Gave implements Error
	up
end 'Gave'

function intoDefault(which Score) returns Score throws Gave
	var sum = 0

	try 'work'
		let a = callA(which)
		let b = callB(which)
		sum = a + b
	end 'work'
	otherwise (e) 'h'
		match e 'k'
			ErrA.bad then sum = 4 and fallthrough
			default throws Gave.up
		end 'k'
	end 'h'

	return sum
end 'intoDefault'

function main() returns ExitCode
	print("{intoSeveral(1)}\n")
	print("{intoSeveral(2)}\n")
	print("{intoSeveral(3)}\n")
	print("{intoBoxedArm(3)}\n")
	print("{intoBoxedArm(1)}\n")
	print("{try intoDefault(1) otherwise 99}\n")
	print("{try intoDefault(2) otherwise 98}\n")
	print("{try intoDefault(4) otherwise 97}\n")
	print("{try intoDefault(0) otherwise 96}\n")
	return 0
end 'main'
```
```stdout
11
10
10
33
30
99
98
97
11
```
```exitcode
0
```

<!-- test: error.try-block-combined-error-matched-again-inside-an-arm-naming-several-types -->
A `match e` inside an arm of the `match e` that already ran is a second match on that path: the arm naming
several error types released the box as it was entered, so the inner arm would read a released box.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(msg String)
	worse
end 'ErrA'

enum ErrB implements Error
	splat
end 'ErrB'

function callA(which Score) returns Score throws ErrA
	if which == 1 'bad'
		throw ErrA.bad("bad {which}")
	end 'bad'

	if which == 2 'worse'
		throw ErrA.worse
	end 'worse'

	return 5
end 'callA'

function callB(which Score) returns Score throws ErrB
	if which == 3 'splat'
		throw ErrB.splat
	end 'splat'

	return 6
end 'callB'

function classify(which Score) returns String
	var said = "none"

	try 'work'
		let a = callA(which)
		let b = callB(which)
		said = "sum {a + b}"
	end 'work'
	otherwise (e) 'h'
		match e 'k'
			ErrA.bad or
				ErrB.splat then said = match e 'inner'
					ErrA.bad(msg) gives msg
					ErrA.worse or
						splat gives "other"
				end 'inner'
			ErrA.worse then said = "worse"
		end 'k'
	end 'h'

	return said
end 'classify'

function main() returns ExitCode
	print("{classify(1)}\n")
	print("{classify(2)}\n")
	print("{classify(3)}\n")
	print("{classify(0)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3161: <fragment>:44:28: 'e' may hold a boxed error, which only the arm of the match that runs releases, so every path through its handler must match 'e' exactly once: this path has already matched it. Match 'e' once, and bind in its arms whatever the rest of the handler needs
```

<!-- test: error.try-block-combined-error-matched-again-inside-a-boxed-member-arm -->
A second `match e` inside the arm that keeps one boxed error type's box is refused too, though that arm still
holds the box: the inner match's arms release boxes of their own.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(msg String)
	worse
end 'ErrA'

enum ErrB implements Error
	splat
end 'ErrB'

function callA(which Score) returns Score throws ErrA
	if which == 1 'bad'
		throw ErrA.bad("bad {which}")
	end 'bad'

	return 5
end 'callA'

function callB() returns Score throws ErrB
	return 6
end 'callB'

function classify(which Score) returns String
	var said = "none"

	try 'work'
		let a = callA(which)
		let b = callB()
		said = "sum {a + b}"
	end 'work'
	otherwise (e) 'h'
		match e 'k'
			ErrA.bad then said = match e 'inner'
					ErrA.bad(msg) gives "{msg}!"
					default panic("not bad")
				end 'inner'
			ErrA.worse or
				splat then said = "other"
		end 'k'
	end 'h'

	return said
end 'classify'

function main() returns ExitCode
	print("{classify(1)}\n")
	print("{classify(0)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3161: <fragment>:35:25: 'e' may hold a boxed error, which only the arm of the match that runs releases, so every path through its handler must match 'e' exactly once: this path has already matched it. Match 'e' once, and bind in its arms whatever the rest of the handler needs
```

<!-- test: try-block.fallthrough-chain-through-a-boxed-members-payload-arm -->
A chain of `and fallthrough` arms over a combined error, starting at an arm that moves a boxed member's `String`
payload out, releases that box once on every path, and each later arm's own dispatch edge releases its own.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(msg String)
	worse(note String)
end 'ErrA'

enum ErrB implements Error
	splat
	crash
end 'ErrB'

function callA(which Score) returns Score throws ErrA
	if which == 1 'bad'
		throw ErrA.bad("bad {which}")
	end 'bad'

	if which == 2 'worse'
		throw ErrA.worse("worse {which}")
	end 'worse'

	return 5
end 'callA'

function callB(which Score) returns Score throws ErrB
	if which == 3 'splat'
		throw ErrB.splat
	end 'splat'

	if which == 4 'crash'
		throw ErrB.crash
	end 'crash'

	return 6
end 'callB'

function classify(which Score) returns String
	var said = ""

	try 'work'
		let a = callA(which)
		let b = callB(which)
		said = "sum {a + b}"
	end 'work'
	otherwise (e) 'h'
		match e 'k'
			ErrA.bad(msg) then said = msg and fallthrough
			splat then said = "{said}+splat" and fallthrough
			ErrA.worse then said = "{said}+worse"
			crash then said = "crash"
		end 'k'
	end 'h'

	return said
end 'classify'

function main() returns ExitCode
	print("{classify(1)}\n")
	print("{classify(2)}\n")
	print("{classify(3)}\n")
	print("{classify(4)}\n")
	print("{classify(0)}\n")
	return 0
end 'main'
```
```stdout
bad 1+splat+worse
+worse
+splat+worse
crash
sum 11
```
```exitcode
0
```

<!-- test: error.try-block-combined-error-matched-twice-in-sequence -->
Two `match e` statements in sequence match one combined error twice on one path, and the first already
released the box.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(msg String)
end 'ErrA'

enum ErrB implements Error
	splat
end 'ErrB'

function callA(which Score) returns Score throws ErrA
	if which == 1 'bad'
		throw ErrA.bad("bad {which}")
	end 'bad'

	return 5
end 'callA'

function callB(which Score) returns Score throws ErrB
	if which == 3 'splat'
		throw ErrB.splat
	end 'splat'

	return 6
end 'callB'

function classify(which Score) returns String
	var said = ""

	try 'work'
		let a = callA(which)
		let b = callB(which)
		said = "sum {a + b}"
	end 'work'
	otherwise (e) 'h'
		match e 'first'
			ErrA.bad(msg) then said = msg
			splat then said = "splat"
		end 'first'

		match e 'second'
			ErrA.bad(msg) then said = "{said}/{msg}"
			splat then said = "{said}/splat"
		end 'second'
	end 'h'

	return said
end 'classify'

function main() returns ExitCode
	print("{classify(1)}\n")
	print("{classify(3)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3161: <fragment>:42:3: 'e' may hold a boxed error, which only the arm of the match that runs releases, so every path through its handler must match 'e' exactly once: this path has already matched it. Match 'e' once, and bind in its arms whatever the rest of the handler needs
```

<!-- test: error.try-block-combined-error-matched-on-one-path-only -->
A combined error matched on only one path through its handler is never released on the other, so the join
the two paths reach is refused.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(msg String)
end 'ErrA'

enum ErrB implements Error
	splat
end 'ErrB'

function callA(which Score) returns Score throws ErrA
	if which == 1 or which == 2 'bad'
		throw ErrA.bad("bad {which}")
	end 'bad'

	return 5
end 'callA'

function callB(which Score) returns Score throws ErrB
	if which == 3 'splat'
		throw ErrB.splat
	end 'splat'

	return 6
end 'callB'

function classify(which Score) returns String
	var said = "quiet"

	try 'work'
		let a = callA(which)
		let b = callB(which)
		said = "sum {a + b}"
	end 'work'
	otherwise (e) 'h'
		if which == 1 'loud'
			match e 'k'
				ErrA.bad(msg) then said = msg
				splat then said = "splat"
			end 'k'
		end 'loud'
	end 'h'

	return said
end 'classify'

function main() returns ExitCode
	print("{classify(1)}\n")
	print("{classify(2)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3161: <fragment>:37:3: 'e' may hold a boxed error, which only the arm of the match that runs releases, so every path through its handler must match 'e' exactly once: it is matched on some of the paths that join here and not on others. Match 'e' once, and bind in its arms whatever the rest of the handler needs
```

<!-- test: error.try-block-combined-error-matched-inside-a-loop -->
A `match e` inside a loop the handler opened runs once per iteration — never, if the loop does not run, and
again on a box the first iteration released if it runs twice.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(msg String)
end 'ErrA'

enum ErrB implements Error
	splat
end 'ErrB'

function callA(which Score) returns Score throws ErrA
	if which == 1 'bad'
		throw ErrA.bad("bad {which}")
	end 'bad'

	return 5
end 'callA'

function callB() returns Score throws ErrB
	return 6
end 'callB'

function classify(which Score, tries Score) returns String
	var said = ""

	try 'work'
		let a = callA(which)
		let b = callB()
		said = "sum {a + b}"
	end 'work'
	otherwise (e) 'h'
		for _ in 0 upto tries 'again'
			match e 'k'
				ErrA.bad(msg) then said = "{said}{msg};"
				splat then said = "{said}splat;"
			end 'k'
		end 'again'
	end 'h'

	return said
end 'classify'

function main() returns ExitCode
	print("{classify(1, tries: 2)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3161: <fragment>:34:4: 'e' may hold a boxed error, which only the arm of the match that runs releases, so every path through its handler must match 'e' exactly once: this match is inside a loop the handler opened, which runs it zero times or more than once. Match 'e' once, and bind in its arms whatever the rest of the handler needs
```

<!-- test: error.try-block-combined-error-returns-before-its-match -->
A `return` that leaves the handler before its `match e` never releases a boxed error in flight.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(msg String)
end 'ErrA'

enum ErrB implements Error
	splat
end 'ErrB'

function callA(which Score) returns Score throws ErrA
	if which == 1 'bad'
		throw ErrA.bad("bad {which}")
	end 'bad'

	return 5
end 'callA'

function callB() returns Score throws ErrB
	return 6
end 'callB'

function classify(which Score, quiet bool) returns String
	var said = ""

	try 'work'
		let a = callA(which)
		let b = callB()
		said = "sum {a + b}"
	end 'work'
	otherwise (e) 'h'
		if quiet 'early'
			return "quiet"
		end 'early'

		match e 'k'
			ErrA.bad(msg) then said = msg
			splat then said = "splat"
		end 'k'
	end 'h'

	return said
end 'classify'

function main() returns ExitCode
	print("{classify(1, quiet: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3161: <fragment>:34:4: 'e' may hold a boxed error, which only the arm of the match that runs releases, so every path through its handler must match 'e' exactly once: a path leaves the handler here without having matched it. Match 'e' once, and bind in its arms whatever the rest of the handler needs
```

<!-- test: error.try-block-combined-error-breaks-out-before-its-match -->
A `break` out of a loop around the whole `try` leaves the handler too, and before its `match e` it never releases
a boxed error in flight.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(msg String)
end 'ErrA'

enum ErrB implements Error
	splat
end 'ErrB'

function callA(which Score) returns Score throws ErrA
	if which == 1 'bad'
		throw ErrA.bad("bad {which}")
	end 'bad'

	return 5
end 'callA'

function callB() returns Score throws ErrB
	return 6
end 'callB'

function main() returns ExitCode
	var said = ""

	for which in 0 upto 3 'each'
		try 'work'
			let a = callA(which)
			let b = callB()
			said = "{said}{a + b};"
		end 'work'
		otherwise (e) 'h'
			if which == 1 'stop'
				break
			end 'stop'

			match e 'k'
				ErrA.bad(msg) then said = "{said}{msg};"
				splat then said = "{said}splat;"
			end 'k'
		end 'h'
	end 'each'

	print("{said}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3161: <fragment>:35:5: 'e' may hold a boxed error, which only the arm of the match that runs releases, so every path through its handler must match 'e' exactly once: a path leaves the handler here without having matched it. Match 'e' once, and bind in its arms whatever the rest of the handler needs
```

<!-- test: try-block.combined-error-matched-once-after-other-statements -->
A handler over a combined error with a boxed member may do other work before its one `match e` — a nested
`try` block, an `if` — and may match `e` on each arm of an `if`/`else`, which is one match per path. Every path
releases the box exactly once: a leak exits 101, a double release faults.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(msg String)
end 'ErrA'

enum ErrB implements Error
	splat
end 'ErrB'

enum ErrC implements Error
	late
end 'ErrC'

function callA(which Score) returns Score throws ErrA
	if which == 1 or which == 2 'bad'
		throw ErrA.bad("bad {which}")
	end 'bad'

	return 5
end 'callA'

function callB(which Score) returns Score throws ErrB
	if which == 3 'splat'
		throw ErrB.splat
	end 'splat'

	return 6
end 'callB'

function callC(which Score) returns Score throws ErrC
	if which == 2 'late'
		throw ErrC.late
	end 'late'

	return 7
end 'callC'

function classify(which Score) returns String
	var said = ""

	try 'work'
		let a = callA(which)
		let b = callB(which)
		said = "sum {a + b}"
	end 'work'
	otherwise (e) 'h'
		var extra = 0

		try 'inner'
			extra = callC(which)
		end 'inner'
		otherwise (f) 'innerHandler'
			match f 'lateness'
				late then extra = 1
			end 'lateness'
		end 'innerHandler'

		if extra == 1 'loud'
			match e 'k'
				ErrA.bad(msg) then said = "{msg}!"
				splat then said = "splat!"
			end 'k'
		end 'loud' else 'soft'
			match e 'k2'
				ErrA.bad(msg) then said = "{msg}+{extra}"
				splat then said = "splat+{extra}"
			end 'k2'
		end 'soft'
	end 'h'

	return said
end 'classify'

function main() returns ExitCode
	print("{classify(1)}\n")
	print("{classify(2)}\n")
	print("{classify(3)}\n")
	print("{classify(0)}\n")
	return 0
end 'main'
```
```stdout
bad 1+7
bad 2!
splat+7
sum 11
```
```exitcode
0
```

<!-- test: error.try-block-combined-error-matched-on-the-right-of-and -->
A `match e` on the right of `and` runs only when the left is true, so the path that skips it leaves the handler
without having matched `e`.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(msg String)
end 'ErrA'

enum ErrB implements Error
	splat
end 'ErrB'

function callA(which Score) returns Score throws ErrA
	if which == 1 or which == 2 'bad'
		throw ErrA.bad("bad {which}")
	end 'bad'

	return 5
end 'callA'

function callB() returns Score throws ErrB
	return 6
end 'callB'

function classify(which Score) returns String
	var said = "quiet"

	try 'work'
		let a = callA(which)
		let b = callB()
		said = "sum {a + b}"
	end 'work'
	otherwise (e) 'h'
		let loud = which == 1 and match e 'k'
			ErrA.bad gives true
			splat gives false
		end 'k'

		said = "{loud}"
	end 'h'

	return said
end 'classify'

function main() returns ExitCode
	print("{classify(1)}\n")
	print("{classify(2)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3161: <fragment>:33:25: 'e' may hold a boxed error, which only the arm of the match that runs releases, so every path through its handler must match 'e' exactly once: it is matched on some of the paths that join here and not on others. Match 'e' once, and bind in its arms whatever the rest of the handler needs
```

<!-- test: error.try-block-combined-error-matched-in-a-while-condition -->
A `match e` in a `while` condition runs on every trip, so a second trip matches a box the first released.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(msg String)
end 'ErrA'

enum ErrB implements Error
	splat
end 'ErrB'

function callA(which Score) returns Score throws ErrA
	if which == 1 'bad'
		throw ErrA.bad("bad {which}")
	end 'bad'

	return 5
end 'callA'

function callB() returns Score throws ErrB
	return 6
end 'callB'

function classify(which Score) returns Score
	var trips = 0

	try 'work'
		let a = callA(which)
		let b = callB()
		trips = a + b
	end 'work'
	otherwise (e) 'h'
		while trips < 3 and match e 'k'
			ErrA.bad(msg) gives msg.byteLength() > 0
			splat gives false
		end 'k' 'again'
			trips = trips + 1
		end 'again'
	end 'h'

	return trips
end 'classify'

function main() returns ExitCode
	print("{classify(1)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3161: <fragment>:33:3: 'e' may hold a boxed error, which only the arm of the match that runs releases, so every path through its handler must match 'e' exactly once: this match is inside a loop the handler opened, which runs it zero times or more than once. Match 'e' once, and bind in its arms whatever the rest of the handler needs
```

<!-- test: error.try-block-combined-error-matched-between-two-calls-of-an-inner-try -->
A `match e` between two throwing calls of a `try` block inside the handler is matched on the second call's
error path and not on the first's, which reach the inner handler together.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(msg String)
end 'ErrA'

enum ErrB implements Error
	splat
end 'ErrB'

enum ErrC implements Error
	boom
end 'ErrC'

function callA(which Score) returns Score throws ErrA
	if which == 1 'bad'
		throw ErrA.bad("bad {which}")
	end 'bad'

	return 5
end 'callA'

function callB() returns Score throws ErrB
	return 6
end 'callB'

function callC(which Score) returns Score throws ErrC
	if which == 1 'boom'
		throw ErrC.boom
	end 'boom'

	return 7
end 'callC'

function classify(which Score) returns String
	var said = ""

	try 'work'
		let a = callA(which)
		let b = callB()
		said = "sum {a + b}"
	end 'work'
	otherwise (e) 'h'
		try 'inner'
			let first = callC(which)
			said = match e 'k'
				ErrA.bad(msg) gives msg
				splat gives "splat"
			end 'k'
			let second = callC(first)
			said = "{said}{second}"
		end 'inner'
		otherwise (f) 'ih'
			match f 'fk'
				boom then said = "boom"
			end 'fk'
		end 'ih'
	end 'h'

	return said
end 'classify'

function main() returns ExitCode
	print("{classify(1)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3161: <fragment>:45:3: 'e' may hold a boxed error, which only the arm of the match that runs releases, so every path through its handler must match 'e' exactly once: it is matched on some of the paths that join here and not on others. Match 'e' once, and bind in its arms whatever the rest of the handler needs
```

<!-- test: error.try-block-combined-error-matched-in-an-arm-another-arm-falls-from -->
An `and fallthrough` arm reached both from an arm that matched `e` and straight from the dispatch arrives with
`e` matched on one edge and not on the other.
```maxon
typealias Score = int(0 to 1000)

union ErrA implements Error
	bad(msg String)
end 'ErrA'

enum ErrB implements Error
	splat
end 'ErrB'

function callA(which Score) returns Score throws ErrA
	if which == 1 or which == 2 'bad'
		throw ErrA.bad("bad {which}")
	end 'bad'

	return 5
end 'callA'

function callB() returns Score throws ErrB
	return 6
end 'callB'

function classify(which Score) returns String
	var said = ""

	try 'work'
		let a = callA(which)
		let b = callB()
		said = "sum {a + b}"
	end 'work'
	otherwise (e) 'h'
		match which 'w'
			1 then said = match e 'k'
				ErrA.bad(msg) gives msg
				splat gives "splat"
			end 'k' and fallthrough
			default then said = "{said}!"
		end 'w'
	end 'h'

	return said
end 'classify'

function main() returns ExitCode
	print("{classify(1)}\n")
	print("{classify(2)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3161: <fragment>:38:4: 'e' may hold a boxed error, which only the arm of the match that runs releases, so every path through its handler must match 'e' exactly once: it is matched on some of the paths that join here and not on others. Match 'e' once, and bind in its arms whatever the rest of the handler needs
```

<!-- test: implied-try-block-witness-dispatch -->
A bare throwing witness dispatch inside a try block routes to the block's handler, as a bare direct call
does. A `Point` of 3 throws `tooSmall`, so the handler's 55 is the answer; a dropped error flag would return the
impl's throw-path primary instead.
```maxon
typealias Code = int(0 to u32.max)

enum DigestError implements Error
	tooSmall
end 'DigestError'

interface Digest
	function digest() returns Code throws DigestError
end 'Digest'

type Point implements Digest
	let x as Code

	static function create(x Code) returns Self
		return Self{x: x}
	end 'create'

	function digest() returns Code throws DigestError
		if self.x < 10 'small'
			throw DigestError.tooSmall
		end 'small'

		return self.x
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function itemDigest() returns Code
		var result = 0 as Code

		try 'work'
			result = self.item.digest()
		end 'work' otherwise (e) 'h'
			match e 'k'
				tooSmall then result = 55
			end 'k'
		end 'h'

		return result
	end 'itemDigest'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(3))
	return b.itemDigest()
end 'main'
```
```exitcode
55
```

<!-- test: implied-try-block-await -->
A bare `await` of a throwing promise inside a try block routes the awaited error to the block's handler.
The thunk throws, so the handler's 42 is the answer; the success value is 10.
```maxon
typealias Integer = int(i64.min to i64.max)

enum WorkError implements Error
	failed
end 'WorkError'

function mayFail(succeed bool) returns Integer throws WorkError
	Scheduler.yield()

	if succeed 'ok'
		return 10
	end 'ok'

	throw WorkError.failed
end 'mayFail'

function main() returns ExitCode
	var result = 0 as Integer
	let p = async mayFail(false)

	try 'work'
		result = await p
	end 'work' otherwise (e) 'h'
		match e 'k'
			failed then result = 42
		end 'k'
	end 'h'

	return result as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: implied-try-block-argument-of-explicit-try -->
An explicit `try … otherwise` inside a try block covers its own target call only. The throwing call in
that target's ARGUMENT list routes to the block's handler, so the answer is the handler's 42, not the
explicit `otherwise`'s 0 and not the success path's 5.
```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
	kaboom
end 'ErrA'

enum ErrB implements Error
	splat
end 'ErrB'

function inner(fail bool) returns Score throws ErrA
	if fail 'c'
		throw ErrA.kaboom
	end 'c'

	return 5
end 'inner'

function outer(n Score) returns Score throws ErrB
	if n > 50 'big'
		throw ErrB.splat
	end 'big'

	return n
end 'outer'

function main() returns ExitCode
	var sum = 0

	try 'work'
		let v = try outer(inner(true)) otherwise 0
		sum = v
	end 'work' otherwise (e) 'h'
		match e 'k'
			kaboom then sum = 42
		end 'k'
	end 'h'

	return sum
end 'main'
```
```exitcode
42
```

<!-- test: implied-try-block-explicit-try-of-a-rebranded-call -->
An explicit `try … otherwise` on a parenthesized call renamed to another brand of the same instance, inside
a try block, keeps its own error: the rebrand emits no op, so the author's `otherwise` owns the call. The
answer is the fallback's empty count plus 7, not the block handler's 55.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias Scores = Array with Integer
typealias Tally = int(0 to 100)

enum ApiError implements Error
	notFound
end 'ApiError'

enum OtherError implements Error
	failed
end 'OtherError'

function items(hit bool) returns IntArray throws ApiError
	if not hit 'miss'
		throw ApiError.notFound
	end 'miss'

	var found = IntArray.create()
	found.push(4)
	return found
end 'items'

function other(fail bool) returns Tally throws OtherError
	if fail 'boom'
		throw OtherError.failed
	end 'boom'

	return 7
end 'other'

function main() returns ExitCode
	var result = 0 as Tally

	try 'work'
		let s = try (items(false) as Scores) otherwise Scores.create()
		result = (s.count() as Tally) + other(false)
	end 'work' otherwise (e) 'h'
		match e 'k'
			failed then result = 55
		end 'k'
	end 'h'

	return result
end 'main'
```
```exitcode
7
```

<!-- test: a-statement-after-a-try-block-whose-both-edges-leave -->
```maxon
enum Failure implements Error
	bad
end 'Failure'

function main() returns ExitCode
	try 'work'
		throw Failure.bad
	end 'work' otherwise (e) 'handler'
		match e 'kind'
			bad then return 3
		end 'kind'
	end 'handler'

	return 0
end 'main'
```
```exitcode
3
```

<!-- test: an-error-union-binding-read-after-a-boxed-arm-is-the-flag -->
```maxon
typealias Tally = int(0 to 1000)

union BoxErr implements Error
	boxed(text String)
end 'BoxErr'

enum Plain implements Error
	flat
	sharp
end 'Plain'

function fa(n Tally) returns Tally throws BoxErr
	if n > 5 'big'
		throw BoxErr.boxed("big {n}")
	end 'big'

	return 1
end 'fa'

function fb(n Tally) returns Tally throws Plain
	if n == 1 'one'
		throw Plain.sharp
	end 'one'

	return 2
end 'fb'

function probe(n Tally) returns Tally
	var total = 0 as Tally

	try 'work'
		total = total + fa(n)
		total = total + fb(n)
	end 'work' otherwise (e) 'handler'
		match e 'kind'
			BoxErr.boxed(text) then total = text.byteLength() as Tally
			Plain.flat then total = 100
			Plain.sharp then print("sharp {e}\n")
		end 'kind'

		let again = e
		total = total + (1 if again > 0 else 0)
	end 'handler'

	return total
end 'probe'

function main() returns ExitCode
	print("{probe(9)} {probe(1)} {probe(4)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
sharp 2
6 2 3
```

<!-- test: an-error-union-binding-inside-a-boxed-arm-is-the-member -->
```maxon
typealias Tally = int(0 to 1000)

union BoxErr implements Error
	boxed(text String)
end 'BoxErr'

enum Plain implements Error
	flat
	sharp
end 'Plain'

function fa(n Tally) returns Tally throws BoxErr
	if n > 5 'big'
		throw BoxErr.boxed("big {n}")
	end 'big'

	return 1
end 'fa'

function fb(n Tally) returns Tally throws Plain
	if n == 1 'one'
		throw Plain.sharp
	end 'one'

	return 2
end 'fb'

function probe(n Tally) returns Tally
	var total = 0 as Tally

	try 'work'
		total = total + fa(n)
		total = total + fb(n)
	end 'work' otherwise (e) 'handler'
		match e 'kind'
			BoxErr.boxed(text) then print("in arm {e} {text}\n")
			Plain.flat then total = 100
			Plain.sharp then print("sharp {e}\n")
		end 'kind'

		let again = e
		total = total + (1 if again > 0 else 0)
	end 'handler'

	return total
end 'probe'

function main() returns ExitCode
	print("{probe(9)} {probe(1)} {probe(4)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
in arm boxed big 9
sharp 2
1 2 3
```

<!-- test: an-error-union-binding-copied-into-a-var-after-a-boxed-arm -->
```maxon
typealias Tally = int(0 to 1000)

union BoxErr implements Error
	boxed(text String)
end 'BoxErr'

enum Plain implements Error
	flat
	sharp
end 'Plain'

function fa(n Tally) returns Tally throws BoxErr
	if n > 5 'big'
		throw BoxErr.boxed("big {n}")
	end 'big'

	return 1
end 'fa'

function fb(n Tally) returns Tally throws Plain
	if n == 1 'one'
		throw Plain.sharp
	end 'one'

	return 2
end 'fb'

function probe(n Tally) returns Tally
	var total = 0 as Tally

	try 'work'
		total = total + fa(n)
		total = total + fb(n)
	end 'work' otherwise (e) 'handler'
		match e 'kind'
			BoxErr.boxed(text) then total = text.byteLength() as Tally
			Plain.flat then total = 100
			Plain.sharp then print("sharp {e}\n")
		end 'kind'

		var again = e
		again = again + 1
		total = total + (1 if again > 1 else 0)
	end 'handler'

	return total
end 'probe'

function main() returns ExitCode
	print("{probe(9)} {probe(1)} {probe(4)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
sharp 2
6 2 3
```

<!-- test: an-error-union-binding-copied-into-a-var-reassigned-on-one-path -->
```maxon
typealias Tally = int(0 to 1000)

union BoxErr implements Error
	boxed(text String)
end 'BoxErr'

enum Plain implements Error
	flat
	sharp
end 'Plain'

function fa(n Tally) returns Tally throws BoxErr
	if n > 5 'big'
		throw BoxErr.boxed("big {n}")
	end 'big'

	return 1
end 'fa'

function fb(n Tally) returns Tally throws Plain
	if n == 1 'one'
		throw Plain.sharp
	end 'one'

	return 2
end 'fb'

function probe(n Tally) returns Tally
	var total = 0 as Tally

	try 'work'
		total = total + fa(n)
		total = total + fb(n)
	end 'work' otherwise (e) 'handler'
		match e 'kind'
			BoxErr.boxed(text) then total = text.byteLength() as Tally
			Plain.flat then total = 100
			Plain.sharp then print("sharp {e}\n")
		end 'kind'

		var again = e

		if n > 500 'never'
			again = 0
		end 'never'

		total = total + (1 if again > 0 else 0)
	end 'handler'

	return total
end 'probe'

function main() returns ExitCode
	print("{probe(9)} {probe(1)} {probe(4)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
sharp 2
6 2 3
```

<!-- test: an-error-union-binding-assigned-to-an-outer-var-after-a-boxed-arm -->
```maxon
typealias Tally = int(0 to 1000)

union BoxErr implements Error
	boxed(text String)
end 'BoxErr'

enum Plain implements Error
	flat
	sharp
end 'Plain'

function fa(n Tally) returns Tally throws BoxErr
	if n > 5 'big'
		throw BoxErr.boxed("big {n}")
	end 'big'

	return 1
end 'fa'

function fb(n Tally) returns Tally throws Plain
	if n == 1 'one'
		throw Plain.sharp
	end 'one'

	return 2
end 'fb'

function probe(n Tally) returns Tally
	var total = 0 as Tally
	var x = 0

	try 'work'
		total = total + fa(n)
		total = total + fb(n)
	end 'work' otherwise (e) 'handler'
		match e 'kind'
			BoxErr.boxed(text) then total = text.byteLength() as Tally
			Plain.flat then total = 100
			Plain.sharp then print("sharp {e}\n")
		end 'kind'

		x = e
	end 'handler'

	return total + (1 if x > 0 else 0)
end 'probe'

function main() returns ExitCode
	print("{probe(9)} {probe(1)} {probe(4)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
sharp 2
6 2 3
```

<!-- test: an-error-union-binding-given-by-an-arm-after-a-boxed-arm -->
```maxon
typealias Tally = int(0 to 1000)

union BoxErr implements Error
	boxed(text String)
end 'BoxErr'

enum Plain implements Error
	flat
	sharp
end 'Plain'

function fa(n Tally) returns Tally throws BoxErr
	if n > 5 'big'
		throw BoxErr.boxed("big {n}")
	end 'big'

	return 1
end 'fa'

function fb(n Tally) returns Tally throws Plain
	if n == 1 'one'
		throw Plain.sharp
	end 'one'

	return 2
end 'fb'

function probe(n Tally) returns Tally
	var total = 0 as Tally

	try 'work'
		total = total + fa(n)
		total = total + fb(n)
	end 'work' otherwise (e) 'handler'
		let got = match e 'kind'
			BoxErr.boxed(text) gives text.byteLength()
			Plain.flat gives 100
			Plain.sharp gives e
		end 'kind'

		total = got as Tally
	end 'handler'

	return total
end 'probe'

function main() returns ExitCode
	print("{probe(9)} {probe(1)} {probe(4)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 2 3
```

<!-- test: a-closure-in-a-boxed-arm-does-not-see-the-handlers-error-union -->
```maxon
typealias Tally = int(0 to 1000)
typealias Measure = function() returns Tally

union BoxErr implements Error
	boxed(text String)
end 'BoxErr'

enum Plain implements Error
	flat
	sharp
end 'Plain'

function fa(n Tally) returns Tally throws BoxErr
	if n > 5 'big'
		throw BoxErr.boxed("big {n}")
	end 'big'

	return 1
end 'fa'

function fb(n Tally) returns Tally throws Plain
	if n == 1 'one'
		throw Plain.sharp
	end 'one'

	return 2
end 'fb'

function run(m Measure) returns Tally
	return m()
end 'run'

function pick(a Tally, b BoxErr) returns Tally
	return a + textLength(b)
end 'pick'

function textLength(b BoxErr) returns Tally
	return match b 'k'
		boxed(text) gives text.byteLength() as Tally
	end 'k'
end 'textLength'

function inArm(n Tally) returns Tally
	var total = 0 as Tally

	try 'work'
		total = total + fa(n)
		total = total + fb(n)
	end 'work' otherwise (e) 'handler'
		match e 'kind'
			BoxErr.boxed then total = run(function() gives (n + n + n + n + n + n) * 0 + match e 'outer'
				boxed(e) gives match e 'inner'
					"big 9" gives 1 as Tally
					default gives 2 as Tally
				end 'inner'
			end 'outer')
			Plain.flat then total = 100
			Plain.sharp then total = 7
		end 'kind'
	end 'handler'

	return total
end 'inArm'

function main() returns ExitCode
	let a = inArm(9)
	let b = inArm(1)
	let c = inArm(0)
	print("inArm {a} {b} {c}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
inArm 1 7 3
```

<!-- test: a-closure-in-a-scalar-arm-does-not-see-the-handlers-error-union -->
```maxon
typealias Tally = int(0 to 1000)
typealias Measure = function() returns Tally

union BoxErr implements Error
	boxed(text String)
end 'BoxErr'

enum Plain implements Error
	flat
	sharp
end 'Plain'

function fa(n Tally) returns Tally throws BoxErr
	if n > 5 'big'
		throw BoxErr.boxed("big {n}")
	end 'big'

	return 1
end 'fa'

function fb(n Tally) returns Tally throws Plain
	if n == 1 'one'
		throw Plain.sharp
	end 'one'

	return 2
end 'fb'

function run(m Measure) returns Tally
	return m()
end 'run'

function pick(a Tally, b BoxErr) returns Tally
	return a + textLength(b)
end 'pick'

function textLength(b BoxErr) returns Tally
	return match b 'k'
		boxed(text) gives text.byteLength() as Tally
	end 'k'
end 'textLength'

function inArm(n Tally) returns Tally
	var total = 0 as Tally
	let s = BoxErr.boxed("big 9")

	try 'work'
		total = total + fa(n)
		total = total + fb(n)
	end 'work' otherwise (e) 'handler'
		match e 'kind'
			BoxErr.boxed then total = 50
			Plain.flat then total = 100
			Plain.sharp then total = run(function() gives (n + n) * 0 + match s 'outer'
				boxed(e) gives match e 'inner'
					"big 9" gives 1 as Tally
					default gives 2 as Tally
				end 'inner'
			end 'outer')
		end 'kind'
	end 'handler'

	return total
end 'inArm'

function main() returns ExitCode
	let a = inArm(9)
	let b = inArm(1)
	let c = inArm(0)
	print("inArm {a} {b} {c}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
inArm 50 1 3
```

<!-- test: a-closure-in-a-boxed-arm-captures-the-member -->
```maxon
typealias Tally = int(0 to 1000)
typealias Reader = function() returns String
typealias TextArray = Array with String

union BoxErr implements Error
	boxed(text String)
end 'BoxErr'

enum Plain implements Error
	flat
	sharp
end 'Plain'

function fa(n Tally) returns Tally throws BoxErr
	if n > 5 'big'
		throw BoxErr.boxed("big {n}")
	end 'big'

	return 1
end 'fa'

function fb(n Tally) returns Tally throws Plain
	if n == 1 'one'
		throw Plain.sharp
	end 'one'

	return 2
end 'fb'

function textOf(b BoxErr) returns String
	return match b 'k'
		boxed(text) gives text
	end 'k'
end 'textOf'

function applyReader(r Reader, into TextArray) returns Tally
	let t = r()
	into.push(t)
	return t.byteLength() as Tally
end 'applyReader'

function captured(n Tally, readers TextArray) returns Tally
	var sink = 0 as Tally

	try 'work'
		sink = sink + fa(n)
		sink = sink + fb(n)
	end 'work' otherwise (e) 'handler'
		match e 'kind'
			BoxErr.boxed then sink = applyReader(function() gives textOf(e), into: readers)
			Plain.flat then sink = 100
			Plain.sharp then sink = 7
		end 'kind'
	end 'handler'

	return sink
end 'captured'

function main() returns ExitCode
	var readers = TextArray.create()
	let c9 = captured(9, readers: readers)
	let c1 = captured(1, readers: readers)
	let c7 = captured(7, readers: readers)
	print("captured {c9} {c1} {c7} {readers.count()}\n")

	for r in readers 'eachReader'
		print("  {r}\n")
	end 'eachReader'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
captured 5 7 5 2
  big 9
  big 7
```

<!-- test: a-payload-binding-named-like-the-handlers-error-in-a-boxed-arm -->
```maxon
typealias Tally = int(0 to 1000)

union BoxErr implements Error
	boxed(text String)
end 'BoxErr'

enum Plain implements Error
	flat
	sharp
end 'Plain'

function fa(n Tally) returns Tally throws BoxErr
	if n > 5 'big'
		throw BoxErr.boxed("big {n}")
	end 'big'

	return 1
end 'fa'

function fb(n Tally) returns Tally throws Plain
	if n == 1 'one'
		throw Plain.sharp
	end 'one'

	return 2
end 'fb'

function payloadNamedE(n Tally) returns Tally
	var total = 0 as Tally

	try 'work'
		total = total + fa(n)
		total = total + fb(n)
	end 'work' otherwise (e) 'handler'
		match e 'kind'
			BoxErr.boxed(e) then total = e.byteLength() as Tally
			Plain.flat then total = 100
			Plain.sharp then total = 7
		end 'kind'
	end 'handler'

	return total
end 'payloadNamedE'

function main() returns ExitCode
	let a = payloadNamedE(9)
	let b = payloadNamedE(1)
	let c = payloadNamedE(0)
	print("payloadNamedE {a} {b} {c}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
payloadNamedE 5 7 3
```
