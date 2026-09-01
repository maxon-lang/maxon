---
feature: debugstream-log-events
status: stable
keywords: [__DebugStream, event, phaseBegin, phaseEnd, text, enabled, debugstream, trace, monitor]
category: system
---

# The `__DebugStream` emitting members

## Documentation

`__DebugStream` is a compiler builtin TYPE with no instances and no state — only static methods.
`nameId` (see `debugstream-name-id.md`) turns a NAME into a number; the five members here are the
ones that PUT SOMETHING ON THE WIRE, or ask whether anything is listening:

```text
__DebugStream.enabled()                                  -> bool
__DebugStream.phaseBegin(nameId, unitId)                 -> nothing
__DebugStream.phaseEnd(nameId, unitId)                   -> nothing
__DebugStream.event(nameId, cat, lvl, unitId, a0, a1)    -> nothing
__DebugStream.text(cat, lvl, unitId, message)            -> nothing
```

The four emitting members return nothing, so a bare statement is the only position they can be
written in. `enabled()` is the one that answers a value.

### TWO GATES, and a program can observe both

⭐ **COMPILE TIME.** Built without `--debugstream`, every emitting member lowers to NOTHING — not a
branch that is never taken, no call and no store — and `enabled()` folds to the constant `false`, so
every body guarded by it is dead before the optimizer sees it. A normal build's emitted code is what
it would have been had the calls not been written.

⭐ **RUN TIME.** Built WITH `--debugstream` but run with no monitor attached, the ring is not mapped:
`enabled()` answers `false` and every emitting member returns without writing. Attach a monitor and
the same binary emits.

⇒ A program can therefore print `enabled()` and see `false` in an ordinary run and `true` under
`maxon monitor`, which is the only difference the flag makes to what a program COMPUTES.

### `nameId` and `unitId` are numbers, and the monitor resolves the name

A phase and an event carry a 16-bit interned name id (`__DebugStream.nameId("…")`), never a String —
that is what lets a compiler pass emit an event from inside its register allocator without
allocating into the very stream the trace exists to read. `unitId` is 32 bits and says WHICH
compilation the event belongs to, so N interleaved emitters can be demuxed back apart.

`cat` and `lvl` are raw bytes whose meaning belongs to the emitting program's own enums; the monitor
prints them as numbers and deliberately does not know what they mean.

### `text` takes the message's BYTES, not a String

The message argument is a `__ManagedMemory` — the buffer a `String`'s `toByteArray()` hands back —
and it is BORROWED: the entry copies the bytes into the ring and never takes ownership. A message
longer than one ring entry can carry is TRUNCATED rather than torn across two entries.

### Every argument is POSITIONAL

⚠ **A `name:` label is REFUSED, and that is a deliberate divergence from the bootstrap**, which
registers parameter names for these members and therefore accepts an optional label. shv2 has no
parameter names for this family — it is a compiler intrinsic no file declares, exactly like
`__Builtins.*` — so there is nothing here for a label to be checked against, and an UNCHECKED label
on a six-argument call is a silent mis-slot rather than a convenience. The positional spelling is
accepted by both compilers, and it is the one the compiler's own emitters write.

## Tests

<!-- test: debugstream-log-events.detached-answers-false-and-emits-nothing -->
An ordinary build — no `--debugstream` — computes exactly what it would have without the calls:
`enabled()` is `false`, and the four emitting members are statements that do nothing at all. This is
the compile-time gate seen from inside the program.
```maxon
function main() returns ExitCode
	let phase = __DebugStream.nameId("compile")
	let ev = __DebugStream.nameId("allocated")
	__DebugStream.phaseBegin(phase, 7)
	__DebugStream.event(ev, 1, 2, 7, 100, 200)
	let msg = "hello ring"
	let bytes = msg.toByteArray()
	__DebugStream.text(1, 2, 7, bytes.managed)
	__DebugStream.phaseEnd(phase, 7)
	print("enabled={__DebugStream.enabled()}\n")
	return 0
end 'main'
```
```stdout
enabled=false
```
```exitcode
0
```

<!-- test: debugstream-log-events.the-four-events-reach-the-ring -->
⭐ **THE WHOLE SURFACE, ON THE WIRE.** Under a monitor the same four statements put four entries in
the ring, in emission order, each carrying the interned NAME the monitor resolves back, the category
and level bytes, the unit, and its own payload — two opaque numbers for an event, the UTF-8 message
for a text. Nothing here is a claim about how the program was compiled: it is what the decoder read
out of the shared segment.

The `gt=…` and `P…` identity the monitor prints is dropped by the normalizer for the reason the
timestamp is — it is a property of the RUN, not of the program. `unit` survives, because the program
chose it.
<!-- LogTrace -->
```maxon
function main() returns ExitCode
	let phase = __DebugStream.nameId("compile")
	let ev = __DebugStream.nameId("allocated")
	__DebugStream.phaseBegin(phase, 7)
	__DebugStream.event(ev, 1, 2, 7, 100, 200)
	let msg = "hello ring"
	let bytes = msg.toByteArray()
	__DebugStream.text(3, 4, 7, bytes.managed)
	__DebugStream.phaseEnd(phase, 7)
	return 0
end 'main'
```
```log-trace
log_phase_begin compile unit=7
log_event allocated cat=1 lvl=2 unit=7 a0=100 a1=200
log_text cat=3 lvl=4 unit=7 hello ring
log_phase_end compile unit=7
```
```exitcode
0
```

<!-- test: debugstream-log-events.a-green-thread-program-logs-before-its-scheduler-exists -->
<!-- targets: x64-windows -->
⛔⛔ **THE CASE THAT CATCHES A PRE-INITIALIZED SCHEDULER READ, AND IT WAS A SEGFAULT.** Every log
entry stamps the green thread that authored it, which since the P landed (`sched-processor.md`) is
`M->currentGt` — reached through this OS thread's TLS slot, at a byte offset `__sched_init_procs`
computes. A program may log BEFORE its first `async`, i.e. before that offset exists, and this is the
shape that does: three events, then a spawn.

The offset's uninitialized value is 0, and `gs:[0]` on Win64 is `NT_TIB.ExceptionList` — a **non-null
pointer**, not a null slot — so an unguarded read follows it and loads `ExceptionList + 0x08`
(`SchedRuntime.MOffCurrentGt`; it was `+ 0x18` while the field was the P's).
MEASURED under `maxon monitor`: exit 42 became a **SEGMENTATION FAULT**, reported by the monitor as
`1 abandoned (producer died mid-entry)`. The `.data` word the P replaced read 0 before init and could
not fail this way, which is why the discipline had never needed writing down.

⭐ **THE `gt=` FIELD IS NORMALIZED OUT OF THE GOLDEN, SO THE ASSERTION IS THE EXIT CODE AND THE THREE
LINES BEING THERE AT ALL** — which is exactly right: what went wrong was not a wrong thread id, it was
the process dying while writing the entry. A run that survives to emit all three and then completes
its `async` is the whole property.
```maxon
function work(n Integer) returns Integer
	__Builtins.parallelBoundary()
	return n * 2
end 'work'

function main() returns ExitCode
	let phase = __DebugStream.nameId("early")
	__DebugStream.phaseBegin(phase, 1)
	__DebugStream.event(phase, 1, 2, 3, 4, 5)
	__DebugStream.phaseEnd(phase, 1)
	let p = async work(21)
	return await p as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```log-trace
log_phase_begin early unit=1
log_event early cat=1 lvl=2 unit=3 a0=4 a1=5
log_phase_end early unit=1
```
```exitcode
42
```

<!-- test: debugstream-log-events.attached-answers-true -->
The run-time gate, from inside the program: the same `enabled()` that printed `false` above prints
`true` when a monitor is attached, and the event it guards is on the wire. The two cases together are
the whole of what the gate does.
<!-- LogTrace -->
```maxon
function main() returns ExitCode
	let ev = __DebugStream.nameId("guarded")
	if __DebugStream.enabled() 'attached'
		__DebugStream.event(ev, 5, 6, 11, 1, 2)
	end 'attached'
	print("enabled={__DebugStream.enabled()}\n")
	return 0
end 'main'
```
```log-trace
log_event guarded cat=5 lvl=6 unit=11 a0=1 a1=2
```
```stdout
enabled=true
```
```exitcode
0
```

<!-- test: debugstream-log-events.a-unit-demuxes-two-emitters -->
`unitId` is what puts N interleaved emitters back in order, so two events written under two different
units stay distinguishable in one ring — which is the whole reason the field is on the wire.
<!-- LogTrace -->
```maxon
function main() returns ExitCode
	let ev = __DebugStream.nameId("step")
	__DebugStream.event(ev, 0, 0, 100, 1, 0)
	__DebugStream.event(ev, 0, 0, 200, 2, 0)
	return 0
end 'main'
```
```log-trace
log_event step cat=0 lvl=0 unit=100 a0=1 a1=0
log_event step cat=0 lvl=0 unit=200 a0=2 a1=0
```
```exitcode
0
```

<!-- test: debugstream-log-events.error.event-arity -->
An intrinsic has no declaration for the ordinary arity check to read, so the count is checked at the
call — the same `builtinArity` every other builtin raises, at the callee's own token.
```maxon
function main() returns ExitCode
	let ev = __DebugStream.nameId("e")
	__DebugStream.event(ev, 1, 2)
	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:4:16: '__DebugStream.event' takes exactly 6 argument, but 3 were given
```

<!-- test: debugstream-log-events.error.phase-begin-wants-numbers -->
Every `__DebugStream` argument but `text`'s message is a machine word. A `String` in one of those
slots is refused where the mistake is, not a pass later.

⚠ *"requires a int"* is the shared `builtinOperandType` sentence every `__Builtins` and `subp*` refusal
already speaks, quoted here as it stands. Its wording is named as its own rung by
`specs-shv2/builtins-type.md`'s disabled `bits-to-float` cases, and moving it would move four spec files'
pins for a reason unrelated to this surface.
```maxon
function main() returns ExitCode
	__DebugStream.phaseBegin("compile", 7)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:16: '__DebugStream.phaseBegin' requires a int, but its argument is String
```

<!-- test: debugstream-log-events.error.text-wants-the-bytes -->
`text`'s message slot takes the `__ManagedMemory` a `toByteArray()` hands back. A `String` is not it:
the entry copies raw bytes and has no String record to read them out of.
```maxon
function main() returns ExitCode
	__DebugStream.text(1, 2, 7, "hello")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:16: '__DebugStream.text' requires a __ManagedMemory, but its argument is String
```

<!-- test: debugstream-log-events.error.an-emitting-member-answers-nothing -->
The four emitting members return nothing, so reading a result is reading a value that is not there —
the same refusal a void user call gets in value position.
```maxon
function main() returns ExitCode
	let ev = __DebugStream.nameId("e")
	let x = __DebugStream.event(ev, 1, 2, 3, 4, 5)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:24: Function '__DebugStream.event' does not return a value
```

<!-- test: debugstream-log-events.error.a-label-is-refused -->
⚠ **THE DIVERGENCE, PINNED.** The bootstrap accepts `unitId: 7` here; shv2 refuses it, because it
has no parameter names for this family and an unchecked label on a positional call is a mis-slot
waiting to happen. See this spec's documentation.
```maxon
function main() returns ExitCode
	let phase = __DebugStream.nameId("compile")
	__DebugStream.phaseBegin(phase, unitId: 7)
	return 0
end 'main'
```
```maxoncstderr
error E2067: <fragment>:4:34: a builtin's arguments are all positional and cannot be named ('name:' labels a parameter, and a builtin has no declaration to have one)
```

<!-- test: debugstream-log-events.error.unknown-member-names-the-callee -->
An unrecognized `__DebugStream` member is refused BY NAME rather than by shape, in statement position
exactly as in expression position — the answer `__Builtins.nope()` already gets one family over.
```maxon
function main() returns ExitCode
	__DebugStream.nope(1)
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:16: call to undefined function '__DebugStream.nope': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```
