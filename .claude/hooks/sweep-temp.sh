#!/usr/bin/env bash

max_age_days=3
temp_dir="${CLAUDE_PROJECT_DIR:-.}/temp"

[ -d "$temp_dir" ] || exit 0

find "$temp_dir" -mindepth 1 -maxdepth 1 ! -name .maxonignore -print0 |
	while IFS= read -r -d '' entry; do
		if [ -z "$(find "$entry" -mtime -"$max_age_days" -print -quit)" ]; then
			rm -rf "$entry" || echo "sweep-temp: left $entry" >&2
		fi
	done
