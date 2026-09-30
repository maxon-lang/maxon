# shellcheck shell=bash
# Every name here is prefixed `goldens_`, because a sourced file has no locals.
#
# An artifact is written by a CI runner and committed to `main` by `scripts/record-goldens.sh`, so
# every path is checked against its own target's goldens before it is staged or placed.

GOLDENS_ROOT="specs/fragments"
GOLDENS_TARGETS="x64-windows x64-linux arm64-macos arm64-linux"
GOLDENS_KINDS="new rewritten unchanged"
GOLDENS_ARTIFACT_PREFIX="goldens-"

goldens_is_target() {
	case " $GOLDENS_TARGETS " in
		*" $1 "*) return 0 ;;
	esac

	return 1
}

goldens_path_is_valid() {
	goldens_path="$1"
	goldens_expected_target="$2"

	[[ "$goldens_path" =~ ^$GOLDENS_ROOT/([a-z0-9-]+)/[A-Za-z0-9_][A-Za-z0-9._-]*/[A-Za-z0-9_][A-Za-z0-9._-]*\.test$ ]] || return 1
	goldens_is_target "${BASH_REMATCH[1]}" || return 1
	[ "${BASH_REMATCH[1]}" = "$goldens_expected_target" ]
}

goldens_changes() {
	goldens_status="$(git status --porcelain --untracked-files=all -- "$GOLDENS_ROOT")" || return 1
	[ -n "$goldens_status" ] || return 0

	goldens_refused=0

	while IFS= read -r goldens_line; do
		goldens_path="${goldens_line:3}"

		case "${goldens_line:0:2}" in
			'??'|'A ')        printf 'new %s\n' "$goldens_path" ;;
			' M'|'M '|'MM')   printf 'rewritten %s\n' "$goldens_path" ;;
			*)
				echo "goldens.sh: unexpected change under $GOLDENS_ROOT: $goldens_line" >&2
				goldens_refused=1
				;;
		esac
	done <<< "$goldens_status"

	[ "$goldens_refused" -eq 0 ]
}

goldens_stage_lane() {
	goldens_stage="$1"
	goldens_lane_target="$2"

	goldens_lane_changes="$(goldens_changes)" || return 1
	[ -n "$goldens_lane_changes" ] || return 0

	while read -r goldens_kind goldens_path; do
		if ! goldens_path_is_valid "$goldens_path" "$goldens_lane_target"; then
			echo "goldens.sh: the $goldens_lane_target lane changed a path that is not one of its goldens: $goldens_path" >&2
			return 1
		fi

		mkdir -p "$goldens_stage/$(dirname "$goldens_path")" || return 1
		cp "$goldens_path" "$goldens_stage/$goldens_path" || return 1
	done <<< "$goldens_lane_changes"

	printf '%s\n' "$goldens_lane_changes"
}

goldens_guard_artifacts() {
	goldens_artifacts="$1"
	goldens_refused=0

	for goldens_artifact in "$goldens_artifacts"/* "$goldens_artifacts"/.[!.]*; do
		[ -e "$goldens_artifact" ] || [ -L "$goldens_artifact" ] || continue

		goldens_name="$(basename "$goldens_artifact")"
		goldens_artifact_target="${goldens_name#"$GOLDENS_ARTIFACT_PREFIX"}"

		if [ -L "$goldens_artifact" ] || [ ! -d "$goldens_artifact" ] \
			|| [ "$goldens_name" = "$goldens_artifact_target" ] || ! goldens_is_target "$goldens_artifact_target"; then
			echo "goldens.sh: not a goldens artifact: $goldens_artifact" >&2
			goldens_refused=1
			continue
		fi

		goldens_entries="$(cd "$goldens_artifact" && find . -mindepth 1 ! -type d)" || return 1
		[ -n "$goldens_entries" ] || continue

		while IFS= read -r goldens_entry; do
			goldens_path="${goldens_entry#./}"

			if [ -L "$goldens_artifact/$goldens_path" ] || [ ! -f "$goldens_artifact/$goldens_path" ] \
				|| ! goldens_path_is_valid "$goldens_path" "$goldens_artifact_target"; then
				echo "goldens.sh: $goldens_name holds a file that is not one of its target's goldens: $goldens_path" >&2
				goldens_refused=1
			fi
		done <<< "$goldens_entries"
	done

	[ "$goldens_refused" -eq 0 ]
}

goldens_place() {
	goldens_artifacts="$1"
	goldens_destination="$2"

	goldens_guard_artifacts "$goldens_artifacts" || return 1

	for goldens_artifact in "$goldens_artifacts"/"$GOLDENS_ARTIFACT_PREFIX"*; do
		[ -d "$goldens_artifact" ] || continue

		goldens_entries="$(cd "$goldens_artifact" && find . -type f)" || return 1
		[ -n "$goldens_entries" ] || continue

		while IFS= read -r goldens_entry; do
			goldens_path="${goldens_entry#./}"
			goldens_target_file="$goldens_destination/$goldens_path"

			if [ ! -e "$goldens_target_file" ]; then
				goldens_kind=new
			else
				# Compared as git would stage them: `--path=` applies that path's `.gitattributes`, so a
				# golden differing only in line endings under `eol=lf` is unchanged, and
				# `record-goldens.sh` decides whether to commit from this answer.
				goldens_placed_blob="$(git hash-object --path="$goldens_path" -- "$goldens_artifact/$goldens_path")" || return 1
				goldens_held_blob="$(git hash-object --path="$goldens_path" -- "$goldens_target_file")" || return 1

				if [ "$goldens_placed_blob" = "$goldens_held_blob" ]; then
					goldens_kind=unchanged
				else
					goldens_kind=rewritten
				fi
			fi

			mkdir -p "$(dirname "$goldens_target_file")" || return 1
			cp "$goldens_artifact/$goldens_path" "$goldens_target_file" || return 1
			printf '%s %s\n' "$goldens_kind" "$goldens_path"
		done <<< "$goldens_entries"
	done
}

goldens_tally() {
	awk -v root="$GOLDENS_ROOT/" -v targets="$GOLDENS_TARGETS" -v kinds="$GOLDENS_KINDS" '
		index($2, root) == 1 {
			split(substr($2, length(root) + 1), part, "/")
			count[part[1], $1]++
		}

		END {
			target_count = split(targets, target, " ")
			kind_count = split(kinds, kind, " ")

			for (t = 1; t <= target_count; t++) {
				line = ""

				for (k = 1; k <= kind_count; k++) {
					if ((target[t], kind[k]) in count) {
						line = line (line == "" ? "" : ", ") count[target[t], kind[k]] " " kind[k]
					}
				}

				if (line != "") {
					print target[t] ": " line
				}
			}
		}'
}
