import codec_config as config
from codec_emit import Unresolved

CLASS_KEPT = "kept"
CLASS_COUNTED = "counted"
CLASS_PARALLEL = "parallel"
CLASS_OP_KEYED = "opKeyed"
CLASS_CUSTOM = "custom"

CLASSES = (CLASS_KEPT, CLASS_COUNTED, CLASS_PARALLEL, CLASS_OP_KEYED, CLASS_CUSTOM)

MARK_ELEMENT_TYPE = "ElementIndex"
OP_LIMIT = "opLimit"
OP_MARK = "ops"


class Mark:
    def __init__(self, name, type_text, capture):
        self.name = name
        self.type_text = type_text
        self.capture = capture


class Requirement:
    def __init__(self, label, now, marked):
        self.label = label
        self.now = now
        self.marked = marked


class RewindPlan:
    def __init__(self, path, type_name, record_block, mark_block):
        self.path = path
        self.type_name = type_name
        self.record_block = record_block
        self.mark_block = mark_block


def classification_of(field):
    entry = config.ARTIFACT_REWIND.get(field.name)

    if entry is None:
        raise Unresolved("%s.%s has no rewind classification: add it to ARTIFACT_REWIND in codec_config.py as kept, counted, parallel, opKeyed or custom" % (config.ARTIFACT_REWIND_TYPE, field.name))

    if entry[0] not in CLASSES:
        raise Unresolved("%s.%s is classified %r, which is none of %s" % (config.ARTIFACT_REWIND_TYPE, field.name, entry[0], ", ".join(CLASSES)))

    return entry


def reject_stale_classifications(declaration):
    declared = {field.name for field in declaration.fields}

    for name in config.ARTIFACT_REWIND:
        if name not in declared:
            raise Unresolved("ARTIFACT_REWIND classifies %s.%s, which the type does not declare" % (config.ARTIFACT_REWIND_TYPE, name))


def rewind_plan(tree, path):
    declaration = tree.types[config.ARTIFACT_REWIND_TYPE]
    reject_stale_classifications(declaration)
    marks = []
    requirements = []
    op_keyed = []
    rewinds = []

    for field in declaration.fields:
        entry = classification_of(field)
        kind = entry[0]

        if kind == CLASS_KEPT:
            continue

        if kind == CLASS_COUNTED:
            marks.append(Mark(field.name, MARK_ELEMENT_TYPE, "artifact.%s.count()" % field.name))
            requirements.append(Requirement("artifact %s" % field.name, "artifact.%s.count()" % field.name, field.name))
            rewinds.append("self.%s.truncate(mark.%s)" % (field.name, field.name))
        elif kind == CLASS_PARALLEL:
            owner = entry[1]
            requirements.append(Requirement("artifact %s" % field.name, "artifact.%s.count()" % field.name, owner))
            rewinds.append("self.%s.truncate(mark.%s)" % (field.name, owner))
        elif kind == CLASS_OP_KEYED:
            op_keyed.append(field.name)
        else:
            custom = entry[1]
            marks += [Mark(*mark) for mark in custom["marks"]]
            requirements += [Requirement(*requirement) for requirement in custom["require"]]
            rewinds += custom["rewind"]

    mark_names = {mark.name for mark in marks}

    for requirement in requirements:
        if requirement.marked not in mark_names:
            raise Unresolved("a rewind requirement names the mark %s, which no field of %s captures" % (requirement.marked, config.ARTIFACT_REWIND_TYPE))

    if op_keyed and OP_MARK not in mark_names:
        raise Unresolved("op-keyed fields need a mark named %s" % OP_MARK)

    return RewindPlan(path, config.ARTIFACT_REWIND_TYPE, record_lines(op_keyed, rewinds), mark_lines(marks, requirements))


def record_lines(op_keyed, rewinds):
    lines = ["\tfunction rewindTo(mark %s)" % config.ARTIFACT_MARK_TYPE, "\t\tlet %s = self.module.opCount()" % OP_LIMIT, ""]

    # Inclusive of the op count: a site is filed at `opCount()` before its op is appended, so a site
    # keyed by the count itself belongs to the rewound region.
    if op_keyed:
        lines.append("\t\tfor opIndex in mark.%s to %s 'eachOpSince'" % (OP_MARK, OP_LIMIT))
        lines += ["\t\t\t_ = self.%s.remove(opIndex)" % name for name in op_keyed]
        lines += ["\t\tend 'eachOpSince'", ""]

    lines += ["\t\t%s" % statement for statement in rewinds]
    lines += ["\t\tmark.requireHeldBy(self)", "\tend 'rewindTo'"]

    return lines


def mark_lines(marks, requirements):
    mark_type = config.ARTIFACT_MARK_TYPE
    lines = ["type %s" % mark_type]
    lines += ["\tlet %s as %s" % (mark.name, mark.type_text) for mark in marks]
    lines += ["", "\tstatic function of(artifact %s) returns %s" % (config.ARTIFACT_REWIND_TYPE, mark_type)]
    lines.append("\t\treturn Self{%s}" % ", ".join("%s: %s" % (mark.name, mark.capture) for mark in marks))
    lines += ["\tend 'of'", "", "\tfunction requireHeldBy(artifact %s)" % config.ARTIFACT_REWIND_TYPE]
    lines += ['\t\trequireRewoundCount("%s", now: %s, marked: self.%s)' % (requirement.label, requirement.now, requirement.marked) for requirement in requirements]
    lines += ["\tend 'requireHeldBy'", "end '%s'" % mark_type]

    return lines
