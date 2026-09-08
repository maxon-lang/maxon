---
feature: generic-hash-table-regalloc
status: experimental
keywords: [register-allocator, e5001, false-positive, generics, witness-table, forced-bracket, hall, rule-3]
category: register-allocator
---

# A constrained generic's hidden parameters must not raise E5001

## Documentation

A FALSE `E5001` is the worst bug this compiler can have, and a constrained generic body is where
the register allocator most easily produced one — because such a body holds values the author
never wrote and cannot delete.

`type Tbl uses Key where Key is Hashable and Equatable` compiles to methods carrying **hidden
parameters**: a layout descriptor for `Key`, plus one witness-table pointer per constraint. A
method that forwards them to another method of the same instance keeps them live across everything
in between — including a loop. They are then indistinguishable, to the pressure analysis, from the
author's own values, and they push the loop's working set over the pool.

That is exactly RULE 3's case (`ARCHITECTURE.md`, register-allocator section): *no
compiler-introduced value may ever appear in an `E5001` blocking set*, because an agent told to
delete a value that is not in its source cannot converge. The design's answer for a dictionary
parameter is stated there too — *"under a forced bracket it simply spills around the call and
blocks nothing."*

### Why it was refused anyway: the ranking, not the rule

`peakOutranks` places every FULL-POOL overflow above every CONFINED one, so while any op overflows
its own pool no confined rank can be the function's peak. At a full-pool peak the only relief is a
COLD split — a value idle across the whole region — and a hidden parameter forwarded to a call
*inside* the loop is not idle. `chooseVictim` returned `none`, and the driver refused.

But the values at that peak were **also confined**: 14 of the 16 were live across a method call in
the same loop, so the ABI allows them only the 5 callee-saved registers. Hall's condition was
violated there by nine. The program could not compile without ~9 forced brackets — the ABI decided
that, not the allocator — and once they are emitted the full-pool peak is gone. **Refusing at the
full-pool peak measured the loop's working set before the ABI had finished shrinking it.**

`SplitLiveRanges.confinedOverflowAtPeak` closes that: where a full-pool peak has no relievable
value, Hall is asked once at that same op, and a witness that is a PROPER SUBSET of the file's pool
names a confinement the forced bracket relieves at any loop depth. A witness equal to the whole
pool is the pigeonhole restated — the loop genuinely wants more registers than exist — and E5001
stands, byte for byte. Every committed `E5001` case is a loop with no call in it, so nothing in it
is confined and nothing there moves.

## Tests

<!-- test: generic-hash-table-regalloc.rehash-loop-forwards-hidden-parameters -->
The reproducer, and the reason this file exists. `Tbl.grow`'s rehash loop holds 16 GPR values at
one op against a pool of 14; two of them are the `Hashable` and `Equatable` witness parameters,
forwarded to `insertAtSlot` inside the loop, and a third is a `try` call's compiler-synthetic error
flag. None is deletable by the author.

Before the fix this did not merely report `E5001` — it PANICKED, in `defRangeOf`'s RULE 3 backstop,
because the error flag resolves to no source span at all. The bootstrap compiles and runs the same
program.

`grow()` on an empty table takes the `newCapacity == 0` arm, so the loop body never executes: the
subject here is the ALLOCATION, and the two accessors pin that the function still did its work.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Cap = int(0 to u64.max)

enum SlotState
	Empty
	Occupied
	Deleted
end 'SlotState'

type Tbl uses Key where Key is Hashable and Equatable
	typealias KeyArray = Array with Key
	typealias ValueArray = Array with Integer
	typealias StateArray = Array with SlotState
	typealias HashSlotArray = Array with HashValue

	var keys as KeyArray
	var values as ValueArray
	var states as StateArray
	var hashes as HashSlotArray
	var count = 0
	var capacity = 0

	export static function create() returns Self
		return Self{keys: KeyArray{}, values: ValueArray{}, states: StateArray{}, hashes: HashSlotArray{}}
	end 'create'

	export function slots() returns Cap
		return capacity
	end 'slots'

	export function entries() returns Cap
		return count
	end 'entries'

	function insertAtSlot(slotIndex Cap, key Key, value Integer, hash HashValue)
		try keys.set(slotIndex, value: key) otherwise panic("k")
		try values.set(slotIndex, value: value) otherwise panic("v")
		try states.set(slotIndex, value: SlotState.Occupied) otherwise panic("s")
		try hashes.set(slotIndex, value: hash) otherwise panic("h")
		count = count + 1
	end 'insertAtSlot'

	export function grow()
		let oldCapacity = capacity
		var newCapacity = oldCapacity * 2
		if newCapacity == 0 'handle_zero'
			newCapacity = 16
		end 'handle_zero'

		var newKeys = KeyArray{}
		newKeys.resize(newCapacity)
		var newValues = ValueArray{}
		newValues.resize(newCapacity)
		var newStates = StateArray{}
		newStates.resize(newCapacity)
		var newHashes = HashSlotArray{}
		newHashes.resize(newCapacity)

		let oldKeys = keys
		let oldValues = values
		let oldStates = states
		let oldHashes = hashes
		keys = newKeys
		values = newValues
		states = newStates
		hashes = newHashes
		capacity = newCapacity
		count = 0

		let mask = newCapacity - 1

		for i in 0 upto oldCapacity 'rehash'
			let state = try oldStates.get(i) otherwise SlotState.Empty

			let isOccupied = match state 'so1'
				Occupied gives true
				Empty gives false
				Deleted gives false
			end 'so1'
			if isOccupied 'occupied'
				let key = try oldKeys.get(i) otherwise 'skip_key'
					continue
				end 'skip_key'
				let value = try oldValues.get(i) otherwise 'skip_val'
					continue
				end 'skip_val'
				let hash = try oldHashes.get(i) otherwise 'skip_hash'
					continue
				end 'skip_hash'

				var index = spreadHash(hash) and mask
				var currentState = try states.get(index) otherwise SlotState.Empty
				var isNotEmpty = match currentState 'ns1'
					Empty gives false
					Occupied gives true
					Deleted gives true
				end 'ns1'
				while isNotEmpty 'find_slot'
					index = (index + 1) and mask
					currentState = try states.get(index) otherwise SlotState.Empty
					isNotEmpty = match currentState 'ns2'
						Empty gives false
						Occupied gives true
						Deleted gives true
					end 'ns2'
				end 'find_slot'

				insertAtSlot(index as Cap, key: key, value: value, hash: hash)
			end 'occupied'
		end 'rehash'
	end 'grow'
end 'Tbl'

typealias T = Tbl with String

function main() returns ExitCode
	var t = T.create()
	t.grow()
	if t.slots() != 16 'wrongCapacity'
		return 90
	end 'wrongCapacity'
	if t.entries() != 0 'wrongCount'
		return 91
	end 'wrongCount'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: generic-hash-table-regalloc.witness-dispatch-inside-a-pressured-loop -->
The same refusal with the loop actually RUNNING, so the brackets it now emits are gated on an
ANSWER rather than on a compile. Twelve accumulators, the loop counter, `key`, and both witness
parameters are nineteen values live at once inside a loop that calls `bump` every iteration — the
pre-fix compiler reported `E5001` with a deficit of five. Every one of them is live across that
call, so all nineteen are confined to the five callee-saved registers and the forced bracket is the
placement the ABI already chose.

⚠ The witness dispatches are INSIDE the loop deliberately. Moved outside it, `key` and both
witnesses are defined and used at loop depth 0, the ordinary cold split relieves them, and the case
stops testing anything — measured: the same program with `key.hash()` after the loop compiles
byte-identically before and after the fix.

`a1..a12` start at `1..12`; each gains `bump(i) + key.hash()` = `(i + 1) + 5` for `i = 0,1,2`, i.e.
`+21`. So they end at `22..33`, summing to 330.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntKey = int(0 to u32.max)

function bump(x Integer) returns Integer
	return x + 1
end 'bump'

type Mixer uses T where T is Hashable and Equatable
	var base as Integer

	export static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'

	export function mix(key T) returns Integer
		var a1 = base + 1
		var a2 = base + 2
		var a3 = base + 3
		var a4 = base + 4
		var a5 = base + 5
		var a6 = base + 6
		var a7 = base + 7
		var a8 = base + 8
		var a9 = base + 9
		var a10 = base + 10
		var a11 = base + 11
		var a12 = base + 12
		var i = 0
		while i < 3 'loop'
			let d = bump(i)
			let h = key.hash()
			var step = d
			if key.equals(key) 'sameKey'
				step = d + (h as Integer)
			end 'sameKey'
			a1 = a1 + step
			a2 = a2 + step
			a3 = a3 + step
			a4 = a4 + step
			a5 = a5 + step
			a6 = a6 + step
			a7 = a7 + step
			a8 = a8 + step
			a9 = a9 + step
			a10 = a10 + step
			a11 = a11 + step
			a12 = a12 + step
			i = i + 1
		end 'loop'
		return a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12
	end 'mix'
end 'Mixer'

typealias IntMixer = Mixer with IntKey

function main() returns ExitCode
	var m = IntMixer.create(0)
	if m.mix(5 as IntKey) != 330 'wrongSum'
		return 90
	end 'wrongSum'
	return 0
end 'main'
```
```exitcode
0
```
