---
feature: merge-in-an-open
status: stable
keywords: [merge, isolated, modify, one-statement open]
category: semantics
---
# merge inside an open

## Documentation

`merge x` moves an isolated region into the region the enclosing open writes, so `built.reader = merge reader` is accepted in a one-statement open and inside `modify built`, and so is a call into `built` that takes `merge reader`. A `merge` bound to a local first is ordinary mutable data of the current region.

## Tests

<!-- test: a-merge-in-a-one-statement-open-joins-the-opened-region -->
`merge reader` hands an isolated region over to the region `built.reader = …` opens, whether the open is a one-statement open or a `modify` block, so both forms store it.

```maxon
typealias Num = int(i64.min to i64.max)

type Reader
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Reader'

type Built
	export var reader as Reader
	export var total as Num

	static function create() returns Self
		return Self{reader: Reader.create(1), total: 0}
	end 'create'

	function adopt(next Reader)
		self.reader = next
	end 'adopt'
end 'Built'

function oneStatement() returns Num
	let built = Built.create() as isolated Built
	let reader = Reader.create(7) as isolated Reader
	built.reader = merge reader

	return built.reader.n
end 'oneStatement'

function inBlock() returns Num
	let built = Built.create() as isolated Built
	let reader = Reader.create(8) as isolated Reader

	modify built 'fill'
		built.reader = merge reader
	end 'fill'

	return built.reader.n
end 'inBlock'

function main() returns ExitCode
	print("{oneStatement()} {inBlock()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 8
```

<!-- test: a-merge-in-a-call-into-an-isolated-region-joins-it -->
A call that opens `built` takes `merge reader` as an argument for the same reason: the merged region joins the one the call opens.

```maxon
typealias Num = int(i64.min to i64.max)

type Reader
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Reader'

type Built
	export var reader as Reader
	export var total as Num

	static function create() returns Self
		return Self{reader: Reader.create(1), total: 0}
	end 'create'

	function adopt(next Reader)
		self.reader = next
	end 'adopt'
end 'Built'

function work() returns Num
	let built = Built.create() as isolated Built
	let reader = Reader.create(7) as isolated Reader
	built.adopt(merge reader)

	return built.reader.n
end 'work'

function main() returns ExitCode
	print("{work()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: error.a-merge-bound-to-a-local-first-is-outside-data-for-a-later-open -->
`merged` was merged in a statement of its own, so it is mutable data of this region and a later open of `built` cannot take it.

```maxon
typealias Num = int(i64.min to i64.max)

type Reader
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Reader'

type Built
	export var reader as Reader
	export var total as Num

	static function create() returns Self
		return Self{reader: Reader.create(1), total: 0}
	end 'create'

	function adopt(next Reader)
		self.reader = next
	end 'adopt'
end 'Built'

function work() returns Num
	let built = Built.create() as isolated Built
	let reader = Reader.create(7) as isolated Reader
	let merged = merge reader
	built.reader = merged

	return 1
end 'work'

function main() returns ExitCode
	print("{work()}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:29:17: 'merged' is mutable outside 'built'; a store into an isolated region takes values, immutable or isolated
```

<!-- test: error.a-merge-of-a-value-that-is-not-isolated-is-refused-in-an-open -->
`reader` is ordinary mutable data, so there is no isolated region for `merge` to move.

```maxon
typealias Num = int(i64.min to i64.max)

type Reader
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Reader'

type Built
	export var reader as Reader
	export var total as Num

	static function create() returns Self
		return Self{reader: Reader.create(1), total: 0}
	end 'create'

	function adopt(next Reader)
		self.reader = next
	end 'adopt'
end 'Built'

function work() returns Num
	let built = Built.create() as isolated Built
	let reader = Reader.create(7)
	built.reader = merge reader

	return 1
end 'work'

function main() returns ExitCode
	print("{work()}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:28:17: 'reader' is not an isolated value; `merge` moves an isolated region into the current one
```

<!-- test: error.a-merged-value-is-moved-and-cannot-be-read-again -->
`merge reader` moves the region, so `reader` is not readable after the statement.

```maxon
typealias Num = int(i64.min to i64.max)

type Reader
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Reader'

type Built
	export var reader as Reader
	export var total as Num

	static function create() returns Self
		return Self{reader: Reader.create(1), total: 0}
	end 'create'

	function adopt(next Reader)
		self.reader = next
	end 'adopt'
end 'Built'

function work() returns Num
	let built = Built.create() as isolated Built
	let reader = Reader.create(7) as isolated Reader
	built.reader = merge reader

	return built.reader.n + reader.n
end 'work'

function main() returns ExitCode
	print("{work()}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:26: use of moved value 'reader': its ownership moved to another binding at an earlier bind or assignment
```
