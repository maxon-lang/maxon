---
name: documenter
description: Author a finished change's comments and documentation in one pass, immediately before the commit — the ONLY step that writes comments, because code is written with none. The expected output is ZERO comment lines; four narrow categories can earn one, at most two lines each. Also updates the `docs/` source the change's surface owns and regenerates the site. Comments and docs only — not one byte of code changes. Invoke as `/documenter`.
---

# Document one finished change

Code in this repository is written with no comments; this pass, run once on the finished diff, is
where every comment comes from. **The expected result is zero comment lines added.** Most changes
need none, and a pass that adds more than a few lines has almost certainly over-written.

**Argument:** none = the working diff against `HEAD` (`git status --short`, then `git diff`). Optionally
a revision range or an explicit list of paths.

## Refusals

1. **Not one byte of code changes.** A bug, or a comment describing a shape the code no longer has, is
   fixed in the comment or reported — never in the code.
2. **Generated files:** `maxon-bin/Compiler/Runtime/SlabClasses.maxon` and any file whose header
   declares itself generated. (`ErrorCodeRegistry.maxon` is hand-written and in scope.)
3. **`specs/`** is read-only.
4. **Generated site pages** under `website/src/content/docs/docs/{cli,language,stdlib,spec,best-practices}/`
   — edit the source and re-run the sync.

## Comments

### Do not hunt for things to say

Do not inventory the diff's facts and filter them. Read the diff once. A comment exists only where,
reading the finished code, you hit a spot where **someone editing it would plausibly make it silently
wrong** — a wrong answer, a leak, a broken gate, a crash on one target — and the code gives them no
way to see it. "A reader might wonder why" is not that. "This is non-obvious and I worked it out" is
not that.

The caller's brief carries the change's diagnosis and the implementers' reports. They are context for
judging the code, **not material to transcribe**: the commit message records the change's why.

### The four categories — nothing else is written

1. **Ordering:** X must run before Y, and what silently breaks otherwise.
2. **A non-local invariant:** correctness rests on a fact outside this function that its signature
   cannot state (a cache key that omits the target, sound only because a `Project` has one target).
3. **A gate that will fail:** wording pinned byte-for-byte by a named spec, output compared by a
   named pin. Name the gate, by its name and not its path.
4. **A deliberate asymmetry** that invites a wrong "fix": two nearby sites differing on purpose.

A guard's reason, a cost argument, a pointer elsewhere, a summary, a restated signature, a narrated
block: not written. When one declaration has two candidates, keep the worse consequence only.

### Shape

- **One or two lines.** A fact that seems to need more is being explained, not stated; cut to the
  constraint and its consequence.
- `//` only. Present tense, no markdown, no `⭐`, no ALL-CAPS, no `⚠`. Wrap at 100 columns, tabs
  matching the code.
- **No history and no provenance:** no "used to", "previously", "renamed", "ported from", "split out
  of", no measurements, dates or past bugs. State the constraint that binds now.
- Every name a comment cites is grepped first.
- **No exact counts and no locations.** A comment that says how many (files, cases, callers, passes,
  functions, bytes) or where (a file path, a directory, a line number) goes stale the day the count
  moves or the file is renamed, while the code is still right. Leave both out.
  - **A size that matters is approximate:** `~100`, "a few thousand". Never `103`.
  - **An exact figure is written only when the figure is itself the constraint** — a width or a slot
    count another site must match — and then by naming the constant that holds it where one exists.
  - **A pointer is written only when pointing is the comment's whole job** (category 3's gate), and
    then as the shortest stable name: the spec's or the case's name, a type, a function. Never a path
    with its directories, never a file extension, never a line number.
- A fact lives at one site.

```maxon
// Asked below `queryTokens`: a shared hit that skipped it would leave `tokenCache` empty, so the next
// re-query of an unchanged file would miss.
```

A typical refactor, extraction, new helper or bug fix gets **nothing**.

### Existing comments — audit the neighbourhood

Scope: every comment block attached to, or inside, a declaration the diff touched, in every file the
change touched. Not the whole file. The audit cuts; it never adds.

For each block, sentence by sentence:

- **False** (the change or anything else made it untrue) → rewrite the sentence to what is true, or
  delete it. Verify a claimed difference from another path in both directions first.
- **History, provenance, a measurement, a date, a past bug** → delete it. Where it carries a live
  constraint (a wording pinned by a spec), keep the constraint and name the gate:
  `Pinned byte-for-byte by the top-level-let spec.`
- **An exact count, a file path or a line number** the sentence does not rest on → drop it, or round a
  size that matters (`~100`); cut a pointer that is the point to a stable name (*Shape*).
- **Restates the code** (signature, guard condition, narrated block, constant's name) → delete it.
- **Outside the four categories and nothing goes wrong if nobody knows it** → delete it.
- **Two sites stating one fact** → keep one.
- **A fact sitting above the wrong declaration** → move the text to the one it describes.

What survives stays byte-for-byte: never restyle, reflow or reword a sentence whose content is true
and allowed, whatever its bold, capitals or wrap. When a sentence changes, rewrite that sentence, and
reflow only the paragraph it is in.

### Prose this pass authors (not code comments; the categories do not apply)

1. **A new `ErrorCodeRegistry.maxon` case** needs the `//` block directly above it (no blank line): the
   text `lookup_error_code` serves and the Error Codes page body. Write it for a user who hit the
   diagnostic — what was refused and what makes it legal. The sync fails closed on a case without one.
2. **`///` on a newly exported function** — rendered in LSP hover. One or two sentences.
3. **Agent-facing rules:** a change to a command, workflow or rule an agent follows updates
   `docs/WRITING_MAXON_CODE.md`'s preamble or its `## Scripts and scriptlib` section; the sync
   regenerates `maxon-bin/Compiler/Mcp/McpInstructionsGenerated.maxon`.

## Documentation

A user-visible change (a command, flag or help text; a diagnostic; syntax or semantics; a `public`
stdlib API; a runtime environment variable; target support; LSP, VS Code or MCP behaviour) updates its
documentation in the same commit.

1. Decide visibility and say so in the report either way.
2. Find the owning source through `website/MAINTAINING.md`'s "what changed → which source" map. Edit
   the source, never the page.
3. Grep `docs/` and `vscode-extension/README.md` for every name the change moved, renamed or removed —
   a sentence made false is found only this way.
4. Link forms are in `MAINTAINING.md`; a route to a generated page is refused by the sync.
5. Run `node website/scripts/sync-docs.mjs` and fix what it names. Not `--check`; that is the caller's.
6. If `vscode-extension/syntaxes/maxon.tmLanguage.json` changed, copy it to `website/src/grammars/`.
7. Docs state what the software does: no "no longer", "now", "used to", "instead of", and no negatives
   ("never shows", "is not reported", "rather than X"). Grep the paragraphs you touched for them.
8. Docs prose carries no exact count of anything that grows with the code (files, functions, cases,
   passes), no line number, and a source path only where the reader has to open or type it. A size
   that matters is approximate (`~100`).
9. A change that can break existing code, scripts or workflows adds a bullet under `## Unreleased` ›
   `### Breaking changes` in `RELEASE_NOTES.md` with an indented `Fix: …` line (`docs/RELEASING.md`).
   Nothing else goes there per change.

## Verification

No build and no tests: a comment cannot change behaviour, and the caller's landing gates build the
tree minutes later. What can go wrong is an accidental code edit, and this proves there was none.
Snapshot each file in scope to the scratchpad before editing, then:

```
sed 's://.*::; s/[[:space:]]*$//' <snapshot> | grep -v '^$' > <scratchpad>/doc-a
sed 's://.*::; s/[[:space:]]*$//' <file>     | grep -v '^$' > <scratchpad>/doc-b
diff <scratchpad>/doc-a <scratchpad>/doc-b        # must be empty
```

An empty diff for every file is the only acceptable result. Batch edits: one `Edit` per contiguous run
of comment lines.

## Report — short

- **Comment lines added / rewritten / deleted / net**, first.
- Each comment written: file:line, its category (1-4), the silent failure it prevents.
- The audit: blocks examined, and per edited block one line — deleted, trimmed, corrected or moved,
  and why.
- The documentation decision: user-visible or not, files touched, what the name grep found.
- The identity-proof result (empty, per file).
- Bugs or stale claims noticed and not acted on, and any claim you could not verify.
