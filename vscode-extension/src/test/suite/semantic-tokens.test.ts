import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';

/**
 * Test suite for Semantic Tokens functionality (nested block identifier colorization)
 */
suite('Semantic Tokens Test Suite', () => {

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

	test('Should provide semantic tokens for nested block identifiers', async function () {
		const content = [
			"function main() int",
			"    var count = 0",
			"    var a = 0",
			"    while a < 2 'outer'",
			"        a = a + 1",
			"        var b = 0",
			"        while b < 3 'middle'",
			"            b = b + 1",
			"            var c = 0",
			"            while c < 4 'inner'",
			"                c = c + 1",
			"                count = count + 1",
			"                if c = 2 'check'",
			"                    continue 'middle'",
			"                end 'check'",
			"            end 'inner'",
			"        end 'middle'",
			"    end 'outer'",
			"    return count",
			"end 'main'"
		].join('\n');

		testDocument = await createTestFile('test_semantic_tokens.maxon', content);

		// Request semantic tokens
		const semanticTokens = await vscode.commands.executeCommand<vscode.SemanticTokens>(
			'vscode.provideDocumentSemanticTokens',
			testDocument.uri
		);

		assert.ok(semanticTokens, 'Should receive semantic tokens');
		assert.ok(semanticTokens.data, 'Semantic tokens should have data');
		assert.ok(semanticTokens.data.length > 0, 'Semantic tokens data should not be empty');

		// The data is in delta-encoded format: [deltaLine, deltaChar, length, tokenType, tokenModifiers]
		// We should have semantic tokens for:
		// - 'main' (opening function name + closing) = 2
		// - 'outer' (opening + closing) = 2
		// - 'middle' (opening + closing + continue target) = 3
		// - 'inner' (opening + closing) = 2
		// - 'check' (opening + closing) = 2
		// Total: 11 tokens
		const expectedTokenCount = 11;
		const actualTokenCount = semanticTokens.data.length / 5; // Each token is 5 integers

		assert.strictEqual(actualTokenCount, expectedTokenCount,
			`Should have ${expectedTokenCount} semantic tokens (including continue 'middle' target)`);

		// Verify token type is 'label' (index 22)
		// The token type is the 4th value in the 5-integer tuple (index 3)
		const firstTokenType = semanticTokens.data[3];
		assert.strictEqual(firstTokenType, 22, 'Token type should be "label" (index 22)');
	});

	test('Semantic tokens should have correct modifiers for nesting levels', async function () {
		const content = [
			"function test() returns int",
			"    if true 'level1'",
			"        if true 'level2'",
			"            if true 'level3'",
			"            end 'level3'",
			"        end 'level2'",
			"    end 'level1'",
			"    return 0",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_semantic_nesting.maxon', content);

		const semanticTokens = await vscode.commands.executeCommand<vscode.SemanticTokens>(
			'vscode.provideDocumentSemanticTokens',
			testDocument.uri
		);

		assert.ok(semanticTokens, 'Should receive semantic tokens');
		assert.ok(semanticTokens.data.length > 0, 'Should have token data');

		// Parse the delta-encoded data
		const tokens: Array<{ line: number; char: number; length: number; type: number; modifiers: number; }> = [];
		let currentLine = 0;
		let currentChar = 0;

		for (let i = 0; i < semanticTokens.data.length; i += 5) {
			const deltaLine = semanticTokens.data[i];
			const deltaChar = semanticTokens.data[i + 1];
			const length = semanticTokens.data[i + 2];
			const type = semanticTokens.data[i + 3];
			const modifiers = semanticTokens.data[i + 4];

			currentLine += deltaLine;
			currentChar = (deltaLine === 0) ? currentChar + deltaChar : deltaChar;

			tokens.push({ line: currentLine, char: currentChar, length, type, modifiers });
		}

		// We should have tokens for: test (function name), 'test' (closing), 'level1'(x2), 'level2'(x2), 'level3'(x2)
		// Note: Function names are implied block identifiers
		// The LSP may also return tokens for return types, so check >= 8
		assert.ok(tokens.length >= 8, 'Should have at least 8 block identifier tokens');

		// Filter to only label tokens (type 22) for the modifier check
		const labelTokens = tokens.filter(t => t.type === 22);

		// Check that modifiers are different for different nesting levels
		// Modifiers are bit masks, so different levels should have different values
		const modifierValues = new Set(labelTokens.map(t => t.modifiers));
		assert.ok(modifierValues.size >= 3, 'Should have at least 3 different modifier values for different nesting levels');
	});

	test('Function-level block identifiers should be at level 0', async function () {
		const content = [
			"function test() returns int",
			"    return 0",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_function_level.maxon', content);

		const semanticTokens = await vscode.commands.executeCommand<vscode.SemanticTokens>(
			'vscode.provideDocumentSemanticTokens',
			testDocument.uri
		);

		assert.ok(semanticTokens, 'Should receive semantic tokens');
		// Functions have at least 2 label tokens: function name + closing 'name'
		// The LSP may also return tokens for return types
		assert.ok(semanticTokens.data.length >= 10, 'Should have at least 2 tokens (function name and closing "test") with 5 values each');

		// Find the two label tokens (type 22) for the function block identifier
		const tokens: Array<{ type: number; modifiers: number; }> = [];
		for (let i = 0; i < semanticTokens.data.length; i += 5) {
			tokens.push({ type: semanticTokens.data[i + 3], modifiers: semanticTokens.data[i + 4] });
		}
		const labelTokens = tokens.filter(t => t.type === 22);
		assert.ok(labelTokens.length >= 2, 'Should have at least 2 label tokens');

		// Both label tokens should have level 0 modifier (bit 10 set)
		const modifier1 = labelTokens[0].modifiers;
		const modifier2 = labelTokens[1].modifiers;
		const level0Bit = 1 << 10; // level0 is modifier index 10
		assert.ok(modifier1 & level0Bit, 'Function name should have level 0 modifier');
		assert.ok(modifier2 & level0Bit, 'Closing identifier should have level 0 modifier');
		assert.strictEqual(modifier1, modifier2, 'Both should have the same modifier (same color)');
	});

	test('Nested loops should have different colors', async function () {
		const content = [
			"function test() returns int",
			"    while true 'outer'",
			"        while true 'inner'",
			"        end 'inner'",
			"    end 'outer'",
			"    return 0",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_nested_loops.maxon', content);

		const semanticTokens = await vscode.commands.executeCommand<vscode.SemanticTokens>(
			'vscode.provideDocumentSemanticTokens',
			testDocument.uri
		);

		assert.ok(semanticTokens, 'Should receive semantic tokens');

		// Parse tokens to get modifiers
		const modifiers: number[] = [];
		for (let i = 4; i < semanticTokens.data.length; i += 5) {
			modifiers.push(semanticTokens.data[i]);
		}

		// 'test' identifiers should have modifier for level 0
		const testModifier = modifiers[0];

		// 'outer' identifiers should have modifier for level 1
		const outerModifier = modifiers[1];

		// 'inner' identifiers should have modifier for level 2
		const innerModifier = modifiers[3];

		assert.notStrictEqual(testModifier, outerModifier, 'Function and first loop should have different modifiers');
		assert.notStrictEqual(outerModifier, innerModifier, 'Outer and inner loops should have different modifiers');
		assert.notStrictEqual(testModifier, innerModifier, 'Function and inner loop should have different modifiers');
	});

	test('Opening and closing block identifiers should have matching colors', async function () {
		const content = [
			"function test() returns int",
			"    while true 'loop'",
			"        if true 'condition'",
			"        end 'condition'",
			"    end 'loop'",
			"    return 0",
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_matching_colors.maxon', content);

		const semanticTokens = await vscode.commands.executeCommand<vscode.SemanticTokens>(
			'vscode.provideDocumentSemanticTokens',
			testDocument.uri
		);

		assert.ok(semanticTokens, 'Should receive semantic tokens');

		// Parse tokens with type information
		const tokens: Array<{ text: string; modifier: number; type: number; }> = [];
		let currentLine = 0;
		let currentChar = 0;

		for (let i = 0; i < semanticTokens.data.length; i += 5) {
			const deltaLine = semanticTokens.data[i];
			const deltaChar = semanticTokens.data[i + 1];
			const length = semanticTokens.data[i + 2];
			const type = semanticTokens.data[i + 3];
			const modifier = semanticTokens.data[i + 4];

			currentLine += deltaLine;
			currentChar = (deltaLine === 0) ? currentChar + deltaChar : deltaChar;

			// Extract the actual text from the document
			const lineContent = content.split('\n')[currentLine];
			const text = lineContent.substring(currentChar, currentChar + length).replace(/'/g, '');

			tokens.push({ text, modifier, type });
		}

		// Filter to only label tokens (type 22)
		const labelTokens = tokens.filter(t => t.type === 22);

		// Find matching pairs among label tokens
		const testTokens = labelTokens.filter(t => t.text === 'test');
		const loopTokens = labelTokens.filter(t => t.text === 'loop');
		const conditionTokens = labelTokens.filter(t => t.text === 'condition');

		assert.strictEqual(testTokens.length, 2, 'Should have 2 "test" tokens');
		assert.strictEqual(loopTokens.length, 2, 'Should have 2 "loop" tokens');
		assert.strictEqual(conditionTokens.length, 2, 'Should have 2 "condition" tokens');

		// Check that opening and closing have the same modifier
		assert.strictEqual(testTokens[0].modifier, testTokens[1].modifier,
			'Opening and closing "test" should have same modifier');
		assert.strictEqual(loopTokens[0].modifier, loopTokens[1].modifier,
			'Opening and closing "loop" should have same modifier');
		assert.strictEqual(conditionTokens[0].modifier, conditionTokens[1].modifier,
			'Opening and closing "condition" should have same modifier');
	});

	test('Deep nesting should cycle through colors', async function () {
		const content = [
			"function f() int",
			"    if true 'i1'",
			"        if true 'i2'",
			"            if true 'i3'",
			"                if true 'i4'",
			"                    if true 'i5'",
			"                        if true 'i6'",
			"                            if true 'i7'",
			"                            end 'i7'",
			"                        end 'i6'",
			"                    end 'i5'",
			"                end 'i4'",
			"            end 'i3'",
			"        end 'i2'",
			"    end 'i1'",
			"    return 0",
			"end 'f'"
		].join('\n');

		testDocument = await createTestFile('test_deep_nesting.maxon', content);

		const semanticTokens = await vscode.commands.executeCommand<vscode.SemanticTokens>(
			'vscode.provideDocumentSemanticTokens',
			testDocument.uri
		);

		assert.ok(semanticTokens, 'Should receive semantic tokens');

		// Extract modifiers
		const modifiers: number[] = [];
		for (let i = 4; i < semanticTokens.data.length; i += 5) {
			modifiers.push(semanticTokens.data[i]);
		}

		// With 6 levels (0-5) cycling, level 6 should equal level 0, level 7 should equal level 1, etc.
		// We have: f(0), 'i1'(1), 'i2'(2), 'i3'(3), 'i4'(4), 'i5'(5), 'i6'(0), 'i7'(1)
		// Token order in document: f, 'i1' open, 'i2' open, ..., 'i7' open, 'i7' close, 'i6' close, ..., 'f' close

		// Get modifiers for 'f' (level 0), 'i6' opening (should also be level 0)
		const fModifier = modifiers[0]; // f (function name, level 0)
		const i6Modifier = modifiers[6]; // 'i6' opening (after f, i1-5 openings)

		// Get modifiers for 'i1' opening (level 1), 'i7' opening (should also be level 1)
		const i1Modifier = modifiers[1]; // 'i1' opening (level 1)
		const i7Modifier = modifiers[7]; // 'i7' opening (level 7 % 6 = 1)

		assert.strictEqual(fModifier, i6Modifier, 'Level 0 and level 6 should have same modifier (cycling)');
		assert.strictEqual(i1Modifier, i7Modifier, 'Level 1 and level 7 should have same modifier (cycling)');
	});

	test('Should not provide semantic tokens for type names inside string literals', async function () {
		// Test that type references inside strings are not highlighted as types
		const content = [
			"type Widget",
			"    var name string",
			"end 'Widget'",
			"",
			"function test() returns string",
			'    var widget = Widget{name: "test"}',
			'    return "The Widget is {widget.name}"',
			"end 'test'"
		].join('\n');

		testDocument = await createTestFile('test_string_interpolation.maxon', content);

		// Wait for LSP to process the document
		await new Promise(resolve => setTimeout(resolve, 500));

		const semanticTokens = await vscode.commands.executeCommand<vscode.SemanticTokens>(
			'vscode.provideDocumentSemanticTokens',
			testDocument.uri
		);

		assert.ok(semanticTokens, 'Should receive semantic tokens');

		// Parse all tokens to find their positions and types
		const TYPE_TOKEN = 1; // Type token type index
		const tokens: Array<{ line: number; char: number; length: number; type: number; }> = [];
		let currentLine = 0;
		let currentChar = 0;

		for (let i = 0; i < semanticTokens.data.length; i += 5) {
			const deltaLine = semanticTokens.data[i];
			const deltaChar = semanticTokens.data[i + 1];
			const length = semanticTokens.data[i + 2];
			const type = semanticTokens.data[i + 3];

			currentLine += deltaLine;
			currentChar = (deltaLine === 0) ? currentChar + deltaChar : deltaChar;

			tokens.push({ line: currentLine, char: currentChar, length, type });
		}

		// Line 6 (0-indexed) contains: 'return "The Widget is {widget.name}"'
		// The word "Widget" inside the string (after "The ") should NOT be a type token
		const stringLine = 6;
		const typeTokensOnStringLine = tokens.filter(t =>
			t.line === stringLine && t.type === TYPE_TOKEN
		);

		// There should be NO type tokens on the line with the string literal
		// (the Widget usage on line 5 is outside a string, which is fine)
		assert.strictEqual(typeTokensOnStringLine.length, 0,
			'Type names inside string literals should not be highlighted as types');
	});
});
