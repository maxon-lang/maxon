---
feature: hasher
status: stable
keywords: [Hasher, HashDigest, FNV-1a, hash, digest, combine, combined, empty, finalize, resume, length tag, content hash]
category: crypto
---

# `Hasher` — FNV-1a 64-bit, folded incrementally, with no length tag

## Documentation

`stdlib/Hasher.maxon` is the tree's ONE implementation of FNV-1a 64-bit. It is shaped as an
incremental fold:

```
var hasher = Hasher.create()
hasher.combine(bytes)
let digest = hasher.finalize()
```

`create()` starts at the FNV-1a offset basis `0xCBF29CE484222325`; `combine` xors a value in and
multiplies by the FNV prime `1099511628211`, wrapping; `finalize()` hands the running state back.
`resume(state)` starts from a state a previous `finalize()` returned, so a caller that threads the
running value through its own structures — the shv2 query spine does exactly this — needs no second
implementation of the fold.

The result type is `HashDigest`, `int(0 to u64.max)`. It is **not** `HashValue`: that alias is 32
bits wide (`stdlib/Interfaces.maxon`), which is the surface `Hashable` answers on, and the first
wrapping multiply of a 64-bit fold would trip its range check.

### The same fold has a second, PURE face — because a mutable hasher has no expression position

```
let digest = Hasher.combined(Hasher.combined(Hasher.empty(), bytes: name), value: name.count())
```

`Hasher.empty()` is the state of a fold with nothing in it, and `Hasher.combined(state, ...)` takes a
state and returns the next one. Nothing else is different: `combine` is `state = combined(state, …)`,
and `create()` is `resume(empty())`, so the two faces are ONE fold with one copy of the arithmetic —
which is the property the agreement case below pins.

⭐ **A pure fold is what a nested call or a `match` arm needs.** A caller that must write
`f(g(state, …), …)`, or `methodOf(name) gives mixBytes(state, bytes: name)`, cannot hold a `var`
hasher there; without this face it would grow its own copy of the step, and a second copy of an FNV
step is exactly how the digests in this file drifted apart before.

### ⛔ THE FOLD CARRIES NO LENGTH TAG, AND THAT IS THE WHOLE REASON THIS IS A STDLIB FILE

FNV-1a is defined over a flat byte sequence. Combining `"ab"` then `"c"` therefore reaches exactly
the same digest as combining `"a"` then `"bc"`, and as combining `"abc"` — all three fold the same
bytes in the same order.

A caller hashing a **sequence of parts** — where the boundaries between parts carry meaning — must
combine each part's length itself. `maxon-shv2/Compiler/ContentHash.maxon` is that call site, and
after this fold moved to stdlib it is the ONE function left in it:

```
export function mixBytes(state HashDigest, bytes ByteArray) returns HashDigest
	return Hasher.combined(Hasher.combined(state, bytes: bytes), value: bytes.count())
end 'mixBytes'
```

⭐ **That tag lives at the call site and never in stdlib.** A length tag baked into a function named
for FNV-1a is a policy wearing an algorithm's name, and it is precisely how two copies of this hash
drifted apart before: one folded a tag, one did not, and nothing named the difference. Anything that
must interoperate — the C# bootstrap's `ErrorCodeRegistry.HashBytes`, the `sourceHash` stamped into
`docs/error-codes.json` — needs the bare fold, and gets it by default.

### The digests below are the published FNV-1a-64 vectors

`""` is the basis itself, `"abc"` is `e71fa2190541574b`, `"hello"` is `a430d84680aabd0b`. They are
checkable against any other FNV-1a implementation, which is what makes these cases a pin on the
ALGORITHM rather than a record of what this tree happened to compute.

## Tests

<!-- test: folds-the-published-fnv1a-vectors -->
The empty fold is the offset basis, and two published vectors follow from it. A wrong basis is
perfectly stable — every one of these still agrees with itself — so a case that only checked
determinism would pass over the exact defect this pins.
```maxon
function digestOf(text String) returns HashDigest
	var hasher = Hasher.create()
	hasher.combine(text.toByteArray())
	return hasher.finalize()
end 'digestOf'

function main() returns ExitCode
	print("basis={Hasher.create().finalize():016x}\n")
	print("empty={digestOf(""):016x}\n")
	print("abc={digestOf("abc"):016x}\n")
	print("hello={digestOf("hello"):016x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
basis=cbf29ce484222325
empty=cbf29ce484222325
abc=e71fa2190541574b
hello=a430d84680aabd0b
```

<!-- test: combining-a-value-folds-it-as-one-step -->
`combine(value)` is the same single FNV step the byte fold takes, so combining the number 42 reaches
the digest of the one-byte sequence `0x2A`. That is what lets a caller mix a tag, a count or another
digest into the same running state as the bytes.
```maxon
function main() returns ExitCode
	var byValue = Hasher.create()
	byValue.combine(42)

	var byByte = Hasher.create()
	byByte.combine(b"\x2A")

	print("value={byValue.finalize():016x}\n")
	print("byte={byByte.finalize():016x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
value=af63a74c8601927d
byte=af63a74c8601927d
```

<!-- test: a-bare-fold-cannot-separate-the-parts-of-a-sequence -->
⭐ **THE PROPERTY STDLIB DELIBERATELY DOES NOT HAVE.** Three different groupings of the same three
bytes reach one digest. A future edit that "helpfully" folded a length inside `combine` would break
this case, which is the point of writing it down.
```maxon
typealias PartArray = Array with String

function foldParts(parts PartArray) returns HashDigest
	var hasher = Hasher.create()
	for part in parts 'each'
		hasher.combine(part.toByteArray())
	end 'each'
	return hasher.finalize()
end 'foldParts'

function main() returns ExitCode
	print("whole={foldParts(["abc"]):016x}\n")
	print("ab-c={foldParts(["ab", "c"]):016x}\n")
	print("a-bc={foldParts(["a", "bc"]):016x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
whole=e71fa2190541574b
ab-c=e71fa2190541574b
a-bc=e71fa2190541574b
```

<!-- test: a-caller-added-length-tag-separates-them -->
The same three groupings under `ContentHash.mixBytes`'s policy — fold the bytes, then fold the count
— now disagree, and the last three lines are the shv2 query spine's own per-file cache keys for
`""`, `"a"` and `"hello"`. These are the values the compiler memoizes on: a silent change here is a
cache MISS rather than a wrong answer, which is exactly why it needs a case and would otherwise read
green.
```maxon
typealias PartArray = Array with String

function taggedFold(state HashDigest, text String) returns HashDigest
	let bytes = text.toByteArray()
	var hasher = Hasher.resume(state)
	hasher.combine(bytes)
	hasher.combine(bytes.count())
	return hasher.finalize()
end 'taggedFold'

function foldParts(parts PartArray) returns HashDigest
	var state = Hasher.create().finalize()
	for part in parts 'each'
		state = taggedFold(state, text: part)
	end 'each'
	return state
end 'foldParts'

function main() returns ExitCode
	print("whole={foldParts(["abc"]):016x}\n")
	print("ab-c={foldParts(["ab", "c"]):016x}\n")
	print("a-bc={foldParts(["a", "bc"]):016x}\n")
	print("contentHash-empty={foldParts([""]):016x}\n")
	print("contentHash-a={foldParts(["a"]):016x}\n")
	print("contentHash-hello={foldParts(["hello"]):016x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
whole=fc17b883ee074f58
ab-c=9b03a82f48f10a60
a-bc=3ba8b80874c9ff72
contentHash-empty=af63bd4c8601b7df
contentHash-a=089be307b544f397
contentHash-hello=a9bc8dcca21f3eca
```

<!-- test: resume-continues-a-finalized-fold -->
`finalize()` yields the running state and `resume()` takes it back, so carrying the value across a
boundary is not a different hash. Without that pair — or its pure twin `combined` — a caller that
threads the running value through its own structures would need a second copy of the fold, which is
the duplication this file exists to end.
```maxon
function main() returns ExitCode
	var first = Hasher.create()
	first.combine("hel".toByteArray())
	let carried = first.finalize()

	var second = Hasher.resume(carried)
	second.combine("lo".toByteArray())

	print("carried={carried:016x}\n")
	print("resumed={second.finalize():016x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
carried=334a30192fe3892e
resumed=a430d84680aabd0b
```

<!-- test: the-two-faces-are-one-fold -->
⭐ **THE AGREEMENT CASE.** `Hasher.empty()`/`Hasher.combined` and `create()`/`combine` are the same
arithmetic reached two ways, so every line below must print twice-identical digests. Two independent
copies of an FNV step agree with themselves forever and only disagree with each other, which is why
the pin has to be the CROSS-face comparison and not a determinism check.
```maxon
function main() returns ExitCode
	var byObject = Hasher.create()
	byObject.combine("hello".toByteArray())
	byObject.combine(42)

	let byValue = Hasher.combined(Hasher.combined(Hasher.empty(), bytes: "hello".toByteArray()), value: 42)

	print("basis-object={Hasher.create().finalize():016x}\n")
	print("basis-pure={Hasher.empty():016x}\n")
	print("fold-object={byObject.finalize():016x}\n")
	print("fold-pure={byValue:016x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
basis-object=cbf29ce484222325
basis-pure=cbf29ce484222325
fold-object=a9bca0cca21f5f13
fold-pure=a9bca0cca21f5f13
```

<!-- test: the-pure-fold-carries-no-length-tag-either -->
⭐ **THE PROPERTY THE PURE FACE MUST ALSO NOT HAVE.** Three groupings of the same three bytes reach
one digest through `Hasher.combined`, exactly as they do through `combine`. A length tag smuggled
into either face — but especially into the one that looks like a convenience helper — would break
this and nothing else in the tree would name the difference.
```maxon
typealias PartArray = Array with String

function foldParts(parts PartArray) returns HashDigest
	var state = Hasher.empty()
	for part in parts 'each'
		state = Hasher.combined(state, bytes: part.toByteArray())
	end 'each'
	return state
end 'foldParts'

function main() returns ExitCode
	print("whole={foldParts(["abc"]):016x}\n")
	print("ab-c={foldParts(["ab", "c"]):016x}\n")
	print("a-bc={foldParts(["a", "bc"]):016x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
whole=e71fa2190541574b
ab-c=e71fa2190541574b
a-bc=e71fa2190541574b
```

<!-- test: a-pure-fold-nests-where-a-hasher-cannot -->
`mixBytes` as `maxon-shv2/Compiler/ContentHash.maxon` actually writes it: one expression, the inner
fold feeding the outer. The three digests are the ones the object-face case above already commits, so
this pins the two spellings of the tagged fold to the SAME values — a refactor that moved a digest
would redden here rather than silently rekeying every memo in the query spine.
```maxon
typealias PartArray = Array with String

function mixBytes(state HashDigest, bytes ByteArray) returns HashDigest
	return Hasher.combined(Hasher.combined(state, bytes: bytes), value: bytes.count())
end 'mixBytes'

function foldParts(parts PartArray) returns HashDigest
	var state = Hasher.empty()
	for part in parts 'each'
		state = mixBytes(state, bytes: part.toByteArray())
	end 'each'
	return state
end 'foldParts'

function main() returns ExitCode
	print("whole={foldParts(["abc"]):016x}\n")
	print("ab-c={foldParts(["ab", "c"]):016x}\n")
	print("a-bc={foldParts(["a", "bc"]):016x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
whole=fc17b883ee074f58
ab-c=9b03a82f48f10a60
a-bc=3ba8b80874c9ff72
```
