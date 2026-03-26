---
feature: type-methods
status: experimental
keywords: [type, method, function, self, Self, static, export]
category: type-system
---

# Type Methods

## Documentation

Types can contain methods - functions that operate on type instances.

### Instance Methods

Instance methods automatically receive `self` as the current instance:

```text
type Counter
  var count int

  function increment()
    count = count + 1
  end 'increment'

  function get() returns int
    return count
  end 'get'
end 'Counter'
```

Call methods using dot notation:
```text
var c = Counter{count: 0}
c.increment()
var value = c.get()
```

### Static Methods

Static methods don't have access to `self`:

```text
type Math
  static function square(x int) returns int
    return x * x
  end 'square'
end 'Math'

var result = Math.square(5)  // 25
```

### Export Modifier

Use `export` to make methods visible outside the module:

```text
type PublicAPI
  export function doSomething() returns int
    return 42
  end 'doSomething'
end 'PublicAPI'
```

### Field Access in Methods

Methods can access fields directly without `self.` prefix:

```text
type Point
  var x int
  var y int

  function magnitude() returns int
    return x * x + y * y
  end 'magnitude'
end 'Point'
```

## Tests

<!-- test: type-method-basic -->
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
end 'Counter'

function main() returns ExitCode
	var c = Counter{count: 0}
	c.increment()
	return c.get()
end 'main'
```
```exitcode
1
```

<!-- test: type-method-with-params -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Adder
	var total Integer

	function add(value Integer)
		total = total + value
	end 'add'

	function getTotal() returns Integer
		return total
	end 'getTotal'
end 'Adder'

function main() returns ExitCode
	var a = Adder{total: 0}
	a.add(10)
	a.add(32)
	return a.getTotal()
end 'main'
```
```exitcode
42
```

<!-- test: type-method-returning-value -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Calculator
	var value Integer

	function double() returns Integer
		return value * 2
	end 'double'
end 'Calculator'

function main() returns ExitCode
	var c = Calculator{value: 21}
	return c.double()
end 'main'
```
```exitcode
42
```

<!-- test: type-multiple-methods -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter
	var count Integer

	function increment()
		count = count + 1
	end 'increment'

	function decrement()
		count = count - 1
	end 'decrement'

	function reset()
		count = 0
	end 'reset'

	function get() returns Integer
		return count
	end 'get'
end 'Counter'

function main() returns ExitCode
	var c = Counter{count: 10}
	c.increment()
	c.increment()
	c.decrement()
	return c.get()
end 'main'
```
```exitcode
11
```

<!-- test: type-method-chain -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Value
	var n Integer

	function add(x Integer)
		n = n + x
	end 'add'

	function get() returns Integer
		return n
	end 'get'
end 'Value'

function main() returns ExitCode
	var v = Value{n: 0}
	v.add(10)
	v.add(20)
	v.add(12)
	return v.get()
end 'main'
```
```exitcode
42
```
