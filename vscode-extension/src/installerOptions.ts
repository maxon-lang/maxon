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

export function printRootFlag(windows: boolean): string {
	return windows ? '-PrintRoot' : '--print-root';
}

export function parsePrintedRoot(stdout: string): string {
	const lines = stdout.split(/\r?\n/).map(line => line.trim()).filter(Boolean);
	if (lines.length !== 1) {
		throw new Error(`the installer printed ${lines.length} lines where it prints one directory: ${JSON.stringify(stdout)}`);
	}
	return lines[0];
}

/**
 * The directory `website/public/install.sh` (`install_root`) and `install.ps1` (`Get-InstallRoot`)
 * install into, normalised as they normalise it, for the compiler search at activation and restart,
 * which must work offline. The install offer shows the directory the script itself prints, and this
 * one only when the script cannot be asked. A relative `MAXON_INSTALL` is returned as set: the scripts
 * refuse it, and the failure message names it.
 */
export function installRoot(env: NodeJS.ProcessEnv, home: string): string {
	const dir = env.MAXON_INSTALL || path.join(home, '.maxon');
	return path.isAbsolute(dir) ? path.resolve(dir) : dir;
}

export function installedCompilerPath(root: string, binaryName: string): string {
	return path.join(root, 'bin', binaryName);
}

export interface InstallMessages {
	prompt: string;
	progress: string;
	installed: string;
	installedWithErrors: string;
	finishedWithoutCompiler: string;
	failed: string;
}

export function installRootUnavailable(expectedRoot: string, reason: string): string {
	return `The installer could not say where it installs (${reason}). Install is expected to put Maxon in ${expectedRoot}.`;
}

export function installMessages(root: string): InstallMessages {
	return {
		prompt: `Install puts Maxon in ${root}.`,
		progress: `Installing Maxon in ${root}…`,
		installed: `Maxon installed in ${root}.`,
		installedWithErrors: `Maxon installed in ${root}, but the installer reported an error. See the Maxon Language Server output.`,
		finishedWithoutCompiler: `The Maxon installer finished, but no compiler was found in ${root}. See the Maxon Language Server output.`,
		failed: `The Maxon installer failed to install in ${root}. See the Maxon Language Server output, or install from https://maxon.dev/install.`
	};
}

export function firstRunnable(probeOutput: string, windows: boolean): string | undefined {
	const candidates = probeOutput.split(/\r?\n/).map(line => line.trim()).filter(Boolean);
	if (!windows) {
		return candidates[0];
	}
	return candidates.find(candidate => WindowsRunnableExtensions.includes(path.win32.extname(candidate).toLowerCase()));
}
