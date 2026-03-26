---
feature: duplicate-functions
status: stable
keywords: [duplicate, function, main, semantic, validation]
category: basics
---

## Documentation

### Duplicate Function Detection

The compiler detects duplicate function definitions and reports an error.

#### Same-file duplicates

If the same function name is defined more than once in a single file:

```text
error E3006: file.maxon:5:10: Duplicate function 'helper'
```

#### Multiple main functions across files

In a multi-file build, if more than one file defines a `main` function:

```text
error E3006: b.maxon:1:10: Duplicate function 'main'
```

#### No main function

Every program must have a `main` function. This is tested in the `basics` spec.

## Tests

<!-- test: error.same-file-duplicate -->
```maxon

typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
	return 1
end 'helper'

function helper() returns Integer
	return 2
end 'helper'

function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/duplicate-functions/error.same-file-duplicate.test:9:10: Duplicate function 'duplicate-functions.helper'
```

<!-- test: error.same-file-duplicate-main -->
```maxon
function main() returns ExitCode
	return 0
end 'main'

function main() returns ExitCode
	return 1
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/duplicate-functions/error.same-file-duplicate-main.test:6:10: Duplicate function 'main'
```

<!-- test: error.multi-file-duplicate-main -->
```maxon
// --- file: a.maxon
function main() returns ExitCode
	return 0
end 'main'

// --- file: b.maxon
function main() returns ExitCode
	return 1
end 'main'
```
```maxoncstderr
error E3006: b.maxon:1:10: Duplicate function 'main'
```
