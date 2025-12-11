import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';
import { clearLogs } from '../../logger';

suite('Stdlib Sibling Method Call Tests', () => {
	let stdlibFileUri: vscode.Uri;
	let document: vscode.TextDocument;

	suiteSetup(async function () {
		// Ensure extension is activated once for the entire suite
		const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
		if (ext && !ext.isActive) {
			await ext.activate();
		}
	});

	/**
	 * Helper to wait for diagnostics to stabilize
	 */
	async function waitForDiagnostics(
		uri: vscode.Uri,
		maxAttempts = 30,
		predicate?: (diags: vscode.Diagnostic[]) => boolean
	): Promise<vscode.Diagnostic[]> {
		let diagnostics: vscode.Diagnostic[] = [];
		for (let i = 0; i < maxAttempts; i++) {
			diagnostics = vscode.languages.getDiagnostics(uri);
			if (predicate ? predicate(diagnostics) : true) {
				break;
			}
			await new Promise(resolve => setTimeout(resolve, 100)); // wait for diagnostics to update
		}
		return diagnostics;
	}

	setup(async () => {
		clearLogs();

		// Get the path to stdlib/string/string.maxon
		const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
		if (!workspaceFolder) {
			throw new Error('No workspace folder found');
		}

		const stringFilePath = path.join(workspaceFolder.uri.fsPath, 'stdlib', 'string', 'string.maxon');
		stdlibFileUri = vscode.Uri.file(stringFilePath);
	});

	teardown(async () => {
		if (document) {
			await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
		}
	});

	test('Should not report argument count error for sibling method call in contains()', async function () {
		// Open the actual stdlib string.maxon file
		document = await vscode.workspace.openTextDocument(stdlibFileUri);
		await vscode.window.showTextDocument(document);

		// Wait for LSP to analyze the file
		const diagnostics = await waitForDiagnostics(stdlibFileUri, 30);

		// Filter for the specific error we're looking for:
		// "Function 'find' argument count mismatch - Expected: 2 arguments, Found: 1 argument"
		const findArgCountError = diagnostics.find(d =>
			d.message.includes('find') &&
			d.message.includes('argument') &&
			d.message.includes('mismatch')
		);

		// This is the bug: calling find(needle) inside contains() should NOT
		// report an argument count error because find() is a sibling method
		// and should implicitly receive 'self' as the first argument
		assert.strictEqual(
			findArgCountError,
			undefined,
			`Should NOT report argument count error for sibling method call 'find(needle)' in contains(). ` +
			`The call to find() inside the string struct should implicitly use self. ` +
			`Got error: ${findArgCountError?.message ?? 'none'}`
		);
	});

	test('Should not report argument count error for any sibling method calls in string.maxon', async function () {
		// Open the actual stdlib string.maxon file
		document = await vscode.workspace.openTextDocument(stdlibFileUri);
		await vscode.window.showTextDocument(document);

		// Wait for LSP to analyze the file
		const diagnostics = await waitForDiagnostics(stdlibFileUri, 30);

		// Check for any argument count mismatch errors
		const argCountErrors = diagnostics.filter(d =>
			d.message.includes('argument') &&
			d.message.includes('mismatch') &&
			d.severity === vscode.DiagnosticSeverity.Error
		);

		// There should be no argument count errors in the stdlib string file
		assert.strictEqual(
			argCountErrors.length,
			0,
			`Should not have any argument count mismatch errors in stdlib string.maxon. ` +
			`Got ${argCountErrors.length} errors: ${argCountErrors.map(d => `Line ${d.range.start.line + 1}: ${d.message}`).join(', ')}`
		);
	});
});
