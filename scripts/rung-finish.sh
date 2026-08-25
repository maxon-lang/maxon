#!/usr/bin/env bash
#
# THE /rung FINISH LINE — the gate battery, the cross-target gate, the landing and the board closure,
# as one command. It is `.claude/skills/rung/SKILL.md` §8; read `reference/gates.md` for what a red one
# MEANS, because this script can tell you a gate failed and never why it matters.
#
#   rebase the branch -> build -> suite -> GATES -> BASE-DRIFT A/B -> cross-target -> ff-merge
#     -> flip the board -> commit -> push -> tear down the worktree -> REAP the leaked ones
#
# ⭐ THE ORDER IS THE POINT, and by hand it has gone wrong in every direction:
#
#    - the rebase comes FIRST, so the suite runs on the tree that will actually be pushed. Rebasing
#      after testing means landing something nobody tested — and in SLICE mode another agent lands
#      while you work, so this is the COMMON case, not the exception.
#    - a REJECTED PUSH IS NOT A RETRY. The tree changed under you; the suite re-runs before it lands.
#    - the worktree is torn down LAST, after the push, so a failure anywhere leaves the work intact.
#
# ⭐⭐ WHAT THIS SCRIPT MEASURES THAT NOBODY HAS BEEN MEASURING: THE GOLDEN-DRIFT DELTA.
#
#    `git status specs-shv2/fragments/` CANNOT tell you whether codegen moved — it answers "has anyone
#    REGENERATED a golden", and a tree can have every golden mismatching with a perfectly clean status.
#    It was reported as codegen evidence in FIVE rungs (X1, N3, X5, X6, A3h). The instrument that CAN
#    answer is the runner's own trailer:
#
#        note: N golden fragment(s) no longer match what the compiler emits.
#
#    …and N alone is still not the answer, because a rung inherits whatever drift `main` already
#    carries. THE ANSWER IS THE DELTA AGAINST THE MERGE BASE BUILT THE SAME WAY. So this script builds
#    origin/main in a scratch worktree and runs its suite, purely to subtract. That costs ~30 s and it
#    is the only honest way to say "this rung did not move codegen".
#
#    A NON-ZERO DELTA IS NOT A FAILURE. It is a codegen change that has to be EXPLAINED — pass
#    --codegen-note-file and the explanation goes in the report. "This codegen change is intended" is
#    not a question a machine can answer; whether one happened is.
#
# THE GATES (every one of them stops the script):
#   - the branch rebases onto origin/main cleanly
#   - build exit 0 (bootstrap too, if stale)
#   - full unfiltered shv2 suite: `failed: 0`      <- the gate is ZERO FAILURES, never a total
#   - no memory leak (exit 101)
#   - ZERO untracked goldens under specs-shv2/fragments/   <- a minted golden nobody committed
#   - the SELF-COMPILE: shv2 builds maxon-shv2 with the branch's own binary, exit 0. It is the ONLY
#     build that runs `checkUnusedExports` (E3092/E3093/E3094) — the bootstrap never does — so an
#     `export` a rung adds and nothing outside its file reads is invisible to every other gate. Found
#     2026-08-25 (EC1): a three-line contract commit broke the self-compile, the suite stayed 6673/0,
#     and it was found only because a later agent happened to self-compile. ~3 min.
#   - golden-drift delta vs the merge base: explained if non-zero
#   - a row in docs/optimization-log.md, or an explicit --no-ladder-row reason
#   - the C# suite + codegen neutrality, if the branch touched maxon-sharp/
#   - scripts/cross-target-gate.sh over every LOCAL target (arm64 is remote and NEVER blocks a rung)
#   - PLAN.md carries an uncommitted DETAIL row — a rung that wrote no detail is not closed
#
# USAGE
#   scripts/rung-finish.sh --batch <ID> --message-file <f>
#                          [--codegen-note-file <f>] [--no-ladder-row "<reason>"]
#                          [--branch <name>] [--csharp] [--no-cross-target "<reason>"]
#                          [--dry-run] [--no-push] [--keep-worktree]
#
#   --batch             the board row id; its Owner cell is where the BRANCH NAME comes from, so the
#                       board stays the one place that fact is written
#   --message-file      the closure commit message (the merge + the board flip land as one push)
#   --codegen-note-file why the emitted code moved. REQUIRED iff the drift delta is non-zero or the
#                       branch modified a committed golden
#   --no-ladder-row     skip the optimization-log.md check, with a reason that is echoed into the report
#   --branch            WAVE mode: name the branch directly, no board row is read or flipped
#   --dry-run           run every gate, merge nothing, write nothing, push nothing
#   --keep-worktree     keep THIS rung's worktree and branch — and skip the reap below with it, because
#                       both are the same request: leave the trees on disk alone
#
set -o pipefail

# ⭐ RUN FROM A COPY OUTSIDE THE INDEX. This script stashes the working tree, and bash reads a script
#    INCREMENTALLY — so if its own bytes are among what gets stashed, the part not yet read is replaced
#    underneath it. That does not fail cleanly, it fails weirdly. `temp/` is gitignored and `git stash
#    -u` does not touch ignored files, so the running bytes are permanently out of reach.
if [ -z "${RUNG_FINISH_REPO:-}" ]; then
  _repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
  mkdir -p "$_repo/temp" || { echo "cannot create $_repo/temp" >&2; exit 1; }
  _copy="$_repo/temp/.rung-finish.running.sh"
  cp "${BASH_SOURCE[0]}" "$_copy" || { echo "cannot copy self to $_copy" >&2; exit 1; }
  export RUNG_FINISH_REPO="$_repo"
  exec bash "$_copy" "$@"
fi

readonly REPO="$RUNG_FINISH_REPO"
readonly PLAN="$REPO/maxon-shv2/PLAN.md"
readonly LOGDIR="$REPO/temp"
readonly MAX_PUSH_ATTEMPTS=3
# How long a worktree must have gone UNWRITTEN before step 10 will consider it dead. See the reap
# block for why an idle test earns its place beside three git-state tests: it is the only one of the
# four that is not a reading of the commit graph, so it is the only one that can disagree with them.
readonly REAP_IDLE_MINUTES=30

# shellcheck source=lib/plan-board.sh
. "$REPO/scripts/lib/plan-board.sh" || { echo "cannot source scripts/lib/plan-board.sh" >&2; exit 1; }

# shellcheck source=lib/host-binaries.sh
# ⛔ THIS FILE IS THE ONE `host-binaries.sh` WAS WRITTEN FOR, AND IT WAS THE ONE THAT DID NOT SOURCE IT.
#    Its header names this script by name — "seven hard-coded `.exe` paths, so on this host not one
#    gate in the battery could run at all" — and the commit that moved those seven onto the helpers
#    added the `.` line to `rung-start.sh` and not here. Every call then resolved to the empty string,
#    so step 1 died with `no bootstrap in the worktree ()` on EVERY host including the one it was
#    developed on. Nothing caught it because nothing runs this script except a rung finishing.
. "$REPO/scripts/lib/host-binaries.sh" || { echo "cannot source scripts/lib/host-binaries.sh" >&2; exit 1; }

BATCH=""; MSG_FILE=""; CODEGEN_NOTE=""; NO_LADDER=""; BRANCH=""; RUN_CSHARP=0
NO_CROSS=""; DRY_RUN=0; NO_PUSH=0; KEEP_WORKTREE=0; STASHED=0

restore_stash() {
  if [ "$STASHED" = "1" ]; then
    warn "restoring your PLAN.md detail rows from the stash"
    git -C "$REPO" stash pop --quiet || warn "STASH POP FAILED — your work is in \`git stash list\`"
    STASHED=0
  fi
}
trap restore_stash EXIT

while [ $# -gt 0 ]; do
  case "$1" in
    --batch)             BATCH="$2";        shift 2 ;;
    --message-file)      MSG_FILE="$2";     shift 2 ;;
    --codegen-note-file) CODEGEN_NOTE="$2"; shift 2 ;;
    --no-ladder-row)     NO_LADDER="$2";    shift 2 ;;
    --branch)            BRANCH="$2";       shift 2 ;;
    --csharp)            RUN_CSHARP=1;      shift   ;;
    --no-cross-target)   NO_CROSS="$2";     shift 2 ;;
    --dry-run)           DRY_RUN=1;         shift   ;;
    --no-push)           NO_PUSH=1;         shift   ;;
    --keep-worktree)     KEEP_WORKTREE=1;   shift   ;;
    -h|--help)           sed -n '2,64p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) die "unknown argument: $1" ;;
  esac
done

[ -n "$BATCH" ] || [ -n "$BRANCH" ] || die "--batch is required (the board row id) â or --branch,
     which is WAVE mode: a rung with no board row to read or flip."
if [ "$DRY_RUN" = "0" ]; then
  [ -n "$MSG_FILE" ] || die "--message-file is required (omit only with --dry-run)"
  [ -f "$MSG_FILE" ] || die "--message-file does not exist: $MSG_FILE"
fi
[ -z "$CODEGEN_NOTE" ] || [ -f "$CODEGEN_NOTE" ] || die "--codegen-note-file does not exist: $CODEGEN_NOTE"

mkdir -p "$LOGDIR"
cd "$REPO" || die "cannot cd to $REPO"

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "1/10  Position — the branch, the worktree, and the DETAIL ROW you owe"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
[ "$(git rev-parse --abbrev-ref HEAD)" = "main" ] || die "this checkout is on '$(git rev-parse --abbrev-ref HEAD)', not main.
     The merge and the board flip both land ON main, so they run in the checkout that OWNS main —
     never in the worktree (which structurally cannot check main out)."

WAVE=0
if [ -n "$BRANCH" ]; then
  WAVE=1
  # BATCH is a board row id in the other mode; here it survives only as the scratch-tree name and
  # the stash label, so the branch supplies it rather than the caller inventing an id for nothing.
  [ -n "$BATCH" ] || BATCH="$BRANCH"
  ok "WAVE mode: branch '$BRANCH' given directly, no board row will be read or flipped"
else
  # ⭐ THE BRANCH NAME COMES OFF THE BOARD, not off an argument. The row is where that fact is written;
  #    a second spelling on the command line is how a branch and the row it claims stop matching, and
  #    a claim nobody can verify is worse than no claim.
  ROW_NO="$(row_line_no "$PLAN" "$BATCH")" || exit 1
  STATUS="$(row_status "$PLAN" "$BATCH")"
  case "$STATUS" in
    *🔶*) ;;
    *) die "row $BATCH is not claimed — its status cell reads '$(printf '%s' "$STATUS" | cut -c1-160)'.
     Only a live 🔶 claim can be closed. If you are running a rung with no board row, use --branch." ;;
  esac
  BRANCH="$(row_owner "$PLAN" "$BATCH" | tr -d '`')"
  [ -n "$BRANCH" ] || die "row $BATCH names no branch in its Owner cell"
  ok "row $BATCH (PLAN.md:$ROW_NO) → branch $BRANCH"
  MEMBERS="$(row_members "$PLAN" "$BATCH")"
  [ -z "$MEMBERS" ] || echo "  members to close with it: $(echo "$MEMBERS" | tr '\n' ' ')"
fi

git show-ref --verify --quiet "refs/heads/$BRANCH" || die "no local branch '$BRANCH'"

WORKTREE="$(git worktree list --porcelain | awk -v b="refs/heads/$BRANCH" '
  /^worktree /{ wt=substr($0,10) } /^branch /{ if ($2==b) { print wt; exit } }')"
[ -n "$WORKTREE" ] || die "no worktree has '$BRANCH' checked out (\`git worktree list\`).
     The gates run in the worktree — that is the tree holding the work."
ok "worktree $WORKTREE"

[ -z "$(git -C "$WORKTREE" status --porcelain)" ] || {
  git -C "$WORKTREE" status --short | head -20
  die "the worktree has UNCOMMITTED work. Everything the rung did must be committed on its branch
     before it can be gated — the suite below runs on HEAD, not on your working tree."; }
ok "worktree is clean — all rung work is committed"

# ⭐ THE DETAIL ROW IS PART OF THE RUNG, AND THIS IS WHERE IT GETS CHECKED. A rung's deliverable is the
#    set of markers it flipped; the row is where that is written down. The status flip below is
#    mechanical and this script does it — the PROSE is judgement and only you can write it.
PLAN_DIRTY="$(git diff --name-only -- maxon-shv2/PLAN.md)"
[ -n "$PLAN_DIRTY" ] || die "maxon-shv2/PLAN.md has no uncommitted changes, so this rung wrote NO
   DETAIL ROW. Write it first, here in the main checkout, and leave it uncommitted — this script
   flips the board's status cells on top of it and lands both in one commit.
   ⚠ A rung with open RESIDUALS is marked ◑, not ✅, and each residual goes into the PLAN.md section
     it belongs to. \"Core landed\" is not \"complete\"."
ok "PLAN.md carries uncommitted detail ($(git diff --numstat -- maxon-shv2/PLAN.md | awk '{print $1"+ "$2"-"}'))"

OTHER_DIRTY="$(git status --porcelain | grep -v ' maxon-shv2/PLAN\.md$' | head -5)"
[ -z "$OTHER_DIRTY" ] || { echo "$OTHER_DIRTY"; warn "other files are dirty in the main checkout; they will be stashed and restored, NOT committed"; }

SHV2="$(maxon_shv2_path "$WORKTREE")"
BOOTSTRAP="$(maxon_bootstrap_path "$WORKTREE")"
[ -x "$BOOTSTRAP" ] || die "no bootstrap in the worktree ($BOOTSTRAP) — bin/ is gitignored; copy it in"

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "2/10  Rebase the branch onto origin/main — BEFORE the suite, not after"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
git fetch --quiet origin || die "git fetch failed"
BASE_SHA="$(git rev-parse origin/main)"
OLD_TIP="$(git -C "$WORKTREE" rev-parse HEAD)"

if [ "$(git merge-base "$BRANCH" origin/main)" = "$BASE_SHA" ]; then
  ok "branch is already on top of origin/main ($(git log --oneline -1 --format=%h origin/main))"
else
  git -C "$WORKTREE" rebase --quiet origin/main || die "REBASE CONFLICTED. Resolve it in the worktree
     ($WORKTREE), then re-run. ⚠ If the agent who landed first touched YOUR lane's file, READ THEIR
     DIFF before re-gating: a clean textual rebase is not evidence the two changes compose — it is
     only evidence they were far apart in the file."
  ok "rebased onto origin/main; the suite below runs on the tree that will actually land"
  warn "upstream moved, so the tree you gated before this point no longer exists"
fi
TIP="$(git -C "$WORKTREE" rev-parse HEAD)"
[ "$OLD_TIP" = "$TIP" ] || echo "  branch tip ${OLD_TIP:0:9} → ${TIP:0:9}"

CHANGED_FILES="$(git -C "$WORKTREE" diff --name-only "$BASE_SHA...$TIP")"
# ⚠ `stdlib/` IS IN THIS TRIGGER, AND IT WAS MISSING — found by the BATCH14 review, 2026-08-03.
#   The condition used to ask only about `maxon-sharp/`, on the reading that the C# suite gates the C#
#   compiler. But **the bootstrap COMPILES `stdlib/` too**, and `/specs` is its suite — so a stdlib edit
#   made from an shv2 rung would have landed with the one suite that covers it never run. The review hit
#   this concretely: it declined a one-fact cleanup in `stdlib/Subprocess.maxon` partly *because* this
#   script would not have gated it. A gate nobody can reach is not a gate.
# ⛔ `grep -c … >/dev/null`, NEVER `grep -q` — W71, MEASURED 2026-08-19 by W157 (1651 changed files).
#    `grep -q` exits at the FIRST match and closes the pipe; `echo` then takes SIGPIPE, and `set -o
#    pipefail` (line 65) turns that into 141. So the bigger the rung, the likelier this reads FALSE
#    **because** the answer was yes. Here that silently left RUN_CSHARP unset — a gate not running,
#    with no message; at the ladder-row check below it failed a rung that had written its row.
#    `grep -c` consumes the whole stream, so there is no early exit and no signal.
echo "$CHANGED_FILES" | grep -cE '^(maxon-sharp|stdlib)/' >/dev/null && RUN_CSHARP=1
[ "$RUN_CSHARP" = "0" ] || ok "the branch touched maxon-sharp/ or stdlib/ — the C# suite is now part of this battery"

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "3/10  Build"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
build_tree() {                       # build_tree <tree> <tag> [force-bootstrap] — bootstrap, then shv2
  local tree="$1" tag="$2" force="${3:-0}"
  if [ "$force" = "1" ] || [ -n "$(find "$tree/maxon-sharp" -name '*.cs' -newer "$(maxon_bootstrap_path "$tree")" -print -quit 2>/dev/null)" ]; then
    echo "  [$tag] bootstrap is stale — rebuilding it first (it BUILDS shv2, so a stale one emits an"
    echo "        shv2 that does not match its own sources)"
    ( cd "$tree/maxon-sharp" && dotnet build --nologo -v q ) > "$LOGDIR/rung-$tag-bootstrap.log" 2>&1 \
      || { tail -30 "$LOGDIR/rung-$tag-bootstrap.log"; die "[$tag] bootstrap build FAILED"; }
  fi
  ( cd "$tree" && "$(maxon_bootstrap_path .)" build maxon-shv2 ) > "$LOGDIR/rung-$tag-build.log" 2>&1 \
    || { tail -30 "$LOGDIR/rung-$tag-build.log"; die "[$tag] shv2 build FAILED — see $LOGDIR/rung-$tag-build.log"; }
  # A build that succeeds while printing warnings is not a green build (Code Quality: zero warnings).
  local warns
  warns="$(grep -ciE '^[^:]*: *warning' "$LOGDIR/rung-$tag-build.log")"
  [ "$warns" = "0" ] || warn "[$tag] the build emitted $warns warning line(s) — see $LOGDIR/rung-$tag-build.log"
}

# ⛔⛔ THE TIP'S BOOTSTRAP IS FORCED, AND THE mtime TEST INSIDE `build_tree` IS WHY.
#   That test asks "is any `.cs` NEWER than the binary?" — which is the right question for a tree that
#   BUILT its binary, and an unanswerable one for a tree that was HANDED one. `rung-start.sh` seeds a
#   worktree with `cp -r bin/`, stamping the copy with NOW while `git worktree add` stamps every source
#   with NOW; the copy therefore always looks fresh, whatever commit its CONTENT was built at.
#   Measured 2026-08-10, three times in one session: worktrees cut from a main that already carried
#   ARR0's `of`-is-not-a-keyword change were seeded with a bootstrap built before it, and the C# suite
#   failed `keyword-parameter-names` — a false red charged to the rung, each costing a full battery.
#   The base tree below is force-built for the same reason and always has been; the tip was the half
#   nobody re-ran.
build_tree "$WORKTREE" "tip" 1
ok "shv2 built from the rebased branch, on a bootstrap rebuilt from that branch's own sources"

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "4/10  The full unfiltered shv2 suite"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
# ⚠ REDIRECTED, never piped. A pipe decides what to keep BEFORE you know what failed, so a red run
#   costs a second full run just to read what the first one already had.
run_suite() {                        # run_suite <tree> <binary> <log> [target] -> sets PASSED FAILED DRIFT
  local tree="$1" bin="$2" log="$3" target="${4:-}"
  if [ -n "$target" ]; then
    ( cd "$tree" && "$bin" spec-test "--target=$target" ) > "$log" 2>&1
  else
    ( cd "$tree" && "$bin" spec-test ) > "$log" 2>&1
  fi
  SUITE_EXIT=$?
  local summary
  summary="$(grep -oE '^[0-9]+ passed, [0-9]+ failed' "$log" | tail -1)"
  [ -n "$summary" ] || { tail -40 "$log"; die "could not parse a summary from the suite — see $log"; }
  PASSED="$(echo "$summary" | awk '{print $1}')"
  FAILED="$(echo "$summary" | awk '{print $3}')"
  # SILENT when nothing drifted, which is the normal case — so "no note" means zero.
  #
  # ⛔ MATCH THE NOTE'S OWN WORDING, NOT JUST ITS SHAPE. The runner prints TWO `note: N golden
  #   fragment(s) …` blocks, and they count different things: one is DRIFT ("no longer match what the
  #   compiler emits"), the other is UNTRACKED ("are NOT TRACKED BY GIT"). This used to take the LAST
  #   number of that shape, which is the untracked one whenever both are present.
  #
  #   That is not a rare case — it is the BASE's normal case. The base is a fresh worktree of
  #   origin/main, so running its suite MINTS a golden for every case the branch added, and those are
  #   untracked by construction. Measured 2026-08-04: base drift read 291 and untracked read 39, the
  #   script reported the base at 39, and a rung whose real delta was 4 was accused of a 256-fragment
  #   codegen change. A gate that reports a number which is not the one it names is worse than no
  #   gate: it sends you looking for a change that never happened, and it would hide a real one just
  #   as readily whenever the untracked count happened to be the larger.
  DRIFT="$(grep -oE 'note: [0-9]+ golden fragment\(s\) no longer match' "$log" | grep -oE '[0-9]+' | tail -1)"
  [ -n "$DRIFT" ] || DRIFT=0
}

SUITE_LOG="$LOGDIR/rung-tip-suite.log"
run_suite "$WORKTREE" "$SHV2" "$SUITE_LOG"
TIP_PASSED="$PASSED"; TIP_FAILED="$FAILED"; TIP_DRIFT="$DRIFT"; TIP_EXIT="$SUITE_EXIT"
echo "  $TIP_PASSED passed, $TIP_FAILED failed   (golden drift: $TIP_DRIFT)"

[ "$TIP_EXIT" != "101" ] || { grep -iE 'leak' "$SUITE_LOG" | head -20; die "MEMORY LEAK — exit 101.
     Leaks are not ok. A leak this rung's mechanism can reach is FIXED, or the leak-causing construct
     is cleanly REJECTED, before merge — NEVER deferred."; }
ok "no memory leak"

if [ "$TIP_FAILED" != "0" ]; then
  echo
  grep -n '^FAIL' "$SUITE_LOG" | head -40
  echo
  warn "the full log is $SUITE_LOG — READ IT at each hit. The marker line is a headline, not the"
  warn "  evidence: a failed compile embeds the compiler's whole stderr."
  die "$TIP_FAILED test(s) FAILED. The gate is zero failures, INCLUDING every pre-existing test.
     ⛔ Never turn a gate green by narrowing what it tests."
fi
[ "$TIP_EXIT" = "0" ] || die "the suite exited $TIP_EXIT while reporting 0 failures — that is a runner
     problem, not a test problem"
ok "suite green: $TIP_PASSED passed, 0 failed"

# A minted golden left untracked is invisible to `git status` noise and to the summary alike. It has
# been found three times in one session, every time by a LATER rung's baseline.
UNTRACKED="$(git -C "$WORKTREE" ls-files --others --exclude-standard specs-shv2/fragments/)"
[ -z "$UNTRACKED" ] || { echo "$UNTRACKED" | head -20; die "$(echo "$UNTRACKED" | wc -l | tr -d ' ') golden(s) were MINTED and never committed.
     \`git add\` them on the branch and commit, then re-run — a golden this rung created belongs to it."; }
ok "no untracked goldens"

# ⭐ THE SELF-COMPILE — the gate the suite is not. `checkUnusedExports` (E3092/E3093/E3094) runs only
#   when shv2 compiles a program, and the only program that exercises every `export` in the compiler is
#   the compiler itself; the bootstrap builds it without that pass. So a rung that adds an `export`
#   nobody outside the file reads — or that shifts a type interner (the E3093 false findings of
#   2026-08-25 were exactly that) — passes every gate above and breaks stage-2. This is also what
#   `scripts/self-host-ab.sh`, the emitted-code instrument, needs green before it can measure anything.
#   The output goes to LOGDIR and is discarded; only the exit code and the E-lines matter here.
SELFCOMPILE_LOG="$LOGDIR/rung-selfcompile.log"
( cd "$WORKTREE" && "$SHV2" build maxon-shv2 -o "$LOGDIR/rung-selfcompile" ) > "$SELFCOMPILE_LOG" 2>&1
SELFCOMPILE_EXIT=$?
if [ "$SELFCOMPILE_EXIT" != "0" ]; then
  grep -nE 'error E[0-9]{4}' "$SELFCOMPILE_LOG" | head -20
  die "the SELF-COMPILE exited $SELFCOMPILE_EXIT — shv2 cannot build itself on this branch. The full log is
     $SELFCOMPILE_LOG. An E3092/E3093/E3094 here is a declaration more visible than its uses: narrow it
     (or read UnusedExportCheck's interner note before believing a finding on a type the rung never touched)."
fi
ok "self-compile: shv2 builds itself (exit 0)"

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "5/10  The golden-drift DELTA against the merge base"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
# ⛔ `git status specs-shv2/fragments/` cannot answer this and was believed to in five rungs. The
#    runner's drift count can — but a rung INHERITS whatever drift main already carries, so the count
#    alone is not the answer either. The DELTA against a like-for-like base build is.
BASE_TREE="$(cd .. && pwd)/maxon-$(echo "$BATCH" | tr '[:upper:]' '[:lower:]')-base"
cleanup_base() { [ -e "$BASE_TREE" ] && git worktree remove --force "$BASE_TREE" >/dev/null 2>&1; }

# ⭐ MEASURE EACH COMMIT ONCE, EVER. The base measurement is a property of a COMMIT, not of a rung — so
#    it is cached under temp/ by SHA, and every rung that lands on the same `origin/main` reads it
#    instead of re-deriving it. Two things fill this cache for free:
#      · the previous rung's TIP measurement IS the next rung's base, and is written below at merge;
#      · any earlier rung that already measured this base.
#    Without it, consecutive rungs on an unmoved `main` each spend a build + a suite re-deriving one
#    number. `temp/` is gitignored, so a cold cache costs exactly what it did before and nothing else.
BASE_CACHE="$LOGDIR/rung-drift-$BASE_SHA.txt"
if [ -r "$BASE_CACHE" ]; then
  read -r BASE_DRIFT BASE_PASSED BASE_FAILED < "$BASE_CACHE"
  ok "base $(git log --oneline -1 --format=%h "$BASE_SHA") already measured — reusing it (drift $BASE_DRIFT)"
  echo "     $BASE_CACHE — delete it to force a re-measure"
else
  if [ -e "$BASE_TREE" ]; then
    warn "a base worktree is already at $BASE_TREE — removing it"
    cleanup_base
  fi
  git worktree add --detach --quiet "$BASE_TREE" "$BASE_SHA" || die "cannot create the base worktree"
  cp -r "$WORKTREE/bin" "$BASE_TREE/bin" || { cleanup_base; die "cannot copy bin/ into the base worktree"; }
  echo "  building origin/main ($(git log --oneline -1 --format=%h "$BASE_SHA")) to subtract from…"

  # ⚠ THE COPIED bin/ IS THE *TIP'S* BOOTSTRAP, AND THAT WOULD CONTAMINATE THE A/B. `bin/` is
  #   gitignored, so the base worktree has no compiler and must borrow one — but if this rung touched
  #   maxon-sharp/, borrowing the tip's means building BASE sources with the CHANGED bootstrap, and the
  #   drift delta would measure nothing at all. Force the base to build its own in exactly that case.
  build_tree "$BASE_TREE" "base" "$RUN_CSHARP"
  run_suite "$BASE_TREE" "$(maxon_shv2_path "$BASE_TREE")" "$LOGDIR/rung-base-suite.log"
  BASE_DRIFT="$DRIFT"; BASE_PASSED="$PASSED"; BASE_FAILED="$FAILED"
  cleanup_base
  printf '%s %s %s\n' "$BASE_DRIFT" "$BASE_PASSED" "$BASE_FAILED" > "$BASE_CACHE"
fi

echo "  base  $BASE_PASSED passed, $BASE_FAILED failed   (golden drift: $BASE_DRIFT)"
echo "  tip   $TIP_PASSED passed, $TIP_FAILED failed   (golden drift: $TIP_DRIFT)"

if [ "$BASE_FAILED" != "0" ]; then
  warn "⚠ THE BASE IS ALREADY RED ($BASE_FAILED failed). Whatever this rung did, it did not cause that."
  warn "  A pre-existing red still gets fixed — but it is not this rung's gate, and its own commit is"
  warn "  the right home for the fix."
fi

# ─────────────────────────────────────────────────────────────────────────────────────────────────
# ⭐⭐ THE OTHER LOCAL LANES — REPORTED, NEVER GATED. User ruling, 2026-08-24.
#
# Everything above measures the HOST lane and nothing else, so a rung that moves x64-linux or wasm
# codegen passes the drift gate in silence. That is not hypothetical: BATCH44 re-minted x64-linux to
# ZERO drift, and by BATCH46's merge base 538 fragments already differed — moved by something in
# between that nobody measured, because nothing was looking. `cross-target-gate.sh` DOES run those
# lanes, but it reads only PASS/FAIL, and drift is a `note:` that never touches an exit code.
#
# ⛔ REPORT ONLY. These deltas never set CODEGEN_MOVED, never demand a note and never die — that is
#    the ruling, and it is the right shape: a second gate on a number this script cannot attribute
#    would stop rungs over movement that belongs to somebody else, which is the failure the host
#    delta already had to be taught to avoid.
#
# COST: the base half is cached by SHA exactly like the host's, and THIS rung's tip is the NEXT
# rung's base — so a lane is measured once per commit of `main`, not once per rung.
LOCAL_LANES=""
command -v wsl.exe >/dev/null 2>&1 && LOCAL_LANES="$LOCAL_LANES x64-linux"
[ -x "$REPO/vendor/wasmtime/wasmtime.exe" ] || [ -x "$REPO/vendor/wasmtime/wasmtime" ]   && LOCAL_LANES="$LOCAL_LANES wasm32-wasi"

if [ -n "$LOCAL_LANES" ]; then
  echo
  echo "  other LOCAL lanes — reported, never gated (the host delta above is the gate):"
  for lane in $LOCAL_LANES; do
    lane_cache="$LOGDIR/rung-drift-$BASE_SHA-$lane.txt"
    if [ -r "$lane_cache" ]; then
      read -r lane_base < "$lane_cache"
    else
      if [ -e "$BASE_TREE" ]; then git worktree remove --force "$BASE_TREE" >/dev/null 2>&1; fi
      if git worktree add --detach --quiet "$BASE_TREE" "$BASE_SHA" 2>/dev/null          && cp -r "$WORKTREE/bin" "$BASE_TREE/bin" 2>/dev/null          && build_tree "$BASE_TREE" "base" 0 2>/dev/null; then
        run_suite "$BASE_TREE" "$(maxon_shv2_path "$BASE_TREE")" "$LOGDIR/rung-base-$lane.log" "$lane" || true
        lane_base="$DRIFT"
        printf '%s
' "$lane_base" > "$lane_cache"
      else
        lane_base=""
      fi
      cleanup_base
    fi
    run_suite "$WORKTREE" "$SHV2" "$LOGDIR/rung-tip-$lane.log" "$lane" || true
    lane_tip="$DRIFT"
    if [ -n "$lane_base" ]; then
      printf '    %-14s base %-6s tip %-6s delta %s
' "$lane" "$lane_base" "$lane_tip" "$(( lane_tip - lane_base ))"
    else
      printf '    %-14s base UNMEASURED  tip %-6s (could not build the base for this lane)
' "$lane" "$lane_tip"
    fi
  done
  echo "    ⚠ a non-zero delta here is NOT a gate and NOT a failure — it is a lane this rung moved"
  echo "      that the host delta cannot see. Say it in the rung report."
  echo
fi

DRIFT_DELTA=$(( TIP_DRIFT - BASE_DRIFT ))
MODIFIED_GOLDENS="$(git -C "$WORKTREE" diff --name-status "$BASE_SHA...$TIP" -- specs-shv2/fragments/ | grep -c '^M' || true)"

CODEGEN_MOVED=0
[ "$DRIFT_DELTA" = "0" ] || CODEGEN_MOVED=1
[ "$MODIFIED_GOLDENS" = "0" ] || CODEGEN_MOVED=1

if [ "$CODEGEN_MOVED" = "0" ]; then
  ok "drift delta 0 and no golden modified ⇒ this rung emitted BYTE-IDENTICAL code"
else
  warn "CODEGEN MOVED: drift delta $DRIFT_DELTA (base $BASE_DRIFT → tip $TIP_DRIFT), $MODIFIED_GOLDENS golden(s) modified"
  [ -n "$CODEGEN_NOTE" ] || die "a codegen change needs an EXPLANATION, not a shrug. Pass
     --codegen-note-file with what moved and why. ⚠ This is not a failure — 'is this change intended'
     is not a question a machine can answer, which is exactly why you have to answer it.
     ⚠ It also means the arm64 goldens are now OWED A MINT. They can only be minted by the lane that
       emits them, so that happens at the periodic remote sync, NOT here — say so in the rung report."
  ok "explained: $(head -c 160 "$CODEGEN_NOTE" | tr '\n' ' ')…"
fi

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "6/10  The ladder read, and the C# lane"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
# The scale-test READ is per rung and never batches — not out of thoroughness, but because ATTRIBUTION
# IS ONLY AVAILABLE NOW. The instrument sees exactly WHAT moved and can never see why; ten rungs later,
# neither can you. The row in docs/optimization-log.md is the deliverable.
if echo "$CHANGED_FILES" | grep -c '^docs/optimization-log\.md$' >/dev/null; then
  ok "docs/optimization-log.md carries a row from this rung"
elif [ -n "$NO_LADDER" ]; then
  warn "no ladder row — stated reason: $NO_LADDER"
else
  die "this rung wrote NO ROW into docs/optimization-log.md.
     Run the ladder (~17 s), READ it, and record what moved and WHY. The memory columns are exact and
     bit-for-bit reproducible, so any movement is real; the CPU column moves only outside its noise
     band. If the rung genuinely changed no pass, no IR and no indexed data structure, say so with
     --no-ladder-row \"<reason>\" and the reason goes into the report."
fi

if [ "$RUN_CSHARP" = "1" ]; then
  echo "  running the C# bootstrap suite…"
  CS_LOG="$LOGDIR/rung-csharp-suite.log"
  ( cd "$WORKTREE" && "$(maxon_bootstrap_path .)" spec-test ) > "$CS_LOG" 2>&1
  CS_EXIT=$?
  CS_FAILED="$(grep -c '\[FAIL\]' "$CS_LOG")"
  [ "$CS_EXIT" = "0" ] && [ "$CS_FAILED" = "0" ] || {
    grep -n '\[FAIL\]' "$CS_LOG" | head -30
    die "the C# suite is RED ($CS_FAILED [FAIL] markers, exit $CS_EXIT) — see $CS_LOG"; }
  ok "C# suite green"

  # Codegen neutrality for a bootstrap change is asked of BOTH spec trees: the bootstrap mints the
  # goldens under specs/ and shv2 is built by it.
  CS_DIRTY="$(git -C "$WORKTREE" status --short specs/ specs-shv2/)"
  if [ -n "$CS_DIRTY" ]; then
    echo "$CS_DIRTY" | head -20
    [ -n "$CODEGEN_NOTE" ] || die "the C# suite moved goldens and nothing explains it — pass --codegen-note-file"
    warn "the C# lane moved goldens; they are covered by your codegen note and must be COMMITTED"
  else
    ok "C# codegen neutrality: git status over specs/ and specs-shv2/ is empty"
  fi
fi

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "7/10  The CROSS-TARGET gate — every LOCAL target"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
# Step 4 proved the rung on exactly ONE target: whichever one this host happens to be. A green suite on
# one target is evidence about one target — that is how 317 stale arm64 goldens sat on main unnoticed
# through a run of rungs that were all green on x64-windows.
#
# ⛔ The REMOTE arm64 lanes are NOT in this gate and are NEVER required to complete a rung. They print
#    as SKIP with the reason, and an unverified lane is a REPORTED SKIP — never folded into the green,
#    and never a residual that holds the rung at ◑.
if [ -n "$NO_CROSS" ]; then
  warn "cross-target gate SKIPPED — stated reason: $NO_CROSS"
  warn "  ⇒ this rung is verified on the HOST LANE ONLY. Say so in the report."
  CROSS_SUMMARY="SKIPPED — $NO_CROSS"
else
  CROSS_LOG="$LOGDIR/rung-cross-target.log"
  CROSS_ARGS="--skip-build --skip-host"
  [ "$RUN_CSHARP" = "0" ] || CROSS_ARGS="$CROSS_ARGS --csharp"
  echo "  scripts/cross-target-gate.sh $CROSS_ARGS   (--skip-build/--skip-host cost no coverage: the"
  echo "  first REFUSES if any source is newer than its binary, the second prints the host as PRIOR)"
  # shellcheck disable=SC2086
  ( cd "$WORKTREE" && bash scripts/cross-target-gate.sh $CROSS_ARGS ) > "$CROSS_LOG" 2>&1
  CROSS_EXIT=$?
  tail -20 "$CROSS_LOG"
  [ "$CROSS_EXIT" = "0" ] || die "a target RAN AND FAILED — that is a rung-halting red gate, exactly
     like any other. See $CROSS_LOG. ⚠ If two suites overlapped in one tree, re-run that lane ALONE
     before believing it: a shared .spec-tmp has produced a FALSE RED before. No flag softens a real
     one, and you never turn it green by dropping a target."
  # ⚠ MATCH THE MATRIX ROWS, NOT EVERY `PASS` IN THE FILE. The first version counted any line starting
  #   with PASS/FAIL/SKIP/PRIOR and reported "7548 lane rows" on BATCH14 — it had swept up every
  #   per-test PASS line the three suites print. A number that large is obviously wrong, which is the
  #   only reason it was harmless; a plausible wrong number in a rung report is not. The matrix rows are
  #   `<arch>-<os>` followed by a verdict, so anchor on the target name.
  CROSS_SUMMARY="$(grep -cE '^[a-z0-9_]+-[a-z0-9_]+[[:space:]]+(PASS|FAIL|SKIP|PRIOR)([[:space:]]|$)' "$CROSS_LOG" | tr -d ' ') target lane(s) — see $CROSS_LOG"
  ok "cross-target gate passed (arm64 lanes SKIPPED — remote, UNVERIFIED, and not required)"
fi

if [ "$DRY_RUN" = "1" ]; then
  printf '\n\033[32m✓ DRY RUN CLEAN\033[0m — every gate passed. Nothing merged, written or pushed.\n'
  echo "  suite   $TIP_PASSED passed, 0 failed"
  echo "  drift   base $BASE_DRIFT → tip $TIP_DRIFT (delta $DRIFT_DELTA)"
  echo "  cross   ${CROSS_SUMMARY:-ran}"
  exit 0
fi

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "8/10  Land it — linear history"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
if [ -n "$(git status --porcelain)" ]; then
  git stash push -u --quiet -m "rung-finish: $BATCH" || die "git stash failed"
  STASHED=1
fi

git merge --ff-only --quiet origin/main || die "main cannot fast-forward to origin/main — resolve by hand"
git merge --ff-only --quiet "$BRANCH" || die "FAST-FORWARD MERGE REFUSED. The --ff-only above is passed
     EXPLICITLY, so this errors rather than making a merge commit whatever the config says. The branch
     must sit directly on top of main — re-run, which rebases it."
ok "main fast-forwarded to $BRANCH ($(git log --oneline -1 --format=%h))"

# ⭐ THIS RUNG'S TIP IS THE NEXT RUNG'S BASE, and it was measured minutes ago on this exact tree. Write
#    it into the cache so the next finish subtracts for free instead of rebuilding main to re-derive a
#    number this run already holds.
printf '%s %s %s\n' "$TIP_DRIFT" "$TIP_PASSED" "$TIP_FAILED" > "$LOGDIR/rung-drift-$(git rev-parse HEAD).txt"

restore_stash

# ⛔ **A COMMIT CONTAINING CONFLICT MARKERS MUST NOT BE REACHABLE FROM A GREEN CLOSURE.**
# `restore_stash` only WARNS when the pop conflicts, and everything below — the flip, the commit, the
# push — runs anyway. MEASURED 2026-08-05 (BATCH23): another agent added a row to the same board table
# while the rung was gating, the pop conflicted, and the script reported `RUNG LANDED` with exit 0
# having committed AND PUSHED three `<<<<<<<` markers, a duplicated batch row and statuses it believed
# it had flipped. The gates were all green; none of them looks at the file the closure is ABOUT.
#
# Resolving the pop is a judgement call — the upstream side may carry another agent's live claim that
# must survive, so this cannot be automated — but shipping the markers is never right.
# ⚠ Matched as a PREFIX, not an exact line: git writes `<<<<<<< Updated upstream` and
# `>>>>>>> Stashed changes` with a suffix, and only `=======` is ever bare — so an anchored `$` would
# have caught this conflict solely by way of that one line, and missed a diff3-style one entirely.
if grep -qE '^(<<<<<<<|>>>>>>>|\|\|\|\|\|\|\||=======)' "$REPO/maxon-shv2/PLAN.md" 2>/dev/null; then
  die "maxon-shv2/PLAN.md contains CONFLICT MARKERS — the stash pop above did not clean up.
   Nothing has been flipped, committed or pushed. Resolve PLAN.md by hand in $REPO, keeping BOTH
   sides' rows (the upstream side may hold another agent's live claim — deleting it is worse than
   any conflict), then re-run. Your detail rows are still in \`git stash list\` if you need them."
fi

if [ "$WAVE" = "0" ]; then
  # ⭐ THE FLIP IS MECHANICAL AND THIS IS WHERE IT HAPPENS — including every member row. A finished
  #    slice whose row was never flipped blocks its whole lane exactly as effectively as an abandoned
  #    one, and a member left at 🔶 is the same failure one level down.
  OWNER="$(row_owner "$PLAN" "$BATCH")"; CLAIMED="$(row_claimed "$PLAN" "$BATCH")"

  FLIP="$BATCH
$(row_members "$PLAN" "$BATCH")"
  while IFS= read -r id; do
    [ -n "$id" ] || continue
    ln="$(row_line_no_soft "$PLAN" "$id")"
    [ -n "$ln" ] || continue
    st="$(row_status "$PLAN" "$id")"
    case "$st" in
      *🔶*) set_row_tail "$PLAN" "$ln" "✅ DONE" "$OWNER" "$CLAIMED"
            echo "  PLAN.md:$ln  $id  🔶 CLAIMED → ✅ DONE" ;;
      *✅*) ;;
      # A member that is neither claimed nor done was dropped from the batch mid-rung, or never
      # claimed with it. Either way it is not this rung's to close — the coordinator releases it back
      # to ⬜ FREE in the detail rows, and says why.
      # ⚠ TRUNCATED: a status cell can be a paragraph. G14's release note ran ~1,500 characters and
      #   buried the three flips either side of it in the BATCH14 closure output.
      *)    warn "  $id (line $ln) reads '$(printf '%s' "$st" | cut -c1-60)…' — leaving it alone; say in the detail row why it is not ✅" ;;
    esac
  done <<< "$FLIP"
  ok "board flipped"
fi

# ⭐ THE DETAIL ROW LANDS IN BOTH MODES. Only the board FLIP above is board-specific — the row itself
#    is the rung's deliverable ("a rung that wrote no detail is not closed"), and a WAVE rung that
#    merged its code and silently dropped its PLAN.md row would close having written nothing down.
git add -- maxon-shv2/PLAN.md || die "git add failed"
git commit --quiet -F "$MSG_FILE" || die "git commit failed"
ok "committed $(git log --oneline -1)"

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "9/10  Push"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
if [ "$NO_PUSH" = "1" ]; then
  warn "--no-push: main is ahead locally. Push it yourself — the parallel repo consumes it."
else
  attempt=1
  while : ; do
    if git push --quiet origin main 2>"$LOGDIR/rung-push.log"; then
      ok "pushed $(git log --oneline -1 --format=%h)"
      break
    fi
    [ "$attempt" -lt "$MAX_PUSH_ATTEMPTS" ] || { cat "$LOGDIR/rung-push.log"; die "push rejected $attempt times — land it by hand"; }
    warn "push REJECTED (attempt $attempt) — another agent landed while this battery ran."
    warn "  ⛔ A REJECTED PUSH IS NOT A RETRY. The tree changed, so the suite runs again before it lands."
    attempt=$(( attempt + 1 ))

    git pull --rebase --quiet || die "rebase after a rejected push FAILED — resolve by hand"
    build_tree "$REPO" "merged"
    run_suite "$REPO" "$(maxon_shv2_path "$REPO")" "$LOGDIR/rung-merged-suite.log"
    [ "$SUITE_EXIT" != "101" ] || die "MEMORY LEAK after rebasing onto the new upstream"
    [ "$FAILED" = "0" ] || { grep -n '^FAIL' "$LOGDIR/rung-merged-suite.log" | head -20
      die "your rung and the new upstream do NOT compose: $PASSED passed, $FAILED failed.
     ⚠ If they landed in YOUR lane's file, read their diff — a clean textual rebase is not evidence
       two changes compose, only that they were far apart in the file."; }
    ok "re-verified on the new upstream: $PASSED passed, 0 failed"
  done
fi

if [ "$KEEP_WORKTREE" = "0" ]; then
  # ⛔ THE BRANCH DELETE IS NOT INDEPENDENT OF THE WORKTREE REMOVE, AND IT USED TO SWALLOW ITS OWN
  #    FAILURE. `git branch -D` REFUSES a branch that is checked out in a worktree, so a failed remove
  #    GUARANTEES a failed delete — and the delete ended in `|| true`, so the pair leaked while the
  #    output mentioned only the worktree. Both speak now, and the failure names the whole recovery.
  if git worktree remove --force "$WORKTREE" >/dev/null 2>&1; then
    ok "worktree removed"
    git branch -q -D "$BRANCH" 2>/dev/null && ok "branch $BRANCH deleted" \
      || warn "worktree gone but branch '$BRANCH' SURVIVES — delete it: git branch -D $BRANCH"
  else
    warn "could not remove the worktree — BOTH it and branch '$BRANCH' survive. By hand:"
    warn "    git worktree remove --force $WORKTREE && git branch -D $BRANCH"
  fi

  # ⛔ NO `git push origin --delete` HERE, AND THERE MUST NOT BE ONE. There has never been a remote
  #    branch for it to delete: rung-start.sh stopped pushing one on 2026-08-04 ("BRANCHES NEVER GO TO
  #    ORIGIN", user rule) and documents it in its own step 5. The call that used to sit here was a
  #    network round-trip that could only ever fail, with the failure sent to /dev/null and `|| true` —
  #    while its `ok "remote branch deleted"` could print in exactly ONE case, somebody having violated
  #    the rule, which is the one case worth SEEING rather than quietly tidying away.
fi

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "10/10  Reap LEAKED rung worktrees — the debris nothing has ever removed"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
# ⭐ WHY THIS EXISTS. The teardown above removes the worktree of the rung that RAN this script. Nothing
#    has ever removed the worktree of a rung that DIDN'T, and there are two routine ways to be one:
#
#      · a rung merged BY HAND — into a feature branch, say — never invokes this script at all;
#      · a rung whose battery died after the push but before the teardown.
#
#    MEASURED 2026-08-09: 33 worktrees on this checkout at ~570 MB each, of which 7 were leaked rung
#    trees. Every leaked path also BLOCKS ITS OWN SLUG — rung-start.sh refuses a worktree path that
#    already exists ("A rung is live there, or one was never torn down") — so the debris is not merely
#    disk, it is a name nobody can reuse. Cleanup nobody schedules never happens; a rung finishing is
#    the one moment it is both cheap and provably safe.
#
# ⛔ IT REMOVES ONLY WHAT IT CAN PROVE IS INERT, AND IT PRINTS EVERY ONE IT DECLINES, WITH THE REASON.
#    A reaper that reported only its successes would read as "nothing left behind" while leaving
#    plenty — the silent-cap failure this repo keeps paying for. FIVE conditions, each excluding one
#    distinct way of being wrong:
#
#      1. the path is a `maxon-*` SIBLING of this checkout      ⇒ rung-start.sh:82 made it. The agent
#         harness's own `.claude/worktrees/` trees are NOT this script's to judge, and it says so.
#      2. `git status --porcelain` is empty                     ⇒ no uncommitted work to lose.
#      3. NOTHING HAS WRITTEN TO IT for REAP_IDLE_MINUTES       ⇒ no agent is standing in it.
#      4. zero commits that are not already in main             ⇒ no committed work to lose either.
#      5. no LIVE 🔶 claim names its branch, AND its tip is
#         STRICTLY OLDER than the base this rung gated against  ⇒ it is not a rung STARTING UP.
#
#    (5) is two tests because neither covers the other. A rung rung-start.sh created minutes ago sits
#    exactly AT origin/main's tip — which is $BASE_SHA at the newest — so "strictly older" excludes it
#    without the board being consulted at all; that is what protects a WAVE rung, which has no board
#    row to hold a claim. The board test is what protects a SLICE rung that started BEFORE this one
#    and has not committed yet, whose tip is genuinely older.
#
#    ⭐ AND (3) IS THE ONE THAT IS NOT A READING OF THE COMMIT GRAPH. 1, 4 and 5 all interrogate git,
#    so they can be wrong TOGETHER — which is exactly what happened on 2026-08-09, when all of them
#    cleared two rungs that had been created eight minutes earlier and had agents inside them. A guard
#    drawn from a different instrument is worth more than a fourth guard drawn from the same one.
#
#    It errs in one known direction, stated rather than left to be discovered: a rung that landed with
#    NO other rung between it and this one has a tip EQUAL to $BASE_SHA, so it survives one extra cycle
#    and the next finish takes it. That error costs a directory for one rung. The opposite error costs
#    a live agent its worktree, so the asymmetry is the point.
if [ "$KEEP_WORKTREE" = "1" ]; then
  warn "--keep-worktree: reap skipped along with the teardown — same request, leave the trees alone"
else
  # ⚠ A BOARD IT CANNOT READ MUST NOT READ AS "NO LIVE CLAIMS". An empty result is legitimate — nobody
  #   is mid-rung — and a `die` inside the substitution produces the identical empty string, so the
  #   failure is caught explicitly rather than folded into the benign case. Losing this guard is
  #   survivable (the strictly-older test still stands alone); losing it SILENTLY is not.
  LIVE_CLAIMS="$(live_claim_branches "$PLAN")" || {
    warn "could not read live claims off the board — falling back to the strictly-older test alone"
    LIVE_CLAIMS=""; }
  REPO_WIN="$(win_path "$REPO")"
  MAIN_SHA="$(git rev-parse HEAD)"
  REAPED=0; KEPT=0

  # `git worktree list --porcelain` emits one blank-line-separated block per tree. A detached tree has
  # no `branch` line at all, which is why `br` is cleared per block rather than left to carry over.
  # ⚠ The `IFS=$'\t' read` below is safe ONLY because the possibly-empty field is the LAST one: tab is
  #   IFS whitespace, so bash collapses a RUN of tabs, and an empty field anywhere but the end would
  #   shift every later one left. That exact collapse produced a real bug in plan-board.sh's
  #   `live_claim_branches` and a latent one in `lane_conflicts` — both now split with awk/cut. Do not
  #   add a third field to this record without moving it off `read` as well.
  WT_LIST="$(git worktree list --porcelain | awk '
    /^worktree /{ wt = substr($0, 10); br = "" }
    /^branch /  { br = $2 }
    /^$/        { if (wt != "") print wt "\t" br; wt = "" }
    END         { if (wt != "") print wt "\t" br }')"

  while IFS=$'\t' read -r wt ref; do
    [ -n "$wt" ] || continue
    branch="${ref#refs/heads/}"

    # (1) ours to judge? A `maxon-*` sibling, never the checkout itself and never anything inside it.
    #     Compared through win_path because git prints native absolute paths and $REPO is whatever
    #     the shell's `pwd` spells — on Git Bash those two disagree, and an untested string compare
    #     between them would silently match nothing and reap nothing forever.
    wt_win="$(win_path "$wt")"
    [ "$wt_win" != "$REPO_WIN" ] || continue
    case "$wt_win" in "$REPO_WIN"/*) continue ;; esac
    case "$(basename "$wt_win")" in maxon-*) ;; *) continue ;; esac

    if [ -z "$branch" ]; then
      warn "  KEPT  $wt — detached HEAD, so there is no branch to reason about"; KEPT=$(( KEPT + 1 )); continue
    fi
    if [ -n "$(git -C "$wt" status --porcelain 2>/dev/null)" ]; then
      warn "  KEPT  $wt [$branch] — UNCOMMITTED work in it"; KEPT=$(( KEPT + 1 )); continue
    fi

    # ⛔ GIT STATE CANNOT SEE A LIVE AGENT, and this guard was added because the git-only version very
    #    nearly deleted two live rungs. MEASURED 2026-08-09: a cleanup sweep listed
    #    `rung-forin-existential` and `rung-opaque-element-ownership` as removable — both CLEAN, both
    #    fully merged, both created EIGHT MINUTES EARLIER with agents working in them right then. A
    #    tree being actively built in is clean and merged right up until the moment it is not, so
    #    "clean + merged" describes a rung STARTING UP and a rung FINISHED equally well.
    #
    #    Recent WRITES are the one signal git has no opinion about, and they are independent of every
    #    test around them — which is the point: the tests below can all be wrong together (they are
    #    three readings of the same commit graph), and this one still fires.
    newest=0
    for probe in "$wt/bin" "$wt/maxon-shv2/.maxon" "$wt/temp" "$wt/maxon-sharp/bin" "$wt"; do
      [ -e "$probe" ] || continue
      m="$(stat -c %Y "$probe" 2>/dev/null || echo 0)"
      [ "$m" -gt "$newest" ] && newest="$m"
    done
    idle_min=$(( ( $(date +%s) - newest ) / 60 ))
    if [ "$idle_min" -lt "$REAP_IDLE_MINUTES" ]; then
      warn "  KEPT  $wt [$branch] — written to $idle_min min ago; something may be working in it"
      KEPT=$(( KEPT + 1 )); continue
    fi
    unique="$(git rev-list --count "refs/heads/$branch" "^$MAIN_SHA" 2>/dev/null || echo unknown)"
    if [ "$unique" != "0" ]; then
      warn "  KEPT  $wt [$branch] — $unique commit(s) NOT in main; this is unmerged work"; KEPT=$(( KEPT + 1 )); continue
    fi
    if printf '%s\n' "$LIVE_CLAIMS" | grep -qxF "$branch"; then
      warn "  KEPT  $wt [$branch] — a LIVE 🔶 board claim names it"; KEPT=$(( KEPT + 1 )); continue
    fi
    if ! git merge-base --is-ancestor "refs/heads/$branch" "$BASE_SHA" 2>/dev/null \
       || [ "$(git rev-parse "refs/heads/$branch")" = "$BASE_SHA" ]; then
      warn "  KEPT  $wt [$branch] — tip is not strictly older than the base this rung gated ($(git log --oneline -1 --format=%h "$BASE_SHA")); it may be a rung starting up"
      KEPT=$(( KEPT + 1 )); continue
    fi

    if git worktree remove --force "$wt" >/dev/null 2>&1; then
      git branch -q -D "$branch" 2>/dev/null \
        && ok "  reaped $wt [$branch]" \
        || warn "  reaped $wt but branch '$branch' survives — git branch -D $branch"
      REAPED=$(( REAPED + 1 ))
    else
      warn "  KEPT  $wt [$branch] — inert, but \`git worktree remove\` FAILED (open handle? locked?)"
      KEPT=$(( KEPT + 1 ))
    fi
  done <<< "$WT_LIST"

  git worktree prune                   # admin entries whose directory somebody already deleted
  [ "$REAPED" = "0" ] && [ "$KEPT" = "0" ] && ok "no leaked rung worktrees" \
    || ok "reaped $REAPED, kept $KEPT (each kept one printed its reason above)"
fi

printf '\n\033[32m✓ RUNG LANDED\033[0m\n\n'
cat <<EOF
  row       $BATCH$( [ "$WAVE" = "1" ] && printf '  (WAVE mode — no board row)' )
  suite     $TIP_PASSED passed, 0 failed
  drift     base $BASE_DRIFT → tip $TIP_DRIFT  (delta $DRIFT_DELTA, $MODIFIED_GOLDENS golden(s) modified)
  cross     ${CROSS_SUMMARY:-ran}

  FOR THE RUNG REPORT — say these out loud, because a skip folded into the green is the one failure
  this battery cannot catch for you:
    · arm64-macos / arm64-linux: SKIP — remote, UNVERIFIED. Not required, owed by the periodic sync.$( [ "$CODEGEN_MOVED" = "1" ] && printf '\n    · codegen MOVED ⇒ the arm64 goldens are OWED A MINT at that sync.' )
    · every LOCAL target that ran, and every one that did not.
EOF
