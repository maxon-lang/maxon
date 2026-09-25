import * as assert from 'assert';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import type { UnitTestController } from '../../testController';
import { TestBinaryExitCode } from '../../unitTestModel';
import { activateMaxonExtension, closeAllEditors, openFixture, stageProject } from './fixtures';

interface DapMessage {
    type: string;
    seq?: number;
    command?: string;
    event?: string;
    request_seq?: number;
    success?: boolean;
    message?: string;
    arguments?: Record<string, unknown>;
    body?: Record<string, unknown>;
}

type Direction = 'toAdapter' | 'fromAdapter';

interface RecordedMessage {
    direction: Direction;
    message: DapMessage;
}

class Transcript {
    readonly recorded: RecordedMessage[] = [];
    session: vscode.DebugSession | undefined;
    sessionEnded = false;
    private readonly listeners = new Set<() => void>();

    record(direction: Direction, message: DapMessage): void {
        this.recorded.push({ direction, message });
        this.notify();
    }

    endSession(): void {
        this.sessionEnded = true;
        this.notify();
    }

    private notify(): void {
        for (const listener of [...this.listeners]) listener();
    }

    refused(): string[] {
        return this.recorded
            .filter(({ direction, message }) => direction === 'fromAdapter' && message.type === 'response' && message.success === false)
            .map(({ message }) => `${message.command}: ${message.message}`);
    }

    summary(): string {
        return this.recorded
            .map(({ direction, message }) => `${direction === 'toAdapter' ? '->' : '<-'} ${message.type} ${message.command ?? message.event ?? ''}${message.success === false ? ` FAILED: ${message.message}` : ''}`)
            .join('\n');
    }

    private until<T>(what: string, deadlineMs: number, probe: () => T | undefined): Promise<T> {
        return new Promise((resolve, reject) => {
            const look = () => {
                const found = probe();
                if (found === undefined) return false;

                this.listeners.delete(look);
                clearTimeout(timer);
                resolve(found);
                return true;
            };
            const timer = setTimeout(() => {
                this.listeners.delete(look);
                reject(new Error(`no ${what} within ${deadlineMs} ms. The session said:\n${this.summary()}`));
            }, deadlineMs);

            if (!look()) this.listeners.add(look);
        });
    }

    private message(what: string, deadlineMs: number, direction: Direction, matches: (message: DapMessage) => boolean): Promise<DapMessage> {
        return this.until(what, deadlineMs, () => this.recorded.find(one => one.direction === direction && matches(one.message))?.message);
    }

    event(name: string, deadlineMs: number): Promise<DapMessage> {
        return this.message(`'${name}' event`, deadlineMs, 'fromAdapter', message => message.type === 'event' && message.event === name);
    }

    request(command: string, deadlineMs: number): Promise<DapMessage> {
        return this.message(`'${command}' request`, deadlineMs, 'toAdapter', message => message.type === 'request' && message.command === command);
    }

    response(command: string, deadlineMs: number): Promise<DapMessage> {
        return this.message(`'${command}' response`, deadlineMs, 'fromAdapter', message => message.type === 'response' && message.command === command);
    }

    async ended(deadlineMs: number): Promise<void> {
        await this.until('end of the session', deadlineMs, () => this.sessionEnded ? true : undefined);
    }
}

const unsupportedHostReason = process.platform === 'win32' && process.arch === 'x64'
    ? undefined
    : `maxon dap-server debugs x64-windows programs only until stage 4 of the debugger brings x64-linux, arm64-linux and arm64-macos; this host is ${process.platform}-${process.arch}`;

function skipUnlessDebuggerHost(context: Mocha.Context): void {
    if (unsupportedHostReason === undefined) return;

    if (context.test) context.test.title += ` (skipped: ${unsupportedHostReason})`;
    context.skip();
}

const FixtureExitCode = 5;
const AnchoredStatement = 'print("{label} {seed}\\n")';
const CallStatement = 'render(4)';
const LabelLocal = 'label';
const LabelValue = 'parcel';

const ProjectManifest = [
    'export function build() returns ExitCode',
    '\tBuild.build(".", output: ".maxon/debugged")',
    '\treturn 0',
    "end 'build'",
    ''
].join('\n');

const MainSource = [
    'typealias Seed = int(0 to 100)',
    '',
    'function render(seed Seed)',
    `\tlet ${LabelLocal} = "${LabelValue}"`,
    `\t${AnchoredStatement}`,
    "end 'render'",
    '',
    'function main() returns ExitCode',
    `\t${CallStatement}`,
    '',
    `\treturn ${FixtureExitCode}`,
    "end 'main'",
    ''
].join('\n');

function stageDebuggedProject(name: string): string {
    return stageProject(name, { 'fixture.maxproj': ProjectManifest, 'main.maxon': MainSource });
}

function lineOf(source: string, statement: string): number {
    const line = source.split('\n').findIndex(text => text.includes(statement));
    assert.ok(line >= 0, `the fixture holds no line with ${statement}`);
    return line;
}

function samePath(left: unknown, right: string): boolean {
    if (typeof left !== 'string') return false;

    const normalize = (spelled: string) => {
        const resolved = path.resolve(spelled);
        return process.platform === 'win32' ? resolved.toLowerCase() : resolved;
    };
    return normalize(left) === normalize(right);
}

interface MaxonExtensionApi {
    getUnitTests(): UnitTestController | undefined;
}

const PassingTestName = 'doubling two gives four';
const FailingTestName = 'doubling two gives five';
const DoublingAnnouncement = 'print("doubling {value}\\n")';
const SourceFileName = 'doubling.maxon';
const TestFileName = 'doubling.maxtest';

const DoublingSource = [
    'export typealias Small = int(0 to 100)',
    'export typealias Doubled = int(0 to 200)',
    '',
    'export function double(value Small) returns Doubled',
    `\t${DoublingAnnouncement}`,
    '\treturn value * 2',
    "end 'double'",
    ''
].join('\n');

const DoublingTestSource = [
    `test '${PassingTestName}'`,
    '\ttry Expect.isTrue(double(2) == 4, message: "two doubled is four")',
    `end '${PassingTestName}'`,
    '',
    `test '${FailingTestName}'`,
    '\ttry Expect.isTrue(double(2) == 5, message: "two doubled is not five")',
    `end '${FailingTestName}'`,
    ''
].join('\n');

type RecordedOutcome = 'passed' | 'failed' | 'errored' | 'skipped';

const DiscoveryPollMs = 250;
const FixtureRemovalRetries = 20;
const FixtureRemovalRetryDelayMs = 100;

const stagedTestExplorerProjects: string[] = [];

function removeFixture(dir: string): void {
    fs.rmSync(dir, { recursive: true, force: true, maxRetries: FixtureRemovalRetries, retryDelay: FixtureRemovalRetryDelayMs });
}

function stageTestExplorerProject(name: string): string {
    const dir = path.resolve(__dirname, '../../../.maxon/e2e-fixtures', name);
    removeFixture(dir);
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(dir, SourceFileName), DoublingSource);
    fs.writeFileSync(path.join(dir, TestFileName), DoublingTestSource);
    stagedTestExplorerProjects.push(dir);
    return dir;
}

async function discoveredFileItem(controller: vscode.TestController, testFile: string, deadlineMs: number): Promise<vscode.TestItem> {
    const started = Date.now();
    await controller.refreshHandler?.(new vscode.CancellationTokenSource().token);

    while (Date.now() - started < deadlineMs) {
        let found: vscode.TestItem | undefined;
        controller.items.forEach(item => {
            if (item.uri && samePath(item.uri.fsPath, testFile)) found = item;
        });
        if (found && found.children.size > 0) return found;

        await new Promise(resolve => setTimeout(resolve, DiscoveryPollMs));
    }

    assert.fail(`the Test Explorer did not discover ${testFile} within ${deadlineMs} ms`);
}

const RecordedOutcomes: ReadonlySet<string> = new Set<RecordedOutcome>(['passed', 'failed', 'errored', 'skipped']);
const CommandEchoPrefix = '> ';
const BuildFlag = '--build';

interface RecordedRun {
    profile: vscode.TestRunProfile | undefined;
    outcomes: Map<string, { state: RecordedOutcome; message: string; }>;
    commands: string[];
    ended: boolean;
}

function recordRuns(controller: vscode.TestController, runs: RecordedRun[]): vscode.Disposable {
    const original = controller.createTestRun;

    controller.createTestRun = (request, name, persist) => {
        const run = original.call(controller, request, name, persist);
        const recorded: RecordedRun = { profile: request.profile, outcomes: new Map(), commands: [], ended: false };
        runs.push(recorded);

        return new Proxy(run, {
            get(target, property, receiver) {
                const value = Reflect.get(target, property, receiver);
                if (typeof value !== 'function') return value;

                if (property === 'appendOutput') {
                    return (output: string, ...rest: unknown[]) => {
                        if (output.startsWith(CommandEchoPrefix)) recorded.commands.push(output.trim());
                        return value.call(target, output, ...rest);
                    };
                }

                if (property === 'end') {
                    return () => {
                        recorded.ended = true;
                        return value.call(target);
                    };
                }

                if (typeof property !== 'string' || !RecordedOutcomes.has(property)) return value.bind(target);

                return (item: vscode.TestItem, ...rest: unknown[]) => {
                    const message = rest[0] instanceof vscode.TestMessage ? String(rest[0].message) : '';
                    recorded.outcomes.set(item.id, { state: property as RecordedOutcome, message });
                    return value.call(target, item, ...rest);
                };
            }
        });
    };

    return new vscode.Disposable(() => { controller.createTestRun = original; });
}

function soleRunOf(runs: RecordedRun[], profile: vscode.TestRunProfile): RecordedRun {
    const matching = runs.filter(run => run.profile === profile);
    assert.strictEqual(matching.length, 1, `expected one ${profile.label} run, and ${matching.length} were created`);
    return matching[0];
}

function describeOutcomes(run: RecordedRun): string {
    return JSON.stringify([...run.outcomes]);
}

function childNamed(fileItem: vscode.TestItem, name: string): vscode.TestItem {
    let found: vscode.TestItem | undefined;
    fileItem.children.forEach(child => {
        if (child.label === name) found = child;
    });
    assert.ok(found, `the Test Explorer did not discover '${name}'`);
    return found;
}

function settledWithin<T>(pending: Thenable<T> | void, deadlineMs: number, what: string): Promise<T | void> {
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error(`${what} within ${deadlineMs} ms`)), deadlineMs);
        Promise.resolve(pending).then(
            value => {
                clearTimeout(timer);
                resolve(value);
            },
            error => {
                clearTimeout(timer);
                reject(error);
            }
        );
    });
}

function workspaceFolder(): vscode.WorkspaceFolder {
    const folder = vscode.workspace.workspaceFolders?.[0];
    assert.ok(folder, 'the test host opened no workspace folder');
    return folder;
}

suite('Debugging', () => {

    const sessionDeadlineMs = 120000;
    const stepDeadlineMs = 60000;

    let transcript = new Transcript();
    let registrations: vscode.Disposable | undefined;
    let extensionApi: MaxonExtensionApi | undefined;

    suiteSetup(async function () {
        this.timeout(sessionDeadlineMs);
        extensionApi = (await activateMaxonExtension()).exports as MaxonExtensionApi | undefined;

        registrations = vscode.Disposable.from(
            vscode.debug.registerDebugAdapterTrackerFactory('maxon', {
                createDebugAdapterTracker(session) {
                    const recording = transcript;
                    recording.session = session;
                    return {
                        onWillReceiveMessage: message => recording.record('toAdapter', message as DapMessage),
                        onDidSendMessage: message => recording.record('fromAdapter', message as DapMessage)
                    };
                }
            }),
            vscode.debug.onDidTerminateDebugSession(session => {
                if (transcript.session?.id === session.id) transcript.endSession();
            })
        );
    });

    suiteTeardown(() => {
        registrations?.dispose();
    });

    setup(() => {
        transcript = new Transcript();
        vscode.debug.removeBreakpoints(vscode.debug.breakpoints);
    });

    teardown(async function () {
        this.timeout(stepDeadlineMs);

        const session = transcript.session;
        if (session) {
            if (!transcript.sessionEnded) await vscode.debug.stopDebugging(session);
            await transcript.ended(stepDeadlineMs);
        }

        for (const dir of stagedTestExplorerProjects.splice(0)) removeFixture(dir);

        vscode.debug.removeBreakpoints(vscode.debug.breakpoints);
        await closeAllEditors();
    });

    test('a gutter breakpoint stops the program at its line, the stop answers its frames and locals, and continue runs it to its exit', async function () {
        skipUnlessDebuggerHost(this);
        this.timeout(sessionDeadlineMs);

        const dir = stageDebuggedProject('debug-breakpoint');
        const source = path.join(dir, 'main.maxon');
        const line = lineOf(MainSource, AnchoredStatement);

        vscode.debug.addBreakpoints([
            new vscode.SourceBreakpoint(new vscode.Location(vscode.Uri.file(source), new vscode.Position(line, 0)))
        ]);

        const started = await vscode.debug.startDebugging(workspaceFolder(), {
            type: 'maxon',
            request: 'launch',
            name: 'debug-breakpoint',
            program: source
        });
        assert.ok(started, `VS Code did not start the session. The session said:\n${transcript.summary()}`);

        const placed = await transcript.response('setBreakpoints', sessionDeadlineMs);
        const placedBreakpoints = placed.body?.breakpoints as { verified: boolean; line?: number; message?: string; }[] | undefined;
        assert.ok(placed.success, `setBreakpoints was refused: ${placed.message}`);
        assert.ok(
            placedBreakpoints?.length === 1 && placedBreakpoints[0].verified && placedBreakpoints[0].line === line + 1,
            `the gutter breakpoint at line ${line + 1} is not verified at that line: ${JSON.stringify(placedBreakpoints)}`
        );

        const stopped = await transcript.event('stopped', sessionDeadlineMs);
        assert.strictEqual(stopped.body?.reason, 'breakpoint', `the first stop is not the breakpoint: ${JSON.stringify(stopped.body)}`);

        const session = transcript.session;
        assert.ok(session, 'no debug session reached the tracker');
        const threadId = stopped.body?.threadId;

        const frames = await session.customRequest('stackTrace', { threadId });
        const top = frames.stackFrames?.[0];
        assert.ok(top, `the stop answered no frames: ${JSON.stringify(frames)}`);
        assert.strictEqual(top.name, 'render', `frame 0 is the innermost frame, inside render: ${JSON.stringify(frames.stackFrames)}`);
        assert.strictEqual(top.line, line + 1, `frame 0 is not at the breakpoint's line: ${JSON.stringify(top)}`);
        assert.ok(samePath(top.source?.path, source), `frame 0 is not in ${source}: ${JSON.stringify(top.source)}`);

        const caller = frames.stackFrames?.[1];
        assert.strictEqual(caller?.name, 'main', `frame 1 is render's caller, main: ${JSON.stringify(frames.stackFrames)}`);
        assert.strictEqual(caller.line, lineOf(MainSource, CallStatement) + 1, `frame 1 is not at the line that calls render: ${JSON.stringify(caller)}`);

        const scopes = await session.customRequest('scopes', { frameId: top.id });
        const locals = (scopes.scopes as { name: string; variablesReference: number; }[] | undefined)?.find(scope => scope.name === 'Locals');
        assert.ok(locals, `frame 0 has no Locals scope: ${JSON.stringify(scopes)}`);

        const variables = await session.customRequest('variables', { variablesReference: locals.variablesReference });
        const label = (variables.variables as { name: string; value: string; }[] | undefined)?.find(variable => variable.name === LabelLocal);
        assert.ok(label, `Locals does not name ${LabelLocal}: ${JSON.stringify(variables)}`);
        assert.ok(label.value.includes(LabelValue), `${LabelLocal} holds "${LabelValue}" at the breakpoint, but the variables pane says ${label.value}`);

        await session.customRequest('continue', { threadId });

        const exited = await transcript.event('exited', sessionDeadlineMs);
        assert.strictEqual(exited.body?.exitCode, FixtureExitCode, `the exited event does not carry the program's own exit code: ${JSON.stringify(exited.body)}`);
        await transcript.event('terminated', stepDeadlineMs);
        assert.deepStrictEqual(transcript.refused(), [], 'the adapter refused a request VS Code sent');

        console.log(`      ${transcript.recorded.length} DAP messages in the breakpoint session`);
    });

    test('F5 with no program debugs the .maxon file in the active editor', async function () {
        skipUnlessDebuggerHost(this);
        this.timeout(sessionDeadlineMs);

        const dir = stageDebuggedProject('debug-no-configuration');
        const source = path.join(dir, 'main.maxon');
        await openFixture(dir, 'main.maxon');

        const started = await vscode.debug.startDebugging(workspaceFolder(), {
            type: 'maxon',
            request: 'launch',
            name: 'debug-no-configuration'
        });
        assert.ok(started, `VS Code did not start the session. The session said:\n${transcript.summary()}`);

        const launch = await transcript.request('launch', sessionDeadlineMs);
        assert.ok(
            samePath(launch.arguments?.program, source),
            `the launch request does not name the active editor's file ${source}: ${JSON.stringify(launch.arguments)}`
        );

        const exited = await transcript.event('exited', sessionDeadlineMs);
        assert.strictEqual(exited.body?.exitCode, FixtureExitCode, `the exited event does not carry the program's own exit code: ${JSON.stringify(exited.body)}`);
        await transcript.event('terminated', stepDeadlineMs);
        assert.deepStrictEqual(transcript.refused(), [], 'the adapter refused a request VS Code sent');

        console.log(`      ${transcript.recorded.length} DAP messages in the no-configuration session`);
    });


    function unitTestsUnderTest(): UnitTestController {
        const unitTests = extensionApi?.getUnitTests();
        assert.ok(unitTests, 'the extension exports no unit test controller');
        return unitTests;
    }

    function breakAtTheDoubling(dir: string): void {
        const source = vscode.Uri.file(path.join(dir, SourceFileName));
        vscode.debug.addBreakpoints([
            new vscode.SourceBreakpoint(new vscode.Location(source, new vscode.Position(lineOf(DoublingSource, DoublingAnnouncement), 0)))
        ]);
    }

    function launchedPrograms(): { program?: string; args?: string[]; }[] {
        return transcript.recorded
            .filter(({ direction, message }) => direction === 'toAdapter' && message.type === 'request' && message.command === 'launch')
            .map(({ message }) => message.arguments as { program?: string; args?: string[]; });
    }

    function exitCodes(): unknown[] {
        return transcript.recorded
            .filter(({ direction, message }) => direction === 'fromAdapter' && message.type === 'event' && message.event === 'exited')
            .map(({ message }) => message.body?.exitCode);
    }

    test('the Test Explorer Debug profile debugs one test and reports its pass or its failure', async function () {
        skipUnlessDebuggerHost(this);
        this.timeout(2 * sessionDeadlineMs);

        const { controller, debugProfile } = unitTestsUnderTest();
        const dir = stageTestExplorerProject('debug-test-profile');
        const runs: RecordedRun[] = [];
        const recording = recordRuns(controller, runs);

        try {
            const cancellation = new vscode.CancellationTokenSource();
            const fileItem = await discoveredFileItem(controller, path.join(dir, TestFileName), stepDeadlineMs);

            for (const [name, expected, exitCode] of [[PassingTestName, 'passed', TestBinaryExitCode.AllPassed], [FailingTestName, 'failed', TestBinaryExitCode.TestFailed]] as const) {
                const item = childNamed(fileItem, name);

                transcript = new Transcript();
                runs.length = 0;
                await debugProfile.runHandler(new vscode.TestRunRequest([item], undefined, debugProfile), cancellation.token);

                const launch = await transcript.request('launch', stepDeadlineMs);
                const launched = launch.arguments as { program?: string; args?: string[]; } | undefined;
                assert.ok(
                    typeof launched?.program === 'string' && path.basename(launched.program).startsWith('maxon-test'),
                    `the Debug profile did not launch the test binary: ${JSON.stringify(launched)}`
                );
                assert.ok(
                    launched.args?.length === 1 && /^--select=\|\d+\|$/.test(launched.args[0]),
                    `the Debug profile did not select exactly one test: ${JSON.stringify(launched.args)}`
                );

                const exited = await transcript.event('exited', stepDeadlineMs);
                assert.strictEqual(exited.body?.exitCode, exitCode, `the test binary for '${name}' did not exit with ${exitCode}: ${JSON.stringify(exited.body)}`);
                await transcript.ended(stepDeadlineMs);
                assert.deepStrictEqual(transcript.refused(), [], 'the adapter refused a request VS Code sent');

                const outcome = soleRunOf(runs, debugProfile).outcomes.get(item.id);
                assert.strictEqual(outcome?.state, expected, `'${name}' was reported ${JSON.stringify(outcome)}, not ${expected}`);

                console.log(`      ${transcript.recorded.length} DAP messages debugging '${name}'`);
            }
        } finally {
            recording.dispose();
        }
    });

    test('debugging a test file builds its project once and debugs each of its tests from that one binary', async function () {
        skipUnlessDebuggerHost(this);
        this.timeout(2 * sessionDeadlineMs);

        const { controller, debugProfile } = unitTestsUnderTest();
        const dir = stageTestExplorerProject('debug-test-file');
        const runs: RecordedRun[] = [];
        const recording = recordRuns(controller, runs);

        try {
            const fileItem = await discoveredFileItem(controller, path.join(dir, TestFileName), stepDeadlineMs);
            const cancellation = new vscode.CancellationTokenSource();
            await settledWithin(
                debugProfile.runHandler(new vscode.TestRunRequest([fileItem], undefined, debugProfile), cancellation.token),
                2 * sessionDeadlineMs,
                'the Debug profile run of a test file did not end'
            );

            const run = soleRunOf(runs, debugProfile);
            const builds = run.commands.filter(command => command.includes(BuildFlag));
            assert.strictEqual(builds.length, 1, `debugging a file of ${fileItem.children.size} tests built ${builds.length} times:\n${builds.join('\n')}`);

            const launched = launchedPrograms();
            assert.strictEqual(launched.length, 2, `debugging a file of two tests launched ${launched.length} sessions: ${JSON.stringify(launched)}`);
            assert.ok(samePath(launched[0].program, launched[1].program as string), `the two sessions debugged different binaries: ${JSON.stringify(launched)}`);

            const selections = launched.map(one => one.args?.join(' '));
            assert.strictEqual(new Set(selections).size, 2, `the two sessions did not each select their own test: ${JSON.stringify(selections)}`);
            assert.deepStrictEqual(exitCodes(), [TestBinaryExitCode.AllPassed, TestBinaryExitCode.TestFailed], `the two sessions did not exit with a pass and then a failure:\n${transcript.summary()}`);
            assert.deepStrictEqual(transcript.refused(), [], 'the adapter refused a request VS Code sent');

            assert.strictEqual(run.outcomes.get(childNamed(fileItem, PassingTestName).id)?.state, 'passed', describeOutcomes(run));
            assert.strictEqual(run.outcomes.get(childNamed(fileItem, FailingTestName).id)?.state, 'failed', describeOutcomes(run));
            assert.ok(run.ended, 'the Debug profile run never ended');
        } finally {
            recording.dispose();
        }
    });

    test('cancelling a Debug profile run stops its live session, and the tests it did not finish get no verdict', async function () {
        skipUnlessDebuggerHost(this);
        this.timeout(2 * sessionDeadlineMs);

        const { controller, debugProfile } = unitTestsUnderTest();
        const dir = stageTestExplorerProject('debug-test-cancelled');
        const testFile = path.join(dir, TestFileName);
        const runs: RecordedRun[] = [];
        const recording = recordRuns(controller, runs);

        try {
            const fileItem = await discoveredFileItem(controller, testFile, stepDeadlineMs);
            breakAtTheDoubling(dir);

            const cancellation = new vscode.CancellationTokenSource();
            const debugging = debugProfile.runHandler(new vscode.TestRunRequest([fileItem], undefined, debugProfile), cancellation.token);

            const stopped = await transcript.event('stopped', sessionDeadlineMs);
            assert.strictEqual(stopped.body?.reason, 'breakpoint', `the first stop is not the breakpoint: ${JSON.stringify(stopped.body)}`);

            cancellation.cancel();
            await transcript.ended(stepDeadlineMs);
            await settledWithin(debugging, stepDeadlineMs, 'the Debug profile run did not end after it was cancelled');

            const run = soleRunOf(runs, debugProfile);
            assert.ok(run.ended, 'the cancelled Debug profile run never ended');
            assert.deepStrictEqual([...run.outcomes], [], 'a cancelled run reports no verdict for a test it did not finish, as the Run profile does; ending the run reports it skipped');
            assert.strictEqual(launchedPrograms().length, 1, `a cancelled run went on to debug the next test: ${JSON.stringify(launchedPrograms())}`);
        } finally {
            recording.dispose();
        }
    });

    test('a Run while a Debug profile session is live builds and runs the same project', async function () {
        skipUnlessDebuggerHost(this);
        this.timeout(2 * sessionDeadlineMs);

        const { controller, runProfile, debugProfile } = unitTestsUnderTest();
        const dir = stageTestExplorerProject('debug-test-beside-a-run');
        const testFile = path.join(dir, TestFileName);
        const runs: RecordedRun[] = [];
        const recording = recordRuns(controller, runs);

        try {
            const fileItem = await discoveredFileItem(controller, testFile, stepDeadlineMs);
            const item = childNamed(fileItem, PassingTestName);
            breakAtTheDoubling(dir);

            const cancellation = new vscode.CancellationTokenSource();
            const debugging = debugProfile.runHandler(new vscode.TestRunRequest([item], undefined, debugProfile), cancellation.token);

            const stopped = await transcript.event('stopped', sessionDeadlineMs);
            assert.strictEqual(stopped.body?.reason, 'breakpoint', `the first stop is not the breakpoint: ${JSON.stringify(stopped.body)}`);

            await settledWithin(
                runProfile.runHandler(new vscode.TestRunRequest([item], undefined, runProfile), cancellation.token),
                sessionDeadlineMs,
                'the Run beside a live Debug session did not end'
            );
            const ran = soleRunOf(runs, runProfile);
            assert.strictEqual(ran.outcomes.get(item.id)?.state, 'passed', `the Run beside a live Debug session did not pass '${PassingTestName}': ${describeOutcomes(ran)}`);

            const session = transcript.session;
            assert.ok(session, 'no debug session reached the tracker');
            await session.customRequest('continue', { threadId: stopped.body?.threadId });
            await settledWithin(debugging, sessionDeadlineMs, 'the Debug profile run did not end after the session continued');

            const debugged = soleRunOf(runs, debugProfile);
            assert.strictEqual(debugged.outcomes.get(item.id)?.state, 'passed', `the debugged test was not reported passed: ${describeOutcomes(debugged)}`);
            assert.deepStrictEqual(exitCodes(), [TestBinaryExitCode.AllPassed], `the debugged test binary did not exit with a pass:\n${transcript.summary()}`);
        } finally {
            recording.dispose();
        }
    });
});
