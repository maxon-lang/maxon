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

<!-- test: error.overloads-disagree-on-returning-a-value-at-all -->
<!-- W49 wave 4. THE ONE QUADRANT OF THE RETURN-TYPE DISAGREEMENT NOTHING GUARDED, AND IT REACHED THE
MACHINE. The declaration sweep records ONE return type per NAME and is LAST-WINS, so here it records the
`String` of the second declaration; `mintCallResult` then types EVERY call to `emit` as a String and
`enrolOwnedCallTemp` enrols the scope-exit drop that a managed result owes. This call resolves to the VOID
member a whole pass later, so the drop is spent on a register the callee never wrote. MEASURED before the
refusal existed: this exact program compiled clean and died with an access violation (0xC0000005). The
mirror direction — void recorded, a value member called — was already refused, which is the case below. -->
```maxon
function emit(flag bool)
	print("flag {flag}")
end 'emit'

function emit(tag int) returns String
	return "tag{tag}"
end 'emit'

function main() returns ExitCode
	emit(true)
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/function-overloads/error.overloads-disagree-on-returning-a-value-at-all.test:11:2: the overloads of 'emit' do not agree on their return type ('String' and 'void'), and this call needed the one they disagree about. A call's result type is fixed while its file is parsed, from a whole-program index that records one return type per NAME, so only a difference between plain scalars can be corrected once the overload is known. Make the overloads return the same type
```

<!-- test: error.overloads-disagree-with-the-void-member-recorded -->
<!-- The MIRROR of the case above, and the NEGATIVE CONTROL for it: the sweep records the void member (it
is declared last), so the call to the value member is typed `void`, no drop is enrolled, and the `+1` the
`String` member returns would LEAK. This direction was already refused; it is pinned so that the pair is
one guarded fact rather than one guarded half. -->
```maxon
function emit(tag int) returns String
	return "tag{tag}"
end 'emit'

function emit(flag bool)
	print("flag {flag}")
end 'emit'

function main() returns ExitCode
	emit(1)
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/function-overloads/error.overloads-disagree-with-the-void-member-recorded.test:11:2: the overloads of 'emit' do not agree on their return type ('void' and 'String'), and this call needed the one they disagree about. A call's result type is fixed while its file is parsed, from a whole-program index that records one return type per NAME, so only a difference between plain scalars can be corrected once the overload is known. Make the overloads return the same type
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
