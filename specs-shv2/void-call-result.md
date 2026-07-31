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

- a **bare-call statement** — `noop()` on a line of its own. The call is evaluated for its effect
  and its result discarded. A void callee is what this position is *for*.
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
A value-returning call in statement position keeps its existing behaviour: the result is discarded,
and that is not this error.
```maxon
typealias Integer = int(i64.min to i64.max)

function answer() returns Integer
	return 42
end 'answer'

function main() returns ExitCode
	answer()
	return 42
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
error E2015: <fragment>:8:14: Unsupported: `Array` method 'frobnicate' — P1.7 slice 1 provides create/push/get/set/count/capacity/isEmpty/reserve/resize/first/last/pop/clear/insert/remove and slice 4 adds slice/clone/append; the rest (map/contains/…) arrive later
```

<!-- test: error.unknown-set-method-in-value-position -->
The same for a `Set` — the two containers shared the misplaced check, so they share the fix.
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
error E2015: <fragment>:8:12: Unsupported: `Set` method 'frobnicate' — P1.7b provides create/insert/contains/remove/count; `from`-construction and iteration are later slices
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
error E2015: <fragment>:4:13: Unsupported: `__ManagedMemory` member 'frobnicate' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
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
error E2015: <fragment>:8:14: Unsupported: `Array` method 'grow' — P1.7 slice 1 provides create/push/get/set/count/capacity/isEmpty/reserve/resize/first/last/pop/clear/insert/remove and slice 4 adds slice/clone/append; the rest (map/contains/…) arrive later
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
error E2004: <fragment>:7:12: Function 'insert' does not return a value
```

<!-- test: error.string-void-mutator-in-value-position -->
⚠ THE CONTROL for `String`, the container that was already correct here and is the pattern the other
two now follow: its void refusal has always lived inside the arm `append` matched.
```maxon
function main() returns ExitCode
	var s = "ab"
	let x = s.append("cd")
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:12: Function 'append' does not return a value
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
error E2004: <fragment>:8:14: Function 'reserve' does not return a value
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
error E2004: <fragment>:8:14: Function 'clear' does not return a value
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
error E2004: <fragment>:8:14: Function 'insert' does not return a value
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
error E3059: <fragment>:8:10: type mismatch: ''__arr_set' does not return a value'
```

<!-- test: error.buffer-set-in-value-position -->
⭐ The buffer's `set` is a SECOND callee (`__arr_mem_set`, capacity-bounded where the `Array`'s is
length-bounded), reached through the same arm — so it needs its own case for the reason the two `append`
arms do.
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = try mm.set(0, 7) otherwise return 2
	return x
end 'main'
```
```maxoncstderr
error E3059: <fragment>:4:10: type mismatch: ''__arr_mem_set' does not return a value'
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
