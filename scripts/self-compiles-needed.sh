#!/usr/bin/env bash
#
# One self-compile, or two?
#
# ⭐⭐ **THE RULE IS NARROW AND WAS BEING APPLIED BROADLY.** A change under `Compiler/Runtime/` needs
# TWO self-compiles, because the compiler emits the runtime into every program it builds including
# itself: the first build fixes the emitter, the second gives the compiler its own fixed runtime.
# Every other change needs ONE. But the condition was written as *"whenever the seed you built with
# predates a runtime change"* — a thing nobody can evaluate in their head, so the safe answer was
# always "build twice", and a needless self-compile is ninety seconds off every task.
#
# ⭐ **IT IS ANSWERABLE EXACTLY, because the compiler stamps the commit it was built from.**
# `maxon version` reports it, so "has any runtime file changed since the slot binary was built" is a
# `git diff` and not a judgement.
#
# ⛔ **ASK IT BEFORE THE FIRST BUILD, NOT BETWEEN THE TWO.** `C1` and `C2` are both built from the
# same sources and stamp the same commit, so nothing can tell them apart from the outside — after the
# first of two builds this reports `once`, which is still true (one more build is needed) but reads
# like "you are done". The answer describes the work remaining from the slot AS IT IS NOW, and asking
# again mid-sequence tells you nothing new.
#
# Prints `once` or `twice` on stdout and the reason on stderr. Exits 0 either way — this is an
# answer, not a verdict.
#
# Usage:
#   scripts/self-compiles-needed.sh

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh

RuntimeDir="maxon-bin/Compiler/Runtime"

# ⚠ EVERY UNCERTAIN ANSWER IS `twice`. Being wrong in that direction costs one self-compile; being
# wrong the other way is a compiler whose own runtime is stale, which reads as a miscompile somewhere
# else entirely — measured once as a Windows-only lane bug that was neither.
answer() { echo "self-compiles-needed: $2" >&2; echo "$1"; exit 0; }

maxon="$(maxon_compiler_path .)"
[ -x "$maxon" ] || answer twice "the slot is empty, so the build runs with the seed — a released binary that predates any runtime work in this tree"

# `maxon version` → `maxon dev (d38c602a5d 2026-09-09) (x64-windows)`
built_from="$("$maxon" version | sed -n 's/.*(\([0-9a-f]\{7,\}\) .*/\1/p')"
[ -n "$built_from" ] || answer twice "the slot binary reports no commit — it was built by a seed, which cannot stamp one"

git cat-file -e "$built_from^{commit}" 2>/dev/null || answer twice "the slot binary names commit $built_from, which is not in this history — it was rebased away or came from elsewhere"

# ⛔ BOTH HALVES, OR THE ANSWER IS WRONG IN THE COSTLY DIRECTION. A committed runtime change since the
# slot was built counts, and so does one sitting uncommitted in the working tree — the compiler about
# to be built carries it either way.
committed="$(git diff --name-only "$built_from..HEAD" -- "$RuntimeDir")"
working="$(git status --porcelain -- "$RuntimeDir")"

if [ -n "$committed$working" ]; then
	{
		echo "self-compiles-needed: the runtime has changed since the slot binary was built ($built_from):"
		[ -z "$committed" ] || printf '%s\n' "$committed" | sed 's|^|  committed  |'
		[ -z "$working" ] || printf '%s\n' "$working" | sed 's|^|  uncommitted |'
	} >&2
	echo twice
	exit 0
fi

answer once "no change under $RuntimeDir/ since the slot binary was built ($built_from)"
