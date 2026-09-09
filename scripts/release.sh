#!/usr/bin/env bash
#
# Package one target for release, or publish the packages once every target has one.
#
# ⭐⭐ **TWO HALVES BECAUSE THEY RUN IN DIFFERENT PLACES.** `--package` runs on a machine of the
# target's own architecture — it BUILDS and then RUNS the whole suite, which is the only way a claim
# about a platform is worth anything — and `--publish` runs once, anywhere, over the archives the
# first half left. A single script doing both could only ever have tested the host it ran on.
#
# Usage:
#   scripts/release.sh --package [--target=<arch>-<os>] [--skip-tests]
#   scripts/release.sh --publish <version> [--dry-run]
#
#   --package        stage and archive this host's target into dist/
#   --target=        cross-compile another target instead. ⚠ Its suite CANNOT be run here; the
#                    archive is marked untested and `--publish` says so in the release notes.
#   --skip-tests     package without running the suite. For iterating on the packaging itself.
#   --publish        create the GitHub release from everything in dist/
#   --dry-run        with --publish: assemble and verify, print what would be uploaded, upload nothing
#
# ⛔ ON macOS, BUILD THE BINARY AS `maxon` AND NEVER RENAME IT AFTERWARDS. `MachOWriter` bakes the
# OUTPUT FILENAME into the ad-hoc code-signature identifier, so a post-build rename leaves a signature
# naming a file that no longer exists. Measured: two otherwise byte-identical self-compiles differ in
# exactly one byte when written as `stage2` and `stage3`.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh

DIST="dist"
mode=""
target=""
version=""
dry_run=0
skip_tests=0

for arg in "$@"; do
	case "$arg" in
		--package)   mode="package" ;;
		--publish)   mode="publish" ;;
		--target=*)  target="${arg#*=}" ;;
		--dry-run)   dry_run=1 ;;
		--skip-tests) skip_tests=1 ;;
		-h|--help)   sed -n '2,24p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
		v*)          version="$arg" ;;
		*) echo "release.sh: unknown argument: $arg" >&2; exit 2 ;;
	esac
done

[ -n "$mode" ] || { echo "release.sh: pass --package or --publish (see --help)" >&2; exit 2; }

maxon="$(maxon_compiler_path .)"
[ -x "$maxon" ] || { echo "release.sh: no compiler at $maxon — build one first (see CONTRIBUTING.md)" >&2; exit 1; }

# ⛔ THE VERSION IS READ OFF THE BINARY, NOT OFF THE REF. `build.maxon` derives it from git at build
# time — a `vX.Y.Z` tag or a `release/X.Y.Z` branch gives the number, anything else is `dev` — so what
# ships is whatever the BINARY says, and a binary built before the branch was cut still says `dev`.
# Asking the artifact removes the one disagreement this whole scheme exists to prevent.
compiler_version() { "$maxon" version | awk '{print $2}'; }
version_from_binary="$(compiler_version)"

package_one() {
	local tgt="$1" native="$2"
	local stage="$DIST/maxon-$version_from_binary-$tgt"

	echo "release.sh: packaging $tgt (version $version_from_binary)"
	rm -rf "$stage"
	mkdir -p "$stage"

	# ⛔ THE BINARY IS BUILT UNDER ITS FINAL NAME. See the Mach-O signature note in the header.
	local exe_ext=""
	case "$tgt" in *-windows) exe_ext=".exe" ;; esac

	if [ "$native" -eq 1 ]; then
		cp "$maxon" "$stage/maxon$exe_ext"
	else
		"$maxon" build maxon-bin --target="$tgt" -o "$stage/maxon$exe_ext" >/dev/null \
			|| { echo "release.sh: cross-build failed for $tgt" >&2; return 1; }
	fi
	chmod +x "$stage/maxon$exe_ext"

	# ⛔ THE DEBUG SIDECAR IS NOT A RELEASE ASSET, AND ONLY THE CROSS PATH EVER CREATES ONE. A native
	# package COPIES an already-built compiler, so nothing lands beside it; a cross-build compiles INTO
	# the stage and `maxon.mxdbg` — 7.8 MB, more than the compiler compresses to — arrives with it. That
	# asymmetry is why this looked done: the one target packaged natively here was clean.
	#
	# The sweep is over the stage rather than over the one name, so a second sidecar cannot reappear by
	# being spelled differently.
	find "$stage" -maxdepth 1 -type f -name '*.mxdbg' -delete

	# ⚠ `stdlib/` SHIPS AS SOURCE, WITHOUT ITS BUILD CACHE. The compiler reads the stdlib from source
	# and finds it by walking up from its own executable, so the layout here IS the contract: `maxon`
	# and `stdlib/` as siblings. `.maxon/` is this machine's cache and means nothing anywhere else.
	cp -r stdlib "$stage/stdlib"
	rm -rf "$stage/stdlib/.maxon"
	cp -r examples "$stage/examples" 2>/dev/null || true
	rm -rf "$stage/examples/.maxon"
	cp LICENSE-MIT LICENSE-APACHE README.md "$stage/"
	write_install_md "$stage/INSTALL.md" "$tgt" "$exe_ext"

	local archive
	case "$tgt" in
		*-windows)
			archive="$DIST/maxon-$version_from_binary-$tgt.zip"
			rm -f "$archive"
			make_zip "$archive" "$stage" ;;
		*)
			archive="$DIST/maxon-$version_from_binary-$tgt.tar.gz"
			rm -f "$archive"
			make_tar "$archive" "$stage" ;;
	esac

	rm -rf "$stage"
	echo "release.sh: wrote $archive"
	[ "$native" -eq 1 ] || echo "release.sh: ⚠ $tgt was CROSS-BUILT and its suite was not run here" >&2

	# ⚠ THE INSTALLER IS BUILT FROM THE ARCHIVE, and only when WiX is present. A release machine has it;
	# someone packaging locally may not, and should not be stopped by that — the zip is a release asset
	# either way and the MSI is an additional one.
	if [ "$tgt" = "x64-windows" ]; then
		if command -v wix >/dev/null 2>&1; then
			installer/windows/build.sh "$archive"
		else
			echo "release.sh: no WiX on PATH, so no MSI was built (dotnet tool install --global wix --version 5.*)" >&2
		fi
	fi
}

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
# cannot carry a mode: on a POSIX host the `chmod +x` in `package_one` is real and a plain tar records
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
	[ -n "$line" ] || { echo "release.sh: $archive contains no maxon binary" >&2; return 1; }
	case "$line" in
		-rwx*) return 0 ;;
		*) echo "release.sh: $archive ships maxon as NOT EXECUTABLE — $line" >&2; return 1 ;;
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
	local dir base
	dir="$(dirname "$stage")"
	base="$(basename "$stage")"

	if command -v zip >/dev/null 2>&1; then
		( cd "$dir" && zip -qr "$(basename "$archive")" "$base" )
		return
	fi

	if [ -x /c/Windows/System32/tar.exe ]; then
		( cd "$dir" && /c/Windows/System32/tar.exe -a -c -f "$(basename "$archive")" "$base" )
		return
	fi

	if command -v powershell >/dev/null 2>&1; then
		echo "release.sh: WARNING — falling back to Compress-Archive, which writes backslash separators" >&2
		powershell -NoProfile -Command "Compress-Archive -Path '$(cygpath -w "$stage")' -DestinationPath '$(cygpath -w "$archive")' -Force" 			|| { echo "release.sh: Compress-Archive failed for $archive" >&2; return 1; }
		return
	fi

	echo "release.sh: no way to make a .zip here — install zip, or run this on Windows" >&2
	return 1
}

write_install_md() {
	local out="$1" tgt="$2" exe_ext="$3"
	cat > "$out" <<EOF
# Installing Maxon $version_from_binary ($tgt)

This archive holds the \`maxon\` compiler and the standard library it reads.

## Layout, and why it matters

    maxon$exe_ext     the compiler
    stdlib/           the standard library, as source

⛔ **Keep these two together.** The compiler finds \`stdlib/\` by walking UP from its own executable,
so moving \`maxon$exe_ext\` somewhere else on its own leaves it with no standard library. Move the
whole directory, or put it on your PATH as it is.

## Install

Extract the archive somewhere permanent and add that directory to your PATH. Then:

    maxon version
    maxon build examples/basic.maxon -o hello
    ./hello

EOF

	case "$tgt" in
		arm64-macos)
			cat >> "$out" <<'EOF'
## macOS: the quarantine flag

A downloaded archive is quarantined by Gatekeeper whatever is inside it, so the first run is refused.
Clear it once:

    xattr -d com.apple.quarantine ./maxon

The binary carries an ad-hoc code signature already; the quarantine attribute is about where the file
came from, not about the signature.
EOF
			;;
		*-linux)
			cat >> "$out" <<'EOF'
## Linux: the executable bit

Some extraction tools drop it. If `./maxon` reports "permission denied":

    chmod +x ./maxon

The binary is statically linked and makes raw syscalls, so there is nothing else to install.
EOF
			;;
		*-windows)
			cat >> "$out" <<'EOF'
## Windows: SmartScreen

This build is not code-signed, so SmartScreen will warn the first time you run it. Choose
**More info** then **Run anyway**. Signing is planned; until then the checksum published beside this
archive is what tells you the file is the one that was built.

⚠ Do not build your own projects from INSIDE the install directory. The compiler takes a lock at the
root of the tree it is compiling, and a directory you do not own is one it cannot write to.
EOF
			;;
	esac
}

# Release notes: how to install, and what each asset is for.
#
# ⚠ IT NAMES ONLY THE ASSETS THAT EXIST. A release built without WiX has no MSI, and a notes template
# that mentions one regardless sends people to a download that is not there.
write_default_notes() {
	local out="$1"

	{
		echo "## Install"
		echo
		if ls "$DIST"/*.msi >/dev/null 2>&1; then
			echo "**Windows**"
			echo
			echo '```'
			echo "winget install MaxonLang.Maxon"
			echo '```'
			echo
			echo "Or download the \`.msi\` below. It installs to \`C:\\Program Files\\Maxon\` and adds it to PATH."
			echo "The installer is not code-signed, so SmartScreen warns on first run: **More info** then **Run anyway**."
			echo
			echo
		fi
		if ls "$DIST"/*arm64-macos.tar.gz >/dev/null 2>&1; then
			echo "**macOS**"
			echo
			echo '```'
			echo "brew install maxon-lang/tap/maxon"
			echo '```'
			echo
			echo "Homebrew puts \`maxon\` on your PATH immediately and clears the quarantine attribute."
			echo "If you download the archive instead, run \`xattr -d com.apple.quarantine ./maxon\` once."
			echo
		fi
		if ls "$DIST"/*-linux.tar.gz >/dev/null 2>&1; then
			echo "**Linux**"
			echo
			echo "Extract the archive for your architecture and put that directory on your PATH."
			echo "The binary is statically linked and makes raw syscalls, so there is nothing else to install."
			echo
		fi

		echo "⚠ **Keep \`maxon\` and \`stdlib/\` together.** The compiler finds its standard library by walking"
		echo "up from its own executable, so moving the binary out on its own leaves it without one."
		echo
		echo "## Verify a download"
		echo
		echo "\`SHA256SUMS\` is published beside the archives. These builds are not signed, so the checksum is"
		echo "what tells you a file is the one that was built."
		echo
		echo "## Assets"
		echo
		local f
		for f in $(find "$DIST" -maxdepth 1 -type f \( -name '*.zip' -o -name '*.tar.gz' -o -name '*.msi' \) -printf '%f\n' | sort); do
			echo "- \`$f\`"
		done
	} > "$out"
}

if [ "$mode" = "package" ]; then
	mkdir -p "$DIST"
	host="$(maxon_host_target)"

	if [ -z "$target" ] || [ "$target" = "$host" ]; then
		if [ "$skip_tests" -eq 0 ]; then
			# ⭐ THE SUITE RUNS BEFORE THE ARCHIVE EXISTS, on the architecture the archive is FOR. A
			# packaged compiler nobody ran is a claim, and this is the one step that can make it a fact.
			echo "release.sh: running the suite on $host before packaging"
			"$maxon" spec-test > "$DIST/spec-test-$host.log" 2>&1 \
				|| { echo "release.sh: the suite FAILED on $host — see $DIST/spec-test-$host.log; nothing packaged" >&2; exit 1; }
			grep -E "passed, .* failed" "$DIST/spec-test-$host.log" | tail -1
		fi
		package_one "$host" 1
	else
		package_one "$target" 0
	fi
	exit 0
fi

# ── publish ───────────────────────────────────────────────────────────────────────────────────────

[ -n "$version" ] || { echo "release.sh: --publish needs a version, e.g. v0.1.0" >&2; exit 2; }

# ⛔ THE TAG AND THE BINARY MUST AGREE, AND THIS IS THE ONLY PLACE THAT CAN CHECK IT. Everything
# downstream — the MSI, the winget manifest, the install page — derives from one of the two, so a
# disagreement here becomes a release whose parts name different versions.
if [ "$version" != "v$version_from_binary" ]; then
	echo "release.sh: the tag says $version and the compiler says $version_from_binary." >&2
	echo "  Build from a release/X.Y.Z branch or a vX.Y.Z tag and rebuild, or tag the version that was built." >&2
	exit 1
fi

archives="$(find "$DIST" -maxdepth 1 -type f \( -name '*.zip' -o -name '*.tar.gz' -o -name '*.msi' \) | sort)"
[ -n "$archives" ] || { echo "release.sh: no archives in $DIST — run --package first" >&2; exit 1; }

( cd "$DIST" && sha256sum $(find . -maxdepth 1 -type f \( -name '*.zip' -o -name '*.tar.gz' -o -name '*.msi' \) -printf '%f\n' | sort) > SHA256SUMS )
echo "release.sh: checksums"
sed 's/^/  /' "$DIST/SHA256SUMS"

# ⭐ WRITTEN BEFORE THE DRY-RUN EXIT, so `--dry-run` exercises the notes too. A preview that skips the
# one artifact a person actually reads is not a preview.
# ⭐ THE NOTES ARE WRITTEN HERE IF NOBODY WROTE THEM. A release with no notes is a page of filenames,
# and the one thing a reader wants — which file is theirs, and what to do with it — is exactly what the
# filenames do not say. A `dist/NOTES.md` placed by hand wins: this is a floor, not a template to
# fight.
if [ ! -f "$DIST/NOTES.md" ]; then
	echo "release.sh: writing default notes to $DIST/NOTES.md"
	write_default_notes "$DIST/NOTES.md"
fi

if [ "$dry_run" -eq 1 ]; then
	echo "release.sh: --dry-run; would create $version with the assets above"
	exit 0
fi

command -v gh >/dev/null 2>&1 || { echo "release.sh: publishing needs the GitHub CLI (gh)" >&2; exit 1; }

gh release create "$version" --title "Maxon $version_from_binary" --notes-file "$DIST/NOTES.md" \
	$archives "$DIST/SHA256SUMS"
echo "release.sh: published $version"
