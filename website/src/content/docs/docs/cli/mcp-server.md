---
title: MCP Server
description: Connect AI coding agents directly to the Maxon compiler with the built-in Model Context Protocol server.
sidebar:
  order: 2
---

Maxon includes a native **Model Context Protocol (MCP)** server built directly into the compiler binary:

```bash
maxon mcp-server
```

Because Maxon was designed from the ground up as a language *written by AI, for AI*, the compiler provides first-class tooling for AI coding assistants. Rather than relying on external scripts or fragile shell wrappers, any MCP-compatible client (such as Claude Desktop, Cursor, Antigravity, or VS Code) can interact directly with the Maxon compiler over standard input and output using JSON-RPC 2.0.

There are no Node.js dependencies, external packages, or separate services to install — `maxon mcp-server` is an integral command in every Maxon distribution.

---

## Quick Setup

Add the Maxon MCP server to your client's configuration file:

### Claude Desktop

Add to `claude_desktop_config.json` (`%APPDATA%\Claude\` on Windows, `~/Library/Application Support/Claude/` on macOS):

```json
{
  "mcpServers": {
    "maxon": {
      "command": "maxon",
      "args": ["mcp-server"]
    }
  }
}
```

### Cursor & Antigravity

Add to `.cursor/mcp.json` or `.mcp.json` in your project root:

```json
{
  "mcpServers": {
    "maxon": {
      "command": "maxon",
      "args": ["mcp-server"]
    }
  }
}
```

### VS Code

Configure via your preferred MCP client extension or workspace configuration pointing to:
- **Command:** `maxon`
- **Arguments:** `["mcp-server"]`

---

## Operating Modes

The Maxon MCP server provides two operational modes:

| Mode | Invocation | Exposed Tools | Audience |
|---|---|---|---|
| **Standard Mode** | `maxon mcp-server` | 8 tools (`build`, `run`, `test`, `fmt`, `check`, `dump_ir`, `lookup_error_code`, `info`) | Application and library developers using Maxon |
| **Developer Mode** | `maxon mcp-server --dev` | 11 tools (all standard tools plus `run_spec_test`, `run_scale_test`, `spec_test_outcome`, plus worktree options on `build`) | Contributors developing the Maxon compiler |

---

## Standard Tools

When launched as `maxon mcp-server`, the server advertises 8 standard tools:

### `build`

Compile a Maxon source file, directory, or project manifest.

- **Arguments:**
  - `path` *(string, optional)*: Path to a `.maxon` source file or project directory. Defaults to the current directory (`build.maxon`).
  - `output` *(string, optional)*: Destination path for the output binary.
  - `target` *(string, optional)*: Target architecture and OS (e.g. `x64-windows`, `arm64-macos`, `x64-linux`).
  - `emitIr` *(boolean, optional)*: If `true`, emits intermediate representation (`.ir`) files alongside the build.
- **Returns:** Structured execution outcome with `success`, `exitCode`, `stdout`, and `stderr`.

### `run`

Compile (or reuse a cached build of) a Maxon program and execute it immediately.

- **Arguments:**
  - `path` *(string, required)*: Path to the `.maxon` source file or directory to run.
  - `arguments` *(array of strings, optional)*: Command-line arguments passed to the running program.
- **Returns:** Program exit code and standard output/error.

### `test`

Run a project's native `test` declarations.

- **Arguments:**
  - `path` *(string, optional)*: Project or directory to test (default: current directory).
  - `filter` *(string, optional)*: Filter pattern to select matching test names or files.
  - `timeout` *(number, optional)*: Per-test timeout in milliseconds.
- **Returns:** Test execution summary and failure diagnostics.

### `fmt`

Format Maxon source code in-place to canonical layout.

- **Arguments:**
  - `path` *(string, optional)*: Path to a `.maxon` file or directory.
  - `check` *(boolean, optional)*: When `true`, reports whether files are already formatted without modifying them on disk.
- **Returns:** Formatter results and modified file list.

### `check`

Perform fast syntactic and semantic validation without generating machine code.

- **Arguments:**
  - `path` *(string, optional)*: Path to file or directory to check.
- **Returns:** Type-check errors, warnings, and diagnostic spans.

### `dump_ir`

Emit compiler Intermediate Representation (IR) for inspection or debugging.

- **Arguments:**
  - `path` *(string, required)*: Path to the source file to inspect.
  - `stage` *(string, optional)*: Pipeline stage to dump (e.g. `hir`, `lir`, `opt`).
- **Returns:** The textual IR emitted by the compiler pipeline.

### `lookup_error_code`

Look up comprehensive diagnostic documentation for a Maxon compiler error code (e.g. `E2001`, `E3014`).

- **Arguments:**
  - `code` *(string, required)*: The error code identifier (such as `"E3014"` or `"3014"`).
- **Returns:** Detailed explanation of the error, the compiler stage that produced it, the language rule violated, and recommended corrective actions.

### `info`

Retrieve compiler metadata and environment information.

- **Arguments:** None.
- **Returns:** Version string, commit hash, build date, host target architecture, and available cross-compilation targets.

---

## Compiler Contributor Mode (`--dev`)

When working on the Maxon compiler repository itself, start the server with `--dev`:

```bash
maxon mcp-server --dev
```

In addition to all 8 standard tools, developer mode enables 3 contributor-specific tools and expands the `build` tool:

### `run_spec_test`

Execute the compiler's language specification test suite (`spec-test`).

- **Arguments:**
  - `filter` *(string, optional)*: Case-insensitive filter pattern matching spec test paths or names.
  - `update` *(boolean, optional)*: When `true`, re-blesses test expectations against the current compiler output.
  - `repoRoot` *(string, optional)*: Absolute path to the Maxon repository checkout.

### `run_scale_test`

Run compiler scale, throughput, and benchmark tests.

- **Arguments:**
  - `filter` *(string, optional)*: Filter pattern selecting scale tests to run.
  - `repoRoot` *(string, optional)*: Absolute path to the Maxon repository checkout.

### `spec_test_outcome`

Parse and return structured JSON outcomes for spec test runs.

- **Arguments:**
  - `filter` *(string, required)*: Test filter to evaluate.
  - `repoRoot` *(string, optional)*: Absolute path to the Maxon repository checkout.

### Contributor `build` Extensions

In `--dev` mode, the `build` tool accepts two additional parameters:
- `repoRoot` *(string, optional)*: Absolute path to the repository root where `build.maxon` is located.
- `from` *(string, optional)*: Path to an alternative compiler binary to use when performing the build.

---

## Concurrent Rebuilding

A common scenario in AI-assisted compiler development is asking the agent to rebuild the compiler binary while the MCP server is actively running.

Maxon handles this seamlessly on Windows, macOS, and Linux:

- When `maxon build maxon-bin` is invoked, the compiler detects that the output target is the currently running executable image.
- On Windows, where running binaries cannot be directly overwritten, the compiler's `vacateRunningImage` routine automatically renames the active executable to `maxon.previous.exe` and writes the freshly compiled binary to `maxon.exe`.
- The MCP server process safely continues running in memory on the vacated image, completes the tool execution, and responds to subsequent requests without interruption.
