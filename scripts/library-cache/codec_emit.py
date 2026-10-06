import re

import codec_config as config
from maxon_source import parse_union_case, split_top_level

CONTAINER_KINDS = ("array", "list", "set", "map")
RESOLUTION_DEPTH_LIMIT = 20


class Unresolved(Exception):
    pass


class Node:
    def __init__(self, kind, **fields):
        self.kind = kind
        self.__dict__.update(fields)


def capitalized(name):
    return name[0].upper() + name[1:]


def parse_range(text):
    match = re.match(r"^int\((.+?)\s+to\s+(.+?)\)", text)

    if not match:
        return None

    return match.group(1).strip(), match.group(2).strip()


def is_signed_lower_bound(bound):
    return bound in config.SIGNED_LOWER_BOUNDS or bound.startswith("-")


def split_with(text):
    match = re.match(r"^([A-Za-z_][A-Za-z0-9_]*)\s+with\s+(.+)$", text)

    if not match:
        return None

    head = match.group(1)
    argument = re.sub(r"\s+implements\s+.*$", "", match.group(2).strip())

    if argument.startswith("(") and argument.endswith(")"):
        return head, split_top_level(argument[1:-1])

    return head, [argument]


class Emitter:
    def __init__(self, tree):
        self.tree = tree
        self.touched_aliases = set()

    def resolve(self, type_text, depth=0):
        text = type_text.strip()

        if depth > RESOLUTION_DEPTH_LIMIT:
            raise Unresolved(text)

        if text == config.BUILTIN_BOOL:
            return Node("bool", text=text)

        if text in config.CUSTOM_TYPES:
            return Node("custom", name=text, text=text)

        if text == config.BUILTIN_STRING:
            return Node("string", text=text)

        if text == config.BUILTIN_PATH:
            return Node("path", text=text)

        if text == config.BUILTIN_BYTES:
            return Node("bytes", text=text)

        if text in self.tree.aliases:
            self.touched_aliases.add(text)

            return self.resolve_alias(text, depth)

        declaration = self.tree.types.get(text)

        if declaration is not None:
            return Node({"type": "record", "union": "union", "enum": "enum"}[declaration.kind], text=text, name=text)

        raise Unresolved(text)

    def resolve_alias(self, text, depth):
        target = re.sub(r"\s+implements\s+.*$", "", self.tree.aliases[text].target)
        bounds = parse_range(target)

        if bounds:
            return Node("sint" if is_signed_lower_bound(bounds[0]) else "uint", text=text, low=bounds[0], high=bounds[1])

        width = re.match(r"^bits\((\d+)\)", target)

        if width:
            return Node("uint", text=text, low=config.UNSIGNED_FLOOR, high=config.UNSIGNED_CEILING if int(width.group(1)) >= config.WORD_BITS else str((1 << int(width.group(1))) - 1))

        if re.match(r"^float\(", target):
            return Node("real", text=text)

        applied = split_with(target)

        if applied:
            head, arguments = applied

            if head in ("Array", "Set", "List"):
                return Node(head.lower(), text=text, alias=text, elem=arguments[0])

            if head == "Map":
                return Node("map", text=text, alias=text, key=arguments[0], value=arguments[1])

            raise Unresolved("%s = %s" % (text, target))

        if re.match(r"^[A-Za-z_][A-Za-z0-9_]*$", target):
            inner = self.resolve(target, depth + 1)

            if inner.kind in CONTAINER_KINDS:
                inner = Node(inner.kind, **{k: v for k, v in inner.__dict__.items() if k != "kind"})
                inner.alias = text

            inner.text = text

            return inner

        raise Unresolved("%s = %s" % (text, target))

    def field_node(self, declaration, field):
        if (declaration.name, field.name) in config.SKIPPED_FIELDS:
            return None

        if field.type_text is None:
            if field.default in ("true", "false"):
                return Node("bool", text="bool")

            raise Unresolved("shorthand field %s.%s = %s" % (declaration.name, field.name, field.default))

        return self.resolve(field.type_text)

    def write_statement(self, node, value, indent):
        tabs = "\t" * indent
        kind = node.kind

        if kind == "bool":
            return tabs + "writer.flag(%s)" % value

        if kind == "uint":
            return tabs + "writer.unsigned(%s as CacheUnsigned)" % value

        if kind == "sint":
            return tabs + "writer.signed(%s as CacheSigned)" % value

        if kind == "real":
            return tabs + "writer.real(%s as ParsedFloat)" % value

        if kind == "bytes":
            return tabs + "writer.bytes(%s)" % value

        if kind == "string":
            return tabs + "writer.text(%s)" % value

        if kind == "path":
            return tabs + "writer.text(%s.toString())" % value

        if kind == "enum":
            return tabs + "writer.unsigned(%s.ordinal as CacheUnsigned)" % value

        if kind == "record":
            return tabs + "%s.cacheWrite(writer)" % value

        if kind in ("union", "custom"):
            return tabs + "cacheWrite%s(writer, value: %s)" % (node.name, value)

        if kind in CONTAINER_KINDS:
            return tabs + "cacheWrite%s(writer, value: %s)" % (node.alias, value)

        raise Unresolved("write %s" % kind)

    def read_statement(self, node, variable, indent):
        tabs = "\t" * indent
        kind = node.kind

        if kind == "bool":
            return tabs + "let %s = try reader.flag()" % variable

        if kind in ("uint", "sint", "real"):
            return tabs + "let %s = (try reader.%s) as %s" % (variable, self.read_call(node), node.text)

        if kind == "bytes":
            return tabs + "let %s = try reader.bytes()" % variable

        if kind == "string":
            return tabs + "let %s = try reader.text()" % variable

        if kind == "path":
            return tabs + "let %s = try reader.path()" % variable

        if kind == "record":
            return tabs + "let %s = try %s.cacheRead(reader)" % (variable, node.name)

        if kind in ("enum", "union", "custom"):
            return tabs + "let %s = try cacheRead%s(reader)" % (variable, node.name)

        if kind in CONTAINER_KINDS:
            return tabs + "let %s = try cacheRead%s(reader)" % (variable, node.alias)

        raise Unresolved("read %s" % kind)

    def read_call(self, node):
        if node.kind == "real":
            return "real()"

        full = (config.UNSIGNED_FLOOR, config.UNSIGNED_CEILING) if node.kind == "uint" else (config.SIGNED_FLOOR, config.SIGNED_CEILING)
        method = "Unsigned" if node.kind == "uint" else "Signed"

        if (node.low, node.high) == full:
            return method.lower() + "()"

        return "bounded%s(%s, high: %s)" % (method, node.low, node.high)

    def bounded_alias_readers(self):
        out = []

        for name in config.HAND_READ_ALIASES:
            node = self.resolve(name)
            assert node.kind in ("uint", "sint"), name
            out.append("")
            out.append("module function cacheRead%s(reader LibraryCacheReader) returns %s throws LibraryCacheDamage" % (name, name))
            out.append("	return (try reader.%s) as %s" % (self.read_call(node), name))
            out.append("end 'cacheRead%s'" % name)

        return out

    def alias_helpers(self, name, visibility):
        node = self.resolve(name)
        assert node.kind in CONTAINER_KINDS, name
        write_name = "cacheWrite%s" % name
        read_name = "cacheRead%s" % name
        out = [""]
        out.append("%sfunction %s(writer LibraryCacheWriter, value %s)" % (visibility, write_name, name))
        out.append("\twriter.unsigned(value.count() as CacheUnsigned)")
        out.append("")

        if node.kind == "map":
            out.append("\tfor (key, entry) in value 'eachEntry'")
            out.append(self.write_statement(self.resolve(node.key), "key", 2))
            out.append(self.write_statement(self.resolve(node.value), "entry", 2))
            out.append("\tend 'eachEntry'")
        else:
            out.append("\tfor item in value 'eachItem'")
            out.append(self.write_statement(self.resolve(node.elem), "item", 2))
            out.append("\tend 'eachItem'")

        out.append("end '%s'" % write_name)
        out.append("")
        out.append("%sfunction %s(reader LibraryCacheReader) returns %s throws LibraryCacheDamage" % (visibility, read_name, name))
        out.append("\tlet count = try reader.count()")
        out.append("\tvar result = %s.create()" % name)

        if node.kind == "array":
            out.append("\tresult.reserve(count)")

        out.append("")

        if node.kind == "map":
            out.append("\tfor _ in 0 upto count 'eachEntry'")
            out.append(self.read_statement(self.resolve(node.key), "key", 2))
            out.append(self.read_statement(self.resolve(node.value), "entry", 2))
            out.append("\t\tresult.upsert(key, value: entry)")
            out.append("\tend 'eachEntry'")
        else:
            adder = {"set": "insert", "list": "append", "array": "push"}[node.kind]
            out.append("\tfor _ in 0 upto count 'eachItem'")
            out.append(self.read_statement(self.resolve(node.elem), "item", 2))
            out.append("\t\tresult.%s(item)" % adder)
            out.append("\tend 'eachItem'")

        out.append("")
        out.append("\treturn result")
        out.append("end '%s'" % read_name)

        return out

    def record_methods(self, declaration, visibility):
        writes = ["\t" + visibility + "function cacheWrite(writer LibraryCacheWriter)"]
        reads = ["\t" + visibility + "static function cacheRead(reader LibraryCacheReader) returns %s throws LibraryCacheDamage" % declaration.name]
        literal = []
        written = 0

        for field in declaration.fields:
            node = self.field_node(declaration, field)

            if node is None:
                literal.append("%s: %s" % (field.name, config.SKIPPED_FIELDS[(declaration.name, field.name)]))
                continue

            writes.append(self.write_statement(node, "self.%s" % field.name, 2))
            written += 1
            variable = "f_" + field.name
            reads.append(self.read_statement(node, variable, 2))
            literal.append("%s: %s" % (field.name, variable))

        if not written:
            writes.append("\t\twriter.flag(true)")
            reads.append("\t\t_ = try reader.flag()")

        writes.append("\tend 'cacheWrite'")
        reads.append("")
        reads.append("\t\treturn Self{%s}" % ", ".join(literal))
        reads.append("\tend 'cacheRead'")
        reads.append("")

        # The cast stops compiling once the record's size moves off one slot per field it had when
        # this was generated, so a field added without re-running the generator cannot go unwritten.
        reads.append("\tstatic function cacheLayoutBytes() returns %s" % self.size_alias(declaration))
        reads.append("\t\treturn sizeof(%s) as %s" % (declaration.name, self.size_alias(declaration)))
        reads.append("\tend 'cacheLayoutBytes'")

        return writes + [""] + reads

    def record_size_bytes(self, declaration):
        return config.SLOT_BYTES * len(declaration.fields)

    def size_alias(self, declaration):
        return "CacheSize%d" % self.record_size_bytes(declaration)

    def size_aliases(self, sizes):
        out = []

        for size, visibility in sorted(sizes.items()):
            out.append("")
            out.append("%stypealias CacheSize%d = int(%d to %d)" % (visibility, size, size, size))

        return out

    def refusing_methods(self, declaration, visibility):
        return [
            "\t" + visibility + "function cacheWrite(writer LibraryCacheWriter)",
            "\t\twriter.refuse()",
            "\tend 'cacheWrite'",
            "",
            "\t" + visibility + "static function cacheRead(_ LibraryCacheReader) returns %s throws LibraryCacheDamage" % declaration.name,
            "\t\tthrow LibraryCacheDamage.unrepresentable",
            "\tend 'cacheRead'",
        ]

    def enum_helpers(self, declaration, visibility):
        name = declaration.name
        table = "CacheCases%s" % name

        if not declaration.enum_cases:
            raise Unresolved("enum %s declares no cases" % name)

        # Decoded through a listed table because `allCases` builds an array on every call. The table
        # is checked against `allCases` before a cache directory is used (`cacheEnumTablesAgree`).
        listed = ", ".join("%s.%s" % (name, case) for case in declaration.enum_cases)
        out = ["", "let %s = [%s]" % (table, listed)]
        out.append("")
        out.append("%sfunction cacheRead%s(reader LibraryCacheReader) returns %s throws LibraryCacheDamage" % (visibility, name, name))
        out.append("\tlet ordinal = try reader.unsigned()")
        out.append("")
        out.append("\treturn try %s.get(ordinal as ElementIndex) otherwise throw LibraryCacheDamage.unknownTag(ordinal)" % table)
        out.append("end 'cacheRead%s'" % name)
        out.append("")
        out.append("function cacheTableAgrees%s() returns bool" % name)
        out.append("\tlet declared = %s.allCases" % name)
        out.append("")
        out.append("\tif %s.count() != declared.count() 'differentCount'" % table)
        out.append("\t\treturn false")
        out.append("\tend 'differentCount'")
        out.append("")
        out.append("\tfor position in 0 upto declared.count() 'eachCase'")
        out.append("\t\tlet listed = try %s.get(position) otherwise return false" % table)
        out.append("\t\tlet expected = try declared.get(position) otherwise return false")
        out.append("")
        out.append("\t\tif listed != expected 'anotherCase'")
        out.append("\t\t\treturn false")
        out.append("\t\tend 'anotherCase'")
        out.append("\tend 'eachCase'")
        out.append("")
        out.append("\treturn true")
        out.append("end 'cacheTableAgrees%s'" % name)

        return out

    def union_cases(self, declaration):
        return [parse_union_case(body) for body in declaration.cases]

    def union_helpers(self, declaration, visibility):
        name = declaration.name
        cases = self.union_cases(declaration)
        out = [""]
        carries_payload = any(payload for (_, payload) in cases)
        out.append("%sfunction cacheWrite%s(writer LibraryCacheWriter, value %s)" % (visibility, name, name))
        out.append("\twriter.unsigned(value.ordinal as CacheUnsigned)")

        if carries_payload:
            out.append("")
            out.append("\tmatch value 'payload'")

            for case, payload in cases:
                if payload:
                    bindings = ", ".join("a%d" % i for i in range(len(payload)))
                    arguments = ", ".join("a%d: a%d" % (i, i) for i in range(len(payload)))
                    spacing = " " if case in config.SPACED_PAYLOAD_CASES else ""
                    out.append("\t\t%s%s(%s) then cacheWrite%s%s(writer, %s)" % (case, spacing, bindings, name, capitalized(case), arguments))
                else:
                    out.append("\t\t%s then break 'payload'" % case)

            out.append("\tend 'payload'")

        out.append("end 'cacheWrite%s'" % name)

        for case, payload in cases:
            if not payload:
                continue

            parameters = ", ".join("a%d %s" % (i, payload_type) for i, (_, payload_type) in enumerate(payload))
            out.append("")
            out.append("function cacheWrite%s%s(writer LibraryCacheWriter, %s)" % (name, capitalized(case), parameters))

            for i, (_, payload_type) in enumerate(payload):
                out.append(self.write_statement(self.resolve(payload_type), "a%d" % i, 1))

            out.append("end 'cacheWrite%s%s'" % (name, capitalized(case)))

        out.append("")
        out.append("%sfunction cacheRead%s(reader LibraryCacheReader) returns %s throws LibraryCacheDamage" % (visibility, name, name))
        out.append("\tlet tag = try reader.unsigned()")
        out.append("")
        out.append("\treturn match tag 'case'")

        for i, (case, payload) in enumerate(cases):
            if payload:
                out.append("\t\t%d gives try cacheRead%s%s(reader)" % (i, name, capitalized(case)))
            else:
                out.append("\t\t%d gives %s.%s" % (i, name, case))

        out.append("\t\tdefault throws LibraryCacheDamage.unknownTag(tag)")
        out.append("\tend 'case'")
        out.append("end 'cacheRead%s'" % name)

        for case, payload in cases:
            if not payload:
                continue

            out.append("")
            out.append("function cacheRead%s%s(reader LibraryCacheReader) returns %s throws LibraryCacheDamage" % (name, capitalized(case), name))

            for i, (_, payload_type) in enumerate(payload):
                out.append(self.read_statement(self.resolve(payload_type), "a%d" % i, 1))

            out.append("")
            arguments = ", ".join("a0" if i == 0 else "%s: a%d" % (label, i) for i, (label, _) in enumerate(payload))
            out.append("\treturn %s.%s(%s)" % (name, case, arguments))
            out.append("end 'cacheRead%s%s'" % (name, capitalized(case)))

        return out

    def module_helpers(self):
        declaration = self.tree.types["IrModule"]
        columns = [field for field in declaration.fields if field.name in ("functions", "blocks", "ops", "spans")]
        out = [""]
        out.append("export function cacheWriteMaxonModule(writer LibraryCacheWriter, value MaxonModule)")
        reads = []

        for field in columns:
            if field.name == "ops":
                out.append("\twriter.unsigned(value.ops.count() as CacheUnsigned)")
                out.append("")
                out.append("\tfor op in value.ops 'eachOp'")
                out.append("\t\tcacheWriteMaxonOp(writer, value: op)")
                out.append("\tend 'eachOp'")
                out.append("")
                reads.append((field.name, None))
                continue

            node = self.resolve(field.type_text)
            out.append(self.write_statement(node, "value.%s" % field.name, 1))
            reads.append((field.name, node))

        out.append("end 'cacheWriteMaxonModule'")
        out.append("")
        out.append("export function cacheReadMaxonModule(reader LibraryCacheReader) returns MaxonModule throws LibraryCacheDamage")
        out.append("\tvar assembled = MaxonModule.create()")
        out.append("")

        for name, node in reads:
            if node is None:
                out.append("\tlet opCount = try reader.count()")
                out.append("")
                out.append("\tfor _ in 0 upto opCount 'eachOp'")
                out.append("\t\tassembled.ops.push(try cacheReadMaxonOp(reader))")
                out.append("\tend 'eachOp'")
                out.append("")
                continue

            out.append(self.read_statement(node, "f_" + name, 1))
            out.append("\tassembled.%s = f_%s" % (name, name))

        out.append("")
        out.append("\treturn assembled")
        out.append("end 'cacheReadMaxonModule'")

        return out

    def agreement_function(self, prefix, name, checks):
        out = ["", "%sfunction %s() returns bool" % (prefix, name)]

        for check in checks:
            out.append("	if not %s() 'disagrees%s'" % (check, capitalized(check)))
            out.append("		return false")
            out.append("	end 'disagrees%s'" % capitalized(check))
            out.append("")

        out.append("	return true")
        out.append("end '%s'" % name)

        return out

    def enum_file_aggregate(self, name, visibility, enums):
        return self.agreement_function(visibility, name, ["cacheTableAgrees%s" % enum for enum in enums])

    def enum_tables_aggregate(self, calls, host_enums):
        return self.agreement_function("export ", "cacheEnumTablesAgree", calls + ["cacheTableAgrees%s" % enum for enum in host_enums])
