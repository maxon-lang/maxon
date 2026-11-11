---
feature: regions-results
status: experimental
keywords: [result, adapting, mixed return, read-only, frozen, source parameter, call result]
category: semantics
---

# A call result takes its capability from the parameters it can return

## Documentation

A call's result is judged by the arguments at the parameters some return path comes from, and by nothing else. Every
source argument mutable gives a mutable result. Every source argument immutable gives an immutable result when each
path returns a parameter, and a read-only one when some path returns a fresh or local value. Any read-only source
argument gives a read-only result. An argument at a parameter no path returns plays no part. A source that is the
caller's own parameter makes the result read-only unless that parameter is mutable-only.

## Tests

<!-- test: a-mixed-return-given-frozen-data-is-read-only-where-immutable-is-required -->
`pick` returns its parameter on one path and a fresh box on the other, so given frozen data its result may be fresh and writable: it is read-only, and `take` needs it immutable.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(p Box, fresh bool) returns Box
	if fresh 'makeOne'
		return Box.create(0)
	end 'makeOne'

	return p
end 'pick'

function take(item immutable Box) returns Tally
	return item.n
end 'take'

function main() returns ExitCode
	let frozen = freeze Box.create(4)
	print("{take(pick(frozen, fresh: false))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:26:15: 'take' takes `immutable` 'item'; 'pick(frozen, fresh: false)' is read-only
```

<!-- test: a-mixed-return-given-frozen-data-is-read -->
Reading a mixed return's result needs no more than read access, so a frozen argument is accepted.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(p Box, fresh bool) returns Box
	if fresh 'makeOne'
		return Box.create(0)
	end 'makeOne'

	return p
end 'pick'

function main() returns ExitCode
	let frozen = freeze Box.create(4)
	let seen = pick(frozen, fresh: false)
	print("{seen.n}\n")
	return 0
end 'main'
```
```stdout
4
```
```exitcode
0
```

<!-- test: a-mixed-return-given-a-mutable-argument-is-written-through -->
With a mutable source argument the result is mutable, so a write through it reaches the caller's box.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(p Box, fresh bool) returns Box
	if fresh 'makeOne'
		return Box.create(0)
	end 'makeOne'

	return p
end 'pick'

function main() returns ExitCode
	let row = Box.create(1)
	let alias = pick(row, fresh: false)
	alias.n = 9
	print("{row.n}\n")
	return 0
end 'main'
```
```stdout
9
```
```exitcode
0
```

<!-- test: a-result-from-a-mutable-argument-is-writable-beside-a-frozen-one -->
`second` returns only `q`, so the frozen first argument plays no part: the result is mutable and may be written.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function second(p Box, q Box) returns Box
	print("{p.n}\n")
	return q
end 'second'

function main() returns ExitCode
	let frozen = freeze Box.create(4)
	let row = Box.create(1)
	let r = second(frozen, q: row)
	r.n = 7
	print("{row.n}\n")
	return 0
end 'main'
```
```stdout
4
7
```
```exitcode
0
```

<!-- test: a-result-sourced-from-the-callers-own-undecided-parameter-is-read-only -->
`viaParam`'s parameter is only read, so it stays undecided, and a result sourced from it is read-only: `take` needs it immutable.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(p Box, fresh bool) returns Box
	if fresh 'makeOne'
		return Box.create(0)
	end 'makeOne'

	return p
end 'pick'

function take(item immutable Box) returns Tally
	return item.n
end 'take'

function viaParam(p Box) returns Tally
	return take(pick(p, fresh: false))
end 'viaParam'

function main() returns ExitCode
	let row = Box.create(3)
	print("{viaParam(row)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:25:14: 'take' takes `immutable` 'item'; 'pick(p, fresh: false)' is read-only
error E3207: <fragment>:30:19: 'viaParam' needs 'p' frozen (it calls 'take', which takes `immutable` 'item'); 'row' is mutable. Freeze it first
```
