---
feature: harness-gate-a-case-leaves-a-file
---
# The leaves-a-file gate

A passing run case that writes a file into its working directory and removes nothing.

## Tests

<!-- test: leaves-a-file.writes -->

```maxon
function main() returns ExitCode
	let path = FilePath from "stray-file.txt"
	try File.writeText(path, content: "left behind\n") otherwise return 1
	print("wrote\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
wrote
```
