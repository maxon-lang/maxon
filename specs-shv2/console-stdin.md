---
feature: console-stdin
status: stable
keywords: [console, stdin, readLine, readBytes, eof, __Builtins, readStdin, intrinsics]
category: stdlib
---

# `Console.stdin()` — buffered line-oriented standard input

## Documentation

`stdlib/Console.maxon` gives a program its standard input as a `Stdin` reader that buffers OS
reads. `Console.stdin()` constructs one; `readLine()` returns one line at a time with the
terminator stripped (`\r\n` and `\n` both), `readBytes(n)` drains up to `n` bytes, and `eof()`
answers whether there is genuinely nothing left.

The whole module bottoms out in ONE compiler intrinsic:

| Intrinsic | Meaning |
|---|---|
| `__Builtins.readStdin(maxBytes)` | read up to `maxBytes` bytes from stdin into a FRESH owned `__ManagedMemory`; its `length()` is the count actually read, and **0 means EOF** |

`/specs/builtins-type.md` states that contract in the same words — *"length reflects bytes
actually read; 0 on EOF"* — and it is the whole reason `Stdin` can tell "nothing yet" from "nothing
ever": a NUL scan could not, because stdin is binary and a NUL is a legal byte.

### EOF is what this spec pins, and that is a property of the HARNESS, not a shortcut

`Testing/SpecParser.maxon` has no `stdin` block: a case supplies a source, an argv (`Args:`) and an
expectation, and nothing else. A spec-test binary is spawned through
`SpecTestRunner.runProcess` → `Subprocess.Configuration.create`, whose `standardInput` defaults to
`InputSource.none` — **stdin closed immediately** (`stdlib/Subprocess.maxon:150`, `:313`). So every
case below reads EOF on its first syscall, deterministically, on every run.

That is a real bound on what can be tested here and it is stated rather than hidden: the LINE
SPLITTER (CRLF stripping, a partial final line, the `pending` carry across two chunks) has no
reachable input in this harness and is not pinned by this file. What IS pinned is the half a
closed stdin can reach, and it is the half every consumer hits first — including shv2's own
`Testing/SpecWorkerPool.maxon:425`, whose worker loop treats "EOF on stdin" as *"the parent
vanished, shut down"*. A `readLine()` that returned `""` instead of throwing there would spin a
worker forever.

### ⚠ The PUBLIC surface is exactly two functions, and the module's own comment says otherwise

`stdlib/Console.maxon:35-39` states that *"The instance methods don't need export — Maxon resolves
method calls through inferred receiver types regardless of the type's own export status."* **That is
FALSE, and both compilers say so.** Only `Console.stdin()` and `Stdin.readLine()` carry `export`;
`eof()`, `readBytes()`, `fillOnce()` and the four buffer helpers do not, and a user program naming
one is refused:

```text
shv2:      error E3008: <fragment>:4:9:  function 'Stdin.eof' is not exported
bootstrap: error E3008: main.maxon:4:15: function 'stdlib.Stdin.eof' is not exported
```

MEASURED on the same program under both, which is the only reason this is stated as a fact rather
than as shv2's behaviour. (The two spell the subject differently — the bootstrap qualifies it
`stdlib.Stdin.eof` — which is a namespace-rendering difference, not a disagreement about the rule.)

⇒ **So `eof()`'s BEHAVIOUR is not directly pinnable from a spec case, and no case below pretends to
be pinning it.** What the public surface CAN reach, it reaches through `readLine()`'s throw: the
state machine under it is exercised, and its observable consequence is asserted.

**The REFUSAL is pinnable, though, and it is now pinned** (`error.a-non-exported-method-is-refused`),
because it is the only case in the suite that asks the visibility question of an INSTANCE METHOD.
Every other E3008 case names a free function or a static, and those reach the check by a different
door: a method dispatch's callee head is the RECEIVER'S RESOLVED TYPE, so it carries
`CalleeMint.resolvedTypeQualifier` (F31) and `SemanticCheck.mintSpellsTheWholeCallee` is what keeps
it answerable — a minted callee is otherwise exempt from the export rule, and read as "minted,
therefore the compiler's" that arm would have exempted every method call in every program.

### `eof()` is not "has the stream ended", it is "is there nothing left"

`Stdin.eof()` is `eofSeen and pending.count() == 0`, and `eofSeen` is set only by a read that came
back with 0 bytes. So a FRESH reader answers `false` — nothing has asked the OS anything yet — and
flips only after a read observes the end: the difference between "I know there is nothing" and "I
have not looked". It is documented here because `readLine()`'s loop depends on it, and reached only
through that loop.

### `fillOnce` is idempotent at EOF

Once `eofSeen` is set, `fillOnce` returns without a syscall, so repeated `readLine()` calls at EOF
each throw `ConsoleError.endOfFile` rather than the second one blocking, faulting, or answering a
stale buffer. `repeat-reads-at-eof-are-idempotent` drives it three times.

### The substrate is x64-windows and arm64-macOS at this rung

`__Builtins.readStdin` lowers to `__con_read_stdin`, which fetches the standard handle and reads it —
`GetStdHandle` + `ReadFile` on Windows, and on arm64-macOS the same two ops with no handle call at all:
`osStdHandle` becomes ONE subtraction (Win32's three selectors and POSIX's three descriptors are
consecutive runs) and the read is `read(2)`. It shares the `TargetFacilities` gate with the file,
directory and command-line families, so a program that reaches it on a lane that has neither is refused
with `E3104` at the call's own span rather than panicking inside a backend — see
`Compiler/Runtime/ConsoleRuntime.maxon`'s header for why a WASI `fd_read` lowering is still a rung: a
component holds an input-stream RESOURCE, which is neither a handle nor a descriptor, so there is no
arithmetic that produces one.

## Tests

<!-- test: console-stdin.read-line-at-eof-throws -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The first `readLine()` on a closed stdin throws `ConsoleError.endOfFile`. If it instead SUCCEEDED
with an empty line — the failure a NUL-scanned length would produce — the program falls through to
`return 1`.
```maxon
function main() returns ExitCode
	var stdin = Console.stdin()
	let line = try stdin.readLine() otherwise 'atEof'
		return 9
	end 'atEof'
	print(line)
	return 1
end 'main'
```
```exitcode
9
```

<!-- test: console-stdin.error.a-non-exported-method-is-refused -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐ **THE ONE CASE IN THE SUITE THAT ASKS VISIBILITY OF AN INSTANCE METHOD.** `eof()` carries no
`export`, and a user program holding a `Stdin` value is refused it — the fact the section above
MEASURED under both compilers and left unpinned. It is pinned here because the method-dispatch door
is a different route into `SemanticCheck.calleeVisibleFrom` than the free function and static every
other E3008 case takes, and nothing else would notice if that route stopped asking.
```maxon
function main() returns ExitCode
	var stdin = Console.stdin()
	return 3 if stdin.eof() else 4
end 'main'
```
```maxoncstderr
error E3008: <fragment>:4:20: function 'Stdin.eof' is not exported
```

<!-- test: console-stdin.a-second-reader-also-sees-eof -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
`Stdin` is STATEFUL and its state is PER INSTANCE: a second `Console.stdin()` is a fresh reader with
its own empty buffer and its own `eofSeen`, so it asks the OS itself and reaches the same answer.
That is what makes the module's own warning — *"constructing a new one silently drops anything
already buffered from a prior read syscall"* — a statement about BUFFERED bytes rather than about
the handle, and it is the half a closed stdin can reach. A reader that shared a static handle, or
one whose second construction faulted on a spent stream, fails here where the repeat-read case above
would not notice.
```maxon
function main() returns ExitCode
	var first = Console.stdin()
	let a = try first.readLine() otherwise 'firstAtEof'
		var second = Console.stdin()
		let b = try second.readLine() otherwise 'secondAtEof'
			return 3
		end 'secondAtEof'
		print(b)
		return 1
	end 'firstAtEof'
	print(a)
	return 2
end 'main'
```
```exitcode
3
```

<!-- test: console-stdin.repeat-reads-at-eof-are-idempotent -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
`fillOnce` returns without a syscall once `eofSeen` is set, so the second and third `readLine()`
throw exactly as the first did. A reader that re-issued the read would still be correct here; one
that answered a stale buffer, or that stopped throwing, would not.
```maxon
function main() returns ExitCode
	var stdin = Console.stdin()
	var thrown = 0
	var i = 0
	while i < 3 'again'
		let line = try stdin.readLine() otherwise 'atEof'
			thrown = thrown + 1
			i = i + 1
			continue
		end 'atEof'
		print(line)
		return 1
	end 'again'
	return thrown as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: console-stdin.builtin-answers-zero-bytes-at-eof -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The intrinsic under the module, driven directly: EOF is a length of 0, not a failure and not a
1-byte NUL buffer. A capacity leaking into the length would answer 64.
```maxon
function main() returns ExitCode
	let mm = __Builtins.readStdin(64)
	if mm.length() == 0 'eof'
		return 5
	end 'eof'
	return 1
end 'main'
```
```exitcode
5
```

<!-- test: console-stdin.builtin-result-is-owned -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Each call answers a FRESH owned `__ManagedMemory`, dropped at the end of the statement that bound
it. A hundred of them in a loop is the leak gate's shape: a missing drop is a non-zero mm balance
at exit, which the runtime reports as exit 101 rather than as a wrong number.
```maxon
function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 100 'reads'
		let mm = __Builtins.readStdin(4096)
		total = total + mm.length()
		i = i + 1
	end 'reads'
	return (total + 6) as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: console-stdin.builtin-takes-a-ranged-count -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The byte budget may be spelled with a RANGED typealias, which carries the `named` tag until
TypeResolution collapses it. The narrow `tag == integer` test would refuse this argument by naming
one type twice; the operand check is `tagIsIntegral`, which is the measured-correct predicate for
every builtin argument door.
```maxon
typealias ByteBudget = int(0 to 65535)

function main() returns ExitCode
	let budget = 4096 as ByteBudget
	let mm = __Builtins.readStdin(budget)
	if mm.length() == 0 'eof'
		return 8
	end 'eof'
	return 1
end 'main'
```
```exitcode
8
```

<!-- test: console-stdin.builtin-arity-checked -->
`readStdin` takes exactly one argument. An intrinsic has no signature for the ordinary arity check
to read, so it is refused by the same `builtinArity` check `trunc`/`sleep`/`commandLineArg` use.
```maxon
function main() returns ExitCode
	return __Builtins.readStdin() as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.readStdin' takes exactly 1 argument, but 0 were given
```

<!-- test: console-stdin.builtin-operand-type -->
The byte budget is a COUNT. A String argument is refused at the call site rather than reaching a
runtime that would read a record pointer as a length.
```maxon
function main() returns ExitCode
	return __Builtins.readStdin("lots") as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:20: '__Builtins.readStdin' requires a int, but its argument is String
```

<!-- test: console-stdin.rejected-on-wasm -->
<!-- targets: wasm32-wasi -->
The stdin substrate is x64-windows and arm64-macOS at this rung. On a target with neither the call is
refused at its source span with `E3104`, naming the runtime entry that has no lowering there — never a
panic from inside the wasm backend.
```maxon
function main() returns ExitCode
	let mm = __Builtins.readStdin(64)
	return mm.length() as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:22: this construct is x64-windows only at this rung: it lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
```
