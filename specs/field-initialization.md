---
feature: field-initialization
status: stable
keywords: struct, field, initialization, factory, self
category: semantics
---
# Field Initialization

## Documentation

Every field of a struct must be initialized when the struct is constructed. A
field is considered initialized if any of the following is true:

1. The field declaration supplies a default value: `var count = 0`.
2. The struct literal provides a value for the field: `Counter{count: 5}`.
3. The literal appears as the direct return expression of a `static` factory
   function whose return type is the enclosing type, and the field is assigned
   via `self.field = expr` on every control-flow path that reaches the literal.

A literal-provided value always overrides a declared default.

Writing `Self{}` with any non-default field triggers compile error
**E3086 `SemanticFieldNotInitialized`**. Writing a literal that omits a
non-default, non-self-assigned field is also E3086.

```text
type Counter
	export var value = 0       // default: rule 1
	export var version as Integer // no default
end 'Counter'
```

The literal must then provide `version`:

```text
var c = Counter{version: 1}  // OK — value defaults to 0, version provided
```

Inside a static factory returning `Self`, the assignment form can supply the
value:

```text
type Counter
	export var value as Integer
	export var version as Integer

	export static function create(initial Integer) returns Self
		self.value = initial
		self.version = 1
		return Self{}
	end 'create'
end 'Counter'
```

The compiler proves that each field is assigned on every path to the return; a
conditional write that reaches the return only on some paths is rejected.

## Tests

<!-- test: all-in-literal -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	export static function make(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'make'
end 'Point'

function main() returns ExitCode
	let p = Point.make(1, y: 41)
	return p.x + p.y
end 'main'
```
```exitcode
42
```

<!-- test: all-defaults -->
```maxon
type Defaults
	export var a = 10
	export var b = 32

	export static function make() returns Self
		return Self{}
	end 'make'
end 'Defaults'

function main() returns ExitCode
	let d = Defaults.make()
	return d.a + d.b
end 'main'
```
```exitcode
42
```

<!-- test: literal-overrides-default -->
```maxon
type Thing
	export var value = 7

	export static function make(value Integer) returns Self
		return Self{value: value}
	end 'make'
end 'Thing'

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let t = Thing.make(42)
	return t.value
end 'main'
```
```exitcode
42
```

<!-- test: mixed-default-and-literal -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Mixed
	export var a = 10
	export var b as Integer

	export static function make(b Integer) returns Self
		return Self{b: b}
	end 'make'
end 'Mixed'

function main() returns ExitCode
	let m = Mixed.make(32)
	return m.a + m.b
end 'main'
```
```exitcode
42
```

<!-- test: empty-literal-no-defaults-errors -->
```maxon

typealias Integer = int(i64.min to i64.max)

type P
	export var x as Integer
	export var y as Integer

	export static function make() returns Self
		return Self{}
	end 'make'
end 'P'

function main() returns ExitCode
	_ = P.make()
	return 0
end 'main'
```
```maxoncstderr
error E3086: specs/fragments/field-initialization/empty-literal-no-defaults-errors.test:10:14: Fields 'x', 'y' of type 'P' are not initialized (provide in literal, add a default value on the declaration, or assign via self.field in a static factory)
```

<!-- test: missing-field-errors -->
```maxon

typealias Integer = int(i64.min to i64.max)

type P
	export var x as Integer
	export var y as Integer

	export static function make(x Integer) returns Self
		return Self{x: x}
	end 'make'
end 'P'

function main() returns ExitCode
	_ = P.make(0)
	return 0
end 'main'
```
```maxoncstderr
error E3086: specs/fragments/field-initialization/missing-field-errors.test:10:14: Field 'y' of type 'P' is not initialized (provide in literal, add a default value on the declaration, or assign via self.field in a static factory)
```

<!-- test: missing-non-exported-errors -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Q
	var hidden as Integer
	export var shown as Integer

	export static function make(s Integer) returns Self
		return Self{shown: s}
	end 'make'
end 'Q'

function main() returns ExitCode
	_ = Q.make(0)
	return 0
end 'main'
```
```maxoncstderr
error E3086: specs/fragments/field-initialization/missing-non-exported-errors.test:10:14: Field 'hidden' of type 'Q' is not initialized (provide in literal, add a default value on the declaration, or assign via self.field in a static factory)
```

<!-- test: factory-self-assign-straight-line -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter
	export var value as Integer
	export var version as Integer

	export static function make(initial Integer) returns Self
		self.value = initial
		self.version = 1
		return Self{}
	end 'make'
end 'Counter'

function main() returns ExitCode
	let c = Counter.make(41)
	return c.value + c.version
end 'main'
```
```exitcode
42
```

<!-- test: factory-self-assign-both-branches -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Branched
	export var value as Integer

	export static function make(sign bool) returns Self
		if sign 'br'
			self.value = 42
		end 'br' else 'other'
			self.value = -42
		end 'other'
		return Self{}
	end 'make'
end 'Branched'

function main() returns ExitCode
	let b = Branched.make(true)
	return b.value
end 'main'
```
```exitcode
42
```

<!-- test: factory-self-assign-one-branch-errors -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Broken
	export var value as Integer

	export static function make(sign bool) returns Self
		if sign 'br'
			self.value = 42
		end 'br'
		return Self{}
	end 'make'
end 'Broken'

function main() returns ExitCode
	let b = Broken.make(true)
	return b.value
end 'main'
```
```maxoncstderr
error E3086: specs/fragments/field-initialization/factory-self-assign-one-branch-errors.test:12:14: field 'value' of type 'Broken' is not definitely assigned: the 'self.value = ...' assignment does not reach this Self{...} literal on all control-flow paths
```

<!-- test: factory-self-assign-loop-only-errors -->
```maxon

typealias Integer = int(i64.min to i64.max)

type LoopOnly
	export var value as Integer

	export static function make(limit Integer) returns Self
		var i = 0
		while i < limit 'loop'
			self.value = i
			i = i + 1
		end 'loop'
		return Self{}
	end 'make'
end 'LoopOnly'

function main() returns ExitCode
	let l = LoopOnly.make(1)
	return l.value
end 'main'
```
```maxoncstderr
error E3086: specs/fragments/field-initialization/factory-self-assign-loop-only-errors.test:14:14: field 'value' of type 'LoopOnly' is not definitely assigned: the 'self.value = ...' assignment does not reach this Self{...} literal on all control-flow paths
```

<!-- test: factory-multiple-returns -->
```maxon

typealias Integer = int(i64.min to i64.max)

type TwoReturns
	export var value as Integer

	export static function make(sign bool) returns Self
		if sign 'br'
			self.value = 42
			return Self{}
		end 'br' else 'other'
			self.value = -42
			return Self{}
		end 'other'
	end 'make'
end 'TwoReturns'

function main() returns ExitCode
	let t = TwoReturns.make(true)
	return t.value
end 'main'
```
```exitcode
42
```
