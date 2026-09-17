You do not care if an issue is pre-existing. Just debug and fix it.

Do not use "cmd /c" to run commands

Do not use heredocs. Write a file with the file-writing tool, and pass prose to a command from a file
(`git commit -F <path>`) rather than inline. A heredoc breaks on the punctuation real prose contains.

There are no time constraints. Complexity doesn't matter. If you are fixing an issue then fix it properly. No workarounds.

## Where the rules live

This file holds what binds EVERY tree in the repository. The compiler is a project inside it, with its
own build, gates and traps, and those rules load with it:

- **`maxon-bin/CLAUDE.md`** — the compiler: how to build it, the seed, the two-self-compiles rule, the
  `maxon` MCP tools, targets, `spec-test`, `tests/`, `fmt`, the error-code registry, spec fragments.
  `stdlib/`, `specs/`, `tests/`, `scripts/` and `examples/` import it, because work there is compiler work.
- **the `compiler-workflow` skill** — the long procedures: the `run_scale_test` ladder, `fixpoint.sh`,
  hosting the x64-linux lane under WSL, staging `vendor/`.

⇒ **Touching the compiler means those rules apply whether or not they are in front of you.** If you are
about to build, gate or measure it and have not read `maxon-bin/CLAUDE.md` this session, read it first.

## Documentation

**A user-visible change updates its documentation in the same commit.** User-visible means a command, flag or
help text; a diagnostic; syntax or semantics; a `public` stdlib API; a runtime environment variable; target
support; LSP, VS Code or MCP behaviour.

- **The `documenter` skill is the step that does this**, on the finished diff just before the commit,
  in the same pass that writes the change's comments. What follows is what it carries out.
- **Edit the SOURCE, never the site.** `website/MAINTAINING.md`'s "what changed → which source" map names the
  `docs/*.md` file (or the error-code registry) that owns each surface.
- **Then regenerate the site:** `node website/scripts/sync-docs.mjs`. The reference pages are generated from
  those sources and `website.yml` fails on drift, so a hand edit to a generated page is overwritten.
- **The doc-coverage gates catch an undocumented surface:** `maxon test tests/cli -t reference-documents`,
  `tests/mcp -t reference-documents`, `tests/docs`. They check names, not truth — a statement the change made
  false is found by grepping the docs for what changed.

## Code Quality

Apply these standards when writing or reviewing any code:

- **Eliminate duplicated code** — refactor shared logic into helper methods. This includes
  pre-existing duplication.
- **No silent unhandled cases** — `match`/`if` chains that don't cover all cases must throw on the
  unhandled path, not return a default value. Never use a bare `default` case in `match` — use
  `default throws` or `default panic("msg")`.
- **No silent `else` fallthrough** — if an `else` branch should never be reached, throw an error.
- **`try/otherwise` that should never fail** must use `otherwise panic("reason")`.
- **No skipped work** — look for comments implying something was skipped, deferred, or not fully
  implemented, and address them.
- **typealias names describe purpose**, not type — e.g. `BytePos` not `Offset`.
- **Typed ranges should be as specific as possible** — e.g. `int(0 to 100)` instead of
  `int(0 to u64.max)`. Use the narrowest range correct for the domain. Wide ranges are fine when there
  is no clear limit.
- **Fix all IDE-reported problems and compiler warnings.**
- **Cross-target consistency** — any change to target-specific code (e.g. x64) must have an equivalent
  change in all other targets (e.g. arm64) where applicable.
- **Consolidate redundant match arms** — collapse multiple cases with the same result into one.
- **No thin wrapper functions** — remove functions that do nothing but delegate to one other call.
- **No sentinel return values** — a function that cannot return a valid value must throw, not return
  `""`, `-1`, `null`, or similar.
- **Blank lines for readability** — around control flow statements and between logical sections.
- **No magic values** — replace bare literal constants with named `static` constants that describe
  their meaning. Group a related set into a `static enum` rather than scattering them.

### ⛔⭐ COMMENTS — YOU WRITE NONE. THE `documenter` SKILL WRITES THEM ALL, JUST BEFORE THE COMMIT.

**While you are writing code, write NO comments.** Not a `//`, not a `///` doc comment, not the doc
block above an `ErrorCodeRegistry.maxon` case. **Code changes shape while a change is being written**,
so a comment written before the code settles is written and rewritten several times and most of it
never reaches the commit. Only the final shape is worth commenting, and it does not exist yet.

**Every comment in this repository comes from one place: the `documenter` skill, run once on the
finished diff, immediately before the commit.** It also updates the `docs/` source the change's surface
owns and regenerates the site, so the Documentation section above is the same step.

⚠ **Running last is not a licence to write more.** What it writes is **minimal and concise — the
default is still no comment**, and most declarations end with none. The rules below bind it, and bind
every comment anyone touches:

- **Minimal and concise.** The default is **no comment**. Write one only where the code cannot carry
  the point by itself, and then in as few words as it takes. A comment per line, a banner over every
  section, and a restated signature above a function are all noise — and noise is unverified prose
  that rots while the code keeps working.
- **"Why", never "how".** The code is the "how". Comment what the reader cannot recover from it: the
  constraint, the invariant, the reason this order/bound/branch is correct, the cost that motivated an
  unobvious shape.
- ⛔ **NO HISTORY. Describe the CURRENT state only.** No "used to", "previously", "changed from",
  "renamed", "now that we…", "this was a workaround for…", no dated narration of an edit, no reference
  to a former name. **Git holds the history; a comment holds the present.** The reason a guard exists
  is a *why* and belongs — but state the constraint that still binds ("callers may hand this an
  unsorted list"), never the edit that introduced it. *(This bans history in SOURCE COMMENTS. Docs and
  commit messages are where a measurement, an incident and a correction get recorded.)*
- **Editing a comment means REWRITING it to conform** — or deleting it. Never leave a conforming edit
  inside a non-conforming comment.

