---
feature: function-overloads
status: stable
keywords: [function, overload, disambiguation, parameter, types]
category: functions
---

## Documentation

### Function Overloads

Maxon supports function overloading — multiple functions with the same name but different signatures.

#### Disambiguation by parameter types

When overloads differ in their parameter types, the compiler automatically selects the correct overload based on the argument types at the call site:

```text
function process(value int) returns int
  return value * 2
end 'process'

function process(value String) returns int
  return value.count()
end 'process'

process(42)        // calls process(value int)
process("hello")   // calls process(value String)
```

#### Disambiguation by parameter names

When overloads have different parameter names, the caller uses named arguments to select the correct overload:

```text
function create(name String) returns String
  return name
end 'create'

function create(label String) returns String
  return label
end 'create'

create("foo")    // calls first overload
create("bar")   // calls second overload
```

#### Ambiguous calls

If the compiler cannot determine which overload to call based on argument types alone, it requires named arguments. Calling an ambiguous overload without named arguments is a compile error.

## Tests

<!-- test: basic-type-disambiguation -->
```maxon
typealias Integer = int(i64.min to i64.max)

function process(value Integer) returns Integer
	return value * 2
end 'process'

function process(value String) returns Integer
	return value.count()
end 'process'

function main() returns ExitCode
	return process(21)
end 'main'
```
```exitcode
42
```

<!-- test: basic-type-disambiguation-string -->
```maxon
typealias Integer = int(i64.min to i64.max)

function process(value Integer) returns Integer
	return value * 2
end 'process'

function process(value String) returns Integer
	return value.count()
end 'process'

function main() returns ExitCode
	return process("hello world hello world hello world hello worl!")
end 'main'
```
```exitcode
47
```

<!-- test: name-disambiguation-preserved -->
```maxon
typealias Integer = int(i64.min to i64.max)

function slice(start Integer, endIndex Integer) returns Integer
	return endIndex - start
end 'slice'

function slice(start Integer, length Integer) returns Integer
	return start + length
end 'slice'

function main() returns ExitCode
	return slice(10, length: 32)
end 'main'
```
```exitcode
42
```

<!-- test: error.ambiguous-same-signature -->
```maxon
typealias Integer = int(i64.min to i64.max)

function create(name String) returns Integer
	return name.count()
end 'create'

function create(label String) returns Integer
	return label.count()
end 'create'

function main() returns ExitCode
	return create("hello")
end 'main'
```
```maxoncstderr
error E3007: specs/fragments/function-overloads/error.ambiguous-same-signature.test:13:9: Ambiguous overload for 'create': multiple overloads match. Candidates: (name String), (label String)
```

<!-- test: method-type-disambiguation -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Converter
	static function create() returns Self
		return Self{}
	end 'create'

	function convert(value Integer) returns Integer
		return value * 2
	end 'convert'

	function convert(value String) returns Integer
		return value.count()
	end 'convert'
end 'Converter'

function main() returns ExitCode
	var c = Converter.create()
	return c.convert(21)
end 'main'
```
```exitcode
42
```

<!-- test: variable-type-inference -->
```maxon
typealias Integer = int(i64.min to i64.max)

function process(value Integer) returns Integer
	return value * 2
end 'process'

function process(value String) returns Integer
	return value.count()
end 'process'

function main() returns ExitCode
	let x = 21
	return process(x)
end 'main'
```
```exitcode
42
```

<!-- test: string-contains-char -->
```maxon
function main() returns ExitCode
	let text = "hello"
	if text.contains('e') 'check'
		return 1
	end 'check' else 'other'
		return 0
	end 'other'
end 'main'
```
```exitcode
1
```

<!-- test: string-contains-string -->
```maxon
function main() returns ExitCode
	let text = "hello world"
	if text.contains("world") 'check'
		return 1
	end 'check' else 'other'
		return 0
	end 'other'
end 'main'
```
```exitcode
1
```

<!-- test: bool-type-disambiguation -->
```maxon
typealias Integer = int(i64.min to i64.max)

function check(value Integer) returns Integer
	return value
end 'check'

function check(value bool) returns Integer
	if value 'branch'
		return 1
	end 'branch' else 'other'
		return 0
	end 'other'
end 'check'

function main() returns ExitCode
	return check(true)
end 'main'
```
```exitcode
1
```

<!-- test: float-type-disambiguation -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Decimal = float(f64.min to f64.max)

function measure(value Integer) returns Integer
	return value
end 'measure'

function measure(value Decimal) returns Integer
	return trunc(value)
end 'measure'

function main() returns ExitCode
	return measure(42.0)
end 'main'
```
```exitcode
42
```

<!-- test: second-param-disambiguation-result-type -->
Two overloads that SHARE their leading parameter type must still be told apart
by their later parameters — the overload key mangles every parameter, not just
the first. Here both `lookup` overloads start with `Registry`, so a resolver
that keyed only on the first argument would collapse them to one bucket member
and mis-type the result of the call. Because the overloads return DIFFERENT
types (`Integer` vs `bool`), picking the wrong one would flow the wrong type
into the downstream `use(...)` argument check. Selecting by the full signature
keeps each result type correct.
```maxon
typealias Integer = int(i64.min to i64.max)

type Registry
	export var seed as Integer

	export static function make(seed Integer) returns Registry
		return Registry{seed: seed}
	end 'make'
end 'Registry'

function lookup(reg Registry, index Integer) returns Integer
	return reg.seed + index
end 'lookup'

function lookup(reg Registry, present bool) returns bool
	if present 'yes'
		return reg.seed > 0
	end 'yes'
	return false
end 'lookup'

function useInt(value Integer) returns Integer
	return value
end 'useInt'

function useBool(flag bool) returns Integer
	if flag 'on'
		return 1
	end 'on'
	return 0
end 'useBool'

function main() returns ExitCode
	let reg = Registry.make(10)
	let asInt = lookup(reg, index: 5)
	let asBool = lookup(reg, present: true)
	return useInt(asInt) + useBool(asBool)
end 'main'
```
```exitcode
16
```
