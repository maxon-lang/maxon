---
feature: harness-gate-a-worker-reads-the-librarys-names-once-for-every-claims-pass-south
---
# The a-worker-reads-the-librarys-names-once-for-every-claims-pass gate: south

Three plain run cases that share no type name, so the three run batched in one program.

## Tests

<!-- test: claims-south.span -->

```maxon
typealias SouthWidth = int(0 to 100)

type SouthSpan
	export var low as SouthWidth
	export var high as SouthWidth

	static function create(low SouthWidth, high SouthWidth) returns Self
		return Self{low: low, high: high}
	end 'create'
end 'SouthSpan'

function southWidth(span SouthSpan) returns SouthWidth
	return span.high - span.low
end 'southWidth'

function main() returns ExitCode
	let span = SouthSpan.create(4, high: 11)
	print("south span {southWidth(span)}\n")
	return southWidth(span) as ExitCode
end 'main'
```

```exitcode
7
```

```stdout
south span 7
```

<!-- test: claims-south.signal -->

```maxon
typealias SouthLevel = int(0 to 1000)

enum SouthSignal
	raised
	lowered
end 'SouthSignal'

function southLevel(signal SouthSignal) returns SouthLevel
	match signal 'pick'
		raised then return 4
		lowered then return 6
	end 'pick'
end 'southLevel'

function main() returns ExitCode
	print("south signal {southLevel(SouthSignal.raised)}\n")
	return southLevel(SouthSignal.raised) as ExitCode
end 'main'
```

```exitcode
4
```

```stdout
south signal 4
```

<!-- test: claims-south.tag -->

```maxon
type SouthTag
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'SouthTag'

function southCaption(tag SouthTag) returns String
	return "south tag {tag.text}"
end 'southCaption'

function main() returns ExitCode
	let tag = SouthTag.create("cape")
	print("{southCaption(tag)}\n")
	return 2
end 'main'
```

```exitcode
2
```

```stdout
south tag cape
```
