#!/usr/bin/env bash
#
# READING AND WRITING maxon-shv2/PLAN.md's 🧭 SLICE BOARD — the one parser, sourced by both
# scripts/rung-start.sh (which claims a row) and scripts/rung-finish.sh (which closes one).
#
# ⭐ IT LIVES HERE BECAUSE A SECOND COPY WOULD BE THE PROJECT'S SIGNATURE BUG. The board's format is
#    ONE FACT; a claimer and a closer that each parsed it their own way would eventually disagree
#    about which cell is the status — and the disagreement would surface as a row somebody else has to
#    arbitrate, which is the exact thing the board exists to prevent.
#
# ═══════════════════════════════════════════════════════════════════════════════════════════════════
# WHAT A BOARD ROW IS — three narrowings, and MEASUREMENT forced every one of them
# ═══════════════════════════════════════════════════════════════════════════════════════════════════
#
# (1) ⛔ NOT a file-wide `| **id** |` grep. PLAN.md's own A2n row says so before you try it:
#
#       "the obvious rule is WRONG and I paid for that: a file-wide 'a bolded first table cell is a
#        row id' uniqueness check finds 19 duplicates that are all LEGITIMATE — every ladder rung
#        (P1.0a, P1.2, P1.7a, P2.3, …) appears once in the *Status at a glance* index and once in its
#        detail row, by design."
#
#     It is wrong a second way the row does not mention: those tables are a DIFFERENT SHAPE (5
#     columns), so reading their last three cells as status/owner/claimed hands back a prose note
#     where a date belongs — silently.
#
# (2) ⛔ NOR the board's `## 🧭` SECTION. Measured: that section spans 117–679 and holds 211
#     bolded-first-cell rows, because the LANES table (`| Lane | Owns | Rows |`) and the RULES table
#     live inside it. A three-column lane row has no status cell at all.
#
# (3) ✅ THE ANCHOR IS THE TABLE'S OWN HEADER: a board table is one whose header row ends
#     `| Claimed (UTC) |`. There are exactly two, and its rows are the contiguous `|` lines beneath.
#     The header DECLARES the columns, so deriving from it is the one reading that cannot drift.
#
# ═══════════════════════════════════════════════════════════════════════════════════════════════════
# WHICH END TO COUNT A CELL FROM — and why it is not one rule
# ═══════════════════════════════════════════════════════════════════════════════════════════════════
#
#   The BOARD    | id    | Slice(prose)    | Lane            | Status | Owner | Claimed |
#   The BATCHES  | Batch | Members | Lanes it claims | Why together(prose) | Status | Owner | Claimed |
#
# The tables differ in width and in what the middle means, and AGREE ON THE TAIL. Prose cells contain
# escaped `\|`, which adds fields when splitting on `|` — but only ever BEFORE the tail. So:
#
#   · cells at or after the prose are counted FROM THE END   (status = NF-3, owner = NF-2, …)
#   · cells before the prose are counted FROM THE LEFT       (id = $2, batch members = $3)
#   · the Lane cell sits on OPPOSITE sides in the two tables: $4 in BATCHES, NF-4 in the BOARD.
#
# ⚠ AND THE SPLIT IS VALIDATED, because a silent mis-parse is the worst thing this file could do — it
#   would read the wrong cell as the status and answer confidently that a claimed row is free. The
#   check is the one cell whose shape is known: `Claimed (UTC)` is a date, a dash, or empty.
#
# ═══════════════════════════════════════════════════════════════════════════════════════════════════
# ⚠ ONE AWK PASS, CACHED — this used to be a function per cell and it was UNUSABLE
#
#   Measured on this board: 203 rows × ~4 `sed`/`awk` subshells per row is 800+ process spawns, which
#   on Git Bash does not finish inside 30 s. A claim step that takes a minute to read a table gets
#   skipped, and a check that gets skipped is not a check. So the whole board is scanned ONCE into
#   $_BOARD_CACHE and every query below is shell string-matching over it.
# ═══════════════════════════════════════════════════════════════════════════════════════════════════

# ── message helpers ──────────────────────────────────────────────────────────────────────────────
die()  { printf '\n\033[31m✗ %s\033[0m\n' "$*" >&2; exit 1; }
ok()   { printf '\033[32m✓\033[0m %s\n' "$*"; }
step() { printf '\n\033[1m── %s\033[0m\n' "$*"; }
warn() { printf '\033[33m⚠\033[0m %s\n' "$*"; }

# win_path <path> — the path as the maxon-dev MCP tools need it: ABSOLUTE and Windows-style.
#
# ⚠ FOUND ON THE FIRST LIVE RUN (BATCH14, 2026-08-03). Git Bash's `pwd` returns an MSYS path
# (`/c/Users/Eric/Dev/maxon-batch14`), and the MCP **REFUSES** that: it takes absolute Windows paths
# only, and a relative one would resolve against the SERVER's working directory rather than yours.
# So the worktree brief was printing a `repoRoot` that cannot work — which is the very failure the
# `repoRoot` parameter exists to prevent, reproduced one level up in the tool that hands it over.
# `pwd -W` is Git Bash's own conversion; the sed fallback covers shells that lack it.
win_path() {
  local p="$1"
  if [ -d "$p" ]; then ( cd "$p" && pwd -W 2>/dev/null ) && return 0; fi
  printf '%s\n' "$p" | sed -E 's|^/([a-zA-Z])/|\1:/|'
}

# ── the scan ─────────────────────────────────────────────────────────────────────────────────────
# One TAB-separated record per board row:
#     line \t id \t status \t lanes(comma) \t members(comma) \t owner \t claimed
_BOARD_CACHE=""
_BOARD_CACHE_FOR=""

board_scan() {                       # board_scan <plan> — populate the cache, or die
  local plan="$1"
  [ "$_BOARD_CACHE_FOR" = "$plan" ] && [ -n "$_BOARD_CACHE" ] && return 0
  _BOARD_CACHE="$(awk -F'|' '
    function trim(s) { gsub(/^[ \t]+|[ \t]+$/, "", s); gsub(/\t/, " ", s); return s }

    # A board table opens with a header ending `| Claimed (UTC) |`. Which table it is decides where
    # the Lane cell sits, and the header is what says so.
    /^\|.*\| *Claimed \(UTC\) *\|[ \t]*$/ { intable = 1; isbatch = ($2 ~ /Batch/); next }
    !intable                              { next }
    /^\|[ \t]*-+/                         { next }          # the |---|---| separator
    !/^\|/                                { intable = 0; next }   # first non-row line closes it
    !/^\| \*\*/                           { next }          # a continuation or note row, not a row

    {
      id      = trim($2); gsub(/\*/, "", id)
      status  = trim($(NF-3))
      owner   = trim($(NF-2))
      claimed = trim($(NF-1))

      # ⚠ VALIDATE THE SPLIT. `Claimed (UTC)` is a date, a dash, or empty — anything else means a
      #   literal `|` got into the last three cells and every index here is off by one.
      if (claimed != "" && claimed != "—" && claimed != "-" && claimed !~ /^[0-9][0-9][0-9][0-9]-[0-9][0-9]-/) {
        printf "BADROW\t%d\t%s\t%s\n", NR, id, substr(claimed, 1, 80)
        next
      }

      lanes   = isbatch ? trim($4) : trim($(NF-4))
      members = isbatch ? trim($3) : ""
      gsub(/~~[^~]*~~/, "", members)              # a struck member was dropped from the batch

      printf "%d\t%s\t%s\t%s\t%s\t%s\t%s\n", NR, id, status, lanes, members, owner, claimed
    }
  ' "$plan")"

  [ -n "$_BOARD_CACHE" ] || die "found no board table in $plan.
     A board table's header ends '| Claimed (UTC) |'. Has the board's column layout changed?"

  local bad
  bad="$(printf '%s\n' "$_BOARD_CACHE" | grep '^BADROW' | head -3)"
  [ -z "$bad" ] || die "cannot trust the cell split on $(printf '%s\n' "$_BOARD_CACHE" | grep -c '^BADROW') row(s):
$(printf '%s\n' "$bad" | awk -F'\t' '{printf "     PLAN.md:%s  %s  last cell reads: %s\n", $2, $3, $4}')
     The status/owner/claimed cells are addressed FROM THE END and cannot contain a literal \`|\`.
     Escape it as \`\\|\` in the row's prose, then re-run."

  _BOARD_CACHE_FOR="$plan"
}

_board_record() {                    # _board_record <plan> <id> — the row's record, or empty
  board_scan "$1"
  printf '%s\n' "$_BOARD_CACHE" | awk -F'\t' -v id="$2" '$2==id'
}

# row_line_no <plan> <id> — the ONE line number of that row, or die.
#
# ⛔ A DUPLICATE ID IS A HARD ERROR, and this is the check that did not exist: 2026-08-01, two agents
#    filed different rows both called `A2o`, both noticed independently, both renamed to `A2r`, and
#    the collision simply MOVED. A third rename settled it. Three pushes spent on a name.
row_line_no() {
  local rec n
  rec="$(_board_record "$1" "$2")"
  [ -n "$rec" ] || die "no BOARD row for '$2'.
     ⚠ A ladder rung (P1.2, P1.7a…) is not a board row — it lives in the ladder and in the *Status at
       a glance* index, neither of which this tooling claims or closes."
  n="$(printf '%s\n' "$rec" | grep -c .)"
  [ "$n" = "1" ] || die "row id '$2' appears $n times in the board, on lines: $(printf '%s\n' "$rec" | cut -f1 | tr '\n' ' ')
     A DUPLICATE ROW ID IS THE COLLISION THIS CHECK EXISTS FOR. Settle the name on the board first."
  printf '%s\n' "$rec" | cut -f1
}

# row_line_no_soft <plan> <id> — empty when the id has no row, or more than one. A batch member
# legitimately may not have a row of its own; the caller leaves those alone.
row_line_no_soft() {
  local rec
  rec="$(_board_record "$1" "$2")"
  [ "$(printf '%s\n' "$rec" | grep -c .)" = "1" ] || return 0
  printf '%s\n' "$rec" | cut -f1
}

row_status()  { _board_record "$1" "$2" | cut -f3; }
row_lanes()   { _board_record "$1" "$2" | cut -f4 | grep -oE 'L-[A-Za-z0-9_-]+' | sort -u; }
row_members() { _board_record "$1" "$2" | cut -f5 | grep -oE '`[A-Za-z0-9_.-]+`' | tr -d '`' | sort -u; }
row_owner()   { _board_record "$1" "$2" | cut -f6; }
row_claimed() { _board_record "$1" "$2" | cut -f7; }

# row_is_free <status-cell> — true when the row is claimable.
#
# ⚠ THE STATUS CELL IS NOT ALWAYS JUST A STATUS. BATCH10's holds a 300-character release note
#   ("RELEASED UNSTARTED, and the reason is a finding…"). That is a real state — released, with a
#   reason — so `⬜ FREE` cannot be an exact-match test. Claimable iff it is a ⬜ and holds nobody
#   else's 🔶.
row_is_free() {
  case "$1" in
    *🔶*|*✅*|*⛔*) return 1 ;;
    ⬜*)            return 0 ;;
    *)              return 1 ;;
  esac
}

# lane_conflicts <plan> <self-id> <lanes> — every LIVE (🔶) row holding one of <lanes>, one per line.
#
# THE LANE IS THE REAL EXCLUSION UNIT. Six of the board's rows raise refusals inside ONE 28,152-line
# Parser.maxon, so "different mechanism" does not mean "different code". The board calls this "the
# concurrency limit, and it is not advisory" — and until this function existed, nothing checked it.
#
# ⛔ THE RECORD IS SPLIT WITH `cut`, NOT BY `read`'s IFS, AND THE DIFFERENCE IS A MISSED CONFLICT.
#    Tab is IFS *whitespace*, so bash collapses a RUN of tabs into a single delimiter. This loop wants
#    field 4 (lanes) — but a row whose Lane cell is EMPTY collapses the gap and hands field 4 the next
#    non-empty cell instead, which on a BOARD row (whose `members` is empty by construction) is the
#    OWNER. The `L-` scan would then run over a branch name and find nothing, and this function's whole
#    job is to REFUSE — so its failure mode is silently reporting "no conflict" and letting two agents
#    into one lane. `cut -f` counts empty fields; `read` cannot be made to.
lane_conflicts() {
  local plan="$1" self="$2" lanes="$3" rec line id lane_cell shared
  [ -n "$lanes" ] || return 0
  board_scan "$plan"
  while IFS= read -r rec; do
    [ -n "$rec" ] || continue
    id="$(printf '%s' "$rec" | cut -f2)"
    [ "$id" != "$self" ] || continue
    line="$(printf '%s' "$rec" | cut -f1)"
    lane_cell="$(printf '%s' "$rec" | cut -f4)"
    shared="$(comm -12 <(printf '%s\n' "$lanes") \
                       <(printf '%s\n' "$lane_cell" | grep -oE 'L-[A-Za-z0-9_-]+' | sort -u))"
    [ -n "$shared" ] && echo "$id (PLAN.md:$line) holds: $(printf '%s' "$shared" | tr '\n' ' ')"
  done <<< "$(printf '%s\n' "$_BOARD_CACHE" | awk -F'\t' '$3 ~ /🔶/')"
  return 0
}

# live_claim_branches <plan> — the branch named by every LIVE (🔶) row's Owner cell, one per line.
#
# ⭐ THE BOARD IS THE ONLY PLACE A LIVE RUNG IS WRITTEN DOWN, and since 2026-08-04 it is the only place
#    LEFT. Branches never go to origin, so `git ls-remote --heads origin` — which rung-start.sh's own
#    refusal message used to name as the way to tell a live claim from an abandoned one — reports
#    nothing for either. rung-start.sh says so itself in its step 5: "Do not read an absent remote
#    branch as evidence of anything."
#
#    So anything that wants to DELETE a worktree has exactly one honest way to ask whether another
#    agent is standing in it, and this is it. A reaper without this question is a reaper that guesses,
#    and the thing it guesses wrong about is somebody else's live rung.
#
# ⛔ awk, NOT `while IFS=$'\t' read`. THE FIRST VERSION OF THIS FUNCTION USED read AND RETURNED THE
#    CLAIM DATE WHERE THE BRANCH BELONGS. Tab is IFS *whitespace*, so bash collapses a RUN of tabs into
#    one delimiter — and a BOARD row's `members` field is empty by construction (only BATCH rows have
#    members), so every field after it shifted left by one and `owner` received `claimed`. It read
#    `2026-08-09` as a branch name, which no worktree is ever called, so the guard would have silently
#    protected NOTHING while looking like it worked. Caught only by a synthetic board with a known
#    answer; the live board has zero 🔶 rows today, so the real one could not have caught it.
#    `awk -F'\t'` does not collapse, which is also why every accessor above reaches for `cut`.
live_claim_branches() {
  board_scan "$1"
  printf '%s\n' "$_BOARD_CACHE" | awk -F'\t' '$3 ~ /🔶/ { gsub(/`/, "", $6); if ($6 != "") print $6 }'
}

# ── writing ──────────────────────────────────────────────────────────────────────────────────────

# row_prefix <line-text> — everything BEFORE the last three cells: the part this tooling never edits.
row_prefix() {
  local line="$1" p
  p="${line%|*}"; p="${p%|*}"; p="${p%|*}"; p="${p%|*}"
  case "$line" in "$p"*) ;; *) die "cannot split the row's trailing cells — malformed row" ;; esac
  [ -n "$p" ] || die "row has fewer than four cells; this is not a board row"
  printf '%s' "$p"
}

# set_row_tail <plan> <line-no> <status> <owner> <claimed> — replace ONLY the last three cells.
#
# By LINE NUMBER, never by pattern: a pattern that matched twice would edit a row nobody claimed.
set_row_tail() {
  local plan="$1" ln="$2" status="$3" owner="$4" claimed="$5" line prefix tmp
  line="$(sed -n "${ln}p" "$plan")"
  prefix="$(row_prefix "$line")"
  tmp="$(mktemp)"
  printf '%s| %s | %s | %s |\n' "$prefix" "$status" "$owner" "$claimed" > "$tmp"
  awk -v n="$ln" -v f="$tmp" 'NR==n{ while ((getline l < f) > 0) print l; next } {print}' \
    "$plan" > "$plan.tmp" && mv "$plan.tmp" "$plan" || { rm -f "$tmp"; die "failed to edit $plan"; }
  rm -f "$tmp"
  _BOARD_CACHE=""; _BOARD_CACHE_FOR=""      # the file moved under the cache
}
