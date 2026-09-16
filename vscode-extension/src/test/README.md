# VSCode Extension Tests

This directory contains tests for the Maxon Language Support VSCode extension.

## Test Structure

- `runTest.ts` - Entry point for running tests using @vscode/test-electron
- `suite/index.ts` - Mocha test suite configuration and test file discovery
- `suite/extension.test.ts` - Main extension tests

## Running Tests

To run the tests:

```bash
npm test
```

This will:
1. Compile the TypeScript code
2. Download a VS Code instance for testing (if not already downloaded)
3. Launch VS Code and run the tests
4. Display test results in the console

## Test Coverage

The test suite covers:

1. **Extension Activation**
   - Extension presence check
   - Extension activation
   - Language registration

2. **Language Client**
   - File extension handling (.maxon)
   - Language scheme support
   - Document management

3. **Extension Deactivation**
   - Graceful shutdown
   - Client cleanup

## Prerequisites

Before running tests, ensure dependencies are installed:

```bash
npm install
```

## Writing New Tests

To add new tests:

1. Create a new `.test.ts` file in the `suite/` directory
2. Use the Mocha TDD interface (`suite` and `test` functions)
3. Import necessary modules from `vscode` and the extension
4. Follow the existing test patterns

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
3. Verify the LSP server binary exists at the expected path
4. Check console output for specific error messages
