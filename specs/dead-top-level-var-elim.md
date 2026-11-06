---
feature: dead-top-level-var-elim
status: experimental
keywords: [dce, dead-code-elimination, top-level, let, module-init, optimization]
category: optimizations
---
# Dead Top-Level Binding Elimination

## Documentation

### Overview

A top-level `let` whose backing slot is never read by any reachable function is dropped from the `.data` section, and the chain of operations in the synthetic `__module_init_<n>` that populates the slot is removed from `mrt_start`. A constant `let` (a scalar, or a record decided at compile time) has no slot to begin with.

### Mechanism

Implemented at the Std-IR level inside `eliminateDeadStdFunctions` after the function-level reachability walk has populated `liveLabels`. For each `__module_init_<n>` in `project.moduleInitFuncs`:

1. Look up the set of globals it writes from `project.initToStoredVars` (populated at parse time).
2. If none of the corresponding `__data_<name>` labels appear in `liveLabels`, the init is dead.
3. Dead inits are removed from `project.moduleInitFuncs` (so `patchMrtStartWithModuleInits` doesn't emit a `call`), from `project.livenessRoots`, and from `module.functions` (if present). The dead globals are removed from `project.topLevelVars` and `globalData.dataSectionEntries`.

### Limits

- **Side effects in user initializers**: If a top-level `let`'s runtime initializer calls a user function with side effects, the call still runs even when the slot is dead (`specs/lazy-static-observable.md`). The conservative behavior keeps the call but drops the slot.

## Tests

<!-- test: unused-array-literal-init-dropped -->
```maxon
let deadArr = [1, 2, 3, 4, 5]

function main() returns ExitCode
	return 7
end 'main'
```
```exitcode
7
```
