---
name: file-issue
description: File a defect or planned work on maxon-lang/maxon, from a worker's FOUND or your own finding. Checks for an existing issue that already records it, decides whether it is one instance of a class another issue owns, chooses its priority (P0-P3), then files it with `.claude/roadmap/file.mjs`. Also the procedure for an issue found on the Roadmap without a priority. Invoke as `/file-issue <the report, or an issue number>`.
---

# File an issue

A filing is three judgments, then one command. Do them in order: a duplicate filed with a priority still
costs a worker a task.

## 1. Is it already recorded?

Search open issues, and closed ones from the last few weeks, for its subject, not its wording:
- the function, pass or file it names (`gh issue list -R maxon-lang/maxon --search "<name> in:body" --state all`);
- the diagnostic code;
- the symptom in plain words ("leak", "panic", "exit 101", "wrong value").

The same defect seen again, even from another program, is a **sighting**: add it to the existing issue
as a comment (the new repro, and where it was seen), and reply with that issue's number. A closed issue
that the new report shows is not fixed is reopened, with the report as its comment. Only a defect with
its own mechanism gets a new issue.

## 2. Does it belong to a class?

Ask whether an open issue already owns the decision this defect gets wrong. That means one predicate, one
table or one rule that several sites make, such as #18 (layer and origin decided by name) or #45
(consequential refusals).
- If one does, file the new issue as an instance and say so in its body ("an instance of #<n>'s class;
  plan it with #<n>"), and give it that issue's priority.
- If the report shows a new class (the same mistake at several sites, with no owner), say that in the
  body. The `planner` reviews it before anything is assigned.

## 3. Choose its priority

Every open issue on the Maxon Roadmap carries one `Priority`. Work is taken from the highest level
first, so the priority decides what the fleet does next. The tools refuse an issue without one
(`file.mjs`, `status.mjs`).

### The four levels

| Level | Meaning | Ask |
|---|---|---|
| **P0** now | a red CI lane; a soundness hole (a program the compiler accepts that can corrupt memory, alias across threads, or show a wrong value); wrong output from compiled code; a compiler crash or panic on a valid program | Is main red, or can a correct program go wrong because of this today? |
| **P1** next | blocks other planned work; is the next step of an accepted design's queue; a compile-time or memory regression that breaks the linear-scaling contract | Is something else waiting on it, or does a ladder bend? |
| **P2** normal | an ordinary bug; a wrong refusal; a misleading diagnostic; an enhancement whose design is settled | Is it a real defect that nothing waits on? |
| **P3** later | cleanup; duplication; docs drift; tooling; performance work with no measured regression | Would the language behave the same if it waited? |

### Deciding

1. **Take the highest level that is true.** A wrong refusal that also blocks a design step is P1, not P2.
2. **Unverified suspicions:** a suspected soundness hole is P0 until its repro confirms or refutes it, because the repro is the first step. A suspected leak or miscompile with no repro yet is P2, and is raised to P0 when the repro shows it.
3. **Design issues** take the priority of their remaining implementation work, not of the design.
4. **Measured versus unmeasured performance:** a superlinear ladder is P1; a constant-factor slowdown with no regression is P3; a measured cost of a few percent with a decided fix is P2.
5. **A debugger defect** that can show a wrong value is P0 only when it is shown on a real program. Found by reading, it is P2.
6. **Instances of a class:** an issue that is one instance of a decision another issue centralises takes that issue's priority, and is planned with it.

### Worked examples (2026-11-16 ranking)

- A red Windows lane where a fault escapes the handler: **P0** (ci-red).
- The compiler panics on a generic alias inside a generic type: **P0** (crash on a valid program).
- Interface values may cross processors with non-atomic counts, unverified: **P0** (suspected soundness; the repro is the first step).
- A struct with 65,536 fields takes ~100 s against ~1 s for its neighbours: **P1** (superlinear).
- The next step of the hashing design's queue: **P1**.
- A wrong refusal of `BidirectionalIterator with Int`: **P2**.
- E3005 naming a type by an unrelated library alias: **P2** (misleading diagnostic).
- Short-string hashing at 1.6x Go, with no regression: **P3**.
- Six hand-written copies of one error string: **P3** (duplication).

### When to ask

Send it to the `planner` when two levels both fit and the choice changes what runs next. That means a
P0/P1 call on a suspected soundness or scaling issue, or a P1 that depends on reading a design queue.

## 4. File it

Write the body in a file in your scratchpad, using `/land` §10's "Recording" fields (Where, Seen,
Repro, Expected), plus the class line from step 2 when it applies. Keep fleet details out: no session,
machine, account or usage figure. Then:

```
node .claude/roadmap/file.mjs --title "<the defect, stated as the wrong behaviour>" --body-file <path> --label bug --label accepted [--label compiler|runtime|…] --priority P<k>
```

An accepted bug or design goes on the Roadmap with its priority, and any other issue lands on the
Backlog. A hook refuses a raw `gh issue create` on this repository, so this is the only way to file.
Reply to the reporter with the number, and do not assign the issue to it mid-task.

**An issue already on the Roadmap without a priority** (wake step 5) skips steps 1 and 4: do step 2,
choose its priority in step 3, and set it with `node .claude/roadmap/status.mjs <n> <its status> <P>`.
