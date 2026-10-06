import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SOURCE_DIRECTORIES = ("maxon-bin", "stdlib", "runtime")
REGION_BEGIN = "// @generated library-cache begin"
REGION_END = "// @generated library-cache end"

VISIBILITY = r"(?:(?:export|module|public)\s+)?"
DECLARATION = re.compile(r"^" + VISIBILITY + r"(type|union|enum|interface)\s+([A-Za-z_][A-Za-z0-9_]*)(.*)$")
ALIAS = re.compile(r"^" + VISIBILITY + r"typealias\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$")
FIELD_TYPED = re.compile(r"^\t(?:(export|module|public)\s+)?(var|let)\s+([A-Za-z_][A-Za-z0-9_]*)\s+as\s+(.+)$")
FIELD_INFERRED = re.compile(r"^\t(?:(export|module|public)\s+)?(var|let)\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.+)$")
STATIC_MEMBER = re.compile(r"^(?:(?:export|module|public)\s+)?static\b")
UNION_CASE = re.compile(r"^([a-z][A-Za-z0-9_]*)(?:\((.*)\))?(?:\s*=\s*.+)?\s*$")
ENUM_CASE = re.compile(r"^([A-Za-z][A-Za-z0-9_]*)\s*(?:=\s*.+)?\s*$")
MEMBER_KEYWORDS = re.compile(r"^(?:(?:export|module|public)\s+)?(?:static\s+)?(?:function|var|let)\s+[A-Za-z_]")
PAYLOAD_PARAMETER = re.compile(r"^([A-Za-z_][A-Za-z0-9_]*)\s+(.+)$")
USES_CLAUSE = re.compile(r"\buses\s+(.+?)(?:\s+implements\b|\s+where\b|$)")


class Field:
    def __init__(self, visibility, mutability, name, type_text, default):
        self.visibility = visibility
        self.mutability = mutability
        self.name = name
        self.type_text = type_text
        self.default = default


class TypeDeclaration:
    def __init__(self, kind, name, path, visibility, uses):
        self.kind = kind
        self.name = name
        self.path = path
        self.visibility = visibility
        self.uses = uses
        self.fields = []
        self.cases = []
        self.enum_cases = []


class AliasDeclaration:
    def __init__(self, name, target, path, visibility):
        self.name = name
        self.target = target
        self.path = path
        self.visibility = visibility


class SourceFile:
    def __init__(self, path, lines, carriage_returns):
        self.path = path
        self.lines = lines
        self.carriage_returns = carriage_returns

    def text(self):
        joined = "\n".join(self.lines)

        if self.carriage_returns:
            return joined.replace("\n", "\r\n")

        return joined


def relative(path):
    return os.path.relpath(path, ROOT).replace("\\", "/")


def source_paths():
    found = []

    for directory in SOURCE_DIRECTORIES:
        for current, subdirectories, names in os.walk(os.path.join(ROOT, directory)):
            subdirectories[:] = sorted(d for d in subdirectories if not d.startswith("."))

            for name in sorted(names):
                if name.endswith(".maxon"):
                    found.append(os.path.join(current, name))

    return found


def read_source(path):
    with open(path, encoding="utf8", newline="") as handle:
        text = handle.read()

    carriage_returns = "\r\n" in text

    return SourceFile(path, text.replace("\r\n", "\n").split("\n"), carriage_returns)


def without_regions(lines):
    kept = []
    inside = False

    for line in lines:
        marker = line.strip()

        if marker == REGION_BEGIN:
            if inside:
                raise ValueError("a generated region opens inside another")

            inside = True

            if kept and kept[-1] == "":
                kept.pop()

            continue

        if marker == REGION_END:
            if not inside:
                raise ValueError("a generated region closes without opening")

            inside = False
            continue

        if not inside:
            kept.append(line)

    if inside:
        raise ValueError("a generated region never closes")

    return kept


def split_top_level(text, separator=","):
    parts = []
    depth = 0
    current = ""

    for ch in text:
        if ch in "([{":
            depth += 1

        if ch in ")]}":
            depth -= 1

        if ch == separator and depth == 0:
            parts.append(current)
            current = ""
        else:
            current += ch

    parts.append(current)

    return [part.strip() for part in parts if part.strip()]


def split_default(text):
    depth = 0

    for index, ch in enumerate(text):
        if ch in "([{":
            depth += 1

        if ch in ")]}":
            depth -= 1

        if ch == "=" and depth == 0:
            return text[:index].strip(), text[index + 1:].strip()

    return text.strip(), None


def strip_comment(text):
    index = text.find("//")

    if index >= 0:
        return text[:index].rstrip()

    return text


def visibility_of(line):
    match = re.match(r"^(export|module|public)\s+", line)

    return match.group(1) if match else "private"


def parse_lines(path, lines):
    types = []
    aliases = []
    current = None

    for raw in lines:
        line = raw.rstrip("\r")

        if current is None:
            match = ALIAS.match(line)

            if match:
                aliases.append(AliasDeclaration(match.group(1), strip_comment(match.group(2)).strip(), path, visibility_of(line)))
                continue

            match = DECLARATION.match(line)

            if match and not line.lstrip().startswith("//"):
                uses = USES_CLAUSE.search(match.group(3))
                current = TypeDeclaration(match.group(1), match.group(2), path, visibility_of(line), [x.strip() for x in uses.group(1).split(",")] if uses else [])

            continue

        if re.match(r"^end '" + re.escape(current.name) + r"'", line):
            types.append(current)
            current = None
            continue

        if not line.startswith("\t") or line.startswith("\t\t"):
            continue

        body = line[1:]

        if body.lstrip().startswith("//"):
            continue

        body = strip_comment(body)

        if current.kind == "type":
            if STATIC_MEMBER.match(body):
                continue

            match = FIELD_TYPED.match(line)

            if match:
                type_text, default = split_default(strip_comment(match.group(4)))
                current.fields.append(Field(match.group(1) or "private", match.group(2), match.group(3), type_text, default))
                continue

            match = FIELD_INFERRED.match(line)

            if match:
                current.fields.append(Field(match.group(1) or "private", match.group(2), match.group(3), None, strip_comment(match.group(4)).strip()))
        elif current.kind == "union":
            if UNION_CASE.match(body) and not MEMBER_KEYWORDS.match(body):
                current.cases.append(body)
        elif current.kind == "enum":
            match = ENUM_CASE.match(body)

            if match and not MEMBER_KEYWORDS.match(body):
                current.enum_cases.append(match.group(1))

    return types, aliases


def parse_union_case(body):
    name = re.match(r"^([a-z][A-Za-z0-9_]*)", body).group(1)
    rest = body[len(name):]
    payload = []

    if rest.startswith("("):
        depth = 0

        for index, ch in enumerate(rest):
            if ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1

                if depth == 0:
                    for part in split_top_level(rest[1:index]):
                        parameter = PAYLOAD_PARAMETER.match(part)
                        payload.append((parameter.group(1), parameter.group(2).strip()))

                    break

    return name, payload


class Tree:
    def __init__(self, sources):
        self.sources = sources
        self.types = {}
        self.aliases = {}

        for path, source in sources.items():
            types, aliases = parse_lines(path, without_regions(source.lines))

            for declaration in types:
                self.types.setdefault(declaration.name, declaration)

            for alias in aliases:
                self.aliases.setdefault(alias.name, alias)


def load_tree():
    sources = {}

    for path in source_paths():
        sources[path] = read_source(path)

    return Tree(sources)
