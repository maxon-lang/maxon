---
feature: grapheme-clusters
status: experimental
keywords: [grapheme, egc, unicode, uax29, segmentation]
category: unicode
---

# Grapheme Clusters

## Documentation

Maxon provides grapheme cluster support through the `character` type and string iteration.

### Grapheme Boundary Detection

The `stdlib/unicode/grapheme` module provides functions for grapheme segmentation:

```maxon
import unicode/grapheme

// Get grapheme break property for a codepoint
var prop = graphemeBreakProperty(0x1F600)  // Returns GBP_Extended_Pictographic info

// Check if there's a boundary between two positions
var boundary = is_grapheme_boundary(data, len, pos)

// Find next/previous grapheme boundary
var next = nextGraphemeBoundary(data, len, offset)
var prev = prevGraphemeBoundary(data, len, offset)
```

### String Integration

Strings automatically use grapheme clusters for iteration:

```maxon
var s = "👨‍👩‍👧"  // Family emoji - 1 grapheme, 18 bytes, 5 codepoints
print("{s.count()}\n")         // 1 (grapheme count)
print("{s.bytes().count()}\n") // 18 (byte count)

for c in s 'loop'
	// Iterates once, c is the family emoji
end 'loop'
```

## Tests

<!-- test: grapheme-boundary-ascii -->
### Grapheme Boundary - ASCII

Each ASCII character is its own grapheme:

```maxon
function main() returns ExitCode
	var s = "abc"
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: grapheme-boundary-multibyte -->
### Grapheme Boundary - Multi-byte UTF-8

Multi-byte characters are single graphemes:

```maxon
function main() returns ExitCode
	var s = "中文"  // 2 CJK characters
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: grapheme-boundary-emoji -->
### Grapheme Boundary - Basic Emoji

Basic emoji are single graphemes:

```maxon
function main() returns ExitCode
	var s = "🎉🎊🎁"  // 3 emoji
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: grapheme-boundary-flag -->
### Grapheme Boundary - Flag Emoji (Regional Indicators)

Flag emoji (pairs of regional indicators) are single graphemes:

```maxon
function main() returns ExitCode
	var s = "🇺🇸"  // US flag (2 regional indicators = 1 grapheme)
	print("{s.count()}\n")
	print("{s.bytes().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
8
```

<!-- test: grapheme-boundary-multiple-flags -->
### Grapheme Boundary - Multiple Flags

Multiple flags are separate graphemes:

```maxon
function main() returns ExitCode
	var s = "🇺🇸🇬🇧🇫🇷"  // 3 flags
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: grapheme-boundary-skin-tone -->
### Grapheme Boundary - Skin Tone Modifier

Emoji with skin tone modifiers are single graphemes:

```maxon
function main() returns ExitCode
	var s = "👋🏽"  // Wave + medium skin tone
	print("{s.count()}\n")
	print("{s.bytes().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
8
```

<!-- test: grapheme-boundary-zwj-family -->
### Grapheme Boundary - ZWJ Family Sequence

Family emoji (ZWJ sequences) are single graphemes:

```maxon
function main() returns ExitCode
	var s = "👨‍👩‍👧"  // Man + ZWJ + Woman + ZWJ + Girl
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: grapheme-boundary-zwj-profession -->
### Grapheme Boundary - ZWJ Profession Sequence

Professional emoji (ZWJ sequences) are single graphemes:

```maxon
function main() returns ExitCode
	var s = "👨‍💻"  // Man + ZWJ + Computer = Man Technologist
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: grapheme-boundary-crlf -->
### Grapheme Boundary - CRLF

CR+LF is a single grapheme (GB3):

```maxon
function main() returns ExitCode
	var s = "\r\n"
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: grapheme-boundary-mixed -->
### Grapheme Boundary - Mixed Content

Mixed ASCII, emoji, and CJK:

```maxon
function main() returns ExitCode
	var s = "Hi🎉中"  // H, i, party, 中 = 4 graphemes
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: grapheme-break-property-ascii -->
### Grapheme Break Property - ASCII

```maxon
function main() returns ExitCode
	var prop = graphemeBreakProperty(65)  // 'A'
	print("{prop}\n")  // GBP_Other = 0
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: grapheme-break-property-cr -->
### Grapheme Break Property - CR

```maxon
function main() returns ExitCode
	var prop = graphemeBreakProperty(13)  // CR
	print("{prop}\n")  // GBP_CR = 1
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: grapheme-break-property-lf -->
### Grapheme Break Property - LF

```maxon
function main() returns ExitCode
	var prop = graphemeBreakProperty(10)  // LF
	print("{prop}\n")  // GBP_LF = 2
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: grapheme-break-property-zwj -->
### Grapheme Break Property - ZWJ

```maxon
function main() returns ExitCode
	var prop = graphemeBreakProperty(8205)  // ZWJ U+200D
	print("{prop}\n")  // GBP_ZWJ = 5
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: grapheme-break-property-regional -->
### Grapheme Break Property - Regional Indicator

```maxon
function main() returns ExitCode
	var prop = graphemeBreakProperty(127482)  // Regional Indicator U
	print("{prop}\n")  // GBP_Regional_Indicator = 6
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

<!-- test: is-extended-pictographic -->
### Extended Pictographic Check

```maxon
function main() returns ExitCode
	if isExtendedPictographic(128512) 'c1'
		print("{1}\n")  // 😀
	end 'c1'
	if isExtendedPictographic(65) 'c2'
		print("{0}\n")      // 'A'
	end 'c2'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: grapheme-boundary-combining-accent -->
### Grapheme Boundary - Combining Accent

A base character followed by a combining acute accent (U+0301) forms one grapheme:

```maxon
function main() returns ExitCode
	var s = "e\u0301"  // e + combining acute = é (1 grapheme, 3 bytes)
	print("{s.count()}\n")
	print("{s.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
3
```

<!-- test: grapheme-boundary-multiple-combiners -->
### Grapheme Boundary - Multiple Combining Marks

A base character with multiple combining marks forms one grapheme:

```maxon
function main() returns ExitCode
	var s = "a\u0308\u0301"  // a + diaeresis + acute = 1 grapheme
	print("{s.count()}\n")
	print("{s.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
5
```

<!-- test: grapheme-boundary-combining-iteration -->
### Grapheme Boundary - Combining Mark Iteration

Iterating a string with combining marks yields grapheme clusters:

```maxon
function main() returns ExitCode
	var s = "caf\u0065\u0301"  // c, a, f, e+combining accent = 4 graphemes
	var count = 0
	for _ in s 'loop'
		count = count + 1
	end 'loop'
	print("{count}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: grapheme-boundary-odd-ri -->
### Grapheme Boundary - Odd Number of Regional Indicators

Three regional indicators: first two pair into a flag, third is standalone = 2 graphemes:

```maxon
function main() returns ExitCode
	var s = "🇺🇸🇬"  // US flag (2 RIs) + standalone G (1 RI) = 2 graphemes
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: grapheme-boundary-four-ri -->
### Grapheme Boundary - Four Regional Indicators

Four regional indicators pair into two flags = 2 graphemes:

```maxon
function main() returns ExitCode
	var s = "🇺🇸🇬🇧"  // US flag + GB flag = 2 graphemes
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```
