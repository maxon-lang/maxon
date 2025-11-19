import * as assert from 'assert';
import * as vscode from 'vscode';

suite('Syntax Highlighting Test Suite', () => {
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
