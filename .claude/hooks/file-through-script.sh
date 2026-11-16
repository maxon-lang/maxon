#!/usr/bin/env bash

input=$(cat)

raw_create='"tool_name"[[:space:]]*:[[:space:]]*"(Bash|PowerShell)".*gh[[:space:]]+issue[[:space:]]+create.*maxon-lang/maxon([^-]|$)'

if printf '%s' "$input" | grep -Eq -e "$raw_create"; then
	echo "File public Maxon issues with .claude/roadmap/file.mjs (it puts each on the right board and refuses an accepted bug or design without --priority; choose the priority with the prioritize skill)." >&2
	exit 2
fi
