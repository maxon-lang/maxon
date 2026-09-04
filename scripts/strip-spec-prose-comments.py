#!/usr/bin/env python3
"""Strip non-directive HTML comments from the shv2 spec corpus.

A spec file's `<!-- ... -->` comments are two populations. The HARNESS DIRECTIVES
below are parsed by both spec parsers and are load-bearing. Everything else is
prose a human left behind, and prose in a spec rots: it narrates a blocker that
was cleared, a measurement whose date has passed, a rung that shipped. This
strips the prose and leaves every directive byte-identical.

    python scripts/strip-spec-prose-comments.py            # rewrite specs-shv2/
    python scripts/strip-spec-prose-comments.py --dry-run   # list, write nothing
    python scripts/strip-spec-prose-comments.py path/a.md   # explicit targets
"""

import argparse
import pathlib
import sys

# The union of what BOTH spec parsers recognize — shv2's marker constants
# (Testing/SpecParser.maxon) and the bootstrap's regexes (Testing/SpecParser.cs).
# A comment opening with one of these survives; anything else is prose. The union
# rather than either half, because a directive only one compiler honours is still
# a directive the other must not have deleted out from under it.
DIRECTIVES = (
    "test:",
    "disabled-test:",
    "targets:",
    "Args:",
    "procs:",
    "network:",
    "stdin:",
    "MmTrace",
    "LogTrace",
    "AsyncTrace",
    "SelfhostedOnly",
    "DebugInfo",
    "TimeoutMs:",
)

COMMENT_OPEN = "<!--"
COMMENT_CLOSE = "-->"

# A prose note runs to a handful of lines; a scan longer than this has lost its
# closing token and is walking over real content.
MAX_COMMENT_LINES = 60


def is_directive(payload):
    """True if a comment body opens with a harness directive.

    Both parsers match the directive name as a literal prefix, so a prefix test
    here answers the same question they do.
    """
    for name in DIRECTIVES:
        if not payload.startswith(name):
            continue
        if name.endswith(":"):
            return True
        # A valueless flag takes no payload: what follows is the close token, or
        # the optional colon SelfhostedOnly's regex tolerates.
        rest = payload[len(name):].lstrip()
        if rest.startswith(COMMENT_CLOSE) or rest.startswith(":"):
            return True

    return False


def strip(lines, path, problems):
    """Return the file's lines with every standalone prose comment removed.

    Fence state is tracked because a `<!--` at the start of a line inside a code
    block is program text, not markup. A tagged fence opens, a bare one closes,
    and a line ENDING in a fence closes too — `specs-shv2/arrays.md` writes one
    exitcode block as ``42``` `` on a single line.
    """
    out = []
    removed = []
    in_fence = False
    i = 0

    while i < len(lines):
        line = lines[i]
        stripped = line.strip()

        if stripped.startswith("```"):
            tag = stripped[3:].strip()
            if not in_fence and tag:
                in_fence = True
            elif in_fence and not tag:
                in_fence = False
            out.append(line)
            i += 1
            continue

        if in_fence:
            if stripped.endswith("```"):
                in_fence = False
            out.append(line)
            i += 1
            continue

        if not stripped.startswith(COMMENT_OPEN):
            out.append(line)
            i += 1
            continue

        end = i
        while end < len(lines) and COMMENT_CLOSE not in lines[end]:
            end += 1

        if end >= len(lines) or end - i >= MAX_COMMENT_LINES:
            problems.append(f"{path}:{i + 1}: comment never closes — left alone")
            out.append(line)
            i += 1
            continue

        # Markup sharing a line with prose is not a standalone comment; leaving it
        # whole is the only edit that cannot corrupt the sentence around it.
        if lines[end].split(COMMENT_CLOSE, 1)[1].strip():
            out.append(line)
            i += 1
            continue

        if is_directive(stripped[len(COMMENT_OPEN):].lstrip()):
            out.extend(lines[i:end + 1])
            i = end + 1
            continue

        removed.append((i + 1, stripped[:100]))
        i = end + 1

        # A note fenced by blank lines on both sides leaves a double blank behind.
        if out and not out[-1].strip() and i < len(lines) and not lines[i].strip():
            i += 1

    return out, removed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="*", default=None,
                        help="files or directories (default: specs-shv2)")
    parser.add_argument("--dry-run", action="store_true",
                        help="report what would be removed, write nothing")
    parser.add_argument("--quiet", action="store_true",
                        help="totals only, no per-comment listing")
    args = parser.parse_args()

    roots = [pathlib.Path(p) for p in args.paths] if args.paths else [pathlib.Path("specs-shv2")]
    files = []
    for root in roots:
        if root.is_dir():
            files.extend(sorted(root.rglob("*.md")))
        else:
            files.append(root)

    problems = []
    total_comments = 0
    total_files = 0

    for path in files:
        text = path.read_text(encoding="utf-8")
        lines = text.splitlines(keepends=True)
        kept, removed = strip(lines, path, problems)
        if not removed:
            continue

        total_comments += len(removed)
        total_files += 1
        if not args.quiet:
            for lineno, preview in removed:
                print(f"{path}:{lineno}: {preview}")

        if not args.dry_run:
            path.write_text("".join(kept), encoding="utf-8", newline="")

    verb = "would remove" if args.dry_run else "removed"
    print(f"\n{verb} {total_comments} prose comments across {total_files} files")

    for problem in problems:
        print(f"WARNING: {problem}", file=sys.stderr)

    return 0


if __name__ == "__main__":
    sys.exit(main())
