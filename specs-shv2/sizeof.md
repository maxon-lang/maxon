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
