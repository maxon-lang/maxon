---
title: Maxon 0.2.1
description: Release notes for Maxon 0.2.1 — what changed in the compiler and standard library.
date: 2026-09-15
authors: maxon
tags:
  - release
excerpt: Maxon 0.2.1 is out. Here's what changed.
---

**Maxon 0.2.1 is released.** Archives for every supported target are on the
[GitHub releases page](https://github.com/maxon-lang/maxon/releases/tag/v0.2.1).

## What changed

### Fixed

- The compiler occasionally crashed partway through a build with `Range check failed: value outside
  typealias 'AllocCount'`.
- A green thread that started reading a socket while another was finishing a read of the same socket
  could wait forever, unreported by the deadlock detector. It now stops with the documented error for
  two readers on one socket, every time.
- On Windows, a socket receive or send that had already completed when its deadline passed or the
  socket closed reported a timeout: the bytes received were lost, and a retried send went out twice.

## Install

To install Maxon or upgrade an existing install, see [Installation](/docs/getting-started/installation/), which also
shows how to install a specific release.
