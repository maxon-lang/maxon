import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';

/**
 * Test suite for Enum LSP features (completions, go-to-definition, hover, formatting)
 */
suite('Enum LSP Test Suite', () => {

	let testDocument: vscode.TextDocument | undefined;
	let testFilePath: string;

	suiteSetup(async function () {
		// Ensure extension is activated
		const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
		if (ext && !ext.isActive) {
			await ext.activate();
		}
	});

	setup(async function () {
		const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
		assert.ok(workspaceFolder, 'Workspace folder should exist');
		testFilePath = path.join(workspaceFolder.uri.fsPath, 'temp', 'test_enum_lsp.maxon');
	});

	teardown(async () => {
		// Close test documents
		await vscode.commands.executeCommand('workbench.action.closeAllEditors');
		testDocument = undefined;

		// Clean up test file
		if (testFilePath) {
			try {
				fs.unlinkSync(testFilePath);
			} catch (e) {
				// Ignore errors
			}
		}
	});

	async function createTestFile(filename: string, content: string): Promise<vscode.TextDocument> {
		const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
		assert.ok(workspaceFolder, 'Workspace folder should exist');

		const testFilePathLocal = path.join(workspaceFolder.uri.fsPath, 'temp', filename);
		const testUri = vscode.Uri.file(testFilePathLocal);

		const edit = new vscode.WorkspaceEdit();
		edit.createFile(testUri, { ignoreIfExists: true, overwrite: true });
		edit.insert(testUri, new vscode.Position(0, 0), content);
		await vscode.workspace.applyEdit(edit);

		const doc = await vscode.workspace.openTextDocument(testUri);
		await vscode.window.showTextDocument(doc);

		return doc;
	}

	async function getDefinitionLocations(doc: vscode.TextDocument, position: vscode.Position): Promise<vscode.Location[]> {
		const locations = await vscode.commands.executeCommand<vscode.Location[] | vscode.LocationLink[]>(
			'vscode.executeDefinitionProvider',
			doc.uri,
			position
		);

		if (!locations || locations.length === 0) {
			return [];
		}

		return locations.map(loc => {
			if ('targetUri' in loc) {
				return new vscode.Location(loc.targetUri, loc.targetRange);
			}
			return loc;
		});
	}

	// Helper function to get completions after a dot
	async function getCompletionsAfterDot(
		validContent: string,
		incompleteContent: string,
		dotPosition: vscode.Position
	): Promise<vscode.CompletionList> {
		// Write and open valid content first (so semantic cache is populated)
		fs.writeFileSync(testFilePath, validContent, 'utf-8');
		testDocument = await vscode.workspace.openTextDocument(testFilePath);
		const editor = await vscode.window.showTextDocument(testDocument);

		// Trigger a document change to ensure LSP analyzes it
		await editor.edit(editBuilder => {
			editBuilder.insert(new vscode.Position(0, 0), '');
		});

		// Wait for LSP to analyze by polling completions until we get keywords
		for (let i = 0; i < 10; i++) {
			const testCompletions = await vscode.commands.executeCommand<vscode.CompletionList>(
				'vscode.executeCompletionItemProvider',
				testDocument.uri,
				new vscode.Position(1, 4)
			);
			if (testCompletions.items.some(item => item.label === 'var')) {
				break;
			}
			await new Promise(resolve => setTimeout(resolve, 50));
		}

		// Modify document to have incomplete dot access
		const edit = new vscode.WorkspaceEdit();
		const fullRange = new vscode.Range(
			new vscode.Position(0, 0),
			testDocument.lineAt(testDocument.lineCount - 1).range.end
		);
		edit.replace(testDocument.uri, fullRange, incompleteContent);
		await vscode.workspace.applyEdit(edit);

		// Request completions at the dot position
		return await vscode.commands.executeCommand<vscode.CompletionList>(
			'vscode.executeCompletionItemProvider',
			testDocument.uri,
			dotPosition
		);
	}

	// ==================== Go to Definition Tests ====================

	test('Go to definition for enum type', async function () {
		const content = [
			"enum Direction",
			"    case north",
			"    case south",
			"end 'Direction'",
			"",
			"function test() int",
			"    var d = Direction.north",
			"    return 0",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_def_enum.maxon', content);

		// Go to definition on 'Direction' (line 6, col 12)
		const position = new vscode.Position(6, 12);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition for enum type');
		assert.strictEqual(locations[0].range.start.line, 0, 'Definition should be on line 0 (enum declaration)');
	});

	test('Go to definition for enum in variable type annotation', async function () {
		const content = [
			"enum Color",
			"    case red",
			"    case blue",
			"end 'Color'",
			"",
			"function test() int",
			"    var c = Color.red",
			"    return 0",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_def_enum_var.maxon', content);

		// Go to definition on 'Color' (line 6, col 12)
		const position = new vscode.Position(6, 12);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition');
		assert.strictEqual(locations[0].range.start.line, 0, 'Definition should be on line 0 (enum declaration)');
	});

	test('Go to definition for enum case', async function () {
		const content = [
			"enum Color",
			"    case red",
			"    case blue",
			"    case green",
			"end 'Color'",
			"",
			"function test() int",
			"    var c = Color.blue",
			"    return 0",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_def_enum_case.maxon', content);

		// Go to definition on 'blue' (line 7, col 18)
		const position = new vscode.Position(7, 18);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition for enum case');
		assert.strictEqual(locations[0].range.start.line, 2, 'Definition should be on line 2 (case blue declaration)');
	});

	// ==================== Hover Tests ====================

	test('Hover should show enum definition', async function () {
		const content = [
			"enum Status",
			"    case pending",
			"    case complete",
			"    case failed",
			"end 'Status'",
			"",
			"function test() int",
			"    var s = Status.pending",
			"    return 0",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_hover_enum.maxon', content);

		// Hover over 'Status' (line 7, col 12)
		const position = new vscode.Position(7, 12);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		assert.ok(hoverText.includes('enum'), 'Hover should mention "enum"');
		assert.ok(hoverText.includes('Status'), 'Hover should mention enum name "Status"');
		assert.ok(hoverText.includes('pending'), 'Hover should show case "pending"');
		assert.ok(hoverText.includes('complete'), 'Hover should show case "complete"');
	});

	test('Hover should show raw value enum definition', async function () {
		const content = [
			"enum HttpStatus int",
			"    case ok = 200",
			"    case notFound = 404",
			"end 'HttpStatus'",
			"",
			"function test() int",
			"    var s = HttpStatus.ok",
			"    return 0",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_hover_raw_enum.maxon', content);

		// Hover over 'HttpStatus' (line 6, col 12)
		const position = new vscode.Position(6, 12);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		assert.ok(hoverText.includes('enum'), 'Hover should mention "enum"');
		assert.ok(hoverText.includes('HttpStatus'), 'Hover should mention enum name');
		assert.ok(hoverText.includes('int'), 'Hover should show raw value type "int"');
	});

	// ==================== Completion Tests ====================

	test('Should provide enum case completions after EnumName.', async function () {
		const validContent = `enum Direction
    case north
    case south
    case east
    case west
end 'Direction'

function main() int
    var d = Direction.north
    return 0
end 'main'`;

		const incompleteContent = `enum Direction
    case north
    case south
    case east
    case west
end 'Direction'

function main() int
    var d = Direction.
    return 0
end 'main'`;

		const completions = await getCompletionsAfterDot(
			validContent,
			incompleteContent,
			new vscode.Position(8, 22) // Position after "Direction."
		);

		assert.ok(completions, 'Should return completions');
		const labels = completions.items.map(item =>
			typeof item.label === 'string' ? item.label : item.label.label
		);

		assert.ok(labels.includes('north'), 'Should include "north" case');
		assert.ok(labels.includes('south'), 'Should include "south" case');
		assert.ok(labels.includes('east'), 'Should include "east" case');
		assert.ok(labels.includes('west'), 'Should include "west" case');
	});

	test('Should provide rawValue completion for raw value enum', async function () {
		const validContent = `enum Status int
    case ok = 200
    case error = 500
end 'Status'

function main() int
    var s = Status.ok
    return s.rawValue
end 'main'`;

		const incompleteContent = `enum Status int
    case ok = 200
    case error = 500
end 'Status'

function main() int
    var s = Status.ok
    return s.
end 'main'`;

		const completions = await getCompletionsAfterDot(
			validContent,
			incompleteContent,
			new vscode.Position(7, 13) // Position after "s."
		);

		assert.ok(completions, 'Should return completions');
		const labels = completions.items.map(item =>
			typeof item.label === 'string' ? item.label : item.label.label
		);

		assert.ok(labels.includes('rawValue'), 'Should include "rawValue" property');
	});

	test('Should provide enum method completions', async function () {
		const validContent = `enum Toggle
    case on
    case off

    function flip() Toggle
        if self == Toggle.on 'check'
            return Toggle.off
        end 'check'
        return Toggle.on
    end 'flip'
end 'Toggle'

function main() int
    var t = Toggle.on
    var flipped = t.flip()
    return 0
end 'main'`;

		const incompleteContent = `enum Toggle
    case on
    case off

    function flip() Toggle
        if self == Toggle.on 'check'
            return Toggle.off
        end 'check'
        return Toggle.on
    end 'flip'
end 'Toggle'

function main() int
    var t = Toggle.on
    var flipped = t.
    return 0
end 'main'`;

		const completions = await getCompletionsAfterDot(
			validContent,
			incompleteContent,
			new vscode.Position(14, 20) // Position after "t."
		);

		assert.ok(completions, 'Should return completions');
		const labels = completions.items.map(item =>
			typeof item.label === 'string' ? item.label : item.label.label
		);

		assert.ok(labels.includes('flip'), 'Should include "flip" method');
	});

	// ==================== Formatting Tests ====================

	test('Should format enum with correct indentation', async function () {
		// Code with incorrect indentation
		const badlyFormattedContent = `enum Direction
case north
case south
end 'Direction'`;

		testDocument = await createTestFile('test_format_enum.maxon', badlyFormattedContent);
		const testUri = testDocument.uri;

		// Request formatting
		const edits = await vscode.commands.executeCommand<vscode.TextEdit[]>(
			'vscode.executeFormatDocumentProvider',
			testUri,
			{ tabSize: 4, insertSpaces: false }
		);

		assert.ok(edits, 'Should return formatting edits');
		assert.ok(edits.length > 0, 'Should have at least one edit');

		// Apply the edits
		const workspaceEdit = new vscode.WorkspaceEdit();
		for (const edit of edits) {
			workspaceEdit.replace(testUri, edit.range, edit.newText);
		}
		await vscode.workspace.applyEdit(workspaceEdit);

		// Check the formatted content
		const formattedContent = testDocument.getText();

		// The case statements should be indented
		assert.ok(formattedContent.includes('\tcase north'),
			'case north should be indented with tab');
		assert.ok(formattedContent.includes('\tcase south'),
			'case south should be indented with tab');
		assert.ok(!formattedContent.match(/^case north/m),
			'case north should not be at column 0');
	});

	test('Should format enum with methods correctly', async function () {
		// Code with incorrect indentation
		const badlyFormattedContent = `enum Toggle
case on
case off
function flip() Toggle
if self == Toggle.on 'check'
return Toggle.off
end 'check'
return Toggle.on
end 'flip'
end 'Toggle'`;

		testDocument = await createTestFile('test_format_enum_method.maxon', badlyFormattedContent);
		const testUri = testDocument.uri;

		// Request formatting
		const edits = await vscode.commands.executeCommand<vscode.TextEdit[]>(
			'vscode.executeFormatDocumentProvider',
			testUri,
			{ tabSize: 4, insertSpaces: false }
		);

		assert.ok(edits, 'Should return formatting edits');

		// Apply the edits
		const workspaceEdit = new vscode.WorkspaceEdit();
		for (const edit of edits) {
			workspaceEdit.replace(testUri, edit.range, edit.newText);
		}
		await vscode.workspace.applyEdit(workspaceEdit);

		// Check the formatted content
		const formattedContent = testDocument.getText();

		// Cases and function should be indented once
		assert.ok(formattedContent.includes('\tcase on'), 'case should be indented');
		assert.ok(formattedContent.includes('\tfunction flip'), 'function should be indented');

		// Function body should be indented twice
		assert.ok(formattedContent.includes('\t\tif self'), 'if statement should be double-indented');
	});

	test('Should format export enum correctly', async function () {
		const badlyFormattedContent = `export enum Color
case red
case green
case blue
end 'Color'`;

		testDocument = await createTestFile('test_format_export_enum.maxon', badlyFormattedContent);
		const testUri = testDocument.uri;

		// Request formatting
		const edits = await vscode.commands.executeCommand<vscode.TextEdit[]>(
			'vscode.executeFormatDocumentProvider',
			testUri,
			{ tabSize: 4, insertSpaces: false }
		);

		assert.ok(edits, 'Should return formatting edits');

		// Apply the edits
		const workspaceEdit = new vscode.WorkspaceEdit();
		for (const edit of edits) {
			workspaceEdit.replace(testUri, edit.range, edit.newText);
		}
		await vscode.workspace.applyEdit(workspaceEdit);

		const formattedContent = testDocument.getText();

		// Cases should be indented
		assert.ok(formattedContent.includes('\tcase red'), 'case red should be indented');
		assert.ok(formattedContent.includes('\tcase green'), 'case green should be indented');
		assert.ok(formattedContent.includes('\tcase blue'), 'case blue should be indented');
	});
});
