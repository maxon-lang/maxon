#!/bin/sh

set -eu

here="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(cd "$here/../.." && pwd)"
MAXON_TARGETS_TABLE="${MAXON_TARGETS_TABLE:-$repo_root/maxon-bin/targets.tsv}"
export MAXON_TARGETS_TABLE

# shellcheck source=scripts/lib/host-binaries.sh
. "$repo_root/scripts/lib/host-binaries.sh"

usage() {
	printf '%s\n' \
		'usage: matrix.sh json [--with NAME=A,B ...] [--count NAME=COLUMN ...] [SELECTOR ...]' \
		'--count NAME=COLUMN expands each row into NAME=1..N, N being that row'"'"'s COLUMN cell' \
		'       matrix.sh list COLUMN [SELECTOR ...]' \
		'a SELECTOR is native, cross or COLUMN=VALUE (the cell is a comma list holding VALUE); all must hold' >&2
	exit 2
}

selected_rows() {
	selection="$(maxon_table_select "$@")" || exit 1
	[ -n "$selection" ] || { echo "matrix.sh: no row of $MAXON_TARGETS_TABLE matches: ${selectors:-every row}" >&2; exit 1; }

	printf '%s\n' "$selection"
}

dimension_name() {
	printf '%s' "$1" | sed 's/[=#].*//'
}

add_dimension() {
	option="$1"
	name="$2"

	for column in $(maxon_table_header); do
		[ "$column" != "$name" ] || { echo "matrix.sh: $option \`$name\` is already a column of $MAXON_TARGETS_TABLE" >&2; exit 2; }
	done

	for declared in $dimensions; do
		[ "$(dimension_name "$declared")" != "$name" ] || { echo "matrix.sh: $option \`$name\` is given twice" >&2; exit 2; }
	done

	dimensions="$dimensions $3"
}

json_matrix() {
	dimensions=""
	selectors=""

	while [ $# -gt 0 ]; do
		case "$1" in
			--with)
				[ $# -ge 2 ] || usage

				case "$2" in
					?*=?*) ;;
					*) echo "matrix.sh: --with takes NAME=A,B, not \`$2\`" >&2; exit 2 ;;
				esac

				case ",${2#*=}," in
					*",,"*) echo "matrix.sh: --with \`$2\` holds an empty value" >&2; exit 2 ;;
				esac

				add_dimension --with "${2%%=*}" "$2"
				shift 2 ;;
			--count)
				[ $# -ge 2 ] || usage

				case "$2" in
					?*=?*) ;;
					*) echo "matrix.sh: --count takes NAME=COLUMN, not \`$2\`" >&2; exit 2 ;;
				esac

				count_column="${2#*=}"
				header_holds=no

				for column in $(maxon_table_header); do
					[ "$column" != "$count_column" ] || header_holds=yes
				done

				[ "$header_holds" = yes ] || { echo "matrix.sh: --count \`$2\` names \`$count_column\`, which is not a column of $MAXON_TARGETS_TABLE" >&2; exit 2; }

				add_dimension --count "${2%%=*}" "${2%%=*}#$count_column"
				shift 2 ;;
			*) selectors="$selectors $1"; shift ;;
		esac
	done

	# shellcheck disable=SC2086
	rows="$(selected_rows '*' $selectors)" || exit 1
	# shellcheck disable=SC2086
	unrunnable="$(maxon_table_select target cross $selectors)" || exit 1

	if [ -n "$unrunnable" ]; then
		echo "matrix.sh: the selection holds targets with no runner, which a job cannot run on: $(printf '%s' "$unrunnable" | tr '\n' ' '). Select native targets" >&2
		exit 1
	fi

	header="$(maxon_table_header | paste -s -d "$TableSeparator" -)"

	printf '%s\n%s\n' "$header" "$rows" | awk -F "$TableSeparator" -v dimensions="$dimensions" '
		function quoted(text) {
			if (text ~ /["\\]/) {
				print "matrix.sh: `" text "` holds a quote or a backslash, which this does not escape" > "/dev/stderr"
				failed = 1
				exit 1
			}

			return "\"" text "\""
		}

		BEGIN {
			dimension_count = split(dimensions, dimension, " ")
		}

		NR == 1 {
			for (i = 1; i <= NF; i++) {
				key[i] = $i
				column_index[$i] = i
			}

			width = NF
			next
		}

		{
			object = ""

			for (i = 1; i <= width; i++) {
				object = object (i > 1 ? "," : "") quoted(key[i]) ":" quoted($i)
			}

			variants[1] = object
			variant_count = 1

			for (d = 1; d <= dimension_count; d++) {
				at = index(dimension[d], "=")

				if (at > 0) {
					name = substr(dimension[d], 1, at - 1)
					value_count = split(substr(dimension[d], at + 1), value, ",")
				} else {
					at = index(dimension[d], "#")
					name = substr(dimension[d], 1, at - 1)
					count_column = substr(dimension[d], at + 1)
					count_cell = $(column_index[count_column])

					if (count_cell !~ /^[1-9][0-9]*$/) {
						print "matrix.sh: --count " name "=" count_column " needs a positive count, but " $(column_index["target"]) " holds `" count_cell "`" > "/dev/stderr"
						failed = 1
						exit 1
					}

					value_count = count_cell + 0

					for (w = 1; w <= value_count; w++) {
						value[w] = w
					}
				}

				next_count = 0

				for (v = 1; v <= variant_count; v++) {
					for (w = 1; w <= value_count; w++) {
						next_count++
						expanded[next_count] = variants[v] "," quoted(name) ":" quoted(value[w])
					}
				}

				for (v = 1; v <= next_count; v++) {
					variants[v] = expanded[v]
				}

				variant_count = next_count
			}

			for (v = 1; v <= variant_count; v++) {
				entries[++entry_count] = "{" variants[v] "}"
			}
		}

		END {
			if (failed) {
				exit 1
			}

			line = ""

			for (e = 1; e <= entry_count; e++) {
				line = line (e > 1 ? "," : "") entries[e]
			}

			print "{\"include\":[" line "]}"
		}
	'
}

column_list() {
	[ $# -ge 1 ] || usage
	column="$1"
	shift
	selectors="$*"
	# shellcheck disable=SC2086
	selected_rows "$column" $selectors
}

[ $# -gt 0 ] || usage
mode="$1"
shift

case "$mode" in
	json) json_matrix "$@" ;;
	list) column_list "$@" ;;
	*) usage ;;
esac
