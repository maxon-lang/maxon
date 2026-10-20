// The server spells this method again (`LspServer.maxon`'s `MethodWorkspaceTrust`), and nothing
// checks the two agree: a mismatch leaves a granted workspace untrusted until the server restarts.
export const WorkspaceTrustNotificationMethod = 'maxon/workspaceTrust';

export const UntrustedWorkspaceRefusal = 'Maxon runs tests and debug sessions only in a trusted workspace: they compile the workspace\'s code and run it, and its .maxproj files with it. Trust this workspace to enable them.';

export interface TrustInitializationOptions {
	trustedWorkspace: boolean;
}

export interface WorkspaceTrustParams {
	trusted: boolean;
}

export function trustInitializationOptions(isTrusted: boolean): TrustInitializationOptions {
	return { trustedWorkspace: isTrusted };
}

export function trustGrantedParams(): WorkspaceTrustParams {
	return { trusted: true };
}

export function untrustedRefusal(isTrusted: boolean): string | undefined {
	return isTrusted ? undefined : UntrustedWorkspaceRefusal;
}
