---
feature: emitted-runtime-body
status: experimental
keywords: [runtime, golden, fragment, codegen, string, view]
category: codegen
---
# Emitted Runtime Bodies

## Documentation

The compiler EMITS more code than the program declares. The entry stub `mrt_start`, the
`__mm_*`/`__slab_*` allocator, the `__str_*`/`__arr_*` families and the `__destruct_*`
thunks are all synthesized by the backend, and a golden fragment shows NONE of them: the
fragment renders only functions parsed from source, because the shared scaffolding is
identical in every test and would be pure noise in all of them.

That default is right for the scaffolding and wrong for a body a rung has just written.
`__str_bytes_view` publishes a zero-copy `Array with Byte` over a String's own bytes and
counts a reference on the allocation those bytes live in; getting that count wrong is a
use-after-free that BALANCES — the matching release disappears with it — so neither the
exit code, nor the leak gate, nor any golden can see it.

### Naming a body to render: ```RequiredRuntime

A test opts one or more emitted runtime functions INTO its own golden with a
```RequiredRuntime block, one name per line:

```
__str_bytes_view
```

Only the named functions are added, and only to that test's fragment; every test without
the block renders byte-identically to a test written before the block existed. A name
that matches no emitted function — or one that names a function the fragment already
shows — is refused by the compiler, so a misspelling cannot silently pin nothing. An
EMPTY block is refused by the spec parser for the same reason: it would render no body at
all, leaving the golden identical to a test that never asked.

### What it pins, and what it does not

⚠ **This block pins the compiler's Target IR — what the backend DECIDED — not the bytes the
linker wrote.** It is the `printDataSection` half of the pair, not the ```RequiredData
half: the text is the printer's own rendering of a lowered, register-allocated body, so it
moves on any change to lowering, allocation or block structure, and is BLIND to anything
below it. An instruction encoder that emits different bytes for the same mnemonic and
operands leaves this golden identical. ```RequiredData / ```RequiredRdata are the gates
that read the linked image back; ```RequiredRuntime is not one of them.

⚠ It also cannot reach a runtime piece that has no IR at all. The hand-assembled byte-level
chunks — `__gt_morestack`, `__gt_context_switch` — never enter the module's function list,
so no spelling of them renders; naming one is refused as "this program emits no function
named …".

## Tests

<!-- test: string-bytes-view-body -->
The `addressableBytes()` / `byteAtOrPanic()` runtime pair, reached through
`stdlib/FilePath.maxon`'s own cone. `__str_bytes_view`'s `bvsep` block is a DETACHED
String's owed-base arm: it carries the incref that keeps the bytes alive for as long as
the view exists.

```maxon
function main() returns ExitCode
	let p = FilePath from "a/b.txt"
	if p.fileExtension() == ".txt" 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredRuntime
__str_bytes_view
__str_byte_at_or_panic
```

<!-- test: error-ordinal-in-an-emitted-body -->
An emitted body transcribes an error enum's ORDINAL as a literal — `__arr_set`'s `rejcont` block
returns ordinal 0 with the error flag set, which is the wire format an `otherwise` arm decodes. The
ordinal is a positional fact about a case LIST, and nothing else in the suite can see the literal
the runtime actually carries: the run only sees that *some* error came back.

```maxon
function main() returns ExitCode
	var names = ["a", "b"]
	try names.set(1, value: "c") otherwise panic("set rejected a valid index")
	return 0
end 'main'
```
```exitcode
0
```
```RequiredRuntime
__arr_set
```

<!-- test: entry-stub-body -->
The `mrt_` band, from the other side of `isRuntimeFunction`'s two prefixes: the entry
stub every program has and no fragment has ever shown.

```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
```RequiredRuntime
mrt_start
```
