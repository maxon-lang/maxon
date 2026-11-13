---
feature: harness-gate-a-case-leaves-a-process
---
# The leaves-a-process gate

A run case whose stdout and exit code match, which starts a detached process, records its id beside the spec directory and returns while the process still runs.

## Tests

<!-- test: leaves-a-process.starts-one -->
<!-- process: own -->

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
