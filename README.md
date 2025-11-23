# Maxon Programming Language

Maxon is a statically-typed programming language with LLVM backend, designed for clear syntax and robust IDE support.

## Project Components

- **Compiler (`maxon`)**: C++ compiler with LLVM backend
- **Language Server (LSP)**: C++ implementation for IDE integration
- **VS Code Extension**: Syntax highlighting and language features

## Prerequisites

### Windows
- Git for Windows (Git Bash required)
- CMake 3.13+
- Ninja build system

### Linux
- Use the provided dev container (recommended)
- Or install: build-essential, cmake, ninja-build, .NET 8.0 SDK, Node.js 20+

## Building

The build system automatically downloads and configures LLVM. Simply run:

```bash
make all              # Downloads LLVM (first time), builds everything
make compiler         # Build only the compiler
make lsp              # Build LSP server and extension
make extension        # Build VS Code extension
make test             # Run all tests
make clean            # Clean build artifacts (keeps LLVM)
make clean-llvm       # Remove LLVM download
```

**Note:** All commands must be run in Git Bash on Windows or bash on Linux.

## Quick Start

### Windows (Git Bash)
```bash
# Build everything (downloads LLVM automatically)
make all

# Compile a Maxon program
./bin/maxon examples/hello-world.maxon

# Run tests
make test
```

### Linux (Dev Container)
```bash
# Open project in VS Code with dev container

# Build everything (LLVM downloaded automatically on first build)
make all

# Compile a Maxon program
./bin/maxon examples/hello-world.maxon

# Run tests
make test
```

## Development with VS Code

### Windows
1. Install Git for Windows (includes Git Bash)
2. Install required extensions
3. Run all Make commands in Git Bash terminal

### Linux
1. Open folder in container (VS Code will prompt)
2. Container automatically sets up environment
3. Run Make commands in integrated terminal

See [docs/CROSS_PLATFORM_PLAN.md](docs/CROSS_PLATFORM_PLAN.md) for detailed cross-platform setup information.

## License

Licensed under either of:

- Apache License, Version 2.0 ([LICENSE-APACHE](LICENSE-APACHE) or http://www.apache.org/licenses/LICENSE-2.0)
- MIT license ([LICENSE-MIT](LICENSE-MIT) or http://opensource.org/licenses/MIT)

at your option.

### Contribution

Unless you explicitly state otherwise, any contribution intentionally submitted for inclusion in the work by you, as defined in the Apache-2.0 license, shall be dual licensed as above, without any additional terms or conditions.
