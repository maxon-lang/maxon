# Every declaration has one resolved identity (origin, layer, owner, kind): no lookup, key or message rebuilds it from a spelling

Class issue: #18 · Priority: P1

## Stage order

One stage is one local commit, which ticks its box. A stage starts when every stage it waits on is ticked
here and every issue it waits on is closed. A `deferred` stage is skipped. The plan lands once, after its
last stage.

| Done | Stage | Rule it establishes | Waits on | Not beside | Needs a ruling |
|---|---|---|---|---|---|
| [ ] | 1 | A declaration carries its origin and layer as fields; no path sentinel and no path-set classification remain |  | 2, 3, 4, 5, 6, 7, 8, 9 |  |
| [ ] | 2 | A canonical tuple layout has no file: its origin is compilerOwned and its elements are resolved in the file of the spelling that mints it | 1 |  |  |
| [ ] | 3 | A member lookup resolves over the methods the reader's own layer can see | 1 |  |  |
| [ ] | 4 | An extension that declares a static its primitive already has is a duplicate member, E3006 |  |  |  |
| [ ] | 5 | One written-name resolver and a structured declaration key replace every door's own name resolution and the rendered inner-alias key | 1 |  |  |
| [ ] | 6 | The language server resolves a written type name through the one resolver, with its enclosing type or extension as the site | 5 |  |  |
| [ ] | 7 | A door refuses a name by the kind it resolved to: a ranged alias has no from construction and no statics, under its own error code | 5 |  |  |
| [ ] | 8 | One mismatch renderer takes both types and the reader, names each side as the reader sees it, and panics when the two sides still read alike | 1 | 5 |  |
| [ ] | 9 | A type-name id carries the interner space it indexes, and every read through the wrong interner fails |  | 1, 5 | Open question 2: how an id carries its space (#214): (a) static type parameter, (b) dynamic tag in the high bits, (c) wait for #36 stage 3's per-package id spaces; recommended (b) |

## Rulings

- Open question 1: the resolver's order across layers is Design-packages ruling 8: the own layer first, then the single visible public declaration of a lower layer; two candidates are an ambiguity error.

## Problem

A declaration has no identity of its own in the compiler. What stands in for one is a spelling, and each
consumer rebuilds the identity from that spelling together with a reader's file path:

- **The key is rendered text.** A declaration is filed under a `ByteArray` built by string joins and
  suffix mints: `joinQualifiedName(owner, member)` for a nested alias (`Parser.innerAliasKey`),
  `qualifiedMintName(name, qualifier: layer.name)` for a per-layer member, `aliasFamilyMint`,
  `ordinalMintSpelling`, the range-qualified mint. Readers take the text apart again
  (`extensionMemberMintOf` recovers a `DeclarationLayer` by comparing a suffix with `layer.name`;
  `isMintName`, `containsQualifier`, `withoutExtensionMemberMint`). A rendered key is not injective:
  `ProgramSignatures.writtenAlias` sends every spelling that contains a qualifier through
  `namespaceAliasOf`, so `<Type>.<member>` and `<directory>.<name>` are one spelling.
- **The origin is a path, and "no file" is the empty path.** `CompilerOwnedDeclFilePath` (`FilePath from ""`)
  fills every `declFilePath`, `scopeFilePath` and `readerFilePath` that has no file (about 50 uses of the
  constant; about 2,300 mentions of the four path fields and parameters across the compiler).
  `ProgramSignatures.bareWrittenKey` branches on it.
- **The layer is not on the declaration.** `ProgramSignatures.declarationLayerOf` derives it from two path
  sets (`libraryFilePaths`, `scriptlibFilePaths`); `ParseJob.declarationLayer` derives it a second time,
  and `requireJobLayerMatchesIndex` exists to panic when the two drift. Other sites ask a name set
  instead: `computedFunctionValuedFieldNames(corpusOnly:)` asks `stdlibDeclaredTypeNames.contains(layout.name)`,
  `viewedAliasName` asks `sharesItsNameWithACorpusDeclaration` and knows two layers where
  `DeclarationLayer` has three, `closeForwardedStores` resolves a library forwarder's store over
  `instanceMethodsByMemberName` with no layer at all. The settled declarations hold one inner-alias name
  set per layer (`sharedCorpusExtensionInnerAliasMembers`, `scriptlibExtensionInnerAliasMembers`,
  `authorExtensionInnerAliasMembers`, `multiLayerInnerAliases`).
- **Each door resolves a written name itself.** `Parser.innerAliasKeyOrName`, `writtenGenericAliasInstance`,
  `spellingDenotesBufferSurface` (file-scope name first, nested key second: the reverse of the shadowing
  rule), the parsing reader's qualified-alias path, the sweep's `fromHeadNames` (raw `aliasName`),
  `LspHover.declaredTypeArgument` (the bare token), `ProgramSignatures.aliasFormFor` (three probes in a
  row, each filtered by a wanted kind, so a name of another kind reads as "not declared" and the caller
  falls through to "undefined variable").
- **A type is rendered for a message per side, per site.** `ProgramSignatures.typeDisplayName`,
  `SemanticCheck.typeDisplayName` and `Parser.displayTypeNameOf` each print one type; about twenty sites
  build a mismatch message from two independent renderings, so nothing checks that the two sides read
  differently. `deriveInstanceDisplayNames` picks one display name per instance for the whole program,
  with no reader, so a message can name a type by an alias the reader's file never wrote.
- **A canonical tuple borrows a file.** `ProgramSignatures.canonicalTupleName` gives the canonical layout
  the `declFilePath` of whichever spelling was canonicalised first, and later walks read that path as a
  scope.
- **An id does not say which table it indexes.** `TypeNameId` is one integer type for three id spaces (a
  file artifact's interner, the signature index's, the project's), and travels inside `MaxonType`.

Every member below is one of these seven shapes.

## Design

**One resolved identity per declaration, made in one place, and a spelling only at the three boundaries
that need text (a diagnostic, a mangled symbol, a cache file).**

1. **`DeclarationOrigin`** replaces the path sentinel: `union DeclarationOrigin { sourceFile(file SourceFile),
   compilerOwned }`. A `SourceFile` is made once, by the loader, and carries its path and its
   `DeclarationLayer`; the layer is a field read off the origin, never a set membership and never a name
   test. A reader is `union NameReader { file(SourceFile), noFile }`. `CompilerOwnedDeclFilePath`,
   `declarationLayerOf`'s path sets, `requireJobLayerMatchesIndex` and the per-layer name sets go.
2. **A structured declaration key**: `(layer, owner, name, kind)`, where `owner` is the enclosing type's
   key or none, and a directory qualifier is a separate field of the top-level form. It is a value with
   `Hashable` and an ordering, not text, so a nested alias and a directory-qualified name cannot be equal.
   One renderer turns a key into text for a symbol, a cache row or a message; no code parses a rendered
   key back. This is the shape #36 needs: a layer today is a package there, and the key of a lower layer
   never changes because a higher layer declares the same name (Design-packages ruling 9, "Names").
3. **One written-name resolver**: `resolveWrittenTypeName(spelling, site) -> resolved(key, kind) | absent |
   ambiguous`, where `site` is the reader, the enclosing type or extension and its type parameters. It
   owns the order (a nested alias shadows a file-scope one; own layer before a lower layer) and both the
   bare and the `Outer.Alias` forms, for every alias kind. Every door asks it, the language server
   included, and the answer is memoised per (spelling, reader file, enclosing owner). A door that wants
   one kind and gets another refuses by the kind it got.
4. **One mismatch renderer** takes both types and the reader. A side is named by the brand or alias the
   reader can see, else by its structural spelling. When two different types render alike, the renderer
   adds what tells them apart (the origin note that `originNote` already writes at some sites, then the
   instance arguments, then capabilities) and panics if every level still reads alike: a refusal that
   prints one type twice is a compiler defect, not a message.
5. **A canonical tuple's origin is `compilerOwned`**, and its elements are resolved when the spelling that
   mints it is read, in that spelling's own file. No later walk reads a scope off a tuple layout.
6. **A type-name id carries its space**, so a lookup in the wrong interner fails wherever it runs instead
   of answering about another name. See open question 2.

**Prior art read.** Go's type checker gives every declaration an object (`types.Object`: package, name,
position, scope) and compares named types by that object, never by name; text is produced only when
printing, through a qualifier function. `cmd/compile/internal/types2/format.go`, `Checker.qualifier`,
prints a package by name and switches to the full import path when two packages in the import graph share
a name (`pkgPathMap`): that is design point 4, and Maxon adopts it with the layer or declaring file in the
note. rustc's `DefId` is `(crate, index)`: the id names its table, which is design point 6 and the
"fixed id space per package" of Design-packages stage 3. Maxon departs from Go in one place: Go resolves
every name before any consumer reads a type, while Maxon's declaration sweep records signatures before
every file is folded, so a swept type still holds a bare name. This plan keeps that pass structure and
moves the resolution to one function; replacing `MaxonType.named`'s interned spelling by a resolved
declaration is the end state and belongs to #36 stages 1 and 3 (open question 3).

**Order against the design issues.** Stage 1 lands before #37: #37's narrowed rule needs each
declaration's layer to be a fact. Stage 5's resolver order is the rule #37 leaves open ("how the narrowed
rule decides which declaration a name means"), so it is ruled on #37 before stage 5 lands (open question
1), and #37 then removes the whole-program overlap flag over that resolver. #36 stage 1 (the library
never renames) follows stage 5: after it, a contested name is two keys, and no mint rewrites the lower
layer's. Nothing here adds a marker, an import or a package boundary.

### Open design questions

1. **The resolver's order across layers.** (a) Design-packages ruling 8 now: the reader's own layer in
   today's order, then the single visible `public` declaration of a lower layer, two candidates an
   ambiguity error. (b) Today's ranking, centralised unchanged. Recommendation: (a), recorded as #37's
   ruling, since (b) centralises a rule #36 has already replaced.
2. **How an id carries its space (#214).** (a) Static: `MaxonType` takes its space as a type parameter;
   a mix does not compile; about 1,640 `MaxonType` references change and every method is compiled per
   space (#106). (b) Dynamic: an id holds its space in its high bits, every interner read checks it and
   panics naming both spaces, and an id becomes an index only through its interner. (c) Wait for #36
   stage 3's per-package id spaces. Recommendation: (b) now. It fails on every execution that reaches a
   mix, whatever the names' order (the #206 failure depended on the order), and it is the tag (c) needs.
3. **Whether `MaxonType.named` carrying a resolved declaration is a stage here.** Recommendation: no; it
   is #36 stages 1 and 3, planned from their own survey once stages 1 to 7 have landed.
4. **The runtime entries named by prefix** (#153, #159, #220, #294) are a second class with its own
   single point of truth; see "Second class" below. Recommendation: its own enhancement issue, #153
   rewritten as its owner.

## Stages

Each stage is one `/land` unless marked `/plan-and-land`. A stage whose member is "suspected" or "not
run" builds that member's program first; if it does not fail, the stage still lands the structural change
and pins the correct behaviour.

1. **Origin and layer on every declaration** (`/plan-and-land`).
   `DeclarationOrigin`, `SourceFile`, `NameReader`; the layer is read off the origin. Delete
   `CompilerOwnedDeclFilePath`, the path-set classification, `requireJobLayerMatchesIndex`,
   `stdlibDeclaredTypeNames` as a layer test. `computedFunctionValuedFieldNames(corpusOnly:)` asks the
   layout's layer; `viewedAliasName` keys by the alias's layer, for all three layers. The codecs
   regenerate. It may land as a short series, one registry family per commit, the constant deleted in the
   last.
   - Area: `Project.maxon`, `SignatureLookupTypes.maxon`, `SignatureIndex*.maxon`, `ParseStaging.maxon`,
     `StdlibLoader.maxon`, the generated cache codecs, every file that reads the four path fields.
   - Depends on: none. Nothing else in this plan may run beside it.
   - Discharges: #18 (its original scope), #12, #74.
   - Shown by: `git grep CompilerOwnedDeclFilePath -- maxon-bin` is empty; a scriptlib package declaring a
     type with a function-valued field, compiled by a single-file program (#12); a scriptlib package and
     the stdlib each declaring a top-level function `typealias` of one name with different signatures,
     both instantiated through one generic, each keeping its own signature (#74).
     `specs/static-variables.md` and `specs/typealias-file-scope.md` stop naming the sentinel.

2. **A canonical tuple has no file.**
   Canonical layouts are `compilerOwned`; `canonicalTupleElement` resolves in the minting spelling's
   file and stores resolved elements; `tupleElementBrands`, `settleSweptTupleElementBrands` and
   `substituteTupleThroughInstance` read no scope off a layout.
   - Area: `SignatureIndexInstances.maxon` (the tuple registry), the tuple layout codec.
   - Depends on: 1.
   - Discharges: #205.
   - Shown by: `tests/cli/fixtures/library-cache-seed-independent/tuples` built into a fresh cache root:
     no library parse section holds a program path; two programs built into separate fresh roots write
     the library parse rows they share with equal bytes for the tuple registry; a program where one bare
     element name is a ranged alias in one file's scope and not in another's compiles to two canonical
     tuples.

3. **A member lookup takes the reader's layer.**
   `closeForwardedStores` resolves a forwarder's `x.m(p)` store over the methods its own layer can see.
   - Area: `SignatureIndexSettle.maxon` (`closeForwardedStores`, `instanceMethodsByMemberName`,
     `forwardedTargetTakes`).
   - Depends on: 1.
   - Discharges: #29.
   - Shown by: a library function forwarding a promise through `x.m(p)`, compiled by a program that
     declares an instance method `m` taking a promise at that position, warm against cold: the library's
     facts and its cache rows are equal with and without the program's method.

4. **A primitive's built-in static is a member it already has.**
   An `extension` that declares a static its primitive already has is E3006, as a duplicate method is
   (Design-packages ruling 13). `resolveUnownedStatic` keeps "a declaration outranks the primitive
   reading" only for a name the primitive does not have. Owes a `RELEASE_NOTES.md` entry with its `Fix:`
   (rename the static).
   - Area: `ParserStaticCalls.maxon` (`resolveUnownedStatic`), the extension duplicate check, the
     primitive statics' roster.
   - Depends on: none.
   - Discharges: #54.
   - Shown by: #54's two-file program (`extension int` adding `fromString(input String, base FullRangeInt)`)
     is refused E3006 at the extension; `extension int` adding a new static name still links.

5. **One written-name resolver and a structured inner-alias key** (`/plan-and-land`; ruling on #37 first).
   Design points 2 and 3. Routed through it: the parser's type positions, `spellingDenotesBufferSurface`,
   `writtenGenericAliasInstance`, `writtenAlias` and `namespaceAliasOf`, the sweep's `fromHeadNames`,
   `aliasFormFor`. The suffix mints for layers and the parse-back helpers go.
   - Area: `SignatureIndexSettle.maxon`, `SignatureIndexInstances.maxon`, `SignatureLookupTypes.maxon`,
     `ParserTypeDeclarations.maxon`, `ParserDeclarations.maxon`, `ChainResolution.maxon`.
   - Depends on: 1; open question 1.
   - Discharges: #173, #217, #185, #189.
   - Shown by:
     - #173: a directory `Bag/` exporting an alias `Items` beside `type Bag` with an inner
       `typealias Items`: each spelling resolves to its own declaration; a nested `FixedArray` alias used
       as a `from` head matches its sweep entry.
     - #217: the five `specs/sendable-bounds.md` cases in #217's comment (`Pool with Outer.Step`,
       `Pool with Outer.Span`, `Cell with Outer.Step`, `Pool with Digits.Step`, `Pool with Digits.Pair`),
       each exactly one E3215 and no E3011; `Outer.Step` and `Outer.Span` in parameter and
       `Array with` positions resolve.
     - #185: stdlib tier, file-scope `typealias B = __ManagedMemory with Byte` and a nested
       `typealias B = Array with Int`: a `B` parameter in that type has the `Array` surface.
     - #189: `tests/ladders` `genmemberlookup.sh 16 fields` reads equal parse allocations under eight
       `MAXON_HASH_SEED` values; a program whose `typealias Count` clashes with the stdlib's allocates
       the same per reference as one with a unique name; `tests/ladders/README.md` says a hand-run ladder
       pins `MAXON_HASH_SEED` and `MAXON_HASH_TIER`.

6. **The language server asks the resolver.**
   `LspHover.declaredTypeArgument` passes its site (the enclosing type or extension) to stage 5's door.
   - Area: `Compiler/Lsp/LspHover.maxon`.
   - Depends on: 5.
   - Discharges: #172.
   - Shown by: a `tests/lsp` case hovering `Items` in `let items = Items from [..]` inside
     `type Bag uses T` declaring `typealias Items = Array with T` shows the type argument.

7. **A door refuses a name by the kind it resolved to.**
   A ranged alias used as a `from` head or a static base gets its own diagnostic: a ranged alias has no
   `from` construction and no statics. One new error code in the registry, with its documentation.
   - Area: `ParserExpressions.maxon` (`parsePrimary`'s named-primary arm), the static-call path for a
     base that names no type, `ErrorCodeRegistry.maxon`.
   - Depends on: 5.
   - Discharges: #180.
   - Shown by: `specs/inner-alias-construction.md`'s `error.an-inner-ranged-alias-has-no-static-create`
     and `error.an-inner-ranged-alias-takes-no-from` re-pinned from E3004 and E2004 to the new code, and a
     file-scope twin of each.

8. **One mismatch renderer.**
   Design point 4. Every "expected X, got Y" and "X and Y are different typealiases" site hands both
   types and the reader to it. `deriveInstanceDisplayNames` stops choosing one name per instance for the
   program; the name is chosen for the reader.
   - Area: `Queries.maxon` (the diagnostic texts), `SemanticCheck.maxon`, `ParserStaticCalls.maxon`,
     `SignatureIndexInstances.maxon` and `SignatureIndexSettle.maxon` (display names, `originNote`).
   - Depends on: 1 (the layer in a note). Runs beside 5 only if 5 does not hold the two index files.
   - Discharges: #68, #122, #123.
   - Shown by:
     - #122: `typealias Labels = Array with String` passed where another type is expected names `Labels`
       or the structural spelling, never `TlsPemLabelArray`.
     - #123: two layers each declaring nested `Box.Slot`, mixed by one operator: the message tells them
       apart.
     - #68: inside `type Pair uses U` with nested `typealias UPair` and `typealias PairGen`,
       `PairGen.create(UPair.create(...))` compiles, or its E3005 names two different types. If the
       rebuilt program shows the refusal itself is wrong, that is a new known issue, filed from this
       stage.

9. **A type-name id carries its space** (`/plan-and-land`; open question 2).
   - Area: `TypeNameInterner.maxon`, `IR/Maxon/MaxonDialect.maxon`, the remap and merge code, every site
     that turns a `TypeNameId` into an index.
   - Depends on: none in this plan; it must not run beside stage 1 or 5 (shared files).
   - Discharges: #214.
   - Shown by: a compiler self-test that hands a file-interner type to each `ProgramSignatures` reader of
     `self.typeNames` and expects the panic; on #206's parent shape (a `regions-recover` program with 9
     extra typealiases before `Box`), a reintroduced mix panics on every target instead of answering by
     name order. Size, from #214: about 1,640 `MaxonType` references in 55 files, 414 `TypeNameId`
     references in 42 files, about 57 `ProgramSignatures` methods reading `self.typeNames` with no
     interner parameter.

Stages 2, 3, 4 and 9 are independent of each other; 6 and 7 are independent after 5.

## Members

- #12 — the corpus-only function-valued field names classify a type by `stdlibDeclaredTypeNames`; stage 1.
- #29 — a library forwarder's promise-store facts resolved by member name over author methods; stage 3.
- #54 — an extension static replaces a primitive's built-in static with no duplicate error; stage 4.
- #68 — E3005 "expected 'Pair.UPair', got 'Pair.UPair'"; stage 8.
- #74 — a scriptlib function alias named like a stdlib one shares its instance-view entry (suspected);
  stage 1.
- #122 — an E3005 message names a type by an unrelated library alias; stage 8.
- #123 — two layers' same-named nested aliases read alike in an operand-mismatch message; stage 8.
- #172 — hover on an alias declared in a generic type or extension body looks up the bare name; stage 6.
- #173 — inner alias keys collide with directory-qualified names; the sweep matches `from` heads by raw
  name (both suspected); stage 5.
- #180 — a ranged alias as a construction head or static base is "undefined variable or function";
  stage 7.
- #185 — a parameter typed by a buffer alias reads the file-scope alias before the nested one; stage 5.
- #189 — resolving a top-level alias name the stdlib also declares allocates per reference, by hash seed
  (mechanism not yet located to a line; the stage locates it first); stage 5.
- #205 — a library cache entry records a user file's path as a tuple layout's file; stage 2.
- #214 — a type-name id does not record its interner; stage 9.
- #217 — a function or tuple alias nested in a type is "Unknown type" when named `Outer.Alias`; stage 5.

#18 is this issue.

## Related, not folded

- #36 — design issue. Its stages 1 and 3 build on stages 1, 5 and 9 here.
- #37 — design issue. Lands after stage 1, over stage 5's resolver; its open rule is open question 1.
- #153, #159, #220, #294 — the second class above.
- #51 — `targetProvidesEveryFacility` is recomputed per function; closes with R3 only if R3 deletes it.
- #211 — E3005 with both sides printing one function type: the expected result is that the call
  compiles, and the suspected cause is function-type equality across a generic extension's substitution.
  Stage 8 makes the message say what differs; the false refusal is its own defect.
- #163, #186 — a library pack is keyed by a rendered file name: the same shape, but the object is a cache
  pack and the fix point is `LibraryCacheStore`.
- #133 — a `RequiredRuntime` function is kept from the inliner only: a roster one pass reads, fixed in the
  interprocedural passes (#115 ruling 1).
- #81 — the lock check answers "no lock" for an unrecognised exclusive site: fixed in `exclusiveLockBit`.
- #157 — the scheduler's ordering checks cover only `installGtRuntime`'s functions, and whether the async
  releaser's plain load is a defect is unsettled. A candidate for R5's "after the last installer" point,
  not decided by it.
- #158 — emitted order follows the stdlib's absolute path: fixed in `serviceCompanionRoster`'s order;
  stage 1's `SourceFile` gives it a path-independent key.
- #188 — member lookup by hand-written scans beside `MemberRoster`: a member-roster class.
- #61 — what remains is the `SharedValue` cell over a type parameter, not the alias door.
- #263 — "Unknown type 'Zr2.handle'" in a service cycle: cause unestablished.
