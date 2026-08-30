---
feature: function-overloads
status: stable
keywords: [function, overload, disambiguation, parameter, types]
category: functions
---

## Documentation

### Function Overloads

Maxon supports function overloading — multiple functions with the same name but different signatures.

#### Disambiguation by parameter types

When overloads differ in their parameter types, the compiler automatically selects the correct overload based on the argument types at the call site:

```text
function process(value int) returns int
  return value * 2
end 'process'

function process(value String) returns int
  return value.count()
end 'process'

process(42)        // calls process(value int)
process("hello")   // calls process(value String)
```

#### Disambiguation by parameter names

When overloads have different parameter names, the caller uses named arguments to select the correct overload:

```text
function create(name String) returns String
  return name
end 'create'

function create(label String) returns String
  return label
end 'create'

create("foo")    // calls first overload
create("bar")   // calls second overload
```

#### Ambiguous calls

If the compiler cannot determine which overload to call based on argument types alone, it requires named arguments. Calling an ambiguous overload without named arguments is a compile error.

#### Each overload carries its own parameter defaults

Every member of an overload set may give a parameter a default value, and a call that omits
the argument gets **the default belonging to the member the call resolves to** — not the one
belonging to whichever declaration the compiler read last.

```text
function pad(value Integer, with String = "0") returns String
  return with
end 'pad'

function pad(value bool, with String = "-") returns String
  return with
end 'pad'

pad(1)      // "0" — the Integer member's default
pad(true)   // "-" — the bool member's default
```

The members have to agree on the **shape** of their defaults, because the argument list is
built while the call is parsed and the overload is picked a whole pass later: the same
call-aligned parameter names, and at every position the same answer to "does this parameter
default, and is it produced by a synthesized helper or by the caller's own location?".
What they may differ in is the only thing the parse does not have to commit to — the default
**expression**, which is a function body the resolved member names. A set whose members
disagree about the shape is refused at the declaration, with the position of the `=`.

#### An overload set may `throws`, when every member throws the same error

A `throws` clause is published by the whole-program declaration sweep under the name the source
wrote, and a `try` is desugared while the call is **parsed** — a whole pass before the overload is
resolved. So the clause the `try` reads has to be every member's, and when the members agree it is:
one error type, recovered whichever member the call turns out to name.

```text
static function want(actual Num, expected Num) returns Num throws Boom
static function want(actual bool, expected bool) returns Num throws Boom

try Chk.want(1, expected: 1)      // recovers Boom
try Chk.want(true, expected: true) // recovers Boom
```

Members that **disagree** are refused at the second declaration. There are two ways to disagree and
both are unrepairable at a call site: naming two different error types (the `(e)` binding would be
typed from whichever declaration the by-name sweep recorded last), and one member throwing where
another does not (the call would be compiled with or without an error flag, and `try` on a call that
cannot throw is itself an error).

A `static` member beside an instance member of one name is **not** an overload set — the two are told
apart at the call by syntax, and each is registered under a key of its own — and the sweep files each
member's `throws` clause under that member's key, so a `try` at either call recovers that member's own
error type. (Such a pair was refused whenever either member threw, for as long as the sweep published one
clause under the one name they share.)

A **free function** whose bare name is also declared in another directory is registered under its
directory-qualified name (`alpha.want`), and the sweep files that declaration's facts — and the tallies
that say whether its declarations agree — under the same key. So such an overload set is judged on its
declarations exactly as a root-level one is: agreeing members compile, disagreeing ones are refused. (It
was refused whatever the members said until the tallies were keyed that way, because the verdict was kept
under the bare name, where a second directory's declarations are counted too.)

##### ⚠ Every refusal above is **narrower than the language**, and none of them is canon

The paragraphs above describe what **shv2** can compile, not what the language permits. shv2
decides the ABI of a `try` while the call is **parsed**, from a whole-program entry keyed by the name the
source wrote; the oracle carries its throws facts **per declaration** and so has nothing to be unable to
tell apart. Every one of these programs is a conservative refusal awaiting per-member facts in the sweep,
and a later reader must not read them as rules. MEASURED against the bootstrap, on the very programs the
cases below refuse:

| shv2 refuses | the oracle |
|---|---|
| two members naming two error types (`Boom` beside `Splat`) | accepts, answers **14** |
| a throwing member beside a non-throwing one | accepts, answers **14** |

The `static`/instance pair is the one place the two compilers disagree about the SHAPE and not merely about
what shv2 can attribute: the oracle treats such a pair as **one overload set**, so when the two members'
parameter types cannot tell them apart it reports `E3007: Ambiguous overload for 'T.m'` at the call, where
shv2 separates them by registration key (`same-name-methods.md`) and compiles. The pair cases below are
therefore programs **shv2 accepts and the oracle refuses** — the opposite direction from the table above,
and a difference of rule rather than of precision.

#### The oracle's non-injective overload name is deliberately not ported

The canonical suite's `error.overload-pair-compiling-to-one-name` pins a **refusal**: the
bootstrap builds an overload's disambiguating name by joining `{parameter}_{type}` parts with
`_`, and `_` is legal inside both a parameter name and a type name, so the join is not
injective — `f(x_i64_y P, w R)` and `f(x i64_y_P, w R)` compile to one name and the bootstrap
reports `E3006`.

shv2 **compiles that program and prints the correct `3 34`**, both overloads live, each
answering for its own argument types. Its overload identity does not go through that join at
all: a member's registration name is claimed against the file's own set of already-claimed
names and counted past on a contest, so uniqueness is established by construction rather than
by an injectivity argument (`Parser.overloadRegistrationNameFor`). Porting the canonical case
would therefore demand a regression — a refusal of a program this compiler handles — so it is
not ported, and this paragraph is the record of that decision rather than a silent gap.

## Tests

<!-- test: basic-type-disambiguation -->
```maxon
typealias Integer = int(i64.min to i64.max)

function process(value Integer) returns Integer
	return value * 2
end 'process'

function process(value String) returns Integer
	return value.count()
end 'process'

function main() returns ExitCode
	return process(21)
end 'main'
```
```exitcode
42
```

<!-- test: basic-type-disambiguation-string -->
```maxon
typealias Integer = int(i64.min to i64.max)

function process(value Integer) returns Integer
	return value * 2
end 'process'

function process(value String) returns Integer
	return value.count()
end 'process'

function main() returns ExitCode
	return process("hello world hello world hello world hello worl!")
end 'main'
```
```exitcode
47
```

<!-- test: name-disambiguation-preserved -->
```maxon
typealias Integer = int(i64.min to i64.max)

function slice(start Integer, endIndex Integer) returns Integer
	return endIndex - start
end 'slice'

function slice(start Integer, length Integer) returns Integer
	return start + length
end 'slice'

function main() returns ExitCode
	return slice(10, length: 32)
end 'main'
```
```exitcode
42
```

<!-- test: error.ambiguous-same-signature -->
```maxon
typealias Integer = int(i64.min to i64.max)

function create(name String) returns Integer
	return name.count()
end 'create'

function create(label String) returns Integer
	return label.count()
end 'create'

function main() returns ExitCode
	return create("hello")
end 'main'
```
```maxoncstderr
error E3007: specs/fragments/function-overloads/error.ambiguous-same-signature.test:13:9: Ambiguous overload for 'create': multiple overloads match. Candidates: (name String), (label String)
```

<!-- test: a-void-overload-beside-a-value-one-resolves-by-argument -->
<!-- W49 wave 4 REFUSED THIS PROGRAM AND IT IS NOW THE RIGHT ANSWER. What the refusal was about is worth
keeping: the declaration sweep recorded ONE return type per NAME, LAST-WINS, so it recorded the `String` of
the second declaration; `mintCallResult` typed EVERY call to `emit` as a String and `enrolOwnedCallTemp`
enrolled the scope-exit drop a managed result owes. This call resolves to the VOID member a whole pass later,
so the drop was spent on a register the callee never wrote. MEASURED before the refusal existed: this exact
program compiled clean and died with an ACCESS VIOLATION (0xC0000005).

WHAT MAKES THAT HAZARD STRUCTURALLY IMPOSSIBLE IS NOT A REFUSAL BUT THE SWEEP: it now publishes each
declaration's PARAMETER TYPES beside its return type (`ProgramSignatures.OverloadedDecl`), so `emit(true)` is
typed from the member a `bool` argument means -- the void one -- and there is no managed result for a drop to
be enrolled against at all. The bug was never "these overloads disagree"; it was one return type per NAME.

Both `exitcode` AND `stdout` are pinned: an unpinned exit code is never checked, so a stdout-only case would
pass while leaking. MEASURED against the bootstrap on the ranged-alias spelling of this program
(`emit(tag Integer)`, which is what its bare `int` has to become there): `flag true`, exit 0. -->
```maxon
function emit(flag bool)
	print("flag {flag}\n")
end 'emit'

function emit(tag Integer) returns String
	return "tag{tag}"
end 'emit'

function main() returns ExitCode
	emit(true)
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
flag true
```

<!-- test: a-value-overload-beside-a-void-one-declared-last-resolves-by-argument -->
<!-- The MIRROR of the case above, and the NEGATIVE CONTROL for it -- the direction whose failure was a LEAK
rather than a crash. The sweep recorded the void member (it is declared last), so the call to the VALUE member
was typed `void`, no drop was enrolled, and the `+1` the `String` member returns would have leaked. Pinned so
that the pair is one guarded fact rather than one guarded half, and the `exitcode` block is what catches it:
a leaked record makes the process exit 101.

THE CALL USES ITS RESULT, AND THAT IS THE ORACLE'S REQUIREMENT RATHER THAN THIS RULE'S. shv2 compiles the
bare `emit(1)` statement and exits 0 with no leak; the bootstrap refuses it as `E3064: result of pure
function 'emit$tag' must be used`, and it refuses the same program with `emit` declared ONCE -- so that
divergence is about discarded pure results and not about overloads. Reading the result keeps both compilers
on one program. MEASURED against the bootstrap on the ranged-alias spelling: `tag1`, exit 0. -->
```maxon
function emit(tag Integer) returns String
	return "tag{tag}"
end 'emit'

function emit(flag bool)
	print("flag {flag}\n")
end 'emit'

function main() returns ExitCode
	print("{emit(1)}\n")
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
tag1
```

<!-- test: error.overload-redeclared-with-the-same-parameters -->
<!-- A REDECLARED OVERLOAD IS REFUSED BY NAME, AND THE NAME E3006 QUOTES IS A MINTED ONE — so the diagnostic
has to say what the author actually wrote. `pick(x Integer)` takes the bare name; `pick(x bool)` is minted
`pick#bool`; the second `pick(x bool)` matches an already-registered signature, so
`Parser.overloadMemberHoldingSignature` hands it the INCUMBENT'S name and the two land on one key. MEASURED
before this pin, and the reason it exists: the refusal read `Duplicate function 'pick#bool'` — the canonical
plain sentence, whose slot `duplicate-functions.md` fills only with names a declaration wrote, quoting a
symbol that appears nowhere in the program. The sort in `ParseStaging.duplicateFunctionMessage` was an
ENUMERATION of the two contested kinds and this kind is neither, so it fell through to the one sentence that
promises the name is greppable. It now tests the PROPERTY — is this the written name? — and a minted name
nobody classifies lands here.

⚠ The bootstrap does NOT refuse this program at all; it compiles it, reporting only the unused-variable
warnings. shv2 is deliberately stricter — a duplicate is a duplicate — so there is no oracle to match on the
wording, only the canonical rule about whose names may fill that slot. -->
```maxon
typealias Integer = int(i64.min to i64.max)

function pick(x Integer) returns Integer
	return x
end 'pick'

function pick(flag bool) returns Integer
	return 1 if flag else 0
end 'pick'

function pick(flag bool) returns Integer
	return 2 if flag else 0
end 'pick'

function main() returns ExitCode
	return pick(3) as ExitCode
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/function-overloads/error.overload-redeclared-with-the-same-parameters.test:12:10: duplicate definition of function 'pick#bool' — 'pick' has more than one declaration in this program, so every one of those declarations is registered under its parameter-type spelling, and two of them spell the same parameters. Give the overloads distinct parameter types, or distinct names
```

<!-- test: method-type-disambiguation -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Converter
	static function create() returns Self
		return Self{}
	end 'create'

	function convert(value Integer) returns Integer
		return value * 2
	end 'convert'

	function convert(value String) returns Integer
		return value.count()
	end 'convert'
end 'Converter'

function main() returns ExitCode
	var c = Converter.create()
	return c.convert(21)
end 'main'
```
```exitcode
42
```

<!-- test: variable-type-inference -->
```maxon
typealias Integer = int(i64.min to i64.max)

function process(value Integer) returns Integer
	return value * 2
end 'process'

function process(value String) returns Integer
	return value.count()
end 'process'

function main() returns ExitCode
	let x = 21
	return process(x)
end 'main'
```
```exitcode
42
```

<!-- test: string-contains-char -->
<!-- W49 wave 4 UNLOCKED THIS. It was disabled because `String.contains` was a SYNTHESIZED arm that served
the `String` form only, so `text.contains('e')` was `E3005: 'contains' requires a String`. Retiring the
member onto `stdlib/String.maxon:439,446` makes it an ordinary DECLARED overload set, which is exactly what
`resolveOverloadedCalls` has always been able to pick from. -->
```maxon
function main() returns ExitCode
	let text = "hello"
	if text.contains('e') 'check'
		return 1
	end 'check' else 'other'
		return 0
	end 'other'
end 'main'
```
```exitcode
1
```

<!-- test: string-contains-string -->
```maxon
function main() returns ExitCode
	let text = "hello world"
	if text.contains("world") 'check'
		return 1
	end 'check' else 'other'
		return 0
	end 'other'
end 'main'
```
```exitcode
1
```

<!-- test: bool-type-disambiguation -->
```maxon
typealias Integer = int(i64.min to i64.max)

function check(value Integer) returns Integer
	return value
end 'check'

function check(value bool) returns Integer
	if value 'branch'
		return 1
	end 'branch' else 'other'
		return 0
	end 'other'
end 'check'

function main() returns ExitCode
	return check(true)
end 'main'
```
```exitcode
1
```

<!-- test: float-type-disambiguation -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Decimal = float(f64.min to f64.max)

function measure(value Integer) returns Integer
	return value
end 'measure'

function measure(value Decimal) returns Integer
	return trunc(value)
end 'measure'

function main() returns ExitCode
	return measure(42.0)
end 'main'
```
```exitcode
42
```

<!-- test: second-param-disambiguation-result-type -->
Two overloads that SHARE their leading parameter type must still be told apart
by their later parameters — the overload key mangles every parameter, not just
the first. Here both `lookup` overloads start with `Registry`, so a resolver
that keyed only on the first argument would collapse them to one bucket member
and mis-type the result of the call. Because the overloads return DIFFERENT
types (`Integer` vs `bool`), picking the wrong one would flow the wrong type
into the downstream `use(...)` argument check. Selecting by the full signature
keeps each result type correct.
```maxon
typealias Integer = int(i64.min to i64.max)

type Registry
	export var seed as Integer

	export static function make(seed Integer) returns Registry
		return Registry{seed: seed}
	end 'make'
end 'Registry'

function lookup(reg Registry, index Integer) returns Integer
	return reg.seed + index
end 'lookup'

function lookup(reg Registry, present bool) returns bool
	if present 'yes'
		return reg.seed > 0
	end 'yes'
	return false
end 'lookup'

function useInt(value Integer) returns Integer
	return value
end 'useInt'

function useBool(flag bool) returns Integer
	if flag 'on'
		return 1
	end 'on'
	return 0
end 'useBool'

function main() returns ExitCode
	let reg = Registry.make(10)
	let asInt = lookup(reg, index: 5)
	let asBool = lookup(reg, present: true)
	return useInt(asInt) + useBool(asBool)
end 'main'
```
```exitcode
16
```

<!-- test: method-call-argument -->
An argument that is itself a METHOD CALL is scored by the METHOD'S RETURN TYPE,
not by the receiver's type. `a.count()` is an integer however `a` is declared,
so it selects `over(x Wide)` even though the `String` overload is declared
first. Declaration order is the whole point of this test: with the matching
overload written first, picking the first candidate would look correct by luck.
```maxon
typealias Wide = int(i64.min to i64.max)
typealias WideArray = Array with Wide

function over(x String) returns Wide
	return x.count() + 200
end 'over'

function over(x Wide) returns Wide
	return x + 100
end 'over'

function main() returns ExitCode
	var a = WideArray.create()
	a.push(7)
	return over(a.count())
end 'main'
```
```exitcode
101
```

<!-- test: method-call-argument-via-variable -->
The same call routed through a local binding. Binding the result first has
always worked; it is the control that says the method-call form must agree
with it rather than resolving to something else.
```maxon
typealias Wide = int(i64.min to i64.max)
typealias WideArray = Array with Wide

function over(x String) returns Wide
	return x.count() + 200
end 'over'

function over(x Wide) returns Wide
	return x + 100
end 'over'

function main() returns ExitCode
	var a = WideArray.create()
	a.push(7)
	let n = a.count()
	return over(n)
end 'main'
```
```exitcode
101
```

<!-- test: method-call-argument-receiver-type-is-wrong -->
The receiver's type is not merely unhelpful here, it is the WRONG answer:
`s` is a `String` and `s.count()` is an integer, so scoring the argument as
the receiver would match the `String` overload — which is declared first —
and then fail the call-site check on a parameter that never fitted.
```maxon
typealias Wide = int(i64.min to i64.max)

function over(x String) returns Wide
	return x.count() + 200
end 'over'

function over(x Wide) returns Wide
	return x + 100
end 'over'

function main() returns ExitCode
	let s = "hello"
	return over(s.count())
end 'main'
```
```exitcode
105
```

<!-- test: method-call-argument-chained -->
A chain resolves left to right: `t.branch()` yields a `Leaf`, and `size()` is
looked up on THAT type rather than on `t`.
```maxon
typealias Wide = int(i64.min to i64.max)

type Leaf
	export var tally as Wide

	export static function make(tally Wide) returns Self
		return Self{tally: tally}
	end 'make'

	export function size() returns Wide
		return self.tally
	end 'size'
end 'Leaf'

type Trunk
	export var leaf as Leaf

	export static function make(leaf Leaf) returns Self
		return Self{leaf: leaf}
	end 'make'

	export function branch() returns Leaf
		return self.leaf
	end 'branch'
end 'Trunk'

function over(x String) returns Wide
	return x.count() + 200
end 'over'

function over(x Wide) returns Wide
	return x + 100
end 'over'

function main() returns ExitCode
	let t = Trunk.make(Leaf.make(7))
	return over(t.branch().size())
end 'main'
```
```exitcode
107
```

<!-- test: method-call-argument-on-field -->
A method call whose receiver is a FIELD, not a bare variable. The field's
declared type owns the method, so `t.leaf.size()` must resolve through `Leaf`.
```maxon
typealias Wide = int(i64.min to i64.max)

type Leaf
	export var tally as Wide

	export static function make(tally Wide) returns Self
		return Self{tally: tally}
	end 'make'

	export function size() returns Wide
		return self.tally
	end 'size'
end 'Leaf'

type Trunk
	export var leaf as Leaf

	export static function make(leaf Leaf) returns Self
		return Self{leaf: leaf}
	end 'make'
end 'Trunk'

function over(x String) returns Wide
	return x.count() + 200
end 'over'

function over(x Wide) returns Wide
	return x + 100
end 'over'

function main() returns ExitCode
	let t = Trunk.make(Leaf.make(7))
	return over(t.leaf.size())
end 'main'
```
```exitcode
107
```

<!-- test: method-call-argument-string-result -->
The mirror of the integer cases, with the overloads written in the opposite
order: a method call returning `String` must select the `String` overload even
though the `Wide` one comes first, and even though the receiver is a struct
that is neither.
```maxon
typealias Wide = int(i64.min to i64.max)

type Leaf
	export var tally as Wide

	export static function make(tally Wide) returns Self
		return Self{tally: tally}
	end 'make'

	export function label() returns String
		return "leaf"
	end 'label'
end 'Leaf'

function over(x Wide) returns Wide
	return x + 100
end 'over'

function over(x String) returns Wide
	return x.count() + 200
end 'over'

function main() returns ExitCode
	let leaf = Leaf.make(7)
	return over(leaf.label())
end 'main'
```
```exitcode
204
```

<!-- test: method-call-argument-generic-element -->
The method's declared return type is the type PARAMETER `T`, which carries no
information on its own. It is resolved through the receiver alias's binding
(`WideCell` binds `T` to `Wide`) before it is scored; without that
substitution the only sound answer would be "unknown".
```maxon
typealias Wide = int(i64.min to i64.max)

type Cell uses T
	export var value as T

	export static function create(value T) returns Self
		return Self{value: value}
	end 'create'

	export function unwrap() returns T
		return self.value
	end 'unwrap'
end 'Cell'

typealias WideCell = Cell with Wide

function over(x String) returns Wide
	return x.count() + 200
end 'over'

function over(x Wide) returns Wide
	return x + 100
end 'over'

function main() returns ExitCode
	let c = WideCell.create(7)
	return over(c.unwrap())
end 'main'
```
```exitcode
107
```

<!-- test: method-call-argument-returns-self -->
A chainable method declared `returns Self` yields the RECEIVER's type, so it
must keep selecting the `Widget` overload however long the chain gets. This is
the one shape the old receiver-typed guess got right by accident — scoring
`w.bump()` as `w` happens to be correct exactly when the method returns `Self`
— so it is the case most at risk of quietly regressing.
```maxon
typealias Wide = int(i64.min to i64.max)

type Widget
	export var id as Wide

	export static function make(id Wide) returns Self
		return Self{id: id}
	end 'make'

	export function bump() returns Self
		return Widget{id: self.id + 1}
	end 'bump'
end 'Widget'

function over(x String) returns Wide
	return x.count() + 200
end 'over'

function over(x Widget) returns Wide
	return x.id + 100
end 'over'

function main() returns ExitCode
	let w = Widget.make(7)
	return over(w.bump().bump())
end 'main'
```
```exitcode
109
```

<!-- test: enum-property-argument -->
An ENUM PROPERTY is a member access that is not a struct field, and it scores by
what the property yields. `.name` is a `String` for every enum, so it selects the
`String` overload even though the `Wide` one is declared first. Reaching this
needed the member step of the argument peek to know an enum receiver at all: it
recognised struct fields only, so `k.name` produced no type, both overloads
survived, and the call was rejected as ambiguous rather than resolved.
```maxon
typealias Wide = int(i64.min to i64.max)

enum Kind
	alpha
	beta
end 'Kind'

function over(x Wide) returns Wide
	return x + 100
end 'over'

function over(x String) returns Wide
	return x.count() + 200
end 'over'

function main() returns ExitCode
	let k = Kind.alpha
	return over(k.name)
end 'main'
```
```exitcode
205
```

<!-- test: enum-property-argument-via-variable -->
The same property routed through a local binding. Binding first has always
worked; it is the control that says the direct form must agree with it rather
than failing where it succeeds.
```maxon
typealias Wide = int(i64.min to i64.max)

enum Kind
	alpha
	beta
end 'Kind'

function over(x Wide) returns Wide
	return x + 100
end 'over'

function over(x String) returns Wide
	return x.count() + 200
end 'over'

function main() returns ExitCode
	let k = Kind.alpha
	let name = k.name
	return over(name)
end 'main'
```
```exitcode
205
```

<!-- test: enum-ordinal-argument -->
`.ordinal` is an integer, so the same shape of access on the same value selects
the OTHER overload. Declared with `String` first, so a resolver that fell back to
declaration order would pick the wrong one and be caught here.
```maxon
typealias Wide = int(i64.min to i64.max)

enum Kind
	alpha
	beta
end 'Kind'

function over(x String) returns Wide
	return x.count() + 200
end 'over'

function over(x Wide) returns Wide
	return x + 100
end 'over'

function main() returns ExitCode
	let k = Kind.beta
	return over(k.ordinal)
end 'main'
```
```exitcode
101
```

<!-- test: enum-raw-value-argument -->
`.rawValue` scores by the enum's BACKING rather than by one fixed type: a
string-backed enum yields a `String` here, while the integer-backed default
yields an integer. Both spellings appear in one program so neither can be
satisfied by a constant answer.

⚠ The two addends are deliberately SMALL, so the sum lands inside the narrowest
`ExitCode` any host has. `ExitCode` is `int(0 to u32.max)` on Windows but
`int(0 to 255)` on Linux, macOS and wasi (`stdlib/Process.maxon`), so a larger
sum is not merely truncated on POSIX — the range check fires and the program
panics before it can return at all. This test returned 309 until 2026-07-27 and
was therefore red on every non-Windows target.
```maxon
typealias Wide = int(i64.min to i64.max)

enum Label
	greeting = "hi"
	farewell = "bye"
end 'Label'

enum Status
	ok = 7
	bad = 9
end 'Status'

function over(x Wide) returns Wide
	return x + 10
end 'over'

function over(x String) returns Wide
	return x.count() + 20
end 'over'

function main() returns ExitCode
	let l = Label.greeting
	let s = Status.ok
	return over(l.rawValue) + over(s.rawValue)
end 'main'
```
```exitcode
39
```

<!-- test: enum-struct-backing-field-argument -->
A struct-backed enum exposes its backing struct's fields directly — `e.field` is
`e.rawValue.field` — so the peek has to take both steps to score one access.
Stopping after the enum hands back no type and leaves the call ambiguous;
stopping after `rawValue` would hand back the backing STRUCT, which is a wrong
type rather than a missing one.
```maxon
typealias Wide = int(i64.min to i64.max)

type Spec
	export var width as Wide
	export var height as Wide

	export static function make(width Wide, height Wide) returns Spec
		return Spec{width: width, height: height}
	end 'make'
end 'Spec'

enum Preset
	small = Spec{width: 3, height: 2}
	large = Spec{width: 9, height: 4}
end 'Preset'

function over(x String) returns Wide
	return x.count() + 200
end 'over'

function over(x Wide) returns Wide
	return x + 100
end 'over'

function main() returns ExitCode
	let p = Preset.large
	return over(p.width)
end 'main'
```
```exitcode
109
```

<!-- test: overloads-each-carry-their-own-default -->
```maxon
typealias Integer = int(i64.min to i64.max)

function pad(value Integer, with String = "0") returns Integer
	return value + with.count()
end 'pad'

function pad(value bool, with String = "---") returns Integer
	if value 'yes'
		return with.count()
	end 'yes'
	return 0
end 'pad'

function main() returns ExitCode
	return (pad(1) + pad(true)) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: overloads-three-way-each-carry-their-own-default -->
```maxon
typealias Integer = int(i64.min to i64.max)

function tag(value Integer, mark String = "a") returns Integer
	return value + mark.count()
end 'tag'

function tag(value String, mark String = "bb") returns Integer
	return value.count() + mark.count()
end 'tag'

function tag(value bool, mark String = "ccc") returns Integer
	if value 'yes'
		return mark.count()
	end 'yes'
	return 0
end 'tag'

function main() returns ExitCode
	return (tag(1) + tag("xy") + tag(true)) as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: overload-default-supplied-explicitly-on-one-member -->
```maxon
typealias Integer = int(i64.min to i64.max)

function width(value Integer, unit String = "mm") returns Integer
	return value + unit.count()
end 'width'

function width(value bool, unit String = "inches") returns Integer
	if value 'yes'
		return unit.count()
	end 'yes'
	return 0
end 'width'

function main() returns ExitCode
	return (width(3, unit: "centimetre") + width(true)) as ExitCode
end 'main'
```
```exitcode
19
```

<!-- test: overloads-with-caller-location-defaults -->
```maxon
// --- file: main.maxon
typealias Integer = int(i64.min to i64.max)

type Expect

	export static function equal(actual Integer, expected Integer, message String = "num", file String = __file__, line SourceLineNumber = __line__) returns Integer
		print("{file}:{line} {message}\n")
		if actual == expected 'same'
			return message.count()
		end 'same'
		return 0
	end 'equal'

	export static function equal(actual String, expected String, message String = "text", file String = __file__, line SourceLineNumber = __line__) returns Integer
		print("{file}:{line} {message}\n")
		if actual.count() == expected.count() 'same'
			return message.count()
		end 'same'
		return 0
	end 'equal'

	export static function equal(actual bool, expected bool, message String = "flag", file String = __file__, line SourceLineNumber = __line__) returns Integer
		print("{file}:{line} {message}\n")
		if actual == expected 'same'
			return message.count()
		end 'same'
		return 0
	end 'equal'

end 'Expect'

function main() returns ExitCode
	let a = Expect.equal(1, expected: 1)
	let b = Expect.equal("xy", expected: "xy")
	let c = Expect.equal(true, expected: true)
	return (a + b + c) as ExitCode
end 'main'
```
```exitcode
11
```
```stdout
main.maxon:32 num
main.maxon:33 text
main.maxon:34 flag
```

<!-- test: error.overloads-disagree-on-a-defaulted-parameters-type -->
The members agree on the SHAPE of their defaults — the same parameter names, the same defaulted position —
so the set is admitted at its declarations, and the argument `f(true)` omits is supplied while the call is
parsed from a whole-program index that records ONE answer per name. That answer is the `Num` member's, and
this call resolves to the `Real` member: its helper returns a float, in a different REGISTER FILE from the
`int` the caller minted the value as, so the call would read the result out of the wrong register. It is the
`overloads-disagree-*` pair's own rule at a second op — the difference is only that the value in question is
one the compiler supplied, so the sentence names the parameter POSITION rather than a return type.
```maxon
typealias Num = int(-1000 to 1000)
typealias Real = float(f64.min to f64.max)

function f(a bool, x Real = 2.5) returns Num
	if a 'yes'
		if x > 1.0 'big'
			return 7
		end 'big'
		return 3
	end 'yes'
	return 0
end 'f'

function f(a Num, x Num = 1) returns Num
	return a + x
end 'f'

function main() returns ExitCode
	return f(true) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:20:9: the overloads of 'f' do not agree on the TYPE of parameter 1, which this call omitted and the compiler supplied from that parameter's default ('int' and 'float'). A defaulted argument's type is fixed while the call is parsed, from a whole-program index that records one answer per NAME, and the overload is resolved a whole pass later — so only a difference between plain scalars can be corrected by then. Declare that parameter at the same type in every overload
```

<!-- test: overloads-agree-on-the-error-they-throw -->
Both members `throws Boom`, so the one clause the whole-program declaration sweep files under the shared
source name is every member's answer and the `try` recovers `Boom` whichever member the call resolves to.
The two members return DIFFERENT values, so a call that reached the wrong one would be a wrong number
rather than a crash: `5 + 9`.
```maxon
typealias Num = int(-1000 to 1000)

enum Boom
	bad
end 'Boom'

type Chk

	export static function want(actual Num, expected Num) returns Num throws Boom
		if actual != expected 'differ'
			throw Boom.bad
		end 'differ'
		return 5
	end 'want'

	export static function want(actual bool, expected bool) returns Num throws Boom
		if actual != expected 'differ'
			throw Boom.bad
		end 'differ'
		return 9
	end 'want'

end 'Chk'

function main() returns ExitCode
	let a = try Chk.want(1, expected: 1) otherwise 0
	let b = try Chk.want(true, expected: true) otherwise 0
	return (a + b) as ExitCode
end 'main'
```
```exitcode
14
```

<!-- test: overloads-three-way-agree-on-the-error-they-throw -->
The shape both `stdlib/Testing.maxon`'s `Expect.equal` and `stdlib/Subprocess.maxon`'s `run` are written
in: THREE declarations of one name, every one of them throwing the one error type the module declares.
Each member answers a different value, so the sum names which members ran: `1 + 2 + 4`.
```maxon
typealias Num = int(-1000 to 1000)

enum TestFailure
	mismatch
end 'TestFailure'

type Expect

	export static function equal(actual Num, expected Num) returns Num throws TestFailure
		if actual != expected 'differ'
			throw TestFailure.mismatch
		end 'differ'
		return 1
	end 'equal'

	export static function equal(actual String, expected String) returns Num throws TestFailure
		if actual.count() != expected.count() 'differ'
			throw TestFailure.mismatch
		end 'differ'
		return 2
	end 'equal'

	export static function equal(actual bool, expected bool) returns Num throws TestFailure
		if actual != expected 'differ'
			throw TestFailure.mismatch
		end 'differ'
		return 4
	end 'equal'

end 'Expect'

function main() returns ExitCode
	let a = try Expect.equal(1, expected: 1) otherwise 0
	let b = try Expect.equal("xy", expected: "ab") otherwise 0
	let c = try Expect.equal(true, expected: true) otherwise 0
	return (a + b + c) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: overloaded-throw-is-recovered-by-try -->
The accept path is not only that the set COMPILES — the thrown error has to reach the handler, and the
`(e)` binding has to be typed from the clause the members share. Each member throws a different CASE of
that one type and the handler discriminates on it, so a recovery attributed to the wrong member is a
wrong number: `10` from the Num member's throw, `3` from the bool member's, and `2` from the bool
member's non-throwing return.
```maxon
typealias Num = int(-1000 to 1000)

enum Boom implements Error
	fromNumber
	fromFlag
end 'Boom'

function check(value Num) returns Num throws Boom
	if value < 0 'low'
		throw Boom.fromNumber
	end 'low'
	return 1
end 'check'

function check(value bool) returns Num throws Boom
	if value 'yes'
		throw Boom.fromFlag
	end 'yes'
	return 2
end 'check'

function main() returns ExitCode
	var result = 0
	try check(-1) otherwise (e) 'fromTheNumberMember'
		match e 'which'
			fromNumber then result = result + 10
			fromFlag then result = result + 100
		end 'which'
	end 'fromTheNumberMember'
	try check(true) otherwise (e) 'fromTheFlagMember'
		match e 'which'
			fromNumber then result = result + 1000
			fromFlag then result = result + 3
		end 'which'
	end 'fromTheFlagMember'
	let last = try check(false) otherwise 0
	return (result + last) as ExitCode
end 'main'
```
```exitcode
15
```

<!-- test: overloaded-throw-is-recovered-by-try-members-reversed -->
The identical program with the two members written in the other order, and it earns a case of its own
because the clause the `try` reads is filed LAST-WINS under one key: the case above would pass just as
well if the recovery were attributed to whichever member the sweep happened to record last, and only the
reversal can tell the two apart. Same three numbers, same total — `10` from the Num member's throw, `3`
from the bool member's, `2` from the bool member's non-throwing return.
```maxon
typealias Num = int(-1000 to 1000)

enum Boom implements Error
	fromNumber
	fromFlag
end 'Boom'

function check(value bool) returns Num throws Boom
	if value 'yes'
		throw Boom.fromFlag
	end 'yes'
	return 2
end 'check'

function check(value Num) returns Num throws Boom
	if value < 0 'low'
		throw Boom.fromNumber
	end 'low'
	return 1
end 'check'

function main() returns ExitCode
	var result = 0
	try check(-1) otherwise (e) 'fromTheNumberMember'
		match e 'which'
			fromNumber then result = result + 10
			fromFlag then result = result + 100
		end 'which'
	end 'fromTheNumberMember'
	try check(true) otherwise (e) 'fromTheFlagMember'
		match e 'which'
			fromNumber then result = result + 1000
			fromFlag then result = result + 3
		end 'which'
	end 'fromTheFlagMember'
	let last = try check(false) otherwise 0
	return (result + last) as ExitCode
end 'main'
```
```exitcode
15
```

<!-- test: error.overloads-disagree-on-the-error-they-throw -->
Two error types under one name. The `(e)` binding a `try` mints is typed while the call is PARSED, from
the one entry the by-name sweep holds — whichever declaration wrote it last — so one of the two calls
would decode the other member's error. The oracle accepts this program (it answers 14): its throws facts
are per-declaration, and shv2's sweep is keyed by the name the source wrote, so this is a conservative
refusal of a program the language permits rather than a rule of the language.
```maxon
typealias Num = int(-1000 to 1000)

enum Boom
	bad
end 'Boom'

enum Splat
	worse
end 'Splat'

function want(actual Num) returns Num throws Boom
	if actual < 0 'neg'
		throw Boom.bad
	end 'neg'
	return 5
end 'want'

function want(actual bool) returns Num throws Splat
	if actual 'yes'
		throw Splat.worse
	end 'yes'
	return 9
end 'want'

function main() returns ExitCode
	let a = try want(1) otherwise 0
	let b = try want(false) otherwise 0
	return (a + b) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:19:10: Unsupported: overloading 'want' — its declarations do not all state the same `throws` clause, and the whole-program declaration sweep publishes a function's throws clause under the name the source wrote, so a `try` at a call to this name cannot be told whether the call throws at all or which error type it recovers. The `try` is desugared when the call is PARSED and the overload is resolved a whole pass later, so nothing downstream can repair it. Give every overload the same `throws` clause, or give the overloads distinct names
```

<!-- test: error.overloads-disagree-on-whether-they-throw-at-all -->
The second way to disagree, and the sweep sees it from the other side: only a THROWING declaration is
recorded, so nothing in the throws registry can report the member that publishes nothing. It is the
declaration COUNT that does — every declaration is counted once, and the throwing ones are counted again
in a tally of their own, so a difference between the two IS the silent sibling. Without it this set is
admitted and `want(true)` is compiled as a throwing call, whose error flag the bool member never writes.
⚠ **A CONSERVATIVE REFUSAL, not a rule of the language**: the oracle compiles this exact program and
answers **14** (MEASURED), because its throws facts are per-declaration. Lifting it needs per-member facts
in shv2's sweep, not a better test here.
```maxon
typealias Num = int(-1000 to 1000)

enum Boom
	bad
end 'Boom'

function want(actual Num) returns Num throws Boom
	if actual < 0 'neg'
		throw Boom.bad
	end 'neg'
	return 5
end 'want'

function want(actual bool) returns Num
	if actual 'yes'
		return 9
	end 'yes'
	return 2
end 'want'

function main() returns ExitCode
	let a = try want(1) otherwise 0
	let b = want(true)
	return (a + b) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:10: Unsupported: overloading 'want' — its declarations do not all state the same `throws` clause, and the whole-program declaration sweep publishes a function's throws clause under the name the source wrote, so a `try` at a call to this name cannot be told whether the call throws at all or which error type it recovers. The `try` is desugared when the call is PARSED and the overload is resolved a whole pass later, so nothing downstream can repair it. Give every overload the same `throws` clause, or give the overloads distinct names
```

<!-- test: error.overloads-disagree-on-whether-they-throw-at-all-non-throwing-member-first -->
The same two declarations in the other order. The refusal is settled from the whole-program sweep, which
has folded every file before any of them is parsed, so which member the author wrote first cannot change
the verdict — only the position the diagnostic is reported at. Conservative in the same way its twin
above is, and for the same reason.
```maxon
typealias Num = int(-1000 to 1000)

enum Boom
	bad
end 'Boom'

function want(actual bool) returns Num
	if actual 'yes'
		return 9
	end 'yes'
	return 2
end 'want'

function want(actual Num) returns Num throws Boom
	if actual < 0 'neg'
		throw Boom.bad
	end 'neg'
	return 5
end 'want'

function main() returns ExitCode
	let a = try want(1) otherwise 0
	let b = want(true)
	return (a + b) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:10: Unsupported: overloading 'want' — its declarations do not all state the same `throws` clause, and the whole-program declaration sweep publishes a function's throws clause under the name the source wrote, so a `try` at a call to this name cannot be told whether the call throws at all or which error type it recovers. The `try` is desugared when the call is PARSED and the overload is resolved a whole pass later, so nothing downstream can repair it. Give every overload the same `throws` clause, or give the overloads distinct names
```

<!-- test: a-static-and-instance-pair-where-the-static-throws -->
✅ **REFUSED UNTIL W75, AND WHAT REFUSED IT WAS THE KEY.** A `static` member and an instance member of one
type are two registration keys — `T.m` and `T.m#__static`, told apart at the call by SYNTAX — and the
declaration sweep used to publish a function's `throws` clause under the ONE name the source wrote, so the
clause belonged to neither key. The by-name sweep folds now key by the MEMBER each entry belongs to, so the
static's clause is filed under the static's key and the instance's under the instance's, and a `try` at
either call recovers that member's own error type. Answers **7**.

⚠ **THE ORACLE REFUSES THIS PROGRAM, ON A DIFFERENT RULE** (MEASURED: `E3007: Ambiguous overload for 'T.m'`).
It treats a `static`/instance pair as ONE overload set and cannot tell these two members apart by their
parameter types; shv2 separates them by registration key (`same-name-methods.md`). That divergence is about
what a PAIR IS, and it is unchanged — this case is only about whether the clause can be attributed once the
pair exists.
```maxon
typealias Num = int(-1000 to 1000)

enum Boom
	bad
end 'Boom'

type T
	export var v as Num

	export static function make(v Num) returns T
		return Self{v: v}
	end 'make'

	export function m(b Num) returns Num
		return self.v + b
	end 'm'

	export static function m(a Num) returns Num throws Boom
		if a < 0 'neg'
			throw Boom.bad
		end 'neg'
		return a
	end 'm'
end 'T'

function main() returns ExitCode
	let t = T.make(1)
	let s = try T.m(4) otherwise 0
	return (s + t.m(2)) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-static-and-instance-pair-where-the-static-throws-declared-first -->
✅ **THE SAME PAIR WITH THE STATIC WRITTEN FIRST, and the point is that the answer is the same.** The
contest is detected at the SECOND member to fold, so one order re-keys the incumbent's already-filed clause
and the other files the newcomer's under its own key from the start — two different paths through the sweep
to one answer, and only running both says whether they agree. Answers **7**, as above. The oracle refuses
this one too, on `E3007`.
```maxon
typealias Num = int(-1000 to 1000)

enum Boom
	bad
end 'Boom'

type T
	export var v as Num

	export static function make(v Num) returns T
		return Self{v: v}
	end 'make'

	export static function m(a Num) returns Num throws Boom
		if a < 0 'neg'
			throw Boom.bad
		end 'neg'
		return a
	end 'm'

	export function m(b Num) returns Num
		return self.v + b
	end 'm'
end 'T'

function main() returns ExitCode
	let t = T.make(1)
	let s = try T.m(4) otherwise 0
	return (s + t.m(2)) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-static-and-instance-pair-where-the-instance-throws -->
✅ **THE OTHER HALF OF THE PAIR CARRYING THE CLAUSE.** Before W75 the instance's call found the clause and
was right by accident, while the static's asked for `T.m#__static`, missed, and was compiled as a call that
cannot throw — so a `try` over the STATIC would have been refused as a `try` on a non-throwing callee. Each
member now carries its own. Answers **7**; the oracle refuses on `E3007`.
```maxon
typealias Num = int(-1000 to 1000)

enum Boom
	bad
end 'Boom'

type T
	export var v as Num

	export static function make(v Num) returns T
		return Self{v: v}
	end 'make'

	export function m(b Num) returns Num throws Boom
		if b < 0 'neg'
			throw Boom.bad
		end 'neg'
		return self.v + b
	end 'm'

	export static function m(a Num) returns Num
		return a
	end 'm'
end 'T'

function main() returns ExitCode
	let t = T.make(1)
	let i = try t.m(2) otherwise 0
	return (T.m(4) + i) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-throwing-overload-set-whose-bare-name-another-directory-declares -->
✅ **THIS WAS A REFUSAL UNTIL W78, AND WHAT REFUSED IT WAS THE KEY, NOT THE DECLARATIONS.** The members
AGREE — both `throws Boom` — and the same two declarations at the root have always compiled. What refused
this one is that a free function contested across directories is registered as `alpha.want` while the
declaration sweep filed its clause, its disagreement verdict and both of its tallies under the bare `want`
— where `beta/`'s declaration is tallied too. There was no verdict at `alpha.want` to read, so the honest
answer was a blanket refusal. The sweep now files a declaration's facts AND its tallies under the one key
the parser asks with (`ProgramSignatures.sweepRegistrationKey`), so this set is judged on its declarations
like any other and answers **18** — which is what the oracle has always answered (MEASURED).
```maxon
// --- file: alpha/x.maxon
typealias Num = int(-1000 to 1000)

enum Boom
	bad
end 'Boom'

export function want(actual Num) returns Num throws Boom
	if actual < 0 'neg'
		throw Boom.bad
	end 'neg'
	return 5
end 'want'

export function want(actual bool) returns Num throws Boom
	if actual 'yes'
		throw Boom.bad
	end 'yes'
	return 9
end 'want'

// --- file: beta/y.maxon
typealias Small = int(-1000 to 1000)

export function want(actual Small) returns Small
	return actual + 1
end 'want'

// --- file: app/main.maxon
function main() returns ExitCode
	let a = try alpha.want(1) otherwise 0
	let b = try alpha.want(false) otherwise 0
	return (a + b + beta.want(3)) as ExitCode
end 'main'
```
```exitcode
18
```

<!-- test: error.a-contested-overload-set-whose-members-name-two-error-types -->
⛔ **THE DISCRIMINATING HALF OF THE CASE ABOVE, AND WITHOUT IT "the contested set compiles" WOULD BE
INDISTINGUISHABLE FROM "the contested set is never judged".** The same shape, with `alpha/`'s two members
made to name DIFFERENT error types: the verdict now has to be computed at `alpha.want` and has to say no.
Before W78 the verdict lived on the bare `want` and this key had none, so the refusal here proves the
per-key tally actually FIRES rather than merely being absent. ⚠ **Still narrower than the language**: the
oracle carries its throws facts per declaration and compiles this too.
```maxon
// --- file: alpha/x.maxon
typealias Num = int(-1000 to 1000)

enum Boom
	bad
end 'Boom'

enum Splat
	worse
end 'Splat'

export function want(actual Num) returns Num throws Boom
	if actual < 0 'neg'
		throw Boom.bad
	end 'neg'
	return 5
end 'want'

export function want(actual bool) returns Num throws Splat
	if actual 'yes'
		throw Splat.worse
	end 'yes'
	return 9
end 'want'

// --- file: beta/y.maxon
typealias Small = int(-1000 to 1000)

export function want(actual Small) returns Small
	return actual + 1
end 'want'

// --- file: app/main.maxon
function main() returns ExitCode
	let a = try alpha.want(1) otherwise 0
	let b = try alpha.want(false) otherwise 0
	return (a + b + beta.want(3)) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: alpha/specs/fragments/function-overloads/error.a-contested-overload-set-whose-members-name-two-error-types.test:20:17: Unsupported: overloading 'alpha.want' — its declarations do not all state the same `throws` clause, and the whole-program declaration sweep publishes a function's throws clause under the name the source wrote, so a `try` at a call to this name cannot be told whether the call throws at all or which error type it recovers. The `try` is desugared when the call is PARSED and the overload is resolved a whole pass later, so nothing downstream can repair it. Give every overload the same `throws` clause, or give the overloads distinct names
```

<!-- test: contested-directory-overload-set-agreeing-on-defaults -->
⭐ **THE CONTROL FOR W78's PER-KEY TALLY: A CONTESTED OVERLOAD SET WHOSE MEMBERS *AGREE* MUST STILL
COMPILE.** `alpha/`'s two `pick` members declare the same parameter names and default the same position, so
a short call is filled identically whichever one resolves — the W74 rule — and `beta/`'s own `pick` merely
contests the bare name. Before W78 this compiled for the wrong reason (the verdict was read off a key
nothing had written, which answers "agree" for every program); it must go on compiling once the tally is
kept per registration key, or the cure has bought its correctness by refusing what the language allows.
The oracle answers **65** too.
```maxon
// --- file: alpha/a.maxon
typealias Num = int(-1000 to 1000)

export function pick(a Num, b Num = 5) returns Num
	return a + b
end 'pick'

export function pick(a bool, b Num = 5) returns Num
	return b if a else 0
end 'pick'

// --- file: beta/b.maxon
typealias Small = int(-1000 to 1000)

export function pick(a Small) returns Small
	return a + 50
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return (alpha.pick(2) + alpha.pick(true) + beta.pick(3)) as ExitCode
end 'main'
```
```exitcode
65
```
