Work here is compiler work.

@../maxon-bin/CLAUDE.md

## binary-trees.maxon

The allocation-bound benchmark. A node cannot hold its children by reference — a type that contains
itself is E4014, through a field, a union payload, a container element or an interface alike — so a
tree is an arena (`Array with Node`) whose nodes name their children by index; every node is still
one managed record. `scripts/bench-binary-trees.py` times it and verifies any depth from the node
count formula; there is no C arm because `vendor/` holds no C binary-trees.

## msort.maxon

A worker sorts its own chunk with `Array.sort`, from inside a service message — the sort cone reaches
no module-level `var`, so E3143 does not refuse it. What the program still writes for itself is
`mergedPair`, because **joining runs that are already ordered is not a sort and the library has no
call for it**: `mergeChunks` is handed the finished chunks and must consume them pairwise. That is the
one piece of sorting machinery here, and it is stable for the same reason `sort` is — an equal key
takes the left run.

Its chunks are built line by line **at the send**, not in a helper: a call result is a legal message
argument only when the fresh-return summary proves it, and that summary treats `x.push(…)` on a
returned local as possibly handing out a second reference. `line.clone()` is what gives each chunk
sole ownership of its strings, which is what a send requires.

