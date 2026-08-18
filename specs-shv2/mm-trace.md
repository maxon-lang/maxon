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
mm_alloc ArrayRecord #1 size=48
mm_alloc StringBuilder #2 size=16
mm_alloc ArrayRecord #3 size=48
mm_alloc ElementBuffer #4 size=4
mm_decref ArrayRecord #3 rc=0
mm_free ArrayRecord #3
mm_alloc ArrayRecord #5 size=48
mm_decref ArrayRecord #5 rc=0
mm_free ArrayRecord #5
mm_incref ArrayRecord #1 rc=2
mm_alloc StringRecord #6 size=53
mm_decref ArrayRecord #1 rc=1
mm_alloc ArrayRecord #7 size=48
mm_decref ArrayRecord #1 rc=0
mm_decref ElementBuffer #4 rc=0
mm_free ElementBuffer #4
mm_free ArrayRecord #1
mm_decref StringRecord #6 rc=0
mm_free StringRecord #6
mm_decref StringBuilder #2 rc=0
mm_decref ArrayRecord #7 rc=0
mm_free ArrayRecord #7
mm_free StringBuilder #2
```

<!-- test: heap-alloc-free -->
An interpolated `String` costs ONE record: one `mm_alloc`, one `mm_free`, and a balanced refcount
column in between. That is what this pins — the allocation count, not the retain count.

The middle `incref`/`decref` pair belongs to `print`. Since Stage 4c of the SSO plan `print` reaches
the bytes through `value.addressableBytes()` rather than the raw `.managed` field, and a callee that
returns a value hands its caller an OWNED reference — so it increfs on the way out and `print`
releases at scope end, where a field read was a borrow and did neither. It buys the thing Stage 4b
needs: one named call site per materialization, which is where a short string's allocation will
appear once its bytes live inline in a register. **If an `mm_alloc` line appears here per `print`,
that is the regression to chase — not this pair.**
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
mm_alloc InterpolationScratch #1 size=21
mm_alloc StringRecord #2 size=57
mm_decref InterpolationScratch #1 rc=0
mm_free InterpolationScratch #1
mm_decref StringRecord #2 rc=0
mm_free StringRecord #2
```
