---
feature: harness-gate-a-batched-case-leaves-a-process
---
# The batched-leak gate

Three run cases batch into one program. The middle one starts a detached process, records its id beside the spec directory and returns while the process still runs; its neighbours start nothing.

## Tests

<!-- test: batched-leak.before -->

```maxon
function main() returns ExitCode
	print("before\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
before
```

<!-- test: batched-leak.leaves-one -->

```maxon
function main() returns ExitCode
	#if os(Windows)
		let config = Configuration.create(Executable.name("ping"))
		config.arguments = StringArray from ["-n", "60", "127.0.0.1"]
	#else
		let config = Configuration.create(Executable.name("sleep"))
		config.arguments = StringArray from ["60"]
	#endif

	let pid = try config.runDetached() otherwise return 1

	try File.writeText(FilePath from "../leaker.pid", content: "{pid}") otherwise return 2
	print("started\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
started
```

<!-- test: batched-leak.after -->

```maxon
function main() returns ExitCode
	print("after\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
after
```
