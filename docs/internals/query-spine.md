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
tokens, so the sweep and the real parse cannot drift apart. Files are partitioned by provenance — the
library's files and then the program's onto the [library baseline](#the-library-baseline), or user files and
then standard-library files for a program whose names must win a contest against the library's — and
within each group the order is whatever the filesystem returned.

The loader does not sort files, deliberately. Sorting would not remove an order dependence, only hide it
reliably. Where an order genuinely matters it is made canonical where it is decided: the merge folds the
files in `Queries.sourceFilesInEmissionOrder`, keyed on each file's tier (author, then standard library,
then runtime) and its path relative to that tier's namespace anchor, compared component by component, so
one program compiles to the same bytes on every host. An author file outside the project root keys by its
resolved path.

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
setup do mutate it, so `invalidateAllModuleCache` discards it as each compile takes it. Re-folding it is cheap,
and every expensive memo underneath survives. Parse artifacts are also rewritten on their first merge, which
is safe only within one `Project`.

Across `Project`s, the store a `CompileSession` carries (`CompileSession.fileMemos`) — one per spec worker
process, one for the language server's document projects, one per MCP session — holds the library's tokens,
`#if` views and producer masks, keyed by content hash, and each library file's parse. A parse embeds
index-numbered facts the asking program's own declarations shift (type-name ids, generic-instance ids, the
interface indexes witness dispatches carry), so a held parse records a footprint of them, taken while its
names are still file-local, and a hit is rebased onto the current program; a row or a told answer that moved
is a miss. Its key is a signature hash over the library alone, since the whole-program hash moves with every
user declaration. A store that serves many compiles deep-clones a held parse at the stash and at every hit,
because the merge renumbers an artifact in place; the store of a one-shot `maxon build` hands it over
instead, since that compile is its last. A program that shadows a library name, declares one the library
means, or is rooted inside `stdlib/` parses the library cold. One file's swept declarations stay with their
`Project`. A compile's state lives on its `Project` and its `CompileSession`, which lets the language server
run project checks in services of their own beside its main loop; each store serves one compile at a time.

### The library baseline

The library's declarations are swept once, into a `ProgramSignatures` holding the library alone
(`LibraryBaseline.maxon`). A program starts from a copy of it and folds only its own files on top, then
runs the whole-program tail. A program that shadows a library type, declares a name the library means, or
is rooted inside `stdlib/` needs its names settled before the library folds, so it is swept user first.
It finds that out per file, through a flag the index sets the moment an author name meets a name the
library means, and the facts already discovered for its files are handed to the user-first sweep, so each
file is discovered once.

There are two baselines. The complete one holds every library file; the core one leaves out the files that
declare generic function templates, because their fold depends on the instances the program requests. A
program requesting one of those templates starts from the core baseline and folds the remaining files after
adopting its instances.

### The disk tier

The store has a disk tier (`LibraryCacheStore.maxon`) under the run cache's root, so a new process starts
warm. It holds the library files' `#if` views, the held parses and both baselines, packed one file per kind
and library state, since per-file I/O dominates on Windows. A token stream is written only as a view, and a
baseline names the views its token streams come from. A view carries the facts read off the file's raw
stream, `#if` branches included — its declared type names and its `__` spellings — so a warm compile
respells a diagnostic exactly as a cold one. Every entry carries a schema fingerprint over the
codecs that wrote it, and a reader rejects, deletes and rewrites an entry that is damaged, was written
under another schema or holds a number outside the range it is read into, and the compile goes on as a
cold one. A compile that writes an entry goes on with the decoded copy, so a warm and a cold compile emit
the same bytes.

The codecs are generated into the compiler's sources by `scripts/library-cache/generate-codecs.py`, between
`// @generated library-cache` markers; the hand-written ones live in `LibraryCacheCodecs.maxon`,
`LibraryCacheFormat.maxon` and `ContentHash.maxon`. A cached record that gains a field stops compiling until
the generator runs again.

The tier is bounded: each kind keeps its 4 newest entries (16 for parses), a process holds at most 4
baselines, and the 16 most recently written compiler lineages (one per compiler path and target) are kept.
`verify-warm-rebuild` runs without the disk tier, because its memo counters assume every memo lives in the
process.

## The parallel front end

`queryAllModule` opens a pool of workers for its duration (`FrontEndPool.maxon`) and lexes and parses the
files that missed their memo on it. `MAXON_MAX_PROCS=1` gives a pool of one worker, not a separate path.

- A worker parses against a private copy of the settled signature index; a write a parse has not declared
  panics.
- Results are merged in emission order on the compiling thread, so the output is the same whatever order
  the workers finish in. `mergeArtifact` is the only writer of the shared `Project`.
- Every pool hands each worker a fixed share of the jobs (worker `w` takes dispatch positions `w`,
  `w + workers`, …) and splices replies in dispatch order, because a worker's own scratch and the arrays a
  splice grows would otherwise make a compile's allocation totals depend on which worker finished first.
  Worker handles are released only after the compile's measurement closes.

## Verification

Two gates exercise the spine the spec suite cannot:

- **`verify-warm-rebuild`** checks that two cold compiles are byte-identical; that re-asking every query on
  an unchanged `Project` hits every memo; that each kind of edit invalidates exactly the memos it should;
  that a warm compile after an edit equals a cold one; and that later programs in one session are served
  every library parse and still equal a cold compile of each.
- **`verify-recheck`** runs the whole pipeline repeatedly on one `Project`, as an editor does: the diagnostic
  count must be stable, an error that is fixed must stop being reported, and two live `Project`s checked in
  turn must not affect each other.
