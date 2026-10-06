#!/usr/bin/env bash
# Ladder generator for WHOLE-FUNCTION EXITS PER FUNCTION — the shared exit chain
# (`Parser.emitFunctionExit`).
#
# Usage: genexitdrops.sh <n> <try|throw|return|none> <outfile>
#
# Emits ONE function `work` holding `n` owned `String` bindings, each with an exit: its own
# propagating `try`, or a guarded `throw` or early `return` after it. Every exit owes a drop of
# every binding above it. `none` is the CONTROL: the same bindings with no exit between them.
set -euo pipefail

if [ $# -ne 3 ]; then
	echo "usage: genexitdrops.sh <n> <try|throw|return|none> <outfile>" >&2
	exit 2
fi

n=$1; kind=$2; out=$3

case $kind in
	try|throw|return|none) ;;
	*) echo "genexitdrops.sh: kind must be try, throw, return or none, got '$kind'" >&2; exit 2 ;;
esac

{
	printf 'typealias Word = int(0 to u32.max)\n\n'
	case $kind in
		try|throw) printf 'enum Refusal implements Error\n\trefused\nend '"'"'Refusal'"'"'\n\n' ;;
	esac
	case $kind in
		try) printf 'function make(seed Word, index Word) returns String throws Refusal\n\tif seed == index '"'"'refuse'"'"'\n\t\tthrow Refusal.refused\n\tend '"'"'refuse'"'"'\n\n\treturn "{seed}-{index}"\nend '"'"'make'"'"'\n\n' ;;
		*) printf 'function make(seed Word, index Word) returns String\n\treturn "{seed}-{index}"\nend '"'"'make'"'"'\n\n' ;;
	esac
	case $kind in
		return|none) printf 'function work(seed Word) returns Word\n' ;;
		*) printf 'function work(seed Word) returns Word throws Refusal\n' ;;
	esac
	for ((i = 0; i < n; i++)); do
		case $kind in
			try) printf '\tlet s%d = try make(seed, index: %d)\n' $i $i ;;
			throw) printf '\tlet s%d = make(seed, index: %d)\n\n\tif seed == %d '"'"'exit%d'"'"'\n\t\tthrow Refusal.refused\n\tend '"'"'exit%d'"'"'\n\n' $i $i $i $i $i ;;
			return) printf '\tlet s%d = make(seed, index: %d)\n\n\tif seed == %d '"'"'exit%d'"'"'\n\t\treturn %d\n\tend '"'"'exit%d'"'"'\n\n' $i $i $i $i $i $i ;;
			none) printf '\tlet s%d = make(seed, index: %d)\n' $i $i ;;
		esac
	done
	printf '\n\tvar total = seed\n'
	for ((i = 0; i < n; i++)); do
		printf '\ttotal = (total + (s%d.byteLength() as Word)) and 0xFFFF\n' $i
	done
	printf '\n\treturn total\nend '"'"'work'"'"'\n\n'
	printf 'function main() returns ExitCode\n\tlet spelled = try Process.environmentVariable("PATH") otherwise ""\n\tlet seed = (spelled.byteLength() and 0xFFFF) as Word\n'
	case $kind in
		return|none) printf '\tlet answer = work(seed)\n' ;;
		*) printf '\tlet answer = try work(seed) otherwise 0\n' ;;
	esac
	printf '\n\treturn 0 if answer > 0 else 1\nend '"'"'main'"'"'\n'
} > "$out"
