---
feature: function-overloads
status: stable
keywords: [function, overload, disambiguation, parameter, types]
category: functions
---

## Documentation

### Function Overloads

Maxon supports function overloading — multiple functions with the same name but different signatures.

#### Disambiguation by parameter types

When overloads differ in their parameter types, the compiler automatically selects the correct overload based on the argument types at the call site:

```text
function process(value int) returns int
  return value * 2
end 'process'

function process(value String) returns int
  return value.count()
end 'process'

process(42)        // calls process(value int)
process("hello")   // calls process(value String)
```

#### Disambiguation by parameter names

When overloads have different parameter names, the caller uses named arguments to select the correct overload:

```text
function create(name String) returns String
  return name
end 'create'

function create(label String) returns String
  return label
end 'create'

create(name: "foo")    // calls first overload
create(label: "bar")   // calls second overload
```

#### Ambiguous calls

If the compiler cannot determine which overload to call based on argument types alone, it requires named arguments. Calling an ambiguous overload without named arguments is a compile error.

## Tests

<!-- test: basic-type-disambiguation -->
```maxon
typealias Integer = int(i64.min to i64.max)

function process(value Integer) returns Integer
  return value * 2
end 'process'

function process(value String) returns Integer
  return value.count()
end 'process'

function main() returns ExitCode
  return process(21)
end 'main'
```
```exitcode
42
```

<!-- test: basic-type-disambiguation-string -->
```maxon
typealias Integer = int(i64.min to i64.max)

function process(value Integer) returns Integer
  return value * 2
end 'process'

function process(value String) returns Integer
  return value.count()
end 'process'

function main() returns ExitCode
  return process("hello world hello world hello world hello worl!")
end 'main'
```
```exitcode
47
```

<!-- test: name-disambiguation-preserved -->
```maxon
typealias Integer = int(i64.min to i64.max)

function slice(start Integer, endIndex Integer) returns Integer
  return endIndex - start
end 'slice'

function slice(start Integer, length Integer) returns Integer
  return start + length
end 'slice'

function main() returns ExitCode
  return slice(10, length: 32)
end 'main'
```
```exitcode
42
```

<!-- test: error.ambiguous-same-signature -->
```maxon
typealias Integer = int(i64.min to i64.max)

function create(name String) returns Integer
  return name.count()
end 'create'

function create(label String) returns Integer
  return label.count()
end 'create'

function main() returns ExitCode
  return create("hello")
end 'main'
```
```maxoncstderr
error E3007: specs/fragments/function-overloads/error.ambiguous-same-signature.test:13:10: Ambiguous overload for 'create': multiple overloads match. Candidates: (name String), (label String)
```

<!-- test: ambiguous-same-signature-with-named-args -->
```maxon
typealias Integer = int(i64.min to i64.max)

function create(name String) returns Integer
  return name.count()
end 'create'

function create(label String) returns Integer
  return label.count() + 1
end 'create'

function main() returns ExitCode
  return create(label: "hello world hello world hello world hello worl!")
end 'main'
```
```exitcode
48
```

<!-- test: method-type-disambiguation -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Converter
  function convert(value Integer) returns Integer
    return value * 2
  end 'convert'

  function convert(value String) returns Integer
    return value.count()
  end 'convert'
end 'Converter'

function main() returns ExitCode
  let c = Converter{}
  return c.convert(21)
end 'main'
```
```exitcode
42
```

<!-- test: variable-type-inference -->
```maxon
typealias Integer = int(i64.min to i64.max)

function process(value Integer) returns Integer
  return value * 2
end 'process'

function process(value String) returns Integer
  return value.count()
end 'process'

function main() returns ExitCode
  let x = 21
  return process(x)
end 'main'
```
```exitcode
42
```

<!-- test: string-contains-char -->
```maxon
function main() returns ExitCode
  let text = "hello"
  if text.contains('e') 'check'
    return 1
  end 'check' else 'other'
    return 0
  end 'other'
end 'main'
```
```exitcode
1
```

<!-- test: string-contains-string -->
```maxon
function main() returns ExitCode
  let text = "hello world"
  if text.contains("world") 'check'
    return 1
  end 'check' else 'other'
    return 0
  end 'other'
end 'main'
```
```exitcode
1
```

<!-- test: bool-type-disambiguation -->
```maxon
typealias Integer = int(i64.min to i64.max)

function check(value Integer) returns Integer
  return value
end 'check'

function check(value bool) returns Integer
  if value 'branch'
    return 1
  end 'branch' else 'other'
    return 0
  end 'other'
end 'check'

function main() returns ExitCode
  return check(true)
end 'main'
```
```exitcode
1
```

<!-- test: float-type-disambiguation -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Decimal = float(f64.min to f64.max)

function measure(value Integer) returns Integer
  return value
end 'measure'

function measure(value Decimal) returns Integer
  return trunc(value)
end 'measure'

function main() returns ExitCode
  return measure(42.0)
end 'main'
```
```exitcode
42
```
