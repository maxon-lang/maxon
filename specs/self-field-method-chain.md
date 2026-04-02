---
feature: self-field-method-chain
status: experimental
keywords: [self, field, method, chain, nested]
category: type-system
---

# Self Field Method Chain

## Documentation

Methods on struct types can call methods on their own fields using `self.field.method()` syntax. This supports arbitrary chain depth like `self.field.subfield.method()`.

## Tests

<!-- test: self-field-push -->
Call a method on a struct field via self.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Container
		export var items IntArray

		function addItem(value Integer)
				self.items.push(value)
		end 'addItem'

		function getCount() returns Integer
				return self.items.count()
		end 'getCount'

		static function create(items IntArray) returns Self
			return Self{items: items}
		end 'create'
end 'Container'

function main() returns ExitCode
		var c = Container.create(items: IntArray.empty())
		c.addItem(10)
		c.addItem(20)
		return c.getCount()
end 'main'
```
```exitcode
2
```

<!-- test: self-field-get -->
Call a throwing method on a struct field via self.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type NumberList
		export var data IntArray

		function add(value Integer)
				self.data.push(value)
		end 'add'

		function first() returns Integer
				return try self.data.get(0) otherwise -1
		end 'first'

		static function create(data IntArray) returns Self
			return Self{data: data}
		end 'create'
end 'NumberList'

function main() returns ExitCode
		var list = NumberList.create(data: IntArray.empty())
		list.add(42)
		return list.first()
end 'main'
```
```exitcode
42
```

<!-- test: self-nested-field-method -->
Call a method on a nested struct field via self.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Inner
		export var values IntArray

		function total() returns Integer
				return self.values.count()
		end 'total'

		static function create(values IntArray) returns Self
			return Self{values: values}
		end 'create'
end 'Inner'

type Outer
		export var inner Inner

		function getInnerCount() returns Integer
				return self.inner.values.count()
		end 'getInnerCount'

		static function create(inner Inner) returns Self
			return Self{inner: inner}
		end 'create'
end 'Outer'

function main() returns ExitCode
		var inner = Inner.create(values: [1, 2, 3])
		var outer = Outer.create(inner: inner)
		return outer.getInnerCount()
end 'main'
```
```exitcode
3
```

<!-- test: self-field-method-assignment -->
Mutate a struct field's sub-object via self chain.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Accumulator
		export var items IntArray

		function addAndCount(value Integer) returns Integer
				self.items.push(value)
				return self.items.count()
		end 'addAndCount'

		static function create(items IntArray) returns Self
			return Self{items: items}
		end 'create'
end 'Accumulator'

function main() returns ExitCode
		var acc = Accumulator.create(items: IntArray.empty())
		let _ = acc.addAndCount(1)
		let _ = acc.addAndCount(2)
		return acc.addAndCount(3)
end 'main'
```
```exitcode
3
```
