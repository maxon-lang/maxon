import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { runTests } from '@vscode/test-electron';
import { compilerBinaryName, slotCompilerOf } from '../compilerLocator';

const PinnedCompilerVariable = 'MAXON_E2E_COMPILER';
const ServerPathSetting = 'maxon.serverPath';

async function main() {
    try {
        const extensionDevelopmentPath = path.resolve(__dirname, '../../');

        const extensionTestsPath = path.resolve(__dirname, './suite/index');

        const workspacePath = path.resolve(__dirname, '../../../');

        // Only `maxon.serverPath` outranks the workspace checkout's own build, so the compiler
        // under test, `MAXON_E2E_COMPILER` or else this checkout's build, is pinned through that
        // setting in a throwaway user-data directory.
        const pinnedCompiler = process.env[PinnedCompilerVariable];
        const compiler = pinnedCompiler
            ? path.resolve(pinnedCompiler)
            : slotCompilerOf(workspacePath, compilerBinaryName);

        if (!fs.existsSync(compiler)) {
            console.error(`No compiler at ${compiler}. The end-to-end suite drives the language server, which IS the compiler; build it first (scripts/build-from-seed.sh, or \`maxon build maxon-bin\`).`);
            process.exit(1);
        }

        console.log(`The end-to-end suite runs against ${compiler}`);

        // A VS Code launched from inside another must not inherit its Electron variables:
        // `ELECTRON_RUN_AS_NODE=1` makes the test build run the workspace path as a script and
        // fail with `Cannot find module`, and the `VSCODE_*` variables point it at its parent's
        // sockets and caches.
        const inheritedEditorEnv: Record<string, undefined> = { ELECTRON_RUN_AS_NODE: undefined };
        for (const name of Object.keys(process.env)) {
            if (name.startsWith('VSCODE_')) {
                inheritedEditorEnv[name] = undefined;
            }
        }

        const userDataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'maxon-e2e-user-data-'));
        fs.mkdirSync(path.join(userDataDir, 'User'));
        fs.writeFileSync(path.join(userDataDir, 'User', 'settings.json'), JSON.stringify({ [ServerPathSetting]: compiler }));

        try {
            await runTests({
                extensionDevelopmentPath,
                extensionTestsPath,
                extensionTestsEnv: inheritedEditorEnv,
                launchArgs: [
                    workspacePath,
                    '--disable-extensions',
                    `--user-data-dir=${userDataDir}`
                ]
            });
        } finally {
            fs.rmSync(userDataDir, { recursive: true, force: true });
        }
    } catch (err) {
        console.error('Failed to run tests');
        console.error(err);
        process.exit(1);
    }
}

main();
