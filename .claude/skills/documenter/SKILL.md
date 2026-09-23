---
name: documenter
description: Author a finished change's comments and documentation in one pass, immediately before the commit — the ONLY step that writes comments, because code is written with none. Minimal and concise: three gates and an allow-list, and most declarations end with no comment. Also updates the `docs/` source the change's surface owns and regenerates the site. Comments and docs only — not one byte of code changes. Invoke as `/documenter`.
---

# Document one finished change

**Code in this repository is written with NO comments.** This skill is where every comment comes from,
and it runs once, on the finished diff, immediately before the commit.

**Why the split:** code changes shape while a change is being written, so a comment written mid-flight
is written and rewritten several times and most of that prose never reaches the commit. Only the final
shape of the code is worth commenting, and that shape does not exist until the change is done.

⚠ **That is not a licence to write more now.** Running last means holding the whole change in context,
which is exactly what makes this step prone to over-writing. The output is **minimal and concise**: the
default is no comment, and most declarations in the diff end with none.

**Argument:** none = the working diff against `HEAD` (`git status --short`, then `git diff`). Optionally
a revision range or an explicit list of paths.

## ⛔ Refusals, before anything else

**1. Not one byte of code changes.** Not a rename, not a reordered argument, not a whitespace fix inside
a statement. Found a bug, or a comment describing a shape the code no longer has? The fix is to the
**comment**; report the bug and leave the code alone. Deleting and adding comments cannot break a build
— that is what lets you work freely, and it is true only while you change nothing else.

**2. Generated files are refused outright.** Rewriting one is reverted by the next generator run *and*
fails that generator's own drift check — for `SlabClasses.maxon` that is
`scripts/gen-slab-classes.sh --check`. Refuse the path
`maxon-bin/Compiler/Runtime/SlabClasses.maxon`, and any file whose first 20 lines say `GENERATED` or
`DO NOT EDIT`; the second test is the one that holds as generators are added.
⚠ **`maxon-bin/Compiler/ErrorCodeRegistry.maxon` is NOT one of these.** It is hand-authored, has no
generator, and the `//` block above each case is the text `lookup_error_code` serves — it is a primary
work item here.

**3. `/specs/**` is read-only.** It is the canonical definition of the language.

**4. A generated site page is never edited by hand.** Everything under
`website/src/content/docs/docs/{cli,language,stdlib,spec,best-practices}/` is built from a source; edit
the source and re-run the sync.

A `//` inside a string literal is not a comment, and anything the lexer treats as a token is not one
either.

## The comment pass

> ### ⛔⛔ MINIMAL AND CONCISE IS THE OUTPUT CONSTRAINT, NOT A STYLE NOTE
>
> **The default is NO COMMENT, structurally.** A comment has to be argued *into* the file against the
> allow-list below. There is no delete-list, so anything matching no category is simply never written.
> **Expect most declarations in the diff to end with none, and a typical change to add between zero and
> a handful of lines.**
>
> ⛔ **You are not documenting the change.** The commit message does that, and the diff is already the
> record of what moved. You are leaving behind the few facts a future reader cannot recover from the
> code.
>
> **Length is what the fact takes and not one word more.** A twelve-line ordering argument that
> genuinely needs twelve lines keeps them; the same argument stated in four keeps four. A comment never
> earns its place by being interesting, well-written or hard-won — and a fact worked out painfully
> during the change is exactly the one that gets over-explained.

> ### ⭐⭐ THREE GATES, IN ORDER. EACH IS NECESSARY; NONE IS SUFFICIENT.
>
> **Gate 1 — ⛔ Does the code already say it? Then it is not written.**
>
> Not a paraphrase of it, not a summary of it, not a friendlier spelling of it. **The test: delete the
> comment and read the code. If the fact is recoverable from the signature, the types, the names, the
> guard's condition or the literal, it was never a comment.** The code is the one copy that cannot go
> stale; a second copy in prose is a second thing to maintain, and it stops being true silently.
>
> These are Gate-1 failures, and they are what gets written by default when nothing stops it: a
> restated signature above a function; a narration of the block below; a guard's condition re-spelled
> in English; a return described when the return type describes it; a named constant's name repeated as
> prose; a type restated.
>
> ⇒ **The comment starts where the code stops.** Nothing above that line goes in the file.
>
> **Gate 2 — Is it on the allow-list?** Nine categories read as nine invitations, and a diligent pass
> finds something on the list to say about almost any declaration. The list decides only what is
> *eligible*.
>
> **Gate 3 — What goes wrong if nobody ever knows this?**
>
> - "A reader works it out from the code in a minute" → **no comment.** True, non-obvious and on the
>   list is not enough. The bar is *consequence*, not *interest*.
> - "Someone changes this and gets a silent wrong answer, a leak, a broken gate, or a crash on one
>   target" → **write it**, and name that consequence, because it is the whole content.
>
> **Then rank and cut.** Where a declaration clears all three gates twice, one fact usually has the
> worst consequence: keep that one and drop the rest. Three comments around one declaration is how a
> file reaches 55% commentary one honest step at a time.
>
> ⇒ **Most eligible facts are still not written.** A pass writing everything that matched a category is
> not applying Gates 1 and 3.

### The rules

Binding on every comment written and every comment touched.

- **Minimal and concise.** The default is no comment. Write one only where the code cannot carry the
  point by itself, and then in as few words as it takes. A comment per line, a banner over every
  section, and a restated signature above a function are all noise — and noise is unverified prose that
  rots while the code keeps working.
- **"Why", never "how".** The code is the "how". Comment what the reader cannot recover from it: the
  constraint, the invariant, the reason this order or bound or branch is correct, the cost that
  motivated an unobvious shape.
- ⛔ **NO HISTORY. Describe the CURRENT state only.** No "used to", "previously", "changed from",
  "renamed", "now that we…", "this was a workaround for…", no dated narration of an edit, no reference
  to a former name. Git holds the history; a comment holds the present. The reason a guard exists is a
  *why* and belongs — but state the constraint that still binds ("callers may hand this an unsorted
  list"), never the edit that introduced it.
- **Touching a comment means REWRITING it to conform** — or deleting it. Never leave a conforming
  sentence inside a non-conforming block.

The Maxon form:

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

### Two rulings that decide most borderline cases

**1. Measurements, dates, incident reports and gate output are DELETED** — not compressed, not
relocated. `MEASURED 2026-08-20`, `10.8 ms of a 227.8 ms compile`, `expected 52, got 1`, a named past
bug. Git, `docs/optimization-log.md` and the commit message hold them. **Keep the rule the measurement
established; drop the measurement.**

**2. ⛔ NEVER RECORD WHERE CODE CAME FROM. Provenance is history.** No "ported from", no "the same
reason X does it", no "inherited from", no citation into a file outside this tree. A comment describes
THIS code.

⚠ **But many such references encode a LIVE constraint, and deleting the whole comment breaks the
suite.** A diagnostic's wording is often compared byte-for-byte by a spec golden. Strip the provenance
and keep the constraint, restated in terms of what binds now — which reads better anyway, because it no
longer needs the reader to know where the code came from:

- ❌ `Byte-for-byte the wording this was ported from, pinned by top-level-let.md`
- ✅ `Pinned byte-for-byte by specs/top-level-let.md — changing this wording fails the suite.`
- ❌ `The code this was ported from has the identical unbounded recursion`
- ✅ *(clause deleted — the constraint above it stands on its own)*

⇒ **Ask of every such reference: does it bind, or does it explain an origin?** A gate that will fail
binds — name the gate. An origin goes, with nothing in its place.

### The allow-list — the only things eligible (Gate 2)

1. an **ordering constraint** — X runs before Y, and what silently goes wrong otherwise;
2. an **invariant or precondition** the signature cannot state;
3. **why a guard, bound or branch exists** — the input it refuses, the case it catches;
4. a **soundness argument resting on a fact outside this function** — a cache key that omits the
   target, sound only because a `Project` is aimed at one target for its whole life;
5. a **deliberate asymmetry** — two nearby sites doing different things on purpose;
6. a **cost reason for an unobvious shape** — "asked per token on a walk that visits every token, so
   the next probe is a compare rather than a scan";
7. a **gate that will fail** — a wording pinned byte-for-byte by a named spec file, a golden that
   compares this output. Name the gate, never where the wording came from;
8. a **pointer to the one place a rule lives** — "see `X`, which owns the rule". The pointer only,
   never a summary of what is there;
9. a **known unguarded failure mode of THIS code** — what it does not handle and what happens then.
   Category 3 covers why a guard EXISTS; this one covers the ABSENCE of a guard, and a limitation of
   the code as it stands is present state, not history.

**There is no delete-list, deliberately.** A delete-list must enumerate every bad shape, and the one it
forgets survives: a deny-list caught `"Split out of …"` and missed `"Split into its own function so …"`
two declarations later, because that spelling was not on it. Under an allow-list an unrecognized fact
is simply never written, and no gap in any enumeration can rescue it.

### Two inputs, two rules

**For code the change wrote, you are COMPOSING.** A composed comment may state only a fact you can
point at — in the code itself, or in the diagnosis your caller's brief carries. ⛔ **Never a reason
inferred from the shape of the diff.**

**For a comment that already exists, you are RE-ADDING**, never rewriting freely: every surviving fact
traces to text that was already in the file.

⚠ **Both ways, a fact must be VERIFIABLE.** If a claim cannot be checked and does not clearly match a
category, drop it. **A confident, fabricated invariant is far worse than a missing comment**, because
someone will rely on it. If a claim's substance matters but cannot be verified, preserve it verbatim
rather than rephrasing into something the code does not support.

### Scope, and working in order

**The diff plus its neighbourhood, never the whole file.** For each file the change touched: every
comment block attached to a declaration the diff touched, and every comment block *inside* such a
declaration. A conforming comment three hundred lines away is not this pass's business.

**Build the inventory first, for the whole scope — do not start editing and see how it goes.** It
carries all three gates as separate columns so none can be skipped:

| line | attaches to | fact | in the code already? | category (1-9, or NONE) | what goes wrong if unknown | written? |
|---|---|---|---|---|---|---|

A `yes` in the fourth column ends the row; nothing after it is asked. Naming the consequence in its own
cell is what turns "this is a real invariant" into "nothing goes wrong, so nothing is written" — a
blank or hand-waving consequence cell is a `no`.

Naming the fact before deciding is also what turns "this paragraph feels long" into "this paragraph
asserts one thing, and here it is in nine words". **A block you cannot summarize is a block you do not
yet understand** — read the code again before judging it.

⭐ **A fact belongs on the declaration it describes.** A block documenting two declarations is split,
and a block sitting above the wrong one is moved. Moving comment TEXT between declarations is expected;
moving CODE is refused, and Verification proves you did not.

Keep a running list of facts already written: a fact belongs at ONE site, and a second site gets a
pointer or nothing. **Duplication is a finding, exactly as it is for code** — one rule in three
paraphrases is three things free to drift.

⚠ **Batch the edits.** One `Edit` per comment block at most, and prefer one per contiguous run of
blocks. Forty small edits cost roughly twice what ten large ones do.

### Writing what survives

- Present tense, declarative, third person. State what is true now.
- One idea per sentence. No sentence whose only job is to set up the next one.
- **No markdown** — no `**bold**`, no `*italics*`, no `>`. Backticks around identifiers stay; they are
  idiomatic here and carry information.
- **No `⭐`.** At most one `⚠` per block, only where the hazard is a *silent wrong answer* rather than a
  compile error. Never doubled.
- No ALL-CAPS runs. A single capitalized word for emphasis, rarely.
- **Wrap at 100 columns** (a tab counted as one). Tabs for indentation, matching the code the comment
  attaches to.

The register to aim at:

```maxon
// LSP stdio framing: `Content-Length: <n>\r\n\r\n<body>` in both directions.

// The one header this server reads, lowercased: LSP header field names are case-insensitive.

// Parsing and serializing JSON is `stdlib/Json.maxon`'s job and is not repeated here; this file owns
// only the members JSON-RPC and LSP define, and the rules for filling them.
```

### Four worked examples

**A. New code, nothing written — and this is the common case.**

```
The change extracts `foldOneFileInto` from `queryProgramSignatures` and passes the
`isStdlibSource` bit the caller already knows.

WRITTEN
(nothing)
```

Gate 1 stops it: the name says what it sweeps, the signature says what it takes. *Why the function was
extracted* is an edit, not a constraint, and belongs in the commit message. An ordinary refactor
yielding zero comments is a correct outcome, not a skipped step.

**B. New code, one comment — category 1, and the consequence is the content.**

```
The change adds a filtered-token query below the raw one in `Queries.maxon`.

WRITTEN (2 lines)
// Asked below `queryTokens`, not above it: a shared hit that skipped it would leave this project's
// `tokenCache` empty, so the next re-query of an unchanged file would miss.
```

An ordering constraint whose violation is a silent cache miss, not an error. Gate 3 is what earns it
the two lines; the measurement that found it stays in the commit message.

**C. An existing block, re-added under category 4 — long because the argument has that many steps.**

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

Every constraint survives, because each is a category-4 fact whose violation is a wrong answer. What
goes is the shouting, the bold, and the two clauses that only assert the paragraph's own importance.

**D. An existing block, nothing survives.**

```
BEFORE (3 lines, over `function foldOneFileInto(...)`)
// Sweep ONE file's declarations into the index. Split out of `queryProgramSignatures` when the sweep
// was partitioned by provenance, so the two halves are one body rather than two copies of it — and so
// the `isStdlibSource` bit each half already knows is PASSED rather than re-derived per file.

AFTER
(nothing)
```

Sentence 1 fails Gate 1. The rest is provenance and an extraction rationale.

### Two surfaces this pass AUTHORS, not merely tidies

Code is written with no comments, so these two arrive empty and are work items here. Neither is a code
comment, and the allow-list does not apply to them — they are prose for a reader outside this file.

1. ⭐ **A new `ErrorCodeRegistry.maxon` case with no doc comment.** The `//` block immediately above a
   case is the text `lookup_error_code` serves at runtime and the body of the generated Error Codes
   page. Write it for a compiler user who hit the diagnostic: what the compiler refused and what makes
   it legal. **`node website/scripts/sync-docs.mjs` fails closed on a case without one, so running the
   sync is how these are found.** ⚠ A blank line between the comment and its case orphans it and fails
   the same way.
2. **`///` doc comments on newly exported functions**, which the language server renders in
   `textDocument/hover` — what a reader sees in an editor. Tighten the prose of an existing one; never
   delete it.

## The documentation pass

**A user-visible change updates its documentation in the same commit** (`.claude/CLAUDE.md`).
User-visible means a command, flag or `maxon help` text; a diagnostic; syntax or semantics; a `public`
stdlib API; a runtime environment variable; target support; LSP, VS Code or MCP behaviour.

1. **Decide visibility against that list, and say so either way.** A change with nothing user-visible
   states that in the report — a claim someone can check, not a step quietly skipped.
2. **Find the owning source** through `website/MAINTAINING.md`'s "what changed → which source" map. It
   is the one map of where the site's documentation comes from. ⛔ Edit the source, never the page.
3. ⭐ **Grep `docs/` for every name the change moved, renamed or removed.** The doc-coverage gates
   check names, not truth: a sentence the change made FALSE is found only this way, and finding it is
   the part of this step that cannot be automated.
4. **Link forms**, from `MAINTAINING.md`: `[text](#anchor)` within a file,
   `[text](CLI_REFERENCE.md#anchor)` across synced files,
   `[E3014](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3014)` for a code (lower case), and a route
   like `/docs/contributing/` only for a page written for the site alone. ⛔ A route to a *generated*
   page is refused by the sync.
5. **Regenerate: `node website/scripts/sync-docs.mjs`.** The regenerated pages are committed with the
   source. The sync fails closed and names every problem; fix them here rather than leaving them for
   the caller's gate.
6. **The manual copies nothing checks**, when the change touches them: `examples/*.maxon` →
   `website/src/examples/`, and `vscode-extension/syntaxes/maxon.tmLanguage.json` →
   `website/src/grammars/`. `MAINTAINING.md` lists the install-script fan-out.
7. ⛔ **DOCS STATE WHAT THE SOFTWARE DOES — NOT WHAT IT USED TO DO, AND NOT WHAT IT DOESN'T DO** (user
   ruling). No history: "no longer", "as before", "instead of <what it did>", "used to", "now". No
   negatives: "never shows", "does not pop up", "is not reported", "sends neither", "rather than X".
   Say where a thing goes and when it happens, and stop there. Grep every doc file you touched for those
   words before reporting, and rewrite a hit in a paragraph you touched, whoever wrote it. The records
   whose purpose IS history — `docs/optimization-log.md`, a release's changelog entry, a commit
   message — are the exception.
8. ⛔ **`CHANGELOG.md` is NOT touched.** `docs/RELEASING.md` makes it a release-time artifact, written
   at the cut with the whole release in view — never appended to per change.

## Verification — a recompile, and nothing else

> ⭐ **The verification is `mcp__maxon__build`.** A comments-and-docs pass can break exactly one thing:
> the code, by accident. The build catches that, and it is the whole gate.
>
> ⛔ **No test runs. Not the spec suite, not the wasm lane, not the doc-coverage gates.** None of them
> can move on a comment, so running one is confirmation rather than detection — and the caller's gate
> battery runs all of them minutes later on the same tree. Never repeat a run whose inputs have not
> changed.

Two things precede the build. Both cost seconds, and both catch what a build cannot.

1. **Read every hunk of `git diff`.** Each `-`/`+` line must be a whole comment line, a blank line, or
   a code line whose text before the `//` is byte-identical.
2. **Mechanical code-identity proof.** ⚠ The baseline is NOT `HEAD` — the file already differs from it.
   Snapshot every file in scope before the pass, then compare the two stripped of comments:
   ```
   sed 's://.*::; s/[[:space:]]*$//' <snapshot> | grep -v '^$' > temp/doc-a
   sed 's://.*::; s/[[:space:]]*$//' <file>     | grep -v '^$' > temp/doc-b
   diff temp/doc-a temp/doc-b        # MUST be empty
   ```
   ⚠ **The `grep -v '^$'` is load-bearing** — stripping a comment-only line leaves a blank one, so a
   correct DELETE would otherwise show in the diff and force you to explain away a failure you should
   never see. A deleted CODE line is still caught: stripped of its comment it is non-blank. Both sides
   get identical mangling of any `//` inside a string. Put the output in the report verbatim; **an
   empty diff is the only acceptable result** — do not argue around a non-empty one.
3. **`mcp__maxon__build`** — ~4 min, exit 0. ⛔ Skip only when the caller says it is batching the build.

`node website/scripts/sync-docs.mjs` still **runs** — it is the regeneration this pass owes, not a
check. Its `--check` form is the caller's gate and is not run here.

⚠ `mcp__maxon__fmt` is not a substitute for the build: a file `fmt` cannot lex is left byte-identical
and reported `unchanged`, the same word it uses for an already-canonical file, so `unchanged` alone
proves nothing.

## Report

- ⭐ **The count, first: comment lines added, rewritten and deleted, and the net.** A volume you have
  to state is a volume you have to defend, and this line is what makes over-writing visible at a
  glance.
- **Every comment written, with its category number (1-9) and the consequence it prevents.** ⛔ A
  comment matching no category, or whose consequence is "a reader might wonder", comes back out.
- **Every fact NOT written, one line each, split by the gate that stopped it**: already in the code ·
  matched no category · failed the consequence gate. ⭐ **These lists together should dwarf the one
  above.** The omissions are the bulk of the work and leave no trace in the file, so this is the only
  place a wrongly-dropped constraint becomes reviewable, and your caller spot-checks it.
- **The documentation decision** — user-visible or not, which source owns the surface, what the grep of
  `docs/` for moved names turned up, and every file touched.
- **The step-2 `diff` verbatim, and the build's exit code.**
- Any claim you could not verify against the code, named, with what you did about it.
- Bugs and stale claims found and **not** acted on.

Never claim a check you did not run.
