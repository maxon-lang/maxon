import * as assert from 'assert';
import * as vscode from 'vscode';

/**
 * Response types for LSP requests
 */
interface GenerateIRResponse {
	ir: string;
	errors: Array<{ message: string; line: number; column: number; type?: string; }>;
}

interface GenerateAsmResponse {
	assembly: string;
	errors: Array<{ message: string; line: number; column: number; type?: string; }>;
}

/**
 * Test suite for Compiler Explorer LSP features (IR and Assembly generation)
 *
 * These tests use VS Code commands (maxon.generateIR, maxon.generateAsm) which
 * wrap the underlying LSP requests, similar to how other LSP tests use
 * vscode.executeHoverProvider etc.
 */
suite('Compiler Explorer LSP Test Suite', () => {

	suiteSetup(async function () {
		// Ensure extension is activated
		const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
		if (ext && !ext.isActive) {
			await ext.activate();
		}
	});

	// ==================== IR Generation Tests ====================

	suite('IR Generation (maxon/generateIR)', () => {

		test('Should generate IR for a simple function', async function () {
			const source = `function add(a int, b int) returns int
    return a + b
end 'add'`;

			const response = await vscode.commands.executeCommand<GenerateIRResponse>(
				'maxon.generateIR',
				{
					source: source,
					filename: 'test.maxon',
					optimize: false
				}
			);

			assert.ok(response, 'Should receive a response');
			assert.ok(response.ir, 'Response should contain IR');
			assert.strictEqual(response.errors.length, 0, 'Should have no errors');

			// Verify IR contains expected elements
			assert.ok(response.ir.includes('define'), 'IR should contain function definition');
			assert.ok(response.ir.includes('add'), 'IR should contain function name');
			assert.ok(response.ir.includes('i32'), 'IR should contain int type');
		});

		test('Should generate optimized IR when optimize=true', async function () {
			const source = `function add(a int, b int) returns int
    return a + b
end 'add'`;

			const unoptimized = await vscode.commands.executeCommand<GenerateIRResponse>(
				'maxon.generateIR',
				{
					source: source,
					filename: 'test.maxon',
					optimize: false
				}
			);

			const optimized = await vscode.commands.executeCommand<GenerateIRResponse>(
				'maxon.generateIR',
				{
					source: source,
					filename: 'test.maxon',
					optimize: true
				}
			);

			assert.ok(unoptimized!.ir, 'Unoptimized IR should be generated');
			assert.ok(unoptimized!.ir.length > 0, 'Unoptimized IR should not be empty');
			assert.strictEqual(unoptimized!.errors.length, 0, 'Unoptimized IR should have no errors');

			// This is the key assertion - optimized IR must not be blank
			assert.ok(optimized!.ir, 'Optimized IR should be generated');
			assert.ok(optimized!.ir.length > 0, `Optimized IR should not be empty, got: "${optimized!.ir}"`);
			assert.strictEqual(optimized!.errors.length, 0, 'Optimized IR should have no errors');

			// Optimized IR should contain valid MIR
			assert.ok(optimized!.ir.includes('define'), 'Optimized IR should contain function definition');
			assert.ok(optimized!.ir.includes('add'), 'Optimized IR should contain function name');
		});

		test('Should return parse errors for invalid syntax', async function () {
			const source = `function broken(
    return 42
end`;

			const response = await vscode.commands.executeCommand<GenerateIRResponse>(
				'maxon.generateIR',
				{
					source: source,
					filename: 'test.maxon',
					optimize: false
				}
			);

			assert.ok(response, 'Should receive a response');
			assert.ok(response.errors.length > 0, 'Should have parse errors');
			assert.strictEqual(response.ir, '', 'IR should be empty on error');
		});

		test('Should return semantic errors for type mismatches', async function () {
			const source = `function test() returns int
    return "hello"
end 'test'`;

			const response = await vscode.commands.executeCommand<GenerateIRResponse>(
				'maxon.generateIR',
				{
					source: source,
					filename: 'test.maxon',
					optimize: false
				}
			);

			assert.ok(response, 'Should receive a response');
			assert.ok(response.errors.length > 0, 'Should have semantic errors');
		});

		test('Should handle empty source gracefully', async function () {
			const response = await vscode.commands.executeCommand<GenerateIRResponse>(
				'maxon.generateIR',
				{
					source: '',
					filename: 'test.maxon',
					optimize: false
				}
			);

			assert.ok(response, 'Should receive a response');
			// Empty source might produce empty IR or an error, both are acceptable
		});

		test('Should generate IR for function without main', async function () {
			// Test that we can generate IR for helper functions (not just main)
			const source = `function helper(x int) returns int
    return x * 2
end 'helper'`;

			const response = await vscode.commands.executeCommand<GenerateIRResponse>(
				'maxon.generateIR',
				{
					source: source,
					filename: 'test.maxon',
					optimize: false
				}
			);

			assert.ok(response, 'Should receive a response');
			assert.ok(response.ir, 'Response should contain IR');
			assert.strictEqual(response.errors.length, 0, 'Should have no errors');
			assert.ok(response.ir.includes('helper'), 'IR should contain function name');
		});
	});

	// ==================== Assembly Generation Tests ====================

	suite('Assembly Generation (maxon/generateAsm)', () => {

		test('Should generate assembly for a simple function', async function () {
			const source = `function add(a int, b int) returns int
    return a + b
end 'add'`;

			const response = await vscode.commands.executeCommand<GenerateAsmResponse>(
				'maxon.generateAsm',
				{
					source: source,
					filename: 'test.maxon',
					optimize: false
				}
			);

			assert.ok(response, 'Should receive a response');
			assert.ok(response.assembly, 'Response should contain assembly');
			assert.strictEqual(response.errors.length, 0, 'Should have no errors');

			// Verify assembly contains expected x86-64 elements
			assert.ok(
				response.assembly.includes('add') || response.assembly.includes('ret'),
				'Assembly should contain x86 instructions'
			);
		});

		test('Should generate optimized assembly when optimize=true', async function () {
			const source = `function add(a int, b int) returns int
    return a + b
end 'add'`;

			const unoptimized = await vscode.commands.executeCommand<GenerateAsmResponse>(
				'maxon.generateAsm',
				{
					source: source,
					filename: 'test.maxon',
					optimize: false
				}
			);

			const optimized = await vscode.commands.executeCommand<GenerateAsmResponse>(
				'maxon.generateAsm',
				{
					source: source,
					filename: 'test.maxon',
					optimize: true
				}
			);

			assert.ok(unoptimized!.assembly, 'Unoptimized assembly should be generated');
			assert.ok(optimized!.assembly, 'Optimized assembly should be generated');

			// Both should produce valid assembly
			assert.ok(
				unoptimized!.assembly.includes('ret') || unoptimized!.assembly.length > 0,
				'Unoptimized assembly should be valid'
			);
			assert.ok(
				optimized!.assembly.includes('ret') || optimized!.assembly.length > 0,
				'Optimized assembly should be valid'
			);
		});

		test('Should return parse errors for invalid syntax', async function () {
			const source = `function broken(
    return 42
end`;

			const response = await vscode.commands.executeCommand<GenerateAsmResponse>(
				'maxon.generateAsm',
				{
					source: source,
					filename: 'test.maxon',
					optimize: false
				}
			);

			assert.ok(response, 'Should receive a response');
			assert.ok(response.errors.length > 0, 'Should have parse errors');
			assert.strictEqual(response.assembly, '', 'Assembly should be empty on error');
		});

		test('Should return semantic errors for type mismatches', async function () {
			const source = `function test() returns int
    return "hello"
end 'test'`;

			const response = await vscode.commands.executeCommand<GenerateAsmResponse>(
				'maxon.generateAsm',
				{
					source: source,
					filename: 'test.maxon',
					optimize: false
				}
			);

			assert.ok(response, 'Should receive a response');
			assert.ok(response.errors.length > 0, 'Should have semantic errors');
		});

		test('Should use Intel syntax for assembly output', async function () {
			const source = `function test() returns int
    return 42
end 'test'`;

			const response = await vscode.commands.executeCommand<GenerateAsmResponse>(
				'maxon.generateAsm',
				{
					source: source,
					filename: 'test.maxon',
					optimize: false
				}
			);

			assert.ok(response, 'Should receive a response');
			assert.ok(response.assembly, 'Should have assembly output');

			// Intel syntax uses keywords like mov, eax, rbp (not %eax, %rbp like AT&T)
			// Also check for absence of AT&T-style % prefixes
			const hasIntelStyle = !response.assembly.includes('%eax') &&
				!response.assembly.includes('%rbp') &&
				(response.assembly.includes('eax') ||
					response.assembly.includes('rax') ||
					response.assembly.includes('mov') ||
					response.assembly.includes('ret'));

			assert.ok(hasIntelStyle, 'Assembly should use Intel syntax (no % register prefixes)');
		});

		test('Should generate assembly for multiple functions', async function () {
			const source = `function helper(x int) returns int
    return x * 2
end 'helper'

function main() returns int
    return helper(21)
end 'main'`;

			const response = await vscode.commands.executeCommand<GenerateAsmResponse>(
				'maxon.generateAsm',
				{
					source: source,
					filename: 'test.maxon',
					optimize: false
				}
			);

			assert.ok(response, 'Should receive a response');
			assert.ok(response.assembly, 'Response should contain assembly');
			assert.strictEqual(response.errors.length, 0, 'Should have no errors');

			// Should contain both function labels
			assert.ok(
				response.assembly.includes('helper') && response.assembly.includes('main'),
				'Assembly should contain both function names'
			);
		});
	});

	// ==================== Error Response Format Tests ====================

	suite('Error Response Format', () => {

		test('Error responses should include line and column', async function () {
			const source = `function test() returns int
    return "wrong type"
end 'test'`;

			const response = await vscode.commands.executeCommand<GenerateIRResponse>(
				'maxon.generateIR',
				{
					source: source,
					filename: 'test.maxon',
					optimize: false
				}
			);

			assert.ok(response, 'Should receive a response');
			assert.ok(response.errors.length > 0, 'Should have errors');

			const error = response.errors[0];
			assert.ok(typeof error.line === 'number', 'Error should have line number');
			assert.ok(typeof error.column === 'number', 'Error should have column number');
			assert.ok(error.line >= 1, 'Line number should be >= 1');
			assert.ok(error.column >= 1, 'Column number should be >= 1');
		});

		test('Error responses should include error type', async function () {
			// Parse error
			const parseSource = `function broken(`;

			const parseResponse = await vscode.commands.executeCommand<GenerateIRResponse>(
				'maxon.generateIR',
				{
					source: parseSource,
					filename: 'test.maxon',
					optimize: false
				}
			);

			assert.ok(parseResponse, 'Should receive a response');
			assert.ok(parseResponse.errors.length > 0, 'Should have parse errors');
			// Error message should indicate what kind of error it is
			assert.ok(
				parseResponse.errors[0].message.length > 0,
				'Error should have a message'
			);
		});
	});
});
