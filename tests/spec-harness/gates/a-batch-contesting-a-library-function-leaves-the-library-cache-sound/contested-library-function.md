---
feature: harness-gate-a-batch-contesting-a-library-function-leaves-the-library-cache-sound
---
# The batch-contesting-a-library-function gate

Two run cases that batch into one program and each declare `sleep` in a directory of their own, so `sleep` is contested between three directories: their two and the library's. The other spec of this fixture calls the library's `sleep`. The pair of specs is run twice under one private run-cache root, and nothing the first run's workers write about the contested program may change what a program that does not contest `sleep` compiles to.

## Tests

<!-- test: contested-library-function.first-declares-sleep -->

```maxon
typealias Ms = int(0 to 1000000)

function sleep(milliseconds Ms)
	print("first {milliseconds}\n")
end 'sleep'

function main() returns ExitCode
	sleep(41)
	return 0
end 'main'
```

```exitcode
0
```

```stdout
first 41
```

<!-- test: contested-library-function.second-declares-sleep -->

```maxon
typealias Ms = int(0 to 1000)

function sleep(milliseconds Ms) returns Ms
	return milliseconds + 7
end 'sleep'

function main() returns ExitCode
	let r = sleep(1)
	print("second {r}\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
second 8
```
