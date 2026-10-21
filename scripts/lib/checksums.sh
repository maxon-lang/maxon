# WHAT THE CHECKSUM FILE IS CALLED, HOW A FILE IS HASHED AND HOW THE LIST IS WRITTEN — written ONCE,
# for every script that writes or checks one. POSIX sh for the reason `host-binaries.sh` gives.

ChecksumsAsset="SHA256SUMS"

# A stock macOS host has `shasum` but may lack `sha256sum`.
sha256_of() (
	if command -v sha256sum >/dev/null 2>&1; then
		sha256_line="$(sha256sum "$1")" || exit 1
	else
		sha256_line="$(shasum -a 256 "$1")" || exit 1
	fi

	printf '%s' "${sha256_line%% *}"
)

# `<hex>  <name>` per line is the one format every reader accepts: `fetch-seed.sh`, both installers,
# `installer/docker/Dockerfile` and `homebrew.yml`'s `sha256sum -c`. None of them can source this
# file.
write_checksums() (
	cd "$1" || exit 1
	shift

	[ "$#" -gt 0 ] || { echo "checksums.sh: no files to checksum" >&2; exit 1; }

	printf '%s\n' "$@" | LC_ALL=C sort | while IFS= read -r name; do
		file_hash="$(sha256_of "$name")" || exit 1
		printf '%s  %s\n' "$file_hash" "$name"
	done > "$ChecksumsAsset" || exit 1
)
