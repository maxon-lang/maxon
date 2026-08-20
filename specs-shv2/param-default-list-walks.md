---
feature: param-default-list-walks
status: selfhosted
keywords: [default, default-values, parameters, ownership, generics, token-scan]
category: core
---

# A Parameter Default Does Not Truncate the Parameter List

## Documentation

shv2 answers several whole-program questions about a function from its TOKENS, before any file is
parsed — which parameters a body moves into durable storage, which type parameter each parameter
feeds, which are bare inner aliases. Each of those is a peek-only walk of the parameter list, and each
advances from one parameter to the next by finding the token that ENDS the current one.

A `= <default>` sits between a parameter's type and that end. A walk that steps by the end of the
TYPE therefore stops at the first defaulted parameter and reports a parameter list with every later
parameter missing — silently, because a short list is a well-formed list. The facts those scans exist
to publish then go unrecorded for the dropped parameters, and every one of them is an OWNERSHIP fact.

These cases pin that the scans see the whole list. Each pairs a program that declares a default
BEFORE the parameter the fact belongs to with the identical program that does not, so the two must
agree; the second is the control, and it passed throughout.

## Tests

<!-- test: generic-feed-after-defaulted-param -->
A defaulted parameter ahead of a type-parameter-typed one. `item` feeds `T`, so a concrete
instantiation over `String` must CONSUME the argument — the box takes ownership of the heap record.
Unrecorded, the caller keeps ownership too, drops the String at scope exit, and the next iteration
reads a freed record: a use-after-free, measured as a SEGFAULT rather than a wrong answer.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var item as T

	static function create(_ Integer = 1, item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 50 'loop'
		let s = "hello!"
		let b = StrBox.create(2, item: s)
		print("{b.item}")
		total = 6
		i = i + 1
	end 'loop'
	return total
end 'main'
```
```exitcode
6
```
```stdout
hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!

```

<!-- test: generic-feed-without-default-control -->
The control: the identical program with the default removed. It must answer the same, and it did
before parameter defaults existed — which is what makes the case above a statement about the walk
rather than about generics.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var item as T

	static function create(_ Integer, item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 50 'loop'
		let s = "hello!"
		let b = StrBox.create(2, item: s)
		print("{b.item}")
		total = 6
		i = i + 1
	end 'loop'
	return total
end 'main'
```
```exitcode
6
```
```stdout
hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!

```

<!-- test: generic-feed-after-two-defaulted-params -->
Two defaults ahead of the fed parameter, one of them a String — so the walk has to step over a
default whose expression is not a bare literal token, and the fed parameter sits at index 2.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var item as T

	static function create(_ Integer = 1, _ String = "n", item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 50 'loop'
		let s = "hello!"
		let b = StrBox.create(2, item: s)
		print("{b.item}")
		total = 6
		i = i + 1
	end 'loop'
	return total
end 'main'
```
```exitcode
6
```
```stdout
hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!hello!

```

<!-- test: default-holding-commas-does-not-split-the-list -->
A default whose expression carries commas of its own — a constructor call — must not be read as
further parameters. `flag` is the third parameter and is reached only if the walk steps over the
whole `Point.create(1, y: 2)` rather than stopping at its first comma.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function pick(base Integer, origin Point = Point.create(1, y: 2), flag bool = true) returns Integer
	if flag 'yes'
		return base + origin.x + origin.y
	end 'yes'
	return base
end 'pick'

function main() returns ExitCode
	return pick(39)
end 'main'
```
```exitcode
42
```
