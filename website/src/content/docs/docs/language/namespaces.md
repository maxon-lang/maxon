---
title: Namespaces
description: Automatic namespace derivation, export and module visibility, and qualified names.
sidebar:
  order: 12
---

### Automatic Derivation
Namespaces are derived from file paths:

| File Path | Namespace |
|-----------|-----------|
| `math.maxon` | (global) |
| `utils/helpers.maxon` | `utils` |
| `stdlib/fmt/integer.maxon` | `stdlib.fmt` |

### Export Keyword

Functions, types, enums, typealiases, and top-level variables are file-scoped by default. Use the `export` keyword to make them visible to other files:

```maxon
typealias Score = int(i64.min to i64.max)

export function publicAdd(a Score, b Score) returns Score
		return a + b
end 'publicAdd'

function privateHelper(x Score) returns Score
		return x * 2
end 'privateHelper'
```

Only `publicAdd` can be called from other files.

**Exporting types and enums:**

```maxon
typealias Coord = int(i64.min to i64.max)

export type Point
	export var x as Coord
	export var y as Coord
end 'Point'

export enum Color
	red
	green
	blue
end 'Color'
```

Without `export`, types and enums are only usable within the file where they are declared.

**Exporting typealiases:**

```maxon
export typealias Score = int(0 to 100)
```

Non-exported typealiases are only visible within their file.

**Exporting top-level variables:**

```maxon
export var sharedCounter = 0
export let MAX_CONNECTIONS = 100
```

Exported variables can be read and (for `var`) modified from other files in the same project.

**Exporting methods within types:**

Individual methods can be exported independently of the type itself:

```maxon
typealias Amount = int(i64.min to i64.max)

export type Calculator
	var result as Amount

	export function add(n Amount)
		result = result + n
	end 'add'

	function internalReset()
		result = 0
	end 'internalReset'
end 'Calculator'
```

### Module Keyword (directory-scoped visibility)

`module` is a third visibility tier between file-scoped (the default) and `export`. A `module`-marked declaration is visible to every file in the **same directory** as the declaring file AND every file in **any subdirectory** of that directory — but not to files outside that subtree.

```maxon
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

`module` and `export` are mutually exclusive — combining them is a parse error. The keyword applies in every position where `export` does: top-level functions, types, enums, unions, typealiases, top-level vars/lets, and per-method or per-field modifiers inside types. A code outside the declarer's directory subtree that tries to use a `module` symbol gets error `E3088: function 'X' is module-scoped and not visible from this directory`.

In Maxon, "module" in this context means a directory subtree — useful for sharing helpers across a feature folder without leaking them to the rest of the program.

### Qualified Names
Call functions with full namespace:
```maxon
var result = stdlib.fmt.format_int(42)
```

### Suffix Matching
If unambiguous, use short name:
```maxon
var result = format_int(42)   // Finds stdlib.fmt.format_int
```

### Cross-File Bare-Name Ambiguity

When two files in different directories both expose a declaration with the same bare name and a third file references it without qualification, the compiler reports an ambiguity error and asks the user to qualify with the appropriate directory namespace.

- **Functions** — bare-name calls with multiple visible definitions trigger **E3095** (`Ambiguous bare-name call to 'X': multiple visible definitions found. Qualify with a directory name.`). Qualify the call with the directory namespace (`api.format(...)`, `lib.fmt.format(...)`).
- **Typealiases** — bare-name type references with multiple reachable definitions trigger **E3063** (`Ambiguous typealias 'X': multiple visible definitions found. Qualify with a directory name.`). Write the qualified form (`api.Score`, `lib.fmt.Score`) at the use site. Same-file duplicates remain **E3061** since qualification cannot disambiguate two declarations in the same file. File-private aliases (no modifier) are scoped to their declaring file and never contribute to cross-file ambiguity.

See `specs/typealias-collision.md` and `specs/namespaces.md` for the canonical tests.

---
