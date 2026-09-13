#!/bin/sh
#
# Install Maxon on macOS or Linux:
#
#   curl -fsSL https://maxon.dev/install.sh | sh
#
# Downloads the release archive for this machine from GitHub, checks it against the release's
# SHA256SUMS, and installs it into ~/.maxon: the compiler in ~/.maxon/bin, the standard library in
# ~/.maxon/stdlib and the language runtime in ~/.maxon/runtime. Running it again installs the latest
# release, or says the install is current.
# `usage` below lists the options.
#
# ⛔ `maxon upgrade` RUNS THIS SCRIPT, SO ITS INTERFACE IS A CONTRACT WITH EVERY SHIPPED COMPILER:
# it honours MAXON_INSTALL and --no-modify-path, and exits 0 when the install is current and non-zero
# when it is not. Renaming or dropping any of those breaks `maxon upgrade` in every release that has it.

# Everything is inside main, so a download cut off partway runs nothing.
main() {
	set -eu

	repo="maxon-lang/maxon"
	version=""
	modify_path=1
	force=0

	while [ $# -gt 0 ]; do
		case "$1" in
			--version)        [ $# -ge 2 ] || fail "--version needs a value"; version="${2#v}"; shift 2 ;;
			--version=*)      version="${1#--version=}"; version="${version#v}"; shift ;;
			--no-modify-path) modify_path=0; shift ;;
			--force)          force=1; shift ;;
			-h|--help)        usage; exit 0 ;;
			*)                fail "unknown option '$1' (see --help)" ;;
		esac
	done

	if [ -n "$version" ]; then
		check_version "$version"
	fi

	need curl
	need tar
	need uname

	target="$(detect_target)"
	root="$(install_root)"
	bin_dir="$root/bin"

	pinned=1
	[ -n "$version" ] || pinned=0

	if [ -n "${MAXON_DOWNLOAD_BASE:-}" ]; then
		base="$MAXON_DOWNLOAD_BASE"
		[ "$pinned" -eq 1 ] || fail "MAXON_DOWNLOAD_BASE needs --version: a mirror has no 'latest' to ask"
		fetch() { curl -fsSL "$1" -o "$2"; }
	else
		base="https://github.com/$repo/releases/download"
		# Only https, and only TLS 1.2 or later, for anything this script runs.
		fetch() { curl --proto '=https' --tlsv1.2 -fsSL "$1" -o "$2"; }
		[ "$pinned" -eq 1 ] || version="$(latest_version)"
	fi

	if [ "$force" -eq 0 ] && [ -d "$root/stdlib" ] && [ "$(installed_version "$bin_dir/maxon")" = "$version" ]; then
		if [ "$pinned" -eq 1 ]; then
			say "Maxon $version is already installed in $root (--force reinstalls it)"
		else
			say "Maxon $version, the latest release, is already installed in $root (--force reinstalls it)"
		fi
		finish_path "$bin_dir" "$modify_path"
		return 0
	fi

	# A directory that already holds a stdlib/, runtime/ or examples/ of its own is not ours to replace.
	if [ ! -e "$bin_dir/maxon" ]; then
		for entry in stdlib runtime examples; do
			if [ -e "$root/$entry" ]; then
				fail "$root/$entry exists and $root holds no bin/maxon, so it is not a Maxon install; set MAXON_INSTALL to a new directory"
			fi
		done
	fi

	asset="maxon-$version-$target.tar.gz"
	say "installing Maxon $version for $target into $root"

	tmp="$(mktemp -d 2>/dev/null || mktemp -d -t maxon-install)"
	# Staged beside the install so every rename below stays on one filesystem.
	mkdir -p "$bin_dir"
	staging="$root/.staging.$$"
	retired="$root/.retired.$$"
	# `retired` is only ever removed empty: after a kill mid-swap it holds the previous install.
	trap 'rm -rf "$tmp" "$staging"; rmdir "$retired" 2>/dev/null || true' EXIT INT TERM
	rm -rf "$staging"
	mkdir "$staging"

	fetch "$base/v$version/$asset" "$tmp/$asset" \
		|| fail "could not download $asset — is $version a published release with a $target build?"
	fetch "$base/v$version/SHA256SUMS" "$tmp/SHA256SUMS" \
		|| fail "could not download SHA256SUMS for $version"
	verify_checksum "$tmp" "$asset"

	tar -xzf "$tmp/$asset" -C "$staging"
	unpacked="$staging/maxon-$version-$target"
	if [ ! -x "$unpacked/maxon" ] || [ ! -d "$unpacked/stdlib" ] || [ ! -d "$unpacked/examples" ]; then
		fail "$asset does not hold maxon, stdlib/ and examples/ in maxon-$version-$target/"
	fi

	# 126 and 127 are the shell saying it could not execute the file at all, and 128 and up a signal —
	# a wrong architecture or a noexec mount. Any other status is the compiler running and answering,
	# and older releases answer `version` differently.
	rc=0
	"$unpacked/maxon" version >/dev/null 2>&1 || rc=$?
	if [ "$rc" -ge 126 ]; then
		fail "the downloaded compiler does not run on this machine (exit $rc)"
	fi

	mkdir "$retired"
	if ! swap_in "$root" "$unpacked" "$retired"; then
		roll_back "$root" "$retired"
		rm -rf "$retired"
		fail "could not move the new release into $root; the previous install is unchanged"
	fi
	rm -rf "$retired"

	say "installed Maxon $version in $root"
	finish_path "$bin_dir" "$modify_path"
}

usage() {
	cat <<'USAGE'
Install Maxon on macOS or Linux.

  curl -fsSL https://maxon.dev/install.sh | sh
  curl -fsSL https://maxon.dev/install.sh | sh -s -- [options]

Options:
  --version X.Y.Z     install that release instead of the latest
  --force             reinstall even when that release is already installed
  --no-modify-path    do not add ~/.maxon/bin to PATH in your shell profile

Environment:
  MAXON_INSTALL       where Maxon is installed (default ~/.maxon)
  MAXON_DOWNLOAD_BASE a mirror of the release assets, laid out as <base>/v<version>/<asset>

Uninstall: rm -rf ~/.maxon, and delete the line this script added to your shell profile.
USAGE
}

say() { printf 'maxon-install: %s\n' "$1"; }

fail() {
	printf 'maxon-install: %s\n' "$1" >&2
	exit 1
}

need() {
	command -v "$1" >/dev/null 2>&1 || fail "this installer needs '$1', which is not on PATH"
}

# A version is interpolated into URLs and file names, so anything but a version's own characters is refused.
check_version() {
	case "$1" in
		[0-9]*.[0-9]*.[0-9]*) ;;
		*) fail "'$1' is not a release version (expected X.Y.Z)" ;;
	esac
	case "$1" in
		*[!0-9A-Za-z.-]*) fail "'$1' is not a release version (expected X.Y.Z)" ;;
	esac
}

install_root() {
	dir="${MAXON_INSTALL:-$HOME/.maxon}"
	case "$dir" in
		/*) ;;
		*) fail "MAXON_INSTALL must be an absolute path, not '$dir'" ;;
	esac

	while [ "${dir%/}" != "$dir" ]; do
		dir="${dir%/}"
	done
	[ -n "$dir" ] || fail "MAXON_INSTALL cannot be /"
	echo "$dir"
}

# The version an installed compiler reports, or nothing when there is no compiler or it answers in a
# form this script does not know.
installed_version() {
	[ -x "$1" ] || return 0
	"$1" version 2>/dev/null | awk 'NR == 1 && $1 == "maxon" { print $2 }' || true
}

# Each entry is renamed aside before the new one takes its place, and the binary goes last as one
# rename(2): a running compiler keeps its inode, and the old binary stays until the new one is whole.
swap_in() {
	root="$1"
	unpacked="$2"
	retired="$3"
	# ⚠ THE ARCHIVE DECIDES WHICH TIERS EXIST. `runtime/` is absent from releases that predate it, and
	# this script installs any version, so an entry the archive does not carry is only retired.
	for entry in stdlib runtime examples; do
		if [ -e "$root/$entry" ]; then
			mv "$root/$entry" "$retired/$entry" || return 1
		fi
		[ -d "$unpacked/$entry" ] || continue
		mv "$unpacked/$entry" "$root/$entry" || return 1
		: > "$retired/$entry.placed" || return 1
	done
	mv -f "$unpacked/maxon" "$root/bin/maxon" || return 1
}

roll_back() {
	root="$1"
	retired="$2"
	for entry in examples runtime stdlib; do
		if [ -e "$retired/$entry.placed" ]; then
			rm -rf "${root:?}/$entry"
		fi
		if [ -e "$retired/$entry" ]; then
			mv "$retired/$entry" "$root/$entry"
		fi
	done
}

detect_target() {
	os="$(uname -s)"
	arch="$(uname -m)"
	case "$os" in
		Darwin)
			# A shell running under Rosetta reports x86_64 on Apple silicon; the hardware is what matters.
			if [ "$arch" = "x86_64" ] && [ "$(sysctl -n hw.optional.arm64 2>/dev/null || echo 0)" = "1" ]; then
				arch="arm64"
			fi
			case "$arch" in
				arm64) echo "arm64-macos" ;;
				*)     fail "Maxon runs on Apple silicon Macs only; this one reports $arch" ;;
			esac
			;;
		Linux)
			case "$arch" in
				x86_64|amd64)  echo "x64-linux" ;;
				aarch64|arm64) echo "arm64-linux" ;;
				*)             fail "there is no Maxon build for Linux on $arch" ;;
			esac
			;;
		MINGW*|MSYS*|CYGWIN*)
			fail "on Windows, install with: powershell -c \"irm maxon.dev/install.ps1|iex\""
			;;
		*)
			fail "there is no Maxon build for $os"
			;;
	esac
}

# The newest release, from where GitHub redirects /releases/latest — no API call, so no rate limit.
latest_version() {
	url="$(curl --proto '=https' --tlsv1.2 -fsSLo /dev/null -w '%{url_effective}' "https://github.com/$repo/releases/latest")" \
		|| fail "could not ask GitHub for the latest release"
	tag="${url##*/}"
	case "$tag" in
		v[0-9]*) check_version "${tag#v}"; echo "${tag#v}" ;;
		*)       fail "could not read a version from $url" ;;
	esac
}

verify_checksum() {
	dir="$1"
	file="$2"
	# `*` before a name is sha256sum's binary-mode marker.
	expected="$(awk -v f="$file" '{ name = $2; sub(/^\*/, "", name) } name == f { print $1 }' "$dir/SHA256SUMS")"
	[ -n "$expected" ] || fail "SHA256SUMS lists no $file"

	if command -v sha256sum >/dev/null 2>&1; then
		actual="$(sha256sum "$dir/$file" | awk '{ print $1 }')"
	elif command -v shasum >/dev/null 2>&1; then
		actual="$(shasum -a 256 "$dir/$file" | awk '{ print $1 }')"
	else
		fail "this installer needs sha256sum or shasum to check the download"
	fi

	[ "$actual" = "$expected" ] || fail "$file does not match its published checksum — nothing was installed"
}

finish_path() {
	dir="$1"
	modify="$2"
	case ":$PATH:" in
		*":$dir:"*)
			found="$(command -v maxon 2>/dev/null || true)"
			if [ -n "$found" ] && [ "$found" != "$dir/maxon" ]; then
				say "warning: $found comes before $dir on your PATH, so \`maxon\` runs that one$(owner_hint "$found")"
			fi
			;;
		*)
			if [ "$modify" -eq 1 ]; then
				add_to_path "$dir"
			else
				say "$dir is not on your PATH; add it to run \`maxon\` by name"
			fi
			;;
	esac
}

owner_hint() {
	case "$1" in
		/opt/homebrew/*|/home/linuxbrew/.linuxbrew/*|"$HOME"/.linuxbrew/*)
			echo " (Homebrew's; \`brew uninstall maxon-lang/tap/maxon\` removes it)"
			;;
	esac
}

# One line in the profile the user's login shell reads, written once however often this runs.
add_to_path() {
	dir="$1"
	# Written with $HOME unexpanded, so the profile keeps working if the home directory moves.
	case "$dir" in
		"$HOME"/*) shown="\$HOME${dir#"$HOME"}" ;;
		*)         shown="$dir" ;;
	esac

	shell_name="$(basename "${SHELL:-sh}")"
	case "$shell_name" in
		zsh)  profile="${ZDOTDIR:-$HOME}/.zshrc" ;;
		bash)
			if [ "$(uname -s)" = "Darwin" ]; then
				profile="$HOME/.bash_profile"
			else
				profile="$HOME/.bashrc"
			fi
			;;
		fish) profile="$HOME/.config/fish/conf.d/maxon.fish" ;;
		*)    profile="$HOME/.profile" ;;
	esac

	if [ "$shell_name" = "fish" ]; then
		line="fish_add_path \"$shown\""
	else
		line="export PATH=\"$shown:\$PATH\""
	fi

	mkdir -p "$(dirname "$profile")"
	if [ -f "$profile" ] && grep -qxF "$line" "$profile"; then
		:
	else
		printf '\n# Added by the Maxon installer\n%s\n' "$line" >> "$profile"
		say "added $dir to PATH in $profile"
	fi
	say "open a new terminal, or run this to use maxon now:  $line"
}

main "$@"
