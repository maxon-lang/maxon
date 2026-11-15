# Known issues

Open defects nobody has landed a fix for yet. `/land` §10 is the procedure: how an entry is recorded,
claimed and drained. An entry is deleted by the commit that fixes it.

`ci-red` entries stay above every `normal` one. Every edit to this file is committed at once, alone:
`git commit -F <msg> -- KNOWN_ISSUES.md`.

```
## <one-line symptom>
- priority: ci-red | normal
- suspects: <sha> <subject>, …          (ci-red only)
- status: open | claimed <session> <ISO date>
- where: file:line
- seen: how it was found, and by which task
- repro: a program that reproduces it, or for ci-red the run URL, lane and failing case
```

## The Unicode category generator emits a lookup body that differs from the checked-in one
- priority: normal
- status: open
- where: scripts/unicode/generate_unicode_category.py:136
- seen: fixing the CI red after directory visibility landed. Regenerating
  stdlib/helpers/string/unicodeCategory.maxon changes it: `typealias GeneralCategory` loses `public`, and
  `unicodeGeneralCategory` loses its `as GeneralCategory` and `as FullRangeInt` casts, the `target`
  local and its blank lines. Nothing runs the generator in CI, so the drift is silent.
- repro: `python3 -I scripts/unicode/generate_unicode_category.py; git diff stdlib/helpers/string/unicodeCategory.maxon`

## The Windows process listing once took over 30 s in the VS Code suite
- priority: normal
- status: open
- where: vscode-extension/src/test/processTree.ts:60
- seen: CI run 43648604039 (059dd0104b), corpora x64-windows. `the listing shows this process with its
  arguments` timed out at 30 s. The next listing in the same run took 6.7 s, and the same test took 3.4 s
  on the previous run; locally the `Get-CimInstance Win32_Process` command takes ~1.5 s. It ran right after
  four Debugging tests had each waited 60–120 s for a debug session that never launched. The mechanism is
  not yet known. The test's timeout now exceeds the listing budget, so the next occurrence fails with the
  lister's own error.
- repro: the run above; not reproduced locally

## Nothing checks that spec-test prints the same stdout at one worker and at several
- priority: normal
- status: open
- where: maxon-bin/AGENTS.md:252
- seen: the spec-suite speed plan's gate survey; the contract is stated there and in the worker pool's
  header, and every tests/specHarness gate passes only `--workers=1`.
- repro: a tests/specHarness gate that runs one fixture at `--workers=1` and at `--workers=4` and compares stdout; none exists

## A lex sweep that fails drops any phase span its project left open
- priority: normal
- status: open
- where: maxon-bin/Compiler/Compiler.maxon:424
- seen: the spec-suite speed plan's L2 review; `sweepProject` creates its project without the job's span
  array and `beginSweep` sets no trace unit, so `JobRun.failed()` has nothing to close. Read off the
  code, not run. Visible only in a debug-stream trace of the compiler.
- repro: a debug-stream trace of a sweep whose project throws after opening a span shows a begin with no end

## `--workers=1` is spelled as a private constant in five tests/specHarness gates
- priority: normal
- status: open
- where: tests/specHarness/gates/spec-workers-start-from-the-library-cache.maxtest:3
- seen: the spec-suite speed plan's L2 land; the fifth copy came with its gate.
- repro: `grep -n '"--workers=1"' tests/specHarness/gates/*.maxtest` lists five declarations

## The spec worker pool's header says the serial suite takes about three seconds
- priority: normal
- status: open
- where: maxon-bin/Testing/SpecWorkerPool.maxon:8
- seen: the spec-suite speed plan's survey; a lane's suite takes tens of minutes.
- repro: read the comment against any CI suite job's duration

## ProgramSignatures.reassembled leaves two fields at their defaults on a worker's copy
- priority: normal
- status: open
- where: maxon-bin/Compiler/SignatureIndex.maxon:8976
- seen: the spec-suite speed plan's L3c review, by reading; the constructor omits
  corpusByRefConventionDigestAnswer and liveSpawnedTypeNames. No wrong answer was found: parse keys are
  computed on the main thread and the second field is refilled when reach is full.
- repro: a front-end pool worker that reads either field off its reassembled index gets 0 or an empty set

## The corpus-only function-valued field names classify a type by name, not by declaring file
- priority: normal
- status: open
- where: maxon-bin/Compiler/SignatureIndexSettle.maxon:1734
- seen: the spec-suite speed plan's L3c review, by reading; computedFunctionValuedFieldNames(corpusOnly: true)
  asks stdlibDeclaredTypeNames, so a scriptlib type landing in that set would have its field names treated
  as the corpus's own. Not run.
- repro: a scriptlib package declaring a type with a function-valued field, compiled by a single-file program

## Two signature hash marks hold the same value
- priority: normal
- status: open
- where: maxon-bin/Compiler/SignatureIndex.maxon:9397
- seen: the spec-suite speed plan's L3c documenter pass, by reading; DeclShapeGenericInstances and
  LivenessMutableFunctionValues are both 0x3C6EF372FE94F82B, and the comment above the marks says a new
  one is checked against all of them by hand. They feed different chains and no wrong answer was found.
- repro: `grep -n 0x3C6EF372FE94F82B maxon-bin/Compiler/SignatureIndex.maxon` lists two declarations

## A tuple-alias local read by a call in the `return` expression gives a wrong value
- priority: normal
- status: open
- where: not located; the tuple local looks released before the `return` expression reads it
- seen: the spec-suite speed plan's alias landing, soundness hunt; no library alias is involved and
  renaming the aliases changes nothing. Not checked against an older compiler.
- repro: `typealias MyWord = int(0 to 100000)`, `typealias MyTriple = (MyWord, MyWord, MyWord)`, a
  function `sumOf(triple MyTriple) returns MyWord` that destructures and adds, and a `main` that binds
  `let triple = (1, 2, 3) as MyTriple`, calls `sumOf(triple)` twice into locals, prints them (`6 6`) and
  ends `return sumOf(triple) as ExitCode`: it exits 54, not 6. With `return 0` the program is correct.

## A regions-modify spec case pins column 31 where the compiler reports column 25
- priority: normal
- status: open
- where: specs/regions-modify.md (case `one-statement-open-refuses-a-read-through-a-region-beside-a-one-call-open-of-another`)
- seen: the directory-scoped visibility land; it fails the same way with a compiler built from c8773e3, so
  either the diagnostic's anchor moved or the pin is stale.
- repro: `./maxon-bin/.maxon/maxon spec-test --filter=regions-modify/one-statement-open-refuses-a-read-through-a-region-beside-a-one-call-open-of-another`

## Identifiers on `#if` directive lines count as spellings in the unused-declaration audits
- priority: normal
- status: open
- where: maxon-bin/Compiler/QueryDatabase.maxon (`rawSpellingCountsIn` counts them; `conditionalSpellingsIn` excludes them)
- seen: the directory-scoped visibility land's code review; read off the code, not run. An unused
  declaration named like a directive word escapes E3092/E3219.
- repro: one file with `#if debug` … `#end`, another in the same directory declaring an unused unmarked
  `let debug = 1`; expect E3219, observe none

## A generic function that is never instantiated may close with the wrong `end` label
- priority: normal
- status: open
- where: maxon-bin/Compiler/ParserDeclarations.maxon, recordTemplateParamReassigns and noteDeclarationCloser
- seen: the spec-suite speed plan's deletion of the global-alias-return fact; the seed compiler pinned
  at 55d03beeee17 accepts it too. An instantiated template and a non-generic function are refused E2008.
- repro: `public function pick(value T) uses T returns T`, a body `return value`, closed `end 'pikc'`,
  and a `main` that never calls it: it compiles. The same as a `public static` method of a type.

## The compiler-owned declaration file is an empty `FilePath` sentinel
- priority: normal
- status: open
- where: maxon-bin/Compiler/Project.maxon:621
- seen: the Verona regions plan, stage E (user, 2026-11-07, "after the plan"). `let CompilerOwnedDeclFilePath = FilePath from ""`
  stands for "no source file" in every `declFilePath`, `bindingFilePath` and `scopeFilePath` field and every
  `readerFilePath` parameter, so an empty path is a real value that means something else. The fix is
  `union DeclarationOrigin { sourceFile(let path FilePath), compilerOwned }` threaded through those fields
  and parameters (~20 files read the constant, many more carry the fields; the codecs regenerate). The uses
  fall in four groups: a reader with no file, an is-compiler-owned test, a stored declaration file, and
  environments or seen-sets. specs/static-variables.md and specs/typealias-file-scope.md name the sentinel in prose.
- repro: `git grep -n CompilerOwnedDeclFilePath -- maxon-bin` lists the sentinel's declaration and every use

## `renderTargetIr`'s panic names `compileToCodeResult`, which no longer exists
- priority: normal
- status: open
- where: maxon-bin/Compiler/Compiler.maxon:1363
- seen: the Verona regions plan, stage F round 6g (2026-11-12), when `CompilePump` was deleted and the compile moved
  onto the driver. The ~50 stale mentions of `CompilePump`, `compileToCodeResult` and `queryAllModule` the round
  recorded are gone; this panic message is the one left, re-checked 2026-11-14.
- repro: `git grep -n -i "CompilePump\|compileToCodeResult\|queryAllModule"` prints this line only

## Every std-pass function builds its own scratch, which costs the fan-out time and allocations
- priority: normal
- status: open
- where: maxon-bin/Compiler/IR/Std/StdPassPool.maxon:91
- seen: the Verona regions plan, stage D (user, 2026-11-07: accepted for D, revisit after E2). A fresh `StepWorkspace`
  per function made `stdFanOut` 1.47x-3x slower, whole-compile CPU +1-2% on large programs and +7-9% on small
  ones, allocations +6-18%; the U11b read suspected the gap is superlinear. The shape is unchanged today; not
  re-measured. Idea: recycle each workspace: the unit region (body plus scratch) returns in the reply, the driver
  reads the body out, and the same region goes out with the next function. Check the gap's slope rung by rung.
- repro: `maxon scale-test --rungs=6 --repeat=3` on a compiler built from 1c0e822cef and on one built from HEAD;
  compare the std-pass phase's CPU and allocations per rung

## Raw tokens and views are held per owner, not in the `SharedLibraryCache`
- priority: normal
- status: open
- where: maxon-bin/Compiler/QueryEngine.maxon:75
- seen: the Verona regions plan, stage F round 8 (2026-11-13). `SharedLibraryCache` holds frozen parses, baselines,
  audits and packs (LSP RSS -16..-20%), but each `CompileSession.fileMemos` owner still lexes and keeps its own
  `TokenArray` and view per library file, because `Parser` was fenced. Freezing them needs `Parser` to take an
  `immutable TokenArray`.
- repro: `union SharedLibraryChange` in maxon-bin/Compiler/SharedLibraryCache.maxon has parse, baseline, audit
  and pack cases and none for tokens or views; two LSP document projects over one stdlib each hold their own tokens

## `BackendWorker.create` takes an `isolated` ABI table, so every worker is handed a clone
- priority: normal
- status: open
- where: maxon-bin/Compiler/Targets/Shared/BackendPool.maxon:694
- seen: the Verona regions plan, stage D (2026-11-07). The pool holds the table as `immutable CalleeAbiTable` and the
  worker freezes the one it is given, but `create` (line 492) asks for `isolated`, so each spawn copies a table
  the pool could share. `OpVariantFacts.create()` (line 558) also runs per unit, ~6 allocations per function
  (regalloc CPU maybe +6%, under 0.3% of a compile); a shared worker-lifetime memo needs a cheap empty
  constructor in TargetLiveness.maxon.
- repro: `git grep -n "abi.clone()" -- maxon-bin` lists the spawn site

## The service-handler profile case still names a synthesized loop
- priority: normal
- status: open
- where: tests/profile/ProfileHarness.maxon:55
- seen: the Verona regions plan, stage C (2026-11-08), when a service became a cown drained by a pooled runner.
  `ServiceLoopFixtureName`, the `serviceloop` fixture directory and the case title and prose of
  tests/profile/profile-service-handler-is-attributed-as-itself.maxtest still speak of a "synthesized loop" and a
  "message loop"; rename them to the cown/behaviour model.
- repro: `git grep -n -i "synthesized loop\|serviceloop" -- tests/profile`

## alloc-torture's header describes cross-P frees that its own warning says it cannot reach
- priority: normal
- status: open
- where: scripts/multicore-stress/alloc-torture.maxon:9
- seen: the Verona regions plan, stage C (2026-11-08). The "Mechanism" paragraph says an `async` argument is freed on
  a worker P, a remote free back onto P0; the block below it says no task of the program leaves `main`'s strand,
  so nothing here is cross-P.
- repro: read lines 1-31 of the file

## Module-level `var` is still shown in the docs and in spec prose
- priority: normal
- status: open
- where: docs/LANGUAGE_REFERENCE.md:5016
- seen: the Verona regions plan, stage C (2026-11-08), which removed module and static `var`s; a module-level `var` is
  now `E2001 Expected function declaration, got 'var'`. The qualified-name section still says qualification works
  for another directory's top-level `let` and `var`. Uncompiled `text` fences in specs still show a mutating
  module-level `var`: fixed-array.md:123, managed-list.md:22, static-methods.md:29 and pass-by-reference.md:19. The prose
  at fixed-array.md:1071 still says "a top-level `var g = Vec3.create()`". The compiler's panic text for a module
  global still says "top-level var" (`globalStdType` in SignatureIndex.maxon:2793-2834, `recordPointerSlotType`
  in SignatureIndexSettle.maxon:7731-7755).
- repro: `git grep -n '^var ' -- specs/fixed-array.md specs/managed-list.md specs/static-methods.md specs/pass-by-reference.md`
  and the doc line above

## A lent unique parameter rebound with `let b = a` is refused when `b` is read
- priority: normal
- status: open
- where: maxon-bin/Compiler/ParserExpressions.maxon (`bindingReadsValue`, called by `noteReadOfKeptArgument`)
- seen: the sibling-shape hunt of the three-panics fix. `let b = a` over a lent unique parameter files a
  moved-lent-unique site, and `bindingReadsValue` matches every binding holding the moved value, so a read of
  the new name `b` is reported as a read of the moved `a`. A `let b = a` over an owned local compiles.
- repro: `type Tk is unique` with `var n as Num`; `function rebind(a Tk)` holding `let b = a` then
  `print("{b.n}\n")`; a `main` calling `rebind(Tk.create(7))`: E3102 at the read of `b`, naming 'a'. With
  `shelf.push(b)` in place of the print it is refused the same way.

## Windows cannot spawn a spec test executable whose path is 260 characters or more
- priority: normal
- status: open
- where: maxon-bin/Testing/SpecTestRunner.maxon:990
- seen: the Verona regions plan, 2026-11-11. `CreateProcessW` with a null application name caps the program's path at
  MAX_PATH, even spelled `\\?\`, and spec-test builds each executable at `.spec-tmp\<spec>\<case>.exe` under the
  checkout, so a long spec plus case name fails at spawn on x64-windows. Fix: stage test executables in a short
  directory. Not re-run on 2026-11-14.
- repro: on x64-windows, a spec case whose `<checkout>\specs\.spec-tmp\<spec>\<case>.exe` is over 259 characters

## An author extension method on a library type captures a bare call inside the library, cold but not warm
- priority: normal
- status: open
- where: maxon-bin/Compiler, isImplicitSelfMethod: a bare call resolves to a sibling through a
  program-wide member lookup that includes an author's extension methods
- seen: the spec-suite speed plan's forward-callee landing; f518f20 behaves the same. An author
  method's bare name is in no overlap set and no shared-parse key, so a cached library parse is served.
  Such a method that also takes a `mutable` parameter and is named like a library forward callee was
  declined the cache before that landing and is served after it.
- repro: a program declaring `extension String` with a method `scanIsAscii` of another return type
  (the name of a free function `String.isAscii` calls bare) and calling `isAscii` on a string. Under a
  fresh cache root it is refused E3005 at the `return` inside stdlib's String; built after any other
  program under the same cache root it compiles and runs.

## A library forwarder's promise-store facts are resolved by member name over author methods too
- priority: normal
- status: open
- where: maxon-bin/Compiler, closeForwardedStores: anyStoreResolvesByMemberName over
  instanceMethodsByMemberName, then forwardedTargetTakes
- seen: the spec-suite speed plan's forward-callee landing, by reading; not run. A store of the shape
  `x.m(p)` in a library function is resolved against every swept instance method named `m`, the
  author's included, and neither the overlap verdict nor a shared-parse key records an author
  method's bare name.
- repro: a library function forwarding a promise through `x.m(p)`, compiled by a program that
  declares an instance method `m` taking a promise at that position, warm against cold
