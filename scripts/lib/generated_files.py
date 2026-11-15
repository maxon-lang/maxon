import difflib
import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

EXIT_CLEAN = 0
EXIT_DRIFT = 1
EXIT_FAILED = 2

DRIFT_DIFF_LINES = 40

TEXT_ENCODING = "utf-8"


def relative(path):
    return os.path.relpath(path, REPO_ROOT).replace("\\", "/")


def add_check_argument(parser, checked):
    parser.add_argument("--check", action="store_true", help=f"write nothing; exit {EXIT_DRIFT} when {checked} differ from what the generator emits")


def read_current(path):
    try:
        with open(path, "rb") as handle:
            return handle.read()
    except FileNotFoundError:
        return None


def is_text(content):
    try:
        content.decode(TEXT_ENCODING)
    except UnicodeDecodeError:
        return False

    return b"\0" not in content


def report_drift(name, current, desired):
    if current is None:
        print(f"missing: {name}", file=sys.stderr)

        return

    print(f"stale: {name}", file=sys.stderr)

    if not (is_text(current) and is_text(desired)):
        first = next((offset for offset, (old, new) in enumerate(zip(current, desired)) if old != new), min(len(current), len(desired)))
        print(f"  {len(current)} bytes checked in, {len(desired)} generated; first difference at byte {first}", file=sys.stderr)

        return

    diff = difflib.unified_diff(current.decode(TEXT_ENCODING).split("\n"), desired.decode(TEXT_ENCODING).split("\n"), lineterm="", n=0)

    for line in list(diff)[:DRIFT_DIFF_LINES]:
        print("  " + line, file=sys.stderr)


def sync(desired, current, check):
    drifted = [path for path in desired if current[path] != desired[path]]

    if check:
        for path in drifted:
            report_drift(relative(path), current[path], desired[path])

        return EXIT_DRIFT if drifted else EXIT_CLEAN

    try:
        for path in drifted:
            with open(path, "wb") as handle:
                handle.write(desired[path])

            print(f"wrote {relative(path)}")
    except OSError as failure:
        print(f"cannot write: {failure}", file=sys.stderr)

        return EXIT_FAILED

    print(f"{len(drifted)} of {len(desired)} file(s) rewritten")

    return EXIT_CLEAN
