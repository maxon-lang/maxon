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

When launched as `maxon mcp-server`, the server advertises 8 standard tools.

**An argument a tool does not declare is refused** with `invalidParams`, rather than ignored. An argument that is silently dropped leaves the caller believing their run honoured something it never did — so the arguments listed below are exactly the ones that exist.

### `build`

Compile a Maxon source file, directory, project manifest, or inline snippet.

- **Arguments:**
  - `path` *(string, optional)*: A `.maxon` source file or project directory. Defaults to the current directory (`build.maxon`).
  - `source` *(string, optional)*: Inline Maxon source to build instead of a path. Mutually exclusive with `path`.
  - `output` *(string, optional)*: Destination path for the output binary.
  - `target` *(string, optional)*: A cross-compile target (`x64-windows`, `x64-linux`, `arm64-macos`, `arm64-linux`, `wasm32-wasi`), or the name of a target declared in `build.maxon`. A value containing a dash is passed as `--target=`; a bare word names a manifest target.
  - `emitIr` *(boolean, optional)*: Emit the intermediate representation alongside the build.
- **Returns:** `success`, the `command` that ran, `exitCode`, `stdout`, and `stderr`.

### `run`

Compile (or reuse a cached build of) a Maxon program and execute it immediately.

- **Arguments:**
  - `path` *(string, optional)*: The `.maxon` source file or directory to run.
  - `source` *(string, optional)*: Inline Maxon source to compile and run. Mutually exclusive with `path`; give one.
  - `arguments` *(array of strings, optional)*: Command-line arguments passed to the running program.
- **Returns:** Program exit code and standard output/error.

### `test`

Run a project's native `test` declarations.

- **Arguments:**
  - `path` *(string, optional)*: Project or directory to test (default: current directory).
  - `filter` *(string, optional)*: Selects tests by name or file — case-insensitive, and comma-separated patterns are a union.
- **Returns:** Test execution summary and failure diagnostics.

### `fmt`

Re-print Maxon source in canonical layout.

- **Arguments:**
  - `path` *(string, optional)*: A `.maxon` file or a directory, rewritten **in place**. Defaults to the working directory.
  - `source` *(string, optional)*: Inline Maxon source to format. Nothing is written; the formatted text comes back in `formatted`. Mutually exclusive with `path`.
- **Returns:** With `path`, the formatter's own output. With `source`, the `formatted` text.

### `check`

Perform fast syntactic and semantic validation without generating machine code.

- **Arguments:**
  - `path` *(string, required)*: The file to check.
- **Returns:** Type-check errors, warnings, and diagnostic spans.

### `dump_ir`

Emit compiler Intermediate Representation (IR) for inspection or debugging.

- **Arguments:**
  - `path` *(string, required)*: Path to the source file to inspect.
- **Returns:** The textual IR emitted by the compiler pipeline.

### `lookup_error_code`

Look up a Maxon compiler error code.

- **Arguments:**
  - `code` *(string or integer, required)*: `3014`, `"3014"`, `"E3014"`, or the registry case name `"semanticUnneededCast"`.
- **Returns:** The code, its canonical registry name, the compilation stage its leading digit names, and the registry's documentation of it.

The code, name and stage come from the error-code registry compiled into the binary, so they are answered by any install. The prose lives in the registry's source comments; when the compiler sits in a checkout it is read from there and `documentationAvailable` is `true`, and otherwise that field is `false` and the answer says so.

**A number no registry case claims is refused**, not answered. An explanation of a code that does not exist reads as an answer and is not one.

### `info`

Retrieve compiler metadata and environment information.

- **Arguments:** None.
- **Returns:** Version string, commit hash, commit date, the path of the running executable, and the host target.

---

## Compiler Contributor Mode (`--dev`)

When working on the Maxon compiler repository itself, start the server with `--dev`:

```bash
maxon mcp-server --dev
```

In addition to all 8 standard tools, developer mode enables 3 contributor-specific tools and expands the `build` tool:

### `run_spec_test`

Execute the compiler's language specification test suite (`spec-test`), returning structured pass/fail counts.

- **Arguments:**
  - `filter` *(string, optional)*: Passed to `--filter=`. **One case-sensitive substring** of the `<spec>/<test>` label — not a list.
  - `directory` *(string, optional)*: Spec directory to run. Defaults to `specs`.
  - `updateRequired` *(boolean, optional)*: Regenerate the committed `RequiredIR` blocks. **Always pair with `filter`** — unfiltered it rewrites every golden in the suite.
  - `log` *(string, optional)*: Passed to `--log=` (e.g. `ir:debug`).
  - `network` *(boolean, optional)*: Also run the live network fragments.
  - `target` *(string, optional)*: Cross-compile target (e.g. `wasm32-wasi`).
  - `workers` *(integer, optional)*: Worker subprocess count. A debugging tool; the runner's own default is what the suite runs at.
  - `repoRoot` *(string, optional)*: Absolute path to the Maxon checkout to run in.

### `run_scale_test`

Run the scaling instrument: compile a doubling ladder of generated programs and report per-phase memory and CPU.

**It has no verdict and nothing to pass.** The ratio between rungs is the reading — ×2 is linear, ×4 quadratic.

- **Arguments:**
  - `rungs` *(integer, optional)*: How many rungs to climb (1–8, default 6). Each rung doubles the program.
  - `repeat` *(integer, optional)*: How many times each rung is compiled (1–25, default 3).
  - `note` *(string, optional)*: Record this run in `docs/optimization-log.md` with this text as the reason.
  - `emitCorpus` *(string, optional)*: Dump the generated corpus to this directory and compile nothing.
  - `log` *(string, optional)*: Passed to `--log=`.
  - `repoRoot` *(string, optional)*: Absolute path to the Maxon checkout to run in.

### `spec_test_outcome`

Run spec-tests with a filter, returning structured per-test PASS/FAIL entries plus a failure summary.

- **Arguments:**
  - `filter` *(string, required)*: Passed to `--filter=`. One case-sensitive substring, typically a label like `arithmetic/addition`.
  - `target` *(string, optional)*: Cross-compile target.
  - `network` *(boolean, optional)*: Also run the live network fragments.
  - `repoRoot` *(string, optional)*: Absolute path to the Maxon checkout to run in.

### Contributor `build` extensions

In `--dev` mode, `build` accepts two more arguments:
- `repoRoot` *(string, optional)*: Absolute path of the checkout to build.
- `from` *(string, optional)*: A compiler binary to build WITH, instead of the target tree's own. This is the seed case — the tree's own slot may be empty precisely because that is what is being fixed.

### Which tree, and which compiler in it

One server process serves every checkout and worktree, and its working directory belongs to the editor rather than to you. So:

- `repoRoot` must be **absolute**; a relative path would resolve against the server's own working directory. Relative paths and directories that are not checkouts are refused, never quietly swapped for another tree.
- A tool acting on a tree spawns **that tree's own compiler**, at `<repoRoot>/maxon-bin/.maxon/maxon`. The compiler resolves `stdlib/` by walking up from its own executable, so running the server's binary against your sources would compile them against a different standard library. A tree whose compiler has not been built is refused, naming the `build` tool.
- **Every answer echoes the `repoRoot` it used**, on success and on refusal alike. It is the only way to check that a call acted where you meant.

---

## Concurrent Rebuilding

A common scenario in AI-assisted compiler development is asking the agent to rebuild the compiler binary while the MCP server is actively running.

The server is the compiler, so this is a process being asked to replace its own executable.

- When `maxon build maxon-bin` is invoked, the compiler detects that the output is the running executable image.
- A running executable cannot be deleted, but it can be renamed. The compiler renames the active binary to `maxon.previous` and writes the freshly compiled one in its place.
- The MCP server process keeps running from the vacated image and answers subsequent requests without interruption.

⚠ **The running server is still the compiler it was started as.** It serves from the image that was renamed away, so its answers come from the code you built *from*, not the code you just built. Restart the MCP server when you want the new compiler to answer.

A failed build leaves the slot **empty** rather than restoring the old binary — a stale compiler reporting as current is the failure that rule exists to prevent. The live server keeps answering either way; a tool call naming that tree will refuse until the slot is filled again.
