#!/usr/bin/env bash

set -u

. "$(cd "$(dirname "$0")" && pwd)/lib.sh"

WORK="$MULTICORE_HERE/.compile-all"

require_compiler

mkdir -p "$WORK"
echo "compiler: $MAXON"

built=0

for prog in $(torture_programs); do
	if build_or_report "$prog" "$WORK/$prog" "$WORK/$prog.build.log"; then
		pass "$prog builds"
		built=$((built + 1))
	fi
done

if [ "$built" -eq 0 ]; then
	bad "no program was built"
fi

for entry in $REFUSED_PROGRAMS; do
	prog="${entry%%:*}"
	code="${entry##*:}"
	log="$WORK/$prog.build.log"

	if build_program "$prog" "$WORK/$prog" >"$log" 2>&1; then
		bad "$prog COMPILED, and it must be refused with $code"
	elif grep -q "^error $code:" "$log" && [ "$(grep -c '^error ' "$log")" -eq 1 ]; then
		pass "$prog refused with $code"
	else
		bad "$prog was not refused with $code as its only error"
		cat "$log"
	fi
done

finish_checks
