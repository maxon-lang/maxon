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
type denotes — and report `E3011` when no registry declares the name. The third reaches it through
`TypeResolution.checkStructFieldTypes`, which `resolveTypes` runs over every `StructLayout.fieldTypes`.

That walk is what makes `Parser.fieldStorageType`'s recovery value safe. The recovery hands back
`integer` so the `loadIndirect` stays well-formed for the instant before the pipeline's error gate
throws; without a check on the field, that value would be the **final answer**: the program would
compile clean and the field would silently type itself `i64`. The recovery is safe *because* E3011,
reported by `TypeResolution` against the merged registry, fires first.

⚠ **The check cannot live at the layout COMMIT point**, which is the obvious other home
(`ParseStaging.commitStructTypes`, where the layouts arrive in declaration order). `mergeArtifact`
folds one artifact at a time, so a field naming a type declared in a file folded LATER would
report a false E3011. The registry is only complete once every artifact is folded — which is
exactly where `resolveTypes` runs.

The diagnostic is UNPOSITIONED, identically to the parameter/return-type E3011 above it and for
that one's stated reason: nothing records a source span for a type REFERENCE (only ops and
parameter NAMES carry one). That is an accepted, documented limitation.

### The one route the authority cannot reach — and what stands in for it there

`resolveTypes` reports the undeclared name only if the file PARSES. A **member access on a base
whose declared type is such a name** cannot parse: there is no `StructLayout` to resolve, so the
parser throws — and a thrown `ParseError` both stops the file before the pipeline's first pass and
discards that file's artifact diagnostics (`Parser.abortedParseArtifact`). So on this one route the
authority never speaks at all, and whatever the parser says IS the whole diagnosis.

There are three member-access doors — a local binding (`Parser.requireStructBase`, serving both the
read and the write path), a CLOSURE CAPTURE (`Parser.capturedStructBase`) and a VALUE receiver with no
binding to name (`Parser.structBaseOfReceiver`). Each words its ordinary refusal around
`typeTagName(<the base's tag>)`, and a `named` tag PRINTS AS `int` (that arm is correct for the ranged
alias it was written for). Worded that way, `function takes(h NoSuchTypeAtAll)` + `return h.v` would be
reported as *"a field access on 'h', which is declared 'int'"*: a statement about the source that the
source does not make, pointing the reader at a type the compiler invented for its own recovery. What is
wrong is the DECLARATION: `NoSuchTypeAtAll` names no type.

**The precedence rule.** A member-access refusal may not preempt the authority with
a type it invented. Before wording itself, each of the three doors asks
`TypeResolution.denotedNamedType` — the ONE cascade that knows what a `named` type denotes, the same
one the two `as`-cast sites and the generic-type-argument check ask — whether the base's declared
type name denotes anything:

* **it denotes something** (a declared `enum`/`union`, a ranged int alias, a qualified inner or
  per-instance alias, a declared `type` named from outside its own body, and five of the six
  compiler-owned names — `ExitCode`, `HashValue`, `Codepoint`, `Ordering`, `CharSet`) ⇒ the existing
  refusal stands, VERBATIM. A field access on a genuine `int`
  is a correct refusal with a fine message and it is pinned unchanged by
  `specs/struct-field-assign-precedence.md`'s `error.not-a-struct-outranks-immutable-instance`
  (a bare `let n = 5`) and `specs/self-field-struct-typed.md`'s
  `error.scalar-field-base-is-not-a-struct` (a field declared with a ranged alias, whose tag is
  `named`). ⭐ **That second one is the whole corpus's coverage of this branch:** widen the query to
  fire on every `named` and it reports
  `E3011: Unknown type 'Integer'` about a perfectly declared alias — and it is the ONLY case
  that goes red, so it is the single thing standing between this arm and a new false rejection.
* **it is the SIXTH compiler-owned name, `CharacterSet`** ⇒ the existing refusal also stands.
  `stdlib/CharacterSet.maxon` is listed, the corpus layout is filed
  under the BARE name, and the cascade answers it like any other declared struct.
  `isCompilerOwnedTypeName` remains the gate for the names that have no cascade arm.
* **it denotes nothing** ⇒ the door reports **E3011 with `unknownTypeMessage`** — the authority's own
  code and the authority's own words — positioned at the base (or, for a method call, the member).
  Not a sentence of its own: `ParseError.unknownTypeName`, the one arm every positioned undeclared
  type name in the compiler is rendered through. One `try`, not three copies of an ask-then-throw:
  `Parser.requireDeclaredBaseTypeName` throws internally, so no door holds a "nothing to report"
  value and none of them can word this fact for itself.

The query is asked only on a path that has ALREADY decided to refuse, so it can never reject a
program that compiles: it chooses the verdict and nothing else. And it is a diagnostic query only —
the denoted type is discarded, never substituted for the base's, because the parser deliberately
keeps a `named` value's alias name (the shift rule and the per-instance identity checks read it)
where `resolveTypes` erases it.

### The code is E3011

⚠ **A BETTER SENTENCE UNDER THE WRONG CODE IS STILL A SECOND HOME FOR ONE FACT.** A door that kept
its *"a field access on 'h', …"* framing and merely put the real name in place of the invented type
would remove the false assertion — but it would leave `E2015 ParserUnsupportedFeature` (*"a construct
this compiler does not implement yet"*, which is not what is wrong with the program) carrying a fact
`E3011 SemanticUnknownType` is registered for, in a second wording, beside the one
`TypeResolution.unknownTypeMessage` exists to make unique. The registry keys a code to a MEANING, so
the register is not cosmetic.

**So the doors raise the arm the two `as`-cast sites raise**, **`ParseError.unknownTypeName`**. Its
payload is not cast-specific — a type name and a position.
One authority (`denotedNamedType`), one code (E3011), one text (`unknownTypeMessage`), and SIX
anchors: a parameter, a return type and a field report it unpositioned from `resolveTypes`; a body
`as` cast and a top-level `let`'s cast report it at the `as`; a generic type argument reports it at
the argument; and these three member-access doors report it at the base. The cast diagnostics are
pinned by `specs/cast-target-type-resolution.md`, all four of them.

**Why a parse-time door reports a code the authority also reports, and why that is not a second
producer.** It supplies neither the predicate nor the words. What it supplies is a report on a route
the authority cannot reach — the `abortedParseArtifact` discard above. On every route where the parse
SUCCEEDS, `resolveTypes` is still the only reporter.

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

	static function make(n Small) returns Holder
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
error E3011: <fragment>:14:9: Unknown type 'NoSuchTypeAtAll'
```

<!-- test: param-unknown-type-captured-member-access -->
The same fact reached through a CLOSURE CAPTURE, which is its own door (`capturedStructBase`) with its
own wording of the refusal (*"a field access or method call on the captured 'h', which is declared 'int'
and not a struct type"*). A check on only the plain access would leave the false assertion reachable
through one `function(…) gives`.
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
error E3011: <fragment>:11:41: Unknown type 'NoSuchTypeAtAll'
```

<!-- test: param-unknown-type-method-call -->
And the THIRD door — a METHOD call, whose receiver is a VALUE with no binding to name
(`structBaseOfReceiver`). Its refusal is worded differently again (*"a member access 'readIt' on a
'int' value"*), so a check at only the two field-access doors would leave one of the three asserting
the invented type. All three ask ONE authority
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
error E3011: <fragment>:6:11: Unknown type 'NoSuchTypeAtAll'
```

<!-- test: compiler-reserved-base-type-is-nameable-at-a-parameter -->
⭐⭐ **A NAME THE COMPILER RESERVES IS NOT AN UNDECLARED NAME — AND IT IS NOT AN UNNAMEABLE ONE
EITHER.** `CharacterSet` is reserved: `isCompilerOwnedTypeName`, keyed by `CharacterSetBuiltinName`, stops a
USER DECLARATION binding the name. The reservation does not stop a source NAMING the type —
`stdlib/String.maxon` names `CharacterSet` at its `trim`/`trimStart`/`trimEnd` parameters and two private
scans, and a user parameter may name it the same way.

⇒ ONE layout, reachable under the name the corpus writes: `stdlib/CharacterSet.maxon` is listed and the
CORPUS's layout is the only one under the bare name, so the *"one concept has two layouts under two keys"*
hazard `SignatureIndex.recordStruct`'s header names cannot arise, and the reservation does the one job it
is for — refusing a USER declaration of the name. What the case below pins is that the name is NAMEABLE at
a parameter, not which layout answers it.

⚠ The OTHER half of `undeclaredBaseTypeNameOf` keeps its own witness — widen the
`denotedNamedType` ask to fire on every `named` and `self-field-struct-typed`'s
`error.scalar-field-base-is-not-a-struct` still reddens.
```maxon

typealias HitCount = int(0 to u64.max)

function countIn(s String, chars CharacterSet) returns HitCount
	var seen = 0
	for c in s 'scan'
		if chars.contains(c) 'hit'
			seen = seen + 1
		end 'hit'
	end 'scan'
	return seen
end 'countIn'

function main() returns ExitCode
	return countIn("a b c", chars: CharacterSet.whitespaces()) as ExitCode
end 'main'
```
```exitcode
2
```
