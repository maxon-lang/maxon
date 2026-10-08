---
title: Maxon 0.5.0
description: Release notes for Maxon 0.5.0 — what changed in the compiler and standard library.
date: 2026-10-08
tags:
  - release
excerpt: Maxon 0.5.0 is out. Here's what changed.
---

**Maxon 0.5.0 is released.** Archives for every supported target are on the
[GitHub releases page](https://github.com/maxon-lang/maxon/releases/tag/v0.5.0).

## What changed

### Added

- `maxon build` skips the compile when nothing it read has changed since it last built that output,
  printing `Up to date -> <path>`; a build that does compile logs why. `--rebuild`, and the MCP `build`
  tool's `rebuild`, compile regardless. Two builds of one output take turns.
- The standard library's front-end work is cached on disk and shared between compiles, so a small
  program builds in about half the time. `maxon cache` reports the library cache and `maxon cache clear`
  removes it.
- `maxon test` takes a single `.maxtest` file and runs it alone, as does the MCP `test` tool.
- `Count`, the standard library's type for a number of things: Array, List and Vector `count()`,
  `Json.arrayLength`, `Character.byteLength()` and the string views' counts return it, and
  `Scheduler.processorCount()` compares with any of them with no cast.
- `JsonDoc.hasChild`, which says whether an object holds a key.
- `clone()` on a generic instance — a `Map`, a `Set` or a generic type of your own.
- The language server names itself and its version in its `initialize` reply.

### Changed

- A typealias's identity is its declaration rather than its spelling. Two declarations of one name are
  two types, even over the same range, and a value crossing between them needs a cast.
- A bare type name that reaches more than one visible declaration is E3063 in every file, including a
  file that declares one of them.
- A typealias declared inside a type carries its own visibility modifier, and unmarked it is private to
  its file; one inside an interface takes the interface's. Naming a file-private one from another file,
  bare or qualified, is E3008.
- E3167 checks every type a signature names, including an interface's requirements and the types a
  named alias reaches.
- Discarding a pure function's result (E3064) is caught in more programs: a function that writes only
  into values it created, one that calls a closure it can name, a search through a container's
  `withIterator()`, and a `Map` or `Set` lookup. A discarded call is judged by the overload it reached.
- Diagnostics name types and overloads as source writes them: `Array with int`, `stdlib.Name`,
  `dir.Name`, `pick(String)`.
- A program may not declare `BuiltinStringLiteral`, `BuiltinCharLiteral` or `BuiltinArrayLiteral`
  (E2015).
- Only a `.maxproj` marks a project for the language server. A file with no `.maxproj` above it, up to
  its workspace folder, is checked alone against the standard library, and nothing is loaded for it.
  Opening a `.maxproj` loads its project.
- `maxon test`'s default per-file deadline is two minutes.
- The compiler builds itself about a quarter faster, with about 30% less peak memory, and a
  function with many early exits no longer grows quadratically in code size.

### Fixed

- `Array.sort` could return unsorted output on arrays with many equal elements, and the stable sorts
  could reorder equal elements.
- Array `equals`, `hash` and `==` compared raw bytes, which is wrong for a `String` or any other managed
  element, and could disagree with each other and with `Map` and `Set` keys. They compare element by
  element through the element's own `equals` and `hash`.
- An array of floats compared, searched and hashed the wrong values on x64 and arm64, and trapped on
  `wasm32-wasi`.
- `==` on two values of a generic type compared their addresses. It calls the type's `equals`, and is
  E3005 when the type declares none.
- An array of a declared `enum Byte` with more than 256 cases truncated its values.
- On Linux, a spawned child inherited every file the program had open, so a child reading a pipe the
  program was writing could wait forever for its end.
- A program declaring its own `Comparable`, `Equatable`, `Hashable` or `Error` interface lost the
  library's `sort` and `contains` on built-in types, and its services' reply errors stopped conforming
  to `Error`; the program's own interface could not be implemented.
- A ternary choosing between a parameter typed by a function-type alias and a declared function
  crashed the compiler.
- Two conformers binding an associated type differently crashed the compiler, and an associated type
  bound by a type's second `implements` clause, or by an `extension`'s, was not found.
- A generic type nested in itself through its own instances made the compiler allocate without bound;
  it is E3184.
- Writing through a module `let`'s record handed back by a function, or copying a `let`'s field into a
  `var`, compiled and faulted at run time; they are E3159, E3019 and E3078.
- A copy that reached a promise — a `List.clone`, or a generic type copying an array of promises —
  compiled, then crashed or left a green thread un-awaited. Every copy that reaches a promise is E3141.
- A variable passed as a kept argument to a `spawn` factory aborted the program; it is moved, and a
  borrowed argument — a parameter, a field, an element — is E3138.
- A static factory's result, a static call through an instance alias, and an array literal passed at a
  type-parameter slot were refused or mistyped.
- A service reply that is a freshly interpolated `String` was refused with E3137.
- The language server kept declarations a file no longer had, reporting a false cycle after a union was
  replaced by a type of the same name.
- A `--emit-ir-runtime=` name the program lacks crashed the compiler instead of being refused.

### Removed

- `Utf8ByteCount`, `CodepointCount` and `Utf16UnitCount`, replaced by `Count`.

## Install

To install Maxon or upgrade an existing install, see [Installation](/docs/getting-started/installation/), which also
shows how to install a specific release.
