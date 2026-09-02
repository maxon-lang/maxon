#!/usr/bin/env python3
"""Regenerate every fmt fixture's expectations BY RUNNING THE BOOTSTRAP COMPILER.

Every `expected-*` file and every `expected-tree/` in this corpus is the REFERENCE
compiler's real answer, never a hand-written one. That is what makes the corpus a
parity oracle for the shv2 port rather than a record of what its author happened to
expect: a fixture written by hand proves only "shv2 does what I wrote down", where
one generated here proves "shv2 does what the bootstrap does".

Re-run after changing any fixture input. Never hand-edit an `expected-*` file.

Usage:  python tests/fmt/generate-expectations.py [case ...]      (default: all)
"""

import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
FIXTURES = os.path.join(HERE, "fixtures")

BACKSLASH = chr(92)
FIXTURE_SUFFIX = ".fixture"
DOT_PREFIX = "dot-"


# --- THE STORED-NAME MAPPING, WRITTEN ONCE ---------------------------------------
# Nothing stored in this corpus is a live `.maxon` or a real `.git`, and it cannot be.
# Two independent reasons:
#   (a) git refuses to commit any path with a `.git` component, in either the
#       directory or the gitfile form - so the fixture that gates the 92-file
#       worktree incident cannot be stored literally;
#   (b) a real `.maxon` under `tests/` is walked by `maxon fmt` over the checkout,
#       which is the tool under test rewriting its own oracle. It would silently
#       re-bless every expectation here, including `already-formatted`, which would
#       then be green forever by construction.
#
#     <name>.fixture -> <name>            dot-<name> -> .<name>
#
# `tests/fmt/fixtures.test.maxon` implements the identical two rules so the harness
# and this generator stage the same tree. They must agree.
def stored_to_real(name):
    trimmed = name[: -len(FIXTURE_SUFFIX)] if name.endswith(FIXTURE_SUFFIX) else name
    if trimmed.startswith(DOT_PREFIX):
        return "." + trimmed[len(DOT_PREFIX):]
    return trimmed


def real_to_stored(name):
    if name.startswith("."):
        return DOT_PREFIX + name[1:] + FIXTURE_SUFFIX
    return name + FIXTURE_SUFFIX


def stage(src, dst):
    """Stored layout -> a tree the compiler can actually be pointed at."""
    os.makedirs(dst, exist_ok=True)
    for entry in sorted(os.listdir(src)):
        source = os.path.join(src, entry)
        target = os.path.join(dst, stored_to_real(entry))
        if os.path.isdir(source):
            stage(source, target)
        else:
            shutil.copy2(source, target)


def capture(src, dst):
    """A real tree -> the stored layout, so what we commit is inert."""
    os.makedirs(dst, exist_ok=True)
    for entry in sorted(os.listdir(src)):
        source = os.path.join(src, entry)
        if os.path.isdir(source):
            # A directory needs only the `dot-` half of the mapping; appending
            # `.fixture` to it would make `stored_to_real` round-trip to a name the
            # tree never had.
            name = DOT_PREFIX + entry[1:] if entry.startswith(".") else entry
            capture(source, os.path.join(dst, name))
        else:
            shutil.copy2(source, os.path.join(dst, real_to_stored(entry)))


TREE_TOKEN = "<TREE>"


def normalize(text, tree):
    """Fold what is host-specific out of an observed stream.

    Three things vary between hosts and between runs, and all three would make a pin
    match only on the machine that minted it:

    - CR, because the pins are committed with LF endings;
    - the path separator, because `fmt` echoes the argument spelling it was given plus
      the host separator for whatever it walked into;
    - the ABSOLUTE STAGING PATH. `no-argument-defaults-to-cwd` passes no argument at
      all, so `fmt` falls back to the current directory and prints absolute paths out
      of a fresh temp directory -- a different one on every single run. Without this
      the case could never be pinned, and it is the ONLY case that exercises the cwd
      default that every flag-refusal case exists to protect.

    `tests/fmt/fixtures.test.maxon` performs the identical three substitutions against
    its own staging directory. They must agree.
    """
    folded = text.replace(chr(13), "").replace(BACKSLASH, "/")
    return folded.replace(tree.replace(BACKSLASH, "/"), TREE_TOKEN)


def write_stream(path, text, tree):
    body = normalize(text, tree)
    if body and not body.endswith("\n"):
        body += "\n"
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(body)


SELFTEST = os.path.join(HERE, "selftest-cases")


def generate_selftest_expectations(exe):
    """Mint `<Case>.expected` beside every `<Case>.in`, from the bootstrap.

    The shv2 formatter is a PORT, so byte-parity with the reference is the whole
    contract and a divergence is the failure rather than an improvement -- which is
    why these are pinned byte-exact, where `FormatterSelfTest`'s own invariants
    (comment multiplicity, idempotence) deliberately pin no layout. The two catch
    different things and both are kept: parity says "not the reference's answer",
    the invariants say "destroyed something", and the second reads far better when
    it is the one that fires.

    A case the lexer REJECTS mints an `.expected` identical to its `.in` -- that is
    the contract for an unformattable source, not an accident.
    """
    if not os.path.isdir(SELFTEST):
        return
    work = tempfile.mkdtemp()
    try:
        names = sorted(n for n in os.listdir(SELFTEST) if n.endswith(".in"))
        for name in names:
            stem = name[: -len(".in")]
            shutil.copy2(os.path.join(SELFTEST, name),
                         os.path.join(work, stem + ".maxon"))
        subprocess.run([exe, "fmt", "."], cwd=work, capture_output=True, text=True)
        for name in names:
            stem = name[: -len(".in")]
            with open(os.path.join(work, stem + ".maxon"), encoding="utf-8") as h:
                body = h.read()
            with open(os.path.join(SELFTEST, stem + ".expected"), "w",
                      encoding="utf-8", newline=chr(10)) as h:
                h.write(body)
        print("%-32s %d cases" % ("selftest-cases", len(names)))
    finally:
        shutil.rmtree(work, ignore_errors=True)


def main():
    exe = os.path.join(ROOT, "bin", "maxon.exe")
    if not os.path.exists(exe):
        exe = os.path.join(ROOT, "bin", "maxon")
    if not os.path.exists(exe):
        sys.exit("no bootstrap binary under " + os.path.join(ROOT, "bin"))

    if not sys.argv[1:]:
        generate_selftest_expectations(exe)

    for case in sys.argv[1:] or sorted(os.listdir(FIXTURES)):
        case_dir = os.path.join(FIXTURES, case)
        input_dir = os.path.join(case_dir, "input")
        if not os.path.isdir(input_dir):
            print("skip " + case + " (no input/)")
            continue

        work = tempfile.mkdtemp()
        try:
            tree = os.path.join(work, "tree")
            stage(input_dir, tree)

            argv = []
            argv_file = os.path.join(case_dir, "argv.txt")
            if os.path.exists(argv_file):
                with open(argv_file, encoding="utf-8") as handle:
                    argv = [line for line in handle.read().splitlines() if line]

            # The working directory IS the staged tree, never the checkout root.
            # The flag-refusal cases exist because a regression makes `fmt` fall back
            # to the current directory and rewrite it in place; run from the checkout
            # root, the fixture that gates that incident would REPRODUCE it.
            run = subprocess.run(
                [exe, "fmt"] + argv, cwd=tree, capture_output=True, text=True
            )

            write_stream(os.path.join(case_dir, "expected-stdout.txt"), run.stdout, tree)
            write_stream(os.path.join(case_dir, "expected-stderr.txt"), run.stderr, tree)
            write_stream(os.path.join(case_dir, "expected-exit.txt"), str(run.returncode), tree)

            expected_tree = os.path.join(case_dir, "expected-tree")
            shutil.rmtree(expected_tree, ignore_errors=True)
            capture(tree, expected_tree)

            print("%-32s exit=%d" % (case, run.returncode))
        finally:
            shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    main()
