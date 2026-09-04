---
feature: bool-int-type-discipline
status: stable
keywords: [bool, int, types, conversion, operators, condition, return, type-mismatch]
category: type-system
---

# A `bool` Is Not a Number

## Documentation

**Maxon permits no implicit conversion between `bool` and `int`, in EITHER direction.**
`specs/implicit-type-conversion.md` states the rule for one position — a function ARGUMENT —
and its `no-bool-to-int` / `no-int-to-bool` cases gate it there.

That spec is the whole of the upstream corpus's coverage, and **the gap is why this bug
survived**: shv2 rejected `takeInt(flag)` nowhere and `4 + flag` nowhere, and quietly compiled
both, because a bool is an `i1` carrying 1 or 0 and every arithmetic instruction is perfectly
willing to add it. `4 + flag` returned **5**. `4 * flag` returned **4**. `flag shl 4` returned
**16**. `if 4` branched on `4 != 0` and was always taken. Every one of them was a *wrong
answer*, not a crash — the worst kind of compiler bug, and the kind a spec exists to make
impossible.

The rule is ONE rule (`maxon-shv2/Compiler/IR/Maxon/TypeRules.maxon` — `typesAgree`), and the
cases below are the SYNTAXES it has to hold in:

| position | what is required | example |
|---|---|---|
| arithmetic / division / shift operand | a NUMBER on both sides | `4 + flag`, `flag shl 4` |
| unary `-` operand | a NUMBER | `-flag` |
| comparison operands | the two must AGREE (both bools, or both numbers) | `4 < flag` |
| `and` / `or` / `xor` operands | the two must AGREE | `4 and flag` — see `word-operator-mixed-operands.md` |
| `if` / `while` condition | a `bool` | `if 4` |
| **reassignment value** | **agrees with the binding's DECLARED type** | **`var x = 5` then `x = true`** |
| `return` value | agrees with the declared return type | `return flag` from `returns Integer` |
| call argument | agrees with the parameter | `takeInt(flag)` — see `implicit-type-conversion.md` |

**The reassignment row is the one that holds the other six up**, and it is not obvious. A
binding's type is fixed where it is DECLARED; a reassignment does not redeclare it. A merge phi
is stamped with the BINDING's type while carrying the REASSIGNED value — so if a reassignment
may change a binding's class, the phi LAUNDERS it, and every other rule reads the laundered tag
and is satisfied. `var x = 5` / `x = false` inside an `if` / `return x + 1` would add a bool to
an int through a phi the compiler believes is an `int`. The `laundered-*` cases below are the
regression test for exactly that, and they are why the rule is checked at the assignment rather
than at the phi: a loop-header phi is minted BEFORE the body that writes it is parsed, so there
is no incoming value to derive a type from.

**Maxon has no C-style truthiness.** `if 4` is not "if 4 is nonzero"; it is a type error. The
x64 tier tests a condition it cannot fuse with `cmp reg, 0` + `jne`, which is exactly right for
a bool (a bool IS 0 or 1) and is precisely why an `int` condition silently "worked" — the
instruction cannot tell an int from a bool. The TYPE can, so the front end is where this is
decided.

Two things are deliberately still LEGAL, and are gated below so the rule cannot be
over-applied: a comparison of two `bool`s (`a == b` — they agree), and the bitwise reading of
the word operators on two ints (`12 and 10` — see `bitwise-operators.md`).

### Authored, not ported

`/specs` covers the ARGUMENT position only (`implicit-type-conversion.md`) and the word
operators (which shv2 gates in `word-operator-mixed-operands.md`). It has no case anywhere for
a bool in an arithmetic, shift, comparison, condition, negation or return position, so those
are authored here. Their diagnostics match the C# bootstrap's byte-for-byte — same code, same
message, same line:column — wherever the bootstrap HAS one:

- `Cannot operate on int and bool` (E2004) — `maxon-sharp/Compiler/2-Parser.cs:17785`
- `type mismatch: 'cannot compare int with bool'` (E3005) — `2-Parser.cs:17777`
- `Cannot return 'bool' from function declared to return 'int'` (E3005) — `2-Parser.cs:7840`

Three cases have NO bootstrap diagnostic to match: an `int` condition, `-flag`, and `true +
false` all reach the bootstrap's Maxon→Std lowering and die there with **E9001** — an
INTERNAL-ERROR code, carrying a .NET stack trace and no source position. An internal error
leaking to a user describes no defect in the program, so it is a bootstrap bug and not a
diagnostic worth copying. shv2 reports each as a positioned diagnostic of the same family the
rest of the rule uses.

## Tests

### An `int` and a `bool` cannot be arithmetic operands

<!-- test: int-plus-bool -->
```maxon
function main() returns ExitCode
	let flag = true
	let r = 4 + flag
	return r
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:12: Cannot operate on int and bool
```

<!-- test: int-times-bool -->
```maxon
function main() returns ExitCode
	let flag = true
	let r = 4 * flag
	return r
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:12: Cannot operate on int and bool
```

<!-- test: bool-minus-int -->
```maxon
function main() returns ExitCode
	let flag = true
	let r = flag - 1
	return r
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:15: Cannot operate on bool and int
```

### A shift is integral on BOTH sides

<!-- test: bool-shl-int -->
```maxon
function main() returns ExitCode
	let flag = true
	let r = flag shl 4
	return r
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:15: Cannot operate on bool and int
```

<!-- test: int-shr-bool -->
```maxon
function main() returns ExitCode
	let flag = true
	let r = 16 shr flag
	return r
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:13: Cannot operate on int and bool
```

### Two bools AGREE — and arithmetic on them is still meaningless

Agreement is necessary, not sufficient: a pure "do the operands match?" rule would wave
`true + false` straight through. There is no reading of it, so the numeric-domain rule rejects
it. (This is one of the three cases the bootstrap answers with an internal error — E9001,
`Unsupported binop: Add on Bool`.)

<!-- test: bool-plus-bool -->
```maxon
function main() returns ExitCode
	let a = true
	let b = false
	let r = a + b
	return r
end 'main'
```
```maxoncstderr
error E3005: <fragment>:5:12: operator '+' is not defined for type 'bool'
```

### Unary `-` needs a number

Negating a bool negated its 1 payload and produced **-1** — which is still TRUE in a condition.

<!-- test: negate-bool -->
```maxon
function main() returns ExitCode
	let flag = true
	let r = -flag
	if r 'r'
		return 1
	end 'r'
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:10: Cannot negate bool
```

### A comparison is CLASS-STRICT

`4 < flag` compiled to `4 < 1` — a comparison against the bool's payload, which is not what the
source says and is always false.

<!-- test: int-less-than-bool -->
```maxon
function main() returns ExitCode
	let flag = true
	if 4 < flag 'c'
		return 1
	end 'c'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:4:7: type mismatch: 'cannot compare int with bool'
```

<!-- test: bool-equals-int -->
```maxon
function main() returns ExitCode
	let flag = true
	if flag == 4 'c'
		return 1
	end 'c'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:4:10: type mismatch: 'cannot compare bool with int'
```

### …but two bools compare fine

The guard against over-applying the rule: `a == b` is two operands of the SAME class, which is
exactly what the rule asks for. `false == false` is true, so this returns 1.

<!-- test: bool-equals-bool -->
```maxon
function main() returns ExitCode
	let a = true
	let b = false

	if a == b 'same'
		return 0
	end 'same'

	if b == false 'bothFalse'
		return 1
	end 'bothFalse'
	return 2
end 'main'
```
```exitcode
1
```

### A condition is a `bool`, not "anything nonzero"

<!-- test: int-as-if-condition -->
```maxon
function main() returns ExitCode
	let n = 4
	if n 'c'
		return 1
	end 'c'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:4:5: 'if' requires a bool condition, got 'int'
```

<!-- test: int-as-while-condition -->
```maxon
function main() returns ExitCode
	var n = 4
	while n 'loop'
		n = n - 1
	end 'loop'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:4:8: 'while' requires a bool condition, got 'int'
```

The diagnostic anchors on the CONDITION, not on the keyword — the message is about the
expression's type, and a condition spanning several lines should not point at the `if`.

<!-- test: int-expression-as-condition -->
```maxon
function main() returns ExitCode
	let a = 3
	let b = 4
	if a + b 'c'
		return 1
	end 'c'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:5:5: 'if' requires a bool condition, got 'int'
```

### A reassignment cannot change a binding's type — which is what keeps a merge phi honest

Each of the three cases below defeated EVERY other rule in this spec before the reassignment was
checked: the bool reaches the phi, the phi is stamped `int` from the binding's declaration, and
the operator, the condition or the call argument downstream sees an `int` and waves it through.
`laundered-bool-into-arithmetic` returned **1**; `laundered-int-into-condition` returned **42**
(C-style truthiness, restored); `laundered-bool-into-int-param` put a bool in an int parameter,
which is `implicit-type-conversion.md`'s `no-bool-to-int` defeated by adding one `if`.

<!-- test: assign-bool-to-int-var -->
```maxon
function main() returns ExitCode
	var x = 5
	x = true
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:4:2: cannot assign a value of type 'bool' to variable 'x', which holds 'int'
```

<!-- test: assign-int-to-bool-var -->
```maxon
function main() returns ExitCode
	var flag = true
	flag = 7
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:4:2: cannot assign a value of type 'int' to variable 'flag', which holds 'bool'
```

<!-- test: laundered-bool-into-arithmetic -->
```maxon
function main() returns ExitCode
	var x = 5
	if true 'b'
		x = false
	end 'b'
	return x + 1
end 'main'
```
```maxoncstderr
error E3005: <fragment>:5:3: cannot assign a value of type 'bool' to variable 'x', which holds 'int'
```

<!-- test: laundered-int-into-condition -->
```maxon
function main() returns ExitCode
	var flag = true
	var n = 0

	if flag 'b'
		flag = 7
	end 'b'

	if flag 'c'
		n = 42
	end 'c'
	return n
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:3: cannot assign a value of type 'int' to variable 'flag', which holds 'bool'
```

<!-- test: laundered-bool-through-loop-phi -->
```maxon
function main() returns ExitCode
	var x = 0
	var i = 0

	while i < 3 'loop'
		x = true
		i = i + 1
	end 'loop'

	return x + 10
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:3: cannot assign a value of type 'bool' to variable 'x', which holds 'int'
```

<!-- test: laundered-bool-into-int-param -->
```maxon
typealias Integer = int(i64.min to i64.max)

function takeInt(n Integer) returns Integer
	return n
end 'takeInt'

function main() returns ExitCode
	var x = 5
	if true 'b'
		x = false
	end 'b'
	return takeInt(x)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:3: cannot assign a value of type 'bool' to variable 'x', which holds 'int'
```

### …and a reassignment WITHIN the class is fine

The guard: reassigning a `var` is ordinary, and a `bool` var may take any bool while an `int`
var takes any int. Only a change of CLASS is refused. This returns 1.

<!-- test: reassign-within-class -->
```maxon
function main() returns ExitCode
	var flag = true
	var n = 5

	if n > 0 'pos'
		flag = false
		n = n * 2
	end 'pos'

	if flag 'stillTrue'
		return 99
	end 'stillTrue'

	if n == 10 'doubled'
		return 1
	end 'doubled'
	return 0
end 'main'
```
```exitcode
1
```

### A `return` value must agree with what the function promised

The argument-position bug in reverse: the bool's 1 payload would go back through the return
register as the integer 1.

<!-- test: return-bool-from-int-function -->
```maxon
typealias Integer = int(i64.min to i64.max)

function pick() returns Integer
	let flag = true
	return flag
end 'pick'

function main() returns ExitCode
	return pick()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:2: Cannot return 'bool' from function declared to return 'int'
```

<!-- test: return-int-from-bool-function -->
```maxon
function isBig() returns bool
	return 1
end 'isBig'

function main() returns ExitCode
	if isBig() 'c'
		return 1
	end 'c'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:2: Cannot return 'int' from function declared to return 'bool'
```

### `return 0` from `main` is NOT a mismatch

`ExitCode` is a ranged `int` alias, so an integer agrees with it — the rule partitions types
into CLASSES (bool vs number), and every alias, `ExitCode` included, is a number. If this
regressed, every program in the corpus would stop compiling.

<!-- test: int-returned-as-exitcode -->
```maxon
typealias Integer = int(i64.min to i64.max)

function double(n Integer) returns Integer
	return n * 2
end 'double'

function main() returns ExitCode
	return double(21)
end 'main'
```
```exitcode
42
```

### A container COLUMN is one more position, and a `bool` column holds `bool`s

A `Map`'s VALUE column is one machine word dropped by nothing, which is exactly what a `bool`
is — so `Map with (String, bool)` is admissible, and the gate
(`ProgramSignatures.slotTypeFitsOneWord`) admits it. But which types may BE a column and which
values may be WRITTEN to one are decided in two different files, and for one tick `boolean` was
in the first roster and in neither arm of the second: the write fell through to the integral
residual, `tagIsIntegral(boolean)` is FALSE **because a bool is not a number** — this spec's
whole subject — and the program was admitted at the `typealias` and refused at the first
`upsert` as *"this `Map`'s value is a bool — got a 'bool' value"*, a sentence that argues
against itself.

The three cases below are the pin, and they must be read together: the column WORKS, and the
discipline holds at it in both directions. Widening `slotTypeFitsOneWord` without an answering
arm in `TypeRules.columnValueTagMatches` breaks the first; collapsing the bool arm into the
integral fall-through to "fix" it breaks the other two.

<!-- test: bool-map-value-column -->
A `bool`-valued map stores, reads back and replaces both `true` and `false`. `count` proves the
two writes landed in two slots rather than one, and the `upsert` at the end proves a replaced
bool is the new one and not the old.
```maxon
typealias Flags = Map with (String, bool)

function main() returns ExitCode
	var m = Flags.create()
	m.upsert("on", value: true)
	m.upsert("off", value: false)
	let on = try m.get("on") otherwise false
	let off = try m.get("off") otherwise true
	m.upsert("on", value: false)
	let on2 = try m.get("on") otherwise true
	print("count={m.count()} on={on} off={off} on2={on2}")
	return 0
end 'main'
```
```stdout
count=2 on=true off=false on2=false
```

<!-- test: int-into-a-bool-map-value -->
An `int` is not admitted by a `bool` column. If this starts passing, the column has been folded
back into the integral fall-through and a `1` is being stored where a `bool` is read.

⭐ **THE SENTENCE MOVED WHEN `Map` STOPPED BEING SYNTHESIZED (W41), AND WHAT IT PINS DID NOT.** The
`E2015` here was the builtin map's own per-column gate. `Map` is `stdlib/Map.maxon` now, so
`upsert(key Key, value Value)` is an ordinary declared method and the refusal is the ordinary
argument-type check — `E3005`, naming the `value:` label. **The discipline this case exists for is
untouched**: `bool` and `int` are still distinct at a container column, and the case still goes red
the moment they are folded together. It is anchored on the CALL rather than on the argument, which
is where an argument-type mismatch has always been anchored.

⚠ **AND THE CALL'S ANCHOR IS THE METHOD NAME, NOT THE RECEIVER (W49b).** This block read `:6:2` — the
`m` — which is the column the Map-retirement branch emitted and which nothing else in the suite agrees
with. MEASURED against the runnable oracle on the same shape (`h.put(3, value: 1)` on a plain user
type, no `Map` and no stdlib involved): the C# bootstrap answers `:15:4`, the method name, and
`per-instance-typealias.md`'s `wrong-instance-error` already pins `:30:4` on a method call and passes.
The compiler is right and this golden was stale; it is the receiver-anchored spelling that was the
outlier.
```maxon
typealias Flags = Map with (String, bool)

function main() returns ExitCode
	var m = Flags.create()
	m.upsert("on", value: 1)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:4: argument type mismatch for 'value': expected 'bool', got 'int'
```

<!-- test: bool-into-an-int-map-value -->
And the converse: a `bool` is not admitted by an integral column, which is the same rule read
from the other side.
```maxon
typealias Byte = int(0 to 255)
typealias Counts = Map with (String, Byte)

function main() returns ExitCode
	var m = Counts.create()
	m.upsert("on", value: true)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:4: argument type mismatch for 'value': expected 'int', got 'bool'
```
