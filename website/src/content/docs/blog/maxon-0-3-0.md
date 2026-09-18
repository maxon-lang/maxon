---
title: Maxon 0.3.0
description: Release notes for Maxon 0.3.0 — what changed in the compiler and standard library.
date: 2026-09-17
authors: maxon
tags:
  - release
excerpt: Maxon 0.3.0 is out. Here's what changed.
---

**Maxon 0.3.0 is released.** Archives for every supported target are on the
[GitHub releases page](https://github.com/maxon-lang/maxon/releases/tag/v0.3.0).

## What changed

### Added

- `maxon cache`, which reports what the run cache holds on this host and which root the next run will
  fill, and `maxon cache clear`, which empties it. The cache moved out of the host's scratch area,
  where a temporary-file reaper could sweep it mid-session, into `<home>/.maxon/cache` beside the
  compiler an install puts there.
- `Directory.delete`, which removes an empty directory and tells `notFound`, `notEmpty` and
  `deleteFailed` apart.
- `Runtime.processorCount()`, the number of processors the scheduler runs green threads on.
- `Json.quote`, the JSON string escape three places in the standard library and the tooling each had
  a private copy of.
- Two worked examples — `maxgrep`, a parallel grep, and `msort`, a parallel merge sort — each beside
  a tour that reads it.
- A complete reference on maxon.dev: the language, the standard library, the command line and every
  diagnostic, generated from the sources the compiler itself is checked against.

### Changed

- The compiler lexes and parses over a worker pool rather than one thread. On its own source that
  stage spent 7.3 seconds on one processor of sixteen before the change.
- A `let` argument to a service message is LENT rather than moved. The sender keeps its reference and
  goes on reading the value after the send, where the only way to keep reading one was to clone it;
  `var` arguments, temporaries and literals still move. A lent graph is read-only while the service
  can see it, and a write reaching it through another path is refused (E3160).
- A value held at an interface type crosses a message — as a spawn argument, a message parameter or a
  reply.
- A record a callee builds crosses a service boundary as freshly owned, whether it is returned from a
  local it filled or produced by a qualified, static or `try`-guarded call. The proof followed one hop
  and recognised one spelling before.
- A struct-literal field initializer and a field assignment widen a concrete value into an
  interface-typed field, as a call argument already did.
- A closure written at an argument whose parameter is declared with a function type takes its
  parameter types from that declaration, so a comparator passed to `sort` needs no annotations. That
  offer was made at one position only.
- `match` accepts or-arms over a combined error — an awaited reply's own errors merged with
  `ServiceError.stopped`, or a block try's handler over several error types. A handler that binds a
  combined error carrying a boxed member must match it exactly once (E3161), which the release model
  had left unsound in both directions.
- A callee that writes a FIELD of its parameter earns E3019 at the call. `bump(a)` with `bump` doing
  `b.n = 99` compiled and wrote through a binding the caller had declared immutable; a method writing
  its own receiver stays legal, and so does a callee that only calls such a method.
- A `Vector` held inside an element type no longer makes the containing `Array` uncloneable.
- `HttpClient` refuses any scheme but `http` before it connects, rather than sending an `https` URL as
  plaintext to port 80.
- A missing executable is `executableNotFound` on every operating system, and a failed spawn names
  which step failed — stream setup, working directory or launch. A working directory that does not
  exist was silently ignored on Linux.
- A `build.maxon` manifest's `debug_info` is honoured; it was written and read by nothing. A manifest
  build now takes the tree lock and applies the same build-option refusals a command-line build does,
  and malformed manifest fields — a non-string version or source, a define with no `=`, a non-array
  `defines` — are refused rather than silently dropped.
- `maxon build` with no path reports one line about its build runner instead of that runner's own
  compile chatter.

### Fixed

- A `Map` or `Set` under insert-and-remove churn degraded until every lookup probed the whole table,
  because a removed slot counted toward nothing. A thousand rounds over one map and one set took 8.07
  seconds; four thousand now take 87 milliseconds.
- `FilePath.parent()` of a path directly under a root answered an empty or drive-relative path
  instead of the root, so a walk upwards never stopped at the top — it began asking the filesystem
  about the working directory and getting plausible answers back. The parent of a root now throws
  `noParent`.
- A `spawn` or a send could stop the program reporting a second owner for a value that had one: the
  temporaries its arguments built were released after the ownership walk rather than before, and a
  reply's walk ran before the handler had released what it held.
- Reading a promise through an array cursor crashed the compiler when the reply carried a companion
  error type, and a program awaiting promises read that way stopped before its results were in.
- `SharedSegment.readWord`, `writeWord` and `copyOut` read and wrote past the end of the segment
  instead of throwing `outOfBounds`.
- On Windows, `maxon upgrade` and the one-line installer could fail when the calling shell's module
  path hid the host's own PowerShell modules.

### Removed

- `optimize` in a `build.maxon` manifest, which named optimization levels that do not exist.

## Install

To install Maxon or upgrade an existing install, see [Installation](/docs/getting-started/installation/), which also
shows how to install a specific release.
