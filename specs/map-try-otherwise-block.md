---
feature: map-try-otherwise-block
status: stable
keywords: [map, try, otherwise, block, throw, struct]
category: collections
---
# Map Try Otherwise Block

## Documentation

Map.get with `try...otherwise 'label'...end` block form should correctly resolve the value type.

## Tests

<!-- test: map-get-try-otherwise-block-string -->
Map.get with try-otherwise block form returns correct String type.
```maxon
typealias StringMap = Map with (String, String)

enum TestError implements Error
		notFound
end 'TestError'

function getValue(m StringMap, key String) returns String throws TestError
		let entry = try m.get(key) otherwise 'missing'
				throw TestError.notFound
		end 'missing'
		return entry
end 'getValue'

function main() returns ExitCode
		var m = StringMap.empty()
		m.insert("key", value: "hello")
		let got = try getValue(m, key: "key") otherwise "none"
		if got == "hello" 'ok'
				return 1
		end 'ok'
		return 0
end 'main'
```
```exitcode
1
```

<!-- test: map-get-try-otherwise-block-struct -->
Map.get with try-otherwise block form returns correct struct type.
```maxon
typealias SmallInt = int(0 to u8.max)

type Entry
		export var name String
		export var count SmallInt

		static function create(name String, count SmallInt) returns Self
			return Self{name: name, count: count}
		end 'create'
end 'Entry'

typealias EntryMap = Map with (String, Entry)

enum TestError implements Error
		notFound
end 'TestError'

function getEntry(m EntryMap, key String) returns Entry throws TestError
		let entry = try m.get(key) otherwise 'missing'
				throw TestError.notFound
		end 'missing'
		return entry
end 'getEntry'

function main() returns ExitCode
		var m = EntryMap.empty()
		m.insert("key", value: Entry.create(name: "hello", count: 7))
		let got = try getEntry(m, key: "key") otherwise Entry.create(name: "none", count: 0)
		return got.count
end 'main'
```
```exitcode
7
```
