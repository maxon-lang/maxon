---
feature: regions-recover-type-names
status: experimental
keywords: [isolated, recover, payload, match, E3200]
category: semantics
---

# Whether an owned payload's type is a value is decided by its own name

## Documentation

An owned union's plain payload goes where an `isolated` value goes (`regions-recover`). Whether the payload's type is
a value type, which would make it no region at all, is decided from that type's own declaration, however many type
names the program declares before it. Each case declares a different number of type names before `Box`, so `Box`'s
position among the program's names changes from case to case while the verdict stays the same.

## Tests

<!-- test: the-payload-type-preceded-by-0-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 0 other type names, goes to an `isolated` parameter.
```maxon
typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-1-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 1 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)

type Filler
	var a1 as A1

	static function create() returns Self
		return Self{a1: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-2-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 2 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)

type Filler
	var a1 as A1
	var a2 as A2

	static function create() returns Self
		return Self{a1: 0, a2: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-3-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 3 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-4-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 4 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-5-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 5 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-6-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 6 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-7-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 7 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-8-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 8 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-9-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 9 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-10-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 10 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-11-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 11 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-12-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 12 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-13-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 13 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-14-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 14 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-15-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 15 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-16-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 16 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-17-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 17 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-18-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 18 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-19-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 19 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-20-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 20 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-21-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 21 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-22-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 22 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-23-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 23 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-24-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 24 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-25-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 25 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-26-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 26 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)
typealias A26 = int(0 to 26)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25
	var a26 as A26

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-27-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 27 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)
typealias A26 = int(0 to 26)
typealias A27 = int(0 to 27)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25
	var a26 as A26
	var a27 as A27

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0, a27: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-28-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 28 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)
typealias A26 = int(0 to 26)
typealias A27 = int(0 to 27)
typealias A28 = int(0 to 28)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25
	var a26 as A26
	var a27 as A27
	var a28 as A28

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0, a27: 0, a28: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-29-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 29 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)
typealias A26 = int(0 to 26)
typealias A27 = int(0 to 27)
typealias A28 = int(0 to 28)
typealias A29 = int(0 to 29)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25
	var a26 as A26
	var a27 as A27
	var a28 as A28
	var a29 as A29

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0, a27: 0, a28: 0, a29: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-30-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 30 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)
typealias A26 = int(0 to 26)
typealias A27 = int(0 to 27)
typealias A28 = int(0 to 28)
typealias A29 = int(0 to 29)
typealias A30 = int(0 to 30)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25
	var a26 as A26
	var a27 as A27
	var a28 as A28
	var a29 as A29
	var a30 as A30

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0, a27: 0, a28: 0, a29: 0, a30: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-31-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 31 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)
typealias A26 = int(0 to 26)
typealias A27 = int(0 to 27)
typealias A28 = int(0 to 28)
typealias A29 = int(0 to 29)
typealias A30 = int(0 to 30)
typealias A31 = int(0 to 31)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25
	var a26 as A26
	var a27 as A27
	var a28 as A28
	var a29 as A29
	var a30 as A30
	var a31 as A31

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0, a27: 0, a28: 0, a29: 0, a30: 0, a31: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-32-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 32 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)
typealias A26 = int(0 to 26)
typealias A27 = int(0 to 27)
typealias A28 = int(0 to 28)
typealias A29 = int(0 to 29)
typealias A30 = int(0 to 30)
typealias A31 = int(0 to 31)
typealias A32 = int(0 to 32)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25
	var a26 as A26
	var a27 as A27
	var a28 as A28
	var a29 as A29
	var a30 as A30
	var a31 as A31
	var a32 as A32

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0, a27: 0, a28: 0, a29: 0, a30: 0, a31: 0, a32: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-33-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 33 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)
typealias A26 = int(0 to 26)
typealias A27 = int(0 to 27)
typealias A28 = int(0 to 28)
typealias A29 = int(0 to 29)
typealias A30 = int(0 to 30)
typealias A31 = int(0 to 31)
typealias A32 = int(0 to 32)
typealias A33 = int(0 to 33)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25
	var a26 as A26
	var a27 as A27
	var a28 as A28
	var a29 as A29
	var a30 as A30
	var a31 as A31
	var a32 as A32
	var a33 as A33

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0, a27: 0, a28: 0, a29: 0, a30: 0, a31: 0, a32: 0, a33: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-34-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 34 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)
typealias A26 = int(0 to 26)
typealias A27 = int(0 to 27)
typealias A28 = int(0 to 28)
typealias A29 = int(0 to 29)
typealias A30 = int(0 to 30)
typealias A31 = int(0 to 31)
typealias A32 = int(0 to 32)
typealias A33 = int(0 to 33)
typealias A34 = int(0 to 34)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25
	var a26 as A26
	var a27 as A27
	var a28 as A28
	var a29 as A29
	var a30 as A30
	var a31 as A31
	var a32 as A32
	var a33 as A33
	var a34 as A34

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0, a27: 0, a28: 0, a29: 0, a30: 0, a31: 0, a32: 0, a33: 0, a34: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-35-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 35 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)
typealias A26 = int(0 to 26)
typealias A27 = int(0 to 27)
typealias A28 = int(0 to 28)
typealias A29 = int(0 to 29)
typealias A30 = int(0 to 30)
typealias A31 = int(0 to 31)
typealias A32 = int(0 to 32)
typealias A33 = int(0 to 33)
typealias A34 = int(0 to 34)
typealias A35 = int(0 to 35)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25
	var a26 as A26
	var a27 as A27
	var a28 as A28
	var a29 as A29
	var a30 as A30
	var a31 as A31
	var a32 as A32
	var a33 as A33
	var a34 as A34
	var a35 as A35

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0, a27: 0, a28: 0, a29: 0, a30: 0, a31: 0, a32: 0, a33: 0, a34: 0, a35: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-36-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 36 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)
typealias A26 = int(0 to 26)
typealias A27 = int(0 to 27)
typealias A28 = int(0 to 28)
typealias A29 = int(0 to 29)
typealias A30 = int(0 to 30)
typealias A31 = int(0 to 31)
typealias A32 = int(0 to 32)
typealias A33 = int(0 to 33)
typealias A34 = int(0 to 34)
typealias A35 = int(0 to 35)
typealias A36 = int(0 to 36)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25
	var a26 as A26
	var a27 as A27
	var a28 as A28
	var a29 as A29
	var a30 as A30
	var a31 as A31
	var a32 as A32
	var a33 as A33
	var a34 as A34
	var a35 as A35
	var a36 as A36

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0, a27: 0, a28: 0, a29: 0, a30: 0, a31: 0, a32: 0, a33: 0, a34: 0, a35: 0, a36: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-37-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 37 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)
typealias A26 = int(0 to 26)
typealias A27 = int(0 to 27)
typealias A28 = int(0 to 28)
typealias A29 = int(0 to 29)
typealias A30 = int(0 to 30)
typealias A31 = int(0 to 31)
typealias A32 = int(0 to 32)
typealias A33 = int(0 to 33)
typealias A34 = int(0 to 34)
typealias A35 = int(0 to 35)
typealias A36 = int(0 to 36)
typealias A37 = int(0 to 37)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25
	var a26 as A26
	var a27 as A27
	var a28 as A28
	var a29 as A29
	var a30 as A30
	var a31 as A31
	var a32 as A32
	var a33 as A33
	var a34 as A34
	var a35 as A35
	var a36 as A36
	var a37 as A37

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0, a27: 0, a28: 0, a29: 0, a30: 0, a31: 0, a32: 0, a33: 0, a34: 0, a35: 0, a36: 0, a37: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-38-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 38 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)
typealias A26 = int(0 to 26)
typealias A27 = int(0 to 27)
typealias A28 = int(0 to 28)
typealias A29 = int(0 to 29)
typealias A30 = int(0 to 30)
typealias A31 = int(0 to 31)
typealias A32 = int(0 to 32)
typealias A33 = int(0 to 33)
typealias A34 = int(0 to 34)
typealias A35 = int(0 to 35)
typealias A36 = int(0 to 36)
typealias A37 = int(0 to 37)
typealias A38 = int(0 to 38)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25
	var a26 as A26
	var a27 as A27
	var a28 as A28
	var a29 as A29
	var a30 as A30
	var a31 as A31
	var a32 as A32
	var a33 as A33
	var a34 as A34
	var a35 as A35
	var a36 as A36
	var a37 as A37
	var a38 as A38

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0, a27: 0, a28: 0, a29: 0, a30: 0, a31: 0, a32: 0, a33: 0, a34: 0, a35: 0, a36: 0, a37: 0, a38: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-39-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 39 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)
typealias A26 = int(0 to 26)
typealias A27 = int(0 to 27)
typealias A28 = int(0 to 28)
typealias A29 = int(0 to 29)
typealias A30 = int(0 to 30)
typealias A31 = int(0 to 31)
typealias A32 = int(0 to 32)
typealias A33 = int(0 to 33)
typealias A34 = int(0 to 34)
typealias A35 = int(0 to 35)
typealias A36 = int(0 to 36)
typealias A37 = int(0 to 37)
typealias A38 = int(0 to 38)
typealias A39 = int(0 to 39)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25
	var a26 as A26
	var a27 as A27
	var a28 as A28
	var a29 as A29
	var a30 as A30
	var a31 as A31
	var a32 as A32
	var a33 as A33
	var a34 as A34
	var a35 as A35
	var a36 as A36
	var a37 as A37
	var a38 as A38
	var a39 as A39

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0, a27: 0, a28: 0, a29: 0, a30: 0, a31: 0, a32: 0, a33: 0, a34: 0, a35: 0, a36: 0, a37: 0, a38: 0, a39: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: the-payload-type-preceded-by-40-declarations-is-judged-by-its-own-name -->
The owned payload of type `Box`, declared after 40 other type names, goes to an `isolated` parameter.
```maxon
typealias A1 = int(0 to 1)
typealias A2 = int(0 to 2)
typealias A3 = int(0 to 3)
typealias A4 = int(0 to 4)
typealias A5 = int(0 to 5)
typealias A6 = int(0 to 6)
typealias A7 = int(0 to 7)
typealias A8 = int(0 to 8)
typealias A9 = int(0 to 9)
typealias A10 = int(0 to 10)
typealias A11 = int(0 to 11)
typealias A12 = int(0 to 12)
typealias A13 = int(0 to 13)
typealias A14 = int(0 to 14)
typealias A15 = int(0 to 15)
typealias A16 = int(0 to 16)
typealias A17 = int(0 to 17)
typealias A18 = int(0 to 18)
typealias A19 = int(0 to 19)
typealias A20 = int(0 to 20)
typealias A21 = int(0 to 21)
typealias A22 = int(0 to 22)
typealias A23 = int(0 to 23)
typealias A24 = int(0 to 24)
typealias A25 = int(0 to 25)
typealias A26 = int(0 to 26)
typealias A27 = int(0 to 27)
typealias A28 = int(0 to 28)
typealias A29 = int(0 to 29)
typealias A30 = int(0 to 30)
typealias A31 = int(0 to 31)
typealias A32 = int(0 to 32)
typealias A33 = int(0 to 33)
typealias A34 = int(0 to 34)
typealias A35 = int(0 to 35)
typealias A36 = int(0 to 36)
typealias A37 = int(0 to 37)
typealias A38 = int(0 to 38)
typealias A39 = int(0 to 39)
typealias A40 = int(0 to 40)

type Filler
	var a1 as A1
	var a2 as A2
	var a3 as A3
	var a4 as A4
	var a5 as A5
	var a6 as A6
	var a7 as A7
	var a8 as A8
	var a9 as A9
	var a10 as A10
	var a11 as A11
	var a12 as A12
	var a13 as A13
	var a14 as A14
	var a15 as A15
	var a16 as A16
	var a17 as A17
	var a18 as A18
	var a19 as A19
	var a20 as A20
	var a21 as A21
	var a22 as A22
	var a23 as A23
	var a24 as A24
	var a25 as A25
	var a26 as A26
	var a27 as A27
	var a28 as A28
	var a29 as A29
	var a30 as A30
	var a31 as A31
	var a32 as A32
	var a33 as A33
	var a34 as A34
	var a35 as A35
	var a36 as A36
	var a37 as A37
	var a38 as A38
	var a39 as A39
	var a40 as A40

	static function create() returns Self
		return Self{a1: 0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0, a27: 0, a28: 0, a29: 0, a30: 0, a31: 0, a32: 0, a33: 0, a34: 0, a35: 0, a36: 0, a37: 0, a38: 0, a39: 0, a40: 0}
	end 'create'
end 'Filler'

typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let _ = Filler.create()
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```
