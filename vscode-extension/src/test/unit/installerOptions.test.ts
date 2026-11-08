import * as assert from 'assert';
import * as path from 'path';
import { claudePluginFlag, firstRunnable, installMessages, installRoot, installRootUnavailable, installedCompilerPath, parsePrintedRoot, printRootFlag } from '../../installerOptions';

suite('installer options', () => {
	test('the print-root flag is the installer flag of its platform', () => {
		assert.strictEqual(printRootFlag(false), '--print-root');
		assert.strictEqual(printRootFlag(true), '-PrintRoot');
	});

	test('the directory the installer printed is its one line, trimmed', () => {
		assert.strictEqual(parsePrintedRoot('/home/a/.maxon\n'), '/home/a/.maxon');
		assert.strictEqual(parsePrintedRoot('C:\\Users\\a\\.maxon\r\n'), 'C:\\Users\\a\\.maxon');
		assert.strictEqual(parsePrintedRoot('\n  /opt/maxon  \n\n\n'), '/opt/maxon');
	});

	test('output that is not exactly one directory is refused', () => {
		assert.throws(() => parsePrintedRoot(''), /printed 0 lines/);
		assert.throws(() => parsePrintedRoot('\r\n  \n'), /printed 0 lines/);
		assert.throws(() => parsePrintedRoot('noise\n/opt/maxon\n'), /printed 2 lines/);
	});

	test('when the installer cannot be asked, the prompt names the failure and the directory the extension expects', () => {
		const prompt = installRootUnavailable('/home/a/.maxon', 'could not download https://maxon.dev/install.sh');
		assert.ok(prompt.includes('/home/a/.maxon'), prompt);
		assert.ok(prompt.includes('could not download https://maxon.dev/install.sh'), prompt);
	});

	test('each Claude plugin choice is the installer flag of its platform', () => {
		assert.strictEqual(claudePluginFlag(false, 'install'), '--claude-plugin');
		assert.strictEqual(claudePluginFlag(false, 'skip'), '--no-claude-plugin');
		assert.strictEqual(claudePluginFlag(true, 'install'), '-ClaudePlugin');
		assert.strictEqual(claudePluginFlag(true, 'skip'), '-NoClaudePlugin');
	});

	test('on Windows the first match a process can be started from is chosen', () => {
		const where = 'C:\\npm\\claude\r\nC:\\npm\\claude.cmd\r\nC:\\other\\claude.exe\r\n';
		assert.strictEqual(firstRunnable(where, true), 'C:\\npm\\claude.cmd');
		assert.strictEqual(firstRunnable('C:\\Users\\a.b\\claude\r\n', true), undefined);
	});

	test('elsewhere the first match is chosen', () => {
		assert.strictEqual(firstRunnable('/usr/local/bin/maxon\n/opt/bin/maxon\n', false), '/usr/local/bin/maxon');
		assert.strictEqual(firstRunnable('', false), undefined);
	});

	test('the install directory is MAXON_INSTALL, else .maxon in the home directory', () => {
		const custom = path.resolve('/opt/maxon');
		assert.strictEqual(installRoot({ MAXON_INSTALL: custom }, '/home/a'), custom);
		assert.strictEqual(installRoot({ MAXON_INSTALL: custom + path.sep }, '/home/a'), custom);
		assert.strictEqual(installRoot({ MAXON_INSTALL: 'relative' }, '/home/a'), 'relative');
		const home = path.resolve('/home/a');
		assert.strictEqual(installRoot({}, home), path.join(home, '.maxon'));
		assert.strictEqual(installRoot({ MAXON_INSTALL: '' }, home), path.join(home, '.maxon'));
	});

	test('an install keeps its compiler in its bin directory', () => {
		const root = path.resolve('/home/a/.maxon');
		assert.strictEqual(installedCompilerPath(root, 'maxon.exe'), path.join(root, 'bin', 'maxon.exe'));
	});

	test('every install message names the install directory', () => {
		const root = '/home/a/.maxon';
		const messages = installMessages(root);
		for (const [name, text] of Object.entries<string>({ ...messages })) {
			assert.ok(text.includes(root), `${name} does not name ${root}: ${text}`);
		}
		assert.deepStrictEqual(Object.keys(messages).sort(), ['failed', 'finishedWithoutCompiler', 'installed', 'installedWithErrors', 'progress', 'prompt']);
		assert.ok(!messages.installedWithErrors.includes('failed'), messages.installedWithErrors);
		assert.ok(messages.installedWithErrors.includes('error'), messages.installedWithErrors);
	});
});
