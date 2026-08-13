---
feature: divergent-arm-owned-temporaries
status: experimental
keywords: [match, panic, throws, divergent, ownership, temporary, borrow, scope-lifetime]
category: memory-management
---

# A Diverging Arm Settles Its OWNED TEMPORARIES Like Any Other Arm

## Documentation

A diverging arm — `panic("…")` or `throws E.case` in place of a value — still *evaluates an expression* on
its way out, and that expression can build heap the arm becomes responsible for. The shape that does it is
a **member borrow off a temporary**:

```text
default throws E.p(Box.make().name)
```

`Box.make()` yields an owned box; `.name` borrows a field out of it. `dispatchMethodOnBase` calls
`giveTemporaryScopeLifetime`, which moves that box out of `pendingTempDrops` and into `ownedBindings` so
the borrow cannot outlive it — and `emitHandoffExitDrops` then **restores** `ownedBindings`, so the record
survives the arm. Something has to drain it, against a floor taken before the arm was parsed.

**Every position that admits a diverging terminal form owes that bracket**, and they are the positions
`divergentHandlerOpensAt` rosters:

| position | how it settles |
|---|---|
| a block-form `try`'s divergent `otherwise` | `dropArmScopedOwned(ownedFloor, …)` |
| a match EXPRESSION arm (`red throws E.case`) | `finishArmExit(…, ownedFloor: armOwnedFloor, …)` |
| a match `default` (`default throws E.case`) | `finishArmExit(…, ownedFloor: defaultOwnedFloor, …)` |

The third one **had no floor at all** until this spec. With nothing to drain to, the move marks and the
owned-binding stack fell out of lockstep and the compiler **panicked** —
`reconcileMovesAtMerge: a reaching edge's mark holds 0 entries but 1 owned bindings are in scope` — with a
twenty-frame stack trace, on a program the C# bootstrap compiles and runs correctly (measured: the same
`gives=5` / `throws=9` these cases pin). It is the exact gap A3h closed for the *plain* `default`, whose
comment records the same reasoning; A3h fixed that arm and left the diverging one beside it. The floors are
now taken **once, above the fork**, so both default shapes reach one exit obligation rather than two that
can drift apart again.

⚠ **The two cases below are a matched pair and must stay one.** The first is the defect; the second is the
control that proves the expression-arm position was independently correct. Measured by A/B on one binary
with a single line reverted: the `default` case panicked, the arm case still answered `throws=9gives=5`.
A fix that made both pass for one reason would have lost that.

⚠ **Both pin `exitcode` as well as `stdout`.** A leaked box exits **101**, and a case that pins only
stdout never checks the exit code — so the leak this whole spec is about would pass silently.

## Tests

<!-- test: default-throws-settles-a-borrowed-temporary -->
The defect, both ways through the match: `pick(1)` takes the `gives` arm and `pick(2)` takes the diverging
`default`, whose thrown value borrows a field out of a temporary box. Before the fix this program did not
compile at all — the parser panicked at `reconcileMovesAtMerge`.
```maxon
typealias Small = int(0 to 100)

type Box
	export let name as String

	export static function make() returns Box
		return Box{name: "boxed-name-long-enough-to-heap-allocate"}
	end 'make'
end 'Box'

union E implements Error
	p(s String)
end 'E'

function pick(n Small) returns Small throws E
	let r = match n 'k'
		1 gives 5
		default throws E.p(Box.make().name)
	end 'k'
	return r
end 'pick'

function main() returns ExitCode
	let hit = try pick(1) otherwise 9
	print("gives={hit}")
	let thrown = try pick(2) otherwise 9
	print("throws={thrown}")
	return 0
end 'main'
```
```stdout
gives=5throws=9
```
```exitcode
0
```

<!-- test: expression-arm-throws-settles-a-borrowed-temporary -->
The control: the same borrowed temporary on a diverging PATTERN arm of a match expression rather than on
the `default`. This position brackets the arm through `finishArmExit` and was already correct — it
answered identically on a binary where the `default` case still panicked, which is what proves the two
positions are settled independently rather than by one shared accident.
```maxon
typealias Small = int(0 to 100)

type Box
	export let name as String

	export static function make() returns Box
		return Box{name: "boxed-name-long-enough-to-heap-allocate"}
	end 'make'
end 'Box'

union E implements Error
	p(s String)
end 'E'

function pick(n Small) returns Small throws E
	let r = match n 'k'
		1 throws E.p(Box.make().name)
		default gives 5
	end 'k'
	return r
end 'pick'

function main() returns ExitCode
	let thrown = try pick(1) otherwise 9
	print("throws={thrown}")
	let hit = try pick(2) otherwise 9
	print("gives={hit}")
	return 0
end 'main'
```
```stdout
throws=9gives=5
```
```exitcode
0
```
