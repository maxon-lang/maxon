---
feature: array-literal-element-size
status: selfhosted
keywords: [array, literal, byte, bytearray, element-size, stride, non-constant]
category: memory
---
# Non-Constant Narrow Array-Literal Element Size

## Documentation

An array literal (`[a, b, c]`, `ByteArray from [...]`) lowers to a runtime
`__ManagedMemory.create(count, elementSize)`. The parser hardcodes
`elementSize = 8` because element types are not resolved at parse time. A
literal whose elements are all compile-time constants is corrected to the true
narrow width via the `.rdata` fast path, but a *non-constant* narrow-element
literal — elements that are runtime values, e.g. `ByteArray from [aByteVar,
bByteVar]` — would otherwise keep the pointer-width stride. Every slot store then
lands 8 bytes apart, so any bulk read (`String.from`, `toCString`, `memcpy`) or
`grow` reads the wrong bytes.

The self-hosted compiler corrects this in `ConstantArrayLiteralRdata`'s
`stampNonConstantArrayElementSize`: post-TypeResolution it recovers the element
type from the surviving `mm.set(i, value:)` call's backfilled argument type and
rewrites the create's `elementSize` operand to the resolved width (only narrow
widths `< 8`; pointer/managed elements are untouched). This is the array-literal
twin of the `ByteArray.create() + push` grow-site stamp in
`bytearray-element-size.md`.

**Why `status: selfhosted`.** This test is not run against the C# bootstrap. The
bootstrap determines an array literal's element size at PARSE time from the first
element's value kind (`GetValueKind` in `2-Parser.cs`), and a non-constant narrow
element (e.g. a `ByteArray.get` result) is integer-storage there, so the C#
bootstrap computes `elementSize = 8` and fails this test. Sizing it correctly
requires the resolved element type, which the self-hosted compiler obtains in a
dedicated post-resolution pass — a capability the C# bootstrap's value-kind front
end lacks. The self-hosted compiler owns this correctness guarantee; the C#
bootstrap is scaffolding being retired, and its bootstrap-critical source
contains no such literal (or S1 would already miscompile a self-compiled v-next).

## Tests

<!-- disabled-test: array-literal-nonconstant-byte-stride -->
<!-- P1.8 String.toByteArray() -->
### Non-constant Byte array literal has element_size 1, not pointer-width 8
Builds a `Byte` array *literal* from runtime byte values (read out of a source
string, so the elements are non-constant and cannot take the `.rdata` constant
fast path), then bulk-reads it back with `String.from`. The parser emits the
create with a provisional `elementSize = 8`; `stampNonConstantArrayElementSize`
must rewrite it to `1`. With the wrong 8-byte stride `String.from` reads only
every 8th byte and the equality check fails (verified: exit 1 on the pre-fix
compiler, exit 0 after).
```maxon
function main() returns ExitCode
	let src = "Hi!"
	let sb = src.toByteArray()
	let x = try sb.get(0) otherwise 0
	let y = try sb.get(1) otherwise 0
	let z = try sb.get(2) otherwise 0
	let bytes = [x, y, z]
	let s = String.from(bytes)
	return 0 if s == "Hi!" else 1
end 'main'
```
```exitcode
0
```
