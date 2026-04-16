---
feature: empty-block
status: stable
keywords: [empty, block, error, diagnostic, if, else, while, for]
category: diagnostics
---

## Documentation

### Overview

Empty blocks are a compile error. All block constructs (`if`, `else`, `while`, `for`, `try/otherwise`) must contain at least one statement. Function bodies are excluded from this rule.

### Example

```text
if condition 'check'
  // error: empty block
end 'check'
```

## Tests

<!-- test: empty-if -->
```maxon
function main() returns ExitCode
	if true 'check'
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3082: specs/fragments/empty-block/empty-if.test:4:2: empty block: 'check'
```

<!-- test: empty-else -->
```maxon
function main() returns ExitCode
	if true 'then'
		return 1
	end 'then' else 'otherwise'
	end 'otherwise'
	return 0
end 'main'
```
```maxoncstderr
error E3082: specs/fragments/empty-block/empty-else.test:6:2: empty block: 'otherwise'
```

<!-- test: empty-while -->
```maxon
function main() returns ExitCode
	var x = 5
	while x > 0 'loop'
	end 'loop'
	return 0
end 'main'
```
```maxoncstderr
error E3082: specs/fragments/empty-block/empty-while.test:5:2: empty block: 'loop'
```

<!-- test: empty-for-in -->
```maxon
function main() returns ExitCode
	let arr = [1, 2, 3]
	for item in arr 'loop'
	end 'loop'
	return 0
end 'main'
```
```maxoncstderr
error E3082: specs/fragments/empty-block/empty-for-in.test:5:2: empty block: 'loop'
```

<!-- test: empty-for-range -->
```maxon
function main() returns ExitCode
	for i in 0 to 10 'loop'
	end 'loop'
	return 0
end 'main'
```
```maxoncstderr
error E3082: specs/fragments/empty-block/empty-for-range.test:4:2: empty block: 'loop'
```

<!-- test: valid-nonempty-if -->
```maxon
function main() returns ExitCode
	if true 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: valid-empty-function -->
```maxon
function doNothing()
end 'doNothing'

function main() returns ExitCode
	doNothing()
	return 0
end 'main'
```
```exitcode
0
```
