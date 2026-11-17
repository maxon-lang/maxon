ScaleLogPath="docs/optimization-log.md"
ScaleTables="allocations bytes cpu"
ScaleLogAbsent=1
ScaleLogUnreadable=2

ScaleTablesAwk='
	function trim(text) {
		sub(/^[ \t]+/, "", text)
		sub(/[ \t]+$/, "", text)
		return text
	}

	function load(path, version,    line, count) {
		count = 0

		if (path != "") {
			while ((getline line < path) > 0) {
				original[version, ++count] = line
				sub(/\r$/, "", line)
				text[version, count] = line
			}

			close(path)
		}

		lineCount[version] = count
	}

	function readTable(version, table,    marker, at, start, i, heading, cells, columns, row, j, k, name) {
		marker = "<!-- scale-history:" table " -->"
		at = 0

		for (i = 1; i <= lineCount[version]; i++) {
			if (trim(text[version, i]) == marker) {
				at = i
				break
			}
		}

		rowCount[version, table] = 0
		hasCells[version, table] = 0
		firstRowAt[version, table] = 0
		markerAt[version, table] = 0

		if (at == 0) {
			return 0
		}

		start = at

		while (start > 1 && substr(text[version, start - 1], 1, 1) == "|") {
			start--
		}

		if (at - start < 2) {
			return 0
		}

		firstRowAt[version, table] = start + 2
		markerAt[version, table] = at

		columns = split(text[version, start], heading, "|")
		corpusAt[version, table] = 0
		treeAt[version, table] = 0

		for (j = 1; j <= columns; j++) {
			name = trim(heading[j])
			column[version, table, name] = j

			if (name == "corpus") {
				corpusAt[version, table] = j
			} else if (name == "tree") {
				treeAt[version, table] = j
			}
		}

		hasCells[version, table] = corpusAt[version, table] && treeAt[version, table]

		for (i = start + 2; i < at; i++) {
			row = ++rowCount[version, table]
			rowLine[version, table, row] = text[version, i]
			cellCount[version, table, row] = split(text[version, i], cells, "|")

			for (j = 1; j <= cellCount[version, table, row]; j++) {
				raw[version, table, row, j] = trim(cells[j])
			}

			for (k = 1; k <= keyCount; k++) {
				j = column[version, table, keyName[k]]
				cell[version, table, row, keyName[k]] = j ? raw[version, table, row, j] : ""
			}
		}

		return 1
	}
'

scale_table_rows() {
	ScaleLog="$1" ScaleTables="$ScaleTables" awk "$ScaleTablesAwk"'
		BEGIN {
			load(ENVIRON["ScaleLog"], "log")
			tableCount = split(ENVIRON["ScaleTables"], tableNames, " ")

			for (t = 1; t <= tableCount; t++) {
				readTable("log", tableNames[t])

				for (r = 1; r <= rowCount["log", tableNames[t]]; r++) {
					print tableNames[t] "\t" rowLine["log", tableNames[t], r]
				}
			}
		}
	'
}

scale_log_at() {
	local commit="$1" path="$2"

	: > "$path"
	git rev-parse --verify --quiet "$commit^{tree}" > /dev/null || return "$ScaleLogUnreadable"
	git cat-file -e "$commit:$ScaleLogPath" 2> /dev/null || return "$ScaleLogAbsent"
	git show "$commit:$ScaleLogPath" > "$path" || return "$ScaleLogUnreadable"
}

scale_rows_appended() {
	OldLog="$1" NewLog="$2" ScaleTables="$ScaleTables" awk "$ScaleTablesAwk"'
		BEGIN {
			load(ENVIRON["OldLog"], "old")
			load(ENVIRON["NewLog"], "new")
			tableCount = split(ENVIRON["ScaleTables"], tableNames, " ")

			for (t = 1; t <= tableCount; t++) {
				table = tableNames[t]
				readTable("old", table)
				readTable("new", table)
				kept = rowCount["old", table]

				if (rowCount["new", table] < kept) {
					exit 1
				}

				for (r = 1; r <= kept; r++) {
					if (rowLine["old", table, r] != rowLine["new", table, r]) {
						exit 1
					}
				}

				appended[table] = rowCount["new", table] - kept

				for (i = firstRowAt["new", table] + kept; i < markerAt["new", table]; i++) {
					skipped[i] = 1
				}
			}

			o = 1

			for (i = 1; i <= lineCount["new"]; i++) {
				if (i in skipped) {
					continue
				}

				if (o > lineCount["old"] || original["new", i] != original["old", o]) {
					exit 1
				}

				o++
			}

			if (o <= lineCount["old"]) {
				exit 1
			}

			for (t = 1; t <= tableCount; t++) {
				print tableNames[t] "\t" appended[tableNames[t]]
			}
		}
	'
}
