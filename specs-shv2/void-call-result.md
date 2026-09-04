---
feature: void-call-result
status: stable
keywords: [void, functions, call, statement, type-mismatch]
category: diagnostics
---

# Using the Result of a Void Call

## Documentation

A function that declares no return type returns **nothing**. There is no value, so there is nothing
to use:

```text
function noop()
	return
end 'noop'

let x = noop()      // error: Function 'noop' does not return a value
```

The grammar has exactly two positions a call can appear in, and they differ by precisely this:

- a **bare-call statement** — `noop()` on a line of its own. The call is evaluated for its effect;
  a result it returns must be discarded with `_ =` (`discarded-results.md`). A void callee is what
  this position is *for*.
- a call inside an **expression** — `let x = f()`, `f() + 1`, `if f()`, `g(f())`. The result *is*
  the expression. A void callee has nothing to give here, and the program is rejected.

### Why this was a wrong answer, and what it has to do with cross-file calls

It is the same defect as the cross-file one, wearing different clothes: **one sentinel, two
meanings.** The parser reported *both* "I could not see this callee" *and* "this callee returns
nothing" as `unresolved` — and `unresolved` is the tag that **agrees with everything**, because
deferring is the right thing to do about a type you cannot know. So "there is no value" was
classified as "I cannot judge this value", every type rule correctly deferred on it, and

```text
let x = noop()
return x + 4
```

compiled — returning whatever happened to be in the return register.

The deferral was never the defect. Giving "no value" its own tag is the fix, and the two meanings
can never be confused again.

## Tests

<!-- test: void-result-in-binding -->
```maxon
function noop()
	return
end 'noop'

function main() returns ExitCode
	let x = noop()
	return x + 4
end 'main'
```
```maxoncstderr
error E2004: <fragment>:7:10: Function 'noop' does not return a value
```

<!-- test: void-result-in-arithmetic -->
```maxon
function noop()
	return
end 'noop'

function main() returns ExitCode
	return noop() + 4
end 'main'
```
```maxoncstderr
error E2004: <fragment>:7:9: Function 'noop' does not return a value
```

<!-- test: void-result-in-condition -->
```maxon
function noop()
	return
end 'noop'

function main() returns ExitCode
	if noop() 'branch'
		return 1
	end 'branch'
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:7:5: Function 'noop' does not return a value
```

<!-- test: void-result-as-argument -->
```maxon
typealias Integer = int(i64.min to i64.max)

function noop()
	return
end 'noop'

function takeInt(n Integer) returns Integer
	return n
end 'takeInt'

function main() returns ExitCode
	return takeInt(noop())
end 'main'
```
```maxoncstderr
error E2004: <fragment>:13:17: Function 'noop' does not return a value
```

<!-- test: void-result-returned -->
```maxon
function noop()
	return
end 'noop'

function main() returns ExitCode
	return noop()
end 'main'
```
```maxoncstderr
error E2004: <fragment>:7:9: Function 'noop' does not return a value
```

<!-- test: cross-file-void-result -->
A void callee in ANOTHER file is the same error, and it is the case the two-meaning sentinel hid
best: the parser could see neither the callee nor its voidness, and reported the same `unresolved`
for both.
```maxon
// --- file: a.maxon
export function noop()
	return
end 'noop'

// --- file: b.maxon
function main() returns ExitCode
	let x = noop()
	return x + 4
end 'main'
```
```maxoncstderr
error E2004: <fragment>:9:10: Function 'noop' does not return a value
```

<!-- test: void-call-statement-is-legal -->
⚠ THE OVER-REJECTION GUARD. A void call in STATEMENT position is exactly what that position is for,
and it must keep compiling — the check is about the RESULT being used, not about the call.
```maxon
function noop()
	return
end 'noop'

function main() returns ExitCode
	noop()
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: value-call-result-may-still-be-discarded -->
A value-returning call whose result nobody wants is discarded with `_ =`, and that is not this
error: the void check is about a call that has NO result being used as if it had one.

⚠ `answer` writes a module counter, so it HAS an effect. That is deliberate and it is a different
rule: a bare statement of it would be E3065 and a pure callee's E3064 (`discarded-results.md`); this
case is about the void check, so the discard is spelled the way both rules accept.
```maxon
typealias Integer = int(i64.min to i64.max)

var calls = 0 as Integer

function answer() returns Integer
	calls = calls + 1
	return 42
end 'answer'

function main() returns ExitCode
	_ = answer()
	return 41 + calls
end 'main'
```
```exitcode
42
```

### A container's void method — and the name it has to actually HAVE

⚠ **THIS ERROR ASSERTS TWO THINGS ABOUT THE CALLEE — that it EXISTS and that it is VOID — so raising
it about a name the receiver has never heard of is false twice over.** The builtin containers dispatch
a method by NAME, arm by arm, and a name matching no arm is the roster refusal (E2015). Between the
value-yielding arms and the void ones sat a check reading only `resultUsed`, so an unknown name in
VALUE position reached it first: `let x = arr.frobnicate()` reported *"Function 'frobnicate' does not
return a value"* — a claim that `frobnicate` is a real, void `Array` method — while the identical call
in STATEMENT position correctly reported the roster. The refusal is now inside the arm the name
matched, which is what `String` has always done (`parseStringAppend`).

⚠⚠ **THE FOUR RED CASES AND THE THREE CONTROLS ARE ONE TEST.** Simply deleting the void check would
turn every case below green in the first group and every one in the second into a confusing downstream
type error — a real void mutator in value position must STILL be E2004, because for those names the
sentence is TRUE. The pairs are what distinguish *"the check is name-scoped"* from *"the check is
gone"*.

<!-- test: error.unknown-array-method-in-value-position -->
An unknown `Array` method in value position is the roster refusal, not a claim that it exists.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	let x = arr.frobnicate()
	return x
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:14: Unsupported: `Array` member 'frobnicate' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: error.unknown-set-method-in-value-position -->
The same for a `Set` — the two containers shared the misplaced check, so they share the fix.

⭐ **THE SENTENCE MOVED WHEN `Set` STOPPED BEING SYNTHESIZED (W90), AND THE SUBJECT DID NOT.** With
`stdlib/Set.maxon` listed there is no builtin roster left to quote: an unknown member of a declared type is
the ordinary undefined-callee refusal, which is character-for-character what the already-retired `Map` answers
for `m.frobnicate()` today (MEASURED: `error E3004: … call to undefined function 'Map.frobnicate'`). What this
case is FOR is unchanged and still checked — the refusal is about the member being unknown, not about the call
being in value position.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

function main() returns ExitCode
	var s = IntSet.create()
	s.insert(1)
	let x = s.frobnicate()
	return x
end 'main'
```
```maxoncstderr
error E4006: <fragment>:8:12: Type 'Set' has no method named 'frobnicate'
```

<!-- test: error.unknown-buffer-member-in-value-position -->
The `__ManagedMemory` buffer surface has its own roster, and it is the one the reader needs — the two
method sets differ, so naming the `Array`'s here would send them to the wrong question.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = mm.frobnicate()
	return x
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:13: Unsupported: `__ManagedMemory` member 'frobnicate' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: error.buffer-only-method-on-an-array-in-value-position -->
⭐ The sharpest spelling of the false claim: `grow` IS a name this dispatcher knows, but only on the
BUFFER surface — so on a plain `Array` it is as absent as `frobnicate`, and it was reported as an
existing void method of it.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	let x = arr.grow(8)
	return x
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:14: Unsupported: `Array` member 'grow' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: error.array-void-mutator-in-value-position -->
⚠ THE CONTROL. `push` really is a void `Array` mutator, so here the sentence is TRUE and E2004 is the
right answer. `specs-shv2/stdlib-array.md`'s `push-self-assignment` documents this exact program as
the mutators-hand-the-receiver-back behaviour it is waiting on; this pins what the compiler says
about it TODAY, so the property is tested rather than described.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr = arr.push(1)
	return arr.count()
end 'main'
```
```maxoncstderr
error E2004: <fragment>:7:12: Function 'push' does not return a value
```

<!-- test: error.set-void-mutator-in-value-position -->
⚠ THE CONTROL for `Set` — `insert` is its one void mutator.

⭐ **IT NAMES `Set.insert` RATHER THAN A BARE `insert` SINCE W90, FOR THE REASON ITS `String` NEIGHBOUR TWO
CASES DOWN ALREADY RECORDS**: an arm blames the member the author wrote, while a corpus call names the
function it actually resolved to. `stdlib/Set.maxon` is listed, so this is that second shape. The code, the
position and the answer are unchanged.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

function main() returns ExitCode
	var s = IntSet.create()
	let x = s.insert(1)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:7:12: Function 'Set.insert' does not return a value
```

<!-- test: error.string-void-mutator-in-value-position -->
⚠ THE CONTROL for `String`, and the one of the three whose refusal NO LONGER COMES FROM AN ARM. Its void
guard used to live inside the arm `append` matched — which is why it was correct first and why the other
two were written to follow it — but `append` retired onto `stdlib/String.maxon` at W49 wave 8, so this is
now the ORDINARY refusal every void call gets. ⭐ **That is why it names `String.append` where its two
neighbours name a bare `insert`/`push`**: an arm blames the member the author wrote, while a corpus call
names the function it actually resolved to, exactly as `stdlib-only-string-methods`' E3088 does. The
code, the position and the answer are unchanged.
```maxon
function main() returns ExitCode
	var s = "ab"
	let x = s.append("cd")
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:12: Function 'String.append' does not return a value
```

### One control PER VOID ARM — because the guard is now written once per arm

⚠⚠ **THE REFUSAL IS NAME-SCOPED BY BEING WRITTEN IN EACH VOID ARM, so nothing but a test makes the
twelve arms agree.** That is the cost of the scoping and it is deliberate — the alternative, one shared
call ahead of them all, IS the defect above — and the failure mode of a MISSING one is never a compile
error. It is one of TWO things, depending on the arm, and both were measured by neutering the guard and
running this section (12 of 12 controls red, one per arm, and nothing else in the spec):

- the eight arms that hand the RECEIVER back (`push`/`reserve`/`resize`/`clear`/`insert`/`append`,
  `Set.insert`, `String.append`) **silently accept**: `arr = arr.push(1)` compiled and linked.
- the four THROWING buffer arms (`setLength`/`setByte`/`grow`/buffer `append`) tag their result `void`,
  so with the guard gone `let x = mm.setLength(1)` reaches `declareInitializedBinding` and **PANICS the
  compiler** — *"maxonTypeOfTag: a `void` tag names no value"*. For those four the guard is not a
  message, it is the only thing standing between the parser and its own precondition.

Every arm below was verified reachable with the result USED, so every one of these pins a distinct wrong
answer. The three cases above cover `push`, `Set.insert` and `String.append`; these cover the remaining
nine.

<!-- test: error.array-reserve-in-value-position -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	let x = arr.reserve(4)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:8:14: Function 'Array.reserve' does not return a value
```

<!-- test: error.array-resize-in-value-position -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	let x = arr.resize(4)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:8:14: Function 'resize' does not return a value
```

<!-- test: error.array-clear-in-value-position -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	let x = arr.clear()
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:8:14: Function 'Array.clear' does not return a value
```

<!-- test: error.array-insert-in-value-position -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	let x = arr.insert(0, value: 1)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:8:14: Function 'Array.insert' does not return a value
```

<!-- test: error.array-append-in-value-position -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	let other = IntArray.create()
	let x = arr.append(other)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:8:14: Function 'append' does not return a value
```

<!-- test: error.buffer-setlength-in-value-position -->
⭐ The buffer's three void mutators are THROWING, and a `try` around one makes the result unused — so
this reachable spelling is the one WITHOUT the `try`, which is exactly where the guard fires.
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = mm.setLength(1)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:13: Function 'setLength' does not return a value
```

<!-- test: error.buffer-setbyte-in-value-position -->
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = mm.setByte(0, 65)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:13: Function 'setByte' does not return a value
```

<!-- test: error.buffer-grow-in-value-position -->
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = mm.grow(8)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:13: Function 'grow' does not return a value
```

<!-- test: error.buffer-append-in-value-position -->
`append` is the one name with TWO void arms — the buffer's throwing one and the `Array`'s — so it needs
a control on each receiver, or a guard lost from either arm passes on the other's case.
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let other = try __ManagedMemory.create(4, elementSize: 1) otherwise return 2
	let x = mm.append(other)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:5:13: Function 'append' does not return a value
```

### THE THIRTEENTH ARM — `set`, whose only reachable value spelling is under a `try`

⚠⚠ **A `resultUsed` GUARD CANNOT SEE A THROWING METHOD'S VALUE POSITION, because a `try` target is
parsed with `resultUsed: false` BY DESIGN** — the `try` decides value-ness at its OWN position, from the
TAG of the result the target minted (`parseTry`'s `voidInValue`). That is the derived, single-site half of
this rule, and it is why the buffer's three throwing void mutators need no more than an honest tag: a
value-position `try mm.setLength(1)` is refused by it. `set` is throwing and valueless too — its runtime
entry ok-returns a literal `0` and `dispatchArrayMethod`'s own comment calls it *"a discarded dummy"* —
but the arm tagged that dummy `integer`, so the tag said "there IS a value here" and the one check able
to look was answered wrongly.

⇒ MEASURED on the tree this case was written against: `let x = try arr.set(0, value: 7) otherwise return
1` **compiled, linked and ran, exit 0**, binding `x` to the dummy — while the runnable oracle refuses the
identical program with `E3059: type mismatch: ''stdlib.Array.set' does not return a value'`. It is the
D11 defect's dual: not a false claim that a method exists, but a silent fabricated value for a method
that has none.

⚠⚠ **A THROWING VOID METHOD NEEDS BOTH HALVES, BECAUSE THE TWO SPELLINGS OF ITS VALUE POSITION ARE SEEN
BY DIFFERENT CHECKS.** Under a `try` the arm's `resultUsed` is false and only the TAG can refuse; written
BARE the arm's `resultUsed` is true and only the GUARD can refuse — and there the tag is not merely
insufficient, it is dangerous: an honest `void` with no guard reaches `declareInitializedBinding` and
**PANICS the compiler** (*"maxonTypeOfTag: a `void` tag names no value"*), because the bare-throwing-call
E3057 lives a whole pass later, in `SemanticCheck`. That is measured, and it is the same panic the four
buffer mutators' guards have been quietly preventing. Both spellings are pinned below.

⚠ The STATEMENT position is what these methods are FOR and it is unaffected:
`arrays.md:index-assignment` and `managed-memory-builtin.md:set-and-get` already run
`try …set(…) otherwise …` and assert the value it stored, on both surfaces.

<!-- test: error.array-set-in-value-position -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	let x = try arr.set(0, value: 7) otherwise return 1
	return x
end 'main'
```
```maxoncstderr
error E3059: <fragment>:8:10: type mismatch: ''set' does not return a value'
```

<!-- test: error.buffer-set-in-value-position -->
⭐ The buffer's `set` is a SECOND callee (`__managed_mem_set`, capacity-bounded where the `Array`'s is
length-bounded), reached through the same arm — so it needs its own case for the reason the two `append`
arms do.

⭐⭐ **AND THE TWO CASES QUOTE THE SAME NOUN, WHICH IS THE POINT OF THIS BLOCK (D11c).** Two callees, one
source spelling: the author wrote `set` on both surfaces, so `set` is what both messages say, and the cases
are told apart by their PROGRAMS rather than by their text. A message that distinguished them would be
distinguishing something the author cannot see.
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = try mm.set(0, 7) otherwise return 2
	return x
end 'main'
```
```maxoncstderr
error E3059: <fragment>:4:10: type mismatch: ''set' does not return a value'
```

<!-- test: error.bare-array-set-in-value-position -->
⚠ THE OTHER HALF — the same call WITHOUT the `try`, which is the arm's guard's case and not the tag's. One
case covers both surfaces here, because one guard in one arm does (the surface only picks the callee), and
what it pins is that the parser refuses this itself rather than handing a `void` to a binding.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	let x = arr.set(0, value: 7)
	return x
end 'main'
```
```maxoncstderr
error E2004: <fragment>:8:14: Function 'set' does not return a value
```

### THE NOUN A VALUELESS `try` QUOTES IS THE AUTHOR'S METHOD, NOT THE EMITTED SYMBOL (D11c)

⚠⚠ **E3059 USED TO NAME A SYMBOL NO AUTHOR CAN TYPE.** The two `set` cases above and the seven below all
reach `parseTry`'s `voidInValue` refusal, and its noun came straight off the `tryCall`'s callee — so a
program that says `mm.setLength(1)` was told about `'__managed_set_length'`, a name the `__` prefix forbids the
author from writing at all (E2051). The right code, quoting a construct the program does not contain: the
same defect D12 removed from E3057's sentence, arriving at the one door D12 did not pass through.

⇒ The cure is D12's own map (`GtRuntime.runtimeCalleeSourceMethod`) asked from a SECOND consumer, not a
second map. **These cases exist because a map with one caller is a map whose coverage nothing measures**:
E3057's caller reaches the file, directory, string-search and buffer families, and NOT the array one; only
E3059 can reach a callee that is both throwing and VALUELESS, which is a different subset again.

⭐⭐ **THAT SUBSET IS NINE, AND IT IS DERIVED — `isThrowingRuntimeCallee` INTERSECTED WITH THE EMISSION
SITES THAT TAG THEIR RESULT `void`.** `__managed_set`, the buffer's five void mutators (`set`, `setLength`,
`setByte`, `grow`, `append`), and — from two families this rung was not looking at — `__mf_delete`,
`__mf_rename` and `__md_create`. All nine are pinned here.

⚠⚠ **D11c FIRST CLAIMED SIX, BY PROBING THE TWO FAMILIES IT HAD IN HAND, AND THE THREE IT MISSED WERE
EXACTLY THE THREE NOTHING ELSE PINNED EITHER.** The file and directory maps' `delete`/`rename`/`create`
arms have no E3057 case of their own, so E3059 was their only reader and it was unpinned: with
`managedFileSourceMethod`'s `delete` arm answering `stat` and `managedDirectorySourceMethodName`'s `create`
arm answering `next`, the suite read **2897 passed, 0 failed** while the compiler told an author who wrote
`delete` about `'stat'` — the rung's own defect, surviving the rung. **An enumeration is a claim about a
SET: derive it from the predicates that define the set, because probing family-by-family can only find the
families you already suspected.**

⚠ The BARE spellings of the buffer four are pinned far above as E2004 (`Function 'setLength' does not
return a value`), by the arm's own `resultUsed` guard. **Two codes, two checks, one English sentence** —
and they must both be here, because a guard lost from either arm passes on the other's case.

<!-- test: error.buffer-try-setlength-in-value-position -->
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = try mm.setLength(1) otherwise return 2
	return x
end 'main'
```
```maxoncstderr
error E3059: <fragment>:4:10: type mismatch: ''setLength' does not return a value'
```

<!-- test: error.buffer-try-setbyte-in-value-position -->
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = try mm.setByte(0, value: 7) otherwise return 2
	return x
end 'main'
```
```maxoncstderr
error E3059: <fragment>:4:10: type mismatch: ''setByte' does not return a value'
```

<!-- test: error.buffer-try-grow-in-value-position -->
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = try mm.grow(8) otherwise return 2
	return x
end 'main'
```
```maxoncstderr
error E3059: <fragment>:4:10: type mismatch: ''grow' does not return a value'
```

<!-- test: error.buffer-try-append-in-value-position -->

⚖ **`append` LEFT THIS ENUMERATION ON 2026-08-07 AND IS KEPT AS THE CASE THAT SAYS SO.** The four above it
are void THROWING members, and a `try` on one is well-formed until the value position is asked about — which
is the E3059 this block enumerates. The ruling that made the buffer's `append` NON-throwing
(`ManagedMemoryRuntime.ManagedAppendName`, and `managed-memory-methods.error.try-on-the-buffers-append` for the door)
takes it out of that class: this program is now wrong about the `try` BEFORE it is wrong about the value.

⚠ **AND THAT ORDER IS THE RIGHT ONE, WHICH IS THE ONLY REASON THIS IS AN EDIT AND NOT A REGRESSION.** Both
complaints are true and `parseTry` raises both, E3055 first. `try` is what the author wrote OUTERMOST, and
*"there is nothing here to catch"* is the fact that survives deleting the `let x =` — where E3059 would not
survive deleting the `try`. The three void throwing siblings below are unaffected and still pin E3059.
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let other = try __ManagedMemory.create(4, elementSize: 1) otherwise return 2
	let x = try mm.append(other) otherwise return 3
	return x
end 'main'
```
```maxoncstderr
error E3055: <fragment>:5:10: try requires a throwing function: this builtin call cannot fail
```

<!-- test: error.file-try-delete-in-value-position -->
⭐⭐ **THE THREE CASES BELOW ARE THE ONES THE "SIX" MISSED, AND THEY ARE HERE RATHER THAN IN
`managed-file.md`/`managed-directory.md` FOR THE REASON THIS WHOLE BLOCK EXISTS: the roster E3059 can reach
is not any one family's roster, so it has to be enumerated in ONE place.** `delete`, `rename` and `create`
are the file and directory families' void throwing members. Each has an E3057 sentence in principle and no
E3057 CASE, so before these three the only reader of those map arms was E3059 and nothing checked it.

⚠ Compile-time on purpose — E3059 is raised in `parseTry`, ahead of every target gate, so unlike its
siblings in `managed-file.md` this case needs no `<!-- targets: -->` marker and no file to exist.
**VERIFIED, not assumed**: byte-identical output under `--target=x64-linux` and `--target=wasm32-wasi`,
neither of which can so much as emit `__mf_delete`.
```maxon
function main() returns ExitCode
	let x = try __ManagedFile.delete("no_such_file_void_try_xyz.txt".toByteArray().managed) otherwise return 1
	return x
end 'main'
```
```maxoncstderr
error E3059: <fragment>:3:10: type mismatch: ''delete' does not return a value'
```

<!-- test: error.file-try-rename-in-value-position -->
```maxon
function main() returns ExitCode
	let x = try __ManagedFile.rename("no_such_a_xyz.txt".toByteArray().managed, "no_such_b_xyz.txt".toByteArray().managed) otherwise return 1
	return x
end 'main'
```
```maxoncstderr
error E3059: <fragment>:3:10: type mismatch: ''rename' does not return a value'
```

<!-- test: error.dir-try-create-in-value-position -->
⚠ `create` is spelled by THREE unrelated constants (`Parser.CreateMethod` for `Array`/`Set`,
`ManagedMemoryCreateMethod`, `ManagedDirectoryCreateMethod`) because three unrelated surfaces happen to use
the word. This case pins the DIRECTORY one, which is the only one of the three that is void and throwing.
```maxon
function main() returns ExitCode
	let x = try __ManagedDirectory.create("no_such_dir_void_try_xyz".toByteArray().managed) otherwise return 1
	return x
end 'main'
```
```maxoncstderr
error E3059: <fragment>:3:10: type mismatch: ''create' does not return a value'
```

<!-- test: error.user-void-try-in-value-position -->
⭐⭐ **THE CONTROL THAT SEPARATES THE MAP FROM THE MESSAGE.** An ordinary user function reaches the very
same refusal, and its callee IS its source spelling — so it must be quoted UNCHANGED by the map lookup
above it. `declaresCallee` is what routes it past the map, and it is the SAME door
`requireThrowingNamedTryTarget` used to admit this `try` one line earlier, so the two cannot come to
disagree about whether the name has a declaration.

⚠ **The doubled quotes are CORRECT and are not a defect** — `''mayFail' does not return a value'` is the
runnable oracle's byte-exact output (`maxon-sharp/Compiler/2-Parser.cs:9374` spells both layers in one
literal) and `specs/error-handling.md:634` is the canonical pin. The outer pair is E3059's own
`type mismatch: '…'` frame; the inner pair quotes the noun inside it. Do not "fix" one compiler's half.
```maxon
enum MyError
	bad
end 'MyError'

function mayFail() throws MyError
	throw MyError.bad
end 'mayFail'

function main() returns ExitCode
	let x = try mayFail() otherwise return 1
	return x
end 'main'
```
```maxoncstderr
error E3059: <fragment>:11:10: type mismatch: ''mayFail' does not return a value'
```

<!-- test: error.user-method-void-try-in-value-position -->
⭐ **THE SECOND HALF OF THE `declaresCallee` CONTROL: a QUALIFIED callee.** A user METHOD is registered under
`Gate.bump`, so the arm that returns the callee unchanged returns something the author did not literally
type — they wrote `g.bump(1)`. That is NOT this rung's defect and must not be "fixed" into `bump`: the
runnable oracle answers `'Gate.bump'` for this identical program (verified against `bin/maxon.exe`), so the
qualified spelling IS the specified noun and shv2 agreeing with it is the point of the case.
```maxon
typealias Integer = int(i64.min to i64.max)

enum E implements Error
	bad
end 'E'

type Gate
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump(v Integer) throws E
		if v < 0 'neg'
			throw E.bad
		end 'neg'
	end 'bump'
end 'Gate'

function main() returns ExitCode
	var g = Gate.create()
	let x = try g.bump(1) otherwise return 8
	return x
end 'main'
```
```maxoncstderr
error E3059: <fragment>:24:10: type mismatch: ''Gate.bump' does not return a value'
```
