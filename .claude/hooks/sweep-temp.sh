#!/usr/bin/env bash

max_age_days=3
minutes_per_day=1440
temp_names_table_path=scripts/lib/temp-names.tsv
uuid_pattern='^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'

unix_path() {
	if command -v cygpath >/dev/null 2>&1; then
		cygpath -u -- "$1"
	else
		printf '%s\n' "$1"
	fi
}

physical_dir() {
	(cd "$1" 2>/dev/null && pwd -P)
}

json_field() {
	if command -v jq >/dev/null 2>&1; then
		printf '%s' "$hook_input" | jq -r --arg key "$1" '.[$key] // empty' 2>/dev/null | tr -d '\r'
	else
		printf '%s' "$hook_input" |
			sed -n 's/.*"'"$1"'"[[:space:]]*:[[:space:]]*"\(\([^"\\]\|\\.\)*\)".*/\1/p' |
			sed 's/\\\\/\\/g' |
			head -n 1
	fi
}

system_temp_dir() {
	local candidate
	for candidate in "${TMPDIR:-}" "${TEMP:-}" "${TMP:-}"; do
		if [ -n "$candidate" ]; then
			candidate=$(unix_path "$candidate")
			if [ -d "$candidate" ]; then
				printf '%s\n' "${candidate%/}"
				return
			fi
		fi
	done
	printf '%s\n' /tmp
}

is_idle() {
	local recent
	recent=$(find "$1" -mmin -"$2" -print -quit 2>/dev/null) || {
		echo "sweep-temp: left $1: cannot read it to check its age" >&2
		return 1
	}
	[ -z "$recent" ]
}

remove_tree() {
	local err
	err=$(rm -rf -- "$1" 2>&1) || {
		echo "sweep-temp: left $1: ${err%%$'\n'*}" >&2
		return 1
	}
}

sweep_idle_children() {
	local dir=$1 minutes=$2 entry
	shift 2
	while IFS= read -r -d '' entry; do
		if is_idle "$entry" "$minutes"; then
			remove_tree "$entry"
		fi
	done < <(find "$dir" -mindepth 1 -maxdepth 1 "$@" -print0)
}

sweep_project_temp() {
	[ -d "$project_dir/temp" ] || return 0
	sweep_idle_children "$project_dir/temp" "$idle_minutes" ! -name .maxonignore
}

temp_name_rows() {
	local table="$project_dir/$temp_names_table_path"
	if [ ! -r "$table" ]; then
		echo "sweep-temp: left $system_temp: cannot read $table, the table of temp name prefixes" >&2
		return 0
	fi
	tail -n +2 "$table" | tr -d '\r'
}

sweep_system_temp() {
	local name prefix stale_minutes
	while IFS=$'\t' read -r name prefix stale_minutes; do
		if [ -z "$prefix" ] || [ -z "$stale_minutes" ]; then
			echo "sweep-temp: left $system_temp: the row '$name' of $temp_names_table_path names no prefix or no age" >&2
			continue
		fi
		sweep_idle_children "$system_temp" "$stale_minutes" -name "$prefix*"
	done < <(temp_name_rows)
}

sweep_claude_sessions() {
	local claude_dir="$system_temp/claude" session name emptied_project
	local project_dirs=()
	[ -d "$claude_dir" ] || return 0
	if [ -z "$session_id" ]; then
		echo "sweep-temp: left $claude_dir: hook input has no session_id to protect" >&2
		return 0
	fi
	while IFS= read -r -d '' session; do
		name=${session##*/}
		[[ $name =~ $uuid_pattern ]] || continue
		[ "$name" = "$session_id" ] && continue
		if is_idle "$session" "$idle_minutes" && remove_tree "$session"; then
			project_dirs+=("${session%/*}")
		fi
	done < <(find "$claude_dir" -mindepth 2 -maxdepth 2 -type d -print0)
	for emptied_project in "${project_dirs[@]}"; do
		rmdir "$emptied_project" 2>/dev/null
	done
}

holds_live_session() {
	local live
	for live in "$project_physical" "$session_cwd_physical"; do
		if [ -n "$live" ]; then
			case "$live/" in
				"$1"/*) return 0 ;;
			esac
		fi
	done
	return 1
}

remove_idle_worktree() {
	local wt=$1 physical status unpushed branch err
	if [ ! -f "$wt/.git" ]; then
		echo "sweep-temp: left $wt: not a linked git worktree" >&2
		return
	fi
	physical=$(physical_dir "$wt")
	if [ -z "$physical" ]; then
		echo "sweep-temp: left $wt: cannot enter it" >&2
		return
	fi
	if holds_live_session "$physical"; then
		echo "sweep-temp: left $wt: the current session is inside it" >&2
		return
	fi
	status=$(git --no-optional-locks -C "$wt" status --porcelain 2>&1) || {
		echo "sweep-temp: left $wt: git status failed: ${status%%$'\n'*}" >&2
		return
	}
	if [ -n "$status" ]; then
		echo "sweep-temp: left $wt: uncommitted changes" >&2
		return
	fi
	unpushed=$(git -C "$wt" rev-list HEAD --not --remotes=origin 2>&1) || {
		echo "sweep-temp: left $wt: git rev-list failed: ${unpushed%%$'\n'*}" >&2
		return
	}
	if [ -n "$unpushed" ]; then
		echo "sweep-temp: left $wt: commits not on any origin/* ref" >&2
		return
	fi
	branch=$(git -C "$wt" symbolic-ref --short -q HEAD)
	err=$(git -C "$project_dir" worktree remove "$wt" 2>&1) || {
		echo "sweep-temp: left $wt: ${err%%$'\n'*}" >&2
		return
	}
	err=$(git -C "$project_dir" worktree prune 2>&1) || {
		echo "sweep-temp: left $wt: git worktree prune failed: ${err%%$'\n'*}" >&2
		return
	}
	if [ -n "$branch" ]; then
		err=$(git -C "$project_dir" branch -d "$branch" 2>&1) || {
			echo "sweep-temp: left refs/heads/$branch: ${err%%$'\n'*}" >&2
		}
	fi
}

sweep_worktrees() {
	local root="$project_dir/.claude/worktrees" wt
	[ -d "$root" ] || return 0
	while IFS= read -r -d '' wt; do
		if is_idle "$wt" "$idle_minutes"; then
			remove_idle_worktree "$wt"
		fi
	done < <(find "$root" -mindepth 1 -maxdepth 1 -type d -print0)
}

hook_input=""
if [ ! -t 0 ]; then
	hook_input=$(cat)
fi

session_id=$(json_field session_id)
if [[ ! $session_id =~ $uuid_pattern ]]; then
	session_id=""
fi
session_cwd=$(json_field cwd)

project_dir=$(unix_path "${CLAUDE_PROJECT_DIR:-.}")
project_physical=$(physical_dir "$project_dir")
session_cwd_physical=""
if [ -n "$session_cwd" ]; then
	session_cwd_physical=$(physical_dir "$(unix_path "$session_cwd")")
fi
system_temp=$(system_temp_dir)
idle_minutes=$((max_age_days * minutes_per_day))

sweep_project_temp
sweep_system_temp
sweep_claude_sessions
sweep_worktrees

exit 0
