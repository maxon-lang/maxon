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
refuse one that hands the array to a callee. shv2 read the free-poison byte here (0x3F) before
this rule. The oracle accepts the program — it retains the element — so this is a deliberate,
user-ruled divergence in the refusing direction.
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

<!-- test: propagating-try-carries-the-borrow -->
### A PROPAGATING `try` binds the accessor's own value
No merge, no phi, nothing to retarget — the borrow is the value the binding takes. The third of the
parser's three value merges is therefore the only `try` form that needs one.
```maxon
typealias StringArray = Array with String

enum Oops implements Error
	failed
end 'Oops'

function look(arr StringArray) returns ExitCode throws Oops
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
error E3070: specs/fragments/borrow-liveness/receiver-method-writing-its-own-field.test:20:2: cannot mutate 'b' via 'wipe' while it is borrowed by 's' (borrowed at line 19)
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
error E3070: specs/fragments/borrow-liveness/receiver-method-explicit-self-spelling.test:20:2: cannot mutate 'b' via 'wipe' while it is borrowed by 's' (borrowed at line 19)
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
error E3070: specs/fragments/borrow-liveness/receiver-method-rebinding-its-own-field.test:20:2: cannot mutate 'b' via 'wipe' while it is borrowed by 's' (borrowed at line 19)
```

<!-- test: receiver-method-inside-a-for-loop -->
### … and inside a `for … in` over the field it clears
The loop element's borrow is lexical, so the call is refused wherever in the body it sits.
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
error E3070: specs/fragments/borrow-liveness/receiver-method-inside-a-for-loop.test:22:3: cannot mutate 'b' via 'wipe' while it is borrowed by 'it' (borrowed at line 21)
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
error E3070: specs/fragments/borrow-liveness/receiver-method-writes-transitively.test:24:2: cannot mutate 'b' via 'wipe' while it is borrowed by 's' (borrowed at line 23)
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
error E3070: specs/fragments/borrow-liveness/sibling-call-explicit-self-spelling.test:18:3: cannot mutate 'items' via 'reset' while it is borrowed by 's' (borrowed at line 17)
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
