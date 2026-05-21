# Hybrid Generics Plan for the Self-Hosted Compiler

## Strategic positioning

> **"Rust-class safety, Swift-class compile times."**

This document is the design + execution plan for getting Maxon there. The pivotal architectural choice is the *generics implementation strategy*, and the answer is a Swift-style hybrid: monomorphize layouts, dispatch methods through witness tables, recover monomorphized-quality code on hot paths via aggressive inlining.

This is the single most important design decision in front of the project. It determines whether incremental compilation can ever be transformative, whether binaries can be small, whether dynamic dispatch becomes a first-class language feature, and whether compile times stay competitive with Go-class languages while keeping Rust-class semantic safety.

## Context: where we are today

- **C# bootstrap (`maxon-sharp/`)**: ships, fully working, uses **full monomorphization**. This is the reference implementation. After this session's perf work, it builds the self-hosted compiler in ~5s, monomorphization being the largest pipeline pass at ~660ms (44% of pipeline excluding parse). Full monomorphization compounds downstream: it multiplies the work of every subsequent lowering and codegen pass.

- **Self-hosted compiler (`maxon-selfhosted/`)**: passes 3 of ~131 spec tests. Phase 1 (core arithmetic) complete. Phases 2–10 must land before generics are even on the table. **Generics are Phase 11.** No interfaces, no generic types, no monomorphization yet — but also no commitments yet, which makes this the cheapest possible moment to choose the hybrid model.

- **Incremental compiler design**: already implemented at the front-end (per-file token & parse caching via Salsa-style queries in `Queries.maxon`/`QueryEngine.maxon`/`QueryDatabase.maxon`). The back-end is whole-program. The single biggest design constraint hampering finer back-end caching is *exactly* monomorphization — caller IR encodes specialized callee names, so any change to a generic ripples through the whole module.

- **Stdlib**: relies heavily on generics (`Array uses Element`, `Map`, `Iterator uses Element`, `Iterable uses Element, Iter`). Interfaces already declared (`Comparable`, `Equatable`, `Hashable`, `Cloneable`, `Iterable`, `Iterator`). The hybrid plan must support the existing stdlib without breaking source compatibility.

## The hybrid model in one paragraph

Each concrete generic instantiation gets its own **layout descriptor** — a small static record describing element size, alignment, copy/destroy behavior, whether it contains heap refs. Methods on generic types are compiled **once** as a single body that takes a layout descriptor (and, for interface-typed parameters, a witness table of method pointers) as implicit parameters. Calls to generic methods pass these descriptors automatically. At call sites where the concrete type is statically inferable, an inliner pass inlines the method body and constant-folds the descriptor's fields, recovering performance equivalent to today's full monomorphization. Everything else dispatches through the descriptor at a small runtime cost.

This gives us:
- One method body per generic method instead of N per (method × type)
- Per-function incremental compilation becomes feasible (caller IR references stable names)
- Dynamic dispatch as a first-class language feature
- Smaller binaries, better icache behavior
- Hot paths stay fast via inlining

The trade-off is increased compiler complexity (witness tables, layout descriptors, an inliner) and a small runtime cost on call sites that don't get inlined (typically 5–15% on typical code, near-zero on numeric hot paths).

## Prerequisites

This plan is **Phase 11+** in the existing roadmap. Phases 2–10 must land first:
- Phase 2: control flow (if/while)
- Phase 3: function parameters
- Phase 4: basic types (int widths, float widths)
- Phase 5: structs
- Phase 6: managed memory
- Phase 7: strings
- Phase 8: error handling (try/throw/catch)
- Phase 9: enums
- Phase 10: closures

These are necessary because generics require a working type system, struct layouts, error handling for `try`-based generic methods, and closures (for higher-kinded callbacks). Building generics on an incomplete foundation would create rework.

The plan below assumes Phases 2–10 exist by the time this work starts.

## The plan

The work breaks into 7 sub-phases over an estimated 12–20 weeks. Each sub-phase is independently testable.

### Phase 11.0 — Foundation: parser + interface declarations (2–3 weeks)

**Goal**: parser accepts the full surface syntax for generics and interfaces. No semantic action yet.

**Files to modify:**
- [maxon-selfhosted/Compiler/Lexer.maxon](maxon-selfhosted/Compiler/Lexer.maxon) — keywords `interface`, `extension`, `extends`, `implements`, `with`, `where`, `from`, `uses` already tokenized; verify precedence and contextual handling
- [maxon-selfhosted/Compiler/Parser.maxon](maxon-selfhosted/Compiler/Parser.maxon) — add productions for:
  - `interface Name uses T1, T2 ... end` (with optional associated types)
  - `type Name uses T1 implements I1, I2 with (...) ... end`
  - `where T: Comparable`, `where T: Equatable and Hashable`
  - `function foo<T>(x T) returns T where T: Comparable`
  - `extension Iterable uses Element ... end`
  - `from Type implements Interface ... end` (out-of-line conformance)
- [maxon-selfhosted/Compiler/IR/Maxon/MaxonDialect.maxon](maxon-selfhosted/Compiler/IR/Maxon/MaxonDialect.maxon) — extend `MaxonType` union:
  - Add `typeParameter(id TypeNameId)` for unresolved `T` references
  - Add `genericInstance(baseId TypeNameId, args MaxonTypeArray)` for `Array with Int`
  - Note: keep `named` for fully-resolved types — instantiation produces a fresh `named` after monomorphization
- [maxon-selfhosted/Compiler/Project.maxon](maxon-selfhosted/Compiler/Project.maxon) — add new tables:
  - `interfaces InterfaceMap` — interface name → method list + associated types
  - `conformances ConformanceMap` — `(typeName, interfaceName)` → conformance entry
  - `typeParameters TypeParamMap` — type param `T` in scope X → constraint set

**Critical reuse**: the C# bootstrap already parses all this syntax. The grammar is solved; this is mostly transcription with adjustments for self-hosted parsing style.

**Tests**: spec tests under `specs/interfaces/`, `specs/generics/` start parsing successfully (still fail at lower stages). Add parser-only tests verifying AST shape.

**Risk**: low. Pure additive parsing work.

---

### Phase 11.1 — Type system: representation, substitution, conformance (3–4 weeks)

**Goal**: type system can represent and reason about generics. Type checking distinguishes instantiated from uninstantiated types.

**New types in `MaxonDialect.maxon` / `Project.maxon`:**
```
export type InterfaceType
    var name String
    var methods InterfaceMethodArray   // signature only
    var associatedTypes StringArray
end

export type GenericTypeDecl
    var name String                    // "Array"
    var typeParams StringArray         // ["Element"]
    var constraints ConstraintMap      // "Element" -> [Equatable, Hashable]
    var fields StructFieldArray        // pre-substitution; field types may be type params
    var methods FunctionDeclArray
    var conformsTo InterfaceConformanceArray
end

export type TypeSubstitution
    var bindings Map with (String, MaxonType)  // "Element" -> .named(Int)
end
```

**Substitution algorithm:**
- `substituteType(type, sub) returns MaxonType` — walks a `MaxonType`, replacing `typeParameter(T)` with the concrete type from `sub`. Recurses into `genericInstance` arguments.
- Substitution is **structural and pure** — no side effects, no mutation of the source type.

**Conformance checking:**
- `typeConformsTo(type, interface, sub) returns bool` — does the instantiated type provide all interface methods? Recurses into `where` constraints.
- `findConformance(type, interface) returns ConformanceEntry?` — returns the conformance record if present.
- Conformances live in `Project.conformances`, populated when parsing `implements` clauses.

**Where clauses:**
- Translate `where T: Equatable and Hashable` into a `ConstraintSet` per type parameter
- Constraint satisfaction is checked at the *call site*, not at the generic body — this matters for incremental: the body never needs to be re-checked when callers change

**Tests**: type checking tests for substitution correctness, conformance lookup, where-clause satisfaction. No code generation yet.

**Risk**: medium. The C# bootstrap's `TypeSubstitution.cs` is a useful reference but has accumulated complexity from interface-alias specialization that we explicitly want to avoid in this design.

**Critical decision recorded here**: we are **not** specializing types per call-site arg type the way the C# bootstrap does. Interface-typed parameters stay interface-typed. The witness table makes this work without specialization.

---

### Phase 11.2 — Layout descriptors (2–3 weeks)

**Goal**: every concrete instantiation of a generic type gets a layout descriptor, emitted into the data section.

**New IR concept:**
```
export type LayoutDescriptor
    var typeName String                // "Array_Int" — only for diagnostics
    var size MachineWord               // total size in bytes
    var alignment MachineWord          // alignment requirement
    var elementSize MachineWord        // for collections: stride between elements
    var fieldOffsets MachineWordArray  // for structs: per-field byte offset
    var copyFunc String?               // function name to copy, or null = memcpy
    var destroyFunc String?            // function name to destroy/decref, or null = noop
    var hasHeapRefs bool               // does this type contain managed pointers?
end
```

**New pass: `BuildLayoutDescriptors`** (runs after type checking, before lowering)
- Walks all `genericInstance` types reached during type checking
- For each, computes layout (recursively descending into fields)
- Produces a fresh `named` type for the concrete instantiation (e.g. `Array_Int`)
- Emits the descriptor into a new `LayoutDescriptorTable` on `Project`
- Returns a mapping `genericInstance → concreteName + descriptorLabel`

**Memory layout:**
- For `Array<T>`: size = pointer to managed memory (always 8 bytes — Array is a thin wrapper). The *element* layout differs.
- For `Pair<T, U>`: size = sizeof(T) + sizeof(U) + padding. Each instantiation has its own layout.
- For struct types containing type params: same recursive layout algorithm as concrete structs, but with `T`'s size resolved from the binding

**Emission:**
- Layout descriptors are emitted into `.rdata` as constant data
- Each gets a stable label like `__layout_Array_Int`
- The label is referenced by callers at known offsets

**Reuse**: existing `GlobalDataTable` infrastructure for rdata emission. Add a parallel `LayoutDescriptorTable` for type-keyed lookup.

**Tests**: snapshot tests of generated descriptor tables for `Array<Int>`, `Array<String>`, `Pair<Int, Float>`, etc. Verify size/alignment match C# bootstrap output.

**Risk**: medium. Easy to get layout wrong in edge cases (generics-of-generics, recursive types).

---

### Phase 11.3 — Witness tables for interfaces (2–3 weeks)

**Goal**: every `(type, interface)` conformance gets a witness table emitted as data; interface types have a uniform runtime representation.

**Witness table structure:**
```
__witness_Int_Comparable:
    .quad <ptr to Int.compare>
__witness_String_Comparable:
    .quad <ptr to String.compare>
__witness_Int_Hashable:
    .quad <ptr to Int.hash>
```

One witness table per `(type, interface)` pair. Each entry is a function pointer to the concrete implementation.

**Interface-typed values** become a fat pointer at runtime:
```
{value: ptr_or_inline, witness: ptr_to_witness_table}
```
- For value types ≤ 8 bytes (Int, Bool, etc.), the value can be inlined
- For larger types, a pointer to a heap allocation

**New pass: `BuildWitnessTables`** (after `BuildLayoutDescriptors`)
- Walks all `(type, interface)` conformances
- For each, emits a witness table: ordered list of method pointers
- Method order is fixed per interface (deterministic from interface declaration order)

**Method dispatch:**
- Calling `x.method()` where `x: SomeInterface`:
  - Lower to: load witness pointer from x's fat pointer, load method-N from witness table, indirect call
- Calling `x.method()` where `x: ConcreteType` and `ConcreteType implements Interface`:
  - Direct call to `ConcreteType.method` — no witness involved

**Decision recorded**: we use a **single witness table per (type, interface)**, *not* a witness table embedded in the value (Java/Go-style). Reason: smaller value representation, fewer cache misses for collections of interface-typed values. Same approach as Swift.

**Tests**: spec tests for `Iterable`, `Comparable`, `Equatable` calling through trait objects.

**Risk**: medium. Layout of fat pointers needs to match across all backends; calling convention for indirect calls must work on x64 Windows, x64 SysV, ARM64.

---

### Phase 11.4 — Generic body lowering (4–6 weeks) **[the biggest piece]**

**Goal**: generic method bodies are lowered **once** with implicit layout/witness parameters. Calls pass the right descriptors.

This is where the project shape diverges most from the C# bootstrap. The bootstrap clones a function per (callee × type-args). The hybrid lowers it once.

**Changes to `LowerMaxonToStd.maxon`:**
- When lowering a generic method `Array<T>.push(self, value)`:
  - Add implicit parameters: `(self, value, layout: ptr, /* witness if any T: Interface */)`
  - Inside the body, replace operations that depend on `T`'s layout with descriptor-driven ops:
    - `sizeof(T)` → load `[layout + 0]`
    - `alignof(T)` → load `[layout + 8]`
    - `copy(dst, src, T)` → indirect call to `[layout + copyFuncOffset]`
    - `destroy(x, T)` → indirect call to `[layout + destroyFuncOffset]`
  - Field access on type-parameter-typed values uses descriptor offsets
- When lowering a call `arr.push(x)` where `arr: Array<Int>`:
  - Look up the layout descriptor label for `Array<Int>`
  - Emit a load of that label as the implicit `layout` argument
  - Emit a regular call to `Array.push` (single shared body)
- Interface-typed parameters: emit a load of the witness table pointer as the implicit `witness` argument

**New IR ops in Std dialect** (`StdDialect.maxon`):
- `loadLayoutDescriptor(label String) returns ValueId` — loads layout pointer
- `loadWitnessTable(typeName String, interfaceName String) returns ValueId`
- `descriptorField(layout ValueId, fieldOffset MachineWord) returns ValueId` — load a field from a descriptor
- `witnessMethod(witness ValueId, methodIndex int) returns ValueId` — load a method ptr from witness table
- All resolve to existing `loadIndirect` / `funcRef` ops at MIR level — they're sugar over what's already there

**ABI lowering** (`LowerABI.maxon`):
- Implicit layout/witness params are classified as register parameters
- They occupy the first few register slots (e.g., RCX/RDX on Windows x64)
- The existing parameter classifier handles this naturally if we just add them to the parameter list

**Critical reuse**: the existing call-op infrastructure (`StdCallOp.call`, `indirectCall`, `funcRef`) is already general enough. We're not adding a new op kind, just a new parameter convention.

**The generic-method-name-stays-stable property:**
- After this phase, `Array.push` is **one** function in the emitted module, named `Array.push` (not `Array_Int.push` or similar)
- This is the property that unlocks per-function incremental compilation
- Caller IR references `Array.push` regardless of T

**Tests**: full Iterable/Iterator/Array test suite must pass. Compare emitted IR shape against C# bootstrap to confirm no regressions in correctness (perf will diverge — that's expected and recovered in Phase 11.6).

**Risk**: high. This is the core of the change. Many subtle interactions:
- Refcount insertion: descriptor's `destroy` function handles ref-counting per-element; the surrounding code does ref-counting on the container itself
- Generic constructors: `Array<T>.create()` needs to know `T`'s layout to allocate properly
- Type inference at call sites: the elaborator must produce the right layout descriptor reference at every generic call

**Mitigation**: build a parallel test harness that compiles a small "canonical generic test program" (push/pop on Array<Int>, Array<String>, sort with Comparable) and snapshots both the IR and the runtime output. Run after every commit during this phase.

---

### Phase 11.5 — Per-function incremental queries (2–3 weeks)

**Goal**: extend the query system to cache MIR and emitted code at function granularity, enabling sub-second incremental rebuilds for typical edits.

**This phase is only possible because Phase 11.4 made callee names stable** — caller IR doesn't change when callee bodies change.

**New queries in `Queries.maxon`:**
- `queryMidForFunction(project, funcName) returns StdFunction` — extracts one function from `queryAllMid` output, runs lowering passes on just that function
- `queryMirForFunction(project, funcName) returns MirFunction` — lowers Std → MIR for one function
- `queryCodeForFunction(project, funcName) returns FunctionCode` — emits machine code for one function

**Refactor needed:**
- `PassPipeline.run()` currently runs all passes on the whole module. Add `runForFunction(funcName)` paths for per-function incremental
- Some passes are intrinsically whole-module (DFE, monomorphization-replacement, layout descriptor generation). Those stay as `queryAllMid`-level. Per-function queries operate after these whole-module passes
- Code emission needs to support emitting one function and patching it into a partially-built module

**Cache invalidation:**
- `queryMirForFunction(F)` depends on:
  - `queryMidForFunction(F)`'s output
  - The signatures of all functions F calls (not their bodies)
  - The layout descriptors F uses
- A change to function G's *body* invalidates only `queryMir/CodeForFunction(G)`, not F's caches
- A change to function G's *signature* invalidates F if F calls G
- A change to a layout descriptor invalidates all functions using that descriptor

**Tests**: extend `IncrementalTestRunner.maxon` with new scenarios:
- "Edit one function body" → only that function's MIR/code is recomputed
- "Edit a generic body" → only that generic body's MIR is recomputed (not all callers!)
- "Add a new instantiation" → new layout descriptor generated; existing function bodies untouched
- "Edit a function signature" → callers of that function are recomputed; non-callers untouched

**Risk**: medium. The pipeline refactoring is mechanical; the dependency tracking needs to be precise to avoid stale caches.

**Expected payoff**: edit-one-function rebuild time drops from ~3s to ~50–200ms.

---

### Phase 11.6 — Inliner pass (3–5 weeks)

**Goal**: aggressive inlining at static call sites, recovering monomorphized-quality code on hot paths.

**This is what makes the runtime perf cost of the hybrid model approach zero on typical code.**

**Inliner mechanics:**
- Operates on Std-level IR, before MIR lowering
- For each call site `arr.push(x)` where `arr: Array<Int>`:
  - The layout descriptor is statically known (it's a fixed label reference)
  - The call target is statically known (`Array.push` is a known function)
  - If the callee is `@inlinable` (or below a size threshold and not recursive), inline the body
  - Once inlined, the layout descriptor's fields become constants — `loadIndirect [layout+0]` becomes the constant 8 (sizeof Int)
  - Subsequent constant folding eliminates the descriptor accesses entirely
  - The result is byte-identical to today's monomorphized output for hot paths

**Heuristics:**
- Always inline: callees marked `@inlinable`, callees < 20 ops, callees with a single call site
- Profile-guided / size-balance: medium callees, multiple call sites
- Never inline: recursive functions (without bound), functions with `@noinline`, functions taking truly dynamic types

**New annotations:**
- `@inlinable` — hint for the inliner (becomes mandatory for cross-module inlining if we add modules later)
- `@noinline` — block inlining
- `@alwaysInline` — force inlining; error if can't

**Stdlib annotation pass:**
- Annotate hot stdlib methods: `Array.push`, `Array.get`, `Array.length`, iterator `current`/`advance`, `Optional.unwrap`, etc. with `@inlinable`
- This is mostly mechanical; expected ~20-30 annotations to recover 90%+ of monomorphization's runtime perf

**Constant folding extensions:**
- After inlining, layout descriptor field loads become loads from known addresses
- Extend the existing canonicalize pass to recognize these and replace with constants
- This is what makes the inliner actually pay off — without constant folding through descriptor loads, inlining alone doesn't recover the perf

**Tests**:
- Benchmarks showing inlined hot paths match monomorphized C# bootstrap output to within 5%
- Spec tests still pass (inlining is purely an optimization; correctness must be preserved)
- IR snapshots showing descriptor loads constant-folded away after inlining

**Risk**: medium. Inliners are well-trodden territory; the LLVM/Cranelift literature has established heuristics. The trickiest part is constant folding through descriptor loads, which requires the inliner to recognize the descriptor as a known constant memory region.

**Critical decision**: **the inliner does not run automatically without `@inlinable` for stdlib methods.** Inlining unannotated functions is too risky for compile time (can blow up code size). Stdlib gets explicit annotations; user code gets size-based heuristics.

---

### Phase 11.7 — Validation, polish, parity (2–3 weeks)

**Goal**: spec test parity with C# bootstrap, performance comparable to or better than current full-monomorphization design.

**Validation:**
- All ~131 spec tests pass on self-hosted compiler
- Self-host test: self-hosted compiler can compile itself (correctness baseline)
- Self-host benchmark: self-hosted compiler compiling itself in ≤ 3s (target: faster than C# bootstrap because of incremental + smaller binary)

**Performance targets:**
- Compile time of stdlib + selfhosted source: **≤ 60% of current C# bootstrap** (~3s vs current 5s)
- Incremental rebuild of one-function edit: **≤ 200ms**
- Runtime performance of compiled code: within **10%** of C# bootstrap output on representative benchmarks; within **2%** on stdlib hot loops (with inlining)
- Binary size: **≤ 50%** of C# bootstrap output

**Polish:**
- Diagnostic quality: "could not satisfy `T: Comparable` at call site of `sort` in `main:line 42`" rather than `__Array_Foo.sort not found`
- IR dumps clearly show generic bodies and inlined call sites
- LSP integration: hover on a generic call site shows the inferred substitution

**Documentation:**
- Update `ARCHITECTURE.md` with the hybrid generics design
- Add `docs/generics-design.md` with the runtime representation, witness tables, layout descriptors

## Critical files to modify (summary)

| Phase | File / area | What |
|---|---|---|
| 11.0 | [Lexer.maxon](maxon-selfhosted/Compiler/Lexer.maxon), [Parser.maxon](maxon-selfhosted/Compiler/Parser.maxon) | Parse interface, generics, where, from, with, uses |
| 11.0 | [MaxonDialect.maxon](maxon-selfhosted/Compiler/IR/Maxon/MaxonDialect.maxon) | Extend MaxonType with type parameters and instances |
| 11.0 | [Project.maxon](maxon-selfhosted/Compiler/Project.maxon) | Add interface, conformance, type-param tables |
| 11.1 | New: `Compiler/TypeSystem/Substitution.maxon` | Pure substitution + conformance lookup |
| 11.1 | New: `Compiler/TypeSystem/Constraints.maxon` | Where-clause checking |
| 11.2 | New: `Compiler/Passes/BuildLayoutDescriptors.maxon` | Layout descriptor generation pass |
| 11.2 | [Project.maxon](maxon-selfhosted/Compiler/Project.maxon) | Add `LayoutDescriptorTable` |
| 11.3 | New: `Compiler/Passes/BuildWitnessTables.maxon` | Witness table generation pass |
| 11.4 | [LowerMaxonToStd.maxon](maxon-selfhosted/Compiler/IR/Maxon/LowerMaxonToStd.maxon) | Implicit param lowering, descriptor-driven generic bodies |
| 11.4 | [StdDialect.maxon](maxon-selfhosted/Compiler/IR/Std/StdDialect.maxon) | New ops for descriptor/witness loads |
| 11.5 | [Queries.maxon](maxon-selfhosted/Compiler/Queries.maxon) | Per-function MIR/code queries |
| 11.5 | [PassPipeline.maxon](maxon-selfhosted/Compiler/IR/PassPipeline.maxon) | Per-function pass dispatch |
| 11.6 | New: `Compiler/Passes/Inliner.maxon` | Inlining + constant folding through descriptors |
| 11.6 | Stdlib | Add `@inlinable` annotations to hot methods |

## Existing utilities to reuse

- **C# bootstrap MonomorphizationPass** ([maxon-sharp/Compiler/MLIR/Passes/MonomorphizationPass.cs](maxon-sharp/Compiler/MLIR/Passes/MonomorphizationPass.cs)) — reference for type substitution algorithm, conformance checking, and call-site rewriting. We will *not* port the specialization-by-cloning; only the type-system mechanics.
- **Query system** ([Queries.maxon](maxon-selfhosted/Compiler/Queries.maxon)) — extend with new query keys; the memoization template is reusable as-is.
- **Existing call ops** (`StdCallOp`, `indirectCall`, `funcRef`) — already general enough; no new op kinds needed for dispatch.
- **`GlobalDataTable`** — emit layout descriptors and witness tables here.
- **`LowerABI.maxon`** — implicit parameters fit naturally; no calling-convention changes needed.
- **`RegisterAllocator.maxon`** — no changes; the linear-scan allocator handles implicit params automatically.

## Verification

After each sub-phase:
1. Self-hosted compiler builds via C# bootstrap with no test regressions
2. Spec tests in scope for that phase pass
3. Self-hosted compiler can compile a small benchmark program and produce a working binary
4. After 11.4: self-hosted compiler can compile its own stdlib
5. After 11.7: self-hosted compiler can compile itself, full self-hosting baseline restored

End-state:
- Self-hosted compiler has full feature parity with C# bootstrap
- Compile time: ≤ 60% of C# bootstrap baseline
- Incremental rebuild for one-function edit: ≤ 200ms
- Runtime: within 10% of C# bootstrap output (within 2% with inlining on hot paths)
- All 131 spec tests passing

## Out of scope (deliberately)

- **Cross-module inlining / link-time optimization** — interesting but a separate project; not needed for the self-hosting compile case where everything's in one compilation unit
- **Profile-guided optimization** — orthogonal; could be added later as a second-stage compilation mode
- **Specialization-by-attribute (`@specialize`)** — a future optimization for cases where inlining isn't enough; not needed for v1
- **Type erasure for code size** — Swift's existential containers; we keep monomorphized layouts on purpose because they're a key perf win
- **Backporting to the C# bootstrap** — the C# compiler stays as-is. It's the reference, not the target.

## Risks and open questions

1. **Refcount interaction with descriptor-driven destroy.** When `Array<String>.pop()` returns a `String`, refcount semantics need to be preserved. The `destroy` function in the layout descriptor handles this — but the surrounding code must call it correctly. **Mitigation**: extensive testing of refcount-balanced code paths early in Phase 11.4.

2. **Inliner pessimism**. If the inliner is too conservative, runtime perf suffers; too aggressive and code size blows up. **Mitigation**: explicit `@inlinable` on stdlib hot paths gives us precise control; user code uses conservative heuristics.

3. **Type inference at generic call sites.** The elaborator needs to produce concrete substitutions at every generic call. This is solved territory (Hindley-Milner with constraints) but needs careful integration with where clauses. **Mitigation**: keep the inference algorithm small and well-tested; lean on the existing parser/checker structure.

4. **Recursive generic types.** `LinkedList<T>` containing `LinkedList<T>` itself, or recursive type parameters in trait bounds. **Mitigation**: layout descriptor computation must handle cycles (compute size symbolically when recursion detected). The C# bootstrap has `IsRecursiveTypeNesting` for this; port the algorithm.

5. **Migration from existing C# bootstrap output.** We need to be able to run the self-hosted compiler against the same stdlib and produce compatible output. Stdlib uses things like `BuiltinArrayLiteral` with magic compiler integration. **Mitigation**: Phase 11.4 must keep stdlib's existing surface API — only the lowering changes.

## Estimated effort and timeline

| Phase | Estimate | Cumulative |
|---|---|---|
| 11.0 Foundation | 2–3 weeks | 3 weeks |
| 11.1 Type system | 3–4 weeks | 7 weeks |
| 11.2 Layout descriptors | 2–3 weeks | 10 weeks |
| 11.3 Witness tables | 2–3 weeks | 13 weeks |
| 11.4 Generic body lowering | 4–6 weeks | 19 weeks |
| 11.5 Per-function queries | 2–3 weeks | 22 weeks |
| 11.6 Inliner | 3–5 weeks | 27 weeks |
| 11.7 Validation/polish | 2–3 weeks | 30 weeks |

**Total: ~16–30 weeks** (4–7 months, full-time-equivalent)

For a part-time project this stretches; for focused work it's ~5 months.

## Why this is the right plan

1. **It commits to the design at the cheapest moment** — before Phase 11 ships full monomorphization that would have to be undone later.

2. **It positions Maxon distinctively.** "Rust-class safety with Swift-class compile times" is a real, defensible niche. No other production language occupies it. Rust is too slow to compile for this niche; Go is too unsafe; Swift is Apple-only and has its own compile-time issues.

3. **It compounds with existing infrastructure.** The query system was already built for incrementality; this plan unlocks its actual value. The C# bootstrap's monomorphization code is a useful reference for type machinery without being a model to copy.

4. **It's incremental.** Each sub-phase is testable and shippable. If priorities shift after 11.4, we have a working hybrid generics implementation; 11.5–11.7 are pure perf wins on top.

5. **It preserves Maxon's identity.** Refcount + borrow checking + AOT-compiled binaries stay. We're trading a small amount of runtime perf (recoverable via inlining) for a transformative gain in compile time and language capability.
