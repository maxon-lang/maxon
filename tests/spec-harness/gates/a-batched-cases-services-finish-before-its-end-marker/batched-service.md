---
feature: harness-gate-a-batched-cases-services-finish-before-its-end-marker
---
# The a-batched-cases-services-finish-before-its-end-marker gate

The middle case's service prints after its `main` returns, and that output must still be its own when the three run batched.

## Tests

<!-- test: batched-service.before -->

```maxon
function main() returns ExitCode
	print("before\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
before
```

<!-- test: batched-service.spawns -->

```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Plot
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function at(p Point)
		self.n = self.n + 1
		print("at {p.x},{p.y} ({self.n})\n")
	end 'at'
end 'Plot'

function main() returns ExitCode
	let h = spawn Plot.create()
	h.at(Point.create(1, y: 2))
	h.at(Point.create(3, y: 4))
	return 0
end 'main'
```

```exitcode
0
```

```stdout
at 1,2 (1)
at 3,4 (2)
```

<!-- test: batched-service.after -->

```maxon
function main() returns ExitCode
	print("after\n")
	return 5
end 'main'
```

```exitcode
5
```

```stdout
after
```
