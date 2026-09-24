---
name: maxon-coder
description: Write, edit or review Maxon code in this repository — a .maxon or .maxtest file, a spec case, stdlib/, runtime/, or the compiler's own source under maxon-bin/. Loads the language rules, the extra rules for where the code lives, and the loop that compiles every edit before it is handed back. Invoke it before writing any Maxon, and tell every agent you dispatch to write Maxon to invoke it too.
---

# Writing Maxon

## 1. Read the rulebook first

**Read `docs/WRITING_MAXON_CODE.md` before writing any Maxon, every session.** It is the one source of the
language's rules — the syntax that does not exist, the mandatory rules, the idioms, tests — and this skill
does not repeat it. Then, as needed:

- `docs/LANGUAGE_REFERENCE.md` — the full semantics.
- `docs/STDLIB_REFERENCE.md` — the standard library's API. Read a stdlib signature there (or in `stdlib/`)
  before calling it; do not guess a method name.
- `lookup_error_code` — what a diagnostic means, by number (`"E3014"`) or case name.

⚠ If the compiler and the doc disagree, the compiler is right about what compiles today and the doc is a
defect: fix the doc in the same change (the `documenter` skill owns that step).

## 2. Where the code lives decides the extra rules

| Where | Extra rules |
|---|---|
| `maxon-bin/` — the compiler | Read `maxon-bin/CLAUDE.md` first. A diagnostic names `ErrorCode.<case>`, never `"E3010"`; `maxon-bin/Compiler/ErrorCodeRegistry.maxon` is hand-authored — add a case there. A change under `Compiler/Runtime/` needs two self-compiles: ask `scripts/self-compiles-needed.sh`. |
| `runtime/` | The tier rules in `maxon-bin/CLAUDE.md`: no managed values (E3153), no guarded construct in an always-reached family, a restated geometry figure owes its pin. |
| `stdlib/` | A symbol callers outside the stdlib use is `public`, not `export`. No instrumentation (no `Log` calls) in stdlib algorithms. |
| `specs/` | The language's canonical definition. Under `/land` it is read-only except to the agent briefed to write the cases. A case is a program in a spec `.md` file, pinned by `exitcode` / `stdout` / `maxoncstderr` blocks. |
| `tests/` | Read `tests/README.md` first. A fixture `.maxon` is stored as `<name>.fixture`; one test per `.maxtest` file; expectations are generated, never hand-written. |
| anywhere else | A standalone program or example: `main` returns `ExitCode`. |

## 3. No comments

⛔ **Write no comments** — not a `//`, not a `///`, not the doc block above a registry case. Code changes
shape while it is being written, so a comment written now is rewritten and mostly thrown away. **The
`documenter` skill writes every comment, once, on the finished diff before the commit** — it is the one
exception to this rule. Leave the *why* of an unobvious shape in your report instead; the documenter builds
the comments from it. A block holding only a comment is still empty (E3082).

## 4. Compile every edit before you hand it back

**Code you hand back compiles.** A syntax or type error left for review to find is a round trip wasted.
After each edit:

1. **Format the file** — `mcp__maxon__fmt` with `path:` naming **the file**. With no path it formats the
   whole current directory.
2. **Compile it** with the check that fits where it lives:

| You edited | Check |
|---|---|
| a standalone program or example | `mcp__maxon__check`, then `mcp__maxon__execute` if it should run — `path:` the file for a one-file program, **the directory** for one spread over several files |
| a `.maxtest` or its project | `mcp__maxon__test` (`path:` the project directory, `filter:` the test) |
| a spec case | `mcp__maxon__spec_test_outcome` (`filter:` ONE case-sensitive substring of `<spec>/<test>`) |
| `maxon-bin/`, `stdlib/` or `runtime/` | build the slot — `./maxon-bin/.maxon/maxon run build`, or `mcp__maxon__build` with `path: "maxon-bin"` — then the spec filter that owns the behaviour |

⚠ **Traps in the instruments:**

- `check` and `dump_ir` compile inside the MCP server, so they answer with the **server's** compiler, and a
  running server keeps serving the compiler it started as after a rebuild. Read the `executable` field, and
  restart the server when a rebuild must be what answers.
- A directory path compiles every `.maxon` under it as one program, so two probe programs in one
  directory are one program — give each its own. A file path compiles that file ALONE: a `main.maxon`
  that uses another file's types fails there, and can report a misleading error (an undefined type behind
  a later `try` reads as E2015 "the expression after `try` is not a call").
- `execute` runs the program with the host's working directory — the repository root. A probe that writes
  a file writes it there; delete it.
- A diagnostic run stops at the first failing phase, so fixing one error can reveal the next. Re-run until
  clean.
- **Exit 101 is a leak.** Read exit codes; never grep output for a success string.
- The compiler binary is gitignored and nothing rebuilds it, so a stale one lies in both directions —
  build before you trust a run of compiler, stdlib or runtime changes.
- In a worktree, pass `repoRoot` (the worktree's absolute path) to the tools that act in a tree — `build`,
  `run_spec_test`, `spec_test_outcome`, `run_scale_test`, `execute`, `test`, `fmt`. `check`, `dump_ir`,
  `lookup_error_code` and `info` take none.

## 5. Traps the compiler will not explain well

- **A default parameter on an overloaded name is E2015** unless every declaration of the name has the same
  parameters and the same defaults. Give the short form its own overload that calls the long one, and add
  the new parameter LAST — a second function-typed parameter at argument 0 withholds closure parameter
  inference from every caller.
- **A static method is not a function value.** `apply(Ops.twice, …)` fails with a misleading
  `E2010: Expected 'min or max'`; pass `function(n) gives Ops.twice(n)`.
- **An unmarked field is private to its type (E3014); an unmarked method is private to its file (E3008).**
  An `export` no other file uses is E3092, and an exported signature naming a file-private typealias is
  E3167 — so a one-file program marks nothing `export`, and files sharing one directory use `module`.
- **An unused parameter is E3012**, like an unused variable.
