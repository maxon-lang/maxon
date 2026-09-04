---
feature: streaming-subprocess-env
status: experimental
keywords: [subprocess, streaming, StreamingSubprocess, environment, inheritUpdating, spawn, stdio, pipe]
category: system
---

# The streaming child's ENVIRONMENT

## Documentation

`StreamingSubprocess.spawn` and `spawnWithCwd` hand the child this process's own environment and offer
no way to change it: the streaming arm of the spawn core is told to inherit, and the caller's
`Environment` never reaches it. `spawnWithEnvironment` is the door that closes that gap — the same
spawn with the fourth decision, the child's environment, taken by the caller rather than hardcoded.

It matters because a monitor is a streaming parent that has to TELL its child something before the
child's first instruction runs. A DebugStream producer finds the ring it writes into by reading a
name out of its environment, so a consumer that can only inherit can only ever trace a child that was
already going to be traced. `Environment` already answers all three arms — `inherit`,
`inheritUpdating(overrides)`, `custom(vars)` — through `spawnEnvironmentFor`; the streaming spawn is
the only one of the four spawn doors that could not ask it.

⚠ **THE ASSERTION IS THE CHILD'S OWN READING, NOT THE SPAWN'S RETURN**, for the reason
`subprocess-builtins.a-caller-built-environment-is-the-childs-whole-environment` states: a spawn that
merely SUCCEEDS is satisfied by a runtime that accepted the block and then passed the parent's
environment anyway. So the child expands a name the parent never had and echoes it back down the very
pipe the streaming reader is holding.

**Targets — x64-windows only**, the restriction the whole streaming-subprocess family carries: the
reader parks its green thread on the Windows completion driver, and the child here is `cmd`.

## Tests

<!-- test: streaming-subprocess-env.inherit-updating-reaches-the-streaming-child -->
<!-- targets: x64-windows -->
A streaming child is spawned with `Environment.inheritUpdating` naming ONE variable that does not
exist in this process. The child is `cmd /c echo %MAXON_STREAM_ENV_PROBE%`, which expands the name
from the environment it was actually given and writes the result to the stdout pipe; the parent reads
that one line back with `readStdoutLine` (so the terminator is already stripped) and exits with its
byte length — `seen`, 4. A spawn that inherited instead would echo the unexpanded
`%MAXON_STREAM_ENV_PROBE%` literal, which is 24 bytes and not 4, so the wrong answer is a different
exit code rather than a near miss.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("echo")
	argv.push("%MAXON_STREAM_ENV_PROBE%")

	let inheritCwd = try FilePath.from("") otherwise return 3
	let childEnvironment = Environment.inheritUpdating(["MAXON_STREAM_ENV_PROBE": "seen"])
	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.name("cmd"), arguments: argv, workingDirectory: inheritCwd, environment: childEnvironment) otherwise return 4
	let line = try child.readStdoutLine() otherwise return 5
	let code = try child.wait() otherwise return 6
	child.release()

	print("line={line} childCode={code}\n")
	return line.byteLength() as ExitCode
end 'main'
```
```stdout
line=seen childCode=0
```
```exitcode
4
```
