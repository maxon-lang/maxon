---
feature: expected-expression-error
status: stable
keywords: [parse-error, expression, error-op]
category: diagnostics
---

# Expected-Expression Error

## Documentation

When the parser reaches a position where it expects an expression and finds a
token that cannot start one (a stray operator, a closing bracket, etc.), it
reports an `E2004: Expected expression but got '<token>'` diagnostic and emits
a `MaxonOp.error` op at the failed span so the future LSP can anchor a
diagnostic squiggle on the exact tokens.

### Error Example

```maxon
function main() returns ExitCode
	return * 1
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/expected-expression-error/docs-example-1.test:3:9: Expected expression but got '*'
```

### Notes

- The parser skips to the next newline after the bad token so a single
  malformed expression doesn't cascade into a flood of follow-on errors.
- Batch compile fails at `hasErrors(project)` as soon as any such diagnostic
  is reported; the surfaced message comes from `reportCompileError` before
  the pipeline even reaches `LowerMaxonToStd`.
- `LowerMaxonToStd` quietly drops `MaxonOp.error` ops it encounters, since
  the diagnostic has already been recorded. Future LSP queries will consume
  the ops directly from the Maxon IR and never run lowering.

## Tests

<!-- test: return-operator-without-operand -->
```maxon
function main() returns ExitCode
	return * 1
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/expected-expression-error/return-operator-without-operand.test:3:9: Expected expression but got '*'
```

<!-- test: let-rhs-missing -->
```maxon
function main() returns ExitCode
	let x =
	return x
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/expected-expression-error/let-rhs-missing.test:3:9: Expected expression but got '(empty)'
```
