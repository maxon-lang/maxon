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
var scores = ColorMap.create()
try scores.insert(Color.red, value: 100) otherwise ignore
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
	let m = ColorMap.create()
	try m.insert(Color.red, value: 10) otherwise ignore
	try m.insert(Color.green, value: 20) otherwise ignore
	try m.insert(Color.blue, value: 30) otherwise ignore
	let result = try m.get(Color.green) otherwise 0
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
	let m = StatusMap.create()
	try m.insert(HttpStatus.ok, value: "OK") otherwise ignore
	try m.insert(HttpStatus.notFound, value: "Not Found") otherwise ignore
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
	let m = PlanetMap.create()
	try m.insert(Planet.earth, value: 1) otherwise ignore
	try m.insert(Planet.mars, value: 2) otherwise ignore
	try m.insert(Planet.venus, value: 3) otherwise ignore
	let result = try m.get(Planet.mars) otherwise 0
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
	let d = Direction.south
	let h = d.hash()
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
	let a = Color.green
	let b = Color.green
	let c = Color.red
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
	let m = ContainerMap.create()
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
	let m = GradeMap.create()
	try m.insert(Grade.excellent, value: 100) otherwise ignore
	try m.insert(Grade.good, value: 85) otherwise ignore
	let result = try m.get(Grade.excellent) otherwise 0
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
	let m = ColorMap.create()
	try m.insert(Color.red, value: 10) otherwise ignore
	try m.insert(Color.green, value: 20) otherwise ignore
	_ = m.remove(Color.red)
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

var categoryLevels = CategoryLevelMap.create()

function setCategoryLevel(category LogCategory, level LogLevel)
	try categoryLevels.insert(category, value: level) otherwise ignore
end 'setCategoryLevel'

function getCategoryLevel(category LogCategory) returns LogLevel
	return try categoryLevels.get(category) otherwise LogLevel.NONE
end 'getCategoryLevel'

function main() returns ExitCode
	setCategoryLevel(LogCategory.compiler, level: LogLevel.INFO)
	setCategoryLevel(LogCategory.lexer, level: LogLevel.ERROR)
	let compilerLevel = getCategoryLevel(LogCategory.compiler)
	let lexerLevel = getCategoryLevel(LogCategory.lexer)
	let r1 = match compilerLevel 'c'
		INFO gives true
		NONE gives false
		ERROR gives false
	end 'c'
	let r2 = match lexerLevel 'l'
		ERROR gives true
		NONE gives false
		INFO gives false
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
	let m = [Color.red: 10, Color.green: 20, Color.blue: 30]
	for (color, score) in m 'loop'
		m.upsert(color, value: score + 1)
	end 'loop'
	let result = try m.get(Color.red) otherwise 0
	return result
end 'main'
```
```exitcode
11
```
