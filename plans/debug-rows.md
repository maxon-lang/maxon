# A local's debug facts are ops the passes carry: a version sits at its assignment, a scope end in its block, and a value id is minted once per function

Class issue: #145 · Priority: P2

## Stage order

One stage is one local commit, which ticks its box. A stage starts when every stage it waits on is ticked
here and every issue it waits on is closed. The plan lands once, after its last stage.

| Done | Stage | Rule it establishes | Waits on | Not beside | Needs a ruling |
|---|---|---|---|---|---|
| [ ] | 1 | A value id is minted once per function: IrFunction carries the next value id and no pass recomputes it by walking the body |  |  |  |
| [ ] | 2 | A binding's version is a debugBind op in the function's own op stream at its assignment, carried by every pass as any op is | 1 |  |  |
| [ ] | 3 | A scope end is a debugScopeEnd op in its block, so a block split or clone carries it and the scope can never lose a tail | 2 |  |  |
| [ ] | 4 | Folded words or memory is stated by the debugBind source at the pass that removes the stores, not inferred from whether a binding has rows | 2, 3 | 3 |  |
| [ ] | 5 | A pass that changes a named value's representation rewrites its binding: a register-pair tuple result reads unavailable | 4 | 3 |  |

## Problem

What a debugger is told about a local is a side table that no pass owns. `IrFunction.debugLocals` holds one `DebugLocalDecl` per binding, and each fact in it is a key into IR that optimisation passes rewrite without knowing the table exists:

| Fact | How it is keyed today | What a pass does to the key |
|---|---|---|
| which value the name holds | `DebugLocalVersion.value`, a `ValueId` | Std has no value-id counter: `nextFreeValueId` (`StdDialect`) answers "highest id still defined, plus 1" by walking the body, so an id whose definition was deleted is minted again (#143) |
| from where it holds it | `DebugVersionDef.definedAfter(block, anchor)`, written by `ParserOwnership.debugDefinitionHere` as "the last value-defining op before here" | a store or void call after the anchor is not counted; when the anchor is removed, `debugSiteAfterAnchor` (`RegisterAllocator`) walks `candidate - 1` down the value ids to the nearest surviving value (#145) |
| until where | `DebugLocalDecl.scopeEnds`, block ids, followed through `IrBlock.exitMovedTo` by `IrModule.blockHoldingTheExitOf` | `splitBlockExit` (`InsertRangeChecks`) calls `moveExitTo`, which overwrites an earlier move, so a block split twice drops the first tail from the scope (#150); `moveExitTo` has that one caller, so no pass that clones a block records the copy as a scope exit (read, not yet shown in a program) |
| folded words or memory | `DebugLocalDecl.foldedRecord`, noted by `NoteFoldedRecords` before the stores are removed | `MxdbgEmit.writeFunctionLocals` reads the folded record only when the binding has no rows (`unplacedRow`, `foldedAggregatePlacement`), so the answer follows from an unrelated fact (#141) |
| in which representation | the declared type, assumed to describe the value | `ValueTupleReturn.rewriteOneCall` turns a tuple-returning call's result into the low word of a register pair and the version keeps naming it (#144) |

Each member below is one of these keys going stale. #136 (closed) fixed one case, a folded block argument, by teaching the replacement primitive to forward versions, and had to stop there because a rebind's position has no carrier.

## Design

**A binding's debug facts are ops in the function's own op stream, written by the parser and carried by every pass as any op is.** The side table keeps only what never changes: the name, the declared type, the inline site.

1. **`debugBind(local, source)`**, a result-less op in the Maxon and Std dialects, carried through Target lowering to the range harvest. `source` is one of: a value; a constant; folded record words; gone. The parser emits one wherever it records a version today (`trackDebugLocal`, every `recordDebugRebind` site; a block-entry version is the block's first op). A version's position is the op's position. `DebugLocalDecl.versions`, `DebugVersionDef`, `debugDefinitionHere`, `debugSiteAfterAnchor`, `debugSiteAfterAnchorIfMoved` and `forwardDebugVersions` are deleted.
2. **`debugScopeEnd(local)`**, the same kind of op, in place of `scopeEnds`, `scopeEndLost`, `exitMoved`, `exitMovedTo`, `moveExitTo`, `blockHoldingTheExitOf` and `scopeExitsAfterSplits`. The scope ends at the exit of the block that holds the op. A block clone copies it; the one split door leaves a trailing scope end with the half that receives the exit.
3. **The operand is not a use.** Liveness, dead-code removal, register pressure, inline cost and every op count ignore both ops, through the central rosters (`collectStdOpUses`, `stdOpDefinedValue` and the cost counters), not per pass. A replacement (`replaceValuesInFunc`, `substituteValuesInFunc`, `substituteValuesInBlock`) rewrites the operand, so a version follows every replacement, op-defined ones included; this lifts the limit #136 recorded. A redirection does not. A deleted definition leaves the operand naming nothing: the version is located nowhere and still ends the earlier one (#118, decision 4).
4. **A value id is minted once per function.** `IrFunction` carries the next value id as it carries `nextBlockId`; every tier mints through it, a pool unit carries it out and home as `StdUnit` does `nextBlockId`, and `nextFreeValueId`'s body walk is deleted. A dangling operand can then never come to name another value, and each pass that sized a column by a walk reads a field.
5. **The executable stays byte-identical with and without the sidecar** (`docs/CLI_REFERENCE.md`). The ops are emitted only when the build captures debug locals, as the roster is today. `tests/debug/byte-identical-debug-info.maxtest` and `byte-identical-debug-info-under-inlining.maxtest` are the gate, extended over every `tests/debug` fixture.
6. **Cost.** One op per version and one per scope exit, which is what the roster holds today. The harvest stays one forward pass (#118, decision 7); `tests/ladders/genoncechain.sh` stays linear.

**Prior art.** LLVM carries a variable's location as `dbg.value` at the assignment: a replace-all-uses updates it, a deleted value leaves it undefined and ends the earlier location, and a block split or clone carries it. LLVM later moved these out of the instruction list (debug records) because passes had to skip them and code could differ with `-g`; point 3 and point 5 are that lesson. Go keeps a side table (`Func.NamedValues`) with no position, so a name can show a value before its assignment; Maxon's rule that a rebind opens at the assignment (#136) rules that out. Go's `IDAlloc` (`cmd/compile/internal/ssa/id.go`) is a per-function counter, as point 4 is.

**Rulings this extends.** #118 (closed) and #136 (closed) bind: one place per name per PC, a later version of another value ends the earlier one, rows end at scope exits, no bridging. Nothing here lets a name read a value those rulings refuse. Point 3 widens #136's forwarding to op-defined replacements, which #136 deferred to this issue.

### Open questions

1. **Op in the stream, or a record attached to an op?** A record keyed to an op's identity needs no invisibility rule, but passes rebuild a block's op list by filtering (`replaceBodyOps`), and that door cannot tell a deleted op from a moved one, so a record on a deleted op is lost: the present fault again. An op in the stream survives every rebuild unless a pass removes it on purpose. *Recommended: the op, with invisibility owned by the central rosters and gated by the byte-identical cases.*
2. **Always emit the ops, or only when capturing?** Always emitting makes byte-identity hold by construction, and charges every `--no-debug-info` build (every spec case) the capture. *Recommended: only when capturing, with the gate of point 5.*
3. **A tuple held in a register pair (#144): render it, or report it unavailable?** Rendering needs a composite location in the sidecar format and the readers. *Recommended: unavailable in this plan (`source` gone at the rewrite); a composite location is a separate enhancement.*
4. **Where a scope end sits when ops are appended to its block after it.** *Recommended: the op marks its block, not a position in it, as point 2 says; the scope covers the block through its exit, as today.*

## Stages

Each stage is one commit, green alone. Stages 3 to 5 share the harvest and the emitter, so they land in order.

1. **A value id is never reused within a function.**
   - Changes: the per-function counter of point 4; `nextFreeValueId` and its walk removed; `ValueMinter.resuming` reads the counter.
   - Area: `IR/IrFunction.maxon` (the field and its library-cache codec), `IR/Std/StdDialect.maxon`, `IR/Std/StdUnit.maxon`, and the callers of `nextFreeValueId`: `BorrowedParameterReturns`, `CancelRetainRelease`, `ExpandRuntimeLocks`, `InlineLeaves`, `InlineManagedPrimitives`, `InsertRangeChecks`, `NoteFoldedRecords`, `PromoteStackRecords`, `RefineValueRanges`, `StrengthReduceDivision`, `ThreadConstantBranches`, `UnswitchInvariantGuards`, `ValueOrigin`, both target lowerings.
   - Depends on: none.
   - Discharges: #143.
   - Shown by: a `tests/debug` case of #143's shape (below), red first; the scale ladder read, since a per-pass body walk goes away.
2. **A version is an op at its assignment.** `/plan-and-land`: the survey lists every pass that counts ops, matches adjacent ops or reads a block's last op.
   - Changes: points 1, 3 and 5. `InlineLeaves` clones the ops with the callee's body in place of `DebugLocalDecl.splicedCopy`'s version rename.
   - Area: `ParserOwnership.maxon`, `ParserControlFlow.maxon` (the `recordDebugRebind` sites), the Maxon dialect and `IR/Maxon/LowerMaxonToStd.maxon`, `IR/Std/StdDialect.maxon`, `IR/Std/InlineLeaves.maxon`, `IR/Std/StdPassPool.maxon`, `IR/IrFunction.maxon`, `Targets/Shared/BackendUnit.maxon`, `Targets/Shared/RegisterAllocator.maxon` (the harvest), the x64, arm64 and wasm conversions, `tests/debug`.
   - Depends on: stage 1.
   - Discharges: #145, and the op-defined half #136 left unavailable.
   - Shown by: two `tests/debug` cases, one per fault of #145 (below), red first; #136's acceptance cases stay green; the byte-identical cases over every fixture.
3. **A scope end is an op.**
   - Changes: point 2.
   - Area: `ParserTypeDeclarations.maxon` (the `noteScopeExit` sites), `IR/IrBlock.maxon`, `IR/IrModule.maxon`, `IR/Std/InsertRangeChecks.maxon` (`splitBlockExit`), `IR/Std/InlineLeaves.maxon`, `Targets/Shared/BackendUnit.maxon`, the harvest's scope handling.
   - Depends on: stage 2.
   - Discharges: #150.
   - Shown by: #150's repro (below), red first; a case with a shadowing local in a loop a pass clones, whose row must end in both copies (a hypothesis from reading; confirm it first).
4. **Folded words or memory is stated where the stores are removed.**
   - Changes: the pass that removes the dead field stores of a named record rewrites that binding's `debugBind` source to the folded words; a record whose stores stay is described by memory. `NoteFoldedRecords` supplies the words and no longer publishes them onto the declaration. The parser's folded constant becomes a constant source. `MxdbgEmit` reads the source and no longer asks whether the binding has rows. #136's rule "a binding with a folded record is not forwarded" (`followsAReplacementIn`) is removed.
   - Area: `IR/Std/NoteFoldedRecords.maxon`, the store-removing pass, `Debug/MxdbgEmit.maxon` (`unplacedRow`, `foldedAggregatePlacement`), `IR/IrFunction.maxon` (`foldedRecord`, `foldedConstant`).
   - Depends on: stage 2.
   - Discharges: #141.
   - Shown by: #141's two shapes (below), one case each, red first; `values-render` stays green.
5. **A pass that changes a named value's representation rewrites its binding.**
   - Changes: `ValueTupleReturn` sets the source of every `debugBind` naming a register-pair result to gone (open question 3).
   - Area: `IR/Std/ValueTupleReturn.maxon`; `Debug/MxdbgEmit.maxon` only if a guard there is found redundant.
   - Depends on: stage 4.
   - Discharges: #144.
   - Shown by: #144's shape (below), red first, or the stage's commit states that the emitter already refuses it and adds the case as a pin.

## Members

- #143 — a version naming a value deleted as dead can come to name an unrelated value. Stage 1. Shape to reproduce: a `var` whose last assignment is dead and is the last value minted in its function, in a function a later pass (unswitching, branch threading, division strength reduction) mints into. The binding must read unavailable, never another value's bytes.
- #145 — a rebind's position is inferred from the last value-defining op. Stage 2. Fault one: `var x = 1`, `let y = f()`, a store, a stop on the store's line, then `x = y`; `x` must still read 1 at the stop. Fault two: the anchor op is removed and the version falls back to its value's own definition, far before the assignment; it must open at the assignment. A rebind never opens before its assignment.
- #150 — a block split twice loses the first move of its scope exit. Stage 3. Repro: build `tests/debug/fixtures/blockedthenexit` with debug info and read `raw`'s rows in `Process.currentEnvironmentEntries` (also `spawnEnvironmentFor`): the row must run through the `__rc_ok` release block's argument move before `call __managed_decref`. 9 bytes over the debug fixtures.
- #141 — folded words or memory is decided by whether the binding has rows. Stage 4. Shape one: a record named directly, not through an inlined constructor, whose stores are removed, must not read null. Shape two: a folded record that stays live and is written through a callee must not show stale folded words. Both are hypotheses from reading.
- #144 — a tuple local returned in a register pair names the pair's low word. Stage 5. Shape: `let t = divmod(a, b)`, read `t.0` and `t.1` only through whole-word loads, stop after it, `print t`: the tuple or unavailable, never a word rendered as a record pointer. Not yet reproduced.

This issue is #145 rewritten: #145 already names the structural cause, a position with no carrier of its own.

## Related, not folded

- #146 — a parameter's register row opens after the prologue. Its mechanism is the harvest's write set for code that is not an op (#118, decision 6, loss class 5), not a binding's carrier. It touches the prologue emitters of both backends and can land before or after this plan.

## Assumed, not read

- Which pass removes the dead field stores of a named record (stage 4 finds it).
- Whether `MxdbgEmit` already refuses #144's shape.
- That no Target-tier pass replaces a named value by another after the roster is read.
- LLVM's and Go's `NamedValues` behaviour is stated from their documentation, not re-read in source; Go's `id.go` and `ssagen.addNamedValue` were read.
