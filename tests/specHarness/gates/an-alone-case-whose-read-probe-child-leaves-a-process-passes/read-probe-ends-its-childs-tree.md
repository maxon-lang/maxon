---
feature: harness-gate-an-alone-case-whose-read-probe-child-leaves-a-process
---
# The alone read-probe child-tree gate

A run case in a process of its own whose only way of starting a process is the `spawnReadLine` probe. The child writes its line and starts a process that outlives it. The probe ends the child's whole tree before it returns, so the case leaves nothing running.

## Tests

<!-- test: read-probe-ends-its-childs-tree.reads-a-line -->
<!-- process: own -->

```maxon
function main() returns ExitCode
	#if os(Windows)
		let read = spawnReadLine("cmd /c echo hello& start /b ping -n 60 127.0.0.1 >nul")
	#else
		let read = spawnReadLine("echo hello; sleep 60 >/dev/null 2>&1 &")
	#endif

	if read < 1 'nothingRead'
		return 1
	end 'nothingRead'

	return 0
end 'main'
```

```exitcode
0
```
