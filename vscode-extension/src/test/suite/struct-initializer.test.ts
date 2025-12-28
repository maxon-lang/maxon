import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';

/**
 * Test suite for struct initializer formatting and cross-file definition
 */
suite('Struct Initializer Test Suite', () => {

	let testDocument: vscode.TextDocument | undefined;
	let testDocument2: vscode.TextDocument | undefined;

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
		if (testDocument2) {
			await vscode.window.showTextDocument(testDocument2);
			await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
			testDocument2 = undefined;
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

	async function getDefinitionLocations(doc: vscode.TextDocument, position: vscode.Position): Promise<vscode.Location[]> {
		const locations = await vscode.commands.executeCommand<vscode.Location[] | vscode.LocationLink[]>(
			'vscode.executeDefinitionProvider',
			doc.uri,
			position
		);

		if (!locations || locations.length === 0) {
			return [];
		}

		// Convert LocationLinks to Locations if needed
		return locations.map(loc => {
			if ('targetUri' in loc) {
				// It's a LocationLink
				return new vscode.Location(loc.targetUri, loc.targetRange);
			}
			return loc;
		});
	}

	test('Should format multi-line struct initializer fields with proper indentation', async function () {
		this.timeout(10000);

		// Code with improperly indented struct initializer fields in a var declaration
		const badlyFormattedContent = `type Point
	var x int
	var y int
end 'Point'

function createPoint() returns Point
var p = Point{
x: 0,
y: 0
}
return p
end 'createPoint'`;

		const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
		assert.ok(workspaceFolder, 'Workspace folder should exist');

		const testFilePath = path.join(workspaceFolder.uri.fsPath, 'temp', 'test_struct_init_indent.maxon');
		const testUri = vscode.Uri.file(testFilePath);

		const edit = new vscode.WorkspaceEdit();
		edit.createFile(testUri, { ignoreIfExists: true, overwrite: true });
		edit.insert(testUri, new vscode.Position(0, 0), badlyFormattedContent);
		await vscode.workspace.applyEdit(edit);

		testDocument = await vscode.workspace.openTextDocument(testUri);
		await vscode.window.showTextDocument(testDocument);

		// Request formatting
		const edits = await vscode.commands.executeCommand<vscode.TextEdit[]>(
			'vscode.executeFormatDocumentProvider',
			testUri,
			{ tabSize: 4, insertSpaces: false }
		);

		assert.ok(edits, 'Should return formatting edits');

		// Apply the edits
		const workspaceEdit = new vscode.WorkspaceEdit();
		for (const formatEdit of edits) {
			workspaceEdit.replace(testUri, formatEdit.range, formatEdit.newText);
		}
		await vscode.workspace.applyEdit(workspaceEdit);

		// Check the formatted content
		const formattedContent = testDocument.getText();

		// The var statement should be indented (inside function)
		assert.ok(formattedContent.includes('\tvar p = Point{'),
			`Var statement should be indented with 1 tab. Got:\n${formattedContent}`);
		// The return statement should be indented
		assert.ok(formattedContent.includes('\treturn p'),
			`Return statement should be indented with 1 tab. Got:\n${formattedContent}`);

		await vscode.workspace.fs.delete(testUri);
	});

	test('Go to definition for type from another file in same folder', async function () {
		this.timeout(30000);

		const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
		assert.ok(workspaceFolder, 'Workspace folder should exist');

		// Create a project directory with build.maxon to simulate a Maxon project
		const projectDir = path.join(workspaceFolder.uri.fsPath, 'temp', 'cross_file_project');
		const projectDirUri = vscode.Uri.file(projectDir);

		// Create the project directory
		await vscode.workspace.fs.createDirectory(projectDirUri);

		// Create build.maxon to make it a project
		const buildMaxonPath = path.join(projectDir, 'build.maxon');
		const buildMaxonUri = vscode.Uri.file(buildMaxonPath);
		await vscode.workspace.fs.writeFile(buildMaxonUri, Buffer.from('// Build file\n', 'utf8'));

		// Create the first file with the type definition
		const typeDefContent = `export type MyCustomType
	var value int

	export function create() returns MyCustomType
		return MyCustomType{value: 0}
	end 'create'
end 'MyCustomType'
`;
		const typeDefPath = path.join(projectDir, 'types.maxon');
		const typeDefUri = vscode.Uri.file(typeDefPath);
		await vscode.workspace.fs.writeFile(typeDefUri, Buffer.from(typeDefContent, 'utf8'));

		// Open and show the type definition file first
		testDocument = await vscode.workspace.openTextDocument(typeDefUri);
		await vscode.window.showTextDocument(testDocument);

		// Create the second file that uses the type
		const consumerContent = `function useType() returns MyCustomType
	return MyCustomType.create()
end 'useType'
`;
		const consumerPath = path.join(projectDir, 'consumer.maxon');
		const consumerUri = vscode.Uri.file(consumerPath);
		await vscode.workspace.fs.writeFile(consumerUri, Buffer.from(consumerContent, 'utf8'));

		// Open and show the consumer file
		testDocument2 = await vscode.workspace.openTextDocument(consumerUri);
		await vscode.window.showTextDocument(testDocument2);

		// Go to definition on 'MyCustomType' in return type (line 0, col 28)
		// "function useType() returns MyCustomType"
		//  0         1         2         3
		//  0123456789012345678901234567890123456789
		//                              ^ col 28 = 'M'
		const position = new vscode.Position(0, 28);
		const result = await vscode.commands.executeCommand<vscode.Location[] | vscode.LocationLink[]>(
			'vscode.executeDefinitionProvider',
			consumerUri,
			position
		);

		const locations = result.map(loc => {
			if ('targetUri' in loc) {
				return new vscode.Location(loc.targetUri, loc.targetRange);
			}
			return loc;
		});

		assert.ok(locations.length > 0, 'Should find definition for cross-file type in project');
		assert.ok(locations[0].uri.fsPath.includes('types.maxon'),
			`Definition should be in types.maxon, got: ${locations[0].uri.fsPath}`);
		assert.strictEqual(locations[0].range.start.line, 0,
			'Definition should be on line 0 (type declaration)');

		// Clean up - close editors first
		await vscode.commands.executeCommand('workbench.action.closeAllEditors');

		// Delete the project directory
		await vscode.workspace.fs.delete(projectDirUri, { recursive: true });
	});

	test('Go to definition for type used in map type annotation', async function () {
		this.timeout(15000);

		const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
		assert.ok(workspaceFolder, 'Workspace folder should exist');

		// Create a file with a type and a map using that type
		const content = `type ValueHolder
	var data int
end 'ValueHolder'

type Container
	var items Map from string to ValueHolder

	function create() returns Container
		return Container{
			items: Map from string to ValueHolder
		}
	end 'create'
end 'Container'
`;
		const testPath = path.join(workspaceFolder.uri.fsPath, 'temp', 'map_type_def.maxon');
		const testUri = vscode.Uri.file(testPath);

		const edit = new vscode.WorkspaceEdit();
		edit.createFile(testUri, { ignoreIfExists: true, overwrite: true });
		edit.insert(testUri, new vscode.Position(0, 0), content);
		await vscode.workspace.applyEdit(edit);

		testDocument = await vscode.workspace.openTextDocument(testUri);
		await vscode.window.showTextDocument(testDocument);

		// Go to definition on 'ValueHolder' in the map type (line 5, around col 35)
		// "	var items Map from string to ValueHolder"
		const position = new vscode.Position(5, 35);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition for type in map annotation');
		assert.strictEqual(locations[0].range.start.line, 0,
			'Definition should be on line 0 (ValueHolder type declaration)');

		await vscode.workspace.fs.delete(testUri);
	});
});
