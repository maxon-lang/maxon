---
feature: enum-allcases
status: experimental
keywords: [enum, allCases, iteration, array]
category: type-system
---

## Documentation

# Enum allCases

All enums have a static `.allCases` property that returns an `Array` of all cases in declaration order:

```text
enum Color
  red
  green
  blue
end 'Color'

for color in Color.allCases 'loop'
  print("{color.name}\n")
end 'loop'
// Prints: red green blue

var count = Color.allCases.count()  // 3
```

This works with all backing types (simple, int, float, string, char). The array always contains the cases in their declaration order.

`.allCases` is not available on enums with associated values.

## Tests

### Simple Enum

<!-- test: enum-allcases.simple -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	for color in Color.allCases 'loop'
		print("{color.name}\n")
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
red
green
blue
```

### Count

<!-- test: enum-allcases.count -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	let count = Color.allCases.count()
	if count == 3 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Int-Backed Enum

<!-- test: enum-allcases.int-backed -->
```maxon
enum HttpStatus
	ok = 200
	notFound = 404
	serverError = 500
end 'HttpStatus'

function main() returns ExitCode
	for status in HttpStatus.allCases 'loop'
		print("{status.name}={status.rawValue}\n")
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ok=200
notFound=404
serverError=500
```

### Float-Backed Enum

<!-- test: enum-allcases.float-backed -->
```maxon
enum Threshold
	low = 0.1
	medium = 0.5
	high = 0.9
end 'Threshold'

function main() returns ExitCode
	let count = Threshold.allCases.count()
	if count == 3 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### String-Backed Enum

<!-- test: enum-allcases.string-backed -->
```maxon
enum ContentType
	json = "application/json"
	html = "text/html"
	plain = "text/plain"
end 'ContentType'

function main() returns ExitCode
	for ct in ContentType.allCases 'loop'
		print("{ct.rawValue}\n")
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
application/json
text/html
text/plain
```

### Char-Backed Enum

<!-- test: enum-allcases.char-backed -->
```maxon
enum Grade
	a = 'A'
	b = 'B'
	c = 'C'
end 'Grade'

function main() returns ExitCode
	for g in Grade.allCases 'loop'
		print("{g.rawValue}")
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ABC
```

### Single Case

<!-- test: enum-allcases.single-case -->
```maxon
enum Singleton
	only
end 'Singleton'

function main() returns ExitCode
	let count = Singleton.allCases.count()
	if count == 1 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Error: allCases on Enum with Associated Values

<!-- test: enum-allcases.error-enum -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	for s in Container.allCases 'loop'
		print("x")
	end 'loop'
	return 0
end 'main'
```
```maxoncstderr
error E4006: specs/fragments/enum-allcases/enum-allcases.error-enum.test:10:11: allCases is not available on enums with associated values
```
