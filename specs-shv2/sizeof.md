---
feature: sizeof
status: stable
keywords: sizeof, type size, memory, intrinsic
category: intrinsic
---
# sizeof

## Documentation

Returns the size of a type in bytes as a compile-time integer constant.

## Tests

<!-- test: sizeof.type-parameter -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Sizer uses T
	export var dummy as T

	export static function create(dummy T) returns Self
		return Self{dummy: dummy}
	end 'create'

	export function typeSize() returns Integer
		return sizeof(T)
	end 'typeSize'
end 'Sizer'

typealias BoolSizer = Sizer with bool

function main() returns ExitCode
	let s = BoolSizer.create(false)
	return s.typeSize()
end 'main'
```
```exitcode
1
```

<!-- test: sizeof.type-parameter-struct -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
	export var a as Integer
	export var b as Integer

	static function create(a Integer, b Integer) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Pair'

type Sizer uses T
	export var dummy as T

	export static function create(dummy T) returns Self
		return Self{dummy: dummy}
	end 'create'

	export function typeSize() returns Integer
		return sizeof(T)
	end 'typeSize'
end 'Sizer'

typealias PairSizer = Sizer with Pair

function main() returns ExitCode
	let s = PairSizer.create(Pair.create(0, b: 0))
	return s.typeSize()
end 'main'
```
```exitcode
16
```

<!-- test: sizeof.concrete -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
	export var a as Integer
	export var b as Integer

	static function create(a Integer, b Integer) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Pair'

function main() returns ExitCode
	return sizeof(bool) + sizeof(Integer) + sizeof(Pair)
end 'main'
```
```exitcode
25
```

<!-- test: sizeof.self-forward -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Sizer uses T
	export var dummy as T

	export static function create(dummy T) returns Self
		return Self{dummy: dummy}
	end 'create'

	export function directSize() returns Integer
		return sizeof(T)
	end 'directSize'

	export function indirectSize() returns Integer
		return self.directSize()
	end 'indirectSize'
end 'Sizer'

typealias BoolSizer = Sizer with bool

function main() returns ExitCode
	let s = BoolSizer.create(false)
	return s.indirectSize()
end 'main'
```
```exitcode
1
```

<!-- test: sizeof.transitive-two-hop -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
	export var a as Integer
	export var b as Integer

	static function create(a Integer, b Integer) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Pair'

type Sizer uses T
	export var dummy as T

	export static function create(dummy T) returns Self
		return Self{dummy: dummy}
	end 'create'

	export function level0() returns Integer
		return sizeof(T)
	end 'level0'

	export function level1() returns Integer
		return self.level0()
	end 'level1'

	export function level2() returns Integer
		return self.level1()
	end 'level2'
end 'Sizer'

typealias PairSizer = Sizer with Pair

function main() returns ExitCode
	let s = PairSizer.create(Pair.create(0, b: 0))
	return s.level2()
end 'main'
```
```exitcode
16
```

<!-- test: sizeof.int -->
```maxon
function main() returns ExitCode
	return sizeof(int)
end 'main'
```
```exitcode
8
```

<!-- test: sizeof.float -->
```maxon
function main() returns ExitCode
	return sizeof(float)
end 'main'
```
```exitcode
8
```

<!-- test: sizeof.bool -->
```maxon
function main() returns ExitCode
	return sizeof(bool)
end 'main'
```
```exitcode
1
```

<!-- disabled-test: sizeof.byte -->
<!-- WRONG ANSWER, NOT A MISSING FEATURE — board row `S2v`. shv2 answers 8; canonical and the C#
     oracle both answer 1. It is not a `byte` special case: shv2 reports a MACHINE WORD for every
     ranged type (`byte` 8, `int(0 to 255)` 8, `ExitCode` 8, `int(i64.min to i64.max)` 8) where the
     oracle reports the STORAGE width (1, 1, 4, 8). Only `bool` agrees, at 1.
     `LayoutDescriptor.maxon:296-301` documents the machine-word answer as deliberate — which is why
     this is filed for a ruling-grade rung and not patched here. Re-enable at `S2v`. -->
```maxon
function main() returns ExitCode
	return sizeof(byte)
end 'main'
```
```exitcode
1
```

<!-- test: sizeof.struct -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer
end 'Point'

function main() returns ExitCode
	return sizeof(Point)
end 'main'
```
```exitcode
16
```

<!-- test: sizeof.struct-three-fields -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Vec3
	export var x as Integer
	export var y as Integer
	export var z as Integer
end 'Vec3'

function main() returns ExitCode
	return sizeof(Vec3)
end 'main'
```
```exitcode
24
```

<!-- test: sizeof.enum -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	return sizeof(Color)
end 'main'
```
```exitcode
8
```

<!-- test: sizeof.arithmetic -->
```maxon
function main() returns ExitCode
	return sizeof(int) + sizeof(bool)
end 'main'
```
```exitcode
9
```

<!-- test: sizeof.let-binding -->
```maxon
function main() returns ExitCode
	let size = sizeof(int)
	return size
end 'main'
```
```exitcode
8
```
