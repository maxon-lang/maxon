---
feature: extension-over-primitive
status: stable
keywords: [extension, primitive, int, float, bool, byte, self, conformance, dispatch]
category: type-system
---

# An `extension` whose target is a PRIMITIVE

## Documentation

`extension` takes a **type name**, and three of Maxon's types are spelled with a KEYWORD — `int`,
`float`, `bool` (and `byte`). An extension over one of those is an ordinary type extension: the target
is its own sole conformer, its methods become that primitive's methods, and a `Self` inside the body
means the primitive.

```maxon
extension int implements Hashable
	export function hash() returns HashValue
		return self and 0xFFFFFFFF
	end 'hash'
end 'int'
```

### The target is a keyword, and the keyword's spelling IS its name

Every other extension target arrives as an `identifier` token. A primitive target does not — `int` is
lexed as `TokenKind.int` — so the header reader admits the four primitive keywords and reads the name
off the token's own bytes. There is **one** header reader, shared by the pre-scan, the fold and the real
parse, so the four keywords are admitted in exactly one place.

### `Self` on a primitive extension is the PRIMITIVE, not a struct

`equals(other Self)`, `compare(other Self)` and `clone() returns Self` are the shapes that force this.
A `Self` resolved through the ordinary named-type road would produce a reference to a struct type no
registry holds — the same defect the self-hosted compiler recorded and fixed
(`maxon-selfhosted/ROADMAP.md:472`: an `extension float` block declared its `__self` as a *named* Float
rather than the float primitive, which broke `"{self}"` interpolation). `Self` here is the primitive's
own type, in parameter and return position alike.

### ⭐ A DECLARED body BEATS the compiler's own

shv2 synthesizes a conformance surface for the primitives — `hash`, `equals`, `compare` come from a
generated impl, and `toString` and `clone` are lowered **inline**, calling no symbol at all. That
surface exists precisely *because* there was no `extension` mechanism to hang a body on.

Now that there is one, a declaration wins. This is the rule that matters, and it is the one a test
where the two AGREE cannot check: the cases below deliberately declare bodies that return something
the synthesized surface never would, so a passing test can only mean the declared body ran.

## Tests

<!-- test: an-extension-adds-a-method-to-int -->
The base case: a new method, no conformance, an `int` receiver.
```maxon
typealias Integer = int(i64.min to i64.max)

extension int
	export function doubled() returns Integer
		return self * 2
	end 'doubled'
end 'int'

function main() returns ExitCode
	let n = 21
	return n.doubled() as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: an-extension-adds-a-method-to-float -->
`trunc` rather than `as Integer`: a float→int CAST is `E3009 Cannot cast from float to int` in shv2 with no
extension anywhere in sight, so the cast form would have tested that rule instead of this one.
```maxon
extension float
	export function tripled() returns float
		return self * 3.0
	end 'tripled'
end 'float'

function main() returns ExitCode
	let x = 4.0
	return trunc(x.tripled()) as ExitCode
end 'main'
```
```exitcode
12
```

<!-- test: an-extension-adds-a-method-to-bool -->
```maxon
typealias Integer = int(i64.min to i64.max)

extension bool
	export function asCount() returns Integer
		if self 'yes'
			return 7
		end 'yes'
		return 3
	end 'asCount'
end 'bool'

function main() returns ExitCode
	let t = true
	let f = false
	return (t.asCount() + f.asCount()) as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: self-in-return-position-on-a-primitive-extension -->
⭐ `returns Self` on a primitive extension — the shape `maxon-selfhosted/ROADMAP.md:472` records getting
wrong. `Self` is the primitive, so the returned value is an ordinary `int`.
```maxon
extension int
	export function twin() returns Self
		return self + 1
	end 'twin'
end 'int'

function main() returns ExitCode
	let n = 40
	return n.twin().twin() as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: self-in-parameter-position-on-a-primitive-extension -->
```maxon
typealias Integer = int(i64.min to i64.max)

extension int
	export function plus(other Self) returns Integer
		return self + other
	end 'plus'
end 'int'

function main() returns ExitCode
	let n = 20
	return n.plus(22) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: a-declared-hash-beats-the-synthesized-impl -->
⭐⭐ **THE DIFFERING-DECLARATIONS CONTROL for the conformance half.** shv2's synthesized `int.hash`
answers the receiver masked to 32 bits — for a receiver of `5` it answers `5`. This body answers `77`
for every receiver, so `77` can only come from the declaration.
```maxon
extension int implements Hashable
	export function hash() returns HashValue
		return 77
	end 'hash'
end 'int'

function main() returns ExitCode
	let n = 5
	return n.hash()
end 'main'
```
```exitcode
77
```

<!-- test: a-declared-hash-agrees-through-a-witness -->
⭐⭐ **THE TWO ROADS TO ONE DECLARED BODY, PINNED AGAINST EACH OTHER (W133 review).** The case above
proves the DIRECT call reaches the declaration; it says nothing about the WITNESS. Those are two
separate mechanisms — the direct call is `Parser.dispatchBuiltinConformanceMethod`'s `declaresCallee`
peek, while the witness road is a table `BuiltinConformanceRuntime` fills by declining to synthesize an
impl for a symbol the module already DEFINES. Nothing structural makes the two agree, and if they ever
came apart the failure would be silent: `n.hash()` answering `77` beside a `where T is Hashable` body
answering `5`, both compiling, both linking. So the two are compared **in one program**, where a split
is a differing line rather than two green runs nobody put side by side.
```maxon
typealias Integer = int(0 to u32.max)

extension int implements Hashable
	export function hash() returns HashValue
		return 77
	end 'hash'
end 'int'

type Box uses T where T is Hashable
	export var a as T
	export static function create(a T) returns Self
		return Self{ a: a }
	end 'create'
	export function digest() returns HashValue
		return self.a.hash()
	end 'digest'
end 'Box'

typealias IntBox = Box with Integer

function main() returns ExitCode
	let n = 5
	let b = IntBox.create(n)
	print("direct={n.hash()} witness={b.digest()}")
	return 0
end 'main'
```
```stdout
direct=77 witness=77
```

<!-- test: a-declared-conformance-supplies-a-protocol-the-primitive-lacks -->
⭐⭐ **A DECLARATION DOES NOT MERELY *BEAT* THE SYNTHESIZED SURFACE — IT CAN EXTEND IT (W133 review).**
`bool` has no intrinsic `Hashable` at all: `IrInterface.isIntrinsicBuiltinConformance` grants it
`Comparable` and nothing else, so a bare `t.hash()` is **`E2015 'bool' has no method named 'hash' — a
builtin-typed receiver supplies `compare`, `toString`, `clone``** (measured on this tree). That makes
this the case where the declared body is not competing with a compiler answer but is the *only* one,
and it is the sharper test of the witness road: the table cannot be inherited from a synthesized impl,
because there is none to inherit. Both roads are compared in one program for the case above's reason.
```maxon
extension bool implements Hashable
	export function hash() returns HashValue
		return 9 if self else 4
	end 'hash'
end 'bool'

type Box uses T where T is Hashable
	export var a as T
	export static function create(a T) returns Self
		return Self{ a: a }
	end 'create'
	export function digest() returns HashValue
		return self.a.hash()
	end 'digest'
end 'Box'

typealias BoolBox = Box with bool

function main() returns ExitCode
	let t = true
	let b = BoolBox.create(t)
	print("direct={t.hash()} witness={b.digest()}")
	return 0
end 'main'
```
```stdout
direct=9 witness=9
```

<!-- test: a-declared-tostring-beats-the-inline-lowering -->
⭐⭐ **THE DIFFERING-DECLARATIONS CONTROL for the INLINE half, and it is the one that can fail
silently.** `toString` on a primitive receiver is lowered inline and calls no symbol, so a declared body
for it can be published, type-checked, and never run — a wrong answer rather than a refusal. The
synthesized lowering would print `5`; this body prints `five`.
```maxon
extension int implements Stringable
	export function toString() returns String
		return "five"
	end 'toString'
end 'int'

function main() returns ExitCode
	let n = 5
	print(n.toString())
	return 0
end 'main'
```
```stdout
five
```

<!-- test: a-declared-clone-beats-the-inline-lowering -->
⭐⭐ The same control for `clone`, the other inline-lowered arm. A synthesized `clone` returns the
receiver unchanged; this one does not.
```maxon
extension int implements Cloneable
	export function clone() returns Self
		return self + 100
	end 'clone'
end 'int'

function main() returns ExitCode
	let n = 5
	return n.clone() as ExitCode
end 'main'
```
```exitcode
105
```

<!-- test: an-extension-on-a-primitive-may-not-declare-storage -->
An extension adds behaviour, never storage — there is no layout for a field to join. The primitive
target does not relax that, and the refusal is the same one every other extension body gets.
```maxon
typealias Integer = int(i64.min to i64.max)

extension int
	var extra as Integer
end 'int'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:2: Unsupported: a `var` declaration in an `extension` body — an extension adds methods to types other declarations named and declares no storage of its own, so there is no type for this binding to belong to; declare it in the `type` body as a `static var`
```

<!-- test: error.byte-is-not-an-extendable-primitive -->
⭐ **`byte` is admitted by `namesAPrimitiveType` and is NOT an extendable target, and the difference is
the point.** That predicate names the set with STATICS (`byte.fromString`); this door needs the set with
VALUES. `byte` names no type in shv2 — a byte-sized value is an `int` range (`typealias Byte = int(0 to
u8.max)`) reaching every receiver door tagged `integer` — so an `extension byte` body would publish
`byte.<method>` symbols nothing could ever dispatch: a silent declaration, the exact shape this rung
closes. **Refused with a position rather than compiled to nothing**, which is what it did before.
```maxon
typealias Integer = int(i64.min to i64.max)

extension byte
	export function widened() returns Integer
		return 1
	end 'widened'
end 'byte'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:11: Unsupported: `extension byte` — `byte` is the qualifier of a static (`byte.fromString`) and names no type of its own, so it has no values for these methods to be dispatched on; a byte-sized value is an `int` range (`typealias Byte = int(0 to u8.max)`), so extend `int`
```

<!-- test: error.self-in-a-primitive-extension-has-no-fields -->
⭐ A primitive **is** its value. `self` in these bodies is the `int` itself, so there is nothing for a
field access to read — and reaching for one used to take the compiler down at `enclosingLayout`,
blaming the pre-scan for a `type int` no program wrote. It is a positioned refusal, mirroring the enum
arm.
```maxon
typealias Integer = int(i64.min to i64.max)

extension int
	export function peek() returns Integer
		return self.v
	end 'peek'
end 'int'

function main() returns ExitCode
	let n = 5
	return n.peek() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:10: Unsupported: a field access through `self` in a method of `extension int` — a primitive IS its value and declares no fields, so `self` here is the int itself and has no members to read
```

<!-- test: error.an-interface-extension-publishes-nothing-onto-a-primitive -->
⭐⭐ **THE BOUNDARY OF THIS RUNG, PINNED SO IT CANNOT DRIFT INTO A WRONG ANSWER (W133 review).** A
primitive may be an extension TARGET, and this case's `extension int implements Doubler` is accepted and
conformance-checked. It may NOT be an interface CONFORMER: `ProgramSignatures.conformersOf` builds its
list by walking `structNamesInDeclOrder`, so `int` is in no interface's conformer set and an
`extension Doubler` body is expanded onto every declared conforming `type` and onto no primitive.
⚠ **The guarantee worth pinning is that this is a REFUSAL rather than silence** — the same shape
`conformersOf` already documents for `enum` and `union`. A capability this rung did not build, stated by
a case instead of by a comment nobody reruns.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Doubler
	function doubled() returns Integer
end 'Doubler'

extension int implements Doubler
	export function doubled() returns Integer
		return self * 2
	end 'doubled'
end 'int'

extension Doubler
	export function quadrupled() returns Integer
		return self.doubled() * 2
	end 'quadrupled'
end 'Doubler'

function main() returns ExitCode
	let n = 5
	return n.quadrupled() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:22:11: Unsupported: 'int' has no method named 'quadrupled' — a builtin-typed receiver supplies `hash`, `equals`, `compare`, `toString`, `clone`
```
