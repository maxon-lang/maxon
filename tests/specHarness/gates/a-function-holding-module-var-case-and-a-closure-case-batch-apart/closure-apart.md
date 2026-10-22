---
feature: harness-gate-a-function-holding-module-var-case-and-a-closure-case-batch-apart
---
# The a-function-holding-module-var-case-and-a-closure-case-batch-apart gate

Two cases keep a function in a module-level `var` and two build records holding closures. Each compiles alone, but one program holding both kinds is refused (E3188: the closure calls may reach the module `var`'s storage), so the four run batched in two programs.

## Tests

<!-- test: closure-apart.keeps-a-handler -->

```maxon
typealias Integer = int(i64.min to i64.max)

function idle(n Integer) returns Integer
	return n
end 'idle'

var current = idle

function main() returns ExitCode
	print("handled={current(4)}\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
handled=4
```

<!-- test: closure-apart.holds-two-closures -->

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Op = function(Integer) returns Integer

type PairOne
	export var first as Op
	export var second as Op

	static function create(first Op, second Op) returns Self
		return Self{first: first, second: second}
	end 'create'
end 'PairOne'

function main() returns ExitCode
	let label = "a label padded long enough to heap allocate {3}"
	let f = function(n Integer) gives n + (label.byteLength() as Integer)
	let p = PairOne.create(f, second: f)
	let total = p.first(1) + p.second(2)
	print("total={total}\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
total=93
```

<!-- test: closure-apart.keeps-a-second-handler -->

```maxon
typealias Integer = int(i64.min to i64.max)

function ready(n Integer) returns Integer
	return n + 1
end 'ready'

var handler = ready

function main() returns ExitCode
	print("handled={handler(6)}\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
handled=7
```

<!-- test: closure-apart.holds-two-more-closures -->

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Step = function(Integer) returns Integer

type PairTwo
	export var first as Step
	export var second as Step

	static function create(first Step, second Step) returns Self
		return Self{first: first, second: second}
	end 'create'
end 'PairTwo'

function main() returns ExitCode
	let label = "another label padded long enough to heap allocate {5}"
	let g = function(n Integer) gives n * (label.byteLength() as Integer)
	let p = PairTwo.create(g, second: g)
	let total = p.first(1) + p.second(1)
	print("total={total}\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
total=102
```
