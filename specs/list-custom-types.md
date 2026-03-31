---
feature: list-custom-types
status: experimental
keywords: [list, linked list, struct, custom type, generic]
category: collections
---
# List with Custom Types

## Documentation

`List` should work with custom struct types, not just primitives. When using `List with MyStruct`, all operations (append, prepend, get, iterate, remove) should work correctly with user-defined types.

## Tests

<!-- test: list-of-structs-basic -->
### Basic List of Structs
Create a list of custom structs and retrieve elements.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer
end 'Point'

typealias PointList = List with Point

function main() returns ExitCode
	var list = PointList{}
	list.append(Point{x: 10, y: 20})
	list.append(Point{x: 30, y: 40})
	var first = try list.first() otherwise Point{x: 0, y: 0}
	var second = try list.get(1) otherwise Point{x: 0, y: 0}
	print("{first.x}\n")
	print("{first.y}\n")
	print("{second.x}\n")
	print("{second.y}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
30
40
```

<!-- test: list-of-structs-iterate -->
### Iterate Over List of Structs
```maxon
typealias Integer = int(i64.min to i64.max)

type Entry
	export var id Integer
	export var value Integer
end 'Entry'

typealias EntryList = List with Entry

function main() returns ExitCode
	var list = EntryList{}
	list.append(Entry{id: 1, value: 100})
	list.append(Entry{id: 2, value: 200})
	list.append(Entry{id: 3, value: 300})
	var sum = 0
	for item in list 'loop'
		sum = sum + item.value
	end 'loop'
	print("{sum}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
600
```

<!-- test: list-of-structs-with-string -->
### List of Structs with Managed String Fields
```maxon
typealias Integer = int(i64.min to i64.max)

type Person
	export var name String
	export var age Integer
end 'Person'

typealias PersonList = List with Person

function main() returns ExitCode
	var list = PersonList{}
	list.append(Person{name: "Alice has a long name for heap", age: 30})
	list.append(Person{name: "Bob also has a long name for heap", age: 25})
	var first = try list.first() otherwise Person{name: "", age: 0}
	print("{first.name}\n")
	print("{first.age}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Alice has a long name for heap
30
```

<!-- test: list-of-structs-prepend-remove -->
### Prepend and Remove Structs
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
	export var a Integer
	export var b Integer
end 'Pair'

typealias PairList = List with Pair

function main() returns ExitCode
	var list = PairList{}
	list.prepend(Pair{a: 3, b: 30})
	list.prepend(Pair{a: 2, b: 20})
	list.prepend(Pair{a: 1, b: 10})
	var removed = try list.removeFirst() otherwise Pair{a: 0, b: 0}
	print("{removed.a}\n")
	print("{removed.b}\n")
	print("{list.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
10
2
```

<!-- test: list-of-structs-from-literal -->
### List from Array Literal of Structs
```maxon
typealias Integer = int(i64.min to i64.max)

type Vec2
	export var x Integer
	export var y Integer
end 'Vec2'

function main() returns ExitCode
	var list = List from [Vec2{x: 1, y: 2}, Vec2{x: 3, y: 4}, Vec2{x: 5, y: 6}]
	for item in list 'loop'
		print("{item.x},{item.y}\n")
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1,2
3,4
5,6
```
