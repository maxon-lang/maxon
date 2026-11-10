---
feature: harness-gate-a-run-owns-its-working-directory
---
# The owns-its-directory gate

A run case that reports whether its working directory is named for the run that started it.

## Tests

<!-- test: owns-its-directory.is-named-for-the-run -->

```maxon
function main() returns ExitCode
	let here = Directory.currentPath().filename()

	if here.startsWith(".spec-run-") 'owned'
		print("owned\n")
		return 0
	end 'owned'

	print("shared {here}\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
owned
```
