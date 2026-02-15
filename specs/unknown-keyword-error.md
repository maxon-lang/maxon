---
feature: unknown-keyword-error
status: stable
keywords: [parse-error, syntax, keyword]
category: diagnostics
---

# Unknown Keyword Error

## Documentation

Using an undefined keyword or bare identifier as a statement causes a parse error.

### Error Example

```maxon
function main() returns ExitCode
  var x = 0
  foo
    x = 5
  end 'foo'
  return x
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/unknown-keyword-error/docs-example-1.test:4:3: unexpected token: 'foo'
```


### Common Solutions

When you see this error, consider:
- Function call: `foo()` instead of `foo`
- Assignment: `foo = 5` instead of `foo`
- Check spelling of keywords: `while`, `if`, `var`, `let`, `return`, `break`

## Tests

<!-- test: bare-identifier -->
```maxon
function main() returns ExitCode
  var x = 0
  foo
    x = 5
  end 'foo'
  return x
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/unknown-keyword-error/bare-identifier.test:4:3: unexpected token: 'foo'
```

<!-- test: typo-keyword -->
```maxon
function main() returns ExitCode
  var x = 5
  retur x
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/unknown-keyword-error/typo-keyword.test:4:3: unexpected token: 'retur'
```

<!-- test: missing-call-parens -->
```maxon
function test() returns Integer
  return 42
end 'test'

function main() returns ExitCode
  test
  return 0
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/unknown-keyword-error/missing-call-parens.test:7:3: unexpected token: 'test'
```
