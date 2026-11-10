"""Smoke test for the LSP E3010 quick fix.

Launches `maxon lsp-server`, opens a small Maxon document with an unneeded
cast, waits for the E3010 diagnostic, then requests code actions and verifies
the response contains a "Remove unneeded cast" CodeAction with the expected
WorkspaceEdit.

Exits 0 on success, non-zero on any mismatch."""

import contextlib
import os
import sys
import time
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bench_common
from lsp_smoke import CODE_ACTION_REQUEST_ID, CODE_ACTION_WAIT_SECONDS, MAXON_EXE, ROOT, await_response, lsp_server, read_message, send, shut_down


SAMPLE_SOURCE = """typealias Byte = int(0 to u8.max)

function main() returns ExitCode
\tlet a = 1 as Byte
\tlet b = a as Byte
\tlet c = b as Byte
\tlet d = c as Byte
\treturn d
end 'main'
"""


def main():
  if not MAXON_EXE.exists():
    print(f"missing {MAXON_EXE}", file=sys.stderr)
    return 1

  with contextlib.ExitStack() as stack:
    tmp_dir = Path(stack.enter_context(bench_common.temp_directory("lsp-codefix")).name)
    return run(tmp_dir, stack)


def run(tmp_dir, stack):
  sample_file = tmp_dir / "main.maxon"
  sample_file.write_text(SAMPLE_SOURCE, encoding="utf-8")
  uri = sample_file.as_uri()

  proc = stack.enter_context(lsp_server(MAXON_EXE, ROOT))

  # Initialize
  send(proc, {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "initialize",
    "params": {
      "processId": os.getpid(),
      "rootUri": tmp_dir.as_uri(),
      "capabilities": {},
    },
  })

  diagnostics = []
  initialized = False
  deadline = time.time() + 30

  while time.time() < deadline:
    msg = read_message(proc, deadline)
    if msg is None:
      break
    method = msg.get("method")
    if msg.get("id") == 1 and "result" in msg:
      send(proc, {"jsonrpc": "2.0", "method": "initialized", "params": {}})
      send(proc, {
        "jsonrpc": "2.0",
        "method": "textDocument/didOpen",
        "params": {
          "textDocument": {
            "uri": uri,
            "languageId": "maxon",
            "version": 1,
            "text": SAMPLE_SOURCE,
          }
        },
      })
      initialized = True
    elif method == "textDocument/publishDiagnostics":
      params = msg.get("params", {})
      diag_uri = params.get("uri")
      diags = params.get("diagnostics", [])
      # The LSP returns its URI with a lowercased Windows drive letter, so
      # match case-insensitively and from now on use the server's exact spelling
      # — the WorkspaceEdit.changes dict in the response is keyed off it.
      if diag_uri and diag_uri.lower() == uri.lower():
        uri = diag_uri
        e3010 = [d for d in diags if d.get("code") == "E3010"]
        if e3010:
          diagnostics = e3010
          break

  if not initialized:
    print("LSP never initialized", file=sys.stderr)
    proc.terminate()
    return 1
  if not diagnostics:
    print("never received E3010 diagnostics", file=sys.stderr)
    proc.terminate()
    return 1

  print(f"received {len(diagnostics)} E3010 diagnostics", flush=True)

  # Send a codeAction request covering the whole document. Editors normally
  # narrow context.diagnostics to the visible range, but for testing we hand
  # over every E3010 so the per-diagnostic actions are all produced.
  send(proc, {
    "jsonrpc": "2.0",
    "id": CODE_ACTION_REQUEST_ID,
    "method": "textDocument/codeAction",
    "params": {
      "textDocument": {"uri": uri},
      "range": {
        "start": {"line": 0, "character": 0},
        "end": {"line": len(SAMPLE_SOURCE.split("\n")), "character": 0},
      },
      "context": {"diagnostics": diagnostics},
    },
  })

  response = await_response(proc, CODE_ACTION_REQUEST_ID, CODE_ACTION_WAIT_SECONDS)
  shut_down(proc)

  if response is None:
    print("no codeAction response", file=sys.stderr)
    return 1

  actions = response.get("result")
  if not actions:
    print("FAIL: empty actions list", file=sys.stderr)
    return 1

  print(f"got {len(actions)} action(s):")
  for a in actions:
    file_edits = a.get("edit", {}).get("changes", {})
    edit_count = sum(len(v) for v in file_edits.values())
    print(f"  - {a.get('title')!r} (kind={a.get('kind')}, edits={edit_count} across {len(file_edits)} file(s))")

  per_diag = [a for a in actions if a.get("title") == "Remove unneeded cast"]
  if len(per_diag) != len(diagnostics):
    print(f"FAIL: expected {len(diagnostics)} per-diagnostic fixes, got {len(per_diag)}", file=sys.stderr)
    return 1

  # Fix-all variants are surfaced under quickfix (with diagnostics attached) so
  # they appear in the lightbulb next to the squiggle, not just under "Source
  # Action..." — that's how popular extensions like ESLint/TypeScript expose
  # the in-file/in-project variants.
  fix_all_file = [a for a in actions if a.get("kind") == "quickfix" and "in file" in a.get("title", "")]
  if len(fix_all_file) != 1:
    print(f"FAIL: expected 1 fix-all-in-file action, got {len(fix_all_file)}", file=sys.stderr)
    return 1
  file_edits = fix_all_file[0].get("edit", {}).get("changes", {}).get(uri, [])
  if len(file_edits) != len(diagnostics):
    print(f"FAIL: fix-all-in-file: expected {len(diagnostics)} edits, got {len(file_edits)}", file=sys.stderr)
    return 1

  # Apply all the edits and verify only the redundant casts are removed.
  # The literal `1 as Byte` is genuinely needed (it does the range check), so
  # the parser doesn't flag it and we shouldn't either — `as Byte` should
  # appear exactly once in the patched source.
  lines = SAMPLE_SOURCE.split("\n")
  for edit in file_edits:
    start = edit["range"]["start"]
    end = edit["range"]["end"]
    line = lines[start["line"]]
    lines[start["line"]] = line[:start["character"]] + line[end["character"]:]
  patched = "\n".join(lines)
  as_remaining = patched.count(" as ")
  if as_remaining != 1:
    print(f"FAIL: expected exactly 1 ' as ' remaining (the literal cast), got {as_remaining}", file=sys.stderr)
    print(patched, file=sys.stderr)
    return 1
  print(f"patched source:\n{patched}")

  # Single-file mode: the project still notionally exists, but it only
  # contains one file. The fix-all-in-project action is offered when the
  # project-wide edit count exceeds the file-only count; in a single-file
  # project they're equal, so the project action should not appear.
  fix_all_project = [a for a in actions if a.get("kind") == "quickfix" and "in project" in a.get("title", "")]
  if fix_all_project:
    print(f"FAIL: single-file test should not see fix-all-in-project, got {len(fix_all_project)}", file=sys.stderr)
    return 1

  print("OK")
  return 0


if __name__ == "__main__":
  sys.exit(main())
