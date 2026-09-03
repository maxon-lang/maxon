---
feature: scaled-index-addressing
status: experimental
keywords: [optimizer, codegen, addressing, index, scale, sib, array, element]
category: codegen
---
# `[base + index*scale + disp]` — the addressing mode the index scaling folds into

## Documentation

Every element access in the language is spelled by `ManagedMemoryRuntime.emitSlotAddr` as
`index * element_size` feeding `buffer + offset` feeding a `loadIndirect`/`storeIndirect`, and
`InlineManagedPrimitives` (EC1) puts that inline at every array read and write in every program. Until
EC16 the x64 dialect could express exactly `[base + disp]` and `[base + index]` at **scale 1**, so the
scaling was a separate instruction:

```
imul rcx, r13, 8      ; scale the index
lea  rax, [rbx + rcx] ; add the buffer
mov  rax, [rax + 0]   ; read the element
```

Three instructions for what `mov rax, [rbx + r13*8]` does in one. `StdLoweringShared.ScaledIndexFolds`
matches that chain at instruction selection and hands the whole address to the memory op.

### THREE CONDITIONS, and each of them is a case below

- **The multiply's multiplier must be 1, 2, 4 or 8.** `MemoryIndexScale` states that set once, as an
  enum's case list, because it is what the hardware's SIB scale field and AArch64's `LSL` amount both
  hold. A multiply by 16 has no addressing form on either ISA and stays an instruction.
- **The intermediate must have NO OTHER READER.** A multiply something else also reads must be
  materialised anyway, so folding it into the address would ADD an instruction rather than remove one.
  `a-scaled-address-with-no-memory-reader-is-still-one-lea` carries that control as its SECOND
  function, beside the one that folds — which is what says the condition is load-bearing rather than
  tidy: the shared multiply's `imul` survives in the committed fragment and both its `+` stay plain.
- **The chain must be in ONE BLOCK.** SSA makes a cross-block fold *correct* — the add's def dominates
  the memory op, so its own operands do too — but the live ranges of `base` and `index` would then
  stretch across a block boundary in place of a single value's, and shv2 REFUSES rather than spills
  (`RegisterPressureDiagnostic`; EC13 measured exactly that as an `E5001`). Inside one block the
  extension is bounded by the window between the add and its reader, which for the shape above is
  empty.

### The `lea` fires even where the memory fold does not, and it is pressure-neutral

`base + i * 8` with no memory op at all is still ONE `lea` rather than an `imul` plus a `lea`, and it
costs no register: before, `index` was live to the `imul` and the multiply's RESULT live from there to
the `add`; after, `index` is live to the `lea` and there is no intermediate at all. Same count of
simultaneously-live values at every point, one fewer instruction.
`a-scaled-address-with-no-memory-reader-is-still-one-lea` is that reading.

### ⭐ FLAGS ARE SAFE IN ONE DIRECTION, AND THIS IS IT

The fold DELETES an `imul` (which writes OF/CF) and replaces a `lea` (which writes none) with another
`lea`. It can only ever REMOVE a flag write, so a compare/branch fusion that the Std-tier scan already
decided is either unchanged or was already declined — and declining to fuse is always safe.

### ⚠ The BYTE-strided arm must not regress, and it is a different shape

An `Array with` a one-byte element has stride 1, so `index * 1` is folded to `index` by
`foldConstOperands`' identity rule long before this pass — there is no multiply to absorb, and the arm
keeps its scale-1 `leaRegRegReg` plus a `movzx`. `a-byte-strided-element-keeps-its-scale-1-address` is
the pin: a fold that "helpfully" rewrote it would be changing code that was already optimal.

### arm64 takes both halves, and its memory half is NARROWER

`ADD Xd, Xn, Xm, LSL #k` is exactly `lea [base + index*2^k]`, and arm64 gains MORE from it than x64
does — AArch64 has no multiply-immediate at all, so the scaling was a `movz` into the IP scratch plus a
register `MUL`, three instructions where this is one.

Its `LDR Xt, [Xn, Xm, LSL #k]` then carries the whole address, but on **strictly narrower terms** than
x64's SIB byte, and both narrowings are the encoding's rather than a conservatism:

- **The scale must BE the access size.** The `S` bit is one bit — shift 0, or exactly log2(the access
  size). There is no `LSL #1` under a 64-bit access, so a `word64` element at stride 4 has no form.
- **The displacement must be zero.** The register-offset encoding has no displacement field at all; the
  12-bit one belongs to the immediate-offset form, which has no index register.

Every element access the language spells is a `word64` at stride 8 and offset 0, which satisfies both —
so the arm64 fragments show `loadRegBaseIndexScale`/`storeBaseIndexScaleReg` exactly where the x64 ones
do. A chain outside those terms keeps its own `arm64AddLsl` and the access reads the result.
`StdLoweringShared.indexedFormCarries` states each ISA's reach once; `TargetDialect.arm64AddLsl` and
`TargetDialect.loadRegBaseIndexScale` carry the encodings.

## Tests

<!-- test: an-indexed-element-read-is-one-instruction -->
The shape the row was opened for. `for v in a` over an `Array with` an 8-byte element reads each
element through `loadRegBaseIndexScale.word64 rax, [rax + <i>*8 + 0]` — one instruction where the
committed fragment used to show `imul` / `lea` / `mov`. The sum is checked so a wrong address is a
wrong exit code rather than a silent pass.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function total(a WordArray) returns Word
	var t = 0
	for v in a 'loop'
		t = t + v
	end 'loop'
	return t
end 'total'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 6 'seed'
		a.push(i * i)
	end 'seed'
	if total(a) != 55 'sum'
		return 1
	end 'sum'
	if a.count() != 6 'count'
		return 2
	end 'count'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-indexed-element-write-is-one-instruction -->
The store mirror: `storeBaseIndexScaleReg.word64 [<buf> + <i>*8 + 0], <value>`. The index is a loop
variable the compiler cannot see through, and every slot is written and then read back, so an address
off by a scale factor loses or duplicates an element and the readback disagrees.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function fill(a WordArray, n Word)
	for i in 0 upto n 'each'
		try a.set(i, value: i * 3 + 1) otherwise panic("set: index out of range")
	end 'each'
end 'fill'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 5 'seed'
		a.push(0)
	end 'seed'
	fill(a, n: 5)
	var seen = 0
	for i in 0 upto 5 'check'
		seen = seen + (try a.get(i) otherwise panic("get: index out of range"))
	end 'check'
	if seen != 35 'sum'
		return 1
	end 'sum'
	if (try a.get(4) otherwise panic("get: index out of range")) != 13 'last'
		return 2
	end 'last'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-scaled-address-with-no-memory-reader-is-still-one-lea -->
Two functions, one program, and the DIFFERENCE between them is the whole of the single-reader rule.

`scaledAddress` computes `base + i * 8` and nothing else reads the scaled value, so it is one
`leaRegBaseIndexScale` with no `imul` at all — the address form firing where there is no memory op to
fold into.

`sharedScaling` computes the same `i * 8` and reads it THREE times, so the multiply must be
materialised anyway: folding it into either `+` would have added an instruction rather than removed
one. Its `imulRegRegImm32 …, 8` therefore SURVIVES in the committed fragment and both its `+` stay a
plain `leaRegRegReg`. ⭐ That surviving `imul` is the control: delete the `containsRepeat` check in
`ScaledIndexFolds.tryIndexedAddress` and this fragment gains a scaled `lea` while keeping the `imul`,
which is one instruction MORE than it started with.

Both are fed from a loop counter so nothing here is constant-folded away.
```maxon
typealias Word = int(i64.min to i64.max)

function scaledAddress(base Word, i Word) returns Word
	return base + i * 8
end 'scaledAddress'

function sharedScaling(base Word, i Word) returns Word
	let scaled = i * 8
	let hi = base + scaled
	let lo = base - scaled
	return hi + lo + scaled
end 'sharedScaling'

function main() returns ExitCode
	var seed = 0
	for i in 0 upto 4 'feed'
		seed = seed + scaledAddress(i, i: i + 1) + sharedScaling(i, i: i + 2)
	end 'feed'
	// scaledAddress: 8, 17, 26, 35 = 86.  sharedScaling: 2*base + 8*(i+2) = 16, 26, 36, 46 = 124.
	if seed != 210 'value'
		return 1
	end 'value'
	if scaledAddress(0, i: seed) != 1680 'scaleOfARuntimeValue'
		return 2
	end 'scaleOfARuntimeValue'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-byte-strided-element-keeps-its-scale-1-address -->
The stride-1 arm, which has no multiply to absorb: `index * 1` was folded to `index` by
`foldConstOperands` long before this pass, so the address stays a scale-1 `leaRegRegReg` and the read
stays a `loadRegBaseDisp.byte`. A fold that rewrote this would be changing code that was already one
instruction shorter than the scaled form.

⚠ The fragment shows BOTH arms, and that is the point of reading it here rather than in the integer
case: `emitStrideDispatch` emits the word arm (now one `loadRegBaseIndexScale`) and the byte arm (still
`leaRegRegReg` + `loadRegBaseDisp.byte`) side by side even for a ByteArray. ⚠ **EC15 landed and did NOT
remove the arm this program never takes, deliberately** — a byte-stamped container's record is stride 1
or the machine word (a shared generic body creates its `Array with Element` at the word slot and hands
it back under a substituted concrete type), so the fork is exactly the question that distinguishes them.
`specs-shv2/static-stride-specialization.md` carries the measurement and the case that fails without it.
The INTEGER cases above are where EC15 does fire, and their fragments no longer show a fork at all.
```maxon
typealias Byte = int(0 to 255)
typealias Total = int(0 to u64.max)

function sumBytes(b ByteArray) returns Total
	var t = 0
	for v in b 'loop'
		t = t + v
	end 'loop'
	return t
end 'sumBytes'

function main() returns ExitCode
	var b = ByteArray.create()
	for i in 0 upto 10 'seed'
		b.push(i as Byte)
	end 'seed'
	if sumBytes(b) != 45 'sum'
		return 1
	end 'sum'
	if b.count() != 10 'count'
		return 2
	end 'count'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-float-element-reads-through-the-same-addressing-mode -->
An `Array with` a float element is 8 bytes wide like an integer one, so its slot address takes the same
`×8` scale — and the value moves through the same word-width memory op, because the Std tier loads the
element's 64 bits and reinterprets them. This is the case that says the fold is about the ADDRESS and
not about what the loaded bits mean: a scale applied to the wrong width would read a float out of the
middle of two.
```maxon
typealias Real = float(f64.min to f64.max)
typealias RealArray = Array with Real

function total(a RealArray) returns Real
	var t = 0.0
	for v in a 'loop'
		t = t + v
	end 'loop'
	return t
end 'total'

function main() returns ExitCode
	var a = RealArray.create()
	a.push(1.5)
	a.push(2.25)
	a.push(4.0)
	if total(a) != 7.75 'sum'
		return 1
	end 'sum'
	if (try a.get(1) otherwise panic("get: index out of range")) != 2.25 'middle'
		return 2
	end 'middle'
	return 0
end 'main'
```
```exitcode
0
```
