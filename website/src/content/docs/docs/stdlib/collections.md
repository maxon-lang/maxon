---
title: Collections
description: Array, List, Map, Set, Vector, Range, iterators, and the collection interfaces.
sidebar:
  order: 3
---

## Array

`Array` is a growable, contiguous, generic sequence. Declare a concrete type with `typealias`, or write a
literal: `[1, 2, 3]`. `Array` implements `Iterable` and `Cloneable`; it is also `Hashable` and `Equatable`
when its element is.

```maxon
typealias Score = int(i64.min to i64.max)
typealias ScoreArray = Array with Score

function main() returns ExitCode
	var a = ScoreArray.create()
	a.push(5)
	a.push(1)
	a.push(4)

	let last = try a.pop() otherwise 0
	a.insert(0, value: 9)
	a.sort()

	for x in a 'each'
		print("{x} ")
	end 'each'

	a.sort(function(x Score, y Score) gives y.compare(x))
	let first = try a.first() otherwise 0
	let missing = try a.get(50) otherwise -1
	print("\n{last} {first} {missing} {a.contains(9)}\n")
	return 0
end 'main'
```

Output: `1 5 9` and `4 9 -1 true`.

### Creating

| Member | Returns | Description |
|--------|---------|-------------|
| `Array.create()` | `Array` | An empty array. |
| `[a, b, c]` | `Array` | A literal; its element type is inferred from context or from the first element. |
| `clone()` | `Array` | A second array over the same elements. Storage is shared copy-on-write and separates on the first write to either. |
| `Array.from(source Iterable)` | `Array` | Collect every element of an iterable, called through an alias (`ScoreArray.from(range)`). The iterable must bind `Element` to the alias's own element (E3127). |
| `Array.init(managed)` | `Array` | Wrap raw compiler-managed storage; used by the compiler and the library. |
| `managed` | field | The array's raw storage, for `appendMemory` and library code. |

### Reading

| Member | Returns | Description |
|--------|---------|-------------|
| `count()` | `int(0 to u64.max)` | Number of elements. |
| `isEmpty()` | `bool` | True when `count()` is 0. |
| `capacity()` | `int(i64.min to i64.max)` | Slots allocated. Negative when the storage is not this array's own yet: `-1` for a slice view that has not been written, other negative values for a literal's or a module-level constant's read-only storage. Treat any negative value as "not owned yet". |
| `get(index)` | `Element` | Throws `ArrayError.indexOutOfBounds` at or past `count()`, `ArrayError.emptySlot` for a slot that was never written. |
| `first()` | `Element` | Throws like `get(0)`. |
| `last()` | `Element` | Throws `indexOutOfBounds` when empty. |
| `slice(start, endIndex)` | `Array` | Elements `[start, endIndex)`. Throws `indexOutOfBounds` for a reversed or out-of-range pair; nothing is clamped. |
| `contains(element Element)` | `bool` | Element test. Requires `Element is Equatable`. |
| `contains(sequence Array)` | `bool` | True when `sequence` occurs contiguously. An empty sequence is always contained. Requires `Element is Equatable`. |

### Writing

| Member | Description |
|--------|-------------|
| `set(index, value Element)` | Replace an element. Throws `indexOutOfBounds` at or past `count()`, even when capacity exists there. |
| `push(value Element)` | Append one element. |
| `append(other Array)` | Append every element of another array. |
| `appendMemory(source)` | Append every element of raw storage (another array's `managed`) without wrapping it in an `Array`. |
| `pop()` | Remove and return the last element. Throws `indexOutOfBounds` when empty. |
| `insert(at, value Element)` | Insert, shifting later elements right. An `at` past the end appends. |
| `remove(at)` | Remove and return the element at `at`. Throws `indexOutOfBounds`. |
| `clear()` | Remove every element, keeping the capacity. |

### Capacity and length

| Member | Description |
|--------|-------------|
| `reserve(minCapacity)` | Ensure at least `minCapacity` slots. |
| `resize(newLength)` | Set the length. Growing exposes zero-valued elements, so it is refused at compile time (E3106) for an element type that is not a plain value — a struct, a `String`, a nested container. Use `growFilled` or `truncate` for those. |
| `truncate(newLength)` | Shorten to `newLength`, releasing the removed elements. A longer `newLength` does nothing. |
| `growFilled(newLength, value Element)` | Lengthen to `newLength`, setting only the added elements to `value`. Grows capacity geometrically, so repeated small extensions stay amortized linear. A shorter `newLength` does nothing. |
| `refill(newLength, value Element)` | Set the length to `newLength` and write `value` into every element, growing or shrinking. |

An array grows by doubling while small and eases toward 1.25x once it is large.

### Sorting

| Member | Description |
|--------|-------------|
| `sort()` | Stable sort by `Comparable.compare`. Requires `Element is Comparable`. |
| `sortUnstable()` | Unstable sort (pattern-defeating quicksort). Requires `Element is Comparable`. |
| `sort(cmp function(Element, Element) returns Ordering)` | Stable sort by a comparator; any element type. |
| `sortUnstable(cmp function(Element, Element) returns Ordering)` | Unstable sort by a comparator. |

A sort reads and writes no module-level state, so it may run inside a service message handler without
tripping [E3143](/docs/cli/error-codes/#e3143--semanticsharedglobalaccessfromgreenthread).

### Iterating

| Member | Returns | Description |
|--------|---------|-------------|
| `createIterator()` | `ArrayIterator` | Positioned at the first element. Throws `IterationError.exhausted` when empty. |
| `cursor()` | `ArrayIterator` | The same as `createIterator()`. |

`map`, `filter`, `contains(predicate)` and `withIterator` come from [Iterable](#interfaces).

### ArrayError

```maxon
enum ArrayError implements Error
	indexOutOfBounds
	emptySlot
end 'ArrayError'
```

`indexOutOfBounds`: the index was outside `[0, count())`. `emptySlot`: the index was inside and the slot
there was never filled.

## List

`List` is a doubly linked list: O(1) insertion and removal at both ends, O(n) access by index. It implements
`Iterable`, `Cloneable` and array-literal construction (`List from [1, 2, 3]`).

| Member | Returns | Complexity | Description |
|--------|---------|-----------|-------------|
| `List.create()` | `List` | O(1) | An empty list. |
| `count()` | `int(0 to u64.max)` | O(1) | Number of elements. |
| `isEmpty()` | `bool` | O(1) | True when empty. |
| `first()` | `Element` | O(1) | Throws `ArrayError` when empty. |
| `last()` | `Element` | O(1) | Throws `ArrayError` when empty. |
| `get(index)` | `Element` | O(n) | Throws `ArrayError.indexOutOfBounds`. |
| `prepend(value Element)` | — | O(1) | Add to the front. |
| `append(value Element)` | — | O(1) | Add to the back. |
| `insert(at, value Element)` | — | O(n) | `at` may be `0` through `count()`. Past `count()` throws `ArrayError.indexOutOfBounds`. |
| `removeFirst()` | `Element` | O(1) | Throws `ArrayError` when empty. |
| `removeLast()` | `Element` | O(1) | Throws `ArrayError` when empty. |
| `remove(at)` | `Element` | O(n) | Throws `ArrayError.indexOutOfBounds`. |
| `clear()` | — | O(n) | Remove every element. |
| `clone()` | `List` | O(n) | A deep copy. |
| `createIterator()` | `ListIterator` | O(1) | Throws `IterationError.exhausted` when empty. |

`ListIterator` implements `Iterator with Element`: `current()` and `advance()`.

The list's throwing methods throw `ArrayError`.

```maxon
typealias Score = int(i64.min to i64.max)
typealias ScoreList = List with Score

function main() returns ExitCode
	var list = ScoreList.create()
	list.append(2)
	list.prepend(1)
	try list.insert(2, value: 3) otherwise panic("index is within [0, count]")

	let front = try list.removeFirst() otherwise 0

	for value in list 'each'
		print("{value} ")
	end 'each'

	print("\n{front} {list.count()}\n")
	return 0
end 'main'
```

Output: `2 3` and `1 2`.

## Map

`Map` is a hash table from keys to values. The key type must be `Hashable` and `Equatable`. Maps are built
with `create()` or a dictionary literal `["a": 1, "b": 2]`, and iterate as `(key, value)` tuples in table
order, which is unspecified. The table resizes when it is three-quarters full.

| Member | Returns | Description |
|--------|---------|-------------|
| `Map.create()` | `Map` | An empty map. |
| `insert(key Key, value Value)` | — | Add a new entry. Throws `MapError.keyAlreadyExists` when the key is present. |
| `upsert(key Key, value Value)` | — | Add the entry, or replace the value of an existing key. |
| `get(key Key)` | `Value` | Throws `MapError.keyNotFound`. |
| `contains(key Key)` | `bool` | Key test. |
| `remove(key Key)` | `bool` | Remove the entry; true when the key was present. |
| `count()` | `int(0 to 4611686018427387904)` | Number of entries. |
| `getCapacity()` | table capacity | Total slots in the table: `0` for a map from `create()` until its first insert, `16` after that for a small map. |
| `createIterator()` | `MapIterator` | Throws `IterationError.exhausted` when empty. |

`MapIterator` implements `Iterator with (Key, Value)`: `current()` and `advance()`.

```maxon
enum MapError implements Error
	keyNotFound
	keyAlreadyExists
end 'MapError'
```

```maxon
typealias Age = int(0 to 150)
typealias Ages = Map with String, Age

function main() returns ExitCode
	var ages = Ages.create()
	try ages.insert("ann", value: 30) otherwise panic("new key")

	try ages.insert("ann", value: 31) otherwise 'duplicate'
		print("ann is already present\n")
	end 'duplicate'

	ages.upsert("bob", value: 25)
	ages.upsert("ann", value: 32)
	let ann = try ages.get("ann") otherwise 0
	print("{ann} {ages.contains("bob")} {ages.remove("bob")} {ages.count()}\n")

	for (name, age) in ages 'each'
		print("{name}: {age}\n")
	end 'each'

	return 0
end 'main'
```

Output: `ann is already present`, `32 true true 1`, `ann: 32`.

## Set

`Set` is a hash set. The element type must be `Hashable` and `Equatable`. Build one with `create()` or
`Set from [1, 2, 3]`. Iteration order is unspecified.

| Member | Returns | Description |
|--------|---------|-------------|
| `Set.create()` | `Set` | An empty set. |
| `insert(element Element)` | — | Add an element; inserting a present element does nothing. |
| `contains(element Element)` | `bool` | Membership test. |
| `remove(element Element)` | `bool` | Remove; true when the element was present. |
| `count()` | `int(0 to 4611686018427387904)` | Number of elements. |
| `getCapacity()` | table capacity | Total slots in the table. |
| `createIterator()` | `SetIterator` | Throws `IterationError.exhausted` when empty. |

`SetIterator` implements `Iterator with Element`: `current()` and `advance()`.

```maxon
typealias Tags = Set with String

function main() returns ExitCode
	var tags = Tags.create()
	tags.insert("red")
	tags.insert("red")
	tags.insert("blue")
	print("{tags.count()} {tags.contains("red")} {tags.remove("red")} {tags.count()}\n")
	return 0
end 'main'
```

Output: `2 true true 1`.

## Vector

`Vector` is a fixed-size array whose element count is part of its type: `Vector with 3 Coord` holds exactly
three elements, and `countof(Vec3)` is the constant `3`. It implements `Iterable`.

| Member | Returns | Description |
|--------|---------|-------------|
| `Vector.create()` | `Vector` | Every element zero. |
| `Vector from [a, b, c]` | `Vector` | A literal; the count is the literal's length. |
| `count()` | `int(0 to u64.max)` | The fixed size, answered from the type. |
| `get(index)` | `Element` | Throws `ArrayError.indexOutOfBounds`. |
| `set(index, value Element)` | — | Throws `ArrayError.indexOutOfBounds` at or past the fixed size. |
| `createIterator()` | `ArrayIterator` | Throws `IterationError.exhausted` when the vector is empty. |

A vector never changes length: it has no `push`, `insert` or `remove`.

```maxon
typealias Coord = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Coord

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(1, value: 7) otherwise panic("inside the fixed size")

	try v.set(3, value: 1) otherwise 'outside'
		print("index 3 refused\n")
	end 'outside'

	let literal = Vector from [1, 2, 3]
	var sum = 0

	for x in literal 'each'
		sum = sum + x
	end 'each'

	print("{v.count()} {countof(Vec3)} {try v.get(1) otherwise 0} {sum}\n")
	return 0
end 'main'
```

Output: `index 3 refused` and `3 3 7 6`.

## Range

In a `for` header, `start to end` and `start upto end` compile to a counted loop with no allocation.
Anywhere else they are values:

| Expression | Type | Visits |
|------------|------|--------|
| `start to end` | `Range` | `start` through `end`, both included |
| `start upto end` | `OpenRange` | `start` up to but excluding `end` |

Both implement `Iterable with (RangeBound, RangeIterator)`; `RangeBound` is `int(i64.min to i64.max)`.

| Member | Returns | Description |
|--------|---------|-------------|
| `Range.create(start RangeBound, finish RangeBound)` | `Range` | The same as `start to finish`. |
| `OpenRange.create(start RangeBound, endExclusive RangeBound)` | `OpenRange` | The same as `start upto endExclusive`. |
| `createIterator()` | `RangeIterator` | Throws `IterationError.exhausted` for an empty range (`finish < start`, or `endExclusive <= start`). |

`RangeIterator` implements `Iterator with RangeBound` and `BidirectionalIterator`:

| Member | Returns | Description |
|--------|---------|-------------|
| `RangeIterator.create(first RangeBound, last RangeBound)` | `RangeIterator` | Both inclusive. Throws `exhausted` when `last < first`. |
| `current()` | `RangeBound` | The current value. |
| `index()` | `int(0 to u64.max)` | Steps taken from `first`. |
| `advance()` | — | Throws `exhausted` at `last`. |
| `retreat()` | — | Throws `atStart` at `first`. |

```maxon
function main() returns ExitCode
	let inclusive = 1 to 4
	var total = 0

	for x in inclusive 'sum'
		total = total + x
	end 'sum'

	for (iter, x) in (10 upto 13).withIterator() 'each'
		print("{iter.index()}:{x} ")
	end 'each'

	print("total={total}\n")
	return 0
end 'main'
```

Output: `0:10 1:11 2:12 total=10`.

## Iterators

A live iterator always points at a valid element, so `current()` cannot fail; navigation throws
`IterationError`. `createIterator()` on an empty collection throws `IterationError.exhausted` instead of
returning an iterator with nothing under it.

```maxon
enum IterationError implements Error
	exhausted
	atStart
end 'IterationError'
```

`exhausted`: an `advance` past the last element. `atStart`: a `retreat` before the first. A failed move
leaves the position unchanged.

### ArrayIterator

`ArrayIterator` (what `Array.createIterator()`, `Array.cursor()` and `Vector.createIterator()` return)
implements `BidirectionalIterator with Element` and adds random access:

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `ArrayIterator.create(source)` | `ArrayIterator` | `IterationError` | Over raw array storage; `exhausted` when it is empty. |
| `current()` | `Element` | — | The element at the position. |
| `index()` | `int(0 to u64.max)` | — | The position. |
| `advance()` | — | `IterationError` | Forward one; `exhausted` at the end. |
| `retreat()` | — | `IterationError` | Back one; `atStart` at position 0. |
| `seek(index)` | — | `IterationError` | Jump to an absolute position; `exhausted` when out of bounds. |
| `peek(ahead)` | `Element` | `IterationError` | The element `ahead` positions on, without moving. |
| `advanceBy(n IterStep)` | — | `IterationError` | Forward `n` (from `Iterator`). |
| `retreatBy(n IterStep)` | — | `IterationError` | Back `n` (from `BidirectionalIterator`). |

`advanceBy` and `retreatBy` step one at a time, so a move that fails part-way leaves the iterator where the
throw happened. `IterStep` is unsigned: the direction is the method you call, so a negative step is refused
at the argument door rather than read as a move the other way.

```maxon
typealias Score = int(i64.min to i64.max)
typealias ScoreArray = Array with Score

function main() returns ExitCode
	var values = ScoreArray.create()

	for i in 1 to 5 'fill'
		values.push(i * 10)
	end 'fill'

	var c = try values.cursor() otherwise panic("not empty")
	try c.advance() otherwise panic("has a second element")
	let ahead = try c.peek(2) otherwise 0
	print("{c.index()} {c.current()} {ahead}\n")

	try c.advanceBy(3) otherwise panic("in range")
	let beyond = try c.peek(1) otherwise -1
	print("{c.current()} {beyond}\n")
	return 0
end 'main'
```

Output: `1 20 40` and `50 -1`.

### The other iterators

| Iterator | Produced by | Element |
|----------|-------------|---------|
| `ListIterator` | `List.createIterator()` | the list's element |
| `MapIterator` | `Map.createIterator()` | `(Key, Value)` |
| `SetIterator` | `Set.createIterator()` | the set's element |
| `StringIterator` | `String.createIterator()` | `Character` |
| `RangeIterator` | `Range`/`OpenRange.createIterator()` | `RangeBound` |

Each has a static `create`, `current()` and `advance()`.

## Interfaces

The protocols the library and the language are built on.

| Interface | Requirement | Used by |
|-----------|-------------|---------|
| `Error` | none | Every throwable type: `enum E implements Error` |
| `Equatable` | `function equals(other Self) returns bool` | `==`, `!=`, `contains` |
| `Comparable` | `function compare(other Self) returns Ordering` | `sort()` |
| `Hashable` | `function hash() returns HashValue` | `Map` keys, `Set` elements |
| `Cloneable` | `function clone() returns Self` | `clone()` |
| `Stringable` | `function toString() returns String` | `"{value}"` |
| `FormattedStringable` | `function toString(format String) returns String` | `"{value:format}"` — the text after the colon is passed as `format` |
| `Iterator with Element` | `current() returns Element`, `advance() throws IterationError` | iterators |
| `BidirectionalIterator` | extends `Iterator`; `retreat() throws IterationError` | reversible iterators |
| `Iterable with (Element, Iter)` | `createIterator() returns Iter throws IterationError` | `for`-`in` |
| `Parsable` | `static fromString(input String) returns Self throws Error` | `int.fromString` and friends |

The literal-initialization interfaces (`InitableFromStringLiteral`, `InitableFromCharLiteral`,
`InitableFromArrayLiteral`, `InitableFromDictionaryLiteral`) let a type be written as `MyType from <literal>`;
see Initializing From Literals in [LANGUAGE_REFERENCE.md](/docs/language/overview/).

### Supporting types

```maxon
enum Ordering
	lessThan
	equalTo
	greaterThan
end 'Ordering'
```

`HashValue` is `int(0 to u32.max)`; `IterStep` is `int(0 to u64.max)`.

| Function | Returns | Description |
|----------|---------|-------------|
| `spreadHash(hash HashValue)` | `HashValue` | Mix a hash's bits (the MurmurHash3 32-bit finalizer). `Map` and `Set` apply it before choosing a slot, so keys with regular structure do not cluster. A `hash()` implementation does not need to call it. |

### Default methods

| On | Method | Returns | Description |
|----|--------|---------|-------------|
| `Iterator` | `advanceBy(n IterStep)` | — | Call `advance()` `n` times. |
| `BidirectionalIterator` | `retreatBy(n IterStep)` | — | Call `retreat()` `n` times. |
| `Iterable` | `map(transform function(Element) returns Element)` | `Array with Element` | Each element transformed. |
| `Iterable` | `filter(keep function(Element) returns bool)` | `Array with Element` | The elements `keep` accepts. |
| `Iterable` | `contains(predicate function(Element) returns bool)` | `bool` | True when any element matches. |
| `Iterable` | `withIterator()` | iterator pairs | Iterate as `for (iter, element) in collection.withIterator()`, with the iterator in hand. |

```maxon
typealias Cents = int(0 to u32.max)

type Money implements Stringable, FormattedStringable, Equatable, Comparable, Hashable, Cloneable
	let cents as Cents

	static function create(cents Cents) returns Money
		return Money{cents: cents}
	end 'create'

	function toString() returns String
		return "{cents} cents"
	end 'toString'

	function toString(format String) returns String
		return "{format} {cents}"
	end 'toString'

	function equals(other Money) returns bool
		return cents == other.cents
	end 'equals'

	function compare(other Money) returns Ordering
		return cents.compare(other.cents)
	end 'compare'

	function hash() returns HashValue
		return cents.hash()
	end 'hash'

	function clone() returns Money
		return Money{cents: cents}
	end 'clone'
end 'Money'

typealias Prices = Set with Money

function main() returns ExitCode
	let price = Money.create(250)
	var prices = Prices.create()
	prices.insert(price)
	prices.insert(price.clone())
	print("{price} | {price:EUR} | {price == Money.create(250)} {price.compare(Money.create(1))} {prices.count()}\n")
	return 0
end 'main'
```

Output: `250 cents | EUR 250 | true greaterThan 1`.
