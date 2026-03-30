---
feature: match-jump-table
status: experimental
keywords: [match, enum, jump-table, optimization]
category: control-flow
---

# Match Jump Table Optimization

## Documentation

Enum match statements with 4 or more simple cases are optimized to use a jump table instead of a linear comparison chain. This provides O(1) dispatch instead of O(n) sequential comparisons.

The optimization applies when:
- The match is on an enum (simple or enum tag)
- There are 4 or more cases
- All cases are simple enum case patterns (no multi-pattern OR, no ranges)
- Ordinals are dense (0 to N-1)

## Tests

<!-- test: jump-table.four-cases-statement -->
```maxon
enum Direction
		north
		south
		east
		west
end 'Direction'

function main() returns ExitCode
		var d = Direction.west
		match d 'dir'
				north then print("N")
				south then print("S")
				east then print("E")
				west then print("W")
		end 'dir'
		return 0
end 'main'
```
```stdout
W
```

<!-- test: jump-table.four-cases-expression -->
```maxon
enum Direction
		north
		south
		east
		west
end 'Direction'

function main() returns ExitCode
		var d = Direction.east
		let code = match d 'dir'
				north gives 10
				south gives 20
				east gives 30
				west gives 40
		end 'dir'
		return code
end 'main'
```
```exitcode
30
```

<!-- test: jump-table.seven-cases-first -->
```maxon
enum Weekday
		mon
		tue
		wed
		thu
		fri
		sat
		sun
end 'Weekday'

function main() returns ExitCode
		var w = Weekday.mon
		let name = match w 'w'
				mon gives "Monday"
				tue gives "Tuesday"
				wed gives "Wednesday"
				thu gives "Thursday"
				fri gives "Friday"
				sat gives "Saturday"
				sun gives "Sunday"
		end 'w'
		print(name)
		return 0
end 'main'
```
```stdout
Monday
```

<!-- test: jump-table.seven-cases-last -->
```maxon
enum Weekday
		mon
		tue
		wed
		thu
		fri
		sat
		sun
end 'Weekday'

function main() returns ExitCode
		var w = Weekday.sun
		let name = match w 'w'
				mon gives "Monday"
				tue gives "Tuesday"
				wed gives "Wednesday"
				thu gives "Thursday"
				fri gives "Friday"
				sat gives "Saturday"
				sun gives "Sunday"
		end 'w'
		print(name)
		return 0
end 'main'
```
```stdout
Sunday
```

<!-- test: jump-table.seven-cases-middle -->
```maxon
enum Weekday
		mon
		tue
		wed
		thu
		fri
		sat
		sun
end 'Weekday'

function main() returns ExitCode
		var w = Weekday.thu
		let name = match w 'w'
				mon gives "Monday"
				tue gives "Tuesday"
				wed gives "Wednesday"
				thu gives "Thursday"
				fri gives "Friday"
				sat gives "Saturday"
				sun gives "Sunday"
		end 'w'
		print(name)
		return 0
end 'main'
```
```stdout
Thursday
```

<!-- test: jump-table.five-cases-with-print -->
```maxon
enum Suit
		hearts
		diamonds
		clubs
		spades
		joker
end 'Suit'

function main() returns ExitCode
		var s = Suit.clubs
		match s 'check'
				hearts then print("hearts")
				diamonds then print("diamonds")
				clubs then print("clubs")
				spades then print("spades")
				joker then print("joker")
		end 'check'
		return 0
end 'main'
```
```stdout
clubs
```

<!-- test: jump-table.default-panic -->
```maxon
enum Color
		red
		green
		blue
		yellow
end 'Color'

function main() returns ExitCode
		var c = Color.blue
		match c 'check'
				red then print("red")
				green then print("green")
				blue then print("blue")
				yellow then print("yellow")
				default panic("unknown")
		end 'check'
		return 0
end 'main'
```
```stdout
blue
```

<!-- test: jump-table.enum-with-associated-values -->
```maxon
typealias ID = int(i64.min to i64.max)

enum Shape
		circle(r ID)
		square(s ID)
		triangle(b ID, h ID)
		rectangle(w ID, h ID)
end 'Shape'

function describe(s Shape) returns String
		return match s 'sh'
				circle gives "circle"
				square gives "square"
				triangle gives "triangle"
				rectangle gives "rectangle"
		end 'sh'
end 'describe'

function main() returns ExitCode
		print(describe(Shape.triangle(3, 4)))
		print(describe(Shape.circle(5)))
		print(describe(Shape.rectangle(2, 3)))
		print(describe(Shape.square(4)))
		return 0
end 'main'
```
```stdout
trianglecirclerectanglesquare
```
