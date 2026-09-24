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

# Beside a cross-built archive in `dist/`: its suite was not run on its own platform.
UntestedMarkerSuffix=".untested"

# Where the release notes send a reader. `announce.sh` writes each release's post as `maxon-X-Y-Z`.
SiteUrl="https://maxon.dev"
ChangelogUrl="$SiteUrl/docs/changelog/"
InstallationUrl="$SiteUrl/docs/getting-started/installation/"

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

# ⛔ THE VERSION IS READ OFF THE BINARY, NOT OFF THE REF. `maxon.maxproj` derives it from git at build
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
		"$maxon" run build --target="$tgt" --output="$stage/maxon$exe_ext" >/dev/null \
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

	# ⚠ `stdlib/` AND `runtime/` SHIP AS SOURCE, WITHOUT THEIR BUILD CACHES. The compiler reads both
	# from source and finds them by walking up from its own executable — `runtime/` as a sibling of
	# `stdlib/` — so the layout here IS the contract: `maxon`, `stdlib/` and `runtime/` side by side.
	# `.maxon/` is this machine's cache and means nothing anywhere else. An archive missing either
	# directory yields a compiler that cannot compile anything, so neither copy may be made optional.
	cp -r stdlib "$stage/stdlib"
	rm -rf "$stage/stdlib/.maxon"
	cp -r runtime "$stage/runtime"
	rm -rf "$stage/runtime/.maxon"
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

	# The marker is how `--publish`, which may run elsewhere and later, learns that this archive's suite
	# never ran; the release notes name every archive that carries one.
	if [ "$native" -eq 1 ]; then
		rm -f "$archive$UntestedMarkerSuffix"
	else
		: > "$archive$UntestedMarkerSuffix"
		echo "release.sh: ⚠ $tgt was CROSS-BUILT and its suite was not run here" >&2
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
    runtime/          the language runtime the compiler links in, as source

⛔ **Keep these three together.** The compiler finds \`stdlib/\` and \`runtime/\` by walking UP from its
own executable, so moving \`maxon$exe_ext\` somewhere else on its own leaves it unable to compile at
all. Move the whole directory, or put it on your PATH as it is.

## Install

The one-line installers do all of this for you — see https://maxon.dev/docs/getting-started/installation/:

    curl -fsSL https://maxon.dev/install.sh | sh          # macOS and Linux
    irm https://maxon.dev/install.ps1 | iex               # Windows, in PowerShell

By hand: extract the archive somewhere permanent and add that directory to your PATH. Then:

    maxon version
    maxon build examples/basic.maxon
    ./examples/basic

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

The `maxon.exe` in this archive is not code-signed, so when the archive came from a browser, SmartScreen
may warn the first time you run it. Choose **More info** then **Run anyway**. The checksum published
beside this archive is what tells you the file is the one that was built.
EOF
			;;
	esac
}

# Release notes: where to read what changed and how to install, then what is attached.
#
# ⭐ **THE CHANGELOG AND THE INSTALL INSTRUCTIONS LIVE ON maxon.dev, AND THE NOTES LINK TO THEM.** A
# copy here is a second place for either to be wrong: the installation page changes between releases,
# and notes published once never do. The site deploys right after the release, so the links go live
# within minutes of the notes.
write_default_notes() {
	local out="$1"

	# ⛔ **WRITTEN ASIDE AND MOVED INTO PLACE, because a half-written file here is indistinguishable
	# from a hand-written one**, and the caller regenerates only when `NOTES.md` is absent. The stale
	# partial is cleared on entry rather than by a trap, because under `set -e` a failure exits the whole
	# script and no `RETURN` trap runs.
	local partial="$out.partial"
	rm -f "$partial"

	{
		echo "- [What's new in Maxon $version_from_binary]($SiteUrl/blog/maxon-${version_from_binary//./-}/)"
		echo "- [Changelog]($ChangelogUrl), every release"
		echo "- [Installation]($InstallationUrl)"
		echo
		echo "## Assets"
		echo
		local f
		for f in $(find "$DIST" -maxdepth 1 -type f \( -name '*.zip' -o -name '*.tar.gz' \) -printf '%f\n' | sort); do
			if [ -f "$DIST/$f$UntestedMarkerSuffix" ]; then
				echo "- \`$f\` ⚠ cross-built: the test suite was not run on its platform"
			else
				echo "- \`$f\`"
			fi
		done
		echo
		echo "\`SHA256SUMS\` is published beside the archives. The archives are not signed, so the checksum is what"
		echo "tells you a file is the one that was built."
	} > "$partial"

	mv "$partial" "$out"
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

			# The tree-level gates `spec-test` does not run. The timeout is `spec-harness`'s: its drift
			# gate runs `spec-test` three times, within a second of the 5 s default.
			for corpus in fmt spec-harness ladders; do
				log="$DIST/test-$corpus-$host.log"
				"$maxon" test "tests/$corpus" --timeout=15000 > "$log" 2>&1 \
					|| { echo "release.sh: tests/$corpus FAILED on $host — see $log; nothing packaged" >&2; exit 1; }
				grep -E "^ *[0-9]+ fail" "$log" | tail -1
			done
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
# downstream — the archives, the Homebrew formula, the install page — derives from one of the two, so a
# disagreement here becomes a release whose parts name different versions.
if [ "$version" != "v$version_from_binary" ]; then
	echo "release.sh: the tag says $version and the compiler says $version_from_binary." >&2
	echo "  Build from a release/X.Y.Z branch or a vX.Y.Z tag and rebuild, or tag the version that was built." >&2
	exit 1
fi

archives="$(find "$DIST" -maxdepth 1 -type f \( -name '*.zip' -o -name '*.tar.gz' \) | sort)"
[ -n "$archives" ] || { echo "release.sh: no archives in $DIST — run --package first" >&2; exit 1; }

( cd "$DIST" && sha256sum $(find . -maxdepth 1 -type f \( -name '*.zip' -o -name '*.tar.gz' \) -printf '%f\n' | sort) > SHA256SUMS )
echo "release.sh: checksums"
sed 's/^/  /' "$DIST/SHA256SUMS"

# ⭐ WRITTEN BEFORE THE DRY-RUN EXIT, so `--dry-run` exercises the notes too. A preview that skips the
# one artifact a person actually reads is not a preview.
# ⭐ THE NOTES ARE WRITTEN HERE IF NOBODY WROTE THEM. A release with no notes is a page of filenames,
# and the one thing a reader wants — which file is theirs, and what to do with it — is exactly what the
# filenames do not say. A `dist/NOTES.md` placed by hand wins: this is a floor, not a template to
# fight.
# ⛔ A RELEASE WITH NO CHANGELOG ENTRY DOES NOT SHIP: the post and the changelog page the notes link to
# are built from it. `release.yml`'s guard asks before any runner starts; this asks again at the last
# moment, and `--section` refuses a version the file has no heading for.
scripts/changelog.sh --section="$version_from_binary" > /dev/null

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
