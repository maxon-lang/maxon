---
feature: enum-hashable
status: experimental
keywords: [enum, hashable, equatable, map, hash, equals]
category: type-system
---

# Enum Hashable & Equatable

## Documentation

Enums without associated values automatically conform to `Hashable` and `Equatable`. This allows them to be used as map keys, call `.hash()` and `.equals()`, and satisfy where-clause constraints.

The implementation delegates to the backing type:
- `hash()` returns `self.rawValue.hash()`
- `equals(other)` returns `self.rawValue.equals(other.rawValue)`

For simple enums, the raw value is the ordinal (an `int`). For backed enums (int, float, String, Character), the raw value is the explicit backing value.

Enums with associated values do **not** automatically conform — they have no single raw value to hash.

### Example

```text
enum Color
  red
  green
  blue
end 'Color'

typealias Int = int(i64.min to i64.max)
typealias ColorMap = Map with (Color, Int)
var scores = ColorMap{}
scores.insert(Color.red, value: 100)
```

## Tests

<!-- test: simple-enum-as-map-key -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

typealias Int = int(i64.min to i64.max)
typealias ColorMap = Map with (Color, Int)

function main() returns ExitCode
  var m = ColorMap{}
  m.insert(Color.red, value: 10)
  m.insert(Color.green, value: 20)
  m.insert(Color.blue, value: 30)
  var result = try m.get(Color.green) otherwise 0
  return result
end 'main'
```
```exitcode
20
```

<!-- test: int-backed-enum-as-map-key -->
```maxon
enum HttpStatus
  ok = 200
  notFound = 404
  serverError = 500
end 'HttpStatus'

typealias StatusMap = Map with (HttpStatus, String)

function main() returns ExitCode
  var m = StatusMap{}
  m.insert(HttpStatus.ok, value: "OK")
  m.insert(HttpStatus.notFound, value: "Not Found")
  if m.contains(HttpStatus.notFound) 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: string-backed-enum-as-map-key -->
```maxon
enum Planet
  earth = "Earth"
  mars = "Mars"
  venus = "Venus"
end 'Planet'

typealias Int = int(i64.min to i64.max)
typealias PlanetMap = Map with (Planet, Int)

function main() returns ExitCode
  var m = PlanetMap{}
  m.insert(Planet.earth, value: 1)
  m.insert(Planet.mars, value: 2)
  m.insert(Planet.venus, value: 3)
  var result = try m.get(Planet.mars) otherwise 0
  return result
end 'main'
```
```exitcode
2
```

<!-- test: enum-hash-direct -->
```maxon
enum Direction
  north
  south
  east
  west
end 'Direction'

function main() returns ExitCode
  var d = Direction.south
  var h = d.hash()
  return h
end 'main'
```
```exitcode
1
```

<!-- test: enum-equals-direct -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var a = Color.green
  var b = Color.green
  var c = Color.red
  if a.equals(b) 'eq'
    if a.equals(c) 'neq'
      return 0
    end 'neq'
    return 1
  end 'eq'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: error.associated-value-enum-not-hashable -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Container
  empty
  value(n Integer)
end 'Container'

typealias ContainerMap = Map with (Container, Integer)

function main() returns ExitCode
  var m = ContainerMap{}
  return 0
end 'main'
```
```maxoncstderr
error E3017: specs/fragments/enum-hashable/error.associated-value-enum-not-hashable.test:10:11: Type 'Container' does not satisfy constraint 'Hashable' required by type parameter 'Key' of 'Map'
```

<!-- test: char-backed-enum-as-map-key -->
```maxon
enum Grade
  excellent = 'A'
  good = 'B'
  average = 'C'
end 'Grade'

typealias Int = int(i64.min to i64.max)
typealias GradeMap = Map with (Grade, Int)

function main() returns ExitCode
  var m = GradeMap{}
  m.insert(Grade.excellent, value: 100)
  m.insert(Grade.good, value: 85)
  var result = try m.get(Grade.excellent) otherwise 0
  return result
end 'main'
```
```exitcode
100
```

<!-- test: enum-map-remove -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

typealias Int = int(i64.min to i64.max)
typealias ColorMap = Map with (Color, Int)

function main() returns ExitCode
  var m = ColorMap{}
  m.insert(Color.red, value: 10)
  m.insert(Color.green, value: 20)
  let _ = m.remove(Color.red)
  if m.contains(Color.red) 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: global-enum-map -->
```maxon
enum LogLevel
  NONE
  ERROR
  INFO
end 'LogLevel'

enum LogCategory
  compiler
  lexer
  parser
end 'LogCategory'

typealias CategoryLevelMap = Map with (LogCategory, LogLevel)

var categoryLevels = CategoryLevelMap{}

function setCategoryLevel(category LogCategory, level LogLevel)
  categoryLevels.insert(category, value: level)
end 'setCategoryLevel'

function getCategoryLevel(category LogCategory) returns LogLevel
  return try categoryLevels.get(category) otherwise LogLevel.NONE
end 'getCategoryLevel'

function main() returns ExitCode
  setCategoryLevel(LogCategory.compiler, level: LogLevel.INFO)
  setCategoryLevel(LogCategory.lexer, level: LogLevel.ERROR)
  var compilerLevel = getCategoryLevel(LogCategory.compiler)
  var lexerLevel = getCategoryLevel(LogCategory.lexer)
  var r1 = match compilerLevel 'c'
    LogLevel.INFO gives true
    LogLevel.NONE gives false
    LogLevel.ERROR gives false
  end 'c'
  var r2 = match lexerLevel 'l'
    LogLevel.ERROR gives true
    LogLevel.NONE gives false
    LogLevel.INFO gives false
  end 'l'
  if r1 and r2 'both'
    return 1
  end 'both'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-map-for-in-insert -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var m = [Color.red: 10, Color.green: 20, Color.blue: 30]
  for (color, score) in m 'loop'
    m.insert(color, value: score + 1)
  end 'loop'
  var result = try m.get(Color.red) otherwise 0
  return result
end 'main'
```
```exitcode
11
```
