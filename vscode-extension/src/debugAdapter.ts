import { randomUUID } from 'crypto';
import * as path from 'path';
import * as vscode from 'vscode';
import { log } from './logger';
import { registerAll } from './registration';
import { holdsProjectFile, isProgramSource } from './unitTestModel';

export const MaxonDebugType = 'maxon';
const LaunchRequest = 'launch';
const DapServerCommand = 'dap-server';
const ExitedEvent = 'exited';
const DebugRunTokenKey = '__maxonDebugRun';

export const NoCompilerMessage = 'No Maxon compiler was found. Set `maxon.serverPath` or install Maxon, then reload the window.';
const NoProgramMessage = 'Nothing to debug: open a .maxon file, open a folder that holds a .maxproj file, or set "program" in launch.json.';

export interface DebugTarget {
	name: string;
	program: string;
	args?: string[];
	cwd?: string;
}

function launchConfiguration(target: DebugTarget): vscode.DebugConfiguration {
	return { type: MaxonDebugType, request: LaunchRequest, ...target };
}

export type DebugRunOutcome =
	| { kind: 'exited'; code: number; }
	| { kind: 'ended'; reason: string; }
	| { kind: 'cancelled'; };

interface PendingDebugRun {
	exitCode?: number;
	refusal?: string;
	session?: vscode.DebugSession;
	stopRequested: boolean;
	finish(outcome: DebugRunOutcome): void;
}

const pendingRuns = new Map<string, PendingDebugRun>();

function pendingRunOf(session: vscode.DebugSession): { token: string; pending: PendingDebugRun; } | undefined {
	const token = session.configuration[DebugRunTokenKey];
	const pending = typeof token === 'string' ? pendingRuns.get(token) : undefined;
	return pending ? { token, pending } : undefined;
}

function endedOutcome(pending: PendingDebugRun): DebugRunOutcome {
	if (pending.exitCode !== undefined) {
		return { kind: 'exited', code: pending.exitCode };
	}

	return { kind: 'ended', reason: pending.refusal ?? 'The debug session ended before the program exited.' };
}

function stopSession(session: vscode.DebugSession): void {
	vscode.debug.stopDebugging(session).then(
		undefined,
		error => log(`Could not stop the debug session '${session.name}': ${error}`)
	);
}

function stopPendingRun(pending: PendingDebugRun): void {
	pending.stopRequested = true;
	if (pending.session) stopSession(pending.session);
}

export async function debugToExit(
	folder: vscode.WorkspaceFolder | undefined,
	target: DebugTarget,
	options: vscode.DebugSessionOptions,
	cancellation: vscode.CancellationToken
): Promise<DebugRunOutcome> {
	if (cancellation.isCancellationRequested) return { kind: 'cancelled' };

	const token = randomUUID();
	const ended = new Promise<DebugRunOutcome>(resolve => pendingRuns.set(token, { stopRequested: false, finish: resolve }));
	const stopOnCancellation = cancellation.onCancellationRequested(() => {
		const pending = pendingRuns.get(token);
		if (pending) stopPendingRun(pending);
	});

	try {
		const outcome = await startedToExit(folder, { ...launchConfiguration(target), [DebugRunTokenKey]: token }, options, token, ended);
		return cancellation.isCancellationRequested ? { kind: 'cancelled' } : outcome;
	} finally {
		stopOnCancellation.dispose();
	}
}

async function startedToExit(
	folder: vscode.WorkspaceFolder | undefined,
	config: vscode.DebugConfiguration,
	options: vscode.DebugSessionOptions,
	token: string,
	ended: Promise<DebugRunOutcome>
): Promise<DebugRunOutcome> {
	let started: boolean;
	try {
		started = await vscode.debug.startDebugging(folder, config, options);
	} catch (error) {
		pendingRuns.delete(token);
		return { kind: 'ended', reason: `VS Code could not start the debug session: ${error}` };
	}

	const pending = pendingRuns.get(token);
	if (!started && pending) {
		pendingRuns.delete(token);
		return { kind: 'ended', reason: pending.refusal ?? 'VS Code did not start the debug session.' };
	}

	return ended;
}

function activeSourceTarget(): DebugTarget | undefined {
	const document = vscode.window.activeTextEditor?.document;
	if (!document || document.uri.scheme !== 'file' || !isProgramSource(document.uri.fsPath)) {
		return undefined;
	}

	return { name: `Debug ${path.basename(document.uri.fsPath)}`, program: document.uri.fsPath };
}

function projectTarget(folder: vscode.WorkspaceFolder | undefined): DebugTarget | undefined {
	const root = (folder ?? vscode.workspace.workspaceFolders?.[0])?.uri.fsPath;
	if (!root || !holdsProjectFile(root)) {
		return undefined;
	}

	return { name: `Debug ${path.basename(root)}`, program: root };
}

function debugTargets(folder: vscode.WorkspaceFolder | undefined): DebugTarget[] {
	return [activeSourceTarget(), projectTarget(folder)].filter((target): target is DebugTarget => target !== undefined);
}

class MaxonDebugConfigurationProvider implements vscode.DebugConfigurationProvider {
	resolveDebugConfiguration(folder: vscode.WorkspaceFolder | undefined, config: vscode.DebugConfiguration): vscode.DebugConfiguration | undefined {
		if (typeof config.program === 'string' && config.program.trim() !== '') {
			return config;
		}

		const target = debugTargets(folder)[0];
		if (!target) {
			vscode.window.showErrorMessage(NoProgramMessage);
			return undefined;
		}

		log(`Debugging ${target.program}: the launch configuration names no program`);
		return { ...launchConfiguration(target), ...config, program: target.program };
	}
}

class MaxonDynamicDebugConfigurationProvider implements vscode.DebugConfigurationProvider {
	provideDebugConfigurations(folder: vscode.WorkspaceFolder | undefined): vscode.DebugConfiguration[] {
		return debugTargets(folder).map(target => launchConfiguration(target));
	}
}

class MaxonDebugAdapterFactory implements vscode.DebugAdapterDescriptorFactory {
	constructor(private readonly compilerExecutable: () => string | undefined) { }

	createDebugAdapterDescriptor(session: vscode.DebugSession): vscode.DebugAdapterDescriptor {
		const compiler = this.compilerExecutable();
		if (!compiler) {
			throw new Error(NoCompilerMessage);
		}

		const cwd = session.workspaceFolder?.uri.fsPath;
		log(`Starting ${compiler} ${DapServerCommand} for '${session.name}'${cwd ? ` in ${cwd}` : ''}`);
		return new vscode.DebugAdapterExecutable(compiler, [DapServerCommand], cwd ? { cwd } : undefined);
	}
}

export function registerDebugging(compilerExecutable: () => string | undefined): vscode.Disposable {
	return registerAll([
		() => vscode.debug.registerDebugAdapterDescriptorFactory(MaxonDebugType, new MaxonDebugAdapterFactory(compilerExecutable)),
		() => vscode.debug.registerDebugConfigurationProvider(MaxonDebugType, new MaxonDebugConfigurationProvider()),
		() => vscode.debug.registerDebugConfigurationProvider(
			MaxonDebugType,
			new MaxonDynamicDebugConfigurationProvider(),
			vscode.DebugConfigurationProviderTriggerKind.Dynamic
		),
		() => vscode.debug.registerDebugAdapterTrackerFactory(MaxonDebugType, {
			createDebugAdapterTracker(session) {
				const run = pendingRunOf(session);
				if (!run) return undefined;

				return {
					onDidSendMessage: message => {
						if (message.type === 'event' && message.event === ExitedEvent && typeof message.body?.exitCode === 'number') {
							run.pending.exitCode = message.body.exitCode;
						} else if (message.type === 'response' && message.command === LaunchRequest && message.success === false) {
							run.pending.refusal = `The debug adapter refused to launch ${session.configuration.program}: ${message.message}`;
						}
					}
				};
			}
		}),
		() => vscode.debug.onDidStartDebugSession(session => {
			const run = pendingRunOf(session);
			if (!run) return;

			run.pending.session = session;
			if (run.pending.stopRequested) stopSession(session);
		}),
		() => vscode.debug.onDidTerminateDebugSession(session => {
			const run = pendingRunOf(session);
			if (!run) return;

			pendingRuns.delete(run.token);
			run.pending.finish(endedOutcome(run.pending));
		})
	]);
}
