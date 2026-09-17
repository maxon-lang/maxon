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
read as closing this one.

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

	export function size() returns Count
		return self.n
	end 'size'
end 'Box'

extension Box
	export function doubled() returns Count
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

	export function size() returns Count
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
	export function doubled() returns Count
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
	var total = 0

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
	var total = 0

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
	var total = 0

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
	var total = 0

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
