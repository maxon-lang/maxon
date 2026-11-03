import * as assert from 'assert';
import { claudePluginFlag, firstRunnable } from '../../installerOptions';

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
});
