#!/usr/bin/env bash
# THE P1.8 SLICE E SURFACE — `toByteArray` / `clone` / `codepoints` / `utf16` / `isEmpty` /
# `replaceFirst` / `String.from(bytes)`, plus the nine `utf16*` free functions the stdlib LOADER
# delivers. `gentrim.sh`'s sibling and deliberately its twin in shape; read that file's header first.
#
# ⚠ **`ScaleCorpus` IS BLIND TO EVERY LAST PIECE OF THIS RUNG — THE SEVENTH CONSECUTIVE RUNG WITH THAT
# PROPERTY.** Dump the corpus (`scale-test --emit-corpus=<dir>`) and enumerate it at rung 5:
#
#     `toByteArray` / `replaceFirst` / `codepoints` / `utf16` / `String.from`   ZERO sites, each.
#     `.bytes()` / `.replace(`                                                  ZERO.
#     `.isEmpty()`                                                              ZERO — in ANY receiver.
#     `.clone()`                                                                256 sites, and ⚠ **EVERY
#                                                                               ONE IS AN `Array`
#                                                                               RECEIVER** (`ac0`…`ac255`
#                                                                               out of the array-ops
#                                                                               knob). Not one String.
#
# ⇒ `__str_to_bytes`, `__str_to_codepoints`, `__str_to_utf16`, `__str_clone`, `__str_replace_first`,
# `parseMaterializedView`, `parseStringStaticCall` and all nine stdlib `utf16*` declarations are
# STRUCTURALLY invisible to a default `scale-test` run — in allocations, in bytes AND in CPU alike. A Δ0
# from it is the instrument's blind spot and not a result. This ladder is the instrument for them.
#
# ⚠⚠ **FIVE OF THOSE NAMES NO LONGER EXIST (W49 wave 6), AND THE MODES THAT DRIVE THEM STILL DO — WHICH IS
# THE POINT OF KEEPING THEM.** `String.toByteArray()`/`.bytes()`/`.codepoints()`/`.utf16()` retired onto
# `stdlib/String.maxon` + `stdlib/helpers/string/views.maxon`, taking `__str_to_codepoints`,
# `__str_to_utf16`, `parseDecodedView`, `parseMaterializedView` and `emitStringWalkSkeleton` with them
# (`__str_to_bytes` survives, serving `Character.bytes()` alone). The MODES below are unchanged and still
# compile: they now price the CORPUS bodies rather than the synthesized ones, which is exactly the A/B a
# retirement owes.
#
# --- W49 WAVE 7 A/B (2026-08-08). `isEmpty` and `clone` are TWO OF THIS FILE'S OWN MODES, and wave 7 is
#     the wave that retired them — so their second reading belongs here beside the first. Same SOURCE,
#     two compilers swapped into ONE path, interleaved, min-of-7. base = ddfc4278ad. ---
#
#   ⛔⛔⛔ **THE COST WAS A HEAP ALLOCATION PER CALL, NOT A CALL FRAME — AND THE SIZE OF THE RATIO IS WHAT
#   FOUND IT.** A frame would have read 2-4x; the first measurement read up to 87x, and that gap is what
#   made it worth dumping `func @String.byteLength` instead of filing the number. ⚠ **THE WAVE SHIPS
#   CURED, so all three columns are here** — a number quoted without its `cured` neighbour describes a
#   state that never merged.
#
#                                        base       uncured        cured    uncured     cured
#     s.byteLength()  4,000,000 trips    3.33 ms   278.69 ms    11.13 ms     83.7x   **3.34x**
#     s.isEmpty()     4,000,000 trips    5.05 ms   287.48 ms    17.22 ms     56.9x   **3.41x**  (body IS byteLength)
#     hashString(s)   524,288 B x 8      8.82 ms   302.39 ms    19.74 ms     34.3x   **2.24x**  (byteAt, per byte)
#     short.clone()   4,000,000 trips  251.03 ms  1370.27 ms   841.54 ms      5.5x   **3.35x**  (see below)
#     integer control                   5.89 ms      5.83 ms     5.88 ms      1.0x     1.00x    THE NOISE FLOOR
#
#   **WHY IT WAS 87x**: `String.byteLength`'s corpus body is `return managed.length()`, and reading a fused
#   wrapper's inline `managed` minted `__str_bytes_view` — a 48-byte `Array` record plus an `__mm_incref`.
#   That ONE expression emitted `__str_bytes_view` / `__arr_count` / `__arr_decref` to read `length@8`, and
#   `String.byteAt` was the same shape once PER BYTE.
#
#   **THE CURE**: a `managed` read that is immediately CALLED hands the receiver's OWN record to the buffer
#   entry and mints nothing. A String record and an `Array` record agree on the five slots those entries
#   read — the argument W49 wave 2 already used on the WRITE side for `setByte`. The door is
#   `dispatchMethodOnBinding`'s `inlineManagedServesTheRecord`, and since `EC2` it admits EVERY member
#   rather than the six `Parser.fusedManagedMemberTakesTheRecord` used to list — that function is deleted
#   and its slot census now lives in the door's own header. ⇒ `--emit-ir` one `s.byteLength()` and read
#   the body rather than trusting this note: it is a single `callDirect __arr_count` now.
#
#   ⚠ **`clone()` IS THE ONE MEMBER THE CURE CANNOT REACH, AND IT IMPROVED ANYWAY.** `slice` is the single
#   buffer member that may not take a String record — `__arr_slice` -> `emitArrayView` LOADS `@40` and
#   STORES it into the fresh view, so a String's `singleByteGraphemesFlag` would become that view's
#   `element_destroy` and the first `__arr_decref` would `callIndirect` address 1. The mint is not overhead
#   there; it is what supplies a correct `TrivialDestructor`. `clone` still went 5.5x -> 3.35x, because
#   `sliceBytes` asks `byteLength()` and the cure made that cheap.
#
#   ⚠ **A SLOWDOWN — OR A SPEEDUP — IS A CHANGED PROGRAM UNTIL A CHECKSUM SAYS OTHERWISE** (wave 6's rule,
#   both directions). Every mode accumulates into a printed sink and ALL THREE binaries agree to the digit
#   on all five: byteLength 2097152000000, isEmpty 0, clone 32000000, bytewalk 11897186344, control
#   11999994.
#
#   ⚠ **`ScaleCorpus` IS NOT BLIND TO WAVE 7, UNLIKE THE SIX BEFORE IT, AND ITS READING IS A DIFFERENT
#   QUESTION.** The 2026-08-08 corpus change gave the String knob `.byteLength()` and the materializer knob
#   `.isEmpty()`/`.clone()`, so a default `scale-test` prices this wave at **+2.39% allocations at rung 0
#   and +2.40% at rung 5, every growth ratio identical to three digits on both columns**. That is the
#   COMPILER's cost of compiling programs that contain more calls, and it says nothing about what those
#   programs then DO — which is the whole reason this ladder exists beside it. ⚠ The CURE barely moves that
#   column (uncured read +2.51% / +2.40%): it removes ops from the EMITTED program, not work from the
#   compile, so the two instruments are measuring different things and only this one saw the 87x.
#
#   ⚠ **THE `print` PATH WAS A SECOND, INDEPENDENT COST OF THIS WAVE, AND IT IS CURED SEPARATELY AND FULLY.**
#   Retiring `addressableBytes` put the byte view one call frame below `stdlib/Print.maxon:10`, which left
#   `foldByteViewIntoStreamWrite` with no producer: `print("hi\n")` alone went **1,749 -> 3,226 code bytes
#   (+84.4%)** and regained a `.data` section. The fold is RE-AIMED rather than rebuilt — it now rewrites the
#   corpus call `String.addressableBytes(s)`, whose one argument is the String, exactly as it used to rewrite
#   the `__str_bytes_view(s)` a synthesized arm appended. **Back to 1,749 bytes, the base number to the
#   byte**, and `func @print` is one `callDirect __write_stdout` again.
#
#   ⚠ **IT IS DELIBERATELY NOT WALL-TIMED, and that is a statement about the instrument.** A `print` loop
#   measures `WriteFile` at ~450 us per call, which swamps the difference by three orders of magnitude. CODE
#   SIZE and the emitted IR are the instruments that can see this one. The runtime FLOOR is unaffected
#   either way: a bare `return 0 as ExitCode` emits no data section, because it calls no `print`.
#
# --- W49 WAVE 6 A/B (2026-08-08). Same SOURCE, two compilers, interleaved, min-of-5, 524,288 bytes x 5
#     reps, ascii seed. base = 38fb221b0c, tip = the retirement. ---
#
#   ⭐⭐ **THE RETIREMENT IS FASTER ON EVERY MODE, WHICH IS THE OPPOSITE OF WHAT WAS EXPECTED**, and the
#   reason is that materializing was never free: the synthesized views did an `__arr_push` per unit
#   (capacity test, possible grow, indexed store, slab) BEFORE the walk began, where the corpus's views
#   hold the `String` and read one unit per `advance()`.
#
#     data-tobytes    35.43 ms -> 0.0008 ms   `toByteArray().count()` is now O(1): the corpus returns a
#                                             COW `managed.slice` and `count()` reads its length. The
#                                             independence contract is unaffected — a write detaches
#                                             (measured: `b.set(0,88)` leaves the String untouched, on
#                                             shv2 AND on the oracle).
#     data-codepoints 171.17 ms -> 13.62 ms   (12.6x) `CodepointView.count()` walks without building.
#     data-utf16      180.03 ms -> 24.14 ms   (7.5x)
#     data-clone        3.97 ms ->  3.73 ms   THE CONTROL, untouched by this wave — within noise.
#
#   ⚠ **`data-*` MEASURES `.count()`, WHICH IS NOT THE ITERATION PATH, AND FOR `bytes()` THE CORPUS
#   ANSWERS IT WITHOUT WALKING AT ALL.** So a `for`-loop A/B was run beside it, same sizes
#   (`temp/cost/iterbytes|itercp|iteru16` in that rung's tree — three programs, one `for x in <view>` each):
#
#     for b in s.bytes()        42.61 ms -> 24.33 ms   (1.75x faster)
#     for c in s.codepoints()  195.04 ms -> 32.34 ms   (6.0x)
#     for u in s.utf16()       208.27 ms -> 41.41 ms   (5.0x)
#
#   ⚠ **A SPEEDUP IS A DELETED LOOP UNTIL A CHECKSUM SAYS OTHERWISE.** All three walks were checksummed
#   over 32,768 bytes of mixed 1/2/4-byte content and base shv2, tip shv2 AND the bootstrap oracle print
#   the identical `bytesSum=5169152 cpSum=528236544 u16Sum=461377536`.
#   ⇒ ⚠ AND `.clone()` IS THE TRAP IN REVERSE, the one slice B's optimizer named: a reading CAN move on
#     the shared ladder for a `clone` — 256 of them — and it still cannot be ABOUT this rung, because
#     every one of those receivers is an Array and the String arm did not exist when they were written.
#     **Check what the corpus CONTAINS before believing what a non-zero means, in either direction.**
#
# THE THREE FAMILIES:
#
#   `sites-*` — a COMPILE ladder. `<n>` is the number of CALL SITES, so the question is the mandate's:
#              does the COMPILER stay linear in program size when the program is made of these?
#              `sites-control` is THE CONTROL and matters most — the same program shape over the P1.2
#              String surface and none of this rung's. It prices what the rung costs a program that
#              never uses it. ⭐ `sites-utf16fns` is the one family member that is not about a parser
#              arm at all: it calls the nine STDLIB free functions, so it prices what a program
#              pays for a stdlib module it genuinely uses, against `sites-control`, which pays only for
#              the module being LOADED. (What the loaded-but-unused module costs is a different
#              question with its own instrument — `genstdlibload.sh`.)
#
#   `data-*`  — a RUN ladder on the STRING LENGTH knob. `<n>` is a number of DOUBLINGS of the subject
#              string; the DATA doubles and the SOURCE does not. ⭐ **THIS IS THE FAMILY THAT SETTLES
#              THE O(n)-vs-O(n²) QUESTION, AND FOR THESE METHODS IT IS A REAL QUESTION.** All three
#              views are PUSH-ONLY LOOPS — `__str_to_bytes`/`__str_to_codepoints`/`__str_to_utf16` call
#              `__arr_push` once per byte or per codepoint into an array the parser created with NO
#              reserve — so the whole cost rests on `__arr_grown_cap` being geometric. It is (double
#              below `GrowthThreshold`, then ease toward 1.25×), but that is READING; this family is
#              MEASURING. If a push ever reallocated to the exact required length, every one of these
#              would read ×4 per doubling, exactly as `__str_append` does (see `gentrim.sh`'s note on
#              why the setup is `s.append(s)` and not an append loop).
#              ⭐ `data-replacefirst` vs `data-replace` is the A/B that checks THE SLICE'S OWN CLAIM
#              that `replaceFirst` needs neither of `replace`'s two loops because one match means the
#              result size is known outright.
#
#   `loop-*`  — a RUN ladder on the TRIP COUNT, at a fixed tiny subject: the SLAB TRIGGER. `MmRuntime`
#              is a bump allocator with no free list, so PLAN.md's Workstream O debt says memory is
#              linear in ITERATIONS rather than in live data, and its re-measure trigger is *"any
#              construct that allocates once per loop trip."* ⭐ **THIS RUNG ADDS FOUR MORE SUCH
#              CONSTRUCTS** — `toByteArray()` mints an owned `Array with Byte` per call, `clone()` an
#              owned `String`, `codepoints()`/`utf16()` an owned `Array with integer` — and this family
#              prices each against `loop-control`, which mints an owned String per trip from machinery
#              this rung did not touch. ⚠ There is nothing to HOIST here, which is the difference from
#              slice D's `loop-trim` vs `loop-trim-shared`: a `CharacterSet` could be built once outside
#              the loop, but a materialized view IS the per-call allocation. So the A/B is against the
#              control, not against a hoisted twin.
#
# ⚠ **THE SEEDS ARE REAL KNOBS FOR `codepoints`/`utf16` AND INERT FOR `toByteArray`/`clone`**, and that
# asymmetry is the point rather than an oversight: the byte copy and the deep copy walk BYTES and cannot
# see an encoding, while the two decoding views walk CODEPOINTS through `__utf8_cp_at`/`__utf8_len_at`
# and `utf16` additionally branches per codepoint on BMP-vs-surrogate-pair. Each seed is 8 BYTES, so
# every `data-*` rung is directly comparable in bytes; what differs is how many CODEPOINTS those bytes
# are and which arm each takes.
#
#   ASCII  'abcdefgh'  8 codepoints — 1 byte each, `utf16` pushes 8 units.
#   WIDE   'éàéà'      4 codepoints — 2 bytes each, still BMP, `utf16` pushes 4 units.
#   SUPP   '𝐀𝐁'        2 codepoints — 4 bytes each, SUPPLEMENTARY: ⭐ the only seed that reaches
#                                     `utf16`'s SURROGATE-PAIR arm, which pushes TWO units per
#                                     codepoint. A ladder without it has not measured that arm at all.
#
# ⚠ **THE SETUP IS `s.append(s)`, NOT AN APPEND LOOP** — `genstring.sh`'s reason, unchanged:
# `__str_append` reallocates to the EXACT required length on every grow, so building an N-byte string by
# N appends copies O(N²) bytes and would swamp every reading below with a quadratic that is not the one
# being measured. Self-append doubles the length per step.
#
# ⛔ **DO NOT BUILD A `data-*`/`loop-*` PROGRAM WITH THE C# BOOTSTRAP AND BELIEVE ITS NUMBERS.**
# `genstring-grapheme.sh` found and documented the bug: the bootstrap MISCOMPILES the SECOND
# `s.append(s)` after the buffer grows, leaving fill bytes in place of content. shv2 is the one that is
# right. Build these with shv2.
#
# Usage: genstringviews.sh <n> <mode> <out>
#   sites-tobytes | sites-clone | sites-codepoints | sites-utf16 | sites-isempty      (<n> = CALL SITES)
#   sites-replacefirst | sites-from | sites-utf16fns | sites-control                  (<n> = CALL SITES)
#   data-tobytes | data-clone | data-codepoints | data-utf16                          (<n> = DOUBLINGS)
#   data-replacefirst | data-replace                                                  (<n> = DOUBLINGS)
#   loop-tobytes | loop-clone | loop-codepoints | loop-utf16 | loop-control           (<n> = DOUBLINGS)
#
#   e.g. genstringviews.sh 512 sites-tobytes a.maxon
#        genstringviews.sh 512 sites-control c.maxon        (same size, none of the rung's surface)
#        SEED=supp genstringviews.sh 16 data-utf16 d.maxon  (8·2^16 bytes of supplementary codepoints)
#        genstringviews.sh 16 loop-clone f.maxon            (65,536 clones of a 10-byte string)
#
# Env knobs: SITES_PER_FN (default 4 — hold `<n>` and move this to separate a per-FUNCTION term from a
# per-SITE one, `gentrim.sh`'s trick), REPS (default 5, `data-*` only), SEED (ascii | wide | supp).
#
# The `data-*`/`loop-*` programs each print ONE CSV line — `mode,bytes,units,nanos,reps` — where `units`
# is the count the operation's cost should be linear in (BYTES for `data-*`, the TRIP COUNT for
# `loop-*`). Wall nanos are used because the emitted program has no thread-CPU intrinsic, so measure on
# an idle box and take the MINIMUM of several runs. Against the ×2-vs-×4 question that is ample; against
# a few percent it is worth nothing.
#
# ⚠ **PEAK MEMORY IS NOT PRINTED BY THE PROGRAM AND MUST BE READ FROM OUTSIDE IT** — the slab-trigger
# question is about the working set, and a bump allocator's own counters would report every allocation
# freed (it is not a leak; no run exits 101). On Windows:
#
#     $p = Start-Process -FilePath .\out.exe -PassThru -Wait; $p.PeakWorkingSet64
#
# --- WHAT IT READ ON THE DAY IT WAS BUILT (2026-07-28, branch `p18e-string-views`), so a later run has
#     a BEFORE to compare against rather than only a shape to re-derive ---
#
#   CORPUS BLINDNESS, MEASURED not assumed (`scale-test --emit-corpus`, rung 5): the inventory at the
#   top of this file IS that run.
#
#   COMPILE (`--metrics` total allocations, 64 -> 1,024 sites): EVERY mode linear, in allocations and
#   CPU alike, with every ratio approaching x2.00 from BELOW as the compiler's fixed cost amortizes.
#   `sites-tobytes` x1.53 x1.69 x1.82 x1.90; `sites-clone` x1.58 x1.73 x1.84 x1.91; `sites-codepoints`
#   x1.51 x1.68 x1.81 x1.89; `sites-utf16` x1.51 x1.67 x1.80 x1.89; `sites-isempty` x1.77 x1.87 x1.93
#   x1.96; `sites-replacefirst` x1.57 x1.72 x1.84 x1.91; `sites-from` x1.57 x1.72 x1.84 x1.91;
#   `sites-utf16fns` x1.66 x1.79 x1.88 x1.94; `sites-control` x1.72 x1.84 x1.91 x1.95. **Nothing bends.**
#   ⭐ `sites-utf16fns` is the CHEAPEST mode of the nine (488,192 allocations at 1,024 sites against the
#   control's 921,668), which is the useful direction of that comparison: calling a stdlib
#   function costs less than the `append` the control does, so the module delivers its capability
#   without a per-site tax.
#
#   ⭐⭐ **RUN, `data-*` (12 -> 16 doublings, 32,768 -> 524,288 bytes): x2.00 PER DOUBLING IN EVERY MODE
#   AND EVERY SEED. ALL THREE MATERIALIZED VIEWS ARE O(n).** The push-only loops do NOT inherit a
#   reallocation quadratic — `__arr_grown_cap`'s geometric policy holds under one push per byte, which
#   was the open question and is now measured rather than read. `data-tobytes` x2.02 x1.99 x1.94 x1.98;
#   `data-clone` x2.06 x1.83 x1.94 x1.96; `data-codepoints` x1.97 x1.98 x1.95 x1.95; `data-utf16` x2.01
#   x1.93 x1.96 x1.97. Peak RSS reads x2 likewise.
#     PER BYTE at the top rung: `clone` 1.66 ns (a blit), `replaceFirst` 3.2 ns, `replace` 4.6 ns,
#     `toByteArray` **16.4 ns**, `codepoints` 83.6 ns, `utf16` 93.2 ns.
#     ⚠ **`toByteArray` COSTS ~10x WHAT `clone` COSTS FOR THE SAME BYTES**, and that is the whole
#     difference between a per-byte `__arr_push` call (capacity test, grow call, indexed store) and a
#     block copy. It is a CONSTANT FACTOR on a linear curve, not a curve — reported, not chased.
#     ⭐ `data-replacefirst` vs `data-replace`, same absent needle: **1.43x faster at every rung**
#     (8.44 ms vs 12.09 ms at 524,288 bytes), which confirms the slice's own claim that knowing the
#     result size outright removes one of `replace`'s two passes.
#     ⭐ PER CODEPOINT, by seed, at 524,288 bytes: `codepoints` 83.6 / 87.6 / 91.4 ns (ascii / wide /
#     supp) — nearly flat, so `__utf8_cp_at`'s multi-byte arm is cheap. `utf16` 93.2 / 96.5 / **191.1**
#     ns. ⚠ **The supplementary seed costs almost exactly DOUBLE**, which IS the surrogate-pair arm
#     doing two `__arr_push`es where BMP does one. That arm is reached only by `SEED=supp`.
#
#   RUN, `loop-*` (2^17 -> 2^20 trips, 10-byte / 10-codepoint subject): x2.00 per doubling in time and
#   in peak RSS — memory linear in ITERATIONS, which is PLAN.md Workstream O's filed slab debt firing
#   again, now with four more constructs. NEVER-RECLAIMED SLAB PER TRIP at 1,048,576 trips:
#   `loop-clone` **72 B**, `loop-control` 141 B, `loop-tobytes` **161 B**, `loop-codepoints` **376 B**,
#   `loop-utf16` **378 B**.
#     ⇒ `clone()` is the CHEAPEST per-trip construct measured here — HALF the control, which mints two
#       String records where a clone mints one.
#     ⇒ ⚠ **`codepoints()`/`utf16()` COST ~2.6x THE CONTROL AND ~5x `clone()` PER CALL** on a subject
#       of ten codepoints. The elements are 8 bytes and the array is created with NO reserve, so a
#       ten-element view walks 4 -> 8 -> 16 capacities and the slab keeps every intermediate buffer.
#       The runtime KNOWS the bound at entry (`__str_to_bytes` already loads `len`; the decoded views
#       could reserve `len` as an upper bound), so a single reserve would collapse it — a
#       CONSTANT-FACTOR change on an already-linear curve, so it is reported for a decision and not
#       shipped by the optimizer that found it.
set -euo pipefail
N="$1"; MODE="$2"; OUT="$3"
REPS="${REPS:-5}"
SEED="${SEED:-ascii}"

case "$MODE" in
sites-tobytes | sites-clone | sites-codepoints | sites-utf16 | sites-isempty) FAMILY=sites ;;
sites-replacefirst | sites-from | sites-utf16fns | sites-control) FAMILY=sites ;;
data-tobytes | data-clone | data-codepoints | data-utf16 | data-replacefirst | data-replace) FAMILY=data ;;
loop-tobytes | loop-clone | loop-codepoints | loop-utf16 | loop-control) FAMILY=loop ;;
*)
	echo "genstringviews.sh: unknown mode '$MODE' (see header)" >&2
	exit 2
	;;
esac

if [ "$N" -lt 1 ]; then echo "genstringviews.sh: <n> must be >= 1" >&2; exit 2; fi

# One SITE GROUP per function, `genstring.sh`'s reason: a function stays a constant size as `<n>`
# doubles, so the register allocator's own curves stay out of the reading. OVERRIDABLE because a
# per-function term and a per-site term are different findings and a fixed ratio cannot tell them apart
# (see `gentrim.sh`, which used exactly this to identify slice D's +16 bytes as per-FUNCTION).
SITES_PER_FN="${SITES_PER_FN:-4}"

# --- THE SEEDS. Each is 8 bytes; what differs is how many codepoints they are and which arm of the two
# DECODING views each codepoint takes.
seed_bytes() {
	case "$SEED" in
	ascii) printf 'abcdefgh' ;;
	wide) printf '\xc3\xa9\xc3\xa0\xc3\xa9\xc3\xa0' ;;
	supp) printf '\xf0\x9d\x90\x80\xf0\x9d\x90\x81' ;;
	*)
		echo "genstringviews.sh: unknown SEED '$SEED' (want ascii | wide | supp)" >&2
		exit 2
		;;
	esac
}

emit_prelude() {
	echo "// string-views ladder: $N, mode $MODE, seed $SEED — see tests/ladders/genstringviews.sh"
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

# ONE call site of the mode under test, accumulating into `total` so nothing is dead.
emit_site() {
	n="$1"
	case "$MODE" in
	sites-tobytes)
		echo -e "\tlet v${n} = s.toByteArray()"
		echo -e "\ttotal = total + v${n}.count()"
		;;
	sites-clone)
		echo -e "\tlet v${n} = s.clone()"
		echo -e "\ttotal = total + v${n}.byteLength()"
		;;
	sites-codepoints)
		echo -e "\tlet v${n} = s.codepoints()"
		echo -e "\ttotal = total + v${n}.count()"
		;;
	sites-utf16)
		echo -e "\tlet v${n} = s.utf16()"
		echo -e "\ttotal = total + v${n}.count()"
		;;
	sites-isempty)
		# The one member that allocates NOTHING — a field load and an integer compare. It is here so a
		# `sites-*` reading can separate "what the parser arm costs" from "what the owned result costs".
		echo -e "\tlet v${n} = s.isEmpty()"
		echo -e "\ttotal = total + 1 if v${n} else total"
		;;
	sites-replacefirst)
		echo -e "\tlet v${n} = s.replaceFirst(p, with: q)"
		echo -e "\ttotal = total + v${n}.byteLength()"
		;;
	sites-from)
		# `String.from` is the one exported `String` static, and `from` is a KEYWORD — so this site also
		# prices `parseStringStaticCall`'s token-kind consume, which is the thing that failed to PARSE
		# before this slice.
		echo -e "\tlet w${n} = s.toByteArray()"
		echo -e "\tlet v${n} = String.from(w${n})"
		echo -e "\ttotal = total + v${n}.byteLength()"
		;;
	sites-utf16fns)
		# ⭐ The STDLIB-DELIVERED surface: nine free functions declared in a stdlib module, called by
		# BARE NAME. Every site cycles a different one so no single declaration dominates the reading.
		case $((n % 3)) in
		0) echo -e "\ttotal = total + utf16Width(total)" ;;
		1) echo -e "\ttotal = total + 1 if utf16IsLeadSurrogate(total) else total" ;;
		2) echo -e "\ttotal = total + utf16LeadSurrogate(total + 65536)" ;;
		esac
		;;
	sites-control)
		# THE CONTROL: the P1.2 String surface and NOT ONE member this rung added. Same function shape,
		# same call-op density — so the difference against a PARENT binary is the tax slice E levies on a
		# program that does not use it.
		echo -e "\tvar acc${n} = p"
		echo -e "\tacc${n}.append(s)"
		echo -e "\ttotal = total + acc${n}.byteLength()"
		;;
	esac
}

emit_sites_program() {
	FNS=$((N / SITES_PER_FN))
	if [ "$FNS" -lt 1 ]; then FNS=1; fi

	emit_prelude

	f=0
	while [ "$f" -lt "$FNS" ]; do
		echo "function fn${f}(s String, p String, q String) returns Int"
		echo -e "\tvar total = 0"
		n=0
		while [ "$n" -lt "$SITES_PER_FN" ]; do
			emit_site "$n"
			n=$((n + 1))
		done
		echo -e "\treturn total"
		echo "end 'fn${f}'"
		echo ""
		f=$((f + 1))
	done

	echo "function main() returns ExitCode"
	echo -e "\tvar sum = 0"
	f=0
	while [ "$f" -lt "$FNS" ]; do
		echo -e "\tsum = sum + fn${f}(\"alpha beta\", p: \"gamma\", q: \"delta\")"
		f=$((f + 1))
	done
	echo -e "\treturn 0 if sum >= 0 else 1"
	echo "end 'main'"
}

# The measured expression, and what `units` means for it.
emit_data_body() {
	case "$MODE" in
	data-tobytes) echo -e "\t\tlet res = subject.toByteArray()\n\t\ttotal = total + res.count()" ;;
	data-clone) echo -e "\t\tlet res = subject.clone()\n\t\ttotal = total + res.byteLength()" ;;
	data-codepoints) echo -e "\t\tlet res = subject.codepoints()\n\t\ttotal = total + res.count()" ;;
	data-utf16) echo -e "\t\tlet res = subject.utf16()\n\t\ttotal = total + res.count()" ;;
	# ⭐ The A/B behind the slice's own claim. Both search for a needle that is NOT PRESENT, so both walk
	# the whole subject and neither's result-size arithmetic is short-circuited by an early hit — which
	# is what makes the pair comparable at all.
	data-replacefirst) echo -e "\t\tlet res = subject.replaceFirst(\"zqx\", with: \"y\")\n\t\ttotal = total + res.byteLength()" ;;
	data-replace) echo -e "\t\tlet res = subject.replace(\"zqx\", with: \"y\")\n\t\ttotal = total + res.byteLength()" ;;
	esac
}

emit_data_program() {
	emit_prelude
	emit_grown

	echo "function main() returns ExitCode"
	echo -e "\tvar total = 0"
	echo -e "\tlet subject = grown(\"$(seed_bytes)\", doublings: $N)"
	echo -e "\tlet units = subject.byteLength()"
	echo -e "\tlet t0 = __Builtins.currentTimeNanos()"
	echo -e "\tvar r = 0"
	echo -e "\twhile r < $REPS 'rep'"
	emit_data_body
	echo -e "\t\tr = r + 1"
	echo -e "\tend 'rep'"
	echo -e "\tlet t1 = __Builtins.currentTimeNanos()"
	echo -e "\tprint(\"$MODE-$SEED,{subject.byteLength()},{units},{t1 - t0},$REPS\\\\n\")"
	echo -e "\treturn 0 if total >= 0 else 1"
	echo "end 'main'"
}

emit_loop_program() {
	emit_prelude

	echo "function main() returns ExitCode"
	echo -e "\tvar total = 0"
	echo -e "\tlet subject = \"$(seed_bytes)ij\""
	echo -e "\tlet trips = $((1 << N))"
	echo -e "\tlet units = trips"
	echo -e "\tlet t0 = __Builtins.currentTimeNanos()"
	echo -e "\tvar i = 0"
	echo -e "\twhile i < trips 'trip'"
	case "$MODE" in
	loop-tobytes) echo -e "\t\tlet c = subject.toByteArray()\n\t\ttotal = total + c.count()" ;;
	loop-clone) echo -e "\t\tlet c = subject.clone()\n\t\ttotal = total + c.byteLength()" ;;
	loop-codepoints) echo -e "\t\tlet c = subject.codepoints()\n\t\ttotal = total + c.count()" ;;
	loop-utf16) echo -e "\t\tlet c = subject.utf16()\n\t\ttotal = total + c.count()" ;;
	# THE CONTROL: a per-trip owned String from machinery this rung did not touch, so the slab's own
	# per-trip growth is separated from anything slice E added.
	loop-control) echo -e "\t\tvar acc = subject\n\t\tacc.append(subject)\n\t\ttotal = total + acc.byteLength()" ;;
	esac
	echo -e "\t\ti = i + 1"
	echo -e "\tend 'trip'"
	echo -e "\tlet t1 = __Builtins.currentTimeNanos()"
	echo -e "\tprint(\"$MODE-$SEED,{subject.byteLength()},{units},{t1 - t0},1\\\\n\")"
	echo -e "\treturn 0 if total >= 0 else 1"
	echo "end 'main'"
}

case "$FAMILY" in
sites) emit_sites_program >"$OUT" ;;
data) emit_data_program >"$OUT" ;;
loop) emit_loop_program >"$OUT" ;;
esac
