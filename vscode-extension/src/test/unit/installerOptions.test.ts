import * as assert from 'assert';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { claudePluginFlag, firstRunnable, installMessages, installRoot, isInstalledIn } from '../../installerOptions';

suite('installer options', () => {
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

	test('a compiler counts as installed only from the bin directory of the install', () => {
		const scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'maxon-install-'));
		try {
			const root = path.join(scratch, '.maxon');
			const elsewhere = path.join(scratch, 'elsewhere');
			fs.mkdirSync(path.join(root, 'bin'), { recursive: true });
			fs.mkdirSync(elsewhere);
			for (const executable of [path.join(root, 'bin', 'maxon'), path.join(root, 'maxon'), path.join(elsewhere, 'maxon')]) {
				fs.writeFileSync(executable, '');
			}

			assert.ok(isInstalledIn(path.join(root, 'bin', 'maxon'), root));
			assert.ok(!isInstalledIn(path.join(elsewhere, 'maxon'), root));
			assert.ok(!isInstalledIn(path.join(root, 'maxon'), root));
			assert.ok(!isInstalledIn(path.join(elsewhere, 'maxon'), elsewhere));
			if (process.platform === 'win32') {
				assert.ok(isInstalledIn(path.join(root, 'BIN', 'maxon'), root.toUpperCase()));
			}
		} finally {
			fs.rmSync(scratch, { recursive: true, force: true });
		}
	});

	test('every install message names the install directory', () => {
		const root = '/home/a/.maxon';
		const messages = installMessages(root);
		for (const [name, text] of Object.entries<string>({ ...messages })) {
			assert.ok(text.includes(root), `${name} does not name ${root}: ${text}`);
		}
		assert.deepStrictEqual(Object.keys(messages).sort(), ['failed', 'finishedWithoutCompiler', 'installed', 'progress', 'prompt']);
	});
});
