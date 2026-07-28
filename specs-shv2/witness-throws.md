---
feature: witness-throws
status: stable
keywords: [where, constraints, type-parameters, generics, interfaces, witness, throws, try]
category: type-system
---

# Throwing Witness Dispatch

## Documentation

An interface method may declare a `throws` clause. Dispatched through a witness table — the only route a
constrained type parameter has to its interface (`type Box uses T where T is Digest`) — a throwing method
uses the SAME dual-register error ABI a direct throwing call uses: the primary value in the result register
and the error flag in the error register. The call site must be written with `try`, exactly as a direct
throwing call must be; a bare `self.item.digest()` on a throwing interface method is E3057, and a `try` on a
NON-throwing one is E3055.

Conformance validates the throws relation in BOTH directions:

- an interface method that declares `throws E` requires its implementation to declare a throws clause
  (E3016) — otherwise a `try` at the dispatch would branch on a flag register the callee never wrote;
- an interface method that declares NO throws clause requires its implementation to declare none either
  (E3016) — otherwise the witness dispatch, correctly emitted as a non-throwing call, would silently drop
  the error the implementation raises.

## Tests

<!-- test: witness-throws.propagate-through-witness -->
<!-- targets: x64-windows, x64-linux -->
The throw is taken through the witness: `Point.digest` throws, `Box.itemDigest` propagates it with a bare
`try`, and `main`'s `otherwise` yields 55. A dropped error flag would return the impl's throw-path primary
(0) instead.
```maxon
typealias Code = int(0 to u32.max)

enum DigestError implements Error
	tooSmall
end 'DigestError'

interface Digest
	function digest() returns Code throws DigestError
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code throws DigestError
		if self.x < 10 'small'
			throw DigestError.tooSmall
		end 'small'
		return self.x
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemDigest() returns Code throws DigestError
		return try self.item.digest()
	end 'itemDigest'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let p = Point.create(3)
	let b = PointBox.create(p)
	let v = try b.itemDigest() otherwise 55
	return v as ExitCode
end 'main'
```
```exitcode
55
```

<!-- test: witness-throws.success-edge-through-witness -->
<!-- targets: x64-windows, x64-linux -->
The SAME program with a value that does not throw — the success edge of the throwing witness dispatch must
still carry the real result (42), so a fix to the error edge cannot be bought by breaking this one.
```maxon
typealias Code = int(0 to u32.max)

enum DigestError implements Error
	tooSmall
end 'DigestError'

interface Digest
	function digest() returns Code throws DigestError
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code throws DigestError
		if self.x < 10 'small'
			throw DigestError.tooSmall
		end 'small'
		return self.x
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemDigest() returns Code throws DigestError
		return try self.item.digest()
	end 'itemDigest'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let p = Point.create(42)
	let b = PointBox.create(p)
	let v = try b.itemDigest() otherwise 55
	return v as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: witness-throws.try-binding-inside-generic-body -->
<!-- targets: x64-windows, x64-linux -->
The `try`-BOUND spelling inside the shared generic body — `let d = try self.item.digest()`. A `try` target
reached through a `self.<field>.<method>()` chain whose field is a type parameter must resolve to the witness
dispatch, not to a further field access.
```maxon
typealias Code = int(0 to u32.max)

enum DigestError implements Error
	tooSmall
end 'DigestError'

interface Digest
	function digest() returns Code throws DigestError
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code throws DigestError
		if self.x < 10 'small'
			throw DigestError.tooSmall
		end 'small'
		return self.x
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemDigest() returns Code throws DigestError
		let d = try self.item.digest()
		return d + 1
	end 'itemDigest'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let good = PointBox.create(Point.create(20))
	let bad = PointBox.create(Point.create(3))
	let a = try good.itemDigest() otherwise 0
	let b = try bad.itemDigest() otherwise 2
	return (a + b) as ExitCode
end 'main'
```
```exitcode
23
```

<!-- test: witness-throws.otherwise-inside-generic-body -->
<!-- targets: x64-windows, x64-linux -->
The generic body CATCHES rather than propagates: `try self.item.digest() otherwise 9` inside the shared body,
so the whole error edge of the witness dispatch is confined to `Box.itemDigest`, which cannot itself throw.
```maxon
typealias Code = int(0 to u32.max)

enum DigestError implements Error
	tooSmall
end 'DigestError'

interface Digest
	function digest() returns Code throws DigestError
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code throws DigestError
		if self.x < 10 'small'
			throw DigestError.tooSmall
		end 'small'
		return self.x
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemDigest() returns Code
		return try self.item.digest() otherwise 9
	end 'itemDigest'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let good = PointBox.create(Point.create(30))
	let bad = PointBox.create(Point.create(3))
	return (good.itemDigest() + bad.itemDigest()) as ExitCode
end 'main'
```
```exitcode
39
```

<!-- test: witness-throws.managed-result-both-edges -->
<!-- targets: x64-windows, x64-linux -->
A MANAGED (`String`) witness result on both edges. The success edge owns its `+1` and drops it once at scope
exit; the error edge leaves the result register NULL and must not decref it. A leak or a double free of the
returned String is exit 101, not a wrong answer.
```maxon
typealias Count = int(0 to 1000)

enum NameError implements Error
	blank
end 'NameError'

interface Namer
	function name() returns String throws NameError
end 'Namer'

type Point implements Namer
	export var x as Count
	export static function create(x Count) returns Self
		return Self{ x: x }
	end 'create'
	export function name() returns String throws NameError
		if self.x < 10 'small'
			throw NameError.blank
		end 'small'
		return "point"
	end 'name'
end 'Point'

type Box uses T where T is Namer
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemNameLength() returns Count throws NameError
		let n = try self.item.name()
		return n.byteLength() as Count
	end 'itemNameLength'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let good = PointBox.create(Point.create(42))
	let bad = PointBox.create(Point.create(3))
	let a = try good.itemNameLength() otherwise 0
	let b = try bad.itemNameLength() otherwise 1
	return (a + b) as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: witness-throws.boxed-error-caught-at-witness-edge -->
<!-- targets: x64-windows, x64-linux -->
A PAYLOAD-CARRYING (heap-boxed) union error caught at the witness dispatch's own error edge, inside the
shared generic body. The box is handed to the caller owned, so the `(e)` binding must release it exactly
once — a leak or a double free is exit 101.
```maxon
typealias Code = int(0 to u32.max)

union CheckError implements Error
	tooSmall(limit Code)
end 'CheckError'

interface Checked
	function check() returns Code throws CheckError
end 'Checked'

type Point implements Checked
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function check() returns Code throws CheckError
		if self.x < 10 'small'
			throw CheckError.tooSmall(7)
		end 'small'
		return self.x
	end 'check'
end 'Point'

type Box uses T where T is Checked
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemCheck() returns Code
		var out = 1 as Code
		try self.item.check() otherwise (e) 'caught'
			match e 'k'
				tooSmall(limit) then out = limit + 1
			end 'k'
		end 'caught'
		return out
	end 'itemCheck'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(3))
	return b.itemCheck() as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: witness-throws.error.impl-must-throw -->
An interface method that declares `throws E` requires its implementation to declare a throws clause: a
non-throwing implementation would leave the `try` at the witness dispatch branching on a flag register the
callee never wrote (E3016).
```maxon
typealias Code = int(0 to u32.max)

enum DigestError implements Error
	tooSmall
end 'DigestError'

interface Digest
	function digest() returns Code throws DigestError
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code
		return self.x
	end 'digest'
end 'Point'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:12:6: Method 'Point.digest' must throw 'DigestError' as required by interface 'Digest'
```

<!-- test: witness-throws.error.impl-may-not-throw-beyond-interface -->
An interface method that declares NO throws clause requires its implementation to declare none either: the
witness dispatch is emitted as a NON-throwing call, so an implementation that throws would return through the
dual-register error ABI while every caller read only the primary register — silently dropping the error
(E3016).
```maxon
typealias Code = int(0 to u32.max)

enum DigestError implements Error
	tooSmall
end 'DigestError'

interface Digest
	function digest() returns Code
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code throws DigestError
		if self.x < 10 'small'
			throw DigestError.tooSmall
		end 'small'
		return self.x
	end 'digest'
end 'Point'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:12:6: Method 'Point.digest' throws 'DigestError' but interface 'Digest' declares it non-throwing — a witness dispatch of a non-throwing interface method reads no error flag, so the error would be silently dropped
```

<!-- test: witness-throws.error.bare-dispatch-needs-try -->
A throwing interface method dispatched through a witness WITHOUT `try` is E3057, exactly as a bare throwing
direct call is — the flag would be dropped and the impl's throw-path primary returned as a real answer.
```maxon
typealias Code = int(0 to u32.max)

enum DigestError implements Error
	tooSmall
end 'DigestError'

interface Digest
	function digest() returns Code throws DigestError
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code throws DigestError
		if self.x < 10 'small'
			throw DigestError.tooSmall
		end 'small'
		return self.x
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemDigest() returns Code throws DigestError
		return self.item.digest()
	end 'itemDigest'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(3))
	return (try b.itemDigest() otherwise 55) as ExitCode
end 'main'
```
```maxoncstderr
error E3057: <fragment>:31:20: throwing interface method requires try: wrap the witness dispatch as `try <receiver>.<method>(…)` — a bare call drops the error flag the method returns
```

<!-- test: witness-throws.error.try-on-nonthrowing-witness -->
A `try` on a NON-throwing interface method is E3055, exactly as a `try` on a non-throwing direct call is —
the desugar would branch on an error-flag register the callee never wrote.
```maxon
typealias Code = int(0 to u32.max)

interface Digest
	function digest() returns Code
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code
		return self.x
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemDigest() returns Code
		return try self.item.digest()
	end 'itemDigest'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(3))
	return b.itemDigest() as ExitCode
end 'main'
```
```maxoncstderr
error E3055: <fragment>:24:10: try requires a throwing function: 'digest' does not throw'
```
