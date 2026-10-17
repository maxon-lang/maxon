---
feature: borrowed-field-getters
status: stable
keywords: [getter, borrow, field, receiver, refcount, hold, uaf, memory]
category: memory
---
# Borrowed field getters — a method that returns a field of its receiver hands back no reference of its own

## Documentation

A method that takes no argument but its receiver and whose every `return` is `self.<field>` hands back the
record that field names, and hands it back uncounted. The caller treats the result exactly as it treats the
field read `receiver.field`: a binding of it, an argument it is passed as and a receiver it is used on each
take a reference of their own only where something between the read and the last use can replace the
field or release the receiver. A method that returns anything else, that declares a parameter, that
implements an interface requirement or whose receiver is generic returns a counted reference as before.

## Tests

<!-- test: a-getter-result-bound-before-the-field-is-replaced-keeps-its-value -->
`kept` is bound to the getter's result and the field is then replaced. The binding holds its own
reference, so it still reads the first note after the replacement released the field's.
```maxon
typealias Count = int(0 to 1000000)

type Note
	export var text as String
	export var weight as Count

	static function create(text String, weight Count) returns Note
		return Note{text: text, weight: weight}
	end 'create'
end 'Note'

type Shelf
	var note as Note

	static function create(text String) returns Shelf
		return Shelf{note: Note.create(text, weight: 1)}
	end 'create'

	function holding() returns Note
		return self.note
	end 'holding'

	function replace(text String)
		self.note = Note.create(text, weight: 2)
	end 'replace'
end 'Shelf'

function main() returns ExitCode
	var shelf = Shelf.create("first note built at run time {40 + 2}")
	view kept = shelf.holding()
	shelf.replace("second note built at run time {40 + 3}")
	print("[{kept.text}] [{shelf.holding().text}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[first note built at run time 42] [second note built at run time 43]
```

<!-- test: a-getter-result-passed-as-an-argument-survives-the-callee-replacing-the-field -->
The callee receives the getter's result and the shelf, replaces the shelf's note, and then reads the note it
was handed. The argument is read out of storage the callee writes, so the call takes a reference to it.
```maxon
typealias Count = int(0 to 1000000)

type Note
	export var text as String
	export var weight as Count

	static function create(text String, weight Count) returns Note
		return Note{text: text, weight: weight}
	end 'create'
end 'Note'

type Shelf
	var note as Note

	static function create(text String) returns Shelf
		return Shelf{note: Note.create(text, weight: 1)}
	end 'create'

	function holding() returns Note
		return self.note
	end 'holding'

	function replace(text String)
		self.note = Note.create(text, weight: 2)
	end 'replace'
end 'Shelf'

function swapAndShow(shelf Shelf, item Note)
	shelf.replace("replacement built at run time {7 * 6}")
	print("[{item.text}] [{shelf.holding().text}]\n")
end 'swapAndShow'

function main() returns ExitCode
	var shelf = Shelf.create("original built at run time {6 * 7 + 1}")
	swapAndShow(shelf, item: shelf.holding())
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[original built at run time 43] [replacement built at run time 42]
```

<!-- test: a-getter-result-of-a-temporary-receiver-outlives-the-statement -->
The receiver is a temporary that dies at the end of the binding's statement. The binding names the note the
temporary's field held, so it owns a reference of its own.
```maxon
typealias Count = int(0 to 1000000)

type Note
	export var text as String
	export var weight as Count

	static function create(text String, weight Count) returns Note
		return Note{text: text, weight: weight}
	end 'create'
end 'Note'

type Shelf
	var note as Note

	static function create(text String) returns Shelf
		return Shelf{note: Note.create(text, weight: 1)}
	end 'create'

	function holding() returns Note
		return self.note
	end 'holding'
end 'Shelf'

function main() returns ExitCode
	let kept = Shelf.create("temporary shelf built at run time {20 + 22}").holding()
	print("[{kept.text}] {kept.weight}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[temporary shelf built at run time 42] 1
```

<!-- test: a-getter-chain-through-two-receivers-reads-the-inner-field -->
Two getters are chained, and the inner field is read through both results in one expression.
```maxon
typealias Count = int(0 to 1000000)

type Note
	export var text as String
	export var weight as Count

	static function create(text String, weight Count) returns Note
		return Note{text: text, weight: weight}
	end 'create'
end 'Note'

type Shelf
	var note as Note

	static function create(text String) returns Shelf
		return Shelf{note: Note.create(text, weight: 5)}
	end 'create'

	function holding() returns Note
		return self.note
	end 'holding'
end 'Shelf'

type Cabinet
	var inner as Shelf

	static function create(text String) returns Cabinet
		return Cabinet{inner: Shelf.create(text)}
	end 'create'

	function innerShelf() returns Shelf
		return self.inner
	end 'innerShelf'
end 'Cabinet'

function main() returns ExitCode
	let cabinet = Cabinet.create("cabinet note built at run time {21 * 2}")
	print("[{cabinet.innerShelf().holding().text}] {cabinet.innerShelf().holding().weight}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[cabinet note built at run time 42] 5
```

<!-- test: a-getter-with-two-returns-follows-its-flag -->
`active` returns one of two fields according to a flag. A binding made before the flag flips keeps the note
it was made with, and a read after the flip reaches the other field.
```maxon
typealias Count = int(0 to 1000000)

type Note
	export var text as String
	export var weight as Count

	static function create(text String, weight Count) returns Note
		return Note{text: text, weight: weight}
	end 'create'
end 'Note'

type Library
	var primary as Note
	var secondary as Note
	var usesSecondary as bool

	static function create() returns Library
		return Library{primary: Note.create("primary built at run time {10 + 1}", weight: 1), secondary: Note.create("secondary built at run time {10 + 2}", weight: 2), usesSecondary: false}
	end 'create'

	function active() returns Note
		if self.usesSecondary 'useSecondary'
			return self.secondary
		end 'useSecondary'

		return self.primary
	end 'active'

	function flip()
		self.usesSecondary = not self.usesSecondary
	end 'flip'

	function retire()
		self.primary = Note.create("retired primary built at run time {10 + 3}", weight: 3)
	end 'retire'
end 'Library'

function main() returns ExitCode
	var library = Library.create()
	view before = library.active()
	library.retire()
	library.flip()
	print("[{before.text}] [{library.active().text}] {library.active().weight}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[primary built at run time 11] [secondary built at run time 12] 2
```

<!-- test: a-getter-that-implements-an-interface-requirement-hands-back-a-counted-record -->
`note` is also the requirement of `Keeper`, so a dispatch through the interface's witness table reaches the
method and expects a counted reference. The method keeps returning one, and the direct call beside the
dispatch reads the same field.
```maxon
typealias Count = int(0 to 1000000)

type Note
	export var text as String
	export var weight as Count

	static function create(text String, weight Count) returns Note
		return Note{text: text, weight: weight}
	end 'create'
end 'Note'

interface Keeper
	function note() returns Note
end 'Keeper'

type Drawer implements Keeper
	var held as Note

	static function create(text String) returns Drawer
		return Drawer{held: Note.create(text, weight: 7)}
	end 'create'

	function note() returns Note
		return self.held
	end 'note'
end 'Drawer'

function obtainKeeper(text String) returns Keeper
	return Drawer.create(text)
end 'obtainKeeper'

function main() returns ExitCode
	let keeper = obtainKeeper("drawer note built at run time {41 + 1}")
	let dispatched = keeper.note()
	let drawer = Drawer.create("direct drawer note built at run time {41 + 2}")
	print("[{dispatched.text}] {dispatched.weight} [{drawer.note().text}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[drawer note built at run time 42] 7 [direct drawer note built at run time 43]
```

<!-- test: a-getter-read-on-every-trip-of-a-loop-that-replaces-the-field -->
The loop replaces the shelf's note on every trip and prints what the getter returns after the replacement,
while a binding made before the loop keeps the first note.
```maxon
typealias Count = int(0 to 1000000)

type Note
	export var text as String
	export var weight as Count

	static function create(text String, weight Count) returns Note
		return Note{text: text, weight: weight}
	end 'create'
end 'Note'

type Shelf
	var note as Note

	static function create(text String) returns Shelf
		return Shelf{note: Note.create(text, weight: 0)}
	end 'create'

	function holding() returns Note
		return self.note
	end 'holding'

	function replace(text String, weight Count)
		self.note = Note.create(text, weight: weight)
	end 'replace'
end 'Shelf'

function main() returns ExitCode
	var shelf = Shelf.create("loop note built at run time {0 + 100}")
	view first = shelf.holding()

	for trip in 1 to 3 'trips'
		shelf.replace("loop note built at run time {trip + 100}", weight: trip as Count)
		print("[{shelf.holding().text}] {shelf.holding().weight}\n")
	end 'trips'

	print("[{first.text}] {first.weight}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[loop note built at run time 101] 1
[loop note built at run time 102] 2
[loop note built at run time 103] 3
[loop note built at run time 100] 0
```

<!-- test: a-getter-with-a-parameter-hands-back-a-counted-record -->
`holding` declares a parameter, so it is not a borrowed getter: it returns a counted reference as an ordinary method
does. `kept` therefore still reads the first note after the field is replaced.
```maxon
typealias Count = int(0 to 1000000)

type Note
	export var text as String
	export var weight as Count

	static function create(text String, weight Count) returns Note
		return Note{text: text, weight: weight}
	end 'create'
end 'Note'

type Shelf
	var note as Note

	static function create(text String) returns Shelf
		return Shelf{note: Note.create(text, weight: 1)}
	end 'create'

	function holding(slot Count) returns Note
		if slot > 1000 'beyond'
			panic("slot beyond the shelf")
		end 'beyond'

		return self.note
	end 'holding'

	function replace(text String)
		self.note = Note.create(text, weight: 2)
	end 'replace'
end 'Shelf'

function main() returns ExitCode
	var shelf = Shelf.create("first note built at run time {40 + 2}")
	view kept = shelf.holding(0)
	shelf.replace("second note built at run time {40 + 3}")
	print("[{kept.text}] [{shelf.holding(0).text}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[first note built at run time 42] [second note built at run time 43]
```

<!-- test: a-getter-of-a-generic-receiver-hands-back-a-counted-record -->
A receiver whose type is generic is outside the rule too: `Shelf with Note` returns a counted reference, and the
binding survives the replacement of the field.
```maxon
typealias Count = int(0 to 1000000)
typealias NoteShelf = Shelf with Note

type Note
	export var text as String
	export var weight as Count

	static function create(text String, weight Count) returns Note
		return Note{text: text, weight: weight}
	end 'create'
end 'Note'

type Shelf uses Item
	var held as Item

	static function create(held Item) returns Self
		return Self{held: held}
	end 'create'

	function holding() returns Item
		return self.held
	end 'holding'

	function replace(next Item)
		self.held = next
	end 'replace'
end 'Shelf'

function main() returns ExitCode
	var shelf = NoteShelf.create(Note.create("first note built at run time {40 + 2}", weight: 1))
	view kept = shelf.holding()
	shelf.replace(Note.create("second note built at run time {40 + 3}", weight: 2))
	print("[{kept.text}] [{shelf.holding().text}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[first note built at run time 42] [second note built at run time 43]
```

<!-- test: a-method-that-returns-a-fresh-record-is-not-a-borrowed-getter -->
`describe` returns a record it builds, not a field, so the caller owns the result and the replacement of the field
cannot reach it.
```maxon
typealias Count = int(0 to 1000000)

type Note
	export var text as String
	export var weight as Count

	static function create(text String, weight Count) returns Note
		return Note{text: text, weight: weight}
	end 'create'
end 'Note'

type Shelf
	var note as Note

	static function create(text String) returns Shelf
		return Shelf{note: Note.create(text, weight: 1)}
	end 'create'

	function describe() returns Note
		return Note.create(self.note.text, weight: 9)
	end 'describe'

	function replace(text String)
		self.note = Note.create(text, weight: 2)
	end 'replace'
end 'Shelf'

function main() returns ExitCode
	var shelf = Shelf.create("first note built at run time {40 + 2}")
	let kept = shelf.describe()
	shelf.replace("second note built at run time {40 + 3}")
	print("[{kept.text}] {kept.weight} [{shelf.describe().text}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[first note built at run time 42] 9 [second note built at run time 43]
```

<!-- test: a-getter-result-outlives-the-receiver-it-was-read-from -->
The receiver is released while the getter's result is still live: `shelf` is reassigned, which drops the old shelf and
its field. The binding holds a reference of its own, so it reads the first note afterwards.
```maxon
typealias Count = int(0 to 1000000)

type Note
	export var text as String
	export var weight as Count

	static function create(text String, weight Count) returns Note
		return Note{text: text, weight: weight}
	end 'create'
end 'Note'

type Shelf
	var note as Note

	static function create(text String) returns Shelf
		return Shelf{note: Note.create(text, weight: 1)}
	end 'create'

	function holding() returns Note
		return self.note
	end 'holding'
end 'Shelf'

function main() returns ExitCode
	var shelf = Shelf.create("first note built at run time {40 + 2}")
	view kept = shelf.holding()
	shelf = Shelf.create("second note built at run time {40 + 3}")
	print("[{kept.text}] [{shelf.holding().text}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[first note built at run time 42] [second note built at run time 43]
```
