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

### Rank 2 — an immutable INSTANCE outranks an immutable FIELD

Writing `c.version = 2` needs two things to be mutable, and they are two different facts:

1. the **instance** — `let c` cannot be written through at all;
2. the **field** — `export let version` cannot be written, whatever `c` is.

Both report **E2013**, because they are one defect seen from two sides: a write to something
declared not to accept one. But when **both** are immutable, only one message can be
printed, and *which one* is a real decision rather than a tie.

**The instance wins.** The reason is that the other message would be actively wrong: told
`cannot assign to field 'Config.version' … (declare with 'var' to make it mutable)`, a reader
who changes `let version` to `var version` still has a program that does not compile, because
`let c` was the binding refusing the write. The instance message names the change that
actually fixes it.

The C# bootstrap agrees on rank 2 (measured), but there it is a **positional accident** —
`ResolveStructVariable(requireMutable: true)` happens to throw before `RequireMutableField`
runs, and nothing declares the precedence. In shv2 both ranks are deliberate, and both are
stated by nothing stronger than the ORDER OF CHECKS in `Parser.parseFieldAssignment`. That is
exactly the kind of rule the next edit reorders without noticing, which is why it is pinned
here rather than left to a comment: the ordering is not derivable, so it gets a CHECK.

Both cases are shv2-AUTHORED. `/specs` covers each half of rank 2 alone — `structs.md`'s
`error.let-struct-field-assign` (a `let` instance, a `var` field) and `error.let-field-assign`
(a `var` instance, a `let` field) — and never makes them fail together, so no ported case can
tell the two orderings apart. For rank 1 the corpus has no case at all: it never writes a
field on a non-struct through an immutable binding.

## Tests

<!-- test: immutable-instance-outranks-immutable-field -->
Both are immutable: `let c` AND `export let version`. Exactly one diagnostic is printed, and
it must be the INSTANCE's. Anchored at the root variable `c`, which is column 2 — the same
anchor both halves use, and never the field.

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
error E2013: <fragment>:15:2: cannot assign to immutable variable: 'c'
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
