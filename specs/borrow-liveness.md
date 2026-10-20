---
feature: borrow-liveness
status: selfhosted
keywords: [borrow, counted, reference, liveness, for-in, mutation, safety]
category: memory
---
# Borrow liveness — a value read out of storage outlives every write to that storage

## Documentation

A value read out of storage holds its own reference to what it read: `arr.get(i)`, `arr.first()`, a
read of a managed field, the element of a `for … in`, and a cursor's `current()` all count. A write to
the storage — `clear`, `push`, `truncate`, a rebind, a field store, or a call to a function or method
that does any of these — releases only the storage's own reference, so a value read before the write
stays alive and unchanged, and the program reads exactly what it read.

A `for … in` over an array holds a reference to the array it started on and reads its live count on
every step: a `push` in the body extends the iteration, a `clear` ends it, and rebinding the variable
leaves the loop walking the array it started on.

An argument read out of storage is counted like any other read, so a call may hand a function storage
it writes together with another view of that storage, and the view still names what it was read as. Only
a parameter the callee REASSIGNS, which is passed by reference, may not overlap another argument; that
is E3186, pinned in `specs/argument-overlap.md`.

⚠ **A RUNTIME-BUILT STRING IS WHAT MAKES THESE CASES EVIDENCE.** A literal is an immortal record that no
write can free, so a program reading one prints the right answer whether or not the read is counted.
The strings these cases read are built at run time, and each case prints what it read AFTER the write.

## Tests

<!-- test: rebind-keeps-the-value-read-out-of-the-old-record -->
### Rebinding the source leaves an element read out of it alive
`arr = <fresh>` releases the array's reference to the old record; `s` holds its own.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation {40 + 2}"]
	let s = try arr.get(0) otherwise ""
	arr = StringArray.create()
	print("[{s}] {arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 0
```

<!-- test: a-value-read-before-a-loop-survives-a-clear-on-every-trip -->
`first` is bound before the loop and read at the top of its body, so the read on the second trip follows
the first trip's `clear()` and `push`. It reads the element it was bound to.
```maxon
function main() returns ExitCode
	var arr = StringArray.create()
	arr.push("first {3} long enough for the heap")
	arr.push("second {4} long enough for the heap")
	let first = try arr.get(0) otherwise "none"
	var i = 0 as Count

	while i < 2 'loop'
		print("{first}\n")
		arr.clear()
		arr.push("third {5} long enough for the heap")
		i = i + 1
	end 'loop'

	print("{arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first 3 long enough for the heap
first 3 long enough for the heap
1
```

<!-- test: field-chain-source -->
### An element read through a field chain survives a clear of that field
`b.items.get(0)` counts the element, so `b.items.clear()` releases only the array's reference.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation {40 + 2}"]}
	end 'create'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	let s = try b.items.get(0) otherwise ""
	b.items.clear()
	print("[{s}] {b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 0
```

<!-- test: self-field-alias-source -->
### The bare self-field spelling counts the same
`items` inside a method of `Bag` names the field.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation {40 + 2}"]}
	end 'create'

	function peek() returns ExitCode
		let s = try items.get(0) otherwise ""
		items.clear()
		print("[{s}] {items.count()}\n")
		return 0
	end 'peek'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	return b.peek()
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 0
```

<!-- test: self-field-rebind -->
### Rebinding a self-field leaves an element read out of it alive
`items = <fresh>` and `self.items = <fresh>` are one self-field store; it releases the field's reference
to the old array and nothing else.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation {40 + 2}"]}
	end 'create'

	function peek() returns ExitCode
		let s = try items.get(0) otherwise ""
		items = StringArray.create()
		print("[{s}] {items.count()}\n")
		return 0
	end 'peek'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	return b.peek()
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 0
```

<!-- test: parameter-source -->
### An element read out of a PARAMETER survives a clear through that parameter
The clear reaches the caller's record; the element keeps its own reference.
```maxon
typealias StringArray = Array with String

function look(arr StringArray) returns ExitCode
	let s = try arr.get(0) otherwise ""
	arr.clear()
	print("[{s}] {arr.count()}\n")
	return 0
end 'look'

function main() returns ExitCode
	var a = ["hello world this is a long string for heap allocation {40 + 2}"]
	return look(a)
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 0
```

<!-- test: mutating-callee-argument -->
### Handing the source to a callee that clears it
The callee empties the caller's array; the element `s` read before the call is still its own.
```maxon
typealias StringArray = Array with String

function grow(dest StringArray)
	dest.clear()
end 'grow'

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation {40 + 2}"]
	let s = try arr.get(0) otherwise ""
	grow(arr)
	print("[{s}] {arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 0
```

<!-- test: mutating-callee-labelled-argument -->
### … and the same through a labelled argument out of declaration order
`grow(1, dest: arr, other: spare)` clears parameter 2 while `arr` is source argument 1.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

function grow(n Integer, other StringArray, dest StringArray) returns Integer
	dest.clear()
	return n + (other.count() as Integer)
end 'grow'

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation {40 + 2}"]
	let spare = StringArray.create()
	let s = try arr.get(0) otherwise ""
	let k = grow(1, dest: arr, other: spare)
	print("[{s}] {k} {arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 1 0
```

<!-- test: non-mutating-callee-argument -->
### A callee that only READS the source
The element and the array are both intact after the call.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

function peek(src StringArray) returns Integer
	return src.count()
end 'peek'

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	let s = try arr.get(0) otherwise ""
	let n = peek(arr)
	print("[{s}] {n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation] 1
```

<!-- test: shadowed-source-is-writable -->
### A block that SHADOWS the source's name writes a different array
The inner `arr` is a different binding, so writing it leaves the outer array and its element untouched.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	let s = try arr.get(0) otherwise ""
	if s.byteLength() > 0 'inner'
		var arr = StringArray.create()
		arr.push("a wholly different array that shares only the name")
		print("{arr.count()}\n")
	end 'inner'
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
[hello world this is a long string for heap allocation]
```

<!-- test: first-counts-like-get -->
### `first()` counts exactly as `get()` does
The pushes reallocate the array's buffer; `s` holds the element itself, not a slot of the buffer.
```maxon
function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation {40 + 2}"]
	let s = try arr.first() otherwise ""

	for i in 0 upto 40 'grow'
		arr.push("another long string for the heap allocation path {i}")
	end 'grow'

	arr.clear()
	print("[{s}] {arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 0
```

<!-- test: pop-moves-the-element-out -->
### `pop()` MOVES the element out of the array
The caller owns the element `pop()` returns outright.
```maxon
function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation", "second long string for the heap allocation path"]
	let s = try arr.pop() otherwise ""
	arr.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[second long string for the heap allocation path]
```

<!-- test: remove-moves-the-element-out -->
### `remove()` moves out too
```maxon
function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation", "second long string for the heap allocation path"]
	let s = try arr.remove(0) otherwise ""
	arr.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation]
```

<!-- test: a-value-read-and-consumed-inside-a-block -->
### A value read and consumed inside a block
The element is read and measured inside `inner`; the `clear()` after the block writes the array alone.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	var n = 0 as Integer
	if n == 0 'inner'
		let s = try arr.get(0) otherwise ""
		n = s.byteLength() as Integer
	end 'inner'
	arr.clear()
	print("{n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
53
```

<!-- test: mutation-in-otherwise-handler -->
### The source is mutable inside the `otherwise` handler
On the handler's path the get FAILED and `s` was never bound; the handler writes the array and returns.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = StringArray.create()
	let s = try arr.get(0) otherwise 'empty'
		arr.push("a replacement long string for the heap allocation path")
		return 7
	end 'empty'
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
7
```

<!-- test: forin-element-survives-a-callee-clearing-the-source -->
### A `for … in` element survives a callee clearing the array, and the clear ends the loop
The element is counted, so the body reads it intact after `wipe`; the next step reads the live count,
which is 0, so the second element is never reached.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation {40 + 2}", "second string long enough for heap allocation {40 + 2}"]}
	end 'create'
end 'Bag'

function wipe(dest StringArray)
	dest.clear()
end 'wipe'

function main() returns ExitCode
	var b = Bag.create()
	var total = 0 as Integer
	for it in b.items 'scan'
		wipe(b.items)
		print("[{it}]\n")
		total = total + (it.byteLength() as Integer)
	end 'scan'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42]
56
```

<!-- test: a-clear-at-the-end-of-a-for-body-ends-the-iteration -->
### A clear as the body's last statement ends the iteration at the next step
The loop reads the array's live count on every step, not the count it started with.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation {40 + 2}", "second string long enough for heap allocation {40 + 2}"]}
	end 'create'
end 'Bag'

function wipe(dest StringArray)
	dest.clear()
end 'wipe'

function main() returns ExitCode
	var b = Bag.create()
	var total = 0 as Integer
	for it in b.items 'scan'
		total = total + (it.byteLength() as Integer)
		wipe(b.items)
	end 'scan'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
56
```

<!-- test: forin-mutation-after-loop -->
### A write after the loop
The loop walks the whole array; the clear after its `end` writes an array no loop is reading.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation"]}
	end 'create'
end 'Bag'

function wipe(dest StringArray)
	dest.clear()
end 'wipe'

function main() returns ExitCode
	var b = Bag.create()
	var total = 0 as Integer
	for it in b.items 'scan'
		total = total + (it.byteLength() as Integer)
	end 'scan'
	wipe(b.items)
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
53
```

<!-- test: forin-discard-binds-no-element -->
### `for _ in` reads the live count too
The discard binds no element, and the loop still reads the array's live count each step, so the clear
in the first trip ends it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation", "second string long enough for heap allocation"]}
	end 'create'
end 'Bag'

function wipe(dest StringArray)
	dest.clear()
end 'wipe'

function main() returns ExitCode
	var b = Bag.create()
	var total = 0 as Integer
	for _ in b.items 'scan'
		wipe(b.items)
		total = total + 1
	end 'scan'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: forin-over-a-fresh-clone-walks-the-clone -->
### A loop over a fresh clone of a field walks the clone, not the field
The iterated container is a new array the loop owns, so pushing onto the field inside the body does not
extend the iteration.
```maxon
typealias Strings = Array with String
typealias Tally = int(0 to u64.max)

type Paths
	var items as Strings

	static function create() returns Self
		return Self{items: ["first path", "second path"]}
	end 'create'

	function doubled()
		for p in self.items.clone() 'each'
			self.items.push(p)
		end 'each'
	end 'doubled'

	function size() returns Tally
		return self.items.count()
	end 'size'
end 'Paths'

function main() returns ExitCode
	var paths = Paths.create()
	paths.doubled()
	print("{paths.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: otherwise-return-keeps-the-element-counted -->
### A diverging `otherwise` binds a counted element on the success path
`try arr.get(0) otherwise return 1` binds `s` to the element it read, which outlives the `clear()`.
```maxon
function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation {40 + 2}"]
	let s = try arr.get(0) otherwise return 1
	arr.clear()
	print("[{s}] {arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 0
```

<!-- test: otherwise-block-keeps-the-element-counted -->
### … and so does a diverging `otherwise` BLOCK
```maxon
function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation {40 + 2}"]
	let s = try arr.get(0) otherwise 'empty'
		return 7
	end 'empty'
	arr.clear()
	print("[{s}] {arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 0
```

<!-- test: a-value-computed-from-an-element-survives-a-clear -->
### A value computed from the element
`n` is an integer computed FROM the element, and the clear after it leaves `n` as it was.
```maxon
typealias Integer = int(i64.min to i64.max)

function sizeOf(s String) returns Integer
	return s.byteLength()
end 'sizeOf'

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	let n = sizeOf(try arr.get(0) otherwise "")
	arr.clear()
	print("{n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
53
```

<!-- test: range-loop-counter-is-an-index-not-an-element -->
### A counted range's loop variable is the index, not an element
The range's bound is evaluated once, before the loop, so the clear in the body changes nothing the loop
reads.
```maxon
typealias Integer = int(i64.min to i64.max)

function sizeOf(s String) returns Integer
	return s.byteLength()
end 'sizeOf'

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	var total = 0 as Integer
	for i in 0 upto sizeOf(try arr.get(0) otherwise "") 'l'
		arr.clear()
		total = total + i
	end 'l'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1378
```

<!-- test: a-sibling-field-clear-leaves-the-element-intact -->
### Clearing a SIBLING field of the same base
`b.other.clear()` empties a different array; the element read out of `b.items` and `b.items` itself are
untouched.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray
	export var other as StringArray

	static function create() returns Self
		return Self{items: ["hello world this is a long string for heap allocation {40 + 2}"], other: ["a second array entirely, sharing only its base binding"]}
	end 'create'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	let s = try b.items.get(0) otherwise ""
	b.other.clear()
	print("[{s}] {b.items.count()} {b.other.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 1 0
```

<!-- test: ternary-merge-keeps-the-element-counted -->
### An element chosen by a TERNARY survives a clear of its array
The merge carries the counted element, not a slot of the array.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation {40 + 2}"]
	let n = 0 as Integer
	let s = (try arr.get(0) otherwise "") if n == 0 else "a fallback string long enough for the heap"
	arr.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42]
```

<!-- test: match-gives-keeps-the-element-counted -->
### … and one chosen by a `match … gives`
```maxon
enum Mode
	first
	other
end 'Mode'

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation {40 + 2}"]
	let m = Mode.first
	let s = match m 'pick'
		first gives try arr.get(0) otherwise ""
		other gives "a fallback string long enough for the heap here"
	end 'pick'
	arr.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42]
```

<!-- test: match-gives-over-two-arrays-survives-clearing-both -->
### … whichever array the chosen arm read from
`s` may come out of either array, and clearing both leaves it intact.
```maxon
enum Mode
	first
	second
end 'Mode'

function main() returns ExitCode
	var a = ["alpha string long enough for the heap allocation path {40 + 2}"]
	var b = ["beta string long enough for the heap allocation path {40 + 2}"]
	let m = Mode.first
	let s = match m 'pick'
		first gives try a.get(0) otherwise ""
		second gives try b.get(0) otherwise ""
	end 'pick'
	a.clear()
	b.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha string long enough for the heap allocation path 42]
```

<!-- test: propagating-try-keeps-the-element-counted -->
### A PROPAGATING `try` binds the counted element
```maxon
typealias StringArray = Array with String

function look(arr StringArray) returns ExitCode throws ArrayError
	let s = try arr.get(0)
	arr.clear()
	print("[{s}] {arr.count()}\n")
	return 0
end 'look'

function main() returns ExitCode
	var a = ["hello world this is a long string for heap allocation {40 + 2}"]
	return try look(a) otherwise 9
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 0
```

<!-- test: var-reassigned-from-an-element-copies-it -->
### Assigning an element into a `var`
`s` owns the value it was assigned, so clearing the array leaves it intact.
```maxon
function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation"]
	var s = "an initial string long enough for the heap allocation"
	s = try arr.get(0) otherwise ""
	arr.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation]
```

<!-- test: loop-carried-var-holds-a-copy -->
### A `var` assigned on every trip of a loop keeps the last element it was assigned
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var arr = ["hello world this is a long string for heap allocation", "second long string for the heap allocation path"]
	var s = "an initial string long enough for the heap allocation"
	var i = 0 as Integer
	while i < 2 'scan'
		s = try arr.get(i as ElementIndex) otherwise ""
		i = i + 1
	end 'scan'
	arr.clear()
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[second long string for the heap allocation path]
```

<!-- test: receiver-method-writing-its-own-field -->
### A method that clears its OWN field leaves an element the caller read intact
`b.wipe()` empties the array exactly as `wipe(b.items)` does, with the array arriving as the receiver.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function wipe()
		items.clear()
	end 'wipe'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation {40 + 2}")
	let s = try b.items.get(0) otherwise ""
	b.wipe()
	print("{s.byteLength()} [{s}] {b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
47 [alpha string long enough for heap allocation 42] 0
```

<!-- test: receiver-method-explicit-self-spelling -->
### … and the `self.items.clear()` spelling is the same write
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function wipe()
		self.items.clear()
	end 'wipe'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation {40 + 2}")
	let s = try b.items.get(0) otherwise ""
	b.wipe()
	print("{s.byteLength()} [{s}] {b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
47 [alpha string long enough for heap allocation 42] 0
```

<!-- test: receiver-method-rebinding-its-own-field -->
### … and so does a method that REBINDS the field
`items = <fresh>` inside the method releases the field's reference to the old array and nothing else.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function wipe()
		items = StringArray.create()
	end 'wipe'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation {40 + 2}")
	let s = try b.items.get(0) otherwise ""
	b.wipe()
	print("{s.byteLength()} [{s}] {b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
47 [alpha string long enough for heap allocation 42] 0
```

<!-- test: receiver-method-inside-a-for-loop -->
### … and inside a `for … in` over the field it clears
The loop element survives the clear, and the clear ends the loop: the second element is never reached.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function wipe()
		items.clear()
	end 'wipe'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation {40 + 2}")
	b.items.push("beta string long enough for heap allocation {40 + 2}")
	var total = 0 as Integer
	for it in b.items 'scan'
		b.wipe()
		print("[{it}]\n")
		total = total + (it.byteLength() as Integer)
	end 'scan'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha string long enough for heap allocation 42]
47
```

<!-- test: receiver-method-writes-transitively -->
### … and through a method that CALLS one that clears the field
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function reset()
		items.clear()
	end 'reset'

	function wipe()
		self.reset()
	end 'wipe'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation {40 + 2}")
	let s = try b.items.get(0) otherwise ""
	b.wipe()
	print("{s.byteLength()} [{s}] {b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
47 [alpha string long enough for heap allocation 42] 0
```

<!-- test: receiver-method-writing-its-own-field-through-a-corpus-member -->
### … and through `truncate`, an `Array` member declared in `stdlib/Array.maxon`
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function wipe()
		items.truncate(0)
	end 'wipe'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation {40 + 2}")
	let s = try b.items.get(0) otherwise ""
	b.wipe()
	print("{s.byteLength()} [{s}] {b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
47 [alpha string long enough for heap allocation 42] 0
```

<!-- test: a-free-callee-writing-a-field-of-its-parameter -->
### … and through a FREE callee that truncates a field of its parameter
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'
end 'Bag'

function wipe(bag Bag)
	bag.items.truncate(0)
end 'wipe'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation {40 + 2}")
	let s = try b.items.get(0) otherwise ""
	wipe(b)
	print("{s.byteLength()} [{s}] {b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
47 [alpha string long enough for heap allocation 42] 0
```

<!-- test: a-corpus-served-read-only-member-leaves-an-element-read-intact -->
### A method calling a read-only `stdlib/Array.maxon` member
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function room() returns Integer
		return items.capacity()
	end 'room'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	let n = b.room()
	print("{s.byteLength()} {n > 0}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
44 true
```

<!-- test: receiver-method-that-writes-nothing -->
### A method that does NOT write the field
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function size() returns Integer
		return items.count()
	end 'size'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	let n = b.size()
	print("{s.byteLength()} {n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
44 1
```

<!-- test: let-receiver-refuses-mutating-methods -->
### A `let` receiver refuses a method that writes its own fields
A `let` is deeply read-only, so a type's own methods that write `self` are refused on it, as is a write
through one of its fields (`self-keyword.md:self-with-params` pins the field-store door,
`parameter-mutation:let-struct-with-array-field-to-mutating-method-error` the container-method one).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray
	export var total as Integer

	static function create() returns Self
		return Self{items: StringArray.create(), total: 0}
	end 'create'

	function wipe()
		items.clear()
	end 'wipe'

	function add(v Integer)
		total = total + v
	end 'add'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	b.add(42)
	b.wipe()
	print("{b.total} {b.items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: specs/borrow-liveness/let-receiver-refuses-mutating-methods.maxon:24:4: cannot pass 'b', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
error E3159: specs/borrow-liveness/let-receiver-refuses-mutating-methods.maxon:25:4: cannot pass 'b', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: var-receiver-takes-mutating-methods -->
### A `var` receiver takes a method that writes its own fields
The same program on a `var` runs, and each write is visible to the read after it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray
	export var total as Integer

	static function create() returns Self
		return Self{items: StringArray.create(), total: 0}
	end 'create'

	function wipe()
		items.clear()
	end 'wipe'

	function add(v Integer)
		total = total + v
	end 'add'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	b.add(42)
	b.wipe()
	print("{b.total} {b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42 0
```

<!-- test: sibling-call-on-the-enclosing-self -->
### A sibling call INSIDE the type that clears the field the method read from
`reset()` clears `items` after `s` was read out of it, one level in from `b.wipe()`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function reset()
		items.clear()
	end 'reset'

	function measure() returns Integer
		let s = try items.get(0) otherwise ""
		reset()
		print("[{s}]\n")
		return s.byteLength()
	end 'measure'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation {40 + 2}")
	print("{b.measure()} {b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha string long enough for heap allocation 42]
47 0
```

<!-- test: sibling-call-explicit-self-spelling -->
### … and `self.reset()` is the same call
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function reset()
		items.clear()
	end 'reset'

	function measure() returns Integer
		let s = try items.get(0) otherwise ""
		self.reset()
		print("[{s}]\n")
		return s.byteLength()
	end 'measure'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation {40 + 2}")
	print("{b.measure()} {b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha string long enough for heap allocation 42]
47 0
```

<!-- test: sibling-call-that-writes-nothing -->
### A read-only sibling call
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'

	function size() returns Integer
		return items.count()
	end 'size'

	function ok() returns Integer
		let s = try items.get(0) otherwise ""
		let n = self.size()
		return (s.byteLength() as Integer) + n
	end 'ok'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	print("{b.ok()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
45
```

<!-- test: a-method-writing-a-non-array-field-leaves-an-element-read-intact -->
### A method that writes a NON-ARRAY field of the receiver
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray
	export var total as Integer

	static function create() returns Self
		return Self{items: StringArray.create(), total: 0}
	end 'create'

	function add(v Integer)
		total = total + v
	end 'add'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	b.add(42)
	print("{s.byteLength()} {b.total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
44 42
```

<!-- test: module-storage-source -->
### An element read out of a top-level `var` survives a clear of it
Module storage counts exactly as a local does.
```maxon
typealias StringArray = Array with String

var g = StringArray.create()

function main() returns ExitCode
	g.push("hello world this is a long string for heap allocation {40 + 2}")
	let s = try g.get(0) otherwise ""
	g.clear()
	print("[{s}] {g.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 0
```

<!-- test: module-storage-rebind -->
### … and a rebind of it
The global store releases the slot's reference to the old array; `s` holds its own.
```maxon
typealias StringArray = Array with String

var g = StringArray.create()

function main() returns ExitCode
	g.push("hello world this is a long string for heap allocation {40 + 2}")
	let s = try g.get(0) otherwise ""
	g = StringArray.create()
	print("[{s}] {g.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 0
```

<!-- test: module-storage-to-a-mutating-callee -->
### … and a callee that clears it
```maxon
typealias StringArray = Array with String

var g = StringArray.create()

function grow(dest StringArray)
	dest.clear()
end 'grow'

function main() returns ExitCode
	g.push("hello world this is a long string for heap allocation {40 + 2}")
	let s = try g.get(0) otherwise ""
	grow(g)
	print("[{s}] {g.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 0
```

<!-- test: forin-over-module-storage -->
### … and a `for … in` over one, cleared in the body
The element survives the clear, and the clear ends the loop.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

var g = StringArray.create()

function main() returns ExitCode
	g.push("hello world this is a long string for heap allocation {40 + 2}")
	g.push("second string long enough for heap allocation {40 + 2}")
	var total = 0 as Integer
	for it in g 'scan'
		g.clear()
		print("[{it}]\n")
		total = total + (it.byteLength() as Integer)
	end 'scan'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42]
56
```

<!-- test: module-storage-read-only-callee-then-clear -->
### A read-only callee, then a clear, over a top-level `var`
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

var g = ["hello world this is a long string for heap allocation"]

function peek(src StringArray) returns Integer
	return src.count()
end 'peek'

function main() returns ExitCode
	let s = try g.get(0) otherwise ""
	let n = peek(g)
	print("[{s}] {n}\n")
	g.clear()
	print("{g.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation] 1
0
```

<!-- test: a-method-writing-a-string-or-set-field-leaves-an-element-read-intact -->
### A method that writes a `String` or a `Set` field of the receiver
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String
typealias IntSet = Set with Integer

type Bag
	export var items as StringArray
	export var name as String
	export var seen as IntSet

	static function create() returns Self
		return Self{items: StringArray.create(), name: "tag", seen: IntSet.create()}
	end 'create'

	function mark()
		name.append("!")
	end 'mark'

	function note(v Integer)
		seen.insert(v)
	end 'note'

	function seenCount() returns Integer
		return seen.count()
	end 'seenCount'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	b.mark()
	b.note(7)
	print("{s.byteLength()} {b.name} {b.seenCount()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
44 tag! 1
```

<!-- test: a-field-store-in-a-callee-freeing-its-parameters-array -->
### A callee that STORES an array field of its parameter
The store through the parameter replaces the caller's field and releases the old array; the element the
caller read is still its own.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray

	static function create() returns Self
		return Self{items: StringArray.create()}
	end 'create'
end 'Bag'

function wipe(b Bag)
	b.items = StringArray.create()
end 'wipe'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation {40 + 2}")
	let s = try b.items.get(0) otherwise ""
	wipe(b)
	print("{s.byteLength()} [{s}] {b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
47 [alpha string long enough for heap allocation 42] 0
```

<!-- test: a-scalar-field-store-leaves-an-element-read-intact -->
### Storing a SCALAR field of the same base
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

type Bag
	export var items as StringArray
	export var n as Integer

	static function create() returns Self
		return Self{items: StringArray.create(), n: 0}
	end 'create'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("hello")
	let s = try b.items.get(0) otherwise ""
	b.n = 5
	print("{s} {b.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello 5
```

<!-- test: a-value-read-through-a-cursor-survives-a-clear-of-its-source -->
### A value read through a cursor survives a clear of the cursor's source
`c.current()` counts the element it returns, and the cursor counts its source.
```maxon
function main() returns ExitCode
	var xs = ["hello world this is a long string for heap allocation {40 + 2}"]
	view c = try xs.cursor() otherwise return 1
	let s = c.current()
	xs.clear()
	print("[{s}] {xs.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42] 0
```

<!-- test: a-value-read-through-a-cursor-printed-before-the-clear -->
### A value read through a cursor, printed before the clear
```maxon
function main() returns ExitCode
	var xs = ["hello world this is a long string for heap allocation {40 + 2}"]
	view c = try xs.cursor() otherwise return 1
	let s = c.current()
	print("[{s}]\n")
	xs.clear()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello world this is a long string for heap allocation 42]
```

<!-- test: a-value-read-out-of-an-element-survives-a-clear-of-that-element-s-field -->
### A value read out of an element's field survives a clear of that field
`p` is an element read out of `arr`, and `s` an element read out of `p.items`; `p.items.clear()`
releases only the array's reference to `s`.
```maxon
typealias StringArray = Array with String

type Holder
	export var items as StringArray

	static function create() returns Self
		return Self{items: ["alpha string long enough for heap allocation {40 + 2}"]}
	end 'create'
end 'Holder'

typealias HolderArray = Array with Holder

function main() returns ExitCode
	var arr = HolderArray.create()
	arr.push(Holder.create())
	var p = try arr.get(0) otherwise Holder.create()
	let s = try p.items.get(0) otherwise ""
	p.items.clear()
	print("[{s}] {p.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha string long enough for heap allocation 42] 0
```

<!-- test: a-callee-clearing-a-managed-list-leaves-a-node-handle-alive -->
### A callee that clears a `__ManagedList` does NOT free the node a handle still names

⚖ **USER RULING — NODES ARE REFCOUNTED AND A HANDLE IS A SECOND OWNER.** `clear` drops the CHAIN's
reference and nothing else; a node a handle still holds walks out of the walk alive, still carrying its
element, and dies with its last owner, so a `clear` in a callee frees nothing a caller's handle names.

⭐ `managed-list-node-handle-lifetime.md:a-returned-handle-survives-a-clear` is its twin with the mint
in another function.
```maxon
typealias StringChain = __ManagedList with String

function wipe(chain StringChain)
	chain.clear()
end 'wipe'

function main() returns ExitCode
	var chain = StringChain.create()
	view node = chain.insertLast("alpha string long enough for heap allocation {40 + 2}")
	wipe(chain)
	print("[{node.value()}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha string long enough for heap allocation 42]
```

<!-- test: a-callee-inserting-into-a-managed-list-keeps-every-handle -->
### An INSERTION in a callee disturbs no handle the caller holds
A chain's nodes are individually allocated and never move, so an insertion rewrites two link words
and dangles nothing. This case pins that a **cross-function** insertion leaves every outstanding handle
readable, which is the normal way a program builds a list it holds handles into
(`specs/managed-list.md:core.insert-first-multiple` is its same-body twin) — a *composition* case: a
callee mutating a chain the caller holds handles into.

⚠ The exit code is pinned, not just the output: a node read back out of freed memory is a wrong answer,
and a stdout-only case never checks that the run succeeded.
```maxon
typealias StringChain = __ManagedList with String

function grow(chain StringChain)
	_ = chain.insertLast("beta string long enough for heap allocation")
end 'grow'

function main() returns ExitCode
	var chain = StringChain.create()
	view node = chain.insertLast("alpha string long enough for heap allocation {40 + 2}")
	grow(chain)
	print("[{node.value()}] {chain.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha string long enough for heap allocation 42] 2
```

<!-- test: a-method-rebinding-a-list-field-leaves-an-element-read-out-of-it-alive -->
### A method that REBINDS a `List` field does not free the element a caller holds
`first()` counts the element it returns, so the store releases the list and `s` keeps its own reference.
```maxon
typealias StringList = List with String

type Bag
	export var items as StringList

	static function create() returns Self
		return Self { items: StringList.create() }
	end 'create'

	function reset()
		items = StringList.create()
	end 'reset'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.append("alpha string long enough for heap allocation {40 + 2}")
	let s = try b.items.first() otherwise "none"
	b.reset()
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha string long enough for heap allocation 42]
```

<!-- test: a-field-store-in-a-callee-freeing-its-parameters-list -->
### … and the same store one indirection out, through a parameter
`b.items = <fresh>` through a parameter, where the case above stores `items = <fresh>` inside a method.
```maxon
typealias StringList = List with String

type Bag
	export var items as StringList

	static function create() returns Self
		return Self { items: StringList.create() }
	end 'create'
end 'Bag'

function wipe(b Bag)
	b.items = StringList.create()
end 'wipe'

function main() returns ExitCode
	var b = Bag.create()
	b.items.append("alpha string long enough for heap allocation {40 + 2}")
	let s = try b.items.first() otherwise "none"
	wipe(b)
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha string long enough for heap allocation 42]
```

<!-- test: a-method-rebinding-a-nested-struct-field-keeps-an-element-read-out-of-it -->
### Rebinding a nested STRUCT field drops the record and its array, and the element read out of it survives
Dropping `inner` releases the `Array` that struct owns; `s` holds its own reference to the element.
```maxon
typealias StringArray = Array with String

type Inner
	export var items as StringArray

	static function create() returns Self
		return Self { items: StringArray.create() }
	end 'create'
end 'Inner'

type Bag
	export var inner as Inner

	static function create() returns Self
		return Self { inner: Inner.create() }
	end 'create'

	function reset()
		inner = Inner.create()
	end 'reset'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.inner.items.push("alpha string long enough for heap allocation {40 + 2}")
	let s = try b.inner.items.get(0) otherwise "none"
	b.reset()
	print("[{s}] {b.inner.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha string long enough for heap allocation 42] 0
```

<!-- test: a-string-field-store-leaves-an-element-read-intact -->
### Storing a `String` field of the same receiver
`retag` is the self-field spelling, `rename` the field-chain one through a parameter.
```maxon
typealias StringArray = Array with String

type Bag
	export var items as StringArray
	export var name as String

	static function create() returns Self
		return Self { items: StringArray.create(), name: "tag" }
	end 'create'

	function retag()
		name = "another tag entirely"
	end 'retag'
end 'Bag'

function rename(b Bag)
	b.name = "a third tag entirely"
end 'rename'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push("alpha string long enough for heap allocation")
	let s = try b.items.get(0) otherwise ""
	b.retag()
	rename(b)
	print("{s.byteLength()} {b.name}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
44 a third tag entirely
```

<!-- test: a-managed-field-handed-to-the-method-that-writes-its-owner -->
### The receiver and one of its fields, handed to a method that replaces the field
`replace` stores over `self.s`, the field its argument was read out of. The argument holds its own
reference, so the store releases only the field's, and `other` still names the bytes `fill` built.
```maxon
typealias Count = int(0 to 1000)

type Cell
	export var s as String

	static function create() returns Cell
		return Cell{s: ""}
	end 'create'

	function fill(n Count)
		var t = ""
		for _ in 0 upto n 'grow'
			t = "{t}x"
		end 'grow'
		self.s = t
	end 'fill'

	function replace(other String) returns String
		self.s = "replaced"
		return "[{other}]"
	end 'replace'
end 'Cell'

function main() returns ExitCode
	var a = Cell.create()
	a.fill(10)
	let out = a.replace(a.s)
	print("{out} {a.s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx] replaced
```

<!-- test: a-managed-field-handed-to-a-free-callee-beside-its-owner -->
### The free-function spelling: the owner and its field, in two argument columns
`clobber` is handed `b` for `c` together with `b.s`, and stores over `c.s`; `borrowed` holds its own
reference to the record the store replaces.
```maxon
typealias Count = int(0 to 1000)

type Cell
	export var s as String

	static function create() returns Cell
		return Cell{s: ""}
	end 'create'

	function fill(n Count)
		var t = ""
		for _ in 0 upto n 'grow'
			t = "{t}x"
		end 'grow'
		self.s = t
	end 'fill'
end 'Cell'

function clobber(c Cell, borrowed String) returns String
	c.s = "clobbered"
	return "[{borrowed}]"
end 'clobber'

function main() returns ExitCode
	var b = Cell.create()
	b.fill(10)
	let out = clobber(b, borrowed: b.s)
	print("{out} {b.s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx] clobbered
```

<!-- test: an-array-element-handed-to-a-callee-that-clears-the-array -->
### The ARRAY-ELEMENT analogue: an array and one of its elements, to a callee that clears the array
An element read inline as an argument is counted like the bound spelling — `let s = try arr.get(0) …`
then `grow(arr)` in `mutating-callee-argument` — so `clear` releases only the array's reference and
`borrowed` still names the element.
```maxon
typealias Count = int(0 to 1000)
typealias StringArray = Array with String

function clobber(a StringArray, borrowed String) returns String
	a.clear()
	return "[{borrowed}]"
end 'clobber'

function build(n Count) returns String
	var t = ""
	for _ in 0 upto n 'grow'
		t = "{t}x"
	end 'grow'
	return t
end 'build'

function main() returns ExitCode
	var a = StringArray.create()
	a.push(build(10))
	let out = clobber(a, borrowed: try a.get(0) otherwise "")
	print("{out} {a.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx] 0
```

<!-- test: a-field-of-a-module-var-handed-to-a-callee-that-reaches-it-by-name -->
### A field of a module `var`, handed to a callee that replaces it by name
`clobber` is handed one argument and reaches the cell by NAME. The read of `g.s` counts, so the store
releases only the field's reference and `borrowed` still names the bytes it was handed.
```maxon
typealias Count = int(0 to 1000)

type Cell
	export var s as String

	static function create() returns Cell
		return Cell{s: ""}
	end 'create'

	function fill(n Count)
		var t = ""
		for _ in 0 upto n 'grow'
			t = "{t}x"
		end 'grow'
		self.s = t
	end 'fill'
end 'Cell'

var g = Cell.create()

function clobber(borrowed String) returns String
	g.s = "clobbered"
	return "[{borrowed}]"
end 'clobber'

function main() returns ExitCode
	g.fill(10)
	print("{clobber(g.s)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx]
```

<!-- test: a-bare-module-var-handed-to-a-callee-that-rebinds-it -->
### … and the global itself, rebound under its own argument
The argument IS the global's value; the rebind releases the global's reference and the parameter keeps
its own.
```maxon
typealias Count = int(0 to 1000)

var g = ""

function grow(n Count)
	var t = ""
	for _ in 0 upto n 'grow'
		t = "{t}x"
	end 'grow'
	g = t
end 'grow'

function clobber(borrowed String) returns String
	g = "clobbered"
	return "[{borrowed}]"
end 'clobber'

function main() returns ExitCode
	grow(10)
	print("{clobber(g)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx]
```

<!-- test: a-receiver-read-out-of-a-module-var-outlives-a-callee-that-replaces-it -->
### … and a RECEIVER read out of a module `var`, whose method replaces that global
`g.measure()` reads its receiver out of a module `var`; the body replaces the global, and `self` still
names the record the call was made on.
```maxon
typealias Count = int(0 to 1000)

type Inner
	export var s as String

	static function create() returns Inner
		return Inner{s: ""}
	end 'create'

	function fill(n Count)
		var t = ""
		for _ in 0 upto n 'grow'
			t = "{t}x"
		end 'grow'
		self.s = t
	end 'fill'

	function measure() returns String
		g = Inner.create()
		return "[{self.s}]"
	end 'measure'
end 'Inner'

var g = Inner.create()

function main() returns ExitCode
	g.fill(10)
	print("{g.measure()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx]
```

<!-- test: a-field-handed-to-a-callee-that-cannot-reach-it -->
### A callee handed only the VALUE of a field, with the owner nowhere in reach
`measure` is handed `b.s` and nothing else: it cannot name the cell, the cell is not module storage, and no other
argument denotes it, so nothing it can do frees the record the field holds. The program runs and prints the string
the field held.
```maxon
typealias Count = int(0 to 1000)

type Cell
	export var s as String

	static function create() returns Cell
		return Cell{s: ""}
	end 'create'

	function fill(n Count)
		var t = ""
		for _ in 0 upto n 'grow'
			t = "{t}x"
		end 'grow'
		self.s = t
	end 'fill'
end 'Cell'

function measure(borrowed String) returns String
	return "[{borrowed}]"
end 'measure'

function main() returns ExitCode
	var b = Cell.create()
	b.fill(10)
	print("{measure(b.s)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx]
```

<!-- test: a-merged-borrow-carries-every-arms-storage-to-the-call -->
### A value MERGED from two arms, handed beside the array one arm read from
The `match` produces a new value — the element its chosen arm read, counted — rather than a view of
either array's storage, so the call hands `clobber` one view of `b` and the clear leaves `borrowed`
intact.
```maxon
typealias Count = int(0 to 1000)
typealias StringArray = Array with String

enum Mode
	first
	second
end 'Mode'

function clobber(target StringArray, borrowed String) returns String
	target.clear()
	return "[{borrowed}]"
end 'clobber'

function build(n Count) returns String
	var t = ""
	for _ in 0 upto n 'grow'
		t = "{t}x"
	end 'grow'
	return t
end 'build'

function main() returns ExitCode
	var a = StringArray.create()
	var b = StringArray.create()
	a.push(build(4))
	b.push(build(10))
	let m = Mode.second
	let out = clobber(b, borrowed: match m 'pick'
		first gives try a.get(0) otherwise ""
		second gives try b.get(0) otherwise ""
	end 'pick')
	print("{out}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[xxxxxxxxxx]
```

<!-- test: iterating-a-conditional-of-two-fields-survives-a-call-that-rebinds-one -->
A `for` over a conditional of two field chains holds its own reference to the array it walks, so the body
rebinding `self.holder.items` through `weigh` leaves the loop walking that array to its end.
```maxon
typealias Tally = int(0 to i64.max)

type Decl
	export let name as String
	export let weight as Tally

	static function create(name String, weight Tally) returns Decl
		return Decl{name: name, weight: weight}
	end 'create'
end 'Decl'

typealias DeclArray = Array with Decl

type Holder
	export var items = DeclArray.create()

	static function create() returns Holder
		return Holder{}
	end 'create'
end 'Holder'

type Driver
	var holder as Holder
	var other as Holder
	var flag = true

	static function create() returns Driver
		return Driver{holder: Holder.create(), other: Holder.create()}
	end 'create'

	function fill()
		self.holder.items.push(Decl.create("first {self.holder.items.count()}", weight: 1))
		self.holder.items.push(Decl.create("second {self.holder.items.count()}", weight: 2))
	end 'fill'


	function churn()
		var junk = DeclArray.create()

		for i in 0 upto 50 'each'
			junk.push(Decl.create("garbage-garbage-garbage {i}", weight: 99))
		end 'each'

		print("{junk.count()}\n")
	end 'churn'

	function weigh(w Tally) returns Tally
		return self.deeper(w)
	end 'weigh'

	function deeper(w Tally) returns Tally
		self.holder.items = DeclArray.create()
		self.churn()
		return w + 1
	end 'deeper'

	function run() returns Tally
		var total = 0 as Tally

		for candidate in self.holder.items if self.flag else self.other.items 'eachCandidate'
			let w = self.weigh(candidate.weight)
			print("{candidate.name} {w}\n")
			total = total + w
		end 'eachCandidate'

		return total
	end 'run'
end 'Driver'

function main() returns ExitCode
	var d = Driver.create()
	d.fill()
	let t = d.run()
	print("total {t}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
50
first 0 2
50
second 1 3
total 5
```

<!-- test: iterating-a-field-of-a-call-result-survives-a-rebind-of-that-field -->
The loop holds its own reference to the array it walks, so the body rebinding `self.holder.items` frees nothing the loop still reads.
```maxon
typealias Tally = int(0 to i64.max)

type Decl
	export let name as String
	export let weight as Tally

	static function create(name String, weight Tally) returns Decl
		return Decl{name: name, weight: weight}
	end 'create'
end 'Decl'

typealias DeclArray = Array with Decl

type Holder
	export var items = DeclArray.create()

	static function create() returns Holder
		return Holder{}
	end 'create'
end 'Holder'

type Driver
	var holder as Holder

	static function create() returns Driver
		return Driver{holder: Holder.create()}
	end 'create'

	function fill()
		self.holder.items.push(Decl.create("first {self.holder.items.count()}", weight: 1))
		self.holder.items.push(Decl.create("second {self.holder.items.count()}", weight: 2))
	end 'fill'

	function theHolder() returns Holder
		return self.holder
	end 'theHolder'

	function churn()
		var junk = DeclArray.create()

		for i in 0 upto 50 'each'
			junk.push(Decl.create("garbage-garbage-garbage {i}", weight: 99))
		end 'each'

		print("{junk.count()}\n")
	end 'churn'

	function weigh(w Tally) returns Tally
		return self.deeper(w)
	end 'weigh'

	function deeper(w Tally) returns Tally
		self.holder.items = DeclArray.create()
		self.churn()
		return w + 1
	end 'deeper'

	function run() returns Tally
		var total = 0 as Tally

		for candidate in self.theHolder().items 'eachCandidate'
			let w = self.weigh(candidate.weight)
			print("{candidate.name} {w}\n")
			total = total + w
		end 'eachCandidate'

		return total
	end 'run'
end 'Driver'

function main() returns ExitCode
	var d = Driver.create()
	d.fill()
	let t = d.run()
	print("total {t}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
50
first 0 2
50
second 1 3
total 5
```

<!-- test: a-borrowed-field-survives-its-root-handed-to-a-consuming-constructor -->
### A consuming argument co-owns the root, so a field read out of it stays alive
`Holder.create(b)` stores `b` and takes a reference of its own; `b` keeps its own, so `inner` names a live record.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

type Holder
	export var box as Outer

	static function create(box Outer) returns Self
		return Self{box: box}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let b = Outer.create(1)
	let inner = b.inner
	let h = Holder.create(b)
	print("{inner.label.byteLength()} {h.box.inner.label.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
52 52
```

<!-- test: a-borrowed-field-survives-the-co-owner-of-its-root-letting-go -->
### The record a consuming constructor co-owns outlives that co-owner
Replacing `h.box` releases the holder's reference; `b`'s own still keeps the record and the field `inner` names.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

type Holder
	export var box as Outer

	static function create(box Outer) returns Self
		return Self{box: box}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var b = Outer.create(1)
	let inner = b.inner
	var h = Holder.create(b)
	h.box = Outer.create(5)
	print("{inner.label.byteLength()} {h.box.inner.label.byteLength()} {b.inner.label.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
52 52 52
```

<!-- test: a-borrowed-field-survives-the-field-its-root-was-stored-into-being-replaced -->
### A field store co-owns the root, so replacing that field frees nothing a field read names
`h.box = b` takes a reference of its own, so replacing `h.box` again releases only that one.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

type Holder
	export var box as Outer

	static function create(box Outer) returns Self
		return Self{box: box}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var b = Outer.create(1)
	let inner = b.inner
	var h = Holder.create(Outer.create(0))
	h.box = b
	h.box = Outer.create(5)
	print("{inner.label.byteLength()} {b.inner.label.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
52 52
```

<!-- test: a-borrowed-field-survives-its-root-pushed-into-a-cleared-container -->
### A container store co-owns the root, so clearing the container frees nothing a field read names
`xs.push(b)` takes a reference of its own, so clearing `xs` releases only that one.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

typealias Outers = Array with Outer

function main() returns ExitCode
	var b = Outer.create(1)
	let inner = b.inner
	var xs = Outers.create()
	xs.push(b)
	xs.clear()
	print("{inner.label.byteLength()} {xs.count()} {b.inner.label.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
52 0 52
```

<!-- test: a-captured-borrowed-field-survives-the-co-owner-of-its-root-letting-go -->
### A closure holding a field read out of the root still reads a live record after the root's co-owner lets go
The closure reads `inner` after the holder lets go of its reference to `b`'s record, which `b` still keeps.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

type Holder
	export var box as Outer

	static function create(box Outer) returns Self
		return Self{box: box}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var b = Outer.create(1)
	let inner = b.inner
	let peek = function() gives inner.label.byteLength()
	var h = Holder.create(b)
	h.box = Outer.create(5)
	print("{peek()} {h.box.inner.label.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
52 52
```

<!-- test: a-field-read-survives-a-move-of-its-root-into-a-nested-block -->
### The binding a root moves into is dropped at its block's end, and a field read out of the root survives
`let r = b` moves the record into `r`, which is dropped at the end of `nested`; `inner` holds its own reference.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

function main() returns ExitCode
	var b = Outer.create(1)
	let inner = b.inner

	if inner.label.byteLength() > 0 'nested'
		let r = b
		print("[{r.inner.label}]\n")
	end 'nested'

	print("[{inner.label}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[an inner label padded long enough to heap allocate 1]
[an inner label padded long enough to heap allocate 1]
```

<!-- test: a-field-read-survives-a-match-give-of-its-root -->
### A match arm that gives the root away, to a binding then rebound
The `first` arm moves `b` into the match's result, which `r = Outer.create(3)` then releases; `inner` holds its own reference.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

enum Pick
	first
	second
end 'Pick'

function choose(n Integer) returns Pick
	return Pick.first if n > 0 else Pick.second
end 'choose'

function main() returns ExitCode
	var b = Outer.create(1)
	let inner = b.inner
	var r = match choose(1) 'pick'
		first gives b
		second gives Outer.create(2)
	end 'pick'
	r = Outer.create(3)
	print("[{inner.label}] [{r.inner.label}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[an inner label padded long enough to heap allocate 1] [an inner label padded long enough to heap allocate 3]
```

<!-- test: a-field-read-survives-a-rebind-of-its-root -->
### Rebinding the root leaves a field read out of it alive
Rebinding `b` releases `b`'s reference to the record; `inner` holds its own.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

function main() returns ExitCode
	var b = Outer.create(1)
	let inner = b.inner
	b = Outer.create(2)
	print("[{inner.label}] [{b.inner.label}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[an inner label padded long enough to heap allocate 1] [an inner label padded long enough to heap allocate 2]
```

<!-- test: a-field-read-survives-a-rebind-of-the-binding-its-root-moved-into -->
### A reassignment that moves the root, then a rebind of the binding it moved into
`c = b` moves the record into `c`, and rebinding `c` releases that reference; `inner` holds its own.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

function main() returns ExitCode
	var b = Outer.create(1)
	let inner = b.inner
	var c = Outer.create(2)
	c = b
	c = Outer.create(3)
	print("[{inner.label}] [{c.inner.label}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[an inner label padded long enough to heap allocate 1] [an inner label padded long enough to heap allocate 3]
```

<!-- test: an-element-survives-a-move-of-its-array-into-a-nested-block -->
### The binding an array moves into is dropped at its block's end, and an element read out of it survives
`let r = xs` moves the array into `r`, which is dropped at the end of `nested`; `s` holds its own reference.
```maxon
typealias Words = Array with String

function main() returns ExitCode
	var xs = Words.create()
	xs.push("a word padded out long enough to heap allocate {1}")
	let s = try xs.get(0) otherwise ""

	if s.byteLength() > 0 'nested'
		let r = xs
		print("{r.count()}\n")
	end 'nested'

	print("{s.byteLength()} [{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
48 [a word padded out long enough to heap allocate 1]
```

<!-- test: an-element-survives-a-match-give-of-its-array -->
### A match arm that gives an array away, to a binding then cleared
The `first` arm moves `xs` into the match's result, which `r.clear()` then empties; `s` holds its own reference.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Words = Array with String

enum Pick
	first
	second
end 'Pick'

function choose(n Integer) returns Pick
	return Pick.first if n > 0 else Pick.second
end 'choose'

function main() returns ExitCode
	var xs = Words.create()
	xs.push("a word padded out long enough to heap allocate {1}")
	let s = try xs.get(0) otherwise ""
	var r = match choose(1) 'pick'
		first gives xs
		second gives Words.create()
	end 'pick'
	r.clear()
	print("{s.byteLength()} {r.count()} [{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
48 0 [a word padded out long enough to heap allocate 1]
```

<!-- test: an-element-survives-a-clear-of-the-binding-its-array-moved-into -->
### A reassignment that moves an array, then a clear of the binding it moved into
`c = xs` moves the array into `c`, and clearing `c` releases the array's reference to the element; `s`
holds its own.
```maxon
typealias Words = Array with String

function main() returns ExitCode
	var xs = Words.create()
	xs.push("a word padded out long enough to heap allocate {1}")
	let s = try xs.get(0) otherwise ""
	var c = Words.create()
	c = xs
	c.clear()
	print("{s.byteLength()} {c.count()} [{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
48 0 [a word padded out long enough to heap allocate 1]
```

<!-- test: a-closure-capturing-a-borrowed-field-is-copied-into-an-outer-binding -->
### A closure capturing a field read holds a reference of its own
`g` outlives `nested` and the rebind of `b`; the closure's environment keeps `inner` alive.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

function main() returns ExitCode
	var b = Outer.create(1)
	let inner = b.inner
	var g = function() gives Inner.create(0).label.byteLength()

	if inner.label.byteLength() > 0 'nested'
		let peek = function() gives inner.label.byteLength()
		g = peek
	end 'nested'

	b = Outer.create(22)
	print("{g()} {b.inner.label.byteLength()}\n")
	return 0
end 'main'
```
```stdout
52 53
```

<!-- test: a-closure-capturing-a-borrowed-field-is-returned -->
### A closure capturing a field read of a local can be returned
`b` is released when `reader` returns; the closure's environment still holds `inner`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

typealias Reader = function() returns Integer

function reader() returns Reader
	let b = Outer.create(1)
	let inner = b.inner
	return function() gives inner.label.byteLength() as Integer
end 'reader'

function main() returns ExitCode
	let r = reader()
	print("{r()}\n")
	return 0
end 'main'
```
```stdout
52
```

<!-- test: a-closure-capturing-a-borrowed-field-is-stored-in-a-field -->
### A closure capturing a field read of a local can be stored in a record
`b` is released when `fill` returns; the stored closure still holds `inner`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

typealias Reader = function() returns Integer

type Slot
	export var read as Reader

	static function create(read Reader) returns Self
		return Self{read: read}
	end 'create'
end 'Slot'

function fill(slot Slot)
	let b = Outer.create(1)
	let inner = b.inner
	slot.read = function() gives inner.label.byteLength() as Integer
end 'fill'

function zero() returns Integer
	return 0
end 'zero'

function main() returns ExitCode
	var s = Slot.create(zero)
	fill(s)
	print("{s.read()}\n")
	return 0
end 'main'
```
```stdout
52
```

<!-- test: a-closure-capturing-a-borrowed-field-is-handed-to-a-function-that-keeps-it -->
### A closure capturing a field read of a local can be handed to a callee that stores it
`Slot.create` keeps the closure past `build`, which releases `b`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

typealias Reader = function() returns Integer

type Slot
	export var read as Reader

	static function create(read Reader) returns Self
		return Self{read: read}
	end 'create'
end 'Slot'

function build() returns Slot
	let b = Outer.create(1)
	let inner = b.inner
	return Slot.create(function() gives inner.label.byteLength() as Integer)
end 'build'

function main() returns ExitCode
	let s = build()
	print("{s.read()}\n")
	return 0
end 'main'
```
```stdout
52
```

<!-- test: a-closure-capturing-a-borrowed-field-is-run-by-a-coroutine-after-its-block -->
<!-- unsupported-targets: wasm32-wasi -->
### A coroutine runs a closure capturing a field read after the closure's block
The coroutine owns the closure, whose environment holds `inner` past the rebind of `b`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

typealias Reader = function() returns Integer

function later(read Reader) returns Integer
	Scheduler.yield()
	return read()
end 'later'

function nothing() returns Integer
	Scheduler.yield()
	return 0
end 'nothing'

function main() returns ExitCode
	var b = Outer.create(1)
	let inner = b.inner
	var p = async nothing()

	if inner.label.byteLength() > 0 'nested'
		let peek = function() gives inner.label.byteLength() as Integer
		p = async later(peek)
	end 'nested'

	b = Outer.create(22)
	print("{await p} {b.inner.label.byteLength()}\n")
	return 0
end 'main'
```
```stdout
52 53
```

<!-- test: a-borrowed-field-follows-its-root-into-a-binding-of-the-same-block -->
### A move of the root into a binding of the same block
`r` owns the record from `let r = b` until the end of `main`, which outlives every read of `inner`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

function main() returns ExitCode
	var b = Outer.create(1)
	let inner = b.inner
	let r = b
	print("{inner.label.byteLength()} {r.inner.label.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
52 52
```

<!-- test: a-closure-argument-holds-a-borrowed-field-only-to-the-end-of-its-block -->
### A closure handed to a call inside a block, then a rebind of the root after it
`apply` calls the closure inside `nested`, and the root is rebound after the block has ended.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var label as String

	static function create(n Integer) returns Self
		return Self{label: "an inner label padded long enough to heap allocate {n}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create(n Integer) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Outer'

typealias Reader = function() returns Integer

function apply(read Reader) returns Integer
	return read()
end 'apply'

function main() returns ExitCode
	var b = Outer.create(1)
	let inner = b.inner

	if inner.label.byteLength() > 0 'nested'
		print("{apply(function() gives inner.label.byteLength() as Integer)}\n")
	end 'nested'

	b = Outer.create(22)
	print("{b.inner.label.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
52
53
```

<!-- test: an-element-survives-a-clear-through-a-second-binding-of-its-array -->
### An element survives a clear reached through a SECOND binding of the same array
`Holder.create(x)` stores the record `x` names, so `h.items` and `x` are one array. The clear through
`h.items` empties `x` too, and `s` keeps the element it read.
```maxon
typealias StringArray = Array with String

type Holder
	export var items as StringArray

	static function create(items StringArray) returns Holder
		return Holder{items: items}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var x = StringArray.create()
	x.push("hello {40 + 2} world, long enough for the heap")
	var h = Holder.create(x)
	let s = try x.get(0) otherwise ""
	h.items.clear()
	print("[{s}] {x.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello 42 world, long enough for the heap] 0
```

<!-- test: an-element-survives-a-holder-growing-and-clearing-the-shared-array -->
### … and a method of the second holder that grows the array past many reallocations, then clears it
The element was read before the holder existed; it is neither a slot of the buffer the pushes
reallocate nor released by the clear.
```maxon
typealias Words = Array with String

type Holder
	var items as Words

	static function create(items Words) returns Self
		return Self{items: items}
	end 'create'

	function grow()
		for i in 0 upto 300 'g'
			items.push("holder-filler-{i}-pppppppppppppppp")
		end 'g'

		items.clear()
	end 'grow'
end 'Holder'

function main() returns ExitCode
	var arr = ["first {1} padded out long enough for the heap", "second {2} padded out long enough for the heap"]
	let s = try arr.get(0) otherwise "none"
	var h = Holder.create(arr)
	h.grow()
	print("[{s}] {s.byteLength()} {arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[first 1 padded out long enough for the heap] 43 0
```

<!-- test: an-interpolated-field-read-survives-a-later-part-reassigning-the-field -->
### A field read as an interpolation part survives a later part that reassigns the field
`{self.name}` is read before `self.rename()` runs; the part holds its own reference, so the
reassignment's release of the old name leaves the text being built intact.
```maxon
type Person
	var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'

	function current() returns String
		return name
	end 'current'

	function rename() returns String
		self.name = "renamed {40 + 2} value long enough for a heap record"
		return "done"
	end 'rename'

	function describe() returns String
		return "{self.name} {self.rename()}"
	end 'describe'
end 'Person'

function main() returns ExitCode
	var p = Person.create("original {40 + 2} value long enough for a heap record")
	let text = p.describe()
	print("[{text}]\n")
	print("[{p.current()}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original 42 value long enough for a heap record done]
[renamed 42 value long enough for a heap record]
```

<!-- test: a-field-read-as-a-left-operand-survives-the-right-operand-reassigning-the-field -->
### A field read as a comparison's LEFT operand survives a right operand that reassigns the field
`self.name` is read before `self.swapName()` runs; the comparison sees the original name, which
equals the text `swapName` returns.
```maxon
type Person
	var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'

	function current() returns String
		return name
	end 'current'

	function swapName() returns String
		self.name = "swapped {40 + 2} value long enough for a heap record"
		return "original {40 + 2} value long enough for a heap record"
	end 'swapName'

	function keptOriginal() returns bool
		return self.name == self.swapName()
	end 'keptOriginal'
end 'Person'

function main() returns ExitCode
	var p = Person.create("original {40 + 2} value long enough for a heap record")
	print("{p.keptOriginal()}\n")
	print("[{p.current()}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
[swapped 42 value long enough for a heap record]
```

<!-- test: an-element-read-after-an-indirect-call-survives-a-set-of-its-slot -->
### An element read after a call through a function value survives a `set` of the slot it came from
`cb()` may do anything, so it ends what a caller can know about `items`; the reads after it are counted
just the same. `set` releases the slot's reference to the record `e` read, and `e` keeps its own; the
`filler` allocated after the write would take over a freed record.
```maxon
type Box
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Box'

typealias Boxes = Array with Box
typealias Hook = function()

function announce()
	print("hook\n")
end 'announce'

function replaceFirst(items Boxes, cb Hook)
	cb()
	view e = try items.get(0) otherwise panic("the caller pushed one")
	try items.set(0, value: Box.create("replacement {7} padded out long enough for the heap")) otherwise panic("the caller pushed one")
	let filler = Box.create("filler {9} padded out long enough for the heap")
	view now = try items.get(0) otherwise panic("the caller pushed one")
	print("[{e.label}] [{now.label}] [{filler.label}]\n")
end 'replaceFirst'

function main() returns ExitCode
	var xs = Boxes.create()
	xs.push(Box.create("original {40 + 2} padded out long enough for the heap"))
	replaceFirst(xs, cb: announce)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hook
[original 42 padded out long enough for the heap] [replacement 7 padded out long enough for the heap] [filler 9 padded out long enough for the heap]
```

<!-- test: a-raw-cursor-current-survives-a-clear-of-its-buffer -->
<!-- tier: stdlib -->
### A value read through a raw buffer cursor survives a `clear` of the buffer
The raw cursor's `current()` is an element read like `get`: it counts what it returns, so the buffer's
`clear` releases only the buffer's reference.
```maxon
function main() returns ExitCode
	var xs = ["alpha {40 + 2} value long enough for a heap record"]
	view c = try xs.managed.createCursor() otherwise return 1
	let s = c.current()
	xs.managed.clear()
	let filler = "filler {9} padded out long enough for the heap"
	print("[{s}] {xs.count()} [{filler}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha 42 value long enough for a heap record] 0 [filler 9 padded out long enough for the heap]
```

<!-- test: a-field-read-through-a-var-field-of-self-survives-a-rebind-of-that-field -->
### A value read through a `var` field of `self` survives the method rebinding that field
`self.inner.name` passes through `inner`, a `var` field the method then replaces; `n` holds its own
reference to the name, so the store releases only the old `Inner`'s.
```maxon
type Inner
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(name String) returns Self
		return Self{inner: Inner.create(name)}
	end 'create'

	function nameAfterRebind() returns String
		let n = self.inner.name
		self.inner = Inner.create("replacement {7} padded out long enough for the heap")
		let filler = "filler {1} padded out long enough for the heap"
		return "[{n}] [{self.inner.name}] [{filler}]"
	end 'nameAfterRebind'
end 'Outer'

function main() returns ExitCode
	var a = Outer.create("first {40 + 2} padded out long enough for the heap")
	print("{a.nameAfterRebind()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[first 42 padded out long enough for the heap] [replacement 7 padded out long enough for the heap] [filler 1 padded out long enough for the heap]
```

<!-- test: a-field-read-through-a-var-field-of-a-parameter-survives-a-rebind-of-that-field -->
### … and through a `var` field of a parameter
```maxon
type Inner
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(name String) returns Self
		return Self{inner: Inner.create(name)}
	end 'create'
end 'Outer'

function nameAfterRebind(o Outer) returns String
	let n = o.inner.name
	o.inner = Inner.create("replacement {8} padded out long enough for the heap")
	let filler = "filler {2} padded out long enough for the heap"
	return "[{n}] [{o.inner.name}] [{filler}]"
end 'nameAfterRebind'

function main() returns ExitCode
	var b = Outer.create("second {40 + 3} padded out long enough for the heap")
	print("{nameAfterRebind(b)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[second 43 padded out long enough for the heap] [replacement 8 padded out long enough for the heap] [filler 2 padded out long enough for the heap]
```

<!-- test: an-argument-read-through-a-var-field-survives-the-callee-rebinding-that-field -->
### … and as an argument, beside the record the callee rebinds the field of
`c.inner.name` is read through the `var` field `inner` and handed to `clobber` beside `c`; `clobber`
replaces `o.inner`, and `name` still holds its own reference.
```maxon
type Inner
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(name String) returns Self
		return Self{inner: Inner.create(name)}
	end 'create'
end 'Outer'

function clobber(o Outer, name String) returns String
	o.inner = Inner.create("replacement {9} padded out long enough for the heap")
	let filler = "filler {3} padded out long enough for the heap"
	return "[{name}] [{o.inner.name}] [{filler}]"
end 'clobber'

function main() returns ExitCode
	var c = Outer.create("third {40 + 4} padded out long enough for the heap")
	print("{clobber(c, name: c.inner.name)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[third 44 padded out long enough for the heap] [replacement 9 padded out long enough for the heap] [filler 3 padded out long enough for the heap]
```

<!-- test: a-field-write-target-survives-its-right-hand-side-rebinding-the-chain -->
### A field write's target survives a right-hand side that rebinds the record it writes into
`x.inner` is read as the write's target before `x.dropInner()` runs; the store lands in the `Inner` it
read, which `dropInner` has already replaced, so the new `x.inner` keeps its own name.
```maxon
type Inner
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(name String) returns Self
		return Self{inner: Inner.create(name)}
	end 'create'

	function dropInner() returns String
		self.inner = Inner.create("replacement {7} padded out long enough for the heap")
		return "stored {5} padded out long enough for the heap"
	end 'dropInner'
end 'Outer'

function main() returns ExitCode
	var x = Outer.create("original {40 + 2} padded out long enough for the heap")
	x.inner.name = x.dropInner()
	let filler = Inner.create("filler {1} padded out long enough for the heap")
	let filler2 = Inner.create("filler {2} padded out long enough for the heap")
	print("[{x.inner.name}] [{filler.name}] [{filler2.name}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[replacement 7 padded out long enough for the heap] [filler 1 padded out long enough for the heap] [filler 2 padded out long enough for the heap]
```

<!-- test: a-two-hop-field-write-target-survives-its-right-hand-side-rebinding-the-middle-hop -->
### … and through two hops, when the right-hand side rebinds the middle one
```maxon
type Leaf
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Leaf'

type Middle
	export var leaf as Leaf

	static function create(name String) returns Self
		return Self{leaf: Leaf.create(name)}
	end 'create'
end 'Middle'

type Outer
	export var middle as Middle

	static function create(name String) returns Self
		return Self{middle: Middle.create(name)}
	end 'create'

	function dropLeaf() returns String
		self.middle.leaf = Leaf.create("replacement {7} padded out long enough for the heap")
		return "stored {5} padded out long enough for the heap"
	end 'dropLeaf'
end 'Outer'

function main() returns ExitCode
	var x = Outer.create("original {40 + 2} padded out long enough for the heap")
	x.middle.leaf.name = x.dropLeaf()
	let filler = Leaf.create("filler {1} padded out long enough for the heap")
	let filler2 = Leaf.create("filler {2} padded out long enough for the heap")
	print("[{x.middle.leaf.name}] [{filler.name}] [{filler2.name}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[replacement 7 padded out long enough for the heap] [filler 1 padded out long enough for the heap] [filler 2 padded out long enough for the heap]
```

<!-- test: a-field-write-rooted-at-a-module-var-survives-its-right-hand-side-rebinding-the-var -->
### … and from a module-level `var`, when the right-hand side rebinds the `var`
```maxon
type Inner
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(name String) returns Self
		return Self{inner: Inner.create(name)}
	end 'create'
end 'Outer'

var current = Outer.create("placeholder")

function rebindCurrent() returns String
	current = Outer.create("replacement {7} padded out long enough for the heap")
	return "stored {5} padded out long enough for the heap"
end 'rebindCurrent'

function main() returns ExitCode
	current = Outer.create("original {40 + 2} padded out long enough for the heap")
	current.inner.name = rebindCurrent()
	let filler = Inner.create("filler {1} padded out long enough for the heap")
	let filler2 = Outer.create("filler {2} padded out long enough for the heap")
	print("[{current.inner.name}] [{filler.name}] [{filler2.inner.name}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[replacement 7 padded out long enough for the heap] [filler 1 padded out long enough for the heap] [filler 2 padded out long enough for the heap]
```

<!-- test: a-union-payload-read-survives-a-later-payload-reassigning-the-field -->
### A field read as a union payload survives a later payload that reassigns the field
`x.label` is read before `x.reset()` runs; the payload holds its own reference, so the reassignment's
release of the old label leaves the case's first payload intact.
```maxon
union Pair
	both(first String, second String)
	none
end 'Pair'

type Holder
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'

	function reset() returns String
		self.label = "replacement {7} padded out long enough for the heap"
		return "second {5} padded out long enough for the heap"
	end 'reset'
end 'Holder'

function describe(p Pair) returns String
	return match p 'm'
		both(first, second) gives "[{first}] [{second}]"
		none gives "none"
	end 'm'
end 'describe'

function main() returns ExitCode
	var x = Holder.create("original {40 + 2} padded out long enough for the heap")
	let p = Pair.both(x.label, second: x.reset())
	let filler = "filler {1} padded out long enough for the heap"
	print("{describe(p)} [{x.label}] [{filler}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original 42 padded out long enough for the heap] [second 5 padded out long enough for the heap] [replacement 7 padded out long enough for the heap] [filler 1 padded out long enough for the heap]
```

<!-- test: a-struct-literal-field-read-survives-a-later-field-reassigning-it -->
### … as a struct literal's field, beside a later field that reassigns it
```maxon
type Holder
	var first as String
	var second as String

	static function create(label String) returns Self
		return Self{first: label, second: "unset"}
	end 'create'

	function reset() returns String
		self.first = "replacement {7} padded out long enough for the heap"
		return "second {5} padded out long enough for the heap"
	end 'reset'

	function snapshot() returns Self
		return Self{first: self.first, second: self.reset()}
	end 'snapshot'

	function describe() returns String
		return "[{self.first}] [{self.second}]"
	end 'describe'
end 'Holder'

function main() returns ExitCode
	var x = Holder.create("original {40 + 2} padded out long enough for the heap")
	let copy = x.snapshot()
	let filler = "filler {1} padded out long enough for the heap"
	print("{copy.describe()} {x.describe()} [{filler}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original 42 padded out long enough for the heap] [second 5 padded out long enough for the heap] [replacement 7 padded out long enough for the heap] [unset] [filler 1 padded out long enough for the heap]
```

<!-- test: a-tuple-element-read-survives-a-later-element-reassigning-the-field -->
### … as a tuple element, beside a later element that reassigns it
```maxon
type Holder
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'

	function reset() returns String
		self.label = "replacement {7} padded out long enough for the heap"
		return "second {5} padded out long enough for the heap"
	end 'reset'
end 'Holder'

function main() returns ExitCode
	var x = Holder.create("original {40 + 2} padded out long enough for the heap")
	let pair = (x.label, x.reset())
	let filler = "filler {1} padded out long enough for the heap"
	print("[{pair.0}] [{pair.1}] [{x.label}] [{filler}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original 42 padded out long enough for the heap] [second 5 padded out long enough for the heap] [replacement 7 padded out long enough for the heap] [filler 1 padded out long enough for the heap]
```

<!-- test: an-indirect-call-argument-read-survives-a-later-argument-reassigning-the-field -->
### … as an argument of a call through a function value, beside a later argument that reassigns it
```maxon
typealias Joiner = function(String, String) returns String

type Holder
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'

	function reset() returns String
		self.label = "replacement {7} padded out long enough for the heap"
		return "second {5} padded out long enough for the heap"
	end 'reset'
end 'Holder'

function join(first String, second String) returns String
	let filler = "filler {1} padded out long enough for the heap"
	return "[{first}] [{second}] [{filler}]"
end 'join'

function combine(x Holder, f Joiner) returns String
	return f(x.label, x.reset())
end 'combine'

function main() returns ExitCode
	var x = Holder.create("original {40 + 2} padded out long enough for the heap")
	print("{combine(x, f: join)} [{x.label}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original 42 padded out long enough for the heap] [second 5 padded out long enough for the heap] [filler 1 padded out long enough for the heap] [replacement 7 padded out long enough for the heap]
```

<!-- test: an-async-argument-read-survives-a-later-argument-reassigning-the-field -->
### … as an argument of an `async` call, beside a later argument that reassigns it
```maxon
type Holder
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'

	function reset() returns String
		self.label = "replacement {7} padded out long enough for the heap"
		return "second {5} padded out long enough for the heap"
	end 'reset'
end 'Holder'

function join(first String, second String) returns String
	let filler = "filler {1} padded out long enough for the heap"
	return "[{first}] [{second}] [{filler}]"
end 'join'

function main() returns ExitCode
	var x = Holder.create("original {40 + 2} padded out long enough for the heap")
	let p = async join(x.label, second: x.reset())
	let text = await p
	print("{text} [{x.label}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original 42 padded out long enough for the heap] [second 5 padded out long enough for the heap] [filler 1 padded out long enough for the heap] [replacement 7 padded out long enough for the heap]
```

<!-- test: a-service-send-argument-read-survives-a-later-argument-reassigning-the-field -->
### … as an argument of a message to a service, beside a later argument that reassigns it
```maxon
type Holder
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'

	function reset() returns String
		self.label = "replacement {7} padded out long enough for the heap"
		return "second {5} padded out long enough for the heap"
	end 'reset'
end 'Holder'

type Joiner
	var seen as String

	static function create() returns Self
		return Self{seen: "none"}
	end 'create'

	export function take(first String, second String) returns String
		let filler = "filler {1} padded out long enough for the heap"
		return "[{first}] [{second}] [{filler}]"
	end 'take'
end 'Joiner'

function main() returns ExitCode
	let h = spawn Joiner.create()
	var x = Holder.create("original {40 + 2} padded out long enough for the heap")
	let text = try await h.take(x.label, second: x.reset()) otherwise "failed"
	print("{text} [{x.label}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original 42 padded out long enough for the heap] [second 5 padded out long enough for the heap] [filler 1 padded out long enough for the heap] [replacement 7 padded out long enough for the heap]
```

<!-- test: a-field-read-as-an-equals-receiver-survives-its-argument-reassigning-the-field -->
### A field read as the receiver of `equals` survives an argument that reassigns the field
`self.name` is read before `self.swapName()` runs; the comparison sees the original name, which differs
from the text `swapName` returns.
```maxon
type Person
	var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'

	function current() returns String
		return name
	end 'current'

	function swapName() returns String
		self.name = "swapped {40 + 2} value long enough for a heap record"
		return "returned {40 + 2} value long enough for a heap record"
	end 'swapName'

	function keptOriginal() returns bool
		return self.name.equals(self.swapName())
	end 'keptOriginal'
end 'Person'

function main() returns ExitCode
	var p = Person.create("original {40 + 2} value long enough for a heap record")
	print("{p.keptOriginal()}\n")
	let filler = "filler {1} value long enough for a heap record"
	print("[{p.current()}] [{filler}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
false
[swapped 42 value long enough for a heap record] [filler 1 value long enough for a heap record]
```

<!-- test: an-indirect-call-last-argument-read-survives-the-callee-reassigning-the-field -->
### A field read as the last argument of a call through a function value survives the callee reassigning the field
`x.label` is the last argument, so no later argument frees it; the callee does, by resetting
`h.label` before it reads `s`. The argument holds its own reference, so `s` is still the original label.
```maxon
typealias Reader = function(Holder, String) returns String

type Holder
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'

	function reset()
		self.label = "replacement {7} padded out long enough for the heap"
	end 'reset'
end 'Holder'

function clobberThenRead(h Holder, s String) returns String
	h.reset()
	let filler = "filler {1} padded out long enough for the heap"
	return "[{s}] [{filler}]"
end 'clobberThenRead'

function readThrough(x Holder, f Reader) returns String
	return f(x, x.label)
end 'readThrough'

function main() returns ExitCode
	var x = Holder.create("original {40 + 2} padded out long enough for the heap")
	print("{readThrough(x, f: clobberThenRead)} [{x.label}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original 42 padded out long enough for the heap] [filler 1 padded out long enough for the heap] [replacement 7 padded out long enough for the heap]
```

<!-- test: a-generic-field-read-as-a-tuple-element-survives-a-later-element-reassigning-it -->
### A field of an opaque type read as a tuple element survives a later element that reassigns it
`self.item` is a `T`, instantiated here at `String`; it is read before `self.store` replaces it, and the
tuple keeps the original.
```maxon
type Box uses T
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function store(replacement T) returns bool
		self.item = replacement
		return true
	end 'store'

	function snapshot(replacement T) returns (T, bool)
		return (self.item, self.store(replacement))
	end 'snapshot'

	function current() returns T
		return self.item
	end 'current'
end 'Box'

typealias StringBox = Box with String

function main() returns ExitCode
	var b = StringBox.create("original {40 + 2} padded out long enough for the heap")
	view pair = b.snapshot("replacement {7} padded out long enough for the heap")
	let filler = "filler {1} padded out long enough for the heap"
	print("[{pair.0}] [{pair.1}] [{b.current()}] [{filler}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original 42 padded out long enough for the heap] [true] [replacement 7 padded out long enough for the heap] [filler 1 padded out long enough for the heap]
```

<!-- test: a-map-literal-key-read-survives-its-value-reassigning-the-field -->
### A field read as a map literal's key survives its value reassigning the field
```maxon
type Holder
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'

	function reset() returns String
		self.label = "replacement {7} padded out long enough for the heap"
		return "second {5} padded out long enough for the heap"
	end 'reset'
end 'Holder'

function main() returns ExitCode
	var x = Holder.create("original {40 + 2} padded out long enough for the heap")
	let m = [x.label: x.reset()]
	let filler = "filler {1} padded out long enough for the heap"
	let found = try m.get("original {40 + 2} padded out long enough for the heap") otherwise "missing"
	print("[{found}] [{x.label}] [{filler}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[second 5 padded out long enough for the heap] [replacement 7 padded out long enough for the heap] [filler 1 padded out long enough for the heap]
```

<!-- test: an-array-push-receiver-read-survives-its-argument-reassigning-the-field -->
### A field read as the receiver of `push` survives an argument that reassigns the field
```maxon
typealias StringArray = Array with String

type Holder
	export var items as StringArray

	static function create() returns Self
		var items = StringArray.create()
		items.push("original {40 + 2} padded out long enough for the heap")
		return Self{items: items}
	end 'create'

	function reset() returns String
		var fresh = StringArray.create()
		fresh.push("replacement {7} padded out long enough for the heap")
		self.items = fresh
		return "pushed {5} padded out long enough for the heap"
	end 'reset'
end 'Holder'

function main() returns ExitCode
	var x = Holder.create()
	x.items.push(x.reset())
	var filler = StringArray.create()
	filler.push("filler {1} padded out long enough for the heap")
	filler.push("filler {2} padded out long enough for the heap")
	print("{x.items.count()} [{try x.items.get(0) otherwise "none"}] [{try filler.get(0) otherwise "none"}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 [replacement 7 padded out long enough for the heap] [filler 1 padded out long enough for the heap]
```

<!-- test: an-interface-field-read-as-a-struct-literal-field-survives-a-later-field-reassigning-it -->
### An interface-typed field read as a struct literal's field survives a later field that reassigns it
```maxon
interface Named
	function name() returns String
end 'Named'

type Tag
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Tag'

extension Tag implements Named
	function name() returns String
		return self.text
	end 'name'
end 'Tag'

type Holder
	var shown as Named
	var note as String

	static function create(text String) returns Self
		return Self{shown: Tag.create(text), note: "unset"}
	end 'create'

	function reset() returns String
		self.shown = Tag.create("replacement {7} padded out long enough for the heap")
		return "second {5} padded out long enough for the heap"
	end 'reset'

	function snapshot() returns Self
		return Self{shown: self.shown, note: self.reset()}
	end 'snapshot'

	function describe() returns String
		return "[{self.shown.name()}] [{self.note}]"
	end 'describe'
end 'Holder'

function main() returns ExitCode
	var x = Holder.create("original {40 + 2} padded out long enough for the heap")
	view copy = x.snapshot()
	let filler = Tag.create("filler {1} padded out long enough for the heap")
	print("{copy.describe()} {x.describe()} [{filler.name()}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original 42 padded out long enough for the heap] [second 5 padded out long enough for the heap] [replacement 7 padded out long enough for the heap] [unset] [filler 1 padded out long enough for the heap]
```

<!-- test: a-service-send-interface-let-field-read-through-a-var-field-survives-a-later-argument-rebinding-it -->
### An interface-typed `let` field read through a `var` field as a service message's argument survives a later argument that rebinds the `var` field
```maxon
interface Named
	function name() returns String
end 'Named'

type Tag
	let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Tag'

extension Tag implements Named
	function name() returns String
		return self.text
	end 'name'
end 'Tag'

type Inner
	export let shape as Named

	static function create(text String) returns Self
		return Self{shape: Tag.create(text)}
	end 'create'
end 'Inner'

type Holder
	export var inner as Inner

	static function create(text String) returns Self
		return Self{inner: Inner.create(text)}
	end 'create'

	function reset() returns String
		self.inner = Inner.create("replacement {7} padded out long enough for the heap")
		return "second {5} padded out long enough for the heap"
	end 'reset'
end 'Holder'

type Joiner
	var seen as String

	static function create() returns Self
		return Self{seen: "none"}
	end 'create'

	export function take(first Named, second String) returns String
		let filler = Tag.create("filler {1} padded out long enough for the heap")
		return "[{first.name()}] [{second}] [{filler.name()}]"
	end 'take'
end 'Joiner'

function main() returns ExitCode
	let h = spawn Joiner.create()
	var x = Holder.create("original {40 + 2} padded out long enough for the heap")
	let text = try await h.take(x.inner.shape, second: x.reset()) otherwise "failed"
	print("{text} [{x.inner.shape.name()}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original 42 padded out long enough for the heap] [second 5 padded out long enough for the heap] [filler 1 padded out long enough for the heap] [replacement 7 padded out long enough for the heap]
```

<!-- test: an-async-last-argument-read-survives-a-default-argument-rebinding-its-storage -->
### An `async` call's last argument survives a default argument that rebinds the storage it was read from
`current.label` is the last argument written, but `second`'s default runs after it and replaces `current`.
```maxon
type Holder
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Holder'

var current = Holder.create("placeholder")

function rebindCurrent() returns String
	current = Holder.create("replacement {7} padded out long enough for the heap")
	return "second {5} padded out long enough for the heap"
end 'rebindCurrent'

function join(first String, second String = rebindCurrent()) returns String
	let filler = "filler {1} padded out long enough for the heap"
	return "[{first}] [{second}] [{filler}]"
end 'join'

function main() returns ExitCode
	current = Holder.create("original {40 + 2} padded out long enough for the heap")
	let p = async join(current.label)
	let text = await p
	print("{text} [{current.label}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original 42 padded out long enough for the heap] [second 5 padded out long enough for the heap] [filler 1 padded out long enough for the heap] [replacement 7 padded out long enough for the heap]
```

<!-- test: a-bound-read-only-interface-field-is-shared-with-a-service-as-its-struct-twin-is -->
### A `let` interface field bound through a `var` root is shared with a service, as the struct-typed twin below is
```maxon
interface Named
	function name() returns String
end 'Named'

type Tag
	let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Tag'

extension Tag implements Named
	function name() returns String
		return self.text
	end 'name'
end 'Tag'

type Inner
	export let shape as Named

	static function create(text String) returns Self
		return Self{shape: Tag.create(text)}
	end 'create'
end 'Inner'

type Holder
	export var inner as Inner

	static function create(text String) returns Self
		return Self{inner: Inner.create(text)}
	end 'create'

	function reset() returns String
		self.inner = Inner.create("replacement {7} padded out long enough for the heap")
		return "second {5} padded out long enough for the heap"
	end 'reset'
end 'Holder'

type Joiner
	var seen as String

	static function create() returns Self
		return Self{seen: "none"}
	end 'create'

	export function take(first Named, second String) returns String
		return "[{first.name()}] [{second}]"
	end 'take'
end 'Joiner'

function main() returns ExitCode
	let h = spawn Joiner.create()
	var x = Holder.create("original {40 + 2} padded out long enough for the heap")
	let v = x.inner.shape
	let text = try await h.take(v, second: x.reset()) otherwise "failed"
	print("{text} [{x.inner.shape.name()}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original 42 padded out long enough for the heap] [second 5 padded out long enough for the heap] [replacement 7 padded out long enough for the heap]
```

<!-- test: a-bound-read-only-struct-field-is-shared-with-a-service -->
### … the struct-typed twin
```maxon
type Tag
	let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'

	function name() returns String
		return self.text
	end 'name'
end 'Tag'

type Inner
	export let shape as Tag

	static function create(text String) returns Self
		return Self{shape: Tag.create(text)}
	end 'create'
end 'Inner'

type Holder
	export var inner as Inner

	static function create(text String) returns Self
		return Self{inner: Inner.create(text)}
	end 'create'

	function reset() returns String
		self.inner = Inner.create("replacement {7} padded out long enough for the heap")
		return "second {5} padded out long enough for the heap"
	end 'reset'
end 'Holder'

type Joiner
	var seen as String

	static function create() returns Self
		return Self{seen: "none"}
	end 'create'

	export function take(first Tag, second String) returns String
		return "[{first.name()}] [{second}]"
	end 'take'
end 'Joiner'

function main() returns ExitCode
	let h = spawn Joiner.create()
	var x = Holder.create("original {40 + 2} padded out long enough for the heap")
	let v = x.inner.shape
	let text = try await h.take(v, second: x.reset()) otherwise "failed"
	print("{text} [{x.inner.shape.name()}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original 42 padded out long enough for the heap] [second 5 padded out long enough for the heap] [replacement 7 padded out long enough for the heap]
```

<!-- test: a-merged-tuple-copy-of-a-module-let-field-is-writable -->
### A trivial tuple read out of a module `let` and copied at a merge is a fresh, writable record
```maxon
typealias Num = int(i64.min to i64.max)

type Pairing
	export var pair as (Num, Num)

	static function create() returns Self
		return Self{pair: (1, 2)}
	end 'create'
end 'Pairing'

let Shared = Pairing.create()

function fallback() returns (Num, Num)
	return (7, 8)
end 'fallback'

function pick(c bool) returns Num
	var t = Shared.pair if c else fallback()
	t.1 = 99
	return t.1
end 'pick'

function main() returns ExitCode
	let n = pick(true)
	print("t.1={n} shared.1={Shared.pair.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
t.1=99 shared.1=2
```

<!-- test: a-merged-tuple-copy-of-a-let-field-is-writable -->
### A trivial tuple read out of a `let` field and copied at a merge is a fresh, writable record
```maxon
typealias Num = int(i64.min to i64.max)

type Pairing
	export let pair as (Num, Num)

	static function create() returns Self
		return Self{pair: (1, 2)}
	end 'create'
end 'Pairing'

function fallback() returns (Num, Num)
	return (7, 8)
end 'fallback'

function pick(p Pairing, c bool) returns Num
	var t = p.pair if c else fallback()
	t.1 = 99
	return t.1
end 'pick'

function main() returns ExitCode
	let p = Pairing.create()
	let n = pick(p, c: true)
	print("t.1={n} p.1={p.pair.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
t.1=99 p.1=2
```

<!-- test: a-merged-tuple-copy-of-a-module-let-field-reassigned-is-writable -->
### … and assigned to an existing `var` rather than declaring one
```maxon
typealias Num = int(i64.min to i64.max)

type Pairing
	export var pair as (Num, Num)

	static function create() returns Self
		return Self{pair: (1, 2)}
	end 'create'
end 'Pairing'

let Shared = Pairing.create()

function fallback() returns (Num, Num)
	return (7, 8)
end 'fallback'

function pick(c bool) returns Num
	var t = fallback()
	t = Shared.pair if c else fallback()
	t.1 = 99
	return t.1
end 'pick'

function main() returns ExitCode
	let n = pick(true)
	print("t.1={n} shared.1={Shared.pair.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
t.1=99 shared.1=2
```

<!-- test: a-let-field-array-through-a-merge-into-a-writing-callee-is-refused -->
### A `let` field's array handed through a merge to a callee that writes it is refused, as the bare read is
```maxon
typealias Count = int(0 to 1000)
typealias CountArray = Array with Count

type Holder
	export let items as CountArray

	static function create() returns Self
		return Self{items: [1]}
	end 'create'
end 'Holder'

function grow(a CountArray)
	a.push(2)
end 'grow'

function main() returns ExitCode
	let h = Holder.create()
	var other = CountArray.create()
	let c = h.items.count() == 1
	grow(h.items if c else other)
	print("{h.items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:21:2: cannot pass 'h', which is read-only (a `let` binding), to a function that writes parameter 'a' (in main); pass a clone()
```

<!-- test: a-generic-async-last-argument-read-survives-a-default-argument-rebinding-its-storage -->
### A generic function's `async` last argument survives a default argument that rebinds the storage it was read from
```maxon
type Holder
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Holder'

var current = Holder.create("placeholder")

function rebindCurrent() returns String
	current = Holder.create("replacement {7} padded out long enough for the heap")
	return "second {5} padded out long enough for the heap"
end 'rebindCurrent'

function join(first T, second String = rebindCurrent()) uses T returns T where T is Comparable
	let filler = "filler {1} padded out long enough for the heap"

	if filler.byteLength() == 0 'never'
		print(second)
	end 'never'

	return first
end 'join'

function main() returns ExitCode
	current = Holder.create("original {40 + 2} padded out long enough for the heap")
	let p = async join(current.label)
	let text = await p
	print("[{text}] [{current.label}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original 42 padded out long enough for the heap] [replacement 7 padded out long enough for the heap]
```

<!-- test: a-let-fields-record-merged-into-a-var-is-not-writable -->
### A `let` field's record merged into a `var` is not a writable name for it
```maxon
typealias Num = int(i64.min to i64.max)

type Inner
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create(1)}
	end 'create'
end 'Outer'

function fresh() returns Inner
	return Inner.create(7)
end 'fresh'

function main() returns ExitCode
	let h = Outer.create()
	let c = h.inner.n == 1
	var t = h.inner if c else fresh()
	t.n = 5
	print("{h.inner.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:27:6: cannot assign a value, which may share storage with a `let` field, to mutable binding 't'; use 'let' instead of 'var', or use clone()
```

<!-- test: a-module-let-fields-record-returned-through-a-merge-is-not-writable -->
### A module `let`'s field record returned through a merge is not writable by the caller
```maxon
typealias Num = int(i64.min to i64.max)

type Inner
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create(1)}
	end 'create'
end 'Outer'

let Shared = Outer.create()

function fresh() returns Inner
	return Inner.create(7)
end 'fresh'

function pick(c bool) returns Inner
	let v = Shared.inner if c else fresh()
	return v
end 'pick'

function main() returns ExitCode
	var r = pick(true)
	r.n = 5
	print("{Shared.inner.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:32:6: cannot assign a value, which may share storage with a `let` field, to mutable binding 'r'; use 'let' instead of 'var', or use clone()
```

<!-- test: a-let-fields-record-returned-through-a-merge-is-not-writable -->
### A `let` field's record returned through a merge is not writable by the caller
```maxon
typealias Num = int(i64.min to i64.max)

type Inner
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create(1)}
	end 'create'
end 'Outer'

function fresh() returns Inner
	return Inner.create(7)
end 'fresh'

function pick(h Outer, c bool) returns Inner
	return h.inner if c else fresh()
end 'pick'

function main() returns ExitCode
	let h = Outer.create()
	var r = pick(h, c: true)
	r.n = 5
	print("{h.inner.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:30:6: cannot assign a value, which may share storage with a `let` field, to mutable binding 'r'; use 'let' instead of 'var', or use clone()
```

<!-- test: a-let-fields-record-held-as-a-struct-literal-field-into-a-var-field-is-refused -->
### A `let` field's record held across a struct literal and stored into a `var` field is refused, as the bare store is
```maxon
typealias Num = int(i64.min to i64.max)

type Inner
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create(1)}
	end 'create'
end 'Outer'

function label() returns String
	return "x"
end 'label'

type Pair
	export var first as Inner
	export var label as String

	static function from(h Outer) returns Self
		return Self{first: h.inner, label: label()}
	end 'from'
end 'Pair'

function main() returns ExitCode
	let h = Outer.create()
	var p = Pair.from(h)
	p.first.n = 5
	print("{h.inner.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:29:15: cannot store a value read out of a `let` field into mutable place 'Pair.first'; use clone()
```

<!-- test: a-let-fields-string-merged-into-a-var-is-a-writable-copy -->
### A `let` field's `String` merged into a `var` is a copy the `var` may grow
```maxon
type Holder
	export let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Holder'

function fresh() returns String
	return "fresh {7}"
end 'fresh'

function pick(h Holder, c bool) returns String
	return h.text if c else fresh()
end 'pick'

function main() returns ExitCode
	let h = Holder.create("original {42}")
	let c = h.text.byteLength() > 0
	var s = h.text if c else fresh()
	s.append("!")
	var r = pick(h, c: c)
	r.append("?")
	print("{s} {r} {h.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
original 42! original 42? original 42
```

<!-- test: a-module-let-record-merged-into-a-let-is-readable-and-passed-on -->
### A module `let`'s record merged into a `let` reads and passes to a reading callee
```maxon
typealias Num = int(i64.min to i64.max)

type Inner
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create(1)}
	end 'create'
end 'Outer'

let Shared = Outer.create()

function fresh() returns Inner
	return Inner.create(7)
end 'fresh'

function show(i Inner) returns Num
	return i.n
end 'show'

function pick(c bool) returns Inner
	return Shared.inner if c else fresh()
end 'pick'

function main() returns ExitCode
	let c = Shared.inner.n == 1
	let v = Shared.inner if c else fresh()
	let w = pick(c)
	print("{show(v)} {show(w)} {show(pick(not c))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1 7
```

<!-- test: let-field-string-rebound-into-a-by-reference-var-may-grow -->
### Review: a `let` field's `String` rebound into a by-reference `var` is a copy the `var` may grow
```maxon
type Holder
	export let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Holder'

function replace(s String)
	s = "{s}+"
end 'replace'

function main() returns ExitCode
	let h = Holder.create("original {42}")
	var s = "start {0}"
	replace(s)
	s = h.text
	s.append("!")
	replace(s)
	print("{s} {h.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
original 42!+ original 42
```

<!-- test: module-let-string-rebound-into-a-by-reference-var-may-grow -->
### Review: a module `let`'s `String` field rebound into a by-reference `var` is a copy the `var` may grow
```maxon
type Holder
	export let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Holder'

let Shared = Holder.create("original 42")

function replace(s String)
	s = "{s}+"
end 'replace'

function main() returns ExitCode
	var s = "start {0}"
	replace(s)
	s = Shared.text
	s.append("!")
	replace(s)
	print("{s} {Shared.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
original 42!+ original 42
```

<!-- test: let-field-immutable-record-rebound-into-a-by-reference-var-may-be-rebound -->
### Review: a `let` field's immutable record rebound into a by-reference `var` may be rebound by the callee
```maxon
typealias Num = int(i64.min to i64.max)

type Point
	export let n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Point'

type Outer
	export let inner as Point

	static function create() returns Self
		return Self{inner: Point.create(1)}
	end 'create'
end 'Outer'

function bump(p Point)
	p = Point.create(p.n + 1)
end 'bump'

function main() returns ExitCode
	let h = Outer.create()
	var q = Point.create(0)
	bump(q)
	q = h.inner
	bump(q)
	print("{q.n} {h.inner.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 1
```

<!-- test: module-let-immutable-record-rebound-into-a-by-reference-var-may-be-rebound -->
### Review: a module `let`'s immutable record rebound into a by-reference `var` may be rebound by the callee
```maxon
typealias Num = int(i64.min to i64.max)

type Point
	export let n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Point'

let Origin = Point.create(1)

function bump(p Point)
	p = Point.create(p.n + 1)
end 'bump'

function main() returns ExitCode
	var q = Point.create(0)
	bump(q)
	q = Origin
	bump(q)
	print("{q.n} {Origin.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 1
```

<!-- test: a-var-cannot-take-a-let-fields-writable-record-so-no-by-reference-callee-writes-through-it -->
### A `var` is refused a `let` field's writable record at the rebind, before any by-reference callee could write through it
```maxon
typealias Num = int(i64.min to i64.max)

type Point
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Point'

type Outer
	export let inner as Point

	static function create() returns Self
		return Self{inner: Point.create(1)}
	end 'create'
end 'Outer'

function overwrite(p Point)
	p.n = 9
	p = Point.create(p.n + 1)
end 'overwrite'

function main() returns ExitCode
	let h = Outer.create()
	var q = Point.create(0)
	overwrite(q)
	q = h.inner
	overwrite(q)
	print("{q.n} {h.inner.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:29:2: cannot assign a value read out of a `let` field to mutable binding 'q'; use 'let' instead of 'var', or use clone()
```

<!-- test: module-let-merged-into-a-var-then-a-field-bound-survives-the-var-rebinding -->
### Review r8: a field of a `var` merged from a module `let` or a fresh record survives the `var` being rebound
```maxon
type Item
	export let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Item'

type Wrap
	export let item as Item

	static function create(text String) returns Self
		return Self{item: Item.create(text)}
	end 'create'
end 'Wrap'

let Origin = Wrap.create("origin")

function main() returns ExitCode
	let c = Origin.item.text.byteLength() == 99
	var w = Origin if c else Wrap.create("fresh {7} padded out long enough for the heap")
	let e = w.item
	w = Wrap.create("replacement {8} padded out long enough for the heap")
	print("{e.text} {w.item.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
fresh 7 padded out long enough for the heap replacement 8 padded out long enough for the heap
```

<!-- test: by-reference-var-whose-later-arm-stores-a-module-let-then-a-field-bound-survives-the-rebind -->
### Review r8: a by-reference `var` whose LATER arm stores a module `let`'s record keeps a bound field alive across a rebind
```maxon
type Item
	export let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Item'

type Wrap
	export let item as Item

	static function create(text String) returns Self
		return Self{item: Item.create(text)}
	end 'create'
end 'Wrap'

let Origin = Wrap.create("origin")

function rebind(w Wrap)
	w = Wrap.create("rebound {3} padded out long enough for the heap")
end 'rebind'

function main() returns ExitCode
	let c = Origin.item.text.byteLength() == 6
	var w = Wrap.create("start {0} padded out long enough for the heap")
	rebind(w)

	if c 'fresh'
		w = Wrap.create("fresh {7} padded out long enough for the heap")
	end 'fresh' else 'shared'
		w = Origin
	end 'shared'

	let e = w.item
	w = Wrap.create("replacement {8} padded out long enough for the heap")
	print("{e.text} {w.item.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
fresh 7 padded out long enough for the heap replacement 8 padded out long enough for the heap
```

<!-- test: by-reference-var-whose-later-arm-stores-a-module-let-in-a-loop-then-a-field-bound-survives-the-rebind -->
### Review r8: … and when the stores sit in a loop body
```maxon
type Item
	export let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Item'

type Wrap
	export let item as Item

	static function create(text String) returns Self
		return Self{item: Item.create(text)}
	end 'create'
end 'Wrap'

let Origin = Wrap.create("origin")

function rebind(w Wrap)
	w = Wrap.create("rebound {3} padded out long enough for the heap")
end 'rebind'

function main() returns ExitCode
	var w = Wrap.create("start {0} padded out long enough for the heap")
	rebind(w)

	for i in 0 upto 2 'each'
		if i == 1 'last'
			w = Wrap.create("fresh {7} padded out long enough for the heap")
		end 'last' else 'first'
			w = Origin
		end 'first'
	end 'each'

	let e = w.item
	w = Wrap.create("replacement {8} padded out long enough for the heap")
	print("{e.text} {w.item.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
fresh 7 padded out long enough for the heap replacement 8 padded out long enough for the heap
```

<!-- test: let-field-string-in-one-arm-of-a-by-reference-var-grown-by-a-callee -->
### Review r8: a `let` field's `String` stored into a by-reference `var` on the LATER arm is a copy a callee may grow
```maxon
type Holder
	export let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Holder'

function grow(s String)
	s.append("+")
end 'grow'

function main() returns ExitCode
	let h = Holder.create("original {42} padded out long enough for the heap")
	let c = h.text.byteLength() == 0
	var s = "start {0}"
	grow(s)

	if c 'fresh'
		s = "fresh {7}"
	end 'fresh' else 'shared'
		s = h.text
	end 'shared'

	grow(s)
	print("{s} | {h.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
original 42 padded out long enough for the heap+ | original 42 padded out long enough for the heap
```

<!-- test: let-field-string-in-the-earlier-arm-of-a-by-reference-var-grown-by-a-callee -->
### Review r8: … and on the EARLIER arm, with a clean store after it
```maxon
type Holder
	export let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Holder'

function grow(s String)
	s.append("+")
end 'grow'

function main() returns ExitCode
	let h = Holder.create("original {42} padded out long enough for the heap")
	let c = h.text.byteLength() > 0
	var s = "start {0}"
	grow(s)

	if c 'shared'
		s = h.text
	end 'shared' else 'fresh'
		s = "fresh {7}"
	end 'fresh'

	grow(s)
	print("{s} | {h.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
original 42 padded out long enough for the heap+ | original 42 padded out long enough for the heap
```

<!-- test: module-let-string-in-the-earlier-arm-of-a-by-reference-var-grown-by-a-callee -->
### Review r8: a module `let`'s `String` on the EARLIER arm of a by-reference `var`, grown by a callee
```maxon
type Holder
	export let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Holder'

let Shared = Holder.create("original 42 padded out long enough for the heap")

function grow(s String)
	s.append("+")
end 'grow'

function main() returns ExitCode
	let c = Shared.text.byteLength() > 0
	var s = "start {0}"
	grow(s)

	if c 'shared'
		s = Shared.text
	end 'shared' else 'fresh'
		s = "fresh {7}"
	end 'fresh'

	grow(s)
	print("{s} | {Shared.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
original 42 padded out long enough for the heap+ | original 42 padded out long enough for the heap
```

<!-- test: control-two-fresh-records-merged-into-a-var-then-a-field-bound-survives-the-var-rebinding -->
### Review r8: control — two fresh records merged into a `var`, a bound field survives the `var` being rebound
```maxon
type Item
	export let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Item'

type Wrap
	export let item as Item

	static function create(text String) returns Self
		return Self{item: Item.create(text)}
	end 'create'
end 'Wrap'

function main() returns ExitCode
	let probe = Wrap.create("origin")
	let c = probe.item.text.byteLength() == 99
	var w = Wrap.create("other {1} padded out long enough for the heap") if c else Wrap.create("fresh {7} padded out long enough for the heap")
	let e = w.item
	w = Wrap.create("replacement {8} padded out long enough for the heap")
	print("{e.text} {w.item.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
fresh 7 padded out long enough for the heap replacement 8 padded out long enough for the heap
```

<!-- test: module-let-rebound-into-a-var-under-an-if-then-a-field-bound-survives-the-var-rebinding -->
### Review r8: a `var` rebound to a module `let`'s record under an `if`, a bound field survives the `var` being rebound
```maxon
type Item
	export let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Item'

type Wrap
	export let item as Item

	static function create(text String) returns Self
		return Self{item: Item.create(text)}
	end 'create'
end 'Wrap'

let Origin = Wrap.create("origin")

function main() returns ExitCode
	let c = Origin.item.text.byteLength() == 99
	var w = Wrap.create("fresh {7} padded out long enough for the heap")

	if c 'shared'
		w = Origin
	end 'shared'

	let e = w.item
	w = Wrap.create("replacement {8} padded out long enough for the heap")
	print("{e.text} {w.item.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
fresh 7 padded out long enough for the heap replacement 8 padded out long enough for the heap
```

<!-- test: module-let-writable-record-on-one-arm-of-a-by-reference-var-is-refused -->
### Review r8: a module `let`'s writable record stored into a by-reference `var` on the EARLIER arm is refused at the store
```maxon
typealias Num = int(i64.min to i64.max)

type Point
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Point'

let Origin = Point.create(1)

function overwrite(p Point)
	p.n = 9
	p = Point.create(p.n + 1)
end 'overwrite'

function main() returns ExitCode
	let c = Origin.n == 1
	var q = Point.create(0)
	overwrite(q)

	if c 'shared'
		q = Origin
	end 'shared' else 'fresh'
		q = Point.create(2)
	end 'fresh'

	overwrite(q)
	print("{q.n} {Origin.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:25:3: cannot assign a value read out of a module-level `let` to mutable binding 'q'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-payload-record-on-one-arm-of-a-by-reference-var-is-refused -->
### Review r8: a `let` union payload's writable record stored into a by-reference `var` on the EARLIER arm is refused at the store
```maxon
typealias Num = int(i64.min to i64.max)

type Point
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Point'

union Slot
	full(point Point)
	empty
end 'Slot'

type Holder
	export let slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(Point.create(1))}
	end 'create'
end 'Holder'

function overwrite(p Point)
	p.n = 9
	p = Point.create(p.n + 1)
end 'overwrite'

function main() returns ExitCode
	let h = Holder.create()
	var q = Point.create(0)
	overwrite(q)

	match h.slot 'slot'
		full(point) then q = point
		empty then q = Point.create(2)
	end 'slot'

	overwrite(q)

	match h.slot 'after'
		full(point) then print("{q.n} {point.n}\n")
		empty then print("empty\n")
	end 'after'

	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:36:20: cannot assign a value read out of a `let` field to mutable binding 'q'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-payload-record-on-the-later-arm-of-a-by-reference-var-is-refused -->
### Review r8: … and on the LATER arm, with the clean store before it
```maxon
typealias Num = int(i64.min to i64.max)

type Point
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Point'

union Slot
	full(point Point)
	empty
end 'Slot'

type Holder
	export let slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(Point.create(1))}
	end 'create'
end 'Holder'

function overwrite(p Point)
	p.n = 9
	p = Point.create(p.n + 1)
end 'overwrite'

function main() returns ExitCode
	let h = Holder.create()
	var q = Point.create(0)
	overwrite(q)

	match h.slot 'slot'
		empty then q = Point.create(2)
		full(point) then q = point
	end 'slot'

	overwrite(q)

	match h.slot 'after'
		full(point) then print("{q.n} {point.n}\n")
		empty then print("empty\n")
	end 'after'

	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:37:20: cannot assign a value read out of a `let` field to mutable binding 'q'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-payload-record-into-a-plain-var-then-written -->
### Review r8: a `let` field's union payload record assigned to a plain `var` in a `match` arm, then written through the `var`
```maxon
typealias Num = int(i64.min to i64.max)

type Point
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Point'

union Slot
	full(point Point)
	empty
end 'Slot'

type Holder
	export let slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(Point.create(1))}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	var q = Point.create(0)

	match h.slot 'slot'
		full(point) then q = point
		empty then q = Point.create(2)
	end 'slot'

	q.n = 9

	match h.slot 'after'
		full(point) then print("{q.n} {point.n}\n")
		empty then print("empty\n")
	end 'after'

	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:30:20: cannot assign a value read out of a `let` field to mutable binding 'q'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-field-array-element-into-a-by-reference-var-written-by-a-callee -->
### Review r8: an element of a `let` field's array assigned to a by-reference `var` in a loop, then written by a callee
```maxon
typealias Num = int(i64.min to i64.max)

type Point
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Point'

typealias Points = Array with Point

type Holder
	export let points as Points

	static function create() returns Self
		var points = Points.create()
		points.push(Point.create(1))
		return Self{points: points}
	end 'create'
end 'Holder'

function overwrite(p Point)
	p.n = 9
	p = Point.create(p.n + 1)
end 'overwrite'

function main() returns ExitCode
	let h = Holder.create()
	var q = Point.create(0)
	overwrite(q)

	for point in h.points 'each'
		q = point
	end 'each'

	overwrite(q)
	let first = try h.points.get(0) otherwise panic("one point")
	print("{q.n} {first.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:35:3: cannot assign a value read out of a `let` field to mutable binding 'q'; use 'let' instead of 'var', or use clone()
```

<!-- test: view-of-a-let-field-into-a-by-reference-var-written-by-a-callee -->
### Review r8: a `view` of a `let` field's record assigned to a by-reference `var`, then written by a callee
```maxon
typealias Num = int(i64.min to i64.max)

type Point
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Point'

type Holder
	export let inner as Point

	static function create() returns Self
		return Self{inner: Point.create(1)}
	end 'create'
end 'Holder'

function overwrite(p Point)
	p.n = 9
	p = Point.create(p.n + 1)
end 'overwrite'

function main() returns ExitCode
	let h = Holder.create()
	var q = Point.create(0)
	overwrite(q)
	view v = h.inner
	q = v
	overwrite(q)
	print("{q.n} {h.inner.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:30:2: cannot assign a value read out of a `let` field to mutable binding 'q'; use 'let' instead of 'var', or use clone()
```

<!-- test: match-merge-of-a-module-let-and-a-fresh-record-field-survives-the-rebind -->
### Review r9: a field of a `var` merged by a `match` from a module `let` or a fresh record survives the `var` being rebound
```maxon
enum Pick
	first
	second
end 'Pick'

type Item
	export let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Item'

type Wrap
	export let item as Item

	static function create(text String) returns Self
		return Self{item: Item.create(text)}
	end 'create'
end 'Wrap'

let Origin = Wrap.create("origin")

function choose(fresh bool) returns Pick
	return Pick.second if fresh else Pick.first
end 'choose'

function main() returns ExitCode
	let c = Origin.item.text.byteLength() == 6
	var w = match choose(c) 'pick'
		first gives Origin
		second gives Wrap.create("fresh {7} padded out long enough for the heap")
	end 'pick'
	let e = w.item
	w = Wrap.create("replacement {8} padded out long enough for the heap")
	print("{e.text} {w.item.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
fresh 7 padded out long enough for the heap replacement 8 padded out long enough for the heap
```

<!-- test: try-otherwise-merge-of-a-fresh-record-and-a-module-let-field-survives-the-rebind -->
### Review r9: a field of a `var` merged by `try … otherwise` from a fresh record or a module `let` survives the `var` being rebound
```maxon
enum Boom implements Error
	bad
end 'Boom'

type Item
	export let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Item'

type Wrap
	export let item as Item

	static function create(text String) returns Self
		return Self{item: Item.create(text)}
	end 'create'
end 'Wrap'

let Origin = Wrap.create("origin")

function make(fail bool) returns Wrap throws Boom
	if fail 'boom'
		throw Boom.bad
	end 'boom'

	return Wrap.create("fresh {7} padded out long enough for the heap")
end 'make'

function main() returns ExitCode
	let c = Origin.item.text.byteLength() == 99
	var w = try make(c) otherwise Origin
	let e = w.item
	w = Wrap.create("replacement {8} padded out long enough for the heap")
	print("{e.text} {w.item.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
fresh 7 padded out long enough for the heap replacement 8 padded out long enough for the heap
```

<!-- test: ternary-of-two-module-lets-field-survives-the-rebind -->
### Review r9: a field of a `var` merged from two module `let`s survives the `var` being rebound
```maxon
type Item
	export let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Item'

type Wrap
	export let item as Item

	static function create(text String) returns Self
		return Self{item: Item.create(text)}
	end 'create'
end 'Wrap'

let Origin = Wrap.create("origin")
let Other = Wrap.create("other")

function main() returns ExitCode
	let c = Origin.item.text.byteLength() == 6
	var w = Origin if c else Other
	let e = w.item
	w = Wrap.create("replacement {8} padded out long enough for the heap")
	print("{e.text} {w.item.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
origin replacement 8 padded out long enough for the heap
```

<!-- test: match-merge-of-a-module-let-into-a-var-then-written-is-refused -->
### Review r9: a `match` merge of a module `let`'s writable record into a `var`, then written through the `var`, is refused
```maxon
typealias Num = int(i64.min to i64.max)

enum Pick
	first
	second
end 'Pick'

type Point
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Point'

let Origin = Point.create(1)

function choose(fresh bool) returns Pick
	return Pick.second if fresh else Pick.first
end 'choose'

function main() returns ExitCode
	let c = Origin.n == 7
	var q = match choose(c) 'pick'
		first gives Origin
		second gives Point.create(2)
	end 'pick'
	q.n = 9
	print("{q.n} {Origin.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:25:6: cannot assign a value read out of a module-level `let` to mutable binding 'q'; use 'let' instead of 'var', or use clone()
```

<!-- test: try-otherwise-merge-of-a-module-let-into-a-var-then-written-is-refused -->
### Review r9: a `try … otherwise` merge of a module `let`'s writable record into a `var`, then written through the `var`, is refused
```maxon
typealias Num = int(i64.min to i64.max)

enum Boom implements Error
	bad
end 'Boom'

type Point
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Point'

let Origin = Point.create(1)

function make(fail bool) returns Point throws Boom
	if fail 'boom'
		throw Boom.bad
	end 'boom'

	return Point.create(2)
end 'make'

function main() returns ExitCode
	let c = Origin.n == 1
	var q = try make(c) otherwise Origin
	q.n = 9
	print("{q.n} {Origin.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:28:6: cannot assign a value read out of a module-level `let` to mutable binding 'q'; use 'let' instead of 'var', or use clone()
```

<!-- test: statement-match-rebinding-a-var-to-a-module-let-or-a-fresh-record-field-survives-the-rebind -->
### Review r9: a `var` rebound by a statement `match` to a module `let` or a fresh record, a bound field survives the `var` being rebound
```maxon
enum Pick
	first
	second
end 'Pick'

type Item
	export let text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Item'

type Wrap
	export let item as Item

	static function create(text String) returns Self
		return Self{item: Item.create(text)}
	end 'create'
end 'Wrap'

let Origin = Wrap.create("origin")

function choose(fresh bool) returns Pick
	return Pick.second if fresh else Pick.first
end 'choose'

function main() returns ExitCode
	let c = Origin.item.text.byteLength() == 6
	var w = Wrap.create("start {0} padded out long enough for the heap")

	match choose(c) 'pick'
		first then w = Origin
		second then w = Wrap.create("fresh {7} padded out long enough for the heap")
	end 'pick'

	let e = w.item
	w = Wrap.create("replacement {8} padded out long enough for the heap")
	print("{e.text} {w.item.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
fresh 7 padded out long enough for the heap replacement 8 padded out long enough for the heap
```

<!-- test: try-otherwise-merge-of-a-module-let-element-and-a-fresh-record-into-a-let-runs -->
### Review r10: the same merge into a `let` is accepted and reads the element on the OK edge and the fallback on the error edge
```maxon
typealias Num = int(i64.min to i64.max)

type Point
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Point'

let Origins = [Point.create(1)]

function pick(index ElementIndex) returns Num
	let found = try Origins.get(index) otherwise Point.create(2)
	return found.n
end 'pick'

function main() returns ExitCode
	print("{pick(0)} {pick(5)} {Origins.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2 1
```

<!-- test: for-in-element-of-a-module-let-array-into-a-var-then-written-is-refused -->
### Review r10: an element of a module `let` array bound by `for … in`, assigned to a `var` and written, is refused
```maxon
typealias Num = int(i64.min to i64.max)

type Point
	export var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Point'

let Origins = [Point.create(1)]

function main() returns ExitCode
	var q = Point.create(0)

	for point in Origins 'each'
		q = point
	end 'each'

	q.n = 9
	print("{q.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:18:3: cannot assign a value read out of a module-level `let` to mutable binding 'q'; use 'let' instead of 'var', or use clone()
```
