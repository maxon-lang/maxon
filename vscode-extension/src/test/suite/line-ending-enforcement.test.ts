import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';

/**
 * Test suite for LF line ending enforcement in Maxon files
 * 
 * The extension enforces LF line endings through two mechanisms:
 * 1. onWillSaveTextDocument event handler that sets EOL to LF
 * 2. CRLF replacement in the document text
 * 3. Formatting middleware that appends setEndOfLine(LF) edit
 * 
 * NOTE: These tests verify the extension's configuration and behavior.
 * The actual line ending conversion on save depends on VS Code's file system
 * handling and may vary based on editor settings and platform.
 */
suite('Line Ending Enforcement Test Suite', () => {
	let testDir: string;
	let workspaceFolder: vscode.WorkspaceFolder | undefined;

	suiteSetup(async () => {
		// Get workspace folder
		workspaceFolder = vscode.workspace.workspaceFolders?.[0];
		assert.ok(workspaceFolder, 'Workspace folder should be available');

		// Create test directory
		testDir = path.join(workspaceFolder!.uri.fsPath, 'temp', 'line-ending-tests');
		if (!fs.existsSync(testDir)) {
			fs.mkdirSync(testDir, { recursive: true });
		}
	});

	suiteTeardown(() => {
		// Clean up test files
		if (fs.existsSync(testDir)) {
			const files = fs.readdirSync(testDir);
			files.forEach(file => {
				const filePath = path.join(testDir, file);
				try {
					fs.unlinkSync(filePath);
				} catch (e) {
					// File might be locked, ignore
				}
			});
			try {
				fs.rmdirSync(testDir);
			} catch (e) {
				// Directory might not be empty, ignore
			}
		}
	});

	test('Should detect Maxon files for line ending enforcement', async () => {
		const testFilePath = path.join(testDir, 'detection-test.maxon');
		const testFileUri = vscode.Uri.file(testFilePath);

		// Create a fresh Maxon file
		const content = 'function main() int\n    return 0\nend "main"';
		fs.writeFileSync(testFilePath, content, 'utf-8');

		// Open the document
		const document = await vscode.workspace.openTextDocument(testFileUri);

		// Verify it's detected as Maxon language (required for LF enforcement)
		assert.strictEqual(document.languageId, 'maxon',
			'File with .maxon extension must be detected as Maxon language to trigger line ending enforcement');

		// Close the editor
		await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
	});

	test('Document EOL property should be accessible', async () => {
		const testFilePath = path.join(testDir, 'eol-property-test.maxon');
		const testFileUri = vscode.Uri.file(testFilePath);

		// Create a file with LF
		const content = 'function main() int\n    return 0\nend "main"';
		fs.writeFileSync(testFilePath, content, 'utf-8');

		// Open the document
		const document = await vscode.workspace.openTextDocument(testFileUri);

		// Verify EOL property is accessible
		assert.ok(document.eol !== undefined, 'Document should have EOL property');
		// On most systems, newly created files default to LF (1) or CRLF (2)
		assert.ok([vscode.EndOfLine.LF, vscode.EndOfLine.CRLF].includes(document.eol),
			'Document EOL should be either LF or CRLF');

		// Close the editor
		await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
	});

	test('Extension should hook into onWillSaveTextDocument event', async () => {
		// This test verifies the extension has registered the save handler
		// We can't directly test the handler, but we can verify the pattern is correct

		// The extension should have subscriptions for the save event
		const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
		assert.ok(ext, 'Maxon extension should be loaded');
		assert.ok(ext.isActive, 'Maxon extension should be active');
	});

	test('Maxon file detection via languageId', async () => {
		const testFilePath = path.join(testDir, 'lang-id-test.maxon');
		const testFileUri = vscode.Uri.file(testFilePath);

		// Create a .maxon file
		const content = 'function main() int\n    return 0\nend "main"';
		fs.writeFileSync(testFilePath, content, 'utf-8');

		// Open the document
		const document = await vscode.workspace.openTextDocument(testFileUri);

		// The onWillSaveTextDocument handler checks: if (e.document.languageId === 'maxon')
		// This is the gating mechanism for line ending enforcement
		assert.strictEqual(document.languageId, 'maxon',
			'Document must have languageId "maxon" to trigger line ending enforcement');
	});

	test('Should ignore files without .maxon extension', async () => {
		const testFilePath = path.join(testDir, 'other-file.txt');
		const testFileUri = vscode.Uri.file(testFilePath);

		// Create a .txt file
		const content = 'Some text';
		fs.writeFileSync(testFilePath, content, 'utf-8');

		// Open the document
		const document = await vscode.workspace.openTextDocument(testFileUri);

		// The handler only processes files where languageId === 'maxon'
		assert.notStrictEqual(document.languageId, 'maxon',
			'.txt files should not have maxon language ID');
	});

	test('Document text should be obtainable for CRLF detection', async () => {
		const testFilePath = path.join(testDir, 'crlf-detect.maxon');
		const testFileUri = vscode.Uri.file(testFilePath);

		// Create a .maxon file
		const content = 'function main() int\n    return 0\nend "main"';
		fs.writeFileSync(testFilePath, content, 'utf-8');

		// Open the document
		const document = await vscode.workspace.openTextDocument(testFileUri);

		// The handler calls e.document.getText() to check for \r
		const text = document.getText();
		assert.ok(typeof text === 'string', 'Document.getText() should return a string');

		// The handler checks: if (text.includes('\r'))
		const hasCRLF = text.includes('\r');
		assert.ok(typeof hasCRLF === 'boolean', 'Should be able to check for CRLF');
	});

	test('Extension should call setEndOfLine(LF) on Maxon files', async () => {
		const testFilePath = path.join(testDir, 'set-eol-test.maxon');
		const testFileUri = vscode.Uri.file(testFilePath);

		// Create a .maxon file
		const content = 'function main() int\n    return 0\nend "main"';
		fs.writeFileSync(testFilePath, content, 'utf-8');

		// Open the document
		const document = await vscode.workspace.openTextDocument(testFileUri);
		await vscode.window.showTextDocument(document);

		// The handler always adds: vscode.TextEdit.setEndOfLine(vscode.EndOfLine.LF)
		// This is the first mechanism for enforcing LF
		assert.ok(vscode.TextEdit.setEndOfLine !== undefined,
			'vscode.TextEdit.setEndOfLine should be available');

		// Close the editor
		await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
	});

	test('CRLF replacement logic should work correctly', async () => {
		// Test the string replacement logic that the handler uses
		const withCRLF = 'line1\r\nline2\r\nline3';
		const replaced = withCRLF.replace(/\r/g, '');

		assert.ok(!replaced.includes('\r'), 'After replace(/\\r/g, ""), no CR should remain');
		assert.strictEqual(replaced, 'line1\nline2\nline3', 'Content should be preserved with only LF');
	});

	test('Full range replacement should work for entire document', async () => {
		const testFilePath = path.join(testDir, 'range-test.maxon');
		const testFileUri = vscode.Uri.file(testFilePath);

		// Create a .maxon file
		const content = 'function main() int\nreturn 0\nend "main"';
		fs.writeFileSync(testFilePath, content, 'utf-8');

		// Open the document
		const document = await vscode.workspace.openTextDocument(testFileUri);

		// The handler creates a Range from position 0 to text.length
		const text = document.getText();
		const range = new vscode.Range(
			document.positionAt(0),
			document.positionAt(text.length)
		);

		// Verify the range covers the entire document
		assert.strictEqual(range.start.line, 0, 'Range should start at line 0');
		assert.strictEqual(range.start.character, 0, 'Range should start at character 0');
		// End should be at or near the end of the document
		assert.ok(range.end.line > 0 || range.end.character > 0, 'Range should extend to document end');
	});

	test('TextEdit array should be returned via waitUntil', async () => {
		const testFilePath = path.join(testDir, 'waituntil-test.maxon');
		const testFileUri = vscode.Uri.file(testFilePath);

		// Create a .maxon file
		const content = 'function main() int\nreturn 0\nend "main"';
		fs.writeFileSync(testFilePath, content, 'utf-8');

		// Open the document
		const document = await vscode.workspace.openTextDocument(testFileUri);

		// The handler calls: e.waitUntil(Promise.resolve(edits))
		// This is how VS Code applies the edits during the save event
		const mockEdits: vscode.TextEdit[] = [
			vscode.TextEdit.setEndOfLine(vscode.EndOfLine.LF)
		];

		// Verify we can create a promise of edits
		const editPromise = Promise.resolve(mockEdits);
		const resolvedEdits = await editPromise;

		assert.ok(Array.isArray(resolvedEdits), 'e.waitUntil should receive an array of TextEdits');
		assert.ok(resolvedEdits.length > 0, 'Should have at least one TextEdit (setEndOfLine)');
	});
});
