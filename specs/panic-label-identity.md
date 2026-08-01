---
feature: panic-label-identity
status: stable
keywords: [panic, stdlib, symdata, label, monomorphization]
category: runtime
---

# Stdlib panic messages keep their identity across specialization

## Documentation

Every panic carries its message as a blob of static data addressed by a label
the compiler mints for it. Two different messages must never share one label:
only the first one to reach the emitted data section is written, so a second
panic holding that label prints the first one's text.

The hazard is not theoretical. A panic inside a GENERIC stdlib function is
COPIED into every specialization, while a panic inside a CONCRETE one is
emitted exactly as it was parsed. If a copy re-derived its label instead of
carrying the one it was given, the two derivations could answer to different
state — and the program would then report a failure that never happened,
naming a function it never called.

The rule is therefore: a panic's label is decided once, where its message is
written down, and every copy of that panic keeps it.

## Tests

<!-- test: specialized-panic-keeps-its-own-message -->
The string operations put a long run of CONCRETE stdlib panics into the program
— the grapheme, hash, Unicode-category, UTF-8 and UTF-16 helpers all panic on
invariants they never break — while `resize` panics from inside a
SPECIALIZATION of the generic `Array`. The panic that actually fires must print
its own message, the one naming `Array.resize`, and none of theirs.
```maxon
function main() returns ExitCode
	let s = "  héllo wörld  "
	var n = s.trim(CharacterSet.letters()).count()
	n = n + s.bytes().count()
	for cp in "héllo".codepoints() 'c'
		n = n + cp
	end 'c'
	for u in "h€llo".utf16() 'u'
		n = n + u
	end 'u'
	let m = ["a": 1, "b": 2]
	n = n + m.count()
	print("{n}\n")
	var a = [10, 20, 30]
	a.resize(-2)
	return 0
end 'main'
```
```exitcode
1
```
```stdout
9493
```
```stderr
panic at Array.maxon:382: Array.resize: newLength is not an ElementCount — a negative request is never above the capacity, so reserve does not grow for it and setLength refuses it
Stack trace:
  in __Array_i64.resize
  in main
  in mrt_start
```

<!-- test: specialized-panic-keeps-its-own-message-twin -->
The same program again. The compiler's spec runner compiles on a pool of worker
threads, and a panic label derived from per-thread state answers differently on
a thread that parsed the stdlib and one that did not — so a single copy could
be handed the one thread that happens to agree with itself. Two independent
work items cannot both land there.
```maxon
function main() returns ExitCode
	let s = "  héllo wörld  "
	var n = s.trim(CharacterSet.letters()).count()
	n = n + s.bytes().count()
	for cp in "héllo".codepoints() 'c'
		n = n + cp
	end 'c'
	for u in "h€llo".utf16() 'u'
		n = n + u
	end 'u'
	let m = ["a": 1, "b": 2]
	n = n + m.count()
	print("{n}\n")
	var a = [10, 20, 30]
	a.resize(-2)
	return 0
end 'main'
```
```exitcode
1
```
```stdout
9493
```
```stderr
panic at Array.maxon:382: Array.resize: newLength is not an ElementCount — a negative request is never above the capacity, so reserve does not grow for it and setLength refuses it
Stack trace:
  in __Array_i64.resize
  in main
  in mrt_start
```
