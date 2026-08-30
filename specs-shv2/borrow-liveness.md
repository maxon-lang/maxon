---
feature: borrow-liveness
status: selfhosted
keywords: [borrow, checker, E3070, NLL, liveness, for-in, mutation, safety]
category: memory
---
# Borrow Liveness (E3070) — the shapes `specs/borrow-checker.md` does not reach

## Documentation

`specs-shv2/borrow-checker.md` and `specs-shv2/array-realloc-dangling-ref.md` are ports of the
corpus and pin what the LANGUAGE promises. This file pins the shapes shv2's own borrow-liveness
mechanism reaches — every one of them found by probing it, and each one is either a defect it
closed or a guard against it over-rejecting.

**Why some of these DIVERGE from the runnable oracle, in the refusing direction.** `arr.get(i)`
in shv2 hands back the element pointer and takes no reference for it
(`Parser.emitArrayElementAccessor(owned: false)`); the oracle RETAINS, so a program that frees the
array under an outstanding borrow is merely wasteful there and is a **use-after-free here**. Where
that is so, the only two sound answers are retain-on-get or refuse, and P1.8 Slice A already ruled
the element a borrow — so shv2 refuses. Each such case says so, with the measurement the refusal
replaces.

⭐⭐ **THE `for … in` ELEMENT IS ONE OF THEM — ⚖ USER RULING, 2026-08-26 — AND IT SPENT ONE RELEASE NOT
BEING ONE, SO READ THE ROUND TRIP RATHER THAN EITHER HALF.** Four cases below refuse a `clear()`
reached from inside a `for … in` over the array being cleared. They refused it at P1.8 Slice A;
they ACCEPTED it from the day `stdlib/Array.maxon` was listed, because that made `for x in a` take
the CURSOR form, whose `ArrayIterator.current()` returns a `+1` through `coOwnBorrowedOpaque` — no
borrow left to dangle, so the refusal would have been a false one; and they refuse it again from
**EC3**, which stopped rewriting an `Array` source to its cursor and put the counter form, and with
it the borrowed element, back.

⚠ **THE MIDDLE VERDICT WAS NEVER A RE-RULING; IT WAS THE MECHANISM MOVING UNDER THE RULE.** The
paragraph above says the choice is retain-on-get *or* refuse, and that P1.8 Slice A ruled the
element a borrow. Listing the corpus array bought retain-on-get by accident, at two heap records
per loop entry and three calls per trip — about half of the shv2-emitted compiler's allocations,
which is what EC3 measured and removed. **The 2026-08-26 ruling settles it in the same direction
P1.8 did: the element is a BORROW.**

⚠⚠ **SO TWO OF THE FOUR ARE PROGRAMS THE RUNNABLE ORACLE COMPILES AND RUNS, AND shv2 REFUSES THEM
BY DECISION.** The bootstrap prints **44** for `receiver-method-inside-a-for-loop` and **53** for
`forin-over-module-storage`, because it retains on element read; shv2 does not retain, so the same
programs are use-after-frees here unless refused. **That is a documented divergence and not a bug on
either side** — it is this file's opening divergence, reached through the `for … in` door instead of
the `get` door. The other two the oracle cannot arbitrate in either spelling (see them).

⚠ **THE EVIDENCE ON BOTH SIDES IS KEPT, because each half measured a real program.** Under a BORROW
the body reads the free-poison byte `4557430888798830399` = `0x3F3F3F3F3F3F3F3F`, which is what these
four were opened on and what they refuse today. Under the cursor's `+1` the same programs ran: a
runtime-built 40-byte String (a literal would be a false negative — an immortal `.rdata` record
survives a free it never had) pushed into an array a callee `clear()`s, refilled so the freed slot is
REUSED, read back as **40** with exit 0.

⚠ **`arr.get(i)` NEVER MOVED IN EITHER DIRECTION** — it is a different door, which is why only the
four `for … in` cases changed verdict twice and every `get`-based refusal here stood throughout.

**Why one of them diverges in the ACCEPTING direction.** shv2 keys a borrow on the BINDING
(`Scope`-resolved, object identity), not on the variable's NAME, so a block that shadows a
borrowed array's name is correctly writable. The oracle is name-keyed and rejects it.

## Tests

<!-- test: rebind-drops-the-borrowed-record -->
### Rebinding the source frees what the borrow points at
`arr = <fresh>` drops the record `arr` held, so every element borrowed out of it is freed.
Measured **0xC0000005** before this rule; the oracle prints the string, because it retains on
`get` and therefore just forgets the borrow at a reassignment.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	let s = try arr.get(0) otherwise ""
	arr = StringArray.create()
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/rebind-drops-the-borrowed-record.test:7:2: cannot mutate 'arr' via '=' while it is borrowed by 's' (borrowed at line 6)
```

<!-- test: field-chain-source -->
### A borrow taken through a field chain is a borrow
`b.items.get(0)` borrows, and `b.items.clear()` frees it. The subject is the chain's BASE — the
same key `for it in b.items` locks — so the two spellings cannot disagree. Measured
**0xC0000005** before this rule; the oracle accepts it (it retains).
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation"]}
	end 'create'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	let s = try b.items.get(0) otherwise ""
	b.items.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/field-chain-source.test:15:10: cannot mutate 'b' via 'clear' while it is borrowed by 's' (borrowed at line 14)
```

<!-- test: self-field-alias-source -->
### A bare self-field alias is a borrow source
`items` inside a method of `Bag` names the field, and the borrow and the write reach the ONE alias
installed at method entry. Byte-identical to the oracle's diagnostic.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation"]}
	end 'create'

	function peek() returns ExitCode
		let s = try items.get(0) otherwise ""
		items.clear()
		print("[{s}]\n")
		return 0
	end 'peek'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	return b.peek()
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/self-field-alias-source.test:13:9: cannot mutate 'items' via 'clear' while it is borrowed by 's' (borrowed at line 12)
```

<!-- test: self-field-rebind -->
### Rebinding a self-field frees what the borrow points at
The rebind door one indirection out — `items = <fresh>` and `self.items = <fresh>` converge on the
one self-field store and both refuse.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation"]}
	end 'create'

	function peek() returns ExitCode
		let s = try items.get(0) otherwise ""
		items = StringArray.create()
		print("[{s}]\n")
		return 0
	end 'peek'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	return b.peek()
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/self-field-rebind.test:13:3: cannot mutate 'items' via '=' while it is borrowed by 's' (borrowed at line 12)
```

<!-- test: parameter-source -->
### A PARAMETER may be a borrow source
A parameter's container is writable (it is a borrowed reference to the caller's record), which is
exactly why a borrow out of it can dangle. Byte-identical to the oracle's diagnostic.
```maxon
typealias StringArray = Array with String

function look(arr StringArray) returns ExitCode
	let s = try arr.get(0) otherwise ""
	arr.clear()
	print("[{s}]\n")
	return 0
end 'look'

function main() returns ExitCode
	var a = ["hello world this is a long string for heap allocation"]
	return look(a)
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/parameter-source.test:6:6: cannot mutate 'arr' via 'clear' while it is borrowed by 's' (borrowed at line 5)
```

<!-- test: mutating-callee-argument -->
### Handing the source to a callee that writes it
The one write the parser cannot settle — whether `grow` mutates what it was handed depends on its
body — so it is decided against the whole-program parameter-mutation summary. Byte-identical to
the oracle's diagnostic, anchor included.
```maxon
typealias StringArray = Array with String

function grow(dest StringArray)
	dest.clear()
end 'grow'

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	let s = try arr.get(0) otherwise ""
	grow(arr)
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/mutating-callee-argument.test:11:2: cannot mutate 'arr' via 'grow' while it is borrowed by 's' (borrowed at line 10)
```

<!-- test: mutating-callee-labelled-argument -->
### The argument's SOURCE order is bridged to the parameter's DECLARATION order
`grow(1, dest: arr, other: spare)` writes parameter 2 while `arr` is source argument 1, so the
labelled slotting has to be inverted before the mutation mask can be read. Byte-identical to the
oracle's diagnostic.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

function grow(n Integer, other StringArray, dest StringArray) returns Integer
	dest.clear()
	return n + other.count()
end 'grow'

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	var spare = StringArray.create()
	let s = try arr.get(0) otherwise ""
	let k = grow(1, dest: arr, other: spare)
	print("[{s}] {k}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/mutating-callee-labelled-argument.test:14:10: cannot mutate 'arr' via 'grow' while it is borrowed by 's' (borrowed at line 13)
```

<!-- test: non-mutating-callee-argument -->
### A callee that only READS the source is not a conflict
The over-rejection guard for the case above: the summary says `peek` writes no parameter, so the
borrow survives the call.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

function peek(src StringArray) returns Integer
	return src.count()
end 'peek'

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	let s = try arr.get(0) otherwise ""
	let n = peek(arr)
	print("[{s}] {n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation] 1
```

<!-- test: shadowed-source-is-writable -->
### A block that SHADOWS the source's name writes a different array
shv2 keys a borrow on the BINDING, not on the name, so the inner `arr` is a different storage and
writing it cannot free the outer array's element. The oracle is name-keyed and rejects this
program; shv2 accepts it and returns the right answer.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	let s = try arr.get(0) otherwise ""
	if s.byteLength() > 0 'inner'
		var arr = StringArray.create()
		arr.push("a wholly different array that shares only the name")
		print("{arr.count()}\n")
	end 'inner'
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
[hello world this is a long string for heap allocation]
```

<!-- test: first-is-a-borrow-source -->
### `first()` borrows exactly as `get()` does
All three read-in-place accessors are one door, so none of them can be taught the rule separately.
Byte-identical to the oracle's diagnostic.
```maxon
function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	let s = try arr.first() otherwise ""
	arr.push("another long string for the heap allocation path here")
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/first-is-a-borrow-source.test:5:6: cannot mutate 'arr' via 'push' while it is borrowed by 's' (borrowed at line 4)
```

<!-- test: pop-is-not-a-borrow -->
### `pop()` MOVES the element out, so there is nothing to borrow
The exclusion is structural — it is the same `owned` flag that decides the runtime's move-out, not
a second list of method names.
```maxon
function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation", "second long string for the heap allocation path"]
	let s = try arr.pop() otherwise ""
	arr.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[second long string for the heap allocation path]
```

<!-- test: remove-is-not-a-borrow -->
### `remove()` moves out too
```maxon
function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation", "second long string for the heap allocation path"]
	let s = try arr.remove(0) otherwise ""
	arr.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation]
```

<!-- test: block-scoped-borrow-expires -->
### A borrow bound inside a block is dead once the block is left
The borrowing name's last use is inside the block, so the write after it is fine.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	var n = 0 as Integer
	if n == 0 'inner'
		let s = try arr.get(0) otherwise ""
		n = s.byteLength()
	end 'inner'
	arr.clear()
	print("{n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
53
```

<!-- test: mutation-in-otherwise-handler -->
### The source is mutable inside the `otherwise` handler
The array shape of `specs/borrow-checker.md:borrow-not-live-in-otherwise` (whose own `Map` form
waits for Phase 2). On the handler's path the get FAILED and `s` was never bound, so there is no
live borrow and the push must be allowed — which activation-at-the-BINDING gives structurally.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = StringArray.create()
	let s = try arr.get(0) otherwise 'empty'
		arr.push("a replacement long string for the heap allocation path")
		return 7
	end 'empty'
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
7
```

<!-- test: forin-element-borrow-via-callee -->
### A `for … in` element is a borrow, and a callee can free it
P1.8 Slice A's lock refuses every write that NAMES the iterated array; it structurally cannot
refuse one that hands the array to a callee. shv2 read the free-poison byte here — `0x3F`,
`4557430888798830399` — before this rule. The oracle accepts the program, because it retains the
element, so this is a deliberate divergence in the refusing direction: ⚖ **USER RULING, 2026-08-26**,
which upheld P1.8's borrow after EC3 put the counter form back.

⭐⭐ **THIS CASE HELD THE OPPOSITE VERDICT FOR ONE RELEASE, AND ITS NAME WENT WITH IT.** Between the
`stdlib/Array.maxon` listing and EC3 it read *"A `for … in` element is OWNED, so a callee that clears
the array cannot free it"*, under the name `forin-element-survives-a-callee-that-clears`, pinning
exit 0 / stdout `53`. That was correct **for the cursor form**, whose `ArrayIterator.current()` hands
back a `+1` (`coOwnBorrowedOpaque`): there was no borrow left to dangle, so the refusal would have
been a false one. EC3 stopped rewriting an `Array` source to its cursor, the borrowed element came
back, and the refusal came back with it — name included, by this file's own rule that a case may not
carry a noun asserting the opposite of its verdict.

⚠ **THE ORACLE CANNOT ARBITRATE THIS PARTICULAR PROGRAM IN EITHER SPELLING.** With `let b` it is
`E3019 … cannot pass immutable 'let' variable to function that mutates parameter 'dest'` — the
container-through-a-`let`-field divergence `Parser.paramMaskOfValue`'s header states as a RULING and
`forin-mutation-after-loop` below already pins — and with `var b` it is `E3077 … variable 'b' is
never reassigned; use 'let' instead`. The two shapes the oracle DOES compile are
`receiver-method-inside-a-for-loop` and `forin-over-module-storage` below, and those are where this
rule's divergence is visible rather than inferred.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation"]}
	end 'create'
end 'Bag'

function wipe(dest StringArray)
	dest.clear()
end 'wipe'

function main() returns ExitCode
	let b = Bag.create()
	var total = 0 as Integer
	for it in b.items 'scan'
		wipe(b.items)
		total = total + it.byteLength()
	end 'scan'
	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/forin-element-borrow-via-callee.test:21:3: cannot mutate 'b' via 'wipe' while it is borrowed by 'it' (borrowed at line 20)
```

<!-- test: forin-element-borrow-is-lexical -->
### The loop element's borrow runs to the loop's `end`, NOT to the variable's last use
`it` is never read again after the write, so ordinary last-use liveness would call the borrow dead
and let this through — but the ITERATION keeps reading the record on every later trip. The loop
element's liveness is the body's extent, which is the same lexical liveness the Slice A lock models.

⚠ Kept as a SEPARATE case from the one above rather than folded into it: the two differ only in
where `wipe` sits, and that difference is the whole content of the liveness question the pair asks.

⭐⭐ **AND THIS LEXICAL EXTENT IS THE FACT EC3's OWN CURE RESTS ON.** The 32 loops EC3 restructured in
shv2's source are `for … in` walks whose element was live across a mutating call for exactly this
reason; each became an index loop over `count()` + `get(i)`, whose borrow **is** ordinary last-use
liveness and therefore dies at the call it feeds. So this case pins the difference between the two
loop forms, not a curiosity: see `IR/Std/ElimTrivialBlockArgs.elimTrivialBlockArgs` for the note the
restructured sites point at.

⚠ It spent one release as `forin-element-outlives-a-clear-later-in-the-body` pinning exit 0 / stdout
`53` — correct for the cursor form's `+1` element and for nothing else. ⚖ Ruling of 2026-08-26. The
oracle cannot arbitrate this spelling either: same two spellings, same two refusals as above.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation"]}
	end 'create'
end 'Bag'

function wipe(dest StringArray)
	dest.clear()
end 'wipe'

function main() returns ExitCode
	let b = Bag.create()
	var total = 0 as Integer
	for it in b.items 'scan'
		total = total + it.byteLength()
		wipe(b.items)
	end 'scan'
	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/forin-element-borrow-is-lexical.test:22:3: cannot mutate 'b' via 'wipe' while it is borrowed by 'it' (borrowed at line 20)
```

<!-- test: forin-mutation-after-loop -->
### … and it ends THERE — a write after the loop is fine
The over-rejection guard for the lexical extent above. Without the `end` bound this program would
be refused, and it is perfectly safe.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation"]}
	end 'create'
end 'Bag'

function wipe(dest StringArray)
	dest.clear()
end 'wipe'

function main() returns ExitCode
	let b = Bag.create()
	var total = 0 as Integer
	for it in b.items 'scan'
		total = total + it.byteLength()
	end 'scan'
	wipe(b.items)
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
53
```

<!-- test: forin-discard-borrows-no-element -->
### `for _ in` reads no element into a name, so it borrows nothing
The discard binds nothing, so no borrow can outlive the read. The RECORD itself survives any
callee — a callee can clear a container but cannot rebind the caller's binding — and a write that
WOULD drop it is refused by the Slice A lock instead.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation"]}
	end 'create'
end 'Bag'

function wipe(dest StringArray)
	dest.clear()
end 'wipe'

function main() returns ExitCode
	let b = Bag.create()
	var total = 0 as Integer
	for _ in b.items 'scan'
		wipe(b.items)
		total = total + 1
	end 'scan'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: otherwise-return-preserves-the-borrow -->
### A diverging `otherwise` does not destroy the borrow
`try arr.get(0) otherwise return 1` still binds `s` on the success path, so the borrow is real and
the later `clear()` must be refused. The handler is parsed by re-entering the STATEMENT parser
mid-initializer, so a borrow that were tracked by statement position rather than by the VALUE it
produced would be wiped here — measured **0xC0000005** while it was.
```maxon
function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	let s = try arr.get(0) otherwise return 1
	arr.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/otherwise-return-preserves-the-borrow.test:5:6: cannot mutate 'arr' via 'clear' while it is borrowed by 's' (borrowed at line 4)
```

<!-- test: otherwise-block-preserves-the-borrow -->
### … and neither does a diverging `otherwise` BLOCK
The block form re-enters the statement parser through `parseBlockBody`, one level further down.
Same borrow, same refusal.
```maxon
function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	let s = try arr.get(0) otherwise 'empty'
		return 7
	end 'empty'
	arr.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/otherwise-block-preserves-the-borrow.test:7:6: cannot mutate 'arr' via 'clear' while it is borrowed by 's' (borrowed at line 4)
```

<!-- test: borrow-must-reach-the-binding -->
### A binding that does NOT hold the element borrows nothing
`n` is an integer computed FROM the element; the element itself reaches no name, so nothing can
dangle and the mutation is legal. A borrow attached to whatever binding came next would refuse this.
```maxon
typealias Integer = int(i64.min to i64.max)

function sizeOf(s String) returns Integer
	return s.byteLength()
end 'sizeOf'

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	let n = sizeOf(try arr.get(0) otherwise "")
	arr.clear()
	print("{n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
53
```

<!-- test: range-loop-counter-is-not-a-borrower -->
### A counted range's loop variable is the index, not an element
The bound expression may borrow, but the loop variable is the header phi — so the loop's LEXICAL
extent must not be handed to it, or every write inside the body would be refused.
```maxon
typealias Integer = int(i64.min to i64.max)

function sizeOf(s String) returns Integer
	return s.byteLength()
end 'sizeOf'

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	var total = 0 as Integer
	for i in 0 upto sizeOf(try arr.get(0) otherwise "") 'l'
		arr.clear()
		total = total + i
	end 'l'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1378
```

<!-- test: sibling-field-shares-the-subject -->
### A SIBLING field of the same base is the same subject — conservative, and it must be
The subject of a field chain is its BASE (`b`), the key P1.8 Slice A's iteration lock already uses,
and it has to be: a rebind `b = other` drops the record `b.items` points at, so a finer
`(base, field)` key would miss that use-after-free. The price is that writing `b.other` while
`b.items` is borrowed is refused too. The oracle accepts it — as it accepts `b.items.clear()`
itself, because it retains — so this is the same divergence one field over, not a new one.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray
	export var other as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation"], other: ["a second array entirely, sharing only its base binding"]}
	end 'create'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	let s = try b.items.get(0) otherwise ""
	b.other.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/sibling-field-shares-the-subject.test:16:10: cannot mutate 'b' via 'clear' while it is borrowed by 's' (borrowed at line 15)
```

<!-- test: ternary-merge-carries-the-borrow -->
### A borrow survives a TERNARY merge
A merge MINTS A NEW VALUE, and the borrow is keyed on the value the accessor produced — so the phi
has to inherit it or the binding holds an element nothing is tracking. Measured **0xC0000005**
while it did not.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	var n = 0 as Integer
	let s = (try arr.get(0) otherwise "") if n == 0 else "a fallback string long enough for the heap"
	arr.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/ternary-merge-carries-the-borrow.test:8:6: cannot mutate 'arr' via 'clear' while it is borrowed by 's' (borrowed at line 7)
```

<!-- test: match-gives-carries-the-borrow -->
### … and a `match … gives` merge, which is the same merge one construct over
Both go through the one merge finalizer, so they inherit the borrow at the same line of code — the
alternative is two rules for one join.
```maxon
enum Mode
	first
	other
end 'Mode'

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	let m = Mode.first
	let s = match m 'pick'
		first gives try arr.get(0) otherwise ""
		other gives "a fallback string long enough for the heap here"
	end 'pick'
	arr.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/match-gives-carries-the-borrow.test:14:6: cannot mutate 'arr' via 'clear' while it is borrowed by 's' (borrowed at line 11)
```

<!-- test: match-gives-merges-every-arms-borrow -->
### … and it carries EVERY arm's borrow, not just one
One binding, two subjects: each arm retargets its own pending borrow onto the same phi, so `s` really
does hold an element of both arrays and both writes must be refused. The claim loop is what makes
this work — stopping it at its first match would wave one of the two through as a use-after-free.
```maxon
enum Mode
	first
	second
end 'Mode'

function main() returns ExitCode
	var a = ["alpha string long enough for the heap allocation path"]
	var b = ["beta string long enough for the heap allocation path"]
	let m = Mode.first
	let s = match m 'pick'
		first gives try a.get(0) otherwise ""
		second gives try b.get(0) otherwise ""
	end 'pick'
	a.clear()
	b.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/match-gives-merges-every-arms-borrow.test:15:4: cannot mutate 'a' via 'clear' while it is borrowed by 's' (borrowed at line 12)
error E3070: specs/fragments/borrow-liveness/match-gives-merges-every-arms-borrow.test:16:4: cannot mutate 'b' via 'clear' while it is borrowed by 's' (borrowed at line 13)
```

<!-- test: propagating-try-carries-the-borrow -->
### A PROPAGATING `try` binds the accessor's own value
No merge, no phi, nothing to retarget — the borrow is the value the binding takes. The third of the
parser's three value merges is therefore the only `try` form that needs one.

⚠ `look` declares `throws ArrayError` — the error `arr.get` actually throws — and it did NOT until R4.4.
It declared `throws Oops`, an unrelated enum, and compiled only because shv2 did not yet know the array
family's error TYPE (`runtimeThrowsClause` answered `none`, so nothing checked the propagation). Registering
that type made the mismatch visible, and the runnable oracle reports it identically:
`E3059: try propagates 'ArrayError' but enclosing function throws 'Oops'`. The `Oops` enum stays declared but
unused so the line numbers this case's expected diagnostic names do not move.
```maxon
typealias StringArray = Array with String

enum Oops implements Error
	failed
end 'Oops'

function look(arr StringArray) returns ExitCode throws ArrayError
	let s = try arr.get(0)
	arr.clear()
	print("[{s}]\n")
	return 0
end 'look'

function main() returns ExitCode
	var a = ["hello world this is a long string for heap allocation"]
	return try look(a) otherwise 9
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/propagating-try-carries-the-borrow.test:10:6: cannot mutate 'arr' via 'clear' while it is borrowed by 's' (borrowed at line 9)
```

<!-- test: var-reassigned-from-an-element-copies-it -->
### Assigning an element into a `var` COPIES it, so there is no borrow to conflict with
A managed value stored into a `var` is promoted to an owned copy at the store, so `s` owns its own
record and clearing the array cannot reach it. The oracle refuses this program; shv2 accepts it and
returns the right answer, because here it genuinely does not borrow.
```maxon
function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	var s = "an initial string long enough for the heap allocation"
	s = try arr.get(0) otherwise ""
	arr.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation]
```

<!-- test: loop-carried-var-holds-a-copy -->
### A loop-header phi over a `var` carries a COPY, not a borrow
The fourth and last merge class the parser mints. It needs no borrow retarget and correctly has
none: every store into a managed `var` promotes to an owned copy first, so the value the phi joins
already owns its own record. The oracle refuses this program; shv2 accepts it and prints the right
string.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation", "second long string for the heap allocation path"]
	var s = "an initial string long enough for the heap allocation"
	var i = 0 as Integer
	while i < 2 'scan'
		s = try arr.get(i) otherwise ""
		i = i + 1
	end 'scan'
	arr.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[second long string for the heap allocation path]
```

<!-- test: receiver-method-writing-its-own-field -->
### A method that clears its OWN field frees what the caller borrowed
`b.wipe()` destroys the element `s` holds exactly as `wipe(b.items)` does — the array just arrives in
the RECEIVER column instead of an argument one. Answering it needs a second question of the callee:
*"does this body write the storage this parameter points at?"*, which is **yes** here while E3019's
*"does passing an immutable binding here make it an error?"* stays **no**. Measured before the split:
the oracle prints 44 (it retains), shv2 ran and printed `4557430888798830399` out of freed memory.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function wipe()
		items.clear()
	end 'wipe'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	b.wipe()
	print("{s.byteLength()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/receiver-method-writing-its-own-field.test:20:4: cannot mutate 'b' via 'wipe' while it is borrowed by 's' (borrowed at line 19)
```

<!-- test: receiver-method-explicit-self-spelling -->
### … and the `self.items.clear()` spelling is the same write
Both spellings converge on one door, so neither can be taught the rule separately.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function wipe()
		self.items.clear()
	end 'wipe'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	b.wipe()
	print("{s.byteLength()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/receiver-method-explicit-self-spelling.test:20:4: cannot mutate 'b' via 'wipe' while it is borrowed by 's' (borrowed at line 19)
```

<!-- test: receiver-method-rebinding-its-own-field -->
### … and REBINDING the field is a write of the receiver's storage too
`items = <fresh>` inside the method drops the record the caller borrowed out of. It is recorded at the
one self-field store, and only into the E3070 column — feeding E3019's from there turns
`self-keyword.md:self-with-params` red, which is that door's pinned guard.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function wipe()
		items = StringArray.create()
	end 'wipe'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	b.wipe()
	print("{s.byteLength()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/receiver-method-rebinding-its-own-field.test:20:4: cannot mutate 'b' via 'wipe' while it is borrowed by 's' (borrowed at line 19)
```

<!-- test: receiver-method-inside-a-for-loop -->
### … and inside a `for … in` over the field it clears
The loop element's borrow is lexical, so the call is refused wherever in the body it sits.

⭐⭐ **THE ONE OF THE FOUR THE ORACLE CAN ARBITRATE — AND IT ARBITRATES *AGAINST* THIS REFUSAL, WHICH
IS EXACTLY WHY THE CASE IS HERE.** The bootstrap compiles this program and prints **44**, because it
RETAINS on element read. shv2 does not retain, so the same program is a use-after-free here unless
refused. ⚖ **USER RULING, 2026-08-26: refuse.** This is therefore the sharpest instance of the
divergence this file opens with — where the two sound answers are retain-on-get *or* refuse, shv2
refuses — and it is **a documented divergence, not a bug on either side**.

⚠ It printed 44 here too for the one release the `stdlib/Array.maxon` listing had `for … in` taking
the cursor form, whose element is a `+1`. EC3 put the counter form and the borrow back.

⚠ **THE NAME NEVER MOVED, BECAUSE IT NAMES A SHAPE AND NOT A VERDICT** — unlike the two cases higher
up, which asserted "borrow" in their own ids and could not keep them across the round trip.

⚠ The sibling directly above (`receiver-method-rebinding-its-own-field`) borrows through
`arr.get(0)`, a different door, and has expected E3070 throughout — which is what made the pair
readable as "which door moved" while one of them was moving.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function wipe()
		items.clear()
	end 'wipe'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	var total = 0 as Integer
	for it in b.items 'scan'
		b.wipe()
		total = total + it.byteLength()
	end 'scan'
	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/receiver-method-inside-a-for-loop.test:22:5: cannot mutate 'b' via 'wipe' while it is borrowed by 'it' (borrowed at line 21)
```

<!-- test: receiver-method-writes-transitively -->
### A method that CALLS one that clears the field inherits the write
The second column rides the SAME least fixpoint over the SAME call-graph edges as the first, so
transitivity needs no second rule: `wipe()` gains the receiver's bit from `reset()`.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function reset()
		items.clear()
	end 'reset'

	function wipe()
		self.reset()
	end 'wipe'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	b.wipe()
	print("{s.byteLength()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/receiver-method-writes-transitively.test:24:4: cannot mutate 'b' via 'wipe' while it is borrowed by 's' (borrowed at line 23)
```

<!-- test: receiver-method-writing-its-own-field-through-a-corpus-member -->
### … and the write reaches the caller through a member the COMPILER does not serve
⛔⛔ **THE SAME WRITE, SPELLED THROUGH A CORPUS-DECLARED MEMBER, AND IT WAS A USE-AFTER-FREE ON THIS
TREE.** Every case above writes through `clear`, which `Parser.arraySurfaceMemberNames` still serves —
so the parser settles the write itself and stamps the enclosing method's `storageWrittenParamMask`
directly. `truncate` has NEVER been on that roster: it is `stdlib/Array.maxon:426`, an ordinary
declared function, and the enclosing method's bit then had to arrive through the call graph instead.
It did not. `SemanticCheck.collectCallParamEdges` records an edge only for an argument that IS one of
the caller's own parameters, and `items` is a FIELD of one — so `wipe` was summarised as writing
nothing and `b.wipe()` was waved through.

**MEASURED on the tree that shipped it, with the suite green over it: this program compiled clean and
printed `4557430888798830399`** — `__mm_free`'s `0x3F` poison read back as a length — where 44 is
correct and the oracle prints 44. It is the exact symptom `receiver-method-writing-its-own-field`
records from before the roster door existed, reappearing one member over.

⇒ The edge set the STORAGE column is closed over now carries a second edge kind: *"the caller's
parameter `p` owns the ARRAY this call's receiver denotes"*, recorded at the one ordinary-call
receiver door (`Parser.prependReceiverArg`). It is storage-only — feeding it into E3019's column
would refuse a `let` receiver for a method writing its own field, which is the ruling
`self-keyword.md:self-with-params` pins.

⚠ **THIS IS THE RETIREMENT CHAIN'S OWN HAZARD, AND IT IS WHY THE CASE IS WRITTEN OVER `truncate`
RATHER THAN OVER A NAME THAT JUST LEFT.** Every member struck from the roster moves from the door
that settles the write to the door that had to infer it, so the hole was opened by `insert` (ARR1) and
`reserve` (ARR2) and would have been re-opened by `clear` (ARR4). Pinning it on a member that was
NEVER on the roster is what keeps it pinned after the roster empties.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function wipe()
		items.truncate(0)
	end 'wipe'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	b.wipe()
	print("{s.byteLength()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/receiver-method-writing-its-own-field-through-a-corpus-member.test:20:4: cannot mutate 'b' via 'wipe' while it is borrowed by 's' (borrowed at line 19)
```

<!-- test: a-free-callee-writing-a-field-of-its-parameter -->
### … and the same hole through a FREE callee, where the field's base is an ordinary parameter
The receiver of the corpus call is `bag.items` — a field of parameter 0 rather than a field of `self`
— so the edge is recorded off the same `subjectStorageMask` derivation and not off a second one. It
matters because the two spellings reach `prependReceiverArg` by different receiver paths and a rule
taught to one of them is exactly the half nobody re-runs.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'
end 'Bag'

function wipe(bag Bag)
	bag.items.truncate(0)
end 'wipe'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	wipe(b)
	print("{s.byteLength()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/a-free-callee-writing-a-field-of-its-parameter.test:20:2: cannot mutate 'b' via 'wipe' while it is borrowed by 's' (borrowed at line 19)
```

<!-- test: a-corpus-served-read-only-member-is-not-a-conflict -->
### The over-rejection guard for the two above: a corpus member that only READS is still callable
The new edge is recorded at every ordinary member call whose receiver's storage reaches a parameter,
WITHOUT asking whether the callee writes — that answer belongs to the fixpoint, which has not run yet.
So the guard is the fixpoint's own filter: `Array.capacity` writes nothing, no bit travels the edge,
and `b.room()` stays legal while `s` is live. Recorded on a member that is corpus-served for the same
reason the two above are — `capacity` left the roster at X-array-retire — because a guard written over
a rostered member would not exercise the edge at all.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function room() returns Integer
		return items.capacity()
	end 'room'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	let n = b.room()
	print("{s.byteLength()} {n > 0}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
44 true
```

<!-- test: receiver-method-that-writes-nothing -->
### A method that does NOT write the field stays callable while the borrow is live
The over-rejection guard for the five above, and the one this fix is one step away from breaking: a
receiver is now an argument the conflict check sees, so a rule that blamed the receiver for merely
BEING one would refuse every method call on `b`. Only a callee the summary says writes the storage
counts.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function size() returns Integer
		return items.count()
	end 'size'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	let n = b.size()
	print("{s.byteLength()} {n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
44 1
```

<!-- test: let-receiver-still-takes-mutating-methods -->
### E3019's answer is UNCHANGED — a `let` receiver still takes a method that writes its own fields
The other half of the split, and the one a merged mask would have destroyed: shv2 rules that a `let`
on a struct binding does not reach inside the type's own methods (`self-keyword.md:self-with-params`
pins the field-store door, `parameter-mutation:let-struct-with-array-field-to-mutating-method-ok` the
container-method one). Both writes here are refused for E3070 only when a borrow is live — with none
outstanding, the program compiles and runs.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray
	export var total as Integer

	static function create() returns Self
		return Self{items: StringArray.create(), total: 0}
	end 'create'

	function wipe()
		items.clear()
	end 'wipe'

	function add(v Integer)
		total = total + v
	end 'add'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	b.add(42)
	b.wipe()
	print("{b.total} {b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42 0
```

<!-- test: sibling-call-on-the-enclosing-self -->
### A sibling call INSIDE the type reaches the same field, and must be refused there too
`reset()` clears `items` while `s` borrows an element of it — the same use-after-free as `b.wipe()`,
one level in. It needs its own answer because the receiver here is `self`, which stands for the WHOLE
receiver, while the borrow was recorded against the FIELD's alias: a single site keyed on `self` would
match nothing. Measured before this door: shv2 printed `4557430888798830399`, the oracle 44.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function reset()
		items.clear()
	end 'reset'

	function bad() returns Integer
		let s = try items.get(0) otherwise ""
		reset()
		return s.byteLength()
	end 'bad'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	print("{b.bad()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/sibling-call-on-the-enclosing-self.test:18:3: cannot mutate 'items' via 'reset' while it is borrowed by 's' (borrowed at line 17)
```

<!-- test: sibling-call-explicit-self-spelling -->
### … and `self.reset()` is the same call
Both spellings resolve the receiver to the same value, so one predicate answers for both.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function reset()
		items.clear()
	end 'reset'

	function bad() returns Integer
		let s = try items.get(0) otherwise ""
		self.reset()
		return s.byteLength()
	end 'bad'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	print("{b.bad()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/sibling-call-explicit-self-spelling.test:18:8: cannot mutate 'items' via 'reset' while it is borrowed by 's' (borrowed at line 17)
```

<!-- test: sibling-call-that-writes-nothing -->
### A read-only sibling call stays legal while the borrow is live
The over-rejection guard for the two above. A `self` receiver stands for every field of the enclosing
receiver, so it records a site per borrowed field — and every one of them is still filtered by the
whole-program summary, which says `size()` writes nothing.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function size() returns Integer
		return items.count()
	end 'size'

	function ok() returns Integer
		let s = try items.get(0) otherwise ""
		let n = self.size()
		return s.byteLength() + n
	end 'ok'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	print("{b.ok()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
45
```

<!-- test: method-writing-a-non-array-field-is-not-a-conflict -->
### A method that writes a NON-ARRAY field of the receiver is not a conflict
E3070 tracks an array element and nothing else, so only an ARRAY write can free one — the same line
the `String` and `Set` receiver doors already draw. Ungated, `total = total + v` marked the whole
receiver written and this legal program was refused; the oracle compiles and runs it.

⚠ The gate needs the TYPE TAG and not just the name: a `TypeNameId` and a `GenericInstanceId` share a
numeric space, so asking "is this an Array instance?" of a plain alias answered TRUE by coincidence,
which is exactly how the false rejection arrived.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray
	export var total as Integer

	static function create() returns Self
		return Self{items: StringArray.create(), total: 0}
	end 'create'

	function add(v Integer)
		total = total + v
	end 'add'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	b.add(42)
	print("{s.byteLength()} {b.total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
44 42
```

<!-- test: module-storage-source -->
### A top-level `var` is a borrow source
Module storage differs from a local in WHERE the record is anchored and in nothing a borrow can see.
The subject was `Scope`-only until this case: `g.clear()` compiled clean and faulted with
**0xC0000005**, on a program the oracle refuses with this exact diagnostic.
```maxon
var g = ["hello world this is a long string for heap allocation"]

function main() returns ExitCode
	let s = try g.get(0) otherwise ""
	g.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/module-storage-source.test:6:4: cannot mutate 'g' via 'clear' while it is borrowed by 's' (borrowed at line 5)
```

<!-- test: module-storage-rebind -->
### … and rebinding one frees what the borrow points at
The global store DECREFS the record the slot held (`emitCheckedGlobalStore`), so it is the local
rebind door one anchoring out. Measured **0xC0000005** before it existed.
```maxon
typealias StringArray = Array with String

var g = ["hello world this is a long string for heap allocation"]

function main() returns ExitCode
	let s = try g.get(0) otherwise ""
	g = StringArray.create()
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/module-storage-rebind.test:8:2: cannot mutate 'g' via '=' while it is borrowed by 's' (borrowed at line 7)
```

<!-- test: module-storage-to-a-mutating-callee -->
### … and handing one to a callee that writes it
The call-argument door reaches module storage through the same subject derivation as a local. The
oracle ACCEPTS this program — its borrow check is over var slots, which a global is not, and it
retains anyway — so this is the refusing-direction divergence one storage class over.
```maxon
typealias StringArray = Array with String

var g = ["hello world this is a long string for heap allocation"]

function grow(dest StringArray)
	dest.clear()
end 'grow'

function main() returns ExitCode
	let s = try g.get(0) otherwise ""
	grow(g)
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/module-storage-to-a-mutating-callee.test:12:2: cannot mutate 'g' via 'grow' while it is borrowed by 's' (borrowed at line 11)
```

<!-- test: forin-over-module-storage -->
### … and a `for … in` over one borrows its element lexically
The loop element's borrow reaches module storage too. Measured before this rule: the body read the
free-poison byte and printed `4557430888798830399` — `0x3F3F3F3F3F3F3F3F`, `__mm_free`'s always-on
poison. That number is what makes this case EVIDENCE rather than an assertion: the element's record
is genuinely freed by the `clear()`, and the poison is genuinely readable at that address.

⭐⭐ **THE SECOND CASE THE ORACLE ARBITRATES, AND IT ARBITRATES AGAINST THIS REFUSAL TOO.** The
bootstrap compiles this program and prints **53**, because it retains on element read; shv2 refuses
it by ⚖ **USER RULING, 2026-08-26** — a documented divergence, not a bug on either side. It printed
53 here as well for the one release `for … in` took the cursor form; EC3 restored the counter form
and the borrow.

⚠ **WITHOUT THIS RULE THE STORAGE CLASS WOULD DECIDE THE PROGRAM, WHICH IS WHY MODULE STORAGE IS IN
THE SUBJECT SPACE AT ALL.** P1.8 Slice A's lock refuses a write NAMING the iterated array and does
not reach module storage, so `g.clear()` inside `for it in g` passes the LOCK — while the same two
lines over a LOCAL array are `E3019 … cannot pass 'arr' to function that mutates parameter 'self'`
from the lock alone. The E3070 borrow is what makes the two spellings agree; the lock's reach is a
different mechanism and the two must not be read as one.
```maxon
typealias Integer = int(i64.min to i64.max)

var g = ["hello world this is a long string for heap allocation"]

function main() returns ExitCode
	var total = 0 as Integer
	for it in g 'scan'
		g.clear()
		total = total + it.byteLength()
	end 'scan'
	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/forin-over-module-storage.test:9:5: cannot mutate 'g' via 'clear' while it is borrowed by 'it' (borrowed at line 8)
```

<!-- test: module-storage-borrow-expires -->
### The over-rejection guard for the four above
A read-only callee does not end the borrow, and once the borrowing name's last use is past, the
global is writable again — the same NLL rule a local subject obeys, which is the point of admitting
module storage to the SUBJECT space rather than to a rule of its own.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

var g = ["hello world this is a long string for heap allocation"]

function peek(src StringArray) returns Integer
	return src.count()
end 'peek'

function main() returns ExitCode
	let s = try g.get(0) otherwise ""
	let n = peek(g)
	print("[{s}] {n}\n")
	g.clear()
	print("{g.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation] 1
0
```

<!-- test: method-writing-a-string-or-set-field-is-not-a-conflict -->
### A method that writes a `String` or a `Set` field of the receiver is not a conflict
The over-rejection guard for the two rows of `IrFunction`'s mask table that run the OTHER way: a
`String` or `Set` receiver write sets E3019's mask and deliberately NOT the storage column, because
neither can free an ARRAY element and an array element is the only borrow E3070 tracks. Recording
them would refuse both calls here, on a program that is perfectly safe.

⚠ E3019's own answer about those two receivers is unchanged and must stay so — `tagIt(msg)` on a
`let` String argument is still refused. The two masks are asked of the same write and answer
differently; this case pins the E3070 half.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String
typealias IntSet = Set with Integer

type Bag
	export var items as StringArray
	export var name as String
	export var seen as IntSet

	static function create() returns Self
		return Self{items: StringArray.create(), name: "tag", seen: IntSet.create()}
	end 'create'

	function mark()
		name.append("!")
	end 'mark'

	function note(v Integer)
		seen.insert(v)
	end 'note'

	function seenCount() returns Integer
		return seen.count()
	end 'seenCount'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	b.mark()
	b.note(7)
	print("{s.byteLength()} {b.name} {b.seenCount()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
44 tag! 1
```

<!-- test: a-field-store-in-a-callee-freeing-its-parameters-array -->
### A callee that STORES an array field of its parameter frees the borrowed element
The direct-store twin of `a-free-callee-writing-a-field-of-its-parameter` above, and it exists
because the two reach the storage by different doors: that one calls a mutating member ON the field
(`bag.items.truncate(0)`, `Parser.noteReceiverWrite`), this one REPLACES the field
(`b.items = <fresh>`, `Parser.parseFieldAssignment`), whose `emitFieldWrite` decrefs the record the
field held and frees every element some other name still borrows.

⚠ **This door had nothing to guard until PBR-1** — a field store through a PARAMETER was E2013, so no
write here could reach a caller's record. Measured on the permission change with the seeds absent:
this program compiled clean and faulted **0xC0000005**, where the oracle reports the conflict.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'
end 'Bag'

function wipe(b Bag)
	b.items = StringArray.create()
end 'wipe'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	wipe(b)
	print("{s.byteLength()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/a-field-store-in-a-callee-freeing-its-parameters-array.test:20:2: cannot mutate 'b' via 'wipe' while it is borrowed by 's' (borrowed at line 19)
```

<!-- test: a-scalar-field-store-is-not-a-conflict -->
### The over-rejection guard for the case above: storing a SCALAR field frees nothing
The store door's twin of `method-writing-a-non-array-field-is-not-a-conflict`, and it needs its own
case because the two doors key their subject DIFFERENTLY. A self-field store's subject is the FIELD
(`iterationSubjectNameAt` names it), so a write to one field cannot collide with a borrow out of
another; a `b.n = 5` store's subject is the chain BASE, `b`, which every field of `b` collapses onto.
Ungated, the write conflicted with every live borrow rooted at `b` — including, as here, one it
cannot possibly free — and this legal program was refused *"cannot mutate 'b' via '='"* where the
oracle compiles it and prints `hello`.

⚠ The gate is MANAGED-ness and deliberately not array-ness, which is the narrower gate the mask
beside it uses: `b.inner = other` frees a nested struct and cascades to ITS arrays, and a borrow taken
through `b.inner.items.get(0)` carries the same subject `b` — so array-gating the note would open a
real use-after-free while closing this false rejection.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray
	export var n as Integer

	static function create() returns Self
		return Self{items: StringArray.create(), n: 0}
	end 'create'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("hello")
	let s = try b.items.get(0) otherwise ""
	b.n = 5
	print("{s} {b.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello 5
```

<!-- test: error.a-borrow-taken-out-of-a-borrow-holds-the-root -->
### A borrow of a borrow holds the ROOT storage, not just the value it was read from
`c.current()` reads an element out of the CURSOR, and the cursor is itself a standing borrow of
`xs`. Keyed only on `c`, the element's borrow expires with `c`'s own last use while the element it
names still points into `xs` — measured **0xC0000005** on exactly this program, which
`dispatchArrayMethod`'s `createCursor` arm predicted in as many words and left to this rung. A
borrow therefore composes: minting one on storage that is ITSELF borrowed mints one on its base
too, up to the root.
```maxon
function main() returns ExitCode
	var xs = ["hello world this is a long string for heap allocation"]
	let c = try xs.managed.createCursor() otherwise return 1
	let s = c.current()
	xs.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/error.a-borrow-taken-out-of-a-borrow-holds-the-root.test:6:5: cannot mutate 'xs' via 'clear' while it is borrowed by 's' (borrowed at line 5)
```

<!-- test: a-composed-borrow-expires-at-its-own-last-use -->
### A composed borrow is still NLL — it expires with the borrower, not with the chain
The same chain with the element read BEFORE the write is safe and must stay compilable: composing
the borrow up to the root may not turn the root into a lexical lock. This is the over-rejection
guard for the case above.
```maxon
function main() returns ExitCode
	var xs = ["hello world this is a long string for heap allocation"]
	let c = try xs.managed.createCursor() otherwise return 1
	let s = c.current()
	print("[{s}]\n")
	xs.clear()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation]
```

<!-- test: error.a-try-merge-keeps-every-link-of-a-composed-chain -->
### A `try … otherwise` merge retargets EVERY link of a composed borrow, not just one
`try p.items.get(0) otherwise ""` binds the merge PHI, not the accessor's result, so each link the
composition minted has to move onto that phi. Retargeting only the first left the OTHER link keyed
on a value no binding claims — and which link survived depended on push order alone. Here the
surviving question is the INTERMEDIATE one: `p` is itself an element borrowed out of `arr`, and
`p.items.clear()` frees what `s` names.
```maxon
typealias StringArray = Array with String

type Holder
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["alpha string long enough for heap allocation"]}
	end 'create'
end 'Holder'

typealias HolderArray = Array with Holder

function main() returns ExitCode
	var arr = HolderArray.create()
	arr.push(Holder.create())
	let p = try arr.get(0) otherwise Holder.create()
	let s = try p.items.get(0) otherwise ""
	p.items.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/error.a-try-merge-keeps-every-link-of-a-composed-chain.test:19:10: cannot mutate 'p' via 'clear' while it is borrowed by 's' (borrowed at line 18)
```

<!-- test: a-callee-clearing-a-managed-list-frees-a-node-handle -->
### A callee that clears a `__ManagedList` does NOT free the node a handle still names

⚖ **THIS CASE PINNED E3070 UNTIL `W138`, AND THE ANSWER MOVED BECAUSE THE MEMORY MODEL DID — SO IT IS
KEPT, RUNNING, RATHER THAN DELETED.** The name is left as it was written: it is the claim the case was
built on, and a reader who greps for it must land on the record of it being withdrawn rather than on
nothing at all.

**What it pinned.** E3070 has **two halves** — the same-body site and the cross-function
`storageWrittenParamMask` — and until W-BORROW they were recorded at two doors off two different flags.
`dispatchManagedListMethod` was the one surface whose two answers DIFFERED
(`managedListMethodFreesANode` was narrower than `managedListMethodMutatesReceiver`), so it got E3019's
answer for E3070 as well and set no storage bit at all. ⚠ **MEASURED on `df0fbfd3bf`: this program
compiled clean and ran to `0xC0000005`**, and the bootstrap oracle refused it. That measurement was
correct, and under the one-owner node model so was the refusal: `clear` FREED every node, so a handle
outliving the call named freed memory.

⚖ **WHAT CHANGED: USER RULING 2026-08-17 (`W138`, option (iii)) — NODES ARE REFCOUNTED AND A HANDLE IS A
SECOND OWNER.** `clear` now drops the CHAIN's reference and nothing else; a node a handle still holds
walks out of the walk alive, still carrying its element, and dies with its last owner. **The program is
memory-safe**, so the E3070 was an over-rejection of a legal program and the predicate the cure read
(`managedListMethodFreesANode`) is deleted — `dispatchManagedListMethod` now calls
`noteBorrowSubjectWrite` not at all.

⭐ **AND THE PROGRAM IS EXACTLY THE BOUNDARY WORTH PINNING, WHICH IS WHY IT BECOMES A RUNNING CASE.** It
is the shape the ruling was taken ON — `managed-list-node-handle-lifetime.md:a-returned-handle-survives-
a-clear` is its twin with the mint in another function, and calls itself *"THE CASE THE SECOND RULING WAS
TAKEN FOR"*. Under the SUPERSEDED design (a handle retained its LIST, nodes unrefcounted) this printed
`0x3F3F3F3F3F3F3F3F` — `__mm_free`'s poison — at exit 0. It now prints the string.

⚠ **W-BORROW's ARCHITECTURE IS UNTOUCHED BY THIS, AND IS WHAT MAKES THE WITHDRAWAL EXPRESSIBLE.** Both
halves of E3070 still come off one flag at one door; this surface's flag is simply now empty where its
E3019 flag is not, which is the same "two answers differ" point in its strongest form. Every OTHER case
in this file is unaffected — an ELEMENT borrow is not a node handle, and the `List`/`Array`/nested-struct
store-door refusals below all stay red. MEASURED at this merge: `--filter=borrow-liveness` moved this
case and no other.
```maxon
typealias StringChain = __ManagedList with String

function wipe(chain StringChain)
	chain.clear()
end 'wipe'

function main() returns ExitCode
	var chain = StringChain.create()
	let node = chain.insertLast("alpha string long enough for heap allocation")
	wipe(chain)
	print("[{node.value()}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha string long enough for heap allocation]
```

<!-- test: a-callee-inserting-into-a-managed-list-keeps-every-handle -->
### An INSERTION in a callee disturbs no handle the caller holds
⚠ **THIS CASE'S ORIGINAL RATIONALE WAS WITHDRAWN BY THE `W138` RULING, AND THE CASE OUTLIVED IT.** It
was written as the *width guard* for the case above — the argument being that a chain's nodes are
individually allocated and never move, so an insertion rewrites two link words and dangles nothing,
where *"only `remove` and `clear` free a node"*. **That last clause is no longer true of anything**:
nodes are refcounted, a handle is a second owner, and neither `remove` nor `clear` frees a node a
handle still names — which is why the case above is now a running program rather than a refusal.

So it guards something narrower and still worth pinning: that a **cross-function** insertion leaves
every outstanding handle readable, which is the normal way a program builds a list it holds handles
into (`/specs/managed-list.md:core.insert-first-multiple` is its same-body twin). What made it a
*width* guard is gone; what makes it a *composition* case — a callee mutating a chain the caller holds
handles into — is not.

⚠ The exit code is pinned, not just the output: a node read back out of freed memory is a wrong answer
this file has measured before, and a stdout-only case never checks that the run succeeded.
```maxon
typealias StringChain = __ManagedList with String

function grow(chain StringChain)
	_ = chain.insertLast("beta string long enough for heap allocation")
end 'grow'

function main() returns ExitCode
	var chain = StringChain.create()
	let node = chain.insertLast("alpha string long enough for heap allocation")
	grow(chain)
	print("[{node.value()}] {chain.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha string long enough for heap allocation] 2
```

<!-- test: a-method-rebinding-a-list-field-frees-the-borrowed-element -->
### A method that REBINDS a `List` field no longer frees the element a caller holds
⚖ **RULED 2026-08-18 (W160). THIS CASE AND ITS TWIN BELOW PINNED `E3070`, AND `W153` MOVED WHAT THEY ARE
ABOUT — NOT WHETHER THE GATE WORKS.** `emitCheckedSelfFieldStore`'s E3070 seed was gated on
`typeIsArrayInstance`, under the sentence *"an array element is the only borrow E3070 tracks"*, and a
`List`-typed struct field was waved through as "not an array": **measured on `df0fbfd3bf`, this program
compiled clean and ran to `0xC0000005`.** The gate became `typeOwnsBorrowableStorage`, which asks what the
store can FREE rather than what the field is named after, and that gate is UNCHANGED and still name-agnostic
— `a-field-store-in-a-callee-freeing-its-parameters-array` and
`a-method-rebinding-a-nested-struct-field-frees-the-array-inside-it` are the cases that hold it.

⭐ **WHAT MOVED IS THE BORROW, WHICH IS NOW NEVER MINTED.** Retiring the synthesized `List` made
`first()` corpus-served, so it discharges a real `+1` (`coOwnBorrowedOpaque`) and `s` is an OWNED `String`.
There is no borrow for the store to conflict with, the use-after-free is structurally absent rather than
merely undetected, and the program prints the string the oracle prints. **Re-measured at `W153`: exit 0,
no leak, correct output.** The oracle still refuses — that is the `arr.last()` divergence
`BorrowCheck.maxon` documents, stated for this family in its header.
```maxon
typealias StringList = List with String

type Bag
	export var items as StringList

	export static function create() returns Self
		return Self { items: StringList.create() }
	end 'create'

	export function reset()
		items = StringList.create()
	end 'reset'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.append("alpha string long enough for heap allocation")
	let s = try b.items.first() otherwise "none"
	b.reset()
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha string long enough for heap allocation]
```

<!-- test: a-field-store-in-a-callee-freeing-its-parameters-list -->
### … and the same store one indirection out, through the field-chain door
The `List` twin of `a-field-store-in-a-callee-freeing-its-parameters-array` above, and it needs its own
case because it is a DIFFERENT door: that store is `items = <fresh>` inside a method
(`emitCheckedSelfFieldStore`), this one is `b.items = <fresh>` through a parameter
(`parseFieldAssignment`). Both carried the same `typeIsArrayInstance` gate and both were open; neither
was reachable from the other's fix.

⚠ **MEASURED on `df0fbfd3bf`: compiled clean, ran to `0xC0000005`.** It takes the accepting answer above
for the same reason and under the same W160 ruling — the `Array` twin directly above is the case that still
holds this door's gate red.
```maxon
typealias StringList = List with String

type Bag
	export var items as StringList

	export static function create() returns Self
		return Self { items: StringList.create() }
	end 'create'
end 'Bag'

function wipe(b Bag)
	b.items = StringList.create()
end 'wipe'

function main() returns ExitCode
	var b = Bag.create()
	b.items.append("alpha string long enough for heap allocation")
	let s = try b.items.first() otherwise "none"
	wipe(b)
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha string long enough for heap allocation]
```

<!-- test: a-method-rebinding-a-nested-struct-field-frees-the-array-inside-it -->
### A store does not free an ELEMENT — it drops the RECORD, and the drop cascades
The `Array`-keyed gate was wrong a second way, and this shape has nothing to do with `List`: the field
here is a plain STRUCT. Dropping `inner` releases the `Array` that struct owns, freeing the element a
caller borrowed through `b.inner.items.get(0)` — and that borrow's subject is the chain base `b`,
which is exactly what the call to `b.reset()` is checked against. `a-scalar-field-store-is-not-a-conflict`
records this same cascade as the reason the SAME-BODY seed was already gated on managed-ness rather
than array-ness; the cross-function seed beside it was not, and the two halves of one rule disagreeing
is what this case pins shut.

⚠ **MEASURED on `df0fbfd3bf`: compiled clean, ran to `0xC0000005`.**
```maxon
typealias StringArray = Array with String

type Inner
	export var items as StringArray

	export static function create() returns Self
		return Self { items: StringArray.create() }
	end 'create'
end 'Inner'

type Bag
	export var inner as Inner

	export static function create() returns Self
		return Self { inner: Inner.create() }
	end 'create'

	export function reset()
		inner = Inner.create()
	end 'reset'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.inner.items.push("alpha string long enough for heap allocation")
	let s = try b.inner.items.get(0) otherwise "none"
	b.reset()
	print("[{s}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/borrow-liveness/a-method-rebinding-a-nested-struct-field-frees-the-array-inside-it.test:28:4: cannot mutate 'b' via 'reset' while it is borrowed by 's' (borrowed at line 27)
```

<!-- test: a-string-field-store-is-not-a-conflict -->
### The over-rejection guard for the three above: storing a `String` field frees no container
The store doors' gate is *"can dropping this record free storage a tracked borrow points into"*, and
the plausible spelling of that — bare managed-ness — is a **measured false rejection**, which is why
`typeOwnsBorrowableStorage` carves out the two managed types that are not aggregates. Every borrow
E3070 tracks is a reference INTO A CONTAINER (an element, a chain node, a cursor); a `String` owns a
byte buffer and hands out no such reference, so replacing one can invalidate nothing.

⚠ It is the same fact `method-writing-a-string-or-set-field-is-not-a-conflict` already pins for the
METHOD door — one fact may not have two answers depending on which door asks. Written with a `String`
store gated at bare managed-ness, `b.retag()` was refused *"cannot mutate 'b' via 'retag'"* on a
program the runnable oracle compiles and runs (`44 another tag entirely`). Both store doors are
exercised: `retag` is the self-field spelling, `rename` the field-chain one through a parameter.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray
	export var name as String

	export static function create() returns Self
		return Self { items: StringArray.create(), name: "tag" }
	end 'create'

	export function retag()
		name = "another tag entirely"
	end 'retag'
end 'Bag'

function rename(b Bag)
	b.name = "a third tag entirely"
end 'rename'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	b.retag()
	rename(b)
	print("{s.byteLength()} {b.name}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
44 a third tag entirely
```

<!-- test: a-managed-field-handed-to-the-method-that-reassigns-it -->
### A borrowed argument is HELD across the call — the receiver and one of its fields, to one method
E3070 refuses a write the parser can PROVE conflicts with a NAMED live borrow. A borrow that no name
holds has no borrower to blame and no diagnostic to raise — and it was handed to the callee with no
reference at all. `Parser.anchorBorrowedArguments` closes that: when a call is given both a value read
out of storage and the storage itself, the caller holds a reference for the length of the statement.

⛔⛔ **THREE MEASURED SYMPTOMS OF ONE CORRUPTION, ALL FROM PROGRAMS THAT COMPILED CLEAN.** shv2 gave
`panic: Range check failed` on the `byteLength()` spelling and exit **82 `slabOsAllocFailed`** on the
interpolating one; on `f(g.s)` reaching the field through a module `var` it gave **0xC0000005**.

⚠⚠ **AND THE BOOTSTRAP ORACLE IS WRONG HERE TOO, WHICH IS WHY THIS IS A RULE shv2 MAKES RATHER THAN A
DIVERGENCE IT REPAIRS.** On this very program spelled with `"[{other}]"` in place of the length, the
bootstrap prints `[[[[[[[[[[[]` where the field held `xxxxxxxxxx` — exit 0, silent, the freed buffer
read back after the interpolation reused it. Its `byteLength()` spelling survives only because the
length word at `@8` outlives the free, which is exactly how *"runs correctly on the oracle"* came to
be written down about one spelling of a broken program.

⚠ **THE FIELD IS FILLED AT RUN TIME ON PURPOSE.** A literal would be an immortal `.rdata` record that
survives a free it never had — a false negative. `fill` builds the bytes in a loop, so the record the
cell holds is heap and solely the cell's.
```maxon
typealias Count = int(0 to 1000)

type Cell
	export var s as String

	export static function create() returns Cell
		return Cell{s: ""}
	end 'create'

	export function fill(n Count)
		var t = ""
		for _ in 0 upto n 'grow'
			t = "{t}x"
		end 'grow'
		self.s = t
	end 'fill'

	// The callee reassigns the very field its own argument was read out of.
	export function replace(other String) returns String
		self.s = "replaced"
		return "[{other}]"
	end 'replace'
end 'Cell'

function main() returns ExitCode
	var a = Cell.create()
	a.fill(10)
	print("{a.replace(a.s)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx]
```

<!-- test: a-managed-field-handed-to-a-free-callee-beside-its-owner -->
### The free-function spelling: the owner and its field, in two argument columns
The same conflict with no receiver in it. `clobber` is handed `b` and `b.s`; the store through `b`
drops the record `borrowed` names. This is the shape `Parser.callReachesBorrowedArgumentStorage`
answers by finding the borrower's storage named by ANOTHER argument of the same call — the bare name
`b` — rather than by the receiver.
```maxon
typealias Count = int(0 to 1000)

type Cell
	export var s as String

	export static function create() returns Cell
		return Cell{s: ""}
	end 'create'

	export function fill(n Count)
		var t = ""
		for _ in 0 upto n 'grow'
			t = "{t}x"
		end 'grow'
		self.s = t
	end 'fill'
end 'Cell'

function clobber(c Cell, borrowed String) returns String
	c.s = "clobbered"
	return "[{borrowed}]"
end 'clobber'

function main() returns ExitCode
	var b = Cell.create()
	b.fill(10)
	print("{clobber(b, borrowed: b.s)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx]
```

<!-- test: an-array-element-handed-to-a-callee-that-clears-the-array -->
### The ARRAY-ELEMENT analogue — E3070's own subject, in the one shape it could not see
`try a.get(0) otherwise ""` mints a pending borrow, and a pending borrow becomes a `BorrowRecord` only
when a BINDING claims it (`Parser.attachPendingBorrows`). Written inline as an argument, no binding
ever does — so the entry is dropped unclaimed at the statement's end and the write door's
`functionHoldsABorrow` gate answers false. The element was handed to a callee that frees it with
nothing anywhere recording that it had been borrowed.

⚠ **THE BOUND SPELLING IS STILL REFUSED AND THAT IS NOT AN INCONSISTENCY** — see
`mutating-callee-argument` above, where `let s = try arr.get(0) …` then `grow(arr)` is E3070. There
the borrow outlives the statement under a name, which is the fact E3070 is about; here it cannot
outlive the call it is an argument to.
```maxon
typealias Count = int(0 to 1000)
typealias StringArray = Array with String

function clobber(a StringArray, borrowed String) returns String
	a.clear()
	return "[{borrowed}]"
end 'clobber'

function build(n Count) returns String
	var t = ""
	for _ in 0 upto n 'grow'
		t = "{t}x"
	end 'grow'
	return t
end 'build'

function main() returns ExitCode
	var a = StringArray.create()
	a.push(build(10))
	print("{clobber(a, borrowed: try a.get(0) otherwise "")}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx]
```

<!-- test: a-field-of-a-module-var-handed-to-a-callee-that-reaches-it-by-name -->
### MODULE STORAGE is reachable from every callee, so it needs no second argument
`clobber` is handed nothing but the string. It reaches the cell by NAME, which no argument list can
show — so the reachability test that settles the two cases above cannot settle this one, and the rule
answers it the other way: a top-level `var` is reachable from everywhere, so a borrow out of one is
anchored unconditionally. **MEASURED before the anchor: exit 82 `slabOsAllocFailed`.** The oracle
prints `[[[[[[[[[[[]` on this program.
```maxon
typealias Count = int(0 to 1000)

type Cell
	export var s as String

	export static function create() returns Cell
		return Cell{s: ""}
	end 'create'

	export function fill(n Count)
		var t = ""
		for _ in 0 upto n 'grow'
			t = "{t}x"
		end 'grow'
		self.s = t
	end 'fill'
end 'Cell'

var g = Cell.create()

function clobber(borrowed String) returns String
	g.s = "clobbered"
	return "[{borrowed}]"
end 'clobber'

function main() returns ExitCode
	g.fill(10)
	print("{clobber(g.s)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx]
```

<!-- test: a-bare-module-var-handed-to-a-callee-that-rebinds-it -->
### The same rule at the simplest spelling: the global itself, rebound under its own argument
No field and no container — the argument IS the global's value, and `emitCheckedGlobalStore` drops
the record it displaces. `recordGlobalReadValue`'s writable arm is the second of
`markRebindableSlotRead`'s three producers, so the read carries the mark exactly as a field read does.
**MEASURED before the anchor: exit 82.**
```maxon
typealias Count = int(0 to 1000)

var g = ""

function grow(n Count)
	var t = ""
	for _ in 0 upto n 'grow'
		t = "{t}x"
	end 'grow'
	g = t
end 'grow'

function clobber(borrowed String) returns String
	g = "clobbered"
	return "[{borrowed}]"
end 'clobber'

function main() returns ExitCode
	grow(10)
	print("{clobber(g)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx]
```

<!-- test: a-receiver-read-out-of-a-module-var-outlives-a-callee-that-replaces-it -->
### The RECEIVER is an argument too, and a call with an EMPTY argument list still has one
`parseCallArgs` used to `return` the moment it saw `)`, so a method call taking no arguments never
reached the anchor pass at all — and the receiver is prepended into the same column an argument
occupies (`prependReceiverArg`). `g.measure()` reads its receiver out of a module `var` and the body
replaces that very global, so `self` dangles for the rest of the method.
**MEASURED before the anchor: 0xC0000005.**

⚠ The receiver does NOT satisfy its own reachability — only its being MODULE storage does. Were it to,
every method call through a self field (`self.items.push(v)`) would anchor its own receiver, which is a
refcount pair on the hottest shape in the corpus. `a-field-handed-to-a-callee-that-cannot-reach-it`
below is the guard on that.
```maxon
typealias Count = int(0 to 1000)

type Inner
	export var s as String

	export static function create() returns Inner
		return Inner{s: ""}
	end 'create'

	export function fill(n Count)
		var t = ""
		for _ in 0 upto n 'grow'
			t = "{t}x"
		end 'grow'
		self.s = t
	end 'fill'

	export function measure() returns String
		g = Inner.create()
		return "[{self.s}]"
	end 'measure'
end 'Inner'

var g = Inner.create()

function main() returns ExitCode
	g.fill(10)
	print("{g.measure()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx]
```

<!-- test: a-field-handed-to-a-callee-that-cannot-reach-it -->
### The over-anchoring guard: a callee handed only the VALUE takes no reference
The narrowing is the whole reason this rule is affordable, so it needs a case that fails if the rule
stops narrowing. `measure` is handed `b.s` and nothing else: it cannot name the cell, the cell is not
module storage, and no other argument denotes it — so nothing it can do frees the record and the
caller owes no reference. The committed fragment's `main` carries **no `__str_retain` at all**, against
exactly one in the free-callee case above whose only difference is that the owner travels beside the
field. Anchoring unconditionally instead was measured to put one at every `f(self.field)` in the tree
— most of the calls a compiler writes — and to push `Parser.foldFile`'s 24-argument call over the x64
register file (**E5001: needs 9 more registers than are available**), so shv2 stopped self-compiling.
```maxon
typealias Count = int(0 to 1000)

type Cell
	export var s as String

	export static function create() returns Cell
		return Cell{s: ""}
	end 'create'

	export function fill(n Count)
		var t = ""
		for _ in 0 upto n 'grow'
			t = "{t}x"
		end 'grow'
		self.s = t
	end 'fill'
end 'Cell'

function measure(borrowed String) returns String
	return "[{borrowed}]"
end 'measure'

function main() returns ExitCode
	var b = Cell.create()
	b.fill(10)
	print("{measure(b.s)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx]
```

<!-- test: a-merged-borrow-carries-every-arms-storage-to-the-call -->
### ONE value, TWO pending borrows — and the one the callee can reach is the SECOND
`match-gives-merges-every-arms-borrow` above pins this for a BINDING: each arm retargets its own pending
borrow onto the one phi, so the phi carries an entry per SUBJECT. Written inline as an ARGUMENT the same
phi reaches `Parser.borrowedArgumentIsAtRisk`, which must ask its reachability question of **every** entry
— `a` here, then `b`, and only `b` is what the call is handed. Which entry comes first is push order,
which is not a rule.

⛔ **CAUGHT AT REVIEW, BEFORE IT SHIPPED, AND IT IS THE DEFECT `Parser.retargetPendingBorrow`'s OWN ⚠⚠
BLOCK RECORDS ONE DOOR OVER** — that walk was written to stop at its first match on the reasoning *"a value
id is DEFINED ONCE, so at most one entry can carry it"*, true of the id and never the claim that mattered.
**SABOTAGE-VERIFIED**: stop the walk at its first link and this program exits **82 `slabOsAllocFailed``**,
exactly as it does on the pre-change binary; run it whole and it prints the bytes.

⚠ A composed borrow reaches the same walk the same way — `composePendingBorrowOntoBases` files one entry
per link of the chain, all keyed on the accessor's result — so this case guards both producers of the
several-entries-one-value shape.
```maxon
typealias Count = int(0 to 1000)
typealias StringArray = Array with String

enum Mode
	first
	second
end 'Mode'

function clobber(target StringArray, borrowed String) returns String
	target.clear()
	return "[{borrowed}]"
end 'clobber'

function build(n Count) returns String
	var t = ""
	for _ in 0 upto n 'grow'
		t = "{t}x"
	end 'grow'
	return t
end 'build'

function main() returns ExitCode
	var a = StringArray.create()
	var b = StringArray.create()
	a.push(build(4))
	b.push(build(10))
	let m = Mode.second
	let out = clobber(b, borrowed: match m 'pick'
		first gives try a.get(0) otherwise ""
		second gives try b.get(0) otherwise ""
	end 'pick')
	print("{out}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx]
```
