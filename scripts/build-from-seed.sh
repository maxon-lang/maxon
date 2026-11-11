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
#   * **ITS FRONT END RUNS IN PROCESS.** A seed never binds `FrontEndPool.RuntimeSharesAnyGraph`, so `C1`
#     lexes and parses serially, and only a compile `C2` runs reports the pool under `--log=compiler:debug`.
#     That declaration is deleted once a release carries a runtime that moves any graph.
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
# so a refusal is four red lanes that no later commit can fix. The cure is a new seed frozen from the refused
# commit, which lands with it through `scripts/pin-seed.sh`.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh

seed="$(maxon_downloaded_path)"
built="$(maxon_compiler_path)"

[ -x "$seed" ] || { echo "build-from-seed.sh: no seed at $seed — run scripts/fetch-seed.sh" >&2; exit 1; }

"$seed" build maxon-bin --output="$built"

# `C1` was compiled from this tree, so the `C2` it writes already carries the tree's runtime; `C1`
# stamps no commit, so its manifest would otherwise ask for a third build.
MAXON_SECOND_STAGE=1 "$built" run build
"$built" version
