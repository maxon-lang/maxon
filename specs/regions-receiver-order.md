---
feature: regions-receiver-order
status: experimental
keywords: [evaluation order, receiver, assignment, values first, method call]
category: semantics
---

# The arguments run before the receiver path is resolved

## Documentation

A call's arguments run first. Then the receiver path is resolved and any region on it is opened, so a receiver path
that an argument changes names the new object. An assignment runs its right-hand side first and then resolves the target
path.

## Tests

<!-- test: a-method-receiver-path-is-resolved-after-the-arguments-run -->
`self.current.bump(self.advance())`: `advance` replaces `current`, and the call goes to the object the path names once the arguments have run.

```maxon
typealias Tally = int(0 to u64.max)

type Stage
	export var total = 0 as Tally
	export var generation = 0 as Tally

	static function create(generation Tally) returns Self
		return Self{generation: generation}
	end 'create'

	function bump(by Tally)
		self.total = self.total + by
	end 'bump'
end 'Stage'

type Pipeline
	var current as Stage

	static function create() returns Self
		return Self{current: Stage.create(0)}
	end 'create'

	function advance() returns Tally
		self.current = Stage.create(self.current.generation + 1)

		return 1
	end 'advance'

	function step() returns String
		self.current.bump(self.advance())

		return "{self.current.generation} {self.current.total}"
	end 'step'
end 'Pipeline'

function main() returns ExitCode
	let pipeline = Pipeline.create()
	print("{pipeline.step()}\n")
	return 0
end 'main'
```
```stdout
1 1
```
```exitcode
0
```

<!-- test: an-assignment-target-path-is-resolved-after-the-right-hand-side-runs -->
`self.current.total = self.advance()`: the total lands on the object the path names once the right-hand side has run.

```maxon
typealias Tally = int(0 to u64.max)

type Stage
	export var total = 0 as Tally
	export var generation = 0 as Tally

	static function create(generation Tally) returns Self
		return Self{generation: generation}
	end 'create'

	function bump(by Tally)
		self.total = self.total + by
	end 'bump'
end 'Stage'

type Pipeline
	var current as Stage

	static function create() returns Self
		return Self{current: Stage.create(0)}
	end 'create'

	function advance() returns Tally
		self.current = Stage.create(self.current.generation + 1)

		return 1
	end 'advance'

	function step() returns String
		self.current.total = self.advance()

		return "{self.current.generation} {self.current.total}"
	end 'step'
end 'Pipeline'

function main() returns ExitCode
	let pipeline = Pipeline.create()
	print("{pipeline.step()}\n")
	return 0
end 'main'
```
```stdout
1 1
```
```exitcode
0
```

<!-- test: a-local-receiver-rewritten-by-its-own-argument-is-refused -->
`label.equals(rewrite(label))`: `rewrite` replaces `label` through its by-reference parameter, and the receiver, a local the call reads before its arguments, would be held across the write.

```maxon
function rewrite(text mutable String) returns String
	text = "replaced"

	return "replaced"
end 'rewrite'

function main() returns ExitCode
	var label = "original"
	let same = label.equals(rewrite(label))
	print("{same}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:10:34: argument 'label' is read by another operand of this statement and passed by reference to 'rewrite' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```
