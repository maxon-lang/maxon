---
feature: match-expr-divergent-class
status: stable
keywords: match, expression, gives, float, register-class, E2015, unsupported
category: control-flow
---
# A Match Expression Whose Arms Cross Register Classes Is REFUSED, Not Crashed

## Documentation

Every `gives` arm of a match expression feeds ONE result phi, and a value's register file — general
(int/bool) vs XMM (float) — is fixed by its type at birth. So an integer give and a float give in the
same match hand the phi two values from different files. The register allocator cannot color a move
across files (`X64Backend.emitRegRegMove`); before this was guarded, such a program **crashed the
compiler** with `panic: crosses register files` — a backend panic on a user program, not a diagnostic.

The reference oracle UNIFIES such arms: it promotes the integer arms to float (`cvtsi2sd`) so the result
is uniformly float, then the surrounding context decides (returning that float as an `ExitCode` is a
separate `E3009`). shv2 has both the instruction (`promoteToFloat`) and the lattice already — but the
*result* of a promoted match is a float **value**, and a float has no nameable type in shv2 yet
(`typealias F = float(…)` is itself still `E2015`). A promotion whose result nothing can hold or name is
the `mintPhi` trap: a mechanism with no consumer. So it is deferred to the float type-system rung, and
until then a cross-class match expression is refused **loudly and positioned**, exactly as every other
unbuilt scalar surface is — never allowed to reach the backend.

**Same-class arms are unaffected** and match the oracle exactly: `1 gives true  2 gives 5` (both general
registers) types the result off the first arm and reports `E3005` on a `bool`→`int` return, byte-for-byte
what the bootstrap says. Only a genuine GP↔XMM crossing reaches the refusal below.

## Tests

<!-- test: error.int-and-float-arms -->
```maxon
function main() returns ExitCode
	let x = 1
	let r = match x 'a'
		1 gives 5
		default gives 7.5
	end 'a'
	return r
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/match-expr-divergent-class/error.int-and-float-arms.test:4:10: Unsupported: a match expression whose arms give values of different register classes (a float arm and a non-float arm) — unifying them promotes the integer arms to float, whose result has no nameable type until the float type system lands (a `float` typealias is itself not yet parsed), and it arrives with that rung
```

<!-- test: error.float-then-int-arms -->
```maxon
function main() returns ExitCode
	let x = 1
	let r = match x 'a'
		1 gives 2.5
		default gives 9
	end 'a'
	return r
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/match-expr-divergent-class/error.float-then-int-arms.test:4:10: Unsupported: a match expression whose arms give values of different register classes (a float arm and a non-float arm) — unifying them promotes the integer arms to float, whose result has no nameable type until the float type system lands (a `float` typealias is itself not yet parsed), and it arrives with that rung
```
