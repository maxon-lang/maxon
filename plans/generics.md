# A generic body is judged once, from its bounds alone, and every instance the program writes or infers is judged once at one door

Class issue: #310 · Priority: P0

## Goal

A generic body's verdict and its emitted code depend on its bounds alone, never on which instances the rest of the program names, and every instance the program writes or infers is judged exactly once.

- **Done when:** every member's program gets its ruled verdict at the declaration or at the instance's own base name; `valueIsManagedHeap` and the direct `typeParameter` tag comparisons are gone; no body reads the program's instance rows.
- **Not the goal:** specialisation, faster generic code, or a new generics feature. Cost of generic code is `map-string-cost`'s.
- **Between two designs that both satisfy a stage, take** the one that makes "what does an opaque value do here" impossible to leave unanswered, and that refuses at the declaration, naming the bound, whatever the bounds cannot prove.

## Stage order

One stage is one local commit, which ticks its box. A stage starts when every stage it waits on is ticked
here and every issue it waits on is closed. A `deferred` stage is skipped. The plan lands once, after its
last stage.

| Done | Stage | Rule it establishes | Waits on | Not beside | Needs a ruling |
|---|---|---|---|---|---|
| [ ] | 1 | Every generic instance is interned with its origin and judged once, at one door, whether written or inferred |  |  |  |
| [ ] | 2 | What `T` may be in a body is answered by the bounds in lexical scope alone, never by the program's instance rows | 1 |  |  |
| [ ] | 3 | One value-kind classification, with an opaque arm and no default, answers every ownership and holding question | 2 | 4, 5 |  |
| [ ] | 4 | A bare-base call infers once, after defaults, through one translation of a value's type into a type argument | 1 | 3, 5 | How does a `spawn` start a `sendable`-bounded generic service at an `immutable` record (#192)? Needs a ruling on #30 (recommended: inference takes the argument's capability). |
| [ ] | 5 | A member read through a concrete instance is typed through its receiver's arguments before any predicate judges it | 1 | 3, 4 |  |
| [ ] | 6 | A generic function body is checked once at its declaration against its `where` bounds, whether or not anything calls it | 2, 3 |  | Are generic function bodies checked once at their declaration, against `where` bounds? (recommended: yes) |
| [ ] | 7 | Every per-instance type fact is memoized once per `GenericInstanceId`, and every walk asks the table | 1 |  |  |
| [ ] | 8 | A spawn at the enclosing generic type's parameters compiles and runs at every instantiation (`/plan-and-land`) | 4, #106 |  | How does a `spawn` inside a generic body start a service at the enclosing type's parameters (#242)? (a) under #106 the body is concrete per instantiation; (b) without #106 the descriptor carries the behaviour pair. Decide after #106's gate is read. |

## Rulings

- Open question 1: #30 ruling 25 is enforced at the instance door. A generic with no bound is not instantiated at a promise; a unique argument needs `where T is isolated`. The programs of #190 and #63 are correct refusals.

## Problem

A generic type is checked and compiled once: its methods are one shared body in which a value of type `T` is opaque (`ValueTypeTag.typeParameter`), and each `Base with Args` the program names is an instance row. #30 rules that this is how a generic is judged ("generic bodies are checked once against `T` with only its bound's capabilities, so errors stay at the declaration"; an instance is judged where it is written). The compiler has no single owner for either half of that rule, so each rule about a plain type carries its own type-parameter arm, and each arm is a place the generic case can be missed.

Four decisions are made in several places that can disagree:

1. **"What is this opaque value?"** `Parser.valueIsManagedHeap` is false for every type parameter. Sites that need the opaque case add it by hand: `valueMayNameARecord`, `borrowedValueOwesAHandoffReference`, and about 70 direct `tagOf(…) == ValueTypeTag.typeParameter` comparisons across the parser and the signature index. Sites that forgot it answer as for a scalar: `argumentHoldsAPromise` (#272), `finishValueTry` (#271), `requireLinearArgumentIsOwned`, the `for` source hold in `ParserControlFlow`. The retain question alone has five dispatch points (`retainCalleeForProtocol`, `retainCalleeOf`, `holdRetainPlan`, `retainPlanOf`/`emitRetainCall`, `ownedFormOfBorrowedValue`) (#277).
2. **"What may `T` be here?"** Three sources answer, and a rule picks one by accident:
   - the declaring type's `where` list alone (`opaqueParamDischarge`, `opaqueElementConstraintCheck`), which ignores the `where` clause of the extension the code stands in (#212);
   - the whole program's instance rows, asked from inside the shared body (`typeParamFeedGreenThreadInstantiation`, `typeParamFeedCanOwnAReference`), which makes a body's verdict and its emitted code depend on which instances the rest of the program names;
   - nothing: a predicate asked of a field's declared type `T` before the receiver's arguments are substituted (the promise-slot test in the field read of `ParserCalls`, asked before `slotTypeThroughReceiver`) (#190).
3. **"Which instances exist, and who judges them?"** Every road that makes an instance ends in `ProgramSignatures.internRow`, which records no origin. A site is filed only by `recordGenericAlias` (an alias's outer instance) and `recordInferredInstantiation` (four callers). `checkOneInstantiation` runs once per filed site, so an instance made by any other road is never judged: a nested `Inner with (…)` header (`internNestedNode`) gets region bounds through a partial walk but neither E3017 nor the linear-argument check (#222); a bare base name bound to the enclosing scope (`scopedGenericInstanceOf`) gets nothing (#269). That last road binds by the *spelling* of the parameters: `Pool.create(x)` inside `type Outer uses T` becomes `Pool with Outer.T` only when `Pool`'s parameter is also spelled `T`.
4. **"What type argument does this value give?"** `typeArgumentOfValue`/`spawnTypeArgumentOfValue` (factory inference), `literalColumnTypeFor` (container literals) and `genericArgumentOf` (generic-function unification) each translate a value's type into a type argument and differ on `exitCode`, function values, type parameters, deferred values and float aliases; none carries the value's capability (#155, #192). Factory inference itself runs twice per call, once before defaulted arguments are filled (#154). Nine functions answer "which type parameters does this type mention", and the share walk and the `sendable`-bound walk disagree on a tuple alias (#276). No per-instance fact is memoized, so each walk re-substitutes the chain beneath every instance (#236, #268).

Generic functions are the mirror image: a template's body is never parsed at its declaration (`readGenericTemplateAt` reads the head; `recordTemplateParamReassigns` scans the body for written parameters only) and is parsed from tokens once per inferred type. A function nobody calls is therefore never checked: a syntax error, an undefined name and a wrong `end` label all compile (#17).

## Design

Maxon's generics follow the languages that check a generic body once against its constraints and treat specialisation as a lowering step: Go (the type checker judges the generic body; stencilling by shape with dictionaries comes after), Swift (checked once; value witnesses own a `T`; specialisation is an optimisation) and Rust (checked once against trait bounds; monomorphised afterwards). Checking per instantiation is the C++ template model, whose known costs are the ones recorded here: errors that depend on the caller, and bodies nobody checked. Maxon adopts the first model for types and functions alike, and departs from Go and Swift in one place its own rules require: a verdict that would differ between instantiations is not deferred to run time or to the instantiation, it is a compile error at the declaration that names the bound to add (#30; "a deterministic error that names the fix").

The rule: **a generic body is judged once, from its bounds alone; every instance the program writes or infers is judged once, at one door.** Five single points carry it.

1. **One classification of a value's kind.** A `match` over a closed kind set that includes `opaque`, with no default arm, answers every ownership and holding question (owns heap, may name a record, may hold a promise, how it is retained, released and moved). `valueIsManagedHeap` and the direct tag comparisons are removed; a new door cannot be written without saying what an opaque value does there. The retain and drop spellings of #277 collapse into the one retain table and one field drop-kind answer this classification reads.
2. **One bounds environment.** "What may `T` be here" is answered by the bounds in lexical scope: the declaring type's `where`, the enclosing extension's, the generic function's. Every opaque predicate reads it, and it has three answers: yes, no, and *not provable from the bounds*, which is a refusal at the declaration naming the bound. No body asks the program's instance rows; `typeParamFeedGreenThreadInstantiation` and its siblings are deleted, and with them the re-parse a moved answer forces.
3. **One instance ledger.** `internRow` is reached only through a door that takes the instance's origin: written at a token, inferred at a call, or derived from another instance (which blames its origin, as the blame edge does today). `checkOneInstantiation` runs every per-instance check (E3017, linear argument, region bounds) on each written or inferred instance, blamed at its own base name. An alias name used as an argument is judged at its own declaration only. The `writtenInside` partial walk and the region member walk are deleted. A bare generic base name other than the declaring type's own never binds through the scope's parameter spellings; it goes to inference.
4. **One inference door.** A bare-base static call infers once, after defaults are filled, through one translation of a value's type into a type argument that every road asks (factory inference, container and tuple literals, generic-function unification). It answers deferral itself, crosses interners in one place, and interns and sites an instance whose arguments are the enclosing type's parameters.
5. **One memoized fact per instance.** Mentioned parameters, share-walk verdict, promise reach and conformer reduction are facts of a `GenericInstanceId`, computed once from the facts of its arguments. Every walk asks the table; none substitutes a chain itself.

A read through a concrete instance is typed first and judged second: `slotTypeThroughReceiver` runs before any predicate on the member's type.

**Relation to #106.** #106 (compile a generic type per instantiation) is a lowering decision and stays one. It is not this class's owner and discharges one member only (#242, see question 4). Its open point "is an instance checked once as its template or again as concrete code" is answered here and by #30: once, as its template. That also removes the three refusals #106 lists as blockers for a concrete re-check (E3106, E2010, E2015), and makes per-instantiation compilation a clone of an already-checked body with its descriptor and witness words constant, which is what the called-once splice already produces. #106's measurement gate remains a performance gate and is unchanged; no correctness member waits on it.

### Open design questions

1. **May an unbounded generic be instantiated at a promise?** #30 ruling 25 says a generic without `where T is isolated` cannot be instantiated with a `unique` type, and a promise is unique. #190 and #63 instantiate unbounded `Box` and `Maker` at a promise and expect the program to compile; today the instantiation is accepted and a later rule refuses or misjudges it, and E3141 is documented as refusing some bodies "where the program instantiates it with a promise".
   - (a) Enforce ruling 25 at the instance ledger: both repros are E3215 at the alias, naming `where T is isolated`. Inside an unbounded body `T` never holds a promise; inside an `isolated`-bounded body it may, and every promise rule is judged at the declaration. Costs: programs that hold promises in unbounded user generics add the bound; an `## Unreleased` entry.
   - (b) Keep promise instantiation of unbounded generics and judge promise rules per instantiation. Costs: contradicts "checked once"; keeps the in-body program queries.
   - Recommendation: (a). It is what #30 already rules.
2. **Are generic function bodies checked once at their declaration, against `where` bounds?** Today a body is checked per inferred type, so `a > b` needs no bound when every caller passes a number.
   - (a) Yes: one rule for types and functions. Costs: a breaking change (an operation on `T` needs its bound written); the template is parsed with `T` opaque; an `## Unreleased` entry.
   - (b) No: only the syntactic layer (block structure, labels, names that resolve without `T`) is checked at the declaration. Costs: type errors in an uncalled generic function stay unreported; two checking models remain.
   - Recommendation: (a).
3. **How does a `spawn` start a `sendable`-bounded generic service at an `immutable` record (#192)?** Needs a ruling on #30.
   - (a) Inference takes the capability of the argument's value (a frozen argument, or an `immutable T` parameter, gives `immutable Job`).
   - (b) `spawn` may name an instance alias. Costs: service discovery reads raw tokens with no scope and must resolve aliases.
   - Recommendation: (a), in the one inference door, so every inferred instance carries its argument's capability; no second spelling.
4. **How does a `spawn` inside a generic body start a service at the enclosing type's parameters (#242)?** A generic service's `__behave`/`__finish` pair is already synthesized per spawned instance; an opaque instance has none.
   - (a) Under #106 the enclosing body is concrete per instantiation and the spawn names a concrete instance; nothing else is needed.
   - (b) Without #106, the enclosing instantiation's descriptor carries the behaviour pair for each service it spawns over its parameters.
   - Recommendation: decide after #106's gate is read; (a) if it is confirmed, (b) if it is withdrawn. The static half (the instance is sited and its bound judged at the spawn) lands in stage 4 either way.

## Stages

Each stage is one `/land` and one commit.

**1. The instance ledger.** Every instance is interned with its origin; `checkOneInstantiation` runs every check on every written or inferred instance; the bare-base scope binding by parameter spelling goes.
- Area: `SignatureIndexGenericTypes.maxon` (`internRow`), `SignatureIndexSettle.maxon` (`recordGenericAlias`, `internNestedNode`), `SignatureIndexInstances.maxon` (`scopedGenericInstanceOf`, `internGenericInstanceOf`, `internInstanceOverArgsOf`), `ConformanceCheck.maxon` (`checkWhereConstraints`, `checkOneInstantiation`, `recordInstancesWrittenAtSites`, `judgeInstancesWrittenAt`), `ParseStaging.maxon`; `specs/generic-instance-conformance.md`, `specs/regions-generic-flows.md`.
- Depends on: none. If question 1 is answered (a), the unique-argument refusal is one more check at this door.
- Discharges: #269, #61. #222's part is on `main`: every written `Base with Args`, a nested inline one included, and a scoped bare name in a type position file a site, the judge reads sites only, and the member, container and tuple walks and the written-instance pre-pass are deleted. What remains is the instance a body infers (#269), the bare-base scope binding by parameter spelling, and #61. A "Shown by" case that is already green needs no work.
- Shown by, red first:
  - `typealias StepPools = Array with (Pool with Step)` (`Pool uses T where T is sendable`, `Step` a function alias) is one E3215 at the inline `Pool`; `typealias Boxes = Box with StepPool` reports nothing at `Boxes`; a phantom argument `Keeper with (Pool with Step)` is refused; `Array with (Map with (Unhashable, Tally))` is E3017, and a nested linear argument is refused (#222).
  - `type Outer uses T` (no bound) whose method does `let pool = Pool.create(self.item)`, with `typealias JobOuter = Outer with Job` over a plain record: E3215 in `Outer` naming `T` and `where T is sendable`, identically when `Pool`'s parameter is spelled `T` or `V` (#269, P0: today the same-name spelling compiles and runs a `Pool with Job`).
  - #61's program (a `typealias TCell = SharedValue with T` inside `type Maker uses T`, `TCell.create(x)`) is refused by a diagnostic at the alias and does not panic in `genericAliasInstanceFrom`; with `Maker` bounded `sendable`, `Maker with Holder` is E3215 at its alias and a walkable `T` compiles and runs.
  - The parser is confirmed to accept an inline `with` only on a typealias's right-hand side.
  - Scale: a ladder of N nested instance aliases stays linear in `checkDeclarations`.

**2. The bounds environment.** One answer for "what may `T` be here", read by every opaque predicate; in-body program queries are deleted.
- Area: `ConformanceCheck.maxon` (`opaqueParamDischarge`, `receiverMeetsExtensionConstraints`), `SignatureIndexInstances.maxon` (`opaqueElementConstraintCheck`, `instanceWithTypeArgKind` and the `typeParamFeed…` queries), `ParserTypeDeclarations.maxon` (`typeParamHoldsAGreenThread`, `requireCapturedOpaqueHoldsNoGreenThread`, `noteFeedStored`), `ParserStaticCalls.maxon` (`requireReceiverSatisfiesExtensionConstraints`); `specs/generic-types.md`.
- Depends on: stage 1; question 1 for the promise queries (the `where`-clause half lands without it).
- Discharges: #212.
- Shown by: inside `extension Map where Key is Comparable`, a method builds an `Array with Key` (and `Array with immutable Key`) and calls `sortUnstable()`: compiles and sorts; the same body in a plain `extension Map` is E4006 naming the bound. A capture of an opaque `T` stored or returned by a closure is judged from the bound, with one verdict whatever the program instantiates.

**3. One value-kind classification with an opaque arm.**
- Area: `ParserTypeDeclarations.maxon` (`valueIsManagedHeap`, `valueMayNameARecord`, `holdRetainPlan`), `ParserOwnership.maxon` (`retainPlanOf`, `emitRetainCall`, `emitRetainExistentialCall`), `ParserCalls.maxon` (`argumentHoldsAPromise`, the capture release test), `ParserPromises.maxon` (`requireLinearArgumentIsOwned`, `enrolOwnedCallTemp`), `ParserStaticCalls.maxon` (`keepFieldSourceAliveForItsRead`, `valueHasACapability`), `ParserControlFlow.maxon`, `Runtime/MmRuntime.maxon` (`dictionaryFieldDropKind`); `specs/generic-opaque-*.md`.
- Depends on: stage 2. Shares `finishValueTry` with the issue that owns #271; whichever lands second asks the classification.
- Discharges: #272, #277.
- Shown by:
  - #272, first step a repro: a generic body passes a value of type `T` to an `async` call at a parameter the callee only reads. Under question 1(a): with `T` bounded `isolated`, E3141 at the declaration; unbounded, it compiles and no promise instantiation exists. Raise or drop its P0 on the repro.
  - `requireLinearArgumentIsOwned` and the `for` source hold each get a case with an opaque value.
  - #277: "managed or opaque field" has one answer shared by the read gate and the destructor; the existential retain arm is written once; no site spells `valueIsManagedHeap(v) or tag == typeParameter`.
  - No direct `ValueTypeTag.typeParameter` comparison remains in an ownership decision.

**4. One inference door and one type-argument translation.**
- Area: `ParserStaticCalls.maxon` (`spawnFactoryConstructorInstance`, `instanceOfFactoryTypeArguments`, `factoryTypeArgumentAt`, `typeArgumentOfValue`, `spawnTypeArgumentOfValue`, `literalColumnTypeFor`), `ParserCalls.maxon` (`parseCallNamed`, `emitResolvedCall`, `unifyGenericArguments`, `genericArgumentOf`), `ParserContainers.maxon` (the literal roads' deferral guards); `specs/generic-types.md`, `specs/services.md`.
- Depends on: stage 1; question 3 for the capability half.
- Discharges: #154, #155, #192; the static half of #242.
- Shown by:
  - `Wrap.create()` whose defaulted parameter fixes `T` (`create(held T = 3 as Tally)`): one inferred instance, one site, and `storedSlotCandidates` asked against that instance (#154).
  - `Wrap.create(function(n Tally) gives twice(n))` on a bare generic base interns no file-interner id into the program index, and a container literal and a factory call given the same value name the same type argument, for `exitCode`, a function value and a float alias (#155).
  - With `Keeper uses T where T is sendable` and `create(item immutable T)`: `spawn Keeper.create(freeze Job.create(1))` starts `Keeper with immutable Job` (question 3(a)); the three guard cases for an `immutable` argument at a bounded service position, a function value, an OS handle and an interface conformer, are each refused by the share side (#192).
  - `spawn Keeper.create(item)` inside an unbounded `Holder uses T` is E3215 at the spawn, naming `T`, `Holder` and the bound, reported once; the inferred instance is one written-instance site (#242).

**5. A member read through a concrete instance is typed before it is judged.**
- Area: `ParserCalls.maxon` (the field read's promise-slot registration and `slotTypeThroughReceiver`), `ParserDeclarations.maxon` (`forwardedStoreOf`), `SignatureIndexSettle.maxon` (`noteQualifiedCandidates`, `aliasBaseTypeName`); `specs/generic-type-substitution.md`.
- Depends on: stage 1; question 1 decides which form of each repro is the accepting one.
- Discharges: #190, #63.
- Shown by:
  - #190: `Box uses T` with `export var item as T`, `PendingBox = Box with Pending`, `await box.item`. Under question 1(a) with `Box` bounded `isolated`, the read is judged exactly as the same field in a plain record is; unbounded, the alias is E3215.
  - #63: `Maker uses T` with `typealias G = Gen with T` and `static function shape(held T, …) returns Shape` returning `G.create(held)`: the promise argument is seen as stored, with one verdict for `G.create(held)`, `Gen.create(held)` and the non-generic spelling. First step: confirm that the alias over `T` fails `aliasBaseTypeName`'s lookup; the three spellings differ today.

**6. A generic function is checked at its declaration.**
- Area: `ParserDeclarations.maxon` (`readGenericTemplateAt`, `recordTemplateParamReassigns`, `openGenericInstance`), `ParserTypeDeclarations.maxon` (`noteDeclarationCloser`), `GenericFunctions.maxon`; `specs/generic-functions.md`, `docs/LANGUAGE_REFERENCE.md`, `RELEASE_NOTES.md`.
- Depends on: question 2; stages 2 and 3 for 2(a).
- Discharges: #17.
- Shown by: a `public function pick(value T) uses T returns T` closed `end 'pikc'` and never called is E2008, as its instantiated twin and a `public static` method are; an uncalled generic function with an undefined name in its body is refused; under 2(a), `a > b` on an unbounded `T` is refused at the declaration naming `where T is Comparable`.

**7. One memoized fact per instance.**
- Area: `SignatureIndexGenericTypes.maxon` (`collectMentionedParameters`, `instanceTransferWalk`, `recordKindsWithinInstance`), `SignatureIndexSettle.maxon` (`parameterMentionOf`, `sharedGraphAnswer`, `registryValueKindOf`), `SignatureIndexInstances.maxon` (`instanceConformerReduction`, `conformerNameOfType`, the `typeMentions…` family), `TypeCycleCheck.maxon`, `TypeResolution.maxon`.
- Depends on: stage 1.
- Discharges: #276. #236 and #268 are folded into #308; the two ladders below are shared with its stages.
- Shown by:
  - #276: in `D uses T` with `typealias Pair = (T, Tally)`, a share door (`freeze`, a `SharedValue`, an `immutable` argument at a `sendable` position) demands the same proof of `T`'s bound for `Pair` and for `(T, Tally)` spelled out. P0 until the repro confirms or refutes that a door shares an unbounded `T` through the alias.
  - #236: N aliases `A1 = Array with A0 … AN = Array with A(N-1)`, and the `Box uses T` chain: `checkDeclarations` allocations at N = 200/400/800/1600 have differences that double (today 295,672 / 855,086 / 3,053,698 / 11,770,905 for `Array`, 119,954 / 183,456 / 430,468 / 1,404,475 for `Box`).
  - #268: the chain ladder (N records `Ci` each holding `C(i+1)`, one `Pi = Pool with immutable Ci` each) and the open-chain ladder (`Bi uses T` each holding `B(i+1) with T`, N instances `Pool with immutable (Bi with T)` in one bounded `Outer`) are linear in `checkDeclarations` (today ×3.9 and ×3.8 per doubling). The open chain's growth in `signatures`, `deriveRuntimeNeeds` and `synthesizeBodies` (46 s at N = 200) is measured again after this stage; what remains is filed on its own.

**8. A spawn at the enclosing type's parameters runs.** `/plan-and-land`.
- Area: `ServiceCompanions.maxon`, `IR/…/ServiceBehaviour` (`buildServiceBehaviours`, `spawnedInstancesByService`), `LayoutSource.maxon`.
- Depends on: stage 4; question 4 and #106's gate.
- Discharges: the rest of #242.
- Shown by: `Holder uses T where T is sendable` whose method does `let keeper = spawn Keeper.create(item)` compiles and runs at two instantiations; `error.a-spawn-inside-a-generic-body-is-refused` in `specs/services.md` is retired.

Stages 2, 4, 5 and 7 touch disjoint files after stage 1 and may run in parallel, except that stages 4 and 5 both edit `ParserCalls.maxon` and stage 3 edits most parser files, so 3, 4 and 5 are serial.

## Members

- #222 — a generic instance written inline as a type argument gets no instantiation check; stage 1.
- #269 — a `sendable`-bounded instance inferred in a generic body is never judged when the callee's parameter shares the caller's spelling (P0); stage 1.
- #61 — a generic alias of `SharedValue` inside a generic type panics at its `create`; stage 1. Its expected E3171 "naming `Holder`" is replaced by the declaration-side refusal.
- #212 — inside `extension Map where Key is Comparable`, an array over `Key` does not get `sortUnstable` (E4006); stage 2. Not caused by `immutable`: plain `Array with Key` fails alike.
- #272 — an `async` call is not refused when a type-parameter argument may hold a promise (suspected, P0 until run); stage 3.
- #277 — three retain and drop decisions each spelled in more than one place; stage 3.
- #154 — factory inference on a bare generic base runs twice per call; stage 4.
- #155 — a value's type is translated to a type argument by functions that disagree; stage 4. The issue's `indexTypeArgumentOf` is `typeArgumentOfValue`; `genericArgumentOf` is a third.
- #192 — a spawn cannot start a `sendable`-bounded generic service at an `immutable` record; stage 4, after question 3.
- #190 — awaiting a promise read from a generic type's field is refused where a plain record's field awaits; stage 5, after question 1.
- #63 — E3213 on a promise stored through a generic static, not on the non-generic spelling; stage 5.
- #17 — a generic function that is never instantiated is not checked; stage 6, after question 2.
- #276 — the share walk and the `sendable`-bound walk collect a tuple alias's parameters differently (P0 until run); stage 7.
- #242 — a spawn inside a generic body cannot start a generic service at the enclosing type's parameters; stage 4 (judged), stage 8 (runs).

## Related, not folded

- #236, #268 — the quadratic type walks; folded into #308, which keeps their answers per instance.
- #106 — compiling a generic type per instantiation is a lowering decision with a performance gate. This issue answers its "checked once or again" point and must land first, so that the clone is of a checked body.
- #82, #199, #200, #202, #207, #3 — what a stored element costs in a generic container; they have their own measurements and owner. #199's retain word is the same table stage 3 reads, and the two must agree.
- #271 — a `try … otherwise` fallback of type-parameter type may point into a freed temporary: one merge-edge function settles it. Stage 3 removes the predicate that misled it; the fix is not here.
- #65 — `parse` grows as doors × fields at `freeze`: the share walk per door, not per instance. Re-measure after stage 7.
- #55 — `implements BidirectionalIterator with X` is refused: `BidirectionalIterator extends Iterator` declares no `uses`, and `conformanceUsesArity` counts only an interface's own names. An interface-inheritance rule, not an opaque-value or instance decision.
- #211 — E3005 with both sides printing one function type in a generic `extension Map` method: does not reproduce from its description and has no located mechanism. Reduce it again after stage 2.
- #90 — a promise cast to another promise alias cannot be awaited: `rebrandValue` mints a fresh value without the promise's ownership mark. No type parameter is involved.
- #193 — a type argument cannot be written `isolated`: the capability-word set on a type argument, which #104 owns.
- #263 — "Unknown type 'Zr2.handle'": a service's handle type exists only when the program spawns the service; nothing generic. Probably a translator program with no `spawn`.
- #104, #102 — one canonicaliser for a capability word on a value type, generic arguments included.

- #222 is closed: its change is on `main` and is the first part of stage 1.
