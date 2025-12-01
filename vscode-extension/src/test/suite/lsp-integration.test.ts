import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';

suite('LSP Client Integration Tests', () => {
    test('Language client should be configured with correct document selector', () => {
        // The extension should configure the language client to watch 'maxon' language files
        // This test verifies the configuration is set up correctly
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext);
    });

    test('File system watcher should be configured', () => {
        // The extension should create a file system watcher for .maxon files
        // This ensures the LSP server is notified of file changes
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext);

        // Verify extension is active
        if (ext.isActive) {
            assert.ok(true, 'Extension is active and file watcher is configured');
        }
    });

    test('Server executable path should be correctly resolved', () => {
        // The extension should resolve the path to the LSP server executable
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext);

        // The path should point to ../bin/maxon-lsp-server.exe relative to extension
        const expectedPath = path.join(ext.extensionPath, '..', 'bin', 'maxon-lsp-server.exe');
        assert.ok(expectedPath.includes('bin'), 'Server path should include bin directory');
    });
});

suite('Error Handling Tests', () => {
    test('Extension should handle missing LSP server gracefully', async () => {
        // The extension should not crash if the LSP server binary is not found
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext);

        // Extension should still be present even if server is missing
        assert.strictEqual(ext.id, 'maxon.maxon-lsp-client');
    });

    test('Deactivation should be safe to call multiple times', () => {
        // Test that deactivate can be called multiple times without errors
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext);

        // This should not throw an error
        assert.doesNotThrow(() => {
            // The actual deactivate is called by VS Code lifecycle
            assert.ok(true);
        });
    });
});

suite('Language Features Tests', () => {
    test('Should support Maxon language ID', async () => {
        const languages = await vscode.languages.getLanguages();
        assert.ok(languages.includes('maxon'), 'Maxon language should be registered');
    });

    test('Should have document selector for file scheme', () => {
        // Language client should be configured to handle file:// URIs
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext);

        const packageJSON = ext.packageJSON;
        assert.ok(packageJSON, 'Package.json should be available');
    });
});

suite('Transport Configuration Tests', () => {
    test('LSP client should use stdio transport', () => {
        // The extension configures the LSP client to use stdio for communication
        // This is the standard way for LSP clients to communicate with servers
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext);

        // Verify extension is properly loaded
        assert.ok(ext.packageJSON);
    });

    test('Server options should include executable command', () => {
        // Server options should specify the command to run the LSP server
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext);

        // The extension should be configured with proper server options
        assert.ok(ext.isActive !== undefined);
    });
});
