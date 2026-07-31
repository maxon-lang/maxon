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

<!-- disabled-test: interface-method-unused-param-allowed -->
<!-- P1.7a-existentials: an interface-typed PARAMETER (`function callGreet(g Greeter)`) is a fat pointer — plan-settled out of Phase 1 -->
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


<!-- disabled-test: interface-method-via-extended-interface -->
<!-- P1.7a-existentials: an interface-typed PARAMETER (`function callPing(b Base)`) is a fat pointer — plan-settled out of Phase 1 -->
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


<!-- disabled-test: non-interface-method-on-conforming-type-still-errors -->
<!-- P1.7a-s2: E3012 unused-variable checking is not implemented in shv2 (the conformance half works; this asserts the unused-param check still fires) -->
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


<!-- disabled-test: interface-method-local-var-still-errors -->
<!-- P1.7a-s2: E3012 unused-variable checking is not implemented in shv2 (the conformance half works; this asserts the unused-local check still fires) -->
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

<!-- disabled-test: interface-impl-ignore-param-name -->
<!-- P1.7a-existentials: an interface-typed PARAMETER (`function useGreeter(g Greeter)`) is a fat pointer — plan-settled out of Phase 1 -->
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
`Parser.overloadRegistrationNameFor` hands back the BARE name on purpose and it collides in
`commitFuncSignatures`, earning the E3006 it always did.

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
error E3006: <fragment>:16:11: duplicate definition of function 'Widget.label'
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
