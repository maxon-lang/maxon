# One verdict per type supplies equals, compare, hash and clone, and every door reads it; each Map and Set hashes under its own seed

Class issue: #312 · Priority: P1

## Stage order

One stage is one local commit, which ticks its box. A stage starts when every stage it waits on is ticked
here and every issue it waits on is closed. The plan lands once, after its last stage.

| Done | Stage | Rule it establishes | Waits on | Not beside | Needs a ruling |
|---|---|---|---|---|---|
| [ ] | 1 | MAXON_HASH_SEED's reference states both consequences of a pin: no collision resistance, and predictable HashSeed.random() seeds |  |  |  |
| [ ] | 2 | The compiler supplies structural equality: one memoised verdict per type answers == , equals, the Equatable conformance, where checks and diagnostics | [map-string-cost](map-string-cost.md) 2 | [map-string-cost](map-string-cost.md) 2 |  |
| [ ] | 3 | The compiler supplies structural ordering from the same walk: compare and the ordering operators, lexicographic in declaration order | 2 | 4 |  |
| [ ] | 4 | Hashable is hash(sink HashSink), supplied structurally or written by the type; the process fold, HashValue and spreadHash are removed (the flip) | 2, 3 | 3 |  |
| [ ] | 5 | Each Map and Set draws its own seed at creation from the hash runtime's one seed-draw door; a clone keeps its source's seed | 4 |  |  |
| [ ] | 6 | One library hash: Hasher, seeded by default with a fresh random HashSeed, with seed(), reset(), combineValue and a 64-bit HashDigest | 5 |  |  |
| [ ] | 7 | FilePath's equals and hash fold case unit by unit without allocating the lower-cased comparison spelling | 4 |  |  |
| [ ] | 8 | clone is the verdict's fourth answer: Cloneable follows the 11a.5 clause rules and the clone refusal reads the verdict's reason path | 2 |  | Is clone accepted into the verdict now (open question 1: accept as the verdict's fourth answer with the 11a.5 clause rules, or leave it on the Backlog and keep three answers)? Needs a ruling on #78. |

## Problem

Whether a type has equality, an ordering, a hash or a copy, and what each one computes, is decided door by door, and the doors can disagree.

- **Equality has several owners.** The `==` operator lowers a `String` to `__str_eq` (`emitStrEqCall`) and an `Array` through `emitArrayEqualityCall`; a method call and an `Equatable` witness resolve `equals` through the intrinsic conformance table (`conformsIntrinsically`, `intrinsicConformanceProtocols`, `DiscoveredConformer`) or to a body the type wrote; a record with no `equals` is refused by a message of its own (`TypeRules`). `stdlib/String.maxon` declares `equals` as a library body while `BuiltinConformanceRuntime` builds a second graph under the same impl name.
- **Hash is a public member each type writes.** `interface Hashable` is `hash() returns HashValue` (`stdlib/Interfaces.maxon`). `String.hash`, `FilePath.hash`, every user `hash()` and the compiler-synthesised `int.hash` / `float.hash` are separate bodies that nothing ties to the same type's `equals`. A 32-bit `HashValue` is observable, and `Map` and `Set` spread it with one process-wide function (`spreadHash`), so keys equal in it collide in every table.
- **Ordering is a third set of bodies** (`int.compare`, `float.compare`, `bool.compare`, a type's own `compare`), with no rule tying `compare` to `equals`.
- **`clone` is supplied by a fourth walk** (`__clone_<T>`), with its own refusal.
- **The library hash is two types.** `Hasher` (seedless, from the process fold, 32-bit result) and `KeyedHasher` (seeded, 64-bit) in `stdlib/Hasher.maxon`.

The rulings that replace this are #78 (rulings 11, 11a, 11.6a to 11.6c), with the page [Design-hashing](https://github.com/maxon-lang/maxon/wiki/Design-hashing). This issue is their implementation queue, in dependency order. #78 stays the ruling; nothing here changes it.

## Design

**One verdict per type.** For every type and every generic instance the compiler computes, once and memoised, one record with four answers: equality, ordering, hash and clone. Each answer is *structural*, *declared* (the type wrote the member) or *none, with a reason path* (the first failing field in declaration order, followed to a terminal reason from #78 ruling 11a.9's closed list). One synthesiser emits the structural members from one walk of the fields in declaration order (`__eq_<T>`, `__cmp_<T>`, the hash walk, beside `__clone_<T>`). The operator, the method call, the conformance, the `where` check, the table and the diagnostic all read the verdict; none decides on its own.

**Leaves are one closed table in the compiler**, with each leaf's equality, order and fold side by side: every integer and `bits` type, `bool`, `float`, `Character`, `String`, a payload-free enum, and `Array with E`. The stdlib/compiler twins of these members go.

**The verdict is linear.** It is memoised per type and per instance, recursive types are judged coinductively, and a chain of N records costs N. A ladder over a chain of nested records and a chain of nested generic instances shows it.

**What was read in Go, and what is taken.**
- `cmd/compile/internal/reflectdata/alg.go`: one `eqSignature(t)` drives both `geneq` and `genhash`, so a type's equality and hash come from one description and cannot disagree. Adopted as the one verdict and the one walk.
- `internal/runtime/maps/map.go`: every map has its own seed (`m.seed = uintptr(rand())`), passed to the hasher on each call. Adopted as stage 5; the seed comes from the hash runtime's one seed-draw door (#78 ruling 11.6b), never from a system call per table.
- `hash/maphash`: a hash object seeds itself randomly unless given a seed, with `Seed`, `SetSeed`, `Reset` and `WriteComparable`. Adopted as stage 6's `Hasher`.
- Departures, in Maxon's terms. Go has no user-written equality or hash; Maxon lets a type write its own (11a.5), because a type such as `FilePath` has a meaning the structure does not carry, and the sink of 11a.7 keeps the hash unobservable. Go panics at run time when an uncomparable value reaches `==` through an interface; Maxon refuses at compile time with the path, because the error must be a property of the program.

**Points the rulings settle that the member issues word differently.**
- `FilePath` stays a key through its own `equals` and a written `hash(sink)` (11a.5, and "`FilePathKey` as a requirement" is ruled out). No table is re-keyed.
- Ordering lands before the flip, because the flip's required specs sort each key shape (11a, "Before the flip lands").
- Each breaking change of 11a's "Breaking" list owes its `RELEASE_NOTES.md` entry with its `Fix:` in the commit that makes it.

### Open design questions

1. **Is `clone` accepted into the verdict now (stage 8)?** The proposal is on the Backlog without `accepted`. Options: (a) accept it as the verdict's fourth answer with the clause rules of 11a.5 (`implements Cloneable` means "I write `clone`"; a written `clone` is allowed where a structural one exists; an enclosing type's structural clone calls a field's written one; the refusal is the verdict's reason path, the closed list gaining "a `unique` field" and "a compiler-owned handle"); (b) leave it on the Backlog and build the verdict with three answers. Recommendation: (a). The verdict record and the walk exist after stage 2, 11a.11 already lists `clone` in the supplied-member roster, and a separate clone walk is the kind of second decision this class removes. It needs a ruling on #78 before stage 8 starts; stages 1 to 7 do not wait for it.
2. **The seed between stage 4 and stage 5.** After the flip and before each table draws its own seed, every table hashes under the process secret, which "a process-wide table seed" rules out as an end state. Options: (a) accept the interval, which is what tables do today; (b) land stages 4 and 5 as one commit. Recommendation: (a), with stage 5 assigned next after stage 4 and no release cut between them.

## Stages

Each stage is one `/land` commit, green alone. A stage whose library calls a member or a compiler-owned type the pinned seed does not know lands as a bootstrap chain through `land.yml`.

1. **`MAXON_HASH_SEED`'s reference states both consequences of a pin.**
   - Changes: the entry says a pinned process has no collision resistance and that the seeds `HashSeed.random()` draws under a pin can be predicted by anyone who knows the pin.
   - Area: `docs/CLI_REFERENCE.md`, the regenerated site page.
   - Depends on: nothing.
   - Discharges: #198.
   - Shown by: the `tests/docs` gate and `website.yml`'s drift check; the sentence is checked by reading.

2. **The compiler supplies structural equality; one verdict answers every equality door** (11a.1, 2, 3, 5, 9, 10, 11).
   - Changes: the memoised verdict and the synthesiser; `==`, `!=`, `a.equals(b)`, the `Equatable` conformance, `where T is Equatable` and the diagnostics read it. Records, unions with payloads, tuples and generic instances gain structural equality with no declaration. `Map` and `Set` join the no-equality roster and gain `contentEquals`. The clause rules of 11a.5 are checked for `Equatable`, including an `equals` added by an extension. Errors: the new "has no equality" code with the path, E3017 with the path, E3066's added sentence, E3016's "remove the clause", E3005's moved refusals, E3130's coverage, an enum naming the interface or declaring the member refused. A hand-written `hash()` still compiles.
   - Area: `IR/Maxon/IrInterface.maxon` (the intrinsic conformance table), `ConformanceCheck.maxon`, `IR/Maxon/TypeRules.maxon`, the equality operators in `ParserExpressions.maxon`, the `__clone_<T>` synthesis site (the new synthesiser sits beside it), `Runtime/BuiltinConformanceRuntime.maxon`, `ErrorCodeRegistry.maxon`, `stdlib/Map.maxon` and `stdlib/Set.maxon` (`contentEquals` only), `specs/`, `docs/`.
   - Depends on: #82's String-equality stage (one String comparison reached alike through `==` and the witness), which edits the same String equality sites; never in parallel with it.
   - Discharges: #108.
   - Shown by: specs red first for `==` and `equals` on a record, a union, a tuple, a nested generic and a recursive type; a type holding a `Map` refused at `==` with the path; each clause error; the verdict ladder linear.

3. **The compiler supplies structural ordering** (11a.2, 4, 5, 8).
   - Changes: `compare` and `<`, `<=`, `>`, `>=` come from the same walk, lexicographic in declaration order. Every enum orders by case declaration order, raw values included. `String` and `Character` order by Unicode scalar value, a prefix first; `String` gains `compare` and the operators. `float`'s operators stay IEEE and a walk uses `float.compare`. A type that writes `equals` gets no structural `compare`. The documentation states the field-reordering hazard and the law of 11a.8.
   - Area: the verdict's ordering answer and the synthesiser, the ordering operators in `ParserExpressions.maxon`, the leaf table, `stdlib/String.maxon`, `stdlib/Character.maxon`, `stdlib/PrimitiveExtensions.maxon`, `specs/`, `docs/`, `RELEASE_NOTES.md` (a raw-valued enum's order).
   - Depends on: stage 2. Never in parallel with stage 4.
   - Discharges: #112.
   - Shown by: specs red first for `compare` and the operators on each shape of stage 2, sorted; a raw-valued enum sorted by declaration; the union operators still E3066 with the `compare` sentence.

4. **`Hashable` is `hash(sink HashSink)`: supplied structurally or written by the type** (11.1, 11a.5 to 11a.9, 11a.12). This is the flip.
   - Changes: `interface Hashable extends Equatable` requires `hash(sink HashSink)`. `HashSink` is compiler-owned with no public member, constructor, equality, `clone` or `toString`. Removed: `hash() returns HashValue`, `HashValue`, `spreadHash`, the seedless process fold and the stdlib/compiler hash twins. The leaf table holds each leaf's fold beside its equality and order. `Array`'s conformances split: `Equatable` when its element is, `Hashable` when its element is. A tuple of `Hashable` elements is a key. `where K is Hashable` is the whole constraint. `Map` and `Set` hash a key through the sink into a 64-bit internal hash. `FilePath` writes `hash(sink)`. `0.0` and `-0.0` hash alike. E3187's no-writing rule binds `hash`. Every `hash()` in the compiler's own source moves.
   - Area: the verdict's hash answer and the synthesiser, `Runtime/HashRuntime.maxon` (the sink's entries), `stdlib/Interfaces.maxon`, `stdlib/Map.maxon`, `stdlib/Set.maxon`, `stdlib/Hasher.maxon`, `stdlib/String.maxon`, `stdlib/Character.maxon`, `stdlib/Array.maxon`, `stdlib/PrimitiveExtensions.maxon`, `stdlib/FilePath.maxon`, `stdlib/helpers/hash`, `stdlib/helpers/string/hash.maxon`, every `hash()` under `maxon-bin/`, `specs/`, `docs/`, `RELEASE_NOTES.md`.
   - Depends on: stages 2 and 3.
   - Discharges: #109.
   - Shown by: the specs 11a requires before the flip: structural `equals`, `compare` and `hash` agree for a record, a union, a tuple, a nested generic and a recursive type, each used as a key and sorted; `HashSink` has no public surface; a hand-written `equals` with no `hash` is refused as a key; a type holding a `Map` is refused as a key and at `==`; output is byte-identical under two seeds (the `tests/cli` two-seed gate). The scale ladder is measured, because integer keys change cost.

5. **Each `Map` and `Set` draws its own seed** (11.4, 11.6, 11.6b, 11.6c).
   - Changes: a table draws its seed when it is created, from the table stream of the hash runtime's one seed-draw door (a second `HashSeedStream` case with its own atomic index). A clone keeps its source's seed. No API returns it. A draw is an effect: it is never classed effect-free and ends no hold.
   - Area: `Runtime/HashRuntime.maxon` (the table stream), `stdlib/Map.maxon`, `stdlib/Set.maxon`, `specs/`, `docs/`.
   - Depends on: stage 4.
   - Discharges: #79. Its "collision resistance covers byte-hashed keys only" sentence does not stand in `docs/STDLIB_REFERENCE.md`, so nothing is removed for it.
   - Shown by: two tables holding the same keys can iterate in different orders; a pinned run repeats, table for table; inserting one table's keys into another in iteration order stays linear (a ladder); compiler output stays byte-identical under two seeds.

6. **One library hash: `Hasher`, with a seed that defaults to a fresh random one** (11.5, 11a.12).
   - Changes: `KeyedHasher` becomes `Hasher`. `create` and `empty` take a `HashSeed` defaulting to `HashSeed.random()`. Added: `seed()`, `reset()`, `combineValue(value)` and `Hasher.combinedValue` for any `Hashable` value, driving `value.hash` with a sink over the `Hasher`'s own state. Removed: `resume`. `finalize()` returns the 64-bit `HashDigest`. Every compiler call site moves.
   - Area: `stdlib/Hasher.maxon`, the call sites under `maxon-bin/` and `stdlib/`, `specs/`, `docs/`, `RELEASE_NOTES.md`.
   - Depends on: stage 5 (both edit `Hasher`, `Map` and `Set`).
   - Discharges: #110.
   - Shown by: specs red first for the default seed (two `Hasher.create()` folds of the same steps differ unpinned and repeat under a pin), `seed()`, `reset()` and `combineValue` agreeing with the type's `hash`.

7. **`FilePath`'s `equals` and `hash` fold case without allocating.**
   - Changes: `equals` and `hash(sink)` compare and fold the comparison spelling unit by unit. Neither builds the lower-cased copy that `comparisonSpelling()` allocates at every call.
   - Area: `stdlib/FilePath.maxon`.
   - Depends on: stage 4.
   - Discharges: #107.
   - Shown by: an allocation census spec: a lookup in a `Map` keyed by `FilePath` allocates nothing, on a case-insensitive host as on a case-sensitive one.

8. **`clone` is the verdict's fourth answer.** Starts only after open question 1 is ruled.
   - Changes: `Cloneable` follows the clause rules of 11a.5; the clone refusal reads the verdict's reason path.
   - Area: the verdict, the `__clone_<T>` synthesis site, `ConformanceCheck.maxon`, `specs/`, `docs/`.
   - Depends on: stage 2, and the ruling.
   - Discharges: #111.
   - Shown by: specs red first for a written `clone` on a type holding a `unique` field, a structural clone calling a field's written `clone`, and the field-path refusal when neither exists.

## Members

- #108 — structural equality and the one verdict; stage 2.
- #112 — structural ordering from the same walk; stage 3.
- #109 — `hash(sink HashSink)`, the leaf table and the removals; stage 4.
- #79 — a seed per table; stage 5. Its specs: two tables with the same keys can differ in order; a pinned run repeats; inserting one table's keys into another in iteration order stays linear.
- #110 — one `Hasher`; stage 6.
- #107 — `FilePath`'s `equals` and `hash` without the per-lookup lower-casing allocation; stage 7.
- #111 — `clone` under the same rule; stage 8, after its ruling. Its four points to decide are open question 1.
- #198 — the `MAXON_HASH_SEED` reference; stage 1.

## Related, not folded

- #78 — the hashing rulings. This issue implements them.
- #106 — generic types compiled per instantiation; a ruling, gated by #82's measurement.
- #209 — a seedable PRNG. It is a library generator for programs; the hidden generator behind seed draws is not it.
- #82 — Map, String and hash cost against Go. Its String-equality stage lands before stage 2 here; its short-key hash stage and its table-storage stage start after stages 4 and 5 here.
- #86 — `Hasher.create()` heap-allocates. Its mechanism is a non-escaping record reaching the heap, which stage 6's rename does not change.
- #237, #189 — table walks in the compiler whose effect follows the process seed. They are instances of #78 ruling 10a's ordered-walk rule, not of the verdict.
- #212 — a bound on `Key` inside `extension Map where Key is Comparable` does not reach `Array with immutable Key`. It is a constraint-propagation defect; stage 3 does not touch it.
- #60 — the CPU-feature probes enumerated by hand. It concerns the tier probe, not the hash surface.

- #111 is on the Backlog, not yet accepted: it stays open and is not folded until it is.
