import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';
import { clearLogs } from '../../logger';

/**
 * Tests for cross-file diagnostics when stdlib files are edited.
 *
 * This tests the scenario where:
 * 1. A consumer file calls a stdlib function
 * 2. The stdlib file is edited to rename that function
 * 3. The consumer file should show an error for the now-undefined function
 */
suite('Stdlib Cross-File Diagnostics', () => {
	let consumerFileUri: vscode.Uri;
	let stdlibFileUri: vscode.Uri;
	let consumerDocument: vscode.TextDocument;
	let stdlibDocument: vscode.TextDocument;
	let originalStdlibContent: string;

	suiteSetup(async function () {
		// Ensure extension is activated
		const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
		if (ext && !ext.isActive) {
			await ext.activate();
		}
	});

	/**
	 * Helper to wait for diagnostics with a predicate
	 */
	async function waitForDiagnostics(
		uri: vscode.Uri,
		maxAttempts = 30,
		predicate?: (diags: vscode.Diagnostic[]) => boolean
	): Promise<vscode.Diagnostic[]> {
		let diagnostics: vscode.Diagnostic[] = [];
		for (let i = 0; i < maxAttempts; i++) {
			diagnostics = vscode.languages.getDiagnostics(uri);
			if (predicate ? predicate(diagnostics) : diagnostics.length > 0) {
				break;
			}
			await new Promise(resolve => setTimeout(resolve, 100)); // wait for diagnostics to update
		}
		return diagnostics;
	}

	/**
	 * Helper to wait for diagnostics to be cleared or change
	 */
	async function waitForDiagnosticsChange(
		uri: vscode.Uri,
		previousCount: number,
		maxAttempts = 30
	): Promise<vscode.Diagnostic[]> {
		let diagnostics: vscode.Diagnostic[] = [];
		for (let i = 0; i < maxAttempts; i++) {
			diagnostics = vscode.languages.getDiagnostics(uri);
			if (diagnostics.length !== previousCount) {
				break;
			}
			await new Promise(resolve => setTimeout(resolve, 100)); // wait for diagnostics to update
		}
		return diagnostics;
	}

	setup(async function () {
		this.timeout(30000);
		clearLogs();

		const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
		if (!workspaceFolder) {
			throw new Error('No workspace folder found');
		}

		// Set up the consumer file path
		const consumerPath = path.join(workspaceFolder.uri.fsPath, 'temp', 'stdlib-cross-file-test.maxon');
		consumerFileUri = vscode.Uri.file(consumerPath);

		// Set up the stdlib file path (we'll use grapheme.maxon which has findGraphemeEndManaged)
		const stdlibPath = path.join(workspaceFolder.uri.fsPath, 'stdlib', 'string', 'grapheme.maxon');
		stdlibFileUri = vscode.Uri.file(stdlibPath);

		// Read and store the original stdlib content
		const stdlibBytes = await vscode.workspace.fs.readFile(stdlibFileUri);
		originalStdlibContent = Buffer.from(stdlibBytes).toString('utf8');
	});

	teardown(async function () {
		this.timeout(30000);

		// Restore the original stdlib content if it was modified
		if (stdlibDocument && originalStdlibContent) {
			const edit = new vscode.WorkspaceEdit();
			const fullRange = new vscode.Range(
				new vscode.Position(0, 0),
				new vscode.Position(stdlibDocument.lineCount, 0)
			);
			edit.replace(stdlibFileUri, fullRange, originalStdlibContent);
			await vscode.workspace.applyEdit(edit);
			await stdlibDocument.save();
		}

		// Close all editors
		await vscode.commands.executeCommand('workbench.action.closeAllEditors');

		// Delete the consumer test file
		try {
			await vscode.workspace.fs.delete(consumerFileUri);
		} catch {
			// Ignore if file doesn't exist
		}
	});

	test('Should report error when stdlib function is renamed', async function () {
		this.timeout(60000);

		// Step 1: Create a consumer file that calls findGraphemeEndManaged
		const consumerContent = `function testGrapheme(m _ManagedString) returns int
	return findGraphemeEndManaged(m, 0, 10)
end 'testGrapheme'
`;
		await vscode.workspace.fs.writeFile(consumerFileUri, Buffer.from(consumerContent, 'utf8'));

		// Open the consumer file
		consumerDocument = await vscode.workspace.openTextDocument(consumerFileUri);
		await vscode.window.showTextDocument(consumerDocument);

		// Wait for initial analysis - should have NO errors since findGraphemeEndManaged exists
		let diagnostics = await waitForDiagnostics(consumerFileUri, 30, diags => {
			// Wait until we get diagnostics OR we're confident the file was analyzed
			// (no "Undefined function: 'findGraphemeEndManaged'" error)
			const hasUndefinedError = diags.some(d =>
				d.message.includes('Undefined function') &&
				d.message.includes('findGraphemeEndManaged')
			);
			return !hasUndefinedError;
		});

		// Verify no undefined function error initially
		const initialUndefinedError = diagnostics.find(d =>
			d.message.includes('Undefined function') &&
			d.message.includes('findGraphemeEndManaged')
		);
		assert.strictEqual(initialUndefinedError, undefined,
			`Should not have undefined function error initially. Got: ${diagnostics.map(d => d.message).join(', ')}`);

		// Step 2: Open the stdlib file
		stdlibDocument = await vscode.workspace.openTextDocument(stdlibFileUri);
		await vscode.window.showTextDocument(stdlibDocument);

		// Step 3: Rename findGraphemeEndManaged to findGraphemeEndManagedXXX in the stdlib file
		const modifiedContent = originalStdlibContent.replace(
			/\bfindGraphemeEndManaged\b(?!Range)/g,
			'findGraphemeEndManagedXXX'
		);

		const edit = new vscode.WorkspaceEdit();
		const fullRange = new vscode.Range(
			new vscode.Position(0, 0),
			new vscode.Position(stdlibDocument.lineCount, 0)
		);
		edit.replace(stdlibFileUri, fullRange, modifiedContent);
		const editApplied = await vscode.workspace.applyEdit(edit);

		// Note: We're NOT saving the file - this tests in-memory changes

		// Step 4: Check that the consumer file now has an error
		const previousCount = diagnostics.length;
		diagnostics = await waitForDiagnostics(consumerFileUri, 50, diags => {
			return diags.some(d =>
				d.message.includes('Undefined function') &&
				d.message.includes('findGraphemeEndManaged')
			);
		});

		const undefinedError = diagnostics.find(d =>
			d.message.includes('Undefined function') &&
			d.message.includes('findGraphemeEndManaged')
		);

		assert.ok(undefinedError,
			`After renaming stdlib function, consumer should report undefined function error. ` +
			`Got diagnostics: ${diagnostics.map(d => d.message).join(', ')}`);
	});

	test('Should clear error when stdlib function is renamed back', async function () {
		this.timeout(60000);

		// Step 1: Create consumer file
		const consumerContent = `function testGrapheme(m _ManagedString) returns int
	return findGraphemeEndManaged(m, 0, 10)
end 'testGrapheme'
`;
		await vscode.workspace.fs.writeFile(consumerFileUri, Buffer.from(consumerContent, 'utf8'));

		// Open consumer file
		consumerDocument = await vscode.workspace.openTextDocument(consumerFileUri);
		await vscode.window.showTextDocument(consumerDocument);

		// Open stdlib file
		stdlibDocument = await vscode.workspace.openTextDocument(stdlibFileUri);
		await vscode.window.showTextDocument(stdlibDocument);

		// Step 2: Rename the function (break it)
		const modifiedContent = originalStdlibContent.replace(
			/\bfindGraphemeEndManaged\b(?!Range)/g,
			'findGraphemeEndManagedXXX'
		);

		let edit = new vscode.WorkspaceEdit();
		let fullRange = new vscode.Range(
			new vscode.Position(0, 0),
			new vscode.Position(stdlibDocument.lineCount, 0)
		);
		edit.replace(stdlibFileUri, fullRange, modifiedContent);
		await vscode.workspace.applyEdit(edit);

		// Wait for error to appear
		let diagnostics = await waitForDiagnostics(consumerFileUri, 50, diags => {
			return diags.some(d =>
				d.message.includes('Undefined function') &&
				d.message.includes('findGraphemeEndManaged')
			);
		});

		assert.ok(diagnostics.some(d =>
			d.message.includes('Undefined function') &&
			d.message.includes('findGraphemeEndManaged')
		), 'Should have undefined function error after rename');

		// Step 3: Rename it back (fix it)
		edit = new vscode.WorkspaceEdit();
		fullRange = new vscode.Range(
			new vscode.Position(0, 0),
			new vscode.Position(stdlibDocument.lineCount, 0)
		);
		edit.replace(stdlibFileUri, fullRange, originalStdlibContent);
		await vscode.workspace.applyEdit(edit);

		// Step 4: Error should be cleared
		diagnostics = await waitForDiagnostics(consumerFileUri, 50, diags => {
			return !diags.some(d =>
				d.message.includes('Undefined function') &&
				d.message.includes('findGraphemeEndManaged')
			);
		});

		const undefinedError = diagnostics.find(d =>
			d.message.includes('Undefined function') &&
			d.message.includes('findGraphemeEndManaged')
		);

		assert.strictEqual(undefinedError, undefined,
			`After renaming stdlib function back, error should be cleared. ` +
			`Got diagnostics: ${diagnostics.map(d => d.message).join(', ')}`);
	});
});
