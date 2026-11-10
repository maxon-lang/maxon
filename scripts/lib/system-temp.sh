# Sourced. Every path `temp_make` or `temp_register` names is removed when the script exits, a HUP, INT
# or TERM included. A script that installs its own EXIT trap calls `temp_remove_all` from it, or what it
# made outlives it.
#
# The first `temp_make` of a run also removes this prefix's entries older than `temp-names.tsv` allows,
# which a killed run left. Write permission is restored first: a directory a gate left without it cannot
# have its entries removed.
TempNamesTable="${TempNamesTable:-scripts/lib/temp-names.tsv}"
TempTableSeparator="$(printf '\t')"
TempExitHangup=129
TempExitInterrupt=130
TempExitTerminate=143

TempPaths="${TempPaths-}"
TempTrapsInstalled="${TempTrapsInstalled-0}"
TempMade=""

temp_names_field() {
	[ -r "$TempNamesTable" ] || { echo "system-temp.sh: cannot read $TempNamesTable, the table the temp name prefixes are read from" >&2; return 1; }

	awk -F "$TempTableSeparator" -v row="$1" -v column="$2" '
		{ sub(/\r$/, "") }
		NR == 1 { for (i = 1; i <= NF; i++) if ($i == column) wanted = i; next }
		$1 == row && wanted { print $wanted; found = 1; exit }
		END { exit found ? 0 : 1 }
	' "$TempNamesTable" || { echo "system-temp.sh: $TempNamesTable has no '$column' for '$row'" >&2; return 1; }
}

TempNamePrefix="$(temp_names_field script prefix)" || exit 1
TempStaleMinutes="$(temp_names_field script stale_minutes)" || exit 1

temp_remove_all() {
	printf '%s' "$TempPaths" | while IFS= read -r temp_path; do
		[ -z "$temp_path" ] || rm -rf "$temp_path"
	done
}

temp_sweep_stale() {
	find "$1" -mindepth 1 -maxdepth 1 -name "$TempNamePrefix*" -mmin "+$TempStaleMinutes" -exec sh -c 'for stale; do [ -L "$stale" ] || chmod -R u+rwx "$stale"; done; rm -rf "$@"' temp-sweep {} + 2>/dev/null || true
}

temp_install_traps() {
	trap temp_remove_all EXIT
	trap "exit $TempExitHangup" HUP
	trap "exit $TempExitInterrupt" INT
	trap "exit $TempExitTerminate" TERM
	TempTrapsInstalled=1
}

temp_register() {
	if [ "$TempTrapsInstalled" -eq 0 ]; then
		temp_install_traps
	fi

	TempPaths="$TempPaths$1
"
}

temp_make() {
	temp_kind="$1"
	temp_label="$2"
	temp_base="${TMPDIR:-/tmp}"
	temp_base="${temp_base%/}"

	if [ "$TempTrapsInstalled" -eq 0 ]; then
		temp_sweep_stale "$temp_base"
	fi

	case "$temp_kind" in
		dir)  temp_made="$(mktemp -d "$temp_base/$TempNamePrefix$temp_label-XXXXXX")" || return 1 ;;
		file) temp_made="$(mktemp "$temp_base/$TempNamePrefix$temp_label-XXXXXX")" || return 1 ;;
		*)    echo "system-temp.sh: temp_make takes 'dir' or 'file', got '$temp_kind'" >&2; return 1 ;;
	esac

	temp_register "$temp_made"
	TempMade="$temp_made"
}
