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
bin/maxon build hello.maxon
```

## Adding a ranged type

In Maxon, numeric types used in type positions are declared with a `typealias` that states
their range. The bound is enforced by the compiler:

```maxon
typealias Port = int(0 to 65535)

function main() returns ExitCode
	let port = Port{8080}
	print("listening on {port}")
	return 0
end 'main'
```

`Port{8080}` is fine; `Port{70000}` is a compile error. The constraint lives in the type, so a
function that takes a `Port` can never receive an invalid one.

## Fallible operations: `try … otherwise`

There is no null in Maxon. Operations that can fail — like reading an element that might be out
of bounds — must be resolved explicitly. The most common form supplies a fallback:

```maxon
typealias Index = int(0 to 5500)
typealias Value = float(0.0 to f64.max)
typealias Vector = Array with Value

function sumInto(out Vector, source Vector, count Index)
	for i in 0 upto count 'accumulate'
		let v = try source.get(i) otherwise 0.0
		out.set(i, value: v)
	end 'accumulate'
end 'sumInto'
```

Because the fallible result has no "null" to leak, there is no missed-null-check bug to write.

## Labeled blocks

Every block in Maxon names what it opens and closes. Loops, `if`, and other constructs take a
quoted label, and the matching `end` repeats it:

```maxon
typealias Count = int(0 to 10)

function main() returns ExitCode
	var iteration = Count{0}
	while iteration < 10 'iterate'
		print("iteration {iteration}")
		iteration = Count{iteration + 1}
	end 'iterate'
	return 0
end 'main'
```

This makes nesting unambiguous to read back — for a human reviewer or a model regenerating the
surrounding code.

## Where to go next

- [Language Reference](/docs/language/overview/) — the complete language, section by section.
- [Examples](/examples/) — full, real programs you can compile.
