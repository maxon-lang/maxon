import * as path from 'path';

export const ClaudePluginName = 'maxon-lsp';

export type ClaudePluginChoice = 'install' | 'skip';

const WindowsRunnableExtensions = ['.exe', '.cmd', '.bat', '.com'];

export function claudePluginFlag(windows: boolean, choice: ClaudePluginChoice): string {
	switch (choice) {
		case 'install':
			return windows ? '-ClaudePlugin' : '--claude-plugin';
		case 'skip':
			return windows ? '-NoClaudePlugin' : '--no-claude-plugin';
		default:
			throw new Error(`claudePluginFlag: unhandled choice ${JSON.stringify(choice)}`);
	}
}

export function firstRunnable(probeOutput: string, windows: boolean): string | undefined {
	const candidates = probeOutput.split(/\r?\n/).map(line => line.trim()).filter(Boolean);
	if (!windows) {
		return candidates[0];
	}
	return candidates.find(candidate => WindowsRunnableExtensions.includes(path.win32.extname(candidate).toLowerCase()));
}
