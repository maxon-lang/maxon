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
#   * **IT REPORTS `dev`.** A seed older than named manifest targets reads `maxon-bin` as a PATH, so
#     `build.maxon` — where the version is derived from the ref and handed to `--define` — never runs.
#     MEASURED on the 0.1.1 rehearsal: the x64-windows archive came out as `maxon-dev-x64-windows`.
#
# ⛔ **THE SEED'S OUTPUT IS NAMED EXPLICITLY** for the same reason: a seed reading `maxon-bin` as a path
# writes the compiler to a default of its own choosing — MEASURED: `maxon-bin/Profile/ProfileSampler`
# — and exits 0. `-o` is correct under both readings. `C1` builds by NAME, so it runs the manifest.
#
# Usage:
#   scripts/build-from-seed.sh

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh

seed="$(maxon_downloaded_path)"
built="$(maxon_compiler_path)"

[ -x "$seed" ] || { echo "build-from-seed.sh: no seed at $seed — put a released maxon binary there (the binary alone)" >&2; exit 1; }

"$seed" build maxon-bin -o maxon-bin/.maxon/maxon
"$built" build maxon-bin
"$built" version
