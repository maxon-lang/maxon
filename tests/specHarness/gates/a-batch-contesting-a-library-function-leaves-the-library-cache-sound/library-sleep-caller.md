---
feature: harness-gate-a-batch-contesting-a-library-function-leaves-the-library-cache-sound
---
# The library-sleep caller of the batch-contesting-a-library-function gate

One run case that declares nothing and calls the library's `sleep`. It shares no program with the cases of `contested-library-function.md`.

## Tests

<!-- test: library-sleep-caller.calls-the-librarys-sleep -->

```maxon
function main() returns ExitCode
	sleep(1)
	print("library\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
library
```
