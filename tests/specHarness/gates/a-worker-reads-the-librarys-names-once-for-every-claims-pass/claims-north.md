---
feature: harness-gate-a-worker-reads-the-librarys-names-once-for-every-claims-pass-north
---
# The a-worker-reads-the-librarys-names-once-for-every-claims-pass gate: north

Three plain run cases that share no type name, so the three run batched in one program.

## Tests

<!-- test: claims-north.pair -->

```maxon
typealias NorthSum = int(0 to 100)

type NorthPair
	export var left as NorthSum
	export var right as NorthSum

	static function create(left NorthSum, right NorthSum) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'NorthPair'

function northTotal(pair NorthPair) returns NorthSum
	return pair.left + pair.right
end 'northTotal'

function main() returns ExitCode
	let pair = NorthPair.create(2, right: 3)
	print("north pair {northTotal(pair)}\n")
	return northTotal(pair) as ExitCode
end 'main'
```

```exitcode
5
```

```stdout
north pair 5
```

<!-- test: claims-north.heading -->

```maxon
typealias NorthBearing = int(0 to 1000)

enum NorthHeading
	ahead
	astern
end 'NorthHeading'

function northBearing(heading NorthHeading) returns NorthBearing
	match heading 'pick'
		ahead then return 9
		astern then return 1
	end 'pick'
end 'northBearing'

function main() returns ExitCode
	print("north heading {northBearing(NorthHeading.ahead)}\n")
	return northBearing(NorthHeading.ahead) as ExitCode
end 'main'
```

```exitcode
9
```

```stdout
north heading 9
```

<!-- test: claims-north.label -->

```maxon
type NorthLabel
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'NorthLabel'

function northCaption(label NorthLabel) returns String
	return "north label {label.text}"
end 'northCaption'

function main() returns ExitCode
	let label = NorthLabel.create("pole")
	print("{northCaption(label)}\n")
	return 3
end 'main'
```

```exitcode
3
```

```stdout
north label pole
```
