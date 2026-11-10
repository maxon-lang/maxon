---
feature: end-labels
status: stable
keywords: [end, label, block, declaration, error, diagnostic]
category: diagnostics
---

## Documentation

### Overview

An `end` that carries a label must repeat the label its opener gave it. For a block — `if`, `else`, `while`,
`for`, `match`, a `try` block and an `otherwise` handler — that is the label written after the header. For a
declaration — `function`, `type`, `enum`, `union`, `interface` and `extension` — it is the declared name. A
mismatch is **E2008**, positioned at the `end`, so a label that names a different block or declaration is never
read as closing this one. A block's label also differs from every label that encloses it, the enclosing
declaration's name included: the repeat is **E2081**, positioned at the inner label, so an `end` names exactly
one open block. Blocks that do not enclose one another may share a label.

```maxon
function helper() returns ExitCode
	return 0
end 'helper'
```

## Tests

<!-- test: labels-that-repeat-their-openers-compile -->
Every construct closed by its own label, and a function `end` that carries none.
```maxon
typealias Count = int(0 to 100)

interface Sized
	function size() returns Count
end 'Sized'

type Box implements Sized
	export var n as Count

	static function create() returns Self
		return Self{n: 3}
	end 'create'

	function size() returns Count
		return self.n
	end 'size'
end 'Box'

extension Box
	function doubled() returns Count
		return self.n * 2
	end 'doubled'
end 'Box'

enum Color
	red
	green
end 'Color'

union Shape
	dot
	square(side Count)
end 'Shape'

function unlabelled() returns Count
	return 1
end

function main() returns ExitCode
	var total = unlabelled()
	let box = Box.create()

	if box.size() > 2 'big'
		total = total + box.doubled()
	end 'big' else 'small'
		total = total + 1
	end 'small'

	for i in 0 upto 2 'eachStep'
		total = total + i
	end 'eachStep'

	while total < 10 'grow'
		total = total + 1
	end 'grow'

	let color = Color.green

	match color 'pick'
		red then total = total + 1
		green then total = total + 2
	end 'pick'

	match Shape.square(2) 'shape'
		dot then total = total + 1
		square(side) then total = total + side
	end 'shape'

	return total
end 'main'
```
```exitcode
14
```

<!-- test: error.a-function-end-label-names-another-function -->
```maxon
function helper() returns ExitCode
	return 0
end 'helperMessage'

function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E2008: <fragment>:4:1: Mismatched end label: expected 'helper', got 'helperMessage'
```

<!-- test: error.a-method-end-label-names-another-method -->
```maxon
typealias Count = int(0 to 10)

type Box
	export var n as Count

	static function create() returns Self
		return Self{n: 1}
	end 'make'
end 'Box'

function main() returns ExitCode
	return Box.create().n as ExitCode
end 'main'
```
```maxoncstderr
error E2008: <fragment>:9:2: Mismatched end label: expected 'create', got 'make'
```

<!-- test: error.a-type-end-label-names-another-type -->
```maxon
typealias Count = int(0 to 10)

type Box
	export var n as Count

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Bag'

function main() returns ExitCode
	return Box.create().n as ExitCode
end 'main'
```
```maxoncstderr
error E2008: <fragment>:10:1: Mismatched end label: expected 'Box', got 'Bag'
```

<!-- test: error.an-enum-end-label-names-another-enum -->
```maxon
enum Color
	red
	green
end 'Colour'

function main() returns ExitCode
	return Color.green.ordinal as ExitCode
end 'main'
```
```maxoncstderr
error E2008: <fragment>:5:1: Mismatched end label: expected 'Color', got 'Colour'
```

<!-- test: error.a-union-end-label-names-another-union -->
```maxon
typealias Count = int(0 to 10)

union Shape
	dot
	square(side Count)
end 'Shapes'

function main() returns ExitCode
	match Shape.square(2) 'shape'
		dot then return 1
		square(side) then return side as ExitCode
	end 'shape'
end 'main'
```
```maxoncstderr
error E2008: <fragment>:7:1: Mismatched end label: expected 'Shape', got 'Shapes'
```

<!-- test: error.an-interface-end-label-names-another-interface -->
```maxon
typealias Count = int(0 to 10)

interface Sized
	function size() returns Count
end 'Size'

type Box implements Sized
	var n as Count

	static function create() returns Self
		return Self{n: 3}
	end 'create'

	function size() returns Count
		return self.n
	end 'size'
end 'Box'

function main() returns ExitCode
	return Box.create().size() as ExitCode
end 'main'
```
```maxoncstderr
error E2008: <fragment>:6:1: Mismatched end label: expected 'Sized', got 'Size'
```

<!-- test: error.an-extension-end-label-names-another-type -->
```maxon
typealias Count = int(0 to 10)

type Box
	var n as Count

	static function create() returns Self
		return Self{n: 3}
	end 'create'
end 'Box'

extension Box
	function doubled() returns Count
		return self.n * 2
	end 'doubled'
end 'Bag'

function main() returns ExitCode
	return Box.create().doubled() as ExitCode
end 'main'
```
```maxoncstderr
error E2008: <fragment>:16:1: Mismatched end label: expected 'Box', got 'Bag'
```

<!-- test: error.an-if-end-label-names-another-block -->
```maxon
function main() returns ExitCode
	var total = 0 as ExitCode

	if total == 0 'empty'
		total = 1
	end 'full'

	return total
end 'main'
```
```maxoncstderr
error E2008: <fragment>:7:2: Mismatched end label: expected 'empty', got 'full'
```

<!-- test: error.an-else-end-label-names-another-block -->
```maxon
function main() returns ExitCode
	var total = 0 as ExitCode

	if total == 1 'one'
		total = 2
	end 'one' else 'other'
		total = 3
	end 'one'

	return total
end 'main'
```
```maxoncstderr
error E2008: <fragment>:9:2: Mismatched end label: expected 'other', got 'one'
```

<!-- test: error.a-while-end-label-names-another-block -->
```maxon
function main() returns ExitCode
	var total = 0 as ExitCode

	while total < 3 'grow'
		total = total + 1
	end 'shrink'

	return total
end 'main'
```
```maxoncstderr
error E2008: <fragment>:7:2: Mismatched end label: expected 'grow', got 'shrink'
```

<!-- test: error.a-for-end-label-names-another-block -->
```maxon
function main() returns ExitCode
	var total = 0 as ExitCode

	for i in 0 upto 3 'eachStep'
		total = total + i
	end 'eachItem'

	return total
end 'main'
```
```maxoncstderr
error E2008: <fragment>:7:2: Mismatched end label: expected 'eachStep', got 'eachItem'
```

<!-- test: error.a-try-block-end-label-names-another-block -->
```maxon
typealias Count = int(0 to 10)

enum Oops implements Error
	bad
end 'Oops'

function risky(n Count) returns Count throws Oops
	if n > 5 'tooBig'
		throw Oops.bad
	end 'tooBig'

	return n
end 'risky'

function main() returns ExitCode
	try 'work'
		let n = risky(2)
		return n as ExitCode
	end 'job' otherwise (e) 'failed'
		match e 'kind'
			bad then return 1
		end 'kind'
	end 'failed'
end 'main'
```
```maxoncstderr
error E2008: <fragment>:20:2: Mismatched end label: expected 'work', got 'job'
```

<!-- test: error.an-otherwise-handler-end-label-names-another-block -->
```maxon
typealias Count = int(0 to 10)

enum Oops implements Error
	bad
end 'Oops'

function risky(n Count) returns Count throws Oops
	if n > 5 'tooBig'
		throw Oops.bad
	end 'tooBig'

	return n
end 'risky'

function main() returns ExitCode
	let n = try risky(2) otherwise 'failed'
		return 1
	end 'recovered'

	return n as ExitCode
end 'main'
```
```maxoncstderr
error E2008: <fragment>:19:2: Mismatched end label: expected 'failed', got 'recovered'
```

<!-- test: error.a-stray-closer-in-a-method-is-refused-where-it-stands -->
```maxon
typealias Integer = int(0 to 100)

type Box
	var v as Integer

	static function make(v Integer) returns Self
		return Self{v: v, w: 2}
	end 'make'

	function show()
		print("{self.v}")
	end 'noEnclosingType'
		print("y")
	end 'show'

	var w as Integer
end 'Box'

function main() returns ExitCode
	let b = Box.make(3)
	b.show()
	return 0
end 'main'
```
```maxoncstderr
error E2008: <fragment>:13:2: Mismatched end label: expected 'show', got 'noEnclosingType'
```

<!-- test: error.a-missing-closer-in-a-method-is-refused-where-the-next-one-stands -->
```maxon
typealias Integer = int(0 to 100)

type Box
	var v as Integer

	static function make(v Integer) returns Self
		return Self{v: v, w: 2}
	end 'make'

	function show()
		if self.v > 1 'big'
			print("{self.v}")
	end 'show'

	function other()
		print("o")
	end 'other'

	var w as Integer
end 'Box'

function main() returns ExitCode
	let b = Box.make(3)
	b.show()
	return 0
end 'main'
```
```maxoncstderr
error E2008: <fragment>:14:2: Mismatched end label: expected 'big', got 'show'
```

<!-- test: error.a-stray-closer-in-a-value-function-is-named-before-its-missing-return -->
```maxon
typealias Integer = int(0 to 100)

function helper(x Integer) returns Integer
	let y = x + 1
end 'noEnclosingType'
	return y
end 'helper'

function main() returns ExitCode
	return helper(1) as ExitCode
end 'main'
```
```maxoncstderr
error E2008: <fragment>:6:1: Mismatched end label: expected 'helper', got 'noEnclosingType'
```

<!-- test: error.another-files-syntax-error-is-reported-beside-a-misclosure -->
A misclosed block can swallow the declarations that follow it in its own file, so another file's error that
names one of them may be a consequence and is held back. An error that names none of them is that file's own,
and is reported beside the misclosure.
```maxon
// --- file: x.maxon
export function first() returns ExitCode
	if true 'check'
		return 1

	return 2
end 'first'

export function second() returns ExitCode
	return 3
end 'second'

// --- file: y.maxon
function main() returns ExitCode
	return first() + second() + helper()
end 'main'

// --- file: z.maxon
function helper() returns ExitCode
	return 1 + )
end 'helper'
```
```maxoncstderr
error E3071: <fragment>:7:2: unreachable code after 'return'
error E2004: <fragment>:21:13: Expected expression but got ')'
```

<!-- test: sibling-blocks-and-other-declarations-may-share-a-label -->
Blocks that do not enclose one another may reuse a label: a loop run twice, the arms of one `if` chain, a block
inside `main` named after another function, and one label in two methods of a type.
```maxon
typealias Count = int(0 to 100)

function sum(limit Count) returns Count
	var total = 0 as Count

	for i in 0 upto limit 'each'
		total = total + i
	end 'each'

	for j in 0 upto limit 'each'
		total = total + j
	end 'each'

	if total > 20 'check'
		total = total + 5
	end 'check' else if total > 10 'check'
		total = total + 1
	end 'check' else 'check'
		total = total + 3
	end 'check'

	return total
end 'sum'

type Meter
	var reading as Count

	static function create() returns Self
		return Self{reading: 2}
	end 'create'

	function up() returns Count
		if self.reading > 0 'step'
			return self.reading + 1
		end 'step'

		return 0
	end 'up'

	function down() returns Count
		if self.reading > 0 'step'
			return self.reading - 1
		end 'step'

		return 0
	end 'down'
end 'Meter'

function main() returns ExitCode
	let total = sum(4)
	let meter = Meter.create()

	if total == 13 'sum'
		return (total + meter.up() + meter.down()) as ExitCode
	end 'sum'

	return 1
end 'main'
```
```exitcode
17
```

<!-- test: error.a-block-reuses-its-functions-name -->
```maxon
function flipped(x ExitCode) returns ExitCode
	if x > 0 'flipped'
		return 1
	end 'flipped'

	return 0
end 'flipped'

function main() returns ExitCode
	return flipped(1)
end 'main'
```
```maxoncstderr
error E2081: <fragment>:3:11: Block label 'flipped' is already the label of an enclosing block or declaration (line 2, column 10); an `end 'flipped'` could close either, so rename the inner block's label
```

<!-- test: error.a-block-reuses-its-methods-name -->
```maxon
typealias Count = int(0 to 10)

type Box
	export var n as Count

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function bump() returns Count
		if self.n > 0 'bump'
			return 1
		end 'bump'

		return 0
	end 'bump'
end 'Box'

function main() returns ExitCode
	return Box.create().bump() as ExitCode
end 'main'
```
```maxoncstderr
error E2081: <fragment>:12:17: Block label 'bump' is already the label of an enclosing block or declaration (line 11, column 11); an `end 'bump'` could close either, so rename the inner block's label
```

<!-- test: error.a-block-in-a-method-reuses-its-types-name -->
```maxon
typealias Count = int(0 to 10)

type Box
	export var n as Count

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function bump() returns Count
		if self.n > 0 'Box'
			return 1
		end 'Box'

		return 0
	end 'bump'
end 'Box'

function main() returns ExitCode
	return Box.create().bump() as ExitCode
end 'main'
```
```maxoncstderr
error E2081: <fragment>:12:17: Block label 'Box' is already the label of an enclosing block or declaration (line 4, column 6); an `end 'Box'` could close either, so rename the inner block's label
```

<!-- test: error.a-block-in-a-union-method-reuses-the-unions-name -->
```maxon
typealias Count = int(0 to 10)

union Shade
	light
	dark

	function level() returns Count
		let seen = 1 as Count

		if seen > 0 'Shade'
			return 1
		end 'Shade'

		return 2
	end 'level'
end 'Shade'

function main() returns ExitCode
	return Shade.dark.level() as ExitCode
end 'main'
```
```maxoncstderr
error E2081: <fragment>:11:15: Block label 'Shade' is already the label of an enclosing block or declaration (line 4, column 7); an `end 'Shade'` could close either, so rename the inner block's label
```

<!-- test: error.a-block-in-an-enum-method-reuses-the-enums-name -->
```maxon
typealias Count = int(0 to 10)

enum Color
	red
	green

	function rank() returns Count
		if self == Color.red 'Color'
			return 1
		end 'Color'

		return 2
	end 'rank'
end 'Color'

function main() returns ExitCode
	return Color.green.rank() as ExitCode
end 'main'
```
```maxoncstderr
error E2081: <fragment>:9:24: Block label 'Color' is already the label of an enclosing block or declaration (line 4, column 6); an `end 'Color'` could close either, so rename the inner block's label
```

<!-- test: error.a-block-in-an-extension-method-reuses-the-extended-types-name -->
```maxon
typealias Count = int(0 to 10)

type Box
	var n as Count

	static function create() returns Self
		return Self{n: 3}
	end 'create'
end 'Box'

extension Box
	function doubled() returns Count
		if self.n > 0 'Box'
			return self.n * 2
		end 'Box'

		return 0
	end 'doubled'
end 'Box'

function main() returns ExitCode
	return Box.create().doubled() as ExitCode
end 'main'
```
```maxoncstderr
error E2081: <fragment>:14:17: Block label 'Box' is already the label of an enclosing block or declaration (line 12, column 11); an `end 'Box'` could close either, so rename the inner block's label
```

<!-- test: error.a-nested-block-reuses-an-outer-blocks-label -->
```maxon
function main() returns ExitCode
	var total = 0 as ExitCode

	while total < 3 'scan'
		if total == 1 'scan'
			total = total + 2
		end 'scan'

		total = total + 1
	end 'scan'

	return total
end 'main'
```
```maxoncstderr
error E2081: <fragment>:6:17: Block label 'scan' is already the label of an enclosing block or declaration (line 5, column 18); an `end 'scan'` could close either, so rename the inner block's label
```

<!-- test: error.a-label-reused-two-levels-down -->
```maxon
function main() returns ExitCode
	var total = 0 as ExitCode

	for i in 0 upto 2 'outer'
		while total < 3 'middle'
			if total == 1 'outer'
				total = total + 2
			end 'outer'

			total = total + i + 1
		end 'middle'
	end 'outer'

	return total
end 'main'
```
```maxoncstderr
error E2081: <fragment>:7:18: Block label 'outer' is already the label of an enclosing block or declaration (line 5, column 20); an `end 'outer'` could close either, so rename the inner block's label
```

<!-- test: error.a-match-reuses-an-enclosing-blocks-label -->
```maxon
function main() returns ExitCode
	var total = 0 as ExitCode

	while total < 3 'scan'
		match total 'scan'
			1 then total = 2
			default then total = total + 1
		end 'scan'
	end 'scan'

	return total
end 'main'
```
```maxoncstderr
error E2081: <fragment>:6:15: Block label 'scan' is already the label of an enclosing block or declaration (line 5, column 18); an `end 'scan'` could close either, so rename the inner block's label
```

<!-- test: error.an-else-branch-reuses-an-enclosing-blocks-label -->
```maxon
function main() returns ExitCode
	var total = 0 as ExitCode

	while total < 3 'scan'
		if total == 1 'first'
			total = total + 2
		end 'first' else 'scan'
			total = total + 1
		end 'scan'
	end 'scan'

	return total
end 'main'
```
```maxoncstderr
error E2081: <fragment>:8:20: Block label 'scan' is already the label of an enclosing block or declaration (line 5, column 18); an `end 'scan'` could close either, so rename the inner block's label
```

<!-- test: error.a-method-named-like-its-type -->
```maxon
typealias Count = int(0 to 10)

type Box
	var n as Count

	static function create() returns Self
		return Self{n: 3}
	end 'create'

	function Box() returns Count
		return self.n
	end 'Box'
end 'Box'

function main() returns ExitCode
	return Box.create().Box() as ExitCode
end 'main'
```
```maxoncstderr
error E2081: <fragment>:11:11: Block label 'Box' is already the label of an enclosing block or declaration (line 4, column 6); an `end 'Box'` could close either, so rename the inner block's label
```

<!-- test: error.a-reused-label-in-a-function-leaves-its-callers-unreported -->
The reuse is the only error: `remake` keeps its declared `mutable` result, so storing the result of `remake(source)` into the array is not refused beside it.
```maxon
typealias Tally = int(0 to 1024)
typealias BoxArray = Array with Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function remake(source Box) returns mutable Box
	if source.n > 0 'remake'
		return Box.create(source.n)
	end 'remake'

	return Box.create(0)
end 'remake'

function main() returns ExitCode
	let source = freeze Box.create(7)
	var copy = remake(source)
	var all = BoxArray.create(0)
	all.push(copy)

	return all.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2081: <fragment>:14:18: Block label 'remake' is already the label of an enclosing block or declaration (line 13, column 10); an `end 'remake'` could close either, so rename the inner block's label
```

<!-- test: error.every-reused-label-of-one-declaration-is-reported -->
```maxon
function main() returns ExitCode
	var total = 0 as ExitCode

	while total < 3 'scan'
		if total == 1 'scan'
			total = total + 2
		end 'scan'

		if total == 2 'scan'
			total = total + 1
		end 'scan'

		total = total + 1
	end 'scan'

	return total
end 'main'
```
```maxoncstderr
error E2081: <fragment>:6:17: Block label 'scan' is already the label of an enclosing block or declaration (line 5, column 18); an `end 'scan'` could close either, so rename the inner block's label
error E2081: <fragment>:10:17: Block label 'scan' is already the label of an enclosing block or declaration (line 5, column 18); an `end 'scan'` could close either, so rename the inner block's label
```
