---
title: Contributing
description: How to contribute to Maxon — report bugs, improve the compiler and standard library, write docs and examples, or build something real in the language.
sidebar:
  order: 7
---

Maxon is free and open source, dual-licensed under MIT and Apache-2.0. It was written by AI,
but it's built in the open — and contributions are welcome, whether that's a bug report, a
patch to the compiler, a documentation fix, or a real program written in the language.

Everything lives in one repository:
[github.com/maxon-lang/maxon](https://github.com/maxon-lang/maxon).

## Ways to contribute

### Report a bug

Found a miscompile, a crash, a confusing diagnostic, or a place where the docs and the
compiler disagree? Open an issue on the
[issue tracker](https://github.com/maxon-lang/maxon/issues). A good report includes:

- A minimal `.maxon` program that reproduces the problem.
- What you expected to happen, and what actually happened (exact output or error code).
- Your platform (Windows / Linux) and how you built the compiler.

The smaller the reproduction, the faster it can be fixed — Maxon's whole philosophy is that
code should be easy to read, and that applies to bug reports too.

### Improve the compiler or standard library

The compiler (and its from-scratch native backends), the language server, and the standard
library are all
open. Patches that fix bugs, improve diagnostics, or extend the standard library are welcome.
Open an issue first for anything non-trivial so the approach can be discussed before you invest
the work, then send a pull request.

### Improve the docs and examples

Documentation and examples are some of the highest-leverage contributions, because a language
designed to be *read* lives or dies on how well it's explained. Typo fixes, clearer
explanations, and new [example programs](/examples/) are all valuable. Examples should be real,
compilable `.maxon` files.

### Write something in Maxon

The most useful thing you can do is build something real. Writing actual programs surfaces the
rough edges no test suite will, and shapes where the language goes next. Share what you build —
report what felt awkward, what was missing, and what worked.

## The development loop

Maxon builds from source. See [Installation](/docs/getting-started/installation/) for the full
prerequisites; the short version, once you have them:

```bash
git clone https://github.com/maxon-lang/maxon.git
cd maxon

mkdir -p .bootstrap && cp "$(which maxon)" .bootstrap/maxon   # seed with a released compiler
maxon build                                                    # build the compiler with it
maxon-bin/.maxon/maxon spec-test                               # run the full spec-test suite
```

Maxon is written in Maxon, so building it needs a Maxon compiler — install a release first. Copy the
**binary** into `.bootstrap/`, not the unpacked archive: the compiler finds `stdlib/` by walking up
from its own executable, so a released standard library left beside the seed would be compiled in
place of the checkout's own.

### Tests

Maxon's tests are organized as **spec files**: each language feature has a single source of
truth in the `specs/` directory that holds the feature's documentation *and* its executable
test cases together. When you change behavior, update or add the relevant spec, and make sure
`./maxon-bin/.maxon/maxon spec-test` passes before opening a pull request.

## Pull requests

- Open an issue first for anything beyond a small fix, so the design can be agreed on.
- Keep changes focused — one logical change per pull request.
- Make sure the compiler builds cleanly and `./maxon-bin/.maxon/maxon spec-test` passes.
- Match the style of the surrounding code; Maxon favors explicit, readable code over clever or
  terse code, in the compiler as much as in the language.

By contributing, you agree that your contributions are dual-licensed under MIT and Apache-2.0,
the same terms as the project.
