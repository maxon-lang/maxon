#!/usr/bin/env bash
#
# THE /rung STARTING LINE — the board claim and the worktree isolation, as one command.
# It is `.claude/skills/rung/SKILL.md` §1; `reference/slice-mode.md` is the protocol it implements.
#
#     verify the row  ->  verify the LANES  ->  claim  ->  push  ->  worktree  ->  bin/  ->  build
#
# ⭐ WHY THIS IS A SCRIPT AND NOT A CHECKLIST. Every step below is mechanical, order-sensitive, and has
#    a KNOWN failure that prose did not prevent:
#
#      - two agents filed different rows both called `A2o`, twice in one hour, and a third rename was
#        spent settling it. NOTHING CHECKED THAT A ROW ID WAS UNIQUE.
#      - the board says "AT MOST ONE 🔶 CLAIM PER LANE … it is not advisory", and NOTHING CHECKED IT.
#        The lane is the real exclusion unit: six rows raise refusals inside one 28k-line Parser.maxon.
#      - a worktree cannot check out `main`, so it structurally cannot claim — and the agent that tried
#        found out several steps in.
#      - `bin/` is GITIGNORED, so a fresh worktree has NO COMPILER AT ALL until it is copied in.
#      - a stale binary read `71 FAILED` on a tree that a 13 s rebuild read `1922/0` on.
#
#    None of that is judgement. It is bookkeeping that has to be right, which is what a script is for.
#
# ⭐ THE PUSH IS THE LOCK. A claim exists when, and only when, it is on `origin/main`. If the push is
#    REJECTED this script does NOT force and does NOT blindly retry: it re-fetches, RE-READS the board
#    and re-checks the row and every lane — because losing the race means somebody else's claim is now
#    the truth. ⛔ NEVER `git push --force` on main: a rejection here IS the lock working.
#
# USAGE
#   scripts/rung-start.sh --batch <ID> --slug <slug> --message "<one line>" [--dry-run] [--no-push]
#                         [--wave] [--batch-row-only] [--no-build]
#
#   --batch          board row id, e.g. BATCH14 (or a singleton row, e.g. R10b). In --wave mode it is
#                    just the rung's name, used for the worktree path.
#   --slug           kebab-case; the branch becomes `slice/<ID>-<slug>`
#   --message        one line for the claim commit subject
#   --wave           WAVE mode: no board, no claim, no push. Worktree + build only, branch = <slug>.
#   --batch-row-only claim the BATCH row without flipping its member rows (default flips both, so a
#                    member can never read ⬜ FREE while its batch is claimed)
#   --dry-run        run every check, print the edit, write nothing, create nothing
#   --no-push        claim locally. ⚠ AN UNPUSHED CLAIM IS NOT A CLAIM — the next fetch never sees it.
#   --no-build       set up the worktree but skip the build
#
set -o pipefail

readonly REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly PLAN="$REPO/maxon-shv2/PLAN.md"
readonly LOGDIR="$REPO/temp"
readonly MAX_PUSH_ATTEMPTS=3

# shellcheck source=lib/plan-board.sh
. "$REPO/scripts/lib/plan-board.sh" || { echo "cannot source scripts/lib/plan-board.sh" >&2; exit 1; }
# shellcheck source=lib/host-binaries.sh
. "$REPO/scripts/lib/host-binaries.sh" || { echo "cannot source scripts/lib/host-binaries.sh" >&2; exit 1; }

BATCH=""; SLUG=""; MESSAGE=""; DRY_RUN=0; NO_PUSH=0; WAVE=0; BATCH_ROW_ONLY=0; NO_BUILD=0

while [ $# -gt 0 ]; do
  case "$1" in
    --batch)          BATCH="$2";       shift 2 ;;
    --slug)           SLUG="$2";        shift 2 ;;
    --message)        MESSAGE="$2";     shift 2 ;;
    --wave)           WAVE=1;           shift   ;;
    --batch-row-only) BATCH_ROW_ONLY=1; shift   ;;
    --dry-run)        DRY_RUN=1;        shift   ;;
    --no-push)        NO_PUSH=1;        shift   ;;
    --no-build)       NO_BUILD=1;       shift   ;;
    -h|--help)        sed -n '2,41p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) die "unknown argument: $1" ;;
  esac
done

[ -n "$BATCH" ] || die "--batch is required (the board row id, e.g. BATCH14)"
[ -n "$SLUG" ]  || die "--slug is required (kebab-case, becomes the branch suffix)"
case "$SLUG" in *[!a-z0-9-]*) die "--slug must be kebab-case [a-z0-9-]: '$SLUG'" ;; esac
if [ "$WAVE" = "0" ] && [ "$DRY_RUN" = "0" ]; then
  [ -n "$MESSAGE" ] || die "--message is required (one line for the claim commit)"
fi

mkdir -p "$LOGDIR"
cd "$REPO" || die "cannot cd to $REPO"

if [ "$WAVE" = "1" ]; then BRANCH="$SLUG"; else BRANCH="slice/$BATCH-$SLUG"; fi
WORKTREE="$(cd .. && pwd)/maxon-$(echo "$BATCH" | tr '[:upper:]' '[:lower:]')"

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "1/6  Position"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
BRANCH_HERE="$(git rev-parse --abbrev-ref HEAD)"
if [ "$BRANCH_HERE" != "main" ]; then
  die "this checkout is on '$BRANCH_HERE', not main.
     A CLAIM MUST COME FROM A CHECKOUT THAT OWNS \`main\`. Git allows one checkout of a branch per
     clone, so a worktree cannot check main out and structurally cannot claim (verified 2026-07-29).
     Run this in the primary checkout, or in your own clone. If you were spawned INTO a worktree, your
     launcher owed you a pre-claimed row — stop and say so rather than improvising a claim."
fi

if [ "$WAVE" = "0" ] && [ -n "$(git status --porcelain)" ]; then
  git status --short | head -20
  if [ "$DRY_RUN" = "1" ]; then
    warn "working tree is dirty — a real run would REFUSE here (the claim commit carries PLAN.md and"
    warn "  nothing else, so it can replay cleanly over any other agent's claim). Checking on anyway."
  else
    die "working tree is dirty. THE CLAIM COMMIT CARRIES PLAN.md AND NOTHING ELSE — it has to replay
     cleanly over any other agent's claim, every time, without thought. Commit or stash first."
  fi
fi

# A dry run reads; it does not move your branch. Only the real path rebases.
git fetch --quiet origin || die "git fetch failed"
if [ "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)" ]; then
  ok "already at origin/main ($(git log --oneline -1 --format=%h))"
elif [ "$DRY_RUN" = "1" ]; then
  warn "local main is $(git rev-list --count HEAD..origin/main) commit(s) behind origin/main — a real"
  warn "  run would rebase first, because you must NEVER claim against a stale board."
else
  git pull --rebase --quiet || die "rebase onto origin/main failed — resolve by hand, then re-run"
  ok "rebased onto origin/main ($(git log --oneline -1 --format=%h)) — never claim against a stale board"
fi

if [ "$DRY_RUN" = "0" ]; then
  [ -e "$WORKTREE" ] && die "worktree path already exists: $WORKTREE
     A rung is live there, or one was never torn down — check \`git worktree list\`."
  git show-ref --verify --quiet "refs/heads/$BRANCH" && die "branch '$BRANCH' already exists locally"
fi

# ─────────────────────────────────────────────────────────────────────────────────────────────────
if [ "$WAVE" = "1" ]; then
  step "2/6  Board — SKIPPED (--wave: you are the only coordinator, nobody else is touching main)"
  MEMBERS=""
else
  step "2/6  The row, and the LANES it claims"

  LINE_NO="$(row_line_no "$PLAN" "$BATCH")" || exit 1
  STATUS="$(row_status "$PLAN" "$BATCH")"

  if row_is_free "$STATUS"; then
    ok "row $BATCH is ⬜ FREE (PLAN.md:$LINE_NO)"
    # ⚠ A STATUS CELL THAT IS ALSO PROSE. BATCH10's holds a 300-character release note explaining why
    #   it was given back and what to reclaim it after. Claiming overwrites that cell, so refuse
    #   rather than delete it: a release note is HISTORY and belongs in the row's own prose cell,
    #   where the next reader finds it. The status column is a status.
    if [ "$STATUS" != "⬜ FREE" ]; then
      echo
      echo "  $STATUS" | fold -sw 96 | sed 's/^/     /'
      echo
      die "row $BATCH is free, but its STATUS CELL CARRIES PROSE (above) — and claiming it would
     silently delete that. Move the note into the row's own description cell first, leave the status
     cell reading exactly '⬜ FREE', and re-run. The note is somebody's finding; the status column is
     a status, and one fact written in two columns is what this board keeps paying for."
    fi
  else
    die "row $BATCH is not free — its status cell reads:
       $(printf '%s' "$STATUS" | cut -c1-200)
     A 🔶 means somebody is on it. If that claim looks STALE, releasing it is allowed — but it is an
     edit that gets PUSHED like any other: move it to ⬜ FREE naming the claim you released and why,
     push, and only THEN claim it. Never just take it — the previous agent may be mid-rebase, and two
     live branches for one row is the one state this board cannot represent.
     ⚠ JUDGING STALENESS IS NOW A HUMAN CALL. This message used to say 'and \`git ls-remote --heads
     origin slice/$BATCH-*\` finds no branch', but BRANCHES NEVER GO TO ORIGIN (user rule 2026-08-04),
     so that query answers NO BRANCH for a live rung and stale alike. Go by the claim TIMESTAMP in the
     row, and ask. An absent remote branch is evidence of nothing."
  fi

  LANES="$(row_lanes "$PLAN" "$BATCH")"
  if [ -z "$LANES" ]; then
    warn "this row names no lane. Normal for a singleton; for a BATCH it means the lane cell is empty,"
    warn "  and a batch claims EVERY lane it lists. Check the row before proceeding."
  else
    echo "  lanes claimed: $(echo "$LANES" | tr '\n' ' ')"
  fi

  CONFLICT="$(lane_conflicts "$PLAN" "$BATCH" "$LANES")"
  [ -z "$CONFLICT" ] || die "LANE COLLISION — a live claim already holds a lane this row needs:
     $(echo "$CONFLICT" | sed 's/^/     /')
     AT MOST ONE 🔶 CLAIM PER LANE. That is the concurrency limit and it is not advisory: a second
     claim in a live lane is the failure this board exists to prevent. Take a row in a free lane."
  ok "no live claim holds any of these lanes"

  MEMBERS="$(row_members "$PLAN" "$BATCH")"
  [ -z "$MEMBERS" ] || echo "  members: $(echo "$MEMBERS" | tr '\n' ' ')"
fi

# ─────────────────────────────────────────────────────────────────────────────────────────────────
if [ "$WAVE" = "1" ]; then
  step "3/6  Claim — SKIPPED (--wave)"
  step "4/6  Push — SKIPPED (--wave)"
else
  step "3/6  Claim"

  UTC="$(date -u +%Y-%m-%dT%H:%MZ)"

  # The batch row, plus every member unless --batch-row-only. A member left reading ⬜ FREE while its
  # batch is claimed invites a second claim — BATCH10 shipped exactly that, and its members read FREE
  # for the whole of its life.
  TARGETS="$BATCH"
  [ "$BATCH_ROW_ONLY" = "1" ] || TARGETS="$BATCH
$MEMBERS"

  EDIT_LINES=""
  while IFS= read -r id; do
    [ -n "$id" ] || continue
    ln="$(row_line_no_soft "$PLAN" "$id")"
    if [ -z "$ln" ]; then
      warn "member '$id' has no unique row of its own — leaving it alone"
      continue
    fi
    st="$(row_status "$PLAN" "$id")"
    if [ "$id" != "$BATCH" ] && ! row_is_free "$st"; then
      warn "member '$id' (line $ln) reads '$(printf '%s' "$st" | cut -c1-60)', not free — leaving it alone"
      continue
    fi
    echo "  PLAN.md:$ln  $id  ⬜ FREE → 🔶 CLAIMED"
    EDIT_LINES="$EDIT_LINES$ln
"
  done <<< "$TARGETS"
  [ -n "$EDIT_LINES" ] || die "nothing to edit — refusing to make an empty claim commit"

  if [ "$DRY_RUN" = "1" ]; then
    warn "DRY RUN — nothing written, nothing pushed, no worktree created."
    echo "  the tail each of those rows would get:  | 🔶 CLAIMED | \`$BRANCH\` | $UTC |"
    printf '\n\033[32m✓ DRY RUN CLEAN\033[0m — every check passed. Re-run without --dry-run to claim.\n'
    exit 0
  fi

  # Descending, so an earlier edit can never move a later line number.
  while IFS= read -r ln; do
    [ -n "$ln" ] || continue
    set_row_tail "$PLAN" "$ln" "🔶 CLAIMED" "\`$BRANCH\`" "$UTC"
  done <<< "$(echo "$EDIT_LINES" | grep -v '^$' | sort -rn)"

  [ -n "$(git diff --name-only -- maxon-shv2/PLAN.md)" ] || die "PLAN.md unchanged — refusing to commit nothing"
  ok "PLAN.md edited ($(git diff --numstat -- maxon-shv2/PLAN.md | awk '{print $1" insertions, "$2" deletions"}'))"

  git add -- maxon-shv2/PLAN.md || die "git add failed"
  OTHER="$(git diff --cached --name-only | grep -v '^maxon-shv2/PLAN\.md$')"
  [ -z "$OTHER" ] || die "something other than PLAN.md is staged: $OTHER"
  git commit --quiet -m "claim(slice): $BATCH — $MESSAGE" || die "git commit failed"
  ok "committed $(git log --oneline -1)"

  step "4/6  Push — THE PUSH IS THE LOCK"
  if [ "$NO_PUSH" = "1" ]; then
    warn "--no-push: your claim is LOCAL ONLY, which is not a claim at all — it is a private intention"
    warn "  the next agent's fetch will never see. Push it before you start work."
  else
    attempt=1
    while : ; do
      if git push --quiet origin main 2>"$LOGDIR/rung-start-push.log"; then
        ok "claim pushed: $(git log --oneline -1 --format=%H)"
        echo "     ⇒ ANNOUNCE THIS SHA. It is the claim's evidence — what another agent can verify,"
        echo "       and what you point at if the board and reality ever disagree."
        break
      fi
      [ "$attempt" -lt "$MAX_PUSH_ATTEMPTS" ] || { cat "$LOGDIR/rung-start-push.log"; die "push rejected $attempt times — read the board by hand"; }
      warn "push REJECTED (attempt $attempt) — somebody claimed between the fetch and the push."
      warn "  Re-reading the board. NEVER forcing: a rejection here is the lock working, and forcing"
      warn "  past it deletes a claim another agent is already building against."
      attempt=$(( attempt + 1 ))

      git reset --hard --quiet HEAD~1 || die "cannot drop the local claim commit"
      git pull --rebase --quiet       || die "rebase failed after a rejected push"

      row_is_free "$(row_status "$PLAN" "$BATCH")" || die "you lost the race: row $BATCH now reads
       '$(row_status "$PLAN" "$BATCH" | cut -c1-160)'
     Pick a different row."
      CONFLICT="$(lane_conflicts "$PLAN" "$BATCH" "$(row_lanes "$PLAN" "$BATCH")")"
      [ -z "$CONFLICT" ] || die "you lost the LANE: $CONFLICT
     The row is still free but its lane is not. Pick a different row."
      die "the row and its lanes are still free, but the board MOVED under this script. Re-run it —
     the checks have to be made against the board you are actually claiming on, not one read a
     minute ago."
    done
  fi
fi

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "5/6  Worktree"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
git worktree add --quiet "$WORKTREE" -b "$BRANCH" || die "git worktree add failed"
ok "worktree $WORKTREE on branch $BRANCH"

# `bin/` is GITIGNORED, so the worktree has no compiler at all. Nothing else re-creates it: not a
# checkout, not a rebase, not `git worktree add`.
if [ -d "$REPO/bin" ]; then
  cp -r "$REPO/bin" "$WORKTREE/bin" || die "cannot copy bin/ into the worktree"
  ok "copied bin/ — gitignored, so a worktree has no compiler without this"
else
  warn "no bin/ in the main checkout: the bootstrap has never been built here. Build it first —"
  warn "  ( cd maxon-sharp && dotnet build )  — then copy it in."
fi

# ⛔ BRANCHES NEVER GO TO ORIGIN (user rule, 2026-08-04). This step used to
# `git push --set-upstream origin "$BRANCH"`, so every rung left a `slice/*` ref on the remote.
# Only `main` is ever pushed — by `rung-finish.sh`, carrying the merge and the board flip together.
#
# ⚠ WHAT THIS COSTS, STATED RATHER THAN LEFT TO BE DISCOVERED: a slice branch is now LOCAL to the
#   clone that made it, so `git ls-remote --heads origin 'slice/<id>-*'` — which this script's own
#   refusal message and `reference/slice-mode.md` both name as the way to tell a live claim from an
#   abandoned one — can no longer answer. It will report NO branch for a perfectly live rung. There is
#   no automatic replacement: judging a stale 🔶 is back to the claim TIMESTAMP in the row plus asking.
#   Do not read an absent remote branch as evidence of anything.
ok "branch is LOCAL — branches never go to origin; only main is pushed, by rung-finish.sh"

# ─────────────────────────────────────────────────────────────────────────────────────────────────
step "6/6  Build the worktree"
# ─────────────────────────────────────────────────────────────────────────────────────────────────
# ⚠ ALWAYS BUILD. The SUITE is the skippable part; the BUILD never is. Both binaries are gitignored and
#   nothing rebuilds them, and a stale one lies in BOTH directions — measured 2026-07-27 on a clean
#   main, the tree's binary read 71 FAILED where a 13 s rebuild read 1922/0.
if [ "$NO_BUILD" = "1" ]; then
  warn "--no-build: the worktree has NO shv2 binary. Build before you gate anything."
elif [ ! -x "$(maxon_bootstrap_path "$WORKTREE")" ]; then
  warn "no bootstrap in the worktree — skipping the shv2 build"
else
  # ⛔⛔ THE COPIED BOOTSTRAP IS OF UNKNOWN VINTAGE, AND NO mtime TEST CAN SAY OTHERWISE.
  #   `cp -r` above stamps `$WORKTREE/bin/maxon.exe` with NOW, while `git worktree add` has just
  #   written every source with NOW too — so the binary always looks at least as new as the sources
  #   whatever its CONTENT is. It is as old as whenever the MAIN CHECKOUT last built, which may be
  #   many commits back, and nothing in the worktree records that.
  #
  #   Measured 2026-08-10, three times in one session: a worktree cut from a main that already
  #   carried ARR0's `of`-is-not-a-keyword change was seeded with a bootstrap built BEFORE it, so the
  #   C# suite failed `keyword-parameter-names` — a false red attributed to the rung, each one
  #   costing a full ~10-minute `rung-finish` battery to discover and re-run. `rung-finish.sh`'s own
  #   `build_tree` has the same blind spot and is fixed the same way.
  #
  # ⇒ BUILD IT UNCONDITIONALLY. ~65 s, once, against a re-run of everything downstream. The bootstrap
  #   BUILDS shv2, so a stale one also emits an shv2 that does not match its own sources.
  echo "  rebuilding the bootstrap in the worktree (the copied one is of unknown vintage — see above)…"
  ( cd "$WORKTREE/maxon-sharp" && dotnet build --nologo -v q ) > "$LOGDIR/rung-start-bootstrap.log" 2>&1 \
    || { tail -30 "$LOGDIR/rung-start-bootstrap.log"; die "bootstrap build FAILED in the worktree — see $LOGDIR/rung-start-bootstrap.log"; }
  ok "bootstrap rebuilt — the worktree's compiler now matches the worktree's sources"

  echo "  building shv2 in the worktree…"
  ( cd "$WORKTREE" && "$(maxon_bootstrap_path .)" build maxon-shv2 ) > "$LOGDIR/rung-start-build.log" 2>&1 \
    || { tail -30 "$LOGDIR/rung-start-build.log"; die "shv2 build FAILED in the worktree — see $LOGDIR/rung-start-build.log"; }
  ok "shv2 built"
fi

printf '\n\033[32m✓ CLAIMED AND ISOLATED\033[0m\n\n'
cat <<EOF
  row        $BATCH$( [ "$WAVE" = "1" ] && printf '  (WAVE mode — no board claim)' )
  branch     $BRANCH
  worktree   $WORKTREE

  ⚠ EVERY maxon-dev MCP CALL FROM HERE ON NEEDS repoRoot, or it drives the MAIN repo and tells you
    \`success: true\` about a tree containing none of your work:

      repoRoot: "$(win_path "$WORKTREE")"

  NEXT: §2 plan it — survey BOTH references, write the SPEC PORT LIST, and watch it go RED.
        Then the wave, the ladder read, the review, and:

      scripts/rung-finish.sh --batch $BATCH --message-file <f>
EOF
