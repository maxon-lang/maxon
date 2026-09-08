#!/usr/bin/env bash
#
# Fetch the vendored wasm toolchain for ONE platform, verify it, and place it under `vendor/`.
#
# `vendor/` is gitignored: these are third-party release binaries, tens of megabytes each, and a
# clone should not carry four platforms' copies of a tool it will run one of. What is committed is
# this script and the manifest below — the version, the URL and the SHA256 of every artifact — so a
# fetch is reproducible and a tampered download is a refusal rather than a silent substitution.
#
# ⭐ ONE PLATFORM'S BINARIES, UNDER THEIR NATURAL NAMES. The tool that ends up in `vendor/wasmtime/`
# is the one THIS host runs, called `wasmtime` on unix and `wasmtime.exe` on Windows — there is no
# extensionless-means-Mach-O convention to remember, because a machine never holds another
# platform's build. Pass a target explicitly to stage a different one (for a remote host, or a CI
# runner priming a cache).
#
# Usage:
#   scripts/fetch-vendor.sh                      # this host's target, the tools the tree invokes
#   scripts/fetch-vendor.sh arm64-macos          # another target
#   scripts/fetch-vendor.sh --force              # re-fetch even if the stamp already matches
#   scripts/fetch-vendor.sh wasm-opt             # exactly that tool (it is not in the default set)
#   scripts/fetch-vendor.sh x64-linux wasmtime   # a target and a tool list, in any order
#
# Targets: x64-windows · x64-linux · arm64-linux · arm64-macos

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh

# ⚠ `wasm-opt` IS NOT FETCHED BY DEFAULT BECAUSE NOTHING IN THIS TREE INVOKES IT. The wasm lane runs
# `wasmtime` (to execute a component) and `wasm-tools` (to wrap the emitted core module into one, and
# to disassemble when a wrong answer has to be attributed to an instruction). Binaryen is here for a
# reader who wants a second, stricter parser's opinion of an emitted module — ask for it by name.
DEFAULT_TOOLS="wasmtime wasm-tools"

WASMTIME_VERSION="v44.0.0"
WASM_TOOLS_VERSION="1.250.0"
BINARYEN_VERSION="version_129"

target=""
tools=""
force=0

for arg in "$@"; do
	case "$arg" in
		--force) force=1 ;;
		-h|--help) sed -n '2,26p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
		x64-windows|x64-linux|arm64-linux|arm64-macos)
			[ -z "$target" ] || { echo "fetch-vendor.sh: two targets named: $target and $arg" >&2; exit 2; }
			target="$arg" ;;
		wasmtime|wasm-tools|wasm-opt) tools="$tools $arg" ;;
		*) echo "fetch-vendor.sh: unknown argument: $arg" >&2; exit 2 ;;
	esac
done

[ -n "$target" ] || target="$(maxon_host_target)"
[ -n "$tools" ] || tools="$DEFAULT_TOOLS"

# The artifact for a tool on a target: its URL, its SHA256, and the basename to keep out of it.
#
# ⛔ EVERY HASH HERE WAS COMPUTED FROM THE ARTIFACT ITSELF. Binaryen publishes a `.sha256` beside each
# asset and those are its numbers verbatim; wasmtime and wasm-tools publish none, so theirs were taken
# by downloading the asset and hashing it. A bump means re-taking them the same way — never copying a
# number forward, which would verify the new download against the old file's identity.
artifact_url() {
	case "$1/$2" in
		wasmtime/x64-windows) echo "https://github.com/bytecodealliance/wasmtime/releases/download/$WASMTIME_VERSION/wasmtime-$WASMTIME_VERSION-x86_64-windows.zip" ;;
		wasmtime/x64-linux)   echo "https://github.com/bytecodealliance/wasmtime/releases/download/$WASMTIME_VERSION/wasmtime-$WASMTIME_VERSION-x86_64-linux.tar.xz" ;;
		wasmtime/arm64-linux) echo "https://github.com/bytecodealliance/wasmtime/releases/download/$WASMTIME_VERSION/wasmtime-$WASMTIME_VERSION-aarch64-linux.tar.xz" ;;
		wasmtime/arm64-macos) echo "https://github.com/bytecodealliance/wasmtime/releases/download/$WASMTIME_VERSION/wasmtime-$WASMTIME_VERSION-aarch64-macos.tar.xz" ;;

		wasm-tools/x64-windows) echo "https://github.com/bytecodealliance/wasm-tools/releases/download/v$WASM_TOOLS_VERSION/wasm-tools-$WASM_TOOLS_VERSION-x86_64-windows.zip" ;;
		wasm-tools/x64-linux)   echo "https://github.com/bytecodealliance/wasm-tools/releases/download/v$WASM_TOOLS_VERSION/wasm-tools-$WASM_TOOLS_VERSION-x86_64-linux.tar.gz" ;;
		wasm-tools/arm64-linux) echo "https://github.com/bytecodealliance/wasm-tools/releases/download/v$WASM_TOOLS_VERSION/wasm-tools-$WASM_TOOLS_VERSION-aarch64-linux.tar.gz" ;;
		wasm-tools/arm64-macos) echo "https://github.com/bytecodealliance/wasm-tools/releases/download/v$WASM_TOOLS_VERSION/wasm-tools-$WASM_TOOLS_VERSION-aarch64-macos.tar.gz" ;;

		wasm-opt/x64-windows) echo "https://github.com/WebAssembly/binaryen/releases/download/$BINARYEN_VERSION/binaryen-$BINARYEN_VERSION-x86_64-windows.tar.gz" ;;
		wasm-opt/x64-linux)   echo "https://github.com/WebAssembly/binaryen/releases/download/$BINARYEN_VERSION/binaryen-$BINARYEN_VERSION-x86_64-linux.tar.gz" ;;
		wasm-opt/arm64-linux) echo "https://github.com/WebAssembly/binaryen/releases/download/$BINARYEN_VERSION/binaryen-$BINARYEN_VERSION-aarch64-linux.tar.gz" ;;
		wasm-opt/arm64-macos) echo "https://github.com/WebAssembly/binaryen/releases/download/$BINARYEN_VERSION/binaryen-$BINARYEN_VERSION-arm64-macos.tar.gz" ;;

		*) return 1 ;;
	esac
}

artifact_sha256() {
	case "$1/$2" in
		wasmtime/x64-windows) echo "6139de7554514c2df1e30cec9b1e6ca493685ece23a039f987b55bf32bc42a57" ;;
		wasmtime/x64-linux)   echo "52eba06fe9f4364aa6164a4a3eafb2ca692ba9a756cbe8137b5574871f8cbfc8" ;;
		wasmtime/arm64-linux) echo "294cae921fb88cbbcb60a914eaaaf313df3249d718609afb5804186b3f1912f5" ;;
		wasmtime/arm64-macos) echo "38a3b9d9fe64cee21bc9d9268e2c19fa35a7be59e030717545b84da0e6514eab" ;;

		wasm-tools/x64-windows) echo "e6ab7924618d1caeb6eaa9debdf2a20ad9248f830731493776b283e42e1cd62e" ;;
		wasm-tools/x64-linux)   echo "b746c34e7c4162b8812eb29397ebe076834e496a8c46fe68d793379a2741eb50" ;;
		wasm-tools/arm64-linux) echo "a6f7684f4bc618068cf9ae09cf3c1ccfe72a97061324c8f7a15f409f6a4c18c3" ;;
		wasm-tools/arm64-macos) echo "1efe40e1923a80947db3a9a8b84c64442c539988a25cfd4ebe516d00bb5c4ba3" ;;

		wasm-opt/x64-windows) echo "1405d2f51377859ccf5fcd2c59c0a8c5756373e691ca0eeb5219f646b743e3aa" ;;
		wasm-opt/x64-linux)   echo "50b9fa62b9abea752da92ec57e0c555fee578760cd237c40107957715d2976ba" ;;
		wasm-opt/arm64-linux) echo "81d46b86b10876ab615eec67e09fcc5615115a7b189cfe3d466725ee36c46ac2" ;;
		wasm-opt/arm64-macos) echo "d1bb014775ca3002506712b81b4406d126ff6845e8b2f343bc2696a1a88b7117" ;;

		*) return 1 ;;
	esac
}

tool_version() {
	case "$1" in
		wasmtime)   echo "$WASMTIME_VERSION" ;;
		wasm-tools) echo "$WASM_TOOLS_VERSION" ;;
		wasm-opt)   echo "$BINARYEN_VERSION" ;;
	esac
}

# Windows binaries carry `.exe`; every other target's do not. This is the TARGET's convention, not the
# host's, so it cannot come from `MAXON_EXE_EXT` — staging arm64-macos from Windows must not produce
# a `wasmtime.exe` that machine will never run.
target_exe_ext() { case "$1" in *-windows) echo ".exe" ;; *) echo "" ;; esac; }

# ⛔ `local`, ON EVERY ONE. Without it this function assigns to the CALLER's `dest` — the vendor
# directory the loop is about to `rm -rf` — and the removal takes the freshly extracted tree instead.
extract_into() {
	local src="$1" into="$2"
	mkdir -p "$into"
	case "$src" in
		*.zip)     unzip -q -o "$src" -d "$into" ;;
		*.tar.xz)  tar -xJf "$src" -C "$into" ;;
		*.tar.gz)  tar -xzf "$src" -C "$into" ;;
		*) echo "fetch-vendor.sh: unknown archive type: $src" >&2; return 1 ;;
	esac
}

ext="$(target_exe_ext "$target")"
fetched=0

# ⛔ NO `trap ... EXIT` HERE, DELIBERATELY. Bash runs an EXIT trap when a COMMAND-SUBSTITUTION subshell
# exits, so a trap that removes this directory removes it the first time `$(sha256sum …)` or
# `$(find …)` runs — the download verifies, the archive extracts, and the copy then fails on a path
# that was deleted between reading it and using it. Cleanup is explicit, and a FAILED run deliberately
# leaves the directory behind: it holds the archive whose checksum did not match.
work="$(mktemp -d)"

for tool in $tools; do
	version="$(tool_version "$tool")"
	dest="vendor/$tool"
	stamp="$dest/.fetched"
	binary="$dest/$tool$ext"

	# The stamp records what is on disk, so a re-run is a no-op and a version bump re-fetches. It names
	# the TARGET too: staging another platform's build into the same directory must not be mistaken for
	# a cache hit on this one's.
	if [ "$force" -eq 0 ] && [ -f "$stamp" ] && [ -x "$binary" ] \
		&& [ "$(cat "$stamp")" = "$tool $version $target" ]; then
		echo "fetch-vendor.sh: $tool $version ($target) already present"
		continue
	fi

	url="$(artifact_url "$tool" "$target")" || {
		echo "fetch-vendor.sh: no $tool artifact for $target" >&2; exit 1; }
	want="$(artifact_sha256 "$tool" "$target")"

	echo "fetch-vendor.sh: fetching $tool $version for $target"
	archive="$work/$(basename "$url")"
	curl -fsSL -o "$archive" "$url" \
		|| { echo "fetch-vendor.sh: download failed: $url" >&2; exit 1; }

	# ⛔ VERIFY OR REFUSE, and DELETE what failed. Leaving a mismatched archive on disk invites a
	# second run to find it and skip the download, which is how an unverified file becomes the one in
	# use.
	got="$(sha256sum "$archive" | cut -d' ' -f1)"
	if [ "$got" != "$want" ]; then
		rm -f "$archive"
		echo "fetch-vendor.sh: SHA256 MISMATCH for $tool $version ($target) — deleted the download" >&2
		echo "  expected $want" >&2
		echo "  got      $got" >&2
		echo "  $url" >&2
		exit 1
	fi

	rm -rf "$work/x"
	extract_into "$archive" "$work/x"

	# ⭐ FIND THE BINARY BY NAME rather than by a path spelled per tool: the three archives lay out
	# differently (a versioned top directory, or `bin/` under one), and a layout that changes upstream
	# should not be a silent miss.
	found="$(find "$work/x" -type f -name "$tool$ext" | head -n1)"
	[ -n "$found" ] || { echo "fetch-vendor.sh: no $tool$ext inside $(basename "$url")" >&2; exit 1; }

	rm -rf "$dest"
	mkdir -p "$dest"
	cp "$found" "$binary"
	chmod +x "$binary"

	# Binaryen's macOS build links against a dylib beside it and will not start without one.
	for lib in libbinaryen.dylib libbinaryen.so; do
		extra="$(find "$work/x" -type f -name "$lib" | head -n1)"
		[ -n "$extra" ] && cp "$extra" "$dest/$lib"
	done

	printf '%s %s %s' "$tool" "$version" "$target" > "$stamp"
	fetched=$((fetched + 1))
done

rm -rf "$work"
echo "fetch-vendor.sh: $fetched tool(s) fetched for $target into vendor/"
