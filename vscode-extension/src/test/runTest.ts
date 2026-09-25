import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { runTests } from '@vscode/test-electron';

const PinnedCompilerVariable = 'MAXON_E2E_COMPILER';
const compilerFileName = os.platform() === 'win32' ? 'maxon.exe' : 'maxon';

async function main() {
    try {
        const extensionDevelopmentPath = path.resolve(__dirname, '../../');

        const extensionTestsPath = path.resolve(__dirname, './suite/index');

        const workspacePath = path.resolve(__dirname, '../../../');

        // `findCompiler` asks `PATH` before the workspace's own build, so on a machine with
        // Maxon installed the suite would test the last release. The compiler under test,
        // `MAXON_E2E_COMPILER` or else this checkout's build, has its directory prepended to
        // `PATH`, so its file name must be the one `findCompiler` looks for there.
        const pinnedCompiler = process.env[PinnedCompilerVariable];
        const compiler = pinnedCompiler
            ? path.resolve(pinnedCompiler)
            : path.join(workspacePath, 'maxon-bin', '.maxon', compilerFileName);
        const compilerDir = path.dirname(compiler);

        if (path.basename(compiler) !== compilerFileName) {
            console.error(`${PinnedCompilerVariable} names ${compiler}, but the extension finds the compiler on PATH as ${compilerFileName}.`);
            process.exit(1);
        }

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

        await runTests({
            extensionDevelopmentPath,
            extensionTestsPath,
            extensionTestsEnv: {
                ...inheritedEditorEnv,
                PATH: `${compilerDir}${path.delimiter}${process.env.PATH ?? ''}`
            },
            launchArgs: [
                workspacePath,
                '--disable-extensions'
            ]
        });
    } catch (err) {
        console.error('Failed to run tests');
        console.error(err);
        process.exit(1);
    }
}

main();
