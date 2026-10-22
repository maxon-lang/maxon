---
feature: conditional-extensions
status: stable
keywords: [extension, where, conditional, constraint, generics]
category: type-system
---

# Conditional Extensions

## Documentation

### Conditional Extension Methods

Extensions can include a `where` clause to restrict which conforming types receive the extension methods. Only types whose associated type bindings satisfy the constraints will have the methods synthesized.

### Syntax

```text
extension Iterable where Element is Equatable
  function contains(element Element) returns bool
    for item in self 'loop'
      if item == element 'found'
        return true
      end 'found'
    end 'loop'
    return false
  end 'contains'
end 'Iterable'
```

The `where` clause follows the same syntax as type-level where clauses: `where TypeParam is Interface` with `and` for multiple interfaces and comma separation for multiple type parameters.

### Behavior

When a type conforms to the extended interface, the compiler checks whether the type's associated type bindings satisfy the where constraints. If they do, the extension methods are synthesized for that type. If they don't, the methods are silently skipped.

For example, `Array with Int` conforms to `Iterable with int`. Since `int` implements `Equatable`, the `contains` method is available on `Array with Int`. A hypothetical `Array with SomeNonEquatableType` would not receive the `contains` method.

### Multiple Constraints

Multiple constraints are supported:

```text
extension Container where Key is Hashable and Equatable
  // methods available only when Key is both Hashable and Equatable
end 'Container'
```

## Tests

### Basic conditional extension with Equatable constraint

<!-- test: conditional-extensions.basic-equatable -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface HasItems uses Element
	function current() returns Element
	function advance() throws IterationError
end 'HasItems'

extension HasItems where Element is Equatable
	function has(target Element) returns bool
		for item in self 'loop'
			if item == target 'found'
				return true
			end 'found'
		end 'loop'
		return false
	end 'has'
end 'HasItems'

typealias IntegerArray = Array with Integer

type IntList implements HasItems with Integer
	let data as IntegerArray
	var idx as Integer

	function current() returns Integer
		return try data.get(idx as ElementIndex) otherwise panic("IntList.current: idx out of bounds")
	end 'current'

	function advance() throws IterationError
		if idx + 1 >= (data.count() as Integer) 'done'
			throw IterationError.exhausted
		end 'done'
		idx = idx + 1
	end 'advance'

	static function create(data IntegerArray) returns Self throws IterationError
		if data.count() == 0 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{data: data, idx: 0}
	end 'create'
end 'IntList'

function main() returns ExitCode
	var list = try IntList.create([10, 20, 30]) otherwise return 2
	if list.has(20) 'yes'
		return 1
	end 'yes'
	return 0
end 'main'
```
```exitcode
1
```

### Conditional extension not available when constraint not met

When the associated type does not satisfy the where constraint, calling the method should produce a compile error.

<!-- test: conditional-extensions.constraint-not-met -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Holder uses Item
	function get() returns Item
end 'Holder'

extension Holder where Item is Comparable
	function isGreater(other Item) returns bool
		return self.get().compare(other) == Ordering.greaterThan
	end 'isGreater'
end 'Holder'

type NotComparable
	let x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'NotComparable'

type MyHolder implements Holder with NotComparable
	let item as NotComparable

	function get() returns NotComparable
		return item
	end 'get'

	static function create(item NotComparable) returns Self
		return Self{item: item}
	end 'create'
end 'MyHolder'

function main() returns ExitCode
	let h = MyHolder.create(NotComparable.create(5))
	// isGreater should not exist on MyHolder since NotComparable doesn't implement Comparable
	let r = h.isGreater(NotComparable.create(3))
	return 0
end 'main'
```
```maxoncstderr
error E4006: specs/conditional-extensions/conditional-extensions.constraint-not-met.maxon:38:12: Type 'MyHolder' has no field named 'isGreater' ('isGreater' is available as a conditional extension where Item is Comparable, but 'NotComparable' does not implement 'Comparable')
```

### Conditional extension on a type (not an interface)

Extensions can target types directly, not just interfaces. This is useful when the method needs type-specific capabilities (e.g., `get()` on Array).

<!-- test: conditional-extensions.type-extension -->
```maxon
type Box uses Item
	export var item as Item

	static function create(item Item) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

extension Box where Item is Equatable
	function matches(other Item) returns bool
		return item == other
	end 'matches'
end 'Box'

typealias Int = int(i64.min to i64.max)
typealias IntBox = Box with Int

function main() returns ExitCode
	let b = IntBox.create(42)
	if b.matches(42) 'yes'
		return 1
	end 'yes'
	return 0
end 'main'
```
```exitcode
1
```

### Conditional extension on stdlib Array with contains

The real motivation: Array.contains requiring Element is Equatable.

<!-- test: conditional-extensions.array-contains -->
```maxon
function main() returns ExitCode
	let nums = [10, 20, 30, 40]
	if nums.contains([20]) 'found'
		return 1
	end 'found'
	return 0
end 'main'
```
```exitcode
1
```

### Conditional extension with multiple constraints using and

<!-- test: conditional-extensions.multiple-constraints -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Bucket uses Element
	function current() returns Element
	function advance() throws IterationError
end 'Bucket'

extension Bucket where Element is Equatable and Hashable
	function lookup(target Element) returns bool
		for item in self 'loop'
			if item == target 'found'
				return true
			end 'found'
		end 'loop'
		return false
	end 'lookup'
end 'Bucket'

type HashItem implements Equatable, Hashable
	export var v as Integer

	function equals(other HashItem) returns bool
		return v == other.v
	end 'equals'

	function hash() returns HashValue
		return v
	end 'hash'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'HashItem'

typealias HashItemArray = Array with HashItem

type HashBucket implements Bucket with HashItem
	let items as HashItemArray
	var idx as Integer

	function current() returns HashItem
		return try items.get(idx as ElementIndex) otherwise panic("HashBucket.current: idx out of bounds")
	end 'current'

	function advance() throws IterationError
		if idx + 1 >= (items.count() as Integer) 'done'
			throw IterationError.exhausted
		end 'done'
		idx = idx + 1
	end 'advance'

	static function create(items HashItemArray) returns Self throws IterationError
		if items.count() == 0 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{items: items, idx: 0}
	end 'create'
end 'HashBucket'

function main() returns ExitCode
	var b = try HashBucket.create([HashItem.create(1), HashItem.create(2), HashItem.create(3)]) otherwise return 2
	if b.lookup(HashItem.create(2)) 'found'
		return 1
	end 'found'
	return 0
end 'main'
```
```exitcode
1
```

### Conditional extension skipped for non-qualifying type but other extensions still work

<!-- test: conditional-extensions.partial-availability -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Seq uses Element
	function current() returns Element
	function advance() throws IterationError
end 'Seq'

extension Seq
	function countItems() returns Integer
		var n = 0 as Integer
		for _ in self 'loop'
			n = n + 1
		end 'loop'
		return n
	end 'countItems'
end 'Seq'

extension Seq where Element is Equatable
	function includes(target Element) returns bool
		for item in self 'loop'
			if item == target 'yes'
				return true
			end 'yes'
		end 'loop'
		return false
	end 'includes'
end 'Seq'

type NotEq
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'NotEq'

typealias NotEqArray = Array with NotEq

type NotEqSeq implements Seq with NotEq
	let items as NotEqArray
	var idx as Integer

	function current() returns NotEq
		return try items.get(idx as ElementIndex) otherwise panic("NotEqSeq.current: idx out of bounds")
	end 'current'

	function advance() throws IterationError
		if idx + 1 >= (items.count() as Integer) 'done'
			throw IterationError.exhausted
		end 'done'
		idx = idx + 1
	end 'advance'

	static function create(items NotEqArray) returns Self throws IterationError
		if items.count() == 0 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{items: items, idx: 0}
	end 'create'
end 'NotEqSeq'

function main() returns ExitCode
	var s = try NotEqSeq.create([NotEq.create(1), NotEq.create(2)]) otherwise return 9
	return s.countItems()
end 'main'
```
```exitcode
2
```

### A conditional extension's verdict reads the binding's type, not the alias name two files share

Two files each declare a private `Bag`, one over `Byte` and one over a type that is not `Equatable`. `Packed`
binds `Item` to the first file's `Bag`, a byte array, which is `Equatable`, so the conditional extension
reaches `Packed` and `witnessed()` returns 7. The name `Bag` alone denotes no one type, so a verdict decided
from the name withholds the method from `Packed`.

<!-- test: conditional-extensions.binding-over-an-alias-two-files-declare -->
```maxon
// --- file: holder.maxon
export interface Holder uses Item
	function get() returns Item
end 'Holder'

extension Holder where Item is Equatable
	export function witnessed() returns ExitCode
		return 7
	end 'witnessed'
end 'Holder'

// --- file: packed.maxon
typealias Bag = Array with Byte

export type Packed implements Holder with Bag
	let tag as ExitCode

	export static function create() returns Packed
		return Packed{tag: 0}
	end 'create'

	function get() returns Bag
		return Bag from [1, 2, 3]
	end 'get'
end 'Packed'

// --- file: loose.maxon
type Opaque
	let tag as ExitCode
end 'Opaque'

typealias Bag = Array with Opaque

export type Loose implements Holder with Bag
	let tag as ExitCode

	export static function create() returns Loose
		return Loose{tag: 0}
	end 'create'

	function get() returns Bag
		return Bag.create()
	end 'get'

	export function size() returns ExitCode
		return self.get().count() as ExitCode
	end 'size'
end 'Loose'

// --- file: main.maxon
function main() returns ExitCode
	let loose = Loose.create()
	return Packed.create().witnessed() + loose.size()
end 'main'
```
```exitcode
7
```

### An extension body's associated type is the conformer's binding, not a name read again in the extension's file

`Packed` and `Loose` each bind `Item` to the private `Bag` of their own file, one over `Byte` and one over a
type that is not `Equatable`; the extension's file declares a third `Bag`, over `String`, and uses it for its
own local. `weigh(other Item)` takes the conformer's binding, so on `Packed` it takes the byte array and
`weigh` of a three-byte array returns 3. Reading the binding's name again in the extension's file would
type `other` as an array of strings.

<!-- test: conditional-extensions.associated-type-in-an-extension-body-over-an-alias-two-files-declare -->
```maxon
// --- file: holder.maxon
typealias Bag = Array with String

export interface Holder uses Item
	function get() returns Item
end 'Holder'

extension Holder where Item is Equatable
	export function weigh(other Item) returns ExitCode
		let names = Bag.create()
		return (other.count() as ExitCode) + (names.count() as ExitCode)
	end 'weigh'
end 'Holder'

// --- file: packed.maxon
typealias Bag = Array with Byte

export type Packed implements Holder with Bag
	let tag as ExitCode

	export static function create() returns Packed
		return Packed{tag: 0}
	end 'create'

	function get() returns Bag
		return Bag from [1, 2, 3]
	end 'get'
end 'Packed'

// --- file: loose.maxon
type Opaque
	let tag as ExitCode
end 'Opaque'

typealias Bag = Array with Opaque

export type Loose implements Holder with Bag
	let tag as ExitCode

	export static function create() returns Loose
		return Loose{tag: 0}
	end 'create'

	function get() returns Bag
		return Bag.create()
	end 'get'

	export function size() returns ExitCode
		return self.get().count() as ExitCode
	end 'size'
end 'Loose'

// --- file: main.maxon
function main() returns ExitCode
	let loose = Loose.create()
	return Packed.create().weigh(ByteArray from [1, 2, 3]) + loose.size()
end 'main'
```
```exitcode
3
```
