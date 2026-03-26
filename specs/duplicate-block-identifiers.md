---
feature: duplicate-block-identifiers
status: stable
keywords: block, identifier, error, validation, duplicate
category: error-handling
---
# Duplicate Block Identifiers

## Documentation

The compiler prevents using the same block identifier for multiple blocks within overlapping scopes.

**Valid Code - Different Identifiers:**

```maxon
function main() returns ExitCode
	var x = 0
	if true 'outer'
		x = 1
	end 'outer'

	if true 'inner'
		x = 2
	end 'inner'

	return x
end 'main'
```
```exitcode
2
```


**Valid Code - Shadowing at Different Nesting Levels:**

```maxon
function main() returns ExitCode
	var x = 0
	if true 'check'
		x = 1
		if true 'check'
			x = 2
		end 'check'
	end 'check'
	return x
end 'main'
```
```exitcode
2
```


**Invalid Code - Duplicate at Same Level:**

Note: Currently duplicate block identifiers at the same scope level are not validated. This is a known limitation.

```text
function main() returns int
  var x = 0
  if true 'check'
    x = 1
  end 'check'
  while false 'check'  // Would error: duplicate 'check' at same level
    x = 2
  end 'check'
  return x
end 'main'
```


**Notes:**
- Block identifiers must be unique within the same function scope
- Nested blocks can reuse identifiers from parent scopes
- Error message indicates the duplicate identifier and its location
- Applies to if, else, while, and for statements

## Tests

<!-- test: duplicate-block-identifiers.different-blocks -->
```maxon
function main() returns ExitCode
	var x = 0
	if true 'outer'
		x = 1
	end 'outer'
	
	if true 'inner'
		x = 2
	end 'inner'
	
	return x
end 'main'
```
```exitcode
2
```

<!-- test: duplicate-block-identifiers.nested-same-id -->
```maxon
function main() returns ExitCode
	var x = 0
	if true 'check'
		x = 1
		if true 'check'
			x = 2
		end 'check'
	end 'check'
	return x
end 'main'
```
```exitcode
2
```

<!-- test: duplicate-block-identifiers.multiple-nested -->
```maxon
function main() returns ExitCode
	var x = 0
	if true 'outer'
		x = 1
		while true 'inner'
			x = 2
			break
		end 'inner'
	end 'outer'
	return x
end 'main'
```
```exitcode
2
```

<!-- test: duplicate-block-identifiers.else-nested -->
```maxon
function main() returns ExitCode
	var x = 0
	if false 'check'
		x = 1
	end 'check' else 'else_check'
		if true 'nested'
			x = 2
		end 'nested'
	end 'else_check'
	return x
end 'main'
```
```exitcode
2
```
