---
feature: harness-gate-a-case-that-passes-alone-but-fails-batched-is-reported
---
# The a-case-that-passes-alone-but-fails-batched-is-reported gate

Two plain run cases that each pass in a process of their own. The first binds a loopback port and keeps the listener in a module-level variable until its process ends; the second binds the same port and prints that it was refused when the port is taken. In one process the second sees the first's listener, so batched it fails its stdout pin. The harness diagnoses a batched failure with a compile and run of its own; the case passes there, so it FAILS with a message saying it passes alone but fails batched, which is a batching gap the runner must close. The listener dies with the process on every OS and leaves nothing behind.

## Tests

<!-- test: process-state.first -->

```maxon
union Holder
	empty
	holding(listener TcpListener)
end 'Holder'

var held = Holder.empty

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 38417) otherwise 'refused'
		print("first refused\n")
		return 0
	end 'refused'

	held = Holder.holding(listener)
	print("first bound\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
first bound
```

<!-- test: process-state.second -->

```maxon
function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 38417) otherwise 'refused'
		print("second refused\n")
		return 0
	end 'refused'

	print("second bound {listener.port()}\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
second bound 38417
```
