# VSCode Extension Tests

This directory contains tests for the Maxon VS Code extension.

## Test Structure

- `runTest.ts` - Entry point for running tests using @vscode/test-electron. It opens the checkout
  root as the workspace, and pins the language server and the debug adapter to one compiler: the one
  `MAXON_E2E_COMPILER` names when it is set, otherwise this checkout's own at `maxon-bin/.maxon/`. The
  suite refuses to run without it.
- `suite/index.ts` - Mocha test suite configuration and test file discovery
- `suite/fixtures.ts` - the extension id (derived from `package.json`), and staging for the fixture
  projects the end-to-end tests drive the server against
- `suite/definition.test.ts` - go to definition, end to end, judged on the answer's URI
- `suite/lsp-integration.test.ts` - the rest of the language features, each one a real request
- `suite/debug.test.ts` - debugging, end to end through `maxon dap-server`, judged on the Debug Adapter
  Protocol messages a tracker records. It debugs x64-windows programs only, so on any other host every
  case skips and says why in its title
- `unit/` - Tests of modules that do not import `vscode`, run under plain mocha by `npm run test:unit`

## Running Tests

To run the tests:

```bash
npm test
```

This will:
1. Discard `out/` and recompile, so a suite deleted from `src/` cannot go on running from a stale build
2. Run the TextMate grammar snapshot tests
3. Download a VS Code instance for testing (if not already downloaded)
4. Launch VS Code and run the end-to-end suite
5. Display test results in the console

The end-to-end tests need a built compiler, because the compiler *is* the language server and the debug
adapter: `scripts/build-from-seed.sh`, or `maxon build maxon-bin`. To test another build, name its
executable in `MAXON_E2E_COMPILER`; the file must be named `maxon` (`maxon.exe` on Windows), because the
extension finds it on `PATH` by that name.

## Fixtures

Each end-to-end case stages its own project under `<checkout>/temp/vscode-e2e/<case>/`, written to
disk rather than edited into an unsaved buffer — a cross-file answer is read from disk. `temp/` is
gitignored and carries a `.maxonignore`, so a fixture is in neither the repository nor any project
walk rooted above it; the server roots the project at the fixture's own `build.maxon`, which is also
what keeps it from climbing to the checkout root and indexing the whole tree.

## Test Coverage

1. **Extension Activation** - presence, activation, language registration, manifest contributions
2. **Go to definition** - into the stdlib, into a sibling file, and within the buffer being edited
3. **Language server** - the handshake's advertised capabilities, diagnostics, hover, formatting and
   document symbols, each driven through the editor's own provider commands
4. **Grammar** - the TextMate grammar's structure, and its snapshots (`npm run test:grammar`)
5. **Debugging** - a gutter breakpoint stopping at its line, the stop's frames and locals, `continue` to
   the program's own exit code; F5 without a `program` debugging the active editor's file; and the Test
   Explorer's Debug profile reporting one passing and one failing test, building a project once for all
   its tests, stopping a live session when the run is cancelled, and leaving the project free for a Run
   while a session is live. That project is staged under `vscode-extension/.maxon/e2e-fixtures/`, which
   is gitignored and listed by the Test Explorer; `temp/` holds a `.maxonignore`, which hides a fixture
   there from it

## Prerequisites

Before running tests, ensure dependencies are installed:

```bash
npm install
```

## Writing New Tests

To add new tests:

1. Create a new `.test.ts` file in the `suite/` directory
2. Use the Mocha TDD interface (`suite` and `test` functions)
3. Stage what the test needs with `stageProject` from `./fixtures`, and ask for the extension with
   `maxonExtension()` rather than spelling its id
4. A test that drives the server raises its own timeout; the suite default is 10 s

Example:

```typescript
import * as assert from 'assert';
import * as vscode from 'vscode';

suite('My Test Suite', () => {
    test('My Test', () => {
        assert.strictEqual(1 + 1, 2);
    });
});
```

## Troubleshooting

If tests fail to run:
1. Ensure all dependencies are installed (`npm install`)
2. Check that TypeScript compilation succeeds (`npm run compile`)
3. Verify the compiler exists at `maxon-bin/.maxon/`, or where `MAXON_E2E_COMPILER` names —
   `runTest.ts` says so and stops
4. Check console output for specific error messages
