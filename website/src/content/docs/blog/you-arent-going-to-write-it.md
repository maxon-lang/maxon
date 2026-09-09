---
title: You Aren't Going To Write It
description: Maxon's design philosophy — why a language meant to be written by AI should optimize for the reader, not the typist.
date: 2026-05-30
authors: maxon
tags:
  - philosophy
excerpt: For fifty years, languages optimized for the person typing. Maxon makes a different bet — that you'll read far more Maxon than you write — and follows it all the way down.
---

For about fifty years, programming languages have optimized for the same person: the one
typing. Terseness, implicit conversions, clever defaults, values that might or might not be
there — almost every "ergonomic" feature is really a way to type fewer characters. That made
sense. A human wrote every line, and keystrokes were the bottleneck.

That assumption no longer holds. Increasingly, the code is written by a model. And once the AI
is the one typing, optimizing for the typist optimizes for the wrong party. The bottleneck
moves from *writing* to *reading* — by the human reviewing a diff, and by the next model
picking up the file and trying to reason about it correctly.

So Maxon makes a different bet, and the whole language follows from it:

> **You aren't going to write it. You are going to read it.**

## Spend the keystrokes

If reading is what matters, then verbosity stops being a cost. Every character that makes the
code clearer to read is worth typing — because the entity typing it doesn't mind, and the
entity reading it benefits. Maxon spends keystrokes deliberately:

- **No null.** A value is there or the operation says, in the code, what happens when it
  isn't. There's nothing to forget to check, because there's no hidden absent case to forget.

  ```maxon
  let value = try inputVector.get(index) otherwise 0.0
  ```

- **Constraints in the type system.** A range isn't a comment or a runtime assert; it's part
  of the type. A reader knows the bounds without leaving the line, and an out-of-range value is
  a compile error.

  ```maxon
  typealias Port = int(0 to 65535)
  let port = Port{8080}
  ```

- **Every block names what it closes.** No counting braces to find where a loop ends. The
  structure is stated.

  ```maxon
  while iteration < 10 'iterate'
      iteration = Iteration{iteration + 1}
  end 'iterate'
  ```

None of these are conveniences for the typist. Each is a small tax on writing and a steady
dividend on reading. That's the trade Maxon takes everywhere.

## The same thing that's easy to read is hard to get wrong

There's a happy consequence. The properties that make code legible to a human reviewer are the
same ones that make it legible to a model writing it: no implicit state to track, no absent
values to mishandle, no ambiguous structure to misjudge. The language that's easiest to read is
also the one a model is least likely to get subtly wrong.

That's not a coincidence — it's the point. Maxon was written by AI, and it's designed to be read
by you. Optimizing for the reader turns out to be the same as optimizing for correctness.

## Where this goes

This post is the first of what will be a mixed feed here — release notes when versions ship,
and essays like this one when there's an idea worth laying out. If you want to see the
philosophy in code, the [examples](/examples/) are real, compilable programs, and the
[language reference](/docs/language/overview/) walks through every construct.

You're going to read a lot of it. We tried to make that worth your while.
