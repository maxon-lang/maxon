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
# 4. The SUITE. Every case in a filtered run is a DIFFERENT program compiled in a worker that has
#    already compiled hundreds of others, so a store that mis-serves anything shows up as a failing
#    case here at corpus scale.
#
#    ⚠ **IT IS NOT THE WARM-AGAINST-COLD CHECK, AND THIS FILE CLAIMED IT WAS.** It read *"the
#    two-different-fragments check at scale … strictly stronger than a hand-built pair of
#    fragments"*, on the ground that each case is compared against *"a golden minted by a compiler
#    that had no such store"*. **A golden mismatch is a `note:` and not a failure** (⚖ user ruling
#    2026-08-02) and the committed corpus already carries hundreds of drifting goldens, so this check
#    reads `0 failed` and nothing else. A defect that mis-compiles WITHOUT failing a case is invisible
#    to it. That is CHECK 5's job, and it is why CHECK 5 exists rather than being a smaller copy of
#    this one. **MEASURED at the commit that added CHECK 5:** under the raw-stream sabotage below,
#    with a one-case `--filter`, CHECK 4 stays GREEN and CHECK 5 goes RED.
#
# 5. WARM AGAINST COLD, on three unrelated programs. Its own section below carries the argument; the
#    short form is that it is the only comparison in this tree whose reference was minted by THIS
#    binary with the store EMPTY, and the only place a golden mismatch is a hard failure.
#
#   ⚠ WHAT THIS GATE DOES NOT COVER, stated rather than left to be assumed: the store's key carries
#     the TARGET (`QueryDatabase.SharedTargetView`), because a `#if`-resolved stream is
#     target-dependent and a memo outliving the `Project` cannot inherit "one `Project`, one target".
#     No driver in this tree compiles for two targets in ONE process — `verify-warm-rebuild` calls
#     `detectHostTarget()` outright and `spec-test` takes a single `--target` — so the MIXING case is
#     unreachable from here. Each lane proves its own key; nothing available proves the two apart.
#
#   ⚠ VERIFIED TO GO RED, by sabotage rather than by argument. On the commit that added CHECK 5:
#     * the raw-stream sabotage below, run with `--filter=free-function-from-method` (ONE case, so no
#       worker compiles a second program): CHECK 5 red — `1 passed, 2 failed`, and the two that failed
#       are exactly `beta` and `clock-shadow`, the second and third compiles in the worker. `alpha`,
#       the compile that FILLS the store, passes. CHECK 4 stays green on that filter, which is the
#       measurement in the CHECK 4 note above.
#     * a cold reference perturbed by one byte before the shared run: CHECK 5 red, `1 of 3 program(s)
#       emit different code warm than they do cold` — the DRIFT arm, which no other check has.
#     * a cold reference DELETED before the shared run: CHECK 5 red, `2 of 3 program(s) had a cold
#       reference to compare against` — the guard against passing over fewer programs than it names.
#     * the cold mints pointed at a filter matching nothing: CHECK 5 red, `3 of 3 cold reference
#       mint(s) failed`.
#
#   ⛔ **AND ONE SABOTAGE THAT DID *NOT* FIRE, RECORDED BECAUSE IT BOUNDS WHAT HAS BEEN PROVED.**
#     Making `sharedProducerMaskOf` return ALL FIVE producer bits on a hit — a store serving a wrong
#     but well-formed answer — left every check GREEN, CHECK 5 included. It is not a hole in the
#     check: an over-set producer bit registers a layout or interns an instance that nothing
#     references, and neither reaches codegen (`Parser.foldDeclaredSignaturesInto`'s probe block says
#     so, and this measures it). ⇒ **Everything the store holds today is either byte-exact — corrupt
#     the tokens and the compile FAILS — or codegen-inert. There is no silent-poison surface to
#     exercise, so CHECK 5's DRIFT arm has been proved against a perturbed REFERENCE and never against
#     a compiler defect.** The moment the store is asked to hold something richer, that stops being
#     true, and this is the check that would see it.
#
#   Earlier, on the commit that added this file:
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

# ---------------------------------------------------------------------------------- CHECK 5
#
# ⛔⛔ **TWO DIFFERENT FRAGMENTS, NOT ONE FRAGMENT TWICE — AND THIS IS THE ONE CHECK THAT COMPARES
# WARM AGAINST COLD.** Everything above compares a compile against an EXPECTATION (an exit code, a
# committed golden minted by some earlier binary). This compares the same program's EMITTED CODE from
# two positions in the same process: alone, and after two unrelated programs have filled the store.
#
# ⚠ **THE CORPUS'S OWN GOLDENS CANNOT MAKE THAT COMPARISON, FOR TWO SEPARATE REASONS, AND THAT IS THE
# GAP.** First, a golden mismatch is a `note:` and never a failure (⚖ user ruling 2026-08-02), so CHECK
# 4 above — which reads `0 failed` — cannot see one at all. Second, the committed corpus already
# carries hundreds of drifting goldens for unrelated reasons, so even a human reading that note is
# reading a signal buried in a crowd. The three programs below get a reference minted MINUTES earlier
# by THIS binary, in a corpus whose drift is zero by construction, and here the mismatch is a hard
# failure.
#
# ⚠ **AND THE REFERENCE HAS TO BE MINTED ONE PROGRAM PER PROCESS, WHICH IS THE WHOLE COST OF THIS
# CHECK.** Mint all three in one run and the second and third references are themselves produced by
# a served compile; cold and warm then move together and the diff is empty whatever is wrong. The
# three `--filter`ed runs below are three processes, and that is what makes them cold.
#
# The three programs are deliberately unlike each other — different declared type names, so their
# interner id assignments differ — and the third declares `type Clock`, which CONTESTS a stdlib type
# name and is the one input measured to move a stdlib file's swept output. It is compiled LAST, so if
# a contest could leak backwards into what the store already holds, this is where it would show.
#
# ⚠ `--workers=1` is not a gate on worker-count invariance (there is no such gate — see
# `.claude/CLAUDE.md`). It is the PROPERTY UNDER TEST: one worker is one process, which is what puts
# all three compiles behind one store. At the default pool the three would land in three workers and
# the check would silently measure nothing.
#
# ⚠ COMPARED is asserted to equal the case count, not merely "0 differ". A cold mint that failed to
# write leaves nothing to compare, and "0 of 0 differ" is the shape of a check that cannot fail.

AB_SPEC="$WORK/abspec"
mkdir -p "$AB_SPEC"
cat > "$AB_SPEC/shared-scan-ab.md" <<'SPEC'
---
feature: shared-scan-ab
status: stable
keywords: [shared memo, cross fragment, byte identity]
category: compiler
---

# Three unrelated programs through one process

## Documentation

What a program compiles to may not depend on what was compiled before it in the same process.

## Tests

<!-- test: alpha -->
```maxon
typealias Num = int(0 to 1000)
typealias Nums = Array with Num

type Alpha
	export var slot as Num

	export static function create(slot Num) returns Alpha
		return Self{slot: slot}
	end 'create'

	export function doubled() returns Num
		return self.slot * 2
	end 'doubled'
end 'Alpha'

function main() returns ExitCode
	var xs = Nums.create()
	xs.push(7)
	let a = Alpha.create(try xs.get(0) otherwise 0)
	print("alpha {a.doubled()}\n")
	return 0
end 'main'
```
```stdout
alpha 14
```

<!-- test: beta -->
```maxon
typealias Count = int(0 to 500)

enum Mood
	calm
	loud
end 'Mood'

type Beta
	export var tally as Count

	export static function create(tally Count) returns Beta
		return Self{tally: tally}
	end 'create'
end 'Beta'

function describe(m Mood) returns String
	return match m 'm'
		calm gives "calm"
		loud gives "loud"
	end 'm'
end 'describe'

function main() returns ExitCode
	let b = Beta.create(3)
	print("beta {b.tally} {describe(Mood.loud)}\n")
	return 0
end 'main'
```
```stdout
beta 3 loud
```

<!-- test: clock-shadow -->
```maxon
typealias Tick = int(0 to 99)

type Clock
	export var tick as Tick

	export static function create(tick Tick) returns Clock
		return Self{tick: tick}
	end 'create'
end 'Clock'

function main() returns ExitCode
	let c = Clock.create(9)
	print("clock {c.tick}\n")
	return 0
end 'main'
```
```stdout
clock 9
```
SPEC

# The case names, once. `AB_CASES` is COUNTED from this list rather than written down beside it: the
# count is what every assertion below compares against, and a second spelling of it is a gate that
# stops matching the spec the moment a fourth program is added.
AB_PROGRAMS="alpha beta clock-shadow"
AB_CASES=$(echo $AB_PROGRAMS | wc -w)

ab_cold_failed=0
for program in $AB_PROGRAMS; do
	# One program per PROCESS, so its reference is minted by a compile the store could not have served.
	if ! "$SHV2" spec-test "$AB_SPEC" --filter="shared-scan-ab/$program" > "$WORK/ab-cold-$program.log" 2>&1; then
		ab_cold_failed=$((ab_cold_failed + 1))
		echo "       cold mint of '$program' exited non-zero:"
		grep -E '^FAIL|^error' "$WORK/ab-cold-$program.log" | head -5 | sed 's/^/       /'
	fi
done

"$SHV2" spec-test "$AB_SPEC" --workers=1 > "$WORK/ab-warm.log" 2>&1
ab_warm_rc=$?
ab_summary=$(grep -E '^[0-9]+ passed, [0-9]+ failed' "$WORK/ab-warm.log" | tail -1)
ab_note=$(grep -o '[0-9]* COMPARED against a committed reference ([0-9]* of them differ' "$WORK/ab-warm.log" | tail -1)
ab_compared=$(echo "$ab_note" | grep -o '^[0-9]*')
ab_differ=$(echo "$ab_note" | grep -o '([0-9]*' | grep -o '[0-9]*')
ab_compared=${ab_compared:-0}
ab_differ=${ab_differ:--1}

if [ "$ab_cold_failed" -ne 0 ]; then
	fail "CHECK 5: $ab_cold_failed of $AB_CASES cold reference mint(s) failed — there is nothing to compare warm against"
elif [ "$ab_warm_rc" -ne 0 ] || ! echo "$ab_summary" | grep -q ', 0 failed'; then
	fail "CHECK 5: the shared run exited $ab_warm_rc (${ab_summary:-no summary line})"
	grep -E '^FAIL' "$WORK/ab-warm.log" | head -10 | sed 's/^/       /'
elif [ "$ab_compared" -ne "$AB_CASES" ]; then
	fail "CHECK 5: $ab_compared of $AB_CASES program(s) had a cold reference to compare against — a check over fewer than all of them cannot fail"
elif [ "$ab_differ" -ne 0 ]; then
	fail "CHECK 5: $ab_differ of $AB_CASES program(s) emit different code warm than they do cold — what a program compiles to depends on what was compiled before it"
	grep -n 'no longer match what the compiler emits' -A 6 "$WORK/ab-warm.log" | sed 's/^/       /'
else
	pass "CHECK 5: all $AB_CASES program(s) emit byte-identical code whether compiled alone or behind two unrelated compiles"
fi

echo
if [ "$failures" -eq 0 ]; then
	echo "shared-stdlib-memo-gate: PASS"
	exit 0
fi
echo "shared-stdlib-memo-gate: FAIL ($failures check(s))"
exit 1
