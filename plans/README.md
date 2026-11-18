# Class plans

Each file here plans one class of defects that share a mechanism: the design that removes the class, and the
ordered stages that land it. The class issue on GitHub holds the same plan and the folded member issues; the
file is what a session works from.

## Working a plan

The `work-plan` skill is the procedure (`/work-plan <plan>`, or `/work-plan <queue>` for that queue's next
plan). In short:

- **Two queues, A and B, one session each.** A session works only its own queue, in turn order, so no plan
  is claimed.
- **One session works one plan, stage by stage,** in the order its "Stage order" table gives. A stage is one
  local commit that ticks its box; nothing is pushed between stages.
- **A stage starts only when everything it waits on is done:** a stage of this plan ticked, a stage of
  another plan landed with that plan, an issue closed.
- **The plan lands once, after its last stage:** its stage commits are squashed into one commit that
  marks the plan's "Done" cell below and carries `Fixes #<class issue>`. Comments and documentation are written then, on the plan's final shape.
- **An open question takes the plan's recommendation,** recorded under the plan's "Rulings".
- **`deferred`** marks a stage, or a whole plan, that is left out of the queues: it is skipped, and its class
  issue stays open for it.
- "Not beside" names stages that change the same files: whichever plan lands second rebases over the first.

## The plans

| Plan | Class issue | Priority | Stages | Queue | Turn | Done |
|---|---|---|---|---|---|---|
| [ownership](ownership.md): Every fact about a value rides one record: written where the value is minted, merged or carried round a loop, and read by every ownership and region rule | #309 | P0 | 12 | A | 1 |  |
| [declaration-identity](declaration-identity.md): Every declaration has one resolved identity (origin, layer, owner, kind): no lookup, key or message rebuilds it from a spelling | #18 | P1 | 9 | A | 2 |  |
| [kept-answers](kept-answers.md): The compiler answers every by-name, by-type and per-compile question from one kept, keyed answer, and a ladder per program dimension shows it | #308 | P1 | 11 | A | 3 |  |
| [determinism](determinism.md): Compiler output, caches, allocation counts and reports are a function of the program: one environment gate, one source order key, and a table walk that names its order | #311 | P1 | 7 | A | 4 |  |
| [constants](constants.md): A constant is declared once; a copy across a boundary no name crosses is sealed, read or pinned, and a gate refuses any other | #53 | P2 | 3 | A | 5 | deferred |
| [unused-methods](unused-methods.md): maxon lint reports a method nothing uses, and its spelling census counts only what can name a declaration | #69 | P2 | 3 | A | 6 | deferred |
| [generics](generics.md): A generic body is judged once, from its bounds alone, and every instance the program writes or infers is judged once at one door | #310 | P0 | 8 | B | 1 |  |
| [map-string-cost](map-string-cost.md): A stored element has one door per operation, reached alike from concrete, generic and library code: Map, String and hash cost what Go's do | #82 | P1 | 10 | B | 2 |  |
| [structural-equality](structural-equality.md): One verdict per type supplies equals, compare, hash and clone, and every door reads it; each Map and Set hashes under its own seed | #312 | P1 | 8 | B | 2 |  |
| [runtime-entries](runtime-entries.md): A runtime entry is one row of one table, read by the usage scan, the target check, the installers and the stdlib gates: no fact about it comes from its name's prefix | #153 | P1 | 5 | B | 3 |  |
| [debug-rows](debug-rows.md): A local's debug facts are ops the passes carry: a version sits at its assignment, a scope end in its block, and a value id is minted once per function | #145 | P2 | 5 | B | 4 |  |
| [debug-host](debug-host.md): Every operation on a held program's lifecycle answers a typed outcome: a release is refused by name while a stop the session sent is uncollected, and no wait for a program's end expires in silence | #313 | P2 | 3 | B | 5 |  |
| [region-model](region-model.md): Every region-model corpus program has one checked-in fate: an agreeing spec case, an owned ledger row or a counted skip, and the translator writes only what the model judged | #248 | P2 | 6 | B | 6 |  |

Queues A and B run side by side, one session each; a queue takes its plans in turn order.
`map-string-cost` and `structural-equality` are one queue for one session: their stages wait on each other.

## Order across plans

- [map-string-cost](map-string-cost.md) 7 waits on [structural-equality](structural-equality.md) 4.
- [map-string-cost](map-string-cost.md) 8 waits on [structural-equality](structural-equality.md) 4.
- [map-string-cost](map-string-cost.md) 8 waits on [structural-equality](structural-equality.md) 5.
- [structural-equality](structural-equality.md) 2 waits on [map-string-cost](map-string-cost.md) 2.
- [kept-answers](kept-answers.md) 7 waits on [generics](generics.md) 7.
- #37 waits on [declaration-identity](declaration-identity.md) 1 and 5.
- [ownership](ownership.md) E1 and [generics](generics.md) 3 both change `finishValueTry`: whichever lands second builds on the first.
- [declaration-identity](declaration-identity.md) 1 changes the signature index widely: no other front-end stage runs beside it.
