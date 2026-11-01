---
feature: byref-param-widening-and-guard
keywords: [mutable, byref, param, range, typealias, guard, cell]
category: memory-safety
---

# `mutable` Parameters: What the Entry Guard Reads

## Documentation

A `mutable` parameter is passed as the address of a cell rather than as a value (`pass-by-reference.md` is
the feature). Its own ENTRY RANGE GUARD reads one dereference away from the parameter slot. The slot holds
the cell's ADDRESS, so guarding it compares a heap address against the declared bounds; the value the caller
actually passed is what the range describes. A full-range alias hides this completely — it is not guarded at
all — so the symptom is a NARROW alias panicking on every call while the identical program with a wide one
runs.

## Tests

<!-- test: narrow-ranged-byref-param-guards-the-pointee -->
The entry guard of a by-reference parameter checks what the cell HOLDS. Both 5 (in) and 99 (out) sit
well inside `Int`, so nothing panics and the reassigned value comes back.
```maxon
typealias Int = int(0 to 1000000)

function bump(n mutable Int) returns Int
	n = 99
	return n
end 'bump'

function main() returns ExitCode
	var v = 5 as Int
	return bump(v)
end 'main'
```
```exitcode
99
```

<!-- test: full-range-byref-param-needs-no-guard -->
The control that isolates the guard from everything else about the ABI: the identical program over a
FULL-range alias emits no entry guard at all, and behaved correctly even while the narrow one did
not.
```maxon
typealias Wide = int(i64.min to i64.max)

function bump(n mutable Wide) returns Wide
	n = 99
	return n
end 'bump'

function main() returns ExitCode
	var v = 5 as Wide
	return bump(v)
end 'main'
```
```exitcode
99
```

<!-- test: out-of-range-write-through-a-byref-param-still-panics -->
Reading the pointee is not the same as not reading anything, and writing through it is a door like any other:
`bump` doubles the `Small` it was handed by reference, and the sum of two in-range values is refused where it is
written, on the line of the write. Every door into a `Small` `var` checks what it stores, and a slot nothing wrote
cannot be made over a `Small`: `resize`, `setLength` and `FixedArray.create` over an element whose range excludes zero
are **E3106**. So no out-of-range value can arrive at the parameter itself.
```maxon
typealias Small = int(1 to 100)

function bump(n mutable Small) returns Small
	n = n + n
	return n
end 'bump'

function main() returns ExitCode
	var v = 60 as Small
	return bump(v)
end 'main'
```
```exitcode
1
```
```stderr
panic at out-of-range-write-through-a-byref-param-still-panics.test:5: Range check failed: value outside typealias 'Small'
Stack trace:
  in bump
  in main
  in mrt_start
```

<!-- test: narrow-ranged-float-byref-param-guards-the-pointee -->
A ranged FLOAT parameter takes the same door and the same dereference, in f64 — the domain is
decided where the bounds are read, never where the site is recorded.
```maxon
typealias Ratio = float(0.0 to 10.0)

function scale(r mutable Ratio) returns Ratio
	r = r * 2.0
	return r
end 'scale'

function main() returns ExitCode
	var v = 3.0 as Ratio
	let doubled = scale(v)
	if doubled == 6.0 'exact'
		return 6
	end 'exact'
	return 1
end 'main'
```
```exitcode
6
```
