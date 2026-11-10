---
title: Regions and Capabilities
description: Where the capability checker lives, which facts the parser records and which are inferred over the call graph, and how windows, tied promises and by-reference order are checked.
sidebar:
  order: 7
---

What the checks mean for a user is in [Regions and Capabilities](../LANGUAGE_REFERENCE.md#regions-and-capabilities).
This page is the map of where each check runs and what it relies on.

## Two halves: the parser records, SemanticCheck judges

The parser emits Maxon IR directly ([Names and Values](names-and-values.md)), and it decides everything that
needs only the body in hand while it parses. Anything that needs another function's facts is recorded as a
pending fact and judged in `SemanticCheck.maxon` once the whole program is parsed, because a callee's body may
be parsed after its caller and recursion needs a least fixpoint.

| Where | What it holds |
| --- | --- |
| `Compiler/CapabilityRules.maxon` | The contextual words (`IsolatedCapabilityWord`, `ModifyBlockWord`, `UniqueDeclarationWord`, `SendableBoundWord`, `MutatesClauseWord`, …), the store verdict (`storeVerdict`, `assignmentVerdict`), the join and adapt rules, and the shared refusal wording |
| `Compiler/RegionWindows.maxon` | `RegionFactTable`, the per-body facts the parser writes: the window stack (`OpenWindow`, `WindowKind.modify` and `oneStatement`), field origins, carried derivations and stores, lent locals, and the deferred refusal. Also `regionOperatorAt`, the token-level rule for the region operators |
| `Compiler/Lexer.maxon` | `markContextualHeads` retags a statement-start `modify` that opens a block |
| `Compiler/TypeQualifierPositions.maxon` | `TypeQualifierPositions`, the one token-stream classifier that decides whether each `readonly`, `isolated`, `immutable` and `mutable` qualifies a type or is a name. The parser, the formatter and the LSP all read its verdicts, and the parser reads a receiver as `( word self` followed by `,` or `)` (`receiverHeadAt`) |
| `Compiler/CapabilityInference.maxon` | `inferCapabilities`: the per-function facts over the call graph (`CapabilityFacts`) |
| `Compiler/CapabilityCallCheck.maxon` | `checkCapabilityCalls`: the call-site, conversion, tied-promise and must-use checks, run after inference |
| `Compiler/ServiceCompanions.maxon` | `classifyServiceRoster`, run at a `spawn`: after the roster's E3135, E3137, E3138, E3140 and E3216 verdicts, `capabilityVerdict` judges the message parameters, the replies and the named factory's parameters for E3217 (`typeNeedsACapabilityWord`) |
| `Compiler/OperandEvent.maxon`, `Compiler/OperandOrder.maxon`, `Compiler/ByRefOverlapCheck.maxon` | The E3186 evaluation-order check |

`SemanticCheck.maxon` runs `checkByReferenceOverlaps` and then `inferCapabilities` and `checkCapabilityCalls`,
after the parameter-mutation summary they read.

## Where the checker runs

The parse-time checks run in every body of every program, the standard library's included, and so do
`inferCapabilities`, `checkCapabilityCalls` and `checkFunctionTypeDoors`. A program that spells no region word
has no frozen data, window or tied promise for them to judge, so what they report there is the conversion
rule: a function value or conformer that writes, keeps or returns a read-only parameter, or may wait or yield,
against a function type or requirement that does not declare it (E3210).

## Inferred facts

`CapabilityFacts` holds, per function:

- `mutableOnly` and `immutableOnly`: position masks of the parameters whose uses need a writable or a frozen
  argument. A parameter in both is `ParamClass.both` (E3206); in neither it is `anyCapability`. A declared
  `mutable`, `immutable` or `isolated` marker fixes the class (`classAt`).
- `directMutable` and `directImmutable`: the masks before propagation, which the refusal messages use to name
  the line that decided a class.
- `mayWait` and `mayYield`: one bit per function.

`inferCapabilities` seeds the masks from each function's own writes and needs, adds the seeds that
`collectCapabilitySeeds` finds at stores, captures, sends and calls through requirements and function values,
and closes both masks over the call edges with `propagateParamMasks`. The bits come from `BlockingEdges`:
`markCallers` marks every caller of a seeded function, over `call` and `spawn` edges for both bits and over an
`async` edge for the wait bit only (`edgeTransmits`). `blockingSeedOf` seeds a function that waits on another
party, or that awaits a coroutine, and treats a body no summary can read as waiting.

A call result's capability depends on the callee's masks, which are unknown while the caller parses, so the
parser records the use (`ResultUse`, `StoredSlotArg`, `CaptureSite`, `JoinedParamSource`) and
`CapabilityCallCheck` judges it with `settledArgumentCapability` and `settledResultCapability` once every mask
is known. The value the parser holds meanwhile is never more permissive than the settled one.

## Windows

A `modify` block, a one-call open and a one-statement open push an `OpenWindow` on the parser's stack.
`capabilityWithinWindows` answers a value's capability under the stack: the opened root is writable and every
other value is read-only. A nested window keeps the enclosing roots read-only. Calls inside a window are
recorded and `checkNoWaitInsideWindows` refuses the ones whose callee `mayWait` or `mayYield` (E3205).

At run time a `modify` block brackets its body with `__gt_strand_guard_enter` and `__gt_strand_guard_exit`
(`GtRuntime.maxon`). The guard records the holder green thread and a depth in the strand's owner
(`GtOffStrandGuardHolder`, `GtOffStrandGuardDepth`); while it is set, the ready queue and the resume path keep a
sibling coroutine of the same strand from running. A `break` out of the block runs the
exit (`specs/regions-runtime.md`, `break-leaves-window-lets-sibling-run`). `wasm32-wasi` has no green
threads, so the guard calls are not emitted there.

## Tied and must-use promises

`tiedCarrierOwnersOf` collects, per function, the locals that hold the promise of an `async` call given
mutable data. `checkTiedPromisesAreOnlyAwaited` refuses any other use of such a promise (E3212),
`checkNoPromiseIsDropped` reports a promise that reaches an exit unconsumed (E3212 for a tied one, E3213 for any
other), and the exits it considers are the function's returns, throws and propagating `try` edges. At a
`.cancel()` of a tied promise the parser emits `__gt_promise_cancel_wait` instead of the plain renounce.

## By-reference order (E3186)

The parser keeps an `OperandOrder` per statement: a log of `OperandEvent`s (plain reads, reads of a by-reference
cell, writes) and the `ByRefLend`s of the calls whose by-reference argument a callee may write. An event
conflicts with a lend when its binding is the lend's binding (compared with `is`, so events share their
`VarInfo` and are never cloned) and it runs before the call's write. A candidate whose verdict depends on the
callee's body is stored in `IrFunction.pendingOverlaps`, and `checkByReferenceOverlaps` accepts it only when the
callee writes that parameter (`mutationSummary.writesByReference`), directly or by forwarding it. Bodies no
summary can see assume every position written.

The reads are recorded at the doors that read a name, not at `requireReceiverValue`, because an assignment
target reads its receiver after the right-hand side. The if-condition frame restarts at the top of
`parseIfCondition`, so `else if try …` chains restart it.

## `let` and constant data

A `let` binding, a `let` field and a union payload are write-through names. `readOnlyBlameOfBinding` answers
`WriteBlame.rebindOnly` for them: a write through the name is recorded exactly as a write through a `var` is
(it feeds the mutation summary, `provenanceWrites`, hold resolution and the capability seeds), and the only
callee that refuses the name is one that reassigns a `mutable` by-reference parameter (E2013), because a `let`
has no cell to give. `view` is parsed as `let`.

Constant data is the one place a name carries a capability. `recordModuleLetRoot` settles a read of a module
or `static` constant `immutable`; a local `let` bound to a constant and an inline `b"…"` literal take the same
capability and are emitted as image data. Everything derived
from such a value follows by adaptation: a field read off an `immutable` holder, an element of an `immutable`
slot, a call result computed from `immutable` arguments. Reading an `immutable` slot leaves the capability its holder
needs unchanged.

The refusals come from the same rules as every other capability mismatch. `storeVerdict` returns
`immutableIntoMutable` for an immutable value stored into a plain field, element or payload (E3196, with the
holder noun in the message); `checkCapabilityCalls` refuses it at a parameter the callee writes or stores into
a plain place (E3207); the result check refuses it at an unmarked result (E3209, `frozenDataForAPlainResult`).
A write through it is E3207 at the receiver (`'push' needs a writable receiver`).

`ProgramSignatures.aggregateTypeIsImmutable` makes "every field `let`" structural: each field type must itself be
a value type or an immutable aggregate (`declaredLayoutIsImmutable`, cycle-guarded). The emitted share walk
reads this predicate, so a record with a `let` array field is walked as an owner, not a view.
