# WHERE THIS HOST'S MAXON BINARIES ARE, AND WHAT EACH TARGET CALLS ITS BINARY AND ITS ARCHIVE —
# read ONCE, from `maxon-bin/targets.tsv`, for every script that drives or ships one.
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
maxon_host_target() (
	host_os="$(uname -s)"
	host_cpu="$(uname -m)"

	case "$host_os" in
		MINGW*|MSYS*|CYGWIN*|Windows_NT) host_os="windows" ;;
		Darwin*)
			host_os="macos"

			# A shell under Rosetta reports x86_64 on Apple silicon; the hardware is what matters.
			if [ "$host_cpu" = "x86_64" ] && [ "$(sysctl -n hw.optional.arm64 2>/dev/null || echo 0)" = "1" ]; then
				host_cpu="arm64"
			fi ;;
		Linux*) host_os="linux" ;;
		*) host_os="$(printf '%s' "$host_os" | tr '[:upper:]' '[:lower:]')" ;;
	esac

	case "$host_cpu" in
		x86_64|amd64)  host_cpu="x64" ;;
		aarch64|arm64) host_cpu="arm64" ;;
		*)             host_cpu="$(printf '%s' "$host_cpu" | tr '[:upper:]' '[:lower:]')" ;;
	esac

	if ! maxon_is_target "$host_cpu-$host_os"; then
		echo "host-binaries.sh: unsupported host: $host_cpu-$host_os is not a Maxon target (one of: $MaxonTargets)" >&2
		return 1
	fi

	printf '%s-%s' "$host_cpu" "$host_os"
)

# The table every target is read from: TAB-separated, a header row, `-` for an empty cell, and a
# comma list in a cell that holds several values. TSV because POSIX sh and Git Bash have no JSON
# reader. Readers find a column by its header name, never its position. `docs/RELEASING.md` says what
# each column means.
#
# Relative to the repository root, because a sourced POSIX script cannot find its own path: a caller
# that runs anywhere else sets MAXON_TARGETS_TABLE first.
MAXON_TARGETS_TABLE="${MAXON_TARGETS_TABLE:-maxon-bin/targets.tsv}"
TableSeparator="$(printf '\t')"
NoValue="-"

maxon_table_header() {
	[ -r "$MAXON_TARGETS_TABLE" ] || { echo "host-binaries.sh: cannot read $MAXON_TARGETS_TABLE, the table every target is read from" >&2; return 1; }

	awk -F "$TableSeparator" 'NR == 1 { sub(/\r$/, ""); for (i = 1; i <= NF; i++) print $i; exit }' "$MAXON_TARGETS_TABLE"
}

maxon_table_select() {
	[ -r "$MAXON_TARGETS_TABLE" ] || { echo "host-binaries.sh: cannot read $MAXON_TARGETS_TABLE, the table every target is read from" >&2; return 1; }

	columns="$1"
	shift

	awk -F "$TableSeparator" -v OFS="$TableSeparator" -v columns="$columns" -v selectors="$*" -v none="$NoValue" '
		function fail(message) {
			print "host-binaries.sh: " message > "/dev/stderr"
			failed = 1
			exit 1
		}

		function column_of(name) {
			if (!(name in index_of)) {
				fail("the table has no `" name "` column")
			}

			return index_of[name]
		}

		function listed(cell, wanted,    items, count, i) {
			count = split(cell, items, ",")

			for (i = 1; i <= count; i++) {
				if (items[i] == wanted) {
					return 1
				}
			}

			return 0
		}

		function selected(    i, name, value, at) {
			for (i = 1; i <= selector_count; i++) {
				if (selector[i] == "native") {
					if ($(column_of("runner")) == none) {
						return 0
					}
				} else if (selector[i] == "cross") {
					if ($(column_of("runner")) != none) {
						return 0
					}
				} else {
					at = index(selector[i], "=")

					if (at == 0) {
						fail("`" selector[i] "` is not a selector (native, cross or column=value)")
					}

					name = substr(selector[i], 1, at - 1)
					value = substr(selector[i], at + 1)

					if (!listed($(column_of(name)), value)) {
						return 0
					}
				}
			}

			return 1
		}

		BEGIN {
			selector_count = split(selectors, selector, " ")
		}

		{
			sub(/\r$/, "")
		}

		NR == 1 {
			for (i = 1; i <= NF; i++) {
				index_of[$i] = i
			}

			header_width = NF

			if (columns == "*") {
				output_count = NF

				for (i = 1; i <= NF; i++) {
					output[i] = i
				}
			} else {
				output_count = split(columns, wanted, ",")

				for (i = 1; i <= output_count; i++) {
					output[i] = column_of(wanted[i])
				}
			}

			next
		}

		NF == 0 {
			next
		}

		{
			if (NF != header_width) {
				fail("row " NR " has " NF " fields but the header has " header_width ": " $0)
			}

			if (selected()) {
				line = ""

				for (i = 1; i <= output_count; i++) {
					line = line (i > 1 ? OFS : "") $(output[i])
				}

				print line
			}
		}

		END {
			if (!failed && NR == 0) {
				fail("the table is empty")
			}
		}
	' "$MAXON_TARGETS_TABLE"
}

MaxonTargets="$(maxon_table_select target native)" || return 1
MaxonTargets="$(printf '%s' "$MaxonTargets" | tr '\n' ' ')"
[ -n "$MaxonTargets" ] || { echo "host-binaries.sh: $MAXON_TARGETS_TABLE lists no native target" >&2; return 1; }

maxon_is_target() (
	for known_target in $MaxonTargets; do
		[ "$known_target" != "$1" ] || return 0
	done

	return 1
)

maxon_require_target() {
	maxon_is_target "$1" || { echo "host-binaries.sh: $1 is not a Maxon target (one of: $MaxonTargets)" >&2; return 1; }
}

maxon_target_cell() (
	maxon_require_target "$1" || exit 1

	cell="$(maxon_table_select "$2" "target=$1")" || exit 1
	[ -n "$cell" ] || { echo "host-binaries.sh: the table records no $2 for $1" >&2; exit 1; }

	printf '%s' "$cell"
)

# The TARGET's convention, so it is not `MAXON_EXE_EXT`, which is the host's: staging arm64-macos
# from Windows must not produce a `maxon.exe` that machine will never run.
maxon_target_exe_ext() (
	exe_ext="$(maxon_target_cell "$1" executable)" || exit 1

	[ "$exe_ext" = "$NoValue" ] || printf '%s' "$exe_ext"
)

maxon_target_archive_ext() (
	archive_ext="$(maxon_target_cell "$1" archive)" || exit 1

	[ "$archive_ext" != "$NoValue" ] || { echo "host-binaries.sh: $1 ships no archive" >&2; exit 1; }

	printf '%s' "$archive_ext"
)

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
