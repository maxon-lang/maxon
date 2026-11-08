import * as fs from 'fs';
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

/**
 * The directory `website/public/install.sh` (`install_root`) and `install.ps1` (`Get-InstallRoot`)
 * install into, normalised as they normalise it so the directory shown is the one installed into. A
 * relative `MAXON_INSTALL` is returned as set: the scripts refuse it, and the failure message names it.
 */
export function installRoot(env: NodeJS.ProcessEnv, home: string): string {
	const dir = env.MAXON_INSTALL || path.join(home, '.maxon');
	return path.isAbsolute(dir) ? path.resolve(dir) : dir;
}

export function installBinDirectory(root: string): string {
	return path.join(root, 'bin');
}

export function isInstalledIn(executable: string, root: string): boolean {
	const bin = installBinDirectory(root);
	if (!fs.existsSync(bin) || !fs.existsSync(executable)) {
		return false;
	}
	return path.dirname(fs.realpathSync.native(executable)) === fs.realpathSync.native(bin);
}

export interface InstallMessages {
	prompt: string;
	progress: string;
	installed: string;
	finishedWithoutCompiler: string;
	failed: string;
}

export function installMessages(root: string): InstallMessages {
	return {
		prompt: `Install puts Maxon in ${root}.`,
		progress: `Installing Maxon in ${root}…`,
		installed: `Maxon installed in ${root}.`,
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
