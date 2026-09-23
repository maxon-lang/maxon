---
title: Namespaces
description: Automatic namespace derivation, export and module visibility, and qualified names.
sidebar:
  order: 12
---

A Maxon program is every `.maxon` file in a project directory, compiled together. There are no import
statements: a file sees its own declarations, the declarations other files make visible, and the standard
library.

## Automatic Derivation

A file's namespace is its directory path relative to the project root, joined with `.`:

| File | Namespace |
|------|-----------|
| `main.maxon` | (none) |
| `utils/helpers.maxon` | `utils` |
| `lib/fmt/integer.maxon` | `lib.fmt` |

Every file in one directory shares that directory's namespace.

## Visibility

Top-level declarations — functions, types, enums, unions, typealiases, variables — are **private to their
file** unless marked. Three modifiers widen that:

| Modifier | Visible to | Checked for unused exports |
|----------|------------|----------------------------|
| *(none)* | the declaring file | — |
| `module` | files in the declaring directory and its subdirectories | yes (**E3094**) |
| `export` | every file | yes (**E3092**, **E3093**) |
| `public` | every file | no |

The same modifiers apply to members inside a type: fields, methods and static members are private to the
type unless marked, independently of the type's own visibility. At most one modifier may be written;
combining two is **E2001** (`'export' and 'public' cannot be combined`).

**A signature may not name a type less visible than the function itself.** Whoever may call a function has to
be able to name what the call takes and gives back, so every type its parameters, its return type and its
`throws` clause name must carry at least the function's own modifier. For this rule the four tiers are ordered
*(none)* < `module` < `export` < `public`: `public` outranks `export`, so a `public` function may not name an
`export` type. The check is structural — a generic instance's base type and each of its type arguments, a
tuple's elements, and a function typealias's parameter and return types are all asked; a type parameter and a
primitive name no declaration and are asked nothing. A member is held to its own modifier rather than its
type's, and an interface's members are held to the interface's. A [service](/docs/language/async/#services--spawn)'s message is
held to the narrower of its own modifier and its service type's. Naming a narrower type is
[E3167](/docs/cli/error-codes/#e3167--semanticsignaturetypelessvisiblethanfunction); the fix is to raise the type to the function's
tier, or to narrow the function.

## `export`

```maxon
export typealias Score = int(i64.min to i64.max)

export function publicAdd(a Score, b Score) returns Score
	return a + b
end 'publicAdd'

function privateHelper(x Score) returns Score     // file-private
	return x * 2
end 'privateHelper'
```

Calling a non-exported function from another file is **E3008** (`function 'privateHelper' is not
exported`).

`export` also states an expectation: **this program uses the declaration from another file.** When
nothing outside the declaring file refers to it, the compiler reports **E3092** (`exported function
'geometry.perimeter' is never referenced outside its declaring file`), and when every use is inside the
declaring directory it suggests `module` (**E3093**). These checks run on every program that otherwise
compiles, a one-file program included. The entry point and every task a `tasks.maxon` declares are
exempt, because nothing in the source calls them. A type an exported or `module` signature names is exempt while that function is itself
referenced from another file: the signature requires the wider tier, so dropping the modifier would only
trade E3092 for [E3167](/docs/cli/error-codes/#e3167--semanticsignaturetypelessvisiblethanfunction).

## `public`

`public` gives exactly the visibility of `export` and declares the symbol **API surface**: something that
exists for callers outside this program, so "nothing here uses it" is not a finding. Library code marks its
surface `public`; the standard library does so throughout.

```maxon
public typealias Length = int(0 to 1000)

public function area(width Length, height Length) returns Length
	return width * height
end 'area'
```

`export` and `public` are reserved words. `module` is contextual: it is a modifier only directly before a
declaration and remains usable as an ordinary name.

## `module`

A `module` declaration is visible to every file in the declaring file's directory and in its subdirectories,
and to nothing outside that subtree — for helpers shared across a feature folder:

```text
project/
├── main.maxon                 # cannot call helper()
└── feature/
    ├── api.maxon              # module function helper() — declared here
    ├── routes.maxon           # can call helper()
    └── internal/
        └── cache.maxon        # can call helper()
```

Using a `module` declaration from outside its subtree is **E3088** (`function 'helper' is module-scoped
and not visible from this directory`). A `module` declaration that nothing outside its file uses is
**E3094**.

## Qualified Names

Qualify a name with its namespace to be explicit, or to choose between two declarations with the same
name:

```maxon
function main() returns ExitCode
	let a = geometry.area(3, height: 4)
	let side = 7 as geometry.Length
	print("{a} {side}\n")
	return 0
end 'main'
```

Qualification works for functions and typealiases (`lib.fmt.format(x)`, `50 as api.Score`). It never
bypasses visibility. Types are referred to by their bare name.

## Bare Names and Ambiguity

A bare name resolves when exactly one visible declaration has it. Only a declaration the referring file may
name is a candidate: a file-private function in another file and a `module` function outside the caller's
subtree never count, and the candidate list an error prints names only visible ones. A bare call or a bare
function value takes the type of the declaration it resolves to, and a function-backed enum case's function
is resolved from the file that declares the enum, whichever file reads the case. When several do:

- a declaration at the project root, or in an enclosing directory, takes precedence over one in a nested
  directory, and a project declaration takes precedence over a standard-library one;
- otherwise the reference is ambiguous. A function call is **E3095** (`Ambiguous bare-name call to 'describe':
  multiple visible definitions found. Qualify with a directory name. Candidates: alpha.describe,
  beta.describe`), worded for a function value or an enum case's backing where the name is one, and a
  typealias is **E3063** — in every alias form, including `export typealias Step = function(…) returns …`.
  Qualify the name to resolve it — a call (`api.format(...)`), a function value
  (`let f = api.format`) and a function-backed enum case (`plain = api.format`) all accept the qualified form.

Two typealiases with the same name in **one** file are **E3061**, which qualification cannot resolve.

Every typealias a `public` standard-library signature names is itself `public`, so a value can always be cast
to the alias a library signature asks for (`x as AssertedInt`). A standard-library typealias with no modifier
is private to its declaring file exactly as anyone's is — `Math.maxon`'s `SeriesTermLimit` is one — and
naming it from another file is **E2003**.

## Multi-Project Workspaces

Several projects can share a workspace. Each is a directory, and the directory you build is the one that is
compiled:

```text
workspace/
├── project-a/
│   ├── project.maxon    # how project A is built
│   └── main.maxon
└── project-b/
    ├── project.maxon    # how project B is built
    └── main.maxon
```

See [Build System](/docs/language/build-system/) and [Project Structure](/docs/cli/project-structure/) for
what a project directory contains.

**The language server checks open files.** It checks a document together with the standard library, not with the sibling files a
`maxon build` of its directory would compile. It does read the rest of the project to find out which names
those files declare, so it no longer reports an error the build does not — but it can still miss one: a
diagnostic that only the merged program raises is out of reach, and while a buffer is unsaved the
name-dependent diagnostics are withheld until it matches disk again. The build remains the authority.
