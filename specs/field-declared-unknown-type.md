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
`integer` so the `loadIndirect` stays well-formed until the pipeline's error gate throws before
lowering; without a check on the field, that value would be the **final answer**: the program would
compile clean and the field would silently type itself `i64`. The recovery is safe *because* E3011,
reported by `TypeResolution` against the merged registry, fires first.

⚠ **The check cannot live at the layout COMMIT point**, which is the obvious other home
(`ParseStaging.commitStructTypes`, where the layouts arrive in declaration order). `mergeArtifact`
folds one artifact at a time, so a field naming a type declared in a file folded LATER would
report a false E3011. The registry is only complete once every artifact is folded — which is
exactly where `resolveTypes` runs.

The diagnostic is positioned at the type name in the field's declaration, as the parameter and
return-type E3011 above it are positioned at the type name in theirs.

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
anchors: a parameter, a return type and a field report it from `resolveTypes` at the type name; a body
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
error E3011: <fragment>:4:18: Unknown type 'Nonexistent'
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
error E3011: <fragment>:7:20: Unknown type 'Nonexistent'
```

<!-- test: param-unknown-type-member-access -->
The FIRST declaration site, met at a MEMBER ACCESS — the one route on which the authority above never
gets to speak first, because the access is refused at PARSE time, ahead of `resolveTypes`. What the refusal may not do is invent a type: `h` is declared
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
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:14:9: Unknown type 'NoSuchTypeAtAll'
error E3011: <fragment>:13:18: Unknown type 'NoSuchTypeAtAll'
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
error E3011: <fragment>:10:18: Unknown type 'NoSuchTypeAtAll'
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
error E3011: <fragment>:5:18: Unknown type 'NoSuchTypeAtAll'
```

<!-- test: param-unknown-type-unread -->
A parameter whose undeclared type no member access ever reaches parses, so `resolveTypes` reports it,
at the type name in the parameter's declaration.
```maxon

function take(n NoSuchTypeAtAll) returns ExitCode
	_ = n
	return 0
end 'take'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:3:17: Unknown type 'NoSuchTypeAtAll'
```

<!-- test: return-unknown-type -->
A return type naming no declared type is reported at the type name after `returns`.
```maxon

function make() returns NoSuchTypeAtAll
	return 0
end 'make'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:3:25: Unknown type 'NoSuchTypeAtAll'
```

<!-- test: tuple-element-unknown-type -->
A tuple type's element is a declaration type position too, so an unknown element type is reported at
its name inside the tuple.
```maxon

typealias Small = int(0 to 100)

function take(p (NoSuchElementType, Small)) returns ExitCode
	_ = p
	return 0
end 'take'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:5:18: Unknown type 'NoSuchElementType'
```

<!-- test: tuple-element-unknown-type-at-each-declaration -->
The same tuple type written in two declarations is reported at each one: a declaration's element
position belongs to that declaration, not to the first that spelled the tuple.
```maxon

typealias Small = int(0 to 100)

function take(p (NoSuchElementType, Small), q (NoSuchElementType, Small)) returns ExitCode
	_ = p
	_ = q
	return 0
end 'take'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:5:18: Unknown type 'NoSuchElementType'
error E3011: <fragment>:5:48: Unknown type 'NoSuchElementType'
```

<!-- test: tuple-alias-element-unknown-type -->
A tuple typealias is the declaration that writes the tuple, so its unknown element is reported inside the alias and not at a use of it.
```maxon

typealias Small = int(0 to 100)
typealias P = (NoSuchElementType, Small)

function take(p P) returns ExitCode
	_ = p
	return 0
end 'take'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:4:16: Unknown type 'NoSuchElementType'
```

<!-- test: nested-tuple-element-unknown-type -->
A tuple inside a tuple reports its own unknown element; the outer element, the inner tuple, resolves.
```maxon

typealias Small = int(0 to 100)

function take(p ((NoSuchElementType, Small), Small)) returns ExitCode
	_ = p
	return 0
end 'take'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:5:19: Unknown type 'NoSuchElementType'
```

<!-- test: tuple-element-unknown-type-in-a-field-and-a-parameter -->
The same tuple spelled by a field and by a parameter is reported at each spelling.
```maxon

typealias Small = int(0 to 100)

type Holder
	export let pair as (NoSuchElementType, Small)
end 'Holder'

function take(p (NoSuchElementType, Small)) returns ExitCode
	_ = p
	return 0
end 'take'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:6:22: Unknown type 'NoSuchElementType'
error E3011: <fragment>:9:18: Unknown type 'NoSuchElementType'
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
	var seen = 0 as HitCount
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
