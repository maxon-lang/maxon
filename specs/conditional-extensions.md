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
	function next() returns Element throws IterationError
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
	var data IntegerArray
	var idx Integer

	function next() returns Integer throws IterationError
		if idx >= data.count() 'done'
			throw IterationError.exhausted
		end 'done'
		var v = try data.get(idx) otherwise 'bail'
			throw IterationError.exhausted
		end 'bail'
		idx = idx + 1
		return v
	end 'next'

	static function create(data IntegerArray, idx Integer) returns Self
		return Self{data: data, idx: idx}
	end 'create'
end 'IntList'

function main() returns ExitCode
	var list = IntList.create(data: [10, 20, 30], idx: 0)
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
	var x Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'NotComparable'

type MyHolder implements Holder with NotComparable
	var item NotComparable

	function get() returns NotComparable
		return item
	end 'get'

	static function create(item NotComparable) returns Self
		return Self{item: item}
	end 'create'
end 'MyHolder'

function main() returns ExitCode
	var h = MyHolder.create(item: NotComparable.create(x: 5))
	// isGreater should not exist on MyHolder since NotComparable doesn't implement Comparable
	var r = h.isGreater(NotComparable.create(x: 3))
	return 0
end 'main'
```
```maxoncstderr
error E4006: specs/fragments/conditional-extensions/conditional-extensions.constraint-not-met.test:38:12: Type 'MyHolder' has no field named 'isGreater' ('isGreater' is available as a conditional extension where Item is Comparable, but 'NotComparable' does not implement 'Comparable')
```

### Conditional extension on a type (not an interface)

Extensions can target types directly, not just interfaces. This is useful when the method needs type-specific capabilities (e.g., `get()` on Array).

<!-- test: conditional-extensions.type-extension -->
```maxon
type Box uses Item
	export var item Item

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
	var b = IntBox.create(item: 42)
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
	var nums = [10, 20, 30, 40]
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
	function next() returns Element throws IterationError
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
	export var v Integer

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
	var items HashItemArray
	var idx Integer

	function next() returns HashItem throws IterationError
		if idx >= items.count() 'done'
			throw IterationError.exhausted
		end 'done'
		var v = try items.get(idx) otherwise 'bail'
			throw IterationError.exhausted
		end 'bail'
		idx = idx + 1
		return v
	end 'next'

	static function create(items HashItemArray, idx Integer) returns Self
		return Self{items: items, idx: idx}
	end 'create'
end 'HashBucket'

function main() returns ExitCode
	var b = HashBucket.create(items: [HashItem.create(v: 1), HashItem.create(v: 2), HashItem.create(v: 3)], idx: 0)
	if b.lookup(HashItem.create(v: 2)) 'found'
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
	function next() returns Element throws IterationError
end 'Seq'

extension Seq
	function countItems() returns Integer
		var n = 0
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
	export var n Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'NotEq'

typealias NotEqArray = Array with NotEq

type NotEqSeq implements Seq with NotEq
	var items NotEqArray
	var idx Integer

	function next() returns NotEq throws IterationError
		if idx >= items.count() 'done'
			throw IterationError.exhausted
		end 'done'
		var v = try items.get(idx) otherwise 'bail'
			throw IterationError.exhausted
		end 'bail'
		idx = idx + 1
		return v
	end 'next'

	static function create(items NotEqArray, idx Integer) returns Self
		return Self{items: items, idx: idx}
	end 'create'
end 'NotEqSeq'

function main() returns ExitCode
	var s = NotEqSeq.create(items: [NotEq.create(n: 1), NotEq.create(n: 2)], idx: 0)
	return s.countItems()
end 'main'
```
```exitcode
2
```
