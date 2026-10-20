import * as assert from 'assert';
import {
	trustGrantedParams,
	trustInitializationOptions,
	untrustedRefusal,
	UntrustedWorkspaceRefusal,
	WorkspaceTrustNotificationMethod
} from '../../workspaceTrust';

suite('workspace trust', () => {
	test('the initialize options carry the state the editor reports', () => {
		assert.deepStrictEqual(trustInitializationOptions(true), { trustedWorkspace: true });
		assert.deepStrictEqual(trustInitializationOptions(false), { trustedWorkspace: false });
	});

	test('granting trust is notified to the server as trusted', () => {
		assert.strictEqual(WorkspaceTrustNotificationMethod, 'maxon/workspaceTrust');
		assert.deepStrictEqual(trustGrantedParams(), { trusted: true });
	});

	test('an untrusted workspace refuses to run tests or debug, and says how to enable them', () => {
		assert.strictEqual(untrustedRefusal(false), UntrustedWorkspaceRefusal);
		assert.match(UntrustedWorkspaceRefusal, /trusted workspace/);
		assert.match(UntrustedWorkspaceRefusal, /\.maxproj/);
	});

	test('a trusted workspace runs them', () => {
		assert.strictEqual(untrustedRefusal(true), undefined);
	});
});
