---
feature: init-from-literal
status: stable
keywords: [InitableFromStringLiteral, InitableFromCharLiteral, literals, interface, cast]
category: type-system
---

# InitableFrom*Literal Interfaces

## Documentation

### InitableFromStringLiteral

Types conforming to `InitableFromStringLiteral` can be initialized from string literals using cast syntax. The `init` method receives a `String`:

```maxon
typealias Score = int(i64.min to i64.max)

type MyString implements InitableFromStringLiteral
	var _value String

	static function init(value String) returns MyString
		return MyString{_value: value}
	end 'init'

	export function len() returns Score
		return _value.byteLength()
	end 'len'
end 'MyString'

function main() returns ExitCode
	var ms = MyString from "hello"
	print("{ms.len()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

### InitableFromCharLiteral

Types conforming to `InitableFromCharLiteral` can be initialized from character literals. The `init` method receives a `Character`:

```maxon
typealias Score = int(i64.min to i64.max)

type MyChar implements InitableFromCharLiteral
	var _value Character

	static function init(value Character) returns MyChar
		return MyChar{_value: value}
	end 'init'

	export function len() returns Score
		return _value.byteLength()
	end 'len'
end 'MyChar'

function main() returns ExitCode
	var mc = MyChar from 'A'
	print("{mc.len()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

## Tests

<!-- test: init-from-string-literal-basic -->
```maxon

typealias Integer = int(i64.min to i64.max)

// User-defined type that wraps a String and can be created from string literals
type Wrapper implements InitableFromStringLiteral
	var _value String

	static function init(value String) returns Wrapper
		return Wrapper{_value: value}
	end 'init'

	export function len() returns Integer
		return _value.byteLength()
	end 'len'
end 'Wrapper'

function main() returns ExitCode
	var w = Wrapper from "hello"
	print("{w.len()}\n")
	return 0
end 'main'
```
```stdout
5
```

<!-- test: init-from-string-literal-empty -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Wrapper implements InitableFromStringLiteral
	var _value String

	static function init(value String) returns Wrapper
		return Wrapper{_value: value}
	end 'init'

	export function len() returns Integer
		return _value.byteLength()
	end 'len'
end 'Wrapper'

function main() returns ExitCode
	var w = Wrapper from ""
	print("len: {w.len()}\n")
	return 0
end 'main'
```
```stdout
len: 0
```

<!-- test: init-from-char-literal-basic -->
```maxon

typealias Integer = int(i64.min to i64.max)

type CharWrapper implements InitableFromCharLiteral
	var _value Character

	static function init(value Character) returns CharWrapper
		return CharWrapper{_value: value}
	end 'init'

	export function len() returns Integer
		return _value.byteLength()
	end 'len'
end 'CharWrapper'

function main() returns ExitCode
	var cw = CharWrapper from 'X'
	print("{cw.len()}\n")
	return 0
end 'main'
```
```stdout
1
```
