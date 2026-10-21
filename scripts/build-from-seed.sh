#!/usr/bin/env bash
#
# Build this tree's compiler from the pinned seed at `.bootstrap/maxon` (`scripts/seed-tag`, placed by
# `scripts/fetch-seed.sh`): the seed builds `C1`, and `C1` builds `C2` into the slot. Prints the result's
# `maxon version`.
#
# ⛔⛔ **ONE BUILD FROM THE SEED IS NOT THIS TREE'S COMPILER, IN THREE WAYS.**
#
#   * **ITS OWN RUNTIME IS THE SEED'S.** `C1` emits the new runtime into what it builds, but the
#     runtime inside `C1` was emitted by the seed. `spec-test`'s worker IS the compiler,
#     so a suite run by `C1` exercises the seed's scheduler, subprocess and console handling.
#   * **ITS FRONT END RUNS IN PROCESS.** A seed never binds `FrontEndPool.RuntimeMovesSharedRecords`, so `C1`
#     lexes and parses serially, and only a compile `C2` runs reports the pool under `--log=compiler:debug`.
#   * **IT REPORTS `dev`.** The seed builds `maxon-bin` as a PATH, so `maxon-bin/maxon.maxproj` —
#     where the version is derived from the ref and handed to `--define` — never runs.
#     MEASURED on the 0.1.1 rehearsal: the x64-windows archive came out as `maxon-dev-x64-windows`.
#
# ⛔ **THE SEED'S OUTPUT IS NAMED EXPLICITLY** for the same reason: a seed reading `maxon-bin` as a path
# writes the compiler to a default of its own choosing — MEASURED: `maxon-bin/Profile/ProfileSampler`
# — and exits 0. `--output=` is correct under both readings. `C1` builds by NAME, so it runs the manifest.
#
# ⛔ **A TREE DECLARING WHAT THE PINNED SEED DOES NOT KNOW CANNOT BE BUILT BY IT**, and the seed is the one
# compiler nothing here can regenerate — every CI lane and every release runner starts with this script,
# so a refusal is four red lanes that no later commit can fix. The cure is a new seed.
# `scripts/seed-shim/` holds the patches that withdraw such a declaration until one is pinned; they
# are staged only after a plain build has been tried and refused. A patch there is what makes CI
# freeze and pin the next seed, and the pin deletes every patch (`docs/RELEASING.md`).
#
# ⛔ **THE SHIM IS WITHDRAWN BEFORE `C1` BUILDS `C2`, AND THAT IS WHAT MAKES IT SOUND.** A patch
# withdraws a DECLARATION the seed refuses, a CALL SITE in compiler source the seed refuses the tree at,
# a VISIBILITY MODIFIER an older audit reads as an unused export, a LANGUAGE FORM the seed cannot parse
# (with the rewrites its older rules then demand), a LIBRARY BODY naming what the seed has never heard of,
# or a CONSTRUCT the seed miscompiles — never the tables that teach `C1` the entry. `C1` therefore knows the entry and compiles the unshimmed
# tree. Left in place for the second build it would ship a compiler built from the stubbed source.
# `scripts/seed-shim/README.md` owns the rule.
#
# Usage:
#   scripts/build-from-seed.sh
#   scripts/build-from-seed.sh --unshimmed       # a refusal fails the build: proves a seed
#                                                # accepts the tree with no patch, before the
#                                                # pin deletes them

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh
. scripts/lib/seed.sh

unshimmed=0

for argument in "$@"; do
	case "$argument" in
		--unshimmed) unshimmed=1 ;;
		*) echo "build-from-seed.sh: unknown argument: $argument" >&2; exit 2 ;;
	esac
done

seed="$(maxon_downloaded_path)"
built="$(maxon_compiler_path)"

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

	patches="$(seed_shim_patches)"

	if [ -z "$patches" ]; then
		echo "build-from-seed.sh: the seed refused this tree and $SeedShimDirectory holds no patch withdrawing what it does not know — add one and CI freezes a new seed from main, or freeze one by hand with scripts/freeze-seed.sh" >&2
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

	echo "build-from-seed.sh: the seed refused this tree — staging $SeedShimDirectory over the first build only" >&2

	while IFS= read -r patch; do
		[ -n "$patch" ] || continue
		git apply -- "$patch"
	done < <(printf '%s\n' "$patches")

	shim_staged="$touched"
}

if ! "$seed" build maxon-bin --output="$built"; then
	[ "$unshimmed" = 0 ] || { echo "build-from-seed.sh: the seed refused this tree and --unshimmed forbids staging $SeedShimDirectory" >&2; exit 1; }
	stage_shim
	"$seed" build maxon-bin --output="$built"
	withdraw_shim
fi

# `C1` was compiled from this tree, so the `C2` it writes already carries the tree's runtime; `C1`
# stamps no commit, so its manifest would otherwise ask for a third build.
MAXON_SECOND_STAGE=1 "$built" run build
"$built" version
