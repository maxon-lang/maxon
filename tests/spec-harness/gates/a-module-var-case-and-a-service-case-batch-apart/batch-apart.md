---
feature: harness-gate-a-module-var-case-and-a-service-case-batch-apart
---
# The a-module-var-case-and-a-service-case-batch-apart gate

Two cases run a closure on a service's message and two write a module-level `var` from a function whose address they take. Each compiles alone, but one program holding both kinds is refused (E3143: the message's closure call may land on the writer), so the four run batched in two programs.

## Tests

<!-- test: batch-apart.sorts-on-a-message -->

```maxon
type Sorter
	var items as IntArray

	static function create() returns Self
		return Self{items: IntArray.create()}
	end 'create'

	export function add(n Integer)
		self.items.push(n)
	end 'add'

	export function arrange() returns Integer
		self.items.sort(function(a, b) gives a.compare(b))
		let low = try self.items.get(0) otherwise 0
		let high = try self.items.get(2) otherwise 0
		return low + high
	end 'arrange'
end 'Sorter'

function main() returns ExitCode
	let h = spawn Sorter.create()
	h.add(3)
	h.add(1)
	h.add(2)
	let span = try await h.arrange() otherwise 0
	print("span={span}\n")
	return (span - 4) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
```

```exitcode
0
```

```stdout
span=4
```

<!-- test: batch-apart.keeps-a-ledger -->

```maxon
var ledger = 0

typealias Step = function(Integer) returns Integer

function bookKeeping(by Integer) returns Integer
	ledger = ledger + by
	return ledger
end 'bookKeeping'

function callIndirect(f Step, n Integer) returns Integer
	return f(n)
end 'callIndirect'

function main() returns ExitCode
	let l = callIndirect(bookKeeping, n: 2)
	print("ledger={l}\n")
	return (l - 2) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```

```exitcode
0
```

```stdout
ledger=2
```

<!-- test: batch-apart.doubles-on-a-message -->

```maxon
typealias Step = function(Integer) returns Integer

function applyStep(f Step, n Integer) returns Integer
	return f(n)
end 'applyStep'

type Doubler
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function double(by Integer) returns Integer
		return applyStep(function(n Integer) gives n * 2, n: by)
	end 'double'
end 'Doubler'

function main() returns ExitCode
	let h = spawn Doubler.create()
	let n = try await h.double(5) otherwise 0
	print("doubled={n}\n")
	return (n - 10) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```

```exitcode
0
```

```stdout
doubled=10
```

<!-- test: batch-apart.keeps-a-tally -->

```maxon
var tally = 0

typealias Bump = function(Integer) returns Integer

function bump(by Integer) returns Integer
	tally = tally + by
	return tally
end 'bump'

function bumpThrough(f Bump, n Integer) returns Integer
	return f(n)
end 'bumpThrough'

function main() returns ExitCode
	let t = bumpThrough(bump, n: 3)
	print("tally={t}\n")
	return (t - 3) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```

```exitcode
0
```

```stdout
tally=3
```
