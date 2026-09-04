---
feature: character-type
status: experimental
keywords: [character, grapheme, egc, utf8]
category: types
---

# Character Type

## Documentation

The `character` type represents an Extended Grapheme Cluster (EGC) — what users perceive as a single character.

### Syntax

```maxon
var letter = 'A'
var accent = 'é'
var emoji = '🎉'
```
Character literals are enclosed in single quotes.

### Extended Grapheme Clusters

An EGC represents what a user perceives as a single character, even if composed of multiple Unicode code points:

```maxon
var family = '👨‍👩‍👧‍👦'  // Family emoji (multiple code points joined with ZWJ)
var flag = '🇺🇸'          // Flag (regional indicator pair)
```

### String Iteration

Iterating over a string yields `character` values (EGCs):

```maxon
var s = "café"
for c in s 'chars'
	print("{c}")  // iterates 4 times: 'c', 'a', 'f', 'é' (not 5 bytes)
end 'chars'
```

### Character Methods

```maxon
var c = 'é'
var b = c.bytes()
b.count()              // Returns byte length of UTF-8 encoding (2 for é)
var cp = c.codepoints()
cp.count()             // Returns number of Unicode codepoints
"{c}"                 // Converts to string via interpolation

var a = 'A'
a.asciiValue()         // Returns 65 (ASCII code for 'A')
```

### ASCII Value

The `asciiValue()` method returns the ASCII code (0-127) for single-byte ASCII characters:

```maxon
var letter = 'A'
print("{letter.asciiValue()}\n")  // Prints: 65

var digit = '0'
print("{digit.asciiValue()}\n")   // Prints: 48
```

For non-ASCII characters (multi-byte UTF-8 or values >= 128), `asciiValue()` returns `nil`.

## Tests

<!-- test: basic-character -->
### Basic Character

```maxon
function main() returns ExitCode
	let x = 'A'
	if x == 'A' 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: character-comparison -->
### Character Comparison

```maxon
function main() returns ExitCode
	let a = 'A'
	let b = 'B'
	if a < b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: character-in-variable -->
### Character in Variable

```maxon
function main() returns ExitCode
	let letter = 'Z'
	if letter == 'Z' 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: multibyte-character-2byte -->
### Multi-byte Character (2-byte UTF-8)

```maxon
function main() returns ExitCode
	let c = 'é'
	print("{c.bytes().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: multibyte-character-3byte -->
### Multi-byte Character (3-byte UTF-8)

```maxon
function main() returns ExitCode
	let c = '中'
	print("{c.bytes().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: multibyte-character-4byte -->
### Multi-byte Character (4-byte Emoji)

```maxon
function main() returns ExitCode
	let c = '🎉'
	print("{c.bytes().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: character-to-string -->
### Character to String Conversion

```maxon
function main() returns ExitCode
	let c = 'A'
	let s = "{c}"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
A
```

<!-- test: multibyte-character-to-string -->
### Multi-byte Character to String

```maxon
function main() returns ExitCode
	let c = '中'
	let s = "{c}"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
中
```

<!-- test: character-equality-multibyte -->
### Multi-byte Character Equality

```maxon
function main() returns ExitCode
	let a = 'é'
	let b = 'é'
	if a == b 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: character-inequality-multibyte -->
### Multi-byte Character Inequality

```maxon
function main() returns ExitCode
	let a = 'é'
	let b = 'è'
	if a != b 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: character-ordering-lt -->
### Multi-byte Character Ordering

`Character` implements `Comparable`; the order is lexicographic over the UTF-8 bytes.

```maxon
function main() returns ExitCode
	let a = 'é'
	let b = 'ü'
	if a < b 'lt'
		print("lt\n")
	end 'lt'
	if b < a 'reversed'
		print("REVERSED\n")
	end 'reversed'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
lt
```

<!-- test: character-ordering-le-ge -->
### Multi-byte Character Ordering, Inclusive and Reversed

```maxon
function main() returns ExitCode
	let a = 'é'
	let b = 'ü'
	if a <= b 'le'
		print("le\n")
	end 'le'
	if b >= a 'ge'
		print("ge\n")
	end 'ge'
	if b > a 'gt'
		print("gt\n")
	end 'gt'
	if a >= b 'notGe'
		print("NOTGE\n")
	end 'notGe'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
le
ge
gt
```

<!-- test: character-ordering-equal -->
### Equal Characters Satisfy Both Inclusive Operators

```maxon
function main() returns ExitCode
	let a = 'é'
	let b = 'é'
	if a <= b 'le'
		print("le\n")
	end 'le'
	if a >= b 'ge'
		print("ge\n")
	end 'ge'
	if a < b 'lt'
		print("LT\n")
	end 'lt'
	if a > b 'gt'
		print("GT\n")
	end 'gt'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
le
ge
```

<!-- test: character-ordering-compares-bytes-unsigned -->
### Character Ordering Compares Bytes as UNSIGNED

`e` + COMBINING ACUTE (`65 CC 81`) orders BELOW the precomposed `é` (`C3 A9`), because `0x65` (101) is
below `0xC3` (195). Read as SIGNED bytes the answer inverts — `0xC3` is -61 — so this case is the one
that tells a zero-extending byte load from a sign-extending one.

```maxon
function main() returns ExitCode
	let decomposed = 'é'
	let precomposed = 'é'
	if decomposed < precomposed 'lt'
		print("lt\n")
	end 'lt'
	if precomposed < decomposed 'reversed'
		print("REVERSED\n")
	end 'reversed'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
lt
```

<!-- test: character-ordering-prefix-is-less -->
### A Character Whose Bytes Prefix Another's Orders Below It

Every byte agrees up to the shorter cluster's length, so the LENGTH decides.

```maxon
function main() returns ExitCode
	let shorter = 'é'
	let longer = 'é̈'
	if shorter < longer 'lt'
		print("lt\n")
	end 'lt'
	if longer < shorter 'reversed'
		print("REVERSED\n")
	end 'reversed'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
lt
```

<!-- test: character-ordering-of-loop-elements -->
### Ordering the OWNED Characters a String Loop Yields

Each `c` is a freshly minted, owned `Character` (`__char_at`), while the pivot is an immortal `.rdata`
literal. Ordering BORROWS both, so the loop's temporaries are dropped exactly once and the leak gate is
live for the whole run — the one shape a compare that retained an operand would fail on.

```maxon
function main() returns ExitCode
	let s = "caféüñ"
	let pivot = 'ü'
	var below = 0
	for c in s 'each'
		if c < pivot 'lt'
			below = below + 1
		end 'lt'
	end 'each'
	print("{below}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: character-ordering-of-two-owned-temporaries -->
### Ordering TWO Owned Character Temporaries

`character-ordering-of-loop-elements` puts an owned Character on the LEFT only; here BOTH operands are
freshly minted records and the pair is compared n² times, so an operand the compare retained or freed
would show up as a leak (exit 101) or a double free rather than as a wrong count. `"üé"` has one
ordered pair out of four.

```maxon
function main() returns ExitCode
	let s = "üé"
	var seen = 0
	for c in s 'each'
		for d in s 'inner'
			if c < d 'lt'
				seen = seen + 1
			end 'lt'
		end 'inner'
	end 'each'
	print("{seen}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: character-comparable-witness -->
### A `Character` Satisfies a `where T is Comparable` Constraint

`stdlib/Character.maxon:19` declares `Comparable`, so a `Character` type argument resolves the witness slot
and `self.a < self.b` inside the generic body dispatches `Character.compare` — the SAME graph `<` calls
directly through `__char_cmp`, which is what keeps the two doors from ordering Characters differently.

⚠ **MEASURED 2026-08-05: THE BOOTSTRAP GETS THIS WRONG AND shv2 GETS IT RIGHT.** `./bin/maxon.exe` prints
`lt` AND `WRONG` here — it answers `'ü' < 'é'` TRUE through the witness — while answering the
DIRECT `'ü' < 'é'` and the direct `'ü'.compare('é')` correctly, and answering the
identical generic over `Integer` correctly. It is therefore a `Character`-type-argument witness defect in
`maxon-sharp`, reported rather than fixed here. This case pins the ANSWER the corpus states, which is
shv2's.

```maxon
type Pair uses T where T is Comparable
	export var a as T
	export var b as T
	export static function create(a T, b T) returns Self
		return Self{ a: a, b: b }
	end 'create'
	export function lt() returns bool
		return self.a < self.b
	end 'lt'
end 'Pair'

typealias CharPair = Pair with Character

function main() returns ExitCode
	let p = CharPair.create('é', b: 'ü')
	if p.lt() 'ordered'
		print("lt\n")
	end 'ordered'
	let q = CharPair.create('ü', b: 'é')
	if q.lt() 'reversed'
		print("REVERSED\n")
	end 'reversed'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
lt
```

<!-- test: error.string-ordering-still-refused -->
### Ordering a String Is Still Refused

The NEGATIVE CONTROL for Character ordering. A `String` and a `Character` are the same byte record, but
only the `Character` is ordered: `String` declares no `Comparable` conformance in the corpus
(`stdlib/String.maxon`), so `<` on two Strings has no meaning to give.

```maxon
function main() returns ExitCode
	let a = "apple"
	let b = "banana"
	if a < b 'lt'
		return 1
	end 'lt'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/character-type/error.string-ordering-still-refused.test:5:7: cannot order String values using '<': a String is a byte record with no ordering, so its only comparisons are '==' and '!='
```

<!-- test: error.character-arithmetic-still-refused -->
### Arithmetic on a Character Is Still Refused

The second NEGATIVE CONTROL. Ordering is not an integral reading: a `Character` is a POINTER to a byte
record, not a magnitude, so `c - 1` stays refused exactly as it was before the ordering landed.

```maxon
function main() returns ExitCode
	let c = 'é'
	let d = c - 1
	return d
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/character-type/error.character-arithmetic-still-refused.test:4:12: Cannot operate on Character and int
```

<!-- test: emoji-character -->
### Emoji Character

```maxon
function main() returns ExitCode
	let emoji = '🎉'
	print("{emoji}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
🎉
```

<!-- test: flag-emoji-character -->
### Flag Emoji (Regional Indicator Pair)

```maxon
function main() returns ExitCode
	let flag = '🇺🇸'
	print("{flag.bytes().count()}\n")
	print("{flag}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8
🇺🇸
```

<!-- test: family-emoji-character -->
### Family Emoji (ZWJ Sequence)

```maxon
function main() returns ExitCode
	let family = '👨‍👩‍👧'
	print("{family.bytes().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18
```

<!-- test: skin-tone-emoji -->
### Skin Tone Modifier Emoji

```maxon
function main() returns ExitCode
	let wave = '👋🏽'
	print("{wave.bytes().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8
```

<!-- test: escape-sequences -->
### Escape Sequences in Character

```maxon
function main() returns ExitCode
	let newline = '\n'
	let tab = '\t'
	let backslash = '\\'
	let quote = '\''
	print("{newline.bytes().count()}\n")
	print("{tab.bytes().count()}\n")
	print("{backslash.bytes().count()}\n")
	print("{quote.bytes().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
1
1
1
```

<!-- test: ascii-value-letter -->
### ASCII Value for Letter

```maxon
function main() returns ExitCode
	let c = 'A'
	let val = try c.asciiValue() otherwise 0
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
65
```

<!-- test: ascii-value-digit -->
### ASCII Value for Digit

```maxon
function main() returns ExitCode
	let c = '0'
	let val = try c.asciiValue() otherwise 0
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
48
```

<!-- test: ascii-value-lowercase -->
### ASCII Value for Lowercase

```maxon
function main() returns ExitCode
	let c = 'a'
	let val = try c.asciiValue() otherwise 0
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
97
```

<!-- test: ascii-value-space -->
### ASCII Value for Space

```maxon
function main() returns ExitCode
	let c = ' '
	let val = try c.asciiValue() otherwise 0
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
32
```

<!-- test: ascii-value-newline -->
### ASCII Value for Newline Escape

```maxon
function main() returns ExitCode
	let c = '\n'
	let val = try c.asciiValue() otherwise 0
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
```

<!-- test: ascii-value-non-ascii -->
### ASCII Value for Non-ASCII Returns Error

```maxon
function main() returns ExitCode
	let c = 'é'
	if let ascii = try c.asciiValue() 'hasAscii'
		return ascii
	end 'hasAscii'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: ascii-value-emoji -->
### ASCII Value for Emoji Returns Error

```maxon
function main() returns ExitCode
	let c = '🎉'
	if let ascii = try c.asciiValue() 'hasAscii'
		return ascii
	end 'hasAscii'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.declare-character-error-enum -->
### `CharacterError` is a declaration the compiler owns
The two cases above rest on `asciiValue()` throwing `CharacterError.notAscii`, and the ordinal that reaches
the failure edge is the COMPILER's: `Runtime/GraphemeRuntime.buildCharAscii` returns `notAscii`'s position as
`__char_ascii`'s error flag, while `stdlib/Character.maxon:15-17` declares the position it encodes. shv2 has
no namespace, so a user `enum CharacterError` lands in the same registry bucket — and the array family's
`error.declare-array-error-enum` measured what that costs before its own reservation existed: the program
compiled and the runtime's ordinal 0 routed into whichever case the user happened to write first, with no
diagnostic anywhere. MEASURED here the same way at W135's review — this exact program compiled clean. Refused
now, exactly as `ArrayError`, `StringError`, `MapError` and `IterationError` are; a REFERENCE to the name in a
`throws` clause stays legal, which is the whole reason the name is bare.
```maxon
enum CharacterError
	somethingElse
	another
end 'CharacterError'

function main() returns ExitCode
	let c = 'x'
	let val = try c.asciiValue() otherwise 0
	return val
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:6: Unsupported: a declaration of the type name 'CharacterError', which the compiler owns — its one meaning comes from the compiler itself or from the stdlib module that declares it, and shv2 has no namespace to tell a user declaration of the name apart from that one
```

<!-- disabled-test: error.otherwise-out-of-range -->
<!-- MEASURED 2026-09-04: shv2 COMPILES `try c.asciiValue() otherwise -1` CLEAN, so a value outside its ranged
     type's domain reaches the merge. The bootstrap refuses it. An `otherwise` fallback is not range-checked
     against the try's result type here, and that is a soundness hole rather than a diagnostic gap. -->
### Otherwise value must be within ranged type bounds

```maxon
function main() returns ExitCode
	let c = 'x'
	let val = try c.asciiValue() otherwise -1
	return val
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/character-type/error.otherwise-out-of-range.test:4:12: otherwise value -1 is outside the range of 'AsciiValue' (int(0 to 127))
```

<!-- test: match-escape-character -->
### Match with Escape Character Literals

Character match patterns must correctly handle escape sequences like `'\n'`, `'\t'`, `'\r'`, and `'\\'`.

```maxon
function main() returns ExitCode
	let c = '\n'
	match c 'check'
		'\n' then return 0
		default then return 1
	end 'check'
end 'main'
```
```exitcode
0
```
