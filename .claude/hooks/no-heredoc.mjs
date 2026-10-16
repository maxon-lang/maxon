import { readFileSync } from "node:fs";

const input = JSON.parse(readFileSync(0, "utf8"));
const command = input.tool_input?.command ?? "";

const patterns = {
	Bash: /<<-?\s*(['"]?)[A-Za-z_]\w*\1/,
	PowerShell: /@['"][ \t]*\r?$/m,
};

const pattern = patterns[input.tool_name];

if (pattern && pattern.test(command)) {
	process.stderr.write(
		"Heredocs and here-strings are not allowed in this repository (AGENTS.md). " +
			"Write the text to a file with the Write tool, then pass the file to the command " +
			"(e.g. `git commit -F <path>`).\n",
	);
	process.exit(2);
}
