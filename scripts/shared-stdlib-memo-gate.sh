#!/usr/bin/env bash
#
# THE SHARED-STDLIB-MEMO GATE — does a compile that reads the stdlib out of the PROCESS-WIDE store
# produce exactly what a compile that lexed it itself produces?
#
# G20 wave 2 made `spec-test` compile every case IN PROCESS. Wave 3 used that to give ONE memo a
# longer life: a stdlib file's tokens, its `#if`-resolved stream and the producer mask derived from
# it are now held for the life of the PROCESS rather than of a `Project`
# (`QueryEngine.sharedFileMemos`). Every compile after the first in a worker therefore reads a stdlib
# file's tokens out of an array some EARLIER, UNRELATED program's compile produced.
#
# That is a sharing this compiler has never done before, and the property it must not break is byte
# identity: what a program compiles to may not depend on what was compiled before it.
#
# ============================================================================================
# WHY THIS IS A SHELL GATE AND NOT A SPEC
# ============================================================================================
#
# A spec case is one program compiled once. The property here is about the SECOND compile in a
# process and about what the FIRST one left behind, so no single case can express it — the same
# reason `output-lock-gate.sh` and `spec-wedge-repro.sh` are scripts: the property lives outside the
# program under test, in the state of the process around it.
#
# ============================================================================================
# WHAT IT ASSERTS, AND WHY EACH PART IS LOAD-BEARING
# ============================================================================================
#
# 1. `verify-warm-rebuild` still passes all three of its properties (determinism, cache-hit,
#    invalidation). Its determinism property is TWO INDEPENDENT COLD COMPILES IN ONE PROCESS, so
#    since wave 3 the second of them is served entirely out of the store — which makes an assertion
#    that was about the query spine into an assertion about the store as well.
#
#    ⚠ Its cache-hit property is the one that already caught a defect in this mechanism, and that is
#    why it is check 1 rather than a formality. The first draft answered a shared ACTIVE-token hit
#    without ever asking for the raw tokens, which left the per-`Project` `tokenCache` empty for
#    every file the store answered for: `rebuild token re-query hits (tokenHits): expected 52, got 1`.
#    Nothing was re-lexed and nothing was mis-compiled — what broke was incrementality, and only this
#    check could see it.
#
# 2. POSITIVE CONTROL: the store must be SEEN to serve. The compiler reports its per-compile take
#    under `--log=compiler:debug`, and this asserts BOTH ends of it — a first compile that reads zero
#    (the store starts empty, so a non-zero here means it is answering from somewhere it should not)
#    and a later compile that reads non-zero (a zero here means every other check in this file passed
#    over a mechanism that never fired).
#
# 3. The MEMORY BOUND: a shared entry lives for the life of the worker, so what the store HOLDS must
#    track `stdlib/` and not the corpus — otherwise a suite grows it by one token array per spec
#    fragment and a worker leaks for the whole run. Asserted as `holding == admitted` on every
#    reported compile: `admitted` is what one compile offered, `holding` is what the process kept, and
#    the two part company exactly when the bound breaks. Neither number is written down here — the
#    value is the whitelist's length, and a copy of it would be a second place to update.
#
#    ⚠ `holding == admitted` is the invariant for a process that sees ONE stdlib, which is what this
#    gate's own driver does. A run that also compiles `// --- stdlib-overlay:` cases legitimately
#    holds more, because an overlay stages a SECOND stdlib whose changed file has different bytes and
#    so earns its own entry. That growth is bounded by the number of overlay cases, not by the corpus.
#
#    ⚠ **THIS CHECK USED TO COMPARE THE ADMITTED COUNT BETWEEN A ONE-FILE AND A TWO-FILE PROGRAM,
#    AND THAT VERSION COULD NOT FAIL.** The sabotage below — publishing every file instead of the
#    admitted ones — leaves the admission count at exactly `stdlib/`'s in both runs, because the
#    admission LIST is not the thing that grows. The STORE is. Found by running the sabotage, which
#    is the only reason this check now measures what its name claims.
#
# 4. The SUITE, which is the two-different-fragments check at scale. Every case in a filtered run is
#    a DIFFERENT program compiled in a worker that has already compiled hundreds of others, and every
#    one of them is compared against a golden minted by a compiler that had no such store. That is
#    strictly stronger than a hand-built pair of fragments, and it is where a wrong answer would show.
#
#   ⚠ WHAT THIS GATE DOES NOT COVER, stated rather than left to be assumed: the store's key carries
#     the TARGET (`QueryDatabase.SharedTargetView`), because a `#if`-resolved stream is
#     target-dependent and a memo outliving the `Project` cannot inherit "one `Project`, one target".
#     No driver in this tree compiles for two targets in ONE process — `verify-warm-rebuild` calls
#     `detectHostTarget()` outright and `spec-test` takes a single `--target` — so the MIXING case is
#     unreachable from here. Each lane proves its own key; nothing available proves the two apart.
#
#   ⚠ VERIFIED TO GO RED, by sabotage rather than by argument, on the commit that added this file:
#     * make `sharedActiveTokensOf` hand back the RAW stream instead of the `#if`-resolved one and
#       CHECKS 1 and 4 go red — `12 passed, 591 failed`, most of them
#       `E2001: stdlib/FilePath.maxon:3:1: Expected function declaration, got '#if'`. CHECKS 2 and 3
#       stay GREEN, correctly: the store still fires and is still bounded, only its content is wrong.
#     * make `shareTokens` publish every file rather than only the admitted ones and CHECK 3 goes red
#       (`holding` climbs past `admitted`) while 1, 2 and 4 stay green — nothing is MIS-compiled by an
#       over-large store, it simply never stops growing.
#
# Usage:  scripts/shared-stdlib-memo-gate.sh [--filter=PATTERN]
# Exit:   0 = all checks pass · 1 = a check failed · 2 = the gate could not run (setup failure)

set -u

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 2

FILTER="string"
for arg in "$@"; do
	case "$arg" in
		--filter=*) FILTER="${arg#--filter=}" ;;
		*) echo "gate: unrecognized argument '$arg'" >&2; exit 2 ;;
	esac
done

SHV2="./maxon-shv2/.maxon/maxon-shv2.exe"
[ -x "$SHV2" ] || SHV2="./maxon-shv2/.maxon/maxon-shv2"
if [ ! -x "$SHV2" ]; then
	echo "gate: no shv2 binary — run \`./bin/maxon.exe build maxon-shv2\` first" >&2
	exit 2
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

failures=0
fail() { echo "FAIL $*"; failures=$((failures + 1)); }
pass() { echo "PASS $*"; }

cat > "$WORK/one.maxon" <<'EOF'
function main() returns ExitCode
	var total = 0
	for i in 0 upto 5 'loop'
		total = total + i
	end 'loop'
	return total
end 'main'
EOF

# ---------------------------------------------------------------------------- CHECKS 1, 2 and 3
"$SHV2" verify-warm-rebuild "$WORK/one.maxon" --log=compiler:debug > "$WORK/vwr.log" 2>&1
vwr_rc=$?

if [ "$vwr_rc" -eq 0 ]; then
	pass "CHECK 1: verify-warm-rebuild holds with the shared store live (determinism + cache + invalidation)"
else
	fail "CHECK 1: verify-warm-rebuild exited $vwr_rc"
	grep -E '^FAIL' "$WORK/vwr.log" | sed 's/^/       /'
fi

grep -o 'shared stdlib memo .*holding' "$WORK/vwr.log" > "$WORK/memo-lines.txt"

if [ ! -s "$WORK/memo-lines.txt" ]; then
	fail "CHECK 2: the compiler printed no shared-memo line at all — the gate cannot see the mechanism"
	fail "CHECK 3: no reading to bound the store with"
else
	# `[0-9][0-9]*` rather than `[0-9]*`, which also matches the empty string and would emit blank
	# lines for `sort -n` to sink to the top. The space before `token` is what keeps this from also
	# matching the `active-token hit(s)` field, which is preceded by a hyphen.
	first_hits=$(head -1 "$WORK/memo-lines.txt" | grep -o '[0-9][0-9]* token hit' | grep -o '[0-9][0-9]*')
	max_hits=$(grep -o '[0-9][0-9]* token hit' "$WORK/memo-lines.txt" | grep -o '[0-9][0-9]*' | sort -n | tail -1)
	first_hits=${first_hits:-0}
	max_hits=${max_hits:-0}

	if [ "$first_hits" -ne 0 ]; then
		fail "CHECK 2a: the FIRST compile in the process read $first_hits token stream(s) out of a store that had not been filled yet"
	else
		pass "CHECK 2a: the first compile in a process reads nothing from the store"
	fi

	if [ "$max_hits" -le 0 ]; then
		fail "CHECK 2b: no compile ever read anything from the store — every other check here passed over a mechanism that never fired"
	else
		pass "CHECK 2b: a later compile read $max_hits stdlib file(s) out of the store (the mechanism fires)"
	fi

	# `verify-warm-rebuild` compiles the same program many times AND edits it between runs, so its log
	# holds a whole sequence of readings — every one of which must show the store holding exactly what
	# was admitted. An edit mints new bytes, so a store that kept them shows up here first.
	readings=$(wc -l < "$WORK/memo-lines.txt")
	overgrown=$(sed 's/.*, \([0-9]*\) admitted, \([0-9]*\) holding.*/\1 \2/' "$WORK/memo-lines.txt" | awk '$1 != $2 { print $1" admitted, "$2" holding" }' | head -3)

	if [ -n "$overgrown" ]; then
		fail "CHECK 3: the store holds more than was admitted, so it grows with the corpus rather than with stdlib/"
		echo "$overgrown" | sed 's/^/       /'
	else
		held=$(head -1 "$WORK/memo-lines.txt" | sed 's/.*, \([0-9]*\) holding.*/\1/')
		pass "CHECK 3: the store holds exactly what was admitted ($held file(s)) across all $readings reported compile(s)"
	fi
fi

# ---------------------------------------------------------------------------------- CHECK 4
"$SHV2" spec-test --filter="$FILTER" > "$WORK/suite.log" 2>&1
suite_rc=$?
summary=$(grep -E '^[0-9]+ passed, [0-9]+ failed' "$WORK/suite.log" | tail -1)

if [ "$suite_rc" -ne 0 ]; then
	fail "CHECK 4: spec-test --filter=$FILTER exited $suite_rc (${summary:-no summary line})"
	grep -E '^FAIL' "$WORK/suite.log" | head -20 | sed 's/^/       /'
elif [ -z "$summary" ]; then
	fail "CHECK 4: spec-test printed no summary line"
elif ! echo "$summary" | grep -q ', 0 failed'; then
	fail "CHECK 4: $summary"
else
	pass "CHECK 4: $summary — every one of them a different program compiled in a worker the store had already served"
fi

echo
if [ "$failures" -eq 0 ]; then
	echo "shared-stdlib-memo-gate: PASS"
	exit 0
fi
echo "shared-stdlib-memo-gate: FAIL ($failures check(s))"
exit 1
