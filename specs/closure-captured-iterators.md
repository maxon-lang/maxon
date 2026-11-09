---
feature: closure-captured-iterators
keywords: [closure, capture, iterator, mutates captures, parameter, frozen, immutable, array, list, map, holder]
category: semantics
---

# A Closure That Captures an Iterator Captures the Collection the Iterator Runs Over

## Documentation

An iterator reads the elements of the collection it was created from, so a closure that captures an iterator
captures that collection's data. A closure that writes an element through a captured iterator writes the
collection, and the collection must be mutable data: a function whose parameter the iterator runs over takes a
mutable argument, as it does when the closure captures the parameter itself. A closure that only reads the
elements through the iterator leaves the collection as it was, so it takes a frozen argument too.

The iterator a `for (it, _) in xs.withIterator()` loop binds runs over `xs`, and an iterator held in a tuple or a
record that the closure captures runs over the collection it was made from, so a closure that writes through either
captures that collection as it does when it captures the iterator directly.

An element read through a captured iterator is data of the collection, not a new value: a closure that hands one
to an `immutable` parameter is refused, because the iterator's collection may be mutable.

## Tests

<!-- test: error.a-mutates-captures-closure-over-an-array-iterator-is-refused-for-a-frozen-array -->
`bumpFirst` captures an iterator over its parameter in a closure that writes the element it reads, so the frozen array is refused where `bumpFirst` is called.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

function bumpFirst(boxes BoxArray) returns Count
	let it = try boxes.createIterator() otherwise panic("empty")
	let g = function() gives bumpBox(it.current())
	return runIt(g)
end 'bumpFirst'

function main() returns ExitCode
	var fill = BoxArray.create(0) as isolated BoxArray

	modify fill 'fill'
		fill.push(Box.create(1))
	end 'fill'

	let frozen = freeze fill
	print("{bumpFirst(frozen)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:37:20: 'bumpFirst' writes 'boxes' (it calls 'bumpFirst$closure_0', which calls 'bumpBox', which writes 'box'); 'frozen' is immutable
```

<!-- test: error.a-returned-mutates-captures-closure-over-an-array-iterator-is-refused-for-a-frozen-array -->
`makeBump` returns the closure, so the write happens wherever the closure is called; the closure still holds the iterator over the parameter, so the frozen array is refused where `makeBump` is called.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

function makeBump(boxes BoxArray) returns Bump
	let it = try boxes.createIterator() otherwise panic("empty")
	return function() gives bumpBox(it.current())
end 'makeBump'

function main() returns ExitCode
	var fill = BoxArray.create(0) as isolated BoxArray

	modify fill 'fill'
		fill.push(Box.create(1))
	end 'fill'

	let frozen = freeze fill
	let g = makeBump(frozen)
	print("{runIt(g)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:36:19: 'makeBump' writes 'boxes' (it calls 'makeBump$closure_0', which calls 'bumpBox', which writes 'box'); 'frozen' is immutable
```

<!-- test: error.a-mutates-captures-closure-over-a-list-iterator-is-refused-for-a-frozen-list -->
The same closure over a `List` iterator writes the list's elements, so the frozen list is refused.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxList = List with Box

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

function bumpFirst(boxes BoxList) returns Count
	let it = try boxes.createIterator() otherwise panic("empty")
	let g = function() gives bumpBox(it.current())
	return runIt(g)
end 'bumpFirst'

function main() returns ExitCode
	var fill = BoxList.create() as isolated BoxList

	modify fill 'fill'
		fill.append(Box.create(1))
	end 'fill'

	let frozen = freeze fill
	print("{bumpFirst(frozen)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:37:20: 'bumpFirst' writes 'boxes' (it calls 'bumpFirst$closure_0', which calls 'bumpBox', which writes 'box'); 'frozen' is immutable
```

<!-- test: error.a-mutates-captures-closure-over-a-map-iterator-is-refused-for-a-frozen-map -->
The same closure over a `Map` iterator writes a value of the map, so the frozen map is refused.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxMap = Map with (String, Box)

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

function bumpFirst(boxes BoxMap) returns Count
	let it = try boxes.createIterator() otherwise panic("empty")
	let g = function() gives bumpBox(it.current().1)
	return runIt(g)
end 'bumpFirst'

function main() returns ExitCode
	var fill = BoxMap.create() as isolated BoxMap

	modify fill 'fill'
		fill.upsert("a", value: Box.create(1))
	end 'fill'

	let frozen = freeze fill
	print("{bumpFirst(frozen)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:37:20: 'bumpFirst' writes 'boxes' (it calls 'bumpFirst$closure_0', which calls 'bumpBox', which writes 'box'); 'frozen' is immutable
```

<!-- test: error.a-mutates-captures-closure-over-an-iterator-of-a-records-collection-is-refused-for-a-frozen-record -->
The iterator runs over the `items` field of the parameter record, so the closure writes the record's data and the frozen record is refused.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

type Coll
	export var items as BoxArray

	static function create() returns Self
		return Self{items: BoxArray.create(0)}
	end 'create'

	function add(box Box)
		items.push(box)
	end 'add'
end 'Coll'

function bumpFirst(coll Coll) returns Count
	let it = try coll.items.createIterator() otherwise panic("empty")
	let g = function() gives bumpBox(it.current())
	return runIt(g)
end 'bumpFirst'

function main() returns ExitCode
	let coll = Coll.create()
	coll.add(Box.create(1))
	let frozen = freeze coll.clone()
	print("{bumpFirst(frozen)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:45:20: 'bumpFirst' writes 'coll' (it calls 'bumpFirst$closure_0', which calls 'bumpBox', which writes 'box'); 'frozen' is immutable
```

<!-- test: error.a-returned-mutates-captures-closure-over-an-iterator-through-a-holder-is-refused-for-a-frozen-record -->
`makeBump` reaches the collection through a `Cur` that holds its parameter and returns a closure over the iterator, so the frozen record is refused where `makeBump` is called.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

type Coll
	export var items as BoxArray

	static function create() returns Self
		return Self{items: BoxArray.create(0)}
	end 'create'

	function add(box Box)
		items.push(box)
	end 'add'
end 'Coll'

type Cur
	export var coll as Coll
	var pos as Count

	static function create(coll Coll, pos Count) returns Self
		return Self{coll: coll, pos: pos}
	end 'create'
end 'Cur'

function makeBump(coll Coll) returns Bump
	let h = Cur.create(coll, pos: 0)
	let it = try h.coll.items.createIterator() otherwise panic("empty")
	return function() gives bumpBox(it.current())
end 'makeBump'

function main() returns ExitCode
	let coll = Coll.create()
	coll.add(Box.create(1))
	let frozen = freeze coll.clone()
	let g = makeBump(frozen)
	print("{runIt(g)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:54:19: 'makeBump' writes 'coll' (it calls 'makeBump$closure_0', which calls 'bumpBox', which writes 'box'); 'frozen' is immutable
```

<!-- test: error.a-mutates-captures-closure-inside-a-mutates-captures-closure-over-an-iterator-is-refused-for-a-frozen-array -->
The inner closure writes through the iterator the outer closure captured, so the outer closure writes the array too and the frozen array is refused.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

function makeBump(boxes BoxArray) returns Bump
	let it = try boxes.createIterator() otherwise panic("empty")
	return function() gives runIt(function() gives bumpBox(it.current()))
end 'makeBump'

function main() returns ExitCode
	var fill = BoxArray.create(0) as isolated BoxArray

	modify fill 'fill'
		fill.push(Box.create(1))
	end 'fill'

	let frozen = freeze fill
	let g = makeBump(frozen)
	print("{runIt(g)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:36:19: 'makeBump' writes 'boxes' (it calls 'makeBump$closure_0', which writes the data it captured); 'frozen' is immutable
```

<!-- test: error.a-mutates-captures-closure-over-a-user-iterator-is-refused-for-a-frozen-array -->
An iterator type of the program's own that holds the array is captured the same way: the closure writes through it and the frozen array is refused.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

type Cursor
	var boxes as BoxArray
	var pos as Count

	static function over(boxes BoxArray) returns Self
		return Self{boxes: boxes, pos: 0}
	end 'over'

	function current() returns Box
		return try boxes.get(pos) otherwise panic("empty")
	end 'current'
end 'Cursor'

function bumpFirst(boxes BoxArray) returns Count
	let it = Cursor.over(boxes)
	let g = function() gives bumpBox(it.current())
	return runIt(g)
end 'bumpFirst'

function main() returns ExitCode
	var fill = BoxArray.create(0) as isolated BoxArray

	modify fill 'fill'
		fill.push(Box.create(1))
	end 'fill'

	let frozen = freeze fill
	print("{bumpFirst(frozen)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:50:20: 'bumpFirst' writes 'boxes' (line 38: a result read from it is stored in a writable slot (it)); 'frozen' is immutable
```

<!-- test: error.a-plain-closure-hands-an-element-of-a-captured-iterator-to-an-immutable-parameter -->
`peekFirst` is handed a mutable array, and the closure passes the element its iterator reads to an `immutable` parameter, which would share mutable data as frozen data; the element is refused where the closure uses it.

```maxon
typealias Peek = function() returns Count

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function look(box immutable Box) returns Count
	return box.count
end 'look'

function runIt(g Peek) returns Count
	return g()
end 'runIt'

function peekFirst(boxes BoxArray) returns Count
	let it = try boxes.createIterator() otherwise panic("empty")
	let g = function() gives look(it.current())
	return runIt(g)
end 'peekFirst'

function main() returns ExitCode
	var boxes = BoxArray.create(0)
	boxes.push(Box.create(1))
	print("{peekFirst(boxes)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:24:32: 'look' takes `immutable` 'box'; 'it.current()' is mutable. Freeze it first
error E3207: <fragment>:31:20: 'peekFirst' needs 'boxes' frozen (it calls 'peekFirst$closure_0', which calls 'look', which takes `immutable` 'box'); 'boxes' is mutable. Freeze it first
```

<!-- test: a-plain-closure-reads-elements-through-a-captured-iterator-over-a-frozen-array -->
A closure that only reads through its iterator leaves the array alone, so `peekFirst` takes the frozen array.

```maxon
typealias Peek = function() returns Count

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function runIt(g Peek) returns Count
	return g()
end 'runIt'

function peekFirst(boxes BoxArray) returns Count
	let it = try boxes.createIterator() otherwise panic("empty")
	let g = function() gives it.current().count
	return runIt(g)
end 'peekFirst'

function main() returns ExitCode
	var fill = BoxArray.create(0) as isolated BoxArray

	modify fill 'fill'
		fill.push(Box.create(1))
	end 'fill'

	let frozen = freeze fill
	print("{peekFirst(frozen)}\n")
	return 0
end 'main'
```
```stdout
1
```

<!-- test: a-mutates-captures-closure-writes-through-an-iterator-over-a-mutable-array -->
The array handed to `bumpFirst` is mutable, so the closure's write through its iterator is allowed and the caller sees it.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

function bumpFirst(boxes BoxArray) returns Count
	let it = try boxes.createIterator() otherwise panic("empty")
	let g = function() gives bumpBox(it.current())
	return runIt(g)
end 'bumpFirst'

function main() returns ExitCode
	var boxes = BoxArray.create(0)
	let first = Box.create(1)
	boxes.push(first)
	print("{bumpFirst(boxes)} {first.count}\n")
	return 0
end 'main'
```
```stdout
2 2
```

<!-- test: a-returned-mutates-captures-closure-writes-through-an-iterator-over-a-mutable-array -->
`makeBump` returns the closure over a mutable array's iterator, and calling it on that array writes the element.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function makeBump(boxes BoxArray) returns Bump
	let it = try boxes.createIterator() otherwise panic("empty")
	return function() gives bumpBox(it.current())
end 'makeBump'

function main() returns ExitCode
	var boxes = BoxArray.create(0)
	let first = Box.create(1)
	boxes.push(first)
	let g = makeBump(boxes)
	print("{g()} {first.count}\n")
	return 0
end 'main'
```
```stdout
2 2
```

<!-- test: error.a-mutates-captures-closure-over-a-with-iterator-pair-is-refused-for-a-frozen-array -->
The iterator `for (it, _) in boxes.withIterator()` binds runs over `boxes`, so the closure that writes through it writes the array, and the frozen array is refused where `bumpAll` is called.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

function bumpAll(boxes BoxArray) returns Count
	var total = 0 as Count

	for (it, _) in boxes.withIterator() 'each'
		let g = function() gives bumpBox(it.current())
		total = total + runIt(g)
	end 'each'

	return total
end 'bumpAll'

function main() returns ExitCode
	var fill = BoxArray.create(0) as isolated BoxArray

	modify fill 'fill'
		fill.push(Box.create(1))
	end 'fill'

	let frozen = freeze fill
	print("{bumpAll(frozen)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:42:18: 'bumpAll' writes 'boxes' (it calls 'bumpAll$closure_0', which calls 'bumpBox', which writes 'box'); 'frozen' is immutable
```

<!-- test: error.a-mutates-captures-closure-over-an-iterator-held-in-a-tuple-is-refused-for-a-frozen-array -->
The iterator is held in a tuple (`(it, 1)`) and the closure writes through `pair.0`, so the closure still writes the array the iterator runs over and the frozen array is refused.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

function bumpFirst(boxes BoxArray) returns Count
	let it = try boxes.createIterator() otherwise panic("empty")
	let pair = (it, 1)
	let g = function() gives bumpBox(pair.0.current())
	return runIt(g)
end 'bumpFirst'

function main() returns ExitCode
	var fill = BoxArray.create(0) as isolated BoxArray

	modify fill 'fill'
		fill.push(Box.create(1))
	end 'fill'

	let frozen = freeze fill
	print("{bumpFirst(frozen)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:38:20: 'bumpFirst' writes 'boxes' (it calls 'bumpFirst$closure_0', which calls 'bumpBox', which writes 'box'); 'frozen' is immutable
```

<!-- test: a-mutates-captures-closure-writes-through-a-with-iterator-pair-over-a-mutable-array -->
The array handed to `bumpAll` is mutable, so the closure's write through the iterator the loop binds is allowed and the caller sees both writes.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

function bumpAll(boxes BoxArray) returns Count
	var total = 0 as Count

	for (it, _) in boxes.withIterator() 'each'
		let g = function() gives bumpBox(it.current())
		total = total + runIt(g)
	end 'each'

	return total
end 'bumpAll'

function main() returns ExitCode
	var boxes = BoxArray.create(0)
	let first = Box.create(1)
	boxes.push(first)
	boxes.push(Box.create(10))
	print("{bumpAll(boxes)} {first.count}\n")
	return 0
end 'main'
```
```stdout
13 2
```

<!-- test: a-mutates-captures-closure-writes-through-an-iterator-held-in-a-tuple-over-a-mutable-array -->
The array handed to `bumpFirst` is mutable, so the closure's write through the iterator in the tuple is allowed and the caller sees it.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

function bumpFirst(boxes BoxArray) returns Count
	let it = try boxes.createIterator() otherwise panic("empty")
	let pair = (it, 1)
	let g = function() gives bumpBox(pair.0.current())
	return runIt(g)
end 'bumpFirst'

function main() returns ExitCode
	var boxes = BoxArray.create(0)
	let first = Box.create(1)
	boxes.push(first)
	print("{bumpFirst(boxes)} {first.count}\n")
	return 0
end 'main'
```
```stdout
2 2
```

<!-- test: a-returned-mutates-captures-closure-hands-back-an-element-of-an-iterator-over-a-mutable-array -->
`makeFetch` returns a `mutates captures` closure that hands back the element its iterator reads. The caller passes a mutable array, so the element is the array's own and a write through the result reaches `first`.

```maxon
typealias Fetch = function() returns Box mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function makeFetch(boxes BoxArray) returns Fetch
	let it = try boxes.createIterator() otherwise panic("empty")
	return function() gives it.current()
end 'makeFetch'

function main() returns ExitCode
	var boxes = BoxArray.create(0)
	let first = Box.create(1)
	boxes.push(first)
	let g = makeFetch(boxes)
	let got = g()
	got.count = got.count + 5
	print("{first.count}\n")
	return 0
end 'main'
```
```stdout
6
```

<!-- test: error.a-mutates-captures-closure-over-an-iterator-held-in-a-record-is-refused-for-a-frozen-array -->
The iterator is held in a `Lens` record the closure captures, so the closure that writes through `lens.it` writes the array the iterator runs over, and the frozen array is refused.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box
typealias BoxIter = ArrayIterator with Box

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

type Lens
	export var it as BoxIter

	static function over(it BoxIter) returns Self
		return Self{it: it}
	end 'over'
end 'Lens'

function bumpFirst(boxes BoxArray) returns Count
	let lens = Lens.over(try boxes.createIterator() otherwise panic("empty"))
	let g = function() gives bumpBox(lens.it.current())
	return runIt(g)
end 'bumpFirst'

function main() returns ExitCode
	var fill = BoxArray.create(0) as isolated BoxArray

	modify fill 'fill'
		fill.push(Box.create(1))
	end 'fill'

	let frozen = freeze fill
	print("{bumpFirst(frozen)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:46:20: 'bumpFirst' writes 'boxes' (it calls 'bumpFirst$closure_0', which calls 'bumpBox', which writes 'box'); 'frozen' is immutable
```

<!-- test: a-mutates-captures-closure-writes-through-an-iterator-held-in-a-record-over-a-mutable-array -->
The array handed to `bumpFirst` is mutable, so the closure's write through the iterator the `Lens` holds is allowed and the caller sees it.

```maxon
typealias Bump = function() returns Count mutates captures

type Box
	export var count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box
typealias BoxIter = ArrayIterator with Box

function bumpBox(box Box) returns Count
	box.count = box.count + 1
	return box.count
end 'bumpBox'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

type Lens
	export var it as BoxIter

	static function over(it BoxIter) returns Self
		return Self{it: it}
	end 'over'
end 'Lens'

function bumpFirst(boxes BoxArray) returns Count
	let lens = Lens.over(try boxes.createIterator() otherwise panic("empty"))
	let g = function() gives bumpBox(lens.it.current())
	return runIt(g)
end 'bumpFirst'

function main() returns ExitCode
	var boxes = BoxArray.create(0)
	let first = Box.create(1)
	boxes.push(first)
	print("{bumpFirst(boxes)} {first.count}\n")
	return 0
end 'main'
```
```stdout
2 2
```
