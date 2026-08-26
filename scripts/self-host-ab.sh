#!/usr/bin/env bash
# self-host-ab.sh — THE ONE INSTRUMENT FOR shv2's EMITTED-CODE QUALITY.
#
# The question it answers: "how fast is the compiler shv2 EMITS, compared with the one the C#
# bootstrap emits, from the same source?" — the stage-2/stage-1 gap. A green suite cannot answer
# it and neither can `scale-test` on its own: both measure the compiler's LOGIC, which is identical
# in every stage (the fixpoint says so, byte for byte). Only running the SAME logic in two
# differently-emitted binaries isolates the codegen.
#
# What it does, in order:
#   1. stage-1 = the tree's `maxon-shv2` (bootstrap-built). Builds stage-2 WITH stage-1, timed.
#   2. Builds stage-3 WITH stage-2, timed, and `cmp`s it against stage-2 — the fixpoint gate. A
#      difference here is a MISCOMPILE, and the rest of the reading would be about a broken binary.
#   3. Runs `scale-test --repeat=3 --result-json` on stage-1 and stage-2, INTERLEAVED
#      (S1 S2 S1 S2): the CPU column drifts with the machine's state over minutes, and an A/B whose
#      two arms sit in different time windows reads that drift as a code effect (measured, P1.8:
#      a +8.6% "consistent" reading that changed sign once interleaved). The memory columns are
#      exact and need no repeat — they are the sharper reading.
#   4. Prints the per-rung and per-phase ratio table, stage-2 over stage-1: allocations, bytes, CPU.
#
# READING IT. Same compiler logic ⇒ any allocation difference IS the emitted code's: a ratio above
# 1.00 in the alloc column is a construct shv2's codegen allocates for and the bootstrap's does not
# (measured 2026-08-25: ×3.24 at rung 5, from `for x in arr` cursor records and per-element
# retain/detach in the shared generic Array bodies). The CPU ratio carries the few-percent band of
# every CPU reading; the phase split says WHICH code is slow, not merely that it is.
#
# Usage:
#   scripts/self-host-ab.sh                 full run (~15 min: two self-compiles + four scale-tests)
#   scripts/self-host-ab.sh --skip-build    reuse temp/selfhost/stage{2,3} from a previous run
#   scripts/self-host-ab.sh --profile       ALSO sample-profile both self-compiles (scripts/
#                                           sample_profile.py; another ~9 min) — the function-level
#                                           attribution the phase table cannot give
#   scripts/self-host-ab.sh --repeat=N      scale-test repeat count (default 3; 1 is enough for the
#                                           memory columns, never for the CPU column)
#   scripts/self-host-ab.sh --suite         ALSO run the whole specs-shv2 suite UNDER THE STAGE-2
#                                           COMPILER (~5 min). This is the Phase 2 gate's second half,
#                                           and the byte-identical fixpoint cannot stand in for it:
#                                           2026-08-26 the stage-2 compiler failed 3 committed cases —
#                                           `Lexer.maxon`'s `leftBrace = "\{"` is two characters under
#                                           the bootstrap and one under shv2, so BOTH stages carried the
#                                           same wrong constant and only a suite run under stage-2 saw it.
#
# Everything it writes goes under temp/selfhost/ (gitignored). It records NOTHING in
# docs/optimization-log.md — that row is yours to write with `scale-test --note=…` once you know WHY
# the numbers moved, which is the one thing an instrument cannot know.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
# shellcheck source=lib/host-binaries.sh
. scripts/lib/host-binaries.sh

skip_build=0
profile=0
suite=0
repeat=3
for arg in "$@"; do
	case "$arg" in
		--skip-build) skip_build=1 ;;
		--profile) profile=1 ;;
		--suite) suite=1 ;;
		--repeat=*) repeat="${arg#--repeat=}" ;;
		-h|--help) sed -n '2,40p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
		*) echo "self-host-ab.sh: unknown arg: $arg" >&2; exit 2 ;;
	esac
done

# Git Bash on Windows resolves `python3` to the Store's stub, which is not an interpreter; take
# whichever of the two spellings actually runs.
python_bin=""
for candidate in python3 python; do
	if "$candidate" -c pass > /dev/null 2>&1; then python_bin="$candidate"; break; fi
done
if [[ -z "$python_bin" ]]; then
	echo "self-host-ab.sh: no working python3/python on PATH — the ratio table needs one" >&2
	exit 1
fi

out=temp/selfhost
mkdir -p "$out"
stage1="$(maxon_shv2_path .)"
# `-o` is given WITHOUT the extension: the compiler appends the target's own.
stage2="$out/stage2$MAXON_EXE_EXT"
stage3="$out/stage3$MAXON_EXE_EXT"

if [[ ! -x "$stage1" ]]; then
	echo "self-host-ab.sh: no stage-1 compiler at $stage1 — build it first (build target=shv2)" >&2
	exit 1
fi

# Wall-clock seconds of one command, to a tenth. Wall time is fine HERE and nowhere else in this
# script: a whole self-compile is minutes long and single-threaded, so the machine's other work
# moves it by far less than the gap under study. The per-phase CPU column below is what carries
# the fine reading.
#
# The command's own output goes to <log>; the timing line goes to OUR stdout. The redirection is
# INSIDE the function for that reason — wrapped around the call it would swallow the timing too
# (the first run of this script printed no times: they were at the tail of the build logs).
timed() {
	local label="$1" log="$2"; shift 2
	local start end
	start="$(date +%s.%N)"
	"$@" > "$log" 2>&1
	end="$(date +%s.%N)"
	awk -v s="$start" -v e="$end" -v l="$label" 'BEGIN { printf "%s: %.1f s\n", l, e - s }'
}

if [[ $skip_build -eq 0 ]]; then
	echo "=== stage-2: $stage1 compiling maxon-shv2"
	timed "stage-2 self-compile (bootstrap-emitted compiler)" "$out/stage2-build.log" "$stage1" build maxon-shv2 -o "$out/stage2"
	echo "=== stage-3: $stage2 compiling maxon-shv2"
	timed "stage-3 self-compile (shv2-emitted compiler)" "$out/stage3-build.log" "$stage2" build maxon-shv2 -o "$out/stage3"
fi

if cmp -s "$stage2" "$stage3"; then
	echo "fixpoint: stage-2 == stage-3 BYTE-IDENTICAL ($(stat -c %s "$stage2") bytes)"
else
	echo "fixpoint BROKEN: stage-2 and stage-3 differ — a miscompile; the A/B below would measure a broken binary" >&2
	cmp "$stage2" "$stage3" | head -1 >&2
	exit 1
fi

echo "=== scale-test A/B, interleaved S1 S2 S1 S2, --repeat=$repeat"
for pair in 1 2; do
	for stage in 1 2; do
		bin="$stage1"; [[ $stage -eq 2 ]] && bin="$stage2"
		json="$out/scale-s$stage-p$pair.json"
		"$bin" scale-test "--repeat=$repeat" "--result-json=$json" > "$out/scale-s$stage-p$pair.log" 2>&1
		echo "  stage-$stage pair $pair done"
	done
done

"$python_bin" - "$out" <<'PY'
import json, sys
out = sys.argv[1]

def load(stage, pair):
    return json.load(open(f"{out}/scale-s{stage}-p{pair}.json", encoding="utf-8"))

runs = {(s, p): load(s, p) for s in (1, 2) for p in (1, 2)}
rung_count = len(runs[(1, 1)]["rungs"])

# CPU is the MINIMUM over the two pairs — noise can only add to a CPU reading. Memory is read from
# pair 1 and checked equal to pair 2: a memory number that differs between two runs of one binary is
# a broken run, and the table below would be meaningless.
def cpu(stage, r, key=None, group="phases"):
    if key is None:
        return min(runs[(stage, p)]["rungs"][r]["cpuTicks"] for p in (1, 2))
    return min(next(x["cpuTicks"] for x in runs[(stage, p)]["rungs"][r][group] if x["name"] == key) for p in (1, 2))

def mem(stage, r, field, key=None, group="phases"):
    vals = []
    for p in (1, 2):
        rung = runs[(stage, p)]["rungs"][r]
        vals.append(rung[field] if key is None else next(x[field] for x in rung[group] if x["name"] == key))
    if vals[0] != vals[1]:
        sys.exit(f"stage-{stage} rung {r} {key or 'total'} {field}: {vals[0]} vs {vals[1]} between two runs of ONE binary — broken run")
    return vals[0]

def ratio(b, a):
    return b / a if a else float("inf")

print()
print("PER RUNG — stage-2 / stage-1 (same compiler logic; only the emitted code differs)")
print(f"  {'rung':>4} {'allocs s1':>12} {'allocs s2':>12} {'x':>5} | {'bytes s1':>10} {'bytes s2':>10} {'x':>5} | {'cpu s1':>9} {'cpu s2':>9} {'x':>5}")
for r in range(rung_count):
    a1, a2 = mem(1, r, "allocs"), mem(2, r, "allocs")
    b1, b2 = mem(1, r, "bytes"), mem(2, r, "bytes")
    c1, c2 = cpu(1, r), cpu(2, r)
    print(f"  {r:>4} {a1:>12,} {a2:>12,} {ratio(a2, a1):5.2f} | {b1/1e6:9.1f}M {b2/1e6:9.1f}M {ratio(b2, b1):5.2f} | {c1/1e9:8.2f}e9 {c2/1e9:8.2f}e9 {ratio(c2, c1):5.2f}")

top = rung_count - 1
names = [x["name"] for x in runs[(1, 1)]["rungs"][top]["phases"]]
rows = []
for n in names:
    c1, c2 = cpu(1, top, n), cpu(2, top, n)
    rows.append((c2 - c1, n, c1, c2, mem(1, top, "allocs", n), mem(2, top, "allocs", n), mem(1, top, "bytes", n), mem(2, top, "bytes", n)))
gap = sum(x[0] for x in rows)
print()
print(f"PER PHASE at rung {top} — sorted by share of the CPU gap (total gap {gap/1e9:.1f}e9 ticks)")
print(f"  {'phase':24} {'cpu s1':>8} {'cpu s2':>8} {'x':>5} {'gap%':>6} | {'allocs s1':>11} {'allocs s2':>11} {'x':>6} | {'bytes s1':>9} {'bytes s2':>9} {'x':>5}")
for d, n, c1, c2, a1, a2, b1, b2 in sorted(rows, reverse=True):
    if c1 < 1e7 and c2 < 1e7 and a1 < 1000 and a2 < 1000:
        continue
    print(f"  {n:24} {c1/1e9:8.2f} {c2/1e9:8.2f} {ratio(c2, c1):5.2f} {100*d/gap if gap else 0:5.1f}% | {a1:>11,} {a2:>11,} {ratio(a2, a1):6.2f} | {b1/1e6:8.1f}M {b2/1e6:8.1f}M {ratio(b2, b1):5.2f}")

sub = [x["name"] for x in runs[(1, 1)]["rungs"][top]["regalloc"]]
print()
print(f"REGALLOC SUB-PHASES at rung {top}")
for n in sub:
    c1, c2 = cpu(1, top, n, "regalloc"), cpu(2, top, n, "regalloc")
    a1, a2 = mem(1, top, "allocs", n, "regalloc"), mem(2, top, "allocs", n, "regalloc")
    print(f"  {n:24} {c1/1e9:8.2f} {c2/1e9:8.2f} {ratio(c2, c1):5.2f}        | {a1:>11,} {a2:>11,} {ratio(a2, a1):6.2f}")
PY

if [[ $profile -eq 1 ]]; then
	if [[ $MAXON_HOST_IS_WINDOWS -ne 1 ]]; then
		echo "--profile: scripts/sample_profile.py is Windows-only; skipping" >&2
		exit 0
	fi
	echo "=== sample profiles (same sampler on both; function-level attribution)"
	for stage in 1 2; do
		bin="$stage1"; [[ $stage -eq 2 ]] && bin="$stage2"
		"$python_bin" scripts/sample_profile.py --duration 600 --hz 250 --top 80 -- "$bin" build maxon-shv2 -o "$out/profiled-s$stage" > "$out/profile-s$stage.txt" 2>&1 || true
		echo "  stage-$stage profile → $out/profile-s$stage.txt"
	done
	echo "top of each (self time):"
	for stage in 1 2; do
		echo "--- stage-$stage"; grep -A 25 '=== sample profile' "$out/profile-s$stage.txt" | head -26
	done
fi

if [[ $suite -eq 1 ]]; then
	# The compiler under test is the binary running the runner, so this IS the suite as stage-2 sees it.
	# It takes the tree lock, so it runs after the scale-tests, never beside them. Redirected, never piped.
	echo "=== specs-shv2 under the STAGE-2 compiler"
	"$stage2" spec-test > "$out/suite-s2.log" 2>&1
	suite_exit=$?
	summary="$(grep -oE '^[0-9]+ passed, [0-9]+ failed' "$out/suite-s2.log" | tail -1)"
	echo "  stage-2: ${summary:-no summary — read $out/suite-s2.log}   (exit $suite_exit)"
	# `|| true`: under `set -eo pipefail` a grep that matches NOTHING is exit 1 and would end the script
	# right here — which it did (EC4, 2026-08-26): a GREEN suite exited 1 and a RED one, where grep
	# matched, ran on to exit 0. The instrument reported the opposite of what it measured.
	grep -n '^FAIL' "$out/suite-s2.log" | head -20 || true
	if [[ $suite_exit -ne 0 ]]; then
		echo "  ⚠ the suite is RED under the stage-2 compiler while (presumably) green under stage-1: a program the" >&2
		echo "    self-hosted compiler compiles differently. The fixpoint's byte identity cannot see this. Read the log." >&2
	fi
	# The suite under stage-2 is the Phase 2 gate's second half; the script's exit code IS its verdict.
	exit "$suite_exit"
fi

