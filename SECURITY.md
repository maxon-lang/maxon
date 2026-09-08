# Security

## Reporting a vulnerability

Report privately through GitHub's [security advisory
form](https://github.com/maxon-lang/maxon/security/advisories/new), not as a public issue.

Expect an acknowledgement within a few days. Maxon is a small project: there is no on-call rotation
and no guaranteed response window, and saying so is better than implying one that does not exist.

## What is in scope

Maxon is a compiler, so the interesting cases are about what it *emits* and what it *accepts*:

- The compiler emitting code that violates a safety property the language states — a bounds check
  elided where the language promises one, a use-after-free the borrow checker should have refused.
- A crafted source file crashing the compiler in a way that is exploitable rather than merely a crash.
- The standard library mishandling untrusted input.

**A compiler crash on malformed input is a bug, not usually a vulnerability.** Report it as an
ordinary issue — that path is faster and gets the same attention.

Compiling untrusted source is not a sandbox and is not treated as one: a Maxon program can run
arbitrary code the moment you run it, and the build manifest (`build.maxon`) is itself a program the
compiler executes. Treat compiling an untrusted project exactly as you would treat running one.

## Releases

Release binaries are **not code-signed**, so Windows SmartScreen and macOS Gatekeeper will warn on
first run. Signing is a known gap and a planned one.

Every release publishes `SHA256SUMS` beside its archives. Until the artifacts are signed, that
checksum is what tells you a download is the file that was built — check it.
