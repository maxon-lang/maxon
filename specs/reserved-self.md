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
function configure(self int) returns ExitCode
	return 0
end 'configure'

function main() returns ExitCode
	return configure(0)
end 'main'
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
