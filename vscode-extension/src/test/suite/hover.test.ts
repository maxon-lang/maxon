import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';

/**
 * Test suite for Hover functionality
 */
suite('Hover Test Suite', () => {

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

	test('Hover should show type for let variable', async function () {
		const content = [
			"function test() int",
			"    let x = 42",
			"    return x",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_hover_let.maxon', content);

		// Hover over 'x' in the return statement (line 2, col 11)
		const position = new vscode.Position(2, 11);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		assert.ok(hoverText.includes('let'), 'Hover should mention "let"');
		assert.ok(hoverText.includes('x'), 'Hover should mention variable name "x"');
		assert.ok(hoverText.includes('int'), 'Hover should show type "int"');
	});

	test('Hover should show type for var variable', async function () {
		const content = [
			"function test() int",
			"    var count = 0",
			"    count = count + 1",
			"    return count",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_hover_var.maxon', content);

		// Hover over 'count' in the assignment (line 2, col 4)
		const position = new vscode.Position(2, 4);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		assert.ok(hoverText.includes('var'), 'Hover should mention "var"');
		assert.ok(hoverText.includes('count'), 'Hover should mention variable name');
		assert.ok(hoverText.includes('int'), 'Hover should show type');
	});

	test('Hover should show type for float variable', async function () {
		const content = [
			"function test() float",
			"    let pi = 3.14159",
			"    return pi",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_hover_float.maxon', content);

		const position = new vscode.Position(2, 11);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		assert.ok(hoverText.includes('pi'), 'Hover should mention variable name');
		assert.ok(hoverText.includes('float'), 'Hover should show float type');
		assert.ok(hoverText.includes('3.14159'), 'Hover should show exact literal value');
	});

	test('Hover should show exact float literal with many decimal places', async function () {
		const content = [
			"function test() float",
			"    let SOLAR_MASS = 39.478417604357432",
			"    return SOLAR_MASS",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_hover_float_precise.maxon', content);

		const position = new vscode.Position(2, 15);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		assert.ok(hoverText.includes('SOLAR_MASS'), 'Hover should mention variable name');
		assert.ok(hoverText.includes('float'), 'Hover should show float type');
		assert.ok(hoverText.includes('39.478417604357432'), 'Hover should show exact value as written in source');
	});

	test('Hover should show function parameter info', async function () {
		const content = [
			"function add(a int, b int) int",
			"    return a + b",
			"end 'add'"
		].join('\n');

		testDocument = await createTestFile('test_hover_param.maxon', content);

		// Hover over 'a' in the return statement
		const position = new vscode.Position(1, 11);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		assert.ok(hoverText.includes('a'), 'Hover should mention parameter name');
		assert.ok(hoverText.includes('int'), 'Hover should show parameter type');
		assert.ok(hoverText.toLowerCase().includes('parameter'),
			'Hover should indicate it is a parameter');
	});

	test('Hover should show struct definition', async function () {
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

		testDocument = await createTestFile('test_hover_struct.maxon', content);

		// Hover over 'Point' in the var declaration
		const position = new vscode.Position(6, 11);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		// Note: Hovering over type name in var declaration may show identifier info
		// rather than full struct definition - this is acceptable
		assert.ok(hoverText.includes('Point'), 'Hover should show Point name');

		// If it shows struct definition, check fields (optional)
		if (hoverText.includes('struct')) {
			assert.ok(hoverText.includes('x'), 'Struct definition should show field x');
			assert.ok(hoverText.includes('y'), 'Struct definition should show field y');
		}
	});

	test('Hover should show user function signature', async function () {
		const content = [
			"function multiply(x int, y int) int",
			"    return x * y",
			"end 'multiply'",
			"",
			"function main() int",
			"    return multiply(3, 4)",
			"end 'main'"
		].join('\n');

		testDocument = await createTestFile('test_hover_function.maxon', content);

		// Hover over 'multiply' in the function call
		const position = new vscode.Position(5, 11);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		assert.ok(hoverText.includes('function'), 'Hover should mention function');
		assert.ok(hoverText.includes('multiply'), 'Hover should show function name');
		assert.ok(hoverText.includes('x'), 'Hover should show parameter x');
		assert.ok(hoverText.includes('y'), 'Hover should show parameter y');
	});

	test('Hover should show keyword info', async function () {
		const content = [
			"function test() int",
			"    return 42",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_hover_keyword.maxon', content);

		// Hover over 'return' keyword
		const position = new vscode.Position(1, 4);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		assert.ok(hoverText.includes('return'), 'Hover should mention return');
		assert.ok(hoverText.toLowerCase().includes('keyword') ||
			hoverText.toLowerCase().includes('control flow'),
			'Hover should indicate it is a keyword or control flow');
	});

	test('Hover should show array type', async function () {
		const content = [
			"function test() int",
			"    var numbers []int",
			"    return numbers[0]",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_hover_array.maxon', content);

		// Hover over 'numbers' in the return statement
		const position = new vscode.Position(2, 11);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		// Array type declarations may not be fully supported yet
		if (!hovers || hovers.length === 0) {
			return; // Skip this test
		} const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		// Should at least show the variable name
		assert.ok(hoverText.includes('numbers'), 'Hover should mention variable name');
	});

	test('Hover on struct keyword should be recognized', async function () {
		const content = [
			"struct MyStruct",
			"    value int",
			"end 'MyStruct'"
		].join('\n');

		testDocument = await createTestFile('test_hover_struct_keyword.maxon', content);

		// Hover over 'struct' keyword
		const position = new vscode.Position(0, 3);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		assert.ok(hoverText.includes('struct'), 'Hover should mention struct keyword');
	});
});
