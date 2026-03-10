#!/usr/bin/env python3
"""
Generate _unicode_category.maxon from DerivedGeneralCategory.txt.

Reads Unicode General Category data and produces a page-based dispatch function
in Maxon that maps codepoints to their General Category integer code.

Usage:
    python3 tools/unicode/generate_unicode_category.py
"""

import re
import os
from collections import defaultdict

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

    # Sort by start codepoint
    entries.sort(key=lambda x: x[0])
    return entries


def split_ranges_by_page(entries):
    """Split ranges at page boundaries and group by page.

    A page is codepoint >> 8. Ranges that span multiple pages are split.
    Returns dict: page_number -> list of (start, end, category_code).
    """
    pages = defaultdict(list)

    for start, end, cat in entries:
        code = CATEGORY_CODES[cat]
        s = start
        while s <= end:
            page = s >> 8
            page_end = (page << 8) | 0xFF
            chunk_end = min(end, page_end)
            pages[page].append((s, chunk_end, code))
            s = chunk_end + 1

    return pages


def merge_adjacent_ranges(ranges):
    """Merge adjacent ranges with the same category code within a page."""
    if not ranges:
        return ranges

    # Sort by start
    ranges.sort(key=lambda x: x[0])
    merged = [ranges[0]]

    for start, end, code in ranges[1:]:
        prev_start, prev_end, prev_code = merged[-1]
        if code == prev_code and start == prev_end + 1:
            merged[-1] = (prev_start, end, code)
        else:
            merged.append((start, end, code))

    return merged


def generate_maxon(entries, output_path):
    """Generate the Maxon source file."""
    pages = split_ranges_by_page(entries)

    # Merge adjacent ranges within each page
    for page in pages:
        pages[page] = merge_adjacent_ranges(pages[page])

    lines = []

    # Header
    lines.append("// Auto-generated Unicode General Category lookup table")
    lines.append("// Source: DerivedGeneralCategory-16.0.0.txt")
    lines.append("// Do not edit manually - regenerate with tools/unicode/generate_unicode_category.py")
    lines.append("")
    lines.append("typealias GeneralCategory = int(0 to 30)")
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
        comment_parts = "|".join(cats)
        lines.append(f"let {mask_name} = {val}  // {comment_parts}")
    lines.append("")

    # Main function
    lines.append("export function unicodeGeneralCategory(cp Codepoint) returns GeneralCategory")
    lines.append("  var page = cp shr 8")

    sorted_pages = sorted(pages.keys())

    for page_num in sorted_pages:
        ranges = pages[page_num]
        page_label = f"page_0x{page_num:02X}"
        lines.append(f"  if page == {page_num} '{page_label}'")

        for start, end, code in ranges:
            label = f"r_{start:04X}"
            if start == end:
                lines.append(f"    if cp == 0x{start:04X} '{label}'")
            else:
                lines.append(f"    if cp >= 0x{start:04X} and cp <= 0x{end:04X} '{label}'")
            lines.append(f"      return {code}")
            lines.append(f"    end '{label}'")

        lines.append(f"    return 0")
        lines.append(f"  end '{page_label}'")

    lines.append("  return 0")
    lines.append("end 'unicodeGeneralCategory'")
    lines.append("")

    with open(output_path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))


def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    repo_root = os.path.dirname(os.path.dirname(script_dir))

    input_path = os.path.join(script_dir, "DerivedGeneralCategory.txt")
    output_path = os.path.join(repo_root, "stdlib", "helpers", "string", "_unicode_category.maxon")

    if not os.path.exists(input_path):
        print(f"Error: {input_path} not found")
        return 1

    entries = parse_derived_general_category(input_path)
    print(f"Parsed {len(entries)} ranges (excluding Cn)")

    generate_maxon(entries, output_path)

    # Print some stats
    pages = split_ranges_by_page(entries)
    total_ranges = sum(len(v) for v in pages.values())
    print(f"Generated {len(pages)} pages with {total_ranges} total ranges")
    print(f"Output: {output_path}")
    return 0


if __name__ == "__main__":
    exit(main())
