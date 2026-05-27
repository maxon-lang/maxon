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
