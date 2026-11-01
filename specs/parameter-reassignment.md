---
feature: parameter-reassignment
status: stable
keywords: [parameter, let, reassign, assignment, mutable, by-reference, E2013]
category: statements
---

# Parameters Are `let`

## Documentation

A parameter is a `let` binding: reassigning it is **E2013**, whether the statement is a plain assignment
(`n = n + 1`), a store of a fresh record (`r = Row.create(9)`), or a method that writes a value in place
(`s.append("!")` over a `String` parameter). Writing THROUGH a record parameter — a field store
(`p.field = x`) or a mutating call on a field (`p.items.push(x)`) — is not a reassignment of the parameter and is
unaffected.

```text
error E2013: <fragment>:5:2: 'n' is a parameter and cannot be reassigned; mark it mutable or copy it into a local var
```

A parameter of a value type (a number, `bool`, an enum or `String`) that the function does reassign is declared
`mutable` before its type, which makes it a by-reference parameter (`pass-by-reference.md`). A record-type
parameter is never rebound, marked or not: copy it into a local `var` and reassign the local. A generic body is
checked against the bound of its type parameter only, so a parameter whose type is a type parameter is never
reassigned either, marked or not: the function returns the new value.

## Tests

<!-- test: error.reassigning-a-parameter-is-refused -->
```maxon
typealias Count = int(0 to 100)

function bump(n Count) returns Count
	n = n + 1
	return n
end 'bump'

function main() returns ExitCode
	return bump(1) as ExitCode
end 'main'
```
```maxoncstderr
error E2013: <fragment>:5:2: 'n' is a parameter and cannot be reassigned; mark it mutable or copy it into a local var
```

<!-- test: error.reassigning-a-record-parameter-is-refused -->
```maxon
typealias Count = int(0 to 1000)

type Row
	export var n as Count

	static function create(n Count) returns Row
		return Row{n: n}
	end 'create'
end 'Row'

function swap(r Row) returns Count
	r = Row.create(9)
	return r.n
end 'swap'

function main() returns ExitCode
	return swap(Row.create(1)) as ExitCode
end 'main'
```
```maxoncstderr
error E2013: <fragment>:13:2: 'r' is a record parameter and cannot be reassigned; copy it into a local var
```

<!-- test: error.reassigning-a-string-parameter-is-refused -->
```maxon
function shout(s String)
	s = "{s}!"
	print("{s}\n")
end 'shout'

function main() returns ExitCode
	shout("hey")
	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:3:2: 's' is a parameter and cannot be reassigned; mark it mutable or copy it into a local var
```

<!-- test: error.appending-to-a-string-parameter-is-refused -->
A mutating method on a `String` parameter replaces the value the parameter holds, so it is a reassignment.
```maxon
function shout(s String)
	s.append("!")
	print("{s}\n")
end 'shout'

function main() returns ExitCode
	shout("hey")
	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:3:2: 's' is a parameter and cannot be reassigned; mark it mutable or copy it into a local var
```

<!-- test: error.a-method-reassigning-its-parameter-is-refused -->
```maxon
typealias Count = int(0 to 1000)

type Counter
	export var total as Count

	static function create() returns Counter
		return Counter{total: 0}
	end 'create'

	function add(amount Count) returns Count
		amount = amount + 1
		return self.total + amount
	end 'add'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create()
	return c.add(4) as ExitCode
end 'main'
```
```maxoncstderr
error E2013: <fragment>:12:3: 'amount' is a parameter and cannot be reassigned; mark it mutable or copy it into a local var
```

<!-- test: error.a-parameter-reassigned-on-one-branch-is-refused -->
Reassigning on one path is a reassignment: the parameter is a `let` on every path.
```maxon
typealias Count = int(0 to 100)

function clamp(n Count, high Count) returns Count
	if n > high 'over'
		n = high
	end 'over'

	return n
end 'clamp'

function main() returns ExitCode
	return clamp(9, high: 5) as ExitCode
end 'main'
```
```maxoncstderr
error E2013: <fragment>:6:3: 'n' is a parameter and cannot be reassigned; mark it mutable or copy it into a local var
```

<!-- test: error.mutable-on-a-record-parameter-does-not-allow-reassigning-it -->
A record is never rebound through a parameter, so the marker does not lift the refusal.
```maxon
typealias Count = int(0 to 1000)

type Row
	export var n as Count

	static function create(n Count) returns Row
		return Row{n: n}
	end 'create'
end 'Row'

function swap(r mutable Row) returns Count
	r = Row.create(9)
	return r.n
end 'swap'

function main() returns ExitCode
	var r = Row.create(1)
	return swap(r) as ExitCode
end 'main'
```
```maxoncstderr
error E2013: <fragment>:13:2: 'r' is a record parameter and cannot be reassigned; copy it into a local var
```

<!-- test: a-field-store-and-a-push-through-a-record-parameter-are-writes-through -->
Writing through a record parameter changes the caller's record and does not reassign the parameter.
```maxon
typealias Count = int(0 to 1000)
typealias CountArray = Array with Count

type Row
	export var n as Count
	export var items as CountArray

	static function create(n Count) returns Row
		return Row{n: n, items: CountArray.create()}
	end 'create'
end 'Row'

function fill(r Row)
	r.n = 7
	r.items.push(3)
end 'fill'

function main() returns ExitCode
	var r = Row.create(1)
	fill(r)
	print("{r.n} {r.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 1
```

<!-- test: a-parameter-copied-into-a-local-var-may-be-reassigned -->
The copy is a local, so the parameter is read once and the caller's value is untouched.
```maxon
typealias Count = int(0 to 100)

function bumped(n Count) returns Count
	var m = n
	m = m + 1
	return m
end 'bumped'

function main() returns ExitCode
	let before = 4 as Count
	let after = bumped(before)
	print("{before} {after}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 5
```

<!-- test: a-string-parameter-copied-into-a-local-var-may-be-appended-to -->
```maxon
function shouted(s String) returns String
	var t = s
	t.append("!")
	return t
end 'shouted'

function main() returns ExitCode
	let word = "hey"
	print("{word} {shouted(word)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hey hey!
```

<!-- test: a-mutable-parameter-takes-a-var-and-the-caller-sees-the-write -->
```maxon
typealias Count = int(0 to 100)

function bump(n mutable Count)
	n = n + 1
end 'bump'

function main() returns ExitCode
	var n = 6 as Count
	bump(n)
	print("{n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: error.a-let-handed-to-a-mutable-parameter-is-refused -->
```maxon
typealias Count = int(0 to 100)

function bump(n mutable Count)
	n = n + 1
end 'bump'

function main() returns ExitCode
	let n = 6
	bump(n)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:10:2: cannot pass 'n', which is read-only (a `let` binding), to a function that writes parameter 'n' (in main); pass a clone()
```

<!-- test: error.a-literal-handed-to-a-mutable-parameter-is-refused -->
A literal is not storage, so there is no variable for the write to reach.
```maxon
typealias Count = int(0 to 100)

function bump(n mutable Count)
	n = n + 1
end 'bump'

function main() returns ExitCode
	bump(6)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:9:2: cannot pass a literal to a function that writes parameter 'n' (in main); pass a `var`
```

<!-- test: error.an-expression-handed-to-a-mutable-parameter-is-refused -->
```maxon
typealias Count = int(0 to 100)

function bump(n mutable Count)
	n = n + 1
end 'bump'

function main() returns ExitCode
	var a = 2 as Count
	bump(a + 1)
	return a as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:10:2: cannot pass an expression to a function that writes parameter 'n' (in main); pass a `var`
```

<!-- test: error.a-function-with-a-mutable-parameter-does-not-fit-an-unmarked-function-type -->
```maxon
typealias Count = int(0 to 100)
typealias Step = function(Count)

function bump(n mutable Count)
	n = 7
end 'bump'

function run(f Step)
	f(3)
end 'run'

function main() returns ExitCode
	run(bump)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:2: argument type mismatch for 'f': expected 'fn(Count)', got 'fn(mutable Count)'
```

<!-- test: error.a-closure-with-a-mutable-parameter-does-not-fit-an-unmarked-function-type -->
The marker must match exactly in both directions, so a closure that declares `mutable` and only reads does not fit
`function(Count)` either.
```maxon
typealias Count = int(0 to 100)
typealias Step = function(Count) returns Count

function run(f Step) returns Count
	return f(3)
end 'run'

function main() returns ExitCode
	let r = run(function(n mutable Count) gives n)
	return r as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:10: argument type mismatch for 'f': expected 'fn(Count) returns Count', got 'fn(mutable Count) returns Count'
```

<!-- test: error.a-generic-body-may-not-reassign-a-mutable-parameter-of-its-type-parameter -->
A generic body is checked against the bound of `T` only, and an unbounded `T` may be a record, which is never replaced through a parameter, so the marker does not lift the refusal inside a generic function. The generic function returns the new value instead.
```maxon
function reset(item mutable T, fresh T) uses T
	item = fresh
end 'reset'

function main() returns ExitCode
	var s = "old"
	reset(s, fresh: "new")
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:3:2: 'item' is a generic parameter and cannot be reassigned; return the new value
```

<!-- test: error.a-message-parameter-may-not-be-marked-mutable -->
A message hands its arguments to another green thread, which owns them and writes nothing back, so no message parameter is by reference.
```maxon
typealias Count = int(0 to 1000)

type Counter
	var total as Count

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function add(amount mutable Count)
		self.total = self.total + amount
	end 'add'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	try h.add(1) otherwise ignore
	h.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E2076: <fragment>:11:22: 'amount' is a message parameter and cannot be marked mutable; return the new value instead
```

<!-- test: error.a-factory-parameter-may-not-be-marked-mutable -->
A `spawn` hands the factory's arguments to the service, which owns them and writes nothing back, so no factory parameter is by reference.
```maxon
typealias Count = int(0 to 1000)

type Counter
	var total as Count

	static function create(start mutable Count) returns Self
		return Self{total: start}
	end 'create'

	export function current() returns Count
		return self.total
	end 'current'
end 'Counter'

function main() returns ExitCode
	var seed = 4 as Count
	let h = spawn Counter.create(seed)
	h.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E2076: <fragment>:7:25: 'start' is a factory parameter and cannot be marked mutable; return the new value instead
```
