---
title: Compiler Internals
description: How the Maxon compiler works inside, subsystem by subsystem, for people changing it.
sidebar:
  order: 0
---

How the Maxon compiler works inside, for people changing it. The compiler is written in Maxon and lives
in `maxon-bin/`. These pages describe mechanisms and the invariants they rely on; what a command, flag or
diagnostic does for a user is in the [CLI reference](../CLI_REFERENCE.md).

## From source to machine code

A compile is a chain of memoized queries over the project's files ([Query Spine](query-spine.md)):

1. each file is tokenized, and its `#if` blocks resolved against the build;
2. every file's declarations are read from its tokens into one program-wide signature index, before any
   file is parsed;
3. each file is parsed. The parser has no AST: it emits Maxon IR directly, already in SSA
   ([Names and Values](names-and-values.md));
4. the parsed files are merged into one module.

The program then passes through three IR tiers (`maxon-bin/Compiler/IR/PassPipeline.maxon` schedules
them):

| Tier | Holds | Passes |
| --- | --- | --- |
| Maxon (`IR/Maxon/`) | Source-level IR with width-free types, as the parser emits it | name reporting, signature visibility, type resolution, semantic checks, unused exports |
| Std (`IR/Std/`) | Widths and the ABI resolved; values are unlimited virtual registers | range checks, inlining, then per function: constant folding, strength reduction, branch threading, value ranges, CSE, loop-invariant code motion, guard unswitching, and dead and trivial phi removal; then stack-record promotion and tuple returns |
| Target (`IR/Target/`) | x64 or arm64 machine instructions | instruction selection, [register allocation](register-allocation.md), prologue and epilogue, encoding |

`LowerMaxonToStd` takes the Maxon tier to Std without renumbering values. The x64 and arm64 back ends lower
Std to Target; the wasm32 back end reads the Std tier directly and has no Target tier, because every SSA
value is a wasm local.

## Pages

- [Query Spine](query-spine.md) — memoized queries, content hashes and the parallel front end
- [Names and Values](names-and-values.md) — SSA at parse time, value numbering, symbol names, data
  representation
- [Register Allocation](register-allocation.md) — splitting, colouring and SSA destruction on x64 and arm64
- [Memory Allocator](memory-allocator.md) — the runtime heap, its zeroing contract and the fill ladders
- [Debug Stream](debug-stream.md) — the shared-memory event ring behind `--debugstream` and `maxon monitor`
- [Debugger](debugger.md) — the parts of `maxon debug`
- [Scaling](scaling.md) — keeping compilation linear in program size, and measuring it
