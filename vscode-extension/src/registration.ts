import * as vscode from 'vscode';

export function registerAll(registrations: Iterable<() => vscode.Disposable>): vscode.Disposable {
	const made: vscode.Disposable[] = [];

	try {
		for (const register of registrations) made.push(register());
	} catch (error) {
		for (const registration of made) registration.dispose();
		throw error;
	}

	return vscode.Disposable.from(...made);
}
