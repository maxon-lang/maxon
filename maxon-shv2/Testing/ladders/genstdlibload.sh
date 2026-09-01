#!/usr/bin/env bash
# THE STDLIB-LOAD LADDER — the one question the SHARED corpus structurally cannot ask:
#
#     Does `phase:parse` cost O(user corpus x modules loaded), or O(user corpus) + O(modules)?
#
# ⭐ **WHY IT MATTERS FAR MORE THAN TODAY'S NUMBER.** shv2 loads ALL of `stdlib/` — every `.maxon` file in this
# checkout, less the one named exclusion (`StdlibLoader.SupersededRuntimeModule`, `Internals.maxon`) —
# on EVERY compile, whether or not the program names any of them. If a per-compile cost is
# proportional to the PRODUCT of corpus size and modules loaded, that is a genuine superlinearity
# every user of the compiler pays. ⚠ **THIS LADDER WAS WRITTEN (2026-07-28) WHILE THE LOADER STILL
# CARRIED A WHITELIST** that named 3, then 16, of those files one widening rung at a time, and the
# question then was whether the whitelist's own intended end state — naming every file — was safe to
# reach. **It was reached on 2026-08-31, and the whitelist was deleted.** The question the ladder
# asks did not change with it: it was always about modules LOADED, never about the filter that chose
# them, and its readings below already priced the end state directly (module counts up to 64, more
# than `stdlib/` holds). `StdlibLoader`'s header reasoned about an O(files x listed) membership test
# and chose a hash for it, so the authors were alert to exactly this class; this ladder asks whether
# a SECOND instance exists one phase downstream.
#
# ⚠ **THE SHARED LADDER CANNOT ASK IT, AND NOT FOR THE USUAL REASON.** The usual blindness is a
# construct the corpus never emits (see `gentrim.sh`'s header). This one is different and worse: the
# shared corpus DOES exercise the cost — every rung loads the same stdlib — but it
# moves BOTH axes at once and can only ever move one of them (the corpus doubles; the module count is
# fixed at whatever `stdlib/` holds that day). A product term and a sum term are indistinguishable
# when one factor never varies. **This ladder varies the MODULE COUNT with the corpus held, which is
# the axis the shared instrument does not have a knob for.**
#
# ⭐ **THE EXTRA MODULES ARE ORDINARY USER FILES, AND THAT IS THE POINT, NOT A COMPROMISE.**
# `StdlibLoader` registers a stdlib module into the query database (`fileChanged`) exactly like a user
# source, so it flows through the SAME tokenize -> signature-index -> parse -> merge spine with no
# query-spine change. A stdlib file is not a special kind of input; it is an input. So the question
# generalizes, with nothing lost, to *"does parse cost scale with the product of user code and total
# declarations in the program?"* — which is measurable with plain files and no compiler edit, at any
# module count, including counts larger than `stdlib/` itself.
#   ⇒ The confirmation that the generalization holds is a REAL loader A/B — rebuild shv2 with a stdlib
#     module withheld and diff the two binaries over one corpus. Done the day this was written (when
#     the loader still chose its modules from a whitelist, so the A/B was made by removing an entry),
#     and the two routes agree to FOUR ALLOCATIONS OUT OF 1,650: the real `helpers/string/utf16.maxon`
#     dropped in as an ordinary USER file (this ladder's `real` mode) costs **+1,650** `phase:parse`
#     allocations, flat at corpus rung 0 AND rung 5; the same file reached through the LOADER costs
#     **+1,654**, flat at all six rungs. The four are the loader's own — one more `File.exists`, one
#     more set insert, one more `join`. Do the rebuild when a reading here is surprising; do not do it
#     FIRST, because it costs a compiler build where this costs a shell loop.
#
# Usage: genstdlibload.sh <n> <mode> <outdir>
#   <n>    = USER CORPUS SIZE, in functions. THE DOUBLING KNOB. Spread FUNCS_PER_FILE to a file, so
#            the file count doubles with it — which matters, because a per-FILE term and a per-corpus
#            term are different findings and the extra modules are themselves files.
#   mode   = synth | real
#            `synth` — MODULES generated modules, each a SHAPE MATCH for `stdlib/helpers/string/utf16.maxon`:
#                      DECLS_PER_MODULE exported free functions over two ranged typealiases, pure
#                      arithmetic bodies, no imports, NEVER CALLED by the corpus. Uniquely named, so
#                      any count is legal (E3006 would catch a collision loudly).
#            `real`  — MODULES copies of the ACTUAL `stdlib/helpers/string/utf16.maxon`, with every
#                      declared name suffixed so the copies do not collide. The check that `synth` is
#                      pricing the same thing the rung actually shipped.
#   outdir = the project directory to write (CREATED AND OVERWRITTEN; its `*.maxon` are pruned first).
#
# Env knobs:
#   MODULES          how many extra modules (default 1). ⭐ **THIS IS THE INDEPENDENT KNOB.** Hold <n>
#                    and move this, and the product term separates from the sum term: if parse cost is
#                    a + b*C + c*M, the slope in C does not move when M does. If it is a + b*C + c*M +
#                    d*C*M, it does.
#   DECLS_PER_MODULE declarations per generated module (default 9 — utf16.maxon's function count).
#   FUNCS_PER_FILE   corpus functions per corpus file (default 8).
#
#   e.g. MODULES=0  genstdlibload.sh 256 synth p0/     # the CONTROL: corpus alone
#        MODULES=16 genstdlibload.sh 256 synth p16/    # same corpus, 16 extra modules
#        MODULES=1  genstdlibload.sh 512 real  pr/     # the real utf16.maxon, once, over a 512-fn corpus
#
# Read it with `--metrics`, whose `phase` rows carry the exact allocation counts:
#
#     maxon-shv2 build p16/ -o temp/p16.exe --metrics=temp/p16.tsv
#     grep -P '^phase\tparse' temp/p16.tsv
#
# ⚠ **READ THE ALLOCATION COLUMN, NOT THE CPU ONE.** Allocations are exact and bit-reproducible for
# the same input; the question here is a slope of a few allocations per file, which is far inside the
# CPU column's few-percent noise band and could not be read there at all.
#
# ⚠ **THE CORPUS BODIES MUST NOT FOLD AWAY.** shv2 runs a real `foldConstOperands`, so a corpus of
# constant arithmetic compiles to nothing and every curve is a beautiful straight line through zero.
# Every generated corpus function therefore takes its inputs as PARAMETERS and every call site passes
# a value derived from `main`'s argument count, so nothing is a compile-time constant.
#
# --- WHAT IT READ ON THE DAY IT WAS BUILT (2026-07-28, branch `p18e-string-views`), so a later run has
#     a BEFORE to compare against rather than only a shape to re-derive ---
#
#   ⭐⭐ **THE VERDICT: `phase:parse` IS A SUM, NOT A PRODUCT — AND THE READING IS EXACT, NOT
#   APPROXIMATE.** Allocations are bit-reproducible, and the extra cost of an extra module came out
#   IDENTICAL TO THE ALLOCATION at every corpus size tried:
#
#     MODULES        1        4        16        64      (extra `phase:parse` allocations vs MODULES=0)
#     C=128     +1,914   +7,656   +30,632   +122,512
#     C=256     +1,914   +7,656   +30,632   +122,512
#     C=512     +1,914   +7,656   +30,632   +122,512
#
#   Exactly linear in the module count, exactly invariant to the corpus. (C=64 reads +1,922 / +7,664 /
#   +30,640 / +122,520 — eight allocations more, flat across the module axis, so it is an artefact of
#   the smallest corpus and not a slope.)
#
#   ⭐ **AND THE SAME KNOB ON THE REAL SHARED CORPUS SAYS THE SAME THING**, which is what makes the
#   user-file generalization safe to rely on. Dropping M synthetic modules into `scale-test`'s own
#   emitted rung 0 (30 files, 4,890 lines) and rung 5 (185 files, 140,917 lines) — a 32x span:
#
#     MODULES        1        4        16        64
#     rung 0    +1,914   +7,656   +30,632   +122,512
#     rung 5    +1,914   +7,656   +30,632   +122,512
#
#   ⭐ **THE REAL STDLIB MODULE BEHAVES IDENTICALLY, MEASURED THREE-BINARY.** `head` (the slice),
#   `nowl` (the slice with ONLY `helpers/string/utf16.maxon` withheld from the loader) and `base` (the
#   parent commit) were built by the same bootstrap and driven interleaved from equal-length paths.
#   `head - nowl` — that one module and nothing else — is **+1,654 `phase:parse` allocations at EVERY ONE
#   of the six rungs**, plus +659 lex, +212 signatures, ~+400 merge, for a total of +4,453 at rung 0
#   and +4,444 at rung 5. FLAT. That is the module's own lex/sweep/parse and nothing per unit of user
#   code.
#
#   ⚠ **THE DOCUMENTED `O(files x types)` HAZARD WAS TESTED SEPARATELY AND IS ALSO ABSENT.**
#   `ProgramSignatures.arrayLiteralElementsInterned` exists because array-literal instance interning
#   once WAS O(files x types), so modules made of nine functions with no literals would have been a
#   soft target. Repeated with modules that each declare a ranged alias, an `Array with <that alias>`
#   and both a `[…]` and a `b"…"` literal: `phase:parse` reads +8,936 (M=16) and +35,728 (M=64) at
#   rung 0 and **the same +8,936 / +35,728** at rung 5. The `signatures` and `merge` deltas are very
#   slightly SMALLER on the bigger corpus (-55 / -126 and -26 / -80), because the corpus has already
#   interned some of what the module wants — the only cross term that exists, and it has the wrong
#   SIGN to be a hazard: an extra module gets marginally CHEAPER as the program grows.
#
#   ⇒ **THE LOADER'S END STATE IS SAFE ON THIS AXIS — and that end state is now what SHIPS.** The 51
#   modules loaded today (52 on disk less the permanently-excluded `Internals.maxon`) cost the SUM of
#   51 modules' own parses, at any program size. What each module costs is its own file's size; what it
#   costs per unit of user code is ZERO.
#
#   ⚠ **WHAT DID GROW IN THAT RUNG'S DELTA COLUMN WAS NOT THE STDLIB LOAD**, and the three-binary split
#   is what separates them: `nowl - base` — the rest of slice E — is **+24, +48, +96, +192, +384, +768**
#   parse allocations across the six rungs, an exact x2.00 doubling, i.e. strictly linear in program
#   size. It is **+3 allocations per TRIVIAL ARRAY-ELEMENT READ** (`emitArrayElementAccessor` now asks
#   `arrayElementValueType`, which `adoptType`-interns the element's declared name, where it used to
#   hardcode `ValueTypeTag.integer`) — verified in isolation at 8/16/32/64 `.get()` sites reading
#   +24/+48/+96/+192, and confirmed by deleting `u_arrays.maxon` from the corpus, which takes the whole
#   delta to EXACTLY ZERO in both parse and merge. It is the price of the type-identity fix that makes
#   a `Byte` read back out of an `Array with Byte` still a `Byte`.
set -euo pipefail
N="$1"; MODE="$2"; OUT="$3"

case "$MODE" in
synth | real) ;;
*)
	echo "genstdlibload.sh: unknown mode '$MODE' (want synth | real)" >&2
	exit 2
	;;
esac

if [ "$N" -lt 1 ]; then echo "genstdlibload.sh: <n> must be >= 1" >&2; exit 2; fi

MODULES="${MODULES:-1}"
DECLS_PER_MODULE="${DECLS_PER_MODULE:-9}"
FUNCS_PER_FILE="${FUNCS_PER_FILE:-8}"

# The real module the `real` mode copies. Resolved off THIS script's own location rather than off the
# caller's working directory, so the ladder can be driven from anywhere.
LADDER_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REAL_MODULE="$LADDER_DIR/../../../stdlib/helpers/string/utf16.maxon"

mkdir -p "$OUT"
# Prune only `.maxon`, never the directory: an `-o` output or a metrics TSV a caller parked here is
# theirs, and a generator that removed it would be deleting evidence.
find "$OUT" -maxdepth 1 -name '*.maxon' -delete

emit_prelude() {
	echo "typealias Int = int(i64.min to i64.max)"
	echo "typealias ExitCode = int(0 to 255)"
	echo ""
}

# --- THE CORPUS. Ordinary user code that names NONE of the extra modules, because the question is what
# an extra module costs a program that does not use it — the invariant the loader is expected to hold:
# adding a module changes NOTHING for a program that does not name it.
emit_corpus() {
	FILES=$((N / FUNCS_PER_FILE))
	if [ "$FILES" -lt 1 ]; then FILES=1; fi

	f=0
	while [ "$f" -lt "$FILES" ]; do
		{
			echo "// stdlib-load ladder corpus file $f of $FILES — see maxon-shv2/Testing/ladders/genstdlibload.sh"
			if [ "$f" -eq 0 ]; then emit_prelude; fi

			i=0
			while [ "$i" -lt "$FUNCS_PER_FILE" ]; do
				idx=$((f * FUNCS_PER_FILE + i))
				echo "function corpus${idx}(a Int, b Int) returns Int"
				echo -e "\tvar acc = a"
				# Two `if`s rather than an if/else: shv2 does not accept `else` (E2015), and the
				# corpus this ladder sits beside does not emit one either.
				echo -e "\tif b > a 'gt'"
				echo -e "\t\tacc = acc + b"
				echo -e "\tend 'gt'"
				echo -e "\tif b <= a 'le'"
				echo -e "\t\tacc = acc - b"
				echo -e "\tend 'le'"
				echo -e "\tvar k = 0"
				echo -e "\twhile k < 3 'spin'"
				echo -e "\t\tacc = acc + k"
				echo -e "\t\tk = k + 1"
				echo -e "\tend 'spin'"
				echo -e "\treturn acc"
				echo "end 'corpus${idx}'"
				echo ""
				i=$((i + 1))
			done
		} >"$OUT/c$(printf '%05d' "$f").maxon"
		f=$((f + 1))
	done

	{
		echo "// stdlib-load ladder entry point — see maxon-shv2/Testing/ladders/genstdlibload.sh"
		# ⭐ The seed is a LOOP-DERIVED value, `ScaleCorpus.scaleSeed`'s own trick, so no corpus call has
		# a constant-foldable argument and `foldConstOperands` cannot collapse the program this ladder
		# exists to measure. (shv2's `main` takes no parameters, so an argv-derived seed is not
		# available.)
		echo "function ladderSeed() returns Int"
		echo -e "\tvar acc = 1"
		echo -e "\tvar i = 0"
		echo -e "\twhile i < 4 'seedLoop'"
		echo -e "\t\tacc = acc + i * 3"
		echo -e "\t\ti = i + 1"
		echo -e "\tend 'seedLoop'"
		echo -e "\treturn acc"
		echo "end 'ladderSeed'"
		echo ""
		echo "function main() returns ExitCode"
		echo -e "\tvar sum = ladderSeed()"
		idx=0
		while [ "$idx" -lt $((FILES * FUNCS_PER_FILE)) ]; do
			echo -e "\tsum = corpus${idx}(sum, b: sum + ${idx})"
			idx=$((idx + 1))
		done
		echo -e "\treturn 0 if sum != 999999 else 1"
		echo "end 'main'"
	} >"$OUT/zmain.maxon"
}

# --- THE EXTRA MODULES. A shape match for `stdlib/helpers/string/utf16.maxon`: two ranged typealiases
# and DECLS_PER_MODULE exported free functions over them, pure arithmetic, no imports, never called.
emit_synth_module() {
	M="$1"
	{
		echo "// stdlib-load ladder: synthetic stdlib-shaped module $M — never called by the corpus"
		echo "typealias LadderUnit${M} = int(0 to u16.max)"
		echo "typealias LadderCount${M} = int(1 to 2)"
		echo ""
		d=0
		while [ "$d" -lt "$DECLS_PER_MODULE" ]; do
			echo "export function ladder${M}_fn${d}(unit LadderUnit${M}, other LadderUnit${M}) returns bool"
			echo -e "\treturn unit >= ${d} and other <= 56319"
			echo "end 'ladder${M}_fn${d}'"
			echo ""
			d=$((d + 1))
		done
		echo "export function ladder${M}_width(unit LadderUnit${M}) returns LadderCount${M}"
		echo -e "\tif unit < 1024 'small'"
		echo -e "\t\treturn 1"
		echo -e "\tend 'small'"
		echo -e "\treturn 2"
		echo "end 'ladder${M}_width'"
	} >"$OUT/m$(printf '%05d' "$M")_synth.maxon"
}

# A copy of the real module with every declared name suffixed, so N copies coexist. The suffixing is a
# blunt `sed` over the declared identifiers, which is enough because the file declares them all itself
# and imports nothing.
emit_real_module() {
	M="$1"
	if [ ! -f "$REAL_MODULE" ]; then
		echo "genstdlibload.sh: mode 'real' needs $REAL_MODULE" >&2
		exit 2
	fi

	sed -e "s/\bCodeUnit16\b/CodeUnit16_${M}/g" \
		-e "s/\bUtf16UnitCount\b/Utf16UnitCount_${M}/g" \
		-e "s/\butf16\([A-Za-z]*\)\b/utf16\1_${M}/g" \
		"$REAL_MODULE" >"$OUT/m$(printf '%05d' "$M")_real.maxon"
}

emit_corpus

m=0
while [ "$m" -lt "$MODULES" ]; do
	case "$MODE" in
	synth) emit_synth_module "$m" ;;
	real) emit_real_module "$m" ;;
	esac
	m=$((m + 1))
done
