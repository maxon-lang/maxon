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

Every case below was a **hand probe first** (D1, 2026-07-29). Three of them found nothing, and they
are here because a probe that found nothing is worth exactly as much as one that found something:
next rung, only a committed case still runs. The refusals are here because a refusal nobody pinned is
a refusal the next rung deletes by accident.

The three receiver cases each carry a payload long enough to force a heap allocation, so the leak gate
has something to catch: `managed-payload-receiver-never-bound` is the `TestOutcome` shape (the payload
is managed and the method never binds it); `two-calls-on-one-managed-receiver` borrows twice and drops
once, so a receiver consumed by the first call would make the second a use-after-move while a receiver
INCREF'd per call would leak; `self-passed-to-a-free-function` hands `self` on as an ordinary borrowed
argument, which is what proves the receiver is parameter 0 and nothing more.

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
compiler-owned and erased types that `__StringIndex` already needs.

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

<!-- test: error.return-self-from-a-boxed-union -->
```maxon
union Boxed
	one(v int)
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
		two(_) then return 1
	end 'k'
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:3: Unsupported: returning a borrowed `int` value — a struct/union parameter (or a re-borrow of one) is a heap box the caller would adopt and free while the borrow's own owner frees it too, a double free. Return an OWNED value; consuming or copying a borrowed aggregate to return it arrives at P1.4b
```

<!-- test: error.static-method-on-a-union -->
```maxon
union Shape
	circle(r int)

	export static function unit() returns int
		return 1
	end 'unit'
end 'Shape'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:9: Unsupported: a `static function` on `union Shape` (an INSTANCE method is supported — a static one has no receiver to name the enum through, and no `enum`/`union` in the corpus declares one)
```

<!-- test: error.static-method-on-an-enum -->
```maxon
enum Color
	red

	export static function best() returns int
		return 1
	end 'best'
end 'Color'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:9: Unsupported: a `static function` on `enum Color` (an INSTANCE method is supported — a static one has no receiver to name the enum through, and no `enum`/`union` in the corpus declares one)
```
