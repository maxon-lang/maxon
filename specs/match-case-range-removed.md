---
feature: match-case-range-removed
status: experimental
keywords: [match, enum, union, or, alternative, range, refusal, line]
category: control-flow
---

# Match Arms Name Their Cases

## Documentation

An arm that covers several enum or union cases names each of them, joined by `or`, **one
alternative per line**. The chain continues onto the next line after a trailing `or`; the last
alternative carries the arm's `then` or `gives` and its body.

```text
match op 'dispatch'
	add or
		sub or
		mul then compute()
	halt then stop()
end 'dispatch'
```

There is no case RANGE. `add to mul` is **E3146**, and so is `add upto mul`.

A range covered the cases whose DECLARATION POSITION fell inside its span, so a case added inside
that span was absorbed silently and exhaustiveness never fired — the arm handled a case nobody had
decided about. Naming each case is what makes E2026 point at every match site when an enum grows.

The refusal is raised as soon as `to`/`upto` is seen, **before** the upper bound is resolved, so
`add to nope` reports E3146 rather than E3034: the range is the mistake whichever name follows it.

Packing two alternatives onto one line is **E3147**. One per line keeps an arm readable when it
covers twenty cases, and makes adding or removing one a one-line diff.

The continuation is **trailing-only** — a newline is admitted only after an `or` already consumed,
never before one. A leading `or` would be ambiguous: `or` is itself a legal case name, so a line
beginning with `or` reads equally as this chain continuing and as a fresh arm whose first
alternative is the case named `or`.

Scalar range patterns (`1 to 10`, `'a' to 'z'`, `min upto 0`) are unaffected: those are values in an
ordered domain, where a new value cannot appear between the bounds behind the author's back.

## Tests

<!-- test: or-chain-wraps -->
A wrapped chain selects every one of its alternatives, in both the expression form (`gives`) and
the statement form (`then`), and whether or not the arm is the first.
```maxon
typealias Code = int(0 to 125)

union Op
	add
	sub
	mul
	halt
end 'Op'

function scored(op Op) returns Code
	let v = match op 'op'
		add or
			sub or
			mul gives 1
		halt gives 9
	end 'op'
	return v
end 'scored'

function labelled(op Op) returns Code
	match op 'op'
		add or
			halt then return 5
		sub or
			mul then return 6
	end 'op'
end 'labelled'

function main() returns ExitCode
	print("{scored(Op.add)}{scored(Op.sub)}{scored(Op.mul)}{scored(Op.halt)}")
	print("{labelled(Op.add)}{labelled(Op.sub)}{labelled(Op.mul)}{labelled(Op.halt)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11195665
```

<!-- test: chain-covers-every-case-for-exhaustiveness -->
Every alternative counts toward exhaustiveness, so a chain naming every case needs no `default`.
```maxon
typealias Code = int(0 to 125)

enum Color
	red
	green
	blue
end 'Color'

function pick(c Color) returns Code
	match c 'c'
		red or
			green or
			blue then return 4
	end 'c'
end 'pick'

function main() returns ExitCode
	return pick(Color.blue)
end 'main'
```
```exitcode
4
```

<!-- test: error.case-added-mid-span-is-named -->
⭐ **THE ARGUMENT FOR THE WHOLE RULE, AS A TEST.** `Op` once read `add sub mul halt`, and an arm
covering the first three was spelled `add to mul`. `fma` is then added BETWEEN `sub` and `mul` — the
mid-span insert. A range would have absorbed it in silence: its endpoints still say `add` and `mul`,
the arm still compiles, and `fma` is handled by whatever that arm happened to do. Named cases cannot:
`fma` is in no arm, so the match is not exhaustive and the compiler says which case nobody decided
about.
```maxon
typealias Code = int(0 to 125)

union Op
	add
	sub
	fma
	mul
	halt
end 'Op'

function classify(op Op) returns Code
	match op 'op'
		add or
			sub or
			mul then return 1
		halt then return 2
	end 'op'
end 'classify'

function main() returns ExitCode
	return classify(Op.add)
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/match-case-range-removed/error.case-added-mid-span-is-named.test:18:2: match on union 'Op' is not exhaustive, missing: fma
```

<!-- test: error.enum-case-range -->
A case range over a union is refused, quoting the range as the source spells it.
```maxon
typealias Code = int(0 to 125)

union Op
	add
	sub
	mul
end 'Op'

function pick(op Op) returns Code
	match op 'op'
		add to mul then return 1
	end 'op'
end 'pick'

function main() returns ExitCode
	return pick(Op.add)
end 'main'
```
```maxoncstderr
error E3146: specs/fragments/match-case-range-removed/error.enum-case-range.test:12:3: 'add to mul' is not a match pattern: name each case in an 'or'-chain, one per line
```

<!-- test: error.enum-case-range-upto -->
`upto` is refused for the same reason and by the same code.
```maxon
typealias Code = int(0 to 125)

enum Color
	red
	green
	blue
end 'Color'

function pick(c Color) returns Code
	match c 'c'
		red upto blue then return 1
		blue then return 2
	end 'c'
end 'pick'

function main() returns ExitCode
	return pick(Color.red)
end 'main'
```
```maxoncstderr
error E3146: specs/fragments/match-case-range-removed/error.enum-case-range-upto.test:12:3: 'red upto blue' is not a match pattern: name each case in an 'or'-chain, one per line
```

<!-- test: error.enum-case-range-unknown-upper -->
The range is refused BEFORE its upper bound is resolved, so an upper naming no case is still
E3146 and not E3034. Pinning the order keeps the two compilers from disagreeing about which
mistake a reader is told about first.
```maxon
typealias Code = int(0 to 125)

enum Color
	red
	green
	blue
end 'Color'

function pick(c Color) returns Code
	match c 'c'
		red to nope then return 1
	end 'c'
end 'pick'

function main() returns ExitCode
	return pick(Color.red)
end 'main'
```
```maxoncstderr
error E3146: specs/fragments/match-case-range-removed/error.enum-case-range-unknown-upper.test:12:3: 'red to nope' is not a match pattern: name each case in an 'or'-chain, one per line
```

<!-- test: error.packed-or-chain -->
Two alternatives on one line is refused, positioned at the token standing where the newline
belongs.
```maxon
typealias Code = int(0 to 125)

enum Color
	red
	green
	blue
end 'Color'

function pick(c Color) returns Code
	match c 'c'
		red or green then return 1
		blue then return 2
	end 'c'
end 'pick'

function main() returns ExitCode
	return pick(Color.red)
end 'main'
```
```maxoncstderr
error E3147: specs/fragments/match-case-range-removed/error.packed-or-chain.test:12:10: each alternative of a match arm's 'or'-chain needs its own line: break the line after 'or'
```

<!-- test: error.leading-or -->
The continuation is trailing-only: a line may not BEGIN with `or`. Refused rather than accepted,
because `or` is a legal case name and the two readings of such a line are both complete arms.
```maxon
typealias Code = int(0 to 125)

enum Color
	red
	green
	blue
end 'Color'

function pick(c Color) returns Code
	match c 'c'
		red
			or green then return 1
		blue then return 2
	end 'c'
end 'pick'

function main() returns ExitCode
	return pick(Color.red)
end 'main'
```
```maxoncstderr
error E2010: specs/fragments/match-case-range-removed/error.leading-or.test:12:6: Expected 'then' but got 'newline'
```
