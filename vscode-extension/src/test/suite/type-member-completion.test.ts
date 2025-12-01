import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';

suite('Type Member Completion Tests', () => {
	let document: vscode.TextDocument;
	let testFilePath: string;

	suiteSetup(async function () {
		// Ensure extension is activated
		const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
		if (ext && !ext.isActive) {
			await ext.activate();
		}
	});

	setup(async function () {
		// Create a temporary test file
		const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
		assert.ok(workspaceFolder, 'Workspace folder should be available');

		testFilePath = path.join(workspaceFolder.uri.fsPath, 'temp', 'test_type_members.maxon');
	});

	teardown(async () => {
		// Close all editors
		await vscode.commands.executeCommand('workbench.action.closeAllEditors');

		// Clean up test file
		if (testFilePath) {
			try {
				fs.unlinkSync(testFilePath);
			} catch (e) {
				// Ignore errors
			}
		}
	});

	// Helper function to prepare document and get completions after a dot
	// We first write valid code that can be parsed, then modify to incomplete dot access
	async function getCompletionsAfterDot(
		validContent: string,
		incompleteContent: string,
		dotPosition: vscode.Position
	): Promise<vscode.CompletionList> {
		// Step 1: Write and open valid content (so semantic cache is populated)
		fs.writeFileSync(testFilePath, validContent, 'utf-8');
		document = await vscode.workspace.openTextDocument(testFilePath);
		const editor = await vscode.window.showTextDocument(document);

		// Trigger a document change to ensure LSP analyzes it
		await editor.edit(editBuilder => {
			editBuilder.insert(new vscode.Position(0, 0), '');
		});

		// Wait for LSP to analyze by polling completions until we get keywords
		for (let i = 0; i < 10; i++) {
			const testCompletions = await vscode.commands.executeCommand<vscode.CompletionList>(
				'vscode.executeCompletionItemProvider',
				document.uri,
				new vscode.Position(1, 4)
			);
			if (testCompletions.items.some(item => item.label === 'var')) {
				break;
			}
			await new Promise(resolve => setTimeout(resolve, 50));
		}

		// Step 2: Modify document to have incomplete dot access
		const edit = new vscode.WorkspaceEdit();
		const fullRange = new vscode.Range(
			new vscode.Position(0, 0),
			document.lineAt(document.lineCount - 1).range.end
		);
		edit.replace(document.uri, fullRange, incompleteContent);
		await vscode.workspace.applyEdit(edit);

		// Step 3: Request completions at the dot position
		return await vscode.commands.executeCommand<vscode.CompletionList>(
			'vscode.executeCompletionItemProvider',
			document.uri,
			dotPosition
		);
	}

	test('Should provide string property completions after dot', async function () {
		const validContent = `function main() int
    var s = "hello"
    return s.count
end 'main'`;

		const incompleteContent = `function main() int
    var s = "hello"
    s.
    return 0
end 'main'`;

		const completions = await getCompletionsAfterDot(
			validContent,
			incompleteContent,
			new vscode.Position(2, 6) // After "s."
		);

		assert.ok(completions, 'Completions should be returned');

		// Check for string property 'count'
		const hasCount = completions.items.some(item => item.label === 'count');
		assert.ok(hasCount, 'Should include "count" property for string');

		// Check for string property 'isEmpty'
		const hasIsEmpty = completions.items.some(item => item.label === 'isEmpty');
		assert.ok(hasIsEmpty, 'Should include "isEmpty" property for string');
	});

	test('Should provide string method completions after dot', async function () {
		const validContent = `function main() int
    var s = "hello"
    return s.count
end 'main'`;

		const incompleteContent = `function main() int
    var s = "hello"
    s.
    return 0
end 'main'`;

		const completions = await getCompletionsAfterDot(
			validContent,
			incompleteContent,
			new vscode.Position(2, 6)
		);

		assert.ok(completions, 'Completions should be returned');

		// Check for string methods
		const methodNames = ['startsWith', 'endsWith', 'contains', 'find', 'toUpper', 'toLower', 'trimWhitespace'];
		for (const methodName of methodNames) {
			const hasMethod = completions.items.some(item => item.label === methodName);
			assert.ok(hasMethod, `Should include "${methodName}" method for string`);
		}
	});

	test('String method completions should have correct kind', async function () {
		const validContent = `function main() int
    var s = "hello"
    return s.count
end 'main'`;

		const incompleteContent = `function main() int
    var s = "hello"
    s.
    return 0
end 'main'`;

		const completions = await getCompletionsAfterDot(
			validContent,
			incompleteContent,
			new vscode.Position(2, 6)
		);

		// Check that count() is a method (it's a function in stdlib)
		const countItem = completions.items.find(item => item.label === 'count');
		assert.ok(countItem, 'count should be in completions');
		assert.strictEqual(countItem.kind, vscode.CompletionItemKind.Method, 'count should be Method kind');

		// Check that methods have Method kind
		const toUpperItem = completions.items.find(item => item.label === 'toUpper');
		assert.ok(toUpperItem, 'toUpper should be in completions');
		assert.strictEqual(toUpperItem.kind, vscode.CompletionItemKind.Method, 'toUpper should be Method kind');
	});

	test('String method completions should have detail with return type', async function () {
		const validContent = `function main() int
    var s = "hello"
    return s.count
end 'main'`;

		const incompleteContent = `function main() int
    var s = "hello"
    s.
    return 0
end 'main'`;

		const completions = await getCompletionsAfterDot(
			validContent,
			incompleteContent,
			new vscode.Position(2, 6)
		);

		// Check count has int detail
		const countItem = completions.items.find(item => item.label === 'count');
		assert.ok(countItem, 'count should be in completions');
		assert.ok(countItem.detail, 'count should have detail');
		assert.ok((countItem.detail as string).includes('int'), 'count detail should include "int"');

		// Check toUpper has string detail
		const toUpperItem = completions.items.find(item => item.label === 'toUpper');
		assert.ok(toUpperItem, 'toUpper should be in completions');
		assert.ok(toUpperItem.detail, 'toUpper should have detail');
		assert.ok((toUpperItem.detail as string).includes('string'), 'toUpper detail should include "string"');

		// Check find has int detail
		const findItem = completions.items.find(item => item.label === 'find');
		assert.ok(findItem, 'find should be in completions');
		assert.ok(findItem.detail, 'find should have detail');
		assert.ok((findItem.detail as string).includes('int'), 'find detail should include "int"');
	});

	test('String method completions should have documentation', async function () {
		const validContent = `function main() int
    var s = "hello"
    return s.count
end 'main'`;

		const incompleteContent = `function main() int
    var s = "hello"
    s.
    return 0
end 'main'`;

		const completions = await getCompletionsAfterDot(
			validContent,
			incompleteContent,
			new vscode.Position(2, 6)
		);

		const toUpperItem = completions.items.find(item => item.label === 'toUpper');
		assert.ok(toUpperItem, 'toUpper should be in completions');
		assert.ok(toUpperItem.documentation, 'toUpper should have documentation');

		const containsItem = completions.items.find(item => item.label === 'contains');
		assert.ok(containsItem, 'contains should be in completions');
		assert.ok(containsItem.documentation, 'contains should have documentation');
	});

	test('Should provide array member completions after dot', async function () {
		const validContent = `function main() int
    var arr = [5]int
    return arr.length
end 'main'`;

		const incompleteContent = `function main() int
    var arr = [5]int
    arr.
    return 0
end 'main'`;

		const completions = await getCompletionsAfterDot(
			validContent,
			incompleteContent,
			new vscode.Position(2, 8) // After "arr."
		);

		assert.ok(completions, 'Completions should be returned');

		// Check for array properties
		const hasLength = completions.items.some(item => item.label === 'length');
		assert.ok(hasLength, 'Should include "length" property for array');
	});

	test('Array length should have Property kind and int detail', async function () {
		const validContent = `function main() int
    var arr = [5]int
    return arr.length
end 'main'`;

		const incompleteContent = `function main() int
    var arr = [5]int
    arr.
    return 0
end 'main'`;

		const completions = await getCompletionsAfterDot(
			validContent,
			incompleteContent,
			new vscode.Position(2, 8)
		);

		const lengthItem = completions.items.find(item => item.label === 'length');
		assert.ok(lengthItem, 'length should be in completions');
		assert.strictEqual(lengthItem.kind, vscode.CompletionItemKind.Property, 'length should be Property kind');
		assert.ok(lengthItem.detail, 'length should have detail');
		assert.ok((lengthItem.detail as string).includes('int'), 'length detail should include "int"');
	});

	test('Should provide struct field completions after dot', async function () {
		const validContent = `struct Point
    var x int
    var y int
end 'Point'

function main() int
    var p = Point { x: 0, y: 0 }
    return p.x
end 'main'`;

		const incompleteContent = `struct Point
    var x int
    var y int
end 'Point'

function main() int
    var p = Point { x: 0, y: 0 }
    p.
    return 0
end 'main'`;

		const completions = await getCompletionsAfterDot(
			validContent,
			incompleteContent,
			new vscode.Position(7, 6) // After "p."
		);

		assert.ok(completions, 'Completions should be returned');

		// Check for struct fields
		const hasX = completions.items.some(item => item.label === 'x');
		assert.ok(hasX, 'Should include "x" field for Point struct');

		const hasY = completions.items.some(item => item.label === 'y');
		assert.ok(hasY, 'Should include "y" field for Point struct');
	});

	test('Struct field completions should have Field kind', async function () {
		const validContent = `struct Point
    var x int
    var y int
end 'Point'

function main() int
    var p = Point { x: 0, y: 0 }
    return p.x
end 'main'`;

		const incompleteContent = `struct Point
    var x int
    var y int
end 'Point'

function main() int
    var p = Point { x: 0, y: 0 }
    p.
    return 0
end 'main'`;

		const completions = await getCompletionsAfterDot(
			validContent,
			incompleteContent,
			new vscode.Position(7, 6)
		);

		const xItem = completions.items.find(item => item.label === 'x');
		assert.ok(xItem, 'x should be in completions');
		assert.strictEqual(xItem.kind, vscode.CompletionItemKind.Field, 'x should be Field kind');
		assert.ok(xItem.detail, 'x should have detail');
		assert.strictEqual(xItem.detail, 'int', 'x detail should be "int"');
	});

	test('String method with parentheses should have insertText', async function () {
		const validContent = `function main() int
    var s = "hello"
    return s.count
end 'main'`;

		const incompleteContent = `function main() int
    var s = "hello"
    s.
    return 0
end 'main'`;

		// Poll for completions until we get string methods (toUpper may take time to appear)
		let toUpperItem: vscode.CompletionItem | undefined;
		for (let attempt = 0; attempt < 5; attempt++) {
			const completions = await getCompletionsAfterDot(
				validContent,
				incompleteContent,
				new vscode.Position(2, 6)
			);
			toUpperItem = completions.items.find(item => item.label === 'toUpper');
			if (toUpperItem) {
				break;
			}
		}

		// Check that methods have insertText with parentheses
		assert.ok(toUpperItem, 'toUpper should be in completions');
		assert.ok(toUpperItem.insertText, 'toUpper should have insertText');
		const insertText = typeof toUpperItem.insertText === 'string'
			? toUpperItem.insertText
			: toUpperItem.insertText.value;
		assert.ok(insertText.includes('()'), 'toUpper insertText should include parentheses');
	});
});
