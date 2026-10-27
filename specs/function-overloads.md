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
typealias Integer = int(i64.min to i64.max)

function process(value Integer) returns Integer
	return value * 2
end 'process'

function process(value String) returns Integer
	return value.count()
end 'process'

process(42)        // calls process(value Integer)
process("hello")   // calls process(value String)
```

#### Disambiguation by parameter names

When overloads have the same parameter types but different names from the SECOND parameter on, the
caller's argument labels select the overload:

```text
function slice(start Integer, endIndex Integer) returns Integer
	return endIndex - start
end 'slice'

function slice(start Integer, length Integer) returns Integer
	return start + length
end 'slice'

slice(10, endIndex: 32)   // calls slice(start, endIndex)
slice(10, length: 32)     // calls slice(start, length)
```

A first argument is named only at a defaulted parameter (**E2052** otherwise), and overloads of one name
that declare defaults must agree on their parameters, so its label can never select an overload.

#### Ambiguous calls

A call that more than one overload matches is **E3007**, reported at the call and listing the candidates.
In particular, overloads that differ only in the NAME of their first parameter can never be told apart:

```text
function create(name String) returns Integer
	return name.count()
end 'create'

function create(label String) returns Integer
	return label.count()
end 'create'

create("hello")   // E3007: Ambiguous overload for 'create'
```

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
error type.

A **free function** whose bare name is also declared in another directory is registered under its
directory-qualified name (`alpha.want`), and the sweep files that declaration's facts — and the tallies
that say whether its declarations agree — under the same key. So such an overload set is judged on its
declarations exactly as a root-level one is: agreeing members compile, disagreeing ones are refused. The
verdict is not kept under the bare name, where a second directory's declarations are counted too.

##### ⚠ Every refusal above is **narrower than the language**

The paragraphs above describe what the compiler can compile, not what the language permits. The compiler
decides the ABI of a `try` while the call is **parsed**, from a whole-program entry keyed by the name the
source wrote, so it cannot tell the members of one name apart. Throws facts kept **per declaration** would
admit both refused shapes — two members naming two error types, and a throwing member beside a
non-throwing one — so each is a conservative refusal awaiting per-member facts in the sweep, not a rule.

#### An overload's registration name is unique by construction, not by an injective join

A disambiguating name built by joining `{parameter}_{type}` parts with `_` would not be injective,
`_` being legal inside both a parameter name and a type name — `f(x_i64_y P, w R)` and
`f(x i64_y_P, w R)` would compile to one name. The compiler's overload identity does not go through
that join at all: a member's registration name is claimed against the file's own set of
already-claimed names and counted past on a contest, so uniqueness is established by construction
rather than by an injectivity argument (`Parser.overloadRegistrationNameFor`). Such a pair compiles,
both overloads live, each answering for its own argument types.

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
error E3007: specs/function-overloads/error.ambiguous-same-signature.maxon:13:9: Ambiguous overload for 'create': multiple overloads match. Candidates: (name String), (label String)
```

<!-- test: error.an-argument-from-an-undefined-call-picks-no-overload -->
```maxon
function pick(x ExitCode) returns ExitCode
	return x
end 'pick'

function pick(x String) returns ExitCode
	return 1 if x.isEmpty() else 0
end 'pick'

function main() returns ExitCode
	return pick(undefinedAnswer())
end 'main'
```
```maxoncstderr
error E3004: specs/function-overloads/error.an-argument-from-an-undefined-call-picks-no-overload.maxon:11:14: call to undefined function 'undefinedAnswer'
```

<!-- test: error.an-argument-from-an-undefined-call-picks-no-overload-of-a-set-two-files-declare -->
The overloads are declared by two files, so no member holds the bare name. The undefined call is still the
only thing reported: the overload it feeds is neither refused nor named as undefined.
```maxon
// --- file: strings.maxon
export function pick(x String) returns ExitCode
	return 1 if x.isEmpty() else 0
end 'pick'

// --- file: codes.maxon
export function pick(x ExitCode) returns ExitCode
	return x
end 'pick'

// --- file: main.maxon
function main() returns ExitCode
	return pick(undefinedAnswer())
end 'main'
```
```maxoncstderr
error E3004: <fragment>:14:14: call to undefined function 'undefinedAnswer'
```

<!-- test: error.an-undefined-call-beside-a-known-argument-is-the-only-report-of-an-overloaded-call -->
The argument that cannot be typed blocks the choice of overload, so the argument beside it is judged against
none: it is not held to the first overload's parameter, whichever type that is.
```maxon
function pick(a ExitCode, b ExitCode) returns ExitCode
	return a + b
end 'pick'

function pick(a String, b String) returns ExitCode
	return 1 if a.isEmpty() or b.isEmpty() else 0
end 'pick'

function main() returns ExitCode
	return pick(undefinedAnswer(), b: "s")
end 'main'
```
```maxoncstderr
error E3004: <fragment>:11:14: call to undefined function 'undefinedAnswer'
```

<!-- test: error.an-undefined-call-in-an-overloaded-call-of-another-arity-is-the-only-report -->
Overloads of different arity: the undefined call picks none of them, so the call's argument count is not held
to the first overload's either.
```maxon
function pick(a ExitCode) returns ExitCode
	return a
end 'pick'

function pick(a String, b String) returns ExitCode
	return 1 if a.isEmpty() or b.isEmpty() else 0
end 'pick'

function main() returns ExitCode
	return pick(undefinedAnswer(), b: "s")
end 'main'
```
```maxoncstderr
error E3004: <fragment>:11:14: call to undefined function 'undefinedAnswer'
```

<!-- test: error.an-undefined-call-in-a-set-whose-first-member-is-hidden-from-the-caller-is-the-only-report -->
The overload registered first is file-private to a file the caller is not. The undefined call picks none of
the overloads, and the call is not checked against the hidden one either.
```maxon
// --- file: a.maxon
function pick(x ExitCode) returns ExitCode
	return x
end 'pick'

// --- file: main.maxon
function pick(x String) returns ExitCode
	return 1 if x.isEmpty() else 0
end 'pick'

function main() returns ExitCode
	return pick(undefinedAnswer())
end 'main'
```
```maxoncstderr
error E3004: <fragment>:13:14: call to undefined function 'undefinedAnswer'
```

<!-- test: a-void-overload-beside-a-value-one-resolves-by-argument -->
```maxon
function emit(flag bool)
	print("flag {flag}\n")
end 'emit'

function emit(tag Integer) returns String
	return "tag{tag}"
end 'emit'

function main() returns ExitCode
	emit(true)
	return 0
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
```maxon
function emit(tag Integer) returns String
	return "tag{tag}"
end 'emit'

function emit(flag bool)
	print("flag {flag}\n")
end 'emit'

function main() returns ExitCode
	print("{emit(1)}\n")
	return 0
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
error E3006: specs/function-overloads/error.overload-redeclared-with-the-same-parameters.maxon:12:10: duplicate definition of function 'pick(bool)' — 'pick' has more than one declaration in this program, so every one of those declarations is registered under its parameter-type spelling, and two of them spell the same parameters. Give the overloads distinct parameter types, or distinct names
```

<!-- test: error.a-label-distinguished-overload-redeclared-names-its-labels -->
Two overloads that share their parameter types are told apart by their labels, so the third declaration,
repeating the second's labels, collides — and the message names the member with its labels and types.
```maxon
typealias Integer = int(i64.min to i64.max)

function slice(start Integer, endIndex Integer) returns Integer
	return endIndex - start
end 'slice'

function slice(start Integer, length Integer) returns Integer
	return start + length
end 'slice'

function slice(start Integer, length Integer) returns Integer
	return start * length
end 'slice'

function main() returns ExitCode
	print("{slice(10, endIndex: 32)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3006: specs/function-overloads/error.a-label-distinguished-overload-redeclared-names-its-labels.maxon:12:10: duplicate definition of function 'slice(start Integer, length Integer)' — 'slice' has more than one declaration in this program, so every one of those declarations is registered under its parameter types and labels, and two of them spell the same parameters. Give the overloads distinct parameter types, or distinct names
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

	static function make(seed Integer) returns Registry
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
	return over(a.count() as Wide)
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
	let n = a.count() as Wide
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
	return over(s.count() as Wide)
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

	static function make(tally Wide) returns Self
		return Self{tally: tally}
	end 'make'

	function size() returns Wide
		return self.tally
	end 'size'
end 'Leaf'

type Trunk
	export var leaf as Leaf

	static function make(leaf Leaf) returns Self
		return Self{leaf: leaf}
	end 'make'

	function branch() returns Leaf
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

	static function make(tally Wide) returns Self
		return Self{tally: tally}
	end 'make'

	function size() returns Wide
		return self.tally
	end 'size'
end 'Leaf'

type Trunk
	export var leaf as Leaf

	static function make(leaf Leaf) returns Self
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

	static function make(tally Wide) returns Self
		return Self{tally: tally}
	end 'make'

	function label() returns String
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

	static function create(value T) returns Self
		return Self{value: value}
	end 'create'

	function unwrap() returns T
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
the one shape a receiver-typed guess would get right by accident — scoring
`w.bump()` as `w` happens to be correct exactly when the method returns `Self`
— so it is the case most at risk of quietly regressing.
```maxon
typealias Wide = int(i64.min to i64.max)

type Widget
	export var id as Wide

	static function make(id Wide) returns Self
		return Self{id: id}
	end 'make'

	function bump() returns Self
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
`String` overload even though the `Wide` one is declared first. The member step
of the argument peek knows an enum receiver, not only struct fields: without it
`k.name` would produce no type, both overloads would survive, and the call would
be rejected as ambiguous rather than resolved.
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
panics before it can return at all.
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

	static function make(width Wide, height Wide) returns Spec
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
	return value + (with.count() as Integer)
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
	return value + (mark.count() as Integer)
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
	return value + (unit.count() as Integer)
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
export typealias Integer = int(i64.min to i64.max)

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
error E2015: <fragment>:20:9: Unsupported: the overloads of 'f' do not agree on the TYPE of parameter 1, which this call omitted and the compiler supplied from that parameter's default ('int' and 'float'). A defaulted argument's type is fixed while the call is parsed, from a whole-program index that records one answer per NAME, and resolution applies the overload afterwards — so only a difference between plain scalars can be corrected by then. Declare that parameter at the same type in every overload
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

	static function want(actual Num, expected Num) returns Num throws Boom
		if actual != expected 'differ'
			throw Boom.bad
		end 'differ'
		return 5
	end 'want'

	static function want(actual bool, expected bool) returns Num throws Boom
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

	static function equal(actual Num, expected Num) returns Num throws TestFailure
		if actual != expected 'differ'
			throw TestFailure.mismatch
		end 'differ'
		return 1
	end 'equal'

	static function equal(actual String, expected String) returns Num throws TestFailure
		if actual.count() != expected.count() 'differ'
			throw TestFailure.mismatch
		end 'differ'
		return 2
	end 'equal'

	static function equal(actual bool, expected bool) returns Num throws TestFailure
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
	var result = 0 as Num
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
	var result = 0 as Num
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
would decode the other member's error. The compiler's sweep is keyed by the name the source wrote rather than
by declaration, so this is a conservative refusal of a program the language permits rather than a rule of the language.
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
error E2015: <fragment>:19:10: Unsupported: overloading 'want' — its declarations do not all state the same `throws` clause, and the whole-program declaration sweep publishes a function's throws clause under the name the source wrote, so a `try` at a call to this name cannot be told whether the call throws at all or which error type it recovers. The `try` is desugared when the call is PARSED, from the one clause kept per NAME, so nothing downstream can repair it. Give every overload the same `throws` clause, or give the overloads distinct names
```

<!-- test: error.overloads-disagree-on-whether-they-throw-at-all -->
The second way to disagree, and the sweep sees it from the other side: only a THROWING declaration is
recorded, so nothing in the throws registry can report the member that publishes nothing. It is the
declaration COUNT that does — every declaration is counted once, and the throwing ones are counted again
in a tally of their own, so a difference between the two IS the silent sibling. Without it this set is
admitted and `want(true)` is compiled as a throwing call, whose error flag the bool member never writes.
⚠ **A CONSERVATIVE REFUSAL, not a rule of the language**: the language permits this program. Lifting it needs per-member facts
in the compiler's sweep, not a better test here.
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
error E2015: <fragment>:15:10: Unsupported: overloading 'want' — its declarations do not all state the same `throws` clause, and the whole-program declaration sweep publishes a function's throws clause under the name the source wrote, so a `try` at a call to this name cannot be told whether the call throws at all or which error type it recovers. The `try` is desugared when the call is PARSED, from the one clause kept per NAME, so nothing downstream can repair it. Give every overload the same `throws` clause, or give the overloads distinct names
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
error E2015: <fragment>:15:10: Unsupported: overloading 'want' — its declarations do not all state the same `throws` clause, and the whole-program declaration sweep publishes a function's throws clause under the name the source wrote, so a `try` at a call to this name cannot be told whether the call throws at all or which error type it recovers. The `try` is desugared when the call is PARSED, from the one clause kept per NAME, so nothing downstream can repair it. Give every overload the same `throws` clause, or give the overloads distinct names
```

<!-- test: a-static-and-instance-pair-where-the-static-throws -->
✅ **THE `throws` CLAUSE IS KEYED BY THE MEMBER, NOT BY THE NAME.** A `static` member and an instance
member of one type are two registration keys — `T.m` and `T.m#__static`, told apart at the call by
SYNTAX — and the by-name sweep folds key by the MEMBER each entry belongs to, so the static's clause is
filed under the static's key and the instance's under the instance's, and a `try` at either call
recovers that member's own error type. Answers **7**.

The pair is two members and not one overload set even where their parameter types cannot tell them apart
(`same-name-methods.md`); this case is only about whether the clause can be attributed once the pair exists.
```maxon
typealias Num = int(-1000 to 1000)

enum Boom
	bad
end 'Boom'

type T
	export var v as Num

	static function make(v Num) returns T
		return Self{v: v}
	end 'make'

	function m(b Num) returns Num
		return self.v + b
	end 'm'

	static function m(a Num) returns Num throws Boom
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
to one answer, and only running both says whether they agree. Answers **7**, as above.
```maxon
typealias Num = int(-1000 to 1000)

enum Boom
	bad
end 'Boom'

type T
	export var v as Num

	static function make(v Num) returns T
		return Self{v: v}
	end 'make'

	static function m(a Num) returns Num throws Boom
		if a < 0 'neg'
			throw Boom.bad
		end 'neg'
		return a
	end 'm'

	function m(b Num) returns Num
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
✅ **THE OTHER HALF OF THE PAIR CARRYING THE CLAUSE.** With one clause filed under the name, the
instance's call would find it and be right by accident, while the static's would ask for `T.m#__static`,
miss, and be compiled as a call that cannot throw — so a `try` over the STATIC would be refused as a `try`
on a non-throwing callee. Each member carries its own. Answers **7**.
```maxon
typealias Num = int(-1000 to 1000)

enum Boom
	bad
end 'Boom'

type T
	export var v as Num

	static function make(v Num) returns T
		return Self{v: v}
	end 'make'

	function m(b Num) returns Num throws Boom
		if b < 0 'neg'
			throw Boom.bad
		end 'neg'
		return self.v + b
	end 'm'

	static function m(a Num) returns Num
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
✅ **THE VERDICT IS KEYED BY THE REGISTRATION KEY, NOT BY THE BARE NAME.** The members AGREE — both
`throws Boom` — exactly as the same two declarations at the root do. A free function contested across
directories is registered as `alpha.want`, and a clause, disagreement verdict and tallies filed under the
bare `want` — where `beta/`'s declaration is tallied too — would leave no verdict at `alpha.want` to read.
The sweep files a declaration's facts AND its tallies under the one key the parser asks with
(`ProgramSignatures.sweepRegistrationKey`), so this set is judged on its declarations like any other and
answers **18**.
```maxon
// --- file: alpha/x.maxon
export typealias Num = int(-1000 to 1000)

export enum Boom
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
export typealias Small = int(-1000 to 1000)

export function want(actual Small) returns Small
	return actual + 1
end 'want'

// --- file: app/main.maxon
function main() returns ExitCode
	let a = try alpha.want(1) otherwise 0
	let b = try alpha.want(false) otherwise 0
	return ((a + b) as ExitCode) + (beta.want(3) as ExitCode)
end 'main'
```
```exitcode
18
```

<!-- test: error.a-contested-overload-set-whose-members-name-two-error-types -->
⛔ **THE DISCRIMINATING HALF OF THE CASE ABOVE, AND WITHOUT IT "the contested set compiles" WOULD BE
INDISTINGUISHABLE FROM "the contested set is never judged".** The same shape, with `alpha/`'s two members
made to name DIFFERENT error types: the verdict has to be computed at `alpha.want` and has to say no. A
verdict living on the bare `want` would leave this key with none, so the refusal here proves the per-key
tally actually FIRES rather than merely being absent. ⚠ **Still narrower than the language**: throws
facts kept per declaration would compile this too.
```maxon
// --- file: alpha/x.maxon
export typealias Num = int(-1000 to 1000)

export enum Boom
	bad
end 'Boom'

export enum Splat
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
export typealias Small = int(-1000 to 1000)

export function want(actual Small) returns Small
	return actual + 1
end 'want'

// --- file: app/main.maxon
function main() returns ExitCode
	let a = try alpha.want(1) otherwise 0
	let b = try alpha.want(false) otherwise 0
	return ((a + b) as ExitCode) + (beta.want(3) as ExitCode)
end 'main'
```
```maxoncstderr
error E2015: alpha/specs/function-overloads/error.a-contested-overload-set-whose-members-name-two-error-types.maxon:20:17: Unsupported: overloading 'alpha.want' — its declarations do not all state the same `throws` clause, and the whole-program declaration sweep publishes a function's throws clause under the name the source wrote, so a `try` at a call to this name cannot be told whether the call throws at all or which error type it recovers. The `try` is desugared when the call is PARSED, from the one clause kept per NAME, so nothing downstream can repair it. Give every overload the same `throws` clause, or give the overloads distinct names
```

<!-- test: contested-directory-overload-set-agreeing-on-defaults -->
⭐ **THE CONTROL FOR THE PER-KEY TALLY: A CONTESTED OVERLOAD SET WHOSE MEMBERS *AGREE* MUST STILL
COMPILE.** `alpha/`'s two `pick` members declare the same parameter names and default the same position, so
a short call is filled identically whichever one resolves — the shape-of-defaults rule — and `beta/`'s own `pick`
merely contests the bare name. A verdict read off a key nothing had written would answer "agree" for every
program and compile this for the wrong reason; a tally kept per registration key must still compile it, or
it buys its correctness by refusing what the language allows.
```maxon
// --- file: alpha/a.maxon
export typealias Num = int(-1000 to 1000)

export function pick(a Num, b Num = 5) returns Num
	return a + b
end 'pick'

export function pick(a bool, b Num = 5) returns Num
	return b if a else 0
end 'pick'

// --- file: beta/b.maxon
export typealias Small = int(-1000 to 1000)

export function pick(a Small) returns Small
	return a + 50
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return ((alpha.pick(2) + alpha.pick(true)) as ExitCode) + (beta.pick(3) as ExitCode)
end 'main'
```
```exitcode
65
```

<!-- test: overloads-that-return-their-parameter -->
Handing a parameter back with `return` transfers nothing into durable storage, so an overload set whose
members each return their parameter is legal even when one member takes a managed type. The declaration
sweep records the hand-back — a returned PROMISE parameter is owned by the callee — and the consuming-overload
refusal asks only about the parameters a declaration consumes or feeds.
```maxon
typealias Integer = int(i64.min to i64.max)

function same(value String) returns String
	return value
end 'same'

function same(value Integer) returns Integer
	return value
end 'same'

function main() returns ExitCode
	print("{same("ok")} {same(3)}\n")
	return 0
end 'main'
```
```stdout
ok 3
```
```exitcode
0
```

<!-- test: overloads-that-hand-back-a-promise-parameter-transfer-by-the-member-called -->
Two overloads take a promise at different positions and each hands back its own; a call transfers ownership by the member its arguments select.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function relay(n Integer, p IntPromise) returns IntPromise
	print("{n}\n")
	return p
end 'relay'

function relay(p IntPromise, q IntPromise, flag bool) returns IntPromise
	if flag and not p.isComplete() 'peeked'
		print("peeked\n")
	end 'peeked'

	return q
end 'relay'

function main() returns ExitCode
	let r = relay(1, p: async work(1))
	print("{await r}\n")
	return 0
end 'main'
```
```stdout
1
2
```

<!-- test: overloads-where-one-stores-its-parameter-bind-each-members-ownership -->
Two static factories share a name: the first moves its `String` parameter into a field and the second only returns its `Integer` parameter; each call transfers ownership by the member it selects.
```maxon
typealias Integer = int(i64.min to i64.max)

type Holder
	export var s as String
	export var n as Integer

	static function make(s String) returns Self
		return Self{s: s, n: 0}
	end 'make'

	static function make(n Integer) returns Integer
		return n
	end 'make'
end 'Holder'

function main() returns ExitCode
	print("{Holder.make("abc").s} {Holder.make(7)}\n")
	return 0
end 'main'
```
```stdout
abc 7
```

<!-- test: overloads-where-the-second-stores-its-parameter-bind-each-members-ownership -->
The same set written in the other order.
```maxon
typealias Integer = int(i64.min to i64.max)

type Holder
	export var s as String
	export var n as Integer

	static function make(n Integer) returns Integer
		return n
	end 'make'

	static function make(s String) returns Self
		return Self{s: s, n: 0}
	end 'make'
end 'Holder'

function main() returns ExitCode
	print("{Holder.make("abc").s} {Holder.make(7)}\n")
	return 0
end 'main'
```
```stdout
abc 7
```

<!-- test: a-live-binding-to-the-overload-that-stores-it-is-still-readable -->
A live `String` binding passed to the overload that stores it is co-owned by the record, so the binding reads the same text afterwards.
```maxon
typealias Integer = int(i64.min to i64.max)

type Holder
	export var s as String
	export var n as Integer

	static function make(s String) returns Self
		return Self{s: s, n: 0}
	end 'make'

	static function make(n Integer) returns Integer
		return n
	end 'make'

	static function make(s String, n Integer) returns Integer
		return (s.count() as Integer) + n
	end 'make'
end 'Holder'

function main() returns ExitCode
	let word = "ab{Holder.make(1)}"
	let held = Holder.make(word)
	let counted = Holder.make(word, n: 4)
	print("{held.s} {counted} {word}\n")
	return 0
end 'main'
```
```stdout
ab1 7 ab1
```

<!-- test: a-call-resolving-to-the-overload-that-only-borrows-leaves-its-argument-owned -->
A call resolving to the overload that only reads its argument leaves a live binding owned by the caller and releases a temporary once.
```maxon
typealias Integer = int(i64.min to i64.max)

type Holder
	export var s as String
	export var n as Integer

	static function make(s String) returns Self
		return Self{s: s, n: 0}
	end 'make'

	static function make(h Holder) returns Integer
		return h.n + 5
	end 'make'
end 'Holder'

function main() returns ExitCode
	let held = Holder.make("x{1}")
	let fresh = Holder.make(Holder.make("y{2}"))
	let counted = Holder.make(held)
	print("{held.s} {counted} {fresh}\n")
	return 0
end 'main'
```
```stdout
x1 5 5
```

<!-- test: an-overload-that-stores-and-one-that-reassigns-the-same-column-bind-each-call -->
One overload stores its `String` parameter and another reassigns its `Integer` parameter; each call binds its argument by the member it selects.
```maxon
typealias Integer = int(i64.min to i64.max)

type Holder
	export var s as String

	static function make(s String) returns Self
		return Self{s: s}
	end 'make'

	static function make(n Integer)
		n = 42
	end 'make'
end 'Holder'

function main() returns ExitCode
	var word = "ab{1}"
	let held = Holder.make(word)
	var count = 1 as Integer
	Holder.make(count)
	print("{held.s} {count} {word}\n")
	word = "done"
	return 0
end 'main'
```
```stdout
ab1 42 ab1
```

<!-- test: a-promise-passed-to-the-overload-that-only-reads-it-is-awaited-afterwards -->
Two overloads take a promise in the same column: one hands it back, the other only reads a second argument. A call selecting the reading member leaves the promise with the caller, who awaits it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function peek(p IntPromise, v Integer) returns IntPromise
	print("{v}\n")
	return p
end 'peek'

function peek(_ IntPromise, v String) returns Integer
	return v.count() as Integer
end 'peek'

function main() returns ExitCode
	let p = async work(1)
	let n = peek(p, v: "a")
	print("{n} {await p}\n")
	return 0
end 'main'
```
```stdout
1 2
```

<!-- test: error.a-promise-handed-to-the-overload-that-takes-it-cannot-be-awaited-again -->
A call selecting the overload that hands the promise back moves it, so awaiting the original binding again is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function peek(p IntPromise, v Integer) returns IntPromise
	print("{v}\n")
	return p
end 'peek'

function peek(_ IntPromise, v String) returns Integer
	return v.count() as Integer
end 'peek'

function main() returns ExitCode
	let p = async work(1)
	let n = peek(p, v: 1)
	print("{await n} {await p}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:22:26: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-promise-handed-over-on-one-branch-is-released-on-the-other -->
One branch hands the promise to the overload that takes it and the other leaves it; each path releases it exactly once.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function peek(p IntPromise, v Integer) returns IntPromise
	print("{v}\n")
	return p
end 'peek'

function peek(_ IntPromise, v String) returns Integer
	return v.count() as Integer
end 'peek'

function decide(x Integer) returns bool
	return x > 3
end 'decide'

function run(x Integer)
	let p = async work(x)

	if decide(x) 'taken'
		let r = peek(p, v: x)
		print("taken {await r}\n")
	end 'taken'
end 'run'

function borrow(x Integer)
	let p = async work(x)

	if decide(x) 'read'
		let n = peek(p, v: "abc")
		print("read {n}\n")
	end 'read'

	print("awaited {await p}\n")
end 'borrow'

function main() returns ExitCode
	run(5)
	run(1)
	borrow(5)
	borrow(1)
	return 0
end 'main'
```
```stdout
5
taken 6
read 3
awaited 6
awaited 2
```

<!-- test: error.a-promise-given-away-in-every-iteration-of-a-loop-is-used-after-its-move -->
Handing a promise declared outside a loop to a callee that takes it, on every iteration, reads it again after the first iteration moved it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function keep(p IntPromise, v Integer) returns IntPromise
	print("{v}\n")
	return p
end 'keep'

function main() returns ExitCode
	let p = async work(1)
	var i = 0 as Integer

	while i < 2 'loop'
		let q = keep(p, v: i)
		print("{await q}\n")
		i = i + 1
	end 'loop'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:20:16: use of moved value 'p': it was moved in an earlier iteration of this loop
```

<!-- test: a-promise-given-away-inside-a-loop-that-then-breaks-is-moved-once -->
A promise declared outside a loop and handed to a callee that takes it, followed by `break`, is moved exactly once.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function keep(p IntPromise, v Integer) returns IntPromise
	print("{v}\n")
	return p
end 'keep'

function main() returns ExitCode
	let p = async work(1)
	var i = 0 as Integer

	while i < 2 'loop'
		let q = keep(p, v: i)
		print("{await q}\n")
		break
	end 'loop'

	return 0
end 'main'
```
```stdout
0
2
```

<!-- test: error.an-alias-of-a-promise-given-away-cannot-be-awaited -->
After a promise is handed to a callee that takes it, an alias bound before the call no longer owns it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function keep(p IntPromise, v Integer) returns IntPromise
	print("{v}\n")
	return p
end 'keep'

function main() returns ExitCode
	let p = async work(1)
	let q = p
	let r = keep(p, v: 5)
	print("{await q}\n")
	print("{await r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:19:16: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```


<!-- test: a-promise-reassigned-on-one-branch-and-read-by-the-other-is-awaited-after-the-join -->
A promise reassigned on one branch and handed to the overload that only reads it on the other is owned by the caller after the join and awaited once.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function peek(p IntPromise, v Integer) returns IntPromise
	print("{v}\n")
	return p
end 'peek'

function peek(_ IntPromise, v String) returns Integer
	return v.count() as Integer
end 'peek'

function decide(x Integer) returns bool
	return x > 3
end 'decide'

function joinReassignedAndHandedOver(x Integer) returns Integer
	var p = async work(x)

	if decide(x) 'rearm'
		p = async work(x * 10)
	end 'rearm' else 'read'
		print("read {peek(p, v: "ab")}\n")
	end 'read'

	return await p
end 'joinReassignedAndHandedOver'

function main() returns ExitCode
	print("{joinReassignedAndHandedOver(5)} {joinReassignedAndHandedOver(1)}\n")
	return 0
end 'main'
```
```stdout
read 2
51 2
```

<!-- test: error.an-overloaded-function-refuses-an-argument-wearing-another-alias-of-the-parameters-type -->
`Nums` and `Others` are two spellings of one array type that carry different brands. The only array-taking `pick` is declared at `Nums`, so the call with an `Others` binds no member and is refused as an argument mismatch.
```maxon
typealias Num = int(0 to 100)
typealias Nums = Array with Num
typealias Others = Array with Num

function pick(a Nums) returns Count
	return a.count()
end 'pick'

function pick(n Count) returns Count
	return n
end 'pick'

function main() returns ExitCode
	var good = Nums.create()
	good.push(5)
	var other = Others.create()
	other.push(6)
	return (pick(good) + pick(other)) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:19:23: argument type mismatch for 'a': expected 'Nums', got 'Others'
```

<!-- test: error.an-overloaded-function-refuses-an-argument-wearing-another-alias-of-a-qualified-parameters-type -->
The same pair with the parameter spelled through a namespace qualifier: the brand `lib.Nums` names is the one the declaration is bound by, so an `lib.Others` argument binds no member.
```maxon
// --- file: lib/nums.maxon
public typealias Num = int(0 to 100)
public typealias Nums = Array with Num
public typealias Others = Array with Num

// --- file: main.maxon
function pick(a lib.Nums) returns Count
	return a.count()
end 'pick'

function pick(n Count) returns Count
	return n
end 'pick'

function main() returns ExitCode
	var good = lib.Nums.create()
	good.push(5)
	var other = lib.Others.create()
	other.push(6)
	return (pick(good) + pick(other)) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:21:23: argument type mismatch for 'a': expected 'Nums', got 'Others'
```

<!-- test: a-bare-literal-at-a-narrower-alias-binds-the-alias-member-beside-a-generic -->
<!-- process: own -->
A bare literal fits `describe(n Score)` by its range, so the non-generic member binds it with no cast; the generic serves every other type.
```maxon
typealias Score = int(0 to 100)

function describe(n Score) returns Count
	_ = n
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	print("{describe(5)} {describe("a")}\n")
	return 0
end 'main'
```
```stdout
1 2
```

<!-- test: error.a-cast-to-the-alias-beside-a-generic-is-an-unneeded-cast -->
<!-- process: own -->
The non-generic member wins without the cast, so `5 as Score` repeats what the parameter already says.
```maxon
typealias Score = int(0 to 100)

function describe(n Score) returns Count
	_ = n
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	print("{describe(5 as Score)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3010: <fragment>:15:21: unneeded cast: the literal 5 already fits in 'Score'
```

<!-- test: a-cast-that-fixes-the-type-parameter-picks-the-generic-beside-an-alias-member -->
<!-- process: own -->
A cast to another alias fixes `T` to that alias and the alias member no longer fits, so the generic binds.
```maxon
typealias Score = int(0 to 100)
typealias Other = int(0 to 1000)

function describe(n Score) returns Count
	_ = n
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	print("{describe(5 as Other)}\n")
	return 0
end 'main'
```
```stdout
2
```

<!-- test: a-full-range-alias-parameter-takes-a-bare-literal-beside-a-generic -->
<!-- process: own -->
An alias that admits every value is an exact fit for a bare literal; the generic binds every other type.
```maxon
typealias Integer = int(i64.min to i64.max)

function describe(n Integer) returns Count
	_ = n
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	print("{describe(5)} {describe("a")}\n")
	return 0
end 'main'
```
```stdout
1 2
```

<!-- test: an-argument-of-the-alias-type-picks-the-alias-member-beside-a-generic -->
<!-- process: own -->
An argument that already wears `Score` fits the alias member exactly.
```maxon
typealias Score = int(0 to 100)

function describe(n Score) returns Count
	_ = n
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	let s = 5 as Score
	print("{describe(s)} {describe("a")}\n")
	return 0
end 'main'
```
```stdout
1 2
```

<!-- test: a-bare-float-literal-at-a-narrower-float-alias-binds-the-alias-member-beside-a-generic -->
<!-- process: own -->
The rule holds for a float alias as for an integer one.
```maxon
typealias Ratio = float(0.0 to 1.0)

function describe(r Ratio) returns Count
	_ = r
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	print("{describe(0.5)} {describe("a")}\n")
	return 0
end 'main'
```
```stdout
1 2
```

<!-- test: a-loop-counter-binds-the-alias-member-beside-a-generic -->
<!-- process: own -->
A counter that no alias names fits the alias parameter by value, so the non-generic member binds it on every trip.
```maxon
typealias Score = int(0 to 100)

function describe(n Score) returns Count
	_ = n
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	var total = 0 as Count

	for i in 0 upto 3 'eachTrip'
		total = total + describe(i)
	end 'eachTrip'

	print("{total}\n")
	return 0
end 'main'
```
```stdout
3
```

<!-- test: a-negated-literal-binds-the-alias-member-while-an-out-of-range-one-binds-the-generic -->
<!-- process: own -->
`-5` is a value of `Signed` and `-500` is not, so the first binds the alias member and the second falls to the generic.
```maxon
typealias Signed = int(-100 to 100)

function describe(n Signed) returns Count
	_ = n
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	print("{describe(-5)} {describe(-500)}\n")
	return 0
end 'main'
```
```stdout
1 2
```

<!-- test: a-literal-outside-the-alias-binds-the-generic -->
<!-- process: own -->
`500` is not a value of `Score`, so the alias member does not serve it and the generic does.
```maxon
typealias Score = int(0 to 100)

function describe(n Score) returns Count
	_ = n
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	print("{describe(500)}\n")
	return 0
end 'main'
```
```stdout
2
```

<!-- test: a-bound-literal-binds-the-alias-member-beside-a-generic -->
<!-- process: own -->
A `let` that holds a literal is a value that fits the alias by its range.
```maxon
typealias Score = int(0 to 100)

function describe(n Score) returns Count
	_ = n
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	let k = 5
	print("{describe(k)}\n")
	return 0
end 'main'
```
```stdout
1
```

<!-- test: a-count-parameter-takes-a-bare-literal-beside-a-generic -->
<!-- process: own -->
`Count` is a library alias that narrows the integers to the non-negative ones, and a literal fits it by its range.
```maxon
function describe(n Count) returns Count
	_ = n
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	print("{describe(5)} {describe("a")}\n")
	return 0
end 'main'
```
```stdout
1 2
```

<!-- test: a-per-instance-alias-parameter-takes-a-bare-literal-beside-a-generic -->
<!-- process: own -->
An alias declared inside a generic type and named through an instance is judged by its range like any other: a literal inside its range binds the non-generic member and one outside it falls to the generic.
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to 9)
	export var value as T

	static function create(value T) returns Self
		return Self{value: value}
	end 'create'
end 'Wrapper'

typealias IntWrapper = Wrapper with Integer

function describe(i IntWrapper.Idx) returns Count
	_ = i
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	print("{describe(5)} {describe(50)} {describe("a")}\n")
	return 0
end 'main'
```
```stdout
1 2 2
```

<!-- test: a-member-missing-a-required-argument-does-not-serve-the-call-beside-a-generic -->
<!-- process: own -->
`show("a")` leaves `width` unsupplied and it has no default, so the two-parameter member does not fit and the generic serves the call.
```maxon
function show(s String, width Count) returns Count
	_ = s
	_ = width
	return 1
end 'show'

function show(x T) uses T returns Count
	_ = x
	return 2
end 'show'

function main() returns ExitCode
	print("{show("a")}\n")
	return 0
end 'main'
```
```stdout
2
```

<!-- test: error.an-inline-ranged-parameter-type-is-refused-before-any-overload-is-picked -->
A ranged type is spelled through a `typealias`, so the declaration is refused where it is written and the call is never judged against it.
```maxon
function pick(n int(0 to 10)) returns Count
	return 1
end 'pick'

function pick(s String) returns Count
	return 2
end 'pick'

function main() returns ExitCode
	print("{pick("abc")}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:2:17: Cannot use bare 'int' as a type. Define a typealias with range constraints, e.g., typealias MyInt = int(0 to 100)
```

<!-- test: error.an-inline-ranged-float-parameter-of-a-static-is-refused-before-any-overload-is-picked -->
The same refusal for a static member of a type, with a `String` member beside it that would take the call.
```maxon
type Box
	export var n as Count

	static function make(v float(0.0 to 1.0)) returns Self
		return Self{n: 1}
	end 'make'

	static function make(s String) returns Self
		return Self{n: 2}
	end 'make'
end 'Box'

function main() returns ExitCode
	let b = Box.make("abc")
	print("{b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:5:25: Cannot use bare 'float' as a type. Define a typealias with range constraints, e.g., typealias MyFloat = float(0.0 to 1.0)
```

<!-- test: error.a-capability-word-on-a-plain-function-parameter-is-refused-before-any-overload-is-picked -->
`keep` qualifies a parameter of a function type, an interface method or a generic function only, so a plain overloaded function that writes it is refused at the declaration.
```maxon
function pick(s keep String) returns Count
	return 1
end 'pick'

function pick(n Count) returns Count
	return 2
end 'pick'

function main() returns ExitCode
	print("{pick("abc")}{pick(7)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2076: <fragment>:2:17: 'keep' qualifies a parameter or result of a function type, an interface method or a generic function only
```

<!-- test: error.an-undeclared-parameter-type-is-reported-once-and-picks-no-overload -->
A parameter type that names nothing is the declaration's own error; the call beside it adds no ambiguity or mismatch to it.
```maxon
function pick(n Nonexistent) returns Count
	return 1
end 'pick'

function pick(s String) returns Count
	return 2
end 'pick'

function main() returns ExitCode
	print("{pick("abc")}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:2:17: Unknown type 'Nonexistent'
```

<!-- test: error.overloads-that-disagree-about-defaults-are-refused-at-the-declaration -->
A defaulted parameter that its sibling overload does not declare is refused where the default is written, so no call is ever filled against a member it does not belong to.
```maxon
function f(a String) returns Count
	return 1
end 'f'

function f(a Count, b Count = 1) returns Count
	return a + b
end 'f'

function main() returns ExitCode
	print("{f("x")}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:29: Unsupported: a default value on parameter 'b' of 'f' — that name is declared more than once in this program and the declarations do not agree about their defaults: they differ in their parameter list, in which positions default, or in whether they default anything at all. The whole-program declaration sweep publishes defaults under the name the source wrote and a short call is filled when it is PARSED, a whole pass before the overload is resolved, so a call that omits an argument cannot be told which declaration's shape to fill. Give every declaration of this name the same parameters and the same defaults, or drop the default
```

<!-- test: error.overloads-that-differ-only-by-one-defaulted-parameter-are-refused-at-the-declaration -->
The same refusal when both members take a `Count` and one of them defaults a second parameter.
```maxon
function f(a Count) returns Count
	return a
end 'f'

function f(a Count, b Count = 1) returns Count
	return a + b
end 'f'

function main() returns ExitCode
	print("{f(5)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:29: Unsupported: a default value on parameter 'b' of 'f' — that name is declared more than once in this program and the declarations do not agree about their defaults: they differ in their parameter list, in which positions default, or in whether they default anything at all. The whole-program declaration sweep publishes defaults under the name the source wrote and a short call is filled when it is PARSED, a whole pass before the overload is resolved, so a call that omits an argument cannot be told which declaration's shape to fill. Give every declaration of this name the same parameters and the same defaults, or drop the default
```

<!-- test: overloads-that-agree-about-defaults-fill-the-omitted-argument-of-the-member-the-call-binds -->
Both members declare the same defaulted parameter, so the omitted argument is filled the same way whichever member the call binds, and each member's own default expression is the one that runs.
```maxon
function f(a String, b Count = 10) returns Count
	return a.count() + b
end 'f'

function f(a Count, b Count = 20) returns Count
	return a + b
end 'f'

function main() returns ExitCode
	print("{f("xy")} {f(5)} {f("xy", b: 1)} {f(5, b: 1)}\n")
	return 0
end 'main'
```
```stdout
12 25 3 6
```

<!-- test: an-overloaded-element-returning-method-on-a-float-array-binds-each-call -->
The element a method hands back is the opaque word the instance reads as a float, and the overload the arguments pick is checked against the same word.
```maxon
typealias Ratio = float(0.0 to 1.0)
typealias Ratios = Array with Ratio

extension Array
	function head() returns Element
		return try self.get(0) otherwise panic("empty")
	end 'head'

	function head(fallback Element) returns Element
		return (try self.get(0) otherwise fallback)
	end 'head'
end 'Array'

function main() returns ExitCode
	var r = Ratios.create()
	r.push(0.5)
	print("{r.head()} {r.head(0.25)}\n")
	return 0
end 'main'
```
```stdout
0.5 0.5
```

<!-- test: error.a-float-type-argument-is-refused-before-an-overloaded-method-is-judged -->
A user generic type cannot be instantiated over a float, so an overloaded element-returning method of one is never reached with a float result.
```maxon
typealias Ratio = float(0.0 to 1.0)

type Box uses T
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function get() returns T
		return self.item
	end 'get'

	function get(fallback T) returns T
		_ = fallback
		return self.item
	end 'get'
end 'Box'

typealias RatioBox = Box with Ratio

function main() returns ExitCode
	let b = RatioBox.create(0.5)
	print("{b.get()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2062: <fragment>:21:31: Cannot use 'Ratio' as a type argument: a float type argument is not supported yet. A type parameter is an opaque 8-byte general-purpose slot under the compiler's dictionary-passing, and a float value travels in a floating-point register, so it has no way through
```

<!-- test: error.a-tied-call-with-a-cast-reports-the-tie-and-no-unneeded-cast -->
`f(a Score)` and `f(b Score)` fit a positional call alike, so the call is ambiguous; the cast to `Score` is not reported as unneeded beside the real error.
```maxon
typealias Score = int(0 to 100)

function f(a Score) returns Count
	_ = a
	return 1
end 'f'

function f(b Score) returns Count
	_ = b
	return 2
end 'f'

function main() returns ExitCode
	print("{f(5 as Score)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3007: <fragment>:15:10: Ambiguous overload for 'f': multiple overloads match. Candidates: (a Score), (b Score)
```

<!-- test: error.an-unmatched-call-with-a-cast-reports-the-mismatch-and-no-unneeded-cast -->
No member takes `true` for its second parameter, so the call is refused as a mismatch; the cast to `Score` is not reported as unneeded beside the real error.
```maxon
typealias Score = int(0 to 100)

function f(a Score, b String) returns Count
	_ = a
	_ = b
	return 1
end 'f'

function f(a Score, b Count) returns Count
	_ = a
	_ = b
	return 2
end 'f'

function main() returns ExitCode
	print("{f(5 as Score, b: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:17:10: argument type mismatch for 'b': expected 'String', got 'bool'
```

<!-- test: function-overloads.a-written-negative-and-an-over-the-top-literal-leave-the-alias-and-the-generic-serves -->
<!-- process: own -->
A written negative and a literal above the top lie outside `Score`, so the alias member does not take them by value and the generic binds.
```maxon
typealias Score = int(0 to 100)

function describe(n Score) returns Count
	_ = n
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	print("{describe(-1)} {describe(101)} {describe(100)}\n")
	return 0
end 'main'
```
```stdout
2 2 1
```

<!-- test: function-overloads.u64-max-written-as-a-name-leaves-the-alias-and-the-generic-serves -->
<!-- process: own -->
`u64.max` is a known constant outside `Score`, so the generic binds it.
```maxon
typealias Score = int(0 to 100)

function describe(n Score) returns Count
	_ = n
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	print("{describe(u64.max)} {describe(100)}\n")
	return 0
end 'main'
```
```stdout
2 1
```

<!-- test: function-overloads.a-counter-that-leaves-the-alias-is-served-by-the-generic -->
<!-- process: own -->
A counter over 0 upto 200 reaches values `Score` cannot hold, so the generic binds it instead of the alias member refusing it.
```maxon
typealias Score = int(0 to 100)

function describe(n Score) returns Count
	_ = n
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	var total = 0 as Count

	for i in 0 upto 200 'eachNumber'
		total = total + describe(i)
	end 'eachNumber'

	print("{total}\n")
	return 0
end 'main'
```
```stdout
400
```

<!-- test: function-overloads.a-counter-inside-the-alias-binds-the-alias-member -->
<!-- process: own -->
A counter over 0 upto 50 stays inside `Score`, so the non-generic member takes it by value.
```maxon
typealias Score = int(0 to 100)

function describe(n Score) returns Count
	_ = n
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	var total = 0 as Count

	for i in 0 upto 50 'eachNumber'
		total = total + describe(i)
	end 'eachNumber'

	print("{total}\n")
	return 0
end 'main'
```
```stdout
50
```

<!-- test: function-overloads.a-value-computed-from-a-counter-is-judged-by-its-interval -->
<!-- process: own -->
`i + 1` over 0 upto 50 stays inside `Score` and binds the alias member; `i * 10` reaches 490 and the generic serves it.
```maxon
typealias Score = int(0 to 100)

function describe(n Score) returns Count
	_ = n
	return 1
end 'describe'

function describe(x T) uses T returns Count
	_ = x
	return 2
end 'describe'

function main() returns ExitCode
	var inside = 0 as Count
	var outside = 0 as Count

	for i in 0 upto 50 'eachNumber'
		inside = inside + describe(i + 1)
		outside = outside + describe(i * 10)
	end 'eachNumber'

	print("{inside} {outside}\n")
	return 0
end 'main'
```
```stdout
50 100
```

<!-- test: function-overloads.error.a-free-function-defaulting-its-sixty-fifth-parameter-is-refused -->
<!-- process: own -->
A free function with 66 parameters, the last two defaulted, is past the 64 argument slots a call can carry, so the declaration is refused whatever its defaults are.
```maxon
function wide(p0 Count, p1 Count, p2 Count, p3 Count, p4 Count, p5 Count, p6 Count, p7 Count, p8 Count, p9 Count, p10 Count, p11 Count, p12 Count, p13 Count, p14 Count, p15 Count, p16 Count, p17 Count, p18 Count, p19 Count, p20 Count, p21 Count, p22 Count, p23 Count, p24 Count, p25 Count, p26 Count, p27 Count, p28 Count, p29 Count, p30 Count, p31 Count, p32 Count, p33 Count, p34 Count, p35 Count, p36 Count, p37 Count, p38 Count, p39 Count, p40 Count, p41 Count, p42 Count, p43 Count, p44 Count, p45 Count, p46 Count, p47 Count, p48 Count, p49 Count, p50 Count, p51 Count, p52 Count, p53 Count, p54 Count, p55 Count, p56 Count, p57 Count, p58 Count, p59 Count, p60 Count, p61 Count, p62 Count, p63 Count, p64 Count = 0, p65 Count = 0) returns Count
	return p0
end 'wide'

function main() returns ExitCode
	_ = wide(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:10: Unsupported: a function with 66 argument slots — more than the 64 a call can carry. A signature's slots are its declared parameters plus the hidden ones the ABI adds (a witness per interface-typed parameter, a layout descriptor for a generic that reads one or calls a function that does, one witness per `where` constraint), and the limit is the width of the per-argument float mask that routes each one to its register file
```

<!-- test: function-overloads.error.a-method-defaulting-its-sixty-fifth-parameter-is-refused -->
<!-- process: own -->
A method with 66 parameters, the last two defaulted, is past the argument slots a call can carry, so the declaration is refused.
```maxon
type Holder
	var seed as Count

	function wide(p0 Count, p1 Count, p2 Count, p3 Count, p4 Count, p5 Count, p6 Count, p7 Count, p8 Count, p9 Count, p10 Count, p11 Count, p12 Count, p13 Count, p14 Count, p15 Count, p16 Count, p17 Count, p18 Count, p19 Count, p20 Count, p21 Count, p22 Count, p23 Count, p24 Count, p25 Count, p26 Count, p27 Count, p28 Count, p29 Count, p30 Count, p31 Count, p32 Count, p33 Count, p34 Count, p35 Count, p36 Count, p37 Count, p38 Count, p39 Count, p40 Count, p41 Count, p42 Count, p43 Count, p44 Count, p45 Count, p46 Count, p47 Count, p48 Count, p49 Count, p50 Count, p51 Count, p52 Count, p53 Count, p54 Count, p55 Count, p56 Count, p57 Count, p58 Count, p59 Count, p60 Count, p61 Count, p62 Count, p63 Count, p64 Count = 0, p65 Count = 0) returns Count
		return self.seed + p0
	end 'wide'
end 'Holder'

function main() returns ExitCode
	var holder = Holder{seed: 1}
	_ = holder.wide(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:11: Unsupported: a function with 67 argument slots — more than the 64 a call can carry. A signature's slots are its declared parameters plus the hidden ones the ABI adds (a witness per interface-typed parameter, a layout descriptor for a generic that reads one or calls a function that does, one witness per `where` constraint), and the limit is the width of the per-argument float mask that routes each one to its register file
```

<!-- test: function-overloads.error.a-static-defaulting-its-sixty-fifth-parameter-is-refused -->
<!-- process: own -->
A static with 66 parameters, the last two defaulted, is past the argument slots a call can carry, so the declaration is refused.
```maxon
type Holder
	var seed as Count

	static function wide(p0 Count, p1 Count, p2 Count, p3 Count, p4 Count, p5 Count, p6 Count, p7 Count, p8 Count, p9 Count, p10 Count, p11 Count, p12 Count, p13 Count, p14 Count, p15 Count, p16 Count, p17 Count, p18 Count, p19 Count, p20 Count, p21 Count, p22 Count, p23 Count, p24 Count, p25 Count, p26 Count, p27 Count, p28 Count, p29 Count, p30 Count, p31 Count, p32 Count, p33 Count, p34 Count, p35 Count, p36 Count, p37 Count, p38 Count, p39 Count, p40 Count, p41 Count, p42 Count, p43 Count, p44 Count, p45 Count, p46 Count, p47 Count, p48 Count, p49 Count, p50 Count, p51 Count, p52 Count, p53 Count, p54 Count, p55 Count, p56 Count, p57 Count, p58 Count, p59 Count, p60 Count, p61 Count, p62 Count, p63 Count, p64 Count = 0, p65 Count = 0) returns Count
		return p0
	end 'wide'
end 'Holder'

function main() returns ExitCode
	_ = Holder.wide(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:18: Unsupported: a function with 66 argument slots — more than the 64 a call can carry. A signature's slots are its declared parameters plus the hidden ones the ABI adds (a witness per interface-typed parameter, a layout descriptor for a generic that reads one or calls a function that does, one witness per `where` constraint), and the limit is the width of the per-argument float mask that routes each one to its register file
```

<!-- test: function-overloads.a-value-that-leaves-a-narrow-alias-binds-the-wider-alias-member-beside-a-generic -->
<!-- process: own -->
50 lies outside `Small`, so only the wider alias member and the generic fit it; the non-generic member wins and the generic serves what no alias member takes.
```maxon
typealias Small = int(0 to 10)
typealias Wide = int(0 to 1000)

function pick(n Small) returns Count
	_ = n
	return 1
end 'pick'

function pick(n Wide) returns Count
	_ = n
	return 2
end 'pick'

function pick(x T) uses T returns Count
	_ = x
	return 3
end 'pick'

function main() returns ExitCode
	print("{pick(50)} {pick("a")}\n")
	return 0
end 'main'
```
```stdout
2 3
```

<!-- test: function-overloads.a-value-that-leaves-a-narrow-alias-binds-the-wider-alias-member -->
<!-- process: own -->
Two alias members and no generic: 50 lies outside `Small`, so the call binds `Wide` rather than tying.
```maxon
typealias Small = int(0 to 10)
typealias Wide = int(0 to 1000)

function pick(n Small) returns Count
	_ = n
	return 1
end 'pick'

function pick(n Wide) returns Count
	_ = n
	return 2
end 'pick'

function main() returns ExitCode
	print("{pick(50)}\n")
	return 0
end 'main'
```
```stdout
2
```

<!-- test: function-overloads.a-call-with-a-count-argument-picks-the-count-overload-over-the-integer-one -->
<!-- process: own -->
`Count` implements `Integer`, so a `Count` argument fits both members, and the member declared over `Count` itself is the nearer one: the call binds it rather than tying. `create(n Count)` returns `7`, `create(n Integer)` would return `1007`, so exit code `7` is the `Count` member.
```maxon
typealias Integer = int(0 to 100000)
typealias Count = int(0 to 100) implements Integer

type Rec
	export var v as Integer

	static function create(n Count) returns Self
		return Self{v: n}
	end 'create'

	static function create(n Integer) returns Self
		return Self{v: n + 1000}
	end 'create'
end 'Rec'

function main() returns ExitCode
	let y = Rec.create(7 as Count)
	return y.v as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: function-overloads.a-call-with-a-float-alias-argument-picks-the-member-over-that-alias-rather-than-its-ancestor -->
<!-- process: own -->
The same ranking for a float alias: `Fine` implements `Coarse`, and a `Fine` argument binds the member declared over `Fine`.
```maxon
typealias Coarse = float(0.0 to 100.0)
typealias Fine = float(0.0 to 1.0) implements Coarse

function rank(x Coarse) returns ExitCode
	_ = x
	return 2
end 'rank'

function rank(x Fine) returns ExitCode
	_ = x
	return 1
end 'rank'

function main() returns ExitCode
	return rank(0.5 as Fine)
end 'main'
```
```exitcode
1
```

<!-- test: function-overloads.a-call-with-an-aliased-argument-picks-the-member-over-that-alias-over-an-int-member -->
<!-- process: own -->
An argument's own alias outranks an exact primitive fit: `Integer` admits every `int`, so a `Small` argument fits it as an `int`, and the call still binds the member declared over `Small`. Exit code `1` is the `Small` member.
```maxon
typealias Small = int(0 to 10)
typealias Integer = int(i64.min to i64.max)

function f(n Small) returns ExitCode
	_ = n
	return 1
end 'f'

function f(n Integer) returns ExitCode
	_ = n
	return 2
end 'f'

function main() returns ExitCode
	let x = 5 as Small
	return f(x)
end 'main'
```
```exitcode
1
```

<!-- test: function-overloads.a-float-alias-parameter-takes-a-float-and-leaves-an-integer-to-the-other-member -->
<!-- process: own -->
A float alias parameter is read the same way from the declaration and from the argument: `0.5` binds `Ratio`, `3` binds the `Count` member.
```maxon
typealias Ratio = float(0.0 to 1.0)

function show(r Ratio) returns Count
	_ = r
	return 1
end 'show'

function show(n Count) returns Count
	_ = n
	return 2
end 'show'

function main() returns ExitCode
	print("{show(0.5)} {show(3)}\n")
	return 0
end 'main'
```
```stdout
1 2
```

<!-- test: function-overloads.a-conditional-interface-extension-is-skipped-for-a-conformer-that-fails-its-clause -->
<!-- process: own -->
The extension adds `same(other Element)` only where `Element` is `Equatable`; `Crate` binds `Element` to a type that is not, so the extension member is skipped and the conformer's own `same(n Count)` is the only one its call can mean.
```maxon
interface Holds uses Element
	function first() returns Element
end 'Holds'

extension Holds where Element is Equatable
	function same(other Element) returns bool
		return self.first() == other
	end 'same'
end 'Holds'

type Tag
	let id as Count

	static function create(id Count) returns Self
		return Self{id: id}
	end 'create'
end 'Tag'

type Crate implements Holds with Tag
	let tag as Tag

	static function create() returns Self
		return Self{tag: Tag.create(1)}
	end 'create'

	function first() returns Tag
		return self.tag
	end 'first'

	function same(n Count) returns bool
		return n == 7
	end 'same'
end 'Crate'

function main() returns ExitCode
	let c = Crate.create()
	if c.same(7) 'yes'
		return 1
	end 'yes'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: function-overloads.a-conditional-interface-extension-yields-to-the-conformers-own-method-where-its-clause-holds -->
<!-- process: own -->
The same shape with a conformer whose `Element` is `Equatable`: the clause holds, and the conformer's own `same(label String)` still shadows the extension's `same(other Element)`, so the call means the conformer's.
```maxon
interface Holds uses Element
	function first() returns Element
end 'Holds'

extension Holds where Element is Equatable
	function same(other Element) returns bool
		return self.first() == other
	end 'same'
end 'Holds'

type Crate implements Holds with Count
	let value as Count

	static function create(value Count) returns Self
		return Self{value: value}
	end 'create'

	function first() returns Count
		return self.value
	end 'first'

	function same(label String) returns bool
		return label.count() == 3
	end 'same'
end 'Crate'

function main() returns ExitCode
	let c = Crate.create(4)

	if c.same("abc") 'byLabel'
		return 2
	end 'byLabel'

	return 0
end 'main'
```
```exitcode
2
```

<!-- test: error.an-interface-constrained-call-sees-the-conformers-private-member-in-the-pool-it-scores -->
<!-- process: own -->
The conformer's file-private `show(n Count)` and exported `show(n Whole)` are both nameable from an interface-constrained generic that calls through `Showy`, so the literal 5 fits both and the call is ambiguous.
```maxon
// --- file: a.maxon
export typealias Whole = int(i64.min to i64.max)

export interface Showy
	function show(n Whole) returns Whole
end 'Showy'

export type Gadget implements Showy
	var seen as Whole

	export static function create() returns Self
		return Self{seen: 0}
	end 'create'

	function show(n Count) returns Whole
		return 1
	end 'show'

	export function show(n Whole) returns Whole
		return 2
	end 'show'
end 'Gadget'

// --- file: main.maxon
function run(x T) uses T returns Whole where T is Showy
	return x.show(5)
end 'run'

function main() returns ExitCode
	let g = Gadget.create()
	return run(g) as ExitCode
end 'main'
```
```maxoncstderr
error E3007: <fragment>:27:11: Ambiguous overload for 'show': multiple overloads match. Candidates: (n Count), (n Whole)
```

<!-- test: error.a-user-extension-of-int-declaring-tostring-replaces-the-stdlibs-and-is-refused-by-its-conformance -->
<!-- process: own -->
A user `extension int` that declares `toString(pad Count)` takes the name from the stdlib's `extension int implements Stringable`, so the conformance no longer finds the stdlib's `toString()` and the build is refused rather than the two forming one overload set.
```maxon
extension int
	function toString(pad Count) returns String
		return "{self}/{pad}"
	end 'toString'
end 'int'

function main() returns ExitCode
	let n = 21
	print("{n.toString()} {n.toString(3)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3016: stdlib/PrimitiveExtensions.maxon:17:11: Partial interface implementation: type 'int' has 1 method(s) with wrong signature:
  - toString(pad Count) returns String (expected toString() returns String)
```

<!-- test: a-type-and-a-directory-of-one-name-keep-their-own-overload-sets -->
<!-- process: own -->
`Shape` is both a type with two `area` methods and a directory whose two files each declare a free `area`; the directory's cross-file contest does not rename the type's methods, and each call finds its own set: 9 + 6 + 4 + 3.
```maxon
// --- file: Shape/one.maxon
export function area(n Count) returns Count
	return n
end 'area'

export function viaTwo() returns Count
	return area("abc")
end 'viaTwo'

// --- file: Shape/two.maxon
export function area(s String) returns Count
	return s.count()
end 'area'

export function viaOne() returns Count
	return area(4)
end 'viaOne'

// --- file: main.maxon
type Shape
	let side as Count

	static function create(side Count) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Count
		return self.side * self.side
	end 'area'

	function area(scale Count) returns Count
		return self.side * scale
	end 'area'
end 'Shape'

function main() returns ExitCode
	let s = Shape.create(3)
	return (s.area() + s.area(2) + Shape.viaOne() + Shape.viaTwo()) as ExitCode
end 'main'
```
```exitcode
22
```
