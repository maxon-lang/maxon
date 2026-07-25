#!/usr/bin/env bash
# Debugger golden gate — AUTO-DISCOVERING.
#
# Every maxon-sharp/DebugSamples/*.expected.txt already states the command that reproduces it. This
# reads that command FROM THE GOLDEN rather than repeating it here, so a new golden is gated the moment
# it lands and the command exists exactly once — in the golden that depends on it. Two forms:
#
#   1. The `--- EXPECTED: maxon debug … ---` marker carries the command inline (complete.expected.txt).
#   2. The marker is generic (`--- EXPECTED --batch STDOUT …---`) and block N's command is the Nth
#      client line in the HOW TO REPRODUCE block (step/values/mcp.expected.txt).
#
# The BUILD command is read from the same block, for the same reason: a sample that needs a build FLAG
# (P4e's trace.maxon needs `--debugstream`, since the trace hooks are opt-in) states it exactly once.
#
# A golden whose HOW TO REPRODUCE names a `maxon mcp` line is an MCP transcript: that server is started
# before its blocks run and stopped after, and its blocks' commands are curl pipelines against it.
#
# Usage: bash scripts/check-debug-goldens.sh [repoRoot]
# Exit 0 = every golden matches. Exit 1 = drift (or a build/extract failure). Exit 2 = cannot run.

set -u

# The tree to gate: the argument, else THIS SCRIPT'S OWN repository. Never a path baked in — a gate
# with someone's checkout hard-coded silently measures the wrong tree on every other machine, and a
# worktree is exactly where that matters most.
if [ $# -ge 1 ]; then
	ROOT="$1"
else
	ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fi
cd "$ROOT" || { echo "bad repoRoot: $ROOT"; exit 2; }
[ -d maxon-sharp ] || { echo "not a Maxon checkout: $ROOT"; exit 2; }

MAXON="$ROOT/bin/maxon.exe"
[ -x "$MAXON" ] || MAXON="$ROOT/bin/maxon"
[ -x "$MAXON" ] || { echo "no compiler at $ROOT/bin — build csharp first"; exit 2; }
G="maxon-sharp/DebugSamples"
WORK="$(mktemp -d)"
FAIL=0; CHECKS=0
MCP_PID=""

# Whatever happens — a drift, a failed extract, an interrupt — the server this gate started must not
# outlive it. A gate that leaks the process it was testing is the defect it exists to catch.
cleanup() {
	[ -n "$MCP_PID" ] && kill "$MCP_PID" 2>/dev/null
	rm -rf "$WORK"
}
trap cleanup EXIT INT TERM

# A golden's HOW TO REPRODUCE block is STRUCTURAL, not pattern-matched: its one-line header, then one
# command per line, ending at the first blank line. Every line in it is a command, and there are exactly
# three kinds — a `maxon build`, a `maxon mcp` server, and everything else, which is a CLIENT command in
# block order. That is why no allow-list of command names appears anywhere below: a pattern that merely
# looked for `maxon` matched the golden's own PROSE describing what the gate does, and started the wrong
# thing. The prose may say anything it likes; only this block is read as instructions.
BLOCK_SCAN='
	FNR == 1 { inblock = 0 }
	/^HOW TO REPRODUCE/ { inblock = 1; next }
	inblock && /^[[:space:]]*$/ { inblock = 0 }
	inblock { line = $0; sub(/^[ \t]+/, "", line) }
'

# The `maxon build` line naming this source, from any golden's HOW TO REPRODUCE block. Empty when no
# golden states one, in which case a plain build is right.
build_line() { awk -v src="$1" "$BLOCK_SCAN"'
	inblock && line ~ /^maxon build/ && index(line, src) { print line; exit }
' "$G"/*.expected.txt; }

echo "== building samples =="
for src in "$G"/*.maxon; do
	[ -e "$src" ] || continue
	cmd="$(build_line "$src")"
	[ -n "$cmd" ] || cmd="maxon build $src"
	# The golden writes `maxon …` for readability; run THIS tree's compiler.
	if ! eval "${cmd/#maxon /$MAXON }" > "$WORK/build.log" 2>&1; then
		echo "  BUILD FAILED: $cmd"; tail -5 "$WORK/build.log"; FAIL=1
	fi
done

# Nth (1-based) CLIENT line from the HOW TO REPRODUCE block — everything that is not a build or a
# server line, in the order the blocks appear.
reproduce_line() { awk -v want="$2" "$BLOCK_SCAN"'
	inblock && line !~ /^maxon build/ && line !~ /^maxon mcp/ {
		n++
		if (n == want) { print line; exit }
	}
' "$1"; }

# The `maxon mcp` server line, if this golden is an MCP transcript.
server_line() { awk "$BLOCK_SCAN"'
	inblock && line ~ /^maxon mcp/ { print line; exit }
' "$1"; }

# The command embedded in the Nth marker, if it carries one. A marker carries a command exactly when it
# is written `--- EXPECTED: <command> ---` — the COLON is the marker of a marker that is an instruction,
# which is how a descriptive `--- EXPECTED --batch STDOUT, BLOCK 2: … ---` cannot be mistaken for one.
marker_cmd() { awk -v want="$2" '
	/^--- EXPECTED/ {
		n++
		if (n == want) {
			if ($0 ~ /^--- EXPECTED:/) {
				line = $0
				sub(/^--- EXPECTED:[ \t]*/, "", line)
				sub(/[ \t]*---[ \t]*$/, "", line)
				sub(/[ \t]*\([^()]*\)[ \t]*$/, "", line)   # trailing parenthetical note
				print line
			}
			exit
		}
	}
' "$1"; }

# The target exe this golden drives, taken from its HOW TO REPRODUCE block. An inline marker command
# may omit the target (`--complete '' (all command words)`) because context implies it; this supplies it.
default_exe() { awk "$BLOCK_SCAN"'
	inblock && line ~ /^maxon debug/ { for (i = NF; i >= 1; i--) if ($i ~ /\.exe$/) { print $i; exit } }
' "$1"; }

# Body of the Nth `--- EXPECTED` block: up to the next marker or EOF, trailing blanks trimmed.
block_body() { awk -v want="$2" '
	/^--- EXPECTED/ { n++; grab = (n == want); next }
	grab && /^--- / { grab = 0 }
	grab { print }
' "$1" | sed -e :a -e '/^[[:space:]]*$/{$d;N;ba' -e '}'; }

# Start the `maxon mcp` server a golden names, and wait for it to say it is listening. A fixed port is
# deliberate: a server that silently moved would be a client talking to something else.
start_mcp() {
	# `exec` so the recorded pid IS the server rather than a subshell that happens to have spawned it.
	# Without it $! named a shell that had already gone, `kill` hit nothing, and the server the gate
	# started outlived the gate — measured, and exactly the leak this whole rung is about.
	eval "exec ${1/#maxon /$MAXON }" > "$WORK/mcp.log" 2>&1 &
	MCP_PID=$!
	for _ in $(seq 1 100); do
		grep -q 'listening on' "$WORK/mcp.log" && return 0
		kill -0 "$MCP_PID" 2>/dev/null || break
		sleep 0.1
	done
	echo "  MCP SERVER FAILED TO START: $1"; sed -n '1,5p' "$WORK/mcp.log"
	MCP_PID=""
	return 1
}

# Stop the server AND CHECK IT STOPPED, escalating if it did not. A gate that leaves the process it was
# testing running is the defect it exists to catch, so the failure is loud rather than swallowed.
stop_mcp() {
	[ -n "$MCP_PID" ] || return 0
	kill "$MCP_PID" 2>/dev/null
	for _ in $(seq 1 30); do
		kill -0 "$MCP_PID" 2>/dev/null || { MCP_PID=""; return 0; }
		sleep 0.1
	done

	kill -9 "$MCP_PID" 2>/dev/null
	sleep 0.5
	if kill -0 "$MCP_PID" 2>/dev/null; then
		echo "  LEAKED: the mcp server (pid $MCP_PID) survived both kills — kill it by hand"
		FAIL=1
	fi
	MCP_PID=""
}

for golden in "$G"/*.expected.txt; do
	[ -e "$golden" ] || continue
	total=$(grep -c '^--- EXPECTED' "$golden")
	[ "$total" -gt 0 ] || { echo "  SKIP  $(basename "$golden") — no EXPECTED block"; continue; }
	echo "== $(basename "$golden") ($total block(s)) =="

	server="$(server_line "$golden")"
	if [ -n "$server" ]; then
		start_mcp "$server" || { FAIL=1; continue; }
	fi

	for i in $(seq 1 "$total"); do
		cmd="$(marker_cmd "$golden" "$i")"
		[ -n "$cmd" ] || cmd="$(reproduce_line "$golden" "$i")"
		if [ -z "$cmd" ]; then
			echo "  FAIL  block $i — no command found (marker has none, HOW TO REPRODUCE has no #$i)"
			FAIL=1; continue
		fi
		# An inline marker command may omit the target exe (`--complete '' (all command words)`) because
		# context implies it; supply the golden's own. ONLY for a `maxon debug` command — a curl pipeline
		# or a shell probe names whatever it acts on itself, and appending an exe to one would corrupt it.
		case "$cmd" in
			maxon\ debug*)
				case "$cmd" in
					*.exe*) ;;
					*) exe="$(default_exe "$golden")"
					   [ -n "$exe" ] && cmd="$cmd $exe" ;;
				esac ;;
		esac
		# The golden writes `maxon …` for readability; run THIS tree's compiler.
		run="${cmd/#maxon /$MAXON }"
		block_body "$golden" "$i" > "$WORK/exp.txt"
		eval "$run" > "$WORK/act.txt" 2>"$WORK/err.txt"
		CHECKS=$((CHECKS + 1))
		if diff --strip-trailing-cr -q "$WORK/exp.txt" "$WORK/act.txt" > /dev/null; then
			echo "  PASS  block $i: ${cmd:0:88}"
		else
			echo "  FAIL  block $i: ${cmd:0:88}"
			diff --strip-trailing-cr "$WORK/exp.txt" "$WORK/act.txt" | head -14
			[ -s "$WORK/err.txt" ] && { echo "    stderr:"; head -3 "$WORK/err.txt"; }
			FAIL=1
		fi
	done

	stop_mcp
done

echo
if [ $FAIL -eq 0 ]; then echo "ALL $CHECKS GOLDEN CHECKS GREEN"; else echo "GOLDENS DRIFTED ($CHECKS checks run)"; fi
exit $FAIL
