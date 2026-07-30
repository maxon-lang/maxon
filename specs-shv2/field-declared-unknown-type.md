---
feature: field-declared-unknown-type
status: stable
keywords: field, type, resolution, E3011, declaration-site
category: type-system
---
# A Field Declared With An Undeclared Type Is E3011

## Documentation

A program names a type in exactly THREE places: a parameter, a return type, and a FIELD. The
first two reach `TypeResolution.resolveNamedType` — the one place that knows what a `named`
type denotes — and report `E3011` when no registry declares the name. The third did not.

`resolveTypes` walked `func.maxonReturnType` and `func.maxonParamTypes` and nothing else, so
`StructLayout.fieldTypes` was never read by the authority. A field declared `as Nonexistent`
therefore reached NO check at all, and `Parser.fieldStorageType`'s recovery value — `integer`,
handed back so the `loadIndirect` stays well-formed for the instant before the pipeline's error
gate throws — became the **final answer**: the program compiled clean and the field silently
typed itself `i64`.

That made `fieldStorageType`'s own comment false. It said the recovery was safe *because*
"E3011, which `TypeResolution` reports against the merged registry, the authority" would fire —
and nothing did. The comment described the right design; the implementation never had it. The
fix is the walk that makes the sentence true, not the deletion of the sentence.

⚠ **The check cannot live at the layout COMMIT point**, which is the obvious other home
(`ParseStaging.commitStructTypes`, where the layouts arrive in declaration order). `mergeArtifact`
folds one artifact at a time, so a field naming a type declared in a file folded LATER would
report a false E3011. The registry is only complete once every artifact is folded — which is
exactly where `resolveTypes` runs.

The diagnostic is UNPOSITIONED, identically to the parameter/return-type E3011 above it and for
that one's stated reason: nothing records a source span for a type REFERENCE (only ops and
parameter NAMES carry one). That is an accepted, documented limitation, not a new one.

### The one route the authority cannot reach — and what stands in for it there

`resolveTypes` reports the undeclared name only if the file PARSES. A **member access on a base
whose declared type is such a name** cannot parse: there is no `StructLayout` to resolve, so the
parser throws — and a thrown `ParseError` both stops the file before the pipeline's first pass and
discards that file's artifact diagnostics (`Parser.abortedParseArtifact`). So on this one route the
authority never speaks at all, and whatever the parser says IS the whole diagnosis.

What it said was **false**. All three member-access doors — a local binding
(`Parser.requireStructBase`, serving both the read and the write path), a CLOSURE CAPTURE
(`Parser.capturedStructBase`) and a VALUE receiver with no binding to name
(`Parser.structBaseOfReceiver`) — worded their refusal around `typeTagName(<the base's tag>)`, and a
`named` tag PRINTS AS `int` (that arm is correct for the ranged alias it was written for). So
`function takes(h NoSuchTypeAtAll)` + `return h.v` was reported as *"a field access on 'h', which is
declared 'int'"*: a statement about the source that the source does not make, pointing the reader at
a type the compiler had invented for its own recovery. Measured on all three doors, in three
different sentences. The runnable oracle answers the same program `Unknown type: NoSuchTypeAtAll`,
blaming the DECLARATION.

**The precedence rule now implemented.** A member-access refusal may not preempt the authority with
a type it invented. Before wording itself, each of the three doors asks
`TypeResolution.denotedNamedType` — the ONE cascade that knows what a `named` type denotes, the same
one the two `as`-cast sites and the generic-type-argument check ask — whether the base's declared
type name denotes anything:

* **it denotes something** (a declared `enum`/`union`, a ranged int alias, a qualified inner or
  per-instance alias, `ExitCode` and the other compiler-owned names, a declared `type` named from
  outside its own body) ⇒ the existing refusal stands, VERBATIM. A field access on a genuine `int`
  is a correct refusal with a fine message and it is pinned unchanged by
  `specs-shv2/struct-field-assign-precedence.md`'s `error.not-a-struct-outranks-immutable-instance`
  (a bare `let n = 5`) and `specs-shv2/self-field-struct-typed.md`'s
  `error.scalar-field-base-is-not-a-struct` (a field declared with a ranged alias, whose tag is
  `named` — the case that turns red if the new arm is widened to fire on every `named`).
* **it denotes nothing** ⇒ the refusal names the type **the source wrote** and says the DECLARATION
  is what has to change. One sentence for all three doors
  (`Parser.undeclaredBaseTypeMessage`), because it is one fact met at three spellings.

The query is asked only on a path that has ALREADY decided to refuse, so it can never reject a
program that compiles: it chooses the wording and nothing else. And it is a diagnostic query only —
the denoted type is discarded, never substituted for the base's, because the parser deliberately
keeps a `named` value's alias name (the shift rule and the per-instance identity checks read it)
where `resolveTypes` erases it.

⚠ **The CODE on this route is still E2015 and not E3011.** The meaning is E3011's, and the parser
already raises E3011 positioned at two other sites (both `as` casts, via
`ParseError.unknownCastTargetType`) — but that variant is named for the cast and the shared
`Queries.reportParseError` mapping is the only place a new arm could be rendered, so promoting this
route to E3011 is a separate change to that mapper. What is fixed here is the assertion, which was
the wrong ANSWER; the code is a register question with no wrong answer in it.

## Tests

<!-- test: field-unknown-type-read -->
```maxon

type Value
	export var n as Nonexistent

	static function create() returns Self
		return Value{n: 7}
	end 'create'
end 'Value'

function main() returns ExitCode
	let v = Value.create()
	return v.n
end 'main'
```
```maxoncstderr
error E3011: Unknown type 'Nonexistent'
```

<!-- test: field-unknown-type-unread -->
An undeclared field type is a DECLARATION error, so it is reported even when no code ever reads
the field. The walk is over the registry, not over the uses — which is what makes it a check on
the declaration rather than an accident of a load site being present.
```maxon

typealias Integer = int(i64.min to i64.max)

type Value
	export var n as Integer
	export var bad as Nonexistent

	static function create() returns Self
		return Value{n: 42, bad: 0}
	end 'create'
end 'Value'

function main() returns ExitCode
	let v = Value.create()
	return v.n
end 'main'
```
```maxoncstderr
error E3011: Unknown type 'Nonexistent'
```

<!-- test: param-unknown-type-member-access -->
The FIRST declaration site, met at a MEMBER ACCESS — the one route on which the authority above never
gets to speak, because the access is refused at PARSE time and a thrown `ParseError` stops the file
before `resolveTypes` runs. What the refusal may not do is invent a type: `h` is declared
`NoSuchTypeAtAll` and nothing else, so a message calling it `int` asserts something the source does
not say. It is the SAME undeclared-name fact the two cases above report, named by the name the source
wrote.
```maxon

typealias Small = int(0 to 1000)

type Holder
	export var v as Small

	export static function make(n Small) returns Holder
		return Self{v: n}
	end 'make'
end 'Holder'

function takes(h NoSuchTypeAtAll) returns Small
	return h.v
end 'takes'

function main() returns ExitCode
	return takes(Holder.make(7))
end 'main'
```
```maxoncstderr
error E2015: <fragment>:14:9: Unsupported: a field access on 'h', whose declared type 'NoSuchTypeAtAll' names no type this program declares — so it has no fields and no methods. The DECLARATION is the error, not this access: declare 'NoSuchTypeAtAll', or correct the name
```

<!-- test: param-unknown-type-captured-member-access -->
The same fact reached through a CLOSURE CAPTURE, which is its own door (`capturedStructBase`) and had
its own copy of the invented `int`. Fixing only the plain access would leave the false assertion
reachable through one `function(…) gives` — measured, before this case existed: *"a field access or
method call on the captured 'h', which is declared 'int' and not a struct type"*.
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function outer(h NoSuchTypeAtAll) returns Integer
	return apply(function(_ Integer) gives h.v, x: 0)
end 'outer'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:11:41: Unsupported: a field access or method call on the captured 'h', whose declared type 'NoSuchTypeAtAll' names no type this program declares — so it has no fields and no methods. The DECLARATION is the error, not this access: declare 'NoSuchTypeAtAll', or correct the name
```

<!-- test: param-unknown-type-method-call -->
And the THIRD door — a METHOD call, whose receiver is a VALUE with no binding to name
(`structBaseOfReceiver`). It carried the identical invented type in a differently-worded sentence
(*"a member access 'readIt' on a 'int' value"*), so a fix that stopped at the two field-access doors
would have left one of the three still asserting it. All three ask ONE authority
(`TypeResolution.denotedNamedType`) and share ONE sentence, so they cannot come to disagree about
what a name denotes.
```maxon

typealias Small = int(0 to 1000)

function takes(h NoSuchTypeAtAll) returns Small
	return h.readIt()
end 'takes'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:11: Unsupported: a member access 'readIt' on a value, whose declared type 'NoSuchTypeAtAll' names no type this program declares — so it has no fields and no methods. The DECLARATION is the error, not this access: declare 'NoSuchTypeAtAll', or correct the name
```
