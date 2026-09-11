- claude skills (submit issue, etc)
- libraries
- A typealias (or any named type) used in a function's SIGNATURE must have visibility >= that
  function's. `stdlib/Array.maxon:168` is the case that found it: `public function count() returns
  ElementIndex`, where `ElementIndex` (Array.maxon:17) is a bare `typealias` — file-visible — so a
  public signature hands back a type no caller can name. MEASURED 2026-09-05 by a textual scan of
  stdlib/ + maxon-bin/: ~235 leaked signatures across 66 files (Array 22, Subprocess 19, Builtins 13,
  Parser 13, Testing 12); `String.from(bytes ByteArray)` is the sharpest, `ByteArray` being file-private
  to String.maxon:26. Treat the count as an order of magnitude — the scan does not resolve file scope.
  ⚠ The compiler already polices the OPPOSITE direction and only that one: E3092 (an `export` nothing
  uses) and E3093 (an `export` that could be `module`). It objects to visibility that is too broad and
  says nothing about visibility that leaks.
  Needs deciding before building: whether the rule covers all named types or only typealiases, whether
  it reaches struct fields and generic arguments, and a new error code in the 3xxx band.
- auto-update the install
- ⛔ `scale-test --repeat=N` (N>=2) REPORTS THE COMPILER NONDETERMINISTIC, and it is the REPEAT that is
  nondeterministic rather than the compiler. Measured 2026-09-07 on BOTH this tree and a build of
  origin HEAD, so it is not new: `scale-test --repeat=3` fails as a BROKEN RUN at whichever rung it
  reaches first, always with the same signature — the later compile of one rung reports exactly +4
  allocs, +4 frees and +4,453 bytes (e.g. rung 3: 17,098,076/13,355,416/1,366,115,712 then
  17,098,080/13,355,420/1,366,120,165). ⭐ THREE SEPARATE PROCESSES AGREE BIT FOR BIT
  (`scale-test --rungs=4 --result-json` x3, identical), so the difference is state carried from one
  compile to the NEXT INSIDE ONE PROCESS, and it grows rather than shrinks — not a lazily-built cache
  the first compile pays for. Consequence: `scripts/self-host-ab.sh` cannot produce its ratio table
  (its default is `--repeat=3`), so the emitted-code A/B is unavailable until this is fixed. The
  FIXPOINT half of that script does run and passes.
- `tests/ladders/` generators are in the state `genrangesites.sh` documents for itself: several emit
  programs that no longer compile. Measured 2026-09-07 against BOTH this tree and origin HEAD, so
  none of it is new — `genshareddag` (E3012 unused variable `base`), `genclosure` in both `ranged`
  (E3005 `Integer` + `Word`) and `plain` (E3062 unused typealias `Word`) modes, and `genfsprobe`
  fails to generate at all. `gennest`, `genemit` and `genrangesites` are the ones that work.
- specs carries 46 ORPHAN golden fragments across four lanes — cases renamed or deleted in older
  commits. The runner names every one on each run. Delete or rename them.
- safeffi
- use code generation for generics to remove monomorphization/witness
- look into making optimizations into compile error (ie hoisting a static value out of a loop)
- emit-asm (for compiler explorer)
- investigate changing Hasher from FNV-1 to SipHash-1-3
- remove @category from stdlib
- ensure static/const unions/enums exist in rdata not the heap (like strings)
- tokenkind should be a type
- multiline string literals using multiple quotes

## TODO
- code coverage during spec tests
- test for __chkstk
- advent of compiler optimization
- 2 types of Stringable, formatted and not formatted
- // Use prevCp to avoid unused parameter warning (reserved for future Extended_Pictographic checks)
- warnings as errors in release mode
- Extra Inhabitants to optimize memory layout
- toLower/toUpper need to be unicode aware, maybe other string functions too
- add "implement interface" code action
- code actions should be directly linked to the errors that made them needed
- oh god locales
- optimize stack arrays (simd, bitmask filtering)
- dedup struct literals with COW ie = OpMeta{latency: 40}
- check for missing fields in struct literals
- tests for Process.executablePath longer than 1024 bytes
- add tests for compiler will all kinds of malformed inputs
- @embedFile from zig for multiline strings
- add "repl" to maxon
- add "lint" to maxon
- add "docs" to maxon

## Ideas
- codelens to show the complexity/cost of a function
- reorganize structs to improve cache locality
- have a command line options stdlib that supplies all the common CLI features (flags, parameters, validation)
  and you just get a type back with everything filled in
- live process monitor (memory allocations, etc)

- how to have the language prevent users doing this
The Trap: If you make an O(n) operation look like a property (s.count), a user might innocently write for i in 0..s.count, inadvertently creating an O(n²) loop because the language recalculates the count on every iteration.


### AI Assistance

1. Provide an llms.txt File
This is an emerging standard (used by projects like Svelte) specifically for AI consumption. While humans like formatted HTML, AI agents perform better with a linear, high-density markdown file.

What it is: A file located at /llms.txt on your docs site.

Why it works: It acts as a curated "brain dump" that agents can ingest in one go, stripping away UI noise and navigation, and focusing purely on syntax, API signatures, and rules.

2. Build a Language Server Protocol (LSP)
Since you are already deep into compiler architecture (IR stages, register allocation, etc.), building an LSP is the "gold standard" for AI effectiveness.

The AI Connection: When an agent (like Cursor or GitHub Copilot) interacts with a codebase, it uses the LSP to understand the symbol graph.

Why it's better than docs: An LSP provides real-time semantic validation. If an AI suggests code that violates your memory management rules, the LSP will flag the error immediately. This allows the agent to "self-correct" before it ever presents the code to you.

3. Create a "Synthetic Golden Dataset"
AI agents struggle with a new language because they lack "intuition" for common patterns. You can bridge this by generating a synthetic dataset of Prompt + Correct Code + Explanation triplets.

Seed Examples: Write 50–100 high-quality "idiomatic" examples covering everything from basic loops to your specific string handling and iterators.

Chain-of-Thought (CoT) Guides: For complex features (like your custom memory management), provide examples that include the "internal monologue" of how to solve a problem in your language.

Example: "To process this list, I must first initialize the iterator because in [Your Language], iterators are stateful..."

4. Grammar-Based Constraints (BNF/Tree-sitter)
If you provide an agent with your language's formal grammar (like a Tree-sitter parser or a BNF file), it can use that to ensure the code it generates is syntactically valid.

Many advanced AI agents can use these files to "constrain" their output, preventing them from hallucinating keywords or syntax from other languages like C# or Rust.

5. Specialized "System Instructions" for the Compiler
Since you're building the compiler itself, you can provide the AI with a "Mental Model of the IR." * Instead of just showing the surface syntax, explain why the compiler expects certain patterns for optimization. When an AI understands the underlying architecture (like how you handle phi nodes), it is less likely to write "clever" code that the compiler can't actually lower to machine code efficiently.
