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
[ -x "$maxon" ] || { echo "release.sh: no compiler at $maxon — run scripts/build.sh" >&2; exit 1; }

# ⛔ THE VERSION IS READ OFF THE BINARY, NOT OFF THE SOURCE. `Compiler/Version.maxon` is where it is
# written, but what ships is whatever the BINARY says — and a binary built before a version bump
# reports the old one. Asking the artifact removes the one disagreement this whole scheme exists to
# prevent.
compiler_version() { "$maxon" --version | awk '{print $2}'; }
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
			tar -czf "$archive" -C "$DIST" "$(basename "$stage")" ;;
	esac

	rm -rf "$stage"
	echo "release.sh: wrote $archive"
	[ "$native" -eq 1 ] || echo "release.sh: ⚠ $tgt was CROSS-BUILT and its suite was not run here" >&2
}

# ⚠ `zip` IS NOT A WINDOWS TOOL AND GIT BASH DOES NOT SHIP ONE. PowerShell's `Compress-Archive` is on
# every Windows since 5.1 and needs no install, so it is the fallback — and it takes WINDOWS paths,
# which is what `cygpath -w` is for. `zip` is still preferred where it exists, because a Linux or macOS
# host packaging the Windows target has it and PowerShell is the thing it lacks.
make_zip() {
	local archive="$1" stage="$2"
	if command -v zip >/dev/null 2>&1; then
		( cd "$(dirname "$stage")" && zip -qr "$(basename "$archive")" "$(basename "$stage")" )
		return
	fi
	if command -v powershell >/dev/null 2>&1; then
		powershell -NoProfile -Command 			"Compress-Archive -Path '$(cygpath -w "$stage")' -DestinationPath '$(cygpath -w "$archive")' -Force" 			|| { echo "release.sh: Compress-Archive failed for $archive" >&2; return 1; }
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

    maxon --version
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
	echo "  Update Compiler/Version.maxon and rebuild, or tag the version that was built." >&2
	exit 1
fi

archives="$(find "$DIST" -maxdepth 1 -type f \( -name '*.zip' -o -name '*.tar.gz' \) | sort)"
[ -n "$archives" ] || { echo "release.sh: no archives in $DIST — run --package first" >&2; exit 1; }

( cd "$DIST" && sha256sum $(find . -maxdepth 1 -type f \( -name '*.zip' -o -name '*.tar.gz' \) -printf '%f\n' | sort) > SHA256SUMS )
echo "release.sh: checksums"
sed 's/^/  /' "$DIST/SHA256SUMS"

if [ "$dry_run" -eq 1 ]; then
	echo "release.sh: --dry-run; would create $version with the assets above"
	exit 0
fi

command -v gh >/dev/null 2>&1 || { echo "release.sh: publishing needs the GitHub CLI (gh)" >&2; exit 1; }
gh release create "$version" --title "Maxon $version_from_binary" --notes-file "$DIST/NOTES.md" \
	$archives "$DIST/SHA256SUMS"
echo "release.sh: published $version"
