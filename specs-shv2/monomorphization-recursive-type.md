---
feature: monomorphization-recursive-type
status: experimental
keywords: [monomorphization, recursive, type, nesting, list, array]
category: compiler
---

## Documentation

# Recursive Type Monomorphization

When a generic collection's element type is itself a generic collection (e.g., `List<List<Integer>>`),
monomorphization must specialize methods for the nested type without entering infinite recursion.

The `map()` and `enumerated()` extension methods on `Iterable` return new generic types
(`Array with Element`, `EnumeratedIterator with Iter, Element`). If monomorphization eagerly
specializes these for every new concrete type, it can create unbounded nesting:
`List<List<X>>` -> `List<List<List<X>>>` -> ...

The compiler detects this recursive nesting pattern and stops specialization before it diverges.

## Tests

### List of Lists - Basic

<!-- test: list-of-lists.basic -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer
typealias IntListList = List with IntList

function main() returns ExitCode
	var inner1 = IntList.create()
	inner1.append(1)
	inner1.append(2)
	var inner2 = IntList.create()
	inner2.append(3)
	inner2.append(4)
	var outer = IntListList.create()
	outer.append(inner1)
	outer.append(inner2)
	print("{outer.count()}\n")
	let first = try outer.first() otherwise IntList.create()
	print("{first.count()}\n")
	let val = try first.get(0) otherwise 0
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
2
1
```

### Array of Lists

<!-- test: array-of-lists -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
	var list1 = IntList.create()
	list1.append(10)
	list1.append(20)
	var list2 = IntList.create()
	list2.append(30)
	var arr = [list1, list2]
	print("{arr.count()}\n")
	let first = try arr.get(0) otherwise IntList.create()
	print("{first.count()}\n")
	let val = try first.get(0) otherwise 0
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
2
10
```
