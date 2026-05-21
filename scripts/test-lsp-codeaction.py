"""Smoke test for the LSP E3010 quick fix.

Launches `bin/maxon-lsp.exe lsp-server` (actually `bin/maxon.exe lsp-server` since
maxon-lsp.exe is just a copy), opens a small Maxon document with an unneeded
cast, waits for the E3010 diagnostic, then requests code actions and verifies
the response contains a "Remove unneeded cast" CodeAction with the expected
WorkspaceEdit.

Exits 0 on success, non-zero on any mismatch."""

import json
import os
import subprocess
import sys
import threading
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
MAXON_EXE = ROOT / "bin" / "maxon.exe"

SAMPLE_SOURCE = """typealias Byte = int(0 to u8.max)

function main() returns ExitCode
\tlet a = 1 as Byte
\tlet b = a as Byte
\tlet c = b as Byte
\tlet d = c as Byte
\treturn d
end 'main'
"""


def send(proc, payload):
  body = json.dumps(payload).encode("utf-8")
  header = f"Content-Length: {len(body)}\r\n\r\n".encode("ascii")
  proc.stdin.write(header + body)
  proc.stdin.flush()


def read_message(proc, deadline):
  # Headers are CRLF-terminated and end with a blank line.
  header = b""
  while True:
    if time.time() > deadline:
      return None
    ch = proc.stdout.read(1)
    if not ch:
      return None
    header += ch
    if header.endswith(b"\r\n\r\n"):
      break
  length = 0
  for line in header.decode("ascii").split("\r\n"):
    if line.lower().startswith("content-length:"):
      length = int(line.split(":", 1)[1].strip())
  body = b""
  while len(body) < length:
    chunk = proc.stdout.read(length - len(body))
    if not chunk:
      return None
    body += chunk
  return json.loads(body.decode("utf-8"))


def main():
  if not MAXON_EXE.exists():
    print(f"missing {MAXON_EXE}", file=sys.stderr)
    return 1

  tmp_dir = Path(os.environ.get("TEMP", "/tmp")) / "maxon-lsp-codefix-test"
  tmp_dir.mkdir(parents=True, exist_ok=True)
  sample_file = tmp_dir / "main.maxon"
  sample_file.write_text(SAMPLE_SOURCE, encoding="utf-8")
  uri = sample_file.as_uri()

  proc = subprocess.Popen(
    [str(MAXON_EXE), "lsp-server"],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    cwd=str(ROOT),
  )

  # Drain stderr in background so it doesn't block the pipe.
  def drain_stderr():
    while True:
      line = proc.stderr.readline()
      if not line:
        return

  threading.Thread(target=drain_stderr, daemon=True).start()

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
    "id": 2,
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

  response = None
  deadline = time.time() + 10
  while time.time() < deadline:
    msg = read_message(proc, deadline)
    if msg is None:
      break
    if msg.get("id") == 2:
      response = msg
      break

  send(proc, {"jsonrpc": "2.0", "id": 99, "method": "shutdown"})
  send(proc, {"jsonrpc": "2.0", "method": "exit"})
  try:
    proc.wait(timeout=5)
  except subprocess.TimeoutExpired:
    proc.terminate()

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
