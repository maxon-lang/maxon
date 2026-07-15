---
feature: mm-trace
status: stable
keywords: mm-trace, memory manager, debug stream, monitor, alloc, refcount, incref, decref, free
category: tooling
---
# mm-trace

## Documentation

`mm-trace` is a spec-test capture mode that verifies the runtime's memory
management behavior — heap allocations, reference-count transitions, and frees —
by decoding the binary debug-event stream the compiled program emits.

A test enters mm-trace mode when it carries either a `<!-- MmTrace -->` directive
or an ` ```mm-trace ` block. In this mode the harness compiles the program with
the shared-memory debug stream enabled, runs it under `maxon monitor --filter=mm`,
and compares the decoded, normalized event trace against the ` ```mm-trace `
golden block.

The golden is normalized so it is stable across runs and machines:

- Timestamps (`[+SSSS.mmm]`) and depth indentation are stripped, leaving the
  bare `mm_<verb> ...` payload.
- Allocation ids (`#<id>`) are densely renumbered `1, 2, 3, …` by first
  appearance, so the runtime's monotonic counter never leaks into the golden.

Regenerate the golden with `--update-required`.

## Tests

<!-- test: heap-alloc-free -->
<!-- MmTrace -->
```maxon
function main() returns ExitCode
	let n = 42
	let s = "value {n}"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```

```mm-trace
mm_alloc String #1 size=48
mm_incref String #1 rc=1
mm_incref String #1 rc=2
mm_decref String #1 rc=1
mm_decref String #1 rc=0
mm_free String #1
```
