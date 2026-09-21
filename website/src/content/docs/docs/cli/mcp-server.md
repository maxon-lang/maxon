---
title: MCP Server
description: Connect AI coding agents directly to the Maxon compiler with the built-in Model Context Protocol server.
sidebar:
  order: 6
---

Maxon includes a **Model Context Protocol (MCP)** server built into the compiler binary:

```bash
maxon mcp-server
```

Any MCP-compatible client (Claude Desktop, Claude Code, Cursor, Antigravity, VS Code and others) can use
it to build, run, test, format and inspect Maxon code through structured tools, instead of shell
commands. There is nothing else to install: the server is a command of every `maxon` binary.

## Quick setup

Add the server to your client's configuration.

**Claude Desktop:** `claude_desktop_config.json` (in `%APPDATA%\Claude\` on Windows,
`~/Library/Application Support/Claude/` on macOS):

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

**Cursor, Claude Code and Antigravity:** `.cursor/mcp.json` or `.mcp.json` in your project root, with
the same content.

**Other clients:** configure a stdio server with command `maxon` and arguments `["mcp-server"]`.

## `maxon mcp-server`

```bash
maxon mcp-server         # standard mode
maxon mcp-server --dev   # also exposes the compiler-development tools
```

| Option | Description |
|--------|-------------|
| `--dev` | Enable the tools for working on the Maxon compiler: `run_spec_test`, `run_scale_test`, `spec_test_outcome`, the `repoRoot` argument of `build`, `run`, `test` and `fmt`, and the `from` argument of `build` |

The server reads newline-delimited JSON-RPC 2.0 messages on stdin and writes responses to stdout. It
implements `initialize` (protocol version `2024-11-05`, server name `maxon`), `tools/list` and
`tools/call`.

| Mode | Invocation | Tools |
|------|------------|-------|
| Standard | `maxon mcp-server` | 8: `build`, `run`, `test`, `fmt`, `check`, `dump_ir`, `lookup_error_code`, `info` |
| Developer | `maxon mcp-server --dev` | 11: the standard tools plus `run_spec_test`, `run_scale_test`, `spec_test_outcome` |

**An argument a tool does not declare is refused** with `invalidParams`, never ignored, so the arguments
listed below are exactly the ones that exist. A developer-mode argument sent to a standard-mode server is
refused the same way, as is an argument of the wrong JSON type.

Tools that run a compiler command answer with a JSON object holding `success`, the `command` that ran,
`exitCode`, `stdout` and `stderr`; `check` and `dump_ir` compile inside the server and answer as described
under each. Every tool acts in the server's working directory, which is normally your project.

## Standard tools

### `build`

Compiles a source file, a directory, a manifest target or an inline snippet, as `maxon build` does.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string | Source file or project directory. Omitted, the working directory's `project.maxon` runs. |
| `source` | string | Inline Maxon source to build instead of a path. Give `path` or `source`, not both. |
| `output` | string | Output executable path (`--output=<path>`) |
| `target` | string | A target such as `wasm32-wasi` (a value containing `-` is passed as `--target=`), or the name of a target declared in `project.maxon` (a bare word) |
| `emitIr` | boolean | Also write the Target IR (`--emit-ir`) |

### `run`

Compiles, or reuses a cached build of, a program and runs it, as `maxon execute` does.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string | The `.maxon` file or directory to run |
| `source` | string | Inline Maxon source to compile and run. Give `path` or `source`. |
| `arguments` | array of strings | Command-line arguments for the program |
| `repoRoot` | string | Developer mode only. The checkout whose compiler runs the program; see [Which tree, and which compiler](#which-tree-and-which-compiler). |

The answer carries the program's exit code, stdout and stderr.

### `test`

Runs a project's `test` declarations, as `maxon test` does.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string | Project directory (default: the working directory) |
| `filter` | string | Selects tests by name or file: case-insensitive, comma-separated patterns are a union |
| `repoRoot` | string | Developer mode only. The checkout whose compiler runs the tests; see [Which tree, and which compiler](#which-tree-and-which-compiler). |

### `fmt`

Formats Maxon source, as `maxon fmt` does.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string | File or directory, rewritten **in place**. Omitted, the whole working directory is formatted. |
| `source` | string | Inline source to format. Nothing is written; the result's `formatted` field holds the text. Give `path` or `source`, not both. |
| `repoRoot` | string | Developer mode only. The checkout whose compiler formats; see [Which tree, and which compiler](#which-tree-and-which-compiler). |

### `check`

Compiles a program for the host, as `maxon build` would, and writes nothing: no executable and no
sidecar. The answer's `success` says whether it compiled, and `diagnostics` holds what `maxon build` would
have printed on stderr, one `error E…` line per problem. The compile runs inside the server process, not
in a child `maxon`, so a compiler panic ends the server.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string, required | The `.maxon` file or directory to check |

Because the compile runs in the server process, `check` honours no `repoRoot`: it always answers about the
server's own compiler and standard library. The answer says which those were, in `executable` (the running
compiler's path) and `stdlibRoot` (the `stdlib/` directory it compiled against).

### `dump_ir`

Compiles a program for the host, as `maxon build` would, and answers its Target IR in `ir`: the text
`maxon build --emit-ir` writes. Nothing is written. `success` and `diagnostics` are as for `check`, and
`ir` is empty when the compile fails. Like `check`, it compiles inside the server process, so a compiler
panic ends the server.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string, required | The `.maxon` file or directory to compile |

It carries `executable` and `stdlibRoot` for the same reason `check` does.

### `lookup_error_code`

Looks up a compiler error code.

| Argument | Type | Description |
|----------|------|-------------|
| `code` | string or integer, required | `3014`, `"3014"`, `"E3014"`, or the registry case name `"semanticUnneededCast"` |

The answer gives the code, its registry name, the compilation stage its leading digit names, and its
documentation. The name and stage come from the registry compiled into the binary, so every install
answers them. The documentation is read from the compiler's source when the compiler sits in a source
checkout; otherwise `documentationAvailable` is `false`. A number no error code claims is refused.

### `info`

Takes no arguments. Returns `name`, `version`, `commit`, `commitDate`, `executable` (the running
compiler's path) and `hostTarget`.

## Developer tools (`--dev`)

These tools are for working on the Maxon compiler itself, in a checkout of its repository.

### Which tree, and which compiler

One server can serve several checkouts or worktrees, so every developer tool, and `build`, `run`, `test` and
`fmt` in developer mode, takes a `repoRoot` argument:

| Argument | Type | Description |
|----------|------|-------------|
| `repoRoot` | string | Absolute path of the Maxon checkout to act in; that tree's own compiler is the one that runs. |

- `repoRoot` must be **absolute**. A relative path, or a directory that is not a Maxon checkout, is
  refused, never replaced by another tree.
- A tool acting on a tree runs **that tree's own compiler**, `<repoRoot>/maxon-bin/.maxon/maxon`, because
  the compiler finds its standard library by walking up from its own executable. A tree whose compiler
  has not been built is refused, naming the `build` tool.
- **Omitted, the default differs by tool.** `build`, `run_spec_test`, `run_scale_test` and
  `spec_test_outcome` act on the checkout the server's own compiler sits in. `run`, `test` and `fmt` act in
  the host's working directory, driven by the server's own compiler and naming no tree — their `path`
  arguments are the caller's, and are resolved against the caller's directory.
- **An answer echoes the `repoRoot` it used** whenever it acted on a named tree, on success and on refusal.
  A tool that named no tree leaves the field out rather than reporting an empty one.
- `check` and `dump_ir` take no `repoRoot`: they compile in the server process and answer with the
  `executable` and `stdlibRoot` that did so.

In developer mode `build` also accepts:

| Argument | Type | Description |
|----------|------|-------------|
| `repoRoot` | string | The checkout to build in |
| `from` | string | A compiler binary to build **with**, instead of the tree's own (for a tree whose compiler slot is empty or broken) |

### `run_spec_test`

Runs `maxon spec-test` and returns `passed`, `failed`, `total`, `summaryParsed`, `durationMs`,
`exitCode`, `memoryLeak` (exit code 101) and `rawTail` (the last 4 KiB of output).

| Argument | Type | Description |
|----------|------|-------------|
| `filter` | string | `--filter=`: one case-sensitive substring of the `<spec>/<test>` label, not a list |
| `directory` | string | Spec directory (default `specs`) |
| `updateRequired` | boolean | `--update-required`: rewrite the committed IR goldens. Always pair it with `filter`; unfiltered, it rewrites every golden. |
| `log` | string | `--log=` value, such as `ir:debug` |
| `network` | boolean | `--network`: also run the cases that reach a real external host |
| `target` | string | `--target=` value, such as `wasm32-wasi` |
| `workers` | integer | `--workers=`: the worker process count. A debugging aid; the default is what the suite normally runs at. |
| `repoRoot` | string | The checkout to run in |

### `run_scale_test`

Runs `maxon scale-test`, the scaling instrument, and returns its whole result document as `result`. It
has no verdict: the ratio between rungs is the reading (×2 linear, ×4 quadratic).

| Argument | Type | Description |
|----------|------|-------------|
| `rungs` | integer | Rungs to climb (1–8, default 6). Each rung doubles the program. |
| `repeat` | integer | Compiles per rung (1–25, default 1). CPU takes the minimum; memory is cross-checked. |
| `note` | string | Record the run in `docs/optimization-log.md` with this text as the reason |
| `emitCorpus` | string | Write the generated programs to this directory and compile nothing |
| `log` | string | `--log=` value |
| `repoRoot` | string | The checkout to run in |

### `spec_test_outcome`

Runs spec tests for a filter and returns a `tests` array of `{spec, test, status}` entries (`PASS` or
`FAIL`), a `failures` array of `{spec, message}`, and the counts.

| Argument | Type | Description |
|----------|------|-------------|
| `filter` | string, required | One case-sensitive substring, typically a label like `arithmetic/addition` |
| `target` | string | `--target=` value |
| `network` | boolean | Also run the cases that reach a real external host |
| `repoRoot` | string | The checkout to run in |

## Rebuilding the compiler under a running server

The server is the compiler, so building the compiler (for example with the `build` tool and
`path: "maxon-bin"`) replaces the executable the server runs from. That works: once the compile
succeeds, the running image is renamed to `maxon.previous` and the new compiler is written in its
place, and the server keeps answering from the renamed image. A server started before an earlier
rebuild is running an older `maxon.previous`; that file is renamed aside to `maxon.retired-<stamp>` and
deleted by a later rebuild once the server has exited. A failed build leaves the running compiler in the
slot.

**The running server is still the compiler it was started as.** Restart it when you want the newly
built compiler to answer.
