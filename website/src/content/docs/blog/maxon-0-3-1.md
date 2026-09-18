---
title: Maxon 0.3.1
description: Release notes for Maxon 0.3.1 — what changed in the compiler and standard library.
date: 2026-09-17
authors: maxon
tags:
  - release
excerpt: Maxon 0.3.1 is out. Here's what changed.
---

**Maxon 0.3.1 is released.** Archives for every supported target are on the
[GitHub releases page](https://github.com/maxon-lang/maxon/releases/tag/v0.3.1).

## What changed

### Fixed

- `maxon run` and a path-less `maxon build` refused to run anything on a host whose home directory
  cannot be written, rather than using the next cache directory in the list. A container started with
  a numeric user that has no account entry is the common way to meet this: the home directory is the
  filesystem root, nothing can be created under it, and every program was refused. A run now keeps to
  the same order as before and takes the first directory it can actually create.
- The refusal, when no directory at all can be created, names every variable the host consults
  instead of quoting whichever one it stopped on.
- The Docker images could not run a program as any user without an account entry, which is both
  variants' documented use. They name a writable temporary directory now.

### Changed

- `maxon cache` marks as in use the directory a run will actually fill, and where no directory can be
  created it says so instead of marking nothing. Reporting the cache now creates that directory if it
  is absent, which is what the next run would have done.

## Install

To install Maxon or upgrade an existing install, see [Installation](/docs/getting-started/installation/), which also
shows how to install a specific release.
