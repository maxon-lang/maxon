*You* Aren't Going To Write It
You *Are* Going To Read It


Sequence: The most basic protocol. Allows you to iterate (use a for loop).

Collection: Extends Sequence. Adds the ability to access elements by index (str[i]) and count elements.

BidirectionalCollection: Extends Collection. Adds the ability to traverse backwards (needed for finding the last index of a character).

StringProtocol: Extends BidirectionalCollection. This is the specific protocol that adds string-like behaviors (comparisons, uppercase/lowercase transformations, C-string interoperability).

Why do this? By making String a Collection, you get hundreds of algorithms for free: .map, .filter, .reduce, .dropFirst, .split. The Swift team didn't have to write these specifically for Strings; they just inherited them from the Collection protocol logic.

## Priorities
- implement map() for Set which should be a generic implementation that also replaces code in Map
- self hosting features
- debugging (speed up the dev process)
- memory safety (generational references)
- parser needs to be strict about new lines

## TODO
- // Use prevCp to avoid unused parameter warning (reserved for future Extended_Pictographic checks)
- implement swift inspired stdlib File support
- see if we can/should get rid of MIR arrays, and also strings use StaticArray
- platform specific optimization for runtime
- warnings as errors in release mode
- compiler error codes
- extensive math function tests
- centralize help text from the lsp server analyzer
- cross compiling
- DeadCodeEliminationPass::isPureFunction has hardcoded list of pure functions. Can analyzer determine if its pure? isPureFunction
- Extra Inhabitants to optimize memory layout
- toLower/toUpper need to be unicode aware, maybe other string functions too
- add "implement interface" code action
- code actions should be directly linked to the errors that made them needed
- type aliases
- lsp is lowercasing paths on windows for comparison which isn't really correct
- oh god locales
- optimize stack arrays (simd, bitmask filtering)

## Ideas
- reorganize structs to improve cache locality
- array syntax: var x = Array of 10 int
- define structs by usage 
	var p = Point { x: 3, y: 4 }
	type has x and y
	var p2 = Point { x: 3, y: 4, z: 0 }
	type now has x y and z

 private type Entry {
        let key: Key
        var value: Value
    }	
- have a command line options stdlib that supplies all the common CLI features (flags, parameters, validation)
  and you just get a type back with everything filled in
- precompile stdlib and link it


- how to have the language prevent users doing this
The Trap: If you make an O(n) operation look like a property (s.count), a user might innocently write for i in 0..s.count, inadvertently creating an O(n²) loop because the language recalculates the count on every iteration.

add "run" to maxon
add "build" to maxon
add "repl" to maxon
add "test" to maxon
add "lint" to maxon
add "profile" to maxon
add "docs" to maxon
add "fmt" to maxon
add package manager to maxon

self hosting
mcp server
AI optimized debugger

get running on linux (devcontainer)
get running on macos

memory safety (arenas)

vscode extention 
	- checks (unneeded fully qualitied name) 
	- quick fixes
		- unneeded type declaration
		- unneeded casts
		- unused parameters
		
	- highlight block identifiers differently than strings
	- enable "go to definition" for variables
	- enable intellisense variable type

add tests for compiler will all kinds of malformed inputs


@embedFile from zig
\\ for multiline strings (zig)


## Future Enhancements

1. **More numeric types:** f32 (single precision), i64 (long), u32 (unsigned), etc.
3. **String type:** Proper string handling beyond char arrays
4. **Array return values:** Transfer ownership when returning arrays from functions
5. **Reference counting:** For complex ownership scenarios
6. **Generics:** Type-parameterized functions
7. **SIMD support:** Vector operations for performance
8. **Array slicing:** Sub-array references without copying


can't instantiate vars with built in types. you have to create a user defined type that defines its range

Struct literal can only be used as an initializer in variable declarations

Rust defaults to Deep Immutability for everything.

	
oh god what about error handling
	- defined error handling block

debugging


Higher RAII - Linear Types




Implement Phi elimination pass in maxon-bin/mir/optimizer.cpp: Create a new pass that inserts Copy instructions at predecessor block ends for each phiIncoming value, then removes the Phi instruction.

Review register allocation liveness analysis at [x86_codegen.cpp:390-500[](c:\Users\Eric\Dev\maxon\maxon-bin\backend\x86_codegen.cpp): The cross-block liveness analysis is conservative but incomplete—it doesn't do full dataflow for values defined in predecessor blocks and used after calls. Compare against LLVM's ](http://_vscodecontentref_/0)LiveIntervals.cpp.

Complete float parameter spilling at x86_codegen.cpp:605: There's a TODO comment indicating float parameters aren't properly spilled to stack when callee-saved XMM registers are exhausted.

Verify large type return handling at x86_codegen.cpp:380-390: The hidden return pointer logic shifts all parameters right by one register—verify this matches Windows x64 ABI exactly and test with functions that have 4+ parameters.

Audit GEP instruction at [x86_codegen.cpp[ genGEP function](c:\Users\Eric\Dev\maxon\maxon-bin\backend\x86_codegen.cpp): Complex index calculation with multiple code paths for arrays vs structs—compare against LLVM's ](http://_vscodecontentref_/1)X86ISelLowering.cpp for GetElementPtr lowering.

backend tests coverage
profiling




A1. The "Clean-Up" Trinity (Essential for Everything)
These passes are valuable not because they make code fast on their own, but because they canonicalize IR so other passes can understand it. If you run nothing else, run these.

SROA (Scalar Replacement of Aggregates):

What it does: Breaks down aggregate structures (like structs and arrays) and stack allocations (alloca) into individual SSA registers.

Why it's #1: LLVM is an SSA-based compiler. Most powerful optimizations (like GVN or Instruction Combination) only work on SSA registers, not on memory. SROA lifts your memory variables into registers, unlocking 90% of the optimizer's power.

Frontend Tip: Don't try to manage registers in your frontend. Just alloca everything on the stack and let SROA promote it.

InstCombine (Instruction Combining):

What it does: A massive collection of peephole optimizations. It simplifies algebraic expressions (e.g., changing x * 2 to x << 1, or x + 0 to x).

Why it's valuable: It canonicalizes code. By reducing complex expressions to a standard form, it allows other passes to recognize patterns they wouldn't see otherwise. It is run multiple times throughout the pipeline.

SimplifyCFG (Simplify Control Flow Graph):

What it does: Removes dead blocks, merges basic blocks that can be joined, and eliminates unnecessary branches.

Why it's valuable: It cleans up the "spaghetti code" generated by high-level control structures (like if/while), making loops easier to analyze for the vectorizer.

2. The Performance Multipliers
Once the code is clean (thanks to the passes above), these passes provide the actual speedups.

The Inliner (AlwaysInliner / SimpleInliner):

Value: Arguably the single most impactful optimization for modern languages (especially C++, Rust, etc.).

Why: It removes function call overhead, but more importantly, it exposes context. If a function takes a constant true as an argument, inlining it allows the optimizer to delete all the if (false) branches inside that function.

GVN (Global Value Numbering):

Value: Eliminates redundant calculations. If you calculate a + b in one place and do it again later (without a or b changing), GVN replaces the second calculation with the result of the first.

Note: It is more powerful than EarlyCSE because it can handle redundancy across different basic blocks.

LICM (Loop Invariant Code Motion):

Value: Hoists calculations out of loops. If you calculate x = y + 5 inside a loop but y never changes, LICM moves it before the loop starts so it only executes once.

3. The Modern Hardware Enablers
These are essential for getting performance out of modern CPUs (AVX, NEON, etc.).

Loop Vectorizer:

Value: Transforms scalar loops (processing one item at a time) into vector loops (processing 4, 8, or 16 items at a time using SIMD instructions).

Requirement: Requires "Canonical Loop Form" (which passes like LoopRotate and IndVarSimplify help create).

SLP Vectorizer (Superword-Level Parallelism):

Value: Vectors straight-line code. If you manually write x1 = a[0] + b[0]; x2 = a[1] + b[1];, SLP packs these into a single vector instruction.


## Potential optimizations
- xor eax,eax to zero register. Done
- use LEA for adds. Planned

Phase 7: Interface conformance checking (verify types implement required methods)
Phase 9: Export keyword enforcement (visibility rules)
Phase 11: Interface declarations (parsing interface definitions)


- extensions
- map extension
- remove [] indexing
