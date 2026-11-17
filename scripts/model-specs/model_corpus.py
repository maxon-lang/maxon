
from __future__ import annotations

import copy
import glob
import importlib
import os
import re
import sys
from dataclasses import dataclass

DEFAULT_RULESET = "core-m3w-fa25"
WORKERS = "3"

HAND_FAMILIES = (
    "AB", "AC", "AD", "AE", "AF", "AG", "AH", "AI", "AJ", "AK", "AL", "AM", "AN", "AO", "AP", "AQ", "AR",
    "FA14", "FA15", "FA16", "FA17", "FA18", "FA19", "FA20", "FA21", "FA22", "FA23", "FA24", "FA25",
)
FINDING_FAMILIES = ("N", "P", "Q", "R", "S", "T", "V", "W", "X", "Y", "Z", "AA", "AB", "AC", "AD", "AE", "AF", "AG")

SEPARATOR = re.compile(r"^---- (.*)$", re.MULTILINE)
MODEL_ROUND_FAMILY = re.compile(r"FA\d+")


@dataclass
class Case:
    family: str
    origin: str
    ident: str
    note: str
    src: str


@dataclass
class Verdict:
    accepted: bool
    rule: str
    reason: str


def configure(model_dir, ruleset):
    sys.dont_write_bytecode = True
    os.environ["PYTHONDONTWRITEBYTECODE"] = "1"
    os.environ["CAPMODEL_CORE_RULESET"] = ruleset
    os.environ["CAPMODEL_JUDGE_RULESET"] = ruleset
    os.environ["CAPMODEL_ORACLE_FACTORY_HOLDS"] = "1"
    os.environ["CAPMODEL_CORE_FIX"] = "M5"
    os.environ["CAPMODEL_OPHOLD"] = "1"
    os.environ["CAPMODEL_RT_GADGETS"] = "1"
    os.environ.setdefault("CAPMODEL_MAX_WORKERS", WORKERS)
    os.environ["PYTHONPATH"] = model_dir
    sys.path.insert(0, model_dir)
    os.chdir(model_dir)


def model_stamp(model_dir):
    import hashlib

    digest = hashlib.sha1()
    for path in sorted(glob.glob(os.path.join(model_dir, "capmodel", "*.py")) + glob.glob(os.path.join(model_dir, "reggio", "core_gates.py"))):
        with open(path, "rb") as handle:
            digest.update(handle.read())
    return digest.hexdigest()


HAND_CASE_PREFIX = "hand_"
HAND_TEXT_PREFIXES = (HAND_CASE_PREFIX, "hole_")


def separated_cases(label, paths):
    cases = []
    for path in paths:
        parts = SEPARATOR.split(open(path, encoding="utf-8").read())
        for ident, src in zip(parts[1::2], parts[2::2]):
            cases.append(Case(label, path.replace(os.sep, "/"), ident.strip(), "", src.strip() + "\n"))
    return cases


def hand_cases(family):
    cases = []
    for path in sorted(glob.glob(os.path.join(f"redteam{family}", f"{HAND_CASE_PREFIX}*.py"))):
        module = f"redteam{family}.{os.path.basename(path)[:-3]}"
        for attack in importlib.import_module(module).ATTACKS:
            cases.append(Case(f"hand-{family}", f"{module}", attack.id, attack.note, attack.src))
    hand_texts = sorted(path for prefix in HAND_TEXT_PREFIXES for path in glob.glob(os.path.join(f"redteam{family}", f"{prefix}*.txt")))
    return cases + separated_cases(f"hand-{family}", hand_texts)


def finding_cases(family):
    texts = sorted(glob.glob(os.path.join(f"redteam{family}", "*.txt")))
    return separated_cases(f"finding-{family}", [path for path in texts if not os.path.basename(path).startswith(HAND_CASE_PREFIX)])


def report_file(family):
    return f"REPORT{family}.txt" if MODEL_ROUND_FAMILY.fullmatch(family) else f"REPORTRT{family}.txt"


def report_cases(family):
    path = os.path.join("reggio", report_file(family))
    if not os.path.exists(path):
        return []
    lines = open(path, encoding="utf-8").read().splitlines()
    cases = []
    index = 0
    while index < len(lines):
        if lines[index].strip() != "dialect reggio":
            index += 1
            continue
        indent = len(lines[index]) - len(lines[index].lstrip())
        block = [lines[index].strip()]
        index += 1
        while index < len(lines) and lines[index].strip() and len(lines[index]) - len(lines[index].lstrip()) >= indent:
            block.append(lines[index][indent:])
            index += 1
        cases.append(Case(f"report-{family}", path.replace(os.sep, "/"), f"inline-{len(cases) + 1}", "", "\n".join(block) + "\n"))
    return cases


def collect(hands, findings):
    cases = []
    for family in findings:
        cases += finding_cases(family)
        cases += report_cases(family)
    for family in hands:
        cases += hand_cases(family)
    return cases


def check(src, ruleset):
    from capmodel import checker, parser

    try:
        prog = parser.parse(src)
    except SyntaxError:
        return None, None
    result = checker.get(ruleset).check(copy.deepcopy(prog))
    return prog, Verdict(bool(result.accepted), result.rule or "", result.reason or "")


def raw_verdict(src):
    from redteam38.judge38 import raw_verdict as oracle_word

    return oracle_word(src)


CLEAN_TAG = "ACCEPT+CLEAN"


def oracle_tags(programs):
    from redteam38.judge38 import run

    rows = run([(ident, text, "") for ident, text in programs], 300, False)
    return [row["tag"] for row in rows]
