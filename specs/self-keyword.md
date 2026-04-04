---
feature: self-keyword
status: stable
keywords: [self, Self, method, type, instance]
category: type-system
---

# Self and self Keywords

## Documentation

### The `self` Keyword

Inside instance methods, `self` refers to the current instance:

```text
type Counter
  var count int

  function increment()
    self.count = self.count + 1
  end 'increment'
end 'Counter'
```

Field access can omit `self.` when unambiguous:

```text
type Counter
  var count int

  function increment()
    count = count + 1
  end 'increment'
end 'Counter'
```

### The `Self` Type

`Self` refers to the enclosing type, useful for method signatures:

```text
type Point
  var x int
  var y int

  function origin() returns Self
    return Point{x: 0, y: 0}
  end 'origin'
end 'Point'
```

## Tests

<!-- test: self-explicit-access -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter
	var count Integer

	function increment()
		self.count = self.count + 1
	end 'increment'

	function get() returns Integer
		return self.count
	end 'get'

	static function create(count Integer) returns Self
		return Self{count: count}
	end 'create'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(count: 0)
	c.increment()
	c.increment()
	return c.get()
end 'main'
```
```exitcode
2
```

<!-- test: self-implicit-access -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter
	var count Integer

	function increment()
		count = count + 1
	end 'increment'

	function get() returns Integer
		return count
	end 'get'

	static function create(count Integer) returns Self
		return Self{count: count}
	end 'create'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(count: 0)
	c.increment()
	c.increment()
	c.increment()
	return c.get()
end 'main'
```
```exitcode
3
```

<!-- test: self-with-params -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Accumulator
	var total Integer

	function add(value Integer)
		self.total = self.total + value
	end 'add'

	function getTotal() returns Integer
		return self.total
	end 'getTotal'

	static function create(total Integer) returns Self
		return Self{total: total}
	end 'create'
end 'Accumulator'

function main() returns ExitCode
	let acc = Accumulator.create(total: 0)
	acc.add(10)
	acc.add(20)
	acc.add(12)
	return acc.getTotal()
end 'main'
```
```exitcode
42
```

<!-- test: self-multiple-fields -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	var x Integer
	var y Integer

	function sum() returns Integer
		return self.x + self.y
	end 'sum'

	function setX(newX Integer)
		self.x = newX
	end 'setX'

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let p = Point.create(x: 10, y: 32)
	return p.sum()
end 'main'
```
```exitcode
42
```

<!-- test: self-modify-and-return -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Value
	var n Integer

	function double()
		self.n = self.n * 2
	end 'double'

	function get() returns Integer
		return self.n
	end 'get'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Value'

function main() returns ExitCode
	let v = Value.create(n: 21)
	v.double()
	return v.get()
end 'main'
```
```exitcode
42
```

<!-- test: self-implicit-multiple-fields -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Rectangle
	var width Integer
	var height Integer

	function area() returns Integer
		return width * height
	end 'area'

	static function create(width Integer, height Integer) returns Self
		return Self{width: width, height: height}
	end 'create'
end 'Rectangle'

function main() returns ExitCode
	let r = Rectangle.create(width: 6, height: 7)
	return r.area()
end 'main'
```
```exitcode
42
```
