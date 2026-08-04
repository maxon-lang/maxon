#!/usr/bin/env bash
#
# THE /rung FINISH LINE — the gate battery, the cross-target gate, the landing and the board closure,
# as one command. It is `.claude/skills/rung/SKILL.md` §8; read `reference/gates.md` for what a red one
# MEANS, because this script can tell you a gate failed and never why it matters.
#
#   rebase the branch -> build -> suite -> GATES -> BASE-DRIFT A/B -> cross-target -> ff-merge
#     -> flip the board -> commit -> push -> tear down the worktree
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
    -h|--help)           sed -n '2,62p' "${BASH_SOURCE[0]}"; exit 0 ;;
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
step "1/9  Position — the branch, the worktree, and the DETAIL ROW you owe"
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
step "2/9  Rebase the branch onto origin/main — BEFORE the suite, not after"
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
echo "$CHANGED_FILES" | grep -qE '^(maxon-sharp|stdlib)/' && RUN_CSHARP=1
[ "$RUN_CSHARP" = "0" ] || ok "the branch touched maxon-sharp/ or stdlib/ — the C# suite is now part of this battery"

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "3/9  Build"
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

build_tree "$WORKTREE" "tip"
ok "shv2 built from the rebased branch"

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "4/9  The full unfiltered shv2 suite"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
# ⚠ REDIRECTED, never piped. A pipe decides what to keep BEFORE you know what failed, so a red run
#   costs a second full run just to read what the first one already had.
run_suite() {                        # run_suite <tree> <binary> <log> -> sets PASSED FAILED DRIFT
  local tree="$1" bin="$2" log="$3"
  ( cd "$tree" && "$bin" spec-test ) > "$log" 2>&1
  SUITE_EXIT=$?
  local summary
  summary="$(grep -oE '^[0-9]+ passed, [0-9]+ failed' "$log" | tail -1)"
  [ -n "$summary" ] || { tail -40 "$log"; die "could not parse a summary from the suite — see $log"; }
  PASSED="$(echo "$summary" | awk '{print $1}')"
  FAILED="$(echo "$summary" | awk '{print $3}')"
  # SILENT when nothing drifted, which is the normal case — so "no note" means zero.
  DRIFT="$(grep -oE 'note: [0-9]+ golden fragment' "$log" | grep -oE '[0-9]+' | tail -1)"
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

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "5/9  The golden-drift DELTA against the merge base"
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
step "6/9  The ladder read, and the C# lane"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
# The scale-test READ is per rung and never batches — not out of thoroughness, but because ATTRIBUTION
# IS ONLY AVAILABLE NOW. The instrument sees exactly WHAT moved and can never see why; ten rungs later,
# neither can you. The row in docs/optimization-log.md is the deliverable.
if echo "$CHANGED_FILES" | grep -q '^docs/optimization-log\.md$'; then
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
step "7/9  The CROSS-TARGET gate — every LOCAL target"
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
step "8/9  Land it — linear history"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
if [ -n "$(git status --porcelain)" ]; then
  git stash push -u --quiet -m "rung-finish: $BATCH" || die "git stash failed"
  STASHED=1
fi

git merge --ff-only --quiet origin/main || die "main cannot fast-forward to origin/main — resolve by hand"
git merge --ff-only --quiet "$BRANCH" || die "FAST-FORWARD MERGE REFUSED. merge.ff=only is configured,
     so this errors rather than making a merge commit. The branch must sit directly on top of main —
     re-run, which rebases it."
ok "main fast-forwarded to $BRANCH ($(git log --oneline -1 --format=%h))"

# ⭐ THIS RUNG'S TIP IS THE NEXT RUNG'S BASE, and it was measured minutes ago on this exact tree. Write
#    it into the cache so the next finish subtracts for free instead of rebuilding main to re-derive a
#    number this run already holds.
printf '%s %s %s\n' "$TIP_DRIFT" "$TIP_PASSED" "$TIP_FAILED" > "$LOGDIR/rung-drift-$(git rev-parse HEAD).txt"

restore_stash

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
step "9/9  Push"
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
  git worktree remove --force "$WORKTREE" >/dev/null 2>&1 && ok "worktree removed" \
    || warn "could not remove the worktree — remove it by hand: git worktree remove --force $WORKTREE"
  git branch -q -D "$BRANCH" 2>/dev/null && ok "branch $BRANCH deleted" || true
  git push --quiet origin --delete "$BRANCH" 2>/dev/null && ok "remote branch deleted" || true
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
