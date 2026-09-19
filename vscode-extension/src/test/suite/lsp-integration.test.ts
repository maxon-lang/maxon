import * as assert from 'assert';
import * as vscode from 'vscode';
import type { LanguageClient } from 'vscode-languageclient/node';
import {
    activateMaxonExtension,
    closeAllEditors,
    diagnosticsFor,
    openFixture,
    stageProject
} from './fixtures';

/**
 * The language server, driven.
 *
 * ⛔ EVERY TEST HERE SENDS A REQUEST AND READS THE ANSWER. Asserting that the extension exists, or
 * that it has a `packageJSON`, proves that VS Code loaded a manifest and nothing about the server —
 * a suite of those stays green through a language feature disappearing entirely.
 */
suite('Language Server Integration', () => {

    /** MEASURED on x64-windows: 253-928 ms per request. `definition.test.ts` states why the deadline is wide. */
    const serverDeadlineMs = 90000;

    let client: LanguageClient;

    suiteSetup(async function () {
        this.timeout(serverDeadlineMs);

        const ext = await activateMaxonExtension();
        const exported = ext.exports as { getClient(): LanguageClient | undefined; } | undefined;
        const running = exported?.getClient();

        assert.ok(running, 'the extension activated without a language client — no compiler was found');
        client = running;
    });

    teardown(async () => {
        await closeAllEditors();
    });

    test('the server handshake advertises the features the editor drives', () => {
        const capabilities = client.initializeResult?.capabilities;
        assert.ok(capabilities, 'the client holds no initialize result, so no handshake completed');

        for (const advertised of [
            'definitionProvider',
            'hoverProvider',
            'completionProvider',
            'documentFormattingProvider',
            'documentSymbolProvider',
            'renameProvider',
            'semanticTokensProvider'
        ]) {
            assert.ok(
                (capabilities as Record<string, unknown>)[advertised],
                `the server does not advertise ${advertised}`
            );
        }
    });

    test('a call to a name that exists nowhere is reported as a diagnostic', async function () {
        this.timeout(serverDeadlineMs);

        const dir = stageProject('lsp-diagnostics', {
            'main.maxon': "function main() returns ExitCode\n\treturn thereIsNoSuchCallee()\nend 'main'\n"
        });
        const doc = await openFixture(dir, 'main.maxon');

        const published = await diagnosticsFor(doc.uri, serverDeadlineMs / 2);

        assert.ok(published.length > 0, 'the server published no diagnostic for an undefined callee');
        assert.ok(
            published.some(one => one.message.includes('thereIsNoSuchCallee')),
            `no diagnostic names the undefined callee: ${published.map(one => one.message).join(' | ')}`
        );
        assert.ok(
            published.some(one => one.severity === vscode.DiagnosticSeverity.Error),
            'an undefined callee is an error, not a hint'
        );
    });

    test('hovering a declaration answers with its signature', async function () {
        this.timeout(serverDeadlineMs);

        const dir = stageProject('lsp-hover', {
            'main.maxon': "function main() returns ExitCode\n\tprint(\"42\\n\")\n\treturn 0\nend 'main'\n"
        });
        const doc = await openFixture(dir, 'main.maxon');

        const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
            'vscode.executeHoverProvider',
            doc.uri,
            new vscode.Position(0, 10)
        );

        assert.ok(hovers && hovers.length > 0, 'the server answered no hover over a function declaration');

        const text = hovers
            .flatMap(one => one.contents)
            .map(part => typeof part === 'string' ? part : part.value)
            .join('\n');
        assert.match(
            text,
            /function main\(\) returns ExitCode/,
            `the hover does not render the declaration it is over: ${text}`
        );
    });

    test('formatting answers with the edits that re-indent a file', async function () {
        this.timeout(serverDeadlineMs);

        const dir = stageProject('lsp-formatting', {
            'main.maxon': "function main() returns ExitCode\n        return 0\nend 'main'\n"
        });
        const doc = await openFixture(dir, 'main.maxon');

        const edits = await vscode.commands.executeCommand<vscode.TextEdit[]>(
            'vscode.executeFormatDocumentProvider',
            doc.uri,
            { insertSpaces: false, tabSize: 2 }
        );

        assert.ok(edits && edits.length > 0, 'the server answered no edits for an over-indented file');
        assert.ok(
            edits.some(one => one.newText.includes('\t')),
            `no edit indents with a tab: ${JSON.stringify(edits.map(one => one.newText))}`
        );
    });

    test('document symbols list what a file declares', async function () {
        this.timeout(serverDeadlineMs);

        const dir = stageProject('lsp-symbols', {
            'main.maxon': "type Point\n\tvar x int\nend 'Point'\n\nfunction main() returns ExitCode\n\treturn 0\nend 'main'\n"
        });
        const doc = await openFixture(dir, 'main.maxon');

        const symbols = await vscode.commands.executeCommand<(vscode.DocumentSymbol | vscode.SymbolInformation)[]>(
            'vscode.executeDocumentSymbolProvider',
            doc.uri
        );

        assert.ok(symbols && symbols.length > 0, 'the server listed no symbols for a file declaring two things');

        const names = symbols.map(one => one.name);
        assert.ok(names.includes('main'), `the declared function is missing from ${JSON.stringify(names)}`);
        assert.ok(names.includes('Point'), `the declared type is missing from ${JSON.stringify(names)}`);
    });
});
