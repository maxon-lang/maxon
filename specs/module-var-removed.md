---
feature: module-var-removed
status: stable
keywords: [var, let, module, static, global, grammar]
category: language
---
# A module-level `var` and a `static var` do not exist

## Documentation

A top-level binding and a `static` member are written `let`. The grammar has no `var` at file scope and none after
`static`, so either spelling fails exactly as any stray word in that position does: the parser's own error, at the `var`
token, with no dedicated diagnostic. Mutable state is a local `var`, a `var` field of a record, a `SharedValue`, or a field of
a service; a counter or flag a helper updates is a local `var` of the caller passed to a `mutable` parameter
(`specs/pass-by-reference.md`).

A `var` stays legal as a local, as a field of a `type`, and as a parameter marker's target.

## Tests

<!-- test: error.a-module-var-is-refused -->
A module-level `var` is the parser's generic error at the `var` token.
```maxon
var counter = 0 as FullRangeInt

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:1: Expected function declaration, got 'var'
```

<!-- test: error.an-exported-module-var-is-refused -->
The `export` modifier changes nothing: the word after it is still not a declaration.
```maxon
export var globalState = false

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:8: Expected function declaration, got 'var'
```

<!-- test: error.a-static-var-is-refused -->
After `static` a type body takes a function or a `let`, so a `static var` is refused at the `var` token.
```maxon
type Counter
	static var count = 0 as FullRangeInt
end 'Counter'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:3:9: Expected 'function' but got 'var'
```

<!-- test: a-module-let-and-a-static-let-stay-legal -->
The control: a module-level `let` and a `static let` are the file-scope and type-scope bindings.
```maxon
let limit = 40

type Config
	static let extra = 2
end 'Config'

function main() returns ExitCode
	return limit + Config.extra
end 'main'
```
```exitcode
42
```

<!-- test: a-local-var-and-a-var-field-stay-legal -->
The control for the other half: `var` as a local and as a record field is untouched.
```maxon
type Cell
	export var n as FullRangeInt

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Cell'

function main() returns ExitCode
	var total = 0 as FullRangeInt
	let cell = Cell.create()
	cell.n = 40
	total = total + cell.n + 2
	return total
end 'main'
```
```exitcode
42
```
