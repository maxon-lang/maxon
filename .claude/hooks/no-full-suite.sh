#!/usr/bin/env bash

input=$(cat)

refuse() {
	echo "The full suite and the tree corpora are CI's (/land: \"Nobody runs the full suite\"). Run spec-test with --filter for what your change touches, and the §8 gates once; CI runs the rest after the push." >&2
	exit 2
}

shell='"tool_name"[[:space:]]*:[[:space:]]*"(Bash|PowerShell)"'
spec_mcp='"tool_name"[[:space:]]*:[[:space:]]*"mcp__maxon__run_spec_test"'

if printf '%s' "$input" | grep -Eq "$shell"; then
	if printf '%s' "$input" | grep -Eq 'run-test-corpora\.sh'; then refuse; fi
	if printf '%s' "$input" | grep -Eq 'spec-test' && ! printf '%s' "$input" | grep -Eq -- '--filter|--update-required|--help'; then refuse; fi
fi

if printf '%s' "$input" | grep -Eq "$spec_mcp" && ! printf '%s' "$input" | grep -Eq '"filter"[[:space:]]*:[[:space:]]*("[^"]+"|\[[[:space:]]*")'; then
	refuse
fi
