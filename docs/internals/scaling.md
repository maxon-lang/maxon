---
title: Scaling
description: The rule that compilation stays linear in program size, the hazards that break it, and how scale-test measures it.
sidebar:
  order: 7
---

**Compilation must stay linear in program size.** A cost that grows faster than the program is a defect,
however small it is on the programs in front of you, because the compiler compiles itself and every program
grows. `scale-test` is the instrument that measures it.

## Hazards

The same few shapes turn a linear pass superlinear, and the code guards against each:

- **A whole-program query under a per-file one** is asked once per file, so O(program) work inside it
  becomes O(files × program) ([Query Spine](query-spine.md#whole-program-queries-under-per-file-ones)).
- **A hash table keyed by a dense index** spends a hash and a cache miss on an array subscript
  ([Names and Values](names-and-values.md#data-representation)).
- **A scan per op or per site** — finding a block by id in a list, comparing each candidate with every
  expression in scope — is O(sites × size).
- **Growing a column one element at a time with `resize`** reserves the exact length each time, which is
  quadratic; `growFilled` grows geometrically.
- **Iterating a dense index space** where only the set bits matter, and **rebuilding an analysis** after each
  local edit where a repair of the touched region would do.

## scale-test

`maxon scale-test` (`maxon-bin/Testing/ScaleTestRunner.maxon`) compiles a generated program at a ladder of
sizes, each rung double the one before, and reports, per compile phase and per rung, the allocations, bytes
and CPU time of the compiling thread. Wall time is never measured.

**Growth is read as the ratio between rungs.** ×2.00 is linear, ×4.00 quadratic, ×1.00 constant; nothing is
fitted and there is no threshold (`ScaleRatio.maxon`). Allocation counts are exact and reproducible; bytes are
comparable only between runs from the same checkout path, because they vary with it; CPU time is noisy,
so a small movement in it is not a finding. The ladder has 6 rungs by default, 1 to 8 allowed.

`scale-test` is an instrument, not a gate: it exits 0 whatever the numbers say. A non-zero exit means the run
itself failed — a rung did not compile, or the generated program folded to a constant and measured nothing.
`--note` appends the run to `docs/optimization-log.md`, whose header gives the rules for reading its rows.

## The corpus

`ScaleCorpus.maxon` generates one program per rung, doubling every knob at once. Each knob is `base << rung`
and exercises one kind of growth: many functions spread over many files (loading, merging and the
[query spine](query-spine.md)), one long function of sequential `if`s, one function with a very large block
count, call chains that keep values live across calls, loops under register pressure, nested loops,
globals, structs, enums and `match`, strings, arrays, generics, interfaces, and more. At rung 5 the long
function holds 3,200 `if`s. Every value is seeded from a loop-carried `var` or a call result, because a
constant would be folded away and measure nothing.

The corpus prints its own blind spots on every run: it measures no wall time, it covers a subset of the
language, and it cannot see a cost its programs never trigger.

## Ladders

A cost the corpus cannot isolate — because it doubles every axis together — gets a hand-built generator in
`tests/ladders/`, described in its README. Each grows one axis and comes with a control that grows another,
such as `genblocks.sh`'s one function of `n` `if`s against `n` functions of one `if` each. A logged result
from a ladder names the generator that produced it, so it can be re-run.
