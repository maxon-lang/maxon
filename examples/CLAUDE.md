Work here is compiler work.

@../maxon-bin/CLAUDE.md

## binary-trees.maxon

The allocation-bound benchmark. A node cannot hold its children by reference — a type that contains
itself is E4014, through a field, a union payload, a container element or an interface alike — so a
tree is an arena (`Array with Node`) whose nodes name their children by index; every node is still
one managed record. `scripts/bench-binary-trees.py` times it and verifies any depth from the node
count formula; there is no C arm because `vendor/` holds no C binary-trees.

## msort.maxon

The parallel sorter carries its own merge sort because **the merge is what the program needs at two
scales, and only one of them is a sort**: `mergedPair` combines two sorted runs, used inside a worker
by `sortedRun` to sort its chunk and again across workers by `mergeChunks` to join the finished
chunks. `Array.sort` would serve the first and cannot serve the second — a chunk merge is handed two
already-sorted arrays and must consume them pairwise — so a stdlib sort in `sortedRun` would leave
`mergedPair` written anyway, with two merge implementations to keep in agreement instead of one.

Its chunks are built line by line **at the send**, not in a helper: a call result is a legal message
argument only when the fresh-return summary proves it, and that summary treats `x.push(…)` on a
returned local as possibly handing out a second reference. `line.clone()` is what gives each chunk
sole ownership of its strings, which is what a send requires.

