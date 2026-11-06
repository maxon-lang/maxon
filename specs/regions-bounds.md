---
feature: regions-bounds
status: experimental
keywords: [region, bound, sendable, isolated, where, generic, instantiation, unique]
category: semantics
---

# `where T is sendable` and `where T is isolated`

## Documentation

A generic type may bound a type parameter with `sendable` or `isolated` in its own `where` clause, and every
instantiation is checked against the bound. A `sendable` bound takes a value, a service handle or an `immutable`
record; it refuses a plain record, which the caller keeps sharing, and a `unique` type, whose values move. An
`isolated` bound takes a value, a service handle or a `unique` type. A function's or an extension's `where`
clause cannot spell either bound.

## Tests

<!-- test: a-sendable-bound-refuses-a-plain-record -->
`Pool with Job` binds `T` to a mutable record the caller keeps, which a `sendable` bound refuses.

```maxon
typealias Tally = int(0 to 1024)

type Job
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Job'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias JobPool = Pool with Job

function main() returns ExitCode
	let pool = JobPool.create(Job.create(1))
	print("{pool.item.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:20:11: 'Job' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable Job`, or a value or a service handle
```

<!-- test: a-sendable-bound-accepts-an-immutable-record -->
`immutable Job` is shared read-only, so the bound holds.

```maxon
typealias Tally = int(0 to 1024)

type Job
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Job'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias FrozenPool = Pool with immutable Job

function main() returns ExitCode
	let pool = FrozenPool.create(freeze Job.create(2))
	print("{pool.item.n}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: a-sendable-bound-accepts-a-value -->
A value type is copied, so it is sendable as it stands.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias TallyPool = Pool with Tally

function main() returns ExitCode
	let pool = TallyPool.create(7)
	print("{pool.item}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: a-sendable-bound-refuses-a-unique-type -->
A `unique` value is never sendable as a type: whether it can be sent depends on the value.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias TokenPool = Pool with Token

function main() returns ExitCode
	let pool = TokenPool.create(Token.create(1))
	print("{pool.item.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:20:11: 'Token' is `unique`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a unique value moves between owners instead of being shared; bound 'T' with `isolated` instead
```

<!-- test: an-isolated-bound-refuses-a-plain-record -->
`Cell with Job` is a mutable record the caller keeps, which an `isolated` bound refuses.

```maxon
typealias Tally = int(0 to 1024)

type Job
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Job'

type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

typealias JobCell = Cell with Job

function main() returns ExitCode
	let cell = JobCell.create(Job.create(1))
	print("{cell.item.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:20:11: 'Job' is not isolated, so it cannot be 'T' of 'Cell', which declares `where T is isolated`: the bound takes a value, a service handle or a `unique` type
```

<!-- test: an-isolated-bound-accepts-a-unique-type -->
A `unique` type is owned and move-only, so an `isolated` bound takes it.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

typealias TokenCell = Cell with Token

function main() returns ExitCode
	let cell = TokenCell.create(Token.create(3))
	print("{cell.item.n}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: a-function-where-clause-cannot-spell-a-region-bound -->
A bound belongs to a generic type, where every instantiation is checked; a function's clause would be a bound nothing checks.

```maxon
function larger(a T, b T) uses T returns T where T is sendable
	return a
end 'larger'

function main() returns ExitCode
	print("{larger(1, b: 2)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:2:44: a function's `where` clause cannot bound a type parameter with `sendable` or `isolated`: those bounds belong to a generic type's own `where` clause, where every instantiation is checked against them
```

<!-- test: a-sendable-bound-reaches-an-instance-made-through-a-type-parameter -->
`Outer uses T` holds `Pool with T`; `JobOuter` fills `T` with a plain record, so the `Pool with Job` it makes breaks the `sendable` bound even though no typealias spells it.

```maxon
typealias Tally = int(0 to 1024)

type Job
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Job'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T
	typealias Inner = Pool with T

	export var pool as Inner

	static function create(item T) returns Self
		return Self{pool: Inner.create(item)}
	end 'create'
end 'Outer'

typealias JobOuter = Outer with Job

function main() returns ExitCode
	let outer = JobOuter.create(Job.create(1))
	print("{outer.pool.item.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:30:11: 'Job' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable Job`, or a value or a service handle
```

<!-- test: a-generic-function-where-clause-with-a-bound-is-refused-at-its-template -->
A generic function whose `where` clause bounds `T` with `isolated` is refused where it is declared, with the same rule as an extension's.

```maxon
typealias Tally = int(0 to 1024)

type Job
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Job'

function hold(item T) uses T returns T where T is isolated
	return item
end 'hold'

function main() returns ExitCode
	let job = hold(Job.create(1))
	print("{job.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:12:40: a function's `where` clause cannot bound a type parameter with `sendable` or `isolated`: those bounds belong to a generic type's own `where` clause, where every instantiation is checked against them
```
