#!/bin/sh
#
# Install Maxon on macOS or Linux:
#
#   curl -fsSL https://maxon.dev/install.sh | sh
#
# Downloads the release archive for this machine from GitHub, checks it against the release's
# SHA256SUMS, unpacks it into ~/.local/share/maxon and links ~/.local/bin/maxon to it. Running it again
# replaces the install with the latest release. `usage` below lists the options.

# Everything is inside main, so a download cut off partway runs nothing.
main() {
	set -eu

	repo="maxon-lang/maxon"
	version=""
	modify_path=1

	while [ $# -gt 0 ]; do
		case "$1" in
			--version)        [ $# -ge 2 ] || fail "--version needs a value"; version="${2#v}"; shift 2 ;;
			--version=*)      version="${1#--version=}"; version="${version#v}"; shift ;;
			--no-modify-path) modify_path=0; shift ;;
			-h|--help)        usage; exit 0 ;;
			*)                fail "unknown option '$1' (see --help)" ;;
		esac
	done

	need curl
	need tar
	need uname

	target="$(detect_target)"
	install_dir="${MAXON_INSTALL_DIR:-$HOME/.local/share/maxon}"
	bin_dir="${MAXON_BIN_DIR:-$HOME/.local/bin}"

	if [ -n "${MAXON_DOWNLOAD_BASE:-}" ]; then
		base="$MAXON_DOWNLOAD_BASE"
		[ -n "$version" ] || fail "MAXON_DOWNLOAD_BASE needs --version: a mirror has no 'latest' to ask"
		fetch() { curl -fsSL "$1" -o "$2"; }
	else
		base="https://github.com/$repo/releases/download"
		# Only https, and only TLS 1.2 or later, for anything this script runs.
		fetch() { curl --proto '=https' --tlsv1.2 -fsSL "$1" -o "$2"; }
		[ -n "$version" ] || version="$(latest_version)"
	fi

	asset="maxon-$version-$target.tar.gz"
	say "installing Maxon $version for $target"

	tmp="$(mktemp -d 2>/dev/null || mktemp -d -t maxon-install)"
	trap 'rm -rf "$tmp"' EXIT INT TERM

	fetch "$base/v$version/$asset" "$tmp/$asset" \
		|| fail "could not download $asset — is $version a published release with a $target build?"
	fetch "$base/v$version/SHA256SUMS" "$tmp/SHA256SUMS" \
		|| fail "could not download SHA256SUMS for $version"
	verify_checksum "$tmp" "$asset"

	# The compiler finds stdlib/ by walking up from its own executable, so the archive's layout — `maxon`
	# and `stdlib/` side by side — is kept whole, and only a link to the binary goes on PATH.
	mkdir -p "$tmp/unpacked"
	tar -xzf "$tmp/$asset" -C "$tmp/unpacked"
	unpacked="$tmp/unpacked/maxon-$version-$target"
	if [ ! -x "$unpacked/maxon" ] || [ ! -d "$unpacked/stdlib" ]; then
		fail "$asset does not hold maxon and stdlib/ in maxon-$version-$target/"
	fi

	# Unpacked next to its destination and swapped in with two renames, so a failure leaves either the
	# old install or the new one, never half of each.
	mkdir -p "$(dirname "$install_dir")" "$bin_dir"
	staged="$install_dir.new.$$"
	retired="$install_dir.old.$$"
	rm -rf "$staged"
	mv "$unpacked" "$staged"
	# 126 and 127 are the shell saying it could not execute the file at all, and 128 and up a signal —
	# a wrong architecture or a noexec mount. Any other status is the compiler running and answering,
	# and older releases answer `version` differently.
	rc=0
	"$staged/maxon" version >/dev/null 2>&1 || rc=$?
	if [ "$rc" -ge 126 ]; then
		rm -rf "$staged"
		fail "the downloaded compiler does not run on this machine (exit $rc)"
	fi
	if [ -e "$install_dir" ]; then
		mv "$install_dir" "$retired"
	fi
	mv "$staged" "$install_dir"
	rm -rf "$retired"

	ln -sf "$install_dir/maxon" "$bin_dir/maxon"
	say "installed Maxon $version in $install_dir, linked as $bin_dir/maxon"

	case ":$PATH:" in
		*":$bin_dir:"*)
			found="$(command -v maxon 2>/dev/null || true)"
			if [ -n "$found" ] && [ "$found" != "$bin_dir/maxon" ]; then
				say "warning: $found comes before $bin_dir on your PATH, so \`maxon\` runs that one"
			fi
			;;
		*)
			if [ "$modify_path" -eq 1 ]; then
				add_to_path "$bin_dir"
			else
				say "$bin_dir is not on your PATH; add it to run \`maxon\` by name"
			fi
			;;
	esac
}

usage() {
	cat <<'USAGE'
Install Maxon on macOS or Linux.

  curl -fsSL https://maxon.dev/install.sh | sh
  curl -fsSL https://maxon.dev/install.sh | sh -s -- [options]

Options:
  --version X.Y.Z     install that release instead of the latest
  --no-modify-path    do not add the link directory to PATH in your shell profile

Environment:
  MAXON_INSTALL_DIR   where the release is unpacked (default ~/.local/share/maxon)
  MAXON_BIN_DIR       where the `maxon` link goes (default ~/.local/bin)
  MAXON_DOWNLOAD_BASE a mirror of the release assets, laid out as <base>/v<version>/<asset>

Uninstall: rm -rf ~/.local/share/maxon ~/.local/bin/maxon
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
			fail "on Windows, install with: winget install MaxonLang.Maxon"
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
		v[0-9]*) echo "${tag#v}" ;;
		*)       fail "could not read a version from $url" ;;
	esac
}

verify_checksum() {
	dir="$1"
	file="$2"
	# `*` before a name is sha256sum's binary-mode marker, which v0.1.0's SHA256SUMS carries.
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

# One line in the profile the user's login shell reads, written once however often this runs.
add_to_path() {
	dir="$1"
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
		line="fish_add_path \"$dir\""
	else
		line="export PATH=\"$dir:\$PATH\""
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
