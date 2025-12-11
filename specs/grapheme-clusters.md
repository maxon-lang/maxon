---
feature: grapheme-clusters
status: experimental
keywords: [grapheme, egc, unicode, uax29, segmentation]
category: unicode
---

# Grapheme Clusters

## Developer Notes

Implementation of Unicode Text Segmentation (UAX #29) for grapheme cluster boundary detection.

### Overview

A grapheme cluster represents what a user perceives as a single character. This can be:
- A single ASCII character: `'A'`
- A multi-byte UTF-8 character: `'é'` (2 bytes), `'中'` (3 bytes), `'🎉'` (4 bytes)
- Multiple codepoints forming one visual unit:
  - Flag emoji: `'🇺🇸'` (2 regional indicators)
  - Family emoji: `'👨‍👩‍👧'` (multiple codepoints joined with ZWJ)
  - Skin tone: `'👋🏽'` (base + modifier)
  - Combining marks: `'é'` as `e` + combining acute accent

### Grapheme_Cluster_Break Property Values

From Unicode Standard Annex #29:

| Value | Code | Description |
|-------|------|-------------|
| Other | 0 | Default, all other characters |
| CR | 1 | Carriage Return (U+000D) |
| LF | 2 | Line Feed (U+000A) |
| Control | 3 | Control characters, format characters |
| Extend | 4 | Combining marks, modifiers |
| ZWJ | 5 | Zero Width Joiner (U+200D) |
| Regional_Indicator | 6 | Regional indicator symbols (flags) |
| Prepend | 7 | Prepended concatenation marks |
| SpacingMark | 8 | Spacing combining marks |
| L | 9 | Hangul leading jamo |
| V | 10 | Hangul vowel jamo |
| T | 11 | Hangul trailing jamo |
| LV | 12 | Hangul LV syllable |
| LVT | 13 | Hangul LVT syllable |

### UAX #29 Grapheme Cluster Break Rules

| Rule | Description |
|------|-------------|
| GB1 | Break at start of text |
| GB2 | Break at end of text |
| GB3 | Do not break between CR and LF (CR × LF) |
| GB4 | Break after controls (Control/CR/LF ÷) |
| GB5 | Break before controls (÷ Control/CR/LF) |
| GB6 | Do not break Hangul syllables (L × L/V/LV/LVT) |
| GB7 | Do not break Hangul syllables (LV/V × V/T) |
| GB8 | Do not break Hangul syllables (LVT/T × T) |
| GB9 | Do not break before Extend/ZWJ (× Extend/ZWJ) |
| GB9a | Do not break before SpacingMark (× SpacingMark) |
| GB9b | Do not break after Prepend (Prepend ×) |
| GB11 | Do not break within emoji ZWJ sequences |
| GB12-13 | Do not break within emoji flag sequences (pairs of RI) |
| GB999 | Otherwise, break everywhere (÷ Any) |

### Extended_Pictographic Property

Required for GB11 (emoji ZWJ sequences). Characters with this property can form emoji sequences when joined with ZWJ.

### Implementation Files

- `stdlib/unicode/grapheme_data.maxon` - Unicode property lookup tables
- `stdlib/unicode/grapheme.maxon` - Boundary detection algorithm

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
printInt(s.count())         // 1 (grapheme count)
printInt(s.bytes().count()) // 18 (byte count)

for c in s 'loop'
    // Iterates once, c is the family emoji
end 'loop'
```

## Tests

<!-- test: grapheme-boundary-ascii -->
### Grapheme Boundary - ASCII

Each ASCII character is its own grapheme:

```maxon
function main() returns int
    var s = "abc"
    printInt(s.count())
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
function main() returns int
    var s = "中文"  // 2 CJK characters
    printInt(s.count())
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
function main() returns int
    var s = "🎉🎊🎁"  // 3 emoji
    printInt(s.count())
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
function main() returns int
    var s = "🇺🇸"  // US flag (2 regional indicators = 1 grapheme)
    printInt(s.count())
    printInt(s.bytes().count())
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
function main() returns int
    var s = "🇺🇸🇬🇧🇫🇷"  // 3 flags
    printInt(s.count())
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
function main() returns int
    var s = "👋🏽"  // Wave + medium skin tone
    printInt(s.count())
    printInt(s.bytes().count())
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
function main() returns int
    var s = "👨‍👩‍👧"  // Man + ZWJ + Woman + ZWJ + Girl
    printInt(s.count())
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
function main() returns int
    var s = "👨‍💻"  // Man + ZWJ + Computer = Man Technologist
    printInt(s.count())
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
function main() returns int
    var s = "\r\n"
    printInt(s.count())
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
function main() returns int
    var s = "Hi🎉中"  // H, i, party, 中 = 4 graphemes
    printInt(s.count())
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
function main() returns int
    var prop = graphemeBreakProperty(65)  // 'A'
    printInt(prop)  // GBP_Other = 0
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
function main() returns int
    var prop = graphemeBreakProperty(13)  // CR
    printInt(prop)  // GBP_CR = 1
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
function main() returns int
    var prop = graphemeBreakProperty(10)  // LF
    printInt(prop)  // GBP_LF = 2
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
function main() returns int
    var prop = graphemeBreakProperty(8205)  // ZWJ U+200D
    printInt(prop)  // GBP_ZWJ = 5
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
function main() returns int
    var prop = graphemeBreakProperty(127482)  // Regional Indicator U
    printInt(prop)  // GBP_Regional_Indicator = 6
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
function main() returns int
    if isExtendedPictographic(128512) 'c1'
        printInt(1)  // 😀
    end 'c1'
    if isExtendedPictographic(65) 'c2'
        printInt(0)      // 'A'
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
