import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';

const isWindows = os.platform() === 'win32';

export const compilerBinaryName = isWindows ? 'maxon.exe' : 'maxon';

export const CheckoutDirectories = ['specs', 'stdlib', 'runtime', 'maxon-bin'];
const SlotDirectories = ['maxon-bin', '.maxon'];
const InstallBinDirectory = 'bin';

export type CompilerSource = 'setting' | 'checkout' | 'path' | 'install' | 'extension';

export interface CompilerChoice {
	executable: string;
	source: CompilerSource;
}

export type LookupNotice =
	| { kind: 'settingPointsAtNothing'; configured: string; }
	| { kind: 'checkoutHasNoBuild'; checkout: string; slot: string; };

export interface CompilerLookup {
	choice: CompilerChoice | undefined;
	notices: LookupNotice[];
}

export interface CompilerSearch {
	configuredPath: string;
	workspaceFolders: readonly string[];
	extensionCheckout: string;
	installRoot: string;
	binaryName: string;
	findOnPath(): Promise<string>;
	isExecutable(candidate: string): Promise<boolean>;
	isCheckout(directory: string): boolean;
}

export function slotCompilerOf(checkout: string, binaryName: string): string {
	return path.join(checkout, ...SlotDirectories, binaryName);
}

function isDirectory(candidate: string): boolean {
	try {
		return fs.statSync(candidate).isDirectory();
	} catch {
		return false;
	}
}

export function isCheckoutLayout(isDirectoryUnder: (name: string) => boolean): boolean {
	return CheckoutDirectories.every(name => isDirectoryUnder(name));
}

export function isMaxonCheckout(root: string): boolean {
	return isCheckoutLayout(name => isDirectory(path.join(root, name)));
}

// `fs.access(X_OK)` alone passes a directory, and on Windows any existing path.
export async function isExecutableFile(candidate: string): Promise<boolean> {
	try {
		const info = await fs.promises.stat(candidate);
		if (!info.isFile()) {
			return false;
		}

		if (!isWindows) {
			await fs.promises.access(candidate, fs.constants.X_OK);
		}

		return true;
	} catch {
		return false;
	}
}

// A checkout's own build outranks `PATH` because the compiler reads `stdlib/` and `runtime/` from
// beside its executable: any other compiler sweeps the checkout's stdlib as author code. The install
// directory is searched directly because a VS Code started from the dock or the Start menu does not
// see the `PATH` a shell profile sets.
export async function locateCompiler(search: CompilerSearch): Promise<CompilerLookup> {
	const notices: LookupNotice[] = [];
	const found = (executable: string, source: CompilerSource): CompilerLookup => ({ choice: { executable, source }, notices });

	if (search.configuredPath) {
		if (await search.isExecutable(search.configuredPath)) {
			return found(search.configuredPath, 'setting');
		}
		notices.push({ kind: 'settingPointsAtNothing', configured: search.configuredPath });
	}

	for (const folder of search.workspaceFolders) {
		if (!search.isCheckout(folder)) {
			continue;
		}

		const slot = slotCompilerOf(folder, search.binaryName);
		if (await search.isExecutable(slot)) {
			return found(slot, 'checkout');
		}
		notices.push({ kind: 'checkoutHasNoBuild', checkout: folder, slot });
	}

	const onPath = await search.findOnPath();
	if (onPath) {
		return found(onPath, 'path');
	}

	const installed = path.join(search.installRoot, InstallBinDirectory, search.binaryName);
	if (await search.isExecutable(installed)) {
		return found(installed, 'install');
	}

	const alongsideExtension = slotCompilerOf(search.extensionCheckout, search.binaryName);
	if (await search.isExecutable(alongsideExtension)) {
		return found(alongsideExtension, 'extension');
	}

	return { choice: undefined, notices };
}

export function compilerToRestartOn(current: string, lookup: CompilerLookup): string {
	return lookup.choice ? lookup.choice.executable : current;
}

export function watchedCompilers(
	chosen: string,
	workspaceFolders: readonly string[],
	binaryName: string,
	isCheckout: (directory: string) => boolean
): string[] {
	const slots = workspaceFolders.filter(folder => isCheckout(folder)).map(folder => slotCompilerOf(folder, binaryName));

	return [...new Set([chosen, ...slots])];
}
