---
feature: harness-gate-a-case-whose-initializer-runs-code-is-left-out-before-the-batch-runs
---
# The a-case-whose-initializer-runs-code-is-left-out-before-the-batch-runs gate

The middle case's module-level `var` is initialized by a static factory that prints, so it runs alone while the other two still batch.

## Tests

<!-- test: initializer-alone.first -->

```maxon
function main() returns ExitCode
	print("first\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
first
```

<!-- test: initializer-alone.prints-at-initialization -->

```maxon
typealias Tally = int(0 to 100)

type Seed
	export var n as Tally

	static function create() returns Self
		print("initializer ran\n")
		return Self{n: 3}
	end 'create'
end 'Seed'

var seed = Seed.create()

function main() returns ExitCode
	seed.n = seed.n + 1
	print("main {seed.n}\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
initializer ran
main 4
```

<!-- test: initializer-alone.last -->

```maxon
function main() returns ExitCode
	print("last\n")
	return 6
end 'main'
```

```exitcode
6
```

```stdout
last
```
