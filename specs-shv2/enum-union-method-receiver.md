---
feature: enum-union-method-receiver
status: experimental
keywords: [enum, union, method, receiver, self, ownership, borrow, static]
category: type-system
---

# An `enum`/`union` method's RECEIVER

## Documentation

`specs/enum-full.md` defines what a method on an `enum` or a `union` *computes*. This file pins what
its **receiver** is, which that spec never exercises: an enum's `self` is its i64 tag (payload-free)
or a pointer to its box (a union with payloads), and it arrives as **parameter 0**, **borrowed** —
exactly as an `enum`-typed parameter does.

Every case in this file was a **hand probe first** (D1, 2026-07-29; the second section's cases are the
independent review's, same day). Some of them found nothing, and they are here because a probe that found
nothing is worth exactly as much as one that found something: next rung, only a committed case still runs.
The refusals are here because a refusal nobody pinned is a refusal the next rung deletes by accident.

The three receiver cases each carry a payload long enough to force a heap allocation, so the leak gate
has something to catch: `managed-payload-receiver-never-bound` is the `TestOutcome` shape (the payload
is managed and the method never binds it); `two-calls-on-one-managed-receiver` borrows twice and drops
once, so a receiver consumed by the first call would make the second a use-after-move while a receiver
INCREF'd per call would leak; `self-passed-to-a-free-function` hands `self` on as an ordinary borrowed
argument, which is what proves the receiver is parameter 0 and nothing more.

## Binding the receiver's MANAGED PAYLOAD (D1b)

The cases above never bind the payload; the four beside them now do, because that is what the harness
itself wants (`PeReadError.displayReason` reads one, `WorkerRecord.spec` RETURNS one). The receiver is
the CALLER's box, so the payload cannot be MOVED out of it — it is **RETAINED** at the bind
(`__mm_incref`), the binding owns that second reference and drops it at the arm's own exit, and **the
box slot is left intact** so the owner keeps its own reference. The match therefore does not consume
the receiver. See `union-managed-payload.md`'s Documentation for the rule in full and for the
borrowed-PARAMETER half of the same mechanism.

Two of the four exist only to catch the two ways a plausible implementation goes wrong, and neither is
caught by the other cases: `receiver-still-owns-its-payload-after-a-bind` asks the same receiver twice,
which a slot-nulling implementation fails; `managed-payload-receiver-bind-leak-free` binds 300 times, so
an unbalanced refcount is a certainty (exit 101) rather than a coin flip.

⚠ **`return self` is REFUSED, and that refusal is the whole safety argument for letting bare `self`
be a value at all.** A boxed union's receiver is a pointer the CALLER's binding owns; returning it
would have the caller adopt and free a box whose own owner frees it too. It is refused by the
*ordinary borrowed-return rule* reaching parameter 0 — not by an enum-specific check — which is why
allowing `self` as a value does not create a case, it stops hiding one.

⚠ **The oracle is WORSE on statics, which is why they are pinned.** The bootstrap makes an `enum`
static a bare parse error (`E2010`), and a `union` static it **accepts and then misreads** — `static`
is silently dropped, so the call fails with `E3036 missing argument for parameter 'self'`, blaming the
call site for a declaration the compiler mis-parsed. A positioned refusal at the declaration is the
better answer, and it costs nothing: no `union`/`enum` in `stdlib/` declares a static method.

⚠ **Known cosmetic debt, recorded rather than fixed here:** the borrowed-return refusal below renders
the receiver's type as `` `int` ``, because a declared enum erases to `integer` in `TypeResolution`.
It is confusing, never wrong at runtime, and its one-place cure is the same display-name funnel for
compiler-owned and erased types that `__CharacterSet` already needs.

## An enum BODY now has two kinds of member, and THREE readers walk it

Three separate walks read an `enum`/`union` body, and a method member is the first construct that makes
them able to disagree: the **real parse** (`parseEnumDeclaration`), the **tolerant declaration sweep**
(`recordScannedEnum`, which builds the whole-program layout and signature index), and the
**sibling-receiver scan** (`ensureSiblingReceivers`, which resolves a bare `inner()` inside a method).
The D1 review found all three wrong, each in its own way, and each case below is the measurement:

- ⚠ **A method's closing `end` carries a LABEL, and the sweep read it as a member.** `end 'bump'` left the
  sweep's cursor on the charLiteral, which `readEnumCaseInto` reports as a string-backed case and which
  aborted the scan — so **only the FIRST method of any enum was ever scanned**, and every case declared
  after a method was silently dropped from the whole-program layout. The second method's return type was
  therefore `unresolved`: `e.weight()` **panicked in lowering** (`valueTagToStdType`) when it returned a
  float, and typed its result `unknown` when it returned a String.
- ⚠ **An enum case may be spelled with a KEYWORD, and a case list is not block structure.** The
  sibling-receiver scan counted `end`, `while`, `if` and `match` case names as block openers and closers:
  a case named `end` closed the walk early (a bare sibling call declared after it reported `E3004 call to
  undefined function`), and a case named `while` over-counted so the walk ran PAST the enum's own `end`
  and adopted a LATER type's method as a sibling. Both refused legal programs.
- ⚠ **An `enum`/`union` has no FIELDS, and `self.x` used to take the compiler down.** `self.reason` on a
  payload-bearing union — the first thing a reader tries — reached `enclosingLayout` and **panicked**,
  blaming the declaration sweep for a disagreement that never happened. A case's payload is bound by a
  pattern; the refusal is now positioned, and it is one door for both the read and the write.

⚠ **A case declared AFTER a method is ACCEPTED by shv2 and refused by the oracle** (`E2010 Expected 'end'
but got 'omega'` — the bootstrap ends an enum body at its first method). shv2's real parse always accepted
it; making the sweep agree is what the fix is, and the permissive direction is deliberate — the two readers
agreeing matters more than matching a restriction neither `stdlib/` nor the corpus relies on.

## Tests

<!-- test: managed-payload-receiver-never-bound -->
```maxon
union Outcome
	pass
	fail(reason String)

	export function isPass() returns bool
		return match self 'p'
			pass gives true
			fail gives false
		end 'p'
	end 'isPass'
end 'Outcome'

function main() returns ExitCode
	let o = Outcome.fail("a rather long failure reason to force a heap allocation")
	if o.isPass() 'y'
		return 1
	end 'y'
	return 7
end 'main'
```
```exitcode
7
```

<!-- test: managed-payload-receiver-is-bound -->
The "IS bound" twin of the case directly above, and the shape the harness actually wants
(`PeSectionReader.PeReadError.displayReason`): the method BINDS the managed payload out of its
borrowed receiver and reads it. The receiver is the CALLER's box, so the payload cannot be moved
out of it — it is RETAINED instead (`__mm_incref` at the bind), the binding owns that second
reference and drops it at the arm's exit, and the box slot is left intact for the owner. The
refcount balances at exactly one free.
```maxon
union PathError
	missing
	unreadable(path String)

	export function displayReason() returns String
		return match self 'k'
			missing gives "no path was given at all, and this literal is heap-long"
			unreadable(path) gives "cannot open {path}"
		end 'k'
	end 'displayReason'
end 'PathError'

function main() returns ExitCode
	let e = PathError.unreadable("/a/rather/long/path/that/forces/a/real/heap/allocation")
	print(e.displayReason())
	return 42
end 'main'
```
```exitcode
42
```
```stdout
cannot open /a/rather/long/path/that/forces/a/real/heap/allocation
```

<!-- test: managed-payload-escapes-through-the-receiver -->
`SpecWorkerPool.WorkerRecord.spec` verbatim in shape: the bound payload is not merely READ inside
the method, it is RETURNED — so the retained reference outlives the receiver's borrow and the
caller adopts it. This is the sub-case a borrow-only design could not close, and it is why the bind
retains rather than borrows. The other two payloads are `_`-discarded and stay in the box, freed by
the owner's cascade.
```maxon
union Record
	pass(specName String, testName String)
	fail(specName String, testName String, reason String)

	export function spec() returns String
		return match self 's'
			pass(s, _) gives s
			fail(s, _, _) gives s
		end 's'
	end 'spec'
end 'Record'

function main() returns ExitCode
	let r = Record.fail("the spec name, long enough to force a real heap allocation", testName: "the test name, also long enough to be heap allocated", reason: "the failure reason, likewise long enough for the heap")
	print(r.spec())
	return 42
end 'main'
```
```exitcode
42
```
```stdout
the spec name, long enough to force a real heap allocation
```

<!-- test: receiver-still-owns-its-payload-after-a-bind -->
⭐ **THE CASE THAT CATCHES A NULLED SLOT.** A retain must NOT clear the box slot the payload was
loaded from — the container's owner keeps its own reference — so the SAME receiver is asked twice
and the second call must still see the payload's bytes. A move-out implementation (which nulls the
slot) passes every other case in this file and fails only here: the second call would interpolate a
null pointer.
```maxon
union PathError
	missing
	unreadable(path String)

	export function displayReason() returns String
		return match self 'k'
			missing gives "no path was given at all, and this literal is heap-long"
			unreadable(path) gives "cannot open {path}"
		end 'k'
	end 'displayReason'
end 'PathError'

function main() returns ExitCode
	let e = PathError.unreadable("the path string, itself long enough to be a real heap allocation")
	let first = e.displayReason()
	let second = e.displayReason()
	print(first)
	print(second)
	return 42
end 'main'
```
```exitcode
42
```
```stdout
cannot open the path string, itself long enough to be a real heap allocationcannot open the path string, itself long enough to be a real heap allocation
```

<!-- test: managed-payload-receiver-bind-leak-free -->
⭐ **THE LEAK GATE.** 300 rounds of binding a managed payload out of a borrowed receiver. One
unbalanced `incref` per round leaves the payload's refcount at 300 when the container dies, so the
box is never freed and the run exits 101; one unbalanced `decref` frees it under the container. The
loop is what turns a single off-by-one into a certainty rather than a coin flip.
```maxon
typealias Round = int(0 to 1000)

union PathError
	missing
	unreadable(path String)

	export function displayReason() returns String
		return match self 'k'
			missing gives "no path was given at all, and this literal is heap-long"
			unreadable(path) gives "cannot open {path}"
		end 'k'
	end 'displayReason'
end 'PathError'

function main() returns ExitCode
	let e = PathError.unreadable("a heap allocated path string, long enough to be a real one")
	var i = 0 as Round
	while i < 300 'spin'
		let shown = e.displayReason()
		print("{shown}")
		i = i + 1
	end 'spin'
	print(e.displayReason())
	return 42
end 'main'
```
```exitcode
42
```
```stdout
cannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real onecannot open a heap allocated path string, long enough to be a real one

```

<!-- test: managed-payload-bound-out-of-an-RVALUE-receiver -->
The receiver is a TEMPORARY the caller owns for the length of the statement, not a named
binding — so the retain, the arm-exit drop and the temporary's own drop all land in one
statement. The payload must still print intact and the box must still be freed exactly once.
```maxon
union PathError
	missing
	unreadable(path String)

	export function displayReason() returns String
		return match self 'k'
			missing gives "no path was given at all, and this literal is heap-long"
			unreadable(path) gives "cannot open {path}"
		end 'k'
	end 'displayReason'
end 'PathError'

function main() returns ExitCode
	print(PathError.unreadable("an rvalue receiver's payload, long enough for the heap").displayReason())
	return 0
end 'main'
```
```exitcode
0
```
```stdout
cannot open an rvalue receiver's payload, long enough for the heap
```

<!-- test: a-method-that-binds-its-payload-and-recurses -->
The method binds its managed payload out of `self` and then calls itself on the same receiver,
four frames deep — four simultaneously live retains of one payload, released as the frames
unwind. A retain that leaked would leave the count at four when the caller's binding dies.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Depth = int(0 to 10)

union M
	silent
	text(body String)

	export function spin(d Depth) returns Integer
		return match self 'k'
			silent gives 0
			text(s) gives 1 + self.spin((d - 1) if d > 0 else 0) if d > 0 and s.byteLength() > 0 else 1
		end 'k'
	end 'spin'
end 'M'

function main() returns ExitCode
	let m = M.text("a recursively bound payload string, long enough for the heap")
	return m.spin(4) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: two-calls-on-one-managed-receiver -->
```maxon
union Outcome
	pass
	fail(reason String)

	export function isPass() returns bool
		return match self 'p'
			pass gives true
			fail gives false
		end 'p'
	end 'isPass'
end 'Outcome'

function main() returns ExitCode
	let o = Outcome.fail("another long heap allocated failure reason string here")
	let a = o.isPass()
	let b = o.isPass()
	if a or b 'y'
		return 1
	end 'y'
	return 7
end 'main'
```
```exitcode
7
```

<!-- test: self-passed-to-a-free-function -->
```maxon
union Outcome
	pass
	fail(reason String)

	export function viaHelper() returns bool
		return helper(self)
	end 'viaHelper'
end 'Outcome'

function helper(o Outcome) returns bool
	return match o 'p'
		pass gives true
		fail gives false
	end 'p'
end 'helper'

function main() returns ExitCode
	let o = Outcome.fail("yet another long heap allocated reason string for the probe")
	if o.viaHelper() 'y'
		return 1
	end 'y'
	return 7
end 'main'
```
```exitcode
7
```

<!-- test: return-self-from-a-boxed-union -->
A boxed union's `self` is a borrowed heap box, and it escapes through the same door a struct's does: the
receiver is co-owned by an `__mm_retain` before the `ret`, so the caller's `c` and the receiver's own `b`
each drop it once and the box is freed exactly once. This case used to pin the borrowed-return REFUSAL,
whose sentence deferred the copy to "P1.4b" — a milestone that had already shipped.
```maxon
union Boxed
	one(v Integer)
	two(s String)

	export function giveBack() returns Boxed
		return self
	end 'giveBack'
end 'Boxed'

function main() returns ExitCode
	let b = Boxed.one(3)
	let c = b.giveBack()
	match c 'k'
		one(v) then return v as ExitCode
		two then return 1
	end 'k'
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
3
```

<!-- test: error.static-method-on-a-union -->
```maxon
union Shape
	circle(r Integer)

	export static function unit() returns Integer
		return 1
	end 'unit'
end 'Shape'

function main() returns ExitCode
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2015: <fragment>:5:9: Unsupported: a `static function` on `union Shape` (an INSTANCE method is supported — a static one has no receiver to name the enum through, and no `enum`/`union` in the corpus declares one)
```

<!-- test: error.static-method-on-an-enum -->
```maxon
enum Color
	red

	export static function best() returns Integer
		return 1
	end 'best'
end 'Color'

function main() returns ExitCode
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2015: <fragment>:5:9: Unsupported: a `static function` on `enum Color` (an INSTANCE method is supported — a static one has no receiver to name the enum through, and no `enum`/`union` in the corpus declares one)
```

<!-- test: two-methods-and-the-second-ones-return-type -->
```maxon
union Outcome
	pass
	fail(reason String)

	export function isPass() returns bool
		return match self 'p'
			pass gives true
			fail gives false
		end 'p'
	end 'isPass'

	export function weight() returns Real
		return 2.5
	end 'weight'
end 'Outcome'

function main() returns ExitCode
	let o = Outcome.fail("a rather long failure reason to force a heap allocation")
	if o.isPass() 'y'
		return 1
	end 'y'
	if o.weight() > 2.0 'w'
		return 7
	end 'w'
	return 2
end 'main'
typealias Real = float(f64.min to f64.max)
```
```exitcode
7
```

<!-- test: a-case-declared-after-a-method -->
```maxon
enum Order
	alpha

	export function bump() returns Integer
		return 1
	end 'bump'

	omega
end 'Order'

function tagOf(k Order) returns Integer
	return match k 'w'
		alpha gives 1
		omega gives 5
	end 'w'
end 'tagOf'

function main() returns ExitCode
	let e = Order.omega
	return tagOf(e) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
5
```

<!-- test: keyword-named-cases-interleaved-with-methods -->
```maxon
enum Weird
	alpha
	function

	export function mid() returns Integer
		var total = 0
		while total < 2 'spin'
			total = total + 1
		end 'spin'
		return total
	end 'mid'

	end
	export

	export function two() returns Integer
		return 20
	end 'two'

	export function three() returns Integer
		return 30
	end 'three'

	omega

	export function tag() returns Integer
		return match self 'w'
			alpha gives 1
			function gives 2
			end gives 3
			export gives 4
			omega gives 5
		end 'w'
	end 'tag'
end 'Weird'

function main() returns ExitCode
	let a = Weird.alpha
	let f = Weird.function
	let e = Weird.end
	let x = Weird.export
	let o = Weird.omega
	let acc = a.tag() + f.tag() * 10 + e.tag() * 100 + x.tag() * 1000 + o.tag() * 10000
	if acc != 54321 'bad'
		return 1
	end 'bad'
	if a.mid() != 2 'badMid'
		return 2
	end 'badMid'
	if a.two() + a.three() != 50 'badTwo'
		return 3
	end 'badTwo'
	return 7
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: sibling-call-past-a-case-named-end -->
```maxon
enum Sib
	alpha
	end
	omega

	export function outer() returns Integer
		return inner() + 1
	end 'outer'

	export function inner() returns Integer
		return 6
	end 'inner'
end 'Sib'

function main() returns ExitCode
	let a = Sib.alpha
	return a.outer() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: a-free-call-beside-a-case-named-while -->
```maxon
enum Sib
	alpha
	while

	export function outer() returns Integer
		return helper() + 1
	end 'outer'
end 'Sib'

type Later
	export var n as Integer

	export function helper() returns Integer
		return 99
	end 'helper'
end 'Later'

function helper() returns Integer
	return 6
end 'helper'

function main() returns ExitCode
	let a = Sib.alpha
	return a.outer() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: error.field-read-through-an-enum-receiver -->
```maxon
union Outcome
	pass
	fail(reason String)

	export function why() returns bool
		return self.reason.byteLength() > 0
	end 'why'
end 'Outcome'

function main() returns ExitCode
	let o = Outcome.fail("a rather long failure reason to force a heap allocation")
	if o.why() 'y'
		return 1
	end 'y'
	return 7
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:10: Unsupported: a field access through `self` in a method of `enum`/`union` `Outcome` — an enum/union declares no fields; a case's PAYLOAD is bound by a pattern (`match self 'k' … fail(reason) then …`), never read through the receiver
```

<!-- test: error.field-write-through-an-enum-receiver -->
```maxon
union Outcome
	pass
	fail(reason String)

	export function clobber() returns bool
		self.reason = "nope"
		return true
	end 'clobber'
end 'Outcome'

function main() returns ExitCode
	let o = Outcome.fail("a rather long failure reason to force a heap allocation")
	if o.clobber() 'y'
		return 1
	end 'y'
	return 7
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:3: Unsupported: a field access through `self` in a method of `enum`/`union` `Outcome` — an enum/union declares no fields; a case's PAYLOAD is bound by a pattern (`match self 'k' … fail(reason) then …`), never read through the receiver
```

<!-- test: a-directive-selecting-between-two-methods -->
```maxon
enum Plat
	alpha

#if testing(true)
	export function tag() returns Integer
		return 3
	end 'tag'
#else
	export function tag() returns Integer
		return 7
	end 'tag'
#endif

	omega
end 'Plat'

function main() returns ExitCode
	let p = Plat.omega
	match p 'k'
		alpha then return 1
		omega then return p.tag() as ExitCode
	end 'k'
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: a-directive-inside-a-method-body -->
```maxon
enum Plat
	alpha

	export function tag() returns Integer
#if testing(true)
		return 3
#else
		return 7
#endif
	end 'tag'

	omega
end 'Plat'

function main() returns ExitCode
	let p = Plat.omega
	match p 'k'
		alpha then return 1
		omega then return p.tag() as ExitCode
	end 'k'
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```
