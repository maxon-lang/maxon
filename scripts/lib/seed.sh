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
SeedRepoApi="repos/{owner}/{repo}"
GhUploadHost="https://uploads.github.com"
GhNotFound="(HTTP 404)"

# The commit in the tag keeps two freezes on one day apart.
seed_tag() {
	local date_stamp="$1" commit="$2"
	printf '%s-%s-%s' "$SeedTagPrefix" "$date_stamp" "${commit:0:$SeedCommitAbbreviation}"
}

# ⚠ Only a 404 answers no. Any other failure of `gh` — no network, no authentication — says nothing about
# whether the release exists, so it stops the script. Every call here is REST, because `gh release` needs
# GitHub GraphQL, which some sessions cannot reach. Sets SeedReleaseId.
seed_release_published() {
	local tag="$1" lookup

	lookup="$(gh api "$SeedRepoApi/releases/tags/$tag" --jq .id 2>&1)" && { SeedReleaseId="$lookup"; return 0; }
	[[ "$lookup" != *"$GhNotFound"* ]] || return 1

	echo "$(basename "$0"): cannot read the release $tag: $lookup" >&2
	exit 1
}

seed_release_require_assets() {
	local tag="$1" target archive file

	seed_release_assets=()

	for target in $MaxonTargets; do
		archive="$(maxon_archive_file "$tag" "$target")"
		seed_release_assets+=("$SeedDistDirectory/$archive")
	done

	seed_release_assets+=("$SeedChecksumsFile")

	for file in "$SeedNotesFile" "${seed_release_assets[@]}"; do
		[ -s "$file" ] || { echo "$(basename "$0"): $file is missing or empty — $tag cannot be published" >&2; exit 1; }
	done
}

seed_release_create() {
	local tag="$1" repository file

	gh api "$SeedRepoApi/git/ref/tags/$tag" --silent \
		|| { echo "$(basename "$0"): the tag $tag is not on the remote — $tag cannot be published" >&2; return 1; }
	repository="$(gh api "$SeedRepoApi" --jq .full_name)" || return 1
	SeedReleaseId="$(gh api "$SeedRepoApi/releases" -f tag_name="$tag" -F prerelease=true \
		-f name="Maxon bootstrap seed ${tag#"$SeedTagPrefix-"}" -F body=@"$SeedNotesFile" --jq .id)" || return 1

	for file in "${seed_release_assets[@]}"; do
		gh api --method POST -H "Content-Type: application/octet-stream" --input "$file" --silent \
			"$GhUploadHost/repos/$repository/releases/$SeedReleaseId/assets?name=$(basename "$file")" || return 1
	done
}

seed_release_delete() {
	local tag="$1"

	gh api --method DELETE "$SeedRepoApi/releases/$SeedReleaseId" --silent || return 1
	gh api --method DELETE "$SeedRepoApi/git/refs/tags/$tag" --silent
}

seed_release_download() {
	local target="$1" directory="$2" listing asset_id name

	listing="$(gh api "$SeedRepoApi/releases/$SeedReleaseId" --jq '.assets[] | "\(.id)\t\(.name)"')" || return 1

	while IFS=$'\t' read -r asset_id name; do
		case "$name" in
			*"$target"* | "$ChecksumsAsset")
				gh api -H "Accept: application/octet-stream" "$SeedRepoApi/releases/assets/$asset_id" > "$directory/$name" || return 1
				;;
		esac
	done < <(printf '%s\n' "$listing")
}
