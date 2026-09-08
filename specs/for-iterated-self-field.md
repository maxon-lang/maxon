---
feature: for-iterated-self-field
status: experimental
keywords: [for, iteration, lock, self, field, borrow, use-after-free]
category: control-flow
---

## Documentation

# Iterating a field of the enclosing `self`

A `for … in <array>` hands the body a **borrowed** element the array still owns, and re-reads the
array's length live in the loop header on every trip. So the iterated storage is made unwritable for
the body — a write that reallocates it, clears it, or drops the record entirely would leave both the
borrow and the header's next length read pointing at freed memory.

That storage has **one** identity, and this file is the test that keeps it that way. A field of the
enclosing type can be named **two** ways as a source (`items`, `self.items`) and **four** ways as a
write (`items = …`, `self.items = …`, `items.clear()`, `self.items.clear()`), which is eight programs
that must all reach the same verdict. They did not: the lock keyed the `self.`-spelled source on the
*receiver parameter* while the write doors keyed on the *field's alias*, and the store door did not
consult the lock at all. Four of the eight spellings compiled, and all four **segfaulted**
(0xC0000005), while their twins were correctly refused.

An **assignment** to the iterated storage is **E2013**, whichever way it is spelled — the same code and
the same wording the local spelling `arr = other` has always produced. A **mutating method call** on it
is **E3019**. Both blame the field, never the receiver: `self` is a parameter, and telling a reader to
make it writable names a change that does not work.

```text
for it in items 'scan'
    items = IntArray.create()   // E2013 — the loop is still walking the old record
end 'scan'
```

## Tests

<!-- test: iterated-self-field-bare-source-bare-assign -->
The bare source, the bare write.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type Bag
	export var items as IntArray

	static function create() returns Self
		var a = IntArray.create()
		a.push(5)
		a.push(7)
		return Self{items: a}
	end 'create'

	function sum() returns Int
		var total = 0
		for it in items 'scan'
			items = IntArray.create()
			total = total + it
		end 'scan'
		return total
	end 'sum'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/for-iterated-self-field/iterated-self-field-bare-source-bare-assign.test:18:4: cannot assign to immutable variable: 'items'
```

<!-- test: iterated-self-field-bare-source-explicit-assign -->
The bare source, the `self.`-spelled write — the same storage, so the same answer.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type Bag
	export var items as IntArray

	static function create() returns Self
		var a = IntArray.create()
		a.push(5)
		a.push(7)
		return Self{items: a}
	end 'create'

	function sum() returns Int
		var total = 0
		for it in items 'scan'
			self.items = IntArray.create()
			total = total + it
		end 'scan'
		return total
	end 'sum'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/for-iterated-self-field/iterated-self-field-bare-source-explicit-assign.test:18:9: cannot assign to immutable variable: 'items'
```

<!-- test: iterated-self-field-explicit-source-bare-assign -->
The `self.`-spelled source, the bare write. This is the combination the lock keyed on the receiver
parameter and the store door never asked about at all.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type Bag
	export var items as IntArray

	static function create() returns Self
		var a = IntArray.create()
		a.push(5)
		a.push(7)
		return Self{items: a}
	end 'create'

	function sum() returns Int
		var total = 0
		for it in self.items 'scan'
			items = IntArray.create()
			total = total + it
		end 'scan'
		return total
	end 'sum'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/for-iterated-self-field/iterated-self-field-explicit-source-bare-assign.test:18:4: cannot assign to immutable variable: 'items'
```

<!-- test: iterated-self-field-explicit-source-explicit-assign -->
Both halves spelled with `self.`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type Bag
	export var items as IntArray

	static function create() returns Self
		var a = IntArray.create()
		a.push(5)
		a.push(7)
		return Self{items: a}
	end 'create'

	function sum() returns Int
		var total = 0
		for it in self.items 'scan'
			self.items = IntArray.create()
			total = total + it
		end 'scan'
		return total
	end 'sum'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/for-iterated-self-field/iterated-self-field-explicit-source-explicit-assign.test:18:9: cannot assign to immutable variable: 'items'
```

<!-- test: iterated-self-field-bare-source-bare-call -->
A mutating METHOD on the iterated field is E3019, and it blames the field.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type Bag
	export var items as IntArray

	static function create() returns Self
		var a = IntArray.create()
		a.push(5)
		a.push(7)
		return Self{items: a}
	end 'create'

	function sum() returns Int
		var total = 0
		for it in items 'scan'
			items.clear()
			total = total + it
		end 'scan'
		return total
	end 'sum'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```maxoncstderr
error E3019: specs/fragments/for-iterated-self-field/iterated-self-field-bare-source-bare-call.test:18:10: cannot pass 'items' to function that mutates parameter 'self' (in Bag.sum)
```

<!-- test: iterated-self-field-bare-source-explicit-call -->
The chain spelling of the same call. It reaches a different door and must reach the same verdict.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type Bag
	export var items as IntArray

	static function create() returns Self
		var a = IntArray.create()
		a.push(5)
		a.push(7)
		return Self{items: a}
	end 'create'

	function sum() returns Int
		var total = 0
		for it in items 'scan'
			self.items.clear()
			total = total + it
		end 'scan'
		return total
	end 'sum'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```maxoncstderr
error E3019: specs/fragments/for-iterated-self-field/iterated-self-field-bare-source-explicit-call.test:18:15: cannot pass 'items' to function that mutates parameter 'self' (in Bag.sum)
```

<!-- test: iterated-self-field-explicit-source-bare-call -->
The `self.`-spelled source with the bare call.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type Bag
	export var items as IntArray

	static function create() returns Self
		var a = IntArray.create()
		a.push(5)
		a.push(7)
		return Self{items: a}
	end 'create'

	function sum() returns Int
		var total = 0
		for it in self.items 'scan'
			items.clear()
			total = total + it
		end 'scan'
		return total
	end 'sum'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```maxoncstderr
error E3019: specs/fragments/for-iterated-self-field/iterated-self-field-explicit-source-bare-call.test:18:10: cannot pass 'items' to function that mutates parameter 'self' (in Bag.sum)
```

<!-- test: iterated-self-field-explicit-source-explicit-call -->
The eighth cell. It was already refused before the lock was single-keyed — but it blamed `self`,
which is a parameter nothing can be done about.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type Bag
	export var items as IntArray

	static function create() returns Self
		var a = IntArray.create()
		a.push(5)
		a.push(7)
		return Self{items: a}
	end 'create'

	function sum() returns Int
		var total = 0
		for it in self.items 'scan'
			self.items.clear()
			total = total + it
		end 'scan'
		return total
	end 'sum'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```maxoncstderr
error E3019: specs/fragments/for-iterated-self-field/iterated-self-field-explicit-source-explicit-call.test:18:15: cannot pass 'items' to function that mutates parameter 'self' (in Bag.sum)
```

<!-- test: unrelated-self-field-stays-writable -->
The lock is keyed on the FIELD, not on the receiver — so every OTHER field of the same `self` is
still writable inside the body. A lock that keyed on `self` would refuse this.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type Bag
	export var items as IntArray
	export var seen as Int

	static function create() returns Self
		var a = IntArray.create()
		a.push(5)
		a.push(7)
		return Self{items: a, seen: 0}
	end 'create'

	function sum() returns Int
		var total = 0
		for it in self.items 'scan'
			seen = seen + 1
			total = total + it
		end 'scan'
		return total + seen
	end 'sum'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	return b.sum() as ExitCode
end 'main'
```
```exitcode
14
```

<!-- test: self-field-writable-after-the-loop -->
The lock is released at the loop's `end`, so the very next line may write the field.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type Bag
	export var items as IntArray

	static function create() returns Self
		var a = IntArray.create()
		a.push(5)
		a.push(7)
		return Self{items: a}
	end 'create'

	function sumThenClear() returns Int
		var total = 0
		for it in items 'scan'
			total = total + it
		end 'scan'
		items.clear()
		return total + items.count()
	end 'sumThenClear'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	return b.sumThenClear() as ExitCode
end 'main'
```
```exitcode
12
```
