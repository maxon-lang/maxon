---
feature: match-payload-discard-bindings
status: experimental
keywords: [union, match, payload, discard, underscore]
category: type-system
---

# An ALL-DISCARD payload binding list (`value(_)`) is refused; a PARTIAL one is not

## Documentation

`specs/enum-full.md`'s `error.match-discarded-bindings` pins the single-payload half of this rule.
This file pins the **boundary**, which that spec never exercises, and the boundary is the whole
content of the rule:

> **E3081 fires if and only if the binding list is NON-EMPTY and EVERY binding is `_`** — because
> then the list is exactly equivalent to omitting it, which is the spelling the diagnostic recommends.

Three neighbours have to stay legal for that to be the rule rather than a blanket ban on `_`, and each
is a case below:

- **A PARTIAL discard is legal** (`two(_, b)`) — the list is not equivalent to omitting it: `b` is
  bound. This is the case that stops a later rung widening the check to "any `_` in a binding list".
- **A BARE case name is legal** (`value then …` on a payload-carrying case) — it is the canonical
  alternative the message names, so a compiler that refused it would be recommending a rejection.
- **The rule is about the LIST, not about the match FORM** — a `gives` (expression) arm and a `then`
  (statement) arm read the same binding list through the same routine, so both are refused.

Measured against the bootstrap oracle (2026-07-29), which is where the exact wording and anchor come
from: the message spells the list back as the user wrote it (`two(_, _)`, one `_` per slot, joined with
`, `) and is anchored on the ARM's case-name token, not on the offending `_`.

## Tests

<!-- test: error.all-discard-two-payloads -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Container
	empty
	two(a Integer, b Integer)
end 'Container'

function main() returns ExitCode
	let c = Container.two(1, b: 2)
	match c 'check'
		empty then return 1
		two(_, _) then return 0
	end 'check'
end 'main'
```
```maxoncstderr
error E3081: specs/fragments/match-payload-discard-bindings/error.all-discard-two-payloads.test:13:3: use 'two' instead of 'two(_, _)' to ignore associated values
```

<!-- test: error.all-discard-in-a-gives-arm -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	let c = Container.value(42)
	let r = match c 'check'
		empty gives 1
		value(_) gives 0
	end 'check'
	return r as ExitCode
end 'main'
```
```maxoncstderr
error E3081: specs/fragments/match-payload-discard-bindings/error.all-discard-in-a-gives-arm.test:13:3: use 'value' instead of 'value(_)' to ignore associated values
```

<!-- test: partial-discard-is-legal -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Container
	empty
	two(a Integer, b Integer)
end 'Container'

function main() returns ExitCode
	let c = Container.two(1, b: 2)
	match c 'check'
		empty then return 1
		two(_, b) then return b as ExitCode
	end 'check'
end 'main'
```
```exitcode
2
```

<!-- test: bare-case-name-ignores-the-payload -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	let c = Container.value(42)
	match c 'check'
		empty then return 1
		value then return 7
	end 'check'
end 'main'
```
```exitcode
7
```
