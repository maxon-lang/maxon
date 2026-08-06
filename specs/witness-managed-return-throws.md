---
feature: witness-managed-return-throws
keywords: [witness, interface, dispatch, throws, managed, refcount, memory, boxed-union, uaf]
category: memory-safety
---

# Witness Dispatch: Throwing Managed-Return Ownership

## Documentation

The dual of `witness-managed-return` for a THROWING interface method. A witness-
dispatched interface method declared `throws E` lowers to `witnessTryCall` — the
error-ABI witness variant that defines BOTH a result register and an error flag.
Two references then cross the dispatch owning a `+1` exactly as a direct
`tryCall` does, and both must be ownership-classified:

  - On the SUCCESS edge, the managed struct RESULT owns its `+1` like any managed
    return. It is classified `callReturnRc1` so the store / last-use sweeps
    balance it — the same fix `witness-managed-return` applies to the non-throwing
    `witnessCall` result.
  - On the ERROR edge, a `throws E` whose `E` is a HEAP-BOXED union (it carries a
    payload case) hands the caller an owned box in the error register. It is
    classified so the caught box is RELEASED at its last use, not leaked.

Before the fix, `emitWitnessDispatch`'s throwing arm captured the error flag but
classified NEITHER def, so a caught boxed-union error leaked (ownership-audit
gap #3(d)) and a managed struct result went unbalanced. The refcount inserter's
`witnessTryCall`-aware error-edge machinery (`tryCallResultSuccessBlock` /
`tryCallErrorFlagOf`) lands the result's def-acquire on the SUCCESS block, so the
throw edge — whose error ABI zeroes the result register — never increfs a null
result.

The C# bootstrap reaches the same contract from the other end. It DEVIRTUALIZES
the dispatch, so there is no witness call to classify — but the decision is made
BEFORE monomorphization, in the parser, and there the callee is still the
interface. Every question a `try` asks is keyed off looking the callee's name up
in the module's function registry, and `ChunkMaker.makeChunk` is not a module
function: the whole `throws` protocol of an interface-dispatched call therefore
answered "does not throw". The cure is that the DISPATCHED SIGNATURE — which the
call site holds and the registry cannot supply — is what those questions are
asked of. It is the same fact the witness ABI carries, read at the one place the
bootstrap decides instead of at the one place the self-hosted compiler emits.

Because the answer comes off the INTERFACE and not off the impl, the two must
agree about it, and that is a second rule rather than a consequence of the first:
an impl whose `throws` differs from its requirement's would have its error
decoded as the requirement's type. `ValidateThrowsConformance` owns that relation
in both directions — see the `error.impl-*` cases below.

Runs under the suite's leak gate — and, in the self-hosted compiler, `--rc-sanitize`
— so a missing error-flag classification (leak) fails it in either. A release too
EARLY is what neither gate can see: the leak gate counts what REMAINS, so an
over-release is silent to it. That is what
`caught-boxed-error-payload-is-live-in-the-handler` is for — it READS the caught
box's payload back out in the handler, and `__mm_free` poisons what it releases.
Restricted to the register-frame targets for the same
witness-result-slot reason `witness-managed-return`'s two-sink test excludes
`wasm32-wasi`: the intervening incref clobbers the one shared linear-memory slot
region (needs a per-invocation shadow stack).

## Tests

<!-- test: throwing-witness-managed-return-both-edges -->
<!-- targets: x64-windows, x64-linux, x64-macos, arm64-windows, arm64-macos, arm64-linux -->
A throwing witness-dispatched method returning a managed `Chunk`, exercising BOTH
edges of one `witnessTryCall` shape. `attempt` dispatches through the bare
`ChunkMaker` interface. The first call SUCCEEDS — its managed result's `+1` must
be balanced. The second THROWS a boxed `MakeError.tooBig(payload)` caught by a
diverging `otherwise` — the caught box must be released, not leaked. A missing
result classification dangles/leaks the success result; a missing error-flag
classification leaks the thrown box (pre-fix: `MM leak`).
```maxon
typealias Payload = int(0 to u64.max)

type Chunk
	export var payload as Payload

	static function create(payload Payload) returns Chunk
		return Self{payload: payload}
	end 'create'
end 'Chunk'

// A boxed-union error (it carries a payload case) so the caught error is a heap
// box — exercising the `witnessTryCall` error-flag release.
union MakeError implements Error
	tooBig(limit Payload)
end 'MakeError'

interface ChunkMaker
	function makeChunk(seed Payload) returns Chunk throws MakeError
end 'ChunkMaker'

type Backend implements ChunkMaker
	export let cap as Payload

	static function create(cap Payload) returns Backend
		return Self{cap: cap}
	end 'create'

	function makeChunk(seed Payload) returns Chunk throws MakeError
		if seed > self.cap 'reject'
			throw MakeError.tooBig(self.cap)
		end 'reject'
		return Chunk.create(seed)
	end 'makeChunk'
end 'Backend'

// Returns the bare INTERFACE so `maker.makeChunk` dispatches through the witness
// table (a `witnessTryCall`, not a monomorphized direct call).
function obtainMaker(cap Payload) returns ChunkMaker
	return Backend.create(cap)
end 'obtainMaker'

function attempt(maker ChunkMaker, seed Payload, fallback Payload) returns Payload
	let chunk = try maker.makeChunk(seed) otherwise return fallback
	return chunk.payload
end 'attempt'

function main() returns ExitCode
	let maker = obtainMaker(100)
	print("{attempt(maker, seed: 42, fallback: 999)}\n")
	print("{attempt(maker, seed: 200, fallback: 7)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
7
```

<!-- test: caught-boxed-error-payload-is-live-in-the-handler -->
<!-- targets: x64-windows, x64-linux, x64-macos, arm64-windows, arm64-macos, arm64-linux -->
The over-release dual of the case above, and the sharper of the two: the leak gate
counts what REMAINS, so it is blind to a box released too EARLY. Here the `(e)`
binding is a TYPED union — which it can only be if the dispatch site knew the
requirement's `throws` type at all — and the handler READS the payload back out of
it. `__mm_free` poisons what it releases (0x3F), so a box released before its last
use prints garbage here rather than `cap 100`. The payload is a `String`, which
adds the second half: the release must run the union's destructor exactly once —
too few leaks the string, too many free it twice.
```maxon
typealias Payload = int(0 to u64.max)

union MakeError implements Error
	tooBig(why String)
end 'MakeError'

interface ChunkMaker
	function makeChunk(seed Payload) returns Payload throws MakeError
end 'ChunkMaker'

type Backend implements ChunkMaker
	export let cap as Payload

	static function create(cap Payload) returns Backend
		return Self{cap: cap}
	end 'create'

	function makeChunk(seed Payload) returns Payload throws MakeError
		if seed > self.cap 'reject'
			throw MakeError.tooBig("cap {self.cap}")
		end 'reject'
		return seed
	end 'makeChunk'
end 'Backend'

function obtainMaker(cap Payload) returns ChunkMaker
	return Backend.create(cap)
end 'obtainMaker'

function attempt(maker ChunkMaker, seed Payload) returns Payload
	try maker.makeChunk(seed) otherwise (e) 'oops'
		match e 'kind'
			tooBig(why) then print("{why}\n")
		end 'kind'
	end 'oops'
	return seed
end 'attempt'

function main() returns ExitCode
	let maker = obtainMaker(100)
	print("{attempt(maker, seed: 42)}\n")
	print("{attempt(maker, seed: 200)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
cap 100
200
```

<!-- test: caught-box-released-when-the-otherwise-arm-falls-through -->
<!-- targets: x64-windows, x64-linux, x64-macos, arm64-windows, arm64-macos, arm64-linux -->
The first case's `otherwise` DIVERGES (`return fallback`), so its release sits on a
block that leaves the function. This one falls THROUGH to the continue block and
keeps going — a second dispatch runs after it — which is the shape where a release
placed on the wrong edge reads as either a leak (never reached) or a double free
(reached twice).
```maxon
typealias Payload = int(0 to u64.max)

union MakeError implements Error
	tooBig(limit Payload)
end 'MakeError'

interface ChunkMaker
	function makeChunk(seed Payload) returns Payload throws MakeError
end 'ChunkMaker'

type Backend implements ChunkMaker
	export let cap as Payload

	static function create(cap Payload) returns Backend
		return Self{cap: cap}
	end 'create'

	function makeChunk(seed Payload) returns Payload throws MakeError
		if seed > self.cap 'reject'
			throw MakeError.tooBig(self.cap)
		end 'reject'
		return seed
	end 'makeChunk'
end 'Backend'

function obtainMaker(cap Payload) returns ChunkMaker
	return Backend.create(cap)
end 'obtainMaker'

function attempt(maker ChunkMaker, seed Payload) returns Payload
	var total = 0
	try maker.makeChunk(seed) otherwise (e) 'oops'
		match e 'kind'
			tooBig(limit) then total = limit
		end 'kind'
	end 'oops'
	let second = try maker.makeChunk(1) otherwise 0
	return total + second
end 'attempt'

function main() returns ExitCode
	let maker = obtainMaker(100)
	print("{attempt(maker, seed: 42)}\n")
	print("{attempt(maker, seed: 200)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
101
```

<!-- test: caught-box-released-once-when-propagated-onward -->
<!-- targets: x64-windows, x64-linux, x64-macos, arm64-windows, arm64-macos, arm64-linux -->
A bare `try` instead of a caught one: the dispatched error is PROPAGATED through
the enclosing function's own error channel and caught one frame further out. The
box crosses two frames and must be released exactly once, at the frame that
finally catches it — a release at the propagating frame would free a box the outer
handler then reads.
```maxon
typealias Payload = int(0 to u64.max)

union MakeError implements Error
	tooBig(limit Payload)
end 'MakeError'

interface ChunkMaker
	function makeChunk(seed Payload) returns Payload throws MakeError
end 'ChunkMaker'

type Backend implements ChunkMaker
	export let cap as Payload

	static function create(cap Payload) returns Backend
		return Self{cap: cap}
	end 'create'

	function makeChunk(seed Payload) returns Payload throws MakeError
		if seed > self.cap 'reject'
			throw MakeError.tooBig(self.cap)
		end 'reject'
		return seed
	end 'makeChunk'
end 'Backend'

function obtainMaker(cap Payload) returns ChunkMaker
	return Backend.create(cap)
end 'obtainMaker'

function attempt(maker ChunkMaker, seed Payload) returns Payload throws MakeError
	return try maker.makeChunk(seed)
end 'attempt'

function catchIt(maker ChunkMaker, seed Payload) returns Payload
	let v = try attempt(maker, seed: seed) otherwise (e) 'caught'
		match e 'kind'
			tooBig(limit) then return limit
		end 'kind'
	end 'caught'
	return v
end 'catchIt'

function main() returns ExitCode
	let maker = obtainMaker(100)
	print("{catchIt(maker, seed: 42)}\n")
	print("{catchIt(maker, seed: 200)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
100
```

<!-- test: otherwise-ignore-releases-the-box -->
<!-- targets: x64-windows, x64-linux, x64-macos, arm64-windows, arm64-macos, arm64-linux -->
`otherwise ignore` names nobody to own the box, so the release is the site's own
obligation. It is the one `otherwise` form with no handler block to hang a release
on, and the parser synthesizes a one-block branch for exactly this.
```maxon
typealias Payload = int(0 to u64.max)

union MakeError implements Error
	tooBig(limit Payload)
end 'MakeError'

interface ChunkMaker
	function check(seed Payload) throws MakeError
end 'ChunkMaker'

type Backend implements ChunkMaker
	export let cap as Payload

	static function create(cap Payload) returns Backend
		return Self{cap: cap}
	end 'create'

	function check(seed Payload) throws MakeError
		if seed > self.cap 'reject'
			throw MakeError.tooBig(self.cap)
		end 'reject'
	end 'check'
end 'Backend'

function obtainMaker(cap Payload) returns ChunkMaker
	return Backend.create(cap)
end 'obtainMaker'

function attempt(maker ChunkMaker, seed Payload) returns Payload
	try maker.check(seed) otherwise ignore
	return seed
end 'attempt'

function main() returns ExitCode
	let maker = obtainMaker(100)
	print("{attempt(maker, seed: 200)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
200
```

<!-- test: otherwise-default-value-releases-the-box -->
<!-- targets: x64-windows, x64-linux, x64-macos, arm64-windows, arm64-macos, arm64-linux -->
The `otherwise <expression>` form, whose error edge is a block the parser
synthesizes rather than one the author wrote. The default is a plain literal, so
nothing else on that edge could be releasing the box by accident.
```maxon
typealias Payload = int(0 to u64.max)

union MakeError implements Error
	tooBig(limit Payload)
end 'MakeError'

interface ChunkMaker
	function makeChunk(seed Payload) returns Payload throws MakeError
end 'ChunkMaker'

type Backend implements ChunkMaker
	export let cap as Payload

	static function create(cap Payload) returns Backend
		return Self{cap: cap}
	end 'create'

	function makeChunk(seed Payload) returns Payload throws MakeError
		if seed > self.cap 'reject'
			throw MakeError.tooBig(self.cap)
		end 'reject'
		return seed
	end 'makeChunk'
end 'Backend'

function obtainMaker(cap Payload) returns ChunkMaker
	return Backend.create(cap)
end 'obtainMaker'

function attempt(maker ChunkMaker, seed Payload) returns Payload
	return try maker.makeChunk(seed) otherwise 7
end 'attempt'

function main() returns ExitCode
	let maker = obtainMaker(100)
	print("{attempt(maker, seed: 42)}\n")
	print("{attempt(maker, seed: 200)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
7
```

<!-- test: error.try-on-a-non-throwing-requirement -->
A `try` whose target is a requirement declaring no `throws`. The concrete-receiver
spelling of this has always been E3055; the dispatched one was ACCEPTED, and the
program it produced branched on an error register the callee never wrote —
MEASURED before the fix: `try maker.plain(5) otherwise 0` printed `0` where
`plain` returns `5` and cannot fail.
```maxon
typealias Payload = int(0 to u64.max)

interface ChunkMaker
	function plain(seed Payload) returns Payload
end 'ChunkMaker'

type Backend implements ChunkMaker
	export let cap as Payload

	static function create(cap Payload) returns Backend
		return Self{cap: cap}
	end 'create'

	function plain(seed Payload) returns Payload
		return seed
	end 'plain'
end 'Backend'

function obtainMaker(cap Payload) returns ChunkMaker
	return Backend.create(cap)
end 'obtainMaker'

function attempt(maker ChunkMaker, seed Payload) returns Payload
	return try maker.plain(seed) otherwise 0
end 'attempt'

function main() returns ExitCode
	return attempt(obtainMaker(100), seed: 5) as ExitCode
end 'main'
```
```maxoncstderr
error E3055: specs/fragments/witness-managed-return-throws/error.try-on-a-non-throwing-requirement.test:25:9: try requires a throwing function: 'ChunkMaker.plain' does not throw'
```

<!-- test: error.throwing-requirement-called-without-try -->
The other side of the same missing fact: a requirement that DOES declare `throws`,
called with no `try` at all. A direct call has always been E3057; a dispatched one
compiled, dropped the error and leaked its box (MEASURED before the fix: exit 101,
`MM leak: 1 allocation(s) remain`).
```maxon
typealias Payload = int(0 to u64.max)

union MakeError implements Error
	tooBig(limit Payload)
end 'MakeError'

interface ChunkMaker
	function check(seed Payload) throws MakeError
end 'ChunkMaker'

type Backend implements ChunkMaker
	export let cap as Payload

	static function create(cap Payload) returns Backend
		return Self{cap: cap}
	end 'create'

	function check(seed Payload) throws MakeError
		if seed > self.cap 'reject'
			throw MakeError.tooBig(self.cap)
		end 'reject'
	end 'check'
end 'Backend'

function obtainMaker(cap Payload) returns ChunkMaker
	return Backend.create(cap)
end 'obtainMaker'

function attempt(maker ChunkMaker, seed Payload) returns Payload
	maker.check(seed)
	return seed
end 'attempt'

function main() returns ExitCode
	return attempt(obtainMaker(100), seed: 200) as ExitCode
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/witness-managed-return-throws/error.throwing-requirement-called-without-try.test:31:8: throwing function requires try: 'ChunkMaker.check'
```

<!-- test: error.propagates-a-different-error-type-than-the-enclosing-clause -->
Propagation re-throws the caught error through the enclosing function's own error
flag, so the two clauses must name the same type or the caller decodes one enum's
ordinals as another's. The check has always existed; on a dispatched call it never
ran, because nothing could say what the call threw. MEASURED before the fix: this
program compiled, and its caller — decoding a boxed `MakeError` as a payload-free
`FlatError` — left the box unreleased (exit 101).
```maxon
typealias Payload = int(0 to u64.max)

union MakeError implements Error
	tooBig(limit Payload)
end 'MakeError'

enum FlatError implements Error
	nope
end 'FlatError'

interface ChunkMaker
	function check(seed Payload) throws MakeError
end 'ChunkMaker'

type Backend implements ChunkMaker
	export let cap as Payload

	static function create(cap Payload) returns Backend
		return Self{cap: cap}
	end 'create'

	function check(seed Payload) throws MakeError
		if seed > self.cap 'reject'
			throw MakeError.tooBig(self.cap)
		end 'reject'
	end 'check'
end 'Backend'

function obtainMaker(cap Payload) returns ChunkMaker
	return Backend.create(cap)
end 'obtainMaker'

function attempt(maker ChunkMaker, seed Payload) throws FlatError
	try maker.check(seed)
end 'attempt'

function main() returns ExitCode
	try attempt(obtainMaker(100), seed: 200) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3059: specs/fragments/witness-managed-return-throws/error.propagates-a-different-error-type-than-the-enclosing-clause.test:35:2: try propagates 'MakeError' but enclosing function throws 'FlatError' — add 'otherwise' to convert
```

<!-- test: error.impl-throws-a-different-type-than-its-requirement -->
The soundness precondition of typing a dispatch off the REQUIREMENT: the impl has
to deliver what the requirement says it delivers. Here the requirement is a
heap-boxed union and the impl throws a payload-free enum, so the catch would treat
an ordinal as a box pointer. The check is on the conformance, not on the call —
only there is both halves of the relation in hand.
```maxon
typealias Payload = int(0 to u64.max)

union MakeError implements Error
	tooBig(limit Payload)
end 'MakeError'

enum FlatError implements Error
	nope
end 'FlatError'

interface ChunkMaker
	function check(seed Payload) throws MakeError
end 'ChunkMaker'

type Backend implements ChunkMaker
	export let cap as Payload

	static function create(cap Payload) returns Backend
		return Self{cap: cap}
	end 'create'

	function check(seed Payload) throws FlatError
		if seed > self.cap 'reject'
			throw FlatError.nope
		end 'reject'
	end 'check'
end 'Backend'

function main() returns ExitCode
	let backend = Backend.create(100)
	return backend.cap as ExitCode
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/witness-managed-return-throws/error.impl-throws-a-different-type-than-its-requirement.test:16:6: Method 'Backend.check' throws 'FlatError' but interface 'ChunkMaker' declares it 'throws MakeError' — a dispatch through an interface types its caught error off the INTERFACE, so the impl's error would be decoded as 'MakeError'
```

<!-- test: error.impl-throws-under-a-non-throwing-requirement -->
The same relation from the other side. A dispatch through a requirement that
declares no `throws` reads no error flag, so an impl that writes one is throwing
into nothing. MEASURED before the rule: the throw path's PRIMARY register came back
as a real answer (`0` where the program's only answer is `200`) and the box leaked —
exit 101. This rule's first run also found the one live instance of the shape in
this repository: `stdlib`'s `Iterable.createIterator` requirement omitted the
`throws IterationError` that all eleven of its conformers declare.
```maxon
typealias Payload = int(0 to u64.max)

union MakeError implements Error
	tooBig(limit Payload)
end 'MakeError'

interface ChunkMaker
	function plain(seed Payload) returns Payload
end 'ChunkMaker'

type Backend implements ChunkMaker
	export let cap as Payload

	static function create(cap Payload) returns Backend
		return Self{cap: cap}
	end 'create'

	function plain(seed Payload) returns Payload throws MakeError
		if seed > self.cap 'reject'
			throw MakeError.tooBig(self.cap)
		end 'reject'
		return seed
	end 'plain'
end 'Backend'

function main() returns ExitCode
	let backend = Backend.create(100)
	return backend.cap as ExitCode
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/witness-managed-return-throws/error.impl-throws-under-a-non-throwing-requirement.test:12:6: Method 'Backend.plain' throws 'MakeError' but interface 'ChunkMaker' declares it non-throwing — a dispatch through a non-throwing interface method reads no error flag, so the error would be silently dropped
```

<!-- test: abstract-requirement-narrows-to-a-scalar-error -->
<!-- targets: x64-windows, x64-linux, x64-macos, arm64-windows, arm64-macos, arm64-linux -->
The exemption the same-name rule has to carry, and the control for the refusal
below. A requirement naming an INTERFACE declares no case for the `(e)` binding to
decode wrong — a marker interface has no ordinals — so an impl may narrow it to its
own concrete error. `stdlib`'s `interface Parsable` is the reason this exists.
```maxon
typealias Payload = int(0 to u64.max)

enum ParseFailure implements Error
	badDigit
end 'ParseFailure'

interface Reader
	function read(seed Payload) returns Payload throws Error
end 'Reader'

type Digits implements Reader
	export let cap as Payload

	static function create(cap Payload) returns Digits
		return Self{cap: cap}
	end 'create'

	function read(seed Payload) returns Payload throws ParseFailure
		if seed > self.cap 'reject'
			throw ParseFailure.badDigit
		end 'reject'
		return seed
	end 'read'
end 'Digits'

function obtainReader(cap Payload) returns Reader
	return Digits.create(cap)
end 'obtainReader'

function attempt(reader Reader, seed Payload) returns Payload
	return try reader.read(seed) otherwise 9
end 'attempt'

function main() returns ExitCode
	let reader = obtainReader(100)
	print("{attempt(reader, seed: 42)}\n")
	print("{attempt(reader, seed: 200)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
9
```

<!-- test: error.abstract-requirement-narrowed-to-a-boxed-union -->
The price of the exemption above. An abstract requirement is caught through the
SCALAR `ordinal + bias` flag, because an interface has no cases whose boxedness
could be read off it — so an impl narrowing it to a PAYLOAD-CARRYING union hands
the catch a heap box pointer to decode as an ordinal, and nothing releases it. The
same pair spelled as a plain function's own `throws Error` is E3113; this is the
dispatched door into it, and it is shut here rather than opened.
```maxon
typealias Payload = int(0 to u64.max)

union ParseFailure implements Error
	badDigit(at Payload)
end 'ParseFailure'

interface Reader
	function read(seed Payload) returns Payload throws Error
end 'Reader'

type Digits implements Reader
	export let cap as Payload

	static function create(cap Payload) returns Digits
		return Self{cap: cap}
	end 'create'

	function read(seed Payload) returns Payload throws ParseFailure
		if seed > self.cap 'reject'
			throw ParseFailure.badDigit(seed)
		end 'reject'
		return seed
	end 'read'
end 'Digits'

function main() returns ExitCode
	let digits = Digits.create(100)
	return digits.cap as ExitCode
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/witness-managed-return-throws/error.abstract-requirement-narrowed-to-a-boxed-union.test:12:6: Method 'Digits.read' throws 'ParseFailure' but interface 'Reader' declares it 'throws Error', which declares no case to decode — a dispatch catches such a requirement through the SCALAR error-flag ABI, while a payload-carrying union is handed over as a heap box pointer that would be decoded as an ordinal and never released. Throw a payload-free enum, or declare the requirement as 'ParseFailure' itself
```
