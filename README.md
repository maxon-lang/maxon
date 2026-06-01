# Maxon

> ### *You* Aren't Going To Write It.
> Maxon makes a bet: the AI writes the code, and you read it.

Maxon is a statically-typed, compiled programming language with a from-scratch native
**x86-64 backend** — **written by AI, for AI**. The compiler, standard library, and
documentation were all authored by AI coding agents.

Because the AI writes the code and a human reads it, Maxon optimizes for the **reader, not
the typist**. Where another language is terse, Maxon is explicit: every constraint is
stated, every block is named, nothing is implicit to puzzle out later. The same
explicitness that makes it easy to read makes it hard to get wrong — exactly the property
you want when a model is generating the code.

**[maxon.dev](https://maxon.dev)**&nbsp;&nbsp;·&nbsp;&nbsp;[Documentation](https://maxon.dev/docs)&nbsp;&nbsp;·&nbsp;&nbsp;[Examples](https://maxon.dev/examples)&nbsp;&nbsp;·&nbsp;&nbsp;[Discussions](https://github.com/maxon-lang/maxon/discussions)

```maxon
function main() returns ExitCode
    let port = Port{8080}
    print("listening on {port}")
    return 0
end 'main'
```

## What makes it legible for AI

**Constraints live in the type system.** Domain bounds are part of the type, not a comment —
so an agent can't silently construct an invalid value.

```maxon
typealias Port = int(0 to 65535)
let port = Port{8080}      // Port{70000} is a compile error
```

**No null to forget.** Fallible reads must be resolved explicitly with `try … otherwise`.
There's no value to forget to check, so "missing null check" bugs can't occur.

```maxon
let value = try inputVector.get(col) otherwise 0.0
```

**Unambiguous structure.** Every block names what it closes, so structure reads back
clearly — for a human or a model.

```maxon
while iteration < 10 'iterate'
    applyAtA(previous, inputVector: current, dimension: size)
end 'iterate'
```

## A serious language underneath

The AI story only holds up because the compiler does. Maxon is a real, fast,
statically-typed, compiled language:

- **Native x86-64 backend** — compiles straight to PE and ELF executables. No LLVM, no VM,
  no external runtime.
- **Reference-counted memory** — deterministic cleanup the moment a value is no longer
  referenced. No garbage collector, no pauses.
- **Strong inference + ranged types** — static typing that stays terse, with ranged type
  aliases that encode real domain bounds into the types themselves.
- **Structured diagnostics** — stable error codes; the signal an agent uses to read a
  failure and correct itself.
- **Self-hosting** — Maxon compiles Maxon, continuously tested for parity against the
  bootstrap compiler (work in progress).
- **First-class tooling** — a Language Server and VS Code extension ship with the language.

See [docs/COMPILER_ARCHITECTURE.md](docs/COMPILER_ARCHITECTURE.md) for the full pipeline.

## Project components

- **Compiler (`maxon-sharp`)** — the C# bootstrap compiler, which builds to `bin/maxon.exe`.
  It has a native x86-64 backend and emits standalone PE/ELF executables with no external
  runtime. This is the compiler the toolchain currently ships with.
- **Self-hosted compiler (`maxon-selfhosted`)** — the Maxon compiler written in Maxon, built
  by the C# compiler and continuously tested for parity against it. Work in progress.
- **Language Server (LSP)** — part of the C# implementation, for IDE integration.
- **VS Code extension** — syntax highlighting and language features.

## Building from source

**Prerequisites**

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (the C# compiler targets `net10.0`)
- Git
- Node.js 20+ (only needed to build the VS Code extension)

**Build and run**

```bash
dotnet build maxon-sharp               # build the compiler -> bin/maxon.exe
bin/maxon build examples/basic.maxon   # compile and run an example
bin/maxon spec-test                    # run the spec-test suite
```

On Windows, `buildall.bat` builds and tests the full toolchain end to end — the C# compiler,
the self-hosted compiler, the dev MCP server, and their spec tests.

See [docs/CLI_REFERENCE.md](docs/CLI_REFERENCE.md) for every command and flag, or the
[installation guide](https://maxon.dev/install) for a step-by-step walkthrough.

## Tests

Tests are organized as **spec files** in [`specs/`](specs/): each language feature has a
single source of truth holding its documentation and its executable test cases together
(see [docs/SPECS.md](docs/SPECS.md)). Run them with:

```bash
bin/maxon spec-test
```

When you change behavior, update or add the relevant spec and make sure the suite passes.

## Contributing

Maxon is written by AI and built in the open. Bug reports, patches, clearer docs, and real
programs written in Maxon are all welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md),
join the [discussions](https://github.com/maxon-lang/maxon/discussions), or read the
[contributing guide](https://maxon.dev/docs/contributing) on the site.

## License

Licensed under either of

- Apache License, Version 2.0 ([LICENSE-APACHE](LICENSE-APACHE) or <http://www.apache.org/licenses/LICENSE-2.0>)
- MIT license ([LICENSE-MIT](LICENSE-MIT) or <http://opensource.org/licenses/MIT>)

at your option.

Unless you explicitly state otherwise, any contribution intentionally submitted for
inclusion in the work by you, as defined in the Apache-2.0 license, shall be dual licensed
as above, without any additional terms or conditions.
