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
- **Self-hosting** — Maxon compiles Maxon, and the compiler is a fixed point of itself: build it
  with itself twice and the two binaries are byte-identical.
- **First-class tooling** — a Language Server and VS Code extension ship with the language.

## Project components

- **Compiler (`maxon-bin`)** — the Maxon compiler, written in Maxon, which builds to
  `maxon-bin/.maxon/maxon`. It has a native backend and emits standalone PE, ELF, Mach-O and
  WebAssembly executables with no external runtime.
- **Language Server (LSP)** — `maxon lsp-server`, for IDE integration.
- **VS Code extension** — syntax highlighting and language features.

## Building from source

Maxon is written in Maxon, so building it needs a Maxon compiler. There is no second implementation
to fall back on — the published release is what seeds a build.

**Prerequisites**

- Git
- Node.js 20+ (only needed to build the VS Code extension)

**Seed the build.** Download the release for your platform from
[the latest release](https://github.com/maxon-lang/maxon/releases/latest) and put its `maxon` binary
at `.bootstrap/maxon` (`.bootstrap/maxon.exe` on Windows).

> ⚠ The **binary alone**, not the unpacked archive. The compiler finds `stdlib/` by walking up from
> its own executable, so a released `stdlib/` left beside it would be compiled instead of this
> tree's — silently, and the build would succeed.

**Build and run**

```bash
./.bootstrap/maxon build                      # first build, with the seed
./maxon-bin/.maxon/maxon build                # afterwards, it rebuilds itself
maxon-bin/.maxon/maxon build examples/basic.maxon
maxon-bin/.maxon/maxon spec-test              # run the spec-test suite
```

⛔ **Run a compiler that lives INSIDE this checkout — never one installed on your PATH.** The
compiler finds `stdlib/` by walking up from its own executable, so an installed `maxon` compiles this
repository's sources against the RELEASE's standard library. Measured: it succeeds and exits 0, having
built a compiler from a library that is not this tree's.

`build` with no path compiles [`build.maxon`](build.maxon) at the root and does what it says. The
compiler in the slot can rebuild itself: it renames its own running image to `maxon.previous` first,
so a failed build leaves the slot empty rather than a stale compiler answering as though it were
current.

`scripts/fixpoint.sh` builds the compiler with itself twice and checks the two binaries are
byte-identical.

See [docs/CLI_REFERENCE.md](docs/CLI_REFERENCE.md) for every command and flag, or the
[installation guide](https://maxon.dev/install) for a step-by-step walkthrough.

## Tests

Tests are organized as **spec files** in [`specs/`](specs/): each language feature has a
single source of truth holding its documentation and its executable test cases together
(see [docs/SPECS.md](docs/SPECS.md)). Run them with:

```bash
maxon-bin/.maxon/maxon spec-test
```

When you change behavior, update or add the relevant spec and make sure the suite passes.

## History

This repository starts at v0.1.0. Everything before it — the C# bootstrap compiler that first
compiled Maxon, the first self-hosted compiler written in Maxon, and the ~5,800 commits that got
from one to the other — is archived read-only at
[maxon-lang/maxon-bootstrap](https://github.com/maxon-lang/maxon-bootstrap).

Nothing here depends on it. It is kept because a language that compiles itself has to have been
compiled by something else first, and that is worth being able to look at.

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
