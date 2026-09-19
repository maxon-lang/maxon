import * as assert from 'assert';
import * as vscode from 'vscode';
import {
    activateMaxonExtension,
    closeAllEditors,
    definitionsAt,
    lineOfFile,
    openFixture,
    slashed,
    stageProject,
    warmUpDefinitions
} from './fixtures';

/**
 * Go to definition, end to end: the editor asks, the real language server answers, and the answer's
 * URI is what the assertion reads.
 *
 * ⭐⭐ THE URI IS THE POINT. A definition that lands on the right LINE of the WRONG file is the
 * regression this file exists to catch, and it is invisible to any assertion that only reads the
 * range. Go-to-definition across files was lost for eight months behind exactly that blind spot.
 */
suite('Go to Definition', () => {

    /**
     * MEASURED on x64-windows: 336-878 ms per case, the first included — a corpus over both library
     * tiers is built for each project root before the first answer comes back. The deadline is wide
     * because a cold CI runner is not this host, and because the default 10 s would be a flake rather
     * than a verdict the day one is slow.
     */
    const corpusDeadlineMs = 90000;

    suiteSetup(async function () {
        this.timeout(corpusDeadlineMs);
        await activateMaxonExtension();
    });

    teardown(async () => {
        await closeAllEditors();
    });

    test('a call to a stdlib function lands in the stdlib file that declares it', async function () {
        this.timeout(corpusDeadlineMs);

        const dir = stageProject('definition-stdlib', {
            'main.maxon': "function main() returns ExitCode\n\tprint(\"42\\n\")\n\treturn 0\nend 'main'\n"
        });
        const doc = await openFixture(dir, 'main.maxon');
        const onPrint = new vscode.Position(1, 3);

        const waited = await warmUpDefinitions(doc, onPrint, corpusDeadlineMs / 2);
        const found = await definitionsAt(doc, onPrint);

        assert.ok(found.length > 0, `no definition for the stdlib call \`print\` after ${waited} ms`);
        assert.ok(
            slashed(found[0].uri.fsPath).endsWith('stdlib/Print.maxon'),
            `\`print\` should resolve into stdlib/Print.maxon, got: ${found[0].uri.fsPath}`
        );
        assert.match(
            lineOfFile(found[0].uri.fsPath, found[0].range.start.line),
            /function print\b/,
            `the jump landed on line ${found[0].range.start.line} of ${found[0].uri.fsPath}, which does not declare \`print\``
        );
    });

    test('a call to a sibling file\'s declaration lands in that sibling', async function () {
        this.timeout(corpusDeadlineMs);

        const dir = stageProject('definition-sibling', {
            'helper.maxon': "export function sharedHelper() returns ExitCode\n\treturn 0\nend 'sharedHelper'\n",
            'main.maxon': "function main() returns ExitCode\n\treturn sharedHelper()\nend 'main'\n"
        });
        const doc = await openFixture(dir, 'main.maxon');
        const onCall = new vscode.Position(1, 12);

        const waited = await warmUpDefinitions(doc, onCall, corpusDeadlineMs / 2);
        const found = await definitionsAt(doc, onCall);

        assert.ok(found.length > 0, `no definition for the sibling call \`sharedHelper\` after ${waited} ms`);
        assert.ok(
            slashed(found[0].uri.fsPath).endsWith('definition-sibling/helper.maxon'),
            `\`sharedHelper\` should resolve into the sibling file, got: ${found[0].uri.fsPath}`
        );
        assert.strictEqual(found[0].range.start.line, 0, 'the declaration is on the sibling\'s first line');
    });

    test('a local variable still lands in the buffer being edited', async function () {
        this.timeout(corpusDeadlineMs);

        const dir = stageProject('definition-local', {
            'main.maxon': "function main() returns ExitCode\n\tvar counter = 0\n\tcounter = counter + 1\n\tprint(\"{counter}\\n\")\n\treturn 0\nend 'main'\n"
        });
        const doc = await openFixture(dir, 'main.maxon');
        const onAssignment = new vscode.Position(2, 2);

        const waited = await warmUpDefinitions(doc, onAssignment, corpusDeadlineMs / 2);
        const found = await definitionsAt(doc, onAssignment);

        assert.ok(found.length > 0, `no definition for the local \`counter\` after ${waited} ms`);
        assert.strictEqual(
            slashed(found[0].uri.fsPath).toLowerCase(),
            slashed(doc.uri.fsPath).toLowerCase(),
            'a local resolves within the file being edited'
        );
        assert.strictEqual(found[0].range.start.line, 1, 'the `var` declaration is on line 1');
    });
});
