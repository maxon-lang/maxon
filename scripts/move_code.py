#!/usr/bin/env python3
"""
Script to move chunks of code from one file to another.

Usage:
    python move_code.py <source_file> <start_line> <end_line> <dest_file> [insert_line]
    python move_code.py --functions <source_file> <dest_file> <func1> [func2] ...

Arguments:
    source_file  - File to extract code from
    start_line   - First line to move (1-indexed)
    end_line     - Last line to move (1-indexed, inclusive)
    dest_file    - File to insert code into
    insert_line  - Line number to insert at (optional, default: end of file)

Examples:
    # Move lines 100-200 from file1.zig to end of file2.zig
    python move_code.py src/file1.zig 100 200 src/file2.zig

    # Move lines 50-75 from file1.zig to line 10 of file2.zig
    python move_code.py src/file1.zig 50 75 src/file2.zig 10

    # Move specific functions by name
    python move_code.py --functions src/file1.zig src/file2.zig convertFoo convertBar
"""

import sys
import os
import re


def read_file(path):
    """Read file and return lines with LF endings."""
    with open(path, 'r', encoding='utf-8') as f:
        content = f.read()
    # Normalize to LF
    content = content.replace('\r\n', '\n').replace('\r', '\n')
    return content.split('\n')


def write_file(path, lines):
    """Write lines to file with LF endings."""
    content = '\n'.join(lines)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(content)


def find_zig_function_range(lines, func_name):
    """
    Find the start and end line (1-indexed) of a Zig function.
    Handles doc comments before the function as part of the range.
    """
    # Pattern to match function declaration
    func_pattern = re.compile(rf'^\s+fn {re.escape(func_name)}\s*\(')

    func_start = None
    for i, line in enumerate(lines):
        if func_pattern.match(line):
            func_start = i
            break

    if func_start is None:
        return None, None

    # Look backwards for doc comments (/// or //)
    doc_start = func_start
    for i in range(func_start - 1, -1, -1):
        stripped = lines[i].strip()
        if stripped.startswith('///') or stripped.startswith('//'):
            doc_start = i
        elif stripped == '':
            # Allow blank lines between comments
            continue
        else:
            break

    # Find the end of the function by tracking brace depth
    brace_depth = 0
    in_function = False
    func_end = func_start

    for i in range(func_start, len(lines)):
        line = lines[i]

        # Count braces (simple approach, doesn't handle strings/comments perfectly)
        for char in line:
            if char == '{':
                brace_depth += 1
                in_function = True
            elif char == '}':
                brace_depth -= 1

        if in_function and brace_depth == 0:
            func_end = i
            break

    # Return 1-indexed lines
    return doc_start + 1, func_end + 1


def move_code(source_path, start_line, end_line, dest_path, insert_line=None):
    """
    Move lines from source file to destination file.

    Args:
        source_path: Path to source file
        start_line: First line to move (1-indexed)
        end_line: Last line to move (1-indexed, inclusive)
        dest_path: Path to destination file
        insert_line: Line to insert at (1-indexed), or None for end of file
    """
    # Validate inputs
    if start_line < 1:
        raise ValueError(f"start_line must be >= 1, got {start_line}")
    if end_line < start_line:
        raise ValueError(f"end_line ({end_line}) must be >= start_line ({start_line})")

    # Read source file
    source_lines = read_file(source_path)

    # Validate line numbers against source
    if start_line > len(source_lines):
        raise ValueError(f"start_line ({start_line}) exceeds source file length ({len(source_lines)})")
    if end_line > len(source_lines):
        raise ValueError(f"end_line ({end_line}) exceeds source file length ({len(source_lines)})")

    # Extract the chunk (convert to 0-indexed)
    chunk = source_lines[start_line - 1:end_line]

    # Remove chunk from source
    new_source = source_lines[:start_line - 1] + source_lines[end_line:]

    # Read or create destination file
    if os.path.exists(dest_path):
        dest_lines = read_file(dest_path)
    else:
        dest_lines = []

    # Insert chunk into destination
    if insert_line is None:
        # Append to end
        if dest_lines and dest_lines[-1] != '':
            dest_lines.append('')  # Add blank line separator
        new_dest = dest_lines + chunk
    else:
        if insert_line < 1:
            raise ValueError(f"insert_line must be >= 1, got {insert_line}")
        # Insert at specified position (convert to 0-indexed)
        insert_idx = insert_line - 1
        new_dest = dest_lines[:insert_idx] + chunk + dest_lines[insert_idx:]

    # Write files
    write_file(source_path, new_source)
    write_file(dest_path, new_dest)

    lines_moved = end_line - start_line + 1
    print(f"Moved {lines_moved} lines from {source_path}:{start_line}-{end_line} to {dest_path}")
    print(f"  Source now has {len(new_source)} lines")
    print(f"  Destination now has {len(new_dest)} lines")

    return lines_moved


def move_functions(source_path, dest_path, func_names):
    """
    Move multiple functions from source to destination file.
    Functions are moved in reverse order of their position to preserve line numbers.
    """
    source_lines = read_file(source_path)

    # Find all function ranges
    func_ranges = []
    for func_name in func_names:
        start, end = find_zig_function_range(source_lines, func_name)
        if start is None:
            print(f"Warning: Function '{func_name}' not found in {source_path}")
            continue
        func_ranges.append((func_name, start, end))
        print(f"Found {func_name}: lines {start}-{end}")

    if not func_ranges:
        print("No functions found to move.")
        return

    # Sort by start line in reverse order (move from bottom to top to preserve line numbers)
    func_ranges.sort(key=lambda x: x[1], reverse=True)

    # Read destination file
    if os.path.exists(dest_path):
        dest_lines = read_file(dest_path)
    else:
        dest_lines = []

    # Track total lines moved
    total_moved = 0
    moved_chunks = []

    # Extract each function (in reverse order) and collect chunks
    for func_name, start, end in func_ranges:
        # Re-read source to get current state
        source_lines = read_file(source_path)

        # Extract chunk (convert to 0-indexed)
        chunk = source_lines[start - 1:end]
        moved_chunks.append((func_name, chunk))

        # Remove chunk from source
        new_source = source_lines[:start - 1] + source_lines[end:]
        write_file(source_path, new_source)

        lines_moved = end - start + 1
        total_moved += lines_moved
        print(f"Extracted {func_name}: {lines_moved} lines")

    # Add all chunks to destination (in original order)
    moved_chunks.reverse()  # Restore original order

    for func_name, chunk in moved_chunks:
        if dest_lines and dest_lines[-1] != '':
            dest_lines.append('')  # Add blank line separator
        dest_lines.extend(chunk)

    write_file(dest_path, dest_lines)

    print(f"\nMoved {total_moved} total lines ({len(moved_chunks)} functions)")
    print(f"  Source now has {len(read_file(source_path))} lines")
    print(f"  Destination now has {len(dest_lines)} lines")


def main():
    if len(sys.argv) < 5:
        print(__doc__)
        sys.exit(1)

    if sys.argv[1] == '--functions':
        if len(sys.argv) < 5:
            print("Usage: move_code.py --functions <source> <dest> <func1> [func2] ...")
            sys.exit(1)
        source_path = sys.argv[2]
        dest_path = sys.argv[3]
        func_names = sys.argv[4:]

        if not os.path.exists(source_path):
            print(f"Error: Source file not found: {source_path}")
            sys.exit(1)

        move_functions(source_path, dest_path, func_names)
    else:
        source_path = sys.argv[1]
        start_line = int(sys.argv[2])
        end_line = int(sys.argv[3])
        dest_path = sys.argv[4]
        insert_line = int(sys.argv[5]) if len(sys.argv) > 5 else None

        if not os.path.exists(source_path):
            print(f"Error: Source file not found: {source_path}")
            sys.exit(1)

        try:
            move_code(source_path, start_line, end_line, dest_path, insert_line)
        except ValueError as e:
            print(f"Error: {e}")
            sys.exit(1)


if __name__ == '__main__':
    main()
