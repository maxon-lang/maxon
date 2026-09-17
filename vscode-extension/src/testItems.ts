import * as vscode from 'vscode';

export function childList(item: vscode.TestItem): vscode.TestItem[] {
	const out: vscode.TestItem[] = [];
	item.children.forEach(c => out.push(c));
	return out;
}

/** One line of a child's output in the run log, which wants `\r\n` whatever the child wrote. */
export function appendRunOutput(run: vscode.TestRun, line: string): void {
	run.appendOutput(line.replace(/\r?\n?$/, '') + '\r\n');
}

export function pipeLines(stream: NodeJS.ReadableStream, onLine: (line: string) => void): void {
	let buffer = '';
	stream.setEncoding('utf8');
	stream.on('data', (chunk: string) => {
		buffer += chunk;
		let nl: number;
		while ((nl = buffer.indexOf('\n')) !== -1) {
			const line = buffer.slice(0, nl);
			buffer = buffer.slice(nl + 1);
			onLine(line);
		}
	});
	stream.on('end', () => {
		if (buffer.length > 0) onLine(buffer);
	});
}
