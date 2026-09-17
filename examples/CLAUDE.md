Work here is compiler work.

@../maxon-bin/CLAUDE.md

## binary-trees.maxon

The allocation-bound benchmark. A node cannot hold its children by reference — a type that contains
itself is E4014, through a field, a union payload, a container element or an interface alike — so a
tree is an arena (`Array with Node`) whose nodes name their children by index; every node is still
one managed record. `scripts/bench-binary-trees.py` times it and verifies any depth from the node
count formula; there is no C arm because `vendor/` holds no C binary-trees.

## msort.maxon

The parallel sorter carries its own merge sort because **`Array.sort` cannot be reached from a service
message**: every sort helper under `stdlib/helpers/sort/` calls `Log.trace`, which reads the
module-level `capturing`, and E3143 refuses a module `var` on a green thread. `mergedPair` is the
cure and the design — one merge, used inside a worker by `sortedRun` and across workers by
`mergeChunks`.

Its chunks are built line by line **at the send**, not in a helper: a call result is a legal message
argument only when the fresh-return summary proves it, and that summary treats `x.push(…)` on a
returned local as possibly handing out a second reference. `line.clone()` is what gives each chunk
sole ownership of its strings, which is what a send requires.

