---
feature: nested-typed-array-literal
status: stable
keywords: [array, literal, from, nested, closure, typealias]
category: collections
---

# A typed array literal whose elements are typed array literals

## Documentation

`Alias from [ ... ]` states the literal's element type through the alias, so when that element is itself an
array alias, each element is a value of that alias: `Rows from [Ints from [1, 2], Ints from [3]]` over
`typealias Rows = Array with Ints`. The element is judged against the instance the alias names, whether it is
written as another `from` literal, a bare `[...]` that takes its element type from the alias, or held in a local.
Each element is judged exactly as `push` judges it, so an element of a different instance or of another brand of the same instance is E3005.

## Tests

<!-- test: nested-typed-array-literal-over-ints -->
Each inner `Ints from [...]` is an element of `Rows`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Ints = Array with Integer
typealias Rows = Array with Ints

function main() returns ExitCode
	let rows = Rows from [Ints from [1, 2], Ints from [3]]
	var total = 0 as Integer

	for row in rows 'eachRow'
		for n in row 'eachNumber'
			total = total + n
		end 'eachNumber'
	end 'eachRow'

	print("{rows.count()} {total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 6
```

<!-- test: nested-typed-array-literal-over-a-local -->
An element held in a local is an element of the declared instance as well.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Ints = Array with Integer
typealias Rows = Array with Ints

function main() returns ExitCode
	let first = Ints from [4, 5, 6]
	let rows = Rows from [first, Ints from [7]]
	print("{rows.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: nested-typed-array-literal-over-records -->
The inner elements are records.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export let x as Integer
	export let y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

typealias Points = Array with Point
typealias Paths = Array with Points

function main() returns ExitCode
	let paths = Paths from [Points from [Point.create(1, y: 2), Point.create(3, y: 4)], Points from [Point.create(5, y: 6)]]
	var sum = 0 as Integer

	for path in paths 'eachPath'
		for p in path 'eachPoint'
			sum = sum + p.x + p.y
		end 'eachPoint'
	end 'eachPath'

	print("{paths.count()} {sum}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 21
```

<!-- test: nested-typed-array-literal-over-closures -->
The inner elements are function values that capture a local.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer
typealias Thunks = Array with Thunk
typealias Groups = Array with Thunks

function main() returns ExitCode
	let base = 10 as Integer
	let groups = Groups from [Thunks from [function() gives base + 1, function() gives base + 2], Thunks from [function() gives base + 3]]
	var total = 0 as Integer

	for group in groups 'eachGroup'
		for thunk in group 'eachThunk'
			total = total + thunk()
		end 'eachThunk'
	end 'eachGroup'

	print("{groups.count()} {total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 36
```

<!-- test: nested-typed-array-literal-three-levels -->
Three levels of `from` each take their element type from the alias beside them.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Ints = Array with Integer
typealias Rows = Array with Ints
typealias Cube = Array with Rows

function main() returns ExitCode
	let cube = Cube from [Rows from [Ints from [1, 2], Ints from [3]], Rows from [Ints from [4]]]
	var total = 0 as Integer

	for rows in cube 'eachRows'
		for row in rows 'eachRow'
			for n in row 'eachNumber'
				total = total + n
			end 'eachNumber'
		end 'eachRow'
	end 'eachRows'

	print("{cube.count()} {total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 10
```

<!-- test: nested-typed-array-literal-in-a-field-assignment -->
A nested literal assigned to a field, and one in a constructor's field, whose closures capture a copy of what they read.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer
typealias Thunks = Array with Thunk
typealias Groups = Array with Thunks

type Holder
	export let n as Integer
	export var groups as Groups

	static function create(n Integer) returns Self
		return Self{n: n, groups: Groups from [Thunks from [function() gives n]]}
	end 'create'

	export function refill()
		let current = self.n
		self.groups = Groups from [Thunks from [function() gives current + 1, function() gives current + 2]]
	end 'refill'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create(3)
	var seen = 0 as Integer

	for group in holder.groups 'eachCreated'
		for thunk in group 'eachCreatedThunk'
			seen = seen + thunk()
		end 'eachCreatedThunk'
	end 'eachCreated'

	holder.refill()

	for group in holder.groups 'eachRefilled'
		for thunk in group 'eachRefilledThunk'
			seen = seen + thunk()
		end 'eachRefilledThunk'
	end 'eachRefilled'

	print("{holder.groups.count()} {seen}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 12
```

<!-- test: nested-typed-array-literal-bare-inner-literals -->
An inner `[...]` with no head of its own takes the element type the outer alias states.
```maxon
typealias Total = int(0 to 1000)
typealias Small = int(0 to 100)
typealias Smalls = Array with Small
typealias Table = Array with Smalls

function main() returns ExitCode
	let table = Table from [[1, 2], [3]]
	var total = 0 as Total

	for row in table 'eachRow'
		for n in row 'eachNumber'
			total = total + n as Total
		end 'eachNumber'
	end 'eachRow'

	print("{table.count()} {total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 6
```

<!-- test: nested-typed-array-literal-at-module-level -->
A module-level initializer takes the same nested form.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Ints = Array with Integer
typealias Rows = Array with Ints

let table = Rows from [Ints from [1, 2], Ints from [3]]

function main() returns ExitCode
	print("{table.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: nested-typed-array-literal.error.an-element-of-another-instance -->
An inner literal whose instance is not the declared element is still refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Ints = Array with Integer
typealias Words = Array with String
typealias Rows = Array with Ints

function main() returns ExitCode
	let rows = Rows from [Words from ["a"]]
	return rows.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:24: argument type mismatch for 'value': expected 'Ints', got 'Words'
```

<!-- test: nested-typed-array-literal.error.a-value-of-another-brand -->
Two aliases over one instance are two brands, and a literal element is judged exactly as `push` judges it: a `B` does not fit an `Array with A`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias A = Array with Integer
typealias B = Array with Integer
typealias Rows = Array with A

function main() returns ExitCode
	var b = B.create()
	b.push(1)
	let rows = Rows from [b]
	return rows.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:24: argument type mismatch for 'value': expected 'A', got 'B'
```

<!-- test: nested-typed-array-literal.error.push-refuses-the-same-brand -->
The `push` spelling of the same store is refused in the same words.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias A = Array with Integer
typealias B = Array with Integer
typealias Rows = Array with A

function main() returns ExitCode
	var rows = Rows.create()
	var b = B.create()
	b.push(1)
	rows.push(b)
	return rows.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:7: argument type mismatch for 'value': expected 'A', got 'B'
```

<!-- test: nested-typed-array-literal.error.an-outer-literal-of-another-brand-at-a-declared-slot -->
An outer literal with no head takes the declared slot's instance, so its elements are judged against the slot's brand.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias A = Array with Integer
typealias B = Array with Integer
typealias Rows = Array with A

function width(rows Rows) returns Integer
	return rows.count() as Integer
end 'width'

function main() returns ExitCode
	var first = B.create()
	first.push(1)
	var second = B.create()
	second.push(2)
	return width([first, second]) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:16:16: argument type mismatch for 'value': expected 'A', got 'B'
```

<!-- test: nested-typed-array-literal-an-unbranded-literal-fits-either-brand -->
A literal carries no brand, so the same inner literal fits a slot of either brand.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias A = Array with Integer
typealias B = Array with Integer
typealias RowsOfA = Array with A
typealias RowsOfB = Array with B

function main() returns ExitCode
	let first = RowsOfA from [B from [1], A from [2], [3]]
	let second = RowsOfB from [B from [1], A from [2], [3]]
	print("{first.count()} {second.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 3
```

<!-- test: nested-typed-array-literal-the-same-brand-fits -->
A value of the slot's own brand fits, whether the outer literal states its head or takes the slot's instance.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias A = Array with Integer
typealias Rows = Array with A

function width(rows Rows) returns Integer
	return rows.count() as Integer
end 'width'

function main() returns ExitCode
	var first = A.create()
	first.push(1)
	var second = A.create()
	second.push(2)
	var third = A.create()
	third.push(3)
	let headed = Rows from [first]
	print("{headed.count()} {width([second, third])}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: nested-typed-array-literal-at-a-declared-slot -->
A bare outer literal of bare inner literals takes the element types the declared slot states: a parameter, a field and a return.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Small = int(0 to 100)
typealias Smalls = Array with Small
typealias Table = Array with Smalls

type Holder
	export var table as Table

	static function create() returns Self
		return Self{table: [[1, 2], [3]]}
	end 'create'
end 'Holder'

function width(table Table) returns Integer
	return table.count() as Integer
end 'width'

function make() returns Table
	return [[4, 5], [6], [7]]
end 'make'

function main() returns ExitCode
	let holder = Holder.create()
	print("{holder.table.count()} {width([[1, 2]])} {make().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 1 3
```

<!-- test: nested-typed-array-literal-of-locals-at-a-declared-slot -->
A bare outer literal of two array locals takes the declared slot's instance in a parameter, a field and a return.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Ints = Array with Integer
typealias Rows = Array with Ints

type Holder
	export var rows as Rows

	static function create(first Ints, second Ints) returns Self
		return Self{rows: [first, second]}
	end 'create'
end 'Holder'

function width(rows Rows) returns Integer
	return rows.count() as Integer
end 'width'

function make(first Ints) returns Rows
	return [first]
end 'make'

function main() returns ExitCode
	var branded = Ints.create()
	branded.push(1)
	let plain = Ints from [2]
	let holder = Holder.create(branded, second: plain)
	var again = Ints.create()
	again.push(3)
	var third = Ints.create()
	third.push(4)
	print("{holder.rows.count()} {width([again, third])} {make(plain).count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 2 1
```
