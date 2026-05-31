# Maxon Programming Language

Maxon is a statically-typed programming language with a custom native x86-64 backend, designed
so that AI writes the code and humans read it — explicit, unambiguous, with no null and
constraints encoded in the type system.

## Project components

- **Compiler (`maxon`)** — the C# compiler (`maxon-sharp`), which builds to `bin/maxon.exe`. It
  has a native x86-64 backend and emits standalone PE/ELF executables with no external runtime.
  This is the compiler the toolchain currently ships with.
- **Self-hosted compiler (`maxon-selfhosted`)** — the Maxon compiler written in Maxon, built by
  the C# compiler and continuously tested for parity against it. Work in progress.
- **Language Server (LSP)** — part of the C# implementation, for IDE integration.
- **VS Code extension** — syntax highlighting and language features.

See [docs/COMPILER_ARCHITECTURE.md](docs/COMPILER_ARCHITECTURE.md) for the full pipeline.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (the C# compiler targets `net10.0`)
- Git
- Node.js 20+ (only needed to build the VS Code extension)

## Building

The C# compiler builds with `dotnet` and copies the resulting binary to `bin/maxon.exe`:

```bash
dotnet build maxon-sharp        # build the compiler -> bin/maxon.exe
```

On Windows, `buildall.bat` builds and tests the full toolchain end to end — the C# compiler,
the self-hosted compiler, the dev MCP server, and their spec tests:

```bat
buildall.bat
```

## Quick start

```bash
# build the compiler
dotnet build maxon-sharp

# compile and run an example
bin/maxon build examples/basic.maxon

# run the spec-test suite
bin/maxon spec-test
```

See [docs/CLI_REFERENCE.md](docs/CLI_REFERENCE.md) for every command and flag.

## Tests

Tests are organized as **spec files** in [`specs/`](specs/): each language feature has a single
source of truth holding its documentation and its executable test cases together (see
[docs/SPECS.md](docs/SPECS.md)). Run them with:

```bash
bin/maxon spec-test
```

When you change behavior, update or add the relevant spec and make sure the suite passes.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Licensed under either of:

- Apache License, Version 2.0 ([LICENSE-APACHE](LICENSE-APACHE) or http://www.apache.org/licenses/LICENSE-2.0)
- MIT license ([LICENSE-MIT](LICENSE-MIT) or http://opensource.org/licenses/MIT)

at your option.

### Contribution

Unless you explicitly state otherwise, any contribution intentionally submitted for inclusion in
the work by you, as defined in the Apache-2.0 license, shall be dual licensed as above, without
any additional terms or conditions.
