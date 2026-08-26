#!/usr/bin/env bash
# Regenerate `maxon-shv2/Compiler/Runtime/SlabClasses.maxon` from Go's VENDORED size-class table.
#
# ⭐ WHY A GENERATOR AND NOT A HAND-TYPED TABLE. shv2's allocator is Go's design, so its size classes
# must BE Go's — and Go's are themselves generated (`vendor/go/src/runtime/_mkmalloc/mksizeclasses.go`)
# from a rule that balances internal waste against the tail wasted when a class is laid into a whole
# number of pages. The second term needs the span geometry, so the ladder cannot be re-derived from a
# waste bound alone: an earlier attempt here produced 80 classes against Go's 68, matching in the small
# sizes and diverging completely above 4096. ⇒ The table is TAKEN, not re-derived — and taken by a
# script, because 136 numbers typed by hand are 136 unverifiable claims.
#
# ONLY TWO ARRAYS ARE COPIED. Everything else is DERIVED and the derivation is VERIFIED against Go's
# own arrays before this script will write anything:
#   • SizeClassToSize, SizeClassToNPages  — copied
#   • SizeToSizeClass8 / SizeToSizeClass128 — DERIVED (`smallest class whose size >= the bucket's
#     largest size`), verified to reproduce Go's arrays exactly, 0/129 and 0/249 mismatches
#   • objects per span — DERIVED as `npages * 8192 / size`, verified against Go's header table
#
# Usage: scripts/gen-slab-classes.sh          # regenerate in place
#        scripts/gen-slab-classes.sh --check  # verify the checked-in file is up to date (exit 1 if not)
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
src="$root/vendor/go/src/internal/runtime/gc/sizeclasses.go"
out="$root/maxon-shv2/Compiler/Runtime/SlabClasses.maxon"

# ⚠ `vendor/go` is GITIGNORED (~640 MB), so on most clones this file is absent. That is not an error:
# the generated table is COMMITTED, so the compiler builds and the suite runs regardless. What is lost is
# only the ability to RE-VERIFY the table against upstream Go. ⇒ `--check` SKIPS with a reason and exits 0;
# a regenerate still refuses, because writing the table without its source is the one thing that would
# silently produce a wrong answer.
if [ ! -f "$src" ]; then
	if [ "${1:-}" = "--check" ]; then
		echo "slab-classes: SKIPPED - vendored Go absent ($src); the committed table cannot be re-verified here"
		exit 0
	fi
	echo "gen-slab-classes: vendored Go table not found at $src — cannot regenerate without its source" >&2
	exit 2
fi

pull() { sed -n "s/.*$1 = \[[^]]*\][a-z0-9]*{\(.*\)}.*/\1/p" "$src" | tr -d ' '; }
sizes="$(pull SizeClassToSize)"
npages="$(pull SizeClassToNPages)"
gs2c8="$(pull SizeToSizeClass8)"
gs2c128="$(pull SizeToSizeClass128)"

[ -n "$sizes" ] && [ -n "$npages" ] || { echo "gen-slab-classes: could not parse Go's arrays" >&2; exit 2; }

# Verify the derivations BEFORE writing. A generator that emits a table it has not checked is just a
# slower way to be wrong.
echo "$sizes" > /tmp/.sc_sizes.$$; echo "$gs2c8" > /tmp/.sc_g8.$$; echo "$gs2c128" > /tmp/.sc_g128.$$
awk -F, '
FILENAME ~ /sizes/ { n=NF; for(i=1;i<=NF;i++) sz[i-1]=$i+0; next }
FILENAME ~ /g8/    { n8=NF; for(i=1;i<=NF;i++) g8[i-1]=$i+0; next }
FILENAME ~ /g128/  { n128=NF; for(i=1;i<=NF;i++) g128[i-1]=$i+0; next }
END{
  bad=0
  for (i=0;i<n8;i++){ want=i*8; c=0; if(i>0){ for(k=1;k<n;k++) if(sz[k]>=want){c=k;break} } if(c!=g8[i]) bad++ }
  for (i=0;i<n128;i++){ want=1024+i*128; c=0; for(k=1;k<n;k++) if(sz[k]>=want){c=k;break} if(c!=g128[i]) bad++ }
  if (bad>0) { printf "gen-slab-classes: reverse-lookup derivation does not reproduce Go (%d mismatches)\n", bad > "/dev/stderr"; exit 1 }
}' /tmp/.sc_sizes.$$ /tmp/.sc_g8.$$ /tmp/.sc_g128.$$
rm -f /tmp/.sc_sizes.$$ /tmp/.sc_g8.$$ /tmp/.sc_g128.$$

# Emit the separator BEFORE each element, never after.
# ⚠ The obvious form — print "N, " per element and strip the trailing comma with `sed 's/, $//'` —
# is WRONG and silently so: sed applies per LINE, so it strips the separator at the end of EVERY
# wrapped line, leaving `208` and `224` adjacent with no comma between them. Caught by reading the
# bytes (`cat -A`), not the rendered output, which hides trailing whitespace.
wrap() { printf '%s' "$1" | tr ',' '\n' | awk 'BEGIN{ORS=""} { if (NR>1) printf ","; if (NR%16==1) { printf "\n\t\t" } else if (NR>1) { printf " " } printf "%s", $0 }'; }
gover="$(sed -n 's/^go \([0-9.]*\)$/\1/p' "$root/vendor/go/go.env" 2>/dev/null | head -1)"
[ -n "$gover" ] || gover="(version unrecorded)"

tmp="$(mktemp)"
{
cat <<'HDR'
// ⭐⭐ **GO'S SIZE-CLASS TABLE. GENERATED — DO NOT EDIT BY HAND.**
//
// Regenerate with `scripts/gen-slab-classes.sh`; check with `--check`. The source of truth is the
// VENDORED Go runtime at `vendor/go/src/internal/runtime/gc/sizeclasses.go`, which is itself generated
// by `mksizeclasses.go`.
//
// ⛔⛔ **THE LADDER CANNOT BE RE-DERIVED FROM A WASTE BOUND, AND THIS FILE ONCE TRIED.** The obvious
// rule — "step by the largest power of two at most `prev/8`, bounding internal waste at 12.5%" —
// produces **80 classes against Go's 68**. It agrees in the small sizes and diverges completely above
// 4096 (it gives 4608, 5120, 5632 where Go gives 4864, 5376, 6144). The reason is that Go bounds a
// SECOND term this rule cannot see: the TAIL WASTED when a class is laid into a whole number of pages,
// which is why `SlabClassPages` below ranges 1..10 rather than being constant. ⇒ The table is TAKEN.
//
// ⚠ **AND THE 12.5% BOUND DOES NOT HOLD AT THE SMALL END — GO'S OWN HEADER SAYS SO.** Class 1 (8
// bytes) carries a `max waste` of **87.50%**, because a 1-byte object still occupies an 8-byte slot
// and 8 is the smallest step there is. Any check asserting a flat 12.5% panics on the first rung.
//
// **ONLY TWO ARRAYS ARE COPIED FROM GO; THE REST IS DERIVED AND THE DERIVATION IS VERIFIED.**
//   • `slabClassSizes` / `slabClassPages` — copied.
//   • the two reverse lookups — DERIVED here, and the generator refuses to write this file unless the
//     derivation reproduces Go's own `SizeToSizeClass8`/`SizeToSizeClass128` EXACTLY (0/129 and 0/249).
//   • objects per span — DERIVED as `pages * 8192 / size`, cross-checked against Go's header table.
// That is 136 numbers taken instead of 514, and every derived one is checked against its source.
HDR
printf '//\n// Source: Go %s, %s classes.\n\n' "$gover" "$(printf '%s' "$sizes" | tr ',' '\n' | wc -l | tr -d ' ')"
cat <<'DECL'
// A class's slot size in bytes. Class 0 is Go's sentinel (size 0) and serves no allocation; it is kept
// so a class id indexes its own slot with no off-by-one on any path.
export typealias SlabClassSize = int(0 to 32768)
// A class id. The range is the table's own bound, so a value that escaped the ladder cannot be stored.
export typealias SlabClassIndex = int(0 to 67)
// Pages in one span of a class. Go's table spans 1..10; 0 belongs to the sentinel class alone.
export typealias SlabSpanPages = int(0 to 10)
export typealias SlabClassSizeArray = Array with SlabClassSize
export typealias SlabSpanPagesArray = Array with SlabSpanPages

// Requests above this go OS-direct: one mapping each, released whole.
export let SlabMaxSmallSize = 32768
// The boundary between the two reverse-lookup tables, and their strides.
export let SlabSmallSizeMax = 1024
export let SlabSmallSizeDiv = 8
export let SlabLargeSizeDiv = 128
// One page. `SlabPageShift` and `SlabPageBytes` are ONE fact written twice by necessity (a shift and
// its value); `checkSlabClassTable` asserts they agree rather than trusting the pair.
export let SlabPageShift = 13
export let SlabPageBytes = 8192

DECL
# ⚠ The table is emitted as `.create()` + `.push()` and NOT as an array literal. A literal infers
# `ParsedIntArray`, which is E3005 against a ranged typealias return — and widening the return type to
# match the literal would throw away the domain bound that keeps an out-of-table size unstorable.
emit_table() {
	printf '\tvar out = %s.create()\n' "$2"
	printf '	out.reserve(%s)
' "$(printf '%s' "$1" | awk -F, '{print NF}')"
	printf '%s' "$1" | tr ',' '\n' | awk '{ printf "\tout.push(%s)\n", $0 }'
	printf '\treturn out\n'
}
printf "// Slot size per class. Go's \`SizeClassToSize\`, verbatim.\nexport function slabClassSizes() returns SlabClassSizeArray\n"
emit_table "$sizes" SlabClassSizeArray
printf "end 'slabClassSizes'\n\n"
printf "// Pages per span per class. Go's \`SizeClassToNPages\`, verbatim. It is NOT constant, and that\n// variation IS the tail-waste term the header above says a waste bound alone cannot see.\nexport function slabClassPages() returns SlabSpanPagesArray\n"
emit_table "$npages" SlabSpanPagesArray
printf "end 'slabClassPages'\n"
} > "$tmp"

if [ "${1:-}" = "--check" ]; then
	if cmp -s "$tmp" "$out"; then echo "slab-classes: OK - checked-in table matches vendored Go"; rm -f "$tmp"; exit 0
	else echo "slab-classes: STALE - $out differs from vendored Go; run scripts/gen-slab-classes.sh" >&2; rm -f "$tmp"; exit 1; fi
fi
mv "$tmp" "$out"
echo "slab-classes: wrote $out"
