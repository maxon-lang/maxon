---
feature: try-block-shadowed-binding
status: experimental
keywords: [try, otherwise, block, error, union, shadowing, closure]
category: error-handling
---

# A Block-Form `try`'s Error Binding, Shadowed

## Documentation

A block-form `try 'l' … end 'l' otherwise (e) 'h' … end 'h'` whose body routes **two or more** distinct
error types binds `(e)` to a *synthesized error union*, and `match e` in the handler dispatches on which
`(member, case)` pair actually arrived. Maxon permits shadowing, so the handler's body may re-bind that very
name — and when it does, `match e` is a match on the **shadow**, with the shadow's own type and cases. The
error union is reached through the binding the handler installed, never through its spelling.

⛔⛔ **THIS FILE IS shv2-AUTHORED AND EXISTS BECAUSE THE FIRST IMPLEMENTATION GOT IT WRONG — SILENTLY.**
`/specs/try-block.md` says nothing about shadowing, so nothing in the ported suite could see it. shv2's
error-union lookup was keyed on the binding's **source NAME** alone, which made any inner `let e = …` hijack
the handler's dispatch: the shadow's `match` was compiled against the *error's* fused ordinal instead of
against the shadow's own enum. Measured on `shadowed-binding-is-not-the-error-union` below: **shv2 exited 11
where the C# bootstrap exits 22**, with no diagnostic anywhere. Case names were chosen to collide on purpose
— with non-colliding names the same defect surfaced as a loud `E3034 no case '…' in the error union`, which
is how the silent form was found at all. The key is now the binding's **identity** (the name must still
resolve to the value the handler bound), so a shadow simply is not the union.

**A lossy key used as an identity** — the shape this tree has paid for before, and the one the union's own
design note claimed to have avoided by not joining member names into a table key. That claim was true of one
handler against another and false of a handler against its own body.

⚠ **E3084 IS KEYED ON THE NAME, AND DELIBERATELY SO — the two questions are not the same question.** The
dispatch asks *"which value is being discriminated?"*, which a shadow answers differently and must. E3084
asks *"did the author write a `match` on this name in this handler?"*, which is syntactic, and both
reference compilers implement it syntactically. Tightening E3084 to the identity too was measured to refuse
`handler-matching-only-a-shadow-still-compiles` — a program the bootstrap compiles and runs — so the strict
reading is not the language's. Both halves are pinned here so neither can be "tidied" into the other.

## Tests

<!-- test: try-block-shadowed-binding.shadowed-binding-is-not-the-error-union -->
### RED-GATE CONTROL. Returned **11** before the identity key; returns **22** after, which is the oracle's answer.

The body routes two error types, so `(e)` is a synthesized error union; `callA(true)` throws, so `ErrA.bad`
is the error in flight. The handler then shadows `e` with an `Inner` value whose cases are spelled `bad` and
`splat` — the same two names the union's members carry. `match e` is a match on the shadow, so it must take
the `splat` arm (the shadow holds `Inner.splat`) and answer 22. Keyed on the name alone it took the union's
dispatch and answered 11, which is the `bad` arm — the error's case, matched against the shadow's arms.

```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    bad
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

enum Inner
    bad
    splat
end 'Inner'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.bad
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
        if true 'shadowscope'
            let e = Inner.splat
            match e 'inner'
                bad then sum = 11
                splat then sum = 22
            end 'inner'
        end 'shadowscope'
    end 'h'
    return sum
end 'main'
```
```exitcode
22
```

<!-- test: try-block-shadowed-binding.handler-matching-only-a-shadow-still-compiles -->
E3084's half of the same program: the handler's ONLY `match e` names a shadow, so the error itself is never
discriminated — and that is accepted, because the rule is about what the author WROTE and both references
read it that way. The case is the one above with the shadow's arms made unreachable-but-distinct, so a
regression that revived the union dispatch here would answer 33 rather than 44.

```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    bad
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

enum Inner
    bad
    splat
end 'Inner'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.bad
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
        let e = Inner.bad
        match e 'inner'
            bad then sum = 44
            splat then sum = 33
        end 'inner'
    end 'h'
    return sum
end 'main'
```
```exitcode
44
```

<!-- test: try-block-shadowed-binding.unshadowed-binding-is-still-the-error-union -->
The control the two cases above need: the SAME handler with no shadow at all still reaches the synthesized
error union, so the identity test rejects a shadow without rejecting the binding itself. `callB(true)`
throws, so the `ErrB.splat` arm must win.

```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    bad
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.bad
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
            ErrA.bad then sum = 11
            ErrB.splat then sum = 22
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
22
```

<!-- test: try-block-shadowed-binding.shadow-in-a-nested-handler-scope -->
The shadow need not be in an inner block: a handler that re-binds the name at its own top level, and then
runs a further `match` on it, must still dispatch on the shadow. This is the case an identity test keyed on
the innermost record rather than on the bound VALUE would get wrong, because both the union record and the
shadow are live in the same frame at the same depth.

```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    one
end 'ErrA'

enum ErrB implements Error
    two
end 'ErrB'

enum Other
    alpha
    beta
end 'Other'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.one
    end 'c'
    return 5
end 'callA'

function callB(x bool) returns Score throws ErrB
    if x 'c'
        throw ErrB.two
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
        let e = Other.beta
        match e 'shadowed'
            alpha then sum = 7
            beta then sum = 9
        end 'shadowed'
    end 'h'
    return sum
end 'main'
```
```exitcode
9
```

<!-- test: error.closure-inside-a-handler-does-not-discharge-e3084 -->
A CLOSURE DECLARED INSIDE A HANDLER is a different `IrFunction`, so neither the handler's error-union binding
nor its E3084 obligation may be visible inside it — a `match e` there names the CLOSURE's own `e`, and a
fused phi from the enclosing function would be a value that function does not have. The parser state
carrying both is therefore saved and reset per closure, exactly as the try-block stack beside it is.

This is the case that discriminates the reset, and only this one: the identity key already makes the closure's
`e` not the union (its parameter is a different value), so a *wrong answer* cannot be observed here. What is
observable is E3084, which is keyed on the NAME — so without the per-closure reset the closure's `match e`
would mark the OUTER handler's obligation satisfied and this program would COMPILE, with the real error
never discriminated at all.

```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    bad
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

enum Inner
    bad
    splat
end 'Inner'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.bad
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
        let pick = function(e Inner) gives match e 'inner'
            bad gives 3
            splat gives 8
        end 'inner'
        sum = pick(Inner.splat)
    end 'h'
    return sum
end 'main'
```
```maxoncstderr
error E3084: specs/fragments/try-block-shadowed-binding/error.closure-inside-a-handler-does-not-discharge-e3084.test:38:19: otherwise block must contain a match on the error binding 'e'
```

<!-- test: try-block-shadowed-binding.closure-inside-a-handler-matches-its-own-binding -->
The same shape with the handler's own obligation discharged: the closure's `e` is its PARAMETER, so its
`match e` dispatches on `Inner` and answers 8, while the handler's `match e` reaches the synthesized error
union and takes the `ErrA.bad` arm — which is the error really in flight.

```maxon
typealias Score = int(0 to 100)

enum ErrA implements Error
    bad
end 'ErrA'

enum ErrB implements Error
    splat
end 'ErrB'

enum Inner
    bad
    splat
end 'Inner'

function callA(x bool) returns Score throws ErrA
    if x 'c'
        throw ErrA.bad
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
        let pick = function(e Inner) gives match e 'inner'
            bad gives 3
            splat gives 8
        end 'inner'
        match e 'k'
            ErrA.bad then sum = pick(Inner.splat)
            ErrB.splat then sum = pick(Inner.bad)
        end 'k'
    end 'h'
    return sum
end 'main'
```
```exitcode
8
```
