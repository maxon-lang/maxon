---
title: Query Spine
description: The memoized queries a compile is made of, how they are keyed, and the rules that keep them linear and sound.
sidebar:
  order: 2
---

A compile is a chain of memoized queries (`maxon-bin/Compiler/Queries.maxon`). A query returns its cached
value when that value is still valid and otherwise computes and caches it. The memos live in the
`QueryDatabase` on a `Project` (`QueryDatabase.maxon`), and a `Project` that compiles more than once — the
language server, the MCP `check` tool, `run`'s cache — reuses them.

## Queries

| Query | Scope | Produces |
| --- | --- | --- |
| `queryTokens` | per file | the file's tokens |
| `queryActiveTokens` | per file | the tokens left once `#if` blocks are resolved; every token consumer reads through it, so the declaration sweep and the parse cannot disagree about a file |
| `queryProgramSignatures` | whole program | the signature index: every declaration in the program, read from tokens |
| `queryParseOps` | per file | the file's parse artifact |
| `queryAllModule` | whole program | the merged module; every file is parsed before any is merged |

The inputs are the source files themselves, registered by `fileChanged`. A failed lex or parse is never
cached; a failed `#if` resolution is, so its diagnostic is reported once.

## Keys

Memos are keyed by content hash, not by a revision counter: a memo is valid while the hash it was stored
under equals its input's current hash. This needs no coordination between threads. There is no dependency
graph either: each memo names its own inputs in the one function that computes its key
(`QueryEngine.maxon`).

- A file's tokens are keyed on its content hash.
- The signature index and the merged module are keyed on a composite hash of every (path, content hash)
  pair, so renaming a file invalidates them.
- A file's parse is keyed on its content hash, the signature index's own hash, whether bodies are built,
  whether the build instruments for coverage, and the program-wide answers the file is parsed with where
  its own parse answered a question otherwise.

The signature index's hash covers declarations, not sources. Editing a function's body moves only that file's
content hash, so only that file re-parses; editing a return type moves the index hash, and every file
re-parses. The index also carries a few facts derived from bodies, which ride its hash too.

### Mix what a reader copies

A fact rides the signature index's hash if and only if changing it changes what *another* file compiles to
while that file's own bytes stay put. The hash keys every parse, so each extra fact makes more edits re-parse
the whole program, and each missing one lets a stale parse survive.

- A `let` constant's value is copied into every reader as a literal, so the value rides.
- A `var` global's value is not copied: a reader loads it. Only its width and label ride, so editing its
  initial value re-parses only the declaring file.
- A parameter default's expression does not ride: a caller emits a call to the default's helper, the same
  bytes whatever the expression. The helper's return type and the parameter's name and presence do.
- An interface body's private type aliases do not ride: no parse copies them, and their one consumer, the
  conformance check, memoizes nothing.

Mixing more than readers copy is sound and wrong: every keystroke would re-parse the program.
`verify-warm-rebuild` pins this with paired probes that expect opposite re-parse counts.

## Order-independent visibility

`queryProgramSignatures` sweeps every file's tokens for declarations before any file is parsed, so a file can
use a declaration from any other file whatever order the files arrive in. The sweep runs a `Parser` over the
tokens, so the sweep and the real parse cannot drift apart. Files are partitioned by provenance — user
files, then standard-library files — and within each group the order is whatever the filesystem returned.

The loader does not sort files, deliberately. Sorting would not remove an order dependence, only hide it
reliably. Where an order genuinely matters, as in the order functions are emitted, it is made canonical at
the point of emission.

## Whole-program queries under per-file ones

Every `queryParseOps` validates the signature index before validating itself, so a whole-program query that
sits under a per-file one is asked once per file. Any O(program) work done inside it becomes
O(files × program). That is why the composite hash is memoized, and why program-wide work the back end needs
— laying out the `.data` section's globals (`declaredGlobals`) and the managed globals (`managedGlobals`) —
is asked once per compile from `compileToCodeResult`, not from inside the index.

## The memo is shared

A memo's value is read by every later compile of the same `Project`, so nothing may mutate it in place. A
pass that writes into a structure it got from a memo corrupts what the next compile reads.

The merged module is the one exception, and it is handled by dropping it: type resolution and managed-global
setup do mutate it, so `invalidateAllModuleCache` discards it after every compile. Re-folding it is cheap,
and every expensive memo underneath survives. Parse artifacts are also rewritten on their first merge, which
is safe only within one `Project`.

Across `Project`s in one process, only facts that are a pure function of a file's bytes are shared: the
standard library's tokens, `#if` views and producer masks, keyed by content hash
(`QueryEngine.sharedFileMemos`). Parse artifacts and swept declarations carry interned ids and are
mutated, so they are never shared. Compiles in one process run strictly one at a time.

## The parallel front end

`queryAllModule` opens a pool of workers for its duration (`FrontEndPool.maxon`) and lexes and parses the
files that missed their memo on it. `MAXON_MAX_PROCS=1` gives a pool of one worker, not a separate path.

- A worker parses against a private copy of the settled signature index; a write a parse has not declared
  panics.
- Results land in source-path order on the compiling thread, never in arrival order, so the output does not
  depend on the pool. `mergeArtifact` is the only writer of the shared `Project`.

## Verification

Two gates exercise the spine the spec suite cannot:

- **`verify-warm-rebuild`** checks that two cold compiles are byte-identical; that re-asking every query on
  an unchanged `Project` hits every memo; that each kind of edit invalidates exactly the memos it should;
  and that a warm compile after an edit equals a cold one.
- **`verify-recheck`** runs the whole pipeline repeatedly on one `Project`, as an editor does: the diagnostic
  count must be stable, an error that is fixed must stop being reported, and two live `Project`s checked in
  turn must not affect each other.
