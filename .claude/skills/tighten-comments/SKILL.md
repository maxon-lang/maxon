---
name: tighten-comments
description: Rewrite ONE source file's comments to conform to the project's comment rules — concise, why-only, present state only. Deletes history, measurements, dates and rung IDs; preserves every live constraint. Comments only — not one byte of code changes.
---

Rewrite the comments in ONE file. The path is your argument, optionally with a `<start>-<end>` line
range for a file too large to hold at once. **Never a directory sweep** — if given no path, ask.

This skill is temporary scaffolding for a one-off campaign and is deleted when the queue empties. It
states its rules in full rather than pointing at `.claude/CLAUDE.md` and `docs/STYLE_GUIDE.md`, so
there is no document you have to go and open first. Those two remain the permanent copy.

## ⛔ Two refusals, before anything else

**1. Not one byte of code changes.** Not a rename, not a reordered argument, not a whitespace fix
inside a statement. Found a bug, or a comment describing a shape the code no longer has? The fix is
to the **comment**; report the bug and leave the code alone. Deleting comments cannot break a build —
that is exactly what lets you delete freely, and it is true only while you delete nothing else.

**2. Generated files are refused outright.** Rewriting one is reverted by the next `generate` run
*and* fails `maxon error-codes check` on drift. Refuse and stop if the path is
`maxon-shv2/Compiler/ErrorCodeRegistry.maxon`, `maxon-shv2/Compiler/Runtime/SlabClasses.maxon`,
`maxon-sharp/Compiler/ErrorCode.g.cs`, `maxon-selfhosted/Compiler/ErrorCodeRegistry.maxon`, or if the
file's first 20 lines say `GENERATED` or `DO NOT EDIT`.

Three things are never edited: **`///` doc comments** on exported functions (`maxon run` prints them
in its command listing — they are program output; tighten their prose, never delete them); a `//`
**inside a string literal**, which is not a comment; and anything the lexer treats as a token.

## The rules

Binding on every comment you touch.

- **Minimal and concise.** The default is **no comment**. Write one only where the code cannot carry
  the point by itself, and then in as few words as it takes. A comment per line, a banner over every
  section, and a restated signature above a function are all noise — and noise is unverified prose
  that rots while the code keeps working.
- **"Why", never "how".** The code is the "how". Comment what the reader cannot recover from it: the
  constraint, the invariant, the reason this order/bound/branch is correct, the cost that motivated
  an unobvious shape.
- ⛔ **NO HISTORY. Describe the CURRENT state only.** No "used to", "previously", "changed from",
  "renamed", "now that we…", "this was a workaround for…", no dated narration of an edit, no
  reference to a former name. Git holds the history; a comment holds the present. The reason a guard
  exists is a *why* and belongs — but state the constraint that still binds ("callers may hand this
  an unsorted list"), never the edit that introduced it.
- **Editing a comment means REWRITING it to conform** — or deleting it. Never leave a conforming edit
  inside a non-conforming comment.

The Maxon form of the same rules:

```maxon
// Bad: restates the code
var i = 0  // set i to zero

// Bad: narrates the block below
// Loop over the items and add them up

// Good: the reason this bound is correct, which the code cannot say
// Callers pass unsorted ids, so the scan cannot early-exit.

// Bad: history
// We used to hash the name here; switched to the interned id in the parser rewrite.

// Good: the invariant that still binds
// Keyed by interned id — two spellings of one name must land on the same entry.
```

Use `//`, or `/* … */` for a block. A comment-only block is still an empty block (E3082).

### Five campaign rulings

1. **Zero historical reference of any kind.** Present state only.
2. **Measurements, dates, incident reports and gate output are DELETED** — not compressed, not
   relocated. `MEASURED 2026-08-20`, `10.8 ms of a 227.8 ms compile`, `expected 52, got 1`, a named
   past bug. Git and `docs/optimization-log.md` hold them. **Keep the rule the measurement
   established; drop the measurement.**
3. **Rung/slice IDs are stripped** — `(A1s wave 2)`, `(N1a)`, `(SV1)`, `(W95)`, `(P1.7 slice 3)`,
   `R-1`, `EC6`. The board they indexed is retired; `git blame` recovers the commit.
4. ⛔ **NEVER NAME THE OTHER COMPILERS. Where code CAME FROM is history.** No `bootstrap`, no
   `maxon-sharp`, no `v1`, no `maxon-selfhosted`, no C# file/line citation (`2-Parser.cs:4955`), no
   "ported from", "the same reason X does it", "inherited from". A comment describes THIS code.

   ⚠ **But a large share of those references encode a LIVE constraint, and deleting the whole
   comment breaks the suite.** A diagnostic's wording is often compared BYTE-FOR-BYTE by a spec
   golden. Strip the provenance; keep the constraint, restated in terms of what binds NOW — which
   reads better anyway, because it no longer needs the reader to know what a bootstrap is:

   - ❌ `Byte-for-byte the bootstrap's sentence (2-Parser.cs:4955-4957), pinned by top-level-let.md`
   - ✅ `Pinned byte-for-byte by specs-shv2/top-level-let.md — changing this wording fails the suite.`
   - ❌ `The bootstrap has the identical unbounded recursion in the code this was ported from`
   - ✅ *(clause deleted — the constraint above it stands on its own)*
   - ❌ `the same reason the bootstrap keys its stdlib cache per target`
   - ✅ *(clause deleted — the reason this memo carries the target is already stated)*

   ⇒ **Ask of every such reference: does it bind, or does it explain an origin?** A gate that will
   fail binds — name the gate. An origin goes, with nothing left in its place.

5. **Aggressiveness is HARD — the default is no comment.** A paragraph survives only by matching the
   allow-list, and then only at the length the fact needs. It never survives for being interesting,
   well-written, or hard-won. A twelve-line ordering argument that genuinely needs twelve lines keeps
   them; the same argument stated in four keeps four. Expect most declarations to end with NO comment
   and the file's comment ratio to fall by roughly half.

## Method: an ALLOW-LIST. Start from ZERO and re-add what qualifies.

⛔ **You are not deleting bad comments. You are rebuilding the file's commentary from nothing.**
That inversion is the whole method, and it is not a figure of speech.

Take one declaration at a time. **Read its code first, before you read its comments.** Then treat the
existing comments as SOURCE MATERIAL — a record of facts someone once knew — and ask of each fact,
one at a time:

> **Does this match a category on the allow-list below? If not, it is not re-added.**

There is no delete-list, deliberately. A delete-list must enumerate every bad shape, and the one it
forgets survives — measured: the deny-list version of this skill caught `"Split out of …"` and missed
`"Split into its own function so …"` two declarations later, because that spelling was not on it.
This is the same argument `tokenKindBelongsInATypeHeader` makes for scanning a type header with an
allow-list: *"the complement is every keyword in the language and forgetting one runs the scan into a
type BODY."* Under an allow-list, an unrecognized comment is simply never re-added, and no gap in any
enumeration can rescue it.

⇒ **The default is NO COMMENT, structurally.** A comment now has to be argued INTO the file. Most
declarations end with none. That is the correct outcome, not an over-correction.

### The allow-list — the only things that may be re-added (nine categories)

1. an **ordering constraint** — X runs before Y, and what silently goes wrong otherwise;
2. an **invariant or precondition** the signature cannot state;
3. **why a guard, bound or branch exists** — the input it refuses, the case it catches;
4. a **soundness argument resting on a fact outside this function** — a cache key that omits the
   target, sound only because a `Project` is aimed at one target for its whole life;
5. a **deliberate asymmetry** — two nearby sites doing different things on purpose;
6. a **cost reason for an unobvious shape** — "asked per token on a walk that visits every token, so
   the next probe is a compare rather than a scan";
7. a **gate that will fail** — a wording pinned byte-for-byte by a named spec file, a golden that
   compares this output. Name the gate, never where the wording came from (ruling 4);
8. a **pointer to the one place a rule lives** — "see `X`, which owns the rule". The pointer only,
   never a summary of what is there.
9. a **known unguarded failure mode of THIS code** — what it does not handle and what happens then.
   ⚠ **This category exists because the list without it lost one.** Category 3 covers why a guard
   EXISTS; nothing covered the ABSENCE of a guard, so a note that an initializer DFS has no depth
   bound and dies with SIGSEGV and no diagnostic matched nothing and was dropped — deleting the only
   record of a live crash path. A limitation of the code as it stands is present state, not history.

⚠ **A fact must be VERIFIABLE against the code to be re-added.** You are re-adding, never composing:
every surviving comment traces to text that was already in the file. **Invent nothing.** If a claim
cannot be checked and does not clearly match a category, drop it — a confident, fabricated invariant
is far worse than a missing comment, because someone will rely on it.

### A fact belongs on the declaration it describes

⭐ **A block that documents two declarations is split, and a block sitting above the wrong one is
moved.** Rebuilding from zero means each fact attaches to the declaration it is about, not to
whichever one it happened to precede. Measured in this campaign: one file had a large header about
`livesUnderStdlibDirectory` sitting above `programSignaturesIn`, which carried none of its own.
Moving comment text between declarations is authorized and expected; moving CODE is not, and the
mechanical check in Verification still proves you did not.

### Work the file in order

Build the table first, for the whole range — do not start editing and see how it goes:

| line | attaches to | facts present | which qualify (1-8, or NONE) | lines after |
|---|---|---|---|---|

Naming the fact before you decide is what turns "this paragraph feels long" into "this paragraph
asserts one thing, and here it is in nine words". **A block you cannot summarize is a block you do
not yet understand** — go read the code again before you judge it.

Work in declaration-aligned windows of 300-400 lines. Keep a running list of facts already re-added:
a fact belongs at ONE site, and the second site gets a pointer or nothing. **Duplication is a
finding, exactly as it is for code** — one rule in three paraphrases is three things free to drift.

⚠ **Batch your edits.** One `Edit` per comment block at most, and prefer one per contiguous run of
blocks. Forty small edits cost roughly twice what ten large ones do, because every call re-sends the
accumulated context.

## Rewriting what survives

- Present tense, declarative, third person. State what is true now.
- One idea per sentence. No paragraph whose only job is to set up the next one.
- **No markdown** — no `**bold**`, no `*italics*`, no `>`. Backticks around identifiers stay; they
  are idiomatic here and they carry information.
- **No `⭐`.** At most one `⚠` per block, only where the hazard is a *silent wrong answer* rather
  than a compile error. Never doubled.
- No ALL-CAPS runs. A single capitalized word for emphasis, rarely.
- **Wrap at 100 columns** (tab counted as one). Not "the file's column" — the pilot left 34 lines
  over 105. Tabs for indentation, matching the code the comment attaches to.
- ⚠ **Never invent a reason.** If a claim cannot be verified from the code, preserve its substance
  verbatim or delete the block — never rephrase into something the code does not support. **A
  rewritten comment that is subtly wrong is worse than the bloated one it replaced.**

The register to aim at — real comments from `maxon-shv2/Compiler/Lsp/`:

```maxon
// LSP stdio framing: `Content-Length: <n>\r\n\r\n<body>` in both directions.

// The one header this server reads, lowercased: LSP header field names are case-insensitive.

// Parsing and serializing JSON is `stdlib/Json.maxon`'s job and is not repeated here; this file owns
// only the members JSON-RPC and LSP define, and the rules for filling them.
```

## Three worked examples

**A. Re-added under category 1 (ordering constraint) — the rule qualifies, its evidence does not.**

```
BEFORE (8 lines)
// ⛔⛔ **IT IS ASKED *BELOW* THE TOKENS QUERY, NOT ABOVE IT, AND THAT ORDER IS THE WHOLE
// INCREMENTALITY PROPERTY.** Short-circuiting here — answering the filtered stream without ever
// asking for the raw one — leaves THIS project's `tokenCache` empty for every file the store
// answered for, so the next re-query of an unchanged file lands as a MISS. MEASURED, and it is the
// gate that caught it: `verify-warm-rebuild` reported
// `rebuild token re-query hits (tokenHits): expected 52, got 1` and
// `rebuild does not recompute tokens (tokenMisses): expected 0, got 52`. Nothing is re-LEXED either
// way; what moves is whether the per-`Project` spine stays whole, and it must.

AFTER (2 lines)
// Asked below `queryTokens`, not above it: a shared hit that skipped it would leave this project's
// `tokenCache` empty, so the next re-query of an unchanged file would miss.
```

**B. Nothing qualifies — a restated signature plus an extraction rationale.**

```
BEFORE (3 lines, over `function foldOneFileInto(...)`)
// Sweep ONE file's declarations into the index. Split out of `queryProgramSignatures` when the sweep was
// partitioned by provenance, so the two halves are one body rather than two copies of it — and so the
// `isStdlibSource` bit each half already knows is PASSED rather than re-derived per file.

AFTER
(nothing)
```

Sentence 1 restates the name. The rest is *why the function was extracted* — an edit, not a
constraint. That the parameter is passed rather than re-derived is visible in the signature.

**C. Re-added under category 4 (soundness argument) — length is what the fact needs, nothing more.**

```
BEFORE (11 lines)
// ⚠ **THE FILTERED STREAM IS TARGET-DEPENDENT AND THIS MEMO'S KEY IS NOT, WHICH IS SOUND ONLY
// BECAUSE A `Project` HAS EXACTLY ONE TARGET FOR ITS WHOLE LIFE.** `Project.target` is a `let` set
// by `Project.create` and re-aiming a live `Project` does not compile (Project.maxon says so at the
// field, which is the line that would have to change first), and this cache lives on `project.db` —
// so two targets are two `Project`s and two databases, and one cache can never be asked a question
// about a target it was not filled for. Should a `Project` ever need re-aiming, THIS memo's key is
// what becomes wrong: mix the target into it, do not simply invalidate.
//
// ⚠ **THE PROCESS-WIDE STORE BELOW HAS NO SUCH GUARANTEE AND SO ALREADY CARRIES THE TARGET IN ITS
// KEY** — it outlives every `Project`, which is exactly the premise this paragraph rests on. See the
// second store lookup in the body.

AFTER (5 lines)
// ⚠ The filtered stream is target-dependent and this memo's key is not. That is sound only because a
// `Project` is aimed at one target for its whole life: `Project.target` is a `let` and this cache
// lives on `project.db`, so two targets are two `Project`s and two databases. If a `Project` ever
// needs re-aiming, this key is what becomes wrong — mix the target in, do not merely invalidate.
// The process-wide store has no such guarantee and already carries the target in its key.
```

Every constraint survives, because each is a category-4 fact. What goes is the shouting, the bold,
and the two clauses that only assert the paragraph's own importance. Note this is the SHAPE of a long
survivor under HARD: it is long because the soundness argument has that many steps, not because the
original was long.

## Verification — all four, in order

1. **Read every hunk of `git diff -- <file>`.** Each `-`/`+` line must be a whole comment line, a
   blank line, or a code line whose text before the `//` is byte-identical.
2. **Mechanical code-identity proof.** Strip comments from both versions and compare:
   ```
   mkdir -p temp
   git show HEAD:<file> | sed 's://.*::; s/[[:space:]]*$//' | grep -v '^$' > temp/tc-a
   sed 's://.*::; s/[[:space:]]*$//' <file>                  | grep -v '^$' > temp/tc-b
   diff temp/tc-a temp/tc-b        # MUST be empty
   ```
   ⚠ **The `grep -v '^$'` is load-bearing** — stripping a comment-only line leaves a blank one, so a
   correct DELETE always shows in the diff without it, and you would be forced to explain away a
   failure you should never see. A deleted CODE line is still caught: stripped of its comment it is
   non-blank. Both sides get identical mangling of any `//` inside a string. Put this output in your
   report verbatim; **an empty diff is the only acceptable result** — do not argue around a non-empty
   one.
3. **`mcp__maxon-dev__fmt`, the file form** (name the file — with no path it formats the whole
   directory). Proves the file still lexes and the formatter's line-keyed comment map still resolves.
   ⚠ A file `fmt` cannot lex is left byte-identical and reported `unchanged` — the same word it uses
   for an already-canonical file — so `unchanged` alone proves nothing. Step 4 is what proves it
   parses.
4. **The build**, unless your caller says they are batching it: `mcp__maxon-dev__build` with
   `target: "shv2"` for `maxon-shv2/**`, `target: "csharp"` for `maxon-sharp/**`. ~4 min.

The spec suite is not run — a comments-only edit cannot move it, so it is confirmation, not
detection.

## Report

- The inventory table, plus comment-line count before → after for the range.
- **Every fact NOT re-added, one line each.** This is the most important section. Under an allow-list
  the omissions are the bulk of the work and they leave no trace in the file — this list is the only
  place a wrongly-dropped constraint becomes reviewable, and your caller spot-checks it.
- **Every comment you DID re-add, with its category number (1-8).** A re-added comment that matches
  no category is the failure this method exists to prevent; if you cannot number it, it should not be
  there.
- Any comment whose claim you could not verify against the code — named, with what you did.
- The step-2 `diff` output verbatim, and the step-3/4 results.
- Bugs and stale claims you found and did **not** act on.

Do not update the campaign queue — your caller owns it. Never claim a check passed unless you ran it.
