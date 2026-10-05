---
feature: harness-gate-a-case-that-needs-a-process-of-its-own-runs-alone
---
# The a-case-that-needs-a-process-of-its-own-runs-alone gate

The middle case carries `<!-- process: own -->`, so it never joins the batch the other two share: it compiles and runs on its own, in a process where no earlier case holds the loopback port it binds, and passes. The first case binds the same port and keeps the listener in a module-level variable until its process ends, which dies with the process on every OS and leaves nothing behind.

## Tests

<!-- test: own-process.first -->

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

<!-- test: own-process.second -->
<!-- process: own -->

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

<!-- test: own-process.third -->

```maxon
function main() returns ExitCode
	print("third\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
third
```
