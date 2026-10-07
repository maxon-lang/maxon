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
file** unless marked: only that file may name them. The name of a type, enum, union or interface is still
the whole program's, so a second declaration of it anywhere is **E3006** (see
[Bare Names and Ambiguity](#bare-names-and-ambiguity)). Three modifiers widen visibility:

| Modifier | Visible to | Checked for unused exports |
|----------|------------|----------------------------|
| *(none)* | the declaring file | — |
| `module` | files in the declaring directory and its subdirectories | yes (**E3094**) |
| `export` | every file | yes (**E3092**, **E3093**) |
| `public` | every file | no |

The same modifiers apply to members inside a type. An unmarked field is private to the type: reading or
writing it anywhere else is **E3014**. An unmarked method or static member is private to the file, like a
top-level declaration: calling it from another file is **E3008**. A typealias declared inside a type body
carries its own modifier the same way: unmarked, it is private to the file, and naming it from another file —
bare or as `Holder.Inner` — is **E3008**. A typealias inside an `interface` has the interface's visibility.
At most one modifier may be written; combining two is **E2001** (`'export' and 'public' cannot be combined`).

**A type hides its members.** Where a type is not visible, nothing of it is: naming it is **E3008**
(**E3088** for a `module` type), and so is reaching a member through a value of it — a value an exported
function returns included. That covers its fields, methods, extension methods, accessors and statics, and
the calls the language makes on the author's behalf: `toString` in an interpolation, `==` and `<`, the
iteration a `for` performs, and a `match` over an enum or union value. A value held at an interface type that
is visible answers that interface's requirements. The standard library follows the same rule: a library type
or interface without `public` is hidden from a program.

**A signature may not name a type less visible than the function itself.** Whoever may call a function has to
be able to name what the call takes and gives back, so every type its parameters, its return type and its
`throws` clause name must carry at least the function's own modifier. For this rule the four tiers are ordered
*(none)* < `module` < `export` < `public`: `public` outranks `export`, so a `public` function may not name an
`export` type. The check is structural — a generic instance's base type and each of its type arguments, a
tuple's elements, and a function typealias's parameter and return types are all asked; a type parameter and a
primitive name no declaration and are asked nothing. A typealias the signature names is walked through, each
name on its right-hand side read as the alias's own file means it, and a type body's inner typealias is held
to its own modifier before its right-hand side is walked. A member is held to its own modifier, whatever its
type's, and an interface's members — its requirements' types included — are held to the interface's. A [service](/docs/language/async/#services--spawn)'s message is
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
compiles, a one-file program included. The entry point, every target a `.maxproj` file declares and every
task a `.maxtasks` file declares are exempt, because the driver calls them by name. A type an exported or `module` signature names is exempt while that function is itself
referenced from another file: the signature requires the wider tier, so dropping the modifier would only
trade E3092 for [E3167](/docs/cli/error-codes/#e3167--semanticsignaturetypelessvisiblethanfunction). Every typealias form —
ranged, function, generic-instance and tuple — is audited, and a use credits exactly the declaration it
means, a use reached only through a called function's signature included.

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

Qualification works for functions and for every kind of type name — typealiases, types, enums, unions and
interfaces (`lib.fmt.format(x)`, `50 as api.Score`, `api.Point.origin()`) — at every position a type name
is written: a declaration, a cast, a construction, a static call's base, a `throws`, `implements`, `where`
or `extends` clause, and the head of a top-level constant's initializer. It never bypasses visibility: a
type the referring file may not name is refused qualified exactly as it is bare.

Two qualifiers are reserved. `export.X` names a declaration at the project root, and `stdlib.X` one in the
standard library. A source directory that cannot be written as a qualifier is refused when the program is
loaded, **E3182**: one whose name is not an identifier, such as `my-dir`, and a top-level one named
`export`, `stdlib`, `runtime` or a keyword.

## Bare Names and Ambiguity

A bare name resolves when exactly one visible declaration has it. Only a declaration the referring file may
name is a candidate: a file-private function counts only in its own file and a `module` function only
inside its subtree, and the candidate list an error prints names only visible ones. A bare call or a bare
function value takes the type of the declaration it resolves to, and a function-backed enum case's function
is resolved from the file that declares the enum, whichever file reads the case; a case that resolves to no
single declaration is reported in that file.

**A type name** — a typealias of any form, a type, an enum, a union or an interface — that reaches more than
one project declaration is ambiguous, **E3063**, in every file, a file that declares one of them included.
The message lists the spellings that resolve it, a declaration at the project root as `export.Name`:

```text
error E3063: app/main.maxon:7:11: Ambiguous type name 'Score': more than one visible declaration matches it. Qualify it as one of: api.Score, lib.Score
```

In a file whose own declaration is file-private, the message ends `, or rename this file's own declaration`:
renaming is the remedy in the declaring file, and other files qualify.

Only project declarations take part. A project declaration of a library name is what the bare name means in
every project file, so a project's `export typealias StringArray` is the one a bare
`StringArray` names, and the library's stays reachable as `stdlib.StringArray`. The standard library's own
files see only the library's declarations. A `b"…"` literal's element is the type the file's bare `Byte`
names when that is an integer alias — the file's own, else the one project alias it sees, else the
library's; a `type Byte` leaves the literal on the library's, and an ambiguous bare `Byte` refuses the
literal with **E3063**.

**A function name** that reaches several declarations resolves to one at the project root, or in an
enclosing directory, over one in a nested directory, and to a project function over a standard-library one.
Otherwise the call is ambiguous, **E3095**, in every form it takes — plain, under `try`, or spawned with
`async` (`Ambiguous bare-name call to 'describe': more than one visible declaration matches it. Qualify it
as one of: alpha.describe, beta.describe`) — worded for a function value or an enum case's backing where
the name is one. A call (`api.format(...)`), a function value (`let f = api.format`) and a function-backed
enum case (`plain = api.format`) all accept the qualified form. A call to a generic function first keeps the
declarations its arguments fit, then the nearest of those: one in the calling file's own directory, then
one at the project root, then any other — so an overload of another arity in a farther directory serves the
calls the nearer one cannot take.

**Only a declaration the file can see counts**, for every kind of name. A project type named like a
primitive (`byte`, say) and declared where a file cannot see it leaves that name the primitive in that file.
What a call knows about its callee comes from the declaration it binds, too: whether the callee never
returns (its body ends in `panic`) or can only throw, and which generic template it instantiates.

**Every candidate is nameable.** Two type declarations of one name that can both be named from outside
their files may not share a directory: two typealiases are **E3061** and a pair involving a type, enum,
union or interface is **E3006**. Two typealiases of one name in one file are **E3061** too. The declaration
kept is the earlier one in a file, and across files the one in the file whose absolute path, with `/`
separators, sorts first byte by byte; each other one is reported at its name. Declarations in different directories
coexist — a typealias in one and a type in another included — and a file-private typealias coexists with
anything, since only its own file can name it. A type, enum, union or interface name is the whole program's:
two of them sharing a name are **E3006** wherever they sit and whatever their modifiers.

Every typealias a `public` standard-library signature names is itself `public` — an inner one such as
`Array.ElementIterator` included — so a value can always be cast to the alias a library signature asks for
(`x as ElementIndex`). A standard-library typealias with no modifier
is private to its declaring file exactly as anyone's is — `Math.maxon`'s `SeriesTermLimit` is one — and
naming it from another file is **E3008**.

## Multi-Project Workspaces

Several projects can share a workspace. Each is a directory marked by its own `.maxproj` file, and the
directory you build is the one that is compiled:

```text
workspace/
├── project-a/
│   ├── project-a.maxproj    # how project A is built
│   └── main.maxon
└── project-b/
    ├── project-b.maxproj    # how project B is built
    └── main.maxon
```

The projects sit side by side: a `.maxproj` file inside another project's tree is **E2074**. See
[Build System](/docs/language/build-system/) and [Project Structure](/docs/cli/project-structure/) for what a
project directory contains.

**The language server checks open files.** It checks a document together with the standard library, and
reads the rest of the project to learn which names its other files declare, so it reports what the build
reports about the names a buffer uses. A diagnostic that only the merged program raises is the build's to
report, and while a buffer is unsaved the name-dependent diagnostics are withheld until it matches disk
again. The build remains the authority.
