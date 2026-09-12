---
feature: jcc-erratum-padding
status: experimental
keywords: [codegen, layout, x64, jcc, erratum, branch, alignment, nop, padding, loop]
category: codegen
---
# No jump crosses or ends on a 32-byte boundary

## Documentation

Intel's Skylake-family microcode (erratum SKX102 and its siblings, in every core from Skylake
through Comet Lake) cannot keep the decoded µops of a jump whose bytes cross a 32-byte boundary, or
whose last byte sits right before one, in the decoded-µop cache. A jump is a `jcc`, `jmp`, `call` or
`ret`, and also the macro-fused pair a `cmp`, `sub` or `and` forms with the `jcc` right after it —
the pair is one µop, so it is one jump. A tight loop containing such a jump runs from the legacy
decoder at a fraction of its speed, and which loop that is depends on nothing but where the bytes
happened to land: two textually identical loops in one program can differ by a third.

The x64 backend lays out every such unit inside one 32-byte window. In `emitFunctionChunk`'s block
loop a unit — a jump, a call, a return, or a fusible ALU op with the `jcc` after it — is emitted and
then measured; one that straddles a window boundary, or ends exactly on one, is rolled back together
with every fixup record it filed, padded forward to the next boundary with Intel's recommended
multi-byte NOPs, and emitted again. Nothing tables an instruction's width: the encoder itself says
where the unit ended, so `call [base+disp]`'s REX- and displacement-dependent length is exact. The
rule is stated over the final address, so `concatX64FunctionChunks` starts every function chunk on
a 32-byte boundary (the gap is `int3`) and the chunk-local offset the block loop reasons in has the
window of the address the writer gives it wherever `.text` itself starts on one — the PE writer
places it at RVA `0x1000`.

The scope is the TargetOps of compiled functions. A hand-assembled sequence places its own branches
through the shared `encodeJcc`/`encodeJmp` primitives and is left byte-exact: the runtime chunks
(`specs/emitted-runtime-body.md` pins their bytes), the system-stack shim an `iatCall` becomes in a
program with the green-thread scheduler, the prologue's stack-probe walk, and the dense-match jump
table. arm64 needs none of this: its instructions are four bytes on four-byte alignment, so none
straddles a fetch line, and its cores carry no such erratum.

The padding is bytes, not ops. A fragment golden renders TargetOps, so it records the shape of the
loop the case below compiles and its exit code records the answer; the placement itself is below the
IR, and is read off the emitted binary with a disassembler, which is how `tests/profile`'s
green-thread corpus measures it: two loops of the same text, sampled in proportion to their work.

## Tests

<!-- test: a-tight-loops-back-edge-never-straddles-a-32-byte-boundary -->
A counted loop whose back edge is a `cmp`/`jcc` pair, the unit the erratum most often lands on. The
exit code is the loop's answer; the golden records the loop's shape.
```maxon
typealias Rounds = int(0 to 1000000)

function spin(rounds Rounds) returns Rounds
	var acc = 0
	var i = 0

	while i < rounds 'spin'
		acc = acc + i
		i = i + 1
	end 'spin'

	return acc mod 251
end 'spin'

function main() returns ExitCode
	return spin(1000)
end 'main'
```
```exitcode
10
```
