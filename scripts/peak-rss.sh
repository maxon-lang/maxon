#!/usr/bin/env bash
# PEAK RESIDENT SET SIZE of a child process — the ONE thing the compiler's own counters
# structurally cannot see.
#
# ⭐ WHY THIS EXISTS AS A SCRIPT. Until now this measurement lived only as DEAD COMMENT TEXT
# inside two ladder generators (`tests/ladders/genstringviews.sh:213`, `gentrim.sh:119`),
# which each told the reader to type a PowerShell one-liner by hand. Every number produced
# that way is unreproducible by anyone who has not read that comment.
#
# ⚠⚠ IT IS NOT A SUBSTITUTE FOR THE IN-PROCESS COUNTERS, AND THEY ARE NOT A SUBSTITUTE FOR IT.
# They answer different questions and BOTH are needed:
#
#   • `__Builtins.mmAllocTotal()` / `mmAllocLive()` / `mmAllocBytes()`, and the byte count
#     `__Builtins.scavengeMemory()` returns, are EXACT, deterministic and bit-reproducible. They
#     count what the program ASKED FOR and what the allocator HANDED BACK.
#   • PEAK RSS (this script) is noisy and cannot be trended — but it is the only instrument that
#     can see whether DECOMMITTED PAGES ACTUALLY LEFT THE RESIDENT SET. A scavenger that
#     decommits correctly and one that only marks its bookkeeping produce the SAME released-byte
#     figure and different RSS. (Measured: 566.62 MB → 320.20 MB across a scavenge, against a
#     self-reported 247.06 MiB released — the two agree, which is the point of having both.)
#
# ⛔ This header previously cited `__slab_committed_bytes` / `__slab_committed_peak`. **Those
# counters were specified in the plan and never built.** A comment naming an instrument that does
# not exist sends the next reader looking for it; corrected here rather than left standing.
#
# ⚠ THE CHILD'S EXIT CODE IS REPORTED AND NEVER SWALLOWED. A program that crashed or tripped
# the leak gate (101) has not produced a valid memory measurement, and a harness that printed
# only a peak would read a crash as a very good result.
#
# ⚠ IT REFUSES RATHER THAN REPORTING 0. An unknown platform or an unreadable figure exits 3
# with a reason; 0 bytes would read as a perfect result.
#
# Usage:  scripts/peak-rss.sh <command> [args...]
#         scripts/peak-rss.sh --repeat N <command> [args...]   # reports the MAX across N runs
#
# Output (stable and greppable; the child's own stdout/stderr pass through unchanged):
#         peak_rss_bytes=<n>
#         peak_rss_mb=<n.nn>
#         exit=<code>
#
# Exit status: this script exits with the CHILD's status, so it composes in a pipeline.
set -u

repeat=1
if [ "${1:-}" = "--repeat" ]; then
	repeat="$2"; shift 2
fi

if [ "$#" -eq 0 ]; then
	echo "peak-rss.sh: no command given" >&2
	echo "usage: scripts/peak-rss.sh [--repeat N] <command> [args...]" >&2
	exit 2
fi

uname_s="$(uname -s)"
best=0
child_exit=0

# Single-quote a string for embedding in a PowerShell literal ('' escapes a quote).
psq() { printf "'%s'" "$(printf '%s' "$1" | sed "s/'/''/g")"; }

for _ in $(seq 1 "$repeat"); do
	bytes=0
	case "$uname_s" in
	MINGW*|MSYS*|CYGWIN*)
		# Windows: a JOB OBJECT, because every spelling of `$p.PeakWorkingSet64` reads EMPTY once
		# the child has exited. See `scripts/lib/peak-rss.ps1` for the measurement and the three
		# spellings that were tried first.
		#
		# ⚠ Paths reach PowerShell via `cygpath -m` (Windows drive, FORWARD slashes), never `-w`.
		# A backslash path has to survive two layers of quoting, and getting that wrong is a
		# silent wrong path rather than an error.
		tmpdir="$(mktemp -d)"
		exe="$1"; shift
		[ -f "$exe" ] && exe="$(cygpath -m "$(cd "$(dirname "$exe")" && pwd)/$(basename "$exe")")"
		helper="$(cygpath -m "$(cd "$(dirname "$0")" && pwd)/lib/peak-rss.ps1")"
		# Arguments go through a FILE, one per line — see the helper's own note. A bare `-o` in a
		# PowerShell array argument binds as a PARAMETER NAME, so any child carrying a dash-flag
		# (every `build ... -o out` invocation) would break the harness rather than the child.
		printf '%s\n' "$@" > "$tmpdir/args.txt"
		out="$(powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$helper" \
			-Exe "$exe" -OutFile "$(cygpath -m "$tmpdir/out.txt")" \
			-ErrFile "$(cygpath -m "$tmpdir/err.txt")" \
			-ArgsFile "$(cygpath -m "$tmpdir/args.txt")" 2>&1 | tr -d '\r')"
		[ -f "$tmpdir/out.txt" ] && cat "$tmpdir/out.txt"
		[ -f "$tmpdir/err.txt" ] && cat "$tmpdir/err.txt" >&2
		bytes="$(printf '%s' "$out" | sed -n 's/^PEAK=//p' | tail -1)"
		child_exit="$(printf '%s' "$out" | sed -n 's/^EXIT=//p' | tail -1)"
		# Surface the helper's own diagnostics when it produced no figure — otherwise a broken
		# measurement is indistinguishable from a quiet one.
		[ -z "${bytes:-}" ] && printf '%s\n' "$out" >&2
		set -- "$exe" "$@"
		rm -rf "$tmpdir"
		;;
	Linux)
		# GNU time reports "Maximum resident set size (kbytes)"; its own exit status is the
		# child's, which is what we propagate.
		tmpf="$(mktemp)"
		/usr/bin/time -v -o "$tmpf" "$@"
		child_exit=$?
		bytes="$(( $(sed -n 's/.*Maximum resident set size (kbytes): *//p' "$tmpf" | tail -1) * 1024 ))"
		rm -f "$tmpf"
		;;
	Darwin)
		# BSD `time -l` reports "maximum resident set size" in BYTES on macOS.
		tmpf="$(mktemp)"
		/usr/bin/time -l "$@" 2>"$tmpf"
		child_exit=$?
		grep -v 'maximum resident set size' "$tmpf" >&2 || true
		bytes="$(sed -n 's/^ *\([0-9][0-9]*\)  *maximum resident set size.*/\1/p' "$tmpf" | tail -1)"
		rm -f "$tmpf"
		;;
	*)
		echo "peak-rss.sh: unsupported platform '$uname_s' — no peak-RSS source known here." >&2
		echo "peak-rss.sh: refusing rather than reporting 0, which would read as a perfect result." >&2
		exit 3
		;;
	esac

	case "${bytes:-}" in
	''|*[!0-9]*)
		echo "peak-rss.sh: could not read a peak-RSS figure for '$*' (got '${bytes:-}')." >&2
		echo "peak-rss.sh: refusing rather than reporting 0, which would read as a perfect result." >&2
		exit 3
		;;
	esac

	[ "$bytes" -gt "$best" ] && best="$bytes"
done

# Two decimals without bc: integer MiB arithmetic, exact at any size this reports.
printf 'peak_rss_bytes=%s\n' "$best"
printf 'peak_rss_mb=%d.%02d\n' "$(( best / 1048576 ))" "$(( (best % 1048576) * 100 / 1048576 ))"
printf 'exit=%s\n' "$child_exit"
exit "$child_exit"
