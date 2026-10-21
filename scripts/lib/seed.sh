# WHAT A BOOTSTRAP SEED IS CALLED AND WHERE ITS PIECES LIVE — written ONCE, for every script that
# freezes, proves, publishes, pins or fetches one, so no two of them disagree about a tag or an
# archive name.

SeedTagPrefix="seed"
SeedTargets="x64-windows x64-linux arm64-macos arm64-linux"
SeedPinFile="scripts/seed-tag"
SeedShimDirectory="scripts/seed-shim"
SeedCommitAbbreviation=12
SeedWorkDirectory="temp/seed"
SeedDistDirectory="$SeedWorkDirectory/dist"
SeedChecksumsAsset="SHA256SUMS"
SeedChecksumsFile="$SeedDistDirectory/$SeedChecksumsAsset"
SeedNotesFile="$SeedDistDirectory/NOTES.md"
GhReleaseNotFound="release not found"

# The commit in the tag keeps two freezes on one day apart, and is what `seed_tag_names_commit`
# checks a pin against.
seed_tag() {
	local date_stamp="$1" commit="$2"
	printf '%s-%s-%s' "$SeedTagPrefix" "$date_stamp" "${commit:0:$SeedCommitAbbreviation}"
}

seed_tag_names_commit() {
	local tag="$1" commit="$2"
	[[ "$tag" == "$SeedTagPrefix-"?*"-${commit:0:$SeedCommitAbbreviation}" ]]
}

seed_archive_stem() {
	local tag="$1" target="$2"
	printf 'maxon-%s-%s' "$tag" "$target"
}

seed_archive_file() {
	local tag="$1" target="$2" extension

	case "$target" in
		*-windows) extension="zip" ;;
		*-linux|*-macos) extension="tar.gz" ;;
		*) echo "seed.sh: $target is not a seed target" >&2; exit 1 ;;
	esac

	printf '%s.%s' "$(seed_archive_stem "$tag" "$target")" "$extension"
}

seed_shim_patches() {
	[ -d "$SeedShimDirectory" ] || return 0
	find "$SeedShimDirectory" -name '*.patch' -type f | LC_ALL=C sort
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
	local tag="$1" commit="$2" target file
	local -a assets=()

	for target in $SeedTargets; do
		assets+=("$SeedDistDirectory/$(seed_archive_file "$tag" "$target")")
	done

	assets+=("$SeedChecksumsFile")

	for file in "$SeedNotesFile" "${assets[@]}"; do
		[ -s "$file" ] || { echo "$(basename "$0"): $file is missing or empty — $tag cannot be published" >&2; exit 1; }
	done

	seed_release_create=(gh release create "$tag" --prerelease --target "$commit"
		--title "Maxon bootstrap seed ${tag#"$SeedTagPrefix-"}" --notes-file "$SeedNotesFile" "${assets[@]}")
}
