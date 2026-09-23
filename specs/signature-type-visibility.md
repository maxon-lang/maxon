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

### The library is held to it too

A typealias written in `stdlib/` with no modifier is file-private to the library file it lives in, the
same as anyone's, so user code that names one is refused where it writes the name. Every alias a
`public` library signature names is therefore written `public` itself.

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
name it. The `otherwise` here does not, which is why the program runs today.
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
	_ = wrap(7)
	return 7
end 'main'
```
```maxoncstderr
error E3167: api/<fragment>:13:17: exported function 'api.wrap' names file-private type 'Box' in its return type
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

<!-- test: error.a-stdlib-internal-alias-is-not-nameable-from-user-code -->
⭐⭐ **THE LIBRARY IS HELD TO THE SAME RULE, AND THIS IS WHAT THAT COSTS USER CODE.**
`stdlib/Math.maxon:8` writes `typealias SeriesTermLimit = int(2 to 64)` with no modifier; its one
reader is `Math.lnBySeries`, a file-private static, and no other library file names it. It is an
implementation detail of one function's term count, and nothing about `Math`'s public surface says it
exists — yet the corpus promotion made it a global name every program could write. Once the library's
unmarked aliases stay file-private, the name resolves to nothing at the `as` and the compiler gives
the answer it already gives for any other file's non-exported typealias.
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
error E2003: api/<fragment>:4:15: Expected type name after 'as'
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

Now the guards. Each is legal today and stays legal: the rule is a floor under the function's own
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
	var n = 0

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
ByteArray` of its own, over a two-byte element, and the library declares one in six files. `stdlib/Sha256.maxon`
declares none of the name and its `public function sha256(data ByteArray) returns ByteArray` names it, so
the library's own declaration has to answer there; the author's alias answers only where the author wrote
it.
```maxon
// --- file: api/lib.maxon
export typealias Tally = int(0 to 1000)
export typealias ByteArray = Array with Tally

export function widest() returns Tally
	var xs = ByteArray.create()
	xs.push(900)
	var top = 0

	for x in xs 'each'
		top = x if x > top else top
	end 'each'

	return top
end 'widest'

// --- file: app/main.maxon
function main() returns ExitCode
	let digest = sha256("hi".toByteArray())

	return ((widest() - 900) + digest.count()) as ExitCode
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
⚠ **THE TIER IS THE READER'S DECLARATION'S, NOT THE FIRST ONE FILED UNDER THE NAME.** Two files
declare `TallyBag`; `alpha/lib.maxon` exports its own and `pkg/lib.maxon` keeps its own file-private.
`pkg.apply` is `export` and its `Handler` is spelled over `pkg/lib.maxon`'s `TallyBag`, so that is the
declaration it is held against — a door that answered with whichever declaration was filed first would
read `alpha`'s `export` tier and refuse nothing.
```maxon
// --- file: alpha/lib.maxon
export typealias Tally = int(0 to 100)
export typealias TallyBag = Array with Tally

export function count(b TallyBag) returns Tally
	return b.count() as Tally
end 'count'

// --- file: pkg/lib.maxon
typealias TallyBag = Array with Tally

export typealias Handler = function(TallyBag) returns Tally

export function apply(h Handler, b TallyBag) returns Tally
	return h(b)
end 'apply'

// --- file: app/main.maxon
function main() returns ExitCode
	var b = TallyBag.create()
	b.push(7)

	return (count(b) + apply(function(x TallyBag) gives x.count() as Tally, b: b)) as ExitCode
end 'main'
```
```maxoncstderr
error E3167: pkg/<fragment>:15:17: exported function 'pkg.apply' names file-private typealias 'TallyBag' in the type of parameter 'h'
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

⚠ `Worker` is named nowhere outside `api/lib.maxon`, which is E3092 — and `checkSignatureVisibility` runs
ahead of `checkUnusedExports` (`PassPipeline.buildMaxonTierPasses`) and the pipeline stops at the first
pass that reports, so the stderr below is the whole of it.
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
