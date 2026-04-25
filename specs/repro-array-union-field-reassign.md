---
feature: match-binding-vs-self-field-name-collision
status: stable
keywords: [match, binding, self, field, shadow, name-collision, codegen]
category: codegen
---

# Self-Field Shadowing Is A Compile Error

## Documentation

In an instance method, declaring a local — `let`/`var`, function parameter,
match-pattern binding, tuple-destructure binding, for-in loop variable, or
try/otherwise error binding — whose name collides with a self-field is a
compile error (E3006).

The original code allowed this and silently rerouted every read/write of the
local to `self.field`-via-heap-pointer (because `IsSelfField` keys on name
alone). When the local's type differed from the field's type, the resulting
type confusion produced silent memory corruption — `match hlOp 'lower'
literal(_, _, valueType, _) then ...` whose `valueType` local has type
`MaxonType` clobbered `self.valueType` of type `MaxonTypeArray`, and the next
`self.valueType.count()` segfaulted dereferencing the union's tag bytes as a
pointer.

## Tests

<!-- test: match-binding-shadowing-self-field-rejected -->
### Match binding name shadowing a self field is rejected
```maxon
typealias Idx = int(0 to 1024)

export union Op
		literal(payload Idx)
		nop
end 'Op'

type Holder
		export var payload Idx

		export static function create() returns Self
				return Self{payload: 999}
		end 'create'

		function check(op Op) returns Idx
				match op 'm'
						literal(payload) then return payload
						nop then return 0
				end 'm'
		end 'check'
end 'Holder'

function main() returns ExitCode
		return Holder.create().check(Op.literal(7))
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/repro-array-union-field-reassign/match-binding-shadowing-self-field-rejected.test:18:15: local 'payload' shadows self field 'Holder.payload' — rename the local to avoid silent type confusion at every read/write keyed on the name
```

<!-- test: param-shadowing-self-field-rejected -->
### Function parameter shadowing a self field is rejected
```maxon
typealias Idx = int(0 to 1024)

type Counter
		export var count Idx

		export static function create() returns Self
				return Self{count: 0}
		end 'create'

		function bump(count Idx)
				print("{count}\n")
		end 'bump'
end 'Counter'

function main() returns ExitCode
		var c = Counter.create()
		c.bump(5)
		return 0
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/repro-array-union-field-reassign/param-shadowing-self-field-rejected.test:11:17: local 'count' shadows self field 'Counter.count' — rename the local to avoid silent type confusion at every read/write keyed on the name
```

<!-- test: let-shadowing-self-field-rejected -->
### `let` declaration shadowing a self field is rejected
```maxon
typealias Idx = int(0 to 1024)

type Holder
		export var value Idx

		export static function create() returns Self
				return Self{value: 0}
		end 'create'

		function compute() returns Idx
				let value = 42 as Idx
				return value
		end 'compute'
end 'Holder'

function main() returns ExitCode
		return Holder.create().compute()
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/repro-array-union-field-reassign/let-shadowing-self-field-rejected.test:12:9: local 'value' shadows self field 'Holder.value' — rename the local to avoid silent type confusion at every read/write keyed on the name
```

<!-- test: for-in-shadowing-self-field-rejected -->
### `for ... in` loop variable shadowing a self field is rejected
```maxon
typealias Idx = int(0 to 1024)
typealias IdxArray = Array with Idx

type Holder
		export var element Idx
		export var elements IdxArray

		export static function create() returns Self
				return Self{element: 0, elements: IdxArray.create()}
		end 'create'

		function sum() returns Idx
				var total = 0 as Idx
				for element in self.elements 'each'
						total = total + element
				end 'each'
				return total
		end 'sum'
end 'Holder'

function main() returns ExitCode
		return Holder.create().sum()
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/repro-array-union-field-reassign/for-in-shadowing-self-field-rejected.test:15:9: local 'element' shadows self field 'Holder.element' — rename the local to avoid silent type confusion at every read/write keyed on the name
```
