---
feature: immutable-constants
status: experimental
keywords: [immutable, constant, let, module let, byte-string literal, ByteArray, Map key, Set element, freeze, E3196, E3200, E3207, E3197]
category: semantics
---

# Constant data is `immutable`, and what holds it says so

## Documentation

A `let` whose initializer is a compile-time constant names image data: a module `let`, a `static let` or a local
`let` bound to a literal such as `b"abc"`. That data is `immutable`, and so is every inline byte-string literal.
Nothing may write it, and it is stored where it can be read from any green thread.

An `immutable` value is accepted by a parameter that only reads it, by a parameter marked `immutable`, by a
parameter that stores it into an `immutable` place, and by a `Map` key or `Set` element. It is refused where
something may write it: a field or array element typed plain `ByteArray`, a parameter that writes or keeps it as
mutable, a `var` that is then written, and an unmarked result. The fix names the holder: declare the field, the
element or the result `immutable ByteArray`, or write `.clone()` for a mutable copy.

Data that is built at run time is `immutable` only once it is frozen: `freeze` it where it enters an `immutable`
holder. A `Map` key and a `Set` element are `immutable` slots, so a key is either immutable data or frozen where it
is inserted, and a key read back out of a map goes straight into an `immutable` holder. A `Map` literal's keys are
judged like `upsert` arguments, so a key that something may still write is refused there too (E3207).

A mutable value used where frozen data is needed is refused with the copy to freeze: `freeze x.clone()`, and
`freeze (expression).clone()` for an expression that is more than a name, a member path or a call chain (E3200). A
parameter that is written in one place and kept frozen in another is E3206. A container made from an array whose
elements its body keeps, such as a `Set`, needs an element type nothing writes (E3078) and a frozen array (E3207).
A service message, reply or factory parameter that is a record, a payload union, an array or an interface value
carries `isolated` or `immutable` (E3217).

## Tests

<!-- test: error.a-constant-module-let-stored-into-a-plain-field-is-refused -->
`Name` is image data, and `value` is a plain `ByteArray` field, where something may write it.

```maxon
let Name = b"abc"

type Tok
	export let value as ByteArray

	static function named() returns Self
		return Self{value: Name}
	end 'named'
end 'Tok'

function main() returns ExitCode
	let t = Tok.named()
	print("{t.value.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:8:22: cannot store an immutable 'ByteArray' into 'Tok.value', which holds a mutable 'ByteArray'; declare the field `immutable ByteArray`
```

<!-- test: a-constant-module-let-passed-to-a-parameter-a-plain-field-keeps-is-read-only-for-the-caller -->
`create` only holds its parameter in the record it returns, so the constant `Name` is accepted and the `Tok` the caller gets is read-only: the caller reads it.

```maxon
let Name = b"abc"

type Tok
	export let value as ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function main() returns ExitCode
	let t = Tok.create(Name)
	print("{t.value.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: error.a-write-through-a-record-built-from-a-constant-module-let-is-refused -->
The `Tok` holds the constant `Name`, so the caller cannot write its bytes through the record.

```maxon
let Name = b"abc"

type Tok
	export let value as ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function main() returns ExitCode
	let t = Tok.create(Name)
	t.value.push(100)
	print("{t.value.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:14:10: cannot write 'a container' through 'a call result', which is read-only
```

<!-- test: a-constant-local-let-passed-to-a-parameter-a-plain-field-keeps-is-read-only-for-the-caller -->
A `let` bound to a literal inside `main` is image data like a module `let`, and the `Tok` built from it is read-only for the caller.

```maxon
type Tok
	export let value as ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function main() returns ExitCode
	let name = b"abc"
	let t = Tok.create(name)
	print("{t.value.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: error.a-write-through-a-record-built-from-a-constant-local-let-is-refused -->
The `Tok` holds the constant `name`, so the caller cannot write its bytes through the record.

```maxon
type Tok
	export let value as ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function main() returns ExitCode
	let name = b"abc"
	let t = Tok.create(name)
	t.value.push(100)
	print("{t.value.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:13:10: cannot write 'a container' through 'a call result', which is read-only
```

<!-- test: a-constant-static-let-passed-to-a-parameter-a-plain-field-keeps-is-read-only-for-the-caller -->
A `static let` bound to a literal is image data too, and the `Tok` built from it is read-only for the caller.

```maxon
type Tok
	export let value as ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

type Names
	static let Alpha = b"abc"

	static function token() returns Tok
		return Tok.create(Names.Alpha)
	end 'token'
end 'Names'

function main() returns ExitCode
	let t = Names.token()
	print("{t.value.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: error.a-record-built-from-a-constant-static-let-is-refused-by-a-mutable-array -->
The `Tok` holds the constant `Alpha`, so it is read-only and the mutable array refuses it.

```maxon
typealias TokArray = Array with Tok

type Tok
	export let value as ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

type Names
	static let Alpha = b"abc"

	static function keep(into TokArray)
		into.push(Tok.create(Names.Alpha))
	end 'keep'
end 'Names'

function main() returns ExitCode
	var toks = TokArray.create(0)
	Names.keep(toks)
	print("{toks.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:16:13: cannot store a read-only 'Tok' into 'an element of the container push writes', as it joins immutable and mutable data; store a copy
```

<!-- test: a-constant-module-let-is-stored-into-an-immutable-field -->
`value` is declared `immutable ByteArray`, and `create` takes its parameter with no word: storing it into an
`immutable` field is all `create` does, so the parameter takes immutable data.

```maxon
let Name = b"abc"

type Tok
	export let value as immutable ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function main() returns ExitCode
	let t = Tok.create(Name)
	print("{t.value.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: a-constant-local-let-is-stored-into-an-immutable-field -->
```maxon
type Tok
	export let value as immutable ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function main() returns ExitCode
	let name = b"abcd"
	let t = Tok.create(name)
	print("{t.value.count()} {name.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 4
```

<!-- test: constant-names-are-pushed-into-an-immutable-array-and-read-by-for -->
An array whose element is `immutable ByteArray` takes a module `let` and a local `let`, and `for` reads each name back.

```maxon
typealias NameArray = Array with immutable ByteArray

let Alpha = b"alpha"

function main() returns ExitCode
	let beta = b"be"
	var names = NameArray.create(0)
	names.push(Alpha)
	names.push(beta)
	var total = 0 as Count

	for name in names 'eachName'
		total = total + name.count()
	end 'eachName'

	print("{names.count()} {total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 7
```

<!-- test: error.a-constant-module-let-pushed-into-a-plain-array-is-refused -->
The element is a plain `ByteArray`, so a reader of the array may write what it holds.

```maxon
typealias NameArray = Array with ByteArray

let Alpha = b"alpha"

function main() returns ExitCode
	var names = NameArray.create(0)
	names.push(Alpha)
	print("{names.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:8:13: cannot store an immutable 'ByteArray' into 'an element of the container push writes', which holds a mutable 'ByteArray'; declare the element `immutable ByteArray`
```

<!-- test: error.a-write-through-a-constant-module-let-is-refused -->
```maxon
let Name = b"abc"

function main() returns ExitCode
	Name.push(100)
	print("{Name.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:5:7: 'push' needs a writable receiver; 'Name' is immutable
```

<!-- test: error.a-write-through-a-constant-local-let-is-refused -->
A `let` no longer freezes what it names, but this one names image data.

```maxon
function main() returns ExitCode
	let name = b"abc"
	name.push(100)
	print("{name.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:4:7: 'push' needs a writable receiver; 'name' is immutable
```

<!-- test: error.a-write-through-a-let-bound-to-a-constant-let-is-refused -->
`copy` names what `Name` names, which is image data.

```maxon
let Name = b"abc"

function main() returns ExitCode
	let copy = Name
	copy.push(100)
	print("{copy.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:6:7: 'push' needs a writable receiver; 'copy' is immutable
```

<!-- test: error.a-write-through-a-constant-field-of-an-immutable-holder-is-refused -->
`value` is `immutable`, so what it holds is not writable through any name that reads it.

```maxon
let Name = b"abc"

type Tok
	export let value as immutable ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function main() returns ExitCode
	let t = Tok.create(Name)
	t.value.push(100)
	print("{t.value.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:14:10: 'push' needs a writable receiver; 'value' is immutable
```

<!-- test: a-built-value-bound-by-let-is-not-constant -->
A `let` bound to a call is not image data: the buffer is written through its name and stored into a plain field.

```maxon
type Tok
	export let value as ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function main() returns ExitCode
	var buffer = ByteArray.create(0)
	buffer.push(1)
	buffer.push(2)
	let t = Tok.create(buffer)
	print("{t.value.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: a-fresh-frozen-name-is-stored-into-an-immutable-field -->
`freeze` turns the fresh buffer into `immutable` data where it enters the holder.

```maxon
type Tok
	export let value as immutable ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function main() returns ExitCode
	let t = Tok.create(freeze ByteArray.create(3))
	print("{t.value.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: error.a-fresh-name-is-refused-by-an-immutable-field-until-it-is-frozen -->
```maxon
type Tok
	export let value as immutable ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function main() returns ExitCode
	let t = Tok.create(ByteArray.create(3))
	print("{t.value.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:11:21: 'create' needs 'value' frozen (line 6: `return Self{value: value}`); a fresh value is mutable. Freeze it first: write `freeze` before the fresh expression (`freeze Box.create(1)`), or around a receiver (`(freeze Table.create()).submit(a)`)
```

<!-- test: a-slice-of-an-immutable-source-is-frozen-into-an-immutable-field -->
`src` is `immutable`, so a slice of it is a clean call and `freeze` takes it.

```maxon
let Source = b"abcdef"

type Tok
	export let value as immutable ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function head(src immutable ByteArray) returns Tok
	let part = freeze try src.slice(0, endIndex: 2) otherwise panic("head: Source holds two bytes")

	return Tok.create(part)
end 'head'

function main() returns ExitCode
	let t = head(Source)
	print("{t.value.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: error.a-slice-of-a-mutable-source-cannot-be-frozen -->
`src` is ordinary mutable data, so its slice is part of its region.

```maxon
type Tok
	export let value as immutable ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function head(src ByteArray) returns Tok
	let part = freeze try src.slice(0, endIndex: 2) otherwise panic("head: the caller passes two bytes")

	return Tok.create(part)
end 'head'

function main() returns ExitCode
	var bytes = ByteArray.create(4)
	let t = head(bytes)
	print("{t.value.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:11:20: 'try src.slice(0, endIndex: 2) otherwise panic("head: the caller passes two bytes")' is used as mutable; freeze a copy: `freeze (try src.slice(0, endIndex: 2) otherwise panic("head: the caller passes two bytes")).clone()`
```

<!-- test: a-constant-module-let-is-read-by-a-reading-parameter-and-an-immutable-parameter -->
Neither parameter writes or keeps its argument, so both take image data.

```maxon
let Name = b"abc"

function reads(name ByteArray) returns Count
	return name.count()
end 'reads'

function needs(name immutable ByteArray) returns Count
	return name.count()
end 'needs'

function main() returns ExitCode
	print("{reads(Name)} {needs(Name)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 3
```

<!-- test: error.a-constant-module-let-passed-to-a-parameter-the-callee-writes-is-refused -->
```maxon
let Name = b"abc"

function grow(name ByteArray)
	name.push(100)
end 'grow'

function main() returns ExitCode
	grow(Name)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:9:7: 'grow' writes 'name' (line 5: `name.push(100)`); 'Name' is immutable
```

<!-- test: error.an-unmarked-result-returning-a-constant-is-refused -->
The result is read by a caller that may write it, so it is declared `immutable`.

```maxon
let Name = b"abc"

function nameOf() returns ByteArray
	return Name
end 'nameOf'

function main() returns ExitCode
	print("{nameOf().count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:5:9: a return does not fit the declared result: 'Name' is immutable, the result is not `immutable`; declare the result `immutable`
```

<!-- test: an-immutable-result-returns-a-constant -->
```maxon
let Name = b"abc"

function nameOf() returns immutable ByteArray
	return Name
end 'nameOf'

type Tok
	export let value as immutable ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function main() returns ExitCode
	let t = Tok.create(nameOf())
	print("{t.value.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: inline-byte-literal-is-immutable -->
An inline literal is image data like a constant `let`: it is stored into an `immutable` field and into an
`immutable` array element with no `freeze` and no copy.

```maxon
typealias NameArray = Array with immutable ByteArray

type Tok
	export let value as immutable ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function main() returns ExitCode
	let t = Tok.create(b"abc")
	var names = NameArray.create(0)
	names.push(b"de")
	print("{t.value.count()} {names.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 1
```

<!-- test: an-inline-byte-literal-passed-to-a-parameter-a-plain-field-keeps-is-read-only-for-the-caller -->
The `Tok` built from an inline byte literal is read-only for the caller, which reads it.

```maxon
type Tok
	export let value as ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function main() returns ExitCode
	let t = Tok.create(b"abc")
	print("{t.value.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: error.a-record-built-from-an-inline-byte-literal-is-refused-by-a-mutable-array -->
The `Tok` holds the literal, so it is read-only and the mutable array refuses it.

```maxon
typealias TokArray = Array with Tok

type Tok
	export let value as ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function main() returns ExitCode
	var toks = TokArray.create(0)
	toks.push(Tok.create(b"abc"))
	print("{toks.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:14:12: cannot store a read-only 'Tok' into 'an element of the container push writes', as it joins immutable and mutable data; store a copy
```

<!-- test: error.an-inline-byte-literal-is-refused-by-a-parameter-the-callee-writes -->
```maxon
function grow(name ByteArray)
	name.push(100)
end 'grow'

function main() returns ExitCode
	grow(b"abc")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:7:7: 'grow' writes 'name' (line 3: `name.push(100)`); a byte-string literal is immutable
```

<!-- test: error.a-var-bound-to-a-byte-literal-and-written-is-refused -->
`var` allows rebinding, not writing a literal: `.clone()` is the mutable copy.

```maxon
function main() returns ExitCode
	var name = b"abc"
	name.push(100)
	print("{name.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:4:7: 'push' needs a writable receiver; 'name' is immutable
```

<!-- test: a-clone-of-a-byte-literal-is-mutable -->
```maxon
function main() returns ExitCode
	var name = b"abc".clone()
	name.push(100)
	print("{name.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: error.an-unmarked-result-returning-a-byte-literal-is-refused -->
```maxon
function nameOf() returns ByteArray
	return b"abc"
end 'nameOf'

function main() returns ExitCode
	print("{nameOf().count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:3:9: a return does not fit the declared result: a byte-string literal is immutable, the result is not `immutable`; declare the result `immutable`
```

<!-- test: map-key-read-into-immutable-holder -->
A `Map` key slot is `immutable`: a literal is inserted as it is, and the key a `for` reads back is stored into an
`immutable` field with no copy.

```maxon
typealias CountMap = Map with (ByteArray, Count)
typealias CreditArray = Array with Credit

type Credit
	export let name as immutable ByteArray

	static function create(name ByteArray) returns Self
		return Self{name: name}
	end 'create'
end 'Credit'

function main() returns ExitCode
	var counts = CountMap.create()
	counts.upsert(b"ab", value: 1)
	counts.upsert(b"cde", value: 2)
	var credits = CreditArray.create(0)

	for (name, _) in counts 'eachKey'
		credits.push(Credit.create(name))
	end 'eachKey'

	var total = 0 as Count

	for credit in credits 'eachCredit'
		total = total + credit.name.count()
	end 'eachCredit'

	print("{credits.count()} {total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 5
```

<!-- test: set-element-read-into-immutable-holder -->
A `Set` element slot is `immutable` as well: the element a `for` reads goes into an `immutable` field with no copy.

```maxon
typealias NameSet = Set with ByteArray
typealias CreditArray = Array with Credit

type Credit
	export let name as immutable ByteArray

	static function create(name ByteArray) returns Self
		return Self{name: name}
	end 'create'
end 'Credit'

function main() returns ExitCode
	var names = NameSet.create()
	names.insert(b"ab")
	names.insert(b"cde")
	var credits = CreditArray.create(0)

	for name in names 'eachElement'
		credits.push(Credit.create(name))
	end 'eachElement'

	var total = 0 as Count

	for credit in credits 'eachCredit'
		total = total + credit.name.count()
	end 'eachCredit'

	print("{credits.count()} {total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 5
```

<!-- test: a-frozen-fresh-key-is-inserted-into-a-map-and-a-set -->
A name built at run time is frozen where it becomes a key or an element.

```maxon
typealias CountMap = Map with (ByteArray, Count)
typealias NameSet = Set with ByteArray

function main() returns ExitCode
	var counts = CountMap.create()
	counts.upsert(freeze ByteArray.create(3), value: 7)
	var names = NameSet.create()
	names.insert(freeze ByteArray.create(2))
	print("{counts.count()} {names.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
```

<!-- test: error.a-fresh-key-is-refused-by-a-map-until-it-is-frozen -->
```maxon
typealias CountMap = Map with (ByteArray, Count)

function main() returns ExitCode
	var counts = CountMap.create()
	counts.upsert(ByteArray.create(3), value: 7)
	print("{counts.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:6:16: 'upsert' takes `immutable` 'key'; a fresh value is mutable. Freeze it first: write `freeze` before the fresh expression (`freeze Box.create(1)`), or around a receiver (`(freeze Table.create()).submit(a)`)
```

<!-- test: error.a-fresh-element-is-refused-by-a-set-until-it-is-frozen -->
```maxon
typealias NameSet = Set with ByteArray

function main() returns ExitCode
	var names = NameSet.create()
	names.insert(ByteArray.create(2))
	print("{names.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:6:15: 'insert' takes `immutable` 'element'; a fresh value is mutable. Freeze it first: write `freeze` before the fresh expression (`freeze Box.create(1)`), or around a receiver (`(freeze Table.create()).submit(a)`)
```

<!-- test: error.a-map-literal-key-built-by-a-call-is-refused-until-it-is-frozen -->
`pick` hands back the array it was given, so the key of the literal is mutable, as it is for `upsert`.

```maxon
function pick(a ByteArray) returns ByteArray
	return a
end 'pick'

function main() returns ExitCode
	let buf = ByteArray.create(3)
	let counts = [pick(buf): 1 as Count]
	print("{counts.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:8:15: 'upsert' takes `immutable` 'key'; the argument is mutable. Freeze it first
```

<!-- test: a-map-literal-key-is-accepted-once-it-is-frozen -->
```maxon
function main() returns ExitCode
	let counts = [freeze ByteArray.create(3): 1 as Count]
	print("{counts.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: error.a-union-holding-a-record-is-refused-by-an-immutable-parameter-until-it-is-frozen -->
A union with a payload carries a capability whether it is the parameter or the argument, so a mutable `Slot` is refused by `keep`.

```maxon
type Box
	export var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Box'

union Slot
	empty
	holding(box Box)
end 'Slot'

function keep(slot immutable Slot) returns Count
	match slot 's'
		empty then return 0
		holding(box) then return box.n
	end 's'
end 'keep'

function main() returns ExitCode
	let slot = Slot.holding(Box.create())
	print("{keep(slot)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:24:15: 'keep' takes `immutable` 'slot'; 'slot' is mutable. Freeze it first
```

<!-- test: a-frozen-union-holding-a-record-is-accepted-by-an-immutable-parameter -->
```maxon
type Box
	export var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Box'

union Slot
	empty
	holding(box Box)
end 'Slot'

function keep(slot immutable Slot) returns Count
	match slot 's'
		empty then return 0
		holding(box) then return box.n
	end 's'
end 'keep'

function main() returns ExitCode
	let slot = freeze Slot.holding(Box.create())
	print("{keep(slot)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: a-scalar-key-and-element-need-no-freeze -->
A number carries no capability, so a `Map` key and a `Set` element of one is inserted as it is, in the program and in the library's generic bodies that insert type-parameter values.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyMap = Map with (Tally, Tally)
typealias TallySet = Set with Tally

function main() returns ExitCode
	var map = TallyMap.create()
	var seen = TallySet.create()
	let key = 4 as Tally
	map.upsert(key, value: 5)
	seen.insert(key)
	print("{map.count()} {seen.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
```

<!-- test: error.a-map-key-read-is-refused-by-a-plain-field -->
The key a `for` reads is `immutable`, so a plain `ByteArray` field cannot hold it.

```maxon
typealias CountMap = Map with (ByteArray, Count)
typealias CreditArray = Array with Credit

type Credit
	export let name as ByteArray

	static function create(name ByteArray) returns Self
		return Self{name: name}
	end 'create'
end 'Credit'

function main() returns ExitCode
	var counts = CountMap.create()
	counts.upsert(b"ab", value: 1)
	var credits = CreditArray.create(0)

	for (name, _) in counts 'eachKey'
		credits.push(Credit.create(name))
	end 'eachKey'

	print("{credits.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:19:16: cannot store a read-only 'Credit' into 'an element of the container push writes', as it joins immutable and mutable data; store a copy
```

<!-- test: immutable-names-share-across-a-service-send -->
A `Tok` holding a module `let` name is immutable already (every field is a `let` of an immutable type, so `freeze`
would be E3200), and a send hands the `Tok` over by move. Two `Tok`s built from the same name are sent to a service:
the name is image data, so the sends walk no counts and both read the same bytes.

```maxon
let Label = b"alpha"

type Tok
	export let value as immutable ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

type Sink
	var seen = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'

	export function keep(tok immutable Tok) returns Count
		self.seen = self.seen + tok.value.count()

		return self.seen
	end 'keep'
end 'Sink'

function main() returns ExitCode
	let sink = spawn Sink.create()
	let first = try await sink.keep(Tok.create(Label)) otherwise panic("main: a fresh sink answers keep")
	let second = try await sink.keep(Tok.create(Label)) otherwise panic("main: a fresh sink answers keep")
	sink.shutdown()
	print("{first} {second} {Label.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 10 5
```

<!-- test: error.a-var-bound-to-frozen-data-and-written-is-refused -->
A `var` can be rebound, but what it holds is frozen: its capability is fixed where it is bound.

```maxon
function main() returns ExitCode
	var frozen = freeze ByteArray.create(3)
	frozen.push(100)
	print("{frozen.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:4:9: 'push' needs a writable receiver; 'frozen' is immutable
```

<!-- test: error.a-write-through-an-immutable-field-of-a-var-is-refused -->
`data` is an `immutable` field, so no name that reads it may write it, whatever binds the holder.

```maxon
type Tok
	export var data as immutable ByteArray

	static function create(data immutable ByteArray) returns Self
		return Self{data: data}
	end 'create'
end 'Tok'

function main() returns ExitCode
	var t = Tok.create(freeze ByteArray.create(3))
	t.data.push(100)
	print("{t.data.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:12:9: 'push' needs a writable receiver; 'data' is immutable
```

<!-- test: error.a-write-through-an-immutable-parameter-is-refused -->
```maxon
function grow(name immutable ByteArray)
	name.push(100)
end 'grow'

function main() returns ExitCode
	grow(freeze ByteArray.create(3))
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:3:7: 'push' needs a writable receiver; 'name' is immutable
```

<!-- test: a-union-payload-declared-immutable-holds-a-constant-name -->
A union payload takes a capability word like a field: `named` holds image data, and the `match` reads it back as
`immutable`.

```maxon
let Alpha = b"alpha"

union Label
	named(let text immutable ByteArray)
	anonymous
end 'Label'

function describe(label Label) returns Count
	return match label 'which'
		named(text) gives text.count()
		anonymous gives 0
	end 'which'
end 'describe'

function main() returns ExitCode
	let label = Label.named(Alpha)
	let fresh = Label.named(freeze ByteArray.create(4))
	print("{describe(label)} {describe(fresh)} {describe(Label.anonymous)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 4 0
```

<!-- test: error.a-constant-name-stored-into-a-plain-union-payload-is-refused -->
```maxon
let Alpha = b"alpha"

union Label
	named(let text ByteArray)
	anonymous
end 'Label'

function main() returns ExitCode
	let label = Label.named(Alpha)
	print("built\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:10:26: cannot store an immutable 'ByteArray' into 'Label.named.text', which holds a mutable 'ByteArray'; declare the payload `immutable ByteArray`
```

<!-- test: error.a-write-through-an-immutable-union-payload-is-refused -->
```maxon
union Label
	named(let text immutable ByteArray)
	anonymous
end 'Label'

function main() returns ExitCode
	let label = Label.named(freeze ByteArray.create(4))

	match label 'which'
		named(text) then text.push(1)
		anonymous then print("none\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:11:25: 'push' needs a writable receiver; 'text' is immutable
```

<!-- test: a-set-and-a-map-of-values-are-built-from-a-plain-mutable-array -->
`immutable` over a value type means the type itself: a `Set` or `Map` of numbers takes any array of them, with no
`freeze` and no copy.

```maxon
typealias Code = int(0 to 1000)
typealias CodeSet = Set with Code
typealias CodeMap = Map with (Code, Count)

function main() returns ExitCode
	var codes = [1 as Code, 2 as Code, 3 as Code]
	let members = CodeSet from codes
	var byCode = CodeMap.create()

	for code in codes 'eachCode'
		byCode.upsert(code, value: 1)
	end 'eachCode'

	codes.push(4 as Code)
	print("{members.count()} {byCode.count()} {codes.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 3 4
```

<!-- test: reads-of-an-immutable-slot-are-immutable -->
`get`, a `for` loop and a `Map` value all hand back `immutable` data when the slot is `immutable`, so each goes
straight into an `immutable` result or element.

```maxon
let Alpha = b"alpha"

typealias NameArray = Array with immutable ByteArray
typealias NameMap = Map with (ByteArray, immutable ByteArray)

function firstOf(names NameArray) returns immutable ByteArray
	return try names.get(0) otherwise panic("firstOf: the caller passes a name")
end 'firstOf'

function lookup(byPath NameMap, path ByteArray) returns immutable ByteArray
	return try byPath.get(path) otherwise panic("lookup: the caller passes a stored path")
end 'lookup'

function copyAll(names NameArray) returns NameArray
	var out = NameArray.create(0)

	for name in names 'eachName'
		out.push(name)
	end 'eachName'

	return out
end 'copyAll'

function main() returns ExitCode
	var names = NameArray.create(0)
	names.push(Alpha)
	var byPath = NameMap.create()
	byPath.upsert(Alpha, value: Alpha)
	print("{firstOf(names).count()} {lookup(byPath, path: Alpha).count()} {copyAll(names).count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 5 1
```

<!-- test: a-map-get-result-takes-its-capability-from-the-value-slot-only -->
The key `Spelling` is immutable, and the value array `get` returns is the map's own, writable one: the result does not
join the key's capability.

```maxon
let Spelling = b"x"
let Declaring = b"decl"

typealias PathArray = Array with immutable ByteArray
typealias PathMap = Map with (ByteArray, PathArray)

function main() returns ExitCode
	var byName = PathMap.create()
	byName.upsert(Spelling, value: PathArray.create(0))
	var sharers = try byName.get(Spelling) otherwise panic("main: the key was just inserted")
	sharers.push(Declaring)
	print("{sharers.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-parameter-default-may-be-immutable-data-marked-or-not -->
A default value is checked against its parameter: an unmarked parameter only reads, so it accepts a constant and a
call that returns `immutable` data, and an `immutable` parameter takes them as well.

```maxon
let Preset = b"abc"

function standard() returns immutable ByteArray
	return Preset
end 'standard'

function lengthOf(name ByteArray = Preset) returns Count
	return name.count()
end 'lengthOf'

function lengthOfCall(name ByteArray = standard()) returns Count
	return name.count()
end 'lengthOfCall'

function lengthOfFrozen(name immutable ByteArray = Preset) returns Count
	return name.count()
end 'lengthOfFrozen'

function main() returns ExitCode
	print("{lengthOf()} {lengthOfCall()} {lengthOfFrozen()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 3 3
```

<!-- test: a-read-of-an-immutable-field-passed-to-an-immutable-parameter-does-not-freeze-its-holder -->
`label` is an `immutable` field, so `sigs.label` is frozen whoever holds it: `measure` hands it to a parameter that
needs frozen data and still writes `sigs`, and `sigs` stays a mutable parameter.

```maxon
type Sigs
	export var hits as Count
	export let label as immutable ByteArray

	static function create() returns Self
		return Self{hits: 0, label: b"abc"}
	end 'create'
end 'Sigs'

function takes(name immutable ByteArray) returns Count
	return name.count()
end 'takes'

function measure(sigs Sigs) returns Count
	sigs.hits = sigs.hits + 1
	return takes(sigs.label)
end 'measure'

function main() returns ExitCode
	var sigs = Sigs.create()
	let size = measure(sigs)
	print("{size} {sigs.hits}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 1
```

<!-- test: an-immutable-payload-read-through-a-result-does-not-freeze-the-record-the-result-came-from -->
`name` is the `immutable` payload of the union `memberOf` hands back out of `sigs`. `Chain.create` keeps it in an
`immutable` field, so it needs frozen data, and the payload already is: `sigs` is not asked to be frozen, and
`outer` writes it.

```maxon
export union Member
	one(let name immutable ByteArray)
	none
end 'Member'

type Sigs
	export var hits as Count
	export var last as Member

	static function create() returns Self
		return Self{hits: 0, last: Member.one(b"payload")}
	end 'create'

	export function memberOf(qualified immutable ByteArray) returns Member
		if qualified.isEmpty() 'empty'
			return self.last
		end 'empty'

		self.hits = self.hits + 1
		return self.last
	end 'memberOf'
end 'Sigs'

type Chain
	export let name as immutable ByteArray

	static function create(name immutable ByteArray) returns Self
		return Self{name: name}
	end 'create'
end 'Chain'

function lookup(sigs Sigs, written immutable ByteArray) returns Chain
	match sigs.memberOf(written) 'claims'
		one(name) then return Chain.create(name)
		none then return Chain.create(written)
	end 'claims'
end 'lookup'

function outer(sigs Sigs, written immutable ByteArray) returns Chain
	sigs.hits = 5
	return lookup(sigs, written: written)
end 'outer'

function main() returns ExitCode
	var sigs = Sigs.create()
	let chain = outer(sigs, written: b"abc")
	print("{chain.name.count()} {sigs.hits}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 6
```

<!-- test: a-call-declared-to-return-immutable-data-passed-to-a-frozen-need-does-not-freeze-the-receiver -->
`nameAt` is declared `returns immutable`, so its result is frozen however `table` is held: `reframe` writes `table` and
passes the result to `canon`, which needs frozen data, and `table` stays a mutable parameter.

```maxon
typealias NameList = Array with immutable ByteArray

type Table
	export var names as NameList
	export var hits as Count

	static function create() returns Self
		return Self{names: NameList.create(0), hits: 0}
	end 'create'

	export function nameAt(index ElementIndex) returns immutable ByteArray
		return try self.names.get(index) otherwise b"none"
	end 'nameAt'

	export function intern(name immutable ByteArray)
		self.names.push(name)
		self.hits = self.hits + 1
	end 'intern'
end 'Table'

function canon(name immutable ByteArray) returns immutable ByteArray
	return name
end 'canon'

function reframe(table Table) returns immutable ByteArray
	table.hits = table.hits + 10
	return canon(table.nameAt(0))
end 'reframe'

function main() returns ExitCode
	var table = Table.create()
	table.intern(b"xy")
	let name = reframe(table)
	print("{name.count()} {table.hits}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 11
```

<!-- test: a-method-returning-its-immutable-field-does-not-freeze-its-receiver -->
`labelOf` writes `self` and returns its `immutable` field: the result is frozen data whatever holds the receiver, so
`self` is not asked to be frozen, and `lookup` hands the result to a parameter that needs frozen data.

```maxon
type Sigs
	export var hits as Count
	export let label as immutable ByteArray

	static function create() returns Self
		return Self{hits: 0, label: b"label"}
	end 'create'

	export function labelOf() returns immutable ByteArray
		self.hits = self.hits + 1
		return self.label
	end 'labelOf'
end 'Sigs'

type Chain
	export let name as immutable ByteArray

	static function create(name immutable ByteArray) returns Self
		return Self{name: name}
	end 'create'
end 'Chain'

function lookup(sigs Sigs) returns Chain
	return Chain.create(sigs.labelOf())
end 'lookup'

function main() returns ExitCode
	var sigs = Sigs.create()
	let chain = lookup(sigs)
	print("{chain.name.count()} {sigs.hits}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 1
```

<!-- test: error.a-mutable-field-read-passed-to-an-immutable-parameter-still-freezes-its-holder -->
`plain` is a mutable field, so `sigs.plain` is frozen only if `sigs` is, and `a3` also writes `sigs`: the parameter
cannot be both.

```maxon
type Sigs
	export var hits as Count
	export var plain as ByteArray

	static function create() returns Self
		return Self{hits: 0, plain: ByteArray.create(0)}
	end 'create'
end 'Sigs'

function takes(name immutable ByteArray) returns Count
	return name.count()
end 'takes'

function a3(sigs Sigs) returns Count
	sigs.hits = 1
	return takes(sigs.plain)
end 'a3'

function main() returns ExitCode
	var sigs = Sigs.create()
	let size = a3(sigs)
	print("{size}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3206: <fragment>:15:18: parameter 'sigs' is used as mutable at line 16 and as immutable at line 17; a parameter cannot be both
error E3207: <fragment>:17:15: 'takes' takes `immutable` 'name'; 'sigs' is mutable. Freeze it first
```

<!-- test: an-immutable-element-fills-an-immutable-struct-field -->
An element of an `Array with immutable ByteArray` is frozen data, so it fills a field declared `immutable ByteArray`
with nothing copied.

```maxon
typealias NameList = Array with immutable ByteArray

type Window
	export let label as immutable ByteArray
	export let depth as Count

	static function create(label immutable ByteArray, depth Count) returns Self
		return Self{label: label, depth: depth}
	end 'create'
end 'Window'

function main() returns ExitCode
	var names = NameList.create(0)
	names.push(b"outer")
	let first = try names.get(0) otherwise panic("main: one name was pushed")
	let window = Window.create(first, depth: 1)
	print("{window.label.count()} {window.depth}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 1
```

<!-- test: an-immutable-element-fills-a-one-word-union-payload -->
A union whose only payload case holds one `immutable` word takes an element of an `Array with immutable ByteArray`
and is stored into a `var` field.

```maxon
typealias NameList = Array with immutable ByteArray

union Source
	file
	body(let name immutable ByteArray)
end 'Source'

type Holder
	export var source as Source

	static function create() returns Self
		return Self{source: Source.file}
	end 'create'

	export function keep(name immutable ByteArray)
		self.source = Source.body(name)
	end 'keep'

	export function size() returns Count
		return match self.source 'which'
			file gives 0
			body(name) gives name.count()
		end 'which'
	end 'size'
end 'Holder'

function main() returns ExitCode
	var names = NameList.create(0)
	names.push(b"unit")
	let first = try names.get(0) otherwise panic("main: one name was pushed")
	var holder = Holder.create()
	holder.keep(first)
	print("{holder.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: an-immutable-element-fills-the-second-payload-case-of-a-boxed-union -->
A union with two payload cases is boxed, and its second case takes `immutable` elements of an
`Array with immutable ByteArray` as well.

```maxon
typealias NameList = Array with immutable ByteArray

union Shape
	named(let name immutable ByteArray)
	pair(let left immutable ByteArray, let right immutable ByteArray)
end 'Shape'

function weigh(shape Shape) returns Count
	return match shape 'which'
		named(name) gives name.count()
		pair(left, right) gives left.count() + right.count()
	end 'which'
end 'weigh'

function main() returns ExitCode
	var names = NameList.create(0)
	names.push(b"ab")
	names.push(b"cde")
	let first = try names.get(0) otherwise panic("main: two names were pushed")
	let second = try names.get(1) otherwise panic("main: two names were pushed")
	let shape = Shape.pair(first, right: second)
	print("{weigh(shape)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: an-immutable-field-read-off-a-parameter-arrays-element-goes-into-a-set -->
`name` is an `immutable` field, so reading it off an element of an unmarked array parameter hands the set data nobody
else can write: no clone, no `freeze`.

```maxon
typealias NameSet = Set with ByteArray
typealias NameMap = Map with (ByteArray, Count)
typealias FuncArray = Array with Func

type Func
	let name as immutable ByteArray

	static function create(name immutable ByteArray) returns Func
		return Self{name: name}
	end 'create'

	static function namesOf(functions FuncArray) returns NameSet
		var names = NameSet.create()
		let func = try functions.get(0) otherwise panic("namesOf: the caller passes a function")
		names.insert(func.name)
		return names
	end 'namesOf'

	static function countsOf(functions FuncArray) returns NameMap
		var counts = NameMap.create()
		let func = try functions.get(0) otherwise panic("countsOf: the caller passes a function")
		counts.upsert(func.name, value: 1)
		return counts
	end 'countsOf'
end 'Func'

function main() returns ExitCode
	var functions = FuncArray.create(0)
	functions.push(Func.create(b"alpha"))
	print("{Func.namesOf(functions).count()} {Func.countsOf(functions).count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
```

<!-- test: error.a-mutable-field-read-off-a-parameter-arrays-element-is-refused-by-a-set -->
`name` is a plain `ByteArray` field, so the element's name is data another name may write.

```maxon
typealias NameSet = Set with ByteArray
typealias FuncArray = Array with Func

type Func
	let name as ByteArray

	static function create(name ByteArray) returns Func
		return Self{name: name}
	end 'create'

	static function namesOf(functions FuncArray) returns NameSet
		var names = NameSet.create()
		let func = try functions.get(0) otherwise panic("namesOf: the caller passes a function")
		names.insert(func.name)
		return names
	end 'namesOf'
end 'Func'

function main() returns ExitCode
	var functions = FuncArray.create(0)
	functions.push(Func.create(b"alpha".clone()))
	print("{Func.namesOf(functions).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:15:16: 'insert' takes `immutable` 'element'; 'func' is mutable. Freeze it first
```

<!-- test: a-set-of-values-is-built-from-an-array-literal -->
```maxon
typealias Code = int(0 to 1000)
typealias CodeSet = Set with Code

function main() returns ExitCode
	let members = CodeSet from [1 as Code, 2 as Code, 2 as Code]
	print("{members.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: a-set-of-names-is-built-from-an-array-literal-of-names -->
```maxon
typealias NameSet = Set with ByteArray

function main() returns ExitCode
	let members = NameSet from [b"ab", b"cd", b"ab"]
	print("{members.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: error.a-set-of-names-is-refused-a-plain-array-of-names -->
`Set.init` keeps the elements of the array it is given, so for a record element it needs them frozen: the array of
`ByteArray` is data other names may write.

```maxon
typealias NameSet = Set with ByteArray
typealias NameArray = Array with ByteArray

function main() returns ExitCode
	var names = NameArray.create(0)
	names.push(b"ab".clone())
	let members = NameSet from names
	names.push(b"cd".clone())
	print("{members.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:8:16: cannot call 'Set.init' with a type whose values something may write: its body keeps a value of its type parameter, and a kept value must have no other writer, which only a type nothing writes guarantees (a number, a String, or a record whose fields are all `let`); use such a type
error E3207: <fragment>:8:29: 'init' needs 'arr' frozen (it calls 'insert', which takes `immutable` 'element'); the argument is mutable. Freeze it first
```

<!-- test: a-map-of-text-keys-is-built-from-a-dictionary-literal -->
```maxon
function main() returns ExitCode
	let ranks = ["ab": 1 as Count, "cd": 2 as Count]
	print("{ranks.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: a-map-of-values-is-built-from-a-dictionary-literal -->
```maxon
typealias Code = int(0 to 1000)

function main() returns ExitCode
	let ranks = [1 as Code: 1 as Count, 2 as Code: 2 as Count]
	print("{ranks.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: an-unmarked-parameter-passed-to-an-immutable-parameter-is-inferred-frozen -->
`wrap` marks nothing: passing `name` to the `immutable` parameter that keeps it makes `wrap` take frozen data, and
a frozen argument is all it needs.

```maxon
type Holder
	export let name as immutable ByteArray

	static function create(name immutable ByteArray) returns Holder
		return Self{name: name}
	end 'create'
end 'Holder'

function wrap(name ByteArray) returns Holder
	return Holder.create(name)
end 'wrap'

function main() returns ExitCode
	print("{wrap(b"abc").name.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: error.a-parameter-inferred-frozen-refuses-a-mutable-argument -->
The refusal is at the caller that passes mutable data, not inside `wrap`.

```maxon
type Holder
	export let name as immutable ByteArray

	static function create(name immutable ByteArray) returns Holder
		return Self{name: name}
	end 'create'
end 'Holder'

function wrap(name ByteArray) returns Holder
	return Holder.create(name)
end 'wrap'

function main() returns ExitCode
	print("{wrap(b"abc".clone()).name.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:15:15: 'wrap' needs 'name' frozen (line 11: `return Holder.create(name)`); a fresh value is mutable. Freeze it first: write `freeze` before the fresh expression (`freeze Box.create(1)`), or around a receiver (`(freeze Table.create()).submit(a)`)
```

<!-- test: a-match-expression-with-a-panic-arm-returns-a-declared-immutable-result -->
The arm that panics yields no value, so it does not make the merged result mutable.

```maxon
union Kind
	managed
	scalar
end 'Kind'

function constant() returns immutable ByteArray
	return b"retain"
end 'constant'

function pick(kind Kind) returns immutable ByteArray
	return match kind 'k'
		managed gives constant()
		scalar panic("pick: a scalar has no callee")
	end 'k'
end 'pick'

function main() returns ExitCode
	print("{pick(Kind.managed).count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

<!-- test: an-all-let-record-read-from-an-element-is-stored-into-a-plain-payload -->
A record whose fields are all `let` values has no writer, so a read of it stores anywhere.

```maxon
typealias LocationArray = Array with Location

type Location
	export let path as String

	static function create(path String) returns Location
		return Self{path: path}
	end 'create'
end 'Location'

union Origin
	declaredIn(site Location)
end 'Origin'

function originOf(files LocationArray) returns Origin
	return Origin.declaredIn(try files.get(0) otherwise panic("originOf: no files"))
end 'originOf'

function main() returns ExitCode
	var files = LocationArray.create(0)
	files.push(Location.create("a.maxon"))

	match originOf(files) 'origin'
		declaredIn(site) then print("{site.path}\n")
	end 'origin'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
a.maxon
```

<!-- test: a-record-of-let-values-is-a-map-key-and-a-set-element-with-no-freeze -->
A record whose every field is a `let` value (`FilePath`) is itself a value: `immutable` over it means the type, so a
fresh one enters a `Map` key or a `Set` element with no `freeze`.

```maxon
typealias PathSet = Set with FilePath
typealias PathCounts = Map with (FilePath, Count)

function main() returns ExitCode
	var paths = PathSet.create()
	paths.insert(FilePath from "a.txt")

	var counts = PathCounts.create()
	counts.upsert(FilePath from "b.txt", value: 2)

	let again = FilePath from "a.txt"
	print("{paths.count()} {counts.count()} {paths.contains(again)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1 true
```

<!-- test: a-constant-record-of-let-values-is-stored-in-a-plain-field -->
`LogLevel.info` is a constant of an all-`let` record, so a plain field holds it and nothing is pinned.

```maxon
type Holder
	export var level as LogLevel

	static function create(level LogLevel) returns Holder
		return Self{level: level}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create(LogLevel.info)
	print("{holder.level.rank}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: a-record-of-let-values-crosses-a-message-with-no-capability-word -->
`Reading` holds only `let` numbers, so it is a value on a message like a `String`.

```maxon
typealias Total = int(0 to 1000000)

type Reading
	export let label as String
	export let amount as Total

	static function create(label String, amount Total) returns Self
		return Self{label: label, amount: amount}
	end 'create'
end 'Reading'

type Meter
	var total as Total

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function add(reading Reading) returns Total
		self.total = self.total + reading.amount
		return self.total
	end 'add'
end 'Meter'

function main() returns ExitCode
	let meter = spawn Meter.create()
	let reading = Reading.create("five", amount: 5)
	let first = try await meter.add(reading) otherwise panic("main: a fresh meter answers add")
	let second = try await meter.add(Reading.create("seven", amount: 7)) otherwise panic("main: a fresh meter answers add")
	meter.shutdown()
	print("{first} {second}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 12
```

<!-- test: error.an-all-let-record-holding-an-array-still-needs-a-capability-word -->
An array is writable state, so a record that holds one is not a value even when every field is a `let`.

```maxon
typealias Total = int(0 to 1000000)
typealias TotalArray = Array with Total

type Readings
	export let amounts as TotalArray

	static function create() returns Self
		return Self{amounts: TotalArray.create(0)}
	end 'create'
end 'Readings'

type Meter
	var total as Total

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function add(readings Readings) returns Total
		self.total = self.total + (readings.amounts.count() as Total)
		return self.total
	end 'add'
end 'Meter'

function main() returns ExitCode
	let meter = spawn Meter.create()
	meter.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:27:14: parameter `readings` of the message `Meter.add` is a `Readings` with no capability word, and this `spawn` makes `Meter` a service — whose messages hand their arguments to another green thread, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `add(readings isolated …)` or `add(readings immutable …)`
```

<!-- test: a-lookup-with-an-immutable-key-and-a-fresh-fallback-is-written -->
The key is `immutable`, but the value the lookup answers is read from the map's value slot, so `lines` is writable.

```maxon
typealias CountArray = Array with Count
typealias LinesMap = Map with (ByteArray, CountArray)
typealias TokenArray = Array with Token

type Token
	export let value as immutable ByteArray

	static function create(value immutable ByteArray) returns Token
		return Self{value: value}
	end 'create'
end 'Token'

function collect(tokens TokenArray) returns LinesMap
	var built = LinesMap.create()

	for tok in tokens 'eachToken'
		var lines = try built.get(tok.value) otherwise CountArray.create(0)
		lines.push(1)
		built.upsert(tok.value, value: lines)
	end 'eachToken'

	return built
end 'collect'

function main() returns ExitCode
	var tokens = TokenArray.create(0)
	tokens.push(Token.create(b"a"))
	tokens.push(Token.create(b"a"))
	print("{collect(tokens).count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: an-arm-returns-an-immutable-result-beside-an-arm-that-panics-over-an-owned-payload -->
The arm that panics returns no value, so the one arm that returns decides the result.

```maxon
typealias CountArray = Array with Count

type Bag
	export let items as CountArray

	static function create() returns Bag
		return Self{items: CountArray.create(0)}
	end 'create'
end 'Bag'

union Lookup
	found(let bag Bag)
	notFound
end 'Lookup'

function lookup(flag bool) returns Lookup
	return Lookup.found(Bag.create()) if flag else Lookup.notFound
end 'lookup'

function constant() returns immutable ByteArray
	return b"abc"
end 'constant'

function viaMatch() returns immutable ByteArray
	match lookup(true) 'resolve'
		notFound then panic("viaMatch: nothing")
		found(bag) then return constant() if bag.items.count() > 100 else constant()
	end 'resolve'
end 'viaMatch'

function main() returns ExitCode
	print("{viaMatch().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: an-immutable-constant-is-returned-past-two-owned-locals -->
Every owned local is dropped on the way out, and the one return still carries the constant's class.

```maxon
typealias CountArray = Array with Count

type Bag
	export let items as CountArray

	static function create() returns Bag
		return Self{items: CountArray.create(0)}
	end 'create'
end 'Bag'

function pick() returns immutable ByteArray
	let first = Bag.create()
	let second = Bag.create()
	print("{first.items.count() + second.items.count()}\n")
	return b"picked"
end 'pick'

function main() returns ExitCode
	print("{pick().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
6
```

<!-- test: a-name-read-through-a-result-chain-keeps-its-immutable-element -->
An element of an array of `immutable` names stays `immutable` when the array is reached through a call result.

```maxon
typealias NameRoster = Array with immutable ByteArray

type Frozen
	export let texts as NameRoster

	static function of(texts NameRoster) returns Frozen
		return Self{texts: texts}
	end 'of'
end 'Frozen'

union Base
	none
	over(let frozen Frozen)
end 'Base'

type Holder
	let base as Base

	static function create(base Base) returns Holder
		return Self{base: base}
	end 'create'

	function names() returns Frozen
		return match self.base 'base'
			none panic("not an overlay")
			over(frozen) gives frozen
		end 'base'
	end 'names'

	function nameOf(id ElementIndex) returns immutable ByteArray
		return try self.names().texts.get(id) otherwise panic("oob")
	end 'nameOf'
end 'Holder'

function main() returns ExitCode
	var texts = NameRoster.create(1, zeroed: false)
	texts.push(b"a")
	let h = Holder.create(Base.over(Frozen.of(texts)))
	let n = h.nameOf(0)
	print("{n.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-tuple-destructured-from-a-method-result-is-stored-and-written -->
A name bound by `var (a, b) = self.method()` is the method's data, so the receiver decides whether it can be stored or written.

```maxon
typealias Slot = int(0 to 100)
typealias BlockArray = Array with Block

type Block
	export let id as Slot
	export var heat as Slot

	static function create(id Slot, heat Slot) returns Block
		return Self{id: id, heat: heat}
	end 'create'
end 'Block'

type Walker
	export var current as Block
	var blocks as BlockArray

	static function create() returns Walker
		var blocks = BlockArray.create(0)
		blocks.push(Block.create(0, heat: 0))
		return Self{current: Block.create(9, heat: 0), blocks: blocks}
	end 'create'

	function branch(heat Slot) returns (Block, Block)
		let first = Block.create(1, heat: heat)
		let second = Block.create(2, heat: 0)
		self.blocks.push(first)
		self.blocks.push(second)
		return (first, second)
	end 'branch'

	function go(heat Slot)
		var (refused, accepted) = self.branch(heat)
		refused.heat = refused.heat + 1
		self.current = accepted
		self.current = refused
	end 'go'
end 'Walker'

function main() returns ExitCode
	var walker = Walker.create()
	walker.go(3)
	print("{walker.current.id} {walker.current.heat}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 4
```

<!-- test: a-constant-name-kept-by-a-field-an-element-and-a-map-key-is-frozen-into-an-array -->
A constant name stored in an `immutable` field, an `immutable` array element and a map key is the one image record, never a
copy. Reading it back out of each holder and freezing the arrays that hold it neither copies it nor aborts the freeze.

```maxon
typealias Names = Array with immutable ByteArray
typealias Counts = Map with (ByteArray, ElementIndex)

type Token
	export let value as immutable ByteArray

	static function create(value immutable ByteArray) returns Token
		return Self{value: value}
	end 'create'
end 'Token'

typealias Tokens = Array with Token

function frozenCopyOf(names Names) returns immutable Names
	var frozen = Names.create(names.count(), zeroed: false) as isolated Names

	for name in names 'eachName'
		frozen.push(name)
	end 'eachName'

	return freeze frozen
end 'frozenCopyOf'

function main() returns ExitCode
	var tokens = Tokens.create(0)
	tokens.push(Token.create(b"generateRung"))
	tokens.push(Token.create(b"abc"))

	var counts = Counts.create()
	counts.upsert(b"generateRung", value: 1)
	counts.upsert(b"abc", value: 2)

	var names = Names.create(0)

	for token in tokens 'eachToken'
		names.push(token.value)
	end 'eachToken'

	for (key, _) in counts 'eachKey'
		names.push(key)
	end 'eachKey'

	let first = frozenCopyOf(names)
	let second = frozenCopyOf(names)
	print("{first.count()} {second.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 4
```

<!-- test: a-constant-name-handed-through-a-plain-parameter-into-a-map-is-not-copied -->
`record` takes its name and identity as plain parameters and files them under a `Map` key and an `immutable` value.
Every caller passes constants, so the parameters are read-only, and the key and the value are the image records
themselves: the map is cloned into a shared cell, whose freeze finds no record with an owner outside it.

```maxon
typealias NominalMap = Map with (ByteArray, immutable ByteArray)

let HashName = b"HashValue"
let OrderName = b"Ordering"

type Registry
	var nominals as NominalMap

	static function create() returns Registry
		return Self{nominals: NominalMap.create()}
	end 'create'

	function record(name ByteArray, identity ByteArray)
		self.nominals.upsert(name, value: identity)
	end 'record'

	function count() returns ElementIndex
		return self.nominals.count()
	end 'count'
end 'Registry'

function identityOf(name immutable ByteArray) returns immutable ByteArray
	return name
end 'identityOf'

type Box
	export let registry as Registry

	static function create(registry Registry) returns Self
		return Self{registry: registry}
	end 'create'
end 'Box'

typealias BoxCell = SharedValue with Box

function main() returns ExitCode
	var registry = Registry.create()
	registry.record(HashName, identity: identityOf(HashName))
	registry.record(OrderName, identity: identityOf(OrderName))
	let cell = BoxCell.create(Box.create(registry.clone()))
	print("{registry.count()} {cell.current().registry.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 2
```

<!-- test: a-clone-of-a-record-holding-an-immutable-name-is-sent-to-an-async-worker -->
Cloning a record shares the `immutable` name it holds instead of copying it, so the clone is sole and the send to an
async worker finds no owner outside the graph it moves.

```maxon
let Label = b"alpha"

type Tok
	export let value as immutable ByteArray

	static function create(value ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function measure(tok Tok) returns Count
	return tok.value.count()
end 'measure'

function built() returns immutable ByteArray
	var bytes = ByteArray.create(0) as isolated ByteArray

	modify bytes 'fill'
		bytes.push(1)
		bytes.push(2)
		bytes.push(3)
	end 'fill'

	return freeze bytes
end 'built'

function main() returns ExitCode
	let tok = Tok.create(Label)
	let copy = tok.clone()
	let again = copy.clone()
	let first = async measure(copy)
	let second = async measure(again)
	let frozen = Tok.create(built())
	let third = async measure(frozen.clone())
	print("{await first} {await second} {await third} {tok.value.count()} {frozen.value.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 5 3 5 3
```
