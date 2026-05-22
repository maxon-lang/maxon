---
feature: namespaces
status: stable
keywords: [namespace, organization, scope, export]
category: organization
---

# Namespaces

## Documentation

Namespaces are derived from the file's location in the directory structure. Functions can be exported to make them available to other files.

### File-Based Namespaces

The namespace of a file is determined by its path:
- `math.maxon` in root → no namespace (global)
- `utils/helpers.maxon` → namespace `utils`
- `stdlib/fmt/integer.maxon` → namespace `stdlib.fmt`

### Export Keyword

Use `export` to make functions visible outside the file:

```maxon
typealias Score = int(i64.min to i64.max)

export function public_add(a Score, b Score) returns Score
	return a + b
end 'public_add'

function private_helper(x Score) returns Score
	return x * 2
end 'private_helper'
```
Only `public_add` can be called from other files. `private_helper` is file-private.

### Example

File: `math/operations.maxon`

```maxon
typealias Score = int(i64.min to i64.max)

function add(a Score, b Score) returns Score
	return a + b
end 'add'

function multiply(x Score, y Score) returns Score
	return x * y
end 'multiply'

function main() returns ExitCode
	return add(3, b: 4)  // Called from within same file
end 'main'
```
```exitcode
7
```


## Tests

<!-- test: basic-namespace -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(10, b: 20)
end 'main'
```
```exitcode
30
```


<!-- test: multiple-functions -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function triple(x Integer) returns Integer
	return x * 3
end 'triple'

function main() returns ExitCode
	return double(5) + triple(4)
end 'main'
```
```exitcode
22
```


<!-- test: nested-calls-in-namespace -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function sum_three(a Integer, b Integer, c Integer) returns Integer
	return add(add(a, b: b), b: c)
end 'sum_three'

function main() returns ExitCode
	return sum_three(1, b: 2, c: 3)
end 'main'
```
```exitcode
6
```


<!-- test: cross-file-bare-name-resolves -->
A bare call from `app/main.maxon` finds an exported function in a sibling directory `utils/helper.maxon` via cross-file resolution.
```maxon
// --- file: utils/helper.maxon
typealias Integer = int(i64.min to i64.max)

export function bareHelper() returns Integer
	return 42
end 'bareHelper'

// --- file: app/main.maxon
function main() returns ExitCode
	return bareHelper()
end 'main'
```
```exitcode
42
```


<!-- test: cross-file-qualified-name-resolves -->
A qualified call `utils.helper()` from `app/main.maxon` resolves to the function declared in `utils/helper.maxon`. The directory name is the module namespace.
```maxon
// --- file: utils/helper.maxon
typealias Integer = int(i64.min to i64.max)

export function qualifiedHelper() returns Integer
	return 42
end 'qualifiedHelper'

// --- file: app/main.maxon
function main() returns ExitCode
	return utils.qualifiedHelper()
end 'main'
```
```exitcode
42
```


<!-- test: same-directory-bare-name-sees-sibling -->
Two files in the same directory `utils/` share a module namespace. A function in `utils/b.maxon` calls a function in `utils/a.maxon` with no qualifier because they belong to the same module. The producer uses `module` visibility so it is visible across files inside the `utils/` subtree but not to callers outside it; the consumer (`export`ed) is the only entry point from `app/main.maxon`.
```maxon
// --- file: utils/a.maxon
typealias Integer = int(i64.min to i64.max)

module function siblingProducer() returns Integer
	return 21
end 'siblingProducer'

// --- file: utils/b.maxon
typealias Integer = int(i64.min to i64.max)

export function siblingConsumer() returns Integer
	return siblingProducer() + siblingProducer()
end 'siblingConsumer'

// --- file: app/main.maxon
function main() returns ExitCode
	return siblingConsumer()
end 'main'
```
```exitcode
42
```


<!-- test: multi-segment-namespace-resolves -->
A function declared in a nested user directory `lib/inner/leaf.maxon` is reachable via its full multi-segment qualified name `lib.inner.deepHelper`. The parser walks the dotted chain greedily and resolves against the registered function name; if the qualified callee matches `funcReturnTypes` it routes through the qualified-call path without first looking for a struct or local variable.
```maxon
// --- file: lib/inner/leaf.maxon
typealias Integer = int(i64.min to i64.max)

export function deepHelper() returns Integer
	return 42
end 'deepHelper'

// --- file: app/main.maxon
function main() returns ExitCode
	return lib.inner.deepHelper()
end 'main'
```
```exitcode
42
```


<!-- test: error.cross-file-bare-name-ambiguous -->
<!-- SelfhostedOnly -->
When two different directories both export a function with the same bare name, a third file's unqualified call is ambiguous. E3095 instructs the user to qualify the call with the appropriate directory namespace. The C# bootstrap reports an equivalent E3007 overload-ambiguity at a different point in the pipeline; this test pins the self-hosted diagnostic.
```maxon
// --- file: alpha/dup.maxon
typealias Integer = int(i64.min to i64.max)

export function duplicate() returns Integer
	return 1
end 'duplicate'

// --- file: beta/dup.maxon
typealias Integer = int(i64.min to i64.max)

export function duplicate() returns Integer
	return 2
end 'duplicate'

// --- file: app/main.maxon
function main() returns ExitCode
	return duplicate()
end 'main'
```
```maxoncstderr
error E3095: specs/fragments/namespaces/error.cross-file-bare-name-ambiguous.test:16:9: Ambiguous bare-name call to 'duplicate': multiple visible definitions found. Qualify with a directory name. Candidates: alpha.duplicate, beta.duplicate
```

