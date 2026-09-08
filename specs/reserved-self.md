---
feature: reserved-self
status: experimental
keywords: [self, reserved, identifier, semantic-error]
category: language
---
# `self` is a Reserved Identifier

## Documentation

`self` is the implicit instance receiver inside an instance method. It cannot be bound by user code in any declaration form. Any attempt to declare a name `self` is rejected by the compiler.

This rule prevents silent shadowing of the receiver — a class of bug familiar from JavaScript, where `var self = this` was the standard workaround for `this`-rebinding inside callbacks.

```text
// Rejected:
let self = 42                               // E2010 (lexer rejects `self` where an identifier is required)
function configure(self String)             // E2051 (semantic: reserved identifier)
for self in items 'each'                    // E2010
```

If you need a name that suggests "this thing," pick `me`, `it`, `instance`, or any descriptive name.

## Tests

<!-- test: function-param-named-self -->
### Free-function parameter named `self` is rejected
Function parameter parsing accepts keyword-shaped tokens as names, so the rejection happens in the semantic-name check (`E2051`), not the lexer.
```maxon
function configure(self Integer) returns ExitCode
	return 0
end 'configure'

function main() returns ExitCode
	return configure(0)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2051: specs/fragments/reserved-self/function-param-named-self.test:2:20: identifier 'self' is reserved: it is the implicit instance receiver and cannot be bound by user code
```

<!-- test: let-named-self -->
### `let self = ...` is rejected
`let` requires a strict identifier, so `self` is rejected at the token level.
```maxon
function main() returns ExitCode
	let self = 42
	return 0
end 'main'
```
```maxoncstderr
error E2010: specs/fragments/reserved-self/let-named-self.test:3:6: Expected identifier but got 'self'
```

<!-- test: var-named-self -->
### `var self = ...` is rejected
```maxon
function main() returns ExitCode
	var self = 0
	return 0
end 'main'
```
```maxoncstderr
error E2010: specs/fragments/reserved-self/var-named-self.test:3:6: Expected identifier but got 'self'
```

<!-- test: for-in-named-self -->
### `for self in ...` is rejected
```maxon
function main() returns ExitCode
	let arr = [1, 2, 3]
	for self in arr 'each'
		print("hi\n")
	end 'each'
	return 0
end 'main'
```
```maxoncstderr
error E2010: specs/fragments/reserved-self/for-in-named-self.test:4:6: Expected identifier but got 'self'
```

<!-- test: enum-case-named-self-allowed -->
### An enum case named `self` IS allowed
The `self` reservation guards the value namespace (locals, params, functions,
types) where a binding could shadow the implicit receiver. Enum and union cases
live in the `TypeName.case` namespace and cannot shadow it, so a case named
`self` is accepted — exactly as the compiler's own keyword-token enum does for
the `"self"` keyword spelling.
```maxon
enum Marker
	first = 1
	self = 2
	last = 3
end 'Marker'

function main() returns ExitCode
	let m = Marker.self
	return m.rawValue as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: union-case-named-self-allowed -->
### A union case named `self` IS allowed
```maxon
typealias Tally = int(0 to 125)

union Node
	leaf(value Tally)
	self
end 'Node'

function pick(n Node) returns Tally
	return match n 'm'
		leaf(v) gives v
		self gives 7
	end 'm'
end 'pick'

function main() returns ExitCode
	return pick(Node.self)
end 'main'
```
```exitcode
7
```
