---
name: maxon-planner
description: Plan and coordinate implementation of new features or roadmap phases in the Maxon self-hosted compiler. Use this skill when the user asks to plan a feature, design an implementation approach, figure out what needs to change for a compiler feature, or coordinate a multi-step compiler change. Triggers on phrases like "plan", "design", "figure out how to implement", "what would it take to add", "implementation plan", "plan the next phase".
---

Create a detailed implementation plan for a feature or roadmap phase in the self-hosted Maxon compiler, then coordinate implementation using the maxon-coder skill.

This skill is the thinking-first entry point for compiler work. Rather than jumping straight into code, it researches the codebase, identifies all affected files across all targets, designs the approach, and produces a structured plan before any code is written. This matters because the compiler has a 7-phase pipeline with 6 target combinations (x64/arm64 × windows/linux/macos), and a change in one place almost always requires corresponding changes in several others.

## When to use this skill vs others

- **maxon-planner** (this skill): When you need to think through what to build before building it. Use for anything non-trivial — new language features, roadmap phases, cross-cutting compiler changes.
- **implement-phase**: When the plan already exists in ROADMAP.md and you just need to execute it.
- **implement-feature**: When you need to create a new spec and implement from scratch in the C# compiler.
- **selfhosted-dev**: When you are iterating on failing spec tests in the self-hosted compiler.

## Workflow

### Phase 1: Load Context

1. Read `docs/WRITING_MAXON_CODE.md` to load Maxon syntax rules. Refer to `docs/LANGUAGE_REFERENCE.md` and `docs/QUICK_REFERENCE.md` for full details when needed.
2. Read `maxon-selfhosted/ARCHITECTURE.md` to understand the compilation pipeline and file layout.
3. Read `maxon-selfhosted/ROADMAP.md` to understand the current progress and phase dependencies.
4. If the request maps to a specific roadmap phase, read that phase's section carefully — it contains the spec list, required changes per component, and file list.

### Phase 2: Research the Codebase

Investigate how similar features are currently implemented. This is the most important phase — skipping it leads to plans that don't match the actual code patterns.

5. **Find analogous features.** Identify 1-2 existing features that are structurally similar to what needs to be implemented. For example, if adding a new binary operator, look at how existing binary operators flow through the pipeline.

6. **Trace the pipeline end-to-end.** For the analogous feature, read the relevant code in each pipeline stage to understand the concrete pattern:
   - `Compiler/Parser.maxon` — how is it parsed?
   - `Compiler/IR/Dialects/MaxonDialect.maxon` — what MaxonOp variants exist?
   - `Compiler/IR/Passes/LowerMaxonToArith.maxon` — how is it lowered to arith/cf/func/memref?
   - `Compiler/IR/Passes/LowerToMir.maxon` — how does it become MIR?
   - `Compiler/Targets/X86/MidToX64Conversion.maxon` — how is it lowered to x64?
   - `Compiler/Targets/X86/X64CodeEmitter.maxon` — what machine code is emitted?
   - `Compiler/Targets/Arm64/MidToArm64Conversion.maxon` — how is it lowered to arm64?
   - `Compiler/Targets/Arm64/Arm64CodeEmitter.maxon` — what machine code is emitted?

7. **Check semantic analysis.** Read `Compiler/SemanticCheck.maxon` to see if similar features have validation rules.

8. **Check the spec.** Read the relevant spec file(s) in `specs/` to understand the expected behavior and test cases. If no spec exists yet, look at similar specs to understand the testing patterns.

9. **Check runtime involvement.** If the feature might need runtime support (memory allocation, string operations, error handling), check:
   - `Compiler/Runtime/runtime.mid` — existing runtime functions
   - `Compiler/Runtime/RuntimeFunctions.maxon` — global data tables
   - `Compiler/IR/Passes/LowerMaxonToSysAndRuntime.maxon` — runtime/sys lowering

10. **Check all targets.** The compiler supports 6 target combinations. Any plan that touches code generation must account for:
    - x64 (Windows calling convention: RCX, RDX, R8, R9; Linux/macOS: RDI, RSI, RDX, RCX, R8, R9)
    - arm64 (X0-X7 for all platforms)
    - PE writer (Windows), ELF writer (Linux), Mach-O writer (macOS)

### Phase 3: Create the Plan

Produce a structured implementation plan. The plan should be specific enough that someone (or the maxon-coder skill) can follow it without additional research.

11. **Write the plan** with these sections:

```
## Feature: [name]

### Summary
One paragraph describing what this feature does and why it matters.

### Prerequisites
- List any phases or features that must be completed first
- Reference ROADMAP.md dependency chain if applicable

### Spec Tests
- List the spec files that cover this feature
- Note which tests need to be added to the whitelist in SpecTestRunner.maxon
- If no spec exists, note that one needs to be created (and outline test cases)

### Pipeline Changes

For each pipeline stage that needs modification, specify:
- **File**: exact path
- **What to add/change**: specific operations, enum variants, match arms, etc.
- **Pattern to follow**: reference the analogous feature code you found in Phase 2
- **Code sketch**: pseudo-code or Maxon snippets showing the shape of the change

#### Parser (Compiler/Parser.maxon)
[changes]

#### Maxon Dialect (Compiler/IR/Dialects/MaxonDialect.maxon)
[changes — new enum variants, their fields]

#### Lowering: MaxonToArith (Compiler/IR/Passes/LowerMaxonToArith.maxon)
[changes]

#### Lowering: MaxonToSysAndRuntime (Compiler/IR/Passes/LowerMaxonToSysAndRuntime.maxon)
[changes, if applicable]

#### Semantic Check (Compiler/SemanticCheck.maxon)
[validation rules, if applicable]

#### MIR Lowering (Compiler/IR/Passes/LowerToMir.maxon)
[changes]

#### X64 Target
- **Dialect** (Compiler/Targets/X86/X64Dialect.maxon): new op variants
- **Conversion** (Compiler/Targets/X86/MidToX64Conversion.maxon): MIR to x64 lowering
- **Emitter** (Compiler/Targets/X86/X64CodeEmitter.maxon): machine code bytes

#### ARM64 Target
- **Dialect** (Compiler/Targets/Arm64/Arm64Dialect.maxon): new op variants
- **Conversion** (Compiler/Targets/Arm64/MidToArm64Conversion.maxon): MIR to arm64 lowering
- **Emitter** (Compiler/Targets/Arm64/Arm64CodeEmitter.maxon): machine code bytes

#### Executable Writers (if applicable)
- PE (Compiler/Targets/Windows/PeWriter.maxon)
- ELF (Compiler/Targets/Linux/ElfWriter.maxon)
- Mach-O (Compiler/Targets/Macos/MachOWriter.maxon)

#### Runtime (if applicable)
- runtime.mid changes
- RuntimeFunctions.maxon changes

### Implementation Order
Numbered list of steps in dependency order. Each step should be small enough to test independently. Prefer an order that gets one spec test passing as early as possible, then expands coverage.

### Risk Areas
Things that could go wrong or need special attention:
- Register pressure concerns
- Calling convention differences between platforms
- Short jump overflow for x86 (max +/-127 bytes)
- Memory management implications (borrow checking, drop injection)
- Cases where x64 and arm64 need fundamentally different approaches

### Files to Modify (complete list)
Flat list of every file path that needs changes, for easy reference.
```

### Phase 4: Implement

12. Present the plan to the user for review. Wait for confirmation before proceeding.

13. Once approved, use the `maxon-coder` skill for writing Maxon code and implement the plan:
    - Rebuild and verify: `./maxon-selfhosted/bin/maxon-selfhosted.exe spec-test --filter=<test>`
    - Use `--verbose` for detailed failure messages
    - Use `--log=CATEGORY:LEVEL` for debugging (e.g., `--log=ir:debug`, `--log=codegen:trace`)

14. After all tests pass, run the full spec test suite to check for regressions:
    `./maxon-selfhosted/bin/maxon-selfhosted.exe spec-test`

15. Review all code changes:
    - Ensure equivalent functionality across all targets (x64, arm64)
    - Eliminate duplicated code — refactor shared logic into helper methods
    - Ensure no `match` statements use `default` cases — all cases must be handled explicitly
    - Ensure no `else` clauses silently catch unhandled conditions — throw errors for unexpected inputs
    - Ensure comments explain "why" not "what"
    - typealias should describe its purpose, not its type
    - typed ranges should be as specific as possible (e.g., `int(0 to 100)` not `int(0 to u64.max)`). Max range is fine if there is no clear limit.
    - Look for comments implying something was skipped or deferred
    - Fix any compiler warnings

## Guidelines

- The plan is the deliverable of this skill. Invest time in research (Phase 2) to make the plan accurate. A plan that doesn't match the actual code patterns will cause more work during implementation than it saves.
- Always trace at least one analogous feature through the full pipeline before planning. Reading the code is faster than guessing and fixing later.
- Every pipeline stage change should reference a concrete pattern from existing code. "Add a new op" is not specific enough — "Add a `floatAdd` variant to MaxonOp following the same pattern as `add`" is.
- Consider all 6 target combinations. A plan that only covers x64-windows is incomplete.
- Fix root causes, not symptoms. No workarounds.
- If you find a pre-existing issue during research, note it in the plan's Risk Areas and fix it during implementation.
- For memory-related features, consider borrow checking and drop injection implications.
- If any tests that use RequiredIR fail you can regenerate the required IR and MmTrace stderr by using `--update-required`.
- It's possible that bugs encountered could be in the C# bootstrap compiler. If so, fix the C# compiler in `maxon-sharp/`.
