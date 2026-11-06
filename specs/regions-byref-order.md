---
feature: regions-byref-order
status: experimental
keywords: [by-reference, overlap, E3186, order, ternary, async, closure, field cell]
category: semantics
---

# Argument overlap follows evaluation order

## Documentation

A local handed to a `mutable` parameter is overwritten right after the call's own operands run. E3186 refuses a read of
that local that runs BEFORE the write: an earlier operand, a ternary condition that wraps the call at any group depth, a
closure literal that captures it, the arguments of the call itself. A read that runs after the write sees the new value
and is accepted. The arms of a ternary count in order. Another call's by-reference argument counts only while that
call's cell is lent, from its argument to its call, so two sibling calls that rewrite one local do not overlap, and a
nested call does while its enclosing call holds the cell.

The refusal is per root binding: it counts reads of the local the cell is rooted in and of that local's field paths.
Another name for the same record (a `view`, a `var` that took the record by move, a `let` alias of a parameter) is not
that local, and the hold a read of it takes keeps the value it read alive across the write.

## Tests

<!-- test: constructor-argument-before -->
A constructor takes the local and the by-reference call's result. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let p = PA.build(label, second: put(label))
	keep(p)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:24:38: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: constructor-argument-after -->
A constructor takes the local and the by-reference call's result. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

type PB
	export var first as Tally
	export var second as String

	static function build(first Tally, second String) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PB'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let p = PB.build(put(label), second: label)
	keep(p)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: union-literal-argument-before -->
A union literal takes the local and the by-reference call's result. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

union UA
	two(first String, second Tally)
	none
end 'UA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function main() returns ExitCode
	var label = "original"
	let u = UA.two(label, second: put(label))
	keep(u)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:24:36: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: union-literal-argument-after -->
A union literal takes the local and the by-reference call's result. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

union UB
	two(first Tally, second String)
	none
end 'UB'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function main() returns ExitCode
	var label = "original"
	let u = UB.two(put(label), second: label)
	keep(u)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: tuple-literal-element-before -->
A tuple literal holds the local and the by-reference call's result. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let t = (label, put(label))
	keep(t)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:15:22: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: tuple-literal-element-after -->
A tuple literal holds the local and the by-reference call's result. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let t = (put(label), label)
	keep(t)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: call-argument-before -->
A call takes the local and the by-reference call's result. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function main() returns ExitCode
	var label = "original"
	let k = two(label, k: put(label))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:19:28: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: call-argument-after -->
A call takes the local and the by-reference call's result. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function main() returns ExitCode
	var label = "original"
	let k = two2(put(label), text: label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: call-field-read-before -->
A call takes a field read of the local and the by-reference call's result. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function sum(a Tally, b Tally) returns Tally
	return a + b
end 'sum'

function main() returns ExitCode
	var label = "original"
	let k = sum(label.byteLength() as Tally, b: put(label))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:19:50: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: call-field-read-after -->
A call takes a field read of the local and the by-reference call's result. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function sum(a Tally, b Tally) returns Tally
	return a + b
end 'sum'

function main() returns ExitCode
	var label = "original"
	let k = sum(put(label), b: label.byteLength() as Tally)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: method-argument-before -->
A method call takes the local and the by-reference call's result as arguments. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	var h = Holder.create()
	let k = h.mix(label, k: put(label))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:33:30: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: method-argument-after -->
A method call takes the local and the by-reference call's result as arguments. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	var h = Holder.create()
	let k = h.mix2(put(label), text: label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: async-argument-before -->
<!-- unsupported-targets: wasm32-wasi -->
An `async` call takes the local and the by-reference call's result. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two3(text String, k Tally) returns Tally
	sleep(1)

	return k + (text.byteLength() as Tally)
end 'two3'

function main() returns ExitCode
	var label = "original"
	let promise = async two3(label, k: put(label))
	let k = await promise
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:21:41: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: async-argument-after -->
<!-- unsupported-targets: wasm32-wasi -->
An `async` call takes the local and the by-reference call's result. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two4(k Tally, text String) returns Tally
	sleep(1)

	return k + (text.byteLength() as Tally)
end 'two4'

function main() returns ExitCode
	var label = "original"
	let promise = async two4(put(label), text: label)
	let k = await promise
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: field-cell-two-levels-before -->
A two-level field cell is handed by reference and the same path read by another operand. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

type Wrapper
	export var h as Holder

	static function create() returns Self
		return Self{h: Holder.create()}
	end 'create'
end 'Wrapper'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var w = Wrapper.create()
	let p = PA.build(w.h.f, second: put(w.h.f))
	keep(p)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:49:38: argument 'w.h.f' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'w.h.f' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: field-cell-two-levels-after -->
A two-level field cell is handed by reference and the same path read by another operand. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

type Wrapper
	export var h as Holder

	static function create() returns Self
		return Self{h: Holder.create()}
	end 'create'
end 'Wrapper'

type PB
	export var first as Tally
	export var second as String

	static function build(first Tally, second String) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PB'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var w = Wrapper.create()
	let p = PB.build(put(w.h.f), second: w.h.f)
	keep(p)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: ternary-condition-before -->
A ternary wrapping the call has a condition reading the local. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let k = put(label) if label.byteLength() > 3 else 0
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:15:14: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: ternary-condition-after -->
A ternary wrapping the call has a condition reading the local. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let k = label.byteLength() as Tally if put(label) > 0 else 0
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: ternary-two-levels-before -->
A ternary wraps a ternary that wraps the call. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let k = (put(label) if label.byteLength() > 3 else 0) if label.byteLength() > 100 else 1
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:15:15: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: ternary-two-levels-after -->
A ternary wraps a ternary that wraps the call. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let k = label.byteLength() as Tally if (1 if put(label) > 0 else 0) > 0 else 0
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: ternary-three-levels-before -->
Three nested ternaries, the read in the outermost condition. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let k = ((put(label) if true else 0) if true else 0) if label.byteLength() > 3 else 0
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:15:16: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: ternary-three-levels-after -->
Three nested ternaries, the read in the outermost condition. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let k = label.byteLength() as Tally if (1 if (1 if put(label) > 0 else 0) > 0 else 0) > 0 else 0
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: ternary-in-an-enclosing-call-before -->
A ternary condition is an argument of the call beside the argument that reads the local. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function main() returns ExitCode
	var label = "original"
	let k = two(label, k: 0 if put(label) > 0 else 1)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:19:33: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: ternary-in-an-enclosing-call-after -->
A ternary condition is an argument of the call beside the argument that reads the local. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function main() returns ExitCode
	var label = "original"
	let k = two2(0 if put(label) > 0 else 1, text: label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: ternary-chained-else-arm-before -->
The inner ternary sits in the outer one's else arm, and the outer then arm reads the local beside the call: the arms are numbered in order, so the read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

function bump(n mutable Tally) returns bool
	n = n + 1

	return true
end 'bump'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var n = 5 as Tally
	let flag = n > 3
	let k = n if flag else 7 if bump(n) else 0
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:16:35: argument 'n' is read by another operand of this statement and passed by reference to 'bump' (parameter 'n') — a hold cannot protect a value its by-reference cell overwrites. Copy 'n' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: ternary-chained-else-arm-then-arm-after -->
The call is in the outer condition and the chained ternary is in the else arm, so every read in the arms runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

function bump(n mutable Tally) returns bool
	n = n + 1

	return true
end 'bump'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var n = 5 as Tally
	let flag = n > 3
	let k = n if bump(n) else 7 if flag else 0
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: ternary-arms-before -->
The arms of a ternary count in order: the arm that reads the local runs beside the arm that writes it. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function wrap(k Tally) returns Tally
	return k
end 'wrap'

function main() returns ExitCode
	var label = "original"
	let flag = label.byteLength() > 3
	let k = wrap(label.byteLength() as Tally) if flag else put(label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:20:61: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: ternary-arms-after -->
The arms of a ternary count in order: the arm that reads the local runs beside the arm that writes it. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function wrap(k Tally) returns Tally
	return k
end 'wrap'

function main() returns ExitCode
	var label = "original"
	let flag = label.byteLength() > 3
	let k = put(label) if flag else wrap(label.byteLength() as Tally)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: nested-inner-call-writes-before -->
The inner call writes and the outer call reads. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function wrap(k Tally) returns Tally
	return k
end 'wrap'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function main() returns ExitCode
	var label = "original"
	let k = two(label, k: wrap(put(label)))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:23:33: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: nested-inner-call-writes-after -->
The inner call writes and the outer call reads. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function wrap(k Tally) returns Tally
	return k
end 'wrap'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function main() returns ExitCode
	var label = "original"
	let k = two2(wrap(put(label)), text: label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: nested-three-deep-writes-before -->
The innermost of three calls writes. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function wrap(k Tally) returns Tally
	return k
end 'wrap'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function main() returns ExitCode
	var label = "original"
	let k = two(label, k: wrap(wrap(wrap(put(label)))))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:23:43: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: nested-three-deep-writes-after -->
The innermost of three calls writes. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function wrap(k Tally) returns Tally
	return k
end 'wrap'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function main() returns ExitCode
	var label = "original"
	let k = two2(wrap(wrap(wrap(put(label)))), text: label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: nested-outer-call-writes -->
The writing call reads the local in its own later operand, which runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

function putK(text mutable String, k Tally) returns Tally
	text = "replaced"

	return k
end 'putK'

function rd(text String) returns Tally
	return text.byteLength() as Tally
end 'rd'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let k = putK(label, k: rd(label))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:19:15: argument 'label' is read by another operand of this statement and passed by reference to 'putK' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: nested-writing-call-then-read -->
The writing call is an operand of a call that reads the local afterwards.

```maxon
typealias Tally = int(0 to u64.max)

function putK2(k Tally, text mutable String) returns Tally
	text = "replaced"

	return k
end 'putK2'

function keep(_ T) uses T
end 'keep'

function wrap(k Tally) returns Tally
	return k
end 'wrap'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function main() returns ExitCode
	var label = "original"
	let k = two2(putK2(wrap(1), text: label), text: label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: sibling-by-reference-calls-before -->
Two sibling calls rewrite the local, with another read around them. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function sum(a Tally, b Tally) returns Tally
	return a + b
end 'sum'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function main() returns ExitCode
	var label = "original"
	let k = two(label, k: sum(put(label), b: put(label)))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:23:32: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: sibling-by-reference-calls-after -->
Two sibling calls rewrite the local, with another read around them. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function sum(a Tally, b Tally) returns Tally
	return a + b
end 'sum'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function main() returns ExitCode
	var label = "original"
	let k = two2(sum(put(label), b: put(label)), text: label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: sibling-by-reference-calls-alone -->
Two sibling calls rewrite the local and nothing else reads it: each lends the cell for its own call.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function sum(a Tally, b Tally) returns Tally
	return a + b
end 'sum'

function main() returns ExitCode
	var label = "original"
	let k = sum(put(label), b: put(label))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: inner-call-writes-while-the-outer-lends -->
The inner call rewrites the local while the outer call holds its cell.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function putK(text mutable String, k Tally) returns Tally
	text = "replaced"

	return k
end 'putK'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let k = putK(label, k: put(label))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:21:15: argument 'label' is read by another operand of this statement and passed by reference to 'putK' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `var` in an earlier statement (`.clone()` a record, array or string), pass the copy for that operand and store it back into 'label' after the call
```

<!-- test: read-between-sibling-writes -->
A read between two sibling writes of the local.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function rd(text String) returns Tally
	return text.byteLength() as Tally
end 'rd'

function keep(_ T) uses T
end 'keep'

function sum3(a Tally, b Tally, c Tally) returns Tally
	return a + b + c
end 'sum3'

function main() returns ExitCode
	var label = "original"
	let k = sum3(put(label), b: rd(label), c: put(label))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:23:48: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: match-subject-before -->
A match subject reads the local and then rewrites it.

```maxon
typealias Tally = int(0 to u64.max)

union UA
	two(first String, second Tally)
	none
end 'UA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function main() returns ExitCode
	var label = "original"
	match UA.two(label, second: put(label)) 'subject'
		two then break
		none then break
	end 'subject'
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:21:34: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: match-subject-after -->
A match subject rewrites the local and then reads it.

```maxon
typealias Tally = int(0 to u64.max)

union UB
	two(first Tally, second String)
	none
end 'UB'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function main() returns ExitCode
	var label = "original"
	match UB.two(put(label), second: label) 'subject'
		two then break
		none then break
	end 'subject'
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: alias-let-before -->
An alias of the local through a `let` is read before the write: it is another binding, not the cell.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function main() returns ExitCode
	var label = "original"
	let alias = label.clone()
	let k = two(alias, k: put(label))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: record-alias-sibling-field-before -->
A second name for the record of a field cell is not the cell's local, and the hold on `h2.f` keeps the old string alive across the write of `h.f`.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function mix(h Holder) returns PA
	let h2 = h
	return PA.build(h2.f, second: put(h.f))
end 'mix'

function main() returns ExitCode
	var h = Holder.create()
	let p = mix(h)
	keep(p)
	print("{p.first}\n")
	return 0
end 'main'
```
```stdout
original
```
```exitcode
0
```

<!-- test: alias-let-after -->
An alias of the local through a `let` is read after the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function main() returns ExitCode
	var label = "original"
	let alias = label.clone()
	let k = two2(put(label), text: alias)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: two-locals-one-rewritten -->
Two locals: one is read first, the other rewritten.

```maxon
typealias Tally = int(0 to u64.max)

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	var other = "other"
	let p = PA.build(label, second: put(other))
	keep(p)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: two-locals-read-before-each-write -->
Each local is read before the other's rewrite, one of them rewritten twice.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function rd(text String) returns Tally
	return text.byteLength() as Tally
end 'rd'

function keep(_ T) uses T
end 'keep'

function sum3(a Tally, b Tally, c Tally) returns Tally
	return a + b + c
end 'sum3'

function main() returns ExitCode
	var label = "original"
	var other = "other"
	let k = sum3(rd(label), b: put(other), c: put(label))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:24:48: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: field-cell-before -->
A field of a local is handed by reference and the same field read by another operand. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var h = Holder.create()
	let p = PA.build(h.f, second: put(h.f))
	keep(p)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:41:36: argument 'h.f' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'h.f' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: field-cell-after -->
A field of a local is handed by reference and the same field read by another operand. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

type PB
	export var first as Tally
	export var second as String

	static function build(first Tally, second String) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PB'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var h = Holder.create()
	let p = PB.build(put(h.f), second: h.f)
	keep(p)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: field-cell-two-levels-sibling-read-before -->
A two-level field cell is handed by reference and a sibling of its parent is read before the write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

type Wrapper
	export var h as Holder

	static function create() returns Self
		return Self{h: Holder.create()}
	end 'create'
end 'Wrapper'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var w = Wrapper.create()
	let p = PA.build(w.h.g, second: put(w.h.f))
	keep(p)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:49:38: argument 'w.h.f' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'w.h.g' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: field-cell-two-levels-whole-root-read-before -->
A two-level field cell is handed by reference and the whole root is read before the write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

type Wrapper
	export var h as Holder

	static function create() returns Self
		return Self{h: Holder.create()}
	end 'create'
end 'Wrapper'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function useW(w Wrapper, k Tally) returns Tally
	return k + (w.h.f.byteLength() as Tally)
end 'useW'

function main() returns ExitCode
	var w = Wrapper.create()
	let k = useW(w, k: put(w.h.f))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:44:25: argument 'w.h.f' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'w' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: field-cell-sibling-read-after -->
A field is handed by reference and a sibling field read after the write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

type PB
	export var first as Tally
	export var second as String

	static function build(first Tally, second String) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PB'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var h = Holder.create()
	let p = PB.build(put(h.f), second: h.g)
	keep(p)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: field-cell-sibling-read-before -->
A field is handed by reference and a sibling field read before the write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var h = Holder.create()
	let p = PA.build(h.g, second: put(h.f))
	keep(p)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:41:36: argument 'h.f' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'h.g' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: field-cell-whole-record-before -->
A field is handed by reference and the whole record read before the write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function useH(h Holder, k Tally) returns Tally
	return k + (h.f.byteLength() as Tally)
end 'useH'

function main() returns ExitCode
	var h = Holder.create()
	let k = useH(h, k: put(h.f))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:36:25: argument 'h.f' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'h' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: field-cell-whole-record-after -->
A field is handed by reference and the whole record read after the write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function useH2(k Tally, h Holder) returns Tally
	return k + (h.f.byteLength() as Tally)
end 'useH2'

function main() returns ExitCode
	var h = Holder.create()
	let k = useH2(put(h.f), h: h)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: field-cell-ternary-before -->
A ternary condition reads the field its arm hands by reference. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var h = Holder.create()
	let k = put(h.f) if h.f.byteLength() > 3 else 0
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:32:14: argument 'h.f' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'h.f' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: field-cell-ternary-after -->
A ternary condition reads the field its arm hands by reference. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var h = Holder.create()
	let k = h.f.byteLength() as Tally if put(h.f) > 0 else 0
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: field-cell-twice-between -->
A read of the field between two writes of it.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function rd(text String) returns Tally
	return text.byteLength() as Tally
end 'rd'

function keep(_ T) uses T
end 'keep'

function sum3(a Tally, b Tally, c Tally) returns Tally
	return a + b + c
end 'sum3'

function main() returns ExitCode
	var h = Holder.create()
	let k = sum3(put(h.f), b: rd(h.f), c: put(h.f))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:40:44: argument 'h.f' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'h.f' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-between-reads -->
A read before and a read after the write: the one before decides.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function rd(text String) returns Tally
	return text.byteLength() as Tally
end 'rd'

function keep(_ T) uses T
end 'keep'

function sum3(a Tally, b Tally, c Tally) returns Tally
	return a + b + c
end 'sum3'

function main() returns ExitCode
	var label = "original"
	let k = sum3(rd(label), b: put(label), c: label.byteLength() as Tally)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:23:33: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-then-two-reads -->
Two reads after the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function rd(text String) returns Tally
	return text.byteLength() as Tally
end 'rd'

function keep(_ T) uses T
end 'keep'

function sum3(a Tally, b Tally, c Tally) returns Tally
	return a + b + c
end 'sum3'

function main() returns ExitCode
	var label = "original"
	let k = sum3(put(label), b: label.byteLength() as Tally, c: rd(label))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: write-then-read-inside-a-later-call -->
A read after the write, inside the nested call of a later operand.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function rd(text String) returns Tally
	return text.byteLength() as Tally
end 'rd'

function keep(_ T) uses T
end 'keep'

function wrap(k Tally) returns Tally
	return k
end 'wrap'

function sum(a Tally, b Tally) returns Tally
	return a + b
end 'sum'

function main() returns ExitCode
	var label = "original"
	let k = sum(put(label), b: wrap(rd(label)))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: two-writes-two-reads-after -->
Two writes of the same local, then reads.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function rd(text String) returns Tally
	return text.byteLength() as Tally
end 'rd'

function keep(_ T) uses T
end 'keep'

function mix4(a Tally, b Tally, c Tally, d Tally) returns Tally
	return a + b + c + d
end 'mix4'

function main() returns ExitCode
	var label = "original"
	let k = mix4(put(label), b: put(label), c: label.byteLength() as Tally, d: rd(label))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: returned-alias-before -->
An operand returns the local's own value. The read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function id(text String) returns String
	return text
end 'id'

function main() returns ExitCode
	var label = "original"
	let p = PA.build(id(label), second: put(label))
	keep(p)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:28:42: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: returned-alias-after -->
An operand returns the local's own value. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

type PB
	export var first as Tally
	export var second as String

	static function build(first Tally, second String) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PB'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function id(text String) returns String
	return text
end 'id'

function main() returns ExitCode
	var label = "original"
	let p = PB.build(put(label), second: id(label))
	keep(p)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: returned-alias-kept-and-rewritten -->
The alias of the new value is kept and the local rewritten later.

```maxon
typealias Tally = int(0 to u64.max)

type PB
	export var first as Tally
	export var second as String

	static function build(first Tally, second String) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PB'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function id(text String) returns String
	return text
end 'id'

function main() returns ExitCode
	var label = "original"
	let p = PB.build(put(label), second: id(label))
	keep(p)
	let k = put(label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: return-statement-after -->
A return whose operands write and then read the local.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function early() returns Tally
	var label = "original"

	return two2(put(label), text: label)
end 'early'

function main() returns ExitCode

	let r = early()
	keep(r)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: return-statement-before -->
A return whose operands read and then write the local.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function early() returns Tally
	var label = "original"

	return two(label, k: put(label))
end 'early'

function main() returns ExitCode

	let r = early()
	keep(r)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:20:27: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: if-condition-after -->
An if condition that writes and then reads the local.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function main() returns ExitCode
	var label = "original"
	if two2(put(label), text: label) > 100 'cond'
		print("big\n")
	end 'cond'
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: if-condition-before -->
An if condition that reads and then writes the local.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function main() returns ExitCode
	var label = "original"
	if two(label, k: put(label)) > 100 'cond'
		print("big\n")
	end 'cond'
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:16:23: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: while-condition-after -->
A while condition that writes and then reads the local.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function main() returns ExitCode
	var label = "original"
	while two2(put(label), text: label) > 100 'cond'
		print("big\n")
	end 'cond'
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: if-body-reads-after-the-condition -->
The write is in the condition, the read in the body: a different statement.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var label = "original"
	if put(label) > 0 'cond'
		print("{label}\n")
	end 'cond'
	print("ok\n")
	return 0
end 'main'
```
```stdout
replaced
ok
```
```exitcode
0
```

<!-- test: async-split-await -->
<!-- unsupported-targets: wasm32-wasi -->
An `async` started in one statement, the local read beside the await of another statement.

```maxon
typealias Tally = int(0 to u64.max)

function keep(_ T) uses T
end 'keep'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function slow(text mutable String) returns Tally
	sleep(1)
	text = "replaced"

	return 0
end 'slow'

function main() returns ExitCode
	var label = "original"
	let promise = async slow(label)
	let k = two2(await promise, text: label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: async-start-reads-before-the-wait -->
<!-- unsupported-targets: wasm32-wasi -->
An `async` whose later operand reads the local.

```maxon
typealias Tally = int(0 to u64.max)

function keep(_ T) uses T
end 'keep'

function wrap(k Tally) returns Tally
	return k
end 'wrap'

function two3(text String, k Tally) returns Tally
	sleep(1)

	return k + (text.byteLength() as Tally)
end 'two3'

function main() returns ExitCode
	var label = "original"
	let promise = async two3(label, k: wrap(label.byteLength() as Tally))
	let k = await promise
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: async-holds-the-cell-by-reference -->
<!-- unsupported-targets: wasm32-wasi -->
The `async` call hands the local by reference, and a reading operand precedes it in the statement: the coroutine's write is not ordered against any read of the statement.

```maxon
typealias Tally = int(0 to u64.max)

function keep(_ T) uses T
end 'keep'

function slow2(k Tally, text mutable String) returns Tally
	sleep(1)
	text = "replaced"

	return k
end 'slow2'

function main() returns ExitCode
	var label = "original"
	let promise = async slow2(label.byteLength() as Tally, text: label)
	let k = await promise
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:16:63: argument 'label' is read by another operand of this statement and passed by reference to 'slow2' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: nested-outer-call-with-an-alias-argument -->
The writing call's own later operand returns the local's value.

```maxon
typealias Tally = int(0 to u64.max)

function putK(text mutable String, k Tally) returns Tally
	text = "replaced"

	return k
end 'putK'

function rd(text String) returns Tally
	return text.byteLength() as Tally
end 'rd'

function keep(_ T) uses T
end 'keep'

function id(text String) returns String
	return text
end 'id'

function main() returns ExitCode
	var label = "original"
	let k = putK(label, k: rd(id(label)))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:23:15: argument 'label' is read by another operand of this statement and passed by reference to 'putK' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: await-of-an-async-then-a-read -->
<!-- unsupported-targets: wasm32-wasi -->
An `async` started and awaited inside one operand, the local read by the next operand of the same statement.

```maxon
typealias Tally = int(0 to u64.max)

function keep(_ T) uses T
end 'keep'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function slow(text mutable String) returns Tally
	sleep(1)
	text = "replaced"

	return 0
end 'slow'

function main() returns ExitCode
	var label = "original"
	let k = two2(await async slow(label), text: label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:20:32: argument 'label' is read by another operand of this statement and passed by reference to 'slow' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: closure-capture-before -->
A closure literal capturing the local runs before the write.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyFn = function() returns Tally

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function call2(f TallyFn, k Tally) returns Tally
	return f() + k
end 'call2'

function main() returns ExitCode
	var label = "original"
	let k = call2(function() gives label.byteLength() as Tally, k: put(label))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:20:69: use of moved value 'label': its ownership moved to the closure that captures it
```

<!-- test: closure-capture-after -->
A closure literal capturing the local runs after the write.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyFn = function() returns Tally

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function call3(k Tally, f TallyFn) returns Tally
	return f() + k
end 'call3'

function main() returns ExitCode
	var label = "original"
	let k = call3(put(label), f: function() gives label.byteLength() as Tally)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: two-parameters-two-locals -->
One call rewrites two locals and one of them is read after it.

```maxon
typealias Tally = int(0 to u64.max)

type PB
	export var first as Tally
	export var second as String

	static function build(first Tally, second String) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PB'

function put2(a mutable String, b mutable String) returns Tally
	a = "replaced"
	b = "replaced"

	return 1
end 'put2'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	var other = "other"
	let p = PB.build(put2(label, b: other), second: label)
	keep(p)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: two-parameters-one-local -->
One call rewrites the same local through both parameters.

```maxon
typealias Tally = int(0 to u64.max)

type PB
	export var first as Tally
	export var second as String

	static function build(first Tally, second String) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PB'

function put2(a mutable String, b mutable String) returns Tally
	a = "replaced"
	b = "replaced"

	return 1
end 'put2'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let p = PB.build(put2(label, b: label), second: label)
	keep(p)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:25:34: argument 'label' is read by another operand of this statement and passed by reference to 'put2' (parameter 'b') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `var` in an earlier statement (`.clone()` a record, array or string), pass the copy for that operand and store it back into 'label' after the call
```

<!-- test: by-reference-call-returns-the-old-value -->
The writing call returns the old value; the local read after it holds the new one.

```maxon
typealias Tally = int(0 to u64.max)

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function putId(text mutable String) returns String
	let old = text.clone()
	text = "replaced"

	return old
end 'putId'

function keep(_ T) uses T
end 'keep'

function wrap(k Tally) returns Tally
	return k
end 'wrap'

function main() returns ExitCode
	var label = "original"
	let p = PA.build(putId(label), second: wrap(label.byteLength() as Tally))
	keep(p)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: push-after-the-write-twice -->
A push after the write, then another push of the local's length.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function sum(a Tally, b Tally) returns Tally
	return a + b
end 'sum'

function main() returns ExitCode
	var label = "original"
	var items = TallyArray.create(0)
	items.push(sum(put(label), b: label.byteLength() as Tally))
	items.push(label.byteLength() as Tally)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: assignment-target-after-the-write -->
The assignment target is the local being rewritten by the right-hand side.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	h.g = "x" if put(h.g) > 0 else "y"
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: function-value-before -->
A function value of `put` writes through its by-reference parameter, and an earlier operand reads the local.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function main() returns ExitCode
	var label = "original"
	let step = put
	let k = two(label, k: step(label))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:20:29: argument 'label' is read by another operand of this statement and passed by reference to 'step' — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: function-value-after -->
The same indirect call, the local read after it.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function main() returns ExitCode
	var label = "original"
	let step = put
	let k = two2(step(label), text: label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: function-value-ternary-condition -->
A ternary condition reads the local before the indirect call writes it.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let step = put
	let k = step(label) if label.byteLength() > 3 else 0
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:16:15: argument 'label' is read by another operand of this statement and passed by reference to 'step' — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: function-value-ternary-after -->
The indirect call is the condition and the arm reads after it.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let step = put
	let k = label.byteLength() as Tally if step(label) > 0 else 0
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: function-value-later-operand -->
A later operand reads the local after the indirect call.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function sum(a Tally, b Tally) returns Tally
	return a + b
end 'sum'

function main() returns ExitCode
	var label = "original"
	let step = put
	let k = sum(step(label), b: label.byteLength() as Tally)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: function-typed-parameter-before -->
A function-typed parameter called with a by-reference variable beside an earlier read.

```maxon
typealias Tally = int(0 to u64.max)
typealias PutFn = function(mutable String) returns Tally

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function drive(step PutFn, text mutable String) returns Tally
	return two(text, k: step(text))
end 'drive'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function main() returns ExitCode
	var label = "original"
	let k = drive(put, text: label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:12:27: argument 'text' is read by another operand of this statement and passed by reference to 'step' — a hold cannot protect a value its by-reference cell overwrites. Copy 'text' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: function-typed-parameter-after -->
The same call with the read after it.

```maxon
typealias Tally = int(0 to u64.max)
typealias PutFn = function(mutable String) returns Tally

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function driveAfter(step PutFn, text mutable String) returns Tally
	return two2(step(text), text: text)
end 'driveAfter'

function keep(_ T) uses T
end 'keep'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function main() returns ExitCode
	var label = "original"
	let k = driveAfter(put, text: label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: for-binding-field-cell-before -->
A field cell of a `for` binding is read beside the by-reference call.

```maxon
typealias Tally = int(0 to u64.max)
typealias HolderArray = Array with Holder

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var items = HolderArray.create(0)
	items.push(Holder.create())
	for e in items 'each'
		keep(PA.build(e.f, second: put(e.f)))
	end 'each'
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:44:34: argument 'e.f' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'e.f' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: for-binding-field-cell-after -->
The twin with the read after the write.

```maxon
typealias Tally = int(0 to u64.max)
typealias HolderArray = Array with Holder

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'

	function mix(text String, k Tally) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix'

	function mix2(k Tally, text String) returns Tally
		return (text.byteLength() as Tally) + k
	end 'mix2'
end 'Holder'

type PB
	export var first as Tally
	export var second as String

	static function build(first Tally, second String) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PB'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var items = HolderArray.create(0)
	items.push(Holder.create())
	for e in items 'each'
		keep(PB.build(put(e.f), second: e.f))
	end 'each'
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:44:17: cannot pass 'e', which is read-only (a `for` loop variable), to a function that writes parameter 'text' (in main); pass a clone()
```

<!-- test: loop-body-alias-after-the-write -->
The write and the later read inside a loop body.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function sum(a Tally, b Tally) returns Tally
	return a + b
end 'sum'

function main() returns ExitCode
	var label = "original"
	var held = 0 as Tally
	var i = 0 as Tally
	while i < 1 'again'
		held = sum(put(label), b: label.byteLength() as Tally)
		i = i + 1
	end 'again'
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: view-alias-sibling-field-before -->
A `view` of the record a field cell lends is another name for that record, and the hold on `h2.f` keeps the old string alive across the write of `h.f`: `p.first` is the string `h.f` held before the call.
<!-- MmTrace -->
```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	view h2 = h
	let p = PA.build(h2.f, second: put(h.f))
	print("{p.first} {h.f}\n")
	return 0
end 'main'
```
```stdout
original replaced
```
```exitcode
0
```
```mm-trace
mm_alloc Holder #1 size=16
mm_alloc StringRecord #2 size=65
mm_alloc StringRecord #3 size=62
mm_incref Holder #1 rc=2
mm_incref StringRecord #2 rc=2
mm_alloc StringRecord #4 size=65
mm_decref StringRecord #2 rc=1
mm_alloc StringRecord #5 size=65
mm_incref StringRecord #5 rc=2
mm_alloc StringRecord #6 size=65
mm_decref StringRecord #5 rc=1
mm_decref StringRecord #5 rc=0
mm_free StringRecord #5
mm_decref StringRecord #2 rc=0
mm_free StringRecord #2
mm_alloc StringRecord #7 size=75
mm_decref StringRecord #7 rc=0
mm_free StringRecord #7
mm_decref StringRecord #6 rc=0
mm_free StringRecord #6
mm_decref Holder #1 rc=1
mm_decref Holder #1 rc=0
mm_decref StringRecord #4 rc=0
mm_free StringRecord #4
mm_decref StringRecord #3 rc=0
mm_free StringRecord #3
mm_free Holder #1
```

<!-- test: view-alias-sibling-field-after -->
A `view` of the record is read after the write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'
end 'Holder'

type PB
	export var first as Tally
	export var second as String

	static function build(first Tally, second String) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PB'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	view h2 = h
	let p = PB.build(put(h.f), second: h2.g)
	print("{p.second} {h.f}\n")
	return 0
end 'main'
```
```stdout
other replaced
```
```exitcode
0
```

<!-- test: view-alias-source-reassigned-in-a-loop -->
The source of a `view` is reassigned later in the loop. `h2` reads the record `h` named at the view: the first lap reads it before the write of `h.f`, the second lap reads what that write left.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	view h2 = h
	var i = 0 as Tally

	while i < 2 'loop'
		let p = PA.build(h2.f, second: put(h.f))
		print("{p.first}\n")
		h = Holder.create()
		i = i + 1
	end 'loop'

	return 0
end 'main'
```
```stdout
original
replaced
```
```exitcode
0
```

<!-- test: view-alias-source-reassigned-before-the-statement -->
`h2` names the record `h` held when the view was taken, and `h` was rebound to another record, so `put(h.f)` writes a record `h2.g` does not read.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "a", g: "b"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	view h2 = h
	h = Holder.create()
	let p = PA.build(h2.g, second: put(h.f))
	print("{p.first} {h.f}\n")
	return 0
end 'main'
```
```stdout
b replaced
```
```exitcode
0
```

<!-- test: view-alias-source-reassigned-on-one-path -->
After the `if`, `h` still holds the record `h2` views, so the hold on `h2.f` keeps the old string alive across the write of `h.f`.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "a", g: "b"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	view h2 = h
	let n = "x".count()

	if n > 5 'rebind'
		h = Holder.create()
	end 'rebind'

	let p = PA.build(h2.f, second: put(h.f))
	print("{p.first} {h.f}\n")
	return 0
end 'main'
```
```stdout
a replaced
```
```exitcode
0
```

<!-- test: view-alias-source-rebound-to-the-view -->
The source of a `view` is rebound to the view itself, so `h` holds a record read through a `view` and the by-reference write of `h.f` is refused as a write through a view, not as an overlap.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "a", g: "b"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	view h2 = h
	h = h2
	let p = PA.build(h2.f, second: put(h.f))
	print("{p.first} {h.f}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:31:2: cannot assign the read-only 'h2' to mutable binding 'h'; use 'let' instead of 'var', or use clone()
error E3159: <fragment>:32:33: cannot pass 'h2', which is read-only (a `view`), to a function that writes parameter 'text' (in main); pass a clone()
```

<!-- test: view-alias-source-rebound-to-another-view -->
The source of a `view` is rebound to a second view of the same record, so the by-reference write of `h.f` is refused as a write through a view, not as an overlap.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "a", g: "b"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	view h2 = h
	view h3 = h
	h = h3
	let p = PA.build(h2.f, second: put(h.f))
	print("{p.first} {h.f}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:32:2: cannot assign the read-only 'h3' to mutable binding 'h'; use 'let' instead of 'var', or use clone()
error E3159: <fragment>:33:33: cannot pass 'h3', which is read-only (a `view`), to a function that writes parameter 'text' (in main); pass a clone()
```

<!-- test: view-alias-source-rebound-to-a-conditional-of-the-view -->
The source of a `view` is rebound to a conditional whose taken arm is the view, so the by-reference write of `h.f` is refused as a write through a view, not as an overlap.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "a", g: "b"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	view h2 = h
	let n = "x".count()
	h = h2 if n < 5 else Holder.create()
	let p = PA.build(h2.f, second: put(h.f))
	print("{p.first} {h.f}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:33:33: cannot pass 'h2', which is read-only (a `view`), to a function that writes parameter 'text' (in main); pass a clone()
```

<!-- test: view-alias-source-rebound-to-a-call-that-returns-the-view -->
The source of a `view` is rebound to a call result that returns the view, so the by-reference write of `h.f` is refused as a write through a view, not as an overlap.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "a", g: "b"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function same(holder Holder) returns Holder
	return holder
end 'same'

function main() returns ExitCode
	var h = Holder.create()
	view h2 = h
	h = same(h2)
	let p = PA.build(h2.f, second: put(h.f))
	print("{p.first} {h.f}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:36:33: cannot pass 'h2', which is read-only (a `view`), to a function that writes parameter 'text' (in main); pass a clone()
```

<!-- test: view-alias-source-rebound-through-a-call-on-the-source -->
The source of a `view` is rebound to a call result that returns the very record `h` held, so `h` still names the record `h2` names.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "a", g: "b"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function same(holder Holder) returns Holder
	return holder
end 'same'

function main() returns ExitCode
	var h = Holder.create()
	view h2 = h
	h = same(h)
	let p = PA.build(h2.f, second: put(h.f))
	print("{p.first} {h.f}\n")
	return 0
end 'main'
```
```stdout
a replaced
```
```exitcode
0
```

<!-- test: view-alias-source-rebound-to-a-conditional-of-the-source -->
The source of a `view` is rebound to a conditional whose taken arm is the record `h` held, so `h` still names the record `h2` names.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "a", g: "b"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	view h2 = h
	let n = "x".count()
	h = h if n < 5 else Holder.create()
	let p = PA.build(h2.f, second: put(h.f))
	print("{p.first} {h.f}\n")
	return 0
end 'main'
```
```stdout
a replaced
```
```exitcode
0
```

<!-- test: view-alias-source-reassigned-on-both-paths -->
After the `if`, `h` holds one of two records both made after the view, so `h2` names neither and `put(h.f)` writes a record `h2.g` does not read.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "a", g: "b"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	view h2 = h
	h = Holder.create()
	let n = "x".count()

	if n > 5 'again'
		h = Holder.create()
	end 'again'

	let p = PA.build(h2.g, second: put(h.f))
	print("{p.first} {h.f}\n")
	return 0
end 'main'
```
```stdout
b replaced
```
```exitcode
0
```

<!-- test: view-alias-source-moved-into-a-var-then-rebound -->
A `var` that took the record by move names the record `h2` views, and `h` was rebound to another record. The cell is `o.f`; the hold on `h2.f` keeps the old string alive across its write.
<!-- MmTrace -->
```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "a", g: "b"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	view h2 = h
	var o = h
	h = Holder.create()
	let p = PA.build(h2.f, second: put(o.f))
	print("{p.first} {o.f} {h.f}\n")
	return 0
end 'main'
```
```stdout
a replaced a
```
```exitcode
0
```
```mm-trace
mm_alloc Holder #1 size=16
mm_alloc StringRecord #2 size=58
mm_alloc StringRecord #3 size=58
mm_incref Holder #1 rc=2
mm_alloc Holder #4 size=16
mm_alloc StringRecord #5 size=58
mm_alloc StringRecord #6 size=58
mm_incref StringRecord #2 rc=2
mm_alloc StringRecord #7 size=65
mm_decref StringRecord #2 rc=1
mm_alloc StringRecord #8 size=58
mm_incref StringRecord #8 rc=2
mm_alloc StringRecord #9 size=58
mm_decref StringRecord #8 rc=1
mm_decref StringRecord #8 rc=0
mm_free StringRecord #8
mm_decref StringRecord #2 rc=0
mm_free StringRecord #2
mm_alloc StringRecord #10 size=70
mm_decref StringRecord #10 rc=0
mm_free StringRecord #10
mm_decref StringRecord #9 rc=0
mm_free StringRecord #9
mm_decref Holder #1 rc=1
mm_decref Holder #1 rc=0
mm_decref StringRecord #7 rc=0
mm_free StringRecord #7
mm_decref StringRecord #3 rc=0
mm_free StringRecord #3
mm_free Holder #1
mm_decref Holder #4 rc=0
mm_decref StringRecord #5 rc=0
mm_free StringRecord #5
mm_decref StringRecord #6 rc=0
mm_free StringRecord #6
mm_free Holder #4
```

<!-- test: view-alias-moved-var-viewed-again -->
A second `view` of the `var` that took the record by move reads the record before the write of `o.f`.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "a", g: "b"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	var o = h
	view h3 = o
	h = Holder.create()
	let p = PA.build(h3.f, second: put(o.f))
	print("{p.first} {o.f} {h.f}\n")
	return 0
end 'main'
```
```stdout
a replaced a
```
```exitcode
0
```

<!-- test: view-alias-source-moved-to-a-var-and-rebound-nested-path -->
The cell is a nested field path of the `var` that took the record by move, and `w2.inner.f` is read before the write of `o.inner.f`.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "a", g: "b"}
	end 'create'
end 'Holder'

type Wrapper
	export var inner as Holder

	static function create() returns Self
		return Self{inner: Holder.create()}
	end 'create'
end 'Wrapper'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var w = Wrapper.create()
	view w2 = w
	var o = w
	w = Wrapper.create()
	let p = PA.build(w2.inner.f, second: put(o.inner.f))
	print("{p.first} {o.inner.f} {w.inner.f}\n")
	return 0
end 'main'
```
```stdout
a replaced a
```
```exitcode
0
```

<!-- test: view-alias-source-rebound-to-a-factory-that-returns-a-local -->
The factory builds a local record and returns it, so `h` names a record `h2` does not, and `put(h.f)` writes a record `h2.g` does not read.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		var built = Self{f: "x", g: "y"}
		built.f = "a"
		built.g = "b"
		return built
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	view h2 = h
	h = Holder.create()
	let p = PA.build(h2.g, second: put(h.f))
	print("{p.first} {h.f}\n")
	return 0
end 'main'
```
```stdout
b replaced
```
```exitcode
0
```

<!-- test: view-alias-source-rebound-to-an-older-var -->
`h` takes the record of a `var` made before the view, which is not the record `h2` views, so `put(h.f)` writes a record `h2.g` does not read.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "a", g: "b"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	var older = Holder.create()
	view h2 = h
	h = older
	let p = PA.build(h2.g, second: put(h.f))
	print("{p.first} {h.f}\n")
	return 0
end 'main'
```
```stdout
b replaced
```
```exitcode
0
```

<!-- test: view-alias-source-rebound-to-fresh-records-in-a-loop -->
`h` holds a fresh record at the loop head and after every lap, none of which `h2` views, so `put(h.f)` writes a record `h2.g` does not read.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "a", g: "b"}
	end 'create'
end 'Holder'

type PA
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'PA'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	view h2 = h
	h = Holder.create()
	var i = 0 as Tally

	while i < 2 'loop'
		let p = PA.build(h2.g, second: put(h.f))
		print("{p.first} {h.f}\n")
		h = Holder.create()
		i = i + 1
	end 'loop'

	return 0
end 'main'
```
```stdout
b replaced
b replaced
```
```exitcode
0
```

<!-- test: closure-parameter-is-the-root-before -->
The by-reference cell is a closure's own parameter, read by an earlier operand of the same call inside the closure body.

```maxon
typealias Tally = int(0 to u64.max)
typealias StepFn = function(mutable String) returns Tally

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function apply(step StepFn, text mutable String) returns Tally
	return step(text)
end 'apply'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let k = apply(function(s mutable String) gives two(s, k: put(s)), text: label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:24:63: argument 's' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: closure-parameter-is-the-root-with-an-earlier-name -->
A name the file spelled earlier is not an operand of the closure body's own statement.

```maxon
typealias Tally = int(0 to u64.max)
typealias StepFn = function(mutable String) returns Tally

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function measure(s String) returns Tally
	return s.byteLength() as Tally
end 'measure'

function apply(step StepFn, text mutable String) returns Tally
	return step(text)
end 'apply'

function main() returns ExitCode
	var label = "original"
	let k = apply(function(s mutable String) gives put(s), text: label)
	print("{k} {label} {measure(label)}\n")
	return 0
end 'main'
```
```stdout
1 replaced 8
```
```exitcode
0
```

<!-- test: keyword-named-root-before -->
The by-reference cell is a parameter named by a keyword, read by an earlier operand.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function drive(type mutable String) returns Tally
	return two(type, k: put(type))
end 'drive'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let k = drive(label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:15:26: argument 'type' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'type' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: map-literal-key-reads-the-root -->
A map literal's key is an operand that reads the local before the call rewrites it.

```maxon
typealias Tally = int(0 to u64.max)
typealias CountMap = Map with (Tally, Tally)

function bump(n mutable Tally) returns Tally
	n = n + 1

	return n
end 'bump'

function tally(m CountMap, k Tally) returns Tally
	return (m.count() as Tally) + k
end 'tally'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var n = 0 as Tally
	let k = tally([n: 1 as Tally], k: bump(n))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:20:41: argument 'n' is read by another operand of this statement and passed by reference to 'bump' (parameter 'n') — a hold cannot protect a value its by-reference cell overwrites. Copy 'n' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: for-binder-named-like-the-root -->
A loop variable spelled like the local is a different binding, so the header's one read of the local is the cell.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function bump(n mutable Tally) returns Tally
	n = n + 1

	return n
end 'bump'

function spread(k Tally) returns TallyArray
	return [k, k]
end 'spread'

function main() returns ExitCode
	var x = 0 as Tally
	var total = 0 as Tally

	for x in spread(bump(x)) 'each'
		total = total + x
	end 'each'

	print("{total}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: match-payload-named-like-the-root -->
A payload bound by an earlier arm is a different binding from the local a later arm passes by reference.

```maxon
typealias Tally = int(0 to u64.max)

union Slot
	some(value Tally)
	none
end 'Slot'

function bump(n mutable Tally) returns Tally
	n = n + 1

	return n
end 'bump'

function main() returns ExitCode
	var label = 0 as Tally
	let slot = Slot.some(5)

	let r = match slot 'm'
		some(label) gives label
		none gives bump(label)
	end 'm'

	print("{r}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: closure-with-commas-in-brackets -->
A closure whose own parameter is spelled like the local, with commas inside a bracketed body, reads nothing of the local.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally
typealias SpreadFn = function(Tally) returns TallyArray

function bump(n mutable Tally) returns Tally
	n = n + 1

	return n
end 'bump'

function drive(spread SpreadFn, k Tally) returns Tally
	return k + (spread(k).count() as Tally)
end 'drive'

function main() returns ExitCode
	var x = 0 as Tally
	let r = drive(function(x Tally) gives [x, x], k: bump(x))
	print("{r}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: self-field-cell-before -->
A bare writable field of `self` is handed by reference and read by an earlier operand. The root is `self`, so the read runs before the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

type Holder
	export var label as String

	static function create() returns Self
		return Self{label: "original"}
	end 'create'

	function run() returns Tally
		return two(label, k: put(label))
	end 'run'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	keep(h.run())
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:25:28: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: self-field-cell-after -->
A bare writable field of `self` is handed by reference and read by a later operand. The read runs after the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

type Holder
	export var label as String

	static function create() returns Self
		return Self{label: "original"}
	end 'create'

	function run() returns Tally
		return two2(put(label), text: label)
	end 'run'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	keep(h.run())
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: self-field-explicit-cell-before -->
The field of `self` spelled `self.label` is the same root as the bare name.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

type Holder
	export var label as String

	static function create() returns Self
		return Self{label: "original"}
	end 'create'

	function run() returns Tally
		return two(self.label, k: put(self.label))
	end 'run'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	keep(h.run())
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:25:33: argument 'self.label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: self-field-bare-then-explicit-cell-before -->
The bare name and `self.label` name one root, so a read spelled one way refuses a cell spelled the other.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

type Holder
	export var label as String

	static function create() returns Self
		return Self{label: "original"}
	end 'create'

	function run() returns Tally
		return two(self.label, k: put(label))
	end 'run'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	keep(h.run())
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:25:33: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: self-field-path-cell-before -->
A field of a record held in a field of `self` is handed by reference and read by an earlier operand.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

type Inner
	export var f as String

	static function create() returns Self
		return Self{f: "original"}
	end 'create'
end 'Inner'

type Holder
	export var inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create()}
	end 'create'

	function run() returns Tally
		return two(inner.f, k: put(inner.f))
	end 'run'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	keep(h.run())
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:33:30: argument 'inner.f' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'inner.f' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: self-field-explicit-path-cell-before -->
The same field path spelled through `self`.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

type Inner
	export var f as String

	static function create() returns Self
		return Self{f: "original"}
	end 'create'
end 'Inner'

type Holder
	export var inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create()}
	end 'create'

	function run() returns Tally
		return two(self.inner.f, k: put(self.inner.f))
	end 'run'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	keep(h.run())
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:33:35: argument 'self.inner.f' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'inner.f' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: self-field-sibling-field-before -->
Another field of `self` read by an earlier operand is a read of the same root, as a sibling field of a local record is.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

type Holder
	export var label as String
	export var other as String

	static function create() returns Self
		return Self{label: "original", other: "other"}
	end 'create'

	function run() returns Tally
		return two(other, k: put(label))
	end 'run'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	keep(h.run())
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:26:28: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'other' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: self-field-method-receiver-before -->
A method called on a field of `self` reads the root before a later operand's write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function sum(a Tally, b Tally) returns Tally
	return a + b
end 'sum'

type Holder
	export var label as String

	static function create() returns Self
		return Self{label: "original"}
	end 'create'

	function run() returns Tally
		return sum(label.byteLength() as Tally, b: put(label))
	end 'run'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	keep(h.run())
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:25:50: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: local-var-cell-before -->
A local `var` is handed by reference and read by an earlier operand.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function main() returns ExitCode
	var label = "original"
	let k = two(label, k: put(label))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:19:28: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: local-var-cell-after -->
A local `var` is handed by reference and read by a later operand.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function main() returns ExitCode
	var label = "original"
	let k = two2(put(label), text: label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: local-var-field-path-cell-before -->
A field of a record held in a local `var` is handed by reference and read by an earlier operand.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

type Holder
	export var f as String

	static function create() returns Self
		return Self{f: "original"}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	let k = two(holder.f, k: put(holder.f))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:27:31: argument 'holder.f' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'holder.f' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: local-var-method-receiver-before -->
A method called on a local `var` reads it before a later operand's write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function sum(a Tally, b Tally) returns Tally
	return a + b
end 'sum'

function main() returns ExitCode
	var label = "original"
	let k = sum(label.byteLength() as Tally, b: put(label))
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:19:50: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: function-value-two-cells-one-local -->
One local handed to two `mutable` positions of a call through a function value overlaps itself: nothing names the callee's body, so the ordering rule owns it.

```maxon
function swapTexts(a mutable String, b mutable String)
	a = "left"
	b = "right"
end 'swapTexts'

function main() returns ExitCode
	var s = "start"
	let f = swapTexts
	f(s, s)
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:10:7: argument 's' is read by another operand of this statement and passed by reference to 'f' — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `var` in an earlier statement (`.clone()` a record, array or string), pass the copy for that operand and store it back into 's' after the call
```

<!-- test: witness-dispatch-two-cells-one-local -->
One local handed to two `mutable` positions of an interface requirement overlaps itself: the callee is not known here, so the ordering rule owns it.

```maxon
interface Swapper
	function swap(a mutable String, b mutable String)
end 'Swapper'

type Plain implements Swapper
	export var tag = "plain"

	static function create() returns Self
		return Self{}
	end 'create'

	function swap(a mutable String, b mutable String)
		a = "left"
		b = "right"
	end 'swap'
end 'Plain'

function drive(w Swapper)
	var s = "start"
	w.swap(s, b: s)
	print("{s}\n")
end 'drive'

function main() returns ExitCode
	let p = Plain.create()
	drive(p)
	print("{p.tag}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:21:15: argument 's' is read by another operand of this statement and passed by reference to 'swap' — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `var` in an earlier statement (`.clone()` a record, array or string), pass the copy for that operand and store it back into 's' after the call
```

<!-- test: leading-sibling-method-call-before -->
A later argument that leads with the local but is not the whole local is read before the call's write; only a whole local beside a cell is the argument-overlap check's.

```maxon
typealias Tally = int(0 to u64.max)

function fill(text mutable String, n Tally) returns Tally
	text = "replaced"

	return n
end 'fill'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let k = fill(label, n: label.byteLength() as Tally)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:15:15: argument 'label' is read by another operand of this statement and passed by reference to 'fill' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: call-receiver-write-then-argument-read -->
The compiler runs a receiver call before the arguments of the method call, and the program is refused anyway: the order the rule is stated in evaluates a method call's arguments before its receiver, so the receiver call's write follows the argument's read, which no hold can protect.

```maxon
typealias Tally = int(0 to u64.max)

type Meter
	export var n as Tally

	static function create() returns Self
		return Self{n: 3}
	end 'create'

	function add(text String) returns Tally
		return self.n + (text.byteLength() as Tally)
	end 'add'
end 'Meter'

function putMeter(text mutable String) returns Meter
	text = "replaced"

	return Meter.create()
end 'putMeter'

function keep(_ T) uses T
end 'keep'

function main() returns ExitCode
	var label = "original"
	let k = putMeter(label).add(label)
	keep(k)
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:27:19: argument 'label' is read by another operand of this statement and passed by reference to 'putMeter' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: call-receiver-write-then-other-argument -->
The same receiver call beside an argument that does not read the written local.

```maxon
typealias Tally = int(0 to u64.max)

type Meter
	export var n as Tally

	static function create() returns Self
		return Self{n: 3}
	end 'create'

	function add(text String) returns Tally
		return self.n + (text.byteLength() as Tally)
	end 'add'
end 'Meter'

function putMeter(text mutable String) returns Meter
	text = "replaced"

	return Meter.create()
end 'putMeter'

function main() returns ExitCode
	var label = "original"
	let other = "abc"
	let k = putMeter(label).add(other)
	print("{k} {label}\n")
	return 0
end 'main'
```
```stdout
6 replaced
```
```exitcode
0
```

<!-- test: else-if-try-after-the-first-condition -->
A catching `else if` condition begins after the first condition has finished, so it does not overlap the first condition's reads.

```maxon
enum Fault implements Error
	broken
end 'Fault'

function probe(text String) throws Fault
	if text.byteLength() > 3 'long'
		throw Fault.broken
	end 'long'
end 'probe'

function fill(text mutable String) throws Fault
	if text.isEmpty() 'empty'
		throw Fault.broken
	end 'empty'

	text = "replaced"
end 'fill'

function main() returns ExitCode
	var s = "original"

	if try probe(s) 'first'
		print("first\n")
	end 'first' else if try fill(s) 'second'
		print("second {s}\n")
	end 'second'

	return 0
end 'main'
```
```stdout
second replaced
```
```exitcode
0
```

<!-- test: else-if-try-after-a-plain-condition -->
A plain condition's reads are finished before a catching `else if` begins.

```maxon
enum Fault implements Error
	broken
end 'Fault'

function fill(text mutable String) throws Fault
	if text.isEmpty() 'empty'
		throw Fault.broken
	end 'empty'

	text = "replaced"
end 'fill'

function main() returns ExitCode
	var s = "original"

	if s.byteLength() < 3 'short'
		print("short\n")
	end 'short' else if try fill(s) 'second'
		print("second {s}\n")
	end 'second'

	return 0
end 'main'
```
```stdout
second replaced
```
```exitcode
0
```

<!-- test: panic-handler-reads-in-its-own-frame -->
A `panic` handler diverges, so the read of the local in its message does not precede a later operand's write.

```maxon
typealias Tally = int(0 to u64.max)

enum Fault implements Error
	broken
end 'Fault'

function g(n Tally) returns Tally throws Fault
	if n > 5 'big'
		throw Fault.broken
	end 'big'

	return n
end 'g'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function combine(a Tally, b Tally) returns Tally
	return a + b
end 'combine'

function main() returns ExitCode
	var x = "original"
	let k = combine(try g(2) otherwise panic("bad {x}"), b: put(x))
	print("{k} {x}\n")
	return 0
end 'main'
```
```stdout
3 replaced
```
```exitcode
0
```

<!-- test: witness-candidates-disagree-on-a-cell -->
A position that any requirement of the called name takes by reference is lent, though the arity that picks the requirement is known only after the arguments.

```maxon
interface Base
	function swap(a String, b String, c String)
end 'Base'

interface Sub extends Base
	function swap(a mutable String, b mutable String)
end 'Sub'

function drive(w Sub)
	var s = "start"
	w.swap(s, b: s)
	print("{s}\n")
end 'drive'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:12:15: argument 's' is read by another operand of this statement and passed by reference to 'swap' — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `var` in an earlier statement (`.clone()` a record, array or string), pass the copy for that operand and store it back into 's' after the call
```

<!-- test: self-field-receiver-beside-a-field-cell -->
A method called on one field of `self` reads the root before the call that takes another field of `self` by reference writes it.

```maxon
typealias Tally = int(0 to u64.max)

type Gauge
	export var n as Tally

	static function create() returns Self
		return Self{n: 3}
	end 'create'

	function bump(by mutable Tally)
		by = by + self.n
	end 'bump'
end 'Gauge'

type Holder
	export var gauge as Gauge
	export var level as Tally

	static function create() returns Self
		return Self{gauge: Gauge.create(), level: 4}
	end 'create'

	function run()
		gauge.bump(level)
	end 'run'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	h.run()
	print("{h.level}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:25:14: argument 'level' is read by another operand of this statement and passed by reference to 'bump' (parameter 'by') — a hold cannot protect a value its by-reference cell overwrites. Copy 'gauge' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: self-field-receiver-parameter-beside-a-field-cell -->
The same call through a receiver that is a parameter reads nothing of `self` before the write.

```maxon
typealias Tally = int(0 to u64.max)

type Gauge
	export var n as Tally

	static function create() returns Self
		return Self{n: 3}
	end 'create'

	function bump(by mutable Tally)
		by = by + self.n
	end 'bump'
end 'Gauge'

type Holder
	export var level as Tally

	static function create() returns Self
		return Self{level: 4}
	end 'create'

	function run(gauge Gauge)
		gauge.bump(level)
	end 'run'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	h.run(Gauge.create())
	print("{h.level}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: record-method-result-beside-a-field-cell-before -->
A later argument that is a method call on the record whose field the call hands by reference reads the record before the write, whatever the call returns.

```maxon
typealias Coord = int(i64.min to i64.max)

type Holder
	export var n as Coord

	static function create(n Coord) returns Holder
		return Holder{n: n}
	end 'create'

	function copy() returns Holder
		return Holder.create(self.n)
	end 'copy'
end 'Holder'

function bump(n mutable Coord, h Holder) returns Coord
	n = n + 1
	return h.n
end 'bump'

function main() returns ExitCode
	var h = Holder.create(5)
	let seen = bump(h.n, h: h.copy())
	print("{seen} {h.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:23:18: argument 'h.n' is read by another operand of this statement and passed by reference to 'bump' (parameter 'n') — a hold cannot protect a value its by-reference cell overwrites. Copy 'h' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: captured-record-field-cell-before -->
A field of a captured record is handed by reference inside a closure body and read by an earlier operand of the same statement.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var s as String

	static function create() returns Self
		return Self{s: "original"}
	end 'create'
end 'Box'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two(text String, k Tally) returns Tally
	return (text.byteLength() as Tally) + k
end 'two'

function main() returns ExitCode
	var c = Box.create()
	let f = function() gives two(c.s, k: put(c.s))
	keep(f())
	print("ok\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:27:43: argument 'c.s' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'c.s' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: captured-record-field-cell-after -->
A field of a captured record is handed by reference inside a closure body and read by a later operand.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var s as String

	static function create() returns Self
		return Self{s: "original"}
	end 'create'
end 'Box'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function keep(_ T) uses T
end 'keep'

function two2(k Tally, text String) returns Tally
	return (text.byteLength() as Tally) + k
end 'two2'

function main() returns ExitCode
	var c = Box.create()
	let f = function() gives two2(put(c.s), text: c.s)
	keep(f())
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: same-call-sibling-of-a-string-cell-the-callee-appends-to -->
A `mutable` value parameter the callee only appends to is still a by-reference cell, so a sibling argument of the same call that reads the local is read before the write.

```maxon
function fillStr(a mutable String, b String)
	a.append("x")
	a.append(b)
end 'fillStr'

function main() returns ExitCode
	var s = "ab"
	fillStr(s, b: s)
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:9:10: argument 's' is read by another operand of this statement and passed by reference to 'fillStr' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: same-call-sibling-of-a-record-parameter-the-callee-only-writes -->
`mutable` on a record parameter is the write-through capability and lends no cell, so the same two arguments are legal: nothing overwrites what the sibling read.

```maxon
typealias StringArray = Array with String

function fill(a mutable StringArray, b StringArray)
	a.push("x {b.count()}")
end 'fill'

function main() returns ExitCode
	var x = StringArray.create(0)
	fill(x, b: x)
	print("{x.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: two-cells-of-one-local-the-callee-appends-to -->
Two `mutable` value positions of one call that name the same local overlap whether or not the callee reassigns either.

```maxon
function both(a mutable String, b mutable String)
	a.append("a")
	b.append("b")
end 'both'

function main() returns ExitCode
	var s = "ab"
	both(s, b: s)
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:9:13: argument 's' is read by another operand of this statement and passed by reference to 'both' (parameter 'b') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `var` in an earlier statement (`.clone()` a record, array or string), pass the copy for that operand and store it back into 's' after the call
```

<!-- test: cure-names-the-other-operand-a-sibling-field -->
The cure names the operand that reads the local, which is not the cell's own path.

```maxon
typealias Coord = int(i64.min to i64.max)

type Holder
	export var n as Coord
	export var a as String

	static function create() returns Self
		return Self{n: 5, a: "label"}
	end 'create'
end 'Holder'

function bump(n mutable Coord, text String) returns Coord
	n = n + 1
	return (text.byteLength() as Coord) + n
end 'bump'

function main() returns ExitCode
	var h = Holder.create()
	let k = bump(h.n, text: h.a)
	print("{k}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:20:15: argument 'h.n' is read by another operand of this statement and passed by reference to 'bump' (parameter 'n') — a hold cannot protect a value its by-reference cell overwrites. Copy 'h.a' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: cure-names-the-other-operand-a-receiver -->
A receiver that reads the record whose field the method takes by reference is the other operand, and the cure names it.

```maxon
typealias Coord = int(i64.min to i64.max)

type Holder
	export var n as Coord

	static function create(n Coord) returns Holder
		return Holder{n: n}
	end 'create'

	function settle(m mutable Coord) returns Coord
		m = m + 1
		return self.n
	end 'settle'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create(5)
	print("{h.settle(h.n)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:19:19: argument 'h.n' is read by another operand of this statement and passed by reference to 'settle' (parameter 'm') — a hold cannot protect a value its by-reference cell overwrites. Copy 'h' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: cure-rewrite-of-a-receiver-compiles -->
The rewrite the cure names compiles: the copy is another record, so the receiver no longer reads the cell's local.

```maxon
typealias Coord = int(i64.min to i64.max)

type Holder
	export var n as Coord

	static function create(n Coord) returns Holder
		return Holder{n: n}
	end 'create'

	function settle(m mutable Coord) returns Coord
		m = m + 1
		return self.n
	end 'settle'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create(5)
	let copy = h.clone()
	print("{copy.settle(h.n)} {h.n}\n")
	return 0
end 'main'
```
```stdout
5 6
```
```exitcode
0
```

<!-- test: cure-rewrite-of-a-sibling-field-compiles -->
The rewrite the cure names compiles: the sibling is read into a `let` in an earlier statement.

```maxon
typealias Coord = int(i64.min to i64.max)

type Holder
	export var n as Coord
	export var a as String

	static function create() returns Self
		return Self{n: 5, a: "label"}
	end 'create'
end 'Holder'

function bump(n mutable Coord, text String) returns Coord
	n = n + 1
	return (text.byteLength() as Coord) + n
end 'bump'

function main() returns ExitCode
	var h = Holder.create()
	let copy = h.a.clone()
	let k = bump(h.n, text: copy)
	print("{k}\n")
	return 0
end 'main'
```
```stdout
11
```
```exitcode
0
```

<!-- test: throw-handler-reads-in-its-own-frame -->
A `throw` handler diverges, so the read of the local in the thrown value does not precede a later operand's write.

```maxon
typealias Tally = int(0 to u64.max)

union Fault implements Error
	sized(n Tally)
end 'Fault'

function g(n Tally) returns Tally throws Fault
	if n > 5 'big'
		throw Fault.sized(n)
	end 'big'

	return n
end 'g'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function combine(a Tally, b Tally) returns Tally
	return a + b
end 'combine'

function run() returns Tally throws Fault
	var x = "original"

	return combine(try g(2) otherwise throw Fault.sized(x.byteLength() as Tally), b: put(x))
end 'run'

function main() returns ExitCode
	let k = try run() otherwise 0
	print("{k}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: two-cells-of-one-root-the-cure-is-a-var -->
Two `mutable` positions that name fields of one record are two cells of one root; the cure for a cell is a `var` copy stored back, since a `let` at a `mutable` position is refused.

```maxon
typealias Coord = int(i64.min to i64.max)

type Holder
	export var a as Coord
	export var b as Coord

	static function create() returns Self
		return Self{a: 1, b: 2}
	end 'create'
end 'Holder'

function swap(x mutable Coord, y mutable Coord)
	let t = x
	x = y
	y = t
end 'swap'

function main() returns ExitCode
	var h = Holder.create()
	swap(h.a, y: h.b)
	print("{h.a} {h.b}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:21:15: argument 'h.b' is read by another operand of this statement and passed by reference to 'swap' (parameter 'y') — a hold cannot protect a value its by-reference cell overwrites. Copy 'h.a' into a `var` in an earlier statement (`.clone()` a record, array or string), pass the copy for that operand and store it back into 'h.a' after the call
```

<!-- test: cure-rewrite-of-two-cells-of-one-root-compiles -->
The rewrite the cell cure names compiles: one field travels through a `var` copy that is stored back after the call.

```maxon
typealias Coord = int(i64.min to i64.max)

type Holder
	export var a as Coord
	export var b as Coord

	static function create() returns Self
		return Self{a: 1, b: 2}
	end 'create'
end 'Holder'

function swap(x mutable Coord, y mutable Coord)
	let t = x
	x = y
	y = t
end 'swap'

function main() returns ExitCode
	var h = Holder.create()
	var a = h.a
	swap(a, y: h.b)
	h.a = a
	print("{h.a} {h.b}\n")
	return 0
end 'main'
```
```stdout
2 1
```
```exitcode
0
```

<!-- test: view-of-a-cell-occupant-sent-after-a-store-and-a-hand-over -->
A `view` of a cell-resident `var` keeps the record it read after the `var` is rebound and the cell is handed by reference, and the service takes that record from the view.

```maxon
typealias Tally = int(0 to u64.max)

type Store
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(text String) returns Tally
		self.seen = self.seen + 1

		return text.byteLength() as Tally
	end 'keep'
end 'Store'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function fresh(n Tally) returns String
	return "{n} original"
end 'fresh'

function main() returns ExitCode
	let sink = spawn Store.create()
	var a = fresh(1)
	view v = a
	a = fresh(22)
	let k = put(a)
	let n = try await sink.keep(v) otherwise 0
	print("{k} {n} {a}\n")
	return 0
end 'main'
```
```stdout
1 10 replaced
```
```exitcode
0
```

<!-- test: let-of-a-cell-occupant-sent-after-a-store-and-a-hand-over -->
A `let` of a cell-resident `var` owns the record it read after the `var` is rebound and the cell is handed by reference, and the service takes that record from the `let`.

```maxon
typealias Tally = int(0 to u64.max)

type Store
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(text String) returns Tally
		self.seen = self.seen + 1

		return text.byteLength() as Tally
	end 'keep'
end 'Store'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function fresh(n Tally) returns String
	return "{n} original"
end 'fresh'

function main() returns ExitCode
	let sink = spawn Store.create()
	var a = fresh(1)
	let w = a
	a = fresh(22)
	let k = put(a)
	let n = try await sink.keep(w) otherwise 0
	print("{k} {n} {a}\n")
	return 0
end 'main'
```
```stdout
1 10 replaced
```
```exitcode
0
```

<!-- test: closure-capture-of-a-cell-lent-in-the-same-call-is-refused -->
A closure that captures a `var` moves it in, and the same call has already lent the `var`'s cell by reference.

```maxon
typealias Tally = int(0 to u64.max)

typealias Probe = function() returns Tally

function put2(text mutable String, f Probe) returns Tally
	text = "replaced"

	return f()
end 'put2'

function fresh(n Tally) returns String
	return "{n} original"
end 'fresh'

function main() returns ExitCode
	var a = fresh(1)
	let k = put2(a, f: function() gives a.byteLength() as Tally)
	print("{k} {a}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:18:15: use of moved value 'a': its ownership moved to the closure that captures it
```

<!-- test: cure-rewrite-of-two-cells-of-one-string-compiles -->
The rewrite the cell cure names compiles for a `String` local: the copy is a `.clone()` in a `var`, stored back after the call.

```maxon
function both(a mutable String, b mutable String)
	a.append("a")
	b.append("b")
end 'both'

function main() returns ExitCode
	var s = "ab"
	var c = s.clone()
	both(c, b: s)
	s = c
	print("{s}\n")
	return 0
end 'main'
```
```stdout
aba
```
```exitcode
0
```

<!-- test: write-based-callee-never-writes-its-cell -->
A `mutable` position lends its cell only if the callee writes the parameter. `look` never writes `a`, so a sibling argument that reads the same local reads a value nothing overwrites.

```maxon
typealias Tally = int(0 to u64.max)

function look(a mutable String, b String) returns Tally
	return (a.byteLength() as Tally) + (b.byteLength() as Tally)
end 'look'

function main() returns ExitCode
	var s = "abc"
	print("{look(s, b: s)}\n")
	return 0
end 'main'
```
```stdout
6
```
```exitcode
0
```

<!-- test: write-based-callee-assigns-its-cell -->
The same call is refused once `look` assigns the parameter.

```maxon
typealias Tally = int(0 to u64.max)

function look(a mutable String, b String) returns Tally
	a = "replaced"

	return b.byteLength() as Tally
end 'look'

function main() returns ExitCode
	var s = "abc"
	print("{look(s, b: s)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:12:15: argument 's' is read by another operand of this statement and passed by reference to 'look' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-forwarded-to-a-writer -->
A `mutable` parameter that is only forwarded writes through the parameter it is forwarded to, so the outer call lends its cell.

```maxon
typealias Tally = int(0 to u64.max)

function inner(x mutable String)
	x = "replaced"
end 'inner'

function outer(a mutable String, b String) returns Tally
	inner(a)

	return b.byteLength() as Tally
end 'outer'

function main() returns ExitCode
	var s = "abc"
	print("{outer(s, b: s)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:16:16: argument 's' is read by another operand of this statement and passed by reference to 'outer' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-forwarded-to-a-reader -->
Forwarding to a `mutable` parameter that is never written writes nothing.

```maxon
typealias Tally = int(0 to u64.max)

function inner(x mutable String) returns Tally
	return x.byteLength() as Tally
end 'inner'

function outer(a mutable String, b String) returns Tally
	return inner(a) + (b.byteLength() as Tally)
end 'outer'

function main() returns ExitCode
	var s = "abc"
	print("{outer(s, b: s)}\n")
	return 0
end 'main'
```
```stdout
6
```
```exitcode
0
```

<!-- test: write-based-forwarded-two-hops-to-a-writer -->
The written fact travels through every forwarding hop.

```maxon
typealias Tally = int(0 to u64.max)

function innermost(x mutable String)
	x = "replaced"
end 'innermost'

function middle(m mutable String)
	innermost(m)
end 'middle'

function outer(a mutable String, b String) returns Tally
	middle(a)

	return b.byteLength() as Tally
end 'outer'

function main() returns ExitCode
	var s = "abc"
	print("{outer(s, b: s)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:20:16: argument 's' is read by another operand of this statement and passed by reference to 'outer' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-mutual-recursion-neither-writes-ping-first -->
Two `mutable` parameters that forward to each other and are never written are never written, whichever is declared first.

```maxon
typealias Tally = int(0 to u64.max)

function ping(a mutable String, b String, again bool) returns Tally
	if again 'forward'
		return pong(a, b: b, again: false)
	end 'forward'

	return b.byteLength() as Tally
end 'ping'

function pong(a mutable String, b String, again bool) returns Tally
	if again 'forward'
		return ping(a, b: b, again: false)
	end 'forward'

	return (a.byteLength() as Tally) + (b.byteLength() as Tally)
end 'pong'

function main() returns ExitCode
	var s = "abc"
	print("{ping(s, b: s, again: true)}\n")
	return 0
end 'main'
```
```stdout
6
```
```exitcode
0
```

<!-- test: write-based-mutual-recursion-neither-writes-pong-first -->
The same pair declared in the other order.

```maxon
typealias Tally = int(0 to u64.max)

function pong(a mutable String, b String, again bool) returns Tally
	if again 'forward'
		return ping(a, b: b, again: false)
	end 'forward'

	return (a.byteLength() as Tally) + (b.byteLength() as Tally)
end 'pong'

function ping(a mutable String, b String, again bool) returns Tally
	if again 'forward'
		return pong(a, b: b, again: false)
	end 'forward'

	return b.byteLength() as Tally
end 'ping'

function main() returns ExitCode
	var s = "abc"
	print("{ping(s, b: s, again: true)}\n")
	return 0
end 'main'
```
```stdout
6
```
```exitcode
0
```

<!-- test: write-based-mutual-recursion-one-writes-ping-first -->
One member of the cycle writes the parameter, so the other writes it through the forward.

```maxon
typealias Tally = int(0 to u64.max)

function ping(a mutable String, b String, again bool) returns Tally
	if again 'forward'
		return pong(a, b: b, again: false)
	end 'forward'

	return b.byteLength() as Tally
end 'ping'

function pong(a mutable String, b String, again bool) returns Tally
	if again 'forward'
		return ping(a, b: b, again: false)
	end 'forward'

	a = "replaced"

	return b.byteLength() as Tally
end 'pong'

function main() returns ExitCode
	var s = "abc"
	print("{ping(s, b: s, again: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:24:15: argument 's' is read by another operand of this statement and passed by reference to 'ping' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-mutual-recursion-one-writes-pong-first -->
The same cycle declared in the other order.

```maxon
typealias Tally = int(0 to u64.max)

function pong(a mutable String, b String, again bool) returns Tally
	if again 'forward'
		return ping(a, b: b, again: false)
	end 'forward'

	a = "replaced"

	return b.byteLength() as Tally
end 'pong'

function ping(a mutable String, b String, again bool) returns Tally
	if again 'forward'
		return pong(a, b: b, again: false)
	end 'forward'

	return b.byteLength() as Tally
end 'ping'

function main() returns ExitCode
	var s = "abc"
	print("{ping(s, b: s, again: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:24:15: argument 's' is read by another operand of this statement and passed by reference to 'ping' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-function-value-counts-every-position -->
A call through a function value knows no body, so a `mutable` position counts as written even when the function the value names never writes.

```maxon
typealias Tally = int(0 to u64.max)

function look(a mutable String, b String) returns Tally
	return (a.byteLength() as Tally) + (b.byteLength() as Tally)
end 'look'

function main() returns ExitCode
	var s = "abc"
	let f = look
	print("{f(s, s)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:11:12: argument 's' is read by another operand of this statement and passed by reference to 'f' — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-interface-requirement-counts-every-position -->
A call through an interface requirement counts a `mutable` position as written even when every conformer only reads it.

```maxon
typealias Tally = int(0 to u64.max)

interface Looker
	function look(a mutable String, b String) returns Tally
end 'Looker'

type Plain implements Looker
	export var tag = "plain"

	static function create() returns Self
		return Self{}
	end 'create'

	function look(a mutable String, b String) returns Tally
		return (a.byteLength() as Tally) + (b.byteLength() as Tally)
	end 'look'
end 'Plain'

function drive(w Looker)
	var s = "start"
	print("{w.look(s, b: s)}\n")
end 'drive'

function main() returns ExitCode
	let p = Plain.create()
	drive(p)
	print("{p.tag}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:22:17: argument 's' is read by another operand of this statement and passed by reference to 'look' — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-callee-in-another-file -->
The written fact crosses files: the callee's file is parsed apart from the caller's.

```maxon
// --- file: lib/look.maxon
export function look(a mutable String, b String)
	print("{a.byteLength() + b.byteLength()}\n")
end 'look'

// --- file: app/main.maxon
function main() returns ExitCode
	var s = "abc"
	lib.look(s, b: s)
	return 0
end 'main'
```
```stdout
6
```
```exitcode
0
```

<!-- test: write-based-callee-in-another-file-writes -->
The same call to the sibling that assigns the parameter.

```maxon
// --- file: lib/look.maxon
export function rewrite(a mutable String, b String)
	a = "replaced"
	print("{b}\n")
end 'rewrite'

// --- file: app/main.maxon
function main() returns ExitCode
	var s = "abc"
	lib.rewrite(s, b: s)
	return 0
end 'main'
```
```maxoncstderr
error E3186: app/<fragment>:11:14: argument 's' is read by another operand of this statement and passed by reference to 'rewrite' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-async-callee-never-writes -->
An `async` call lends its cell only if the coroutine writes the parameter.

```maxon
typealias Tally = int(0 to u64.max)

function look(a mutable String, b String) returns Tally
	sleep(1)

	return (a.byteLength() as Tally) + (b.byteLength() as Tally)
end 'look'

function main() returns ExitCode
	var s = "abc"
	let promise = async look(s, b: s)
	let k = await promise
	print("{k}\n")
	return 0
end 'main'
```
```stdout
6
```
```exitcode
0
```

<!-- test: write-based-async-callee-writes -->
The coroutine that writes the parameter writes the caller's local.

```maxon
typealias Tally = int(0 to u64.max)

function look(a mutable String, b String) returns Tally
	sleep(1)
	a = "replaced"

	return b.byteLength() as Tally
end 'look'

function main() returns ExitCode
	var s = "abc"
	let promise = async look(s, b: s)
	let k = await promise
	print("{k}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:13:27: argument 's' is read by another operand of this statement and passed by reference to 'look' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-two-cells-only-one-written -->
Two `mutable` positions name one local and the callee writes only the second: the first is a read of the local before that write.

```maxon
function both(a mutable String, b mutable String)
	b = "b"
	print("{a}\n")
end 'both'

function main() returns ExitCode
	var s = "ab"
	both(s, b: s)
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:9:13: argument 's' is read by another operand of this statement and passed by reference to 'both' (parameter 'b') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-two-cells-neither-written -->
Two `mutable` positions that name one local overlap nothing when the callee writes neither.

```maxon
function both(a mutable String, b mutable String)
	print("{a}{b}\n")
end 'both'

function main() returns ExitCode
	var s = "ab"
	both(s, b: s)
	print("{s}\n")
	return 0
end 'main'
```
```stdout
abab
ab
```
```exitcode
0
```

<!-- test: a-send-after-an-operand-read-of-the-cell-and-a-writing-hand-over-is-refused -->
A message operand reads a cell-resident `var`, and a later operand hands the same cell to a callee that overwrites it. The send would take the record the first operand read out of a cell that was stored into in between, so the statement is refused at the hand-over and never reaches the take.

```maxon
typealias Tally = int(0 to u64.max)

type Store
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(text String, n Tally) returns Tally
		self.seen = self.seen + n

		return text.byteLength() as Tally
	end 'keep'
end 'Store'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	let sink = spawn Store.create()
	var a = "original"
	let n = try await sink.keep(a, n: put(a)) otherwise 0
	print("{n} {a}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:27:40: argument 'a' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'a' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: a-send-after-an-operand-read-of-the-cell-and-a-reading-hand-over-runs -->
The same statement with a callee that only reads the cell lends nothing, so the send takes the record the first operand read and the cell keeps the variable usable afterwards.

```maxon
typealias Tally = int(0 to u64.max)

type Store
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(text String, n Tally) returns Tally
		self.seen = self.seen + n

		return text.byteLength() as Tally
	end 'keep'
end 'Store'

function put(text mutable String) returns Tally
	return text.byteLength() as Tally
end 'put'

function main() returns ExitCode
	let sink = spawn Store.create()
	var a = "original"
	let n = try await sink.keep(a, n: put(a)) otherwise 0
	print("{n} {a}\n")
	return 0
end 'main'
```
```stdout
8 original
```
```exitcode
0
```

<!-- test: a-spawn-after-an-operand-read-of-the-cell-and-a-writing-hand-over-is-refused -->
A service factory operand takes a cell-resident `var` the same way a message does, so a later operand that overwrites the cell is refused before the take.

```maxon
typealias Tally = int(0 to u64.max)

type Store
	var seen as Tally

	static function create(text String, n Tally) returns Self
		return Self{seen: (text.byteLength() as Tally) + n}
	end 'create'

	export function total() returns Tally
		return self.seen
	end 'total'
end 'Store'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var a = "original"
	let sink = spawn Store.create(a, n: put(a))
	let n = try await sink.total() otherwise 0
	print("{n} {a}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:24:42: argument 'a' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'a' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: a-spawn-after-an-operand-read-of-the-cell-and-a-reading-hand-over-runs -->
A factory operand that reads the cell and a later operand whose callee only reads it lend nothing, so the spawn takes the record.

```maxon
typealias Tally = int(0 to u64.max)

type Store
	var seen as Tally

	static function create(text String, n Tally) returns Self
		return Self{seen: (text.byteLength() as Tally) + n}
	end 'create'

	export function total() returns Tally
		return self.seen
	end 'total'
end 'Store'

function put(text mutable String) returns Tally
	return text.byteLength() as Tally
end 'put'

function main() returns ExitCode
	var a = "original"
	let sink = spawn Store.create(a, n: put(a))
	let n = try await sink.total() otherwise 0
	print("{n} {a}\n")
	return 0
end 'main'
```
```stdout
16 original
```
```exitcode
0
```

<!-- test: an-async-after-an-operand-read-of-the-cell-and-a-writing-hand-over-is-refused -->
A coroutine argument takes a cell-resident `var` as a message does, so a later operand that overwrites the cell is refused before the take.

```maxon
typealias Tally = int(0 to u64.max)

function work(text String, n Tally) returns Tally
	return (text.byteLength() as Tally) + n
end 'work'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var a = "original"
	let promise = async work(a, n: put(a))
	let n = await promise
	print("{n} {a}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:16:37: argument 'a' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'a' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: an-async-after-an-operand-read-of-the-cell-and-a-reading-hand-over-runs -->
A coroutine argument that reads the cell and a later operand whose callee only reads it lend nothing, so the coroutine takes the record.

```maxon
typealias Tally = int(0 to u64.max)

function work(text String, n Tally) returns Tally
	return (text.byteLength() as Tally) + n
end 'work'

function put(text mutable String) returns Tally
	return text.byteLength() as Tally
end 'put'

function main() returns ExitCode
	var a = "original"
	let promise = async work(a, n: put(a))
	let n = await promise
	print("{n} {a}\n")
	return 0
end 'main'
```
```stdout
16 original
```
```exitcode
0
```

<!-- test: write-based-generic-template-never-writing-its-parameter-is-accepted -->
A generic body never reassigns a `mutable T` parameter, so a template whose body does not write it through anything else lends nothing, and a sibling argument may read the same local.

```maxon
function look(a mutable T, b T) uses T returns bool where T is Equatable
	return a == b
end 'look'

function main() returns ExitCode
	var s = "ab"
	let same = look(s, b: s)
	print("{same} {s}\n")
	return 0
end 'main'
```
```stdout
true ab
```
```exitcode
0
```

<!-- test: write-based-generic-template-forwarding-to-a-writer-is-refused -->
A template that forwards its `mutable T` parameter to a by-reference parameter its callee reassigns writes the caller's local.

```maxon
function replace(x mutable String)
	x = "replaced"
end 'replace'

function look(a mutable T, b T) uses T returns bool where T is Equatable
	replace(a)

	return a == b
end 'look'

function main() returns ExitCode
	var s = "ab"
	let same = look(s, b: s)
	print("{same} {s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:14:18: argument 's' is read by another operand of this statement and passed by reference to 'look' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-generic-template-second-position-is-judged-at-its-own-position -->
The fact is per parameter position of the template: the first parameter is read and the second, which is `mutable`, is never written.

```maxon
function look(a T, b mutable T, c T) uses T returns bool where T is Equatable
	return (a == b) and (b == c)
end 'look'

function main() returns ExitCode
	var s = "ab"
	let same = look(s, b: s, c: s)
	print("{same} {s}\n")
	return 0
end 'main'
```
```stdout
true ab
```
```exitcode
0
```

<!-- test: write-based-generic-template-second-position-written-through-a-forward-is-refused -->
The second position forwarded to a writer is refused at the second position and not the first.

```maxon
function replace(x mutable String)
	x = "replaced"
end 'replace'

function look(a T, b mutable T, c T) uses T returns bool where T is Equatable
	replace(b)

	return (a == b) and (b == c)
end 'look'

function main() returns ExitCode
	var s = "ab"
	let same = look(s, b: s, c: s)
	print("{same} {s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:14:24: argument 's' is read by another operand of this statement and passed by reference to 'look' (parameter 'b') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-field-write-through-a-mutable-isolated-parameter-is-accepted -->
A `mutable isolated` parameter whose callee only writes a field of the record never rebinds the caller's variable, so a sibling argument may read a field of the same variable.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function bump(box mutable isolated Box, seen Tally) returns Tally
	modify box 'edit'
		box.n = box.n + seen
	end 'edit'

	return seen
end 'bump'

function main() returns ExitCode
	var a = Box.create(1) as isolated Box
	let k = bump(a, seen: a.n)
	print("{k} {a.n}\n")
	return 0
end 'main'
```
```stdout
1 2
```
```exitcode
0
```

<!-- test: write-based-field-write-through-a-mutable-unique-parameter-is-accepted -->
A `unique` record behaves as an isolated one: a field write through the by-reference parameter leaves the caller's variable bound to the same record.

```maxon
typealias Tally = int(0 to u64.max)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

function bump(token mutable Token, seen Tally) returns Tally
	token.n = token.n + seen

	return seen
end 'bump'

function main() returns ExitCode
	var t = Token.create(1)
	let k = bump(t, seen: t.n)
	print("{k} {t.n}\n")
	return 0
end 'main'
```
```stdout
1 2
```
```exitcode
0
```

<!-- test: write-based-transitive-append-through-a-forward-is-refused -->
A write-through that rebinds the cell travels along the forwarding edge like a reassignment: `outer` forwards its parameter to a callee that appends to it.

```maxon
typealias Tally = int(0 to u64.max)

function inner(x mutable String)
	x.append("!")
end 'inner'

function outer(a mutable String, b String) returns Tally
	inner(a)

	return b.byteLength() as Tally
end 'outer'

function main() returns ExitCode
	var s = "abc"
	print("{outer(s, b: s)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:16:16: argument 's' is read by another operand of this statement and passed by reference to 'outer' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-forwarding-to-a-function-value-position-the-callee-may-write-is-refused -->
A parameter handed to a `mutable` position of a function value is written by the unknown body the value may name.

```maxon
typealias Tally = int(0 to u64.max)
typealias Editor = function(mutable String)

function shout(x mutable String)
	x = "replaced"
end 'shout'

function outer(a mutable String, edit Editor, b String) returns Tally
	edit(a)

	return b.byteLength() as Tally
end 'outer'

function main() returns ExitCode
	var s = "abc"
	print("{outer(s, edit: shout, b: s)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:17:16: argument 's' is read by another operand of this statement and passed by reference to 'outer' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-forwarding-to-a-by-value-position-of-a-function-value-writes-nothing -->
A by-value position of a function value cannot write the caller's cell, whatever body the value names.

```maxon
typealias Tally = int(0 to u64.max)
typealias Reader = function(String)

function peek(x String)
	print("{x}\n")
end 'peek'

function pick() returns Reader
	return peek
end 'pick'

function outer(a mutable String, read Reader, b String) returns Tally
	read(a)

	return b.byteLength() as Tally
end 'outer'

function main() returns ExitCode
	var s = "abc"
	print("{outer(s, read: pick(), b: s)}\n")
	return 0
end 'main'
```
```stdout
abc
3
```
```exitcode
0
```

<!-- test: write-based-forwarding-to-an-interface-requirement-position-is-refused -->
A parameter handed to a `mutable` position of an interface requirement is written by a conformer the call may reach.

```maxon
typealias Tally = int(0 to u64.max)

interface Editor
	function edit(text mutable String)
end 'Editor'

type Replacer implements Editor
	export var tag = "replacer"

	static function create() returns Self
		return Self{}
	end 'create'

	function edit(text mutable String)
		text = "replaced"
	end 'edit'
end 'Replacer'

function outer(a mutable String, e Editor, b String) returns Tally
	e.edit(a)

	return b.byteLength() as Tally
end 'outer'

function main() returns ExitCode
	let r = Replacer.create()
	var s = "abc"
	print("{outer(s, e: r, b: s)}\n")
	print("{r.tag}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:29:16: argument 's' is read by another operand of this statement and passed by reference to 'outer' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-nested-outer-written-inner-not -->
`f(s, b: g(s))`: the outer call overwrites its cell and the inner call only reads its own, so the outer lend is read by the inner argument.

```maxon
typealias Tally = int(0 to u64.max)

function f(a mutable String, k Tally) returns Tally
	a = "replaced"

	return k
end 'f'

function g(x mutable String) returns Tally
	return x.byteLength() as Tally
end 'g'

function main() returns ExitCode
	var s = "abc"
	print("{f(s, k: g(s))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:16:12: argument 's' is read by another operand of this statement and passed by reference to 'f' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-nested-inner-written-outer-not -->
The inner call overwrites its cell and the outer only reads its own: the outer cell is a read of the local before the inner write.

```maxon
typealias Tally = int(0 to u64.max)

function f(a mutable String, k Tally) returns Tally
	return (a.byteLength() as Tally) + k
end 'f'

function g(x mutable String) returns Tally
	x = "replaced"

	return 1
end 'g'

function main() returns ExitCode
	var s = "abc"
	print("{f(s, k: g(s))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:16:20: argument 's' is read by another operand of this statement and passed by reference to 'g' (parameter 'x') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-nested-both-written -->
Both calls overwrite their cells; the outer cell is lent across the inner call, so the statement is refused.

```maxon
typealias Tally = int(0 to u64.max)

function f(a mutable String, k Tally) returns Tally
	a = "outer"

	return k
end 'f'

function g(x mutable String) returns Tally
	x = "inner"

	return 1
end 'g'

function main() returns ExitCode
	var s = "abc"
	print("{f(s, k: g(s))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:18:12: argument 's' is read by another operand of this statement and passed by reference to 'f' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `var` in an earlier statement (`.clone()` a record, array or string), pass the copy for that operand and store it back into 's' after the call
```

<!-- test: write-based-nested-neither-written -->
Neither call writes its cell, so the nested hand-overs lend nothing and the statement runs.

```maxon
typealias Tally = int(0 to u64.max)

function f(a mutable String, k Tally) returns Tally
	return (a.byteLength() as Tally) + k
end 'f'

function g(x mutable String) returns Tally
	return x.byteLength() as Tally
end 'g'

function main() returns ExitCode
	var s = "abc"
	print("{f(s, k: g(s))}\n")
	return 0
end 'main'
```
```stdout
6
```
```exitcode
0
```

<!-- test: write-based-tuple-assignment-writes-both-cells -->
A tuple assignment to two `mutable` parameters reassigns each, so a sibling read of the first is refused.

```maxon
typealias Tally = int(0 to u64.max)

function swapped(a mutable String, b mutable String, c String) returns Tally
	(a, b) = (b, a)

	return c.byteLength() as Tally
end 'swapped'

function main() returns ExitCode
	var s = "abc"
	var t = "de"
	print("{swapped(s, b: t, c: s)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:13:18: argument 's' is read by another operand of this statement and passed by reference to 'swapped' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-assignment-in-a-try-handler-writes-the-cell -->
An assignment inside a `try ... otherwise` handler is a write of the parameter.

```maxon
typealias Tally = int(0 to u64.max)

enum Fault implements Error
	broken
end 'Fault'

function mayFail(n Tally) throws Fault
	if n > 0 'fails'
		throw Fault.broken
	end 'fails'
end 'mayFail'

function recover(a mutable String, b String) returns Tally
	try mayFail(1) otherwise 'handler'
		a = "recovered"
	end 'handler'

	return b.byteLength() as Tally
end 'recover'

function main() returns ExitCode
	var s = "abc"
	print("{recover(s, b: s)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:24:18: argument 's' is read by another operand of this statement and passed by reference to 'recover' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-assignment-in-a-match-arm-writes-the-cell -->
An assignment in a `match` arm is a write of the parameter.

```maxon
typealias Tally = int(0 to u64.max)

function pick(a mutable String, b String, n Tally) returns Tally
	match n 'arms'
		1 then a = "one"
		default then print("other\n")
	end 'arms'

	return b.byteLength() as Tally
end 'pick'

function main() returns ExitCode
	var s = "abc"
	print("{pick(s, b: s, n: 1)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:15:15: argument 's' is read by another operand of this statement and passed by reference to 'pick' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-assignment-in-a-loop-body-writes-the-cell -->
An assignment inside a loop body is a write of the parameter.

```maxon
typealias Tally = int(0 to u64.max)

function spin(a mutable String, b String) returns Tally
	for i in 0 upto 2 'loop'
		a = "spun {i}"
	end 'loop'

	return b.byteLength() as Tally
end 'spin'

function main() returns ExitCode
	var s = "abc"
	print("{spin(s, b: s)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:14:15: argument 's' is read by another operand of this statement and passed by reference to 'spin' (parameter 'a') — a hold cannot protect a value its by-reference cell overwrites. Copy 's' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: write-based-stale-isolated-occupant-at-a-send-is-refused -->
An isolated record read out of a cell that a later by-reference hand-over of the same call invalidates is refused at the send, never lowered with a stale occupant.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(box isolated Box, n Tally) returns Tally
		self.seen = self.seen + n

		return 1
	end 'keep'
end 'Store'

function look(box mutable isolated Box) returns Tally
	return 1
end 'look'

function main() returns ExitCode
	let sink = spawn Store.create()
	var a = Box.create(1) as isolated Box
	let n = try await sink.keep(a, n: look(a)) otherwise 0
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3138: <fragment>:33:30: argument `box` of the message `Store.keep` is BORROWED — read out of a field, an element or a parameter — so this frame does not own it. A send moves a mutable value: the service becomes its one owner and this frame gives up the reference it held, and a box one green thread holds is counted plainly — so a value with a second owner would put one box into two green threads' hands. A read-only value is shared with the service instead. Send a `.clone()`, or build the value at the send: an INTERPOLATION over it is a record nothing else can name
```

<!-- test: write-based-write-through-keeps-a-pending-field-read-alive -->
A callee that replaces a String field of a unique record while a sibling argument still reads that field leaves the read intact: the call holds the old text until it returns.

```maxon
typealias Tally = int(0 to u64.max)

type Token is unique
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Token'

function rename(token mutable Token, seen String) returns Tally
	token.label = "renamed"

	return seen.byteLength() as Tally
end 'rename'

function main() returns ExitCode
	var t = Token.create("original")
	let k = rename(t, seen: t.label)
	print("{k} {t.label}\n")
	return 0
end 'main'
```
```stdout
8 renamed
```
```exitcode
0
```

<!-- test: write-based-method-by-reference-parameter-reading-twin-is-accepted -->
A method whose `mutable` parameter is only read lends nothing, so a sibling argument may name the same variable; the receiver offset leaves the parameter position unchanged.

```maxon
typealias Tally = int(0 to u64.max)

type Gauge
	export var n as Tally

	static function create() returns Self
		return Self{n: 3}
	end 'create'

	function look(by mutable Tally, other Tally) returns Tally
		return by + other + self.n
	end 'look'
end 'Gauge'

function main() returns ExitCode
	let g = Gauge.create()
	var level = 4 as Tally
	print("{g.look(level, other: level)}\n")
	return 0
end 'main'
```
```stdout
11
```
```exitcode
0
```

<!-- test: write-based-ternary-arms-are-both-operands-of-the-statement -->
The arms of a conditional expression are numbered as operands in source order, whichever is taken, so a read in one arm and a by-reference write in the other are refused.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var a = "original"
	let c = true
	let n = (a.byteLength() as Tally) if c else put(a)
	print("{n} {a}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:13:50: argument 'a' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'a' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```
