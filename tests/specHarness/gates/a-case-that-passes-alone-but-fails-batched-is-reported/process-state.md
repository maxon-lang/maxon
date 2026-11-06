---
feature: harness-gate-a-case-that-passes-alone-but-fails-batched-is-reported
---
# The a-case-that-passes-alone-but-fails-batched-is-reported gate

Two plain run cases that each pass in a process of their own. The first leaves a marker file in the working directory; the second removes the marker if it finds one and prints whether it did. In one process the second sees the first's marker, so batched it fails its stdout pin. The harness diagnoses a batched failure with a compile and run of its own; the marker is gone by then, so the case passes there and FAILS with a message saying it passes alone but fails batched, which is a batching gap the runner must close. The second consumes the marker, so a run leaves nothing behind.

## Tests

<!-- test: process-state.first -->

```maxon
function main() returns ExitCode
	let marker = FilePath from "process-state-gate.marker"

	if File.exists(marker) 'stale'
		try File.delete(marker) otherwise panic("process-state-gate.marker could not be removed")
	end 'stale'

	try File.writeText(marker, content: "first") otherwise panic("process-state-gate.marker could not be written")
	print("first left its marker\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
first left its marker
```

<!-- test: process-state.second -->

```maxon
function main() returns ExitCode
	let marker = FilePath from "process-state-gate.marker"

	if File.exists(marker) 'left'
		try File.delete(marker) otherwise panic("process-state-gate.marker could not be removed")
		print("second found the marker\n")
		return 0
	end 'left'

	print("second found no marker\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
second found no marker
```
