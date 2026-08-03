---
feature: atan2
status: experimental
keywords: atan2, arctangent, trigonometry, math, radians
category: math-intrinsic
---
# atan2

## Documentation

Computes the arc tangent of y/x using the signs of both arguments to determine the quadrant of the result.

**Signature:** `Math.atan2(y float, x float) float`

**Parameters:**
- `y` - The y-coordinate (numerator)
- `x` - The x-coordinate (denominator)

**Returns:** The angle in radians between the positive x-axis and the point (x, y), in the range [-π, π]

**Example:**

```maxon
// Point on positive x-axis: angle = 0
var a = Math.atan2(0.0, x: 1.0)    // 0.0

// Point on positive y-axis: angle = π/2
var b = Math.atan2(1.0, x: 0.0)    // 1.5708 (≈ π/2)

// Point on negative x-axis: angle = π
var c = Math.atan2(0.0, x: -1.0)   // 3.14159 (≈ π)

// Point on negative y-axis: angle = -π/2
var d = Math.atan2(-1.0, x: 0.0)   // -1.5708 (≈ -π/2)
```

**Notes:**
- Unlike `atan(y/x)`, `atan2` correctly handles all quadrants
- `atan2(0.0, 0.0)` is implementation-defined (typically returns 0)
- Result is always in the range [-π, π]
- Useful for converting Cartesian coordinates to polar coordinates

## Tests

<!-- test: atan2.positive-x-axis -->
```maxon
function main() returns ExitCode
	let angle = Math.atan2(0.0, x: 1.0)
	if angle == 0.0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: atan2.positive-y-axis -->
⚠ **EVERY `stdout` BLOCK IN THIS FILE IS RETRACTED TO SHORTEST ROUND-TRIP, for the reason
`specs-shv2/sin.md` sets out at length — see that file rather than a copy here.** `/specs` renders floats
in the bootstrap's fixed-6-decimal format; shv2 prints the shortest round-trip representation by user
ruling. It changes the rendering, not the numbers, and you can check that here without running
anything: each old value is exactly the fixed-6-decimal rendering of the double replacing it —
`1.570796` → `1.5707963267948966`, `3.141593` → `3.141592653589793`, `-1.570796` →
`-1.5707963267948966`. Stated once here; the other two axis cases below carry the same retraction.
```maxon
function main() returns ExitCode
	// π/2 ≈ 1.5708
	let angle = Math.atan2(1.0, x: 0.0)
	print("{angle}\n")
	// Should be approximately π/2
	if angle > 1.57 'check1'
		if angle < 1.58 'check2'
			return 0
		end 'check2'
	end 'check1'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
1.5707963267948966
```

<!-- test: atan2.negative-x-axis -->
```maxon
function main() returns ExitCode
	// π ≈ 3.14159
	let angle = Math.atan2(0.0, x: -1.0)
	print("{angle}\n")
	// Should be approximately π
	if angle > 3.14 'check1'
		if angle < 3.15 'check2'
			return 0
		end 'check2'
	end 'check1'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
3.141592653589793
```

<!-- test: atan2.negative-y-axis -->
```maxon
function main() returns ExitCode
	// -π/2 ≈ -1.5708
	let angle = Math.atan2(-1.0, x: 0.0)
	print("{angle}\n")
	// Should be approximately -π/2
	if angle < -1.57 'check1'
		if angle > -1.58 'check2'
			return 0
		end 'check2'
	end 'check1'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
-1.5707963267948966
```

<!-- test: atan2.first-quadrant -->
⭐ **THE `stdout` BLOCK BELOW IS ADDED, AND IT IS WHY THIS TICK FOUND A REAL BUG.** `/specs` has this case
and `atan2.third-quadrant` PRINT without asserting anything, so whatever they printed was accepted. They
were the only two off-axis cases in the file — every other case either returns a stored `pi_half` literal
or hands `Math.atan` a zero, which the series never sees. So the one code path with real arithmetic in it
was the one path nothing checked, and **`Math.atan` was wrong by ~1e-2 near |z| = 1**: its 24-term Taylor
series is the Leibniz series at z = 1, converging like 1/n. This case printed `0.7953941713587581` where
π/4 is `0.7853981633974483`. Fixed in `stdlib/Math.maxon` by reducing the argument so nothing reaches the
series above `tan(π/8)`; the truncation error is now far under the rounding floor and accumulated
summation rounding leaves a few ULP.

Asserting the output is the whole repair — a case that prints without a `stdout` block is not a test of
what it prints.
```maxon
function main() returns ExitCode
	// 45 degrees = π/4 ≈ 0.7854
	let angle = Math.atan2(1.0, x: 1.0)
	print("{angle}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0.7853981633974483
```

<!-- test: atan2.third-quadrant -->
```maxon
function main() returns ExitCode
	// Third quadrant: -3π/4 ≈ -2.356
	let angle = Math.atan2(-1.0, x: -1.0)
	print("{angle}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-2.356194490192345
```

<!-- test: atan2.origin -->
```maxon
function main() returns ExitCode
	let angle = Math.atan2(0.0, x: 0.0)
	print("{angle}\n")
	// Origin is typically 0
	if angle == 0.0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
0.0
```

<!-- test: atan2.signed-zero -->
⭐ **ADDED HERE, not ported: `/specs` does not cover the sign of a zero, and four of the eight zero
combinations were wrong until this tick.** IEEE 754 requires `atan2` to carry the sign of `y` through
every zero case — `atan2(-0.0, x: -1.0)` is `-π`, not `+π` — and the case is easy to get wrong because
**no comparison can see it**: `-0.0 == 0.0`, so `y < 0.0` and `y >= 0.0` both answer as though every
zero were positive. `stdlib/Math.maxon` now reads the sign from the bits (`__Builtins.floatToBits`)
exactly once and branches on that.

The rows returning a **zero** are pinned through `floatToBits` rather than printed: `+0.0` and `-0.0`
are equal, so no comparison can tell them apart, and pinning the bits makes this case INDEPENDENT of
`__float_toString`'s treatment of the sign of zero instead of resting on it. That formatter does carry
the sign — `stdlib/Builtins.maxon:1607` keeps it deliberately, and that is exactly what lets
`atan2.origin`'s printed `0.0` pin a POSITIVE zero — but it is a separate fact with its own home, and
this case is not a second copy of it. `-9223372036854775808` is the sign bit alone — the encoding
of `-0.0`.

Seven of the eight zero combinations are pinned below; the eighth, `atan2(0.0, x: 0.0)`, is
`atan2.origin` above.

```maxon
function main() returns ExitCode
	// Sign of y is carried into a zero result, where no comparison can show it.
	print("{__Builtins.floatToBits(Math.atan2(-0.0, x: 1.0))}\n")
	print("{__Builtins.floatToBits(Math.atan2(0.0, x: 1.0))}\n")

	// Negative x reflects by a half turn whose DIRECTION is the sign of y.
	print("{Math.atan2(-0.0, x: -1.0)}\n")
	print("{Math.atan2(0.0, x: -1.0)}\n")

	// Both coordinates zero: the sign of x picks ±0 vs ±π, the sign of y picks the sign.
	print("{__Builtins.floatToBits(Math.atan2(-0.0, x: 0.0))}\n")
	print("{Math.atan2(-0.0, x: -0.0)}\n")

	// The mirror of the row above: a branch that read the sign of x and ignored the sign of y
	// would answer -π here and pass every other row in this case.
	print("{Math.atan2(0.0, x: -0.0)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-9223372036854775808
0
-3.141592653589793
3.141592653589793
-9223372036854775808
-3.141592653589793
3.141592653589793
```
