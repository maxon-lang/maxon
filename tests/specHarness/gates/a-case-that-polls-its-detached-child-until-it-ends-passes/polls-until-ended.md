---
feature: harness-gate-a-case-polls-its-detached-child
---
# The polls-until-ended gate

A run case that detaches a short-lived process and polls it until it has ended before it returns.

## Tests

<!-- test: polls-until-ended.waits -->
<!-- process: own -->

```maxon
let EndsWithinMs = 30000
let PollMs = 50

function main() returns ExitCode
	#if os(Windows)
		var config = Configuration.create(Executable.name("ping"))
		config.arguments = StringArray from ["-n", "2", "127.0.0.1"]
	#else
		var config = Configuration.create(Executable.name("sleep"))
		config.arguments = StringArray from ["0.5"]
	#endif

	let pid = try config.runDetached() otherwise return 1
	var waitedMs = 0 as DurationMs

	while Process.isAlive(pid) and waitedMs < EndsWithinMs 'untilEnded'
		sleep(PollMs)
		waitedMs = waitedMs + PollMs
	end 'untilEnded'

	print("ended={not Process.isAlive(pid)}\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
ended=true
```
