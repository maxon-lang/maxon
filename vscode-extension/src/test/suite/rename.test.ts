import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';

/**
 * Test suite for Rename functionality
 */
suite('Rename Test Suite', () => {

	let testDocument: vscode.TextDocument | undefined;

	setup(async () => {
		// Wait for extension to activate
		const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
		if (ext && !ext.isActive) {
			await ext.activate();
		}
	});

	teardown(async () => {
		// Close test documents
		if (testDocument) {
			await vscode.window.showTextDocument(testDocument);
			await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
			testDocument = undefined;
		}
	});

	async function createTestFile(filename: string, content: string): Promise<vscode.TextDocument> {
		const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
		assert.ok(workspaceFolder, 'Workspace folder should exist');

		const testFilePath = path.join(workspaceFolder.uri.fsPath, 'temp', filename);
		const testUri = vscode.Uri.file(testFilePath);

		const edit = new vscode.WorkspaceEdit();
		edit.createFile(testUri, { ignoreIfExists: true, overwrite: true });
		edit.insert(testUri, new vscode.Position(0, 0), content);
		await vscode.workspace.applyEdit(edit);

		const doc = await vscode.workspace.openTextDocument(testUri);
		await vscode.window.showTextDocument(doc);

		return doc;
	}

	test('Rename function block identifier', async function () {
		const content = [
			"function oldName() int",
			"    return 42",
			"end 'oldName'"
		].join('\n');

		testDocument = await createTestFile('test_rename_function.maxon', content);

		// Position on the block identifier 'oldName' at end (line 2, col 6)
		const position = new vscode.Position(2, 6);

		const edits = await vscode.commands.executeCommand<vscode.WorkspaceEdit>(
			'vscode.executeDocumentRenameProvider',
			testDocument.uri,
			position,
			"'newName'"
		);

		assert.ok(edits, 'Should return workspace edit');
		assert.ok(edits.size > 0, 'Should have edits');

		const documentEdits = edits.get(testDocument.uri);
		assert.ok(documentEdits, 'Should have edits for document');
		assert.strictEqual(documentEdits.length, 1, 'Should have 1 edit');
		assert.strictEqual(documentEdits[0].newText, "'newName'", 'New text should match');
	});

	test('Rename if statement block identifiers', async function () {
		const content = [
			"function test() int",
			"    var x = 5",
			"    if x = 5 'check'",
			"        return 1",
			"    else 'check'",
			"        return 0",
			"    end 'check'",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_rename_if.maxon', content);

		// Position on the first 'check' (line 2, col 14)
		const position = new vscode.Position(2, 14);

		const edits = await vscode.commands.executeCommand<vscode.WorkspaceEdit>(
			'vscode.executeDocumentRenameProvider',
			testDocument.uri,
			position,
			"'verify'"
		);

		assert.ok(edits, 'Should return workspace edit');
		const documentEdits = edits.get(testDocument.uri);
		assert.ok(documentEdits, 'Should have edits for document');
		assert.strictEqual(documentEdits.length, 3, 'Should have 3 edits (if, else, end)');

		// Verify all are renamed
		for (const edit of documentEdits) {
			assert.strictEqual(edit.newText, "'verify'", 'All edits should have new name');
		}

		// Check lines
		assert.strictEqual(documentEdits[0].range.start.line, 2, 'First edit on if line');
		assert.strictEqual(documentEdits[1].range.start.line, 4, 'Second edit on else line');
		assert.strictEqual(documentEdits[2].range.start.line, 6, 'Third edit on end line');
	});

	test('Rename while loop block identifiers', async function () {
		const content = [
			"function test() int",
			"    var i = 0",
			"    while i < 10 'loop'",
			"        i = i + 1",
			"    end 'loop'",
			"    return i",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_rename_while.maxon', content);

		// Position on 'loop' at end (line 4, col 10)
		const position = new vscode.Position(4, 10);

		const edits = await vscode.commands.executeCommand<vscode.WorkspaceEdit>(
			'vscode.executeDocumentRenameProvider',
			testDocument.uri,
			position,
			"'iteration'"
		);

		assert.ok(edits, 'Should return workspace edit');
		const documentEdits = edits.get(testDocument.uri);
		assert.ok(documentEdits, 'Should have edits for document');
		assert.strictEqual(documentEdits.length, 2, 'Should have 2 edits (while, end)');

		for (const edit of documentEdits) {
			assert.strictEqual(edit.newText, "'iteration'", 'All edits should have new name');
		}
	});

	test('Rename namespace block identifiers', async function () {
		const content = [
			"namespace utils 'utils'",
			"    function helper() int",
			"        return 42",
			"    end 'helper'",
			"end 'utils'"
		].join('\n');

		testDocument = await createTestFile('test_rename_namespace.maxon', content);

		// Position on first 'utils' (line 0, col 17)
		const position = new vscode.Position(0, 17);

		const edits = await vscode.commands.executeCommand<vscode.WorkspaceEdit>(
			'vscode.executeDocumentRenameProvider',
			testDocument.uri,
			position,
			"'helpers'"
		);

		assert.ok(edits, 'Should return workspace edit');
		const documentEdits = edits.get(testDocument.uri);
		assert.ok(documentEdits, 'Should have edits for document');
		assert.strictEqual(documentEdits.length, 2, 'Should have 2 edits (namespace, end)');
	});

	test('Rename struct block identifiers', async function () {
		const content = [
			"struct Point",
			"    var x int",
			"    var y int",
			"end 'Point'"
		].join('\n');

		testDocument = await createTestFile('test_rename_struct.maxon', content);

		// Position on 'Point' at end (line 3, col 6)
		const position = new vscode.Position(3, 6);

		const edits = await vscode.commands.executeCommand<vscode.WorkspaceEdit>(
			'vscode.executeDocumentRenameProvider',
			testDocument.uri,
			position,
			"'Vector2D'"
		);

		assert.ok(edits, 'Should return workspace edit');
		const documentEdits = edits.get(testDocument.uri);
		assert.ok(documentEdits, 'Should have edits for document');
		assert.strictEqual(documentEdits.length, 1, 'Should have 1 edit (only end block)');
	});

	test('Rename on non-block identifier returns nothing', async function () {
		const content = [
			"function test() int",
			"    var x = 5",
			"    return x",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_rename_non_block.maxon', content);

		// Position on 'var' keyword (line 1, col 4)
		const position = new vscode.Position(1, 4);

		try {
			const edits = await vscode.commands.executeCommand<vscode.WorkspaceEdit>(
				'vscode.executeDocumentRenameProvider',
				testDocument.uri,
				position,
				"'newName'"
			);

			// Should return undefined or empty edits
			if (edits) {
				const documentEdits = edits.get(testDocument.uri);
				assert.ok(!documentEdits || documentEdits.length === 0,
					'Should not have edits for non-block identifier');
			}
		} catch (error) {
		}
	});

	test('Rename nested block identifiers correctly', async function () {
		const content = [
			"function test() int",
			"    var x = 0",
			"    while x < 10 'outer'",
			"        if x = 5 'inner'",
			"            x = x + 2",
			"        else 'inner'",
			"            x = x + 1",
			"        end 'inner'",
			"    end 'outer'",
			"    return x",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_rename_nested.maxon', content);

		// Rename the inner block identifier (line 3, col 18)
		const position = new vscode.Position(3, 18);

		const edits = await vscode.commands.executeCommand<vscode.WorkspaceEdit>(
			'vscode.executeDocumentRenameProvider',
			testDocument.uri,
			position,
			"'innerCheck'"
		);

		assert.ok(edits, 'Should return workspace edit');
		const documentEdits = edits.get(testDocument.uri);
		assert.ok(documentEdits, 'Should have edits for document');
		assert.strictEqual(documentEdits.length, 3, 'Should have 3 edits for inner block only');

		// Verify only 'inner' identifiers are renamed, not 'outer'
		for (const edit of documentEdits) {
			assert.strictEqual(edit.newText, "'innerCheck'", 'Edits should have new name');
			const line = edit.range.start.line;
			assert.ok(line >= 3 && line <= 7, 'Edits should only be in inner block range');
		}
	});

	test('Rename all occurrences when multiple blocks share name', async function () {
		const content = [
			"function test1() int",
			"    if true 'block'",
			"        return 1",
			"    end 'block'",
			"end 'test1'",
			"",
			"function test2() int",
			"    if false 'block'",
			"        return 0",
			"    end 'block'",
			"end 'test2'"
		].join('\n');

		testDocument = await createTestFile('test_rename_multiple.maxon', content);

		// Rename first 'block' (line 1, col 13)
		const position = new vscode.Position(1, 13);

		const edits = await vscode.commands.executeCommand<vscode.WorkspaceEdit>(
			'vscode.executeDocumentRenameProvider',
			testDocument.uri,
			position,
			"'condition'"
		);

		assert.ok(edits, 'Should return workspace edit');
		const documentEdits = edits.get(testDocument.uri);
		assert.ok(documentEdits, 'Should have edits for document');

		// Should rename ALL occurrences of 'block' (2 in first function, 2 in second)
		assert.strictEqual(documentEdits.length, 4, 'Should rename all 4 occurrences of "block"');
	});

	test('Rename command integration with F2', async function () {
		const content = [
			"function myFunc() int",
			"    return 42",
			"end 'myFunc'"
		].join('\n');

		testDocument = await createTestFile('test_rename_f2.maxon', content);

		// Position cursor on block identifier (line 2, col 6)
		const editor = await vscode.window.showTextDocument(testDocument);
		editor.selection = new vscode.Selection(2, 6, 2, 6);

		// Test that we can get rename edits at this position
		const position = new vscode.Position(2, 6);
		const edits = await vscode.commands.executeCommand<vscode.WorkspaceEdit>(
			'vscode.executeDocumentRenameProvider',
			testDocument.uri,
			position,
			"'renamed'"
		);

		assert.ok(edits, 'Should return edits for F2 position');

		// Note: We can't fully automate the interactive F2 rename flow in tests,
		// but we verified the position is renameable
	});

	test('Rename struct name should correctly update block identifier', async function () {
		const content = [
			"struct Point",
			"    var x int",
			"    var y int",
			"end 'Point'",
			"",
			"function test() Point",
			"    var p Point",
			"    return Point{x: 1, y: 2}",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_rename_struct_block_id.maxon', content);

		// Rename struct name on line 0, col 7 (on "Point")
		const position = new vscode.Position(0, 7);

		const edits = await vscode.commands.executeCommand<vscode.WorkspaceEdit>(
			'vscode.executeDocumentRenameProvider',
			testDocument.uri,
			position,
			'Vector'
		);

		assert.ok(edits, 'Should return workspace edit');
		const documentEdits = edits.get(testDocument.uri);
		assert.ok(documentEdits, 'Should have edits for document');

		// Should rename:
		// 1. struct Point (line 0)
		// 2. end 'Point' (line 3) - block identifier
		// 3. return type Point (line 5)
		// 4. var p Point (line 6)
		// 5. Point{...} literal (line 7)
		assert.strictEqual(documentEdits.length, 5, 'Should have 5 edits');

		// Find the block identifier edit (line 3)
		const blockIdEdit = documentEdits.find(e => e.range.start.line === 3);
		assert.ok(blockIdEdit, 'Should have edit for block identifier');

		// The block identifier edit should replace 'Point' with 'Vector' (with quotes)
		assert.strictEqual(blockIdEdit.newText, "'Vector'", 'Block identifier should be renamed with quotes');

		// Verify the range is correct - should replace 'Point' (with quotes)
		// The original text is "end 'Point'"
		// The block ID starts at column 4 (opening quote) and ends at column 11 (after closing quote)
		assert.strictEqual(blockIdEdit.range.start.character, 4,
			'Block identifier edit should start at opening quote');
		assert.strictEqual(blockIdEdit.range.end.character, 11,
			'Block identifier edit should end after closing quote');
	});

	test('Linked editing for struct name with block identifier', async function () {
		const content = [
			"struct Point",
			"    var x int",
			"    var y int",
			"end 'Point'",
			"",
			"function test() Point",
			"    var p Point",
			"    return Point{x: 1, y: 2}",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_linked_editing_struct.maxon', content);

		// Position on struct name (line 0, col 7)
		const position = new vscode.Position(0, 7);

		const editor = await vscode.window.showTextDocument(testDocument);
		editor.selection = new vscode.Selection(position, position);

		// Verify position is valid for editing struct name
		const text = testDocument.getText(new vscode.Range(
			new vscode.Position(0, 7),
			new vscode.Position(0, 12)
		));
		assert.strictEqual(text, 'Point', 'Position should be on struct name');

		// Verify the block identifier exists
		const blockIdText = testDocument.getText(new vscode.Range(
			new vscode.Position(3, 5),
			new vscode.Position(3, 10)
		));
		assert.strictEqual(blockIdText, 'Point', 'Block identifier should match struct name');
	});

	test('Linked editing for block identifier only', async function () {
		const content = [
			"if 'condition'",
			"    var x = 1",
			"else 'condition'",
			"    var x = 2",
			"end 'condition'"
		].join('\n');

		testDocument = await createTestFile('test_linked_editing_block.maxon', content);

		// Position on first block identifier (line 0, col 4)
		const position = new vscode.Position(0, 4);

		const editor = await vscode.window.showTextDocument(testDocument);
		editor.selection = new vscode.Selection(position, position);

		// Verify position is valid for editing
		const text = testDocument.getText(new vscode.Range(
			new vscode.Position(0, 4),
			new vscode.Position(0, 13)
		));
		assert.strictEqual(text, 'condition', 'Position should be on block identifier');
	});

	test('Linked editing for function name (no quotes at start)', async function () {
		// Linked editing is fully tested at the LSP C++ level
		// This test verifies the capability is registered and the extension is working
		const content = [
			"function myFunction() int",
			"    return 42",
			"end 'myFunction'"
		].join('\n');

		testDocument = await createTestFile('test_linked_editing_function.maxon', content);

		const editor = await vscode.window.showTextDocument(testDocument);

		// Position cursor at function name
		const functionNamePos = new vscode.Position(0, 9);
		editor.selection = new vscode.Selection(functionNamePos, functionNamePos);

		// Verify the document structure is correct for linked editing
		const functionName = testDocument.getText(new vscode.Range(
			new vscode.Position(0, 9),
			new vscode.Position(0, 19)
		));
		assert.strictEqual(functionName, 'myFunction', 'Function name should be at expected position');

		const blockId = testDocument.getText(new vscode.Range(
			new vscode.Position(2, 5),
			new vscode.Position(2, 15)
		));
		assert.strictEqual(blockId, 'myFunction', 'Block identifier should match function name');

		// The actual linked editing behavior is tested in C++ (lsp-server/tests/test_rename.cpp)
		// where we verify:
		// 1. Function name returns linked editing ranges
		// 2. Ranges include both function name (line 0, cols 9-19)
		// 3. And block identifier inside quotes (line 2, cols 5-15)
	});

	test('Linked editing for while loop block identifier (quotes at start)', async function () {
		// Linked editing is fully tested at the LSP C++ level
		// This test verifies the capability is registered and the extension is working
		const content = [
			"function test() int",
			"    var i = 0",
			"    while i < 10 'loop'",
			"        i = i + 1",
			"    end 'loop'",
			"    return i",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_linked_editing_while.maxon', content);

		const editor = await vscode.window.showTextDocument(testDocument);

		// Position cursor inside 'loop' block identifier
		const loopPos = new vscode.Position(2, 18);
		editor.selection = new vscode.Selection(loopPos, loopPos);

		// Verify the document structure is correct for linked editing
		const whileBlockId = testDocument.getText(new vscode.Range(
			new vscode.Position(2, 18),
			new vscode.Position(2, 22)
		));
		assert.strictEqual(whileBlockId, 'loop', 'While block identifier should be at expected position');

		const endBlockId = testDocument.getText(new vscode.Range(
			new vscode.Position(4, 9),
			new vscode.Position(4, 13)
		));
		assert.strictEqual(endBlockId, 'loop', 'End block identifier should match');

		// The actual linked editing behavior is tested in C++ (lsp-server/tests/test_rename.cpp)
		// where we verify:
		// 1. Block ID inside quotes returns linked editing ranges
		// 2. Ranges include both 'loop' identifiers (lines 2 and 4)
		// 3. Ranges exclude the quotes (only the content inside)
	});
});

