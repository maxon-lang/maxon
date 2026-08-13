---
feature: match-expr-all-arms-diverge
status: experimental
keywords: [match, expression, panic, throws, divergent, E2015, bounded-gap]
category: control-flow
---

# A Match Expression Whose EVERY Arm Diverges Is REFUSED, Not Crashed

## Documentation

⚠ **THIS SPEC PINS A BOUNDED GAP IN shv2, NOT A RULE OF THE LANGUAGE.** The programs below are legal
Maxon: **the C# bootstrap compiles and runs every one of them** (measured — the value binding is simply
never reached, because control leaves through the arm that ran). shv2 refuses them. The refusal is the
honest answer to a construct it does not implement yet; it is **not** a claim that the programs are
wrong, and it must not be read as one.

A match *expression* takes its value from the arms that reach the merge. `match-expr-arm-divergent` lets
an individual arm end in `panic("…")` or `throws E.case` instead of `gives <expr>` — such an arm produces
no value and feeds no phi, which is exactly what makes the feature work. Run that to its limit and
**every** arm diverges, so nothing at all reaches the merge and the expression has no value to hand the
binding around it:

```text
let r = match c 'e'
    red panic("a")
    green panic("b")
end 'e'
```

**shv2 has no `never`-typed expression result to stand in for the missing value.** `parseExpression`
returns a `ValueId` that the whole Pratt climber then reads and types, so there is no representation for
"this expression yields nothing and control does not come back". Closing the gap means giving the
expression tier such a representation and teaching every consumer of a `ValueId` about it — a change to
the expression tier, not to `match`. Until then the construct is refused as **E2015**, positioned at the
`match` keyword.

**E2015 is deliberate.** The registry defines it as *"the source uses a language construct this compiler
does not implement yet"*, which is precisely this case. A syntax code (E2001) or a semantic one (E3xxx)
would file a pending feature as a permanent illegality — the mistake this parser records having made in
the opposite direction at `expectedDeclaration` and `selfOutsideInstanceMethod`, where a permanent
refusal had been filed as a pending one.

## Two Doors Reach It, and Both Are Pinned

The condition is "no arm reaches the merge", so it is reachable from either divergent spelling:

- **every PATTERN arm diverges** — the door `match-expr-arm-divergent` opened;
- **a diverging `default` is the ONLY arm** — reachable since the `default panic` / `default throws`
  catch-all landed at P1.4b, and measured crashing the compiler long before per-arm divergence existed.

Both are pinned below, because a fix that closed only one of them would leave the other reporting
whatever the untaken path happened to do.

## What It Replaced

Before this, both doors ended in `finalizeMatchMerge`'s internal `panic` — a compiler crash with a
twenty-frame stack trace on a legal program, which is never an acceptable answer. It also stranded the
checkout's tree lock for its full 60-second abandonment window, blocking every following command in that
tree. The sibling spec `match-expr-divergent-class` records the same shape one step away in the same
merge: a backend `panic: crosses register files` on a user program, replaced by real behaviour.

## The Cure the Diagnostic Names

The message offers two, and both are real:

- **make it a match STATEMENT** — `then panic(…)` arms need no value, so the all-diverging shape is
  already expressible today (green case below);
- **give at least one arm a `gives`** — the whole of `match-expr-arm-divergent`, which passes.

## Tests

<!-- test: error.every-pattern-arm-panics -->
Every pattern arm of an exhaustive enum match diverges, so no arm reaches the merge.
```maxon
enum Colour
	red
	green
end 'Colour'

function main() returns ExitCode
	let c = Colour.red
	let r = match c 'e'
		red panic("a")
		green panic("b")
	end 'e'
	return r
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/match-expr-all-arms-diverge/error.every-pattern-arm-panics.test:9:10: Unsupported: a match EXPRESSION whose every arm diverges — each arm ends in `panic(…)` or `throws`, so no arm reaches the merge and there is no value to give the binding or expression around it. Write it as a match STATEMENT (`then panic(…)` arms need no value), or give at least one arm a `gives`
```

<!-- test: error.default-panic-is-the-only-arm -->
The second door, and the older one: a diverging `default` as the only arm, on a scalar match. This
spelling reached the crash before per-arm divergence existed, so it is pinned independently rather than
assumed to travel with the case above.
```maxon
function main() returns ExitCode
	let x = 3
	let r = match x 'e'
		default panic("nothing to give")
	end 'e'
	return r
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/match-expr-all-arms-diverge/error.default-panic-is-the-only-arm.test:4:10: Unsupported: a match EXPRESSION whose every arm diverges — each arm ends in `panic(…)` or `throws`, so no arm reaches the merge and there is no value to give the binding or expression around it. Write it as a match STATEMENT (`then panic(…)` arms need no value), or give at least one arm a `gives`
```

<!-- test: error.every-arm-throws -->
The other divergent keyword, and a mix of the two doors: a `throws` pattern arm and a `throws` default,
inside a function that declares `throws`. The condition is "no arm reaches the merge", so which keyword
diverged and which arm position it sat in make no difference to the answer.
```maxon
typealias Integer = int(i64.min to i64.max)

enum E
	bad
	worse
end 'E'

function pick(x Integer) returns ExitCode throws E
	let r = match x 'e'
		1 throws E.bad
		default throws E.worse
	end 'e'
	return r
end 'pick'

function main() returns ExitCode
	let v = try pick(1) otherwise 7
	return v
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/match-expr-all-arms-diverge/error.every-arm-throws.test:10:10: Unsupported: a match EXPRESSION whose every arm diverges — each arm ends in `panic(…)` or `throws`, so no arm reaches the merge and there is no value to give the binding or expression around it. Write it as a match STATEMENT (`then panic(…)` arms need no value), or give at least one arm a `gives`
```

<!-- test: the-statement-form-the-diagnostic-names-compiles -->
The cure the message offers, exercised rather than asserted: the same all-diverging arms written as a
match STATEMENT. A statement arm yields no value and never needed one, so this compiles and runs — and
the arm that does NOT diverge is the one selected, proving the construct still dispatches normally.
```maxon
enum Colour
	red
	green
end 'Colour'

function main() returns ExitCode
	let c = Colour.green
	match c 'e'
		red then panic("a")
		green then print("green")
	end 'e'
	return 0
end 'main'
```
```stdout
green
```
```exitcode
0
```
