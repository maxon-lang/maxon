---
title: Introduction
description: What Maxon is, why it exists, and what makes it legible for AI coding agents.
sidebar:
  order: 1
---

> **You aren't going to write it. You are going to read it.**

That motto is the whole design philosophy. Maxon assumes the AI writes the code and you read
it — so the language optimizes for the reader, not the typist. Where other languages trade
clarity for keystrokes, Maxon spends the keystrokes: constraints are stated, structure is
named, and nothing is left implicit to puzzle out later.

Maxon is a compiled programming language with from-scratch native backends. It compiles
directly to standalone native executables, and to WebAssembly — no LLVM, no virtual machine,
and no external runtime.

Two things follow from the philosophy:

- **It was written by AI.** The compiler, the standard library, and this documentation were
  authored by AI coding agents. Maxon is a working demonstration of a complete toolchain —
  lexer, parser, type checker, optimizer, native code generator — built end-to-end by agents.
- **It is designed to be read.** The same explicitness that makes code easy to read makes it
  hard to get wrong: there is no null, fallible operations must be resolved explicitly, numeric
  domain constraints live in the type system, and every block names what it closes.

## What that buys you

The features that make Maxon legible to a model also make it pleasant for a human:

- **No null.** Fallible reads use `try … otherwise`, so there is no value you can forget to
  check.
- **Ranged type aliases.** `typealias Port = int(0 to 65535)` pushes a real bound into the
  type. Constructing an out-of-range value is a compile error.
- **Explicit block labels.** `while … 'iterate' … end 'iterate'` makes structure unambiguous
  to read back.
- **No silent failures and no implicit coercions.** Code says what it does.
- **Structured diagnostics.** Errors carry stable codes — the exact signal an agent uses to
  read a failure and self-correct.

## A first taste

```maxon
typealias Port = int(0 to 65535)

function main() returns ExitCode
	let port = Port{8080}
	print("listening on {port}")
	return 0
end 'main'
```

Every program has a `main()` that returns an `ExitCode`. String interpolation uses `{}`. The
`port` value can only ever hold `0`–`65535`, enforced by the compiler.

## Where to go next

- [Installation](/docs/getting-started/installation/) — build the compiler and run a program.
- [Your first program](/docs/getting-started/first-program/) — a guided walk-through.
- [Language Reference](/docs/language/overview/) — the complete language.
