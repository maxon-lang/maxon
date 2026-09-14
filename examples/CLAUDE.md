Work here is compiler work.

@../maxon-bin/CLAUDE.md

## binary-trees.maxon

The allocation-bound benchmark. A node cannot hold its children by reference — a type that contains
itself is E4014, through a field, a union payload, a container element or an interface alike — so a
tree is an arena (`Array with Node`) whose nodes name their children by index; every node is still
one managed record. `scripts/bench-binary-trees.py` times it and verifies any depth from the node
count formula; there is no C arm because `vendor/` holds no C binary-trees.
