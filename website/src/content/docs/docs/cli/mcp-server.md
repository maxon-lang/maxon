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
it to build, run, test, format, inspect and debug Maxon code through structured tools, instead of shell
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

**Other clients:** configure a stdio server with command `maxon` and arguments `["mcp-server"]`. A
client that cannot spawn a process can reach the same tools over [the HTTP transport](#the-http-transport).

## `maxon mcp-server`

```bash
maxon mcp-server                  # standard mode, over stdio
maxon mcp-server --dev            # also exposes the compiler-development tools
maxon mcp-server --http           # the same roster over loopback HTTP, on a free port
maxon mcp-server --http=7823      # ... on a port you choose
```

| Option | Description |
|--------|-------------|
| `--dev` | Enable the tools for working on the Maxon compiler: `run_spec_test`, `run_scale_test`, `spec_test_outcome`, the `repoRoot` argument of `build`, `execute`, `test` and `fmt`, and the `from` argument of `build` |
| `--http=<port>` | Serve the same tool roster over HTTP on `127.0.0.1`, on `<port>` (1 to 65535). Written bare as `--http` it takes whichever free port the kernel gives. |
| `--idle-timeout=<seconds>` | Close an HTTP session idle for this long, reaping any debug session it held (1 to 86400, default 300). See [Sessions and idleness](#sessions-and-idleness). Only with `--http`. |
| `--max-sessions=<n>` | How many HTTP sessions may be live at once (1 to 4096, default 8); an `initialize` past the cap is answered `429`. Only with `--http`. |
| `--exit-on-stdin-eof` | End the HTTP server when its stdin reaches end of file: the listener closes, every session is reaped and the server exits 0, so a server a parent process started ends with that parent. Only with `--http`. |

Any other spelling, and a value outside its range, is refused with exit 1, and so is `--idle-timeout`,
`--max-sessions` or `--exit-on-stdin-eof` without `--http`. The HTTP transport binds loopback only, so a
host or interface spelling such as `--http=0.0.0.0:80` is refused the same way.

The stdio server reads newline-delimited JSON-RPC 2.0 messages on stdin and writes responses to stdout. It
implements `initialize` (protocol version `2024-11-05`, server name `maxon`), `tools/list` and
`tools/call`.

| Mode | Invocation | Tools |
|------|------------|-------|
| Standard | `maxon mcp-server` | 21: `build`, `execute`, `test`, `fmt`, `check`, `dump_ir`, `lookup_error_code`, `info`, and the thirteen `debug_*` tools |
| Developer | `maxon mcp-server --dev` | 24: the standard tools plus `run_spec_test`, `run_scale_test`, `spec_test_outcome` |

## The HTTP transport

`--http` serves the SAME roster as stdio — name for name — for a client that cannot spawn a process. The
server binds `127.0.0.1` and prints the endpoint it chose as ONE JSON line on stdout, the only line it
writes there:

```json
{"endpoint":"http://127.0.0.1:7823/mcp"}
```

`/mcp` is the only route; every other path is `404`. `POST /mcp` carries one JSON-RPC object and is
answered `200 application/json`, or `202` with an empty body when the message is a notification, which
JSON-RPC forbids answering; a body that is something other than one JSON object is `400`. `DELETE /mcp`
ends the session and answers `204`. `GET /mcp` is `405`, because the transport carries answers to
requests alone, and `PUT`, `HEAD`, `PATCH` and `OPTIONS` are `405` too.

`initialize` mints a 32-hex-character session id and returns it in the `Mcp-Session-Id` response header;
every other message must carry that header, and one naming a session that has ended, or none at all, is
`404`. An `initialize` that already carries a session id is `400`.

A request must name the server in its `Host` header, exactly `127.0.0.1:<port>` or `localhost:<port>`
with the server's own port, and an `Origin` header, when present, must be exactly
`http://127.0.0.1:<port>` or `http://localhost:<port>`; any other request is refused `403`. A browser
sets both headers from the page's own address, so a page open in a browser is refused. A request target
that does not begin with `/` is `400`. The server runs until its process ends or, with
`--exit-on-stdin-eof`, until its stdin closes.

### Sessions and idleness

The server answers one request at a time: a request is read, handled and answered before the next
connection is accepted, so a long call — a build, or a `debug_continue` waiting for a stop — holds
every other session's requests until it answers. The time spent handling any request is credited to
every session, so a session's idle time counts only the time between requests, and a request queued
behind another session's long call finds its own session live.

A session idle for `--idle-timeout` seconds is closed and its debug session reaped. The server sweeps
before it serves each request and wakes on its own when the next session is due, so an idle session
closes on time whatever the traffic. When the server ends, every session still open is reaped.

## Arguments and answers

**An argument a tool does not declare is refused** with `invalidParams`, so the arguments listed below
are exactly the ones that exist. A developer-mode argument sent to a standard-mode server is refused the
same way, as is an argument of the wrong JSON type, an array argument holding anything but strings, or a
number outside the range the tool declares.

**`timeoutSeconds`** bounds the `maxon` command a tool runs: `build`, `execute`, `test`, `fmt`, the
developer tools, and the build `debug_start` makes of a `source`. It is a number of seconds, fractions
honoured, from 0.001 to 922337203685 (default 600). A command still running at the bound has its whole
process tree ended, and the call answers an error carrying what the command had written. For `execute`
and `test` the bound covers the program or the tests as well as the build.

Tools that run a compiler command answer with a JSON object holding `success`, the `command` that ran,
`exitCode`, `stdout` and `stderr`; `check` and `dump_ir` compile inside the server and answer as described
under each. Every tool acts in the server's working directory, which is normally your project.

## Standard tools

### `build`

Compiles a source file, a directory, a project target or an inline snippet, as `maxon build` does.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string | Source file or project directory. Omitted, the working directory's `.maxproj` file builds its target. |
| `source` | string | Inline Maxon source to build in place of a path. `path` and `source` exclude each other. |
| `output` | string | Output executable path (`--output=<path>`) |
| `target` | string | A target triple such as `wasm32-wasi`, passed as `--target=` when it is one of the five; any other value names a target of the `.maxproj` file, each `_` written `-` |
| `emitIr` | boolean | Also write the Target IR (`--emit-ir`) |
| `timeoutSeconds` | number | Seconds the build may take (default 600); see [Arguments and answers](#arguments-and-answers) |

### `execute`

Compiles, or reuses a cached build of, a program and runs it, as `maxon execute` does.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string | The `.maxon` file or directory to run |
| `source` | string | Inline Maxon source to compile and run. Give `path` or `source`. |
| `arguments` | array of strings | Command-line arguments for the program |
| `timeoutSeconds` | number | Seconds the build and the run together may take (default 600) |
| `repoRoot` | string | Developer mode only. The checkout whose compiler runs the program; see [Which tree, and which compiler](#which-tree-and-which-compiler). |

The answer carries the program's exit code, stdout and stderr.

### `test`

Runs a project's `test` declarations, as `maxon test` does.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string | Project directory (default: the working directory) |
| `filter` | string | Selects tests by name or file: case-insensitive, comma-separated patterns are a union |
| `timeoutSeconds` | number | Seconds the build and the tests together may take (default 600) |
| `repoRoot` | string | Developer mode only. The checkout whose compiler runs the tests; see [Which tree, and which compiler](#which-tree-and-which-compiler). |

### `fmt`

Formats Maxon source, as `maxon fmt` does.

| Argument | Type | Description |
|----------|------|-------------|
| `path` | string | File or directory, rewritten **in place**. Omitted, the whole working directory is formatted. |
| `source` | string | Inline source to format and return in the result's `formatted` field, leaving every file as it is. `path` and `source` exclude each other. |
| `timeoutSeconds` | number | Seconds the format may take (default 600) |
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

## Debug tools

Thirteen tools hold ONE live debug session per MCP session and drive it. Over stdio the process is the
session, and the debuggee is reaped when stdin ends; over `--http` the session is the one the
`Mcp-Session-Id` names, and the debuggee is reaped when that session is deleted or swept, or the server
ends.

Every `debug_*` tool answers `{"state": "stopped" | "running" | "ended" | "none", "events": [ … ],
"output": [ … ]}` — the live state, the debugger events this call produced (the same objects
`maxon debug --batch` writes, parsed rather than quoted), and the debuggee's stdout and stderr lines since
the previous call. A tool other than `debug_start` called with no live session is an error naming
`debug_start`.

The debugger needs the in-process debug agent, which is emitted by default on `x64-windows` only.

### `debug_start`

Launches a program under the debugger, parked before `main`, so a fresh session reports `stopped`. A
`debug_start` while a session is live launches the new program first; once it has started, the live
session is reaped and its remaining output leads the reply. A start that fails leaves the live session as it
was.

| Argument | Type | Description |
|----------|------|-------------|
| `executable` | string | An already-built program to debug. Exactly one of `executable` and `source` is given. |
| `source` | string | A `.maxon` file or project directory to build with this compiler and then debug |
| `args` | array of strings | Command-line arguments for the program being debugged |
| `env` | array of strings | Environment variables for the debuggee, each spelled `NAME=VALUE` |
| `stopTimeoutSeconds` | number | How long a command waits for the program to stop, and the budget for one step: seconds, fractions honoured, from 0.001 to 922337203685 (default 10) |
| `trace` | boolean | Record the DebugStream ring, so `debug_trace` has events to report. A `source` is then built with `--debugstream`; an `executable` must already have been |
| `maxProcs` | integer | Pin the debuggee's scheduler to this many processors, 1 to 4294967295 (`MAXON_MAX_PROCS`) |
| `timeoutSeconds` | number | Seconds the build of a `source` may take (default 600). It bounds a build, so given with `executable` it is refused by name. |

A `source` is built with debug info into the host's Maxon cache and removed when the session ends; see
[`maxon cache`](/docs/cli/#maxon-cache) for what happens to a build its server left behind. A `.maxtest` file,
as either argument, is refused by name: a test file's debug build is `maxon test --list --build`, and the
test binary it builds is the `executable` to give. A `.maxon` file or a directory given as `executable`,
and anything else given as `source`, is refused with the argument to use.

### `debug_break`

| Argument | Type | Description |
|----------|------|-------------|
| `target` | string | Required. `file:line`, a bare line number, a function name, or `*0x<offset>` |
| `condition` | string | Break only when this holds, such as `i > 3`; the agent evaluates it itself |

### `debug_clear`

| Argument | Type | Description |
|----------|------|-------------|
| `target` | string | Required. The `file:line`, line number, function name or `*0x<offset>` the breakpoint was armed on |

### `debug_continue`

No arguments. It STARTS a session that has not run yet and resumes one that is stopped.

### `debug_step`

| Argument | Type | Description |
|----------|------|-------------|
| `kind` | string | `step` into, `next` over, `finish` out of this frame, or `until` the next greater line. Default `step`. |

### `debug_pause`

No arguments. Interrupts a running program that stops on nothing.

### `debug_backtrace`

No arguments. Walks the stopped program's stack.

### `debug_locals`

No arguments. Reports every local of the stopped frame with its value.

### `debug_eval`

| Argument | Type | Description |
|----------|------|-------------|
| `expression` | string | Required. A local, or a dotted path through one, such as `config.retries` |

### `debug_threads`

No arguments. Lists the program's green threads.

### `debug_gt`

| Argument | Type | Description |
|----------|------|-------------|
| `action` | string | Required. `select` a green thread, `clear` the selection, `park` one, `resume` one, or `backtrace` one |
| `thread` | string | The green-thread id `debug_threads` reported, or a selector. Not read by `clear`. |

### `debug_trace`

| Argument | Type | Description |
|----------|------|-------------|
| `count` | integer | How many of the most recent DebugStream events to report (default 20). Needs a session started with `trace`. |

### `debug_stop`

No arguments. Ends the session and REAPS the debuggee: the program ends where it stands.

## Developer tools (`--dev`)

These tools are for working on the Maxon compiler itself, in a checkout of its repository.

### Which tree, and which compiler

One server can serve several checkouts or worktrees, so every developer tool, and `build`, `execute`, `test`
and `fmt` in developer mode, takes a `repoRoot` argument:

| Argument | Type | Description |
|----------|------|-------------|
| `repoRoot` | string | Absolute path of the Maxon checkout to act in; that tree's own compiler is the one that runs. |

- `repoRoot` must be **absolute**. A relative path, or a directory that is not a Maxon checkout, is
  refused, never replaced by another tree.
- A tool acting on a tree runs **that tree's own compiler**, `<repoRoot>/maxon-bin/.maxon/maxon`, because
  the compiler finds its standard library by walking up from its own executable. A tree whose compiler
  has not been built is refused, naming the `build` tool.
- **Omitted, the default differs by tool.** `build`, `run_spec_test`, `run_scale_test` and
  `spec_test_outcome` act on the checkout the server's own compiler sits in. `execute`, `test` and `fmt` act in
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
| `filter` | string or array of strings | One `--filter=` per pattern, each a case-sensitive substring of the `<spec>/<test>` label. The run takes every case any pattern selects; a pattern that selects nothing refuses the run, and an empty pattern is a tool error. A comma is part of a pattern. |
| `directory` | string | Spec directory (default `specs`) |
| `updateRequired` | boolean | `--update-required`: rewrite the committed IR goldens. Always pair it with `filter`; unfiltered, it rewrites every golden. |
| `log` | string | `--log=` value, such as `ir:debug` |
| `network` | boolean | `--network`: also run the cases that reach a real external host |
| `target` | string | `--target=` value, such as `wasm32-wasi` |
| `workers` | integer | `--workers=`: the worker process count. A debugging aid; the default is what the suite normally runs at. |
| `timeoutSeconds` | number | Seconds the run may take (default 600) |
| `repoRoot` | string | The checkout to run in |

### `run_scale_test`

Runs `maxon scale-test`, the scaling instrument, and returns its whole result document as `result`. It
has no verdict: the ratio between rungs is the reading (×2 linear, ×4 quadratic).

| Argument | Type | Description |
|----------|------|-------------|
| `rungs` | integer | Rungs to climb (1–8, default 6). Each rung doubles the program. |
| `repeat` | integer | Compiles per rung (1–25, default 1). CPU takes the minimum; memory is cross-checked. |
| `note` | string | Record the run in `docs/optimization-log.md` with this text as the reason |
| `emitCorpus` | string | Write the generated programs to this directory and stop there |
| `log` | string | `--log=` value |
| `timeoutSeconds` | number | Seconds the run may take (default 600) |
| `repoRoot` | string | The checkout to run in |

### `spec_test_outcome`

Runs spec tests for a filter and returns a `tests` array of `{spec, test, status}` entries (`PASS` or
`FAIL`), a `failures` array of `{spec, message}`, and the counts.

| Argument | Type | Description |
|----------|------|-------------|
| `filter` | string or array of strings, required | One `--filter=` per pattern, each a case-sensitive substring, typically a label like `arithmetic/addition`. At least one pattern, none of them empty. |
| `target` | string | `--target=` value |
| `network` | boolean | Also run the cases that reach a real external host |
| `workers` | integer | `--workers=`: the worker process count. A debugging aid; the default is what the suite normally runs at. |
| `timeoutSeconds` | number | Seconds the run may take (default 600) |
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
