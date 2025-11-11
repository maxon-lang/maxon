# Maxon Programming Language

Maxon is a statically-typed programming language with LLVM backend, designed for clear syntax and robust IDE support.

## Project Components

- **Compiler (`maxonc`)**: C++ compiler with LLVM backend
- **Language Server (LSP)**: C++ implementation for IDE integration
- **VS Code Extension**: Syntax highlighting and language features

## Building

Use the provided Makefile for all build operations:

```bash
make all              # Build compiler and LSP server
make compiler         # Build only the compiler
make lsp              # Build LSP server and extension
make extension        # Build VS Code extension
```

See the Makefile or `.github/copilot-instructions.md` for more commands.

## Quick Start

```bash
# Build everything
make all

# Compile a Maxon program
.\build\bin\maxonc.exe examples/hello-world.maxon

# Run tests
make language-tests
```

## License

Licensed under either of:

- Apache License, Version 2.0 ([LICENSE-APACHE](LICENSE-APACHE) or http://www.apache.org/licenses/LICENSE-2.0)
- MIT license ([LICENSE-MIT](LICENSE-MIT) or http://opensource.org/licenses/MIT)

at your option.

### Contribution

Unless you explicitly state otherwise, any contribution intentionally submitted for inclusion in the work by you, as defined in the Apache-2.0 license, shall be dual licensed as above, without any additional terms or conditions.
