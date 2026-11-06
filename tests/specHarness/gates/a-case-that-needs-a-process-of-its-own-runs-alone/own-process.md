---
feature: harness-gate-a-case-that-needs-a-process-of-its-own-runs-alone
---
# The a-case-that-needs-a-process-of-its-own-runs-alone gate

The middle case carries `<!-- process: own -->`, so it never joins the batch the other two share: it compiles and runs on its own, in a process where no earlier case has left a marker file in the working directory, and passes. The first case leaves such a marker, the second removes it if it finds one, and the third removes it too, so a run leaves nothing behind.

## Tests

<!-- test: own-process.first -->

```maxon
function main() returns ExitCode
	let marker = FilePath from "own-process-gate.marker"

	if File.exists(marker) 'stale'
		try File.delete(marker) otherwise panic("own-process-gate.marker could not be removed")
	end 'stale'

	try File.writeText(marker, content: "first") otherwise panic("own-process-gate.marker could not be written")
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

<!-- test: own-process.second -->
<!-- process: own -->

```maxon
function main() returns ExitCode
	let marker = FilePath from "own-process-gate.marker"

	if File.exists(marker) 'left'
		try File.delete(marker) otherwise panic("own-process-gate.marker could not be removed")
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

<!-- test: own-process.third -->

```maxon
function main() returns ExitCode
	let marker = FilePath from "own-process-gate.marker"

	if File.exists(marker) 'left'
		try File.delete(marker) otherwise panic("own-process-gate.marker could not be removed")
	end 'left'

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
