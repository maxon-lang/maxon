# WHERE THIS HOST'S MAXON BINARIES ARE, AND WHAT EACH TARGET CALLS ITS BINARY AND ITS ARCHIVE —
# written ONCE, for every script that drives or ships one.
#
# POSIX sh, because `scripts/release-postflight.sh` is `#!/bin/sh` and sources it: no `local` (a
# function that needs its own variables runs in a `( )` subshell), no `[[`, no arrays.
#
# ⛔ THE FACT THIS FILE HOLDS IS "AN EXECUTABLE IS CALLED `foo.exe` ON WINDOWS AND `foo` EVERYWHERE
#    ELSE", written ONCE so no script derives it again. A script that spells `.exe` itself works on
#    one host and silently finds nothing on the other — reporting "no compiler" about a tree holding
#    a perfectly good one, which reads as a missing build rather than a missing suffix.
#
# ⇒ Source this, use `$MAXON_EXE_EXT`, `maxon_target_exe_ext` or the path helpers, and never write
#   `.exe` in a script again.
#   Paths are returned, not echoed with a prefix, so a caller can `[ -x ]` them.

case "$(uname -s)" in
	MINGW*|MSYS*|CYGWIN*|Windows_NT) MAXON_EXE_EXT=".exe"; MAXON_HOST_IS_WINDOWS=1 ;;
	*)                               MAXON_EXE_EXT="";     MAXON_HOST_IS_WINDOWS=0 ;;
esac

# The compiler in <tree> — the one everything here runs. Gitignored; `./maxon-bin/.maxon/maxon run build` at the root makes it.
maxon_compiler_path() { printf '%s/maxon-bin/.maxon/maxon%s' "${1:-.}" "$MAXON_EXE_EXT"; }

# The pinned seed compiler, placed here by scripts/fetch-seed.sh, used to build the tree when the slot above is
# empty. It is a previous build of THIS compiler and not a second implementation.
maxon_downloaded_path() { printf '%s/.bootstrap/maxon%s' "${1:-.}" "$MAXON_EXE_EXT"; }

# ⛔ THE HOST'S `<arch>-<os>` TARGET KEY, DERIVED ONCE. Release archives, the vendored toolchain and
#    `--target=` all name a platform the same way, so the mapping from `uname` to that spelling is one
#    fact — and a second copy is how one script learns about a platform the others never hear of.
#    Prints the key; a host this project does not target is a hard failure, never a guess.
maxon_host_target() {
	case "$(uname -s)" in
		MINGW*|MSYS*|CYGWIN*|Windows_NT) printf 'x64-windows' ;;
		Darwin*)                         printf 'arm64-macos' ;;
		Linux*) case "$(uname -m)" in
			aarch64|arm64) printf 'arm64-linux' ;;
			*)             printf 'x64-linux' ;;
		esac ;;
		*) echo "host-binaries.sh: unsupported host: $(uname -s)" >&2; return 1 ;;
	esac
}

# The workflow matrices and the installers (`website/public/install.sh`, `install.ps1`,
# `installer/docker/Dockerfile`, `installer/homebrew/generate.sh`) run without this tree and spell
# the targets themselves, so a target added here is added there too.
MaxonTargets="x64-windows x64-linux arm64-macos arm64-linux"

maxon_is_target() (
	for known_target in $MaxonTargets; do
		[ "$known_target" != "$1" ] || return 0
	done

	return 1
)

maxon_require_target() {
	maxon_is_target "$1" || { echo "host-binaries.sh: $1 is not a Maxon target (one of: $MaxonTargets)" >&2; return 1; }
}

# The TARGET's convention, so it is not `MAXON_EXE_EXT`, which is the host's: staging arm64-macos
# from Windows must not produce a `maxon.exe` that machine will never run.
maxon_target_exe_ext() {
	maxon_require_target "$1" || return 1

	case "$1" in
		*-windows)       printf '.exe' ;;
		*-linux|*-macos) : ;;
		*) echo "host-binaries.sh: no executable extension is recorded for $1" >&2; return 1 ;;
	esac
}

maxon_target_archive_ext() {
	maxon_require_target "$1" || return 1

	case "$1" in
		*-windows)       printf 'zip' ;;
		*-linux|*-macos) printf 'tar.gz' ;;
		*) echo "host-binaries.sh: no archive format is recorded for $1" >&2; return 1 ;;
	esac
}

maxon_archive_extensions() (
	listed_extensions=" "

	for known_target in $MaxonTargets; do
		archive_extension="$(maxon_target_archive_ext "$known_target")" || exit 1

		case "$listed_extensions" in
			*" $archive_extension "*) ;;
			*)
				listed_extensions="$listed_extensions$archive_extension "
				printf '%s\n' "$archive_extension" ;;
		esac
	done
)

maxon_archive_stem() {
	maxon_require_target "$2" || return 1

	printf 'maxon-%s-%s' "$1" "$2"
}

maxon_archive_file() (
	archive_stem="$(maxon_archive_stem "$1" "$2")" || exit 1
	archive_ext="$(maxon_target_archive_ext "$2")" || exit 1

	printf '%s.%s' "$archive_stem" "$archive_ext"
)
