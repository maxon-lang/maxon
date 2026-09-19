import * as assert from 'assert';
import { extensionId, maxonExtension } from './fixtures';

suite('Language Configuration Test Suite', () => {
    test('Should have correct file extensions', () => {
        const packageJSON = maxonExtension().packageJSON;
        assert.ok(packageJSON);

        const languages = packageJSON.contributes?.languages;
        assert.ok(languages);
        assert.strictEqual(languages.length, 1);
        assert.strictEqual(languages[0].id, 'maxon');
        assert.ok(languages[0].extensions.includes('.maxon'));
    });

    test('Should have grammar definition', () => {
        const grammars = maxonExtension().packageJSON.contributes?.grammars;

        assert.ok(grammars);
        assert.strictEqual(grammars.length, 1);
        assert.strictEqual(grammars[0].language, 'maxon');
        assert.strictEqual(grammars[0].scopeName, 'source.maxon');
    });

    test('Should have language configuration file', () => {
        const languages = maxonExtension().packageJSON.contributes?.languages;

        assert.ok(languages[0].configuration);
        assert.strictEqual(languages[0].configuration, './language-configuration.json');
    });
});

suite('Extension Metadata Test Suite', () => {
    test('Should have correct extension ID', () => {
        assert.strictEqual(maxonExtension().id, extensionId);
    });

    test('Should have display name and description', () => {
        const packageJSON = maxonExtension().packageJSON;

        assert.strictEqual(packageJSON.displayName, 'Maxon');
        assert.ok(packageJSON.description);
    });

    test('Should have version number', () => {
        const packageJSON = maxonExtension().packageJSON;

        assert.ok(packageJSON.version);
        assert.match(packageJSON.version, /^\d+\.\d+\.\d+$/);
    });

    test('Should be in Programming Languages category', () => {
        const packageJSON = maxonExtension().packageJSON;

        assert.ok(packageJSON.categories);
        assert.ok(packageJSON.categories.includes('Programming Languages'));
    });
});
