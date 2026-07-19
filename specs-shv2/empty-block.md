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

<!-- disabled-test: empty-if -->
<!-- E3082 empty-block diagnostic (shv2 accepts an empty block) -->
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

<!-- disabled-test: empty-else -->
<!-- E3082 empty-block diagnostic (shv2 accepts an empty block) -->
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

<!-- disabled-test: empty-while -->
<!-- E3082 empty-block diagnostic (shv2 accepts an empty block) -->
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

<!-- disabled-test: empty-for-in -->
<!-- P1.7 Array + P1.8 for-in (and E3082) -->
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

<!-- disabled-test: empty-for-range -->
<!-- P1.8 for-in over a range (and E3082) -->
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
<!-- targets: wasm32-wasi -->
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
<!-- targets: wasm32-wasi -->
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
