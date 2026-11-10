---
feature: harness-gate-a-case-whose-orphaned-detached-grandchild-outlives-it
---
# The orphaned-grandchild gate

A run case that runs itself as a child. The child detaches a long sleeper, names its id on stdout and exits, so the sleeper outlives the only process that was its parent. The case records the id beside the spec directory and returns 0 without waiting for it.

## Tests

<!-- test: orphaned-grandchild.leaves-one -->
<!-- process: own -->

```maxon
let SleeperLabel = "sleeper="

function detachSleeper() returns ExitCode
	#if os(Windows)
		var config = Configuration.create(Executable.name("ping"))
		config.arguments = StringArray from ["-n", "60", "127.0.0.1"]
	#else
		var config = Configuration.create(Executable.name("sleep"))
		config.arguments = StringArray from ["60"]
	#endif

	let pid = try config.runDetached() otherwise return 3

	print("{SleeperLabel}{pid}\n")
	return 0
end 'detachSleeper'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'asTheChild'
		return detachSleeper()
	end 'asTheChild'

	let me = try Process.executablePath() otherwise return 2
	let child = try Subprocess.run(Executable.path(me), arguments: StringArray from ["child"]) otherwise return 3
	let named = child.stdout.trim().replace(SleeperLabel, with: "")

	try File.writeText(FilePath from "../leaker.pid", content: named) otherwise return 4
	print("child ended\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
child ended
```
