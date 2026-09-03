#!/usr/bin/env bash
# The seven BYTE/ASCII `String` METHODS (P1.8 Slice C) — and the two DIFFERENT questions they raise,
# which is why this generator has two families of mode and not one.
#
# ⚠ **`ScaleCorpus` IS BLIND TO THE SEVEN METHODS, AND NOT BLIND TO `String`. BOTH HALVES MATTER.**
# Dump the corpus (`scale-test --emit-corpus=<dir>`) and enumerate every method call it emits and the
# whole list is: `create` `push` `count` `get` `append` `scaleBy` `probe` `byteLength` `slice` `reserve`
# `clone` `firstVal`. NOT ONE of `startsWith` / `endsWith` / `contains` / `toLower` / `toUpper` /
# `replace` / `split` appears at any rung — so their parser dispatch, their eight runtime graphs and
# their `RuntimeUsage` closure are STRUCTURALLY invisible to a default `scale-test` run, in allocations,
# in bytes and in CPU ticks alike.
#   ⇒ But `s_strings.maxon` DOES drive the String dimension — 756 `==` sites plus `append` /
#     `byteLength` / interpolation across the six rungs — which is why the rung's committed
#     −181 allocations / −8,132 bytes WAS visible: that delta is the shared `__str_eq` / `String.equals`
#     byte loop getting smaller, i.e. the ONE part of Slice C the corpus can express. A reader who takes
#     that non-zero as coverage of the rung repeats E3070's mistake in reverse: there, a corpus that
#     contained the shape at degree 1 read as coverage; here, a corpus that contains a NEIGHBOUR of the
#     shape reads as coverage. Neither is.
#
# THE TWO FAMILIES:
#
#   `sites-*` — a COMPILE ladder. `<n>` is the number of METHOD CALL SITES, so the question is the
#              mandate's: does the COMPILER stay linear in program size when the program is made of
#              these calls? Every mode holds the site's shape fixed and doubles the count, so a
#              rung-over-rung ×2 is linear and ×4 is quadratic, read straight off `--metrics`.
#              `sites-control` is THE CONTROL and the most important of them: the same program shape
#              built from the P1.2 String surface only (`==`, `append`, `byteLength`) and not one of the
#              seven methods. It is what the rung costs a program that never uses it — the shape the
#              P1.7a 2b-i pass found (a union that heap-boxed to answer "no conformer", taxing every
#              struct method call in every program). Slice C's candidate for that tax is real and named:
#              `recordCallUsage` now calls `recordStringMethodUsage` → `closeStringMethodNeeds` on EVERY
#              call op in the module, String-related or not. Against a PARENT binary on this mode, at a
#              fixed `<n>`, that tax is what is being priced.
#
#   `data-*`  — a RUN ladder, the odd family out (`genfsprobe.sh`'s footing). `<n>` is a number of
#              DOUBLINGS of the subject string, so the data doubles and the SOURCE does not, and the
#              program brackets ONLY the operation with `__Builtins.currentTimeNanos()` and prints a CSV
#              line. It answers what no compile ladder can: are the emitted runtime GRAPHS linear in the
#              data? `split` restarting its search from 0 per segment would be quadratic in segments;
#              `replace` sizing its output by growth instead of by the two-pass count would be another;
#              and `__str_find` is a NAIVE search, so it has a genuine O(hay x needle) term that a
#              first-byte fast reject hides on every realistic input.
#
# ⚠ **THE SETUP IS `s.append(s)`, NOT AN APPEND LOOP — AND THE REASON GIVEN HERE WAS STALE FOR A LONG
# TIME.** It read: *"`__str_append` allocates a buffer of EXACTLY the required length on every grow, so
# building an N-byte string by N appends copies O(N^2) bytes"*. That was true of the FIRST `__str_append`
# and was cured inside it (growth became `2 * requiredLen`) long before wave 8 retired the entry point
# altogether; `String.append` reaches `__arr_append` -> `__arr_reserve` -> `__arr_grow` ->
# `__arr_grown_cap` now, which is geometric too. **MEASURED (W49 wave 8, min-of-7, both binaries in one
# path)**: `data-appendloop` reads x1.93 x2.06 x1.95 per doubling on the BASE compiler and x1.94 x2.07
# x1.95 on the tip — LINEAR on both sides, not the quadratic this paragraph predicted.
# ⇒ The self-append setup is kept anyway, and now for an honest reason: it reaches the final length in
# `<n>` STEPS rather than `<n>` doublings' worth of them, so the SETUP cost stays off the timed region
# whatever the growth policy is. A future policy change cannot make the setup quadratic again.
#
# Usage: genstring.sh <n> <mode> <out>
#   sites-predicates | sites-case | sites-replace | sites-split | sites-control   (<n> = CALL SITES)
#   data-find | data-findquad | data-split | data-replace | data-case             (<n> = DOUBLINGS)
#   data-appendloop                                                               (<n> = DOUBLINGS)
#
#   e.g. genstring.sh 512 sites-split a.maxon   and   genstring.sh 1024 sites-split b.maxon
#        genstring.sh 512 sites-control c.maxon is the same size in the P1.2 surface only.
#        genstring.sh 12 data-split d.maxon     builds 9*2^12 = 36,864 bytes and 4,096 segments.
#
# The `data-*` programs each print ONE CSV line — `mode,bytes,units,nanos,reps` — where `units` is the
# count the operation's own cost should be linear in (segments for `split`, matches for `replace`, bytes
# otherwise). REPS (env, default 5) repeats the operation inside the timed region; wall nanos are used
# because shv2 has no thread-CPU intrinsic, so measure on an idle box and take the MINIMUM of several
# runs. Against the ×2-vs-×4 question that is ample; against a few percent it is worth nothing.
set -euo pipefail
N="$1"; MODE="$2"; OUT="$3"
REPS="${REPS:-5}"

case "$MODE" in
  sites-predicates|sites-case|sites-replace|sites-split|sites-control) FAMILY=sites ;;
  data-find|data-findquad|data-split|data-replace|data-case|data-appendloop) FAMILY=data ;;
  *) echo "genstring.sh: unknown mode '$MODE' (see header)" >&2; exit 2 ;;
esac

if [ "$N" -lt 1 ]; then echo "genstring.sh: <n> must be >= 1" >&2; exit 2; fi

# One SITE GROUP per function, so a function stays a constant size as `<n>` doubles and the register
# allocator's own curves stay out of the reading (they are measured by genwidelive.sh). Four sites per
# function: every `sites-case`/`sites-replace`/`sites-split` site binds an OWNED result that stays live
# to the function's end for its drop, so a wide group would be measuring E5001's edge instead.
SITES_PER_FN=4

emit_sites_program() {
	FNS=$(( N / SITES_PER_FN ))
	if [ "$FNS" -lt 1 ]; then FNS=1; fi

	echo "// string ladder: $N sites, mode $MODE — see tests/ladders/genstring.sh"
	echo "typealias Int = int(i64.min to i64.max)"
	echo "typealias ExitCode = int(0 to 255)"
	echo ""

	f=0
	while [ "$f" -lt "$FNS" ]; do
		echo "function fn${f}(s String, p String, q String) returns Int"
		echo -e "\tvar total = 0"
		n=0
		while [ "$n" -lt "$SITES_PER_FN" ]; do
			case "$MODE" in
			sites-predicates)
				# All three predicates, so no one runtime graph stands in for the family: `startsWith`
				# and `endsWith` are two symbols from ONE builder and `contains` is the `__str_find`
				# consumer.
				echo -e "\tif s.startsWith(p) 'a${n}'"
				echo -e "\t\ttotal = total + 1"
				echo -e "\tend 'a${n}'"
				echo -e "\tif s.endsWith(q) 'b${n}'"
				echo -e "\t\ttotal = total + 2"
				echo -e "\tend 'b${n}'"
				echo -e "\tif s.contains(p) 'c${n}'"
				echo -e "\t\ttotal = total + 3"
				echo -e "\tend 'c${n}'"
				;;
			sites-case)
				echo -e "\tlet lo${n} = s.toLower()"
				echo -e "\tlet hi${n} = s.toUpper()"
				echo -e "\ttotal = total + lo${n}.byteLength() + hi${n}.byteLength()"
				;;
			sites-replace)
				echo -e "\tlet r${n} = s.replace(p, with: q)"
				echo -e "\ttotal = total + r${n}.byteLength()"
				;;
			sites-split)
				# The one site that mints an `Array with String` per call — `arrayInstanceForString()`
				# is re-interned at EVERY site, so this mode is where a per-site registry cost shows.
				echo -e "\tlet parts${n} = s.split(p)"
				echo -e "\ttotal = total + parts${n}.count()"
				;;
			sites-control)
				# THE CONTROL: the P1.2 wave D String surface and nothing from Slice C. Same function
				# shape, same call-op density, so the difference against a PARENT binary is the tax the
				# seven methods levy on a program that does not use them.
				echo -e "\tif s == p 'e${n}'"
				echo -e "\t\ttotal = total + 1"
				echo -e "\tend 'e${n}'"
				echo -e "\tvar acc${n} = q"
				echo -e "\tacc${n}.append(p)"
				echo -e "\ttotal = total + acc${n}.byteLength()"
				;;
			esac
			n=$(( n + 1 ))
		done
		echo -e "\treturn total"
		echo "end 'fn${f}'"
		echo ""
		f=$(( f + 1 ))
	done

	echo "function main() returns ExitCode"
	echo -e "\tvar sum = 0"
	f=0
	while [ "$f" -lt "$FNS" ]; do
		echo -e "\tsum = sum + fn${f}(\"alpha,beta,gamma\", p: \",\", q: \";\")"
		f=$(( f + 1 ))
	done
	echo -e "\treturn 0 if sum >= 0 else 1"
	echo "end 'main'"
}

emit_data_program() {
	echo "// string ladder: $N doublings, mode $MODE — see tests/ladders/genstring.sh"
	echo "typealias Int = int(i64.min to i64.max)"
	echo "typealias ExitCode = int(0 to 255)"
	echo ""
	echo "// Self-append doubling: O(final length) in total, because __str_append reallocates to the EXACT"
	echo "// required length and a chunk-at-a-time loop would therefore be quadratic in the setup alone."
	echo "function grown(seed String, doublings Int) returns String"
	echo -e "\tvar s = seed"
	echo -e "\tvar d = 0"
	echo -e "\twhile d < doublings 'dbl'"
	echo -e "\t\ts.append(s)"
	echo -e "\t\td = d + 1"
	echo -e "\tend 'dbl'"
	echo -e "\treturn s"
	echo "end 'grown'"
	echo ""
	echo "function main() returns ExitCode"
	echo -e "\tvar total = 0"

	case "$MODE" in
	data-find)
		# The realistic search: a needle whose first byte occurs NOWHERE in the haystack, so the
		# first-byte fast reject answers every position and the scan is one pass.
		echo -e "\tlet hay = grown(\"abcdefgh,\", doublings: $N)"
		echo -e "\tlet t0 = __Builtins.currentTimeNanos()"
		echo -e "\tvar r = 0"
		echo -e "\twhile r < $REPS 'rep'"
		echo -e "\t\tif hay.contains(\"zzz\") 'hit'"
		echo -e "\t\t\ttotal = total + 1"
		echo -e "\t\tend 'hit'"
		echo -e "\t\tr = r + 1"
		echo -e "\tend 'rep'"
		echo -e "\tlet t1 = __Builtins.currentTimeNanos()"
		echo -e "\tprint(\"$MODE,{hay.byteLength()},{hay.byteLength()},{t1 - t0},$REPS\\\\n\")"
		;;
	data-findquad)
		# ⚠ THE WORST CASE THAT DEFEATS THE FAST REJECT, and the ONLY shape here that is meant to bend.
		# Haystack is all 'a', needle is half a haystack of 'a' with ONE trailing 'b', so the first byte
		# matches at every position and each verification runs the whole needle before failing on its
		# last byte: (hay/2) positions x (hay/2) bytes. It needs a needle whose LENGTH SCALES WITH THE
		# HAYSTACK — a fixed needle makes this a constant factor, not a curve.
		echo -e "\tlet hay = grown(\"aaaaaaaa\", doublings: $N)"
		echo -e "\tvar needle = grown(\"aaaaaaaa\", doublings: $(( N - 1 )))"
		echo -e "\tneedle.append(\"b\")"
		echo -e "\tlet t0 = __Builtins.currentTimeNanos()"
		echo -e "\tvar r = 0"
		echo -e "\twhile r < $REPS 'rep'"
		echo -e "\t\tif hay.contains(needle) 'hit'"
		echo -e "\t\t\ttotal = total + 1"
		echo -e "\t\tend 'hit'"
		echo -e "\t\tr = r + 1"
		echo -e "\tend 'rep'"
		echo -e "\tlet t1 = __Builtins.currentTimeNanos()"
		echo -e "\tprint(\"$MODE,{hay.byteLength()},{needle.byteLength()},{t1 - t0},$REPS\\\\n\")"
		;;
	data-split)
		# SEGMENTS, the knob the two-pass question is about: if the search restarted from position 0 for
		# each segment the total would be quadratic in them.
		echo -e "\tlet hay = grown(\"abcdefgh,\", doublings: $N)"
		echo -e "\tvar units = 0"
		echo -e "\tlet t0 = __Builtins.currentTimeNanos()"
		echo -e "\tvar r = 0"
		echo -e "\twhile r < $REPS 'rep'"
		echo -e "\t\tlet parts = hay.split(\",\")"
		echo -e "\t\tunits = parts.count()"
		echo -e "\t\ttotal = total + units"
		echo -e "\t\tr = r + 1"
		echo -e "\tend 'rep'"
		echo -e "\tlet t1 = __Builtins.currentTimeNanos()"
		echo -e "\tprint(\"$MODE,{hay.byteLength()},{units},{t1 - t0},$REPS\\\\n\")"
		;;
	data-replace)
		# MATCHES. The replacement is LONGER than the needle, so the result is a different size from the
		# receiver and the two-pass count is actually load-bearing rather than incidentally right.
		echo -e "\tlet hay = grown(\"abcdefgh,\", doublings: $N)"
		echo -e "\tvar units = 0"
		echo -e "\tlet t0 = __Builtins.currentTimeNanos()"
		echo -e "\tvar r = 0"
		echo -e "\twhile r < $REPS 'rep'"
		echo -e "\t\tlet out = hay.replace(\",\", with: \";;\")"
		echo -e "\t\tunits = out.byteLength() - hay.byteLength()"
		echo -e "\t\ttotal = total + units"
		echo -e "\t\tr = r + 1"
		echo -e "\tend 'rep'"
		echo -e "\tlet t1 = __Builtins.currentTimeNanos()"
		echo -e "\tprint(\"$MODE,{hay.byteLength()},{units},{t1 - t0},$REPS\\\\n\")"
		;;
	data-appendloop)
		# ⚠ NOT A SLICE C METHOD — the P1.2 wave D `append`, here because it is the one shape the rest of
		# this generator has to ROUTE AROUND (see the self-append note in the header) and a claim about it
		# should carry a number. Doubling the CHUNK COUNT is the knob: linear growth reads x2 and a
		# reallocate-to-exact-length policy would read x4. **It reads x2**, and that is the instrument for
		# the one property a `String.append` retirement could silently destroy — see the header.
		echo -e "\tvar chunks = 1024"
		echo -e "\tvar d = 0"
		echo -e "\twhile d < $N 'dbl'"
		echo -e "\t\tchunks = chunks * 2"
		echo -e "\t\td = d + 1"
		echo -e "\tend 'dbl'"
		echo -e "\tlet t0 = __Builtins.currentTimeNanos()"
		echo -e "\tvar s = \"\""
		echo -e "\tvar i = 0"
		echo -e "\twhile i < chunks 'grow'"
		echo -e "\t\ts.append(\"abcdefgh,\")"
		echo -e "\t\ti = i + 1"
		echo -e "\tend 'grow'"
		echo -e "\tlet t1 = __Builtins.currentTimeNanos()"
		echo -e "\ttotal = total + s.byteLength()"
		echo -e "\tprint(\"$MODE,{s.byteLength()},{chunks},{t1 - t0},1\\\\n\")"
		;;
	data-case)
		echo -e "\tlet hay = grown(\"abcdefgh,\", doublings: $N)"
		echo -e "\tlet t0 = __Builtins.currentTimeNanos()"
		echo -e "\tvar r = 0"
		echo -e "\twhile r < $REPS 'rep'"
		echo -e "\t\tlet up = hay.toUpper()"
		echo -e "\t\ttotal = total + up.byteLength()"
		echo -e "\t\tr = r + 1"
		echo -e "\tend 'rep'"
		echo -e "\tlet t1 = __Builtins.currentTimeNanos()"
		echo -e "\tprint(\"$MODE,{hay.byteLength()},{hay.byteLength()},{t1 - t0},$REPS\\\\n\")"
		;;
	esac

	echo -e "\treturn 0 if total >= 0 else 1"
	echo "end 'main'"
}

if [ "$FAMILY" = "sites" ]; then
	emit_sites_program > "$OUT"
else
	if [ "$MODE" = "data-findquad" ] && [ "$N" -lt 1 ]; then
		echo "genstring.sh: data-findquad needs <n> >= 1 (the needle is one doubling behind)" >&2; exit 2
	fi
	emit_data_program > "$OUT"
fi
