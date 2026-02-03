#### Types

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| self.md | self reference in methods | ⬜ | |
| type-casting.md | `as` operator for casts | ⬜ | |

#### Type Inference

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| type-inference.md | Type inference and checking | ⬜ | |
| contextual-typing.md | Context-based literal typing | ⬜ | |
| numeric-promotion.md | Auto int→float promotion | ⬜ | |

### Implementation Tasks

- [ ] Struct type registration
- [ ] Field access semantic analysis
- [ ] Struct layout computation
- [ ] Method resolution
- [ ] `self` binding in methods
- [ ] Static vs instance dispatch
- [ ] Type casting validation
- [ ] Bidirectional type inference
- [ ] Numeric promotion rules


---

## Phase 4: Enums & Interfaces

**Goal**: Add enum types and interface system (needed for error handling and match).

**Dependencies**: Phase 3 (structs, type system)

### Specs to Implement

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| enums.md | Enum types (simple, raw, associated) | ⬜ | |
| interfaces.md | Interface declarations | ⬜ | |
| interface-conformance.md | `is` keyword, conformance | ⬜ | |
| match.md | Pattern matching | ⬜ | |

### Implementation Tasks

- [ ] Simple enum semantic analysis
- [ ] Raw value enums
- [ ] Associated value enums
- [ ] Enum case construction
- [ ] Enum discriminant layout
- [ ] Interface declaration semantic analysis
- [ ] Conformance checking
- [ ] Witness table generation
- [ ] Match expression semantic analysis
- [ ] Pattern exhaustiveness checking
- [ ] Match code generation


---

## Phase 5: Strings & Collections

**Goal**: Implement managed string type and collection types.

**Dependencies**: Phase 4 (interfaces for Collection)

### Specs to Implement

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| string-literals.md | String literal syntax | ⬜ | |
| string.md | String type (SSO, UTF-8) | ⬜ | |
| string-interpolation.md | `\()` in strings | ⬜ | |
| arrays.md | Array type with get/set/push | ⬜ | |
| managed-arrays.md | Arrays of managed types | ⬜ | |
| collection.md | Collection interface | ⬜ | |
| map.md | Hash map type | ⬜ | |
| set.md | Hash set type | ⬜ | |
| character-type.md |  | ⬜ | |
| byte-type.md |  | ⬜ | |

### Implementation Tasks

- [ ] String literal parsing and escape sequences
- [ ] String type layout (SSO implementation)
- [ ] String memory management
- [ ] String interpolation lowering
- [ ] Array type semantic analysis
- [ ] Array bounds checking
- [ ] Array growth/reallocation
- [ ] Collection interface implementation
- [ ] Hash function implementation
- [ ] Map bucket layout
- [ ] Set implementation


---

## Phase 6: Error Handling & Closures

**Goal**: Add error handling with try/throw/otherwise and closure support.

**Dependencies**: Phase 4 (enums for error types), Phase 5 (strings for error messages)

### Specs to Implement

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| error-handling.md | throw/try/otherwise | ⬜ | |
| if-try.md | if try/let expressions | ⬜ | |
| parsable.md | Parsable interface pattern | ⬜ | |
| closures.md | Function refs, closures | ⬜ | |

### Implementation Tasks

- [ ] Error type semantic analysis
- [ ] throw statement codegen
- [ ] try/otherwise control flow
- [ ] Error propagation
- [ ] if try binding
- [ ] Parsable conformance
- [ ] Closure environment capture analysis
- [ ] Closure struct generation
- [ ] Closure call convention


---

## Phase 7: Modules & Stdlib

**Goal**: Multi-file compilation and standard library integration.

**Dependencies**: Phase 6 (error handling for stdlib functions)

### Specs to Implement

#### Module System

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| namespaces.md | File-based namespaces | ⬜ | |
| export.md | export visibility | ⬜ | |
| multi-file.md | Multi-file compilation | ⬜ | |
| qualified-names.md | Namespace.name syntax | ⬜ | |

#### Stdlib

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| stdlib-autodiscovery.md | Auto-linking stdlib | ⬜ | |
| stdlib-core.md | Basic stdlib functions | ⬜ | |
| stdlib-array.md | Array stdlib module | ⬜ | |
| stdlib-set.md | Set stdlib module | ⬜ | |
| stdlib-file.md | File read/write | ⬜ | |
| stdlib-directory.md | Directory operations | ⬜ | |
| commandline-args.md | CommandLine.args() | ⬜ | |

### Implementation Tasks

- [ ] Namespace registration
- [ ] Export visibility checking
- [ ] Multi-file symbol resolution
- [ ] Qualified name lookup
- [ ] Stdlib path discovery
- [ ] Stdlib linking
- [ ] File I/O syscalls
- [ ] Directory syscalls
- [ ] Command line argument passing

---

## Phase 8: Advanced Features

**Goal**: Complete remaining advanced features.

**Dependencies**: All previous phases

### Specs to Implement

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| interface-extensions.md | Extension methods on interfaces | ⬜ | |
| generic-interfaces.md | `uses`/`with` type params | ⬜ | |
| grapheme-clusters.md | Unicode EGC support | ⬜ | |
| slices.md | Memory slices | ⬜ | |
| ffi.md | FFI with crash isolation | ⬜ | |
| allocations.md | Memory allocation tracking | ⬜ | |

### Implementation Tasks

- [ ] Interface extension method dispatch
- [ ] Generic type parameter binding
- [ ] Grapheme cluster iteration
- [ ] Slice type layout
- [ ] FFI declaration parsing
- [ ] FFI call marshaling
- [ ] Crash isolation (structured exception handling)
- [ ] Allocation tracking infrastructure


---


### Testing Strategy

Run spec tests after each phase:
```powershell
cd maxon-sharp
.\bin\Debug\net8.0\win-x64\maxon.exe spec-test
```

Or run filtered spec tests:
```powershell
.\bin\Debug\net8.0\win-x64\maxon.exe spec-test --filter=floor
```

### Debugging Tips

- Use `--log=mlir:debug` for MLIR dumps
- Use `--log=codegen:debug` for X86 codegen dumps
- Use `--log=regalloc:debug` for register allocation details
