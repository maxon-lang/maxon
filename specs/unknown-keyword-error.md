---
feature: unknown-keyword-error
status: stable
keywords: [parse-error, syntax, keyword]
category: diagnostics
---

# Unknown Keyword Error

## Developer Notes

The parser reports an error when it encounters an unexpected identifier that isn't a valid keyword or statement.

Implementation:
- Detected in `Parser::parseStatement()`
- When an identifier doesn't match known keywords (if, while, var, let, return, etc.)
- And isn't followed by `=` (assignment) or `(` (function call)
- Reports helpful error message suggesting what might be wrong
- Compilation halts with parse error

Common causes:
- Typo in keyword name
- Forgetting assignment operator
- Forgetting function call parentheses
- Using identifier as statement

## Documentation

Using an undefined keyword or bare identifier as a statement causes a parse error.

### Error Example

```maxon
function main() returns int
    var x = 0
    foo
        x = 5
    end 'foo'
    return x
end 'main'
```
```maxoncstderr
In file 'temp_fragment.maxon':
Unexpected identifier 'foo'
  Note: Did you forget an assignment (=), function call (), or keyword?
  Location: line 4, column 5
```


### Common Solutions

When you see this error, consider:
- Function call: `foo()` instead of `foo`
- Assignment: `foo = 5` instead of `foo`
- Check spelling of keywords: `while`, `if`, `var`, `let`, `return`, `break`

## Tests

<!-- test: bare-identifier -->
```maxon
function main() returns int
    var x = 0
    foo
        x = 5
    end 'foo'
    return x
end 'main'
```
```maxoncstderr
In file 'temp_fragment.maxon':
Unexpected identifier 'foo'
  Note: Did you forget an assignment (=), function call (), or keyword?
  Location: line 4, column 5
```

<!-- test: typo-keyword -->
```maxon
function main() returns int
    var x = 5
    retur x
end 'main'
```
```maxoncstderr
In file 'temp_fragment.maxon':
Unexpected identifier 'retur'
  Note: Did you forget an assignment (=), function call (), or keyword?
  Location: line 4, column 5
```

<!-- test: missing-call-parens -->
```maxon
function test() returns int
    return 42
end 'test'

function main() returns int
    test
    return 0
end 'main'
```
```maxoncstderr
In file 'temp_fragment.maxon':
Unexpected identifier 'test'
  Note: Did you forget an assignment (=), function call (), or keyword?
  Location: line 7, column 5
```
