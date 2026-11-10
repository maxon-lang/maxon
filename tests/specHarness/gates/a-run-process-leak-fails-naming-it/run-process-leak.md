---
feature: harness-gate-a-run-process-leak
---
# The alone run-process leaves-a-process gate

A run case in a process of its own whose only way of starting a process is the `runProcess` builtin, the scheduler's other spawn door beside the streaming `__gt_subp_*` family and the read probe. It waits for the command it runs, and the command ends leaving a process it started running.

## Tests

<!-- test: run-process-leak.runs-a-command -->
<!-- process: own -->
<!-- tier: stdlib -->

```maxon
function main() returns ExitCode
	#if os(Windows)
		let code = try __Builtins.runProcess("cmd /c start /b ping -n 60 127.0.0.1 >nul") otherwise return 1
	#else
		let code = try __Builtins.runProcess("sleep 60 >/dev/null 2>&1 &") otherwise return 1
	#endif

	if code != 0 'commandFailed'
		return 2
	end 'commandFailed'

	return 0
end 'main'
```

```exitcode
0
```
