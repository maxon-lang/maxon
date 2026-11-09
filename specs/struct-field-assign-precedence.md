---
feature: struct-field-assign-precedence
status: stable
keywords: struct, field, assignment, mutability, immutable, diagnostic
category: semantics
---
# Which Diagnostic A Refused Field Write Prints

## Documentation

A field write can be refused for several reasons at once, and only one message is printed.
Which one is a rule, and this file pins it. It has two ranks.

### Rank 1 — RESOLUTION outranks PERMISSION

`p.x = 1` asks two kinds of question. **Resolution**: is `p` a struct, and does it have a
field `x`? **Permission**: may this write happen?

**Resolution wins**, and the reason is that a permission message can only ever advise
"declare it `var`" — so answering a write that does not resolve names a change that does not
work. `let n = 5` then `n.x = 1` reporting *"cannot assign to immutable variable: 'n'"* sends
the reader to write `var n`, which changes nothing: an `int` has no fields either way.

The field READ path (`parseFieldAccess`) has always reported the not-a-struct error for those
same three tokens, because it has no permission question to ask. So this rank is also what
makes a READ and a WRITE agree about what `p.x` denotes.

### Rank 2 — a `let` FIELD is refused whatever the instance binding is

Writing `c.version = 2` asks two questions, and a `let` answers only one of them:

1. the **instance** — `let c` forbids reassigning `c`, not writing through it, so it never refuses a field write;
2. the **field** — `export let version` cannot be written, whatever `c` is.

A write to a `let` field is **E2013** (`'version' is a let field and cannot be reassigned`) through a `var` instance
and through a `let` instance alike: the binding of the instance has no say, so only one message exists and it names
the field, which is the change that fixes the program (`var version`).

Rank 1 is stated by nothing stronger than the ORDER OF CHECKS in `Parser.parseFieldAssignment`. That is
exactly the kind of rule the next edit reorders without noticing, which is why it is pinned
here rather than left to a comment: the ordering is not derivable, so it gets a CHECK.

`structs.md` covers the write through a `let` instance (`let-struct-field-assign`, accepted) and the write to a `let`
field of a `var` instance (`error.let-field-assign`, E2013); the case below makes them meet. Rank 1 needs a write to a
field on a non-struct through an immutable binding, which only this file writes.

## Tests

<!-- test: a-let-field-is-refused-through-a-let-instance -->
Both are `let`: `let c` AND `export let version`. Exactly one diagnostic is printed, and
it is the FIELD's, anchored at the root variable `c`, which is column 2 — the same anchor a
write through a `var` instance uses.

```maxon

typealias Integer = int(i64.min to i64.max)

type Config
	export let version as Integer

	static function create(version Integer) returns Self
		return Self{version: version}
	end 'create'
end 'Config'

function main() returns ExitCode
	let c = Config.create(1)
	c.version = 2
	return c.version
end 'main'
```
```maxoncstderr
error E2013: <fragment>:15:2: 'version' is a let field and cannot be reassigned
```

<!-- test: error.not-a-struct-outranks-immutable-instance -->
Rank 1. `n` is BOTH immutable AND not a struct, so both bands have something to say. The
resolution message must win: `var n` — the only change the permission message could ask for —
leaves `n.x = 1` just as broken, because an `int` has no field `x` however it is declared.

This is the same message, at the same anchor, that a field READ of `n.x` reports. Sabotage it
by moving the mutability check back above `requireStructBinding` and this case goes red while
every other case in this file stays green — which is what makes it a gate on the rank rather
than on the message.

```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let n = 5
	n.x = 1
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:2: Unsupported: a field access on 'n', which is declared 'int' and not a struct type (only a struct has fields)
```
