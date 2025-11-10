import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';

suite('Language Configuration Test Suite', () => {
    test('Should have correct file extensions', () => {
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext);
        
        const packageJSON = ext?.packageJSON;
        assert.ok(packageJSON);
        
        const languages = packageJSON.contributes?.languages;
        assert.ok(languages);
        assert.strictEqual(languages.length, 1);
        assert.strictEqual(languages[0].id, 'maxon');
        assert.ok(languages[0].extensions.includes('.maxon'));
    });

    test('Should have grammar definition', () => {
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext);
        
        const packageJSON = ext?.packageJSON;
        const grammars = packageJSON.contributes?.grammars;
        
        assert.ok(grammars);
        assert.strictEqual(grammars.length, 1);
        assert.strictEqual(grammars[0].language, 'maxon');
        assert.strictEqual(grammars[0].scopeName, 'source.maxon');
    });

    test('Should have language configuration file', () => {
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext);
        
        const packageJSON = ext?.packageJSON;
        const languages = packageJSON.contributes?.languages;
        
        assert.ok(languages[0].configuration);
        assert.strictEqual(languages[0].configuration, './language-configuration.json');
    });
});

suite('Extension Metadata Test Suite', () => {
    test('Should have correct extension ID', () => {
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext);
        assert.strictEqual(ext.id, 'maxon.maxon-lsp-client');
    });

    test('Should have display name and description', () => {
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext);
        
        const packageJSON = ext?.packageJSON;
        assert.ok(packageJSON.displayName);
        assert.ok(packageJSON.description);
        assert.strictEqual(packageJSON.displayName, 'Maxon Language Support');
    });

    test('Should have version number', () => {
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext);
        
        const packageJSON = ext?.packageJSON;
        assert.ok(packageJSON.version);
        assert.match(packageJSON.version, /^\d+\.\d+\.\d+$/);
    });

    test('Should be in Programming Languages category', () => {
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext);
        
        const packageJSON = ext?.packageJSON;
        assert.ok(packageJSON.categories);
        assert.ok(packageJSON.categories.includes('Programming Languages'));
    });
});
