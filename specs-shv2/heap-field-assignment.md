---
feature: heap-field-assignment
status: stable
keywords: [field, assignment, memory, ownership, struct]
category: memory-management
---

# Heap-Pointer Field Assignment

## Documentation

### Assigning to Heap-Pointer Fields

Struct fields that hold heap-pointer types (other structs, enums with associated values) can be assigned directly. When the struct is freed, its destructor decrefs each managed field, freeing them if no other references remain.

```text
container.child = newChild
```

## Tests

<!-- test: basic-self-field-assign -->
Assign to a struct field on self and verify the new value is stored.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Container
	export var value as Integer
	export var child as Inner

	export function replaceChild(newChild Inner)
		child = newChild
	end 'replaceChild'

	static function create(value Integer, child Inner) returns Self
		return Self{value: value, child: child}
	end 'create'
end 'Container'

function main() returns ExitCode
	var c = Container.create(1, child: Inner.create(10))
	c.replaceChild(Inner.create(20))
	if c.child.value == 20 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: qualified-field-assign -->
Assign to a field on a variable via qualified access.
```maxon
typealias Integer = int(i64.min to i64.max)

type Right
	export var left as Integer

	static function create(left Integer) returns Self
		return Self{left: left}
	end 'create'
end 'Right'

type Pair
	export var left as Integer
	export var right as Right

	static function create(left Integer, right Right) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function main() returns ExitCode
	var p = Pair.create(5, right: Right.create(10))
	p.right = Right.create(20)
	if p.right.left == 20 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: scalar-field-assign-ok -->
Direct assignment to a scalar (non-heap-pointer) field is allowed.
```maxon
typealias Tally = int(0 to u64.max)

type Counter
	var count as Tally

	export function increment()
		count = count + 1
	end 'increment'

	export function value() returns Tally
		return count
	end 'value'

	static function create(count Tally) returns Self
		return Self{count: count}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create(0)
	c.increment()
	c.increment()
	return c.value()
end 'main'
```
```exitcode
2
```

<!-- test: memory.self-field-overwrite-frees-old -->
Overwrite a self field in a method and verify all allocations are freed properly.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Container
	export var value as Integer
	export var child as Inner

	export function replaceChild(newChild Inner)
		child = newChild
	end 'replaceChild'

	export function childValue() returns Integer
		return child.value
	end 'childValue'

	static function create(value Integer, child Inner) returns Self
		return Self{value: value, child: child}
	end 'create'
end 'Container'

function testAssign()
	var c = Container.create(1, child: Inner.create(10))
	c.replaceChild(Inner.create(20))
	print("{c.childValue()}\n")
end 'testAssign'

function main() returns ExitCode
	testAssign()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
20
```

<!-- test: memory.qualified-field-overwrite-frees-old -->
Overwrite a qualified field and verify all allocations are freed properly.
```maxon
typealias Integer = int(i64.min to i64.max)

type Right
	export var left as Integer

	static function create(left Integer) returns Self
		return Self{left: left}
	end 'create'
end 'Right'

type Pair
	export var left as Integer
	export var right as Right

	static function create(left Integer, right Right) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function testAssign()
	var p = Pair.create(5, right: Right.create(10))
	p.right = Right.create(20)
	print("{p.right.left}\n")
end 'testAssign'

function main() returns ExitCode
	testAssign()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
20
```
