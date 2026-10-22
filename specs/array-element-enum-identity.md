---
feature: array-element-enum-identity
status: stable
keywords: [array, enum, push, literal, identity]
category: collections
---

# An array element is judged against the declared element's identity

## Documentation

An element of an array literal, at top level or in a function, is judged exactly as `push` judges it: one code (E3005) and one sentence. An element stored into an `Array with Reg` must be a `Reg`: a case of another enum is refused, whether it is
stored by `push` or written in an array literal beside a `Reg`. Both spellings are judged by the same door, so
they refuse in the same words.

## Tests

<!-- test: error.push-an-element-of-another-enum -->
```maxon
enum Reg
	rcx
	rdx
end 'Reg'

enum CharClass
	other
	tab
end 'CharClass'

typealias Regs = Array with Reg

function main() returns ExitCode
	var pushed = Regs.create()
	pushed.push(CharClass.tab)
	return pushed.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:16:9: argument type mismatch for 'value': expected 'Reg', got 'CharClass'
```

<!-- test: error.a-literal-of-two-enums -->
```maxon
enum Reg
	rcx
	rdx
end 'Reg'

enum CharClass
	other
	tab
end 'CharClass'

function main() returns ExitCode
	let mixed = [Reg.rcx, CharClass.tab]
	return mixed.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:13:24: argument type mismatch for 'value': expected 'Reg', got 'CharClass'
```

<!-- test: a-literal-of-one-enum -->
```maxon
enum Reg
	rcx
	rdx
end 'Reg'

function main() returns ExitCode
	let regs = [Reg.rcx, Reg.rdx, Reg.rcx]
	print("{regs.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: error.a-body-literal-of-a-string-then-an-int -->
A literal inside a function and a literal at top level give one code and one sentence for the same mistake.
```maxon
function main() returns ExitCode
	let xs = ["a", 1]
	return xs.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:17: argument type mismatch for 'value': expected 'String', got 'int'
```

<!-- test: error.a-top-level-literal-of-a-string-then-an-int -->
```maxon
var xs = ["a", 1]

function main() returns ExitCode
	return xs.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:2:16: argument type mismatch for 'value': expected 'String', got 'int'
```

<!-- test: error.a-top-level-literal-of-two-unions -->
A top-level literal of records is judged by the same door as one of scalars.
```maxon
typealias Val = int(i64.min to i64.max)

union Op
	add(value Val)
	nop
end 'Op'

union Other
	sub(value Val)
	skip
end 'Other'

var globalOps = [Op.add(7), Other.skip]

function main() returns ExitCode
	return globalOps.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:29: argument type mismatch for 'value': expected 'Op', got 'Other'
```
