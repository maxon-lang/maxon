# Contributing to Maxon

Maxon is free and open source, dual-licensed under
[MIT](LICENSE-MIT) and [Apache-2.0](LICENSE-APACHE). It was written by AI, but it's built in
the open — and contributions are welcome, whether that's a bug report, a patch to the compiler,
a documentation fix, or a real program written in the language.

The project's guiding philosophy is **"You aren't going to write it. You are going to read
it."** The AI writes the code; humans read it. So Maxon favors explicit, readable code over
clever or terse code — and that applies to contributions, in the compiler and standard library
as much as in the language itself.

## Ways to contribute

### Report a bug

Found a miscompile, a crash, a confusing diagnostic, or a place where the docs and the compiler
disagree? Open an issue. A good report includes:

- A minimal `.maxon` program that reproduces the problem.
- What you expected to happen, and what actually happened (exact output or exit code).
- Your platform (Windows / Linux / macOS) and how you built the compiler.

The smaller the reproduction, the faster it can be fixed.

### Improve the compiler or standard library

The compiler (a native x86-64 backend), the language server, and the standard library are all
open. Patches that fix bugs, improve diagnostics, or extend the standard library are welcome.
For anything non-trivial, open an issue first so the approach can be discussed before you invest
the work, then send a pull request.

### Improve the docs and examples

Documentation and examples are high-leverage contributions — a language designed to be *read*
lives or dies on how well it's explained. Typo fixes, clearer explanations, and new example
programs in [`examples/`](examples/) are all valuable. Examples should be real, compilable
`.maxon` files.

### Write something in Maxon

The most useful thing you can do is build something real. Writing actual programs surfaces the
rough edges no test suite will, and shapes where the language goes next. Share what you build,
and report what felt awkward, what was missing, and what worked.

## Building from source

Maxon is written in Maxon, so building it needs a Maxon compiler. There is no second implementation
in this repo to fall back on — the published release is what seeds a build.

### Prerequisites

- Git (on Windows, **Git for Windows**, which includes Git Bash)
- Node.js 20+, only if you are building the VS Code extension

> The build scripts are bash. Run them in **Git Bash** on Windows, or bash on Linux and macOS — not
> PowerShell or cmd.

### Build and test

Download the release for your platform from
[the latest release](https://github.com/maxon-lang/maxon/releases/latest) and put its `maxon` binary
at `.bootstrap/maxon` (`.bootstrap/maxon.exe` on Windows) — the **binary alone**, not the unpacked
archive, because the compiler resolves `stdlib/` by walking up from its own executable and a released
one left beside it would be compiled in place of this tree's.

```bash
scripts/build.sh              # build the compiler with the seed, into maxon-bin/.maxon/
maxon-bin/.maxon/maxon spec-test
```

`scripts/build.sh` prefers the compiler already in `maxon-bin/.maxon/`, so once you have one the seed
is no longer consulted and the ordinary edit-build loop is `scripts/build.sh` alone. `scripts/fixpoint.sh` builds the compiler with
itself twice and checks that the two binaries are byte-identical.

Compile and run a program with the freshly built compiler:

```bash
maxon-bin/.maxon/maxon build examples/basic.maxon
./examples/basic.exe
```

See [docs/CLI_REFERENCE.md](docs/CLI_REFERENCE.md) for every command and flag, and
[docs/RELEASING.md](docs/RELEASING.md) for how a release is built and published.

## Tests

Maxon's tests are organized as **spec files**. Each language feature has a single source of
truth in the [`specs/`](specs/) directory that holds the feature's documentation *and* its
executable test cases together (see [`docs/SPECS.md`](docs/SPECS.md) for the format). When you
change behavior:

- Update or add the relevant spec file in `specs/`.
- Run `maxon-bin/.maxon/maxon spec-test` and make sure the whole suite passes before opening a pull
  request.

## Pull requests

- Open an issue first for anything beyond a small fix, so the design can be agreed on.
- Keep each pull request focused on one logical change.
- Make sure `scripts/build.sh` builds cleanly and the spec suite passes.
- Format Maxon source with `maxon fmt` and match the style of the surrounding code. Maxon favors
  explicit, readable code — no implicit coercions, no silent failures, meaningful error
  messages.

## License

By contributing, you agree that your contributions are dual-licensed under
[MIT](LICENSE-MIT) and [Apache-2.0](LICENSE-APACHE), the same terms as the project, without any
additional terms or conditions.
