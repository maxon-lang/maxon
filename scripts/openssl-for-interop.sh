#!/bin/sh
# Prints the path of the OpenSSL that specs/tls-interop.md spawns (it reads MAXON_OPENSSL), installing it
# where the runner has none. The cases need OpenSSL 1.1.1 or later: `-ciphersuites`, `-naccept` and
# `-attime`. macOS ships LibreSSL, which has none of them, so that lane uses Homebrew's openssl@3.

set -eu

case "$(uname -s)" in
Darwin)
	brew list openssl@3 >/dev/null 2>&1 || brew install openssl@3
	openssl="$(brew --prefix openssl@3)/bin/openssl"
	;;
MINGW* | MSYS* | CYGWIN*)
	openssl="$(cygpath -m "$(command -v openssl)")"
	;;
*)
	openssl="$(command -v openssl)"
	;;
esac

version="$("$openssl" version)"

case "$version" in
"OpenSSL 1.1.1"* | "OpenSSL 3."*) ;;
*)
	echo "openssl-for-interop.sh: $openssl is '$version'; the interop cases need OpenSSL 1.1.1 or later" >&2
	exit 1
	;;
esac

echo "openssl-for-interop.sh: $openssl is $version" >&2
printf '%s\n' "$openssl"
