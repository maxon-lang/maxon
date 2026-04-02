---
feature: map-struct-bytearray
status: stable
keywords: [map, struct, bytearray, nested]
category: collections
---
# Map Struct ByteArray

## Documentation

Storing and retrieving structs that contain ByteArray (Array with Byte) fields from a Map.

## Tests

<!-- test: map-struct-with-bytearray-field -->
Map.get returns struct with ByteArray field intact.
```maxon
typealias SmallInt = int(0 to u8.max)
typealias Byte = byte(0 to u8.max)
typealias ByteArray = Array with Byte

type Entry
	export var data ByteArray
	export var tag SmallInt

	static function create(data ByteArray, tag SmallInt) returns Self
		return Self{data: data, tag: tag}
	end 'create'
end 'Entry'

typealias EntryMap = Map with (String, Entry)

function main() returns ExitCode
	var m = EntryMap.empty()
	var arr = ByteArray.empty()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	let e = Entry.create(data: arr, tag: 42)
	m.insert("hello", value: e)
	let got = try m.get("hello") otherwise Entry.create(data: ByteArray.empty(), tag: 0)
	print("{got.data.count()}\n")
	print("{got.tag}\n")
	let b = try got.data.get(1) otherwise 0
	print("{b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
42
20
```

<!-- test: struct-field-access-in-untaken-if-branch -->
Accessing a struct field inside an untaken if branch must not corrupt function parameters.
Uses a module-level struct with a Map field (same pattern as QueryDatabase).
```maxon
typealias Byte = byte(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Count = int(0 to u64.max)

type Entry
	export var data ByteArray
	export var tag Count
end 'Entry'

typealias EntryMap = Map with (String, Entry)
typealias StringArray = Array with String

type Database
	export var sourceFiles EntryMap
	export var sourcePaths StringArray

	static function create(sourceFiles EntryMap, sourcePaths StringArray) returns Self
		return Self{sourceFiles: sourceFiles, sourcePaths: sourcePaths}
	end 'create'
end 'Database'

var db = Database.create(sourceFiles: EntryMap.empty(), sourcePaths: StringArray.empty())

function storeAndCheck(key String, data ByteArray)
	if db.sourceFiles.contains(key) 'exists'
		let existing = try db.sourceFiles.get(key) otherwise 'skip'
			return
		end 'skip'
		let existingCount = existing.data.count()
		print("existing: {existingCount}\n")
	end 'exists' else 'newFile'
		print("new\n")
	end 'newFile'
	print("{data.count()}\n")
end 'storeAndCheck'

function main() returns ExitCode
	var arr = ByteArray.empty()
	arr.push(1)
	arr.push(2)
	arr.push(3)
	storeAndCheck("hello", data: arr)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
new
3
```

<!-- test: multi-file-struct-param-after-global-branch -->
Multi-file version: accessing global struct fields inside an untaken if branch must not corrupt function parameters across file boundaries.
```maxon
// --- file: 0-Types.maxon
typealias Byte = byte(0 to u8.max)
export typealias ByteArray = Array with Byte
export typealias Count = int(0 to u64.max)

export type Entry
	export var data ByteArray
	export var tag Count
end 'Entry'

export typealias EntryMap = Map with (String, Entry)
export typealias StringArray = Array with String

export type Database
	export var sourceFiles EntryMap
	export var sourcePaths StringArray

	export static function create(sourceFiles EntryMap, sourcePaths StringArray) returns Self
		return Self{sourceFiles: sourceFiles, sourcePaths: sourcePaths}
	end 'create'
end 'Database'

export var db = Database.create(sourceFiles: EntryMap.empty(), sourcePaths: StringArray.empty())

// --- file: 1-Logic.maxon
export function storeAndCheck(key String, data ByteArray)
	if db.sourceFiles.contains(key) 'exists'
		let existing = try db.sourceFiles.get(key) otherwise 'skip'
			return
		end 'skip'
		let existingCount = existing.data.count()
		print("existing: {existingCount}\n")
	end 'exists' else 'newFile'
		print("new\n")
	end 'newFile'
	print("{data.count()}\n")
end 'storeAndCheck'

function main() returns ExitCode
	var arr = ByteArray.empty()
	arr.push(1)
	arr.push(2)
	arr.push(3)
	storeAndCheck("hello", data: arr)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
new
3
```
