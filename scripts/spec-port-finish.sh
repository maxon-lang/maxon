#!/usr/bin/env bash
#
# THE /spec-port FINISH LINE — §8's gate battery and §9's log row, as one command.
#
# The agent has already done the judgement work: copied the spec, closed the compiler gap, and had the
# diff reviewed (§6). Everything after that is mechanical and ORDER-SENSITIVE, and doing it by hand is
# how a tick ends up having tested a tree it did not push. This script does it in one pass:
#
#     rebase  ->  rebuild  ->  full suite  ->  GATES  ->  write logs  ->  commit  ->  push
#
# ⭐ THE ORDER IS THE POINT. The rebase comes FIRST so the suite runs on the tree that will actually be
#    pushed — a rebase after testing means pushing something nobody tested. And if the push is REJECTED
#    because another agent landed meanwhile (this happened twice on 2026-08-02), the script does NOT
#    just retry the push: it rebases and RE-RUNS the whole suite, because the tree changed under it.
#
# ⭐ NOTHING IS WRITTEN OR COMMITTED UNTIL EVERY GATE IS GREEN. The log rows are appended after the
#    gates pass, never before, so a failed run leaves the tree exactly as it found it (minus the build
#    outputs, which are gitignored). A non-zero exit means STOP AND READ — the tick is not done.
#
# THE GATES (all of them stop the script):
#   - rebase / stash-pop applied without conflict
#   - build exit 0            (bootstrap too, if it is stale)
#   - full unfiltered suite reports `failed: 0`      <- the gate is ZERO FAILURES, not a total (§8)
#   - no memory leak (exit 101)
#   - this spec's `<!-- test:` markers == the cases that actually RAN for it   <- §2, the local check
#   - no name is both `test:` and `disabled-test:`, and the ported spec has ZERO disabled cases (§4)
#
# The suite TOTAL is deliberately NOT a gate. A green suite is green whatever it started at, and other
# agents commit to main between ticks; see §8's box in .claude/skills/spec-port/SKILL.md.
#
# USAGE
#   scripts/spec-port-finish.sh --spec <name> --outcome PORTED --cases <N>/<N> \
#       --note-file <f> --message-file <f> [--build-cost-note-file <f>] [--dry-run] [--no-push]
#
#   --note-file            prose for the docs/spec-port-log.md row's `note` column. The measured
#                          `Suite <before> → <after>` sentence is appended by the script.
#   --message-file         the full git commit message.
#   --build-cost-note-file prose for the maxon-shv2/build-cost-log.md row. REQUIRED when compiler
#                          source changed; refused when it did not (the row would be noise).
#   --dry-run              run every gate, write nothing, commit nothing. Use it to check a tick.
#
set -o pipefail

# ⭐ RUN FROM A COPY OUTSIDE THE INDEX, BEFORE TOUCHING ANYTHING.
#
# This script stashes the working tree, and bash reads a script INCREMENTALLY — so if its own file is
# among what gets stashed, the bytes bash has not read yet are replaced underneath it. That does not
# fail cleanly, it fails weirdly. (Caught on the first run, 2026-08-02: it stashed and restored itself
# and survived only because bash had buffered far enough ahead.)
#
# Re-exec from `temp/`, which is gitignored — and `git stash -u` does NOT touch ignored files (that is
# `-a`). So the running bytes are permanently out of reach of anything below. This is why there is no
# "commit the script first" refusal: it would have made every edit to this file cost a commit before it
# could be tested, to solve a problem a copy solves outright.
if [ -z "${SPEC_PORT_FINISH_REPO:-}" ]; then
  _repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
  mkdir -p "$_repo/temp" || { echo "cannot create $_repo/temp" >&2; exit 1; }
  _copy="$_repo/temp/.spec-port-finish.running.sh"
  cp "${BASH_SOURCE[0]}" "$_copy" || { echo "cannot copy self to $_copy" >&2; exit 1; }
  export SPEC_PORT_FINISH_REPO="$_repo"
  exec bash "$_copy" "$@"
fi

readonly REPO="$SPEC_PORT_FINISH_REPO"
readonly SHV2="$REPO/maxon-shv2/.maxon/maxon-shv2.exe"
readonly BOOTSTRAP="$REPO/bin/maxon.exe"
readonly LOGDIR="$REPO/temp"
readonly SPEC_LOG="$REPO/docs/spec-port-log.md"
readonly COST_LOG="$REPO/maxon-shv2/build-cost-log.md"
readonly MAX_PUSH_ATTEMPTS=3

SPEC=""; OUTCOME=""; CASES=""; NOTE_FILE=""; MSG_FILE=""; COST_NOTE_FILE=""
DRY_RUN=0; NO_PUSH=0; STASHED=0

die()  { printf '\n\033[31m✗ %s\033[0m\n' "$*" >&2; exit 1; }
ok()   { printf '\033[32m✓\033[0m %s\n' "$*"; }
step() { printf '\n\033[1m── %s\033[0m\n' "$*"; }
warn() { printf '\033[33m⚠\033[0m %s\n' "$*"; }

# `printf "%'d"` needs a thousands-separator locale, which Git Bash does not reliably have — and the
# cost log's numbers are read by eye, so the separators are not decoration.
commafy() { echo "$1" | sed -e :a -e 's/\(.*[0-9]\)\([0-9]\{3\}\)/\1,\2/;ta'; }

# A stash raised by this script must never outlive it: an aborted run that left the agent's whole tick
# sitting in the stash list looks exactly like the work having vanished.
restore_stash() {
  if [ "$STASHED" = "1" ]; then
    warn "restoring your working tree from the stash"
    git -C "$REPO" stash pop --quiet || warn "STASH POP FAILED — your work is in \`git stash list\`, recover it by hand"
    STASHED=0
  fi
}
trap restore_stash EXIT

while [ $# -gt 0 ]; do
  case "$1" in
    --spec)                 SPEC="$2";           shift 2 ;;
    --outcome)              OUTCOME="$2";        shift 2 ;;
    --cases)                CASES="$2";          shift 2 ;;
    --note-file)            NOTE_FILE="$2";      shift 2 ;;
    --message-file)         MSG_FILE="$2";       shift 2 ;;
    --build-cost-note-file) COST_NOTE_FILE="$2"; shift 2 ;;
    --dry-run)              DRY_RUN=1;           shift   ;;
    --no-push)              NO_PUSH=1;           shift   ;;
    -h|--help)              sed -n '2,45p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) die "unknown argument: $1" ;;
  esac
done

[ -n "$SPEC" ]      || die "--spec is required"
[ -n "$OUTCOME" ]   || die "--outcome is required (PORTED — the only outcome)"
[ -n "$CASES" ]     || die "--cases is required, e.g. 30/30"
[ -n "$NOTE_FILE" ] || die "--note-file is required"
[ -f "$NOTE_FILE" ] || die "--note-file does not exist: $NOTE_FILE"
# PORTED is the only outcome. Deferring a spec was removed by user ruling 2026-08-02 (SKILL.md §4):
# a tick that has not made every case pass is not finished, so there is no other way for one to end.
case "$OUTCOME" in PORTED) ;; *) die "--outcome must be PORTED — deferring a spec is not an option (SKILL.md §4), got '$OUTCOME'" ;; esac
if [ "$DRY_RUN" = "0" ]; then
  [ -n "$MSG_FILE" ] || die "--message-file is required (omit only with --dry-run)"
  [ -f "$MSG_FILE" ] || die "--message-file does not exist: $MSG_FILE"
fi

mkdir -p "$LOGDIR"
cd "$REPO" || die "cannot cd to $REPO"
[ "$(git rev-parse --abbrev-ref HEAD)" = "main" ] || die "not on main — this repo develops directly on main"
[ -f "specs-shv2/$SPEC.md" ] || die "specs-shv2/$SPEC.md does not exist"

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "1/7  Rebase onto origin/main"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
if [ -n "$(git status --porcelain)" ]; then
  git stash push -u --quiet -m "spec-port-finish: $SPEC" || die "git stash failed"
  STASHED=1
  ok "working tree stashed"
fi

git fetch --quiet origin || die "git fetch failed"
BASE_BEFORE="$(git rev-parse HEAD)"
git pull --rebase --quiet || die "rebase failed — resolve it by hand, then re-run"
BASE_AFTER="$(git rev-parse HEAD)"

if [ "$STASHED" = "1" ]; then
  git stash pop --quiet || die "stash pop CONFLICTED — resolve by hand; your work is in the tree"
  STASHED=0
fi

if [ "$BASE_BEFORE" = "$BASE_AFTER" ]; then
  ok "already up to date with origin/main"
else
  ok "rebased $(git rev-list --count "$BASE_BEFORE".."$BASE_AFTER") commit(s) onto $(git log --oneline -1 --format=%h)"
  warn "upstream moved — the suite below runs on the REBASED tree, which is the point"
fi

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "2/7  Build"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
# The bootstrap builds shv2, so a stale bootstrap silently emits a shv2 that does not match its own
# sources. Rebuild it when anything under maxon-sharp/ (including the GENERATED ErrorCode.g.cs, which a
# registry change moves) is newer than the binary.
if [ ! -x "$BOOTSTRAP" ] || [ -n "$(find maxon-sharp -name '*.cs' -newer "$BOOTSTRAP" -print -quit 2>/dev/null)" ]; then
  echo "  bootstrap is stale or missing — rebuilding it first"
  ( cd maxon-sharp && dotnet build --nologo -v q ) > "$LOGDIR/finish-bootstrap.log" 2>&1 \
    || { tail -30 "$LOGDIR/finish-bootstrap.log"; die "bootstrap build FAILED — see $LOGDIR/finish-bootstrap.log"; }
  ok "bootstrap rebuilt"
else
  ok "bootstrap is current"
fi

BUILD_START=$(date +%s%3N)
"$BOOTSTRAP" build maxon-shv2 > "$LOGDIR/finish-build.log" 2>&1 \
  || { tail -30 "$LOGDIR/finish-build.log"; die "shv2 build FAILED — see $LOGDIR/finish-build.log"; }
BUILD_MS=$(( $(date +%s%3N) - BUILD_START ))
BUILD_S=$(awk "BEGIN{printf \"%.1f\", $BUILD_MS/1000}")

# The build prints one `Wrote N bytes code, ... to <path>` line PER ARTIFACT, and the first is the
# throwaway `.maxon-run.exe` in the cache. Select by the destination naming maxon-shv2.exe, not by
# position — "the last one" is true today and is not a contract.
CODE_BYTES="$(grep 'bytes code' "$LOGDIR/finish-build.log" | grep 'maxon-shv2.exe' | tail -1 | grep -oE 'Wrote [0-9]+' | grep -oE '[0-9]+')"
[ -x "$SHV2" ] || die "shv2 binary missing after a successful build: $SHV2"
EXE_BYTES="$(stat -c '%s' "$SHV2")"

# A build with nothing to do emits no `Wrote …` line at all. That is fine for the gates — the binary is
# current either way — but it means there is NO code-bytes measurement, and a cost row is a trend that
# gets read: a silent 0 there is worse than no row. Say so now, and refuse the row later (§5).
if [ -n "$CODE_BYTES" ]; then
  ok "shv2 built in ${BUILD_S}s — exe $(commafy "$EXE_BYTES") bytes, code $(commafy "$CODE_BYTES") bytes"
else
  ok "shv2 up to date in ${BUILD_S}s (nothing to rebuild) — exe $(commafy "$EXE_BYTES") bytes"
  warn "no 'code bytes' figure: this build did no work, so there was nothing to measure"
fi

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "3/7  Full unfiltered spec suite"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
SUITE_LOG="$LOGDIR/finish-suite.log"
SUITE_START=$(date +%s%3N)
"$SHV2" spec-test > "$SUITE_LOG" 2>&1
SUITE_EXIT=$?
SUITE_MS=$(( $(date +%s%3N) - SUITE_START ))
SUITE_S=$(awk "BEGIN{printf \"%.1f\", $SUITE_MS/1000}")

SUMMARY="$(grep -oE '^[0-9]+ passed, [0-9]+ failed' "$SUITE_LOG" | tail -1)"
[ -n "$SUMMARY" ] || { tail -40 "$SUITE_LOG"; die "could not parse a summary from the suite — see $SUITE_LOG"; }
PASSED="$(echo "$SUMMARY" | awk '{print $1}')"
FAILED="$(echo "$SUMMARY" | awk '{print $3}')"
TOTAL=$(( PASSED + FAILED ))
CPU_TICKS="$(grep -oE 'compile CPU: [0-9]+' "$SUITE_LOG" | tail -1 | grep -oE '[0-9]+')"
echo "  $SUMMARY  in ${SUITE_S}s"

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "4/7  Gates"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
[ "$SUITE_EXIT" != "101" ] || { grep -iE 'leak' "$SUITE_LOG" | head -20; die "MEMORY LEAK (exit 101)"; }
ok "no memory leak"

if [ "$FAILED" != "0" ]; then
  echo
  grep -n '^FAIL' "$SUITE_LOG" | head -40
  echo
  warn "the full log is at $SUITE_LOG — READ IT at each hit, the marker line is a headline not the evidence"
  warn "you did NOT measure a baseline, so do not assume this red is yours (§0):"
  warn "  do the failures touch what you changed? if that is not obvious in a minute, measure the"
  warn "  control now — git stash -u && $SHV2 spec-test ; git stash pop"
  die "$FAILED test(s) FAILED — the gate is zero failures"
fi
ok "suite green: $PASSED passed, 0 failed"

[ "$SUITE_EXIT" = "0" ] || die "suite exited $SUITE_EXIT despite reporting 0 failures — that is a runner problem, not a test problem"

if [ "$OUTCOME" = "PORTED" ]; then
  MARKERS="$(grep -c '<!-- test:' "specs-shv2/$SPEC.md")"
  DISABLED="$(grep -c '<!-- disabled-test:' "specs-shv2/$SPEC.md")"
  RAN="$(grep -cE "^(PASS|FAIL) $SPEC/" "$SUITE_LOG")"

  # §2: the count is the gate, not the colour. A spec can pass by running NOTHING — `status: draft`
  # returns zero tests for the whole file, and a stray `## ` heading ends the active-test region.
  # DO NOT subtract the disabled cases: `<!-- disabled-test:` does not match the `<!-- test:` grep.
  [ "$MARKERS" = "$RAN" ] || die "COUNT MISMATCH: specs-shv2/$SPEC.md has $MARKERS \`<!-- test:\` markers but $RAN case(s) ran.
     A shortfall is a defect in the port, never a pass. Look for a \`## \` heading above the missing
     cases (it ENDS the active-test region) or \`status: draft\` in the frontmatter."
  ok "count check: $MARKERS markers == $RAN ran"

  # §4: you may not disable a case. A file you PORTED carrying one is a skipped case, not a shelve.
  [ "$DISABLED" = "0" ] || die "specs-shv2/$SPEC.md has $DISABLED \`<!-- disabled-test:\` marker(s).
     Disabling a case is not an option (SKILL.md §4, user ruling 2026-08-02). Implement the mechanism
     the case needs — however large — and enable it. If you cannot locate the gap, that is unfinished
     diagnosis, not a reason to skip."

  DUPES="$(grep -o '<!-- \(disabled-\)\?test: [^ ]*' "specs-shv2/$SPEC.md" | sed 's/.*test: //' | sort | uniq -d)"
  [ -z "$DUPES" ] || die "a name is BOTH \`test:\` and \`disabled-test:\` — the file reads as though a case were disabled while it still runs:
$DUPES"
  ok "no name is both test: and disabled-test:"

  if diff -q "specs/$SPEC.md" "specs-shv2/$SPEC.md" >/dev/null 2>&1; then
    ok "spec is byte-identical to /specs — zero retractions"
  else
    warn "spec DIFFERS from /specs/$SPEC.md ($(diff "specs/$SPEC.md" "specs-shv2/$SPEC.md" | grep -c '^[<>]') changed lines)"
    warn "  every retraction needs pointable ratification (§1) and belongs in the commit message"
  fi

  UNTRACKED_GOLDENS="$(git ls-files --others --exclude-standard specs-shv2/fragments/ | wc -l)"
  [ "$UNTRACKED_GOLDENS" = "0" ] || ok "$UNTRACKED_GOLDENS newly minted golden(s) will be committed"
fi

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "5/7  Log rows"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
# "compiler source changed" is asked of the tree being committed, against the upstream it sits on.
COMPILER_CHANGED=0
if [ -n "$(git status --porcelain -- 'maxon-shv2/Compiler' 'maxon-sharp' 'stdlib' | head -1)" ]; then
  COMPILER_CHANGED=1
fi

if [ "$COMPILER_CHANGED" = "1" ] && [ -z "$COST_NOTE_FILE" ]; then
  die "compiler source changed, so maxon-shv2/build-cost-log.md needs a row — pass --build-cost-note-file.
     Its three numbers already fall out of this run: build ${BUILD_S}s, exe $EXE_BYTES, code ${CODE_BYTES:-?}, suite ${SUITE_S}s, tests $TOTAL."
fi
if [ "$COMPILER_CHANGED" = "0" ] && [ -n "$COST_NOTE_FILE" ]; then
  die "--build-cost-note-file was given but no compiler source changed — that row would be noise"
fi
[ -z "$COST_NOTE_FILE" ] || [ -f "$COST_NOTE_FILE" ] || die "--build-cost-note-file does not exist: $COST_NOTE_FILE"

# Refuse to write a cost row with a fabricated size. `code bytes` is EXACT and bit-reproducible, which
# is the whole reason the log trusts it over the two wall-clock times — a 0 standing in for "not
# measured" would be indistinguishable from a real reading.
if [ -n "$COST_NOTE_FILE" ] && [ -z "$CODE_BYTES" ]; then
  die "a cost row was asked for, but this build did no work so there is no 'code bytes' measurement.
     Force a real build and re-run:  rm -f '$SHV2' && $0 <same args>"
fi

TODAY="$(date +%Y-%m-%d)"
NOTE="$(tr '\n' ' ' < "$NOTE_FILE" | sed 's/  */ /g; s/^ //; s/ $//; s/|/\\|/g')"
SPEC_ROW="| $SPEC | $TODAY | $OUTCOME | $CASES | $NOTE Suite $TOTAL/$FAILED after this tick. |"

if [ -n "$COST_NOTE_FILE" ]; then
  COST_NOTE="$(tr '\n' ' ' < "$COST_NOTE_FILE" | sed 's/  */ /g; s/^ //; s/ $//; s/|/\\|/g')"
  CPU_E9="$(awk "BEGIN{printf \"%.1f\", ${CPU_TICKS:-0}/1000000000}")"
  COST_ROW="| $TODAY | \`spec-port: $SPEC\` | $COST_NOTE | $BUILD_S | $(commafy "$EXE_BYTES") | $(commafy "${CODE_BYTES:-0}") | $SUITE_S | ${CPU_E9}e9 | $TOTAL |"
fi

if [ "$DRY_RUN" = "1" ]; then
  warn "DRY RUN — writing nothing. The rows would be:"
  echo; echo "  $SPEC_ROW"
  [ -z "$COST_NOTE_FILE" ] || { echo; echo "  $COST_ROW"; }
  echo
  ok "all gates passed"
  printf '\n\033[32m✓ DRY RUN CLEAN\033[0m — re-run without --dry-run to write, commit and push.\n'
  exit 0
fi

printf '%s\n' "$SPEC_ROW" >> "$SPEC_LOG"
ok "appended a row to docs/spec-port-log.md"

if [ -n "$COST_NOTE_FILE" ]; then
  # ⚠ THIS FILE HAS MORE THAN ONE TABLE. A parallel agent's merge duplicated it, and the second copy
  # carries the OLDER 8-column schema (no `compile cpu`). The row goes in the FIRST table, whose header
  # names that column — appending at EOF would silently file a 9-column row under an 8-column header.
  SECOND_HEADER="$(grep -n '^| date | commit | change |' "$COST_LOG" | sed -n 2p | cut -d: -f1)"
  if [ -n "$SECOND_HEADER" ]; then
    INSERT_AFTER=$(( SECOND_HEADER - 1 ))
    while [ "$INSERT_AFTER" -gt 1 ] && [ -z "$(sed -n "${INSERT_AFTER}p" "$COST_LOG" | grep '^|')" ]; do
      INSERT_AFTER=$(( INSERT_AFTER - 1 ))          # skip blank lines between the tables
    done
  else
    INSERT_AFTER="$(grep -n '^| [0-9]\{4\}-[0-9][0-9]-' "$COST_LOG" | tail -1 | cut -d: -f1)"
  fi
  [ -n "$INSERT_AFTER" ] || die "cannot locate a table row in $COST_LOG — add the row by hand"

  awk -v n="$INSERT_AFTER" -v row="$COST_ROW" 'NR==n{print; print row; next} {print}' "$COST_LOG" > "$COST_LOG.tmp" \
    && mv "$COST_LOG.tmp" "$COST_LOG" || die "failed to write $COST_LOG"
  ok "inserted a row into maxon-shv2/build-cost-log.md after line $INSERT_AFTER"
fi

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "6/7  Commit"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
git add -A || die "git add failed"
[ -n "$(git diff --cached --name-only)" ] || die "nothing staged — there is nothing to land"
echo "  staging $(git diff --cached --name-only | wc -l) file(s)"
git commit --quiet -F "$MSG_FILE" || die "git commit failed"
ok "committed $(git log --oneline -1)"

if [ "$NO_PUSH" = "1" ]; then
  printf '\n\033[32m✓ COMMITTED (--no-push)\033[0m — push it yourself when ready.\n'
  exit 0
fi

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "7/7  Push"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
# ⭐ A REJECTED PUSH IS NOT A RETRY — IT IS A NEW TREE. Another agent landed while this tick was
#    running, so rebasing and pushing again would push something the suite never saw. Re-run it.
attempt=1
while : ; do
  if git push --quiet origin main 2>"$LOGDIR/finish-push.log"; then
    ok "pushed $(git log --oneline -1 --format=%h)"
    break
  fi

  [ "$attempt" -lt "$MAX_PUSH_ATTEMPTS" ] || { cat "$LOGDIR/finish-push.log"; die "push rejected $attempt times — land it by hand"; }
  warn "push rejected (attempt $attempt) — upstream moved. Rebasing and RE-RUNNING the suite."
  attempt=$(( attempt + 1 ))

  git pull --rebase --quiet || die "rebase after a rejected push FAILED — resolve by hand"
  "$BOOTSTRAP" build maxon-shv2 > "$LOGDIR/finish-build.log" 2>&1 \
    || { tail -30 "$LOGDIR/finish-build.log"; die "shv2 build FAILED after the rebase"; }
  "$SHV2" spec-test > "$SUITE_LOG" 2>&1
  RE_EXIT=$?
  RE_SUMMARY="$(grep -oE '^[0-9]+ passed, [0-9]+ failed' "$SUITE_LOG" | tail -1)"
  RE_FAILED="$(echo "$RE_SUMMARY" | awk '{print $3}')"
  [ "$RE_EXIT" != "101" ] || die "MEMORY LEAK after rebasing onto the new upstream"
  [ "$RE_FAILED" = "0" ] || { grep -n '^FAIL' "$SUITE_LOG" | head -20; die "your change and the new upstream do NOT compose: $RE_SUMMARY"; }
  ok "re-verified on the new upstream: $RE_SUMMARY"
done

printf '\n\033[32m✓ TICK COMPLETE\033[0m — %s %s, %s, suite %s passed / 0 failed, pushed.\n' \
  "$SPEC" "$OUTCOME" "$CASES" "$PASSED"
