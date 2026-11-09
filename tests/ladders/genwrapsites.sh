#!/usr/bin/env bash
# THE UNSIGNED WRAP-GUARD LADDER — `+`, `-` and `*` over a full-width unsigned alias, each checked where it
# is computed.
#
#     Does `insertRangeChecks` cost O(unsigned operations), or O(operations x blocks in the function)?
#
# Usage: genwrapsites.sh <n> <add|sub|mulvar|mulconst> <onefunc|spread> <outdir>
#
# Emits `n` operations over `Quantity = int(0 to u64.max)`, every operand routed through `qOpaque` so no
# constant or counted-loop interval proves a result in range and each operation keeps its guard: all in
# ONE function (`onefunc`) or one per function (`spread`, the control for per-function concentration).
# `add`, `sub` and `mulconst` (a multiply by 3) emit one compare each; `mulvar` multiplies two variables,
# which is one high multiply on x64 and arm64 and the portable witness on wasm32.
#
# `ScaleCorpus` reaches no unsigned operation guard, so this ladder is the only instrument over
# `wrapGuardOf`. The programs are compiled, not run: `sub` borrows and `onefunc mulvar` overflows when run.
set -euo pipefail

if [ $# -ne 4 ]; then
	echo "usage: genwrapsites.sh <n> <add|sub|mulvar|mulconst> <onefunc|spread> <outdir>" >&2
	exit 2
fi

N="$1"
OP="$2"
SHAPE="$3"
OUT="$4"

case "$OP" in
	add) EXPR='acc + qOpaque(a + QI)' ;;
	sub) EXPR='acc - qOpaque(a)' ;;
	mulvar) EXPR='qOpaque(acc) * qOpaque(a)' ;;
	mulconst) EXPR='(qOpaque(acc) * 3) and qOpaque(a)' ;;
	*) echo "genwrapsites.sh: op must be add, sub, mulvar or mulconst, got '$OP'" >&2; exit 2 ;;
esac

case "$SHAPE" in
	onefunc|spread) ;;
	*) echo "genwrapsites.sh: shape must be onefunc or spread, got '$SHAPE'" >&2; exit 2 ;;
esac

mkdir -p "$OUT"
rm -f "$OUT"/*.maxon

cat > "$OUT/a_prelude.maxon" <<'PRELUDE'
export typealias Quantity = int(0 to u64.max)
export typealias LadderInt = int(i64.min to i64.max)

export function qOpaque(a Quantity) returns Quantity
	if a > 100 'big'
		return a and 4095
	end 'big'
	return a
end 'qOpaque'
PRELUDE

if [ "$SHAPE" = "onefunc" ]; then
	{
		echo "export function many(a Quantity) returns Quantity"
		echo "	var acc = 1 as Quantity"
		for ((i = 0; i < N; i++)); do
			echo "	acc = ${EXPR//QI/$i}"
		done
		echo "	return acc"
		echo "end 'many'"
	} > "$OUT/b_sites.maxon"

	{
		echo "function main() returns ExitCode"
		echo "	print(\"{(many(7) as LadderInt)}\")"
		echo "	return 0"
		echo "end 'main'"
	} > "$OUT/z_main.maxon"
else
	{
		for ((i = 0; i < N; i++)); do
			echo "export function one$i(a Quantity, acc Quantity) returns Quantity"
			echo "	return ${EXPR//QI/$i}"
			echo "end 'one$i'"
		done
	} > "$OUT/b_sites.maxon"

	{
		echo "function main() returns ExitCode"
		echo "	var t = 0 as LadderInt"
		for ((i = 0; i < N; i++)); do
			echo "	t = t + (one$i(7, acc: 3) as LadderInt)"
		done
		echo "	print(\"{t}\")"
		echo "	return 0"
		echo "end 'main'"
	} > "$OUT/z_main.maxon"
fi

echo "genwrapsites.sh: wrote $OP $SHAPE ladder with n=$N to $OUT"
