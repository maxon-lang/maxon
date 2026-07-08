---
feature: witness-managed-return-throws
status: selfhosted
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

This is `status: selfhosted`: C# devirtualizes the dispatch and its uniform-borrow
model balances the success result, but the C# oracle LEAKS the caught box on the
diverging `otherwise` here (a C#-oracle-side defect), so the two compilers cannot
share a leak-gate verdict. The self-hosted compiler owns this spec and releases
the box; C# skips it.

Runs under the suite's leak gate AND `--rc-sanitize`, so a missing result
classification (dangle/double-free) or a missing error-flag classification (leak)
fails it. Restricted to the register-frame targets for the same
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
