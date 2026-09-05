---
feature: dead-top-level-var-elim
status: experimental
keywords: [dce, dead-code-elimination, top-level, var, module-init, optimization]
category: optimizations
---
# Dead Top-Level Variable Elimination

## Documentation

### Overview

Top-level `var` declarations whose backing slot is never read or written by any reachable function are dropped from the `.data` section, and the chain of operations in the synthetic `__module_init_<n>` that populates the slot is removed from `mrt_start`. The optimization is bidirectional: a `globalLoad` OR a `globalStore` on the slot from any live function keeps the var alive.

### Why It Exists

stdlib code (notably `stdlib/Log.maxon`) declares top-level vars whose initializers allocate heap memory (`var captured = TraceKeyArray.create()`). Without this pass, every user binary pays the allocation cost — even programs that never reference the var. The pass is the optimization referenced by the docstring at the top of `stdlib/Log.maxon`.

### Mechanism

Implemented at the Std-IR level inside `eliminateDeadStdFunctions` after the function-level reachability walk has populated `liveLabels`. For each `__module_init_<n>` in `project.moduleInitFuncs`:

1. Look up the set of vars it writes from `project.initToStoredVars` (populated at parse time).
2. If none of the corresponding `__data_<var>` labels appear in `liveLabels`, the init is dead.
3. Dead inits are removed from `project.moduleInitFuncs` (so `patchMrtStartWithModuleInits` doesn't emit a `call`), from `project.livenessRoots`, and from `module.functions` (if present). The dead vars are removed from `project.topLevelVars` and `globalData.dataSectionEntries`.

The matching cached stdlib init function body never gets pulled into the user binary because nothing references it.

### Limits

- **Side effects in user initializers**: If a top-level var's runtime initializer calls a user function with side effects (`var x = sideEffectingCall()`), the call still runs even when the slot is dead. The conservative behavior keeps the call but drops the slot.
- **Bidirectional liveness**: A var that's only stored to but never read survives. Stores are observable through `globalLoad` or a debugger, so the pass cannot drop them.

## Tests

<!-- test: unused-static-var-dropped -->
```maxon
var unused = 99
var used = 42

function main() returns ExitCode
	return used - 42
end 'main'
```
```exitcode
0
```

<!-- test: unused-array-literal-init-dropped -->
```maxon
let deadArr = [1, 2, 3, 4, 5]

function main() returns ExitCode
	return 7 as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: write-only-var-kept -->
```maxon
var writeOnly = 0
var verifier = 17

function main() returns ExitCode
	writeOnly = 99
	return verifier - 17
end 'main'
```
```exitcode
0
```

<!-- test: dead-scalar-var-leaves-no-data-slot -->
The exit-code cases above compute the right answer whether or not anything is dropped, so they
cannot see this pass at all. `RequiredData` can: it reads the `.data` section back out of the
LINKED binary, so a slot that is still being laid down is a byte at offset 0 that the pin does not
have. The dead global is declared FIRST deliberately — the gate is a PREFIX compare, so a dead
slot has to sit ahead of a live one to be visible to it.

```maxon
var unused = 99
var used = 42

function main() returns ExitCode
	return used - 42
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
```

<!-- test: dead-array-let-leaves-no-data-slot -->
An array `let` is IMAGE DATA: its bytes are laid down in `.rdata` and it reserves no `.data` slot at
all, so `live` is the only global the section may hold. The pin is a PREFIX compare, and the array is
declared FIRST, so a slot that came back would be a byte at offset 0 the pin does not have.

```maxon
let deadArr = [1, 2, 3, 4, 5]
var live = 7

function main() returns ExitCode
	return live
end 'main'
```
```exitcode
7
```
```RequiredData
i64 7
```

<!-- test: dead-global-dropped-beside-a-live-managed-one -->
⭐ **THE PRUNE IS PER-GLOBAL.** v1 prunes all-or-nothing per `__module_init_<n>` and gets away with
it by having one init function per FILE; shv2 has ONE `__module_init` for the whole program, so
all-or-nothing would mean never dropping anything. Here `deadArr` and `liveArr` share that one
function: the dead one's build must go while the live one's stays, and `liveArr.get(1)` still
answering 20 is what proves the surviving record was built correctly rather than merely allocated.

⚠ **BOTH ARRAYS ARE `var`s, AND THAT IS WHAT KEEPS THE CASE ABOUT THIS PASS.** A `let` array is
IMAGE DATA — no slot, no build, nothing for a prune to reach — so declaring either one `let` would
leave `probe` as the only slot and the pin would hold whether or not this pass ran at all.

```maxon
var deadArr = [1, 2, 3]
var liveArr = [10, 20, 30]
var probe = 4

function main() returns ExitCode
	return ((try liveArr.get(1) otherwise 0) - probe) as ExitCode
end 'main'
```
```exitcode
16
```
```RequiredData
i64 0
i64 4
```

<!-- test: write-only-var-kept-beside-a-dead-one -->
⭐ **BOTH DIRECTIONS IN ONE PROGRAM.** `dead` is named nowhere and goes; `writeOnly` is only ever
STORED to and stays, because a store is observable. The pin fails from both sides: too eager and
`.data` is 8 bytes where 16 are claimed, too timid and byte 0 is `dead`'s 1 rather than
`writeOnly`'s 0.

```maxon
var dead = 1
var writeOnly = 0
var live = 5

function main() returns ExitCode
	writeOnly = 99
	return live
end 'main'
```
```exitcode
5
```
```RequiredData
i64 0
i64 5
```
