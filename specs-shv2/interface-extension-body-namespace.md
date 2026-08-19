---
feature: interface-extension-body-namespace
status: stable
keywords: [extension, interface, scope, shadowing, conformer, fields]
category: type-system
---

# An interface extension's body does not see the conformer's fields

## Documentation

`export extension Iterable` is re-parsed once per CONFORMER, with the enclosing type set to that conformer —
which is what lets `for item in self` walk it and what makes `self.createIterator()` a direct call. The
conformer's FIELDS were installed into that body as bare aliases too, and they must not be: the body is
written in another file, against the INTERFACE, by an author who cannot see them.

**An interface declares a contract, not storage.** A bare name in such a body therefore denotes a local, a
parameter or nothing at all — never a conformer field. `self.<field>` remains the explicit spelling for a
body that has some other reason to reach one.

### What the aliases cost while they were installed

Two things, and the first is the one a user meets.

E3006 refuses a local that would displace a self-field alias, because the scope is keyed by NAME and the
displaced field's own reads and writes would silently stop happening. That rule is right for a method whose
author can see both names. It is not right across a library boundary: a `type Bag uses Element implements
Iterable` whose field is spelled `item` collided with `map`'s own `for item in self`, and the whole
diagnostic was

```
error E3006: stdlib/Interfaces.maxon:201:7: local 'item' shadows self field 'Bag.item' —
rename the local to avoid silent type confusion at every read/write keyed on the name
```

— a refusal quoting a library line, naming a library local, on a program whose author's only mistake was
choosing a field name. Every local in every `extension Iterable` method was a reserved word for conformers.

The second half never produced a diagnostic at all, which is why withholding the aliases is the narrower fix
rather than merely permitting the shadow: a bare name that did NOT collide with a local resolved to the
conformer's private field, so the body's meaning depended on storage the interface does not expose and a
different conformer does not have. That is the same "silent type confusion" E3006 is named for, one scope
out.

## Tests

### A conformer field named like an extension local

`item` is `map`/`filter`/`contains`'s own loop binding in `stdlib/Interfaces.maxon`, and `Bag.item` is this
program's field. Nothing connects them.

<!-- test: a-conformer-field-named-like-an-extension-local -->
```maxon
typealias Int = int(i64.min to i64.max)

type BagIter uses Element implements Iterator with Element
	var item as Element

	export static function create(v Element) returns Self
		return Self{item: v}
	end 'create'

	export function current() returns Element
		return item
	end 'current'

	export function advance() throws IterationError
		throw IterationError.exhausted
	end 'advance'
end 'BagIter'

type Bag uses Element implements Iterable with (Element, BagIter)
	var item as Element

	export static function create(v Element) returns Self
		return Self{item: v}
	end 'create'

	export function createIterator() returns BagIter throws IterationError
		return BagIter.create(item)
	end 'createIterator'
end 'Bag'

typealias IntBag = Bag with Int

function isBig(n Int) returns bool
	return n > 10
end 'isBig'

function main() returns ExitCode
	var b = IntBag.create(30)
	if b.contains(isBig) 'yes'
		return 1
	end 'yes'
	return 0
end 'main'
```
```exitcode
1
```

### The same collision over a MANAGED element

`result` is `map`'s accumulator and `item` its trip binding; this conformer spells both as fields and holds a
real `String` in one. The per-trip `+1` and its release are unchanged by the naming — a missing release is
exit 101.

<!-- test: the-same-collision-over-a-managed-element -->
```maxon
typealias Int = int(i64.min to i64.max)

type BagIter uses Element implements Iterator with Element
	var item as Element

	export static function create(v Element) returns Self
		return Self{item: v}
	end 'create'

	export function current() returns Element
		return item
	end 'current'

	export function advance() throws IterationError
		throw IterationError.exhausted
	end 'advance'
end 'BagIter'

type Bag uses Element implements Iterable with (Element, BagIter)
	var item as Element
	var result as Int

	export static function create(v Element) returns Self
		return Self{item: v, result: 0}
	end 'create'

	export function createIterator() returns BagIter throws IterationError
		return BagIter.create(item)
	end 'createIterator'
end 'Bag'

typealias StrBag = Bag with String

function isLong(s String) returns bool
	return s.byteLength() > 5
end 'isLong'

function main() returns ExitCode
	var b = StrBag.create("a string long enough to be heap allocated")
	if b.contains(isLong) 'long'
		print("long\n")
		return 1
	end 'long'
	print("short\n")
	return 0
end 'main'
```
```exitcode
1
```
```stdout
long
```

### error.an-extension-body-cannot-name-a-conformers-field

⛔ **THE HALF THAT IS NOT SERVED, AND IT IS THE POINT RATHER THAN A GAP.** `n` is a field of every conformer
this program has, and the extension body still may not name it: the interface it extends guarantees a `tag()`
and no storage at all, so a body relying on `n` is one a second conformer would break. The refusal lands on
the extension author's own line, which is where the fix belongs.

<!-- test: error.an-extension-body-cannot-name-a-conformers-field -->
```maxon
typealias Int = int(i64.min to i64.max)

interface Tagged
	function tag() returns Int
end 'Tagged'

extension Tagged
	export function doubled() returns Int
		return n * 2
	end 'doubled'
end 'Tagged'

type Counter implements Tagged
	var n as Int

	export static function create(n Int) returns Self
		return Self{n: n}
	end 'create'

	export function tag() returns Int
		return n
	end 'tag'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(3)
	return c.doubled()
end 'main'
```
```maxoncstderr
error E2004: <fragment>:10:10: Undefined variable 'n'
```
