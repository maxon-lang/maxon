# WHICH DIRECTORIES UNDER `scripts/` HOLD A PROGRAM THAT A SCRIPT BUILDS OR LINTS FOR ITSELF — written
# ONCE, so the set that is built and the set that is linted cannot drift apart.
#
# Bash, for the process substitution. Source it from the repository root and call `script_program_dirs`:
# it prints the name of each tracked directory directly under `scripts/` that holds a `.maxon` file, one
# per line and sorted, without the `scripts/` prefix. A directory holding a `compile-all.sh` builds its own
# programs and is left out.

script_program_dirs() {
	local tracked_scripts program_dirs self_building_dirs

	tracked_scripts="$(git ls-files -- scripts)"
	program_dirs="$(printf '%s\n' "$tracked_scripts" | sed -n 's|^scripts/\([^/]*\)/.*\.maxon$|\1|p' | sort -u)"
	self_building_dirs="$(printf '%s\n' "$tracked_scripts" | sed -n 's|^scripts/\([^/]*\)/compile-all\.sh$|\1|p' | sort -u)"

	comm -23 <(printf '%s\n' "$program_dirs") <(printf '%s\n' "$self_building_dirs")
}
