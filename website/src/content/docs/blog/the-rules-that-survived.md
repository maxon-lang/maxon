---
title: The Rules That Survived
description: Ten months of writing a compiler with AI agents — the test format that carried it, the rules file that had to be cut to nothing and rebuilt, and the thirteen skills we deleted.
date: 2026-09-18
authors: maxon
tags:
  - philosophy
  - process
excerpt: Maxon was written by AI agents, which means something had to govern them. What governs them is not a prompt — it is a set of rules in the repository, and every rule in it was bought with a failure.
---

Maxon's compiler was written by AI agents. That is the easy sentence. The harder question is what
governed them, because "write a compiler" is not a thing you can ask for and then walk away from —
and what ended up governing them is not a prompt. It is a set of rules committed to the repository,
which took ten months and roughly 5,800 commits to find.

Almost none of those rules are principles anyone thought up in advance. Each one is a failure that
happened, written down so it could not happen twice.

## Specs are tests

This is the part we would keep if we had to throw out everything else.

Every language feature is one markdown file in `specs/`. Frontmatter, prose documentation, then the
test cases — as fenced code blocks with the result they must produce:

````markdown
# abs

## Documentation

Calculate the absolute value of a number.

**Signature:** `abs(x float) float`

## Tests

<!-- test: abs.float -->
```maxon
function main() returns ExitCode
	let x = abs(-5.5)
	return trunc(x)
end 'main'
```
```exitcode
5
```
````

There are 522 of those files holding 8,348 executable cases, and `maxon spec-test` runs all of them.
The format is doing four things at once, and all four matter.

**The documentation and the test are the same artifact**, so they cannot drift apart. An agent that
changes a behaviour without updating the prose four lines above the case is editing a file where the
contradiction is visible in one screen. There is no separate documentation to forget.

**The spec is also the prompt.** When an agent needs to know what `abs` does, it reads one file and
gets the semantics, the edge cases *and* the exact expected outputs, without reading a line of the
compiler. The specification is written for the thing that will implement it.

**It turns "I fixed it" into something falsifiable.** A model's claim about its own work is worth
nothing; the run outranks the reading, always. Specs are what make that rule actionable rather than
merely true. One bad week taught us the sharper version — *a correction with no test is a claim.* A
documentation sweep replaced one false sentence with a differently false one, and the whole tree
stayed green, because nothing in a tree fails over a sentence. Now the sentence lives next to a case
that runs.

**And the set goes red first.** The `/land` skill starts a change by choosing the minimal set of
cases the change should affect and watching every one of them fail before a line of the fix is
written. A test that has never failed proves nothing, and an agent asked for a passing test will
happily produce one that could not fail if the feature were deleted.

Everything else in this post is scaffolding around that suite.

## The rules file is a working set, not a document

`.claude/CLAUDE.md` — the file every session reads — has been revised 107 times. Its line count tells
the story better than we can:

```
101 → 190 → 62 → 23 → 111 → 279 → 362 → 330
```

It grew as lessons got bolted on, became unusable at 190, and was cut to 23 lines. Then it grew back
— but the second time the long procedures went into skills, and the file kept only what binds every
tree in the repository. It sits around 330 lines and gets trimmed most weeks.

The discipline behind the curve is simple: everything in that file is context tax on every single
session, including the ones it has nothing to do with. A rule earns its place or it goes. Rules that
apply to one area live with that area — the compiler has its own `CLAUDE.md`, and the directories
that are compiler work import it rather than restating it.

## Thirteen skills died

Deleted over ten months: `implement-feature`, `incremental-dev`, `fix-spec-tests`, `run-spec-tests`,
`spec-writing`, `spec-port`, `rung`, `maxon-planner`, `selfhosted-dev`, `compiler-pipeline`,
`maxon-style-guide`, `maxon-compiler`, `tighten-comments`.

Eight survive: `/land`, `/code-review`, `/documenter`, `/optimize`, `/compiler-workflow`,
`/maxon-coder`, `/release` and `/fannkuch-iterate`.

Most of the deaths share a cause: **duplicated instructions rot exactly like duplicated code.** For a
while there were three subagent definitions describing a workflow that had been retired months
earlier, and agents kept faithfully following the dead process, because it was still written down and
nothing in the tree contradicted it. The commit that deleted them states the rule that replaced them
— *`CLAUDE.md` carries each fact once.*

The other cause is finer. A skill that merely describes a tool (`run-spec-tests`) is a worse version
of the tool's own help text, and it goes stale the moment a flag changes. A skill that carries a
*procedure with judgement in it* — what to do when the suite is red, what must never be edited to
make it green — is the only kind worth maintaining.

## What one change looks like

`/land` is the process for compiler work, and it is the whole workflow in one file. One invocation is
one change, taken end to end and pushed as a single commit:

0. Start — read the tree, decide what the change actually is.
1. Pick the **minimal spec set** and watch it go red.
2. Write the code, delegated to an implementer agent.
3. Green the set on a filter.
4. The optimization pass — with the scaling ladder read by the coordinator, never the agent.
5. The review, in a **different agent than the one that wrote the code**.
6. Document the change — the `/documenter` skill, which is the only step in the entire project
   allowed to write a comment. Nobody writes comments while coding, because code changes shape while
   it is being written and a comment written early is rewritten four times and mostly dies.
7. Rebase on `origin/main`.
8. **The battery** — the full suite, the wasm lane and the self-compile, run once, on the rebased
   tree.
9. Commit and push.

Two prohibitions do most of the work, and both exist because the opposite was tried.

**There is no escalation.** The process was chosen deliberately by the person who invoked it, and an
agent may not decide, on seeing how big the work is, that something heavier is called for. Every
excuse has been offered at least once: it touches several passes, it needs new IR ops, it deserves a
plan, it will take hours. The repository settles all of them in one line — *there are no time
constraints; complexity doesn't matter; if you are fixing an issue then fix it properly.*

**There is no slicing.** One change, one chunk, one commit. A task cut into four is not four small
landings; it is one landing done four times, badly — four rebases, four full suites, four wasm lanes,
four self-compiles, for the same code. And because there is one tree and one integrator, a slice is
not a smaller unit of work. It is an unintegrated one that reaches `main` half-finished.

What an agent *may* do is stop and ask. The halt list is short and every entry is a question, never a
reroute: a gate is red and the fix is not yours; the specs and the compiler disagree about what is
correct; a case would have to be disabled or an existing expectation rewritten. That middle one
carries its own warning — *never edit a spec to match the compiler*, because that is precisely how a
compiler bug becomes a specification.

The skill closes by naming the thing it exists to prevent: **a change that is green because nothing
tested it.**

## Every surviving rule is a scar

A few of the laws now sitting in the repository, with the failure that bought each one:

- ***A gate that did not run is not a pass.*** Three separate near-misses where a lane panicked
  before executing a single test and reported success.
- ***Three runs cannot tell a race from a rule.*** A flake that reproduces about 4% of the time reads
  as perfectly deterministic in a three-run sample. Sweep twenty or more, and report `n of m`.
- ***A runtime change needs two self-compiles.*** The compiler emits its own runtime into every
  program it builds, including into itself — so the next generation has the fixed emitter but an
  embedded runtime produced by the old one. Measured, painfully: a case failed 3 of 3 against the
  first build and passed 3 of 3 against the second, and for a while it looked like a
  platform-specific bug.
- ***A cross-day CPU baseline is not a measurement.*** A "10% regression" turned out to be drift
  between two days on the same machine. Interleave the A and B runs in one session, or do not report
  a number.

And the one that generalizes furthest: **never ask a model to make a judgement you could turn into a
command.** That two-self-compiles rule was originally phrased as a question — *does the compiler you
built with predate the runtime change?* Nobody can answer that reliably in their head, so the safe
answer was always "do it twice," at ninety wasted seconds on every task that touched the compiler.
Now `scripts/self-compiles-needed.sh` prints `once` or `twice`, and why, by diffing against the commit
stamped into the binary. Every rule phrased as a question to the model eventually gets answered
wrong; a rule phrased as a command gets run.

## Why this is the same bet as the language

[Maxon's design](/blog/you-arent-going-to-write-it/) starts from the premise that *you* aren't going
to write it — so the code has to answer for itself, and what is scarce is not keystrokes but
confidence that the code does what it claims. Everything above is that same bet applied one level up,
to the process instead of the syntax. Ranged types and `try … otherwise` put the answer on the page
where a reviewer needs it; the spec suite and the gate battery put the answer on the page where a
*coordinator* needs it.

Both of them exist because the alternative is trusting a claim. The compiler
[compiles itself](/blog/maxon-compiles-maxon/) and reproduces itself byte for byte, and that is not a
statistic for a README. It is the one check that cannot be satisfied by a model reporting success.

Everything here is in the repository, and the rules are not documentation about the project — they
are part of it. [`.claude/CLAUDE.md`](https://github.com/maxon-lang/maxon/blob/main/.claude/CLAUDE.md)
is what every session reads;
[`.claude/skills/land/SKILL.md`](https://github.com/maxon-lang/maxon/blob/main/.claude/skills/land/SKILL.md)
is the workflow above in full; [`specs/`](https://github.com/maxon-lang/maxon/tree/main/specs) is the
suite that decides whether any of it worked.
