#!/usr/bin/env python3
"""
Generate binary Unicode category lookup tables and a compact Maxon source file.

Produces:
  - stdlib/helpers/string/ucd_bmp.bin   (65536 bytes, flat BMP table)
  - stdlib/helpers/string/ucd_supp.bin  (N*8 bytes, sorted supplementary ranges)
  - stdlib/helpers/string/_unicode_category.maxon (~65 lines)

Usage:
    python3 tools/unicode/generate_unicode_category.py
"""

import re
import os
import struct

# Category name -> integer constant (skip Cn=0 which is the default)
CATEGORY_CODES = {
    "Lu": 1, "Ll": 2, "Lt": 3, "Lm": 4, "Lo": 5,
    "Mn": 6, "Mc": 7, "Me": 8,
    "Nd": 9, "Nl": 10, "No": 11,
    "Pc": 12, "Pd": 13, "Pe": 14, "Pf": 15, "Pi": 16, "Po": 17, "Ps": 18,
    "Sc": 19, "Sk": 20, "Sm": 21, "So": 22,
    "Zs": 23, "Zl": 24, "Zp": 25,
    "Cc": 26, "Cf": 27, "Co": 28, "Cs": 29,
}

# Bitmask definitions: name -> list of category names
MASK_DEFS = {
    "_GC_MASK_LETTERS":       ["Lu", "Ll", "Lt", "Lm", "Lo", "Mn", "Mc", "Me"],
    "_GC_MASK_LOWERCASE":     ["Ll"],
    "_GC_MASK_UPPERCASE":     ["Lu", "Lt"],
    "_GC_MASK_DIGITS":        ["Nd"],
    "_GC_MASK_ALPHANUMERICS": ["Lu", "Ll", "Lt", "Lm", "Lo", "Mn", "Mc", "Me", "Nd", "Nl", "No"],
    "_GC_MASK_PUNCTUATION":   ["Pc", "Pd", "Pe", "Pf", "Pi", "Po", "Ps"],
    "_GC_MASK_SYMBOLS":       ["Sc", "Sk", "Sm", "So"],
    "_GC_MASK_CONTROL":       ["Cc", "Cf"],
    "_GC_MASK_WHITESPACE_ZS": ["Zs"],
    "_GC_MASK_LINE_PARA_SEP": ["Zl", "Zp"],
}


def compute_mask(categories):
    """Compute bitmask integer from list of category names."""
    mask = 0
    for cat in categories:
        mask |= (1 << CATEGORY_CODES[cat])
    return mask


def parse_derived_general_category(filepath):
    """Parse DerivedGeneralCategory.txt, returning list of (start, end, category) tuples.

    Skips Cn (Unassigned) entries.
    """
    entries = []
    line_re = re.compile(r'^([0-9A-Fa-f]+)(?:\.\.([0-9A-Fa-f]+))?\s*;\s*(\w+)')

    with open(filepath, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            m = line_re.match(line)
            if not m:
                continue
            start = int(m.group(1), 16)
            end = int(m.group(2), 16) if m.group(2) else start
            cat = m.group(3)
            if cat == "Cn":
                continue
            if cat not in CATEGORY_CODES:
                continue
            entries.append((start, end, cat))

    entries.sort(key=lambda x: x[0])
    return entries


def build_bmp_table(entries):
    """Build a flat 65536-byte BMP lookup table."""
    table = bytearray(65536)
    for start, end, cat in entries:
        code = CATEGORY_CODES[cat]
        # Clamp to BMP range
        s = max(start, 0)
        e = min(end, 0xFFFF)
        if s > 0xFFFF:
            continue
        for cp in range(s, e + 1):
            table[cp] = code
    return table


def build_supp_table(entries):
    """Build sorted supplementary range table for codepoints >= 0x10000.

    Each entry is a little-endian uint64 packed as:
      bits  0-20: start codepoint (21 bits)
      bits 21-41: end codepoint (21 bits)
      bits 42-46: category code (5 bits)
    """
    supp_entries = []
    for start, end, cat in entries:
        code = CATEGORY_CODES[cat]
        # Only supplementary codepoints
        s = max(start, 0x10000)
        e = end
        if e < 0x10000:
            continue
        # Pack into uint64
        packed = (s & 0x1FFFFF) | ((e & 0x1FFFFF) << 21) | ((code & 0x1F) << 42)
        supp_entries.append((s, packed))

    # Sort by start codepoint
    supp_entries.sort(key=lambda x: x[0])

    # Pack into binary
    data = bytearray()
    for _, packed in supp_entries:
        data.extend(struct.pack('<Q', packed))

    return data, len(supp_entries)


def generate_maxon(supp_count, output_path):
    """Generate the compact Maxon source file."""
    lines = []

    lines.append("// Auto-generated Unicode General Category lookup table")
    lines.append("// Source: DerivedGeneralCategory-16.0.0.txt")
    lines.append(f"// Binary data: ucd_bmp.bin (65536 bytes), ucd_supp.bin ({supp_count} entries)")
    lines.append("// Do not edit manually - regenerate with tools/unicode/generate_unicode_category.py")
    lines.append("")
    lines.append("typealias GeneralCategory = int(0 to 29)")
    lines.append("")

    # Category constants
    lines.append("// Category constants")
    for cat, code in sorted(CATEGORY_CODES.items(), key=lambda x: x[1]):
        lines.append(f"let _GC_{cat} = {code}")
    lines.append("")

    # Bitmask constants
    lines.append("// Category bitmask helpers for CharacterSet")
    for mask_name, cats in MASK_DEFS.items():
        val = compute_mask(cats)
        lines.append(f"let {mask_name} = {val}")
    lines.append("")

    lines.append(f"let _UCD_SUPP_COUNT = {supp_count}")
    lines.append("")

    # Main function with binary-search lookup
    lines.append("export function unicodeGeneralCategory(cp Codepoint) returns GeneralCategory")
    lines.append("  if cp < 65536 'bmp'")
    lines.append('    return __Builtins.ucdByteAt("__ucd_bmp", cp)')
    lines.append("  end 'bmp'")
    lines.append("  var lo = 0")
    lines.append("  var hi = _UCD_SUPP_COUNT - 1")
    lines.append("  while lo <= hi 'bsearch'")
    lines.append("    var mid = (lo + hi) shr 1")
    lines.append('    var entry = __Builtins.ucdI64At("__ucd_supp", mid)')
    lines.append("    var rangeStart = entry and 2097151")
    lines.append("    var rangeEnd = (entry shr 21) and 2097151")
    lines.append("    if cp < rangeStart 'left'")
    lines.append("      hi = mid - 1")
    lines.append("    end 'left' else 'not_left'")
    lines.append("      if cp > rangeEnd 'right'")
    lines.append("        lo = mid + 1")
    lines.append("      end 'right' else 'found'")
    lines.append("        return (entry shr 42) and 31")
    lines.append("      end 'found'")
    lines.append("    end 'not_left'")
    lines.append("  end 'bsearch'")
    lines.append("  return 0")
    lines.append("end 'unicodeGeneralCategory'")
    lines.append("")

    with open(output_path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))

    return len(lines)


def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    repo_root = os.path.dirname(os.path.dirname(script_dir))

    input_path = os.path.join(script_dir, "DerivedGeneralCategory.txt")
    output_dir = os.path.join(repo_root, "stdlib", "helpers", "string")
    bmp_path = os.path.join(output_dir, "ucd_bmp.bin")
    supp_path = os.path.join(output_dir, "ucd_supp.bin")
    maxon_path = os.path.join(output_dir, "_unicode_category.maxon")

    if not os.path.exists(input_path):
        print(f"Error: {input_path} not found")
        return 1

    entries = parse_derived_general_category(input_path)
    print(f"Parsed {len(entries)} ranges (excluding Cn)")

    # Count BMP and supplementary ranges
    bmp_ranges = sum(1 for s, e, _ in entries if s <= 0xFFFF)
    supp_ranges = sum(1 for s, e, _ in entries if e >= 0x10000)
    print(f"  BMP ranges: {bmp_ranges}")
    print(f"  Supplementary ranges: {supp_ranges}")

    # Build BMP table
    bmp_table = build_bmp_table(entries)
    with open(bmp_path, "wb") as f:
        f.write(bmp_table)
    print(f"Wrote {len(bmp_table)} bytes to {bmp_path}")

    # Count non-zero BMP entries
    bmp_assigned = sum(1 for b in bmp_table if b != 0)
    print(f"  BMP codepoints with category: {bmp_assigned}")

    # Build supplementary table
    supp_data, supp_count = build_supp_table(entries)
    with open(supp_path, "wb") as f:
        f.write(supp_data)
    print(f"Wrote {len(supp_data)} bytes ({supp_count} entries) to {supp_path}")

    # Generate Maxon source
    line_count = generate_maxon(supp_count, maxon_path)
    print(f"Wrote {line_count} lines to {maxon_path}")

    # Verify packing round-trip for first few supplementary entries
    supp_entries_raw = [(s, e, c) for s, e, c in entries if s >= 0x10000]
    if supp_entries_raw:
        first = supp_entries_raw[0]
        s, e, cat = first
        code = CATEGORY_CODES[cat]
        packed = (s & 0x1FFFFF) | ((e & 0x1FFFFF) << 21) | ((code & 0x1F) << 42)
        rt_start = packed & 0x1FFFFF
        rt_end = (packed >> 21) & 0x1FFFFF
        rt_code = (packed >> 42) & 0x1F
        assert rt_start == s, f"Round-trip failed for start: {rt_start:#x} != {s:#x}"
        assert rt_end == e, f"Round-trip failed for end: {rt_end:#x} != {e:#x}"
        assert rt_code == code, f"Round-trip failed for code: {rt_code} != {code}"
        print(f"Packing round-trip verified (first entry: U+{s:04X}..U+{e:04X} = {cat})")

    return 0


if __name__ == "__main__":
    exit(main())
