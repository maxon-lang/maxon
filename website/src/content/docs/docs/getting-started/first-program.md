---
title: Your first program
description: Write, compile, and run a small Maxon program — and meet the language's core ideas along the way.
sidebar:
  order: 3
---

This page walks through a small program that touches Maxon's defining features: ranged types,
`try … otherwise`, labeled blocks, and string interpolation.

## Hello, exit code

The smallest valid program is a `main()` that returns an `ExitCode`:

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

The return value becomes the process exit code. Compile and run it:

```bash
maxon build hello.maxon -o hello
./hello
echo $?
```

## Adding a ranged type

In Maxon, numeric types used in type positions are declared with a `typealias` that states
their range. The bound is enforced by the compiler:

```maxon
typealias Port = int(0 to 65535)

function main() returns ExitCode
	let port = 8080 as Port
	print("listening on {port}\n")
	return 0
end 'main'
```

`8080 as Port` is fine; `70000 as Port` is a compile error. The constraint lives in the type, so a
function that takes a `Port` can never receive an invalid one.

## Fallible operations: `try … otherwise`

There is no null in Maxon. Operations that can fail — like reading an element that might be out
of bounds — must be resolved explicitly: every call that can fail is written with `try`, and
leaving it off is a compile error. The most common form supplies a fallback:

```maxon
typealias Guests = Array with String
typealias Seat = int(0 to 9)

function guestAt(guests Guests, seat Seat) returns String
	return try guests.get(seat) otherwise "empty"
end 'guestAt'

function main() returns ExitCode
	var guests = Guests.create()
	guests.push("Ada")
	guests.push("Grace")

	print("seat 1: {guestAt(guests, seat: 1)}\n")
	print("seat 5: {guestAt(guests, seat: 5)}\n")
	return 0
end 'main'
```

Seat 5 is past the end of the list, so the read fails and `otherwise` supplies `"empty"`: the
program prints `seat 1: Grace`, then `seat 5: empty`. Because the fallible result has no "null" to
leak, there is no missed-null-check bug to write.

## Labeled blocks

Every block in Maxon names what it opens and closes. Loops, `if`, and other constructs take a
quoted label, and the matching `end` repeats it:

```maxon
typealias Iteration = int(0 to 10)

function main() returns ExitCode
	var iteration = 0 as Iteration
	while iteration < 10 'iterate'
		print("iteration {iteration}\n")
		iteration = (iteration + 1) as Iteration
	end 'iterate'
	return 0
end 'main'
```

This makes nesting unambiguous to read back — for a human reviewer or a model regenerating the
surrounding code.

## Where to go next

- [Language Reference](/docs/language/overview/) — the complete language, section by section.
- [Examples](/examples/) — full, real programs you can compile.
