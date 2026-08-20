---
feature: interface-conformance
status: stable
keywords: [interface, conformance, implements, type-checking]
category: type-system
---

# Interface Conformance Checking

## Documentation

### Declaring Interface Conformance

Types declare interface conformance using the `implements` keyword. The type must implement all methods declared by the interface:

```text
interface Printable
  function toString() returns int
end 'Printable'

type MyType implements Printable
  function toString() returns int
    return 42
  end 'toString'
end 'MyType'
```

### Multiple Interface Conformance

Types can conform to multiple interfaces:

```text
type MyType implements Interface1, Interface2
  // must implement all methods from both interfaces
end 'MyType'
```

### Conformance Errors

If a type declares conformance but doesn't implement all required methods, a compile error is reported:

```text
interface Counter
  function get() returns int
  function increment()
end 'Counter'

type BadCounter implements Counter
  function get() returns int
    return 0
  end 'get'
  // ERROR: missing 'increment' method
end 'BadCounter'
```

### Throws Conformance and the Abstract `Error` Requirement

An interface method's `throws` clause fixes the ABI of every witness dispatch of that method, so an
implementation's own clause must agree with it. Naming the SAME type always agrees. Beyond that, one
relaxation is sound, and it is the one `Error` exists for:

- **A requirement whose `throws` names an INTERFACE — `throws Error` — is satisfied by an implementation
  that throws its own concrete error type.** `Error` is a marker interface: it declares no case, so a
  `try` at the dispatch has nothing to decode and binds an opaque scalar. There is no ordinal to get
  wrong, and the implementation is free to be more specific.
- **The relaxation stops at the flag SHAPE.** A payload-carrying (heap-boxed) union hands its error over
  as a BOX POINTER, while a requirement that decodes opaquely is caught through the scalar
  `ordinal + bias` ABI — the pointer would be decoded as an ordinal and its box never released. An
  implementation throwing a boxed union under an abstract requirement is therefore still refused.
- **It is one-directional.** A requirement naming a CONCRETE error type still demands that exact type: an
  implementation declaring the abstract `throws Error` under `throws DigestError` is a WIDENING, and the
  dispatch would decode whatever it threw as a `DigestError`.
- **An unresolvable requirement type is not abstract, it is a mistake**, and still demands the same name.
- **And the same holds of the IMPLEMENTATION's type.** The relaxation is granted only to an error type
  whose flag shape the compiler can actually see, so an implementation whose `throws` names no declared
  enum or union — a typo, or a struct — is refused rather than waved through: "the compiler found no
  entry for it" is not evidence that its flag is a scalar.

## Tests

<!-- test: conformance-basic -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Counter
	function get() returns Integer
	function increment()
end 'Counter'

type SimpleCounter implements Counter
	var value as Integer

	function get() returns Integer
		return value
	end 'get'

	function increment()
		value = value + 1
	end 'increment'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'SimpleCounter'

function main() returns ExitCode
	var c = SimpleCounter.create(40)
	c.increment()
	c.increment()
	return c.get()
end 'main'
```
```exitcode
42
```

<!-- test: conformance-multiple-interfaces -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Readable
	function read() returns Integer
end 'Readable'

interface Writable
	function write(value Integer)
end 'Writable'

type Buffer implements Readable, Writable
	var data as Integer

	function read() returns Integer
		return data
	end 'read'

	function write(value Integer)
		data = value
	end 'write'

	static function create(data Integer) returns Self
		return Self{data: data}
	end 'create'
end 'Buffer'

function main() returns ExitCode
	var buf = Buffer.create(0)
	buf.write(42)
	return buf.read()
end 'main'
```
```exitcode
42
```

<!-- test: conformance-missing-method -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Counter
	function get() returns Integer
	function increment()
end 'Counter'

type BadCounter implements Counter
	let value as Integer

	function get() returns Integer
		return value
	end 'get'
end 'BadCounter'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/interface-conformance/conformance-missing-method.test:10:6: Partial interface implementation: type 'BadCounter' is missing 1 method(s):
  - increment() returns void
```

<!-- test: conformance-wrong-param-type -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

interface Processor
	function process(value Integer) returns Integer
end 'Processor'

type BadProcessor implements Processor
	function process(value Float) returns Integer
		return 0
	end 'process'
end 'BadProcessor'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/interface-conformance/conformance-wrong-param-type.test:10:6: Partial interface implementation: type 'BadProcessor' has 1 method(s) with wrong signature:
  - process(value Float) returns Integer (expected process(value Integer) returns Integer)
```

<!-- test: conformance-wrong-return-type -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

interface Provider
	function provide() returns Integer
end 'Provider'

type BadProvider implements Provider
	function provide() returns Float
		return 0.0
	end 'provide'
end 'BadProvider'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/interface-conformance/conformance-wrong-return-type.test:10:6: Partial interface implementation: type 'BadProvider' has 1 method(s) with wrong signature:
  - provide() returns Float (expected provide() returns Integer)
```

<!-- test: conformance-extra-methods-ok -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Simple
	function getValue() returns Integer
end 'Simple'

type Extended implements Simple
	let value as Integer

	function getValue() returns Integer
		return value
	end 'getValue'

	function extraMethod() returns Integer
		return 100
	end 'extraMethod'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Extended'

function main() returns ExitCode
	let e = Extended.create(42)
	return e.getValue()
end 'main'
```
```exitcode
42
```

<!-- test: conformance-no-interface -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Standalone
	var value as Integer

	function get() returns Integer
		return value
	end 'get'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Standalone'

function main() returns ExitCode
	let s = Standalone.create(42)
	return s.get()
end 'main'
```
```exitcode
42
```

<!-- test: conformance-alias-crossing -->
An interface and a conforming impl may reach a shared type through DIFFERENT ranged typealiases. A ranged
alias resolves to its underlying primitive (the range is dropped from the signature type, enforced by range
checks instead), so `A` and `B` — both `int(0 to 100)` — are ONE type and the conformance is valid, not a
spurious wrong-signature. Regression: the check compared alias NAMES, which false-rejected this valid program.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias A = int(0 to 100)
typealias B = int(0 to 100)

interface Processor
	function process(value A) returns Integer
end 'Processor'

type Widget implements Processor
	var v as Integer

	function process(value B) returns Integer
		return value
	end 'process'

	static function create() returns Self
		return Self{v: 0}
	end 'create'
end 'Widget'

function main() returns ExitCode
	var w = Widget.create()
	return w.process(7)
end 'main'
```
```exitcode
7
```

<!-- disabled-test: builtin-interface-user-code -->
<!-- P1.7a-s2: generic type params (`uses Element`) + `__ManagedMemory` + the `BuiltinArrayLiteral` builtin interface -->
```maxon
type MyCollection uses Element implements BuiltinArrayLiteral
	var managed as __ManagedMemory

	static function init(managed __ManagedMemory) returns Self
		return MyCollection{managed: managed}
	end 'init'
end 'MyCollection'

function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: interface-method-unused-param-allowed -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet(volume Integer) returns Integer
end 'Greeter'

type Silent implements Greeter
	let value as Integer

	function greet(volume Integer) returns Integer
		return value
	end 'greet'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Silent'

function callGreet(g Greeter) returns Integer
	return g.greet(99)
end 'callGreet'

function main() returns ExitCode
	let s = Silent.create(42)
	return callGreet(s)
end 'main'
```
```exitcode
42
```


<!-- test: interface-method-via-extended-interface -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Base
	function ping(payload Integer) returns Integer
end 'Base'

interface Extended extends Base
	function other() returns Integer
end 'Extended'

type Impl implements Extended
	let n as Integer

	function ping(payload Integer) returns Integer
		return n
	end 'ping'

	function other() returns Integer
		return n + 1
	end 'other'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Impl'

function callPing(b Base) returns Integer
	return b.ping(7)
end 'callPing'

function main() returns ExitCode
	let i = Impl.create(5)
	return callPing(i)
end 'main'
```
```exitcode
5
```


<!-- test: interface-method-may-leave-a-required-parameter-unread -->
⭐ **THE POSITIVE HALF OF THE UNUSED-PARAMETER WAIVER** (`unused-parameters`); the negative half is
`non-interface-method-on-conforming-type-still-errors`, directly below. An implementer is forced to declare
every parameter the CONTRACT names, so a method satisfying a requirement is exempt from E3012 even when its
own implementation has no use for one — `Quiet.greet` ignores `volume` entirely and must still compile.
Without the waiver this is `unused variable: 'volume'`, and `_` is not available to fix it: the name is the
interface's to choose, not the implementer's.

The receiver is CONCRETE (`q.greet(7)`), the shape `conformance-basic` uses — no interface-typed parameter
is involved, so this does not wait on existentials.
```maxon

typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet(volume Integer) returns Integer
end 'Greeter'

type Quiet implements Greeter
	let value as Integer

	function greet(volume Integer) returns Integer
		return value
	end 'greet'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Quiet'

function main() returns ExitCode
	let q = Quiet.create(42)
	return q.greet(7)
end 'main'
```
```exitcode
42
```

<!-- test: non-interface-method-on-conforming-type-still-errors -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet() returns Integer
end 'Greeter'

type Hello implements Greeter
	let value as Integer

	function greet() returns Integer
		return value
	end 'greet'

	function helper(unused Integer) returns Integer
		return value
	end 'helper'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Hello'

function main() returns ExitCode
	let h = Hello.create(1)
	return h.helper(5)
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/interface-conformance/non-interface-method-on-conforming-type-still-errors.test:16:18: unused variable: 'unused'
```


<!-- test: interface-method-local-var-still-errors -->
<!-- E3012 exists for PARAMETERS (ff9c825fa) but not yet for LOCALS, and this case asserts the LOCAL half — an unused `let` inside an interface method, which the waiver must not cover. Unblocked by `unused-variables.md` (whitelist 170), not by this file. -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet(volume Integer) returns Integer
end 'Greeter'

type Silent implements Greeter
	let value as Integer

	function greet(volume Integer) returns Integer
		let unusedLocal = 99
		return value
	end 'greet'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Silent'

function main() returns ExitCode
	let s = Silent.create(1)
	return s.greet(0)
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/interface-conformance/interface-method-local-var-still-errors.test:13:7: unused variable: 'unusedLocal'
```

<!-- test: interface-method-loop-variable-still-errors -->
⭐ **THE WAIVER IS ABOUT PARAMETERS, AND A `for` BINDING IS NOT ONE** (A4g). `interface-method-may-leave-a-required-parameter-unread` above is the positive control that the waiver is live at all; this is the line it stops at. A contract can force an implementer to DECLARE a parameter it has no use for, and it has nothing whatever to say about a loop variable the author wrote inside the body — so the loop binding is still refused, and `for _ in` is the spelling that fixes it.

MEASURED on the runnable oracle, which draws the same line structurally: its `skipParamCheck` skips the parameter loop and never the locals loop, and it reports `unused variable: 'i'` on this program.

⚠ **`limit` IS ALSO UNREAD, DELIBERATELY.** The two unused declarations in one method are what make this case discriminating in BOTH directions: it fails if the waiver is allowed to reach the loop binding, and it fails again if a waived PARAMETER is allowed to end the scan before the loop binding is reached.
```maxon

typealias Integer = int(i64.min to i64.max)

interface Counter
	function tally(limit Integer) returns Integer
end 'Counter'

type Impl implements Counter
	let value as Integer

	function tally(limit Integer) returns Integer
		var total = 0
		for i in 0 upto 3 'l'
			total = total + 1
		end 'l'
		return total + value
	end 'tally'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Impl'

function main() returns ExitCode
	let c = Impl.create(1)
	return c.tally(3)
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/interface-conformance/interface-method-loop-variable-still-errors.test:14:7: unused variable: 'i'
```

<!-- test: interface-impl-ignore-param-name -->
An interface-implementing method may name a parameter `_` (the ignore name) even
when the interface declares it with a real name. The impl doesn't use the
argument; callers through the interface still pass it by the interface's name,
which they can see, so an `_` impl param satisfies any expected name — it can
never be the bound name. Without this carve-out the conformance check reports a
spurious E3016 "wrong signature" (`greet(_ bool)` vs `greet(loud bool)`).
Returns `7`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet(loud bool) returns Integer
end 'Greeter'

type Quiet implements Greeter
	let value as Integer

	function greet(_ bool) returns Integer
		return value
	end 'greet'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Quiet'

function useGreeter(g Greeter) returns Integer
	return g.greet(loud: true)
end 'useGreeter'

function main() returns ExitCode
	let q = Quiet.create(7)
	return useGreeter(q)
end 'main'
```
```exitcode
7
```

<!-- test: error.implements-unknown-interface -->
An `implements` clause naming an interface that does not exist is E3015, positioned at the type name.
shv2 DIVERGES from the C# bootstrap here (which silently accepts an unknown interface) toward v1, which
emits E3015: a typo'd interface name should not silently pass.
```maxon

typealias Integer = int(i64.min to i64.max)

type Widget implements Drawable
	let size as Integer

	static function create(size Integer) returns Self
		return Self{size: size}
	end 'create'
end 'Widget'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3015: <fragment>:5:6: type 'Widget' implements unknown interface 'Drawable'
```

<!-- test: error.partial-builtin-interface -->
A struct declaring conformance to a stdlib protocol interface (synthesized by the compiler) without
supplying its method is the same E3016 as a user interface — here `Stringable` without `toString`.
```maxon

typealias Integer = int(i64.min to i64.max)

type Thing implements Stringable
	var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Thing'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:5:6: Partial interface implementation: type 'Thing' is missing 1 method(s):
  - toString() returns String
```

<!-- test: overloaded-method-on-conforming-type -->
A conforming type may OVERLOAD the method it conforms with: `label()` satisfies `Named`, and
`label(extra Integer)` registers beside it as a distinct member (D7). It was `E3006 duplicate definition of
function 'Widget.label'` until this rung, because shv2 keyed a method by its bare `Type.method` name alone —
which is also what this case originally existed to pin, and the ORACLE has always accepted the program.

⚠ **IT DOES NOT KEEP GUARDING THE REGRESSION IT WAS WRITTEN FOR. The case BELOW is what does.** The original
guarded a PANIC on the REFUSAL path, and converting a negative test into a positive one is exactly how such a
guard is lost: the conformance check reads a method's param TYPES from the module and its param NAMES from the
signature registry, those were two independent resolutions of ONE collision, they disagreed on arity, and the
check indexed one column by the other's count. What prevents it is `ConformanceCheck.checkConformance`'s
`projectHasErrors` gate — and THIS program has no diagnostic at all, so it never reaches that gate.
`formatActualSignature`, the function that panicked, is reached only from the E3016 mismatch arm, and here
`label()` matches `Named.label()` exactly. **Delete the gate and this test stays GREEN.**

Nor are the two halves still played off each other: under D7 the second method registers under a MANGLED name,
so the module's function map and `funcSignatures` hold two DISTINCT keys and cannot disagree about one. Only an
IDENTICAL-signature duplicate still collides. This case is therefore kept purely as D7 acceptance — the
assertion is that it compiles and runs — and the guard is restored by
`error.duplicate-method-conformance-same-signature` below.
```maxon

typealias Integer = int(i64.min to i64.max)

interface Named
	function label() returns Integer
end 'Named'

type Widget implements Named
	var v as Integer

	function label() returns Integer
		return v
	end 'label'

	function label(extra Integer) returns Integer
		return v + extra
	end 'label'

	static function create() returns Self
		return Self{v: 0}
	end 'create'
end 'Widget'

function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.duplicate-method-conformance-same-signature -->
⭐⭐ **THE ONE DUPLICATE D7 STILL REFUSES — and the only program left that reaches the conformance check's
malformed-module gate.** Overloading resolves a collision by MANGLING the later member's registration name, so
two methods of one name are two distinct keys unless their signatures are IDENTICAL too; then
`Parser.overloadRegistrationNameFor` hands back the INCUMBENT'S OWN registration name on purpose and it
collides in `commitFuncSignatures`, earning the E3006 it always did.

⚠ **THAT NAME IS BARE ONLY WHEN THE INCUMBENT IS THE FIRST MEMBER OF ITS SET** — which is the case here, and
is why this test reads `'Widget.label'`. Redeclare a LATER member and the name handed back is the incumbent's
MINTED one (`Parser.overloadMemberHoldingSignature`), which is a symbol no declaration wrote; E3006 then says
so rather than quoting it bare, and `function-overloads/error.overload-redeclared-with-the-same-parameters`
pins that half. Stated because the earlier wording said "the BARE name" flatly, which is true of this program
and false of the mechanism.

⚠ **D7 DID NOT LOSE THE OLD PANIC GUARD — IT MADE THE PANIC UNREACHABLE, AND THAT IS A STRONGER OUTCOME THAN A
TEST.** The panic needed TWO things: a name collision, AND the module's function table and `funcSignatures`
disagreeing about the colliding method's ARITY (`checkOneMethod` reads param TYPES from the former and param
NAMES from the latter, so it indexed one column by the other's count). Its predecessor supplied both with
`label()` beside `label(extra Integer)`. Post-D7 a collision survives ONLY when the two signatures are
IDENTICAL — anything else mangles — so the arities necessarily AGREE and the disagreement cannot be
constructed. The second premise is gone, not merely untested.

⚠ **MEASURED, and it is why this note does not claim to be that guard:** stubbing
`ConformanceCheck.checkConformance`'s `projectHasErrors` early-return out entirely leaves the suite at
**2581 passed / 0 failed**. Nothing here exercises that gate — not this case and not any other. It is retained
as defence-in-depth for a malformed module arriving by some other route (any diagnostic, not just E3006), and a
reader should know it is unexercised rather than assume this case covers it. What THIS case pins is the D7
boundary itself: the one duplicate shape the rung still refuses, refused with a clean diagnostic.
```maxon

typealias Integer = int(i64.min to i64.max)

interface Named
	function label() returns Integer
end 'Named'

type Widget implements Named
	var v as Integer

	function label() returns Integer
		return v
	end 'label'

	function label() returns Integer
		return v + 1
	end 'label'

	static function create() returns Self
		return Self{v: 0}
	end 'create'
end 'Widget'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:16:11: Duplicate function 'Widget.label'
```

### `static` interface requirements (R9)

An `interface` may declare a `static function` requirement. Until R9 the conformance check SKIPPED every
such requirement, so a type could declare `implements` and supply nothing for it. That was not a lenience,
it was a disagreement: shv2 dispatches an interface through a WITNESS TABLE with one slot per interface
method — statics included — and the slot is stamped with a relocation naming `<Type>.<method>`. A conformer
that supplied nothing left the linker resolving an address for a function nobody emitted, and the build
died in `bakeFuncAbs64Relocs`, not in a diagnostic.

⚠ **THE RULE IS THE ONE E3016 ALREADY STATES, AND IT DOES NOT CONSULT THE WITNESS TABLE.** A type that does
not define all of an interface's members does not conform to it — whether or not any generic in the program
happens to instantiate against that interface. Making the table's existence decide conformance would leave
the same program accepted or rejected depending on a `typealias` written elsewhere, which is exactly the
two-components-disagreeing shape this rule closes.

⚠ **DIVERGENCE FROM THE C# BOOTSTRAP, DELIBERATE AND MEASURED.** The bootstrap ACCEPTS a conformer that
omits a static requirement (measured: exit 42) — it monomorphizes and has no witness tables at all, so the
question cannot arise there. v1 already reports a MISSING static (`SemanticCheck.maxon:412-430` pushes the
missing entry before its static skip); it skips only a PRESENT static's signature, which shv2 cannot afford
because shv2's slot carries an address whose ABI the interface picked.

<!-- test: error.static-requirement-not-supplied -->
A `static` requirement the conformer does not supply is E3016 — the program that used to panic the
compiler in `bakeFuncAbs64Relocs`.
```maxon
typealias Code = int(0 to u32.max)

interface Wide
	static function tag() returns Code
	function digest() returns Code
end 'Wide'

type Point implements Wide
	export var x as Code

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export function digest() returns Code
		return self.x
	end 'digest'
end 'Point'

type Box uses T where T is Wide
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function run() returns Code
		return self.item.digest()
	end 'run'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let p = Point.create(42)
	let b = PointBox.create(p)
	return b.run()
end 'main'
```
```maxoncstderr
error E3016: <fragment>:9:6: Partial interface implementation: type 'Point' is missing 1 method(s):
  - static tag() returns Code
```

<!-- test: error.static-requirement-not-supplied-without-witness -->
⭐ **WHETHER A WITNESS TABLE IS BUILT MUST NOT DECIDE WHETHER A TYPE CONFORMS.** The identical conformance
with no generic instantiation anywhere in the program — so no witness table, no relocation, nothing that
could fail at link time — is the SAME E3016. Before R9 this program compiled and returned 42.
```maxon
typealias Code = int(0 to u32.max)

interface Wide
	static function tag() returns Code
	function digest() returns Code
end 'Wide'

type Point implements Wide
	export var x as Code

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export function digest() returns Code
		return self.x
	end 'digest'
end 'Point'

function main() returns ExitCode
	let p = Point.create(42)
	return p.digest()
end 'main'
```
```maxoncstderr
error E3016: <fragment>:9:6: Partial interface implementation: type 'Point' is missing 1 method(s):
  - static tag() returns Code
```

<!-- test: static-requirement-supplied -->
⭐⭐ **THE OVER-REJECTION GUARD.** A conformer that DOES supply the static compiles and runs — and it is the
case a careless fix breaks, because checking a static requirement against the type's INSTANCE members
rejects this program while still rejecting the two above. The witness table is built here, so its `tag`
slot relocates against a `Point.tag` that exists.
```maxon
typealias Code = int(0 to u32.max)

interface Wide
	static function tag() returns Code
	function digest() returns Code
end 'Wide'

type Point implements Wide
	export var x as Code

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export static function tag() returns Code
		return 7
	end 'tag'

	export function digest() returns Code
		return self.x
	end 'digest'
end 'Point'

type Box uses T where T is Wide
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function run() returns Code
		return self.item.digest()
	end 'run'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let p = Point.create(42)
	let b = PointBox.create(p)
	return b.run()
end 'main'
```
```exitcode
42
```

<!-- test: error.static-requirement-wrong-signature -->
A supplied static whose signature disagrees with the requirement reaches E3016's WRONG-SIGNATURE arm, the
same arm an instance method reaches. v1 skips this comparison because *"no runtime witness is dispatched
against them"*; under shv2's dictionary-passing one is, so the shapes must agree.
```maxon
typealias Code = int(0 to u32.max)

interface Wide
	static function tag() returns Code
	function digest() returns Code
end 'Wide'

type Point implements Wide
	export var x as Code

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export static function tag(extra Code) returns Code
		return extra
	end 'tag'

	export function digest() returns Code
		return self.x
	end 'digest'
end 'Point'

function main() returns ExitCode
	let p = Point.create(42)
	return p.digest()
end 'main'
```
```maxoncstderr
error E3016: <fragment>:9:6: Partial interface implementation: type 'Point' has 1 method(s) with wrong signature:
  - static tag(extra Code) returns Code (expected static tag() returns Code)
```

<!-- test: error.static-requirement-supplied-as-instance-method -->
⭐ **A RECEIVER-KIND DISAGREEMENT IS A SIGNATURE DISAGREEMENT (R9's own rule — neither reference has it).**
An instance method carries `__self` at position 0 and a static does not, so an instance impl installed in a
slot the interface declared static would be dispatched with no receiver in the register it reads `self`
from. The `static ` prefix is in the rendered signature precisely so this rejection's two halves do not
print as the same string.
```maxon
typealias Code = int(0 to u32.max)

interface Wide
	static function tag() returns Code
	function digest() returns Code
end 'Wide'

type Point implements Wide
	export var x as Code

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export function tag() returns Code
		return self.x
	end 'tag'

	export function digest() returns Code
		return self.x
	end 'digest'
end 'Point'

function main() returns ExitCode
	let p = Point.create(42)
	return p.digest()
end 'main'
```
```maxoncstderr
error E3016: <fragment>:9:6: Partial interface implementation: type 'Point' has 1 method(s) with wrong signature:
  - tag() returns Code (expected static tag() returns Code)
```

<!-- test: error.instance-requirement-supplied-as-static-method -->
The other direction, which was a SILENT WRONG ANSWER before R9: an INSTANCE requirement met by a static was
accepted, and the dispatch then passed a receiver into a callee with no `self` parameter.
```maxon
typealias Code = int(0 to u32.max)

interface Wide
	function digest() returns Code
end 'Wide'

type Point implements Wide
	export var x as Code

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export static function digest() returns Code
		return 7
	end 'digest'
end 'Point'

function main() returns ExitCode
	let p = Point.create(42)
	return p.x
end 'main'
```
```maxoncstderr
error E3016: <fragment>:8:6: Partial interface implementation: type 'Point' has 1 method(s) with wrong signature:
  - static digest() returns Code (expected digest() returns Code)
```

### An OVERLOADED member may satisfy a requirement, whatever order it is written in (R10)

Conformance used to resolve a requirement by the BARE `Type.method` key alone, and under D7 only the
FIRST-declared overload of a name registers under that key — so **a conforming type was accepted or
rejected according to the order its members happened to be written in.** The requirement is now matched
against every member of the name's overload set (`project.overloadSets`) through the same
`signatureMatches` a single member goes through, and the requirement is satisfied when EXACTLY ONE matches.

⚠⚠ **THE SELECTION AND THE WITNESS SLOT ARE ONE VALUE, NOT TWO LOOKUPS THAT AGREE.**
`LowerMaxonToStd.ensureWitnessTable` stamps each slot's `funcAbs64InRdata` relocation with an impl symbol,
and it used to mint the same bare join independently. That was harmless only because it was COUPLED to the
bug — both sites were wrong the same way, so they agreed. Teaching conformance to accept a mangled member
without moving the slot would have converted a loud false reject into a **silent wrong dispatch**: the
witness would carry the address of whichever overload was written first. So conformance RECORDS what it
selected (`project.witnessSlotImpls`) and the table READS that recording; a slot with no recording is a
compiler-internal disagreement and panics, except for a builtin conformer, whose impls are synthesized one
per `(conformer, method)` and can never be overloaded.

<!-- test: overloaded-method-satisfies-requirement-declared-second -->
⭐ **THE RUNG, at its smallest: the same program as `overloaded-method-on-conforming-type` with the two
members SWAPPED.** `label(extra Integer)` is written first and takes the bare `Widget.label` registration;
`label()` — the one `Named` requires — registers as `Widget.label#`. Before R10 this was
`E3016 … has 1 method(s) with wrong signature: - label(extra Integer) returns Integer (expected label()
returns Integer)` — a rejection of a type that supplies the method, naming the member that is not the
candidate, decided purely by declaration order.
```maxon

typealias Integer = int(i64.min to i64.max)

interface Named
	function label() returns Integer
end 'Named'

type Widget implements Named
	var v as Integer

	function label(extra Integer) returns Integer
		return v + extra
	end 'label'

	function label() returns Integer
		return v
	end 'label'

	static function create() returns Self
		return Self{v: 0}
	end 'create'
end 'Widget'

function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: overloaded-method-dispatched-through-witness -->
⭐⭐ **THE DISPATCH CONTROL, and it is the case that matters most in this rung.** Every case here that does
not build a witness table would still pass if conformance were fixed and the table left minting the bare
name — the program would compile, and dispatch to the WRONG overload with no diagnostic. This one calls the
requirement THROUGH the witness (`self.item.label()` inside `Box uses T where T is Labeled`) and asserts a
value the two overloads disagree about: the requirement's `label()` answers **42** and the bare-named
`label(extra Code)` answers 7.

⚠ **MEASURED, and it is the reason this case exists:** reverting `ensureWitnessTable`'s half alone —
conformance still accepting the mangled member — leaves the whole rest of this suite green while this
program returns **7**, silently. Two other cases catch that revert as well, one of them differently: the
two-interface case below answers 7 where 40 is correct, and the overloaded-STATIC case — which has no
runtime observation at all — moves its golden fragment.
```maxon
typealias Code = int(0 to u32.max)

interface Labeled
	function label() returns Code
end 'Labeled'

type Tag implements Labeled
	export var x as Code

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export function label(extra Code) returns Code
		return 7
	end 'label'

	export function label() returns Code
		return 42
	end 'label'
end 'Tag'

type Box uses T where T is Labeled
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function run() returns Code
		return self.item.label()
	end 'run'
end 'Box'

typealias TagBox = Box with Tag

function main() returns ExitCode
	let t = Tag.create(1)
	let b = TagBox.create(t)
	return b.run()
end 'main'
```
```exitcode
42
```

<!-- test: overloaded-method-satisfies-two-interfaces-one-name -->
⭐ **WHY THE RECORDED SELECTION IS KEYED BY THE INTERFACE AND NOT ONLY BY THE METHOD.** `First` requires
`label()` and `Second` requires `label(extra Code)`; one type satisfies both, with one overload each. The
two witness tables must therefore carry DIFFERENT symbols in the same-named slot — `__witness_Tag.First`
the 0-argument member, `__witness_Tag.Second` the 1-argument one. A selection keyed by the method name
alone would let the second conformance overwrite the first's and the dispatch below would answer 7.
This shape is not hypothetical: `stdlib/Interfaces.maxon` declares `Stringable.toString()` beside
`FormattedStringable.toString(format String)`.
```maxon
typealias Code = int(0 to u32.max)

interface First
	function label() returns Code
end 'First'

interface Second
	function label(extra Code) returns Code
end 'Second'

type Tag implements First, Second
	export var x as Code

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export function label(extra Code) returns Code
		return 7
	end 'label'

	export function label() returns Code
		return 40
	end 'label'
end 'Tag'

type Box uses T where T is First and Second
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function run() returns Code
		return self.item.label()
	end 'run'
end 'Box'

typealias TagBox = Box with Tag

function main() returns ExitCode
	let t = Tag.create(1)
	let b = TagBox.create(t)
	return b.run()
end 'main'
```
```exitcode
40
```

<!-- test: overloaded-static-requirement-declared-second -->
⭐ **R9 MADE THIS BUG REACHABLE FOR STATICS, and nothing covered it.** Before R9 the conformance check
skipped a `static` requirement outright, so no bare-key lookup happened for one; R9 removed the skip and
routed statics through the same key. Here `static tag(extra Code)` takes the bare `Point.tag` and the
required `static tag()` registers as `Point.tag#` — E3016 before R10, purely from the order.

⚠ **A STATIC SLOT HAS NO *RUNTIME* CONTROL — shv2 has no syntax for calling a static through a constrained
type parameter, so the slot is stamped and never read, and a wrong symbol in it cannot change an exit code.
ITS GUARD IS THE GOLDEN FRAGMENT, and that guard is real: MEASURED.** The `tag` slot's relocation is also
what DCE-roots the member it names (`DeadFunctionElimination` roots every function a `pendingRdataReloc`
targets), so the committed fragment below emits `func @Point.tag#` — the selected 0-argument member — and
emits it *because* the slot named it. Reverting `ensureWitnessTable` to the bare join reddens this case as
a golden mismatch: the reloc names `Point.tag`, that member is rooted instead, and `Point.tag#` is pruned.
The table IS built here (`PointBox`), so the same relocation additionally has to name a symbol the linker
can resolve — which is exactly how R9's original defect surfaced, in `bakeFuncAbs64Relocs`.
```maxon
typealias Code = int(0 to u32.max)

interface Wide
	static function tag() returns Code
	function digest() returns Code
end 'Wide'

type Point implements Wide
	export var x as Code

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export static function tag(extra Code) returns Code
		return extra
	end 'tag'

	export static function tag() returns Code
		return 7
	end 'tag'

	export function digest() returns Code
		return self.x
	end 'digest'
end 'Point'

type Box uses T where T is Wide
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function run() returns Code
		return self.item.digest()
	end 'run'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let p = Point.create(42)
	let b = PointBox.create(p)
	return b.run()
end 'main'
```
```exitcode
42
```

<!-- test: overloaded-static-requirement-declared-first -->
The lucky order of the same program — the required `static tag()` written FIRST, so it keeps the bare name
and the selection agrees with what the un-suffixed join would have produced. It is the over-rejection
guard for the static half: a fix that only ever consulted the overload set's LATER members would break
this while leaving the case above green.
```maxon
typealias Code = int(0 to u32.max)

interface Wide
	static function tag() returns Code
	function digest() returns Code
end 'Wide'

type Point implements Wide
	export var x as Code

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export static function tag() returns Code
		return 7
	end 'tag'

	export static function tag(extra Code) returns Code
		return extra
	end 'tag'

	export function digest() returns Code
		return self.x
	end 'digest'
end 'Point'

type Box uses T where T is Wide
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function run() returns Code
		return self.item.digest()
	end 'run'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let p = Point.create(42)
	let b = PointBox.create(p)
	return b.run()
end 'main'
```
```exitcode
42
```

<!-- test: overloaded-tostring-satisfies-stringable-and-formatted -->
⭐ **THE SHAPE THE STDLIB ITSELF ASKS FOR, and the proof that the witness table is the ONLY site that had to
move.** `stdlib/Interfaces.maxon` declares `Stringable.toString()` beside
`FormattedStringable.toString(format String)`, so a type conforming to both MUST overload `toString` — and
before R10 that was rejected whichever order the two were written in, because only one of them could hold
the bare `Point.toString` key. Here the FORMATTED member is written first and takes it.

Interpolation dispatches a user struct's `toString` DIRECTLY (`"{p}"` → a plain `Point.toString` call, not a
witness — the concrete type is statically known), so this program also asks whether that third site needed
the same treatment. It did not, and for a reason rather than by luck: the call carries its arguments, so
`SemanticCheck.resolveOverloadedCalls` retargets it to the 0-argument member exactly as it retargets any
other overloaded call. Printing `P` and not `F` is that answer.

⚠ **THE SAME FACT IS PINNED A SECOND TIME**, from the interpolation side, by
`specs-shv2/string-interpolation.md`'s `stringable-and-formatted-interp-selects-the-zero-arg-overload`
(R10d). Both write the FORMATTED member first for the same reason — the bare registration key must be held
by the member the call must NOT reach, or a resolver that picked nothing would pass anyway. **Change the
dispatch rule and both cases move; change one alone and the corpus is asserting two rules.**
```maxon
typealias Small = int(0 to 100)

type Point implements Stringable, FormattedStringable
	export var x as Small

	export static function create(x Small) returns Self
		return Self{ x: x }
	end 'create'

	export function toString(format String) returns String
		return "F"
	end 'toString'

	export function toString() returns String
		return "P"
	end 'toString'
end 'Point'

function main() returns ExitCode
	let p = Point.create(7)
	print("{p}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
P
```

<!-- test: error.no-overload-matches-requirement -->
⭐ **THE FIX IS NOT "ACCEPT ANYTHING WITH THE RIGHT NAME".** Two overloads named `label` and NEITHER has
the required shape, so the type does not conform — and the message may no longer speak as though one
candidate existed. It lists every member declared under the name instead of naming whichever one happened
to hold the bare key.
```maxon
typealias Code = int(0 to u32.max)

interface Labeled
	function label() returns Code
end 'Labeled'

type Tag implements Labeled
	export var x as Code

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export function label(extra Code) returns Code
		return extra
	end 'label'

	export function label(a Code, b Code) returns Code
		return a + b
	end 'label'
end 'Tag'

function main() returns ExitCode
	let t = Tag.create(42)
	return t.x
end 'main'
```
```maxoncstderr
error E3016: <fragment>:8:6: Partial interface implementation: type 'Tag' has 1 method(s) with wrong signature:
  - no member named 'label' matches: label(extra Code) returns Code, label(a Code, b Code) returns Code (expected label() returns Code)
```

<!-- test: error.two-overloads-match-one-requirement -->
⚠ **TWO MATCHES IS AMBIGUITY, NOT SUCCESS — and taking the first would have been the old bug wearing the
fix's clothes.** `Code` and `Small` are two ranged aliases over one primitive, and `signatureMatches`
compares CANONICALIZED type names (that is deliberate — see `canonicalTypeName`), so both members match
`label(v Code)` equally. There is no fact to choose by, the witness slot admits exactly one address, and
the call-site resolver already refuses the same pair as E3007. Before R10 the bare key hid the second
member and this program compiled.
```maxon
typealias Code = int(0 to u32.max)
typealias Small = int(0 to 100)

interface Labeled
	function label(v Code) returns Code
end 'Labeled'

type Tag implements Labeled
	export var x as Code

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export function label(v Code) returns Code
		return v
	end 'label'

	export function label(v Small) returns Code
		return 7
	end 'label'
end 'Tag'

function main() returns ExitCode
	let t = Tag.create(42)
	return t.x
end 'main'
```
```maxoncstderr
error E3016: <fragment>:9:6: Partial interface implementation: type 'Tag' has 1 method(s) with wrong signature:
  - 2 members named 'label' match: label(v Code) returns Code, label(v Small) returns Code (expected label(v Code) returns Code)
```

<!-- test: error.interface-declares-two-requirements-of-one-name -->
⭐⭐ **ONE INTERFACE MAY NOT DECLARE TWO REQUIREMENTS OF ONE NAME — because the accepted-member filing is
keyed by `(conformer, declaring interface, method NAME)` and carries NO ARITY.** `ConformanceCheck` files
the member it accepted for each requirement under that key and `ensureWitnessTable` stamps every slot's
relocation from the filing, so two same-named requirements of ONE interface are two slots contending for
one entry: the first is filed and the second either contradicts it or leaves its slot pointing at the
first's member. The refusal is what keeps the key injective over slots. Before R10 the program was rejected
anyway (the bare `Type.method` key could satisfy only one of the two requirements, so the other reported a
wrong signature), which is why it changes no accepted program.

⚠⚠ **THE REASON USED TO BE "a dispatch resolves by NAME alone", AND R10c FALSIFIED IT — both here and in
the message.** `findWitnessDispatchCandidates` now collects every requirement of the name and selects by
ARITY, so a second same-named requirement is perfectly dispatchable: `where-clauses.inherited-overload-dispatch`
pins exactly this pair (`label()` and `label(width Code)`) WORKING, across an `extends` edge, where the
declaring-interface half of the key differs and the collision does not arise. So the same two requirements
are accepted or refused according to which interface the author wrote each in. That split is real and is
not defensible on its own terms; closing it means widening the impl key to carry a requirement's arity,
which moves R10's conflicting-conformance detection (E3111) with it. It is its own rung. This case pins
the refusal AND its true reason so that the day the key is widened, it turns red and forces the decision.
```maxon
typealias Code = int(0 to u32.max)

interface Labeled
	function label() returns Code
	function label(extra Code) returns Code
end 'Labeled'

type Tag implements Labeled
	export var x as Code

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export function label(extra Code) returns Code
		return extra
	end 'label'

	export function label() returns Code
		return 40
	end 'label'
end 'Tag'

function main() returns ExitCode
	let t = Tag.create(42)
	return t.x
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:11: Unsupported: interface 'Labeled' declares two requirements named 'label' — one conforming type files ONE accepted member per (interface, method NAME), so the two would contend for one witness-table entry. Give the requirements distinct names, or declare one of them on an interface this one `extends`
```

<!-- test: error.one-interface-bound-two-ways -->
⭐⭐ **ONE INTERFACE, TWO BINDINGS, ONE WITNESS SLOT — AND THIS PANICKED THE COMPILER UNTIL THE R10 REVIEW.**
`Conv` is named twice with different associated-type arguments, so its one requirement is substituted two
ways (`convert(v Whole)` and `convert(v Real)`) and the overload set answers each with a DIFFERENT member.
There is one witness table per (conformer, interface) and one address per method slot, so there is nothing
to choose by. Before R10 this was refused cleanly as E3016 — the bare key could satisfy only the
first-declared member, so the other binding reported a wrong signature — and matching a requirement against
the whole overload set is exactly what let both routes succeed and disagree. It is now E3111, which is the
same verdict for the true reason; it is NOT a compiler panic, which is what a wrong internal-invariant
claim had made it.
```maxon
typealias Whole = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)

interface Conv uses Item
	function convert(v Item) returns Whole
end 'Conv'

type Machine implements Conv with Whole, Conv with Real
	export var tag as Whole

	export static function create(tag Whole) returns Self
		return Self{tag: tag}
	end 'create'

	export function convert(v Whole) returns Whole
		return v
	end 'convert'

	export function convert(v Real) returns Whole
		return 7
	end 'convert'
end 'Machine'

function main() returns ExitCode
	let m = Machine.create(1)
	return m.convert(42)
end 'main'
```
```maxoncstderr
error E3111: <fragment>:9:6: Type 'Machine' reaches interface 'Conv''s requirement 'convert' by two routes that select different members: convert(v Whole) returns Whole, convert(v Real) returns Whole. A conforming type has ONE witness table per interface and ONE address per method slot, so 'Conv' must be conformed to exactly one way — bind its associated types once, either by removing the duplicate 'Conv' entry or by not also reaching it through an interface that 'Machine' already implements
```

<!-- test: error.parent-interface-bound-two-ways-through-extends -->
⭐⭐ **THE SAME CONTRADICTION WITH NO INTERFACE NAMED TWICE — which is why it is caught where the two
selections MEET and not by a rule about the `implements` clause.** `Child extends Parent` and re-declares
the same `uses` name, so `implements Child with Whole, Parent with Real` reaches `Parent`'s requirement
twice: once substituted through `Child`'s binding, once through `Parent`'s own. The clause names `Child`
and `Parent`, each exactly once, so a duplicate-entry rule would have passed this program straight through
to the panic. The slot is the unit of the contradiction, so the slot is where it is detected.
```maxon
typealias Whole = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)

interface Parent uses Item
	function convert(v Item) returns Whole
end 'Parent'

interface Child extends Parent uses Item
	function marker() returns Whole
end 'Child'

type Machine implements Child with Whole, Parent with Real
	export var tag as Whole

	export static function create(tag Whole) returns Self
		return Self{tag: tag}
	end 'create'

	export function marker() returns Whole
		return 1
	end 'marker'

	export function convert(v Whole) returns Whole
		return v
	end 'convert'

	export function convert(v Real) returns Whole
		return 7
	end 'convert'
end 'Machine'

function main() returns ExitCode
	let m = Machine.create(1)
	return m.convert(42)
end 'main'
```
```maxoncstderr
error E3111: <fragment>:13:6: Type 'Machine' reaches interface 'Parent''s requirement 'convert' by two routes that select different members: convert(v Whole) returns Whole, convert(v Real) returns Whole. A conforming type has ONE witness table per interface and ONE address per method slot, so 'Parent' must be conformed to exactly one way — bind its associated types once, either by removing the duplicate 'Parent' entry or by not also reaching it through an interface that 'Machine' already implements
```

<!-- test: throws-narrower-than-abstract-requirement -->
⭐⭐ **AN IMPLEMENTATION MAY THROW A NARROWER ERROR TYPE THAN THE REQUIREMENT DECLARES, WHEN THE
REQUIREMENT IS ABSTRACT (A1s).** `Digest.digest` declares `throws Error` — the marker interface, which
declares no case — so the `try` at the witness dispatch has nothing to decode and binds an opaque scalar.
`Point.digest` throwing its own `MyParseError` is a conformer being MORE SPECIFIC, which is exactly what an
error interface is for; `stdlib/Builtins.maxon`'s `Parsable.fromString … throws Error` is the shape the whole
corpus writes. Both edges are pinned in one exit code: the success edge carries the real 20 through the
witness, the error edge takes the handler's 55.
```maxon
typealias Code = int(0 to u32.max)

enum MyParseError implements Error
	badInput
end 'MyParseError'

interface Digest
	function digest() returns Code throws Error
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code throws MyParseError
		if self.x < 10 'small'
			throw MyParseError.badInput
		end 'small'
		return self.x
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemDigest() returns Code
		return try self.item.digest() otherwise 55
	end 'itemDigest'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let good = PointBox.create(Point.create(20))
	let bad = PointBox.create(Point.create(3))
	return (good.itemDigest() + bad.itemDigest()) as ExitCode
end 'main'
```
```exitcode
75
```

<!-- test: error.throws-wider-than-concrete-requirement -->
⭐⭐ **THE NARROWING IS ONE-DIRECTIONAL, AND THIS IS THE CASE THAT PROVES THE NEW PERMISSION DID NOT SWALLOW
THE OLD REFUSAL.** The requirement names a CONCRETE error type, so its ordinals are exactly what the `try` at
the dispatch decodes; an implementation declaring the abstract `throws Error` is a WIDENING — it may throw
anything at all, and whatever it throws comes back decoded as a `DigestError`. Refused, with the same
sentence any other disagreeing pair of named types gets.
```maxon
typealias Code = int(0 to u32.max)

enum DigestError implements Error
	tooSmall
end 'DigestError'

interface Digest
	function digest() returns Code throws DigestError
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code throws Error
		if self.x < 10 'small'
			throw DigestError.tooSmall
		end 'small'
		return self.x
	end 'digest'
end 'Point'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:12:6: Method 'Point.digest' throws 'Error' but interface 'Digest' declares it 'throws DigestError' — a witness dispatch types its caught error off the INTERFACE, so the impl's error would be decoded as 'DigestError'
```

<!-- test: error.boxed-throws-under-abstract-requirement -->
⭐⭐ **THE RELAXATION STOPS AT THE FLAG SHAPE, AND THAT BOUNDARY IS A MEMORY-SAFETY OBLIGATION RATHER THAN A
WRONG ANSWER.** An abstract requirement declares no case, so a `try` at the dispatch catches it through the
SCALAR `ordinal + bias` ABI — while a payload-carrying union hands its error over as a heap BOX POINTER. Were
this accepted, the pointer would be decoded as an ordinal and the box would never be released.
The narrowing is granted only to error types whose own flag is that same scalar.

⚠ **THE PLAIN-FUNCTION SPELLING OF THIS PROGRAM USED TO EXIT 101 — A LEAK — IN shv2 AND IN THE REFERENCE
ORACLE ALIKE, AND IT NO LONGER COMPILES IN EITHER (A1s-throwsbox).** When this case shipped, refusing the
witness-dispatch route was explicitly *"rather than opening a second door"* to a hole that stood open on the
direct path: `function f(x Code) returns Code throws Error` throwing this same `BoxedError` and caught by
`try f(3) otherwise 55` linked, ran, decoded the box pointer as an ordinal and leaked the box. The FIRST
door is now shut too — a plain function's `throws` clause must name a declared enum or union (**E3113**,
`specs-shv2/error-handling.md`'s `error.throws-interface-on-a-plain-function`) — so the two routes into the
boxed-flag mismatch are refused by two checks that each own their own side: E3016 owns the relation between
a requirement and its impl, E3113 owns a function's own clause. This case's own program is untouched by
E3113: `Digest.digest`'s `throws Error` is an interface REQUIREMENT, which never becomes a function.
```maxon
typealias Code = int(0 to u32.max)

union BoxedError implements Error
	withMessage(msg String)
end 'BoxedError'

interface Digest
	function digest() returns Code throws Error
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code throws BoxedError
		if self.x < 10 'small'
			throw BoxedError.withMessage("nope")
		end 'small'
		return self.x
	end 'digest'
end 'Point'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:12:6: Method 'Point.digest' throws 'BoxedError' but interface 'Digest' declares it 'throws Error', which declares no case to decode — a witness dispatch catches such a requirement through the SCALAR error-flag ABI, while a payload-carrying union is handed over as a heap box pointer that would be decoded as an ordinal and never released. Throw a payload-free enum, or declare the requirement as 'BoxedError' itself
```

<!-- test: error.throws-unknown-requirement-type-is-not-abstract -->
⭐⭐ **AN UNRESOLVABLE REQUIREMENT TYPE IS A MISTAKE, NOT AN ABSTRACT ERROR CHANNEL.** `throws Bogus` names
neither a declared enum nor an interface, so nothing licenses an implementation to name something else — a
rule keyed only on "the requirement decodes nothing" would have let a typo'd requirement accept any error
type at all, silently. The requirement must name an INTERFACE for the narrowing to apply.
```maxon
typealias Code = int(0 to u32.max)

enum DigestError implements Error
	tooSmall
end 'DigestError'

interface Digest
	function digest() returns Code throws Bogus
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code throws DigestError
		if self.x < 10 'small'
			throw DigestError.tooSmall
		end 'small'
		return self.x
	end 'digest'
end 'Point'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:12:6: Method 'Point.digest' throws 'DigestError' but interface 'Digest' declares it 'throws Bogus' — a witness dispatch types its caught error off the INTERFACE, so the impl's error would be decoded as 'Bogus'
```

<!-- test: error.throws-requirement-shadowed-by-a-user-enum-is-concrete -->
⭐⭐ **`Error` IS A NAME, NOT A KEYWORD — AND A USER MAY DECLARE AN `enum Error`, WHICH MAKES THE REQUIREMENT
CONCRETE AGAIN.** Interface LOOKUP resolves `Error` to the synthesized marker whatever else is declared, so a
narrowing rule keyed on "the requirement names an interface" ALONE would grant the exemption here — while the
catch site reads the ENUM registry, finds the user's two cases, and decodes the implementation's ordinals as
`shadowAlpha`/`shadowBeta`. The rule therefore asks the enum registry FIRST, exactly as the decode does: a
requirement with cases to decode is concrete, and its implementation must name it.
```maxon
typealias Code = int(0 to u32.max)

enum Error
	shadowAlpha
	shadowBeta
end 'Error'

enum OtherError
	oops
end 'OtherError'

interface Digest
	function digest() returns Code throws Error
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code throws OtherError
		if self.x < 10 'small'
			throw OtherError.oops
		end 'small'
		return self.x
	end 'digest'
end 'Point'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:17:6: Method 'Point.digest' throws 'OtherError' but interface 'Digest' declares it 'throws Error' — a witness dispatch types its caught error off the INTERFACE, so the impl's error would be decoded as 'Error'
```

<!-- test: error.throws-unresolvable-impl-type-under-abstract-requirement -->
⭐⭐ **THE MIRROR OF THE CASE ABOVE, ON THE SIDE THE RUNG DID NOT ASK ABOUT (found by review probing, A1s).**
`error.throws-unknown-requirement-type-is-not-abstract` establishes that a REQUIREMENT naming nothing is a
mistake and not a licence. The IMPLEMENTATION owes the identical argument and was not made to: the guard
that keeps the narrowing to scalar-flagged errors asked the enum registry and read "no entry" as "not
boxed, therefore fine" — the PERMISSIVE answer to a memory-safety question, for a name it could not
resolve. MEASURED on the shipped rung: this exact program compiled, linked and ran, where the strict
same-name rule the exemption relaxes had refused it.

⚠ The refusal reaches only pairs the strict rule ALREADY refused — a same-named pair never asks either
question — so nothing that compiled before the exemption existed is touched by it. `throws Bogus` on a
plain function was accepted when this case shipped, and this door was not where that got closed:
A1s-throwsbox's **E3113** closed it, at the function's own clause. Both fire on the program below, and this
one WINS, deliberately — `checkConformance` runs before the pipeline, and it is the only one of the two that
can name the requirement the implementation is violating.
```maxon
typealias Code = int(0 to u32.max)

interface Digest
	function digest() returns Code throws Error
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code throws Bogus
		return self.x
	end 'digest'
end 'Point'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:8:6: Method 'Point.digest' throws 'Bogus' but interface 'Digest' declares it 'throws Error', and 'Bogus' names no declared enum or union — the narrowing an abstract requirement permits is granted only to an error type whose flag SHAPE the compiler can see, and an unresolvable name is a mistake rather than a licence. Declare 'Bogus', or name 'Error' itself
```

## A builtin literal marker is not a marker a user type may wear

<!-- test: error.literal-marker-conformer-would-be-dropped-through-the-wrong-cascade -->
### The record a marker conformer gets is a BYTE RECORD, and its cascade is a STRUCT's

⭐⭐ **THE ENVELOPE COLLAPSE IS A LAYOUT RULE; THE VALUE IT PRODUCES CARRIES AN IDENTITY NOTHING ELSE
AGREES WITH.** A type whose `implements` clause names one of `stdlib/Builtins.maxon`'s literal markers
holds its `managed` inline, and its `Self{…}` is built through the fused byte-record encoder
(`__str_from_bytes` / `__str_of_buffer`) — 48 bytes, `managed` at 0 and the grapheme flag at 40. But the
VALUE is tagged `structRef`, because `returns Self` resolves to the struct, so it is dropped by
`__destruct_<T>` and copied by `__clone_<T>`: cascades built from the DECLARED field list, over a record
that has no slot for a third field. Measured on the program below before the refusal existed: **exit
0xC0000005**, the cascade reading `label` at offset 48 of a 48-byte record and handing whatever it found
to `__str_decref`.

⇒ shv2 mints a fused record only for the two names it owns the record FOR. `String` and `Character` are
on `TypeResolution.isCompilerOwnedTypeName`, so no user file can declare either, and their values are
tagged `string`/`character` and dropped through `__str_decref` — never through a struct cascade.
```maxon
type Wrapped implements BuiltinStringLiteral
	var managed as __ManagedMemory
	var flag as bool
	var label as String = "tag"

	export static function init(value __ManagedMemory) returns Self
		return Self{managed: value, flag: false}
	end 'init'
end 'Wrapped'

function main() returns ExitCode
	let mm = try __ManagedMemory.create(64, elementSize: 1) otherwise return 1
	try mm.setLength(40) otherwise return 2
	let w = Wrapped.init(mm)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:10: Unsupported: `Wrapped` implements `BuiltinStringLiteral`, one of `stdlib/Builtins.maxon`'s literal markers, so its record would be the compiler's own fused byte record rather than the fields it declares. shv2 mints that record only for the two names it owns the record FOR — `String` and `Character` — because a conformer of any other name gets a VALUE whose bytes are a byte record's and whose IDENTITY is a struct's: every declared field the fused record has no slot for is discarded at construction, and the struct cascade that later drops or clones it reads past the record's end
```

<!-- test: error.literal-marker-conformer-would-be-cloned-through-the-wrong-cascade -->
### The clone half of the same refusal

`__clone_<T>` is built from the identical declared field list, so co-owning such a value out of a struct
field faults for the identical reason — measured **0xC0000005** before the refusal. One door refuses both,
because there is exactly one producer of a fused wrapper value.
```maxon
type Wrapped implements BuiltinStringLiteral
	var managed as __ManagedMemory
	var flag as bool
	var label as String = "tag"

	export static function init(value __ManagedMemory) returns Self
		return Self{managed: value, flag: false}
	end 'init'
end 'Wrapped'

type Box
	var w as Wrapped

	export static function init(x Wrapped) returns Self
		return Self{w: x}
	end 'init'

	export function get() returns Wrapped
		return self.w
	end 'get'
end 'Box'

function main() returns ExitCode
	let mm = try __ManagedMemory.create(64, elementSize: 1) otherwise return 1
	try mm.setLength(40) otherwise return 2
	let b = Box.init(Wrapped.init(mm))
	let copy = b.get()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:10: Unsupported: `Wrapped` implements `BuiltinStringLiteral`, one of `stdlib/Builtins.maxon`'s literal markers, so its record would be the compiler's own fused byte record rather than the fields it declares. shv2 mints that record only for the two names it owns the record FOR — `String` and `Character` — because a conformer of any other name gets a VALUE whose bytes are a byte record's and whose IDENTITY is a struct's: every declared field the fused record has no slot for is discarded at construction, and the struct cascade that later drops or clones it reads past the record's end
```

<!-- test: error.literal-marker-conformer-cannot-hold-what-its-literal-was-given -->
### ⚠ THE SILENT ONE — a field written `false` that reads back TRUE

⭐⭐ **THE MOST SERIOUS OF THE THREE, BECAUSE IT NEITHER FAULTS NOR LEAKS.** The fused encoder takes the
`managed` field as its source and the field named `singleByteGraphemesFlag` as its flag; a conformer whose
second field is named anything else reaches `__str_from_bytes`, which CLASSIFIES the bytes and writes its
own answer at @40. The declared `flag` occupies @40 in the collapsed layout, so `Self{flag: false}` is
written, discarded, and read back as the classifier's `true`. Measured before the refusal: **exit 7**,
the `true` branch, for a program whose only literal wrote `false`.
```maxon
type Wrapped implements BuiltinStringLiteral
	var managed as __ManagedMemory
	var flag as bool

	export static function init(value __ManagedMemory) returns Self
		return Self{managed: value, flag: false}
	end 'init'

	export function readFlag() returns bool
		return self.flag
	end 'readFlag'
end 'Wrapped'

function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(1) otherwise return 2
	try mm.setByte(0, 65) otherwise return 3
	let w = Wrapped.init(mm)
	return (7 if w.readFlag() else 9) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:10: Unsupported: `Wrapped` implements `BuiltinStringLiteral`, one of `stdlib/Builtins.maxon`'s literal markers, so its record would be the compiler's own fused byte record rather than the fields it declares. shv2 mints that record only for the two names it owns the record FOR — `String` and `Character` — because a conformer of any other name gets a VALUE whose bytes are a byte record's and whose IDENTITY is a struct's: every declared field the fused record has no slot for is discarded at construction, and the struct cascade that later drops or clones it reads past the record's end
```

<!-- test: error.char-literal-marker-conformer-is-refused-too -->
### `BuiltinCharLiteral` is refused by the same door

The marker the refusal quotes is the one the type DECLARES, so the two byte-record markers reach one
refusal rather than one each — the record is the same 48 bytes and the defect is the same defect.
```maxon
type Glyph implements BuiltinCharLiteral
	var managed as __ManagedMemory

	export static function init(value __ManagedMemory) returns Self
		return Self{managed: value}
	end 'init'
end 'Glyph'

function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let g = Glyph.init(mm)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:10: Unsupported: `Glyph` implements `BuiltinCharLiteral`, one of `stdlib/Builtins.maxon`'s literal markers, so its record would be the compiler's own fused byte record rather than the fields it declares. shv2 mints that record only for the two names it owns the record FOR — `String` and `Character` — because a conformer of any other name gets a VALUE whose bytes are a byte record's and whose IDENTITY is a struct's: every declared field the fused record has no slot for is discarded at construction, and the struct cascade that later drops or clones it reads past the record's end
```
