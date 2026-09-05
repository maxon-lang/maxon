---
feature: debug-info-locals
status: experimental
keywords: [debug-info, mxdbg, locals, types, fields, register allocation, merge, generics, sidecar]
category: tooling
---
# Debug-Info Locals and Types

## Documentation

`maxon build` writes a `<output>.mxdbg` sidecar by default (see `debug-info-spans`). Past its header,
files, functions and line rows the sidecar carries three further tables: a whole-program TYPE table
(name, kind, size, align, and a window into a FIELD sub-table) and, per function, a window of LOCAL
records (name, location, type id, scope range). A debugger that can name a function and stop on a line
of it still cannot show a single VALUE without those three.

### What `<!-- DebugInfo -->` verifies here

The directive compiles a case's run binary the way `maxon build` does, and the runner then OPENS the
sidecar and checks it can be walked: a non-zero type count, every field and local `typeId` inside the
type table, every type's field window and every function's local window inside their tables, and the
local windows contiguous and non-overlapping in function order. **A sidecar that merely EXISTS proves
nothing** — a window running past the end of its table, or a `typeId` pointing at nothing, is a file
that loads and then answers confidently about the wrong record.

⭐ **A count of zero is the shape a whole missing table hides in.** A writer that recorded no types at
all produces a file every offset check passes, so the type count is asserted NON-ZERO: every program
has types, and a table that names none is a table nobody filled.

### The three shapes a name or a location is lost in

A local record joins a SOURCE NAME to a MACHINE LOCATION, and each of these programs breaks one of the
two joins in a different place.

**Registers and spills are two different carriers, and a function has only one of them.** shv2's
allocator colours a small working set entirely into registers — `coloured` below has three
simultaneously-live scalars and gets a frame with no spill slots at all — so every one of its locals
lives in a register and none has a stack displacement to record. `spilled` holds a band of sixteen
bindings live ACROSS its loop, which the allocator spills around it, so every one of those has a stack
slot and no register. ⇒ A carrier that only knows how to state one of the two is red on one of these
two functions whichever one it implements, and a carrier that GUESSES the other — a slot number for a
value that has no slot — is a debugger printing a byte of somebody else's frame.

⚠ **The band must be live ACROSS the loop, never inside it.** shv2 REFUSES a loop whose working set
exceeds the register file (E5001) rather than spilling into the body, so a program that tries to force
spills by widening a loop's working set does not compile at all.

**One source name owns SEVERAL SSA values.** `Parser.mergeAtContinuation` mints a FRESH `ValueId` where
control flow rejoins, so a `var` assigned in both arms of an `if` and read afterwards is three values
under one name. A name channel keyed one value to one name loses the binding here: the value the reader
after the merge actually holds is the one the parser minted, and it was never the value the name was
attached to.

**A generic body is DICTIONARY-PASSED, not monomorphized.** shv2 compiles one body and passes a witness
rather than cloning per instantiation, so a name attached to a shared body has to survive being reached
through an instantiation that is not the declaration. This is the third program.

## Tests

<!-- test: registers-and-spills-both-name-their-locals -->
### A register-resident function and a spilling one both compile with debug info on
`coloured` holds three simultaneously-live scalars, few enough that the allocator colours them all into
registers; `spilled` holds sixteen bindings live across its loop, which are spilled around it. The two
functions are the two location carriers, in one program, so a build that can only state one of them
cannot be green here by implementing the other.
<!-- DebugInfo -->
```maxon
typealias Tick = int(0 to 1000)

function coloured(seed Tick) returns Tick
	var carry = seed
	var step = 1
	var rounds = 0
	while rounds < 3 'mix'
		carry = (carry + step) mod 97
		step = step + rounds
		rounds = rounds + 1
	end 'mix'

	return carry
end 'coloured'

function spilled(seed Tick) returns Tick
	let a = seed + 1
	let b = seed + 2
	let c = seed + 3
	let d = seed + 4
	let e = seed + 5
	let f = seed + 6
	let g = seed + 7
	let h = seed + 8
	let j = seed + 9
	let k = seed + 10
	let m = seed + 11
	let n = seed + 12
	let p = seed + 13
	let q = seed + 14
	let r = seed + 15
	let t = seed + 16
	var carry = seed
	var rounds = 0
	while rounds < 4 'grind'
		carry = (carry + rounds) mod 89
		rounds = rounds + 1
	end 'grind'

	return (carry + a + b + c + d + e + f + g + h + j + k + m + n + p + q + r + t) mod 89
end 'spilled'

function main() returns ExitCode
	print("{coloured(2)}:{spilled(3)}\n")

	return 0
end 'main'
```
```stdout
6:15
```
```exitcode
0
```

<!-- test: a-var-rebound-across-a-merge-keeps-its-name -->
### A `var` assigned in both arms of an `if` compiles with debug info on
`chosen` is written in both arms and read past the join, so the parser mints a fresh `ValueId` at the
continuation and one source name owns three SSA values. A name channel keyed one-to-one attaches the
name to the value the DECLARATION produced, which is dead by the time the read happens — the binding a
debugger would be asked about is the merged one.
<!-- DebugInfo -->
```maxon
typealias Tick = int(0 to 1000)

function pick(flag bool, lo Tick, hi Tick) returns Tick
	var chosen = 0
	if flag 'takeHigh'
		chosen = hi + 1
	end 'takeHigh' else 'takeLow'
		chosen = lo + 2
	end 'takeLow'

	return chosen
end 'pick'

function main() returns ExitCode
	print("{pick(true, lo: 1, hi: 2)}:{pick(false, lo: 1, hi: 2)}\n")

	return 0
end 'main'
```
```stdout
3:3
```
```exitcode
0
```

<!-- test: locals-inside-a-generic-body-are-named -->
### Named locals inside a generic body compile with debug info on
`fold` is the body of an `Accum uses T`, reached through an instantiation rather than through the
declaration. shv2 dictionary-passes instead of monomorphizing, so the body carrying these names is
SHARED: a channel that records a name against the instantiation that happened to reach it first, or one
that loses the names of a body it did not itself elaborate, is red here rather than silently shipping a
generic every debugger has to step through blind.
<!-- DebugInfo -->
```maxon
typealias Tick = int(0 to 1000)

type Accum uses T
	export var seed as T

	export static function create(seed T) returns Self
		return Self{seed: seed}
	end 'create'

	export function fold(bump Tick) returns Tick
		var total = bump
		var step = 1
		var rounds = 0
		while rounds < 3 'roll'
			total = (total + step) mod 91
			step = step + rounds
			rounds = rounds + 1
		end 'roll'

		return total
	end 'fold'
end 'Accum'

typealias TickAccum = Accum with Tick

function main() returns ExitCode
	let acc = TickAccum.create(5)
	print("{acc.fold(7)}\n")

	return 0
end 'main'
```
```stdout
11
```
```exitcode
0
```
