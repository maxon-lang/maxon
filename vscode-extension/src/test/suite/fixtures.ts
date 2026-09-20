import * as assert from 'assert';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';

/**
 * The manifest this extension is built from, read at runtime from `package.json`.
 *
 * Compiled tests live in `out/test/suite/`, so three levels up is the extension root.
 */
const manifest = JSON.parse(
    fs.readFileSync(path.resolve(__dirname, '../../../package.json'), 'utf8')
) as { publisher: string; name: string; displayName: string; };

/**
 * `<publisher>.<name>`, derived rather than spelled out.
 *
 * A second copy of the id is free to drift from the manifest, and a stale one makes
 * `getExtension` return `undefined` — which reads as "the extension is broken" in every test that
 * asks for it. The publisher has changed once already.
 */
export const extensionId = `${manifest.publisher}.${manifest.name}`;

export const extensionDisplayName = manifest.displayName;

export function maxonExtension(): vscode.Extension<unknown> {
    const found = vscode.extensions.getExtension(extensionId);
    assert.ok(found, `no extension ${extensionId} in this test host`);
    return found;
}

/** The extension, activated — which means its language client has finished starting. */
export async function activateMaxonExtension(): Promise<vscode.Extension<unknown>> {
    const found = maxonExtension();
    if (!found.isActive) {
        await found.activate();
    }
    return found;
}

/**
 * Where a staged fixture project goes: `<workspace>/temp/vscode-e2e/`.
 *
 * `temp/` is gitignored and carries a `.maxonignore`, so a fixture written here is in neither the
 * repository nor any project walk rooted above it. The server's own sweep is rooted at the
 * fixture's `project.maxon` and reads `collectMaxonSources` from there, which never consults a marker
 * above its root — so the fixture is still a project to the server. `tests/lsp/` stages the same way.
 *
 * The marker is written here too, so the fixture root excludes itself whatever `temp/` carries.
 */
function fixturesRoot(): string {
    const folder = vscode.workspace.workspaceFolders?.[0];
    assert.ok(folder, 'the test host opened no workspace folder');

    const root = path.join(folder.uri.fsPath, 'temp', 'vscode-e2e');
    fs.mkdirSync(root, { recursive: true });
    fs.writeFileSync(path.join(root, '.maxonignore'), '');

    return root;
}

/**
 * A fixture project of its own, staged on disk and rooted by an empty `project.maxon`.
 *
 * ⚠ THE FILES ARE WRITTEN, NOT EDITED INTO EXISTENCE. A `WorkspaceEdit` leaves the text unsaved, so
 * a sibling staged that way is empty on disk — and a cross-file answer is read from disk.
 *
 * The `project.maxon` marker is what stops the server rooting the project at the checkout's own
 * manifest and sweeping the whole tree for one two-file fixture.
 */
export function stageProject(name: string, files: Record<string, string>): string {
    const dir = path.join(fixturesRoot(), name);
    fs.rmSync(dir, { recursive: true, force: true });
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(dir, 'project.maxon'), '');

    for (const [fileName, content] of Object.entries(files)) {
        fs.writeFileSync(path.join(dir, fileName), content);
    }

    return dir;
}

export async function openFixture(dir: string, fileName: string): Promise<vscode.TextDocument> {
    const doc = await vscode.workspace.openTextDocument(vscode.Uri.file(path.join(dir, fileName)));
    await vscode.window.showTextDocument(doc);
    return doc;
}

export async function closeAllEditors(): Promise<void> {
    await vscode.commands.executeCommand('workbench.action.closeAllEditors');
}

/** Definition answers as `Location`s, whichever of LSP's two answer shapes the client hands back. */
export async function definitionsAt(doc: vscode.TextDocument, position: vscode.Position): Promise<vscode.Location[]> {
    const answer = await vscode.commands.executeCommand<vscode.Location[] | vscode.LocationLink[]>(
        'vscode.executeDefinitionProvider',
        doc.uri,
        position
    );

    if (!answer || answer.length === 0) {
        return [];
    }

    return answer.map(one => 'targetUri' in one ? new vscode.Location(one.targetUri, one.targetRange) : one);
}

/** The text of one line of a file on disk — what an answer's line number is judged by. */
export function lineOfFile(fsPath: string, line: number): string {
    return fs.readFileSync(fsPath, 'utf8').split(/\r?\n/)[line] ?? '';
}

/** Forward slashes, so an answer's path can be matched against one spelling on either platform. */
export function slashed(fsPath: string): string {
    return fsPath.replace(/\\/g, '/');
}

/**
 * The first request against a project root pays for a corpus build — the stdlib and the runtime are
 * lexed before any answer exists. Polled rather than slept on, so a warm host is not charged for a
 * cold one's worst case, and a deadline still fails the test rather than hanging it.
 */
export async function warmUpDefinitions(doc: vscode.TextDocument, position: vscode.Position, deadlineMs: number): Promise<number> {
    const started = Date.now();

    while (Date.now() - started < deadlineMs) {
        const found = await definitionsAt(doc, position);
        if (found.length > 0) {
            return Date.now() - started;
        }
        await new Promise(resolve => setTimeout(resolve, 250));
    }

    return Date.now() - started;
}

/** Diagnostics for `uri`, once the server has published any — or the empty set at the deadline. */
export async function diagnosticsFor(uri: vscode.Uri, deadlineMs: number): Promise<vscode.Diagnostic[]> {
    const started = Date.now();

    while (Date.now() - started < deadlineMs) {
        const published = vscode.languages.getDiagnostics(uri);
        if (published.length > 0) {
            return published;
        }
        await new Promise(resolve => setTimeout(resolve, 250));
    }

    return vscode.languages.getDiagnostics(uri);
}
