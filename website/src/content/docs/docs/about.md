---
title: About
description: Who makes Maxon, and how the language came to be.
sidebar:
  order: 8
---

Maxon is created and directed by **Eric Stern**.

The split of labor is the same one the language is built around. The design is human: the
philosophy that code should be optimized for the reader, the syntax, the type system, the
decision to push constraints into types and to have no null — those are deliberate choices.
The implementation is the AI's: the compiler, the standard library, the language server, and
the documentation are written by AI coding agents working under that direction.

Maxon exists to test an idea — that if AI is going to write most of the code, a language should
be designed for that, optimizing for the reader rather than the typist — and to find out what
that actually feels like by building a complete, working toolchain on the premise.

## How it came together

Maxon began in November 2025. Through the period below, Eric was its sole developer, directing
the AI agents that wrote the code. Getting to a self-hosted language meant building a series of
bootstrap compilers first — in C++, then Zig, then C# — each one able to compile Maxon while
the language itself was still taking shape.

- **November 2025 — first light.** The project starts: lexer, parser, a working compiler, and
  a language server. An early experiment with an LLVM backend is dropped in favor of writing a
  custom native x86-64 code generator from scratch. This is the **C++ bootstrap compiler** —
  the first thing that could turn Maxon source into a native executable.
- **Late November 2025 — going native.** The C++ compiler's custom backend lands, emitting real
  PE and ELF executables with no external dependencies. A SIMD lexer and parser and the first
  optimization passes follow within days.
- **December 2025 — the standard library, and a Zig bootstrap.** The standard library fills
  out and the language server is embedded into the compiler. A second bootstrap compiler,
  written in **Zig** (`maxon-bin`), is started mid-month and becomes the main build before the
  year is out.
- **December 2025 — the self-hosted compiler begins.** In parallel, work starts on writing the
  Maxon compiler *in Maxon*. It builds for the first time within a week.
- **January 2026 — the C# bootstrap.** A third bootstrap is written in **C#**
  (`maxon-sharp`). Built from the same language spec but along an independent path, it becomes
  the reference the self-hosted compiler is continuously tested for parity against — two
  compilers agreeing on a program's behavior is the bar.
- **March 2026 — retiring Zig.** With the C# reference and the self-hosted compiler both
  advancing, the Zig bootstrap has done its job and is removed.
- **Through 2026 — toward self-hosting.** The self-hosted compiler advances feature by
  feature — real strings and interpolation, enums and unions, generics, ranges and iterators —
  steadily closing the gap with the C# reference.
- **August 2026 — the fixpoint.** The self-hosted compiler compiles itself, and the binary it
  produces compiles itself again to the *same bytes*. That byte-identical fixpoint is what makes
  self-hosting a fact rather than a claim: a compiler that is not a fixed point of itself means
  which binary you happen to hold decides what your programs become.
- **September 2026 — one compiler.** With the self-hosted compiler passing the whole suite on
  every target, the C# bootstrap and the first self-hosted source are retired and removed. What
  ships is a single compiler, written in Maxon, that builds itself — plus the standard library it
  reads. The bootstraps that got it there are archived, read-only, at
  [maxon-lang/maxon-bootstrap](https://github.com/maxon-lang/maxon-bootstrap): a language that
  compiles itself has to have been compiled by something else first, and that is worth being able
  to look at.
- **v0.1.0 — the first release.** Binaries for Windows, macOS and Linux on x64 and arm64, each
  built and tested on the architecture it ships for.

## Open source

The project is open source under MIT and Apache-2.0, and contributions are
[welcome](/docs/contributing/). You can find the source and reach the author on
[GitHub](https://github.com/Stormalong), or email Eric at
[stormalong@gmail.com](mailto:stormalong@gmail.com).
