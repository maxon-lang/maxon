import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';

/**
 * Test suite for Go to Definition functionality
 */
suite('Go to Definition Test Suite', () => {

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

	test('Go to definition for var variable', async function () {
		const content = [
			"function test() int",
			"    var counter = 0",
			"    counter = counter + 1",
			"    return counter",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_def_var.maxon', content);

		// Go to definition on 'counter' usage in assignment (line 2, col 4)
		const position = new vscode.Position(2, 4);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition');
		assert.strictEqual(locations[0].uri.fsPath, testDocument.uri.fsPath, 'Definition should be in same file');
		assert.strictEqual(locations[0].range.start.line, 1, 'Definition should be on line 1 (var declaration)');
	});

	test('Go to definition for let variable', async function () {
		const content = [
			"function test() int",
			"    let value = 42",
			"    return value",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_def_let.maxon', content);

		// Go to definition on 'value' usage in return (line 2, col 11)
		const position = new vscode.Position(2, 11);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition');
		assert.strictEqual(locations[0].range.start.line, 1, 'Definition should be on line 1 (let declaration)');
	});

	test('Go to definition for function', async function () {
		const content = [
			"function helper() int",
			"    return 42",
			"end 'helper'",
			"",
			"function main() int",
			"    return helper()",
			"end 'main'"
		].join('\n');

		testDocument = await createTestFile('test_def_function.maxon', content);

		// Go to definition on 'helper' call (line 5, col 11)
		const position = new vscode.Position(5, 11);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition');
		assert.strictEqual(locations[0].range.start.line, 0, 'Definition should be on line 0 (function declaration)');
	});

	test('Go to definition for function parameter', async function () {
		const content = [
			"function add(a int, b int) int",
			"    return a + b",
			"end 'add'"
		].join('\n');

		testDocument = await createTestFile('test_def_param.maxon', content);

		// Go to definition on 'a' usage in return (line 1, col 11)
		const position = new vscode.Position(1, 11);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition');
		assert.strictEqual(locations[0].range.start.line, 0, 'Definition should be on line 0 (parameter)');
	});

	test('Go to definition for struct type', async function () {
		const content = [
			"struct Point",
			"    x int",
			"    y int",
			"end 'Point'",
			"",
			"function test() int",
			"    var p Point",
			"    return 0",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_def_struct.maxon', content);

		// Go to definition on 'Point' type in var declaration (line 6, col 10)
		const position = new vscode.Position(6, 10);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition');
		assert.strictEqual(locations[0].range.start.line, 0, 'Definition should be on line 0 (struct declaration)');
	});

	test('Go to definition for struct field', async function () {
		const content = [
			"struct Point",
			"    x int",
			"    y int",
			"end 'Point'",
			"",
			"function test() int",
			"    var p Point",
			"    p.x = 10",
			"    return p.x",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_def_field.maxon', content);

		// Go to definition on 'x' field access (line 7, col 6)
		const position = new vscode.Position(7, 6);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition');
		assert.strictEqual(locations[0].range.start.line, 1, 'Definition should be on line 1 (field declaration)');
	});

	test('Go to definition for struct field in return', async function () {
		const content = [
			"struct Point",
			"    x int",
			"    y int",
			"end 'Point'",
			"",
			"function getX(p Point) int",
			"    return p.x",
			"end 'getX'"
		].join('\n');

		testDocument = await createTestFile('test_def_field_return.maxon', content);

		// Go to definition on 'x' field access in return (line 6, col 13)
		const position = new vscode.Position(6, 13);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition');
		assert.strictEqual(locations[0].range.start.line, 1, 'Definition should be on line 1 (field declaration)');
	});

	test('Go to definition for interface type', async function () {
		const content = [
			"interface Printable",
			"    function print() void",
			"end 'Printable'",
			"",
			"struct Message is Printable",
			"    text string",
			"end 'Message'"
		].join('\n');

		testDocument = await createTestFile('test_def_interface.maxon', content);

		// Go to definition on 'Printable' in conformance (line 4, col 18)
		const position = new vscode.Position(4, 18);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition');
		assert.strictEqual(locations[0].range.start.line, 0, 'Definition should be on line 0 (interface declaration)');
	});

	test('Go to definition for variable in nested scope', async function () {
		const content = [
			"function test() int",
			"    var outer = 1",
			"    if outer = 1 'check'",
			"        var inner = 2",
			"        return inner + outer",
			"    end 'check'",
			"    return outer",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_def_scope.maxon', content);

		// Go to definition on 'inner' usage (line 4, col 15)
		const position = new vscode.Position(4, 15);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition');
		assert.strictEqual(locations[0].range.start.line, 3, 'Definition should be on line 3 (inner var)');
	});

	test('Go to definition on keyword returns nothing', async function () {
		const content = [
			"function test() int",
			"    return 42",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_def_keyword.maxon', content);

		// Try go to definition on 'return' keyword (line 1, col 4)
		const position = new vscode.Position(1, 4);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.strictEqual(locations.length, 0, 'Should not find definition for keyword');
	});

	test('Go to definition on number literal returns nothing', async function () {
		const content = [
			"function test() int",
			"    return 42",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_def_number.maxon', content);

		// Try go to definition on '42' literal (line 1, col 11)
		const position = new vscode.Position(1, 11);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.strictEqual(locations.length, 0, 'Should not find definition for literal');
	});

	test('Go to definition for stdlib function', async function () {
		const content = [
			"function main() int",
			"    print(42)",
			"    return 0",
			"end 'main'"
		].join('\n');

		testDocument = await createTestFile('test_def_stdlib.maxon', content);

		// Go to definition on 'print' call (line 1, col 4)
		const position = new vscode.Position(1, 4);
		const locations = await getDefinitionLocations(testDocument, position);

		// print is a built-in - may or may not have a file location
		// Just verify the call doesn't crash
		// If stdlib is loaded with file paths, we should get a location
		if (locations.length > 0) {
			assert.ok(locations[0].uri, 'Should have a URI');
		}
	});

	test('Go to definition for function with multiple params', async function () {
		const content = [
			"function sum(a int, b int, c int) int",
			"    return a + b + c",
			"end 'sum'",
			"",
			"function test() int",
			"    return sum(1, 2, 3)",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_def_multi_param.maxon', content);

		// Go to definition on 'b' in the return (line 1, col 17)
		const position = new vscode.Position(1, 17);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition');
		assert.strictEqual(locations[0].range.start.line, 0, 'Definition should be on line 0 (parameter)');
	});

	test('Go to definition jumps to correct field in struct with multiple fields', async function () {
		const content = [
			"struct Rectangle",
			"    width int",
			"    height int",
			"    area int",
			"end 'Rectangle'",
			"",
			"function test() int",
			"    var r Rectangle",
			"    r.width = 10",
			"    r.height = 20",
			"    return r.area",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_def_multi_field.maxon', content);

		// Go to definition on 'height' (line 9, col 6)
		const heightPosition = new vscode.Position(9, 6);
		const heightLocations = await getDefinitionLocations(testDocument, heightPosition);

		assert.ok(heightLocations.length > 0, 'Should find height definition');
		assert.strictEqual(heightLocations[0].range.start.line, 2, 'height definition should be on line 2');

		// Go to definition on 'area' (line 10, col 13)
		const areaPosition = new vscode.Position(10, 13);
		const areaLocations = await getDefinitionLocations(testDocument, areaPosition);

		assert.ok(areaLocations.length > 0, 'Should find area definition');
		assert.strictEqual(areaLocations[0].range.start.line, 3, 'area definition should be on line 3');
	});

	test('Go to definition for recursive function call', async function () {
		const content = [
			"function factorial(n int) int",
			"    if n <= 1 'base'",
			"        return 1",
			"    end 'base'",
			"    return n * factorial(n - 1)",
			"end 'factorial'"
		].join('\n');

		testDocument = await createTestFile('test_def_recursive.maxon', content);

		// Go to definition on recursive 'factorial' call (line 4, col 16)
		const position = new vscode.Position(4, 16);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition');
		assert.strictEqual(locations[0].range.start.line, 0, 'Definition should be on line 0 (function declaration)');
	});

	test('Go to definition for struct in return type', async function () {
		const content = [
			"struct Point",
			"    x int",
			"    y int",
			"end 'Point'",
			"",
			"function createPoint() Point",
			"    return Point{x: 0, y: 0}",
			"end 'createPoint'"
		].join('\n');

		testDocument = await createTestFile('test_def_return_type.maxon', content);

		// Go to definition on 'Point' return type (line 5, col 23)
		const position = new vscode.Position(5, 23);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition');
		assert.strictEqual(locations[0].range.start.line, 0, 'Definition should be on line 0 (struct declaration)');
	});

	test('Go to definition for struct literal type', async function () {
		const content = [
			"struct Point",
			"    x int",
			"    y int",
			"end 'Point'",
			"",
			"function test() Point",
			"    return Point{x: 0, y: 0}",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_def_literal_type.maxon', content);

		// Go to definition on 'Point' in struct literal (line 6, col 11)
		const position = new vscode.Position(6, 11);
		const locations = await getDefinitionLocations(testDocument, position);

		assert.ok(locations.length > 0, 'Should find definition');
		assert.strictEqual(locations[0].range.start.line, 0, 'Definition should be on line 0 (struct declaration)');
	});
});
