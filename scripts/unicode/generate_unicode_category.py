#!/usr/bin/env python3
"""
Generate the Unicode General Category lookup tables and the Maxon source that reads them.

Produces:
  - stdlib/helpers/string/ucd_bmp.bin   (65536 bytes, flat BMP table)
  - stdlib/helpers/string/ucd_supp.bin  (8 bytes per sorted supplementary range)
  - stdlib/helpers/string/unicodeCategory.maxon

Usage:
    python3 scripts/unicode/generate_unicode_category.py           # rewrite the outputs
    python3 scripts/unicode/generate_unicode_category.py --check   # write nothing; exit 1 when they drift
"""

import argparse
import os
import re
import struct
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "lib"))

import generated_files

UNASSIGNED_CATEGORY = "Cn"
UNASSIGNED_CODE = 0

CATEGORY_CODES = {
    "Lu": 1, "Ll": 2, "Lt": 3, "Lm": 4, "Lo": 5,
    "Mn": 6, "Mc": 7, "Me": 8,
    "Nd": 9, "Nl": 10, "No": 11,
    "Pc": 12, "Pd": 13, "Pe": 14, "Pf": 15, "Pi": 16, "Po": 17, "Ps": 18,
    "Sc": 19, "Sk": 20, "Sm": 21, "So": 22,
    "Zs": 23, "Zl": 24, "Zp": 25,
    "Cc": 26, "Cf": 27, "Co": 28, "Cs": 29,
}

MASK_DEFS = {
    "GC_MASK_LETTERS":       ["Lu", "Ll", "Lt", "Lm", "Lo", "Mn", "Mc", "Me"],
    "GC_MASK_LOWERCASE":     ["Ll"],
    "GC_MASK_UPPERCASE":     ["Lu", "Lt"],
    "GC_MASK_DIGITS":        ["Nd"],
    "GC_MASK_ALPHANUMERICS": ["Lu", "Ll", "Lt", "Lm", "Lo", "Mn", "Mc", "Me", "Nd", "Nl", "No"],
    "GC_MASK_PUNCTUATION":   ["Pc", "Pd", "Pe", "Pf", "Pi", "Po", "Ps"],
    "GC_MASK_SYMBOLS":       ["Sc", "Sk", "Sm", "So"],
    "GC_MASK_CONTROL":       ["Cc", "Cf"],
    "GC_MASK_WHITESPACE_ZS": ["Zs"],
    "GC_MASK_LINE_PARA_SEP": ["Zl", "Zp"],
}

BMP_SIZE = 0x10000
MAX_CODEPOINT = 0x10FFFF

CODEPOINT_BITS = 21
CATEGORY_BITS = 5
CODEPOINT_MASK = (1 << CODEPOINT_BITS) - 1
CATEGORY_MASK = (1 << CATEGORY_BITS) - 1
END_SHIFT = CODEPOINT_BITS
CATEGORY_SHIFT = 2 * CODEPOINT_BITS
SUPP_ENTRY_FORMAT = "<Q"

SOURCE_FILE = "DerivedGeneralCategory.txt"
SOURCE_HEADER_PREFIX = "# "
OUTPUT_DIRECTORY = ("stdlib", "helpers", "string")
BMP_FILE = "ucd_bmp.bin"
SUPP_FILE = "ucd_supp.bin"
MAXON_FILE = "unicodeCategory.maxon"
GENERATOR = "scripts/unicode/generate_unicode_category.py"

ENTRY_LINE = re.compile(r"^([0-9A-Fa-f]+)(?:\.\.([0-9A-Fa-f]+))?\s*;\s*(\w+)\s*(?:#.*)?$")

MAXON_TEMPLATE = """\
// Auto-generated Unicode General Category lookup table
// Source: {source_name}
// Binary data: {bmp_file} ({bmp_size} bytes), {supp_file} ({supp_count} entries)
// Do not edit manually - regenerate with {generator}

public typealias GeneralCategory = int({unassigned} to {max_category})

// Category bitmask helpers for CharacterSet
{masks}

let UCD_SUPP_COUNT = {supp_count}

public function unicodeGeneralCategory(cp Codepoint) returns GeneralCategory
	if cp < {bmp_size} 'bmp'
		return __Builtins.ucdByteAt("__ucd_bmp", offset: cp) as GeneralCategory
	end 'bmp'

	let target = cp as FullRangeInt
	var lo = 0 as FullRangeInt
	var hi = (UCD_SUPP_COUNT - 1) as FullRangeInt

	while lo <= hi 'bsearch'
		let mid = (lo + hi) shr 1
		let entry = __Builtins.ucdI64At("__ucd_supp", index: mid)
		let rangeStart = entry and {codepoint_mask}
		let rangeEnd = (entry shr {end_shift}) and {codepoint_mask}

		if target < rangeStart 'left'
			hi = mid - 1
		end 'left' else 'not_left'
			if target > rangeEnd 'right'
				lo = mid + 1
			end 'right' else 'found'
				return ((entry shr {category_shift}) and {category_mask}) as GeneralCategory
			end 'found'
		end 'not_left'
	end 'bsearch'

	return {unassigned}
end 'unicodeGeneralCategory'
"""


class GenerationFailed(Exception):
    pass


def compute_mask(categories):
    mask = 0

    for category in categories:
        mask |= 1 << CATEGORY_CODES[category]

    return mask


def parse_entry(path, number, line):
    match = ENTRY_LINE.match(line)

    if not match:
        raise GenerationFailed(f"{path}:{number}: not an entry: {line!r}")

    start = int(match.group(1), 16)
    end = int(match.group(2), 16) if match.group(2) else start
    category = match.group(3)

    if start > end or end > MAX_CODEPOINT:
        raise GenerationFailed(f"{path}:{number}: not a codepoint range: {line!r}")

    if category != UNASSIGNED_CATEGORY and category not in CATEGORY_CODES:
        raise GenerationFailed(f"{path}:{number}: unknown category {category!r}")

    return start, end, category


def read_source(path):
    with open(path, "r", encoding=generated_files.TEXT_ENCODING) as handle:
        lines = handle.read().split("\n")

    if not lines[0].startswith(SOURCE_HEADER_PREFIX):
        raise GenerationFailed(f"{path}:1: the first line names no source file: {lines[0]!r}")

    entries = []

    for number, raw in enumerate(lines, start=1):
        line = raw.strip()

        if not line or line.startswith("#"):
            continue

        start, end, category = parse_entry(path, number, line)

        if category != UNASSIGNED_CATEGORY:
            entries.append((start, end, CATEGORY_CODES[category]))

    entries.sort()

    for (_, previous_end, _), (start, end, _) in zip(entries, entries[1:]):
        if start <= previous_end:
            raise GenerationFailed(f"{path}: U+{start:04X}..U+{end:04X} overlaps the range before it")

    return lines[0][len(SOURCE_HEADER_PREFIX):].strip(), entries


def build_bmp_table(entries):
    table = bytearray([UNASSIGNED_CODE]) * BMP_SIZE

    for start, end, code in entries:
        for cp in range(start, min(end, BMP_SIZE - 1) + 1):
            table[cp] = code

    return bytes(table)


def build_supp_table(entries):
    ranges = [(max(start, BMP_SIZE), end, code) for start, end, code in entries if end >= BMP_SIZE]
    data = bytearray()

    for start, end, code in ranges:
        data.extend(struct.pack(SUPP_ENTRY_FORMAT, start | (end << END_SHIFT) | (code << CATEGORY_SHIFT)))

    return bytes(data), len(ranges)


def render_maxon(source_name, supp_count):
    masks = "\n".join(f"export let {name} = {compute_mask(categories)}" for name, categories in MASK_DEFS.items())

    return MAXON_TEMPLATE.format(
        source_name=source_name,
        bmp_file=BMP_FILE,
        supp_file=SUPP_FILE,
        generator=GENERATOR,
        bmp_size=BMP_SIZE,
        supp_count=supp_count,
        unassigned=UNASSIGNED_CODE,
        max_category=max(CATEGORY_CODES.values()),
        masks=masks,
        codepoint_mask=CODEPOINT_MASK,
        end_shift=END_SHIFT,
        category_shift=CATEGORY_SHIFT,
        category_mask=CATEGORY_MASK,
    ).encode(generated_files.TEXT_ENCODING)


def desired_outputs(input_path, output_dir):
    source_name, entries = read_source(input_path)
    supp_data, supp_count = build_supp_table(entries)

    return {
        os.path.join(output_dir, BMP_FILE): build_bmp_table(entries),
        os.path.join(output_dir, SUPP_FILE): supp_data,
        os.path.join(output_dir, MAXON_FILE): render_maxon(source_name, supp_count),
    }


def require_field_widths():
    if MAX_CODEPOINT > CODEPOINT_MASK or max(CATEGORY_CODES.values()) > CATEGORY_MASK:
        raise GenerationFailed("a codepoint or category code does not fit its supplementary entry field")

    if UNASSIGNED_CODE in CATEGORY_CODES.values():
        raise GenerationFailed(f"a category takes the unassigned code {UNASSIGNED_CODE}")


def main():
    parser = argparse.ArgumentParser(description=f"Generate the Unicode General Category tables and {'/'.join(OUTPUT_DIRECTORY)}/{MAXON_FILE}.")
    generated_files.add_check_argument(parser, "the checked-in files")
    arguments = parser.parse_args()

    script_dir = os.path.dirname(os.path.abspath(__file__))
    output_dir = os.path.join(generated_files.REPO_ROOT, *OUTPUT_DIRECTORY)

    try:
        require_field_widths()
        desired = desired_outputs(os.path.join(script_dir, SOURCE_FILE), output_dir)
        current = {path: generated_files.read_current(path) for path in desired}
    except (OSError, UnicodeDecodeError, GenerationFailed) as failure:
        print(f"cannot generate: {failure}", file=sys.stderr)

        return generated_files.EXIT_FAILED

    return generated_files.sync(desired, current, arguments.check)


if __name__ == "__main__":
    sys.exit(main())
