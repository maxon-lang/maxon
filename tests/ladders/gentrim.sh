#!/usr/bin/env bash
# ⚠⚠ **DATED, AND THE COMPILER-SIDE HALF OF THIS HEADER IS HISTORY — READ IT AS A RECORD, NOT A MAP
# (corrected at W129's review, 2026-08-17).** Every generated program below still COMPILES AND RUNS and the
# ladder still measures what a trim costs: `CharacterSet.<preset>()`, `CharSet from […]` and `p.trim(c)` are
# ordinary corpus calls into `stdlib/CharacterSet.maxon` and `stdlib/String.maxon` (listed at W115). What is
# gone is every COMPILER-INTERNAL mechanism this header prices — `__str_trim` (W49 wave 4), then
# `__cs_make`/`__cs_decref`/`__cs_contains`, `__ucd_cat`, `parseCharacterSetStaticCall`,
# `recordCharacterSetUsage`/`closeCharacterSetNeeds` and `Runtime/CharacterSetRuntime.maxon` entire (W129).
# ⇒ The `sites-control` measurements below are still SOUND AS DATED READINGS, and the cost they priced at
# "nothing measurable" is now structurally zero rather than merely small: there is no per-call-op
# `CharacterSet` arm left to run. The two RAW UCD table loads (`__ucd_bmp_at`/`__ucd_supp_at`) and both
# checked-in blobs survive, so the plane-crossing seeds below still reach the supplementary search — through
# `stdlib/helpers/string/unicodeCategory.maxon` rather than through `__ucd_cat`.
#
# `CharacterSet`, the Unicode `General_Category` table and the three `String` trims (P1.8 slice D) — and
# the FOUR different questions they raise, which is why this generator has four families of mode and not
# one. It is `genstring-grapheme.sh`'s sibling and deliberately its twin in shape; read that file's
# header first.
#
# ⚠ **`ScaleCorpus` IS BLIND TO EVERY LAST PIECE OF THIS RUNG, AND THIS IS THE SIXTH CONSECUTIVE RUNG
# WITH THAT PROPERTY.** Dump the corpus (`scale-test --emit-corpus=<dir>`) and enumerate it at rung 5:
#
#     `trim` / `trimStart` / `trimEnd`   ZERO sites.
#     `CharacterSet` / `CharSet`         ZERO sites — the type is never named, in any form.
#     `Set with Character`               ZERO — every `Set` in the corpus is `Int`-keyed (`v_marray`).
#     `\uXXXX`                           ZERO escapes.
#
# ⇒ `__str_trim`, `__cs_make`/`__cs_decref`/`__cs_contains`, `__ucd_cat`, both checked-in UCD blobs, the
# eleven presets, the `CharacterSet` arm of `RuntimeUsage`'s closure and the E1004 escape diagnostic are
# STRUCTURALLY invisible to a default `scale-test` run — in allocations, in bytes AND in CPU ticks alike.
# A Δ0 from it is the instrument's blind spot and not a result. This ladder is the instrument for it.
#   ⇒ ⚠ AND THE CONVERSE, which is the check slice C's optimizer got right: the corpus is NOT blind to
#     `String` generally, so a reading that moves on the shared ladder may still be real — it simply
#     cannot be ABOUT the trim surface, because the corpus contains none of it.
#
# THE FOUR FAMILIES:
#
#   `sites-*` — a COMPILE ladder. `<n>` is the number of CALL SITES, so the question is the mandate's:
#              does the COMPILER stay linear in program size when the program is made of these?
#              `sites-control` is THE CONTROL and matters most — the same program shape with no trim and
#              no `CharacterSet` anywhere. It prices what the rung costs a program that never uses it:
#              `recordCharacterSetUsage` runs four `ByteArray.equals` plus `closeCharacterSetNeeds` on
#              EVERY call op in the module, and `parseCharacterSetStaticCall` walks the eleven-row preset
#              table per static call. (That is the P1.7a 2b-i shape: a cost levied on programs that never
#              asked for the feature.)
#
#   `data-*`  — a RUN ladder on the STRING LENGTH knob. `<n>` is a number of DOUBLINGS of the subject
#              string; the DATA doubles and the SOURCE does not. It answers what no compile ladder can:
#              is the emitted `__str_trim` graph linear in the data? ⭐ **THIS IS THE FAMILY THAT SETTLES
#              THE O(n)-vs-O(n²) QUESTION.** `__str_trim` scans FORWARD ONLY (see `CharacterSetRuntime`'s
#              header: the reference's backward `findGraphemeStart` has no GB12/GB13 rule and would split
#              a flag the forward segmenter joins), so it walks the WHOLE string on every call. A forward
#              scan that re-derived the cluster start per candidate would read ×4 here.
#
#   `edge-*`  — a RUN ladder on the MATCHED-RUN knob, and the one that separates the axes `data-*`
#              conflates. The body is a FIXED 8 bytes and the MATCHED PAD doubles, at the head
#              (`edge-prefix`) or the tail (`edge-tail`). `data-*` doubles the unmatched body; these
#              double the trimmed run. Between them, "cost of scanning past" and "cost of cutting" are
#              independent quantities rather than one number moving.
#
#   `loop-*`  — a RUN ladder on the TRIP COUNT, at a fixed tiny subject: the SLAB TRIGGER. `MmRuntime` is
#              a bump allocator with no free list, so PLAN.md's Workstream O debt says memory is linear in
#              ITERATIONS rather than in live data, and its re-measure trigger is *"any construct that
#              allocates once per loop trip."* This rung adds two more such constructs and this family
#              prices both. ⭐ **`loop-trim` vs `loop-trim-shared` IS THE A/B THAT ISOLATES THE PER-CALL
#              `CharacterSet`**: identical programs but for whether the set is built inside the loop or
#              hoisted out of it, so their difference is exactly what a predefined set costs per call.
#              `loop-charset` prices that construction with no trim attached at all.
#
# ⚠ **THE SEEDS ARE REAL KNOBS, because they pick the path through BOTH `__gr_end` AND `__ucd_cat`**, and
# those two forks are not the same fork:
#
#   ASCII  1 byte/cluster   — `__gr_end`'s fast path; `__ucd_cat`'s DIRECT indexed byte load.
#   WIDE   2 bytes/cluster  — `__gr_end`'s GENERAL SCAN (decode + property + rule); `__ucd_cat` still the
#                             direct load, since U+00E9 is in the BMP.
#   SUPP   4 bytes/cluster  — the general scan AND `__ucd_cat`'s **BINARY SEARCH over the 806-entry
#                             supplementary table**. ⭐ This is the only seed that reaches that search at
#                             all, and a ladder without it cannot claim to have measured `__ucd_cat`.
#
# ⚠⚠ **PLAIN SPACE U+0020 IS NOT AN EXPLICIT MEMBER OF ANY PREDEFINED SET** (`CharacterSetRuntime`'s
# header). It is whitespace only through the mask's `Zs` bit, so **every space in an `edge-*` pad goes
# through the UCD table**, and the `data-*` bodies (letters) miss the explicit set AND the mask — i.e.
# they pay the FULL `__cs_contains` both ways. That is the expensive direction on purpose.
#
# ⚠ **THE SETUP IS `s.append(s)`, NOT AN APPEND LOOP** — `genstring.sh`'s reason, unchanged:
# `__str_append` reallocates to the EXACT required length on every grow, so building an N-byte string by
# N appends copies O(N²) bytes and would swamp every reading below with a quadratic that is not the one
# being measured. Self-append doubles the length per step.
#
# ⛔ **DO NOT BUILD A `data-*`/`edge-*`/`loop-*` PROGRAM WITH THE C# BOOTSTRAP AND BELIEVE ITS NUMBERS.**
# `genstring-grapheme.sh` found and documented the bug: the bootstrap MISCOMPILES the SECOND `s.append(s)`
# after the buffer grows, leaving fill bytes in place of content. shv2 is the one that is right. Build
# these with shv2.
#
# Usage: gentrim.sh <n> <mode> <out>
#   sites-trim | sites-trim-shared | sites-charset | sites-control          (<n> = CALL SITES)
#   data-trim-clean | data-trimstart-clean | data-trimend-clean             (<n> = DOUBLINGS of bytes)
#   data-trim-wide | data-trim-supp | data-trim-allmatch                    (<n> = DOUBLINGS of bytes)
#   edge-prefix | edge-tail                                                 (<n> = DOUBLINGS of the pad)
#   loop-trim | loop-trim-shared | loop-charset | loop-control              (<n> = DOUBLINGS of trips)
#
#   e.g. gentrim.sh 512 sites-trim a.maxon
#        gentrim.sh 512 sites-control c.maxon    (same size, no trim surface)
#        gentrim.sh 16 data-trim-clean d.maxon   (8*2^16 = 524,288 bytes, none of it trimmable)
#        gentrim.sh 16 edge-prefix    e.maxon    (2^16 spaces in front of a fixed 8-byte body)
#        gentrim.sh 16 loop-trim      f.maxon    (65,536 trims of a 12-byte string)
#
# The `data-*`/`edge-*`/`loop-*` programs each print ONE CSV line — `mode,bytes,units,nanos,reps` — where
# `units` is the count the operation's cost should be linear in (BYTES scanned for `data-*`, the PAD
# length for `edge-*`, the TRIP COUNT for `loop-*`). REPS (env, default 5) repeats the operation inside
# the timed region; wall nanos are used because shv2 has no thread-CPU intrinsic, so measure on an idle
# box and take the MINIMUM of several runs. Against the ×2-vs-×4 question that is ample; against a few
# percent it is worth nothing.
#
# ⚠ **PEAK MEMORY IS NOT PRINTED BY THE PROGRAM AND MUST BE READ FROM OUTSIDE IT** — the slab-trigger
# question is about the working set, and a bump allocator's own counters would report every allocation
# freed (it is not a leak; no run exits 101). Read it off the process, e.g. on Windows:
#
#     $p = Start-Process -FilePath .\out.exe -PassThru -Wait; $p.PeakWorkingSet64
#
# --- WHAT IT READ ON THE DAY IT WAS BUILT (2026-07-28, branch `p18d-ucd-characterset-trim`), so a later
#     run has a BEFORE to compare against rather than only a shape to re-derive ---
#
#   CORPUS BLINDNESS, MEASURED not assumed (`scale-test --emit-corpus`, rung 5): `trim` 0, `CharacterSet`
#   0, `CharSet` 0, `Set with` 0, `Character` 0, `\u` 0. The inventory at the top of this file is that run.
#
#   COMPILE (`--metrics` total, 64 → 1,024 sites): EVERY mode linear, in allocations and CPU alike.
#   `sites-trim` allocs ×1.83 ×1.91 ×1.95 ×1.97 (CPU ×1.97 ×1.99 ×1.95 ×2.00); `sites-charset` ×1.85
#   ×1.92 ×1.96 ×1.98; `sites-trim-shared` ×1.61 ×1.76 ×1.86 ×1.93; `sites-control` ×1.76 ×1.86 ×1.93
#   ×1.96. Nothing bends.
#
#   ⭐ **WHAT THE RUNG COSTS A PROGRAM THAT NEVER USES IT — path-clean INTERLEAVED A/B against the parent
#   `71cd8479b`, both binaries built by the SAME bootstrap (md5-identical) and driven from EQUAL-LENGTH
#   checkout paths so the path term cancels: `+14 ALLOCATIONS, FLAT` at 64/128/256/512/1,024 sites (a 16×
#   span), plus `16 bytes per FUNCTION + ~824`. Nothing per site, per call op or per statement — so
#   `recordCharacterSetUsage`'s four `ByteArray.equals` on every call op cost nothing measurable. The
#   per-function 16 bytes is `phase:parse` with ZERO extra allocations, i.e. an existing per-function
#   allocation crossing one 16-byte slab size class; which field was not isolated. ⚠ The per-function form
#   was established by holding `<n>` at 256 and moving `SITES_PER_FN` 1/2/4/8 — 4,920 / 2,872 / 1,848 /
#   1,336 bytes, tracking the FUNCTION count and ignoring the site count. That is what the knob is for.
#   The same shape reproduces on the SHARED corpus: +14 allocations flat at all six rungs, bytes doubling
#   1,352 → 17,224, and IDENTICAL growth ratios on both binaries (×1.80 ×1.89 ×1.94 ×1.97 ×1.98).
#
#   RUN, `data-*` (12 → 16 doublings, 32,768 → 524,288 bytes): ×2.00 per doubling in EVERY seed, in time
#   and in peak RSS alike. **`__str_trim` IS O(n), NOT O(n²)** — the forward-only scan's documented cost is
#   exactly the O(1)→O(n) it claims and nothing worse. Per CLUSTER at the top rung: ASCII 102 ns, wide
#   (2-byte BMP) 150 ns, supplementary 190 ns. So `__gr_end`'s general scan costs ~48 ns over its ASCII
#   fast path, and `__ucd_cat`'s **806-entry binary search costs ~40 ns over a direct BMP byte load** — a
#   real but bounded constant, and no bend. `edge-prefix`/`edge-tail` read ×2.00 likewise, and read the
#   SAME per-cluster cost as `data-*`: scanning past a kept cluster and cutting a matched one cost the
#   same, because both mint a `Character` and probe the set.
#
#   ⭐⭐ **THE FINDING: `trimStart` WALKED THE WHOLE STRING TO ANSWER A QUESTION IT HAD ALREADY SETTLED.**
#   `data-trimstart-clean` read the same curve AND the same absolute nanoseconds as `data-trim-clean`,
#   because the scan ran to the end collecting a `keptEnd` that `emitTrimResult` then discarded (an
#   untrimmed end takes `length`, not `keptEnd`). Isolated to a single call: `"abcdefgh"×65,536`
#   `.trimStart()`, which has NOTHING to cut, cost **56.2 ms and 45.0 MB of peak RSS** to answer "byte 0".
#   Fixed by exiting the walk at the first kept cluster when `fromEnd` is clear (`buildStrTrim`'s
#   `firstKeptStop` arm, reached at most ONCE per trim). After: **0.46 ms and 4.8 MB** — 121× and 9.3×,
#   and the 0.46 ms that remains is the RESULT COPY (`emitSliceSegment` blits 512 KB), which is the floor
#   for an owned result. On the ladder, `data-trimstart-clean` at 524,288 bytes went 262.2 ms → 4.6 ms and
#   208.0 MB → 6.7 MB. ⚠ `trim` and `trimEnd` are UNCHANGED to within noise at every rung (262.4 vs 268.7
#   and 261.8 vs 261.1 ms; RSS identical to the KB) — they genuinely need the far end, and they paid
#   nothing for the exit.
#
#   RUN, `loop-*` (4,096 → 65,536 trips, 9-byte subject): ×2.00 per doubling in time and RSS — memory
#   linear in ITERATIONS, which is PLAN.md Workstream O's filed slab debt firing again. Slopes between the
#   top two rungs: `loop-trim` 2,149 ns and 1,945 bytes per trip; `loop-trim-shared` (the SAME program
#   with the set hoisted out of the loop) 1,081 ns and 790 bytes; `loop-control` 172 ns. ⇒ **A PREDEFINED
#   `CharacterSet` COSTS ~1,068 ns AND ~1,155 BYTES OF NEVER-RECLAIMED SLAB PER CALL** — half of what a
#   whole `trim()` of a 9-byte string costs. It is a CONSTANT per call (the set has a fixed 7 members), not
#   a growing one; what makes it matter is that the slab never gives it back, so in a loop it is linear in
#   trips. `loop-charset` — the same set built explicitly — reads the same RSS to the KB, which is the
#   consistency check that the two spellings are one cost.
set -euo pipefail
N="$1"; MODE="$2"; OUT="$3"
REPS="${REPS:-5}"

case "$MODE" in
  sites-trim|sites-trim-shared|sites-charset|sites-control) FAMILY=sites ;;
  data-trim-clean|data-trimstart-clean|data-trimend-clean) FAMILY=data ;;
  data-trim-wide|data-trim-supp|data-trim-allmatch) FAMILY=data ;;
  edge-prefix|edge-tail) FAMILY=edge ;;
  loop-trim|loop-trim-shared|loop-charset|loop-control) FAMILY=loop ;;
  *) echo "gentrim.sh: unknown mode '$MODE' (see header)" >&2; exit 2 ;;
esac

if [ "$N" -lt 1 ]; then echo "gentrim.sh: <n> must be >= 1" >&2; exit 2; fi

# One SITE GROUP per function, `genstring.sh`'s reason: a function stays a constant size as `<n>` doubles,
# so the register allocator's own curves stay out of the reading.
#
# ⭐ **OVERRIDABLE, BECAUSE A PER-FUNCTION TERM AND A PER-SITE TERM ARE DIFFERENT FINDINGS AND THE DEFAULT
# LADDER CANNOT TELL THEM APART** — at a fixed `SITES_PER_FN` both scale with `<n>` together. Hold `<n>`
# and move this, and the function count moves ALONE; the pair of readings separates them. That is what
# identified slice D's control-mode `+16 bytes per FUNCTION` as per-function rather than per-site:
#
#     SITES_PER_FN=1 gentrim.sh 64 sites-control a.maxon   # 64 functions, 64 sites
#     SITES_PER_FN=2 gentrim.sh 64 sites-control b.maxon   # 32 functions, 64 sites
SITES_PER_FN="${SITES_PER_FN:-4}"

# --- THE SEEDS. Each is 8 bytes so every `data-*` rung is directly comparable in BYTES; what differs is
# how many CLUSTERS those bytes are and which arm of `__ucd_cat` each cluster takes.
#
#   ASCII  'abcdefgh'  8 clusters — fast path in `__gr_end`, direct BMP byte load in `__ucd_cat`.
#   WIDE   'éàéà'      4 clusters — general scan; still a direct BMP load (U+00E9/U+00E0).
#   SUPP   '𝐀𝐁'        2 clusters — general scan AND the 806-entry supplementary BINARY SEARCH
#                                   (U+1D400/U+1D401, MATHEMATICAL BOLD CAPITAL A/B, category Lu).
#   MATCH  8 spaces    8 clusters — every one matches, via the `Zs` MASK BIT and not the explicit set.
ascii_seed() { printf 'abcdefgh'; }
wide_seed()  { printf '\xc3\xa9\xc3\xa0\xc3\xa9\xc3\xa0'; }
supp_seed()  { printf '\xf0\x9d\x90\x80\xf0\x9d\x90\x81'; }
match_seed() { printf '        '; }

seed_for_mode() {
	case "$1" in
	data-trim-clean|data-trimstart-clean|data-trimend-clean) ascii_seed ;;
	data-trim-wide) wide_seed ;;
	data-trim-supp) supp_seed ;;
	data-trim-allmatch) match_seed ;;
	esac
}

# Which trim the mode calls, as the source method name.
trim_method_for_mode() {
	case "$1" in
	data-trimstart-clean) printf 'trimStart' ;;
	data-trimend-clean) printf 'trimEnd' ;;
	*) printf 'trim' ;;
	esac
}

emit_prelude() {
	echo "// trim ladder: $N, mode $MODE — see tests/ladders/gentrim.sh"
	echo "typealias Int = int(i64.min to i64.max)"
	echo "typealias ExitCode = int(0 to 255)"
	echo ""
}

# The self-append doubler, shared by every run family. O(final length) in total.
emit_grown() {
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
}

emit_sites_program() {
	FNS=$(( N / SITES_PER_FN ))
	if [ "$FNS" -lt 1 ]; then FNS=1; fi

	emit_prelude

	f=0
	while [ "$f" -lt "$FNS" ]; do
		echo "function fn${f}(s String, p String) returns Int"
		echo -e "\tvar total = 0"
		if [ "$MODE" = "sites-trim-shared" ]; then
			# ONE set per function, so the per-SITE cost is the trim call alone and the preset construction
			# is amortized. Against `sites-trim` at the same site count this isolates, in the COMPILE
			# columns, exactly what a per-site predefined set costs the parser and the backend.
			echo -e "\tlet cs = CharacterSet.whitespacesAndNewlines()"
		fi
		n=0
		while [ "$n" -lt "$SITES_PER_FN" ]; do
			case "$MODE" in
			sites-trim)
				# A no-argument trim: the parser builds the default preset — a `Set with Character`, SEVEN
				# minted `Character` literals and a `__cs_make` box — INLINE at every one of these sites.
				echo -e "\tlet t${n} = s.trim()"
				echo -e "\ttotal = total + t${n}.byteLength()"
				;;
			sites-trim-shared)
				echo -e "\tlet t${n} = s.trim(cs)"
				echo -e "\ttotal = total + t${n}.byteLength()"
				;;
			sites-charset)
				# The set construction with no trim attached — `parseCharacterSetStaticCall`'s eleven-row
				# table walk, `emitCharacterMemberSet`'s seven literals, and the box.
				echo -e "\tlet c${n} = CharacterSet.whitespacesAndNewlines()"
				echo -e "\tlet m${n} = p.trim(c${n})"
				echo -e "\ttotal = total + m${n}.byteLength()"
				;;
			sites-control)
				# THE CONTROL: the P1.2 String surface and NOT ONE `CharacterSet`, trim or Character. Same
				# function shape, same call-op density — so the difference against a PARENT binary is the
				# tax slice D levies on a program that does not use it.
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
		echo -e "\tsum = sum + fn${f}(\"  alpha beta  \", p: \"  gamma  \")"
		f=$(( f + 1 ))
	done
	echo -e "\treturn 0 if sum >= 0 else 1"
	echo "end 'main'"
}

emit_data_program() {
	METHOD="$(trim_method_for_mode "$MODE")"
	emit_prelude
	emit_grown

	echo "function main() returns ExitCode"
	echo -e "\tvar total = 0"
	echo -e "\tlet subject = grown(\"$(seed_for_mode "$MODE")\", doublings: $N)"
	echo -e "\tlet units = subject.byteLength()"
	echo -e "\tlet t0 = __Builtins.currentTimeNanos()"
	echo -e "\tvar r = 0"
	echo -e "\twhile r < $REPS 'rep'"
	# ⭐ The whole question. `__str_trim` walks CLUSTERS from byte 0 to the end, minting a `Character`
	# through `__char_at` at each and probing the set with `__cs_contains`. If anything re-derived a
	# cluster start — or if `__gr_end` scanned from 0 rather than from `pos` — this reads ×4 per doubling.
	echo -e "\t\tlet cut = subject.${METHOD}()"
	echo -e "\t\ttotal = total + cut.byteLength()"
	echo -e "\t\tr = r + 1"
	echo -e "\tend 'rep'"
	echo -e "\tlet t1 = __Builtins.currentTimeNanos()"
	echo -e "\tprint(\"$MODE,{subject.byteLength()},{units},{t1 - t0},$REPS\\\\n\")"
	echo -e "\treturn 0 if total >= 0 else 1"
	echo "end 'main'"
}

emit_edge_program() {
	emit_prelude
	emit_grown

	echo "function main() returns ExitCode"
	echo -e "\tvar total = 0"
	# The PAD doubles and the BODY does not, so `units` is the trimmed run and the unmatched body is a
	# constant. That is the axis `data-*` cannot separate: there the body doubles and the pad is empty.
	echo -e "\tlet pad = grown(\" \", doublings: $N)"
	echo -e "\tlet units = pad.byteLength()"
	if [ "$MODE" = "edge-prefix" ]; then
		echo -e "\tvar built = pad"
		echo -e "\tbuilt.append(\"abcdefgh\")"
	else
		echo -e "\tvar built = \"abcdefgh\""
		echo -e "\tbuilt.append(pad)"
	fi
	echo -e "\tlet subject = built"
	echo -e "\tlet t0 = __Builtins.currentTimeNanos()"
	echo -e "\tvar r = 0"
	echo -e "\twhile r < $REPS 'rep'"
	echo -e "\t\tlet cut = subject.trim()"
	echo -e "\t\ttotal = total + cut.byteLength()"
	echo -e "\t\tr = r + 1"
	echo -e "\tend 'rep'"
	echo -e "\tlet t1 = __Builtins.currentTimeNanos()"
	echo -e "\tprint(\"$MODE,{subject.byteLength()},{units},{t1 - t0},$REPS\\\\n\")"
	echo -e "\treturn 0 if total >= 0 else 1"
	echo "end 'main'"
}

emit_loop_program() {
	emit_prelude

	echo "function main() returns ExitCode"
	echo -e "\tvar total = 0"
	echo -e "\tlet subject = \"  alpha  \""
	echo -e "\tlet trips = $(( 1 << N ))"
	echo -e "\tlet units = trips"
	if [ "$MODE" = "loop-trim-shared" ]; then
		# HOISTED: one predefined set for the whole loop. Against `loop-trim` — identical but for this one
		# line — the difference IS the per-call `CharacterSet` construction, in time and in peak RSS alike.
		echo -e "\tlet cs = CharacterSet.whitespacesAndNewlines()"
	fi
	echo -e "\tlet t0 = __Builtins.currentTimeNanos()"
	echo -e "\tvar i = 0"
	echo -e "\twhile i < trips 'trip'"
	case "$MODE" in
	loop-trim)
		# ⚠ TWO per-trip allocating constructs at once: the default `CharacterSet` (a `Set with Character`,
		# seven owned `Character` members and a box) AND the trim's own owned result. The slab never gives
		# either back.
		echo -e "\t\tlet cut = subject.trim()"
		echo -e "\t\ttotal = total + cut.byteLength()"
		;;
	loop-trim-shared)
		echo -e "\t\tlet cut = subject.trim(cs)"
		echo -e "\t\ttotal = total + cut.byteLength()"
		;;
	loop-charset)
		# The set construction ALONE — no trim, no owned result String. This is the per-call cost the
		# reference avoids with its `static let cachedWhitespaces`, isolated.
		echo -e "\t\tlet c = CharacterSet.whitespacesAndNewlines()"
		echo -e "\t\tlet cut = subject.trim(c)"
		echo -e "\t\ttotal = total + cut.byteLength()"
		;;
	loop-control)
		# THE CONTROL: a per-trip owned String from machinery this rung did not touch, so the slab's own
		# per-trip growth is separated from anything slice D added.
		echo -e "\t\tvar acc = subject"
		echo -e "\t\tacc.append(subject)"
		echo -e "\t\ttotal = total + acc.byteLength()"
		;;
	esac
	echo -e "\t\ti = i + 1"
	echo -e "\tend 'trip'"
	echo -e "\tlet t1 = __Builtins.currentTimeNanos()"
	echo -e "\tprint(\"$MODE,{subject.byteLength()},{units},{t1 - t0},1\\\\n\")"
	echo -e "\treturn 0 if total >= 0 else 1"
	echo "end 'main'"
}

case "$FAMILY" in
sites) emit_sites_program > "$OUT" ;;
data)  emit_data_program  > "$OUT" ;;
edge)  emit_edge_program  > "$OUT" ;;
loop)  emit_loop_program  > "$OUT" ;;
esac
