import * as assert from 'assert';
import * as vscode from 'vscode';
import * as myExtension from '../../extension';
import { activateMaxonExtension, maxonExtension, openFixture, stageProject } from './fixtures';

suite('Extension Test Suite', () => {
	vscode.window.showInformationMessage('Start all tests.');

	test('Extension should be present', () => {
		assert.ok(maxonExtension());
	});

	test('Extension should activate', async function () {
		this.timeout(90000);

		const ext = await activateMaxonExtension();
		assert.strictEqual(ext.isActive, true);
	});

	test('Maxon language should be registered', () => {
		const languages = vscode.languages.getLanguages();
		return languages.then(langs => {
			assert.ok(langs.includes('maxon'), 'Maxon language should be registered');
		});
	});

	test('Extension exports activate and deactivate functions', () => {
		assert.strictEqual(typeof myExtension.activate, 'function');
		assert.strictEqual(typeof myExtension.deactivate, 'function');
	});
});

suite('Language Client Test Suite', () => {
	test('Should handle .maxon file extensions', async () => {
		const dir = stageProject('extension-language-id', {
			'main.maxon': "function main() returns ExitCode\n\treturn 0\nend 'main'\n"
		});

		const doc = await openFixture(dir, 'main.maxon');
		assert.strictEqual(doc.languageId, 'maxon', 'Document should be identified as Maxon language');
	});

	test('Language client should support file scheme', async () => {
		const dir = stageProject('extension-file-scheme', {
			'main.maxon': "function main() returns ExitCode\n\treturn 0\nend 'main'\n"
		});

		// The selector the extension hands the language client, asked of a real file on disk.
		const doc = await openFixture(dir, 'main.maxon');
		const matched = vscode.languages.match({ scheme: 'file', language: 'maxon', pattern: '**/*.maxon' }, doc);

		assert.ok(matched > 0, 'a .maxon file on disk should match the language client selector');
	});
});

suite('Deactivation Test Suite', () => {
	// ⚠ This is NOT the module VS Code activated. The manifest's `main` is the esbuild bundle in
	// `dist/`, and a test imports `out/` — two instances, so this one holds no client and stopping it
	// leaves the running server alone. That is what makes the no-client path testable at all.
	test('Deactivate should handle no client gracefully', () => {
		assert.strictEqual(myExtension.deactivate(), undefined);
	});
});
