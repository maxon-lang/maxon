---
feature: print-error-function
status: stable
keywords: [printError, stderr, standard error, stdlib, __Builtins, writeStderr, intrinsics]
category: stdlib
---

# `printError` — writing to standard error

## Documentation

`stdlib/PrintError.maxon` declares exactly one free function, and it is `print`'s twin on the
other stream:

```text
export function printError(value String)
	__Builtins.writeStderr(value.addressableBytes())
end 'printError'
```

Like `print`, it takes one `String` and returns nothing. To write anything other than a `String`,
interpolate: `printError("{value}\n")`.

### Why this spec is WRITTEN rather than ported

There is no canonical `/specs/print-error-function.md`. `printError` appears in `/specs` only
incidentally (`source-location-defaults.md`), so there was no file to port byte-identical and this
one is authored — the same route `W5` took for `console-stdin.md` and `process-executable-path.md`.

### The two streams are independent, and that is the property worth pinning

A `print` and a `printError` in the same program must land on *different* streams. A spec that only
checked that the text appeared somewhere would pass just as happily if `printError` were an alias
for `print`, which is exactly the wrong answer this file exists to refuse. So the cases below assert
`stdout` and `stderr` **separately** in one program.

## Tests

<!-- test: basic -->
```maxon
function main() returns ExitCode
	printError("to stderr\n")
	return 0
end 'main'
```
```exitcode
0
```
```stderr
to stderr
```


<!-- test: streams-are-separate -->
```maxon
function main() returns ExitCode
	print("out\n")
	printError("err\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
out
```
```stderr
err
```


<!-- test: interpolation -->
```maxon
function main() returns ExitCode
	let code = 7
	printError("failed with {code}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stderr
failed with 7
```


<!-- test: multiple-calls -->
```maxon
function main() returns ExitCode
	printError("a\n")
	printError("b\n")
	printError("c\n")
	return 0
end 'main'
```
```exitcode
0
```
```stderr
a
b
c
```

### Both spellings of the write's argument agree — and the FOLD that distinguished them is GONE

⛔⛔ **THESE TWO CASES WERE `folded-and-unfolded-agree`, AND W49 WAVE 7 DELETED THE THING THE NAME
ASSERTED.** The section read: *"`stdlib/Print.maxon`'s body is
`__Builtins.writeStdout(value.addressableBytes())`, and `addressableBytes()` is `__str_bytes_view`,
which `__arr_create`s a 48-byte `Array` record so the value can be TYPED as one. The write reads
`buffer@0` and `length@8` — the two slots a String record already carries, at the same offsets — so
that record is minted and destroyed for a consumer that never looks at the one slot the two disagree
on. Left in, it put the allocator, the array runtime and the leak gate into a hello-world: **3,178
code bytes against 1,703** for a program whose only call is `print`. The parser therefore rewrites the
producer INTO the consumer — one op, one field, the callee — but only when the view is still an
unconsumed owned temporary."*

**Every word of that was true and the rewrite no longer happens.** `String.addressableBytes()` retired
onto `stdlib/String.maxon:181`, whose body is `return managed` — so the view is minted one call frame
BELOW the write, where an in-place op rewrite cannot see it, and a value returned across a call
boundary is never the "unconsumed owned temporary" the fold required either. The peephole was deleted
with its last producer; `Parser.emitBuiltinsStreamWrite` carries the account, and the cost is back
(user ruling: accept, record, continue — the real cure is an inliner).

⇒ **WHAT THE TWO CASES STILL PIN IS WHAT THEY ALWAYS ACTUALLY PINNED**: the same intrinsic reached
through an INLINE argument and through a BINDING gives the same ANSWER on the same stream. That was
the property a fold may not change; with no fold it is the property a retirement may not change, and
it is the reason these cases survived the wave rather than being deleted with the optimisation. They
are staged through the stdlib overlay because `addressableBytes()` is stdlib-only. Neither can see an
allocation.

<!-- test: both-argument-spellings-agree -->
```maxon
// --- stdlib-overlay: Builtins.maxon
export typealias WrittenBytes = int(0 to u64.max)

export function writeInlineArgument(text String) returns WrittenBytes
	return __Builtins.writeStdout(text.addressableBytes())
end 'writeInlineArgument'

export function writeThroughABinding(text String) returns WrittenBytes
	let bytes = text.addressableBytes()
	return __Builtins.writeStdout(bytes)
end 'writeThroughABinding'
// --- file: main.maxon
function main() returns ExitCode
	let a = writeInlineArgument("ab\n")
	let b = writeThroughABinding("cde\n")
	return (a + b) as ExitCode
end 'main'
```
```exitcode
7
```
```stdout
ab
cde
```

The same pair on the OTHER stream. It was here because the fold was written once and keyed on the
runtime entry the intrinsic names, so a rewrite that folded only stdout would have passed the case
above; with the fold gone it keeps its own reason — `emitBuiltinsStreamWrite` still serves both
entries from one body, and a change that reached only stdout would still pass the case above.

<!-- test: both-argument-spellings-agree-on-stderr -->
```maxon
// --- stdlib-overlay: Builtins.maxon
export typealias WrittenBytes = int(0 to u64.max)

export function errInlineArgument(text String) returns WrittenBytes
	return __Builtins.writeStderr(text.addressableBytes())
end 'errInlineArgument'

export function errThroughABinding(text String) returns WrittenBytes
	let bytes = text.addressableBytes()
	return __Builtins.writeStderr(bytes)
end 'errThroughABinding'
// --- file: main.maxon
function main() returns ExitCode
	let a = errInlineArgument("xy\n")
	let b = errThroughABinding("z\n")
	return (a + b) as ExitCode
end 'main'
```
```exitcode
5
```
```stderr
xy
z
```
