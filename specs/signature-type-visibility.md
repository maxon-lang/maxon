---
feature: signature-type-visibility
status: stable
keywords: [visibility, export, module, public, signature, api, E3167]
category: diagnostics
---

# Signature Type Visibility

## Documentation

A function's signature is part of its visibility contract: whoever may call the function must be able
to name what the call takes and gives back. So every type a signature names must carry a written
visibility at least the function's own.

The four tiers, narrowest first — `public` outranks `export` for this rule, so a `public` function may not
name an `export` type:

| modifier | tier word in the diagnostic |
|----------|-----------------------------|
| *(none)* | `file-private` |
| `module` | `module` |
| `export`  | `exported` |
| `public` | `public` |

Every type NAMED in a declaration's signature is asked: each parameter type, the return type, and the
throws type, walked structurally through generic type arguments, tuple elements and function-type
shapes. A type parameter, a primitive, and the inline range inside a ranged alias name no declaration
and are asked nothing; a tuple names no declaration either, though each of its elements does.

A member's OWN modifier decides, not its enclosing type's — an `export type` may hold a `public`
method, and that method is held to the `public` bar. An interface member takes the INTERFACE's written
modifier, because a conformer's caller reaches the requirement through the interface.

```text
typealias ElementIndex = int(0 to 1000)

export function f(i ElementIndex) returns ElementIndex   // E3167, twice
	return i
end 'f'
```

One diagnostic per offending (function, type, position), in signature order — parameters left to right,
then the return type, then the throws clause — anchored at the function NAME:

```text
error E3167: api/lib.maxon:3:17: exported function 'api.f' names file-private typealias 'ElementIndex' in the type of parameter 'i'
```

A `module`, `export` or `public` binding is held to the same rule: a top-level `let` or `var`, and a
`static let` or `static var` member. Its type is the one its initializer gives it, whether or not the
binding writes it, and the alias the initializer was spelled through — a cast's `as Small`, or the
`Tallies` in `Tallies.create()` — is judged with it. The refusal sits at the binding name; a `let` is a
"constant", a `var` a "variable", and the position is "in its type":

```text
enum Status
	big
	small
end 'Status'

export let BIG = Status.big   // E3167
```

```text
error E3167: api/lib.maxon:6:12: exported constant 'api.BIG' names file-private enum 'Status' in its type
```

Widen the type to the binding's tier (`export enum Status`), or narrow the binding (`let BIG`).

Every other surface a declaration exposes is held to the same rule, with its own noun and position:

| surface | noun | position | its tier |
|---------|------|----------|----------|
| an instance field | `field` | `in its type` | the narrower of the field's own modifier and its type's |
| a union case's payload | `union case` | `in the type of payload 'name'` | the union's |
| a typealias | `typealias` | `in its target` | the alias's |
| an `implements` clause, with its `with` arguments | `type`, `enum` or `union` | `in its implements clause` | the declaration's |
| a type's `where` clause | `type` | `in its where clause` | the type's |
| a generic function's or an extension's `where` clause | `function` | `in its where clause` | the function's |
| an `extends` clause | `interface` | `in its extends clause` | the interface's |
| a struct-backed enum's backing record | `enum` | `in its backing record` | the enum's |

An unmarked field is private to its type and is asked nothing. A field or payload is reported at the type it
writes, every other surface at the declaration's name:

```text
enum Mode
	fast
	slow
end 'Mode'

export type Holder
	export var mode as Mode   // E3167
end 'Holder'
```

```text
error E3167: api/lib.maxon:7:21: exported field 'api.Holder.mode' names file-private enum 'Mode' in its type
```

A service's `handle` is its service's tier, so a surface naming `Worker.handle` names `Worker`.

A surface reached from another file counts as a use of every type it names this way, and so does a surface
of a type that is itself so used: a field of a type only an exported function returns credits the field's
type. That keeps an `export` type that only an exported surface exposes clear of
[E3092](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3092).

### The library is held to it too

A typealias written in `stdlib/` with no modifier is file-private to the library file it lives in, the
same as anyone's, and any library declaration without `public` is hidden from author code, so user code
that names one — bare or as `stdlib.Name` — is refused where it writes the name. Every alias a `public`
library signature names is therefore written `public` itself.

## Tests

The rule bites where the function outranks a type in its signature.

<!-- test: error.an-export-function-names-a-file-private-parameter-and-return-type -->
⭐ **TWO POSITIONS, ONE TYPE, TWO DIAGNOSTICS, IN SIGNATURE ORDER** — the parameter first, then the
return type. One offending (function, type) pair per POSITION is what makes the report tell an author
what to change, and both sit at the function name.
```maxon
// --- file: api/lib.maxon
typealias ElementIndex = int(0 to 100)

export function f(i ElementIndex) returns ElementIndex
	return i
end 'f'

// --- file: app/main.maxon
function main() returns ExitCode
	let n = f(7)
	print("{n}")
	return 0
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:5:17: exported function 'api.f' names file-private typealias 'ElementIndex' in the type of parameter 'i'
error E3167: api/<fragment>:5:17: exported function 'api.f' names file-private typealias 'ElementIndex' in its return type
```

<!-- test: error.a-module-function-names-a-file-private-type -->
The tier word follows the function's own modifier: `module`, not `exported`. The caller sits in the
declaring directory, which is the only place a `module` function is reachable from.
```maxon
// --- file: api/lib.maxon
typealias ElementIndex = int(0 to 100)

module function show(i ElementIndex)
	print("{i}")
end 'show'

// --- file: api/main.maxon
function main() returns ExitCode
	show(7)
	return 0
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:5:17: module function 'api.show' names file-private typealias 'ElementIndex' in the type of parameter 'i'
```

<!-- test: error.an-export-function-throws-a-file-private-error -->
The throws clause is a signature position: a caller that wants to MATCH on the error has to be able to
name it. The `otherwise` here does not, so nothing but this rule refuses the program.
```maxon
// --- file: api/lib.maxon
enum LibError implements Error
	tooBig
end 'LibError'

export function f(flag bool) returns ExitCode throws LibError
	if flag 'guard'
		throw LibError.tooBig
	end 'guard'

	return 7
end 'f'

// --- file: app/main.maxon
function main() returns ExitCode
	let n = try f(false) otherwise 0
	print("{n}")
	return 0
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:7:17: exported function 'api.f' names file-private enum 'LibError' in its throws clause
```

<!-- test: error.an-export-function-names-a-module-type -->
`module` is NARROWER than `export`, so the violation is one tier wide rather than all the way down to
file-private. Both tier words are read off the written modifiers.
```maxon
// --- file: api/config.maxon
module type Config
	module var size as ExitCode

	module static function create(size ExitCode) returns Self
		return Self{size: size}
	end 'create'
end 'Config'

export function make() returns Config
	return Config.create(7)
end 'make'

// --- file: api/main.maxon
function main() returns ExitCode
	let c = Config.create(1)
	let m = make()
	return c.size + m.size
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:11:17: exported function 'api.make' names module type 'Config' in its return type
```

<!-- test: error.a-public-function-names-an-export-type -->
⭐ **THE TOP OF THE LADDER.** `public` states API surface, and API surface may not be built out of a
type that is merely `export`. Nothing else about this program differs from a legal one.
```maxon
// --- file: api/config.maxon
export type Config
	export var size as ExitCode

	export static function create(size ExitCode) returns Self
		return Self{size: size}
	end 'create'
end 'Config'

public function make() returns Config
	return Config.create(7)
end 'make'

// --- file: app/main.maxon
function main() returns ExitCode
	let c = Config.create(1)
	let m = make()
	return c.size + m.size
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:11:17: public function 'api.make' names exported type 'Config' in its return type
```

<!-- test: error.a-type-argument-counts -->
⭐⭐ **THE WALK IS STRUCTURAL, NOT SURFACE.** `SecretArray` is `export` and passes on its own; the
element it carries does not, and a caller holding the array holds `Secret` values. Asking only the
written name would let every file-private type out through one exported alias.
```maxon
// --- file: api/lib.maxon
type Secret
	static function create() returns Self
		return Self{}
	end 'create'
end 'Secret'

export typealias SecretArray = Array with Secret

export function all() returns SecretArray
	var xs = SecretArray.create()
	xs.push(Secret.create())
	return xs
end 'all'

// --- file: app/main.maxon
function sizeOf(xs SecretArray) returns ExitCode
	return xs.count() as ExitCode
end 'sizeOf'

function main() returns ExitCode
	return sizeOf(all())
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:11:17: exported function 'api.all' names file-private type 'Secret' in its return type
error E3167: api/<fragment>:9:18: exported typealias 'api.SecretArray' names file-private type 'Secret' in its target
```

<!-- test: error.a-tuple-element-counts -->
The tuple itself names no declaration and is asked nothing. Its ELEMENTS are asked, one at a time —
`ExitCode` answers and `Secret` does not.
```maxon
// --- file: api/lib.maxon
type Secret
	static function create() returns Self
		return Self{}
	end 'create'
end 'Secret'

export function pair() returns (Secret, ExitCode)
	return (Secret.create(), 7)
end 'pair'

// --- file: app/main.maxon
function main() returns ExitCode
	let t = pair()
	return t.1
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:9:17: exported function 'api.pair' names file-private type 'Secret' in its return type
```

<!-- test: error.a-generic-instances-base-type-counts -->
A generic instance names two things and both are asked: the argument, and the BASE the argument is
substituted into. `IntBox` is `export` and its argument `ExitCode` is a primitive, so the surface and
the argument both answer — the base `Box` does not, and a caller holding the returned value holds a
`Box`.
```maxon
// --- file: api/lib.maxon
type Box uses T
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

export typealias IntBox = Box with ExitCode

export function wrap(n ExitCode) returns IntBox
	return IntBox.create(n)
end 'wrap'

// --- file: app/main.maxon
function main() returns ExitCode
	let boxed = wrap(7)
	return 7
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:13:17: exported function 'api.wrap' names file-private type 'Box' in its return type
error E3167: api/<fragment>:11:18: exported typealias 'api.IntBox' names file-private type 'Box' in its target
```

<!-- test: error.a-method-outranks-its-parameter-type -->
⭐ **THE MEMBER'S OWN MODIFIER IS THE BAR, NOT ITS TYPE'S.** `Box` is `export`, so `Slot` would pass
against the type — the member says `public`, and it is the member that is held. The anchor is the
member NAME, one tab in: a tab is one column, so `get` sits at column 18.
```maxon
// --- file: api/lib.maxon
typealias Slot = int(0 to 100)

export type Box
	export static function create() returns Self
		return Self{}
	end 'create'

	public function get(index Slot) returns bool
		return index > 3
	end 'get'
end 'Box'

// --- file: app/main.maxon
function main() returns ExitCode
	let b = Box.create()
	return 7 if b.get(7) else 0
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:10:18: public function 'api.Box.get' names file-private typealias 'Slot' in the type of parameter 'index'
```

<!-- test: error.a-public-interface-member-names-a-file-private-type -->
An interface member writes no modifier of its own, so it takes the INTERFACE's: a caller reaches the
requirement through `Sized`, and whatever can name `Sized` must be able to name what `size` returns.
The conforming `Box.size` is file-private and names the same alias legally — the tier that was
breached is the interface's.
```maxon
// --- file: api/lib.maxon
typealias Count = int(0 to 100)

public interface Sized
	function size() returns Count
end 'Sized'

type Box implements Sized
	function size() returns Count
		return 7
	end 'size'

	static function create() returns Self
		return Self{}
	end 'create'
end 'Box'

export function boxSize() returns ExitCode
	let b = Box.create()
	return b.size() as ExitCode
end 'boxSize'

// --- file: app/main.maxon
function main() returns ExitCode
	return boxSize()
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:6:11: public function 'api.Sized.size' names file-private typealias 'Count' in its return type
```

<!-- test: error.a-narrower-generic-alias-in-an-interface-requirement-return-is-refused -->
A generic alias is a declaration like a ranged one, and a requirement's return type is asked like a
function's. `Tallies` is file-private and `Stocked` is `export`, so whatever can name `Stocked` must be able to
name what `items` returns.
```maxon
// --- file: api/lib.maxon
typealias Tallies = Array with ExitCode

export interface Stocked
	function items() returns Tallies
end 'Stocked'

type Shelf implements Stocked
	function items() returns Tallies
		var xs = Tallies.create()
		xs.push(7)
		return xs
	end 'items'

	static function create() returns Self
		return Self{}
	end 'create'
end 'Shelf'

export function stock() returns ExitCode
	let s = Shelf.create()
	return s.items().count() as ExitCode
end 'stock'

// --- file: app/main.maxon
function main() returns ExitCode
	return stock()
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:6:11: exported function 'api.Stocked.items' names file-private typealias 'Tallies' in its return type
```

<!-- test: error.a-narrower-alias-as-an-instance-argument-in-an-interface-requirement-is-refused -->
The structural walk reaches a requirement's parameters too. `Secrets` is `export`, but the element it carries
is the file-private `Secret`, and a conformer's caller hands `keep` an array of them.
```maxon
// --- file: api/lib.maxon
typealias Secret = int(0 to 9)
export typealias Secrets = Array with Secret

export interface Keeper
	function keep(xs Secrets) returns ExitCode
end 'Keeper'

type Vault implements Keeper
	function keep(xs Secrets) returns ExitCode
		return xs.count() as ExitCode
	end 'keep'

	static function create() returns Self
		return Self{}
	end 'create'
end 'Vault'

export function hold() returns ExitCode
	var xs = Secrets.create()
	xs.push(4)
	return Vault.create().keep(xs)
end 'hold'

// --- file: app/main.maxon
function main() returns ExitCode
	return hold()
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:7:11: exported function 'api.Keeper.keep' names file-private typealias 'Secret' in the type of parameter 'xs'
error E3167: api/<fragment>:4:18: exported typealias 'api.Secrets' names file-private typealias 'Secret' in its target
```

<!-- test: error.an-interface-inner-alias-over-a-file-private-type-is-refused -->
An alias declared inside an interface body is as visible as its right-hand side, not as its interface.
`ElementArray` sits inside the `export` interface `Keeper`, but it carries the file-private `Foo`, and a
conformer's caller hands `keep` an array of them.
```maxon
// --- file: api/lib.maxon
typealias Foo = int(0 to 9)

export interface Keeper
	typealias ElementArray = Array with Foo
	function keep(xs ElementArray) returns ExitCode
end 'Keeper'

type Vault implements Keeper
	typealias ElementArray = Array with Foo

	static function create() returns Self
		return Self{}
	end 'create'

	static function sample() returns ElementArray
		var xs = ElementArray.create()
		xs.push(4)
		return xs
	end 'sample'

	function keep(xs ElementArray) returns ExitCode
		return xs.count() as ExitCode
	end 'keep'
end 'Vault'

export function hold() returns ExitCode
	return Vault.create().keep(Vault.sample())
end 'hold'

// --- file: app/main.maxon
function main() returns ExitCode
	return hold()
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:7:11: exported function 'api.Keeper.keep' names file-private typealias 'Foo' in the type of parameter 'xs'
```

<!-- test: error.a-stdlib-internal-alias-is-not-nameable-from-user-code -->
⭐⭐ **THE LIBRARY IS HELD TO THE SAME RULE, AND THIS IS WHAT THAT COSTS USER CODE.**
`stdlib/Math.maxon:8` writes `typealias SeriesTermLimit = int(2 to 64)` with no modifier; its one
reader is `Math.lnBySeries`, a file-private static, and no other library file names it. It is an
implementation detail of one function's term count, and nothing about `Math`'s public surface says it
exists. It is file-private, so the name resolves to nothing the author may name at the `as`, and the
compiler gives the answer it gives for any other file's non-exported typealias.
```maxon
// --- file: api/lib.maxon
export function seriesLimit() returns ExitCode
	let n = 8 as SeriesTermLimit
	print("{n}")
	return 0
end 'seriesLimit'

// --- file: app/main.maxon
function main() returns ExitCode
	return seriesLimit()
end 'main'
```
```maxoncstderr
error E3008: api/<fragment>:4:15: typealias 'SeriesTermLimit' is not exported
```

<!-- test: error.an-export-function-names-a-module-type-declared-in-another-file -->
The declaring file is not the naming file. `Config` is `module`, so `api/lib.maxon` may name it and
`app/main.maxon` may not — which is exactly the gap the rule closes, and the only shape in this file
where the type and the function are written in two different files.
```maxon
// --- file: api/other.maxon
module type Config
	module var size as ExitCode

	module static function create(size ExitCode) returns Self
		return Self{size: size}
	end 'create'
end 'Config'

// --- file: api/lib.maxon
export function make() returns Config
	return Config.create(7)
end 'make'

export function sizeOf(c Config) returns ExitCode
	return c.size
end 'sizeOf'

// --- file: app/main.maxon
function main() returns ExitCode
	return sizeOf(make())
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:12:17: exported function 'api.make' names module type 'Config' in its return type
error E3167: api/<fragment>:16:17: exported function 'api.sizeOf' names module type 'Config' in the type of parameter 'c'
```

Now the guards. Each is legal: the rule is a floor under the function's own
tier, and nothing below that floor is an error.

<!-- test: a-file-private-function-may-name-a-public-type -->
The bar is one-directional. A narrow function may name anything it can see — `scoreOf` is
file-private and names two `public` declarations, which is the common case and must stay silent.
```maxon
// --- file: api/lib.maxon
public typealias Score = int(0 to 100)

public type Holder
	public var score as Score

	public static function create(score Score) returns Self
		return Self{score: score}
	end 'create'
end 'Holder'

// --- file: app/main.maxon
function scoreOf(h Holder) returns Score
	return h.score
end 'scoreOf'

function main() returns ExitCode
	return scoreOf(Holder.create(11)) as ExitCode
end 'main'
```
```exitcode
11
```

<!-- test: a-type-parameter-names-no-declaration -->
⭐ **`T` IS NOT A DECLARATION AND HAS NO TIER.** `create` and `get` are `export` and their signatures
name `T`, which every instantiation supplies — asking `T` for a modifier would refuse every generic
type's exported surface.
```maxon
// --- file: api/lib.maxon
export typealias Tally = int(0 to 100)

export type Box uses T
	export var item as T

	export static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function get() returns T
		return self.item
	end 'get'
end 'Box'

// --- file: app/main.maxon
typealias TallyBox = Box with Tally

function main() returns ExitCode
	let b = TallyBox.create(11)
	return b.get() as ExitCode
end 'main'
```
```exitcode
11
```

<!-- test: a-type-parameter-named-like-a-file-private-type-is-not-judged -->
Inside `Bag`, `Item` is `Bag`'s type parameter, whatever else in the file holds the name. The file-private
`type Item` beside it is a different declaration, and `first` names only the parameter, so nothing is asked.
```maxon
// --- file: api/lib.maxon
type Item
	export let n as ExitCode

	static function create(n ExitCode) returns Self
		return Self{n: n}
	end 'create'
end 'Item'

export type Bag uses Item
	export var held as Item

	export static function create(held Item) returns Self
		return Self{held: held}
	end 'create'

	export function first() returns Item
		return self.held
	end 'first'
end 'Bag'

export function seed() returns ExitCode
	return Item.create(4).n
end 'seed'

// --- file: app/main.maxon
typealias Tally = int(0 to 100)
typealias TallyBag = Bag with Tally

function main() returns ExitCode
	let b = TallyBag.create(7)
	return (b.first() as ExitCode) + seed()
end 'main'
```
```exitcode
11
```

<!-- test: a-public-function-naming-a-family-alias-beside-an-export-interface-is-judged-by-the-alias -->
`lib/` exports an interface `X` and `digits.maxon` declares a `public typealias X` of its own: two
declarations of one name. `f` names the alias, which is `public` like `f`, so the interface's narrower tier
is never asked. The root's own declaration is spelled `export.X`, because `digits.maxon` sees `lib/`'s too.
```maxon
// --- file: lib/shape.maxon
export interface X
	function size() returns ExitCode
end 'X'

// --- file: digits.maxon
public typealias X = int(0 to 9)

public function f(v export.X) returns ExitCode
	return v as ExitCode
end 'f'

// --- file: app/main.maxon
function sizeOf(s lib.X) returns ExitCode
	return s.size()
end 'sizeOf'

function main() returns ExitCode
	return f(7)
end 'main'
```
```exitcode
7
```

<!-- test: a-function-naming-an-alias-beside-an-invisible-interface-of-its-name-is-judged-by-the-alias -->
`lib/`'s interface `X` is `module`, so the root cannot see it and its bare `X` means its own `public` alias.
`f` names that alias, at `f`'s own tier; the interface is never asked.
```maxon
// --- file: lib/shape.maxon
module interface X
	function size() returns ExitCode
end 'X'

// --- file: lib/use.maxon
function sizeOf(s lib.X) returns ExitCode
	return s.size()
end 'sizeOf'

export function libCode() returns ExitCode
	return 2
end 'libCode'

// --- file: digits.maxon
public typealias X = int(0 to 9)

public function f(v X) returns ExitCode
	return v as ExitCode
end 'f'

// --- file: app/main.maxon
function main() returns ExitCode
	let n = f(7) + libCode()
	print("{n}\n")
	return n
end 'main'
```
```exitcode
9
```
```stdout
9
```

<!-- test: an-inner-alias-in-an-exported-method-is-not-judged-as-the-files-same-named-alias -->
Inside `Wrapper`, a bare `Idx` means `Wrapper`'s own alias, whatever the file declares under the same name.
`at` names that inner alias, which is `export` like `at` and whose right-hand side is a range naming no
declaration, so `at` is legal and the file-private `Idx` beside it is never asked. `7` is outside the file's
`Idx` and inside the inner one.
```maxon
// --- file: api/lib.maxon
typealias Idx = int(0 to 3)

export type Wrapper
	export typealias Idx = int(0 to 9)

	export static function make() returns Self
		return Self{}
	end 'make'

	export function at(i Idx) returns ExitCode
		return i as ExitCode
	end 'at'
end 'Wrapper'

function seed() returns Idx
	return 2
end 'seed'

export function base() returns ExitCode
	let n = seed()
	return n as ExitCode
end 'base'

// --- file: app/main.maxon
function main() returns ExitCode
	let n = Wrapper.make().at(7) + base()
	print("{n}\n")
	return n
end 'main'
```
```exitcode
9
```
```stdout
9
```

<!-- test: error.an-unmarked-inner-alias-in-an-exported-signature-is-refused -->
A type's inner alias carries its own modifier like any other member, and an unmarked one is file-private
however visible its type is. `Idx` sits inside the `export` type `Wrapper` with no modifier, so the
`export` method `at` names a file-private alias.
```maxon
// --- file: api/lib.maxon
export type Wrapper
	typealias Idx = int(0 to 9)

	export static function make() returns Self
		return Self{}
	end 'make'

	export function at(i Idx) returns ExitCode
		return i as ExitCode
	end 'at'
end 'Wrapper'

// --- file: app/main.maxon
function main() returns ExitCode
	return Wrapper.make().at(7)
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:10:18: exported function 'api.Wrapper.at' names file-private typealias 'Idx' in the type of parameter 'i'
```

<!-- test: error.an-extension-in-another-file-naming-a-file-private-inner-alias-is-refused -->
An extension written in another file names the type's unmarked inner alias `Arr`, which is private to the
file declaring `Wrapper`, so `api/more.maxon` cannot name it at all, and the `export` method `f` could not
hand it to a caller either.
```maxon
// --- file: api/lib.maxon
export type Wrapper
	typealias Arr = Array with ExitCode

	export static function make() returns Self
		return Self{}
	end 'make'
end 'Wrapper'

// --- file: api/more.maxon
export extension Wrapper
	export function f(xs Arr) returns ExitCode
		return xs.count() as ExitCode
	end 'f'
end 'Wrapper'

// --- file: app/main.maxon
function main() returns ExitCode
	return Wrapper.make().f([4])
end 'main'
```
```maxoncstderr
error E3008: api/<fragment>:13:23: typealias 'Arr' is not exported
```

<!-- test: error.a-qualified-file-private-inner-alias-from-another-file-is-refused -->
Qualifying the name does not widen it. `Wrapper.Idx` is unmarked, so it is private to `api/lib.maxon`
however visible `Wrapper` is, and `app/main.maxon` cannot name it as `Wrapper.Idx` any more than bare.
```maxon
// --- file: api/lib.maxon
export type Wrapper
	typealias Idx = int(0 to 9)

	export static function make() returns Self
		return Self{}
	end 'make'

	export function code() returns ExitCode
		return 2
	end 'code'
end 'Wrapper'

// --- file: app/main.maxon
function g(i Wrapper.Idx) returns ExitCode
	return i as ExitCode
end 'g'

function main() returns ExitCode
	return g(3) + Wrapper.make().code()
end 'main'
```
```maxoncstderr
error E3008: app/<fragment>:16:14: typealias 'Wrapper.Idx' is not exported
```

<!-- test: error.a-per-instance-spelling-of-a-file-private-inner-alias-is-refused -->
An instance alias reaches the same member. `Pools` names `Pool with ExitCode`, so `Pools.Idx` is that
instance's copy of `Pool`'s unmarked inner alias, private to `api/lib.maxon`, and naming it from
`app/main.maxon` is refused as the generic's own spelling is.
```maxon
// --- file: api/lib.maxon
export type Pool uses T
	typealias Idx = int(0 to 9)
	var item as T

	export static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function code() returns ExitCode
		return 2
	end 'code'
end 'Pool'

// --- file: app/main.maxon
typealias Pools = Pool with ExitCode

function g(i Pools.Idx) returns ExitCode
	return i as ExitCode
end 'g'

function main() returns ExitCode
	return g(3) + Pools.create(1).code()
end 'main'
```
```maxoncstderr
error E3008: app/<fragment>:19:14: typealias 'Pools.Idx' is not exported
```

<!-- test: equal-tiers-are-legal -->
Equal is enough at every tier — the rule asks "at least as visible", not "wider". Both pairs are here
in one program so that a check written as a strict comparison fails on both.
```maxon
// --- file: api/lib.maxon
export typealias Score = int(0 to 100)
module typealias Slot = int(0 to 100)

export function scored(s Score) returns Score
	return s
end 'scored'

module function slotted(s Slot) returns Slot
	return s
end 'slotted'

// --- file: api/main.maxon
function widen(s Score, t Slot) returns ExitCode
	print("{s}{t}")
	return 0
end 'widen'

function main() returns ExitCode
	return widen(scored(4), t: slotted(3))
end 'main'
```
```exitcode
0
```
```stdout
43
```

<!-- test: a-users-own-byte-does-not-judge-the-librarys-signatures -->
⭐⭐ **THE LIBRARY IS NEVER HELD AGAINST AN AUTHOR'S DECLARATION.** `api/lib.maxon` declares its own
`export typealias Byte`, and `stdlib/` is full of `public` signatures naming a `Byte` of its own. Those
two names never meet: a library file resolves a bare name to the library's declaration, and a signature
written in `stdlib/` is measured against the library's own tiers. Without that, one `export typealias`
in a user file reports E3167 against a dozen library declarations the author never opened.
```maxon
// --- file: api/lib.maxon
export typealias Byte = int(300 to 1000)

export function widen(b Byte) returns Byte
	return b
end 'widen'

// --- file: app/main.maxon
function main() returns ExitCode
	let s = "hi"
	var n = 0 as ExitCode

	for b in s.bytes() 'each'
		n = n + (b as ExitCode)
	end 'each'

	return (n + ((widen(400) - 400) as ExitCode)) as ExitCode
end 'main'
```
```exitcode
209
```

<!-- test: a-users-own-generic-alias-does-not-reach-the-librarys-readers -->
⭐ **THE SAME LAYER RULE AT THE GENERIC-INSTANCE DOOR.** `api/lib.maxon` declares an `export typealias
ByteArray` of its own, over a two-byte element, and `stdlib/File.maxon` declares the library's. `stdlib/Sha2.maxon`
declares none of the name and its `public function sha256(data ByteArray) returns ByteArray` names it, so
the library's own declaration has to answer there; the author's alias answers only where the author wrote
it.
```maxon
// --- file: api/lib.maxon
export typealias Tally = int(0 to 1000)
export typealias ByteArray = Array with Tally

export function widest(xs ByteArray) returns Tally
	var top = 0 as Tally

	for x in xs 'each'
		top = x if x > top else top
	end 'each'

	return top
end 'widest'

// --- file: app/main.maxon
function main() returns ExitCode
	let digest = sha256("hi".toByteArray())
	var tallies = api.ByteArray.create()
	tallies.push(900)

	return ((widest(tallies) - 900) + (digest.count() as Tally)) as ExitCode
end 'main'
```
```exitcode
32
```

<!-- test: error.a-reached-module-demand-does-not-excuse-an-export -->
⚠ **A REACHED DEMAND EXCUSES ONLY THE TIER IT ASKS FOR.** `hold` is `module` and `pkg/sub/user.maxon`
reaches it, so the demand on `Item` is reached and E3092 is rightly silent — but what a `module` function
needs is a `module` type. `Item` is `export`, nothing outside the subtree names it, and E3093 is the
verdict the audit gives without the demand at all.
```maxon
// --- file: pkg/lib.maxon
export type Item
	export var n as ExitCode

	export static function create(n ExitCode) returns Self
		return Self{n: n}
	end 'create'
end 'Item'

module function hold(it Item) returns ExitCode
	return it.n
end 'hold'

// --- file: pkg/sub/user.maxon
export function entry() returns ExitCode
	return hold(Item.create(7))
end 'entry'

// --- file: main.maxon
function main() returns ExitCode
	return entry()
end 'main'
```
```maxoncstderr
error E3093: pkg/<fragment>:3:13: exported type 'Item' is only referenced inside its declaring module subtree; consider 'module' visibility
```

<!-- test: error.a-contested-generic-alias-is-judged-by-the-readers-own-declaration -->
⚠ **THE TIER IS THE READER'S DECLARATION'S, NOT THE FIRST ONE FILED UNDER THE NAME.** The library declares a
`public` `StringArray`, and `pkg/lib.maxon` keeps a file-private one of its own, which is what its bare
`StringArray` means because the library takes no part in ambiguity. `pkg.apply` is `export` and its `Handler`
is spelled over `pkg/lib.maxon`'s `StringArray`, so that is the declaration it is held against — a door that
answered with the library's declaration would read its `public` tier and refuse nothing, as it refuses nothing
for `alpha.count`, whose `StringArray` IS the library's.
```maxon
// --- file: alpha/lib.maxon
export typealias Tally = int(0 to 100)

export function count(b StringArray) returns Tally
	return b.count() as Tally
end 'count'

// --- file: pkg/lib.maxon
typealias StringArray = Array with Tally

export typealias Handler = function(StringArray) returns Tally

export function apply(h Handler, b StringArray) returns Tally
	return h(b)
end 'apply'

// --- file: app/main.maxon
function main() returns ExitCode
	var b = StringArray.create()
	b.push("x")

	return count(b) as ExitCode
end 'main'
```
```maxoncstderr
error E3167: pkg/<fragment>:14:17: exported function 'pkg.apply' names file-private typealias 'StringArray' in the type of parameter 'h'
error E3167: pkg/<fragment>:14:17: exported function 'pkg.apply' names file-private typealias 'StringArray' in the type of parameter 'b'
error E3167: pkg/<fragment>:12:18: exported typealias 'pkg.Handler' names file-private typealias 'StringArray' in its target
```

<!-- test: error.an-alias-walked-from-another-file-judges-its-components-in-its-own-file -->
⚠ **AN ALIAS'S COMPONENTS MEAN WHAT THEY MEAN WHERE THE ALIAS IS WRITTEN.** `Handler` is declared in
`pkg/lib.maxon`, whose bare `StringArray` is its own file-private alias. `pkg/use.maxon` declares no
`StringArray`, so there the bare name would mean the library's `public` one. `apply` is written in
`pkg/use.maxon`, but the `StringArray` it reaches through `Handler` is `pkg/lib.maxon`'s, and that is the
declaration it is held against.
```maxon
// --- file: pkg/lib.maxon
typealias StringArray = Array with ExitCode

export typealias Handler = function(StringArray) returns ExitCode

// --- file: pkg/use.maxon
export function apply(h Handler) returns ExitCode
	return h([7])
end 'apply'

// --- file: app/main.maxon
function main() returns ExitCode
	return apply(function(xs) gives xs.count() as ExitCode)
end 'main'
```
```maxoncstderr
error E3167: pkg/<fragment>:8:17: exported function 'pkg.apply' names file-private typealias 'StringArray' in the type of parameter 'h'
error E3167: pkg/<fragment>:5:18: exported typealias 'pkg.Handler' names file-private typealias 'StringArray' in its target
```

<!-- test: error.a-function-alias-over-a-file-private-instance-element-is-refused -->
A function alias's parameter that is a generic alias is asked like any other component. `Cb` is `export`,
and its parameter `Secrets` is file-private, so `f` names a file-private alias. The walk stops at the first
narrower declaration it reaches, so `Secret` inside `Secrets` is not reported as well.
```maxon
// --- file: api/lib.maxon
typealias Secret = int(0 to 9)
typealias Secrets = Array with Secret

export typealias Cb = function(Secrets) returns ExitCode

export function f(cb Cb) returns ExitCode
	var xs = Secrets.create()
	xs.push(4)
	return cb(xs)
end 'f'

// --- file: app/main.maxon
function main() returns ExitCode
	return f(function(xs) gives xs.count() as ExitCode)
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:8:17: exported function 'api.f' names file-private typealias 'Secrets' in the type of parameter 'cb'
error E3167: api/<fragment>:6:18: exported typealias 'api.Cb' names file-private typealias 'Secrets' in its target
```

<!-- test: error.a-file-private-tuple-alias-in-an-exported-signature-is-refused -->
A tuple alias is a declaration like any other alias form. The tuple it stands for names no declaration, but
the ALIAS does, and the parameter is written with the alias, so the alias's own tier is asked.
```maxon
// --- file: api/lib.maxon
typealias Pair = (ExitCode, ExitCode)

export function first(p Pair) returns ExitCode
	return p.0
end 'first'

// --- file: app/main.maxon
function main() returns ExitCode
	return first((7, 1))
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:5:17: exported function 'api.first' names file-private typealias 'Pair' in the type of parameter 'p'
```

## A service MESSAGE is judged at its service type's tier

An `export`/`public` INSTANCE method of a spawned type is not an ordinary member: `export` there spells
MESSAGE, which is what puts the method on `Worker.handle` (`specs/services.md:701`). What a message's
signature has to be nameable from is therefore whoever can name the SERVICE TYPE, not whoever can name
the word `export` — so a message is held to its service type's written tier. Every other member keeps
the rule above: its own modifier is the bar.

<!-- test: a-service-message-may-name-a-file-private-type -->
⭐⭐ **THE MESSAGE'S `export` MEANS "MESSAGE", NOT "WIDER THAN THE TYPE".** `Worker` is file-private and so
is `Report`, and the only spelling that can reach `run` is a handle produced by a `spawn` written in this
same file — nothing outside it can name the type, hold the handle or see the reply. Judging `run` by its
own modifier reports E3167 on a reply no other file can even receive, which would make every file-private
service's message surface unwritable over the service's own types.
```maxon
// --- file: api/lib.maxon
typealias Tally = int(0 to 100)

type Report
	export let n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Report'

type Worker
	var seed as Tally

	static function create(seed Tally) returns Self
		return Self{seed: seed}
	end 'create'

	export function run() returns Report
		return Report.create(self.seed)
	end 'run'
end 'Worker'

export function tally() returns ExitCode
	let h = spawn Worker.create(7)
	let r = try await h.run() otherwise Report.create(0)
	return r.n as ExitCode
end 'tally'

// --- file: app/main.maxon
function main() returns ExitCode
	return tally()
end 'main'
```
```exitcode
7
```

<!-- test: error.an-exported-services-message-names-a-file-private-type -->
⭐⭐ **THE EXEMPTION STOPS AT THE SERVICE TYPE'S TIER, AND ONE WORD IS THE WHOLE DIFFERENCE.** This program
is the case above with `export` written on `type Worker`; every other byte is the same. Now any file may
name `Worker`, spawn one and hold the handle `run` sits on — so `run`'s reply has to be nameable at that
tier, and `Report` is not. A message exempt from the rule outright, rather than moved to its type's tier,
would let an exported service hand a file-private record to a caller with no spelling for it.

⚠ `Worker` is named nowhere outside `api/lib.maxon`, which is E3092 — and `checkUnusedExports` audits only
a program no other check has refused (`UnusedExportCheck`), so the stderr below is the whole of it.
```maxon
// --- file: api/lib.maxon
typealias Tally = int(0 to 100)

type Report
	export let n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Report'

export type Worker
	var seed as Tally

	static function create(seed Tally) returns Self
		return Self{seed: seed}
	end 'create'

	export function run() returns Report
		return Report.create(self.seed)
	end 'run'
end 'Worker'

export function tally() returns ExitCode
	let h = spawn Worker.create(7)
	let r = try await h.run() otherwise Report.create(0)
	return r.n as ExitCode
end 'tally'

// --- file: app/main.maxon
function main() returns ExitCode
	return tally()
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:20:18: exported function 'api.Worker.run' names file-private type 'Report' in its return type
```

<!-- test: error.a-contested-function-alias-is-named-as-written -->
Two directories declare a file-private `Op` over two shapes, so each declaration is held under a name of its
own inside the compiler. The refusal still names `Op`, which is what the author wrote.
```maxon
// --- file: aaa/a.maxon
export typealias Small = int(0 to 1000)
typealias Op = function(Small) returns Small

export function twice(f Op) returns Small
	return f(f(1))
end 'twice'

// --- file: zzz/z.maxon
typealias Op = function(ExitCode) returns ExitCode

function applyOp(f Op) returns ExitCode
	return f(1)
end 'applyOp'

export function once() returns ExitCode
	return applyOp(function(x) gives x)
end 'once'

// --- file: app/main.maxon
function main() returns ExitCode
	return (aaa.twice(function(x) gives x + 1) as ExitCode) + zzz.once()
end 'main'
```
```maxoncstderr
error E3167: aaa/<fragment>:6:17: exported function 'aaa.twice' names file-private typealias 'Op' in the type of parameter 'f'
```

## A top-level binding is judged by its type

<!-- test: error.an-exported-let-of-a-file-private-enum-type-is-refused -->
A binding is part of the surface its tier opens, and a reader of `BIG` holds a `Status` it has no spelling for.
The refusal sits at the binding NAME, in the words a function gets, and the noun is the binding's own — a
`let` is a "constant" and a `var` a "variable".
```maxon
// --- file: api/lib.maxon
enum Status
	big
	small
end 'Status'

export let BIG = Status.big

// --- file: app/main.maxon
function main() returns ExitCode
	return 7 if BIG == BIG else 0
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:8:12: exported constant 'api.BIG' names file-private enum 'Status' in its type
```

<!-- test: error.a-module-let-of-a-file-private-enum-type-is-refused -->
The tier word follows the binding's own modifier, as a function's does.
```maxon
// --- file: api/lib.maxon
enum Status
	big
	small
end 'Status'

module let BIG = Status.big

// --- file: api/main.maxon
function main() returns ExitCode
	return 7 if BIG == BIG else 0
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:8:12: module constant 'api.BIG' names file-private enum 'Status' in its type
```

<!-- test: error.a-public-let-of-a-module-type-is-refused -->
`public` outranks every other tier, so a `public` binding may not hold even an `export` type, and a `module`
type is two tiers short. The type here comes from the initializer: `Config.create(7)` is the whole of what
says what `DEFAULT` holds.
```maxon
// --- file: api/config.maxon
module type Config
	module var size as ExitCode

	module static function create(size ExitCode) returns Self
		return Self{size: size}
	end 'create'
end 'Config'

public let DEFAULT = Config.create(7)

// --- file: api/main.maxon
function main() returns ExitCode
	return DEFAULT.size
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:11:12: public constant 'api.DEFAULT' names module type 'Config' in its type
```

<!-- test: error.an-exported-var-of-an-inferred-file-private-record-is-refused -->
No type is written anywhere on the binding: the record is the value of the initializer and nothing else
names it.

⚠ `SHARED` is read nowhere outside `api/lib.maxon`, which is E3092, and `checkUnusedExports` audits only a
program no other check has refused (`UnusedExportCheck`), so the stderr below is the whole of it. A
reader that touched `SHARED.n` would be refused at its own use, before this pass runs.
```maxon
// --- file: api/lib.maxon
type Secret
	export var n as ExitCode

	static function create(n ExitCode) returns Self
		return Self{n: n}
	end 'create'
end 'Secret'

export var SHARED = Secret.create(7)

// --- file: app/main.maxon
function main() returns ExitCode
	return 7
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:11:12: exported variable 'api.SHARED' names file-private type 'Secret' in its type
```

<!-- test: error.an-export-static-let-of-a-file-private-enum-type-is-refused -->
A `static let` is a binding with a tier of its own, and the member's own modifier is the bar, not its type's:
`Holder` is `export` and `Status` is not.
```maxon
// --- file: api/lib.maxon
enum Status
	big
	small
end 'Status'

export type Holder
	export static let DEFAULT = Status.big
end 'Holder'

// --- file: app/main.maxon
function main() returns ExitCode
	return 7 if Holder.DEFAULT == Holder.DEFAULT else 0
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:9:20: exported constant 'api.Holder.DEFAULT' names file-private enum 'Status' in its type
```

<!-- test: error.an-exported-let-cast-to-a-file-private-alias-is-refused -->
A constant keeps the alias its initializer was cast to, and the alias is a declaration like any other.
```maxon
// --- file: api/lib.maxon
typealias Small = int(0 to 100)

export let CAP = 50 as Small

// --- file: app/main.maxon
function main() returns ExitCode
	return CAP as ExitCode
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:5:12: exported constant 'api.CAP' names file-private typealias 'Small' in its type
```

<!-- test: error.an-exported-let-built-through-a-file-private-generic-alias-is-refused -->
The alias the initializer was spelled through is the binding's own spelling of its type. `Array with ExitCode` is
nameable by anyone, `Tallies` is not, and the binding is read under that name.
```maxon
// --- file: api/lib.maxon
typealias Tallies = Array with ExitCode

export let EMPTY = Tallies.create()

// --- file: app/main.maxon
function main() returns ExitCode
	return EMPTY.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:5:12: exported constant 'api.EMPTY' names file-private typealias 'Tallies' in its type
```

<!-- test: an-exported-let-of-an-exported-enum-is-legal -->
The control for the refusals above: `Status` is `export`, so `BIG` may hold one. The reader names no type —
`main` matches on the cases — and the type is still credited as needed by `BIG`, so the unused-export
audit has nothing to say about it.
```maxon
// --- file: api/lib.maxon
export enum Status
	big
	small
end 'Status'

export let BIG = Status.big

// --- file: app/main.maxon
function main() returns ExitCode
	return match BIG 'which'
		big gives 7
		small gives 1
	end 'which'
end 'main'
```
```exitcode
7
```

<!-- test: a-file-private-let-of-a-file-private-type-is-legal -->
A binding no narrower than its type is not asked anything: both are file-private, and nothing outside
`api/lib.maxon` can reach either.
```maxon
// --- file: api/lib.maxon
enum Status
	big
	small
end 'Status'

let BIG = Status.big

export function code() returns ExitCode
	return match BIG 'which'
		big gives 7
		small gives 1
	end 'which'
end 'code'

// --- file: app/main.maxon
function main() returns ExitCode
	return code()
end 'main'
```
```exitcode
7
```

<!-- test: exported-lets-of-scalar-string-and-array-types-are-legal -->
Primitives, `String` and a library container name no declaration an author wrote, so no tier is asked of them.
```maxon
// --- file: api/lib.maxon
export let LIMIT = 7
export let GREETING = "hi"
export let PRIMES = [2, 3, 5]

// --- file: app/main.maxon
function main() returns ExitCode
	print("{GREETING}{PRIMES.count()}")
	return LIMIT
end 'main'
```
```exitcode
7
```
```stdout
hi3
```

<!-- test: an-inferred-library-type-is-not-judged-as-a-same-named-private-one -->
`api/lib.maxon` declares a file-private `HeapCensusMode` of its own, beside the library's `public` one. `MODE`
holds `stdlib.HeapCensusMode.byTag`, so its type is the library's enum: the declaration the initializer
reached, not whichever `HeapCensusMode` the binding's own file declares.
```maxon
// --- file: api/lib.maxon
enum HeapCensusMode
	first
	second
end 'HeapCensusMode'

function own() returns HeapCensusMode
	return HeapCensusMode.first
end 'own'

export let MODE = stdlib.HeapCensusMode.byTag

// --- file: app/main.maxon
function main() returns ExitCode
	return match MODE 'which'
		byTag gives 7
		bySizeClass gives 1
	end 'which'
end 'main'
```
```exitcode
7
```

<!-- test: error.an-exported-let-of-a-private-enum-beside-the-librarys-is-refused -->
The pair of the case above: the binding holds the file's own `HeapCensusMode`, and the refusal names the
file-private declaration the initializer reached.
```maxon
// --- file: api/lib.maxon
enum HeapCensusMode
	first
	second
end 'HeapCensusMode'

export let MODE = HeapCensusMode.first

// --- file: app/main.maxon
function main() returns ExitCode
	return 7
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:8:12: exported constant 'api.MODE' names file-private enum 'HeapCensusMode' in its type
```

<!-- test: a-parameter-naming-the-librarys-type-is-not-judged-as-a-same-named-private-one -->
A function's parameter type is judged by the declaration it names, the same as a binding's type:
`stdlib.HeapCensusMode` is the library's `public` enum, and the file-private `HeapCensusMode` beside the
function is another type.
```maxon
// --- file: api/lib.maxon
enum HeapCensusMode
	first
	second
end 'HeapCensusMode'

function own() returns HeapCensusMode
	return HeapCensusMode.first
end 'own'

export function flip(m stdlib.HeapCensusMode) returns stdlib.HeapCensusMode
	return match m 'which'
		byTag gives stdlib.HeapCensusMode.bySizeClass
		bySizeClass gives stdlib.HeapCensusMode.byTag
	end 'which'
end 'flip'

// --- file: app/main.maxon
function main() returns ExitCode
	return match flip(stdlib.HeapCensusMode.bySizeClass) 'which'
		byTag gives 7
		bySizeClass gives 1
	end 'which'
end 'main'
```
```exitcode
7
```

<!-- test: equal-tiers-are-legal-for-bindings -->
Equal is enough for a binding too, at both tiers that name a type: `module` beside `module`, `public` beside
`public`.
```maxon
// --- file: api/lib.maxon
module typealias Slot = int(0 to 100)
public typealias Score = int(0 to 100)

module let FIRST = 3 as Slot
public let TOP = 9 as Score

// --- file: api/main.maxon
function main() returns ExitCode
	return (FIRST as ExitCode) + (TOP as ExitCode)
end 'main'
```
```exitcode
12
```

## Every other surface that names a type is judged by its type

A type's members and clauses are part of the surface its tier opens, exactly as a function's signature or a
binding's type is. The refusal is E3167 in the words a function gets, with the noun and the position of the
surface:

| surface | noun | position | its tier |
|---------|------|----------|----------|
| an instance field | `field` | `in its type` | the field's own modifier, bounded by its type's |
| a union case's payload | `union case` | `in the type of payload 'name'` | the union's |
| a typealias | `typealias` | `in its target` | the alias's |
| an `implements` clause, with its `with` arguments | `type`, `enum` or `union` | `in its implements clause` | the declaration's |
| a `where` constraint | `type` | `in its where clause` | the declaration's |
| an `extends` clause | `interface` | `in its extends clause` | the interface's |

<!-- test: error.an-exported-field-of-a-file-private-enum-type-is-refused -->
A field reachable from outside hands its readers a `Mode` they have no spelling for. The refusal sits at the
type the field names, and the field is called by its qualified name.
```maxon
// --- file: api/lib.maxon
enum Mode
	fast
	slow
end 'Mode'

export type Holder
	export var mode as Mode

	export static function create() returns Holder
		return Holder{mode: Mode.fast}
	end 'create'

	export function code() returns ExitCode
		return match self.mode 'which'
			fast gives 7
			slow gives 1
		end 'which'
	end 'code'
end 'Holder'

// --- file: app/main.maxon
function main() returns ExitCode
	return Holder.create().code()
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:9:21: exported field 'api.Holder.mode' names file-private enum 'Mode' in its type
```

<!-- test: error.an-exported-field-of-a-module-enum-type-is-refused -->
`module` is one tier short of `export`, and the tier word follows the type.
```maxon
// --- file: api/lib.maxon
module enum Mode
	fast
	slow
end 'Mode'

export type Holder
	export var mode as Mode

	export static function create() returns Holder
		return Holder{mode: Mode.fast}
	end 'create'

	export function code() returns ExitCode
		return match self.mode 'which'
			fast gives 7
			slow gives 1
		end 'which'
	end 'code'
end 'Holder'

// --- file: app/main.maxon
function main() returns ExitCode
	return Holder.create().code()
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:9:21: exported field 'api.Holder.mode' names module enum 'Mode' in its type
```

<!-- test: error.an-exported-field-typed-by-a-tuple-of-a-file-private-alias-is-refused -->
A field's type is judged as a parameter's is, so a tuple spelled over a file-private alias is refused at the field: the element's alias is a declaration like any other.
```maxon
// --- file: api/lib.maxon
typealias Scores = Array with ExitCode

export type Holder
	export var pair as (Scores, ExitCode)

	export static function create() returns Holder
		return Holder{pair: (Scores.create(), 7)}
	end 'create'
end 'Holder'

// --- file: app/main.maxon
function main() returns ExitCode
	return Holder.create().pair.1
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:5:13: exported field 'api.Holder.pair' names file-private typealias 'Scores' in its type
```

<!-- test: error.an-exported-field-typed-by-a-tuple-of-a-contested-file-private-alias-is-refused -->
Two files declaring one `Scores` over one instance make it a contested name, and the field is still judged at the alias its own file wrote.
```maxon
// --- file: api/lib.maxon
typealias Scores = Array with ExitCode

export type Holder
	export var pair as (Scores, ExitCode)

	export static function create() returns Holder
		return Holder{pair: (Scores.create(), 7)}
	end 'create'
end 'Holder'

// --- file: app/main.maxon
typealias Scores = Array with ExitCode

function tally(scores Scores) returns ExitCode
	return scores.count() as ExitCode
end 'tally'

function main() returns ExitCode
	return tally(Scores.create()) + Holder.create().pair.1
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:5:13: exported field 'api.Holder.pair' names file-private typealias 'Scores' in its type
```

<!-- test: error.an-exported-field-typed-by-a-contested-file-private-generic-alias-is-refused -->
The same contest without a tuple: the field names `Scores` directly, and `Scores` is declared in both files over one instance.
```maxon
// --- file: api/lib.maxon
typealias Scores = Array with ExitCode

export type Holder
	export var scores as Scores

	export static function create() returns Holder
		return Holder{scores: Scores.create()}
	end 'create'
end 'Holder'

// --- file: app/main.maxon
typealias Scores = Array with ExitCode

function tally(scores Scores) returns ExitCode
	return scores.count() as ExitCode
end 'tally'

function main() returns ExitCode
	return tally(Scores.create()) + Holder.create().scores.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:6:23: exported field 'api.Holder.scores' names file-private typealias 'Scores' in its type
```

<!-- test: error.a-module-field-is-judged-at-its-own-modifier -->
⭐ **A FIELD'S OWN MODIFIER IS THE TIER, NOT ITS TYPE'S.** `Holder` is `export` and `mode` is `module`, so the
field is held to `module`, and a file-private `Mode` is below it.
```maxon
// --- file: api/lib.maxon
enum Mode
	fast
	slow
end 'Mode'

export type Holder
	module var mode as Mode

	export static function create() returns Holder
		return Holder{mode: Mode.fast}
	end 'create'

	export function code() returns ExitCode
		return match self.mode 'which'
			fast gives 7
			slow gives 1
		end 'which'
	end 'code'
end 'Holder'

// --- file: app/main.maxon
function main() returns ExitCode
	return Holder.create().code()
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:9:21: module field 'api.Holder.mode' names file-private enum 'Mode' in its type
```

<!-- test: a-module-field-of-an-exported-type-may-name-a-module-enum -->
The control: the field is `module`, `Mode` is `module`, and `Holder` is `export`. Nothing outside `api/` can read
`mode`, so nothing outside needs to name `Mode`. `Mode` is not named outside `lib.maxon` at all — the field's
demand is what keeps it from E3094.
```maxon
// --- file: api/lib.maxon
module enum Mode
	fast
	slow
end 'Mode'

export type Holder
	module var mode as Mode

	export static function create() returns Holder
		return Holder{mode: Mode.fast}
	end 'create'
end 'Holder'

// --- file: api/other.maxon
export function modeCode(h Holder) returns ExitCode
	return match h.mode 'which'
		fast gives 7
		slow gives 1
	end 'which'
end 'modeCode'

// --- file: app/main.maxon
function main() returns ExitCode
	return modeCode(Holder.create())
end 'main'
```
```exitcode
7
```

<!-- test: an-export-field-of-a-module-type-is-bounded-by-the-type -->
The other half of the bound: `Holder` is `module`, so its `export` field reaches no further than `module`, and a
`module` `Mode` is enough.
```maxon
// --- file: api/lib.maxon
module enum Mode
	fast
	slow
end 'Mode'

module type Holder
	export var mode as Mode

	module static function create() returns Holder
		return Holder{mode: Mode.fast}
	end 'create'
end 'Holder'

// --- file: api/main.maxon
function main() returns ExitCode
	let h = Holder.create()

	return match h.mode 'which'
		fast gives 7
		slow gives 1
	end 'which'
end 'main'
```
```exitcode
7
```

<!-- test: an-exported-field-of-an-exported-enum-is-legal -->
Equal tiers. The reader names no type, and `Mode` is credited as needed by `mode`, so the unused-export audit
has nothing to say about it.
```maxon
// --- file: api/lib.maxon
export enum Mode
	fast
	slow
end 'Mode'

export type Holder
	export var mode as Mode

	export static function create() returns Holder
		return Holder{mode: Mode.fast}
	end 'create'

	export function code() returns ExitCode
		return match self.mode 'which'
			fast gives 7
			slow gives 1
		end 'which'
	end 'code'
end 'Holder'

// --- file: app/main.maxon
function main() returns ExitCode
	return Holder.create().code()
end 'main'
```
```exitcode
7
```

<!-- test: an-unmarked-field-of-an-exported-type-may-name-a-file-private-enum -->
An unmarked field is private to its type, so no caller can read it and nothing is owed a spelling of its type.
```maxon
// --- file: api/lib.maxon
enum Mode
	fast
	slow
end 'Mode'

export type Holder
	var mode as Mode

	export static function create() returns Holder
		return Holder{mode: Mode.fast}
	end 'create'

	export function code() returns ExitCode
		return match self.mode 'which'
			fast gives 7
			slow gives 1
		end 'which'
	end 'code'
end 'Holder'

// --- file: app/main.maxon
function main() returns ExitCode
	let h = Holder.create()
	return h.code()
end 'main'
```
```exitcode
7
```

<!-- test: an-export-field-of-a-file-private-type-may-name-a-file-private-enum -->
A file-private type bounds its `export` field to file-private, so the field and the enum sit at one tier.
```maxon
// --- file: api/lib.maxon
enum Mode
	fast
	slow
end 'Mode'

type Holder
	export var mode as Mode

	static function create() returns Holder
		return Holder{mode: Mode.fast}
	end 'create'
end 'Holder'

export function code() returns ExitCode
	let h = Holder.create()

	return match h.mode 'which'
		fast gives 7
		slow gives 1
	end 'which'
end 'code'

// --- file: app/main.maxon
function main() returns ExitCode
	return code()
end 'main'
```
```exitcode
7
```

<!-- test: error.an-exported-union-case-payload-of-a-file-private-enum-type-is-refused -->
A payload is read by whoever matches the union, so it is held to the union's tier. The union writes no modifier
on a case, and the refusal sits at the payload's type.
```maxon
// --- file: api/lib.maxon
enum Mode
	fast
	slow
end 'Mode'

export union Result
	ok(v Mode)
	failed
end 'Result'

export function sample() returns Result
	return Result.ok(Mode.fast)
end 'sample'

// --- file: app/main.maxon
function main() returns ExitCode
	return match sample() 'which'
		ok(v) gives 7 if v == v else 0
		failed gives 1
	end 'which'
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:9:7: exported union case 'api.Result.ok' names file-private enum 'Mode' in the type of payload 'v'
```

<!-- test: error.an-exported-union-case-payload-of-a-module-enum-type-is-refused -->
```maxon
// --- file: api/lib.maxon
module enum Mode
	fast
	slow
end 'Mode'

export union Result
	ok(v Mode)
	failed
end 'Result'

export function sample() returns Result
	return Result.ok(Mode.fast)
end 'sample'

// --- file: app/main.maxon
function main() returns ExitCode
	return match sample() 'which'
		ok(v) gives 7 if v == v else 0
		failed gives 1
	end 'which'
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:9:7: exported union case 'api.Result.ok' names module enum 'Mode' in the type of payload 'v'
```

<!-- test: an-exported-union-case-payload-of-an-exported-enum-is-legal -->
Equal tiers, and `Mode` is named by the payload alone.
```maxon
// --- file: api/lib.maxon
export enum Mode
	fast
	slow
end 'Mode'

export union Result
	ok(v Mode)
	failed
end 'Result'

export function sample() returns Result
	return Result.ok(Mode.fast)
end 'sample'

// --- file: app/main.maxon
function main() returns ExitCode
	return match sample() 'which'
		ok(v) gives 7 if v == v else 0
		failed gives 1
	end 'which'
end 'main'
```
```exitcode
7
```

<!-- test: a-file-private-union-case-payload-may-name-a-file-private-enum -->
```maxon
// --- file: api/lib.maxon
enum Mode
	fast
	slow
end 'Mode'

union Result
	ok(v Mode)
	failed
end 'Result'

export function code() returns ExitCode
	return match Result.ok(Mode.fast) 'which'
		ok(v) gives 7 if v == v else 0
		failed gives 1
	end 'which'
end 'code'

// --- file: app/main.maxon
function main() returns ExitCode
	return code()
end 'main'
```
```exitcode
7
```

<!-- test: error.an-exported-generic-alias-over-a-file-private-enum-is-refused -->
An alias is nameable wherever its tier reaches, and so is what it stands for. The refusal sits at the alias NAME.
```maxon
// --- file: api/lib.maxon
enum Mode
	fast
	slow
end 'Mode'

export typealias ModeArray = Array with Mode

// --- file: app/main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:8:18: exported typealias 'api.ModeArray' names file-private enum 'Mode' in its target
```

<!-- test: error.an-exported-generic-alias-over-a-module-enum-is-refused -->
```maxon
// --- file: api/lib.maxon
module enum Mode
	fast
	slow
end 'Mode'

export typealias ModeArray = Array with Mode

// --- file: app/main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:8:18: exported typealias 'api.ModeArray' names module enum 'Mode' in its target
```

<!-- test: error.an-exported-function-alias-over-a-file-private-enum-is-refused -->
```maxon
// --- file: api/lib.maxon
enum Mode
	fast
	slow
end 'Mode'

export typealias Chooser = function(Mode) returns ExitCode

// --- file: app/main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:8:18: exported typealias 'api.Chooser' names file-private enum 'Mode' in its target
```

<!-- test: error.an-exported-tuple-alias-over-a-file-private-enum-is-refused -->
```maxon
// --- file: api/lib.maxon
enum Mode
	fast
	slow
end 'Mode'

export typealias ModePair = (Mode, Mode)

// --- file: app/main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:8:18: exported typealias 'api.ModePair' names file-private enum 'Mode' in its target
```

<!-- test: an-exported-generic-alias-over-an-exported-enum-is-legal -->
Equal tiers; `Mode` is named by the alias alone, which the reader uses.
```maxon
// --- file: api/lib.maxon
export enum Mode
	fast
	slow
end 'Mode'

export typealias ModeArray = Array with Mode

// --- file: app/main.maxon
function main() returns ExitCode
	var modes = ModeArray.create()
	return (modes.count() as ExitCode) + 7
end 'main'
```
```exitcode
7
```

<!-- test: a-module-alias-over-a-module-enum-is-legal -->
```maxon
// --- file: api/lib.maxon
module enum Mode
	fast
	slow
end 'Mode'

module typealias ModeArray = Array with Mode

// --- file: api/main.maxon
function main() returns ExitCode
	var modes = ModeArray.create()
	return (modes.count() as ExitCode) + 7
end 'main'
```
```exitcode
7
```

<!-- test: a-file-private-alias-over-a-file-private-enum-is-legal -->
```maxon
// --- file: api/lib.maxon
enum Mode
	fast
	slow
end 'Mode'

typealias ModeArray = Array with Mode

export function code() returns ExitCode
	var modes = ModeArray.create()
	return (modes.count() as ExitCode) + 7
end 'code'

// --- file: app/main.maxon
function main() returns ExitCode
	return code()
end 'main'
```
```exitcode
7
```

<!-- test: error.an-exported-type-implementing-a-file-private-interface-is-refused -->
Whoever can name `Holder` can ask what it implements. The refusal sits at the type NAME.
```maxon
// --- file: api/lib.maxon
interface Marker
	function code() returns ExitCode
end 'Marker'

export type Holder implements Marker
	export function code() returns ExitCode
		return 7
	end 'code'

	export static function create() returns Holder
		return Holder{}
	end 'create'
end 'Holder'

// --- file: app/main.maxon
function main() returns ExitCode
	return Holder.create().code()
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:7:13: exported type 'api.Holder' names file-private interface 'Marker' in its implements clause
```

<!-- test: error.an-exported-type-implementing-a-module-interface-is-refused -->
```maxon
// --- file: api/lib.maxon
module interface Marker
	function code() returns ExitCode
end 'Marker'

export type Holder implements Marker
	export function code() returns ExitCode
		return 7
	end 'code'

	export static function create() returns Holder
		return Holder{}
	end 'create'
end 'Holder'

// --- file: app/main.maxon
function main() returns ExitCode
	return Holder.create().code()
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:7:13: exported type 'api.Holder' names module interface 'Marker' in its implements clause
```

<!-- test: error.an-exported-type-binding-a-file-private-enum-in-its-implements-clause-is-refused -->
The `with` arguments of a conformance are named types of the declaration.
```maxon
// --- file: api/lib.maxon
enum Mode
	fast
	slow
end 'Mode'

export interface Source uses Element
	function next() returns Element
end 'Source'

export type Holder implements Source with Mode
	function next() returns Mode
		return Mode.fast
	end 'next'

	export static function create() returns Holder
		return Holder{}
	end 'create'

	export function code() returns ExitCode
		return match self.next() 'which'
			fast gives 7
			slow gives 1
		end 'which'
	end 'code'
end 'Holder'

// --- file: app/main.maxon
function main() returns ExitCode
	return Holder.create().code()
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:12:13: exported type 'api.Holder' names file-private enum 'Mode' in its implements clause
```

<!-- test: error.an-exported-enum-implementing-a-file-private-interface-is-refused -->
```maxon
// --- file: api/lib.maxon
interface Marker
	function code() returns ExitCode
end 'Marker'

export enum Mode implements Marker
	fast
	slow

	export function code() returns ExitCode
		return 7
	end 'code'
end 'Mode'

// --- file: app/main.maxon
function main() returns ExitCode
	return Mode.fast.code()
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:7:13: exported enum 'api.Mode' names file-private interface 'Marker' in its implements clause
```

<!-- test: error.an-exported-type-constrained-by-a-file-private-interface-is-refused -->
A `where` constraint is a requirement every user of `Box` has to satisfy, so the interface it names has to be
nameable wherever `Box` is.
```maxon
// --- file: api/lib.maxon
interface Marker
	function code() returns ExitCode
end 'Marker'

export type Box uses T where T is Marker
	let item as T

	export static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

// --- file: app/main.maxon
function main() returns ExitCode
	return 7
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:7:13: exported type 'api.Box' names file-private interface 'Marker' in its where clause
```

<!-- test: error.an-exported-interface-extending-a-file-private-interface-is-refused -->
```maxon
// --- file: api/lib.maxon
interface Inner
	function code() returns ExitCode
end 'Inner'

export interface Outer extends Inner
	function more() returns ExitCode
end 'Outer'

// --- file: app/main.maxon
function main() returns ExitCode
	return 7
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:7:18: exported interface 'api.Outer' names file-private interface 'Inner' in its extends clause
```

<!-- test: an-exported-type-implementing-an-exported-interface-is-legal -->
Equal tiers, and the interface is named by the clause and by the reader.
```maxon
// --- file: api/lib.maxon
export interface Marker
	function code() returns ExitCode
end 'Marker'

export type Holder implements Marker
	export function code() returns ExitCode
		return 7
	end 'code'

	export static function create() returns Holder
		return Holder{}
	end 'create'
end 'Holder'

// --- file: app/main.maxon
function report(m Marker) returns ExitCode
	return m.code()
end 'report'

function main() returns ExitCode
	return report(Holder.create())
end 'main'
```
```exitcode
7
```

<!-- test: an-exported-type-implementing-with-an-exported-enum-is-legal -->
```maxon
// --- file: api/lib.maxon
export enum Mode
	fast
	slow
end 'Mode'

export interface Source uses Element
	function next() returns Element
end 'Source'

export type Holder implements Source with Mode
	function next() returns Mode
		return Mode.fast
	end 'next'

	export static function create() returns Holder
		return Holder{}
	end 'create'

	export function code() returns ExitCode
		return match self.next() 'which'
			fast gives 7
			slow gives 1
		end 'which'
	end 'code'
end 'Holder'

// --- file: app/main.maxon
function main() returns ExitCode
	return Holder.create().code()
end 'main'
```
```exitcode
7
```

<!-- test: a-file-private-type-may-implement-a-file-private-interface -->
```maxon
// --- file: api/lib.maxon
interface Marker
	function code() returns ExitCode
end 'Marker'

type Holder implements Marker
	function code() returns ExitCode
		return 7
	end 'code'

	static function create() returns Holder
		return Holder{}
	end 'create'
end 'Holder'

export function code() returns ExitCode
	return Holder.create().code()
end 'code'

// --- file: app/main.maxon
function main() returns ExitCode
	return code()
end 'main'
```
```exitcode
7
```

<!-- test: an-exported-type-constrained-by-an-exported-interface-is-legal -->
```maxon
// --- file: api/lib.maxon
export interface Marker
	function code() returns ExitCode
end 'Marker'

export type Box uses T where T is Marker
	let item as T

	export function run() returns ExitCode
		return self.item.code()
	end 'run'

	export static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

// --- file: app/main.maxon
type Item implements Marker
	function code() returns ExitCode
		return 7
	end 'code'

	static function create() returns Self
		return Self{}
	end 'create'
end 'Item'

typealias ItemBox = Box with Item

function main() returns ExitCode
	return ItemBox.create(Item.create()).run()
end 'main'
```
```exitcode
7
```
<!-- test: an-exported-interface-extending-an-exported-interface-is-legal -->
```maxon
// --- file: api/lib.maxon
export interface Inner
	function code() returns ExitCode
end 'Inner'

export interface Outer extends Inner
	function more() returns ExitCode
end 'Outer'

// --- file: app/main.maxon
function use(o Outer) returns ExitCode
	return o.code() + o.more()
end 'use'

function main() returns ExitCode
	return 7
end 'main'
```
```exitcode
7
```

<!-- test: error.an-exported-interface-requirement-names-a-module-type -->
An interface requirement takes the interface's tier (see above), and the module tier is judged the same way.
```maxon
// --- file: api/lib.maxon
module typealias Count = int(0 to 100)

export interface Sized
	function size() returns Count
end 'Sized'

// --- file: app/main.maxon
function main() returns ExitCode
	return 7
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:6:11: exported function 'api.Sized.size' names module typealias 'Count' in its return type
```

<!-- test: error.an-exported-enum-backed-by-a-file-private-record-is-refused -->
A struct-backed enum hands out its record through `rawValue`, so the record is part of the enum's surface and
is held to the enum's tier. The refusal sits at the enum NAME.
```maxon
// --- file: api/lib.maxon
type OpMeta
	export let latency as ExitCode
end 'OpMeta'

export enum Instruction
	add = OpMeta{latency: 1}
	load = OpMeta{latency: 4}
end 'Instruction'

export function latency(op Instruction) returns ExitCode
	return op.rawValue.latency
end 'latency'

// --- file: app/main.maxon
function main() returns ExitCode
	return latency(Instruction.load)
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:7:13: exported enum 'api.Instruction' names file-private type 'OpMeta' in its backing record
```

<!-- test: an-exported-enum-backed-by-an-exported-record-is-legal -->
Equal tiers, and `OpMeta` is named by the enum's backing alone.
```maxon
// --- file: api/lib.maxon
export type OpMeta
	export let latency as ExitCode
end 'OpMeta'

export enum Instruction
	add = OpMeta{latency: 1}
	load = OpMeta{latency: 4}
end 'Instruction'

export function latency(op Instruction) returns ExitCode
	return op.rawValue.latency
end 'latency'

// --- file: app/main.maxon
function main() returns ExitCode
	return latency(Instruction.load)
end 'main'
```
```exitcode
4
```

<!-- test: error.an-exported-alias-over-a-file-private-services-handle-is-refused -->
A service's handle is the service's own tier, so an alias naming `Worker.handle` names `Worker`.
```maxon
// --- file: api/lib.maxon
type Worker
	static function create() returns Self
		return Self{}
	end 'create'

	export function run() returns ExitCode
		return 7
	end 'run'
end 'Worker'

export typealias WorkerArray = Array with Worker.handle

export function start() returns ExitCode
	let h = spawn Worker.create()
	return try await h.run() otherwise 0
end 'start'

// --- file: app/main.maxon
function main() returns ExitCode
	return start()
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:13:18: exported typealias 'api.WorkerArray' names file-private type 'Worker.handle' in its target
```

<!-- test: an-exported-alias-over-an-exported-services-handle-credits-the-service -->
The control, and the reason the demand is filed against `Worker` and not against its `handle`: nothing outside
`api/lib.maxon` names `Worker`, and the audit would call it unused if the alias's demand did not reach it.
```maxon
// --- file: api/lib.maxon
export type Worker
	static function create() returns Self
		return Self{}
	end 'create'

	export function run() returns ExitCode
		return 7
	end 'run'
end 'Worker'

export typealias WorkerArray = Array with Worker.handle

export function start() returns ExitCode
	let h = spawn Worker.create()
	return try await h.run() otherwise 0
end 'start'

// --- file: app/main.maxon
function main() returns ExitCode
	var workers = WorkerArray.create()
	return start() + (workers.count() as ExitCode)
end 'main'
```
```exitcode
7
```

<!-- test: a-type-reached-only-through-a-chain-of-aliases-is-credited -->
⭐ **A DEMAND IS CREDITED THROUGH A DEMANDER THAT IS ITSELF ONLY CREDITED.** The reader names `ItemRows` and
nothing else. `ItemRows` demands `ItemArray` at `export`, and `ItemArray` demands `Item` at `export`; neither
is named outside `api/lib.maxon`, and neither is unused.
```maxon
// --- file: api/lib.maxon
export type Item
	export let n as ExitCode

	export static function create(n ExitCode) returns Self
		return Self{n: n}
	end 'create'
end 'Item'

export typealias ItemArray = Array with Item
export typealias ItemRows = Array with ItemArray

export function seed() returns ExitCode
	return Item.create(7).n
end 'seed'

// --- file: app/main.maxon
function main() returns ExitCode
	var rows = ItemRows.create()
	return seed() + (rows.count() as ExitCode)
end 'main'
```
```exitcode
7
```

<!-- test: a-type-reached-only-through-a-chain-of-fields-is-credited -->
The reader names `Root` and nothing else. `Root.middle` demands `Middle` at `export`, and `Middle.leaf` demands
`Leaf` at `export`. `Leaf` and `Middle` are declared before the types whose fields name them.
```maxon
// --- file: api/lib.maxon
export type Leaf
	export let n as ExitCode

	static function create(n ExitCode) returns Self
		return Self{n: n}
	end 'create'
end 'Leaf'

export type Middle
	export let leaf as Leaf

	static function create(leaf Leaf) returns Self
		return Self{leaf: leaf}
	end 'create'
end 'Middle'

export type Root
	export let middle as Middle

	export static function make() returns Root
		return Root{middle: Middle.create(Leaf.create(7))}
	end 'make'

	export function total() returns ExitCode
		return self.middle.leaf.n
	end 'total'
end 'Root'

// --- file: app/main.maxon
function main() returns ExitCode
	return Root.make().total()
end 'main'
```
```exitcode
7
```

<!-- test: error.an-exported-generic-function-constrained-by-a-file-private-interface-is-refused -->
A generic function's `where` constraint is a requirement every caller has to meet, so the interface it names is
held to the function's tier. The refusal sits at the function name.
```maxon
// --- file: api/lib.maxon
interface Marker
	function code() returns ExitCode
end 'Marker'

export function bigger(a T, b T) uses T returns T where T is Marker
	return a if a.code() > b.code() else b
end 'bigger'

// --- file: app/main.maxon
function main() returns ExitCode
	return 7
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:7:17: exported function 'api.bigger' names file-private interface 'Marker' in its where clause
```

<!-- test: an-exported-generic-function-constrained-by-an-exported-interface-is-legal -->
```maxon
// --- file: api/lib.maxon
export interface Marker
	function code() returns ExitCode
end 'Marker'

export function bigger(a T, b T) uses T returns T where T is Marker
	return a if a.code() > b.code() else b
end 'bigger'

// --- file: app/main.maxon
type Item implements Marker
	let n as ExitCode

	function code() returns ExitCode
		return self.n
	end 'code'

	static function create(n ExitCode) returns Self
		return Self{n: n}
	end 'create'
end 'Item'

function main() returns ExitCode
	return bigger(Item.create(3), b: Item.create(7)).code()
end 'main'
```
```exitcode
7
```

<!-- test: error.an-exported-extension-method-constrained-by-a-file-private-interface-is-refused -->
The conditional extension's `where` clause is part of every method's contract: only a conformer of `Marker` has
the method, so whoever can call it has to be able to name `Marker`.
```maxon
// --- file: api/lib.maxon
interface Marker
	function code() returns ExitCode
end 'Marker'

extension Array where Element is Marker
	export function total() returns ExitCode
		var sum = 0 as ExitCode

		for item in self 'each'
			sum = sum + item.code()
		end 'each'

		return sum
	end 'total'
end 'Array'

// --- file: app/main.maxon
function main() returns ExitCode
	return 7
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:8:18: exported function 'api.Array.total' names file-private interface 'Marker' in its where clause
```
