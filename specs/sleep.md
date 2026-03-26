---
feature: sleep
status: experimental
keywords: [sleep, async, concurrency, timer]
category: concurrency
---

# Sleep

## Documentation

The `sleep` function suspends the current green thread for a specified number of milliseconds. It yields to the scheduler, allowing other green threads to run during the sleep period.

```text
sleep(500)  // sleep for 500 milliseconds
```

`sleep` works in both async green threads and the main thread. It is a cooperative yield point — other green threads can execute while the current one sleeps.

## Tests

<!-- test: sleep.basic -->
```maxon
function main() returns ExitCode
		sleep(10)
		print("done\n")
		return 0
end 'main'
```
```stdout
done
```

<!-- test: sleep.async-interleave -->
```maxon
typealias Integer = int(i64.min to i64.max)

function slowTask() returns Integer
		sleep(200)
		print("slow\n")
		return 1
end 'slowTask'

function fastTask() returns Integer
		sleep(10)
		print("fast\n")
		return 2
end 'fastTask'

function main() returns ExitCode
		var p1 = async slowTask()
		var p2 = async fastTask()
		var r1 = await p1
		var r2 = await p2
		print("r1={r1} r2={r2}\n")
		return 0
end 'main'
```
```stdout
fast
slow
r1=1 r2=2
```

<!-- test: sleep.zero -->
```maxon
function main() returns ExitCode
		sleep(0)
		print("ok\n")
		return 0
end 'main'
```
```stdout
ok
```
