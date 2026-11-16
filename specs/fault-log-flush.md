---
feature: fault-log-flush
status: experimental
keywords: [log, fault, stack-overflow, flush, handler, panic, scheduler]
category: runtime
---

# A fatal stack overflow writes the log records still queued

## Documentation

A `Log` record is handed to its handler's mailbox and written when the handler runs. A program that dies on a stack
overflow still writes every record handed over before the overflow, ahead of the panic line: the fatal path gives the
scheduler's processor to another machine, which runs the handler, and waits until nothing else runs or half a second
has passed. A hardware fault does the same: on x64-windows the vectored handler leaves the thread's stack for the system
stack first, and on the POSIX targets the signal handler already runs on the thread's signal stack. A second fatal path
that arrives while the first is flushing waits for it to finish, for at most the same half second. The wait is skipped when
the faulting thread holds a runtime lock, runs on a scheduler's own stack, or is part-way through parking.

A fault whose exception dispatch lands below the bottom of the green thread's stack is the fatal error `exception dispatched
below green thread stack bottom` instead: it flushes no log and runs no green thread, because the dispatch may have
overwritten a neighbour's live stack. The kernel's dispatch of a hardware exception puts the processor's saved state on the
faulting stack, over 12 KB on a host with AMX, so a fault raised from a shallow frame of a stack that has not grown can land
there. A fault raised after the stack has grown dispatches within it and is the panic that flushes.

## Tests

<!-- test: an-error-logged-before-a-stack-overflow-is-written -->
<!-- procs: 1 -->
<!-- preempt: off -->
<!-- unsupported-targets: wasm32-wasi -->
One processor and no preemption keep the handler from running before the recursion overflows, so the record is still
queued when the overflow is reported. The record is written first, then the panic line and the trace.
```maxon
typealias Depth = int(0 to i64.max)

function descend(depth Depth) returns Depth
	return descend(depth + 1) + 1
end 'descend'

function main() returns ExitCode
	Log.setDefault(LogSink.create(spawn(capacity: InstrumentationMailboxCapacity) TextLogHandler.create(LogHandlerOptions.create(LogStream.standardError, addTime: false, addSource: false)), levels: LogLevelVar.create(LogLevels.minimum(LogLevel.info))))
	Log.error("boom")
	let reached = descend(0)
	print("{reached}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
level=ERROR msg=boom
panic: stack overflow
Stack trace:
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  ...additional frames elided...
```

<!-- test: an-error-logged-before-an-access-violation-is-written -->
<!-- tier: stdlib -->
<!-- procs: 1 -->
<!-- preempt: off -->
<!-- unsupported-targets: wasm32-wasi -->
The same holds on a hardware fault raised from a grown stack: on x64-windows the fault runs on the thread's stack, and the
vectored handler leaves for the system stack and flushes before it reports; the POSIX signal handlers already run on the
thread's signal stack and flush there before they report. The program recurses first, so the stack has grown by the time the
fault is raised and the kernel's dispatch fits on every host. A fault whose dispatch lands below the stack's bottom is the
named fatal error, which flushes nothing.
```maxon
typealias Integer = int(i64.min to i64.max)

function descend(depth Integer) returns Integer
	if depth == 0 'bottom'
		return 0
	end 'bottom'

	return descend(depth - 1) + 1
end 'descend'

function main() returns ExitCode
	Log.setDefault(LogSink.create(spawn(capacity: InstrumentationMailboxCapacity) TextLogHandler.create(LogHandlerOptions.create(LogStream.standardError, addTime: false, addSource: false)), levels: LogLevelVar.create(LogLevels.minimum(LogLevel.info))))
	Log.error("boom")

	if descend(1000) != 1000 'grownWrongly'
		return 2
	end 'grownWrongly'

	__Builtins.forceSegfault()
	return 0
end 'main'
```
```exitcode
1
```
```stderr
level=ERROR msg=boom
panic: nil pointer or invalid memory access
Stack trace:
  in maxon_force_segfault
  in main
  in mrt_start
```
