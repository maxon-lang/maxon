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
  export var n Integer      // accessible from outside
  var private Integer       // only accessible within methods
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
  var count Integer              // private
  export var name Integer     // public

  function increment()
    count = count + 1      // OK - accessing within method
  end 'increment'

  export function getCount() returns Integer
    return count           // OK - accessing within method
  end 'getCount'
end 'Counter'

function main() returns Integer
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
  export var n Integer
  var private Integer

  static function create() returns Self
    return {n: 42, private: 100}
  end 'create'
end 'Value'

function main() returns Integer
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
  export var n Integer

  static function create() returns Self
    return {n: 10}
  end 'create'
end 'Value'

function main() returns Integer
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
  export let version Integer

  static function create() returns Self
    return {version: 42}
  end 'create'
end 'Config'

function main() returns Integer
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
  var count Integer

  function increment()
    count = count + 1
  end 'increment'

  function getCount() returns Integer
    return count
  end 'getCount'
end 'Counter'

function main() returns Integer
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
  export var value Integer
  var secret Integer

  static function create(v Integer, s Integer) returns Self
    return {value: v, secret: s}
  end 'create'

  function getSecret() returns Integer
    return secret
  end 'getSecret'
end 'Box'

function main() returns Integer
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
  var private Integer

  static function create() returns Self
    return {private: 42}
  end 'create'
end 'Value'

function main() returns Integer
  var v = Value.create()
  return v.private
end 'main'
```
```maxoncstderr
error E3014: specs/fragments/export-var-fields/error.unexported-field-read.test:12:12: cannot access unexported field: 'private' outside of type 'Value'
```

<!-- test: error.unexported-field-write -->
```maxon
type Value
  var private Integer

  static function create() returns Self
    return {private: 0}
  end 'create'
end 'Value'

function main() returns Integer
  var v = Value.create()
  v.private = 42
  return 0
end 'main'
```
```maxoncstderr
error E3014: specs/fragments/export-var-fields/error.unexported-field-write.test:12:5: cannot access unexported field: 'private' outside of type 'Value'
```

<!-- test: all-fields-private-by-default -->
```maxon
type Simple
  var x Integer
  var y Integer

  static function make(a Integer, b Integer) returns Self
    return {x: a, y: b}
  end 'make'

  function sum() returns Integer
    return x + y
  end 'sum'
end 'Simple'

function main() returns Integer
  var s = Simple.make(20, b: 22)
  return s.sum()
end 'main'
```
```exitcode
42
```
