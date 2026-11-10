---
feature: duplicate-block-identifiers
status: stable
keywords: block, identifier, error, validation, duplicate
category: error-handling
---
# Duplicate Block Identifiers

## Documentation

A block may not reuse the label of a block that encloses it, nor the name of the function, method or type it sits
in (**E2081**), so an `end` names exactly one open block. Blocks that do not enclose one another may share a label.

**Valid Code - Different Identifiers:**

```maxon
function main() returns ExitCode
	var x = 0 as ExitCode
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


**Valid Code - Sibling Blocks Sharing a Label:**

```maxon
function main() returns ExitCode
	var x = 0 as ExitCode
	if true 'check'
		x = 1
	end 'check'

	while x < 3 'check'
		x = x + 1
	end 'check'

	return x
end 'main'
```
```exitcode
3
```


**Invalid Code - A Nested Block Reusing an Enclosing Label:**

```text
function main() returns ExitCode
	var x = 0 as ExitCode
	if true 'check'
		x = 1
		if true 'check'  // error E2081: 'check' is already the label of the enclosing block
			x = 2
		end 'check'
	end 'check'
	return x
end 'main'
```


**Notes:**
- The label of an enclosing block, and the name of the enclosing function, method or type, cannot be reused by a nested block
- Blocks that do not enclose one another, such as the arms of an if-else chain or two loops in a row, may share a label
- The error message names both labels' positions
- Applies to if, else, while, for, match, try and otherwise blocks

## Tests

<!-- test: duplicate-block-identifiers.different-blocks -->
```maxon
function main() returns ExitCode
	var x = 0 as ExitCode
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
	var x = 0 as ExitCode
	if true 'check'
		x = 1
		if true 'check'
			x = 2
		end 'check'
	end 'check'
	return x
end 'main'
```
```maxoncstderr
error E2081: <fragment>:6:11: Block label 'check' is already the label of an enclosing block or declaration (line 4, column 10); an `end 'check'` could close either, so rename the inner block's label
```

<!-- test: duplicate-block-identifiers.sibling-same-id -->
```maxon
function main() returns ExitCode
	var x = 0 as ExitCode
	if true 'check'
		x = 1
	end 'check'

	while x < 3 'check'
		x = x + 1
	end 'check'

	return x
end 'main'
```
```exitcode
3
```

<!-- test: duplicate-block-identifiers.multiple-nested -->
```maxon
function main() returns ExitCode
	var x = 0 as ExitCode
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
	var x = 0 as ExitCode
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
