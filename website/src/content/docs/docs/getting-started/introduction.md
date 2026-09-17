---
title: Introduction
description: What Maxon is, why it exists, and what makes it legible for AI coding agents.
sidebar:
  order: 1
---

> ***You* aren't going to write it.**

That motto is the whole design philosophy. Maxon assumes the AI writes the code, so the code
has to answer for itself — the language optimizes for review, not keystrokes. Where other
languages trade clarity for keystrokes, Maxon spends the keystrokes: constraints are stated,
structure is named, and nothing is left implicit to puzzle out later.

Maxon is a compiled programming language with from-scratch native backends. It compiles
directly to standalone native executables, and to WebAssembly — no LLVM, no virtual machine,
and no external runtime.

Two things follow from the philosophy:

- **It was written by AI.** The compiler, the standard library, and this documentation were
  authored by AI coding agents. Maxon is a working demonstration of a complete toolchain —
  lexer, parser, type checker, optimizer, native code generator — built end-to-end by agents.
- **It is designed to be checked.** The same explicitness that makes code easy to review makes
  it hard to get wrong: there is no null, fallible operations must be resolved explicitly,
  numeric domain constraints live in the type system, and every block names what it closes.

## What that buys you

The features that make Maxon legible to a model also make it quick to check by hand:

- **No null.** Fallible reads use `try … otherwise`, so there is no value you can forget to
  check.
- **Ranged type aliases.** `typealias Port = int(0 to 65535)` pushes a real bound into the
  type. A constant out of range is a compile error; a value computed at run time is checked, and
  an out-of-range one stops the program with a panic instead of wrapping around.
- **Explicit block labels.** `while … 'iterate' … end 'iterate'` makes structure unambiguous —
  no counting braces to find where a block ends.
- **No silent failures and no implicit coercions.** Code says what it does.
- **Structured diagnostics.** Errors carry stable codes — the exact signal an agent uses to
  read a failure and self-correct.

## A first taste

```maxon
typealias Port = int(0 to 65535)

function main() returns ExitCode
	let port = 8080 as Port
	print("listening on {port}\n")
	return 0
end 'main'
```

Every program has a `main()` that returns an `ExitCode`. String interpolation uses `{}`. The
`port` value can only ever hold `0`–`65535`, enforced by the compiler.

## Where to go next

- [Installation](/docs/getting-started/installation/) — install Maxon with one command.
- [Your first program](/docs/getting-started/first-program/) — a guided walk-through.
- [A tour: building sort](/docs/getting-started/tour/) — a whole real program, read end to end.
- [Language Reference](/docs/language/overview/) — the complete language.
