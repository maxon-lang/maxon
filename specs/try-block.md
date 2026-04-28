---
feature: try-block
status: experimental
keywords: [try, otherwise, block, error, match, union]
category: error-handling
---

# Try Block (Multi-Call Error Handling)

## Documentation

The `try { } otherwise (e) { match e { } }` block construct lets you wrap a sequence of statements containing several throwing calls under a single error handler. Within the `try` block, calls to throwing functions do **not** require the `try` keyword; the parser implicitly routes their errors to the shared `otherwise` clause.

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

Inside the block, an explicit `try expr otherwise ...` form still works for any single call — its error is consumed by its own `otherwise` and does not contribute to the synthesized union.

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
        throw ErrA.bad(code: 50)
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
        throw ErrA.bad(code: 50)
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
        throw ErrA.bad(code: 50)
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
        throw ErrA.bad(code: 50)
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
        throw ErrA.bad(code: 50)
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
        throw ErrA.bad(code: 50)
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
        throw ErrA.bad(code: 50, msg: 7)
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
            ErrA.bad(code, msg) then sum = code + msg
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
        throw ErrA.bad(code: 50)
    end 'c1'
    if x == 2 'c2'
        throw ErrA.worse(level: 200)
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
            ErrA.worse then sum = 777
            ErrB.splat then sum = 99
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
777
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
        throw ErrA.bad(code: 50, msg: 1)
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
            fooErr then result = 100
            barErr then result = 200
        end 'he'
    end 'h'
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
301
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
    end 'work' otherwise(e) 'h'
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
    end 'wrap' otherwise(e) 'h'
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
<!-- MmTrace -->
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
    end 'work' otherwise(e) 'h'
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Idx #1 size=40 [IntArray.create]
  sl_alloc __ManagedMemory_Idx #1 size=72 class=6
mm_alloc IntArray #2 size=8 [IntArray.create]
  sl_alloc IntArray #2 size=40 class=4
mm_incref __ManagedMemory_Idx #1 rc=1 [IntArray.create]
mm_incref IntArray #2 rc=1 [IntArray.create]
mm_transfer IntArray #2 rc=1 [IntArray.create]
mm_decref IntArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Idx #1 rc=0 [~IntArray]
    mm_free __ManagedMemory_Idx #1
      sl_free __ManagedMemory_Idx #1 size=96 class=6
  mm_free IntArray #2
    sl_free IntArray #2 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```
