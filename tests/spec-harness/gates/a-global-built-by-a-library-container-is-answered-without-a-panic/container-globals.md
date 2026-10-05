---
feature: harness-gate-a-global-built-by-a-library-container-is-answered-without-a-panic
---
# The a-global-built-by-a-library-container-is-answered-without-a-panic gate

Three run cases each declare a module-level `var` built by a library container's `create()` (`Vector`, `Array`, `Map`), and one reads an enum case, a range bound and a static through member access, beside a plain case. The front-end question the spec runner asks before it batches runs without the library, so a container whose `create()` the library owns must be answered, never crash the worker that asked.

## Tests

<!-- test: container-globals.plain -->

```maxon
function main() returns ExitCode
	print("plain\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
plain
```

<!-- test: container-globals.vector -->

```maxon
typealias Count = int(0 to 1000)
typealias Vec3 = Vector with 3 Count

var shared = Vec3.create()

function main() returns ExitCode
	try shared.set(0, value: 20) otherwise panic("set")
	try shared.set(2, value: 21) otherwise panic("set")
	print("vector {shared.count()}\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
vector 3
```

<!-- test: container-globals.array -->

```maxon
typealias Count = int(0 to 1000)
typealias CountArray = Array with Count

var counts = CountArray.create()

function main() returns ExitCode
	counts.push(5)
	counts.push(6)
	print("array {counts.count()}\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
array 2
```

<!-- test: container-globals.map -->

```maxon
typealias Count = int(0 to u64.max)
typealias StrMap = Map with (String, Count)

var table = StrMap.create()

function main() returns ExitCode
	table.upsert("a", value: 6)
	print("map {table.count()}\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
map 1
```

<!-- test: container-globals.member-reads -->

```maxon
typealias Count = int(0 to 1000)

enum Colour
	red
	green
end 'Colour'

type Config
	static var limit = 5 as Count
end 'Config'

var chosen = Colour.green
var top = u8.max

function main() returns ExitCode
	if chosen == Colour.green 'isGreen'
		print("members {Config.limit} {top}\n")
	end 'isGreen'

	return 0
end 'main'
```

```exitcode
0
```

```stdout
members 5 255
```
