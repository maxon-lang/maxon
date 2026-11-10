#!/usr/bin/env python3
# The `/land` "No leftovers" gate (`.claude/skills/land/SKILL.md` §8): a run that leaves files in the
# checkout is found here, before the commit, as an untracked path or a gitignored one
# `scripts/allowed-ignored-paths.txt` does not allow.
import os
import re
import subprocess
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ALLOW_LIST_RELATIVE_PATH = "scripts/allowed-ignored-paths.txt"
ALLOW_LIST_PATH = os.path.join(REPO_ROOT, *ALLOW_LIST_RELATIVE_PATH.split("/"))

GIT_STATUS_COMMAND = [
    "git",
    "status",
    "--porcelain=v1",
    "-z",
    "--ignored=matching",
    "--untracked-files=normal",
]

STATUS_IGNORED = "!!"
STATUS_UNTRACKED = "??"
STATUS_CODES_WITH_SOURCE_PATH = "RC"
STATUS_FIELD_WIDTH = 2
STATUS_PATH_START = 3
FIELD_SEPARATOR = "\0"

COMMENT_PREFIX = "#"
NEGATION_PREFIX = "!"
DIRECTORY_SUFFIX = "/"
ESCAPE = "\\"
DOUBLE_STAR = "**"
ZERO_OR_MORE_DIRECTORIES = "(?:[^/]+/)*"
EVERYTHING_INSIDE = ".+"
ANY_RUN_IN_SEGMENT = "[^/]*"
ANY_CHARACTER_IN_SEGMENT = "[^/]"

EXIT_CLEAN = 0
EXIT_LEFTOVERS = 1
EXIT_FAILED = 2

FIX_LINE = (
    "Delete each path above. If a tool legitimately leaves one, add a pattern for it to "
    + ALLOW_LIST_RELATIVE_PATH
    + ". An untracked path that belongs to the change is staged with git add."
)


class CheckFailed(Exception):
    pass


class Pattern:
    def __init__(self, text):
        self.text = text
        self.directory_only = text.endswith(DIRECTORY_SUFFIX)
        body = text[:-1] if self.directory_only else text
        anchored = DIRECTORY_SUFFIX in body

        if body.startswith(DIRECTORY_SUFFIX):
            body = body[1:]

        segments = body.split(DIRECTORY_SUFFIX)

        if any(segment == "" for segment in segments):
            raise CheckFailed(f"{ALLOW_LIST_RELATIVE_PATH}: '{text}' has an empty path segment")

        if not anchored:
            segments = [DOUBLE_STAR] + segments

        self.regex = re.compile(self.build_regex(segments, text))

    @staticmethod
    def build_regex(segments, text):
        pieces = []
        last_index = len(segments) - 1

        for index, segment in enumerate(segments):
            is_last = index == last_index

            if segment == DOUBLE_STAR:
                pieces.append(EVERYTHING_INSIDE if is_last else ZERO_OR_MORE_DIRECTORIES)
                continue

            pieces.append(Pattern.segment_regex(segment, text))

            if not is_last:
                pieces.append(DIRECTORY_SUFFIX)

        return "".join(pieces)

    @staticmethod
    def segment_regex(segment, text):
        pieces = []
        index = 0

        while index < len(segment):
            character = segment[index]

            if character == ESCAPE:
                if index + 1 == len(segment):
                    raise CheckFailed(f"{ALLOW_LIST_RELATIVE_PATH}: '{text}' ends with a backslash")

                pieces.append(re.escape(segment[index + 1]))
                index += 2
            elif character == "*":
                pieces.append(ANY_RUN_IN_SEGMENT)
                index += 1
            elif character == "?":
                pieces.append(ANY_CHARACTER_IN_SEGMENT)
                index += 1
            elif character == "[":
                class_regex, index = Pattern.character_class(segment, index)
                pieces.append(class_regex)
            else:
                pieces.append(re.escape(character))
                index += 1

        return "".join(pieces)

    @staticmethod
    def character_class(segment, start):
        index = start + 1
        negated = index < len(segment) and segment[index] in "!^"

        if negated:
            index += 1

        members_start = index

        if index < len(segment) and segment[index] == "]":
            index += 1

        closing = segment.find("]", index)

        if closing == -1:
            return re.escape("["), start + 1

        members = segment[members_start:closing].replace(ESCAPE, ESCAPE + ESCAPE)
        return "[" + ("^" if negated else "") + members + "]", closing + 1

    def matches(self, path, is_directory):
        if self.directory_only and not is_directory:
            return False

        return self.regex.fullmatch(path) is not None


def load_patterns():
    try:
        with open(ALLOW_LIST_PATH, encoding="utf-8") as handle:
            lines = handle.read().splitlines()
    except OSError as error:
        raise CheckFailed(f"cannot read {ALLOW_LIST_RELATIVE_PATH}: {error}")

    patterns = []

    for line in lines:
        text = line.strip()

        if text == "" or text.startswith(COMMENT_PREFIX):
            continue

        if text.startswith(NEGATION_PREFIX):
            raise CheckFailed(f"{ALLOW_LIST_RELATIVE_PATH}: '{text}' negates; the allow-list has no negation")

        patterns.append(Pattern(text))

    return patterns


def git_status_entries():
    try:
        result = subprocess.run(GIT_STATUS_COMMAND, cwd=REPO_ROOT, capture_output=True)
    except OSError as error:
        raise CheckFailed(f"cannot run git: {error}")

    if result.returncode != 0:
        raise CheckFailed("git status failed: " + result.stderr.decode("utf-8", errors="replace").strip())

    fields = result.stdout.decode("utf-8", errors="replace").split(FIELD_SEPARATOR)
    entries = []
    index = 0

    while index < len(fields):
        field = fields[index]
        index += 1

        if field == "":
            continue

        status = field[:STATUS_FIELD_WIDTH]
        entries.append((status, field[STATUS_PATH_START:]))

        if any(code in STATUS_CODES_WITH_SOURCE_PATH for code in status):
            index += 1

    return entries


def is_allowed(path, patterns):
    is_directory = path.endswith(DIRECTORY_SUFFIX)
    components = path.rstrip(DIRECTORY_SUFFIX).split(DIRECTORY_SUFFIX)

    for depth in range(1, len(components) + 1):
        candidate = DIRECTORY_SUFFIX.join(components[:depth])
        candidate_is_directory = depth < len(components) or is_directory

        if any(pattern.matches(candidate, candidate_is_directory) for pattern in patterns):
            return True

    return False


def leftover_paths():
    patterns = load_patterns()
    leftovers = []

    for status, path in git_status_entries():
        if status == STATUS_UNTRACKED:
            leftovers.append(path)
        elif status == STATUS_IGNORED and not is_allowed(path, patterns):
            leftovers.append(path)

    return sorted(leftovers)


def main():
    if len(sys.argv) != 1:
        print("usage: python3 scripts/check-ignored-files.py", file=sys.stderr)
        return EXIT_FAILED

    try:
        leftovers = leftover_paths()
    except CheckFailed as error:
        print(f"check-ignored-files: {error}", file=sys.stderr)
        return EXIT_FAILED

    if not leftovers:
        return EXIT_CLEAN

    sys.stdout.reconfigure(errors="backslashreplace")

    for path in leftovers:
        print(path)

    print(FIX_LINE)
    return EXIT_LEFTOVERS


if __name__ == "__main__":
    sys.exit(main())
