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
	var value as String

	static function init(value String) returns MyString
		return MyString{value: value}
	end 'init'

	export function len() returns Score
		return value.byteLength()
	end 'len'
end 'MyString'

function main() returns ExitCode
	let ms = MyString from "hello"
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
	var value as Character

	static function init(value Character) returns MyChar
		return MyChar{value: value}
	end 'init'

	export function len() returns Score
		return value.byteLength()
	end 'len'
end 'MyChar'

function main() returns ExitCode
	let mc = MyChar from 'A'
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
	var value as String

	static function init(value String) returns Wrapper
		return Wrapper{value: value}
	end 'init'

	export function len() returns Integer
		return value.byteLength()
	end 'len'
end 'Wrapper'

function main() returns ExitCode
	let w = Wrapper from "hello"
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
	var value as String

	static function init(value String) returns Wrapper
		return Wrapper{value: value}
	end 'init'

	export function len() returns Integer
		return value.byteLength()
	end 'len'
end 'Wrapper'

function main() returns ExitCode
	let w = Wrapper from ""
	print("len: {w.len()}\n")
	return 0
end 'main'
```
```stdout
len: 0
```

### From a character literal

`<Type> from 'A'` is the same sugar over `stdlib/Builtins.maxon:47`'s
`interface InitableFromCharLiteral`, whose `init` takes a `Character` where the String form's takes a
`String`. It desugars to the identical `<Type>.init(<the literal>)` call, so the arity check, the argument
type check, the ownership of the returned struct and the E3004 for a type with no `init` are the ones every
other static call gets.

<!-- test: init-from-char-literal-basic -->
```maxon

typealias Integer = int(i64.min to i64.max)

type CharWrapper implements InitableFromCharLiteral
	var value as Character

	static function init(value Character) returns CharWrapper
		return CharWrapper{value: value}
	end 'init'

	export function len() returns Integer
		return value.byteLength()
	end 'len'
end 'CharWrapper'

function main() returns ExitCode
	let cw = CharWrapper from 'X'
	print("{cw.len()}\n")
	return 0
end 'main'
```
```stdout
1
```

<!-- test: init-from-char-literal-multibyte -->
The literal's BYTES are what the record carries, so a character outside ASCII arrives whole rather than
truncated to its first byte. `byteLength()` is the discriminating read — a `Character` that lost its
continuation bytes would answer 1.
```maxon
typealias Integer = int(i64.min to i64.max)

type CharWrapper implements InitableFromCharLiteral
	var value as Character

	static function init(value Character) returns CharWrapper
		return CharWrapper{value: value}
	end 'init'

	export function len() returns Integer
		return value.byteLength()
	end 'len'
end 'CharWrapper'

function main() returns ExitCode
	let cw = CharWrapper from '€'
	print("{cw.len()}\n")
	return 0
end 'main'
```
```stdout
3
```

<!-- test: init-from-char-literal-at-module-scope -->
⭐⭐ **THE SAME CONSTRUCTION AS A TOP-LEVEL INITIALIZER, AND IT IS THE EDGE THE BODY FORM DOES NOT COVER.**
A module-scope initializer is evaluated by a separate walk that folds constants rather than emitting code, so
a form the body path claims and that walk does not reads as `E2004 Undefined constant` — a message about a
constant nobody wrote. Both forms are recognised by one predicate over one `LiteralInitForm`, which is what
keeps the pair from drifting.

⚠ **A BARE `let c = '€'` AT MODULE SCOPE IS STILL REFUSED, AND PERMANENTLY** — a `Character` is a RECORD and a
module-scope initializer is a folded constant by definition. Both references refuse it too. What is legal here
is a CONSTRUCTION: `__module_init` builds the record before `main` runs, exactly as it does for a top-level
`String`.
```maxon
typealias Integer = int(i64.min to i64.max)

type CharWrapper implements InitableFromCharLiteral
	var value as Character

	static function init(value Character) returns CharWrapper
		return CharWrapper{value: value}
	end 'init'

	export function len() returns Integer
		return value.byteLength()
	end 'len'
end 'CharWrapper'

let euro = CharWrapper from '€'

function main() returns ExitCode
	let local = CharWrapper from 'A'
	print("{euro.len()} {local.len()}\n")
	return 0
end 'main'
```
```stdout
3 1
```

<!-- test: error.init-from-char-literal-without-the-conformance -->
The conformance is REQUIRED, and it is checked after the merge rather than at the cursor: `implements` is
recorded when the type's own file is parsed, and shv2 orders files only by source path. The sentence names
the interface the FORM requires, so a type that conforms to `InitableFromStringLiteral` and is written with a
character literal is told which one is missing.
```maxon
typealias Integer = int(i64.min to i64.max)

type CharWrapper
	var value as Character

	static function init(value Character) returns CharWrapper
		return CharWrapper{value: value}
	end 'init'

	export function len() returns Integer
		return value.byteLength()
	end 'len'
end 'CharWrapper'

function main() returns ExitCode
	let cw = CharWrapper from 'X'
	return cw.len() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:17:11: Type 'CharWrapper' does not conform to InitableFromCharLiteral
```
