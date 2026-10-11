---
feature: hold-window-across-ternary-blocks
status: experimental
keywords: [hold, borrow, counted read, ternary, short-circuit, block order, range check]
category: memory
---

# A hold's window follows the control flow, not the order blocks were created in

## Documentation

A read out of storage is counted only when something between the read and its last use can release the
storage. The decision walks every block the read can reach. A ternary whose true arm contains control flow — an
`and`, an `or`, a nested ternary — creates the arm's blocks before the ternary's own, so the order they were
created in is not the order the function lists them in. A release in one of those blocks still ends the
window, and a range check in one of them still stands in front of the operation that spends its value.

## Tests

<!-- test: a-clear-inside-a-short-circuit-arm-of-a-ternary-ends-the-hold -->
`clearsAndAnswers` runs only in the right operand of the `and` that is the ternary's true arm. The element `s`
was read before the ternary and is printed after it, so the read has to be counted: a window that never saw
the arm's block would leave it uncounted and print a freed string.

```maxon
typealias StringArray = Array with String

function clearsAndAnswers(arr StringArray) returns bool
	arr.clear()
	return true
end 'clearsAndAnswers'

function pick(arr StringArray, flag bool) returns String
	let s = try arr.get(0) otherwise ""
	let both = (flag and clearsAndAnswers(arr)) if flag else false

	if both 'cleared'
		return "{s} cleared"
	end 'cleared'

	return "{s} kept"
end 'pick'

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation {40 + 2}"]
	let s = pick(arr, flag: arr.count() == 1)
	print("[{s}] {arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42 cleared] 0
```

<!-- test: a-ternary-arm-that-releases-nothing-leaves-the-read-uncounted-and-correct -->
The control: the arm's right operand reads the array and releases nothing, so the window ends clean and the
program prints the same element.

```maxon
typealias StringArray = Array with String

function readsOnly(arr StringArray) returns bool
	return arr.count() == 1
end 'readsOnly'

function pick(arr StringArray, flag bool) returns String
	let s = try arr.get(0) otherwise ""
	let both = (flag and readsOnly(arr)) if flag else false

	if both 'seen'
		return "{s} seen"
	end 'seen'

	return "{s} missed"
end 'pick'

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation {40 + 2}"]
	let s = pick(arr, flag: arr.count() == 1)
	print("[{s}] {arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42 seen] 1
```

<!-- test: a-range-check-inside-a-short-circuit-arm-stays-in-front-of-its-value -->
The `and` that is the ternary's true arm has four counted reads of `label.text` in its left operand and a ranged
cast in its right one. The reads are uncounted (nothing releases `label`), and the cast's check sits in the
right operand's block, in front of the operation that spends the value. Nothing is out of range, so the program
prints its answer.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Octet = int(0 to 255)

type Label
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Label'

function choose(label Label, wide Integer, flag bool) returns Integer
	let r = (label.text.byteLength() + label.text.byteLength() + label.text.byteLength() + label.text.byteLength() > 0 and ((wide + 1 + 1 + 1 + 1 + 1 + 1 + 1 + 1) as Octet) as Integer > 3) if flag else false
	return 1 if r else 0
end 'choose'

function main() returns ExitCode
	let label = Label.create("a label long enough for the heap allocation path {40 + 2}")
	let ok = choose(label, wide: 100, flag: label.text.byteLength() > 0)
	print("ok {ok}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ok 1
```

<!-- test: the-range-check-in-a-short-circuit-arm-still-fires -->
The same shape with a value past the range: the check is in front of its value, so the program stops at the
cast instead of running on.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Octet = int(0 to 255)

type Label
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Label'

function choose(label Label, wide Integer, flag bool) returns Integer
	let r = (label.text.byteLength() + label.text.byteLength() + label.text.byteLength() + label.text.byteLength() > 0 and ((wide + 1 + 1 + 1 + 1 + 1 + 1 + 1 + 1) as Octet) as Integer > 3) if flag else false
	return 1 if r else 0
end 'choose'

function main() returns ExitCode
	let label = Label.create("a label long enough for the heap allocation path {40 + 2}")
	let ok = choose(label, wide: 300, flag: label.text.byteLength() > 0)
	print("ok {ok}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at the-range-check-in-a-short-circuit-arm-still-fires.test:14: Range check failed: value outside typealias 'Octet'
Stack trace:
  in choose
  in main
  in mrt_start
```
