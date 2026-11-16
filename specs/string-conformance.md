---
feature: string-conformance
keywords: [hashable, equatable, string, witness, where, generics, Hasher]
category: type-system
---

# String Interface Conformance

## Documentation

The managed concrete type `String` conforms to the `Hashable` and `Equatable` protocol interfaces WITHOUT a
user `implements` clause — the compiler synthesizes its `String.hash` / `String.equals` implementations
natively, exactly as it does for the primitive `int` (the type-side twin of the synthesized
`Hashable`/`Equatable` interfaces). So `Box with String where T is Hashable and Equatable` is legal and its
witness slots resolve to real `.text` symbols. This is the prerequisite for String-keyed hash collections.

- `String.hash()` is `Hasher`'s hash over the String's bytes: the `finalize()` of a `Hasher.create()` that
  folds `s.toByteArray()` with `combineBytes`, a `HashValue` (`int(0 to u32.max)`). It starts from the process seed, so equal strings hash equal within a process
  and a hash value differs from one process to the next; the cases below pin relations, never a value.
- `String.equals(other)` is content equality: equal lengths and equal bytes — the same byte-compare
  `__str_eq` computes for the `==` operator, emitted here under the witness-visible `String.equals` symbol.

Inside a generic body the concrete type is unknown, so a call on a constrained parameter —
`self.item.hash()`, `self.item.equals(other)`, or the `==`/`!=` OPERATOR on a `T`-typed value — dispatches
through the runtime WITNESS TABLE (dictionary-passing), whose slots for a `String` argument point at the
synthesized `String.hash`/`String.equals`. The witness receiver (`self.item`) and the `other` argument are
BORROWED: the container owns the `String` and drops it exactly once at scope exit, so hashing or comparing a
boxed `String` neither double-frees nor leaks it.

The witness dispatch rides the rdata function-pointer relocation, which EVERY target fills — the
fixed-base writers bake a `.text` VA, wasm a funcref-table index, and arm64-macOS a dyld chained-fixup
rebase — so these cases run everywhere and carry no target marker (as the `primitive-conformance` and
`where-clauses` witness cases do).

Direct `s.hash()` / `s.equals(t)` on a concrete `String` value ship too, and reach the SAME two symbols: the
call names `String.hash`, the witness slot holds a relocation to `String.hash`. There is one body per method
and no thunk between the two spellings, which is what the `direct-hash-agrees-with-the-witness` case below
asserts. `Set with String` keys probe through those same two witness slots, covered by
`set-string`; direct dispatch on a `Character` VALUE is not covered here
(a `Character` receiver is routed to its own method table, which has no conformance fall-through).

⚠ **THE `String.hash` / `String.equals` SYMBOLS ARE THE COMPILER'S, AND THAT IS ENFORCED RATHER THAN
ASSUMED.** A user declaration binding the name `String` — or `Character`, whose two impls are built from the
same builders — is refused (`TypeResolution.isCompilerOwnedTypeName`), because such a declaration's own
`hash()` mints the identical symbol and the installer declines to build an impl for a name the module
already defines. The two error cases at the end of this file pin both refusals and name the wrong answers
each one prevents.

## Tests

<!-- test: string-conformance.hash-values -->
A `String` argument's `Hashable` witness dispatches `element.hash()` to the synthesized `String.hash`. Three
boxed Strings are constructed, hashed, and dropped at scope exit — the empty string, `"abc"` and `"hi"` —
and each answers what `hash()` answers on the same text called directly.
```maxon
type Box uses T where T is Hashable
	export var item as T
	static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	function itemHash() returns HashValue
		return self.item.hash()
	end 'itemHash'
end 'Box'

typealias StringBox = Box with String

function main() returns ExitCode
	let e = StringBox.create("")
	if e.itemHash() != "".hash() 'p1'
		return 1
	end 'p1'
	let a = StringBox.create("abc")
	if a.itemHash() != "abc".hash() 'p2'
		return 2
	end 'p2'
	let h = StringBox.create("hi")
	if h.itemHash() != "hi".hash() 'p3'
		return 3
	end 'p3'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: string-conformance.equals-via-constraint -->
A `String` argument's `Equatable` witness dispatches `element.equals(other)` to the synthesized
`String.equals` — content equality. The boxed `String` and the two `other` arguments are borrowed, so `b`
stays live and droppable across both comparisons.
```maxon
type Box uses T where T is Equatable
	export var item as T
	static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	function sameAs(other T) returns bool
		return self.item.equals(other)
	end 'sameAs'
end 'Box'

typealias StringBox = Box with String

function main() returns ExitCode
	let b = StringBox.create("abc")
	if not b.sameAs("abc") 'eq'
		return 1
	end 'eq'
	if b.sameAs("abd") 'ne'
		return 2
	end 'ne'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: string-conformance.hashable-and-equatable -->
The hash-collection key shape — a parameter constrained with `where T is Hashable and Equatable` dispatches
BOTH witnesses on a `String` argument, each to its synthesized impl.
```maxon
type Key uses T where T is Hashable and Equatable
	export var value as T
	static function create(value T) returns Self
		return Self{ value: value }
	end 'create'
	function digest() returns HashValue
		return self.value.hash()
	end 'digest'
	function matches(other T) returns bool
		return self.value.equals(other)
	end 'matches'
end 'Key'

typealias StringKey = Key with String

function main() returns ExitCode
	let k = StringKey.create("hi")
	if k.digest() != "hi".hash() 'h'
		return 1
	end 'h'
	if not k.matches("hi") 'e1'
		return 2
	end 'e1'
	if k.matches("no") 'e2'
		return 3
	end 'e2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: string-conformance.eq-with-equatable -->
The `==` OPERATOR on an Equatable-constrained type parameter lowers to the Equatable witness dispatch — for a
`String` argument that is the synthesized `String.equals`, so `b.eq("abc")` is true and the `if` returns 1.
```maxon
type Box uses T where T is Equatable
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function eq(other T) returns bool
		return item == other
	end 'eq'
end 'Box'

typealias StringBox = Box with String

function main() returns ExitCode
	let b = StringBox.create("abc")
	if b.eq("abc") 'yes'
		return 1
	end 'yes'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: string-conformance.hash-in-loop-no-leak -->
A boxed `String` constructed, hashed through the `Hashable` witness, and dropped every iteration of a
100-iteration loop — the standing leak/double-free probe. If the witness receiver were consumed rather than
borrowed the per-iteration drop would double-free the freed payload; if the box failed to drop it the run
would leak. Neither happens: `acc` reaches 100 and the leak gate stays green.
```maxon
type Box uses T where T is Hashable
	export var item as T
	static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	function itemHash() returns HashValue
		return self.item.hash()
	end 'itemHash'
end 'Box'

typealias StringBox = Box with String
typealias Counter = int(0 to 1000)

function main() returns ExitCode
	let expected = "abc".hash()
	var i = 0 as Counter
	var acc = 0 as Counter
	while i < 100 'loop'
		let b = StringBox.create("abc")
		if b.itemHash() == expected 'ok'
			acc = acc + 1
		end 'ok'
		i = i + 1
	end 'loop'
	if acc == 100 'all'
		return 0
	end 'all'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: string-conformance.direct-hash-on-a-concrete-string -->
### A concrete `String` value hashes DIRECTLY, with no witness table in the program
`s.hash()` on a String-typed value is an ordinary `call String.hash` — the very symbol a
`__witness_String.Hashable` slot would have been stamped with — reached without a witness table. The hash
reads the bytes and nothing else, so a literal and a heap String built at run time with the same bytes hash
equal: `""`, `"abc"` and `"hi"` against their interpolated twins.
```maxon
function main() returns ExitCode
	let middle = "b"
	let e = ""
	if e.hash() != "{""}".hash() 'p1'
		return 1
	end 'p1'
	let a = "abc"
	if a.hash() != "a{middle}c".hash() 'p2'
		return 2
	end 'p2'
	let h = "hi"
	if h.hash() != "h{"i"}".hash() 'p3'
		return 3
	end 'p3'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: string-conformance.direct-hash-agrees-with-the-witness -->
### The direct call and the witness dispatch reach ONE body
Both spellings in one program, over the same bytes. They must agree not because two implementations were
written to match, but because there is only one: the direct call names `String.hash` and the witness slot
holds a relocation to `String.hash`.
```maxon
type Box uses T where T is Hashable
	export var item as T
	static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	function itemHash() returns HashValue
		return self.item.hash()
	end 'itemHash'
end 'Box'

typealias StringBox = Box with String

function main() returns ExitCode
	let s = "abc"
	let b = StringBox.create("abc")
	if s.hash() != b.itemHash() 'agree'
		return 1
	end 'agree'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: string-conformance.direct-equals-on-a-concrete-string -->
### A concrete `String` value compares DIRECTLY through `String.equals`
Content equality, both verdicts, plus the length-mismatch shortcut the impl opens with.
```maxon
function main() returns ExitCode
	let a = "ab"
	if not a.equals("ab") 'same'
		return 1
	end 'same'
	if a.equals("ac") 'differentByte'
		return 2
	end 'differentByte'
	if a.equals("abc") 'differentLength'
		return 3
	end 'differentLength'
	if not "".equals("") 'bothEmpty'
		return 4
	end 'bothEmpty'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: string-conformance.direct-call-borrows-both-operands -->
### Both operands are BORROWED, so an OWNED temporary still drops
The receiver and the `other` argument are read and never consumed — `String.hash` walks the bytes and
`String.equals` compares them, freeing nothing — so an interpolated argument stays an ordinary pending temp
and drops at statement end. Run 100 times so a leaked or double-freed temp cannot hide; the leak gate is the
assertion.
```maxon
typealias Counter = int(0 to 1000)

function main() returns ExitCode
	var i = 0 as Counter
	var acc = 0 as Counter
	while i < 100 'loop'
		if "a1b".equals("a{1}b") 'eq'
			acc = acc + 1
		end 'eq'
		if "a{1}b".hash() != "a1b".hash() 'neverTrue'
			return 1
		end 'neverTrue'
		i = i + 1
	end 'loop'
	if acc == 100 'all'
		return 0
	end 'all'
	return 2
end 'main'
```
```exitcode
0
```

<!-- test: string-conformance.hash-is-the-hasher-fold-of-its-bytes -->
### `String.hash` is `Hasher`'s hash of the bytes
`s.hash()` is the `finalize()` of a `Hasher.create()` that folds `s.toByteArray()` with `combineBytes`: one hash
in the library, reached from `Hasher` and from `Hashable`. Every length on either side of the hash's block
boundaries, the empty string included, and a String of multi-byte characters.
```maxon
typealias LengthList = Array with Count

function textOf(length Count) returns String
	let bytes = ByteArray.create(0)

	for index in 0 upto length 'eachByte'
		let mixed = ((index + length * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push((0x40 + ((mixed shr 11) and 0x3F)) as Byte)
	end 'eachByte'

	return String.from(bytes)
end 'textOf'

function folded(bytes ByteArray) returns HashValue
	let hasher = Hasher.create()
	hasher.combineBytes(bytes)
	return hasher.finalize()
end 'folded'

function main() returns ExitCode
	let lengths = LengthList from [0, 1, 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, 128, 129, 300]

	for length in lengths 'eachLength'
		let text = textOf(length)
		print("{length} {text.hash() == folded(text.toByteArray())}\n")
	end 'eachLength'

	let wide = "héllo, wörld — ✓ 🎉"
	print("utf-8 {wide.hash() == folded(wide.toByteArray())}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 true
1 true
15 true
16 true
17 true
31 true
32 true
33 true
63 true
64 true
65 true
127 true
128 true
129 true
300 true
utf-8 true
```

<!-- test: string-conformance.hash-avalanches -->
### One flipped bit moves about half of the 32 hash bits
For every length on either side of the hash's block boundaries, six bits of each byte are flipped in turn
(the text stays ASCII): the hashes differ by at least 8 of 32 bits on average. A multiply-and-add walk moves
one bit for a flip in the last byte and fails here. Over the ~6,000 flips two 32-bit hashes collide by chance
about once in 10^6 runs, so one unmoved hash is allowed and two are a byte the hash did not read.
```maxon
typealias LengthList = Array with Count

function bytesOf(length Count) returns ByteArray
	let bytes = ByteArray.create(0)

	for index in 0 upto length 'eachByte'
		let mixed = ((index + length * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push((0x40 + ((mixed shr 11) and 0x3F)) as Byte)
	end 'eachByte'

	return bytes
end 'bytesOf'

function bitsSet(pattern HashValue) returns Count
	var rest = pattern
	var count = 0 as Count

	while rest != 0 'eachBit'
		count = count + ((rest and 1) as Count)
		rest = rest shr 1
	end 'eachBit'

	return count
end 'bitsSet'

function main() returns ExitCode
	let lengths = LengthList from [1, 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, 128, 129, 300]
	let masks = ByteArray from [1, 2, 4, 8, 16, 32]

	for length in lengths 'eachLength'
		let bytes = bytesOf(length)
		let hash = String.from(bytes).hash()
		var moved = 0 as Count
		var flips = 0 as Count
		var unmoved = 0 as Count

		for index in 0 upto length 'eachByte'
			let original = try bytes.get(index) otherwise panic("the index is inside the text")

			for mask in masks 'eachMask'
				let flipped = bytes.clone()
				try flipped.set(index, value: original xor mask) otherwise panic("the index is inside the text")
				let distance = bitsSet(hash xor String.from(flipped).hash())
				moved = moved + distance
				flips = flips + 1

				if distance == 0 'unchanged'
					unmoved = unmoved + 1
				end 'unchanged'
			end 'eachMask'
		end 'eachByte'

		print("{length} mean>=8: {moved >= flips * 8} at most one 0: {unmoved <= 1}\n")
	end 'eachLength'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 mean>=8: true at most one 0: true
15 mean>=8: true at most one 0: true
16 mean>=8: true at most one 0: true
17 mean>=8: true at most one 0: true
31 mean>=8: true at most one 0: true
32 mean>=8: true at most one 0: true
33 mean>=8: true at most one 0: true
63 mean>=8: true at most one 0: true
64 mean>=8: true at most one 0: true
65 mean>=8: true at most one 0: true
127 mean>=8: true at most one 0: true
128 mean>=8: true at most one 0: true
129 mean>=8: true at most one 0: true
300 mean>=8: true at most one 0: true
```

<!-- test: string-conformance.hash-differs-between-processes -->
<!-- unsupported-targets: wasm32-wasi -->
### A String's hash differs from one process to the next
`String.hash` starts from the process seed, so a String-keyed `Map` or `Set` is laid out differently in every
run and no program can come to depend on one hash value. The program re-executes itself three times and each
child prints `"abc".hash()`; three 32-bit hashes that all agree are a chance of about 2^-64. Each child's
`MAXON_HASH_SEED` is set empty, which draws a seed: a harness that pins the case's own seed in the environment
would otherwise hand every child the one seed. Not on `wasm32-wasi`, where the subprocess band is refused at
compile time (`subprocess-unsupported.md`).
```maxon
function child() returns ExitCode
	print("{"abc".hash()}\n")
	return 0
end 'child'

function spawnChild(me FilePath) returns CollectedOutput
	let argv = StringArray.create(0)
	argv.push("child")

	let config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_HASH_SEED": ""])
	return try Subprocess.runConfiguration(config) otherwise panic("the child could not be spawned")
end 'spawnChild'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return child()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	let first = spawnChild(me)
	let second = spawnChild(me)
	let third = spawnChild(me)

	print("children exit={first.exitCode()} {second.exitCode()} {third.exitCode()}\n")
	print("children printed a hash: {first.stdout.count() > 1 and second.stdout.count() > 1 and third.stdout.count() > 1}\n")
	print("not all equal: {not (first.stdout == second.stdout and second.stdout == third.stdout)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
children exit=0 0 0
children printed a hash: true
not all equal: true
```

<!-- test: error.direct-equals-rejects-a-non-string-argument -->
### `equals` declares `other Self`, so its argument must be a `String`
`String.equals` walks bytes off a pointer, so an `int` actual would be DEREFERENCED — the mirror of the
wrong answer `int.equals` gives a String actual, and refused by the same `Self`-formal check.
```maxon
function main() returns ExitCode
	let a = "ab"
	if a.equals(3) 'notAString'
		return 1
	end 'notAString'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:4:7: 'equals' requires an argument of type String, but its argument is int
```

<!-- test: string-conformance.the-conformance-method-may-be-named-statically -->
### `String.hash(s)` is the corpus's own method named statically, and it agrees with `s.hash()`
`String.hash` is TWO things wearing one spelling: the symbol `BuiltinConformanceRuntime` mints so a
witness-table reloc has a real `.text` target (which is why it carries no `__` prefix at all), and the
ordinary static spelling of the `hash()` `stdlib/String.maxon` declares at `:351`. Naming an instance
method statically is legal — `Adder.bump(a)` IS `a.bump()`, which `Parser.parseQualifiedCall`'s header
settles — so this program is one of those.

⛔ **A REFUSAL HERE WOULD BE A WRONG ANSWER.** `MmRuntime.isCompilerInternalCallee` declares every
`<conformer>.<method>` to be the compiler's own, so `requireCalleeIsNotReservedName` conjoins that clause
with `declaresCallee`, exactly as the four exemptions beside it are. Acting on it unconditionally would
answer *"E3004 call to undefined function 'String.hash': the `__` prefix names a compiler intrinsic, and no
intrinsic of that name exists"* — the wrong sentence AND the wrong verdict, about a method the corpus
declares, in a program that prints `true`.
```maxon
function main() returns ExitCode
	let s = "a"
	print("{String.hash(s) == s.hash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
```

<!-- test: error.a-declaration-may-not-bind-the-name-String -->
### A declaration may not bind `String` to a nominal identity
⚠ **This refusal is what makes the `String.hash` / `String.equals` SYMBOLS the compiler's.** A user
`type String` is otherwise inert — `parseTypeReference` settles the name syntactically, so it can never be
named at a parameter, `String{…}` is E3076 and `String.create(…)` is refused as an unknown builtin static —
but its `hash()` method registers the symbol `String.hash`, and the conformance installer builds an impl only
for a name the module does not already define. Unrefused, with no diagnostic anywhere,
`Box with String`'s `itemHash()` of `""` returns **7** (the user's body) instead of `String.hash`'s answer, and
`Set with String` counts **3** for `insert("alice"); insert("bob"); insert("alice")` against a control's
**2** — a duplicate key stored twice, because the user's `equals` answers `false`.
```maxon
type String
	export var value as ExitCode

	function hash() returns HashValue
		return 7
	end 'hash'
end 'String'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:6: Unsupported: a declaration of the type name 'String', which the compiler owns — its one meaning comes from the compiler itself or from the stdlib module that declares it, and the compiler has no namespace to tell a user declaration of the name apart from that one
```

<!-- test: error.a-declaration-may-not-bind-the-name-Character -->
### `Character` is reserved on the same footing, and for the same symbols
`Character.hash` and `Character.equals` are synthesized from the very builders `String`'s are
(`BuiltinConformanceRuntime` — a Character IS the fused byte record), under Character's own symbols. So a
user declaration of the name captures those two exactly as a `type String` captures String's, and the name
is reserved beside it rather than after the next measurement.
```maxon
enum Character
	first
	second
end 'Character'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:6: Unsupported: a declaration of the type name 'Character', which the compiler owns — its one meaning comes from the compiler itself or from the stdlib module that declares it, and the compiler has no namespace to tell a user declaration of the name apart from that one
```

<!-- test: error.a-declaration-may-not-bind-the-name-BuiltinStringLiteral -->
### The literal marker interfaces are reserved too
`String`'s record is the fused byte record because `String` implements `BuiltinStringLiteral`, and the
compiler recognises that marker by its name. A user interface of the same name would be read as an author
shadow of the library's, and the library's own `implements BuiltinStringLiteral` renamed away from it, so
`String` would lose its fused record. It is refused like a declaration of `String` itself.
```maxon
interface BuiltinStringLiteral
end 'BuiltinStringLiteral'

function main() returns ExitCode
	print("hi\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:11: Unsupported: a declaration of the type name 'BuiltinStringLiteral', which the compiler owns — its one meaning comes from the compiler itself or from the stdlib module that declares it, and the compiler has no namespace to tell a user declaration of the name apart from that one
```

<!-- test: error.a-declaration-may-not-bind-the-name-BuiltinCharLiteral -->
### `BuiltinCharLiteral` is reserved for `Character`'s record
```maxon
interface BuiltinCharLiteral
end 'BuiltinCharLiteral'

function main() returns ExitCode
	let c = 'x'
	print("{c}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:11: Unsupported: a declaration of the type name 'BuiltinCharLiteral', which the compiler owns — its one meaning comes from the compiler itself or from the stdlib module that declares it, and the compiler has no namespace to tell a user declaration of the name apart from that one
```

<!-- test: error.a-declaration-may-not-bind-the-name-BuiltinArrayLiteral -->
### `BuiltinArrayLiteral` is reserved for the array literal's record
```maxon
interface BuiltinArrayLiteral
end 'BuiltinArrayLiteral'

function main() returns ExitCode
	let values = [1, 2, 3]
	print("{values.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:11: Unsupported: a declaration of the type name 'BuiltinArrayLiteral', which the compiler owns — its one meaning comes from the compiler itself or from the stdlib module that declares it, and the compiler has no namespace to tell a user declaration of the name apart from that one
```
