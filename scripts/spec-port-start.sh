#!/usr/bin/env bash
#
# THE /spec-port STARTING LINE — SKILL.md §0 through §2, as one command.
#
# It answers exactly two questions and refuses to editorialize about either:
#
#     WHICH SPEC did we take, and did it PASS or FAIL on its first run?
#
# What it does, in order: pick the next spec off the whitelist -> build -> copy it BYTE-IDENTICAL ->
# run its filter -> run §2's three count checks -> print the verdict.
#
# ⭐ THE VERDICT IS THE WHOLE POINT, because it is the fork in the process:
#
#     PASSED  -> §7b's FAST PATH. The tick is already done. Commit and push; no reviewer, no full
#                suite, no `spec-port-finish.sh`. (Run §2's counts anyway — this script already did.)
#     FAILED  -> §3. Delegate the gap to `maxon-spec-implementer`, then land via `spec-port-finish.sh`.
#
# ⚠ A FAILING SPEC IS LEFT IN THE TREE ON PURPOSE. That copy is the tick's subject — the implementer
#   needs it, and the runner discovers specs by listing `specs-shv2/*.md`, so removing it would remove
#   the work. Use `--revert` to take it back out if you are abandoning the tick.
#
# ⛔ IT DOES NOT COMMIT, PUSH, EDIT A LOG, OR TOUCH `/specs`. Everything it writes is one new file in
#   `specs-shv2/` plus whatever goldens the runner mints. Landing is `spec-port-finish.sh`'s job (or,
#   on the fast path, yours).
#
# USAGE
#   scripts/spec-port-start.sh [--spec <name>] [--no-build] [--revert] [--list N]
#
#   --spec <name>   take this spec instead of the next one on the whitelist (SKILL.md's `/spec-port <name>`)
#   --no-build      skip the shv2 build. ⚠ Only when you KNOW the binary is current — a stale binary has
#                   already cost this repo a ladder read off the wrong compiler.
#   --revert        remove the named/selected spec copy and its minted goldens, then exit. For abandoning
#                   a tick without leaving a red spec on main.
#   --list N        print the next N candidate specs and exit, changing nothing.
#
set -o pipefail

readonly REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly SHV2="$REPO/maxon-shv2/.maxon/maxon-shv2.exe"
readonly WHITELIST="$REPO/maxon-selfhosted/Testing/SpecTestRunner.maxon"
readonly LOGDIR="$REPO/temp"

# ⛔ THE PERMANENT SKIP LIST — specs the loop must never take, with the ruling that withdrew each.
#
# This exists because SKILL.md's selector has, by design, only two exclusions (already-ported, and no
# source file) and DELIBERATELY no third — a `DEFERRED` row must never retire a spec. A user ruling is
# not a deferral, but the selector cannot tell the difference, so it re-offers a withdrawn spec on
# EVERY tick, forever. It was skipped by hand twice on 2026-08-04 alone.
#
# ⇒ Add a name here ONLY for an explicit user ruling, and name the ruling on the line. Never to make a
#   hard spec go away — that is §4, and §4 says build it.
#
# ✅ THE LIST IS NOW EMPTY. Its only entry, `unused-export`, was REINSTATED 2026-08-20 by user ruling:
#   the 2026-08-03 withdrawal (`be29fbbe3`) held that an `export` is a PUBLIC API SURFACE, so "no caller
#   in this compilation" is not the same fact as "dead" — a correct objection to a lint that read a
#   VISIBILITY modifier as a claim about USE. The answer is a `public` tier that says which: `export`
#   means "other files may see this, and I expect this program to use it"; `public` means "this is API
#   surface, do not ask who calls it". With the two facts spelled separately the lint asks a question
#   the author can answer, so E3092/E3093/E3094 land after all. Keep the machinery below — the next
#   ruling will need it.
readonly SKIP_SPECS=""

die()  { printf '\n\033[31m✗ %s\033[0m\n' "$*" >&2; exit 1; }
ok()   { printf '\033[32m✓\033[0m %s\n' "$*"; }
step() { printf '\n\033[1m── %s\033[0m\n' "$*"; }
warn() { printf '\033[33m⚠\033[0m %s\n' "$*"; }
# Neutral context, not a finding — a warn() here would read as a defect in the port.
note() { printf '\033[2m·\033[0m %s\n' "$*"; }

SPEC=""; NO_BUILD=0; REVERT=0; LIST=0

while [ $# -gt 0 ]; do
  case "$1" in
    --spec)     SPEC="$2";   shift 2 ;;
    --no-build) NO_BUILD=1;  shift   ;;
    --revert)   REVERT=1;    shift   ;;
    --list)     LIST="$2";   shift 2 ;;
    -h|--help)  sed -n '2,38p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) die "unknown argument: $1" ;;
  esac
done

cd "$REPO" || die "cannot cd to $REPO"
mkdir -p "$LOGDIR" || die "cannot create $LOGDIR"
[ -f "$WHITELIST" ] || die "whitelist not found: $WHITELIST"

is_skipped() {
  for _s in $SKIP_SPECS; do [ "$1" = "$_s" ] && return 0; done
  return 1
}

# The whitelist IS the backlog, in v1's deliberately easiest-first order. Never re-sort it.
candidates() {
  sed -n '5,425p' "$WHITELIST" | sed -n 's/^[[:space:]]*"\([a-z0-9-]*\)".*/\1/p'
}

next_spec() {
  local s
  while read -r s; do
    [ -z "$s" ]                  && continue
    is_skipped "$s"              && continue
    [ -f "specs-shv2/$s.md" ]    && continue   # already ported
    [ -f "specs/$s.md" ]         || continue   # named in v1's head only; report happens in --list
    echo "$s"; return 0
  done < <(candidates)
  return 1
}

# ── --list: show the queue and change nothing ────────────────────────────────────────────────────
if [ "$LIST" != "0" ]; then
  printf '\033[1mnext %s candidate spec(s)\033[0m\n' "$LIST"
  n=0
  while read -r s; do
    [ -z "$s" ] && continue
    if is_skipped "$s"; then
      [ -f "specs-shv2/$s.md" ] || printf '  \033[33mSKIP\033[0m %-44s (permanent skip — see SKIP_SPECS)\n' "$s"
      continue
    fi
    [ -f "specs-shv2/$s.md" ] && continue
    if [ ! -f "specs/$s.md" ]; then
      printf '  \033[33mSKIP\033[0m %-44s (no specs/%s.md — whitelisted but never written)\n' "$s" "$s"
      continue
    fi
    printf '  %2d. %-44s (%s cases)\n' "$((n + 1))" "$s" "$(grep -c '<!-- test:' "specs/$s.md")"
    n=$((n + 1)); [ "$n" -ge "$LIST" ] && break
  done < <(candidates)
  exit 0
fi

# ── Which spec ───────────────────────────────────────────────────────────────────────────────────
if [ -n "$SPEC" ]; then
  [ -f "specs/$SPEC.md" ] || die "no such spec: specs/$SPEC.md"
  is_skipped "$SPEC" && warn "'$SPEC' is on the permanent skip list — taking it anyway because you named it explicitly"
else
  SPEC="$(next_spec)" || die "no unported spec left on the whitelist — that would be the whole backlog done, so verify before believing it"
fi

# ── --revert: undo a start, for abandoning a tick ────────────────────────────────────────────────
if [ "$REVERT" = "1" ]; then
  step "Revert $SPEC"
  if git ls-files --error-unmatch "specs-shv2/$SPEC.md" >/dev/null 2>&1; then
    die "specs-shv2/$SPEC.md is COMMITTED — this script will not delete tracked work. Use git."
  fi
  rm -f  "specs-shv2/$SPEC.md"
  rm -rf "specs-shv2/fragments"/*/"$SPEC"
  ok "removed specs-shv2/$SPEC.md and any minted goldens"
  exit 0
fi

# §0: the tree must be clean BEFORE the copy, or you cannot tell your minted goldens from leftovers.
if [ -n "$(git status --porcelain)" ]; then
  git status --short
  die "working tree is not clean. The suite MINTS goldens as a side effect — start dirty and you cannot tell yours from the leftovers."
fi

[ -f "specs-shv2/$SPEC.md" ] && die "specs-shv2/$SPEC.md already exists — that spec is already ported"

printf '\n\033[1mspec:\033[0m %s   \033[2m(%s cases in /specs)\033[0m\n' "$SPEC" "$(grep -c '<!-- test:' "specs/$SPEC.md")"

# ── Build ────────────────────────────────────────────────────────────────────────────────────────
# Not a baseline — there is no baseline (§0). This makes the BINARY current, because everything below
# is read off it.
if [ "$NO_BUILD" = "1" ]; then
  warn "skipping the build — everything below is read off whatever binary is on disk"
  [ -x "$SHV2" ] || die "and there is no shv2 binary at $SHV2"
else
  step "Build shv2"
  [ -x "$REPO/bin/maxon.exe" ] || [ -x "$REPO/bin/maxon" ] || die "bootstrap not built — it builds shv2"
  BOOT="$REPO/bin/maxon.exe"; [ -x "$BOOT" ] || BOOT="$REPO/bin/maxon"
  if ! "$BOOT" build maxon-shv2 > "$LOGDIR/spec-port-start-build.log" 2>&1; then
    tail -25 "$LOGDIR/spec-port-start-build.log"
    die "shv2 build FAILED — full log: temp/spec-port-start-build.log"
  fi
  ok "shv2 built"
fi

# ── §1: copy it BYTE-IDENTICAL ───────────────────────────────────────────────────────────────────
step "Copy (byte-identical)"
cp "specs/$SPEC.md" "specs-shv2/$SPEC.md" || die "copy failed"
cmp -s "specs/$SPEC.md" "specs-shv2/$SPEC.md" || die "the copy differs from /specs — that cannot happen; investigate before continuing"
ok "specs-shv2/$SPEC.md is byte-identical to specs/$SPEC.md"

# ── §2: run it ───────────────────────────────────────────────────────────────────────────────────
step "Run its filter"
RUNLOG="$LOGDIR/spec-port-start-$SPEC.log"
"$SHV2" spec-test --filter="$SPEC/" > "$RUNLOG" 2>&1
RUN_EXIT=$?

SUMMARY="$(grep -E '^[0-9]+ passed, [0-9]+ failed' "$RUNLOG" | tail -1)"
[ -n "$SUMMARY" ] || { tail -30 "$RUNLOG"; die "could not parse a summary line from the run — full log: ${RUNLOG#$REPO/}"; }
PASSED="$(echo "$SUMMARY" | sed 's/ passed.*//')"
FAILED="$(echo "$SUMMARY" | sed 's/.*passed, //; s/ failed.*//')"
TOTAL=$((PASSED + FAILED))
ok "$SUMMARY"

# The filter is a SUBSTRING of the `<spec>/<test>` label, so `--filter=<spec>/` also selects any OTHER
# spec whose name ENDS with ours — `regalloc/` drags in `generic-hash-table-regalloc/`. The §2 count is
# a claim about ONE spec, so it must be read off the per-test lines, never off the summary total.
# Measured 2026-08-10: `regalloc` read 6 markers against a total of 8 and looked like a surplus.
OURS="$(grep -cE "^(PASS|FAIL) $SPEC/" "$RUNLOG")"
COLLIDERS="$(grep -oE "^(PASS|FAIL) [a-z0-9-]+/" "$RUNLOG" | sed 's/^[A-Z]* //; s|/$||' | sort -u | grep -vx "$SPEC" || true)"

# ── §2: THE COUNT CHECKS. The colour is not the gate; the count is. ──────────────────────────────
#
# A spec can pass by running NOTHING — `status: draft` returns zero tests, and a stray `## ` heading
# ends the active-test region and silently drops every case below it. Both read as a clean green.
step "Count checks (§2)"
MARKERS="$(grep -c '<!-- test:' "specs-shv2/$SPEC.md")"
DISABLED="$(grep -c '<!-- disabled-test:' "specs-shv2/$SPEC.md")"
DUPES="$(grep -o '<!-- \(disabled-\)\?test: [^ ]*' "specs-shv2/$SPEC.md" | sed 's/.*test: //' | sort | uniq -d)"
COUNTS_OK=1

if [ -n "$COLLIDERS" ]; then
  note "the filter is a SUBSTRING match, so it also selected: $(echo "$COLLIDERS" | tr '\n' ' ')"
  note "counting only the $OURS line(s) labelled '$SPEC/' — the summary total ($TOTAL) is NOT this spec's count"
fi

if [ "$MARKERS" -eq "$OURS" ]; then
  ok "markers == ran ($MARKERS)"
else
  COUNTS_OK=0
  warn "MARKER MISMATCH: $MARKERS '<!-- test:' markers but $OURS ran under '$SPEC/'"
  grep -n '^## ' "specs-shv2/$SPEC.md" | sed 's/^/      heading: /'
  warn "a '## ' heading after '## Tests' ends the active-test region — move it BELOW the cases (§2)"
  grep -q '^status:[[:space:]]*draft' "specs-shv2/$SPEC.md" && warn "frontmatter says 'status: draft' — that returns ZERO tests for the whole file"
fi

if [ "$DISABLED" -eq 0 ]; then
  ok "zero 'disabled-test:' markers"
else
  COUNTS_OK=0
  warn "$DISABLED 'disabled-test:' marker(s) in a spec you are porting — §4 permits NONE. Build the mechanism."
fi

if [ -z "$DUPES" ]; then
  ok "no name spelled both 'test:' and 'disabled-test:'"
else
  COUNTS_OK=0
  warn "name(s) spelled BOTH ways — the file reads as though a case were disabled while it still runs:"
  echo "$DUPES" | sed 's/^/      /'
fi

grep -q 'memory leak' "$RUNLOG" && warn "the run mentions a memory leak — read ${RUNLOG#$REPO/}"
[ "$RUN_EXIT" = "101" ] && warn "exit 101 — MEMORY LEAK detected"

# ── The verdict ──────────────────────────────────────────────────────────────────────────────────
printf '\n'
if [ "$FAILED" -eq 0 ] && [ "$COUNTS_OK" = "1" ] && [ "$TOTAL" -gt 0 ]; then
  printf '\033[32m╔══════════════════════════════════════════════════════════════════════╗\033[0m\n'
  printf '\033[32m║  %-68s║\033[0m\n' "PASSED on the first run: $SPEC, $PASSED/$TOTAL"
  printf '\033[32m╚══════════════════════════════════════════════════════════════════════╝\033[0m\n'
  cat <<EOF

⚡ §7b FAST PATH — this tick is already done, provided you changed no compiler source.

   git add specs-shv2/$SPEC.md specs-shv2/fragments/*/$SPEC
   ... append a docs/spec-port-log.md row (NO suite figure — none was measured) ...
   git commit && git push

   No reviewer (the diff has zero lines of compiler source), no full suite (a new spec
   file cannot change another spec's outcome), and do NOT run spec-port-finish.sh.
EOF
  git status --short | sed 's/^/   /'
  exit 0
fi

printf '\033[33m╔══════════════════════════════════════════════════════════════════════╗\033[0m\n'
if [ "$TOTAL" -eq 0 ]; then
  printf '\033[33m║  %-68s║\033[0m\n' "RAN NOTHING: $SPEC. That is the failure this check exists for."
elif [ "$COUNTS_OK" = "0" ] && [ "$FAILED" -eq 0 ]; then
  printf '\033[33m║  %-68s║\033[0m\n' "GREEN BUT THE COUNTS DISAGREE: $SPEC. Fix the port, not the number."
else
  printf '\033[33m║  %-68s║\033[0m\n' "FAILED on the first run: $SPEC, $FAILED of $TOTAL"
fi
printf '\033[33m╚══════════════════════════════════════════════════════════════════════╝\033[0m\n'

if [ "$FAILED" -gt 0 ]; then
  printf '\nfailing cases:\n'
  grep '^FAIL ' "$RUNLOG" | sed 's/^/   /'
fi

cat <<EOF

→ §3. Brief \`maxon-spec-implementer\` with the case names and these symptoms — the diagnosis you
  have already done is the most valuable thing in the brief. Then land via spec-port-finish.sh.

  The spec copy is LEFT IN PLACE on purpose: it is the tick's subject, and the runner finds specs
  by listing specs-shv2/*.md. Abandoning instead?   scripts/spec-port-start.sh --spec $SPEC --revert

  full log: ${RUNLOG#$REPO/}
EOF
exit 1
