---
name: prioritize
description: Choose a Maxon Roadmap issue's priority (P0-P3) when it is filed or found without one. Gives the four levels, how to tell them apart, worked examples, and when to ask the planner. Invoke as `/prioritize <issue number or draft>`; `.claude/roadmap/file.mjs` and `.claude/roadmap/unprioritized.mjs` point here.
---

# Choose a Roadmap issue's priority

Every open issue on the Maxon Roadmap carries one `Priority`. Work is taken from the highest level
first, so the priority decides what the fleet does next. The tools refuse an issue without one
(`file.mjs`, `status.mjs`); this skill is the judgment that picks it.

## The four levels

| Level | Meaning | Ask |
|---|---|---|
| **P0** now | a red CI lane; a soundness hole (a program the compiler accepts that can corrupt memory, alias across threads, or show a wrong value); wrong output from compiled code; a compiler crash or panic on a valid program | Is main red, or can a correct program go wrong because of this today? |
| **P1** next | blocks other planned work; is the next step of an accepted design's queue; a compile-time or memory regression that breaks the linear-scaling contract | Is something else waiting on it, or does a ladder bend? |
| **P2** normal | an ordinary bug; a wrong refusal; a misleading diagnostic; an enhancement whose design is settled | Is it a real defect that nothing waits on? |
| **P3** later | cleanup; duplication; docs drift; tooling; performance work with no measured regression | Would the language behave the same if it waited? |

## Deciding

1. **Take the highest level that is true.** A wrong refusal that also blocks a design step is P1, not P2.
2. **Unverified suspicions:** a suspected soundness hole is P0 until its repro confirms or refutes it, because the repro is the first step. A suspected leak or miscompile with no repro yet is P2, and is raised to P0 when the repro shows it.
3. **Design issues** take the priority of their remaining implementation work, not of the design.
4. **Measured versus unmeasured performance:** a superlinear ladder is P1; a constant-factor slowdown with no regression is P3; a measured cost of a few percent with a decided fix is P2.
5. **A debugger defect** that can show a wrong value is P0 only when it is shown on a real program. Found by reading, it is P2.
6. **Instances of a class:** an issue that is one instance of a decision another issue centralises takes that issue's priority, and is planned with it.

## Worked examples (2026-11-16 ranking)

- A red Windows lane where a fault escapes the handler: **P0** (ci-red).
- The compiler panics on a generic alias inside a generic type: **P0** (crash on a valid program).
- Interface values may cross processors with non-atomic counts, unverified: **P0** (suspected soundness; the repro is the first step).
- A struct with 65,536 fields takes ~100 s against ~1 s for its neighbours: **P1** (superlinear).
- The next step of the hashing design's queue: **P1**.
- A wrong refusal of `BidirectionalIterator with Int`: **P2**.
- E3005 naming a type by an unrelated library alias: **P2** (misleading diagnostic).
- Short-string hashing at 1.6x Go, with no regression: **P3**.
- Six hand-written copies of one error string: **P3** (duplication).

## When to ask

Send it to the `planner` when two levels both fit and the choice changes what runs next. That means a
P0/P1 call on a suspected soundness or scaling issue, or a P1 that depends on reading a design queue.
Otherwise decide, set it with `node .claude/roadmap/status.mjs <n> <status> <P>`, and move on.
