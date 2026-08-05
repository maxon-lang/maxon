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
		var m = StringMap.create()
		try m.insert("key", value: "hello") otherwise ignore
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
		export var name as String
		export var count as SmallInt

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
		var m = EntryMap.create()
		try m.insert("key", value: Entry.create("hello", count: 7)) otherwise ignore
		let got = try getEntry(m, key: "key") otherwise Entry.create("none", count: 0)
		return got.count
end 'main'
```
```exitcode
7
```

<!-- test: try-block-throwing-method-on-param-array -->
Block-form `try 'l' ... end 'l' otherwise (e) ...` whose body bare-calls a throwing method (Array.get/Array.set) on a PARAMETER-typed array receiver routes to the shared handler — the parser must recover the receiver's struct type through its generic typealias to find Array.get's throws clause.
```maxon
typealias Code = int(0 to 125)
typealias CodeArray = Array with Code

function bump(a CodeArray)
		try 'update'
				let old = a.get(0)
				a.set(0, value: old + 1)
		end 'update' otherwise (e) 'bad'
				match e 'kind'
						indexOutOfBounds then panic("oob")
						emptySlot then panic("empty")
				end 'kind'
		end 'bad'
end 'bump'

function main() returns ExitCode
		var a = CodeArray.create()
		a.push(41)
		bump(a)
		return try a.get(0) otherwise 99
end 'main'
```
```exitcode
42
```

<!-- test: try-block-forward-declared-throwing-call -->
Block-form `try` body that bare-calls a free function declared LATER in the file: the header prescan registers the forward function's throws clause so the body's call is recognized as throwing (no false E3083).
```maxon
enum BumpError implements Error
		tooBig
end 'BumpError'

function run() returns ExitCode
		try 'work'
				step()
		end 'work' otherwise (e) 'bad'
				match e 'kind'
						tooBig then return 7
				end 'kind'
		end 'bad'
		return 3
end 'run'

function step() throws BumpError
		throw BumpError.tooBig
end 'step'

function main() returns ExitCode
		return run()
end 'main'
```
```exitcode
7
```
