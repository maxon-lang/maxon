import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { runTests } from '@vscode/test-electron';

async function main() {
    try {
        // The folder containing the Extension Manifest package.json
        // Passed to `--extensionDevelopmentPath`
        const extensionDevelopmentPath = path.resolve(__dirname, '../../');

        // The path to the extension test script
        // Passed to --extensionTestsPath
        const extensionTestsPath = path.resolve(__dirname, './suite/index');

        // The workspace to open for testing (the Maxon project root)
        const workspacePath = path.resolve(__dirname, '../../../');

        // ⭐⭐ THE SERVER UNDER TEST IS THIS CHECKOUT'S COMPILER, PINNED.
        // `findCompiler` asks `PATH` before it falls back to the workspace's own build, so on a
        // machine with Maxon installed the suite would answer for the last RELEASE and report that a
        // feature fixed here still works, or that one broken here still does not. Prepending the
        // build directory makes the fallback's answer the one `PATH` gives too.
        const compilerDir = path.join(workspacePath, 'maxon-bin', '.maxon');
        const compiler = path.join(compilerDir, os.platform() === 'win32' ? 'maxon.exe' : 'maxon');

        if (!fs.existsSync(compiler)) {
            console.error(`No compiler at ${compiler}. The end-to-end suite drives the language server, which IS the compiler; build it first (scripts/build-from-seed.sh, or \`maxon build maxon-bin\`).`);
            process.exit(1);
        }

        // ⛔ A VS Code LAUNCHED FROM INSIDE ANOTHER ONE MUST NOT INHERIT ITS ELECTRON VARIABLES.
        // `ELECTRON_RUN_AS_NODE=1` makes the test build run its first argument as a script — the
        // workspace path — and fail with `Cannot find module`, and the `VSCODE_*` variables point the
        // child at its parent's sockets and caches. An extension host's child process (an agent, a
        // task runner) carries all of them, so the environment is cleaned rather than trusted.
        const inheritedEditorEnv: Record<string, undefined> = { ELECTRON_RUN_AS_NODE: undefined };
        for (const name of Object.keys(process.env)) {
            if (name.startsWith('VSCODE_')) {
                inheritedEditorEnv[name] = undefined;
            }
        }

        // Download VS Code, unzip it and run the integration test
        await runTests({
            extensionDevelopmentPath,
            extensionTestsPath,
            extensionTestsEnv: {
                ...inheritedEditorEnv,
                PATH: `${compilerDir}${path.delimiter}${process.env.PATH ?? ''}`
            },
            launchArgs: [
                workspacePath, // Open the Maxon workspace
                '--disable-extensions' // Disable other extensions for isolated testing
            ]
        });
    } catch (err) {
        console.error('Failed to run tests');
        console.error(err);
        process.exit(1);
    }
}

main();
