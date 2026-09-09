# WHERE THIS HOST'S MAXON BINARIES ARE — written ONCE, for every script that drives one.
#
# ⛔ THE FACT THIS FILE HOLDS IS "AN EXECUTABLE IS CALLED `foo.exe` ON WINDOWS AND `foo` EVERYWHERE
#    ELSE", written ONCE so no script derives it again. A script that spells `.exe` itself works on
#    one host and silently finds nothing on the other — reporting "no compiler" about a tree holding
#    a perfectly good one, which reads as a missing build rather than a missing suffix.
#
# ⇒ Source this, use `$MAXON_EXE_EXT` or the path helpers, and never write `.exe` in a script again.
#   Paths are returned, not echoed with a prefix, so a caller can `[ -x ]` them.

case "$(uname -s)" in
	MINGW*|MSYS*|CYGWIN*|Windows_NT) MAXON_EXE_EXT=".exe"; MAXON_HOST_IS_WINDOWS=1 ;;
	*)                               MAXON_EXE_EXT="";     MAXON_HOST_IS_WINDOWS=0 ;;
esac

# The compiler in <tree> — the one everything here runs. Gitignored; `maxon build` at the root makes it.
maxon_compiler_path() { printf '%s/maxon-bin/.maxon/maxon%s' "${1:-.}" "$MAXON_EXE_EXT"; }

# A released compiler, placed here by hand, used to build the tree when the slot above is empty. It is
# a previous build of THIS compiler and not a second implementation.
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
