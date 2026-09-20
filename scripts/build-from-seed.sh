#!/usr/bin/env bash
#
# Build this tree's compiler from a released seed at `.bootstrap/maxon`: the seed builds `C1`, and
# `C1` builds `C2` into the slot. Prints the result's `maxon version`.
#
# ⛔⛔ **ONE BUILD FROM THE SEED IS NOT THIS TREE'S COMPILER, IN TWO WAYS.**
#
#   * **ITS OWN RUNTIME IS THE SEED'S.** `C1` emits the new runtime into what it builds, but the
#     runtime inside `C1` was emitted by the previous release. `spec-test`'s worker IS the compiler,
#     so a suite run by `C1` exercises last release's scheduler, subprocess and console handling.
#   * **ITS FRONT END RUNS IN PROCESS.** A seed never binds `FrontEndPool.RuntimeMovesSharedRecords`, so `C1`
#     lexes and parses serially, and only a compile `C2` runs reports the pool under `--log=compiler:debug`.
#   * **IT REPORTS `dev`.** A seed older than named manifest targets reads `maxon-bin` as a PATH, so
#     `project.maxon` — where the version is derived from the ref and handed to `--define` — never runs.
#     MEASURED on the 0.1.1 rehearsal: the x64-windows archive came out as `maxon-dev-x64-windows`.
#
# ⛔ **THE SEED'S OUTPUT IS NAMED EXPLICITLY** for the same reason: a seed reading `maxon-bin` as a path
# writes the compiler to a default of its own choosing — MEASURED: `maxon-bin/Profile/ProfileSampler`
# — and exits 0. `-o` is correct under both readings. `C1` builds by NAME, so it runs the manifest.
#
# ⛔ **A TREE DECLARING A BUILTIN THE PREVIOUS RELEASE DOES NOT KNOW CANNOT BE BUILT BY ITS OWN SEED**,
# and the seed is the one compiler nothing here can regenerate — every CI lane and every release runner
# starts with this script, so a refusal is four red lanes that no later commit can fix.
# `scripts/seed-shim/` holds the patches that withdraw such a declaration; they are staged only after a
# plain build has been tried and refused, so the fallback falls silent by itself once a published
# release carries the entry.
#
# ⛔ **THE SHIM IS WITHDRAWN BEFORE `C1` BUILDS `C2`, AND THAT IS WHAT MAKES IT SOUND.** A patch
# withdraws a DECLARATION the seed refuses, or a CALL SITE in compiler source the seed refuses the tree
# at — never the tables that teach `C1` the entry, and kept to one function so the shimmed build states
# what it loses. `C1` therefore knows the entry and compiles the unshimmed tree. Left in place for the
# second build it would ship a compiler built from the stubbed source.
# `scripts/seed-shim/README.md` owns the rule.
#
# Usage:
#   scripts/build-from-seed.sh

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh

seed="$(maxon_downloaded_path)"
built="$(maxon_compiler_path)"
shim_dir="scripts/seed-shim"

[ -x "$seed" ] || { echo "build-from-seed.sh: no seed at $seed — run scripts/fetch-seed.sh" >&2; exit 1; }

shim_staged=""

withdraw_shim() {
	[ -n "$shim_staged" ] || return 0

	local staged="$shim_staged"
	local file
	shim_staged=""

	while IFS= read -r file; do
		[ -n "$file" ] || continue
		git checkout -- "$file"
	done < <(printf '%s' "$staged")
}

trap withdraw_shim EXIT

stage_shim() {
	local patches
	local patch
	local file
	local touched=""

	patches=""
	if [ -d "$shim_dir" ]; then
		patches="$(find "$shim_dir" -name '*.patch' -type f | LC_ALL=C sort)"
	fi

	if [ -z "$patches" ]; then
		echo "build-from-seed.sh: the seed refused this tree and $shim_dir holds no patch withdrawing what it does not know" >&2
		exit 1
	fi

	while IFS= read -r patch; do
		[ -n "$patch" ] || continue

		while IFS= read -r file; do
			[ -n "$file" ] || continue

			if ! git diff --quiet -- "$file"; then
				echo "build-from-seed.sh: $file is modified — a seed shim is staged over a clean file and withdrawn by restoring it" >&2
				exit 1
			fi

			touched="$touched$file"$'\n'
		done < <(git apply --numstat -- "$patch" | cut -f3)
	done < <(printf '%s\n' "$patches")

	echo "build-from-seed.sh: the seed refused this tree — staging $shim_dir over the first build only" >&2

	while IFS= read -r patch; do
		[ -n "$patch" ] || continue
		git apply -- "$patch"
	done < <(printf '%s\n' "$patches")

	shim_staged="$touched"
}

if ! "$seed" build maxon-bin -o "$built"; then
	stage_shim
	"$seed" build maxon-bin -o "$built"
	withdraw_shim
fi

"$built" run build
"$built" version
