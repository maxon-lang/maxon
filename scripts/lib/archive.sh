# HOW THIS PROJECT MAKES AND UNPACKS AN ARCHIVE — written ONCE, for every script that makes or reads
# one.
#
# `make_tar` and `make_zip` take the archive to write and a staged directory whose basename becomes the
# archive's single top-level entry, and `make_tar` requires that directory to hold the executable as
# `maxon`. Sourced by `scripts/release.sh` (the release archives) and `scripts/freeze-seed.sh` (the
# seed archives), so the two cannot disagree about the executable bit or the zip separators, and by
# `scripts/fetch-seed.sh` and `scripts/fetch-vendor.sh`, which unpack.

. scripts/lib/host-binaries.sh

# ⛔ THE EXECUTABLE BIT HAS TO BE PUT INTO THE ARCHIVE, NOT ASSUMED FROM THE FILE. A tar records the
# mode it reads off disk, and on Windows there is no POSIX mode to read — `chmod +x` in Git Bash
# changes nothing a native `tar` can see, so every Linux and macOS archive built here shipped `maxon`
# as 0644. MEASURED by extracting one under WSL: `bash: ./maxon: Permission denied`, on an archive
# whose own INSTALL.md blamed the extraction tool.
#
# So the binary is added in its own pass with an explicit mode, and the rest of the tree follows in a
# second pass that excludes it. `--mode` applies to everything in one invocation, which is why this is
# two.
#
# ⚠ `--mode` IS GNU TAR'S FLAG AND macOS SHIPS BSDTAR, WHICH REFUSES IT OUTRIGHT — so the override is
# asked for only where it is both supported and needed. It is needed exactly where the FILESYSTEM
# cannot carry a mode: on a POSIX host the `chmod +x` on the staged binary is real and a plain tar records
# it. The capability is PROBED rather than inferred from the platform, because "which tar is on PATH"
# is not a property of the OS.
tar_supports_mode() {
	local probe rc
	probe="$(mktemp -d)"
	: > "$probe/f"
	if tar -cf /dev/null -C "$probe" --mode=0755 f >/dev/null 2>&1; then
		rc=0
	else
		rc=1
	fi
	rm -rf "$probe"
	return "$rc"
}

# ⛔ VERIFY, NEVER ASSUME — THIS BIT HAS SHIPPED WRONG TWICE. An archive whose `maxon` is 0644 looks
# perfectly normal until someone runs it and gets "Permission denied", and neither the build nor the
# upload notices. The listing is the only place the shipped mode can be read back.
require_executable_in_tar() {
	local archive="$1"
	local line
	line="$(tar -tvzf "$archive" | grep -E '/maxon$' | head -n1 || true)"
	[ -n "$line" ] || { echo "archive.sh: $archive contains no maxon binary" >&2; return 1; }
	case "$line" in
		-rwx*) return 0 ;;
		*) echo "archive.sh: $archive ships maxon as NOT EXECUTABLE — $line" >&2; return 1 ;;
	esac
}

make_tar() {
	local archive="$1" stage="$2"
	local dir base plain
	dir="$(dirname "$stage")"
	base="$(basename "$stage")"
	plain="${archive%.gz}"

	rm -f "$plain"
	if tar_supports_mode; then
		tar -cf "$plain" -C "$dir" --mode=0755 "$base/maxon"
		tar -rf "$plain" -C "$dir" --exclude="$base/maxon" "$base"
	else
		tar -cf "$plain" -C "$dir" "$base"
	fi
	gzip -f "$plain"
	require_executable_in_tar "$archive"
}

# ⚠ `zip` IS NOT A WINDOWS TOOL AND GIT BASH DOES NOT SHIP ONE, so this tries three writers in order of
# how well they behave rather than how convenient they are.
#
# ⛔ POWERSHELL'S `Compress-Archive` IS LAST, AND ONLY WITH A WARNING, BECAUSE IT WRITES BACKSLASHES AS
# PATH SEPARATORS. A zip is specified to use `/`; one built with `\` extracts as files with literal
# backslashes in their names on Linux and macOS, and `unzip` says so —
# "appears to use backslashes as path separators" — MEASURED on the first Windows archive this script
# produced. PowerShell reads its own output back happily, which is exactly what makes the defect ship:
# it looks correct on the machine that built it and is broken everywhere else.
#
# Windows has shipped bsdtar as `tar.exe` since Windows 10, and it writes a conforming zip, so the
# fallback order is `zip`, then bsdtar, then a warning.
make_zip() {
	local archive="$1" stage="$2"
	local dir base target
	dir="$(dirname "$stage")"
	base="$(basename "$stage")"
	target="$(cd "$(dirname "$archive")" && pwd)/$(basename "$archive")"

	if command -v zip >/dev/null 2>&1; then
		( cd "$dir" && zip -qr "$target" "$base" )
		return
	fi

	if [ -x /c/Windows/System32/tar.exe ]; then
		( cd "$dir" && /c/Windows/System32/tar.exe -a -c -f "$target" "$base" )
		return
	fi

	if command -v powershell >/dev/null 2>&1; then
		echo "archive.sh: WARNING — falling back to Compress-Archive, which writes backslash separators" >&2
		powershell -NoProfile -Command "Compress-Archive -Path '$(cygpath -w "$stage")' -DestinationPath '$(cygpath -w "$archive")' -Force" 			|| { echo "archive.sh: Compress-Archive failed for $archive" >&2; return 1; }
		return
	fi

	echo "archive.sh: no way to make a .zip here — install zip, or run this on Windows" >&2
	return 1
}

make_archive() {
	local archive="$1" stage="$2"

	# `zip -qr` onto an existing archive adds to it, keeping entries the stage no longer holds.
	rm -f "$archive"

	case "$archive" in
		*.zip)    make_zip "$archive" "$stage" ;;
		*.tar.gz) make_tar "$archive" "$stage" ;;
		*) echo "archive.sh: no packer writes $archive" >&2; return 1 ;;
	esac
}

unpack_archive() {
	local archive="$1" into="$2"

	mkdir -p "$into"

	case "$archive" in
		*.zip)    unzip -q -o "$archive" -d "$into" ;;
		*.tar.xz) tar -xJf "$archive" -C "$into" ;;
		*.tar.gz) tar -xzf "$archive" -C "$into" ;;
		*) echo "archive.sh: no unpacker reads $archive" >&2; return 1 ;;
	esac
}

archives_in() {
	local directory="$1" extensions extension
	local -a name_tests=()

	extensions="$(maxon_archive_extensions)" || return 1

	for extension in $extensions; do
		[ "${#name_tests[@]}" -eq 0 ] || name_tests+=(-o)
		name_tests+=(-name "*.$extension")
	done

	find "$directory" -maxdepth 1 -type f \( "${name_tests[@]}" \) | LC_ALL=C sort
}
