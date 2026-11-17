#!/usr/bin/env python3
"""Regenerate the `specs/model-*.md` cases from the capmodel corpora.

    python scripts/model-specs/generate.py --model <capmodel checkout> [--ruleset core-m3w-fa24]

The phases, in order: judge each program with the model; reduce it to what `main` reaches and re-judge; translate
it to Maxon; compile every translation as a spec case in a scratch directory; sort the results; write the cases
the compiler and the model agree on, grouped by subject; run the written specs once to prove them green. The
report names every program the two disagree on and every program the translator cannot express.
"""

from __future__ import annotations

import argparse
import collections
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
from dataclasses import dataclass, field

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

import model_corpus as mc  # noqa: E402
import rules  # noqa: E402
from spec_writer import SpecCase, spec_text  # noqa: E402

DIAGNOSTIC = re.compile(r"error (E\d{4}):")
WORKER_PANIC = re.compile(r"last step started: compiling (\S+?)/\S+[^\n]*\n(panic at [^\n]*)")
ACTUAL_MARKER = "    actual:   "
HASH_PIN_LINE = re.compile(r"^\s*(?:program: )?MAXON_HASH_SEED=\S+ MAXON_HASH_TIER=\S+\s*$", re.MULTILINE)
CRASH_MARKERS = ("DIED", "panic at")
PROGRAM_STDERR = "child stderr:"
SUCCEEDED = "compilation succeeded"
AGREEING = ("agree-accept", "agree-refuse", "agree-refuse-other-code")
MAX_NAME_LENGTH = 80
SERVICE_WORDS = ("spawn ", "spawn(", "acquire ", "async ", ".handle")
WASM_TARGET = "wasm32-wasi"
WELL_FORMEDNESS_RULE = "WF"
TIMED_OUT = "timed out"
ENVIRONMENT_CODES = {"E6003"}
ENVIRONMENT_RETRIES = 2

SUBJECTS = (
    ("acquire", "model-acquire", "Acquired states", "acquire blocks over service states"),
    ("modify", "model-windows", "Windows over isolated regions", "`modify` windows and one-statement opens"),
    ("linear", "model-linear", "Linear values and finalizers", "linear types, finalizers and their hand-backs"),
    ("service", "model-services", "Services and messages", "spawned services, messages and replies"),
    ("union", "model-unions", "Unions and matches", "unions, payload bindings and `match`"),
    ("plain", "model-regions", "Regions and capabilities", "records, frozen data and capability inference"),
)


@dataclass
class Entry:
    index: int
    case: mc.Case
    verdict: mc.Verdict
    mx: str
    subject: str
    bucket: str = ""
    codes: tuple = ()
    detail: str = ""
    raw_oracle: str = ""
    spec_text: str = ""
    model_text: str = ""


@dataclass
class Run:
    args: argparse.Namespace
    work: str
    model_stamp: str = ""
    cache: dict = field(default_factory=dict)
    collected: int = 0
    entries: list = field(default_factory=list)
    skips: collections.Counter = field(default_factory=collections.Counter)
    notes: collections.Counter = field(default_factory=collections.Counter)


def slug(text):
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def subject_of(prog):
    from capmodel import cast as C
    from model_prune import walk

    seen = set()

    def visit(n):
        if isinstance(n, C.Acquire):
            seen.add("acquire")
        elif isinstance(n, C.Modify):
            seen.add("modify")
        elif isinstance(n, C.Destruct):
            seen.add("linear")
        elif isinstance(n, (C.Spawn, C.Send)):
            seen.add("service")
        elif isinstance(n, (C.UnionDecl, C.Match)):
            seen.add("union")

    walk(prog, visit)
    for key, *_ in SUBJECTS:
        if key in seen:
            return key
    return "plain"


def judged(run, case):
    from capmodel import render

    import model_prune

    prog, verdict = mc.check(case.src, run.args.ruleset)
    if prog is not None:
        unbound, changed = model_prune.unbind_unread_arms(prog)
        if changed:
            prog, verdict = mc.check(render.render(unbound), run.args.ruleset)
    if prog is None:
        return "the model cannot parse it", None
    if not verdict.accepted and verdict.rule == WELL_FORMEDNESS_RULE:
        return "the model refuses it as ill-formed (WF), which is not a capability rule", None
    text = render.render(model_prune.prune(prog))
    again, second = mc.check(text, run.args.ruleset)
    if again is None or second.accepted != verdict.accepted or (not verdict.accepted and second.rule != verdict.rule):
        return "its verdict depends on a declaration main does not reach", None
    return None, (second, text)


def cache_key(run, case):
    digest = hashlib.sha1(f"{run.model_stamp}\n{run.args.ruleset}\n{case.src}".encode()).hexdigest()
    return f"{case.origin}|{case.ident}|{digest}"


def prepare(run, cases):
    from capmodel import parser

    import model_prune
    import mx_translate

    cache = run.cache
    if run.args.cache and os.path.exists(run.args.cache):
        with open(run.args.cache, encoding="utf-8") as handle:
            cache.update(json.load(handle))
    seen = {}
    for case in cases:
        key = cache_key(run, case)
        if key not in cache:
            try:
                skipped, result = judged(run, case)
            except Exception as ex:
                skipped, result = f"the model crashed: {type(ex).__name__}", None
            cache[key] = [skipped, None if result is None else [result[0].accepted, result[0].rule, result[0].reason, result[1]]]
        skipped, result = cache[key]
        if skipped:
            run.skips[skipped] += 1
            continue
        verdict = mc.Verdict(result[0], result[1], result[2])
        pruned = model_prune.prune(parser.parse(result[3]))
        try:
            mx = mx_translate.translate(pruned)
        except mx_translate.Skip as skip:
            run.skips[str(skip)] += 1
            continue
        except Exception as ex:
            run.skips[f"translator crashed: {type(ex).__name__}: {ex}"] += 1
            continue
        if (mx, verdict.accepted) in seen:
            run.notes["duplicate translations dropped"] += 1
            continue
        entry = Entry(len(run.entries), case, verdict, mx, subject_of(pruned), model_text=result[3])
        seen[(mx, verdict.accepted)] = entry
        run.entries.append(entry)
    save_cache(run)


def candidate_name(entry):
    return f"c{entry.index:05d}"


def compiler_path(args):
    if args.compiler:
        return os.path.abspath(args.compiler)
    exe = "maxon.exe" if os.name == "nt" else "maxon"
    return os.path.join(REPO, "maxon-bin", ".maxon", exe)


def spec_test(args, spec_dir, result_path, extra=(), batch=False):
    command = [compiler_path(args), "spec-test", spec_dir, f"--workers={args.workers}", f"--result-json={result_path}", *extra]
    if not batch:
        command.append("--batch=off")
    env = dict(os.environ, MAXON_MAX_PROCS=str(args.workers))
    finished = subprocess.run(command, cwd=REPO, env=env, capture_output=True, text=True)
    with open(result_path, encoding="utf-8") as handle:
        results = {(c["spec"], c["test"]): c for c in json.load(handle)["cases"]}
    for spec, panic in WORKER_PANIC.findall(finished.stdout + finished.stderr):
        for (name, _), case in results.items():
            if name == spec:
                case["reason"] = case.get("reason", "") + "\n" + panic
    return results


def compile_key(run, entry):
    exe = compiler_path(run.args)
    stamp = f"{os.path.getsize(exe)}-{int(os.path.getmtime(exe))}"
    digest = hashlib.sha1(entry.mx.encode()).hexdigest()
    return f"{stamp}|{digest}|{entry.verdict.accepted}"


def compile_candidates(run, entries, subdir, fresh=False):
    path = run.args.compile_cache
    cache = {}
    if path and os.path.exists(path) and not fresh:
        with open(path, encoding="utf-8") as handle:
            cache = json.load(handle)
    pending = [e for e in entries if compile_key(run, e) not in cache]
    if pending:
        spec_dir = os.path.join(run.work, subdir)
        os.makedirs(spec_dir, exist_ok=True)
        for name in [n for n in os.listdir(spec_dir) if n.endswith(".md")]:
            os.remove(os.path.join(spec_dir, name))
        for entry in pending:
            name = candidate_name(entry)
            placeholder = "" if entry.verdict.accepted else "placeholder"
            text = spec_text(name, "candidate", ["model"], "candidate", [SpecCase("t", "candidate", entry.mx, placeholder)])
            with open(os.path.join(spec_dir, name + ".md"), "w", encoding="utf-8", newline="\n") as handle:
                handle.write(text)
        results = spec_test(run.args, spec_dir, os.path.join(run.work, subdir + ".json"))
        for entry in pending:
            case = results[(candidate_name(entry), "t")]
            cache[compile_key(run, entry)] = {"verdict": case["verdict"], "reason": case.get("reason", "")}
        if path:
            with open(path, "w", encoding="utf-8") as handle:
                json.dump(cache, handle)
    for entry in entries:
        classify(run, entry, cache[compile_key(run, entry)])


def environment_failures(run):
    return [e for e in run.entries if e.bucket == "translation" and e.codes and set(e.codes) <= ENVIRONMENT_CODES]


def actual_text(reason):
    marker = reason.find(ACTUAL_MARKER)
    if marker < 0:
        return ""
    return HASH_PIN_LINE.sub("", reason[marker + len(ACTUAL_MARKER):]).strip()


def classify(run, entry, result):
    reason = result.get("reason", "")
    entry.detail = reason
    if result["verdict"] == "PASS":
        entry.bucket = "agree-accept"
        return
    entry.codes = tuple(sorted(set(DIAGNOSTIC.findall(reason))))
    compiler_output = reason.split(PROGRAM_STDERR)[0]
    if any(m in compiler_output for m in CRASH_MARKERS):
        entry.bucket = "crash"
    elif TIMED_OUT in reason:
        entry.bucket = "diverges"
    elif entry.verdict.accepted and not entry.codes:
        entry.bucket = "runtime-failure"
    elif entry.verdict.accepted:
        entry.bucket = "wrong-refusal" if set(entry.codes) <= rules.CAPABILITY_CODES else "translation"
    elif SUCCEEDED in reason:
        entry.bucket = "soundness-candidate"
    elif entry.codes and set(entry.codes) <= rules.CAPABILITY_CODES | rules.codes_of(entry.verdict.rule):
        entry.bucket = "agree-refuse" if set(entry.codes) <= rules.codes_of(entry.verdict.rule) else "agree-refuse-other-code"
        entry.spec_text = actual_text(reason)
    else:
        entry.bucket = "translation"


def judge_with_oracle(run):
    accepted = [e for e in run.entries if e.bucket == "agree-accept"]
    missing = [e for e in accepted if "oracle|" + cache_key(run, e.case) not in run.cache]
    if missing:
        tags = mc.oracle_tags([(e.case.ident, e.model_text) for e in missing])
        for entry, tag in zip(missing, tags):
            run.cache["oracle|" + cache_key(run, entry.case)] = tag
    for entry in accepted:
        tag = run.cache["oracle|" + cache_key(run, entry.case)]
        if tag != mc.CLEAN_TAG:
            entry.bucket = "model-flags-its-own-accept"
            entry.raw_oracle = tag
    for entry in [e for e in run.entries if e.bucket == "soundness-candidate"]:
        key = "raw|" + cache_key(run, entry.case)
        if key not in run.cache:
            run.cache[key] = mc.raw_verdict(entry.model_text)
        entry.raw_oracle = run.cache[key]
    save_cache(run)


def save_cache(run):
    if run.args.cache:
        with open(run.args.cache, "w", encoding="utf-8") as handle:
            json.dump(run.cache, handle)


def unsupported_targets(entry):
    """A linear-value refusal is decided after the target check, so a lane with no services answers E3104 first."""
    if "E3223" in entry.codes and any(word in entry.mx for word in SERVICE_WORDS):
        return (WASM_TARGET,)
    return ()


def case_for(run, entry, used_names):
    base = slug(f"{entry.case.family}-{entry.case.ident}")[:MAX_NAME_LENGTH].rstrip("-")
    name = base
    n = 2
    while name in used_names:
        name = f"{base}-{n}"
        n += 1
    used_names.add(name)
    ruleset = run.args.ruleset
    if entry.verdict.accepted:
        verdict = f"The model (`{ruleset}`) accepts it and its oracle runs it clean; the compiler accepts it and the program exits 0."
        error = ""
    else:
        codes = ", ".join(sorted(set(entry.codes)))
        verdict = f"The model (`{ruleset}`) refuses it by `{entry.verdict.rule}`; the compiler refuses it with {codes}."
        if entry.bucket == "agree-refuse-other-code":
            verdict += " The model reports the first rule that fails and the compiler every independent error, so the codes differ."
        error = entry.spec_text
    report = f"capmodel `reggio/{mc.report_file(entry.case.family.split('-')[1])}`"
    prose = f"{verdict} Model program `{entry.case.ident}` of {entry.case.origin}, in {report}."
    return SpecCase(f"model-{name}", prose, entry.mx, error, unsupported_targets(entry))


def count_markers(out_dir):
    total = 0
    for name in os.listdir(out_dir):
        if name.startswith("model-") and name.endswith(".md"):
            with open(os.path.join(out_dir, name), encoding="utf-8") as handle:
                total += sum(1 for line in handle if line.startswith("<!-- test: "))
    return total


def write_specs(run):
    written = collections.Counter()
    used = set()
    out_dir = run.args.out
    os.makedirs(out_dir, exist_ok=True)
    for key, feature, title, what in SUBJECTS:
        chosen = [e for e in run.entries if e.subject == key and e.bucket in AGREEING]
        path = os.path.join(out_dir, feature + ".md")
        if not chosen:
            if os.path.exists(path):
                os.remove(path)
            continue
        cases = []
        for entry in chosen:
            cases.append(case_for(run, entry, used))
        documentation = DOCUMENTATION.format(what=what, ruleset=run.args.ruleset)
        text = spec_text(feature, title, ["model", "capmodel", "region", "capability"], documentation, cases)
        with open(path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(text)
        written[feature] = len(cases)
    return written


DOCUMENTATION = """These cases are programs of the capmodel executable model, translated to Maxon by
`scripts/model-specs/generate.py` (`docs/SPECS.md`, "Model-derived specs") and pinned to the model's verdict on
{what}. A program the model accepts runs and exits 0; one it refuses fails to compile, with the diagnostic
the compiler gives. A case is written only where the compiler and the model agree, so the suite fails
the day the compiler stops agreeing with a verdict the model pinned. The rule set is `{ruleset}`."""


def report(run, written, verified):
    buckets = collections.defaultdict(list)
    for entry in run.entries:
        buckets[entry.bucket].append(entry)
    lines = [f"# Model-derived specs: {run.args.ruleset}", ""]
    lines.append(f"cases collected: {run.collected}; translated: {len(run.entries)}")
    lines += ["", "## Skipped, by reason", ""]
    lines += [f"- {n}  {reason}" for reason, n in run.skips.most_common()]
    lines += ["", "## Notes", ""] + [f"- {n}  {what}" for what, n in run.notes.items()]
    lines += ["", "## Buckets", ""]
    lines += [f"- {len(v)}  {k}" for k, v in sorted(buckets.items())]
    lines += ["", "## Written", ""] + [f"- {n}  {k}" for k, n in written.items()]
    lines += ["", f"verification run: {verified}", ""]
    for name, title in (
        ("soundness-candidate", "Model refuses, compiler accepts"),
        ("wrong-refusal", "Model accepts, compiler refuses"),
        ("crash", "The compiler crashed"),
        ("agree-refuse-other-code", "Both refuse, with a code outside the rule's mapping (pinned)"),
        ("diverges", "Both accept, the program does not terminate"),
        ("runtime-failure", "Both accept, the program fails when it runs"),
        ("model-flags-its-own-accept", "The model accepts and its oracle flags it"),
        ("translation", "The translation is unfaithful"),
    ):
        lines += ["", f"## {title}: {len(buckets[name])}", ""]
        for entry in buckets[name]:
            lines += describe(entry)
    with open(os.path.join(run.work, "report.md"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(lines) + "\n")
    rows = [
        {"family": e.case.family, "origin": e.case.origin, "ident": e.case.ident, "bucket": e.bucket, "rule": e.verdict.rule,
         "accepted": e.verdict.accepted, "reason": e.verdict.reason, "codes": list(e.codes), "oracle": e.raw_oracle,
         "model": e.model_text, "maxon": e.mx, "compiler": e.detail}
        for e in run.entries
    ]
    with open(os.path.join(run.work, "entries.json"), "w", encoding="utf-8") as handle:
        json.dump(rows, handle, indent=1)


def describe(entry):
    verdict = "accepts" if entry.verdict.accepted else f"refuses {entry.verdict.rule}: {entry.verdict.reason}"
    lines = [f"### {entry.case.family} {entry.case.ident} ({entry.case.origin})", "", f"model {verdict}"]
    if entry.raw_oracle:
        lines.append(f"oracle: {entry.raw_oracle}")
    if entry.codes:
        lines.append(f"compiler codes: {', '.join(entry.codes)}")
    lines += ["", "model program:", "```text", entry.model_text.strip(), "```", "translation:", "```maxon", entry.mx.rstrip(), "```"]
    if entry.detail:
        lines += ["compiler:", "```text", entry.detail[:1500], "```"]
    return lines + [""]


def parse_args(argv):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model", default=os.environ.get("CAPMODEL_DIR"), help="the capmodel checkout")
    parser.add_argument("--ruleset", default=mc.DEFAULT_RULESET, help="the model rule set the specs are pinned to")
    parser.add_argument("--compiler", help="the compiler that judges the translations (default: this tree's slot)")
    parser.add_argument("--out", default=os.path.join(REPO, "specs"), help="where the spec files are written")
    parser.add_argument("--work", help="scratch directory (default: a new one under the system temp directory)")
    parser.add_argument("--workers", type=int, default=4)
    parser.add_argument("--compile-cache", help="a file that keeps what the compiler said about each translation (keyed by the translation and the compiler binary)")
    parser.add_argument("--no-write", action="store_true", help="classify and report only: write no spec file")
    parser.add_argument("--hands", default=",".join(mc.HAND_FAMILIES), help="red teams whose hand corpora are ported")
    parser.add_argument("--findings", default=",".join(mc.FINDING_FAMILIES), help="red teams whose finding files are ported")
    parser.add_argument("--limit", type=int, help="only the first N programs (a smoke run)")
    parser.add_argument("--cache", help="a file that keeps the model's verdicts between runs (they depend on the model, not on the translator)")
    args = parser.parse_args(argv)
    if not args.model:
        parser.error("--model, or CAPMODEL_DIR, names the capmodel checkout")
    return args


def main(argv):
    args = parse_args(argv)
    args.model = os.path.abspath(args.model)
    work = args.work or tempfile.mkdtemp(prefix="model-specs-")
    os.makedirs(work, exist_ok=True)
    mc.configure(args.model, args.ruleset)
    run = Run(args, work, model_stamp=mc.model_stamp(args.model))
    cases = mc.collect([h for h in args.hands.split(",") if h], [f for f in args.findings.split(",") if f])
    if args.limit:
        cases = cases[: args.limit]
    run.collected = len(cases)
    prepare(run, cases)
    compile_candidates(run, run.entries, "candidates")
    for attempt in range(ENVIRONMENT_RETRIES):
        retry = environment_failures(run)
        if retry:
            compile_candidates(run, retry, f"retry{attempt}", fresh=True)
    judge_with_oracle(run)
    if args.no_write:
        report(run, {}, "not run (--no-write)")
        print(f"report: {os.path.join(work, 'report.md')}")
        return 0
    written = write_specs(run)
    verified = spec_test(args, args.out, os.path.join(work, "verify.json"), extra=("--filter=model-",), batch=True)
    passed = sum(1 for c in verified.values() if c["verdict"] == "PASS")
    markers = count_markers(args.out)
    report(run, written, f"{passed} of {len(verified)} pass; {markers} markers in the written files")
    print(f"report: {os.path.join(work, 'report.md')}")
    return 0 if passed == len(verified) == markers == sum(written.values()) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
