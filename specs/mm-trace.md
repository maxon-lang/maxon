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

<!-- test: string-builder-append-allocates-no-record-per-append -->
A `StringBuilder` must not allocate a record **per `append`**. It holds one growable buffer, and
`build()` hands that buffer over to the finished `String` rather than copying it — so the whole build
costs the builder's buffer, the builder, the `String`, and the builder's fresh reset buffer. Nothing
scales with the number of appends.

This golden exists because it did not: through the envelope-collapse series (Stages 1–3) each
`sb.append(...)` quietly paid a 40-byte `ByteArray` record, because `appendBytes` wrapped its piece in
`ByteArray.init(piece)` purely to hand it to `Array.append`, which only ever read `.managed` back out
again. Since the collapse an `Array` **is** its `__ManagedMemory`, so that wrapper stopped being free —
it became an allocation per append, and no test could see it. `Array.appendMemory` takes the memory
directly. **If a `ByteArray` line appears here once per `append`, that regression is back.**
<!-- MmTrace -->
```maxon
function main() returns ExitCode
	var sb = StringBuilder.create()
	sb.append("ab")
	sb.append("cd")
	let s = sb.build()
	return s.byteLength() as ExitCode
end 'main'
```
```exitcode
4
```

```mm-trace
mm_alloc ByteArray #1 size=40
mm_incref ByteArray #1 rc=1
mm_alloc StringBuilder #2 size=16
mm_incref ByteArray #1 rc=2
mm_decref ByteArray #1 rc=1
mm_incref StringBuilder #2 rc=1
mm_alloc String #3 size=48
mm_incref ByteArray #1 rc=2
mm_incref String #3 rc=1
mm_alloc ByteArray #4 size=40
mm_incref ByteArray #4 rc=1
mm_decref ByteArray #1 rc=1
mm_incref ByteArray #4 rc=2
mm_decref ByteArray #4 rc=1
mm_decref String #3 rc=0
mm_decref ByteArray #1 rc=0
mm_free ByteArray #1
mm_free String #3
mm_decref StringBuilder #2 rc=0
mm_decref ByteArray #4 rc=0
mm_free ByteArray #4
mm_free StringBuilder #2
```

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
mm_alloc String #1 size=57
mm_incref String #1 rc=1
mm_incref String #1 rc=2
mm_decref String #1 rc=1
mm_decref String #1 rc=0
mm_free String #1
```
