#!/usr/bin/env bash
# Ladder generator for CLAIMANTS PER ALIAS NAME — the spelling of a contested alias in a range
# guard's panic message (`aliasSpelledForReader`).
#
# Usage: gencontestedalias.sh <n> <outdir>
#
# Emits `n` directories `ns0`…`ns<n-1>`, each declaring `RcScore = int(0 to 255)`, and one function
# per directory casting a non-constant value through its own directory's `RcScore`. Guards and
# claimants of the one name both grow with `n`, so a per-guard walk over the claimants reads as a
# rising ratio in `phase:insertRangeChecks`.
set -euo pipefail

if [ $# -ne 2 ]; then
	echo "usage: gencontestedalias.sh <n> <outdir>" >&2
	exit 2
fi

N="$1"
OUT="$2"

mkdir -p "$OUT"
rm -f "$OUT"/*.maxon
rm -rf "$OUT"/ns*

cat > "$OUT/a_prelude.maxon" <<'PRELUDE'
export typealias LadderInt = int(i64.min to i64.max)

export function rcOpaque(a LadderInt) returns LadderInt
	if a > 100 'big'
		return a - 100
	end 'big'
	return a + 1
end 'rcOpaque'
PRELUDE

for ((i = 0; i < N; i++)); do
	mkdir -p "$OUT/ns$i"
	echo "public typealias RcScore = int(0 to 255)" > "$OUT/ns$i/mod.maxon"
done

{
	for ((i = 0; i < N; i++)); do
		echo "export function rcCast$i(a LadderInt) returns LadderInt"
		echo "	let c = (rcOpaque(a + $i) and 255) as ns$i.RcScore"
		echo "	return c as LadderInt"
		echo "end 'rcCast$i'"
	done
} > "$OUT/b_casts.maxon"

{
	echo "function main() returns ExitCode"
	echo "	var acc = 0"
	for ((i = 0; i < N; i++)); do
		echo "	acc = acc + rcCast$i(7)"
	done
	echo "	print(\"{acc}\")"
	echo "	return 0"
	echo "end 'main'"
} > "$OUT/z_main.maxon"

echo "gencontestedalias.sh: wrote ladder with n=$N to $OUT"
