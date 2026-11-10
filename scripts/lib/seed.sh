# WHAT A BOOTSTRAP SEED IS CALLED AND WHERE ITS PIECES LIVE — written ONCE, for every script that
# freezes, proves, publishes, pins or fetches one, so no two of them disagree about a tag or a path.

# Sourced here, not left to the caller: `mac-host.sh` sources this file alone.
. scripts/lib/host-binaries.sh
. scripts/lib/checksums.sh

SeedTagPrefix="seed"
SeedPinFile="scripts/seed-tag"
SeedCommitAbbreviation=12
SeedDateFormat="%Y-%m-%d"
SeedDatePattern="[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]"
SeedWorkDirectory="temp/seed"
SeedDistDirectory="$SeedWorkDirectory/dist"
SeedChecksumsFile="$SeedDistDirectory/$ChecksumsAsset"
SeedNotesFile="$SeedDistDirectory/NOTES.md"
GhReleaseNotFound="release not found"

# The commit in the tag keeps two freezes on one day apart.
seed_tag() {
	local date_stamp="$1" commit="$2"
	printf '%s-%s-%s' "$SeedTagPrefix" "$date_stamp" "${commit:0:$SeedCommitAbbreviation}"
}

# ⚠ Only "release not found" answers no. Any other failure of `gh` — no network, no authentication —
# says nothing about whether the release exists, so it stops the script.
seed_release_published() {
	local tag="$1" lookup

	lookup="$(gh release view "$tag" --json tagName 2>&1)" && return 0
	[[ "$lookup" != *"$GhReleaseNotFound"* ]] || return 1

	echo "$(basename "$0"): cannot read the release $tag: $lookup" >&2
	exit 1
}

seed_release_create_command() {
	local tag="$1" target archive file
	local -a assets=()

	for target in $MaxonTargets; do
		archive="$(maxon_archive_file "$tag" "$target")"
		assets+=("$SeedDistDirectory/$archive")
	done

	assets+=("$SeedChecksumsFile")

	for file in "$SeedNotesFile" "${assets[@]}"; do
		[ -s "$file" ] || { echo "$(basename "$0"): $file is missing or empty — $tag cannot be published" >&2; exit 1; }
	done

	seed_release_create=(gh release create "$tag" --prerelease --verify-tag
		--title "Maxon bootstrap seed ${tag#"$SeedTagPrefix-"}" --notes-file "$SeedNotesFile" "${assets[@]}")
}
