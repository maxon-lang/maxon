---
feature: unions
status: experimental
keywords: [union, enumeration, associated values]
category: type-system
---

# Unions

## Documentation

# Unions

Unions define a type with a fixed set of named variants called cases. Maxon unions support simple unions and unions with associated values.

### Simple Unions

The simplest form of union defines named cases with no additional data:

```maxon
union Direction
  north
  south
  east
  west
end 'Direction'
```

Create union values using dot notation:

```maxon
var dir = Direction.north
```

### Union Methods

Unions can have methods, similar to structs:

```maxon
union Direction
  north
  south
  east
  west

  function opposite() returns Direction
    match self 'check'
      north then return Direction.south
      south then return Direction.north
      east then return Direction.west
      west then return Direction.east
    end 'check'
  end 'opposite'

  function isVertical() returns bool
    var result = match self 'check'
      north gives true
      south gives true
      east gives false
      west gives false
    end 'check'
    return result
  end 'isVertical'
end 'Direction'
```

Call methods using instance-dot-method syntax:

```maxon
var dir = Direction.north
var opp = dir.opposite()    // Direction.south
var vert = dir.isVertical() // true
```

## Tests

<!-- test: simple-union -->
```maxon
union Direction
  north
  south
  east
  west
end 'Direction'

function main() returns ExitCode
  var dir = Direction.north
  var result = match dir 'check'
    north gives 1
    south gives 0
    east gives 0
    west gives 0
  end 'check'
  return result
end 'main'
```
```exitcode
1
```

<!-- test: union-assignment -->
```maxon
union Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var c = Color.red
  c = Color.blue
  var result = match c 'check'
    red gives 0
    green gives 0
    blue gives 1
  end 'check'
  return result
end 'main'
```
```exitcode
1
```

<!-- test: union-not-equal -->
```maxon
union Status
  pending
  active
  done
end 'Status'

function main() returns ExitCode
  var s = Status.pending
  var result = match s 'check'
    active gives 0
    pending gives 1
    done gives 1
  end 'check'
  return result
end 'main'
```
```exitcode
1
```

<!-- test: union-comparison -->
```maxon
union Status
  pending
  active
  done
end 'Status'

function main() returns ExitCode
  var s1 = Status.pending
  match s1 'check'
    pending then return 1
    active then return 0
    done then return 0
  end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: union-function-param -->
```maxon
union Status
  on
  off
end 'Status'

function isOn(s Status) returns bool
  var result = match s 'check'
    on gives true
    off gives false
  end 'check'
  return result
end 'isOn'

function main() returns ExitCode
  var status = Status.on
  if isOn(status) 'test'
    return 1
  end 'test'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: union-return-type -->
```maxon
union Result
  success
  failure
end 'Result'

function getResult(succeed bool) returns Result
  if succeed 'check'
    return Result.success
  end 'check'
  return Result.failure
end 'getResult'

function main() returns ExitCode
  var r = getResult(true)
  var result = match r 'handle'
    success gives 1
    failure gives 0
  end 'handle'
  return result
end 'main'
```
```exitcode
1
```

<!-- test: float-backed -->
```maxon
union FloatBacked
  North = 1.1
  South = 2.2
  East = 3.3
end 'FloatBacked'

function main() returns ExitCode
  var f = FloatBacked.North
  var result = match f 'check'
    North gives 1
    South gives 0
    East gives 0
  end 'check'
  return result
end 'main'
```
```exitcode
1
```

<!-- test: union-method -->
```maxon
union Direction
  north
  south

  function isNorth() returns bool
    var result = match self 'check'
      north gives true
      south gives false
    end 'check'
    return result
  end 'isNorth'
end 'Direction'

function main() returns ExitCode
  let d = Direction.north
  if d.isNorth() 'test'
    return 1
  end 'test'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: union-method-returns-union -->
```maxon
union Toggle
  on
  off

  function flip() returns Toggle
    var result = match self 'check'
      on gives Toggle.off
      off gives Toggle.on
    end 'check'
    return result
  end 'flip'
end 'Toggle'

function main() returns ExitCode
  let t = Toggle.on
  let flipped = t.flip()
  var result = match flipped 'check'
    off gives 1
    on gives 0
  end 'check'
  return result
end 'main'
```
```exitcode
1
```

<!-- test: error.duplicate-case -->
```maxon
union Color
  red
  red
end 'Color'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3030: specs/fragments/unions-simple/error.duplicate-case.test:4:3: duplicate union case: 'red'
```

<!-- test: error.unknown-union-case -->
```maxon
union Color
  red
  blue
end 'Color'

function main() returns ExitCode
  let _c = Color.green
  return 0
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/unions-simple/error.unknown-union-case.test:8:12: unknown union case: 'green'
```

<!-- test: error.duplicate-raw-value -->
```maxon
union Status
  ok = 200
  success = 200
end 'Status'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3031: specs/fragments/unions-simple/error.duplicate-raw-value.test:4:3: duplicate raw value: '200'
```

<!-- test: error.raw-value-type-mismatch -->
```maxon
union Status
  ok = 100
  fail = 5.0
end 'Status'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3032: specs/fragments/unions-simple/error.raw-value-type-mismatch.test:4:3: raw value type mismatch: 'expected int, got float'
```

<!-- test: keyword-as-case-name -->
Keywords can be used as union case names (e.g., `function`, `return`, `end`).
```maxon
union TokenType
  function
  return
  end
  if
  else
  let
  var
  identifier
end 'TokenType'

function main() returns ExitCode
  var t = TokenType.function
  var result = match t 'check'
    function gives 1
    return gives 0
    end gives 0
    if gives 0
    else gives 0
    let gives 0
    var gives 0
    identifier gives 0
  end 'check'
  return result
end 'main'
```
```exitcode
1
```
