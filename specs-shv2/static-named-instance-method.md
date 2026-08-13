---
feature: static-named-instance-method
status: experimental
keywords: [self, receiver, static, instance, method, argument, type-check]
category: type-system
---

# The receiver argument of a STATICALLY-NAMED instance method is type-checked

## Documentation

`Type.method(…)` names a STATIC method (`specs/static-methods.md`), but nothing refuses the spelling
when `method` is an INSTANCE method: the call is built with no receiver, so the author's first written
argument lands in the slot the receiver would have occupied — parameter 0, `self`. Both reference
compilers accept that arrangement when the argument really is a receiver of the right type
(`Adder.bump(a)` is `a.bump()`, MEASURED at exit 42 on the C# bootstrap), so the form is not refused.

**What must be refused is the argument whose type is wrong.** `Adder.bump(7)` passes an `int` where a
record pointer is expected, and the callee dereferences it. Before this file existed the receiver
argument was skipped by the argument type check outright, on the premise that a receiver is *compatible
by construction* — true of `a.bump()`, where the callee was mangled FROM the receiver's own type, and
false of every statically-named call, where the "receiver" is a value the author chose. All three cases
below COMPILED CLEAN and faulted with an access violation at run time.

The premise the skip rested on is preserved rather than discarded: at an ordinary method call the
receiver's type and its `self` parameter's still have to be judged compatible, and for a call through a
concrete generic instance they are spelled differently — the receiver is tagged with the instance while
the shared body's `self` is the base. `paramTypeThroughCallInstance` already resolves exactly that
difference for every OTHER parameter, and the receiver is now simply one more parameter it resolves;
the cases pinning an ordinary call are here so a check that red-flags one goes red.

## Tests

<!-- test: error.a-plain-user-types-receiver-is-checked -->
### A plain user type: an `int` where the receiver record is expected
```maxon
typealias Num = int(0 to 1000)

type Adder
	var value as Num

	static function create(v Num) returns Adder
		return Adder{value: v}
	end 'create'

	function bump() returns Num
		return self.value + 1
	end 'bump'
end 'Adder'

function main() returns ExitCode
	return Adder.bump(7)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:17:15: argument type mismatch for 'self': expected 'Adder', got 'int'
```

<!-- test: error.a-builtin-receiver-is-checked -->
### A builtin receiver: `String`'s own method, named statically
```maxon
function main() returns ExitCode
	return String.byteLength(3)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:16: argument type mismatch for 'self': expected 'String', got 'int'
```

<!-- test: error.a-corpus-receiver-is-checked -->
### A corpus receiver: `Character`'s method, named statically
```maxon
function main() returns ExitCode
	return Character.hash(3)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:19: argument type mismatch for 'self': expected 'Character', got 'int'
```

<!-- test: a-correctly-typed-receiver-argument-still-runs -->
### The same spelling with a real receiver is the method call, and still runs
```maxon
typealias Num = int(0 to 1000)

type Adder
	var value as Num

	static function create(v Num) returns Adder
		return Adder{value: v}
	end 'create'

	function bump() returns Num
		return self.value + 1
	end 'bump'
end 'Adder'

function main() returns ExitCode
	let a = Adder.create(41)
	return Adder.bump(a)
end 'main'
```
```exitcode
42
```

<!-- test: an-ordinary-method-call-through-a-generic-instance-is-unaffected -->
### The receiver a call MINTS is still compatible by construction
```maxon
typealias Integer = int(i64.min to i64.max)
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function get() returns T
		return self.value
	end 'get'
end 'Box'
typealias IntBox = Box with Integer

function main() returns ExitCode
	let b = IntBox.create(42)
	return b.get()
end 'main'
```
```exitcode
42
```
