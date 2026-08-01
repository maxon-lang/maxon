#!/usr/bin/env python3
"""Compare the tests in `specs/` against the tests in `specs-shv2/`.

shv2 is finished -- as a language -- when `specs-shv2/` runs at least every test
`specs/` runs. It may run more. This measures the gap, file by file and test by
test, so progress is visible and nothing is dropped in silence.

Matching is by SPEC FILE NAME and TEST NAME. A `specs/foo.md` test named `bar`
is ported when `specs-shv2/foo.md` has a test named `bar`. Nothing is inferred:
if a port renames the file or the case, this reports it MISSING, and the fix is
to make the names agree.

Every `specs/` test lands in exactly one bucket:

  ACTIVE    running in specs-shv2 -- the only bucket that counts as done,
            because only an active test can pass
  DISABLED  present but shelved behind `<!-- disabled-test: NAME -->`, with the
            rung that unlocks it on the following comment line
  DEFERRED  present but parked in a `## Deferred` section as a `### name`
  MISSING   not in specs-shv2 in any form -- the bucket that matters, because
            nothing is tracking these

GRAMMAR

Each tree is parsed by its own runner's rules, so this script's idea of "a test"
cannot drift from what actually runs:

  specs/       maxon-sharp/Testing/SpecParser.cs
  specs-shv2/  maxon-shv2/Testing/SpecParser.maxon

The differences between them are real and load-bearing:

  - The C# parser scans the WHOLE file for `<!-- test: -->`; shv2's scans only
    the `## Tests` region, up to the next `## ` heading. That bound is what makes
    a `## Deferred` section inert without relying on HTML comments nesting (they
    do not -- see the DEFERRED-SKIP CONVENTION comment in shv2's parser).
  - The C# runner skips whole files with `status: draft` or `status: selfhosted`,
    and `category: network` files unless `--network`. shv2's skips only `draft`.
    A spec the reference suite does not run is not a debt shv2 owes, so those are
    listed under their own heading rather than counted as unported.
  - The C# parser synthesizes `docs-example-N` tests from executable ```maxon
    blocks in a `## Documentation` section. shv2's parser has no equivalent, so
    these can never match by name; they are reported separately rather than
    dumped into MISSING, where they would be noise nobody can act on.

VALIDATED against the runners rather than assumed. On 2026-07-28, at 7c05a6996:
this script counts 3104 reference marker tests plus 84 docs-examples = 3188, and
`maxon spec-test` ran 3188; it counts 2241 specs-shv2 tests runnable on
x64-windows, and `maxon-shv2 spec-test` ran 2241. Both sides agree to the digit.
If a change to either runner's parser makes them disagree, this script is what is
wrong.

Exit code is 0 whatever the numbers say -- this is an instrument, not a gate.
It exits non-zero only if the run itself failed. `--fail-on-missing` opts into
gate behaviour for CI use.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path

# --- Spec-markdown grammar -------------------------------------------------
# These mirror the two runners' own patterns. `TEST_BOUNDARY` matches active and
# disabled markers alike because a disabled test still ends the preceding test's
# section -- that is how both real parsers keep the two from drifting on where a
# test ends.
FRONTMATTER = re.compile(r"^---\r?\n(.*?)\r?\n---", re.S)
TEST_MARKER = re.compile(r"<!--\s*test:\s*(\S+)\s*-->")
DISABLED_MARKER = re.compile(r"<!--\s*disabled-test:\s*(\S+)\s*-->")
TEST_BOUNDARY = re.compile(r"<!--\s*(?:disabled-)?test:\s*\S+\s*-->")
TARGETS_DIRECTIVE = re.compile(r"<!--\s*targets:\s*(.+?)\s*-->")
SELFHOSTED_ONLY = re.compile(r"<!--\s*SelfhostedOnly\s*-->")
MAXON_BLOCK = re.compile(r"```maxon\r?\n(.*?)```", re.S)
DOCS_HEADING = re.compile(r"^## Documentation", re.M)
TESTS_HEADING = re.compile(r"^## Tests", re.M)
SECTION_HEADING = re.compile(r"^## ", re.M)
DEFERRED_HEADING = re.compile(r"^## Deferred", re.M)
DEFERRED_CASE = re.compile(r"^### +(.+?)\s*$", re.M)
# The comment line after a `<!-- disabled-test: -->` marker names the rung that
# unlocks it. That is the roadmap, so it is carried through to the report.
REASON_COMMENT = re.compile(r"<!--\s*(.+?)\s*-->")

NETWORK_CATEGORY = "network"

ACTIVE, DISABLED, DEFERRED, MISSING = "active", "disabled", "deferred", "missing"

# Targets the per-target census reports on. The two compilers do NOT emit the same
# set, and this comment used to say they did: the BOOTSTRAP emits x64-windows and
# arm64-macos only (A1n withdrew the rest — it has a PE writer and a Mach-O writer
# and no ELF writer, so anything else is refused by name). x64-linux and wasm32-wasi
# are SHV2's, and both are real: the cross-target gate runs the Linux ELF under WSL
# and the wasm component under the vendored wasmtime. x64-linux therefore stays in
# the census — it is a live lane for one compiler and refused by the other.
CENSUS_TARGETS = ("x64-windows", "x64-linux", "arm64-macos", "wasm32-wasi")


def yaml_value(frontmatter: str, key: str) -> str:
    match = re.search(rf"^{re.escape(key)}:\s*(.+)$", frontmatter, re.M)

    return match.group(1).strip() if match else ""


@dataclass(frozen=True)
class Test:
    name: str
    targets: tuple[str, ...]   # `<!-- targets: -->` restriction; empty = all
    kind: str                  # ACTIVE / DISABLED / DEFERRED
    reason: str = ""           # the rung that unlocks a disabled test

    def runs_on(self, target: str | None) -> bool:
        """Whether a runner on `target` selects this test.

        An absent or empty `targets` marker means "no restriction", matching both
        runners. `None` means no target was named, so the restriction cannot be
        evaluated and every test counts -- again matching both runners.
        """
        return target is None or not self.targets or target in self.targets


@dataclass
class SpecFileInfo:
    spec: str                  # basename without the .md
    status: str
    category: str
    tests: list[Test] = field(default_factory=list)
    doc_examples: int = 0
    skip_reason: str = ""      # non-empty when the runner skips the whole file


def _section_targets(section: str) -> tuple[str, ...]:
    match = TARGETS_DIRECTIVE.search(section)

    if not match:
        return ()

    return tuple(t.strip() for t in match.group(1).split(",") if t.strip())


def _sections(content: str, marker: re.Pattern[str], region_end: int) -> list[tuple[str, str]]:
    """Every (name, section) pair for `marker`, each ending at the next test.

    Both runners delimit a test's body by the NEXT test marker, active or
    disabled, so the same boundary rule is used here for both kinds.
    """
    out = []

    for match in marker.finditer(content, 0, region_end):
        start = match.end()
        boundary = TEST_BOUNDARY.search(content, start, region_end)
        out.append((match.group(1), content[start:boundary.start() if boundary else region_end]))

    return out


def parse_reference_spec(path: Path) -> SpecFileInfo:
    """Parse a `specs/` file by the C# runner's rules (`SpecParser.cs`)."""
    content = path.read_text(encoding="utf-8", errors="replace")
    fm = FRONTMATTER.search(content)
    frontmatter = fm.group(1) if fm else ""
    status = yaml_value(frontmatter, "status") or "unknown"
    category = yaml_value(frontmatter, "category")
    info = SpecFileInfo(spec=path.stem, status=status, category=category)

    # Whole-file skips, in the runner's own order.
    if status == "draft":
        info.skip_reason = "status: draft"
    elif status == "selfhosted":
        info.skip_reason = "status: selfhosted"
    elif category == NETWORK_CATEGORY:
        info.skip_reason = "category: network"

    # The C# parser scans the entire file, not just `## Tests`.
    for name, section in _sections(content, TEST_MARKER, len(content)):
        # A `<!-- SelfhostedOnly -->` test opts out of the C# runner, and a test
        # with no ```maxon block is dropped by it outright.
        if SELFHOSTED_ONLY.search(section) or not MAXON_BLOCK.search(section):
            continue

        info.tests.append(Test(name, _section_targets(section), ACTIVE))

    info.doc_examples = _count_doc_examples(content)

    return info


def _count_doc_examples(content: str) -> int:
    """Executable ```maxon blocks the C# parser lifts out of `## Documentation`.

    shv2's parser reads only `## Tests`, so these are counted but never matched.
    """
    docs = DOCS_HEADING.search(content)

    if not docs:
        return 0

    tests_heading = TESTS_HEADING.search(content, docs.end())
    section = content[docs.end():tests_heading.start() if tests_heading else len(content)]

    return sum(1 for m in MAXON_BLOCK.finditer(section) if "function main()" in m.group(1))


def parse_shv2_spec(path: Path) -> SpecFileInfo:
    """Parse a `specs-shv2/` file by shv2's rules (`SpecParser.maxon`)."""
    content = path.read_text(encoding="utf-8", errors="replace")
    fm = FRONTMATTER.search(content)
    frontmatter = fm.group(1) if fm else ""
    status = yaml_value(frontmatter, "status") or "unknown"
    info = SpecFileInfo(
        spec=path.stem,
        status=status,
        category=yaml_value(frontmatter, "category"),
    )

    if status == "draft":
        info.skip_reason = "status: draft"

    # shv2 scans ONLY the `## Tests` region: from the heading to the next `## `.
    # That bound is what makes `## Deferred` inert, so it is reproduced exactly.
    tests_heading = TESTS_HEADING.search(content)

    if tests_heading:
        next_section = SECTION_HEADING.search(content, tests_heading.end())
        region_end = next_section.start() if next_section else len(content)

        for name, section in _sections(content, TEST_MARKER, region_end):
            info.tests.append(Test(name, _section_targets(section), ACTIVE))

        for name, section in _sections(content, DISABLED_MARKER, region_end):
            reason = REASON_COMMENT.search(section)
            info.tests.append(
                Test(name, _section_targets(section), DISABLED,
                     reason=reason.group(1) if reason else "")
            )

    info.tests.extend(_deferred_tests(content))

    return info


def _deferred_tests(content: str) -> list[Test]:
    """`### name` subsections under a `## Deferred` heading.

    These carry no test marker by design, so they are found by heading.
    """
    heading = DEFERRED_HEADING.search(content)

    if not heading:
        return []

    following = SECTION_HEADING.search(content, heading.end())
    region = content[heading.end():following.start() if following else len(content)]

    return [Test(m.group(1).strip(), (), DEFERRED) for m in DEFERRED_CASE.finditer(region)]


def load_tree(directory: Path, parser) -> dict[str, SpecFileInfo]:
    """Parse every `*.md` directly in `directory`, keyed by spec name.

    Non-recursive, because both runners use a non-recursive directory listing --
    `specs/archive/` and the `fragments-*/` golden directories are not suites.
    """
    if not directory.is_dir():
        raise SystemExit(f"error: not a directory: {directory}")

    specs = {}

    for path in sorted(directory.glob("*.md")):
        info = parser(path)
        specs[info.spec] = info

    return specs


@dataclass
class Result:
    spec: str
    name: str
    bucket: str
    reason: str = ""       # the rung named on a disabled test
    file_absent: bool = False   # no specs-shv2 file of this name at all


def compare(
    reference: dict[str, SpecFileInfo],
    shv2: dict[str, SpecFileInfo],
    target: str | None = None,
) -> list[Result]:
    results = []

    for spec, info in sorted(reference.items()):
        if info.skip_reason:
            continue

        counterpart = shv2.get(spec)
        # A `draft` spec on the shv2 side runs nothing, so it covers nothing.
        ported = {}

        if counterpart and not counterpart.skip_reason:
            for test in counterpart.tests:
                # An ACTIVE test excluded from this target does not run here, so
                # it cannot cover a reference test that does.
                if test.kind == ACTIVE and not test.runs_on(target):
                    continue

                ported.setdefault(test.name, test)

        for test in info.tests:
            # The denominator is what the reference suite runs HERE. A test its
            # own marker excludes from this target is not a debt shv2 owes on it.
            if not test.runs_on(target):
                continue

            match = ported.get(test.name)
            results.append(Result(
                spec=spec,
                name=test.name,
                bucket=match.kind if match else MISSING,
                reason=match.reason if match else "",
                file_absent=counterpart is None,
            ))

    return results


# --- reporting -------------------------------------------------------------

def pct(part: int, whole: int) -> str:
    return f"{100.0 * part / whole:.1f}%" if whole else "n/a"


def _active_count(specs: dict[str, SpecFileInfo], target: str | None) -> int:
    return sum(
        1
        for info in specs.values()
        if not info.skip_reason
        for test in info.tests
        if test.kind == ACTIVE and test.runs_on(target)
    )


def render(results, reference, shv2, args) -> int:
    counts = defaultdict(int)

    for result in results:
        counts[result.bucket] += 1

    total = len(results)
    scope = f"target {args.target}" if args.target else "all targets"

    print(f"specs/ -> specs-shv2/ port coverage  ({scope})")
    print("=" * 62)
    print(f"  reference tests the C# suite runs       : {total}")
    print(f"  ACTIVE in specs-shv2                    : {counts[ACTIVE]}  "
          f"({pct(counts[ACTIVE], total)})")
    print(f"  DISABLED (shelved, rung recorded)       : {counts[DISABLED]}")
    print(f"  DEFERRED (## Deferred section)          : {counts[DEFERRED]}")
    print(f"  MISSING (not present at all)            : {counts[MISSING]}")

    absent_specs = {r.spec for r in results if r.file_absent}
    absent_tests = sum(1 for r in results if r.file_absent)
    print()
    print(f"  {len(absent_specs)} reference spec file(s) have no specs-shv2 file of that")
    print(f"  name at all, accounting for {absent_tests} of the MISSING tests.")

    _render_census(reference, shv2)
    _render_reference_skips(reference)
    _render_doc_examples(reference)
    _render_shv2_only(reference, shv2, args.target)

    if args.by_spec:
        _render_by_spec(results)

    if args.list_missing:
        _render_bucket(results, MISSING, "MISSING -- no test of this name in specs-shv2")

    if args.list_disabled:
        _render_bucket(results, DISABLED, "DISABLED in specs-shv2", show_reason=True)

    if args.list_deferred:
        _render_bucket(results, DEFERRED, "DEFERRED in specs-shv2")

    if args.json:
        _write_json(results, reference, shv2, args)
        print(f"\nwrote {args.json}")

    if args.fail_on_missing and counts[MISSING]:
        print(f"\nFAIL: {counts[MISSING]} reference tests are not in specs-shv2")

        return 1

    return 0


def _render_census(reference: dict[str, SpecFileInfo], shv2: dict[str, SpecFileInfo]) -> None:
    """What each suite runs per target, and how to tie it back to the runner.

    Printed because "how many tests are there" is target-dependent, and because a
    reader who cannot reconcile these numbers with a real `spec-test` run has no
    reason to believe the rest of the report.
    """
    docs = sum(info.doc_examples for info in reference.values() if not info.skip_reason)

    print("\ntests each suite runs, per target")
    print("-" * 62)
    print(f"  {'target':<16} {'specs/':>10} {'specs-shv2/':>12}")

    for target in CENSUS_TARGETS:
        print(f"  {target:<16} {_active_count(reference, target) + docs:>10} "
              f"{_active_count(shv2, target):>12}")

    print(f"  specs/ counts include the {docs} synthesized docs-example tests, which is")
    print("  what makes these totals equal what `maxon spec-test` reports.")


def _render_reference_skips(reference: dict[str, SpecFileInfo]) -> None:
    by_reason = defaultdict(list)

    for info in reference.values():
        if info.skip_reason:
            by_reason[info.skip_reason].append(info.spec)

    if not by_reason:
        return

    print("\nreference specs the C# suite does not run (excluded above)")
    print("-" * 62)

    for reason, specs in sorted(by_reason.items()):
        print(f"  {reason}: {len(specs)} file(s)")
        print("    " + ", ".join(sorted(specs)))


def _render_doc_examples(reference: dict[str, SpecFileInfo]) -> None:
    total = sum(info.doc_examples for info in reference.values() if not info.skip_reason)

    if not total:
        return

    print("\ndocs-example tests")
    print("-" * 62)
    print(f"  {total} executable example(s) the C# parser lifts from `## Documentation`.")
    print("  shv2's parser reads only `## Tests`, so these have no counterpart by")
    print("  construction and are excluded from the buckets above.")


def _render_shv2_only(reference, shv2, target: str | None) -> None:
    """Tests and whole files specs-shv2 has that specs/ does not.

    shv2 may run more than the reference suite, and does. These are not a gap --
    they are counted so the two totals in the census reconcile.
    """
    extra_files, extra_tests = [], 0

    for spec, info in sorted(shv2.items()):
        if info.skip_reason:
            continue

        reference_names = (
            {t.name for t in reference[spec].tests}
            if spec in reference and not reference[spec].skip_reason
            else set()
        )

        if spec not in reference:
            extra_files.append(spec)

        extra_tests += sum(
            1
            for t in info.tests
            if t.kind == ACTIVE and t.runs_on(target) and t.name not in reference_names
        )

    print("\nspecs-shv2 tests with no counterpart in specs/")
    print("-" * 62)
    print(f"  {extra_tests} active test(s), including every test in "
          f"{len(extra_files)} shv2-only spec file(s).")
    print("  shv2 is allowed to run more than the reference suite; these are not a gap.")


def _render_by_spec(results: list[Result]) -> None:
    by_spec: dict[str, dict[str, int]] = defaultdict(lambda: defaultdict(int))
    absent = set()

    for result in results:
        by_spec[result.spec][result.bucket] += 1

        if result.file_absent:
            absent.add(result.spec)

    print("\nper spec file (most outstanding first; ! = no specs-shv2 file at all)")
    print("-" * 62)
    print(f"  {'spec':<44} {'act':>4} {'dis':>4} {'def':>4} {'mis':>4}")

    def sort_key(item):
        _, counts = item

        return (-(counts[DISABLED] + counts[DEFERRED] + counts[MISSING]), -sum(counts.values()))

    for spec, counts in sorted(by_spec.items(), key=sort_key):
        outstanding = counts[DISABLED] + counts[DEFERRED] + counts[MISSING]
        mark = "!" if spec in absent else (" " if outstanding == 0 else "*")
        print(f" {mark}{spec:<44} {counts[ACTIVE]:>4} {counts[DISABLED]:>4} "
              f"{counts[DEFERRED]:>4} {counts[MISSING]:>4}")


def _render_bucket(results, bucket: str, title: str, show_reason: bool = False) -> None:
    entries = [r for r in results if r.bucket == bucket]

    if not entries:
        return

    print(f"\n{title} ({len(entries)})")
    print("-" * 62)
    current = None

    for result in sorted(entries, key=lambda r: (r.spec, r.name)):
        if result.spec != current:
            current = result.spec
            note = "   (no specs-shv2 file of this name)" if result.file_absent else ""
            print(f"  {current}.md{note}")

        suffix = f"   [{result.reason}]" if show_reason and result.reason else ""
        print(f"    - {result.name}{suffix}")


def _write_json(results, reference, shv2, args) -> None:
    payload = {
        "target": args.target,
        "summary": {
            bucket: sum(1 for r in results if r.bucket == bucket)
            for bucket in (ACTIVE, DISABLED, DEFERRED, MISSING)
        },
        "referenceTotal": len(results),
        "shv2ActiveTotal": _active_count(shv2, args.target),
        "skippedReferenceSpecs": [
            {"spec": info.spec, "reason": info.skip_reason}
            for info in reference.values()
            if info.skip_reason
        ],
        "tests": [
            {
                "spec": r.spec,
                "name": r.name,
                "bucket": r.bucket,
                "reason": r.reason,
                "specFileAbsent": r.file_absent,
            }
            for r in sorted(results, key=lambda r: (r.spec, r.name))
        ],
    }

    Path(args.json).write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    # Spec prose is UTF-8 and full of em dashes and arrows, but a Windows console
    # defaults to cp1252 and turns each of them into a replacement character --
    # so a disabled test's reason, which is the roadmap entry a reader is here
    # for, arrives corrupted. Ask for UTF-8 rather than degrade the one field
    # that carries prose.
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")

    repo_root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(
        description="Compare the tests in specs/ against the tests in specs-shv2/.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument("--repo-root", type=Path, default=repo_root,
                        help="repository root (default: this script's parent directory)")
    parser.add_argument("--reference", type=Path, default=None,
                        help="reference spec directory (default: <repo-root>/specs)")
    parser.add_argument("--shv2", type=Path, default=None,
                        help="shv2 spec directory (default: <repo-root>/specs-shv2)")
    parser.add_argument("--target", metavar="KEY", default=None,
                        help="count only what this target runs, e.g. x64-windows, wasm32-wasi "
                             "(default: every test in both trees)")
    parser.add_argument("--by-spec", action="store_true", help="per-spec-file breakdown")
    parser.add_argument("--list-missing", action="store_true", help="name every MISSING test")
    parser.add_argument("--list-disabled", action="store_true",
                        help="name every DISABLED test with the rung that unlocks it")
    parser.add_argument("--list-deferred", action="store_true", help="name every DEFERRED test")
    parser.add_argument("--all", action="store_true", help="every breakdown and listing")
    parser.add_argument("--json", metavar="PATH", help="also write machine-readable results")
    parser.add_argument("--fail-on-missing", action="store_true",
                        help="exit 1 when any reference test is MISSING (CI gate)")
    args = parser.parse_args()

    if args.all:
        args.by_spec = args.list_missing = args.list_disabled = args.list_deferred = True

    reference = load_tree(args.reference or args.repo_root / "specs", parse_reference_spec)
    shv2 = load_tree(args.shv2 or args.repo_root / "specs-shv2", parse_shv2_spec)

    return render(compare(reference, shv2, args.target), reference, shv2, args)


if __name__ == "__main__":
    sys.exit(main())
