#!/usr/bin/env bash
#
# The scale-ladder row check: every row a commit adds to `docs/optimization-log.md`'s three trend tables is
# the LAST row of its table and carries that commit's tree digest. `ci.yml` runs it on each commit of a push
# where it builds a compiler, and `scale-rows.yml` runs the history half alone on each commit of every push
# to `main`.
#
# The half that is a fact about history lives here: which rows a commit added. Each table is read as the
# contiguous `|` lines directly above its `<!-- scale-history:… -->` marker, and its data rows are compared
# with the parent's as sequences. The parent's rows must survive, in order, as a subsequence; the rest are
# the added ones. The log is `merge=union`, so a rebase can leave an added row above another's, and that is
# caught here. When exactly one side's heading has the `corpus` and `tree` cells, those cells are left out
# of the comparison and must read `*(unrecorded)*` on the rows that survive: that is the mechanical format
# migration. The three tables must gain the same number of rows, agreeing on date, minted at, corpus, tree
# and change, and every added row carries the same tree cell as the last one.
#
# The half that is a property of a tree is the compiler's: `maxon scale-test --check-row`, run in that
# tree, exits 0 only when the last row carries the tree's digest. `--history-only` skips it, and so needs
# no compiler.
#
# Usage:
#   scripts/check-scale-rows.sh --working-tree
#       the working tree's log against HEAD's, `--check-row` run in it with the slot compiler
#       `maxon-bin/.maxon/maxon`
#   scripts/check-scale-rows.sh --working-tree --history-only
#       the same log, history checks only; no compiler is used
#   scripts/check-scale-rows.sh --range <base>..<head> --compiler <path>
#       each commit of `git rev-list <base>..<head>` against its parent, `--check-row` run in a temporary
#       worktree of that commit; a merge commit is named and not judged
#   scripts/check-scale-rows.sh --range <base>..<head> --history-only
#       the same commits, history checks only; `--compiler` is refused
#
# Exit: 0 every check passes · 1 a check failed · 2 the check could not run (usage, a missing compiler or
# commit)

set -euo pipefail

ExitPassed=0
ExitFailed=1
ExitCannotRun=2

Script="check-scale-rows.sh"
RowFix="drop the hand-made row and carry its note as a \`Scale-Note:\` trailer in the commit's final paragraph: land.yml mints the row on the merged tree, and a row is not minted by hand"
EditFix="a committed row is never edited or removed: restore it, and record a correction as a new row, carried as a \`Scale-Note:\` trailer that land.yml mints"
Unrecorded="*(unrecorded)*"
ChangeTextWidth=60

caller_dir="$PWD"
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
# shellcheck source=lib/host-binaries.sh
. scripts/lib/host-binaries.sh || exit "$ExitCannotRun"
# shellcheck source=lib/system-temp.sh
. scripts/lib/system-temp.sh || exit "$ExitCannotRun"
# shellcheck source=lib/scale-tables.sh
. scripts/lib/scale-tables.sh || exit "$ExitCannotRun"

usage() {
	printf 'usage: %s --working-tree [--history-only]\n       %s --range <base>..<head> --compiler <path>\n       %s --range <base>..<head> --history-only\n' "$Script" "$Script" "$Script" >&2
	exit "$ExitCannotRun"
}

cannot_run() {
	printf '%s: %s\n' "$Script" "$1" >&2
	exit "$ExitCannotRun"
}

mode=""
range=""
compiler=""
history_only=""

while [ "$#" -gt 0 ]; do
	case "$1" in
		--working-tree) [ -z "$mode" ] || usage; mode="working-tree" ;;
		--range) [ -z "$mode" ] && [ "$#" -ge 2 ] || usage; mode="range"; range="$2"; shift ;;
		--compiler) [ -z "$compiler" ] && [ "$#" -ge 2 ] || usage; compiler="$2"; shift ;;
		--history-only) [ -z "$history_only" ] || usage; history_only="yes" ;;
		*) usage ;;
	esac
	shift
done

case "$mode" in
	working-tree) [ -z "$compiler" ] || usage; [ -n "$history_only" ] || compiler="$repo_root/$(maxon_compiler_path .)" ;;
	range)
		if [ -n "$history_only" ]; then
			[ -z "$compiler" ] || usage
		else
			[ -n "$compiler" ] || usage
		fi
		;;
	*) usage ;;
esac

if [ -z "$history_only" ]; then
	case "$compiler" in
		/*|[A-Za-z]:*) ;;
		*) compiler="$caller_dir/$compiler" ;;
	esac

	[ -x "$compiler" ] || cannot_run "no compiler at $compiler — build one with \`./maxon-bin/.maxon/maxon run build\` at the repo root"
fi

temp_make dir check-scale-rows || cannot_run "cannot make a temporary directory"
scratch="$TempMade"
worktree=""

remove_worktree() {
	if [ -n "$worktree" ]; then
		git worktree remove --force "$worktree" >/dev/null 2>&1 || rm -rf "$worktree"
		git worktree prune >/dev/null 2>&1 || true
		worktree=""
	fi
}

on_exit() {
	remove_worktree
	temp_remove_all
}

trap on_exit EXIT

log_present_at() {
	local status=0

	scale_log_at "$1" "$2" || status=$?

	case "$status" in
		0) return 0 ;;
		"$ScaleLogAbsent") return 1 ;;
		*) cannot_run "cannot read $ScaleLogPath at $1" ;;
	esac
}

# Reads $1 (the parent's log, or an empty path when there is none) and $2 (the log under check). Prints
# each defect it finds and exits 1, or writes the number of rows each table gained to $3.
judge_rows() {
	OldLog="$1" NewLog="$2" CountFile="$3" Subject="$subject" LogPath="$ScaleLogPath" Tables="$ScaleTables" RowFix="$RowFix" \
	EditFix="$EditFix" Unrecorded="$Unrecorded" ChangeTextWidth="$ChangeTextWidth" awk "$ScaleTablesAwk"'
		function report(table, message, fix) {
			print subject ": the " table " table: " message
			print "  Fix: " fix
			failed = 1
		}

		function shown(version, table, row,    change, cut) {
			change = cell[version, table, row, "change"]

			if (length(change) > width) {
				cut = substr(change, 1, width)
				sub(/ [^ ]*$/, "", cut)
				change = cut "…"
			}

			return cell[version, table, row, "date"] " · " change
		}

		function normalised(version, table, row, dropping,    j, key) {
			key = ""

			for (j = 1; j <= cellCount[version, table, row]; j++) {
				if (dropping && (j == corpusAt[version, table] || j == treeAt[version, table])) {
					continue
				}

				key = key SUBSEP raw[version, table, row, j]
			}

			return key
		}

		function judgeTable(table,    dropOld, dropNew, o, q, p, oldKey, matched, newCount, altered, last, firstAdded) {
			if (!readTable("new", table)) {
				print subject ": " logPath " has no table above its <!-- scale-history:" table " --> marker"
				failed = 1
				return
			}

			readTable("old", table)
			dropOld = hasCells["old", table] && !hasCells["new", table]
			dropNew = hasCells["new", table] && !hasCells["old", table]
			newCount = rowCount["new", table]

			for (q = 1; q <= newCount; q++) {
				newKey[q] = normalised("new", table, q, dropNew)
				matched[q] = 0
			}

			p = 1

			for (o = 1; o <= rowCount["old", table]; o++) {
				oldKey = normalised("old", table, o, dropOld)
				q = p

				while (q <= newCount && newKey[q] != oldKey) {
					q++
				}

				if (q > newCount) {
					lost[++lostCount] = o
					continue
				}

				matched[q] = 1
				p = q + 1

				if (dropNew && (raw["new", table, q, corpusAt["new", table]] != unrecorded || raw["new", table, q, treeAt["new", table]] != unrecorded)) {
					report(table, "the existing row " shown("new", table, q) " gained corpus or tree cells other than " unrecorded " in the format migration", editFix)
				}
			}

			for (o = 1; o <= lostCount; o++) {
				altered = 0

				for (q = 1; q <= newCount; q++) {
					if (!matched[q] && cell["new", table, q, "date"] == cell["old", table, lost[o], "date"] && cell["new", table, q, "change"] == cell["old", table, lost[o], "change"]) {
						matched[q] = 1
						altered = 1
						break
					}
				}

				report(table, "the existing row " shown("old", table, lost[o]) (altered ? " was altered" : " was removed"), editFix)
			}

			lostCount = 0
			added[table] = 0

			for (q = 1; q <= newCount; q++) {
				if (!matched[q]) {
					addedRow[table, ++added[table]] = q
				}
			}

			firstAdded = newCount - added[table] + 1

			for (o = 1; o <= added[table]; o++) {
				q = addedRow[table, o]

				if (q < firstAdded) {
					report(table, "the added row " shown("new", table, q) " is not last: an existing row follows it", rowFix)
				}
			}

			last = newCount

			for (o = 1; o < added[table]; o++) {
				q = addedRow[table, o]

				if (cell["new", table, q, "tree"] != cell["new", table, last, "tree"]) {
					report(table, "the added row " shown("new", table, q) " carries tree " cell["new", table, q, "tree"] " but the last row carries " cell["new", table, last, "tree"] ": every row a commit adds carries that commit\047s tree digest", rowFix)
				}
			}
		}

		BEGIN {
			subject = ENVIRON["Subject"]
			rowFix = ENVIRON["RowFix"]
			editFix = ENVIRON["EditFix"]
			unrecorded = ENVIRON["Unrecorded"]
			width = ENVIRON["ChangeTextWidth"] + 0
			logPath = ENVIRON["LogPath"]
			keyCount = split("date|minted at|corpus|tree|change", keyName, "|")
			tableCount = split(ENVIRON["Tables"], tableNames, " ")

			load(ENVIRON["OldLog"], "old")
			load(ENVIRON["NewLog"], "new")

			for (t = 1; t <= tableCount; t++) {
				judgeTable(tableNames[t])
			}

			if (failed) {
				exit 1
			}

			first = tableNames[1]

			for (t = 2; t <= tableCount; t++) {
				if (added[tableNames[t]] != added[first]) {
					print subject ": the " first " table gained " added[first] " row(s) and the " tableNames[t] " table " added[tableNames[t]] ": one run adds one row to each of the three tables"
					print "  Fix: " rowFix
					exit 1
				}
			}

			for (r = 1; r <= added[first]; r++) {
				for (t = 2; t <= tableCount; t++) {
					for (k = 1; k <= keyCount; k++) {
						a = cell["new", first, addedRow[first, r], keyName[k]]
						b = cell["new", tableNames[t], addedRow[tableNames[t], r], keyName[k]]

						if (a != b) {
							report(tableNames[t], "the added row " shown("new", tableNames[t], addedRow[tableNames[t], r]) " has " keyName[k] " \"" b "\" where the " first " table has \"" a "\": one run writes the same five leading cells to all three tables", rowFix)
						}
					}
				}
			}

			if (failed) {
				exit 1
			}

			print added[first] > ENVIRON["CountFile"]
			last = rowCount["new", first]
			print shown("new", first, last) > ENVIRON["CountFile"]
		}
	'
}

# Judges $subject: the parent's log in $1 (empty when there is none) against the log under check in $2. When
# rows were added and `--history-only` was not given, `--check-row` runs in the repository root, or, when $3
# names a commit, in a temporary worktree of it. Returns 0 when it passes, 1 when it fails.
check_subject() {
	local count_file="$scratch/count"
	local added last_row tree output status

	rm -f "$count_file"

	if ! judge_rows "$1" "$2" "$count_file"; then
		return 1
	fi

	added="$(sed -n 1p "$count_file")"
	last_row="$(sed -n 2p "$count_file")"

	if [ "$added" = "0" ]; then
		printf '%s: adds no row\n' "$subject"
		return 0
	fi

	if [ -n "$history_only" ]; then
		printf "%s: adds %s row(s) to each table, last (%s); history checked, the tree digest is checked where a compiler is built\n" "$subject" "$added" "$last_row"
		return 0
	fi

	tree="$repo_root"

	if [ -n "$3" ]; then
		worktree="$scratch/tree"
		git worktree add --detach "$worktree" "$3" >/dev/null 2>&1 || cannot_run "cannot check out $3 into a temporary worktree"
		tree="$worktree"
	fi

	status=0
	output="$(cd "$tree" && "$compiler" scale-test --check-row 2>&1)" || status=$?
	remove_worktree

	if [ "$status" -ne 0 ]; then
		printf "%s: the last row of each table (%s) does not carry this tree's digest: \`maxon scale-test --check-row\` exited %s\n" "$subject" "$last_row" "$status"
		printf '%s\n' "$output" | sed 's/^/  | /'
		printf '  Fix: %s\n' "$RowFix"
		return 1
	fi

	printf "%s: adds %s row(s) to each table, last, and the last (%s) carries this tree's digest\n" "$subject" "$added" "$last_row"
	return 0
}

if [ "$mode" = "working-tree" ]; then
	subject="the working tree"
	old_log="$scratch/old.md"

	if ! log_present_at HEAD "$old_log"; then
		old_log=""
	fi

	[ -f "$ScaleLogPath" ] || cannot_run "$ScaleLogPath is not in the working tree"

	if check_subject "$old_log" "$ScaleLogPath" ""; then
		exit "$ExitPassed"
	fi

	exit "$ExitFailed"
fi

case "$range" in
	?*..?*) ;;
	*) usage ;;
esac

base="${range%%..*}"
head="${range#*..}"

git rev-parse --verify --quiet "$base^{commit}" >/dev/null || cannot_run "$base is not a commit in this clone (fetch deeper)"
git rev-parse --verify --quiet "$head^{commit}" >/dev/null || cannot_run "$head is not a commit in this clone"
commits="$(git rev-list --reverse "$base..$head")" || cannot_run "cannot list $range"

if [ -z "$commits" ]; then
	printf '%s: %s holds no commit\n' "$Script" "$range"
	exit "$ExitPassed"
fi

failures=0

for commit in $commits; do
	subject="commit $(git log -1 --format='%h (%s)' "$commit" | cut -c1-80)"
	parents="$(git rev-list --parents -n 1 "$commit")"
	set -- $parents
	shift

	if [ "$#" -gt 1 ]; then
		printf '%s: a merge, not judged: each commit it brings is judged on its own\n' "$subject"
		continue
	fi

	old_log=""

	if [ "$#" -eq 1 ]; then
		if git diff --quiet "$1" "$commit" -- "$ScaleLogPath"; then
			printf '%s: adds no row\n' "$subject"
			continue
		fi

		old_log="$scratch/old.md"
		log_present_at "$1" "$old_log" || old_log=""
	fi

	new_log="$scratch/new.md"

	if ! log_present_at "$commit" "$new_log"; then
		if [ -z "$old_log" ]; then
			printf '%s: adds no row\n' "$subject"
			continue
		fi

		printf '%s: removes %s\n' "$subject" "$ScaleLogPath"
		failures=$((failures + 1))
		continue
	fi

	if ! check_subject "$old_log" "$new_log" "$commit"; then
		failures=$((failures + 1))
	fi
done

if [ "$failures" -ne 0 ]; then
	printf '%s: %s commit(s) of %s failed\n' "$Script" "$failures" "$range"
	exit "$ExitFailed"
fi

exit "$ExitPassed"
