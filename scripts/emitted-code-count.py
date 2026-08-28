#!/usr/bin/env python3
"""emitted-code-count.py - count shv2's emitted-code defects on a fixed corpus.

The companion instrument to scripts/self-host-ab.sh, and a different question.
self-host-ab.sh asks "how fast is the code shv2 emits" and takes ~15 minutes;
this asks "how much of what it emits is known debris" and takes ~10 seconds, so
it can be run after every edit rather than once per rung.

It compiles a fixed corpus with --emit-ir and counts, per program:

  ops             total x64.* ops emitted
  jmp             unconditional jumps
  jmp->next       jumps whose target is the PHYSICALLY NEXT block: pure debris,
                  every one a taken branch (docs/emitted-code-roadmap.md, EC11)
  jmponly-blocks  blocks whose only op is an unconditional jump (EC11)
  imul-imm        multiplies by an immediate ...
  imul-pow2       ... of which by a power of two (EC16 folds these into an
                  addressing mode; EC18 turns the rest into shifts)
  idiv            integer divides (EC18: 20-40 cycles each)

Every column is EXACT and reproducible - it counts instructions in a text dump,
not time - so ANY movement is real and owes an explanation. There is no verdict
and nothing to pass; read the numbers.

Usage:  python scripts/emitted-code-count.py [--json] [corpus.maxon ...]
Default corpus: examples/nbody.maxon, examples/fannkuch-redux.maxon, and the
probe programs under temp/codegen-probe/ when they exist.
"""
import json
import os
import re
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHV2 = os.path.join(REPO, "maxon-shv2", ".maxon",
                    "maxon-shv2.exe" if os.name == "nt" else "maxon-shv2")

JMP = re.compile(r"x64\.jmp\s+(\S+)$")
LABEL = re.compile(r"(\S+):$")
IMUL_IMM = re.compile(r"x64\.imulRegRegImm32 [^,]+, [^,]+, (-?\d+)")


def next_code_line(lines, i):
    """Index of the next non-blank line after i, or None."""
    j = i + 1
    while j < len(lines) and not lines[j].strip():
        j += 1
    return j if j < len(lines) else None


def count(ir_text):
    lines = ir_text.splitlines()
    c = {k: 0 for k in ("ops", "jmp", "jmp->next", "jmponly-blocks",
                        "imul-imm", "imul-pow2", "idiv")}
    for i, raw in enumerate(lines):
        s = raw.strip()
        if s.startswith("x64."):
            c["ops"] += 1
        if s.startswith("x64.idivReg"):
            c["idiv"] += 1

        m = IMUL_IMM.match(s)
        if m:
            c["imul-imm"] += 1
            v = int(m.group(1))
            if v > 0 and (v & (v - 1)) == 0:
                c["imul-pow2"] += 1

        m = JMP.match(s)
        if m:
            c["jmp"] += 1
            j = next_code_line(lines, i)
            if j is not None:
                lab = LABEL.match(lines[j].strip())
                if lab and lab.group(1) == m.group(1):
                    c["jmp->next"] += 1

        # A block whose only op is an unconditional jump: a label line whose
        # next code line is a jmp. Label lines are indented and end in ':'.
        if raw.startswith((" ", "\t")) and LABEL.match(s) and not s.startswith("x64."):
            j = next_code_line(lines, i)
            if j is not None and JMP.match(lines[j].strip()):
                c["jmponly-blocks"] += 1
    return c


def emit_ir(src, workdir):
    """Compile src with --emit-ir into workdir; return the .ir text."""
    base = os.path.splitext(os.path.basename(src))[0]
    dst = os.path.join(workdir, os.path.basename(src))
    if os.path.abspath(src) != os.path.abspath(dst):
        with open(src, "rb") as fh:
            data = fh.read()
        with open(dst, "wb") as fh:
            fh.write(data)
    r = subprocess.run([SHV2, "build", os.path.basename(src), "--emit-ir"],
                       cwd=workdir, capture_output=True, text=True)
    ir = os.path.join(workdir, base + ".ir")
    if r.returncode != 0 or not os.path.exists(ir):
        tail = (r.stdout + r.stderr).strip().splitlines()
        raise SystemExit("FAILED to compile {}: {}".format(
            src, tail[-1] if tail else "no .ir produced"))
    with open(ir, encoding="utf-8", errors="replace") as fh:
        return fh.read()


def main():
    args = [a for a in sys.argv[1:] if a != "--json"]
    as_json = "--json" in sys.argv[1:]

    corpus = args or [p for p in (
        os.path.join(REPO, "examples", "nbody.maxon"),
        os.path.join(REPO, "examples", "fannkuch-redux.maxon"),
        os.path.join(REPO, "temp", "codegen-probe", "arr.maxon"),
        os.path.join(REPO, "temp", "codegen-probe", "cse2.maxon"),
        os.path.join(REPO, "temp", "codegen-probe", "probe.maxon"),
    ) if os.path.exists(p)]

    if not os.path.exists(SHV2):
        raise SystemExit("no shv2 binary at {} - build it first".format(SHV2))

    workdir = os.path.join(REPO, "temp", "emitted-code-count")
    os.makedirs(workdir, exist_ok=True)

    cols = ["ops", "jmp", "jmp->next", "jmponly-blocks",
            "imul-imm", "imul-pow2", "idiv"]
    rows, totals = [], {k: 0 for k in cols}
    for src in corpus:
        c = count(emit_ir(src, workdir))
        rows.append((os.path.basename(src), c))
        for k in cols:
            totals[k] += c[k]

    if as_json:
        print(json.dumps({"programs": {n: c for n, c in rows},
                          "total": totals}, indent=2))
        return

    width = max([len(n) for n, _ in rows] + [len("TOTAL")])
    # Each column is as wide as its header or its widest value, whichever is
    # larger - the header is not always the longest thing in it ("ops"/10389).
    cw = {k: max([len(k), len(str(totals[k]))]
                 + [len(str(c[k])) for _, c in rows]) for k in cols}

    def line(label, cell):
        return "{:<{w}}  {}".format(label, "  ".join(
            "{:>{c}}".format(cell(k), c=cw[k]) for k in cols), w=width)

    print(line("program", lambda k: k))
    for name, c in rows:
        print(line(name, lambda k, c=c: c[k]))
    print(line("TOTAL", lambda k: totals[k]))


if __name__ == "__main__":
    main()
