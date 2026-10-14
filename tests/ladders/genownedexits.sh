#!/usr/bin/env bash
# Ladder generator for the SHARED CLEANUP CHAINS whose exits are not a plain prefix of the owned bindings
# (`Parser.emitFunctionExit`, `Parser.tryBlockThrowEdgeTarget`).
#
# Usage: genownedexits.sh <n> <handoff|fresh|reassign|lanes|box> <outfile>
#
#   handoff   `n` owned Strings, then `n` guarded `return s_i` of ONE of them: each exit drops all the others.
#   fresh     `n` times: an owned String, then a guarded `return` of a fresh String. The chain grows by one
#             node per exit, so its blocks are created in the reverse of the order they run.
#   reassign  one low owned `var` reassigned between `n` pairs of (owned String, guarded `return`).
#   lanes     six low owned `var`s, every one of them reassigned between each of `n` pairs of (owned String,
#             guarded `return`): the most owners whose current values the shared chain carries as arguments.
#   box       a block-form `try` binding `n` boxed errors, then `n` owned Strings, then `n` guarded
#             `throw e_i` of one of the boxes: each routed throw drops the Strings and the other boxes.
#
# Every String is an interpolation written in place, not a call: a call per binding leaves `n` values live
# across `n` calls, which the register allocator pays for on its own and which would drown the cleanup.
set -euo pipefail

if [ $# -ne 3 ]; then
	echo "usage: genownedexits.sh <n> <handoff|fresh|reassign|lanes|box> <outfile>" >&2
	exit 2
fi

n=$1; kind=$2; out=$3

case $kind in
	handoff|fresh|reassign|lanes|box) ;;
	*) echo "genownedexits.sh: kind must be handoff, fresh, reassign, lanes or box, got '$kind'" >&2; exit 2 ;;
esac

q="'"

{
	printf 'typealias Word = int(0 to u32.max)\n\n'
	if [ "$kind" = box ]; then
		printf 'union Failure implements Error\n\tdetail(message String)\nend %sFailure%s\n\n' "$q" "$q"
	fi

	case $kind in
		handoff|fresh) printf 'function work(seed Word) returns String\n' ;;
		reassign|lanes|box) printf 'function work(seed Word) returns Word\n' ;;
	esac

	case $kind in
		handoff)
			for ((i = 0; i < n; i++)); do
				printf '\tlet s%d = "{seed}-%d"\n' $i $i
			done
			printf '\n'
			for ((i = 0; i < n; i++)); do
				printf '\tif seed == %d %sexit%d%s\n\t\treturn s%d\n\tend %sexit%d%s\n\n' $i "$q" $i "$q" $i "$q" $i "$q"
			done
			printf '\treturn "{seed}"\nend %swork%s\n\n' "$q" "$q"
			;;
		fresh)
			for ((i = 0; i < n; i++)); do
				printf '\tlet s%d = "{seed}-%d"\n\n\tif seed == %d %sexit%d%s\n\t\treturn "{seed}-%d"\n\tend %sexit%d%s\n\n' $i $i $i "$q" $i "$q" $((i + 1000)) "$q" $i "$q"
			done
			printf '\tvar total = seed\n'
			for ((i = 0; i < n; i++)); do
				printf '\ttotal = (total + (s%d.byteLength() as Word)) and 0xFFFF\n' $i
			done
			printf '\n\treturn "{total}"\nend %swork%s\n\n' "$q" "$q"
			;;
		reassign)
			printf '\tvar low = "{seed}-low"\n\n'
			for ((i = 0; i < n; i++)); do
				printf '\tlet s%d = "{seed}-%d"\n\tlow = "{seed}-%d"\n\n\tif seed == %d %sexit%d%s\n\t\treturn %d\n\tend %sexit%d%s\n\n' $i $i $((i + 1000)) $i "$q" $i "$q" $i "$q" $i "$q"
			done
			printf '\tvar total = seed + (low.byteLength() as Word)\n'
			for ((i = 0; i < n; i++)); do
				printf '\ttotal = (total + (s%d.byteLength() as Word)) and 0xFFFF\n' $i
			done
			printf '\n\treturn total\nend %swork%s\n\n' "$q" "$q"
			;;
		lanes)
			for ((l = 0; l < 6; l++)); do
				printf '\tvar low%d = "{seed}-low%d"\n' $l $l
			done
			printf '\n'
			for ((i = 0; i < n; i++)); do
				printf '\tlet s%d = "{seed}-%d"\n' $i $i
				for ((l = 0; l < 6; l++)); do
					printf '\tlow%d = "{seed}-%d"\n' $l $((i + 1000))
				done
				printf '\n\tif seed == %d %sexit%d%s\n\t\treturn %d\n\tend %sexit%d%s\n\n' $i "$q" $i "$q" $i "$q" $i "$q"
			done
			printf '\tvar total = seed + (low0.byteLength() as Word) + (low1.byteLength() as Word) + (low2.byteLength() as Word) + (low3.byteLength() as Word) + (low4.byteLength() as Word) + (low5.byteLength() as Word)\n'
			for ((i = 0; i < n; i++)); do
				printf '\ttotal = (total + (s%d.byteLength() as Word)) and 0xFFFF\n' $i
			done
			printf '\n\treturn total\nend %swork%s\n\n' "$q" "$q"
			;;
		box)
			printf '\tvar total = seed\n\n\ttry %sblock%s\n' "$q" "$q"
			for ((i = 0; i < n; i++)); do
				printf '\t\tlet e%d = Failure.detail("{seed}-%d")\n' $i $i
			done
			for ((i = 0; i < n; i++)); do
				printf '\t\tlet s%d = "{seed}-%d"\n' $i $((i + 1000))
			done
			printf '\n'
			for ((i = 0; i < n; i++)); do
				printf '\t\tif seed == %d %sexit%d%s\n\t\t\tthrow e%d\n\t\tend %sexit%d%s\n\n' $i "$q" $i "$q" $i "$q" $i "$q"
			done
			for ((i = 0; i < n; i++)); do
				printf '\t\ttotal = (total + (s%d.byteLength() as Word)) and 0xFFFF\n' $i
			done
			printf '\tend %sblock%s otherwise (caught) %shandler%s\n\t\tmatch caught %skind%s\n\t\t\tdetail(message) then total = total + (message.byteLength() as Word)\n\t\tend %skind%s\n\tend %shandler%s\n\n\treturn total\nend %swork%s\n\n' "$q" "$q" "$q" "$q" "$q" "$q" "$q" "$q" "$q" "$q" "$q" "$q"
			;;
	esac

	printf 'function main() returns ExitCode\n\tlet spelled = try Process.environmentVariable("PATH") otherwise ""\n\tlet seed = (spelled.byteLength() and 0xFFFF) as Word\n\tlet answer = work(seed)\n'
	case $kind in
		handoff|fresh) printf '\n\treturn 0 if answer.byteLength() > 0 else 1\nend %smain%s\n' "$q" "$q" ;;
		reassign|lanes|box) printf '\n\treturn 0 if answer > 0 else 1\nend %smain%s\n' "$q" "$q" ;;
	esac
} > "$out"
