---
feature: generic-type-parameter-slots
status: stable
keywords: [generics, type-parameter, function-value, dictionary, layout-descriptor]
category: type-system
---

# What may occupy a generic type parameter's slot

## Documentation

A generic type's parameter is one machine word in the shared body, and the concrete type it binds to is
decided by the INSTANTIATION. So the question "may this value stand here?" is answered against the
SUBSTITUTED type argument, never against the opaque formal — the same rule a float actual at an opaque
formal already obeys.

A FUNCTION VALUE is one machine word, so it may bind a type parameter whose argument is a function
`typealias`, and may not bind one whose argument is anything else. Both halves are the substituted check;
neither is a property of the formal.

## Tests

### A function value binds a type parameter whose argument is a function alias

<!-- test: generic-type-parameter-slots.function-value-element -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function double(x Integer) returns Integer
	return x * 2
end 'double'

type Holder uses Element
	typealias EArray = Array with Element
	var items as EArray = EArray.create()

	export static function create() returns Self
		return Self{}
	end 'create'

	export function add(item Element)
		items.push(item)
	end 'add'

	export function first() returns Element throws ArrayError
		return try items.get(0)
	end 'first'
end 'Holder'

typealias FHolder = Holder with UnaryOp

function main() returns ExitCode
	var h = FHolder.create()
	h.add(double)
	let f = try h.first() otherwise panic("Holder.first: one element was added")
	return f(21)
end 'main'
```
```exitcode
42
```

### Error: and it does NOT bind one whose argument is an int

The refusal comes from the substituted argument, so it names `'int'` — the type the instantiation really
chose — and not `'type parameter'`, the shape of the formal.

<!-- test: generic-type-parameter-slots.error.function-value-into-an-int-argument -->
```maxon
typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

type Holder uses Element
	typealias EArray = Array with Element
	var items as EArray = EArray.create()

	export static function create() returns Self
		return Self{}
	end 'create'

	export function add(item Element)
		items.push(item)
	end 'add'

	export function first() returns Element throws ArrayError
		return try items.get(0)
	end 'first'
end 'Holder'

typealias IHolder = Holder with Integer

function main() returns ExitCode
	var h = IHolder.create()
	h.add(double)
	return try h.first() otherwise 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/generic-type-parameter-slots/generic-type-parameter-slots.error.function-value-into-an-int-argument.test:29:4: cannot pass a value of type 'function' as argument 'item', which holds 'int': a function value is only usable where a function type declared with 'typealias' is expected
```
