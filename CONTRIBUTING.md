# Contributing to Maxon

Maxon is free and open source, dual-licensed under
[MIT](LICENSE-MIT) and [Apache-2.0](LICENSE-APACHE). It was written by AI, but it's built in
the open — and contributions are welcome, whether that's a fix to the compiler, a documentation
fix, a real program written in the language, or a bug report.

The project's guiding philosophy is **"You aren't going to write it. You are going to read
it."** The AI writes the code; humans read it. So Maxon favors explicit, readable code over
clever or terse code — and that applies to contributions, in the compiler and standard library
as much as in the language itself.

## Ways to contribute

### Fix a bug

**We prefer a fix to a report.** Maxon is written by AI, and an AI coding agent pointed at this
repository can usually turn a failing program into a fix — so a pull request that fixes a bug is
worth far more than an issue describing it, and reaches everyone sooner. The repository carries
instructions for coding agents (`.claude/CLAUDE.md` and its skills for Claude Code,
`.github/copilot-instructions.md` for Copilot), so an agent opened in a checkout already knows how
to build, test and land a change.

Found a miscompile, a crash, a confusing diagnostic, or a place where the docs and the compiler
disagree?

1. Add a spec case that reproduces it (see [Tests](#tests)) and watch it fail.
2. Fix it until that case — and the rest of the suite — passes.
3. Open a pull request. A bug fix needs no issue first.

### Report a bug you cannot fix

If you cannot fix it, [open an issue](https://github.com/maxon-lang/maxon/issues/new?template=bug_report.yml)
— an unfixed bug is still worth knowing about. A good report includes:

- A minimal `.maxon` program that reproduces the problem.
- What you expected to happen, and what actually happened (exact output or exit code).
- Your platform (Windows / Linux / macOS) and how you built the compiler.

The smaller the reproduction, the faster it can be fixed.

### Propose a feature or change

Ideas for the language, the standard library or the toolchain go in
[Ideas](https://github.com/maxon-lang/maxon/discussions/categories/ideas), not the issue
tracker. Search there first — if someone has already proposed it, upvote it and add your use
case in a comment. The most-upvoted ideas are the ones looked at first.

### Improve the compiler or standard library

The compiler (a native backend, with no LLVM), the language server, and the standard library are all
open. Patches that fix bugs, improve diagnostics, or extend the standard library are welcome.
A bug fix goes straight to a pull request. A new feature or a change of design starts in
[Ideas](https://github.com/maxon-lang/maxon/discussions/categories/ideas), so the approach can be
agreed before you invest the work.

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
- Node.js 22.12+, only if you are building the website (`website/` — Astro 7's minimum)

> The build scripts are bash. Run them in **Git Bash** on Windows, or bash on Linux and macOS — not
> PowerShell or cmd.

### Build and test

[Install](https://maxon.dev/install) a release and copy its binary into `.bootstrap/` with
`mkdir -p .bootstrap && cp "$(command -v maxon)" .bootstrap/` — the **binary alone**, not the release's
directory, because the compiler resolves `stdlib/` by walking up from its own executable and a released
one left beside it would be compiled in place of this tree's.

```bash
./.bootstrap/maxon build maxon-bin -o maxon-bin/.maxon/maxon   # first build, with the seed
./maxon-bin/.maxon/maxon build maxon-bin      # afterwards, it rebuilds itself
./maxon-bin/.maxon/maxon spec-test
```

⛔ **Run a compiler that lives INSIDE this checkout — never one installed on your PATH.** The
compiler finds `stdlib/` by walking up from its own executable, so an installed `maxon` compiles this
repository's sources against the RELEASE's standard library. Measured: it succeeds and exits 0, having
built a compiler from a library that is not this tree's.

`build` with no path compiles [`build.maxon`](build.maxon) at the root and runs the build it
describes — it is a program, not a config file, so a build can compute what it compiles. Rebuilding
with the compiler in the slot works because it renames its own running image to `maxon.previous`
first; a FAILED build then leaves the slot EMPTY rather than a stale compiler answering as though it
were current. `scripts/fixpoint.sh` builds the compiler with itself twice and checks that the two
binaries are byte-identical.

Run a program with the freshly built compiler:

```bash
maxon-bin/.maxon/maxon run examples/basic.maxon
```

`run` compiles the program, caches the build and launches it, forwarding its streams and its exit
code; the tail of the command line is the program's own. To produce a binary you can keep, build it
instead:

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

- A bug fix needs no issue first. A new feature or a change of design starts in
  [Ideas](https://github.com/maxon-lang/maxon/discussions/categories/ideas), so the design can be
  agreed on.
- Keep each pull request focused on one logical change.
- Make sure the compiler builds cleanly and the spec suite passes.
- Format Maxon source with `maxon fmt` and match the style of the surrounding code. Maxon favors
  explicit, readable code — no implicit coercions, no silent failures, meaningful error
  messages.

## License

By contributing, you agree that your contributions are dual-licensed under
[MIT](LICENSE-MIT) and [Apache-2.0](LICENSE-APACHE), the same terms as the project, without any
additional terms or conditions.
