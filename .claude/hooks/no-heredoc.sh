#!/usr/bin/env bash

input=$(cat)

bash_heredoc='"tool_name"[[:space:]]*:[[:space:]]*"Bash".*<<-?[[:space:]]*(\\?["'"'"'])?[A-Za-z_]'
powershell_here_string='"tool_name"[[:space:]]*:[[:space:]]*"PowerShell".*@(\\"|'"'"')[ \t]*(\\r)?\\n'

if printf '%s' "$input" | grep -Eq -e "$bash_heredoc" -e "$powershell_here_string"; then
	echo "Heredocs and here-strings are not allowed in this repository (AGENTS.md). Write the text to a file with the Write tool, then pass the file to the command (e.g. \`git commit -F <path>\`)." >&2
	exit 2
fi
