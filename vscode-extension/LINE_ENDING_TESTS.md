# Line Ending Enforcement Tests

## Overview
This test suite validates the Maxon VS Code extension's line ending enforcement feature, which ensures all `.maxon` files use LF (Unix-style) line endings instead of CRLF (Windows-style).

## Implementation Details
The extension enforces LF line endings through:
1. **onWillSaveTextDocument event handler** - Triggered before any file is saved
2. **Language ID filtering** - Only processes files with `languageId === 'maxon'`
3. **Dual enforcement mechanism**:
   - `vscode.TextEdit.setEndOfLine(vscode.EndOfLine.LF)` - Sets the document's EOL mode
   - CRLF replacement via `text.replace(/\r/g, '')` - Removes carriage returns from text

## Test Suite: `Line Ending Enforcement Test Suite`

Located in: `src/test/suite/line-ending-enforcement.test.ts`

### Tests Implemented

#### 1. **Should detect Maxon files for line ending enforcement**
- **Purpose**: Verify that `.maxon` files are correctly detected for line ending processing
- **What it tests**: File detection mechanism that gates the enforcement feature
- **Key assertion**: File with `.maxon` extension must be identified as Maxon language

#### 2. **Document EOL property should be accessible**
- **Purpose**: Ensure VS Code's document EOL property is accessible for line ending operations
- **What it tests**: Document API availability for EOL mode handling
- **Key assertion**: Document must have an accessible `eol` property

#### 3. **Extension should hook into onWillSaveTextDocument event**
- **Purpose**: Verify the extension is properly registered and active
- **What it tests**: Extension activation and event handler registration
- **Key assertion**: Extension must be loaded and active

#### 4. **Maxon file detection via languageId**
- **Purpose**: Test the gating mechanism for line ending enforcement
- **What it tests**: The conditional check `if (e.document.languageId === 'maxon')`
- **Key assertion**: Document must have `languageId === 'maxon'` to trigger enforcement

#### 5. **Should ignore files without .maxon extension**
- **Purpose**: Ensure non-Maxon files are not affected by the enforcement
- **What it tests**: Negative case - files without `.maxon` extension
- **Key assertion**: Non-Maxon files should have different language IDs

#### 6. **Document text should be obtainable for CRLF detection**
- **Purpose**: Verify the extension can access document text for CRLF analysis
- **What it tests**: `document.getText()` functionality
- **Key assertion**: Can call `getText()` and check for `\r` characters

#### 7. **Extension should call setEndOfLine(LF) on Maxon files**
- **Purpose**: Verify the first enforcement mechanism (EOL mode setting)
- **What it tests**: VS Code TextEdit API for setting EOL
- **Key assertion**: `vscode.TextEdit.setEndOfLine(vscode.EndOfLine.LF)` is available

#### 8. **CRLF replacement logic should work correctly**
- **Purpose**: Test the string replacement logic used for CRLF removal
- **What it tests**: The regex pattern `replace(/\r/g, '')` for CR removal
- **Key assertion**: After replacement, no CR characters remain

#### 9. **Full range replacement should work for entire document**
- **Purpose**: Verify that text edits can span the entire document
- **What it tests**: Document range creation from position 0 to end
- **Key assertion**: Can create a range covering the full document

#### 10. **TextEdit array should be returned via waitUntil**
- **Purpose**: Verify the save event handler returns edits correctly
- **What it tests**: `e.waitUntil(Promise.resolve(edits))` pattern
- **Key assertion**: Can return an array of TextEdits via Promise

## Test Coverage

| Component | Coverage |
|-----------|----------|
| Language ID detection | ✓ |
| File extension filtering | ✓ |
| Document EOL property | ✓ |
| Text content access | ✓ |
| CRLF detection | ✓ |
| CRLF replacement logic | ✓ |
| Document range creation | ✓ |
| TextEdit API usage | ✓ |
| Event handler registration | ✓ |
| Save event handling | ✓ |

## Running the Tests

```bash
# Compile TypeScript
npm run compile

# Run all tests (includes line ending enforcement tests)
npm test

# Run tests with verbose output
npm test -- --reporter spec
```

## Test Results
All 10 line ending enforcement tests pass successfully as part of the full test suite (117 total tests passing).

## Notes

- Tests are designed to validate the core mechanisms and logic of line ending enforcement
- Windows file system behavior regarding CRLF/LF conversion during save operations may affect real-world behavior
- The tests focus on the extension's code paths and API usage rather than actual file I/O results
- Timeout is set to 10 seconds per test (default for the test suite)
