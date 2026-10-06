#!/usr/bin/env python3
import argparse
import difflib
import hashlib
import os
import re
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import codec_config as config
from codec_emit import CONTAINER_KINDS, Emitter, Unresolved
from maxon_source import DECLARATION, ALIAS, REGION_BEGIN, REGION_END, load_tree, relative, strip_comment, without_regions

NORMALIZED = os.path.normcase

FREE_DECLARATION = re.compile(r"^(?:(?:export|module) )?(?:function (cache[A-Za-z0-9_]+)\(|let (CacheCases[A-Za-z0-9_]+) =)")

CODEC_PARAMETER = re.compile(r"[(,]\s*[A-Za-z_][A-Za-z0-9_]*\s+LibraryCache(?:Writer|Reader)\b")

CODEC_TYPE_DECLARATION = re.compile(r"^\s*(?:(?:export|module|public)\s+)?(?:extension\s+|typealias\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*)LibraryCache(?:Writer|Reader)\b")

VISIBILITY_RANK = {"private": 0, "module": 1, "export": 2, "public": 3}

EXIT_CLEAN = 0
EXIT_DRIFT = 1
EXIT_FAILED = 2


class CodecOutsideHandCodecFiles(Exception):
    pass


class Plan:
    def __init__(self, tree):
        self.tree = tree
        self.emitter = Emitter(tree)

    def is_library_path(self, path):
        text = relative(path)

        return text.startswith("stdlib/") or text.startswith("runtime/")

    def dependencies(self, item):
        kind, name = item
        found = []

        if kind == "alias":
            node = self.emitter.resolve(name)

            if node.kind == "map":
                found += self.node_dependencies(self.emitter.resolve(node.key)) + self.node_dependencies(self.emitter.resolve(node.value))
            else:
                found += self.node_dependencies(self.emitter.resolve(node.elem))

            return found

        declaration = self.tree.types[name]

        if name in config.CUSTOM_TYPES or name in config.REFUSING_TYPES:
            return found

        if declaration.kind == "type":
            for field in declaration.fields:
                node = self.emitter.field_node(declaration, field)

                if node is not None:
                    found += self.node_dependencies(node)
        elif declaration.kind == "union":
            for _, payload in self.emitter.union_cases(declaration):
                for _, payload_type in payload:
                    found += self.node_dependencies(self.emitter.resolve(payload_type))

        return found

    def node_dependencies(self, node):
        if node.kind in ("record", "union", "enum", "custom"):
            return [("type", node.name)]

        if node.kind in CONTAINER_KINDS:
            return [("alias", node.alias)]

        return []

    def item_path(self, item):
        kind, name = item

        if kind == "alias":
            path = self.tree.aliases[name].path

            return config.GENERATED_HOST if self.is_library_path(path) else path

        path = self.tree.types[name].path
        protected = os.path.basename(path) in config.PROTECTED_FILES or "/Runtime/" in relative(path)

        return config.GENERATED_HOST if protected else path

    def named_ranks(self, item, seen=None):
        seen = seen if seen is not None else set()
        kind, name = item
        ranks = []

        if kind == "type":
            return [VISIBILITY_RANK[self.tree.types[name].visibility]]

        if name in seen:
            return ranks

        seen.add(name)
        ranks.append(VISIBILITY_RANK[self.tree.aliases[name].visibility])
        node = self.emitter.resolve(name)
        parts = [node.key, node.value] if node.kind == "map" else [node.elem]

        for text in parts:
            sub = self.emitter.resolve(text)

            if sub.kind in ("record", "union", "enum", "custom"):
                if sub.name in self.tree.types:
                    ranks.append(VISIBILITY_RANK[self.tree.types[sub.name].visibility])
            elif sub.kind in CONTAINER_KINDS:
                ranks += self.named_ranks(("alias", sub.alias), seen)

        return ranks

    def visibility_prefix(self, item, external, user_files):
        if not external:
            return ""

        allowed = min(self.named_ranks(item))
        own_directory = NORMALIZED(os.path.dirname(self.item_path(item)))
        inside_subtree = all(NORMALIZED(f).startswith(own_directory + os.sep) for f in user_files)
        in_host = self.item_path(item) == config.GENERATED_HOST

        if allowed >= VISIBILITY_RANK["export"] and not inside_subtree:
            return "export "

        if allowed >= VISIBILITY_RANK["module"] and inside_subtree and not in_host:
            return "module "

        if allowed >= VISIBILITY_RANK["export"]:
            return "export "

        if allowed == VISIBILITY_RANK["module"]:
            return "module "

        raise Unresolved("%s is used from another file but is private to its own" % (item,))

    def closure(self):
        seen = set()
        order = []

        def visit(item):
            if item in seen:
                return

            seen.add(item)
            kind, name = item

            if kind == "type" and name in config.CUSTOM_TYPES:
                return

            for dependency in self.dependencies(item):
                visit(dependency)

            order.append(item)

        for root in config.ROOTS + config.HAND_CODEC_ROOTS:
            visit(root if isinstance(root, tuple) else ("type", root))

        return order

    def build(self):
        order = self.closure()
        users = {item: set() for item in order}

        for item in order:
            for dependency in self.dependencies(item):
                if dependency in users:
                    users[dependency].add(NORMALIZED(self.item_path(item)))

        for root in config.ROOTS + config.HAND_CODEC_ROOTS:
            key = root if isinstance(root, tuple) else ("type", root)

            for entry in config.ENTRY_FILES:
                users.setdefault(key, set()).add(NORMALIZED(entry))

        emitted = {}
        errors = []

        for item in order:
            kind, name = item
            external = (self.item_path(item) == config.GENERATED_HOST) or any(f != NORMALIZED(self.item_path(item)) for f in users.get(item, set()))

            try:
                visibility = self.visibility_prefix(item, external, users.get(item, set()))
                emitted[item] = self.emit(kind, name, visibility)
            except Unresolved as failure:
                errors.append((item, str(failure)))

        self.check_name_collisions(emitted, errors)

        return order, emitted, errors

    def emit(self, kind, name, visibility):
        if kind == "alias":
            return ("alias", self.emitter.alias_helpers(name, visibility))

        declaration = self.tree.types[name]

        if name in config.REFUSING_TYPES:
            return ("record", self.emitter.refusing_methods(declaration, visibility))

        if declaration.kind == "type":
            return ("record", self.emitter.record_methods(declaration, visibility))

        if declaration.kind == "union":
            return ("union", self.emitter.union_helpers(declaration, visibility))

        if declaration.kind == "enum":
            return ("enum", self.emitter.enum_helpers(declaration, visibility))

        raise Unresolved("%s is a %s" % (name, declaration.kind))

    def check_name_collisions(self, emitted, errors):
        seen = {}

        for item, (_, block) in emitted.items():
            for line in block:
                match = FREE_DECLARATION.match(line)

                if not match:
                    continue

                declared = match.group(1) or match.group(2)

                if declared in seen and seen[declared] != item:
                    errors.append((item, "name collision %s with %s" % (declared, seen[declared])))

                seen[declared] = item


def region(lines, indent):
    return [indent + REGION_BEGIN] + lines + ["", indent + REGION_END]


def declaration_span(lines, name):
    start = None

    for index, line in enumerate(lines):
        if start is None:
            match = DECLARATION.match(line)

            if match and match.group(2) == name and not line.startswith("\t"):
                start = index

        if start is not None and line == "end '%s'" % name:
            return start, index

    raise ValueError("no declaration of %s with a matching end" % name)


def alias_line(lines, name):
    for index, line in enumerate(lines):
        match = ALIAS.match(line)

        if match and match.group(1) == name and not line.startswith("\t"):
            return index

    raise ValueError("no typealias %s" % name)


def with_generated(source, items, emitted):
    lines = without_regions(source.lines)
    inserts = {}

    for item in items:
        kind, name = item
        shape, block = emitted[item]

        if kind == "alias":
            anchor = alias_line(lines, name) + 1
            addition = [""] + region(block[1:], "")
        elif shape == "record":
            _, end = declaration_span(lines, name)
            anchor = end
            addition = [""] + region(block, "\t")
        else:
            _, end = declaration_span(lines, name)
            anchor = end + 1
            addition = [""] + region(block[1:], "")

        inserts.setdefault(anchor, []).extend(addition)

    for anchor in sorted(inserts, reverse=True):
        lines[anchor:anchor] = inserts[anchor]

    return lines


def aggregate_name(path):
    words = re.split(r"[^A-Za-z0-9]+", relative(path).replace("maxon-bin/", "").replace(".maxon", ""))

    return "cacheEnumTablesAgreeIn" + "".join(word[0].upper() + word[1:] for word in words if word)


def attach_enum_aggregates(by_file, emitted, emitter):
    calls = []
    host_enums = []

    for path, items in by_file.items():
        enums = [item for item in items if emitted[item][0] == "enum"]

        if not enums:
            continue

        if path == config.GENERATED_HOST:
            host_enums = [item[1] for item in enums]
            continue

        name = aggregate_name(path)
        visibility = "module " if os.path.dirname(path) == os.path.dirname(config.GENERATED_HOST) else "export "
        last = enums[-1]
        shape, block = emitted[last]
        emitted[last] = (shape, block + emitter.enum_file_aggregate(name, visibility, [item[1] for item in enums]))
        calls.append(name)

    return calls, host_enums


def size_alias_visibilities(plan, order, emitted):
    users = {}

    for item in order:
        if item not in emitted or emitted[item][0] != "record" or item[0] != "type":
            continue

        declaration = plan.tree.types[item[1]]

        if item[1] in config.REFUSING_TYPES:
            continue

        users.setdefault(plan.emitter.record_size_bytes(declaration), set()).add(NORMALIZED(plan.item_path(item)))

    host = NORMALIZED(config.GENERATED_HOST)
    visibilities = {}

    for size, files in users.items():
        if files == {host}:
            visibilities[size] = ""
        elif all(f.startswith(os.path.dirname(host) + os.sep) for f in files | {host}):
            visibilities[size] = "module "
        else:
            visibilities[size] = "export "

    return visibilities


def declaration_schema(declaration, emitter):
    lines = ["%s %s" % (declaration.kind, declaration.name)]

    if declaration.kind == "type":
        lines.append("size %d" % emitter.record_size_bytes(declaration))

        for field in declaration.fields:
            lines.append("field %s as %s" % (field.name, field.type_text if field.type_text is not None else "inferred from %s" % field.default))

    for case in declaration.cases:
        lines.append("case %s" % case)

    for case in declaration.enum_cases:
        lines.append("case %s" % case)

    return lines


def schema_text(plan, order):
    emitter = plan.emitter
    lines = []
    described = set()
    names = [item[1] for item in order if item[0] == "type"]
    names += sorted(config.CUSTOM_TYPES | config.SCHEMA_HOST_TYPES)

    for name in names:
        if name in described or name not in plan.tree.types:
            continue

        described.add(name)
        lines += declaration_schema(plan.tree.types[name], emitter)

    for item in order:
        if item[0] == "alias":
            lines.append("alias %s = %s" % (item[1], plan.tree.aliases[item[1]].target))

    for name in sorted(emitter.touched_aliases):
        lines.append("alias %s = %s" % (name, plan.tree.aliases[name].target))

    return lines


def hand_codec_lines(tree):
    sources = {NORMALIZED(path): source for path, source in tree.sources.items()}
    lines = []

    for path in sorted(config.HAND_CODEC_FILES, key=relative):
        lines.append("file %s" % relative(path))
        lines += without_regions(sources[NORMALIZED(path)].lines)

    return lines


# Hand-written codecs reach the fingerprint only as their files, hashed whole, so one declared
# anywhere else would change the wire format without changing the fingerprint.
def require_codecs_in_hand_codec_files(tree):
    hand_files = {NORMALIZED(path) for path in config.HAND_CODEC_FILES}
    scanned = NORMALIZED(config.CODEC_SCAN_DIRECTORY) + os.sep
    present = {NORMALIZED(path) for path in tree.sources}
    missing = sorted(relative(path) for path in config.HAND_CODEC_FILES if NORMALIZED(path) not in present)
    found = []

    if missing:
        raise CodecOutsideHandCodecFiles("HAND_CODEC_FILES names %s, which is not a compiler source" % ", ".join(missing))

    for path, source in tree.sources.items():
        if NORMALIZED(path) in hand_files or not NORMALIZED(path).startswith(scanned):
            continue

        inside = False

        for number, line in enumerate(source.lines, 1):
            marker = line.strip()

            if marker in (REGION_BEGIN, REGION_END):
                inside = marker == REGION_BEGIN
            elif not inside and line_extends_the_codec(line):
                found.append("%s:%d: %s" % (relative(path), number, marker))

    if found:
        owners = ", ".join(relative(path) for path in sorted(config.HAND_CODEC_FILES, key=relative))

        raise CodecOutsideHandCodecFiles("a LibraryCacheWriter or LibraryCacheReader parameter, extension or typealias is declared outside a generated region and outside the files that define the wire format (%s); move it into one of them:\n  %s" % (owners, "\n  ".join(found)))


def line_extends_the_codec(line):
    code = strip_comment(line)

    return CODEC_PARAMETER.search(code) is not None or CODEC_TYPE_DECLARATION.match(code) is not None


# Every entry header carries this, and a reader rejects an entry carrying another, so it must cover
# everything an entry's bytes depend on: the generated code, the declared shape of every coded type,
# and the hand-codec files, comments included.
def schema_fingerprint(plan, order, emitted, leading, trailing):
    lines = []

    for item in order:
        if item in emitted:
            lines += emitted[item][1]

    lines += leading + trailing
    lines += schema_text(plan, order)
    digest = hashlib.sha256("\n".join(lines + hand_codec_lines(plan.tree)).encode("utf8")).hexdigest()

    return "0x%s" % digest[:config.FINGERPRINT_HEX_DIGITS].upper()


def fingerprint_lines(fingerprint):
    return ["", "module let CacheSchemaFingerprint = %s as HashDigest" % fingerprint]


def host_lines(items, emitted, leading, trailing, fingerprint):
    body = []

    for item in items:
        body += emitted[item][1]

    body += leading
    body += fingerprint_lines(fingerprint)
    body += trailing

    while body and body[0] == "":
        body.pop(0)

    return region(body, "") + [""]


def desired_text(plan, order, emitted):
    by_file = {}

    for item in order:
        if item in emitted:
            by_file.setdefault(plan.item_path(item), []).append(item)

    calls, host_enums = attach_enum_aggregates(by_file, emitted, plan.emitter)
    sizes = size_alias_visibilities(plan, order, emitted)
    leading = plan.emitter.size_aliases(sizes) + plan.emitter.bounded_alias_readers()
    trailing = plan.emitter.module_helpers() + plan.emitter.enum_tables_aggregate(calls, host_enums)
    fingerprint = schema_fingerprint(plan, order, emitted, leading, trailing)
    desired = {}

    for path, source in plan.tree.sources.items():
        items = by_file.get(path, [])

        if path == config.GENERATED_HOST:
            lines = host_lines(items, emitted, leading, trailing, fingerprint)
        else:
            lines = with_generated(source, items, emitted)

        text = "\n".join(lines)
        desired[path] = text.replace("\n", "\r\n") if source.carriage_returns else text

    return desired


def drift_report(plan, desired):
    drifted = []

    for path, source in plan.tree.sources.items():
        if desired[path] != source.text():
            drifted.append(path)

    return drifted


def main():
    parser = argparse.ArgumentParser(description="Generate the library cache codecs inside the compiler sources, between the generated-region markers.")
    parser.add_argument("--check", action="store_true", help="write nothing; exit 1 when the sources differ from what the generator emits")
    arguments = parser.parse_args()

    plan = Plan(load_tree())

    try:
        require_codecs_in_hand_codec_files(plan.tree)
    except CodecOutsideHandCodecFiles as failure:
        print("cannot generate: %s" % failure, file=sys.stderr)

        return EXIT_FAILED

    order, emitted, errors = plan.build()

    if errors:
        for item, reason in errors:
            print("cannot generate %s: %s" % (item, reason), file=sys.stderr)

        return EXIT_FAILED

    desired = desired_text(plan, order, emitted)
    drifted = drift_report(plan, desired)

    if arguments.check:
        for path in drifted:
            print("stale: %s" % relative(path), file=sys.stderr)

            for line in list(difflib.unified_diff(plan.tree.sources[path].text().split("\n"), desired[path].split("\n"), lineterm="", n=0))[:20]:
                print("  " + line, file=sys.stderr)

        return EXIT_DRIFT if drifted else EXIT_CLEAN

    for path in drifted:
        with open(path, "w", encoding="utf8", newline="") as handle:
            handle.write(desired[path])

        print("wrote %s" % relative(path))

    print("%d item(s) generated, %d file(s) rewritten" % (len(emitted), len(drifted)))

    return EXIT_CLEAN


if __name__ == "__main__":
    sys.exit(main())
