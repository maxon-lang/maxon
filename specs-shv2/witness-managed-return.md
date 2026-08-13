---
feature: witness-managed-return
status: stable
keywords: [witness, interface, dispatch, managed, refcount, memory, uaf]
category: memory-safety
---

# Witness Dispatch: Managed Struct Return Ownership

## Documentation

An interface method whose declared return type is a MANAGED struct (not another
interface, not a scalar) is dispatched through the witness table. The witness
call hands back a freshly-created struct that OWNS a `+1` — exactly like a direct
call return. The refcount inserter can only balance that reference if the witness
result is classified as an owning call return (`callReturnRc1`); otherwise it is
left `notManaged`, and every store/consume sweep skips it.

When such an unclassified result is stored into TWO owning sinks — a long-lived
container/field AND a transient local array — no store mints a copy, so the two
sinks physically alias one `+1`. The transient array's teardown then frees the
struct out from under the long-lived container, dangling it. Reading the
container afterward is a use-after-free.

This is the reduced form of the self-hosted compiler's own `emitFunctionChunk`
chunk-cache dangle: `backend.emitFunctionChunk(func)` is a witness call returning
a managed `FunctionCodeChunk`, stamped into the persisted `db.codePerFunc` memo
AND pushed into a transient `chunks` array. Before the witness result was
classified, `chunks`'s teardown freed the chunk while the persisted memo still
referenced it — surfacing as a poison read in `encodeUserChunkRecord`. The fix
classifies a plain (non-fat, non-throwing) managed witness return as
`callReturnRc1` so both stores incref and the reference is balanced.

These tests run under the suite's leak gate AND `--rc-sanitize`, so a missing
store-incref (dangle / double-free) or a skipped decref-old (leak) fails them.

## Tests

<!-- test: two-sink-persist-and-transient -->
<!-- targets: x64-windows, x64-linux, x64-macos, arm64-windows, arm64-macos, arm64-linux -->
A managed struct returned from a witness-dispatched interface method, stored into
a long-lived field AND pushed into a transient local array that is torn down at
the method's scope exit. The persisted field is read AFTER the transient array is
gone: without a store-incref on the field write, the array's teardown frees the
struct and the read dangles (pre-fix: `__mm_decref: over-release` in
`__destruct_Store`).

Restricted to the register-frame targets. The fix (classifying the witness
result as owning its `+1`) is target-independent IR, but it inserts an
`__mm_incref` between the witness-call result and its SECOND use (the transient
push). On `wasm32-wasi` the witness-call result lands in an unpromoted
linear-memory slot from the ONE shared slot region, and the intervening incref
call clobbers it, so the push reads a stale pointer — the same slot-persistence
limitation `refcount-consumed-interface-param-slot` excludes wasm for (needs a
per-invocation shadow stack). A DIRECT-call result in the identical two-sink
shape is promoted and survives, so it is specifically the witness-result slot.
The single-sink `repeated-persist-overwrites-decref-old` below runs everywhere.
```maxon
typealias Payload = int(0 to u64.max)
typealias ChunkArray = Array with Chunk

// A managed struct returned BY VALUE from a witness-dispatched method — the plain
// (non-fat) witness-return path. Freshly created it owns rc 1.
type Chunk
	export var payload as Payload

	static function create(payload Payload) returns Chunk
		return Self{payload: payload}
	end 'create'
end 'Chunk'

interface ChunkMaker
	function makeChunk() returns Chunk
end 'ChunkMaker'

type Backend implements ChunkMaker
	export let seed as Payload

	static function create(seed Payload) returns Backend
		return Self{seed: seed}
	end 'create'

	function makeChunk() returns Chunk
		return Chunk.create(self.seed)
	end 'makeChunk'
end 'Backend'

// The long-lived sink. `populate` persists the witness-dispatched chunk into
// `self.saved` AND pushes the SAME chunk into a transient local array that is
// freed at the method's scope exit — before `main` reads `self.saved`.
type Store
	export var saved as Chunk

	static function create() returns Store
		return Self{saved: Chunk.create(0)}
	end 'create'

	function populate(maker ChunkMaker)
		var transient = ChunkArray.create()
		let chunk = maker.makeChunk()
		self.saved = chunk
		transient.push(chunk)
	end 'populate'

	function readSaved() returns Payload
		return self.saved.payload
	end 'readSaved'
end 'Store'

// Returns an INTERFACE value so the callee's static receiver type is the bare
// interface — forcing `maker.makeChunk()` to dispatch through the witness table
// rather than a monomorphized direct call.
function obtainMaker(seed Payload) returns ChunkMaker
	return Backend.create(seed)
end 'obtainMaker'

function main() returns ExitCode
	var store = Store.create()
	let maker = obtainMaker(42)
	store.populate(maker)
	print("{store.readSaved()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

<!-- test: repeated-persist-overwrites-decref-old -->
Repeatedly persisting a fresh witness-dispatched chunk into the same field —
dispatching off ONE long-lived interface value across the loop, mirroring the
compiler's own `emitBackendIncrementalWith` calling `backend.emitFunctionChunk`
per function with the same `backend`. Each overwrite must incref the new chunk
and decref the previous occupant (mirrors `codePerFunc.upsert` replacing a memo):
a skipped incref-new dangles the survivor, a skipped decref-old leaks every
superseded chunk (plus the initial one). The final read must see the last value.
```maxon
typealias Payload = int(0 to u64.max)

type Chunk
	export var payload as Payload

	static function create(payload Payload) returns Chunk
		return Self{payload: payload}
	end 'create'
end 'Chunk'

interface ChunkMaker
	function makeChunk() returns Chunk
end 'ChunkMaker'

type Backend implements ChunkMaker
	export let seed as Payload

	static function create(seed Payload) returns Backend
		return Self{seed: seed}
	end 'create'

	function makeChunk() returns Chunk
		return Chunk.create(self.seed)
	end 'makeChunk'
end 'Backend'

type Store
	export var saved as Chunk

	static function create() returns Store
		return Self{saved: Chunk.create(0)}
	end 'create'

	function refresh(maker ChunkMaker)
		self.saved = maker.makeChunk()
	end 'refresh'

	function readSaved() returns Payload
		return self.saved.payload
	end 'readSaved'
end 'Store'

function obtainMaker(seed Payload) returns ChunkMaker
	return Backend.create(seed)
end 'obtainMaker'

function main() returns ExitCode
	var store = Store.create()
	let maker = obtainMaker(40)
	var i = 1
	while i <= 4 'loop'
		store.refresh(maker)
		i = i + 1
	end 'loop'
	print("{store.readSaved()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
40
```
