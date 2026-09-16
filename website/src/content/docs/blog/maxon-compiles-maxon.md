---
title: Maxon Compiles Maxon
description: What it took for the compiler to become self-hosting — three bootstraps, a byte-identical fixpoint, and the oracle we gave up to get here.
date: 2026-09-08
authors: maxon
tags:
  - philosophy
excerpt: A language that compiles itself has to have been compiled by something else first. Three times, in our case — and then we deleted all three.
---

The compiler that builds Maxon is written in Maxon. There is no C++ underneath, no Zig, no C#
holding it up: the only thing that can build this compiler is a previous build of this compiler.

That sounds circular because it is, and getting there is the part worth writing down.

## You need something else first

A language that compiles itself has to have been compiled by something else to begin with. Maxon
needed three somethings.

The first was a **C++ compiler**, written in November 2025 — lexer, parser, and a native x86-64
code generator built from scratch after an early LLVM experiment was dropped. It was the first
thing that could turn Maxon source into a native executable. A **Zig** compiler followed in
December and became the main build before the year was out.

The third one was different. The **C# compiler**, started in January 2026, was built from the
same language spec but along an independent path — and that made it something more useful than a
bootstrap. It was a second opinion. Two compilers, sharing no code, agreeing on what a program
does: that was the bar every change had to clear. When they disagreed, one of them was wrong, and
finding out which one was the work.

Meanwhile, in December 2025, work started on writing the Maxon compiler *in Maxon*. It built for
the first time within a week. Then it spent most of a year catching up — real strings and
interpolation, enums and unions, generics, ranges and iterators — feature by feature, tested for
parity against the C# reference the whole way.

## The fixpoint

In August 2026 it compiled itself. That is the headline, and on its own it means less than it
sounds like.

A compiler compiling its own source only proves the result runs. It doesn't prove the result is
*stable* — that the compiler you get out is the compiler you'd get again. So the real test is
one step further:

```bash
scripts/fixpoint.sh
```

Build the compiler with itself. Build it *again* with the binary that just came out. Compare the
two, byte for byte.

```
=== stage 2: ./maxon-bin/.maxon/maxon builds the compiler
=== stage 3: that binary builds it again
FIXPOINT HOLDS — byte-identical
```

A passing test suite cannot answer this question. Every stage shares the compiler's logic, so the
suite exercises the same behaviour no matter which stage produced the binary running it. Only the
*bytes* of two successive self-compiles say whether the emitted code is stable — and a difference
is a miscompile, because it means which binary you happen to be holding decides what your
programs become.

That fixpoint is what makes self-hosting a fact rather than a claim.

It also comes with a wonderfully petty trap. On macOS the ad-hoc code-signature identifier is
taken from the output *filename*, so building `stage2` and `stage3` as differently-named files
makes them differ by exactly one byte — a difference that reads as a miscompile and isn't one.
The two builds have to share a basename and live in different directories. Every self-hosting
project accumulates a few of these.

## The strange part: some changes take two builds

Here's the consequence of self-hosting that catches people out.

Parts of the compiler describe code it *emits* — the runtime a compiled program carries with it.
Edit one of those, rebuild, and the compiler you now hold still behaves the old way. It was built
by the *previous* generation, which knew nothing about your change. Your edit only reaches the
compiler's own behaviour on the generation after that.

So some changes need one self-compile and some need two, and getting it wrong means measuring a
binary that doesn't contain the thing you're measuring. The tree answers the question rather than
leaving it to judgment:

```bash
scripts/self-compiles-needed.sh    # prints `once` or `twice`, and why
```

A language that compiles itself has a build graph with a time axis in it. That takes some getting
used to.

## What we gave up

In September 2026 the C# reference and the original self-hosted source were retired and removed.
What ships now is one compiler, written in Maxon, that builds itself, plus the standard library it
reads. The bootstraps that got it there are archived read-only at
[maxon-lang/maxon-bootstrap](https://github.com/maxon-lang/maxon-bootstrap) — a language that
compiles itself has to have been compiled by something else first, and that is worth being able
to look at.

But it's worth being straight about the cost, because it's the honest half of this milestone.

**A fixpoint proves the compiler is stable. It does not prove the compiler is right.** Both stages
share one implementation's logic, so a wrong answer that both stages agree on is invisible to the
byte comparison and green in the suite. While the C# reference existed, there was an independent
answer to check against. Now there isn't. What's left is the spec suite, and a spec pins behaviour
against *regression* — against changing — not against being wrong in the first place. A defect
that predates its pin stays pinned.

That's the trade: a second implementation is enormously valuable and enormously expensive, and
past a certain point every change has to be made twice by hand. We took the trade with our eyes
open, and the pressure it creates lands on the specs, which now have to carry weight they used to
share.

## Where the compiler is checked now

If the suite is the thing standing in for a second opinion, it has to run somewhere real.
Cross-compiling a test binary for another platform doesn't count — that never runs the compiler
*as* a program on that platform, on its green threads, through its runtime, with its frame layout.
So CI hosts every target on its own architecture and walks the full chain there: a released binary
as the seed, which builds the compiler, which builds it again, and then the suite runs under
*that*.

Which brings this back to what the language is for. The compiler is the largest Maxon program that
exists, and it is the one that gets checked hardest — by itself, by two generations of its own
output, and by everything above. A language whose code has to answer for itself may as well start
with its own.
