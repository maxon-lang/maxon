---
title: You Aren't Going To Write It
description: Maxon's design philosophy — why a language meant to be written by AI should optimize for review, not keystrokes.
date: 2026-05-30
authors: maxon
tags:
  - philosophy
excerpt: For fifty years, languages optimized for the person typing. Maxon makes a different bet — that code written by a model has to answer for itself — and follows it all the way down.
---

For about fifty years, programming languages have optimized for the same person: the one
typing. Terseness, implicit conversions, clever defaults, values that might or might not be
there — almost every "ergonomic" feature is really a way to type fewer characters. That made
sense. A human wrote every line, and keystrokes were the bottleneck.

That assumption no longer holds. Increasingly, the code is written by a model. And once the AI
is the one typing, optimizing for the typist optimizes for the wrong party. What's scarce isn't
keystrokes any more — it's confidence that the code does what it claims, whether that check
comes from a human opening the diff, a reviewer chasing one specific question, the compiler, or
the next model picking up the file.

So Maxon makes a different bet, and the whole language follows from it:

> ***You* aren't going to write it.**

Which means the code has to answer for itself.

## Spend the keystrokes

If verification is what matters, then verbosity stops being a cost. Every character that makes
a question answerable on the page is worth typing — because the entity typing it doesn't mind,
and anything that checks it benefits. Maxon spends keystrokes deliberately:

- **No null.** A value is there or the operation says, in the code, what happens when it
  isn't. There's nothing to forget to check, because there's no hidden absent case to forget.

  ```maxon
  let value = try inputVector.get(index) otherwise 0.0
  ```

- **Constraints in the type system.** A range isn't a comment or a runtime assert; it's part
  of the type. The bounds are settled without leaving the line, and an out-of-range value is a
  compile error.

  ```maxon
  typealias Port = int(0 to 65535)
  let port = 8080 as Port
  ```

- **Every block names what it closes.** No counting braces to find where a loop ends. The
  structure is stated.

  ```maxon
  while iteration < 10 'iterate'
      iteration = (iteration + 1) as Iteration
  end 'iterate'
  ```

None of these are conveniences for the typist. Each is a small tax on writing and a steady
dividend the moment anything has to be checked. That's the trade Maxon takes everywhere.

## The same thing that's easy to check is hard to get wrong

There's a happy consequence. The properties that let a reviewer settle a question without
leaving the line are the same ones that make the code legible to a model writing it: no
implicit state to track, no absent values to mishandle, no ambiguous structure to misjudge.
The language that's easiest to audit is also the one a model is least likely to get subtly
wrong.

That's not a coincidence — it's the point. Maxon was written by AI, and it's designed to hold
up when you look. Optimizing for review turns out to be the same as optimizing for
correctness.

## Where this goes

This post is the first of what will be a mixed feed here — release notes when versions ship,
and essays like this one when there's an idea worth laying out. If you want to see the
philosophy in code, the [examples](/examples/) are real, compilable programs, and the
[language reference](/docs/language/overview/) walks through every construct.

Whatever you decide to check, we tried to put the answer on the page.
