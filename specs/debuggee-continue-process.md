---
feature: debuggee-continue-process
status: experimental
keywords: [debuggee, continueProcess, signalProcess, SIGCONT, process signal]
category: system
---

# Continuing a process

## Documentation

`continueProcess(process)` sends SIGCONT to a process, with the signal number of the host it runs on, and answers
whether the kernel delivered it: `true` when it did, `false` when it refused, as it does for a process that has
ended and been reaped. It sends through `signalProcess`, so the answer is the one `signalProcess` gives.

`stopProcess(process)` sends SIGSTOP the same way, with the signal number of the host it runs on, and answers the
same: `true` when the kernel delivered it, `false` when it refused. A process it stopped runs again once
`continueProcess` is sent to it.

Only Linux and macOS have POSIX signals. On Windows and wasm32-wasi a call panics.

## Tests

<!-- test: a-continue-answers-true-for-a-live-process-and-false-for-a-reaped-one -->
<!-- unsupported-targets: x64-windows, wasm32-wasi -->
The program continues itself, which is running, and then a short child it has spawned and waited for, which is ended
and reaped. Windows and wasm32-wasi are excluded because they have no POSIX signals to send, so the call panics
there; wasm32-wasi also cannot spawn the child.

```maxon
function reapedChildId() returns Pid
	let shell = try FilePath.from("/bin/sh") otherwise panic("/bin/sh is a spellable path")
	let finished = try Subprocess.run(Executable.path(shell), arguments: ["-c", "exit 0"]) otherwise panic("the short child could not run")

	return finished.pid
end 'reapedChildId'

function main() returns ExitCode
	print("running {continueProcess(Process.id() as DebuggeeProcessId)}\n")
	print("reaped {continueProcess(reapedChildId() as DebuggeeProcessId)}\n")

	return 0
end 'main'
```
```stdout
running true
reaped false
```
```exitcode
0
```

<!-- test: a-stop-answers-true-for-a-live-child-and-false-for-a-reaped-one -->
<!-- unsupported-targets: x64-windows, wasm32-wasi -->
The program spawns a child that waits on its stdin, stops it, continues it, then closes its stdin so it ends, and waits
for it, so the child is reaped. Stopping the program itself would stop the test. Windows and wasm32-wasi are excluded
because they have no POSIX signals to send, so the call panics there; wasm32-wasi also cannot spawn the child.

```maxon
function main() returns ExitCode
	let cat = try FilePath.from("/bin/cat") otherwise panic("/bin/cat is a spellable path")
	let child = try StreamingSubprocess.spawn(Executable.path(cat), arguments: StringArray.create(0)) otherwise panic("the child could not be spawned")
	let id = try child.processId() otherwise panic("the child has no process id")

	print("stopped {stopProcess(id as DebuggeeProcessId)}\n")
	print("continued {continueProcess(id as DebuggeeProcessId)}\n")

	child.closeStdin()

	let code = try child.wait() otherwise panic("the child could not be waited for")
	print("exited {code}\n")
	print("reaped {stopProcess(id as DebuggeeProcessId)}\n")
	child.release()

	return 0
end 'main'
```
```stdout
stopped true
continued true
exited 0
reaped false
```
```exitcode
0
```
