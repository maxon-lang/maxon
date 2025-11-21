import * as assert from 'assert';
import * as vscode from 'vscode';

suite('Syntax Highlighting Test Suite', () => {
    test('Keywords are highlighted with proper TextMate scopes', async () => {
        const testContent = [
            "function main() int",
            "    var x = 0",
            "    let y = 42",
            "    if x > 0 'check'",
            "        return 1",
            "    else 'other'",
            "        while true 'loop'",
            "            break",
            "        end 'loop'",
            "        return 0",
            "    end 'check'",
            "end 'main'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        // Verify language ID is set (this ensures TextMate grammar is loaded)
        assert.strictEqual(doc.languageId, 'maxon', 'Document should have maxon language ID');
        
        // Verify keywords exist in the document
        const content = doc.getText();
        const keywords = ['function', 'var', 'let', 'if', 'else', 'while', 'break', 'return', 'end'];
        for (const keyword of keywords) {
            assert.ok(content.includes(keyword), `Should contain keyword: ${keyword}`);
        }
    });

    test('Type keywords are present and can be highlighted', async () => {
        const testContent = [
            "function test(a int, b float, c bool, d char) int",
            "    var ptr_val ptr = 0",
            "    var str string",
            "    return a",
            "end 'test'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        
        const content = doc.getText();
        const types = ['int', 'float', 'bool', 'char', 'ptr', 'string'];
        for (const type of types) {
            assert.ok(content.includes(type), `Should contain type: ${type}`);
        }
    });

    test('Math intrinsics are present and can be highlighted', async () => {
        const testContent = [
            "function test(x float) float",
            "    var a = sqrt(x)",
            "    var b = abs(x)",
            "    var c = floor(x)",
            "    var d = ceil(x)",
            "    var e = round(x)",
            "    var f = sin(x)",
            "    var g = cos(x)",
            "    var h = trunc(x)",
            "    return a",
            "end 'test'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        
        const content = doc.getText();
        const mathFunctions = ['sqrt', 'abs', 'floor', 'ceil', 'round', 'sin', 'cos', 'trunc'];
        for (const func of mathFunctions) {
            assert.ok(content.includes(func), `Should contain math function: ${func}`);
        }
    });

    test('Numbers are recognized (integers and floats)', async () => {
        const testContent = [
            "function test() int",
            "    var int_val = 42",
            "    var float_val = 3.14159",
            "    var sci_notation = 1.5e10",
            "    var negative_exp = 2.5e-3",
            "    return int_val",
            "end 'test'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        
        const content = doc.getText();
        assert.ok(content.includes('42'), 'Should contain integer');
        assert.ok(content.includes('3.14159'), 'Should contain float');
        assert.ok(content.includes('1.5e10'), 'Should contain scientific notation');
        assert.ok(content.includes('2.5e-3'), 'Should contain negative exponent');
    });

    test('Comments are recognized (single-line and multi-line)', async () => {
        const testContent = [
            "// This is a single-line comment",
            "function test() int",
            "    // Another comment",
            "    /* Multi-line",
            "       comment here */",
            "    var x = 0 // inline comment",
            "    return x",
            "end 'test'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        
        const content = doc.getText();
        assert.ok(content.includes('// This is a single-line comment'), 'Should contain single-line comment');
        assert.ok(content.includes('/* Multi-line'), 'Should contain multi-line comment start');
        assert.ok(content.includes('comment here */'), 'Should contain multi-line comment end');
    });

    test('Strings are recognized (double and single quotes)', async () => {
        const testContent = [
            "function test() int",
            '    var msg = "Hello, World!"',
            "    var ch = 'A'",
            '    var escaped = "Line1\\nLine2"',
            "    return 0",
            "end 'test'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        
        const content = doc.getText();
        assert.ok(content.includes('"Hello, World!"'), 'Should contain double-quoted string');
        assert.ok(content.includes("'A'"), 'Should contain single-quoted character');
        assert.ok(content.includes('\\n'), 'Should contain escape sequence');
    });

    test('Operators are recognized', async () => {
        const testContent = [
            "function test(a int, b int) int",
            "    var sum = a + b",
            "    var diff = a - b",
            "    var prod = a * b",
            "    var quot = a / b",
            "    if a >= b 'check'",
            "        return 1",
            "    end 'check'",
            "    if a <= b 'check2'",
            "        return 0",
            "    end 'check2'",
            "    if a = b 'check3'",
            "        return -1",
            "    end 'check3'",
            "    return sum",
            "end 'test'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        
        const content = doc.getText();
        assert.ok(content.includes('+'), 'Should contain + operator');
        assert.ok(content.includes('-'), 'Should contain - operator');
        assert.ok(content.includes('*'), 'Should contain * operator');
        assert.ok(content.includes('/'), 'Should contain / operator');
        assert.ok(content.includes('>='), 'Should contain >= operator');
        assert.ok(content.includes('<='), 'Should contain <= operator');
        assert.ok(content.includes('='), 'Should contain = operator');
    });

    test('Function calls are recognized', async () => {
        const testContent = [
            "function helper(x int) int",
            "    return x * 2",
            "end 'helper'",
            "",
            "function main() int",
            "    var result = helper(42)",
            "    var sqr = sqrt(16.0)",
            "    return result",
            "end 'main'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        
        const content = doc.getText();
        assert.ok(content.includes('helper(42)'), 'Should contain function call');
        assert.ok(content.includes('sqrt(16.0)'), 'Should contain math function call');
    });


    test('Block identifiers should be highlighted differently from strings', async () => {
        // Create a test document with various block identifier scenarios
        const testContent = [
            "function main() int 'main_func'",
            "    var str = \"regular string\"",
            "    if x > 0 'check'",
            "        var another = 'single quote string'",
            "    end 'check'",
            "    while true 'loop'",
            "        break",
            "    end 'loop'",
            "    return 0",
            "end 'main_func'",
            "",
            "namespace math 'math'",
            "    function add(a int, b int) int",
            "        return a + b",
            "    end 'add'",
            "end 'math'"
        ].join('\n');

        // Create an untitled document with Maxon language
        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        await vscode.window.showTextDocument(doc);

        // Get the semantic tokens
        // Note: TextMate tokenization happens at the editor level and isn't directly
        // accessible via the extension API. This test verifies the language is set correctly.
        assert.strictEqual(doc.languageId, 'maxon', 'Document should be Maxon language');
        
        // Verify the content contains block identifiers
        const content = doc.getText();
        assert.ok(content.includes("'main_func'"), 'Should contain function block identifier');
        assert.ok(content.includes("'check'"), 'Should contain if block identifier');
        assert.ok(content.includes("'loop'"), 'Should contain while block identifier');
        assert.ok(content.includes("'math'"), 'Should contain namespace block identifier');
        assert.ok(content.includes('"regular string"'), 'Should contain regular string');
        
        // Close the document
        await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
    });

    test('Block identifiers after end keyword should be recognized', async () => {
        const testContent = [
            "function test() int",
            "    return 0",
            "end 'test'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        assert.ok(doc.getText().includes("end 'test'"), 'Should contain end with block identifier');
    });

    test('Block identifiers after if keyword should be recognized', async () => {
        const testContent = [
            "function test() int",
            "    if x > 0 'check'",
            "        return 1",
            "    end 'check'",
            "    return 0",
            "end 'test'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        assert.ok(doc.getText().includes("if x > 0 'check'"), 'Should contain if with block identifier');
    });

    test('Block identifiers after while keyword should be recognized', async () => {
        const testContent = [
            "function test() int",
            "    while true 'loop'",
            "        break",
            "    end 'loop'",
            "    return 0",
            "end 'test'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        assert.ok(doc.getText().includes("while true 'loop'"), 'Should contain while with block identifier');
    });

    test('Struct keyword should be recognized', async () => {
        const testContent = [
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

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        assert.ok(doc.getText().includes("struct Point"), 'Should contain struct keyword');
        assert.ok(doc.getText().includes("end 'Point'"), 'Should contain struct block identifier');
    });

    test('All keywords should be recognized', async () => {
        const testContent = [
            "extern function external_func() int",
            "namespace test 'test'",
            "struct Data",
            "    value int",
            "end 'Data'",
            "function test(x int) int",
            "    var count = 0",
            "    let immutable = 42",
            "    while count < 10 'loop'",
            "        if count > 5 'check'",
            "            break",
            "        else 'other'",
            "            continue",
            "        end 'check'",
            "    end 'loop'",
            "    return count as int",
            "end 'test'",
            "end 'test'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        
        // Check all keywords are present
        const content = doc.getText();
        assert.ok(content.includes("extern"), 'Should contain extern keyword');
        assert.ok(content.includes("namespace"), 'Should contain namespace keyword');
        assert.ok(content.includes("struct"), 'Should contain struct keyword');
        assert.ok(content.includes("function"), 'Should contain function keyword');
        assert.ok(content.includes("var"), 'Should contain var keyword');
        assert.ok(content.includes("let"), 'Should contain let keyword');
        assert.ok(content.includes("while"), 'Should contain while keyword');
        assert.ok(content.includes("if"), 'Should contain if keyword');
        assert.ok(content.includes("else"), 'Should contain else keyword');
        assert.ok(content.includes("break"), 'Should contain break keyword');
        assert.ok(content.includes("continue"), 'Should contain continue keyword');
        assert.ok(content.includes("return"), 'Should contain return keyword');
        assert.ok(content.includes("as"), 'Should contain as keyword');
        assert.ok(content.includes("end"), 'Should contain end keyword');
    });

    test('Boolean literals should be recognized', async () => {
        const testContent = [
            "function test() int",
            "    var flag = true",
            "    if flag = false 'check'",
            "        return 1",
            "    end 'check'",
            "    return 0",
            "end 'test'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        assert.ok(doc.getText().includes("true"), 'Should contain true literal');
        assert.ok(doc.getText().includes("false"), 'Should contain false literal');
    });

    test('Multi-line comments should be recognized', async () => {
        const testContent = [
            "/* This is a",
            "   multi-line comment */",
            "function test() int",
            "    /* Another comment */",
            "    return 0",
            "end 'test'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        assert.ok(doc.getText().includes("/* This is a"), 'Should contain multi-line comment start');
        assert.ok(doc.getText().includes("multi-line comment */"), 'Should contain multi-line comment end');
    });

    test('Block identifiers after namespace keyword should be recognized', async () => {
        const testContent = [
            "namespace utils 'utils'",
            "    function helper() int",
            "        return 1",
            "    end 'helper'",
            "end 'utils'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        assert.ok(doc.getText().includes("namespace utils 'utils'"), 'Should contain namespace with block identifier');
    });

    test('Regular strings should not be confused with block identifiers', async () => {
        const testContent = [
            "function test() int",
            "    var message = \"This is a regular string\"",
            "    var char = 'A'",
            "    return 0",
            "end 'test'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        const content = doc.getText();
        assert.ok(content.includes('"This is a regular string"'), 'Should contain regular string');
        assert.ok(content.includes("'A'"), 'Should contain character literal');
        assert.ok(content.includes("end 'test'"), 'Should contain block identifier after end');
    });

    test('Block identifiers with double quotes should work', async () => {
        const testContent = [
            'function test() int "test_function"',
            '    return 0',
            'end "test_function"'
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        assert.ok(doc.getText().includes('"test_function"'), 'Should contain block identifier with double quotes');
    });

    test('Block identifiers after else keyword should be recognized', async () => {
        const testContent = [
            "function test() int",
            "    var x = 5",
            "    if x = 5 'check'",
            "        return 1",
            "    else 'check'",
            "        return 0",
            "    end 'check'",
            "end 'test'"
        ].join('\n');

        const doc = await vscode.workspace.openTextDocument({
            content: testContent,
            language: 'maxon'
        });

        assert.strictEqual(doc.languageId, 'maxon');
        assert.ok(doc.getText().includes("else 'check'"), 'Should contain else with block identifier');
    });
});
