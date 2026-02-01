---
feature: export-var-fields
status: in-progress
keywords: [export, var, field, visibility, encapsulation]
category: type-system
---

# Export Var Fields

## Documentation

### Field Visibility

Type fields can be marked with `export` to make them accessible from outside the type. By default, fields are private and can only be accessed within the type's methods.

```maxon
type Value
  export var n int      // accessible from outside
  var private int       // only accessible within methods
end 'Value'
```

### Accessing Exported Fields

Exported fields can be read and written (if mutable) from any code:

```maxon
var v = Value{n: 42, private: 100}
print(v.n)           // OK - n is exported
v.n = 50             // OK - n is exported and mutable
```

### Private Fields

Non-exported fields are only accessible within methods of the same type:

```maxon
type Counter
  var count int              // private
  export var name int     // public

  function increment()
    count = count + 1      // OK - accessing within method
  end 'increment'

  export function getCount() returns int
    return count           // OK - accessing within method
  end 'getCount'
end 'Counter'

function main() returns int
  var c = Counter{count: 0, name: 5}
  c.increment()
  return c.getCount()
  // c.count would be an error - not exported
end 'main'
```
```exitcode
1
```

### Initialization

All fields (exported or not) can be initialized in struct literals, since the type itself determines what fields exist. The visibility only affects access after construction.

## Tests

<!-- test: export-var-basic -->
```maxon
type Value
  export var n int
  var private int

  static function create() returns Self
    return {n: 42, private: 100}
  end 'create'
end 'Value'

function main() returns int
  var v = Value.create()
  return v.n
end 'main'
```
```exitcode
42
```

<!-- test: export-var-write -->
```maxon
type Value
  export var n int

  static function create() returns Self
    return {n: 10}
  end 'create'
end 'Value'

function main() returns int
  var v = Value.create()
  v.n = 42
  return v.n
end 'main'
```
```exitcode
42
```

<!-- test: export-let-readonly -->
```maxon
type Config
  export let version int

  static function create() returns Self
    return {version: 42}
  end 'create'
end 'Config'

function main() returns int
  var c = Config.create()
  return c.version
end 'main'
```
```exitcode
42
```

<!-- test: private-field-in-method -->
```maxon
type Counter
  var count int

  function increment()
    count = count + 1
  end 'increment'

  function getCount() returns int
    return count
  end 'getCount'
end 'Counter'

function main() returns int
  var c = Counter{count: 40}
  c.increment()
  c.increment()
  return c.getCount()
end 'main'
```
```exitcode
42
```

<!-- test: mixed-export-fields -->
```maxon
type Box
  export var value int
  var secret int

  static function create(v int, s int) returns Self
    return {value: v, secret: s}
  end 'create'

  function getSecret() returns int
    return secret
  end 'getSecret'
end 'Box'

function main() returns int
  var b = Box.create(20, s: 22)
  return b.value + b.getSecret()
end 'main'
```
```exitcode
42
```

<!-- test: error.unexported-field-read -->
```maxon
type Value
  var private int

  static function create() returns Self
    return {private: 42}
  end 'create'
end 'Value'

function main() returns int
  var v = Value.create()
  return v.private
end 'main'
```
```maxoncstderr
error E3013: specs/fragments/export-var-fields/error.unexported-field-read.test:12:12: cannot access unexported field: 'private' outside of type 'Value'
```

<!-- test: error.unexported-field-write -->
```maxon
type Value
  var private int

  static function create() returns Self
    return {private: 0}
  end 'create'
end 'Value'

function main() returns int
  var v = Value.create()
  v.private = 42
  return 0
end 'main'
```
```maxoncstderr
error E3013: specs/fragments/export-var-fields/error.unexported-field-write.test:12:5: cannot access unexported field: 'private' outside of type 'Value'
```

<!-- test: all-fields-private-by-default -->
```maxon
type Simple
  var x int
  var y int

  static function make(a int, b int) returns Self
    return {x: a, y: b}
  end 'make'

  function sum() returns int
    return x + y
  end 'sum'
end 'Simple'

function main() returns int
  var s = Simple.make(20, b: 22)
  return s.sum()
end 'main'
```
```exitcode
42
```
