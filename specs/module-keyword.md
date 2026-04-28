---
feature: module-keyword
status: experimental
keywords: [module, visibility, directory, scope]
category: infrastructure
---

# Module Keyword

## Documentation

### Module Visibility

The `module` keyword is a third visibility tier between file-scoped (the default) and `export`:

- **default** (no keyword): the declaration is visible only within the file where it is defined.
- **`module`**: the declaration is visible to every file in the **same directory** AND every file in **any subdirectory** of that directory.
- **`export`**: the declaration is visible everywhere in the compilation.

`module` and `export` are mutually exclusive — a single declaration cannot use both.

In Maxon, a "module" in this context means a directory subtree. A `module` declaration is the equivalent of "package-private with subpackages included" in other languages — useful for sharing helpers across a feature folder without leaking them to the rest of the program.

```text
// project/feature/internal.maxon
module function helper() returns Integer
  return 42
end 'helper'

// project/feature/main.maxon — same directory, can call helper()
function caller() returns Integer
  return helper()
end 'caller'

// project/feature/sub/deep.maxon — subdirectory, can also call helper()
function deepCaller() returns Integer
  return helper()
end 'deepCaller'

// project/other.maxon — outside feature/, CANNOT call helper()
```

### Module Methods and Fields

Like `export`, the `module` keyword can be applied to methods and fields inside a type body:

```text
export type Counter
  module var value Integer

  module function increment()
    value = value + 1
  end 'increment'
end 'Counter'
```

The field/method is visible to other files in the same directory (and subdirectories) of the file declaring the type, but not outside that subtree.

## Tests

<!-- test: error.module-and-export-conflict -->
```maxon
typealias Integer = int(i64.min to i64.max)

export module function helper() returns Integer
	return 42
end 'helper'

function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E2001: specs/fragments/module-keyword/error.module-and-export-conflict.test:4:8: 'export' and 'module' cannot be combined
```

<!-- test: module-function-same-file -->
```maxon
typealias Integer = int(i64.min to i64.max)

module function helper() returns Integer
	return 42
end 'helper'

function main() returns ExitCode
	return helper()
end 'main'
```
```exitcode
42
```

<!-- test: module-function-same-directory -->
```maxon
// --- file: feature/helper.maxon
typealias Integer = int(i64.min to i64.max)

module function helper() returns Integer
	return 42
end 'helper'

// --- file: feature/main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```exitcode
42
```

<!-- test: module-function-subdirectory -->
```maxon
// --- file: feature/helper.maxon
typealias Integer = int(i64.min to i64.max)

module function helper() returns Integer
	return 42
end 'helper'

// --- file: feature/sub/deep.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```exitcode
42
```

<!-- test: error.module-function-different-directory -->
```maxon
// --- file: dir_a/helper.maxon
typealias Integer = int(i64.min to i64.max)

module function helper() returns Integer
	return 42
end 'helper'

// --- file: dir_b/main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E3088: dir_b/specs/fragments/module-keyword/error.module-function-different-directory.test:11:9: function 'helper' is module-scoped and not visible from this directory
```

<!-- test: error.module-function-parent-directory -->
```maxon
// --- file: feature/helper.maxon
typealias Integer = int(i64.min to i64.max)

module function helper() returns Integer
	return 42
end 'helper'

// --- file: main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E3088: specs/fragments/module-keyword/error.module-function-parent-directory.test:11:9: function 'helper' is module-scoped and not visible from this directory
```

<!-- test: module-type-same-directory -->
```maxon
// --- file: feature/point.maxon
typealias Integer = int(i64.min to i64.max)

module type Point
	module var x Integer
	module var y Integer

	module static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

// --- file: feature/main.maxon
function main() returns ExitCode
	let p = Point.create(x: 20, y: 22)
	return p.x + p.y
end 'main'
```
```exitcode
42
```

<!-- test: module-typealias-same-directory -->
```maxon
// --- file: feature/types.maxon
module typealias Score = int(0 to 100)

// --- file: feature/main.maxon
function main() returns ExitCode
	let s = Score{42}
	return s
end 'main'
```
```exitcode
42
```

<!-- test: module-enum-same-directory -->
```maxon
// --- file: feature/color.maxon
module enum Color
	red
	green
	blue
end 'Color'

// --- file: feature/main.maxon
function main() returns ExitCode
	let c = Color.blue
	match c 'check'
		blue then return 42
		red then return 0
		green then return 0
	end 'check'
end 'main'
```
```exitcode
42
```

<!-- test: module-var-same-directory -->
```maxon
// --- file: feature/state.maxon
module var counter = 42

// --- file: feature/main.maxon
function main() returns ExitCode
	return counter
end 'main'
```
```exitcode
42
```

<!-- test: module-with-nested-call -->
```maxon
// --- file: feature/inner.maxon
typealias Integer = int(i64.min to i64.max)

module function inner() returns Integer
	return 22
end 'inner'

// --- file: feature/outer.maxon
typealias Int = int(i64.min to i64.max)

module function outer() returns Int
	return inner() + 20
end 'outer'

// --- file: feature/main.maxon
function main() returns ExitCode
	return outer()
end 'main'
```
```exitcode
42
```
