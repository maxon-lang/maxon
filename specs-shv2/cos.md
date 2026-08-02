---
feature: cos
status: stable
keywords: cos, cosine, trigonometry, math, radians
category: math-intrinsic
---
# cos

## Documentation

Calculate the cosine of an angle (in radians).

**Signature:** `Math.cos(x float) float`

**Parameters:**
- `x` - The angle in radians

**Returns:** The cosine of the input angle

**Example:**

```maxon
var x = 0.0
var y = Math.cos(x)       // 1.0

var pi = 3.14159265
var z = Math.cos(pi)      // -1.0 (approximately)
```
**Notes:**
- The function works with radians, not degrees
- To convert degrees to radians: `radians = degrees * (π / 180)`
- `Math.cos(0.0)` returns exactly `1.0`
- `Math.cos(π)` returns approximately `-1.0`
- The cosine function oscillates between -1 and 1

## Tests

<!-- test: cos.basic -->
⚠ **THE `stdout` BLOCK IS RETRACTED, FOR THE REASON `specs-shv2/sin.md` SETS OUT AT LENGTH — see that
file rather than a second copy here.** In short: `/specs` renders floats in the bootstrap's fixed-6-decimal format,
whose carry never reaches the integer part; shv2 prints the **shortest round-trip** representation by user
ruling, which makes that whole class of defect unreachable. Every `/specs` file that prints a float needs
this same retraction, and it is one decision, not a per-file judgement.

Verified for these four values specifically, because a shortest-round-trip digit string is only correct
for the exact double it came from: `Math.cos` is **bit-identical under both compilers** here
(`4607182418800017408`, `4606079780542709072`, `4603041830072026763`, `-4697588500269686311`), so only
the rendering differs. ⚠ Note `cos(1.0)` prints `0.5403023058681397` and **not** the more familiar
`0.5403023058681398` — those are two DIFFERENT doubles, one ulp apart (`…398` parses to
`4603041830072026764`), and `…397` is the shortest form of the one this platform's `cos` actually returns.

⭐ **This retraction changes the RENDERING, not the numbers, and you can check that without running
anything:** each `/specs` value is exactly the fixed-6-decimal rendering of the double printed below it —
`0.8775825618903728` → `0.877583`, `0.5403023058681397` → `0.540302`, `-0.0000036732051033919804` →
`-0.000004`, `1.0` → `1.0`. The two blocks describe the same four doubles; only the formatter differs.
```maxon
function main() returns ExitCode
	let x1 = Math.cos(0.0)
	let x2 = Math.cos(0.5)
	let x3 = Math.cos(1.0)
	let x4 = Math.cos(1.5708)
	print("{x1}\n")
	print("{x2}\n")
	print("{x3}\n")
	print("{x4}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1.0
0.8775825618903728
0.5403023058681397
-0.0000036732051033919804
```


<!-- test: cos.zero -->
```maxon
function main() returns ExitCode
	let result = Math.cos(0.0)
	if result == 1.0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: cos.with-int-promotion -->
```maxon
function main() returns ExitCode
	let x = 0  // int
	let result = Math.cos(x)  // x promoted to 0.0
	if result == 1.0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```
