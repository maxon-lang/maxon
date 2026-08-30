---
feature: log10
status: stable
keywords: log10, logarithm, base-10, math
category: stdlib
---
# log10

## Documentation

Calculate the base-10 logarithm of a number.

**Signature:** `Math.log10(x float) float`

**Parameters:**
- `x` - The number to take the base-10 logarithm of (must be positive)

**Returns:** The base-10 logarithm of the input

**Example:**

```maxon
var x = 100.0
var y = Math.log10(x)     // 2.0 (10^2 = 100)

var z = 1000.0
var w = Math.log10(z)     // 3.0 (10^3 = 1000)

var a = 10.0
var b = Math.log10(a)     // 1.0 (10^1 = 10)
```
**Notes:**
- Input must be positive (returns NaN for negative values)
- `log10(0.0)` returns negative infinity
- `log10(1.0)` returns `0.0`
- `log10(10.0)` returns `1.0`
- For integer inputs, the value is automatically promoted to float

## Tests

⚠ **EVERY `stdout` BLOCK IN THIS FILE IS RETRACTED TO SHORTEST ROUND-TRIP, for the reason
`specs-shv2/sin.md` sets out at length.** shv2 prints the shortest round-trip representation by user
ruling; `/specs` used the bootstrap's fixed-6-decimal printer.

⭐ **BUT FOUR OF THESE ARE NOT MERE RE-RENDERINGS — THEY ARE THE CARRY-LOSS DEFECT ITSELF, AND THIS FILE
IS ITS WORST VICTIM.** The bootstrap's printer rounds the fraction to six decimals and never carries into
the integer part. `log10(100.0)` is `1.9999999999999996`, which a *correct* six-decimal printer renders
`2.0` — so `1.999999` was never a faithful rendering of that double under any correct formatter. Same for
`log10(1000.0)` (`2.999999`), `log10(10.0)` (`0.999999`) and `log10(10000.0)` (`3.999999`), whose true
values are 3, 1 and 4. Those four are exactly why the shortest-round-trip ruling exists: a value that is
not 2 can no longer print as though it were, and a value that IS 2 can no longer print as `1.999999`.

The remaining two (`log10(2.0)` → `0.30103`, and the int-promotion case, which repeats `log10(100.0)`)
are ordinary re-renderings.

<!-- test: log10.basic -->
```maxon
function main() returns ExitCode
	let x = Math.log10(100.0)
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1.9999999999999996
```

<!-- test: log10.one-thousand -->
```maxon
function main() returns ExitCode
	let x = Math.log10(1000.0)
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2.9999999999999996
```

<!-- test: log10.ten -->
```maxon
function main() returns ExitCode
	let x = Math.log10(10.0)
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0.9999999999999998
```

<!-- test: log10.one -->
```maxon
function main() returns ExitCode
	let result = Math.log10(1.0)
	if result == 0.0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: log10.precision -->
```maxon
function main() returns ExitCode
	let x = Math.log10(2.0)
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0.30102999566398125
```

<!-- test: log10.with-int-promotion -->
```maxon
function main() returns ExitCode
	let x = 100  // int
	let result = Math.log10(x)  // x promoted to 100.0
	print("{result}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1.9999999999999996
```

<!-- test: log10.large-value -->
```maxon
function main() returns ExitCode
	let x = Math.log10(10000.0)
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3.999999999999999
```

⭐⭐ **shv2-authored, and THIS FILE'S NOTES NEEDED NO RETRACTION ON THE NON-POSITIVE BEHAVIOUR — which
is the strongest evidence the ruling matches what the language always intended.** The Notes at the top
already say *"returns NaN for negative values"* and *"`log10(0.0)` returns negative infinity"*. **Both
sentences were FALSE when they were written** — the implementation returned the sentinel `0.0` for
every non-positive input, exactly as `log.md` and `log2.md` admitted in their own Notes — and both
**become true with this change**. On that question the code was wrong about the prose, and for once
the spec is the party that does not have to move.

⚠ **THAT IS A CLAIM ABOUT TWO SENTENCES AND NOT ABOUT THIS FILE.** The file DID need the
shortest-round-trip retraction above, and the Notes are still wrong about something else: *"`log10(10.0)`
returns `1.0`"* four lines up, and the Example's `// 2.0 (10^2 = 100)`, `// 3.0 (10^3 = 1000)` and
`// 1.0 (10^1 = 10)`, are contradicted by this file's own goldens — `0.9999999999999998`,
`1.9999999999999996`, `2.9999999999999996`. **Read those four as the MATHEMATICAL values this
implementation approximates, not as what it returns.** They were wrong before the change and they are
wrong after it; the ruling never touched them, and nothing in this file should be read as saying its
prose came out clean.

`log10` is also the one of the three that grew **no guard at all**: it is `log` divided by ln 10, a
finite non-zero constant, and IEEE 754 division carries both special answers through untouched
(`-inf / ln10` is `-inf`, `nan / ln10` is `nan`). The values below are inherited, not restated — which
is why they cannot drift from `log`'s.

<!-- test: log10.non-positive-is-ieee -->
```maxon
function main() returns ExitCode
	let negativeZero = -0.0
	print("{Math.log10(0.0)}\n")
	print("{Math.log10(negativeZero)}\n")
	print("{Math.log10(-1.0)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-inf
-inf
nan
```

⭐ **shv2-authored, and INHERITED like the three above it.** `log10` grew no `+inf` guard either, for
the same reason it grew no non-positive one: `+inf / ln10` is `+inf`. The value below is `log`'s answer
divided by a finite non-zero constant, so it cannot drift from `log.md`'s `positive-infinity` case —
there is nothing here for it to drift from.

<!-- test: log10.positive-infinity -->
```maxon
function main() returns ExitCode
	let positiveInfinity = -Math.log(0.0)
	print("{Math.log10(positiveInfinity)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
inf
```
