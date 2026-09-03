#!/usr/bin/env bash
# `Character`, UAX#29 GRAPHEME SEGMENTATION, `String.count()` and `for c in s` (P1.8 slice B) — and the
# two DIFFERENT questions they raise, which is why this generator has two families of mode and not one.
# It is `genstring.sh`'s sibling and deliberately its twin in shape; read that file's header first.
#
# ⚠ **`ScaleCorpus` IS BLIND TO EVERY LAST PIECE OF THIS RUNG. MEASURED, NOT ASSUMED.**
# Dump the corpus (`scale-test --emit-corpus=<dir>`) and enumerate it at rung 5:
#
#     `.count()`  1024 sites — EVERY ONE of them an `Array`/`Set` receiver (`u_arrays.maxon`,
#                 `v_marray.maxon`). NOT ONE on a `String`.
#     `.bytes()`  ZERO sites.
#     `for … in`  ZERO sites of ANY form — the corpus still has no `for` loop at all, which is the same
#                 hole `genfor.sh` was built for at slice A and it has not moved.
#     `'…'`       53,766 single-quoted tokens, and 0 of them in a VALUE position: every one is a BLOCK
#                 LABEL. So the `charLiteral` LEXER path is hammered and the literal-directed TYPING path
#                 (`integerizedOperand`) is never entered.
#
# The whole method inventory of the corpus is `create` `push` `count` `get` `append` `scaleBy` `probe`
# `byteLength` `slice` `reserve` `clone` `firstVal` — the same twelve slice C found, with nothing added.
# ⇒ `String.count()`, `.bytes()`, `for c in s`, `__gr_end`/`__gr_count`/`__char_at`/`__gbp`, the whole
# `RuntimeUsage` grapheme closure and `integerizedOperand` are STRUCTURALLY invisible to a default
# `scale-test` run, in allocations, in bytes and in CPU ticks alike. A Δ0 from it is the instrument's
# blind spot and not a result.
#   ⇒ ⚠ AND THE CONVERSE MATTERS TOO, which is what slice C's optimizer got right by checking BOTH
#     directions: the corpus is NOT blind to `String` (756 `==` sites plus `append`/`byteLength`/
#     interpolation in `s_strings.maxon`) and is NOT blind to the `charLiteral` token. A reading that
#     moves could therefore still be real — it just cannot be about the grapheme surface, because the
#     corpus contains none of it.
#
# THE TWO FAMILIES:
#
#   `sites-*` — a COMPILE ladder. `<n>` is the number of CALL SITES, so the question is the mandate's:
#              does the COMPILER stay linear in program size when the program is made of these? Every
#              mode holds the site's shape fixed and doubles the count, so rung-over-rung ×2 is linear
#              and ×4 is quadratic, read straight off `--metrics`.
#              `sites-control` is THE CONTROL and the most important of them: the same program shape with
#              not one Character in it. It prices what the rung costs a program that never uses it —
#              `recordGraphemeUsage` runs eight `ByteArray.equals` plus `closeGraphemeNeeds` on EVERY
#              call op in the module, grapheme-related or not, and `parseBinary` now asks
#              `integerizedOperand` about BOTH operands of EVERY binary operator. Against a PARENT binary at
#              a fixed `<n>`, that tax is what is being priced. (It is the shape the P1.7a 2b-i pass
#              found: a union that heap-boxed to answer "no conformer", taxing every struct method call
#              in every program.)
#
#   `data-*`  — a RUN ladder. `<n>` is a number of DOUBLINGS of the subject string, so the DATA doubles
#              and the SOURCE does not, and the program brackets ONLY the operation with
#              `__Builtins.currentTimeNanos()` and prints a CSV line. It answers what no compile ladder
#              can: are the emitted runtime GRAPHS linear in the data? `__gr_end` re-scanning from byte 0
#              would be quadratic in `for c in s`; a `for` loop that recomputed the cluster end in the
#              step block would double the constant; `__gr_count` is O(n) BY DESIGN, so `data-countloop`
#              exists to show what that costs a user who calls it inside a loop.
#
# ⚠ **THE SETUP IS `s.append(s)`, NOT AN APPEND LOOP, AND THAT IS LOAD-BEARING** — `genstring.sh`'s
# reason, unchanged: `__str_append` reallocates to the EXACT required length on every grow, so building
# an N-byte string by N appends copies O(N²) bytes and would swamp every reading below with a quadratic
# that is not the one being measured. Self-append doubles the length per step.
#
# ⛔⛔ **DO NOT RUN A `data-*` PROGRAM THROUGH THE C# BOOTSTRAP AND BELIEVE ITS NUMBERS: THE BOOTSTRAP
# MISCOMPILES `s.append(s)` AFTER THE BUFFER GROWS.** Found by this ladder, 2026-07-28, and it is a
# BOOTSTRAP defect — shv2 is the one that is right. MEASURED, minimal case:
#
#     var s = "éà"        # 4 bytes
#     s.append(s)         # 8 bytes  — still correct
#     s.append(s)         # 16 bytes — CORRUPT
#
# The bootstrap reports `byteLength() == 16` (right) but the content is `éàéà` followed by 8 bytes of
# fill: `s == "éàéàéàéà"` is **false**, and `s.count()` answers **12** instead of 8 because the lone
# continuation bytes segment as clusters of their own. shv2 answers 16 / true / 8. It is the SECOND
# self-append that breaks — the first is fine — which is the signature of a grow that copies from the
# receiver's STALE buffer pointer after reallocating it, i.e. the aliasing case `s.append(s)` is.
#   ⇒ The wrong count is a SYMPTOM, not the bug: `count()` is reading real garbage.
#   ⇒ ⚠ `genstring.sh` uses the same `grown()` setup, so this applies to that generator too.
#   ⇒ An ASCII seed hides it in the byte count (the fill is ASCII, so cluster count still equals byte
#     length and an ASCII `count()`/iteration TIMING through the bootstrap is still apples-to-apples);
#     a MULTI-BYTE seed does not, and any bootstrap reading from `data-*-wide`/`-zwj`/`-flag` is
#     measuring a mostly-ASCII string and must be discarded.
#
# ⚠ **AND THE SEED IS WHAT PICKS THE PATH THROUGH `__gr_end`, so the seeds are the real knobs here.**
# `__gr_end` has an ASCII fast path (two byte loads and a compare, no codepoint decode, no property
# lookup, no state machine) and a GENERAL SCAN behind it. An all-ASCII ladder measures the fast path and
# says nothing whatever about UAX#29; a ladder of only ZWJ families measures the state machine and says
# nothing about the fast path. Both are needed, and their RATIO is the per-cluster cost of the rules.
#
# Usage: genstring-grapheme.sh <n> <mode> <out>
#   sites-iter | sites-count | sites-charlit | sites-bytes | sites-control      (<n> = CALL SITES)
#   data-iter-ascii | data-iter-wide | data-iter-zwj | data-iter-flag           (<n> = DOUBLINGS)
#   data-count-ascii | data-count-wide | data-count-zwj                         (<n> = DOUBLINGS)
#   data-countloop | data-bytes                                                 (<n> = DOUBLINGS)
#
#   e.g. genstring-grapheme.sh 512 sites-iter a.maxon
#        genstring-grapheme.sh 512 sites-control c.maxon   (same size, no Character surface)
#        genstring-grapheme.sh 14 data-iter-ascii d.maxon  (8*2^14 = 131,072 bytes)
#
# --- WHAT IT READ ON THE DAY IT WAS BUILT (2026-07-28, commit `6313a6ead`), so a later run has a
#     BEFORE to compare against rather than only a shape to re-derive ---
#
#   COMPILE (`--metrics` total, 64 → 1,024 sites): every mode LINEAR. allocations ×1.93/1.96/1.98/1.99
#   (`sites-charlit`), ×1.87…×1.96 (`sites-iter`), ×1.85…×1.98 (`sites-control`); CPU the same to within
#   the noise band. Per phase — parse, semanticCheck, regalloc, encode — all ×2. Nothing bends.
#
#   RUN, per cluster at the top rung: `for c in s` 57.5 ns (ASCII) / 88.6 ns (2-byte) / 109.2 ns (flag) /
#   255.6 ns (ZWJ family); `count()` 3.4 ns (ASCII) / 32.4 ns (2-byte) / 172.0 ns (ZWJ). Every ladder ×2
#   per doubling. The ASCII fast path in `__gr_end` is worth ~9× a codepoint, and the per-CODEPOINT cost
#   of the general scan is flat (24.6 ns inside a 7-codepoint ZWJ cluster vs 32.4 ns for a lone one), so
#   the GB rules' state machine hides no per-trip cost.
#
#   ⭐ **`for c in s` COSTS 17× WHAT `count()` COSTS PER CLUSTER, AND ALL OF IT IS `__char_at`.** The
#   difference between the two is ~54 ns per cluster and it is CONSTANT in the segmentation work
#   (54.1 ASCII / 56.2 two-byte / 83.6 for a 25-byte cluster) — the signature of one alloc + blit + free
#   per trip, not of any scan. ⚠ And the slab NEVER GIVES IT BACK (`MmRuntime`: bump allocator, no free
#   list), so the memory is linear in ITERATIONS rather than in live data: `data-iter-ascii` peaks at
#   11.6 / 43.7 / 97.1 / 192.6 / 398.7 MB down the rungs, ~82 bytes per minted Character, while
#   `data-count-ascii` — the same `__gr_end` scan minting nothing — is FLAT at 3.7 MB across a 4× data
#   range. The C# bootstrap runs the same source in the same time (155.7 ms vs shv2's 156.4 ms) at
#   **7.3 MB**. That is the control that makes this shv2's, not the algorithm's.
#
# The `data-*` programs each print ONE CSV line — `mode,bytes,units,nanos,reps` — where `units` is the
# count the operation's cost should be linear in (CLUSTERS for every mode here, bytes for `data-bytes`).
# REPS (env, default 5) repeats the operation inside the timed region; wall nanos are used because shv2
# has no thread-CPU intrinsic, so measure on an idle box and take the MINIMUM of several runs. Against
# the ×2-vs-×4 question that is ample; against a few percent it is worth nothing.
set -euo pipefail
N="$1"; MODE="$2"; OUT="$3"
REPS="${REPS:-5}"

case "$MODE" in
  sites-iter|sites-count|sites-charlit|sites-bytes|sites-control) FAMILY=sites ;;
  data-iter-ascii|data-iter-wide|data-iter-zwj|data-iter-flag) FAMILY=data ;;
  data-count-ascii|data-count-wide|data-count-zwj) FAMILY=data ;;
  data-countloop|data-bytes) FAMILY=data ;;
  *) echo "genstring-grapheme.sh: unknown mode '$MODE' (see header)" >&2; exit 2 ;;
esac

if [ "$N" -lt 1 ]; then echo "genstring-grapheme.sh: <n> must be >= 1" >&2; exit 2; fi

# One SITE GROUP per function, `genstring.sh`'s reason: a function stays a constant size as `<n>` doubles,
# so the register allocator's own curves stay out of the reading. Two per function rather than four
# because a `for c in s` site is a whole loop nest, not an expression.
SITES_PER_FN=2

# --- THE FOUR SEEDS, and what each one makes `__gr_end` do ---
#
#   ASCII  8 bytes  = 8 clusters   — the fast path: lead < 0x80, next byte ASCII, return. No decode.
#   WIDE   8 bytes  = 4 clusters   — U+00E9/U+00E0, two bytes each: the GENERAL SCAN, one codepoint per
#                                    cluster, so it prices the decode + property lookup + rule per
#                                    cluster with no state carried between codepoints.
#   ZWJ    25 bytes = 1 cluster    — a four-person family joined by three U+200D: the state machine's
#                                    longest realistic run, and the mode where CLUSTERS grow far slower
#                                    than BYTES (this is the "few long clusters" knob).
#   FLAG   8 bytes  = 1 cluster    — TWO regional indicators: GB12/GB13's RI-parity state, which is the
#                                    only rule carrying a counter rather than a flag.
#
# ⚠ The self-append doubling preserves each seed's cluster/byte ratio EXCEPT at the seam, and the seam is
# where two of these get interesting: appending a flag to a flag makes FOUR RIs, which UAX#29 pairs as
# TWO clusters and not one, so `data-iter-flag`'s cluster count doubles cleanly. The ZWJ seed's seam is a
# 👦 followed by a 👨, which is a break (GB11 needs the ZWJ), so it too doubles cleanly.
ascii_seed() { printf 'abcdefgh'; }
wide_seed()  { printf '\xc3\xa9\xc3\xa0\xc3\xa9\xc3\xa0'; }
zwj_seed()   { printf '\xf0\x9f\x91\xa8\xe2\x80\x8d\xf0\x9f\x91\xa9\xe2\x80\x8d\xf0\x9f\x91\xa7\xe2\x80\x8d\xf0\x9f\x91\xa6'; }
flag_seed()  { printf '\xf0\x9f\x87\xba\xf0\x9f\x87\xb8'; }

emit_sites_program() {
	FNS=$(( N / SITES_PER_FN ))
	if [ "$FNS" -lt 1 ]; then FNS=1; fi

	echo "// grapheme ladder: $N sites, mode $MODE — see tests/ladders/genstring-grapheme.sh"
	echo "typealias Int = int(i64.min to i64.max)"
	echo "typealias ExitCode = int(0 to 255)"
	echo ""

	f=0
	while [ "$f" -lt "$FNS" ]; do
		echo "function fn${f}(s String, p String) returns Int"
		echo -e "\tvar total = 0"
		if [ "$MODE" = "sites-charlit" ]; then
			# The Character the literals are compared AGAINST. Minted once per function from a lone literal
			# rather than from a segmentation scan, so the function's cost is dominated by the comparison
			# sites. ⚠ **The multi-byte spelling is HISTORY, not a requirement.** Under the WIDTH RULE (gone
			# at A5m-ab) a one-byte literal materialized as an `int`, so `'é'` was the ONE spelling that
			# minted a Character without a loop; every character literal is a `Character` now, so `'a'`
			# would serve equally. It is left multi-byte so a reading taken against a PARENT binary stays
			# apples-to-apples with the ones recorded below.
			echo -e "\tvar ch = 'é'"
		fi
		n=0
		while [ "$n" -lt "$SITES_PER_FN" ]; do
			case "$MODE" in
			sites-iter)
				# The whole loop nest: header phi, `__gr_end`, `__char_at`, the per-trip owned frame and its
				# drop. This is the site the parser's `parseForStatement` String arm builds.
				echo -e "\tfor c${n} in s 'i${n}'"
				echo -e "\t\tif c${n} == 'é' 'h${n}'"
				echo -e "\t\t\ttotal = total + 1"
				echo -e "\t\tend 'h${n}'"
				echo -e "\tend 'i${n}'"
				;;
			sites-count)
				echo -e "\ttotal = total + s.count()"
				echo -e "\ttotal = total + p.count()"
				;;
			sites-charlit)
				# ⭐ THE LITERAL-DIRECTED TYPING PATH, and nothing else. `integerizedOperand` is asked of
				# BOTH operands of every binary operator in the module, each against what the OTHER side is,
				# so this mode prices that PROBE at the shape it is asked of most: a Character binding
				# meeting a lone `charLiteral` token. ⚠ **The probe DECLINES here, and that is the reading.**
				# Since A5m-ab every character literal materializes as a `Character`, so both sides are
				# already Characters, the expected tag is not integral and the door returns at its first
				# test — what this mode measures is the per-operand probe plus the literal's own `.rdata`
				# record, not a rewrite. (Under the WIDTH RULE this was the opposite shape: the literal came
				# out an `int` and the old `characterizedOperand` re-materialized it as a Character. That
				# function is gone; the direction that survives converts a literal to its CODEPOINT in an
				# INTEGER-expecting position, and rewrites the emitted `stringLiteral` op IN PLACE rather
				# than leaving one behind for a const DCE that does not run on it — see
				# `Parser.retypeCharLiteralAsCodepoint`. To measure THAT arm, compare against an integral
				# operand: `total - '0'`.) `sites-control` is the same site count with the same operator and
				# no Character, which is what isolates this.
				echo -e "\tif ch == 'a' 'k${n}'"
				echo -e "\t\ttotal = total + 1"
				echo -e "\tend 'k${n}'"
				echo -e "\tif 'b' == ch 'l${n}'"
				echo -e "\t\ttotal = total + 2"
				echo -e "\tend 'l${n}'"
				;;
			sites-bytes)
				echo -e "\tlet b${n} = s.bytes()"
				echo -e "\ttotal = total + b${n}.count()"
				;;
			sites-control)
				# THE CONTROL: the P1.2 wave D String surface and NOT ONE Character. Same function shape,
				# same call-op density, same comparison count — so the difference against a PARENT binary is
				# the tax slice B levies on a program that does not use it.
				echo -e "\tif s == p 'e${n}'"
				echo -e "\t\ttotal = total + 1"
				echo -e "\tend 'e${n}'"
				echo -e "\tvar acc${n} = p"
				echo -e "\tacc${n}.append(s)"
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
		echo -e "\tsum = sum + fn${f}(\"alpha beta\", p: \"gamma\")"
		f=$(( f + 1 ))
	done
	echo -e "\treturn 0 if sum >= 0 else 1"
	echo "end 'main'"
}

# The seed literal and the human name of the unit, per data mode.
seed_for_mode() {
	case "$1" in
	data-iter-ascii|data-count-ascii|data-countloop|data-bytes) ascii_seed ;;
	data-iter-wide|data-count-wide) wide_seed ;;
	data-iter-zwj|data-count-zwj) zwj_seed ;;
	data-iter-flag) flag_seed ;;
	esac
}

emit_data_program() {
	echo "// grapheme ladder: $N doublings, mode $MODE — see tests/ladders/genstring-grapheme.sh"
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
	echo -e "\tlet subject = grown(\"$(seed_for_mode "$MODE")\", doublings: $N)"

	case "$MODE" in
	data-iter-*)
		# ⭐ THE SHAPE THE WHOLE RUNG TURNS ON. Every trip calls `__gr_end(s, pos)` and `__char_at`; the
		# step advances the byte index TO the cluster end rather than by one. If anything re-derived the
		# cluster end — a step block that called `__gr_end` again, or a `__gr_end` that scanned from 0 —
		# this reads ×4 per doubling. `units` is the CLUSTER count, which is what the loop's trip count is
		# and therefore what its cost should be linear in.
		echo -e "\tvar units = 0"
		echo -e "\tlet t0 = __Builtins.currentTimeNanos()"
		echo -e "\tvar r = 0"
		echo -e "\twhile r < $REPS 'rep'"
		echo -e "\t\tunits = 0"
		echo -e "\t\tfor c in subject 'walk'"
		echo -e "\t\t\tunits = units + 1"
		echo -e "\t\tend 'walk'"
		echo -e "\t\ttotal = total + units"
		echo -e "\t\tr = r + 1"
		echo -e "\tend 'rep'"
		echo -e "\tlet t1 = __Builtins.currentTimeNanos()"
		echo -e "\tprint(\"$MODE,{subject.byteLength()},{units},{t1 - t0},$REPS\\\\n\")"
		;;
	data-count-*)
		# `String.count()` — `__gr_count`, which is `__gr_end` in a loop. O(n) PER CALL by design (the
		# reference's `countGraphemes` is the same loop), so this ladder should read ×2 per doubling and a
		# ×4 would mean the scan restarts.
		echo -e "\tvar units = 0"
		echo -e "\tlet t0 = __Builtins.currentTimeNanos()"
		echo -e "\tvar r = 0"
		echo -e "\twhile r < $REPS 'rep'"
		echo -e "\t\tunits = subject.count()"
		echo -e "\t\ttotal = total + units"
		echo -e "\t\tr = r + 1"
		echo -e "\tend 'rep'"
		echo -e "\tlet t1 = __Builtins.currentTimeNanos()"
		echo -e "\tprint(\"$MODE,{subject.byteLength()},{units},{t1 - t0},$REPS\\\\n\")"
		;;
	data-countloop)
		# ⚠ **THE ONE MODE THAT IS MEANT TO BEND, AND IT BENDS IN USER CODE RATHER THAN IN THE COMPILER.**
		# `count()` is O(n) per call and this calls it once per cluster, so the program is O(n²) — exactly
		# what the reference does for the same source, and exactly what `String.count`'s own doc warns
		# about. It is here so the claim "matches the reference's cost" carries a number instead of a
		# reading, and so a future O(1)-on-ASCII `count()` has a before to be measured against.
		echo -e "\tvar units = 0"
		echo -e "\tlet t0 = __Builtins.currentTimeNanos()"
		echo -e "\tfor c in subject 'walk'"
		echo -e "\t\ttotal = total + subject.count()"
		echo -e "\t\tunits = units + 1"
		echo -e "\tend 'walk'"
		echo -e "\tlet t1 = __Builtins.currentTimeNanos()"
		echo -e "\tprint(\"$MODE,{subject.byteLength()},{units},{t1 - t0},1\\\\n\")"
		;;
	data-bytes)
		# `.bytes()` — `__str_to_bytes` pushing every byte into a caller-created `Array with Byte`. The
		# array grows by `Array`'s own doubling, so this is linear; it is measured because it is the one
		# member of the rung that allocates per BYTE rather than per CLUSTER.
		echo -e "\tvar units = 0"
		echo -e "\tlet t0 = __Builtins.currentTimeNanos()"
		echo -e "\tvar r = 0"
		echo -e "\twhile r < $REPS 'rep'"
		echo -e "\t\tlet bs = subject.bytes()"
		echo -e "\t\tunits = bs.count()"
		echo -e "\t\ttotal = total + units"
		echo -e "\t\tr = r + 1"
		echo -e "\tend 'rep'"
		echo -e "\tlet t1 = __Builtins.currentTimeNanos()"
		echo -e "\tprint(\"$MODE,{subject.byteLength()},{units},{t1 - t0},$REPS\\\\n\")"
		;;
	esac

	echo -e "\treturn 0 if total >= 0 else 1"
	echo "end 'main'"
}

if [ "$FAMILY" = "sites" ]; then
	emit_sites_program > "$OUT"
else
	emit_data_program > "$OUT"
fi
