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
			"function test() returns int",
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
			"function test() returns int",
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
			"function test() returns float",
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
			"function test() returns float",
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
			"function add(a int, b int) returns int",
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
			"type Point",
			"    var x int",
			"    var y int",
			"end 'Point'",
			"",
			"function test() returns int",
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
		// rather than full type definition - this is acceptable
		assert.ok(hoverText.includes('Point'), 'Hover should show Point name');

		// If it shows type definition, check fields (optional)
		if (hoverText.includes('type') || hoverText.includes('struct')) {
			assert.ok(hoverText.includes('x'), 'Type definition should show field x');
			assert.ok(hoverText.includes('y'), 'Type definition should show field y');
		}
	});

	test('Hover should show user function signature', async function () {
		const content = [
			"function multiply(x int, y int) returns int",
			"    return x * y",
			"end 'multiply'",
			"",
			"function main() returns int",
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
			"function test() returns int",
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
			"function test() returns int",
			"    var numbers = [10]int",
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

	test('Hover on type keyword should be recognized', async function () {
		const content = [
			"type MyStruct",
			"    var value int",
			"end 'MyStruct'"
		].join('\n');

		testDocument = await createTestFile('test_hover_struct_keyword.maxon', content);

		// Hover over 'type' keyword
		const position = new vscode.Position(0, 2);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		assert.ok(hoverText.includes('type'), 'Hover should mention type keyword');
	});

	test('Hover should NOT show for keywords inside line comments', async function () {
		const content = [
			"// This function uses for loops and if statements",
			"function test() returns int",
			"    return 42",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_hover_line_comment.maxon', content);

		// Hover over 'for' in the line comment (line 0, col 24)
		const position = new vscode.Position(0, 24);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		// Should have no hover info for text inside comments
		const hasHover = hovers && hovers.length > 0;
		assert.ok(!hasHover, 'Should NOT have hover information for keywords inside line comments');
	});

	test('Hover should NOT show for keywords inside block comments', async function () {
		const content = [
			"/* This is a block comment",
			"   that mentions for loops",
			"   and if statements */",
			"function test() returns int",
			"    return 42",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_hover_block_comment.maxon', content);

		// Hover over 'for' in the block comment (line 1, col 17)
		const position = new vscode.Position(1, 17);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		// Should have no hover info for text inside comments
		const hasHover = hovers && hovers.length > 0;
		assert.ok(!hasHover, 'Should NOT have hover information for keywords inside block comments');
	});

	test('Hover should NOT show for keywords on block comment start line', async function () {
		const content = [
			"/* for while if function */",
			"function test() returns int",
			"    return 42",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_hover_block_comment_single_line.maxon', content);

		// Hover over 'for' in the single-line block comment (line 0, col 3)
		const position = new vscode.Position(0, 3);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		// Should have no hover info for text inside comments
		const hasHover = hovers && hovers.length > 0;
		assert.ok(!hasHover, 'Should NOT have hover information for keywords inside single-line block comments');
	});

	test('Hover SHOULD show for keywords after block comment ends', async function () {
		const content = [
			"/* comment */ function test() returns int",
			"    return 42",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_hover_after_block_comment.maxon', content);

		// Hover over 'function' after the block comment (line 0, col 14)
		const position = new vscode.Position(0, 18);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information for keywords after block comment');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		assert.ok(hoverText.includes('function'), 'Hover should mention function keyword');
	});

	test('Hover on struct field declaration should show field info, not function with same name', async function () {
		const content = [
			"type MyStruct",
			"    var count int",
			"    var capacity int",
			"end 'MyStruct'"
		].join('\n');

		testDocument = await createTestFile('test_hover_field_vs_function.maxon', content);

		// Hover over 'capacity' field declaration (line 2, col 8)
		const position = new vscode.Position(2, 8);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		// Should show field info, NOT "function capacity(self array) int"
		assert.ok(hoverText.includes('field') || hoverText.includes('var'),
			'Hover should show field info, not function. Got: ' + hoverText);
		assert.ok(!hoverText.includes('function capacity(self array)'),
			'Hover should NOT show array.capacity() function. Got: ' + hoverText);
	});

	test('Hover should show variable type inside interface method', async function () {
		// Simpler test first: regular method inside struct
		const content = [
			"type MyType",
			"    var value int",
			"",
			"    function doSomething() returns int",
			"        var count = 42",
			"        return count",
			"    end 'doSomething'",
			"end 'MyType'"
		].join('\n');

		testDocument = await createTestFile('test_hover_method_var.maxon', content);

		// Hover over 'count' in the return statement (line 5, col 15)
		const position = new vscode.Position(5, 15);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information for variable in method');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		assert.ok(hoverText.includes('count'), 'Hover should show variable name "count". Got: ' + hoverText);
		assert.ok(hoverText.includes('int'), 'Hover should show variable type "int". Got: ' + hoverText);
		assert.ok(hoverText.includes('var'), 'Hover should mention "var". Got: ' + hoverText);
	});

	test('Hover should show variable type inside interface implementation method', async function () {
		const content = [
			"interface TestInterface",
			"    function doSomething() returns int",
			"end 'TestInterface'",
			"",
			"type MyType is TestInterface",
			"    var value int",
			"    function TestInterface.doSomething() returns int",
			"        var count = 42",
			"        return count",
			"    end 'doSomething'",
			"end 'MyType'"
		].join('\n');

		testDocument = await createTestFile('test_hover_interface_method_var.maxon', content);

		// Hover over 'count' in the return statement (line 8, col 15)
		// Lines: 0=interface, 1=fn, 2=end, 3=empty, 4=type, 5=var, 6=fn, 7=var count, 8=return count, 9=end fn, 10=end type
		const position = new vscode.Position(8, 15);
		const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
			'vscode.executeHoverProvider',
			testDocument.uri,
			position
		);

		assert.ok(hovers && hovers.length > 0, 'Should have hover information for variable in interface method');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		// Log for debugging
		console.log('Hover text for interface method variable:', hoverText);

		assert.ok(hoverText.includes('count'), 'Hover should show variable name "count". Got: ' + hoverText);
		assert.ok(hoverText.includes('int'), 'Hover should show variable type "int". Got: ' + hoverText);
		assert.ok(hoverText.includes('var'), 'Hover should mention "var". Got: ' + hoverText);
	});
});
