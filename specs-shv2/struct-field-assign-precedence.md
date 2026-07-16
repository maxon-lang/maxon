---
feature: struct-field-assign-precedence
status: stable
keywords: struct, field, assignment, mutability, immutable, diagnostic
category: semantics
---
# An Immutable INSTANCE Outranks An Immutable FIELD

## Documentation

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

The C# bootstrap agrees (measured), but there it is a **positional accident** —
`ResolveStructVariable(requireMutable: true)` happens to throw before `RequireMutableField`
runs, and nothing declares the precedence. In shv2 it is deliberate, and it is stated by the
order of two checks in `Parser.parseFieldAssignment`. That is exactly the kind of rule the
next edit reorders without noticing, so it is pinned here rather than left to a comment.

This case is shv2-AUTHORED. `/specs` covers each half alone — `structs.md`'s
`error.let-struct-field-assign` (a `let` instance, a `var` field) and `error.let-field-assign`
(a `var` instance, a `let` field) — and never makes them fail together, so no ported case can
tell the two orderings apart.

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
